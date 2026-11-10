using System;
using System.Collections.Generic;

namespace KubeUIApp.Models
{
    public class DeploymentItem
    {
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public int Replicas { get; set; }
        public int ReadyReplicas { get; set; }
        public int AvailableReplicas { get; set; }
        public int UpToDateReplicas { get; set; }
        public string ReadyDisplay => $"{ReadyReplicas}/{Replicas}";
        public string Images { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public string AgeDisplay { get; set; } = "-";
        public string Selector { get; set; } = string.Empty;
        public string StatusColor => ReadyReplicas == Replicas && Replicas > 0 ? "#10B981" : (ReadyReplicas > 0 ? "#F59E0B" : "#EF4444");
    }
}
