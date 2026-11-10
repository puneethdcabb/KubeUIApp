using System;

namespace KubeUIApp.Models
{
    public class PvcItem
    {
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Status { get; set; } = "Unknown";
        public string VolumeName { get; set; } = "-";
        public string Capacity { get; set; } = "-";
        public string AccessModes { get; set; } = "-";
        public string StorageClass { get; set; } = "-";
        public DateTime? CreatedAt { get; set; }
        public string AgeDisplay { get; set; } = "-";

        public string StatusColor => Status.ToLowerInvariant() switch
        {
            "bound" => "#10B981",
            "pending" => "#F59E0B",
            "lost" => "#EF4444",
            _ => "#9E9E9E"
        };
    }
}
