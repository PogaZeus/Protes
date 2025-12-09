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
    public partial class FontPickerWindow : Window, INotifyPropertyChanged
    {
        // ===== Helper conversion methods (inline) =====
        private static double PointsToDip(double points) => points * 96.0 / 72.0;
        private static double DipToPoints(double dip) => dip * 72.0 / 96.0;

        // ===== Instance fields =====
        private FontFamily _selectedFontFamily;
        private double _selectedFontSize;
        private FontStyleItem _selectedFontStyle;
        private string _selectedScript;
        private string _fontInputText;
        private bool _isUserTyping = false;
        private DispatcherTimer _typeTimer;

        // ===== Application hard-coded defaults (in POINTS now) =====
        private static readonly string AppDefaultFontFamily = "Consolas";
        private static readonly double AppDefaultFontSizeInPoints = 11.0; // matches Notepad feel
        private static readonly FontWeight AppDefaultFontWeight = FontWeights.Normal;
        private static readonly FontStyle AppDefaultFontStyle = System.Windows.FontStyles.Normal;

        public event PropertyChangedEventHandler PropertyChanged;

        // ===== Bindable collections =====
        public List<FontFamily> FontFamilies { get; private set; } // always full list
        public List<double> FontSizes { get; } // in POINTS
        public List<FontStyleItem> FontStyles { get; set; }
        public List<string> Scripts { get; }

        // ===== Bindable properties =====
        public FontFamily SelectedFontFamily
        {
            get => _selectedFontFamily;
            set
            {
                _selectedFontFamily = value;
                OnPropertyChanged(nameof(SelectedFontFamily));
                UpdateFontStyles();

                // Only update input box if user isn't actively typing
                if (!_isUserTyping)
                {
                    FontInputText = value?.Source ?? "";
                }
            }
        }

        public double SelectedFontSize
        {
            get => _selectedFontSize;
            set
            {
                _selectedFontSize = value;
                OnPropertyChanged(nameof(SelectedFontSize));
                OnPropertyChanged(nameof(SampleFontSize));
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

                // Find first font that starts with user input (case-insensitive)
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

                // Reset typing state after inactivity
                _typeTimer?.Stop();
                _typeTimer?.Start();
            }
        }

        public string SelectedFontStyleName => _selectedFontStyle?.Name ?? "Regular";
        public FontWeight SelectedFontWeight => _selectedFontStyle?.Weight ?? FontWeights.Normal;
        public FontStyle SelectedFontStyleEnum => _selectedFontStyle?.Style ?? System.Windows.FontStyles.Normal;

        // Sample text uses DIPs for rendering, but clamped
        public double SampleFontSize
        {
            get
            {
                double dip = PointsToDip(_selectedFontSize);
                return dip > 24 ? 24 : dip;
            }
        }

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

        // ===== Constructors =====

        /// <summary>
        /// Parameterless constructor for XAML designer support (DEBUG only)
        /// </summary>
        public FontPickerWindow()
        {
#if DEBUG
            InitializeComponent();
            FontFamilies = new List<FontFamily> { new FontFamily(AppDefaultFontFamily) };
            FontSizes = new List<double> { AppDefaultFontSizeInPoints };
            Scripts = new List<string> { "Western" };
            FontStyles = new List<FontStyleItem>
            {
                new FontStyleItem("Regular", AppDefaultFontWeight, AppDefaultFontStyle)
            };
            SelectedFontFamily = FontFamilies[0];
            SelectedFontSize = AppDefaultFontSizeInPoints;
            SelectedFontStyle = FontStyles[0];
            SelectedScript = "Western";
            FontInputText = AppDefaultFontFamily;
            DataContext = this;
#endif
        }

        /// <summary>
        /// Runtime constructor — always use this at runtime.
        /// </summary>
        public FontPickerWindow(FontFamily currentFont, double currentSizeInDip, SettingsManager settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _settings = settings;
            InitializeComponent();

            // Load full system font list
            FontFamilies = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();

            // Font sizes in POINTS (user-friendly values)
            FontSizes = new List<double> { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 };

            Scripts = new List<string>
            {
                "Western", "Arabic", "Baltic", "Central European", "Chinese Simplified",
                "Chinese Traditional", "Cyrillic", "Greek", "Hebrew", "Japanese",
                "Korean", "Thai", "Turkish", "Vietnamese"
            };

            // Select initial font
            SelectedFontFamily = FontFamilies.FirstOrDefault(f => f.Source == currentFont?.Source)
                ?? new FontFamily(_settings.DefaultNoteEditorFontFamily ?? AppDefaultFontFamily);

            // Convert size from DIPs → POINTS
            double currentSizeInPoints = DipToPoints(currentSizeInDip);
            currentSizeInPoints = Math.Round(currentSizeInPoints * 2) / 2.0;
            SelectedFontSize = FontSizes.Contains(currentSizeInPoints)
                ? currentSizeInPoints
                : DipToPoints(_settings.DefaultNoteEditorFontSize);

            SelectedScript = "Western";
            UpdateFontStyles();

            // Restore saved font style
            FontWeight savedWeight = ParseFontWeight(_settings.DefaultNoteEditorFontWeight);
            FontStyle savedStyle = ParseFontStyle(_settings.DefaultNoteEditorFontStyle);
            SelectedFontStyle = FontStyles.FirstOrDefault(s =>
                s.Weight.Equals(savedWeight) && s.Style.Equals(savedStyle)) ?? FontStyles.FirstOrDefault();

            // Set up typing timer
            _typeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _typeTimer.Tick += (s, e) =>
            {
                _isUserTyping = false;
                _typeTimer.Stop();
            };

            // Initialize input box with current font name
            FontInputText = SelectedFontFamily?.Source ?? AppDefaultFontFamily;

            DataContext = this;
        }

        // ===== Key handler for Escape key =====
        private void FontInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // Revert to currently selected font
                FontInputText = SelectedFontFamily?.Source ?? "";
                _isUserTyping = false;
                _typeTimer?.Stop();
                e.Handled = true;
            }
        }

        // ===== Logic =====
        private void UpdateFontStyles()
        {
            if (_selectedFontFamily == null)
                return;

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
                _settings.DefaultNoteEditorFontFamily = SelectedFontFamily.Source;
                _settings.DefaultNoteEditorFontSize = SelectedFontSize; // stored as POINTS
                _settings.DefaultNoteEditorFontWeight = SelectedFontWeight.ToString();
                _settings.DefaultNoteEditorFontStyle = SelectedFontStyleEnum.ToString();
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
            // Reset to app default
            SelectedFontFamily = new FontFamily(AppDefaultFontFamily);
            SelectedFontSize = AppDefaultFontSizeInPoints;

            UpdateFontStyles();

            SelectedFontStyle = FontStyles.FirstOrDefault(s =>
                s.Weight == AppDefaultFontWeight && s.Style == AppDefaultFontStyle)
                ?? FontStyles.FirstOrDefault();

            // Update input box
            FontInputText = AppDefaultFontFamily;

            // Clear typing state
            _isUserTyping = false;
            _typeTimer?.Stop();

            // Save defaults
            _settings.DefaultNoteEditorFontFamily = AppDefaultFontFamily;
            _settings.DefaultNoteEditorFontSize = AppDefaultFontSizeInPoints;
            _settings.DefaultNoteEditorFontWeight = AppDefaultFontWeight.ToString();
            _settings.DefaultNoteEditorFontStyle = AppDefaultFontStyle.ToString();
        }

        // ===== INotifyPropertyChanged =====
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // ===== Helper methods =====
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

        // ===== Nested class =====
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