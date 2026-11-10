using System;

namespace KubeUIApp.Models
{
    public class PortForwardItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string PodName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public int ContainerPort { get; set; }
        public int LocalPort { get; set; }
        public bool IsActive { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.Now;
        public string StatusText => IsActive ? "🟢 Tunnel Active" : "⚪ Stopped";
        public string LocalUrl => $"http://localhost:{LocalPort}";
    }
}
