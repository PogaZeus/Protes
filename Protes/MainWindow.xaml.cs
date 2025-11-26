using Protes.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Protes.Properties;

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
                        var newModified = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

                        // Update database
                        using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                        {
                            conn.Open();
                            using (var cmd = new System.Data.SQLite.SQLiteCommand(
                                "UPDATE Notes SET Title = @title, LastModified = @now WHERE Id = @id", conn))
                            {
                                cmd.Parameters.AddWithValue("@id", fullNote.Id);
                                cmd.Parameters.AddWithValue("@title", newTitle);
                                cmd.Parameters.AddWithValue("@now", newModified);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        // Update cache and UI with new values
                        fullNote.Title = newTitle;
                        fullNote.LastModified = newModified;
                        _editingItem.Title = newTitle;
                        _editingItem.LastModified = newModified;
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

            // Load and apply AutoConnect setting
            AutoConnectCheckBox.IsChecked = Properties.Settings.Default.AutoConnect;

            UpdateDatabaseModeCheckmarks();
        }

        private void UpdateDatabaseModeCheckmarks()
        {
            var localItem = (MenuItem)OptionsMenu.Items[0];
            var externalItem = (MenuItem)OptionsMenu.Items[1];
            localItem.IsChecked = (_currentMode == DatabaseMode.Local);
            externalItem.IsChecked = (_currentMode == DatabaseMode.External);
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
            var conn = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter connection string (e.g., Server=localhost;Database=protes;Uid=user;Pwd=pass;):",
                "External Database", "");

            if (!string.IsNullOrWhiteSpace(conn))
            {
                _currentMode = DatabaseMode.External;
                Properties.Settings.Default.DatabaseMode = "External";
                Properties.Settings.Default.Save();
                UpdateDatabaseModeCheckmarks();
                UpdateStatusBar();
                MessageBox.Show("External database configured.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                LoadPersistedSettings();
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
                    using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new System.Data.SQLite.SQLiteCommand(
                            @"CREATE TABLE IF NOT EXISTS Notes (
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

                LoadNotesFromDatabase();
                _isConnected = true;
                NotesDataGrid.Visibility = Visibility.Visible;
                DisconnectedPlaceholder.Visibility = Visibility.Collapsed;
                UpdateStatusBar();
                UpdateButtonStates(); // Enable note actions, disable Connect button
                MessageBox.Show("Connected successfully!", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
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
                            using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                            {
                                conn.Open();
                                using (var cmd = new System.Data.SQLite.SQLiteCommand("DELETE FROM Notes WHERE Id = @id", conn))
                                {
                                    cmd.Parameters.AddWithValue("@id", fullNote.Id);
                                    cmd.ExecuteNonQuery();
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

        private void SaveNoteToDatabase(string title, string content, string tags)
        {
            try
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
            if (_currentMode != DatabaseMode.Local) return;

            var notes = new List<NoteItem>();
            _fullNotesCache.Clear();

            try
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
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update note: {ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

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
}