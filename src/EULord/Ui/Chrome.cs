// The window frame, drawn by us.
//
// The native Windows caption bar is replaced, but through WindowChrome rather than
// WindowStyle.None: that keeps everything people expect a window to do -- Aero
// Snap, drag to the top to maximise, double-click the caption, the taskbar
// thumbnail, the Alt+Space menu -- while we paint the bar itself. Turning the
// frame off wholesale wins the look and loses all of that.
//
// AllowsTransparency is deliberately not used. It would force software rendering
// for the whole window, and nothing here needs a non-rectangular shape: the
// design is square on purpose.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using EULord.Core;

namespace EULord.Ui
{
    internal sealed class TitleBar : Border
    {
        private readonly TextBlock _name;
        private readonly TextBlock _subtitle;
        private readonly Border _mark;
        private readonly StackPanel _languages;
        private readonly WindowButton _minimise, _maximise, _close;
        private readonly Window _window;

        /// <summary>Raised when the viewer picks a language, with its tag.</summary>
        public event EventHandler<string> LanguagePicked;

        public TitleBar(Window window)
        {
            _window = window;

            _mark = new Border { Width = 10, Height = 10, Background = Theme.Red, SnapsToDevicePixels = true };

            _name = Text.MicroStrong(S.AppName, Theme.GroundInk);
            _name.TextWrapping = TextWrapping.NoWrap;
            _name.Margin = new Thickness(Theme.Space2, 0, 0, 0);

            _subtitle = Text.Micro(S.AppNameRoman + "　" + Build.Version, Theme.GroundMuted);
            _subtitle.TextWrapping = TextWrapping.NoWrap;

            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            left.Children.Add(_mark);
            left.Children.Add(_name);

            _subtitle.VerticalAlignment = VerticalAlignment.Center;

            _languages = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, Theme.Space4, 0)
            };

            _minimise = new WindowButton(WindowGlyph.Minimise, S.Minimise);
            _maximise = new WindowButton(WindowGlyph.Maximise, S.Maximise);
            _close = new WindowButton(WindowGlyph.Close, S.Close) { IsClose = true };

            _minimise.Activated += delegate { _window.WindowState = WindowState.Minimized; };
            _maximise.Activated += delegate { ToggleMaximised(); };
            _close.Activated += delegate { _window.Close(); };

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            right.Children.Add(_languages);
            right.Children.Add(_minimise);
            right.Children.Add(_maximise);
            right.Children.Add(_close);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(left, 0);
            Grid.SetColumn(_subtitle, 1);
            Grid.SetColumn(right, 2);
            _subtitle.Margin = new Thickness(Theme.Space4, 0, 0, 0);
            grid.Children.Add(left);
            grid.Children.Add(_subtitle);
            grid.Children.Add(right);

            Child = grid;
            Height = Theme.Titlebar;
            Padding = new Thickness(Theme.GridMargin, 0, 0, 0);
            Background = Brushes.Transparent;

            // The buttons must be clickable; the rest of the bar stays caption, so
            // dragging and double-clicking it behave natively.
            WindowChrome.SetIsHitTestVisibleInChrome(right, true);
        }

        public void SetLanguages(Lang current)
        {
            _languages.Children.Clear();

            var options = new[]
            {
                new KeyValuePair<string, Lang>("zh-Hant", Lang.ZhHant),
                new KeyValuePair<string, Lang>("zh-Hans", Lang.ZhHans),
                new KeyValuePair<string, Lang>("en", Lang.En)
            };
            var captions = new[] { "繁中", "简中", "EN" };

            for (int i = 0; i < options.Length; i++)
            {
                string tag = options[i].Key;
                bool on = options[i].Value == current;
                var tab = new LanguageTab(captions[i], on);
                tab.Activated += delegate
                {
                    EventHandler<string> h = LanguagePicked;
                    if (h != null && !on) h(this, tag);
                };
                _languages.Children.Add(tab);
            }
        }

        private void ToggleMaximised()
        {
            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
            _maximise.SetGlyph(_window.WindowState == WindowState.Maximized
                ? WindowGlyph.Restore : WindowGlyph.Maximise);
        }

        public void Refresh()
        {
            _mark.Background = Theme.Red;
            _name.Foreground = Theme.GroundInk;
            _name.Text = S.AppName;
            _subtitle.Foreground = Theme.GroundMuted;
            _subtitle.Text = S.AppNameRoman + "　" + Build.Version;
            _minimise.Refresh();
            _maximise.Refresh();
            _close.Refresh();
            foreach (object child in _languages.Children)
            {
                var tab = child as LanguageTab;
                if (tab != null) tab.Refresh();
            }
        }
    }

    internal enum WindowGlyph { Minimise, Maximise, Restore, Close }

    /// <summary>
    /// A caption button. The glyphs are drawn rather than typed: the symbol font
    /// Windows uses for these is not guaranteed, and a missing glyph box in the
    /// title bar is the first thing anyone would notice.
    /// </summary>
    internal sealed class WindowButton : Pressable
    {
        private readonly Path _glyph;
        public bool IsClose;

        public WindowButton(WindowGlyph glyph, string name)
        {
            _glyph = new Path
            {
                Stroke = Theme.GroundInk,
                StrokeThickness = 1,
                Fill = null,
                SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 10,
                Height = 10,
                Stretch = Stretch.None
            };
            SetGlyph(glyph);

            Child = _glyph;
            Width = Theme.WindowButton;
            Height = Theme.Titlebar;
            Cursor = Cursors.Arrow;
            SetValue(System.Windows.Automation.AutomationProperties.NameProperty, name);
            Repaint();
        }

        public void SetGlyph(WindowGlyph glyph)
        {
            switch (glyph)
            {
                case WindowGlyph.Minimise:
                    _glyph.Data = Geometry.Parse("M 0,5.5 L 10,5.5");
                    break;
                case WindowGlyph.Maximise:
                    _glyph.Data = Geometry.Parse("M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z");
                    break;
                case WindowGlyph.Restore:
                    _glyph.Data = Geometry.Parse("M 0.5,2.5 L 7.5,2.5 L 7.5,9.5 L 0.5,9.5 Z M 2.5,2.5 L 2.5,0.5 L 9.5,0.5 L 9.5,7.5 L 7.5,7.5");
                    break;
                default:
                    _glyph.Data = Geometry.Parse("M 0.5,0.5 L 9.5,9.5 M 9.5,0.5 L 0.5,9.5");
                    break;
            }
        }

        protected override void Repaint()
        {
            if (_glyph == null) return;
            PaintFocus();

            bool lit = Hovered || Pressed;
            if (IsClose && lit)
            {
                Background = Theme.CloseHover;
                _glyph.Stroke = Theme.OnRed;
                return;
            }

            Background = lit ? Theme.Hover1 : Brushes.Transparent;
            _glyph.Stroke = Theme.GroundInk;
        }
    }

    /// <summary>A language in the title bar. The current one is marked, never disabled.</summary>
    internal sealed class LanguageTab : Pressable
    {
        private readonly TextBlock _label;
        private readonly Border _underline;
        private readonly bool _on;

        public LanguageTab(string caption, bool on)
        {
            _on = on;

            _label = Text.Micro(caption, on ? Theme.GroundInk : Theme.GroundMuted);
            _label.FontWeight = FontWeight.FromOpenTypeWeight(on ? 700 : 400);
            _label.TextWrapping = TextWrapping.NoWrap;
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.HorizontalAlignment = HorizontalAlignment.Center;

            _underline = new Border
            {
                Height = 2,
                Background = on ? Theme.GroundInk : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 8),
                SnapsToDevicePixels = true
            };

            var grid = new Grid();
            grid.Children.Add(_label);
            grid.Children.Add(_underline);

            Child = grid;
            Height = Theme.Titlebar;
            Padding = new Thickness(Theme.Space3 - 2, 0, Theme.Space3 - 2, 0);
            Cursor = _on ? Cursors.Arrow : Cursors.Hand;
            Repaint();
        }

        protected override void Repaint()
        {
            if (_label == null) return;
            PaintFocus();

            _label.Foreground = _on ? Theme.GroundInk
                             : ((Hovered || Pressed) ? Theme.GroundInk : Theme.GroundMuted);
            _underline.Background = _on ? Theme.GroundInk : Brushes.Transparent;
            Background = (!_on && Hovered) ? Theme.Hover1 : Brushes.Transparent;
        }
    }

    internal static class Chrome
    {
        /// <summary>
        /// Replaces the caption bar while keeping the window a real window. The
        /// caption height matches the bar we draw, so dragging and double-clicking
        /// land where they look like they should.
        /// </summary>
        public static void Apply(Window window)
        {
            var chrome = new WindowChrome
            {
                CaptionHeight = Theme.Titlebar,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false,
                NonClientFrameEdges = NonClientFrameEdges.None
            };
            WindowChrome.SetWindowChrome(window, chrome);

            window.WindowStyle = WindowStyle.SingleBorderWindow;
            window.ResizeMode = ResizeMode.CanResize;
            window.AllowsTransparency = false;

            // Maximised, a WindowChrome window would otherwise spill its resize border
            // over the taskbar edge.
            window.StateChanged += delegate
            {
                window.BorderThickness = window.WindowState == WindowState.Maximized
                    ? new Thickness(8) : new Thickness(0);
            };
        }
    }
}
