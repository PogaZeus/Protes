using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Protes.Views
{
    public partial class CalculatorWindow : Window
    {
        private readonly INoteRepository _noteRepository;
        private readonly List<FullNote> _recentNotes;
        private readonly bool _isEditable;
        private readonly Action _onNoteSaved;
        private string _currentExpression = "0";
        private string _lastRawExpression = "";     // e.g., "5 * 5 * 5"
        private string _lastFormattedExpression = ""; // e.g., "5 × 5 × 5"
        private double _lastResult = 0;
        private bool _isNewCalculation = true;
        private readonly List<string> _history = new List<string>();

        public bool IsEditable => _isEditable;

        public CalculatorWindow(INoteRepository noteRepository, List<FullNote> recentNotes, bool isEditable, Action onNoteSaved)
        {
            InitializeComponent();
            _noteRepository = noteRepository;
            _recentNotes = recentNotes.Take(10).ToList();
            _isEditable = isEditable;
            _onNoteSaved = onNoteSaved;
            LoadRecentNotes();
            SizeToContent = SizeToContent.Height;
        }

        private void LoadRecentNotes()
        {
            if (!_isEditable) return;
            InsertToComboBox.ItemsSource = _recentNotes;
            InsertToComboBox.DisplayMemberPath = "Title";
            if (_recentNotes.Any())
                InsertToComboBox.SelectedIndex = 0;
        }

        private void UpdateDisplay() => DisplayBox.Text = _currentExpression;

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            string content = btn.Content.ToString();

            if (_isNewCalculation && !IsOperator(content) && content != "C" && content != "CE" && content != "±" && content != "%")
            {
                _currentExpression = "";
                _isNewCalculation = false;
            }

            switch (content)
            {
                case "CE":
                    _currentExpression = "0";
                    _isNewCalculation = false;
                    break;

                case "C":
                    _currentExpression = "0";
                    _lastRawExpression = "";
                    _lastFormattedExpression = "";
                    _isNewCalculation = true;
                    break;

                case "=":
                    if (!string.IsNullOrWhiteSpace(_currentExpression) && !IsOperator(_currentExpression.Substring(_currentExpression.Length - 1, 1)))
                    {
                        try
                        {
                            string rawExpr = _currentExpression; // e.g., "5 * 5 * 5"
                            string formattedExpr = rawExpr.Replace("*", "×").Replace("/", "÷"); // → "5 × 5 × 5"

                            double result = EvaluateExpression(_currentExpression);
                            string fullLine = $"{formattedExpr} = {result}";

                            _lastRawExpression = rawExpr;
                            _lastFormattedExpression = formattedExpr;
                            _lastResult = result;
                            _currentExpression = result.ToString();

                            AddToHistory(fullLine);
                        }
                        catch
                        {
                            _currentExpression = "Error";
                            _lastRawExpression = "";
                            _lastFormattedExpression = "";
                        }
                        _isNewCalculation = true;
                    }
                    break;

                case "+":
                case "-":
                case "×":
                case "÷":
                    if (_currentExpression == "0" || _currentExpression == "Error")
                        _currentExpression = _lastResult.ToString();
                    if (!IsOperator(_currentExpression.Substring(_currentExpression.Length - 1, 1)))
                        _currentExpression += " " + MapOperator(content) + " ";
                    break;

                case "±":
                    _currentExpression = _currentExpression.StartsWith("-")
                        ? _currentExpression.Substring(1)
                        : "-" + _currentExpression;
                    break;

                case "%":
                    if (!IsOperator(_currentExpression.Substring(_currentExpression.Length - 1, 1)))
                        _currentExpression = (double.Parse(_currentExpression) / 100).ToString();
                    break;

                case ".":
                    if (_currentExpression == "0") _currentExpression = "";
                    if (!_currentExpression.Contains(".")) _currentExpression += ".";
                    break;

                default:
                    _currentExpression = (_currentExpression == "0" || _currentExpression == "Error") ? content : _currentExpression + content;
                    break;
            }
            UpdateDisplay();
        }

        private bool IsOperator(string s) => "+-*/".Contains(s);
        private string MapOperator(string s) => s == "×" ? "*" : s == "÷" ? "/" : s;

        private double EvaluateExpression(string expr)
        {
            var tokens = expr.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return 0;
            double result = double.Parse(tokens[0]);
            for (int i = 1; i < tokens.Length; i += 2)
            {
                if (i + 1 >= tokens.Length) break;
                string op = tokens[i];
                double next = double.Parse(tokens[i + 1]);
                switch (op)
                {
                    case "+": result += next; break;
                    case "-": result -= next; break;
                    case "*": result *= next; break;
                    case "/": result /= next; break;
                }
            }
            return result;
        }

        // === History ===
        private void AddToHistory(string entry)
        {
            _history.Insert(0, entry);
            if (_history.Count > 100) _history.RemoveAt(_history.Count - 1);
            if (HistoryPanel.Visibility == Visibility.Visible)
                RefreshHistoryUI();
        }

        private void HistoryToggleButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryPanel.Visibility = HistoryPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (HistoryPanel.Visibility == Visibility.Visible)
                RefreshHistoryUI();
        }

        private void RefreshHistoryUI()
        {
            HistoryStackPanel.Children.Clear();
            foreach (string line in _history)
            {
                HistoryStackPanel.Children.Add(new TextBlock
                {
                    Text = line,
                    Margin = new Thickness(4, 2, 4, 2)
                });
            }
        }

        // === New Note ===
        private void NewNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isEditable) return;
            try
            {
                string rawTitle = !string.IsNullOrWhiteSpace(_lastFormattedExpression)
                    ? _lastFormattedExpression
                    : _currentExpression.Replace("*", "×").Replace("/", "÷");
                string title = $"🧮 Calculator: {rawTitle}";

                string content = $"🧮 Result: {rawTitle} = \"{_lastResult}\"";
                _noteRepository.SaveNote(title, content, "calculator");
                _onNoteSaved?.Invoke();
                MessageBox.Show("Note saved.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // === Insert Into Existing Note ===
        private void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(InsertToComboBox.SelectedItem is FullNote note)) return;
            if (string.IsNullOrWhiteSpace(_currentExpression)) return;

            string rawExpr = !string.IsNullOrWhiteSpace(_lastFormattedExpression)
                ? _lastFormattedExpression
                : _currentExpression.Replace("*", "×").Replace("/", "÷");
            string expr = $"🧮 Calculator: {rawExpr}";

            string newLine = $"🧮 Result: {rawExpr} = \"{_lastResult}\"";
            string updatedContent = string.IsNullOrEmpty(note.Content)
                ? newLine
                : newLine + Environment.NewLine + note.Content;

            string updatedTags = note.Tags;
            if (string.IsNullOrEmpty(updatedTags) || !ContainsTag(updatedTags, "calculator"))
            {
                updatedTags = "calculator" + (string.IsNullOrEmpty(updatedTags) ? "" : ", " + updatedTags);
            }

            try
            {
                _noteRepository.UpdateNote(note.Id, note.Title, updatedContent, updatedTags);
                _onNoteSaved?.Invoke();
                MessageBox.Show("Note updated.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ContainsTag(string tags, string target)
        {
            if (string.IsNullOrEmpty(tags)) return false;
            var tagList = tags.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(t => t.Trim().ToLowerInvariant());
            return tagList.Contains(target.ToLowerInvariant());
        }
        private void SaveHistoryAsNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isEditable || _history.Count == 0) return;
            try
            {
                string title = "🧮 Calculator History";
                string content = string.Join(Environment.NewLine, _history);
                _noteRepository.SaveNote(title, content, "calculator");
                _onNoteSaved?.Invoke();
                MessageBox.Show("History saved as note.", "Protes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Clear all calculation history?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _history.Clear();
                if (HistoryPanel.Visibility == Visibility.Visible)
                    RefreshHistoryUI();
            }
        }
    }
}