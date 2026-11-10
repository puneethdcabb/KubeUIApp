using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using KubeUIApp.Models;
using KubeUIApp.Services;
using Microsoft.Win32;

namespace KubeUIApp.Views
{
    public partial class SecretViewerDialog : Window
    {
        private readonly SecretItem _secret;
        private readonly IClusterService _clusterService;
        private List<SecretDataEntry> _entries = new();

        public SecretViewerDialog(SecretItem secret, IClusterService clusterService)
        {
            InitializeComponent();
            _secret = secret;
            _clusterService = clusterService;

            TxtSecretName.Text = secret.Name;
            TxtNamespace.Text = $"({secret.Namespace})";
            TxtSecretType.Text = secret.Type;

            Loaded += async (s, e) => await LoadSecretDataAsync();
        }

        private async Task LoadSecretDataAsync()
        {
            TxtStatus.Text = "Loading secret data from cluster...";
            try
            {
                _entries = await _clusterService.GetSecretDataEntriesAsync(_secret.Name, _secret.Namespace);
                DgKeys.ItemsSource = _entries;
                TxtKeysCount.Text = $"{_entries.Count} entries";
                TxtStatus.Text = $"Loaded {_entries.Count} secret data keys.";

                if (_entries.Count > 0)
                {
                    DgKeys.SelectedIndex = 0;
                }
                else
                {
                    TxtValue.Text = "(Secret contains no data entries)";
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Failed to load secret data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private SecretDataEntry? SelectedEntry => DgKeys?.SelectedItem as SecretDataEntry;

        private void OnKeySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateValueDisplay();
        }

        private void OnViewModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateValueDisplay();
        }

        private void UpdateValueDisplay()
        {
            if (TxtValue == null || TxtStatus == null)
            {
                return;
            }

            if (SelectedEntry == null)
            {
                TxtValue.Text = "";
                return;
            }

            bool showDecoded = RbDecoded?.IsChecked == true;
            if (showDecoded)
            {
                TxtValue.Text = SelectedEntry.DecodedText;
                TxtStatus.Text = $"Key: '{SelectedEntry.Key}' ({SelectedEntry.DetectedType}) - Showing Decoded Text ({SelectedEntry.ByteLength} bytes)";
            }
            else
            {
                TxtValue.Text = SelectedEntry.Base64Value;
                TxtStatus.Text = $"Key: '{SelectedEntry.Key}' - Showing Raw Base64 String ({SelectedEntry.Base64Value.Length} chars)";
            }
        }

        private void OnCopyDecodedClick(object sender, RoutedEventArgs e)
        {
            if (SelectedEntry == null) return;
            Clipboard.SetText(SelectedEntry.DecodedText);
            MessageBox.Show($"Decoded content of '{SelectedEntry.Key}' copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnCopyBase64Click(object sender, RoutedEventArgs e)
        {
            if (SelectedEntry == null) return;
            Clipboard.SetText(SelectedEntry.Base64Value);
            MessageBox.Show($"Base64 string of '{SelectedEntry.Key}' copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnSaveKeyClick(object sender, RoutedEventArgs e)
        {
            if (SelectedEntry == null) return;

            var sfd = new SaveFileDialog
            {
                FileName = SelectedEntry.Key,
                Filter = "All Files (*.*)|*.*|Certificate Files (*.crt;*.cer;*.pem)|*.crt;*.cer;*.pem|CRL Files (*.crl)|*.crl|Text Files (*.txt)|*.txt"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(SelectedEntry.Base64Value);
                    File.WriteAllBytes(sfd.FileName, bytes);
                    MessageBox.Show($"Key '{SelectedEntry.Key}' saved successfully to:\n{sfd.FileName}", "Saved to Windows", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void OnViewFullYamlClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string yaml = await _clusterService.GetSecretYamlAsync(_secret.Name, _secret.Namespace);
                var dlg = new YamlViewerDialog($"Secret YAML - {_secret.Namespace}/{_secret.Name}", yaml)
                {
                    Owner = this
                };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load YAML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnDescribeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string desc = await _clusterService.DescribeSecretAsync(_secret.Name, _secret.Namespace);
                var dlg = new YamlViewerDialog($"Describe Secret - {_secret.Namespace}/{_secret.Name}", desc)
                {
                    Owner = this
                };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to describe secret: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
