use chrono::Utc;
use eulord_core::error::Result;
use eulord_core::plan::{Plan, build_plan};
use eulord_core::state::{ApplyRecord, STATE_SCHEMA_VERSION, State, StepResult};
use eulord_windows::{CheckStatus, PolicyOrigin, TaskStatus, VerifyReport, WindowsSystem};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum StepState {
    Pending,
    Running,
    Pass,
    Warn,
    Fail,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StepUpdate {
    pub id: String,
    pub state: StepState,
    pub detail: String,
    pub progress: f32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ApplyOutcome {
    Success,
    Partial,
    Failed,
    NeedsElevation,
    Cancelled,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ApplyReport {
    pub outcome: ApplyOutcome,
    pub steps: Vec<StepUpdate>,
    pub verify: Option<VerifyReport>,
    pub message: String,
}

impl ApplyReport {
    pub fn from_message(outcome: ApplyOutcome, message: impl Into<String>) -> Self {
        Self {
            outcome,
            steps: Vec::new(),
            verify: None,
            message: message.into(),
        }
    }
}

struct Sink<F: FnMut(StepUpdate)> {
    steps: Vec<StepUpdate>,
    progress: F,
}

impl<F: FnMut(StepUpdate)> Sink<F> {
    fn new(progress: F) -> Self {
        Self {
            steps: Vec::new(),
            progress,
        }
    }
    fn emit(&mut self, id: &str, state: StepState, detail: &str, progress: f32) {
        let update = StepUpdate {
            id: id.to_string(),
            state,
            detail: detail.to_string(),
            progress,
        };
        (self.progress)(update.clone());
        self.steps.push(update);
    }
}

pub fn make_plan(system: &WindowsSystem, state: &State) -> Result<Plan> {
    let source = system.pristine_source()?.ok_or_else(|| {
        eulord_core::CoreError::Policy("no pristine Windows policy source is available".to_string())
    })?;
    build_plan(&source, &state.region_iso, &state.overrides)
}

pub fn apply<F>(system: &WindowsSystem, state: &mut State, plan: &Plan, progress: F) -> ApplyReport
where
    F: FnMut(StepUpdate),
{
    let mut sink = Sink::new(progress);
    let started = Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true);
    if !system.is_elevated() {
        return ApplyReport::from_message(
            ApplyOutcome::NeedsElevation,
            "administrator rights are required",
        );
    }
    if let Err(err) = system.ensure_layout() {
        sink.emit(
            "step_backup",
            StepState::Fail,
            &format!("data folder: {err}"),
            0.05,
        );
        return ApplyReport {
            outcome: ApplyOutcome::Failed,
            steps: sink.steps,
            verify: None,
            message: err.to_string(),
        };
    }
    sink.emit(
        "step_backup",
        StepState::Running,
        "capturing Windows policy and state",
        0.05,
    );
    match system.backup_policy() {
        Ok(outcome) => {
            let detail = format!(
                "original_created={}, pristine_updated={}, source_available={}",
                outcome.original_created, outcome.pristine_updated, outcome.source_available
            );
            sink.emit("step_backup", StepState::Pass, &detail, 0.15);
        }
        Err(err) => {
            sink.emit("step_backup", StepState::Fail, &err.to_string(), 0.15);
            return ApplyReport {
                outcome: ApplyOutcome::Failed,
                steps: sink.steps,
                verify: None,
                message: err.to_string(),
            };
        }
    }
    match system.create_restore_point("One-Click EU Lord v3 apply") {
        Ok(()) => sink.emit(
            "step_backup",
            StepState::Pass,
            "restore point created",
            0.22,
        ),
        Err(err) => sink.emit(
            "step_backup",
            StepState::Warn,
            &format!("restore point unavailable: {err}"),
            0.22,
        ),
    }
    let original_region = system.read_region().ok().flatten();
    state.remember_region(original_region);
    if let Err(err) = system.install_self() {
        sink.emit(
            "step_policy",
            StepState::Fail,
            &format!("self copy: {err}"),
            0.25,
        );
        return ApplyReport {
            outcome: ApplyOutcome::Failed,
            steps: sink.steps,
            verify: None,
            message: err.to_string(),
        };
    }
    sink.emit(
        "step_policy",
        StepState::Running,
        "writing the policy file atomically",
        0.3,
    );
    if let Err(err) = system.write_policy(&plan.json) {
        sink.emit("step_policy", StepState::Fail, &err.to_string(), 0.45);
        return ApplyReport {
            outcome: ApplyOutcome::Failed,
            steps: sink.steps,
            verify: None,
            message: err.to_string(),
        };
    }
    sink.emit(
        "step_policy",
        StepState::Pass,
        &format!("{} changed policies written", plan.changed_policies),
        0.5,
    );
    sink.emit(
        "step_region",
        StepState::Running,
        "writing DeviceRegion",
        0.55,
    );
    if let Err(err) = system.write_region(state.region_geoid) {
        sink.emit("step_region", StepState::Fail, &err.to_string(), 0.65);
        rollback(system, state, original_region, &mut sink);
        return ApplyReport {
            outcome: ApplyOutcome::Failed,
            steps: sink.steps,
            verify: None,
            message: err.to_string(),
        };
    }
    sink.emit(
        "step_region",
        StepState::Pass,
        &format!("DeviceRegion = {}", state.region_geoid),
        0.68,
    );
    sink.emit(
        "step_task",
        StepState::Running,
        "registering the scheduled task",
        0.72,
    );
    if let Err(err) = system.install_task() {
        sink.emit("step_task", StepState::Fail, &err.to_string(), 0.8);
        rollback(system, state, original_region, &mut sink);
        return ApplyReport {
            outcome: ApplyOutcome::Failed,
            steps: sink.steps,
            verify: None,
            message: err.to_string(),
        };
    }
    sink.emit(
        "step_task",
        StepState::Pass,
        "scheduled task registered",
        0.82,
    );
    state.schema_version = STATE_SCHEMA_VERSION;
    state.product_version = eulord_core::APP_VERSION.to_string();
    state.applied_utc = Some(Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true));
    state.last_policy_hash = Some(WindowsSystem::policy_hash(&plan.json));
    state.unclassified = plan.unclassified.clone();
    state.last_apply = Some(ApplyRecord {
        started_utc: started,
        finished_utc: Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true),
        outcome: "applied".to_string(),
        steps: sink
            .steps
            .iter()
            .map(|step| StepResult {
                id: step.id.clone(),
                status: format!("{:?}", step.state),
                detail: step.detail.clone(),
            })
            .collect(),
        plan_hash: state.last_policy_hash.clone(),
    });
    if let Err(err) = system.save_state(state) {
        sink.emit(
            "step_verify",
            StepState::Fail,
            &format!("state save: {err}"),
            0.86,
        );
        rollback(system, state, original_region, &mut sink);
        return ApplyReport {
            outcome: ApplyOutcome::Failed,
            steps: sink.steps,
            verify: None,
            message: err.to_string(),
        };
    }
    sink.emit(
        "step_verify",
        StepState::Running,
        "verifying policy, region and persistence",
        0.9,
    );
    let report = system.verify(state, plan);
    let verify_failed = !report.all_settled_pass;
    let verify_detail = report
        .checks
        .iter()
        .map(|c| format!("{}={:?}", c.id, c.status))
        .collect::<Vec<_>>()
        .join(", ");
    sink.emit(
        "step_verify",
        if verify_failed {
            StepState::Fail
        } else {
            StepState::Pass
        },
        &verify_detail,
        0.98,
    );
    if verify_failed {
        rollback(system, state, original_region, &mut sink);
        return ApplyReport {
            outcome: ApplyOutcome::Failed,
            steps: sink.steps,
            verify: Some(report),
            message: "post-apply verification failed".to_string(),
        };
    }
    sink.emit(
        "step_verify",
        StepState::Pass,
        "all critical checks passed",
        1.0,
    );
    ApplyReport {
        outcome: ApplyOutcome::Success,
        steps: sink.steps,
        verify: Some(report),
        message: "superset applied".to_string(),
    }
}
fn rollback<F: FnMut(StepUpdate)>(
    system: &WindowsSystem,
    state: &mut State,
    _original_region: Option<u32>,
    sink: &mut Sink<F>,
) {
    sink.emit(
        "step_rollback",
        StepState::Running,
        "restoring the previous machine state",
        0.9,
    );
    let mut problems = Vec::new();
    if let Err(err) = system.remove_task() {
        problems.push(format!("task: {err}"));
    }
    if let Err(err) = system.restore_policy() {
        problems.push(format!("policy: {err}"));
    }
    let region_result = if state.original_region_present {
        state
            .original_region_geoid
            .map(|geoid| system.write_region(geoid))
            .unwrap_or_else(|| system.delete_region())
    } else {
        system.delete_region()
    };
    if let Err(err) = region_result {
        problems.push(format!("region: {err}"));
    }
    state.applied_utc = None;
    state.last_policy_hash = None;
    if let Err(err) = system.save_state(state) {
        problems.push(format!("state: {err}"));
    }
    if problems.is_empty() {
        sink.emit(
            "step_rollback",
            StepState::Pass,
            "previous policy, region and task restored",
            0.99,
        );
    } else {
        let detail = problems.join("; ");
        sink.emit("step_rollback", StepState::Fail, &detail, 0.99);
    }
}

pub fn restore<F>(system: &WindowsSystem, state: &mut State, progress: F) -> ApplyReport
where
    F: FnMut(StepUpdate),
{
    let mut sink = Sink::new(progress);
    if !system.is_elevated() {
        return ApplyReport::from_message(
            ApplyOutcome::NeedsElevation,
            "administrator rights are required",
        );
    }
    let mut problems = Vec::new();
    sink.emit(
        "step_task",
        StepState::Running,
        "removing the scheduled task",
        0.1,
    );
    if let Err(err) = system.remove_task() {
        problems.push(format!("task: {err}"));
    }
    sink.emit(
        "step_task",
        if problems.is_empty() {
            StepState::Pass
        } else {
            StepState::Warn
        },
        "task removal finished",
        0.3,
    );
    sink.emit(
        "step_policy",
        StepState::Running,
        "restoring the Windows policy file",
        0.35,
    );
    if let Err(err) = system.restore_policy() {
        problems.push(format!("policy: {err}"));
    }
    sink.emit(
        "step_policy",
        if problems.is_empty() {
            StepState::Pass
        } else {
            StepState::Warn
        },
        "policy restore finished",
        0.6,
    );
    sink.emit(
        "step_region",
        StepState::Running,
        "restoring the original DeviceRegion",
        0.65,
    );
    let region_result = if state.original_region_present {
        state
            .original_region_geoid
            .map(|geoid| system.write_region(geoid))
            .unwrap_or_else(|| system.delete_region())
    } else {
        system.delete_region()
    };
    if let Err(err) = region_result {
        problems.push(format!("region: {err}"));
    }
    sink.emit(
        "step_region",
        if problems.is_empty() {
            StepState::Pass
        } else {
            StepState::Warn
        },
        "region restore finished",
        0.85,
    );
    state.applied_utc = None;
    state.last_policy_hash = None;
    if let Err(err) = system.save_state(state) {
        problems.push(format!("state: {err}"));
    }
    let outcome = if problems.is_empty() {
        ApplyOutcome::Success
    } else {
        ApplyOutcome::Partial
    };
    ApplyReport {
        outcome,
        steps: sink.steps,
        verify: None,
        message: if problems.is_empty() {
            "restored".to_string()
        } else {
            problems.join("; ")
        },
    }
}

pub fn purge<F>(system: &WindowsSystem, state: &mut State, mut progress: F) -> ApplyReport
where
    F: FnMut(StepUpdate),
{
    let mut report = restore(system, state, &mut progress);
    let mut problems = Vec::new();
    let root = &system.paths.data_root;
    if root.exists() {
        if system.paths.running_from_data_root() {
            if let Ok(current) = std::env::current_exe()
                && let Ok(entries) = std::fs::read_dir(root)
            {
                for entry in entries.flatten() {
                    let path = entry.path();
                    if path == current {
                        continue;
                    }
                    let result = if path.is_dir() {
                        std::fs::remove_dir_all(&path)
                    } else {
                        std::fs::remove_file(&path)
                    };
                    if let Err(err) = result {
                        problems.push(format!("{}: {err}", path.display()));
                    }
                }
            }
            problems.push("the running copy remains until the next restart".to_string());
        } else if let Err(err) = std::fs::remove_dir_all(root) {
            problems.push(format!("data folder: {err}"));
        }
    }
    report.message = if problems.is_empty() {
        "purged".to_string()
    } else {
        problems.join("; ")
    };
    if !problems.is_empty() {
        report.outcome = ApplyOutcome::Partial;
    }
    report
}

pub fn verify(system: &WindowsSystem, state: &State, plan: &Plan) -> VerifyReport {
    system.verify(state, plan)
}
pub fn verify_all_pass(report: &VerifyReport) -> bool {
    report
        .checks
        .iter()
        .all(|c| !c.critical || c.status == CheckStatus::Pass)
}
pub fn status_origin(system: &WindowsSystem, state: &State) -> PolicyOrigin {
    system.classify_policy(state)
}
pub fn status_task(system: &WindowsSystem) -> TaskStatus {
    system.task_status()
}
pub fn task_ready(status: TaskStatus) -> bool {
    status == TaskStatus::Ready
}
