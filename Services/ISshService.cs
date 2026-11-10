using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KubeUIApp.Models;

namespace KubeUIApp.Services
{
    public class CommandResult
    {
        public string Command { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public long DurationMs { get; set; }
        public bool IsSuccess => ExitCode == 0;
    }

    public interface ISshService : IDisposable
    {
        bool IsConnected { get; }
        ConnectionProfile? CurrentProfile { get; }
        event Action<CommandLogEntry>? CommandExecuted;
        event Action<bool, string>? ConnectionStatusChanged;

        Task<(bool Success, string Message)> ConnectAsync(ConnectionProfile profile);
        void Disconnect();
        Task<CommandResult> RunCommandAsync(string command, int timeoutSeconds = 60, bool asSudo = false);
        Task<List<RemoteFileInfo>> ListDirectoryAsync(string path);
        Task<string> ReadTextFileAsync(string path, int maxBytes = 2 * 1024 * 1024);
        Task DownloadFileAsync(string remotePath, string localPath);
        Task UploadFileAsync(string localPath, string remotePath);
        IDisposable StartPortForward(string remoteHost, uint remotePort, uint localPort);
    }
}
