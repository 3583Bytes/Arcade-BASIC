// The player: one compiled program (game.bin, beside this page), filling the window —
// no IDE. It runs programs the way the IDE does, with the same .NET side
// (Playground.Run.cs) and the same screen, sound and stepping (screen.js, audio.js,
// runner.js). In the desktop player (desktop/player) the page can ask to close the
// window (`quit`); in a browser it just says the program has ended. When `arcade-basic
// run` opens a program in the desktop player (?folder), its files are the ones in the
// folder it was run from, and what it prints and how it ended go to the terminal too.

import { dotnet } from './_framework/dotnet.js';
import { createScreen, connectInput } from './screen.js';
import { stepFrame, newRun, NEEDS_INPUT, NEEDS_POINT, HALTED } from './runner.js';
import { createAudio } from './audio.js';
import { createFiles } from './files.js';

const $ = id => document.getElementById(id);
const root = document.documentElement;
const canvas = $('screen'), output = $('output'), inputRow = $('inputRow'), inputLine = $('inputLine');
const store = {
  get(key) { try { return localStorage.getItem(key); } catch { return null; } },
  set(key, value) { try { localStorage.setItem(key, value); } catch { /* no storage */ } },
};
const query = new URLSearchParams(location.search);
const report = query.get('report');   // (tests: where to post what happened)
const fromTerminal = query.has('folder');

const audio = createAudio();
audio.muted = !!report;     // (tests run silent)
const screen = createScreen(canvas);
let P = null;       // the .NET side, once loaded
let run = null;     // the program's loop state while it runs
let files = null;   // the files it writes, kept between runs

/** A line at the bottom of the window; '' for none. */
function say(text) {
  const span = document.createElement('span');
  span.textContent = text;
  $('status').replaceChildren(...(text ? [span] : []));
}

function appendOutput(text, cls = '') {
  if (!text) return;
  root.classList.add('printed');
  const last = output.lastChild;
  if (!cls && last?.nodeType === Node.TEXT_NODE) last.appendData(text);
  else if (!cls) output.append(text);
  else output.append(Object.assign(document.createElement('span'), { className: cls, textContent: text }));
  output.scrollTop = output.scrollHeight;
}

// Keys go to the program wherever the focus is — except while it's reading a line.
connectInput(screen, canvas, {
  keys: window, program: () => run && P, audio,
  wanted: e => e.target !== inputLine,
  onPress: () => { if (run?.waitingPoint) { run.waitingPoint = false; canvas.style.cursor = ''; } },
});

inputRow.addEventListener('submit', e => {
  e.preventDefault();
  if (!run) return;
  appendOutput(inputLine.value + '\n');     // a terminal echoes what you type
  P.ProvideInput(inputLine.value);
  inputRow.style.display = 'none';
  run.waiting = false;
  canvas.focus();
});

/** A run's files, from the folder it was run from and saved back to it as the program
 *  writes them, as files.js keeps them in the browser. */
function folderFiles() {
  let P = null, lastSync = 0;
  return {
    async attach(playground) {
      P = playground;
      const saved = await fetch('folder').then(r => r.json());
      const names = Object.keys(saved);
      P.PutFiles(names, names.map(n => saved[n]));
    },
    sync(now = false) {
      if (!P || (!now && performance.now() - lastSync < 500)) return;
      lastSync = performance.now();
      const changed = P.ChangedFiles();
      for (let i = 0; i < changed.length; i += 2) {
        const name = changed[i].split('/').map(encodeURIComponent).join('/');
        toTerminal(`folder/${name}`, changed[i + 1]);
      }
    },
  };
}

/** Post `text` to the desktop player (`print`, `ended`, a file), after what went before. */
let posted = Promise.resolve();
function toTerminal(path, text) {
  posted = posted.then(() => fetch(path, { method: 'POST', body: text })).catch(() => {});
  return posted;
}

function tick() {
  const r = run;
  r.ticks++;
  let halted = false;
  if (!r.waiting && !r.waitingPoint) {
    const s = stepFrame(P, r, audio);
    if (s === NEEDS_INPUT) {
      r.waiting = true;
      inputRow.style.display = 'flex';
      inputLine.value = '';
      inputLine.focus();
    } else if (s === NEEDS_POINT) {
      r.waitingPoint = true;
      canvas.style.cursor = 'crosshair';
    } else if (s === HALTED) {
      halted = true;
    }
  }
  audio.play(P.TakeTones());                              // background music (MB)
  const printed = P.TakeOutput();
  appendOutput(printed);
  if (fromTerminal && printed) toTerminal('print', printed);
  files.sync();
  const frame = P.TakeFrame();
  if (frame.length && screen.draw(frame, P.FrameText(), P.FrameCells())) {
    r.frames++;
    root.classList.add('drawn');
  }
  if (halted) return finish(P.Error());
  requestAnimationFrame(tick);
}

/** The program has ended: say so, and close the window on the next key or click. */
async function finish(error) {
  if (error) appendOutput('\n' + error + '\n', 'error');
  files.sync(true);
  if (fromTerminal) await toTerminal('ended', error ?? '');
  run = null;
  inputRow.style.display = 'none';
  sendReport({ ended: true, error });
  say('The program has ended — press a key to close');
  const close = async () => {
    const closed = await fetch('quit').then(r => r.ok, () => false);
    if (!closed) say('The program has ended');
  };
  setTimeout(() => {                        // (not the key that was still down as it ended)
    window.addEventListener('keydown', close, { once: true });
    window.addEventListener('pointerdown', close, { once: true });
  }, 500);
  if (report) setTimeout(close, 1500);      // (tests: as if a key were pressed)
}

function sendReport(extra = {}) {
  if (!report) return;
  const state = { title: document.title, running: !!run, frames: run?.frames, drawn: root.classList.contains('drawn'),
                  visible: document.visibilityState, ticks: run?.ticks,
                  texts: screen.texts.map(t => t.text), output: output.textContent.slice(-500), audio: audio.state, ...extra };
  fetch(report, { method: 'POST', body: JSON.stringify(state, null, 2) }).catch(() => {});
}

try {
  const { getAssemblyExports, getConfig, runMain, setModuleImports } = await dotnet.create();
  setModuleImports('screen', { measureText: (text, font, height) => screen.measureText(text, font, height) });   // (TEXTWIDTH)
  P = (await getAssemblyExports(getConfig().mainAssemblyName)).Playground;
  await runMain();
  const response = await fetch('game.bin');
  if (!response.ok) throw new Error('There is no program here to play (game.bin)');
  const problem = P.Open(Array.from(new Uint8Array(await response.arrayBuffer())));
  if (problem) throw new Error(problem);
} catch (e) {
  say(e.message);
  sendReport({ error: e.message });
  throw e;
}

// The app's name, from the desktop player (?title=) or an exported page's title (export.js);
// else the program's file's.
const title = query.get('title') || (document.title !== 'Arcade BASIC' ? document.title : P.Title()) || 'Arcade BASIC';
document.title = title;
files = fromTerminal ? folderFiles() : createFiles({ store, key: `arcade-basic.files.${title}` });
await files.attach(P);
say('');
audio.reset();
audio.unlock();             // (a desktop window may allow sound straight away; a browser waits for a key or click)
run = { ...newRun(), waiting: false, waitingPoint: false };
canvas.focus();
requestAnimationFrame(tick);
if (report) setTimeout(() => sendReport(), 3000);
