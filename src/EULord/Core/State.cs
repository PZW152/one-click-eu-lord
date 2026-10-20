// state.json: what we did, what the machine looked like before, and what the user
// chose. Everything needed to undo our work and nothing else.
//
// Schema numbering continues v1's lineage (v1 wrote 1 then 2) so an upgrade is an
// upgrade rather than a new product.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EULord.Core
{
    /// <summary>What the user asked for on a single policy.</summary>
    internal enum PolicyOverride
    {
        /// <summary>Let the superset rule decide.</summary>
        Auto,
        /// <summary>Force the Ireland/EEA state.</summary>
        Eea,
        /// <summary>Force the global default, whatever the classification says.</summary>
        Global
    }

    internal sealed class RemovedApp
    {
        public string Id;
        public string DisplayName;
        public string RemovedUtc;
        public string Method;

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o["id"] = new JsonString(Id ?? string.Empty);
            o["displayName"] = new JsonString(DisplayName ?? string.Empty);
            o["removedUtc"] = new JsonString(RemovedUtc ?? string.Empty);
            o["method"] = new JsonString(Method ?? string.Empty);
            return o;
        }

        public static RemovedApp FromJson(JsonObject o)
        {
            if (o == null) return null;
            string id = o.GetString("id");
            if (string.IsNullOrEmpty(id)) return null;
            return new RemovedApp
            {
                Id = id,
                DisplayName = o.GetString("displayName") ?? id,
                RemovedUtc = o.GetString("removedUtc") ?? string.Empty,
                Method = o.GetString("method") ?? string.Empty
            };
        }
    }

    internal sealed class State
    {
        public const int CurrentSchema = 3;

        public int SchemaVersion = CurrentSchema;
        public string ProductVersion = string.Empty;

        /// <summary>Null until the superset has been applied at least once.</summary>
        public string AppliedUtc;

        public string RegionIso = Native.IrelandIso2;
        public int RegionGeoId = Native.IrelandGeoId;

        /// <summary>False when DeviceRegion did not exist before we touched it, so restore deletes it.</summary>
        public bool OriginalRegionPresent;
        public int OriginalRegionGeoId;
        public bool OriginalRegionFromLegacy;

        /// <summary>Policy Id -&gt; the user's explicit decision. Absent means Auto.</summary>
        public readonly Dictionary<string, PolicyOverride> Overrides =
            new Dictionary<string, PolicyOverride>(StringComparer.OrdinalIgnoreCase);

        /// <summary>GUIDs that differ in the EEA but are not in the catalog, so were left untouched.</summary>
        public readonly List<string> Unclassified = new List<string>();

        public readonly List<RemovedApp> RemovedApps = new List<RemovedApp>();

        /// <summary>"auto", "zh-Hant", "zh-Hans" or "en".</summary>
        public string LanguagePreference = "auto";

        public bool EverApplied { get { return !string.IsNullOrEmpty(AppliedUtc); } }

        public PolicyOverride OverrideFor(string id)
        {
            PolicyOverride v;
            return id != null && Overrides.TryGetValue(id, out v) ? v : PolicyOverride.Auto;
        }

        public void SetOverride(string id, PolicyOverride value)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (value == PolicyOverride.Auto) Overrides.Remove(id);
            else Overrides[id] = value;
        }

        /// <summary>The Choice policies the user has opted into the EEA state for.</summary>
        public List<string> SelectedChoices()
        {
            var ids = new List<string>();
            foreach (CatalogEntry e in Catalog.OfClass(PolicyClass.Choice))
                if (OverrideFor(e.Id) == PolicyOverride.Eea) ids.Add(e.Id);
            return ids;
        }

        public RemovedApp FindRemoved(string appId)
        {
            foreach (RemovedApp r in RemovedApps)
                if (string.Equals(r.Id, appId, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        public void RecordRemoval(string appId, string displayName, string method)
        {
            RemovedApp existing = FindRemoved(appId);
            if (existing != null) RemovedApps.Remove(existing);
            RemovedApps.Add(new RemovedApp
            {
                Id = appId,
                DisplayName = displayName,
                RemovedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Method = method
            });
        }

        public static string UtcNow()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }
    }

    internal static class StateStore
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static State Load()
        {
            State state = TryRead(Paths.StateFile);
            if (state != null) return state;

            state = new State();
            AdoptLegacy(state);
            return state;
        }

        private static State TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var root = JsonValue.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
                if (root == null) return null;
                return FromJson(root);
            }
            catch (Exception ex)
            {
                Log.Warn("state.json could not be read, starting from defaults -- " + Log.Describe(ex));
                return null;
            }
        }

        /// <summary>
        /// Picks up what v1.0-v1.2 recorded so an upgrade can still restore the machine.
        /// The original region is the only thing worth salvaging; it is the one fact we
        /// cannot recompute once DeviceRegion has been overwritten.
        /// </summary>
        private static void AdoptLegacy(State state)
        {
            foreach (string folder in Paths.LegacyFolders)
            {
                string path = Path.Combine(Paths.LegacyDataRoot(folder), "state.json");
                try
                {
                    if (!File.Exists(path)) continue;
                    var root = JsonValue.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
                    if (root == null) continue;

                    int legacyRegion = root.GetInt("originalRegion", 0);
                    if (legacyRegion > 0)
                    {
                        state.OriginalRegionPresent = true;
                        state.OriginalRegionGeoId = legacyRegion;
                        state.OriginalRegionFromLegacy = true;
                        Log.Info("Adopted originalRegion=" + legacyRegion + " from legacy state in " + folder);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Legacy state in " + folder + " could not be read -- " + Log.Describe(ex));
                }
            }
        }

        internal static State FromJson(JsonObject root)
        {
            var s = new State();
            s.SchemaVersion = root.GetInt("schemaVersion", State.CurrentSchema);
            s.ProductVersion = root.GetString("productVersion") ?? string.Empty;
            s.AppliedUtc = root.GetString("appliedUtc");
            s.RegionIso = root.GetString("regionIso") ?? Native.IrelandIso2;
            s.RegionGeoId = root.GetInt("regionGeoId", Native.IrelandGeoId);
            s.OriginalRegionPresent = root.GetBool("originalRegionPresent", false);
            s.OriginalRegionGeoId = root.GetInt("originalRegionGeoId", 0);
            s.OriginalRegionFromLegacy = root.GetBool("originalRegionFromLegacy", false);
            s.LanguagePreference = root.GetString("language") ?? "auto";

            JsonObject overrides = root.GetObject("overrides");
            if (overrides != null)
            {
                foreach (string id in overrides.Keys)
                {
                    PolicyOverride value;
                    if (TryParseOverride(overrides.GetString(id), out value)) s.SetOverride(id, value);
                }
            }

            JsonArray unclassified = root.GetArray("unclassified");
            if (unclassified != null) s.Unclassified.AddRange(unclassified.ToStringList());

            JsonArray removed = root.GetArray("removedApps");
            if (removed != null)
            {
                foreach (JsonValue v in removed.Items)
                {
                    RemovedApp r = RemovedApp.FromJson(v as JsonObject);
                    if (r != null) s.RemovedApps.Add(r);
                }
            }

            // v1 (schema 1 and 2) stored the original region under a different name.
            if (s.SchemaVersion < 3 && !s.OriginalRegionPresent)
            {
                int legacy = root.GetInt("originalRegion", 0);
                if (legacy > 0)
                {
                    s.OriginalRegionPresent = true;
                    s.OriginalRegionGeoId = legacy;
                    s.OriginalRegionFromLegacy = true;
                }
            }

            s.SchemaVersion = State.CurrentSchema;
            return s;
        }

        internal static bool TryParseOverride(string text, out PolicyOverride value)
        {
            value = PolicyOverride.Auto;
            if (string.IsNullOrEmpty(text)) return false;
            switch (text.ToLowerInvariant())
            {
                case "eea": value = PolicyOverride.Eea; return true;
                case "global": value = PolicyOverride.Global; return true;
                case "auto": value = PolicyOverride.Auto; return true;
                default: return false;
            }
        }

        internal static string OverrideName(PolicyOverride value)
        {
            switch (value)
            {
                case PolicyOverride.Eea: return "eea";
                case PolicyOverride.Global: return "global";
                default: return "auto";
            }
        }

        internal static JsonObject ToJson(State s)
        {
            var o = new JsonObject();
            o["schemaVersion"] = new JsonNumber(State.CurrentSchema);
            o["productVersion"] = new JsonString(s.ProductVersion ?? string.Empty);
            o["appliedUtc"] = s.AppliedUtc == null ? (JsonValue)JsonNull.Instance : new JsonString(s.AppliedUtc);
            o["regionIso"] = new JsonString(s.RegionIso ?? Native.IrelandIso2);
            o["regionGeoId"] = new JsonNumber(s.RegionGeoId);
            o["originalRegionPresent"] = new JsonBool(s.OriginalRegionPresent);
            o["originalRegionGeoId"] = new JsonNumber(s.OriginalRegionGeoId);
            o["originalRegionFromLegacy"] = new JsonBool(s.OriginalRegionFromLegacy);
            o["language"] = new JsonString(s.LanguagePreference ?? "auto");

            var overrides = new JsonObject();
            // Stable, catalog order so the file diffs cleanly between runs.
            foreach (CatalogEntry e in Catalog.All)
            {
                PolicyOverride v = s.OverrideFor(e.Id);
                if (v != PolicyOverride.Auto) overrides[e.Id] = new JsonString(OverrideName(v));
            }
            o["overrides"] = overrides;

            o["unclassified"] = JsonArray.Of(s.Unclassified);

            var removed = new JsonArray();
            foreach (RemovedApp r in s.RemovedApps) removed.Add(r.ToJson());
            o["removedApps"] = removed;

            return o;
        }

        /// <summary>Writes state.json atomically, so an interrupted save never truncates it.</summary>
        public static void Save(State state)
        {
            Paths.EnsureDataLayout();
            string json = ToJson(state).ToJson() + "\n";
            string target = Paths.StateFile;
            string temp = target + ".new";

            File.WriteAllText(temp, json, Utf8NoBom);
            if (File.Exists(target))
            {
                string backup = target + ".bak";
                File.Replace(temp, target, backup, true);
                try { File.Delete(backup); } catch (IOException) { }
            }
            else
            {
                File.Move(temp, target);
            }
        }
    }
}
