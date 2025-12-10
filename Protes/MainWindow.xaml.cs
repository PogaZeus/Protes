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
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
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
        private bool _isToolbarVisible = true;
        private bool _isSelectMode = false;
        private bool _isConnected = false;
        private bool _isExplicitlyExiting = false;
        private bool _isDisposed = false;
        // Zoom settings
        private const double DEFAULT_ZOOM_POINTS = 11.0;
        private const double MIN_ZOOM_POINTS = 8.0;
        private const double MAX_ZOOM_POINTS = 24.0;

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

            // Load View settings
            _isToolbarVisible = _settings.ViewMainToolbar;
            NotesDataGrid.DataContext = this;
            ViewTitleMenuItem.IsChecked = _settings.ViewMainWindowTitle;
            ViewTagsMenuItem.IsChecked = _settings.ViewMainWindowTags;
            ViewModifiedMenuItem.IsChecked = _settings.ViewMainWindowMod;
            ViewToolbarMenuItem.IsChecked = _settings.ViewMainToolbar;
            // Load Toolbar Submenu Settings
            ViewToolbarConnectMenuItem.IsChecked = _settings.ViewToolbarConnect;
            ViewToolbarLocalDBMenuItem.IsChecked = _settings.ViewToolbarLocalDB;
            ViewToolbarACOSMenuItem.IsChecked = _settings.ViewToolbarACOS;
            ViewToolbarImpExMenuItem.IsChecked = _settings.ViewToolbarImpEx;
            ViewToolbarSearchMenuItem.IsChecked = _settings.ViewToolbarSearch;
            // Apply initial visibility
            ViewToolbarConnectainer.Visibility = _settings.ViewToolbarConnect ? Visibility.Visible : Visibility.Collapsed;
            AutoConnectOSContainer.Visibility = _settings.ViewToolbarACOS ? Visibility.Visible : Visibility.Collapsed;
            LocalDbControls.Visibility = _settings.ViewToolbarLocalDB ? Visibility.Visible : Visibility.Collapsed;
            ImportExportControls.Visibility = _settings.ViewToolbarImpEx ? Visibility.Visible : Visibility.Collapsed;
            SearchDatabase.Visibility = _settings.ViewToolbarSearch ? Visibility.Visible : Visibility.Collapsed;
            CatButton.Visibility = _settings.ViewToolbarCat ? Visibility.Visible : Visibility.Collapsed;

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

            var showNotifications = _settings.ShowNotifications;
            SelectCheckBoxColumn.Visibility = Visibility.Collapsed;
            Loaded += MainWindow_Loaded;
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

        // Checkbox selection
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

        // Toolbar options
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
        private void OpenCatWindow_Click(object sender, RoutedEventArgs e)
        {
            var catWindow = new CatWindow(_settings, () =>
            {
                CatButton.Visibility = _settings.ViewToolbarCat ? Visibility.Visible : Visibility.Collapsed;
            });
            catWindow.Owner = this;
            catWindow.Show();
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
                _settings.LastLocalDatabasePath = selectedItem.FullPath;

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
            _settings.Save();
            ViewToolbarConnectainer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        private void ViewToolbarLocalDBMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isVisible = ViewToolbarLocalDBMenuItem.IsChecked == true;
            _settings.ViewToolbarLocalDB = isVisible;
            _settings.Save();
            LocalDbControls.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ViewToolbarACOSMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isVisible = ViewToolbarACOSMenuItem.IsChecked == true;
            _settings.ViewToolbarACOS = isVisible;
            _settings.Save();
            AutoConnectOSContainer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        private void ViewToolbarImpExMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isVisible = ViewToolbarImpExMenuItem.IsChecked == true;
            _settings.ViewToolbarImpEx = isVisible;
            _settings.Save();
            ImportExportControls.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ViewToolbarSearchMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            bool isVisible = ViewToolbarSearchMenuItem.IsChecked == true;
            _settings.ViewToolbarSearch = isVisible;
            _settings.Save();
            SearchDatabase.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

         public void RefreshToolbarVisibility()
        {
            ViewToolbarConnectainer.Visibility = _settings.ViewToolbarConnect ? Visibility.Visible : Visibility.Collapsed;
            LocalDbControls.Visibility = _settings.ViewToolbarLocalDB ? Visibility.Visible : Visibility.Collapsed;
            AutoConnectOSContainer.Visibility = _settings.ViewToolbarACOS ? Visibility.Visible : Visibility.Collapsed;
            ImportExportControls.Visibility = _settings.ViewToolbarImpEx ? Visibility.Visible : Visibility.Collapsed;
            SearchDatabase.Visibility = _settings.ViewToolbarSearch ? Visibility.Visible : Visibility.Collapsed;
            CatButton.Visibility = _settings.ViewToolbarCat ? Visibility.Visible : Visibility.Collapsed;
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
            AutoConnectOnSwitchCheckBox.IsChecked = _settings.AutoConnectOnSwitch;
            UpdateDatabaseModeCheckmarks();
        }

        public void UpdateDatabaseModeCheckmarks()
        {
            // Main Options Menu (existing)
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
            System.Diagnostics.Debug.WriteLine($"=== CONNECT CLICKED === _isConnected = {_isConnected}");
            System.Diagnostics.Debug.WriteLine($"Stack trace:\n{Environment.StackTrace}");
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
            System.Diagnostics.Debug.WriteLine("=== DISCONNECT CLICKED ===");
            System.Diagnostics.Debug.WriteLine($"Window hashcode: {this.GetHashCode()}");
            System.Diagnostics.Debug.WriteLine($"BEFORE: _isConnected = {_isConnected}");

            _isConnected = false;
            _connectedMode = DatabaseMode.None;
            _pendingModeSwitch = DatabaseMode.None;
            NotesDataGrid.ItemsSource = null;
            NotesDataGrid.Visibility = Visibility.Collapsed;
            DisconnectedPlaceholder.Visibility = Visibility.Visible;
            System.Diagnostics.Debug.WriteLine($"AFTER: _isConnected = {_isConnected}");

            UpdateStatusBar();
            UpdateButtonStates();
            NotesDataGrid_ContextMenuOpening(null, null);
            // Show notification only if enabled
            if (_settings.ShowNotifications)
            {
                MessageBox.Show("Disconnected.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            System.Diagnostics.Debug.WriteLine("=== DISCONNECT COMPLETE ===");
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            _isExplicitlyExiting = true;
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

            // Toolbar Options Menu
            ToolbarOptionsMenu.Visibility = _settings.ViewToolbarOptionsInMenu ? Visibility.Visible : Visibility.Collapsed;

            // 2. Reload submenu item states
            ViewToolbarConnectMenuItem.IsChecked = _settings.ViewToolbarConnect;
            ViewToolbarACOSMenuItem.IsChecked = _settings.ViewToolbarACOS;
            ViewToolbarLocalDBMenuItem.IsChecked = _settings.ViewToolbarLocalDB;
            ViewToolbarImpExMenuItem.IsChecked = _settings.ViewToolbarImpEx;
            ViewToolbarSearchMenuItem.IsChecked = _settings.ViewToolbarSearch;

            // 3. Update visibility of toolbar containers
            ViewToolbarConnectainer.Visibility = _settings.ViewToolbarConnect ? Visibility.Visible : Visibility.Collapsed;
            AutoConnectOSContainer.Visibility = _settings.ViewToolbarACOS ? Visibility.Visible : Visibility.Collapsed;
            LocalDbControls.Visibility = _settings.ViewToolbarLocalDB ? Visibility.Visible : Visibility.Collapsed;
            SearchDatabase.Visibility = _settings.ViewToolbarSearch ? Visibility.Visible : Visibility.Collapsed;
            ImportExportControls.Visibility = _settings.ViewToolbarImpEx ? Visibility.Visible : Visibility.Collapsed;
            CatButton.Visibility = _settings.ViewToolbarCat ? Visibility.Visible : Visibility.Collapsed;

            // Add to RefreshToolbarSettingsFromSettingsManager()
            ViewTitleMenuItem.IsChecked = _settings.ViewMainWindowTitle;
            ViewTagsMenuItem.IsChecked = _settings.ViewMainWindowTags;
            ViewModifiedMenuItem.IsChecked = _settings.ViewMainWindowMod;
            UpdateDataGridColumns(); // This already uses the settings
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
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isConnected)
            {
                string selectedField = (SearchFieldComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Title";
                LoadNotesFromDatabase(SearchBox.Text, selectedField);
            }
        }

        // ===== Load Notes
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
                    Preview = GetFirstTwoLines(note.Content),
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
            string lastPath = _settings.LastLocalDatabasePath;
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

        //Opening a .prote file (External file incoming!)
        //Opening a .prote file (External file incoming!)
        public void HandleIpcMessage(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (message == "-new")
                {
                    // User wants to create a new note in the currently connected database
                    // DON'T activate MainWindow - keep it in tray if minimized

                    if (!_isConnected)
                    {
                        // Only show MainWindow if we need to display error
                        ActivateWindow();
                        MessageBox.Show(
                            "Please connect to a database first before creating a new note.\n\n" +
                            "Go to Options → Use Local Database or Use External Database, then click Connect.",
                            "Protes",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    var editor = new NoteEditorWindow(
                        noteId: null,
                        onSaveRequested: OnSaveNoteRequested
                    );
                    // Don't set Owner so NoteEditor stays independent on taskbar
                    // when MainWindow is minimized to tray
                    editor.Show();
                }
                else if (File.Exists(message) &&
                         (message.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                          message.EndsWith(".prote", StringComparison.OrdinalIgnoreCase)))
                {
                    // User double-clicked a .prote/.db file - check if imported and switch
                    PromptAndSwitchDatabase(message);
                }
                else
                {
                    // Unknown message or file doesn't exist - just activate
                    ActivateWindow();
                }
            }));
        }


        public void ActivateWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        // font main window
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

        // ===== DATABASE IMPORT HELPERS =====
        // Add these methods to your MainWindow.xaml.cs class

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

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }
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
                    System.Diagnostics.Debug.WriteLine("Tray icon 'Protes_W_Trans.ico' not found as resource.");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to setup tray icon: " + ex.Message);
                iconStream?.Dispose();
            }
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

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

        // ===== COPY/PASTE =====
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


        // font
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
        // Right click Sub Menu Local DB list - switch
        private void SwitchLocalDbContextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string dbPath)
            {
                // Switch to this database
                SwitchToLocalDatabase(dbPath);
            }
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new Protes.Views.AboutWindow(_settings, () =>
            {
                CatButton.Visibility = _settings.ViewToolbarCat ? Visibility.Visible : Visibility.Collapsed;
            });
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        // Close to system tray;

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