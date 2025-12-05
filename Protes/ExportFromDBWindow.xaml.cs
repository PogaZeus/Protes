using Protes;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Protes.Views
{
    public partial class ExportFromDBWindow : Window, INotifyPropertyChanged
    {
        private readonly List<FullNote> _notes;
        private readonly ObservableCollection<ExportNoteItem> _exportItems = new ObservableCollection<ExportNoteItem>();
        private bool _isBulkUpdating = false;
        private string _selectedFolder = null;
        private readonly string _databasePath;
        private readonly DatabaseMode _databaseMode;
        public ExportFromDBWindow(List<FullNote> notes, string databasePath, DatabaseMode databaseMode)
        {
            InitializeComponent();
            _notes = notes ?? new List<FullNote>();
            _databasePath = databasePath;
            _databaseMode = databaseMode;
            DataContext = this;

            // Populate grid
            foreach (var note in _notes)
            {
                _exportItems.Add(new ExportNoteItem
                {
                    Id = note.Id,
                    Title = note.Title,
                    LastModified = note.LastModified,
                    IsSelected = false
                });
            }
            ExportDataGrid.ItemsSource = _exportItems;

            // Set database info
            UpdateDatabaseInfo();

            // Initialize UI
            ExportFormatComboBox.SelectedIndex = 0;
            UpdateButtonState();

            // Hook up row checkbox events
            ExportDataGrid.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(OnRowCheckboxChanged));
            ExportDataGrid.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(OnRowCheckboxChanged));
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

        // ===== PROPERTIES =====
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
                    _isBulkUpdating = true;
                    try
                    {
                        foreach (var item in _exportItems)
                            item.IsSelected = value;
                    }
                    finally
                    {
                        _isBulkUpdating = false;
                    }
                    UpdateButtonState();
                }
            }
        }

        // ===== EVENT HANDLERS =====
        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select output folder for exported files:",
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _selectedFolder = folderDialog.SelectedPath;
                SelectedFolderText.Text = $"Selected folder: {_selectedFolder}";
                UpdateButtonState();
            }
        }

        private void OnRowCheckboxChanged(object sender, RoutedEventArgs e)
        {
            // This line ensures the compiler sees a READ of _isBulkUpdating
            var _ = _isBulkUpdating;
            if (_isBulkUpdating) return;

            bool allChecked = _exportItems.All(item => item.IsSelected);
            AllItemsAreChecked = allChecked;
            UpdateButtonState();
        }

        private void ExportFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Prevent null reference during initialization
            if (CsvFilenamePanel == null || InfoNoteText == null)
                return;

            var selected = ExportFormatComboBox.SelectedIndex;
            CsvFilenamePanel.Visibility = (selected == 0) ? Visibility.Visible : Visibility.Collapsed;

            if (selected == 0) // CSV
                InfoNoteText.Text = "This will export the selected entries into 1 CSV file.\nDelimiter: Comma | Text Delimiter: Double quotes | Character set: Unicode (UTF-8)";
            else if (selected == 1) // TXT
                InfoNoteText.Text = "Creates *.txt files - created with 'title' as the filename";
            else // MD
                InfoNoteText.Text = "Creates *.md files - created with 'title' as the filename";

            UpdateButtonState();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = _exportItems.Where(i => i.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("Please select at least one note to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_selectedFolder))
            {
                MessageBox.Show("Please select an output folder.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var formatIndex = ExportFormatComboBox.SelectedIndex;
                if (formatIndex == 0) // CSV
                {
                    ExportAsCsv(selectedItems);
                }
                else // TXT or MD
                {
                    ExportAsTextFiles(selectedItems, formatIndex == 2); // true = .md, false = .txt
                }
                MessageBox.Show($"{selectedItems.Count} note{(selectedItems.Count == 1 ? "" : "s")} exported successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ===== EXPORT LOGIC =====
        // ===== EXPORT LOGIC =====
        private void ExportAsCsv(List<ExportNoteItem> items)
        {
            var filename = CsvFilenameTextBox.Text.TrimEnd('.', 'c', 's', 'v') + ".csv";
            var fullPath = Path.Combine(_selectedFolder, filename);

            var csv = new StringBuilder();
            csv.AppendLine("Title,Content,Tags,LastModified");

            foreach (var item in items)
            {
                var note = _notes.First(n => n.Id == item.Id);
                csv.AppendLine($"{EscapeCsv(note.Title)},{EscapeCsv(note.Content)},{EscapeCsv(note.Tags)},{EscapeCsv(note.LastModified)}");
            }

            File.WriteAllText(fullPath, csv.ToString());
        }

        private void ExportAsTextFiles(List<ExportNoteItem> items, bool isMarkdown)
        {
            var ext = isMarkdown ? ".md" : ".txt";
            foreach (var item in items)
            {
                var note = _notes.First(n => n.Id == item.Id);
                var safeTitle = string.Join("_", note.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim('_');
                var filename = Path.Combine(_selectedFolder, $"{safeTitle}{ext}");
                File.WriteAllText(filename, note.Content);
            }
        }

        // ===== HELPERS =====
        private string EscapeCsv(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            if (input.Contains("\"")) input = input.Replace("\"", "\"\"");
            if (input.Contains(",") || input.Contains("\n") || input.Contains("\r")) input = $"\"{input}\"";
            return input;
        }

        private void UpdateButtonState()
        {
            bool hasSelectedItems = _exportItems.Any(i => i.IsSelected);
            bool hasFolderSelected = !string.IsNullOrEmpty(_selectedFolder);

            ExportButton.IsEnabled = hasSelectedItems && hasFolderSelected;
        }

        // ===== DATA MODEL =====
        public class ExportNoteItem : INotifyPropertyChanged
        {
            public long Id { get; set; }
            public string Title { get; set; }
            public string LastModified { get; set; }
            private bool _isSelected;
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}