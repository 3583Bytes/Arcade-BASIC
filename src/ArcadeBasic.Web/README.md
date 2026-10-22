# Arcade BASIC playground (spike)

A browser page that runs Arcade BASIC programs on the bytecode VM, compiled to
WebAssembly with the plain .NET WebAssembly SDK (no Blazor). The page
(`wwwroot/main.js`) owns the loop: every animation frame it calls `BasicVm.Step`
for up to ~10 ms, waits out `SLEEP`s, shows `PRINT` output, asks for `INPUT`, and
replays the newest frame onto a `<canvas>`. Keys go to the program while the
canvas has focus, with exact held-key state for `KEYDOWN`.

Not in `ArcadeBasic.sln` yet, so CI doesn't build it.

## Run it

```sh
dotnet run --project src/ArcadeBasic.Web -c Release
# open the "App url: http://127.0.0.1:…/" it prints — pick an example, Run,
# click the screen to play. (The HTTPS dev-certificate warning is harmless; use the http URL.)
```

To host it anywhere static (GitHub Pages, `python3 -m http.server`), publish and
serve `bin/Release/net10.0/publish/wwwroot`:

```sh
dotnet publish -c Release src/ArcadeBasic.Web
python3 -m http.server -d src/ArcadeBasic.Web/bin/Release/net10.0/publish/wwwroot 8080
```

Publishing compiles the .NET code to WebAssembly ahead of time
(`RunAOTCompilation`), so it needs the WebAssembly build tools once:
`dotnet workload install wasm-tools` (run it outside the repo, with `sudo` if your
.NET install is system-wide). It takes about a minute and makes the download
about 2.2 MB compressed, against 1.65 MB for the plain build — and the VM about
ten times faster: in Firefox, native Mandelbrot runs in 15 ms rather than 150, and
`maze3d` takes 5 ms of each 40 ms frame rather than 16. `dotnet run` and
`dotnet build` don't compile ahead of time, so they stay quick for trying changes,
at the interpreter's speed.

`?bench=1` runs the measurements and POSTs them to `/results` (for a test server
that accepts them; a plain static server just ignores the request): compute
loops, then ten seconds each of `invaders`, `breakout` and `maze3d` for their
frame rate and VM time per frame. Add `&snapshot` to include each game's last
frame as a PNG.

## Pieces

| File | Role |
|---|---|
| `Program.cs` | `[JSExport]` surface: load/compile, `Step`, output, input, frames, keys, stats |
| `CanvasGraphicsDevice.cs` | §13 backend: records each frame as a flat command list the page replays |
| `BrowserKeyboard.cs` | `IKeyboard` + `IKeyState` from DOM `keydown`/`keyup` |
| `wwwroot/main.js` | the loop, canvas drawing (optional phosphor glow), keyboard, input, stats |
