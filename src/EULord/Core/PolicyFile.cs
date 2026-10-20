// IntegratedServicesRegionPolicySet.json: how EULord reads it, decides, and rewrites it.
//
// Shape of the shipped file (build 26300: 84 entries, 79 unique GUIDs):
//
//   { "$schema": "...", "version": "1.0",
//     "policies": [ { "$comment": "...", "guid": "{...}",
//                     "defaultState": "enabled" | "disabled",
//                     "conditions": { "region": { "enabled" | "disabled": ["AT", ...] } } } ] }
//
// Windows resolves a policy by reading DeviceRegion, converting it to an ISO-3166
// alpha-2 code, and taking the matching region condition if there is one, otherwise
// defaultState. So setting defaultState to the state we want and deleting the
// condition makes the outcome region-independent -- and, crucially, lets us decide
// each policy separately instead of swallowing the whole EEA set.
//
// The rewrite is always built from a pristine Windows-shipped source, never from a
// file we already rewrote, so repeated runs and Windows updates never compound.

using System;
using System.Collections.Generic;

namespace EULord.Core
{
    internal static class PolicyStates
    {
        public const string Enabled = "enabled";
        public const string Disabled = "disabled";

        public static string Opposite(string state)
        {
            return string.Equals(state, Enabled, StringComparison.OrdinalIgnoreCase) ? Disabled : Enabled;
        }
    }

    /// <summary>What we decided about one policy, and why. Drives both the plan and the UI table.</summary>
    internal sealed class PolicyDecision
    {
        public string Guid;
        public string Comment;
        public string GlobalState;
        public string EeaState;
        public string TargetState;
        public CatalogEntry Entry;
        public PolicyOverride Override;

        /// <summary>True when Ireland's state differs from the global default. Only these matter.</summary>
        public bool Differs { get { return !string.Equals(GlobalState, EeaState, StringComparison.OrdinalIgnoreCase); } }

        /// <summary>True when the policy has no classification, so we leave it exactly as Windows shipped it.</summary>
        public bool Unclassified { get { return Entry == null; } }

        /// <summary>True when the EEA state is what will be written.</summary>
        public bool TakesEea
        {
            get { return Differs && string.Equals(TargetState, EeaState, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>True when the user overrode the superset rule for this policy.</summary>
        public bool Overridden
        {
            get
            {
                if (Override == PolicyOverride.Auto || Entry == null) return false;
                return !string.Equals(TargetState, AutoStateFor(Entry.Class, GlobalState, EeaState),
                                      StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static string AutoStateFor(PolicyClass cls, string globalState, string eeaState)
        {
            switch (cls)
            {
                case PolicyClass.Benefit: return eeaState;   // the gain: take it
                case PolicyClass.Loss: return globalState;   // the loss: refuse it
                default: return globalState;                 // taste: the user's call, off by default
            }
        }
    }

    internal sealed class PolicyPlan
    {
        /// <summary>One entry per unique GUID, in file order.</summary>
        public readonly List<PolicyDecision> Decisions = new List<PolicyDecision>();

        /// <summary>GUIDs that differ in the EEA but carry no classification; left untouched.</summary>
        public readonly List<string> Unclassified = new List<string>();

        /// <summary>The EEA country codes the policy file itself names.</summary>
        public IList<string> EeaCodes = new List<string>();

        public int TotalPolicies;
        public int DifferingPolicies;
        public int BenefitsTaken;
        public int BenefitsTotal;
        public int LossesRefused;
        public int LossesTotal;
        public int ChoicesTaken;
        public int ChoicesTotal;

        /// <summary>The rewritten document, ready to write. No BOM, no region conditions.</summary>
        public string Json;

        public int Gains { get { return BenefitsTaken + ChoicesTaken; } }
    }

    internal static class PolicyFile
    {
        /// <summary>
        /// Used only if the policy file names no recognisable EEA list, which would mean
        /// Microsoft changed the format. The 30 EU/EEA states plus Switzerland and the
        /// French overseas territories Microsoft lists.
        /// </summary>
        private static readonly string[] FallbackEeaCodes =
        {
            "AT", "BE", "BG", "CH", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GF", "GP",
            "GR", "HR", "HU", "IE", "IS", "IT", "LI", "LT", "LU", "LV", "MT", "MQ", "NL", "NO",
            "PL", "PT", "RE", "RO", "SE", "SI", "SK", "YT"
        };

        public static JsonObject Parse(string json)
        {
            var root = JsonValue.Parse(json) as JsonObject;
            if (root == null) throw new JsonException("The policy file's root is not an object.");
            if (root.GetArray("policies") == null) throw new JsonException("The policy file has no \"policies\" array.");
            return root;
        }

        public static JsonArray Policies(JsonObject root) { return root.GetArray("policies"); }

        /// <summary>
        /// True when this is a file Windows shipped rather than one we wrote. Our output
        /// never contains a region condition, so the presence of one is the tell. This is
        /// what stops a rewrite being built on top of a previous rewrite.
        /// </summary>
        public static bool IsWindowsOriginal(JsonObject root)
        {
            JsonArray policies = Policies(root);
            if (policies == null) return false;
            foreach (JsonValue v in policies.Items)
            {
                var p = v as JsonObject;
                if (p != null && p.Has("conditions")) return true;
            }
            return false;
        }

        /// <summary>The state a policy has inside the given country, per its region condition.</summary>
        public static string StateIn(JsonObject policy, string iso2)
        {
            string defaultState = policy.GetString("defaultState") ?? PolicyStates.Disabled;
            JsonObject conditions = policy.GetObject("conditions");
            if (conditions == null) return defaultState;

            JsonObject region = conditions.GetObject("region");
            if (region == null) return defaultState;

            if (Lists(region, PolicyStates.Enabled, iso2)) return PolicyStates.Enabled;
            if (Lists(region, PolicyStates.Disabled, iso2)) return PolicyStates.Disabled;
            return defaultState;
        }

        private static bool Lists(JsonObject region, string key, string iso2)
        {
            JsonArray codes = region.GetArray(key);
            if (codes == null) return false;
            foreach (JsonValue v in codes.Items)
            {
                var s = v as JsonString;
                if (s != null && string.Equals(s.Value, iso2, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// The EEA country list, read out of the policy file rather than hardcoded, so it
        /// tracks whatever Microsoft currently considers the EEA. The longest region list
        /// that contains Ireland wins.
        /// </summary>
        public static IList<string> DeriveEeaCodes(JsonObject root)
        {
            var best = new List<string>();
            JsonArray policies = Policies(root);
            if (policies != null)
            {
                foreach (JsonValue v in policies.Items)
                {
                    var p = v as JsonObject;
                    JsonObject region = p == null ? null : (p.GetObject("conditions") == null
                        ? null : p.GetObject("conditions").GetObject("region"));
                    if (region == null) continue;

                    foreach (string key in new[] { PolicyStates.Enabled, PolicyStates.Disabled })
                    {
                        JsonArray codes = region.GetArray(key);
                        if (codes == null) continue;
                        List<string> list = codes.ToStringList();
                        if (list.Contains(Native.IrelandIso2) && list.Count > best.Count) best = list;
                    }
                }
            }

            if (best.Count == 0) best = new List<string>(FallbackEeaCodes);
            best.Sort(StringComparer.Ordinal);
            return best;
        }

        /// <summary>
        /// Decides what every policy should be set to, and renders the file to write.
        /// <paramref name="pristineJson"/> must come from a Windows-shipped file.
        /// </summary>
        public static PolicyPlan BuildPlan(string pristineJson, string iso2, State state)
        {
            if (string.IsNullOrEmpty(iso2)) iso2 = Native.IrelandIso2;

            JsonObject root = Parse(pristineJson);
            var plan = new PolicyPlan { EeaCodes = DeriveEeaCodes(root) };

            plan.BenefitsTotal = Catalog.CountOfClass(PolicyClass.Benefit);
            plan.LossesTotal = Catalog.CountOfClass(PolicyClass.Loss);
            plan.ChoicesTotal = Catalog.CountOfClass(PolicyClass.Choice);

            JsonArray policies = Policies(root);
            plan.TotalPolicies = policies.Count;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (JsonValue v in policies.Items)
            {
                var policy = v as JsonObject;
                if (policy == null) continue;

                string guid = policy.GetString("guid");
                string globalState = policy.GetString("defaultState") ?? PolicyStates.Disabled;
                string eeaState = StateIn(policy, iso2);
                CatalogEntry entry = Catalog.ByGuid(guid);

                var decision = new PolicyDecision
                {
                    Guid = guid,
                    Comment = policy.GetString("$comment") ?? string.Empty,
                    GlobalState = globalState,
                    EeaState = eeaState,
                    Entry = entry,
                    Override = entry == null ? PolicyOverride.Auto : state.OverrideFor(entry.Id)
                };
                decision.TargetState = Decide(decision);

                // Rewrite in place: fix the state, then drop the region condition so the
                // outcome no longer depends on where Windows thinks the machine is.
                policy["defaultState"] = new JsonString(decision.TargetState);
                policy.Remove("conditions");

                // The same GUID appears more than once in the shipped file (5 of them on
                // build 26300, as exact duplicates). Every copy is rewritten identically;
                // only the first is reported, so counts stay honest.
                if (!seen.Add(guid ?? string.Empty)) continue;

                plan.Decisions.Add(decision);

                if (!decision.Differs) continue;
                plan.DifferingPolicies++;

                if (entry == null)
                {
                    plan.Unclassified.Add(guid);
                    continue;
                }

                bool takesEea = decision.TakesEea;
                switch (entry.Class)
                {
                    case PolicyClass.Benefit: if (takesEea) plan.BenefitsTaken++; break;
                    case PolicyClass.Loss: if (!takesEea) plan.LossesRefused++; break;
                    case PolicyClass.Choice: if (takesEea) plan.ChoicesTaken++; break;
                }
            }

            plan.Json = root.ToJson() + "\n";
            return plan;
        }

        /// <summary>
        /// The superset rule. This single method is the difference between v2 and v1's
        /// mistake of copying the EEA set wholesale.
        /// </summary>
        internal static string Decide(PolicyDecision d)
        {
            // Nothing to choose between: Ireland and the rest of the world agree.
            if (!d.Differs) return d.GlobalState;

            // A policy we have never classified is left exactly as Windows shipped it.
            // Guessing could hand the user a regression, which is the one thing the
            // superset promise forbids.
            if (d.Entry == null) return d.GlobalState;

            switch (d.Override)
            {
                case PolicyOverride.Eea: return d.EeaState;
                case PolicyOverride.Global: return d.GlobalState;
                default:
                    return PolicyDecision.AutoStateFor(d.Entry.Class, d.GlobalState, d.EeaState);
            }
        }

        /// <summary>
        /// Reports the live file without deciding anything, for the status readout.
        /// Returns null when the file cannot be read or parsed.
        /// </summary>
        public static JsonObject TryReadLive()
        {
            try
            {
                return Parse(SystemFile.ReadAllText(Paths.PolicyFile));
            }
            catch (Exception ex)
            {
                Log.Warn("The live policy file could not be read -- " + Log.Describe(ex));
                return null;
            }
        }
    }
}
