using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KubeUIApp.Models;
using KubeUIApp.Services;
using Microsoft.Win32;

namespace KubeUIApp.Views
{
    public partial class YamlEditorDialog : Window
    {
        private readonly string _resourceName;
        private readonly string _namespaceName;
        private readonly IClusterService _clusterService;
        private readonly PodItem? _podItem;
        private int _lastMatchIndex = -1;
        public bool WasApplied { get; private set; }

        public YamlEditorDialog(string resourceName, string namespaceName, string initialYaml, IClusterService clusterService, PodItem? podItem = null)
        {
            InitializeComponent();
            _resourceName = resourceName;
            _namespaceName = namespaceName;
            _clusterService = clusterService;
            _podItem = podItem;

            TxtResourceTitle.Text = resourceName;
            TxtNamespace.Text = $"({namespaceName})";
            TxtEditor.Text = initialYaml;
            TxtStatus.Text = $"{initialYaml.Length} characters loaded. Edit manifest and click Apply.";
        }

        private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtEditor == null || TxtStatus == null) return;
            int lineCount = TxtEditor.LineCount;
            TxtStatus.Text = $"Lines: {lineCount} | Length: {TxtEditor.Text.Length} chars";
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _lastMatchIndex = -1;
            FindMatch(findNext: false);
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                FindMatch(findNext: true);
            }
        }

        private void FindMatch(bool findNext)
        {
            string query = TxtSearch.Text;
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(TxtEditor.Text))
            {
                TxtEditor.Select(0, 0);
                return;
            }

            int startIndex = (findNext && _lastMatchIndex >= 0 && _lastMatchIndex + query.Length < TxtEditor.Text.Length)
                ? _lastMatchIndex + query.Length
                : 0;

            int index = TxtEditor.Text.IndexOf(query, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0 && startIndex > 0)
            {
                index = TxtEditor.Text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
            }

            if (index >= 0)
            {
                _lastMatchIndex = index;
                TxtEditor.Select(index, query.Length);
                int lineIndex = TxtEditor.GetLineIndexFromCharacterIndex(index);
                if (lineIndex >= 0)
                {
                    TxtEditor.ScrollToLine(lineIndex);
                }
            }
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtEditor.Text);
            MessageBox.Show("YAML copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "YAML Files (*.yaml;*.yml)|*.yaml;*.yml|All Files (*.*)|*.*",
                FileName = $"{_resourceName.Replace("/", "_")}.yaml"
            };

            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, TxtEditor.Text);
                MessageBox.Show("File saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void OnApplyOnlyClick(object sender, RoutedEventArgs e)
        {
            await ExecuteApplyAsync(redeployAfterApply: false);
        }

        private async void OnApplyAndRedeployClick(object sender, RoutedEventArgs e)
        {
            await ExecuteApplyAsync(redeployAfterApply: true);
        }

        private async Task ExecuteApplyAsync(bool redeployAfterApply)
        {
            string yaml = TxtEditor.Text;
            if (string.IsNullOrWhiteSpace(yaml))
            {
                MessageBox.Show("YAML content cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnApplyAndRedeploy.IsEnabled = false;
            BtnApplyOnly.IsEnabled = false;
            TxtStatus.Text = "Applying YAML manifest to cluster...";

            try
            {
                var (success, message) = await _clusterService.ApplyYamlAsync(yaml, _namespaceName);
                if (success)
                {
                    WasApplied = true;

                    bool isNonDeployment = _resourceName.StartsWith("Secret/", StringComparison.OrdinalIgnoreCase) ||
                                           _resourceName.StartsWith("Route:", StringComparison.OrdinalIgnoreCase) ||
                                           _resourceName.StartsWith("Ingress:", StringComparison.OrdinalIgnoreCase);

                    if (redeployAfterApply && _podItem != null)
                    {
                        TxtStatus.Text = "YAML applied successfully! Triggering pod redeployment...";
                        await _clusterService.RedeployPodAsync(_podItem);
                        MessageBox.Show($"Pod '{_podItem.Name}' YAML was applied and redeployment triggered successfully!\n\nCluster response:\n{message}", "Applied & Redeployed", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (redeployAfterApply && !isNonDeployment)
                    {
                        string depName = _resourceName.StartsWith("Deployment/", StringComparison.OrdinalIgnoreCase)
                            ? _resourceName.Substring("Deployment/".Length).Trim()
                            : _resourceName.Trim();
                        TxtStatus.Text = "YAML applied successfully! Triggering rollout restart...";
                        await _clusterService.RestartDeploymentAsync(depName, _namespaceName);
                        MessageBox.Show($"Deployment '{depName}' YAML was applied and rollout restart triggered successfully!\n\nCluster response:\n{message}", "Applied & Redeployed", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        TxtStatus.Text = "YAML applied successfully!";
                        MessageBox.Show($"YAML applied successfully!\n\nCluster response:\n{message}", "Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    DialogResult = true;
                    Close();
                }
                else
                {
                    TxtStatus.Text = "Apply failed. See error dialog.";
                    MessageBox.Show($"Failed to apply YAML:\n\n{message}", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Unexpected error applying YAML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnApplyAndRedeploy.IsEnabled = true;
                BtnApplyOnly.IsEnabled = true;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
