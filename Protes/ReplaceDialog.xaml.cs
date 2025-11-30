using System.Windows;
using System.Windows.Forms;

namespace Protes
{
    public partial class ReplaceDialog : Window
    {
        public string SearchText => FindTextBox.Text;
        public string ReplaceText => ReplaceTextBox.Text;

        public bool ReplaceAllRequested { get; private set; } = false;

        public ReplaceDialog()
        {
            InitializeComponent();
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            ReplaceAllRequested = false;
            DialogResult = true;
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            ReplaceAllRequested = true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}