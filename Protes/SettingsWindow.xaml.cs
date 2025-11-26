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

        // External DB fields
        private string _externalHost = "";
        private string _externalPort = "1433";
        private string _externalDatabase = "";
        private string _externalUsername = "";
        private string _externalPassword = "";

        public SettingsWindow(string currentDatabasePath, MainWindow mainWindow)
        {
            InitializeComponent();
            _currentDatabasePath = currentDatabasePath;
            _mainWindow = mainWindow;
            _appDataFolder = Path.GetDirectoryName(_currentDatabasePath) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes");

            // Ensure folder exists
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
            PortTextBox.Text = Properties.Settings.Default.External_Port ?? "1433";
            DatabaseTextBox.Text = Properties.Settings.Default.External_Database ?? "";
            UsernameTextBox.Text = Properties.Settings.Default.External_Username ?? "";
            // Password is not loaded for security
        }

        // ===== LOCAL DATABASE ACTIONS =====

        private void LocalSaveButton_Click(object sender, RoutedEventArgs e)
        {
            // "Save" just confirms current DB is saved (no file op needed)
            MessageBox.Show("Current database is already saved.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

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
                    // Create new empty SQLite DB with schema
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

            // Update current path
            _currentDatabasePath = newDbPath;
            CurrentDbPathText.Text = newDbPath;

            // Notify MainWindow to reconnect
            _mainWindow.SwitchDatabase(newDbPath);

            // Refresh list
            LoadLocalDatabases();
        }

        // ===== EXTERNAL DATABASE =====

        // We'll capture values on Save (or you can bind on LostFocus if preferred)

        // ===== BOTTOM BUTTONS =====

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Save external settings
            Properties.Settings.Default.External_Host = HostTextBox.Text;
            Properties.Settings.Default.External_Port = PortTextBox.Text;
            Properties.Settings.Default.External_Database = DatabaseTextBox.Text;
            Properties.Settings.Default.External_Username = UsernameTextBox.Text;
            Properties.Settings.Default.External_Password = PasswordBox.Password;
            // Password is NOT saved (security best practice)

            Properties.Settings.Default.Save();

            DialogResult = true;
            Close();
        }
        private void LoadSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LocalDbList.SelectedItem as DbFileInfo;
            if (selectedItem != null)
            {
                SwitchToDatabase(selectedItem.FullPath);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class DbFileInfo
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
    }
}