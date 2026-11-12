using System;
using System.Windows.Media;

namespace KubeUIApp.Models
{
    public class OsCertificateItem
    {
        public string Name { get; set; } = string.Empty;
        public string StoreCategory { get; set; } = string.Empty;
        public string Kind { get; set; } = "Certificate"; // Root CA, Intermediate CA, Leaf Cert, CRL
        public string Subject { get; set; } = string.Empty;
        public string CommonName { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Thumbprint { get; set; } = string.Empty;
        public DateTime? NotBefore { get; set; }
        public DateTime? NotAfter { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public int BundleIndex { get; set; } = -1; // -1 for standalone file, >= 0 for entry inside a multi-cert bundle
        public string RawPem { get; set; } = string.Empty;
        public string SignatureAlgorithm { get; set; } = string.Empty;
        public string KeyAlgorithm { get; set; } = string.Empty;
        public string SubjectAlternativeNames { get; set; } = string.Empty;
        public bool IsCa { get; set; }
        public bool IsSelfSigned { get; set; }
        public int RevokedCount { get; set; }

        public string DisplayIcon => Kind switch
        {
            "CRL" => "📋",
            "Root CA" => "🏛️",
            "Intermediate CA" => "🔗",
            _ => "🔒"
        };

        public string ValidityStatus
        {
            get
            {
                if (NotAfter == null)
                    return "Unknown";

                var now = DateTime.UtcNow;
                if (NotBefore.HasValue && now < NotBefore.Value.ToUniversalTime())
                    return "Not Yet Valid";
                if (now > NotAfter.Value.ToUniversalTime())
                    return "Expired";
                if ((NotAfter.Value.ToUniversalTime() - now).TotalDays <= 30)
                    return "Expiring Soon";

                return "Valid";
            }
        }

        public string DaysRemainingDisplay
        {
            get
            {
                if (NotAfter == null)
                    return "-";

                var diff = NotAfter.Value.ToUniversalTime() - DateTime.UtcNow;
                if (diff.TotalDays < 0)
                    return $"{Math.Abs((int)diff.TotalDays)}d ago";
                if (diff.TotalDays < 1)
                    return $"{(int)diff.TotalHours}h left";
                return $"{(int)diff.TotalDays}d left";
            }
        }

        public string NotBeforeDisplay => NotBefore.HasValue ? NotBefore.Value.ToString("yyyy-MM-dd HH:mm") : "-";
        public string NotAfterDisplay => NotAfter.HasValue ? NotAfter.Value.ToString("yyyy-MM-dd HH:mm") : "-";

        public Color StatusColor => ValidityStatus switch
        {
            "Valid" => (Color)ColorConverter.ConvertFromString("#A6E3A1"),         // Green
            "Expiring Soon" => (Color)ColorConverter.ConvertFromString("#F9E2AF"), // Yellow
            "Expired" => (Color)ColorConverter.ConvertFromString("#F38BA8"),       // Red
            "Not Yet Valid" => (Color)ColorConverter.ConvertFromString("#89B4FA"), // Blue
            _ => (Color)ColorConverter.ConvertFromString("#9399B2")                // Gray
        };
    }
}
