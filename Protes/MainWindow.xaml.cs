using MySqlConnector;
using Protes.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SWF = System.Windows.Forms;

namespace Protes
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes", "notes.db");
        private string _originalTitle;
        private string _externalConnectionString = "";
        private DatabaseMode _currentMode = DatabaseMode.None;
        private DatabaseMode _pendingModeSwitch = DatabaseMode.None;
        private DatabaseMode _connectedMode = DatabaseMode.None;
        private INoteRepository _noteRepository;
        private readonly SettingsManager _settings = new SettingsManager();
        private List<FullNote> _fullNotesCache = new List<FullNote>();
        private NoteItem _editingItem;
        private List<FullNote> _copiedNotes = new List<FullNote>();
        private SWF.NotifyIcon _notifyIcon;
        private readonly List<string> _pendingImportFiles = new List<string>();
        private DispatcherTimer _importDebounceTimer;
        private readonly object _importLock = new object();
        private bool _isToolbarVisible = true;
        private bool _isSelectMode = false;
        private bool _isConnected = false;
        private bool _isExplicitlyExiting = false;
        private bool _isDisposed = false;
        // Zoom settings
        private const double DEFAULT_ZOOM_POINTS = 11.0;
        private const double MIN_ZOOM_POINTS = 8.0;
        private const double MAX_ZOOM_POINTS = 24.0;
        // GateEntry (Not Secure from Hackers!)
        private bool _isGateLocked = false;
        private bool _hasGatePassword = false; // true if EntryGate table exists and has a password
        private const string GATE_TABLE_NAME = "EntryGate";

        #region Constructor and Initialization
        public MainWindow()
        {
            //External File incoming!
            this.Title = "[Protes] Pro Notes Database";

            string lastPath = _settings.LastLocalDatabasePath;
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
            LoadAvailableDatabases();
            EnsureAppDataFolder();
            UpdateStatusBar();
            UpdateButtonStates();
            SetupNotifyIcon();
            ApplyMainFontToDataGrid();
            UpdateDataGridColumns();
            UpdateToolbarVisibility();
            UpdateToolbarIconVisibility();
            UpdateFileMenuVisibility();
            RefreshLocalDbControls();

            // Load View settings
            _isToolbarVisible = _settings.ViewMainToolbar;
            ToolbarOptionsMenu.Visibility = _settings.ViewToolbarOptionsInMenu ? Visibility.Visible : Visibility.Collapsed;
            NotesDataGrid.DataContext = this;
            ViewTitleMenuItem.IsChecked = _settings.ViewMainWindowTitle;
            ViewTagsMenuItem.IsChecked = _settings.ViewMainWindowTags;
            ViewModifiedMenuItem.IsChecked = _settings.ViewMainWindowMod;
            ViewToolbarMenuItem.IsChecked = _settings.ViewMainToolbar;

            // Load zoom level
            double zoomPoints = _settings.DataGridZoom;
            if (zoomPoints < MIN_ZOOM_POINTS) zoomPoints = DEFAULT_ZOOM_POINTS;
            if (zoomPoints > MAX_ZOOM_POINTS) zoomPoints = DEFAULT_ZOOM_POINTS;
            NotesDataGrid.FontSize = zoomPoints * 96.0 / 72.0;
            // Use AddHandler to capture even if inner controls handled it
            this.AddHandler(KeyDownEvent, new KeyEventHandler(MainWindow_PreviewKeyDown), true);
    
            // Hook up row checkbox events to detect individual changes
            NotesDataGrid.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(OnRowCheckboxChanged));
            NotesDataGrid.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(OnRowCheckboxChanged));
            NotesDataGrid.SelectionChanged += (s, e) => UpdateButtonStates();
            // Right Click Menu
            MainContentGrid.ContextMenu = (ContextMenu)FindResource("DataGridContextMenu");
            NotesDataGrid_ContextMenuOpening(null, null);

            // For WM_COPYDATA to find reliably
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();

            var showNotifications = _settings.ShowNotifications;
            SelectCheckBoxColumn.Visibility = Visibility.Collapsed;

            // Initialize debounce timer for batch import
            _importDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Wait 500ms after last file
            };
            _importDebounceTimer.Tick += (s, e) =>
            {
                _importDebounceTimer.Stop();
                lock (_importLock)
                {
                    if (_pendingImportFiles.Count > 0)
                    {
                        // Open ONE Import window with all files
                        var files = new List<string>(_pendingImportFiles);
                        _pendingImportFiles.Clear();

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            OpenBatchImportWindow(files);
                        }));
                    }
                }
            };

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded; // prevent multiple calls

            if (!_settings.AutoConnect)
                return;

            // Auto-connect logic based on current mode
            if (_currentMode == DatabaseMode.Local)
            {
                AttemptAutoConnect();
            }
            else if (_currentMode == DatabaseMode.External)
            {
                var host = _settings.External_Host;
                var database = _settings.External_Database;

                if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database))
                {
                    ShowExternalConfigError();
                    return;
                }

                AttemptAutoConnect();
            }
        }

        private void EnsureAppDataFolder()
        {
            var folder = Path.GetDirectoryName(_databasePath);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }

        private void LoadPersistedSettings()
        {
            _currentMode = _settings.GetDatabaseMode();
            AutoConnectOnSwitchCheckBox.IsChecked = _settings.AutoConnectOnSwitch;
            UpdateDatabaseModeCheckmarks();
        }

        #endregion

        #region Database Connection Management
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
                    _noteRepository = new SqliteNoteRepository(_databasePath);
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
                    _noteRepository = new MySqlNoteRepository(connString);
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
        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            _isConnected = false;
            _connectedMode = DatabaseMode.None;
            _pendingModeSwitch = DatabaseMode.None;

            _isGateLocked = false;
            _hasGatePassword = false;
            UpdateGateUI();

            NotesDataGrid.ItemsSource = null;
            NotesDataGrid.Visibility = Visibility.Collapsed;
            DisconnectedPlaceholder.Visibility = Visibility.Visible;
            DisconnectedPlaceholder.Text = "Not connected to a database. Choose 'Local' or 'External Database' from Options.";

            UpdateStatusBar();
            UpdateButtonStates();
            NotesDataGrid_ContextMenuOpening(null, null);

            if (_settings.ShowNotifications)
            {
                MessageBox.Show(
                    "Disconnected.",
                    "Protes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        private void AttemptAutoConnect()
        {
            try
            {
                Connect_Click(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Auto-connect failed: {ex.Message}",
                    "Protes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        private void ShowExternalConfigError()
        {
            MessageBox.Show(
                "Auto-connect failed: External database configuration is incomplete.\n\n" +
                "Please go to Settings → External Database to configure your connection.",
                "Protes",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        private void UseLocalDb_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                // If already connected to Local, just clear pending
                if (_connectedMode == DatabaseMode.Local)
                {
                    _pendingModeSwitch = DatabaseMode.None;
                    _currentMode = DatabaseMode.Local;
                    _settings.SetDatabaseMode(DatabaseMode.Local);
                    UpdateDatabaseModeCheckmarks();
                    UpdateStatusBar();
                    LoadAvailableDatabases();
                    return;
                }
                // If connected to External and AutoDisconnect is OFF → set pending
                if (!_settings.AutoDisconnectOnSwitch)
                {
                    _pendingModeSwitch = DatabaseMode.Local;
                    _currentMode = DatabaseMode.Local;
                    _settings.SetDatabaseMode(DatabaseMode.Local);
                    UpdateDatabaseModeCheckmarks();
                    UpdateStatusBar();
                    LoadAvailableDatabases();
                    return;
                }
            }

            // Full switch logic
            _currentMode = DatabaseMode.Local;
            RefreshLocalDbControls();
            _settings.SetDatabaseMode(DatabaseMode.Local);
            UpdateDatabaseModeCheckmarks();

            if (_settings.AutoConnectOnSwitch)
            {
                try
                {
                    EnsureNotesTableExistsLocal();

                    if (_isConnected && _settings.AutoDisconnectOnSwitch)
                    {
                        Disconnect_Click(this, new RoutedEventArgs());
                    }

                    _noteRepository = new SqliteNoteRepository(_databasePath);
                    FinishConnection(); // ✅ Gate logic + lock/placeholder handled here
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to connect to local database:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                    _isConnected = false;
                    _connectedMode = DatabaseMode.None;
                    LoadAvailableDatabases();
                    UpdateButtonStates();
                    UpdateStatusBar();
                }
            }
            else
            {
                // AutoConnect OFF: just switch mode, stay disconnected
                _isConnected = false;
                _connectedMode = DatabaseMode.None;
                NotesDataGrid.ItemsSource = null;
                NotesDataGrid.Visibility = Visibility.Collapsed;
                DisconnectedPlaceholder.Visibility = Visibility.Visible;
                DisconnectedPlaceholder.Text = "Not connected to a database. Choose 'Local' or 'External Database' from Options.";
                LoadAvailableDatabases();
                UpdateButtonStates();
                UpdateStatusBar();
            }
        }

        // Same as above BUT Public method to switch to AND connect a local database (from another Window such as Settings)
        public void SwitchToLocalDatabase(string databasePath)
        {
            _currentMode = DatabaseMode.Local;
            _databasePath = databasePath;
            _settings.SetDatabaseMode(DatabaseMode.Local);
            _settings.LastLocalDatabasePath = databasePath;

            try
            {
                EnsureNotesTableExistsLocal();

                if (_isConnected && _settings.AutoDisconnectOnSwitch)
                {
                    Disconnect_Click(this, new RoutedEventArgs());
                }

                _noteRepository = new SqliteNoteRepository(_databasePath);
                FinishConnection(); // ✅ This handles lock detection, placeholder, and UI
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect to local database:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                _isConnected = false;
                _connectedMode = DatabaseMode.None;
                LoadAvailableDatabases();
                UpdateButtonStates();
                UpdateStatusBar();
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
                    _settings.SetDatabaseMode(DatabaseMode.External);
                    UpdateDatabaseModeCheckmarks();
                    UpdateStatusBar();
                    RefreshLocalDbControls();
                    return;
                }
                // If connected to Local and AutoDisconnect is OFF → set pending
                if (!_settings.AutoDisconnectOnSwitch)
                {
                    _pendingModeSwitch = DatabaseMode.External;
                    _currentMode = DatabaseMode.External;
                    _settings.SetDatabaseMode(DatabaseMode.External);
                    UpdateDatabaseModeCheckmarks();
                    UpdateStatusBar();
                    RefreshLocalDbControls();
                    return;
                }
            }

            // Full switch logic
            _currentMode = DatabaseMode.External;
            _settings.SetDatabaseMode(DatabaseMode.External);
            UpdateDatabaseModeCheckmarks();
            RefreshLocalDbControls();

            if (_settings.AutoConnectOnSwitch)
            {
                var connString = BuildExternalConnectionString();
                if (connString == null)
                {
                    MessageBox.Show(
                        "External database configuration is incomplete.\nPlease go to Settings → External Database to configure your connection.",
                        "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_isConnected && _settings.AutoDisconnectOnSwitch)
                {
                    Disconnect_Click(this, new RoutedEventArgs());
                }

                ConnectToExternalDatabase(connString); // → which calls FinishConnection()
            }
            else
            {
                // AutoConnect OFF: just switch mode, stay disconnected
                _isConnected = false;
                _connectedMode = DatabaseMode.None;
                NotesDataGrid.ItemsSource = null;
                NotesDataGrid.Visibility = Visibility.Collapsed;
                DisconnectedPlaceholder.Visibility = Visibility.Visible;
                DisconnectedPlaceholder.Text = "Not connected to a database. Choose 'Local' or 'External Database' from Options.";
                UpdateButtonStates();
                UpdateStatusBar();
                RefreshLocalDbControls();
            }
        }

        private void ConnectToExternalDatabase(string connectionString)
        {
            try
            {
                TestExternalConnection(connectionString);
                _externalConnectionString = connectionString;
                _noteRepository = new MySqlNoteRepository(connectionString);
                FinishConnection(); // ✅ All gate and UI logic here
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show($"Schema Error:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed:\n{ex.Message}\nInner: {ex.InnerException?.Message}",
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
            bool hasGate = DoesGateTableExist();
            string hashedPassword = "";
            bool isLocked = false;

            if (hasGate)
            {
                (hashedPassword, isLocked) = ReadGateState();
                _hasGatePassword = !string.IsNullOrEmpty(hashedPassword);
                _isGateLocked = isLocked && _hasGatePassword;
            }
            else
            {
                _hasGatePassword = false;
                _isGateLocked = false;
            }

            // 🔑 CRITICAL: Only show placeholder if actually locked
            if (_isGateLocked)
            {
                _isConnected = true;
                _connectedMode = _currentMode;
                _noteRepository = null; // Do not load notes
                ShowGatePlaceholder("🔒 This database is protected. Click the lock icon in the toolbar to unlock.");
                UpdateGateUI();
                UpdateStatusBar();
                UpdateToolbarIconVisibility();
                return;
            }

            // ✅ Normal flow: create repo and load notes
            if (_currentMode == DatabaseMode.Local)
                _noteRepository = new SqliteNoteRepository(_databasePath);
            else if (_currentMode == DatabaseMode.External)
                _noteRepository = new MySqlNoteRepository(_externalConnectionString);

            _isConnected = true;
            _connectedMode = _currentMode;
            _pendingModeSwitch = DatabaseMode.None;
            LoadNotesFromDatabase();
            NotesDataGrid.Visibility = Visibility.Visible;
            DisconnectedPlaceholder.Visibility = Visibility.Collapsed;
            UpdateGateUI();
            UpdateStatusBar();
            UpdateButtonStates();
            UpdateToolbarIconVisibility();

            if (_settings.ShowNotifications)
                MessageBox.Show("Connected successfully!", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        public void ConnectToExternalProfileTemporary(ExternalDbProfile profile)
        {
            if (profile == null) return;

            // Force disconnect first
            if (_isConnected)
            {
                TriggerDisconnect();
            }

            // Temporarily override *in-memory* settings (DO NOT save to disk!)
            _settings.External_Host = profile.Host;
            _settings.External_Port = profile.Port.ToString();
            _settings.External_Database = profile.Database;
            _settings.External_Username = profile.Username;
            _settings.External_Password = profile.Password;

            // Switch mode to External
            _currentMode = DatabaseMode.External;
            UpdateDatabaseModeCheckmarks();
            UpdateStatusBar();

            // Use your EXISTING connection logic
            TriggerConnect();
        }
        #endregion

        #region Note Operations (CRUD)
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
            //editor.Owner = this;
            editor.Show();
        }

        private void OnSaveNoteRequested(string title, string content, string tags, long? noteId)
        {
            if (_noteRepository == null)
            {
                MessageBox.Show("Database not connected. Please connect first.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (noteId.HasValue)
                {
                    _noteRepository.UpdateNote(noteId.Value, title, content, tags);
                }
                else
                {
                    _noteRepository.SaveNote(title, content, tags);
                }

                LoadNotesFromDatabase(); // Refresh list
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save note:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected || _noteRepository == null) return;

            List<FullNote> notesToDelete = new List<FullNote>();

            if (_isSelectMode)
            {
                // Get IDs of selected NoteItems (UI layer)
                var items = NotesDataGrid.ItemsSource as List<NoteItem>;
                var selectedIds = items?.Where(n => n.IsSelected).Select(n => n.Id).ToList() ?? new List<long>();

                if (!selectedIds.Any())
                {
                    MessageBox.Show("Please select at least one note to delete.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Map to FullNote objects (data layer) by ID
                notesToDelete = _fullNotesCache.Where(n => selectedIds.Contains(n.Id)).ToList();
            }
            else
            {
                // Delete single selected note
                if (!(NotesDataGrid.SelectedItem is NoteItem selectedNote))
                {
                    MessageBox.Show("Please select a note to delete.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var fullNote = _fullNotesCache.Find(n => n.Id == selectedNote.Id);
                if (fullNote != null)
                    notesToDelete = new List<FullNote> { fullNote };
            }

            // Confirmation message
            string message;
            if (notesToDelete.Count == 1)
            {
                message = $"Are you sure you want to delete the note titled:\n\n\"{notesToDelete[0].Title}\"?";
            }
            else
            {
                message = $"Are you sure you want to delete {notesToDelete.Count} selected notes?";
            }

            var result = MessageBox.Show(message, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                // Use the repository to delete — no more if/else for Local/External!
                foreach (var note in notesToDelete)
                {
                    _noteRepository.DeleteNote(note.Id);
                }

                LoadNotesFromDatabase();
                if (_settings.NotifyDeleted)
                {
                    MessageBox.Show($"{notesToDelete.Count} note{(notesToDelete.Count == 1 ? "" : "s")} deleted successfully.",
                                    "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete note(s):\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
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
            //editor.Owner = this;
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
                //editor.Owner = this;
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

        #endregion

        #region Copy/Paste
        private void CopyNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) return;

            List<FullNote> notesToCopy = new List<FullNote>();

            if (_isSelectMode)
            {
                var items = NotesDataGrid.ItemsSource as List<NoteItem>;
                var selectedIds = items?.Where(n => n.IsSelected).Select(n => n.Id).ToList() ?? new List<long>();
                notesToCopy = _fullNotesCache.Where(n => selectedIds.Contains(n.Id)).ToList();
            }
            else
            {
                if (NotesDataGrid.SelectedItem is NoteItem selectedNote)
                {
                    var fullNote = _fullNotesCache.Find(n => n.Id == selectedNote.Id);
                    if (fullNote != null)
                        notesToCopy = new List<FullNote> { fullNote };
                }
            }

            if (notesToCopy.Any())
            {
                _copiedNotes = new List<FullNote>(notesToCopy); // Deep copy not needed for this use case
                UpdateButtonStates(); // Enable Paste button
                if (_settings.NotifyCopied)
                {
                    MessageBox.Show($"{notesToCopy.Count} note{(notesToCopy.Count == 1 ? "" : "s")} copied to clipboard.",
                                    "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Please select a note to copy.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void PasteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected || _noteRepository == null || !_copiedNotes.Any())
                return;

            try
            {
                foreach (var note in _copiedNotes)
                {
                    // Add " (Copy)" to title to avoid confusion
                    string newTitle = $"{note.Title} (Copy)";
                    _noteRepository.SaveNote(newTitle, note.Content, note.Tags);
                }
                LoadNotesFromDatabase();
                if (_settings.NotifyPasted)
                {
                    MessageBox.Show($"{_copiedNotes.Count} note{(_copiedNotes.Count == 1 ? "" : "s")} pasted successfully.",
                                    "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // ✅ Clear the clipboard after successful paste
                _copiedNotes.Clear();
                UpdateButtonStates(); // This will disable the Paste button
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to paste notes:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Search and Filter
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isConnected)
            {
                string selectedField = (SearchFieldComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Title";
                LoadNotesFromDatabase(SearchBox.Text, selectedField);
            }
        }
        private string GetFirstTwoLines(string content, int softLimit = 200)
        {
            if (string.IsNullOrEmpty(content)) return "";
            var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var result = lines.Take(2).Select(line =>
                line.Length > softLimit ? line.Substring(0, softLimit) + "…" : line
            );
            return string.Join("\n", result);
        }
        private void LoadNotesFromDatabase(string searchTerm = "", string searchField = "All")
        {
            if (_noteRepository == null)
            {
                NotesDataGrid.ItemsSource = null;
                _fullNotesCache.Clear();
                NoteCountText.Text = "";
                return;
            }

            try
            {
                var fullNotes = _noteRepository.LoadNotes(searchTerm, searchField);
                _fullNotesCache = fullNotes;
                var noteItems = fullNotes.Select(note => new NoteItem
                {
                    Id = note.Id,
                    Title = note.Title,
                    Preview = GetFirstTwoLines(note.Content),
                    Tags = note.Tags,
                    LastModified = note.LastModified,
                }).ToList();

                NotesDataGrid.ItemsSource = noteItems;
                AllItemsAreChecked = false;
                NoteCountText.Text = $"{noteItems.Count} Note{(noteItems.Count == 1 ? "" : "s")}";
                // ⚠️ Do NOT touch DisconnectedPlaceholder here!
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load notes:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                NotesDataGrid.ItemsSource = null;
                _fullNotesCache.Clear();
                NoteCountText.Text = "";
            }
        }
        #endregion

        #region Gate Entry
        private bool DoesGateTableExist()
        {
            try
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name=@name;", conn))
                        {
                            cmd.Parameters.AddWithValue("@name", GATE_TABLE_NAME);
                            return cmd.ExecuteScalar() != null;
                        }
                    }
                }
                else if (_currentMode == DatabaseMode.External)
                {
                    using (var conn = new MySqlConnection(_externalConnectionString))
                    {
                        conn.Open();
                        using (var cmd = new MySqlCommand("SHOW TABLES LIKE @name;", conn))
                        {
                            cmd.Parameters.AddWithValue("@name", GATE_TABLE_NAME);
                            return cmd.ExecuteScalar() != null;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private void EnsureGateTableExists()
        {
            if (!DoesGateTableExist())
            {
                try
                {
                    if (_currentMode == DatabaseMode.Local)
                    {
                        using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                        {
                            conn.Open();
                            using (var cmd = new SQLiteCommand(@"
                        CREATE TABLE EntryGate (
                            Sp00ns TEXT,
                            IsL0ck3d INTEGER DEFAULT 1
                        )", conn))
                            {
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                    else
                    {
                        using (var conn = new MySqlConnection(_externalConnectionString))
                        {
                            conn.Open();
                            using (var cmd = new MySqlCommand(@"
                        CREATE TABLE EntryGate (
                            Sp00ns TEXT,
                            IsL0ck3d TINYINT(1) DEFAULT 1
                        )", conn))
                            {
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create EntryGate table:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return "";
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(password);
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private (string hashedPassword, bool isLocked) ReadGateState()
        {
            if (!DoesGateTableExist())
                return ("", false);

            try
            {
                string query = "SELECT Sp00ns, IsL0ck3d FROM EntryGate LIMIT 1";

                if (_currentMode == DatabaseMode.Local)
                {
                    using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                    {
                        conn.Open();
                        using (var cmd = new SQLiteCommand(query, conn))
                        using (var reader = cmd.ExecuteReader()) // 👈 USING
                        {
                            if (reader.Read())
                            {
                                string pwd = reader["Sp00ns"]?.ToString() ?? "";
                                bool locked = Convert.ToBoolean(reader["IsL0ck3d"]);
                                return (pwd, locked);
                            }
                        }
                    }
                }
                else
                {
                    using (var conn = new MySqlConnection(_externalConnectionString))
                    {
                        conn.Open();
                        using (var cmd = new MySqlCommand(query, conn))
                        using (var reader = cmd.ExecuteReader()) // 👈 USING
                        {
                            if (reader.Read())
                            {
                                string pwd = reader["Sp00ns"]?.ToString() ?? "";
                                bool locked = Convert.ToBoolean(reader["IsL0ck3d"]);
                                return (pwd, locked);
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return ("", false);
        }

        private void SaveGateState(string hashedPassword, bool isLocked)
        {
            // ✅ ASSUME TABLE EXISTS — caller must ensure that!
            string cmdText;
            if (_currentMode == DatabaseMode.Local)
            {
                cmdText = "INSERT OR REPLACE INTO EntryGate (rowid, Sp00ns, IsL0ck3d) VALUES (1, @pwd, @locked)";
            }
            else
            {
                cmdText = @"
            INSERT INTO EntryGate (Sp00ns, IsL0ck3d) VALUES (@pwd, @locked)
            ON DUPLICATE KEY UPDATE Sp00ns = @pwd, IsL0ck3d = @locked";
            }

            if (_currentMode == DatabaseMode.Local)
            {
                using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(cmdText, conn))
                    {
                        cmd.Parameters.AddWithValue("@pwd", hashedPassword ?? "");
                        cmd.Parameters.AddWithValue("@locked", isLocked ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            else
            {
                using (var conn = new MySqlConnection(_externalConnectionString))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand(cmdText, conn))
                    {
                        cmd.Parameters.AddWithValue("@pwd", hashedPassword ?? "");
                        cmd.Parameters.AddWithValue("@locked", isLocked);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
        private void UpdateGateUI()
        {
            if (!_hasGatePassword)
            {
                GateLockButton.Content = "🔓";
                GateSettingsButton.Visibility = Visibility.Collapsed;
            }
            else if (_isGateLocked)
            {
                GateLockButton.Content = "🔒";
                GateSettingsButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                GateLockButton.Content = "🔓";
                GateSettingsButton.Visibility = Visibility.Visible;
            }

            // Disable note actions when locked
            bool canEdit = _isConnected && !_isGateLocked;
            NewNoteButton.IsEnabled = canEdit;
            EditNoteButton.IsEnabled = canEdit && GetSelectedNoteCount() == 1;
            DeleteNoteButton.IsEnabled = canEdit && GetSelectedNoteCount() >= 1;
            SearchBox.IsEnabled = canEdit && !_isSelectMode;
            CopyNoteButton.IsEnabled = canEdit && GetSelectedNoteCount() >= 1;
            PasteNoteButton.IsEnabled = canEdit && _copiedNotes.Any();
            SelectNotesButton.IsEnabled = canEdit;
            ImportFilesButton.IsEnabled = canEdit;
            ExportFilesButton.IsEnabled = canEdit && (_fullNotesCache?.Any() == true);
        }
        private void GateLockButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasGatePassword)
            {
                // === SET PASSWORD ===
                var pwdWindow = new GatePasswordWindow(isSettingPassword: true, isChanging: false);
                pwdWindow.Owner = this;
                if (pwdWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(pwdWindow.Password))
                    return;

                try
                {
                    // Create table + lock
                    if (_currentMode == DatabaseMode.Local)
                    {
                        using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                        {
                            conn.Open();
                            using (var cmd = new SQLiteCommand(@"
                        CREATE TABLE IF NOT EXISTS EntryGate (
                            Sp00ns TEXT,
                            IsL0ck3d INTEGER DEFAULT 1
                        )", conn))
                                cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var conn = new MySqlConnection(_externalConnectionString))
                        {
                            conn.Open();
                            using (var cmd = new MySqlCommand(@"
                        CREATE TABLE IF NOT EXISTS EntryGate (
                            Sp00ns TEXT,
                            IsL0ck3d TINYINT(1) DEFAULT 1
                        )", conn))
                                cmd.ExecuteNonQuery();
                        }
                    }

                    string hash = HashPassword(pwdWindow.Password);
                    SaveGateState(hash, isLocked: true);
                    _hasGatePassword = true;
                    _isGateLocked = true;
                    UpdateGateUI();
                    UpdateToolbarIconVisibility();
                    ShowGatePlaceholder("🔒 Database is now locked.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to protect database:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (_isGateLocked)
            {
                // === UNLOCK ===
                string dbName = _currentMode == DatabaseMode.Local
    ? Path.GetFileName(_databasePath)
    : $"{_settings.External_Host}/{_settings.External_Database}";

                var pwdWindow = new GatePasswordWindow(dbName, isSettingPassword: false, isChanging: false);
                pwdWindow.Owner = this;
                if (pwdWindow.ShowDialog() != true)
                    return;

                string enteredHash = HashPassword(pwdWindow.Password);
                var (savedHash, _) = ReadGateState();

                if (enteredHash == savedHash)
                {
                    // ✅ HIDE PLACEHOLDER IMMEDIATELY
                    NotesDataGrid.Visibility = Visibility.Visible;
                    DisconnectedPlaceholder.Visibility = Visibility.Collapsed;

                    SaveGateState(savedHash, isLocked: false);
                    _isGateLocked = false;
                    UpdateGateUI();

                    // Recreate repo and load notes
                    if (_currentMode == DatabaseMode.Local)
                        _noteRepository = new SqliteNoteRepository(_databasePath);
                    else
                        _noteRepository = new MySqlNoteRepository(_externalConnectionString);

                    LoadNotesFromDatabase();
                }
                else
                {
                    MessageBox.Show("Incorrect password.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                // === RE-LOCK ===
                var (savedHash, _) = ReadGateState();
                SaveGateState(savedHash, isLocked: true);
                _isGateLocked = true;
                UpdateGateUI();
                ShowGatePlaceholder("🔒 Database locked.");
            }
        }

        private void GateSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            string dbName = _currentMode == DatabaseMode.Local
                ? Path.GetFileName(_databasePath)
                : $"{_settings.External_Host}/{_settings.External_Database}";

            var pwdWindow = new GatePasswordWindow(dbName, isSettingPassword: true, isChanging: true);
            if (pwdWindow.ShowDialog() == true)
            {
                if (pwdWindow.WantsToRemovePassword)
                {
                    // ✅ Simply drop the table — no password validation needed
                    try
                    {
                        string dropCmd = "DROP TABLE EntryGate";
                        if (_currentMode == DatabaseMode.Local)
                        {
                            using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
                            {
                                conn.Open();
                                using (var cmd = new SQLiteCommand(dropCmd, conn))
                                    cmd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            using (var conn = new MySqlConnection(_externalConnectionString))
                            {
                                conn.Open();
                                using (var cmd = new MySqlCommand(dropCmd, conn))
                                    cmd.ExecuteNonQuery();
                            }
                        }
                        _hasGatePassword = false;
                        _isGateLocked = false;
                        UpdateGateUI();
                        LoadNotesFromDatabase();
                        UpdateToolbarIconVisibility();
                        MessageBox.Show("Password removed. Database is no longer protected.", "Gate Entry", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to remove password:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(pwdWindow.Password))
                {
                    // ✅ Set new password — no current password check
                    string hash = HashPassword(pwdWindow.Password);
                    SaveGateState(hash, isLocked: _isGateLocked);
                    MessageBox.Show("Password updated.", "Gate Entry", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        private void ShowGatePlaceholder(string message)
        {
            NotesDataGrid.Visibility = Visibility.Collapsed;
            DisconnectedPlaceholder.Text = message;
            DisconnectedPlaceholder.Visibility = Visibility.Visible;
        }
        private int GetSelectedNoteCount()
        {
            if (_isSelectMode)
            {
                var items = NotesDataGrid.ItemsSource as List<NoteItem>;
                return items?.Count(n => n.IsSelected) ?? 0;
            }
            return NotesDataGrid.SelectedItem != null ? 1 : 0;
        }

        #endregion

        #region DataGrid Event Handlers
        private void NotesDataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;

            // 👇 NEW: Let scrollbars work normally
            if (originalSource is ScrollBar || originalSource is ScrollViewer)
            {
                return;
            }

            // Ignore clicks inside column headers
            var parentHeader = FindVisualParent<DataGridColumnHeader>(originalSource);
            if (parentHeader != null)
            {
                return;
            }

            // Walk up to find a DataGridRow
            while (originalSource != null && !(originalSource is DataGridRow))
            {
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            if (originalSource == null)
            {
                // Click on empty space (below rows) → deselect
                NotesDataGrid.SelectedItem = null;
                NotesDataGrid.CurrentCell = new DataGridCellInfo();
                UpdateButtonStates();
                //e.Handled = true;
            }
        }

        public static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && !(child is T))
            {
                child = VisualTreeHelper.GetParent(child);
            }
            return child as T;
        }

        private void NotesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;

            // 👇 NEW: Ignore double-click if it's on ANY column header
            if (FindVisualParent<DataGridColumnHeader>(originalSource) != null)
            {
                return; // Do nothing when double-clicking headers
            }

            // Proceed with edit only if a note is selected
            EditNoteButton_Click(sender, e);
        }
        private void NotesDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var columnHeader = e.Column.Header?.ToString();
            if (columnHeader == "Title" || columnHeader == "Tags")
            {
                _editingItem = e.Row.Item as NoteItem;
                _originalTitle = _editingItem?.Title;
            }
            else
            {
                e.Cancel = true;
            }
        }
        private void NotesDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var columnHeader = e.Column.Header?.ToString();
            if (e.EditAction == DataGridEditAction.Cancel || (columnHeader != "Title" && columnHeader != "Tags"))
                return;

            if (_editingItem == null) return;

            var textBox = e.EditingElement as TextBox;
            if (textBox == null) return;

            if (columnHeader == "Title")
            {
                var newTitle = textBox.Text;
                if (newTitle == _originalTitle) return;

                // Show confirmation dialog for Title
                var result = MessageBox.Show(
                    $"Update note title from:\n\n\"{_originalTitle}\"\n\nto:\n\n\"{newTitle}\"?",
                    "Confirm Title Change",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    UpdateNoteTitle(newTitle);
                }
                else
                {
                    _editingItem.Title = _originalTitle;
                }
            }
            else if (columnHeader == "Tags")
            {
                var newTags = textBox.Text;
                UpdateNoteTags(newTags);
            }

            _editingItem = null;
            _originalTitle = null;
        }
        private void UpdateNoteTitle(string newTitle)
        {
            var fullNote = _fullNotesCache.Find(n => n.Id == _editingItem.Id);
            if (fullNote != null)
            {
                try
                {
                    if (_noteRepository == null)
                    {
                        throw new InvalidOperationException("Database repository not initialized.");
                    }
                    _noteRepository.UpdateNote(fullNote.Id, newTitle, fullNote.Content, fullNote.Tags);
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
        private void UpdateNoteTags(string newTags)
        {
            var fullNote = _fullNotesCache.Find(n => n.Id == _editingItem.Id);
            if (fullNote != null)
            {
                try
                {
                    if (_noteRepository == null)
                    {
                        throw new InvalidOperationException("Database repository not initialized.");
                    }
                    _noteRepository.UpdateNote(fullNote.Id, fullNote.Title, fullNote.Content, newTags);
                    var newModifiedStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    fullNote.Tags = newTags;
                    fullNote.LastModified = newModifiedStr;
                    _editingItem.Tags = newTags;
                    _editingItem.LastModified = newModifiedStr;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to update tags:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void OnRowCheckboxChanged(object sender, RoutedEventArgs e)
        {
            if (_isBulkUpdating) return;

            var items = NotesDataGrid.ItemsSource as List<NoteItem>;
            if (items != null)
            {
                bool allChecked = items.All(n => n.IsSelected);

                if (_allItemsAreChecked != allChecked)
                {
                    _allItemsAreChecked = allChecked;
                    OnPropertyChanged(nameof(AllItemsAreChecked));
                }

                UpdateButtonStates();
            }
        }
        #endregion

        #region UI State Management
        private void UpdateButtonStates()
        {
            bool hasSelection = NotesDataGrid?.SelectedItem != null;
            int selectedCount = 0;
            if (_isSelectMode)
            {
                var items = NotesDataGrid.ItemsSource as List<NoteItem>;
                selectedCount = items?.Count(n => n.IsSelected) ?? 0;
            }
            else
            {
                selectedCount = hasSelection ? 1 : 0;
            }

            // Connection-dependent buttons
            bool isConnected = _isConnected;
            // 🔒 Gate Entry: treat locked state as "not editable"
            bool isEditable = _isConnected && !_isGateLocked;
            NewNoteButton.IsEnabled = isConnected;
            EditNoteButton.IsEnabled = isConnected && selectedCount == 1;
            DeleteNoteButton.IsEnabled = isConnected && selectedCount >= 1;
            SearchBox.IsEnabled = isConnected && !_isSelectMode;
            ConnectIconBtn.IsEnabled = !isConnected;
            DisconnectIconBtn.IsEnabled = isConnected;
            SelectNotesButton.IsEnabled = isConnected;
            ImportFilesButton.IsEnabled = isConnected;
            ExportFilesButton.IsEnabled = isConnected && (_fullNotesCache?.Any() == true);
            CopyNoteButton.IsEnabled = isConnected && selectedCount >= 1;
            PasteNoteButton.IsEnabled = isConnected && _copiedNotes.Any();

            // File menu
            // File menu — direct access via x:Name
            NewNoteMenuItem.IsEnabled = isConnected;
            EditNoteMenuItem.IsEnabled = isConnected && selectedCount == 1;
            DeleteNoteMenuItem.IsEnabled = isConnected && selectedCount >= 1;
            ImportFilesMenuItem.IsEnabled = isConnected;
            ExportFilesMenuItem.IsEnabled = isConnected;

            ConnectMenuItem.IsEnabled = !isConnected;
            DisconnectMenuItem.IsEnabled = isConnected;

            // Disable local DB switcher unless in Local mode
            DatabaseOrConnectionComboBox.IsEnabled = true;
            LoadSelectedDbButton.IsEnabled = true;
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
                var host = _settings.External_Host ?? "localhost";
                var port = _settings.External_Port?.ToString() ?? "3306";
                var database = _settings.External_Database ?? "unknown";
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
        public void UpdateDatabaseModeCheckmarks()
        {
            // Main Options Menu (existing)
            var localItem = LocalDbMenuItem;
            var externalItem = ExternalDbMenuItem;
            localItem.IsChecked = (_currentMode == DatabaseMode.Local);
            externalItem.IsChecked = (_currentMode == DatabaseMode.External);
            externalItem.IsEnabled = !string.IsNullOrWhiteSpace(_settings.External_Host) &&
                                    !string.IsNullOrWhiteSpace(_settings.External_Database);
        }
        private void UpdateDataGridColumns()
        {
            if (NotesDataGrid.Columns.Count >= 4)
            {
                NotesDataGrid.Columns[1].Visibility = ViewTitleMenuItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                NotesDataGrid.Columns[3].Visibility = ViewTagsMenuItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                NotesDataGrid.Columns[4].Visibility = ViewModifiedMenuItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private void UpdateToolbarVisibility()
        {
            var toolbar = (StackPanel)FindName("ToolbarStackPanel");
            if (toolbar != null)
            {
                toolbar.Visibility = _isToolbarVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
public void UpdateToolbarIconVisibility()
{
    // Standard groups — respect user settings
    ConnectDisconnectGroup.Visibility = _settings.ViewToolbarConnect ? Visibility.Visible : Visibility.Collapsed;
    AcosGroup.Visibility = _settings.ViewToolbarACOS ? Visibility.Visible : Visibility.Collapsed;
    SettingsGroup.Visibility = _settings.ViewToolbarSettings ? Visibility.Visible : Visibility.Collapsed;
    LocalDbGroup.Visibility = _settings.ViewToolbarLocalDB ? Visibility.Visible : Visibility.Collapsed;
    ImportExportGroup.Visibility = _settings.ViewToolbarImpEx ? Visibility.Visible : Visibility.Collapsed;
    NoteToolsGroup.Visibility = _settings.ViewToolbarNoteTools ? Visibility.Visible : Visibility.Collapsed;
    CopyPasteGroup.Visibility = _settings.ViewToolbarCopyPaste ? Visibility.Visible : Visibility.Collapsed;
    SearchGroup.Visibility = _settings.ViewToolbarSearch ? Visibility.Visible : Visibility.Collapsed;
    CalculatorGroup.Visibility = _settings.ViewToolbarCalculator ? Visibility.Visible : Visibility.Collapsed;
    CatButtonGroup.Visibility = _settings.ViewToolbarCat ? Visibility.Visible : Visibility.Collapsed;

    bool shouldShowGateGroup = _hasGatePassword || _settings.ViewToolbarGateEntry;
    GateEntryGroup.Visibility = shouldShowGateGroup ? Visibility.Visible : Visibility.Collapsed;

    // GateSettingsButton: only visible when unlocked AND password exists
    GateSettingsButton.Visibility = (_hasGatePassword && !_isGateLocked) 
        ? Visibility.Visible 
        : Visibility.Collapsed;
        }

        public void UpdateFileMenuVisibility() 
        {
            // Load Toolbar Submenu Settings
            ViewToolbarConnectMenuItem.IsChecked = _settings.ViewToolbarConnect;
            ViewToolbarACOSMenuItem.IsChecked = _settings.ViewToolbarACOS;
            ViewToolbarSettingsMenuItem.IsChecked = _settings.ViewToolbarSettings;
            ViewToolbarLocalDBMenuItem.IsChecked = _settings.ViewToolbarLocalDB;
            ViewToolbarImpExMenuItem.IsChecked = _settings.ViewToolbarImpEx;
            ViewToolbarNoteToolsMenuItem.IsChecked = _settings.ViewToolbarNoteTools;
            ViewToolbarCopyPasteMenuItem.IsChecked = _settings.ViewToolbarCopyPaste;
            ViewToolbarSearchMenuItem.IsChecked = _settings.ViewToolbarSearch;
            ViewToolbarGateEntryMenuItem.IsChecked = _settings.ViewToolbarGateEntry;
        }

        #endregion

        #region View Menu Handlers
        private void ViewTitleMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = ViewTitleMenuItem.IsChecked == true;
            _settings.ViewMainWindowTitle = isChecked;
            UpdateDataGridColumns();
        }

        private void ViewTagsMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = ViewTagsMenuItem.IsChecked == true;
            _settings.ViewMainWindowTags = isChecked;
            UpdateDataGridColumns();
        }

        private void ViewModifiedMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = ViewModifiedMenuItem.IsChecked == true;
            _settings.ViewMainWindowMod = isChecked;
            UpdateDataGridColumns();
        }

        private void ViewToolbarMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked = ViewToolbarMenuItem.IsChecked == true;
            _settings.ViewMainToolbar = isChecked;
            _isToolbarVisible = isChecked;
            UpdateToolbarVisibility();
        }
        private void ViewToolbarConnectMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarConnect = ViewToolbarConnectMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        private void ViewToolbarACOSMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarACOS = ViewToolbarACOSMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        private void ViewToolbarSettingsMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarSettings = ViewToolbarSettingsMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        private void ViewToolbarLocalDBMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarLocalDB = ViewToolbarLocalDBMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        private void ViewToolbarImpExMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarImpEx = ViewToolbarImpExMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        private void ViewToolbarNoteToolsMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarNoteTools = ViewToolbarNoteToolsMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        private void ViewToolbarCopyPasteMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarCopyPaste = ViewToolbarCopyPasteMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        private void ViewToolbarSearchMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarSearch = ViewToolbarSearchMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        private void ViewToolbarCalcMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarCalculator = ViewToolbarCalcMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        private void ViewToolbarGateEntryMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarGateEntry = ViewToolbarGateEntryMenuItem.IsChecked == true;
            _settings.Save();
            UpdateToolbarIconVisibility();
        }

        #endregion

        #region Zoom Controls
        private void ZoomInMenuItem_Click(object sender, RoutedEventArgs e)
        {
            double currentPoints = NotesDataGrid.FontSize * 72.0 / 96.0;
            double newPoints = currentPoints + 1.0;
            if (newPoints <= MAX_ZOOM_POINTS)
            {
                NotesDataGrid.FontSize = newPoints * 96.0 / 72.0;
                _settings.DataGridZoom = newPoints; // Save as points
            }
        }

        private void ZoomOutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            double currentPoints = NotesDataGrid.FontSize * 72.0 / 96.0;
            double newPoints = currentPoints - 1.0;
            if (newPoints >= MIN_ZOOM_POINTS)
            {
                NotesDataGrid.FontSize = newPoints * 96.0 / 72.0;
                _settings.DataGridZoom = newPoints;
            }
        }

        private void RestoreZoomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            NotesDataGrid.FontSize = DEFAULT_ZOOM_POINTS * 96.0 / 72.0;
            _settings.DataGridZoom = DEFAULT_ZOOM_POINTS;
        }
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var modifiers = Keyboard.Modifiers;

            if (modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.OemPlus || e.Key == Key.Add)
                {
                    ZoomInMenuItem_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                {
                    ZoomOutMenuItem_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.D0)
                {
                    RestoreZoomMenuItem_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
            }
        }
        #endregion

        #region Import/Export
        // ImportToDB Window
        private void ImportFilesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to a database first.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var importWindow = new ImportToDBWindow(
                _databasePath,
                _currentMode,
                _externalConnectionString,
                _noteRepository,
                () => LoadNotesFromDatabase() // 👈 CALLBACK to refresh
            );
            importWindow.Owner = this;
            importWindow.ShowDialog();
        }

        private void ExportFilesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected || _fullNotesCache == null || !_fullNotesCache.Any())
            {
                MessageBox.Show("No notes available to export.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var exportWindow = new ExportFromDBWindow(_fullNotesCache, _databasePath, _currentMode);
            exportWindow.Owner = this;
            exportWindow.ShowDialog();
        }
        // Import Via Send To
        public void ImportFileViaSendTo(string filePath)
        {
            if (!_isConnected)
            {
                ActivateWindow();
                MessageBox.Show("Please connect to a database before importing.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var importWindow = new ImportToDBWindow(
            _databasePath,
            _currentMode,
            _externalConnectionString,
            _noteRepository,
            () => LoadNotesFromDatabase(),
            preselectedFilePath: filePath
        );
            importWindow.Show(); // or ShowDialog()
        }

        private void HandleImportFileRequestBatch(List<string> filePaths)
        {
            if (!_isConnected)
            {
                ActivateWindow();
                MessageBox.Show(this,
                    "Please connect to a database first.\nImport requires an active database connection.",
                    "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Filter out non-existent files
            var validPaths = filePaths.Where(File.Exists).ToList();
            var invalidPaths = filePaths.Except(validPaths).ToList();

            if (invalidPaths.Any())
            {
                string missing = string.Join("\n", invalidPaths.Take(5)); // show max 5
                if (invalidPaths.Count > 5)
                    missing += $"\n... and {invalidPaths.Count - 5} more";

                ActivateWindow();
                MessageBox.Show(this,
                    $"The following files were not found and will be skipped:\n\n{missing}",
                    "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (!validPaths.Any())
            {
                ActivateWindow();
                MessageBox.Show(this, "No valid files to import.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var importWindow = new ImportToDBWindow(
                    databasePath: _databasePath,
                    databaseMode: _currentMode,
                    externalConnectionString: _externalConnectionString,
                    noteRepository: _noteRepository,
                    onImportCompleted: () => { },
                    preselectedFilePath: null // We'll add all files manually
                );

                // Add all valid files
                foreach (var path in validPaths)
                {
                    importWindow.AddFileToImportList(path);
                }

                importWindow.Show();
                ActivateWindow();
            }
            catch (Exception ex)
            {
                ActivateWindow();
                MessageBox.Show(this,
                    $"Failed to open Import window:\n{ex.Message}",
                    "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Database File Management

        // File Menu NewDatabase
        private void NewDatabaseMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Protes Database (*.prote)|*.prote|SQLite Database (*.db)|*.db",
                FileName = $"notes_{DateTime.Now:yyyyMMdd_HHmm}.prote",
                InitialDirectory = _settings.DefaultDatabaseFolder
            };

            if (saveDialog.ShowDialog() == true)
            {
                string filePath = saveDialog.FileName;

                try
                {
                    // 🔧 Create the new empty database file with Notes table
                    using (var conn = new SQLiteConnection($"Data Source={filePath};Version=3;"))
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

                    // ✅ Switch to it immediately (reuses your existing logic)
                    SwitchToLocalDatabase(filePath);

                    // ✅ Ensure the AvailableDatabasesComboBox and related UI are refreshed
                    LoadAvailableDatabases();

                    MessageBox.Show("New database created successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create database:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void LoadAvailableDatabases()
        {
            var dbFiles = new List<DbFileInfo>();

            string defaultFolder = _settings.DefaultDatabaseFolder;
            if (string.IsNullOrWhiteSpace(defaultFolder) || !Directory.Exists(defaultFolder))
            {
                defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes");
            }

            if (Directory.Exists(defaultFolder))
            {
                // Load both .db and .prote files from the default folder
                var dbFilesInFolder = Directory.GetFiles(defaultFolder, "*.db")
                                              .Concat(Directory.GetFiles(defaultFolder, "*.prote"));
                foreach (var file in dbFilesInFolder)
                {
                    dbFiles.Add(new DbFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        IsImported = false
                    });
                }
            }

            // Imported paths (already support any extension, including .prote)
            var importedRaw = _settings.ImportedDatabasePaths;
            if (!string.IsNullOrWhiteSpace(importedRaw))
            {
                var importedPaths = importedRaw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Where(p => File.Exists(p))
                                               .ToList();
                foreach (var path in importedPaths)
                {
                    dbFiles.Add(new DbFileInfo
                    {
                        FileName = Path.GetFileName(path) + " (imported)",
                        FullPath = path,
                        IsImported = true
                    });
                }
            }

            // Remove duplicates by path
            var uniqueFiles = dbFiles.GroupBy(f => f.FullPath).Select(g => g.First()).ToList();

            // 👇 Use the SHARED ComboBox (renamed from AvailableDatabasesComboBox)
            DatabaseOrConnectionComboBox.ItemsSource = uniqueFiles;
            DatabaseOrConnectionComboBox.DisplayMemberPath = "FileName";
            DatabaseOrConnectionComboBox.SelectedValuePath = "FullPath";

            // Select LastLocalDatabasePath if available
            string lastPath = _settings.LastLocalDatabasePath;
            if (!string.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
            {
                foreach (DbFileInfo item in uniqueFiles)
                {
                    if (item.FullPath.Equals(lastPath, StringComparison.OrdinalIgnoreCase))
                    {
                        DatabaseOrConnectionComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void LoadExternalConnectionsIntoComboBox()
        {
            var profiles = _settings.GetExternalDbProfiles();
            DatabaseOrConnectionComboBox.ItemsSource = profiles;
            DatabaseOrConnectionComboBox.DisplayMemberPath = "DisplayName";

            // Select current default (match by Host+Port+Database)
            string currentHost = _settings.External_Host ?? "";
            string currentPort = _settings.External_Port ?? "3306";
            string currentDb = _settings.External_Database ?? "";

            foreach (var profile in profiles)
            {
                if (profile.Host == currentHost &&
                    profile.Port.ToString() == currentPort &&
                    profile.Database == currentDb)
                {
                    DatabaseOrConnectionComboBox.SelectedItem = profile;
                    break;
                }
            }
        }
        private void RefreshLocalDbControls()
        {
            if (_currentMode == DatabaseMode.Local)
            {
                DatabaseModeLabel.Text = "Local:";
                LoadAvailableDatabases(); // Reuse your existing method
            }
            else if (_currentMode == DatabaseMode.External)
            {
                DatabaseModeLabel.Text = "External:";
                LoadExternalConnectionsIntoComboBox();
            }
            else
            {
                DatabaseModeLabel.Text = "—";
                DatabaseOrConnectionComboBox.ItemsSource = null;
            }
        }

        private void LoadSelectedDbButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == DatabaseMode.Local)
            {
                var selectedItem = DatabaseOrConnectionComboBox.SelectedItem as DbFileInfo;
                if (selectedItem != null)
                {
                    _settings.LastLocalDatabasePath = selectedItem.FullPath;
                    SwitchToLocalDatabase(selectedItem.FullPath);
                }
            }
            else if (_currentMode == DatabaseMode.External)
            {
                var selectedProfile = DatabaseOrConnectionComboBox.SelectedItem as ExternalDbProfile;
                if (selectedProfile != null)
                {
                    // Set as default
                    _settings.External_Host = selectedProfile.Host;
                    _settings.External_Port = selectedProfile.Port.ToString();
                    _settings.External_Database = selectedProfile.Database;
                    _settings.External_Username = selectedProfile.Username;
                    _settings.External_Password = selectedProfile.Password;
                    _settings.Save();

                    // Always attempt to connect — even if already connected
                    var connStr = BuildExternalConnectionString();
                    if (!string.IsNullOrEmpty(connStr))
                    {
                        // If already connected, disconnect first
                        if (_isConnected)
                        {
                            // auto-disconnect first
                            if (_settings.AutoDisconnectOnSwitch)
                            {
                                Disconnect_Click(this, new RoutedEventArgs());
                            }
                            // Or just reconnect — MySQL connector handles it
                        }
                        ConnectToExternalDatabase(connStr);
                        UpdateStatusBar();
                    }
                    else
                    {
                        MessageBox.Show("Incomplete connection details.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }
        private void PromptAndSwitchDatabase(string databasePath)
        {
            // If not connected, just switch directly
            if (!_isConnected)
            {
                SwitchToLocalDatabase(databasePath);
                return;
            }

            // Check if already connected to this exact database
            if (_connectedMode == DatabaseMode.Local &&
                _databasePath.Equals(databasePath, StringComparison.OrdinalIgnoreCase))
            {
                // Already connected to this database, just activate window
                ActivateWindow();
                return;
            }

            // Check if this database is in the available/imported list
            bool isInList = IsDatabaseInAvailableList(databasePath);

            string message;
            if (isInList)
            {
                message = $"Another database is currently open.\n\n" +
                          $"Do you want to switch to:\n{Path.GetFileName(databasePath)}?";
            }
            else
            {
                message = $"Another database is currently open.\n\n" +
                          $"The database you're trying to open is not in your imported list:\n" +
                          $"{Path.GetFileName(databasePath)}\n\n" +
                          $"Do you want to import and switch to this database?";
            }

            var result = MessageBox.Show(
                message,
                "Switch Database",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Yes)
            {
                // If not in list, add it to imported databases
                if (!isInList)
                {
                    AddDatabaseToImportedList(databasePath);
                }

                SwitchToLocalDatabase(databasePath);
            }
        }
        private bool IsDatabaseInAvailableList(string databasePath)
        {
            // Check default folder
            string defaultFolder = _settings.DefaultDatabaseFolder;
            if (string.IsNullOrWhiteSpace(defaultFolder) || !Directory.Exists(defaultFolder))
            {
                defaultFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Protes");
            }

            // Check if file is in default folder
            if (File.Exists(databasePath))
            {
                string fileDir = Path.GetDirectoryName(databasePath);
                if (fileDir.Equals(defaultFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Check imported databases list
            var importedRaw = _settings.ImportedDatabasePaths;
            if (!string.IsNullOrWhiteSpace(importedRaw))
            {
                var importedPaths = importedRaw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(p => p.Trim())
                                               .ToList();

                if (importedPaths.Any(p => p.Equals(databasePath, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddDatabaseToImportedList(string databasePath)
        {
            var importedRaw = _settings.ImportedDatabasePaths ?? "";
            var importedPaths = importedRaw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(p => p.Trim())
                                           .ToList();

            // Add if not already in list
            if (!importedPaths.Any(p => p.Equals(databasePath, StringComparison.OrdinalIgnoreCase)))
            {
                importedPaths.Add(databasePath);
                _settings.ImportedDatabasePaths = string.Join(";", importedPaths);

                // Refresh the dropdown if in Local mode
                if (_currentMode == DatabaseMode.Local)
                {
                    LoadAvailableDatabases();
                }
            }
        }

        #endregion

        #region IPC and External File Handling
        public void HandleIpcMessage(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[IPC] Raw: {message}");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Handle -new
                if (message == "-new")
                {
                    HandleNewNoteRequest();
                    return;
                }

                // Split into tokens, respecting quotes
                var tokens = SplitCommandLine(message);
                if (tokens.Count == 0)
                {
                    ActivateWindow();
                    return;
                }

                string command = tokens[0];
                var args = tokens.Skip(1).ToList();

                if (command == "-noteeditor")
                {
                    if (args.Count == 0)
                    {
    
                        MessageBox.Show(this, "No file specified for Note Editor.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    // Only allow ONE file for Note Editor
                    if (args.Count > 1)
                    {
                        ActivateWindow();
                        MessageBox.Show(this,
                            "The Note Editor only supports opening one file at a time.\n\n" +
                            $"You selected {args.Count} files. Only the first will be opened.",
                            "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    HandleNoteEditorWithFile(args[0]);
                }
                else if (command == "-import")
                {
                    if (args.Count == 0)
                    {
                        ActivateWindow();
                        MessageBox.Show(this, "No files specified for import.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Validate all paths
                    var validPaths = new List<string>();
                    var invalidPaths = new List<string>();
                    foreach (var path in args)
                    {
                        if (File.Exists(path))
                            validPaths.Add(path);
                        else
                            invalidPaths.Add(path);
                    }

                    if (invalidPaths.Any())
                    {
                        string msg = $"The following files were not found and will be skipped:\n\n{string.Join("\n", invalidPaths.Take(5))}";
                        if (invalidPaths.Count > 5) msg += $"\n... and {invalidPaths.Count - 5} more";
                        ActivateWindow();
                        MessageBox.Show(this, msg, "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    if (validPaths.Count == 0)
                    {
                        ActivateWindow();
                        MessageBox.Show(this, "No valid files to import.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // ✅ Open ONE Import window with ALL valid files
                    try
                    {
                        var importWindow = new ImportToDBWindow(
                            databasePath: _databasePath,
                            databaseMode: _currentMode,
                            externalConnectionString: _externalConnectionString,
                            noteRepository: _noteRepository,
                            onImportCompleted: () => { },
                            preselectedFilePath: null
                        );

                        foreach (var path in validPaths)
                        {
                            importWindow.AddFileToImportList(path);
                        }

                        importWindow.Show();
                        ActivateWindow();
                    }
                    catch (Exception ex)
                    {
                        ActivateWindow();
                        MessageBox.Show(this, $"Failed to open Import window:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else if (IsDatabaseFile(command))
                {
                    PromptAndSwitchDatabase(command);
                }
                else if (File.Exists(command))
                {
                    HandleNoteEditorWithFile(command);
                }
                else
                {
                    ActivateWindow();
                }
            }));
        }
        private static List<string> SplitCommandLine(string commandLine)
        {
            var parts = new List<string>();
            if (string.IsNullOrWhiteSpace(commandLine)) return parts;

            bool inQuotes = false;
            StringBuilder current = new StringBuilder();

            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
            {
                parts.Add(current.ToString());
            }

            return parts;
        }
 
        private void OpenBatchImportWindow(List<string> filePaths)
        {
            try
            {
                var importWindow = new ImportToDBWindow(
                    databasePath: _databasePath,
                    databaseMode: _currentMode,
                    externalConnectionString: _externalConnectionString,
                    noteRepository: _noteRepository,
                    onImportCompleted: () => { /* optional: refresh notes */ },
                    preselectedFilePath: null // We’ll pass all files via a new constructor
                );

                // 👇 NEW: Add a method in ImportToDBWindow to add multiple files
                foreach (var path in filePaths)
                {
                    importWindow.AddFileToImportList(path);
                }

                importWindow.Show();
                ActivateWindow();
            }
            catch (Exception ex)
            {
                ActivateWindow();
                MessageBox.Show(this,
                    $"Failed to open batch import window:\n{ex.Message}",
                    "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleNoteEditorWithFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                ActivateWindow();
                MessageBox.Show(this,
                    $"File not found:\n{filePath}",
                    "Protes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!_isConnected)
            {
                ActivateWindow();
                MessageBox.Show(this,
                    "Please connect to a database first.\n\nThe Note Editor can only save notes to a database.",
                    "Protes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                string title = Path.GetFileNameWithoutExtension(filePath);
                string content = File.ReadAllText(filePath);
                string tags = "";

                var editor = new NoteEditorWindow(
                    title: title,
                    content: content,
                    tags: tags,
                    noteId: null,
                    onSaveRequested: OnSaveNoteRequested
                );
                editor.Show();
                ActivateWindow();
            }
            catch (Exception ex)
            {
                ActivateWindow();
                MessageBox.Show(this,
                    $"Could not open file:\n{ex.Message}",
                    "Protes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void HandleNewNoteRequest()
        {
            if (!_isConnected)
            {
                ActivateWindow();
                MessageBox.Show(this,
                    "Please connect to a database first...",
                    "Protes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var editor = new NoteEditorWindow(
                noteId: null,
                onSaveRequested: OnSaveNoteRequested);
            editor.Show();
        }

        private bool IsDatabaseFile(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".db" || ext == ".prote";
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null)
            {
                source.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x004A) // WM_COPYDATA
            {
                try
                {
                    var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                    if (cds.cbData > 0 && cds.lpData != IntPtr.Zero)
                    {
                        byte[] buffer = new byte[cds.cbData];
                        Marshal.Copy(cds.lpData, buffer, 0, cds.cbData);
                        string message = System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            HandleIpcMessage(message);
                        }));
                    }
                    handled = true;
                }
                catch
                {
                    // Ignore malformed messages
                }
            }
            return IntPtr.Zero;
        }
        #endregion

        #region Right Click Context Menu
        // Right click menu stuff
        private void NotesDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {

            var contextMenu = MainContentGrid.ContextMenu;
            if (contextMenu == null) return;

            var connectItem = FindContextMenuItem(contextMenu, "ContextConnectMenuItem");
            var disconnectItem = FindContextMenuItem(contextMenu, "ContextDisconnectMenuItem");
            var newItem = FindContextMenuItem(contextMenu, "ContextNewNoteMenuItem");
            var editItem = FindContextMenuItem(contextMenu, "ContextEditNoteMenuItem");
            var copyItem = FindContextMenuItem(contextMenu, "ContextCopyNoteMenuItem");
            var pasteItem = FindContextMenuItem(contextMenu, "ContextPasteNoteMenuItem");
            var selectItem = FindContextMenuItem(contextMenu, "ContextSelectNotesMenuItem");
            var deleteItem = FindContextMenuItem(contextMenu, "ContextDeleteNoteMenuItem");
            var importItem = FindContextMenuItem(contextMenu, "ContextImportFilesMenuItem");
            var exportItem = FindContextMenuItem(contextMenu, "ContextExportFilesMenuItem");
            var switchModeItem = FindContextMenuItem(contextMenu, "ContextSwitchModeMenuItem");
            var switchLocalItem = FindContextMenuItem(contextMenu, "ContextSwitchLocalMenuItem");
            var optionsItem = FindContextMenuItem(contextMenu, "ContextOptionsMenuItem");
            var localDbItem = FindContextMenuItem(contextMenu, "ContextLocalDbMenuItem");
            var externalDbItem = FindContextMenuItem(contextMenu, "ContextExternalDbMenuItem");

            void SetMenuState(MenuItem item, bool enabled)
            {
                if (item != null)
                {
                    item.Visibility = Visibility.Visible;
                    item.IsEnabled = enabled;
                }
            }

            if (!_isConnected)
            {
                // === DISCONNECTED ===
                SetMenuState(newItem, false);
                SetMenuState(editItem, false);
                SetMenuState(copyItem, false);
                SetMenuState(pasteItem, false);
                SetMenuState(selectItem, false);
                SetMenuState(deleteItem, false);
                SetMenuState(importItem, false);
                SetMenuState(exportItem, false);

                // ✅ CRITICAL: Explicitly control Connect/Disconnect visibility
                if (connectItem != null)
                {
                    connectItem.Visibility = Visibility.Visible;
                    connectItem.IsEnabled = true;
                }
                if (disconnectItem != null)
                {
                    disconnectItem.Visibility = Visibility.Collapsed;
                }

                SetMenuState(switchModeItem, true);
                SetMenuState(optionsItem, true);

                if (switchLocalItem != null)
                {
                    bool show = (_currentMode == DatabaseMode.Local);
                    switchLocalItem.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    if (show)
                    {
                        PopulateSwitchLocalContextMenu(switchLocalItem);
                    }
                }
                
                var switchExternalItem = FindContextMenuItem(contextMenu, "ContextSwitchExternalMenuItem");
                if (switchExternalItem != null)
                {
                    bool show = (_currentMode == DatabaseMode.External);
                    switchExternalItem.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    if (show)
                    {
                        PopulateSwitchExternalContextMenu(switchExternalItem);
                    }
                }

                if (localDbItem != null) localDbItem.IsChecked = (_currentMode == DatabaseMode.Local);
                if (externalDbItem != null)
                {
                    externalDbItem.IsChecked = (_currentMode == DatabaseMode.External);
                    externalDbItem.IsEnabled = !string.IsNullOrWhiteSpace(_settings.External_Host) &&
                                               !string.IsNullOrWhiteSpace(_settings.External_Database);
                }
            }
            else
            {
                // === CONNECTED ===
                bool hasSelection = NotesDataGrid?.SelectedItem != null;
                int selectedCount = 0;
                if (_isSelectMode)
                {
                    var items = NotesDataGrid.ItemsSource as List<NoteItem>;
                    selectedCount = items?.Count(n => n.IsSelected) ?? 0;
                }
                else
                {
                    selectedCount = hasSelection ? 1 : 0;
                }

                SetMenuState(newItem, true);
                SetMenuState(editItem, selectedCount == 1);
                SetMenuState(copyItem, selectedCount >= 1);
                SetMenuState(pasteItem, _copiedNotes.Any());
                SetMenuState(selectItem, true);
                SetMenuState(deleteItem, selectedCount >= 1);
                SetMenuState(importItem, true);
                SetMenuState(exportItem, _fullNotesCache?.Any() == true);

                // ✅ CRITICAL: Explicitly control Connect/Disconnect visibility
                if (connectItem != null)
                {
                    connectItem.Visibility = Visibility.Collapsed;
                }
                if (disconnectItem != null)
                {
                    disconnectItem.Visibility = Visibility.Visible;
                    disconnectItem.IsEnabled = true;
                }

                SetMenuState(switchModeItem, true);
                SetMenuState(optionsItem, true);

                if (switchLocalItem != null)
                {
                    bool show = (_currentMode == DatabaseMode.Local);
                    switchLocalItem.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    if (show)
                    {
                        PopulateSwitchLocalContextMenu(switchLocalItem);
                    }
                }

                if (localDbItem != null) localDbItem.IsChecked = (_currentMode == DatabaseMode.Local);
                if (externalDbItem != null)
                {
                    externalDbItem.IsChecked = (_currentMode == DatabaseMode.External);
                    externalDbItem.IsEnabled = !string.IsNullOrWhiteSpace(_settings.External_Host) &&
                                               !string.IsNullOrWhiteSpace(_settings.External_Database);
                }
            }
        }

        private MenuItem FindContextMenuItem(ContextMenu menu, string name)
        {
            foreach (var item in menu.Items)
            {
                if (FindMenuItemByName(item, name) is MenuItem found)
                    return found;
            }
            return null;
        }

        private object FindMenuItemByName(object item, string name)
        {
            if (item is MenuItem menuItem)
            {
                if (menuItem.Name == name)
                    return menuItem;

                // Recursively search submenu items
                foreach (var child in menuItem.Items)
                {
                    var found = FindMenuItemByName(child, name);
                    if (found != null)
                        return found;
                }
            }
            return null;
        }
        //Populate right click sub menu with LocalDatabase List
        private void PopulateSwitchLocalContextMenu(MenuItem switchLocalMenuItem)
        {
            // Clear existing items
            switchLocalMenuItem.Items.Clear();

            // Get available databases (same logic as LoadAvailableDatabases)
            var dbFiles = new List<DbFileInfo>();
            string defaultFolder = _settings.DefaultDatabaseFolder;
            if (string.IsNullOrWhiteSpace(defaultFolder) || !Directory.Exists(defaultFolder))
            {
                defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes");
            }

            if (Directory.Exists(defaultFolder))
            {
                foreach (var file in Directory.GetFiles(defaultFolder, "*.db").Concat(Directory.GetFiles(defaultFolder, "*.prote")))
                {
                    dbFiles.Add(new DbFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        IsImported = false
                    });
                }
            }

            var importedRaw = _settings.ImportedDatabasePaths;
            if (!string.IsNullOrWhiteSpace(importedRaw))
            {
                var importedPaths = importedRaw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Where(p => File.Exists(p)).ToList();
                foreach (var path in importedPaths)
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

            // Add each database as a checkable menu item
            foreach (var db in uniqueFiles)
            {
                var menuItem = new MenuItem
                {
                    Header = db.FileName,
                    IsCheckable = true,
                    IsChecked = db.FullPath.Equals(_databasePath, StringComparison.OrdinalIgnoreCase),
                    Tag = db.FullPath // Store path for click handler
                };
                menuItem.Click += SwitchLocalDbContextMenuItem_Click;
                switchLocalMenuItem.Items.Add(menuItem);
            }
        }

        private void PopulateSwitchExternalContextMenu(MenuItem switchExternalMenuItem)
        {
            // Clear existing items
            switchExternalMenuItem.Items.Clear();

            // Get saved external connections
            var profiles = _settings.GetExternalDbProfiles();
            if (!profiles.Any())
            {
                // Optionally show "No connections saved"
                var emptyItem = new MenuItem
                {
                    Header = "(No external connections saved)",
                    IsEnabled = false
                };
                switchExternalMenuItem.Items.Add(emptyItem);
                return;
            }

            // Get current default connection values
            string currentHost = _settings.External_Host ?? "";
            string currentPort = _settings.External_Port ?? "3306";
            string currentDb = _settings.External_Database ?? "";

            foreach (var profile in profiles)
            {
                // Check if this profile matches the current default settings
                bool isActive = (profile.Host == currentHost) &&
                                (profile.Port.ToString() == currentPort) &&
                                (profile.Database == currentDb);

                var menuItem = new MenuItem
                {
                    Header = $"{profile.Host}:{profile.Port}/{profile.Database}",
                    IsCheckable = true,
                    IsChecked = isActive,
                    Tag = profile // Store the full profile for click handler
                };
                menuItem.Click += SwitchExternalDbContextMenuItem_Click;
                switchExternalMenuItem.Items.Add(menuItem);
            }
        }

        // Right click Sub Menu Local DB list - switch
        private void SwitchLocalDbContextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string dbPath)
            {
                // Switch to this database
                SwitchToLocalDatabase(dbPath);
            }
        }
        private void SwitchExternalDbContextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is ExternalDbProfile profile)
            {
                // Set as default external connection
                _settings.External_Host = profile.Host;
                _settings.External_Port = profile.Port.ToString();
                _settings.External_Database = profile.Database;
                _settings.External_Username = profile.Username;
                _settings.External_Password = profile.Password;
                _settings.Save();

                // If already in External mode, connect immediately
                if (_currentMode == DatabaseMode.External)
                {
                    var connString = BuildExternalConnectionString();
                    if (!string.IsNullOrEmpty(connString))
                    {
                        ConnectToExternalDatabase(connString);
                        UpdateStatusBar(); // Refresh status bar to show new DB
                    }
                    else
                    {
                        MessageBox.Show("Incomplete connection details.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    // Just save — user will switch mode separately
                    MessageBox.Show($"Default external connection set to:\n{profile}", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        #endregion

        #region Settings and Configuration
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_databasePath, this);
            bool? result = settingsWindow.ShowDialog();
            if (result == true || result == false) // Either OK or Close
            {
                // 👇 Refresh toolbar UI from saved settings
                RefreshToolbarSettingsFromSettingsManager();
            }
        }

        public void RefreshToolbarSettingsFromSettingsManager()
        {

            // 1. Reload toolbar visibility
            _isToolbarVisible = _settings.ViewMainToolbar;
            ViewToolbarMenuItem.IsChecked = _isToolbarVisible;
            UpdateToolbarVisibility();

            // Toolbar Options Menu
            ToolbarOptionsMenu.Visibility = _settings.ViewToolbarOptionsInMenu ? Visibility.Visible : Visibility.Collapsed;

            // 2. Reload submenu item states
            ViewToolbarConnectMenuItem.IsChecked = _settings.ViewToolbarConnect;
            ViewToolbarACOSMenuItem.IsChecked = _settings.ViewToolbarACOS;
            ViewToolbarLocalDBMenuItem.IsChecked = _settings.ViewToolbarLocalDB;
            ViewToolbarImpExMenuItem.IsChecked = _settings.ViewToolbarImpEx;
            ViewToolbarSearchMenuItem.IsChecked = _settings.ViewToolbarSearch;

            // 3. Update visibility of toolbar containers
            ConnectDisconnectGroup.Visibility = _settings.ViewToolbarConnect ? Visibility.Visible : Visibility.Collapsed;
            AcosGroup.Visibility = _settings.ViewToolbarACOS ? Visibility.Visible : Visibility.Collapsed;
            SettingsGroup.Visibility = _settings.ViewToolbarSettings ? Visibility.Visible : Visibility.Collapsed;
            LocalDbGroup.Visibility = _settings.ViewToolbarLocalDB ? Visibility.Visible : Visibility.Collapsed;
            ImportExportGroup.Visibility = _settings.ViewToolbarImpEx ? Visibility.Visible : Visibility.Collapsed;
            NoteToolsGroup.Visibility = _settings.ViewToolbarNoteTools ? Visibility.Visible : Visibility.Collapsed;
            CopyPasteGroup.Visibility = _settings.ViewToolbarCopyPaste ? Visibility.Visible : Visibility.Collapsed;
            SearchGroup.Visibility = _settings.ViewToolbarSearch ? Visibility.Visible : Visibility.Collapsed;

            // Add to RefreshToolbarSettingsFromSettingsManager()
            ViewTitleMenuItem.IsChecked = _settings.ViewMainWindowTitle;
            ViewTagsMenuItem.IsChecked = _settings.ViewMainWindowTags;
            ViewModifiedMenuItem.IsChecked = _settings.ViewMainWindowMod;
            UpdateDataGridColumns(); // This already uses the settings
        }
        internal string BuildExternalConnectionString()
        {
            var host = _settings.External_Host;
            var port = _settings.External_Port?.ToString() ?? "3306";
            var database = _settings.External_Database;
            var username = _settings.External_Username;
            var password = _settings.External_Password ?? "";

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
        #endregion

        #region Main Window Font Management
        private void MainWindowFontMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsManager();
            var fontPicker = new FontMainWindow(
                NotesDataGrid.FontFamily,
                NotesDataGrid.FontWeight,
                NotesDataGrid.FontStyle,
                settings
            )
            {
                Owner = this
            };

            if (fontPicker.ShowDialog() == true)
            {
                // ✅ Apply selected font directly (bypass settings reload)
                NotesDataGrid.FontFamily = fontPicker.SelectedFontFamily;
                NotesDataGrid.FontWeight = fontPicker.SelectedFontWeight;
                NotesDataGrid.FontStyle = fontPicker.SelectedFontStyleEnum;

                // ✅ Save to settings only if "Set as default" is checked
                if (fontPicker.SetAsDefault)
                {
                    settings.DefaultMainFontFamily = fontPicker.SelectedFontFamily.Source;
                    settings.DefaultMainFontWeight = fontPicker.SelectedFontWeight.ToString();
                    settings.DefaultMainFontStyle = fontPicker.SelectedFontStyleEnum.ToString();
                    settings.Save(); // 👈 Ensure Save() is called!
                }
            }
        }
        private void ApplyMainFontToDataGrid()
        {
            var settings = new SettingsManager();
            try
            {
                NotesDataGrid.FontFamily = new FontFamily(settings.DefaultMainFontFamily);
                NotesDataGrid.FontWeight = ParseFontWeight(settings.DefaultMainFontWeight);
                NotesDataGrid.FontStyle = ParseFontStyle(settings.DefaultMainFontStyle);
                // ✅ DO NOT set FontSize — zoom controls it!
            }
            catch
            {
                NotesDataGrid.FontFamily = SystemFonts.MessageFontFamily;
                NotesDataGrid.FontWeight = FontWeights.Normal;
                NotesDataGrid.FontStyle = FontStyles.Normal;
            }
        }

        // Reuse your existing helpers or add these:
        private static FontWeight ParseFontWeight(string weightStr)
        {
            if (weightStr == "Bold") return FontWeights.Bold;
            if (weightStr == "Black") return FontWeights.Black;
            if (weightStr == "ExtraBold") return FontWeights.ExtraBold;
            if (weightStr == "DemiBold") return FontWeights.DemiBold;
            if (weightStr == "Light") return FontWeights.Light;
            if (weightStr == "ExtraLight") return FontWeights.ExtraLight;
            if (weightStr == "Thin") return FontWeights.Thin;
            return FontWeights.Normal;
        }

        private static FontStyle ParseFontStyle(string styleStr)
        {
            if (styleStr == "Italic") return FontStyles.Italic;
            if (styleStr == "Oblique") return FontStyles.Oblique;
            return FontStyles.Normal;
        }
        #endregion

        #region System Tray
        private void SetupNotifyIcon()
        {
            System.IO.Stream iconStream = null;
            try
            {
                var uri = new Uri("pack://application:,,,/Protes_W_Trans.ico");
                var resource = Application.GetResourceStream(uri);
                iconStream = resource?.Stream;
                if (iconStream == null)
                {
                    return;
                }
                var icon = new System.Drawing.Icon(iconStream);
                _notifyIcon = new SWF.NotifyIcon();
                _notifyIcon.Icon = icon;
                _notifyIcon.Visible = false;
                _notifyIcon.Text = "Protes - Note Editor";

                _notifyIcon.DoubleClick += (s, e) =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                };

                // ✅ Enhanced context menu
                var contextMenu = new SWF.ContextMenu();

                contextMenu.MenuItems.Add("Pro Notes Database", (s, e) =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                });

                contextMenu.MenuItems.Add("-"); // separator

                contextMenu.MenuItems.Add("New Note", (s, e) =>
                {
                    if (!_isConnected)
                    {
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                        MessageBox.Show(
                            "Please connect to a database first before creating a new note.",
                            "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var editor = new NoteEditorWindow(noteId: null, onSaveRequested: OnSaveNoteRequested);
                    editor.Show();
                });

                contextMenu.MenuItems.Add("Settings", (s, e) =>
                {
                    var settingsWindow = new SettingsWindow(_databasePath, this);
                    settingsWindow.Show();
                });

                contextMenu.MenuItems.Add("-"); // separator

                contextMenu.MenuItems.Add("Exit", (s, e) =>
                {
                    _isExplicitlyExiting = true;
                    Close();
                });

                _notifyIcon.ContextMenu = contextMenu;
            }
            finally
            {
                iconStream?.Dispose();
            }
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        public void ActivateWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        #endregion

        #region Window Lifecycle
        public void TriggerDisconnect()
        {
            if (_isConnected)
            {
                Disconnect_Click(this, new RoutedEventArgs());
            }
        }

        public bool IsConnected => _isConnected;
        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            _isExplicitlyExiting = true;
            Close();
        }
        protected override void OnClosed(EventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            base.OnClosed(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (_settings.MinimizeToTray && WindowState == WindowState.Minimized)
            {
                Hide();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = true;
                }
            }
            else if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized)
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                }
            }

            base.OnStateChanged(e);
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isExplicitlyExiting)
            {
                // User clicked "Exit" in tray — fully shut down
                if (!_isDisposed)
                {
                    _notifyIcon?.Dispose();
                    _notifyIcon = null;
                    _isDisposed = true;
                }
                base.OnClosing(e);
                return;
            }

            // Otherwise: normal close (e.g. clicked [X])
            if (_settings.CloseToTray)
            {
                e.Cancel = true;  // Prevent actual close
                Hide();           // Hide window

                // Make sure tray icon is visible
                if (_notifyIcon != null && !_isDisposed)
                {
                    _notifyIcon.Visible = true;
                }
            }
            else
            {
                // Close fully (user chose not to close to tray)
                if (!_isDisposed)
                {
                    _notifyIcon?.Dispose();
                    _notifyIcon = null;
                    _isDisposed = true;
                }
                base.OnClosing(e);
            }
        }


        #endregion

        #region Toolbar and Misc UI
        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new Protes.Views.AboutWindow(_settings, () =>
            {
                CatButton.Visibility = _settings.ViewToolbarCat ? Visibility.Visible : Visibility.Collapsed;
            });
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }
        private void AutoConnectOnSwitchCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _settings.AutoConnectOnSwitch = AutoConnectOnSwitchCheckBox.IsChecked == true;
        }
        private void SelectNotesButton_Click(object sender, RoutedEventArgs e)
        {
            _isSelectMode = !_isSelectMode;
            SelectNotesButton.Content = _isSelectMode ? "✔️" : "✅";

            // Toggle checkbox column
            SelectCheckBoxColumn.Visibility = _isSelectMode ? Visibility.Visible : Visibility.Collapsed;

            if (!_isSelectMode)
            {
                // Clear selections
                var items = NotesDataGrid.ItemsSource as List<NoteItem>;
                if (items != null)
                {
                    foreach (var note in items)
                    {
                        note.IsSelected = false;
                    }
                }
                UpdateButtonStates();
            }
        }
        private void CalculatorButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to a database first.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bool isEditable = !_isGateLocked;
            var recentNotes = _fullNotesCache
                .OrderByDescending(n => DateTime.Parse(n.LastModified))
                .Take(10)
                .ToList();
            var calc = new CalculatorWindow(_noteRepository, recentNotes, isEditable, () => LoadNotesFromDatabase());
            calc.Show(); 
        }
        protected override void OnActivated(EventArgs e)
        {
            // Prevent minimize-to-tray from being triggered by reactivation
            if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
            {
                // But still allow user-initiated minimize
                // So do nothing here — just avoid Hide() on reactivation
            }
            base.OnActivated(e);
        }
        private void OpenCatWindow_Click(object sender, RoutedEventArgs e)
        {
            var catWindow = new CatWindow(_settings, () =>
            {
                CatButton.Visibility = _settings.ViewToolbarCat ? Visibility.Visible : Visibility.Collapsed;
            });
            catWindow.Owner = this;
            catWindow.Show();
        }
        #endregion

        #region Properties
        private bool _allItemsAreChecked;
        public bool AllItemsAreChecked
        {
            get => _allItemsAreChecked;
            set
            {
                if (_allItemsAreChecked != value)
                {
                    _allItemsAreChecked = value;
                    OnPropertyChanged();

                    var items = NotesDataGrid.ItemsSource as List<NoteItem>;
                    if (items != null)
                    {
                        _isBulkUpdating = true; // 👈 START BULK UPDATE
                        try
                        {
                            for (int i = 0; i < items.Count; i++)
                            {
                                items[i].IsSelected = value;
                            }
                        }
                        finally
                        {
                            _isBulkUpdating = false; // 👈 END BULK UPDATE
                        }
                    }
                    UpdateButtonStates();
                }
            }
        }

        private bool _isBulkUpdating = false;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Expose current DB path for SettingsWindow access
        public string CurrentDbPathForSettings
        {
            get
            {
                // Return last local DB path if in Local mode, or fallback
                return _settings.LastLocalDatabasePath
                    ?? _databasePath
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes", "notes.db");
            }
        }
        #endregion

        #region Nested Types
        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }
        #endregion
 
    }

    public class NoteItem : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string Preview { get; set; }
        public string LastModified { get; set; }
        public string Tags { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }



    public class DbFileInfo
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public bool IsImported { get; set; }
        public string DisplayNameWithIndicator { get; set; } 
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