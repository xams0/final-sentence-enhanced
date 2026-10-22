using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lanppart3Launcher
{
    // "–  3  +" value picker, like the game's steppers.
    public sealed class Stepper : StackPanel
    {
        readonly TextBlock valueText;
        readonly Button minus, plus;
        int value;

        public int Min, Max, Step = 1;
        public Func<int, string> Format = v => v.ToString();
        public event Action<int> ValueChanged;

        public Stepper(int value, int min, int max, int step = 1, Func<int, string> format = null)
        {
            Orientation = Orientation.Horizontal;
            Min = min; Max = max; Step = step;
            if (format != null) Format = format;
            minus = MakeButton("–", -1);
            plus = MakeButton("+", +1);
            valueText = new TextBlock
            {
                MinWidth = 86, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontSize = 19, Foreground = (Brush)Application.Current.Resources["OptionLabel"],
            };
            Children.Add(minus);
            Children.Add(valueText);
            Children.Add(plus);
            Value = value;
        }

        Button MakeButton(string text, int direction)
        {
            var b = new Button
            {
                Content = text, FontSize = 22, Width = 34, Height = 34,
                Style = (Style)Application.Current.Resources["PlainButton"],
            };
            b.Click += (s, e) => Value = value + direction * Step;
            return b;
        }

        public int Value
        {
            get => value;
            set
            {
                int v = Math.Max(Min, Math.Min(Max, value));
                bool changed = v != this.value;
                this.value = v;
                valueText.Text = Format(v);
                minus.IsEnabled = v > Min;
                plus.IsEnabled = v < Max;
                if (changed) ValueChanged?.Invoke(v);
            }
        }

        public static string Duration(int seconds) => (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
    }

    // Builds the option rows of a settings page, laid out like the game's lobby options window.
    public sealed class OptionsBuilder
    {
        readonly Panel panel;
        public OptionsBuilder(Panel panel) { this.panel = panel; }

        static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        public void Section(string title)
        {
            panel.Children.Add(new TextBlock
            {
                Text = title, FontSize = 20, Foreground = Res("OptionCaption"),
                Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 16, 0, 6),
            });
        }

        public T Row<T>(string label, T control, string hint = null) where T : FrameworkElement
        {
            var grid = new Grid { Margin = new Thickness(18, 3, 0, 3), MinHeight = 38 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = new TextBlock
            {
                Text = label, FontSize = 17, Foreground = Res("OptionLabel"), VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            if (hint != null) text.ToolTip = hint;
            Grid.SetColumn(control, 1);
            control.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(text);
            grid.Children.Add(control);
            panel.Children.Add(grid);
            return control;
        }

        public void Note(string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text, FontSize = 14, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(18, 2, 0, 4),
            });
        }

        public static ComboBox Combo(IEnumerable<string> items, int selected, double width = 250)
        {
            var c = new ComboBox { Style = (Style)Application.Current.Resources["GameCombo"], Width = width, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedIndex = Math.Max(0, Math.Min(selected, c.Items.Count - 1));
            return c;
        }

        public static CheckBox Check(bool value) =>
            new CheckBox { Style = (Style)Application.Current.Resources["GameCheck"], IsChecked = value };

        public static TextBox Text(string value, double width = 250) =>
            new TextBox { Style = (Style)Application.Current.Resources["GameText"], Text = value, Width = width, HorizontalAlignment = HorizontalAlignment.Left };
    }
}
