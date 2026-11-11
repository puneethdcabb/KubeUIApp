using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using KubeUIApp.Models;

namespace KubeUIApp.Services
{
    public class ClusterService : IClusterService
    {
        private readonly ISshService _ssh;
        private string _cli = "kubectl";
        private string _containerRuntime = "podman";
        private string _kubeconfig = "";

        public string DetectedCli => _cli;
        public string DetectedContainerRuntime => _containerRuntime;
        public string KubeconfigPath => _kubeconfig;

        public ClusterService(ISshService ssh)
        {
            _ssh = ssh;
        }

        public async Task InitializeAsync()
        {
            var profile = _ssh.CurrentProfile;

            string probeScript =
                "if [ -f /var/lib/microshift/resources/kubeadmin/kubeconfig ]; then echo 'KCFG:/var/lib/microshift/resources/kubeadmin/kubeconfig'; " +
                "elif [ -f /etc/kubernetes/admin.conf ]; then echo 'KCFG:/etc/kubernetes/admin.conf'; " +
                "elif [ -f ~/.kube/config ]; then echo 'KCFG:~/.kube/config'; else echo 'KCFG:'; fi; " +
                "if command -v oc >/dev/null 2>&1; then echo 'CLI:oc'; elif command -v kubectl >/dev/null 2>&1; then echo 'CLI:kubectl'; else echo 'CLI:kubectl'; fi; " +
                "if command -v podman >/dev/null 2>&1; then echo 'RT:podman'; elif command -v crictl >/dev/null 2>&1; then echo 'RT:crictl'; elif command -v docker >/dev/null 2>&1; then echo 'RT:docker'; else echo 'RT:podman'; fi";

            var probeRes = await _ssh.RunCommandAsync(probeScript);
            string detectedKcfg = "";
            string detectedCli = "kubectl";
            string detectedRt = "podman";

            if (probeRes.IsSuccess && !string.IsNullOrWhiteSpace(probeRes.Output))
            {
                var lines = probeRes.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("KCFG:", StringComparison.Ordinal))
                        detectedKcfg = line.Substring(5).Trim();
                    else if (line.StartsWith("CLI:", StringComparison.Ordinal))
                        detectedCli = line.Substring(4).Trim();
                    else if (line.StartsWith("RT:", StringComparison.Ordinal))
                        detectedRt = line.Substring(3).Trim();
                }
            }

            _kubeconfig = !string.IsNullOrWhiteSpace(profile?.CustomKubeconfig)
                ? profile.CustomKubeconfig.Trim()
                : detectedKcfg;

            _cli = (profile != null && profile.PreferredCli != "Auto" && !string.IsNullOrEmpty(profile.PreferredCli))
                ? profile.PreferredCli
                : detectedCli;

            _containerRuntime = (profile != null && profile.PreferredContainerRuntime != "Auto" && !string.IsNullOrEmpty(profile.PreferredContainerRuntime))
                ? profile.PreferredContainerRuntime
                : detectedRt;
        }

        private string BuildKubeCmd(string subCommand)
        {
            string prefix = "";
            if (!string.IsNullOrEmpty(_kubeconfig))
            {
                prefix = $"export KUBECONFIG='{_kubeconfig}'; ";
            }
            return $"{prefix}{_cli} {subCommand}";
        }

        private async Task<CommandResult> RunKubeRawAsync(string fullCommand)
        {
            string prefix = "";
            if (!string.IsNullOrEmpty(_kubeconfig))
            {
                prefix = $"export KUBECONFIG='{_kubeconfig}'; ";
            }
            return await _ssh.RunCommandAsync($"{prefix}{fullCommand}");
        }

        public async Task<NodeStats> GetNodeStatsAsync()
        {
            var stats = new NodeStats
            {
                CliTool = _cli,
                ContainerRuntime = _containerRuntime
            };

            string kcfgPrefix = !string.IsNullOrEmpty(_kubeconfig) ? $"export KUBECONFIG='{_kubeconfig}'; " : "";
            string batchCmd =
                $"{kcfgPrefix}" +
                "echo \"HOST:$(hostname 2>/dev/null)\"; " +
                "echo \"KERNEL:$(uname -r 2>/dev/null)\"; " +
                "echo \"OS:$(grep PRETTY_NAME /etc/os-release 2>/dev/null | cut -d= -f2 | tr -d '\"')\"; " +
                "echo \"UP:$(uptime -p 2>/dev/null || uptime 2>/dev/null)\"; " +
                "echo \"MEM:$(free -m 2>/dev/null | grep -i mem)\"; " +
                "echo \"LOAD:$(cat /proc/loadavg 2>/dev/null)\"; " +
                "echo \"DISK:$(df -h / 2>/dev/null | tail -n 1)\"; " +
                $"echo \"KVER:$({_cli} version --short 2>/dev/null | tr '\\n' ' ' || {_cli} version --client 2>/dev/null | head -n 2 | tr '\\n' ' ')\"";

            var res = await _ssh.RunCommandAsync(batchCmd);
            if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
            {
                var lines = res.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("HOST:", StringComparison.Ordinal))
                    {
                        var val = line.Substring(5).Trim();
                        if (!string.IsNullOrEmpty(val)) stats.Hostname = val;
                    }
                    else if (line.StartsWith("KERNEL:", StringComparison.Ordinal))
                    {
                        var val = line.Substring(7).Trim();
                        if (!string.IsNullOrEmpty(val)) stats.Kernel = val;
                    }
                    else if (line.StartsWith("OS:", StringComparison.Ordinal))
                    {
                        var val = line.Substring(3).Trim();
                        if (!string.IsNullOrEmpty(val)) stats.OsName = val;
                    }
                    else if (line.StartsWith("UP:", StringComparison.Ordinal))
                    {
                        var val = line.Substring(3).Trim();
                        if (!string.IsNullOrEmpty(val)) stats.Uptime = val;
                    }
                    else if (line.StartsWith("MEM:", StringComparison.Ordinal))
                    {
                        var val = line.Substring(4).Trim();
                        var parts = val.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3 && long.TryParse(parts[1], out long total) && long.TryParse(parts[2], out long used))
                        {
                            stats.MemoryTotalMb = total;
                            stats.MemoryUsedMb = used;
                        }
                    }
                    else if (line.StartsWith("LOAD:", StringComparison.Ordinal))
                    {
                        var val = line.Substring(5).Trim();
                        var parts = val.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            stats.CpuUsage = $"Load: {parts[0]}, {parts[1]}, {parts[2]}";
                        }
                    }
                    else if (line.StartsWith("DISK:", StringComparison.Ordinal))
                    {
                        var val = line.Substring(5).Trim();
                        var parts = val.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            stats.DiskUsage = $"{parts[2]} / {parts[1]} ({parts[4]})";
                        }
                    }
                    else if (line.StartsWith("KVER:", StringComparison.Ordinal))
                    {
                        var val = line.Substring(5).Trim();
                        if (!string.IsNullOrEmpty(val)) stats.KubernetesVersion = val;
                    }
                }
            }

            return stats;
        }

        public async Task<List<string>> GetNamespacesAsync()
        {
            var namespaces = new List<string> { "All Namespaces" };
            var res = await _ssh.RunCommandAsync(BuildKubeCmd("get namespaces -o jsonpath='{.items[*].metadata.name}'"));
            if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
            {
                var items = res.Output.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        namespaces.Add(item.Trim());
                }
            }
            return namespaces;
        }

        public async Task<List<PodItem>> GetPodsAsync(string? namespaceName = null)
        {
            var list = new List<PodItem>();
            string nsFlag = string.IsNullOrWhiteSpace(namespaceName) || namespaceName == "All Namespaces"
                ? "-A"
                : $"-n '{namespaceName}'";

            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get pods {nsFlag} -o json"));
            if (!res.IsSuccess || string.IsNullOrWhiteSpace(res.Output))
            {
                return list;
            }

            try
            {
                using var doc = JsonDocument.Parse(res.Output);
                var root = doc.RootElement;
                if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    return list;
                }

                foreach (var podElem in items.EnumerateArray())
                {
                    var pod = new PodItem();

                    if (podElem.TryGetProperty("metadata", out var meta))
                    {
                        if (meta.TryGetProperty("name", out var nameProp))
                            pod.Name = nameProp.GetString() ?? "";
                        if (meta.TryGetProperty("namespace", out var nsProp))
                            pod.Namespace = nsProp.GetString() ?? "";
                        if (meta.TryGetProperty("creationTimestamp", out var ctProp) && ctProp.TryGetDateTime(out var dt))
                        {
                            pod.CreatedAt = dt;
                            pod.AgeDisplay = FormatAge(dt);
                        }

                        if (meta.TryGetProperty("ownerReferences", out var owners) && owners.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var owner in owners.EnumerateArray())
                            {
                                if (owner.TryGetProperty("kind", out var k) && owner.TryGetProperty("name", out var n))
                                {
                                    pod.ControlledBy = $"{k.GetString()}/{n.GetString()}";
                                    break;
                                }
                            }
                        }

                        if (meta.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var l in labels.EnumerateObject())
                            {
                                pod.Labels[l.Name] = l.Value.GetString() ?? "";
                            }
                        }
                    }

                    if (podElem.TryGetProperty("spec", out var spec))
                    {
                        if (spec.TryGetProperty("nodeName", out var nodeProp))
                            pod.NodeName = nodeProp.GetString() ?? "-";

                        if (spec.TryGetProperty("containers", out var containers) && containers.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var c in containers.EnumerateArray())
                            {
                                if (c.TryGetProperty("name", out var cname))
                                    pod.Containers.Add(cname.GetString() ?? "");
                            }
                        }
                    }

                    pod.TotalContainers = pod.Containers.Count;

                    if (podElem.TryGetProperty("status", out var status))
                    {
                        if (status.TryGetProperty("phase", out var phaseProp))
                            pod.Status = phaseProp.GetString() ?? "Unknown";

                        if (status.TryGetProperty("podIP", out var podIpProp))
                            pod.PodIP = podIpProp.GetString() ?? "-";

                        if (status.TryGetProperty("hostIP", out var hostIpProp))
                            pod.HostIP = hostIpProp.GetString() ?? "-";

                        if (status.TryGetProperty("containerStatuses", out var cStatuses) && cStatuses.ValueKind == JsonValueKind.Array)
                        {
                            int readyCount = 0;
                            int restarts = 0;
                            string detailedReason = "";

                            foreach (var cs in cStatuses.EnumerateArray())
                            {
                                if (cs.TryGetProperty("ready", out var readyProp) && readyProp.GetBoolean())
                                    readyCount++;

                                if (cs.TryGetProperty("restartCount", out var rcProp) && rcProp.TryGetInt32(out var rc))
                                    restarts += rc;

                                if (cs.TryGetProperty("state", out var stateProp))
                                {
                                    if (stateProp.TryGetProperty("waiting", out var waiting) && waiting.TryGetProperty("reason", out var reason))
                                    {
                                        detailedReason = reason.GetString() ?? "";
                                    }
                                }
                            }

                            pod.ReadyContainers = readyCount;
                            pod.Restarts = restarts;

                            if (!string.IsNullOrEmpty(detailedReason))
                            {
                                pod.Status = detailedReason;
                            }
                        }
                    }

                    // Compute badge color
                    pod.StatusColor = PodItem.ComputeStatusColor(pod.Status);
                    list.Add(pod);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ClusterService.GetPodsAsync", "Failed to parse pods JSON output.", ex);
            }

            return list;
        }

        public async Task<string> GetPodLogsAsync(string podName, string ns, string? containerName = null, int lines = 300, bool previous = false)
        {
            string cntFlag = !string.IsNullOrEmpty(containerName) ? $"-c '{containerName}'" : "";
            string prevFlag = previous ? "--previous" : "";
            string cmd = BuildKubeCmd($"logs '{podName}' -n '{ns}' {cntFlag} --tail={lines} {prevFlag}");
            var res = await _ssh.RunCommandAsync(cmd, timeoutSeconds: 30);
            return res.IsSuccess ? res.Output : (string.IsNullOrEmpty(res.Error) ? res.Output : res.Error);
        }

        public async Task<string> GetPodYamlAsync(string podName, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get pod '{podName}' -n '{ns}' -o yaml"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<(string ResourceTitle, string Yaml)> GetPodEditableYamlAsync(PodItem pod)
        {
            if (!string.IsNullOrEmpty(pod.ControlledBy))
            {
                var parts = pod.ControlledBy.Split('/', 2);
                if (parts.Length == 2)
                {
                    string kind = parts[0];
                    string ctrlName = parts[1];

                    if (string.Equals(kind, "StatefulSet", StringComparison.OrdinalIgnoreCase))
                    {
                        var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get statefulset '{ctrlName}' -n '{pod.Namespace}' -o yaml"));
                        if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
                        {
                            return ($"StatefulSet/{ctrlName}", res.Output);
                        }
                    }
                    else if (string.Equals(kind, "Deployment", StringComparison.OrdinalIgnoreCase))
                    {
                        var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get deployment '{ctrlName}' -n '{pod.Namespace}' -o yaml"));
                        if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
                        {
                            return ($"Deployment/{ctrlName}", res.Output);
                        }
                    }
                    else if (string.Equals(kind, "DaemonSet", StringComparison.OrdinalIgnoreCase))
                    {
                        var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get daemonset '{ctrlName}' -n '{pod.Namespace}' -o yaml"));
                        if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
                        {
                            return ($"DaemonSet/{ctrlName}", res.Output);
                        }
                    }
                    else if (string.Equals(kind, "ReplicaSet", StringComparison.OrdinalIgnoreCase))
                    {
                        var depRes = await _ssh.RunCommandAsync(BuildKubeCmd($"get rs '{ctrlName}' -n '{pod.Namespace}' -o jsonpath='{{.metadata.ownerReferences[0].name}}'"));
                        if (depRes.IsSuccess && !string.IsNullOrWhiteSpace(depRes.Output))
                        {
                            string depName = depRes.Output.Trim();
                            var yamlRes = await _ssh.RunCommandAsync(BuildKubeCmd($"get deployment '{depName}' -n '{pod.Namespace}' -o yaml"));
                            if (yamlRes.IsSuccess && !string.IsNullOrWhiteSpace(yamlRes.Output))
                            {
                                return ($"Deployment/{depName}", yamlRes.Output);
                            }
                        }
                    }
                }
            }

            string podYaml = await GetPodYamlAsync(pod.Name, pod.Namespace);
            return ($"Pod/{pod.Name}", podYaml);
        }

        public async Task<string> DescribePodAsync(string podName, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"describe pod '{podName}' -n '{ns}'"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<bool> DeletePodAsync(string podName, string ns, bool force = false)
        {
            string forceFlag = force ? "--force --grace-period=0" : "";
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"delete pod '{podName}' -n '{ns}' {forceFlag}"));
            return res.IsSuccess;
        }

        public async Task<bool> RestartPodAsync(PodItem pod)
        {
            if (!string.IsNullOrEmpty(pod.ControlledBy))
            {
                if (pod.ControlledBy.StartsWith("ReplicaSet/", StringComparison.OrdinalIgnoreCase))
                {
                    return await DeletePodAsync(pod.Name, pod.Namespace);
                }
                if (pod.ControlledBy.StartsWith("Deployment/", StringComparison.OrdinalIgnoreCase))
                {
                    var depName = pod.ControlledBy.Substring("Deployment/".Length);
                    return await RestartDeploymentAsync(depName, pod.Namespace);
                }
                if (pod.ControlledBy.StartsWith("StatefulSet/", StringComparison.OrdinalIgnoreCase))
                {
                    var stsName = pod.ControlledBy.Substring("StatefulSet/".Length);
                    var res = await _ssh.RunCommandAsync(BuildKubeCmd($"rollout restart statefulset '{stsName}' -n '{pod.Namespace}'"));
                    return res.IsSuccess;
                }
            }

            return await DeletePodAsync(pod.Name, pod.Namespace);
        }

        public async Task<bool> RedeployPodAsync(PodItem pod)
        {
            if (!string.IsNullOrEmpty(pod.ControlledBy))
            {
                var parts = pod.ControlledBy.Split('/', 2);
                if (parts.Length == 2)
                {
                    if (string.Equals(parts[0], "ReplicaSet", StringComparison.OrdinalIgnoreCase))
                    {
                        var depRes = await _ssh.RunCommandAsync(BuildKubeCmd($"get rs '{parts[1]}' -n '{pod.Namespace}' -o jsonpath='{{.metadata.ownerReferences[0].name}}'"));
                        if (depRes.IsSuccess && !string.IsNullOrWhiteSpace(depRes.Output))
                        {
                            return await RestartDeploymentAsync(depRes.Output.Trim(), pod.Namespace);
                        }
                    }
                    else if (string.Equals(parts[0], "Deployment", StringComparison.OrdinalIgnoreCase))
                    {
                        return await RestartDeploymentAsync(parts[1].Trim(), pod.Namespace);
                    }
                    else if (string.Equals(parts[0], "StatefulSet", StringComparison.OrdinalIgnoreCase))
                    {
                        var res = await _ssh.RunCommandAsync(BuildKubeCmd($"rollout restart statefulset '{parts[1].Trim()}' -n '{pod.Namespace}'"));
                        return res.IsSuccess;
                    }
                    else if (string.Equals(parts[0], "DaemonSet", StringComparison.OrdinalIgnoreCase))
                    {
                        var res = await _ssh.RunCommandAsync(BuildKubeCmd($"rollout restart daemonset '{parts[1].Trim()}' -n '{pod.Namespace}'"));
                        return res.IsSuccess;
                    }
                }
            }

            // Standard delete-and-recreate
            return await DeletePodAsync(pod.Name, pod.Namespace, force: true);
        }

        public async Task<bool> StopPodAsync(PodItem pod)
        {
            // If deployment or statefulset controlled, scale controller to 0 replicas!
            if (!string.IsNullOrEmpty(pod.ControlledBy))
            {
                var parts = pod.ControlledBy.Split('/', 2);
                if (parts.Length == 2)
                {
                    if (string.Equals(parts[0], "Deployment", StringComparison.OrdinalIgnoreCase))
                    {
                        return await ScaleDeploymentAsync(parts[1].Trim(), pod.Namespace, 0);
                    }
                    else if (string.Equals(parts[0], "StatefulSet", StringComparison.OrdinalIgnoreCase))
                    {
                        var res = await _ssh.RunCommandAsync(BuildKubeCmd($"scale statefulset '{parts[1].Trim()}' -n '{pod.Namespace}' --replicas=0"));
                        return res.IsSuccess;
                    }
                    else if (string.Equals(parts[0], "ReplicaSet", StringComparison.OrdinalIgnoreCase))
                    {
                        var depRes = await _ssh.RunCommandAsync(BuildKubeCmd($"get rs '{parts[1]}' -n '{pod.Namespace}' -o jsonpath='{{.metadata.ownerReferences[0].name}}'"));
                        if (depRes.IsSuccess && !string.IsNullOrWhiteSpace(depRes.Output))
                        {
                            return await ScaleDeploymentAsync(depRes.Output.Trim(), pod.Namespace, 0);
                        }
                    }
                }
            }

            // If standalone pod without deployment/statefulset controller, delete the pod
            return await DeletePodAsync(pod.Name, pod.Namespace);
        }

        public async Task<string> ExecInPodAsync(string podName, string ns, string containerName, string command)
        {
            string cntFlag = !string.IsNullOrEmpty(containerName) ? $"-c '{containerName}'" : "";
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec '{podName}' -n '{ns}' {cntFlag} -- {command}"));
            return res.IsSuccess ? res.Output : $"{res.Output}\nError: {res.Error}";
        }

        private async Task<string?> ResolvePodContainerRootFsAsync(string podName, string ns, string containerName)
        {
            var podJsonRes = await _ssh.RunCommandAsync(BuildKubeCmd($"get pod '{podName}' -n '{ns}' -o json"));
            if (!podJsonRes.IsSuccess || string.IsNullOrWhiteSpace(podJsonRes.Output))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(podJsonRes.Output);
                if (doc.RootElement.TryGetProperty("status", out var status) &&
                    status.TryGetProperty("containerStatuses", out var cStatuses) &&
                    cStatuses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cs in cStatuses.EnumerateArray())
                    {
                        string cName = cs.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                        if (!string.IsNullOrEmpty(containerName) && !string.Equals(cName, containerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (cs.TryGetProperty("containerID", out var cidElem))
                        {
                            string rawCid = cidElem.GetString() ?? "";
                            int sepIdx = rawCid.IndexOf("://", StringComparison.Ordinal);
                            string cid = sepIdx >= 0 ? rawCid.Substring(sepIdx + 3).Trim() : rawCid.Trim();
                            if (!string.IsNullOrEmpty(cid))
                            {
                                string resolveCmd =
                                    $"pid=$(crictl inspect '{cid}' 2>/dev/null | grep -o '\"pid\": *[0-9]*' | head -n 1 | grep -o '[0-9]*'); " +
                                    $"if [ -n \"$pid\" ] && [ -d \"/proc/$pid/root\" ]; then echo \"/proc/$pid/root\"; exit 0; fi; " +
                                    $"mp=$(podman mount '{cid}' 2>/dev/null || buildah mount '{cid}' 2>/dev/null); " +
                                    $"if [ -n \"$mp\" ] && [ -d \"$mp\" ]; then echo \"$mp\"; exit 0; fi";
                                var pidRes = await _ssh.RunCommandAsync(resolveCmd);
                                if (pidRes.IsSuccess && !string.IsNullOrWhiteSpace(pidRes.Output))
                                {
                                    return pidRes.Output.Trim().Split('\n')[0].Trim();
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public async Task<List<RemoteFileInfo>> ListPodFilesDetailedAsync(string podName, string ns, string containerName, string path)
        {
            string cntFlag = !string.IsNullOrEmpty(containerName) ? $"-c '{containerName}'" : "";
            string escapedPath = path.Replace("'", "'\\''");
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec '{podName}' -n '{ns}' {cntFlag} -- sh -c \"ls -la -N --time-style=iso '{escapedPath}' 2>/dev/null || ls -la --time-style=iso '{escapedPath}' 2>/dev/null || ls -la '{escapedPath}'\""));
            if (res.IsSuccess)
            {
                return ParseLsOutput(res.Output, path);
            }

            // Fallback for shell-less / chiseled .NET containers via host /proc/<pid>/root
            string? rootFs = await ResolvePodContainerRootFsAsync(podName, ns, containerName);
            if (!string.IsNullOrEmpty(rootFs))
            {
                string hostDir = $"{rootFs.TrimEnd('/')}/{path.TrimStart('/')}";
                string escapedHostDir = hostDir.Replace("'", "'\\''");
                var hostRes = await _ssh.RunCommandAsync($"ls -la -N --time-style=iso '{escapedHostDir}' 2>/dev/null || ls -la --time-style=iso '{escapedHostDir}' 2>/dev/null || ls -la '{escapedHostDir}'");
                if (hostRes.IsSuccess)
                {
                    return ParseLsOutput(hostRes.Output, path);
                }
            }

            return new List<RemoteFileInfo>();
        }

        public async Task CopyPodFileToWindowsAsync(string podName, string ns, string containerName, string remoteFilePath, string localWindowsPath)
        {
            string cntFlag = !string.IsNullOrEmpty(containerName) ? $"-c '{containerName}'" : "";
            string escapedPath = remoteFilePath.Replace("'", "'\\''");
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec '{podName}' -n '{ns}' {cntFlag} -- base64 '{escapedPath}'"), timeoutSeconds: 120);
            if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
            {
                byte[] bytes = Convert.FromBase64String(res.Output.Trim());
                await System.IO.File.WriteAllBytesAsync(localWindowsPath, bytes);
                return;
            }

            string? rootFs = await ResolvePodContainerRootFsAsync(podName, ns, containerName);
            if (!string.IsNullOrEmpty(rootFs))
            {
                string hostFile = $"{rootFs.TrimEnd('/')}/{remoteFilePath.TrimStart('/')}";
                await _ssh.DownloadFileAsync(hostFile, localWindowsPath);
                return;
            }

            throw new Exception($"Failed to download from pod: {res.Error}");
        }

        public async Task CopyWindowsFileToPodAsync(string podName, string ns, string containerName, string localWindowsPath, string remoteDestinationPath)
        {
            string cntFlag = !string.IsNullOrEmpty(containerName) ? $"-c '{containerName}'" : "";
            byte[] bytes = await System.IO.File.ReadAllBytesAsync(localWindowsPath);
            string b64 = Convert.ToBase64String(bytes);
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec -i '{podName}' -n '{ns}' {cntFlag} -- sh -c 'echo \"{b64}\" | base64 -d > \"{remoteDestinationPath}\"'"), timeoutSeconds: 120);
            if (res.IsSuccess)
            {
                return;
            }

            string? rootFs = await ResolvePodContainerRootFsAsync(podName, ns, containerName);
            if (!string.IsNullOrEmpty(rootFs))
            {
                string hostFile = $"{rootFs.TrimEnd('/')}/{remoteDestinationPath.TrimStart('/')}";
                await _ssh.UploadFileAsync(localWindowsPath, hostFile);
                return;
            }

            throw new Exception($"Upload to pod failed: {res.Error}");
        }

        public async Task<string> ReadPodFileAsync(string podName, string ns, string containerName, string path)
        {
            string cntFlag = !string.IsNullOrEmpty(containerName) ? $"-c '{containerName}'" : "";
            string escapedPath = path.Replace("'", "'\\''");
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec '{podName}' -n '{ns}' {cntFlag} -- head -c 2097152 '{escapedPath}'"));
            if (res.IsSuccess)
            {
                return res.Output;
            }

            string? rootFs = await ResolvePodContainerRootFsAsync(podName, ns, containerName);
            if (!string.IsNullOrEmpty(rootFs))
            {
                string hostFile = $"{rootFs.TrimEnd('/')}/{path.TrimStart('/')}";
                return await _ssh.ReadTextFileAsync(hostFile);
            }

            return res.Error;
        }

        public async Task<List<string>> GetPodAttachedPvcsAsync(string podName, string ns)
        {
            var pvcList = new List<string>();
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get pod '{podName}' -n '{ns}' -o json"));
            if (!res.IsSuccess || string.IsNullOrWhiteSpace(res.Output))
                return pvcList;

            try
            {
                using var doc = JsonDocument.Parse(res.Output);
                if (doc.RootElement.TryGetProperty("spec", out var spec) &&
                    spec.TryGetProperty("volumes", out var volumes) &&
                    volumes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in volumes.EnumerateArray())
                    {
                        if (v.TryGetProperty("persistentVolumeClaim", out var pvc) &&
                            pvc.TryGetProperty("claimName", out var claimName))
                        {
                            string cName = claimName.GetString() ?? "";
                            if (!string.IsNullOrEmpty(cName) && !pvcList.Contains(cName))
                            {
                                pvcList.Add(cName);
                            }
                        }
                    }
                }
            }
            catch { }

            return pvcList;
        }

        // Deployments
        public async Task<List<DeploymentItem>> GetDeploymentsAsync(string? namespaceName = null)
        {
            var list = new List<DeploymentItem>();
            string nsFlag = string.IsNullOrWhiteSpace(namespaceName) || namespaceName == "All Namespaces"
                ? "-A"
                : $"-n '{namespaceName}'";

            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get deployments {nsFlag} -o json"));
            if (!res.IsSuccess || string.IsNullOrWhiteSpace(res.Output))
                return list;

            try
            {
                using var doc = JsonDocument.Parse(res.Output);
                var root = doc.RootElement;
                if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    return list;

                foreach (var elem in items.EnumerateArray())
                {
                    var dep = new DeploymentItem();
                    if (elem.TryGetProperty("metadata", out var meta))
                    {
                        if (meta.TryGetProperty("name", out var n)) dep.Name = n.GetString() ?? "";
                        if (meta.TryGetProperty("namespace", out var ns)) dep.Namespace = ns.GetString() ?? "";
                        if (meta.TryGetProperty("creationTimestamp", out var ct) && ct.TryGetDateTime(out var dt))
                        {
                            dep.CreatedAt = dt;
                            dep.AgeDisplay = FormatAge(dt);
                        }
                    }

                    if (elem.TryGetProperty("spec", out var spec))
                    {
                        if (spec.TryGetProperty("replicas", out var rep)) dep.Replicas = rep.GetInt32();
                        if (spec.TryGetProperty("template", out var tmpl) &&
                            tmpl.TryGetProperty("spec", out var tSpec) &&
                            tSpec.TryGetProperty("containers", out var containers) &&
                            containers.ValueKind == JsonValueKind.Array)
                        {
                            var imgList = new List<string>();
                            foreach (var c in containers.EnumerateArray())
                            {
                                if (c.TryGetProperty("image", out var img))
                                    imgList.Add(img.GetString() ?? "");
                            }
                            dep.Images = string.Join(", ", imgList);
                        }
                    }

                    if (elem.TryGetProperty("status", out var status))
                    {
                        if (status.TryGetProperty("readyReplicas", out var rr)) dep.ReadyReplicas = rr.GetInt32();
                        if (status.TryGetProperty("availableReplicas", out var ar)) dep.AvailableReplicas = ar.GetInt32();
                        if (status.TryGetProperty("updatedReplicas", out var ur)) dep.UpToDateReplicas = ur.GetInt32();
                    }

                    list.Add(dep);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ClusterService.GetDeploymentsAsync", "Failed to parse deployments JSON output.", ex);
            }

            return list;
        }

        public async Task<string> GetDeploymentYamlAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get deployment '{name}' -n '{ns}' -o yaml"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<string> DescribeDeploymentAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"describe deployment '{name}' -n '{ns}'"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<bool> ScaleDeploymentAsync(string name, string ns, int replicas)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"scale deployment '{name}' -n '{ns}' --replicas={replicas}"));
            return res.IsSuccess;
        }

        public async Task<bool> RestartDeploymentAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"rollout restart deployment '{name}' -n '{ns}'"));
            return res.IsSuccess;
        }

        public async Task<bool> DeleteDeploymentAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"delete deployment '{name}' -n '{ns}'"));
            return res.IsSuccess;
        }

        public async Task<(bool Success, string Message)> ApplyYamlAsync(string yaml, string? namespaceName = null)
        {
            if (string.IsNullOrWhiteSpace(yaml))
                return (false, "YAML content is empty.");

            string tmpFile = $"/tmp/k8s_apply_{Guid.NewGuid():N}.yaml";
            try
            {
                string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(yaml));
                var writeRes = await _ssh.RunCommandAsync($"echo '{b64}' | base64 -d > {tmpFile}");
                if (!writeRes.IsSuccess)
                {
                    return (false, $"Failed to stage YAML on remote host: {writeRes.Error}");
                }

                string nsFlag = string.IsNullOrWhiteSpace(namespaceName) || namespaceName == "All Namespaces"
                    ? ""
                    : $"-n '{namespaceName}'";

                var applyRes = await _ssh.RunCommandAsync(BuildKubeCmd($"apply -f {tmpFile} {nsFlag}"));
                string msg = applyRes.IsSuccess ? applyRes.Output : applyRes.Error;
                return (applyRes.IsSuccess, msg);
            }
            finally
            {
                await _ssh.RunCommandAsync($"rm -f {tmpFile}");
            }
        }

        // PVC
        public async Task<List<PvcItem>> GetPvcsAsync(string? namespaceName = null)
        {
            var list = new List<PvcItem>();
            string nsFlag = string.IsNullOrWhiteSpace(namespaceName) || namespaceName == "All Namespaces"
                ? "-A"
                : $"-n '{namespaceName}'";

            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get pvc {nsFlag} -o json"));
            if (!res.IsSuccess || string.IsNullOrWhiteSpace(res.Output))
                return list;

            try
            {
                using var doc = JsonDocument.Parse(res.Output);
                var root = doc.RootElement;
                if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    return list;

                foreach (var elem in items.EnumerateArray())
                {
                    var pvc = new PvcItem();
                    if (elem.TryGetProperty("metadata", out var meta))
                    {
                        if (meta.TryGetProperty("name", out var n)) pvc.Name = n.GetString() ?? "";
                        if (meta.TryGetProperty("namespace", out var ns)) pvc.Namespace = ns.GetString() ?? "";
                        if (meta.TryGetProperty("creationTimestamp", out var ct) && ct.TryGetDateTime(out var dt))
                        {
                            pvc.CreatedAt = dt;
                            pvc.AgeDisplay = FormatAge(dt);
                        }
                    }

                    if (elem.TryGetProperty("spec", out var spec))
                    {
                        if (spec.TryGetProperty("volumeName", out var vn)) pvc.VolumeName = vn.GetString() ?? "-";
                        if (spec.TryGetProperty("storageClassName", out var sc)) pvc.StorageClass = sc.GetString() ?? "-";
                        if (spec.TryGetProperty("resources", out var resElem) &&
                            resElem.TryGetProperty("requests", out var req) &&
                            req.TryGetProperty("storage", out var stor))
                        {
                            pvc.Capacity = stor.GetString() ?? "-";
                        }
                        if (spec.TryGetProperty("accessModes", out var am) && am.ValueKind == JsonValueKind.Array)
                        {
                            var modes = new List<string>();
                            foreach (var m in am.EnumerateArray()) modes.Add(m.GetString() ?? "");
                            pvc.AccessModes = string.Join(", ", modes);
                        }
                    }

                    if (elem.TryGetProperty("status", out var status))
                    {
                        if (status.TryGetProperty("phase", out var p)) pvc.Status = p.GetString() ?? "Unknown";
                    }

                    list.Add(pvc);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ClusterService.GetPvcsAsync", "Failed to parse PVCs JSON output.", ex);
            }

            return list;
        }

        public async Task<string> GetPvcYamlAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get pvc '{name}' -n '{ns}' -o yaml"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<string> DescribePvcAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"describe pvc '{name}' -n '{ns}'"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<bool> DeletePvcAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"delete pvc '{name}' -n '{ns}'"));
            return res.IsSuccess;
        }

        public async Task<PvcMountInfo> FindPvcMountAsync(string pvcName, string ns)
        {
            var info = new PvcMountInfo();

            // 1. Check if any RUNNING pod in the namespace mounts this PVC
            var podsRes = await _ssh.RunCommandAsync(BuildKubeCmd($"get pods -n '{ns}' -o json"));
            if (podsRes.IsSuccess && !string.IsNullOrWhiteSpace(podsRes.Output))
            {
                try
                {
                    using var doc = JsonDocument.Parse(podsRes.Output);
                    if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pod in items.EnumerateArray())
                        {
                            string podName = pod.GetProperty("metadata").GetProperty("name").GetString() ?? "";
                            string phase = pod.TryGetProperty("status", out var st) && st.TryGetProperty("phase", out var ph)
                                ? (ph.GetString() ?? "")
                                : "";
                            bool isPodRunning = string.Equals(phase, "Running", StringComparison.OrdinalIgnoreCase);

                            if (pod.TryGetProperty("spec", out var spec) &&
                                spec.TryGetProperty("volumes", out var volumes) &&
                                volumes.ValueKind == JsonValueKind.Array)
                            {
                                string? matchedVolName = null;
                                foreach (var v in volumes.EnumerateArray())
                                {
                                    if (v.TryGetProperty("persistentVolumeClaim", out var pvcRef) &&
                                        pvcRef.TryGetProperty("claimName", out var cn) &&
                                        cn.GetString() == pvcName)
                                    {
                                        matchedVolName = v.GetProperty("name").GetString();
                                        break;
                                    }
                                }

                                if (!string.IsNullOrEmpty(matchedVolName) &&
                                    spec.TryGetProperty("containers", out var containers) &&
                                    containers.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var c in containers.EnumerateArray())
                                    {
                                        string cName = c.GetProperty("name").GetString() ?? "";
                                        if (c.TryGetProperty("volumeMounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array)
                                        {
                                            foreach (var m in mounts.EnumerateArray())
                                            {
                                                if (m.TryGetProperty("name", out var mn) && mn.GetString() == matchedVolName)
                                                {
                                                    if (isPodRunning)
                                                    {
                                                        info.IsMounted = true;
                                                        info.PodName = podName;
                                                        info.ContainerName = cName;
                                                        info.MountPath = m.GetProperty("mountPath").GetString() ?? "/";
                                                        if (m.TryGetProperty("subPath", out var sp))
                                                        {
                                                            info.SubPath = sp.GetString() ?? "";
                                                        }
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        if (info.IsMounted) break;
                                    }
                                }
                            }
                            if (info.IsMounted) break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ClusterService.FindPvcMountAsync", "Failed to parse pods JSON when locating PVC mount.", ex);
                }
            }

            // 2. Always resolve the PV VolumeName and Node HostStoragePath (supports kubelet CSI mounts & shell-less .NET containers)
            var pvNameRes = await _ssh.RunCommandAsync(BuildKubeCmd($"get pvc '{pvcName}' -n '{ns}' -o jsonpath='{{.spec.volumeName}}'"));
            if (pvNameRes.IsSuccess && !string.IsNullOrWhiteSpace(pvNameRes.Output))
            {
                info.VolumeName = pvNameRes.Output.Trim();
                var pvRes = await _ssh.RunCommandAsync(BuildKubeCmd($"get pv '{info.VolumeName}' -o json"));
                if (pvRes.IsSuccess && !string.IsNullOrWhiteSpace(pvRes.Output))
                {
                    try
                    {
                        using var pvDoc = JsonDocument.Parse(pvRes.Output);
                        var pvSpec = pvDoc.RootElement.GetProperty("spec");
                        if (pvSpec.TryGetProperty("hostPath", out var hp) && hp.TryGetProperty("path", out var hpPath))
                        {
                            info.HostStoragePath = hpPath.GetString() ?? "";
                        }
                        else if (pvSpec.TryGetProperty("local", out var loc) && loc.TryGetProperty("path", out var locPath))
                        {
                            info.HostStoragePath = locPath.GetString() ?? "";
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("ClusterService.FindPvcMountAsync", $"Failed to parse PV '{info.VolumeName}' JSON.", ex);
                    }
                }

                // Check kubelet CSI / volume mounts (e.g. topolvm-provisioner: /var/lib/kubelet/pods/<uid>/volumes/kubernetes.io~csi/<pv>/mount) and MicroShift/CRI paths
                if (string.IsNullOrEmpty(info.HostStoragePath) && !string.IsNullOrEmpty(info.VolumeName))
                {
                    string vol = info.VolumeName.Replace("'", "");
                    string batchCheck =
                        $"for p in /var/lib/kubelet/pods/*/volumes/*/'{vol}'/mount /var/lib/kubelet/pods/*/volumes/*/'{vol}' /sysroot/ostree/deploy/*/var/lib/kubelet/pods/*/volumes/*/'{vol}'/mount /var/lib/microshift/data/'{vol}' /var/lib/containers/storage/volumes/'{vol}' /mnt/data/'{vol}'; do " +
                        $"if [ -d \"$p\" ]; then echo \"$p\"; exit 0; fi; done; " +
                        $"found=$(find /var/lib/kubelet/pods -name '{vol}' 2>/dev/null | head -n 1); " +
                        $"if [ -n \"$found\" ]; then if [ -d \"$found/mount\" ]; then echo \"$found/mount\"; else echo \"$found\"; fi; fi";
                    var testRes = await _ssh.RunCommandAsync(batchCheck);
                    if (testRes.IsSuccess && !string.IsNullOrWhiteSpace(testRes.Output))
                    {
                        info.HostStoragePath = testRes.Output.Trim().Split('\n')[0].Trim();
                    }
                }
            }

            return info;
        }

        public async Task<(List<RemoteFileInfo> Files, string CurrentPath, PvcMountInfo MountInfo)> ListPvcFilesAsync(string pvcName, string ns, string? subPath = null)
        {
            var list = new List<RemoteFileInfo>();
            var mountInfo = await FindPvcMountAsync(pvcName, ns);
            string targetPath = "/";

            // If user navigated to a path already inside HostStoragePath, or if we should use HostStoragePath fallback
            bool targetIsHostPath = !string.IsNullOrEmpty(subPath) &&
                                    !string.IsNullOrEmpty(mountInfo.HostStoragePath) &&
                                    subPath.StartsWith(mountInfo.HostStoragePath, StringComparison.Ordinal);

            if (mountInfo.IsMounted && !targetIsHostPath)
            {
                targetPath = string.IsNullOrWhiteSpace(subPath) ? mountInfo.MountPath : subPath;
                string escapedTarget = targetPath.Replace("'", "'\\''");
                var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec '{mountInfo.PodName}' -n '{ns}' -c '{mountInfo.ContainerName}' -- sh -c \"ls -la -N --time-style=iso '{escapedTarget}' 2>/dev/null || ls -la --time-style=iso '{escapedTarget}' 2>/dev/null || ls -la '{escapedTarget}'\""));
                if (res.IsSuccess)
                {
                    list = ParseLsOutput(res.Output, targetPath);
                    return (list, targetPath, mountInfo);
                }

                // Fallback to HostStoragePath (e.g. .NET runtime/chiseled images without /bin/sh or ls, or kubelet CSI mount on node)
                if (!string.IsNullOrEmpty(mountInfo.HostStoragePath))
                {
                    mountInfo.IsMounted = false;
                    targetPath = mountInfo.HostStoragePath;
                    list = await _ssh.ListDirectoryAsync(targetPath);
                    return (list, targetPath, mountInfo);
                }
            }

            if (!string.IsNullOrEmpty(mountInfo.HostStoragePath))
            {
                mountInfo.IsMounted = false;
                targetPath = string.IsNullOrWhiteSpace(subPath) ? mountInfo.HostStoragePath : subPath;
                list = await _ssh.ListDirectoryAsync(targetPath);
            }

            return (list, targetPath, mountInfo);
        }

        private static string UnquoteLsName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Strip trailing quote if present
            if (name.EndsWith('\'') || name.EndsWith('"'))
            {
                name = name.Substring(0, name.Length - 1);
            }

            // Strip leading quote if present
            if (name.StartsWith('\'') || name.StartsWith('"'))
            {
                name = name.Substring(1);
            }

            return name;
        }

        private static string CleanRemotePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = UnquoteLsName(segments[i]);
            }
            return string.Join("/", segments);
        }

        private async Task<string> ResolvePvcHostFilePathAsync(PvcMountInfo mountInfo, string containerOrHostPath, bool mustExist = true)
        {
            containerOrHostPath = CleanRemotePath(containerOrHostPath);
            if (string.IsNullOrEmpty(mountInfo.HostStoragePath))
                return containerOrHostPath;

            string hostBase = mountInfo.HostStoragePath.TrimEnd('/');
            var candidates = new List<string>();

            if (containerOrHostPath.StartsWith(hostBase, StringComparison.Ordinal))
            {
                candidates.Add(containerOrHostPath);
            }
            else
            {
                string rel = containerOrHostPath.StartsWith(mountInfo.MountPath, StringComparison.Ordinal)
                    ? containerOrHostPath.Substring(mountInfo.MountPath.Length).TrimStart('/')
                    : containerOrHostPath.TrimStart('/');

                if (!string.IsNullOrEmpty(mountInfo.SubPath))
                {
                    string sp = mountInfo.SubPath.Trim('/');
                    candidates.Add(string.IsNullOrEmpty(rel) ? $"{hostBase}/{sp}" : $"{hostBase}/{sp}/{rel}");
                }

                candidates.Add(string.IsNullOrEmpty(rel) ? hostBase : $"{hostBase}/{rel}");

                string mountFolderName = mountInfo.MountPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                if (!string.IsNullOrEmpty(mountFolderName) && !rel.StartsWith(mountFolderName + "/", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(string.IsNullOrEmpty(rel) ? $"{hostBase}/{mountFolderName}" : $"{hostBase}/{mountFolderName}/{rel}");
                }
            }

            if (!mustExist)
                return candidates[0];

            foreach (var cand in candidates)
            {
                string escapedCand = cand.Replace("'", "'\\''");
                var checkRes = await _ssh.RunCommandAsync($"if [ -e '{escapedCand}' ]; then echo 'OK'; fi");
                if (checkRes.IsSuccess && checkRes.Output.Contains("OK"))
                {
                    return cand;
                }
            }

            // Fallback: locate the file inside the PVC host mount folder
            string rawFileName = UnquoteLsName(System.IO.Path.GetFileName(containerOrHostPath));
            if (!string.IsNullOrEmpty(rawFileName))
            {
                string escapedBase = hostBase.Replace("'", "'\\''");
                string b64Target = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rawFileName));

                // 1. Direct Python exact-match script using valid multi-line structure
                string pyScript =
                    "import os, sys, base64\n" +
                    $"target = base64.b64decode('{b64Target}').decode('utf-8')\n" +
                    $"root = '{escapedBase}'\n" +
                    "found = ''\n" +
                    "for r, d, files in os.walk(root):\n" +
                    "    for f in files:\n" +
                    "        if f == target or f.lower() == target.lower():\n" +
                    "            found = os.path.join(r, f)\n" +
                    "            break\n" +
                    "    if found:\n" +
                    "        break\n" +
                    "if found:\n" +
                    "    print(found)\n";
                string b64PyScript = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pyScript));
                string pythonSearch = $"python3 -c \"$(printf '%s' '{b64PyScript}' | base64 -d)\" 2>/dev/null";

                var pyRes = await _ssh.RunCommandAsync(pythonSearch);
                if (pyRes.IsSuccess && !string.IsNullOrWhiteSpace(pyRes.Output))
                {
                    string foundPath = pyRes.Output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(foundPath) && foundPath.StartsWith(hostBase, StringComparison.Ordinal))
                        return foundPath;
                }

                // 2. Exact filename matching loop in bash with base64 payload
                string bashSearch =
                    $"target=$(printf '%s' '{b64Target}' | base64 -d); " +
                    $"find '{escapedBase}' -type f 2>/dev/null | while IFS= read -r f; do " +
                    $"bn=\"${{f##*/}}\"; " +
                    $"if [ \"$bn\" = \"$target\" ]; then echo \"$f\"; break; fi; " +
                    $"done";

                var bashRes = await _ssh.RunCommandAsync(bashSearch);
                if (bashRes.IsSuccess && !string.IsNullOrWhiteSpace(bashRes.Output))
                {
                    string foundPath = bashRes.Output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (!string.IsNullOrEmpty(foundPath) && foundPath.StartsWith(hostBase, StringComparison.Ordinal))
                        return foundPath;
                }

                // 3. Case-insensitive filename matching in bash loop
                string bashInameSearch =
                    $"target=$(printf '%s' '{b64Target}' | base64 -d | tr '[:upper:]' '[:lower:]'); " +
                    $"find '{escapedBase}' -type f 2>/dev/null | while IFS= read -r f; do " +
                    $"bn=\"${{f##*/}}\"; " +
                    $"bn_lower=$(printf '%s' \"$bn\" | tr '[:upper:]' '[:lower:]'); " +
                    $"if [ \"$bn_lower\" = \"$target\" ]; then echo \"$f\"; break; fi; " +
                    $"done";

                var bashInameRes = await _ssh.RunCommandAsync(bashInameSearch);
                if (bashInameRes.IsSuccess && !string.IsNullOrWhiteSpace(bashInameRes.Output))
                {
                    string foundPath = bashInameRes.Output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (!string.IsNullOrEmpty(foundPath) && foundPath.StartsWith(hostBase, StringComparison.Ordinal))
                        return foundPath;
                }
            }

            return candidates[0];
        }

        public async Task<string> ReadPvcFileAsync(string pvcName, string ns, string filePath)
        {
            var mountInfo = await FindPvcMountAsync(pvcName, ns);
            bool isHostFile = !string.IsNullOrEmpty(mountInfo.HostStoragePath) &&
                              filePath.StartsWith(mountInfo.HostStoragePath, StringComparison.Ordinal);

            byte[]? fileBytes = null;
            string? readError = null;

            if (mountInfo.IsMounted && !isHostFile)
            {
                string escapedFile = filePath.Replace("'", "'\\''");
                var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec '{mountInfo.PodName}' -n '{ns}' -c '{mountInfo.ContainerName}' -- head -c 2097152 '{escapedFile}' | base64"), timeoutSeconds: 60);
                if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
                {
                    try
                    {
                        fileBytes = Convert.FromBase64String(res.Output.Trim());
                    }
                    catch
                    {
                        // Fall back to direct text
                        return res.Output;
                    }
                }
                else
                {
                    readError = res.Error;
                }
            }

            if (fileBytes == null && !string.IsNullOrEmpty(mountInfo.HostStoragePath))
            {
                string hostFile = await ResolvePvcHostFilePathAsync(mountInfo, filePath, mustExist: true);
                string escapedHostFile = hostFile.Replace("'", "'\\''");
                var hostB64Res = await _ssh.RunCommandAsync($"head -c 2097152 '{escapedHostFile}' 2>/dev/null | base64", timeoutSeconds: 60);
                if (hostB64Res.IsSuccess && !string.IsNullOrWhiteSpace(hostB64Res.Output))
                {
                    try
                    {
                        fileBytes = Convert.FromBase64String(hostB64Res.Output.Trim());
                    }
                    catch
                    {
                    }
                }

                if (fileBytes == null)
                {
                    return await _ssh.ReadTextFileAsync(hostFile);
                }
            }

            if (fileBytes != null)
            {
                return FormatFileContentForViewer(filePath, fileBytes);
            }

            return readError ?? "Error: PVC file could not be read.";
        }

        private static string FormatFileContentForViewer(string filePath, byte[] bytes)
        {
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".der" || ext == ".cer")
            {
                try
                {
                    using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(bytes);
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("=== X.509 Certificate (.der) ===");
                    sb.AppendLine($"Subject:      {cert.Subject}");
                    sb.AppendLine($"Issuer:       {cert.Issuer}");
                    sb.AppendLine($"Not Before:   {cert.NotBefore:yyyy-MM-dd HH:mm:ss UTC}");
                    sb.AppendLine($"Not After:    {cert.NotAfter:yyyy-MM-dd HH:mm:ss UTC}");
                    sb.AppendLine($"Thumbprint:   {cert.Thumbprint}");
                    sb.AppendLine($"Serial Num:   {cert.SerialNumber}");
                    sb.AppendLine();
                    sb.AppendLine("=== PEM Encoded Format ===");
                    sb.AppendLine("-----BEGIN CERTIFICATE-----");
                    sb.AppendLine(Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks));
                    sb.AppendLine("-----END CERTIFICATE-----");
                    return sb.ToString();
                }
                catch
                {
                    // Fall back to hex dump / base64 if certificate parsing is not possible
                }
            }
            else if (ext == ".crl")
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("=== Certificate Revocation List (.crl) ===");
                    sb.AppendLine($"Raw Size: {bytes.Length} bytes");
                    sb.AppendLine();
                    sb.AppendLine("=== PEM Encoded Format ===");
                    sb.AppendLine("-----BEGIN X509 CRL-----");
                    sb.AppendLine(Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks));
                    sb.AppendLine("-----END X509 CRL-----");
                    return sb.ToString();
                }
                catch
                {
                }
            }

            // Check if content is binary or text
            bool isBinary = false;
            int checkLen = Math.Min(bytes.Length, 1024);
            for (int i = 0; i < checkLen; i++)
            {
                if (bytes[i] == 0)
                {
                    isBinary = true;
                    break;
                }
            }

            if (!isBinary)
            {
                return System.Text.Encoding.UTF8.GetString(bytes);
            }

            // Binary fallback: display Base64 with header
            var binSb = new System.Text.StringBuilder();
            binSb.AppendLine($"=== Binary File ({bytes.Length} bytes) ===");
            binSb.AppendLine("Use 'Copy to Windows File...' in the toolbar to save this exact binary file to Windows.");
            binSb.AppendLine();
            binSb.AppendLine(Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks));
            return binSb.ToString();
        }

        public async Task CopyPvcFileToWindowsAsync(string pvcName, string ns, string remoteFilePath, string localWindowsPath)
        {
            var mountInfo = await FindPvcMountAsync(pvcName, ns);
            bool isHostFile = !string.IsNullOrEmpty(mountInfo.HostStoragePath) &&
                              remoteFilePath.StartsWith(mountInfo.HostStoragePath, StringComparison.Ordinal);

            if (mountInfo.IsMounted && !isHostFile)
            {
                string escapedFile = remoteFilePath.Replace("'", "'\\''");
                var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec '{mountInfo.PodName}' -n '{ns}' -c '{mountInfo.ContainerName}' -- base64 '{escapedFile}'"), timeoutSeconds: 120);
                if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
                {
                    byte[] bytes = Convert.FromBase64String(res.Output.Trim());
                    await System.IO.File.WriteAllBytesAsync(localWindowsPath, bytes);
                    return;
                }
                if (!string.IsNullOrEmpty(mountInfo.HostStoragePath))
                {
                    string hostFile = await ResolvePvcHostFilePathAsync(mountInfo, remoteFilePath, mustExist: true);
                    await _ssh.DownloadFileAsync(hostFile, localWindowsPath);
                    return;
                }
                throw new Exception($"Failed to download from pod: {res.Error}");
            }
            else if (!string.IsNullOrEmpty(mountInfo.HostStoragePath))
            {
                string hostFile = await ResolvePvcHostFilePathAsync(mountInfo, remoteFilePath, mustExist: true);
                await _ssh.DownloadFileAsync(hostFile, localWindowsPath);
                return;
            }
            throw new Exception("PVC is not currently mounted by any running pod and not accessible on host.");
        }

        public async Task CopyWindowsFileToPvcAsync(string pvcName, string ns, string localWindowsPath, string remoteDestinationPath)
        {
            var mountInfo = await FindPvcMountAsync(pvcName, ns);
            bool isHostFile = !string.IsNullOrEmpty(mountInfo.HostStoragePath) &&
                              remoteDestinationPath.StartsWith(mountInfo.HostStoragePath, StringComparison.Ordinal);

            if (mountInfo.IsMounted && !isHostFile)
            {
                byte[] bytes = await System.IO.File.ReadAllBytesAsync(localWindowsPath);
                string b64 = Convert.ToBase64String(bytes);
                var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec -i '{mountInfo.PodName}' -n '{ns}' -c '{mountInfo.ContainerName}' -- sh -c 'echo \"{b64}\" | base64 -d > \"{remoteDestinationPath}\"'"), timeoutSeconds: 120);
                if (res.IsSuccess)
                {
                    return;
                }
                if (!string.IsNullOrEmpty(mountInfo.HostStoragePath))
                {
                    string hostFile = await ResolvePvcHostFilePathAsync(mountInfo, remoteDestinationPath, mustExist: false);
                    await _ssh.UploadFileAsync(localWindowsPath, hostFile);
                    return;
                }
                throw new Exception($"Upload to PVC failed: {res.Error}");
            }
            else if (!string.IsNullOrEmpty(mountInfo.HostStoragePath))
            {
                string hostFile = await ResolvePvcHostFilePathAsync(mountInfo, remoteDestinationPath, mustExist: false);
                await _ssh.UploadFileAsync(localWindowsPath, hostFile);
                return;
            }
            throw new Exception("PVC is not currently mounted and host path could not be resolved.");
        }

        public async Task<bool> DeletePvcFileAsync(string pvcName, string ns, string remoteFilePath)
        {
            var mountInfo = await FindPvcMountAsync(pvcName, ns);
            bool isHostFile = !string.IsNullOrEmpty(mountInfo.HostStoragePath) &&
                              remoteFilePath.StartsWith(mountInfo.HostStoragePath, StringComparison.Ordinal);

            if (mountInfo.IsMounted && !isHostFile)
            {
                string escapedFile = remoteFilePath.Replace("'", "'\\''");
                var res = await _ssh.RunCommandAsync(BuildKubeCmd($"exec '{mountInfo.PodName}' -n '{ns}' -c '{mountInfo.ContainerName}' -- rm -f -- '{escapedFile}'"));
                if (res.IsSuccess)
                {
                    return true;
                }
                if (!string.IsNullOrEmpty(mountInfo.HostStoragePath))
                {
                    string hostFile = await ResolvePvcHostFilePathAsync(mountInfo, remoteFilePath, mustExist: true);
                    await _ssh.DeleteFileAsync(hostFile);
                    return true;
                }
                throw new Exception($"Failed to delete file from pod: {res.Error}");
            }
            else if (!string.IsNullOrEmpty(mountInfo.HostStoragePath))
            {
                string hostFile = await ResolvePvcHostFilePathAsync(mountInfo, remoteFilePath, mustExist: true);
                await _ssh.DeleteFileAsync(hostFile);
                return true;
            }
            throw new Exception("PVC is not currently mounted by any running pod and not accessible on host.");
        }

        private static List<RemoteFileInfo> ParseLsOutput(string output, string basePath)
        {
            var list = new List<RemoteFileInfo>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimStart();
                if (string.IsNullOrEmpty(line) || line.StartsWith("total ", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Tokenize while tracking exact character start positions in `line` so filenames with multiple spaces are preserved
                var tokenRanges = new List<(int Start, int Length)>();
                int i = 0;
                while (i < line.Length && tokenRanges.Count < 10)
                {
                    while (i < line.Length && line[i] == ' ') i++;
                    if (i >= line.Length) break;
                    int start = i;
                    while (i < line.Length && line[i] != ' ') i++;
                    tokenRanges.Add((start, i - start));
                }

                if (tokenRanges.Count < 8)
                    continue;

                string GetToken(int idx) => line.Substring(tokenRanges[idx].Start, tokenRanges[idx].Length);

                string perms = GetToken(0);
                string token5 = GetToken(5);
                bool isIsoDate = token5.Length == 10 && token5[4] == '-' && token5[7] == '-';
                int nameStartIdx = isIsoDate ? 7 : (tokenRanges.Count >= 9 ? 8 : 7);
                if (tokenRanges.Count <= nameStartIdx)
                    continue;

                string name = line.Substring(tokenRanges[nameStartIdx].Start);
                int arrowIdx = name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrowIdx > 0) name = name.Substring(0, arrowIdx);
                name = UnquoteLsName(name);

                if (name == "." || name == "..") continue;
                bool isDir = perms.StartsWith("d") || perms.StartsWith("l");
                long.TryParse(GetToken(4), out long size);
                DateTime dt = default;
                if (isIsoDate)
                {
                    DateTime.TryParse(token5 + " " + GetToken(6), out dt);
                }
                else if (nameStartIdx == 8)
                {
                    DateTime.TryParse($"{token5} {GetToken(6)} {GetToken(7)}", out dt);
                }

                list.Add(new RemoteFileInfo
                {
                    Name = name,
                    FullPath = basePath.TrimEnd('/') + "/" + name,
                    IsDirectory = isDir,
                    SizeBytes = size,
                    LastModified = dt != default ? dt : DateTime.Now,
                    Permissions = perms
                });
            }
            list.Sort((a, b) =>
            {
                if (a.IsDirectory != b.IsDirectory)
                    return b.IsDirectory.CompareTo(a.IsDirectory);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        // Secrets & Certificates / CRL Inspection
        public async Task<List<SecretItem>> GetSecretsAsync(string? namespaceName = null)
        {
            var list = new List<SecretItem>();
            string nsFlag = string.IsNullOrWhiteSpace(namespaceName) || namespaceName == "All Namespaces"
                ? "-A"
                : $"-n '{namespaceName}'";

            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get secrets {nsFlag} -o json"));
            if (!res.IsSuccess || string.IsNullOrWhiteSpace(res.Output))
                return list;

            try
            {
                using var doc = JsonDocument.Parse(res.Output);
                var root = doc.RootElement;
                if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    return list;

                foreach (var elem in items.EnumerateArray())
                {
                    var secret = new SecretItem();
                    if (elem.TryGetProperty("metadata", out var meta))
                    {
                        if (meta.TryGetProperty("name", out var n)) secret.Name = n.GetString() ?? "";
                        if (meta.TryGetProperty("namespace", out var ns)) secret.Namespace = ns.GetString() ?? "";
                        if (meta.TryGetProperty("creationTimestamp", out var ct) && ct.TryGetDateTime(out var dt))
                        {
                            secret.CreatedAt = dt;
                            secret.AgeDisplay = FormatAge(dt);
                        }
                    }

                    if (elem.TryGetProperty("type", out var t))
                    {
                        secret.Type = t.GetString() ?? "Opaque";
                    }

                    if (elem.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in data.EnumerateObject())
                        {
                            secret.Keys.Add(prop.Name);
                        }
                        secret.DataCount = secret.Keys.Count;
                    }

                    list.Add(secret);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ClusterService.GetSecretsAsync", "Failed to parse secrets JSON output.", ex);
            }

            return list;
        }

        public async Task<string> GetSecretYamlAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get secret '{name}' -n '{ns}' -o yaml"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<string> DescribeSecretAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"describe secret '{name}' -n '{ns}'"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<List<SecretDataEntry>> GetSecretDataEntriesAsync(string name, string ns)
        {
            var list = new List<SecretDataEntry>();
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get secret '{name}' -n '{ns}' -o json"));
            if (!res.IsSuccess || string.IsNullOrWhiteSpace(res.Output))
                return list;

            try
            {
                using var doc = JsonDocument.Parse(res.Output);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in data.EnumerateObject())
                    {
                        string b64 = prop.Value.GetString() ?? "";
                        string decoded = "";
                        byte[] rawBytes = Array.Empty<byte>();

                        try
                        {
                            rawBytes = Convert.FromBase64String(b64);
                            decoded = System.Text.Encoding.UTF8.GetString(rawBytes);
                        }
                        catch
                        {
                            decoded = "(Binary content - could not decode as UTF-8 string)";
                        }

                        string detectedType = "Base64 Data";
                        string keyLower = prop.Name.ToLowerInvariant();
                        if (keyLower.EndsWith(".crt") || keyLower.EndsWith(".cer") || decoded.Contains("BEGIN CERTIFICATE"))
                        {
                            detectedType = "X.509 Certificate (PEM)";
                        }
                        else if (keyLower.EndsWith(".crl") || decoded.Contains("BEGIN X509 CRL") || decoded.Contains("BEGIN CERTIFICATE REVOCATION LIST"))
                        {
                            detectedType = "Certificate Revocation List (CRL)";
                        }
                        else if (keyLower.EndsWith(".key") || decoded.Contains("PRIVATE KEY"))
                        {
                            detectedType = "Private Key";
                        }
                        else if (keyLower.EndsWith(".json") || (decoded.TrimStart().StartsWith("{") && decoded.TrimEnd().EndsWith("}")))
                        {
                            detectedType = "JSON Document";
                        }
                        else if (keyLower == "token")
                        {
                            detectedType = "Bearer Token";
                        }
                        else if (keyLower == "ca.crt")
                        {
                            detectedType = "CA Certificate (Root/Intermediate)";
                        }
                        else if (!decoded.Contains('\0') && decoded.Length > 0 && !decoded.StartsWith("(Binary"))
                        {
                            detectedType = "UTF-8 Text";
                        }

                        list.Add(new SecretDataEntry
                        {
                            Key = prop.Name,
                            Base64Value = b64,
                            DecodedText = decoded,
                            ByteLength = rawBytes.Length,
                            DetectedType = detectedType
                        });
                    }
                }
            }
            catch
            {
            }

            return list;
        }

        public async Task<bool> DeleteSecretAsync(string name, string ns)
        {
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"delete secret '{name}' -n '{ns}'"));
            return res.IsSuccess;
        }

        // Ingress & OpenShift Routes
        public async Task<List<IngressRouteItem>> GetIngressRoutesAsync(string? namespaceName = null)
        {
            var list = new List<IngressRouteItem>();
            string nsFlag = string.IsNullOrWhiteSpace(namespaceName) || namespaceName == "All Namespaces"
                ? "-A"
                : $"-n '{namespaceName}'";

            var routeTask = _ssh.RunCommandAsync(BuildKubeCmd($"get routes {nsFlag} -o json 2>/dev/null"));
            var ingTask = _ssh.RunCommandAsync(BuildKubeCmd($"get ingress {nsFlag} -o json 2>/dev/null"));
            await Task.WhenAll(routeTask, ingTask);

            // 1. Fetch OpenShift Routes
            var routeRes = await routeTask;
            if (routeRes.IsSuccess && !string.IsNullOrWhiteSpace(routeRes.Output))
            {
                try
                {
                    using var doc = JsonDocument.Parse(routeRes.Output);
                    if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in items.EnumerateArray())
                        {
                            var r = new IngressRouteItem { Kind = "Route" };
                            if (elem.TryGetProperty("metadata", out var meta))
                            {
                                if (meta.TryGetProperty("name", out var n)) r.Name = n.GetString() ?? "";
                                if (meta.TryGetProperty("namespace", out var ns)) r.Namespace = ns.GetString() ?? "";
                                if (meta.TryGetProperty("creationTimestamp", out var ct) && ct.TryGetDateTime(out var dt))
                                {
                                    r.CreatedAt = dt;
                                    r.AgeDisplay = FormatAge(dt);
                                }
                            }

                            if (elem.TryGetProperty("spec", out var spec))
                            {
                                if (spec.TryGetProperty("host", out var h)) r.Host = h.GetString() ?? "";
                                if (spec.TryGetProperty("path", out var p)) r.Path = p.GetString() ?? "/";
                                if (spec.TryGetProperty("to", out var to) && to.TryGetProperty("name", out var ton))
                                    r.ServiceName = ton.GetString() ?? "";
                                if (spec.TryGetProperty("port", out var port) && port.TryGetProperty("targetPort", out var tp))
                                {
                                    r.TargetPort = tp.ValueKind == JsonValueKind.Number
                                        ? tp.GetInt32().ToString()
                                        : (tp.GetString() ?? "");
                                }
                                if (spec.TryGetProperty("tls", out var tls) && tls.ValueKind == JsonValueKind.Object)
                                {
                                    if (tls.TryGetProperty("termination", out var term))
                                        r.TlsTermination = term.GetString() ?? "Edge";
                                }
                            }

                            if (elem.TryGetProperty("status", out var status) &&
                                status.TryGetProperty("ingress", out var ings) &&
                                ings.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var ing in ings.EnumerateArray())
                                {
                                    if (ing.TryGetProperty("conditions", out var conds) && conds.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var c in conds.EnumerateArray())
                                        {
                                            if (c.TryGetProperty("type", out var ct) && ct.GetString() == "Admitted")
                                            {
                                                if (c.TryGetProperty("status", out var cs) && cs.GetString() == "True")
                                                    r.Status = "Admitted";
                                            }
                                        }
                                    }
                                }
                            }

                            list.Add(r);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ClusterService.GetIngressRoutesAsync", "Failed to parse OpenShift routes JSON output.", ex);
                }
            }

            // 2. Fetch Kubernetes Ingresses
            var ingRes = await ingTask;
            if (ingRes.IsSuccess && !string.IsNullOrWhiteSpace(ingRes.Output))
            {
                try
                {
                    using var doc = JsonDocument.Parse(ingRes.Output);
                    if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in items.EnumerateArray())
                        {
                            var ing = new IngressRouteItem { Kind = "Ingress" };
                            if (elem.TryGetProperty("metadata", out var meta))
                            {
                                if (meta.TryGetProperty("name", out var n)) ing.Name = n.GetString() ?? "";
                                if (meta.TryGetProperty("namespace", out var ns)) ing.Namespace = ns.GetString() ?? "";
                                if (meta.TryGetProperty("creationTimestamp", out var ct) && ct.TryGetDateTime(out var dt))
                                {
                                    ing.CreatedAt = dt;
                                    ing.AgeDisplay = FormatAge(dt);
                                }
                            }

                            if (elem.TryGetProperty("spec", out var spec))
                            {
                                if (spec.TryGetProperty("tls", out var tls) && tls.ValueKind == JsonValueKind.Array)
                                {
                                    ing.TlsTermination = "TLS Secret";
                                }

                                if (spec.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var rule in rules.EnumerateArray())
                                    {
                                        if (rule.TryGetProperty("host", out var h)) ing.Host = h.GetString() ?? "";
                                        if (rule.TryGetProperty("http", out var http) &&
                                            http.TryGetProperty("paths", out var paths) &&
                                            paths.ValueKind == JsonValueKind.Array)
                                        {
                                            foreach (var pElem in paths.EnumerateArray())
                                            {
                                                if (pElem.TryGetProperty("path", out var p)) ing.Path = p.GetString() ?? "/";
                                                if (pElem.TryGetProperty("backend", out var b) &&
                                                    b.TryGetProperty("service", out var svc))
                                                {
                                                    if (svc.TryGetProperty("name", out var sn)) ing.ServiceName = sn.GetString() ?? "";
                                                    if (svc.TryGetProperty("port", out var sp))
                                                    {
                                                        if (sp.TryGetProperty("number", out var num) && num.ValueKind == JsonValueKind.Number)
                                                            ing.TargetPort = num.GetInt32().ToString();
                                                        else if (sp.TryGetProperty("name", out var pName))
                                                            ing.TargetPort = pName.GetString() ?? "";
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            // Avoid duplicate if Route exists with same name & namespace
                            if (!list.Exists(x => x.Name == ing.Name && x.Namespace == ing.Namespace))
                            {
                                list.Add(ing);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ClusterService.GetIngressRoutesAsync", "Failed to parse Kubernetes ingresses JSON output.", ex);
                }
            }

            return list;
        }

        public async Task<string> GetIngressRouteYamlAsync(string name, string ns, string kind)
        {
            string cmd = kind == "Ingress" ? "ingress" : "route";
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"get {cmd} '{name}' -n '{ns}' -o yaml"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<string> DescribeIngressRouteAsync(string name, string ns, string kind)
        {
            string cmd = kind == "Ingress" ? "ingress" : "route";
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"describe {cmd} '{name}' -n '{ns}'"));
            return res.IsSuccess ? res.Output : res.Error;
        }

        public async Task<bool> DeleteIngressRouteAsync(string name, string ns, string kind)
        {
            string cmd = kind == "Ingress" ? "ingress" : "route";
            var res = await _ssh.RunCommandAsync(BuildKubeCmd($"delete {cmd} '{name}' -n '{ns}'"));
            return res.IsSuccess;
        }

        public async Task<RouterStatusInfo> GetRouterStatusAsync()
        {
            var info = new RouterStatusInfo();

            var podsTask = _ssh.RunCommandAsync(BuildKubeCmd("get pods -n openshift-ingress -o json 2>/dev/null"));
            string hostCheckCmd =
                "echo \"SVC:$(systemctl is-active microshift 2>/dev/null || systemctl is-active crio 2>/dev/null)\"; " +
                "echo '---PORTS---'; " +
                "(ss -tulpn 2>/dev/null || netstat -tlpn 2>/dev/null); " +
                "echo '---FW---'; " +
                "(firewall-cmd --state 2>/dev/null && firewall-cmd --list-services 2>/dev/null)";
            var hostCheckTask = _ssh.RunCommandAsync(hostCheckCmd);

            await Task.WhenAll(podsTask, hostCheckTask);

            // 1. Check Router Pods
            var podsRes = await podsTask;
            if (podsRes.IsSuccess && !string.IsNullOrWhiteSpace(podsRes.Output))
            {
                try
                {
                    using var doc = JsonDocument.Parse(podsRes.Output);
                    if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        int running = 0;
                        int total = 0;
                        foreach (var pod in items.EnumerateArray())
                        {
                            total++;
                            if (pod.TryGetProperty("status", out var st) &&
                                st.TryGetProperty("phase", out var ph) &&
                                ph.GetString() == "Running")
                            {
                                running++;
                            }
                        }
                        if (total > 0)
                        {
                            info.IsRouterRunning = running > 0;
                            info.RouterPodsStatus = $"Running ({running}/{total} pods)";
                        }
                    }
                }
                catch { }
            }

            var hostRes = await hostCheckTask;
            string hostOutput = hostRes.Output ?? "";

            if (!info.IsRouterRunning && hostOutput.Contains("SVC:active"))
            {
                info.IsRouterRunning = true;
                info.RouterPodsStatus = "MicroShift Embedded Router Active";
            }

            // 2. Check Ports 80 and 443
            int portsIdx = hostOutput.IndexOf("---PORTS---", StringComparison.Ordinal);
            int fwIdx = hostOutput.IndexOf("---FW---", StringComparison.Ordinal);
            string portsSection = (portsIdx >= 0 && fwIdx > portsIdx)
                ? hostOutput.Substring(portsIdx, fwIdx - portsIdx)
                : hostOutput;

            info.IsHttpListening = portsSection.Contains(":80 ") || portsSection.Contains(":80\t") || portsSection.Contains("*:80");
            info.IsHttpsListening = portsSection.Contains(":443 ") || portsSection.Contains(":443\t") || portsSection.Contains("*:443");

            // 3. Check Firewall
            string fwSection = fwIdx >= 0 ? hostOutput.Substring(fwIdx) : "";
            if (fwSection.Contains("running"))
            {
                bool hasHttp = fwSection.Contains("http");
                bool hasHttps = fwSection.Contains("https");
                info.FirewallStatus = (hasHttp && hasHttps) ? "Active (http/https allowed)" : "Active (Check ports 80/443 in firewalld)";
            }
            else
            {
                info.FirewallStatus = "Firewall Inactive or permissive";
            }

            info.NodeDnsAdvice = "Map Route hostnames to this node IP in your Windows C:\\Windows\\System32\\drivers\\etc\\hosts";
            return info;
        }

        // Containers & Docker / Podman Images
        public async Task<List<ContainerItem>> GetContainersAsync(bool all = true)
        {
            var list = new List<ContainerItem>();
            string allFlag = all ? "-a" : "";

            if (_containerRuntime == "podman")
            {
                var res = await _ssh.RunCommandAsync($"podman ps {allFlag} --format json");
                if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(res.Output);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in doc.RootElement.EnumerateArray())
                            {
                                var c = new ContainerItem();
                                if (elem.TryGetProperty("Id", out var id)) c.Id = id.GetString() ?? "";
                                if (elem.TryGetProperty("Image", out var img)) c.Image = img.GetString() ?? "";
                                if (elem.TryGetProperty("State", out var st)) c.State = st.GetString() ?? "";
                                if (elem.TryGetProperty("Status", out var sts)) c.Status = sts.GetString() ?? "";
                                if (elem.TryGetProperty("Created", out var cr))
                                {
                                    c.Created = cr.ValueKind == JsonValueKind.Number
                                        ? DateTimeOffset.FromUnixTimeSeconds(cr.GetInt64()).ToString("yyyy-MM-dd HH:mm")
                                        : (cr.GetString() ?? "");
                                }

                                if (elem.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array)
                                {
                                    var nList = new List<string>();
                                    foreach (var n in names.EnumerateArray()) nList.Add(n.GetString() ?? "");
                                    c.Name = string.Join(", ", nList);
                                }
                                else if (elem.TryGetProperty("Names", out var nameStr) && nameStr.ValueKind == JsonValueKind.String)
                                {
                                    c.Name = nameStr.GetString() ?? "";
                                }
                                list.Add(c);
                            }
                            return list;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("ClusterService.GetContainersAsync", "Failed to parse podman ps JSON output.", ex);
                    }
                }
            }
            else if (_containerRuntime == "crictl")
            {
                var res = await _ssh.RunCommandAsync($"crictl ps {allFlag} -o json");
                if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(res.Output);
                        if (doc.RootElement.TryGetProperty("containers", out var containers) && containers.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in containers.EnumerateArray())
                            {
                                var c = new ContainerItem();
                                if (elem.TryGetProperty("id", out var id)) c.Id = id.GetString() ?? "";
                                if (elem.TryGetProperty("image", out var img) && img.TryGetProperty("image", out var imgRef))
                                    c.Image = imgRef.GetString() ?? "";
                                if (elem.TryGetProperty("state", out var st)) c.State = st.GetString() ?? "";
                                if (elem.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("name", out var nm))
                                    c.Name = nm.GetString() ?? "";
                                list.Add(c);
                            }
                            return list;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("ClusterService.GetContainersAsync", "Failed to parse crictl ps JSON output.", ex);
                    }
                }
            }

            // Docker / Generic Docker ps fallback
            var dockerRes = await _ssh.RunCommandAsync($"{_containerRuntime} ps {allFlag} --format '{{{{.ID}}}}|{{{{.Names}}}}|{{{{.Image}}}}|{{{{.State}}}}|{{{{.Status}}}}|{{{{.Ports}}}}'");
            if (dockerRes.IsSuccess)
            {
                var lines = dockerRes.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4)
                    {
                        list.Add(new ContainerItem
                        {
                            Id = parts[0].Trim(),
                            Name = parts[1].Trim(),
                            Image = parts[2].Trim(),
                            State = parts[3].Trim(),
                            Status = parts.Length > 4 ? parts[4].Trim() : "",
                            Ports = parts.Length > 5 ? parts[5].Trim() : ""
                        });
                    }
                }
            }

            return list;
        }

        public async Task<string> GetContainerLogsAsync(string containerId, int lines = 200)
        {
            var res = await _ssh.RunCommandAsync($"{_containerRuntime} logs --tail {lines} '{containerId}'");
            return res.IsSuccess ? res.Output : (string.IsNullOrEmpty(res.Error) ? res.Output : res.Error);
        }

        public async Task<bool> StartContainerAsync(string containerId)
        {
            var res = await _ssh.RunCommandAsync($"{_containerRuntime} start '{containerId}'");
            return res.IsSuccess;
        }

        public async Task<bool> StopContainerAsync(string containerId)
        {
            var res = await _ssh.RunCommandAsync($"{_containerRuntime} stop '{containerId}'");
            return res.IsSuccess;
        }

        public async Task<bool> RestartContainerAsync(string containerId)
        {
            var res = await _ssh.RunCommandAsync($"{_containerRuntime} restart '{containerId}'");
            return res.IsSuccess;
        }

        public async Task<bool> RemoveContainerAsync(string containerId, bool force = true)
        {
            string flag = force ? "-f" : "";
            var res = await _ssh.RunCommandAsync($"{_containerRuntime} rm {flag} '{containerId}'");
            return res.IsSuccess;
        }

        public async Task<List<DockerImageItem>> GetImagesAsync()
        {
            var list = new List<DockerImageItem>();

            if (_containerRuntime == "podman")
            {
                var res = await _ssh.RunCommandAsync("podman images --format json");
                if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(res.Output);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in doc.RootElement.EnumerateArray())
                            {
                                var img = new DockerImageItem();
                                if (elem.TryGetProperty("Id", out var id)) img.Id = id.GetString() ?? "";
                                if (elem.TryGetProperty("Size", out var sz))
                                {
                                    if (sz.ValueKind == JsonValueKind.Number)
                                        img.Size = $"{sz.GetInt64() / (1024 * 1024)} MB";
                                    else
                                        img.Size = sz.GetString() ?? "";
                                }
                                if (elem.TryGetProperty("Created", out var cr))
                                {
                                    img.Created = cr.ValueKind == JsonValueKind.Number
                                        ? DateTimeOffset.FromUnixTimeSeconds(cr.GetInt64()).ToString("yyyy-MM-dd HH:mm")
                                        : (cr.GetString() ?? "");
                                }

                                if (elem.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var n in names.EnumerateArray())
                                    {
                                        string fullName = n.GetString() ?? "";
                                        int colonIdx = fullName.LastIndexOf(':');
                                        if (colonIdx > 0 && colonIdx > fullName.LastIndexOf('/'))
                                        {
                                            img.Repository = fullName.Substring(0, colonIdx);
                                            img.Tag = fullName.Substring(colonIdx + 1);
                                        }
                                        else
                                        {
                                            img.Repository = fullName;
                                            img.Tag = "latest";
                                        }
                                        break;
                                    }
                                }
                                else if (elem.TryGetProperty("RepoTags", out var repoTags) && repoTags.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var rt in repoTags.EnumerateArray())
                                    {
                                        string fullName = rt.GetString() ?? "";
                                        int colonIdx = fullName.LastIndexOf(':');
                                        if (colonIdx > 0 && colonIdx > fullName.LastIndexOf('/'))
                                        {
                                            img.Repository = fullName.Substring(0, colonIdx);
                                            img.Tag = fullName.Substring(colonIdx + 1);
                                        }
                                        else
                                        {
                                            img.Repository = fullName;
                                            img.Tag = "latest";
                                        }
                                        break;
                                    }
                                }
                                list.Add(img);
                            }
                            return list;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("ClusterService.GetImagesAsync", "Failed to parse podman images JSON output.", ex);
                    }
                }
            }
            else if (_containerRuntime == "crictl")
            {
                var res = await _ssh.RunCommandAsync("crictl images -o json");
                if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.Output))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(res.Output);
                        if (doc.RootElement.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in images.EnumerateArray())
                            {
                                var img = new DockerImageItem();
                                if (elem.TryGetProperty("id", out var id)) img.Id = id.GetString() ?? "";
                                if (elem.TryGetProperty("size", out var sz))
                                {
                                    if (sz.ValueKind == JsonValueKind.String && long.TryParse(sz.GetString(), out long bytes))
                                        img.Size = $"{bytes / (1024 * 1024)} MB";
                                    else if (sz.ValueKind == JsonValueKind.Number)
                                        img.Size = $"{sz.GetInt64() / (1024 * 1024)} MB";
                                }
                                if (elem.TryGetProperty("repoTags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var t in tags.EnumerateArray())
                                    {
                                        string fullName = t.GetString() ?? "";
                                        int colonIdx = fullName.LastIndexOf(':');
                                        if (colonIdx > 0 && colonIdx > fullName.LastIndexOf('/'))
                                        {
                                            img.Repository = fullName.Substring(0, colonIdx);
                                            img.Tag = fullName.Substring(colonIdx + 1);
                                        }
                                        else
                                        {
                                            img.Repository = fullName;
                                            img.Tag = "latest";
                                        }
                                        break;
                                    }
                                }
                                list.Add(img);
                            }
                            return list;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("ClusterService.GetImagesAsync", "Failed to parse crictl images JSON output.", ex);
                    }
                }
            }

            // Docker / Crictl fallback
            var rawRes = await _ssh.RunCommandAsync($"{_containerRuntime} images --format '{{{{.Repository}}}}|{{{{.Tag}}}}|{{{{.ID}}}}|{{{{.CreatedAt}}}}|{{{{.Size}}}}'");
            if (rawRes.IsSuccess)
            {
                var lines = rawRes.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 5)
                    {
                        list.Add(new DockerImageItem
                        {
                            Repository = parts[0].Trim(),
                            Tag = parts[1].Trim(),
                            Id = parts[2].Trim(),
                            Created = parts[3].Trim(),
                            Size = parts[4].Trim()
                        });
                    }
                }
            }

            return list;
        }

        public async Task<bool> DeleteImageAsync(string imageIdOrName, bool force = false)
        {
            string flag = force ? "-f" : "";
            var res = await _ssh.RunCommandAsync($"{_containerRuntime} rmi {flag} '{imageIdOrName}'");
            return res.IsSuccess;
        }

        public async Task<bool> PullImageAsync(string imageName)
        {
            var res = await _ssh.RunCommandAsync($"{_containerRuntime} pull '{imageName}'", timeoutSeconds: 180);
            return res.IsSuccess;
        }

        private static string FormatAge(DateTime createdAt)
        {
            var span = DateTime.UtcNow - createdAt.ToUniversalTime();
            if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
            return $"{(int)span.TotalDays}d";
        }
    }
}
