// The desktop player: an Arcade BASIC program in a window of its own, no IDE. The window
// shows the player page (src/ArcadeBasic.Player, built into this executable from dist/ —
// see build.sh) on arcade://localhost, which also serves the page the program, game.bin —
// from a macOS app's Resources, or appended to this executable the way `arcade-basic build`
// appends one ([program][u32 length]["FB-BCEND"]) — and `quit`, which the page asks for
// once the program has ended. `arcade-basic build --desktop` makes the apps.

use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::Path;
use tauri::http::Response;
use tauri::{WebviewUrl, WebviewWindowBuilder};

const MAGIC: &[u8; 8] = b"FB-BCEND";

/// The program appended to `exe`, if there is one.
fn appended(exe: &Path) -> Option<Vec<u8>> {
    let mut file = File::open(exe).ok()?;
    let size = file.metadata().ok()?.len();
    if size < 12 {
        return None;
    }
    let mut trailer = [0u8; 12];
    file.seek(SeekFrom::End(-12)).ok()?;
    file.read_exact(&mut trailer).ok()?;
    if &trailer[4..] != MAGIC {
        return None;
    }
    let len = u32::from_le_bytes(trailer[..4].try_into().ok()?) as u64;
    if len == 0 || len > size - 12 {
        return None;
    }
    let mut program = vec![0u8; len as usize];
    file.seek(SeekFrom::End(-12 - len as i64)).ok()?;
    file.read_exact(&mut program).ok()?;
    Some(program)
}

/// The program to play: appended to this executable, or in a macOS app, game.bin in its
/// Resources (Contents/MacOS/<name> → Contents/Resources/game.bin).
fn program(exe: &Path) -> Option<Vec<u8>> {
    appended(exe).or_else(|| std::fs::read(exe.parent()?.parent()?.join("Resources").join("game.bin")).ok())
}

fn main() {
    let exe = std::env::current_exe().unwrap_or_default();
    let title = exe.file_stem().and_then(|s| s.to_str()).unwrap_or("Arcade BASIC").to_string();
    let game = program(&exe);

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
            // (Windows serves custom schemes as http://<scheme>.localhost)
            let mut page = String::from(if cfg!(windows) { "http://arcade.localhost/index.html" } else { "arcade://localhost/index.html" });
            if let Ok(report) = std::env::var("ARCADE_PLAYER_REPORT") {
                page += &format!("?report={report}");   // (tests: where the page posts what happened)
            }
            WebviewWindowBuilder::new(app, "main", WebviewUrl::CustomProtocol(page.parse()?))
                .title(&title)
                .inner_size(960.0, 600.0)
                .min_inner_size(320.0, 200.0)
                .build()?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("the player couldn't start");
}
