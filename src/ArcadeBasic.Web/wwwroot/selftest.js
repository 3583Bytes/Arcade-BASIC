// ?selftest=ide: use the editor the way a person would and post what happened to
// /results (for the local test server). Headless Firefox has no File System Access
// API, so this covers Open and Save's fallbacks: a downloaded file, here caught
// before it reaches the disk.
export async function ideSelftest({ editor, documents, pack, openAddress, $ }) {
  const pause = (ms = 300) => new Promise(r => setTimeout(r, ms));
  const view = editor.view;
  const result = {};
  const step = async (name, fn) => {
    try { result[name] = await fn(); } catch (e) { result[name] = { error: String(e), stack: e.stack }; }
  };
  const setText = text => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text }, selection: { anchor: text.length } });
  const type = text => view.dispatch({ ...view.state.replaceSelection(text), userEvent: 'input.type' });
  const lineAtCursor = () => view.state.doc.lineAt(view.state.selection.main.head).text;
  const dialog = $('dialog');
  async function answer(value, text) {               // answer the next question asked
    for (let i = 0; i < 100 && !dialog.open; i++) await pause(20);
    if (!dialog.open) throw new Error('no question was asked');
    const asked = dialog.querySelector('h2').textContent;
    if (text != null) dialog.querySelector('input').value = text;
    dialog.close(value);
    await pause(50);
    return asked;
  }
  const downloads = [];
  const click = HTMLAnchorElement.prototype.click;
  HTMLAnchorElement.prototype.click = function () {
    if (this.download) downloads.push({ name: this.download, href: this.href }); else click.call(this);
  };
  const downloaded = async d => (await fetch(d.href)).text();

  await step('startup', () => ({ tabs: documents.all.map(d => d.name), active: documents.active?.name, title: document.title, hash: location.hash }));

  await step('highlighting', async () => {
    documents.newProgram();
    setText('10 LET X = INT(RND * 6) ! a die\n20 IF X > 3 THEN 10\n30 PRINT "X is"; X\nREM done\n');
    await pause(300);
    const spans = [...view.contentDOM.querySelectorAll('.cm-line span')];
    const colourOf = word => { const s = spans.find(x => x.textContent === word); return s && getComputedStyle(s).color; };
    const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return {
      spans: spans.length, colours: new Set(spans.map(s => getComputedStyle(s).color)).size,
      LET: colourOf('LET'), keyword: css('--syn-kw'), INT: colourOf('INT'), builtin: css('--syn-fn'),
      jumpTo: colourOf('10'), label: css('--syn-label'), comment: colourOf('! a die'), commentColour: css('--syn-com'),
      string: colourOf('"X is"'), stringColour: css('--syn-str'),
    };
  });

  await step('themes', () => {
    const select = $('theme'), out = {};
    for (const option of select.options) {
      select.value = option.value;
      select.dispatchEvent(new Event('change'));
      out[option.value] = { editor: getComputedStyle(view.dom).backgroundColor, page: getComputedStyle(document.body).backgroundColor };
    }
    out.saved = localStorage.getItem('arcade-basic.theme');
    select.value = 'midnight';
    select.dispatchEvent(new Event('change'));
    return out;
  });

  await step('errorAsYouType', async () => {
    documents.newProgram();
    setText('PRINT (1\n');
    await pause(800);
    return { underlined: [...document.querySelectorAll('.cm-lintRange-error')].map(e => e.textContent), status: $('problems').textContent };
  });

  await step('warningAsYouType', async () => {
    setText('PRINT SCORE\nEND\n');
    await pause(800);
    return { underlined: document.querySelectorAll('.cm-lintRange-warning').length, status: $('problems').textContent };
  });

  await step('problemsPanel', async () => {
    $('problems').click();
    await pause(100);
    return { open: !!document.querySelector('.cm-panel-lint'), items: [...document.querySelectorAll('.cm-panel-lint li')].map(li => li.textContent) };
  });

  await step('runtimeError', async () => {
    setText('10 PRINT "hi"\n20 LET A = 1 / 0\n30 END\n');
    $('run').click();
    await pause(600);
    return { marked: document.querySelector('.cm-runtimeError')?.textContent, cursorLine: lineAtCursor(), output: $('output').textContent.trim() };
  });

  await step('compileErrorOnRun', async () => {
    setText('PRINT "fine"\nPRINT (1\n');
    $('run').click();
    await pause(400);
    return { output: $('output').textContent.split('\n')[0], cursorLine: lineAtCursor() };
  });

  await step('completion', async () => {
    setText('LET SCORE = 1\n');
    type('PRI');
    await pause(400);
    const keywords = [...document.querySelectorAll('.cm-tooltip-autocomplete li')].map(li => li.textContent).slice(0, 4);
    setText('LET SCORE = 1\nPRINT S');
    type('C');
    await pause(400);
    const names = [...document.querySelectorAll('.cm-tooltip-autocomplete li')].map(li => li.textContent).slice(0, 4);
    setText('PRINT "PRI');
    type('N');
    await pause(400);
    return { keywords, names, insideString: document.querySelectorAll('.cm-tooltip-autocomplete li').length };
  });

  await step('goToDefinition', async () => {
    documents.openExample('maze3d.bas');
    const text = view.state.doc.toString(), at = text.indexOf('GOSUB 2000') + 7;
    view.dispatch({ selection: { anchor: at } });
    view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'F12', code: 'F12', bubbles: true, cancelable: true }));
    const lineNumber = lineAtCursor();
    documents.openExample('kanban.bas');
    const call = view.state.doc.toString().indexOf('& LANENAME$(') + 4;
    view.dispatch({ selection: { anchor: call } });
    view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'F12', code: 'F12', bubbles: true, cancelable: true }));
    return { lineNumber, func: lineAtCursor() };
  });

  await step('examplesMenu', async () => {
    const select = $('examples');
    select.value = 'snake.bas';
    select.dispatchEvent(new Event('change'));
    await pause(50);
    return { active: documents.active.name, hash: location.hash, groups: [...select.querySelectorAll('optgroup')].map(g => `${g.label} ${g.children.length}`) };
  });

  await step('closeWithChanges', async () => {
    const doc = documents.newProgram();
    type('PRINT 2\n');
    const dirty = doc.dirty;
    const closing = documents.close(doc);
    const asked = await answer('');                 // Cancel
    await closing;
    const keptAfterCancel = documents.all.includes(doc);
    const again = documents.close(doc);
    await answer('close');
    await again;
    return { dirty, asked, keptAfterCancel, closedAfterClose: !documents.all.includes(doc) };
  });

  await step('saveDownloads', async () => {
    const doc = documents.newProgram();
    setText('PRINT "mine"\n');
    const saving = documents.save();
    const asked = await answer('save', 'mine');
    const saved = await saving;
    const file = downloads.at(-1);
    return { asked, saved, name: file?.name, text: file && await downloaded(file), tab: doc.name, kind: doc.kind, dirty: doc.dirty, title: document.title };
  });

  await step('crlfKept', async () => {
    const doc = await documents.openFile(new File(['10 PRINT 1\r\n20 END\r\n'], 'windows.bas'));
    const inEditor = view.state.doc.toString();
    view.dispatch({ changes: { from: view.state.doc.length, insert: '30 REM edited\n' } });
    const dirty = doc.dirty;
    const before = downloads.length;
    await documents.save();
    const file = downloads[before];
    return { inEditorHasCR: inEditor.includes('\r'), dirty, name: file?.name, saved: JSON.stringify(file && await downloaded(file)), dirtyAfter: doc.dirty };
  });

  await step('sharedLink', async () => {
    const text = 'PRINT "from a link"\nEND\n';
    const code = await pack(text);
    await openAddress('#code=' + code);
    return { codeLength: code.length, active: documents.active.name, same: view.state.doc.toString() === text };
  });

  await step('keptInBrowser', async () => {
    documents.persist();
    const session = JSON.parse(localStorage.getItem('arcade-basic.session'));
    return { tabs: session.docs.map(d => `${d.name}${d.saved != null ? ' (changed)' : ''}`), active: session.docs.find(d => d.id === session.active)?.name };
  });

  HTMLAnchorElement.prototype.click = click;
  await fetch('/results', { method: 'POST', body: JSON.stringify(result, null, 2) });
}
