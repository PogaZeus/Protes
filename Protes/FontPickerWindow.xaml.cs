using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Protes.Views
{
    public partial class FontPickerWindow : Window, INotifyPropertyChanged
    {
        private FontFamily _selectedFontFamily;
        private double _selectedFontSize;

        public event PropertyChangedEventHandler PropertyChanged;

        public List<FontFamily> FontFamilies { get; }
        public List<double> FontSizes { get; }

        public FontFamily SelectedFontFamily
        {
            get => _selectedFontFamily;
            set
            {
                _selectedFontFamily = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedFontFamily)));
            }
        }

        public double SelectedFontSize
        {
            get => _selectedFontSize;
            set
            {
                _selectedFontSize = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedFontSize)));
            }
        }

        public FontPickerWindow(FontFamily currentFont, double currentSize)
        {
            InitializeComponent();

            FontFamilies = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
            FontSizes = new List<double> { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 72 };

            SelectedFontFamily = FontFamilies.FirstOrDefault(f => f.Source == currentFont?.Source) ?? FontFamilies[0];
            SelectedFontSize = FontSizes.Contains(currentSize) ? currentSize : 12.0;

            DataContext = this;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}