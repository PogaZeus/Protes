using Protes.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Protes.Properties;
using MySqlConnector;
using System.Data.SQLite;

namespace Protes
{
    public partial class MainWindow : Window
    {
        private string _databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes", "notes.db");
        private DatabaseMode _currentMode = DatabaseMode.None;
        private bool _isConnected = false;
        private List<FullNote> _fullNotesCache = new List<FullNote>();
        private NoteItem _editingItem;
        private string _originalTitle;
        private string _externalConnectionString = "";

        public MainWindow()
        {
            InitializeComponent();
            LoadPersistedSettings();
            EnsureAppDataFolder();
            UpdateStatusBar();
            UpdateButtonStates();

            // Auto-connect if enabled and mode is Local
            if (Properties.Settings.Default.AutoConnect && _currentMode == DatabaseMode.Local)
            {
                // Use dispatcher to ensure UI is ready
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Connect_Click(this, new RoutedEventArgs());
                }));
            }
        }

        private void AutoConnectCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AutoConnect = AutoConnectCheckBox.IsChecked == true;
            Properties.Settings.Default.Save();
        }

        private void UpdateButtonStates()
        {
            // Enable/disable note action buttons based on connection state
            NewNoteButton.IsEnabled = _isConnected;
            EditNoteButton.IsEnabled = _isConnected;
            DeleteNoteButton.IsEnabled = _isConnected;
            SearchBox.IsEnabled = _isConnected;

            // Enable/disable connection icon buttons
            ConnectIconBtn.IsEnabled = !_isConnected;
            DisconnectIconBtn.IsEnabled = _isConnected;

            // File menu items (Connect / Disconnect)
            if (MainMenu != null && MainMenu.Items.Count > 0)
            {
                var fileMenuItem = MainMenu.Items[0] as MenuItem; // "_File"
                if (fileMenuItem?.Items.Count >= 2)
                {
                    var connectMenuItem = fileMenuItem.Items[0] as MenuItem;   // "_Connect"
                    var disconnectMenuItem = fileMenuItem.Items[1] as MenuItem; // "_Disconnect"

                    if (connectMenuItem != null)
                        connectMenuItem.IsEnabled = !_isConnected;

                    if (disconnectMenuItem != null)
                        disconnectMenuItem.IsEnabled = _isConnected;
                }
            }
        }

        private void NotesDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column.DisplayIndex == 0) // Title column is first (index 0)
            {
                _editingItem = e.Row.Item as NoteItem;
                _originalTitle = _editingItem?.Title;
            }
            else
            {
                // Cancel edit for non-Title columns (extra safety)
                e.Cancel = true;
            }
        }

        private void NotesDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Cancel || e.Column.DisplayIndex != 0)
                return;

            if (_editingItem == null || string.IsNullOrEmpty(_originalTitle))
                return;

            var textBox = e.EditingElement as TextBox;
            var newTitle = textBox?.Text ?? _originalTitle;

            // Only prompt if value actually changed
            if (newTitle == _originalTitle)
                return;

            var result = MessageBox.Show(
                $"Update note title from:\n\n\"{_originalTitle}\" \n\nto:\n\n\"{newTitle}\"?",
                "Confirm Title Change",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Find by ID (robust to title/modification changes)
                var fullNote = _fullNotesCache.Find(n => n.Id == _editingItem.Id);

                if (fullNote != null)
                {
                    try
                    {
                        if (_currentMode == DatabaseMode.Local)
                        {
                            using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                            {
                                conn.Open();
                                using (var cmd = new SQLiteCommand(
                                    "UPDATE Notes SET Title = @title, LastModified = @now WHERE Id = @id", conn))
                                {
                                    cmd.Parameters.AddWithValue("@id", fullNote.Id);
                                    cmd.Parameters.AddWithValue("@title", newTitle);
                                    cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                        else if (_currentMode == DatabaseMode.External)
                        {
                            using (var conn = new MySqlConnection(_externalConnectionString))
                            {
                                conn.Open();
                                using (var cmd = new MySqlCommand(
                                    "UPDATE Notes SET Title = @title, LastModified = @now WHERE Id = @id", conn))
                                {
                                    cmd.Parameters.AddWithValue("@id", fullNote.Id);
                                    cmd.Parameters.AddWithValue("@title", newTitle);
                                    cmd.Parameters.AddWithValue("@now", DateTime.Now); // MySqlConnector handles DateTime
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }

                        // Update cache and UI with new values
                        var newModifiedStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                        fullNote.Title = newTitle;
                        fullNote.LastModified = newModifiedStr;
                        _editingItem.Title = newTitle;
                        _editingItem.LastModified = newModifiedStr;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to update title:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        _editingItem.Title = _originalTitle;
                    }
                }
            }
            else
            {
                // User canceled → revert
                _editingItem.Title = _originalTitle;
            }

            // Clean up
            _editingItem = null;
            _originalTitle = null;
        }

        private void EnsureAppDataFolder()
        {
            var folder = Path.GetDirectoryName(_databasePath);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }

        private void LoadPersistedSettings()
        {
            var savedMode = Properties.Settings.Default.DatabaseMode;
            _currentMode = (savedMode == "Local") ? DatabaseMode.Local :
                           (savedMode == "External") ? DatabaseMode.External :
                           DatabaseMode.Local;

            // No need to load _externalConnectionString — built dynamically
            AutoConnectCheckBox.IsChecked = Properties.Settings.Default.AutoConnect;
            UpdateDatabaseModeCheckmarks();
        }

        private void UpdateDatabaseModeCheckmarks()
        {
            var localItem = (MenuItem)OptionsMenu.Items[0];
            var externalItem = (MenuItem)OptionsMenu.Items[1];

            localItem.IsChecked = (_currentMode == DatabaseMode.Local);
            externalItem.IsChecked = (_currentMode == DatabaseMode.External);

            // Enable if Host and Database are configured
            var hasConfig = !string.IsNullOrWhiteSpace(Properties.Settings.Default.External_Host) &&
                            !string.IsNullOrWhiteSpace(Properties.Settings.Default.External_Database);
            externalItem.IsEnabled = hasConfig;
        }

        private void UpdateStatusBar()
        {
            ConnectionStatusText.Text = _isConnected ? "Connected" : "Disconnected";
            if (_currentMode == DatabaseMode.Local)
            {
                DatabaseModeText.Text = "Local";
            }
            else if (_currentMode == DatabaseMode.External)
            {
                DatabaseModeText.Text = "External";
            }
            else
            {
                DatabaseModeText.Text = "—";
            }
        }

        // ===== MENU HANDLERS =====

        private void UseLocalDb_Click(object sender, RoutedEventArgs e)
        {
            _currentMode = DatabaseMode.Local;
            Properties.Settings.Default.DatabaseMode = "Local";
            Properties.Settings.Default.Save();
            UpdateDatabaseModeCheckmarks();
            UpdateStatusBar();
            MessageBox.Show("Local database (SQLite) selected.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UseExternalDb_Click(object sender, RoutedEventArgs e)
        {
            var connString = BuildExternalConnectionString();
            if (connString == null)
            {
                MessageBox.Show(
                    "External database configuration is incomplete.\n\n" +
                    "Please go to Settings → External Database to configure your connection.",
                    "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentMode = DatabaseMode.External;
            Properties.Settings.Default.DatabaseMode = "External";
            Properties.Settings.Default.Save();

            UpdateDatabaseModeCheckmarks();
            UpdateStatusBar();

            ConnectToExternalDatabase(connString);
        }

        private void ConnectToExternalDatabase(string connectionString)
        {
            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand("SHOW TABLES LIKE 'Notes';", conn))
                    {
                        if (cmd.ExecuteScalar() == null)
                            throw new InvalidOperationException("Table 'Notes' not found in the external database.");
                    }
                }

                _externalConnectionString = connectionString; // Only store for loading notes
                LoadNotesFromDatabase();
                _isConnected = true;
                NotesDataGrid.Visibility = Visibility.Visible;
                DisconnectedPlaceholder.Visibility = Visibility.Collapsed;
                UpdateStatusBar();
                UpdateButtonStates();
                MessageBox.Show("Connected successfully!", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show($"Schema Error:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed:\n{ex.Message}\n\nInner: {ex.InnerException?.Message}",
                                "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == DatabaseMode.None)
            {
                MessageBox.Show("Please select a database mode first (Local or External).", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    // Existing SQLite logic
                    using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_databasePath};Version=3;"))
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
                }
                else if (_currentMode == DatabaseMode.External)
                {
                    // ✅ Build connection string from saved settings (fixes "missing" error)
                    var host = Properties.Settings.Default.External_Host;
                    var port = Properties.Settings.Default.External_Port?.ToString() ?? "3306";
                    var database = Properties.Settings.Default.External_Database;
                    var username = Properties.Settings.Default.External_Username;
                    var password = Properties.Settings.Default.External_Password ?? "";

                    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database))
                    {
                        MessageBox.Show("External database configuration is incomplete.", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(port) || port == "0")
                        port = "3306";

                    var connString = $"Server={host};Port={port};Database={database};Uid={username};Pwd={password};";

                    // Test connection
                    using (var conn = new MySqlConnection(connString))
                    {
                        conn.Open();

                        // Check if 'Notes' table exists
                        using (var cmd = new MySqlCommand("SHOW TABLES LIKE 'Notes';", conn))
                        {
                            var result = cmd.ExecuteScalar();
                            if (result == null)
                            {
                                throw new InvalidOperationException("Table 'Notes' not found in the external database.");
                            }
                        }
                    }

                    // ✅ Store for CRUD operations (Save, Update, Delete)
                    _externalConnectionString = connString;
                }

                LoadNotesFromDatabase();
                _isConnected = true;
                NotesDataGrid.Visibility = Visibility.Visible;
                DisconnectedPlaceholder.Visibility = Visibility.Collapsed;
                UpdateStatusBar();
                UpdateButtonStates();
                MessageBox.Show("Connected successfully!", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                // Custom error: table missing
                MessageBox.Show($"Schema Error:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                // General connection/error
                MessageBox.Show($"Connection failed:\n{ex.Message}\n\nInner: {ex.InnerException?.Message}",
                                "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Switch Local Database
        public void SwitchDatabase(string newDatabasePath)
        {
            if (!_isConnected)
            {
                // Just update path for next connect
                _databasePath = newDatabasePath;
                return;
            }

            // If currently connected, disconnect and reconnect
            _databasePath = newDatabasePath;
            Disconnect_Click(this, new RoutedEventArgs());
            Connect_Click(this, new RoutedEventArgs());
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            _isConnected = false;
            NotesDataGrid.ItemsSource = null;
            NotesDataGrid.Visibility = Visibility.Collapsed;
            DisconnectedPlaceholder.Visibility = Visibility.Visible;
            UpdateStatusBar();
            UpdateButtonStates(); // Disable note actions, enable Connect button
            MessageBox.Show("Disconnected.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_databasePath, this);
            settingsWindow.ShowDialog();
        }

        // ===== NOTE ACTIONS =====

        private void EditNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to a database first.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (NotesDataGrid.SelectedItem is NoteItem selectedNote)
            {
                var fullNote = _fullNotesCache.Find(n => n.Id == selectedNote.Id);

                if (fullNote != null)
                {
                    var editor = new NoteEditorWindow(fullNote.Title, fullNote.Content, fullNote.Tags);
                    if (editor.ShowDialog() == true)
                    {
                        UpdateNoteInDatabase(fullNote.Id, editor.NoteTitle, editor.NoteContent, editor.NoteTags);
                        LoadNotesFromDatabase();
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a note to edit.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void NewNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to a database first.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editor = new NoteEditorWindow();
            if (editor.ShowDialog() == true)
            {
                SaveNoteToDatabase(editor.NoteTitle, editor.NoteContent, editor.NoteTags);
                LoadNotesFromDatabase();
            }
        }

        private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to a database first.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (NotesDataGrid.SelectedItem is NoteItem selectedNote)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the note titled:\n\n\"{selectedNote.Title}\"?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var fullNote = _fullNotesCache.Find(n => n.Id == selectedNote.Id);

                    if (fullNote != null)
                    {
                        try
                        {
                            if (_currentMode == DatabaseMode.Local)
                            {
                                using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                                {
                                    conn.Open();
                                    using (var cmd = new SQLiteCommand("DELETE FROM Notes WHERE Id = @id", conn))
                                    {
                                        cmd.Parameters.AddWithValue("@id", fullNote.Id);
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                            }
                            else if (_currentMode == DatabaseMode.External)
                            {
                                using (var conn = new MySqlConnection(_externalConnectionString))
                                {
                                    conn.Open();
                                    using (var cmd = new MySqlCommand("DELETE FROM Notes WHERE Id = @id", conn))
                                    {
                                        cmd.Parameters.AddWithValue("@id", fullNote.Id);
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                            }

                            LoadNotesFromDatabase();
                            MessageBox.Show("Note deleted successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Failed to delete note:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a note to delete.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // You can implement real search logic later
        }
        private void SaveNoteToDatabase(string title, string content, string tags)
        {
            try
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new System.Data.SQLite.SQLiteCommand(
                            "INSERT INTO Notes (Title, Content, Tags, LastModified) VALUES (@title, @content, @tags, @now)", conn))
                        {
                            cmd.Parameters.AddWithValue("@title", title ?? "");
                            cmd.Parameters.AddWithValue("@content", content ?? "");
                            cmd.Parameters.AddWithValue("@tags", tags ?? "");
                            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                else if (_currentMode == DatabaseMode.External)
                {
                    using (var conn = new MySqlConnection(_externalConnectionString))
                    {
                        conn.Open();
                        using (var cmd = new MySqlCommand(
                            "INSERT INTO Notes (Title, Content, Tags, LastModified) VALUES (@title, @content, @tags, @now)", conn))
                        {
                            cmd.Parameters.AddWithValue("@title", title ?? "");
                            cmd.Parameters.AddWithValue("@content", content ?? "");
                            cmd.Parameters.AddWithValue("@tags", tags ?? "");
                            cmd.Parameters.AddWithValue("@now", DateTime.Now); // MySqlConnector handles DateTime correctly
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save note:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // TODO: Implement filtering
        }

        private void NotesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (NotesDataGrid.SelectedItem is NoteItem selectedNote && _isConnected)
            {
                var fullNote = _fullNotesCache.Find(n => n.Id == selectedNote.Id);

                if (fullNote != null)
                {
                    var editor = new NoteEditorWindow(fullNote.Title, fullNote.Content, fullNote.Tags);
                    if (editor.ShowDialog() == true)
                    {
                        UpdateNoteInDatabase(fullNote.Id, editor.NoteTitle, editor.NoteContent, editor.NoteTags);
                        LoadNotesFromDatabase();
                    }
                }
            }
        }

        private void LoadNotesFromDatabase()
        {
            var notes = new List<NoteItem>();
            _fullNotesCache.Clear();

            try
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new System.Data.SQLite.SQLiteCommand("SELECT Id, Title, Content, Tags, LastModified FROM Notes ORDER BY LastModified DESC", conn))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var id = (long)reader["Id"];
                                var title = reader["Title"].ToString();
                                var content = reader["Content"].ToString();
                                var tags = reader["Tags"].ToString();
                                var modified = reader["LastModified"].ToString();
                                var preview = content.Length > 60 ? content.Substring(0, 57) + "..." : content;

                                _fullNotesCache.Add(new FullNote { Id = id, Title = title, Content = content, Tags = tags, LastModified = modified });
                                notes.Add(new NoteItem { Id = id, Title = title, Preview = preview, LastModified = modified, Tags = tags });
                            }
                        }
                    }
                }
                else if (_currentMode == DatabaseMode.External)
                {
                    using (var conn = new MySqlConnection(_externalConnectionString))
                    {
                        conn.Open();
                        using (var cmd = new MySqlCommand("SELECT Id, Title, Content, Tags, LastModified FROM Notes ORDER BY LastModified DESC", conn))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var id = Convert.ToInt64(reader["Id"]);
                                var title = reader["Title"].ToString();
                                var content = reader["Content"].ToString();
                                var tags = reader["Tags"].ToString();
                                // If LastModified is DATETIME, convert to string
                                var modified = reader["LastModified"] is DateTime dt
                                    ? dt.ToString("yyyy-MM-dd HH:mm")
                                    : reader["LastModified"].ToString();

                                var preview = content.Length > 60 ? content.Substring(0, 57) + "..." : content;

                                _fullNotesCache.Add(new FullNote { Id = id, Title = title, Content = content, Tags = tags, LastModified = modified });
                                notes.Add(new NoteItem { Id = id, Title = title, Preview = preview, LastModified = modified, Tags = tags });
                            }
                        }
                    }
                }

                NotesDataGrid.ItemsSource = notes;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load notes:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateNoteInDatabase(long id, string title, string content, string tags)
        {
            try
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new System.Data.SQLite.SQLiteCommand(
                            @"UPDATE Notes 
                      SET Title = @title, Content = @content, Tags = @tags, LastModified = @now 
                      WHERE Id = @id", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.Parameters.AddWithValue("@title", title ?? "");
                            cmd.Parameters.AddWithValue("@content", content ?? "");
                            cmd.Parameters.AddWithValue("@tags", tags ?? "");
                            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                else if (_currentMode == DatabaseMode.External)
                {
                    using (var conn = new MySqlConnection(_externalConnectionString))
                    {
                        conn.Open();
                        using (var cmd = new MySqlCommand(
                            @"UPDATE Notes 
                      SET Title = @title, Content = @content, Tags = @tags, LastModified = @now 
                      WHERE Id = @id", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.Parameters.AddWithValue("@title", title ?? "");
                            cmd.Parameters.AddWithValue("@content", content ?? "");
                            cmd.Parameters.AddWithValue("@tags", tags ?? "");
                            cmd.Parameters.AddWithValue("@now", DateTime.Now);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update note: {ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ✅ Helper method — MUST be inside MainWindow
        internal string BuildExternalConnectionString()
        {
            var host = Properties.Settings.Default.External_Host;
            var port = Properties.Settings.Default.External_Port?.ToString() ?? "3306";
            var database = Properties.Settings.Default.External_Database;
            var username = Properties.Settings.Default.External_Username;
            var password = Properties.Settings.Default.External_Password ?? "";

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database))
                return null; // Incomplete config

            if (string.IsNullOrWhiteSpace(port) || port == "0")
                port = "3306";

            return $"Server={host};Port={port};Database={database};Uid={username};Pwd={password};";
        }

        internal void SetExternalConnectionString(string connectionString)
        {
            _externalConnectionString = connectionString;
        }

        public void TriggerConnect()
        {
            Connect_Click(this, new RoutedEventArgs());
        }

        // ✅ Public method to safely set mode from SettingsWindow
        public void SetDatabaseMode(DatabaseMode mode)
        {
            _currentMode = mode;
            UpdateDatabaseModeCheckmarks();
            UpdateStatusBar();
        }
    } // 👈 MainWindow class ends here

    // Now define nested/top-level types in namespace
    public class NoteItem
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string Preview { get; set; }
        public string LastModified { get; set; }
        public string Tags { get; set; }
    }

    public class FullNote
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Tags { get; set; }
        public string LastModified { get; set; }
    }

    public enum DatabaseMode
    {
        None,
        Local,
        External
    }
} // 👈 Namespace ends here