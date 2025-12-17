using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Windows;
using System.Windows.Controls;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
using System.Windows.Media;

namespace Protes.Views
{
    public partial class SettingsWindow : Window
    {
        private string _appDataFolder; // ← This is now the *default* folder (may be changed by user)
        private string _currentDatabasePath;
        private MainWindow _mainWindow;
        private List<string> _importedDbPaths = new List<string>();
        private readonly SettingsManager _settings = new SettingsManager();
        private ExternalDbProfile _selectedExternalProfile;
        public string CurrentDbPath => _currentDatabasePath;

        #region Constructor and Initialization
        public SettingsWindow(string currentDatabasePath, MainWindow mainWindow)
        {
            InitializeComponent();
            LoadExternalConnectionsList();
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

            //Notifications
            ShowNotificationsCheckBox.IsChecked = _settings.ShowNotifications;
            NotifyDeletedCheckBox.IsChecked = _settings.NotifyDeleted;
            NotifyCopiedCheckBox.IsChecked = _settings.NotifyCopied;
            NotifyPastedCheckBox.IsChecked = _settings.NotifyPasted;
            ShowGateEntryWarningCheckBox.IsChecked = _settings.ShowGateEntryWarning;

            //DefaultDBFolder
            DefaultDbFolderText.Text = _appDataFolder;
            SyncAutoSwitchUiState(); // 👈 ensures correct IsEnabled + enforced value

            // Toolbar visibility
            ViewToolbarMenuItem.IsChecked = _settings.ViewMainToolbar;
            ViewToolbarOptionsInMenuCheckBox.IsChecked = _settings.ViewToolbarOptionsInMenu;
            ViewToolbarConnectMenuItem.IsChecked = _settings.ViewToolbarConnect;
            ViewToolbarSettingsMenuItem.IsChecked = _settings.ViewToolbarSettings;
            ViewToolbarNoteToolsMenuItem.IsChecked = _settings.ViewToolbarNoteTools;
            ViewToolbarCopyPasteMenuItem.IsChecked = _settings.ViewToolbarCopyPaste;
            ViewToolbarACOSMenuItem.IsChecked = _settings.ViewToolbarACOS;
            ViewToolbarLocalDBMenuItem.IsChecked = _settings.ViewToolbarLocalDB;
            ViewToolbarImpExMenuItem.IsChecked = _settings.ViewToolbarImpEx;
            ViewToolbarSearchMenuItem.IsChecked = _settings.ViewToolbarSearch;
            ViewToolbarCalcMenuItem.IsChecked = _settings.ViewToolbarCalculator;
            ViewToolbarGateEntryMenuItem.IsChecked = _settings.ViewToolbarGateEntry;

            //Systray
            LaunchOnStartupCheckBox.IsChecked = _settings.LaunchOnStartup;
            MinimizeToSystemTray.IsChecked = _settings.MinimizeToTray;
            CloseToSystemTray.IsChecked = _settings.CloseToTray;
            ShellNewCheckBox.IsChecked = _settings.ShellNewIntegrationEnabled;
            SendToCheckBox.IsChecked = _settings.SendToIntegrationEnabled;
            SendToNoteEditorCheckBox.IsChecked = _settings.SendToNoteEditorEnabled;
            CurrentDbPathText.Text = _currentDatabasePath;
            LoadLocalDatabases();
            var importedRaw = _settings.ImportedDatabasePaths;

            _isInitializing = false;
        }
        #endregion

        #region Local Database - UI Loading & Helpers
        private void LoadLocalDatabases()
        {
            var dbFiles = new List<DbFileInfo>();
            string currentDefaultDb = _settings.LastLocalDatabasePath ?? "";

            // 1. Default app folder
            if (Directory.Exists(_appDataFolder))
            {
                var defaultFiles = Directory.GetFiles(_appDataFolder, "*.db")
                                           .Concat(Directory.GetFiles(_appDataFolder, "*.prote"));
                foreach (var file in defaultFiles)
                {
                    bool isDefault = file.Equals(currentDefaultDb, StringComparison.OrdinalIgnoreCase);
                    string displayName = isDefault ? $"⭐ {Path.GetFileName(file)}" : Path.GetFileName(file);

                    dbFiles.Add(new DbFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        IsImported = false,
                        DisplayNameWithIndicator = displayName
                    });
                }
            }

            // 2. Imported databases
            var importedRaw = _settings.ImportedDatabasePaths;
            if (!string.IsNullOrWhiteSpace(importedRaw))
            {
                _importedDbPaths = new List<string>(
                    importedRaw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                );
                _importedDbPaths.RemoveAll(p => !File.Exists(p));

                foreach (var path in _importedDbPaths)
                {
                    if (File.Exists(path))
                    {
                        bool isDefault = path.Equals(currentDefaultDb, StringComparison.OrdinalIgnoreCase);
                        string fileName = Path.GetFileName(path) + " (imported)";
                        string displayName = isDefault ? $"⭐ {fileName}" : fileName;

                        dbFiles.Add(new DbFileInfo
                        {
                            FileName = fileName,
                            FullPath = path,
                            IsImported = true,
                            DisplayNameWithIndicator = displayName
                        });
                    }
                }
            }

            var uniqueFiles = dbFiles.GroupBy(f => f.FullPath).Select(g => g.First()).ToList();
            LocalDbList.ItemsSource = uniqueFiles;
            //_mainWindow.LoadAvailableDatabases();
        }

        private void LocalDbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalDbList.SelectedItem is DbFileInfo selected)
            {
                bool isCurrent = (selected.FullPath == _currentDatabasePath);
                bool isInDefaultFolder = !selected.IsImported;

                // "Load Selected" → hidden if current
                LoadSelectedButton.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
                DeleteDatabaseButton.Visibility = !isCurrent ? Visibility.Visible : Visibility.Collapsed;

                // "Remove from List & Delete" → hidden if in default folder OR if current
                RemoveFromListButton.Visibility = (!isInDefaultFolder && !isCurrent) ? Visibility.Visible : Visibility.Collapsed;

            }
        }
        #endregion

        #region Local Database - Button Actions
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
                    LoadLocalDatabases();
                    MessageBox.Show("New database created successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create database:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
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
        private void LoadSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LocalDbList.SelectedItem as DbFileInfo;
            if (selectedItem != null)
            {
                SwitchToDatabase(selectedItem.FullPath);
                _mainWindow.LoadAvailableDatabases();
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
        private void ChangeDefaultFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the default folder for Protes databases:",
                SelectedPath = _appDataFolder,
                ShowNewFolderButton = true
            };

            // System.Windows.Forms for FolderBrowserDialog
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
        #endregion

        #region Switch Local Database
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
        #endregion

        #region External Database
        private void LoadExternalConnectionsList()
        {
            var profiles = _settings.GetExternalDbProfiles();

            // Get current default connection values for comparison
            string currentHost = _settings.External_Host ?? "";
            string currentPort = _settings.External_Port ?? "3306";
            string currentDb = _settings.External_Database ?? "";
            string currentUser = _settings.External_Username ?? "";

            // Add visual indicator to default connection
            foreach (var profile in profiles)
            {
                bool isDefault = (profile.Host == currentHost) &&
                                 (profile.Port.ToString() == currentPort) &&
                                 (profile.Database == currentDb) &&
                                 (profile.Username == currentUser);
                profile.DisplayNameWithIndicator = isDefault ? $"⭐ {profile.DisplayName}" : profile.DisplayName;
            }

            ExternalConnectionsList.ItemsSource = null;
            ExternalConnectionsList.ItemsSource = profiles;

            // Update CurrentDefaultExternalText
            if (!string.IsNullOrWhiteSpace(_settings.External_Host) &&
                !string.IsNullOrWhiteSpace(_settings.External_Database))
            {
                CurrentDefaultExternalText.Text =
                    $"{_settings.External_Host}:{_settings.External_Port ?? "3306"}/{_settings.External_Database} (User: {_settings.External_Username})";
            }
            else
            {
                CurrentDefaultExternalText.Text = "(No external connection configured)";
            }

            _selectedExternalProfile = null;
            EditConnectionButton.IsEnabled = false;
            RemoveConnectionButton.IsEnabled = false;
            ConnectNowButton.IsEnabled = false;
            SetDefaultButton.IsEnabled = false;
        }
        private void SetDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedExternalProfile == null) return;

            // ✅ Overwrite the current default external settings
            _settings.External_Host = _selectedExternalProfile.Host;
            _settings.External_Port = _selectedExternalProfile.Port.ToString();
            _settings.External_Database = _selectedExternalProfile.Database;
            _settings.External_Username = _selectedExternalProfile.Username;
            _settings.External_Password = _selectedExternalProfile.Password;
            _settings.Save();

            // Refresh UI
            LoadExternalConnectionsList();
            MessageBox.Show("Default external connection updated.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExternalConnectionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedExternalProfile = ExternalConnectionsList.SelectedItem as ExternalDbProfile;
            bool enabled = _selectedExternalProfile != null;
            bool isDefault = enabled && IsProfileCurrentDefault(_selectedExternalProfile);

            EditConnectionButton.IsEnabled = enabled;
            RemoveConnectionButton.IsEnabled = enabled;
            ConnectNowButton.IsEnabled = enabled;
            SetDefaultButton.IsEnabled = enabled && !isDefault; // 👈 disable if already default
        }
        private bool IsProfileCurrentDefault(ExternalDbProfile profile)
        {
            if (profile == null) return false;
            return
                profile.Host == _settings.External_Host &&
                profile.Port.ToString() == (_settings.External_Port ?? "3306") &&
                profile.Database == _settings.External_Database &&
                profile.Username == _settings.External_Username;
            // ❗ Skip password for UI comparison (security + UX)
        }
        private void AddConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            var editor = new ExtConSettingsWindow();
            if (editor.ShowDialog() == true)
            {
                var profiles = _settings.GetExternalDbProfiles();
                profiles.Add(editor.Profile);
                _settings.SaveExternalDbProfiles(profiles);
                LoadExternalConnectionsList();
            }
        }

        private void EditConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedExternalProfile == null) return;

            // Deep copy to avoid reference mutation
            var profileCopy = new ExternalDbProfile
            {
                Host = _selectedExternalProfile.Host,
                Port = _selectedExternalProfile.Port,
                Database = _selectedExternalProfile.Database,
                Username = _selectedExternalProfile.Username,
                Password = _selectedExternalProfile.Password
            };

            var editor = new ExtConSettingsWindow(profileCopy);
            if (editor.ShowDialog() == true)
            {
                var profiles = _settings.GetExternalDbProfiles();
                int index = profiles.IndexOf(_selectedExternalProfile);
                if (index >= 0)
                {
                    profiles[index] = editor.Profile;
                    _settings.SaveExternalDbProfiles(profiles);
                    LoadExternalConnectionsList();
                    ExternalConnectionsList.SelectedIndex = index;
                }
            }
        }

        private void RemoveConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedExternalProfile == null) return;

            if (MessageBox.Show(
                $"Remove connection:\n{_selectedExternalProfile.DisplayName}?",
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var profiles = _settings.GetExternalDbProfiles();

                // ✅ Remove by value (compare all fields)
                profiles = profiles.Where(p =>
                    p.Host != _selectedExternalProfile.Host ||
                    p.Port != _selectedExternalProfile.Port ||
                    p.Database != _selectedExternalProfile.Database ||
                    p.Username != _selectedExternalProfile.Username ||
                    p.Password != _selectedExternalProfile.Password
                ).ToList();

                _settings.SaveExternalDbProfiles(profiles);
                LoadExternalConnectionsList();
            }
        }

        private void ConnectNowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedExternalProfile == null) return;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.ConnectToExternalProfileTemporary(_selectedExternalProfile);
            });
        }
        private string BuildConnectionStringFromProfile(ExternalDbProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.Host) || string.IsNullOrWhiteSpace(profile.Database))
                return null;

            string port = profile.Port.ToString();
            if (string.IsNullOrWhiteSpace(port) || port == "0") port = "3306";
            string password = profile.Password ?? "";

            return $"Server={profile.Host};Port={port};Database={profile.Database};Uid={profile.Username};Pwd={password};";
        }
        #endregion

        #region Font
        private void MainWindowFontButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsManager();
            // Use the SAVED font settings (not MainWindow's live UI)
            var fontPicker = new FontMainWindow(
                new FontFamily(settings.DefaultMainFontFamily),
                ParseFontWeight(settings.DefaultMainFontWeight),
                ParseFontStyle(settings.DefaultMainFontStyle),
                settings
            )
            {
                Owner = this
            };

            fontPicker.ShowDialog();
        }

        // Reuse these helpers (copy from MainWindow if needed)
        private static FontWeight ParseFontWeight(string weightStr)
        {
            switch (weightStr)
            {
                case "Bold": return FontWeights.Bold;
                case "Black": return FontWeights.Black;
                case "ExtraBold": return FontWeights.ExtraBold;
                case "DemiBold": return FontWeights.DemiBold;
                case "Light": return FontWeights.Light;
                case "ExtraLight": return FontWeights.ExtraLight;
                case "Thin": return FontWeights.Thin;
                default: return FontWeights.Normal;
            }
        }

        private static FontStyle ParseFontStyle(string styleStr)
        {
            switch (styleStr)
            {
                case "Italic": return FontStyles.Italic;
                case "Oblique": return FontStyles.Oblique;
                default: return FontStyles.Normal;
            }
        }
        private void RequirementsButton_Click(object sender, RoutedEventArgs e)
        {
            var reqWindow = new ExternalRequirementsWindow();
            reqWindow.Show(); // No Owner = independent window
        }
        #endregion

        #region Application - Startup & Tray
        private bool _isInitializing = true;
        private void LaunchOnStartupCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            try
            {
                SetStartupRegistryKey(enable: true);
                _settings.LaunchOnStartup = true;
                _settings.Save();
                // Optional: quiet success
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to enable startup launch:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                LaunchOnStartupCheckBox.IsChecked = false;
            }
        }

        private void LaunchOnStartupCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            try
            {
                SetStartupRegistryKey(enable: false);
                _settings.LaunchOnStartup = false;
                _settings.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to disable startup launch:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                LaunchOnStartupCheckBox.IsChecked = true;
            }
        }
        private void SetStartupRegistryKey(bool enable)
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true))
            {
                if (key == null)
                    throw new InvalidOperationException("Unable to access startup registry key.");

                string appName = "Protes";
                if (enable)
                {
                    // Get the path to the current executable
                    string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    key.SetValue(appName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(appName, throwOnMissingValue: false);
                }
            }
        }
        private void MinimizeToSystemTray_Checked(object sender, RoutedEventArgs e)
        {
            _settings.MinimizeToTray = MinimizeToSystemTray.IsChecked == true;
        }
        private void CloseToSystemTray_Checked(object sender, RoutedEventArgs e)
        {
            _settings.CloseToTray = CloseToSystemTray.IsChecked == true;
        }
        #endregion

        #region Application - Shell Integration
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
            const string progId = "Protes.DatabaseFile"; // Match RegisterShellNew

            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{extension}"); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{progId}"); } catch { }
        }

        private void SendToCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (MessageBox.Show(
                "This will add 'Protes (Import)' to the Windows 'Send to' context menu.\n\n" +
                "When you right-click a file in File Explorer and choose 'Send to → Protes (Import)',\n" +
                "the (*.txt, *.md or correct format CSV) file will be imported into your current Protes database.\n\n" +
                "Allow this?",
                "Protes", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    RegisterSendToShortcut();
                    _settings.SendToIntegrationEnabled = true;
                    _settings.Save();
                    MessageBox.Show("Send to integration enabled successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to enable Send to integration:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                    SendToCheckBox.IsChecked = false;
                }
            }
            else
            {
                SendToCheckBox.IsChecked = false;
            }
        }

        private void SendToCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (MessageBox.Show(
                "Remove 'Protes (Import)' from the Windows 'Send to' menu?",
                "Protes", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    UnregisterSendToShortcut();
                    _settings.SendToIntegrationEnabled = false;
                    _settings.Save();
                    MessageBox.Show("Send to integration removed.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to remove integration:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                    SendToCheckBox.IsChecked = true; // revert
                }
            }
            else
            {
                SendToCheckBox.IsChecked = true;
            }
        }
        private void RegisterSendToShortcut()
        {
            string sendToFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "SendTo"
            );

            string shortcutPath = Path.Combine(sendToFolder, "Protes (Import).lnk");
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);

            shortcut.TargetPath = exePath;
            shortcut.Arguments = "-import"; // ✅ REMOVED "%1" — Windows supplies file automatically
            shortcut.WindowStyle = 1;
            shortcut.Description = "Import file into Protes";
            shortcut.IconLocation = exePath + ",0";
            shortcut.Save();
        }

        private void UnregisterSendToShortcut()
        {
            string sendToFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "SendTo"
            );
            string shortcutPath = Path.Combine(sendToFolder, "Protes (Import).lnk");

            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        private void SendToNoteEditorCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (MessageBox.Show(
                "This will add 'Pro Note' to the Windows 'Send to' context menu.\n\n" +
                "When you right-click a file and choose 'Send to → Pro Note',\n" +
                "Protes will open the Note Editor with the file's content loaded.\n" +
                "You can then save it as a new note in your database.\n\n" +
                "Note: A database connection is required.\n\n" +
                "Allow this?",
                "Protes", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    RegisterSendToNoteEditorShortcut();
                    _settings.SendToNoteEditorEnabled = true;
                    _settings.Save();
                    MessageBox.Show("Send to Note Editor integration enabled successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to enable Send to Note Editor integration:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                    SendToNoteEditorCheckBox.IsChecked = false;
                }
            }
            else
            {
                SendToNoteEditorCheckBox.IsChecked = false;
            }
        }

        private void SendToNoteEditorCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (MessageBox.Show(
                "Remove 'Pro Note' from the Windows 'Send to' menu?",
                "Protes", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    UnregisterSendToNoteEditorShortcut();
                    _settings.SendToNoteEditorEnabled = false;
                    _settings.Save();
                    MessageBox.Show("Send to Note Editor integration removed.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to remove integration:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                    SendToNoteEditorCheckBox.IsChecked = true; // revert
                }
            }
            else
            {
                SendToNoteEditorCheckBox.IsChecked = true;
            }
        }
        private void RegisterSendToNoteEditorShortcut()
        {
            string sendToFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "SendTo"
            );

            string shortcutPath = Path.Combine(sendToFolder, "Pro Note.lnk");
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);

            shortcut.TargetPath = exePath;
            shortcut.Arguments = "-noteeditor"; 
            shortcut.WindowStyle = 1;
            shortcut.Description = "Open file content in Pro Note Editor";
            shortcut.IconLocation = exePath + ",0";
            shortcut.Save();
        }

        private void UnregisterSendToNoteEditorShortcut()
        {
            string sendToFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "SendTo"
            );
            string shortcutPath = Path.Combine(sendToFolder, "Pro Note.lnk"); 

            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        #endregion

        #region Application - Connection Automation
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
        private void SyncAutoSwitchUiState()
        {
            bool autoConnectOnSwitch = _settings.AutoConnectOnSwitch;

            // Disable AutoDisconnect checkbox when AutoConnectOnSwitch is ON
            AutoDisconnectOnSwitchCheckBox.IsEnabled = !autoConnectOnSwitch;

            // Enforce AutoDisconnect = true when AutoConnectOnSwitch is enabled
            if (autoConnectOnSwitch && !_settings.AutoDisconnectOnSwitch)
            {
                _settings.AutoDisconnectOnSwitch = true;
                AutoDisconnectOnSwitchCheckBox.IsChecked = true;
            }
        }
        #endregion

        #region Application - Notifications
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
        private void ShowGateEntryWarningCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _settings.ShowGateEntryWarning = ShowGateEntryWarningCheckBox.IsChecked == true;
        }
        #endregion

        #region Toolbar - Visibility & Icons
        private void ViewToolbarMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewMainToolbar = ViewToolbarMenuItem.IsChecked == true;
        }
        private void ViewToolbarOptionsInMenuCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarOptionsInMenu = ViewToolbarOptionsInMenuCheckBox.IsChecked == true;
            _mainWindow.RefreshToolbarSettings();
        }

        private void ViewToolbarConnectMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarConnect = ViewToolbarConnectMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }

        private void ViewToolbarLocalDBMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarLocalDB = ViewToolbarLocalDBMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }

        private void ViewToolbarACOSMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarACOS = ViewToolbarACOSMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }

        private void ViewToolbarImpExMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarImpEx = ViewToolbarImpExMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }

        private void ViewToolbarSearchMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarSearch = ViewToolbarSearchMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }
        private void ViewToolbarCalcMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarCalculator = ViewToolbarCalcMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }
        private void ViewToolbarGateEntryMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarGateEntry = ViewToolbarGateEntryMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }
        private void ViewToolbarSettingsMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarSettings = ViewToolbarSettingsMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }

        private void ViewToolbarNoteToolsMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarNoteTools = ViewToolbarNoteToolsMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }

        private void ViewToolbarCopyPasteMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarCopyPaste = ViewToolbarCopyPasteMenuItem.IsChecked == true;
            _settings.Save();
            _mainWindow.RefreshToolbarSettings();
        }
        #endregion

        #region Window Management
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        #endregion

        #region Supporting Types
        public class DbFileInfo
        {
            public string FileName { get; set; }
            public string FullPath { get; set; }
            public bool IsImported { get; set; }
            public string DisplayNameWithIndicator { get; set; } 
        }
        #endregion
    }
}