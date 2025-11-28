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
        private double _originalFontSize = 13.0;
        private double _currentZoomLevel = 1.0;

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
            _onSaveRequested = onSaveRequested;
            _noteId = noteId;
            _originalFontSize = ContentBox.FontSize;
            TitleBox.Text = title;
            ContentBox.Text = content;
            TagsBox.Text = tags;

            CommandManager.AddPreviewCanExecuteHandler(this, OnPreviewCanExecute);
            CommandManager.AddPreviewExecutedHandler(this, OnPreviewExecuted);
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
                // Pass note ID (null = new, non-null = update)
                _onSaveRequested(title, content, tags, _noteId);
            }
            else
            {
                // Fallback for non-callback usage (e.g., standalone export)
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

        private void FindMenuItem_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show("Find feature not yet implemented.", "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);

        private void FindNextMenuItem_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show("Find Next not yet implemented.", "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);

        private void FindPreviousMenuItem_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show("Find Previous not yet implemented.", "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);

        private void ReplaceMenuItem_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show("Replace not yet implemented.", "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);

        private void GoToMenuItem_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show("Go To not yet implemented.", "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);

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
        }

        // ===== STATUS BAR =====
        private void StatusBarMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            bool isVisible = menuItem?.IsChecked == true;
            MessageBox.Show($"Status bar is now {(isVisible ? "enabled" : "disabled")}.", "Status Bar", MessageBoxButton.OK, MessageBoxImage.Information);
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