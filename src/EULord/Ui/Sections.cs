// The four sections behind the headings.
//
// Matters of taste  the 21 Choice policies, each off by default. Ticking one asks
//                   for the EU state; nothing happens until Apply runs again.
// Uninstall apps    Microsoft's own apps, removed through their own uninstallers,
//                   confirmed one at a time, inline, naming the app.
// Advanced          the country grid and all 68 policies with a three-way override.
// Restore           undo, and the explicit full wipe.
//
// Work that costs a child process -- the Appx survey -- happens the first time the
// section opens, not at startup.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EULord.Core;

namespace EULord.Ui
{
    internal sealed partial class MainWindow
    {
        private TextBlock _choicesDirty;
        private StackPanel _appsBody;
        private StackPanel _regionGrid;
        private StackPanel _policyTable;

        /// <summary>Opens every section. Used by the UI snapshot tool.</summary>
        internal void OpenEverySection()
        {
            foreach (Section section in new[] { _choicesSection, _appsSection, _advancedSection, _restoreSection })
                section.IsOpen = true;
        }

        /// <summary>
        /// The page itself, for the snapshot tool. It is rendered instead of the window
        /// because Windows clamps a window to the screen, which would clip a long page,
        /// while the scroll viewer arranges this panel at its full height regardless.
        /// </summary>
        internal FrameworkElement PageVisual { get { return _page; } }

        private void RefreshSectionSummaries()
        {
            int chosen = _state.SelectedChoices().Count;
            _choicesSection.Summary = chosen + " / " + Catalog.CountOfClass(PolicyClass.Choice);

            int removed = _state.RemovedApps.Count;
            _appsSection.Summary = removed == 0 ? string.Empty
                : string.Format(CultureInfo.CurrentCulture, S.NSelected, removed);

            int overridden = _state.Overrides.Count;
            _advancedSection.Summary = string.Format(CultureInfo.CurrentCulture,
                overridden == 0 ? S.NPolicies : S.NOverridden,
                overridden == 0 ? Catalog.Count : overridden);

            _restoreSection.Summary = Backups.HasAnyBackup() ? string.Empty : (string)S.NoBackupShort;
        }

        // -- matters of taste -------------------------------------------------

        private Section BuildChoicesSection()
        {
            var section = new Section(S.SecChoices, string.Empty);

            TextBlock lead = Text.Make(S.SecChoicesLead, Theme.SizeBody, Theme.Mute, FontWeights.Normal);
            lead.MaxWidth = 660;
            lead.Margin = new Thickness(0, 0, 0, 16);
            section.Body.Children.Add(lead);

            _choicesDirty = Text.Make(string.Empty, Theme.SizeMicro, Theme.Red, FontWeights.Bold);
            _choicesDirty.Margin = new Thickness(0, 0, 0, 12);
            _choicesDirty.Visibility = Visibility.Collapsed;
            section.Body.Children.Add(_choicesDirty);

            foreach (CatalogEntry entry in Catalog.OfClass(PolicyClass.Choice))
            {
                CatalogEntry captured = entry;
                bool ticked = _state.OverrideFor(entry.Id) == PolicyOverride.Eea;

                var row = new CheckRow(entry.Describe(S.Current), EeaDirectionNote(entry), ticked);
                row.CheckedChanged += (s, e) =>
                {
                    _state.SetOverride(captured.Id, row.IsChecked ? PolicyOverride.Eea : PolicyOverride.Auto);
                    TrySaveState();
                    _plan = Engine.Preview(_state);
                    MarkPendingApply();
                    RefreshStatus();
                };
                section.Body.Children.Add(row);
            }
            return section;
        }

        /// <summary>
        /// Says which way ticking the box moves this policy. Read from the live file
        /// rather than assumed, because the EU state is on for some of these and off
        /// for others, and "tick to turn off" would be wrong half the time.
        /// </summary>
        private string EeaDirectionNote(CatalogEntry entry)
        {
            if (_plan == null) return string.Empty;
            foreach (PolicyDecision d in _plan.Decisions)
            {
                if (d.Entry != entry) continue;
                bool on = string.Equals(d.EeaState, PolicyStates.Enabled, StringComparison.OrdinalIgnoreCase);
                return on ? S.TickTurnsOn : (string)S.TickTurnsOff;
            }
            return string.Empty;
        }

        private void MarkPendingApply()
        {
            if (!_state.EverApplied) return;
            _choicesDirty.Text = S.ReapplySuperset + " — " + S.ApplySuperset;
            _choicesDirty.Visibility = Visibility.Visible;
        }

        // -- app removal ------------------------------------------------------

        private Section BuildAppsSection()
        {
            var section = new Section(S.SecApps, string.Empty);

            TextBlock lead = Text.Make(S.SecAppsLead, Theme.SizeBody, Theme.Mute, FontWeights.Normal);
            lead.MaxWidth = 660;
            lead.Margin = new Thickness(0, 0, 0, 16);
            section.Body.Children.Add(lead);

            _appsBody = new StackPanel();
            section.Body.Children.Add(_appsBody);

            section.FirstOpened += (s, e) => LoadApps();
            return section;
        }

        /// <summary>
        /// Surveys the installed apps off the UI thread. The result is marshalled back
        /// with Dispatcher.BeginInvoke rather than by awaiting: this runs from a section
        /// opening, which can happen before a message loop is pumping, and in that case
        /// an awaited continuation resumes on a pool thread and faults on the first
        /// control it touches.
        /// </summary>
        private void LoadApps()
        {
            _appsBody.Children.Clear();
            _appsBody.Children.Add(Text.Muted(S.Working));

            JsonObject live = PolicyFile.TryReadLive();

            Task.Factory.StartNew(() => AppRemoval.Survey(live)).ContinueWith(task =>
                Dispatcher.BeginInvoke(new Action(() => ShowApps(task))));
        }

        private void ShowApps(Task<List<AppStatus>> survey)
        {
            _appsBody.Children.Clear();

            if (survey.IsFaulted)
            {
                Log.Error("The app survey failed", survey.Exception);
                _appsBody.Children.Add(Text.Make(S.Failed, Theme.SizeBody, Theme.Red, FontWeights.Bold));
                return;
            }

            foreach (AppStatus app in survey.Result) _appsBody.Children.Add(BuildAppRow(app));
        }

        private UIElement BuildAppRow(AppStatus app)
        {
            RemovedApp record = _state.FindRemoved(app.Candidate.Id);

            var name = Text.Body(app.Candidate.DisplayName);
            name.FontWeight = FontWeights.Bold;

            string stateText;
            Brush stateBrush = Theme.Mute;

            if (record != null) { stateText = S.AppRemoved + "  ·  " + record.Method; }
            else
            {
                switch (app.Availability)
                {
                    case AppAvailability.Removable: stateText = app.Detail; stateBrush = Theme.Ink; break;
                    case AppAvailability.NotInstalled: stateText = S.AppNotInstalled; break;
                    case AppAvailability.SystemProtected: stateText = S.AppSystemProtected; break;
                    case AppAvailability.NoUninstaller: stateText = S.AppNoUninstaller; break;
                    case AppAvailability.NeedsPolicy: stateText = S.AppNeedsPolicyFirst; stateBrush = Theme.Red; break;
                    default: stateText = string.Empty; break;
                }
            }

            TextBlock detail = Text.Make(stateText, Theme.SizeMicro, stateBrush, FontWeights.Normal);
            detail.Margin = new Thickness(0, 2, 16, 0);

            var left = new StackPanel();
            left.Children.Add(name);
            left.Children.Add(detail);

            if (app.Candidate.ReinstallNote != null &&
                (app.Availability == AppAvailability.Removable || record != null))
            {
                TextBlock note = Text.Make(app.Candidate.ReinstallNote, Theme.SizeMicro, Theme.Faint, FontWeights.Normal);
                note.Margin = new Thickness(0, 3, 16, 0);
                note.MaxWidth = 520;
                left.Children.Add(note);
            }

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(left, 0);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(left);
            grid.Children.Add(actions);

            if (record != null)
            {
                if (!string.IsNullOrEmpty(app.Candidate.ReinstallUri))
                {
                    var reinstall = new LinkButton(S.AppReinstall, Theme.SizeBody);
                    reinstall.Activated += (s, e) => AppRemoval.OpenReinstall(app.Candidate);
                    actions.Children.Add(reinstall);
                }
                return grid;
            }

            if (!app.CanUninstall) return grid;

            var uninstall = new LinkButton(S.AppUninstall, Theme.SizeBody);
            uninstall.Activated += (s, e) => ConfirmUninstall(app, left, actions, uninstall);
            actions.Children.Add(uninstall);
            return grid;
        }

        /// <summary>
        /// The per-app confirmation the user asked for: inline, naming the app, and never
        /// a single confirmation covering several.
        /// </summary>
        private void ConfirmUninstall(AppStatus app, StackPanel left, StackPanel actions, UIElement trigger)
        {
            actions.Children.Remove(trigger);

            TextBlock question = Text.Make(
                string.Format(CultureInfo.CurrentCulture, S.AppConfirm, app.Candidate.DisplayName),
                Theme.SizeMicro, Theme.Red, FontWeights.Bold);
            question.Margin = new Thickness(0, 6, 16, 0);
            question.MaxWidth = 520;
            left.Children.Add(question);

            if (app.Candidate.Route == AppRoute.Registered)
            {
                TextBlock window = Text.Make(S.AppUninstallerWindow, Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
                window.Margin = new Thickness(0, 3, 16, 0);
                window.MaxWidth = 520;
                left.Children.Add(window);
            }

            TextBlock command = Text.Make(app.Handle, Theme.SizeMicro, Theme.Faint, FontWeights.Normal);
            command.Margin = new Thickness(0, 3, 16, 0);
            command.MaxWidth = 520;
            left.Children.Add(command);

            var cancel = new LinkButton(S.Cancel, Theme.SizeBody) { Margin = new Thickness(0, 0, 16, 0) };
            var confirm = new LinkButton(S.Confirm, Theme.SizeBody);

            cancel.Activated += (s, e) => LoadApps();
            confirm.Activated += async (s, e) =>
            {
                actions.Children.Clear();
                actions.Children.Add(Text.Muted(S.Working));

                string message = null;
                bool ok = await Task.Run(() =>
                {
                    string inner;
                    bool done = AppRemoval.Uninstall(app, _state, out inner);
                    message = inner;
                    return done;
                });

                if (ok) TrySaveState();

                actions.Children.Clear();
                TextBlock outcome = Text.Make(ok ? S.AppRemoved : S.Failed, Theme.SizeBody,
                                              ok ? Theme.Ink : Theme.Red, FontWeights.Bold);
                actions.Children.Add(outcome);

                if (!ok && !string.IsNullOrEmpty(message))
                {
                    TextBlock reason = Text.Make(message, Theme.SizeMicro, Theme.Red, FontWeights.Normal);
                    reason.Margin = new Thickness(0, 6, 16, 0);
                    reason.MaxWidth = 520;
                    left.Children.Add(reason);
                }

                RefreshSectionSummaries();
            };

            actions.Children.Add(cancel);
            actions.Children.Add(confirm);
        }

        // -- advanced ---------------------------------------------------------

        private Section BuildAdvancedSection()
        {
            var section = new Section(S.SecAdvanced, string.Empty);

            TextBlock lead = Text.Make(
                string.Format(CultureInfo.CurrentCulture, S.SecAdvancedLead, Catalog.Count),
                Theme.SizeBody, Theme.Mute, FontWeights.Normal);
            lead.MaxWidth = 660;
            lead.Margin = new Thickness(0, 0, 0, 20);
            section.Body.Children.Add(lead);

            section.Body.Children.Add(Text.Micro(S.RegionLabel, Theme.Mute));
            TextBlock regionHint = Text.Make(S.RegionHint, Theme.SizeMicro, Theme.Faint, FontWeights.Normal);
            regionHint.MaxWidth = 660;
            regionHint.Margin = new Thickness(0, 6, 0, 10);
            section.Body.Children.Add(regionHint);

            _regionGrid = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
            section.Body.Children.Add(_regionGrid);

            _policyTable = new StackPanel();
            section.Body.Children.Add(_policyTable);

            section.FirstOpened += (s, e) =>
            {
                BuildRegionGrid();
                BuildPolicyTable();
            };
            return section;
        }

        /// <summary>
        /// A grid of country modules rather than a dropdown: the EEA list comes from the
        /// policy file itself, so it stays correct as Microsoft changes it.
        /// </summary>
        private void BuildRegionGrid()
        {
            _regionGrid.Children.Clear();

            IList<string> codes = _plan != null ? _plan.EeaCodes : new List<string> { Native.IrelandIso2 };
            var ordered = new List<string>(codes);
            ordered.Remove(Native.IrelandIso2);
            ordered.Insert(0, Native.IrelandIso2);          // Ireland first; it is the default for a reason

            var wrap = new WrapPanel();
            foreach (string iso in ordered)
            {
                int geoId = Native.GeoIdOf(iso);
                if (geoId == 0) continue;

                string captured = iso;
                bool selected = string.Equals(_state.RegionIso, iso, StringComparison.OrdinalIgnoreCase);
                wrap.Children.Add(BuildRegionChip(iso, geoId, selected, captured));
            }
            _regionGrid.Children.Add(wrap);
        }

        private UIElement BuildRegionChip(string iso, int geoId, bool selected, string captured)
        {
            var code = Text.Make(iso, Theme.SizeBody, selected ? Theme.Paper : Theme.Ink, FontWeights.Bold);
            code.TextWrapping = TextWrapping.NoWrap;

            var name = Text.Make(Native.FriendlyNameOf(geoId) ?? string.Empty, Theme.SizeMicro,
                                 selected ? Theme.Paper : Theme.Mute, FontWeights.Normal);
            name.TextWrapping = TextWrapping.NoWrap;
            name.MaxWidth = 104;
            name.TextTrimming = TextTrimming.CharacterEllipsis;

            var stack = new StackPanel();
            stack.Children.Add(code);
            stack.Children.Add(name);

            var chip = new RegionChip
            {
                Child = stack,
                Width = 118,
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 6, 6),
                Background = selected ? Theme.Ink : Theme.Paper,
                BorderBrush = selected ? Theme.Ink : Theme.Rule,
                BorderThickness = new Thickness(1)
            };

            chip.Activated += (s, e) =>
            {
                _state.RegionIso = captured;
                _state.RegionGeoId = geoId;
                TrySaveState();
                _plan = Engine.Preview(_state);
                MarkPendingApply();
                BuildRegionGrid();
                BuildPolicyTable();
                RefreshStatus();
            };
            return chip;
        }

        private sealed class RegionChip : Pressable
        {
            protected override void Repaint()
            {
                if (Hovered && !(Background == Theme.Ink)) BorderBrush = Theme.Red;
                else if (!(Background == Theme.Ink)) BorderBrush = Theme.Rule;
                BorderThickness = new Thickness(IsKeyboardFocused ? 2 : 1);
            }
        }

        private void BuildPolicyTable()
        {
            _policyTable.Children.Clear();
            if (_plan == null)
            {
                _policyTable.Children.Add(Text.Muted(S.PolicyMissing));
                return;
            }

            _policyTable.Children.Add(BuildPolicyHeader());
            _policyTable.Children.Add(Theme.HairRule(1, Theme.Ink, 6, 0));

            foreach (PolicyDecision decision in _plan.Decisions)
            {
                if (decision.Entry == null) continue;          // unclassified rows are listed separately
                _policyTable.Children.Add(BuildPolicyRow(decision));
            }

            if (_plan.Unclassified.Count == 0) return;

            _policyTable.Children.Add(Theme.HairRule(1, Theme.Rule, 14, 10));
            TextBlock unclassified = Text.Make(
                string.Format(CultureInfo.CurrentCulture, S.NUnclassified, _plan.Unclassified.Count),
                Theme.SizeMicro, Theme.Red, FontWeights.Bold);
            _policyTable.Children.Add(unclassified);

            foreach (string guid in _plan.Unclassified)
            {
                TextBlock line = Text.Make(guid, Theme.SizeMicro, Theme.Faint, FontWeights.Normal);
                line.Margin = new Thickness(0, 3, 0, 0);
                _policyTable.Children.Add(line);
            }
        }

        private static Grid PolicyGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
            return grid;
        }

        private UIElement BuildPolicyHeader()
        {
            Grid grid = PolicyGrid();
            string[] heads = { S.ColPolicy, S.ColClass, S.ColGlobal, S.ColEea, S.ColTarget };
            for (int i = 0; i < heads.Length; i++)
            {
                TextBlock head = Text.Micro(heads[i], Theme.Mute);
                Grid.SetColumn(head, i);
                grid.Children.Add(head);
            }
            return grid;
        }

        private UIElement BuildPolicyRow(PolicyDecision decision)
        {
            Grid grid = PolicyGrid();
            grid.Margin = new Thickness(0, 9, 0, 9);

            var title = new StackPanel();
            title.Children.Add(Text.Body(decision.Entry.Describe(S.Current)));
            TextBlock id = Text.Make(decision.Entry.Id, Theme.SizeMicro, Theme.Faint, FontWeights.Normal);
            id.Margin = new Thickness(0, 2, 12, 0);
            title.Children.Add(id);

            L className = decision.Entry.Class == PolicyClass.Benefit ? S.ClassBenefit
                        : decision.Entry.Class == PolicyClass.Loss ? S.ClassLoss : S.ClassChoice;
            TextBlock classText = Text.Make(className, Theme.SizeMicro,
                decision.Entry.Class == PolicyClass.Loss ? Theme.Red : Theme.Mute, FontWeights.Normal);

            TextBlock globalText = StateCell(decision.GlobalState, Theme.Mute);
            TextBlock eeaText = StateCell(decision.EeaState, Theme.Mute);

            int selected = decision.Override == PolicyOverride.Eea ? 1
                         : decision.Override == PolicyOverride.Global ? 2 : 0;
            var switcher = new Segmented(new[] { (string)S.OverrideAuto, (string)S.OverrideEea, (string)S.OverrideGlobal }, selected);

            string entryId = decision.Entry.Id;
            switcher.SelectionChanged += (s, e) =>
            {
                PolicyOverride value = switcher.Selected == 1 ? PolicyOverride.Eea
                                     : switcher.Selected == 2 ? PolicyOverride.Global : PolicyOverride.Auto;
                _state.SetOverride(entryId, value);
                TrySaveState();
                _plan = Engine.Preview(_state);
                MarkPendingApply();
                RefreshStatus();
                BuildPolicyTable();
            };

            Grid.SetColumn(title, 0);
            Grid.SetColumn(classText, 1);
            Grid.SetColumn(globalText, 2);
            Grid.SetColumn(eeaText, 3);
            Grid.SetColumn(switcher, 4);
            grid.Children.Add(title);
            grid.Children.Add(classText);
            grid.Children.Add(globalText);
            grid.Children.Add(eeaText);
            grid.Children.Add(switcher);

            var row = new StackPanel();
            row.Children.Add(grid);
            row.Children.Add(Theme.HairRule(1, Theme.Rule, 0, 0));
            return row;
        }

        private static TextBlock StateCell(string state, Brush brush)
        {
            bool on = string.Equals(state, PolicyStates.Enabled, StringComparison.OrdinalIgnoreCase);
            TextBlock cell = Text.Make(on ? S.StateEnabled : S.StateDisabled, Theme.SizeMicro, brush, FontWeights.Normal);
            cell.TextWrapping = TextWrapping.NoWrap;
            return cell;
        }

        // -- restore ----------------------------------------------------------

        private Section BuildRestoreSection()
        {
            var section = new Section(S.SecRestore, string.Empty);

            section.Body.Children.Add(Text.Make(S.RestoreTitle, Theme.SizeBody, Theme.Ink, FontWeights.Bold));
            TextBlock restoreLead = Text.Make(S.RestoreLead, Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
            restoreLead.MaxWidth = 660;
            restoreLead.Margin = new Thickness(0, 5, 0, 10);
            section.Body.Children.Add(restoreLead);

            var restorePanel = new StackPanel();
            var restoreButton = new LinkButton(S.RestoreButton, Theme.SizeBody);
            restoreButton.Activated += async (s, e) =>
            {
                if (_busy) return;
                SetBusy(true);
                restorePanel.Children.Clear();

                var progress = new Progress(message => Dispatcher.BeginInvoke(
                    new Action(() => { _progressLine.Text = message + " …"; })));
                EngineResult result = await Task.Run(() => Engine.Restore(_state, progress));

                _plan = Engine.Preview(_state);
                SetBusy(false);
                restorePanel.Children.Add(OutcomeLine(result, S.DoneRestored));
                RefreshStatus();
            };
            section.Body.Children.Add(restoreButton);
            section.Body.Children.Add(restorePanel);

            section.Body.Children.Add(Theme.HairRule(1, Theme.Rule, 22, 16));

            section.Body.Children.Add(Text.Make(S.PurgeTitle, Theme.SizeBody, Theme.Red, FontWeights.Bold));
            TextBlock purgeLead = Text.Make(
                string.Format(CultureInfo.CurrentCulture, S.PurgeLead, Paths.DataRoot),
                Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
            purgeLead.MaxWidth = 660;
            purgeLead.Margin = new Thickness(0, 5, 0, 10);
            section.Body.Children.Add(purgeLead);

            var purgePanel = new StackPanel();
            section.Body.Children.Add(purgePanel);
            BuildPurgeTrigger(purgePanel);

            return section;
        }

        private void BuildPurgeTrigger(StackPanel host)
        {
            host.Children.Clear();
            var button = new LinkButton(S.PurgeButton, Theme.SizeBody);
            button.Activated += (s, e) => BuildPurgeConfirm(host);
            host.Children.Add(button);
        }

        private void BuildPurgeConfirm(StackPanel host)
        {
            host.Children.Clear();

            TextBlock question = Text.Make(
                string.Format(CultureInfo.CurrentCulture, S.PurgeConfirm, Paths.DataRoot),
                Theme.SizeBody, Theme.Red, FontWeights.Bold);
            question.MaxWidth = 660;
            question.Margin = new Thickness(0, 0, 0, 10);
            host.Children.Add(question);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = new LinkButton(S.Cancel, Theme.SizeBody) { Margin = new Thickness(0, 0, 20, 0) };
            var confirm = new LinkButton(S.PurgeButton, Theme.SizeBody);

            cancel.Activated += (s, e) => BuildPurgeTrigger(host);
            confirm.Activated += async (s, e) =>
            {
                if (_busy) return;
                SetBusy(true);
                host.Children.Clear();

                var progress = new Progress(message => Dispatcher.BeginInvoke(
                    new Action(() => { _progressLine.Text = message + " …"; })));
                EngineResult result = await Task.Run(() => Engine.Purge(_state, progress));

                _plan = Engine.Preview(_state);
                SetBusy(false);
                host.Children.Add(OutcomeLine(result, S.DonePurged));
                RefreshStatus();
            };

            row.Children.Add(cancel);
            row.Children.Add(confirm);
            host.Children.Add(row);
        }

        private static UIElement OutcomeLine(EngineResult result, L successWord)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

            L headline = result.Outcome == EngineOutcome.Success ? successWord
                       : result.Outcome == EngineOutcome.NeedsElevation ? S.NeedAdmin
                       : result.Outcome == EngineOutcome.Partial ? S.StatusPartial : S.Failed;

            stack.Children.Add(Text.Make(headline, Theme.SizeBody,
                result.Outcome == EngineOutcome.Success ? Theme.Ink : Theme.Red, FontWeights.Bold));

            foreach (string problem in result.Problems)
            {
                TextBlock line = Text.Make("· " + problem, Theme.SizeMicro, Theme.Mute, FontWeights.Normal);
                line.Margin = new Thickness(0, 4, 0, 0);
                line.MaxWidth = 660;
                stack.Children.Add(line);
            }
            return stack;
        }
    }
}
