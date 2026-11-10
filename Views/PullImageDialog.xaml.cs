using System.Windows;

namespace KubeUIApp.Views
{
    public partial class PullImageDialog : Window
    {
        public string ImageName { get; private set; } = string.Empty;

        public PullImageDialog()
        {
            InitializeComponent();
        }

        private void OnPullClick(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtImageName.Text))
            {
                ImageName = TxtImageName.Text.Trim();
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please enter an image name.", "Image Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
