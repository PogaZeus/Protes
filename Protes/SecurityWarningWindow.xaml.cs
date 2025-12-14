using System.Windows;

namespace Protes.Views
{
    public partial class SecurityWarningWindow : Window
    {
        public bool UserChoseNotToShowAgain => DontShowAgainCheckBox.IsChecked == true;

        public SecurityWarningWindow()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}