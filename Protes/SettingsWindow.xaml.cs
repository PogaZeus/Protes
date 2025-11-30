using Microsoft.Win32;
using Protes.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Protes.Views
{
    public partial class SettingsWindow : Window
    {
        private string _appDataFolder;
        private string _currentDatabasePath;
        private MainWindow _mainWindow;
        private List<string> _importedDbPaths = new List<string>();

        public SettingsWindow(string currentDatabasePath, MainWindow mainWindow)
        {
            InitializeComponent();
            _currentDatabasePath = currentDatabasePath;
            _mainWindow = mainWindow;
            _appDataFolder = Path.GetDirectoryName(_currentDatabasePath) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes");

            if (!Directory.Exists(_appDataFolder))
                Directory.CreateDirectory(_appDataFolder);

            // Load settings
            LoadPersistedSettings();
            CurrentDbPathText.Text = _currentDatabasePath;
            LoadLocalDatabases();
            LoadExternalSettings();
        }

        private void LoadPersistedSettings()
        {
            // Auto Connect
            bool autoConnect = Properties.Settings.Default.AutoConnect;
            AutoConnectCheckBox.IsChecked = autoConnect;

            // Notifications
            bool showNotifications = Properties.Settings.Default.ShowNotifications;
            ShowNotificationsCheckBox.IsChecked = showNotifications;
        }

        private void LoadLocalDatabases()
        {
            var dbFiles = new List<DbFileInfo>();

            // 1. Default app folder
            if (Directory.Exists(_appDataFolder))
            {
                var defaultFiles = Directory.GetFiles(_appDataFolder, "*.db");
                foreach (var file in defaultFiles)
                {
                    dbFiles.Add(new DbFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        IsImported = false
                    });
                }
            }

            // 2. Imported databases (from any location)
            var importedRaw = Properties.Settings.Default.ImportedDatabasePaths;
            if (!string.IsNullOrWhiteSpace(importedRaw))
            {
                _importedDbPaths = new List<string>(importedRaw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                _importedDbPaths.RemoveAll(p => !File.Exists(p)); // clean stale paths
            }

            foreach (var path in _importedDbPaths)
            {
                if (File.Exists(path))
                {
                    dbFiles.Add(new DbFileInfo
                    {
                        FileName = Path.GetFileName(path) + " (imported)",
                        FullPath = path,
                        IsImported = true
                    });
                }
            }

            var uniqueFiles = dbFiles.GroupBy(f => f.FullPath).Select(g => g.First()).ToList();
            LocalDbList.ItemsSource = uniqueFiles;
        }

        private void LoadExternalSettings()
        {
            HostTextBox.Text = Properties.Settings.Default.External_Host ?? "";
            PortTextBox.Text = Properties.Settings.Default.External_Port?.ToString() ?? "3306";
            DatabaseTextBox.Text = Properties.Settings.Default.External_Database ?? "";
            UsernameTextBox.Text = Properties.Settings.Default.External_Username ?? "";
            // Password not loaded for security
        }

        // ===== OPTIONS TAB =====

        private void AutoConnectCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = AutoConnectCheckBox.IsChecked == true;
            Properties.Settings.Default.AutoConnect = isChecked;
            Properties.Settings.Default.Save();

            // Sync with MainWindow checkbox
            _mainWindow.AutoConnectCheckBox.IsChecked = isChecked;
        }

        private void ShowNotificationsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ShowNotifications = ShowNotificationsCheckBox.IsChecked == true;
            Properties.Settings.Default.Save();
        }

        // ===== LOCAL DATABASE ACTIONS =====

        private void ExportDbButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "SQLite Database|*.db",
                FileName = Path.GetFileName(_currentDatabasePath),
                InitialDirectory = _appDataFolder
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(_currentDatabasePath, saveDialog.FileName, true);
                    MessageBox.Show("Database exported successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);

                    var switchResult = MessageBox.Show(
                        "Do you want to switch to the exported database now?",
                        "Switch Database",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (switchResult == MessageBoxResult.Yes)
                    {
                        string exportedPath = saveDialog.FileName;

                        // Only add to imported list if NOT in the default app folder
                        bool isInDefaultFolder = Path.GetDirectoryName(exportedPath)
                            .Equals(_appDataFolder, StringComparison.OrdinalIgnoreCase);

                        if (!isInDefaultFolder)
                        {
                            // Add to imported list if not already present
                            if (!_importedDbPaths.Contains(exportedPath))
                            {
                                _importedDbPaths.Add(exportedPath);
                                Properties.Settings.Default.ImportedDatabasePaths = string.Join(";", _importedDbPaths);
                                Properties.Settings.Default.Save();
                            }
                        }

                        // Now switch
                        SwitchToDatabase(exportedPath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export database:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Filter = "SQLite Database|*.db"
            };

            if (openDialog.ShowDialog() == true)
            {
                string sourcePath = openDialog.FileName;

                if (!_importedDbPaths.Contains(sourcePath))
                {
                    _importedDbPaths.Add(sourcePath);
                    Properties.Settings.Default.ImportedDatabasePaths = string.Join(";", _importedDbPaths);
                    Properties.Settings.Default.Save();
                }

                SwitchToDatabase(sourcePath);
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
            Properties.Settings.Default.LastLocalDatabasePath = newDbPath;
            Properties.Settings.Default.Save();

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

        // ===== NEW: Remove from List =====
        private void RemoveFromListButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LocalDbList.SelectedItem as DbFileInfo;
            if (selectedItem == null) return;

            // If it's NOT imported (i.e. in default folder), show explanatory message
            if (!selectedItem.IsImported)
            {
                MessageBox.Show(
            "This cannot be removed from the list as it is stored in the default database folder which is scanned for databases. " +
            "Your options are: keep it, move it to another folder, or delete it from the disk.",
            "Cannot Remove From List!",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Only imported databases can be removed from the list
            if (MessageBox.Show(
                $"Remove '{selectedItem.FileName.Replace(" (imported)", "")}' from the list?\n\nThe file will NOT be deleted.",
                "Remove from List",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _importedDbPaths.Remove(selectedItem.FullPath);
                Properties.Settings.Default.ImportedDatabasePaths = string.Join(";", _importedDbPaths);
                Properties.Settings.Default.Save();
                LoadLocalDatabases();
            }
        }

        // ===== NEW: Delete Database =====
        private void DeleteDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LocalDbList.SelectedItem as DbFileInfo;
            if (selectedItem == null) return;

            string fileName = Path.GetFileName(selectedItem.FullPath);
            string message;

            if (selectedItem.IsImported)
            {
                message = $"Delete the database file '{fileName}' from your computer?\n\nThis will permanently remove the file.";
            }
            else
            {
                message = $"Delete the database file '{fileName}'?\n\nThis will permanently remove it from the Protes app data folder.";
            }

            if (MessageBox.Show(message, "Delete Database", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    // Prevent deleting currently loaded DB
                    if (selectedItem.FullPath == _currentDatabasePath)
                    {
                        MessageBox.Show("Cannot delete the currently loaded database.", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    File.Delete(selectedItem.FullPath);

                    // If it was imported, remove from list too
                    if (selectedItem.IsImported)
                    {
                        _importedDbPaths.Remove(selectedItem.FullPath);
                        Properties.Settings.Default.ImportedDatabasePaths = string.Join(";", _importedDbPaths);
                        Properties.Settings.Default.Save();
                    }

                    LoadLocalDatabases();
                    MessageBox.Show("Database deleted successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete database:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ===== EXTERNAL DATABASE BUTTONS (unchanged) =====

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
                    _mainWindow.SetDatabaseMode(DatabaseMode.External);
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
            public bool IsImported { get; set; }
        }

    }
}