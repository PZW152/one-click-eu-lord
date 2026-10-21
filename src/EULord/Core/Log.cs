// The log is always English and always Gregorian, whatever the UI language is.
//
// Two reasons. A user reporting a problem should be able to paste the log into an
// issue that anyone can read; and on a zh-TW machine the default calendar is the
// Minguo one, so DateTime formatting without InvariantCulture produces "115-10-20"
// for 2026. v1 shipped that bug.

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace EULord.Core
{
    internal enum LogLevel { Info, Warn, Error }

    internal static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly object _gate = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Set for the headless CLI so progress also reaches the console.</summary>
        public static bool EchoToConsole;

        public static void Info(string message) { Write(LogLevel.Info, message); }
        public static void Warn(string message) { Write(LogLevel.Warn, message); }
        public static void Error(string message) { Write(LogLevel.Error, message); }

        public static void Error(string message, Exception ex)
        {
            Write(LogLevel.Error, message + " -- " + Describe(ex));
        }

        /// <summary>An exception as one log-safe line: type and message, no stack, no paths beyond ours.</summary>
        public static string Describe(Exception ex)
        {
            if (ex == null) return "(no exception)";
            string text = ex.GetType().Name + ": " + Collapse(ex.Message);
            if (ex.InnerException != null)
                text += " <- " + ex.InnerException.GetType().Name + ": " + Collapse(ex.InnerException.Message);
            return text;
        }

        private static string Collapse(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        public static string Stamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static void Write(LogLevel level, string message)
        {
            string line = "[" + Stamp() + "] " + level.ToString().ToUpperInvariant() + " " + Collapse(message);

            if (EchoToConsole)
            {
                // Always stderr, never stdout. stdout belongs to the command's own
                // output: with progress lines mixed in, `--status --json` produced a
                // stream no parser would accept, which silently broke any script
                // reading it.
                try { Console.Error.WriteLine(line); }
                catch (IOException) { }
            }

            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(Paths.LogDir);
                    RotateIfLarge();
                    File.AppendAllText(Paths.LogFile, line + Environment.NewLine, Utf8NoBom);
                }
                catch (Exception)
                {
                    // Logging must never be the reason an operation fails.
                }
            }
        }

        private static void RotateIfLarge()
        {
            var info = new FileInfo(Paths.LogFile);
            if (!info.Exists || info.Length < MaxBytes) return;

            string previous = Paths.LogFile + ".1";
            try
            {
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(Paths.LogFile, previous);
            }
            catch (IOException) { }
        }

        /// <summary>Marks the start of a run so interleaved entries can be told apart.</summary>
        public static void Banner(string version, string invocation)
        {
            Write(LogLevel.Info, "---- EULord " + version + " | " + invocation +
                                 " | elevated=" + Native.IsElevated() +
                                 " | os=" + Environment.OSVersion.Version +
                                 " | exe=" + Paths.CurrentExe);
        }
    }
}
