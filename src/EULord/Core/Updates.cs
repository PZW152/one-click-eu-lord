// The only code in EULord that touches the network, and it runs only when the user
// presses "Check for updates".
//
// It reads the latest release tag and nothing else: no identifiers, no telemetry,
// no automatic checks on launch, and it never downloads or installs anything. If
// there is a newer version the user is told the number; getting it is their move.

using System;
using System.Net;
using System.Text.RegularExpressions;

namespace EULord.Core
{
    internal static class Updates
    {
        private const string LatestReleaseApi =
            "https://api.github.com/repos/PZW152/one-click-eu-lord/releases/latest";

        /// <summary>The newest published version, e.g. "2.0.1", or null if it could not be read.</summary>
        public static string LatestVersion()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApi);
                request.UserAgent = "OneClickEULord/" + Build.Version;      // GitHub rejects requests without one
                request.Accept = "application/vnd.github+json";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;

                string body;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new System.IO.StreamReader(response.GetResponseStream()))
                    body = reader.ReadToEnd();

                var root = JsonValue.Parse(body) as JsonObject;
                string tag = root == null ? null : root.GetString("tag_name");
                if (string.IsNullOrEmpty(tag)) return null;

                return tag.TrimStart('v', 'V');
            }
            catch (Exception ex)
            {
                Log.Warn("The update check failed -- " + Log.Describe(ex));
                return null;
            }
        }

        /// <summary>True when <paramref name="candidate"/> is a higher version than this build.</summary>
        public static bool IsNewer(string candidate)
        {
            Version theirs, ours;
            if (!TryParse(candidate, out theirs) || !TryParse(Build.Version, out ours)) return false;
            return theirs > ours;
        }

        private static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text)) return false;

            // Release tags are written "v2.0.0", so tolerate the prefix wherever a
            // version string arrives rather than only where it is fetched.
            Match match = Regex.Match(text.Trim().TrimStart('v', 'V'), @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!match.Success) return false;

            int major = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            int minor = match.Groups[2].Success
                ? int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            int patch = match.Groups[3].Success
                ? int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;

            version = new Version(major, minor, patch);
            return true;
        }
    }
}
