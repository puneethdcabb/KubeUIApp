using System;

namespace KubeUIApp.Models
{
    public class ContainerItem
    {
        public string Id { get; set; } = string.Empty;
        public string ShortId => Id.Length > 12 ? Id.Substring(0, 12) : Id;
        public string Name { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
        public string State { get; set; } = "unknown";
        public string Status { get; set; } = string.Empty;
        public string Created { get; set; } = string.Empty;
        public string Ports { get; set; } = string.Empty;

        public string StateColor => State.ToLowerInvariant() switch
        {
            "running" => "#10B981",
            "exited" => "#6B7280",
            "paused" => "#F59E0B",
            "dead" => "#EF4444",
            "created" => "#3B82F6",
            _ => "#9E9E9E"
        };
    }
}
