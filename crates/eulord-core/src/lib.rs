pub mod catalog;
pub mod compat;
pub mod error;
pub mod i18n;
pub mod paths;
pub mod plan;
pub mod policy;
pub mod state;

pub use error::{CoreError, Result};

pub const APP_VERSION: &str = env!("CARGO_PKG_VERSION");
pub const REPOSITORY: &str = "https://github.com/PZW152/one-click-eu-lord";
