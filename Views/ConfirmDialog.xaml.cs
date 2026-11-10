using System.Windows;

namespace KubeUIApp.Views
{
    public partial class ConfirmDialog : Window
    {
        public bool IsConfirmed { get; private set; }
        public bool IsForce => ChkForce.IsChecked == true;

        public ConfirmDialog(string title, string message, bool showForce = false, string confirmButtonText = "Confirm")
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtMessage.Text = message;
            BtnConfirm.Content = confirmButtonText;
            if (showForce)
            {
                ChkForce.Visibility = Visibility.Visible;
            }
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
