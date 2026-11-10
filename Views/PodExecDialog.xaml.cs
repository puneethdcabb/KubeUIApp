using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace KubeUIApp.Views
{
    public partial class PodExecDialog : Window
    {
        private readonly Func<string, string, Task<string>> _execRunner;

        public PodExecDialog(string podName, string ns, List<string> containers, Func<string, string, Task<string>> execRunner)
        {
            InitializeComponent();
            TxtTitle.Text = $"Exec: {podName} ({ns})";
            _execRunner = execRunner;

            foreach (var c in containers)
            {
                CmbContainers.Items.Add(c);
            }
            if (CmbContainers.Items.Count > 0)
            {
                CmbContainers.SelectedIndex = 0;
            }
        }

        private async void OnRunClick(object sender, RoutedEventArgs e)
        {
            await ExecuteCommandAsync();
        }

        private async void OnCommandKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ExecuteCommandAsync();
            }
        }

        private async Task ExecuteCommandAsync()
        {
            string cmd = TxtCommand.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            string container = CmbContainers.SelectedItem?.ToString() ?? "";
            TxtStatus.Text = "Running command in pod...";
            TxtOutput.Text += $"\n$ {cmd}\n";

            try
            {
                var output = await _execRunner(container, cmd);
                TxtOutput.Text += output + "\n";
                TxtOutput.ScrollToEnd();
                TxtStatus.Text = $"Completed at {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                TxtOutput.Text += $"[Error] {ex.Message}\n";
                TxtStatus.Text = "Error during execution";
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
