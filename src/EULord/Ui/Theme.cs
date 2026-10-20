// Design tokens.
//
// Swiss International Typographic Style, as asked for: a strict grid, one
// grotesque typeface doing all the work through size and weight, flush-left
// ragged-right text, black on white, and a single accent -- signal red. No
// gradients, no shadows, no rounded corners, no ornament. Hairline rules and
// tabular alignment carry the structure.
//
// Arial is the typeface because it is the metric-compatible grotesque that is on
// every Windows 11 machine; Segoe UI is humanist and would read as "the Windows
// default" rather than a choice. Arial Black handles the one display numeral.
//
// Every widget here is composed from Border and TextBlock rather than restyled
// WPF controls, because a default Button or CheckBox reasserts its own chrome on
// hover whatever the properties say, and the point of this file is exact control.

using System;
using System.Windows;
using System.Windows.Media;

namespace EULord.Ui
{
    internal static class Theme
    {
        // -- palette ----------------------------------------------------------
        public static readonly Brush Paper = Freeze(Rgb(0xFF, 0xFF, 0xFF));
        public static readonly Brush Ink = Freeze(Rgb(0x00, 0x00, 0x00));
        public static readonly Brush Mute = Freeze(Rgb(0x6E, 0x6E, 0x6E));
        public static readonly Brush Faint = Freeze(Rgb(0x9B, 0x9B, 0x9B));
        public static readonly Brush Rule = Freeze(Rgb(0xDC, 0xDC, 0xDC));
        public static readonly Brush Field = Freeze(Rgb(0xF5, 0xF5, 0xF5));

        /// <summary>The single accent. Signal red, as used in Swiss poster work.</summary>
        public static readonly Brush Red = Freeze(Rgb(0xE2, 0x00, 0x1A));
        public static readonly Brush RedDark = Freeze(Rgb(0xA3, 0x00, 0x13));
        public static readonly Brush RedFaint = Freeze(Rgb(0xFD, 0xE8, 0xEA));

        // -- type -------------------------------------------------------------
        public static readonly FontFamily Sans = new FontFamily("Arial, Helvetica, Segoe UI");
        public static readonly FontFamily Display = new FontFamily("Arial Black, Arial Bold, Arial");

        public const double SizeDisplay = 46;
        public const double SizeHead = 20;
        public const double SizeLead = 15;
        public const double SizeBody = 13;
        public const double SizeMicro = 11;

        // -- grid -------------------------------------------------------------
        public const double Margin = 44;
        public const double Gutter = 28;
        public const double RowGap = 6;
        public const double LabelColumn = 104;
        public const double HeroColumn = 210;

        public const double WindowWidth = 940;
        public const double WindowHeight = 720;
        public const double WindowMinWidth = 880;
        public const double WindowMinHeight = 560;

        private static Color Rgb(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>A hairline horizontal rule. Thickness 1 for structure, 2 for the masthead.</summary>
        public static System.Windows.Controls.Border HairRule(double thickness, Brush brush, double topMargin, double bottomMargin)
        {
            return new System.Windows.Controls.Border
            {
                Height = thickness,
                Background = brush,
                Margin = new Thickness(0, topMargin, 0, bottomMargin),
                SnapsToDevicePixels = true
            };
        }
    }
}
