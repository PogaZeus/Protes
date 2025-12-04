using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Protes.Views
{
    public partial class ImportToDBWindow : Window
    {
        private readonly string _databasePath;
        private readonly DatabaseMode _databaseMode;
        private readonly string _externalConnectionString;
        private readonly INoteRepository _noteRepository;
        private readonly ObservableCollection<ImportFileItem> _fileItems = new ObservableCollection<ImportFileItem>();
        private CancellationTokenSource _scanCancellationTokenSource;
        public event PropertyChangedEventHandler PropertyChanged;
        private int _totalFilesToScan = 0;
        private int _filesScanned = 0;
        private readonly object _progressLock = new object();
        private readonly Action _onImportCompleted;
        private bool _isBulkUpdating = false;
        public ImportToDBWindow(string databasePath, DatabaseMode databaseMode, string externalConnectionString, INoteRepository noteRepository, Action onImportCompleted)
        {
            InitializeComponent();
            DataContext = this;
            _databasePath = databasePath;
            _databaseMode = databaseMode;
            _externalConnectionString = externalConnectionString;
            _noteRepository = noteRepository;
            _onImportCompleted = onImportCompleted;
            _fileItems = new ObservableCollection<ImportFileItem>();
            FileListDataGrid.ItemsSource = _fileItems;
            UpdateClearListButtonState();
            UpdateDatabaseInfo();

            // Enable Import button if any item is selected
            _fileItems.CollectionChanged += (s, e) => UpdateImportButtonState();
            FileListDataGrid.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(OnItemChecked));
            FileListDataGrid.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(OnItemChecked));
        }
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private void UpdateDatabaseInfo()
        {
            if (_databaseMode == DatabaseMode.Local)
            {
                DatabaseInfoText.Text = $"Database: Local ({_databasePath})";
            }
            else
            {
                DatabaseInfoText.Text = $"Database: External (Notes table)";
            }
        }

        private void AddFilesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt;*.md;*.csv)|*.txt;*.md;*.csv",
                Multiselect = true
            };
            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!_fileItems.Any(f => f.FullPath == file))
                    {
                        _fileItems.Add(new ImportFileItem { FullPath = file });
                    }
                }
                UpdateImportButtonState();
                UpdateClearListButtonState(); 
            }
        }

        private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            // Cancel any ongoing scan
            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource = new CancellationTokenSource();

            string rootFolder = dialog.SelectedPath;

            // Reset counters
            _filesScanned = 0;
            _totalFilesToScan = 0;

            UpdateStatus("Scanning folder...", isScanning: true);

            try
            {
                var scannedFiles = await Task.Run(() =>
                {
                    var files = new List<string>();
                    ScanFolderRecursively(rootFolder, files, _scanCancellationTokenSource.Token);
                    return files;
                }, _scanCancellationTokenSource.Token);

                // Filter by extension
                var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".csv" };
                var validFiles = scannedFiles.Where(f => allowedExtensions.Contains(Path.GetExtension(f))).ToList();

                // Add to list (avoid duplicates)
                foreach (var file in validFiles)
                {
                    if (!_fileItems.Any(f => f.FullPath == file))
                    {
                        _fileItems.Add(new ImportFileItem { FullPath = file });
                    }
                }
                UpdateClearListButtonState();
                if (validFiles.Count == 0)
                {
                    UpdateStatus("No .txt, .md, or .csv files found.");
                    MessageBox.Show("No valid files found in the selected folder.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    UpdateStatus($"{validFiles.Count} file(s) added.");
                }
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Scan canceled.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Scan failed.");
                MessageBox.Show($"Failed to scan folder:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _scanCancellationTokenSource?.Dispose();
                _scanCancellationTokenSource = null;
            }
        }

        private void ScanFolderRecursively(string folderPath, List<string> fileList, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                var files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
                lock (_progressLock)
                {
                    _totalFilesToScan += files.Length;
                    fileList.AddRange(files);
                    _filesScanned += files.Length;

                    // Update status text on UI thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        string msg = $"Scanning {_filesScanned:N0} of {_totalFilesToScan:N0} files...";
                        StatusText.Text = msg;
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }

                var subDirs = Directory.GetDirectories(folderPath);
                foreach (var subDir in subDirs)
                {
                    if (token.IsCancellationRequested) return;
                    ScanFolderRecursively(subDir, fileList, token);
                }
            }
            catch (UnauthorizedAccessException) { /* Skip */ }
            catch (DirectoryNotFoundException) { /* Skip */ }
            catch (IOException) { /* Skip */ }
        }

        // === HEADER CHECKBOX (FIXED) ===
        private bool _allItemsAreChecked;
        public bool AllItemsAreChecked
        {
            get => _allItemsAreChecked;
            set
            {
                if (_allItemsAreChecked != value)
                {
                    _allItemsAreChecked = value;
                    OnPropertyChanged(nameof(AllItemsAreChecked));

                    _isBulkUpdating = true; // 👈 START BULK
                    try
                    {
                        foreach (var item in _fileItems)
                        {
                            item.IsSelected = value;
                        }
                    }
                    finally
                    {
                        _isBulkUpdating = false; // 👈 END BULK
                    }
                    UpdateImportButtonState();
                }
            }
        }

        private void OnItemChecked(object sender, RoutedEventArgs e)
        {
            if (_isBulkUpdating) return;
            var allChecked = _fileItems.All(f => f.IsSelected);
            var anyChecked = _fileItems.Any(f => f.IsSelected);

            AllItemsAreChecked = allChecked; // This updates the header via binding
            UpdateImportButtonState();
        }

        private void UpdateImportButtonState()
        {
            ImportButton.IsEnabled = _fileItems.Any(f => f.IsSelected);
        }

        // === IMPORT LOGIC ===
        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = _fileItems.Where(f => f.IsSelected).ToList();
            if (!selectedFiles.Any()) return;

            try
            {
                foreach (var fileItem in selectedFiles)
                {
                    var ext = Path.GetExtension(fileItem.FullPath).ToLowerInvariant();
                    if (ext == ".csv")
                    {
                        ImportCsvFile(fileItem.FullPath);
                    }
                    else
                    {
                        ImportTextFile(fileItem.FullPath);
                    }
                }
                MessageBox.Show($"{selectedFiles.Count} file(s) imported successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);

                // Clear list after successful import
                _fileItems.Clear();
                UpdateClearListButtonState();
                UpdateStatus("");
                ImportButton.IsEnabled = false;
                _onImportCompleted?.Invoke();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("");
            }
        }

        private void ImportTextFile(string filePath)
        {
            var title = Path.GetFileNameWithoutExtension(filePath);
            var content = File.ReadAllText(filePath);
            var tags = Path.GetDirectoryName(filePath).Split(Path.DirectorySeparatorChar).LastOrDefault() ?? "Imported";
            _noteRepository.SaveNote(title, content, tags);
        }

        private void ImportCsvFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            if (lines.Length == 0) return;

            var headers = lines[0].Split(',');
            int titleCol = -1, contentCol = -1, tagsCol = -1, modifiedCol = -1;

            for (int i = 0; i < headers.Length; i++)
            {
                var header = headers[i].Trim();
                if (header.Equals("Title", StringComparison.OrdinalIgnoreCase)) titleCol = i;
                else if (header.Equals("Content", StringComparison.OrdinalIgnoreCase)) contentCol = i;
                else if (header.Equals("Tags", StringComparison.OrdinalIgnoreCase)) tagsCol = i;
                else if (header.Equals("Modified", StringComparison.OrdinalIgnoreCase)) modifiedCol = i;
            }

            if (titleCol == -1 || contentCol == -1)
            {
                MessageBox.Show($"CSV file '{Path.GetFileName(filePath)}' is missing required columns 'Title' and 'Content'.", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                var fields = lines[i].Split(',');
                var title = titleCol < fields.Length ? fields[titleCol].Trim('\"') : "";
                var content = contentCol < fields.Length ? fields[contentCol].Trim('\"') : "";
                var tags = tagsCol < fields.Length && !string.IsNullOrEmpty(fields[tagsCol]) ? fields[tagsCol].Trim('\"') : "Imported";
                var modified = modifiedCol < fields.Length && !string.IsNullOrEmpty(fields[modifiedCol])
                    ? fields[modifiedCol].Trim('\"')
                    : DateTime.Now.ToString("yyyy-MM-dd HH:mm");

                if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(content))
                {
                    _noteRepository.SaveNote(title, content, tags);
                }
            }
        }

        // === CANCELLATION & STATUS ===

        private void UpdateClearListButtonState()
        {
            ClearListButton.IsEnabled = _fileItems.Count > 0;
        }
        private void UpdateStatus(string message, bool isScanning = false)
        {
            if (isScanning)
            {
                StatusText.Text = message;
                ScanProgressBar.IsIndeterminate = true;
                ScanProgressBar.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Text = message;
                ScanProgressBar.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ClearListButton_Click(object sender, RoutedEventArgs e)
        {
            _fileItems.Clear();
            UpdateStatus(""); // Hides progress + status
            ImportButton.IsEnabled = false;
            UpdateClearListButtonState();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _scanCancellationTokenSource?.Cancel();
            UpdateStatus("Operation canceled.");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // === DATA MODEL ===
        public class ImportFileItem : INotifyPropertyChanged
        {
            private bool _isSelected;
            public string FullPath { get; set; }
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
    }
}