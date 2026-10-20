// The window.
//
// One page. The things everyone needs -- what state this machine is in, and the
// button that changes it -- are always visible. Everything else is behind a
// heading that opens: matters of taste, app removal, the full policy table,
// restore. A first-time user can finish the job without opening any of them.
//
// The display element is the count of gains actually in effect, because that is
// the number the whole tool exists to move. It is content, not decoration: it
// changes when the machine changes.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EULord.Core;

namespace EULord.Ui
{
    internal sealed partial class MainWindow : Window
    {
        private readonly State _state;
        private PolicyPlan _plan;
        private VerifyReport _lastReport;
        private bool _busy;

        private StackPanel _page;
        private TextBlock _numeral;
        private TextBlock _statusWord;
        private StackPanel _readout;
        private PrimaryButton _applyButton;
        private TextBlock _progressLine;
        private StackPanel _resultPanel;

        private Section _choicesSection;
        private Section _appsSection;
        private Section _advancedSection;
        private Section _restoreSection;

        public MainWindow(State state)
        {
            _state = state;

            Title = S.AppName + " " + Build.Version;
            Width = Theme.WindowWidth;
            Height = Theme.WindowHeight;
            MinWidth = Theme.WindowMinWidth;
            MinHeight = Theme.WindowMinHeight;
            Background = Theme.Paper;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            BuildWindow();
        }

        private void BuildWindow()
        {
            _plan = Engine.Preview(_state);

            _page = new StackPanel { Margin = new Thickness(Theme.Margin, 32, Theme.Margin, 36) };
            _page.Children.Add(BuildMasthead());
            _page.Children.Add(Theme.HairRule(2, Theme.Ink, 22, 30));
            _page.Children.Add(BuildHero());
            _page.Children.Add(BuildAction());

            _choicesSection = BuildChoicesSection();
            _appsSection = BuildAppsSection();
            _advancedSection = BuildAdvancedSection();
            _restoreSection = BuildRestoreSection();

            var sections = new StackPanel { Margin = new Thickness(0, 30, 0, 0) };
            sections.Children.Add(_choicesSection);
            sections.Children.Add(_appsSection);
            sections.Children.Add(_advancedSection);
            sections.Children.Add(_restoreSection);
            sections.Children.Add(Theme.HairRule(1, Theme.Rule, 0, 0));
            _page.Children.Add(sections);

            _page.Children.Add(BuildFooter());

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _page,
                Focusable = false
            };

            RefreshStatus();
        }

        private void Rebuild()
        {
            BuildWindow();
        }

        // -- masthead ---------------------------------------------------------

        private UIElement BuildMasthead()
        {
            var name = Text.Make(S.AppName, Theme.SizeHead, Theme.Ink, FontWeights.Bold);
            name.FontFamily = Theme.Display;
            name.TextWrapping = TextWrapping.NoWrap;

            // The second line carries the name in the other script. A bilingual
            // masthead is honest here: the tool is named in Chinese and in English.
            TextBlock alternate = Text.Micro(S.AppNameRoman, Theme.Mute);
            alternate.Margin = new Thickness(0, 3, 0, 0);

            TextBlock tagline = Text.Make(S.Tagline, Theme.SizeBody, Theme.Ink, FontWeights.Normal);
            tagline.Margin = new Thickness(0, 10, 0, 0);
            tagline.MaxWidth = 460;

            var left = new StackPanel();
            left.Children.Add(name);
            left.Children.Add(alternate);
            left.Children.Add(tagline);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(left, 0);

            UIElement languages = BuildLanguageSwitch();
            Grid.SetColumn(languages, 1);

            grid.Children.Add(left);
            grid.Children.Add(languages);
            return grid;
        }

        private UIElement BuildLanguageSwitch()
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var options = new[]
            {
                new { Tag = "zh-Hant", Lang = Lang.ZhHant, Caption = "繁中" },
                new { Tag = "zh-Hans", Lang = Lang.ZhHans, Caption = "简中" },
                new { Tag = "en", Lang = Lang.En, Caption = "EN" }
            };

            foreach (var option in options)
            {
                bool current = S.Current == option.Lang;

                // The selected language is marked, not disabled: a greyed-out item reads
                // as unavailable, which is the opposite of what it means here.
                if (current)
                {
                    var marked = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                    TextBlock label = Text.Make(option.Caption, Theme.SizeBody, Theme.Ink, FontWeights.Bold);
                    label.TextWrapping = TextWrapping.NoWrap;
                    marked.Children.Add(label);
                    marked.Children.Add(Theme.HairRule(2, Theme.Red, 2, 0));
                    row.Children.Add(marked);
                    continue;
                }

                var button = new LinkButton(option.Caption, Theme.SizeBody)
                {
                    Margin = new Thickness(14, 0, 0, 0)
                };

                string tag = option.Tag;
                Lang lang = option.Lang;
                button.Activated += (s, e) =>
                {
                    if (_busy) return;
                    S.Current = lang;
                    _state.LanguagePreference = tag;
                    TrySaveState();
                    Title = S.AppName + " " + Build.Version;
                    Rebuild();
                };
                row.Children.Add(button);
            }
            return row;
        }

        // -- hero -------------------------------------------------------------

        private UIElement BuildHero()
        {
            _numeral = Text.Numeral("0", Theme.Ink);

            TextBlock caption = Text.Micro(S.BenefitsActive, Theme.Mute);
            caption.Margin = new Thickness(0, 4, 0, 0);

            _statusWord = Text.Make(string.Empty, Theme.SizeLead, Theme.Mute, FontWeights.Bold);
            _statusWord.Margin = new Thickness(0, 12, 12, 0);

            var left = new StackPanel { Width = Theme.HeroColumn };
            left.Children.Add(_numeral);
            left.Children.Add(caption);
            left.Children.Add(_statusWord);

            _readout = new StackPanel();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.HeroColumn) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(left, 0);
            Grid.SetColumn(_readout, 1);
            grid.Children.Add(left);
            grid.Children.Add(_readout);
            grid.Margin = new Thickness(0, 0, 0, 26);
            return grid;
        }

        private UIElement BuildAction()
        {
            _applyButton = new PrimaryButton(S.ApplySuperset, 268);
            _applyButton.Activated += (s, e) => Apply();

            TextBlock hint = Text.Make(string.Empty, Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
            hint.Name = "hint";
            hint.MaxWidth = 560;
            hint.Margin = new Thickness(0, 0, 0, 14);

            _progressLine = Text.Make(string.Empty, Theme.SizeBody, Theme.Red, FontWeights.Bold);
            _progressLine.Margin = new Thickness(0, 14, 0, 0);
            _progressLine.Visibility = Visibility.Collapsed;

            _resultPanel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };

            var stack = new StackPanel();
            stack.Children.Add(hint);
            stack.Children.Add(_applyButton);
            stack.Children.Add(_progressLine);
            stack.Children.Add(_resultPanel);
            _hintBlock = hint;
            return stack;
        }

        private TextBlock _hintBlock;

        // -- status -----------------------------------------------------------

        private void RefreshStatus()
        {
            PolicyFileOrigin origin = Backups.ClassifyLive();
            PersistenceStatus task = Persistence.Status();
            int? region = Region.Read();

            int gains = 0, possible = Catalog.Count;
            if (_plan != null)
            {
                possible = _plan.BenefitsTotal + _plan.ChoicesTotal;
                gains = origin == PolicyFileOrigin.Ours ? CountGainsInEffect() : 0;
            }

            _numeral.Text = gains.ToString(CultureInfo.InvariantCulture) + " / " +
                            possible.ToString(CultureInfo.InvariantCulture);
            _numeral.Foreground = gains == 0 ? Theme.Ink : Theme.Red;

            bool everApplied = _state.EverApplied;
            bool complete = everApplied && origin == PolicyFileOrigin.Ours &&
                            region.HasValue && region.Value == _state.RegionGeoId &&
                            task == PersistenceStatus.Ready;

            L word = !everApplied ? S.StatusNotApplied
                   : origin == PolicyFileOrigin.Unreadable ? S.StatusUnknown
                   : complete ? S.StatusApplied : S.StatusPartial;

            _statusWord.Text = word;
            _statusWord.Foreground = complete ? Theme.Ink : Theme.Mute;

            _readout.Children.Clear();

            string regionText = region.HasValue
                ? region.Value.ToString(CultureInfo.InvariantCulture) + "  " + (Native.Iso2Of(region.Value) ?? "?") +
                  "  " + (Native.FriendlyNameOf(region.Value) ?? string.Empty)
                : "—";
            _readout.Children.Add(Layout.ReadoutRow(S.RowRegion, regionText,
                region.HasValue && region.Value == _state.RegionGeoId ? Theme.Ink : Theme.Mute));

            L policyLabel = origin == PolicyFileOrigin.Ours ? S.PolicyOurs
                          : origin == PolicyFileOrigin.WindowsOriginal ? S.PolicyPristine : S.PolicyMissing;
            _readout.Children.Add(Layout.ReadoutRow(S.RowPolicyFile, policyLabel,
                origin == PolicyFileOrigin.Ours ? Theme.Ink : Theme.Mute));

            L taskLabel = task == PersistenceStatus.Ready ? S.TaskReady
                        : task == PersistenceStatus.Disabled ? S.TaskDisabled
                        : task == PersistenceStatus.Stale ? S.TaskStale : S.TaskAbsent;
            _readout.Children.Add(Layout.ReadoutRow(S.RowPersistence, taskLabel,
                task == PersistenceStatus.Ready ? Theme.Ink : Theme.Mute));

            int chosen = _state.SelectedChoices().Count;
            _readout.Children.Add(Layout.ReadoutRow(S.RowChoices,
                chosen == 0 ? (string)S.NoneSelected : string.Format(CultureInfo.CurrentCulture, S.NSelected, chosen),
                chosen == 0 ? Theme.Mute : Theme.Ink));

            int unclassified = _plan != null ? _plan.Unclassified.Count : _state.Unclassified.Count;
            _readout.Children.Add(Layout.ReadoutRow(S.RowUnclassified,
                unclassified == 0 ? (string)S.AllClassified
                                  : string.Format(CultureInfo.CurrentCulture, S.NUnclassified, unclassified),
                unclassified == 0 ? Theme.Mute : Theme.Red));

            _hintBlock.Text = string.Format(CultureInfo.CurrentCulture, S.ApplyHint,
                _plan != null ? _plan.BenefitsTotal : Catalog.CountOfClass(PolicyClass.Benefit),
                _plan != null ? _plan.LossesTotal : Catalog.CountOfClass(PolicyClass.Loss),
                _plan != null ? _plan.ChoicesTotal : Catalog.CountOfClass(PolicyClass.Choice));

            _applyButton.Caption = everApplied ? S.ReapplySuperset : S.ApplySuperset;

            RefreshSectionSummaries();
        }

        /// <summary>
        /// How many gains are actually in the live file right now, as opposed to how many
        /// the plan intends. Counted by reading the file back, so a half-applied machine
        /// reports honestly.
        /// </summary>
        private int CountGainsInEffect()
        {
            JsonObject live = PolicyFile.TryReadLive();
            if (live == null || _plan == null) return 0;

            var onDisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JsonArray policies = PolicyFile.Policies(live);
            if (policies == null) return 0;

            foreach (JsonValue v in policies.Items)
            {
                var p = v as JsonObject;
                if (p == null) continue;
                string guid = p.GetString("guid");
                if (guid != null) onDisk[guid] = p.GetString("defaultState") ?? string.Empty;
            }

            int count = 0;
            foreach (PolicyDecision d in _plan.Decisions)
            {
                if (d.Entry == null || !d.Differs) continue;
                if (d.Entry.Class == PolicyClass.Loss) continue;

                string actual;
                if (!onDisk.TryGetValue(d.Guid ?? string.Empty, out actual)) continue;
                if (string.Equals(actual, d.EeaState, StringComparison.OrdinalIgnoreCase)) count++;
            }
            return count;
        }

        // -- apply ------------------------------------------------------------

        private async void Apply()
        {
            if (_busy) return;
            SetBusy(true);
            _resultPanel.Children.Clear();

            var progress = new Progress(message => Dispatcher.BeginInvoke(
                new Action(() => { _progressLine.Text = message + " …"; })));

            EngineResult result = await Task.Run(() => Engine.Apply(_state, progress));

            _plan = Engine.Preview(_state);
            _lastReport = result.Report;

            SetBusy(false);
            ShowResult(result);
            RefreshStatus();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _applyButton.Active = !busy;
            _applyButton.Caption = busy ? S.Working : (_state.EverApplied ? S.ReapplySuperset : S.ApplySuperset);
            _progressLine.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Cursor = busy ? Cursors.Wait : Cursors.Arrow;
        }

        private void ShowResult(EngineResult result)
        {
            _resultPanel.Children.Clear();

            L headline = result.Outcome == EngineOutcome.Success ? S.DoneApplied
                       : result.Outcome == EngineOutcome.NeedsElevation ? S.NeedAdmin
                       : result.Outcome == EngineOutcome.Partial ? S.StatusPartial : S.Failed;

            TextBlock head = Text.Make(headline, Theme.SizeLead,
                result.Outcome == EngineOutcome.Success ? Theme.Ink : Theme.Red, FontWeights.Bold);
            _resultPanel.Children.Add(head);

            if (result.Outcome == EngineOutcome.Success || result.Outcome == EngineOutcome.Partial)
            {
                TextBlock note = Text.Make(S.NeedsRestart, Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
                note.Margin = new Thickness(0, 4, 0, 0);
                _resultPanel.Children.Add(note);
            }

            foreach (string problem in result.Problems)
            {
                TextBlock line = Text.Make("· " + problem, Theme.SizeMicro, Theme.Red, FontWeights.Normal);
                line.Margin = new Thickness(0, 4, 0, 0);
                line.MaxWidth = 640;
                _resultPanel.Children.Add(line);
            }

            if (result.Report != null)
                _resultPanel.Children.Add(BuildVerifyTable(result.Report));
        }

        private UIElement BuildVerifyTable(VerifyReport report)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
            stack.Children.Add(Text.Micro(S.VerifyTitle, Theme.Mute));
            stack.Children.Add(Theme.HairRule(1, Theme.Rule, 8, 8));

            foreach (Check check in report.Checks)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 9) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

                var titleStack = new StackPanel();
                titleStack.Children.Add(Text.Body(check.Title));

                TextBlock evidence = Text.Make(check.Evidence, Theme.SizeMicro, Theme.Faint, FontWeights.Normal);
                evidence.Margin = new Thickness(0, 2, 12, 0);
                titleStack.Children.Add(evidence);

                if (check.Result != CheckResult.Pass && check.Advice != null)
                {
                    TextBlock advice = Text.Make(check.Advice, Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
                    advice.Margin = new Thickness(0, 3, 12, 0);
                    titleStack.Children.Add(advice);
                }

                TextBlock verdict = Text.Make(Layout.LabelFor(check.Result), Theme.SizeBody,
                                              Layout.BrushFor(check.Result), FontWeights.Bold);
                verdict.TextAlignment = TextAlignment.Right;
                verdict.TextWrapping = TextWrapping.NoWrap;
                verdict.VerticalAlignment = VerticalAlignment.Top;

                Grid.SetColumn(titleStack, 0);
                Grid.SetColumn(verdict, 1);
                grid.Children.Add(titleStack);
                grid.Children.Add(verdict);
                stack.Children.Add(grid);
            }
            return stack;
        }

        // -- footer -----------------------------------------------------------

        private UIElement BuildFooter()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 26, 0, 0) };

            TextBlock version = Text.Micro(Build.Version, Theme.Faint);
            version.VerticalAlignment = VerticalAlignment.Center;
            version.Margin = new Thickness(0, 0, 20, 0);
            row.Children.Add(version);

            var log = new LinkButton(S.OpenLog, Theme.SizeMicro) { Margin = new Thickness(0, 0, 20, 0) };
            log.Activated += (s, e) => OpenPath(Paths.LogFile);
            row.Children.Add(log);

            var folder = new LinkButton(S.OpenFolder, Theme.SizeMicro) { Margin = new Thickness(0, 0, 20, 0) };
            folder.Activated += (s, e) => OpenPath(Paths.DataRoot);
            row.Children.Add(folder);

            var update = new LinkButton(S.CheckUpdate, Theme.SizeMicro);
            var updateResult = Text.Make(string.Empty, Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
            updateResult.VerticalAlignment = VerticalAlignment.Center;
            updateResult.Margin = new Thickness(12, 0, 0, 0);
            update.Activated += async (s, e) =>
            {
                update.Active = false;
                updateResult.Text = S.Working;
                string latest = await Task.Run(() => Updates.LatestVersion());
                updateResult.Text = latest == null ? S.UpdateFailed
                    : Updates.IsNewer(latest) ? string.Format(CultureInfo.CurrentCulture, S.UpdateAvailable, latest)
                    : S.UpdateLatest;
                update.Active = true;
            };
            row.Children.Add(update);
            row.Children.Add(updateResult);

            var stack = new StackPanel();
            stack.Children.Add(row);

            TextBlock privacy = Text.Make(S.NoNetworkByDefault, Theme.SizeMicro, Theme.Faint, FontWeights.Normal);
            privacy.Margin = new Thickness(0, 8, 0, 0);
            privacy.MaxWidth = 620;
            stack.Children.Add(privacy);
            return stack;
        }

        private static void OpenPath(string path)
        {
            try
            {
                if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path))
                    System.Diagnostics.Process.Start(path);
                else
                    System.Diagnostics.Process.Start(Paths.DataRoot);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not open " + path + " -- " + Log.Describe(ex));
            }
        }

        private void TrySaveState()
        {
            try { StateStore.Save(_state); }
            catch (Exception ex) { Log.Warn("state.json could not be saved -- " + Log.Describe(ex)); }
        }
    }
}
