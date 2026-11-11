using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KubeUIApp.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace KubeUIApp.Services
{
    public class SshService : ISshService
    {
        private SshClient? _sshClient;
        private SftpClient? _sftpClient;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly SemaphoreSlim _commandThrottle = new(6, 6);

        public bool IsConnected => _sshClient != null && _sshClient.IsConnected;
        public ConnectionProfile? CurrentProfile { get; private set; }

        public event Action<CommandLogEntry>? CommandExecuted;
        public event Action<bool, string>? ConnectionStatusChanged;

        private static Renci.SshNet.ConnectionInfo CreateConnectionInfo(ConnectionProfile profile)
        {
            var authMethods = new List<AuthenticationMethod>();
            if (!string.IsNullOrEmpty(profile.SshKeyPath) && File.Exists(profile.SshKeyPath))
            {
                var keyFile = new PrivateKeyFile(profile.SshKeyPath, string.IsNullOrEmpty(profile.Password) ? null : profile.Password);
                authMethods.Add(new PrivateKeyAuthenticationMethod(profile.Username, keyFile));
            }

            string pwd = profile.Password ?? string.Empty;
            authMethods.Add(new PasswordAuthenticationMethod(profile.Username, pwd));

            var kbdInteractive = new KeyboardInteractiveAuthenticationMethod(profile.Username);
            kbdInteractive.AuthenticationPrompt += (sender, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = pwd;
                }
            };
            authMethods.Add(kbdInteractive);

            return new Renci.SshNet.ConnectionInfo(
                profile.Host,
                profile.Port,
                profile.Username,
                authMethods.ToArray()
            )
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public async Task<(bool Success, string Message)> ConnectAsync(ConnectionProfile profile)
        {
            await _lock.WaitAsync();
            try
            {
                DisconnectInternal();
                CurrentProfile = profile;

                var sshConnInfo = CreateConnectionInfo(profile);
                _sshClient = new SshClient(sshConnInfo)
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(30)
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await _sshClient.ConnectAsync(cts.Token);

                // Connect SFTP sequentially with its own dedicated ConnectionInfo & AuthenticationMethod instances
                try
                {
                    var sftpConnInfo = CreateConnectionInfo(profile);
                    _sftpClient = new SftpClient(sftpConnInfo)
                    {
                        KeepAliveInterval = TimeSpan.FromSeconds(30)
                    };
                    using var sftpCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await _sftpClient.ConnectAsync(sftpCts.Token);
                }
                catch (Exception sftpEx)
                {
                    // SFTP connection is optional (all file operations have SSH command fallbacks)
                    AppLogger.LogInfo("SshService.ConnectAsync", $"SFTP optional connection skipped: {sftpEx.Message}");
                }

                AppLogger.LogInfo("SshService.ConnectAsync", $"Connected to {profile.Host}:{profile.Port} as '{profile.Username}' (UseSudo={profile.UseSudo}).");
                ConnectionStatusChanged?.Invoke(true, $"Connected to {profile.Host}:{profile.Port}");
                return (true, "Successfully connected");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SshService.ConnectAsync", $"Failed to connect to {profile.Host}:{profile.Port} as '{profile.Username}'.", ex);
                DisconnectInternal();
                ConnectionStatusChanged?.Invoke(false, $"Connection failed: {ex.Message}");
                return (false, ex.Message);
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Disconnect()
        {
            _lock.Wait();
            try
            {
                DisconnectInternal();
            }
            finally
            {
                _lock.Release();
            }
        }

        private void DisconnectInternal()
        {
            try
            {
                if (_sftpClient != null)
                {
                    if (_sftpClient.IsConnected) _sftpClient.Disconnect();
                    _sftpClient.Dispose();
                    _sftpClient = null;
                }

                if (_sshClient != null)
                {
                    try
                    {
                        foreach (var fp in _sshClient.ForwardedPorts)
                        {
                            try { if (fp.IsStarted) fp.Stop(); } catch { }
                        }
                    }
                    catch { }

                    if (_sshClient.IsConnected) _sshClient.Disconnect();
                    _sshClient.Dispose();
                    _sshClient = null;
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
            finally
            {
                ConnectionStatusChanged?.Invoke(false, "Disconnected");
            }
        }

        public async Task<CommandResult> RunCommandAsync(string command, int timeoutSeconds = 60, bool asSudo = false)
        {
            if (_sshClient == null || !_sshClient.IsConnected)
            {
                return new CommandResult
                {
                    Command = command,
                    ExitCode = -1,
                    Error = "SSH client is not connected"
                };
            }

            string actualCommand;
            string sudoPass = !string.IsNullOrEmpty(CurrentProfile?.SudoPassword)
                ? CurrentProfile.SudoPassword
                : (CurrentProfile?.Password ?? string.Empty);
            bool isRootUser = string.Equals(CurrentProfile?.Username?.Trim(), "root", StringComparison.OrdinalIgnoreCase);
            bool shouldUseSudo = !isRootUser && ((CurrentProfile?.UseSudo ?? true) || asSudo);

            // Escape single quotes for bash -c wrapper
            string escapedCmd = command.Replace("'", "'\\''");

            if (!shouldUseSudo)
            {
                // Logged in as root or sudo disabled: Execute inside bash subshell directly
                actualCommand = $"bash -c '{escapedCmd}'";
            }
            else
            {
                // Non-root user with sudo: Execute non-interactively using password via stdin (-S) or passwordless (-n)
                if (!string.IsNullOrEmpty(sudoPass))
                {
                    string escapedPass = sudoPass.Replace("'", "'\\''");
                    actualCommand = $"printf '%s\\n' '{escapedPass}' | sudo -S -p '' bash -c '{escapedCmd}'";
                }
                else
                {
                    actualCommand = $"sudo -n bash -c '{escapedCmd}'";
                }
            }

            await _commandThrottle.WaitAsync();
            var sw = Stopwatch.StartNew();
            try
            {
                if (_sshClient == null || !_sshClient.IsConnected)
                {
                    return new CommandResult
                    {
                        Command = command,
                        ExitCode = -1,
                        Error = "SSH client is not connected"
                    };
                }

                using var cmd = _sshClient.CreateCommand(actualCommand);
                cmd.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                await cmd.ExecuteAsync(cts.Token);
                sw.Stop();

                string stdout = cmd.Result ?? string.Empty;
                string stderr = cmd.Error ?? string.Empty;
                int exitCode = cmd.ExitStatus ?? 0;

                var result = new CommandResult
                {
                    Command = command,
                    ExitCode = exitCode,
                    Output = stdout,
                    Error = stderr,
                    DurationMs = sw.ElapsedMilliseconds
                };

                if (exitCode != 0)
                {
                    AppLogger.LogCommandError(command, exitCode, sw.ElapsedMilliseconds, stdout, stderr);
                }

                CommandExecuted?.Invoke(new CommandLogEntry
                {
                    Timestamp = DateTime.Now,
                    Command = command,
                    DurationMs = sw.ElapsedMilliseconds,
                    ExitCode = exitCode,
                    Output = stdout,
                    Error = stderr
                });

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.LogError("SshService.RunCommandAsync", $"Exception executing command: {command}", ex);
                var result = new CommandResult
                {
                    Command = command,
                    ExitCode = -1,
                    Error = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                };

                CommandExecuted?.Invoke(new CommandLogEntry
                {
                    Timestamp = DateTime.Now,
                    Command = command,
                    DurationMs = sw.ElapsedMilliseconds,
                    ExitCode = -1,
                    Error = ex.Message
                });

                return result;
            }
            finally
            {
                _commandThrottle.Release();
            }
        }

        public async Task<List<RemoteFileInfo>> ListDirectoryAsync(string path)
        {
            var list = new List<RemoteFileInfo>();

            // Try SFTP first
            if (_sftpClient != null && _sftpClient.IsConnected)
            {
                await _lock.WaitAsync();
                try
                {
                    var sftpList = await Task.Run(() =>
                    {
                        var res = new List<RemoteFileInfo>();
                        var files = _sftpClient.ListDirectory(path);
                        foreach (var f in files)
                        {
                            if (f.Name == "." || f.Name == "..") continue;
                            res.Add(new RemoteFileInfo
                            {
                                Name = f.Name,
                                FullPath = f.FullName,
                                IsDirectory = f.IsDirectory,
                                SizeBytes = f.Length,
                                LastModified = f.LastWriteTime,
                                Permissions = f.OwnerCanRead ? "r" : "-"
                            });
                        }
                        res.Sort((a, b) =>
                        {
                            if (a.IsDirectory != b.IsDirectory)
                                return b.IsDirectory.CompareTo(a.IsDirectory);
                            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        });
                        return res;
                    });
                    if (sftpList.Count > 0) return sftpList;
                }
                catch
                {
                    // Fallback to sudo ls if SFTP has permission issues
                }
                finally
                {
                    _lock.Release();
                }
            }

            // Fallback to command line `ls -la` executed with full sudo/root privileges (GNU & BusyBox compatible)
            string escapedPath = path.Replace("'", "'\\''");
            var cmdRes = await RunCommandAsync($"ls -la -N --time-style=long-iso '{escapedPath}' 2>/dev/null || ls -la --time-style=long-iso '{escapedPath}' 2>/dev/null || ls -la -N --time-style=iso '{escapedPath}' 2>/dev/null || ls -la --time-style=iso '{escapedPath}' 2>/dev/null || ls -la '{escapedPath}'");
            if (cmdRes.IsSuccess)
            {
                var lines = cmdRes.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var rawLine in lines)
                {
                    string line = rawLine.TrimStart();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("total ", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var tokenRanges = new List<(int Start, int Length)>();
                    int i = 0;
                    while (i < line.Length && tokenRanges.Count < 10)
                    {
                        while (i < line.Length && line[i] == ' ') i++;
                        if (i >= line.Length) break;
                        int start = i;
                        while (i < line.Length && line[i] != ' ') i++;
                        tokenRanges.Add((start, i - start));
                    }

                    if (tokenRanges.Count < 8)
                        continue;

                    string GetToken(int idx) => line.Substring(tokenRanges[idx].Start, tokenRanges[idx].Length);

                    string perms = GetToken(0);
                    string token5 = GetToken(5);
                    string token6 = GetToken(6);
                    bool isIsoDate = token5.Length == 10 && token5[4] == '-' && token5[7] == '-';
                    bool isShortIsoDate = !isIsoDate && token5.Length == 5 && token5[2] == '-' && token6.Length >= 5 && token6[2] == ':';
                    int nameStartIdx = (isIsoDate || isShortIsoDate) ? 7 : (tokenRanges.Count >= 9 ? 8 : 7);
                    if (tokenRanges.Count <= nameStartIdx)
                        continue;

                    string name = line.Substring(tokenRanges[nameStartIdx].Start);
                    int arrowIdx = name.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrowIdx > 0) name = name.Substring(0, arrowIdx);
                    if (name.EndsWith('\'') || name.EndsWith('"'))
                    {
                        name = name.Substring(0, name.Length - 1);
                    }
                    if (name.StartsWith('\'') || name.StartsWith('"'))
                    {
                        name = name.Substring(1);
                    }

                    if (name == "." || name == "..") continue;
                    bool isDir = perms.StartsWith("d") || perms.StartsWith("l");
                    long.TryParse(GetToken(4), out long size);
                    DateTime dt = default;
                    if (isIsoDate)
                    {
                        DateTime.TryParse(token5 + " " + token6, out dt);
                    }
                    else if (isShortIsoDate)
                    {
                        DateTime.TryParse($"{DateTime.Now.Year}-{token5} {token6}", out dt);
                    }
                    else if (nameStartIdx == 8)
                    {
                        DateTime.TryParse($"{token5} {token6} {GetToken(7)}", out dt);
                    }

                    list.Add(new RemoteFileInfo
                    {
                        Name = name,
                        FullPath = path.TrimEnd('/') + "/" + name,
                        IsDirectory = isDir,
                        SizeBytes = size,
                        LastModified = dt != default ? dt : DateTime.Now,
                        Permissions = perms
                    });
                }
                list.Sort((a, b) =>
                {
                    if (a.IsDirectory != b.IsDirectory)
                        return b.IsDirectory.CompareTo(a.IsDirectory);
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }

            return list;
        }

        private static string SanitizeRemotePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string s = segments[i];
                if (s.Length >= 2 && ((s[0] == '\'' && s[^1] == '\'') || (s[0] == '"' && s[^1] == '"')))
                {
                    segments[i] = s.Substring(1, s.Length - 2);
                }
            }
            return string.Join("/", segments);
        }

        public async Task<string> ReadTextFileAsync(string path, int maxBytes = 2 * 1024 * 1024)
        {
            path = SanitizeRemotePath(path);
            if (_sftpClient != null && _sftpClient.IsConnected && !path.StartsWith("/var/lib/kubelet/", StringComparison.Ordinal))
            {
                await _lock.WaitAsync();
                try
                {
                    return await Task.Run(() =>
                    {
                        using var stream = _sftpClient.OpenRead(path);
                        using var ms = new MemoryStream();
                        byte[] buffer = new byte[8192];
                        int read;
                        long totalRead = 0;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0 && totalRead < maxBytes)
                        {
                            ms.Write(buffer, 0, read);
                            totalRead += read;
                        }
                        return Encoding.UTF8.GetString(ms.ToArray());
                    });
                }
                catch
                {
                    // Fallback to root execution if SFTP permission denied
                }
                finally
                {
                    _lock.Release();
                }
            }

            // Always fallback to command execution with admin privileges
            string escapedPath = path.Replace("'", "'\\''");
            var cmdRes = await RunCommandAsync($"head -c {maxBytes} -- '{escapedPath}' 2>/dev/null || head -c {maxBytes} '{escapedPath}'");
            return cmdRes.Output;
        }

        public async Task DownloadFileAsync(string remotePath, string localPath)
        {
            remotePath = SanitizeRemotePath(remotePath);
            if (_sftpClient != null && _sftpClient.IsConnected && !remotePath.StartsWith("/var/lib/kubelet/", StringComparison.Ordinal))
            {
                await _lock.WaitAsync();
                try
                {
                    await Task.Run(() =>
                    {
                        using var ms = new MemoryStream();
                        _sftpClient.DownloadFile(remotePath, ms);
                        File.WriteAllBytes(localPath, ms.ToArray());
                    });
                    return;
                }
                catch
                {
                    // Fallback to base64 if SFTP denied
                }
                finally
                {
                    _lock.Release();
                }
            }

            // Fallback via root base64
            string escapedPath = remotePath.Replace("'", "'\\''");
            var cmdRes = await RunCommandAsync($"base64 -- '{escapedPath}' 2>/dev/null || base64 '{escapedPath}'", timeoutSeconds: 120);
            if (cmdRes.IsSuccess && !string.IsNullOrWhiteSpace(cmdRes.Output))
            {
                byte[] bytes = Convert.FromBase64String(cmdRes.Output.Trim());
                await File.WriteAllBytesAsync(localPath, bytes);
            }
            else
            {
                throw new Exception($"Failed to download file: {cmdRes.Error}");
            }
        }

        public async Task UploadFileAsync(string localPath, string remotePath)
        {
            remotePath = SanitizeRemotePath(remotePath);
            if (_sftpClient != null && _sftpClient.IsConnected && !remotePath.StartsWith("/var/lib/kubelet/", StringComparison.Ordinal))
            {
                await _lock.WaitAsync();
                try
                {
                    await Task.Run(() =>
                    {
                        using var fs = File.OpenRead(localPath);
                        _sftpClient.UploadFile(fs, remotePath);
                    });
                    return;
                }
                catch
                {
                    // Fallback if SFTP permission denied on target folder
                }
                finally
                {
                    _lock.Release();
                }
            }

            // Fallback via base64 upload with admin permissions
            byte[] localBytes = await File.ReadAllBytesAsync(localPath);
            string b64 = Convert.ToBase64String(localBytes);
            string escapedPath = remotePath.Replace("'", "'\\''");
            var cmdRes = await RunCommandAsync($"printf '%s' '{b64}' | base64 -d > '{escapedPath}'", timeoutSeconds: 120);
            if (!cmdRes.IsSuccess)
            {
                throw new Exception($"Upload failed: {cmdRes.Error}");
            }
        }

        public async Task DeleteFileAsync(string remotePath)
        {
            remotePath = SanitizeRemotePath(remotePath);
            if (_sftpClient != null && _sftpClient.IsConnected && !remotePath.StartsWith("/var/lib/kubelet/", StringComparison.Ordinal))
            {
                await _lock.WaitAsync();
                try
                {
                    await Task.Run(() => _sftpClient.DeleteFile(remotePath));
                    return;
                }
                catch
                {
                    // Fallback to rm with admin permissions
                }
                finally
                {
                    _lock.Release();
                }
            }

            string escapedPath = remotePath.Replace("'", "'\\''");
            var cmdRes = await RunCommandAsync($"rm -f -- '{escapedPath}'");
            if (!cmdRes.IsSuccess)
            {
                throw new Exception($"Delete failed: {cmdRes.Error}");
            }
        }

        public IDisposable StartPortForward(string remoteHost, uint remotePort, uint localPort)
        {
            if (_sshClient == null || !_sshClient.IsConnected)
                throw new InvalidOperationException("SSH client is not connected to remote host.");

            var port = new ForwardedPortLocal("127.0.0.1", localPort, remoteHost, remotePort);
            _sshClient.AddForwardedPort(port);
            port.Start();

            return new PortForwardTunnel(port, _sshClient);
        }

        private class PortForwardTunnel : IDisposable
        {
            private readonly ForwardedPortLocal _port;
            private readonly SshClient _client;
            private bool _disposed;

            public PortForwardTunnel(ForwardedPortLocal port, SshClient client)
            {
                _port = port;
                _client = client;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    try
                    {
                        if (_port.IsStarted) _port.Stop();
                        _client.RemoveForwardedPort(_port);
                        _port.Dispose();
                    }
                    catch { }
                }
            }
        }

        public void Dispose()
        {
            DisconnectInternal();
            _lock.Dispose();
            _commandThrottle.Dispose();
        }
    }
}
