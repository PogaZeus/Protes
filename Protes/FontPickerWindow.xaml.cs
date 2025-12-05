using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Protes.Views
{
    public partial class FontPickerWindow : Window, INotifyPropertyChanged
    {
        // ===== Instance fields =====
        private FontFamily _selectedFontFamily;
        private double _selectedFontSize;
        private FontStyleItem _selectedFontStyle;
        private string _selectedScript;
        // ===== Application hard-coded defaults (never change) =====
        private static readonly string AppDefaultFontFamily = "Segoe UI";
        private static readonly double AppDefaultFontSize = 14.0;
        private static readonly FontWeight AppDefaultFontWeight = FontWeights.Normal;
        private static readonly FontStyle AppDefaultFontStyle = System.Windows.FontStyles.Normal;

        public event PropertyChangedEventHandler PropertyChanged;

        // ===== Bindable collections =====
        public List<FontFamily> FontFamilies { get; }
        public List<double> FontSizes { get; }
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

        public string SelectedFontStyleName => _selectedFontStyle?.Name ?? "Regular";
        public FontWeight SelectedFontWeight => _selectedFontStyle?.Weight ?? FontWeights.Normal;
        public FontStyle SelectedFontStyleEnum => _selectedFontStyle?.Style ?? System.Windows.FontStyles.Normal;
        public double SampleFontSize => _selectedFontSize > 24 ? 24 : _selectedFontSize;

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
            // Minimal setup to avoid designer crash
            FontFamilies = new List<FontFamily> { new FontFamily(AppDefaultFontFamily) };
            FontSizes = new List<double> { AppDefaultFontSize };
            Scripts = new List<string> { "Western" };
            FontStyles = new List<FontStyleItem>
{
    new FontStyleItem("Regular", AppDefaultFontWeight, AppDefaultFontStyle)
};
            SelectedFontFamily = FontFamilies[0];
            SelectedFontSize = AppDefaultFontSize;
            SelectedFontStyle = FontStyles[0];
            SelectedScript = "Western";
            DataContext = this;
#endif
        }

        /// <summary>
        /// Runtime constructor — always use this at runtime.
        /// </summary>
        public FontPickerWindow(FontFamily currentFont, double currentSize, SettingsManager settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _settings = settings;
            InitializeComponent();

            FontFamilies = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
            FontSizes = new List<double> { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 };
            Scripts = new List<string>
            {
                "Western", "Arabic", "Baltic", "Central European", "Chinese Simplified",
                "Chinese Traditional", "Cyrillic", "Greek", "Hebrew", "Japanese",
                "Korean", "Thai", "Turkish", "Vietnamese"
            };

            SelectedFontFamily = FontFamilies.FirstOrDefault(f => f.Source == currentFont?.Source)
                ?? new FontFamily(_settings.DefaultNoteEditorFontFamily ?? AppDefaultFontFamily);

            SelectedFontSize = FontSizes.Contains(currentSize) ? currentSize : _settings.DefaultNoteEditorFontSize;
            SelectedScript = "Western";

            UpdateFontStyles();

            // Restore saved default style
            FontWeight savedWeight = ParseFontWeight(_settings.DefaultNoteEditorFontWeight);
            FontStyle savedStyle = ParseFontStyle(_settings.DefaultNoteEditorFontStyle);

            SelectedFontStyle = FontStyles.FirstOrDefault(s =>
                s.Weight.Equals(savedWeight) && s.Style.Equals(savedStyle));

            if (SelectedFontStyle == null)
                SelectedFontStyle = FontStyles.FirstOrDefault();

            DataContext = this;
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
                _settings.DefaultNoteEditorFontSize = SelectedFontSize;
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
            // Reset to APP DEFAULT (not user's saved default)
            SelectedFontFamily = new FontFamily(AppDefaultFontFamily);
            SelectedFontSize = AppDefaultFontSize;

            // Rebuild styles for Segoe UI (or fallback)
            UpdateFontStyles();

            // Select "Regular"
            SelectedFontStyle = FontStyles.FirstOrDefault(s =>
                s.Weight == AppDefaultFontWeight && s.Style == AppDefaultFontStyle)
                ?? FontStyles.FirstOrDefault();

            // ALSO reset the saved setting to app default
            _settings.DefaultNoteEditorFontFamily = AppDefaultFontFamily;
            _settings.DefaultNoteEditorFontSize = AppDefaultFontSize;
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