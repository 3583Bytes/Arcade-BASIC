// The shape of a program, for finding your way round a long one. One pass over the
// text finds its section headings (comments framed or marked by ==== or ---- rules),
// its SUBs, FUNCTIONs, multi-line DEFs, MODULEs and HANDLERs, the line numbers GOSUB
// calls (an older program's subroutines), and its pictures. outlineItems lists them,
// for the outline beside the editor and Go to; foldRanges is what the editor folds:
// those blocks and sections, and any more-indented run of lines (an IF's or FOR's body).
import { findPictures } from './pictures.js';

const COMMENT = /^\s*(\d+\s+)?(!|REM\b)(.*)$/i;
const BLOCK = /^\s*(\d+\s+)?(?:(?:PUBLIC|PRIVATE|EXTERNAL)\s+)*(SUB|FUNCTION|DEF|MODULE|HANDLER)\s+([A-Za-z][\w]*\$?)(.*)$/i;
const BLOCK_END = /^\s*(\d+\s+)?END\s+(SUB|FUNCTION|DEF|MODULE|HANDLER)\b/i;
const LABEL = /^\s*(\d+)\b/;

/** A comment that is (or has) a rule of = or -: its level (1 for =, 2 for -) and the words beside it. */
function heading(body) {
  const rule = /([=-])\1{3,}/.exec(body);
  if (!rule) return null;
  const words = body.replace(/[=-]{4,}/g, ' ').replace(/\s+/g, ' ').trim();
  return { level: rule[1] === '=' ? 1 : 2, words };
}

const analyses = new WeakMap();   // doc → its analysis (a document never changes, so it's kept)

function analyse(doc) {
  let found = analyses.get(doc);
  if (found) return found;
  const lines = [], items = [], folds = new Map();
  for (let n = 1; n <= doc.lines; n++) {
    const text = doc.line(n).text;
    const comment = COMMENT.exec(text);
    lines.push({ text, blank: !text.trim(), indent: /^\s*/.exec(text)[0].length, comment: comment ? comment[3].trim() : null });
  }
  const at = n => lines[n - 1];

  // Section headings: "! ====" / "! name" / "! ====" framed, or "! ---- name ----" on one line.
  const headings = [];
  for (let n = 1; n <= lines.length; n++) {
    const line = at(n);
    if (line.comment == null) continue;
    const h = heading(line.comment);
    if (!h) continue;
    if (!h.words) {                                // a rule: a framed heading if words and another rule follow
      let m = n + 1;
      const words = [];
      while (m <= lines.length && at(m).comment != null && !heading(at(m).comment) && words.length < 3) words.push(at(m++).comment);
      if (words.length && m <= lines.length && at(m).comment != null && heading(at(m).comment)?.words === '') {
        headings.push({ line: n, last: m, level: h.level, name: words[0] });
        n = m;
      }
      continue;
    }
    // One line; skip it if it just repeats the framed heading right above (1000 REM ---- render ----).
    const previous = headings.at(-1);
    if (previous && previous.last >= n - 1) continue;
    headings.push({ line: n, last: n, level: h.level, name: h.words.replace(/^\d+\s+(REM\s+)?/i, '') });
  }
  headings.forEach((h, i) => {
    let end = lines.length;
    for (const next of headings.slice(i + 1)) if (next.level <= h.level) { end = next.line - 1; break; }
    while (end > h.last && at(end).blank) end--;
    // A framed heading folds from its (last) name line, so folded it still says what it is.
    const from = h.last > h.line ? h.last - 1 : h.line;
    if (end > h.last) folds.set(from, end);
    items.push({ kind: 'section', name: h.name, line: h.line, level: h.level });
  });

  // SUB … END SUB and the like (a MODULE holds the others).
  const open = [];
  for (let n = 1; n <= lines.length; n++) {
    const text = at(n).text;
    const end = BLOCK_END.exec(text);
    if (end) {
      const kind = end[2].toUpperCase();
      const i = open.findLastIndex(b => b.kind === kind);
      if (i >= 0) {
        const [block] = open.splice(i, 1);
        if (n - 1 > block.line) folds.set(block.line, n - 1);
      }
      continue;
    }
    const m = BLOCK.exec(text);
    if (!m) continue;
    const kind = m[2].toUpperCase();
    if (kind === 'DEF' && /=/.test(m[4].replace(/\([^)]*\)/, ''))) continue;   // DEF F(X) = …: one line
    items.push({ kind: kind.toLowerCase(), name: m[3], line: n, level: open.some(b => b.kind === 'MODULE') ? 2 : 1 });
    open.push({ kind, line: n });
  }

  // Subroutines: the lines GOSUB goes to, named by a REM there (unless a heading already is).
  const targets = new Set([...doc.toString().matchAll(/\bGO\s*SUB\s+(\d+)/gi)].map(m => +m[1]));
  for (let n = 1; n <= lines.length; n++) {
    const label = LABEL.exec(at(n).text);
    if (!label || !targets.has(+label[1])) continue;
    if (headings.some(h => h.line <= n && n - h.last <= 2)) continue;
    const rest = at(n).text.slice(label[0].length).trim();
    const name = /^REM\b/i.test(rest) ? rest.slice(3).replace(/[=-]{2,}/g, ' ').trim() : rest;
    items.push({ kind: 'gosub', name: `${label[1]}${name ? ' ' + name.slice(0, 40) : ''}`, line: n, level: 2 });
  }

  for (const p of findPictures(doc)) items.push({ kind: 'picture', name: p.name || `${p.width} × ${p.height}`, line: p.firstLine, level: 2 });

  // Indented runs: a line followed by more-indented ones folds them.
  for (let n = 1; n < lines.length; n++) {
    if (folds.has(n) || at(n).blank) continue;
    let next = n + 1;
    while (next <= lines.length && at(next).blank) next++;
    if (next > lines.length || at(next).indent <= at(n).indent) continue;
    let end = next;
    for (let m = next + 1; m <= lines.length; m++) {
      if (at(m).blank) continue;
      if (at(m).indent <= at(n).indent) break;
      end = m;
    }
    folds.set(n, end);
  }

  items.sort((a, b) => a.line - b.line);
  found = { items, folds };
  analyses.set(doc, found);
  return found;
}

/** The outline: [{ kind, name, line, level }] in line order. */
export const outlineItems = doc => analyse(doc).items;

/** For the editor's fold service: what folds at line n, or null. */
export function foldAt(state, lineStart) {
  const doc = state.doc, line = doc.lineAt(lineStart);
  const end = analyse(doc).folds.get(line.number);
  return end ? { from: line.to, to: doc.line(end).to } : null;
}

// -- The outline beside the editor, and Go to ------------------------------------------------

const KIND = { section: '§', module: 'M', sub: 'S', function: 'ƒ', def: 'ƒ', handler: 'H', gosub: '↳', picture: '▦' };
const KIND_NAME = { section: 'Section', module: 'MODULE', sub: 'SUB', function: 'FUNCTION', def: 'DEF', handler: 'HANDLER', gosub: 'Subroutine (GOSUB)', picture: 'Picture' };

function itemButton(item, onGo) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = `item ${item.kind} level${item.level}`;
  button.title = `${KIND_NAME[item.kind]} · line ${item.line}`;
  const tag = document.createElement('i');
  tag.textContent = KIND[item.kind];
  const name = document.createElement('span');
  name.textContent = item.name;
  button.append(tag, name);
  button.addEventListener('click', () => onGo(item.line));
  return button;
}

/** The outline panel: show(doc) when the program changes, follow(line) as the cursor moves. */
export function createOutline(panel, { onGo }) {
  const filter = panel.querySelector('input'), list = panel.querySelector('.items');
  let items = [], current = -1;
  const render = () => {
    const words = filter.value.trim().toLowerCase();
    const shown = items.filter(i => !words || i.name.toLowerCase().includes(words));
    list.replaceChildren(...shown.map(i => itemButton(i, onGo)));
    if (!shown.length) {
      const empty = document.createElement('p');
      empty.textContent = items.length ? 'Nothing matches.' : 'No sections, SUBs or FUNCTIONs yet. A comment like ! ---- Drawing ---- starts a section.';
      list.append(empty);
    }
    mark();
  };
  const mark = () => {
    for (const b of list.children) b.classList?.toggle('current', b.title.endsWith(`line ${current}`));
  };
  filter.addEventListener('input', render);
  return {
    show(doc) { items = outlineItems(doc); render(); },
    follow(line) {
      const here = items.filter(i => i.line <= line && i.kind !== 'picture').at(-1)?.line ?? -1;
      if (here !== current) { current = here; mark(); }
    },
  };
}

/** Go to: a list of the outline to filter by typing (or a line number), Enter to go. */
export function openGoTo(dialog, doc, onGo) {
  const input = dialog.querySelector('input'), list = dialog.querySelector('.items');
  const items = outlineItems(doc);
  let chosen = 0, shown = [];
  const render = () => {
    const words = input.value.trim().toLowerCase();
    shown = /^\d+$/.test(words)
      ? [{ kind: 'gosub', name: `Line ${words}`, line: Math.min(+words, doc.lines), level: 1 }]
      : items.filter(i => !words || i.name.toLowerCase().includes(words));
    chosen = Math.min(chosen, Math.max(0, shown.length - 1));
    list.replaceChildren(...shown.map((item, i) => {
      const b = itemButton(item, line => { dialog.close(); onGo(line); });
      if (i === chosen) b.classList.add('chosen');
      return b;
    }));
    list.querySelector('.chosen')?.scrollIntoView({ block: 'nearest' });
  };
  input.value = '';
  input.oninput = () => { chosen = 0; render(); };
  input.onkeydown = e => {
    if (e.key === 'ArrowDown') { chosen = Math.min(chosen + 1, shown.length - 1); render(); e.preventDefault(); }
    else if (e.key === 'ArrowUp') { chosen = Math.max(chosen - 1, 0); render(); e.preventDefault(); }
    else if (e.key === 'Enter') { e.preventDefault(); const item = shown[chosen]; dialog.close(); if (item) onGo(item.line); }
  };
  render();
  dialog.showModal();
  input.focus();
}
