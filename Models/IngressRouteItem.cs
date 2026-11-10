using System;

namespace KubeUIApp.Models
{
    public class IngressRouteItem
    {
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Kind { get; set; } = "Route"; // "Route" or "Ingress"
        public string Host { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
        public string ServiceName { get; set; } = string.Empty;
        public string TargetPort { get; set; } = string.Empty;
        public string TlsTermination { get; set; } = "None"; // None, Edge, Passthrough, Reencrypt
        public string Status { get; set; } = "Active";
        public string AgeDisplay { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }

        public bool IsTls => !string.IsNullOrEmpty(TlsTermination) && !TlsTermination.Equals("None", StringComparison.OrdinalIgnoreCase);
        public string Scheme => IsTls ? "https" : "http";
        public string FullUrl => !string.IsNullOrEmpty(Host) ? $"{Scheme}://{Host}{(!string.IsNullOrEmpty(Path) && Path != "/" ? Path : "")}" : string.Empty;
        public string DisplayIcon => Kind == "Route" ? "🌐" : "🚪";
        public string TlsBadgeColor => IsTls ? "#10B981" : "#6B7280";
    }

    public class RouterStatusInfo
    {
        public bool IsRouterRunning { get; set; }
        public string RouterPodsStatus { get; set; } = "Unknown";
        public bool IsHttpListening { get; set; }
        public bool IsHttpsListening { get; set; }
        public string HttpListeningDisplay => IsHttpListening ? "🟢 Active" : "⚪ Not Detected";
        public string HttpsListeningDisplay => IsHttpsListening ? "🟢 Active" : "⚪ Not Detected";
        public string FirewallStatus { get; set; } = "Unknown";
        public string RouterType { get; set; } = "OpenShift Ingress / HAProxy";
        public string NodeDnsAdvice { get; set; } = string.Empty;
        public string SummaryText => IsRouterRunning && (IsHttpListening || IsHttpsListening) 
            ? "✅ Router Active & Listening on Ports 80/443" 
            : "⚠️ Router Inactive or Ports 80/443 Not Detected";
    }
}
