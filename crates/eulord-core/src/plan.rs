use std::collections::{BTreeMap, HashSet};

use serde::{Deserialize, Serialize};

use crate::catalog::{self, CatalogEntry, PolicyClass};
use crate::error::Result;
use crate::policy;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum PolicyOverride {
    #[default]
    #[serde(rename = "auto")]
    Auto,
    #[serde(rename = "eea")]
    Eea,
    #[serde(rename = "global")]
    Global,
}

impl PolicyOverride {
    pub fn label(self) -> &'static str {
        match self {
            Self::Auto => "auto",
            Self::Eea => "eea",
            Self::Global => "global",
        }
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct PolicyDecision {
    pub guid: String,
    pub id: Option<String>,
    pub class: Option<PolicyClass>,
    pub global: String,
    pub eea: String,
    pub target: String,
    pub differs: bool,
    pub changed: bool,
    pub overridden: bool,
    pub unclassified: bool,
    pub software_uninstall_unlock: bool,
}

#[derive(Debug, Clone, Serialize)]
pub struct Plan {
    pub decisions: Vec<PolicyDecision>,
    pub unclassified: Vec<String>,
    pub eea_codes: Vec<String>,
    pub total_policies: usize,
    pub unique_policies: usize,
    pub differing_policies: usize,
    pub changed_policies: usize,
    pub benefits_total: usize,
    pub benefits_present: usize,
    pub benefits_taken: usize,
    pub losses_total: usize,
    pub losses_present: usize,
    pub losses_refused: usize,
    pub choices_total: usize,
    pub choices_present: usize,
    pub choices_taken: usize,
    #[serde(skip)]
    pub json: String,
}

impl Plan {
    pub fn gains(&self) -> usize {
        self.benefits_taken + self.choices_taken
    }

    pub fn gains_possible(&self) -> usize {
        self.benefits_total + self.choices_total
    }

    pub fn changed_decisions(&self) -> impl Iterator<Item = &PolicyDecision> {
        self.decisions.iter().filter(|decision| decision.changed)
    }

    pub fn decision_for_guid(&self, guid: &str) -> Option<&PolicyDecision> {
        self.decisions
            .iter()
            .find(|decision| decision.guid.eq_ignore_ascii_case(guid))
    }
}

pub fn build_plan(
    pristine_json: &str,
    iso2: &str,
    overrides: &BTreeMap<String, PolicyOverride>,
) -> Result<Plan> {
    let iso2 = if iso2.trim().len() == 2 {
        iso2.trim().to_ascii_uppercase()
    } else {
        policy::IRELAND_ISO.to_string()
    };
    let source = policy::parse(pristine_json)?;
    let snapshot = source.clone();
    let (benefits_total, losses_total, choices_total) = catalog::class_totals();
    let mut plan = Plan {
        decisions: Vec::new(),
        unclassified: Vec::new(),
        eea_codes: policy::derive_eea_codes(&snapshot),
        total_policies: policy::count(&snapshot),
        unique_policies: 0,
        differing_policies: 0,
        changed_policies: 0,
        benefits_total,
        benefits_present: 0,
        benefits_taken: 0,
        losses_total,
        losses_present: 0,
        losses_refused: 0,
        choices_total,
        choices_present: 0,
        choices_taken: 0,
        json: String::new(),
    };

    let mut seen = HashSet::new();
    if let Some(items) = policy::policies(&snapshot) {
        for policy in items {
            let Some(guid) = policy::guid(policy) else {
                continue;
            };
            if !seen.insert(guid.to_ascii_lowercase()) {
                continue;
            }
            let global = policy::default_state(policy).to_string();
            let eea = policy::state_in(policy, &iso2).to_string();
            let differs = global != eea;
            let entry: Option<&CatalogEntry> = catalog::by_guid(guid);
            let (target, rewritten, overridden) = decide(entry, &global, &eea, differs, overrides);
            let changed = rewritten && (target != global || policy::has_region_condition(policy));
            let unclassified = entry.is_none() && differs;
            match entry.map(|e| e.class) {
                Some(PolicyClass::Benefit) => {
                    plan.benefits_present += 1;
                    if differs && target == eea {
                        plan.benefits_taken += 1;
                    }
                }
                Some(PolicyClass::Loss) => {
                    plan.losses_present += 1;
                    if differs && target == global {
                        plan.losses_refused += 1;
                    }
                }
                Some(PolicyClass::Choice) => {
                    plan.choices_present += 1;
                    if differs && target == eea {
                        plan.choices_taken += 1;
                    }
                }
                None => {}
            }
            if differs {
                plan.differing_policies += 1;
            }
            if changed {
                plan.changed_policies += 1;
            }
            if unclassified {
                plan.unclassified.push(guid.to_string());
            }
            plan.decisions.push(PolicyDecision {
                guid: guid.to_string(),
                id: entry.map(|e| e.id.clone()),
                class: entry.map(|e| e.class),
                global,
                eea,
                target,
                differs,
                changed,
                overridden,
                unclassified,
                software_uninstall_unlock: entry
                    .is_some_and(CatalogEntry::is_software_uninstall_unlock),
            });
        }
    }
    plan.unique_policies = plan.decisions.len();

    let decision_by_guid: BTreeMap<String, bool> = plan
        .decisions
        .iter()
        .map(|decision| (decision.guid.to_ascii_lowercase(), decision.changed))
        .collect();
    let targets: BTreeMap<String, String> = plan
        .decisions
        .iter()
        .map(|decision| (decision.guid.to_ascii_lowercase(), decision.target.clone()))
        .collect();
    let mut root = source;
    if let Some(items) = policy::policies_mut(&mut root) {
        for policy in items.iter_mut() {
            let Some(guid) = policy::guid(policy) else {
                continue;
            };
            let key = guid.to_ascii_lowercase();
            if decision_by_guid.get(&key).copied().unwrap_or(false)
                && let Some(target) = targets.get(&key)
            {
                policy::apply_target(policy, target);
            }
        }
    }
    plan.json = policy::normalize(&root)?;
    Ok(plan)
}

fn decide(
    entry: Option<&CatalogEntry>,
    global: &str,
    eea: &str,
    differs: bool,
    overrides: &BTreeMap<String, PolicyOverride>,
) -> (String, bool, bool) {
    let Some(entry) = entry else {
        return (eea.to_string(), false, false);
    };
    if !differs {
        return (global.to_string(), false, false);
    }
    let chosen = overrides.get(&entry.id).copied().unwrap_or_default();
    let auto = match entry.class {
        PolicyClass::Benefit => eea,
        PolicyClass::Loss | PolicyClass::Choice => global,
    };
    let target = match chosen {
        PolicyOverride::Auto => auto,
        PolicyOverride::Eea => eea,
        PolicyOverride::Global => global,
    };
    (
        target.to_string(),
        true,
        chosen != PolicyOverride::Auto && target != auto,
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    const EDGE: &str = "{1bca278a-5d11-4acf-ad2f-f9ab6d7f93a6}";
    const NARRATOR: &str = "{6033b294-79ff-47eb-bbde-97b4c6479217}";
    const COPILOT: &str = "{4ac54d32-0799-405f-9bf4-1fe094cd859c}";
    const UNKNOWN: &str = "{ffffffff-0000-4000-8000-000000000001}";

    fn fixture() -> String {
        format!(
            r#"{{"$schema":"x","version":"1.0","policies":[
            {{"guid":"{EDGE}","defaultState":"disabled","conditions":{{"region":{{"enabled":["IE"]}}}}}},
            {{"guid":"{NARRATOR}","defaultState":"enabled","conditions":{{"region":{{"disabled":["IE"]}}}}}},
            {{"guid":"{COPILOT}","defaultState":"enabled","conditions":{{"region":{{"disabled":["IE"]}}}}}},
            {{"guid":"{UNKNOWN}","defaultState":"enabled","conditions":{{"region":{{"disabled":["IE"]}}}}}},
            {{"guid":"{EDGE}","defaultState":"disabled","conditions":{{"region":{{"enabled":["IE"]}}}}}}
            ]}}"#
        )
    }

    #[test]
    fn superset_plan_rewrites_classified_policies_only() {
        let plan = build_plan(&fixture(), "IE", &BTreeMap::new()).unwrap();
        assert_eq!(plan.total_policies, 5);
        assert_eq!(plan.unique_policies, 4);
        assert_eq!(plan.benefits_taken, 1);
        assert_eq!(plan.losses_refused, 1);
        assert_eq!(plan.choices_taken, 0);
        assert_eq!(plan.unclassified.len(), 1);
        assert_eq!(plan.changed_policies, 3);

        let root = policy::parse(&plan.json).unwrap();
        let items = policy::policies(&root).unwrap();
        let mut edge = 0;
        for item in items {
            match policy::guid(item).unwrap() {
                EDGE => {
                    edge += 1;
                    assert_eq!(policy::default_state(item), "enabled");
                    assert!(!policy::has_region_condition(item));
                }
                NARRATOR => {
                    assert_eq!(policy::default_state(item), "enabled");
                    assert!(!policy::has_region_condition(item));
                }
                COPILOT => {
                    assert_eq!(policy::default_state(item), "enabled");
                    assert!(!policy::has_region_condition(item));
                }
                UNKNOWN => {
                    assert_eq!(policy::default_state(item), "enabled");
                    assert!(policy::has_region_condition(item));
                }
                _ => panic!("unexpected policy"),
            }
        }
        assert_eq!(edge, 2);
    }

    #[test]
    fn choice_override_takes_the_eu_state() {
        let mut overrides = BTreeMap::new();
        overrides.insert("Copilot".to_string(), PolicyOverride::Eea);
        let plan = build_plan(&fixture(), "IE", &overrides).unwrap();
        assert_eq!(plan.choices_taken, 1);
        let decision = plan.decision_for_guid(COPILOT).unwrap();
        assert_eq!(decision.target, "disabled");
        assert!(decision.overridden);
    }

    #[test]
    fn no_diff_keeps_conditions() {
        let text = format!(
            r#"{{"policies":[{{"guid":"{EDGE}","defaultState":"enabled","conditions":{{"region":{{"enabled":["IE"]}}}}}}]}}"#
        );
        let plan = build_plan(&text, "IE", &BTreeMap::new()).unwrap();
        assert_eq!(plan.differing_policies, 0);
        let root = policy::parse(&plan.json).unwrap();
        assert!(policy::has_region_condition(
            &policy::policies(&root).unwrap()[0]
        ));
    }
}
