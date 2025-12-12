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

        public GatePasswordWindow(bool isSettingPassword, bool isChanging)
        {
            InitializeComponent();
            _isSettingPassword = isSettingPassword;
            _isChanging = isChanging;

            if (!_isSettingPassword)
            {
                // Unlock only
                Title = "Unlock Database";
                InstructionText.Text = "Enter password to unlock:";
                ConfirmText.Visibility = Visibility.Collapsed;
                PasswordBox2.Visibility = Visibility.Collapsed;
                PasswordText2.Visibility = Visibility.Collapsed;
                ShowPasswordCheckBox.Content = "Show password";
            }
            else if (_isChanging)
            {
                // Change or remove
                Title = "Database Password";
                InstructionText.Text = "New password (leave blank to remove):";
                ConfirmText.Visibility = Visibility.Visible;
                PasswordBox2.Visibility = Visibility.Visible;
                PasswordText2.Visibility = Visibility.Visible;
                ShowPasswordCheckBox.Content = "Show passwords";
            }
            else
            {
                // Initial setup
                Title = "Set Database Password";
                InstructionText.Text = "Enter a password:";
                ConfirmText.Visibility = Visibility.Visible;
                PasswordBox2.Visibility = Visibility.Visible;
                PasswordText2.Visibility = Visibility.Visible;
                ShowPasswordCheckBox.Content = "Show passwords";
            }

            ShowPasswordCheckBox.IsChecked = false;
        }

        private void OnShowPasswordChanged(object sender, RoutedEventArgs e)
        {
            bool show = ShowPasswordCheckBox.IsChecked == true;

            if (show)
            {
                // Copy password → text
                PasswordText1.Text = PasswordBox1.Password;
                PasswordText2.Text = PasswordBox2.Password;

                // Hide PasswordBox, show TextBox
                PasswordBox1.Visibility = Visibility.Collapsed;
                PasswordBox2.Visibility = Visibility.Collapsed;
                PasswordText1.Visibility = Visibility.Visible;
                PasswordText2.Visibility = Visibility.Visible;
            }
            else
            {
                // Copy text → password
                PasswordBox1.Password = PasswordText1.Text;
                PasswordBox2.Password = PasswordText2.Text;

                // Hide TextBox, show PasswordBox
                PasswordText1.Visibility = Visibility.Collapsed;
                PasswordText2.Visibility = Visibility.Collapsed;
                PasswordBox1.Visibility = Visibility.Visible;
                PasswordBox2.Visibility = Visibility.Visible;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("🔍 [GatePasswordWindow] OK button clicked");

            // Read passwords from visible controls
            string pwd1 = PasswordBox1.Visibility == Visibility.Visible
                ? PasswordBox1.Password
                : PasswordText1.Text;

            string pwd2 = PasswordBox2.Visibility == Visibility.Visible
                ? PasswordBox2.Password
                : PasswordText2.Text;

            System.Diagnostics.Debug.WriteLine($"🔍 pwd1 = '{pwd1}' (length: {pwd1?.Length ?? 0})");
            System.Diagnostics.Debug.WriteLine($"🔍 pwd2 = '{pwd2}' (length: {pwd2?.Length ?? 0})");
            System.Diagnostics.Debug.WriteLine($"🔍 _isSettingPassword = {_isSettingPassword}");
            System.Diagnostics.Debug.WriteLine($"🔍 _isChanging = {_isChanging}");

            try
            {
                if (!_isSettingPassword)
                {
                    // 🔓 Unlock mode
                    System.Diagnostics.Debug.WriteLine("🔓 Entering UNLOCK mode logic");

                    if (string.IsNullOrWhiteSpace(pwd1))
                    {
                        System.Diagnostics.Debug.WriteLine("❌ Password is empty in UNLOCK mode");
                        MessageBox.Show("Password cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    Password = pwd1;
                    System.Diagnostics.Debug.WriteLine("✅ Unlock password captured successfully");
                }
                else
                {
                    // 🔐 Set or change password
                    System.Diagnostics.Debug.WriteLine("🔐 Entering SET/CHANGE password logic");

                    if (string.IsNullOrWhiteSpace(pwd1))
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ Password is empty → checking if REMOVE is allowed");

                        if (_isChanging)
                        {
                            System.Diagnostics.Debug.WriteLine("🗑️ REMOVE password requested");
                            WantsToRemovePassword = true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("❌ Empty password not allowed in SET mode");
                            MessageBox.Show("Password cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                    else
                    {
                        // ✅ Password provided — validate match
                        System.Diagnostics.Debug.WriteLine("✅ Non-empty password provided");

                        if (pwd1 != pwd2)
                        {
                            System.Diagnostics.Debug.WriteLine("❌ Passwords do not match");
                            MessageBox.Show("Passwords do not match.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        Password = pwd1;
                        System.Diagnostics.Debug.WriteLine("✅ Passwords match — proceeding");

                        // Show disclaimer only on first set or change
                        if (!_isChanging)
                        {
                            System.Diagnostics.Debug.WriteLine("ℹ️ Showing security disclaimer (first-time set)");
                            var result = MessageBox.Show(
                                "⚠️ DISCLAIMER:\n" +
                                "This feature offers basic obfuscation only. " +
                                "It is not a substitute for file encryption or system-level security.",
                                "Security Notice", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                            if (result == MessageBoxResult.Cancel)
                            {
                                System.Diagnostics.Debug.WriteLine("❌ User canceled after disclaimer");
                                return;
                            }
                        }
                    }
                }

                // ✅ All validation passed — set DialogResult and close
                System.Diagnostics.Debug.WriteLine("✅ All logic passed — setting DialogResult = true");
                DialogResult = true;
                System.Diagnostics.Debug.WriteLine("CloseOperation: Close() called");
                Close();
            }
            catch (Exception ex)
            {
                // 🔥 Catch any unexpected exception
                System.Diagnostics.Debug.WriteLine($"💥 UNEXPECTED EXCEPTION in OkButton_Click: {ex}");
                MessageBox.Show($"An unexpected error occurred:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}