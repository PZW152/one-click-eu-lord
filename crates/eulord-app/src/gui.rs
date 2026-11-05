#![allow(deprecated)]
//! Windows 11 (Fluent) style GUI.
//!
//! Layout follows the WinUI 3 NavigationView pattern: a custom title bar,
//! a left navigation pane on the window backdrop, and a rounded content
//! layer holding settings-style cards. Colors follow the system light/dark
//! theme and the Windows default accent.

use std::path::PathBuf;
use std::sync::Arc;
use std::sync::mpsc::{self, Receiver};
use std::time::{Duration, Instant};

use eframe::egui::{
    self, Align, Align2, Color32, CornerRadius, FontData, FontDefinitions, FontFamily, FontId,
    Frame, Layout, Margin, Pos2, Rect, RichText, Sense, Stroke, StrokeKind, TextureHandle, Theme,
    Vec2,
};
use eulord_core::catalog::{self, PolicyClass};
use eulord_core::compat::{CompatLevel, OsInfo};
use eulord_core::i18n::{Lang, LangPref, tr};
use eulord_core::plan::{Plan, PolicyDecision, PolicyOverride};
use eulord_core::state::State;
use eulord_windows::{CheckStatus, PolicyOrigin, TaskStatus, VerifyReport, WindowsSystem};

use crate::cli::Cli;
use crate::engine::{self, ApplyOutcome, ApplyReport, StepState, StepUpdate};

// ---------------------------------------------------------------------------
// Design tokens
// ---------------------------------------------------------------------------

const SEMIBOLD: &str = "eulord_semibold";
const ICONS: &str = "eulord_icons";
/// Set once Segoe Fluent Icons / Segoe MDL2 Assets has been loaded.
static ICON_FONT: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

/// Glyphs from Segoe Fluent Icons (Windows 11) / Segoe MDL2 Assets (Windows 10).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Ico {
    Home,
    List,
    Unlock,
    Repair,
    Info,
    Globe,
    Document,
    Sync,
    Shield,
    Search,
    Undo,
    Delete,
    Upload,
    Download,
    Folder,
    Settings,
    Package,
    Check,
    Save,
}

impl Ico {
    fn glyph(self) -> &'static str {
        if !ICON_FONT.load(std::sync::atomic::Ordering::Relaxed) {
            return "";
        }
        match self {
            Ico::Home => "\u{E80F}",
            Ico::List => "\u{E8FD}",
            Ico::Unlock => "\u{E785}",
            Ico::Repair => "\u{E90F}",
            Ico::Info => "\u{E946}",
            Ico::Globe => "\u{E774}",
            Ico::Document => "\u{E8A5}",
            Ico::Sync => "\u{E895}",
            Ico::Shield => "\u{EA18}",
            Ico::Search => "\u{E721}",
            Ico::Undo => "\u{E7A7}",
            Ico::Delete => "\u{E74D}",
            Ico::Upload => "\u{E898}",
            Ico::Download => "\u{E896}",
            Ico::Folder => "\u{E8B7}",
            Ico::Settings => "\u{E713}",
            Ico::Package => "\u{E7B8}",
            Ico::Check => "\u{E73E}",
            Ico::Save => "\u{E74E}",
        }
    }
}

fn icon_font(size: f32) -> FontId {
    FontId::new(size, FontFamily::Name(ICONS.into()))
}
const CONTENT_MAX_WIDTH: f32 = 1000.0;
const TITLEBAR_H: f32 = 40.0;
const NAV_W: f32 = 240.0;

/// Fluent-ish color tokens for one theme.
#[derive(Clone, Copy)]
struct Pal {
    dark: bool,
    /// Window backdrop (title bar + navigation pane).
    bg: Color32,
    /// Rounded content layer behind the cards.
    layer: Color32,
    card: Color32,
    stroke: Color32,
    text: Color32,
    text2: Color32,
    text3: Color32,
    accent: Color32,
    accent_hover: Color32,
    on_accent: Color32,
    subtle: Color32,
    subtle2: Color32,
    control: Color32,
    control_hover: Color32,
    control_stroke: Color32,
    seg_selected: Color32,
    ok: Color32,
    warn: Color32,
    bad: Color32,
    ok_bg: Color32,
    warn_bg: Color32,
    bad_bg: Color32,
    info_bg: Color32,
    danger: Color32,
    danger_hover: Color32,
}

impl Pal {
    const DARK: Pal = Pal {
        dark: true,
        bg: Color32::from_rgb(32, 32, 32),
        layer: Color32::from_rgb(39, 39, 39),
        card: Color32::from_rgb(45, 45, 45),
        stroke: Color32::from_rgb(29, 29, 29),
        text: Color32::from_rgb(255, 255, 255),
        text2: Color32::from_rgb(200, 200, 200),
        text3: Color32::from_rgb(158, 158, 158),
        accent: Color32::from_rgb(96, 205, 255),
        accent_hover: Color32::from_rgb(88, 188, 234),
        on_accent: Color32::from_rgb(0, 0, 0),
        subtle: Color32::from_rgba_premultiplied(15, 15, 15, 15),
        subtle2: Color32::from_rgba_premultiplied(23, 23, 23, 23),
        control: Color32::from_rgb(55, 55, 55),
        control_hover: Color32::from_rgb(62, 62, 62),
        control_stroke: Color32::from_rgb(68, 68, 68),
        seg_selected: Color32::from_rgb(64, 64, 64),
        ok: Color32::from_rgb(108, 203, 95),
        warn: Color32::from_rgb(252, 225, 0),
        bad: Color32::from_rgb(255, 153, 164),
        ok_bg: Color32::from_rgb(57, 61, 27),
        warn_bg: Color32::from_rgb(67, 53, 25),
        bad_bg: Color32::from_rgb(68, 39, 38),
        info_bg: Color32::from_rgb(45, 45, 45),
        danger: Color32::from_rgb(196, 43, 28),
        danger_hover: Color32::from_rgb(214, 60, 44),
    };

    const LIGHT: Pal = Pal {
        dark: false,
        bg: Color32::from_rgb(243, 243, 243),
        layer: Color32::from_rgb(249, 249, 249),
        card: Color32::from_rgb(255, 255, 255),
        stroke: Color32::from_rgb(229, 229, 229),
        text: Color32::from_rgb(27, 27, 27),
        text2: Color32::from_rgb(96, 96, 96),
        text3: Color32::from_rgb(133, 133, 133),
        accent: Color32::from_rgb(0, 95, 184),
        accent_hover: Color32::from_rgb(25, 110, 191),
        on_accent: Color32::from_rgb(255, 255, 255),
        subtle: Color32::from_rgba_premultiplied(0, 0, 0, 10),
        subtle2: Color32::from_rgba_premultiplied(0, 0, 0, 15),
        control: Color32::from_rgb(253, 253, 253),
        control_hover: Color32::from_rgb(246, 246, 246),
        control_stroke: Color32::from_rgb(222, 222, 222),
        seg_selected: Color32::from_rgb(255, 255, 255),
        ok: Color32::from_rgb(15, 123, 15),
        warn: Color32::from_rgb(157, 93, 0),
        bad: Color32::from_rgb(196, 43, 28),
        ok_bg: Color32::from_rgb(223, 246, 221),
        warn_bg: Color32::from_rgb(255, 244, 206),
        bad_bg: Color32::from_rgb(253, 231, 233),
        info_bg: Color32::from_rgb(246, 246, 246),
        danger: Color32::from_rgb(196, 43, 28),
        danger_hover: Color32::from_rgb(178, 36, 22),
    };

    fn of(theme: Theme) -> Self {
        match theme {
            Theme::Dark => Self::DARK,
            Theme::Light => Self::LIGHT,
        }
    }

    fn on_sev(&self) -> Color32 {
        if self.dark {
            Color32::BLACK
        } else {
            Color32::WHITE
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Sev {
    Info,
    Ok,
    Warn,
    Bad,
}

impl Sev {
    fn fg(self, p: &Pal) -> Color32 {
        match self {
            Sev::Info => p.accent,
            Sev::Ok => p.ok,
            Sev::Warn => p.warn,
            Sev::Bad => p.bad,
        }
    }
    fn bg(self, p: &Pal) -> Color32 {
        match self {
            Sev::Info => p.info_bg,
            Sev::Ok => p.ok_bg,
            Sev::Warn => p.warn_bg,
            Sev::Bad => p.bad_bg,
        }
    }
    fn glyph(self) -> &'static str {
        match self {
            Sev::Info => "i",
            Sev::Ok => "",
            Sev::Warn => "!",
            Sev::Bad => "",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Btn {
    Accent,
    Standard,
    Danger,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Caption {
    Minimize,
    Maximize,
    Restore,
    Close,
}

fn semibold() -> FontFamily {
    FontFamily::Name(SEMIBOLD.into())
}

fn rt(text: impl Into<String>, size: f32, color: Color32) -> RichText {
    RichText::new(text).size(size).color(color)
}

fn rts(text: impl Into<String>, size: f32, color: Color32) -> RichText {
    RichText::new(text)
        .size(size)
        .color(color)
        .family(semibold())
}

// ---------------------------------------------------------------------------
// App state (logic unchanged from v3)
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Page {
    Overview,
    Policies,
    Unlocks,
    Maintenance,
    About,
}

impl Page {
    const ALL: [Page; 5] = [
        Page::Overview,
        Page::Policies,
        Page::Unlocks,
        Page::Maintenance,
        Page::About,
    ];
    fn icon(self) -> Ico {
        match self {
            Self::Overview => Ico::Home,
            Self::Policies => Ico::List,
            Self::Unlocks => Ico::Unlock,
            Self::Maintenance => Ico::Repair,
            Self::About => Ico::Info,
        }
    }
    fn key(self) -> &'static str {
        match self {
            Self::Overview => "overview",
            Self::Policies => "policies",
            Self::Unlocks => "unlocks",
            Self::Maintenance => "maintenance",
            Self::About => "about",
        }
    }
    fn title_key(self) -> &'static str {
        match self {
            Self::Overview => "nav_overview",
            Self::Policies => "nav_policies",
            Self::Unlocks => "nav_unlocks",
            Self::Maintenance => "nav_maintenance",
            Self::About => "nav_about",
        }
    }
    fn from_key(key: &str) -> Self {
        Self::ALL
            .into_iter()
            .find(|p| p.key() == key)
            .unwrap_or(Self::Overview)
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Filter {
    All,
    Benefits,
    Losses,
    Choices,
}

#[derive(Debug, Clone)]
struct Snapshot {
    os: OsInfo,
    elevated: bool,
    origin: PolicyOrigin,
    region: Option<u32>,
    task: TaskStatus,
    backups: bool,
    plan: Option<Plan>,
    plan_error: Option<String>,
}

impl Snapshot {
    fn load(system: &WindowsSystem, state: &State) -> Self {
        let (plan, plan_error) = match engine::make_plan(system, state) {
            Ok(plan) => (Some(plan), None),
            Err(err) => (None, Some(err.to_string())),
        };
        Self {
            os: system.os_info(),
            elevated: system.is_elevated(),
            origin: system.classify_policy(state),
            region: system.read_region().ok().flatten(),
            task: system.task_status(),
            backups: system.paths.pristine_backup().exists()
                || system.paths.original_backup().exists(),
            plan,
            plan_error,
        }
    }
}

#[derive(Debug)]
enum JobMessage {
    Step(StepUpdate),
    Done(ApplyReport),
}

struct Job {
    rx: Receiver<JobMessage>,
}

struct Toast {
    text: String,
    error: bool,
    until: Instant,
}

pub struct App {
    system: WindowsSystem,
    state: State,
    lang: Lang,
    lang_pref: LangPref,
    locale: String,
    page: Page,
    snapshot: Snapshot,
    show_welcome: bool,
    show_review: bool,
    show_restore: bool,
    show_purge: bool,
    purge_text: String,
    verify_report: Option<VerifyReport>,
    busy: Option<Job>,
    progress: f32,
    steps: Vec<StepUpdate>,
    last_report: Option<ApplyReport>,
    toast: Option<Toast>,
    search: String,
    filter: Filter,
    expert: bool,
    p: Pal,
    icon: Option<TextureHandle>,
}

impl App {
    pub fn new(cc: &eframe::CreationContext<'_>, cli: &Cli) -> Self {
        let paths =
            eulord_core::paths::Paths::discover(cli.data_dir.clone(), cli.policy_file.clone())
                .expect("valid paths");
        let system = WindowsSystem::new(paths);
        let local = read_local_state();
        let mut state = match system.read_state() {
            Ok(state) => state,
            Err(_) => local.clone().unwrap_or_default(),
        };
        if let Some(local) = local {
            state.ui = local.ui;
            state.language_preference = local.language_preference;
            if state.applied_utc.is_none() {
                state.overrides = local.overrides;
                state.unclassified = local.unclassified;
            }
        }
        let locale = eulord_windows::user_locale();
        let lang_pref = state.language_preference;
        let lang = lang_pref.resolve(&locale);
        let page = Page::from_key(&state.ui.page);
        let expert = state.ui.expert_mode;
        let show_welcome = !state.ui.first_run_done;
        let snapshot = Snapshot::load(&system, &state);
        let icon = load_icon(&cc.egui_ctx);
        let mut app = Self {
            system,
            state,
            lang,
            lang_pref,
            locale,
            page,
            snapshot,
            show_welcome,
            show_review: false,
            show_restore: false,
            show_purge: false,
            purge_text: String::new(),
            verify_report: None,
            busy: None,
            progress: 0.0,
            steps: Vec::new(),
            last_report: None,
            toast: None,
            search: String::new(),
            filter: Filter::All,
            expert,
            p: Pal::of(cc.egui_ctx.theme()),
            icon,
        };
        apply_style(&cc.egui_ctx);
        load_fonts(&cc.egui_ctx, app.lang);
        if let Some(err) = &app.snapshot.plan_error {
            app.set_toast(format!("plan: {err}"), true);
        }
        app
    }

    fn t(&self, key: &'static str) -> &'static str {
        tr(self.lang, key)
    }

    /// Inline localization for UI-only strings that are not in the shared table.
    fn l(&self, tw: &'static str, cn: &'static str, en: &'static str) -> &'static str {
        match self.lang {
            Lang::ZhHant => tw,
            Lang::ZhHans => cn,
            Lang::En => en,
        }
    }

    fn refresh(&mut self) {
        if let Ok(state) = self.system.read_state() {
            self.state = state;
        }
        self.lang_pref = self.state.language_preference;
        self.lang = self.lang_pref.resolve(&self.locale);
        self.snapshot = Snapshot::load(&self.system, &self.state);
    }

    fn refresh_plan(&mut self) {
        match engine::make_plan(&self.system, &self.state) {
            Ok(plan) => {
                self.snapshot.plan = Some(plan);
                self.snapshot.plan_error = None;
            }
            Err(err) => {
                self.snapshot.plan = None;
                self.snapshot.plan_error = Some(err.to_string());
            }
        }
        self.snapshot.origin = self.system.classify_policy(&self.state);
    }

    fn persist_state(&mut self) {
        self.state.ui.page = self.page.key().to_string();
        self.state.ui.expert_mode = self.expert;
        self.state.language_preference = self.lang_pref;
        let local = save_local_state(&self.state);
        if let Err(err) = self.system.save_state(&self.state)
            && local.is_err()
        {
            self.set_toast(format!("preferences not saved: {err}"), true);
        }
    }

    fn set_toast(&mut self, text: impl Into<String>, error: bool) {
        self.toast = Some(Toast {
            text: text.into(),
            error,
            until: Instant::now() + Duration::from_secs(6),
        });
    }

    fn set_language(&mut self, ctx: &egui::Context, pref: LangPref) {
        self.lang_pref = pref;
        self.lang = pref.resolve(&self.locale);
        load_fonts(ctx, self.lang);
        self.persist_state();
    }

    fn set_override(&mut self, id: &str, value: PolicyOverride) {
        self.state.set_override(id, value);
        self.refresh_plan();
        self.persist_state();
    }

    fn go(&mut self, page: Page) {
        self.page = page;
        self.persist_state();
    }

    fn start_apply(&mut self) {
        let Some(plan) = self.snapshot.plan.clone() else {
            self.set_toast("no plan", true);
            return;
        };
        self.show_review = false;
        self.busy = Some(self.spawn_apply(plan));
        self.progress = 0.0;
        self.steps.clear();
        self.last_report = None;
    }

    fn spawn_apply(&mut self, plan: Plan) -> Job {
        let (tx, rx) = mpsc::channel();
        if self.system.is_elevated() {
            let system = WindowsSystem::new(self.system.paths.clone());
            let mut state = self.state.clone();
            std::thread::spawn(move || {
                let report = engine::apply(&system, &mut state, &plan, |step| {
                    let _ = tx.send(JobMessage::Step(step));
                });
                let _ = tx.send(JobMessage::Done(report));
            });
        } else {
            let temp =
                std::env::temp_dir().join(format!("eulord-state-{}.json", std::process::id()));
            let Ok(text) = self.state.to_json() else {
                let _ = tx.send(JobMessage::Done(ApplyReport::from_message(
                    ApplyOutcome::Failed,
                    "could not encode state",
                )));
                return Job { rx };
            };
            let _ = std::fs::write(&temp, text);
            let exe = eulord_core::paths::Paths::current_exe()
                .unwrap_or_else(|_| std::path::PathBuf::from("eulord.exe"));
            let args = vec![
                "--apply".to_string(),
                "--quiet".to_string(),
                "--data-dir".to_string(),
                self.system.paths.data_root.to_string_lossy().to_string(),
                "--policy-file".to_string(),
                self.system.paths.policy_file.to_string_lossy().to_string(),
                "--state-file".to_string(),
                temp.to_string_lossy().to_string(),
            ];
            std::thread::spawn(move || {
                let code = eulord_windows::run_elevated(&exe, &args).unwrap_or(1);
                let _ = std::fs::remove_file(&temp);
                let outcome = match code {
                    0 => ApplyOutcome::Success,
                    4 => ApplyOutcome::Partial,
                    3 => ApplyOutcome::NeedsElevation,
                    _ => ApplyOutcome::Failed,
                };
                let _ = tx.send(JobMessage::Done(ApplyReport::from_message(
                    outcome,
                    format!("elevated worker exit code {code}"),
                )));
            });
        }
        Job { rx }
    }

    fn spawn_elevated_verb(&mut self, verb: &'static str, extra: Vec<String>) -> Job {
        let (tx, rx) = mpsc::channel();
        let temp = std::env::temp_dir().join(format!("eulord-state-{}.json", std::process::id()));
        let Ok(text) = self.state.to_json() else {
            let _ = tx.send(JobMessage::Done(ApplyReport::from_message(
                ApplyOutcome::Failed,
                "could not encode state",
            )));
            return Job { rx };
        };
        let _ = std::fs::write(&temp, text);
        let exe = eulord_core::paths::Paths::current_exe()
            .unwrap_or_else(|_| std::path::PathBuf::from("eulord.exe"));
        let mut args = vec![
            verb.to_string(),
            "--quiet".to_string(),
            "--data-dir".to_string(),
            self.system.paths.data_root.to_string_lossy().to_string(),
            "--state-file".to_string(),
            temp.to_string_lossy().to_string(),
        ];
        args.extend(extra);
        std::thread::spawn(move || {
            let code = eulord_windows::run_elevated(&exe, &args).unwrap_or(1);
            let _ = std::fs::remove_file(&temp);
            let outcome = match code {
                0 => ApplyOutcome::Success,
                4 => ApplyOutcome::Partial,
                3 => ApplyOutcome::NeedsElevation,
                _ => ApplyOutcome::Failed,
            };
            let _ = tx.send(JobMessage::Done(ApplyReport::from_message(
                outcome,
                format!("elevated worker exit code {code}"),
            )));
        });
        Job { rx }
    }

    fn start_restore(&mut self) {
        self.show_restore = false;
        if self.system.is_elevated() {
            let (tx, rx) = mpsc::channel();
            let system = WindowsSystem::new(self.system.paths.clone());
            let mut state = self.state.clone();
            std::thread::spawn(move || {
                let report = engine::restore(&system, &mut state, |step| {
                    let _ = tx.send(JobMessage::Step(step));
                });
                let _ = tx.send(JobMessage::Done(report));
            });
            self.busy = Some(Job { rx });
        } else {
            self.busy = Some(self.spawn_elevated_verb("--restore", Vec::new()));
        }
        self.progress = 0.0;
        self.steps.clear();
    }

    fn start_purge(&mut self) {
        self.show_purge = false;
        if self.system.is_elevated() {
            let (tx, rx) = mpsc::channel();
            let system = WindowsSystem::new(self.system.paths.clone());
            let mut state = self.state.clone();
            std::thread::spawn(move || {
                let report = engine::purge(&system, &mut state, |step| {
                    let _ = tx.send(JobMessage::Step(step));
                });
                let _ = tx.send(JobMessage::Done(report));
            });
            self.busy = Some(Job { rx });
        } else {
            self.busy = Some(self.spawn_elevated_verb("--purge", vec!["--yes".to_string()]));
        }
        self.progress = 0.0;
        self.steps.clear();
    }

    fn poll_job(&mut self, ctx: &egui::Context) {
        let messages: Vec<JobMessage> = self
            .busy
            .as_ref()
            .map(|job| job.rx.try_iter().collect())
            .unwrap_or_default();
        let mut done: Option<ApplyReport> = None;
        for message in messages {
            match message {
                JobMessage::Step(step) => {
                    self.progress = step.progress;
                    self.steps.push(step);
                }
                JobMessage::Done(report) => {
                    done = Some(report);
                }
            }
        }
        if let Some(report) = done {
            self.progress = 1.0;
            self.busy = None;
            let error = report.outcome != ApplyOutcome::Success;
            let text = format!("{}: {}", self.outcome_label(report.outcome), report.message);
            self.last_report = Some(report);
            self.refresh();
            self.set_toast(text, error);
        } else if self.busy.is_some() {
            ctx.request_repaint_after(Duration::from_millis(120));
        }
    }

    fn outcome_label(&self, outcome: ApplyOutcome) -> &'static str {
        match outcome {
            ApplyOutcome::Success => self.t("apply_done"),
            ApplyOutcome::Partial => self.l("部分完成", "部分完成", "Partially applied"),
            ApplyOutcome::NeedsElevation => self.l(
                "需要管理員權限",
                "需要管理员权限",
                "Administrator rights required",
            ),
            _ => self.t("apply_failed"),
        }
    }

    fn compat_label(&self) -> (&'static str, Sev) {
        match self.snapshot.os.compat {
            CompatLevel::Tested => (
                self.l("已測試的版本", "已测试的版本", "Tested build"),
                Sev::Ok,
            ),
            CompatLevel::Known => (self.l("已知版本", "已知版本", "Known build"), Sev::Ok),
            CompatLevel::Unknown => (self.l("未知版本", "未知版本", "Unknown build"), Sev::Warn),
            CompatLevel::Unsupported => (self.l("不支援", "不支持", "Unsupported"), Sev::Bad),
        }
    }

    /// Overall status shown on the overview hero card.
    fn overall_status(&self) -> (&'static str, &'static str, Sev) {
        match self.snapshot.origin {
            PolicyOrigin::Ours if self.snapshot.task == TaskStatus::Ready => (
                self.t("status_active"),
                self.l(
                    "EU 政策已生效。排程會在登入、每天與 Windows Update 之後自動補回。",
                    "EU 策略已生效。计划任务会在登录、每天与 Windows 更新之后自动补回。",
                    "EU policies are active. A scheduled task restores them at sign-in, daily and after Windows Update.",
                ),
                Sev::Ok,
            ),
            PolicyOrigin::Ours => (
                self.t("status_partial"),
                self.l(
                    "政策檔已套用，但自動補回排程不在預期狀態。建議重新套用。",
                    "策略文件已应用，但自动补回计划任务不在预期状态。建议重新应用。",
                    "The policy file is applied, but the restore task isn't in the expected state. Re-applying is recommended.",
                ),
                Sev::Warn,
            ),
            PolicyOrigin::WindowsOriginal if self.state.ever_applied() => (
                self.t("status_partial"),
                self.l(
                    "政策檔已被 Windows 還原（通常是更新造成）。重新套用即可補回。",
                    "策略文件已被 Windows 还原（通常是更新造成）。重新应用即可补回。",
                    "Windows reverted the policy file (usually after an update). Re-apply to restore it.",
                ),
                Sev::Warn,
            ),
            PolicyOrigin::WindowsOriginal => (
                self.t("status_not_configured"),
                self.l(
                    "目前是 Windows 原始設定。套用前會先列出所有變更讓你確認。",
                    "目前是 Windows 原始设置。应用前会先列出所有变更供你确认。",
                    "Windows is on its original settings. You'll review every change before anything is applied.",
                ),
                Sev::Info,
            ),
            _ => (
                self.t("status_unknown"),
                self.l(
                    "無法判斷政策檔狀態。請到「維護」執行驗證。",
                    "无法判断策略文件状态。请到“维护”执行验证。",
                    "The policy file state couldn't be determined. Run Verify under Maintenance.",
                ),
                Sev::Bad,
            ),
        }
    }
}

// ---------------------------------------------------------------------------
// Frame / chrome
// ---------------------------------------------------------------------------

impl eframe::App for App {
    fn clear_color(&self, visuals: &egui::Visuals) -> [f32; 4] {
        visuals.panel_fill.to_normalized_gamma_f32()
    }

    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        self.p = Pal::of(ctx.theme());
        self.poll_job(&ctx);
        if ctx.input(|i| i.key_pressed(egui::Key::Escape)) {
            if self.show_review {
                self.show_review = false;
            } else if self.show_restore {
                self.show_restore = false;
            } else if self.show_purge {
                self.show_purge = false;
            } else if self.verify_report.is_some() {
                self.verify_report = None;
            } else if !self.show_welcome {
                ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            }
        }
        self.draw_titlebar(ui);
        self.draw_sidebar(ui);
        self.draw_main(ui);
        self.draw_resize_grip(ui);
        self.draw_modals(&ctx);
        self.draw_toast(&ctx);
        if self.busy.is_some() {
            ctx.request_repaint_after(Duration::from_millis(120));
        }
    }
}

impl App {
    fn draw_titlebar(&mut self, ui: &mut egui::Ui) {
        let ctx = ui.ctx().clone();
        let p = self.p;
        egui::Panel::top("titlebar")
            .exact_size(TITLEBAR_H)
            .frame(Frame::NONE.fill(p.bg).inner_margin(Margin {
                left: 16,
                right: 0,
                top: 0,
                bottom: 0,
            }))
            .show(ui, |ui| {
                ui.spacing_mut().item_spacing.x = 0.0;
                ui.horizontal_centered(|ui| {
                    if let Some(icon) = &self.icon {
                        ui.add(egui::Image::new(egui::load::SizedTexture::new(
                            icon.id(),
                            Vec2::splat(16.0),
                        )));
                        ui.add_space(12.0);
                    }
                    ui.label(rt(self.t("app_name"), 12.0, p.text));
                    let drag_w = (ui.available_width() - 46.0 * 3.0).max(40.0);
                    let (_, response) = ui.allocate_exact_size(
                        Vec2::new(drag_w, TITLEBAR_H),
                        Sense::click_and_drag(),
                    );
                    if response.drag_started() {
                        ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
                    }
                    let maximized = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
                    if response.double_clicked() {
                        ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maximized));
                    }
                    if caption_button(ui, &p, Caption::Minimize).clicked() {
                        ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(true));
                    }
                    let kind = if maximized {
                        Caption::Restore
                    } else {
                        Caption::Maximize
                    };
                    if caption_button(ui, &p, kind).clicked() {
                        ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maximized));
                    }
                    if caption_button(ui, &p, Caption::Close).clicked() {
                        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                    }
                });
            });
    }

    fn draw_sidebar(&mut self, ui: &mut egui::Ui) {
        let p = self.p;
        egui::Panel::left("sidebar")
            .exact_size(NAV_W)
            .resizable(false)
            .frame(Frame::NONE.fill(p.bg).inner_margin(Margin {
                left: 8,
                right: 8,
                top: 4,
                bottom: 12,
            }))
            .show(ui, |ui| {
                ui.spacing_mut().item_spacing.y = 4.0;
                let mut target = None;
                for page in Page::ALL {
                    if nav_item(
                        ui,
                        &p,
                        page.icon(),
                        self.t(page.title_key()),
                        self.page == page,
                    )
                    .clicked()
                    {
                        target = Some(page);
                    }
                }
                if let Some(page) = target {
                    self.go(page);
                }
                ui.with_layout(Layout::bottom_up(Align::Min), |ui| {
                    ui.spacing_mut().item_spacing.y = 2.0;
                    let pad = |ui: &mut egui::Ui, text: RichText| {
                        ui.horizontal(|ui| {
                            ui.add_space(12.0);
                            ui.label(text);
                        });
                    };
                    pad(ui, rt(self.snapshot.os.build_label(), 12.0, p.text3));
                    ui.horizontal(|ui| {
                        ui.add_space(12.0);
                        let (sev, label) = if self.snapshot.elevated {
                            (Sev::Ok, self.t("status_elevated"))
                        } else {
                            (Sev::Info, self.t("status_standard"))
                        };
                        dot(ui, sev.fg(&p));
                        ui.label(rt(label, 12.0, p.text2));
                    });
                });
            });
    }

    fn draw_main(&mut self, ui: &mut egui::Ui) {
        let p = self.p;
        egui::CentralPanel::default()
            .frame(
                Frame::NONE
                    .fill(p.layer)
                    .stroke(Stroke::new(1.0, p.stroke))
                    .corner_radius(CornerRadius {
                        nw: 8,
                        ne: 0,
                        sw: 0,
                        se: 0,
                    }),
            )
            .show(ui, |ui| {
                egui::ScrollArea::vertical()
                    .auto_shrink([false, false])
                    .show(ui, |ui| {
                        Frame::NONE
                            .inner_margin(Margin {
                                left: 36,
                                right: 36,
                                top: 28,
                                bottom: 36,
                            })
                            .show(ui, |ui| {
                                let width = ui.available_width().min(CONTENT_MAX_WIDTH);
                                ui.set_width(width);
                                ui.set_max_width(width);
                                match self.page {
                                    Page::Overview => self.page_overview(ui),
                                    Page::Policies => self.page_policies(ui),
                                    Page::Unlocks => self.page_unlocks(ui),
                                    Page::Maintenance => self.page_maintenance(ui),
                                    Page::About => self.page_about(ui),
                                }
                            });
                    });
            });
    }
}

// ---------------------------------------------------------------------------
// Pages
// ---------------------------------------------------------------------------

impl App {
    fn page_overview(&mut self, ui: &mut egui::Ui) {
        let p = self.p;
        page_title(ui, &p, self.t("nav_overview"), None);
        let (status, status_desc, sev) = self.overall_status();
        let plan = self.snapshot.plan.clone();
        let mut apply_clicked = false;

        // Hero card: status + the one primary action.
        card(&p).show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                status_badge(ui, &p, sev, 40.0);
                ui.add_space(8.0);
                let text_w = (ui.available_width() - 200.0).max(160.0);
                ui.vertical(|ui| {
                    ui.set_max_width(text_w);
                    ui.spacing_mut().item_spacing.y = 4.0;
                    ui.label(rts(status, 20.0, p.text));
                    ui.add(egui::Label::new(rt(status_desc, 14.0, p.text2)).wrap());
                });
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    let label = if self.state.ever_applied() {
                        self.t("reapply")
                    } else {
                        self.t("apply_superset")
                    };
                    let enabled = plan.is_some() && self.busy.is_none();
                    if ui
                        .add_enabled_ui(enabled, |ui| button(ui, &p, label, Btn::Accent))
                        .inner
                        .clicked()
                    {
                        apply_clicked = true;
                    }
                });
            });
            ui.add_space(20.0);
            if let Some(plan) = &plan {
                ui.columns(3, |cols| {
                    stat(
                        &mut cols[0],
                        &p,
                        &format!("{} / {}", plan.gains(), plan.gains_possible()),
                        self.l("採用的 EU 好處", "采用的 EU 好处", "EU benefits taken"),
                        Sev::Ok,
                    );
                    stat(
                        &mut cols[1],
                        &p,
                        &plan.losses_refused.to_string(),
                        self.l("保留全球版的項目", "保留全球版的项目", "Kept as global"),
                        Sev::Warn,
                    );
                    stat(
                        &mut cols[2],
                        &p,
                        &format!("{} / {}", plan.choices_taken, plan.choices_present),
                        self.l(
                            "你選擇採用的項目",
                            "你选择采用的项目",
                            "Optional items you chose",
                        ),
                        Sev::Info,
                    );
                });
            } else {
                info_bar(
                    ui,
                    &p,
                    Sev::Bad,
                    &self
                        .snapshot
                        .plan_error
                        .clone()
                        .unwrap_or_else(|| self.t("plan_failed").to_string()),
                );
            }
            if !self.snapshot.elevated {
                ui.add_space(12.0);
                ui.label(rt(self.t("not_admin_hint"), 12.0, p.text3));
            }
        });
        if apply_clicked {
            if self.state.ever_applied() {
                self.start_apply();
            } else {
                self.show_review = true;
            }
        }
        ui.add_space(12.0);
        info_bar(ui, &p, Sev::Info, self.t("apply_no_uninstall"));

        // System status rows.
        section(ui, &p, self.l("系統狀態", "系统状态", "System status"));
        group(ui, |ui| {
            let region_ok = self.snapshot.region == Some(self.state.region_geoid);
            let region_value = match self.snapshot.region {
                Some(_) if region_ok => self.state.region_iso.to_string(),
                Some(geo) => format!("GeoID {geo} → {}", self.state.region_iso),
                None => "—".to_string(),
            };
            row(
                ui,
                &p,
                Some(Ico::Globe),
                self.t("status_region"),
                Some(self.l(
                    "Windows 依這個地區決定是否開放 EU 功能",
                    "Windows 依这个区域决定是否开放 EU 功能",
                    "Windows uses this region to decide which EU features apply",
                )),
                220.0,
                |ui| {
                    status_value(
                        ui,
                        &p,
                        &region_value,
                        if region_ok { Sev::Ok } else { Sev::Warn },
                    )
                },
            );
            let unknown = self
                .snapshot
                .plan
                .as_ref()
                .map(|plan| plan.unclassified.len())
                .unwrap_or(0);
            let policy_desc = match self.lang {
                Lang::ZhHant => format!(
                    "{} 條已分類；{} 條未知政策保持原樣",
                    catalog::all().len(),
                    unknown
                ),
                Lang::ZhHans => format!(
                    "{} 条已分类；{} 条未知策略保持原样",
                    catalog::all().len(),
                    unknown
                ),
                Lang::En => format!(
                    "{} catalogued; {} unknown policies left untouched",
                    catalog::all().len(),
                    unknown
                ),
            };
            let (origin_text, origin_sev) = match self.snapshot.origin {
                PolicyOrigin::Ours => (self.t("status_ours"), Sev::Ok),
                PolicyOrigin::WindowsOriginal => (self.t("status_original"), Sev::Info),
                PolicyOrigin::Missing => (self.t("status_missing"), Sev::Bad),
                PolicyOrigin::Unreadable => (self.t("status_unreadable"), Sev::Bad),
            };
            row(
                ui,
                &p,
                Some(Ico::Document),
                self.t("status_policy"),
                Some(&policy_desc),
                220.0,
                |ui| status_value(ui, &p, origin_text, origin_sev),
            );
            row(
                ui,
                &p,
                Some(Ico::Sync),
                self.t("status_persistence"),
                Some(self.l(
                    "登入、每天與 Windows Update 之後自動補回",
                    "登录、每天与 Windows 更新之后自动补回",
                    "Re-applied at sign-in, daily and after Windows Update",
                )),
                220.0,
                |ui| {
                    status_value(
                        ui,
                        &p,
                        task_label(self.snapshot.task, self.lang),
                        if self.snapshot.task == TaskStatus::Ready {
                            Sev::Ok
                        } else {
                            Sev::Warn
                        },
                    )
                },
            );
            row(
                ui,
                &p,
                Some(Ico::Shield),
                self.t("status_integrity"),
                Some(self.l(
                    "原子替換政策檔；失敗時自動回滾",
                    "原子替换策略文件；失败时自动回滚",
                    "Atomic policy replace with automatic rollback",
                )),
                220.0,
                |ui| {
                    let (text, sev) = if self.snapshot.backups {
                        (self.t("backup_ready"), Sev::Ok)
                    } else {
                        (self.t("backup_none"), Sev::Info)
                    };
                    status_value(ui, &p, text, sev)
                },
            );
        });

        // Recent activity, only when there is something to show.
        let mut lines: Vec<(Option<Sev>, String, String)> = Vec::new();
        if let Some(report) = &self.last_report {
            for step in report.steps.iter().rev().take(6).rev() {
                lines.push((step_sev(step.state), step.id.clone(), step.detail.clone()));
            }
        } else if let Some(record) = &self.state.last_apply {
            for step in record.steps.iter().rev().take(6).rev() {
                lines.push((
                    sev_from_str(&step.status),
                    String::new(),
                    step.detail.clone(),
                ));
            }
        }
        if !lines.is_empty() {
            section(
                ui,
                &p,
                self.l("最近一次操作", "最近一次操作", "Last operation"),
            );
            card(&p).show(ui, |ui| {
                ui.set_width(ui.available_width());
                for (sev, id, detail) in &lines {
                    step_line(ui, &p, *sev, id, detail);
                }
            });
        }
    }

    fn page_policies(&mut self, ui: &mut egui::Ui) {
        let p = self.p;
        page_title(
            ui,
            &p,
            self.t("nav_policies"),
            Some(if self.expert {
                self.t("expert_hint")
            } else {
                self.t("simple_hint")
            }),
        );

        // Toolbar: search, (expert) filter, expert toggle.
        let mut toggle_expert = false;
        let mut new_filter = None;
        let search_hint = self.t("search_policies");
        ui.horizontal(|ui| {
            ui.add(
                egui::TextEdit::singleline(&mut self.search)
                    .hint_text(search_hint)
                    .desired_width(260.0)
                    .margin(Margin::symmetric(10, 7)),
            );
            if self.expert {
                ui.add_space(8.0);
                new_filter = segmented(
                    ui,
                    &p,
                    &[
                        (self.t("filter_all"), Filter::All),
                        (self.t("filter_benefits"), Filter::Benefits),
                        (self.t("filter_losses"), Filter::Losses),
                        (self.t("filter_choices"), Filter::Choices),
                    ],
                    self.filter,
                );
            }
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                if toggle(ui, &p, self.expert).clicked() {
                    toggle_expert = true;
                }
                ui.add_space(8.0);
                ui.label(rt(self.t("expert_mode"), 14.0, p.text));
            });
        });
        if let Some(filter) = new_filter {
            self.filter = filter;
        }
        if toggle_expert {
            self.expert = !self.expert;
            if !self.expert {
                self.filter = Filter::All;
            }
            self.persist_state();
        }
        ui.add_space(12.0);

        let Some(plan) = self.snapshot.plan.clone() else {
            info_bar(
                ui,
                &p,
                Sev::Bad,
                &self
                    .snapshot
                    .plan_error
                    .clone()
                    .unwrap_or_else(|| self.t("plan_failed").to_string()),
            );
            return;
        };
        if !self.expert {
            let summary = match self.lang {
                Lang::ZhHant => format!(
                    "已自動處理：採用 {} 項 EU 好處、保留 {} 項全球版。下面 {} 項見仁見智由你決定，開啟＝採用 EU 版。",
                    plan.benefits_taken, plan.losses_refused, plan.choices_present
                ),
                Lang::ZhHans => format!(
                    "已自动处理：采用 {} 项 EU 好处、保留 {} 项全球版。下面 {} 项见仁见智由你决定，打开＝采用 EU 版。",
                    plan.benefits_taken, plan.losses_refused, plan.choices_present
                ),
                Lang::En => format!(
                    "Handled automatically: {} EU benefits taken, {} kept as global. The {} optional items below are up to you — on means use the EU version.",
                    plan.benefits_taken, plan.losses_refused, plan.choices_present
                ),
            };
            info_bar(ui, &p, Sev::Info, &summary);
            ui.add_space(8.0);
        }

        let query = self.search.to_lowercase();
        let mut rows: Vec<PolicyDecision> = plan
            .decisions
            .into_iter()
            .filter(|decision| {
                if !self.expert && decision.class != Some(PolicyClass::Choice) {
                    return false;
                }
                let class_ok = match self.filter {
                    Filter::All => true,
                    Filter::Benefits => decision.class == Some(PolicyClass::Benefit),
                    Filter::Losses => decision.class == Some(PolicyClass::Loss),
                    Filter::Choices => decision.class == Some(PolicyClass::Choice),
                };
                if !class_ok {
                    return false;
                }
                if query.is_empty() {
                    return true;
                }
                policy_description(decision, self.lang)
                    .to_lowercase()
                    .contains(&query)
                    || decision.guid.to_lowercase().contains(&query)
                    || decision
                        .id
                        .as_deref()
                        .unwrap_or("")
                        .to_lowercase()
                        .contains(&query)
            })
            .collect();
        rows.sort_by_key(|decision| {
            (
                decision.class.map(|c| c as u8).unwrap_or(9),
                decision.id.clone().unwrap_or_default(),
            )
        });

        if rows.is_empty() {
            ui.add_space(24.0);
            ui.vertical_centered(|ui| {
                ui.label(rt(
                    self.l("沒有符合的政策", "没有符合的策略", "No matching policies"),
                    14.0,
                    p.text3,
                ));
            });
            return;
        }

        let auto_label = self.l("自動", "自动", "Auto");
        let global_label = self.l("全球", "全球", "Global");
        let same_label = self.l("兩版相同", "两版相同", "Same in both");
        let toggle_hint = self.l(
            "開啟＝採用 EU 版本",
            "打开＝采用 EU 版本",
            "On = use the EU version",
        );
        let mut action: Option<(String, PolicyOverride)> = None;
        let mut current_class: Option<Option<PolicyClass>> = None;
        group(ui, |ui| {
            for decision in &rows {
                if self.expert && current_class != Some(decision.class) {
                    current_class = Some(decision.class);
                    let count = rows.iter().filter(|d| d.class == decision.class).count();
                    let label = decision
                        .class
                        .map(|c| self.t(c.label_key()))
                        .unwrap_or(self.t("unclassified"));
                    ui.add_space(14.0);
                    ui.horizontal(|ui| {
                        dot(ui, class_color(&p, decision.class));
                        ui.label(rts(label, 14.0, p.text));
                        ui.label(rt(count.to_string(), 13.0, p.text3));
                    });
                    ui.add_space(4.0);
                }
                let title = policy_description(decision, self.lang);
                let id_text = decision.id.as_deref().unwrap_or(&decision.guid);
                let caption = if decision.differs {
                    format!(
                        "{id_text}   ·   {global_label} {}  →  EU {}",
                        state_label(self.lang, &decision.global),
                        state_label(self.lang, &decision.eea)
                    )
                } else {
                    format!("{id_text}   ·   {same_label}")
                };
                let right_w = if self.expert { 230.0 } else { 70.0 };
                row(ui, &p, None, &title, Some(&caption), right_w, |ui| {
                    let Some(id) = &decision.id else {
                        ui.label(rt(self.t("unclassified"), 12.0, p.text3));
                        return;
                    };
                    let current = self.state.override_for(id);
                    if self.expert {
                        if let Some(value) = segmented(
                            ui,
                            &p,
                            &[
                                (auto_label, PolicyOverride::Auto),
                                ("EU", PolicyOverride::Eea),
                                (global_label, PolicyOverride::Global),
                            ],
                            current,
                        ) {
                            action = Some((id.clone(), value));
                        }
                    } else if decision.class == Some(PolicyClass::Choice) {
                        let on = current == PolicyOverride::Eea;
                        if toggle(ui, &p, on).on_hover_text(toggle_hint).clicked() {
                            action = Some((
                                id.clone(),
                                if on {
                                    PolicyOverride::Auto
                                } else {
                                    PolicyOverride::Eea
                                },
                            ));
                        }
                    }
                });
            }
        });
        if let Some((id, value)) = action {
            self.set_override(&id, value);
        }
    }

    fn page_unlocks(&mut self, ui: &mut egui::Ui) {
        let p = self.p;
        page_title(ui, &p, self.t("nav_unlocks"), Some(self.t("unlocks_body")));

        section(ui, &p, self.t("unlocks_software"));
        info_bar(ui, &p, Sev::Info, self.t("software_uninstall_note"));
        ui.add_space(4.0);
        group(ui, |ui| {
            for entry in catalog::software_uninstall_unlocks() {
                row(
                    ui,
                    &p,
                    Some(Ico::Package),
                    entry.describe(self.lang),
                    Some(&entry.id),
                    0.0,
                    |_| {},
                );
            }
        });
        ui.add_space(8.0);
        if button(ui, &p, self.t("open_windows_apps"), Btn::Standard).clicked()
            && let Err(err) = self.system.open_uri("ms-settings:appsfeatures")
        {
            self.set_toast(err.to_string(), true);
        }

        section(ui, &p, self.t("unlocks_feature"));
        if let Some(plan) = self.snapshot.plan.clone() {
            group(ui, |ui| {
                for decision in plan.decisions.iter().filter(|decision| {
                    decision.class == Some(PolicyClass::Benefit)
                        && !decision.software_uninstall_unlock
                }) {
                    let id_text = decision.id.as_deref().unwrap_or(&decision.guid);
                    row(
                        ui,
                        &p,
                        Some(Ico::Check),
                        &policy_description(decision, self.lang),
                        Some(id_text),
                        200.0,
                        |ui| {
                            ui.label(rt(
                                format!(
                                    "{} → {}",
                                    state_label(self.lang, &decision.global),
                                    state_label(self.lang, &decision.target)
                                ),
                                12.0,
                                p.text3,
                            ));
                        },
                    );
                }
            });
        }
    }

    fn page_maintenance(&mut self, ui: &mut egui::Ui) {
        let p = self.p;
        page_title(
            ui,
            &p,
            self.t("nav_maintenance"),
            Some(self.l(
                "驗證、還原，以及備份你的設定。",
                "验证、还原，以及备份你的设置。",
                "Verify, restore and back up your configuration.",
            )),
        );

        if let Some(report) = &self.last_report {
            let sev = match report.outcome {
                ApplyOutcome::Success => Sev::Ok,
                ApplyOutcome::Partial => Sev::Warn,
                _ => Sev::Bad,
            };
            info_bar(
                ui,
                &p,
                sev,
                &format!(
                    "{} — {}",
                    self.outcome_label(report.outcome),
                    report.message
                ),
            );
        }

        section(ui, &p, self.l("狀態", "状态", "Status"));
        group(ui, |ui| {
            row(
                ui,
                &p,
                Some(Ico::Save),
                self.t("backup_status"),
                Some(self.l(
                    "原始與乾淨的政策檔快照",
                    "原始与干净的策略文件快照",
                    "Original and pristine policy snapshots",
                )),
                200.0,
                |ui| {
                    let (text, sev) = if self.snapshot.backups {
                        (self.t("backup_ready"), Sev::Ok)
                    } else {
                        (self.t("backup_none"), Sev::Info)
                    };
                    status_value(ui, &p, text, sev)
                },
            );
            row(
                ui,
                &p,
                Some(Ico::Sync),
                self.t("status_persistence"),
                Some(self.l(
                    "SYSTEM 排程工作，3 個觸發條件",
                    "SYSTEM 计划任务，3 个触发器",
                    "SYSTEM scheduled task with 3 triggers",
                )),
                200.0,
                |ui| {
                    status_value(
                        ui,
                        &p,
                        task_label(self.snapshot.task, self.lang),
                        if self.snapshot.task == TaskStatus::Ready {
                            Sev::Ok
                        } else {
                            Sev::Warn
                        },
                    )
                },
            );
        });

        section(
            ui,
            &p,
            self.l("修復與還原", "修复与还原", "Repair and restore"),
        );
        let mut do_verify = false;
        let mut do_restore = false;
        let mut do_purge = false;
        group(ui, |ui| {
            row(
                ui,
                &p,
                Some(Ico::Search),
                self.t("verify"),
                Some(self.l(
                    "檢查政策檔、地區與排程是否符合預期",
                    "检查策略文件、区域与计划任务是否符合预期",
                    "Check that the policy file, region and scheduled task match the plan",
                )),
                140.0,
                |ui| do_verify = button(ui, &p, self.t("verify"), Btn::Standard).clicked(),
            );
            row(
                ui,
                &p,
                Some(Ico::Undo),
                self.t("restore"),
                Some(self.l(
                    "回到第一次套用前的 Windows 原始政策檔與地區",
                    "恢复到首次应用前的 Windows 原始策略文件与区域",
                    "Go back to the original Windows policy file and region",
                )),
                140.0,
                |ui| do_restore = button(ui, &p, self.t("restore"), Btn::Standard).clicked(),
            );
            row(
                ui,
                &p,
                Some(Ico::Delete),
                self.t("purge"),
                Some(self.l(
                    "移除排程、備份與本工具的所有資料",
                    "移除计划任务、备份与本工具的所有数据",
                    "Remove the scheduled task, backups and all app data",
                )),
                140.0,
                |ui| do_purge = button(ui, &p, self.t("purge"), Btn::Danger).clicked(),
            );
        });
        if do_verify {
            if let Some(plan) = self.snapshot.plan.clone() {
                self.verify_report = Some(engine::verify(&self.system, &self.state, &plan));
            } else {
                self.set_toast(self.t("plan_failed"), true);
            }
        }
        if do_restore {
            self.show_restore = true;
        }
        if do_purge {
            self.purge_text.clear();
            self.show_purge = true;
        }

        section(ui, &p, self.l("資料", "数据", "Data"));
        let mut do_export = false;
        let mut do_import = false;
        let mut do_logs = false;
        let mut do_folder = false;
        let mut do_updates = false;
        let open = self.l("開啟", "打开", "Open");
        group(ui, |ui| {
            row(
                ui,
                &p,
                Some(Ico::Upload),
                self.t("export"),
                Some(self.l(
                    "把目前的選擇存成 JSON 檔",
                    "把当前选择保存为 JSON 文件",
                    "Save your current choices as a JSON file",
                )),
                140.0,
                |ui| do_export = button(ui, &p, self.t("export"), Btn::Standard).clicked(),
            );
            row(
                ui,
                &p,
                Some(Ico::Download),
                self.t("import"),
                Some(self.l(
                    "從 JSON 檔載入選擇",
                    "从 JSON 文件载入选择",
                    "Load choices from a JSON file",
                )),
                140.0,
                |ui| do_import = button(ui, &p, self.t("import"), Btn::Standard).clicked(),
            );
            row(
                ui,
                &p,
                Some(Ico::Folder),
                self.l("記錄與資料夾", "日志与文件夹", "Logs and data folder"),
                None,
                260.0,
                |ui| {
                    do_folder = button(ui, &p, self.t("open_folder"), Btn::Standard).clicked();
                    do_logs = button(ui, &p, self.t("open_logs"), Btn::Standard).clicked();
                },
            );
            row(
                ui,
                &p,
                Some(Ico::Download),
                self.t("check_updates"),
                Some(self.l(
                    "在 GitHub Releases 查看新版本",
                    "在 GitHub Releases 查看新版本",
                    "See new versions on GitHub Releases",
                )),
                140.0,
                |ui| do_updates = button(ui, &p, open, Btn::Standard).clicked(),
            );
        });

        if do_export
            && let Some(path) = rfd::FileDialog::new()
                .set_file_name("eulord-state.json")
                .save_file()
        {
            match self.state.to_json().and_then(|text| {
                std::fs::write(&path, text)?;
                Ok(())
            }) {
                Ok(()) => self.set_toast(format!("exported {}", path.display()), false),
                Err(err) => self.set_toast(err.to_string(), true),
            }
        }
        if do_import && let Some(path) = rfd::FileDialog::new().pick_file() {
            match std::fs::read_to_string(path)
                .map_err(|err| err.to_string())
                .and_then(|text| State::from_json(&text).map_err(|err| err.to_string()))
            {
                Ok(state) => {
                    self.state = state;
                    let _ = self.system.save_state(&self.state);
                    self.refresh();
                    self.set_toast("state imported", false);
                }
                Err(err) => self.set_toast(err, true),
            }
        }
        if do_logs {
            let _ = self.system.open_path(&self.system.paths.log_file());
        }
        if do_folder {
            let _ = self.system.open_path(&self.system.paths.data_root);
        }
        if do_updates {
            let _ = self
                .system
                .open_uri(&format!("{}/releases", eulord_core::REPOSITORY));
        }
    }

    fn page_about(&mut self, ui: &mut egui::Ui) {
        let p = self.p;
        page_title(ui, &p, self.t("nav_about"), None);

        card(&p).show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                if let Some(icon) = &self.icon {
                    ui.add(egui::Image::new(egui::load::SizedTexture::new(
                        icon.id(),
                        Vec2::splat(56.0),
                    )));
                    ui.add_space(12.0);
                }
                ui.vertical(|ui| {
                    ui.spacing_mut().item_spacing.y = 2.0;
                    ui.label(rts(self.t("app_name"), 20.0, p.text));
                    ui.label(rt(self.t("tagline"), 14.0, p.text2));
                    ui.label(rt(format!("v{}", eulord_core::APP_VERSION), 12.0, p.text3));
                });
            });
            ui.add_space(12.0);
            ui.add(egui::Label::new(rt(self.t("about_body"), 14.0, p.text2)).wrap());
        });

        section(ui, &p, self.l("設定", "设置", "Settings"));
        let mut new_lang = None;
        let mut flip_expert = false;
        let lang_pref = self.lang_pref;
        group(ui, |ui| {
            row(
                ui,
                &p,
                Some(Ico::Globe),
                self.t("language"),
                None,
                200.0,
                |ui| {
                    let mut selected = lang_pref;
                    let label = |pref: LangPref| match pref {
                        LangPref::Auto => self.l("跟隨系統", "跟随系统", "Use system setting"),
                        LangPref::ZhHant => "繁體中文",
                        LangPref::ZhHans => "简体中文",
                        LangPref::En => "English",
                    };
                    egui::ComboBox::from_id_salt("language")
                        .selected_text(label(selected))
                        .width(160.0)
                        .show_ui(ui, |ui| {
                            for pref in [
                                LangPref::Auto,
                                LangPref::ZhHant,
                                LangPref::ZhHans,
                                LangPref::En,
                            ] {
                                ui.selectable_value(&mut selected, pref, label(pref));
                            }
                        });
                    if selected != lang_pref {
                        new_lang = Some(selected);
                    }
                },
            );
            row(
                ui,
                &p,
                Some(Ico::Settings),
                self.t("expert_mode"),
                Some(self.t("expert_hint")),
                80.0,
                |ui| flip_expert = toggle(ui, &p, self.expert).clicked(),
            );
        });
        if let Some(pref) = new_lang {
            self.set_language(&ui.ctx().clone(), pref);
        }
        if flip_expert {
            self.expert = !self.expert;
            self.persist_state();
        }

        section(ui, &p, self.l("系統資訊", "系统信息", "System information"));
        let (compat, compat_sev) = self.compat_label();
        group(ui, |ui| {
            row(ui, &p, None, self.t("version"), None, 260.0, |ui| {
                ui.label(rt(eulord_core::APP_VERSION, 14.0, p.text2));
            });
            row(
                ui,
                &p,
                None,
                self.l("政策目錄", "策略目录", "Policy catalog"),
                None,
                260.0,
                |ui| {
                    ui.label(rt(catalog::CATALOG_VERSION, 14.0, p.text2));
                },
            );
            row(ui, &p, None, "Windows", None, 320.0, |ui| {
                ui.label(rt(self.snapshot.os.full_label(), 14.0, p.text2));
            });
            row(ui, &p, None, self.t("compatibility"), None, 260.0, |ui| {
                status_value(ui, &p, compat, compat_sev)
            });
        });

        ui.add_space(16.0);
        if button(ui, &p, self.t("about_repo"), Btn::Standard).clicked() {
            let _ = self.system.open_uri(eulord_core::REPOSITORY);
        }
        ui.add_space(12.0);
        ui.add(egui::Label::new(rt(self.t("about_license"), 12.0, p.text3)).wrap());
    }
}

// ---------------------------------------------------------------------------
// Dialogs and overlays
// ---------------------------------------------------------------------------

impl App {
    fn draw_modals(&mut self, ctx: &egui::Context) {
        let p = self.p;
        if self.show_welcome {
            let (compat, compat_sev) = self.compat_label();
            let mut scan = false;
            let mut skip = false;
            dialog(ctx, &p, "welcome", |ui| {
                ui.label(rts(self.t("first_run_title"), 24.0, p.text));
                ui.add_space(8.0);
                ui.add(egui::Label::new(rt(self.t("first_run_body"), 14.0, p.text2)).wrap());
                ui.add_space(16.0);
                ui.horizontal(|ui| {
                    dot(ui, compat_sev.fg(&p));
                    ui.label(rt(
                        format!("{}  ·  {}", self.snapshot.os.full_label(), compat),
                        13.0,
                        p.text2,
                    ));
                });
                ui.add_space(12.0);
                info_bar(ui, &p, Sev::Info, self.t("apply_no_uninstall"));
                dialog_footer(ui, &p, |ui| {
                    skip = button(ui, &p, self.t("first_run_skip"), Btn::Standard).clicked();
                    scan = button(ui, &p, self.t("first_run_scan"), Btn::Accent).clicked();
                });
            });
            if scan || skip {
                self.show_welcome = false;
                self.state.ui.first_run_done = true;
                self.persist_state();
                if scan {
                    self.refresh();
                }
            }
        }

        if self.show_review {
            let plan = self.snapshot.plan.clone();
            let mut confirm = false;
            let mut to_policies = false;
            let mut cancel = false;
            dialog(ctx, &p, "review", |ui| {
                ui.label(rts(self.t("review_title"), 20.0, p.text));
                ui.add_space(4.0);
                ui.add(egui::Label::new(rt(self.t("review_body"), 14.0, p.text2)).wrap());
                ui.add_space(16.0);
                if let Some(plan) = &plan {
                    ui.columns(4, |cols| {
                        stat(
                            &mut cols[0],
                            &p,
                            &plan.benefits_taken.to_string(),
                            self.t("review_gains"),
                            Sev::Ok,
                        );
                        stat(
                            &mut cols[1],
                            &p,
                            &plan.losses_refused.to_string(),
                            self.t("review_kept"),
                            Sev::Warn,
                        );
                        stat(
                            &mut cols[2],
                            &p,
                            &plan.choices_taken.to_string(),
                            self.t("choices"),
                            Sev::Info,
                        );
                        stat(
                            &mut cols[3],
                            &p,
                            &plan.changed_policies.to_string(),
                            self.t("review_changed"),
                            Sev::Info,
                        );
                    });
                    if !plan.unclassified.is_empty() {
                        ui.add_space(8.0);
                        info_bar(ui, &p, Sev::Warn, self.t("unknown_policies_notice"));
                    }
                    ui.add_space(16.0);
                    ui.label(rts(
                        self.l("主要變更", "主要变更", "Top changes"),
                        14.0,
                        p.text,
                    ));
                    ui.add_space(4.0);
                    Frame::NONE
                        .fill(p.layer)
                        .stroke(Stroke::new(1.0, p.stroke))
                        .corner_radius(CornerRadius::same(6))
                        .inner_margin(Margin::symmetric(12, 8))
                        .show(ui, |ui| {
                            ui.set_width(ui.available_width());
                            for decision in plan.changed_decisions().take(7) {
                                ui.horizontal(|ui| {
                                    dot(ui, class_color(&p, decision.class));
                                    ui.label(rt(
                                        policy_description(decision, self.lang),
                                        13.0,
                                        p.text,
                                    ));
                                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                        ui.label(rt(
                                            format!(
                                                "{} → {}",
                                                state_label(self.lang, &decision.global),
                                                state_label(self.lang, &decision.target)
                                            ),
                                            12.0,
                                            p.text3,
                                        ));
                                    });
                                });
                            }
                            if plan.changed_policies > 7 {
                                ui.label(rt(
                                    self.l(
                                        "完整清單請看「政策矩陣」",
                                        "完整清单请看“政策矩阵”",
                                        "See Policy matrix for the full list",
                                    ),
                                    12.0,
                                    p.text3,
                                ));
                            }
                        });
                }
                ui.add_space(12.0);
                info_bar(ui, &p, Sev::Info, self.t("apply_no_uninstall"));
                if !self.snapshot.elevated {
                    ui.add_space(6.0);
                    ui.label(rt(self.t("admin_required"), 12.0, p.text3));
                }
                dialog_footer(ui, &p, |ui| {
                    cancel = button(ui, &p, self.t("cancel"), Btn::Standard).clicked();
                    to_policies = button(ui, &p, self.t("nav_policies"), Btn::Standard).clicked();
                    confirm = button(ui, &p, self.t("confirm_apply"), Btn::Accent).clicked();
                });
            });
            if confirm {
                self.start_apply();
            } else if to_policies {
                self.show_review = false;
                self.go(Page::Policies);
            } else if cancel {
                self.show_review = false;
            }
        }

        if self.busy.is_some() {
            dialog(ctx, &p, "busy", |ui| {
                ui.label(rts(self.t("applying"), 20.0, p.text));
                ui.add_space(12.0);
                ui.add(
                    egui::ProgressBar::new(self.progress)
                        .desired_height(4.0)
                        .fill(p.accent),
                );
                ui.add_space(12.0);
                for step in self.steps.iter().rev().take(8).rev() {
                    step_line(ui, &p, step_sev(step.state), &step.id, &step.detail);
                }
                ui.add_space(8.0);
                ui.add(egui::Label::new(rt(self.t("review_restart"), 12.0, p.text3)).wrap());
            });
        }

        if self.show_restore {
            let mut confirm = false;
            let mut cancel = false;
            dialog(ctx, &p, "restore", |ui| {
                ui.label(rts(self.t("restore"), 20.0, p.text));
                ui.add_space(8.0);
                ui.add(egui::Label::new(rt(self.t("restore_confirm"), 14.0, p.text2)).wrap());
                dialog_footer(ui, &p, |ui| {
                    cancel = button(ui, &p, self.t("cancel"), Btn::Standard).clicked();
                    confirm = button(ui, &p, self.t("restore"), Btn::Accent).clicked();
                });
            });
            if confirm {
                self.start_restore();
            } else if cancel {
                self.show_restore = false;
            }
        }

        if self.show_purge {
            let mut confirm = false;
            let mut cancel = false;
            dialog(ctx, &p, "purge", |ui| {
                ui.label(rts(self.t("purge"), 20.0, p.text));
                ui.add_space(8.0);
                ui.add(egui::Label::new(rt(self.t("purge_confirm"), 14.0, p.text2)).wrap());
                ui.add_space(12.0);
                ui.label(rt(
                    self.l(
                        "輸入 PURGE 以確認",
                        "输入 PURGE 以确认",
                        "Type PURGE to confirm",
                    ),
                    12.0,
                    p.text2,
                ));
                ui.add(
                    egui::TextEdit::singleline(&mut self.purge_text)
                        .hint_text("PURGE")
                        .desired_width(240.0)
                        .margin(Margin::symmetric(10, 7)),
                );
                let can = self.purge_text == "PURGE";
                dialog_footer(ui, &p, |ui| {
                    cancel = button(ui, &p, self.t("cancel"), Btn::Standard).clicked();
                    confirm = ui
                        .add_enabled_ui(can, |ui| button(ui, &p, self.t("purge"), Btn::Danger))
                        .inner
                        .clicked();
                });
            });
            if confirm {
                self.start_purge();
            } else if cancel {
                self.show_purge = false;
            }
        }

        if let Some(report) = self.verify_report.clone() {
            let mut close = false;
            dialog(ctx, &p, "verify", |ui| {
                ui.label(rts(self.t("verify"), 20.0, p.text));
                ui.add_space(8.0);
                let failed = report
                    .checks
                    .iter()
                    .filter(|c| c.status == CheckStatus::Fail)
                    .count();
                if failed == 0 {
                    info_bar(
                        ui,
                        &p,
                        Sev::Ok,
                        self.l(
                            "沒有發現失敗的檢查項目。",
                            "没有发现失败的检查项目。",
                            "No failed checks.",
                        ),
                    );
                } else {
                    let text = match self.lang {
                        Lang::ZhHant => format!("{failed} 項檢查失敗，建議重新套用。"),
                        Lang::ZhHans => format!("{failed} 项检查失败，建议重新应用。"),
                        Lang::En => {
                            format!("{failed} check(s) failed. Re-applying is recommended.")
                        }
                    };
                    info_bar(ui, &p, Sev::Bad, &text);
                }
                ui.add_space(12.0);
                for check in &report.checks {
                    step_line(ui, &p, check_sev(check.status), &check.id, &check.detail);
                }
                dialog_footer(ui, &p, |ui| {
                    close = button(ui, &p, self.t("close"), Btn::Standard).clicked();
                });
            });
            if close {
                self.verify_report = None;
            }
        }
    }

    fn draw_resize_grip(&mut self, ui: &mut egui::Ui) {
        let ctx = ui.ctx().clone();
        let p = self.p;
        let screen = ctx.content_rect();
        egui::Area::new(egui::Id::new("resize_grip"))
            .fixed_pos(screen.right_bottom() - Vec2::new(16.0, 16.0))
            .order(egui::Order::Foreground)
            .show(&ctx, |ui| {
                let (rect, response) = ui.allocate_exact_size(Vec2::new(14.0, 14.0), Sense::drag());
                let response = response.on_hover_cursor(egui::CursorIcon::ResizeNwSe);
                for (dx, dy) in [(3.0, 3.0), (7.0, 3.0), (3.0, 7.0)] {
                    ui.painter().circle_filled(
                        rect.right_bottom() - Vec2::new(dx, dy),
                        1.0,
                        p.text3,
                    );
                }
                if response.dragged() {
                    let delta = ctx.input(|i| i.pointer.delta());
                    let current = ctx.input(|i| {
                        i.viewport()
                            .inner_rect
                            .map(|r| r.size())
                            .unwrap_or(Vec2::new(1080.0, 720.0))
                    });
                    let new_size = Vec2::new(
                        (current.x + delta.x).max(900.0),
                        (current.y + delta.y).max(600.0),
                    );
                    ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(new_size));
                }
            });
    }

    fn draw_toast(&mut self, ctx: &egui::Context) {
        let expired = self
            .toast
            .as_ref()
            .map(|toast| Instant::now() >= toast.until)
            .unwrap_or(true);
        if expired {
            self.toast = None;
            return;
        }
        let p = self.p;
        if let Some(toast) = &self.toast {
            let sev = if toast.error { Sev::Bad } else { Sev::Ok };
            egui::Area::new(egui::Id::new("toast"))
                .anchor(Align2::RIGHT_BOTTOM, Vec2::new(-24.0, -24.0))
                .order(egui::Order::Tooltip)
                .show(ctx, |ui| {
                    Frame::NONE
                        .fill(p.card)
                        .stroke(Stroke::new(1.0, p.control_stroke))
                        .corner_radius(CornerRadius::same(8))
                        .shadow(shadow())
                        .inner_margin(Margin::symmetric(14, 12))
                        .show(ui, |ui| {
                            ui.set_max_width(380.0);
                            ui.horizontal(|ui| {
                                status_badge(ui, &p, sev, 16.0);
                                ui.add_space(4.0);
                                ui.add(egui::Label::new(rt(&toast.text, 13.0, p.text)).wrap());
                            });
                        });
                });
            ctx.request_repaint_after(Duration::from_millis(500));
        }
    }
}

// ---------------------------------------------------------------------------
// Setup
// ---------------------------------------------------------------------------

pub fn run(cli: Cli) -> eframe::Result<()> {
    let mut viewport = egui::ViewportBuilder::default()
        .with_inner_size([1080.0, 720.0])
        .with_min_inner_size([900.0, 600.0])
        .with_decorations(false)
        .with_resizable(true)
        .with_title("One-Click EU Lord");
    if let Ok(icon) = eframe::icon_data::from_png_bytes(include_bytes!("../assets/icon-256.png")) {
        viewport = viewport.with_icon(Arc::new(icon));
    }
    let native_options = eframe::NativeOptions {
        viewport,
        centered: true,
        ..Default::default()
    };
    let cli = cli.clone();
    eframe::run_native(
        "One-Click EU Lord",
        native_options,
        Box::new(move |cc| {
            let app = App::new(cc, &cli);
            Ok(Box::new(app) as Box<dyn eframe::App>)
        }),
    )
}

fn load_icon(ctx: &egui::Context) -> Option<TextureHandle> {
    let data = eframe::icon_data::from_png_bytes(include_bytes!("../assets/icon-256.png")).ok()?;
    let image = egui::ColorImage::from_rgba_unmultiplied(
        [data.width as usize, data.height as usize],
        &data.rgba,
    );
    let options = egui::TextureOptions {
        mipmap_mode: Some(egui::TextureFilter::Linear),
        ..egui::TextureOptions::LINEAR
    };
    Some(ctx.load_texture("app_icon", image, options))
}

fn apply_style(ctx: &egui::Context) {
    ctx.options_mut(|o| o.theme_preference = egui::ThemePreference::System);
    for theme in [Theme::Dark, Theme::Light] {
        let p = Pal::of(theme);
        ctx.style_mut_of(theme, |style| {
            style.visuals = if p.dark {
                egui::Visuals::dark()
            } else {
                egui::Visuals::light()
            };
            let v = &mut style.visuals;
            v.panel_fill = p.bg;
            v.window_fill = p.card;
            v.window_stroke = Stroke::new(1.0, p.control_stroke);
            v.window_corner_radius = CornerRadius::same(8);
            v.menu_corner_radius = CornerRadius::same(8);
            v.window_shadow = shadow();
            v.popup_shadow = shadow();
            v.faint_bg_color = p.card;
            v.extreme_bg_color = p.control;
            v.hyperlink_color = p.accent;
            v.selection.bg_fill = p.accent.gamma_multiply(if p.dark { 0.45 } else { 0.25 });
            v.selection.stroke = Stroke::new(1.0, p.accent);
            for w in [
                &mut v.widgets.noninteractive,
                &mut v.widgets.inactive,
                &mut v.widgets.hovered,
                &mut v.widgets.active,
                &mut v.widgets.open,
            ] {
                w.corner_radius = CornerRadius::same(4);
                w.expansion = 0.0;
            }
            v.widgets.noninteractive.bg_fill = p.card;
            v.widgets.noninteractive.weak_bg_fill = p.card;
            v.widgets.noninteractive.bg_stroke = Stroke::new(1.0, p.stroke);
            v.widgets.noninteractive.fg_stroke = Stroke::new(1.0, p.text);
            v.widgets.inactive.bg_fill = p.control;
            v.widgets.inactive.weak_bg_fill = p.control;
            v.widgets.inactive.bg_stroke = Stroke::new(1.0, p.control_stroke);
            v.widgets.inactive.fg_stroke = Stroke::new(1.0, p.text);
            v.widgets.hovered.bg_fill = p.control_hover;
            v.widgets.hovered.weak_bg_fill = p.control_hover;
            v.widgets.hovered.bg_stroke = Stroke::new(1.0, p.control_stroke);
            v.widgets.hovered.fg_stroke = Stroke::new(1.0, p.text);
            v.widgets.active.bg_fill = p.control_hover;
            v.widgets.active.weak_bg_fill = p.control_hover;
            v.widgets.active.bg_stroke = Stroke::new(1.0, p.accent);
            v.widgets.active.fg_stroke = Stroke::new(1.0, p.text);
            v.widgets.open.bg_fill = p.control_hover;
            v.widgets.open.weak_bg_fill = p.control_hover;
            v.widgets.open.bg_stroke = Stroke::new(1.0, p.control_stroke);
            v.widgets.open.fg_stroke = Stroke::new(1.0, p.text);

            let s = &mut style.spacing;
            s.item_spacing = Vec2::new(8.0, 8.0);
            s.button_padding = Vec2::new(12.0, 6.0);
            s.interact_size = Vec2::new(40.0, 32.0);
            s.scroll = egui::style::ScrollStyle::thin();

            use egui::TextStyle;
            style.text_styles = [
                (TextStyle::Small, FontId::proportional(12.0)),
                (TextStyle::Body, FontId::proportional(14.0)),
                (TextStyle::Button, FontId::proportional(14.0)),
                (TextStyle::Heading, FontId::new(20.0, semibold())),
                (TextStyle::Monospace, FontId::monospace(13.0)),
            ]
            .into();
        });
    }
}

/// Segoe UI (+ Semibold) with Microsoft JhengHei / YaHei fallback for CJK.
/// Missing files are skipped, so the app still runs with egui's bundled fonts.
fn load_fonts(ctx: &egui::Context, lang: Lang) {
    let mut fonts = FontDefinitions::default();
    let defaults = fonts
        .families
        .get(&FontFamily::Proportional)
        .cloned()
        .unwrap_or_default();
    let mut add = |name: &str, path: &str, index: u32| -> bool {
        match std::fs::read(path) {
            Ok(bytes) => {
                let mut data = FontData::from_owned(bytes);
                data.index = index;
                fonts.font_data.insert(name.to_string(), Arc::new(data));
                true
            }
            Err(_) => false,
        }
    };
    let (cjk, cjk_bold) = if lang == Lang::ZhHans {
        (
            [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msjh.ttc"],
            [
                r"C:\Windows\Fonts\msyhbd.ttc",
                r"C:\Windows\Fonts\msjhbd.ttc",
            ],
        )
    } else {
        (
            [r"C:\Windows\Fonts\msjh.ttc", r"C:\Windows\Fonts\msyh.ttc"],
            [
                r"C:\Windows\Fonts\msjhbd.ttc",
                r"C:\Windows\Fonts\msyhbd.ttc",
            ],
        )
    };
    let ui_ok = add("eu_segoe", r"C:\Windows\Fonts\segoeui.ttf", 0);
    let sb_ok = add("eu_segoe_sb", r"C:\Windows\Fonts\seguisb.ttf", 0);
    let icons_ok = add("eu_icons", r"C:\Windows\Fonts\SegoeIcons.ttf", 0)
        || add("eu_icons", r"C:\Windows\Fonts\segmdl2.ttf", 0);
    ICON_FONT.store(icons_ok, std::sync::atomic::Ordering::Relaxed);
    let mono_ok = add("eu_mono", r"C:\Windows\Fonts\CascadiaMono.ttf", 0)
        || add("eu_mono", r"C:\Windows\Fonts\consola.ttf", 0);
    let mut cjk_names = Vec::new();
    for (i, path) in cjk.iter().enumerate() {
        let name = format!("eu_cjk_{i}");
        if add(&name, path, 0) {
            cjk_names.push(name);
        }
    }
    let mut cjk_bold_names = Vec::new();
    for (i, path) in cjk_bold.iter().enumerate() {
        let name = format!("eu_cjk_bold_{i}");
        if add(&name, path, 0) {
            cjk_bold_names.push(name);
        }
    }

    let mut proportional = Vec::new();
    if ui_ok {
        proportional.push("eu_segoe".to_string());
    }
    proportional.extend(cjk_names.iter().cloned());
    proportional.extend(defaults.iter().cloned());

    let mut sb = Vec::new();
    if sb_ok {
        sb.push("eu_segoe_sb".to_string());
    }
    sb.extend(cjk_bold_names);
    sb.extend(proportional.iter().cloned());

    let mut mono = Vec::new();
    if mono_ok {
        mono.push("eu_mono".to_string());
    }
    mono.extend(
        fonts
            .families
            .get(&FontFamily::Monospace)
            .cloned()
            .unwrap_or_default(),
    );
    mono.extend(cjk_names);

    fonts
        .families
        .insert(FontFamily::Proportional, proportional);
    fonts.families.insert(FontFamily::Monospace, mono);
    fonts.families.insert(semibold(), sb);
    let mut icons = Vec::new();
    if icons_ok {
        icons.push("eu_icons".to_string());
    }
    icons.extend(defaults.iter().cloned());
    fonts.families.insert(FontFamily::Name(ICONS.into()), icons);
    ctx.set_fonts(fonts);
}

fn local_state_path() -> Option<PathBuf> {
    std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .map(|root| root.join("OneClickEULord").join("state.json"))
}

fn read_local_state() -> Option<State> {
    let path = local_state_path()?;
    let text = std::fs::read_to_string(path).ok()?;
    State::from_json(&text).ok()
}

fn save_local_state(state: &State) -> std::io::Result<()> {
    let path =
        local_state_path().ok_or_else(|| std::io::Error::other("LOCALAPPDATA is unavailable"))?;
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let text = state
        .to_json()
        .map_err(|err| std::io::Error::other(err.to_string()))?;
    std::fs::write(path, text)
}

// ---------------------------------------------------------------------------
// Fluent-style widgets
// ---------------------------------------------------------------------------

fn shadow() -> egui::Shadow {
    egui::Shadow {
        offset: [0, 8],
        blur: 28,
        spread: 0,
        color: Color32::from_black_alpha(70),
    }
}

fn card(p: &Pal) -> Frame {
    Frame::NONE
        .fill(p.card)
        .stroke(Stroke::new(1.0, p.stroke))
        .corner_radius(CornerRadius::same(8))
        .inner_margin(Margin::same(20))
}

fn page_title(ui: &mut egui::Ui, p: &Pal, title: &str, subtitle: Option<&str>) {
    ui.label(rts(title, 28.0, p.text));
    if let Some(subtitle) = subtitle {
        ui.add_space(2.0);
        ui.add(egui::Label::new(rt(subtitle, 14.0, p.text2)).wrap());
    }
    ui.add_space(20.0);
}

fn section(ui: &mut egui::Ui, p: &Pal, title: &str) {
    ui.add_space(20.0);
    ui.label(rts(title, 14.0, p.text));
    ui.add_space(2.0);
}

/// Stack of settings rows with the tight Windows 11 Settings spacing.
fn group(ui: &mut egui::Ui, add: impl FnOnce(&mut egui::Ui)) {
    ui.scope(|ui| {
        ui.spacing_mut().item_spacing.y = 3.0;
        add(ui);
    });
}

/// One Windows 11 Settings-style row: icon, title, description, control on the right.
fn row(
    ui: &mut egui::Ui,
    p: &Pal,
    icon: Option<Ico>,
    title: &str,
    desc: Option<&str>,
    right_w: f32,
    right: impl FnOnce(&mut egui::Ui),
) {
    Frame::NONE
        .fill(p.card)
        .stroke(Stroke::new(1.0, p.stroke))
        .corner_radius(CornerRadius::same(6))
        .inner_margin(Margin::symmetric(16, 10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                ui.set_min_height(44.0);
                if let Some(icon) = icon.filter(|icon| !icon.glyph().is_empty()) {
                    let (rect, _) = ui.allocate_exact_size(Vec2::new(20.0, 20.0), Sense::hover());
                    ui.painter().text(
                        rect.center(),
                        Align2::CENTER_CENTER,
                        icon.glyph(),
                        icon_font(16.0),
                        p.text,
                    );
                    ui.add_space(12.0);
                }
                let text_w = (ui.available_width() - right_w).max(140.0);
                ui.vertical(|ui| {
                    ui.set_max_width(text_w);
                    ui.spacing_mut().item_spacing.y = 2.0;
                    // Center a lone title against the 44px row (egui aligns the column top).
                    ui.add_space(if desc.is_some() { 3.0 } else { 12.0 });
                    ui.add(egui::Label::new(rt(title, 14.0, p.text)).wrap());
                    if let Some(desc) = desc {
                        ui.add(egui::Label::new(rt(desc, 12.0, p.text2)).wrap());
                    }
                });
                ui.with_layout(Layout::right_to_left(Align::Center), right);
            });
        });
}

fn dot(ui: &mut egui::Ui, color: Color32) {
    let (rect, _) = ui.allocate_exact_size(Vec2::splat(8.0), Sense::hover());
    ui.painter().circle_filled(rect.center(), 4.0, color);
}

/// Right-aligned "● value" pair. Call inside a right-to-left layout.
fn status_value(ui: &mut egui::Ui, p: &Pal, text: &str, sev: Sev) {
    ui.label(rt(text, 14.0, p.text2));
    dot(ui, sev.fg(p));
}

/// Filled circle with a glyph, as used by Fluent InfoBar icons.
/// Check and cross are drawn as strokes so they never depend on font coverage.
fn status_badge(ui: &mut egui::Ui, p: &Pal, sev: Sev, size: f32) {
    let (rect, _) = ui.allocate_exact_size(Vec2::splat(size), Sense::hover());
    let painter = ui.painter();
    let c = rect.center();
    painter.circle_filled(c, size / 2.0, sev.fg(p));
    let k = size / 16.0;
    let stroke = Stroke::new((1.4 * k).max(1.2), p.on_sev());
    match sev {
        Sev::Ok => {
            painter.line_segment(
                [c + Vec2::new(-3.6, 0.2) * k, c + Vec2::new(-1.0, 2.8) * k],
                stroke,
            );
            painter.line_segment(
                [c + Vec2::new(-1.0, 2.8) * k, c + Vec2::new(3.8, -2.4) * k],
                stroke,
            );
        }
        Sev::Bad => {
            painter.line_segment(
                [c + Vec2::splat(-2.8) * k, c + Vec2::splat(2.8) * k],
                stroke,
            );
            painter.line_segment(
                [c + Vec2::new(-2.8, 2.8) * k, c + Vec2::new(2.8, -2.8) * k],
                stroke,
            );
        }
        Sev::Warn | Sev::Info => {
            painter.text(
                c,
                Align2::CENTER_CENTER,
                sev.glyph(),
                FontId::new(size * 0.62, semibold()),
                p.on_sev(),
            );
        }
    }
}

fn info_bar(ui: &mut egui::Ui, p: &Pal, sev: Sev, text: &str) {
    Frame::NONE
        .fill(sev.bg(p))
        .stroke(Stroke::new(1.0, p.stroke))
        .corner_radius(CornerRadius::same(6))
        .inner_margin(Margin::symmetric(14, 10))
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                status_badge(ui, p, sev, 16.0);
                ui.add_space(4.0);
                ui.add(egui::Label::new(rt(text, 13.0, p.text)).wrap());
            });
        });
}

fn stat(ui: &mut egui::Ui, p: &Pal, value: &str, label: &str, sev: Sev) {
    ui.spacing_mut().item_spacing.y = 2.0;
    ui.horizontal(|ui| {
        dot(ui, sev.fg(p));
        ui.label(rts(value, 24.0, p.text));
    });
    ui.add(egui::Label::new(rt(label, 12.0, p.text2)).wrap());
}

fn step_line(ui: &mut egui::Ui, p: &Pal, sev: Option<Sev>, id: &str, detail: &str) {
    ui.horizontal(|ui| {
        match sev {
            Some(sev) => status_badge(ui, p, sev, 14.0),
            None => {
                let (rect, _) = ui.allocate_exact_size(Vec2::splat(14.0), Sense::hover());
                ui.painter()
                    .circle_stroke(rect.center(), 6.0, Stroke::new(1.0, p.text3));
            }
        }
        ui.add_space(4.0);
        if !id.is_empty() {
            ui.label(rt(id, 13.0, p.text));
        }
        ui.add(egui::Label::new(rt(detail, 12.0, p.text2)).wrap());
    });
}

fn button(ui: &mut egui::Ui, p: &Pal, label: &str, kind: Btn) -> egui::Response {
    let enabled = ui.is_enabled();
    let family = if kind == Btn::Standard {
        FontFamily::Proportional
    } else {
        semibold()
    };
    let galley = ui.painter().layout_no_wrap(
        label.to_string(),
        FontId::new(14.0, family),
        Color32::PLACEHOLDER,
    );
    let size = Vec2::new((galley.size().x + 28.0).max(96.0), 32.0);
    let (rect, response) = ui.allocate_exact_size(size, Sense::click());
    if ui.is_rect_visible(rect) {
        let hovered = response.hovered() && enabled;
        let (fill, stroke, text) = match kind {
            _ if !enabled => (p.subtle, Stroke::new(1.0, p.stroke), p.text3),
            Btn::Accent => (
                if hovered { p.accent_hover } else { p.accent },
                Stroke::NONE,
                p.on_accent,
            ),
            Btn::Danger => (
                if hovered { p.danger_hover } else { p.danger },
                Stroke::NONE,
                Color32::WHITE,
            ),
            Btn::Standard => (
                if hovered { p.control_hover } else { p.control },
                Stroke::new(1.0, p.control_stroke),
                p.text,
            ),
        };
        ui.painter().rect(
            rect,
            CornerRadius::same(4),
            fill,
            stroke,
            StrokeKind::Inside,
        );
        ui.painter()
            .galley(rect.center() - galley.size() / 2.0, galley, text);
    }
    if enabled {
        response.on_hover_cursor(egui::CursorIcon::PointingHand)
    } else {
        response
    }
}

fn toggle(ui: &mut egui::Ui, p: &Pal, on: bool) -> egui::Response {
    let (rect, response) = ui.allocate_exact_size(Vec2::new(40.0, 20.0), Sense::click());
    let t = ui.ctx().animate_bool(response.id, on);
    if ui.is_rect_visible(rect) {
        let hovered = response.hovered();
        let painter = ui.painter();
        if on {
            painter.rect(
                rect,
                CornerRadius::same(10),
                if hovered { p.accent_hover } else { p.accent },
                Stroke::NONE,
                StrokeKind::Inside,
            );
        } else {
            painter.rect(
                rect,
                CornerRadius::same(10),
                if hovered { p.subtle2 } else { p.subtle },
                Stroke::new(1.0, p.text2),
                StrokeKind::Inside,
            );
        }
        let x = egui::lerp((rect.left() + 10.0)..=(rect.right() - 10.0), t);
        let radius = if hovered { 6.0 } else { 5.0 };
        painter.circle_filled(
            Pos2::new(x, rect.center().y),
            radius,
            if on { p.on_accent } else { p.text2 },
        );
    }
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

fn segmented<T: Copy + PartialEq>(
    ui: &mut egui::Ui,
    p: &Pal,
    items: &[(&str, T)],
    current: T,
) -> Option<T> {
    let mut picked = None;
    Frame::NONE
        .fill(p.subtle)
        .corner_radius(CornerRadius::same(6))
        .inner_margin(Margin::same(2))
        .show(ui, |ui| {
            ui.spacing_mut().item_spacing.x = 2.0;
            // `horizontal` inherits a right-to-left parent; keep the visual order stable.
            let rtl = ui.layout().main_dir() == egui::Direction::RightToLeft;
            ui.horizontal(|ui| {
                let ordered: Vec<&(&str, T)> = if rtl {
                    items.iter().rev().collect()
                } else {
                    items.iter().collect()
                };
                for (label, value) in ordered {
                    let selected = *value == current;
                    let family = if selected {
                        semibold()
                    } else {
                        FontFamily::Proportional
                    };
                    let galley = ui.painter().layout_no_wrap(
                        label.to_string(),
                        FontId::new(13.0, family),
                        Color32::PLACEHOLDER,
                    );
                    let size = Vec2::new((galley.size().x + 20.0).max(48.0), 26.0);
                    let (rect, response) = ui.allocate_exact_size(size, Sense::click());
                    let painter = ui.painter();
                    if selected {
                        painter.rect(
                            rect,
                            CornerRadius::same(4),
                            p.seg_selected,
                            Stroke::new(1.0, p.control_stroke),
                            StrokeKind::Inside,
                        );
                        let pill = Rect::from_center_size(
                            Pos2::new(rect.center().x, rect.bottom() - 3.0),
                            Vec2::new(16.0, 3.0),
                        );
                        painter.rect_filled(pill, CornerRadius::same(2), p.accent);
                    } else if response.hovered() {
                        painter.rect_filled(rect, CornerRadius::same(4), p.subtle);
                    }
                    painter.galley(
                        rect.center() - galley.size() / 2.0 - Vec2::new(0.0, 1.0),
                        galley,
                        if selected { p.text } else { p.text2 },
                    );
                    if response
                        .on_hover_cursor(egui::CursorIcon::PointingHand)
                        .clicked()
                        && !selected
                    {
                        picked = Some(*value);
                    }
                }
            });
        });
    picked
}

fn nav_item(ui: &mut egui::Ui, p: &Pal, icon: Ico, label: &str, selected: bool) -> egui::Response {
    let (rect, response) =
        ui.allocate_exact_size(Vec2::new(ui.available_width(), 36.0), Sense::click());
    if ui.is_rect_visible(rect) {
        let painter = ui.painter();
        let fill = if selected {
            p.subtle2
        } else if response.hovered() {
            p.subtle
        } else {
            Color32::TRANSPARENT
        };
        painter.rect_filled(rect, CornerRadius::same(4), fill);
        if selected {
            let pill = Rect::from_center_size(
                Pos2::new(rect.left() + 1.5, rect.center().y),
                Vec2::new(3.0, 16.0),
            );
            painter.rect_filled(pill, CornerRadius::same(2), p.accent);
        }
        painter.text(
            Pos2::new(rect.left() + 22.0, rect.center().y),
            Align2::CENTER_CENTER,
            icon.glyph(),
            icon_font(16.0),
            p.text,
        );
        painter.text(
            Pos2::new(rect.left() + 44.0, rect.center().y),
            Align2::LEFT_CENTER,
            label,
            FontId::new(
                14.0,
                if selected {
                    semibold()
                } else {
                    FontFamily::Proportional
                },
            ),
            p.text,
        );
    }
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

fn caption_button(ui: &mut egui::Ui, p: &Pal, kind: Caption) -> egui::Response {
    let (rect, response) = ui.allocate_exact_size(Vec2::new(46.0, TITLEBAR_H), Sense::click());
    let hovered = response.hovered();
    let close = kind == Caption::Close;
    let painter = ui.painter();
    let fill = match (hovered, close) {
        (true, true) => p.danger,
        (true, false) => p.subtle,
        _ => Color32::TRANSPARENT,
    };
    painter.rect_filled(rect, CornerRadius::ZERO, fill);
    let color = if hovered && close {
        Color32::WHITE
    } else {
        p.text
    };
    let stroke = Stroke::new(1.0, color);
    let c = rect.center();
    match kind {
        Caption::Minimize => {
            painter.line_segment([c - Vec2::new(5.0, 0.0), c + Vec2::new(5.0, 0.0)], stroke);
        }
        Caption::Maximize => {
            painter.rect_stroke(
                Rect::from_center_size(c, Vec2::splat(10.0)),
                CornerRadius::same(1),
                stroke,
                StrokeKind::Inside,
            );
        }
        Caption::Restore => {
            painter.rect_stroke(
                Rect::from_min_size(c + Vec2::new(-3.0, -5.0), Vec2::splat(8.0)),
                CornerRadius::same(1),
                stroke,
                StrokeKind::Inside,
            );
            painter.rect(
                Rect::from_min_size(c + Vec2::new(-5.0, -3.0), Vec2::splat(8.0)),
                CornerRadius::same(1),
                if hovered { p.bg.blend(fill) } else { p.bg },
                stroke,
                StrokeKind::Inside,
            );
        }
        Caption::Close => {
            painter.line_segment([c - Vec2::splat(5.0), c + Vec2::splat(5.0)], stroke);
            painter.line_segment([c + Vec2::new(-5.0, 5.0), c + Vec2::new(5.0, -5.0)], stroke);
        }
    }
    response
}

fn dialog(ctx: &egui::Context, p: &Pal, id: &str, add: impl FnOnce(&mut egui::Ui)) {
    egui::Modal::new(egui::Id::new(id))
        .frame(
            Frame::NONE
                .fill(p.card)
                .stroke(Stroke::new(1.0, p.control_stroke))
                .corner_radius(CornerRadius::same(8))
                .shadow(shadow())
                .inner_margin(Margin::same(24)),
        )
        .show(ctx, |ui| {
            let width = (ctx.content_rect().width() - 96.0).clamp(380.0, 560.0);
            ui.set_width(width);
            ui.set_max_width(width);
            add(ui);
        });
}

/// Right-aligned dialog buttons. Add the rightmost button first.
fn dialog_footer(ui: &mut egui::Ui, _p: &Pal, add: impl FnOnce(&mut egui::Ui)) {
    ui.add_space(24.0);
    ui.with_layout(Layout::right_to_left(Align::Center), add);
}

// ---------------------------------------------------------------------------
// Small mappers
// ---------------------------------------------------------------------------

fn policy_description(decision: &PolicyDecision, lang: Lang) -> String {
    decision
        .id
        .as_deref()
        .and_then(catalog::by_id)
        .map(|entry| entry.describe(lang).to_string())
        .unwrap_or_else(|| format!("{} · {}", tr(lang, "unclassified"), decision.guid))
}

/// Localized policy state ("enabled" / "disabled"); unknown values pass through.
fn state_label(lang: Lang, value: &str) -> String {
    let label = match (value, lang) {
        ("enabled", Lang::ZhHant) => "開啟",
        ("enabled", Lang::ZhHans) => "开启",
        ("enabled", Lang::En) => "On",
        ("disabled", Lang::ZhHant) => "關閉",
        ("disabled", Lang::ZhHans) => "关闭",
        ("disabled", Lang::En) => "Off",
        _ => value,
    };
    label.to_string()
}

fn task_label(status: TaskStatus, lang: Lang) -> &'static str {
    match status {
        TaskStatus::Ready => tr(lang, "status_task_ready"),
        TaskStatus::Absent => tr(lang, "status_task_absent"),
        TaskStatus::Disabled => tr(lang, "status_task_disabled"),
        TaskStatus::Stale => tr(lang, "status_task_stale"),
        TaskStatus::Unknown => tr(lang, "status_task_unknown"),
    }
}

fn class_color(p: &Pal, class: Option<PolicyClass>) -> Color32 {
    match class {
        Some(PolicyClass::Benefit) => p.ok,
        Some(PolicyClass::Loss) => p.warn,
        Some(PolicyClass::Choice) => p.accent,
        None => p.text3,
    }
}

fn step_sev(state: StepState) -> Option<Sev> {
    match state {
        StepState::Pass => Some(Sev::Ok),
        StepState::Warn => Some(Sev::Warn),
        StepState::Fail => Some(Sev::Bad),
        StepState::Running => Some(Sev::Info),
        StepState::Pending => None,
    }
}

fn check_sev(status: CheckStatus) -> Option<Sev> {
    match status {
        CheckStatus::Pass => Some(Sev::Ok),
        CheckStatus::Pending => Some(Sev::Warn),
        CheckStatus::Unknown => None,
        CheckStatus::Fail => Some(Sev::Bad),
    }
}

fn sev_from_str(status: &str) -> Option<Sev> {
    let lower = status.to_ascii_lowercase();
    if lower.contains("pass") || lower.contains("ok") || lower.contains("success") {
        Some(Sev::Ok)
    } else if lower.contains("fail") || lower.contains("error") {
        Some(Sev::Bad)
    } else if lower.contains("warn") {
        Some(Sev::Warn)
    } else {
        None
    }
}
