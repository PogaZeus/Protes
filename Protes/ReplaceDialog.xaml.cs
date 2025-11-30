using System;
using System.Windows;
using System.Windows.Input;

namespace Protes
{
    public partial class ReplaceDialog : Window
    {
        public string SearchText => FindTextBox.Text;
        public string ReplaceText => ReplaceTextBox.Text;
        public bool MatchCase => MatchCaseCheckBox.IsChecked == true;

        public ReplaceDialog()
        {
            InitializeComponent();
            FindTextBox.Focus();
        }

        // Events for non-modal interaction (like Find dialog)
        public event System.Action FindNextRequested;
        public event System.Action ReplaceRequested;
        public event System.Action ReplaceAllRequested;

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                MessageBox.Show("Please enter text to find.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            FindNextRequested?.Invoke();
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                MessageBox.Show("Please enter text to find.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ReplaceRequested?.Invoke();
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                MessageBox.Show("Please enter text to find.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ReplaceAllRequested?.Invoke();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}