#![allow(non_snake_case)]

use std::ffi::OsStr;
use std::os::windows::ffi::OsStrExt;
use std::path::Path;

use eulord_core::compat::{CompatLevel, OsInfo, classify};
use eulord_core::error::{CoreError, Result};
use eulord_core::paths::{Paths, TASK_NAME, same_path};
use eulord_core::plan::Plan;
use eulord_core::policy;
use eulord_core::state::State;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use winreg::RegKey;
use winreg::enums::*;

use std::collections::HashMap;
use std::sync::{LazyLock, Mutex};
use windows::Win32::Foundation::{CloseHandle, HANDLE, HLOCAL, LocalFree};
use windows::Win32::Globalization::{
    EnumSystemGeoID, GEO_ISO2, GEOCLASS_NATION, GetGeoInfoW, GetUserDefaultLocaleName, GetUserGeoID,
};
use windows::Win32::Security::Authorization::{
    ConvertStringSecurityDescriptorToSecurityDescriptorW, SDDL_REVISION_1,
};
use windows::Win32::Security::{
    DACL_SECURITY_INFORMATION, GetTokenInformation, PSECURITY_DESCRIPTOR, SetFileSecurityW,
    TOKEN_ELEVATION, TOKEN_QUERY, TokenElevation,
};
use windows::Win32::Storage::FileSystem::{
    MOVEFILE_REPLACE_EXISTING, MOVEFILE_WRITE_THROUGH, MoveFileExW, REPLACEFILE_WRITE_THROUGH,
    ReplaceFileW,
};
use windows::Win32::System::Com::{
    CLSCTX_INPROC_SERVER, COINIT_MULTITHREADED, CoCreateInstance, CoInitializeEx,
};
use windows::Win32::System::Restore::{
    BEGIN_SYSTEM_CHANGE, MODIFY_SETTINGS, RESTOREPOINTINFOW, SRSetRestorePointW, STATEMGRSTATUS,
};
use windows::Win32::System::TaskScheduler::{
    CLSID_CTaskScheduler, ITaskService, TASK_CREATE_OR_UPDATE, TASK_LOGON_SERVICE_ACCOUNT,
};
use windows::Win32::System::Threading::{
    GetCurrentProcess, GetExitCodeProcess, OpenProcessToken, WaitForSingleObject,
};
use windows::Win32::System::Variant::VARIANT;
use windows::Win32::UI::Shell::{
    SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW, ShellExecuteExW, ShellExecuteW,
};
use windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;
use windows::core::BOOL;
use windows::core::{BSTR, PCWSTR};

fn fallback_region() -> Option<u32> {
    let geoid = unsafe { GetUserGeoID(GEOCLASS_NATION) };
    if geoid > 0 { Some(geoid as u32) } else { None }
}

const REGION_KEY: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel";
const REGION_VALUE: &str = "DeviceRegion";
const WINDOWS_VERSION_KEY: &str = r"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum PolicyOrigin {
    WindowsOriginal,
    Ours,
    Missing,
    Unreadable,
}

impl PolicyOrigin {
    pub fn is_applied(self) -> bool {
        matches!(self, Self::Ours)
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct BackupOutcome {
    pub original_created: bool,
    pub pristine_updated: bool,
    pub source_available: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum TaskStatus {
    Ready,
    Absent,
    Disabled,
    Stale,
    Unknown,
}

impl TaskStatus {
    pub fn label(self) -> &'static str {
        match self {
            Self::Ready => "Ready",
            Self::Absent => "Absent",
            Self::Disabled => "Disabled",
            Self::Stale => "Stale",
            Self::Unknown => "Unknown",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum CheckStatus {
    Pass,
    Pending,
    Unknown,
    Fail,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VerifyCheck {
    pub id: String,
    pub status: CheckStatus,
    pub detail: String,
    pub critical: bool,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct VerifyReport {
    pub checks: Vec<VerifyCheck>,
    pub all_settled_pass: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct BackupManifest {
    schema_version: u32,
    product_version: String,
    original_sha256: Option<String>,
    pristine_sha256: Option<String>,
    updated_utc: String,
    source_build: u32,
    source_policy_count: usize,
}

pub struct WindowsSystem {
    pub paths: Paths,
}

impl WindowsSystem {
    pub fn new(paths: Paths) -> Self {
        Self { paths }
    }

    pub fn ensure_layout(&self) -> Result<()> {
        self.paths.ensure_layout()?;
        if self.is_elevated() {
            harden_directory(&self.paths.data_root)?;
        }
        Ok(())
    }

    pub fn is_elevated(&self) -> bool {
        unsafe {
            let mut token = HANDLE::default();
            if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token).is_err() {
                return false;
            }
            let mut elevation = TOKEN_ELEVATION::default();
            let mut returned = 0u32;
            let ok = GetTokenInformation(
                token,
                TokenElevation,
                Some(&mut elevation as *mut _ as *mut _),
                std::mem::size_of::<TOKEN_ELEVATION>() as u32,
                &mut returned,
            )
            .is_ok();
            let _ = CloseHandle(token);
            ok && elevation.TokenIsElevated != 0
        }
    }

    pub fn os_info(&self) -> OsInfo {
        let mut info = OsInfo {
            architecture: std::env::consts::ARCH.to_string(),
            ..Default::default()
        };
        let Ok(key) = RegKey::predef(HKEY_LOCAL_MACHINE)
            .open_subkey_with_flags(WINDOWS_VERSION_KEY, KEY_READ | KEY_WOW64_64KEY)
        else {
            info.compat = CompatLevel::Unknown;
            return info;
        };
        info.product_name = key
            .get_value::<String, _>("ProductName")
            .unwrap_or_default();
        info.display_version = key
            .get_value::<String, _>("DisplayVersion")
            .unwrap_or_default();
        info.build = key
            .get_value::<String, _>("CurrentBuildNumber")
            .ok()
            .and_then(|s| s.parse().ok())
            .unwrap_or(0);
        info.ubr = key.get_value::<u32, _>("UBR").unwrap_or(0);
        info.major = key
            .get_value::<u32, _>("CurrentMajorVersionNumber")
            .unwrap_or(10);
        info.minor = key
            .get_value::<u32, _>("CurrentMinorVersionNumber")
            .unwrap_or(0);
        if info.build >= 22000 {
            info.product_name = "Windows 11".to_string();
        }
        info.compat = classify(info.build);
        info
    }

    pub fn install_self(&self) -> Result<()> {
        self.ensure_layout()?;
        let source = Paths::current_exe()?;
        let target = self.paths.installed_exe();
        if same_path(&source, &target) {
            return Ok(());
        }
        if target.exists() {
            let current = std::fs::read(&source)?;
            let installed = std::fs::read(&target)?;
            if current == installed {
                return Ok(());
            }
        }
        let temp = target.with_extension(format!("new-{}", std::process::id()));
        std::fs::copy(&source, &temp)?;
        replace_file(&target, &temp)?;
        Ok(())
    }

    pub fn read_state(&self) -> Result<State> {
        let path = self.paths.state_file();
        if !path.exists() {
            return Ok(State::default());
        }
        let text = std::fs::read_to_string(&path)?;
        State::from_json(&text)
    }

    pub fn save_state(&self, state: &State) -> Result<()> {
        self.ensure_layout()?;
        let text = state.to_json()?;
        atomic_write(&self.paths.state_file(), text.as_bytes())
    }

    pub fn read_policy(&self) -> Result<Option<String>> {
        match std::fs::read_to_string(&self.paths.policy_file) {
            Ok(text) => Ok(Some(text)),
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(err) => Err(err.into()),
        }
    }

    pub fn classify_policy(&self, state: &State) -> PolicyOrigin {
        let Ok(Some(text)) = self.read_policy() else {
            return if self.paths.policy_file.exists() {
                PolicyOrigin::Unreadable
            } else {
                PolicyOrigin::Missing
            };
        };
        let hash = policy_hash(&text);
        if state.last_policy_hash.as_deref() == Some(hash.as_str()) {
            return PolicyOrigin::Ours;
        }
        let Ok(root) = policy::parse(&text) else {
            return PolicyOrigin::Unreadable;
        };
        if policy::is_windows_original(&root) {
            PolicyOrigin::WindowsOriginal
        } else {
            PolicyOrigin::Ours
        }
    }

    pub fn backup_policy(&self) -> Result<BackupOutcome> {
        self.ensure_layout()?;
        let Some(text) = self.read_policy()? else {
            return Ok(BackupOutcome::default());
        };
        let root = match policy::parse(&text) {
            Ok(root) => root,
            Err(_) => return Ok(BackupOutcome::default()),
        };
        if !policy::is_windows_original(&root) {
            return Ok(BackupOutcome {
                original_created: false,
                pristine_updated: false,
                source_available: self.pristine_source()?.is_some(),
            });
        }
        let hash = policy_hash(&text);
        let original = self.paths.original_backup();
        let pristine = self.paths.pristine_backup();
        let mut outcome = BackupOutcome {
            source_available: true,
            ..Default::default()
        };
        if !original.exists() {
            atomic_write(&original, text.as_bytes())?;
            outcome.original_created = true;
        }
        let needs_pristine = match std::fs::read_to_string(&pristine) {
            Ok(existing) => policy_hash(&existing) != hash,
            Err(_) => true,
        };
        if needs_pristine {
            atomic_write(&pristine, text.as_bytes())?;
            outcome.pristine_updated = true;
        }
        let manifest = BackupManifest {
            schema_version: 1,
            product_version: eulord_core::APP_VERSION.to_string(),
            original_sha256: std::fs::read(&original).ok().map(|b| sha256_bytes(&b)),
            pristine_sha256: std::fs::read(&pristine).ok().map(|b| sha256_bytes(&b)),
            updated_utc: now_utc(),
            source_build: self.os_info().build,
            source_policy_count: policy::count(&root),
        };
        let manifest_text = serde_json::to_string_pretty(&manifest)? + "\n";
        atomic_write(&self.paths.backup_manifest(), manifest_text.as_bytes())?;
        Ok(outcome)
    }

    pub fn pristine_source(&self) -> Result<Option<String>> {
        if let Some(text) = self.read_policy()?
            && policy::parse(&text)
                .map(|root| policy::is_windows_original(&root))
                .unwrap_or(false)
        {
            return Ok(Some(text));
        }
        for path in [self.paths.pristine_backup(), self.paths.original_backup()] {
            let Ok(text) = std::fs::read_to_string(path) else {
                continue;
            };
            if policy::parse(&text)
                .map(|root| policy::is_windows_original(&root))
                .unwrap_or(false)
            {
                return Ok(Some(text));
            }
        }
        Ok(None)
    }

    pub fn write_policy(&self, text: &str) -> Result<()> {
        let root = policy::parse(text)?;
        let expected = policy::count(&root);
        atomic_write(&self.paths.policy_file, text.as_bytes())?;
        let written = std::fs::read_to_string(&self.paths.policy_file)?;
        let root = policy::parse(&written)?;
        if policy::count(&root) != expected {
            return Err(CoreError::Policy(format!(
                "policy file read-back contains {} entries, expected {}",
                policy::count(&root),
                expected
            )));
        }
        Ok(())
    }

    pub fn restore_policy(&self) -> Result<()> {
        let text = self.pristine_source()?.ok_or_else(|| {
            CoreError::Policy("no pristine Windows policy backup is available".to_string())
        })?;
        policy::parse(&text)?;
        atomic_write(&self.paths.policy_file, text.as_bytes())?;
        let written = std::fs::read_to_string(&self.paths.policy_file)?;
        policy::parse(&written)?;
        Ok(())
    }

    pub fn read_region(&self) -> Result<Option<u32>> {
        let key = match RegKey::predef(HKEY_LOCAL_MACHINE)
            .open_subkey_with_flags(REGION_KEY, KEY_READ | KEY_WOW64_64KEY)
        {
            Ok(key) => key,
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(fallback_region()),
            Err(err) => return Err(err.into()),
        };
        match key.get_value::<u32, _>(REGION_VALUE) {
            Ok(value) => Ok(Some(value)),
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(fallback_region()),
            Err(err) => Err(err.into()),
        }
    }

    pub fn write_region(&self, geoid: u32) -> Result<()> {
        let (key, _) = RegKey::predef(HKEY_LOCAL_MACHINE)
            .create_subkey_with_flags(REGION_KEY, KEY_SET_VALUE | KEY_WOW64_64KEY)?;
        key.set_value(REGION_VALUE, &geoid)?;
        let actual = self.read_region()?;
        if actual != Some(geoid) {
            return Err(CoreError::Message(format!(
                "DeviceRegion read-back was {:?}, expected {}",
                actual, geoid
            )));
        }
        Ok(())
    }

    pub fn delete_region(&self) -> Result<()> {
        let key = match RegKey::predef(HKEY_LOCAL_MACHINE)
            .open_subkey_with_flags(REGION_KEY, KEY_SET_VALUE | KEY_WOW64_64KEY)
        {
            Ok(key) => key,
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(()),
            Err(err) => return Err(err.into()),
        };
        match key.delete_value(REGION_VALUE) {
            Ok(()) => Ok(()),
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(err) => Err(err.into()),
        }
    }

    pub fn open_uri(&self, uri: &str) -> Result<()> {
        let wide = wide_str(uri);
        let result = unsafe {
            ShellExecuteW(
                None,
                windows::core::w!("open"),
                PCWSTR::from_raw(wide.as_ptr()),
                PCWSTR::null(),
                PCWSTR::null(),
                SW_SHOWNORMAL,
            )
        };
        if result.0 as usize <= 32 {
            return Err(CoreError::Message(format!("ShellExecute failed for {uri}")));
        }
        Ok(())
    }

    pub fn policy_hash(text: &str) -> String {
        policy_hash(text)
    }

    pub fn open_path(&self, path: &Path) -> Result<()> {
        if !path.exists() {
            return Err(CoreError::Message(format!(
                "path does not exist: {}",
                path.display()
            )));
        }
        let wide = wide_path(path);
        let result = unsafe {
            ShellExecuteW(
                None,
                windows::core::w!("open"),
                PCWSTR::from_raw(wide.as_ptr()),
                PCWSTR::null(),
                PCWSTR::null(),
                SW_SHOWNORMAL,
            )
        };
        if result.0 as usize <= 32 {
            return Err(CoreError::Message(format!(
                "ShellExecute failed for {}",
                path.display()
            )));
        }
        Ok(())
    }
}
impl WindowsSystem {
    pub fn task_status(&self) -> TaskStatus {
        match task_status_via_com(&self.paths.installed_exe()) {
            Ok(status) => status,
            Err(_) => task_status_from_file(&self.paths),
        }
    }

    pub fn install_task(&self) -> Result<()> {
        self.install_self()?;
        let xml = build_task_xml(&self.paths.installed_exe());
        let service = connect_task_service()?;
        let folder = unsafe { service.GetFolder(&BSTR::from("\\")) }.map_err(task_error)?;
        let empty = VARIANT::default();
        unsafe {
            folder.RegisterTask(
                &BSTR::from(TASK_NAME),
                &BSTR::from(xml.as_str()),
                TASK_CREATE_OR_UPDATE.0,
                &empty,
                &empty,
                TASK_LOGON_SERVICE_ACCOUNT,
                &empty,
            )
        }
        .map_err(task_error)?;
        Ok(())
    }

    pub fn remove_task(&self) -> Result<()> {
        if self.task_status() == TaskStatus::Absent {
            return Ok(());
        }
        let service = connect_task_service()?;
        let folder = unsafe { service.GetFolder(&BSTR::from("\\")) }.map_err(task_error)?;
        let result = unsafe { folder.DeleteTask(&BSTR::from(TASK_NAME), 0) };
        if let Err(err) = result
            && self.task_status() != TaskStatus::Absent
        {
            return Err(task_error(err));
        }
        Ok(())
    }

    #[allow(clippy::field_reassign_with_default)]
    pub fn create_restore_point(&self, description: &str) -> Result<()> {
        let mut spec = RESTOREPOINTINFOW::default();
        spec.dwEventType = BEGIN_SYSTEM_CHANGE;
        spec.dwRestorePtType = MODIFY_SETTINGS;
        let units: Vec<u16> = description.encode_utf16().take(255).collect();
        unsafe {
            std::ptr::copy_nonoverlapping(
                units.as_ptr(),
                std::ptr::addr_of_mut!(spec.szDescription).cast::<u16>(),
                units.len(),
            )
        };
        let mut status = STATEMGRSTATUS::default();
        let ok = unsafe { SRSetRestorePointW(&spec, &mut status) };
        if !ok.as_bool() {
            let code = unsafe { std::ptr::read_unaligned(std::ptr::addr_of!(status.nStatus)) };
            return Err(CoreError::Message(format!(
                "System Restore returned error {}",
                code.0
            )));
        }
        Ok(())
    }

    pub fn verify(&self, state: &State, plan: &Plan) -> VerifyReport {
        let mut report = VerifyReport::default();
        let region = self.read_region().ok().flatten();
        report.checks.push(VerifyCheck {
            id: "region".to_string(),
            status: if region == Some(state.region_geoid) {
                CheckStatus::Pass
            } else {
                CheckStatus::Fail
            },
            detail: format!(
                "DeviceRegion actual {:?}, wanted {}",
                region, state.region_geoid
            ),
            critical: true,
        });
        let origin = self.classify_policy(state);
        let (policy_status, policy_detail) = match self.read_policy() {
            Ok(Some(text)) => match policy::parse(&text) {
                Ok(root) => {
                    let changed: Vec<_> = plan.decisions.iter().filter(|d| d.changed).collect();
                    let mut mismatches = 0usize;
                    for decision in &changed {
                        let actual = policy::state_of_guid(&root, &decision.guid);
                        if actual != Some(decision.target.as_str()) {
                            mismatches += 1;
                        }
                    }
                    let status = if mismatches == 0 {
                        CheckStatus::Pass
                    } else {
                        CheckStatus::Fail
                    };
                    (
                        status,
                        format!(
                            "origin {:?}; {} of {} changed policies match",
                            origin,
                            changed.len().saturating_sub(mismatches),
                            changed.len()
                        ),
                    )
                }
                Err(err) => (CheckStatus::Fail, format!("policy parse failed: {err}")),
            },
            _ => (CheckStatus::Fail, format!("policy file is {:?}", origin)),
        };
        report.checks.push(VerifyCheck {
            id: "policy".to_string(),
            status: policy_status,
            detail: policy_detail,
            critical: true,
        });
        let task = self.task_status();
        report.checks.push(VerifyCheck {
            id: "persistence".to_string(),
            status: if task == TaskStatus::Ready {
                CheckStatus::Pass
            } else {
                CheckStatus::Fail
            },
            detail: format!("scheduled task is {}", task.label()),
            critical: true,
        });
        if !plan.unclassified.is_empty() {
            report.checks.push(VerifyCheck {
                id: "unclassified".to_string(),
                status: CheckStatus::Pending,
                detail: format!(
                    "{} unclassified policies stay as Windows shipped them",
                    plan.unclassified.len()
                ),
                critical: false,
            });
        }
        report.all_settled_pass = report
            .checks
            .iter()
            .all(|check| !check.critical || check.status == CheckStatus::Pass);
        report
    }
}

static GEO_FOUND: LazyLock<Mutex<Vec<(String, u32)>>> = LazyLock::new(|| Mutex::new(Vec::new()));

static GEO_ISO: LazyLock<HashMap<String, u32>> = LazyLock::new(|| {
    unsafe extern "system" fn callback(geo_id: i32) -> BOOL {
        let mut buffer = [0u16; 16];
        let len = unsafe { GetGeoInfoW(geo_id, GEO_ISO2, Some(&mut buffer), 0) };
        if len > 1 {
            let iso = String::from_utf16_lossy(&buffer[..(len as usize - 1)]).to_ascii_uppercase();
            if let Ok(mut list) = GEO_FOUND.lock() {
                list.push((iso, geo_id as u32));
            }
        }
        BOOL(1)
    }
    let _ = unsafe { EnumSystemGeoID(GEOCLASS_NATION.0 as u32, 0, Some(callback)) };
    let list = GEO_FOUND.lock().map(|v| v.clone()).unwrap_or_default();
    list.into_iter().collect()
});

pub fn geo_id_for_iso(iso: &str) -> Option<u32> {
    GEO_ISO.get(&iso.to_ascii_uppercase()).copied()
}

pub fn iso_for_geo_id(geo_id: u32) -> Option<String> {
    GEO_ISO
        .iter()
        .find(|(_, id)| **id == geo_id)
        .map(|(iso, _)| iso.clone())
}

pub fn user_locale() -> String {
    let mut buffer = [0u16; 85];
    let len = unsafe { GetUserDefaultLocaleName(&mut buffer) };
    if len <= 0 {
        return "en-US".to_string();
    }
    String::from_utf16_lossy(&buffer[..(len as usize - 1)])
}

fn task_error(err: windows::core::Error) -> CoreError {
    CoreError::Message(format!("Task Scheduler: {err}"))
}

fn connect_task_service() -> Result<ITaskService> {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        let service: ITaskService =
            CoCreateInstance(&CLSID_CTaskScheduler, None, CLSCTX_INPROC_SERVER)
                .map_err(task_error)?;
        let empty = VARIANT::default();
        service
            .Connect(&empty, &empty, &empty, &empty)
            .map_err(task_error)?;
        Ok(service)
    }
}

fn task_status_via_com(installed: &Path) -> std::result::Result<TaskStatus, windows::core::Error> {
    let service: ITaskService = unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        let service: ITaskService =
            CoCreateInstance(&CLSID_CTaskScheduler, None, CLSCTX_INPROC_SERVER)?;
        let empty = VARIANT::default();
        service.Connect(&empty, &empty, &empty, &empty)?;
        service
    };
    let folder = unsafe { service.GetFolder(&BSTR::from("\\")) }?;
    let task = match unsafe { folder.GetTask(&BSTR::from(TASK_NAME)) } {
        Ok(task) => task,
        Err(_) => return Ok(TaskStatus::Absent),
    };
    let enabled = unsafe { task.Enabled()? }.as_bool();
    if !enabled {
        return Ok(TaskStatus::Disabled);
    }
    let xml = unsafe { task.Xml()? }.to_string();
    let Some(command) = extract_tag(&xml, "Command") else {
        return Ok(TaskStatus::Unknown);
    };
    let command = unescape_xml(&command);
    if command.is_empty() {
        return Ok(TaskStatus::Unknown);
    }
    if same_path(Path::new(&command), installed) {
        Ok(TaskStatus::Ready)
    } else {
        Ok(TaskStatus::Stale)
    }
}

fn task_status_from_file(paths: &Paths) -> TaskStatus {
    let path = paths.task_definition_path();
    if !path.exists() {
        return TaskStatus::Absent;
    }
    let Ok(text) = read_text_file(&path) else {
        return TaskStatus::Unknown;
    };
    let enabled = extract_tag(&text, "Enabled")
        .map(|v| !v.eq_ignore_ascii_case("false"))
        .unwrap_or(true);
    if !enabled {
        return TaskStatus::Disabled;
    }
    let Some(command) = extract_tag(&text, "Command") else {
        return TaskStatus::Unknown;
    };
    let command = unescape_xml(&command);
    if command.is_empty() {
        return TaskStatus::Unknown;
    }
    if same_path(Path::new(&command), &paths.installed_exe()) {
        TaskStatus::Ready
    } else {
        TaskStatus::Stale
    }
}

fn read_text_file(path: &Path) -> std::io::Result<String> {
    let bytes = std::fs::read(path)?;
    if bytes.starts_with(&[0xFF, 0xFE]) {
        let units: Vec<u16> = bytes[2..]
            .as_chunks::<2>()
            .0
            .iter()
            .map(|c| u16::from_le_bytes([c[0], c[1]]))
            .collect();
        Ok(String::from_utf16_lossy(&units))
    } else {
        Ok(String::from_utf8_lossy(&bytes).into_owned())
    }
}

fn extract_tag(xml: &str, tag: &str) -> Option<String> {
    let open = format!("<{tag}>");
    let close = format!("</{tag}>");
    let start = xml.find(&open)? + open.len();
    let end = xml[start..].find(&close)? + start;
    Some(xml[start..end].to_string())
}

fn xml_escape(value: &str) -> String {
    value
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}

fn unescape_xml(value: &str) -> String {
    value
        .replace("&amp;", "&")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&quot;", "\"")
        .replace("&apos;", "'")
}

fn build_task_xml(installed_exe: &Path) -> String {
    let yesterday = (chrono::Local::now().date_naive() - chrono::Days::new(1))
        .and_hms_opt(3, 0, 0)
        .unwrap();
    let start = yesterday.format("%Y-%m-%dT%H:%M:%S").to_string();
    let subscription = "<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and (EventID=19)]]</Select></Query></QueryList>";
    let command = xml_escape(&installed_exe.to_string_lossy());
    format!(
        r#"<?xml version="1.0" encoding="UTF-16"?><Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><RegistrationInfo><Author>One-Click EU Lord</Author><Description>Re-applies the One-Click EU Lord policy set after Windows updates, at logon and once a day. Delete with the tool's Restore action.</Description><URI>\{task_name}</URI></RegistrationInfo><Triggers><LogonTrigger><Enabled>true</Enabled><Delay>PT1M</Delay></LogonTrigger><CalendarTrigger><StartBoundary>{start}</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger><EventTrigger><Enabled>true</Enabled><Delay>PT2M</Delay><Subscription>{subscription}</Subscription></EventTrigger></Triggers><Principals><Principal id="Author"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT10M</ExecutionTimeLimit><Priority>7</Priority></Settings><Actions Context="Author"><Exec><Command>{command}</Command><Arguments>--reapply --quiet</Arguments></Exec></Actions></Task>"#,
        task_name = TASK_NAME,
        start = start,
        subscription = subscription,
        command = command
    )
}

pub fn run_elevated(exe: &Path, args: &[String]) -> Result<u32> {
    let exe_w = wide_path(exe);
    let parameters = args
        .iter()
        .map(|arg| quote_arg(arg))
        .collect::<Vec<_>>()
        .join(" ");
    let parameters_w = wide_str(&parameters);
    let mut info: SHELLEXECUTEINFOW = unsafe { std::mem::zeroed() };
    info.cbSize = std::mem::size_of::<SHELLEXECUTEINFOW>() as u32;
    info.fMask = SEE_MASK_NOCLOSEPROCESS;
    info.lpVerb = windows::core::w!("runas");
    info.lpFile = PCWSTR::from_raw(exe_w.as_ptr());
    info.lpParameters = PCWSTR::from_raw(parameters_w.as_ptr());
    info.nShow = SW_SHOWNORMAL.0;
    unsafe { ShellExecuteExW(&mut info) }
        .map_err(|err| CoreError::Message(format!("UAC elevation failed: {err}")))?;
    let handle = info.hProcess;
    unsafe { WaitForSingleObject(handle, u32::MAX) };
    let mut code = 1u32;
    unsafe { GetExitCodeProcess(handle, &mut code) }.map_err(|err| {
        CoreError::Message(format!("could not read elevated process exit code: {err}"))
    })?;
    let _ = unsafe { CloseHandle(handle) };
    Ok(code)
}

fn quote_arg(arg: &str) -> String {
    if arg.is_empty() {
        return "\"\"".to_string();
    }
    if !arg.chars().any(|ch| ch.is_whitespace() || ch == '"') {
        return arg.to_string();
    }
    let escaped = arg.replace('"', "\\\"");
    format!("\"{escaped}\"")
}

fn atomic_write(path: &Path, bytes: &[u8]) -> Result<()> {
    let parent = path
        .parent()
        .ok_or_else(|| CoreError::Message(format!("{} has no parent folder", path.display())))?;
    std::fs::create_dir_all(parent)?;
    let temp = parent.join(format!(
        ".eulord-tmp-{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(0)
    ));
    std::fs::write(&temp, bytes)?;
    if let Ok(file) = std::fs::OpenOptions::new().write(true).open(&temp) {
        let _ = file.sync_all();
    }
    replace_file(path, &temp)
}

fn replace_file(target: &Path, temp: &Path) -> Result<()> {
    if target.exists() {
        let target_w = wide_path(target);
        let temp_w = wide_path(temp);
        let result = unsafe {
            ReplaceFileW(
                PCWSTR::from_raw(target_w.as_ptr()),
                PCWSTR::from_raw(temp_w.as_ptr()),
                PCWSTR::null(),
                REPLACEFILE_WRITE_THROUGH,
                None,
                None,
            )
        };
        if let Err(err) = result {
            let _ = std::fs::remove_file(temp);
            return Err(CoreError::Message(format!(
                "could not replace {} atomically: {err}",
                target.display()
            )));
        }
        let _ = std::fs::remove_file(temp);
        Ok(())
    } else {
        let temp_w = wide_path(temp);
        let target_w = wide_path(target);
        let flags = windows::Win32::Storage::FileSystem::MOVE_FILE_FLAGS(
            MOVEFILE_REPLACE_EXISTING.0 | MOVEFILE_WRITE_THROUGH.0,
        );
        unsafe {
            MoveFileExW(
                PCWSTR::from_raw(temp_w.as_ptr()),
                PCWSTR::from_raw(target_w.as_ptr()),
                flags,
            )
        }
        .map_err(|err| {
            CoreError::Message(format!(
                "could not rename {} to {}: {err}",
                temp.display(),
                target.display()
            ))
        })
    }
}

fn harden_directory(path: &Path) -> Result<()> {
    let sddl = wide_str("D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FR;;;BU)");
    let mut descriptor = PSECURITY_DESCRIPTOR::default();
    unsafe {
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            PCWSTR(sddl.as_ptr()),
            SDDL_REVISION_1,
            &mut descriptor,
            None,
        )
        .map_err(|err| CoreError::Message(format!("security descriptor: {err}")))?;
        let result = apply_dacl_tree(path, descriptor);
        let _ = LocalFree(Some(HLOCAL(descriptor.0)));
        result
    }
}

fn apply_dacl_tree(path: &Path, descriptor: PSECURITY_DESCRIPTOR) -> Result<()> {
    let wide = wide_path(path);
    let ok = unsafe {
        SetFileSecurityW(PCWSTR(wide.as_ptr()), DACL_SECURITY_INFORMATION, descriptor).as_bool()
    };
    if !ok {
        return Err(CoreError::Message(format!(
            "failed to apply restrictive ACL to {}",
            path.display()
        )));
    }
    if !path.is_dir() {
        return Ok(());
    }
    let entries = std::fs::read_dir(path)?;
    for entry in entries {
        let entry = entry?;
        let file_type = entry.file_type()?;
        if file_type.is_symlink() {
            continue;
        }
        if file_type.is_dir() || file_type.is_file() {
            apply_dacl_tree(&entry.path(), descriptor)?;
        }
    }
    Ok(())
}

fn wide_str(value: &str) -> Vec<u16> {
    OsStr::new(value)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect()
}
fn wide_path(path: &Path) -> Vec<u16> {
    path.as_os_str()
        .encode_wide()
        .chain(std::iter::once(0))
        .collect()
}

fn now_utc() -> String {
    chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true)
}

fn policy_hash(text: &str) -> String {
    sha256_bytes(text.as_bytes())
}

fn sha256_bytes(bytes: &[u8]) -> String {
    let digest = Sha256::digest(bytes);
    let mut output = String::with_capacity(digest.len() * 2);
    for byte in digest {
        use std::fmt::Write;
        let _ = write!(&mut output, "{byte:02x}");
    }
    output
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_paths(name: &str) -> Paths {
        let root =
            std::env::temp_dir().join(format!("eulord-win-test-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        Paths::discover(Some(root.join("data")), Some(root.join("policy.json"))).unwrap()
    }

    #[test]
    fn task_xml_has_three_triggers_and_escaped_command() {
        let xml = build_task_xml(Path::new(r"C:\ProgramData\A&B\eulord.exe"));
        assert!(xml.contains("<LogonTrigger>"));
        assert!(xml.contains("<CalendarTrigger>"));
        assert!(xml.contains("<EventTrigger>"));
        assert!(xml.contains("WindowsUpdateClient"));
        assert!(xml.contains("A&amp;B"));
        assert!(extract_tag(&xml, "Command").unwrap().contains("A&amp;B"));
    }

    #[test]
    fn atomic_write_replaces_existing_file() {
        let paths = temp_paths("atomic");
        std::fs::create_dir_all(&paths.data_root).unwrap();
        std::fs::write(paths.state_file(), b"old").unwrap();
        atomic_write(&paths.state_file(), b"new").unwrap();
        assert_eq!(std::fs::read(paths.state_file()).unwrap(), b"new");
        let _ = std::fs::remove_dir_all(paths.data_root.parent().unwrap());
    }

    #[test]
    fn policy_hash_is_stable() {
        assert_eq!(policy_hash("x"), policy_hash("x"));
        assert_ne!(policy_hash("x"), policy_hash("y"));
    }

    #[test]
    fn extraction_and_unescape_roundtrip() {
        let xml = "<Command>C:\\A&amp;B\\eulord.exe</Command>";
        let raw = extract_tag(xml, "Command").unwrap();
        assert_eq!(unescape_xml(&raw), r"C:\A&B\eulord.exe");
    }
}
