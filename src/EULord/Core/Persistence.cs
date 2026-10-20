// Making the change permanent.
//
// Windows restores IntegratedServicesRegionPolicySet.json during servicing, so a
// one-off rewrite is undone by the next cumulative update. A scheduled task running
// as SYSTEM re-applies the saved plan on three triggers:
//
//   logon         covers every reboot
//   daily         covers anything that quietly reset the file in between
//   update event  Microsoft-Windows-WindowsUpdateClient event 19, "installation
//                 successful", so the file is put back minutes after an update
//                 rather than at the next sign-in
//
// No resident service and no driver. The task runs, re-applies, and exits.
//
// Task state is read from the task's own XML under System32\Tasks rather than by
// parsing schtasks output, which is localized -- on a zh-TW machine the field names
// come back in Chinese.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace EULord.Core
{
    internal enum PersistenceStatus
    {
        Absent,
        /// <summary>Registered, enabled, and pointing at the installed copy.</summary>
        Ready,
        /// <summary>Registered but switched off, so it will not re-apply.</summary>
        Disabled,
        /// <summary>Registered but its command no longer matches; needs reinstalling.</summary>
        Stale,
        Unknown
    }

    internal static class Persistence
    {
        private const string WindowsUpdateProvider = "Microsoft-Windows-WindowsUpdateClient";
        private const int InstallationSuccessfulEventId = 19;

        private static string TaskDefinitionPath(string taskName)
        {
            return Path.Combine(Paths.SystemDirectory, "Tasks", taskName);
        }

        public static PersistenceStatus Status()
        {
            string path = TaskDefinitionPath(Paths.TaskName);
            if (!File.Exists(path)) return PersistenceStatus.Absent;

            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.Load(path);

                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");

                XmlNode enabled = doc.SelectSingleNode("/t:Task/t:Settings/t:Enabled", ns);
                if (enabled != null && string.Equals(enabled.InnerText.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                    return PersistenceStatus.Disabled;

                XmlNode command = doc.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Command", ns);
                string wanted = Paths.InstalledExe;
                if (command == null ||
                    !string.Equals(command.InnerText.Trim().Trim('"'), wanted, StringComparison.OrdinalIgnoreCase))
                    return PersistenceStatus.Stale;

                if (!File.Exists(wanted)) return PersistenceStatus.Stale;

                return PersistenceStatus.Ready;
            }
            catch (Exception ex)
            {
                Log.Warn("The scheduled task definition could not be read -- " + Log.Describe(ex));
                return PersistenceStatus.Unknown;
            }
        }

        /// <summary>
        /// Copies this exe into the data folder so the task has a stable target. The copy
        /// is what the user can then delete or move the downloaded file without breaking
        /// anything.
        /// </summary>
        public static bool InstallSelf()
        {
            try
            {
                Paths.EnsureDataLayout();
                string source = Paths.CurrentExe;
                string target = Paths.InstalledExe;

                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    return true;

                if (File.Exists(target) && SystemFile.SameContent(source, target)) return true;

                File.Copy(source, target, true);
                Log.Info("Installed a copy of the executable at " + target);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("The executable could not be copied into the data folder", ex);
                return false;
            }
        }

        public static bool Install()
        {
            if (!InstallSelf()) return false;

            string xmlPath = Path.Combine(Path.GetTempPath(), "eul_task_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                // schtasks requires the definition as UTF-16.
                File.WriteAllText(xmlPath, BuildTaskXml(), Encoding.Unicode);

                int exit = RunSchtasks("/Create /TN \"" + Paths.TaskName + "\" /XML \"" + xmlPath + "\" /F");
                if (exit != 0)
                {
                    Log.Error("Registering the scheduled task failed with exit code " + exit);
                    return false;
                }

                RemoveLegacyTasks();

                PersistenceStatus status = Status();
                Log.Info("Scheduled task " + Paths.TaskName + " registered; status=" + status);
                return status == PersistenceStatus.Ready;
            }
            catch (Exception ex)
            {
                Log.Error("Registering the scheduled task failed", ex);
                return false;
            }
            finally
            {
                try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch (IOException) { }
            }
        }

        public static bool Remove()
        {
            bool ok = DeleteTask(Paths.TaskName);
            RemoveLegacyTasks();
            return ok;
        }

        /// <summary>
        /// Deletes the v1.0-v1.2 tasks. They must go: they re-apply v1's behaviour, which
        /// copied the EEA set wholesale, and would fight this one every time they ran.
        /// </summary>
        public static void RemoveLegacyTasks()
        {
            foreach (string name in Paths.LegacyTaskNames)
            {
                if (!File.Exists(TaskDefinitionPath(name))) continue;
                if (DeleteTask(name)) Log.Info("Removed the superseded task " + name);
            }
        }

        private static bool DeleteTask(string name)
        {
            if (!File.Exists(TaskDefinitionPath(name))) return true;
            int exit = RunSchtasks("/Delete /TN \"" + name + "\" /F");
            if (exit != 0) Log.Warn("Deleting the task " + name + " returned exit code " + exit);
            return !File.Exists(TaskDefinitionPath(name));
        }

        private static int RunSchtasks(string arguments)
        {
            var psi = new ProcessStartInfo(Path.Combine(Paths.SystemDirectory, "schtasks.exe"))
            {
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(psi))
            {
                if (process == null) return -1;
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30000))
                {
                    Log.Error("schtasks did not finish within 30 seconds");
                    return -1;
                }
                if (process.ExitCode != 0)
                    Log.Warn("schtasks: " + (stderr.Trim().Length > 0 ? stderr.Trim() : stdout.Trim()));
                return process.ExitCode;
            }
        }

        internal static string BuildTaskXml()
        {
            // A fixed, past start boundary with a daily interval: the first run is the
            // next 03:00 local time. InvariantCulture, or a zh-TW machine writes a
            // Minguo year and the schema rejects it.
            string start = DateTime.Today.AddDays(-1).AddHours(3)
                .ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

            string subscription =
                "<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">" +
                "*[System[Provider[@Name='" + WindowsUpdateProvider + "'] and (EventID=" +
                InstallationSuccessfulEventId.ToString(CultureInfo.InvariantCulture) + ")]]" +
                "</Select></Query></QueryList>";

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n");
            sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n");
            sb.Append("  <RegistrationInfo>\n");
            sb.Append("    <Author>One-Click EU Lord</Author>\n");
            sb.Append("    <Description>Re-applies the One-Click EU Lord policy set after Windows updates, at logon and once a day. Created by EULord.exe; delete it with the tool's Restore button.</Description>\n");
            sb.Append("    <URI>\\").Append(Esc(Paths.TaskName)).Append("</URI>\n");
            sb.Append("  </RegistrationInfo>\n");
            sb.Append("  <Triggers>\n");
            sb.Append("    <LogonTrigger><Enabled>true</Enabled><Delay>PT1M</Delay></LogonTrigger>\n");
            sb.Append("    <CalendarTrigger>\n");
            sb.Append("      <StartBoundary>").Append(start).Append("</StartBoundary>\n");
            sb.Append("      <Enabled>true</Enabled>\n");
            sb.Append("      <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>\n");
            sb.Append("    </CalendarTrigger>\n");
            sb.Append("    <EventTrigger>\n");
            sb.Append("      <Enabled>true</Enabled>\n");
            sb.Append("      <Delay>PT2M</Delay>\n");
            sb.Append("      <Subscription>").Append(Esc(subscription)).Append("</Subscription>\n");
            sb.Append("    </EventTrigger>\n");
            sb.Append("  </Triggers>\n");
            sb.Append("  <Principals>\n");
            sb.Append("    <Principal id=\"Author\">\n");
            sb.Append("      <UserId>S-1-5-18</UserId>\n");          // NT AUTHORITY\SYSTEM
            sb.Append("      <RunLevel>HighestAvailable</RunLevel>\n");
            sb.Append("    </Principal>\n");
            sb.Append("  </Principals>\n");
            sb.Append("  <Settings>\n");
            sb.Append("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n");
            sb.Append("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n");
            sb.Append("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n");
            sb.Append("    <AllowHardTerminate>true</AllowHardTerminate>\n");
            sb.Append("    <StartWhenAvailable>true</StartWhenAvailable>\n");
            sb.Append("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n");
            sb.Append("    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\n");
            sb.Append("    <AllowStartOnDemand>true</AllowStartOnDemand>\n");
            sb.Append("    <Enabled>true</Enabled>\n");
            sb.Append("    <Hidden>false</Hidden>\n");
            sb.Append("    <RunOnlyIfIdle>false</RunOnlyIfIdle>\n");
            sb.Append("    <WakeToRun>false</WakeToRun>\n");
            sb.Append("    <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>\n");
            sb.Append("    <Priority>7</Priority>\n");
            sb.Append("  </Settings>\n");
            sb.Append("  <Actions Context=\"Author\">\n");
            sb.Append("    <Exec>\n");
            sb.Append("      <Command>").Append(Esc(Paths.InstalledExe)).Append("</Command>\n");
            sb.Append("      <Arguments>--reapply --quiet</Arguments>\n");
            sb.Append("    </Exec>\n");
            sb.Append("  </Actions>\n");
            sb.Append("</Task>\n");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
