using System.Windows;

namespace Protes.Views
{
    public partial class ExternalRequirementsWindow : Window
    {
        public ExternalRequirementsWindow()
        {
            InitializeComponent();
        }

        private void CopySqlButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(CreateTableSqlBox.Text);
            MessageBox.Show("SQL script copied to clipboard!", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}