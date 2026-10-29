// The desktop player: an Arcade BASIC program in a window of its own, no IDE. The window
// shows the player page (src/ArcadeBasic.Player, built into this executable from dist/ —
// see build.sh) on arcade://localhost, which also serves the page the program, game.bin —
// from a macOS app's Resources, or appended to this executable the way `arcade-basic build`
// appends one ([program][u32 length]["FB-BCEND"]) — and `quit`, which the page asks for
// once the program has ended. `arcade-basic build --desktop`, and the IDE's File → Export,
// make the apps.
//
// The app's icon: a macOS app's is in its Resources (Info.plist names it), and a Windows
// one's is this executable's own, written over the placeholder icon it's built with
// (icons/make-icons.mjs). A Linux app's is appended before the program, [PNG][u32 length]
// ["FB-ICEND"], and goes on the window.

// A window, not a console program, on Windows.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::Path;
use tauri::http::Response;
use tauri::{WebviewUrl, WebviewWindowBuilder};

const PROGRAM: &[u8; 8] = b"FB-BCEND";
const ICON: &[u8; 8] = b"FB-ICEND";

/// What ends at `end` in `file`, followed by its length (u32, little-endian) and `magic`:
/// it, and where it starts.
fn ending_at(file: &mut File, end: u64, magic: &[u8; 8]) -> Option<(Vec<u8>, u64)> {
    if end < 12 {
        return None;
    }
    let mut trailer = [0u8; 12];
    file.seek(SeekFrom::Start(end - 12)).ok()?;
    file.read_exact(&mut trailer).ok()?;
    if &trailer[4..] != magic {
        return None;
    }
    let len = u32::from_le_bytes(trailer[..4].try_into().ok()?) as u64;
    if len == 0 || len > end - 12 {
        return None;
    }
    let start = end - 12 - len;
    let mut data = vec![0u8; len as usize];
    file.seek(SeekFrom::Start(start)).ok()?;
    file.read_exact(&mut data).ok()?;
    Some((data, start))
}

/// The program appended to `exe`, if there is one, and the icon before it, if there's that.
fn appended(exe: &Path) -> (Option<Vec<u8>>, Option<Vec<u8>>) {
    let Ok(mut file) = File::open(exe) else { return (None, None) };
    let size = file.metadata().map(|m| m.len()).unwrap_or(0);
    match ending_at(&mut file, size, PROGRAM) {
        Some((program, start)) => (Some(program), ending_at(&mut file, start, ICON).map(|(icon, _)| icon)),
        None => (None, None),
    }
}

/// The program to play — appended to this executable, or in a macOS app, game.bin in its
/// Resources (Contents/MacOS/<name> → Contents/Resources/game.bin) — and its icon, if it's
/// appended too.
fn program(exe: &Path) -> (Option<Vec<u8>>, Option<Vec<u8>>) {
    let (program, icon) = appended(exe);
    let program = program.or_else(|| std::fs::read(exe.parent()?.parent()?.join("Resources").join("game.bin")).ok());
    (program, icon)
}

/// `text` for a URL's query: anything but letters, digits and -._~ as %XX.
fn query_text(text: &str) -> String {
    text.bytes()
        .map(|b| if b.is_ascii_alphanumeric() || b"-._~".contains(&b) { (b as char).to_string() } else { format!("%{b:02X}") })
        .collect()
}

fn main() {
    let exe = std::env::current_exe().unwrap_or_default();
    let title = exe.file_stem().and_then(|s| s.to_str()).unwrap_or("Arcade BASIC").to_string();
    let (game, icon) = program(&exe);

    tauri::Builder::default()
        .register_uri_scheme_protocol("arcade", move |ctx, request| {
            let path = request.uri().path().trim_start_matches('/');
            let path = if path.is_empty() { "index.html" } else { path };
            let (status, mime, body) = match path {
                "game.bin" => match &game {
                    Some(program) => (200, "application/octet-stream".to_string(), program.clone()),
                    None => (404, "text/plain".to_string(), Vec::new()),
                },
                "quit" => {
                    ctx.app_handle().exit(0);
                    (200, "text/plain".to_string(), Vec::new())
                }
                _ => match ctx.app_handle().asset_resolver().get(path.to_string()) {
                    Some(asset) => (200, asset.mime_type, asset.bytes),
                    None => (404, "text/plain".to_string(), Vec::new()),
                },
            };
            Response::builder().status(status).header("Content-Type", mime).body(body).unwrap()
        })
        .setup(move |app| {
            // (Windows serves custom schemes as http://<scheme>.localhost.) The page takes the
            // app's name for its title, which also keeps each game's saved files apart.
            let mut page = String::from(if cfg!(windows) { "http://arcade.localhost/index.html" } else { "arcade://localhost/index.html" });
            page += &format!("?title={}", query_text(&title));
            if let Ok(report) = std::env::var("ARCADE_PLAYER_REPORT") {
                page += &format!("&report={}", query_text(&report));   // (tests: where the page posts what happened)
            }
            let window = WebviewWindowBuilder::new(app, "main", WebviewUrl::CustomProtocol(page.parse()?))
                .title(&title)
                .inner_size(960.0, 600.0)
                .min_inner_size(320.0, 200.0);
            #[cfg(target_os = "linux")]
            let window = match icon.as_deref().map(tauri::image::Image::from_bytes) {
                Some(Ok(image)) => window.icon(image)?,
                _ => window,
            };
            #[cfg(not(target_os = "linux"))]
            let _ = &icon;
            window.build()?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("the player couldn't start");
}
