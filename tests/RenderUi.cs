// A developer tool, not part of the product: renders the window to PNG so the
// design can be looked at instead of imagined, and so a layout regression shows up
// as a picture rather than as a bug report.
//
// It lives in the test assembly because that already compiles the UI, and it uses
// the fixture as the machine state so the output is the same every time.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EULord.Core;
using EULord.Ui;

namespace EULord.Tests
{
    internal static class UiRenderer
    {
        /// <summary>Renders one PNG per language, collapsed and fully expanded.</summary>
        public static int Render(string repoRoot, string outputDir)
        {
            string sandbox = Path.Combine(Path.GetTempPath(), "eulord-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(sandbox, "system32"));
            Paths.OverrideCommonAppData(Path.Combine(sandbox, "programdata"));
            Paths.OverrideDataRoot(Path.Combine(sandbox, "data"));
            Paths.OverridePolicyFile(Path.Combine(sandbox, "system32", Paths.PolicyFileName));

            // Prefer this machine's real policy file, read-only, so the snapshot shows a
            // realistic table of 68 rows rather than the fixture's handful. Falls back to
            // the fixture wherever the real file cannot be read.
            string realPolicyFile = Path.Combine(Paths.SystemDirectory, Paths.PolicyFileName);
            string source;
            try { source = File.ReadAllText(realPolicyFile, Encoding.UTF8); }
            catch (Exception)
            {
                source = File.ReadAllText(Path.Combine(repoRoot, "tests", "fixtures", "policy-sample.json"), Encoding.UTF8);
            }
            File.WriteAllText(Paths.PolicyFile, source, new UTF8Encoding(false));

            Directory.CreateDirectory(outputDir);
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            application.DispatcherUnhandledException += (s, e) =>
            {
                Console.Error.WriteLine("   !! dispatcher: " + e.Exception.GetType().FullName + ": " + e.Exception.Message);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Console.Error.WriteLine("   !! appdomain: " + (ex == null ? "non-exception" : ex.GetType().FullName + ": " + ex.Message));
            };

            try
            {
                // A curated set rather than every combination: both themes of the
                // primary language, the long view, and one shot of each other language
                // so a translation that overflows its column shows up.
                var shots = new[]
                {
                    new Shot(Lang.ZhHant, ThemeMode.Light, false),
                    new Shot(Lang.ZhHant, ThemeMode.Dark, false),
                    new Shot(Lang.ZhHant, ThemeMode.Light, true),
                    new Shot(Lang.ZhHant, ThemeMode.Dark, true),
                    new Shot(Lang.ZhHans, ThemeMode.Light, false),
                    new Shot(Lang.En, ThemeMode.Light, false),
                    new Shot(Lang.En, ThemeMode.Dark, true)
                };

                int failures = 0;
                foreach (Shot shot in shots)
                {
                    S.Current = shot.Lang;
                    Theme.Mode = shot.Mode;
                    try { Shoot(shot, outputDir); }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.Error.WriteLine("render failed for " + shot.Lang + " " + shot.Mode +
                                                " expanded=" + shot.Expanded +
                                                " -- " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
                return failures == 0 ? 0 : 1;
            }
            finally
            {
                try { application.Shutdown(); } catch (Exception) { }
                try { Directory.Delete(sandbox, true); } catch (IOException) { }
            }
        }

        /// <summary>One snapshot: a language, a theme, and whether the sections are open.</summary>
        private sealed class Shot
        {
            public readonly Lang Lang;
            public readonly ThemeMode Mode;
            public readonly bool Expanded;
            public Shot(Lang lang, ThemeMode mode, bool expanded) { Lang = lang; Mode = mode; Expanded = expanded; }
        }

        private static void Shoot(Shot shot, string outputDir)
        {
            var state = new State();
            state.SetOverride("Copilot", PolicyOverride.Eea);
            state.SetOverride("OfficeRecent", PolicyOverride.Eea);

            MainWindow.PinTheme = true;   // render the theme this shot asks for, not the machine's
            var window = new MainWindow(state);
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            // Off to the side rather than invisible: WPF needs a real window handle to
            // lay out, and Visibility.Hidden gives a blank render.
            window.Left = -4000;
            window.Top = -4000;
            window.Show();

            if (shot.Expanded)
            {
                window.OpenEverySection();
                window.UpdateLayout();
                Pump();
            }

            window.UpdateLayout();
            Pump();

            FrameworkElement title = window.TitleVisual;
            FrameworkElement page = window.PageVisual;

            // The window itself is clamped to the screen, so the page is rendered at
            // the full height the scroll viewer arranged it to, with the title bar we
            // draw ourselves stacked above it.
            Thickness margin = page.Margin;
            int width = (int)Math.Ceiling(Math.Max(title.ActualWidth, page.ActualWidth + margin.Left + margin.Right));
            int titleHeight = (int)Math.Ceiling(title.ActualHeight);
            int height = titleHeight + (int)Math.Ceiling(page.ActualHeight + margin.Top + margin.Bottom);

            var composed = new DrawingVisual();
            using (DrawingContext dc = composed.RenderOpen())
            {
                dc.DrawRectangle(Theme.Ground, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(
                    new VisualBrush(title) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(0, 0, title.ActualWidth, title.ActualHeight));
                dc.DrawRectangle(
                    new VisualBrush(page) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null,
                    new Rect(margin.Left, titleHeight + margin.Top, page.ActualWidth, page.ActualHeight));
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(composed);

            string tag = shot.Lang == Lang.ZhHant ? "zh-Hant" : shot.Lang == Lang.ZhHans ? "zh-Hans" : "en";
            string name = string.Format(CultureInfo.InvariantCulture, "ui-{0}-{1}-{2}.png",
                tag, shot.Mode == ThemeMode.Dark ? "dark" : "light",
                shot.Expanded ? "expanded" : "collapsed");
            string path = Path.Combine(outputDir, name);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);

            Console.WriteLine("rendered  " + path + "  " + target.PixelWidth + "x" + target.PixelHeight);
            window.Close();
            Pump();
        }

        /// <summary>Lets the dispatcher finish layout and rendering before the snapshot.</summary>
        private static void Pump()
        {
            for (int i = 0; i < 4; i++)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
        }
    }
}
