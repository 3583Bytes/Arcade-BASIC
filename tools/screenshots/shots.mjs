// The pictures in docs/images, taken from the browser IDE — so that when it changes,
// one command takes them all again:
//
//   node tools/screenshots/shots.mjs              # all of them
//   node tools/screenshots/shots.mjs debugger     # just these (names below)
//   node tools/screenshots/shots.mjs --no-build   # don't publish the IDE first
//
// It publishes the IDE (without ahead-of-time compilation: quick, and no workload
// needed), serves it on localhost, and drives headless Firefox over WebDriver BiDi.
// Needs Node 22 or later (for its WebSocket), Firefox (set FIREFOX=/path/to/firefox if
// it isn't in the usual place) and, for the GIF, ffmpeg. Each shot gets a browser
// profile of its own ("user context"), so none sees the tabs or theme another left.

import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const out = path.join(repo, 'docs', 'images');
const work = path.join(os.tmpdir(), 'arcade-basic-shots');
const site = path.join(work, 'web', 'wwwroot');
const args = process.argv.slice(2);
const only = args.filter(a => !a.startsWith('--'));
const sleep = ms => new Promise(r => setTimeout(r, ms));

// ---- what's in the page for the shots to use ---------------------------------------
// (run inside the page, before each shot's own script)
const helpers = `
  const A = window.arcadeBasic, $ = id => document.getElementById(id), view = A.editor.view;
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const canvas = $('screen');
  const key = (type, code, k) => canvas.dispatchEvent(new KeyboardEvent(type, { key: k, code, bubbles: true }));
  const hold = async (code, k, ms) => { key('keydown', code, k); await sleep(ms); key('keyup', code, k); };
  const tap = (code, k) => hold(code, k, 60);
  const point = (kind, col, row) => { const r = canvas.getBoundingClientRect();     // (an 80 x 24 screen)
    canvas.dispatchEvent(new PointerEvent(kind, { clientX: r.left + col / 80 * r.width, clientY: r.top + (row + 0.5) / 24 * r.height,
                                                  button: 0, pointerId: 1, bubbles: true })); };
  const click = async (col, row) => { point('pointermove', col, row); point('pointerdown', col, row); await sleep(80); point('pointerup', col, row); await sleep(80); };
  const theme = name => { $('theme').value = name; $('theme').dispatchEvent(new Event('change')); };
  const show = name => document.querySelector('#views [data-view="' + name + '"]').click();
  const run = async ms => { $('run').click(); await sleep(ms); };
  const line = text => view.state.doc.lineAt(view.state.doc.toString().indexOf(text)).number;
  const rect = el => { const r = (typeof el === 'string' ? document.querySelector(el) : el).getBoundingClientRect();
                       return { x: r.left, y: r.top, width: r.width, height: r.height }; };
`;

// ---- the shots ---------------------------------------------------------------------
// name: the file (docs/images/<name>.png); open: the example the page opens with;
// size: the window [width, height, pixel ratio]; js: what to do first (the body of an
// async function, with the helpers above); it returns the part of the page to keep
// ({x, y, width, height}; nothing: the whole window). gif: instead, run the program and
// record its screen for that many seconds, js playing alongside.
const page = [1280, 800, 2];
const game = [800, 600, 1];
const shots = [
  {
    name: 'ide', open: 'maze3d', size: page,
    js: `await run(2500); await hold('KeyW', 'w', 400); await sleep(300);`,
  },
  {
    name: 'ide-light', open: 'maze3d', size: page,
    js: `theme('daylight'); await run(2500); await hold('KeyW', 'w', 400); await sleep(300);`,
  },
  // for GitHub's Settings → Social preview (what a link to the repository shows)
  { name: 'social-preview', open: 'maze3d', size: [1280, 640, 1], js: `await run(2500); await hold('KeyW', 'w', 400); await sleep(300);` },
  {
    name: 'debugger', open: 'breakout', size: page,
    js: `A.editor.goToLine(line('GOSUB 1000'));     // the main loop's "draw"
         await sleep(200);
         view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'F9', code: 'F9', bubbles: true, cancelable: true }));
         await run(1500);
         show('code');
         await sleep(600);`,
  },
  {
    name: 'pixel-editor', open: 'maze3d-textures', size: page,
    js: `A.editor.goToLine(line('\\n  ! stone') + 1);
         await sleep(300);
         const lens = [...document.querySelectorAll('.cm-pictureLens')].find(l => l.textContent.includes('stone'));
         lens.querySelector('button').dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
         await sleep(500);
         return rect('#pictureDialog');`,
  },
  {
    name: 'tune-editor', open: 'music', size: page,
    js: `A.editor.goToLine(line('PLAY "'));
         await sleep(300);
         view.contentDOM.querySelectorAll('.cm-tune button')[1].dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
         await sleep(500);
         return rect('#tuneDialog');`,
  },
  { name: 'theme-qbasic', open: 'startrek', size: [1280, 720, 2], js: `theme('qbasic'); A.editor.goToLine(line('2060 INPUT')); await sleep(400);` },
  { name: 'theme-commodore', open: 'lunar', size: [1280, 720, 2], js: `theme('commodore'); A.editor.goToLine(line('120 LET L')); await sleep(400);` },
  // a play link: the program's screen and a bar, no editor
  { name: 'play-link', open: 'play/invaders', size: [1280, 720, 2], js: `$('playButton').click(); await sleep(3000);` },
  // the games and programs: just their screens
  { name: 'invaders', open: 'invaders', size: game, js: `await run(3000); await hold('ArrowLeft', 'ArrowLeft', 500); await tap('Space', ' '); await sleep(300); return rect(canvas);` },
  { name: 'breakout', open: 'breakout', size: game, js: `await run(2500); await tap('Space', ' '); await sleep(1500); return rect(canvas);` },
  { name: 'tetris', open: 'tetris', size: game,
    js: `await run(1500);
         for (const n of [-4, 4, 0, -2, 2, -1, 3, 1, -3]) {       // a few pieces, left and right, dropped
           for (let i = 0; i < Math.abs(n); i++) await tap(n < 0 ? 'KeyA' : 'KeyD', n < 0 ? 'a' : 'd');
           await tap('Space', ' ');
           await sleep(300);
         }
         await sleep(1200);
         return rect(canvas);` },
  { name: 'kanban', open: 'kanban', size: game, js: `await run(2500); return rect(canvas);` },
  { name: 'readyedit', open: 'readyedit', size: game, js: `await run(2500); await click(4, 0); await sleep(300); return rect(canvas);` },
  { name: 'maze3d-walk', open: 'maze3d', size: game, gif: 2.8,
    // (down the first corridor, east, and right at its end into the turning south:
    // 3 tiles a second, a quarter turn in 0.8 s)
    js: `await hold('KeyW', 'w', 1250); await hold('KeyD', 'd', 800);` },
];

// ---- publish the IDE and serve it ----------------------------------------------------
if (!args.includes('--no-build') || !fs.existsSync(site)) {
  const r = spawnSync('dotnet', ['publish', 'src/ArcadeBasic.Web', '-c', 'Release', '-p:RunAOTCompilation=false', '-o', path.join(work, 'web')],
                      { cwd: repo, stdio: 'inherit' });
  if (r.status !== 0) process.exit(r.status ?? 1);
}
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
                 '.json': 'application/json', '.wasm': 'application/wasm', '.png': 'image/png', '.svg': 'image/svg+xml' };
const server = http.createServer((req, res) => {
  let file = path.join(site, decodeURIComponent(new URL(req.url, 'http://x').pathname));
  if (!file.startsWith(site)) { res.writeHead(403); res.end(); return; }
  if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
    res.end(data);
  });
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const url = `http://127.0.0.1:${server.address().port}/`;

// ---- headless Firefox, over WebDriver BiDi ---------------------------------------------
const firefox = process.env.FIREFOX ?? (process.platform === 'darwin' ? '/Applications/Firefox.app/Contents/MacOS/firefox' : 'firefox');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'arcade-basic-firefox-'));
const port = 9300 + Math.floor(Math.random() * 600);
const browser = spawn(firefox, ['--headless', '--no-remote', '-profile', profile, '--remote-debugging-port', String(port), '-remote-allow-system-access'],
                      { stdio: 'ignore' });
let ws;
for (let i = 0; !ws; i++) {
  try {
    ws = await new Promise((resolve, reject) => {
      const s = new WebSocket(`ws://127.0.0.1:${port}/session`);
      s.onopen = () => resolve(s);
      s.onerror = () => reject(new Error('no connection'));
    });
  } catch (e) {
    if (i === 80) throw new Error(`Firefox (${firefox}) didn't start`);
    await sleep(250);
  }
}
let next = 0;
const waiting = new Map();
ws.onmessage = m => {
  const msg = JSON.parse(m.data);
  if (msg.id && waiting.has(msg.id)) { waiting.get(msg.id)(msg); waiting.delete(msg.id); }
};
const send = (method, params = {}) => new Promise((resolve, reject) => {
  const id = ++next;
  waiting.set(id, msg => msg.type === 'error' ? reject(new Error(`${method}: ${msg.error} ${msg.message}`)) : resolve(msg.result));
  ws.send(JSON.stringify({ id, method, params }));
});
const evaluate = async (context, js) => {
  const r = await send('script.evaluate', { expression: js, target: { context }, awaitPromise: true });
  if (r.type === 'exception') throw new Error(r.exceptionDetails.text);
  return r.result;
};
/** A JavaScript value from the page, as a plain one. */
const plain = v => v?.type === 'object' ? Object.fromEntries(v.value.map(([k, x]) => [k, plain(x)])) : v?.value;

await send('session.new', { capabilities: {} });
fs.mkdirSync(out, { recursive: true });
let failed = 0;
for (const shot of shots.filter(s => !only.length || only.includes(s.name))) {
  const { userContext } = await send('browser.createUserContext', {});
  const { context } = await send('browsingContext.create', { type: 'tab', userContext });
  try {
    await send('browsingContext.navigate', { context, url: `${url}#${shot.open}`, wait: 'complete' });
    const [width, height, devicePixelRatio] = shot.size;
    await send('browsingContext.setViewport', { context, viewport: { width, height }, devicePixelRatio });
    for (let i = 0; i < 160; i++) {
      if ((await evaluate(context, `document.getElementById('loading')?.textContent === ''`)).value) break;
      await sleep(250);
    }
    if (shot.gif) {
      await gif(context, shot);
    } else {
      const clip = plain(await evaluate(context, `(async () => { ${helpers} ${shot.js} })()`));
      await sleep(200);
      const { data } = await send('browsingContext.captureScreenshot', clip ? { context, clip: { type: 'box', ...clip } } : { context });
      fs.writeFileSync(path.join(out, `${shot.name}.png`), Buffer.from(data, 'base64'));
    }
    console.log(`${shot.name}: done`);
  } catch (e) {
    failed++;
    console.log(`${shot.name}: ${e.message}`);
  }
  await send('browsingContext.close', { context });
  await send('browser.removeUserContext', { userContext });
}

/** Record the screen (the canvas) while the program runs and the shot's script plays,
 *  and turn it into a GIF with ffmpeg. */
async function gif(context, shot) {
  const webm = path.join(work, `${shot.name}.webm`);
  const recorded = await evaluate(context, `(async () => { ${helpers}
    await run(2500);                                 // (once its screen is up: a recording takes its size from the first frame)
    const recorder = new MediaRecorder(canvas.captureStream(25), { mimeType: 'video/webm' });
    const chunks = [];
    recorder.ondataavailable = e => chunks.push(e.data);
    recorder.start();
    const ended = sleep(${shot.gif * 1000});
    ${shot.js}
    await ended;
    recorder.stop();
    await new Promise(r => recorder.onstop = r);
    const bytes = new Uint8Array(await new Blob(chunks).arrayBuffer());
    let s = '';
    for (const b of bytes) s += String.fromCharCode(b);
    return btoa(s);
  })()`);
  fs.writeFileSync(webm, Buffer.from(recorded.value, 'base64'));
  const filter = 'fps=10,scale=400:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=64:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle';
  const r = spawnSync('ffmpeg', ['-y', '-loglevel', 'error', '-i', webm, '-vf', filter, '-loop', '0', path.join(out, `${shot.name}.gif`)], { stdio: 'inherit' });
  if (r.error || r.status !== 0) throw new Error('ffmpeg failed (is it installed?)');
}

await send('session.end', {}).catch(() => {});
ws.close();
browser.kill();
server.close();
fs.rmSync(profile, { recursive: true, force: true });
process.exit(failed ? 1 : 0);
