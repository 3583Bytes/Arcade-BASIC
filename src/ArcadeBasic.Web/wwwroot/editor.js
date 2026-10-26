// The code editor: CodeMirror 6 (lib/codemirror.js, built from ../codemirror) set
// up for BASIC. Its colours are the page's theme variables (style.css), so a theme
// change restyles it with no work here. The words it colours and suggests, and the
// problems it underlines, come from the engine once .NET has loaded (setWords,
// setChecker); until then it's a plain editor.
import {
  EditorState, StateEffect, StateField, Compartment, EditorView, Decoration, keymap, lineNumbers,
  highlightActiveLine, highlightActiveLineGutter, highlightSpecialChars, drawSelection, dropCursor,
  rectangularSelection, crosshairCursor, defaultKeymap, history, historyKeymap, indentWithTab,
  StreamLanguage, HighlightStyle, syntaxHighlighting, syntaxTree, indentOnInput, bracketMatching, indentUnit,
  tags, search, searchKeymap, highlightSelectionMatches, autocompletion, completionKeymap,
  closeBrackets, closeBracketsKeymap, linter, lintGutter, lintKeymap, openLintPanel, nextDiagnostic, forceLinting,
} from './lib/codemirror.js';

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

  const extensions = () => [
    lineNumbers(), highlightActiveLineGutter(), highlightSpecialChars(), history(), drawSelection(), dropCursor(),
    EditorState.allowMultipleSelections.of(true), indentOnInput(), bracketMatching(), closeBrackets(),
    autocompletion({ override: [complete], icons: false }), rectangularSelection(), crosshairCursor(),
    highlightActiveLine(), highlightSelectionMatches(), search({ top: true }),
    EditorState.tabSize.of(2), indentUnit.of('  '),
    language.of(lang), syntaxHighlighting(highlight), theme, problems, lintGutter(), runtimeError,
    keymap.of([
      { key: 'F12', run: view => goToDefinition(view, view.state.selection.main.head) },
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
    /** A program's editor state: its text, undo history, cursor. */
    create(text, cursor = 0) {
      return EditorState.create({ doc: text, selection: { anchor: Math.min(cursor, text.length) }, extensions: extensions() });
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
