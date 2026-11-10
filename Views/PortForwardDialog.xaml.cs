using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using KubeUIApp.Models;
using KubeUIApp.Services;

namespace KubeUIApp.Views
{
    public partial class PortForwardDialog : Window
    {
        private readonly PodItem _pod;
        private readonly ISshService _sshService;
        private IDisposable? _activeTunnel;
        private int _currentLocalPort;

        public PortForwardDialog(PodItem pod, ISshService sshService)
        {
            InitializeComponent();
            _pod = pod;
            _sshService = sshService;

            TxtPodInfo.Text = $"{pod.Name} ({pod.Namespace})";
            TxtPodIp.Text = string.IsNullOrEmpty(pod.PodIP) || pod.PodIP == "-" ? "127.0.0.1 (via localhost)" : pod.PodIP;

            // Pick a reasonable default port
            TxtLocalPort.Text = GetAvailableLocalPort(8080).ToString();

            Closed += (s, e) => StopTunnel();
        }

        private void OnRandomPortClick(object sender, RoutedEventArgs e)
        {
            TxtLocalPort.Text = GetAvailableLocalPort(new Random().Next(8000, 9999)).ToString();
        }

        private void OnToggleTunnelClick(object sender, RoutedEventArgs e)
        {
            if (_activeTunnel != null)
            {
                StopTunnel();
            }
            else
            {
                StartTunnel();
            }
        }

        private void StartTunnel()
        {
            if (!int.TryParse(TxtContainerPort.Text.Trim(), out int remotePort) || remotePort <= 0 || remotePort > 65535)
            {
                MessageBox.Show("Please enter a valid container port (1-65535).", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtLocalPort.Text.Trim(), out int localPort) || localPort <= 0 || localPort > 65535)
            {
                MessageBox.Show("Please enter a valid local port (1-65535).", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string targetHost = !string.IsNullOrEmpty(_pod.PodIP) && _pod.PodIP != "-" ? _pod.PodIP : "127.0.0.1";
                _activeTunnel = _sshService.StartPortForward(targetHost, (uint)remotePort, (uint)localPort);
                _currentLocalPort = localPort;

                TxtTunnelStatus.Text = $"🟢 Tunnel Active -> 127.0.0.1:{localPort} \u2192 {targetHost}:{remotePort}";
                TxtTunnelStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentSuccessBrush");
                TxtTunnelUrl.Text = $"http://localhost:{localPort}";
                TxtTunnelUrl.Visibility = Visibility.Visible;

                BtnToggleTunnel.Content = "⏹️ Stop Tunnel";
                BtnOpenBrowser.IsEnabled = true;
                BtnCopyUrl.IsEnabled = true;
                TxtContainerPort.IsEnabled = false;
                TxtLocalPort.IsEnabled = false;
            }
            catch (Exception ex)
            {
                StopTunnel();
                MessageBox.Show($"Failed to start port forwarding tunnel:\n{ex.Message}", "Port Forward Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopTunnel()
        {
            if (_activeTunnel != null)
            {
                try { _activeTunnel.Dispose(); } catch { }
                _activeTunnel = null;
            }

            TxtTunnelStatus.Text = "⚪ Tunnel Stopped";
            TxtTunnelStatus.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            TxtTunnelUrl.Visibility = Visibility.Collapsed;

            BtnToggleTunnel.Content = "⚡ Start Tunnel";
            BtnOpenBrowser.IsEnabled = false;
            BtnCopyUrl.IsEnabled = false;
            TxtContainerPort.IsEnabled = true;
            TxtLocalPort.IsEnabled = true;
        }

        private void OnOpenBrowserClick(object sender, RoutedEventArgs e)
        {
            if (_activeTunnel == null) return;
            try
            {
                Process.Start(new ProcessStartInfo($"http://localhost:{_currentLocalPort}") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnCopyUrlClick(object sender, RoutedEventArgs e)
        {
            if (_activeTunnel == null) return;
            Clipboard.SetText($"http://localhost:{_currentLocalPort}");
            MessageBox.Show($"Copied http://localhost:{_currentLocalPort} to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static int GetAvailableLocalPort(int startingPort)
        {
            for (int p = startingPort; p < 65535; p++)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, p);
                    listener.Start();
                    listener.Stop();
                    return p;
                }
                catch
                {
                    // port in use, test next
                }
            }
            return startingPort;
        }
    }
}