// Reading and writing a file in System32 without leaving a mark on it.
//
// v1 ran `takeown /f` and `icacls /grant *S-1-5-32-544:F` on the policy file. Two
// problems with that, both verified on build 26300:
//
//   1. It was not needed. The shipped DACL already grants BUILTIN\Administrators
//      full control -- (A;;FA;;;BA) -- so an elevated process can just write it.
//   2. It is not reversible. Ownership cannot be handed back afterwards:
//      `icacls <file> /setowner "NT SERVICE\TrustedInstaller"` fails with 1307,
//      "This security ID may not be assigned as the owner of this object". So v1
//      permanently changed the owner and DACL of a Windows system file.
//
// v2 therefore tries an ordinary write first. If that is denied, it enables
// SeRestorePrivilege -- the privilege backup software uses -- and opens the file
// with backup semantics, which bypasses the DACL check for this one handle without
// modifying the file's owner or permissions at all. Nothing is left behind either
// way, and the driver-level protections stay in place.

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EULord.Core
{
    internal static class SystemFile
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint OPEN_EXISTING = 3;
        private const uint CREATE_ALWAYS = 2;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        private const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const int TOKEN_QUERY = 0x0008;
        private const int SE_PRIVILEGE_ENABLED = 0x0002;

        private const string SeBackupPrivilege = "SeBackupPrivilege";
        private const string SeRestorePrivilege = "SeRestorePrivilege";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValueW(string lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr tokenHandle, bool disableAllPrivileges, ref TokenPrivileges newState,
            int bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TokenPrivileges
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        private static bool _privilegesTried;
        private static bool _privilegesHeld;

        /// <summary>
        /// Enables the backup/restore privileges for this process. Administrators hold
        /// them but they are disabled by default. Enabling a privilege the token already
        /// contains grants nothing new; it only makes it usable.
        /// </summary>
        private static bool EnableBackupPrivileges()
        {
            if (_privilegesTried) return _privilegesHeld;
            _privilegesTried = true;

            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle,
                                      TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                    return false;

                bool any = Enable(token, SeRestorePrivilege) | Enable(token, SeBackupPrivilege);
                _privilegesHeld = any;
                return any;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not enable backup privileges -- " + Log.Describe(ex));
                return false;
            }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }

        private static bool Enable(IntPtr token, string privilege)
        {
            long luid;
            if (!LookupPrivilegeValueW(null, privilege, out luid)) return false;

            var tp = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;

            // AdjustTokenPrivileges reports success even when it only partly succeeded.
            return Marshal.GetLastWin32Error() == 0;
        }

        /// <summary>Reads a UTF-8 text file, falling back to backup semantics if access is denied.</summary>
        public static string ReadAllText(string path)
        {
            try
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (UnauthorizedAccessException)
            {
                Log.Warn("Direct read of " + Path.GetFileName(path) + " was denied; retrying with backup semantics");
                if (!EnableBackupPrivileges()) throw;

                using (SafeFileHandle handle = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero,
                                                           OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
                {
                    if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                    using (var stream = new FileStream(handle, FileAccess.Read))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                        return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Overwrites a file with UTF-8 text and no BOM, in place, so its owner and DACL
        /// are untouched. The policy file ships without a BOM and must stay that way.
        /// </summary>
        public static void WriteAllTextNoBom(string path, string text)
        {
            byte[] bytes = Utf8NoBom.GetBytes(text);
            try
            {
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    stream.Write(bytes, 0, bytes.Length);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                Log.Warn("Direct write to " + Path.GetFileName(path) + " was denied; retrying with backup semantics");
            }

            if (!EnableBackupPrivileges())
                throw new UnauthorizedAccessException(
                    "Write to " + path + " was denied and the backup privilege is not available.");

            using (SafeFileHandle handle = CreateFileW(path, GENERIC_WRITE, 0, IntPtr.Zero,
                                                       CREATE_ALWAYS, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (var stream = new FileStream(handle, FileAccess.Write))
                    stream.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>True when the file starts with a UTF-8 BOM. Used by the tests as a guard.</summary>
        public static bool HasUtf8Bom(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var head = new byte[3];
                int read = stream.Read(head, 0, 3);
                return read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
            }
        }

        public static string Sha256(string text) { return Sha256(Utf8NoBom.GetBytes(text)); }

        public static string Sha256(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Byte-for-byte comparison of two files, cheapest check first.</summary>
        public static bool SameContent(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;

                using (FileStream sa = fa.OpenRead())
                using (FileStream sb = fb.OpenRead())
                {
                    var bufA = new byte[64 * 1024];
                    var bufB = new byte[64 * 1024];
                    while (true)
                    {
                        int readA = sa.Read(bufA, 0, bufA.Length);
                        int readB = sb.Read(bufB, 0, bufB.Length);
                        if (readA != readB) return false;
                        if (readA == 0) return true;
                        for (int i = 0; i < readA; i++) if (bufA[i] != bufB[i]) return false;
                    }
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
