// Uninstalling Microsoft's own apps, under one hard rule the user set:
//
//     "卸載 app 要走 app 自己的卸載程序，不然我怕刪掉了系統必要的組件"
//     Go through the app's own uninstaller, or risk deleting components the
//     system needs.
//
// So there are exactly two routes, both of them Microsoft's:
//
//   Appx        Remove-AppxPackage, the documented MSIX removal API
//   Registered  the command the app itself wrote into its Uninstall registry key,
//               run as written
//
// And a list of things this code will never do: delete a file or folder belonging
// to an app, touch WinSxS, strip a system component, force-remove something with
// no uninstaller, or fall back to deleting by hand when an uninstaller fails. An
// app with no legitimate removal route is shown as not removable, with the reason.
//
// Nothing here is reachable from the command line. Uninstalling is a GUI action
// with its own confirmation for each app, one at a time, so an unattended run can
// never remove software.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace EULord.Core
{
    internal enum AppRoute
    {
        /// <summary>An MSIX/Appx package, removed through Remove-AppxPackage.</summary>
        Appx,
        /// <summary>A classic installer, removed through its registered UninstallString.</summary>
        Registered
    }

    internal sealed class AppCandidate
    {
        public string Id;
        public string DisplayName;
        public AppRoute Route;

        /// <summary>Package names for the Appx route. '*' matches any run of characters.</summary>
        public string[] PackagePatterns = new string[0];

        /// <summary>Exact DisplayName values for the Registered route.</summary>
        public string[] RegistryNames = new string[0];

        /// <summary>A catalog Id that must be in effect before Windows allows removal.</summary>
        public string RequiresPolicy;

        /// <summary>Where the user can get it back, if there is a one-click route.</summary>
        public string ReinstallUri;

        /// <summary>Shown instead of a reinstall button when getting it back is not a single click.</summary>
        public L ReinstallNote;
    }

    internal enum AppAvailability
    {
        NotInstalled,
        Removable,
        /// <summary>Installed, but Windows marks the package as non-removable.</summary>
        SystemProtected,
        /// <summary>Installed, but no uninstaller is registered, so we will not touch it.</summary>
        NoUninstaller,
        /// <summary>Installed and removable only once the matching policy is in effect.</summary>
        NeedsPolicy
    }

    internal sealed class AppStatus
    {
        public AppCandidate Candidate;
        public AppAvailability Availability;

        /// <summary>The package full name (Appx) or the UninstallString (Registered).</summary>
        public string Handle;

        /// <summary>Version or package identity, shown next to the name.</summary>
        public string Detail = string.Empty;

        public bool CanUninstall { get { return Availability == AppAvailability.Removable; } }
    }

    internal static class AppRemoval
    {
        private static readonly string[] UninstallRoots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        /// <summary>
        /// Microsoft's own inbox apps, and only those. Deliberately not a general app
        /// manager: third-party software is none of this tool's business.
        /// </summary>
        public static readonly AppCandidate[] Candidates =
        {
            new AppCandidate
            {
                Id = "Edge", DisplayName = "Microsoft Edge", Route = AppRoute.Registered,
                RegistryNames = new[] { "Microsoft Edge" },
                RequiresPolicy = "EdgeUninstall",
                ReinstallUri = "https://www.microsoft.com/edge/download"
            },
            new AppCandidate
            {
                Id = "Store", DisplayName = "Microsoft Store", Route = AppRoute.Appx,
                PackagePatterns = new[] { "Microsoft.WindowsStore" },
                RequiresPolicy = "StoreUninstall",
                ReinstallNote = new L(
                    "Store 移除後要用系統修復或 wsreset -i 才能裝回，沒有一鍵還原。",
                    "Store 移除后要用系统修复或 wsreset -i 才能装回，没有一键还原。",
                    "Once removed, the Store needs a system repair or wsreset -i to come back; there is no one-click undo.")
            },
            new AppCandidate
            {
                Id = "Copilot", DisplayName = "Copilot", Route = AppRoute.Appx,
                PackagePatterns = new[] { "Microsoft.Copilot", "Microsoft.Windows.Copilot*" },
                ReinstallUri = "ms-windows-store://pdp/?productid=9NHT9RB2F4HD"
            },
            new AppCandidate
            {
                Id = "PhoneLink", DisplayName = "Phone Link", Route = AppRoute.Appx,
                PackagePatterns = new[] { "Microsoft.YourPhone" },
                ReinstallUri = "ms-windows-store://pdp/?productid=9NMPJ99VJBWV"
            },
            new AppCandidate
            {
                Id = "Widgets", DisplayName = "Widgets", Route = AppRoute.Appx,
                PackagePatterns = new[] { "MicrosoftWindows.Client.WebExperience" },
                ReinstallNote = new L(
                    "小工具是 Windows 體驗套件的一部分，要靠系統更新才會裝回。",
                    "小组件是 Windows 体验包的一部分，要靠系统更新才会装回。",
                    "Widgets ships as part of the Windows Web Experience Pack and comes back through a system update.")
            },
            new AppCandidate
            {
                Id = "XboxApp", DisplayName = "Xbox", Route = AppRoute.Appx,
                PackagePatterns = new[] { "Microsoft.GamingApp" },
                ReinstallUri = "ms-windows-store://pdp/?productid=9MV0B5HZVK9Z"
            },
            new AppCandidate
            {
                Id = "XboxGameBar", DisplayName = "Xbox Game Bar", Route = AppRoute.Appx,
                PackagePatterns = new[] { "Microsoft.XboxGamingOverlay" },
                ReinstallUri = "ms-windows-store://pdp/?productid=9NZKPSTSNW4P"
            },
            new AppCandidate
            {
                Id = "Teams", DisplayName = "Microsoft Teams", Route = AppRoute.Appx,
                PackagePatterns = new[] { "MSTeams", "MicrosoftTeams" },
                ReinstallUri = "ms-windows-store://pdp/?productid=XP8BT8DW290MPQ"
            },
            new AppCandidate
            {
                Id = "StartExperiences", DisplayName = "Start Experiences", Route = AppRoute.Appx,
                PackagePatterns = new[] { "*StartExperiences*" },
                RequiresPolicy = "StartExperiencesUninstall",
                ReinstallNote = new L(
                    "這是「開始」功能表的元件，要靠系統更新才會裝回。",
                    "这是“开始”菜单的组件，要靠系统更新才会装回。",
                    "A Start menu component; it comes back through a system update.")
            },
            new AppCandidate
            {
                Id = "OneDrive", DisplayName = "Microsoft OneDrive", Route = AppRoute.Registered,
                RegistryNames = new[] { "Microsoft OneDrive" },
                ReinstallUri = "https://www.microsoft.com/microsoft-365/onedrive/download"
            }
        };

        private sealed class AppxPackage
        {
            public string Name = string.Empty;
            public string FullName = string.Empty;
            public bool NonRemovable;
        }

        /// <summary>
        /// Looks at every candidate in one pass. The Appx query is a single PowerShell
        /// call, so expanding the Apps section costs one child process rather than ten.
        /// </summary>
        public static List<AppStatus> Survey(JsonObject livePolicies)
        {
            List<AppxPackage> packages = QueryAppxPackages();
            Dictionary<string, string> registered = QueryRegisteredUninstallers();

            var result = new List<AppStatus>();
            foreach (AppCandidate candidate in Candidates)
            {
                AppStatus status = candidate.Route == AppRoute.Appx
                    ? InspectAppx(candidate, packages)
                    : InspectRegistered(candidate, registered);

                if (status.Availability == AppAvailability.Removable &&
                    !string.IsNullOrEmpty(candidate.RequiresPolicy) &&
                    !PolicyIsEnabled(livePolicies, candidate.RequiresPolicy))
                    status.Availability = AppAvailability.NeedsPolicy;

                result.Add(status);
            }
            return result;
        }

        /// <summary>True when the live policy file has the named catalog policy enabled.</summary>
        internal static bool PolicyIsEnabled(JsonObject root, string catalogId)
        {
            CatalogEntry entry = Catalog.ById(catalogId);
            if (root == null || entry == null) return false;

            JsonArray policies = PolicyFile.Policies(root);
            if (policies == null) return false;

            foreach (JsonValue v in policies.Items)
            {
                var p = v as JsonObject;
                if (p == null) continue;
                if (!string.Equals(p.GetString("guid"), entry.Guid, StringComparison.OrdinalIgnoreCase)) continue;

                // The effective state here, not the state we intend: the region
                // condition still counts if we have not rewritten the file yet.
                string iso = Region.EffectiveIso() ?? Native.IrelandIso2;
                return string.Equals(PolicyFile.StateIn(p, iso), PolicyStates.Enabled, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static AppStatus InspectAppx(AppCandidate candidate, List<AppxPackage> packages)
        {
            var status = new AppStatus { Candidate = candidate, Availability = AppAvailability.NotInstalled };

            foreach (AppxPackage pkg in packages)
            {
                if (!MatchesAny(pkg.Name, candidate.PackagePatterns)) continue;

                status.Handle = pkg.FullName;
                status.Detail = pkg.Name;
                status.Availability = pkg.NonRemovable ? AppAvailability.SystemProtected : AppAvailability.Removable;
                return status;
            }
            return status;
        }

        private static AppStatus InspectRegistered(AppCandidate candidate, Dictionary<string, string> registered)
        {
            var status = new AppStatus { Candidate = candidate, Availability = AppAvailability.NotInstalled };

            foreach (string name in candidate.RegistryNames)
            {
                string command;
                if (!registered.TryGetValue(name, out command)) continue;

                status.Detail = name;
                if (string.IsNullOrEmpty(command))
                {
                    status.Availability = AppAvailability.NoUninstaller;
                    return status;
                }
                status.Handle = command;
                status.Availability = AppAvailability.Removable;
                return status;
            }
            return status;
        }

        internal static bool MatchesAny(string value, string[] patterns)
        {
            foreach (string pattern in patterns) if (Matches(value, pattern)) return true;
            return false;
        }

        /// <summary>Case-insensitive match where '*' stands for any run of characters.</summary>
        internal static bool Matches(string value, string pattern)
        {
            if (value == null || pattern == null) return false;
            if (pattern.IndexOf('*') < 0) return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);

            string[] parts = pattern.Split('*');
            int at = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0) continue;

                if (i == 0)
                {
                    if (!value.StartsWith(part, StringComparison.OrdinalIgnoreCase)) return false;
                    at = part.Length;
                    continue;
                }

                int found = value.IndexOf(part, at, StringComparison.OrdinalIgnoreCase);
                if (found < 0) return false;
                at = found + part.Length;
            }

            string tail = parts[parts.Length - 1];
            if (tail.Length > 0 && !value.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static List<AppxPackage> QueryAppxPackages()
        {
            var list = new List<AppxPackage>();

            // Tab separated rather than CSV: package names never contain a tab, and it
            // saves having to unquote. NonRemovable is only populated when elevated.
            const string command =
                "Get-AppxPackage | ForEach-Object { $_.Name + \"`t\" + $_.PackageFullName + \"`t\" + [string]$_.NonRemovable }";

            ProcessResult result = Shell.PowerShell(command, 60000);
            if (!result.Ok)
            {
                Log.Warn("Listing Appx packages failed -- " + result.Failure);
                return list;
            }

            foreach (string line in result.StdOut.Split('\n'))
            {
                string[] fields = line.TrimEnd('\r').Split('\t');
                if (fields.Length < 2 || fields[0].Length == 0) continue;
                list.Add(new AppxPackage
                {
                    Name = fields[0].Trim(),
                    FullName = fields[1].Trim(),
                    NonRemovable = fields.Length > 2 &&
                                   string.Equals(fields[2].Trim(), "True", StringComparison.OrdinalIgnoreCase)
                });
            }
            return list;
        }

        /// <summary>DisplayName -&gt; UninstallString, across the per-machine and per-user hives.</summary>
        private static Dictionary<string, string> QueryRegisteredUninstallers()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (string rootPath in UninstallRoots)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                        using (RegistryKey root = baseKey.OpenSubKey(rootPath, false))
                        {
                            if (root == null) continue;
                            foreach (string subName in root.GetSubKeyNames())
                            {
                                using (RegistryKey sub = root.OpenSubKey(subName, false))
                                {
                                    if (sub == null) continue;
                                    var displayName = sub.GetValue("DisplayName") as string;
                                    if (string.IsNullOrEmpty(displayName)) continue;

                                    var command = sub.GetValue("UninstallString") as string;
                                    if (!map.ContainsKey(displayName)) map[displayName] = command ?? string.Empty;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Reading " + rootPath + " failed -- " + Log.Describe(ex));
                    }
                }
            }
            return map;
        }

        /// <summary>
        /// Removes one app through its own uninstaller. The caller must already have the
        /// user's confirmation for this specific app.
        /// </summary>
        public static bool Uninstall(AppStatus status, State state, out string message)
        {
            message = string.Empty;
            if (status == null || !status.CanUninstall)
            {
                message = "the app is not in a removable state";
                return false;
            }

            AppCandidate candidate = status.Candidate;
            Log.Info("Uninstalling " + candidate.Id + " via " + candidate.Route + ", handle=" + status.Handle);

            ProcessResult result;
            string method;

            if (candidate.Route == AppRoute.Appx)
            {
                method = "Remove-AppxPackage";
                string package = status.Handle.Replace("'", "''");
                result = Shell.PowerShell(
                    "Remove-AppxPackage -Package '" + package + "' -ErrorAction Stop", 180000);
            }
            else
            {
                method = "registered uninstaller";
                string fileName, arguments;
                Shell.SplitCommand(status.Handle, out fileName, out arguments);
                if (string.IsNullOrEmpty(fileName))
                {
                    message = "the registered uninstall command could not be read";
                    Log.Error("Uninstalling " + candidate.Id + " failed: " + message);
                    return false;
                }
                // Run exactly as registered. The vendor's own confirmation may appear,
                // which is the user confirming in Microsoft's UI rather than ours.
                result = Shell.RunInteractive(fileName, arguments, 600000);
            }

            if (!result.Ok)
            {
                message = result.Failure;
                Log.Error("Uninstalling " + candidate.Id + " failed -- " + message);
                // Deliberately no fallback. A failed uninstall stays failed.
                return false;
            }

            state.RecordRemoval(candidate.Id, candidate.DisplayName, method);
            Log.Info("Uninstalled " + candidate.Id + " at " +
                     DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>Opens the official place to get an app back. Never installs anything itself.</summary>
        public static bool OpenReinstall(AppCandidate candidate)
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.ReinstallUri)) return false;
            try
            {
                System.Diagnostics.Process.Start(candidate.ReinstallUri);
                Log.Info("Opened the reinstall page for " + candidate.Id);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not open the reinstall page for " + candidate.Id + " -- " + Log.Describe(ex));
                return false;
            }
        }
    }
}
