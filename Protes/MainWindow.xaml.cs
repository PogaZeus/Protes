using MySqlConnector;
using Protes.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Protes
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes", "notes.db");
        private DatabaseMode _currentMode = DatabaseMode.None;
        private DatabaseMode _pendingModeSwitch = DatabaseMode.None;
        private DatabaseMode _connectedMode = DatabaseMode.None;
        private bool _isConnected = false;
        private INoteRepository _noteRepository;
        private readonly SettingsManager _settings = new SettingsManager();
        private List<FullNote> _fullNotesCache = new List<FullNote>();
        private NoteItem _editingItem;
        private string _originalTitle;
        private string _externalConnectionString = "";
        private bool _isToolbarVisible = true;
        private bool _isSelectMode = false;
        // Zoom settings
        private const double DEFAULT_ZOOM = 13.0;
        private const double MIN_ZOOM = 10.0;
        private const double MAX_ZOOM = 20.0;
        public MainWindow()
        {
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

            // Load View settings
            ViewTitleMenuItem.IsChecked = _settings.ViewMainWindowTitle;
            ViewTagsMenuItem.IsChecked = _settings.ViewMainWindowTags;
            ViewModifiedMenuItem.IsChecked = _settings.ViewMainWindowMod;
            ViewToolbarMenuItem.IsChecked = _settings.ViewMainToolbar;
            _isToolbarVisible = _settings.ViewMainToolbar;
            NotesDataGrid.DataContext = this;

            // Load Toolbar Submenu Settings
            ViewToolbarConnectMenuItem.IsChecked = _settings.ViewToolbarConnect;
            ViewToolbarACOLMenuItem.IsChecked = _settings.ViewToolbarACOL;
            ViewToolbarACOSMenuItem.IsChecked = _settings.ViewToolbarACOS;
            ViewToolbarLocalDBMenuItem.IsChecked = _settings.ViewToolbarLocalDB;

            // Apply initial visibility
            ViewToolbarConnectainer.Visibility = _settings.ViewToolbarConnect ? Visibility.Visible : Visibility.Collapsed;
            AutoConnectOLContainer.Visibility = _settings.ViewToolbarACOL ? Visibility.Visible : Visibility.Collapsed;
            AutoConnectOSContainer.Visibility = _settings.ViewToolbarACOS ? Visibility.Visible : Visibility.Collapsed;
            LocalDbControls.Visibility = _settings.ViewToolbarLocalDB ? Visibility.Visible : Visibility.Collapsed;

            // Load zoom level
            double zoom = _settings.DataGridZoom;
            if (zoom < MIN_ZOOM) zoom = DEFAULT_ZOOM;
            if (zoom > MAX_ZOOM) zoom = DEFAULT_ZOOM;
            NotesDataGrid.FontSize = zoom;

            // Apply initial state
            UpdateDataGridColumns();
            UpdateToolbarVisibility();

            Loaded += MainWindow_Loaded;
    
            // Attach to row checkboxes via event handlers
            NotesDataGrid.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(OnCheckboxChanged));
            NotesDataGrid.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(OnCheckboxChanged));
            NotesDataGrid.SelectionChanged += (s, e) => UpdateButtonStates();
            var showNotifications = _settings.ShowNotifications;
            SelectCheckBoxColumn.Visibility = Visibility.Collapsed;

            // Hide/show "Toolbar Options" menu based on settings
            {
                var viewMenu = (MenuItem)MainMenu.Items[2];
                if (!_settings.ViewToolbarOptionsInMenu)
                {
                    viewMenu.Items.Remove(ToolbarOptionsMenu);
                }
            }
        }

        private void OnCheckboxChanged(object sender, RoutedEventArgs e)
        {
            if (_isBulkUpdating) return;

            var items = NotesDataGrid.ItemsSource as List<NoteItem>;
            if (items != null)
            {
                bool allChecked = items.All(item => item.IsSelected);
                if (AllItemsAreChecked != allChecked)
                {
                    AllItemsAreChecked = allChecked;
                }
            }
            UpdateButtonStates();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded; // prevent multiple calls

            if (_settings.AutoConnect)
            {
                if (_currentMode == DatabaseMode.Local)
                {
                    Connect_Click(this, new RoutedEventArgs());
                }
                else if (_currentMode == DatabaseMode.External)
                {
                    var host = _settings.External_Host;
                    var database = _settings.External_Database;
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
        private void ZoomInMenuItem_Click(object sender, RoutedEventArgs e)
        {
            double newZoom = NotesDataGrid.FontSize + 1.0;
            if (newZoom <= MAX_ZOOM)
            {
                NotesDataGrid.FontSize = newZoom;
                _settings.DataGridZoom = newZoom;
            }
        }

        private void ZoomOutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            double newZoom = NotesDataGrid.FontSize - 1.0;
            if (newZoom >= MIN_ZOOM)
            {
                NotesDataGrid.FontSize = newZoom;
                _settings.DataGridZoom = newZoom;
            }
        }

        private void RestoreZoomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            NotesDataGrid.FontSize = DEFAULT_ZOOM;
            _settings.DataGridZoom = DEFAULT_ZOOM;
        }

        // Toolbar options
        private void AutoConnectCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _settings.AutoConnect = AutoConnectCheckBox.IsChecked == true;
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

        // helper methods
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
            NewNoteButton.IsEnabled = isConnected;
            EditNoteButton.IsEnabled = isConnected && selectedCount == 1;
            DeleteNoteButton.IsEnabled = isConnected && selectedCount >= 1;
            SearchBox.IsEnabled = isConnected && !_isSelectMode;
            ConnectIconBtn.IsEnabled = !isConnected;
            DisconnectIconBtn.IsEnabled = isConnected;
            SelectNotesButton.IsEnabled = isConnected;

            // File menu
            NewNoteMenuItem.IsEnabled = isConnected;
            EditNoteMenuItem.IsEnabled = isConnected && selectedCount == 1;
            DeleteNoteMenuItem.IsEnabled = isConnected && selectedCount >= 1;

            // Connect/Disconnect menu items
            if (MainMenu?.Items.Count > 0)
            {
                var fileMenuItem = MainMenu.Items[0] as MenuItem;
                if (fileMenuItem?.Items.Count >= 6)
                {
                    var connectMenuItem = fileMenuItem.Items[4] as MenuItem;
                    var disconnectMenuItem = fileMenuItem.Items[5] as MenuItem;
                    connectMenuItem?.SetValue(MenuItem.IsEnabledProperty, !isConnected);
                    disconnectMenuItem?.SetValue(MenuItem.IsEnabledProperty, isConnected);
                }
            }

            // 🔥 CRITICAL FIX: Disable local DB switcher unless in Local mode
            bool isLocalMode = (_currentMode == DatabaseMode.Local);
            AvailableDatabasesComboBox.IsEnabled = isLocalMode;
            LoadSelectedDbButton.IsEnabled = isLocalMode;
        }

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
                            System.Diagnostics.Debug.WriteLine($"Setting all {items.Count} items to {value}");
                            for (int i = 0; i < items.Count; i++)
                            {
                                items[i].IsSelected = value;
                                System.Diagnostics.Debug.WriteLine($"  Item {i}: ID={items[i].Id}, Title={items[i].Title}, IsSelected={items[i].IsSelected}");
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

        private void LoadSelectedDbButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = AvailableDatabasesComboBox.SelectedItem as DbFileInfo;
            if (selectedItem != null)
            {
                // Save as last used (optional but recommended)
                var settings = new SettingsManager();
                settings.LastLocalDatabasePath = selectedItem.FullPath;
                settings.Save();

                // Switch to it
                SwitchToLocalDatabase(selectedItem.FullPath);
            }
        }

        //Data Grid (Database Content)
        private void UpdateDataGridColumns()
        {
            if (NotesDataGrid.Columns.Count >= 4)
            {
                NotesDataGrid.Columns[1].Visibility = ViewTitleMenuItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                NotesDataGrid.Columns[3].Visibility = ViewTagsMenuItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                NotesDataGrid.Columns[4].Visibility = ViewModifiedMenuItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private void NotesDataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;

            // 👇 NEW: Ignore clicks inside column headers
            var parentHeader = FindVisualParent<DataGridColumnHeader>(originalSource);
            if (parentHeader != null)
            {
                // Let the header handle the click (e.g., checkbox)
                return;
            }

            // Walk up to find a DataGridRow (for row clicks)
            while (originalSource != null && !(originalSource is DataGridRow))
            {
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            if (originalSource == null)
            {
                // Click on empty space → deselect
                NotesDataGrid.SelectedItem = null;
                NotesDataGrid.CurrentCell = new DataGridCellInfo();
                UpdateButtonStates();
                e.Handled = true;
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

        // ===== INLINE GRID EDITING (Title only) =====

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

        // ===== NEW: Toolbar Visibility Submenu =====

        private void ViewToolbarConnectMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isVisible = ViewToolbarConnectMenuItem.IsChecked == true;
            _settings.ViewToolbarConnect = isVisible;
            ViewToolbarConnectainer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ViewToolbarACOLMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isVisible = ViewToolbarACOLMenuItem.IsChecked == true;
            _settings.ViewToolbarACOL = isVisible;
            AutoConnectOLContainer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ViewToolbarACOSMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isVisible = ViewToolbarACOSMenuItem.IsChecked == true;
            _settings.ViewToolbarACOS = isVisible;
            AutoConnectOSContainer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ViewToolbarLocalDBMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isVisible = ViewToolbarLocalDBMenuItem.IsChecked == true;
            _settings.ViewToolbarLocalDB = isVisible;
            LocalDbControls.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
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
            _currentMode = _settings.GetDatabaseMode();
            AutoConnectCheckBox.IsChecked = _settings.AutoConnect;
            AutoConnectOnSwitchCheckBox.IsChecked = _settings.AutoConnectOnSwitch;
            UpdateDatabaseModeCheckmarks();
        }

        public void UpdateDatabaseModeCheckmarks()
        {
            var localItem = (MenuItem)OptionsMenu.Items[0];
            var externalItem = (MenuItem)OptionsMenu.Items[1];

            localItem.IsChecked = (_currentMode == DatabaseMode.Local);
            externalItem.IsChecked = (_currentMode == DatabaseMode.External);
            externalItem.IsEnabled = !string.IsNullOrWhiteSpace(_settings.External_Host) &&
                                    !string.IsNullOrWhiteSpace(_settings.External_Database);
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
                    _settings.SetDatabaseMode(DatabaseMode.Local);
                    UpdateDatabaseModeCheckmarks();
                    UpdateStatusBar();
                    LoadAvailableDatabases(); // ✅ Refresh dropdown when entering Local mode
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
                    LoadAvailableDatabases(); // ✅ Still in Local mode (pending), so show list
                    return;
                }
            }

            // Full switch logic
            _currentMode = DatabaseMode.Local;
            _settings.SetDatabaseMode(DatabaseMode.Local);
            UpdateDatabaseModeCheckmarks();

            if (_settings.AutoConnectOnSwitch)
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

                    if (_isConnected && _settings.AutoDisconnectOnSwitch)
                    {
                        Disconnect_Click(this, new RoutedEventArgs());
                    }

                    _noteRepository = new SqliteNoteRepository(_databasePath);
                    LoadNotesFromDatabase();
                    _isConnected = true;
                    _connectedMode = DatabaseMode.Local;
                    NotesDataGrid.Visibility = Visibility.Visible;
                    DisconnectedPlaceholder.Visibility = Visibility.Collapsed;
                    LoadAvailableDatabases(); // ✅ Connected → refresh list
                    UpdateButtonStates();
                    UpdateStatusBar();

                    if (_settings.ShowNotifications)
                    {
                        MessageBox.Show("Local database (SQLite) selected.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to connect to local database:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                    _isConnected = false;
                    _connectedMode = DatabaseMode.None;
                    LoadAvailableDatabases(); // ✅ Still Local mode, just disconnected
                    UpdateButtonStates();
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
                LoadAvailableDatabases(); // ✅ Local mode selected → show DB list
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

                if (_isConnected && _settings.AutoDisconnectOnSwitch)
                {
                    _isConnected = false;
                }

                _noteRepository = new SqliteNoteRepository(_databasePath);
                LoadNotesFromDatabase();
                _isConnected = true;
                _connectedMode = DatabaseMode.Local;
                NotesDataGrid.Visibility = Visibility.Visible;
                DisconnectedPlaceholder.Visibility = Visibility.Collapsed;
                LoadAvailableDatabases(); // ✅ Always refresh after switching DB
                UpdateStatusBar();
                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect to local database:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                _isConnected = false;
                _connectedMode = DatabaseMode.None;
                LoadAvailableDatabases(); // ✅ Still in Local mode context
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
                    return;
                }
            }

            // Full switch logic
            _currentMode = DatabaseMode.External;
            _settings.SetDatabaseMode(DatabaseMode.External);
            UpdateDatabaseModeCheckmarks();

            if (_settings.AutoConnectOnSwitch)
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

                if (_isConnected && _settings.AutoDisconnectOnSwitch)
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
                _noteRepository = new MySqlNoteRepository(connectionString);
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

            if (_settings.ShowNotifications)
            {
                MessageBox.Show("Connected successfully!", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
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
            if (_settings.ShowNotifications)
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

            // 2. Reload submenu item states
            ViewToolbarConnectMenuItem.IsChecked = _settings.ViewToolbarConnect;
            ViewToolbarACOLMenuItem.IsChecked = _settings.ViewToolbarACOL;
            ViewToolbarACOSMenuItem.IsChecked = _settings.ViewToolbarACOS;
            ViewToolbarLocalDBMenuItem.IsChecked = _settings.ViewToolbarLocalDB;

            // 3. Update visibility of toolbar containers
            ViewToolbarConnectainer.Visibility = _settings.ViewToolbarConnect ? Visibility.Visible : Visibility.Collapsed;
            AutoConnectOLContainer.Visibility = _settings.ViewToolbarACOL ? Visibility.Visible : Visibility.Collapsed;
            AutoConnectOSContainer.Visibility = _settings.ViewToolbarACOS ? Visibility.Visible : Visibility.Collapsed;
            LocalDbControls.Visibility = _settings.ViewToolbarLocalDB ? Visibility.Visible : Visibility.Collapsed;

            // Add to RefreshToolbarSettingsFromSettingsManager()
            ViewTitleMenuItem.IsChecked = _settings.ViewMainWindowTitle;
            ViewTagsMenuItem.IsChecked = _settings.ViewMainWindowTags;
            ViewModifiedMenuItem.IsChecked = _settings.ViewMainWindowMod;
            UpdateDataGridColumns(); // This already uses the settings

            var viewMenu = (MenuItem)MainMenu.Items[2]; // Assuming "_View" is the 3rd top-level menu item
            var toolbarOptionsMenu = ToolbarOptionsMenu;

            bool shouldShow = _settings.ViewToolbarOptionsInMenu;

            if (shouldShow && !viewMenu.Items.Contains(toolbarOptionsMenu))
            {
                // Insert it after "Toolbar" (which is the item before it)
                int toolbarIndex = viewMenu.Items.IndexOf(ViewToolbarMenuItem);
                viewMenu.Items.Insert(toolbarIndex + 1, toolbarOptionsMenu);
            }
            else if (!shouldShow && viewMenu.Items.Contains(toolbarOptionsMenu))
            {
                viewMenu.Items.Remove(toolbarOptionsMenu);
            }
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
            var originalSource = e.OriginalSource as DependencyObject;

            // 👇 NEW: Ignore double-click if it's on ANY column header
            if (FindVisualParent<DataGridColumnHeader>(originalSource) != null)
            {
                return; // Do nothing when double-clicking headers
            }

            // Proceed with edit only if a note is selected
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
                MessageBox.Show($"{notesToDelete.Count} note{(notesToDelete.Count == 1 ? "" : "s")} deleted successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete note(s):\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
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

        // ===== Load Notes

        private void LoadNotesFromDatabase(string searchTerm = "", string searchField = "All")
        {
            // 🔒 SAFETY: If no repository, show disconnected state
            if (_noteRepository == null)
            {
                // Clear UI
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
                    Preview = note.Content.Length > 60 ? note.Content.Substring(0, 57) + "..." : note.Content,
                    Tags = note.Tags,
                    LastModified = note.LastModified,
                    //IsSelected = false
                }).ToList();

                NotesDataGrid.ItemsSource = noteItems;
                AllItemsAreChecked = false;
                NoteCountText.Text = $"{noteItems.Count} Note{(noteItems.Count == 1 ? "" : "s")}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load notes:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                NotesDataGrid.ItemsSource = null;
                _fullNotesCache.Clear();
                NoteCountText.Text = "";
            }
        }

        // Load Database for the Dropdown Menu
        private void LoadAvailableDatabases()
        {
            var settings = new SettingsManager();
            var dbFiles = new List<DbFileInfo>();

            // Default folder (user-defined or fallback)
            string defaultFolder = settings.DefaultDatabaseFolder;
            if (string.IsNullOrWhiteSpace(defaultFolder) || !Directory.Exists(defaultFolder))
            {
                defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protes");
            }

            if (Directory.Exists(defaultFolder))
            {
                foreach (var file in Directory.GetFiles(defaultFolder, "*.db"))
                {
                    dbFiles.Add(new DbFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        IsImported = false
                    });
                }
            }

            // Imported paths
            var importedRaw = settings.ImportedDatabasePaths;
            var importedPaths = new List<string>();
            if (!string.IsNullOrWhiteSpace(importedRaw))
            {
                importedPaths = importedRaw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Where(p => File.Exists(p)).ToList();
            }

            foreach (var path in importedPaths)
            {
                dbFiles.Add(new DbFileInfo
                {
                    FileName = Path.GetFileName(path) + " (imported)",
                    FullPath = path,
                    IsImported = true
                });
            }

            // Remove duplicates by path
            var uniqueFiles = dbFiles.GroupBy(f => f.FullPath).Select(g => g.First()).ToList();

            // Set to ComboBox
            AvailableDatabasesComboBox.ItemsSource = uniqueFiles;

            // Select LastLocalDatabasePath if available
            string lastPath = settings.LastLocalDatabasePath;
            if (!string.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
            {
                foreach (DbFileInfo item in uniqueFiles)
                {
                    if (item.FullPath.Equals(lastPath, StringComparison.OrdinalIgnoreCase))
                    {
                        AvailableDatabasesComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        // ===== HELPERS =====

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

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new Protes.Views.AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }
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