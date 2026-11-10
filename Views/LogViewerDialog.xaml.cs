using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace KubeUIApp.Views
{
    public partial class LogViewerDialog : Window
    {
        private readonly Func<string?, int, bool, Task<string>> _logFetcher;
        private readonly DispatcherTimer _tailTimer;
        private string _rawLogs = string.Empty;

        public LogViewerDialog(string title, List<string> containers, Func<string?, int, bool, Task<string>> logFetcher)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            _logFetcher = logFetcher;

            foreach (var c in containers)
            {
                CmbContainers.Items.Add(c);
            }
            if (CmbContainers.Items.Count > 0)
            {
                CmbContainers.SelectedIndex = 0;
            }

            _tailTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _tailTimer.Tick += async (s, e) => await LoadLogsAsync(false);

            Loaded += async (s, e) => await LoadLogsAsync(true);
            Closed += (s, e) => _tailTimer.Stop();
        }

        private async Task LoadLogsAsync(bool scrollToBottom)
        {
            try
            {
                TxtStatus.Text = "Fetching logs...";
                string? container = CmbContainers.SelectedItem?.ToString();
                int lines = 300;
                if (CmbLines.SelectedItem is ComboBoxItem item && int.TryParse(item.Content.ToString(), out int parsed))
                {
                    lines = parsed;
                }
                bool prev = ChkPrevious.IsChecked == true;

                _rawLogs = await _logFetcher(container, lines, prev);
                ApplyFilter();
                TxtStatus.Text = $"Updated at {DateTime.Now:HH:mm:ss}";

                if (scrollToBottom)
                {
                    TxtLogs.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
            }
        }

        private void ApplyFilter()
        {
            string query = TxtSearch.Text;
            if (string.IsNullOrWhiteSpace(query))
            {
                TxtLogs.Text = _rawLogs;
                return;
            }

            // Simple line filter
            var lines = _rawLogs.Split('\n');
            var matched = new List<string>();
            foreach (var l in lines)
            {
                if (l.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(l);
                }
            }
            TxtLogs.Text = string.Join("\n", matched);
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadLogsAsync(true);
        }

        private async void OnContainerChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) await LoadLogsAsync(true);
        }

        private async void OnLinesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) await LoadLogsAsync(true);
        }

        private async void OnPreviousToggled(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) await LoadLogsAsync(true);
        }

        private void OnAutoTailToggled(object sender, RoutedEventArgs e)
        {
            if (ChkAutoTail.IsChecked == true)
            {
                _tailTimer.Start();
            }
            else
            {
                _tailTimer.Stop();
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtLogs.Text);
            MessageBox.Show("Logs copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Log Files (*.log)|*.log|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = $"{TxtTitle.Text.Replace(" ", "_").Replace("/", "_")}.log"
            };

            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, TxtLogs.Text);
                MessageBox.Show("Logs saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _tailTimer.Stop();
            Close();
        }
    }
}
