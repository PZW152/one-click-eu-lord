// The test suite.
//
// Two hard rules, both from the spec: no administrator rights, and nothing outside
// a temporary sandbox. Paths.OverrideDataRoot and Paths.OverridePolicyFile redirect
// every file the engine touches, so the real System32 policy file, the real
// ProgramData folder, the registry and the task scheduler are never involved.
//
// Compiled into the same assembly as the product, which is why it can see internals,
// and given its own entry point through csc /main.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using EULord.Core;

namespace EULord.Tests
{
    internal static class TestRunner
    {
        private static int _passed;
        private static readonly List<string> _failures = new List<string>();
        private static string _sandbox;
        private static string _fixture;

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string repoRoot = args.Length > 0 ? args[0] : FindRepoRoot();

            // Snapshot mode: tools\Render.ps1 uses this to produce the design images.
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--render", StringComparison.OrdinalIgnoreCase)) continue;
                string outputDir = i + 1 < args.Length ? args[i + 1] : Path.Combine(repoRoot, "dist", "ui");
                return UiRenderer.Render(repoRoot, outputDir);
            }
            _fixture = Path.Combine(repoRoot, "tests", "fixtures", "policy-sample.json");
            if (!File.Exists(_fixture))
            {
                Console.Error.WriteLine("fixture not found: " + _fixture);
                return 2;
            }

            _sandbox = Path.Combine(Path.GetTempPath(), "eulord-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sandbox);

            // Everything the engine can read or write is redirected before the first
            // test runs: our own data folder, the "system" policy file, and the
            // ProgramData base that v1 installs would be looked for under.
            Paths.OverrideCommonAppData(Path.Combine(_sandbox, "programdata"));
            Paths.OverrideDataRoot(Path.Combine(_sandbox, "data"));
            Paths.OverridePolicyFile(Path.Combine(_sandbox, "system32", "IntegratedServicesRegionPolicySet.json"));
            Directory.CreateDirectory(Path.Combine(_sandbox, "system32"));

            if (!Paths.LegacyDataRoot("OneClickEUOverlord").StartsWith(_sandbox, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("refusing to run: the legacy lookup path is outside the sandbox");
                return 2;
            }

            Console.WriteLine("sandbox      " + _sandbox);
            Console.WriteLine("policy file  " + Paths.PolicyFile);
            Console.WriteLine("data root    " + Paths.DataRoot);
            Console.WriteLine();

            try
            {
                PlatformTests();
                JsonTests();
                CatalogTests();
                LocalizationTests();
                LogTests();
                PolicyReadingTests();
                SupersetRuleTests();
                PlanTests();
                StateTests();
                BackupTests();
                SystemFileTests();
                PersistenceXmlTests();
                AppMatchingTests();
                ShellTests();
                UpdateTests();
                CliTests();
            }
            catch (Exception ex)
            {
                _failures.Add("the suite threw before finishing -- " + ex);
            }
            finally
            {
                TryCleanSandbox();
            }

            Console.WriteLine();
            Console.WriteLine("passed " + _passed + ", failed " + _failures.Count);
            foreach (string failure in _failures) Console.WriteLine("  FAIL  " + failure);

            return _failures.Count == 0 ? 0 : 1;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures"))) return dir.FullName;
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }

        private static void TryCleanSandbox()
        {
            try { if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, true); }
            catch (IOException) { }
        }

        // -- assertions -------------------------------------------------------

        private static void Section(string name)
        {
            Console.WriteLine("-- " + name);
        }

        private static void Check(string what, bool condition)
        {
            if (condition) { _passed++; Console.WriteLine("   ok    " + what); }
            else { _failures.Add(what); Console.WriteLine("   FAIL  " + what); }
        }

        private static void Equal(string what, object expected, object actual)
        {
            bool same = expected == null ? actual == null : expected.Equals(actual);
            if (same) { _passed++; Console.WriteLine("   ok    " + what); }
            else
            {
                string detail = what + " (expected " + Show(expected) + ", got " + Show(actual) + ")";
                _failures.Add(detail);
                Console.WriteLine("   FAIL  " + detail);
            }
        }

        private static string Show(object value) { return value == null ? "null" : "'" + value + "'"; }

        private static void Throws<TException>(string what, Action action) where TException : Exception
        {
            try
            {
                action();
                Check(what + " should have thrown " + typeof(TException).Name, false);
            }
            catch (TException) { Check(what, true); }
            catch (Exception ex)
            {
                Check(what + " threw " + ex.GetType().Name + " instead of " + typeof(TException).Name, false);
            }
        }

        private static string Fixture() { return File.ReadAllText(_fixture, Encoding.UTF8); }

        private static void InstallFixtureAsLivePolicy()
        {
            File.WriteAllText(Paths.PolicyFile, Fixture(), new UTF8Encoding(false));
        }

        // -- platform ---------------------------------------------------------

        private static void PlatformTests()
        {
            Section("platform");

            Console.WriteLine("         process is " + (Environment.Is64BitProcess ? "64-bit" : "32-bit") +
                              " on a " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit") + " OS");
            Console.WriteLine("         system directory resolves to " + Paths.SystemDirectory);

            Check("the system directory exists", Directory.Exists(Paths.SystemDirectory));
            Check("the system directory holds cmd.exe",
                  File.Exists(Path.Combine(Paths.SystemDirectory, "cmd.exe")));
            Check("the system directory holds schtasks.exe",
                  File.Exists(Path.Combine(Paths.SystemDirectory, "schtasks.exe")));
            Check("the system directory holds reg.exe",
                  File.Exists(Path.Combine(Paths.SystemDirectory, "reg.exe")));

            // SysWOW64 keeps its own copy of the policy file. Writing that one would
            // change nothing the 64-bit system reads, so resolving to it is a silent
            // no-op and must never happen.
            Check("the system directory is not SysWOW64",
                  Paths.SystemDirectory.IndexOf("SysWOW64", StringComparison.OrdinalIgnoreCase) < 0);

            if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
                Check("a 32-bit process reaches the real System32 through Sysnative",
                      Paths.SystemDirectory.EndsWith("Sysnative", StringComparison.OrdinalIgnoreCase));

            Check("the policy file path sits in the system directory",
                  Paths.PolicyFile.EndsWith(Paths.PolicyFileName, StringComparison.OrdinalIgnoreCase));
        }

        // -- json -------------------------------------------------------------

        private static void JsonTests()
        {
            Section("json");

            var root = JsonValue.Parse(Fixture()) as JsonObject;
            Check("the fixture parses into an object", root != null);
            Equal("the $schema key survives parsing", "schemas/IntegratedServicesRegionPolicySet.Schema.1.1.0.json",
                  root.GetString("$schema"));
            Equal("the first key is $schema", "$schema", root.Keys[0]);
            Equal("key order is preserved", "policies", root.Keys[2]);

            string rendered = root.ToJson();
            var again = JsonValue.Parse(rendered) as JsonObject;
            Equal("a round trip keeps the schema", root.GetString("$schema"), again.GetString("$schema"));
            Equal("a round trip keeps the policy count",
                  PolicyFile.Policies(root).Count, PolicyFile.Policies(again).Count);

            Check("string arrays render on one line", rendered.Contains("[\"AT\", \"BE\""));

            // An unknown key, including nested values and number spelling, must survive.
            JsonObject future = null;
            foreach (JsonValue v in PolicyFile.Policies(again).Items)
            {
                var p = v as JsonObject;
                if (p != null && p.Has("futureKey")) future = p;
            }
            Check("an unknown key survives a round trip", future != null);
            Equal("a nested unknown string survives", "preserve me", future.GetObject("futureKey").GetString("note"));
            Check("a nested number keeps its spelling",
                  future.GetObject("futureKey").GetArray("nested").ToJson().Contains("2.5"));

            Throws<JsonException>("trailing content is rejected", () => JsonValue.Parse("{} garbage"));
            Throws<JsonException>("an unterminated object is rejected", () => JsonValue.Parse("{\"a\": 1"));
            Throws<JsonException>("a bad escape is rejected", () => JsonValue.Parse("\"\\q\""));
            Throws<JsonException>("a malformed number is rejected", () => JsonValue.Parse("{\"a\": 1.2.3}"));

            var withBom = JsonValue.Parse("\uFEFF{\"a\": 1}") as JsonObject;
            Equal("a BOM on input is tolerated", 1, withBom.GetInt("a", 0));
            Check("a BOM is never written back", !withBom.ToJson().StartsWith("\uFEFF", StringComparison.Ordinal));

            var escapes = JsonValue.Parse("{\"k\":\"a\\\"b\\\\c\\nd\\u00e9\"}") as JsonObject;
            Equal("escapes decode", "a\"b\\c\nd\u00e9", escapes.GetString("k"));
            Equal("escapes re-encode", "a\"b\\c\nd\u00e9",
                  (JsonValue.Parse(escapes.ToJson()) as JsonObject).GetString("k"));

            var ordered = new JsonObject();
            ordered["z"] = new JsonNumber(1);
            ordered["a"] = new JsonNumber(2);
            ordered["z"] = new JsonNumber(3);
            Equal("re-assigning a key keeps its position", "z", ordered.Keys[0]);
            Equal("re-assigning a key keeps the count", 2, ordered.Count);
            ordered.Remove("z");
            Equal("removing a key drops it from the order", "a", ordered.Keys[0]);
            Equal("removing a key drops the count", 1, ordered.Count);
        }

        // -- catalog ----------------------------------------------------------

        private static void CatalogTests()
        {
            Section("catalog");

            Equal("the catalog holds 68 policies", 68, Catalog.Count);
            Equal("40 are gains", 40, Catalog.CountOfClass(PolicyClass.Benefit));
            Equal("7 are losses", 7, Catalog.CountOfClass(PolicyClass.Loss));
            Equal("21 are matters of taste", 21, Catalog.CountOfClass(PolicyClass.Choice));
            Equal("the three classes account for every entry", Catalog.Count,
                  Catalog.CountOfClass(PolicyClass.Benefit) + Catalog.CountOfClass(PolicyClass.Loss) +
                  Catalog.CountOfClass(PolicyClass.Choice));

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int badId = 0, missingText = 0, badGuid = 0;

            foreach (CatalogEntry entry in Catalog.All)
            {
                if (!ids.Add(entry.Id)) badId++;
                if (!guids.Add(entry.Guid)) badGuid++;

                if (string.IsNullOrEmpty(entry.Describe(Lang.ZhHant)) ||
                    string.IsNullOrEmpty(entry.Describe(Lang.ZhHans)) ||
                    string.IsNullOrEmpty(entry.Describe(Lang.En))) missingText++;

                if (entry.Guid.Length != 38 || entry.Guid[0] != '{' || entry.Guid[37] != '}') badGuid++;
            }

            Equal("every Id is unique", 0, badId);
            Equal("every GUID is unique and well formed", 0, badGuid);
            Equal("every entry has all three languages", 0, missingText);

            Check("lookup by GUID works", Catalog.ByGuid("{1bca278a-5d11-4acf-ad2f-f9ab6d7f93a6}") != null);
            Check("lookup by GUID ignores case",
                  Catalog.ByGuid("{1BCA278A-5D11-4ACF-AD2F-F9AB6D7F93A6}") != null);
            Check("lookup by Id works", Catalog.ById("EdgeUninstall") != null);
            Check("an unknown GUID returns null", Catalog.ByGuid("{00000000-0000-0000-0000-000000000000}") == null);
            Check("a null GUID returns null", Catalog.ByGuid(null) == null);

            Equal("EdgeUninstall is a gain", PolicyClass.Benefit, Catalog.ById("EdgeUninstall").Class);
            Equal("NarratorScripts is a loss", PolicyClass.Loss, Catalog.ById("NarratorScripts").Class);
            Equal("Copilot is a matter of taste", PolicyClass.Choice, Catalog.ById("Copilot").Class);
        }

        // -- localization -----------------------------------------------------

        private static void LocalizationTests()
        {
            Section("localization");

            Lang lang;
            Check("zh-Hant-TW is Traditional", S.TryMatch("zh-Hant-TW", out lang) && lang == Lang.ZhHant);
            Check("zh-TW is Traditional", S.TryMatch("zh-TW", out lang) && lang == Lang.ZhHant);
            Check("zh-HK is Traditional", S.TryMatch("zh-HK", out lang) && lang == Lang.ZhHant);
            Check("zh-MO is Traditional", S.TryMatch("zh-MO", out lang) && lang == Lang.ZhHant);
            Check("zh-Hans-CN is Simplified", S.TryMatch("zh-Hans-CN", out lang) && lang == Lang.ZhHans);
            Check("zh-CN is Simplified", S.TryMatch("zh-CN", out lang) && lang == Lang.ZhHans);
            Check("zh-SG is Simplified", S.TryMatch("zh-SG", out lang) && lang == Lang.ZhHans);
            Check("zh alone is Simplified", S.TryMatch("zh", out lang) && lang == Lang.ZhHans);
            Check("an underscore separator works", S.TryMatch("zh_TW", out lang) && lang == Lang.ZhHant);
            Check("en-US is not Chinese", !S.TryMatch("en-US", out lang));
            Check("an empty tag is not Chinese", !S.TryMatch(string.Empty, out lang));
            Check("a null tag is not Chinese", !S.TryMatch(null, out lang));

            Equal("an explicit zh-Hant preference wins", Lang.ZhHant, Program.ResolveLanguage("zh-Hant"));
            Equal("an explicit zh-Hans preference wins", Lang.ZhHans, Program.ResolveLanguage("zh-Hans"));
            Equal("an explicit en preference wins", Lang.En, Program.ResolveLanguage("en"));
            Check("auto detection does not throw", Enum.IsDefined(typeof(Lang), Program.ResolveLanguage("auto")));

            // Every declared string must carry all three languages.
            int blanks = 0, total = 0;
            foreach (FieldInfo field in typeof(S).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(L)) continue;
                var text = (L)field.GetValue(null);
                total++;
                foreach (Lang candidate in new[] { Lang.ZhHant, Lang.ZhHans, Lang.En })
                {
                    if (!string.IsNullOrEmpty(text.For(candidate))) continue;
                    blanks++;
                    Console.WriteLine("         missing " + candidate + " for S." + field.Name);
                }
            }
            Check("there are strings to check", total > 50);
            Equal("no declared string is missing a translation", 0, blanks);

            Lang before = S.Current;
            try
            {
                S.Current = Lang.En;
                Equal("an implicit conversion follows the current language", "Apply superset", (string)S.ApplySuperset);
                S.Current = Lang.ZhHant;
                Equal("switching language switches the text", "套用超集", (string)S.ApplySuperset);
                Equal("Invariant stays English whatever the UI language", "Apply superset", S.ApplySuperset.Invariant);
            }
            finally { S.Current = before; }
        }

        // -- log --------------------------------------------------------------

        private static void LogTests()
        {
            Section("log");

            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            try
            {
                // zh-TW uses the Minguo calendar by default, which turned 2026 into 115
                // in v1's log. The stamp must stay Gregorian.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("zh-TW");
                string stamp = Log.Stamp();
                Check("the timestamp is Gregorian under zh-TW, not Minguo (" + stamp + ")",
                      stamp.StartsWith("20", StringComparison.Ordinal));
                Equal("the timestamp has the expected shape", 19, stamp.Length);
            }
            finally { Thread.CurrentThread.CurrentCulture = before; }

            Log.Info("test: an entry written by the suite");
            Check("the log file is created inside the sandbox", File.Exists(Paths.LogFile));
            Check("the log file is under the sandbox data root",
                  Paths.LogFile.StartsWith(Paths.DataRoot, StringComparison.OrdinalIgnoreCase));

            string contents = File.ReadAllText(Paths.LogFile, Encoding.UTF8);
            Check("the entry is in the file", contents.Contains("an entry written by the suite"));
            Check("the level is recorded", contents.Contains("INFO"));

            Check("an exception describes to one line",
                  Log.Describe(new InvalidOperationException("line one\r\nline two")).IndexOf('\n') < 0);
            Check("a null exception is handled", Log.Describe(null).Length > 0);
        }

        // -- reading policies -------------------------------------------------

        private static void PolicyReadingTests()
        {
            Section("policy reading");

            JsonObject root = PolicyFile.Parse(Fixture());
            Check("the shipped file is recognised as a Windows original", PolicyFile.IsWindowsOriginal(root));

            JsonArray policies = PolicyFile.Policies(root);
            Equal("the fixture has ten entries", 10, policies.Count);

            var edge = policies[0] as JsonObject;
            Equal("Edge is off globally", "disabled", edge.GetString("defaultState"));
            Equal("Edge is on in Ireland", "enabled", PolicyFile.StateIn(edge, "IE"));
            Equal("Edge is off in the United States", "disabled", PolicyFile.StateIn(edge, "US"));

            var narrator = policies[1] as JsonObject;
            Equal("Narrator scripting is on globally", "enabled", narrator.GetString("defaultState"));
            Equal("Narrator scripting is off in Ireland", "disabled", PolicyFile.StateIn(narrator, "IE"));

            var chineseOnly = policies[8] as JsonObject;
            Equal("a non-EEA list leaves Ireland on the default", "enabled", PolicyFile.StateIn(chineseOnly, "IE"));
            Equal("a non-EEA list still applies to its own countries", "enabled", PolicyFile.StateIn(chineseOnly, "CN"));

            IList<string> codes = PolicyFile.DeriveEeaCodes(root);
            Equal("the EEA list is read from the file itself", 36, codes.Count);
            Check("the EEA list contains Ireland", codes.Contains("IE"));
            Check("the EEA list contains Norway", codes.Contains("NO"));
            Check("the EEA list excludes China", !codes.Contains("CN"));
            Check("the EEA list excludes the United States", !codes.Contains("US"));

            // A file with no recognisable list falls back rather than returning nothing.
            JsonObject bare = PolicyFile.Parse(
                "{\"$schema\":\"x\",\"version\":\"1.0\",\"policies\":[{\"guid\":\"{a}\",\"defaultState\":\"enabled\"}]}");
            Check("a file with no region conditions is not a Windows original", !PolicyFile.IsWindowsOriginal(bare));
            Check("the EEA list falls back when the file names none", PolicyFile.DeriveEeaCodes(bare).Contains("IE"));

            Throws<JsonException>("a root that is not an object is rejected", () => PolicyFile.Parse("[]"));
            Throws<JsonException>("a file with no policies array is rejected", () => PolicyFile.Parse("{\"a\":1}"));
        }

        // -- the superset rule ------------------------------------------------

        private static void SupersetRuleTests()
        {
            Section("superset rule");

            // This is the rule v1 got wrong, so it is tested directly rather than only
            // through the plan.
            Equal("a gain takes the EU state",
                  "enabled", Decide(PolicyClass.Benefit, "disabled", "enabled", PolicyOverride.Auto));
            Equal("a gain takes the EU state when the EU state is off",
                  "disabled", Decide(PolicyClass.Benefit, "enabled", "disabled", PolicyOverride.Auto));

            Equal("a loss keeps the global state",
                  "enabled", Decide(PolicyClass.Loss, "enabled", "disabled", PolicyOverride.Auto));
            Equal("a loss keeps the global state in the other direction",
                  "disabled", Decide(PolicyClass.Loss, "disabled", "enabled", PolicyOverride.Auto));

            Equal("a matter of taste keeps the global state by default",
                  "enabled", Decide(PolicyClass.Choice, "enabled", "disabled", PolicyOverride.Auto));
            Equal("a matter of taste takes the EU state when ticked",
                  "disabled", Decide(PolicyClass.Choice, "enabled", "disabled", PolicyOverride.Eea));

            Equal("an override can refuse a gain",
                  "disabled", Decide(PolicyClass.Benefit, "disabled", "enabled", PolicyOverride.Global));
            Equal("an override can accept a loss",
                  "disabled", Decide(PolicyClass.Loss, "enabled", "disabled", PolicyOverride.Eea));
            Equal("an override to auto returns to the rule",
                  "enabled", Decide(PolicyClass.Loss, "enabled", "disabled", PolicyOverride.Auto));

            var unclassified = new PolicyDecision
            {
                Guid = "{ffffffff-0000-4000-8000-000000000001}",
                GlobalState = "disabled", EeaState = "enabled", Entry = null, Override = PolicyOverride.Auto
            };
            Equal("an unclassified policy is left as Windows shipped it",
                  "disabled", PolicyFile.Decide(unclassified));
            Check("an unclassified policy reports itself as such", unclassified.Unclassified);

            var same = new PolicyDecision
            {
                GlobalState = "enabled", EeaState = "enabled",
                Entry = Catalog.ById("EdgeUninstall"), Override = PolicyOverride.Auto
            };
            Equal("a policy that does not differ keeps its state", "enabled", PolicyFile.Decide(same));
            Check("a policy that does not differ reports no difference", !same.Differs);
        }

        private static string Decide(PolicyClass cls, string global, string eea, PolicyOverride over)
        {
            CatalogEntry entry = null;
            foreach (CatalogEntry candidate in Catalog.OfClass(cls)) { entry = candidate; break; }

            var decision = new PolicyDecision
            {
                Guid = entry.Guid, GlobalState = global, EeaState = eea, Entry = entry, Override = over
            };
            return PolicyFile.Decide(decision);
        }

        // -- the plan ---------------------------------------------------------

        private static void PlanTests()
        {
            Section("plan");

            var state = new State { RegionIso = "IE", RegionGeoId = 68 };
            PolicyPlan plan = PolicyFile.BuildPlan(Fixture(), "IE", state);

            Equal("the plan covers every entry in the file", 10, plan.TotalPolicies);
            Equal("the plan reports one decision per unique GUID", 9, plan.Decisions.Count);
            Equal("six entries differ in Ireland", 6, plan.DifferingPolicies);
            Equal("one differing policy is unclassified", 1, plan.Unclassified.Count);
            Equal("the unclassified GUID is named",
                  "{ffffffff-0000-4000-8000-000000000001}", plan.Unclassified[0]);

            Equal("three gains in the fixture were taken", 3, plan.BenefitsTaken);
            Equal("one loss in the fixture was refused", 1, plan.LossesRefused);
            Equal("no matter of taste was taken by default", 0, plan.ChoicesTaken);
            Equal("the totals come from the catalog, not the fixture", 40, plan.BenefitsTotal);

            JsonObject rewritten = PolicyFile.Parse(plan.Json);
            Check("the rewritten file is no longer a Windows original", !PolicyFile.IsWindowsOriginal(rewritten));

            int withConditions = 0;
            var byGuid = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonValue v in PolicyFile.Policies(rewritten).Items)
            {
                var p = v as JsonObject;
                if (p.Has("conditions")) withConditions++;
                string guid = p.GetString("guid");
                if (!byGuid.ContainsKey(guid)) byGuid[guid] = new List<string>();
                byGuid[guid].Add(p.GetString("defaultState"));
            }
            Equal("no region condition remains", 0, withConditions);
            Equal("the rewritten file keeps every entry", 10, PolicyFile.Policies(rewritten).Count);
            Equal("the schema key survives the rewrite",
                  "schemas/IntegratedServicesRegionPolicySet.Schema.1.1.0.json", rewritten.GetString("$schema"));
            Equal("the version key survives the rewrite", "1.0", rewritten.GetString("version"));

            Equal("Edge ends up on", "enabled", byGuid["{1bca278a-5d11-4acf-ad2f-f9ab6d7f93a6}"][0]);
            Equal("Narrator scripting stays on", "enabled", byGuid["{6033b294-79ff-47eb-bbde-97b4c6479217}"][0]);
            Equal("Copilot stays on until asked", "enabled", byGuid["{4ac54d32-0799-405f-9bf4-1fe094cd859c}"][0]);
            Equal("a gain whose EU state is off ends up off",
                  "disabled", byGuid["{61bf5046-c5db-4cd3-b6bf-929e5b421a6a}"][0]);
            Equal("the unclassified policy is untouched",
                  "disabled", byGuid["{ffffffff-0000-4000-8000-000000000001}"][0]);

            List<string> duplicates = byGuid["{36996754-e327-483a-902f-523e2ba03239}"];
            Equal("both copies of a duplicated GUID are present", 2, duplicates.Count);
            Equal("both copies are rewritten the same way", duplicates[0], duplicates[1]);

            // The unknown key has to survive a rewrite, or a future Windows build
            // would quietly lose configuration.
            bool futureSurvived = false;
            foreach (JsonValue v in PolicyFile.Policies(rewritten).Items)
            {
                var p = v as JsonObject;
                if (p.Has("futureKey")) futureSurvived = true;
            }
            Check("an unknown key survives the rewrite", futureSurvived);

            // Ticking a matter of taste changes exactly that policy.
            state.SetOverride("Copilot", PolicyOverride.Eea);
            PolicyPlan withChoice = PolicyFile.BuildPlan(Fixture(), "IE", state);
            Equal("ticking Copilot takes one matter of taste", 1, withChoice.ChoicesTaken);
            Equal("ticking Copilot does not change the gains", plan.BenefitsTaken, withChoice.BenefitsTaken);
            Equal("ticking Copilot does not change the refused losses", plan.LossesRefused, withChoice.LossesRefused);

            string copilotState = null;
            foreach (JsonValue v in PolicyFile.Policies(PolicyFile.Parse(withChoice.Json)).Items)
            {
                var p = v as JsonObject;
                if (string.Equals(p.GetString("guid"), "{4ac54d32-0799-405f-9bf4-1fe094cd859c}", StringComparison.OrdinalIgnoreCase))
                    copilotState = p.GetString("defaultState");
            }
            Equal("ticking Copilot turns it off", "disabled", copilotState);
            state.SetOverride("Copilot", PolicyOverride.Auto);

            // Running the same plan twice must produce identical bytes, or the
            // scheduled task would churn the system file every time it fires.
            PolicyPlan again = PolicyFile.BuildPlan(Fixture(), "IE", state);
            Equal("building the plan twice gives identical output", plan.Json, again.Json);

            // A country outside the EEA should decide nothing.
            PolicyPlan american = PolicyFile.BuildPlan(Fixture(), "US", state);
            Equal("no policy differs for a non-EEA country", 0, american.DifferingPolicies);
            Equal("no gain is taken for a non-EEA country", 0, american.BenefitsTaken);

            // Another EEA country unlocks the same set as Ireland.
            PolicyPlan german = PolicyFile.BuildPlan(Fixture(), "DE", state);
            Equal("another EEA country differs on the same count", plan.DifferingPolicies, german.DifferingPolicies);
            Equal("another EEA country takes the same gains", plan.BenefitsTaken, german.BenefitsTaken);
        }

        // -- state ------------------------------------------------------------

        private static void StateTests()
        {
            Section("state");

            var state = new State();
            Check("a fresh state has never been applied", !state.EverApplied);
            Equal("the default region is Ireland", 68, state.RegionGeoId);
            Equal("the default region code is IE", "IE", state.RegionIso);
            Equal("nothing is overridden by default", PolicyOverride.Auto, state.OverrideFor("Copilot"));
            Equal("an unknown Id has no override", PolicyOverride.Auto, state.OverrideFor("NoSuchPolicy"));

            state.SetOverride("Copilot", PolicyOverride.Eea);
            state.SetOverride("NarratorScripts", PolicyOverride.Eea);
            state.SetOverride("EdgeUninstall", PolicyOverride.Global);
            state.AppliedUtc = State.UtcNow();
            state.ProductVersion = Build.Version;
            state.OriginalRegionPresent = true;
            state.OriginalRegionGeoId = 244;
            state.Unclassified.Add("{ffffffff-0000-4000-8000-000000000001}");
            state.RecordRemoval("Edge", "Microsoft Edge", "registered uninstaller");

            Equal("one matter of taste is selected", 1, state.SelectedChoices().Count);
            Equal("the selected matter of taste is Copilot", "Copilot", state.SelectedChoices()[0]);

            state.SetOverride("Copilot", PolicyOverride.Auto);
            Equal("clearing an override removes it", 2, state.Overrides.Count);
            Equal("clearing an override deselects the choice", 0, state.SelectedChoices().Count);
            state.SetOverride("Copilot", PolicyOverride.Eea);

            StateStore.Save(state);
            Check("state.json lands in the sandbox", File.Exists(Paths.StateFile));
            Check("state.json has no byte order mark", !SystemFile.HasUtf8Bom(Paths.StateFile));

            State loaded = StateStore.Load();
            Equal("the schema version is current", State.CurrentSchema, loaded.SchemaVersion);
            Equal("the region survives a round trip", 68, loaded.RegionGeoId);
            Equal("the applied timestamp survives", state.AppliedUtc, loaded.AppliedUtc);
            Equal("the original region survives", 244, loaded.OriginalRegionGeoId);
            Check("the original region presence survives", loaded.OriginalRegionPresent);
            Equal("three overrides survive", 3, loaded.Overrides.Count);
            Equal("an EU override survives", PolicyOverride.Eea, loaded.OverrideFor("Copilot"));
            Equal("a global override survives", PolicyOverride.Global, loaded.OverrideFor("EdgeUninstall"));
            Equal("the unclassified list survives", 1, loaded.Unclassified.Count);
            Equal("the removal record survives", 1, loaded.RemovedApps.Count);
            Equal("the removal names the app", "Microsoft Edge", loaded.FindRemoved("Edge").DisplayName);
            Check("the removal records its method", loaded.FindRemoved("Edge").Method.Length > 0);
            Check("an unremoved app has no record", loaded.FindRemoved("Store") == null);

            JsonObject saved = StateStore.ToJson(state);
            JsonObject savedOverrides = saved.GetObject("overrides");
            int autoWritten = 0;
            foreach (string key in savedOverrides.Keys)
                if (string.Equals(savedOverrides.GetString(key), "auto", StringComparison.Ordinal)) autoWritten++;
            Equal("overrides left on auto are not written out", 0, autoWritten);
            Equal("only the explicit overrides are written", 3, savedOverrides.Count);

            string json = saved.ToJson();
            Check("overrides are written in catalog order",
                  json.IndexOf("EdgeUninstall", StringComparison.Ordinal) <
                  json.IndexOf("NarratorScripts", StringComparison.Ordinal));

            // v1 wrote originalRegion under a different name; an upgrade must still be
            // able to put the machine back.
            JsonObject legacy = PolicyFile.Parse(
                "{\"schemaVersion\":2,\"mode\":\"Gentle\",\"originalRegion\":244,\"policies\":[]}");
            State migrated = StateStore.FromJson(legacy);
            Check("a v1 original region is adopted", migrated.OriginalRegionPresent);
            Equal("the adopted value is right", 244, migrated.OriginalRegionGeoId);
            Check("the adopted value is marked as coming from v1", migrated.OriginalRegionFromLegacy);
            Equal("a migrated state reports the current schema", State.CurrentSchema, migrated.SchemaVersion);

            PolicyOverride parsed;
            Check("eea parses", StateStore.TryParseOverride("eea", out parsed) && parsed == PolicyOverride.Eea);
            Check("EEA parses whatever the case", StateStore.TryParseOverride("EEA", out parsed));
            Check("global parses", StateStore.TryParseOverride("global", out parsed) && parsed == PolicyOverride.Global);
            Check("rubbish does not parse", !StateStore.TryParseOverride("sideways", out parsed));

            File.Delete(Paths.StateFile);
        }

        // -- backups ----------------------------------------------------------

        private static void BackupTests()
        {
            Section("backups");

            if (Directory.Exists(Paths.BackupDir)) Directory.Delete(Paths.BackupDir, true);
            InstallFixtureAsLivePolicy();

            Equal("a shipped file is classified as the Windows original",
                  PolicyFileOrigin.WindowsOriginal, Backups.ClassifyLive());
            Check("there is no backup to begin with", !Backups.HasAnyBackup());

            Backups.Capture();
            Check("the first-seen copy is written", File.Exists(Paths.OriginalBackup));
            Check("the pristine copy is written", File.Exists(Paths.PristineBackup));
            Check("backups are reported as available", Backups.HasAnyBackup());
            Check("the backup has no byte order mark", !SystemFile.HasUtf8Bom(Paths.PristineBackup));

            string firstSeen = File.ReadAllText(Paths.OriginalBackup, Encoding.UTF8);

            // Our own rewrite must never be mistaken for a source.
            var state = new State();
            PolicyPlan plan = PolicyFile.BuildPlan(Fixture(), "IE", state);
            SystemFile.WriteAllTextNoBom(Paths.PolicyFile, plan.Json);

            Equal("our rewrite is classified as ours", PolicyFileOrigin.Ours, Backups.ClassifyLive());

            Backups.Capture();
            Equal("capturing again does not overwrite the first-seen copy",
                  firstSeen, File.ReadAllText(Paths.OriginalBackup, Encoding.UTF8));
            Check("the pristine copy is still a Windows original",
                  PolicyFile.IsWindowsOriginal(PolicyFile.Parse(File.ReadAllText(Paths.PristineBackup, Encoding.UTF8))));

            string source = Backups.PristineSource();
            Check("a source is still available once the live file is ours", source != null);
            Check("the source is a Windows original", PolicyFile.IsWindowsOriginal(PolicyFile.Parse(source)));

            // Rebuilding from the backup must give the same result as the first run,
            // which is what stops edits compounding across Windows updates.
            PolicyPlan rebuilt = PolicyFile.BuildPlan(source, "IE", state);
            Equal("rebuilding from the backup reproduces the same file", plan.Json, rebuilt.Json);

            // When Windows ships a new file, the pristine copy follows it.
            string changed = Fixture().Replace("\"version\": \"1.0\"", "\"version\": \"1.1\"");
            File.WriteAllText(Paths.PolicyFile, changed, new UTF8Encoding(false));
            Backups.Capture();
            Check("a newly shipped file refreshes the pristine copy",
                  File.ReadAllText(Paths.PristineBackup, Encoding.UTF8).Contains("\"version\": \"1.1\""));
            Equal("a newly shipped file does not disturb the first-seen copy",
                  firstSeen, File.ReadAllText(Paths.OriginalBackup, Encoding.UTF8));

            Check("the restore source is the newest backup",
                  Backups.RestoreSourcePath() == Paths.PristineBackup);

            // With no backup and a rewritten live file there is no honest source, and
            // the engine must say so rather than guess.
            Directory.Delete(Paths.BackupDir, true);
            SystemFile.WriteAllTextNoBom(Paths.PolicyFile, plan.Json);
            Check("no source is offered when none exists", Backups.PristineSource() == null);
            Check("no restore source is offered when none exists", Backups.RestoreSourcePath() == null);

            InstallFixtureAsLivePolicy();
        }

        // -- files ------------------------------------------------------------

        private static void SystemFileTests()
        {
            Section("file writing");

            string path = Path.Combine(_sandbox, "no-bom.json");
            SystemFile.WriteAllTextNoBom(path, "{\"a\":1}");
            Check("the policy writer emits no byte order mark", !SystemFile.HasUtf8Bom(path));

            byte[] bytes = File.ReadAllBytes(path);
            Equal("the first byte is the opening brace", (byte)'{', bytes[0]);

            // The shipped policy file has no BOM and must keep it that way, so the
            // detector has to be right in both directions.
            string withBom = Path.Combine(_sandbox, "with-bom.json");
            File.WriteAllText(withBom, "{\"a\":1}", new UTF8Encoding(true));
            Check("a byte order mark is detected when present", SystemFile.HasUtf8Bom(withBom));

            SystemFile.WriteAllTextNoBom(path, "{\"b\":\"\u6b50\u723a\"}");
            Equal("non-ASCII text round trips as UTF-8", "\u6b50\u723a",
                  (JsonValue.Parse(SystemFile.ReadAllText(path)) as JsonObject).GetString("b"));
            Check("overwriting leaves no byte order mark", !SystemFile.HasUtf8Bom(path));

            Equal("the same text hashes the same", SystemFile.Sha256("abc"), SystemFile.Sha256("abc"));
            Check("different text hashes differently", SystemFile.Sha256("abc") != SystemFile.Sha256("abd"));
            Equal("the hash is 64 hex characters", 64, SystemFile.Sha256("abc").Length);

            string copy = Path.Combine(_sandbox, "copy.json");
            File.Copy(path, copy, true);
            Check("identical files compare equal", SystemFile.SameContent(path, copy));
            SystemFile.WriteAllTextNoBom(copy, "{\"b\":\"different\"}");
            Check("different files compare unequal", !SystemFile.SameContent(path, copy));
        }

        // -- scheduled task ---------------------------------------------------

        private static void PersistenceXmlTests()
        {
            Section("scheduled task definition");

            string xml = Persistence.BuildTaskXml();

            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");

            Check("the definition is well-formed XML", doc.DocumentElement != null);
            Equal("the root element is Task", "Task", doc.DocumentElement.LocalName);

            Check("there is a logon trigger", doc.SelectSingleNode("/t:Task/t:Triggers/t:LogonTrigger", ns) != null);
            Check("there is a daily trigger", doc.SelectSingleNode("/t:Task/t:Triggers/t:CalendarTrigger/t:ScheduleByDay", ns) != null);
            Check("there is an event trigger", doc.SelectSingleNode("/t:Task/t:Triggers/t:EventTrigger", ns) != null);

            XmlNode subscription = doc.SelectSingleNode("/t:Task/t:Triggers/t:EventTrigger/t:Subscription", ns);
            Check("the event trigger watches Windows Update",
                  subscription != null && subscription.InnerText.Contains("Microsoft-Windows-WindowsUpdateClient"));
            Check("the event trigger watches the installation-successful event",
                  subscription.InnerText.Contains("EventID=19"));

            XmlNode principal = doc.SelectSingleNode("/t:Task/t:Principals/t:Principal/t:UserId", ns);
            Equal("the task runs as SYSTEM", "S-1-5-18", principal.InnerText);
            Equal("the task runs with the highest privileges", "HighestAvailable",
                  doc.SelectSingleNode("/t:Task/t:Principals/t:Principal/t:RunLevel", ns).InnerText);

            XmlNode command = doc.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Command", ns);
            Equal("the task runs the installed copy", Paths.InstalledExe, command.InnerText);
            Equal("the task re-applies rather than applies", "--reapply --quiet",
                  doc.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Arguments", ns).InnerText);

            XmlNode start = doc.SelectSingleNode("/t:Task/t:Triggers/t:CalendarTrigger/t:StartBoundary", ns);
            Check("the start boundary is Gregorian, not Minguo (" + start.InnerText + ")",
                  start.InnerText.StartsWith("20", StringComparison.Ordinal));

            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("zh-TW");
                string underTaiwan = Persistence.BuildTaskXml();
                var taiwanDoc = new XmlDocument { XmlResolver = null };
                taiwanDoc.LoadXml(underTaiwan);
                Check("the definition still parses under zh-TW", taiwanDoc.DocumentElement != null);
                Check("the start boundary stays Gregorian under zh-TW",
                      underTaiwan.Contains("<StartBoundary>20"));
            }
            finally { Thread.CurrentThread.CurrentCulture = before; }
        }

        // -- app matching -----------------------------------------------------

        private static void AppMatchingTests()
        {
            Section("app matching");

            Check("an exact name matches", AppRemoval.Matches("Microsoft.WindowsStore", "Microsoft.WindowsStore"));
            Check("case is ignored", AppRemoval.Matches("microsoft.windowsstore", "Microsoft.WindowsStore"));
            Check("a different name does not match", !AppRemoval.Matches("Microsoft.WindowsCalculator", "Microsoft.WindowsStore"));
            Check("a trailing wildcard matches", AppRemoval.Matches("Microsoft.Windows.Copilot.Extra", "Microsoft.Windows.Copilot*"));
            Check("a surrounding wildcard matches", AppRemoval.Matches("MicrosoftWindows.Client.StartExperiences", "*StartExperiences*"));
            Check("a surrounding wildcard rejects a non-match", !AppRemoval.Matches("Microsoft.WindowsStore", "*StartExperiences*"));
            Check("null values do not match", !AppRemoval.Matches(null, "x"));
            Check("a null pattern does not match", !AppRemoval.Matches("x", null));
            Check("any of several patterns can match",
                  AppRemoval.MatchesAny("MSTeams", new[] { "MicrosoftTeams", "MSTeams" }));
            Check("no pattern matching means no match",
                  !AppRemoval.MatchesAny("MSTeams", new[] { "Microsoft.WindowsStore" }));

            // Every candidate must be removable by a real route, or it should not be
            // offered at all. This is the user's safety rule, expressed as a test.
            int misconfigured = 0;
            foreach (AppCandidate candidate in AppRemoval.Candidates)
            {
                bool appx = candidate.Route == AppRoute.Appx && candidate.PackagePatterns.Length > 0;
                bool registered = candidate.Route == AppRoute.Registered && candidate.RegistryNames.Length > 0;
                if (!appx && !registered) { misconfigured++; Console.WriteLine("         no route for " + candidate.Id); }

                if (string.IsNullOrEmpty(candidate.ReinstallUri) && candidate.ReinstallNote == null)
                {
                    misconfigured++;
                    Console.WriteLine("         no way back for " + candidate.Id);
                }

                if (string.IsNullOrEmpty(candidate.RequiresPolicy)) continue;
                if (Catalog.ById(candidate.RequiresPolicy) != null) continue;
                misconfigured++;
                Console.WriteLine("         unknown prerequisite policy for " + candidate.Id);
            }
            Equal("every app has a legitimate route and a way back", 0, misconfigured);
            Check("the list covers Microsoft's own apps only", AppRemoval.Candidates.Length >= 8);
        }

        // -- shell ------------------------------------------------------------

        private static void ShellTests()
        {
            Section("command splitting");

            string file, arguments;

            Shell.SplitCommand("MsiExec.exe /X{6E669770-C655-388E-835E-C146C8EC6A2E}", out file, out arguments);
            Equal("an msiexec command splits off the program", "MsiExec.exe", file);
            Equal("an msiexec command keeps its arguments", "/X{6E669770-C655-388E-835E-C146C8EC6A2E}", arguments);

            Shell.SplitCommand("\"C:\\Program Files\\Microsoft\\Edge\\Installer\\setup.exe\" --uninstall --msedge",
                               out file, out arguments);
            Equal("a quoted path with spaces splits correctly",
                  "C:\\Program Files\\Microsoft\\Edge\\Installer\\setup.exe", file);
            Equal("a quoted path keeps its arguments", "--uninstall --msedge", arguments);

            Shell.SplitCommand("C:\\Users\\x\\OneDriveSetup.exe /uninstall", out file, out arguments);
            Equal("an unquoted path splits at the extension", "C:\\Users\\x\\OneDriveSetup.exe", file);
            Equal("an unquoted path keeps its arguments", "/uninstall", arguments);

            Shell.SplitCommand("setup.exe", out file, out arguments);
            Equal("a bare program is the whole command", "setup.exe", file);
            Equal("a bare program has no arguments", string.Empty, arguments);

            Shell.SplitCommand(null, out file, out arguments);
            Equal("a null command yields an empty program", string.Empty, file);

            // Running something harmless proves the pipe handling, which is where a
            // naive implementation deadlocks.
            ProcessResult result = Shell.Run(Shell.System32("cmd.exe"), "/c echo eulord-probe", 20000);
            Check("a child process reports success", result.Ok);
            Check("its output is captured", result.StdOut.Contains("eulord-probe"));
            Check("it did not time out", !result.TimedOut);

            ProcessResult failure = Shell.Run(Shell.System32("cmd.exe"), "/c exit 3", 20000);
            Equal("a non-zero exit code is reported", 3, failure.ExitCode);
            Check("a failure is not reported as success", !failure.Ok);
            Check("a failure describes itself", failure.Failure.Contains("3"));
        }

        // -- updates ----------------------------------------------------------

        private static void UpdateTests()
        {
            Section("version comparison");

            Check("a higher patch is newer", Updates.IsNewer("99.0.0"));
            Check("the current version is not newer", !Updates.IsNewer(Build.Version));
            Check("a leading v is tolerated", Updates.IsNewer("v99.0.0"));
            Check("an older version is not newer", !Updates.IsNewer("1.0.0"));
            Check("a two-part version works", Updates.IsNewer("99.1"));
            Check("rubbish is not newer", !Updates.IsNewer("not-a-version"));
            Check("an empty string is not newer", !Updates.IsNewer(string.Empty));
            Check("null is not newer", !Updates.IsNewer(null));

            Check("the version is three numbers", Build.Version.Split('.').Length == 3);
        }

        // -- command line -----------------------------------------------------

        private static void CliTests()
        {
            Section("command line");

            Options empty = Cli.Parse(new string[0]);
            Check("no arguments means the window", empty.WantsGui);
            Check("no arguments is not an error", empty.Error == null);

            Options apply = Cli.Parse(new[] { "--apply" });
            Equal("--apply selects the apply action", "apply", apply.Action);
            Check("an action means no window", !apply.WantsGui);

            Check("a slash form works", Cli.Parse(new[] { "/status" }).Action == "status");
            Check("a bare form works", Cli.Parse(new[] { "status" }).Action == "status");
            Check("--help is recognised", Cli.Parse(new[] { "--help" }).Help);
            Check("-h is recognised", Cli.Parse(new[] { "-h" }).Help);
            Check("--version is recognised", Cli.Parse(new[] { "--version" }).Version);
            Check("--quiet is recognised", Cli.Parse(new[] { "--reapply", "--quiet" }).Quiet);
            Check("--json is recognised", Cli.Parse(new[] { "--status", "--json" }).Json);

            Check("an unknown argument is an error", Cli.Parse(new[] { "--nonsense" }).Error != null);
            Check("two actions are an error", Cli.Parse(new[] { "--apply", "--restore" }).Error != null);
            Check("a missing value is an error", Cli.Parse(new[] { "--region" }).Error != null);
            Check("a malformed override is an error", Cli.Parse(new[] { "--override", "Copilot" }).Error != null);
            Check("an unknown override value is an error",
                  Cli.Parse(new[] { "--override", "Copilot=sideways" }).Error != null);
            Check("an unknown override Id is an error",
                  Cli.Parse(new[] { "--override", "NoSuchPolicy=eea" }).Error != null);
            Check("a valid override parses", Cli.Parse(new[] { "--override", "Copilot=eea" }).Error == null);

            Options overrides = Cli.Parse(new[] { "--override", "Copilot=eea", "--override", "EdgeUninstall=global" });
            Equal("two overrides are collected", 2, overrides.Overrides.Count);

            var state = new State();
            Cli.ApplyOptionsToState(Cli.Parse(new[] { "--choices", "all" }), state);
            Equal("--choices all selects every matter of taste",
                  Catalog.CountOfClass(PolicyClass.Choice), state.SelectedChoices().Count);

            Cli.ApplyOptionsToState(Cli.Parse(new[] { "--choice", "Copilot,OfficeRecent" }), state);
            Equal("--choice replaces the selection rather than adding to it", 2, state.SelectedChoices().Count);

            Cli.ApplyOptionsToState(Cli.Parse(new[] { "--choice", "NarratorScripts" }), state);
            Equal("--choice refuses a loss, which is not a matter of taste", 0, state.SelectedChoices().Count);
            Equal("refusing a loss leaves it alone", PolicyOverride.Auto, state.OverrideFor("NarratorScripts"));

            Cli.ApplyOptionsToState(Cli.Parse(new[] { "--override", "NarratorScripts=eea" }), state);
            Equal("--override can accept a loss when asked explicitly",
                  PolicyOverride.Eea, state.OverrideFor("NarratorScripts"));

            Cli.ApplyOptionsToState(Cli.Parse(new[] { "--region", "IE" }), state);
            Equal("--region accepts an ISO code", "IE", state.RegionIso);
            Equal("--region resolves the ISO code to a GeoID", 68, state.RegionGeoId);

            Cli.ApplyOptionsToState(Cli.Parse(new[] { "--region", "68" }), state);
            Equal("--region accepts a GeoID", 68, state.RegionGeoId);
            Equal("--region resolves the GeoID to an ISO code", "IE", state.RegionIso);

            int kept = state.RegionGeoId;
            Cli.ApplyOptionsToState(Cli.Parse(new[] { "--region", "ZZ" }), state);
            Equal("an unrecognised region is ignored rather than guessed", kept, state.RegionGeoId);

            Cli.ApplyOptionsToState(Cli.Parse(new[] { "--language", "zh-Hans" }), state);
            Equal("--language is recorded", "zh-Hans", state.LanguagePreference);

            // Removing apps must not be reachable without a person confirming each one.
            foreach (string word in new[] { "--uninstall", "--remove-app", "--uninstall-app" })
                Check("there is no " + word + " switch", Cli.Parse(new[] { word }).Error != null);
        }
    }
}
