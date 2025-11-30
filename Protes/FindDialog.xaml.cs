using System.Windows;

namespace Protes
{
    public partial class FindDialog : Window
    {
        public string SearchText => FindTextBox.Text;

        public FindDialog()
        {
            InitializeComponent();
            FindTextBox.Focus();
        }

        private void FindNext_Click(object sender, RoutedEventArgs e)
            => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}
