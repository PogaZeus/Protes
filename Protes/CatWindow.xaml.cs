using System;
using System.Windows;
using Protes;

namespace Protes
{
    public partial class CatWindow : Window
    {
        private readonly SettingsManager _settings = new SettingsManager();
        private readonly Action _onSettingsChanged;

        public CatWindow(Action onSettingsChanged = null)
        {
            InitializeComponent();
            _onSettingsChanged = onSettingsChanged;
            ShowCatButtonInToolbarCheckBox.IsChecked = _settings.ViewToolbarCat;
            ShowCatButtonInToolbarCheckBox.Checked += OnToolbarSettingChanged;
            ShowCatButtonInToolbarCheckBox.Unchecked += OnToolbarSettingChanged;
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
        private void OnToolbarSettingChanged(object sender, RoutedEventArgs e)
        {
            _settings.ViewToolbarCat = ShowCatButtonInToolbarCheckBox.IsChecked == true;
            _onSettingsChanged?.Invoke(); // Notify MainWindow to refresh
        }

        private void MeowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}