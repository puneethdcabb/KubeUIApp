using System;

namespace KubeUIApp.Models
{
    public class NodeStats
    {
        public string Hostname { get; set; } = "-";
        public string OsName { get; set; } = "-";
        public string Kernel { get; set; } = "-";
        public string Uptime { get; set; } = "-";
        public string CpuUsage { get; set; } = "-";
        public long MemoryTotalMb { get; set; }
        public long MemoryUsedMb { get; set; }
        public double MemoryUsagePercent => MemoryTotalMb > 0 ? (double)MemoryUsedMb / MemoryTotalMb * 100.0 : 0;
        public string MemoryDisplay => MemoryTotalMb > 0 ? $"{MemoryUsedMb} MB / {MemoryTotalMb} MB ({MemoryUsagePercent:F0}%)" : "-";
        public string DiskUsage { get; set; } = "-";
        public string KubernetesVersion { get; set; } = "-";
        public string CliTool { get; set; } = "-";
        public string ContainerRuntime { get; set; } = "-";
    }
}
