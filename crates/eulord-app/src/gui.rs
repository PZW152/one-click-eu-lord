#![allow(deprecated)]
use std::path::PathBuf;
use std::sync::Arc;
use std::sync::mpsc::{self, Receiver};
use std::time::{Duration, Instant};

use eframe::egui::{
    self, Align, Align2, Color32, CornerRadius, FontData, FontDefinitions, FontFamily, FontId,
    Frame, Layout, Margin, Pos2, RichText, Sense, Stroke, Vec2,
};
use eulord_core::catalog::{self, PolicyClass};
use eulord_core::compat::{CompatLevel, OsInfo};
use eulord_core::i18n::{Lang, LangPref, tr};
use eulord_core::plan::{Plan, PolicyDecision, PolicyOverride};
use eulord_core::state::State;
use eulord_windows::{CheckStatus, PolicyOrigin, TaskStatus, VerifyReport, WindowsSystem};

use crate::cli::Cli;
use crate::engine::{self, ApplyOutcome, ApplyReport, StepState, StepUpdate};

const BG: Color32 = Color32::from_rgb(4, 10, 8);
const BG_ALT: Color32 = Color32::from_rgb(7, 16, 12);
const PANEL: Color32 = Color32::from_rgb(9, 22, 17);
const PANEL_HI: Color32 = Color32::from_rgb(13, 32, 24);
const BORDER: Color32 = Color32::from_rgb(28, 70, 52);
const BORDER_HOT: Color32 = Color32::from_rgb(65, 143, 103);
const INK: Color32 = Color32::from_rgb(214, 255, 226);
const MUTED: Color32 = Color32::from_rgb(122, 168, 140);
const DIM: Color32 = Color32::from_rgb(70, 106, 84);
const AMBER: Color32 = Color32::from_rgb(255, 180, 84);
const CYAN: Color32 = Color32::from_rgb(101, 227, 195);
const GREEN: Color32 = Color32::from_rgb(126, 240, 154);
const RED: Color32 = Color32::from_rgb(255, 107, 94);
const WARN: Color32 = Color32::from_rgb(248, 193, 92);
const MONO: &str = "eulord_mono";

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
    fn index(self) -> usize {
        match self {
            Self::Overview => 1,
            Self::Policies => 2,
            Self::Unlocks => 3,
            Self::Maintenance => 4,
            Self::About => 5,
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
    crt: bool,
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
        let crt = state.ui.crt_scanlines;
        let show_welcome = !state.ui.first_run_done;
        let snapshot = Snapshot::load(&system, &state);
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
            crt,
        };
        app.apply_style(&cc.egui_ctx);
        app.load_fonts(&cc.egui_ctx);
        if let Some(err) = &app.snapshot.plan_error {
            app.set_toast(format!("plan: {err}"), true);
        }
        app
    }

    fn t(&self, key: &'static str) -> &'static str {
        tr(self.lang, key)
    }

    fn apply_style(&self, ctx: &egui::Context) {
        ctx.all_styles_mut(|style| {
            style.visuals = egui::Visuals::dark();
            style.visuals.panel_fill = BG;
            style.visuals.window_fill = PANEL;
            style.visuals.faint_bg_color = PANEL;
            style.visuals.extreme_bg_color = BG;
            style.visuals.override_text_color = Some(INK);
            style.visuals.window_stroke = Stroke::new(1.0, BORDER);
            style.visuals.hyperlink_color = AMBER;
            style.visuals.selection.bg_fill = AMBER;
            style.visuals.selection.stroke = Stroke::new(1.0, BG);
            for widget in [
                &mut style.visuals.widgets.noninteractive,
                &mut style.visuals.widgets.inactive,
                &mut style.visuals.widgets.hovered,
                &mut style.visuals.widgets.active,
                &mut style.visuals.widgets.open,
            ] {
                widget.corner_radius = CornerRadius::ZERO;
            }
            style.visuals.widgets.noninteractive.bg_fill = PANEL;
            style.visuals.widgets.noninteractive.bg_stroke = Stroke::new(1.0, BORDER);
            style.visuals.widgets.inactive.bg_fill = PANEL_HI;
            style.visuals.widgets.inactive.weak_bg_fill = PANEL_HI;
            style.visuals.widgets.inactive.bg_stroke = Stroke::new(1.0, BORDER);
            style.visuals.widgets.hovered.bg_fill = Color32::from_rgb(20, 46, 34);
            style.visuals.widgets.hovered.bg_stroke = Stroke::new(1.0, BORDER_HOT);
            style.visuals.widgets.active.bg_fill = Color32::from_rgb(32, 66, 48);
            style.visuals.widgets.active.bg_stroke = Stroke::new(1.0, AMBER);
            style.spacing.item_spacing = Vec2::new(8.0, 8.0);
            style.spacing.button_padding = Vec2::new(10.0, 6.0);
            style.spacing.interact_size = Vec2::new(36.0, 28.0);
        });
    }

    fn load_fonts(&self, ctx: &egui::Context) {
        let mut fonts = FontDefinitions::default();
        if let Ok(bytes) = std::fs::read(r"C:\Windows\Fonts\consola.ttf") {
            fonts
                .font_data
                .insert(MONO.to_string(), Arc::new(FontData::from_owned(bytes)));
            for family in [FontFamily::Proportional, FontFamily::Monospace] {
                if let Some(list) = fonts.families.get_mut(&family) {
                    list.insert(0, MONO.to_string());
                }
            }
        }
        let cjk = if self.lang == Lang::ZhHant {
            [r"C:\Windows\Fonts\msjh.ttc", r"C:\Windows\Fonts\msyh.ttc"]
        } else {
            [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msjh.ttc"]
        };
        for (index, path) in cjk.into_iter().enumerate() {
            if let Ok(bytes) = std::fs::read(path) {
                let name = format!("eulord_cjk_{index}");
                let mut data = FontData::from_owned(bytes);
                data.index = 0;
                fonts.font_data.insert(name.clone(), Arc::new(data));
                for family in [FontFamily::Proportional, FontFamily::Monospace] {
                    if let Some(list) = fonts.families.get_mut(&family) {
                        list.push(name.clone());
                    }
                }
            }
        }
        ctx.set_fonts(fonts);
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
        self.state.ui.crt_scanlines = self.crt;
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

    fn set_language(&mut self, pref: LangPref) {
        self.lang_pref = pref;
        self.lang = pref.resolve(&self.locale);
        self.persist_state();
    }

    fn set_override(&mut self, id: &str, value: PolicyOverride) {
        self.state.set_override(id, value);
        self.refresh_plan();
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
            let text = format!("{:?}: {}", report.outcome, report.message);
            self.last_report = Some(report);
            self.refresh();
            self.set_toast(text, error);
        } else if self.busy.is_some() {
            ctx.request_repaint_after(Duration::from_millis(120));
        }
    }

    fn draw_titlebar(&mut self, ui: &mut egui::Ui) {
        let ctx = ui.ctx().clone();
        egui::Panel::top("titlebar")
            .exact_size(40.0)
            .frame(
                Frame::NONE
                    .fill(BG_ALT)
                    .inner_margin(Margin::symmetric(12, 0))
                    .stroke(Stroke::new(1.0, BORDER)),
            )
            .show(ui, |ui| {
                ui.horizontal_centered(|ui| {
                    ui.label(RichText::new("▣").monospace().size(16.0).color(AMBER));
                    ui.label(
                        RichText::new("ONE-CLICK EU LORD")
                            .monospace()
                            .size(12.0)
                            .strong()
                            .color(INK),
                    );
                    ui.label(
                        RichText::new(format!("v{}", eulord_core::APP_VERSION))
                            .monospace()
                            .size(9.0)
                            .color(DIM),
                    );
                    let drag_w = (ui.ctx().content_rect().width() - 520.0).max(40.0);
                    let (rect, response) =
                        ui.allocate_exact_size(Vec2::new(drag_w, 38.0), Sense::click_and_drag());
                    let status = match self.snapshot.origin {
                        PolicyOrigin::Ours => "MATRIX ENGAGED",
                        PolicyOrigin::WindowsOriginal => "WINDOWS BASELINE",
                        _ => "MATRIX UNKNOWN",
                    };
                    ui.painter().text(
                        rect.left_center() + Vec2::new(10.0, 0.0),
                        Align2::LEFT_CENTER,
                        status,
                        FontId::monospace(9.0),
                        if self.snapshot.origin == PolicyOrigin::Ours {
                            GREEN
                        } else {
                            MUTED
                        },
                    );
                    if response.drag_started() {
                        ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
                    }
                    if response.double_clicked() {
                        let maximized = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
                        ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maximized));
                    }
                    for pref in [
                        LangPref::Auto,
                        LangPref::ZhHant,
                        LangPref::ZhHans,
                        LangPref::En,
                    ] {
                        let selected = self.lang_pref == pref;
                        if ui
                            .add(
                                egui::Button::new(
                                    RichText::new(pref.label())
                                        .monospace()
                                        .size(9.0)
                                        .color(if selected { AMBER } else { MUTED }),
                                )
                                .frame(false)
                                .corner_radius(0),
                            )
                            .clicked()
                        {
                            self.set_language(pref);
                        }
                    }
                    ui.add_space(8.0);
                    if ui
                        .add(
                            egui::Button::new(RichText::new("—").monospace().size(15.0).color(INK))
                                .frame(false)
                                .corner_radius(0)
                                .min_size(Vec2::new(28.0, 26.0)),
                        )
                        .clicked()
                    {
                        ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(true));
                    }
                    let maximized = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
                    if ui
                        .add(
                            egui::Button::new(
                                RichText::new(if maximized { "❐" } else { "□" })
                                    .monospace()
                                    .size(14.0)
                                    .color(INK),
                            )
                            .frame(false)
                            .corner_radius(0)
                            .min_size(Vec2::new(28.0, 26.0)),
                        )
                        .clicked()
                    {
                        ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maximized));
                    }
                    if ui
                        .add(
                            egui::Button::new(RichText::new("×").monospace().size(17.0).color(RED))
                                .frame(false)
                                .corner_radius(0)
                                .min_size(Vec2::new(28.0, 26.0)),
                        )
                        .clicked()
                    {
                        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                    }
                });
            });
    }

    fn draw_sidebar(&mut self, ui: &mut egui::Ui) {
        egui::Panel::left("sidebar")
            .exact_size(200.0)
            .resizable(false)
            .frame(
                Frame::NONE
                    .fill(BG_ALT)
                    .inner_margin(Margin::symmetric(12, 14))
                    .stroke(Stroke::new(1.0, BORDER)),
            )
            .show(ui, |ui| {
                ui.label(
                    RichText::new("// EU LORD")
                        .monospace()
                        .size(11.0)
                        .color(AMBER),
                );
                ui.label(
                    RichText::new("SOVEREIGNTY SUBSYSTEM")
                        .monospace()
                        .size(8.0)
                        .color(DIM),
                );
                ui.add_space(16.0);
                for page in Page::ALL {
                    let selected = self.page == page;
                    let text = format!("[0{}]  {}", page.index(), self.t(page.title_key()));
                    let button = egui::Button::new(
                        RichText::new(text)
                            .monospace()
                            .size(12.0)
                            .color(if selected { AMBER } else { INK }),
                    )
                    .fill(if selected {
                        PANEL_HI
                    } else {
                        Color32::TRANSPARENT
                    })
                    .stroke(Stroke::new(
                        1.0,
                        if selected {
                            BORDER_HOT
                        } else {
                            Color32::TRANSPARENT
                        },
                    ))
                    .frame(true)
                    .corner_radius(0)
                    .min_size(Vec2::new(174.0, 34.0));
                    if ui.add(button).clicked() {
                        self.page = page;
                        self.persist_state();
                    }
                }
                let remaining = (ui.available_height() - 112.0).max(0.0);
                ui.add_space(remaining);
                ui.separator();
                let admin_color = if self.snapshot.elevated { GREEN } else { WARN };
                ui.label(
                    RichText::new(format!(
                        "● {}",
                        if self.snapshot.elevated {
                            self.t("status_elevated")
                        } else {
                            self.t("status_standard")
                        }
                    ))
                    .monospace()
                    .size(9.0)
                    .color(admin_color),
                );
                ui.label(
                    RichText::new(format!("OS  {}", self.snapshot.os.build_label()))
                        .monospace()
                        .size(9.0)
                        .color(MUTED),
                );
                ui.label(
                    RichText::new(format!("LANG  {}", self.lang.short()))
                        .monospace()
                        .size(9.0)
                        .color(MUTED),
                );
                ui.label(
                    RichText::new(format!("BUILD {}", eulord_core::APP_VERSION))
                        .monospace()
                        .size(9.0)
                        .color(DIM),
                );
                ui.add_space(6.0);
                let crt_label = if self.crt { "[CRT ON]" } else { "[CRT OFF]" };
                if ui
                    .add(
                        egui::Button::new(
                            RichText::new(crt_label).monospace().size(9.0).color(MUTED),
                        )
                        .frame(false)
                        .corner_radius(0),
                    )
                    .clicked()
                {
                    self.crt = !self.crt;
                    self.persist_state();
                }
            });
    }
}
impl eframe::App for App {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
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
        self.draw_crt(ui);
        self.draw_modals(&ctx);
        self.draw_toast(&ctx);
        if self.busy.is_some() {
            ctx.request_repaint_after(Duration::from_millis(120));
        }
    }
}

impl App {
    fn draw_main(&mut self, ui: &mut egui::Ui) {
        egui::CentralPanel::default()
            .frame(Frame::NONE.fill(BG).inner_margin(Margin::symmetric(22, 16)))
            .show(ui, |ui| {
                let content_width = (ui.ctx().content_rect().width() - 244.0).max(260.0);
                egui::ScrollArea::vertical()
                    .auto_shrink([false, false])
                    .show(ui, |ui| {
                        ui.set_width(content_width);
                        ui.set_max_width(content_width);
                        match self.page {
                            Page::Overview => self.page_overview(ui),
                            Page::Policies => self.page_policies(ui),
                            Page::Unlocks => self.page_unlocks(ui),
                            Page::Maintenance => self.page_maintenance(ui),
                            Page::About => self.page_about(ui),
                        }
                        ui.add_space(28.0);
                        ui.separator();
                        ui.label(
                            RichText::new(self.t("footer_hint"))
                                .monospace()
                                .size(9.0)
                                .color(DIM),
                        );
                    });
            });
    }

    fn page_overview(&mut self, ui: &mut egui::Ui) {
        page_header(ui, "OVERVIEW // 01", "SYSTEM STATE / EU + EEA SUPERSET");
        let (status_text, status_color) = match self.snapshot.origin {
            PolicyOrigin::Ours if self.snapshot.task == TaskStatus::Ready => {
                (self.t("status_active"), GREEN)
            }
            PolicyOrigin::Ours => (self.t("status_partial"), WARN),
            PolicyOrigin::WindowsOriginal if self.state.ever_applied() => {
                (self.t("status_partial"), WARN)
            }
            PolicyOrigin::WindowsOriginal => (self.t("status_not_configured"), MUTED),
            _ => (self.t("status_unknown"), RED),
        };
        ui.horizontal(|ui| {
            ui.label(
                RichText::new("●")
                    .monospace()
                    .size(12.0)
                    .color(status_color),
            );
            ui.label(
                RichText::new(status_text)
                    .monospace()
                    .size(16.0)
                    .strong()
                    .color(INK),
            );
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                ui.label(
                    RichText::new(self.snapshot.os.full_label())
                        .monospace()
                        .size(9.0)
                        .color(MUTED),
                );
            });
        });
        ui.add_space(12.0);
        ui.columns(2, |cols| {
            status_card(
                &mut cols[0],
                self.t("status_region"),
                &self
                    .snapshot
                    .region
                    .map(|v| v.to_string())
                    .unwrap_or_else(|| "--".to_string())
                    .to_string(),
                &format!(
                    "target {} / {}",
                    self.state.region_iso, self.state.region_geoid
                ),
                if self.snapshot.region == Some(self.state.region_geoid) {
                    GREEN
                } else {
                    WARN
                },
            );
            status_card(
                &mut cols[1],
                self.t("status_policy"),
                match self.snapshot.origin {
                    PolicyOrigin::Ours => self.t("status_ours"),
                    PolicyOrigin::WindowsOriginal => self.t("status_original"),
                    PolicyOrigin::Missing => self.t("status_missing"),
                    PolicyOrigin::Unreadable => self.t("status_unreadable"),
                },
                &format!(
                    "{} catalogued / {} unknown untouched",
                    catalog::all().len(),
                    self.snapshot
                        .plan
                        .as_ref()
                        .map(|p| p.unclassified.len())
                        .unwrap_or(0)
                ),
                if self.snapshot.origin == PolicyOrigin::Ours {
                    GREEN
                } else {
                    MUTED
                },
            );
        });
        ui.add_space(10.0);
        ui.columns(2, |cols| {
            status_card(
                &mut cols[0],
                self.t("status_persistence"),
                task_label(self.snapshot.task, self.lang),
                "logon + daily + Windows Update",
                if self.snapshot.task == TaskStatus::Ready {
                    GREEN
                } else {
                    WARN
                },
            );
            status_card(
                &mut cols[1],
                self.t("status_integrity"),
                if self.snapshot.backups {
                    self.t("backup_ready")
                } else {
                    self.t("backup_none")
                },
                "atomic policy replace + rollback journal",
                if self.snapshot.backups { GREEN } else { MUTED },
            );
        });
        ui.add_space(14.0);
        let plan = self.snapshot.plan.clone();
        Frame::NONE
            .fill(PANEL_HI)
            .stroke(Stroke::new(1.0, BORDER_HOT))
            .inner_margin(Margin::same(16))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.vertical(|ui| {
                        ui.label(
                            RichText::new("SUPERSET // EXECUTION")
                                .monospace()
                                .size(10.0)
                                .color(AMBER),
                        );
                        ui.label(
                            RichText::new(if status_color == GREEN {
                                "MATRIX IS ENGAGED"
                            } else {
                                "READY TO ENGAGE"
                            })
                            .monospace()
                            .size(21.0)
                            .strong()
                            .color(INK),
                        );
                    });
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        let button = primary_button(self.t("apply_superset"));
                        if ui
                            .add_enabled(plan.is_some() && self.busy.is_none(), button)
                            .clicked()
                        {
                            if self.state.ever_applied() {
                                self.start_apply();
                            } else {
                                self.show_review = true;
                            }
                        }
                    });
                });
                ui.add_space(8.0);
                if let Some(plan) = &plan {
                    ui.label(
                        RichText::new(format!(
                            "{} gains / {} possible  ·  {} kept  ·  {} changed",
                            plan.gains(),
                            plan.gains_possible(),
                            plan.losses_refused,
                            plan.changed_policies
                        ))
                        .monospace()
                        .size(11.0)
                        .color(MUTED),
                    );
                    let total = plan.benefits_total.max(1);
                    let ratio = plan.benefits_taken as f32 / total as f32;
                    ui.add(
                        egui::ProgressBar::new(ratio)
                            .desired_height(6.0)
                            .fill(GREEN),
                    );
                } else {
                    ui.label(
                        RichText::new(
                            self.snapshot
                                .plan_error
                                .clone()
                                .unwrap_or_else(|| self.t("plan_failed").to_string()),
                        )
                        .monospace()
                        .size(10.0)
                        .color(RED),
                    );
                }
                ui.add_space(6.0);
                ui.label(
                    RichText::new(self.t("apply_no_uninstall"))
                        .monospace()
                        .size(9.0)
                        .color(WARN),
                );
                if !self.snapshot.elevated {
                    ui.label(
                        RichText::new(self.t("not_admin_hint"))
                            .monospace()
                            .size(9.0)
                            .color(MUTED),
                    );
                }
                if self.busy.is_some() {
                    ui.add_space(8.0);
                    ui.add(
                        egui::ProgressBar::new(self.progress)
                            .desired_height(8.0)
                            .fill(AMBER),
                    );
                    for step in self
                        .steps
                        .iter()
                        .rev()
                        .take(4)
                        .collect::<Vec<_>>()
                        .into_iter()
                        .rev()
                    {
                        ui.label(
                            RichText::new(format!(
                                "{:?}  {}  {}",
                                step.state, step.id, step.detail
                            ))
                            .monospace()
                            .size(9.0)
                            .color(step_color(step.state)),
                        );
                    }
                }
            });
        ui.add_space(14.0);
        ui.label(
            RichText::new("TRANSMISSION LOG // LAST OPERATIONS")
                .monospace()
                .size(10.0)
                .color(AMBER),
        );
        ui.add_space(6.0);
        Frame::NONE
            .fill(PANEL)
            .stroke(Stroke::new(1.0, BORDER))
            .inner_margin(Margin::same(12))
            .show(ui, |ui| {
                let mut lines = 0;
                if let Some(report) = &self.last_report {
                    for step in report
                        .steps
                        .iter()
                        .rev()
                        .take(6)
                        .collect::<Vec<_>>()
                        .into_iter()
                        .rev()
                    {
                        log_line(
                            ui,
                            &format!("{:?}", step.state),
                            &step.detail,
                            step_color(step.state),
                        );
                        lines += 1;
                    }
                } else if let Some(record) = &self.state.last_apply {
                    for step in record
                        .steps
                        .iter()
                        .rev()
                        .take(6)
                        .collect::<Vec<_>>()
                        .into_iter()
                        .rev()
                    {
                        log_line(ui, &step.status, &step.detail, DIM);
                        lines += 1;
                    }
                }
                if lines == 0 {
                    log_line(ui, "IDLE", "no operation recorded in this session", DIM);
                }
            });
    }
    fn page_policies(&mut self, ui: &mut egui::Ui) {
        page_header(
            ui,
            "POLICY MATRIX // 02",
            "68 ENTRIES / SUPERSET RULE + MANUAL OVERRIDE",
        );
        ui.horizontal_wrapped(|ui| {
            let search_hint = self.t("search_policies");
            ui.add(
                egui::TextEdit::singleline(&mut self.search)
                    .hint_text(search_hint)
                    .desired_width(320.0),
            );
            for (filter, key) in [
                (Filter::All, "filter_all"),
                (Filter::Benefits, "filter_benefits"),
                (Filter::Losses, "filter_losses"),
                (Filter::Choices, "filter_choices"),
            ] {
                if ui
                    .selectable_label(
                        self.filter == filter,
                        RichText::new(self.t(key))
                            .monospace()
                            .size(10.0)
                            .color(if self.filter == filter { BG } else { MUTED }),
                    )
                    .clicked()
                {
                    self.filter = filter;
                }
            }
            let expert_label = self.t("expert_mode");
            if ui
                .checkbox(
                    &mut self.expert,
                    RichText::new(expert_label)
                        .monospace()
                        .size(10.0)
                        .color(MUTED),
                )
                .changed()
            {
                self.persist_state();
            }
        });
        ui.label(
            RichText::new(if self.expert {
                self.t("expert_hint")
            } else {
                self.t("simple_hint")
            })
            .monospace()
            .size(9.0)
            .color(MUTED),
        );
        ui.add_space(8.0);
        let Some(plan) = self.snapshot.plan.clone() else {
            ui.label(
                RichText::new(
                    self.snapshot
                        .plan_error
                        .clone()
                        .unwrap_or_else(|| self.t("plan_failed").to_string()),
                )
                .monospace()
                .size(11.0)
                .color(RED),
            );
            return;
        };
        if !self.expert {
            ui.label(
                RichText::new(format!(
                    "AUTO: {} gains taken · {} losses kept · {} taste choices available",
                    plan.benefits_taken, plan.losses_refused, plan.choices_present
                ))
                .monospace()
                .size(10.0)
                .color(MUTED),
            );
            ui.add_space(6.0);
        }
        let query = self.search.to_lowercase();
        let mut rows: Vec<PolicyDecision> = plan
            .decisions
            .into_iter()
            .filter(|decision| {
                if !self.expert && decision.class != Some(PolicyClass::Choice) {
                    return false;
                }
                match self.filter {
                    Filter::All => {}
                    Filter::Benefits => {
                        if decision.class != Some(PolicyClass::Benefit) {
                            return false;
                        }
                    }
                    Filter::Losses => {
                        if decision.class != Some(PolicyClass::Loss) {
                            return false;
                        }
                    }
                    Filter::Choices => {
                        if decision.class != Some(PolicyClass::Choice) {
                            return false;
                        }
                    }
                }
                if query.is_empty() {
                    return true;
                }
                let description = policy_description(decision, self.lang);
                description.to_lowercase().contains(&query)
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
        for decision in rows {
            let mut action: Option<(String, PolicyOverride)> = None;
            Frame::NONE
                .fill(PANEL)
                .stroke(Stroke::new(1.0, BORDER))
                .inner_margin(Margin::same(10))
                .show(ui, |ui| {
                    ui.horizontal(|ui| {
                        let class = decision.class;
                        let color = class_color(class);
                        ui.label(
                            RichText::new(format!(
                                "[{}]",
                                class.map(|c| c.short()).unwrap_or("UNKNOWN")
                            ))
                            .monospace()
                            .size(9.0)
                            .color(color),
                        );
                        ui.vertical(|ui| {
                            ui.label(
                                RichText::new(policy_description(&decision, self.lang))
                                    .monospace()
                                    .size(12.0)
                                    .color(INK),
                            );
                            ui.label(
                                RichText::new(format!(
                                    "{}  //  {} → {}",
                                    decision.id.as_deref().unwrap_or(&decision.guid),
                                    decision.global,
                                    decision.target
                                ))
                                .monospace()
                                .size(9.0)
                                .color(MUTED),
                            );
                        });
                        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                            if let Some(id) = &decision.id {
                                if decision.class == Some(PolicyClass::Choice) {
                                    let mut checked =
                                        self.state.override_for(id) == PolicyOverride::Eea;
                                    if ui
                                        .checkbox(&mut checked, "")
                                        .on_hover_text(self.t("choices"))
                                        .changed()
                                    {
                                        action = Some((
                                            id.clone(),
                                            if checked {
                                                PolicyOverride::Eea
                                            } else {
                                                PolicyOverride::Auto
                                            },
                                        ));
                                    }
                                }
                                if self.expert {
                                    for (label, value) in [
                                        ("AUTO", PolicyOverride::Auto),
                                        ("EU", PolicyOverride::Eea),
                                        ("GLOBAL", PolicyOverride::Global),
                                    ] {
                                        let selected = self.state.override_for(id) == value;
                                        if ui
                                            .selectable_label(
                                                selected,
                                                RichText::new(label)
                                                    .monospace()
                                                    .size(8.0)
                                                    .color(if selected { AMBER } else { MUTED }),
                                            )
                                            .clicked()
                                        {
                                            action = Some((id.clone(), value));
                                        }
                                    }
                                }
                            }
                            ui.label(
                                RichText::new(if decision.differs { "DIFF" } else { "SAME" })
                                    .monospace()
                                    .size(8.0)
                                    .color(if decision.differs { color } else { DIM }),
                            );
                        });
                    });
                });
            if let Some((id, value)) = action {
                self.set_override(&id, value);
            }
            ui.add_space(6.0);
        }
        if !self.expert {
            ui.add_space(8.0);
            if ui
                .add(
                    egui::Button::new(
                        RichText::new("OPEN EXPERT MATRIX")
                            .monospace()
                            .size(10.0)
                            .color(AMBER),
                    )
                    .frame(false)
                    .corner_radius(0),
                )
                .clicked()
            {
                self.expert = true;
                self.persist_state();
            }
        }
    }
    fn page_unlocks(&mut self, ui: &mut egui::Ui) {
        page_header(
            ui,
            "EU UNLOCKS // 03",
            "WHAT THE POLICY SET UNLOCKS / READ-ONLY",
        );
        ui.label(
            RichText::new(self.t("unlocks_body"))
                .monospace()
                .size(10.0)
                .color(MUTED),
        );
        ui.add_space(10.0);
        ui.label(
            RichText::new(self.t("unlocks_software"))
                .monospace()
                .size(10.0)
                .color(AMBER),
        );
        for entry in catalog::software_uninstall_unlocks() {
            Frame::NONE
                .fill(PANEL)
                .stroke(Stroke::new(1.0, BORDER))
                .inner_margin(Margin::same(10))
                .show(ui, |ui| {
                    ui.label(
                        RichText::new(entry.describe(self.lang))
                            .monospace()
                            .size(12.0)
                            .color(INK),
                    );
                    ui.label(
                        RichText::new(format!(
                            "{}  //  {}",
                            entry.id,
                            self.t("software_uninstall_note")
                        ))
                        .monospace()
                        .size(9.0)
                        .color(MUTED),
                    );
                });
            ui.add_space(5.0);
        }
        if ui
            .add(
                egui::Button::new(
                    RichText::new(format!("[ {} ]", self.t("open_windows_apps")))
                        .monospace()
                        .size(10.0)
                        .color(BG),
                )
                .fill(AMBER)
                .stroke(Stroke::new(1.0, AMBER))
                .corner_radius(0)
                .min_size(Vec2::new(280.0, 34.0)),
            )
            .clicked()
            && let Err(err) = self.system.open_uri("ms-settings:appsfeatures")
        {
            self.set_toast(err.to_string(), true);
        }
        ui.add_space(14.0);
        ui.label(
            RichText::new(self.t("unlocks_feature"))
                .monospace()
                .size(10.0)
                .color(AMBER),
        );
        if let Some(plan) = self.snapshot.plan.clone() {
            for decision in plan.decisions.iter().filter(|decision| {
                decision.class == Some(PolicyClass::Benefit) && !decision.software_uninstall_unlock
            }) {
                Frame::NONE
                    .fill(PANEL)
                    .stroke(Stroke::new(1.0, BORDER))
                    .inner_margin(Margin::same(9))
                    .show(ui, |ui| {
                        ui.horizontal(|ui| {
                            ui.label(RichText::new("[GAIN]").monospace().size(9.0).color(GREEN));
                            ui.label(
                                RichText::new(policy_description(decision, self.lang))
                                    .monospace()
                                    .size(11.0)
                                    .color(INK),
                            );
                            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                ui.label(
                                    RichText::new(format!(
                                        "{} → {}",
                                        decision.global, decision.target
                                    ))
                                    .monospace()
                                    .size(8.0)
                                    .color(MUTED),
                                );
                            });
                        });
                    });
                ui.add_space(3.0);
            }
        }
    }

    fn page_maintenance(&mut self, ui: &mut egui::Ui) {
        page_header(
            ui,
            "MAINTENANCE // 04",
            "VERIFY / RESTORE / EXPORT / RECOVERY",
        );
        ui.columns(2, |cols| {
            status_card(
                &mut cols[0],
                self.t("backup_status"),
                if self.snapshot.backups {
                    self.t("backup_ready")
                } else {
                    self.t("backup_none")
                },
                "original + pristine policy snapshots",
                if self.snapshot.backups { GREEN } else { MUTED },
            );
            status_card(
                &mut cols[1],
                self.t("status_persistence"),
                task_label(self.snapshot.task, self.lang),
                "SYSTEM scheduled task / 3 triggers",
                if self.snapshot.task == TaskStatus::Ready {
                    GREEN
                } else {
                    WARN
                },
            );
        });
        ui.add_space(12.0);
        ui.horizontal_wrapped(|ui| {
            if action_button(ui, self.t("verify"), CYAN).clicked() {
                if let Some(plan) = self.snapshot.plan.clone() {
                    self.verify_report = Some(engine::verify(&self.system, &self.state, &plan));
                } else {
                    self.set_toast(self.t("plan_failed"), true);
                }
            }
            if action_button(ui, self.t("restore"), WARN).clicked() {
                self.show_restore = true;
            }
            if action_button(ui, self.t("purge"), RED).clicked() {
                self.show_purge = true;
            }
            if action_button(ui, self.t("export"), AMBER).clicked()
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
            if action_button(ui, self.t("import"), CYAN).clicked()
                && let Some(path) = rfd::FileDialog::new().pick_file()
            {
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
            if action_button(ui, self.t("open_logs"), MUTED).clicked() {
                let _ = self.system.open_path(&self.system.paths.log_file());
            }
            if action_button(ui, self.t("open_folder"), MUTED).clicked() {
                let _ = self.system.open_path(&self.system.paths.data_root);
            }
            if action_button(ui, self.t("check_updates"), AMBER).clicked() {
                let _ = self
                    .system
                    .open_uri(&format!("{}/releases", eulord_core::REPOSITORY));
            }
        });
        ui.add_space(12.0);
        if let Some(report) = &self.verify_report {
            ui.label(
                RichText::new("VERIFY REPORT // CRITICAL CHECKS")
                    .monospace()
                    .size(10.0)
                    .color(AMBER),
            );
            Frame::NONE
                .fill(PANEL)
                .stroke(Stroke::new(1.0, BORDER))
                .inner_margin(Margin::same(12))
                .show(ui, |ui| {
                    for check in &report.checks {
                        let color = check_color(check.status);
                        ui.horizontal(|ui| {
                            ui.label(
                                RichText::new(format!("{:?}", check.status))
                                    .monospace()
                                    .size(9.0)
                                    .color(color),
                            );
                            ui.label(RichText::new(&check.id).monospace().size(10.0).color(INK));
                            ui.label(
                                RichText::new(&check.detail)
                                    .monospace()
                                    .size(9.0)
                                    .color(MUTED),
                            );
                        });
                    }
                });
        }
        if let Some(report) = &self.last_report {
            ui.add_space(10.0);
            ui.label(
                RichText::new("LAST OPERATION")
                    .monospace()
                    .size(10.0)
                    .color(AMBER),
            );
            Frame::NONE
                .fill(PANEL)
                .stroke(Stroke::new(1.0, BORDER))
                .inner_margin(Margin::same(12))
                .show(ui, |ui| {
                    ui.label(
                        RichText::new(format!("{:?}: {}", report.outcome, report.message))
                            .monospace()
                            .size(10.0)
                            .color(if report.outcome == ApplyOutcome::Success {
                                GREEN
                            } else {
                                RED
                            }),
                    );
                });
        }
    }

    fn page_about(&mut self, ui: &mut egui::Ui) {
        page_header(ui, "ABOUT // 05", "PURE RUST / MIT / NO TELEMETRY");
        ui.label(
            RichText::new(self.t("about_body"))
                .monospace()
                .size(11.0)
                .color(MUTED),
        );
        ui.add_space(12.0);
        ui.columns(2, |cols| {
            info_panel(
                &mut cols[0],
                "BUILD",
                &format!("One-Click EU Lord v{}", eulord_core::APP_VERSION),
            );
            info_panel(&mut cols[1], "CATALOG", catalog::CATALOG_VERSION);
        });
        ui.add_space(10.0);
        ui.columns(2, |cols| {
            info_panel(&mut cols[0], "OS", &self.snapshot.os.full_label());
            info_panel(
                &mut cols[1],
                "COMPATIBILITY",
                match self.snapshot.os.compat {
                    CompatLevel::Tested => "TESTED BUILD",
                    CompatLevel::Known => "KNOWN BUILD",
                    CompatLevel::Unknown => "UNKNOWN BUILD",
                    CompatLevel::Unsupported => "UNSUPPORTED",
                },
            );
        });
        ui.add_space(12.0);
        ui.horizontal_wrapped(|ui| {
            let crt_label = self.t("crt_effect");
            if ui
                .checkbox(
                    &mut self.crt,
                    RichText::new(crt_label).monospace().size(10.0).color(MUTED),
                )
                .changed()
            {
                self.persist_state();
            }
            let expert_label = self.t("expert_mode");
            if ui
                .checkbox(
                    &mut self.expert,
                    RichText::new(expert_label)
                        .monospace()
                        .size(10.0)
                        .color(MUTED),
                )
                .changed()
            {
                self.persist_state();
            }
            if action_button(ui, self.t("about_repo"), AMBER).clicked() {
                let _ = self.system.open_uri(eulord_core::REPOSITORY);
            }
        });
        ui.add_space(8.0);
        ui.label(
            RichText::new(self.t("about_license"))
                .monospace()
                .size(9.0)
                .color(DIM),
        );
        ui.label(
            RichText::new(self.t("software_uninstall_note"))
                .monospace()
                .size(9.0)
                .color(WARN),
        );
    }
    fn draw_modals(&mut self, ctx: &egui::Context) {
        if self.show_welcome {
            egui::Modal::new(egui::Id::new("welcome"))
                .frame(modal_frame())
                .show(ctx, |ui| {
                    modal_body(ui);
                    ui.label(
                        RichText::new(self.t("first_run_title"))
                            .monospace()
                            .size(24.0)
                            .strong()
                            .color(AMBER),
                    );
                    ui.add_space(8.0);
                    ui.label(
                        RichText::new(self.t("first_run_body"))
                            .monospace()
                            .size(12.0)
                            .color(INK),
                    );
                    ui.add_space(10.0);
                    ui.label(
                        RichText::new(format!(
                            "{}  //  {}",
                            self.snapshot.os.full_label(),
                            match self.snapshot.os.compat {
                                CompatLevel::Tested => "TESTED",
                                CompatLevel::Known => "KNOWN",
                                CompatLevel::Unknown => "UNKNOWN",
                                CompatLevel::Unsupported => "UNSUPPORTED",
                            }
                        ))
                        .monospace()
                        .size(9.0)
                        .color(MUTED),
                    );
                    ui.label(
                        RichText::new(self.t("apply_no_uninstall"))
                            .monospace()
                            .size(9.0)
                            .color(WARN),
                    );
                    ui.add_space(12.0);
                    ui.horizontal(|ui| {
                        if ui.add(primary_button(self.t("first_run_scan"))).clicked() {
                            self.show_welcome = false;
                            self.state.ui.first_run_done = true;
                            self.persist_state();
                            self.refresh();
                        }
                        if ui.add(secondary_button(self.t("first_run_skip"))).clicked() {
                            self.show_welcome = false;
                            self.state.ui.first_run_done = true;
                            self.persist_state();
                        }
                    });
                });
        }
        if self.show_review {
            let plan = self.snapshot.plan.clone();
            egui::Modal::new(egui::Id::new("review"))
                .frame(modal_frame())
                .show(ctx, |ui| {
                    modal_body(ui);
                    ui.label(
                        RichText::new(self.t("review_title"))
                            .monospace()
                            .size(20.0)
                            .strong()
                            .color(AMBER),
                    );
                    ui.label(
                        RichText::new(self.t("review_body"))
                            .monospace()
                            .size(10.0)
                            .color(MUTED),
                    );
                    ui.add_space(8.0);
                    if let Some(plan) = &plan {
                        ui.label(
                            RichText::new(format!(
                                "GAINS {}  //  KEPT {}  //  TASTE {}  //  CHANGED {}",
                                plan.benefits_taken,
                                plan.losses_refused,
                                plan.choices_taken,
                                plan.changed_policies
                            ))
                            .monospace()
                            .size(11.0)
                            .color(INK),
                        );
                        if !plan.unclassified.is_empty() {
                            ui.label(
                                RichText::new(self.t("unknown_policies_notice"))
                                    .monospace()
                                    .size(9.0)
                                    .color(WARN),
                            );
                        }
                    }
                    ui.add_space(8.0);
                    if let Some(plan) = &plan {
                        ui.label(
                            RichText::new("DIFF SAMPLE // TOP CHANGES")
                                .monospace()
                                .size(9.0)
                                .color(AMBER),
                        );
                        for decision in plan.changed_decisions().take(7) {
                            let class = decision.class.map(|c| c.short()).unwrap_or("UNKNOWN");
                            ui.horizontal(|ui| {
                                ui.label(
                                    RichText::new(format!("[{}]", class))
                                        .monospace()
                                        .size(8.0)
                                        .color(class_color(decision.class)),
                                );
                                ui.label(
                                    RichText::new(policy_description(decision, self.lang))
                                        .monospace()
                                        .size(9.0)
                                        .color(INK),
                                );
                                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                    ui.label(
                                        RichText::new(format!(
                                            "{} -> {}",
                                            decision.global, decision.target
                                        ))
                                        .monospace()
                                        .size(8.0)
                                        .color(MUTED),
                                    );
                                });
                            });
                        }
                        if plan.changed_policies > 7 {
                            ui.label(
                                RichText::new("... full diff in POLICY MATRIX")
                                    .monospace()
                                    .size(8.0)
                                    .color(DIM),
                            );
                        }
                    }
                    ui.add_space(8.0);
                    ui.label(
                        RichText::new(self.t("apply_no_uninstall"))
                            .monospace()
                            .size(10.0)
                            .color(WARN),
                    );
                    if !self.snapshot.elevated {
                        ui.label(
                            RichText::new(self.t("admin_required"))
                                .monospace()
                                .size(9.0)
                                .color(MUTED),
                        );
                    }
                    ui.add_space(12.0);
                    ui.horizontal(|ui| {
                        if ui.add(secondary_button(self.t("nav_policies"))).clicked() {
                            self.show_review = false;
                            self.page = Page::Policies;
                        }
                        if ui.add(primary_button(self.t("confirm_apply"))).clicked() {
                            self.start_apply();
                        }
                        if ui.add(secondary_button(self.t("cancel"))).clicked() {
                            self.show_review = false;
                        }
                    });
                });
        }
        if self.busy.is_some() {
            egui::Modal::new(egui::Id::new("busy"))
                .frame(modal_frame())
                .show(ctx, |ui| {
                    modal_body(ui);
                    ui.label(
                        RichText::new(self.t("applying"))
                            .monospace()
                            .size(20.0)
                            .strong()
                            .color(AMBER),
                    );
                    ui.add_space(8.0);
                    ui.add(
                        egui::ProgressBar::new(self.progress)
                            .desired_height(8.0)
                            .fill(AMBER),
                    );
                    for step in self
                        .steps
                        .iter()
                        .rev()
                        .take(8)
                        .collect::<Vec<_>>()
                        .into_iter()
                        .rev()
                    {
                        ui.label(
                            RichText::new(format!(
                                "{:?}  {}  {}",
                                step.state, step.id, step.detail
                            ))
                            .monospace()
                            .size(9.0)
                            .color(step_color(step.state)),
                        );
                    }
                    ui.add_space(8.0);
                    ui.label(
                        RichText::new(self.t("review_restart"))
                            .monospace()
                            .size(9.0)
                            .color(MUTED),
                    );
                });
        }
        if self.show_restore {
            egui::Modal::new(egui::Id::new("restore"))
                .frame(modal_frame())
                .show(ctx, |ui| {
                    modal_body(ui);
                    ui.label(
                        RichText::new(self.t("restore"))
                            .monospace()
                            .size(20.0)
                            .strong()
                            .color(WARN),
                    );
                    ui.label(
                        RichText::new(self.t("restore_confirm"))
                            .monospace()
                            .size(11.0)
                            .color(INK),
                    );
                    ui.add_space(10.0);
                    ui.horizontal(|ui| {
                        if ui.add(primary_button(self.t("restore"))).clicked() {
                            self.start_restore();
                        }
                        if ui.add(secondary_button(self.t("cancel"))).clicked() {
                            self.show_restore = false;
                        }
                    });
                });
        }
        if self.show_purge {
            egui::Modal::new(egui::Id::new("purge"))
                .frame(modal_frame())
                .show(ctx, |ui| {
                    modal_body(ui);
                    ui.label(
                        RichText::new(self.t("purge"))
                            .monospace()
                            .size(20.0)
                            .strong()
                            .color(RED),
                    );
                    ui.label(
                        RichText::new(self.t("purge_confirm"))
                            .monospace()
                            .size(11.0)
                            .color(INK),
                    );
                    ui.add(
                        egui::TextEdit::singleline(&mut self.purge_text)
                            .hint_text("PURGE")
                            .desired_width(220.0),
                    );
                    ui.add_space(10.0);
                    ui.horizontal(|ui| {
                        let can = self.purge_text == "PURGE";
                        if ui
                            .add_enabled(can, primary_danger_button(self.t("purge")))
                            .clicked()
                        {
                            self.start_purge();
                        }
                        if ui.add(secondary_button(self.t("cancel"))).clicked() {
                            self.show_purge = false;
                        }
                    });
                });
        }
        if self.verify_report.is_some() {
            let report = self.verify_report.clone().unwrap();
            egui::Modal::new(egui::Id::new("verify"))
                .frame(modal_frame())
                .show(ctx, |ui| {
                    modal_body(ui);
                    ui.label(
                        RichText::new(self.t("verify"))
                            .monospace()
                            .size(20.0)
                            .strong()
                            .color(CYAN),
                    );
                    for check in &report.checks {
                        ui.label(
                            RichText::new(format!(
                                "{:?}  {}  {}",
                                check.status, check.id, check.detail
                            ))
                            .monospace()
                            .size(10.0)
                            .color(check_color(check.status)),
                        );
                    }
                    ui.add_space(10.0);
                    if ui.add(secondary_button(self.t("close"))).clicked() {
                        self.verify_report = None;
                    }
                });
        }
    }

    fn draw_resize_grip(&mut self, ui: &mut egui::Ui) {
        let ctx = ui.ctx().clone();
        let screen = ctx.content_rect();
        egui::Area::new(egui::Id::new("resize_grip"))
            .fixed_pos(screen.right_bottom() - Vec2::new(18.0, 18.0))
            .order(egui::Order::Foreground)
            .show(&ctx, |ui| {
                let (rect, response) = ui.allocate_exact_size(Vec2::new(16.0, 16.0), Sense::drag());
                ui.painter().line_segment(
                    [
                        rect.right_bottom() - Vec2::new(2.0, 12.0),
                        rect.right_bottom() - Vec2::new(12.0, 2.0),
                    ],
                    Stroke::new(1.0, BORDER_HOT),
                );
                ui.painter().line_segment(
                    [
                        rect.right_bottom() - Vec2::new(2.0, 8.0),
                        rect.right_bottom() - Vec2::new(8.0, 2.0),
                    ],
                    Stroke::new(1.0, BORDER_HOT),
                );
                if response.dragged() {
                    let delta = ctx.input(|i| i.pointer.delta());
                    let current = ctx.input(|i| {
                        i.viewport()
                            .inner_rect
                            .map(|r| r.size())
                            .unwrap_or(Vec2::new(1180.0, 780.0))
                    });
                    let new_size = Vec2::new(
                        (current.x + delta.x).max(940.0),
                        (current.y + delta.y).max(620.0),
                    );
                    ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(new_size));
                }
            });
    }

    fn draw_crt(&mut self, ui: &mut egui::Ui) {
        if !self.crt {
            return;
        }
        let ctx = ui.ctx().clone();
        let screen = ctx.content_rect();
        let painter = ctx.layer_painter(egui::LayerId::new(
            egui::Order::Foreground,
            egui::Id::new("crt_scanlines"),
        ));
        let mut y = screen.top();
        while y < screen.bottom() {
            painter.line_segment(
                [Pos2::new(screen.left(), y), Pos2::new(screen.right(), y)],
                Stroke::new(1.0, Color32::from_rgba_unmultiplied(0, 0, 0, 16)),
            );
            y += 4.0;
        }
        let t = ctx.input(|i| i.time);
        let beam_y = screen.top() + ((t * 26.0) % screen.height().max(1.0) as f64) as f32;
        painter.line_segment(
            [
                Pos2::new(screen.left(), beam_y),
                Pos2::new(screen.right(), beam_y),
            ],
            Stroke::new(1.0, Color32::from_rgba_unmultiplied(101, 227, 195, 20)),
        );
        ctx.request_repaint_after(Duration::from_millis(50));
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
        if let Some(toast) = &self.toast {
            let color = if toast.error { RED } else { GREEN };
            egui::Area::new(egui::Id::new("toast"))
                .anchor(Align2::RIGHT_BOTTOM, Vec2::new(-24.0, -24.0))
                .order(egui::Order::Tooltip)
                .show(ctx, |ui| {
                    Frame::NONE
                        .fill(PANEL_HI)
                        .stroke(Stroke::new(1.0, color))
                        .inner_margin(Margin::symmetric(12, 8))
                        .show(ui, |ui| {
                            ui.label(
                                RichText::new(&toast.text)
                                    .monospace()
                                    .size(10.0)
                                    .color(color),
                            );
                        });
                });
        }
    }
}

pub fn run(cli: Cli) -> eframe::Result<()> {
    let mut viewport = egui::ViewportBuilder::default()
        .with_inner_size([960.0, 600.0])
        .with_min_inner_size([820.0, 540.0])
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

fn modal_frame() -> Frame {
    Frame::NONE
        .fill(PANEL)
        .stroke(Stroke::new(1.0, BORDER_HOT))
        .inner_margin(Margin::same(20))
}

fn modal_body(ui: &mut egui::Ui) {
    let width = (ui.ctx().content_rect().width() - 80.0).clamp(360.0, 540.0);
    ui.set_width(width);
    ui.set_max_width(width);
    ui.style_mut().wrap_mode = Some(egui::TextWrapMode::Wrap);
}

fn primary_button(label: &str) -> egui::Button<'static> {
    egui::Button::new(
        RichText::new(format!("[ {} ]", label))
            .monospace()
            .size(12.0)
            .strong()
            .color(BG),
    )
    .fill(AMBER)
    .stroke(Stroke::new(1.0, AMBER))
    .corner_radius(0)
    .min_size(Vec2::new(190.0, 38.0))
}

fn primary_danger_button(label: &str) -> egui::Button<'static> {
    egui::Button::new(
        RichText::new(format!("[ {} ]", label))
            .monospace()
            .size(12.0)
            .strong()
            .color(BG),
    )
    .fill(RED)
    .stroke(Stroke::new(1.0, RED))
    .corner_radius(0)
    .min_size(Vec2::new(190.0, 38.0))
}

fn secondary_button(label: &str) -> egui::Button<'static> {
    egui::Button::new(
        RichText::new(format!("[ {} ]", label))
            .monospace()
            .size(10.0)
            .color(MUTED),
    )
    .fill(Color32::TRANSPARENT)
    .stroke(Stroke::new(1.0, BORDER))
    .corner_radius(0)
    .min_size(Vec2::new(120.0, 34.0))
}

fn action_button(ui: &mut egui::Ui, label: &str, color: Color32) -> egui::Response {
    ui.add(
        egui::Button::new(
            RichText::new(format!("[ {} ]", label))
                .monospace()
                .size(10.0)
                .color(color),
        )
        .fill(PANEL_HI)
        .stroke(Stroke::new(1.0, BORDER))
        .corner_radius(0)
        .min_size(Vec2::new(130.0, 32.0)),
    )
}

fn page_header(ui: &mut egui::Ui, title: &str, subtitle: &str) {
    ui.horizontal(|ui| {
        ui.label(
            RichText::new(title)
                .monospace()
                .size(11.0)
                .strong()
                .color(AMBER),
        );
        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
            ui.label(RichText::new(subtitle).monospace().size(9.0).color(MUTED));
        });
    });
    ui.add_space(6.0);
    ui.separator();
    ui.add_space(10.0);
}

fn status_card(ui: &mut egui::Ui, label: &str, value: &str, detail: &str, color: Color32) {
    Frame::NONE
        .fill(PANEL)
        .stroke(Stroke::new(1.0, BORDER))
        .inner_margin(Margin::same(12))
        .show(ui, |ui| {
            ui.set_min_height(82.0);
            ui.style_mut().wrap_mode = Some(egui::TextWrapMode::Wrap);
            ui.horizontal(|ui| {
                ui.label(RichText::new("●").monospace().size(9.0).color(color));
                ui.label(RichText::new(label).monospace().size(9.0).color(MUTED));
            });
            ui.add_space(5.0);
            ui.label(
                RichText::new(value)
                    .monospace()
                    .size(16.0)
                    .strong()
                    .color(INK),
            );
            ui.label(RichText::new(detail).monospace().size(9.0).color(MUTED));
        });
}

fn info_panel(ui: &mut egui::Ui, label: &str, value: &str) {
    Frame::NONE
        .fill(PANEL)
        .stroke(Stroke::new(1.0, BORDER))
        .inner_margin(Margin::same(10))
        .show(ui, |ui| {
            ui.label(RichText::new(label).monospace().size(9.0).color(AMBER));
            ui.label(RichText::new(value).monospace().size(11.0).color(INK));
        });
}

fn log_line(ui: &mut egui::Ui, tag: &str, text: &str, color: Color32) {
    ui.horizontal(|ui| {
        ui.label(
            RichText::new(format!("{:>7}", tag))
                .monospace()
                .size(9.0)
                .color(color),
        );
        ui.label(RichText::new(text).monospace().size(9.0).color(MUTED));
    });
}

fn policy_description(decision: &PolicyDecision, lang: Lang) -> String {
    decision
        .id
        .as_deref()
        .and_then(catalog::by_id)
        .map(|entry| entry.describe(lang).to_string())
        .unwrap_or_else(|| format!("UNCLASSIFIED // {}", decision.guid))
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

fn class_color(class: Option<PolicyClass>) -> Color32 {
    match class {
        Some(PolicyClass::Benefit) => GREEN,
        Some(PolicyClass::Loss) => WARN,
        Some(PolicyClass::Choice) => CYAN,
        None => RED,
    }
}

fn step_color(state: StepState) -> Color32 {
    match state {
        StepState::Pass => GREEN,
        StepState::Warn => WARN,
        StepState::Fail => RED,
        StepState::Running => AMBER,
        StepState::Pending => DIM,
    }
}

fn check_color(status: CheckStatus) -> Color32 {
    match status {
        CheckStatus::Pass => GREEN,
        CheckStatus::Pending => WARN,
        CheckStatus::Unknown => MUTED,
        CheckStatus::Fail => RED,
    }
}
