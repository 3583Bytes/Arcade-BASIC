// Pictures in the code. A block of DATA lines that are each one quoted string, all
// the same length, is a picture: a character per pixel — the way maze3d keeps its
// map, its wall textures and its sprites. The editor puts an "Edit picture" label
// above each (pictureLens), and openPictureEditor edits one in a pixel editor that
// writes the strings back, as one change you can undo.
//
// A character's colour: when a comment names the picture (`! stone`) and the same
// comment names a DATA line of red, green, blue triples from 0 to 1 elsewhere
// (maze3d's texture colours, for SET COLOR MIX), the digits 0, 1, 2 … are those
// colours, and changing one rewrites that line too. Otherwise the editor picks
// colours — digits a dark-to-light ramp, letters their own hues, "." and " "
// see-through — and remembers any you change, in this browser.
import { EditorState, StateField, EditorView, Decoration, WidgetType } from './lib/codemirror.js';

const PICTURE_LINE = /^(\s*\d+\s+)?\s*DATA\s+"([^"]*)"\s*(!.*)?$/i;
const NUMBER_LINE = /^(\s*\d+\s+)?\s*DATA\s+(.*?)\s*(!.*)?$/i;
const COMMENT = /^\s*(\d+\s+)?(!|REM\b)\s*(.*?)\s*$/i;
const MIN_ROWS = 3, MIN_WIDTH = 3;
const SEE_THROUGH = new Set(['.', ' ']);
// Digits with no colours of their own: dark to light, each easy to tell apart.
const RAMP = ['#14142b', '#2c3e8f', '#2779b5', '#1f9e8a', '#6cc04a', '#d9cf5b', '#f2a541', '#e4572e', '#c02c63', '#f4f1de'];
const COLOUR_STORE = 'arcade-basic.picture-colours';

/** A comment's name: what comes before any explanation ("key (. is see-through)" → "key",
 *  "---- the map: 16 x 16 tiles" → "the map"). */
const nameOf = comment => comment.replace(/^[-=~#*\s]+/, '').replace(/\s*[(:;,—–-].*$/, '').trim().slice(0, 40);

/** The picture's name: the comment just above it, else the nearest section heading a
 *  few lines up (! ---- the map ----), as long as no other picture is in between. */
function nameAbove(doc, firstLine) {
  const above = firstLine > 1 ? COMMENT.exec(doc.line(firstLine - 1).text) : null;
  if (above) return nameOf(above[3]);
  for (let n = firstLine - 2; n >= 1 && n >= firstLine - 12; n--) {
    const text = doc.line(n).text;
    if (PICTURE_LINE.test(text)) return '';
    const comment = COMMENT.exec(text);
    if (comment && /^[-=]{2,}/.test(comment[3])) return nameOf(comment[3]);
  }
  return '';
}

/** The pictures in a document: name (its comment, if any), size, rows, where each row's string is. */
export function findPictures(doc) {
  const pictures = [];
  let run = [];
  const flush = () => {
    if (run.length >= MIN_ROWS && run[0].text.length >= MIN_WIDTH) {
      pictures.push({
        name: nameAbove(doc, run[0].number),
        width: run[0].text.length, height: run.length, firstLine: run[0].number,
        rows: run.map(r => r.text), spans: run.map(r => ({ from: r.from, to: r.from + r.text.length })),
      });
    }
    run = [];
  };
  for (let n = 1; n <= doc.lines; n++) {
    const line = doc.line(n);
    const m = PICTURE_LINE.exec(line.text);
    if (!m) { flush(); continue; }
    if (run.length && run[0].text.length !== m[2].length) flush();
    run.push({ number: n, text: m[2], from: line.from + line.text.indexOf('"') + 1 });
  }
  flush();
  return pictures;
}

/** The colours named like the picture: a DATA line of red, green, blue triples (0-1)
 *  under a comment with the picture's name. */
export function findColours(doc, picture) {
  if (!picture.name) return null;
  for (let n = 1; n < doc.lines; n++) {
    if (n === picture.firstLine - 1) continue;
    const comment = COMMENT.exec(doc.line(n).text);
    if (!comment || nameOf(comment[3]).toLowerCase() !== picture.name.toLowerCase()) continue;
    const line = doc.line(n + 1);
    const m = NUMBER_LINE.exec(line.text);
    if (!m || m[2].includes('"')) continue;
    const spans = [];
    let at = line.text.indexOf(m[2]);
    for (const part of m[2].split(',')) {
      const value = part.trim();
      const offset = part.indexOf(value);
      spans.push({ from: line.from + at + offset, to: line.from + at + offset + value.length, value: Number(value) });
      at += part.length + 1;
    }
    if (spans.length < 3 || spans.length % 3 || spans.some(s => !(s.value >= 0 && s.value <= 1) || !/^[\d.]+$/.test(doc.sliceString(s.from, s.to))))
      continue;
    const colours = [];
    for (let i = 0; i < spans.length; i += 3) colours.push([spans[i].value, spans[i + 1].value, spans[i + 2].value]);
    return { line: n + 1, colours, spans };
  }
  return null;
}

const hex = ([r, g, b]) => '#' + [r, g, b].map(v => Math.round(Math.min(1, Math.max(0, v)) * 255).toString(16).padStart(2, '0')).join('');
const unhex = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16) / 255);

function defaultColour(ch) {
  if (SEE_THROUGH.has(ch)) return null;
  if (/\d/.test(ch)) return RAMP[+ch];
  const code = ch.toUpperCase().charCodeAt(0);
  const hue = Math.round((code * 137.508) % 360), light = ch === ch.toLowerCase() && ch !== ch.toUpperCase() ? 70 : 55;
  return `hsl(${hue} 65% ${light}%)`;
}

/** A character's colour (CSS, or null for see-through): from `colours`, the linked DATA's,
 *  for a digit it has; else from `custom`, those picked in this browser; else the default. */
const colourFrom = (colours, custom, ch) =>
  colours && /\d/.test(ch) && +ch < colours.length ? hex(colours[+ch]) : custom[ch] ?? defaultColour(ch);

/** Where the colours picked for a picture are kept: by the program's name (key), and the
 *  picture's name and size. */
const storeKeyOf = (key, picture) => `${key}|${picture.name || 'line ' + picture.firstLine}|${picture.width}x${picture.height}`;
const storedColours = storeKey => { try { return JSON.parse(localStorage.getItem(COLOUR_STORE))?.[storeKey] ?? {}; } catch { return {}; } };

/** The pictures in `text`, a program's (`key` its name, as for openPictureEditor): name,
 *  size, line, and draw() — the picture on a canvas of its own, a pixel a character, in the
 *  colours the pixel editor shows. (File → Export makes icons of them.) */
export function picturesIn(text, key = '') {
  const doc = EditorState.create({ doc: text }).doc;
  return findPictures(doc).map(picture => ({
    name: picture.name, width: picture.width, height: picture.height, line: picture.firstLine,
    draw() {
      const colours = findColours(doc, picture)?.colours, custom = storedColours(storeKeyOf(key, picture));
      const canvas = document.createElement('canvas');
      canvas.width = picture.width;
      canvas.height = picture.height;
      const ctx = canvas.getContext('2d');
      picture.rows.forEach((row, y) => [...row].forEach((ch, x) => {
        const colour = colourFrom(colours, custom, ch);
        if (colour) { ctx.fillStyle = colour; ctx.fillRect(x, y, 1, 1); }
      }));
      return canvas;
    },
  }));
}

// -- The label above each picture -------------------------------------------------------

class Lens extends WidgetType {
  constructor(picture, open) {
    super();
    this.picture = picture;
    this.open = open;
  }
  eq(other) {
    return other.picture.name === this.picture.name && other.picture.width === this.picture.width &&
      other.picture.height === this.picture.height && other.picture.firstLine === this.picture.firstLine;
  }
  toDOM(view) {
    const lens = document.createElement('div');
    lens.className = 'cm-pictureLens';
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = '▦ Edit picture';
    button.addEventListener('mousedown', e => {
      e.preventDefault();
      this.open(view, this.picture.firstLine);
    });
    const about = document.createElement('span');
    about.textContent = `${this.picture.name ? this.picture.name + ' · ' : ''}${this.picture.width} × ${this.picture.height}`;
    lens.append(button, about);
    return lens;
  }
  ignoreEvent() { return true; }
}

/** The editor extension: an "Edit picture" label above each picture; open(view, firstLine) edits it. */
export function pictureLens(open) {
  const build = state => Decoration.set(findPictures(state.doc).map(p =>
    Decoration.widget({ widget: new Lens(p, open), block: true, side: -1 }).range(state.doc.line(p.firstLine).from)));
  return StateField.define({
    create: build,
    update: (lenses, tr) => tr.docChanged ? build(tr.state) : lenses,
    provide: field => EditorView.decorations.from(field),
  });
}

// -- The pixel editor ---------------------------------------------------------------------

const $ = id => document.getElementById(id);

/** A new picture above the cursor's line: ask its name and size, write it as see-through
 *  DATA under a comment with its name, and open it. */
export async function insertPicture(view, { key = '', ask }) {
  const answer = await ask({
    title: 'A new picture', message: 'Its name and size, across by down (3 to 64): "ship 16 x 12", say.',
    input: 'sprite 16 x 16', buttons: [{ label: 'Cancel' }, { label: 'Insert', value: 'insert', kind: 'primary' }],
  });
  const m = /^\s*(.*?)\s*(\d+)\s*[x×*]\s*(\d+)\s*$/i.exec(answer ?? '');
  if (!m) return;
  const [w, h] = [+m[2], +m[3]].map(n => Math.max(MIN_WIDTH, Math.min(64, n)));
  const name = m[1].replace(/["\n]/g, '') || 'picture';
  const line = view.state.doc.lineAt(view.state.selection.main.head);
  const text = `! ${name}\n` + Array.from({ length: h }, () => `DATA "${'.'.repeat(w)}"`).join('\n') + '\n';
  view.dispatch({ changes: { from: line.from, insert: text }, selection: { anchor: line.from } });
  openPictureEditor(view, line.number + 1, { key, ask, extra: ['0', '1', '2', '3'] });
}

/** Edit the picture whose first row is on firstLine of the view's document. key names it
 *  for remembering colours you pick (the program's name, say); extra are characters to
 *  offer that it doesn't use yet. */
export function openPictureEditor(view, firstLine, { key = '', ask, extra = [] } = {}) {
  const doc = view.state.doc;
  const picture = findPictures(doc).find(p => p.firstLine === firstLine);
  if (!picture) return;
  const linked = findColours(doc, picture);
  const storeKey = storeKeyOf(key, picture);
  const stored = storedColours(storeKey);

  const { width: W, height: H } = picture;
  let grid = picture.rows.map(r => [...r]);
  let colours = linked ? linked.colours.map(c => [...c]) : null;   // the linked DATA's, being edited
  const custom = { ...stored };                                       // others' preview colours
  const history = [], future = [];
  let ink = null, tool = 'pen', mirror = false, hover = null;

  const colourOf = ch => colourFrom(colours, custom, ch);
  const isLinked = ch => colours && /\d/.test(ch) && +ch < colours.length;
  const order = ch => SEE_THROUGH.has(ch) ? -1 : /\d/.test(ch) ? +ch : 100 + ch.charCodeAt(0);
  let alphabet = [...new Set([...grid.flat(), ...extra, ...(colours ? colours.map((_, i) => String(i)) : [])])].sort((a, b) => order(a) - order(b));
  ink = alphabet.find(ch => !SEE_THROUGH.has(ch)) ?? alphabet[0];

  // -- the dialog's parts
  const dialog = $('pictureDialog'), canvas = $('pictureCanvas'), ctx = canvas.getContext('2d');
  $('pictureTitle').textContent = `${picture.name || 'Picture'} · ${W} × ${H}`;
  $('pictureSource').textContent = linked
    ? `Its colours are the DATA on line ${linked.line} (red, green, blue for 0, 1, 2 …); changing them changes that line.`
    : 'Its colours are only for drawing here: the program decides what each character means.';

  const dpr = window.devicePixelRatio || 1;
  const zoom = Math.max(4, Math.min(36, Math.floor(Math.min(520 / W, 440 / H))));
  canvas.width = W * zoom * dpr;
  canvas.height = H * zoom * dpr;
  canvas.style.width = `${W * zoom}px`;
  canvas.style.height = `${H * zoom}px`;

  function checker(c, x, y, size) {
    const half = size / 2;
    c.fillStyle = '#9a9a9a'; c.fillRect(x, y, size, size);
    c.fillStyle = '#d0d0d0'; c.fillRect(x, y, half, half); c.fillRect(x + half, y + half, half, half);
  }

  function paintCells(c, scale, ox = 0, oy = 0) {
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const colour = colourOf(grid[y][x]);
      if (colour) { c.fillStyle = colour; c.fillRect(ox + x * scale, oy + y * scale, scale, scale); }
      else checker(c, ox + x * scale, oy + y * scale, scale);
    }
  }

  function draw() {
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    paintCells(ctx, zoom);
    if (zoom >= 8) {
      ctx.strokeStyle = 'rgba(128, 128, 128, .28)';
      ctx.lineWidth = 1 / dpr;
      ctx.beginPath();
      for (let x = 1; x < W; x++) { ctx.moveTo(x * zoom, 0); ctx.lineTo(x * zoom, H * zoom); }
      for (let y = 1; y < H; y++) { ctx.moveTo(0, y * zoom); ctx.lineTo(W * zoom, y * zoom); }
      ctx.stroke();
    }
    if (mirror) {
      ctx.strokeStyle = 'rgba(255, 80, 200, .7)';
      ctx.lineWidth = 2 / dpr;
      ctx.beginPath(); ctx.moveTo(W * zoom / 2, 0); ctx.lineTo(W * zoom / 2, H * zoom); ctx.stroke();
    }
    if (hover) {
      ctx.strokeStyle = '#fff';
      ctx.lineWidth = 2 / dpr;
      ctx.strokeRect(hover.x * zoom + 1, hover.y * zoom + 1, zoom - 2, zoom - 2);
    }
    // Previews: twice actual size, and tiled 3 × 3 (does a texture repeat cleanly?).
    for (const [id, tiles] of [['pictureActual', 1], ['pictureTiled', 3]]) {
      const preview = $(id), scale = 2;
      preview.width = W * scale * tiles * dpr;
      preview.height = H * scale * tiles * dpr;
      preview.style.width = `${W * scale * tiles}px`;
      preview.style.height = `${H * scale * tiles}px`;
      const p = preview.getContext('2d');
      p.setTransform(dpr, 0, 0, dpr, 0, 0);
      for (let ty = 0; ty < tiles; ty++) for (let tx = 0; tx < tiles; tx++) paintCells(p, scale, tx * W * scale, ty * H * scale);
    }
    $('pictureUndo').disabled = !history.length;
    $('pictureRedo').disabled = !future.length;
    for (const [name, button] of [['pen', 'picturePen'], ['fill', 'pictureFill'], ['pick', 'picturePick']])
      $(button).setAttribute('aria-pressed', String(tool === name));
    $('pictureMirror').setAttribute('aria-pressed', String(mirror));
  }

  function showPalette() {
    const palette = $('pictureColours');
    palette.replaceChildren(...alphabet.map(ch => {
      const swatch = document.createElement('button');
      swatch.type = 'button';
      swatch.className = 'swatch' + (ch === ink ? ' selected' : '');
      swatch.title = SEE_THROUGH.has(ch) ? `"${ch}": see-through` : `"${ch}"`;
      const chip = document.createElement('span');
      chip.className = 'chip';
      const colour = colourOf(ch);
      if (colour) chip.style.background = colour; else chip.classList.add('clear');
      const label = document.createElement('b');
      label.textContent = ch === ' ' ? '␠' : ch;
      swatch.append(chip, label);
      swatch.addEventListener('click', () => { ink = ch; if (tool === 'pick') tool = 'pen'; showPalette(); draw(); });
      return swatch;
    }));
    const colour = colourOf(ink);
    const input = $('pictureInk');
    input.disabled = !colour;
    if (colour?.startsWith('#')) input.value = colour.length === 7 ? colour : '#000000';
    $('pictureInkLabel').textContent = colour
      ? `Colour of "${ink}"${isLinked(ink) ? ` (DATA line ${linked.line})` : ''}`
      : `"${ink}" is see-through`;
  }

  // -- editing
  const snapshot = () => ({ grid: grid.map(r => [...r]), colours: colours?.map(c => [...c]), custom: { ...custom } });
  const restore = s => {
    grid = s.grid; colours = s.colours;
    for (const k of Object.keys(custom)) delete custom[k];
    Object.assign(custom, s.custom);
  };
  const remember = () => { history.push(snapshot()); future.length = 0; };
  const set = (x, y) => {
    if (x < 0 || y < 0 || x >= W || y >= H) return;
    grid[y][x] = ink;
    if (mirror) grid[y][W - 1 - x] = ink;
  };
  function line(a, b) {                            // a stroke: every cell between two points
    let { x, y } = a;
    const dx = Math.abs(b.x - x), dy = -Math.abs(b.y - y), sx = x < b.x ? 1 : -1, sy = y < b.y ? 1 : -1;
    let err = dx + dy;
    for (;;) {
      set(x, y);
      if (x === b.x && y === b.y) break;
      const e2 = 2 * err;
      if (e2 >= dy) { err += dy; x += sx; }
      if (e2 <= dx) { err += dx; y += sy; }
    }
  }
  function fill(x, y) {
    const target = grid[y][x];
    if (target === ink) return;
    const todo = [[x, y]];
    while (todo.length) {
      const [cx, cy] = todo.pop();
      if (cx < 0 || cy < 0 || cx >= W || cy >= H || grid[cy][cx] !== target) continue;
      grid[cy][cx] = ink;
      todo.push([cx + 1, cy], [cx - 1, cy], [cx, cy + 1], [cx, cy - 1]);
    }
  }
  const cellAt = e => {
    const r = canvas.getBoundingClientRect();
    return { x: Math.floor((e.clientX - r.left) / zoom), y: Math.floor((e.clientY - r.top) / zoom) };
  };
  let last = null;
  canvas.onpointerdown = e => {
    const at = cellAt(e);
    if (at.x < 0 || at.y < 0 || at.x >= W || at.y >= H) return;
    e.preventDefault();
    if (e.button === 2 || e.altKey || tool === 'pick') {
      ink = grid[at.y][at.x];
      tool = tool === 'pick' ? 'pen' : tool;
      showPalette(); draw();
      return;
    }
    remember();
    if (tool === 'fill') { fill(at.x, at.y); draw(); return; }
    try { canvas.setPointerCapture(e.pointerId); } catch { /* a synthetic event */ }
    last = at;
    set(at.x, at.y);
    draw();
  };
  canvas.onpointermove = e => {
    const at = cellAt(e);
    hover = at.x >= 0 && at.y >= 0 && at.x < W && at.y < H ? at : null;
    if (last && e.buttons & 1) { line(last, at); last = at; }
    draw();
  };
  canvas.onpointerup = canvas.onpointercancel = () => { last = null; };
  canvas.onpointerleave = () => { hover = null; draw(); };
  canvas.oncontextmenu = e => e.preventDefault();

  const transform = make => { remember(); grid = make(grid); draw(); };
  const actions = {
    pictureUndo: () => { if (history.length) { future.push(snapshot()); restore(history.pop()); showPalette(); draw(); } },
    pictureRedo: () => { if (future.length) { history.push(snapshot()); restore(future.pop()); showPalette(); draw(); } },
    picturePen: () => { tool = 'pen'; draw(); },
    pictureFill: () => { tool = 'fill'; draw(); },
    picturePick: () => { tool = 'pick'; draw(); },
    pictureMirror: () => { mirror = !mirror; draw(); },
    pictureFlipX: () => transform(g => g.map(r => [...r].reverse())),
    pictureFlipY: () => transform(g => [...g].reverse()),
    pictureLeft: () => transform(g => g.map(r => [...r.slice(1), r[0]])),
    pictureRight: () => transform(g => g.map(r => [r[W - 1], ...r.slice(0, W - 1)])),
    pictureUp: () => transform(g => [...g.slice(1), g[0]]),
    pictureDown: () => transform(g => [g[H - 1], ...g.slice(0, H - 1)]),
    pictureRotate: () => { if (W === H) transform(g => g.map((r, y) => r.map((_, x) => g[W - 1 - x][y]))); },
    pictureClear: () => transform(g => g.map(r => r.map(() => ink))),
    pictureAdd: async () => {
      const ch = await ask({
        title: 'Add a character', message: 'One character to draw with (not a quote).', input: '',
        buttons: [{ label: 'Cancel' }, { label: 'Add', value: 'add', kind: 'primary' }],
      });
      const c = ch?.[0];
      if (!c || c === '"') return;
      if (!alphabet.includes(c)) alphabet = [...alphabet, c].sort((a, b) => order(a) - order(b));
      ink = c;
      showPalette(); draw();
    },
  };
  for (const [id, act] of Object.entries(actions)) $(id).onclick = act;
  $('pictureRotate').disabled = W !== H;
  $('pictureInk').onclick = () => remember();      // (one undo for all the picker's changes)
  $('pictureInk').oninput = e => {
    const value = e.target.value;
    if (isLinked(ink)) colours[+ink] = unhex(value); else custom[ink] = value;
    showPalette(); draw();
  };
  dialog.onkeydown = e => {
    if (e.target.tagName === 'INPUT') return;
    const mod = e.ctrlKey || e.metaKey;
    if (mod && e.key.toLowerCase() === 'z') { (e.shiftKey ? actions.pictureRedo : actions.pictureUndo)(); e.preventDefault(); return; }
    if (mod && e.key.toLowerCase() === 'y') { actions.pictureRedo(); e.preventDefault(); return; }
    if (mod || e.altKey) return;
    if (alphabet.includes(e.key) && (/\d/.test(e.key) || SEE_THROUGH.has(e.key))) { ink = e.key; showPalette(); draw(); e.preventDefault(); return; }
    const toolKeys = { b: 'pen', p: 'pen', g: 'fill', f: 'fill', i: 'pick' };
    if (toolKeys[e.key.toLowerCase()]) { tool = toolKeys[e.key.toLowerCase()]; draw(); e.preventDefault(); }
    else if (e.key.toLowerCase() === 'm') { mirror = !mirror; draw(); e.preventDefault(); }
  };

  // -- leaving: write it back (one change in the editor), or not
  const changed = () => grid.some((r, y) => r.join('') !== picture.rows[y]) ||
    (colours && colours.some((c, i) => hex(c) !== hex(linked.colours[i])));
  $('pictureApply').onclick = e => {
    e.preventDefault();
    const changes = [];
    grid.forEach((r, y) => { if (r.join('') !== picture.rows[y]) changes.push({ ...picture.spans[y], insert: r.join('') }); });
    if (colours) colours.flat().forEach((v, i) => {
      if (hex(colours[Math.floor(i / 3)]) === hex(linked.colours[Math.floor(i / 3)])) return;
      changes.push({ from: linked.spans[i].from, to: linked.spans[i].to, insert: v.toFixed(2) });
    });
    if (changes.length) view.dispatch({ changes, userEvent: 'input.picture' });
    try {
      const all = JSON.parse(localStorage.getItem(COLOUR_STORE) ?? '{}');
      if (Object.keys(custom).length) all[storeKey] = custom; else delete all[storeKey];
      localStorage.setItem(COLOUR_STORE, JSON.stringify(all));
    } catch { /* no storage: the colours are just for now */ }
    dialog.close('apply');
  };
  $('pictureCancel').onclick = async e => {
    e.preventDefault();
    if (changed() && await confirmDiscard() !== 'discard') return;
    dialog.close('cancel');
  };
  dialog.oncancel = async e => {                   // Escape
    e.preventDefault();
    if (changed() && await confirmDiscard() !== 'discard') return;
    dialog.close('cancel');
  };
  const confirmDiscard = () => ask({
    title: 'Leave without applying?', message: 'Your changes to the picture will be lost.',
    buttons: [{ label: 'Keep editing' }, { label: 'Leave', value: 'discard', kind: 'danger' }],
  });
  dialog.onclose = () => view.focus();

  showPalette();
  draw();
  dialog.showModal();
  canvas.focus();
}
