// Arcade BASIC playground (spike). The .NET side (Program.cs) compiles a program
// and steps the bytecode VM; this page owns the loop: each animation frame it runs
// the VM for a slice of time, waits out SLEEPs, draws the newest frame on the
// canvas, and feeds the keyboard back. ?bench=1 runs the measurements and posts
// them to /results (for the local test server).
import { dotnet } from './_framework/dotnet.js';

const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
const P = (await getAssemblyExports(getConfig().mainAssemblyName)).Playground;
await runMain();
const readyMs = performance.now();

// StepStatus values (src/ArcadeBasic.Vm/StepResult.cs)
const RUNNING = 0, SLEEPING = 1, NEEDS_INPUT = 2, AWAITING_AUDIO = 3, HALTED = 4, NEEDS_POINT = 5;
const SLICE = 20_000;          // instructions per Step call
const FRAME_BUDGET_MS = 10;    // VM time allowed per animation frame

// The colours (GfxPalette): 256 of them, which SET COLOR MIX can change. A frame
// brings the whole palette with it whenever it has changed — always the first.
let palette = Array(256).fill('#ffffff');   // CSS colours
const paletteAbgr = new Uint32Array(256);   // the same, as ImageData pixels (little-endian ABGR)
// CanvasGraphicsDevice command codes.
const COLOR = 2, LINES = 3, AREA = 4, POINTS = 5, TEXT = 6, LINE_STYLE = 7, POINT_STYLE = 8, CELLS = 9, PALETTE = 10;
// SET LINE STYLE 1 solid, 2 dashed, 3 dotted — the SVG backend's dash patterns.
const DASHES = { 2: [12, 6], 3: [3, 5] };

const $ = id => document.getElementById(id);
const canvas = $('screen'), ctx = canvas.getContext('2d');
const source = $('source'), output = $('output'), stats = $('stats');
const inputRow = $('inputRow'), inputLine = $('inputLine');

let run = null;   // the running program's loop state
let lastTexts = [];   // the last drawn frame's text: [{ x, y, text }] in unit coordinates

function start(text, onHalt) {
  stop();
  output.textContent = '';
  const diagnostics = P.Load(text);
  if (diagnostics) { output.textContent = diagnostics; return false; }
  run = { sleepingUntil: 0, waiting: false, waitingPoint: false, began: performance.now(), ticks: 0, frames: 0, vmMs: 0, onHalt };
  $('stop').disabled = false;
  canvas.focus();
  requestAnimationFrame(tick);
  return true;
}

function stop() {
  if (run) P.Stop();
  run = null;
  $('stop').disabled = true;
  inputRow.style.display = 'none';
  canvas.style.cursor = '';
}

function tick() {
  const r = run;
  if (!r) return;
  r.ticks++;
  let halted = false;
  if (!r.waiting && !r.waitingPoint && performance.now() >= r.sleepingUntil) {
    const t0 = performance.now();
    for (;;) {
      const s = P.Step(SLICE);
      if (s === RUNNING || s === AWAITING_AUDIO) {        // no audio yet: don't wait for it
        if (performance.now() - t0 < FRAME_BUDGET_MS) continue;
        break;
      }
      if (s === SLEEPING) {
        // Wake-ups land on animation frames, so time each SLEEP from the previous
        // deadline (unless we've fallen well behind) to keep the program's frame rate
        // on average: SLEEP 0.04 alternates 2- and 3-frame waits = 25 fps, not 20.
        const now = performance.now();
        const base = now - r.sleepingUntil < 100 ? r.sleepingUntil : now;
        r.sleepingUntil = base + P.SleepSeconds() * 1000;
        break;
      }
      if (s === NEEDS_INPUT) { r.waiting = true; askForInput(); break; }
      if (s === NEEDS_POINT) { r.waitingPoint = true; canvas.style.cursor = 'crosshair'; break; }
      halted = true;
      break;
    }
    r.vmMs += performance.now() - t0;
  }
  appendOutput(P.TakeOutput());
  const frame = P.TakeFrame();
  if (frame.length) { drawFrame(frame, P.FrameText(), P.FrameCells()); r.frames++; }
  showStats(r);
  if (halted) {
    const error = P.Error();
    if (error) appendOutput('\n' + error + '\n');
    r.instructions = P.Instructions();
    stop();
    r.onHalt?.(r);
    return;
  }
  requestAnimationFrame(tick);
}

// The canvas is drawn at the screen's real pixel density (fitCanvas), and straight
// lines are snapped to the pixel grid, so every line is crisp and the same weight.
let lastFrame = null;   // [cmds, texts, cells] — redrawn when the canvas is resized or glow toggled

/** Size the canvas's backing store to its on-screen size in device pixels. */
function fitCanvas() {
  const dpr = window.devicePixelRatio || 1;
  const w = Math.max(1, Math.round(canvas.clientWidth * dpr)), h = Math.max(1, Math.round(canvas.clientHeight * dpr));
  if (canvas.width === w && canvas.height === h) return;
  canvas.width = w;
  canvas.height = h;
  if (lastFrame) drawFrame(...lastFrame);
}
new ResizeObserver(fitCanvas).observe(canvas);

/** Canvas geometry for the current backing store: the unit square, inset by half a
 *  line width so lines on its edges aren't clipped. */
function geometry() {
  const dpr = window.devicePixelRatio || 1;
  const W = canvas.width, H = canvas.height;
  const lineWidth = Math.max(1, Math.round(1.5 * dpr));
  const inset = Math.ceil(lineWidth / 2);
  return { W, H, dpr, lineWidth, inset, w: W - 2 * inset, h: H - 2 * inset };
}

function drawFrame(cmds, texts, cells = []) {
  lastFrame = [cmds, texts, cells];
  let i = 0;
  if (cmds[0] === PALETTE) { setPalette(cmds, 1); i = 1 + 256; }
  const { W, H, dpr, lineWidth, inset, w, h } = geometry();
  const X = x => inset + x * w, Y = y => inset + (1 - y) * h;   // unit square, origin bottom-left
  // Snap to the pixel grid: an odd-width line's centre on a pixel centre, an even one's on an edge.
  const half = lineWidth % 2 ? 0.5 : 0;
  const snap = v => Math.round(v - half) + half;
  const glow = $('glow').checked;
  const colors = [1, 1, 1, 1];                      // point, line, text, area
  let lineStyle = 1, pointStyle = 3;                // GraphicsState's defaults
  lastTexts = [];
  ctx.shadowBlur = 0;
  ctx.fillStyle = palette[0];                       // colour 0 is the background
  ctx.fillRect(0, 0, W, H);
  ctx.imageSmoothingEnabled = false;                // cells stay crisp squares when scaled up
  let atlas = null;                                 // this frame's cell arrays, uploaded on first use
  ctx.lineWidth = lineWidth;
  ctx.lineJoin = 'miter';
  ctx.lineCap = 'square';
  // An 80 x 24-cell screen, like the terminal's: a monospace glyph is ~0.6 em wide,
  // so size the font to fit 80 columns.
  ctx.font = `${Math.round(w / 80 / 0.6)}px ui-monospace, Menlo, monospace`;
  ctx.textBaseline = 'alphabetic';
  const paint = index => {
    const c = palette[index] ?? palette[1];
    ctx.strokeStyle = ctx.fillStyle = c;
    if (glow) { ctx.shadowColor = c; ctx.shadowBlur = 6 * dpr; }
  };
  while (i < cmds.length) {
    const op = cmds[i++];
    if (op === COLOR) { colors[cmds[i]] = cmds[i + 1]; i += 2; continue; }
    if (op === CELLS) {
      // MAT GRAPH CELLS: its block of the atlas, scaled into the rectangle (edges on
      // whole pixels, so neighbouring arrays meet without seams), cut to what's visible.
      const [l, b, r, t, cols, rows, vl, vb, vr, vt, ax, ay] = cmds.slice(i, i + 12);
      i += 12;
      if (!atlas) atlas = uploadAtlas(cells);
      const x0 = Math.round(X(l)), x1 = Math.round(X(r)), y0 = Math.round(Y(t)), y1 = Math.round(Y(b));
      const cx0 = Math.round(X(vl)), cx1 = Math.round(X(vr)), cy0 = Math.round(Y(vt)), cy1 = Math.round(Y(vb));
      const cut = cx0 > x0 || cx1 < x1 || cy0 > y0 || cy1 < y1;
      ctx.shadowBlur = 0;
      if (cut) { ctx.save(); ctx.beginPath(); ctx.rect(cx0, cy0, cx1 - cx0, cy1 - cy0); ctx.clip(); }
      ctx.drawImage(atlas, ax, ay, cols, rows, x0, y0, x1 - x0, y1 - y0);
      if (cut) ctx.restore();
      continue;
    }
    if (op === LINE_STYLE) { lineStyle = cmds[i++]; continue; }
    if (op === POINT_STYLE) { pointStyle = cmds[i++]; continue; }
    if (op === TEXT) {
      paint(colors[2]);                             // the point is the text's baseline (ECMA-116 LEFT, BASE)
      ctx.fillText(texts[cmds[i + 2]], X(cmds[i]), Y(cmds[i + 1]));
      lastTexts.push({ x: cmds[i], y: cmds[i + 1], text: texts[cmds[i + 2]] });
      i += 3;
      continue;
    }
    const n = cmds[i++];
    paint(colors[op === POINTS ? 0 : op === LINES ? 1 : 3]);
    ctx.setLineDash(op === LINES ? (DASHES[lineStyle] ?? []).map(d => d * dpr) : []);
    ctx.beginPath();
    for (let k = 0; k < n; k++, i += 2) {
      let x = X(cmds[i]), y = Y(cmds[i + 1]);
      if (op === LINES) { x = snap(x); y = snap(y); }
      else if (op === AREA) { x = Math.round(x); y = Math.round(y); }
      if (op === POINTS) marker(x, y, pointStyle, dpr);
      else if (k === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    if (op === LINES || (op === POINTS && pointStyle !== 1)) ctx.stroke();
    else { if (op === AREA) ctx.closePath(); ctx.fill(); }
  }
}

/** The colours a PALETTE command carries: 256 ARGB values from cmds[at]. */
function setPalette(cmds, at) {
  palette = [];
  for (let k = 0; k < 256; k++) {
    const c = cmds[at + k] >>> 0, r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
    palette.push(`rgb(${r},${g},${b})`);
    paletteAbgr[k] = (0xff000000 | (b << 16) | (g << 8) | r) >>> 0;
  }
}

/** Put a frame's cell atlas (width, height, then colour indices row by row) into an
 *  image, in one upload, for drawImage to scale pieces of. Colour -1 is see-through
 *  (a transparent pixel), so a cell array can be a sprite. */
const atlasCanvas = document.createElement('canvas'), atlasCtx = atlasCanvas.getContext('2d');
function uploadAtlas(cells) {
  const w = cells[0], h = cells[1];
  if (atlasCanvas.width !== w || atlasCanvas.height !== h) { atlasCanvas.width = w; atlasCanvas.height = h; }
  const image = atlasCtx.createImageData(w, h), pixels = new Uint32Array(image.data.buffer);
  for (let k = 0; k < w * h; k++) {
    const c = cells[2 + k];
    pixels[k] = c < 0 ? 0 : paletteAbgr[c] ?? paletteAbgr[1];
  }
  atlasCtx.putImageData(image, 0, 0);
  return atlasCanvas;
}

// SET POINT STYLE 1 dot, 2 plus, 3 asterisk (adds to the current path).
function marker(x, y, style, dpr) {
  const r = 4 * dpr;
  if (style === 1) { const d = 2.5 * dpr; ctx.moveTo(x + d, y); ctx.arc(x, y, d, 0, 2 * Math.PI); return; }
  ctx.moveTo(x - r, y); ctx.lineTo(x + r, y);
  ctx.moveTo(x, y - r); ctx.lineTo(x, y + r);
  if (style === 3) {
    const d = r * 0.7;
    ctx.moveTo(x - d, y - d); ctx.lineTo(x + d, y + d);
    ctx.moveTo(x - d, y + d); ctx.lineTo(x + d, y - d);
  }
}

function appendOutput(text) {
  if (!text) return;
  output.textContent += text;
  output.scrollTop = output.scrollHeight;
}

function askForInput() {
  inputRow.style.display = 'flex';
  inputLine.value = '';
  inputLine.focus();
}

inputRow.addEventListener('submit', e => {
  e.preventDefault();
  if (!run) return;
  appendOutput(inputLine.value + '\n');     // a terminal echoes what you type
  P.ProvideInput(inputLine.value);
  inputRow.style.display = 'none';
  run.waiting = false;
  canvas.focus();
});

let lastStats = 0;
function showStats(r, force = false) {
  const now = performance.now();
  if (!force && now - lastStats < 500) return;
  lastStats = now;
  const secs = (now - r.began) / 1000;
  const mips = P.Instructions() / Math.max(secs, 1e-3) / 1e6;
  stats.textContent = `${(r.frames / secs).toFixed(1)} frames/s · VM ${(r.vmMs / Math.max(r.ticks, 1)).toFixed(2)} ms per animation frame · ` +
                      `${mips.toFixed(2)} M instructions/s · ready in ${(readyMs / 1000).toFixed(2)} s`;
}

// Keys go to the program only while the canvas has focus (so typing in the editor doesn't).
canvas.addEventListener('keydown', e => {
  if (e.ctrlKey || e.metaKey || e.altKey) return;
  P.KeyDown(e.code, e.key, e.repeat);
  e.preventDefault();
});
canvas.addEventListener('keyup', e => { P.KeyUp(e.code); });
canvas.addEventListener('blur', () => P.ReleaseKeys());

// The pointer, in the canvas's unit square (origin bottom-left), for GET MOUSE and
// LOCATE POINT. Capturing it keeps a drag's moves and release coming even if the
// pointer leaves the canvas.
const unitPoint = e => {
  const r = canvas.getBoundingClientRect(), { inset, w, h } = geometry();
  const px = (e.clientX - r.left - canvas.clientLeft) * canvas.width / canvas.clientWidth;
  const py = (e.clientY - r.top - canvas.clientTop) * canvas.height / canvas.clientHeight;
  return [(px - inset) / w, 1 - (py - inset) / h];
};
canvas.addEventListener('pointermove', e => P.PointerMove(...unitPoint(e)));
canvas.addEventListener('pointerdown', e => {
  if (e.button !== 0) return;
  try { canvas.setPointerCapture(e.pointerId); } catch { /* not a live pointer (synthetic) */ }
  canvas.focus();
  P.PointerDown(...unitPoint(e));
  e.preventDefault();
  if (run?.waitingPoint) { run.waitingPoint = false; canvas.style.cursor = ''; }
});
canvas.addEventListener('pointerup', e => { if (e.button === 0) P.PointerUp(...unitPoint(e)); });
canvas.addEventListener('pointercancel', () => P.PointerCancel());
canvas.addEventListener('lostpointercapture', () => P.PointerCancel());

// Example picker
$('glow').addEventListener('change', () => { if (lastFrame) drawFrame(...lastFrame); });
fitCanvas();

const examples = $('examples');
for (const name of P.Examples()) examples.add(new Option(name, name));
examples.value = 'invaders.bas';
const loadExample = () => { source.value = P.ExampleSource(examples.value); };
examples.addEventListener('change', () => { stop(); loadExample(); });
loadExample();
$('run').addEventListener('click', () => start(source.value));
$('stop').addEventListener('click', stop);
$('run').disabled = false;
$('loading').textContent = '';

// -- ?bench=1: measure, then POST the numbers to /results --------------------
const params = new URLSearchParams(location.search);
if (params.has('bench')) bench();
if (params.get('selftest') === 'kanban') kanbanSelftest();

// ?selftest=kanban: drag card #5 onto Done with real DOM pointer events, type Q, report.
async function kanbanSelftest() {
  const pause = () => new Promise(r => setTimeout(r, 300));
  const at = t => t && [t.x + 0.02, t.y + 0.01];
  const pointer = (type, [x, y]) => {
    const r = canvas.getBoundingClientRect(), { inset, w, h } = geometry();
    const toClient = (px, size, offset, client) => offset + client + px * size;
    canvas.dispatchEvent(new PointerEvent(type, {
      clientX: toClient(inset + x * w, canvas.clientWidth / canvas.width, r.left, canvas.clientLeft),
      clientY: toClient(inset + (1 - y) * h, canvas.clientHeight / canvas.height, r.top, canvas.clientTop),
      button: 0, buttons: type === 'pointerup' ? 0 : 1, pointerId: 1, bubbles: true }));
  };
  const key = (k, code) => {
    canvas.dispatchEvent(new KeyboardEvent('keydown', { key: k, code, bubbles: true }));
    canvas.dispatchEvent(new KeyboardEvent('keyup', { key: k, code, bubbles: true }));
  };
  const result = {};
  try {
    examples.value = 'kanban.bas';
    loadExample();
    start(source.value);
    await pause();
    result.mouseMode = lastTexts.some(t => t.text.includes('type a command'));
    if (params.has('snapshot')) {                  // hover over card #6 first
      pointer('pointermove', at(lastTexts.find(t => t.text.startsWith('#6 '))));
      await pause();
      result.hoverSnapshot = canvas.toDataURL('image/png');
    }
    const card = at(lastTexts.find(t => t.text.startsWith('#5 ')));
    const done = [lastTexts.find(t => t.text.startsWith('DONE (')).x + 0.02, 0.5];
    pointer('pointerdown', card); await pause();
    pointer('pointermove', done); await pause();
    result.copiesWhileDragging = lastTexts.filter(t => t.text.startsWith('#5 ')).length;
    if (params.has('snapshot')) result.snapshot = canvas.toDataURL('image/png');   // mid-drag picture
    pointer('pointerup', done); await pause();
    result.status = lastTexts.find(t => t.text.startsWith('Status:'))?.text;
    result.lanes = lastTexts.filter(t => /^(TO-DO|IN-PROGRESS|DONE) \(/.test(t.text)).map(t => t.text);
    key('q', 'KeyQ'); await pause();
    result.typed = lastTexts.find(t => t.text.startsWith('> '))?.text;
    key('Enter', 'Enter'); await pause();
    result.stillRunning = run !== null;
  } catch (e) {
    result.error = String(e);
  }
  await fetch('/results', { method: 'POST', body: JSON.stringify(result, null, 2) });
}

async function bench() {
  const results = { userAgent: navigator.userAgent, readyMs, framework: frameworkResources() };

  const mandelbrot = P.ExampleSource('mandelbrot.bas');
  const compute = {
    'mandelbrot (decimal)': mandelbrot,
    'mandelbrot (native)': mandelbrot + '\nOPTION ARITHMETIC NATIVE\n',
    'empty loop 1M (native)': 'OPTION ARITHMETIC NATIVE\nFOR I = 1 TO 1000000\nNEXT I\nEND\n',
  };
  for (const [name, text] of Object.entries(compute)) {
    P.Load(text);
    const t0 = performance.now();
    while (P.Step(1_000_000) !== HALTED) { /* flat out */ }
    results[name] = { ms: performance.now() - t0, instructions: P.Instructions(), outputChars: P.TakeOutput().length, error: P.Error() };
  }

  for (const game of ['invaders.bas', 'breakout.bas', 'maze3d.bas']) results[game] = await playFor(game, 10_000);

  await fetch('/results', { method: 'POST', body: JSON.stringify(results, null, 2) });
  document.title = 'bench done';
}

function playFor(name, ms) {
  return new Promise(resolve => {
    const summary = (r, state) => r && ({
      state, seconds: (performance.now() - r.began) / 1000, ticks: r.ticks, frames: r.frames,
      vmMsPerTick: r.vmMs / Math.max(r.ticks, 1), instructions: r.instructions ?? P.Instructions(),
    });
    let done = false;
    start(P.ExampleSource(name), r => { if (!done) { done = true; resolve(summary(r, 'halted')); } });
    const r = run;
    setTimeout(() => {
      if (done) return;
      done = true;
      r.instructions = P.Instructions();
      const s = summary(r, 'still running');
      if (params.has('snapshot')) s.snapshot = canvas.toDataURL('image/png');   // its last frame
      stop();
      resolve(s);
    }, ms);
  });
}

function frameworkResources() {
  const entries = performance.getEntriesByType('resource').filter(e => e.name.includes('/_framework/'));
  const sum = key => entries.reduce((t, e) => t + (e[key] || 0), 0);
  return { files: entries.length, decodedBytes: sum('decodedBodySize'), transferBytes: sum('transferSize') };
}
