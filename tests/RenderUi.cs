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
                int failures = 0;
                foreach (Lang lang in new[] { Lang.ZhHant, Lang.ZhHans, Lang.En })
                {
                    S.Current = lang;
                    foreach (bool expanded in new[] { false, true })
                    {
                        try { Shoot(lang, expanded, outputDir); }
                        catch (Exception ex)
                        {
                            failures++;
                            Console.Error.WriteLine("render failed for " + lang + " expanded=" + expanded +
                                                    " -- " + ex.GetType().Name + ": " + ex.Message);
                        }
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

        private static void Shoot(Lang lang, bool expanded, string outputDir)
        {
            var state = new State();
            state.SetOverride("Copilot", PolicyOverride.Eea);
            state.SetOverride("OfficeRecent", PolicyOverride.Eea);

            var window = new MainWindow(state);
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            // Off to the side rather than invisible: WPF needs a real window handle to
            // lay out, and Visibility.Hidden gives a blank render.
            window.Left = -4000;
            window.Top = -4000;
            window.Show();

            if (expanded)
            {
                window.OpenEverySection();
                window.UpdateLayout();
                Pump();
            }

            window.UpdateLayout();
            Pump();

            FrameworkElement page = window.PageVisual;

            // ActualWidth excludes the page's own margin, which is the outer grid, so
            // add it back or the snapshot shows the content jammed against the edge.
            Thickness margin = page.Margin;
            int width = (int)Math.Ceiling(page.ActualWidth + margin.Left + margin.Right);
            int height = (int)Math.Ceiling(page.ActualHeight + margin.Top + margin.Bottom);

            // The page has no background of its own, so paint the paper first and then
            // stamp the page over it.
            var composed = new DrawingVisual();
            using (DrawingContext dc = composed.RenderOpen())
            {
                dc.DrawRectangle(Theme.Paper, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(
                    new VisualBrush(page) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null,
                    new Rect(margin.Left, margin.Top, page.ActualWidth, page.ActualHeight));
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(composed);

            string tag = lang == Lang.ZhHant ? "zh-Hant" : lang == Lang.ZhHans ? "zh-Hans" : "en";
            string name = string.Format(CultureInfo.InvariantCulture, "ui-{0}-{1}.png",
                tag, expanded ? "expanded" : "collapsed");
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
