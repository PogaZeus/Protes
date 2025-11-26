using System.Windows;

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
            DialogResult = true; // indicates "OK"
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // indicates "Cancel"
            Close();
        }
    }
}