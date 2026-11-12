using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KubeUIApp.Models;
using KubeUIApp.ViewModels;
using Microsoft.Win32;

namespace KubeUIApp
{
    public partial class MainWindow : Window
    {
        private MainViewModel? ViewModel => DataContext as MainViewModel;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                // Sync initial password to PasswordBox if profile loaded with one
                if (!string.IsNullOrEmpty(ViewModel.Password))
                {
                    PbPassword.Password = ViewModel.Password;
                }
                if (!string.IsNullOrEmpty(ViewModel.SudoPassword))
                {
                    PbSudoPassword.Password = ViewModel.SudoPassword;
                }

                ViewModel.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.SelectedProfile) && ViewModel.SelectedProfile != null)
                    {
                        PbPassword.Password = ViewModel.SelectedProfile.Password ?? "";
                        PbSudoPassword.Password = ViewModel.SelectedProfile.SudoPassword ?? "";
                    }
                };
            }
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.Password = PbPassword.Password;
            }
        }

        private void OnSudoPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.SudoPassword = PbSudoPassword.Password;
            }
        }

        private void OnBrowseKeyClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Key Files (*.pem;*.key;id_rsa)|*.pem;*.key;id_rsa;*|All Files (*.*)|*.*",
                Title = "Select SSH Private Key"
            };

            if (dlg.ShowDialog() == true && ViewModel != null)
            {
                ViewModel.SshKeyPath = dlg.FileName;
            }
        }

        private void OnPodDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedPod != null && ViewModel.ViewPodLogsCommand.CanExecute(null))
            {
                ViewModel.ViewPodLogsCommand.Execute(null);
            }
        }

        private void OnPvcDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedPvc != null && ViewModel.BrowsePvcFilesCommand.CanExecute(null))
            {
                ViewModel.BrowsePvcFilesCommand.Execute(null);
            }
        }

        private void OnSecretDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedSecret != null && ViewModel.ViewSecretDataCommand.CanExecute(null))
            {
                ViewModel.ViewSecretDataCommand.Execute(null);
            }
        }

        private void OnOsCertDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedOsCertificate != null && ViewModel.InspectOsCertificateCommand.CanExecute(null))
            {
                ViewModel.InspectOsCertificateCommand.Execute(null);
            }
        }

        private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedRemoteFile != null && ViewModel.OpenRemoteItemCommand.CanExecute(ViewModel.SelectedRemoteFile))
            {
                ViewModel.OpenRemoteItemCommand.Execute(ViewModel.SelectedRemoteFile);
            }
        }

        private void OnLogSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgCommandLogs.SelectedItem is CommandLogEntry entry)
            {
                TxtLogPreview.Text = $"=== COMMAND ===\n{entry.Command}\n\n=== EXIT CODE: {entry.ExitCode} ({entry.DurationMs} ms) ===\n\n=== OUTPUT ===\n{entry.Output}\n\n=== ERROR ===\n{entry.Error}";
            }
        }

        private void OnCustomCommandKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ViewModel?.RunCustomCommand.CanExecute(null) == true)
            {
                ViewModel.RunCustomCommand.Execute(null);
            }
        }
    }
}
