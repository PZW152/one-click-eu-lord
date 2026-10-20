// The headless side. Double-clicking opens the window; any argument runs without one.
//
// This exists for three callers: the scheduled task (--reapply --quiet), IT staff
// rolling the settings out to many machines, and anyone who would rather script it
// than click. Uninstalling apps is deliberately absent -- removing software is a
// GUI action with a confirmation per app, so an unattended run can never do it.
//
// Exit codes: 0 done, 1 failed, 2 bad usage, 3 needs administrator, 4 partly done.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using EULord.Core;

namespace EULord
{
    internal static class ExitCodes
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int Usage = 2;
        public const int NeedsAdmin = 3;
        public const int Partial = 4;
    }

    internal sealed class Options
    {
        public string Action;                       // apply, reapply, restore, purge, status
        public string Region;
        public string Language;
        public string DataDir;
        public string PolicyFile;
        public bool Quiet;
        public bool Json;
        public bool Yes;
        public bool Help;
        public bool Version;
        public readonly List<string> Choices = new List<string>();
        public readonly Dictionary<string, PolicyOverride> Overrides =
            new Dictionary<string, PolicyOverride>(StringComparer.OrdinalIgnoreCase);
        public string Error;

        public bool WantsGui { get { return Action == null && !Help && !Version; } }
    }

    internal static class Cli
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        private const int AttachParentProcess = -1;

        /// <summary>
        /// Joins the console of whatever launched us, so output lands in the caller's
        /// window. Fails harmlessly when there is no console, as under the scheduled
        /// task, and everything still reaches the log.
        /// </summary>
        public static void AttachToParentConsole()
        {
            try { AttachConsole(AttachParentProcess); }
            catch (Exception) { }
        }

        public static Options Parse(string[] args)
        {
            var options = new Options();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string lower = arg.ToLowerInvariant().TrimStart('-', '/');

                switch (lower)
                {
                    case "apply": case "reapply": case "restore": case "purge": case "status":
                        if (options.Action != null)
                        {
                            options.Error = "more than one action was given: " + options.Action + " and " + lower;
                            return options;
                        }
                        options.Action = lower;
                        break;

                    case "region": options.Region = Next(args, ref i, options, "region"); break;
                    case "language": case "lang": options.Language = Next(args, ref i, options, "language"); break;
                    case "data-dir": case "datadir": options.DataDir = Next(args, ref i, options, "data-dir"); break;
                    case "policy-file": options.PolicyFile = Next(args, ref i, options, "policy-file"); break;

                    case "choice": case "choices":
                        {
                            string value = Next(args, ref i, options, lower);
                            if (value == null) return options;
                            foreach (string piece in value.Split(','))
                                if (piece.Trim().Length > 0) options.Choices.Add(piece.Trim());
                            break;
                        }

                    case "override":
                        {
                            string value = Next(args, ref i, options, "override");
                            if (value == null) return options;
                            int split = value.IndexOf('=');
                            if (split <= 0)
                            {
                                options.Error = "--override expects Id=auto|eea|global, got " + value;
                                return options;
                            }
                            string id = value.Substring(0, split).Trim();
                            PolicyOverride parsed;
                            if (!StateStore.TryParseOverride(value.Substring(split + 1).Trim(), out parsed))
                            {
                                options.Error = "--override value must be auto, eea or global: " + value;
                                return options;
                            }
                            if (Catalog.ById(id) == null)
                            {
                                options.Error = "--override names an unknown policy Id: " + id;
                                return options;
                            }
                            options.Overrides[id] = parsed;
                            break;
                        }

                    case "quiet": case "q": options.Quiet = true; break;
                    case "json": options.Json = true; break;
                    case "yes": case "y": options.Yes = true; break;
                    case "help": case "h": case "?": options.Help = true; break;
                    case "version": case "v": options.Version = true; break;

                    default:
                        options.Error = "unknown argument: " + arg;
                        return options;
                }
            }
            return options;
        }

        private static string Next(string[] args, ref int i, Options options, string name)
        {
            if (i + 1 >= args.Length)
            {
                options.Error = "--" + name + " needs a value";
                return null;
            }
            return args[++i];
        }

        public static int Run(Options options)
        {
            if (options.Error != null) { Write(options.Error); Write(string.Empty); WriteHelp(); return ExitCodes.Usage; }
            if (options.Help) { WriteHelp(); return ExitCodes.Ok; }
            if (options.Version) { Write(Build.Version); return ExitCodes.Ok; }

            Log.EchoToConsole = !options.Quiet;
            Log.Banner(Build.Version, "cli " + options.Action);

            State state = StateStore.Load();
            ApplyOptionsToState(options, state);

            switch (options.Action)
            {
                case "status": return ReportStatus(state, options);
                case "apply": return Finish(Engine.Apply(state, null), options);
                case "reapply": return Finish(Engine.Reapply(state, null), options);
                case "restore": return Finish(Engine.Restore(state, null), options);
                case "purge":
                    if (!options.Yes)
                    {
                        Write("--purge deletes " + Paths.DataRoot + " including the backups. Re-run with --yes to confirm.");
                        return ExitCodes.Usage;
                    }
                    return Finish(Engine.Purge(state, null), options);
                default:
                    WriteHelp();
                    return ExitCodes.Usage;
            }
        }

        /// <summary>
        /// Folds command-line settings into the saved state, so --region and --choice
        /// persist exactly as the same change made in the window would.
        /// </summary>
        internal static void ApplyOptionsToState(Options options, State state)
        {
            if (!string.IsNullOrEmpty(options.Language)) state.LanguagePreference = options.Language;

            if (!string.IsNullOrEmpty(options.Region))
            {
                int geoId;
                string iso;
                if (int.TryParse(options.Region, NumberStyles.Integer, CultureInfo.InvariantCulture, out geoId))
                {
                    iso = Native.Iso2Of(geoId);
                }
                else
                {
                    iso = options.Region.ToUpperInvariant();
                    geoId = Native.GeoIdOf(iso);
                }

                if (geoId > 0 && !string.IsNullOrEmpty(iso))
                {
                    state.RegionGeoId = geoId;
                    state.RegionIso = iso;
                }
                else Write("the region " + options.Region + " was not recognised; keeping " + state.RegionIso);
            }

            if (options.Choices.Count > 0)
            {
                bool all = options.Choices.Count == 1 &&
                           string.Equals(options.Choices[0], "all", StringComparison.OrdinalIgnoreCase);

                foreach (CatalogEntry entry in Catalog.OfClass(PolicyClass.Choice))
                    state.SetOverride(entry.Id, PolicyOverride.Auto);

                if (all)
                {
                    foreach (CatalogEntry entry in Catalog.OfClass(PolicyClass.Choice))
                        state.SetOverride(entry.Id, PolicyOverride.Eea);
                }
                else
                {
                    foreach (string id in options.Choices)
                    {
                        CatalogEntry entry = Catalog.ById(id);
                        if (entry == null) { Write("unknown choice Id, ignored: " + id); continue; }
                        if (entry.Class != PolicyClass.Choice)
                        {
                            Write("not a matter of taste, use --override instead: " + id);
                            continue;
                        }
                        state.SetOverride(entry.Id, PolicyOverride.Eea);
                    }
                }
            }

            foreach (KeyValuePair<string, PolicyOverride> pair in options.Overrides)
                state.SetOverride(pair.Key, pair.Value);
        }

        private static int Finish(EngineResult result, Options options)
        {
            foreach (string problem in result.Problems) Write("problem: " + problem);

            if (result.Report != null && !options.Quiet)
            {
                Write(string.Empty);
                foreach (Check check in result.Report.Checks)
                    Write(Pad(check.Result.ToString(), 8) + check.Title.Invariant + " -- " + check.Evidence);
            }

            if (result.Plan != null && !options.Quiet)
            {
                Write(string.Empty);
                Write("gains taken      " + result.Plan.Gains + " of " +
                      (result.Plan.BenefitsTotal + result.Plan.ChoicesTotal));
                Write("losses refused   " + result.Plan.LossesRefused + " of " + result.Plan.LossesTotal);
                Write("unclassified     " + result.Plan.Unclassified.Count);
            }

            Write(string.Empty);
            Write(result.Outcome.ToString());

            switch (result.Outcome)
            {
                case EngineOutcome.Success: return ExitCodes.Ok;
                case EngineOutcome.Partial: return ExitCodes.Partial;
                case EngineOutcome.NeedsElevation: return ExitCodes.NeedsAdmin;
                default: return ExitCodes.Failed;
            }
        }

        private static int ReportStatus(State state, Options options)
        {
            PolicyFileOrigin origin = Backups.ClassifyLive();
            PersistenceStatus task = Persistence.Status();
            int? region = Region.Read();
            PolicyPlan plan = Engine.Preview(state);

            if (options.Json)
            {
                var o = new JsonObject();
                o["version"] = new JsonString(Build.Version);
                o["applied"] = new JsonBool(state.EverApplied);
                o["appliedUtc"] = state.AppliedUtc == null
                    ? (JsonValue)JsonNull.Instance : new JsonString(state.AppliedUtc);
                o["regionWanted"] = new JsonNumber(state.RegionGeoId);
                o["regionActual"] = region.HasValue ? (JsonValue)new JsonNumber(region.Value) : JsonNull.Instance;
                o["policyFile"] = new JsonString(origin.ToString());
                o["scheduledTask"] = new JsonString(task.ToString());
                o["choicesSelected"] = JsonArray.Of(state.SelectedChoices());
                o["unclassified"] = JsonArray.Of(plan != null ? plan.Unclassified : state.Unclassified);
                if (plan != null)
                {
                    o["gainsPlanned"] = new JsonNumber(plan.Gains);
                    o["gainsPossible"] = new JsonNumber(plan.BenefitsTotal + plan.ChoicesTotal);
                    o["lossesRefused"] = new JsonNumber(plan.LossesRefused);
                }
                var removed = new JsonArray();
                foreach (RemovedApp app in state.RemovedApps) removed.Add(app.ToJson());
                o["removedApps"] = removed;

                Write(o.ToJson());
                return ExitCodes.Ok;
            }

            Write("One-Click EU Lord " + Build.Version);
            Write(string.Empty);
            Write(Pad("applied", 18) + (state.EverApplied ? state.AppliedUtc : "never"));
            Write(Pad("region wanted", 18) + state.RegionGeoId + " (" + state.RegionIso + ")");
            Write(Pad("region actual", 18) + (region.HasValue
                ? region.Value.ToString(CultureInfo.InvariantCulture) + " (" + (Native.Iso2Of(region.Value) ?? "?") + ")"
                : "absent"));
            Write(Pad("policy file", 18) + origin);
            Write(Pad("scheduled task", 18) + task);
            Write(Pad("choices", 18) + state.SelectedChoices().Count + " of " + Catalog.CountOfClass(PolicyClass.Choice));
            Write(Pad("overrides", 18) + state.Overrides.Count);
            if (plan != null)
            {
                Write(Pad("gains planned", 18) + plan.Gains + " of " + (plan.BenefitsTotal + plan.ChoicesTotal));
                Write(Pad("losses refused", 18) + plan.LossesRefused + " of " + plan.LossesTotal);
                Write(Pad("unclassified", 18) + plan.Unclassified.Count);
            }
            Write(Pad("data folder", 18) + Paths.DataRoot);
            return ExitCodes.Ok;
        }

        private static string Pad(string text, int width)
        {
            return text.Length >= width ? text + " " : text + new string(' ', width - text.Length);
        }

        private static void Write(string line)
        {
            try { Console.Out.WriteLine(line); }
            catch (Exception) { }
        }

        private static void WriteHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("One-Click EU Lord " + Build.Version + " -- " + Build.Repository);
            sb.AppendLine();
            sb.AppendLine("Double-click to open the window. Pass an argument to run without one.");
            sb.AppendLine();
            sb.AppendLine("Actions");
            sb.AppendLine("  --apply                 take every gain, refuse every loss, write the policy file,");
            sb.AppendLine("                          set the device region and install the scheduled task");
            sb.AppendLine("  --reapply               apply only if the machine has drifted (what the task runs)");
            sb.AppendLine("  --restore               put back the backed-up policy file, region and remove the task");
            sb.AppendLine("  --purge --yes           restore, then delete the data folder and its backups");
            sb.AppendLine("  --status [--json]       report what is in effect, changing nothing");
            sb.AppendLine();
            sb.AppendLine("Settings, saved for later runs");
            sb.AppendLine("  --region IE|68          the EEA country to present as, ISO code or GeoID");
            sb.AppendLine("  --choices all           turn on every matter of taste");
            sb.AppendLine("  --choice Id[,Id...]     turn on these matters of taste, and only these");
            sb.AppendLine("  --override Id=eea       force one policy to the EU state");
            sb.AppendLine("  --override Id=global    force one policy to the global state");
            sb.AppendLine("  --override Id=auto      let the superset rule decide again");
            sb.AppendLine("  --language auto|zh-Hant|zh-Hans|en");
            sb.AppendLine();
            sb.AppendLine("Other");
            sb.AppendLine("  --data-dir PATH         keep state, backups and logs somewhere else");
            sb.AppendLine("  --quiet                 only the outcome");
            sb.AppendLine("  --version, --help");
            sb.AppendLine();
            sb.AppendLine("Exit codes: 0 done, 1 failed, 2 bad usage, 3 needs administrator, 4 partly done.");
            sb.AppendLine();
            sb.AppendLine("Uninstalling apps is not available here on purpose: it runs each app's own");
            sb.AppendLine("uninstaller and asks for confirmation per app, in the window.");
            Write(sb.ToString());
        }
    }
}
