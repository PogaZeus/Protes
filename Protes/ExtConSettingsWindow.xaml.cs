using System;
using System.Windows;
using System.Windows.Controls;

namespace Protes.Views
{
    public partial class ExtConSettingsWindow : Window
    {
        public ExternalDbProfile Profile { get; private set; }

        public ExtConSettingsWindow(ExternalDbProfile profile = null)
        {
            InitializeComponent();
            Profile = profile ?? new ExternalDbProfile();

            // Populate fields (no Name)
            HostTextBox.Text = Profile.Host;
            PortTextBox.Text = Profile.Port.ToString();
            DatabaseTextBox.Text = Profile.Database;
            UsernameTextBox.Text = Profile.Username;
            PasswordBox.Password = Profile.Password;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(HostTextBox.Text) || string.IsNullOrWhiteSpace(DatabaseTextBox.Text))
            {
                MessageBox.Show("Host and Database are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(PortTextBox.Text, out int port) || port <= 0 || port > 65535)
            {
                MessageBox.Show("Please enter a valid port number (1–65535).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update profile — NO Name
            Profile.Host = HostTextBox.Text.Trim();
            Profile.Port = port;
            Profile.Database = DatabaseTextBox.Text.Trim();
            Profile.Username = UsernameTextBox.Text.Trim();
            Profile.Password = PasswordBox.Password;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}