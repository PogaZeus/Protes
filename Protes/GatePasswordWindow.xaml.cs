using System;
using System.Windows;
using System.Windows.Controls;

namespace Protes.Views
{
    public partial class GatePasswordWindow : Window
    {
        public string Password { get; private set; }
        public bool WantsToRemovePassword { get; private set; }

        private readonly bool _isSettingPassword;
        private readonly bool _isChanging;

        // Constructor with database name
        public GatePasswordWindow(string databaseDisplayName, bool isSettingPassword, bool isChanging)
            : this(isSettingPassword, isChanging)
        {
            DatabaseNameText.Text = databaseDisplayName;
            DatabaseNameText.Visibility = Visibility.Visible;
        }

        // Original constructor
        public GatePasswordWindow(bool isSettingPassword, bool isChanging)
        {
            InitializeComponent();
            _isSettingPassword = isSettingPassword;
            _isChanging = isChanging;

            if (!_isSettingPassword)
            {
                // 🔓 Unlock mode
                Title = "Unlock Database";
                InstructionText.Text = "Enter password to unlock:";
                RemovePasswordButton.Visibility = Visibility.Collapsed;
                ConfirmText.Visibility = Visibility.Collapsed;
                PasswordBox2.Visibility = Visibility.Collapsed;
                PasswordText2.Visibility = Visibility.Collapsed;
                ShowPasswordCheckBox.Content = "Show password";
            }
            else if (_isChanging)
            {
                // 🔐 Password Settings mode (change/remove)
                Title = "Database Password";
                InstructionText.Text = "Set a new password:";
                RemovePasswordButton.Visibility = Visibility.Visible;
                ConfirmText.Visibility = Visibility.Visible;
                PasswordBox2.Visibility = Visibility.Visible;
                PasswordText2.Visibility = Visibility.Visible;
                ShowPasswordCheckBox.Content = "Show passwords";
            }
            else
            {
                // ✅ Initial set mode
                Title = "Set Database Password";
                InstructionText.Text = "Set a password:";
                RemovePasswordButton.Visibility = Visibility.Collapsed;
                ConfirmText.Visibility = Visibility.Visible;
                PasswordBox2.Visibility = Visibility.Visible;
                PasswordText2.Visibility = Visibility.Visible;
                ShowPasswordCheckBox.Content = "Show passwords";
            }

            ShowPasswordCheckBox.IsChecked = false;
        }

        private void RemovePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to remove database password protection?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                WantsToRemovePassword = true;
                DialogResult = true;
                Close();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSettingPassword)
                {
                    // 🔓 Unlock
                    string pwd = PasswordBox1.Visibility == Visibility.Visible
                        ? PasswordBox1.Password
                        : PasswordText1.Text;

                    if (string.IsNullOrWhiteSpace(pwd))
                    {
                        MessageBox.Show("Password cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Password = pwd;
                }
                else
                {
                    // 🔐 Set new password (no removal via empty field — handled by Remove button)
                    string newPwd = PasswordBox1.Visibility == Visibility.Visible
                        ? PasswordBox1.Password
                        : PasswordText1.Text;

                    string confirmPwd = PasswordBox2.Visibility == Visibility.Visible
                        ? PasswordBox2.Password
                        : PasswordText2.Text;

                    if (string.IsNullOrWhiteSpace(newPwd))
                    {
                        MessageBox.Show("Password cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (newPwd != confirmPwd)
                    {
                        MessageBox.Show("Passwords do not match.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    Password = newPwd;

                    if (!_isChanging)
                    {
                        var settings = new SettingsManager();
                        if (settings.ShowGateEntryWarning)
                        {
                            var warningWindow = new SecurityWarningWindow();
                            warningWindow.Owner = this;
                            if (warningWindow.ShowDialog() != true)
                            {
                                return; // User clicked Cancel
                            }

                            // Save user preference
                            if (warningWindow.UserChoseNotToShowAgain)
                            {
                                settings.ShowGateEntryWarning = false;
                                settings.Save();
                            }
                        }
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void OnShowPasswordChanged(object sender, RoutedEventArgs e)
        {
            bool show = ShowPasswordCheckBox.IsChecked == true;

            // Toggle New Password
            PasswordText1.Text = PasswordBox1.Password;
            PasswordText1.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PasswordBox1.Visibility = show ? Visibility.Collapsed : Visibility.Visible;

            // Toggle Confirm Password (only if visible)
            if (_isSettingPassword)
            {
                PasswordText2.Text = PasswordBox2.Password;
                PasswordText2.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                PasswordBox2.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}