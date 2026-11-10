using System;
using System.Collections.Generic;
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
    public partial class PodFileExplorerDialog : Window
    {
        private readonly IClusterService _clusterService;
        private readonly string _podName;
        private readonly string _namespace;
        private string _currentPath = "/";
        private List<RemoteFileInfo> _files = new();

        public PodFileExplorerDialog(PodItem pod, IClusterService clusterService)
            : this(pod.Name, pod.Namespace, pod.Containers, clusterService)
        {
        }

        public PodFileExplorerDialog(string podName, string ns, List<string> containers, IClusterService clusterService)
        {
            InitializeComponent();
            _podName = podName;
            _namespace = ns;
            _clusterService = clusterService;

            TxtPodName.Text = podName;
            TxtNamespace.Text = $"({ns})";

            foreach (var c in containers)
            {
                CmbContainers.Items.Add(c);
            }
            if (CmbContainers.Items.Count > 0)
            {
                CmbContainers.SelectedIndex = 0;
            }

            Loaded += async (s, e) => await LoadFilesAsync("/");
        }

        private string CurrentContainer => CmbContainers.SelectedItem?.ToString() ?? "";

        private async Task LoadFilesAsync(string targetPath)
        {
            try
            {
                TxtStatus.Text = "Loading pod files...";
                _currentPath = string.IsNullOrWhiteSpace(targetPath) ? "/" : targetPath;
                TxtCurrentPath.Text = _currentPath;

                _files = await _clusterService.ListPodFilesDetailedAsync(_podName, _namespace, CurrentContainer, _currentPath);
                DgFiles.ItemsSource = null;
                DgFiles.ItemsSource = _files;
                TxtStatus.Text = $"{_files.Count} items loaded";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Failed to load files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnContainerChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                await LoadFilesAsync(_currentPath);
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadFilesAsync(_currentPath);
        }

        private async void OnRootClick(object sender, RoutedEventArgs e)
        {
            await LoadFilesAsync("/");
        }

        private async void OnUpClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath) || _currentPath == "/") return;
            int idx = _currentPath.TrimEnd('/').LastIndexOf('/');
            string parentPath = idx <= 0 ? "/" : _currentPath.Substring(0, idx);
            await LoadFilesAsync(parentPath);
        }

        private async void OnGoClick(object sender, RoutedEventArgs e)
        {
            await LoadFilesAsync(TxtCurrentPath.Text.Trim());
        }

        private async void OnPathKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await LoadFilesAsync(TxtCurrentPath.Text.Trim());
            }
        }

        private async void OnFilesDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgFiles.SelectedItem is RemoteFileInfo item)
            {
                if (item.IsDirectory)
                {
                    await LoadFilesAsync(item.FullPath);
                }
                else
                {
                    await ViewSelectedFileAsync(item);
                }
            }
        }

        private async void OnViewFileClick(object sender, RoutedEventArgs e)
        {
            if (DgFiles.SelectedItem is RemoteFileInfo item)
            {
                if (item.IsDirectory)
                {
                    await LoadFilesAsync(item.FullPath);
                }
                else
                {
                    await ViewSelectedFileAsync(item);
                }
            }
            else
            {
                MessageBox.Show("Please select a file to view.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task ViewSelectedFileAsync(RemoteFileInfo item)
        {
            try
            {
                TxtStatus.Text = $"Reading {item.Name}...";
                string content = await _clusterService.ReadPodFileAsync(_podName, _namespace, CurrentContainer, item.FullPath);
                var dlg = new FileViewerDialog(item.FullPath, content)
                {
                    Owner = this
                };
                dlg.ShowDialog();
                TxtStatus.Text = "Ready";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to read file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnCopyToWindowsClick(object sender, RoutedEventArgs e)
        {
            if (DgFiles.SelectedItem is RemoteFileInfo item)
            {
                if (item.IsDirectory)
                {
                    MessageBox.Show("Please select an individual file to copy to Windows.", "File Required", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    FileName = item.Name,
                    Filter = "All Files (*.*)|*.*",
                    Title = $"Copy {item.Name} from Pod to Windows"
                };

                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        TxtStatus.Text = $"Copying {item.Name} to Windows...";
                        await _clusterService.CopyPodFileToWindowsAsync(_podName, _namespace, CurrentContainer, item.FullPath, dlg.FileName);
                        TxtStatus.Text = "Copy completed!";
                        MessageBox.Show($"File copied successfully to Windows:\n{dlg.FileName}", "Copy Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        TxtStatus.Text = "Copy failed";
                        MessageBox.Show($"Copy failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a file to copy to Windows.", "Select File", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void OnUploadToPodClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*",
                Title = "Select File to Copy into Pod"
            };

            if (dlg.ShowDialog() == true)
            {
                string fileName = Path.GetFileName(dlg.FileName);
                string remoteDest = _currentPath.TrimEnd('/') + "/" + fileName;

                try
                {
                    TxtStatus.Text = $"Copying {fileName} into Pod...";
                    await _clusterService.CopyWindowsFileToPodAsync(_podName, _namespace, CurrentContainer, dlg.FileName, remoteDest);
                    TxtStatus.Text = "File copied to Pod!";
                    MessageBox.Show($"File '{fileName}' successfully copied into pod at:\n{remoteDest}", "Upload Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadFilesAsync(_currentPath);
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = "Upload failed";
                    MessageBox.Show($"Failed to copy file into Pod: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            if (DgFiles.SelectedItem is RemoteFileInfo item)
            {
                Clipboard.SetText(item.FullPath);
                MessageBox.Show($"Path copied to clipboard:\n{item.FullPath}", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Clipboard.SetText(_currentPath);
                MessageBox.Show($"Folder path copied to clipboard:\n{_currentPath}", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
