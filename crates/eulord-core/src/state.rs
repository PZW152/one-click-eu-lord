use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};

use crate::catalog;
use crate::error::{CoreError, Result};
use crate::i18n::LangPref;
use crate::plan::PolicyOverride;
use crate::policy::IRELAND_GEOID;
use crate::policy::IRELAND_ISO;

pub const STATE_SCHEMA_VERSION: u32 = 3;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UiPrefs {
    pub crt_scanlines: bool,
    pub expert_mode: bool,
    pub first_run_done: bool,
    pub page: String,
}

impl Default for UiPrefs {
    fn default() -> Self {
        Self {
            crt_scanlines: true,
            expert_mode: false,
            first_run_done: false,
            page: "overview".to_string(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct StepResult {
    pub id: String,
    pub status: String,
    pub detail: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct ApplyRecord {
    pub started_utc: String,
    pub finished_utc: String,
    pub outcome: String,
    pub steps: Vec<StepResult>,
    pub plan_hash: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct State {
    pub schema_version: u32,
    pub product_version: String,
    pub language_preference: LangPref,
    pub region_iso: String,
    pub region_geoid: u32,
    pub original_region_present: bool,
    pub original_region_geoid: Option<u32>,
    pub original_region_from_legacy: bool,
    pub applied_utc: Option<String>,
    pub last_policy_hash: Option<String>,
    pub overrides: BTreeMap<String, PolicyOverride>,
    pub unclassified: Vec<String>,
    pub ui: UiPrefs,
    pub last_apply: Option<ApplyRecord>,
}

impl Default for State {
    fn default() -> Self {
        Self {
            schema_version: STATE_SCHEMA_VERSION,
            product_version: env!("CARGO_PKG_VERSION").to_string(),
            language_preference: LangPref::Auto,
            region_iso: IRELAND_ISO.to_string(),
            region_geoid: IRELAND_GEOID,
            original_region_present: false,
            original_region_geoid: None,
            original_region_from_legacy: false,
            applied_utc: None,
            last_policy_hash: None,
            overrides: BTreeMap::new(),
            unclassified: Vec::new(),
            ui: UiPrefs::default(),
            last_apply: None,
        }
    }
}

impl State {
    pub fn ever_applied(&self) -> bool {
        self.applied_utc.is_some()
    }

    pub fn set_override(&mut self, id: &str, value: PolicyOverride) {
        if value == PolicyOverride::Auto {
            self.overrides.remove(id);
        } else {
            self.overrides.insert(id.to_string(), value);
        }
    }

    pub fn override_for(&self, id: &str) -> PolicyOverride {
        self.overrides.get(id).copied().unwrap_or_default()
    }

    pub fn selected_choices(&self) -> Vec<String> {
        catalog::all()
            .iter()
            .filter(|entry| entry.class == crate::catalog::PolicyClass::Choice)
            .filter(|entry| self.override_for(&entry.id) == PolicyOverride::Eea)
            .map(|entry| entry.id.clone())
            .collect()
    }

    pub fn remember_region(&mut self, current: Option<u32>) {
        if self.ever_applied() {
            return;
        }
        if let Some(value) = current {
            self.original_region_present = true;
            self.original_region_geoid = Some(value);
            self.original_region_from_legacy = false;
        } else {
            self.original_region_present = false;
            self.original_region_geoid = None;
            self.original_region_from_legacy = false;
        }
    }

    #[allow(clippy::field_reassign_with_default)]
    pub fn from_json(text: &str) -> Result<Self> {
        let root: Value = serde_json::from_str(text.trim_start_matches('\u{feff}'))?;
        let Some(obj) = root.as_object() else {
            return Err(CoreError::State(
                "state.json root is not an object".to_string(),
            ));
        };
        let mut state = Self::default();
        state.schema_version = get_u32(obj, "schemaVersion").unwrap_or(1).max(1);
        state.product_version = get_str(obj, "productVersion")
            .filter(|value| !value.is_empty())
            .unwrap_or_else(|| env!("CARGO_PKG_VERSION").to_string());
        state.language_preference = get_str(obj, "languagePreference")
            .and_then(|v| parse_lang_pref(&v))
            .unwrap_or(LangPref::Auto);
        state.region_iso = get_str(obj, "regionIso").unwrap_or_else(|| IRELAND_ISO.to_string());
        state.region_geoid = get_u32(obj, "regionGeoId").unwrap_or(IRELAND_GEOID);
        state.original_region_present = get_bool(obj, "originalRegionPresent").unwrap_or(false);
        state.original_region_geoid = get_u32(obj, "originalRegionGeoId");
        state.original_region_from_legacy =
            get_bool(obj, "originalRegionFromLegacy").unwrap_or(false);
        state.applied_utc = get_str(obj, "appliedUtc");
        state.last_policy_hash = get_str(obj, "lastPolicyHash");
        state.unclassified = get_string_array(obj, "unclassified");
        if state.unclassified.is_empty() {
            state.unclassified = get_string_array(obj, "unclassifiedPolicies");
        }
        if let Some(overrides) = obj.get("overrides").and_then(Value::as_object) {
            for (id, value) in overrides {
                if let Some(value) = value.as_str().and_then(parse_override) {
                    state.overrides.insert(id.clone(), value);
                }
            }
        }
        let v3_format = obj.get("ui").is_some();
        if !v3_format {
            state.overrides.clear();
            state.applied_utc = None;
            state.last_policy_hash = None;
        }
        if let Some(ui) = obj.get("ui").and_then(Value::as_object) {
            state.ui.crt_scanlines = get_bool(ui, "crtScanlines").unwrap_or(true);
            state.ui.expert_mode = get_bool(ui, "expertMode").unwrap_or(false);
            state.ui.first_run_done = get_bool(ui, "firstRunDone").unwrap_or(false);
            state.ui.page = get_str(ui, "page").unwrap_or_else(|| "overview".to_string());
        }
        state.last_apply = obj
            .get("lastApply")
            .cloned()
            .and_then(|value| serde_json::from_value(value).ok());
        state.schema_version = STATE_SCHEMA_VERSION;
        Ok(state)
    }

    pub fn to_json(&self) -> Result<String> {
        let mut text = serde_json::to_string_pretty(self)?;
        text.push('\n');
        Ok(text)
    }
}

fn get_str(obj: &Map<String, Value>, key: &str) -> Option<String> {
    obj.get(key)
        .and_then(Value::as_str)
        .map(ToString::to_string)
}

fn get_bool(obj: &Map<String, Value>, key: &str) -> Option<bool> {
    obj.get(key).and_then(Value::as_bool)
}

fn get_u32(obj: &Map<String, Value>, key: &str) -> Option<u32> {
    obj.get(key)
        .and_then(|value| value.as_u64().map(|n| n as u32))
}

fn get_string_array(obj: &Map<String, Value>, key: &str) -> Vec<String> {
    obj.get(key)
        .and_then(Value::as_array)
        .map(|items| {
            items
                .iter()
                .filter_map(Value::as_str)
                .map(ToString::to_string)
                .collect()
        })
        .unwrap_or_default()
}

fn parse_override(value: &str) -> Option<PolicyOverride> {
    match value.to_ascii_lowercase().as_str() {
        "auto" => Some(PolicyOverride::Auto),
        "eea" | "eu" => Some(PolicyOverride::Eea),
        "global" => Some(PolicyOverride::Global),
        _ => None,
    }
}

fn parse_lang_pref(value: &str) -> Option<LangPref> {
    match value {
        "auto" => Some(LangPref::Auto),
        "zh-Hant" | "zh-TW" => Some(LangPref::ZhHant),
        "zh-Hans" | "zh-CN" => Some(LangPref::ZhHans),
        "en" => Some(LangPref::En),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn v2_state_is_imported() {
        let text = r#"{"schemaVersion":3,"productVersion":"2.0.1","languagePreference":"zh-Hans","regionIso":"IE","regionGeoId":68,"originalRegionPresent":true,"originalRegionGeoId":244,"appliedUtc":"2026-10-21T00:00:00Z","overrides":{"Copilot":"eea"},"unclassified":["{x}"]}"#;
        let state = State::from_json(text).unwrap();
        assert_eq!(state.language_preference, LangPref::ZhHans);
        assert_eq!(state.original_region_geoid, Some(244));
        assert!(!state.ever_applied());
        assert_eq!(state.override_for("Copilot"), PolicyOverride::Auto);
    }

    #[test]
    fn state_roundtrips_as_json() {
        let mut state = State::default();
        state.set_override("Copilot", PolicyOverride::Eea);
        let text = state.to_json().unwrap();
        let loaded = State::from_json(&text).unwrap();
        assert_eq!(loaded.override_for("Copilot"), PolicyOverride::Eea);
        assert_eq!(loaded.region_geoid, IRELAND_GEOID);
    }
}
