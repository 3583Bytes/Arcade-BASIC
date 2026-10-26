// The parts of CodeMirror 6 the playground's editor (wwwroot/editor.js) uses,
// bundled into one module: wwwroot/lib/codemirror.js.
export { EditorState, Compartment, StateEffect, StateField, RangeSet, RangeSetBuilder } from '@codemirror/state';
export {
  EditorView, Decoration, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter,
  highlightSpecialChars, drawSelection, dropCursor, rectangularSelection, crosshairCursor,
  gutter, GutterMarker, hoverTooltip, WidgetType, ViewPlugin,
} from '@codemirror/view';
export { defaultKeymap, history, historyKeymap, indentWithTab, toggleComment } from '@codemirror/commands';
export {
  StreamLanguage, HighlightStyle, syntaxHighlighting, syntaxTree, indentOnInput, bracketMatching, indentUnit,
} from '@codemirror/language';
export { tags } from '@lezer/highlight';
export { search, searchKeymap, highlightSelectionMatches, openSearchPanel } from '@codemirror/search';
export {
  autocompletion, completionKeymap, closeBrackets, closeBracketsKeymap, completeAnyWord,
} from '@codemirror/autocomplete';
export { linter, lintGutter, lintKeymap, openLintPanel, closeLintPanel, nextDiagnostic, forceLinting } from '@codemirror/lint';
