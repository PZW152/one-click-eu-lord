// DeviceRegion: the GeoID cache Windows consults before anything else.
//
// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion (DWORD)
//
// Rewriting the policy file already makes the unlocked features region-independent.
// Setting the region as well covers EU behaviour that lives outside that file --
// Edge's own conduct, for one. Both are needed; neither alone is enough.
//
// UCPD, the UserChoice Protection Driver, blocks writes to this value by matching
// the writing process's image name: reg.exe, powershell.exe, cmd.exe, regedit.exe,
// rundll32.exe and mshta.exe are on its deny list. EULord.exe is not, so the
// ordinary write is expected to work; v1 had to shell out and was blocked. The
// fallback copies reg.exe to a temporary name, writes this one value, and deletes
// the copy. The driver is never disabled, never unloaded, and nothing else on the
// system is touched.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace EULord.Core
{
    internal enum RegionWriteResult
    {
        /// <summary>Already had the wanted value; nothing was written.</summary>
        AlreadySet,
        /// <summary>Written with the ordinary registry API.</summary>
        Written,
        /// <summary>The ordinary write was blocked; the renamed-reg.exe fallback succeeded.</summary>
        WrittenViaFallback,
        /// <summary>Both paths failed. The reason is in the log.</summary>
        Failed
    }

    internal static class Region
    {
        private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel";
        private const string ValueName = "DeviceRegion";

        /// <summary>The cached GeoID, or null when Windows has not cached one yet.</summary>
        public static int? Read()
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = root.OpenSubKey(KeyPath, false))
                {
                    if (key == null) return null;
                    object value = key.GetValue(ValueName, null);
                    if (value == null) return null;
                    return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("DeviceRegion could not be read -- " + Log.Describe(ex));
                return null;
            }
        }

        /// <summary>The ISO code Windows is currently acting on, computed the way Windows does.</summary>
        public static string EffectiveIso()
        {
            int? cached = Read();
            int geoId = cached.HasValue ? cached.Value : Native.UserGeoId();
            return geoId > 0 ? Native.Iso2Of(geoId) : null;
        }

        public static RegionWriteResult Write(int geoId)
        {
            int? before = Read();
            if (before.HasValue && before.Value == geoId) return RegionWriteResult.AlreadySet;

            if (TryDirectWrite(geoId) && Read() == geoId)
            {
                Log.Info("DeviceRegion set to " + geoId + " (" + (Native.Iso2Of(geoId) ?? "?") + ")");
                return RegionWriteResult.Written;
            }

            Log.Warn("The ordinary DeviceRegion write did not take effect; UCPD is the likely cause");
            if (TryFallbackWrite(geoId) && Read() == geoId)
            {
                Log.Info("DeviceRegion set to " + geoId + " via the renamed-reg.exe fallback");
                return RegionWriteResult.WrittenViaFallback;
            }

            Log.Error("DeviceRegion could not be set to " + geoId);
            return RegionWriteResult.Failed;
        }

        private static bool TryDirectWrite(int geoId)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = root.CreateSubKey(KeyPath))
                {
                    if (key == null) return false;
                    key.SetValue(ValueName, geoId, RegistryValueKind.DWord);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("The direct DeviceRegion write failed -- " + Log.Describe(ex));
                return false;
            }
        }

        /// <summary>
        /// Writes the single DeviceRegion value through a copy of reg.exe under a name
        /// UCPD does not recognise. Scoped to one value, and the copy is deleted straight
        /// away whatever happens.
        /// </summary>
        private static bool TryFallbackWrite(int geoId)
        {
            string source = Path.Combine(Paths.SystemDirectory, "reg.exe");
            string copy = Path.Combine(Path.GetTempPath(), "eul_" + Guid.NewGuid().ToString("N") + ".exe");

            try
            {
                if (!File.Exists(source))
                {
                    Log.Error("reg.exe was not found at " + source);
                    return false;
                }

                File.Copy(source, copy, true);

                var psi = new ProcessStartInfo(copy)
                {
                    Arguments = "add \"HKLM\\" + KeyPath + "\" /v " + ValueName +
                                " /t REG_DWORD /d " + geoId.ToString(System.Globalization.CultureInfo.InvariantCulture) + " /f",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = Process.Start(psi))
                {
                    if (process == null) return false;
                    process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(20000))
                    {
                        Log.Error("The fallback write did not finish within 20 seconds");
                        return false;
                    }
                    if (process.ExitCode != 0)
                    {
                        Log.Error("The fallback write exited with " + process.ExitCode + " " + stderr.Trim());
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("The fallback DeviceRegion write failed", ex);
                return false;
            }
            finally
            {
                try { if (File.Exists(copy)) File.Delete(copy); }
                catch (IOException ex) { Log.Warn("The temporary reg.exe copy could not be deleted -- " + Log.Describe(ex)); }
            }
        }

        /// <summary>
        /// Puts DeviceRegion back. When Windows had not cached a region before we ran, the
        /// value is deleted rather than set to a guess, so Windows recomputes it as it
        /// would have on a fresh install.
        /// </summary>
        public static bool Restore(State state)
        {
            if (!state.OriginalRegionPresent)
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (RegistryKey key = root.OpenSubKey(KeyPath, true))
                    {
                        if (key != null && key.GetValue(ValueName, null) != null) key.DeleteValue(ValueName, false);
                    }
                    Log.Info("DeviceRegion removed; Windows had not cached one before");
                    return Read() == null;
                }
                catch (Exception ex)
                {
                    Log.Error("DeviceRegion could not be removed", ex);
                    return false;
                }
            }

            RegionWriteResult result = Write(state.OriginalRegionGeoId);
            if (result == RegionWriteResult.Failed) return false;

            Log.Info("DeviceRegion restored to " + state.OriginalRegionGeoId +
                     (state.OriginalRegionFromLegacy ? " (value adopted from a v1 install)" : string.Empty));
            return true;
        }

        /// <summary>
        /// Records what the region was before we change it, once. Repeat runs must not
        /// overwrite this with our own value.
        /// </summary>
        public static void RememberOriginal(State state)
        {
            if (state.EverApplied || state.OriginalRegionFromLegacy) return;

            int? current = Read();
            state.OriginalRegionPresent = current.HasValue;
            state.OriginalRegionGeoId = current.HasValue ? current.Value : 0;
            Log.Info("Original DeviceRegion recorded: " +
                     (current.HasValue ? current.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "absent"));
        }
    }
}
