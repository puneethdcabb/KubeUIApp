using System.Collections.Generic;
using System.Threading.Tasks;
using KubeUIApp.Models;

namespace KubeUIApp.Services
{
    public class PvcMountInfo
    {
        public bool IsMounted { get; set; }
        public string PodName { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string MountPath { get; set; } = string.Empty;
        public string SubPath { get; set; } = string.Empty;
        public string VolumeName { get; set; } = string.Empty;
        public string HostStoragePath { get; set; } = string.Empty;
        public string SourceDescription => IsMounted 
            ? $"Mounted via Pod: {PodName} ({MountPath})" 
            : (!string.IsNullOrEmpty(HostStoragePath) ? $"Host Storage: {HostStoragePath}" : "Unmounted Volume");
    }

    public interface IClusterService
    {
        string DetectedCli { get; }
        string DetectedContainerRuntime { get; }
        string KubeconfigPath { get; }

        Task InitializeAsync();

        Task<NodeStats> GetNodeStatsAsync();
        Task<List<string>> GetNamespacesAsync();

        // Pods
        Task<List<PodItem>> GetPodsAsync(string? namespaceName = null);
        Task<string> GetPodLogsAsync(string podName, string ns, string? containerName = null, int lines = 300, bool previous = false);
        Task<string> GetPodYamlAsync(string podName, string ns);
        Task<(string ResourceTitle, string Yaml)> GetPodEditableYamlAsync(PodItem pod);
        Task<string> DescribePodAsync(string podName, string ns);
        Task<bool> DeletePodAsync(string podName, string ns, bool force = false);
        Task<bool> RestartPodAsync(PodItem pod);
        Task<bool> RedeployPodAsync(PodItem pod);
        Task<bool> StopPodAsync(PodItem pod);
        Task<string> ExecInPodAsync(string podName, string ns, string containerName, string command);
        Task<List<RemoteFileInfo>> ListPodFilesDetailedAsync(string podName, string ns, string containerName, string path);
        Task<string> ReadPodFileAsync(string podName, string ns, string containerName, string path);
        Task CopyPodFileToWindowsAsync(string podName, string ns, string containerName, string remoteFilePath, string localWindowsPath);
        Task CopyWindowsFileToPodAsync(string podName, string ns, string containerName, string localWindowsPath, string remoteDestinationPath);
        Task<List<string>> GetPodAttachedPvcsAsync(string podName, string ns);

        // Deployments
        Task<List<DeploymentItem>> GetDeploymentsAsync(string? namespaceName = null);
        Task<string> GetDeploymentYamlAsync(string name, string ns);
        Task<string> DescribeDeploymentAsync(string name, string ns);
        Task<bool> ScaleDeploymentAsync(string name, string ns, int replicas);
        Task<bool> RestartDeploymentAsync(string name, string ns);
        Task<bool> DeleteDeploymentAsync(string name, string ns);
        Task<(bool Success, string Message)> ApplyYamlAsync(string yaml, string? namespaceName = null);

        // PVC & PVC File Browsing & Copying
        Task<List<PvcItem>> GetPvcsAsync(string? namespaceName = null);
        Task<string> GetPvcYamlAsync(string name, string ns);
        Task<string> DescribePvcAsync(string name, string ns);
        Task<bool> DeletePvcAsync(string name, string ns);
        Task<PvcMountInfo> FindPvcMountAsync(string pvcName, string ns);
        Task<(List<RemoteFileInfo> Files, string CurrentPath, PvcMountInfo MountInfo)> ListPvcFilesAsync(string pvcName, string ns, string? subPath = null);
        Task<string> ReadPvcFileAsync(string pvcName, string ns, string filePath);
        Task CopyPvcFileToWindowsAsync(string pvcName, string ns, string remoteFilePath, string localWindowsPath);
        Task CopyWindowsFileToPvcAsync(string pvcName, string ns, string localWindowsPath, string remoteDestinationPath);

        // Secrets & Certificates / CRL Inspection
        Task<List<SecretItem>> GetSecretsAsync(string? namespaceName = null);
        Task<string> GetSecretYamlAsync(string name, string ns);
        Task<string> DescribeSecretAsync(string name, string ns);
        Task<List<SecretDataEntry>> GetSecretDataEntriesAsync(string name, string ns);
        Task<bool> DeleteSecretAsync(string name, string ns);

        // Ingress & OpenShift Routes
        Task<List<IngressRouteItem>> GetIngressRoutesAsync(string? namespaceName = null);
        Task<string> GetIngressRouteYamlAsync(string name, string ns, string kind);
        Task<string> DescribeIngressRouteAsync(string name, string ns, string kind);
        Task<bool> DeleteIngressRouteAsync(string name, string ns, string kind);
        Task<RouterStatusInfo> GetRouterStatusAsync();

        // Container runtime & Docker images
        Task<List<ContainerItem>> GetContainersAsync(bool all = true);
        Task<string> GetContainerLogsAsync(string containerId, int lines = 200);
        Task<bool> StartContainerAsync(string containerId);
        Task<bool> StopContainerAsync(string containerId);
        Task<bool> RestartContainerAsync(string containerId);
        Task<bool> RemoveContainerAsync(string containerId, bool force = true);
        Task<List<DockerImageItem>> GetImagesAsync();
        Task<bool> DeleteImageAsync(string imageIdOrName, bool force = false);
        Task<bool> PullImageAsync(string imageName);
    }
}
