using System;
using System.Windows;
using Protes;

namespace Protes
{
    public partial class CatWindow : Window
    {
        private readonly SettingsManager _settings;
        private readonly Action _onSettingsChanged;

        // Primary constructor: used when settings + live update are available
        public CatWindow(SettingsManager settings, Action onSettingsChanged = null)
        {
            _settings = settings;
            _onSettingsChanged = onSettingsChanged;
            InitializeComponent();
            ShowCatButtonInToolbarCheckBox.IsChecked = _settings.ViewToolbarCat;
            ShowCatButtonInToolbarCheckBox.Checked += OnToolbarSettingChanged;
            ShowCatButtonInToolbarCheckBox.Unchecked += OnToolbarSettingChanged;
        }

        // Fallback constructor: used when opened without context (e.g., from NoteEditor)
        public CatWindow()
        {
            _settings = new SettingsManager(); // local instance
            _onSettingsChanged = null;
            InitializeComponent();
            ShowCatButtonInToolbarCheckBox.IsChecked = _settings.ViewToolbarCat;
            ShowCatButtonInToolbarCheckBox.Checked += OnToolbarSettingChanged;
            ShowCatButtonInToolbarCheckBox.Unchecked += OnToolbarSettingChanged;
        }

        private void OnToolbarSettingChanged(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarCat = ShowCatButtonInToolbarCheckBox.IsChecked == true;
            _settings.Save(); // Persist to disk
            _onSettingsChanged?.Invoke(); 
        }

        private void RevealRiddle1(object sender, RoutedEventArgs e)
        {
            Riddle1Answer.Visibility = Visibility.Visible;
        }

        private void RevealRiddle2(object sender, RoutedEventArgs e)
        {
            Riddle2Answer.Visibility = Visibility.Visible;
        }

        private void RevealRiddle3(object sender, RoutedEventArgs e)
        {
            Riddle3Answer.Visibility = Visibility.Visible;
        }

        private void MeowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}