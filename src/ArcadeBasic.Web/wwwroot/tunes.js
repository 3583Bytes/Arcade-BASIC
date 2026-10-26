// Tunes in the code. Beside each PLAY "…" (a whole literal string) there's a ▶ to hear
// it and a ♫ to open it in the tune editor: its notes on a piano roll, a piano to add
// more (click it, or play it from the keyboard), note lengths, rests, tempo, and the
// string itself, all kept in step. What you hear is the engine's own reading of the
// string (Playground.PlayTones), so it's what PLAY plays.
import { ViewPlugin, Decoration, WidgetType } from './lib/codemirror.js';
import { commentStart } from './basic-text.js';

const PLAY = /\bPLAY\s+"([^"]*)"(?!\s*&)/gi;
const NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
const LENGTHS = [1, 2, 4, 8, 16, 32];
// The computer keyboard as a piano: A is C, W is C#, … K is the next C.
const KEYS = { a: 0, w: 1, s: 2, e: 3, d: 4, f: 5, t: 6, g: 7, y: 8, h: 9, u: 10, j: 11, k: 12 };

let engine = null;
/** How tunes become sound: tones(mml) → { tones, error }; preview(tones) → Promise<{ start, end }>; stop(). */
export function setTuneEngine(e) { engine = e; }

// -- The ▶ and ♫ beside each PLAY string ----------------------------------------------------

class TuneButtons extends WidgetType {
  constructor(mml, where, open) {
    super();
    this.mml = mml;
    this.where = where;
    this.open = open;
  }
  eq(other) { return other.mml === this.mml && other.where.line === this.where.line && other.where.index === this.where.index; }
  toDOM(view) {
    const box = document.createElement('span');
    box.className = 'cm-tune';
    const play = document.createElement('button');
    play.type = 'button';
    play.textContent = '▶';
    play.title = 'Hear this tune';
    play.addEventListener('mousedown', e => {
      e.preventDefault();
      if (!engine) return;
      const { tones } = engine.tones(this.mml);
      if (tones.length) engine.preview(tones);
    });
    const edit = document.createElement('button');
    edit.type = 'button';
    edit.textContent = '♫';
    edit.title = 'Edit this tune';
    edit.addEventListener('mousedown', e => { e.preventDefault(); this.open(view, this.where); });
    box.append(play, edit);
    return box;
  }
  ignoreEvent() { return true; }
}

/** Where a PLAY string is: its line and its match on the line, found again when needed. */
function locate(doc, { line, index }) {
  if (line > doc.lines) return null;
  const l = doc.line(line);
  const m = [...l.text.matchAll(PLAY)].find(x => x.index === index);
  if (!m) return null;
  const from = l.from + m.index + m[0].indexOf('"') + 1;
  return { from, to: from + m[1].length, mml: m[1] };
}

/** The editor extension: open(view, where) edits the tune. */
export function tuneButtons(open) {
  const build = view => {
    const widgets = [];
    const doc = view.state.doc;
    for (const { from, to } of view.visibleRanges) {
      for (let n = doc.lineAt(from).number; n <= doc.lineAt(to).number; n++) {
        const line = doc.line(n);
        const code = line.text.slice(0, commentStart(line.text));   // (not in a comment)
        for (const m of code.matchAll(PLAY)) {
          widgets.push(Decoration.widget({ widget: new TuneButtons(m[1], { line: n, index: m.index }, open), side: 1 })
            .range(line.from + m.index + m[0].length));
        }
      }
    }
    return Decoration.set(widgets, true);
  };
  return ViewPlugin.fromClass(class {
    constructor(view) { this.decorations = build(view); }
    update(u) { if (u.docChanged || u.viewportChanged) this.decorations = build(u.view); }
  }, { decorations: v => v.decorations });
}

// -- Reading MML: where each note is, and the octave and length in force ------------------

/** The commands of an MML string, with where each is; a note or rest makes one tone. */
export function scan(mml) {
  const tokens = [];
  let i = 0;
  const number = () => {
    const m = /^\d+/.exec(mml.slice(i));
    if (!m) return null;
    i += m[0].length;
    return +m[0];
  };
  const dots = () => { while (mml[i] === '.') i++; };
  while (i < mml.length) {
    const c = mml[i].toUpperCase(), from = i;
    if (c === ' ' || c === '\t') { i++; continue; }
    i++;
    if (c === 'O' || c === 'T' || c === 'L') {
      const n = number();
      if (n == null) break;
      tokens.push({ kind: { O: 'octave', T: 'tempo', L: 'length' }[c], from, to: i, value: n });
    } else if (c === '>' || c === '<') tokens.push({ kind: c === '>' ? 'up' : 'down', from, to: i });
    else if (c === 'N') { if (number() == null) break; tokens.push({ kind: 'note', from, to: i }); }
    else if (c === 'P') { if (number() == null) break; dots(); tokens.push({ kind: 'rest', from, to: i }); }
    else if (c === 'M') { i++; tokens.push({ kind: 'mode', from, to: i }); }
    else if ('ABCDEFG'.includes(c)) {
      if ('#+-'.includes(mml[i])) i++;
      number();
      dots();
      tokens.push({ kind: 'note', from, to: i });
    } else break;                                   // (the engine says what's wrong)
  }
  return tokens;
}

/** The octave, default length and tempo in force at a position in the string. */
function stateAt(mml, pos) {
  const state = { octave: 4, length: 4, tempo: 120 };
  for (const t of scan(mml)) {
    if (t.to > pos) break;
    if (t.kind === 'octave') state.octave = t.value;
    else if (t.kind === 'up') state.octave = Math.min(6, state.octave + 1);
    else if (t.kind === 'down') state.octave = Math.max(0, state.octave - 1);
    else if (t.kind === 'length') state.length = t.value;
    else if (t.kind === 'tempo') state.tempo = t.value;
  }
  return state;
}

// -- The tune editor ------------------------------------------------------------------------

const $ = id => document.getElementById(id);

/** Edit the PLAY string at `where` ({ line, index }) in the view's document. */
export function openTuneEditor(view, where, { ask } = {}) {
  const found = locate(view.state.doc, where);
  if (!found || !engine) return;
  const dialog = $('tuneDialog'), text = $('tuneText'), roll = $('tuneRoll'), ctx = roll.getContext('2d');
  let length = 4, dotted = false, pianoOctave = 4;
  let tones = [], notes = [], total = 0, playing = null, selected = -1;

  $('tuneTitle').textContent = `Tune · line ${where.line}`;
  text.value = found.mml;

  // -- the piano roll
  function read() {
    const result = engine.tones(text.value);
    tones = result.tones;
    $('tuneError').textContent = result.error;
    notes = [];
    const tokens = scan(text.value).filter(t => t.kind === 'note' || t.kind === 'rest');
    let t = 0;
    for (let i = 0, k = 0; i + 2 < tones.length; i += 3, k++) {
      const [f, on, off] = [tones[i], tones[i + 1], tones[i + 2]];
      if (f > 0) notes.push({ midi: Math.round(69 + 12 * Math.log2(f / 440)), start: t, length: on, token: tokens[k] });
      t += on + off;
    }
    total = t;
    const tempo = scan(text.value).find(x => x.kind === 'tempo');
    $('tuneTempo').value = tempo ? tempo.value : 120;
    $('tuneLength').textContent = total ? `${total.toFixed(2)} s, ${notes.length} notes` : '';
    draw();
  }

  function geometry() {
    const dpr = window.devicePixelRatio || 1;
    const width = roll.clientWidth || 720, height = 190;
    if (roll.width !== Math.round(width * dpr)) { roll.width = Math.round(width * dpr); roll.height = Math.round(height * dpr); }
    roll.style.height = `${height}px`;
    const midis = notes.map(n => n.midi);
    let low = Math.min(...midis, 60) - 2, high = Math.max(...midis, 72) + 2;
    if (high - low < 14) high = low + 14;
    return { dpr, width, height, low, high, left: 36, rowH: height / (high - low + 1) };
  }

  function draw(at = null) {
    const g = geometry(), css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    ctx.setTransform(g.dpr, 0, 0, g.dpr, 0, 0);
    ctx.fillStyle = css('--panel');
    ctx.fillRect(0, 0, g.width, g.height);
    ctx.font = '10px ui-monospace, monospace';
    ctx.textBaseline = 'middle';
    for (let m = g.low; m <= g.high; m++) {
      const y = (g.high - m) * g.rowH;
      if (NAMES[m % 12].includes('#')) { ctx.fillStyle = 'rgba(0, 0, 0, .18)'; ctx.fillRect(g.left, y, g.width - g.left, g.rowH); }
      if (m % 12 === 0) {
        ctx.fillStyle = css('--muted');
        ctx.fillText(`C${Math.floor(m / 12) - 1}`, 4, y + g.rowH / 2);
        ctx.fillStyle = 'rgba(128, 128, 128, .25)';
        ctx.fillRect(g.left, y + g.rowH - 1, g.width - g.left, 1);
      }
    }
    const x = s => g.left + (total ? s / total : 0) * (g.width - g.left - 6);
    notes.forEach((n, i) => {
      const lit = i === selected || (at != null && at >= n.start && at < n.start + n.length);
      ctx.fillStyle = lit ? css('--paused') : css('--accent');
      ctx.fillRect(x(n.start), (g.high - n.midi) * g.rowH + 1, Math.max(2, x(n.start + n.length) - x(n.start) - 1), Math.max(2, g.rowH - 2));
    });
    if (at != null) {
      ctx.fillStyle = css('--text');
      ctx.fillRect(x(at), 0, 1.5, g.height);
    }
  }

  roll.onpointerdown = e => {
    const g = geometry(), r = roll.getBoundingClientRect();
    const px = e.clientX - r.left, py = e.clientY - r.top;
    const x = s => g.left + (total ? s / total : 0) * (g.width - g.left - 6);
    const hit = notes.findIndex(n => px >= x(n.start) && px <= x(n.start + n.length) && Math.abs(py - ((g.high - n.midi) + 0.5) * g.rowH) <= g.rowH);
    selected = hit;
    draw();
    if (hit >= 0 && notes[hit].token) {
      text.focus();
      text.setSelectionRange(notes[hit].token.from, notes[hit].token.to);
      hear(text.value.slice(0, notes[hit].token.from), text.value.slice(notes[hit].token.from, notes[hit].token.to));
    }
  };

  /** Play one note as it sounds at that point in the string. */
  function hear(before, note) {
    const st = stateAt(before, before.length);
    const { tones: one } = engine.tones(`T${st.tempo} L${st.length} O${st.octave} ${note}`);
    if (one.length) engine.preview(one);
  }

  // -- playing it all, with a playhead
  async function play() {
    stop();
    if (!tones.length) return;
    const token = {};
    playing = token;
    const { start, end } = await engine.preview(tones);
    const frame = () => {
      if (playing !== token) return;
      const now = performance.now();
      if (now >= end) { playing = null; draw(); return; }
      draw(Math.max(0, (now - start) / 1000));
      requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
  }
  function stop() {
    playing = null;
    engine.stop();
    draw();
  }

  // -- writing notes in
  function insert(piece) {
    const pos = text.selectionStart, end = text.selectionEnd;
    const before = text.value.slice(0, pos);
    const sep = before && !/\s$/.test(before) ? ' ' : '';
    text.setRangeText(sep + piece, pos, end, 'end');
    read();
  }
  function addNote(semitones) {                     // semitones above C in pianoOctave
    const octave = Math.min(6, pianoOctave + Math.floor(semitones / 12));
    const name = NAMES[semitones % 12];
    const pos = text.selectionStart, st = stateAt(text.value, pos);
    let piece = octave !== st.octave ? `O${octave}` : '';
    piece += name + (length !== st.length ? length : '') + (dotted ? '.' : '');
    // A new octave would carry on to the notes after this one: put theirs back.
    if (octave !== st.octave && scan(text.value).some(t => t.from >= text.selectionEnd)) piece += ` O${st.octave}`;
    hear(`O${octave}`, name + length + (dotted ? '.' : ''));
    insert(piece);
  }
  function addRest() {
    insert(`P${length}${dotted ? '.' : ''}`);
  }

  // -- the controls
  const lengths = $('tuneLengths');
  lengths.replaceChildren(...LENGTHS.map(n => {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = n === 4 ? '♩ 4' : n === 8 ? '♪ 8' : `${n}`;
    button.title = { 1: 'Whole notes', 2: 'Half notes', 4: 'Quarter notes', 8: 'Eighth notes' }[n] ?? `1/${n} notes`;
    button.dataset.length = n;
    button.addEventListener('click', () => { length = n; showControls(); });
    return button;
  }));
  function showControls() {
    for (const b of lengths.children) b.setAttribute('aria-pressed', String(+b.dataset.length === length));
    $('tuneDotted').checked = dotted;
    $('tuneOctave').textContent = `${pianoOctave}–${pianoOctave + 1}`;
    piano();
  }
  function piano() {
    const keys = $('tunePiano');
    const whites = [0, 2, 4, 5, 7, 9, 11, 12, 14, 16, 17, 19, 21, 23, 24];
    const blacks = [1, 3, 6, 8, 10, 13, 15, 18, 20, 22];
    const width = 100 / whites.length;
    const key = (s, black) => {
      const k = document.createElement('button');
      k.type = 'button';
      k.className = black ? 'black' : 'white';
      const octave = pianoOctave + Math.floor(s / 12);
      k.title = `${NAMES[s % 12]}${octave}${!black && s % 12 === 0 ? '' : ''}`;
      if (!black && s % 12 === 0) k.textContent = `C${octave}`;
      const left = black ? (whites.filter(w => w < s).length * width - width * 0.3) : whites.indexOf(s) * width;
      Object.assign(k.style, { left: `${left}%`, width: `${black ? width * 0.6 : width}%` });
      k.addEventListener('click', () => addNote(s));
      return k;
    };
    keys.replaceChildren(...whites.map(s => key(s, false)), ...blacks.map(s => key(s, true)));
  }
  $('tuneDotted').onchange = e => { dotted = e.target.checked; };
  $('tuneRest').onclick = addRest;
  $('tuneOctDown').onclick = () => { pianoOctave = Math.max(0, pianoOctave - 1); showControls(); };
  $('tuneOctUp').onclick = () => { pianoOctave = Math.min(5, pianoOctave + 1); showControls(); };
  $('tunePlay').onclick = play;
  $('tuneStop').onclick = stop;
  $('tuneTempo').onchange = e => {
    const tempo = Math.max(32, Math.min(255, Math.round(+e.target.value) || 120));
    const first = scan(text.value).find(t => t.kind === 'tempo');
    if (first) text.setRangeText(`T${tempo}`, first.from, first.to, 'preserve');
    else text.value = `T${tempo} ` + text.value;
    read();
  };
  text.oninput = () => { selected = -1; read(); };
  dialog.onkeydown = e => {
    if (e.target === text || e.target.tagName === 'INPUT' || e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.key in KEYS) { addNote(KEYS[e.key]); e.preventDefault(); }
    else if (e.key === ' ') { (playing ? stop : play)(); e.preventDefault(); }
    else if (e.key === 'z') { $('tuneOctDown').click(); e.preventDefault(); }
    else if (e.key === 'x') { $('tuneOctUp').click(); e.preventDefault(); }
  };

  // -- leaving
  const changed = () => text.value !== found.mml;
  const leave = async () => {
    if (changed() && ask && await ask({
      title: 'Leave without applying?', message: 'Your changes to the tune will be lost.',
      buttons: [{ label: 'Keep editing' }, { label: 'Leave', value: 'discard', kind: 'danger' }],
    }) !== 'discard') return;
    dialog.close();
  };
  $('tuneApply').onclick = e => {
    e.preventDefault();
    const now = locate(view.state.doc, where);
    if (now && changed()) view.dispatch({ changes: { from: now.from, to: now.to, insert: text.value.replace(/["\n]/g, '') } });
    dialog.close();
  };
  $('tuneCancel').onclick = e => { e.preventDefault(); leave(); };
  dialog.oncancel = e => { e.preventDefault(); leave(); };
  dialog.onclose = () => { stop(); view.focus(); };

  showControls();
  dialog.showModal();
  read();
  text.focus();
  text.setSelectionRange(text.value.length, text.value.length);
}

/** A new PLAY line above the cursor's, opened in the tune editor. */
export function insertTune(view, options) {
  const line = view.state.doc.lineAt(view.state.selection.main.head);
  view.dispatch({ changes: { from: line.from, insert: 'PLAY "T120 O4 L4"\n' }, selection: { anchor: line.from } });
  openTuneEditor(view, { line: line.number, index: 0 }, options);
}
