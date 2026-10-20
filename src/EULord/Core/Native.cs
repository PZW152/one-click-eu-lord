// The small amount of Win32 EULord genuinely needs: the user's language list and
// the geography tables that translate between GeoIDs and ISO country codes.
//
// GeoIDs are never hardcoded as a table here. DeviceRegion stores a GeoID, the
// policy file stores ISO-3166 alpha-2 codes, and Windows itself owns the mapping
// between them -- so we ask Windows and cache the answer. Ireland's 68 is the one
// fallback, used only if enumeration fails outright.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace EULord.Core
{
    internal static class Native
    {
        private const uint MUI_LANGUAGE_NAME = 0x8;
        private const int GEOCLASS_NATION = 16;
        private const int GEO_ISO2 = 4;
        private const int GEO_FRIENDLYNAME = 8;

        /// <summary>Ireland. Both an EU member and in the EEA, which is why it is the default.</summary>
        public const int IrelandGeoId = 68;
        public const string IrelandIso2 = "IE";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetUserPreferredUILanguages(
            uint dwFlags, out uint pulNumLanguages, char[] pwszLanguagesBuffer, ref uint pcchLanguagesBuffer);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetGeoInfoW(int location, int geoType, StringBuilder lpGeoData, int cchData, int langId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EnumSystemGeoID(int geoClass, int parentGeoId, GeoEnumProc lpGeoEnumProc);

        [DllImport("kernel32.dll")]
        private static extern int GetUserGeoID(int geoClass);

        private delegate bool GeoEnumProc(int geoId);

        /// <summary>The user's preferred UI languages, most preferred first.</summary>
        public static IList<string> UserPreferredUILanguages()
        {
            var result = new List<string>();
            try
            {
                uint count, chars = 0;
                if (!GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, out count, null, ref chars) || chars == 0)
                    return result;

                var buffer = new char[chars];
                if (!GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, out count, buffer, ref chars))
                    return result;

                // A double-null-terminated run of null-terminated names.
                var current = new StringBuilder();
                foreach (char c in buffer)
                {
                    if (c == '\0')
                    {
                        if (current.Length == 0) break;
                        result.Add(current.ToString());
                        current.Length = 0;
                    }
                    else current.Append(c);
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return result;
        }

        /// <summary>The GeoID Windows would pick for this user if DeviceRegion were absent.</summary>
        public static int UserGeoId()
        {
            try { return GetUserGeoID(GEOCLASS_NATION); }
            catch (EntryPointNotFoundException) { return 0; }
        }

        private static string GeoInfo(int geoId, int type)
        {
            try
            {
                var sb = new StringBuilder(128);
                int n = GetGeoInfoW(geoId, type, sb, sb.Capacity, 0);
                return n > 0 ? sb.ToString().Trim('\0').Trim() : null;
            }
            catch (EntryPointNotFoundException) { return null; }
        }

        /// <summary>The ISO-3166 alpha-2 code for a GeoID, e.g. 68 -&gt; "IE".</summary>
        public static string Iso2Of(int geoId) { return GeoInfo(geoId, GEO_ISO2); }

        /// <summary>The country's name in the user's own language, for the region picker.</summary>
        public static string FriendlyNameOf(int geoId) { return GeoInfo(geoId, GEO_FRIENDLYNAME); }

        private static Dictionary<string, int> _isoToGeo;
        private static readonly object _geoLock = new object();

        /// <summary>ISO-3166 alpha-2 -&gt; GeoID, built once by asking Windows to enumerate nations.</summary>
        public static Dictionary<string, int> IsoToGeoId()
        {
            lock (_geoLock)
            {
                if (_isoToGeo != null) return _isoToGeo;

                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    // The callback must stay rooted for the duration of the call.
                    GeoEnumProc callback = id =>
                    {
                        string iso = Iso2Of(id);
                        if (!string.IsNullOrEmpty(iso) && !map.ContainsKey(iso)) map[iso] = id;
                        return true;
                    };
                    EnumSystemGeoID(GEOCLASS_NATION, 0, callback);
                    GC.KeepAlive(callback);
                }
                catch (EntryPointNotFoundException) { }

                if (!map.ContainsKey(IrelandIso2)) map[IrelandIso2] = IrelandGeoId;
                _isoToGeo = map;
                return _isoToGeo;
            }
        }

        /// <summary>The GeoID for an ISO code, or 0 when Windows does not know it.</summary>
        public static int GeoIdOf(string iso2)
        {
            if (string.IsNullOrEmpty(iso2)) return 0;
            int id;
            return IsoToGeoId().TryGetValue(iso2, out id) ? id : 0;
        }

        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }
    }
}
