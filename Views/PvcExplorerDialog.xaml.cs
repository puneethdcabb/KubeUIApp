using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using KubeUIApp.Models;
using KubeUIApp.Services;
using Microsoft.Win32;

namespace KubeUIApp.Views
{
    public partial class PvcExplorerDialog : Window
    {
        private readonly IClusterService _clusterService;
        private readonly string _pvcName;
        private readonly string _namespace;
        private string _rootMountPath = "";
        private string _currentPath = "";
        private List<RemoteFileInfo> _files = new();

        public PvcExplorerDialog(PvcItem pvc, IClusterService clusterService)
            : this(pvc.Name, pvc.Namespace, clusterService)
        {
        }

        public PvcExplorerDialog(string pvcName, string ns, IClusterService clusterService)
        {
            InitializeComponent();
            _pvcName = pvcName;
            _namespace = ns;
            _clusterService = clusterService;

            TxtPvcName.Text = pvcName;
            TxtNamespace.Text = $"({ns})";

            Loaded += async (s, e) => await LoadFilesAsync(null);
        }

        private async Task LoadFilesAsync(string? targetPath)
        {
            try
            {
                TxtStatus.Text = "Loading PVC files...";
                var (files, resolvedPath, mountInfo) = await _clusterService.ListPvcFilesAsync(_pvcName, _namespace, targetPath);
                _files = files;
                _currentPath = resolvedPath;
                if (string.IsNullOrEmpty(_rootMountPath))
                {
                    _rootMountPath = resolvedPath;
                }

                TxtMountInfo.Text = mountInfo.SourceDescription;
                TxtCurrentPath.Text = _currentPath;
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

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadFilesAsync(_currentPath);
        }

        private async void OnRootClick(object sender, RoutedEventArgs e)
        {
            await LoadFilesAsync(_rootMountPath);
        }

        private async void OnUpClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath) || _currentPath == "/" || _currentPath == _rootMountPath)
            {
                return;
            }
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
                string content = await _clusterService.ReadPvcFileAsync(_pvcName, _namespace, item.FullPath);
                var dlg = new FileViewerDialog(
                    item.FullPath,
                    content,
                    customSaveHandler: async destinationPath =>
                    {
                        await _clusterService.CopyPvcFileToWindowsAsync(_pvcName, _namespace, item.FullPath, destinationPath);
                    })
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
                    Title = $"Copy {item.Name} from PVC to Windows"
                };

                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        TxtStatus.Text = $"Copying {item.Name} to Windows...";
                        await _clusterService.CopyPvcFileToWindowsAsync(_pvcName, _namespace, item.FullPath, dlg.FileName);
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
                MessageBox.Show("Please select a file from the list to copy to Windows.", "Select File", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void OnUploadToPvcClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*",
                Title = "Select File to Copy into PVC"
            };

            if (dlg.ShowDialog() == true)
            {
                string fileName = Path.GetFileName(dlg.FileName);
                string remoteDest = _currentPath.TrimEnd('/') + "/" + fileName;

                try
                {
                    TxtStatus.Text = $"Copying {fileName} into PVC...";
                    await _clusterService.CopyWindowsFileToPvcAsync(_pvcName, _namespace, dlg.FileName, remoteDest);
                    TxtStatus.Text = "File copied to PVC!";
                    MessageBox.Show($"File '{fileName}' successfully copied into PVC at:\n{remoteDest}", "Upload Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadFilesAsync(_currentPath);
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = "Upload failed";
                    MessageBox.Show($"Failed to copy file into PVC: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void OnDeleteFileClick(object sender, RoutedEventArgs e)
        {
            if (DgFiles.SelectedItem is RemoteFileInfo item)
            {
                if (item.IsDirectory)
                {
                    MessageBox.Show("Deleting folders from PVC explorer is not supported. Please select an individual file to delete.", "File Required", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show(
                    $"Are you sure you want to permanently delete the file '{item.Name}' from this PVC?\n\nPath:\n{item.FullPath}",
                    "Confirm File Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                    return;

                try
                {
                    TxtStatus.Text = $"Deleting {item.Name}...";
                    await _clusterService.DeletePvcFileAsync(_pvcName, _namespace, item.FullPath);
                    TxtStatus.Text = "File deleted";
                    MessageBox.Show($"File '{item.Name}' deleted successfully from PVC.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadFilesAsync(_currentPath);
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = "Delete failed";
                    MessageBox.Show($"Failed to delete file from PVC: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Please select a file to delete from the list.", "Select File", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show($"Current folder path copied to clipboard:\n{_currentPath}", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
