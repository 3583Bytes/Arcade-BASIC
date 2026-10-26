// Arcade BASIC in the browser: an editor (editor.js) with a tab per program
// (documents.js), and the program's screen. The .NET side (Program.cs) checks and
// compiles programs and steps the bytecode VM; this page owns the loop: each
// animation frame it runs the VM for a slice of time, waits out SLEEPs, draws the
// newest frame on the canvas, and feeds the keyboard back. ?bench=1 runs the
// measurements and ?selftest=… drive the page; both post what they saw to
// /results (for the local test server).
import { dotnet } from './_framework/dotnet.js';
import { createEditor } from './editor.js';
import { createDocuments } from './documents.js';
import { createAudio, QUEUE } from './audio.js';

// .NET starts loading straight away; the editor works meanwhile.
const loadingNet = (async () => {
  const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  await runMain();
  return exports.Playground;
})();
let P = null;       // the .NET side, once loaded
let readyMs = 0;

// StepStatus values (src/ArcadeBasic.Vm/StepResult.cs)
const RUNNING = 0, SLEEPING = 1, NEEDS_INPUT = 2, AWAITING_AUDIO = 3, HALTED = 4, NEEDS_POINT = 5, PAUSED = 6;
// DebugStep values (src/ArcadeBasic.Vm/Debugging.cs)
const PAUSE = 0, INTO = 1, OVER = 2, OUT = 3;
const SLICE = 20_000;          // instructions per Step call
const FRAME_BUDGET_MS = 10;    // VM time allowed per animation frame
// A foreground SOUND/PLAY resumes this soon before its tones end, so the next
// statement's tones follow on without a gap (the page only steps on animation frames).
const AUDIO_LEAD_MS = 20;

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
const output = $('output'), stats = $('stats');
const inputRow = $('inputRow'), inputLine = $('inputLine');
const isMac = /Mac|iPhone|iPad/.test(navigator.platform);
const store = {
  get(key) { try { return localStorage.getItem(key); } catch { return null; } },
  set(key, value) { try { localStorage.setItem(key, value); } catch { /* no storage */ } },
};

const audio = createAudio();
let run = null;   // the running program's loop state
let lastTexts = [];   // the last drawn frame's text: [{ x, y, text }] in unit coordinates

// -- The editor and its tabs ------------------------------------------------------

const editor = createEditor($('editor'), {
  onUpdate(update) {
    documents.update(update);
    if (update.selectionSet || update.docChanged) showCursor();
  },
  onProblems: showProblems,
  onBreakpoints(lines) {
    if (run && run.doc === documents.active) P.SetBreakpoints(lines);   // takes effect at once
    documents.persist();
  },
});
const documents = createDocuments({
  editor, tabs: $('tabs'), ask, notify, onShow: shown,
  exampleSource: name => P.ExampleSource(name).replace(/\r\n?/g, '\n'),
});

/** A different program is showing, or the one showing was saved or changed. */
function shown(doc) {
  document.title = `${doc.dirty ? '● ' : ''}${doc.name} — Arcade BASIC`;
  showFileInfo();
  showCursor();
  // The address names an example (…/#breakout), so a link can open it.
  const hash = doc.kind === 'example' ? '#' + doc.name.replace(/\.bas$/, '') : '';
  if (location.hash !== hash) history.replaceState(null, '', location.pathname + location.search + hash);
}

function showCursor() {
  const state = editor.state, head = state.selection.main.head, line = state.doc.lineAt(head);
  $('cursorPos').textContent = `Ln ${line.number}, Col ${head - line.from + 1}`;
}

function showFileInfo() {
  $('fileInfo').textContent = documents.active ? documents.describe(documents.active) : '';
}

let noticeTimer = 0;
/** A short message in the status line (then back to describing the program). */
function notify(text) {
  $('fileInfo').textContent = text;
  clearTimeout(noticeTimer);
  noticeTimer = setTimeout(showFileInfo, 4000);
}

function showProblems(found) {
  const errors = found.filter(d => d.severity === 'error').length;
  const warnings = found.filter(d => d.severity === 'warning').length;
  const button = $('problems');
  if (!errors && !warnings) { button.textContent = '✓ No problems'; return; }
  const part = (n, cls, icon, word) => {
    const span = document.createElement('span');
    span.className = cls;
    span.textContent = `${icon} ${n} ${word}${n === 1 ? '' : 's'}`;
    return span;
  };
  button.replaceChildren(...[errors && part(errors, 'errors', '✖', 'error'), warnings && part(warnings, 'warnings', '⚠', 'warning')]
    .filter(Boolean).flatMap((s, i) => i ? [' ', s] : [s]));
}
$('problems').addEventListener('click', () => editor.openProblems());

// -- Questions ------------------------------------------------------------------------

const dialog = $('dialog');
/** Ask in a dialog: resolves to the chosen button's value (or, with input, the text
 *  typed), or null for Cancel / Escape. */
function ask({ title, message = '', input = null, buttons }) {
  return new Promise(resolve => {
    dialog.querySelector('h2').textContent = title;
    const text = dialog.querySelector('p');
    text.textContent = message;
    text.hidden = !message;
    const field = dialog.querySelector('input');
    field.hidden = input == null;
    field.value = input ?? '';
    const row = dialog.querySelector('.buttons');
    row.replaceChildren(...buttons.map(b => {
      const button = document.createElement('button');
      button.textContent = b.label;
      button.value = b.value ?? '';
      if (b.kind) button.className = b.kind;
      return button;
    }));
    const primary = buttons.find(b => b.kind === 'primary')?.value ?? '';
    field.onkeydown = e => { if (e.key === 'Enter') { e.preventDefault(); dialog.close(primary); } };
    dialog.returnValue = '';
    dialog.onclose = () => {
      const value = dialog.returnValue;
      resolve(!value ? null : input != null ? field.value.trim() || null : value);
    };
    dialog.showModal();
    if (input != null) { field.focus(); field.select(); } else row.querySelector('.primary')?.focus();
  });
}

// -- Running a program -------------------------------------------------------------------

/** Run the program showing; with stopAtFirst, paused before its first statement. */
function runActive(stopAtFirst = false) {
  const doc = documents.active;
  if (!P || !doc) return;
  audio.unlock();                        // (a click or key press: the browser now allows sound)
  start(editor.state.doc.toString(), null, doc, stopAtFirst);
}

function start(text, onHalt, doc = null, stopAtFirst = false) {
  stop();
  output.replaceChildren();
  editor.clearRuntimeError();
  const diagnostics = P.Load(text);
  if (diagnostics) {
    appendOutput(diagnostics, 'error');
    if (doc) editor.goToFirstProblem();
    return false;
  }
  audio.reset();
  if (doc) P.SetBreakpoints(editor.breakpointLines(doc === documents.active ? editor.state : doc.state));
  if (stopAtFirst) P.PauseAt(PAUSE);
  lastVariables = new Map();
  run = { sleepingUntil: 0, audioUntil: 0, waiting: false, waitingPoint: false, paused: false, began: performance.now(),
          ticks: 0, frames: 0, vmMs: 0, onHalt, doc };
  documents.setRunning(doc);
  $('stop').disabled = false;
  showDebugControls();
  canvas.focus();
  requestAnimationFrame(tick);
  return true;
}

/** Stop the program; its sound stops too unless it ended by itself (background music plays on). */
function stop(silence = true) {
  if (silence) audio.stop();
  if (run) { P.Stop(); documents.setRunning(null); }
  if (run?.paused) leavePause();
  run = null;
  $('stop').disabled = true;
  inputRow.style.display = 'none';
  canvas.style.cursor = '';
  showDebugControls();
}

function tick() {
  const r = run;
  if (!r) return;
  r.ticks++;
  let halted = false;
  const playing = audio.queued();
  const ready = performance.now() >= Math.max(r.sleepingUntil, r.audioUntil) && playing <= QUEUE;
  if (!r.waiting && !r.waitingPoint && !r.paused && ready) {
    const t0 = performance.now();
    P.SetTonesPlaying(playing);
    for (;;) {
      const s = P.Step(SLICE);
      if (s === RUNNING) {
        if (performance.now() - t0 < FRAME_BUDGET_MS) continue;
        break;
      }
      if (s === AWAITING_AUDIO) {     // a foreground SOUND, BEEP or PLAY; or a full background queue
        audio.play(P.TakeTones());
        if (P.MaxQueuedTones() === 0) r.audioUntil = performance.now() + audio.remaining() - AUDIO_LEAD_MS;
        break;                        // (with a full queue, the wait is the `playing <= QUEUE` above)
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
      if (s === PAUSED) { enterPause(r); break; }
      halted = true;
      break;
    }
    r.vmMs += performance.now() - t0;
  }
  if (!r.paused && variablesShowing() && performance.now() - lastVariablesShown > 500) showVariables(false);   // live
  audio.play(P.TakeTones());                              // background music (MB)
  appendOutput(P.TakeOutput());
  const frame = P.TakeFrame();
  if (frame.length) { drawFrame(frame, P.FrameText(), P.FrameCells()); r.frames++; }
  if (!r.paused) showStats(r, halted);
  if (halted) {
    const error = P.Error();
    const line = error ? P.CurrentLine() : 0;
    if (error) appendOutput('\n' + error + '\n', 'error');
    r.instructions = P.Instructions();
    stop(false);
    if (error && r.doc && documents.all.includes(r.doc)) {   // point at the line that failed
      if (documents.active !== r.doc) documents.show(r.doc);
      editor.showRuntimeError(line);
    }
    r.onHalt?.(r);
    return;
  }
  requestAnimationFrame(tick);
}

// -- The debugger ----------------------------------------------------------------------------
// Breakpoints live in the editor (a gutter of red dots, F9); the VM stops before a
// statement on one of their lines (Paused). Then the page marks the line, shows the
// variables, and the controls step: Over (F10), Into (F11), Out (Shift+F11), or
// Continue (F5). Pause (F6) stops a running program at its next statement.

function enterPause(r) {
  r.paused = true;
  if (r.doc && documents.all.includes(r.doc) && documents.active !== r.doc) documents.show(r.doc);
  const line = P.CurrentLine();
  if (documents.active === r.doc) editor.showPaused(line);
  showVariables(true);
  showPanel('variables');
  editor.setValueLookup(valueOf);
  stats.textContent = `Paused at line ${line} · F5 continue · F10 step over · F11 step into · Shift+F11 step out`;
  showDebugControls();
}

function leavePause() {
  editor.showPaused(0);
  editor.setValueLookup(null);
}

/** From a pause: carry on (no step), or step (INTO, OVER, OUT). */
function resume(step = null) {
  if (!run?.paused) return;
  if (step != null) P.PauseAt(step);
  run.paused = false;
  run.resumedAt = performance.now();
  leavePause();
  showDebugControls();
}

/** Step Over / Into: from a pause, a step; with nothing running, run and stop at the first line. */
function debugStep(step) {
  if (run?.paused) resume(step);
  else if (!run && step !== OUT) runActive(true);
}

/** F5 / the Run button: continue from a pause; otherwise run (again) — but not as the
 *  second half of a quick double press, or a held key, that just continued. */
function runOrContinue(event) {
  if (run?.paused) resume();
  else if (event?.repeat || (run && performance.now() - (run.resumedAt ?? 0) < 400)) return;
  else runActive();
}

function pauseRunning() {
  if (run && !run.paused) P.PauseAt(PAUSE);
}

function showDebugControls() {
  const paused = !!run?.paused, running = !!run && !paused;
  $('run').textContent = paused ? '▶ Continue' : '▶ Run';
  $('run').title = paused ? 'Continue (F5)' : `Run (F5 or ${isMac ? '⌘' : 'Ctrl+'}Enter)`;
  $('pause').disabled = !running;
  $('stepOver').disabled = !P || running;
  $('stepInto').disabled = !P || running;
  $('stepOut').disabled = !paused;
}

// The Output and Variables tabs under the screen.
function showPanel(name) {
  for (const tab of document.querySelectorAll('.ptab')) tab.setAttribute('aria-selected', String(tab.dataset.panel === name));
  output.hidden = name !== 'output';
  $('clearOutput').hidden = name !== 'output';
  $('variables').hidden = name !== 'variables';
  if (name === 'variables' && run && !run.paused) showVariables(false);
}
const variablesShowing = () => !$('variables').hidden;
for (const tab of document.querySelectorAll('.ptab')) tab.addEventListener('click', () => showPanel(tab.dataset.panel));

let lastVariables = new Map();      // "frame/name" → what it showed, to mark what a step changed
let lastSnapshot = { frames: [] };
let lastVariablesShown = 0;
const openArrays = new Set(), closedFrames = new Set();

/** The variables of each call in progress (Playground.Variables), innermost first. */
function showVariables(markChanges) {
  lastVariablesShown = performance.now();
  const view = $('variables');
  if (!P || !run) {                                // (after a run, its last variables stay)
    if (!view.childElementCount) {
      const note = document.createElement('p');
      note.className = 'note';
      note.textContent = 'Run the program to see its variables here; they update as it runs. Click beside a line number (or press F9) for a breakpoint.';
      view.replaceChildren(note);
    }
    return;
  }
  const snapshot = JSON.parse(P.Variables());
  const seen = new Map();
  const frames = snapshot.frames.map(frame => {
    const section = document.createElement('details');
    section.className = 'frame';
    const title = frame.name || 'Main program';
    section.open = !closedFrames.has(title);
    section.addEventListener('toggle', () => section.open ? closedFrames.delete(title) : closedFrames.add(title));
    const summary = document.createElement('summary');
    const at = document.createElement('a');
    at.href = '#';
    at.textContent = `line ${frame.line}`;
    at.addEventListener('click', e => { e.preventDefault(); if (run?.doc === documents.active) editor.goToLine(frame.line); });
    summary.append(title, ' · ', at);
    const table = document.createElement('table');
    for (const v of frame.vars) {
      const key = `${frame.name}/${v.name}`;
      const shown = 'dims' in v ? JSON.stringify([v.dims, v.items]) : v.value;
      seen.set(key, shown);
      const row = table.insertRow();
      if (markChanges && lastVariables.has(key) && lastVariables.get(key) !== shown) row.className = 'changed';
      row.insertCell().textContent = v.name;
      const cell = row.insertCell();
      if ('dims' in v) cell.append(arrayView(v, key));
      else {
        cell.textContent = v.value;
        if (v.unset) { cell.className = 'unset'; cell.title = 'not assigned yet'; }
      }
    }
    if (!frame.vars.length) table.insertRow().insertCell().textContent = 'no variables';
    section.append(summary, table);
    return section;
  });
  if (!frames.length) return;                       // (it has ended: keep the last ones)
  view.replaceChildren(...frames);
  lastVariables = seen;
  lastSnapshot = snapshot;
}

/** An array: its bounds, and (opened) its elements — a grid for a small table. */
function arrayView(v, key) {
  if (!v.dims) {
    const span = document.createElement('span');
    span.className = 'unset';
    span.textContent = 'not dimensioned yet';
    return span;
  }
  const box = document.createElement('details');
  const summary = document.createElement('summary');
  summary.textContent = `(${v.dims.map(([lo, hi]) => `${lo} TO ${hi}`).join(', ')}) · ${v.total} ${v.name.endsWith('$') ? 'strings' : 'numbers'}`;
  box.append(summary);
  const fill = () => {
    if (box.childElementCount > 1) return;
    box.append(elements(v));
  };
  box.open = openArrays.has(key);
  if (box.open) fill();
  box.addEventListener('toggle', () => {
    if (box.open) { openArrays.add(key); fill(); } else openArrays.delete(key);
  });
  return box;
}

function elements(v) {
  const wrap = document.createElement('div');
  wrap.className = 'elements';
  const [rows, cols] = v.dims.map(([lo, hi]) => hi - lo + 1);
  if (v.dims.length === 2 && cols <= 64 && v.items.length === v.total) {
    const grid = document.createElement('table');
    grid.className = 'grid';
    const head = grid.insertRow();
    head.insertCell();
    for (let c = 0; c < cols; c++) head.insertCell().textContent = v.dims[1][0] + c;
    for (let r = 0; r < rows; r++) {
      const row = grid.insertRow();
      row.insertCell().textContent = v.dims[0][0] + r;
      for (let c = 0; c < cols; c++) row.insertCell().textContent = v.items[r * cols + c];
    }
    wrap.append(grid);
  } else {
    // Each element with its subscripts, counted row by row (the last subscript fastest).
    const sizes = v.dims.map(([lo, hi]) => hi - lo + 1);
    v.items.forEach((item, i) => {
      const subs = [];
      for (let d = sizes.length - 1, n = i; d >= 0; d--) { subs.unshift(v.dims[d][0] + n % sizes[d]); n = Math.floor(n / sizes[d]); }
      const cell = document.createElement('span');
      const index = document.createElement('i');
      index.textContent = `(${subs.join(',')})`;
      cell.append(index, ' ', item);
      wrap.append(cell);
    });
    if (v.items.length < v.total) wrap.append(` … the first ${v.items.length} of ${v.total}`);
  }
  return wrap;
}

/** Pointing at a name while paused: its value (innermost call first). */
function valueOf(name) {
  if (!run?.paused || run.doc !== documents.active) return null;
  const key = name.toUpperCase();
  for (const frame of lastSnapshot.frames) {
    const v = frame.vars.find(x => x.name.toUpperCase() === key);
    if (!v) continue;
    if (!('dims' in v)) return v.value;
    if (!v.dims) return 'an array, not dimensioned yet';
    const shown = v.items.slice(0, 8).join(', ');
    return `(${v.dims.map(([lo, hi]) => `${lo} TO ${hi}`).join(', ')}): ${shown}${v.total > 8 ? ', …' : ''}`;
  }
  return null;
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

function appendOutput(text, cls = '') {
  if (!text) return;
  const last = output.lastChild;
  if (!cls && last?.nodeType === Node.TEXT_NODE) last.appendData(text);
  else if (!cls) output.append(text);
  else {
    const span = document.createElement('span');
    span.className = cls;
    span.textContent = text;
    output.append(span);
  }
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
$('clearOutput').addEventListener('click', () => output.replaceChildren());

let lastStats = 0;
function showStats(r, finished = false) {
  const now = performance.now();
  if (!finished && now - lastStats < 500) return;
  lastStats = now;
  const secs = (now - r.began) / 1000;
  const instructions = P.Instructions();
  if (finished) {
    stats.textContent = `Ran ${secs < 10 ? secs.toFixed(2) : secs.toFixed(1)} s · ${instructions.toLocaleString()} instructions`;
    return;
  }
  stats.textContent = `${(r.frames / secs).toFixed(1)} frames/s · VM ${(r.vmMs / Math.max(r.ticks, 1)).toFixed(2)} ms per frame · ` +
                      `${(instructions / Math.max(secs, 1e-3) / 1e6).toFixed(2)} M instructions/s`;
}

// Keys go to the program only while the canvas has focus (so typing in the editor
// doesn't); the page's own shortcuts (F5, Ctrl+S, …) are taken before it sees them.
canvas.addEventListener('keydown', e => {
  audio.unlock();
  if (e.ctrlKey || e.metaKey || e.altKey) return;
  P.KeyDown(e.code, e.key, e.repeat);
  e.preventDefault();
});
canvas.addEventListener('keyup', e => { P.KeyUp(e.code); });
canvas.addEventListener('blur', () => P?.ReleaseKeys());

// The pointer, in the canvas's unit square (origin bottom-left), for GET MOUSE and
// LOCATE POINT. Capturing it keeps a drag's moves and release coming even if the
// pointer leaves the canvas.
const unitPoint = e => {
  const r = canvas.getBoundingClientRect(), { inset, w, h } = geometry();
  const px = (e.clientX - r.left - canvas.clientLeft) * canvas.width / canvas.clientWidth;
  const py = (e.clientY - r.top - canvas.clientTop) * canvas.height / canvas.clientHeight;
  return [(px - inset) / w, 1 - (py - inset) / h];
};
canvas.addEventListener('pointermove', e => P?.PointerMove(...unitPoint(e)));
canvas.addEventListener('pointerdown', e => {
  if (e.button !== 0 || !P) return;
  audio.unlock();
  try { canvas.setPointerCapture(e.pointerId); } catch { /* not a live pointer (synthetic) */ }
  canvas.focus();
  P.PointerDown(...unitPoint(e));
  e.preventDefault();
  if (run?.waitingPoint) { run.waitingPoint = false; canvas.style.cursor = ''; }
});
canvas.addEventListener('pointerup', e => { if (e.button === 0) P?.PointerUp(...unitPoint(e)); });
canvas.addEventListener('pointercancel', () => P?.PointerCancel());
canvas.addEventListener('lostpointercapture', () => P?.PointerCancel());

const glowBox = $('glow');
glowBox.checked = store.get('arcade-basic.glow') === '1';
glowBox.addEventListener('change', () => {
  store.set('arcade-basic.glow', glowBox.checked ? '1' : '0');
  if (lastFrame) drawFrame(...lastFrame);
});
fitCanvas();

const soundBox = $('sound');
soundBox.checked = store.get('arcade-basic.sound') !== '0';
audio.muted = !soundBox.checked;
soundBox.addEventListener('change', () => {
  store.set('arcade-basic.sound', soundBox.checked ? '1' : '0');
  audio.muted = !soundBox.checked;
  if (soundBox.checked) audio.unlock();
});

// -- The page's controls ------------------------------------------------------------------

// Shortcut labels as this computer writes them (⌘S on a Mac, Ctrl+S elsewhere).
for (const el of document.querySelectorAll('[data-key]')) {
  const key = el.dataset.key;
  el.textContent = isMac
    ? key.replace(/^Mod$/, '⌘').replace('Mod+', '⌘').replace('Shift+', '⇧').replace('Alt+', '⌥')
    : key.replace('Mod', 'Ctrl');
}
$('stop').title = 'Stop (Shift+F5)';
$('pause').title = 'Pause at the next statement (F6)';
$('stepOver').title = 'Step over: run this line, calls and all (F10). With nothing running, start at the first line.';
$('stepInto').title = 'Step into: the next line, inside any call (F11). With nothing running, start at the first line.';
$('stepOut').title = 'Step out: run to the end of this SUB, FUNCTION or GOSUB (Shift+F11)';
showDebugControls();

const THEMES = { midnight: 'Midnight', daylight: 'Daylight', qbasic: 'QBasic blue', commodore: 'Commodore 64', green: 'Green screen', amber: 'Amber' };
const themes = $('theme');
for (const [id, name] of Object.entries(THEMES)) themes.add(new Option(name, id));
if (!(document.documentElement.dataset.theme in THEMES)) document.documentElement.dataset.theme = 'midnight';
themes.value = document.documentElement.dataset.theme;
themes.addEventListener('change', () => {
  document.documentElement.dataset.theme = themes.value;
  store.set('arcade-basic.theme', themes.value);
});

const actions = {
  new: () => documents.newProgram(),
  open: () => documents.openFiles(),
  save: () => documents.save(),
  saveAs: () => documents.save(documents.active, true),
  share: shareLink,
  close: () => documents.close(),
  shortcuts: () => $('shortcutsDialog').showModal(),
  about: () => $('aboutDialog').showModal(),
};
if (!('CompressionStream' in window)) document.querySelector('[data-action="share"]').disabled = true;

// The File and Help menus.
const menus = [...document.querySelectorAll('.menu')].map(menu => {
  const button = menu.querySelector('.tool'), list = menu.querySelector('.menu-list');
  const items = () => [...list.querySelectorAll('button:not(:disabled)')];
  const close = () => { list.classList.remove('open'); button.setAttribute('aria-expanded', 'false'); };
  button.addEventListener('click', () => {
    const open = !list.classList.contains('open');
    menus.forEach(m => m.close());
    if (!open) return;
    list.classList.add('open');
    button.setAttribute('aria-expanded', 'true');
    items()[0]?.focus();
  });
  list.addEventListener('click', e => {
    const item = e.target.closest('button');
    if (!item) return;
    close();
    if (item.dataset.href) window.open(item.dataset.href, '_blank', 'noopener');
    else actions[item.dataset.action]?.();
  });
  list.addEventListener('keydown', e => {
    const all = items(), i = all.indexOf(document.activeElement);
    if (e.key === 'ArrowDown') all[(i + 1) % all.length]?.focus();
    else if (e.key === 'ArrowUp') all[(i - 1 + all.length) % all.length]?.focus();
    else if (e.key === 'Escape') { close(); button.focus(); }
    else return;
    e.preventDefault();
  });
  return { menu, close };
});
document.addEventListener('pointerdown', e => menus.forEach(m => { if (!m.menu.contains(e.target)) m.close(); }));

$('run').addEventListener('click', runOrContinue);
$('stop').addEventListener('click', () => stop());
$('pause').addEventListener('click', pauseRunning);
$('stepOver').addEventListener('click', () => debugStep(OVER));
$('stepInto').addEventListener('click', () => debugStep(INTO));
$('stepOut').addEventListener('click', () => debugStep(OUT));

// The keyboard shortcuts, wherever the focus is.
window.addEventListener('keydown', e => {
  const mod = isMac ? e.metaKey : e.ctrlKey;
  const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
  let action = null;
  if (key === 'F5' && !mod) action = e.shiftKey ? () => stop() : () => runOrContinue(e);
  else if (mod && key === 'Enter') action = () => runOrContinue(e);
  else if (key === 'F6' && !mod) action = pauseRunning;
  else if (key === 'F10' && !mod) action = () => debugStep(OVER);
  else if (key === 'F11' && !mod) action = () => debugStep(e.shiftKey ? OUT : INTO);
  else if (mod && !e.altKey && key === 's') action = e.shiftKey ? actions.saveAs : actions.save;
  else if (mod && !e.altKey && !e.shiftKey && key === 'o') action = actions.open;
  else if (e.altKey && !mod && e.code === 'KeyN') action = actions.new;
  else if (e.altKey && !mod && e.code === 'KeyW') action = actions.close;
  if (!action || document.querySelector('dialog[open]')) return;
  e.preventDefault();
  e.stopPropagation();
  action();
}, true);

// Files dropped anywhere on the page open in tabs (not into the editor's text).
let dragDepth = 0;
const carriesFiles = e => e.dataTransfer?.types.includes('Files');
window.addEventListener('dragenter', e => {
  if (!carriesFiles(e)) return;
  dragDepth++;
  document.body.classList.add('dropping');
});
window.addEventListener('dragleave', e => {
  if (carriesFiles(e) && --dragDepth <= 0) { dragDepth = 0; document.body.classList.remove('dropping'); }
});
window.addEventListener('dragover', e => { if (carriesFiles(e)) e.preventDefault(); }, true);
window.addEventListener('drop', e => {
  if (!carriesFiles(e)) return;
  e.preventDefault();
  e.stopPropagation();
  dragDepth = 0;
  document.body.classList.remove('dropping');
  documents.openDropped(e.dataTransfer);
}, true);

// The divider between the editor and the screen.
const mainArea = document.querySelector('main'), divider = $('divider');
const setSplit = f => {
  f = Math.min(0.8, Math.max(0.2, f));
  mainArea.style.gridTemplateColumns = `minmax(280px, ${f}fr) 6px minmax(300px, ${1 - f}fr)`;
  return f;
};
let split = setSplit(Number(store.get('arcade-basic.split')) || 0.5);
divider.addEventListener('pointerdown', e => {
  divider.setPointerCapture(e.pointerId);
  divider.classList.add('dragging');
  const move = ev => {
    const r = mainArea.getBoundingClientRect();
    split = setSplit((ev.clientX - r.left) / r.width);
  };
  divider.addEventListener('pointermove', move);
  divider.addEventListener('lostpointercapture', () => {
    divider.classList.remove('dragging');
    divider.removeEventListener('pointermove', move);
    store.set('arcade-basic.split', String(split));
  }, { once: true });
});
divider.addEventListener('keydown', e => {
  const step = e.key === 'ArrowLeft' ? -0.02 : e.key === 'ArrowRight' ? 0.02 : 0;
  if (!step) return;
  split = setSplit(split + step);
  store.set('arcade-basic.split', String(split));
  e.preventDefault();
});

// -- Links to programs ---------------------------------------------------------------------
// …/#breakout opens an example; …/#code=… carries a whole program, compressed.

async function shareLink() {
  if (!documents.active) return;
  const link = `${location.origin}${location.pathname}#code=${await pack(editor.state.doc.toString())}`;
  try {
    await navigator.clipboard.writeText(link);
    notify('Link copied: whoever opens it gets this program');
  } catch {
    await ask({ title: 'A link to this program', message: 'Copy it from here:', input: link, buttons: [{ label: 'Close', value: 'ok', kind: 'primary' }] });
  }
}

async function pack(text) {
  const stream = new Blob([text]).stream().pipeThrough(new CompressionStream('deflate-raw'));
  const bytes = new Uint8Array(await new Response(stream).arrayBuffer());
  let binary = '';
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

async function unpack(code) {
  const binary = atob(code.replace(/-/g, '+').replace(/_/g, '/'));
  const stream = new Blob([Uint8Array.from(binary, c => c.charCodeAt(0))]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
  return new Response(stream).text();
}

/** Open what an address names; false if it names nothing. */
async function openAddress(hash) {
  if (hash.startsWith('#code=')) {
    try {
      documents.openShared(await unpack(hash.slice(6)));
      return true;
    } catch {
      notify('The program in that link couldn\'t be read');
      return false;
    }
  }
  const name = decodeURIComponent(hash.slice(1)).toLowerCase();
  const example = name && P.Examples().find(n => n.toLowerCase() === name || n.toLowerCase() === name + '.bas');
  if (!example) return false;
  documents.openExample(example);
  return true;
}

// -- Starting up -------------------------------------------------------------------------------

window.arcadeBasic = { editor, documents, audio, run: runActive };   // (window.arcadeBasic.run(true): start paused)   // for the browser console (and screenshots)

const startHash = location.hash;
const restored = documents.restore();    // what was open last time, before .NET is even here

P = await loadingNet;
readyMs = performance.now();

const words = P.Words(), gap = words.indexOf('');
documents.refresh(editor.setWords(words.slice(0, gap), words.slice(gap + 1)));
editor.setChecker(text => {
  const spans = P.Check(text), messages = P.CheckMessages();
  editor.setStatementLines(P.StatementLines());
  return messages.map((message, i) => ({
    from: spans[3 * i], to: spans[3 * i + 1], severity: ['info', 'warning', 'error'][spans[3 * i + 2]], message,
  }));
});

// The examples, by the @category on their first line.
const examples = $('examples');
examples.add(new Option('Examples…', ''));
const groups = new Map([['Games', []], ['Graphics', []], ['Basics', []]]);
for (const name of P.Examples()) {
  const group = /@category\s+(\w+)/.exec(P.ExampleSource(name).split('\n', 1)[0])?.[1] ?? 'Basics';
  if (!groups.has(group)) groups.set(group, []);
  groups.get(group).push(name);
}
for (const [group, names] of groups) {
  if (!names.length) continue;
  const optgroup = document.createElement('optgroup');
  optgroup.label = group;
  for (const name of names) optgroup.append(new Option(name.replace(/\.bas$/, ''), name));
  examples.append(optgroup);
}
examples.addEventListener('change', () => {
  if (examples.value) documents.openExample(examples.value);
  examples.value = '';
  editor.focus();
});

if (!(await openAddress(startHash)) && !restored) documents.openExample('maze3d.bas');
window.addEventListener('hashchange', () => openAddress(location.hash));
$('run').disabled = false;
$('loading').textContent = '';
showDebugControls();
showVariables(false);

// -- ?bench=1 and the self-tests -----------------------------------------------------------------
const params = new URLSearchParams(location.search);
if (params.has('bench')) bench();
if (params.get('selftest') === 'kanban') kanbanSelftest();
if (params.get('selftest') === 'ide') import('./selftest.js').then(m => m.ideSelftest({ P, editor, documents, ask, start, stop, pack, openAddress, $, audio }));
// ?selftest=page: report which program the address opened and what's in the tabs.
if (params.get('selftest') === 'page')
  fetch('/results', { method: 'POST', body: JSON.stringify({ example: documents.active?.name, hash: location.hash,
    firstLine: editor.state.doc.line(1).text, examples: P.Examples().length, tabs: documents.all.map(d => d.name) }) });

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
    documents.openExample('kanban.bas');
    start(editor.state.doc.toString());
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
