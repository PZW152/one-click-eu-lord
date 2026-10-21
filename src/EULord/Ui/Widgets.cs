// The widget set.
//
// Everything is composed from Border, Grid and TextBlock rather than restyled WPF
// controls, because a stock Button or CheckBox reasserts its own chrome on hover
// whatever the properties say, and this design has square corners, flat colour
// planes and no blurred shadows anywhere.
//
// Two rules hold throughout, both of them fixes for real bugs:
//
//   Nothing changes size between states. Every focusable thing carries a
//   permanent 2px border that is transparent until it has keyboard focus, so the
//   ring costs no layout. A thickness that changes on focus or on tick nudges
//   every neighbour sideways.
//
//   No outlined box is drawn with a stroke. Strokes and BorderThickness get
//   rounded away at fractional scaling -- that is exactly how the tick box lost
//   its bottom edge -- so an outline is two filled rectangles, one inside the
//   other. Filled rectangles cannot lose an edge.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EULord.Core;

namespace EULord.Ui
{
    internal static class Text
    {
        private static FontFamily _sans, _display;

        private static FontFamily Sans
        {
            get { if (_sans == null) _sans = new FontFamily(Theme.SansFamily); return _sans; }
        }

        private static FontFamily Display
        {
            get { if (_display == null) _display = new FontFamily(Theme.DisplayFamily); return _display; }
        }

        public static TextBlock Make(string content, double size, double line, int weight, Brush brush)
        {
            return new TextBlock
            {
                Text = content ?? string.Empty,
                FontFamily = Sans,
                FontSize = size,
                LineHeight = line,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                FontWeight = FontWeight.FromOpenTypeWeight(weight),
                Foreground = brush,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                // Flush left, ragged right. Without this a MaxWidth inside a
                // StackPanel stretches the block and then centres it, which quietly
                // breaks the grid.
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        public static TextBlock Body(string s, Brush b) { return Make(s, Theme.BodySize, Theme.BodyLine, Theme.BodyWeight, b); }
        public static TextBlock Note(string s, Brush b) { return Make(s, Theme.NoteSize, Theme.NoteLine, Theme.NoteWeight, b); }
        public static TextBlock Micro(string s, Brush b) { return Make(s, Theme.MicroSize, Theme.MicroLine, Theme.MicroWeight, b); }
        public static TextBlock MicroStrong(string s, Brush b) { return Make(s, Theme.MicroStrongSize, Theme.MicroStrongLine, Theme.MicroStrongWeight, b); }
        public static TextBlock Section(string s, Brush b) { return Make(s, Theme.SectionSize, Theme.SectionLine, Theme.SectionWeight, b); }
        public static TextBlock Action(string s, Brush b) { return Make(s, Theme.ActionSize, Theme.ActionLine, Theme.ActionWeight, b); }

        /// <summary>The state of the machine in words, top left of the lower plane.</summary>
        public static TextBlock Headline(string s, Brush b)
        {
            TextBlock t = Make(s, Theme.HeadlineSize, Theme.HeadlineLine, Theme.HeadlineWeight, b);
            t.FontFamily = Display;
            t.TextWrapping = TextWrapping.NoWrap;
            return t;
        }

        /// <summary>The count of gains. One per window, in Arial's Black face.</summary>
        public static TextBlock Count(string s, Brush b)
        {
            // No LineHeight: the token's tight line box is an optical value for CSS,
            // and forcing it here clips the top of the digits instead. The same
            // tightening is done with margins, which cannot crop a glyph.
            var t = new TextBlock
            {
                Text = s ?? string.Empty,
                FontFamily = Display,
                FontSize = Theme.CountSize,
                FontWeight = FontWeight.FromOpenTypeWeight(Theme.CountWeight),
                Foreground = b,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Only the vertical leading is taken up here. Pulling the glyph left by
            // the tracking value as well put its left edge under the plane's padding
            // and shaved the first digit.
            t.Margin = new Thickness(0, -Theme.CountSize * 0.17, 0, -Theme.CountSize * 0.1);
            return t;
        }
    }

    /// <summary>
    /// An outlined or filled square that cannot lose an edge: an outer filled
    /// rectangle with an inner one inset by the stroke width.
    /// </summary>
    internal sealed class Plate : Grid
    {
        private readonly Path _ring;
        private readonly Path _fill;

        public Plate(double size, double stroke)
        {
            Width = size;
            Height = size;

            // One filled ring, not a Border inside a Border. Nesting two filled
            // rectangles looked safe, but the inner one's margin is laid out, and at
            // some sub-pixel positions layout rounding let it cover the outer's top
            // and bottom edges completely -- leaving two vertical slivers where a box
            // should be. That was the reported bug, and it survived the first fix.
            //
            // A ring is a single filled geometry in this element's own coordinate
            // space: nothing is laid out inside it, so no edge can be rounded away.
            var outer = new RectangleGeometry(new Rect(0, 0, size, size));
            var hole = new RectangleGeometry(new Rect(stroke, stroke, size - 2 * stroke, size - 2 * stroke));

            var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outer, hole);
            ring.Freeze();
            hole.Freeze();

            _fill = new Path { Data = hole };
            _ring = new Path { Data = ring };

            Children.Add(_fill);
            Children.Add(_ring);
        }

        /// <summary>Outline only: the edge colour shows around the fill.</summary>
        public void Paint(Brush edge, Brush fill)
        {
            _ring.Fill = edge;
            _fill.Fill = fill;
        }

        /// <summary>The tick, drawn over the ring.</summary>
        public UIElement Overlay
        {
            set
            {
                while (Children.Count > 2) Children.RemoveAt(2);
                if (value != null) Children.Add(value);
            }
        }
    }

    /// <summary>A clickable surface with hover, press, focus and disabled states.</summary>
    internal abstract class Pressable : Border
    {
        private bool _hovered, _pressed, _enabled = true;

        public event EventHandler Activated;

        protected Pressable()
        {
            Focusable = true;
            SnapsToDevicePixels = true;
            Cursor = Cursors.Hand;

            // Always present, so showing it costs no layout.
            BorderThickness = new Thickness(2);
            BorderBrush = Brushes.Transparent;

            MouseEnter += delegate { _hovered = true; Repaint(); };
            MouseLeave += delegate { _hovered = false; _pressed = false; Repaint(); };
            PreviewMouseLeftButtonDown += delegate { _pressed = true; Focus(); Repaint(); };
            PreviewMouseLeftButtonUp += delegate
            {
                bool was = _pressed;
                _pressed = false;
                Repaint();
                if (was && _enabled) Fire();
            };
            GotKeyboardFocus += delegate { Repaint(); };
            LostKeyboardFocus += delegate { Repaint(); };
            KeyDown += (s, e) =>
            {
                if (e.Key != Key.Space && e.Key != Key.Enter) return;
                e.Handled = true;
                if (_enabled) Fire();
            };
        }

        protected bool Hovered { get { return _hovered; } }
        protected bool Pressed { get { return _pressed; } }

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
            EventHandler h = Activated;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>The focus ring, drawn without ever changing the element's size.</summary>
        protected void PaintFocus()
        {
            BorderBrush = IsKeyboardFocused ? Theme.Focus : Brushes.Transparent;
        }

        protected abstract void Repaint();

        /// <summary>Repaints after a theme change.</summary>
        public void Refresh() { Repaint(); }
    }

    /// <summary>
    /// The one action in the window. A flat red plane whose own bottom edge carries
    /// the progress band, so the button is the progress bar rather than sitting
    /// next to one.
    /// </summary>
    internal sealed class ActionButton : Pressable
    {
        private readonly TextBlock _label;
        private readonly Border _band;
        private readonly Grid _root;
        private double _progress;
        private bool _quiet;

        public ActionButton(string caption)
        {
            _label = Text.Action(caption, Theme.OnRed);
            _label.TextWrapping = TextWrapping.NoWrap;
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.Margin = new Thickness(Theme.Space4, 0, Theme.Space4, 0);

            _band = new Border
            {
                Height = Theme.Band,
                Background = Theme.RedDeep,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Width = 0,
                SnapsToDevicePixels = true
            };

            _root = new Grid();
            _root.Children.Add(_label);
            _root.Children.Add(_band);

            Child = _root;
            Height = Theme.ActionHeight;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Repaint();
        }

        public string Caption { get { return _label.Text; } set { _label.Text = value; } }

        /// <summary>Drawn as an outline instead of a fill, once there is nothing to do.</summary>
        public bool Quiet { get { return _quiet; } set { _quiet = value; Repaint(); } }

        /// <summary>0 to 1 along the button's bottom edge.</summary>
        public void SetProgress(double fraction, bool animate)
        {
            _progress = Math.Max(0, Math.Min(1, fraction));
            double target = ActualWidth * _progress;

            if (!animate || Theme.ReduceMotion || ActualWidth <= 0)
            {
                _band.BeginAnimation(WidthProperty, null);
                _band.Width = target;
                return;
            }

            var anim = new System.Windows.Media.Animation.DoubleAnimation(target, Theme.Wipe)
            {
                EasingFunction = Theme.Mech()
            };
            _band.BeginAnimation(WidthProperty, anim);
        }

        protected override void Repaint()
        {
            if (_label == null) return;
            PaintFocus();

            if (!Active)
            {
                Background = Brushes.Transparent;
                _label.Foreground = Theme.Plane2Muted;
                return;
            }

            if (_quiet)
            {
                Background = Brushes.Transparent;
                _label.Foreground = Theme.Plane2Ink;
                BorderBrush = IsKeyboardFocused ? Theme.Focus : Theme.Plane2Muted;
                return;
            }

            Background = (Hovered || Pressed) ? Theme.RedDeep : Theme.Red;
            _label.Foreground = Theme.OnRed;
            _band.Background = Theme.RedDeep;
        }
    }

    /// <summary>A quiet action: a label with a rule under it.</summary>
    internal sealed class LinkButton : Pressable
    {
        private readonly TextBlock _label;
        private readonly Border _rule;
        private Brush _ink, _hair;

        public LinkButton(string caption, Brush ink, Brush hair)
        {
            _ink = ink;
            _hair = hair;

            _label = Text.Note(caption, ink);
            _label.TextWrapping = TextWrapping.NoWrap;

            _rule = new Border { Height = 1, Background = hair, Margin = new Thickness(0, 1, 0, 0), SnapsToDevicePixels = true };

            var stack = new StackPanel();
            stack.Children.Add(_label);
            stack.Children.Add(_rule);

            Child = stack;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Center;
            Repaint();
        }

        public string Caption { get { return _label.Text; } set { _label.Text = value; } }

        public void Recolour(Brush ink, Brush hair) { _ink = ink; _hair = hair; Repaint(); }

        protected override void Repaint()
        {
            if (_label == null) return;
            PaintFocus();

            if (!Active)
            {
                _label.Foreground = Theme.Plane1Muted;
                _rule.Background = _hair;
                return;
            }

            bool lit = Hovered || Pressed;
            _label.Foreground = _ink;
            _rule.Background = lit ? _ink : _hair;
        }
    }

    /// <summary>
    /// One tickable row. The box is a Plate, so it keeps all four edges at every
    /// display scale, and its geometry is identical ticked and unticked.
    /// </summary>
    internal sealed class CheckRow : Pressable
    {
        private readonly Plate _box;
        private readonly Path _tick;
        private readonly TextBlock _title;
        private readonly TextBlock _note;
        private bool _checked;

        public event EventHandler CheckedChanged;

        /// <summary>The distance from the window edge to a row's text.</summary>
        public static double TextIndent { get { return Theme.GridMargin + Theme.Checkbox + Theme.Gutter; } }

        public CheckRow(string title, string note, bool isChecked)
        {
            _checked = isChecked;

            _tick = new Path
            {
                Data = Geometry.Parse("M 3,8 L 6.5,11.5 L 13,4"),
                Stroke = Theme.OnRed,
                StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Flat,
                StrokeEndLineCap = PenLineCap.Flat,
                StrokeLineJoin = PenLineJoin.Miter,
                SnapsToDevicePixels = true
            };

            _box = new Plate(Theme.Checkbox, Theme.CheckboxStroke);
            _box.Overlay = _tick;
            _box.VerticalAlignment = VerticalAlignment.Center;
            _box.Margin = new Thickness(0, 0, Theme.Gutter, 0);

            _title = Text.Body(title, Theme.Plane1Ink);
            _title.TextWrapping = TextWrapping.NoWrap;
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            _title.VerticalAlignment = VerticalAlignment.Center;

            _note = Text.Note(note, Theme.Plane1Muted);
            _note.TextWrapping = TextWrapping.NoWrap;
            _note.VerticalAlignment = VerticalAlignment.Center;
            _note.Margin = new Thickness(Theme.Gutter, 0, 0, 0);

            // Auto, star, Auto -- never three star columns. Star columns share out the
            // width, so a long title took the box's column down to a couple of pixels
            // and the box appeared as a sliver. An Auto column is exactly its content,
            // so the box is always 16 wide whatever the title says.
            var grid = new Grid { MinHeight = Theme.ChoiceRow };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(_box, 0);
            Grid.SetColumn(_title, 1);
            Grid.SetColumn(_note, 2);
            grid.Children.Add(_box);
            grid.Children.Add(_title);
            grid.Children.Add(_note);

            Child = grid;
            Padding = new Thickness(Theme.GridMargin - 2, 2, Theme.GridMargin - 2, 2);
            Activated += delegate { IsChecked = !IsChecked; };
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
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void Repaint()
        {
            if (_box == null) return;
            PaintFocus();

            _tick.Visibility = _checked ? Visibility.Visible : Visibility.Hidden;
            _box.Paint(_checked ? Theme.Red : Theme.Plane1Ink,
                       _checked ? Theme.Red : Theme.Plane1);
            _tick.Stroke = Theme.OnRed;

            _title.Foreground = Theme.Plane1Ink;
            _note.Foreground = Theme.Plane1Muted;
            Background = (Hovered && Active) ? Theme.Hover1 : Brushes.Transparent;
        }
    }

    /// <summary>
    /// A section that opens. Progressive disclosure is the whole structure: the
    /// status and the one action are always visible, everything else is behind a
    /// heading. The marker is a typographic plus that loses its upright stroke,
    /// which suits the grid better than a rotating chevron.
    /// </summary>
    internal sealed class Section : StackPanel
    {
        private readonly TextBlock _title;
        private readonly TextBlock _summary;
        private readonly TextBlock _flag;
        private readonly Border _barH, _barV;
        private readonly StackPanel _body;
        private readonly SectionHeader _header;
        private readonly Border _rule;
        private bool _open, _everOpened;

        public event EventHandler FirstOpened;

        public Section(string title, string summary)
        {
            _title = Text.Section(title, Theme.Plane1Ink);
            _title.TextWrapping = TextWrapping.NoWrap;
            _title.VerticalAlignment = VerticalAlignment.Center;

            _flag = Text.MicroStrong(string.Empty, Theme.RedInk);
            _flag.TextWrapping = TextWrapping.NoWrap;
            _flag.VerticalAlignment = VerticalAlignment.Center;
            _flag.Margin = new Thickness(Theme.Space3, 0, 0, 0);
            _flag.Visibility = Visibility.Collapsed;

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(_title);
            titleRow.Children.Add(_flag);

            _summary = Text.Note(summary, Theme.Plane1Muted);
            _summary.TextWrapping = TextWrapping.NoWrap;
            _summary.VerticalAlignment = VerticalAlignment.Center;

            // A plus built from two filled bars: the upright one scales away when the
            // section opens, leaving a minus. No stroke, so no edge to lose.
            _barH = new Border { Width = 14, Height = 1.5, Background = Theme.Plane1Ink, SnapsToDevicePixels = true };
            _barV = new Border
            {
                Width = 1.5, Height = 14, Background = Theme.Plane1Ink, SnapsToDevicePixels = true,
                RenderTransform = new ScaleTransform(1, 1, 0.75, 7),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var marker = new Grid { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            marker.Children.Add(_barH);
            marker.Children.Add(_barV);

            Grid head = Layout.Columns();
            head.Height = Theme.SectionHead;
            Grid.SetColumn(titleRow, 0);
            Grid.SetColumnSpan(titleRow, 4);
            Grid.SetColumn(_summary, 4);
            Grid.SetColumnSpan(_summary, 7);
            Grid.SetColumn(marker, 11);
            head.Children.Add(titleRow);
            head.Children.Add(_summary);
            head.Children.Add(marker);

            _header = new SectionHeader
            {
                Child = head,
                Padding = new Thickness(Theme.GridMargin - 2, 0, Theme.GridMargin - 2, 0)
            };
            _header.Activated += delegate { IsOpen = !IsOpen; };

            _body = new StackPanel { Visibility = Visibility.Collapsed };
            _rule = new Border { Height = 1, Background = Theme.Plane1Hair, SnapsToDevicePixels = true };

            Children.Add(_rule);
            Children.Add(_header);
            Children.Add(_body);
        }

        public StackPanel Body { get { return _body; } }

        public string Summary { get { return _summary.Text; } set { _summary.Text = value ?? string.Empty; } }

        public string Flag
        {
            set
            {
                _flag.Text = value ?? string.Empty;
                _flag.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public bool IsOpen
        {
            get { return _open; }
            set
            {
                _open = value;
                _body.Visibility = value ? Visibility.Visible : Visibility.Collapsed;

                var scale = (ScaleTransform)_barV.RenderTransform;
                if (Theme.ReduceMotion)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleY = value ? 0 : 1;
                }
                else
                {
                    var anim = new System.Windows.Media.Animation.DoubleAnimation(value ? 0 : 1, Theme.Expand)
                    {
                        EasingFunction = Theme.Mech()
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }

                if (!value || _everOpened) return;
                _everOpened = true;
                EventHandler h = FirstOpened;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void Refresh()
        {
            _title.Foreground = Theme.Plane1Ink;
            _summary.Foreground = Theme.Plane1Muted;
            _flag.Foreground = Theme.RedInk;
            _barH.Background = Theme.Plane1Ink;
            _barV.Background = Theme.Plane1Ink;
            _rule.Background = Theme.Plane1Hair;
            _header.Refresh();
        }

        private sealed class SectionHeader : Pressable
        {
            protected override void Repaint()
            {
                PaintFocus();
                Background = Hovered ? Theme.Hover1 : Brushes.Transparent;
            }
        }
    }

    internal static class Layout
    {
        /// <summary>
        /// The 12-column grid every screen is built on. Columns are equal and the
        /// gutter is part of each column's right edge, so a child in column n always
        /// starts on the same line whatever it contains.
        /// </summary>
        public static Grid Columns()
        {
            var grid = new Grid();
            for (int i = 0; i < 12; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        public static void Place(UIElement element, int column, int span)
        {
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            var fe = element as FrameworkElement;
            if (fe != null && column + span < 12) fe.Margin = Pad(fe.Margin, Theme.Gutter);
        }

        private static Thickness Pad(Thickness t, double right)
        {
            return new Thickness(t.Left, t.Top, Math.Max(t.Right, right), t.Bottom);
        }

        public static Border Rule(Brush brush)
        {
            return new Border { Height = 1, Background = brush, SnapsToDevicePixels = true };
        }

        public static Brush BrushFor(CheckResult result)
        {
            switch (result)
            {
                case CheckResult.Pass: return Theme.RedInk;
                case CheckResult.Fail: return Theme.RedInk;
                default: return Theme.Plane1Muted;
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
