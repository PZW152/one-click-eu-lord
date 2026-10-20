// Every file EULord owns, in one place.
//
// quality-absolutism: an uninstaller must never guess filenames. Nothing outside
// this class decides where anything lives, so "remove everything" can delete
// exactly what we created and nothing else.

using System;
using System.IO;

namespace EULord.Core
{
    internal static class Paths
    {
        public const string ProductFolder = "OneClickEULord";
        public const string TaskName = "OneClickEULord-Persist";
        public const string ExeFileName = "EULord.exe";
        public const string PolicyFileName = "IntegratedServicesRegionPolicySet.json";

        /// <summary>Earlier releases of this same product, so an upgrade can take them over.</summary>
        public static readonly string[] LegacyFolders = { "OneClickEUGrandpa", "OneClickEUOverlord" };
        public static readonly string[] LegacyTaskNames = { "OneClickEUGrandpa-Persist", "OneClickEUOverlord-Persist" };

        private static string _dataRoot;
        private static string _commonAppData;

        /// <summary>
        /// ProgramData, or wherever it has been redirected to. Tests redirect it so that
        /// looking for a v1 install cannot read anything outside their sandbox.
        /// </summary>
        public static string CommonAppData
        {
            get
            {
                if (_commonAppData == null)
                    _commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                return _commonAppData;
            }
        }

        public static void OverrideCommonAppData(string path)
        {
            if (!string.IsNullOrEmpty(path)) _commonAppData = Path.GetFullPath(path);
        }

        /// <summary>
        /// Where state, backups and logs live. Defaults to ProgramData; --data-dir moves it
        /// for IT deployments and for tests, which must never touch the real system.
        /// </summary>
        public static string DataRoot
        {
            get
            {
                if (_dataRoot == null) _dataRoot = Path.Combine(CommonAppData, ProductFolder);
                return _dataRoot;
            }
        }

        public static void OverrideDataRoot(string path)
        {
            if (!string.IsNullOrEmpty(path)) _dataRoot = Path.GetFullPath(path);
        }

        public static string LegacyDataRoot(string folder) { return Path.Combine(CommonAppData, folder); }

        public static string InstalledExe { get { return Path.Combine(DataRoot, ExeFileName); } }
        public static string StateFile { get { return Path.Combine(DataRoot, "state.json"); } }
        public static string BackupDir { get { return Path.Combine(DataRoot, "backups"); } }
        public static string LogDir { get { return Path.Combine(DataRoot, "logs"); } }
        public static string LogFile { get { return Path.Combine(LogDir, "eulord.log"); } }

        /// <summary>The first policy file we ever saw on this machine. Kept for history, never overwritten.</summary>
        public static string OriginalBackup
        {
            get { return Path.Combine(BackupDir, PolicyFileName.Replace(".json", ".original.json")); }
        }

        /// <summary>The newest Windows-shipped policy file. Every rewrite is rebuilt from this.</summary>
        public static string PristineBackup
        {
            get { return Path.Combine(BackupDir, PolicyFileName.Replace(".json", ".pristine.json")); }
        }

        private static string _policyFile;
        private static string _systemDirectory;

        /// <summary>
        /// The real System32, even from a 32-bit process.
        ///
        /// WOW64 redirects System32 to SysWOW64 for 32-bit processes, and SysWOW64 holds
        /// its own copy of IntegratedServicesRegionPolicySet.json. Writing that copy would
        /// change nothing the 64-bit system reads, and the tool would report success while
        /// having done nothing at all. Sysnative is the documented way out, and it exists
        /// only for 32-bit processes, which is why it is used only then.
        /// </summary>
        public static string SystemDirectory
        {
            get
            {
                if (_systemDirectory != null) return _systemDirectory;

                _systemDirectory = !Environment.Is64BitProcess && Environment.Is64BitOperatingSystem
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
                    : Environment.GetFolderPath(Environment.SpecialFolder.System);
                return _systemDirectory;
            }
        }

        /// <summary>The live system policy file. Redirected by tests so they never touch System32.</summary>
        public static string PolicyFile
        {
            get
            {
                if (_policyFile == null) _policyFile = Path.Combine(SystemDirectory, PolicyFileName);
                return _policyFile;
            }
        }

        public static void OverridePolicyFile(string path)
        {
            if (!string.IsNullOrEmpty(path)) _policyFile = Path.GetFullPath(path);
        }

        public static void EnsureDataLayout()
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(BackupDir);
            Directory.CreateDirectory(LogDir);
        }

        /// <summary>The exe this process is running from.</summary>
        public static string CurrentExe
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
        }

        /// <summary>True when we are running as the managed copy inside DataRoot.</summary>
        public static bool RunningFromDataRoot
        {
            get
            {
                try
                {
                    return string.Equals(
                        Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe),
                        StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception) { return false; }
            }
        }
    }
}
