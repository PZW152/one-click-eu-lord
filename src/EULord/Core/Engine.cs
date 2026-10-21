// What the one button does, and how to undo it.
//
// Apply, in order, each step depending on the one before:
//
//   1  back up the Windows-shipped policy file (original once, pristine every time
//      Windows ships a new one)
//   2  record the region Windows had cached, before we overwrite it, so restore can
//      put back the right value -- or delete it, if there was none
//   3  build the plan from the pristine source, never from a file we already wrote
//   4  write the rewritten policy file, in place, preserving its owner and DACL
//   5  set DeviceRegion
//   6  install the copy of ourselves and the scheduled task that re-applies it
//   7  save state, then verify by reading everything back
//
// A failure in steps 1-4 aborts before anything is written. Steps 5 and 6 are
// reported individually: a machine where the region write is blocked still gets the
// policy benefits, and saying otherwise would be a lie.

using System;
using System.Collections.Generic;
using System.IO;

namespace EULord.Core
{
    internal enum EngineOutcome { Success, Partial, Failed, NeedsElevation }

    internal sealed class EngineResult
    {
        public EngineOutcome Outcome = EngineOutcome.Failed;
        public PolicyPlan Plan;
        public VerifyReport Report;

        /// <summary>English, log-ready notes on anything that did not go to plan.</summary>
        public readonly List<string> Problems = new List<string>();

        public bool Ok { get { return Outcome == EngineOutcome.Success; } }
    }

    internal delegate void Progress(string englishMessage);

    internal static class Engine
    {
        /// <summary>Reads the machine without changing anything. Safe without elevation.</summary>
        public static PolicyPlan Preview(State state)
        {
            string source = Backups.PristineSource();
            if (source == null) return null;

            try
            {
                return PolicyFile.BuildPlan(source, state.RegionIso, state);
            }
            catch (Exception ex)
            {
                Log.Error("Building the preview plan failed", ex);
                return null;
            }
        }

        public static EngineResult Apply(State state, Progress progress)
        {
            var result = new EngineResult();
            Report(progress, "Starting");

            if (!Native.IsElevated())
            {
                result.Outcome = EngineOutcome.NeedsElevation;
                result.Problems.Add("administrator rights are required");
                Log.Error("Apply refused: not elevated");
                return result;
            }

            try
            {
                Paths.EnsureDataLayout();

                Report(progress, "Backing up the policy file");
                Backups.Capture();

                Region.RememberOriginal(state);

                string source = Backups.PristineSource();
                if (source == null)
                {
                    result.Problems.Add("no pristine Windows policy file is available as a source");
                    Log.Error("Apply aborted: no pristine source. Nothing was written.");
                    return result;
                }

                Report(progress, "Deciding each policy");
                PolicyPlan plan = PolicyFile.BuildPlan(source, state.RegionIso, state);
                result.Plan = plan;

                // Cheap insurance: the text we are about to put in System32 must parse.
                PolicyFile.Parse(plan.Json);

                Report(progress, "Writing the policy file");
                SystemFile.WriteAllTextNoBom(Paths.PolicyFile, plan.Json);

                // Read the system file back and make sure it is intact. A write that
                // was interrupted, truncated, or silently redirected somewhere else
                // would otherwise leave Windows with a policy file it cannot parse,
                // and the tool would carry on reporting success. If the check fails,
                // put the Windows original back before doing anything else.
                string rollbackProblem;
                if (!PolicyFileSurvivedWrite(plan.Json, out rollbackProblem))
                {
                    result.Problems.Add(rollbackProblem);
                    RollBackPolicyFile(source, result);
                    return result;
                }

                if (SystemFile.HasUtf8Bom(Paths.PolicyFile))
                    result.Problems.Add("the policy file unexpectedly has a byte order mark");

                Log.Info("Policy file rewritten: " + plan.Gains + " gains taken, " +
                         plan.LossesRefused + " losses refused, " +
                         plan.Unclassified.Count + " unclassified left untouched");

                Report(progress, "Setting the device region");
                RegionWriteResult regionResult = Region.Write(state.RegionGeoId);
                if (regionResult == RegionWriteResult.Failed)
                    result.Problems.Add("DeviceRegion could not be set to " + state.RegionGeoId);

                Report(progress, "Installing the scheduled task");
                if (!Persistence.Install())
                    result.Problems.Add("the scheduled task could not be registered, so the change may not survive updates");

                state.AppliedUtc = State.UtcNow();
                state.ProductVersion = Build.Version;
                state.Unclassified.Clear();
                state.Unclassified.AddRange(plan.Unclassified);
                StateStore.Save(state);

                Report(progress, "Verifying");
                result.Report = Verify.Run(state, plan);

                bool settled = result.Report.AllSettledPass;
                result.Outcome = settled && result.Problems.Count == 0
                    ? EngineOutcome.Success
                    : (settled ? EngineOutcome.Partial : EngineOutcome.Failed);

                Log.Info("Apply finished: " + result.Outcome);
                return result;
            }
            catch (Exception ex)
            {
                result.Problems.Add(Log.Describe(ex));
                Log.Error("Apply failed", ex);
                return result;
            }
        }

        /// <summary>
        /// The headless path the scheduled task runs. Does nothing at all unless the user
        /// had applied the superset, so the task can never introduce changes on its own.
        /// </summary>
        public static EngineResult Reapply(State state, Progress progress)
        {
            if (!state.EverApplied)
            {
                Log.Info("Re-apply skipped: the superset has never been applied on this machine");
                return new EngineResult { Outcome = EngineOutcome.Success };
            }

            PolicyFileOrigin origin = Backups.ClassifyLive();
            int? region = Region.Read();

            if (origin == PolicyFileOrigin.Ours && region.HasValue && region.Value == state.RegionGeoId)
            {
                Log.Info("Re-apply skipped: the policy file and region are already as saved");
                return new EngineResult { Outcome = EngineOutcome.Success };
            }

            Log.Info("Re-applying: policy file is " + origin + ", DeviceRegion is " +
                     (region.HasValue ? region.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "absent"));
            return Apply(state, progress);
        }

        /// <summary>
        /// Puts the machine back: the backed-up policy file, the old region value, and no
        /// scheduled task. Backups and logs are kept; uninstalled apps are not reinstalled,
        /// and the report says so rather than implying a full undo.
        /// </summary>
        public static EngineResult Restore(State state, Progress progress)
        {
            var result = new EngineResult();

            if (!Native.IsElevated())
            {
                result.Outcome = EngineOutcome.NeedsElevation;
                result.Problems.Add("administrator rights are required");
                return result;
            }

            try
            {
                Report(progress, "Removing the scheduled task");
                if (!Persistence.Remove())
                    result.Problems.Add("the scheduled task could not be removed");

                string backup = Backups.RestoreSourcePath();
                if (backup == null)
                {
                    result.Problems.Add("no backup of the policy file exists, so it was left as it is");
                    Log.Error("Restore: no backup available");
                }
                else
                {
                    Report(progress, "Restoring the policy file");
                    string text = File.ReadAllText(backup, System.Text.Encoding.UTF8);
                    PolicyFile.Parse(text);
                    SystemFile.WriteAllTextNoBom(Paths.PolicyFile, text);
                    Log.Info("Policy file restored from backups\\" + Path.GetFileName(backup));
                }

                Report(progress, "Restoring the device region");
                if (!Region.Restore(state))
                    result.Problems.Add("DeviceRegion could not be restored");

                state.AppliedUtc = null;
                StateStore.Save(state);

                if (state.RemovedApps.Count > 0)
                    result.Problems.Add(state.RemovedApps.Count +
                        " app(s) were uninstalled earlier and are not reinstalled by restore");

                result.Outcome = result.Problems.Count == 0 ? EngineOutcome.Success : EngineOutcome.Partial;
                Log.Info("Restore finished: " + result.Outcome);
                return result;
            }
            catch (Exception ex)
            {
                result.Problems.Add(Log.Describe(ex));
                Log.Error("Restore failed", ex);
                return result;
            }
        }

        /// <summary>
        /// Restore, then delete everything this tool owns. Only ever called after an
        /// explicit confirmation naming the folder. Anything that cannot be deleted is
        /// reported rather than silently left behind.
        /// </summary>
        public static EngineResult Purge(State state, Progress progress)
        {
            EngineResult result = Restore(state, progress);
            if (result.Outcome == EngineOutcome.NeedsElevation) return result;

            Report(progress, "Deleting the data folder");

            // Our own running copy cannot delete itself; schedule it for the next boot.
            string self = Paths.CurrentExe;
            bool runningFromInside = Paths.RunningFromDataRoot;

            try
            {
                if (Directory.Exists(Paths.DataRoot))
                {
                    foreach (string path in Directory.GetFiles(Paths.DataRoot, "*", SearchOption.AllDirectories))
                    {
                        if (runningFromInside &&
                            string.Equals(Path.GetFullPath(path), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase))
                            continue;
                        try { File.Delete(path); }
                        catch (Exception ex) { result.Problems.Add("could not delete " + path + " -- " + Log.Describe(ex)); }
                    }

                    if (!runningFromInside)
                    {
                        try { Directory.Delete(Paths.DataRoot, true); }
                        catch (Exception ex) { result.Problems.Add("could not delete " + Paths.DataRoot + " -- " + Log.Describe(ex)); }
                    }
                    else
                    {
                        result.Problems.Add("the running copy at " + self + " remains and can be deleted after a restart");
                    }
                }
                Log.Info("Purge finished with " + result.Problems.Count + " problem(s)");
            }
            catch (Exception ex)
            {
                result.Problems.Add(Log.Describe(ex));
                Log.Error("Purge failed", ex);
            }

            result.Outcome = result.Problems.Count == 0 ? EngineOutcome.Success : EngineOutcome.Partial;
            return result;
        }

        /// <summary>
        /// Re-reads the policy file after writing it and confirms it is the document we
        /// meant to write. Returns false, with a reason, if anything is off.
        /// </summary>
        internal static bool PolicyFileSurvivedWrite(string intended, out string problem)
        {
            problem = null;
            try
            {
                string onDisk = SystemFile.ReadAllText(Paths.PolicyFile);

                if (SystemFile.Sha256(onDisk) == SystemFile.Sha256(intended)) return true;

                // Not byte-identical. It may still be the right document -- something
                // could have normalised the newlines -- so judge it by its content.
                JsonObject root = PolicyFile.Parse(onDisk);
                int written = PolicyFile.Policies(root).Count;
                int expected = PolicyFile.Policies(PolicyFile.Parse(intended)).Count;

                if (written == expected) return true;

                problem = "the policy file holds " + written + " policies after writing, not " + expected;
                return false;
            }
            catch (Exception ex)
            {
                problem = "the policy file could not be read back after writing -- " + Log.Describe(ex);
                return false;
            }
        }

        /// <summary>
        /// Puts the Windows original back after a failed write, so a machine is never
        /// left with a policy file this tool damaged.
        /// </summary>
        internal static void RollBackPolicyFile(string pristine, EngineResult result)
        {
            Log.Error("The policy file did not survive the write; restoring the Windows original");
            try
            {
                SystemFile.WriteAllTextNoBom(Paths.PolicyFile, pristine);
                PolicyFile.Parse(SystemFile.ReadAllText(Paths.PolicyFile));
                result.Problems.Add("the Windows original was put back, so nothing was changed");
                Log.Info("Rollback succeeded: the Windows policy file is back in place");
            }
            catch (Exception ex)
            {
                result.Problems.Add("the rollback also failed; restore the file by hand from " + Paths.BackupDir);
                Log.Error("Rollback failed", ex);
            }
        }

        private static void Report(Progress progress, string message)
        {
            Log.Info(message);
            if (progress != null) progress(message);
        }
    }

    internal static class Build
    {
        /// <summary>Kept in step with CHANGELOG.md and the release tag; CI fails if they disagree.</summary>
        public const string Version = "2.0.1";
        public const string Repository = "https://github.com/PZW152/one-click-eu-lord";
    }
}
