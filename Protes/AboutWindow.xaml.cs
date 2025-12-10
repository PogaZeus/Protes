using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Protes;

namespace Protes.Views
{
    public partial class AboutWindow : Window
    {
        private readonly SettingsManager _settings;
        private readonly Action _onSettingsChanged;

        // Primary constructor
        public AboutWindow(SettingsManager settings, Action onSettingsChanged)
        {
            _settings = settings;
            _onSettingsChanged = onSettingsChanged;
            InitializeComponent();
        }

        // Fallback constructor
        public AboutWindow()
        {
            _settings = null;
            _onSettingsChanged = null;
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private bool _easterEggActivated = false;

        private void EasterEgg_Click(object sender, RoutedEventArgs e)
        {
            if (_easterEggActivated) return;

            CreatorImage.Source = new BitmapImage(new Uri("pack://application:,,,/MrE_Clean.png"));
            CreatorImageButton.IsEnabled = true;
            _easterEggActivated = true;
            e.Handled = true;
        }

        private void CreatorImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_easterEggActivated) return;

            if (_settings != null && _onSettingsChanged != null)
            {
                var catWindow = new CatWindow(_settings, _onSettingsChanged);
                catWindow.Owner = this;
                catWindow.Show();
            }
            else
            {
                // Fallback: no live update, relies on restart
                var catWindow = new CatWindow();
                catWindow.Owner = this;
                catWindow.Show();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}