using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Windows;
using System.Windows.Controls;

namespace Protes.Views
{
    public partial class SettingsWindow : Window
    {
        private string _appDataFolder; // ← This is now the *default* folder (may be changed by user)
        private string _currentDatabasePath;
        private MainWindow _mainWindow;
        private List<string> _importedDbPaths = new List<string>();
        private readonly SettingsManager _settings = new SettingsManager();
        public string CurrentDbPath => _currentDatabasePath;
        public SettingsWindow(string currentDatabasePath, MainWindow mainWindow)
        {
            InitializeComponent();
            _currentDatabasePath = currentDatabasePath;
            _mainWindow = mainWindow;
            DataContext = this;

            // ✅ Load user-defined default folder, or fallback to %AppData%\Protes
            string savedDefaultFolder = _settings.DefaultDatabaseFolder;
            if (!string.IsNullOrWhiteSpace(savedDefaultFolder) && Directory.Exists(savedDefaultFolder))
            {
                _appDataFolder = savedDefaultFolder;
            }
            else
            {
                _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes");
                _settings.DefaultDatabaseFolder = _appDataFolder;
            }

            if (!Directory.Exists(_appDataFolder))
                Directory.CreateDirectory(_appDataFolder);

            // Load UI state
            AutoConnectCheckBox.IsChecked = _settings.AutoConnect;
            AutoConnectOnSwitchCheckBox.IsChecked = _settings.AutoConnectOnSwitch;
            AutoDisconnectOnSwitchCheckBox.IsChecked = _settings.AutoDisconnectOnSwitch;
            ShowNotificationsCheckBox.IsChecked = _settings.ShowNotifications;
            NotifyDeletedCheckBox.IsChecked = _settings.NotifyDeleted;
            NotifyCopiedCheckBox.IsChecked = _settings.NotifyCopied;
            NotifyPastedCheckBox.IsChecked = _settings.NotifyPasted;
            DefaultDbFolderText.Text = _appDataFolder;
           
            // Toolbar visibility
            ViewToolbarMenuItem.IsChecked = _settings.ViewMainToolbar;
            ViewToolbarOptionsInMenuCheckBox.IsChecked = _settings.ViewToolbarOptionsInMenu;
            ViewToolbarConnectMenuItem.IsChecked = _settings.ViewToolbarConnect;
            ViewToolbarACOSMenuItem.IsChecked = _settings.ViewToolbarACOS;
            ViewToolbarLocalDBMenuItem.IsChecked = _settings.ViewToolbarLocalDB;
            ViewToolbarImpExMenuItem.IsChecked = _settings.ViewToolbarImpEx;
            ViewToolbarSearchMenuItem.IsChecked = _settings.ViewToolbarSearch;

            //Systray
            MinimizeToSystemTray.IsChecked = _settings.MinimizeToTray;
            ShellNewCheckBox.IsChecked = _settings.ShellNewIntegrationEnabled;

            CurrentDbPathText.Text = _currentDatabasePath;
            LoadLocalDatabases();
            LoadExternalSettings();
            var importedRaw = _settings.ImportedDatabasePaths;

            if (_settings.AutoConnectOnSwitch)
            {
                AutoDisconnectOnSwitchCheckBox.IsEnabled = false;
            }
            else
            {
                AutoDisconnectOnSwitchCheckBox.IsEnabled = true;
            }
            _isInitializing = false;
        }

        private void RegisterShellNew()
        {
            const string extension = ".prote";
            const string progId = "Protes.DatabaseFile";
            string exePath = Process.GetCurrentProcess().MainModule.FileName;

            // 1. Register file extension
            using (var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
            {
                extKey.SetValue("", progId);
                extKey.SetValue("PerceivedType", "document");
            }

            // 2. Define ProgID
            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
            {
                progIdKey.SetValue("", "Protes Database");
                progIdKey.SetValue("FriendlyTypeName", "Pro Note");
            }

            // 3. Associate app with double-click (open/connect to database file)
            using (var openKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open"))
            {
                openKey.SetValue("", "Open with Protes");
                openKey.SetValue("Icon", $"\"{exePath}\",0");

                using (var cmdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open\command"))
                {
                    cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
                }
            }

            // 4. Add "New Protes Note" to Windows "New" context menu
            // This sends the -new command to open NoteEditorWindow in the existing instance
            using (var shellNewKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\ShellNew"))
            {
                shellNewKey.SetValue("Command", $"\"{exePath}\" -new");
                shellNewKey.SetValue("ItemName", "Protes Note");
                // Remove NullFile if it exists from previous versions
                try { shellNewKey.DeleteValue("NullFile"); } catch { }
            }

            // 5. Add "Create New Note" to .prote file context menu
            using (var newNoteKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\newnote"))
            {
                newNoteKey.SetValue("", "Create New Note");
                newNoteKey.SetValue("Icon", $"\"{exePath}\",0");

                using (var cmdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\newnote\command"))
                {
                    cmdKey.SetValue("", $"\"{exePath}\" -new");
                }
            }

            MessageBox.Show(
                "File associations registered successfully!\n\n" +
                "You can now:\n" +
                "• Double-click .prote files to open/switch to them\n" +
                "• Right-click .prote files → 'Create New Note'\n" +
                "• Right-click in folders → New → Protes Note (opens note editor)",
                "Protes",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void UnregisterShellNew()
        {
            const string extension = ".prote";
            const string progId = "Protes.DatabaseFile"; // ✅ Match RegisterShellNew

            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{extension}"); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{progId}"); } catch { }
        }

        // ✅ NEW: Change Default Database Folder
        private void ChangeDefaultFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the default folder for Protes databases:",
                SelectedPath = _appDataFolder,
                ShowNewFolderButton = true
            };

            // Must reference System.Windows.Forms for FolderBrowserDialog
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string newFolder = folderDialog.SelectedPath;

                // Optional: confirm it's writable
                try
                {
                    var testFile = Path.Combine(newFolder, ".protes_test");
                    File.WriteAllText(testFile, "ok");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Cannot write to selected folder:\n{ex.Message}", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Update and save
                _appDataFolder = newFolder;
                _settings.DefaultDatabaseFolder = newFolder;
                DefaultDbFolderText.Text = newFolder;

                // Refresh list
                LoadLocalDatabases();
            }
        }
        private void LoadLocalDatabases()
        {
            var dbFiles = new List<DbFileInfo>();

            // 1. Default app folder (now user-configurable)
            if (Directory.Exists(_appDataFolder))
            {
                var defaultFiles = Directory.GetFiles(_appDataFolder, "*.db")
                                           .Concat(Directory.GetFiles(_appDataFolder, "*.prote"));
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

            // 2. Imported databases
            var importedRaw = _settings.ImportedDatabasePaths;
            if (!string.IsNullOrWhiteSpace(importedRaw))
            {
                _importedDbPaths = new List<string>(importedRaw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                _importedDbPaths.RemoveAll(p => !File.Exists(p));
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
            HostTextBox.Text = _settings.External_Host ?? "";
            PortTextBox.Text = _settings.External_Port?.ToString() ?? "3306";
            DatabaseTextBox.Text = _settings.External_Database ?? "";
            UsernameTextBox.Text = _settings.External_Username ?? "";
            // Password not loaded
        }

        private void LocalDbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalDbList.SelectedItem is DbFileInfo selected)
            {
                bool isCurrent = (selected.FullPath == _currentDatabasePath);
                bool isInDefaultFolder = !selected.IsImported;

                // "Load Selected" → hidden if current
                LoadSelectedButton.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;

                // "Remove from List" → hidden if in default folder OR if current
                RemoveFromListButton.Visibility = (!isInDefaultFolder && !isCurrent) ? Visibility.Visible : Visibility.Collapsed;

            }
            else
            {
                
            }
        }

        // ===== LOCAL DATABASE ACTIONS =====

        private void ExportDbButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Protes Database (*.prote)|*.prote|SQLite Database (*.db)|*.db",
                FileName = Path.GetFileNameWithoutExtension(_currentDatabasePath) + ".prote", // prefer .prote
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
                        bool isInDefaultFolder = Path.GetDirectoryName(exportedPath)
                            .Equals(_appDataFolder, StringComparison.OrdinalIgnoreCase);

                        if (!isInDefaultFolder)
                        {
                            if (!_importedDbPaths.Contains(exportedPath))
                            {
                                _importedDbPaths.Add(exportedPath);
                                _settings.ImportedDatabasePaths = string.Join(";", _importedDbPaths);
                            }
                        }

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
                Filter = "Protes Database (*.prote)|*.prote|SQLite Database (*.db)|*.db",
                FileName = $"notes_{DateTime.Now:yyyyMMdd_HHmm}.prote",
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
                    LoadLocalDatabases(); // ✅ ADD THIS LINE
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
                Filter = "Protes Database (*.prote;*.db)|*.prote;*.db|SQLite Database (*.db)|*.db|All files (*.*)|*.*"
            };

            if (openDialog.ShowDialog() == true)
            {
                string sourcePath = openDialog.FileName;

                if (!_importedDbPaths.Contains(sourcePath))
                {
                    _importedDbPaths.Add(sourcePath);
                    _settings.ImportedDatabasePaths = string.Join(";", _importedDbPaths);
                    _settings.Save();
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
            _settings.LastLocalDatabasePath = newDbPath;

            _mainWindow.SwitchToLocalDatabase(newDbPath);

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

        private void RemoveFromListButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LocalDbList.SelectedItem as DbFileInfo;
            if (selectedItem == null) return;

            if (!selectedItem.IsImported)
            {
                MessageBox.Show(
                    "This cannot be removed from the list as it is stored in the default database folder. " +
                    "Your options are: keep it, move it to another folder, or delete it from the disk.",
                    "Cannot Remove",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                $"Remove '{selectedItem.FileName.Replace(" (imported)", "")}' from the list?\n\nThe file will NOT be deleted.",
                "Remove from List",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _importedDbPaths.Remove(selectedItem.FullPath);
                _settings.ImportedDatabasePaths = string.Join(";", _importedDbPaths);
                _settings.Save();
                LoadLocalDatabases();
            }
        }

        private void DeleteDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LocalDbList.SelectedItem as DbFileInfo;
            if (selectedItem == null) return;

            string fileName = Path.GetFileName(selectedItem.FullPath);
            string message = selectedItem.IsImported
                ? $"Delete the database file '{fileName}' from your computer?\n\nThis will permanently remove the file."
                : $"Delete the database file '{fileName}'?\n\nThis will permanently remove it from the Protes app data folder.";

            if (MessageBox.Show(message, "Delete Database", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    if (selectedItem.FullPath == _currentDatabasePath)
                    {
                        MessageBox.Show("Cannot delete the currently loaded database.", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    File.Delete(selectedItem.FullPath);

                    if (selectedItem.IsImported)
                    {
                        _importedDbPaths.Remove(selectedItem.FullPath);
                        _settings.ImportedDatabasePaths = string.Join(";", _importedDbPaths);
                        _settings.Save();
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

        // ===== EXTERNAL DATABASE =====

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
            _settings.External_Host = HostTextBox.Text;
            _settings.External_Port = PortTextBox.Text;
            _settings.External_Database = DatabaseTextBox.Text;
            _settings.External_Username = UsernameTextBox.Text;
            _settings.External_Password = PasswordBox.Password;
        }

        // ===== MORE OPTIONS =====
        private void AutoConnectCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = AutoConnectCheckBox.IsChecked == true;
            _settings.AutoConnect = isChecked;
        }
        private void AutoConnectOnSwitchCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = AutoConnectOnSwitchCheckBox.IsChecked == true;
            _mainWindow.AutoConnectOnSwitchCheckBox.IsChecked = isChecked;
            _settings.AutoConnectOnSwitch = isChecked;
            if (isChecked)
            {
                _settings.AutoDisconnectOnSwitch = true;
                AutoDisconnectOnSwitchCheckBox.IsChecked = true;
                AutoDisconnectOnSwitchCheckBox.IsEnabled = false;
            }
            else
            {
                AutoDisconnectOnSwitchCheckBox.IsEnabled = true;
            }
        }

        private void AutoDisconnectOnSwitchCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Only save if AutoConnectOnSwitch is OFF (otherwise it's forced ON)
            if (!_settings.AutoConnectOnSwitch)
            {
                _settings.AutoDisconnectOnSwitch = AutoDisconnectOnSwitchCheckBox.IsChecked == true;
            }
        }
        private void ShowNotificationsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isChecked = ShowNotificationsCheckBox.IsChecked == true;
            _settings.ShowNotifications = isChecked;
        }
        private void NotifyDeletedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _settings.NotifyDeleted = NotifyDeletedCheckBox.IsChecked == true;
        }

        private void NotifyCopiedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _settings.NotifyCopied = NotifyCopiedCheckBox.IsChecked == true;
        }

        private void NotifyPastedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _settings.NotifyPasted = NotifyPastedCheckBox.IsChecked == true;
        }
        private void ViewToolbarMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewMainToolbar = ViewToolbarMenuItem.IsChecked == true;
        }
        private void ViewToolbarOptionsInMenuCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarOptionsInMenu = ViewToolbarOptionsInMenuCheckBox.IsChecked == true;
            _mainWindow.RefreshToolbarSettingsFromSettingsManager();
        }

        private void ViewToolbarConnectMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarConnect = ViewToolbarConnectMenuItem.IsChecked == true;
        }
        private void ViewToolbarLocalDBMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarLocalDB = ViewToolbarLocalDBMenuItem.IsChecked == true;
        }

        private void ViewToolbarACOSMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarACOS = ViewToolbarACOSMenuItem.IsChecked == true;
        }
        private void ViewToolbarImpExMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarImpEx = ViewToolbarImpExMenuItem.IsChecked == true;
        }
        private void ViewToolbarSearchMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarSearch = ViewToolbarSearchMenuItem.IsChecked == true;
        }

        private void MinimizeToSystemTray_Checked(object sender, RoutedEventArgs e)
        {
            _settings.MinimizeToTray = MinimizeToSystemTray.IsChecked == true;
        }
        private bool _isInitializing = true;
        private void ShellNewCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (MessageBox.Show(
                "This will add 'Note Editor (Protes)' to the Windows 'New' menu in File Explorer.\n\n" +
                "Allow this?",
                "Protes", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    RegisterShellNew();
                    _settings.ShellNewIntegrationEnabled = true;
                    MessageBox.Show("Integration enabled successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to enable integration:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                    ShellNewCheckBox.IsChecked = false;
                }
            }
            else
            {
                ShellNewCheckBox.IsChecked = false;
            }
        }

        private void ShellNewCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            try
            {
                UnregisterShellNew();
                _settings.ShellNewIntegrationEnabled = false;
                MessageBox.Show("Integration removed.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to remove integration:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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