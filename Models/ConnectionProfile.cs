using System;

namespace KubeUIApp.Models
{
    public class ConnectionProfile
    {
        public string ProfileName { get; set; } = "Default Node";
        public string Host { get; set; } = "192.168.1.100";
        public int Port { get; set; } = 22;
        public string Username { get; set; } = "root";
        public string Password { get; set; } = "";
        public string SudoPassword { get; set; } = "";
        public bool UseSudo { get; set; } = true;
        public string? SshKeyPath { get; set; }
        public string PreferredCli { get; set; } = "Auto"; // Auto, oc, kubectl
        public string PreferredContainerRuntime { get; set; } = "Auto"; // Auto, podman, crictl, docker
        public string CustomKubeconfig { get; set; } = ""; // e.g. /var/lib/microshift/resources/kubeadmin/kubeconfig
    }
}
