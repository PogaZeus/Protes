using MySqlConnector;
using Protes.Properties;
using Protes.Views;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Protes
{
    public partial class MainWindow : Window
    {
        private string _databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes", "notes.db");
        private DatabaseMode _currentMode = DatabaseMode.None;
        private DatabaseMode _pendingModeSwitch = DatabaseMode.None;
        private DatabaseMode _connectedMode = DatabaseMode.None;
        private bool _isConnected = false;
        private List<FullNote> _fullNotesCache = new List<FullNote>();
        private NoteItem _editingItem;
        private string _originalTitle;
        private string _externalConnectionString = "";
        private bool _isToolbarVisible = true;
        public MainWindow()
        {
            string lastPath = Properties.Settings.Default.LastLocalDatabasePath;
            if (!string.IsNullOrWhiteSpace(lastPath) && File.Exists(lastPath))
            {
                _databasePath = lastPath;
            }
            else
            {
                // Fallback to default location
                _databasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Protes", "notes.db"
                );
            }
            InitializeComponent();
            LoadPersistedSettings();
            EnsureAppDataFolder();
            UpdateStatusBar();
            UpdateButtonStates();

            // Load View settings
            ViewTitleMenuItem.IsChecked = Properties.Settings.Default.ViewMainWindowTitle;
            ViewTagsMenuItem.IsChecked = Properties.Settings.Default.ViewMainWindowTags;
            ViewModifiedMenuItem.IsChecked = Properties.Settings.Default.ViewMainWindowMod;
            ViewToolbarMenuItem.IsChecked = Properties.Settings.Default.ViewMainToolbar;
            _isToolbarVisible = Properties.Settings.Default.ViewMainToolbar;

            // Apply initial state
            UpdateDataGridColumns();
            UpdateToolbarVisibility();

            Loaded += MainWindow_Loaded;
            NotesDataGrid.SelectionChanged += (s, e) => UpdateButtonStates();
            var showNotifications = Properties.Settings.Default.ShowNotifications;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded; // prevent multiple calls

            if (Properties.Settings.Default.AutoConnect)
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    Connect_Click(this, new RoutedEventArgs());
                }
                else if (_currentMode == DatabaseMode.External)
                {
                    var host = Properties.Settings.Default.External_Host;
                    var database = Properties.Settings.Default.External_Database;
                    if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(database))
                    {
                        Connect_Click(this, new RoutedEventArgs());
                    }
                    else
                    {
                        // This one is an error, so it's OK to show as top-level
                        MessageBox.Show(
                            "Auto-connect failed: External database configuration is incomplete.\n\n" +
                            "Please go to Settings → External Database to configure your connection.",
                            "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        // ===== VIEW MENU =====
        private void ViewTitleMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = ViewTitleMenuItem.IsChecked == true;
            Properties.Settings.Default.ViewMainWindowTitle = isChecked;
            Properties.Settings.Default.Save();
            UpdateDataGridColumns();
        }

        private void ViewTagsMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = ViewTagsMenuItem.IsChecked == true;
            Properties.Settings.Default.ViewMainWindowTags = isChecked;
            Properties.Settings.Default.Save();
            UpdateDataGridColumns();
        }

        private void ViewModifiedMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = ViewModifiedMenuItem.IsChecked == true;
            Properties.Settings.Default.ViewMainWindowMod = isChecked;
            Properties.Settings.Default.Save();
            UpdateDataGridColumns();
        }

        private void ViewToolbarMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = ViewToolbarMenuItem.IsChecked == true;
            Properties.Settings.Default.ViewMainToolbar = isChecked;
            Properties.Settings.Default.Save();
            _isToolbarVisible = isChecked;
            UpdateToolbarVisibility();
        }
        private void ZoomInMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement zoom for MainWindow (e.g., DataGrid font size)
            MessageBox.Show("Zoom In (MainWindow) - Not implemented yet", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ZoomOutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Zoom Out (MainWindow) - Not implemented yet", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RestoreZoomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Restore Zoom (MainWindow) - Not implemented yet", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Toolbar options
        private void AutoConnectCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AutoConnect = AutoConnectCheckBox.IsChecked == true;
            Properties.Settings.Default.Save();
        }

        // helper methods
        private void UpdateButtonStates()
        {
            bool hasSelection = NotesDataGrid?.SelectedItem != null;

            // Toolbar buttons
            NewNoteButton.IsEnabled = _isConnected;
            EditNoteButton.IsEnabled = _isConnected && hasSelection;
            DeleteNoteButton.IsEnabled = _isConnected && hasSelection;
            SearchBox.IsEnabled = _isConnected;
            ConnectIconBtn.IsEnabled = !_isConnected;
            DisconnectIconBtn.IsEnabled = _isConnected;

            // File menu items
            NewNoteMenuItem.IsEnabled = _isConnected;
            EditNoteMenuItem.IsEnabled = _isConnected && hasSelection;
            DeleteNoteMenuItem.IsEnabled = _isConnected && hasSelection;

            // File menu Connect/Disconnect
            if (MainMenu?.Items.Count > 0)
            {
                var fileMenuItem = MainMenu.Items[0] as MenuItem; // "_File"
                if (fileMenuItem?.Items.Count >= 6) // New, Edit, Delete, Sep, Connect, Disconnect
                {
                    // Connect = index 4, Disconnect = index 5
                    var connectMenuItem = fileMenuItem.Items[4] as MenuItem;
                    var disconnectMenuItem = fileMenuItem.Items[5] as MenuItem;
                    connectMenuItem?.SetValue(MenuItem.IsEnabledProperty, !_isConnected);
                    disconnectMenuItem?.SetValue(MenuItem.IsEnabledProperty, _isConnected);
                }
            }
        }


        //Data Grid (Database Content)
        private void UpdateDataGridColumns()
        {
            if (NotesDataGrid.Columns.Count >= 4)
            {
                NotesDataGrid.Columns[0].Visibility = ViewTitleMenuItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                NotesDataGrid.Columns[2].Visibility = ViewTagsMenuItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                NotesDataGrid.Columns[3].Visibility = ViewModifiedMenuItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private void NotesDataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Get the element that was clicked
            var originalSource = e.OriginalSource as DependencyObject;

            // Walk up the visual tree to see if click was on a DataGridRow or Cell
            while (originalSource != null && !(originalSource is DataGridRow))
            {
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            // If we didn't find a row, click was on empty space → deselect
            if (originalSource == null)
            {
                NotesDataGrid.SelectedItem = null;
                NotesDataGrid.CurrentCell = new DataGridCellInfo(); // Optional: clear current cell
                UpdateButtonStates(); // Update Edit/Delete menu/button states
                e.Handled = true; // Prevent further processing
            }
        }

        // ===== INLINE GRID EDITING (Title only) =====

        private void NotesDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column.DisplayIndex == 0) // Title column
            {
                _editingItem = e.Row.Item as NoteItem;
                _originalTitle = _editingItem?.Title;
            }
            else
            {
                e.Cancel = true; // Only Title is editable
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

            if (newTitle == _originalTitle)
                return;

            var result = MessageBox.Show(
                $"Update note title from:\n\n\"{_originalTitle}\"\n\nto:\n\n\"{newTitle}\"?",
                "Confirm Title Change",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var fullNote = _fullNotesCache.Find(n => n.Id == _editingItem.Id);
                if (fullNote != null)
                {
                    try
                    {
                        UpdateNoteInDatabase(fullNote.Id, newTitle, fullNote.Content, fullNote.Tags);
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
                _editingItem.Title = _originalTitle;
            }

            _editingItem = null;
            _originalTitle = null;
        }

        // Toolbar Visibility
        private void UpdateToolbarVisibility()
        {
            var toolbar = (StackPanel)FindName("ToolbarStackPanel");
            if (toolbar != null)
            {
                toolbar.Visibility = _isToolbarVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ===== LIFECYCLE & SETTINGS =====

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

            AutoConnectCheckBox.IsChecked = Properties.Settings.Default.AutoConnect;
            UpdateDatabaseModeCheckmarks();
        }

        public void UpdateDatabaseModeCheckmarks()
        {
            var localItem = (MenuItem)OptionsMenu.Items[0];
            var externalItem = (MenuItem)OptionsMenu.Items[1];

            localItem.IsChecked = (_currentMode == DatabaseMode.Local);
            externalItem.IsChecked = (_currentMode == DatabaseMode.External);
            externalItem.IsEnabled = !string.IsNullOrWhiteSpace(Properties.Settings.Default.External_Host) &&
                                    !string.IsNullOrWhiteSpace(Properties.Settings.Default.External_Database);
        }

        public void UpdateStatusBar()
        {
            if (!_isConnected)
            {
                ConnectionStatusText.Text = "Disconnected";
                DatabaseModeText.Text = "—";
                DatabaseSwitchText.Text = "—";
                NoteCountText.Text = "";
                return;
            }

            ConnectionStatusText.Text = "Connected";

            // ALWAYS show ACTUAL connected mode in DatabaseModeText
            if (_connectedMode == DatabaseMode.Local)
            {
                DatabaseModeText.Text = $"Local ({_databasePath})";
            }
            else if (_connectedMode == DatabaseMode.External)
            {
                var host = Properties.Settings.Default.External_Host ?? "localhost";
                var port = Properties.Settings.Default.External_Port?.ToString() ?? "3306";
                var database = Properties.Settings.Default.External_Database ?? "unknown";
                DatabaseModeText.Text = $"External ({host}:{port}/{database}/Notes)";
            }
            else
            {
                DatabaseModeText.Text = "—";
            }

            // Show pending message in DatabaseSwitchText (if different from connected mode)
            if (_pendingModeSwitch != DatabaseMode.None && _pendingModeSwitch != _connectedMode)
            {
                string pendingName = _pendingModeSwitch == DatabaseMode.Local ? "Local" : "External";
                DatabaseSwitchText.Text = $"Switched to '{pendingName}' Database. Disconnect from the current database then connect to complete the switch";
            }
            else
            {
                DatabaseSwitchText.Text = "—"; // Hide if no pending or same as connected
            }
        }

        // ===== CONNECTION =====

        private void UseLocalDb_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                // If already connected to Local, just clear pending
                if (_connectedMode == DatabaseMode.Local)
                {
                    _pendingModeSwitch = DatabaseMode.None;
                    _currentMode = DatabaseMode.Local;
                    Properties.Settings.Default.DatabaseMode = "Local";
                    Properties.Settings.Default.Save();
                    UpdateDatabaseModeCheckmarks();
                    UpdateStatusBar();
                    return;
                }

                // If connected to External and AutoDisconnect is OFF → set pending
                if (!Properties.Settings.Default.AutoDisconnectOnSwitch)
                {
                    _pendingModeSwitch = DatabaseMode.Local;
                    _currentMode = DatabaseMode.Local;
                    Properties.Settings.Default.DatabaseMode = "Local";
                    Properties.Settings.Default.Save();
                    UpdateDatabaseModeCheckmarks();
                    UpdateStatusBar();
                    return;
                }
            }

            // Full switch logic
            _currentMode = DatabaseMode.Local;
            Properties.Settings.Default.DatabaseMode = "Local";
            Properties.Settings.Default.Save();
            UpdateDatabaseModeCheckmarks();

            if (Properties.Settings.Default.AutoConnectOnSwitch)
            {
                try
                {
                    using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new SQLiteCommand(@"
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

                    if (_isConnected && Properties.Settings.Default.AutoDisconnectOnSwitch)
                    {
                        Disconnect_Click(this, new RoutedEventArgs());
                    }

                    LoadNotesFromDatabase();
                    _isConnected = true;
                    _connectedMode = DatabaseMode.Local; // 👈 Set connected mode
                    NotesDataGrid.Visibility = Visibility.Visible;
                    DisconnectedPlaceholder.Visibility = Visibility.Collapsed;
                    UpdateButtonStates();
                    UpdateStatusBar();

                    if (Properties.Settings.Default.ShowNotifications)
                    {
                        MessageBox.Show("Local database (SQLite) selected.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to connect to local database:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                    _isConnected = false;
                    _connectedMode = DatabaseMode.None;
                    UpdateStatusBar();
                }
            }
            else
            {
                _isConnected = false;
                _connectedMode = DatabaseMode.None;
                NotesDataGrid.ItemsSource = null;
                NotesDataGrid.Visibility = Visibility.Collapsed;
                DisconnectedPlaceholder.Visibility = Visibility.Visible;
                UpdateButtonStates();
                UpdateStatusBar();
            }
        }

        // Same as above BUT Public method to switch to AND connect a local database (from another Window such as Settings)
        public void SwitchToLocalDatabase(string databasePath)
        {
            _currentMode = DatabaseMode.Local;
            _databasePath = databasePath;
            Properties.Settings.Default.DatabaseMode = "Local";
            Properties.Settings.Default.LastLocalDatabasePath = databasePath;
            Properties.Settings.Default.Save();

            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(@"
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

                if (_isConnected && Properties.Settings.Default.AutoDisconnectOnSwitch)
                {
                    _isConnected = false;
                }

                LoadNotesFromDatabase();
                _isConnected = true;
                NotesDataGrid.Visibility = Visibility.Visible;
                DisconnectedPlaceholder.Visibility = Visibility.Collapsed;
                UpdateStatusBar();
                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect to local database:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                _isConnected = false;
                UpdateStatusBar();
                UpdateButtonStates();
            }
        }

        private void UseExternalDb_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                // If already connected to External, just clear pending
                if (_connectedMode == DatabaseMode.External)
                {
                    _pendingModeSwitch = DatabaseMode.None;
                    _currentMode = DatabaseMode.External;
                    Properties.Settings.Default.DatabaseMode = "External";
                    Properties.Settings.Default.Save();
                    UpdateDatabaseModeCheckmarks();
                    UpdateStatusBar();
                    return;
                }

                // If connected to Local and AutoDisconnect is OFF → set pending
                if (!Properties.Settings.Default.AutoDisconnectOnSwitch)
                {
                    _pendingModeSwitch = DatabaseMode.External;
                    _currentMode = DatabaseMode.External;
                    Properties.Settings.Default.DatabaseMode = "External";
                    Properties.Settings.Default.Save();
                    UpdateDatabaseModeCheckmarks();
                    UpdateStatusBar();
                    return;
                }
            }

            // Full switch logic
            _currentMode = DatabaseMode.External;
            Properties.Settings.Default.DatabaseMode = "External";
            Properties.Settings.Default.Save();
            UpdateDatabaseModeCheckmarks();

            if (Properties.Settings.Default.AutoConnectOnSwitch)
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

                if (_isConnected && Properties.Settings.Default.AutoDisconnectOnSwitch)
                {
                    Disconnect_Click(this, new RoutedEventArgs());
                }

                ConnectToExternalDatabase(connString);
            }
            else
            {
                _isConnected = false;
                _connectedMode = DatabaseMode.None;
                NotesDataGrid.ItemsSource = null;
                NotesDataGrid.Visibility = Visibility.Collapsed;
                DisconnectedPlaceholder.Visibility = Visibility.Visible;
                UpdateButtonStates();
                UpdateStatusBar();
            }
        }

        private void ConnectToExternalDatabase(string connectionString)
        {
            try
            {
                TestExternalConnection(connectionString);
                _externalConnectionString = connectionString;
                FinishConnection();
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
                    EnsureNotesTableExistsLocal();
                }
                else if (_currentMode == DatabaseMode.External)
                {
                    var connString = BuildExternalConnectionString();
                    if (connString == null)
                    {
                        MessageBox.Show("External database configuration is incomplete.", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    TestExternalConnection(connString);
                    _externalConnectionString = connString;
                }
                FinishConnection();
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

        private void EnsureNotesTableExistsLocal()
        {
            using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(@"
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

        private void TestExternalConnection(string connectionString)
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
        }

        private void FinishConnection()
        {
            LoadNotesFromDatabase();
            _isConnected = true;
            _connectedMode = _currentMode;
            _pendingModeSwitch = DatabaseMode.None;
            NotesDataGrid.Visibility = Visibility.Visible;
            DisconnectedPlaceholder.Visibility = Visibility.Collapsed;
            UpdateStatusBar();
            UpdateButtonStates();

            if (Properties.Settings.Default.ShowNotifications)
            {
                MessageBox.Show("Connected successfully!", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public void SwitchDatabase(string newDatabasePath)
        {
            _databasePath = newDatabasePath;
            if (_isConnected)
            {
                Disconnect_Click(this, new RoutedEventArgs());
                Connect_Click(this, new RoutedEventArgs());
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            _isConnected = false;
            _connectedMode = DatabaseMode.None;
            _pendingModeSwitch = DatabaseMode.None;
            NotesDataGrid.ItemsSource = null;
            NotesDataGrid.Visibility = Visibility.Collapsed;
            DisconnectedPlaceholder.Visibility = Visibility.Visible;
            UpdateStatusBar();
            UpdateButtonStates();

            // Show notification only if enabled
            if (Properties.Settings.Default.ShowNotifications)
            {
                MessageBox.Show("Disconnected.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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

            if (NotesDataGrid.SelectedItem == null || !(NotesDataGrid.SelectedItem is NoteItem selectedNote))
            {
                MessageBox.Show("Please select a note to edit.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fullNote = _fullNotesCache.Find(n => n.Id == selectedNote.Id);
            if (fullNote == null) return;

            var editor = new NoteEditorWindow(
                title: fullNote.Title,
                content: fullNote.Content,
                tags: fullNote.Tags,
                noteId: fullNote.Id,
                onSaveRequested: OnSaveNoteRequested
            );
            editor.Owner = this;
            editor.Show();
        }

        public void OpenEditorForNewlySavedNote(string title, string content, string tags)
        {
            LoadNotesFromDatabase(); // Refresh to get new note

            var newNote = _fullNotesCache.Find(n =>
                n.Title == title &&
                n.Content == content &&
                n.Tags == tags
            );

            if (newNote != null)
            {
                var editor = new NoteEditorWindow(
                    title: newNote.Title,
                    content: newNote.Content,
                    tags: newNote.Tags,
                    noteId: newNote.Id,
                    onSaveRequested: OnSaveNoteRequested
                );
                editor.Owner = this;
                editor.Show();
            }
            else
            {
                // Fallback: open as new note (rare)
                var editor = new NoteEditorWindow(
                    title: title,
                    content: content,
                    tags: tags,
                    noteId: null,
                    onSaveRequested: OnSaveNoteRequested
                );
                editor.Owner = this;
                editor.Show();
            }
        }

        private void NotesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            EditNoteButton_Click(sender, e);
        }

        private void NewNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to a database first.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editor = new NoteEditorWindow(
                noteId: null,
                onSaveRequested: OnSaveNoteRequested
            );
            editor.Owner = this;
            editor.Show();
        }

        private void OnSaveNoteRequested(string title, string content, string tags, long? noteId)
        {
            if (noteId.HasValue)
            {
                UpdateNoteInDatabase(noteId.Value, title, content, tags);
            }
            else
            {
                SaveNoteToDatabase(title, content, tags);
            }
            LoadNotesFromDatabase();
        }

        private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to a database first.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!(NotesDataGrid.SelectedItem is NoteItem selectedNote))
            {
                MessageBox.Show("Please select a note to delete.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete the note titled:\n\n\"{selectedNote.Title}\"?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var fullNote = _fullNotesCache.Find(n => n.Id == selectedNote.Id);
            if (fullNote == null) return;

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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isConnected)
            {
                string selectedField = (SearchFieldComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Title";
                LoadNotesFromDatabase(SearchBox.Text, selectedField);
            }
        }

        // ===== DATABASE CRUD =====

        private void SaveNoteToDatabase(string title, string content, string tags)
        {
            try
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new SQLiteCommand(
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
                            cmd.Parameters.AddWithValue("@now", DateTime.Now);
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

        private void UpdateNoteInDatabase(long id, string title, string content, string tags)
        {
            try
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new SQLiteCommand(
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

        private void LoadNotesFromDatabase(string searchTerm = "", string searchField = "All")
        {
            var notes = new List<NoteItem>();
            _fullNotesCache.Clear();
            string likePattern = EscapeLikePattern(searchTerm);

            try
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    LoadNotesFromLocal(likePattern, searchField, notes);
                }
                else if (_currentMode == DatabaseMode.External)
                {
                    LoadNotesFromExternal(likePattern, searchField, notes);
                }

                NotesDataGrid.ItemsSource = notes;
                NoteCountText.Text = $"{notes.Count} Note{(notes.Count == 1 ? "" : "s")}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load notes:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadNotesFromLocal(string likePattern, string searchField, List<NoteItem> notes)
        {
            using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
            {
                conn.Open();
                string query = GetLoadQuery(searchField, isExternal: false);
                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@search", likePattern);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            AppendNote(reader, notes, isExternal: false);
                        }
                    }
                }
            }
        }

        private void LoadNotesFromExternal(string likePattern, string searchField, List<NoteItem> notes)
        {
            using (var conn = new MySqlConnection(_externalConnectionString))
            {
                conn.Open();
                string query = GetLoadQuery(searchField, isExternal: true);
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@search", likePattern);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            AppendNote(reader, notes, isExternal: true);
                        }
                    }
                }
            }
        }

        private string GetLoadQuery(string searchField, bool isExternal)
        {
            string escapeClause = isExternal ? "ESCAPE '\\\\'" : "ESCAPE '\\'";
            string baseQuery = "SELECT Id, Title, Content, Tags, LastModified FROM Notes";

            if (searchField == "All")
            {
                return $@"{baseQuery}
                          WHERE Title LIKE @search {escapeClause}
                             OR Content LIKE @search {escapeClause}
                             OR Tags LIKE @search {escapeClause}
                          ORDER BY LastModified DESC";
            }
            else
            {
                string col = GetColumnName(searchField);
                return $@"{baseQuery}
                          WHERE {col} LIKE @search {escapeClause}
                          ORDER BY LastModified DESC";
            }
        }

        private void AppendNote(System.Data.IDataReader reader, List<NoteItem> notes, bool isExternal)
        {
            var id = isExternal ? Convert.ToInt64(reader["Id"]) : (long)reader["Id"];
            var title = reader["Title"].ToString();
            var content = reader["Content"].ToString();
            var tags = reader["Tags"].ToString();
            string modified;

            if (isExternal && reader["LastModified"] is DateTime dt)
            {
                modified = dt.ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                modified = reader["LastModified"].ToString();
            }

            var preview = content.Length > 60 ? content.Substring(0, 57) + "..." : content;

            _fullNotesCache.Add(new FullNote { Id = id, Title = title, Content = content, Tags = tags, LastModified = modified });
            notes.Add(new NoteItem { Id = id, Title = title, Preview = preview, LastModified = modified, Tags = tags });
        }

        private string EscapeLikePattern(string input)
        {
            if (string.IsNullOrEmpty(input)) return "%";
            return "%" + input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        }

        private string GetColumnName(string searchField)
        {
            switch (searchField)
            {
                case "Title": return "Title";
                case "Content": return "Content";
                case "Tags": return "Tags";
                case "Modified": return "LastModified";
                default: return "Title";
            }
        }

        // ===== HELPERS =====

        internal string BuildExternalConnectionString()
        {
            var host = Properties.Settings.Default.External_Host;
            var port = Properties.Settings.Default.External_Port?.ToString() ?? "3306";
            var database = Properties.Settings.Default.External_Database;
            var username = Properties.Settings.Default.External_Username;
            var password = Properties.Settings.Default.External_Password ?? "";

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database))
                return null;

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

        public void SetDatabaseMode(DatabaseMode mode)
        {
            _currentMode = mode;
            UpdateDatabaseModeCheckmarks();
            UpdateStatusBar();
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new Protes.Views.AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
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