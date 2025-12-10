using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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

        public ImportToDBWindow(
            string databasePath,
            DatabaseMode databaseMode,
            string externalConnectionString,
            INoteRepository noteRepository,
            Action onImportCompleted,
            string preselectedFilePath = null) // ✅ Optional parameter
        {
            InitializeComponent();
            DataContext = this;

            _databasePath = databasePath;
            _databaseMode = databaseMode;
            _externalConnectionString = externalConnectionString;
            _noteRepository = noteRepository;
            _onImportCompleted = onImportCompleted;

            FileListDataGrid.ItemsSource = _fileItems;
            UpdateClearListButtonState();
            UpdateDatabaseInfo();

            // Enable Import button if any item is selected
            _fileItems.CollectionChanged += (s, e) => UpdateImportButtonState();
            FileListDataGrid.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(OnItemChecked));
            FileListDataGrid.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(OnItemChecked));

            // ✅ Handle preselected file (e.g., from "Send to")
            if (!string.IsNullOrEmpty(preselectedFilePath) && File.Exists(preselectedFilePath))
            {
                AddFileToImportList(preselectedFilePath);
            }
        }

        // ✅ Helper to add a single file (used by constructor and future "Add Files" logic)
        private void AddFileToImportList(string filePath)
        {
            if (!_fileItems.Any(f => f.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            {
                var fileInfo = new FileInfo(filePath);
                _fileItems.Add(new ImportFileItem
                {
                    FullPath = filePath,
                    FileName = fileInfo.Name,
                    FileSize = $"{fileInfo.Length / 1024} KB",
                    IsSelected = true // Auto-select since user explicitly sent it
                });
            }
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
                    AddFileToImportList(file);
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

            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource = new CancellationTokenSource();

            string rootFolder = dialog.SelectedPath;
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

                var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".csv" };
                var validFiles = scannedFiles.Where(f => allowedExtensions.Contains(Path.GetExtension(f))).ToList();

                foreach (var file in validFiles)
                {
                    AddFileToImportList(file);
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

        // === HEADER CHECKBOX ===
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

                    _isBulkUpdating = true;
                    try
                    {
                        foreach (var item in _fileItems)
                        {
                            item.IsSelected = value;
                        }
                    }
                    finally
                    {
                        _isBulkUpdating = false;
                    }
                    UpdateImportButtonState();
                }
            }
        }

        private void OnItemChecked(object sender, RoutedEventArgs e)
        {
            if (_isBulkUpdating) return;
            var allChecked = _fileItems.All(f => f.IsSelected);
            AllItemsAreChecked = allChecked;
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

            int successCount = 0;
            int failCount = 0;
            var errors = new List<string>();

            foreach (var fileItem in selectedFiles)
            {
                try
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
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    errors.Add($"{Path.GetFileName(fileItem.FullPath)}: {ex.Message}");
                }
            }

            if (failCount == 0)
            {
                MessageBox.Show($"{successCount} file(s) imported successfully.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                _fileItems.Clear();
                UpdateClearListButtonState();
                UpdateStatus("");
                ImportButton.IsEnabled = false;
                _onImportCompleted?.Invoke();
                Close();
            }
            else if (successCount == 0)
            {
                MessageBox.Show($"Import failed for all {failCount} file(s):\n\n" + string.Join("\n", errors),
                    "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("");
            }
            else
            {
                MessageBox.Show($"Partial import: {successCount} succeeded, {failCount} failed.\n\nErrors:\n" + string.Join("\n", errors),
                    "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);

                var failedPaths = errors.Select(err => err.Split(':')[0]).ToList();
                var itemsToRemove = _fileItems.Where(f => !failedPaths.Contains(Path.GetFileName(f.FullPath))).ToList();
                foreach (var item in itemsToRemove)
                {
                    _fileItems.Remove(item);
                }

                UpdateClearListButtonState();
                UpdateStatus("");
                _onImportCompleted?.Invoke();
            }
        }

        private void ImportTextFile(string filePath)
        {
            var title = Path.GetFileNameWithoutExtension(filePath);
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            var tags = Path.GetDirectoryName(filePath).Split(Path.DirectorySeparatorChar).LastOrDefault() ?? "Imported";
            _noteRepository.SaveNote(title, content, tags);
        }

        private void ImportCsvFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            if (lines.Length == 0) return;

            var headers = ParseCsvLine(lines[0]);
            int titleCol = -1, contentCol = -1, tagsCol = -1, modifiedCol = -1;

            for (int i = 0; i < headers.Count; i++)
            {
                var header = headers[i].Trim();
                if (header.Equals("Title", StringComparison.OrdinalIgnoreCase)) titleCol = i;
                else if (header.Equals("Content", StringComparison.OrdinalIgnoreCase)) contentCol = i;
                else if (header.Equals("Tags", StringComparison.OrdinalIgnoreCase)) tagsCol = i;
                else if (header.Equals("LastModified", StringComparison.OrdinalIgnoreCase)) modifiedCol = i;
            }

            if (titleCol == -1 || contentCol == -1)
            {
                throw new InvalidOperationException($"CSV file '{Path.GetFileName(filePath)}' is missing required columns 'Title' and 'Content'.");
            }

            string fullText = File.ReadAllText(filePath, Encoding.UTF8);
            var records = ParseCsvFile(fullText);

            for (int i = 1; i < records.Count; i++)
            {
                var fields = records[i];
                var title = titleCol < fields.Count ? fields[titleCol] : "";
                var content = contentCol < fields.Count ? fields[contentCol] : "";
                var tags = tagsCol < fields.Count && !string.IsNullOrEmpty(fields[tagsCol]) ? fields[tagsCol] : "Imported";
                var modified = modifiedCol < fields.Count && !string.IsNullOrEmpty(fields[modifiedCol])
                    ? fields[modifiedCol]
                    : DateTime.Now.ToString("yyyy-MM-dd HH:mm");

                if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(content))
                {
                    _noteRepository.SaveNote(title, content, tags);
                }
            }
        }

        private List<List<string>> ParseCsvFile(string csvContent)
        {
            var records = new List<List<string>>();
            var currentRecord = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < csvContent.Length; i++)
            {
                char c = csvContent[i];
                char? nextChar = (i + 1 < csvContent.Length) ? csvContent[i + 1] : (char?)null;

                if (inQuotes)
                {
                    if (c == '"' && nextChar == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        currentRecord.Add(currentField.ToString());
                        currentField.Clear();
                    }
                    else if (c == '\r' || c == '\n')
                    {
                        if (currentField.Length > 0 || currentRecord.Count > 0)
                        {
                            currentRecord.Add(currentField.ToString());
                            records.Add(currentRecord);
                            currentRecord = new List<string>();
                            currentField.Clear();
                        }
                        if (c == '\r' && nextChar == '\n')
                            i++;
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
            }

            if (currentField.Length > 0 || currentRecord.Count > 0)
            {
                currentRecord.Add(currentField.ToString());
                records.Add(currentRecord);
            }

            return records;
        }

        private List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                char? nextChar = (i + 1 < line.Length) ? line[i + 1] : (char?)null;

                if (inQuotes)
                {
                    if (c == '"' && nextChar == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(currentField.ToString());
                        currentField.Clear();
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
            }

            fields.Add(currentField.ToString());
            return fields;
        }

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
            UpdateStatus("");
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
            public string FileName { get; set; } // ✅ Added for UI display
            public string FileSize { get; set; } // ✅ Added for UI display

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