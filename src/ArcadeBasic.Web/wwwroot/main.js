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
import { createAudio } from './audio.js';
import { createScreen, connectInput } from './screen.js';
import { stepFrame, newRun, NEEDS_INPUT, NEEDS_POINT, PAUSED, HALTED } from './runner.js';
import { openPictureEditor, insertPicture, picturesIn } from './pictures.js';
import { setDefaultPalette, openPalette } from './colours.js';
import { setTuneEngine, openTuneEditor, insertTune } from './tunes.js';
import { createOutline, openGoTo } from './outline.js';
import { createFiles } from './files.js';
import { createExport } from './export.js';

// .NET starts loading straight away; the editor works meanwhile.
const loadingNet = (async () => {
  const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  await runMain();
  return exports.Playground;
})();
let P = null;       // the .NET side, once loaded
let readyMs = 0;

// DebugStep values (src/ArcadeBasic.Vm/Debugging.cs)
const PAUSE = 0, INTO = 1, OVER = 2, OUT = 3;


const $ = id => document.getElementById(id);
const root = document.documentElement;
const canvas = $('screen');
const screen = createScreen(canvas, { glow: () => $('glow').checked });
const output = $('output'), stats = $('stats');
const inputRow = $('inputRow'), inputLine = $('inputLine');
const isMac = /Mac|iPhone|iPad/.test(navigator.platform);
const store = {
  get(key) { try { return localStorage.getItem(key); } catch { return null; } },
  set(key, value) { try { localStorage.setItem(key, value); } catch { /* no storage */ } },
};

const audio = createAudio();
const programFiles = createFiles({ store, ask });   // the files programs write, kept in the browser
let run = null;   // the running program's loop state
let screenOf = null;   // the tab whose program the screen shows (for Export's icon)

// -- The editor and its tabs ------------------------------------------------------

const editor = createEditor($('editor'), {
  onUpdate(update) {
    documents.update(update);
    if (update.selectionSet || update.docChanged) showCursor();
    if (update.docChanged) outlineSoon();
  },
  onProblems: showProblems,
  onEditPicture(view, line) {
    openPictureEditor(view, line, { key: documents.active?.name ?? '', ask });
  },
  onPickColour(view, { index, onPick }) {
    openPalette({ current: programColours, index, onPick, insert: insertLine });
  },
  onEditTune(view, where) {
    openTuneEditor(view, where, { ask });
  },
  onBreakpoints(lines) {
    if (run?.docs?.includes(documents.active)) syncBreakpoints();       // takes effect at once
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
  showOutline();
  showCursor();
  // The address names an example (…/#breakout), so a link can open it.
  const hash = doc.kind === 'example' ? '#' + doc.name.replace(/\.bas$/, '') : '';
  if (location.hash !== hash) history.replaceState(null, '', location.pathname + location.search + hash);
}

function showCursor() {
  const state = editor.state, head = state.selection.main.head, line = state.doc.lineAt(head);
  $('cursorPos').textContent = `Ln ${line.number}, Col ${head - line.from + 1}`;
  outline.follow(line.number);
}

// The outline beside the editor (outline.js): the program's sections, SUBs, FUNCTIONs …
const outline = createOutline($('outline'), { onGo: line => editor.goToLine(line) });
let outlineTimer = 0;
const outlineSoon = () => { clearTimeout(outlineTimer); outlineTimer = setTimeout(showOutline, 250); };
function showOutline() {
  if ($('outline').hidden) return;
  outline.show(editor.state.doc);
  outline.follow(editor.state.doc.lineAt(editor.state.selection.main.head).number);
}
function toggleOutline(show = $('outline').hidden) {
  $('outline').hidden = !show;
  $('outlineToggle').setAttribute('aria-pressed', String(show));
  store.set('arcade-basic.outline', show ? '1' : '0');
  showOutline();
}
$('outlineToggle').addEventListener('click', () => toggleOutline());
$('foldAll').addEventListener('click', () => editor.foldAll());
$('unfoldAll').addEventListener('click', () => editor.unfoldAll());
const goTo = () => openGoTo($('goToDialog'), editor.state.doc, line => editor.goToLine(line));

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
  listProblems(found);
  const badge = $('consoleBadge');
  badge.className = errors ? 'errors' : warnings ? 'warnings' : '';
  badge.textContent = errors ? `✖ ${errors}` : warnings ? `⚠ ${warnings}` : '';
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
$('problems').addEventListener('click', () => showView('console'));

// -- The views: Code (editing and debugging), Output (what the program shows), Console ----
// Running shows Output; a pause or an error shows Code, at its line. The tabs (or Alt+1, 2, 3)
// show any of them.

const viewTabs = [...document.querySelectorAll('#views [role="tab"]')];
function showView(name) {
  root.dataset.view = name;
  for (const tab of viewTabs) tab.setAttribute('aria-selected', String(tab.dataset.view === name));
  if (name === 'output' && run && !run.waiting) (root.classList.contains('drawn') ? canvas : $('runPane')).focus();
  if (name === 'code') editor.focus();
}
for (const tab of viewTabs) tab.addEventListener('click', () => showView(tab.dataset.view));

/** Show a program's line in the Code view. */
function goToCode(doc, line) {
  showView('code');
  if (doc && documents.all.includes(doc) && documents.active !== doc) documents.show(doc);
  if (line) editor.goToLine(line);
}

const element = (tag, cls, text) => Object.assign(document.createElement(tag), { className: cls, textContent: text });

/** The Console's list of the program's problems (the ones the editor marks). */
function listProblems(found) {
  const doc = editor.state.doc, shown = documents.active;
  const items = found.map(d => {
    const line = doc.lineAt(Math.min(d.from, doc.length)).number;
    const li = document.createElement('li');
    li.append(element('span', `sev ${d.severity}`, d.severity === 'error' ? '✖' : d.severity === 'warning' ? '⚠' : 'ℹ'),
              element('span', 'where', `line ${line}`), element('span', 'text', d.message));
    li.addEventListener('click', () => goToCode(shown, line));
    return li;
  });
  $('problemList').replaceChildren(...(items.length ? items : [element('li', 'none', `No problems in ${shown?.name ?? 'the program'}`)]));
}

/** A message in the Console: what ran, how it ended, its errors. `go`, if given, is where
 *  clicking it goes. */
function log(text, kind = 'info', go = null) {
  const list = $('log');
  list.querySelector('.none')?.remove();
  const li = document.createElement('li');
  li.append(element('time', '', new Date().toLocaleTimeString([], { hour12: false })),
            element('span', `text ${kind}`, text));
  if (go) { li.classList.add('goes'); li.title = 'Show it in the code'; li.addEventListener('click', go); }
  list.append(li);
  while (list.children.length > 200) list.firstElementChild.remove();
  li.scrollIntoView({ block: 'nearest' });
}
$('clearLog').addEventListener('click', () => $('log').replaceChildren(element('li', 'none', 'Nothing yet')));
$('log').append(element('li', 'none', 'Nothing yet'));

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

// -- Programs in several files ---------------------------------------------------------------
// LIBRARY "file.bas" brings in another file's MODULEs and procedures: the other open
// tabs are the files it can name (and the bundled examples, for theirs).

const textOf = doc => (doc === documents.active ? editor.state : doc.state).doc.toString();

/** The files a program's LIBRARY statements name. */
const libraryNames = text => [...text.matchAll(/^\s*(?:\d+\s+)?LIBRARY\s+(.+)$/gim)]
  .flatMap(m => [...m[1].matchAll(/"([^"]+)"/g)].map(q => q[1]));

/** The other open programs, as LIBRARY can name them: [names, texts]. */
function openPrograms(except) {
  const docs = documents.all.filter(d => d !== except);
  return [docs.map(d => d.name), docs.map(textOf)];
}

/** What F5 runs from a tab: the tab — unless another open program names it in LIBRARY. */
const programFor = doc =>
  documents.all.find(d => d !== doc && libraryNames(textOf(d)).some(n => n.toLowerCase() === doc.name.toLowerCase())) ?? doc;

/** The tab with one of the running program's files (by its number), opening a bundled example's. */
function docOfFile(file) {
  if (!run?.files) return run?.doc ?? null;
  let doc = run.docs[file];
  if (!doc && P.Examples().includes(run.files[file])) doc = run.docs[file] = documents.openExample(run.files[file], false);
  return doc ?? null;
}

/** Every breakpoint in the running program's files. */
function syncBreakpoints() {
  if (!run?.docs) return;
  const files = [], lines = [];
  run.docs.forEach((doc, file) => {
    if (doc && documents.all.includes(doc)) for (const line of editor.breakpointLines(doc === documents.active ? editor.state : doc.state)) { files.push(file); lines.push(line); }
  });
  P.SetBreakpoints(files, lines);
}

// -- Running a program -------------------------------------------------------------------

/** Run the program showing (or the one it's a library of); with stopAtFirst, paused before
 *  its first statement. */
function runActive(stopAtFirst = false) {
  if (!P || !documents.active) return;
  const doc = programFor(documents.active);
  audio.unlock();                        // (a click or key press: the browser now allows sound)
  start(textOf(doc), null, doc, stopAtFirst);
}

/** Run `text`: a tab's program (`doc`), or with no tab, `program` names it and its
 *  libraries ({ name, libraries: [names, texts] }, a play link's). */
function start(text, onHalt, doc = null, stopAtFirst = false, program = null) {
  stop();
  output.replaceChildren();
  root.classList.remove('drawn', 'printed');
  screenOf = doc;
  editor.clearRuntimeError();
  const name = doc?.name ?? program?.name ?? 'program.bas';
  const diagnostics = P.Load(name, text, ...(doc ? openPrograms(doc) : program?.libraries ?? [[], []]));
  if (diagnostics) {
    if (player) { appendOutput(diagnostics, 'error'); return false; }
    const go = () => { goToCode(doc); editor.goToFirstProblem(); };
    log(`${name} can't run:\n${diagnostics}`, 'error', doc ? go : null);
    if (doc) go();
    return false;
  }
  if (!player) log(`▶ ${stopAtFirst ? 'Debugging' : 'Running'} ${name}`);
  audio.reset();
  screen.newRun();
  const files = P.SourceFiles();
  const docs = files.map((name, i) => i === 0 ? doc : documents.all.find(d => d.name.toLowerCase() === name.toLowerCase()) ?? null);
  if (stopAtFirst) P.PauseAt(PAUSE);
  lastVariables = new Map();
  run = { ...newRun(), waiting: false, waitingPoint: false, paused: false, onHalt, doc, files, docs };
  syncBreakpoints();
  documents.setRunning(doc);
  $('stop').disabled = false;
  root.classList.add('running');
  showDebugControls();
  if (!stopAtFirst) {                    // (stopping at the first line stays in the code)
    showView('output');
    $('runPane').focus();                // (its keys go to the program; the screen takes them once it shows)
  }
  requestAnimationFrame(tick);
  return true;
}

/** Stop the program; its sound stops too unless it ended by itself (background music plays on). */
function stop(silence = true) {
  if (silence) audio.stop();
  if (run) { programFiles.sync(true); P.Stop(); documents.setRunning(null); }
  if (run?.paused) leavePause();
  run = null;
  $('stop').disabled = true;
  root.classList.remove('running');
  inputRow.style.display = 'none';
  canvas.style.cursor = '';
  showDebugControls();
}

/** Stop, from the Stop button or Shift+F5. */
function stopByHand() {
  if (!run) return;
  stop();
  log('■ Stopped');
}

function tick() {
  const r = run;
  if (!r) return;
  r.ticks++;
  let halted = false;
  if (!r.waiting && !r.waitingPoint && !r.paused) {
    const s = stepFrame(P, r, audio);
    if (s === NEEDS_INPUT) { r.waiting = true; askForInput(); }
    else if (s === NEEDS_POINT) { r.waitingPoint = true; canvas.style.cursor = 'crosshair'; }
    else if (s === PAUSED) enterPause(r);
    else if (s === HALTED) halted = true;
  }
  if (!r.paused && variablesShowing() && performance.now() - lastVariablesShown > 500) showVariables(false);   // live
  audio.play(P.TakeTones());                              // background music (MB)
  appendOutput(P.TakeOutput());
  programFiles.sync();                                    // keep what it wrote
  const frame = P.TakeFrame();
  if (frame.length) {
    const drew = screen.draw(frame, P.FrameText(), P.FrameCells());
    r.frames++;
    if (drew && !root.classList.contains('drawn')) {
      root.classList.add('drawn');        // (the screen shows from the program's first frame: give it the keys)
      if (player || document.activeElement === $('runPane')) canvas.focus();
    }
  }
  if (!r.paused) showStats(r, halted);
  if (halted) {
    const error = P.Error();
    const line = error ? P.CurrentLine() : 0;
    const failed = error ? docOfFile(P.CurrentFile()) : null;
    r.instructions = P.Instructions();
    stop(false);
    if (player) {
      if (error) appendOutput('\n' + error + '\n', 'error');
    } else if (error) {
      const show = () => { goToCode(failed); editor.showRuntimeError(line); };
      log(error, 'error', failed && documents.all.includes(failed) ? show : null);
      notify(error);
      if (failed && documents.all.includes(failed)) show();   // point at the line that failed
    } else {
      log(`■ Ended · ${stats.textContent}`);
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
  const line = P.CurrentLine(), file = P.CurrentFile(), doc = docOfFile(file);
  if (doc && documents.all.includes(doc) && documents.active !== doc) documents.show(doc);
  if (doc && documents.active === doc) editor.showPaused(line);
  showView('code');
  showVariables(true);
  showVariablesPanel(true);
  editor.setValueLookup(valueOf);
  const where = file > 0 ? ` in ${r.files[file]}` : '';
  stats.textContent = `Paused at line ${line}${where} · F5 continue · F10 step over · F11 step into · Shift+F11 step out`;
  showDebugControls();
}

function leavePause() {
  editor.showPaused(0);
  for (const doc of documents.all) if (doc !== documents.active) doc.state = editor.withoutPause(doc.state);
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
  if (step == null) showView('output');   // (a step stays in the code)
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
/** The Variables panel beside the editor, on or off. */
function showVariablesPanel(show) {
  $('variables').hidden = !show;
  $('variablesToggle').setAttribute('aria-pressed', String(show));
  if (show && run && !run.paused) showVariables(false);
}
const variablesShowing = () => !$('variables').hidden;
$('variablesToggle').addEventListener('click', () => showVariablesPanel(!variablesShowing()));

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
    const title = frame.name || (frame.file > 0 ? 'Module start-up' : 'Main program');
    section.open = !closedFrames.has(title);
    section.addEventListener('toggle', () => section.open ? closedFrames.delete(title) : closedFrames.add(title));
    const summary = document.createElement('summary');
    const at = document.createElement('a');
    at.href = '#';
    at.textContent = `line ${frame.line}`;
    if (frame.file > 0) at.textContent += ` in ${run.files[frame.file]}`;
    at.addEventListener('click', e => {
      e.preventDefault();
      const doc = docOfFile(frame.file);
      if (!doc) return;
      if (documents.active !== doc) documents.show(doc);
      editor.goToLine(frame.line);
    });
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
  if (!run?.paused || !run.docs?.includes(documents.active)) return null;
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


/** The running program's colours ('#rrggbb' × 256) once it has drawn, else null. */
const programColours = () => run ? screen.colours() : null;

/** A line of code above the cursor's (the palette's "Insert SET COLOR MIX"). */
function insertLine(text) {
  const view = editor.view, line = view.state.doc.lineAt(view.state.selection.main.head);
  const indent = /^\s*/.exec(line.text)[0];
  view.dispatch({ changes: { from: line.from, insert: indent + text + '\n' }, selection: { anchor: line.from + indent.length + text.length } });
  view.focus();
}


function appendOutput(text, cls = '') {
  if (!text) return;
  root.classList.add('printed');
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
  showView('output');                    // (where the line it's reading is)
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

// Keys go to the program only from the Output view (so typing in the editor doesn't), and
// not while it's reading a line; the page's own shortcuts (F5, Ctrl+S, …) are taken first.
connectInput(screen, canvas, {
  keys: $('runPane'), program: () => P, audio, wanted: e => e.target !== inputLine,
  onPress: () => { if (run?.waitingPoint) { run.waitingPoint = false; canvas.style.cursor = ''; } },
});

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


const glowBox = $('glow');
glowBox.checked = store.get('arcade-basic.glow') === '1';
glowBox.addEventListener('change', () => {
  store.set('arcade-basic.glow', glowBox.checked ? '1' : '0');
  screen.redraw();
});

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
  openFolder: () => documents.openFolder(),
  save: () => documents.save(),
  saveAs: () => documents.save(documents.active, true),
  share: () => shareLink(false),
  sharePlay: () => shareLink(true),
  picture: () => { if (documents.active) insertPicture(editor.view, { key: documents.active.name, ask }); },
  tune: () => { if (documents.active && P) insertTune(editor.view, { ask }); },
  palette: () => openPalette({ current: programColours, insert: insertLine }),
  close: () => documents.close(),
  programFiles: () => programFiles.open(),
  export: () => { if (P && documents.active) exporter.open(); },
  shortcuts: () => $('shortcutsDialog').showModal(),
  about: () => $('aboutDialog').showModal(),
};
if (!('CompressionStream' in window))
  for (const item of document.querySelectorAll('[data-action^="share"]')) item.disabled = true;

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
$('stop').addEventListener('click', stopByHand);
$('pause').addEventListener('click', pauseRunning);
$('stepOver').addEventListener('click', () => debugStep(OVER));
$('stepInto').addEventListener('click', () => debugStep(INTO));
$('stepOut').addEventListener('click', () => debugStep(OUT));

// The keyboard shortcuts, wherever the focus is.
window.addEventListener('keydown', e => {
  const mod = isMac ? e.metaKey : e.ctrlKey;
  const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
  if (player) {                         // (no editor: F5 plays the link's program again, Shift+F5 stops it)
    if (key !== 'F5' || mod || !root.classList.contains('started')) return;
    e.preventDefault();
    if (e.shiftKey) stop(); else playShared();
    return;
  }
  let action = null;
  if (key === 'F5' && !mod) action = e.shiftKey ? stopByHand : () => runOrContinue(e);
  else if (mod && key === 'Enter') action = () => runOrContinue(e);
  else if (key === 'F6' && !mod) action = pauseRunning;
  else if (key === 'F10' && !mod) action = () => debugStep(OVER);
  else if (key === 'F11' && !mod) action = () => debugStep(e.shiftKey ? OUT : INTO);
  else if (mod && !e.altKey && key === 's') action = e.shiftKey ? actions.saveAs : actions.save;
  else if (mod && !e.altKey && !e.shiftKey && key === 'o') action = actions.open;
  else if (mod && e.shiftKey && !e.altKey && key === 'o') action = goTo;
  else if (e.altKey && !mod && e.code === 'KeyN') action = actions.new;
  else if (e.altKey && !mod && e.code === 'KeyW') action = actions.close;
  else if (e.altKey && !mod && /^Digit[123]$/.test(e.code)) action = () => showView(['code', 'output', 'console'][e.code.slice(5) - 1]);
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

// -- Export ---------------------------------------------------------------------------------
// File → Export…: the program showing (or the one it's a library of), compiled, packed with
// the player (export.js).

const exporter = createExport({
  dialog: $('exportDialog'),
  programName: () => programFor(documents.active).name,
  compile() {
    const doc = programFor(documents.active);
    const problems = P.Export(doc.name, textOf(doc), ...openPrograms(doc));
    return problems ? { doc, name: doc.name, problems } : { doc, name: doc.name, bytecode: new Uint8Array(P.ExportedBytecode()) };
  },
  pictures() {
    const doc = programFor(documents.active);
    const libraries = libraryNames(textOf(doc)).map(n => documents.all.find(d => d.name.toLowerCase() === n.toLowerCase()));
    return [doc, ...libraries.filter(Boolean)].flatMap(d => picturesIn(textOf(d), d.name).map(p => ({ ...p, file: d.name })));
  },
  screen: () => root.classList.contains('drawn') && screenOf === programFor(documents.active) ? screen.picture(1280, 768) : null,
  onProblems({ doc, name, problems }) {
    const go = () => { goToCode(doc); editor.goToFirstProblem(); };
    log(`${name} can't be exported:\n${problems}`, 'error', go);
    go();
  },
  onExported: (file, bytes) => log(`⇩ Exported ${file} (${(bytes / 1048576).toFixed(1)} MB)`),
});

// -- Links to programs ---------------------------------------------------------------------
// …/#breakout opens an example; …/#play/breakout plays it, with no editor; …/#code=…
// carries a whole program, compressed; …/#play=… carries one to play.

/** Copy a link to the program showing (or the one it's a library of): to open in the
 *  editor, or with `play`, to play. */
async function shareLink(play) {
  if (!documents.active) return;
  // A program with libraries (open, not bundled) goes with them: {"main": [name, text], "libraries": [[name, text] …]}.
  // A play link always says the program's name (it's the page's title).
  const doc = programFor(documents.active), text = textOf(doc);
  const libraries = libraryNames(text).map(n => documents.all.find(d => d.name.toLowerCase() === n.toLowerCase()))
    .filter(d => d && !P.Examples().includes(d.name)).map(d => [d.name, textOf(d)]);
  const payload = libraries.length || play ? JSON.stringify({ main: [doc.name, text], libraries }) : text;
  const link = `${location.origin}${location.pathname}#${play ? 'play' : 'code'}=${await pack(payload)}`;
  try {
    await navigator.clipboard.writeText(link);
    notify(play ? 'Link copied: whoever opens it can play this program' : 'Link copied: whoever opens it gets this program');
  } catch {
    await ask({ title: play ? 'A link to play this program' : 'A link to this program', message: 'Copy it from here:', input: link,
                buttons: [{ label: 'Close', value: 'ok', kind: 'primary' }] });
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
  if (hash.startsWith('#play=')) return openPlayer(hash.slice(6));
  if (hash.startsWith('#play/')) {
    const example = exampleNamed(hash.slice(6));
    if (!example) return false;
    const text = P.ExampleSource(example);
    const libraries = libraryNames(text).filter(n => P.Examples().includes(n)).map(n => [n, P.ExampleSource(n)]);
    return openPlayer(await pack(JSON.stringify({ main: [example, text], libraries })));
  }
  if (player) leavePlayer();
  if (hash.startsWith('#code=')) {
    try {
      const shared = await unpack(hash.slice(6));
      const project = shared.startsWith('{"main"') ? JSON.parse(shared) : null;
      if (!project) { documents.openShared(shared); return true; }
      for (const [name, text] of project.libraries) documents.openShared(text, name, false);
      documents.openShared(project.main[1], project.main[0]);
      return true;
    } catch {
      notify('The program in that link couldn\'t be read');
      return false;
    }
  }
  const example = exampleNamed(hash.slice(1));
  if (!example) return false;
  openExampleProgram(example);
  return true;
}

/** The example a link names ("breakout" or "breakout.bas", any case), or undefined. */
function exampleNamed(text) {
  const name = decodeURIComponent(text).toLowerCase();
  return name ? P.Examples().find(n => n.toLowerCase() === name || n.toLowerCase() === name + '.bas') : undefined;
}

// -- Play links ------------------------------------------------------------------------------
// A #play= link shows the program's screen and a bar, no editor; the program isn't opened
// in a tab, so the editor's own programs are just as they were. Play is a click, which is
// also what lets the page make sound.

let player = null;   // { name, text, libraries: [names, texts], code } while showing a play link

async function openPlayer(code) {
  let shared;
  try {
    shared = await unpack(code);
  } catch {
    notify('The program in that link couldn\'t be read');
    return false;
  }
  const project = shared.startsWith('{"main"') ? JSON.parse(shared) : { main: ['program.bas', shared], libraries: [] };
  stop();
  player = { name: project.main[0], text: project.main[1], code,
             libraries: [project.libraries.map(l => l[0]), project.libraries.map(l => l[1])] };
  output.replaceChildren();
  root.classList.add('player');
  root.classList.remove('started', 'drawn');
  const title = player.name.replace(/\.bas$/i, '');
  $('playerName').textContent = title;
  $('playerStatus').textContent = '';
  document.title = `${title} — Arcade BASIC`;
  $('playButton').textContent = `▶ Play ${title}`;
  $('playButton').disabled = false;
  return true;
}

function playShared() {
  if (!player || !P) return;
  audio.unlock();                        // (a click: the browser now allows sound)
  root.classList.add('started');
  $('playerStatus').textContent = '';
  $('playerRestart').disabled = false;
  start(player.text, () => { $('playerStatus').textContent = 'Finished'; }, null, false, player);
}

/** Back to the editor, showing the link's program in a tab — to read it, or change it. */
function leavePlayer(openIt = false) {
  if (!player) return;
  const { code } = player;
  stop();
  player = null;
  root.classList.remove('player', 'started', 'drawn');
  document.title = 'Arcade BASIC';
  showView('code');
  if (openIt) {
    history.replaceState(null, '', location.pathname + location.search + '#code=' + code);
    openAddress('#code=' + code);
  }
}

$('playButton').addEventListener('click', playShared);
$('playerRestart').addEventListener('click', playShared);
$('playerEdit').addEventListener('click', () => leavePlayer(true));
$('playerFullscreen').addEventListener('click', () => {
  if (document.fullscreenElement) document.exitFullscreen();
  else $('runPane').requestFullscreen?.().then(() => canvas.focus(), () => {});
});
if (!document.fullscreenEnabled) $('playerFullscreen').hidden = true;
const playerSound = $('playerSound');
playerSound.checked = soundBox.checked;
playerSound.addEventListener('change', () => {
  soundBox.checked = playerSound.checked;
  soundBox.dispatchEvent(new Event('change'));
});

/** An example, and the bundled libraries it names in tabs beside it. */
function openExampleProgram(name) {
  for (const library of libraryNames(P.ExampleSource(name)))
    if (P.Examples().includes(library)) documents.openExample(library, false);
  return documents.openExample(name);
}

// -- Starting up -------------------------------------------------------------------------------

window.arcadeBasic = { editor, documents, audio, run: runActive };   // (window.arcadeBasic.run(true): start paused)   // for the browser console (and screenshots)

if (store.get('arcade-basic.outline') === '1') toggleOutline(true);

const startHash = location.hash;
const restored = documents.restore();    // what was open last time, before .NET is even here

P = await loadingNet;
readyMs = performance.now();
programFiles.attach(P);

const words = P.Words(), gap = words.indexOf('');
documents.refresh(editor.setWords(words.slice(0, gap), words.slice(gap + 1)));
editor.setChecker(text => {
  const doc = documents.active;
  const spans = P.Check(doc?.name ?? 'program.bas', text, ...openPrograms(doc)), messages = P.CheckMessages();
  editor.setStatementLines(P.StatementLines());
  return messages.map((message, i) => ({
    from: spans[3 * i], to: spans[3 * i + 1], severity: ['info', 'warning', 'error'][spans[3 * i + 2]], message,
  }));
});

setDefaultPalette(P.DefaultPalette());
setTuneEngine({
  tones: mml => ({ tones: P.PlayTones(mml), error: P.PlayError() }),
  preview: tones => audio.preview(tones),
  stop: () => audio.stopPreview(),
});
editor.view.dispatch({});                               // (draw the colour swatches now there's a palette)

// The examples, by the @category on their first line.
const examples = $('examples');
examples.add(new Option('Examples…', ''));
const groups = new Map([['Games', []], ['Graphics', []], ['Basics', []]]);
for (const name of P.Examples()) {
  const first = P.ExampleSource(name).split('\n', 1)[0];
  if (/@library/i.test(first)) continue;                  // (opened with the program that uses it)
  const group = /@category\s+(\w+)/.exec(first)?.[1] ?? 'Basics';
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
  if (examples.value) openExampleProgram(examples.value);
  examples.value = '';
  editor.focus();
});

if (!(await openAddress(startHash)) && !restored) openExampleProgram('maze3d.bas');
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

// ?selftest=kanban: drag card #4 onto Done with real DOM pointer events, check the board
// saved itself, type Q, report.
async function kanbanSelftest() {
  const pause = () => new Promise(r => setTimeout(r, 300));
  const at = t => t && [t.x + 0.02, t.y + 0.01];
  const pointer = (type, [x, y]) => {
    const r = canvas.getBoundingClientRect(), { inset, w, h } = screen.geometry();
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
  const card = prefix => screen.texts.filter(t => t.text.startsWith(prefix));
  const result = {};
  try {
    programFiles.forget('kanban.board');            // the demo board, whatever an earlier run left
    documents.openExample('kanban.bas');
    start(editor.state.doc.toString());
    await pause();
    result.mouseMode = screen.texts.some(t => t.text === '+ add a card');
    if (params.has('snapshot')) {                  // hover over card #6 first
      pointer('pointermove', at(card('The board saves')[0]));
      await pause();
      result.hoverSnapshot = canvas.toDataURL('image/png');
    }
    const done = [screen.texts.find(t => t.text === 'DONE').x + 0.02, 0.5];
    pointer('pointerdown', at(card('Select a card')[0])); await pause();
    pointer('pointermove', done); await pause();
    result.copiesWhileDragging = card('Select a card').length;
    if (params.has('snapshot')) result.snapshot = canvas.toDataURL('image/png');   // mid-drag picture
    pointer('pointerup', done); await pause();
    result.status = screen.texts.find(t => t.text.startsWith('Moved #'))?.text;
    const heads = ['TO DO', 'DOING', 'DONE'].map(name => screen.texts.find(t => t.text === name));
    result.lanes = heads.map(h => h.text + ' ' +
      screen.texts.find(t => t.y === h.y && t.x > h.x && /^\d+$/.test(t.text))?.text);
    programFiles.sync(true);
    result.saved = programFiles.text('kanban.board')?.split('\n').find(l => l.includes('Select a card'));
    key('q', 'KeyQ'); await pause();
    result.typed = screen.texts.find(t => t.text.startsWith('q'))?.text;
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
    P.Load('bench.bas', text, [], []);
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
