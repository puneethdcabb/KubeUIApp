using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace KubeUIApp.Views
{
    public partial class FileViewerDialog : Window
    {
        private int _lastMatchIndex = -1;

        public FileViewerDialog(string filePath, string content)
        {
            InitializeComponent();
            TxtFilePath.Text = filePath;
            TxtContent.Text = content;
            TxtStatus.Text = $"{content.Length} characters loaded";
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
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(TxtContent.Text))
            {
                TxtContent.Select(0, 0);
                return;
            }

            int startIndex = (findNext && _lastMatchIndex >= 0 && _lastMatchIndex + query.Length < TxtContent.Text.Length)
                ? _lastMatchIndex + query.Length
                : 0;

            int index = TxtContent.Text.IndexOf(query, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0 && startIndex > 0)
            {
                index = TxtContent.Text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
            }

            if (index >= 0)
            {
                _lastMatchIndex = index;
                TxtContent.Select(index, query.Length);
                int lineIndex = TxtContent.GetLineIndexFromCharacterIndex(index);
                if (lineIndex >= 0)
                {
                    TxtContent.ScrollToLine(lineIndex);
                }
            }
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtContent.Text);
            MessageBox.Show("File content copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                FileName = Path.GetFileName(TxtFilePath.Text),
                Filter = "All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, TxtContent.Text);
                MessageBox.Show("File saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
