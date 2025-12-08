using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Protes.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
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
            if (_easterEggActivated)
            {
                var catWindow = new CatWindow();
                catWindow.Owner = this;
                catWindow.Show();
            }
        }
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}