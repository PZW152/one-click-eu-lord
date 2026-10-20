// Two backups, with different jobs.
//
//   *.original.json   the first policy file we ever saw on this machine. Written
//                     once, never overwritten. History, and a last resort.
//   *.pristine.json   the newest policy file Windows has shipped. Replaced whenever
//                     Windows ships a different one. Every rewrite is built from it.
//
// Keeping the pristine copy is what makes the tool idempotent across Windows
// updates. Without it, the second run would rewrite an already-rewritten file,
// which has no region conditions left, so the EEA state would be unknowable and
// the user's per-policy overrides would have nothing to switch back to.
//
// A file is recognised as Windows-shipped by still having region conditions; ours
// never do.

using System;
using System.IO;

namespace EULord.Core
{
    internal enum PolicyFileOrigin
    {
        /// <summary>As Windows shipped it: region conditions still present.</summary>
        WindowsOriginal,
        /// <summary>Rewritten by this tool: no region conditions.</summary>
        Ours,
        /// <summary>Not readable or not parseable.</summary>
        Unreadable
    }

    internal static class Backups
    {
        public static PolicyFileOrigin ClassifyLive()
        {
            JsonObject root = PolicyFile.TryReadLive();
            if (root == null) return PolicyFileOrigin.Unreadable;
            return PolicyFile.IsWindowsOriginal(root) ? PolicyFileOrigin.WindowsOriginal : PolicyFileOrigin.Ours;
        }

        /// <summary>
        /// Keeps the two backups current. Call before every rewrite, while the live file
        /// may still be the Windows one.
        /// </summary>
        public static void Capture()
        {
            string live;
            try
            {
                live = SystemFile.ReadAllText(Paths.PolicyFile);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the policy file to back it up -- " + Log.Describe(ex));
                return;
            }

            JsonObject root;
            try { root = PolicyFile.Parse(live); }
            catch (Exception ex)
            {
                Log.Warn("The policy file did not parse, so it was not backed up -- " + Log.Describe(ex));
                return;
            }

            if (!PolicyFile.IsWindowsOriginal(root))
            {
                Log.Info("The live policy file is already our own rewrite; backups left as they are");
                return;
            }

            Paths.EnsureDataLayout();

            if (!File.Exists(Paths.OriginalBackup))
            {
                SystemFile.WriteAllTextNoBom(Paths.OriginalBackup, live);
                Log.Info("Saved the first-seen policy file to backups\\" + Path.GetFileName(Paths.OriginalBackup));
            }

            string liveHash = SystemFile.Sha256(live);
            if (!File.Exists(Paths.PristineBackup))
            {
                SystemFile.WriteAllTextNoBom(Paths.PristineBackup, live);
                Log.Info("Saved the pristine source, sha256=" + liveHash.Substring(0, 12));
                return;
            }

            string existing = File.ReadAllText(Paths.PristineBackup, System.Text.Encoding.UTF8);
            if (SystemFile.Sha256(existing) == liveHash) return;

            SystemFile.WriteAllTextNoBom(Paths.PristineBackup, live);
            Log.Info("Windows shipped a new policy file; pristine source refreshed, sha256=" + liveHash.Substring(0, 12));
        }

        /// <summary>
        /// The Windows-shipped text every rewrite is built from: the live file when it is
        /// still pristine, otherwise the newest backup of one. Null when no pristine
        /// source exists anywhere, which is a hard stop rather than something to guess at.
        /// </summary>
        public static string PristineSource()
        {
            try
            {
                string live = SystemFile.ReadAllText(Paths.PolicyFile);
                if (PolicyFile.IsWindowsOriginal(PolicyFile.Parse(live))) return live;
            }
            catch (Exception ex)
            {
                Log.Warn("The live policy file is unusable as a source -- " + Log.Describe(ex));
            }

            foreach (string path in new[] { Paths.PristineBackup, Paths.OriginalBackup })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
                    if (PolicyFile.IsWindowsOriginal(PolicyFile.Parse(text)))
                    {
                        Log.Info("Rebuilding from backups\\" + Path.GetFileName(path));
                        return text;
                    }
                    Log.Warn("backups\\" + Path.GetFileName(path) + " is not a Windows original; ignored");
                }
                catch (Exception ex)
                {
                    Log.Warn("backups\\" + Path.GetFileName(path) + " could not be read -- " + Log.Describe(ex));
                }
            }

            return null;
        }

        /// <summary>The backup to put back on restore: the newest Windows-shipped one.</summary>
        public static string RestoreSourcePath()
        {
            if (File.Exists(Paths.PristineBackup)) return Paths.PristineBackup;
            if (File.Exists(Paths.OriginalBackup)) return Paths.OriginalBackup;
            return null;
        }

        public static bool HasAnyBackup() { return RestoreSourcePath() != null; }
    }
}
