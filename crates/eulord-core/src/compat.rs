use serde::{Deserialize, Serialize};

/// Windows 11 22H2 is the first baseline. DMA policy rollouts are cumulative,
/// so builds above the baseline are treated as known but not necessarily tested.
pub const MINIMUM_BUILD: u32 = 22621;
pub const TESTED_BUILDS: &[u32] = &[22621, 22631, 26100, 26300];

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum CompatLevel {
    Tested,
    Known,
    Unknown,
    Unsupported,
}

impl CompatLevel {
    pub fn is_supported(self) -> bool {
        !matches!(self, Self::Unsupported)
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct OsInfo {
    pub major: u32,
    pub minor: u32,
    pub build: u32,
    pub ubr: u32,
    pub display_version: String,
    pub product_name: String,
    pub architecture: String,
    pub compat: CompatLevel,
}

impl Default for OsInfo {
    fn default() -> Self {
        Self {
            major: 0,
            minor: 0,
            build: 0,
            ubr: 0,
            display_version: String::new(),
            product_name: String::new(),
            architecture: std::env::consts::ARCH.to_string(),
            compat: CompatLevel::Unknown,
        }
    }
}

impl OsInfo {
    pub fn build_label(&self) -> String {
        if self.ubr > 0 {
            format!("{}.{}", self.build, self.ubr)
        } else {
            self.build.to_string()
        }
    }

    pub fn full_label(&self) -> String {
        if self.product_name.is_empty() {
            format!("Windows {} ({})", self.build_label(), self.architecture)
        } else {
            format!(
                "{} {} ({})",
                if self.build >= 22000 {
                    "Windows 11"
                } else {
                    self.product_name.as_str()
                },
                self.display_version,
                self.architecture
            )
        }
    }
}

pub fn classify(build: u32) -> CompatLevel {
    if build == 0 {
        CompatLevel::Unknown
    } else if build < MINIMUM_BUILD {
        CompatLevel::Unsupported
    } else if TESTED_BUILDS.contains(&build) {
        CompatLevel::Tested
    } else {
        CompatLevel::Known
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn classifies_windows_builds() {
        assert_eq!(classify(19045), CompatLevel::Unsupported);
        assert_eq!(classify(22621), CompatLevel::Tested);
        assert_eq!(classify(26300), CompatLevel::Tested);
        assert_eq!(classify(27000), CompatLevel::Known);
        assert_eq!(classify(0), CompatLevel::Unknown);
    }
}
