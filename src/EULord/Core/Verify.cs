// Writing a setting is not the same as the feature appearing.
//
// Microsoft rolls these features out in waves, so the honest report has two tiers:
//
//   Settled    things we can prove by reading them back: the region value, the
//              policy file being our rewrite, every intended policy state actually
//              present in the file, the scheduled task being registered. If one of
//              these fails, the tool failed.
//
//   Observed   things Windows has to act on: whether the Store reports itself
//              removable, whether Edge's uninstall entry allows removal. These can
//              legitimately lag, so a negative result is reported as "not yet"
//              with the likely reason, never as a failure and never as a success.
//
// Anything we cannot measure is reported as "cannot tell" rather than guessed at.

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace EULord.Core
{
    internal enum CheckResult { Pass, Pending, Unknown, Fail }

    internal sealed class Check
    {
        public L Title;
        public CheckResult Result;

        /// <summary>The measurement, in English, as read. Shown verbatim next to the title.</summary>
        public string Evidence = string.Empty;

        /// <summary>What to do about it, when it is not a pass.</summary>
        public L Advice;

        /// <summary>True for the checks whose failure means the tool itself failed.</summary>
        public bool Settled;
    }

    internal sealed class VerifyReport
    {
        public readonly List<Check> Checks = new List<Check>();

        public bool AllSettledPass
        {
            get
            {
                foreach (Check c in Checks) if (c.Settled && c.Result != CheckResult.Pass) return false;
                return true;
            }
        }

        public int PassCount
        {
            get { int n = 0; foreach (Check c in Checks) if (c.Result == CheckResult.Pass) n++; return n; }
        }

        public bool AnyPending
        {
            get { foreach (Check c in Checks) if (c.Result == CheckResult.Pending) return true; return false; }
        }
    }

    internal static class Verify
    {
        public static VerifyReport Run(State state, PolicyPlan plan)
        {
            var report = new VerifyReport();
            bool managed = LooksManaged();

            report.Checks.Add(CheckRegion(state, managed));
            report.Checks.Add(CheckPolicyFile(managed));
            report.Checks.Add(CheckIntendedStates(state, plan, managed));
            report.Checks.Add(CheckTask());
            report.Checks.Add(CheckStoreRemovable(state));
            report.Checks.Add(CheckEdgeRemovable(state));
            report.Checks.Add(CheckWebSearchToggle(state));

            foreach (Check c in report.Checks)
                Log.Info("Verify: " + c.Title.Invariant + " = " + c.Result + " (" + c.Evidence + ")");

            return report;
        }

        private static Check CheckRegion(State state, bool managed)
        {
            int? actual = Region.Read();
            var check = new Check { Title = S.VerifyRegion, Settled = true };

            if (!actual.HasValue)
            {
                check.Result = CheckResult.Fail;
                check.Evidence = "DeviceRegion is absent";
                check.Advice = managed ? S.DiagManaged : S.DiagUcpd;
                return check;
            }

            check.Evidence = "DeviceRegion = " + actual.Value +
                             " (" + (Native.Iso2Of(actual.Value) ?? "?") + "), wanted " + state.RegionGeoId;

            if (actual.Value == state.RegionGeoId) { check.Result = CheckResult.Pass; return check; }

            check.Result = CheckResult.Fail;
            check.Advice = managed ? S.DiagManaged : S.DiagUcpd;
            return check;
        }

        private static Check CheckPolicyFile(bool managed)
        {
            var check = new Check { Title = S.VerifyPolicy, Settled = true };
            PolicyFileOrigin origin = Backups.ClassifyLive();

            check.Evidence = "policy file looks " +
                (origin == PolicyFileOrigin.Ours ? "rewritten (no region conditions)"
                 : origin == PolicyFileOrigin.WindowsOriginal ? "untouched (region conditions present)"
                 : "unreadable");

            switch (origin)
            {
                case PolicyFileOrigin.Ours: check.Result = CheckResult.Pass; break;
                case PolicyFileOrigin.WindowsOriginal:
                    check.Result = CheckResult.Fail;
                    check.Advice = managed ? S.DiagManaged : S.DiagRestart;
                    break;
                default:
                    check.Result = CheckResult.Unknown;
                    break;
            }
            return check;
        }

        /// <summary>
        /// The strongest check available: re-reads the file and confirms every state the
        /// plan intended is the state actually on disk.
        /// </summary>
        private static Check CheckIntendedStates(State state, PolicyPlan plan, bool managed)
        {
            var check = new Check
            {
                Title = new L("每條策略的狀態與計畫相符", "每条策略的状态与计划相符", "Every policy matches the plan"),
                Settled = true
            };

            JsonObject live = PolicyFile.TryReadLive();
            if (live == null || plan == null)
            {
                check.Result = CheckResult.Unknown;
                check.Evidence = "the live policy file could not be read";
                return check;
            }

            var onDisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JsonArray policies = PolicyFile.Policies(live);
            if (policies != null)
            {
                foreach (JsonValue v in policies.Items)
                {
                    var p = v as JsonObject;
                    if (p == null) continue;
                    string guid = p.GetString("guid");
                    if (guid != null) onDisk[guid] = p.GetString("defaultState") ?? string.Empty;
                }
            }

            int matched = 0, mismatched = 0;
            foreach (PolicyDecision d in plan.Decisions)
            {
                string actual;
                if (!onDisk.TryGetValue(d.Guid ?? string.Empty, out actual)) { mismatched++; continue; }
                if (string.Equals(actual, d.TargetState, StringComparison.OrdinalIgnoreCase)) matched++;
                else mismatched++;
            }

            check.Evidence = matched + " of " + (matched + mismatched) + " policies match";
            if (mismatched == 0) check.Result = CheckResult.Pass;
            else
            {
                check.Result = CheckResult.Fail;
                check.Advice = managed ? S.DiagManaged : S.DiagRestart;
            }
            return check;
        }

        private static Check CheckTask()
        {
            PersistenceStatus status = Persistence.Status();
            var check = new Check { Title = S.VerifyTask, Settled = true, Evidence = "scheduled task is " + status };

            switch (status)
            {
                case PersistenceStatus.Ready: check.Result = CheckResult.Pass; break;
                case PersistenceStatus.Unknown: check.Result = CheckResult.Unknown; break;
                default:
                    check.Result = CheckResult.Fail;
                    check.Advice = S.DiagRestart;
                    break;
            }
            return check;
        }

        private static Check CheckStoreRemovable(State state)
        {
            var check = new Check { Title = S.VerifyStore };

            if (!state.EverApplied)
            {
                check.Result = CheckResult.Pending;
                check.Evidence = "not applied yet";
                check.Advice = S.DiagNotApplied;
                return check;
            }

            ProcessResult result = Shell.PowerShell(
                "$p = Get-AppxPackage Microsoft.WindowsStore; if ($p) { [string]$p.NonRemovable } else { 'absent' }", 60000);

            if (!result.Ok)
            {
                check.Result = CheckResult.Unknown;
                check.Evidence = "could not query the Store package -- " + result.Failure;
                return check;
            }

            string value = result.StdOut.Trim();
            check.Evidence = "Microsoft.WindowsStore NonRemovable = " + (value.Length == 0 ? "(blank)" : value);

            if (string.Equals(value, "absent", StringComparison.OrdinalIgnoreCase))
            {
                check.Result = CheckResult.Pass;
                check.Evidence = "the Store is not installed, so nothing blocks removal";
            }
            else if (string.Equals(value, "False", StringComparison.OrdinalIgnoreCase))
                check.Result = CheckResult.Pass;
            else if (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase))
            {
                check.Result = CheckResult.Pending;
                check.Advice = S.DiagRollout;
            }
            else
                check.Result = CheckResult.Unknown;

            return check;
        }

        private static Check CheckEdgeRemovable(State state)
        {
            var check = new Check { Title = S.VerifyEdge };

            if (!state.EverApplied)
            {
                check.Result = CheckResult.Pending;
                check.Evidence = "not applied yet";
                check.Advice = S.DiagNotApplied;
                return check;
            }

            bool found = false, blocked = false;
            foreach (string root in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (RegistryKey key = baseKey.OpenSubKey(root, false))
                    {
                        if (key == null) continue;
                        foreach (string subName in key.GetSubKeyNames())
                        {
                            using (RegistryKey sub = key.OpenSubKey(subName, false))
                            {
                                if (sub == null) continue;
                                if (!string.Equals(sub.GetValue("DisplayName") as string, "Microsoft Edge",
                                                   StringComparison.OrdinalIgnoreCase)) continue;
                                found = true;
                                object noRemove = sub.GetValue("NoRemove", null);
                                if (noRemove != null && noRemove.ToString() == "1") blocked = true;
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Warn("Edge removability check failed -- " + Log.Describe(ex)); }
            }

            if (!found)
            {
                check.Result = CheckResult.Pass;
                check.Evidence = "Edge has no uninstall entry, so it is not installed";
                return check;
            }

            check.Evidence = "Edge uninstall entry present, NoRemove " + (blocked ? "= 1" : "not set");
            if (blocked)
            {
                check.Result = CheckResult.Pending;
                check.Advice = S.DiagRollout;
            }
            else
            {
                check.Result = CheckResult.Pass;
            }
            return check;
        }

        /// <summary>
        /// Whether the "turn off web results" switch has appeared is not something Windows
        /// exposes to us. The policy state behind it is, so that is what we report -- and
        /// we say which of the two we measured rather than implying the other.
        /// </summary>
        private static Check CheckWebSearchToggle(State state)
        {
            var check = new Check { Title = S.VerifyWebSearch };

            JsonObject live = PolicyFile.TryReadLive();
            if (live == null)
            {
                check.Result = CheckResult.Unknown;
                check.Evidence = "the live policy file could not be read";
                return check;
            }

            bool enabled = AppRemoval.PolicyIsEnabled(live, "WebSearchOff");
            check.Evidence = "the WebSearchOff policy reads " + (enabled ? "enabled" : "disabled") +
                             "; whether the Settings switch has appeared cannot be measured";

            if (enabled)
            {
                check.Result = CheckResult.Pass;
                check.Advice = S.DiagRollout;
            }
            else
            {
                check.Result = CheckResult.Pending;
                check.Advice = state.EverApplied ? S.DiagRestart : S.DiagNotApplied;
            }
            return check;
        }

        /// <summary>
        /// Advisory only. Every Windows install carries dozens of placeholder entries under
        /// Enrollments, so an enrollment alone proves nothing; a UPN alongside it is the
        /// signal that someone else's policy may be overriding ours.
        /// </summary>
        public static bool LooksManaged()
        {
            try
            {
                if (!string.Equals(Environment.UserDomainName, Environment.MachineName,
                                   StringComparison.OrdinalIgnoreCase)) return true;

                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments", false))
                {
                    if (root == null) return false;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        using (RegistryKey sub = root.OpenSubKey(name, false))
                        {
                            if (sub == null) continue;
                            var upn = sub.GetValue("UPN") as string;
                            if (!string.IsNullOrEmpty(upn)) return true;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Management detection failed -- " + Log.Describe(ex)); }
            return false;
        }
    }
}
