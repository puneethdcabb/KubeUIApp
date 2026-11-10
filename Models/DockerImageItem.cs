using System;

namespace KubeUIApp.Models
{
    public class DockerImageItem
    {
        public string Repository { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string ShortId => Id.Length > 12 ? Id.Substring(0, 12) : Id;
        public string Created { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string FullName => string.IsNullOrEmpty(Tag) || Tag == "<none>" ? Repository : $"{Repository}:{Tag}";
    }
}
