// The widget set: text, a button, a checkbox, a three-way switch, and a section
// that opens. Each one is composed from Border/TextBlock so the Swiss look holds
// exactly, including on hover and focus, where WPF's own templates take over.
//
// Keyboard reachability is part of the floor, not an extra: everything clickable is
// focusable, answers to Space and Enter, and draws a visible focus ring.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EULord.Core;

namespace EULord.Ui
{
    internal static class Text
    {
        public static TextBlock Make(string content, double size, Brush brush, FontWeight weight)
        {
            return new TextBlock
            {
                Text = content ?? string.Empty,
                FontFamily = Theme.Sans,
                FontSize = size,
                Foreground = brush,
                FontWeight = weight,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,       // flush left, ragged right
                // Without this, a TextBlock with a MaxWidth inside a StackPanel
                // stretches and then centres itself, which quietly breaks the grid.
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        public static TextBlock Body(string content) { return Make(content, Theme.SizeBody, Theme.Ink, FontWeights.Normal); }
        public static TextBlock Lead(string content) { return Make(content, Theme.SizeLead, Theme.Ink, FontWeights.Normal); }
        public static TextBlock Muted(string content) { return Make(content, Theme.SizeBody, Theme.Mute, FontWeights.Normal); }
        public static TextBlock Head(string content) { return Make(content, Theme.SizeHead, Theme.Ink, FontWeights.Bold); }

        /// <summary>
        /// The small tracked label used for table column heads and the masthead's second
        /// line. Letter-spacing is a Swiss device for small type, and these are structural
        /// column heads rather than decorative eyebrows over every block.
        /// </summary>
        public static TextBlock Micro(string content, Brush brush)
        {
            TextBlock block = Make((content ?? string.Empty).ToUpperInvariant(), Theme.SizeMicro, brush, FontWeights.Normal);
            block.TextWrapping = TextWrapping.NoWrap;
            return block;
        }

        /// <summary>The one display element: the count of gains, set large and tight.</summary>
        public static TextBlock Numeral(string content, Brush brush)
        {
            return new TextBlock
            {
                Text = content ?? string.Empty,
                FontFamily = Theme.Display,
                FontSize = Theme.SizeDisplay,
                Foreground = brush,
                TextWrapping = TextWrapping.NoWrap,
                LineHeight = Theme.SizeDisplay * 1.02,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
        }
    }

    /// <summary>A clickable surface with hover, press, focus and disabled states.</summary>
    internal class Pressable : Border
    {
        private bool _hovered, _pressed, _enabled = true;

        public event EventHandler Activated;

        protected Pressable()
        {
            Focusable = true;
            SnapsToDevicePixels = true;
            Cursor = Cursors.Hand;

            MouseEnter += (s, e) => { _hovered = true; Repaint(); };
            MouseLeave += (s, e) => { _hovered = false; _pressed = false; Repaint(); };
            PreviewMouseLeftButtonDown += (s, e) => { _pressed = true; Focus(); Repaint(); };
            PreviewMouseLeftButtonUp += (s, e) =>
            {
                bool wasPressed = _pressed;
                _pressed = false;
                Repaint();
                if (wasPressed && _enabled) Fire();
            };
            GotKeyboardFocus += (s, e) => Repaint();
            LostKeyboardFocus += (s, e) => Repaint();
            KeyDown += (s, e) =>
            {
                if (e.Key != Key.Space && e.Key != Key.Enter) return;
                e.Handled = true;
                if (_enabled) Fire();
            };
        }

        public bool Hovered { get { return _hovered; } }
        public bool Pressed { get { return _pressed; } }
        public bool Focused { get { return IsKeyboardFocused; } }

        public bool Active
        {
            get { return _enabled; }
            set
            {
                _enabled = value;
                Cursor = value ? Cursors.Hand : Cursors.Arrow;
                Focusable = value;
                Repaint();
            }
        }

        protected void Fire()
        {
            EventHandler handler = Activated;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected virtual void Repaint() { }
    }

    /// <summary>The primary action: a solid red block of colour with the label in white.</summary>
    internal sealed class PrimaryButton : Pressable
    {
        private readonly TextBlock _label;

        public PrimaryButton(string caption, double width)
        {
            _label = Text.Make(caption, Theme.SizeLead, Theme.Paper, FontWeights.Bold);
            _label.TextWrapping = TextWrapping.NoWrap;
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.VerticalAlignment = VerticalAlignment.Center;

            Child = _label;
            Width = width;
            Height = 46;
            HorizontalAlignment = HorizontalAlignment.Left;
            Repaint();
        }

        public string Caption
        {
            get { return _label.Text; }
            set { _label.Text = value; }
        }

        protected override void Repaint()
        {
            if (!Active)
            {
                Background = Theme.Field;
                _label.Foreground = Theme.Faint;
                BorderBrush = Theme.Rule;
                BorderThickness = new Thickness(1);
                return;
            }

            Background = Pressed ? Theme.RedDark : (Hovered ? Theme.RedDark : Theme.Red);
            _label.Foreground = Theme.Paper;
            BorderBrush = Theme.Ink;
            BorderThickness = new Thickness(Focused ? 2 : 0);
        }
    }

    /// <summary>A quiet secondary action: a label with a rule under it, red on hover.</summary>
    internal sealed class LinkButton : Pressable
    {
        private readonly TextBlock _label;
        private readonly Border _underline;

        public LinkButton(string caption, double size)
        {
            _label = Text.Make(caption, size, Theme.Ink, FontWeights.Normal);
            _label.TextWrapping = TextWrapping.NoWrap;

            _underline = new Border { Height = 1, Background = Theme.Rule, Margin = new Thickness(0, 2, 0, 0) };

            var stack = new StackPanel();
            stack.Children.Add(_label);
            stack.Children.Add(_underline);

            Child = stack;
            Padding = new Thickness(0, 1, 0, 1);
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Center;
            Repaint();
        }

        public string Caption { get { return _label.Text; } set { _label.Text = value; } }

        protected override void Repaint()
        {
            if (!Active)
            {
                _label.Foreground = Theme.Faint;
                _underline.Background = Theme.Rule;
                BorderThickness = new Thickness(0);
                return;
            }

            bool lit = Hovered || Pressed;
            _label.Foreground = lit ? Theme.Red : Theme.Ink;
            _underline.Background = lit ? Theme.Red : Theme.Rule;
            BorderBrush = Theme.Red;
            BorderThickness = new Thickness(0, 0, 0, Focused ? 2 : 0);
        }
    }

    /// <summary>
    /// One tickable row: a square that fills red when ticked, a title, and a muted
    /// note. Used for both the matters-of-taste list and the app list.
    /// </summary>
    internal sealed class CheckRow : Pressable
    {
        private readonly Border _box;
        private readonly Border _fill;
        private readonly TextBlock _title;
        private readonly TextBlock _note;
        private bool _checked;

        public event EventHandler CheckedChanged;

        public CheckRow(string title, string note, bool isChecked)
        {
            _fill = new Border
            {
                Width = 8, Height = 8, Background = Theme.Red,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _box = new Border
            {
                Width = 15, Height = 15,
                BorderBrush = Theme.Ink,
                BorderThickness = new Thickness(1),
                Background = Theme.Paper,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 12, 0),
                Child = _fill,
                SnapsToDevicePixels = true
            };

            _title = Text.Body(title);
            _note = Text.Make(note, Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
            _note.Margin = new Thickness(0, 1, 0, 0);

            var column = new StackPanel();
            column.Children.Add(_title);
            if (!string.IsNullOrEmpty(note)) column.Children.Add(_note);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_box, 0);
            Grid.SetColumn(column, 1);
            row.Children.Add(_box);
            row.Children.Add(column);

            Child = row;
            Padding = new Thickness(6, 7, 6, 7);
            _checked = isChecked;
            Activated += (s, e) => { IsChecked = !IsChecked; };
            Repaint();
        }

        public bool IsChecked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Repaint();
                EventHandler handler = CheckedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public void SetNote(string note)
        {
            _note.Text = note ?? string.Empty;
            _note.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
        }

        protected override void Repaint()
        {
            if (_fill == null) return;

            _fill.Visibility = _checked ? Visibility.Visible : Visibility.Hidden;
            _box.BorderBrush = Active ? (_checked ? Theme.Red : Theme.Ink) : Theme.Rule;
            _box.BorderThickness = new Thickness(_checked ? 2 : 1);
            _title.Foreground = Active ? Theme.Ink : Theme.Faint;
            Background = Hovered && Active ? Theme.Field : Brushes.Transparent;
            BorderBrush = Theme.Red;
            BorderThickness = new Thickness(Focused ? 1 : 0);
        }
    }

    /// <summary>
    /// A three-way switch for the advanced table: Auto, EEA, Global. The chosen
    /// segment is filled; the others are outlined.
    /// </summary>
    internal sealed class Segmented : StackPanel
    {
        private readonly List<Pressable> _segments = new List<Pressable>();
        private readonly List<TextBlock> _labels = new List<TextBlock>();
        private int _selected;

        public event EventHandler SelectionChanged;

        public Segmented(string[] captions, int selected)
        {
            Orientation = Orientation.Horizontal;
            _selected = selected;

            for (int i = 0; i < captions.Length; i++)
            {
                int index = i;
                TextBlock label = Text.Make(captions[i], Theme.SizeMicro, Theme.Ink, FontWeights.Normal);
                label.TextWrapping = TextWrapping.NoWrap;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;

                var segment = new Segment { Child = label, Padding = new Thickness(9, 3, 9, 3) };
                segment.Margin = new Thickness(i == 0 ? 0 : -1, 0, 0, 0);
                segment.Activated += (s, e) => { Selected = index; };

                _segments.Add(segment);
                _labels.Add(label);
                Children.Add(segment);
            }
            Repaint();
        }

        public int Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Repaint();
                EventHandler handler = SelectionChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Index 0 is the resting choice. Selecting it is drawn quietly, because across
        /// a table of 68 rows the default is almost every row and filling them all would
        /// be a wall of black. Any other selection is a deliberate override, so it takes
        /// the accent and the handful of changed rows stand out at a glance.
        /// </summary>
        private void Repaint()
        {
            bool overridden = _selected != 0;

            for (int i = 0; i < _segments.Count; i++)
            {
                bool on = i == _selected;
                Pressable segment = _segments[i];

                segment.Background = on && overridden ? Theme.Red : Theme.Paper;
                segment.BorderBrush = segment.IsKeyboardFocused ? Theme.Red
                                    : (on && overridden ? Theme.Red : Theme.Rule);
                segment.BorderThickness = new Thickness(1);

                _labels[i].Foreground = on ? (overridden ? Theme.Paper : Theme.Ink) : Theme.Faint;
                _labels[i].FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        private sealed class Segment : Pressable
        {
            public Segment() { }
            protected override void Repaint()
            {
                var parent = Parent as Segmented;
                if (parent != null) parent.Repaint();
            }
        }
    }

    /// <summary>
    /// A section that opens. Progressive disclosure is the whole GUI structure: the
    /// status and the one button are always visible, everything else is behind a
    /// heading the user can open. The marker is a typographic plus and minus rather
    /// than a chevron, which suits the grid.
    /// </summary>
    internal sealed class Section : StackPanel
    {
        private readonly TextBlock _marker;
        private readonly TextBlock _title;
        private readonly TextBlock _summary;
        private readonly StackPanel _body;
        private readonly Pressable _header;
        private bool _open;

        /// <summary>Called the first time the section opens, so heavy work stays lazy.</summary>
        public event EventHandler FirstOpened;

        private bool _everOpened;

        public Section(string title, string summary)
        {
            _marker = Text.Make("+", Theme.SizeHead, Theme.Red, FontWeights.Bold);
            _marker.Width = 24;
            _marker.TextWrapping = TextWrapping.NoWrap;
            _marker.VerticalAlignment = VerticalAlignment.Center;

            _title = Text.Make(title, Theme.SizeLead, Theme.Ink, FontWeights.Bold);
            _title.TextWrapping = TextWrapping.NoWrap;
            _title.VerticalAlignment = VerticalAlignment.Center;

            _summary = Text.Make(summary, Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
            _summary.TextWrapping = TextWrapping.NoWrap;
            _summary.VerticalAlignment = VerticalAlignment.Center;
            _summary.HorizontalAlignment = HorizontalAlignment.Right;
            _summary.Margin = new Thickness(16, 0, 0, 0);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_marker, 0);
            Grid.SetColumn(_title, 1);
            Grid.SetColumn(_summary, 2);
            grid.Children.Add(_marker);
            grid.Children.Add(_title);
            grid.Children.Add(_summary);

            _header = new SectionHeader { Child = grid, Padding = new Thickness(0, 13, 0, 13) };
            _header.Activated += (s, e) => { IsOpen = !IsOpen; };

            _body = new StackPanel { Margin = new Thickness(24, 0, 0, 18), Visibility = Visibility.Collapsed };

            Children.Add(Theme.HairRule(1, Theme.Rule, 0, 0));
            Children.Add(_header);
            Children.Add(_body);
        }

        public StackPanel Body { get { return _body; } }

        public string Summary
        {
            get { return _summary.Text; }
            set { _summary.Text = value ?? string.Empty; }
        }

        public bool IsOpen
        {
            get { return _open; }
            set
            {
                _open = value;
                _marker.Text = value ? "−" : "+";   // minus sign, not a hyphen
                _body.Visibility = value ? Visibility.Visible : Visibility.Collapsed;

                if (!value || _everOpened) return;
                _everOpened = true;
                EventHandler handler = FirstOpened;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private sealed class SectionHeader : Pressable
        {
            protected override void Repaint()
            {
                Background = Hovered ? Theme.Field : Brushes.Transparent;
                BorderBrush = Theme.Red;
                BorderThickness = new Thickness(0, 0, 0, IsKeyboardFocused ? 2 : 0);
            }
        }
    }

    internal static class Layout
    {
        /// <summary>A label/value pair on the grid: label in the fixed left column, value flush left beside it.</summary>
        public static Grid ReadoutRow(string label, UIElement value)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, Theme.RowGap) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.LabelColumn) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock labelBlock = Text.Micro(label, Theme.Mute);
            labelBlock.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(value, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(value);
            return grid;
        }

        public static Grid ReadoutRow(string label, string value, Brush brush)
        {
            TextBlock block = Text.Make(value, Theme.SizeBody, brush, FontWeights.Normal);
            block.TextWrapping = TextWrapping.NoWrap;
            block.VerticalAlignment = VerticalAlignment.Center;
            return ReadoutRow(label, block);
        }

        public static StackPanel Stack(double bottomMargin)
        {
            return new StackPanel { Margin = new Thickness(0, 0, 0, bottomMargin) };
        }

        public static Brush BrushFor(CheckResult result)
        {
            switch (result)
            {
                case CheckResult.Pass: return Theme.Ink;
                case CheckResult.Fail: return Theme.Red;
                default: return Theme.Mute;
            }
        }

        public static L LabelFor(CheckResult result)
        {
            switch (result)
            {
                case CheckResult.Pass: return S.VerifyPass;
                case CheckResult.Pending: return S.VerifyPending;
                case CheckResult.Fail: return S.Failed;
                default: return S.VerifyUnknown;
            }
        }
    }
}
