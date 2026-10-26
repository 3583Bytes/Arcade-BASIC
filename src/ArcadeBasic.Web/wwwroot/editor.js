// The code editor: CodeMirror 6 (lib/codemirror.js, built from ../codemirror) set
// up for BASIC. Its colours are the page's theme variables (style.css), so a theme
// change restyles it with no work here. The words it colours and suggests, and the
// problems it underlines, come from the engine once .NET has loaded (setWords,
// setChecker); until then it's a plain editor. For the debugger it keeps each
// program's breakpoints (a gutter of red dots), marks the line a paused program is
// on, and shows a variable's value when you point at it.
import {
  EditorState, StateEffect, StateField, RangeSet, Compartment, EditorView, Decoration, keymap, lineNumbers,
  gutter, GutterMarker, hoverTooltip,
  highlightActiveLine, highlightActiveLineGutter, highlightSpecialChars, drawSelection, dropCursor,
  rectangularSelection, crosshairCursor, defaultKeymap, history, historyKeymap, indentWithTab,
  StreamLanguage, HighlightStyle, syntaxHighlighting, syntaxTree, indentOnInput, bracketMatching, indentUnit,
  tags, search, searchKeymap, highlightSelectionMatches, autocompletion, completionKeymap,
  closeBrackets, closeBracketsKeymap, linter, lintGutter, lintKeymap, openLintPanel, nextDiagnostic, forceLinting,
} from './lib/codemirror.js';
import { pictureLens } from './pictures.js';

// The engine's reserved words and supplied functions, upper-case (setWords).
const reserved = new Set(), builtins = new Set();
// After these a number is a line number: GOTO 100, ON K GOSUB 200, 300, THEN 400.
const JUMPS = new Set(['GOTO', 'GOSUB', 'THEN', 'ELSE', 'RESTORE']);

/** BASIC, a line at a time, the way the engine's lexer reads it. */
function basic() {
  return StreamLanguage.define({
    name: 'basic',
    startState: () => ({ start: true, jump: false, go: false }),
    token(stream, state) {
      if (stream.sol()) { state.start = true; state.jump = state.go = false; }
      if (stream.eatSpace()) return null;
      const start = state.start;
      state.start = false;
      const ch = stream.peek();
      if (ch === '!') { stream.skipToEnd(); return 'comment'; }
      if (ch === '"') {                                    // "" is a quote inside a string
        stream.next();
        while (!stream.eol()) if (stream.next() === '"' && !stream.eat('"')) break;
        state.jump = false;
        return 'string';
      }
      if (stream.match(/^(\d+\.?\d*|\.\d+)(e[+-]?\d+)?/i)) return start || state.jump ? 'label' : 'number';
      if (stream.match(/^[a-z][a-z0-9_]*\$?/i)) {
        const word = stream.current().toUpperCase();
        if (word === 'REM') { stream.skipToEnd(); return 'comment'; }
        const go = state.go;
        state.go = word === 'GO';
        if (reserved.has(word)) {
          state.jump = JUMPS.has(word) || (go && (word === 'TO' || word === 'SUB'));   // GO TO, GO SUB
          return 'keyword';
        }
        state.jump = false;
        return builtins.has(word) ? 'builtin' : 'variableName';
      }
      stream.next();
      if (ch === ',' && state.jump) return 'punctuation';
      state.jump = state.go = false;
      return /[-+*/^=<>&]/.test(ch) ? 'operator' : 'punctuation';
    },
    languageData: { commentTokens: { line: '!' }, wordChars: '$' },
    tokenTable: { builtin: tags.standard(tags.variableName), label: tags.labelName },
  });
}

const highlight = HighlightStyle.define([
  { tag: tags.keyword, color: 'var(--syn-kw)', fontWeight: 'var(--syn-kw-weight)' },
  { tag: tags.standard(tags.variableName), color: 'var(--syn-fn)' },
  { tag: tags.variableName, color: 'var(--syn-var)' },
  { tag: tags.string, color: 'var(--syn-str)' },
  { tag: tags.number, color: 'var(--syn-num)' },
  { tag: tags.comment, color: 'var(--syn-com)', fontStyle: 'italic' },
  { tag: tags.labelName, color: 'var(--syn-label)' },
  { tag: tags.operator, color: 'var(--syn-op)' },
  { tag: tags.punctuation, color: 'var(--syn-punct)' },
]);

const theme = EditorView.theme({
  '&': { height: '100%', color: 'var(--ed-text)', backgroundColor: 'var(--ed-bg)', fontSize: '13px' },
  '.cm-scroller': { fontFamily: 'var(--code-font)', lineHeight: '1.5' },
  '.cm-content': { caretColor: 'var(--ed-cursor)', textShadow: 'var(--glow)' },
  '.cm-cursor, .cm-dropCursor': { borderLeft: '2px solid var(--ed-cursor)' },
  '&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection':
    { backgroundColor: 'var(--ed-sel)' },
  '.cm-activeLine': { backgroundColor: 'var(--ed-line)' },
  '.cm-gutters': { backgroundColor: 'var(--ed-gutter)', color: 'var(--ed-gutter-text)', border: 'none' },
  '.cm-activeLineGutter': { backgroundColor: 'var(--ed-line)', color: 'var(--ed-text)' },
  '.cm-selectionMatch': { backgroundColor: 'var(--ed-match)' },
  '.cm-searchMatch': { backgroundColor: 'var(--ed-match)', outline: '1px solid var(--muted)' },
  '.cm-searchMatch.cm-searchMatch-selected': { backgroundColor: 'var(--ed-sel)' },
  '&.cm-focused .cm-matchingBracket': { backgroundColor: 'var(--ed-match)', outline: '1px solid var(--muted)' },
  '.cm-panels': { backgroundColor: 'var(--bar)', color: 'var(--bar-text)' },
  '.cm-panels.cm-panels-top': { borderBottom: '1px solid var(--border)' },
  '.cm-panels.cm-panels-bottom': { borderTop: '1px solid var(--border)' },
  '.cm-textfield': { backgroundColor: 'var(--panel)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: '4px' },
  '.cm-button': { backgroundImage: 'none', backgroundColor: 'var(--panel)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: '4px' },
  '.cm-tooltip': { backgroundColor: 'var(--menu)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: '6px', overflow: 'hidden' },
  '.cm-tooltip-autocomplete > ul > li[aria-selected]': { backgroundColor: 'var(--ed-sel)', color: 'var(--ed-text)' },
  '.cm-completionDetail': { color: 'var(--muted)', fontStyle: 'normal' },
  '.cm-diagnostic': { whiteSpace: 'pre-wrap', fontFamily: 'var(--ui-font)' },
  '.cm-diagnostic-error': { borderLeftColor: 'var(--danger)' },
  '.cm-diagnostic-warning': { borderLeftColor: 'var(--warn)' },
  '.cm-panel.cm-panel-lint ul [aria-selected]': { backgroundColor: 'var(--ed-sel)', color: 'var(--ed-text)' },
  '.cm-panel.cm-panel-lint ul:focus [aria-selected]': { backgroundColor: 'var(--ed-sel)', color: 'var(--ed-text)' },
  '.cm-runtimeError': { backgroundColor: 'var(--ed-error-line)', boxShadow: 'inset 3px 0 var(--danger)' },
  '.cm-pausedLine': { backgroundColor: 'var(--ed-paused-line)', boxShadow: 'inset 3px 0 var(--paused)' },
  '.cm-breakpoints .cm-gutterElement': {
    cursor: 'pointer', minWidth: '18px', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '1px',
  },
  '.cm-breakpoints .cm-gutterElement:hover:empty::after': {
    content: '""', width: '9px', height: '9px', borderRadius: '50%', background: 'var(--bp)', opacity: '.35',
  },
  '.cm-bp': { display: 'inline-block', width: '9px', height: '9px', borderRadius: '50%', background: 'var(--bp)' },
  '.cm-bp-here': {
    display: 'inline-block', width: '0', height: '0', borderTop: '5px solid transparent',
    borderBottom: '5px solid transparent', borderLeft: '8px solid var(--paused)',
  },
  '.cm-valueTip': { padding: '3px 8px', fontFamily: 'var(--code-font)', whiteSpace: 'pre' },
  '.cm-pictureLens': { display: 'flex', gap: '8px', alignItems: 'baseline', padding: '2px 0 1px', fontFamily: 'var(--ui-font)', fontSize: '12px', color: 'var(--muted)' },
  '.cm-pictureLens button': {
    font: 'inherit', color: 'var(--accent)', background: 'transparent', border: '1px solid var(--border)', borderRadius: '4px',
    padding: '0 6px', cursor: 'pointer',
  },
  '.cm-pictureLens button:hover': { borderColor: 'var(--accent)' },
});

// The line a program stopped on with an error, until the next run.
const setRuntimeError = StateEffect.define();
const runtimeError = StateField.define({
  create: () => Decoration.none,
  update(marks, tr) {
    marks = marks.map(tr.changes);
    for (const e of tr.effects) {
      if (e.is(setRuntimeError))
        marks = e.value == null ? Decoration.none : Decoration.set([Decoration.line({ class: 'cm-runtimeError' }).range(e.value)]);
    }
    return marks;
  },
  provide: field => EditorView.decorations.from(field),
});

/** Where the line number, SUB, FUNCTION, DEF or HANDLER named at pos is defined. */
function definitionOf(state, pos) {
  const word = state.wordAt(pos);
  if (!word) return null;
  const name = state.sliceDoc(word.from, word.to).toUpperCase();
  const quoted = name.replace(/\$/g, '\\$');
  const pattern = /^\d+$/.test(name)
    ? new RegExp(`^\\s*0*${Number(name)}(?!\\d)`)
    : new RegExp(`^\\s*(\\d+\\s+)?(EXTERNAL\\s+)?(SUB|FUNCTION|DEF|HANDLER)\\s+${quoted}(?![A-Z0-9_$])`, 'i');
  for (let i = 1; i <= state.doc.lines; i++) {
    const line = state.doc.line(i);
    if (pattern.test(line.text)) return line.from + line.text.search(/\S/);
  }
  return null;
}

// -- Breakpoints and the paused line ----------------------------------------------------

class Dot extends GutterMarker {
  toDOM() { const dot = document.createElement('span'); dot.className = 'cm-bp'; return dot; }
}
class Arrow extends GutterMarker {
  toDOM() { const arrow = document.createElement('span'); arrow.className = 'cm-bp-here'; return arrow; }
}
const dot = new Dot(), arrow = new Arrow();

// Breakpoints, by the start of their line; they move with the text around them.
const toggleBreakpoint = StateEffect.define({ map: (pos, changes) => changes.mapPos(pos) });
const breakpoints = StateField.define({
  create: () => RangeSet.empty,
  update(set, tr) {
    set = set.map(tr.changes);
    for (const e of tr.effects) {
      if (!e.is(toggleBreakpoint)) continue;
      let has = false;
      set.between(e.value, e.value, from => { if (from === e.value) has = true; });
      set = has ? set.update({ filter: from => from !== e.value }) : set.update({ add: [dot.range(e.value)] });
    }
    return set;
  },
});

// The line (its start) a paused program is about to run.
const setPaused = StateEffect.define();
const paused = StateField.define({
  create: () => null,
  update(pos, tr) {
    if (pos != null) pos = tr.changes.mapPos(pos);
    for (const e of tr.effects) if (e.is(setPaused)) pos = e.value;
    return pos;
  },
  provide: field => EditorView.decorations.from(field, pos =>
    pos == null ? Decoration.none : Decoration.set([Decoration.line({ class: 'cm-pausedLine' }).range(pos)])),
});

function breakpointLines(state) {
  const lines = new Set();
  for (let it = state.field(breakpoints).iter(); it.value; it.next()) lines.add(state.doc.lineAt(it.from).number);
  return [...lines].sort((a, b) => a - b);
}

function goToDefinition(view, pos) {
  const at = definitionOf(view.state, pos);
  if (at == null) return false;
  view.dispatch({ selection: { anchor: at }, effects: EditorView.scrollIntoView(at, { y: 'center' }) });
  return true;
}

/** Suggestions: the reserved words, the supplied functions, and the names the program uses. */
function suggestions(state, typing) {
  const options = [];
  for (const w of reserved) options.push({ label: w, type: 'keyword' });
  for (const w of builtins) options.push({ label: w, type: 'function', detail: 'built in' });
  const seen = new Map();
  const text = state.doc.toString().replace(/"([^"\n]|"")*"?|(!|\bREM\b).*$/gim, ' ');   // not strings or comments
  for (const [w] of text.matchAll(/[A-Za-z][A-Za-z0-9_]*\$?/g)) {
    const key = w.toUpperCase();
    if (reserved.has(key) || builtins.has(key)) continue;
    const s = seen.get(key);
    if (s) s.count++; else seen.set(key, { label: w, count: 1 });
  }
  for (const [key, s] of seen) {
    if (key === typing && s.count === 1) continue;   // just the word being typed
    options.push({ label: s.label, type: 'variable' });
  }
  return options;
}

function complete(context) {
  const word = context.matchBefore(/[A-Za-z][A-Za-z0-9_]*\$?/);
  if (!word && !context.explicit) return null;
  if (word && !context.explicit && word.to - word.from < 2) return null;
  const node = syntaxTree(context.state).resolveInner(context.pos, -1);
  if (/comment|string/.test(node.name)) return null;
  const from = word ? word.from : context.pos;
  return {
    from,
    options: suggestions(context.state, word ? word.text.toUpperCase() : ''),
    validFor: /^[A-Za-z][A-Za-z0-9_]*\$?$/,
  };
}

/**
 * Make the editor in parent. hooks.onUpdate(update) hears every change (text,
 * cursor, a different program shown); hooks.onProblems(list) gets each new set of
 * problems.
 */
export function createEditor(parent, hooks) {
  const language = new Compartment();
  let lang = basic();
  let check = null;   // text → [{ from, to, severity, message }]
  let statementLines = null;   // the lines a breakpoint can stop at (setStatementLines)
  let valueOf = null;          // name → its value while paused, else null (setValueLookup)

  /** Breakpoint on or off at the line holding pos. A new one on a line with no
   *  statement goes on the next line that has one, where the program can stop. */
  function toggleAt(view, pos) {
    const doc = view.state.doc;
    let line = doc.lineAt(pos);
    let has = false;
    view.state.field(breakpoints).between(line.from, line.from, from => { if (from === line.from) has = true; });
    if (!has && statementLines && !statementLines.has(line.number)) {
      const next = [...statementLines].filter(n => n > line.number && n <= doc.lines).sort((a, b) => a - b)[0];
      if (next) {
        line = doc.line(next);
        view.state.field(breakpoints).between(line.from, line.from, from => { if (from === line.from) has = true; });
        if (has) return true;                              // already there
      }
    }
    view.dispatch({ effects: toggleBreakpoint.of(line.from) });
    hooks.onBreakpoints?.(breakpointLines(view.state));
    return true;
  }

  const breakpointGutter = gutter({
    class: 'cm-breakpoints',
    markers: view => {
      const set = view.state.field(breakpoints), at = view.state.field(paused);
      return at == null ? set : set.update({ add: [arrow.range(at)] });
    },
    initialSpacer: () => dot,
    domEventHandlers: { mousedown: (view, line) => toggleAt(view, line.from) },
  });

  const values = hoverTooltip((view, pos) => {
    const word = valueOf && view.state.wordAt(pos);
    if (!word) return null;
    const name = view.state.sliceDoc(word.from, word.to);
    const value = valueOf(name);
    if (value == null) return null;
    return {
      pos: word.from, end: word.to, above: true,
      create() {
        const dom = document.createElement('div');
        dom.className = 'cm-valueTip';
        dom.textContent = `${name.toUpperCase()} = ${value}`;
        return { dom };
      },
    };
  });

  const problems = linter(view => {
    if (!check) return [];
    const doc = view.state.doc;
    const found = check(doc.toString());
    for (const d of found) {
      d.from = Math.min(d.from, doc.length);
      d.to = Math.min(Math.max(d.to, d.from), doc.length);
      if (doc.sliceString(d.from, d.to).trim() !== '') continue;
      // Found at a line break or the end (a missing ")", say): mark the word before it.
      let at = d.from;
      while (at > 0 && /\s/.test(doc.sliceString(at - 1, at))) at--;
      if (at === 0) { d.to = Math.min(doc.length, Math.max(d.to, 1)); continue; }
      const word = view.state.wordAt(at - 1);
      d.from = word ? word.from : at - 1;
      d.to = at;
    }
    hooks.onProblems?.(found);
    return found;
  }, { delay: 350 });

  const lenses = pictureLens((view, line) => hooks.onEditPicture?.(view, line));
  const extensions = () => [
    breakpoints, paused, breakpointGutter, values, lenses,
    lineNumbers(), highlightActiveLineGutter(), highlightSpecialChars(), history(), drawSelection(), dropCursor(),
    EditorState.allowMultipleSelections.of(true), indentOnInput(), bracketMatching(), closeBrackets(),
    autocompletion({ override: [complete], icons: false }), rectangularSelection(), crosshairCursor(),
    highlightActiveLine(), highlightSelectionMatches(), search({ top: true }),
    EditorState.tabSize.of(2), indentUnit.of('  '),
    language.of(lang), syntaxHighlighting(highlight), theme, problems, lintGutter(), runtimeError,
    keymap.of([
      { key: 'F12', run: view => goToDefinition(view, view.state.selection.main.head) },
      { key: 'F9', run: view => toggleAt(view, view.state.selection.main.head) },
      ...closeBracketsKeymap, ...defaultKeymap, ...searchKeymap, ...historyKeymap, ...completionKeymap,
      ...lintKeymap, indentWithTab,
    ]),
    EditorView.domEventHandlers({
      mousedown(event, view) {                             // Ctrl/Cmd+click: go to the definition
        if (!(event.ctrlKey || event.metaKey) || event.button !== 0) return false;
        const pos = view.posAtCoords({ x: event.clientX, y: event.clientY });
        if (pos == null || !goToDefinition(view, pos)) return false;
        event.preventDefault();
        return true;
      },
    }),
    EditorView.updateListener.of(update => hooks.onUpdate?.(update)),
  ];

  // Nothing to edit until a program is shown.
  const view = new EditorView({ parent, state: EditorState.create({ extensions: [extensions(), EditorView.editable.of(false)] }) });

  return {
    view,
    /** A program's editor state: its text, undo history, cursor, breakpoints (line numbers). */
    create(text, cursor = 0, lines = []) {
      const state = EditorState.create({ doc: text, selection: { anchor: Math.min(cursor, text.length) }, extensions: extensions() });
      const starts = [...new Set(lines.filter(n => n >= 1 && n <= state.doc.lines))].map(n => state.doc.line(n).from);
      return starts.length ? state.update({ effects: starts.map(at => toggleBreakpoint.of(at)) }).state : state;
    },
    /** The lines with breakpoints in a program's state (the one showing by default). */
    breakpointLines(state = view.state) { return breakpointLines(state); },
    /** The lines a breakpoint can stop at, from the engine's last check. */
    setStatementLines(lines) { statementLines = new Set(lines); },
    /** Mark the line (1-based) a paused program is about to run; 0 clears it. */
    showPaused(line) {
      if (!line || line > view.state.doc.lines) { view.dispatch({ effects: setPaused.of(null) }); return; }
      const at = view.state.doc.line(line).from;
      view.dispatch({ effects: [setPaused.of(at), EditorView.scrollIntoView(at, { y: 'nearest', yMargin: 60 })] });
    },
    /** Pointing at a name shows lookup(name)'s answer (null: nothing); null turns it off. */
    setValueLookup(lookup) { valueOf = lookup; },
    goToLine(line) {
      if (line < 1 || line > view.state.doc.lines) return;
      const at = view.state.doc.line(line).from;
      view.dispatch({ selection: { anchor: at }, effects: EditorView.scrollIntoView(at, { y: 'center' }) });
      view.focus();
    },
    /** Show a program's state (switching tabs). */
    show(state) { view.setState(state); },
    get state() { return view.state; },
    focus() { view.focus(); },
    /** The engine's words; returns a function bringing another program's state up to date. */
    setWords(reservedWords, builtinWords) {
      reservedWords.forEach(w => reserved.add(w));
      builtinWords.forEach(w => builtins.add(w));
      lang = basic();
      view.dispatch({ effects: language.reconfigure(lang) });
      return state => state.update({ effects: language.reconfigure(lang) }).state;
    },
    /** The engine's checker: text → problems. */
    setChecker(fn) {
      check = fn;
      forceLinting(view);
    },
    /** Point at the line (1-based) a program stopped on with an error. */
    showRuntimeError(line) {
      if (line < 1 || line > view.state.doc.lines) return;
      const at = view.state.doc.line(line).from;
      view.dispatch({ selection: { anchor: at }, effects: [setRuntimeError.of(at), EditorView.scrollIntoView(at, { y: 'center' })] });
    },
    clearRuntimeError() { view.dispatch({ effects: setRuntimeError.of(null) }); },
    /** Check now and put the cursor on the first problem. */
    goToFirstProblem() {
      forceLinting(view);
      setTimeout(() => {
        view.dispatch({ selection: { anchor: 0 } });
        nextDiagnostic(view);
        view.focus();
      }, 0);
    },
    openProblems() { openLintPanel(view); },
  };
}
