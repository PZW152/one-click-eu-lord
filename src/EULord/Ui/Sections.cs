// The four sections behind the headings, on the lower colour plane.
//
// Matters of taste  the 21 Choice policies, each off until ticked.
// Uninstall apps    Microsoft's own apps, through their own uninstallers,
//                   confirmed one at a time, inline, naming the app.
// Advanced          the country grid and all 68 policies, each overridable.
// Restore           undo, and the explicit full wipe.
//
// Work that costs a child process -- the Appx survey -- happens the first time
// the section opens, never at startup.

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
        private StackPanel _appsBody;
        private StackPanel _regionGrid;
        private StackPanel _policyTable;

        /// <summary>Where a section's text starts: clear of the tick box column.</summary>
        private static double ChoiceIndent { get { return CheckRow.TextIndent; } }

        internal FrameworkElement PageVisual { get { return _page; } }

        /// <summary>The title bar, so a snapshot shows the frame we draw ourselves.</summary>
        internal FrameworkElement TitleVisual { get { return _titleBar; } }

        internal void OpenEverySection()
        {
            foreach (Section s in new[] { _choicesSection, _appsSection, _advancedSection, _restoreSection })
                s.IsOpen = true;
        }

        private void RefreshSectionSummaries()
        {
            int chosen = _state.SelectedChoices().Count;
            _choicesSection.Summary = string.Format(CultureInfo.CurrentCulture, S.SecChoicesSummary, chosen,
                                                    Catalog.CountOfClass(PolicyClass.Choice));
            _choicesSection.Flag = ChoicesChanged() ? (string)S.PendingApply : null;

            int removed = _state.RemovedApps.Count;
            _appsSection.Summary = removed == 0 ? (string)S.SecAppsSummary
                : string.Format(CultureInfo.CurrentCulture, S.NSelected, removed);

            int overridden = _state.Overrides.Count;
            _advancedSection.Summary = string.Format(CultureInfo.CurrentCulture,
                overridden == 0 ? S.NPolicies : S.NOverridden,
                overridden == 0 ? Catalog.Count : overridden);

            _restoreSection.Summary = Backups.HasAnyBackup() ? (string)S.RestoreSummary : (string)S.NoBackupShort;
        }

        private void MarkPendingApply()
        {
            _choicesSection.Flag = ChoicesChanged() ? (string)S.PendingApply : null;
        }

        private StackPanel SectionLead(string text)
        {
            TextBlock lead = Text.Note(text, Theme.Plane1Muted);
            lead.Margin = new Thickness(ChoiceIndent, 0, Theme.GridMargin, Theme.Space3);
            lead.MaxWidth = 720;

            var host = new StackPanel();
            host.Children.Add(lead);
            return host;
        }

        // -- matters of taste --------------------------------------------------

        private Section BuildChoicesSection()
        {
            var section = new Section(S.SecChoices, string.Empty);
            section.Body.Children.Add(SectionLead(S.SecChoicesLead));

            foreach (CatalogEntry entry in Catalog.OfClass(PolicyClass.Choice))
            {
                CatalogEntry captured = entry;
                bool ticked = _state.OverrideFor(entry.Id) == PolicyOverride.Eea;

                var row = new CheckRow(entry.Describe(S.Current), EeaDirectionNote(entry), ticked);
                row.CheckedChanged += delegate
                {
                    _state.SetOverride(captured.Id, row.IsChecked ? PolicyOverride.Eea : PolicyOverride.Auto);
                    TrySaveState();
                    _plan = Engine.Preview(_state);
                    MarkPendingApply();
                    RefreshStatus();
                };
                section.Body.Children.Add(Layout.Rule(Theme.Plane1Hair));
                section.Body.Children.Add(row);
            }

            section.Body.Children.Add(new Border { Height = Theme.Space4, Background = Brushes.Transparent });
            return section;
        }

        /// <summary>
        /// Says which way ticking the box moves this policy, read from the live file
        /// rather than assumed: the EU state is on for some of these and off for
        /// others, so "tick to turn off" would be wrong half the time.
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

        // -- app removal -------------------------------------------------------

        private Section BuildAppsSection()
        {
            var section = new Section(S.SecApps, string.Empty);
            section.Body.Children.Add(SectionLead(S.SecAppsLead));

            _appsBody = new StackPanel();
            section.Body.Children.Add(_appsBody);
            section.Body.Children.Add(new Border { Height = Theme.Space4, Background = Brushes.Transparent });

            section.FirstOpened += delegate { LoadApps(); };
            return section;
        }

        /// <summary>
        /// Surveys the installed apps off the UI thread, marshalling the result back
        /// with Dispatcher.BeginInvoke rather than awaiting: this runs from a section
        /// opening, which can happen before a message loop is pumping, and an awaited
        /// continuation would then resume on a pool thread and fault on the first
        /// control it touched.
        /// </summary>
        private void LoadApps()
        {
            _appsBody.Children.Clear();
            _appsBody.Children.Add(Indented(Text.Note(S.Working, Theme.Plane1Muted)));

            JsonObject live = PolicyFile.TryReadLive();
            Task.Factory.StartNew(new Func<List<AppStatus>>(delegate { return AppRemoval.Survey(live); }))
                .ContinueWith(delegate(Task<List<AppStatus>> t)
                {
                    Dispatcher.BeginInvoke(new Action(delegate { ShowApps(t); }));
                });
        }

        private void ShowApps(Task<List<AppStatus>> survey)
        {
            _appsBody.Children.Clear();

            if (survey.IsFaulted)
            {
                Log.Error("The app survey failed", survey.Exception);
                _appsBody.Children.Add(Indented(Text.Note(S.Failed, Theme.RedInk)));
                return;
            }

            foreach (AppStatus app in survey.Result)
            {
                _appsBody.Children.Add(Layout.Rule(Theme.Plane1Hair));
                _appsBody.Children.Add(BuildAppRow(app));
            }
        }

        private FrameworkElement Indented(UIElement content)
        {
            var host = new Border { Padding = new Thickness(ChoiceIndent, Theme.Space2, Theme.GridMargin, Theme.Space2) };
            host.Child = content;
            return host;
        }

        private UIElement BuildAppRow(AppStatus app)
        {
            RemovedApp record = _state.FindRemoved(app.Candidate.Id);

            TextBlock name = Text.Body(app.Candidate.DisplayName, Theme.Plane1Ink);
            name.VerticalAlignment = VerticalAlignment.Center;

            string detail;
            if (record != null) detail = S.AppRemoved + "　" + record.Method;
            else
            {
                switch (app.Availability)
                {
                    case AppAvailability.Removable: detail = app.Detail; break;
                    case AppAvailability.NotInstalled: detail = S.AppNotInstalled; break;
                    case AppAvailability.SystemProtected: detail = S.AppSystemProtected; break;
                    case AppAvailability.NoUninstaller: detail = S.AppNoUninstaller; break;
                    case AppAvailability.NeedsPolicy: detail = S.AppNeedsPolicyFirst; break;
                    default: detail = string.Empty; break;
                }
            }

            TextBlock note = Text.Note(detail, app.Availability == AppAvailability.NeedsPolicy
                ? Theme.RedInk : Theme.Plane1Muted);
            note.TextWrapping = TextWrapping.NoWrap;
            note.VerticalAlignment = VerticalAlignment.Center;

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            var extra = new StackPanel();

            // Auto, star, Auto: the name and the buttons get exactly what they
            // need and the note absorbs the rest, so nothing can squeeze a sibling.
            var grid = new Grid { MinHeight = Theme.ChoiceRow };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            note.Margin = new Thickness(Theme.Gutter, 0, Theme.Gutter, 0);
            Grid.SetColumn(name, 0);
            Grid.SetColumn(note, 1);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(name);
            grid.Children.Add(note);
            grid.Children.Add(actions);

            var row = new StackPanel { Margin = new Thickness(ChoiceIndent, 0, Theme.GridMargin, 0) };
            row.Children.Add(grid);
            row.Children.Add(extra);

            if (record != null)
            {
                if (!string.IsNullOrEmpty(app.Candidate.ReinstallUri))
                {
                    var reinstall = new LinkButton(S.AppReinstall, Theme.Plane1Ink, Theme.Plane1Hair);
                    reinstall.Activated += delegate { AppRemoval.OpenReinstall(app.Candidate); };
                    actions.Children.Add(reinstall);
                }
                return row;
            }

            if (!app.CanUninstall) return row;

            var uninstall = new LinkButton(S.AppUninstall, Theme.Plane1Ink, Theme.Plane1Hair);
            uninstall.Activated += delegate { ConfirmUninstall(app, extra, actions, uninstall); };
            actions.Children.Add(uninstall);
            return row;
        }

        /// <summary>
        /// The per-app confirmation: inline, naming the app and the exact command,
        /// and never one confirmation covering several.
        /// </summary>
        private void ConfirmUninstall(AppStatus app, StackPanel extra, StackPanel actions, UIElement trigger)
        {
            actions.Children.Remove(trigger);
            extra.Children.Clear();

            TextBlock question = Text.Note(
                string.Format(CultureInfo.CurrentCulture, S.AppConfirm, app.Candidate.DisplayName), Theme.RedInk);
            question.Margin = new Thickness(0, 0, 0, Theme.Space1);
            extra.Children.Add(question);

            if (app.Candidate.Route == AppRoute.Registered)
                extra.Children.Add(Text.Micro(S.AppUninstallerWindow, Theme.Plane1Muted));

            TextBlock command = Text.Micro(app.Handle, Theme.Plane1Muted);
            command.Margin = new Thickness(0, Theme.Space1, 0, Theme.Space3);
            extra.Children.Add(command);

            var cancel = new LinkButton(S.Cancel, Theme.Plane1Ink, Theme.Plane1Hair)
            { Margin = new Thickness(0, 0, Theme.Space4, 0) };
            var confirm = new LinkButton(S.Confirm, Theme.RedInk, Theme.Plane1Hair);

            cancel.Activated += delegate { LoadApps(); };
            confirm.Activated += async delegate
            {
                actions.Children.Clear();
                actions.Children.Add(Text.Note(S.Working, Theme.Plane1Muted));

                string message = null;
                bool ok = await Task.Run(new Func<bool>(delegate
                {
                    string inner;
                    bool done = AppRemoval.Uninstall(app, _state, out inner);
                    message = inner;
                    return done;
                }));

                if (ok) TrySaveState();

                actions.Children.Clear();
                actions.Children.Add(Text.Note(ok ? S.AppRemoved : S.Failed, ok ? Theme.Plane1Ink : Theme.RedInk));

                extra.Children.Clear();
                if (!ok && !string.IsNullOrEmpty(message))
                    extra.Children.Add(Text.Micro(message, Theme.RedInk));

                RefreshSectionSummaries();
            };

            actions.Children.Add(cancel);
            actions.Children.Add(confirm);
        }

        // -- advanced -----------------------------------------------------------

        private Section BuildAdvancedSection()
        {
            var section = new Section(S.SecAdvanced, string.Empty);
            section.Body.Children.Add(SectionLead(
                string.Format(CultureInfo.CurrentCulture, S.SecAdvancedLead, Catalog.Count)));

            _regionGrid = new StackPanel { Margin = new Thickness(Theme.GridMargin, 0, Theme.GridMargin, Theme.Space6) };
            _policyTable = new StackPanel { Margin = new Thickness(Theme.GridMargin, 0, Theme.GridMargin, Theme.Space4) };
            section.Body.Children.Add(_regionGrid);
            section.Body.Children.Add(_policyTable);

            section.FirstOpened += delegate
            {
                BuildRegionGrid();
                BuildPolicyTable();
            };
            return section;
        }

        /// <summary>
        /// A grid of country modules rather than a dropdown: the EEA list comes from
        /// the policy file itself, so it stays correct as Microsoft changes it.
        /// </summary>
        private void BuildRegionGrid()
        {
            _regionGrid.Children.Clear();
            _regionGrid.Children.Add(Text.Micro(S.RegionLabel, Theme.Plane1Muted));

            TextBlock hint = Text.Micro(S.RegionHint, Theme.Plane1Muted);
            hint.Margin = new Thickness(0, Theme.Space1, 0, Theme.Space3);
            hint.MaxWidth = 660;
            _regionGrid.Children.Add(hint);

            IList<string> codes = _plan != null ? _plan.EeaCodes : new List<string> { Native.IrelandIso2 };
            var ordered = new List<string>(codes);
            ordered.Remove(Native.IrelandIso2);
            ordered.Insert(0, Native.IrelandIso2);     // Ireland first; it is the default for a reason

            var wrap = new WrapPanel();
            foreach (string iso in ordered)
            {
                int geoId = Native.GeoIdOf(iso);
                if (geoId == 0) continue;
                wrap.Children.Add(BuildRegionChip(iso, geoId));
            }
            _regionGrid.Children.Add(wrap);
        }

        private UIElement BuildRegionChip(string iso, int geoId)
        {
            bool selected = string.Equals(_state.RegionIso, iso, StringComparison.OrdinalIgnoreCase);

            TextBlock code = Text.MicroStrong(iso, selected ? Theme.Plane1 : Theme.Plane1Ink);
            TextBlock name = Text.Micro(Native.FriendlyNameOf(geoId) ?? string.Empty,
                                        selected ? Theme.Plane1 : Theme.Plane1Muted);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxWidth = 92;

            var stack = new StackPanel();
            stack.Children.Add(code);
            stack.Children.Add(name);

            var chip = new RegionChip(selected)
            {
                Child = stack,
                Width = 112,
                Padding = new Thickness(Theme.Space2 - 2, Theme.Space2 - 2, Theme.Space2 - 2, Theme.Space2 - 2),
                Margin = new Thickness(0, 0, 4, 4)
            };

            string captured = iso;
            chip.Activated += delegate
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
            private readonly bool _on;
            public RegionChip(bool on) { _on = on; Repaint(); }

            protected override void Repaint()
            {
                PaintFocus();
                if (_on) { Background = Theme.Plane1Ink; return; }
                Background = Hovered ? Theme.Hover1 : Brushes.Transparent;
                if (!IsKeyboardFocused) BorderBrush = Theme.Plane1Hair;
            }
        }

        private void BuildPolicyTable()
        {
            _policyTable.Children.Clear();
            if (_plan == null)
            {
                _policyTable.Children.Add(Text.Note(S.PolicyMissing, Theme.Plane1Muted));
                return;
            }

            _policyTable.Children.Add(BuildPolicyHeader());
            _policyTable.Children.Add(new Border { Height = 2, Background = Theme.Plane1Ink, SnapsToDevicePixels = true });

            foreach (PolicyDecision decision in _plan.Decisions)
            {
                if (decision.Entry == null) continue;    // unclassified rows are listed below
                _policyTable.Children.Add(BuildPolicyRow(decision));
                _policyTable.Children.Add(Layout.Rule(Theme.Plane1Hair));
            }

            if (_plan.Unclassified.Count == 0) return;

            TextBlock unclassified = Text.MicroStrong(
                string.Format(CultureInfo.CurrentCulture, S.NUnclassified, _plan.Unclassified.Count), Theme.RedInk);
            unclassified.Margin = new Thickness(0, Theme.Space3, 0, 0);
            _policyTable.Children.Add(unclassified);

            foreach (string guid in _plan.Unclassified)
            {
                TextBlock line = Text.Micro(guid, Theme.Plane1Muted);
                line.Margin = new Thickness(0, Theme.Space1, 0, 0);
                _policyTable.Children.Add(line);
            }
        }

        private static Grid PolicyGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            return grid;
        }

        private UIElement BuildPolicyHeader()
        {
            Grid grid = PolicyGrid();
            grid.Height = 26;
            string[] heads = { S.ColPolicy, S.ColClass, S.ColGlobal, S.ColEea, S.ColTarget };
            for (int i = 0; i < heads.Length; i++)
            {
                TextBlock head = Text.Micro(heads[i], Theme.Plane1Muted);
                head.VerticalAlignment = VerticalAlignment.Bottom;
                Grid.SetColumn(head, i);
                grid.Children.Add(head);
            }
            return grid;
        }

        private UIElement BuildPolicyRow(PolicyDecision decision)
        {
            Grid grid = PolicyGrid();
            grid.MinHeight = Theme.ChoiceRow;

            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Text.Body(decision.Entry.Describe(S.Current), Theme.Plane1Ink));
            TextBlock id = Text.Micro(decision.Entry.Id, Theme.Plane1Muted);
            id.Margin = new Thickness(0, 1, Theme.Space3, 0);
            title.Children.Add(id);

            L className = decision.Entry.Class == PolicyClass.Benefit ? S.ClassBenefit
                        : decision.Entry.Class == PolicyClass.Loss ? S.ClassLoss : S.ClassChoice;
            TextBlock classText = Text.Micro(className,
                decision.Entry.Class == PolicyClass.Loss ? Theme.RedInk : Theme.Plane1Muted);
            classText.VerticalAlignment = VerticalAlignment.Center;

            TextBlock globalText = StateCell(decision.GlobalState);
            TextBlock eeaText = StateCell(decision.EeaState);

            int selected = decision.Override == PolicyOverride.Eea ? 1
                         : decision.Override == PolicyOverride.Global ? 2 : 0;
            var switcher = new Segmented(
                new[] { (string)S.OverrideAuto, (string)S.OverrideEea, (string)S.OverrideGlobal }, selected);
            switcher.VerticalAlignment = VerticalAlignment.Center;

            string entryId = decision.Entry.Id;
            switcher.SelectionChanged += delegate
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
            return grid;
        }

        private static TextBlock StateCell(string state)
        {
            bool on = string.Equals(state, PolicyStates.Enabled, StringComparison.OrdinalIgnoreCase);
            TextBlock cell = Text.Micro(on ? S.StateEnabled : S.StateDisabled, Theme.Plane1Muted);
            cell.TextWrapping = TextWrapping.NoWrap;
            cell.VerticalAlignment = VerticalAlignment.Center;
            return cell;
        }

        // -- restore -------------------------------------------------------------

        private Section BuildRestoreSection()
        {
            var section = new Section(S.SecRestore, string.Empty);

            var body = new StackPanel { Margin = new Thickness(Theme.GridMargin, 0, Theme.GridMargin, Theme.Space4) };

            body.Children.Add(Text.Section(S.RestoreTitle, Theme.Plane1Ink));
            TextBlock restoreLead = Text.Note(S.RestoreLead, Theme.Plane1Muted);
            restoreLead.MaxWidth = 660;
            restoreLead.Margin = new Thickness(0, Theme.Space1, 0, Theme.Space2);
            body.Children.Add(restoreLead);

            var restorePanel = new StackPanel();
            var restoreButton = new LinkButton(S.RestoreButton, Theme.Plane1Ink, Theme.Plane1Hair);
            restoreButton.Activated += async delegate
            {
                if (_busy) return;
                SetBusy(true);
                restorePanel.Children.Clear();

                EngineResult result = await Task.Run(new Func<EngineResult>(
                    delegate { return Engine.Restore(_state, null); }));

                _plan = Engine.Preview(_state);
                _appliedChoices = 0;
                SetBusy(false);
                restorePanel.Children.Add(OutcomeLine(result, S.DoneRestored));
                RefreshStatus();
                PaintSegments(false, true);
            };
            body.Children.Add(restoreButton);
            body.Children.Add(restorePanel);

            body.Children.Add(new Border
            {
                Height = 1, Background = Theme.Plane1Hair,
                Margin = new Thickness(0, Theme.Space6, 0, Theme.Space4), SnapsToDevicePixels = true
            });

            body.Children.Add(Text.Section(S.PurgeTitle, Theme.RedInk));
            TextBlock purgeLead = Text.Note(
                string.Format(CultureInfo.CurrentCulture, S.PurgeLead, Paths.DataRoot), Theme.Plane1Muted);
            purgeLead.MaxWidth = 660;
            purgeLead.Margin = new Thickness(0, Theme.Space1, 0, Theme.Space2);
            body.Children.Add(purgeLead);

            var purgePanel = new StackPanel();
            body.Children.Add(purgePanel);
            BuildPurgeTrigger(purgePanel);

            section.Body.Children.Add(body);
            return section;
        }

        private void BuildPurgeTrigger(StackPanel host)
        {
            host.Children.Clear();
            var button = new LinkButton(S.PurgeButton, Theme.Plane1Ink, Theme.Plane1Hair);
            button.Activated += delegate { BuildPurgeConfirm(host); };
            host.Children.Add(button);
        }

        private void BuildPurgeConfirm(StackPanel host)
        {
            host.Children.Clear();

            TextBlock question = Text.Body(
                string.Format(CultureInfo.CurrentCulture, S.PurgeConfirm, Paths.DataRoot), Theme.RedInk);
            question.MaxWidth = 660;
            question.Margin = new Thickness(0, 0, 0, Theme.Space2);
            host.Children.Add(question);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = new LinkButton(S.Cancel, Theme.Plane1Ink, Theme.Plane1Hair)
            { Margin = new Thickness(0, 0, Theme.Space4, 0) };
            var confirm = new LinkButton(S.PurgeButton, Theme.RedInk, Theme.Plane1Hair);

            cancel.Activated += delegate { BuildPurgeTrigger(host); };
            confirm.Activated += async delegate
            {
                if (_busy) return;
                SetBusy(true);
                host.Children.Clear();

                EngineResult result = await Task.Run(new Func<EngineResult>(
                    delegate { return Engine.Purge(_state, null); }));

                _plan = Engine.Preview(_state);
                _appliedChoices = 0;
                SetBusy(false);
                host.Children.Add(OutcomeLine(result, S.DonePurged));
                RefreshStatus();
                PaintSegments(false, true);
            };

            row.Children.Add(cancel);
            row.Children.Add(confirm);
            host.Children.Add(row);
        }

        private static UIElement OutcomeLine(EngineResult result, L successWord)
        {
            var stack = new StackPanel { Margin = new Thickness(0, Theme.Space2, 0, 0) };

            L headline = result.Outcome == EngineOutcome.Success ? successWord
                       : result.Outcome == EngineOutcome.NeedsElevation ? S.NeedAdmin
                       : result.Outcome == EngineOutcome.Partial ? S.StatusPartial : S.Failed;

            stack.Children.Add(Text.Body(headline,
                result.Outcome == EngineOutcome.Success ? Theme.Plane1Ink : Theme.RedInk));

            foreach (string problem in result.Problems)
            {
                TextBlock line = Text.Micro("· " + problem, Theme.Plane1Muted);
                line.Margin = new Thickness(0, Theme.Space1, 0, 0);
                line.MaxWidth = 660;
                stack.Children.Add(line);
            }
            return stack;
        }
    }

    /// <summary>
    /// The three-way override. Index 0 is the resting choice and is drawn quietly:
    /// across 68 rows the default is nearly every row, and filling them all would be
    /// a wall of ink. Any other selection is a deliberate override and takes the
    /// accent, so the few changed rows are the ones that stand out.
    /// </summary>
    internal sealed class Segmented : StackPanel
    {
        private readonly List<Segment> _segments = new List<Segment>();
        private readonly List<TextBlock> _labels = new List<TextBlock>();
        private int _selected;

        public event EventHandler SelectionChanged;

        public Segmented(string[] captions, int selected)
        {
            Orientation = Orientation.Horizontal;
            _selected = selected;

            for (int i = 0; i < captions.Length; i++)
            {
                int index = i;
                TextBlock label = Text.Micro(captions[i], Theme.Plane1Muted);
                label.TextWrapping = TextWrapping.NoWrap;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;

                var segment = new Segment(this)
                {
                    Child = label,
                    Padding = new Thickness(Theme.Space2 - 1, Theme.Space1 - 2, Theme.Space2 - 1, Theme.Space1 - 2),
                    Margin = new Thickness(i == 0 ? 0 : -1, 0, 0, 0)
                };
                segment.Activated += delegate { Selected = index; };

                _segments.Add(segment);
                _labels.Add(label);
                Children.Add(segment);
            }
            Paint();
        }

        public int Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Paint();
                EventHandler h = SelectionChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        internal void Paint()
        {
            bool overridden = _selected != 0;

            for (int i = 0; i < _segments.Count; i++)
            {
                bool on = i == _selected;
                Segment segment = _segments[i];

                segment.Background = on && overridden ? Theme.Red : Brushes.Transparent;
                segment.BorderBrush = segment.IsKeyboardFocused ? Theme.Focus
                                    : (on && overridden ? Theme.Red : Theme.Plane1Hair);
                segment.BorderThickness = new Thickness(1);

                _labels[i].Foreground = on ? (overridden ? Theme.OnRed : Theme.Plane1Ink) : Theme.Plane1Muted;
                _labels[i].FontWeight = FontWeight.FromOpenTypeWeight(on ? 700 : 400);
            }
        }

        private sealed class Segment : Pressable
        {
            private readonly Segmented _owner;
            public Segment(Segmented owner) { _owner = owner; }
            protected override void Repaint() { if (_owner != null) _owner.Paint(); }
        }
    }
}
