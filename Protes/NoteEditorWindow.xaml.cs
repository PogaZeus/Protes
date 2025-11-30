using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.VisualBasic;

namespace Protes.Views
{
    public partial class NoteEditorWindow : Window
    {
        private readonly Action<string, string, string, long?> _onSaveRequested;
        private readonly long? _noteId; // null = new note, non-null = existing
        private string _originalTitle; 
        private string _originalContent; 
        private string _originalTags;         
        private double _originalFontSize = 13.0;
        private double _currentZoomLevel = 1.0;
        private int _lastSearchIndex = -1;
        private string _lastSearchTerm = "";

        public string NoteTitle { get; private set; }
        public string NoteContent { get; private set; }
        public string NoteTags { get; private set; }

        public NoteEditorWindow(
            string title = "",
            string content = "",
            string tags = "",
            long? noteId = null,
            Action<string, string, string, long?> onSaveRequested = null)
        {
            InitializeComponent();

            PreviewKeyDown += NoteEditorWindow_PreviewKeyDown;

            // Display tag bar or not
            bool displayTags = Properties.Settings.Default.DisplayTags;
            TagsRow.Visibility = displayTags ? Visibility.Visible : Visibility.Collapsed;
            DisplayTagsMenuItem.IsChecked = displayTags;

            // Display title bar or not
            bool displayTitle = Properties.Settings.Default.DisplayTitle;
            TitleBar.Visibility = displayTitle ? Visibility.Visible : Visibility.Collapsed;
            DisplayTitleMenuItem.IsChecked = displayTitle;

            _originalTitle = title ?? "";
            _originalContent = content ?? "";
            _originalTags = tags ?? "";

            // Set up real-time updates (Ln, Col)
            ContentBox.SelectionChanged += (s, e) => UpdateCursorPosition();
            ContentBox.TextChanged += (s, e) =>
            {
                UpdateCursorPosition();
                UpdateFileFormatInfo();
            };

            UpdateCursorPosition();
            UpdateFileFormatInfo();

            // Rest of initialization
            _onSaveRequested = onSaveRequested;
            _noteId = noteId;
            _originalFontSize = ContentBox.FontSize;
            UpdateZoomDisplay();
            TitleBox.Text = title;
            ContentBox.Text = content;
            TagsBox.Text = tags;

            CommandManager.AddPreviewCanExecuteHandler(this, OnPreviewCanExecute);
            CommandManager.AddPreviewExecutedHandler(this, OnPreviewExecuted);
        }

        // ===== File Menu Shortcut Keydown Detection =====
        private void NoteEditorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                NewNoteMenuItem_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SaveButton_Click(this, new RoutedEventArgs());
                e.Handled = true; // ← prevents "s" from appearing in text
            }
        }

        // ===== NEW NOTE (from menu) =====
        private void NewNoteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var newNoteWindow = new NoteEditorWindow(onSaveRequested: _onSaveRequested);
            newNoteWindow.Owner = this.Owner;
            newNoteWindow.Show();
        }

        // ===== SAVE =====
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var title = TitleBox.Text;
            var content = ContentBox.Text;
            var tags = TagsBox.Text;

            if (_onSaveRequested != null)
            {
                _onSaveRequested(title, content, tags, _noteId);

                // Reset change tracking after save (for close window prompt)
                _originalTitle = title;
                _originalContent = content;
                _originalTags = tags;
            }
            else
            {
                NoteTitle = title;
                NoteContent = content;
                NoteTags = tags;
                DialogResult = true;
            }
        }

        // ===== SAVE AS =====
        private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string newTitle = Interaction.InputBox(
                "Enter a title for the new note:",
                "Save As",
                TitleBox.Text
            );

            if (string.IsNullOrWhiteSpace(newTitle)) return;
            newTitle = newTitle.Trim();
            if (newTitle == "")
            {
                MessageBox.Show("Title cannot be empty.", "Invalid Title", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Step 1: Save the copy immediately
            _onSaveRequested?.Invoke(newTitle, ContentBox.Text, TagsBox.Text, null);

            // Step 2: Ask MainWindow to open editor for the new note
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.OpenEditorForNewlySavedNote(newTitle, ContentBox.Text, TagsBox.Text);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "Do you want to save changes to this note?",
                    "Protes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    // Save and only close if save succeeds
                    SaveButton_Click(this, new RoutedEventArgs());
                    // Note: Save is synchronous, so we assume it worked
                    // (Your DB save shows errors but doesn't throw)
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // Cancel closing
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }
        private bool HasUnsavedChanges()
        {
            return TitleBox.Text != _originalTitle ||
                   ContentBox.Text != _originalContent ||
                   TagsBox.Text != _originalTags;
        }

        private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CancelButton_Click(sender, e);
        }

        // ===== APPLICATION COMMANDS =====
        private void OnPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Cut ||
                e.Command == ApplicationCommands.Copy ||
                e.Command == ApplicationCommands.Paste ||
                e.Command == ApplicationCommands.Delete ||
                e.Command == ApplicationCommands.SelectAll)
            {
                e.CanExecute = ContentBox.IsKeyboardFocused || ContentBox.IsFocused;
                e.Handled = true;
            }
        }

        private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Delete && ContentBox.SelectionLength > 0)
            {
                ContentBox.SelectedText = "";
                e.Handled = true;
            }
        }

        // ===== EDIT =====
        private void InsertDateTimeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ContentBox.Focus();
            int caretIndex = ContentBox.CaretIndex;
            string timestamp = DateTime.Now.ToString();
            ContentBox.Text = ContentBox.Text.Insert(caretIndex, timestamp);
            ContentBox.CaretIndex = caretIndex + timestamp.Length;
        }

        private void FindMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FindDialog { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                _lastSearchTerm = dialog.SearchText;
                _lastSearchIndex = -1;
                FindNext();
            }
        }

        private void FindNextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSearchTerm))
            {
                FindMenuItem_Click(sender, e);
                return;
            }

            FindNext();
        }

        private void FindNext()
        {
            if (string.IsNullOrEmpty(_lastSearchTerm)) return;

            int startIndex = ContentBox.SelectionStart + ContentBox.SelectionLength;

            int index = ContentBox.Text.IndexOf(_lastSearchTerm, startIndex, StringComparison.CurrentCultureIgnoreCase);

            if (index == -1)
            {
                MessageBox.Show("No more matches found.");
                return;
            }

            SelectFoundIndex(index);
        }

        private void FindPreviousMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSearchTerm))
            {
                FindMenuItem_Click(sender, e);
                return;
            }

            FindPrevious();
        }

        private void FindPrevious()
        {
            int start = ContentBox.SelectionStart - 1;
            if (start < 0) start = ContentBox.Text.Length - 1;

            int index = ContentBox.Text.LastIndexOf(_lastSearchTerm, start, StringComparison.CurrentCultureIgnoreCase);

            if (index == -1)
            {
                MessageBox.Show("No previous matches found.");
                return;
            }

            SelectFoundIndex(index);
        }

        private void SelectFoundIndex(int index)
        {
            ContentBox.Focus();
            ContentBox.Select(index, _lastSearchTerm.Length);
            ContentBox.ScrollToLine(ContentBox.GetLineIndexFromCharacterIndex(index));
            _lastSearchIndex = index;
        }

        private void ReplaceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ReplaceDialog { Owner = this };

            if (dialog.ShowDialog() != true)
                return;

            string search = dialog.SearchText;
            string replace = dialog.ReplaceText;

            if (dialog.ReplaceAllRequested)
            {
                // FIXED — works on .NET Framework & Core
                ContentBox.Text = ReplaceIgnoreCase(ContentBox.Text, search, replace);
                return;
            }

            // Replace single
            if (ContentBox.SelectedText.Equals(search, StringComparison.CurrentCultureIgnoreCase))
            {
                ContentBox.SelectedText = replace;
            }
            else
            {
                _lastSearchTerm = search;
                FindNext();
                if (ContentBox.SelectedText.Equals(search, StringComparison.CurrentCultureIgnoreCase))
                    ContentBox.SelectedText = replace;
            }
        }

        private string ReplaceIgnoreCase(string text, string search, string replace)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                text,
                System.Text.RegularExpressions.Regex.Escape(search),
                replace,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }



        private void GoToMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter line number:", "Go To");

            if (int.TryParse(input, out int line))
            {
                line = Math.Max(1, Math.Min(line, ContentBox.LineCount));
                int index = ContentBox.GetCharacterIndexFromLineIndex(line - 1);
                ContentBox.Select(index, 0);
                ContentBox.ScrollToLine(line - 1);
            }
        }

        // ===== FORMAT =====
        private void FontMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentFont = ContentBox.FontFamily ?? System.Windows.SystemFonts.MessageFontFamily;
                var currentSize = double.IsNaN(ContentBox.FontSize) ? 12.0 : ContentBox.FontSize;

                var fontPicker = new FontPickerWindow(currentFont, currentSize) { Owner = this };

                if (fontPicker.ShowDialog() == true)
                {
                    ContentBox.FontFamily = fontPicker.SelectedFontFamily;
                    ContentBox.FontSize = fontPicker.SelectedFontSize;
                    _originalFontSize = ContentBox.FontSize;
                    _currentZoomLevel = 1.0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Font picker error:\n{ex.Message}", "Font Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== VIEW / ZOOM =====
        private void ZoomInMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _currentZoomLevel += 0.25;
            ApplyZoom();
        }

        private void ZoomOutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _currentZoomLevel = Math.Max(0.25, _currentZoomLevel - 0.25);
            ApplyZoom();
        }

        private void RestoreZoomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _currentZoomLevel = 1.0;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            ContentBox.FontSize = _originalFontSize * _currentZoomLevel;
            UpdateZoomDisplay(); // 👈 add this
        }

        private void UpdateZoomDisplay()
        {
            int percent = (int)(_currentZoomLevel * 100);
            ZoomText.Text = $"Zoom: {percent}%";
        }

        // ===== TITLE BAR =====
        private void DisplayTitleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                bool isVisible = menuItem.IsChecked;
                TitleBar.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

                // Save globally
                Properties.Settings.Default.DisplayTitle = isVisible;
                Properties.Settings.Default.Save(); // Persist to disk
            }
        }

        // ===== TAGS BAR =====
        private void DisplayTagsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                bool isVisible = menuItem.IsChecked;
                TagsRow.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

                // Save globally
                Properties.Settings.Default.DisplayTags = isVisible;
                Properties.Settings.Default.Save(); // Persist to disk
            }
        }

        // ===== STATUS BAR =====
        private void StatusBarMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                MainStatusBar.Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateCursorPosition()
        {
            if (ContentBox.Text.Length == 0)
            {
                LnColText.Text = "Ln 1, Col 1";
                return;
            }

            int caretIndex = ContentBox.CaretIndex;
            string text = ContentBox.Text;

            // Count lines up to caret
            int line = 1;
            int col = 1;
            for (int i = 0; i < caretIndex; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    col = 1;
                }
                else
                {
                    col++;
                }
            }

            LnColText.Text = $"Ln {line}, Col {col}";
        }

        private void UpdateFileFormatInfo()
        {
            // Encoding: assume UTF-8 (standard for exported files)
            EncodingText.Text = "UTF-8";

            // Line endings
            string text = ContentBox.Text;
            if (text.Contains("\r\n"))
                LineEndingText.Text = "CRLF";
            else if (text.Contains("\n"))
                LineEndingText.Text = "LF";
            else
                LineEndingText.Text = "None";
        }

        // ===== INFO =====
        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow { Owner = this };
            about.ShowDialog();
        }

        // ===== EXPORT =====
        private void ExportAsTxt_Click(object sender, RoutedEventArgs e) => ExportToFile("Text Files (*.txt)|*.txt", ".txt", c => c);
        private void ExportAsMd_Click(object sender, RoutedEventArgs e) => ExportToFile("Markdown Files (*.md)|*.md", ".md", c => c);
        private void ExportAsCsv_Click(object sender, RoutedEventArgs e) =>
            ExportToFile("CSV Files (*.csv)|*.csv", ".csv", content =>
            {
                string title = EscapeCsvField(TitleBox.Text);
                string escapedContent = EscapeCsvField(content);
                string tags = EscapeCsvField(TagsBox.Text);
                return $"\"{title}\",\"{escapedContent}\",\"{tags}\"";
            });

        private string EscapeCsvField(string input) => string.IsNullOrEmpty(input) ? string.Empty : input.Replace("\"", "\"\"");

        private void ExportToFile(string filter, string defaultExt, Func<string, string> formatContent)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = SanitizeFileName(TitleBox.Text),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, formatContent(ContentBox.Text));
                    MessageBox.Show($"Note exported successfully to:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export note:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "untitled";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "_");
            return name.TrimEnd('.');
        }
    }
}