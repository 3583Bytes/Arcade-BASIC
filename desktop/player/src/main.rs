// The desktop player: an Arcade BASIC program in a window of its own, no IDE. The window
// shows the player page (src/ArcadeBasic.Player, built into this executable from dist/ —
// see build.sh) on arcade://localhost, which also serves the page the program, game.bin —
// from a macOS app's Resources, or appended to this executable the way `arcade-basic build`
// appends one ([program][u32 length]["FB-BCEND"]) — and `quit`, which the page asks for
// once the program has ended. `arcade-basic build --desktop`, and the IDE's File → Export,
// make the apps.
//
// `arcade-basic run` opens a program that draws in this player too: `arcade-player
// <program> --folder <dir> --title <name>`, the compiled program in a file. Then the page
// (?folder) works on the files in <dir>, the folder it was run from, rather than ones kept
// in the browser: `folder` is that folder's files, and `folder/<name>` (POST) saves one.
// What the program prints (`print`) and how it ended (`ended`) go to the terminal, and the
// player's exit code is the program's.
//
// The app's icon: a macOS app's is in its Resources (Info.plist names it), and a Windows
// one's is this executable's own, written over the placeholder icon it's built with
// (icons/make-icons.mjs). A Linux app's is appended before the program, [PNG][u32 length]
// ["FB-ICEND"], and goes on the window.

// A window, not a console program, on Windows.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::fs::File;
use std::io::{Read, Seek, SeekFrom, Write};
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicI32, Ordering};
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

/// What `arcade-basic run` asks for: `<program> [--folder <dir>] [--title <name>]`. The
/// program (the file's bytes), the folder its files are in and the window's title, if
/// they're given.
fn arguments() -> (Option<Vec<u8>>, Option<PathBuf>, Option<String>) {
    let (mut program, mut folder, mut title) = (None, None, None);
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--folder" => folder = args.next().map(PathBuf::from),
            "--title" => title = args.next(),
            _ if !arg.starts_with('-') && program.is_none() => program = std::fs::read(&arg).ok(),
            _ => {}   // (anything else, such as what macOS adds, is someone else's)
        }
    }
    (program, folder, title)
}

/// The most of a folder's files the page is given, each and all together: programs keep
/// their files small, and a folder can be anything.
const FILE_LIMIT: u64 = 1 << 20;
const FOLDER_LIMIT: u64 = 16 << 20;

/// The text files in `folder` (not in folders inside it, nor hidden ones), as JSON: name → text.
fn folder_files(folder: &Path) -> String {
    let mut json = String::from("{");
    let mut total = 0;
    let mut entries: Vec<_> = std::fs::read_dir(folder).into_iter().flatten().flatten().collect();
    entries.sort_by_key(|e| e.file_name());
    for entry in entries {
        let name = entry.file_name().to_string_lossy().into_owned();
        let size = entry.metadata().map(|m| if m.is_file() { m.len() } else { u64::MAX }).unwrap_or(u64::MAX);
        if name.starts_with('.') || size > FILE_LIMIT || total + size > FOLDER_LIMIT {
            continue;
        }
        let Ok(text) = std::fs::read_to_string(entry.path()) else { continue };   // (not text)
        total += size;
        if json.len() > 1 {
            json.push(',');
        }
        json += &format!("{}:{}", json_string(&name), json_string(&text));
    }
    json + "}"
}

fn json_string(text: &str) -> String {
    let mut json = String::from("\"");
    for c in text.chars() {
        match c {
            '"' => json += "\\\"",
            '\\' => json += "\\\\",
            '\n' => json += "\\n",
            '\r' => json += "\\r",
            '\t' => json += "\\t",
            c if (c as u32) < 0x20 => json += &format!("\\u{:04x}", c as u32),
            c => json.push(c),
        }
    }
    json + "\""
}

/// Where file `name` ("/" between folders, as a URL's path has it) goes in `folder`, if it's
/// inside it.
fn in_folder(folder: &Path, name: &str) -> Option<PathBuf> {
    let name = Path::new(name);
    if name.as_os_str().is_empty() || !name.components().all(|c| matches!(c, Component::Normal(_))) {
        return None;
    }
    Some(folder.join(name))
}

/// A URL path's %XX as the bytes they are.
fn unquote(text: &str) -> String {
    let bytes = text.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        let hex = bytes.get(i + 1..i + 3).and_then(|h| std::str::from_utf8(h).ok()).and_then(|h| u8::from_str_radix(h, 16).ok());
        match (bytes[i], hex) {
            (b'%', Some(b)) => { out.push(b); i += 3; }
            (b, _) => { out.push(b); i += 1; }
        }
    }
    String::from_utf8_lossy(&out).into_owned()
}

/// `text` for a URL's query: anything but letters, digits and -._~ as %XX.
fn query_text(text: &str) -> String {
    text.bytes()
        .map(|b| if b.is_ascii_alphanumeric() || b"-._~".contains(&b) { (b as char).to_string() } else { format!("%{b:02X}") })
        .collect()
}

fn main() {
    let exe = std::env::current_exe().unwrap_or_default();
    let (game, icon) = program(&exe);
    // A program of its own (an app) is the one it plays; else the one it's given (a run).
    let (given, folder, given_title) = if game.is_none() { arguments() } else { (None, None, None) };
    let game = game.or(given);
    let title = given_title.unwrap_or_else(|| exe.file_stem().and_then(|s| s.to_str()).unwrap_or("Arcade BASIC").to_string());
    let page_folder = folder.is_some();
    static EXIT_CODE: AtomicI32 = AtomicI32::new(0);   // the program's: 1 if it ended with an error

    tauri::Builder::default()
        .register_uri_scheme_protocol("arcade", move |ctx, request| {
            let path = request.uri().path().trim_start_matches('/');
            let path = if path.is_empty() { "index.html" } else { path };
            let text = |status, body: Vec<u8>| (status, "text/plain".to_string(), body);
            let (status, mime, body) = match (path, &folder) {
                ("game.bin", _) => match &game {
                    Some(program) => (200, "application/octet-stream".to_string(), program.clone()),
                    None => text(404, Vec::new()),
                },
                ("quit", _) => {
                    ctx.app_handle().exit(EXIT_CODE.load(Ordering::SeqCst));
                    text(200, Vec::new())
                }
                ("folder", Some(folder)) => (200, "application/json".to_string(), folder_files(folder).into_bytes()),
                (path, Some(folder)) if path.starts_with("folder/") => match in_folder(folder, &unquote(&path["folder/".len()..])) {
                    Some(file) => {
                        let saved = file.parent().map_or(Ok(()), std::fs::create_dir_all).and_then(|_| std::fs::write(&file, request.body()));
                        match saved {
                            Ok(()) => text(200, Vec::new()),
                            Err(e) => {
                                eprintln!("couldn't save {}: {e}", file.display());
                                text(500, Vec::new())
                            }
                        }
                    }
                    None => text(403, Vec::new()),
                },
                ("print", Some(_)) => {
                    let mut out = std::io::stdout();
                    let _ = out.write_all(request.body()).and_then(|_| out.flush());
                    text(200, Vec::new())
                }
                ("ended", Some(_)) => {
                    if !request.body().is_empty() {
                        eprintln!("{}", String::from_utf8_lossy(request.body()).trim_end());
                        EXIT_CODE.store(1, Ordering::SeqCst);
                    }
                    text(200, Vec::new())
                }
                (path, _) => match ctx.app_handle().asset_resolver().get(path.to_string()) {
                    Some(asset) => (200, asset.mime_type, asset.bytes),
                    None => text(404, Vec::new()),
                },
            };
            Response::builder().status(status).header("Content-Type", mime).body(body).unwrap()
        })
        .setup(move |app| {
            // (Windows serves custom schemes as http://<scheme>.localhost.) The page takes the
            // app's name for its title, which also keeps each game's saved files apart.
            let mut page = String::from(if cfg!(windows) { "http://arcade.localhost/index.html" } else { "arcade://localhost/index.html" });
            page += &format!("?title={}", query_text(&title));
            if page_folder {
                page += "&folder";
            }
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
            let window = window.build()?;
            if page_folder {
                let _ = window.set_focus();   // (run from a terminal, which has the focus)
            }
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("the player couldn't start")
        .run(|_, event| {
            if let tauri::RunEvent::Exit = event {
                std::process::exit(EXIT_CODE.load(Ordering::SeqCst));
            }
        });
}
