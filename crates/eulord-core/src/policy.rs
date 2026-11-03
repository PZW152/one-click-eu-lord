use std::collections::BTreeSet;

use serde_json::{Map, Value};

use crate::error::{CoreError, Result};

pub const ENABLED: &str = "enabled";
pub const DISABLED: &str = "disabled";
pub const IRELAND_ISO: &str = "IE";
pub const IRELAND_GEOID: u32 = 68;

pub const FALLBACK_EEA_CODES: &[&str] = &[
    "AT", "BE", "BG", "CH", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GF", "GP", "GR", "HR",
    "HU", "IE", "IS", "IT", "LI", "LT", "LU", "LV", "MT", "MQ", "NL", "NO", "PL", "PT", "RE", "RO",
    "SE", "SI", "SK", "YT",
];

pub fn parse(text: &str) -> Result<Value> {
    let root: Value = serde_json::from_str(text.trim_start_matches('\u{feff}'))?;
    if !root.is_object() {
        return Err(CoreError::Policy(
            "the policy file root is not an object".to_string(),
        ));
    }
    if policies(&root).is_none() {
        return Err(CoreError::Policy(
            "the policy file has no policies array".to_string(),
        ));
    }
    Ok(root)
}

pub fn policies(root: &Value) -> Option<&Vec<Value>> {
    root.get("policies").and_then(Value::as_array)
}

pub fn policies_mut(root: &mut Value) -> Option<&mut Vec<Value>> {
    root.get_mut("policies").and_then(Value::as_array_mut)
}

pub fn guid(policy: &Value) -> Option<&str> {
    policy.get("guid").and_then(Value::as_str)
}

pub fn default_state(policy: &Value) -> &str {
    policy
        .get("defaultState")
        .and_then(Value::as_str)
        .unwrap_or(DISABLED)
}

pub fn state_in<'a>(policy: &'a Value, iso2: &str) -> &'a str {
    let fallback = default_state(policy);
    let Some(region) = policy
        .get("conditions")
        .and_then(|v| v.get("region"))
        .and_then(Value::as_object)
    else {
        return fallback;
    };
    if list_contains(region.get(ENABLED), iso2) {
        return ENABLED;
    }
    if list_contains(region.get(DISABLED), iso2) {
        return DISABLED;
    }
    fallback
}

fn list_contains(value: Option<&Value>, iso2: &str) -> bool {
    value
        .and_then(Value::as_array)
        .map(|items| {
            items.iter().any(|item| {
                item.as_str()
                    .is_some_and(|code| code.eq_ignore_ascii_case(iso2))
            })
        })
        .unwrap_or(false)
}

pub fn has_region_condition(policy: &Value) -> bool {
    policy
        .get("conditions")
        .and_then(|v| v.get("region"))
        .is_some()
}

pub fn is_windows_original(root: &Value) -> bool {
    policies(root).is_some_and(|items| !items.is_empty() && items.iter().any(has_region_condition))
}

pub fn derive_eea_codes(root: &Value) -> Vec<String> {
    let mut best: Vec<String> = Vec::new();
    if let Some(items) = policies(root) {
        for policy in items {
            let Some(region) = policy
                .get("conditions")
                .and_then(|v| v.get("region"))
                .and_then(Value::as_object)
            else {
                continue;
            };
            for key in [ENABLED, DISABLED] {
                let Some(codes) = region.get(key).and_then(Value::as_array) else {
                    continue;
                };
                let mut current: Vec<String> = codes
                    .iter()
                    .filter_map(Value::as_str)
                    .map(|code| code.to_ascii_uppercase())
                    .collect();
                if current.iter().any(|code| code == IRELAND_ISO) && current.len() > best.len() {
                    current.sort();
                    current.dedup();
                    best = current;
                }
            }
        }
    }
    if best.is_empty() {
        best = FALLBACK_EEA_CODES.iter().map(|s| s.to_string()).collect();
    }
    best
}

pub fn normalize(root: &Value) -> Result<String> {
    let mut text = serde_json::to_string_pretty(root)?;
    text.push('\n');
    Ok(text)
}

/// Rewrites one classified policy. Returns true when the document actually changed.
pub fn apply_target(policy: &mut Value, target: &str) -> bool {
    let Some(object) = policy.as_object_mut() else {
        return false;
    };
    let before = object
        .get("defaultState")
        .and_then(Value::as_str)
        .unwrap_or(DISABLED)
        .to_string();
    let mut removed_region = false;
    if let Some(conditions) = object.get_mut("conditions").and_then(Value::as_object_mut) {
        removed_region = conditions.remove("region").is_some();
        if conditions.is_empty() {
            object.remove("conditions");
        }
    }
    object.insert(
        "defaultState".to_string(),
        Value::String(target.to_string()),
    );
    before != target || removed_region
}

pub fn state_of_guid<'a>(root: &'a Value, wanted: &str) -> Option<&'a str> {
    policies(root)?.iter().find_map(|policy| {
        if guid(policy).is_some_and(|g| g.eq_ignore_ascii_case(wanted)) {
            Some(default_state(policy))
        } else {
            None
        }
    })
}

pub fn count(root: &Value) -> usize {
    policies(root).map(Vec::len).unwrap_or(0)
}

pub fn all_region_codes(root: &Value) -> Vec<String> {
    let mut codes = BTreeSet::new();
    if let Some(items) = policies(root) {
        for policy in items {
            let Some(region) = policy
                .get("conditions")
                .and_then(|v| v.get("region"))
                .and_then(Value::as_object)
            else {
                continue;
            };
            for key in [ENABLED, DISABLED] {
                if let Some(list) = region.get(key).and_then(Value::as_array) {
                    for code in list.iter().filter_map(Value::as_str) {
                        codes.insert(code.to_ascii_uppercase());
                    }
                }
            }
        }
    }
    codes.into_iter().collect()
}

pub fn obj(value: &Value) -> Option<&Map<String, Value>> {
    value.as_object()
}

#[cfg(test)]
mod tests {
    use super::*;

    const SIMPLE: &str = r#"{
      "$schema": "x",
      "version": "1.0",
      "policies": [
        {"guid":"{a}","defaultState":"disabled","conditions":{"region":{"enabled":["IE","DE"]}}},
        {"guid":"{b}","defaultState":"enabled","conditions":{"region":{"disabled":["IE"]},"other":{"keep":true}}},
        {"guid":"{c}","defaultState":"enabled"}
      ]
    }"#;

    #[test]
    fn state_is_resolved_for_ireland() {
        let root = parse(SIMPLE).unwrap();
        let items = policies(&root).unwrap();
        assert_eq!(state_in(&items[0], IRELAND_ISO), ENABLED);
        assert_eq!(state_in(&items[1], IRELAND_ISO), DISABLED);
        assert_eq!(state_in(&items[2], IRELAND_ISO), ENABLED);
    }

    #[test]
    fn eea_codes_are_derived_from_the_longest_irish_list() {
        let root = parse(SIMPLE).unwrap();
        let codes = derive_eea_codes(&root);
        assert_eq!(codes, vec!["DE".to_string(), "IE".to_string()]);
    }

    #[test]
    fn rewrite_strips_only_the_region_condition() {
        let root = parse(SIMPLE).unwrap();
        let mut policy = policies(&root).unwrap()[1].clone();
        assert!(apply_target(&mut policy, ENABLED));
        assert_eq!(default_state(&policy), ENABLED);
        assert!(
            policy
                .get("conditions")
                .and_then(|v| v.get("region"))
                .is_none()
        );
        assert_eq!(policy["conditions"]["other"]["keep"], Value::Bool(true));
    }

    #[test]
    fn original_detection_requires_a_region_condition() {
        let root = parse(SIMPLE).unwrap();
        assert!(is_windows_original(&root));
        let mut changed = root.clone();
        for policy in policies_mut(&mut changed).unwrap() {
            if let Some(obj) = policy.as_object_mut() {
                obj.remove("conditions");
            }
        }
        assert!(!is_windows_original(&changed));
    }
}
