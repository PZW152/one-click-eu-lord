// Entry point. No arguments opens the window; any argument runs headless.

using System;
using System.Windows;
using System.Windows.Threading;
using EULord.Core;
using EULord.Ui;

namespace EULord
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Options options = Cli.Parse(args ?? new string[0]);

            // Where our files live has to be settled before anything reads or writes one.
            Paths.OverrideDataRoot(options.DataDir);
            Paths.OverridePolicyFile(options.PolicyFile);

            if (!options.WantsGui)
            {
                Cli.AttachToParentConsole();
                try
                {
                    return Cli.Run(options);
                }
                catch (Exception ex)
                {
                    Log.Error("The command failed", ex);
                    try { Console.Error.WriteLine(Log.Describe(ex)); } catch (Exception) { }
                    return ExitCodes.Failed;
                }
            }

            return RunGui(options);
        }

        private static int RunGui(Options options)
        {
            State state = StateStore.Load();
            if (!string.IsNullOrEmpty(options.Language)) state.LanguagePreference = options.Language;

            S.Current = ResolveLanguage(state.LanguagePreference);

            Log.Banner(Build.Version, "gui");
            Log.Info("Language: " + S.Current + " (preference " + state.LanguagePreference + ")");

            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            application.DispatcherUnhandledException += OnUnhandledUiException;

            try
            {
                var window = new MainWindow(state);
                application.Run(window);
                return ExitCodes.Ok;
            }
            catch (Exception ex)
            {
                Log.Error("The window could not be opened", ex);
                ShowFatal(ex);
                return ExitCodes.Failed;
            }
        }

        internal static Lang ResolveLanguage(string preference)
        {
            if (!string.IsNullOrEmpty(preference) &&
                !string.Equals(preference, "auto", StringComparison.OrdinalIgnoreCase))
            {
                Lang explicitChoice;
                if (S.TryMatch(preference, out explicitChoice)) return explicitChoice;
                if (preference.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return Lang.En;
            }
            return S.Detect();
        }

        /// <summary>
        /// Keeps a UI-thread fault from closing the window silently. The log already has
        /// the detail; the dialog tells the user where to look.
        /// </summary>
        private static void OnUnhandledUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Unhandled exception on the UI thread", e.Exception);
            e.Handled = true;
            ShowFatal(e.Exception);
        }

        private static void ShowFatal(Exception ex)
        {
            try
            {
                MessageBox.Show(
                    Log.Describe(ex) + "\n\n" + Paths.LogFile,
                    S.AppName + " " + Build.Version,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception) { }
        }
    }
}
