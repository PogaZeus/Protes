using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        public ImportToDBWindow(string databasePath, DatabaseMode databaseMode, string externalConnectionString, INoteRepository noteRepository)
        {
            InitializeComponent();
            _databasePath = databasePath;
            _databaseMode = databaseMode;
            _externalConnectionString = externalConnectionString;
            _noteRepository = noteRepository;

            FileListDataGrid.ItemsSource = _fileItems;
            UpdateDatabaseInfo();

            // Enable Import button if any item is selected
            _fileItems.CollectionChanged += (s, e) => UpdateImportButtonState();
            FileListDataGrid.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(OnItemChecked));
            FileListDataGrid.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(OnItemChecked));
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
            }
        }

        private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string rootFolder = dialog.SelectedPath;
                UpdateStatus("Scanning folder...", isScanning: true);

                try
                {
                    var validFiles = await Task.Run(() =>
                    {
                        var files = new List<string>();
                        ScanFolderRecursively(rootFolder, files);
                        return files;
                    });

                    // Filter by extension
                    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".csv" };
                    var filteredFiles = validFiles.Where(f => allowedExtensions.Contains(Path.GetExtension(f))).ToList();

                    // Add to list (avoid duplicates)
                    foreach (var file in filteredFiles)
                    {
                        if (!_fileItems.Any(f => f.FullPath == file))
                        {
                            _fileItems.Add(new ImportFileItem { FullPath = file });
                        }
                    }

                    if (filteredFiles.Count == 0)
                    {
                        UpdateStatus("No .txt, .md, or .csv files found.");
                        MessageBox.Show("No valid files found.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        UpdateStatus($"{filteredFiles.Count} file(s) added.");
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus("Scan failed.");
                    MessageBox.Show($"Scan failed:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void ScanFolderRecursively(string folderPath, List<string> fileList)
        {
            try
            {
                // Get files in current folder
                var files = Directory.GetFiles(folderPath);
                fileList.AddRange(files);

                // Get subdirectories
                var subDirs = Directory.GetDirectories(folderPath);
                foreach (var subDir in subDirs)
                {
                    ScanFolderRecursively(subDir, fileList); // Recurse
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip this folder and continue
                return;
            }
            catch (DirectoryNotFoundException)
            {
                // Folder was deleted during scan – skip
                return;
            }
            catch (IOException)
            {
                // Device not ready, etc. – skip
                return;
            }
        }

        private void UpdateStatus(string message, bool isScanning = false)
        {
            StatusText.Text = message;
            ScanProgressBar.Visibility = isScanning ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HeaderCheck_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var item in _fileItems)
                item.IsSelected = true;
            UpdateImportButtonState();
        }

        private void HeaderCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var item in _fileItems)
                item.IsSelected = false;
            UpdateImportButtonState();
        }

        private void OnItemChecked(object sender, RoutedEventArgs e)
        {
            var allChecked = _fileItems.All(f => f.IsSelected);
            var anyChecked = _fileItems.Any(f => f.IsSelected);
            var headerCheck = FileListDataGrid.Columns[0].Header as CheckBox;

            if (headerCheck != null)
            {
                headerCheck.IsChecked = allChecked;
                headerCheck.IsThreeState = anyChecked && !allChecked;
            }
            UpdateImportButtonState();
        }

        private void UpdateImportButtonState()
        {
            ImportButton.IsEnabled = _fileItems.Any(f => f.IsSelected);
        }

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
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n{ex.Message}", "Protes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportTextFile(string filePath)
        {
            var title = Path.GetFileNameWithoutExtension(filePath);
            var content = File.ReadAllText(filePath);
            var tags = Path.GetDirectoryName(filePath).Split(Path.DirectorySeparatorChar).LastOrDefault() ?? "Imported";
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class ImportFileItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string FullPath { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}