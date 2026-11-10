using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KubeUIApp.Services
{
    public static class AppLogger
    {
        private static readonly object _syncLock = new();
        private static readonly string _logFilePath;
        private const long MaxLogFileBytes = 10 * 1024 * 1024; // 10 MB

        public static string LogFilePath => _logFilePath;

        static AppLogger()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                string? processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    string? dir = Path.GetDirectoryName(processPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        exeDir = dir;
                    }
                }
            }
            catch
            {
                // Fallback to AppDomain.CurrentDomain.BaseDirectory
            }

            _logFilePath = Path.Combine(exeDir, "kubeshift-error.log");
        }

        public static void LogInfo(string context, string message)
        {
            WriteEntry("INFO", context, message, null);
        }

        public static void LogWarning(string context, string message)
        {
            WriteEntry("WARN", context, message, null);
        }

        public static void LogError(string context, string message, Exception? ex = null)
        {
            WriteEntry("ERROR", context, message, ex);
        }

        public static void LogCommandError(string command, int exitCode, long durationMs, string stdout, string stderr)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Command failed (ExitCode={exitCode}, Duration={durationMs}ms)");
            sb.AppendLine($"  Command : {command}");
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                sb.AppendLine($"  StdErr  : {stderr.Trim()}");
            }
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                string trimmedOut = stdout.Trim();
                if (trimmedOut.Length > 2000)
                {
                    trimmedOut = trimmedOut.Substring(0, 2000) + "... [truncated]";
                }
                sb.AppendLine($"  StdOut  : {trimmedOut}");
            }
            WriteEntry("CMD_ERROR", "SshService.RunCommandAsync", sb.ToString().TrimEnd(), null);
        }

        private static void WriteEntry(string level, string context, string message, Exception? ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append('[')
                  .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append("] [")
                  .Append(level)
                  .Append("] [")
                  .Append(context)
                  .Append("] ")
                  .AppendLine(message);

                if (ex != null)
                {
                    sb.AppendLine($"  Exception: {ex.GetType().FullName}: {ex.Message}");
                    if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    {
                        sb.AppendLine(ex.StackTrace);
                    }
                    var inner = ex.InnerException;
                    int depth = 1;
                    while (inner != null && depth <= 5)
                    {
                        sb.AppendLine($"  InnerException[{depth}]: {inner.GetType().FullName}: {inner.Message}");
                        if (!string.IsNullOrWhiteSpace(inner.StackTrace))
                        {
                            sb.AppendLine(inner.StackTrace);
                        }
                        inner = inner.InnerException;
                        depth++;
                    }
                }

                lock (_syncLock)
                {
                    RotateIfNeeded();
                    File.AppendAllText(_logFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Never throw from logger
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var fi = new FileInfo(_logFilePath);
                if (fi.Exists && fi.Length > MaxLogFileBytes)
                {
                    string backupPath = _logFilePath + ".old";
                    File.Move(_logFilePath, backupPath, overwrite: true);
                }
            }
            catch
            {
                // Ignore rotation errors
            }
        }
    }
}
