use std::path::{Path, PathBuf};

use crate::error::{CoreError, Result};

pub const PRODUCT_FOLDER: &str = "OneClickEULord";
pub const TASK_NAME: &str = "OneClickEULord-Persist";
pub const EXE_NAME: &str = "eulord.exe";
pub const POLICY_FILE_NAME: &str = "IntegratedServicesRegionPolicySet.json";

#[derive(Debug, Clone)]
pub struct Paths {
    pub data_root: PathBuf,
    pub policy_file: PathBuf,
    pub system_dir: PathBuf,
}

impl Paths {
    pub fn discover(data_root: Option<PathBuf>, policy_file: Option<PathBuf>) -> Result<Self> {
        let system_dir = system_directory();
        let data_root = data_root
            .or_else(|| std::env::var_os("EULORD_DATA_DIR").map(PathBuf::from))
            .unwrap_or_else(default_data_root);
        let policy_file = policy_file.unwrap_or_else(|| system_dir.join(POLICY_FILE_NAME));
        if data_root.as_os_str().is_empty() {
            return Err(CoreError::Message("the data folder is empty".to_string()));
        }
        Ok(Self {
            data_root,
            policy_file,
            system_dir,
        })
    }

    pub fn system_default() -> Result<Self> {
        Self::discover(None, None)
    }

    pub fn ensure_layout(&self) -> Result<()> {
        std::fs::create_dir_all(&self.data_root)?;
        std::fs::create_dir_all(self.backup_dir())?;
        std::fs::create_dir_all(self.log_dir())?;
        std::fs::create_dir_all(self.journal_dir())?;
        Ok(())
    }

    pub fn state_file(&self) -> PathBuf {
        self.data_root.join("state.json")
    }

    pub fn export_file(&self) -> PathBuf {
        self.data_root.join("eulord-export.json")
    }

    pub fn backup_dir(&self) -> PathBuf {
        self.data_root.join("backups")
    }

    pub fn journal_dir(&self) -> PathBuf {
        self.data_root.join("journal")
    }

    pub fn log_dir(&self) -> PathBuf {
        self.data_root.join("logs")
    }

    pub fn log_file(&self) -> PathBuf {
        self.log_dir().join("eulord.log")
    }

    pub fn original_backup(&self) -> PathBuf {
        self.backup_dir().join("policy.original.json")
    }

    pub fn pristine_backup(&self) -> PathBuf {
        self.backup_dir().join("policy.pristine.json")
    }

    pub fn backup_manifest(&self) -> PathBuf {
        self.backup_dir().join("manifest.json")
    }

    pub fn installed_exe(&self) -> PathBuf {
        self.data_root.join(EXE_NAME)
    }

    pub fn task_definition_path(&self) -> PathBuf {
        self.system_dir.join("Tasks").join(TASK_NAME)
    }

    pub fn current_exe() -> Result<PathBuf> {
        std::env::current_exe().map_err(CoreError::from)
    }

    pub fn running_from_data_root(&self) -> bool {
        let Ok(current) = Self::current_exe() else {
            return false;
        };
        same_path(&current, &self.installed_exe())
    }

    pub fn legacy_data_roots() -> Vec<PathBuf> {
        let base = common_app_data();
        ["OneClickEUGrandpa", "OneClickEUOverlord"]
            .into_iter()
            .map(|name| base.join(name))
            .collect()
    }
}

pub fn same_path(a: &Path, b: &Path) -> bool {
    let normalize = |p: &Path| {
        let text = p.to_string_lossy().replace('/', "\\");
        if cfg!(windows) {
            text.to_ascii_lowercase()
        } else {
            text
        }
    };
    normalize(a) == normalize(b)
}

fn common_app_data() -> PathBuf {
    std::env::var_os("ProgramData")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(r"C:\ProgramData"))
}

fn default_data_root() -> PathBuf {
    common_app_data().join(PRODUCT_FOLDER)
}

fn system_directory() -> PathBuf {
    let windows = std::env::var_os("SystemRoot")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(r"C:\Windows"));
    windows.join("System32")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn data_layout_is_under_one_root() {
        let root = std::env::temp_dir().join("eulord-paths-test");
        let paths = Paths::discover(Some(root.clone()), None).unwrap();
        assert_eq!(paths.state_file(), root.join("state.json"));
        assert_eq!(
            paths.pristine_backup(),
            root.join("backups").join("policy.pristine.json")
        );
        assert!(paths.policy_file.ends_with(POLICY_FILE_NAME));
    }
}
