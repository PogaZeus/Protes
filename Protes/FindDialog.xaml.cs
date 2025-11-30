using System;
using System.Windows;
using System.Windows.Input;

namespace Protes
{
    public partial class FindDialog : Window
    {
        public event Action<string, bool, bool> FindRequested;

        public FindDialog()
        {
            InitializeComponent();
        }

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FindTextBox.Text))
            {
                MessageBox.Show("Please enter text to find.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Notify owner — don't close
            FindRequested?.Invoke(FindTextBox.Text, MatchCaseCheckBox.IsChecked == true, DirectionUpRadio.IsChecked == true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close(); // Only close when Cancel is clicked
        }

        private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FindNext_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}
