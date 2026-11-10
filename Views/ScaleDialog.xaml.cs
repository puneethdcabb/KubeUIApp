using System.Windows;

namespace KubeUIApp.Views
{
    public partial class ScaleDialog : Window
    {
        public int Replicas { get; private set; }

        public ScaleDialog(string deploymentName, int currentReplicas)
        {
            InitializeComponent();
            TxtTitle.Text = $"Scale: {deploymentName}";
            Replicas = currentReplicas;
            TxtReplicas.Text = currentReplicas.ToString();
        }

        private void OnMinusClick(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtReplicas.Text, out int val) && val > 0)
            {
                TxtReplicas.Text = (val - 1).ToString();
            }
        }

        private void OnPlusClick(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtReplicas.Text, out int val))
            {
                TxtReplicas.Text = (val + 1).ToString();
            }
        }

        private void OnScaleClick(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtReplicas.Text, out int val) && val >= 0)
            {
                Replicas = val;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please enter a valid non-negative number.", "Invalid Replicas", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
