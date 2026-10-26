// Colours in the code, shown as colours. Beside each
//   SET POINT|LINE|TEXT|AREA COLOR n    a swatch of colour n — click it to pick another
//                                       number from the palette;
//   SET COLOR MIX (n) r, g, b           a swatch of that mix — click it for a colour
//                                       picker, which rewrites r, g, b;
//   a picture's colour DATA             a swatch per red, green, blue triple (the DATA
//   (see pictures.js)                   named like a picture), picked the same way.
// Colour n is the starting palette's (the engine's GfxPalette) unless the program sets
// it with a literal SET COLOR MIX. openPalette shows all 256: the running program's,
// or the starting ones.
import { ViewPlugin, Decoration, WidgetType } from './lib/codemirror.js';
import { findPictures, findColours } from './pictures.js';
import { commentStart } from './basic-text.js';

const COLOR_INDEX = /\bSET\s+(?:POINT|LINE|TEXT|AREA)\s+COLOR\s+(\d+)\b/gi;
const COLOR_MIX = /\bSET\s+COLOR\s+MIX\s*\(\s*([^)]*?)\s*\)\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)/gi;

let defaults = null;            // the 256 starting colours, '#rrggbb' (setDefaultPalette)
let version = 0;                // bumped when they arrive, so the swatches redraw

export const hex = ([r, g, b]) =>
  '#' + [r, g, b].map(v => Math.round(Math.min(1, Math.max(0, v)) * 255).toString(16).padStart(2, '0')).join('');
export const unhex = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16) / 255);

/** The engine's starting palette, 0xRRGGBB each. */
export function setDefaultPalette(rgb) {
  defaults = [...rgb].map(c => '#' + (c >>> 0).toString(16).padStart(6, '0'));
  version++;
}

/** Literal SET COLOR MIX in a document: index → '#rrggbb' (the last one wins). */
function literalMixes(doc) {
  const mixes = new Map();
  const text = doc.toString();
  for (const m of text.matchAll(COLOR_MIX)) {
    if (/^\d+$/.test(m[1])) mixes.set(+m[1], hex([+m[2], +m[3], +m[4]]));
  }
  return mixes;
}

class Swatch extends WidgetType {
  constructor(colour, title, onPick) {
    super();
    this.colour = colour;
    this.title = title;
    this.onPick = onPick;
  }
  eq(other) { return other.colour === this.colour && other.title === this.title; }
  toDOM(view) {
    const swatch = document.createElement('span');
    swatch.className = 'cm-swatch';
    swatch.style.background = this.colour;
    swatch.title = this.title;
    swatch.addEventListener('mousedown', e => { e.preventDefault(); this.onPick(view, swatch); });
    return swatch;
  }
  ignoreEvent() { return true; }
}

// One colour input for every swatch, shown where the swatch is (the browser's picker opens there).
let picker = null;
function pickColour(anchor, start, onInput) {
  if (!picker) {
    picker = document.createElement('input');
    picker.type = 'color';
    picker.className = 'swatchPicker';
    document.body.append(picker);
  }
  const r = anchor.getBoundingClientRect();
  Object.assign(picker.style, { left: `${r.left}px`, top: `${r.bottom}px` });
  picker.value = start;
  picker.oninput = () => onInput(picker.value);
  picker.click();
}

/** Rewrite the three numbers of an r, g, b triple (found again by its line and order). */
function rewrite(view, find, colour) {
  const spans = find(view.state.doc);
  if (!spans) return;
  const values = unhex(colour);
  view.dispatch({
    changes: spans.map((s, i) => ({ from: s.from, to: s.to, insert: values[i].toFixed(2) })),
    userEvent: 'input.colour',
  });
}

/** The editor extension. openPalette(view, { index, onPick }) picks a colour number. */
export function colourSwatches(openPalette) {
  return ViewPlugin.fromClass(class {
    constructor(view) {
      this.seen = version;
      this.scan(view);
      this.decorations = this.build(view);
    }
    update(u) {
      if (u.docChanged) this.scan(u.view);
      if (u.docChanged || u.viewportChanged || this.seen !== version) {
        this.seen = version;
        this.decorations = this.build(u.view);
      }
    }
    /** What the whole document says about colours: literal mixes, and the colour DATA lines. */
    scan(view) {
      const doc = view.state.doc;
      this.mixes = literalMixes(doc);
      this.colourLines = new Map();               // line number → [{ from, to, value } × 3k]
      for (const picture of findPictures(doc)) {
        const linked = findColours(doc, picture);
        if (linked) this.colourLines.set(linked.line, linked.spans);
      }
    }
    build(view) {
      const widgets = [];
      const doc = view.state.doc;
      for (const { from, to } of view.visibleRanges) {
        for (let n = doc.lineAt(from).number; n <= doc.lineAt(to).number; n++) {
          const line = doc.line(n);
          const code = line.text.slice(0, commentStart(line.text));   // (not in a comment)
          if (defaults) {
            for (const m of code.matchAll(COLOR_INDEX)) {
              const index = +m[1];
              const at = line.from + m.index + m[0].length - m[1].length;
              const colour = this.mixes.get(index) ?? defaults[index] ?? defaults[1];
              widgets.push(Decoration.widget({
                widget: new Swatch(colour, `Colour ${index}: click to pick another`, view2 =>
                  openPalette(view2, {
                    index,
                    onPick: chosen => {
                      const now = view2.state.doc.line(n);
                      const again = [...now.text.matchAll(COLOR_INDEX)].find(x => x.index === m.index);
                      if (!again) return;
                      const start = now.from + again.index + again[0].length - again[1].length;
                      view2.dispatch({ changes: { from: start, to: start + again[1].length, insert: String(chosen) } });
                    },
                  })),
                side: -1,
              }).range(at));
            }
          }
          for (const m of code.matchAll(COLOR_MIX)) {
            const numbersAt = line.from + m.index + m[0].indexOf(m[2], m[0].indexOf(')') + 1);   // before red
            const find = d => {
              const now = d.line(n);
              const again = [...now.text.matchAll(COLOR_MIX)].find(x => x.index === m.index);
              if (!again) return null;
              const base = now.from + again.index;
              let pos = again[0].indexOf(')');
              return [2, 3, 4].map(g => {
                pos = again[0].indexOf(again[g], pos);
                const span = { from: base + pos, to: base + pos + again[g].length };
                pos += again[g].length;
                return span;
              });
            };
            const colour = hex([+m[2], +m[3], +m[4]]);
            widgets.push(Decoration.widget({
              widget: new Swatch(colour, `SET COLOR MIX ${colour}: click to change it`, (v, anchor) =>
                pickColour(anchor, colour, value => rewrite(v, find, value))),
              side: -1,
            }).range(numbersAt));
          }
          const spans = this.colourLines.get(n);
          if (spans) {
            for (let i = 0; i + 2 < spans.length; i += 3) {
              const colour = hex([spans[i].value, spans[i + 1].value, spans[i + 2].value]);
              const which = i;
              const find = d => {
                const now = d.line(n);
                const parts = [];
                for (const p of now.text.matchAll(/[\d.]+/g)) parts.push({ from: now.from + p.index, to: now.from + p.index + p[0].length });
                // (skip a line number at the start)
                const numbers = /^\s*\d+\s+DATA/i.test(now.text) ? parts.slice(1) : parts;
                return numbers.slice(which, which + 3).length === 3 ? numbers.slice(which, which + 3) : null;
              };
              widgets.push(Decoration.widget({
                widget: new Swatch(colour, `Colour ${which / 3} of the picture: ${colour} — click to change it`, (v, anchor) =>
                  pickColour(anchor, colour, value => rewrite(v, find, value))),
                side: -1,
              }).range(spans[i].from));
            }
          }
        }
      }
      return Decoration.set(widgets, true);
    }
  }, { decorations: v => v.decorations });
}

// -- The palette dialog -----------------------------------------------------------------------

const $ = id => document.getElementById(id);

/**
 * All 256 colours. current() gives the running program's ('#rrggbb' × 256), or null for
 * the starting ones. With onPick, choosing one calls it with the number; insert(text)
 * puts a line of code at the cursor.
 */
export function openPalette({ current, index = 1, onPick = null, insert = null }) {
  const dialog = $('paletteDialog');
  const running = current?.();
  const colours = running ?? defaults ?? [];
  $('paletteSource').textContent = running
    ? 'The running program’s colours, as its SET COLOR MIX statements have left them.'
    : 'The colours every program starts with: 0–15 the standard ones, 16–255 the xterm palette. SET COLOR MIX changes any of them.';
  let selected = index;
  const grid = $('paletteGrid');
  grid.replaceChildren(...colours.map((colour, i) => {
    const cell = document.createElement('button');
    cell.type = 'button';
    cell.style.background = colour;
    cell.title = `${i}: ${colour}`;
    cell.dataset.index = i;
    cell.addEventListener('click', () => { selected = i; show(); });
    cell.addEventListener('dblclick', () => { selected = i; if (onPick) use(); });
    return cell;
  }));
  const show = () => {
    for (const cell of grid.children) cell.classList.toggle('selected', +cell.dataset.index === selected);
    const colour = colours[selected];
    const [r, g, b] = unhex(colour);
    $('paletteChosen').style.background = colour;
    $('paletteAbout').textContent = `Colour ${selected} · ${colour} · red ${r.toFixed(2)}, green ${g.toFixed(2)}, blue ${b.toFixed(2)}`;
    $('paletteUse').hidden = !onPick;
    $('paletteUse').textContent = `Use colour ${selected}`;
  };
  const use = () => { onPick?.(selected); dialog.close(); };
  $('paletteUse').onclick = e => { e.preventDefault(); use(); };
  $('paletteInsert').hidden = !insert;
  $('paletteInsert').onclick = e => {
    e.preventDefault();
    const [r, g, b] = unhex(colours[selected]);
    insert?.(`SET COLOR MIX (${selected}) ${r.toFixed(2)}, ${g.toFixed(2)}, ${b.toFixed(2)}`);
    dialog.close();
  };
  show();
  dialog.showModal();
  grid.children[selected]?.focus();
}
