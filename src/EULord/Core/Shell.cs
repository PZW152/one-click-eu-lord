// Running a child process and getting its exit code and output back, without
// deadlocking on the pipes.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace EULord.Core
{
    internal sealed class ProcessResult
    {
        public int ExitCode;
        public string StdOut = string.Empty;
        public string StdErr = string.Empty;
        public bool TimedOut;

        public bool Ok { get { return !TimedOut && ExitCode == 0; } }

        /// <summary>One line describing the failure, for the log and for the UI.</summary>
        public string Failure
        {
            get
            {
                if (TimedOut) return "the command did not finish in time";
                string text = StdErr.Trim();
                if (text.Length == 0) text = StdOut.Trim();
                if (text.Length > 300) text = text.Substring(0, 300) + "...";
                return "exit code " + ExitCode + (text.Length > 0 ? ": " + text.Replace("\r", " ").Replace("\n", " ") : string.Empty);
            }
        }
    }

    internal static class Shell
    {
        public static string System32(string fileName)
        {
            return Path.Combine(Paths.SystemDirectory, fileName);
        }

        /// <summary>
        /// Runs a program with output captured. Reads both pipes concurrently, because a
        /// child that fills stderr while we block on stdout would hang forever.
        /// </summary>
        public static ProcessResult Run(string fileName, string arguments, int timeoutMs)
        {
            var result = new ProcessResult();
            var psi = new ProcessStartInfo(fileName)
            {
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        result.TimedOut = true;
                        try { process.Kill(); } catch (Exception) { }
                        result.ExitCode = -1;
                    }
                    else
                    {
                        // Lets the async readers drain before we look at the buffers.
                        process.WaitForExit();
                        result.ExitCode = process.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not run " + Path.GetFileName(fileName), ex);
                result.ExitCode = -1;
                stderr.AppendLine(Log.Describe(ex));
            }

            result.StdOut = stdout.ToString();
            result.StdErr = stderr.ToString();
            return result;
        }

        /// <summary>
        /// Runs a PowerShell command. Used only for the documented Appx APIs, which have
        /// no other dependency-free route from .NET Framework.
        /// </summary>
        public static ProcessResult PowerShell(string command, int timeoutMs)
        {
            string exe = Path.Combine(Paths.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            return Run(exe, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded, timeoutMs);
        }

        /// <summary>
        /// Starts a process the user is meant to interact with -- a vendor's own
        /// uninstaller -- and waits for it. Shell execution so a registered command
        /// string with its own quoting works as written.
        /// </summary>
        public static ProcessResult RunInteractive(string fileName, string arguments, int timeoutMs)
        {
            var result = new ProcessResult();
            var psi = new ProcessStartInfo(fileName)
            {
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            };

            try
            {
                using (Process process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        result.ExitCode = -1;
                        result.StdErr = "the uninstaller did not start";
                        return result;
                    }
                    if (!process.WaitForExit(timeoutMs))
                    {
                        result.TimedOut = true;
                        result.ExitCode = -1;
                        return result;
                    }
                    result.ExitCode = process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.StdErr = Log.Describe(ex);
            }
            return result;
        }

        /// <summary>Splits a registered UninstallString into a program and its arguments.</summary>
        public static void SplitCommand(string command, out string fileName, out string arguments)
        {
            fileName = command ?? string.Empty;
            arguments = string.Empty;
            if (string.IsNullOrEmpty(command)) return;

            // After trimming, not before: a registry value holding only spaces is empty
            // here, and indexing it would throw rather than report "no uninstaller".
            command = command.Trim();
            if (command.Length == 0) { fileName = string.Empty; return; }

            if (command[0] == '"')
            {
                int close = command.IndexOf('"', 1);
                if (close > 0)
                {
                    fileName = command.Substring(1, close - 1);
                    arguments = command.Substring(close + 1).Trim();
                    return;
                }
            }

            // Unquoted. Split after the executable's extension, which handles both
            // "MsiExec.exe /X{...}" and "C:\...\OneDriveSetup.exe /uninstall"; an
            // unquoted path containing spaces has no unambiguous reading anyway.
            int cut = command.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
            if (cut >= 0)
            {
                fileName = command.Substring(0, cut + 4);
                arguments = command.Substring(cut + 5).Trim();
                return;
            }

            int space = command.IndexOf(' ');
            if (space > 0)
            {
                fileName = command.Substring(0, space);
                arguments = command.Substring(space + 1).Trim();
            }
        }
    }
}
