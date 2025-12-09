using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Protes.Views
{
    public partial class FontMainWindow : Window, INotifyPropertyChanged
    {
        private FontFamily _selectedFontFamily;
        private FontStyleItem _selectedFontStyle;
        private string _selectedScript;
        private string _fontInputText;
        private bool _isUserTyping = false;
        private DispatcherTimer _typeTimer;

        // Default for MainWindow: Segoe UI (standard WPF UI font)
        private static readonly string AppDefaultFontFamily = "Segoe UI";
        private static readonly FontWeight AppDefaultFontWeight = FontWeights.Normal;
        private static readonly FontStyle AppDefaultFontStyle = System.Windows.FontStyles.Normal;

        public event PropertyChangedEventHandler PropertyChanged;

        public List<FontFamily> FontFamilies { get; private set; }
        public List<FontStyleItem> FontStyles { get; set; }
        public List<string> Scripts { get; }

        public FontFamily SelectedFontFamily
        {
            get => _selectedFontFamily;
            set
            {
                _selectedFontFamily = value;
                OnPropertyChanged(nameof(SelectedFontFamily));
                UpdateFontStyles();
                if (!_isUserTyping)
                {
                    FontInputText = value?.Source ?? "";
                }
            }
        }

        public FontStyleItem SelectedFontStyle
        {
            get => _selectedFontStyle;
            set
            {
                _selectedFontStyle = value;
                OnPropertyChanged(nameof(SelectedFontStyle));
                OnPropertyChanged(nameof(SelectedFontStyleName));
                OnPropertyChanged(nameof(SelectedFontWeight));
            }
        }

        public string FontInputText
        {
            get => _fontInputText;
            set
            {
                if (_fontInputText == value) return;
                _fontInputText = value;
                OnPropertyChanged(nameof(FontInputText));
                _isUserTyping = true;
                if (!string.IsNullOrEmpty(value))
                {
                    var match = FontFamilies
                        .FirstOrDefault(f => f.Source.StartsWith(value, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        SelectedFontFamily = match;
                        FontListBox?.ScrollIntoView(match);
                    }
                }
                _typeTimer?.Stop();
                _typeTimer?.Start();
            }
        }

        public string SelectedFontStyleName => _selectedFontStyle?.Name ?? "Regular";
        public FontWeight SelectedFontWeight => _selectedFontStyle?.Weight ?? FontWeights.Normal;
        public FontStyle SelectedFontStyleEnum => _selectedFontStyle?.Style ?? System.Windows.FontStyles.Normal;

        // Fixed sample size (13 DIPs = ~9.75 pt) — consistent with UI
        public double SampleFontSize => 13.0;

        public string SelectedScript
        {
            get => _selectedScript;
            set
            {
                _selectedScript = value;
                OnPropertyChanged(nameof(SelectedScript));
            }
        }

        public bool SetAsDefault => SetAsDefaultCheckBox.IsChecked ?? false;

        private readonly SettingsManager _settings;

        public FontMainWindow()
        {
#if DEBUG
            InitializeComponent();
            FontFamilies = new List<FontFamily> { new FontFamily(AppDefaultFontFamily) };
            Scripts = new List<string> { "Western" };
            FontStyles = new List<FontStyleItem>
            {
                new FontStyleItem("Regular", AppDefaultFontWeight, AppDefaultFontStyle)
            };
            SelectedFontFamily = FontFamilies[0];
            SelectedFontStyle = FontStyles[0];
            SelectedScript = "Western";
            FontInputText = AppDefaultFontFamily;
            DataContext = this;
#endif
        }

        // ✅ CORRECT CONSTRUCTOR: Accepts current font style (no size)
        public FontMainWindow(FontFamily currentFont, FontWeight currentWeight, FontStyle currentStyle, SettingsManager settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeComponent();

            FontFamilies = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
            Scripts = new List<string>
            {
                "Western", "Arabic", "Baltic", "Central European", "Chinese Simplified",
                "Chinese Traditional", "Cyrillic", "Greek", "Hebrew", "Japanese",
                "Korean", "Thai", "Turkish", "Vietnamese"
            };

            SelectedFontFamily = FontFamilies.FirstOrDefault(f => f.Source == currentFont?.Source)
                ?? new FontFamily(_settings.DefaultMainFontFamily ?? AppDefaultFontFamily);

            SelectedScript = "Western";
            UpdateFontStyles();

            // Match current style
            SelectedFontStyle = FontStyles.FirstOrDefault(s =>
                s.Weight.Equals(currentWeight) && s.Style.Equals(currentStyle))
                ?? FontStyles.FirstOrDefault(s =>
                    s.Weight.Equals(ParseFontWeight(_settings.DefaultMainFontWeight)) &&
                    s.Style.Equals(ParseFontStyle(_settings.DefaultMainFontStyle)))
                ?? FontStyles.FirstOrDefault();

            _typeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _typeTimer.Tick += (s, e) =>
            {
                _isUserTyping = false;
                _typeTimer.Stop();
            };

            FontInputText = SelectedFontFamily?.Source ?? AppDefaultFontFamily;
            DataContext = this;
        }

        private void FontInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                FontInputText = SelectedFontFamily?.Source ?? "";
                _isUserTyping = false;
                _typeTimer?.Stop();
                e.Handled = true;
            }
        }

        private void UpdateFontStyles()
        {
            if (_selectedFontFamily == null) return;

            var styles = new List<FontStyleItem>();
            var typefaces = _selectedFontFamily.GetTypefaces();

            bool hasRegular = false;
            bool hasBold = false;
            bool hasItalic = false;
            bool hasBoldItalic = false;

            foreach (var typeface in typefaces)
            {
                var weight = typeface.Weight;
                var style = typeface.Style;

                if (!hasRegular && weight == FontWeights.Normal && style == System.Windows.FontStyles.Normal)
                {
                    styles.Add(new FontStyleItem("Regular", FontWeights.Normal, System.Windows.FontStyles.Normal));
                    hasRegular = true;
                }
                else if (!hasBold && weight == FontWeights.Bold && style == System.Windows.FontStyles.Normal)
                {
                    styles.Add(new FontStyleItem("Bold", FontWeights.Bold, System.Windows.FontStyles.Normal));
                    hasBold = true;
                }
                else if (!hasItalic && weight == FontWeights.Normal && style == System.Windows.FontStyles.Italic)
                {
                    styles.Add(new FontStyleItem("Italic", FontWeights.Normal, System.Windows.FontStyles.Italic));
                    hasItalic = true;
                }
                else if (!hasBoldItalic && weight == FontWeights.Bold && style == System.Windows.FontStyles.Italic)
                {
                    styles.Add(new FontStyleItem("Bold Italic", FontWeights.Bold, System.Windows.FontStyles.Italic));
                    hasBoldItalic = true;
                }
            }

            if (styles.Count == 0)
            {
                styles.Add(new FontStyleItem("Regular", FontWeights.Normal, System.Windows.FontStyles.Normal));
            }

            FontStyles = styles;
            OnPropertyChanged(nameof(FontStyles));
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (SetAsDefault)
            {
                // ✅ Save ONLY font family, weight, style
                _settings.DefaultMainFontFamily = SelectedFontFamily.Source;
                _settings.DefaultMainFontWeight = SelectedFontWeight.ToString();
                _settings.DefaultMainFontStyle = SelectedFontStyleEnum.ToString();
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RestoreDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedFontFamily = new FontFamily(AppDefaultFontFamily);
            UpdateFontStyles();
            SelectedFontStyle = FontStyles.FirstOrDefault(s =>
                s.Weight == AppDefaultFontWeight && s.Style == AppDefaultFontStyle)
                ?? FontStyles.FirstOrDefault();
            FontInputText = AppDefaultFontFamily;
            _isUserTyping = false;
            _typeTimer?.Stop();

            _settings.DefaultMainFontFamily = AppDefaultFontFamily;
            _settings.DefaultMainFontWeight = AppDefaultFontWeight.ToString();
            _settings.DefaultMainFontStyle = AppDefaultFontStyle.ToString();
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static FontWeight ParseFontWeight(string weightStr)
        {
            if (weightStr == "Bold") return FontWeights.Bold;
            if (weightStr == "Black") return FontWeights.Black;
            if (weightStr == "ExtraBold") return FontWeights.ExtraBold;
            if (weightStr == "DemiBold") return FontWeights.DemiBold;
            if (weightStr == "Light") return FontWeights.Light;
            if (weightStr == "ExtraLight") return FontWeights.ExtraLight;
            if (weightStr == "Thin") return FontWeights.Thin;
            return FontWeights.Normal;
        }

        private static FontStyle ParseFontStyle(string styleStr)
        {
            if (styleStr == "Italic") return System.Windows.FontStyles.Italic;
            if (styleStr == "Oblique") return System.Windows.FontStyles.Oblique;
            return System.Windows.FontStyles.Normal;
        }

        public class FontStyleItem
        {
            public string Name { get; set; }
            public FontWeight Weight { get; set; }
            public FontStyle Style { get; set; }

            public FontStyleItem(string name, FontWeight weight, FontStyle style)
            {
                Name = name;
                Weight = weight;
                Style = style;
            }
        }
    }
}