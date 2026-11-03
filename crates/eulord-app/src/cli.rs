use std::io::Write;
use std::path::PathBuf;

use clap::{Parser, Subcommand};
use eulord_core::catalog::{self, PolicyClass};
use eulord_core::error::Result;
use eulord_core::i18n::LangPref;
use eulord_core::paths::Paths;
use eulord_core::plan::PolicyOverride;
use eulord_core::policy;
use eulord_core::state::State;
use eulord_windows::{WindowsSystem, geo_id_for_iso, iso_for_geo_id, user_locale};
use serde_json::json;

use crate::engine::{self, ApplyOutcome};

#[derive(Debug, Clone, Parser)]
#[command(
    name = "eulord",
    version,
    about = "One-Click EU Lord v3",
    disable_help_subcommand = true
)]
pub struct Cli {
    #[arg(long, global = true)]
    pub data_dir: Option<PathBuf>,
    #[arg(long, global = true)]
    pub policy_file: Option<PathBuf>,
    #[arg(long, global = true)]
    pub language: Option<String>,
    #[arg(long, global = true, hide = true)]
    pub state_file: Option<PathBuf>,
    #[arg(long, global = true)]
    pub quiet: bool,
    #[arg(long, global = true)]
    pub json: bool,
    #[arg(long, global = true)]
    pub yes: bool,
    #[arg(long, global = true)]
    pub region: Option<String>,
    #[arg(long = "choice", global = true, value_delimiter = ',')]
    pub choices: Vec<String>,
    #[arg(long = "override", global = true, value_name = "ID=auto|eea|global")]
    pub overrides: Vec<String>,
    #[command(subcommand)]
    pub command: Option<Command>,
}

#[derive(Debug, Clone, Subcommand)]
pub enum Command {
    Status,
    Plan,
    Apply,
    Reapply,
    Repair,
    Restore,
    Purge,
    Verify,
    Export { path: Option<PathBuf> },
    Import { path: PathBuf },
}

pub fn run(cli: Cli, out: &mut dyn Write) -> i32 {
    match run_inner(&cli, out) {
        Ok(code) => code,
        Err(err) => {
            let _ = writeln!(out, "error: {err}");
            1
        }
    }
}

fn run_inner(cli: &Cli, out: &mut dyn Write) -> Result<i32> {
    let paths = Paths::discover(cli.data_dir.clone(), cli.policy_file.clone())?;
    let system = WindowsSystem::new(paths);
    let mut state = load_state(&system, cli.state_file.as_deref())?;
    apply_options(&mut state, cli, out)?;
    match cli.command.clone().unwrap_or(Command::Status) {
        Command::Status => status(&system, &state, cli, out),
        Command::Plan => plan(&system, &state, cli, out),
        Command::Apply => apply(&system, &mut state, cli, out),
        Command::Reapply => {
            if !state.ever_applied() {
                if !cli.quiet {
                    let _ = writeln!(out, "nothing to re-apply");
                }
                return Ok(0);
            }
            apply(&system, &mut state, cli, out)
        }
        Command::Repair => apply(&system, &mut state, cli, out),
        Command::Restore => restore(&system, &mut state, cli, out),
        Command::Purge => {
            if !cli.yes {
                let _ = writeln!(out, "--purge needs --yes");
                return Ok(2);
            }
            purge(&system, &mut state, cli, out)
        }
        Command::Verify => verify(&system, &state, cli, out),
        Command::Export { path } => {
            let path = path.unwrap_or_else(|| system.paths.export_file());
            let text = state.to_json()?;
            std::fs::write(&path, text)?;
            if !cli.quiet {
                let _ = writeln!(out, "exported state to {}", path.display());
            }
            Ok(0)
        }
        Command::Import { path } => {
            let text = std::fs::read_to_string(path)?;
            let imported = State::from_json(&text)?;
            system.save_state(&imported)?;
            if !cli.quiet {
                let _ = writeln!(out, "imported state");
            }
            Ok(0)
        }
    }
}

fn load_state(system: &WindowsSystem, state_file: Option<&std::path::Path>) -> Result<State> {
    if let Some(path) = state_file {
        let text = std::fs::read_to_string(path)?;
        return State::from_json(&text);
    }
    Ok(system.read_state().unwrap_or_default())
}

fn apply_options(state: &mut State, cli: &Cli, out: &mut dyn Write) -> Result<()> {
    if let Some(language) = &cli.language {
        state.language_preference = match language.as_str() {
            "auto" => LangPref::Auto,
            "zh-Hant" | "zh-TW" => LangPref::ZhHant,
            "zh-Hans" | "zh-CN" => LangPref::ZhHans,
            "en" => LangPref::En,
            _ => {
                let _ = writeln!(out, "unknown language: {language}");
                LangPref::Auto
            }
        };
    }
    if let Some(region) = &cli.region {
        if let Ok(geoid) = region.parse::<u32>() {
            if let Some(iso) = iso_for_geo_id(geoid) {
                state.region_geoid = geoid;
                state.region_iso = iso;
            } else {
                let _ = writeln!(out, "unknown GeoID: {geoid}");
            }
        } else if let Some(geoid) = geo_id_for_iso(region) {
            state.region_geoid = geoid;
            state.region_iso = region.to_ascii_uppercase();
        } else {
            let _ = writeln!(out, "unknown region: {region}");
        }
    }
    if !cli.choices.is_empty() {
        for entry in catalog::all()
            .iter()
            .filter(|entry| entry.class == PolicyClass::Choice)
        {
            state.set_override(&entry.id, PolicyOverride::Auto);
        }
        let all = cli.choices.len() == 1 && cli.choices[0].eq_ignore_ascii_case("all");
        for id in &cli.choices {
            if all {
                for entry in catalog::all()
                    .iter()
                    .filter(|entry| entry.class == PolicyClass::Choice)
                {
                    state.set_override(&entry.id, PolicyOverride::Eea);
                }
                break;
            }
            match catalog::by_id(id) {
                Some(entry) if entry.class == PolicyClass::Choice => {
                    state.set_override(id, PolicyOverride::Eea)
                }
                Some(_) => {
                    let _ = writeln!(out, "not a taste choice: {id}");
                }
                None => {
                    let _ = writeln!(out, "unknown choice: {id}");
                }
            }
        }
    }
    for item in &cli.overrides {
        let Some((id, value)) = item.split_once('=') else {
            let _ = writeln!(out, "bad override: {item}");
            continue;
        };
        if catalog::by_id(id).is_none() {
            let _ = writeln!(out, "unknown policy Id: {id}");
            continue;
        }
        let parsed = match value.to_ascii_lowercase().as_str() {
            "auto" => PolicyOverride::Auto,
            "eea" | "eu" => PolicyOverride::Eea,
            "global" => PolicyOverride::Global,
            _ => {
                let _ = writeln!(out, "bad override value: {value}");
                continue;
            }
        };
        state.set_override(id, parsed);
    }
    Ok(())
}

fn status(system: &WindowsSystem, state: &State, cli: &Cli, out: &mut dyn Write) -> Result<i32> {
    let origin = system.classify_policy(state);
    let task = system.task_status();
    let region = system.read_region()?;
    let plan = engine::make_plan(system, state).ok();
    let os = system.os_info();
    if cli.json {
        let value = json!({
            "version": eulord_core::APP_VERSION,
            "applied": state.ever_applied(),
            "appliedUtc": state.applied_utc,
            "elevated": system.is_elevated(),
            "os": os,
            "policyOrigin": origin,
            "regionActual": region,
            "regionWanted": state.region_geoid,
            "regionIso": state.region_iso,
            "scheduledTask": task,
            "backupAvailable": system.paths.pristine_backup().exists() || system.paths.original_backup().exists(),
            "plan": plan,
            "state": state,
        });
        let _ = writeln!(out, "{}", serde_json::to_string_pretty(&value)?);
        return Ok(0);
    }
    let _ = writeln!(out, "One-Click EU Lord {}", eulord_core::APP_VERSION);
    let _ = writeln!(out, "os              {} ({:?})", os.full_label(), os.compat);
    let _ = writeln!(out, "elevated        {}", system.is_elevated());
    let _ = writeln!(
        out,
        "applied         {}",
        state.applied_utc.as_deref().unwrap_or("never")
    );
    let _ = writeln!(out, "policy          {:?}", origin);
    let _ = writeln!(out, "region actual   {:?}", region);
    let _ = writeln!(
        out,
        "region wanted   {} ({})",
        state.region_geoid, state.region_iso
    );
    let _ = writeln!(out, "scheduled task  {:?}", task);
    if let Some(plan) = plan {
        let _ = writeln!(
            out,
            "gains           {} / {}",
            plan.gains(),
            plan.gains_possible()
        );
        let _ = writeln!(out, "changed files   {}", plan.changed_policies);
        let _ = writeln!(out, "unclassified    {}", plan.unclassified.len());
    }
    Ok(0)
}

fn plan(system: &WindowsSystem, state: &State, cli: &Cli, out: &mut dyn Write) -> Result<i32> {
    let plan = engine::make_plan(system, state)?;
    if cli.json {
        let _ = writeln!(out, "{}", serde_json::to_string_pretty(&plan)?);
    } else {
        let _ = writeln!(out, "gains {} / {}", plan.gains(), plan.gains_possible());
        let _ = writeln!(
            out,
            "changed {} of {} unique policies",
            plan.changed_policies, plan.unique_policies
        );
        let _ = writeln!(out, "unclassified {}", plan.unclassified.len());
        for decision in plan.changed_decisions() {
            let _ = writeln!(
                out,
                "  {:30} {:8} -> {:8}",
                decision.id.as_deref().unwrap_or(&decision.guid),
                decision.global,
                decision.target
            );
        }
    }
    Ok(0)
}

fn apply(system: &WindowsSystem, state: &mut State, cli: &Cli, out: &mut dyn Write) -> Result<i32> {
    if !system.is_elevated() {
        let _ = writeln!(out, "administrator rights are required");
        return Ok(3);
    }
    let plan = engine::make_plan(system, state)?;
    let report = engine::apply(system, state, &plan, |_| {});
    if cli.json {
        let _ = writeln!(out, "{}", serde_json::to_string_pretty(&report)?);
    } else if !cli.quiet {
        for step in &report.steps {
            let _ = writeln!(
                out,
                "{:7} {:20} {}",
                format!("{:?}", step.state),
                step.id,
                step.detail
            );
        }
        let _ = writeln!(out, "{:?}: {}", report.outcome, report.message);
    }
    if !cli.quiet
        && !cli.json
        && let Err(err) = system.save_state(state)
    {
        let _ = writeln!(out, "state save warning: {err}");
    }
    Ok(outcome_code(report.outcome))
}

fn restore(
    system: &WindowsSystem,
    state: &mut State,
    cli: &Cli,
    out: &mut dyn Write,
) -> Result<i32> {
    if !system.is_elevated() {
        let _ = writeln!(out, "administrator rights are required");
        return Ok(3);
    }
    let report = engine::restore(system, state, |_| {});
    if cli.json {
        let _ = writeln!(out, "{}", serde_json::to_string_pretty(&report)?);
    } else if !cli.quiet {
        for step in &report.steps {
            let _ = writeln!(out, "{:7} {}", format!("{:?}", step.state), step.detail);
        }
        let _ = writeln!(out, "{}", report.message);
    }
    Ok(outcome_code(report.outcome))
}

fn purge(system: &WindowsSystem, state: &mut State, cli: &Cli, out: &mut dyn Write) -> Result<i32> {
    if !system.is_elevated() {
        let _ = writeln!(out, "administrator rights are required");
        return Ok(3);
    }
    let report = engine::purge(system, state, |_| {});
    if cli.json {
        let _ = writeln!(out, "{}", serde_json::to_string_pretty(&report)?);
    } else if !cli.quiet {
        let _ = writeln!(out, "{}", report.message);
    }
    Ok(outcome_code(report.outcome))
}

fn verify(system: &WindowsSystem, state: &State, cli: &Cli, out: &mut dyn Write) -> Result<i32> {
    let plan = engine::make_plan(system, state)?;
    let report = engine::verify(system, state, &plan);
    if cli.json {
        let _ = writeln!(out, "{}", serde_json::to_string_pretty(&report)?);
    } else {
        for check in &report.checks {
            let _ = writeln!(
                out,
                "{:7} {:20} {}",
                format!("{:?}", check.status),
                check.id,
                check.detail
            );
        }
        let _ = writeln!(
            out,
            "critical checks: {}",
            if report.all_settled_pass {
                "pass"
            } else {
                "fail"
            }
        );
    }
    Ok(if report.all_settled_pass { 0 } else { 4 })
}

fn outcome_code(outcome: ApplyOutcome) -> i32 {
    match outcome {
        ApplyOutcome::Success => 0,
        ApplyOutcome::Partial => 4,
        ApplyOutcome::NeedsElevation => 3,
        _ => 1,
    }
}

#[allow(dead_code)]
fn _unused(out: &mut dyn Write) -> Result<()> {
    let _ = writeln!(out, "{}", user_locale());
    let _ = policy::IRELAND_ISO;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_stable_commands_and_global_flags() {
        let cli = Cli::try_parse_from(["eulord", "plan", "--json"]).unwrap();
        assert!(matches!(cli.command, Some(Command::Plan)));
        assert!(cli.json);

        let cli = Cli::try_parse_from(["eulord", "purge", "--yes"]).unwrap();
        assert!(matches!(cli.command, Some(Command::Purge)));
        assert!(cli.yes);

        let cli = Cli::try_parse_from(["eulord", "repair"]).unwrap();
        assert!(matches!(cli.command, Some(Command::Repair)));
    }

    #[test]
    fn parses_choice_and_override_options() {
        let cli = Cli::try_parse_from([
            "eulord",
            "--choice",
            "Copilot,WidgetsOnLock",
            "--override",
            "OfficeRecent=global",
            "apply",
        ])
        .unwrap();
        assert_eq!(cli.choices, vec!["Copilot", "WidgetsOnLock"]);
        assert_eq!(cli.overrides, vec!["OfficeRecent=global"]);
        assert!(matches!(cli.command, Some(Command::Apply)));
    }

    #[test]
    fn purge_requires_an_explicit_yes_flag() {
        let cli = Cli::try_parse_from(["eulord", "purge"]).unwrap();
        assert!(!cli.yes);
    }
}
