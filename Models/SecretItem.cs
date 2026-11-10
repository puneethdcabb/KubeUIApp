using System;
using System.Collections.Generic;

namespace KubeUIApp.Models
{
    public class SecretItem
    {
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Type { get; set; } = "Opaque";
        public int DataCount { get; set; }
        public List<string> Keys { get; set; } = new();
        public string AgeDisplay { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }

        public string KeysDisplay => Keys.Count > 0 ? string.Join(", ", Keys) : "(empty)";
        public string DisplayIcon => Type.Contains("tls", StringComparison.OrdinalIgnoreCase) ? "🔒" : "🔑";
    }

    public class SecretDataEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Base64Value { get; set; } = string.Empty;
        public string DecodedText { get; set; } = string.Empty;
        public int ByteLength { get; set; }
        public string DetectedType { get; set; } = "Base64";
        public bool IsCertificateOrCrl => DetectedType.Contains("Certificate") || DetectedType.Contains("CRL");
    }
}
