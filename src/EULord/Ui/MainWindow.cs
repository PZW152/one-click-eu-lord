// The window.
//
// Swiss direction two, "colour planes": depth comes from two flat planes
// overlapping on the 12-column grid, not from shadow or blur. The lower plane
// carries what the machine looks like now; the upper plane, sitting across it and
// bleeding off the right edge, carries the number and the one action. When the
// superset takes effect the upper plane slides one column left -- the single piece
// of motion in the product that moves a plane, and it means something: the thing
// you pressed has engaged.
//
// Below them, 68 segments: 40 gains, 21 matters of taste, 7 losses hatched out
// because they are kept rather than taken. Then everything else, behind headings.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EULord.Core;

namespace EULord.Ui
{
    internal sealed partial class MainWindow : Window
    {
        private readonly State _state;
        private PolicyPlan _plan;
        private bool _busy;
        private int _appliedChoices;

        private TitleBar _titleBar;
        private StackPanel _page;
        private Border _plane2;
        private TranslateTransform _plane2Shift;
        private TextBlock _count, _countUnit, _headline, _sub;
        private StackPanel _readout;
        private ActionButton _apply;
        private StackPanel _resultPanel;
        private readonly List<Border> _segments = new List<Border>();
        private StackPanel _legend;

        private Section _choicesSection, _appsSection, _advancedSection, _restoreSection;
        private DispatcherTimer _themeWatch;

        /// <summary>
        /// Set by the snapshot tool so it can render a theme the machine is not in.
        /// The product never sets it: the window follows Windows.
        /// </summary>
        internal static bool PinTheme = false;

        public MainWindow(State state)
        {
            _state = state;

            Title = S.AppName + " " + Build.Version;
            Width = Theme.WindowMinWidth;
            Height = Theme.WindowMinHeight + 160;
            MinWidth = Theme.WindowMinWidth;
            MinHeight = Theme.WindowMinHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            Chrome.Apply(this);
            if (!PinTheme) Theme.Mode = SystemTheme.IsDark() ? ThemeMode.Dark : ThemeMode.Light;

            BuildWindow();
            if (!PinTheme) WatchSystemTheme();
        }

        // -- shell ------------------------------------------------------------

        private void BuildWindow()
        {
            _plan = Engine.Preview(_state);
            _segments.Clear();

            Background = Theme.Ground;

            _titleBar = new TitleBar(this);
            _titleBar.SetLanguages(S.Current);
            _titleBar.LanguagePicked += (s, tag) =>
            {
                if (_busy) return;
                S.Current = Program.ResolveLanguage(tag);
                _state.LanguagePreference = tag;
                TrySaveState();
                Title = S.AppName + " " + Build.Version;
                BuildWindow();
            };

            _page = new StackPanel();
            _page.Children.Add(BuildPlanes());
            _page.Children.Add(BuildDataBar());
            _page.Children.Add(BuildSections());
            _page.Children.Add(BuildFooter());

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _page,
                Focusable = false,
                Background = Brushes.Transparent
            };

            var root = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_titleBar, Dock.Top);
            root.Children.Add(_titleBar);
            root.Children.Add(scroll);

            Content = root;
            RefreshStatus();
        }

        /// <summary>
        /// The grid every row aligns to: a fixed margin, twelve equal columns, a
        /// fixed margin. The extra outer columns let a plane bleed past the margin.
        /// </summary>
        private static Grid GridWithMargins()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.GridMargin) });
            for (int i = 0; i < 12; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.GridMargin) });
            return grid;
        }

        // -- the two planes ---------------------------------------------------

        private UIElement BuildPlanes()
        {
            Grid area = GridWithMargins();
            area.Height = 452;

            // Lower plane: columns 1 to 7, starting at the left margin.
            Border plane1 = BuildStatusPlane();
            Grid.SetColumn(plane1, 1);
            Grid.SetColumnSpan(plane1, 7);
            plane1.Margin = new Thickness(0, Theme.Space4, 0, Theme.Space6 - 4);

            // Upper plane: from column 7, so it crosses the lower one, and on into
            // the right margin so it bleeds off the edge.
            _plane2 = BuildActionPlane();
            Grid.SetColumn(_plane2, 7);
            Grid.SetColumnSpan(_plane2, 7);
            _plane2.Margin = new Thickness(0, 76, 0, 28);

            _plane2Shift = new TranslateTransform(0, 0);
            _plane2.RenderTransform = _plane2Shift;

            area.Children.Add(plane1);
            area.Children.Add(_plane2);
            return area;
        }

        private Border BuildStatusPlane()
        {
            _headline = Text.Headline(string.Empty, Theme.Plane1Ink);

            _sub = Text.Note(string.Empty, Theme.Plane1Muted);
            _sub.Margin = new Thickness(0, Theme.Space3, 0, Theme.Space4 + 6);

            _readout = new StackPanel();

            var column = new StackPanel();
            column.Children.Add(_headline);
            column.Children.Add(_sub);
            column.Children.Add(_readout);

            // The upper plane crosses only this plane's last column, so the content
            // runs to nine of the twelve inner columns: wide enough that a sentence
            // does not wrap three times, and still clear of the overlap.
            Grid inner = Layout.Columns();
            Grid.SetColumn(column, 0);
            Grid.SetColumnSpan(column, 9);
            inner.Children.Add(column);

            return new Border
            {
                Background = Theme.Plane1,
                Padding = new Thickness(Theme.Space4 + 12, Theme.Space6, Theme.Space4, Theme.Space4),
                Child = inner,
                SnapsToDevicePixels = true
            };
        }

        private Border BuildActionPlane()
        {
            _count = Text.Count("0", Theme.Plane2Ink);

            _countUnit = Text.Note(string.Empty, Theme.Plane2Muted);
            _countUnit.Margin = new Thickness(0, Theme.Space4 - 2, 0, 0);

            _apply = new ActionButton(S.ApplySuperset);
            _apply.Activated += delegate { Apply(); };

            _resultPanel = new StackPanel { Margin = new Thickness(0, Theme.Space2, 0, 0) };

            var column = new StackPanel();
            column.Children.Add(_count);
            column.Children.Add(_countUnit);
            column.Children.Add(new Border { Height = Theme.Space6, Background = Brushes.Transparent });
            column.Children.Add(_apply);
            column.Children.Add(_resultPanel);

            return new Border
            {
                Background = Theme.Plane2,
                Padding = new Thickness(Theme.Space4 + 12, Theme.Space4 + 6, Theme.Space6 + Theme.PlaneOverlap, Theme.Space4),
                Child = column,
                SnapsToDevicePixels = true
            };
        }

        // -- the 68 segments ---------------------------------------------------

        private UIElement BuildDataBar()
        {
            Grid row = GridWithMargins();
            row.Margin = new Thickness(0, 0, 0, Theme.Space6);

            var bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(21, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });

            UIElement gains = BuildSegmentGroup(40, false);
            UIElement taste = BuildSegmentGroup(21, false);
            UIElement losses = BuildSegmentGroup(7, true);
            Grid.SetColumn(gains, 0);
            Grid.SetColumn(taste, 1);
            Grid.SetColumn(losses, 2);
            ((FrameworkElement)gains).Margin = new Thickness(0, 0, Theme.Gutter, 0);
            ((FrameworkElement)taste).Margin = new Thickness(0, 0, Theme.Gutter, 0);
            bar.Children.Add(gains);
            bar.Children.Add(taste);
            bar.Children.Add(losses);

            _legend = new StackPanel { Margin = new Thickness(0, Theme.Space2 + 2, 0, 0) };
            var legendRow = new Grid();
            legendRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40, GridUnitType.Star) });
            legendRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(21, GridUnitType.Star) });
            legendRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
            _legend.Children.Add(legendRow);

            var stack = new StackPanel();
            stack.Children.Add(bar);
            stack.Children.Add(_legend);

            Grid.SetColumn(stack, 1);
            Grid.SetColumnSpan(stack, 12);
            row.Children.Add(stack);
            return row;
        }

        private UIElement BuildSegmentGroup(int count, bool held)
        {
            var grid = new Grid { Height = Theme.Segment };
            for (int i = 0; i < count; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < count; i++)
            {
                var cell = new Border
                {
                    Background = held ? MakeHatch() : Theme.SegOff,
                    Margin = new Thickness(0, 0, i == count - 1 ? 0 : 2, 0),
                    SnapsToDevicePixels = true
                };
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
                if (!held) _segments.Add(cell);
            }
            return grid;
        }

        /// <summary>
        /// The hatching over a loss. Diagonal, so it reads as "struck out, kept" and
        /// never as a dimmer version of a lit segment.
        /// </summary>
        private static Brush MakeHatch()
        {
            // A DrawingBrush, not a VisualBrush: a brush wrapping a live Visual cannot
            // be frozen, and an unfrozen brush cannot be rendered off the UI thread,
            // which is how the snapshot tool reads the window.
            var pen = new Pen(Theme.Held, 1);
            pen.Freeze();

            var drawing = new GeometryDrawing(null, pen, Geometry.Parse("M -1,3 L 3,-1 M 0,4 L 4,0 M 1,5 L 5,1"));
            drawing.Freeze();

            var brush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 4, 4),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 4, 4),
                ViewboxUnits = BrushMappingMode.Absolute
            };
            brush.Freeze();
            return brush;
        }

        // -- sections and footer ----------------------------------------------

        private UIElement BuildSections()
        {
            _choicesSection = BuildChoicesSection();
            _appsSection = BuildAppsSection();
            _advancedSection = BuildAdvancedSection();
            _restoreSection = BuildRestoreSection();

            var stack = new StackPanel();
            stack.Children.Add(_choicesSection);
            stack.Children.Add(_appsSection);
            stack.Children.Add(_advancedSection);
            stack.Children.Add(_restoreSection);
            stack.Children.Add(Layout.Rule(Theme.Plane1Hair));

            return new Border { Background = Theme.Plane1, Child = stack, SnapsToDevicePixels = true };
        }

        private UIElement BuildFooter()
        {
            Grid row = GridWithMargins();
            row.Margin = new Thickness(0, Theme.Space4, 0, Theme.Space6);

            var line = new StackPanel { Orientation = Orientation.Horizontal };

            var log = new LinkButton(S.OpenLog, Theme.GroundInk, Theme.Plane1Hair) { Margin = new Thickness(0, 0, Theme.Space4, 0) };
            log.Activated += delegate { OpenPath(Paths.LogFile); };
            line.Children.Add(log);

            var folder = new LinkButton(S.OpenFolder, Theme.GroundInk, Theme.Plane1Hair) { Margin = new Thickness(0, 0, Theme.Space4, 0) };
            folder.Activated += delegate { OpenPath(Paths.DataRoot); };
            line.Children.Add(folder);

            var update = new LinkButton(S.CheckUpdate, Theme.GroundInk, Theme.Plane1Hair);
            TextBlock outcome = Text.Micro(string.Empty, Theme.GroundMuted);
            outcome.VerticalAlignment = VerticalAlignment.Center;
            outcome.Margin = new Thickness(Theme.Space3, 0, 0, 0);
            update.Activated += async delegate
            {
                update.Active = false;
                outcome.Text = S.Working;
                string latest = await Task.Run(new Func<string>(Updates.LatestVersion));
                outcome.Text = latest == null ? S.UpdateFailed
                    : Updates.IsNewer(latest) ? string.Format(CultureInfo.CurrentCulture, S.UpdateAvailable, latest)
                    : S.UpdateLatest;
                update.Active = true;
            };
            line.Children.Add(update);
            line.Children.Add(outcome);

            TextBlock privacy = Text.Micro(S.NoNetworkByDefault, Theme.GroundMuted);
            privacy.Margin = new Thickness(0, Theme.Space2, 0, 0);
            privacy.MaxWidth = 620;

            var stack = new StackPanel();
            stack.Children.Add(line);
            stack.Children.Add(privacy);

            Grid.SetColumn(stack, 1);
            Grid.SetColumnSpan(stack, 12);
            row.Children.Add(stack);
            return row;
        }

        // -- status ------------------------------------------------------------

        private void RefreshStatus()
        {
            PolicyFileOrigin origin = Backups.ClassifyLive();
            PersistenceStatus task = Persistence.Status();
            int? region = Region.Read();

            int possible = _plan != null ? _plan.BenefitsTotal + _plan.ChoicesTotal : Catalog.Count;
            int gains = origin == PolicyFileOrigin.Ours ? CountGainsInEffect() : 0;

            bool everApplied = _state.EverApplied;
            bool complete = everApplied && origin == PolicyFileOrigin.Ours &&
                            region.HasValue && region.Value == _state.RegionGeoId &&
                            task == PersistenceStatus.Ready;

            _count.Text = gains.ToString(CultureInfo.InvariantCulture);
            _countUnit.Text = "/ " + possible.ToString(CultureInfo.InvariantCulture) + "　" + S.BenefitsActive;

            _headline.Text = !everApplied ? S.StatusNotApplied
                           : origin == PolicyFileOrigin.Unreadable ? S.StatusUnknown
                           : complete ? S.StatusApplied : S.StatusPartial;

            _sub.Text = everApplied
                ? string.Format(CultureInfo.CurrentCulture, S.AppliedSummary, gains, _plan != null ? _plan.LossesTotal : 7)
                : (string)S.NotAppliedSummary;

            BuildReadout(origin, task, region);
            PaintSegments(origin == PolicyFileOrigin.Ours, false);
            PaintLegend();

            _apply.Caption = everApplied ? S.ReapplySuperset : S.ApplySuperset;
            _apply.Quiet = complete && !ChoicesChanged();
            _plane2Shift.X = origin == PolicyFileOrigin.Ours ? -Theme.PlaneOverlap : 0;

            RefreshSectionSummaries();
        }

        private void BuildReadout(PolicyFileOrigin origin, PersistenceStatus task, int? region)
        {
            _readout.Children.Clear();

            string regionText = region.HasValue
                ? region.Value.ToString(CultureInfo.InvariantCulture) + "　" + (Native.Iso2Of(region.Value) ?? "?")
                : "—";
            AddRow(S.RowRegion, regionText, region.HasValue && region.Value == _state.RegionGeoId);

            AddRow(S.RowPolicyFile,
                origin == PolicyFileOrigin.Ours ? (string)S.PolicyOurs
                : origin == PolicyFileOrigin.WindowsOriginal ? (string)S.PolicyPristine : (string)S.PolicyMissing,
                origin == PolicyFileOrigin.Ours);

            AddRow(S.RowPersistence,
                task == PersistenceStatus.Ready ? (string)S.TaskReady
                : task == PersistenceStatus.Disabled ? (string)S.TaskDisabled
                : task == PersistenceStatus.Stale ? (string)S.TaskStale : (string)S.TaskAbsent,
                task == PersistenceStatus.Ready);

            int chosen = _state.SelectedChoices().Count;
            AddRow(S.RowChoices,
                chosen == 0 ? (string)S.NoneSelected
                            : string.Format(CultureInfo.CurrentCulture, S.NSelected, chosen), false);

            int unclassified = _plan != null ? _plan.Unclassified.Count : _state.Unclassified.Count;
            AddRow(S.RowUnclassified,
                unclassified == 0 ? (string)S.AllClassified
                                  : string.Format(CultureInfo.CurrentCulture, S.NUnclassified, unclassified), false);
        }

        private void AddRow(string label, string value, bool marked)
        {
            var grid = new Grid { Height = Theme.Row };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });

            TextBlock labelBlock = Text.Note(label, Theme.Plane1Muted);
            labelBlock.VerticalAlignment = VerticalAlignment.Center;

            TextBlock valueBlock = Text.Body(value, Theme.Plane1Ink);
            valueBlock.TextWrapping = TextWrapping.NoWrap;
            valueBlock.VerticalAlignment = VerticalAlignment.Center;

            var mark = new Border
            {
                Width = 10, Height = 10,
                Background = marked ? Theme.Red : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(valueBlock, 1);
            Grid.SetColumn(mark, 2);
            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
            grid.Children.Add(mark);

            if (_readout.Children.Count > 0) _readout.Children.Add(Layout.Rule(Theme.Plane1Hair));
            else _readout.Children.Add(new Border { Height = 2, Background = Theme.Plane1Ink, SnapsToDevicePixels = true });

            _readout.Children.Add(grid);
        }

        private void PaintSegments(bool lit, bool animate)
        {
            int taste = _state.EverApplied ? _appliedChoices : _state.SelectedChoices().Count;

            for (int i = 0; i < _segments.Count; i++)
            {
                bool on = lit && (i < 40 || i - 40 < taste);
                Brush target = !on ? Theme.SegOff : (i < 40 ? Theme.SegOn : Theme.Red);

                if (!animate || Theme.ReduceMotion)
                {
                    _segments[i].Background = target;
                    continue;
                }

                var solid = target as SolidColorBrush;
                if (solid == null) { _segments[i].Background = target; continue; }

                var fade = new ColorAnimation(solid.Color, Theme.SegmentFade)
                {
                    BeginTime = TimeSpan.FromMilliseconds(i * 16),
                    EasingFunction = Theme.Mech()
                };
                var animated = new SolidColorBrush(((SolidColorBrush)_segments[i].Background).Color);
                _segments[i].Background = animated;
                animated.BeginAnimation(SolidColorBrush.ColorProperty, fade);
            }
        }

        private void PaintLegend()
        {
            var row = (Grid)_legend.Children[0];
            row.Children.Clear();

            int taste = _state.EverApplied ? _appliedChoices : _state.SelectedChoices().Count;
            string[] counts = { "40", "21", "7" };
            string[] words =
            {
                S.LegendGains,
                string.Format(CultureInfo.CurrentCulture, S.LegendTaste, taste),
                S.LegendLosses
            };

            for (int i = 0; i < 3; i++)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                TextBlock number = Text.MicroStrong(counts[i], Theme.GroundInk);
                number.Margin = new Thickness(0, 0, Theme.Space2, 0);
                TextBlock word = Text.Micro(words[i], Theme.GroundMuted);
                word.TextWrapping = TextWrapping.NoWrap;
                line.Children.Add(number);
                line.Children.Add(word);
                Grid.SetColumn(line, i);
                row.Children.Add(line);
            }
        }

        /// <summary>
        /// How many gains are actually in the live file right now, as opposed to how
        /// many the plan intends, so a half-applied machine reports honestly.
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
                if (d.Entry == null || !d.Differs || d.Entry.Class == PolicyClass.Loss) continue;
                string actual;
                if (!onDisk.TryGetValue(d.Guid ?? string.Empty, out actual)) continue;
                if (string.Equals(actual, d.EeaState, StringComparison.OrdinalIgnoreCase)) count++;
            }
            return count;
        }

        private bool ChoicesChanged()
        {
            return _state.EverApplied && _state.SelectedChoices().Count != _appliedChoices;
        }

        // -- apply --------------------------------------------------------------

        private async void Apply()
        {
            if (_busy) return;

            int wanted = _state.SelectedChoices().Count;
            SetBusy(true);
            _resultPanel.Children.Clear();

            int step = 0;
            var progress = new Progress(delegate
            {
                int done = ++step;
                Dispatcher.BeginInvoke(new Action(delegate { _apply.SetProgress(done / 7.0, true); }));
            });

            EngineResult result = await Task.Run(new Func<EngineResult>(delegate { return Engine.Apply(_state, progress); }));

            _plan = Engine.Preview(_state);
            _appliedChoices = result.Ok || result.Outcome == EngineOutcome.Partial ? wanted : _appliedChoices;

            SetBusy(false);
            _apply.SetProgress(0, false);

            if (!Theme.ReduceMotion && result.Outcome != EngineOutcome.NeedsElevation)
            {
                var slide = new DoubleAnimation(
                    Backups.ClassifyLive() == PolicyFileOrigin.Ours ? -Theme.PlaneOverlap : 0,
                    Theme.PlaneTravel) { EasingFunction = Theme.Mech() };
                _plane2Shift.BeginAnimation(TranslateTransform.XProperty, slide);
            }

            ShowResult(result);
            RefreshStatus();
            PaintSegments(Backups.ClassifyLive() == PolicyFileOrigin.Ours, true);
            AnimateCount();
        }

        private void AnimateCount()
        {
            int target;
            if (!int.TryParse(_count.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out target)) return;
            if (Theme.ReduceMotion || target == 0) return;

            var anim = new Int32Animation(0, target, Theme.Count) { EasingFunction = Theme.Mech() };
            var clock = anim.CreateClock();
            clock.CurrentTimeInvalidated += delegate
            {
                double? p = clock.CurrentProgress;
                if (!p.HasValue) return;
                _count.Text = ((int)Math.Round(target * p.Value)).ToString(CultureInfo.InvariantCulture);
            };
            clock.Completed += delegate { _count.Text = target.ToString(CultureInfo.InvariantCulture); };
            clock.Controller.Begin();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _apply.Active = !busy;
            _apply.Caption = busy ? S.Working : (_state.EverApplied ? S.ReapplySuperset : S.ApplySuperset);
            Cursor = busy ? Cursors.Wait : Cursors.Arrow;
        }

        private void ShowResult(EngineResult result)
        {
            _resultPanel.Children.Clear();

            L headline = result.Outcome == EngineOutcome.Success ? S.DoneApplied
                       : result.Outcome == EngineOutcome.NeedsElevation ? S.NeedAdmin
                       : result.Outcome == EngineOutcome.Partial ? S.StatusPartial : S.Failed;

            TextBlock head = Text.MicroStrong(headline, Theme.Plane2Ink);
            head.Margin = new Thickness(0, Theme.Space3, 0, 0);
            _resultPanel.Children.Add(head);

            foreach (string problem in result.Problems)
            {
                TextBlock line = Text.Micro("· " + problem, Theme.Plane2Muted);
                line.Margin = new Thickness(0, Theme.Space1, 0, 0);
                _resultPanel.Children.Add(line);
            }

            if (result.Report == null) return;
            foreach (Check check in result.Report.Checks)
            {
                if (check.Result == CheckResult.Pass) continue;
                TextBlock line = Text.Micro("· " + check.Title + "　" + Layout.LabelFor(check.Result), Theme.Plane2Muted);
                line.Margin = new Thickness(0, Theme.Space1, 0, 0);
                _resultPanel.Children.Add(line);
            }
        }

        // -- theme ---------------------------------------------------------------

        private void WatchSystemTheme()
        {
            _themeWatch = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _themeWatch.Tick += delegate
            {
                ThemeMode wanted = SystemTheme.IsDark() ? ThemeMode.Dark : ThemeMode.Light;
                if (wanted == Theme.Mode) return;
                Theme.Mode = wanted;
                BuildWindow();
            };
            _themeWatch.Start();
            Closed += delegate { if (_themeWatch != null) _themeWatch.Stop(); };
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
