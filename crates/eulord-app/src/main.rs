#![windows_subsystem = "windows"]

use std::ffi::c_void;
use std::io::Write;
use std::os::windows::io::AsRawHandle;

use clap::Parser;

#[link(name = "kernel32")]
unsafe extern "system" {
    fn AttachConsole(dw_process_id: u32) -> i32;
    fn AllocConsole() -> i32;
    fn SetStdHandle(n_std_handle: u32, h_handle: *mut c_void) -> i32;
    fn GetStdHandle(n_std_handle: u32) -> *mut c_void;
    fn GetFileType(h_file: *mut c_void) -> u32;
}

const ATTACH_PARENT_PROCESS: u32 = 0xFFFF_FFFF;
const STD_OUTPUT_HANDLE: u32 = 0xFFFF_FFF5;
const STD_ERROR_HANDLE: u32 = 0xFFFF_FFF4;
const FILE_TYPE_UNKNOWN: u32 = 0;

fn main() {
    std::panic::set_hook(Box::new(|info| {
        let backtrace = std::backtrace::Backtrace::force_capture();
        debug_log(&format!("PANIC: {info}\n{backtrace}"));
    }));
    let has_args = std::env::args_os().len() > 1;
    if has_args {
        let mut console = attach_console();
        let cli = eulord_app::cli::Cli::parse();
        let code = if let Some(file) = console.as_mut() {
            let code = eulord_app::cli::run(cli, file);
            let _ = file.flush();
            code
        } else {
            let mut stdout = std::io::stdout();
            let code = eulord_app::cli::run(cli, &mut stdout);
            let _ = stdout.flush();
            code
        };
        std::process::exit(code);
    }
    let cli = eulord_app::cli::Cli::parse();
    if let Err(err) = eulord_app::gui::run(cli) {
        debug_log(&format!("gui error: {err}"));
        let _ = rfd::MessageDialog::new()
            .set_title("One-Click EU Lord")
            .set_description(format!("The window could not be opened:\n{err}"))
            .set_level(rfd::MessageLevel::Error)
            .show();
    }
}

fn attach_console() -> Option<std::fs::File> {
    let existing = unsafe { GetStdHandle(STD_OUTPUT_HANDLE) };
    if !existing.is_null() && unsafe { GetFileType(existing) } != FILE_TYPE_UNKNOWN {
        return None;
    }
    unsafe {
        if AttachConsole(ATTACH_PARENT_PROCESS) == 0 {
            let _ = AllocConsole();
        }
    }
    let file = std::fs::OpenOptions::new()
        .write(true)
        .open("CONOUT$")
        .ok()?;
    let handle = file.as_raw_handle();
    unsafe {
        let _ = SetStdHandle(STD_OUTPUT_HANDLE, handle);
        let _ = SetStdHandle(STD_ERROR_HANDLE, handle);
    }
    Some(file)
}

fn debug_log(message: &str) {
    let path = std::env::temp_dir().join("eulord-debug.log");
    if let Ok(mut file) = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)
    {
        let _ = writeln!(
            file,
            "{} {}",
            chrono::Local::now().format("%Y-%m-%d %H:%M:%S"),
            message
        );
    }
}
