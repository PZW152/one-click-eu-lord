// Whether Windows is in light or dark mode, and noticing when that changes.
//
// The window has no native caption bar, so it cannot inherit the system's
// light/dark treatment the way an ordinary window does: it has to ask, and it has
// to keep asking. Windows broadcasts WM_SETTINGCHANGE with "ImmersiveColorSet"
// when someone flips the switch, but subscribing to that needs an HWND, so this
// polls the one registry value instead -- once a second, one DWORD read.

using System;
using Microsoft.Win32;

namespace EULord.Core
{
    internal static class SystemTheme
    {
        private const string PersonalizeKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightTheme = "AppsUseLightTheme";

        /// <summary>
        /// True when Windows is asking apps to paint dark. Defaults to light if the
        /// value is missing, which is what Windows itself does.
        /// </summary>
        public static bool IsDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue(AppsUseLightTheme, null);
                    if (value == null) return false;
                    return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("The system theme could not be read -- " + Log.Describe(ex));
                return false;
            }
        }
    }
}
