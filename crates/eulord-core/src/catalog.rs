use std::collections::{HashMap, HashSet};
use std::sync::LazyLock;

use serde::{Deserialize, Serialize};

use crate::error::{CoreError, Result};

pub const CATALOG_VERSION: &str = "2026-10-21 / Windows 11 build 26300";

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum PolicyClass {
    Benefit,
    Loss,
    Choice,
}

impl PolicyClass {
    pub fn label_key(self) -> &'static str {
        match self {
            Self::Benefit => "class_benefit",
            Self::Loss => "class_loss",
            Self::Choice => "class_choice",
        }
    }

    pub fn short(self) -> &'static str {
        match self {
            Self::Benefit => "GAIN",
            Self::Loss => "KEEP",
            Self::Choice => "TASTE",
        }
    }
}

#[derive(Debug, Clone, Deserialize)]
pub struct CatalogEntry {
    pub guid: String,
    pub id: String,
    #[serde(rename = "class")]
    pub class: PolicyClass,
    pub tw: String,
    pub cn: String,
    pub en: String,
}

impl CatalogEntry {
    pub fn describe(&self, lang: crate::i18n::Lang) -> &str {
        match lang {
            crate::i18n::Lang::ZhHant => &self.tw,
            crate::i18n::Lang::ZhHans => &self.cn,
            crate::i18n::Lang::En => &self.en,
        }
    }

    pub fn is_software_uninstall_unlock(&self) -> bool {
        matches!(
            self.id.as_str(),
            "EdgeUninstall" | "StoreUninstall" | "StartExperiencesUninstall"
        )
    }
}

struct CatalogIndex {
    all: Vec<CatalogEntry>,
    by_guid: HashMap<String, usize>,
    by_id: HashMap<String, usize>,
}

static INDEX: LazyLock<CatalogIndex> = LazyLock::new(|| {
    let all: Vec<CatalogEntry> = serde_json::from_str(include_str!("../assets/catalog.json"))
        .expect("embedded catalog is valid");
    let mut by_guid = HashMap::with_capacity(all.len());
    let mut by_id = HashMap::with_capacity(all.len());
    for (index, entry) in all.iter().enumerate() {
        by_guid.insert(entry.guid.to_ascii_lowercase(), index);
        by_id.insert(entry.id.clone(), index);
    }
    CatalogIndex {
        all,
        by_guid,
        by_id,
    }
});

pub fn all() -> &'static [CatalogEntry] {
    &INDEX.all
}

pub fn by_guid(guid: &str) -> Option<&'static CatalogEntry> {
    INDEX
        .by_guid
        .get(&guid.to_ascii_lowercase())
        .map(|&i| &INDEX.all[i])
}

pub fn by_id(id: &str) -> Option<&'static CatalogEntry> {
    INDEX.by_id.get(id).map(|&i| &INDEX.all[i])
}

pub fn count() -> usize {
    INDEX.all.len()
}

pub fn count_of_class(class: PolicyClass) -> usize {
    INDEX
        .all
        .iter()
        .filter(|entry| entry.class == class)
        .count()
}

pub fn class_totals() -> (usize, usize, usize) {
    (
        count_of_class(PolicyClass::Benefit),
        count_of_class(PolicyClass::Loss),
        count_of_class(PolicyClass::Choice),
    )
}

pub fn known_ids() -> HashSet<&'static str> {
    INDEX.by_id.keys().map(String::as_str).collect()
}

pub fn validate() -> Result<()> {
    let mut guids = HashSet::new();
    let mut ids = HashSet::new();
    for entry in all() {
        if !guids.insert(entry.guid.to_ascii_lowercase()) {
            return Err(CoreError::Message(format!(
                "duplicate catalog GUID: {}",
                entry.guid
            )));
        }
        if !ids.insert(entry.id.clone()) {
            return Err(CoreError::Message(format!(
                "duplicate catalog Id: {}",
                entry.id
            )));
        }
        if entry.guid.len() < 10 || entry.id.is_empty() {
            return Err(CoreError::Message(
                "catalog contains an empty identifier".to_string(),
            ));
        }
    }
    if count() != 68 {
        return Err(CoreError::Message(format!(
            "catalog has {} entries, expected 68",
            count()
        )));
    }
    Ok(())
}

pub fn software_uninstall_unlocks() -> impl Iterator<Item = &'static CatalogEntry> {
    all()
        .iter()
        .filter(|entry| entry.is_software_uninstall_unlock())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn catalog_totals_match_the_superset_contract() {
        assert_eq!(count(), 68);
        assert_eq!(count_of_class(PolicyClass::Benefit), 40);
        assert_eq!(count_of_class(PolicyClass::Loss), 7);
        assert_eq!(count_of_class(PolicyClass::Choice), 21);
        validate().unwrap();
    }

    #[test]
    fn software_uninstall_entries_are_explicitly_unlocks_only() {
        let ids: Vec<_> = software_uninstall_unlocks()
            .map(|entry| entry.id.as_str())
            .collect();
        assert!(ids.contains(&"EdgeUninstall"));
        assert!(ids.contains(&"StoreUninstall"));
        assert!(ids.iter().all(|id| by_id(id).is_some()));
    }
}
