using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Protes.Properties;

namespace Protes.Views
{
    public partial class SettingsWindow : Window
    {
        private string _appDataFolder;
        private string _currentDatabasePath;
        private MainWindow _mainWindow;

        public SettingsWindow(string currentDatabasePath, MainWindow mainWindow)
        {
            InitializeComponent();
            _currentDatabasePath = currentDatabasePath;
            _mainWindow = mainWindow;
            _appDataFolder = Path.GetDirectoryName(_currentDatabasePath) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes");

            if (!Directory.Exists(_appDataFolder))
                Directory.CreateDirectory(_appDataFolder);

            CurrentDbPathText.Text = _currentDatabasePath;
            LoadLocalDatabases();
            LoadExternalSettings();
        }

        private void LoadLocalDatabases()
        {
            var dbFiles = new List<DbFileInfo>();
            if (Directory.Exists(_appDataFolder))
            {
                var files = Directory.GetFiles(_appDataFolder, "*.db");
                foreach (var file in files)
                {
                    dbFiles.Add(new DbFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file
                    });
                }
            }
            LocalDbList.ItemsSource = dbFiles;
        }

        private void LoadExternalSettings()
        {
            HostTextBox.Text = Properties.Settings.Default.External_Host ?? "";
            PortTextBox.Text = Properties.Settings.Default.External_Port?.ToString() ?? "3306";
            DatabaseTextBox.Text = Properties.Settings.Default.External_Database ?? "";
            UsernameTextBox.Text = Properties.Settings.Default.External_Username ?? "";
            // Password is not loaded for security
        }

        // ===== LOCAL DATABASE ACTIONS =====

        private void ExportDbButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "SQLite Database|*.db",
                FileName = "notes.db",
                InitialDirectory = _appDataFolder
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(_currentDatabasePath, saveDialog.FileName, true);
                    SwitchToDatabase(saveDialog.FileName);
                    MessageBox.Show("Database saved successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save database:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void NewDbButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "SQLite Database|*.db",
                FileName = $"notes_{DateTime.Now:yyyyMMdd_HHmm}.db",
                InitialDirectory = _appDataFolder
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={saveDialog.FileName};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new System.Data.SQLite.SQLiteCommand(@"
                            CREATE TABLE IF NOT EXISTS Notes (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                Title TEXT NOT NULL,
                                Content TEXT,
                                Tags TEXT,
                                LastModified TEXT
                            )", conn))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }

                    SwitchToDatabase(saveDialog.FileName);
                    MessageBox.Show("New database created successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create database:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopySqlButton_Click(object sender, RoutedEventArgs e)
        {
            string sql = CreateTableSqlBox.Text;
            Clipboard.SetText(sql);
            MessageBox.Show("SQL script copied to clipboard!", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ImportDbButton_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "SQLite Database|*.db",
                InitialDirectory = _appDataFolder
            };

            if (openDialog.ShowDialog() == true)
            {
                SwitchToDatabase(openDialog.FileName);
            }
        }

        private void SwitchToDatabase(string newDbPath)
        {
            if (!File.Exists(newDbPath))
            {
                MessageBox.Show("Database file does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _currentDatabasePath = newDbPath;
            CurrentDbPathText.Text = newDbPath;
            _mainWindow.SwitchDatabase(newDbPath);
            LoadLocalDatabases();
        }

        private void LoadSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LocalDbList.SelectedItem as DbFileInfo;
            if (selectedItem != null)
            {
                SwitchToDatabase(selectedItem.FullPath);
            }
        }

        // ===== EXTERNAL DATABASE BUTTONS =====

        private void SaveExternalSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveExternalSettings();
            MessageBox.Show("External database settings saved.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ConnectNowButton_Click(object sender, RoutedEventArgs e)
        {
            SaveExternalSettings();

            _mainWindow.Dispatcher.Invoke(() =>
            {
                try
                {
                    // ✅ Use public method instead of private field
                    _mainWindow.SetDatabaseMode(DatabaseMode.External);

                    // ✅ Now allowed because BuildExternalConnectionString is 'internal'
                    var connString = _mainWindow.BuildExternalConnectionString();
                    if (string.IsNullOrEmpty(connString))
                    {
                        MessageBox.Show("Configuration incomplete.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    _mainWindow.SetExternalConnectionString(connString);
                    _mainWindow.TriggerConnect();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Connection failed:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void SaveExternalSettings()
        {
            Properties.Settings.Default.External_Host = HostTextBox.Text;
            Properties.Settings.Default.External_Port = PortTextBox.Text;
            Properties.Settings.Default.External_Database = DatabaseTextBox.Text;
            Properties.Settings.Default.External_Username = UsernameTextBox.Text;
            Properties.Settings.Default.External_Password = PasswordBox.Password;
            Properties.Settings.Default.Save();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public class DbFileInfo
        {
            public string FileName { get; set; }
            public string FullPath { get; set; }
        }
    }
}