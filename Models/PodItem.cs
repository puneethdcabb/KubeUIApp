using System;
using System.Collections.Generic;

namespace KubeUIApp.Models
{
    public class PodItem
    {
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Status { get; set; } = "Unknown";
        public string StatusColor { get; set; } = "#9E9E9E"; // Hex color for badge
        public int ReadyContainers { get; set; }
        public int TotalContainers { get; set; }
        public string ReadyDisplay => $"{ReadyContainers}/{TotalContainers}";
        public int Restarts { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string AgeDisplay { get; set; } = "-";
        public string PodIP { get; set; } = "-";
        public string HostIP { get; set; } = "-";
        public string NodeName { get; set; } = "-";
        public List<string> Containers { get; set; } = new();
        public Dictionary<string, string> Labels { get; set; } = new();
        public string ControlledBy { get; set; } = string.Empty; // e.g. Deployment/frontend or ReplicaSet/...

        public static string ComputeStatusColor(string status)
        {
            return status?.ToLowerInvariant() switch
            {
                "running" => "#10B981",       // emerald green
                "succeeded" => "#3B82F6",     // blue
                "completed" => "#6B7280",     // slate gray
                "pending" => "#F59E0B",       // amber yellow
                "containercreating" => "#F59E0B",
                "terminating" => "#EF4444",   // red
                "crashloopbackoff" => "#EF4444",
                "error" => "#EF4444",
                "imagepullbackoff" => "#EF4444",
                "errimagepull" => "#EF4444",
                _ => "#8B5CF6"                // purple for other states
            };
        }
    }
}
