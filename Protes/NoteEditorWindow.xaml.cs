using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Protes.Views
{
    public partial class NoteEditorWindow : Window
    {
        public string NoteTitle { get; private set; }
        public string NoteContent { get; private set; }
        public string NoteTags { get; private set; }

        public NoteEditorWindow(string title = "", string content = "", string tags = "")
        {
            InitializeComponent();
            TitleBox.Text = title;
            ContentBox.Text = content;
            TagsBox.Text = tags;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            NoteTitle = TitleBox.Text;
            NoteContent = ContentBox.Text;
            NoteTags = TagsBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ===== EXPORT FUNCTIONALITY =====

        private void ExportAsTxt_Click(object sender, RoutedEventArgs e)
        {
            ExportToFile("Text Files (*.txt)|*.txt", ".txt", content => content);
        }

        private void ExportAsMd_Click(object sender, RoutedEventArgs e)
        {
            ExportToFile("Markdown Files (*.md)|*.md", ".md", content => content);
        }

        private void ExportAsCsv_Click(object sender, RoutedEventArgs e)
        {
            // CSV format: "Title","Content","Tags"
            ExportToFile("CSV Files (*.csv)|*.csv", ".csv", content =>
            {
                string title = EscapeCsvField(TitleBox.Text);
                string escapedContent = EscapeCsvField(content);
                string tags = EscapeCsvField(NoteTagsFromUI());
                return $"\"{title}\",\"{escapedContent}\",\"{tags}\"";
            });
        }

        private string NoteTagsFromUI() => TagsBox.Text;

        private string EscapeCsvField(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            // Escape double quotes by doubling them, per RFC 4180
            return input.Replace("\"", "\"\"");
        }

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
                    string content = formatContent(ContentBox.Text);
                    File.WriteAllText(dialog.FileName, content);
                    MessageBox.Show($"Note exported successfully to:\n{dialog.FileName}", "Export Complete",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export note:\n{ex.Message}", "Export Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Remove invalid filename characters
        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "untitled";

            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = name;
            foreach (char c in invalid)
            {
                sanitized = sanitized.Replace(c.ToString(), "_");
            }
            return sanitized.TrimEnd('.');
        }
    }
}