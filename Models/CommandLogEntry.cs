using System;

namespace KubeUIApp.Models
{
    public class CommandLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string TimeString => Timestamp.ToString("HH:mm:ss.fff");
        public string Command { get; set; } = string.Empty;
        public long DurationMs { get; set; }
        public int ExitCode { get; set; }
        public bool IsSuccess => ExitCode == 0;
        public string StatusDisplay => IsSuccess ? "SUCCESS" : $"FAILED ({ExitCode})";
        public string StatusColor => IsSuccess ? "#10B981" : "#EF4444";
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
