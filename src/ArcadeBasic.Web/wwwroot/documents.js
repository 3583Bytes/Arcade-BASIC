// The programs open in the editor, one tab each, and getting them in and out of
// files. Each remembers where it came from (an example, a file, a new program, a
// shared link) and its text as last saved, so its tab shows ● while it has changes.
//
// Everything open is kept in this browser as you type (localStorage), so a reload
// brings it all back. In browsers with the File System Access API (Chrome, Edge) a
// file you open or save stays linked to the file on disk (IndexedDB keeps the
// link across reloads) and Save writes straight back to it; elsewhere Open is a
// file picker and Save downloads the file.

const SESSION = 'arcade-basic.session';
const TYPES = [{ description: 'BASIC programs', accept: { 'text/plain': ['.bas', '.txt'] } }];
const canPick = 'showOpenFilePicker' in window && 'showSaveFilePicker' in window;
const STARTER = '! A new program\nPRINT "Hello, world!"\nEND\n';

const newId = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 8);

/**
 * editor: from createEditor. tabs: the tab strip element. ask(question): a dialog
 * (main.js). exampleSource(name): an example's text. onShow(doc): a different program
 * is showing, or the one showing changed name or saved state. notify(text): a short
 * message for the status line.
 */
export function createDocuments({ editor, tabs, ask, exampleSource, onShow, notify }) {
  const docs = [];
  let active = null, running = null;

  function make({ id = newId(), name, kind, text, saved = text, crlf = false, handle = null, cursor = 0, breakpoints = [] }) {
    return { id, name, kind, state: editor.create(text, cursor, breakpoints), saved, crlf, handle, dirty: text !== saved };
  }

  /** A new tab, shown — or, with showIt false, opened behind the one showing (a library). */
  function add(doc, showIt = true) {
    if (!showIt) {
      docs.push(doc);
      render();
      persist();
      return doc;
    }
    // An untouched new program makes way for the first thing opened after it.
    const replace = active && active.kind === 'new' && !active.dirty && active.saved === STARTER ? active : null;
    docs.push(doc);
    show(doc);
    if (replace) remove(replace);
    return doc;
  }

  function show(doc) {
    active = doc;
    editor.show(doc.state);
    render();
    onShow?.(doc);
    persist();
  }

  function remove(doc) {
    docs.splice(docs.indexOf(doc), 1);
    forgetHandle(doc.id);
    render();
    persist();
  }

  /** Every editor change: keep the program's state and whether it has changes. */
  function update(u) {
    if (!active) return;
    active.state = u.state;
    if (u.docChanged) {
      const dirty = u.state.doc.toString() !== active.saved;
      if (dirty !== active.dirty) {
        active.dirty = dirty;
        render();
        onShow?.(active);
      }
    }
    if (u.docChanged || u.selectionSet) persistSoon();
  }

  // -- The tab strip -----------------------------------------------------------

  function render() {
    const items = docs.map(doc => {
      const tab = document.createElement('div');
      tab.className = 'tab' + (doc === active ? ' active' : '') + (doc.dirty ? ' dirty' : '');
      tab.title = describe(doc);
      tab.setAttribute('role', 'tab');
      tab.setAttribute('aria-selected', String(doc === active));
      const mark = document.createElement('span');
      mark.className = 'mark';
      mark.textContent = doc === running ? '▶' : '';
      const name = document.createElement('span');
      name.textContent = doc.name;
      const close = document.createElement('button');
      close.className = 'close';
      close.type = 'button';
      close.title = 'Close';
      close.setAttribute('aria-label', `Close ${doc.name}`);
      close.innerHTML = '<span>×</span>';
      close.addEventListener('click', e => { e.stopPropagation(); closeDoc(doc); });
      tab.addEventListener('click', () => { if (doc !== active) show(doc); editor.focus(); });
      tab.addEventListener('auxclick', e => { if (e.button === 1) closeDoc(doc); });
      tab.append(mark, name, close);
      return tab;
    });
    const plus = document.createElement('button');
    plus.id = 'newTab';
    plus.type = 'button';
    plus.title = 'New program (Alt+N)';
    plus.textContent = '+';
    plus.addEventListener('click', () => newProgram());
    tabs.replaceChildren(...items, plus);
    tabs.querySelector('.tab.active')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }

  /** Where a program lives, for its tab's tooltip and the status line. */
  function describe(doc) {
    switch (doc.kind) {
      case 'example': return doc.dirty ? 'An example you\'ve changed: kept in this browser until you save it to a file' : 'An example';
      case 'shared': return 'From a shared link: kept in this browser until you save it to a file';
      case 'new': return 'A new program: kept in this browser until you save it to a file';
      default:
        if (doc.handle) return doc.dirty ? `Changes not saved to the file ${doc.name} yet` : `Saved in the file ${doc.name}`;
        return doc.dirty ? 'Changes kept in this browser; Save downloads the file' : `From the file ${doc.name}`;
    }
  }

  // -- Opening -----------------------------------------------------------------

  function uniqueName(name) {
    if (!docs.some(d => d.name === name)) return name;
    const [, stem, ext] = /^(.*?)(\.[^.]*)?$/.exec(name);
    for (let n = 2; ; n++) if (!docs.some(d => d.name === `${stem}-${n}${ext ?? ''}`)) return `${stem}-${n}${ext ?? ''}`;
  }

  function newProgram() {
    const doc = make({ name: uniqueName('untitled.bas'), kind: 'new', text: STARTER, cursor: STARTER.indexOf('\n') + 1 });
    docs.push(doc);
    show(doc);
    editor.focus();
    return doc;
  }

  /** An example: its tab if it's open, else a new one. */
  function openExample(name, showIt = true) {
    const open = docs.find(d => d.kind === 'example' && d.name === name);
    if (open) { if (showIt && open !== active) show(open); return open; }
    return add(make({ name, kind: 'example', text: exampleSource(name) }), showIt);
  }

  /** A program from a shared link (named, if it came with its libraries). */
  function openShared(text, name = 'shared.bas', showIt = true) {
    const open = docs.find(d => d.kind === 'shared' && d.state.doc.toString() === text);
    if (open) { if (showIt && open !== active) show(open); return open; }
    return add(make({ name: docs.some(d => d.name === name) ? uniqueName(name) : name, kind: 'shared', text }), showIt);
  }

  /** Every .bas file in a folder, each in a tab — a program and its libraries. In Chrome and
   *  Edge they stay linked to their files; elsewhere the browser asks for the folder's files. */
  async function openFolder() {
    if (!('showDirectoryPicker' in window)) {
      const input = document.createElement('input');
      input.type = 'file';
      input.webkitdirectory = true;
      input.addEventListener('change', async () => {
        await openSeveral([...input.files].filter(f => /\.bas$/i.test(f.name)).map(file => ({ file, handle: null })));
      });
      input.click();
      return;
    }
    let folder;
    try { folder = await window.showDirectoryPicker({ mode: 'readwrite' }); } catch (e) {
      if (e.name !== 'AbortError') notify(`Couldn't open the folder: ${e.message}`);
      return;
    }
    const files = [];
    for await (const handle of folder.values())
      if (handle.kind === 'file' && /\.bas$/i.test(handle.name)) files.push({ file: await handle.getFile(), handle });
    if (!files.length) { notify(`No .bas files in ${folder.name}`); return; }
    await openSeveral(files);
  }

  /** Several files at once: all in tabs, showing the program (one that isn't a library). */
  async function openSeveral(files) {
    files.sort((a, b) => a.file.name.localeCompare(b.file.name));
    const opened = [];
    for (const { file, handle } of files) opened.push(await openFile(file, handle, false));
    const program = opened.find(d => d && !/@library/i.test(d.state.doc.line(1).text)) ?? opened.find(Boolean);
    if (program) show(program);
  }

  async function openFiles() {
    if (!canPick) { pickWithInput(); return; }
    let handles;
    try {
      handles = await window.showOpenFilePicker({ types: TYPES, multiple: true });
    } catch (e) {
      if (e.name !== 'AbortError') notify(`Couldn't open: ${e.message}`);
      return;
    }
    for (const handle of handles) await openFile(await handle.getFile(), handle);
  }

  // Browsers without the File System Access API: a plain file input.
  let input = null;
  function pickWithInput() {
    if (!input) {
      input = document.createElement('input');
      input.type = 'file';
      input.accept = '.bas,.txt,text/plain';
      input.multiple = true;
      input.addEventListener('change', async () => {
        for (const file of input.files) await openFile(file);
        input.value = '';
      });
    }
    input.click();
  }

  /** A file (and its handle, where the browser gives one) in a tab. A file that's
   *  already open just gets shown. Line endings are kept as the file has them. */
  async function openFile(file, handle = null, showIt = true) {
    if (handle) {
      for (const d of docs) {
        if (d.handle && await d.handle.isSameEntry(handle).catch(() => false)) { if (showIt) show(d); return d; }
      }
    }
    const raw = await file.text();
    if (raw.includes('\0')) { notify(`${file.name} isn't a text file`); return null; }
    const text = raw.replace(/\r\n?/g, '\n');
    const doc = add(make({ name: file.name, kind: 'file', text, crlf: raw.includes('\r\n'), handle }), showIt);
    keepHandle(doc);
    return doc;
  }

  /** Files dropped on the page. (The items have to be read before anything waits.) */
  async function openDropped(dataTransfer) {
    const items = [...dataTransfer.items].filter(i => i.kind === 'file');
    const dropped = items.map(i => ({ file: i.getAsFile(), handle: canPick ? i.getAsFileSystemHandle?.() : null }));
    for (const { file, handle } of dropped) {
      const h = await handle?.catch(() => null);
      if (h && h.kind !== 'file') continue;
      if (file) await openFile(file, h ?? null);
    }
  }

  // -- Saving ------------------------------------------------------------------

  /** Save the program: to its file if it has one, else ask where (Save as). */
  async function save(doc = active, saveAs = false) {
    if (!doc) return false;
    const text = (doc === active ? editor.state : doc.state).doc.toString();
    const data = doc.crlf ? text.replace(/\n/g, '\r\n') : text;
    try {
      if (!saveAs && doc.handle) {
        await write(doc.handle, data);
      } else if (canPick) {
        const handle = await window.showSaveFilePicker({ suggestedName: doc.name, types: TYPES });
        await write(handle, data);
        doc.handle = handle;
        doc.name = handle.name;
      } else {
        let name = doc.name;
        if (saveAs || doc.kind !== 'file') {
          name = await ask({
            title: 'Save as', message: 'Your browser saves the file in your downloads.', input: doc.name,
            buttons: [{ label: 'Cancel' }, { label: 'Save', value: 'save', kind: 'primary' }],
          });
          if (!name) return false;
          if (!/\.[a-z0-9]+$/i.test(name)) name += '.bas';
        }
        download(data, name);
        doc.name = name;
      }
    } catch (e) {
      if (e.name !== 'AbortError') notify(`Couldn't save: ${e.message}`);
      return false;
    }
    doc.kind = 'file';
    doc.saved = text;
    doc.dirty = (doc === active ? editor.state : doc.state).doc.toString() !== text;   // typed while saving?
    keepHandle(doc);
    render();
    onShow?.(doc);
    persist();
    notify(doc.handle ? `Saved ${doc.name}` : `Downloaded ${doc.name}`);
    return true;
  }

  async function write(handle, data) {
    const mode = { mode: 'readwrite' };
    if (handle.queryPermission && await handle.queryPermission(mode) !== 'granted' &&
        await handle.requestPermission(mode) !== 'granted')
      throw new Error('the browser wasn\'t allowed to write the file');
    const writable = await handle.createWritable();
    await writable.write(data);
    await writable.close();
  }

  function download(data, name) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([data], { type: 'text/plain' }));
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 10_000);
  }

  // -- Closing -----------------------------------------------------------------

  async function closeDoc(doc = active) {
    if (!doc) return;
    if (doc.dirty) {
      const choice = await ask({
        title: `Close ${doc.name}?`,
        message: 'Its changes haven\'t been saved to a file.',
        buttons: [{ label: 'Cancel' }, { label: 'Close without saving', value: 'close', kind: 'danger' }, { label: 'Save', value: 'save', kind: 'primary' }],
      });
      if (choice === 'save' && !(await save(doc))) return;
      if (!choice) return;
    }
    const i = docs.indexOf(doc);
    if (i < 0) return;
    if (doc === running) running = null;
    if (doc === active) {
      active = null;
      docs.splice(i, 1);
      forgetHandle(doc.id);
      if (docs.length) show(docs[Math.min(i, docs.length - 1)]);
      else newProgram();
    } else {
      remove(doc);
    }
  }

  // -- Kept in this browser ----------------------------------------------------

  let persistTimer = 0;
  function persistSoon() {
    clearTimeout(persistTimer);
    persistTimer = setTimeout(persist, 400);
  }

  function persist() {
    clearTimeout(persistTimer);
    const session = {
      active: active?.id,
      docs: docs.map(d => {
        const text = d.state.doc.toString();
        return { id: d.id, name: d.name, kind: d.kind, text, saved: d.saved === text ? null : d.saved, crlf: d.crlf,
                 cursor: d.state.selection.main.head, breakpoints: editor.breakpointLines(d.state) };
      }),
    };
    try {
      localStorage.setItem(SESSION, JSON.stringify(session));
    } catch {
      /* storage full or blocked: everything still works, it just won't survive a reload */
    }
  }

  /** Bring back what was open last time; false if nothing was. */
  function restore() {
    let session = null;
    try { session = JSON.parse(localStorage.getItem(SESSION)); } catch { /* none */ }
    if (!session?.docs?.length) return false;
    for (const s of session.docs) docs.push(make({ ...s, saved: s.saved ?? s.text }));
    show(docs.find(d => d.id === session.active) ?? docs[0]);
    loadHandles();
    return true;
  }

  // File handles, by program id, in IndexedDB (they can't go in localStorage).
  function handles(mode, request) {
    return new Promise((resolve, reject) => {
      const open = indexedDB.open('arcade-basic', 1);
      open.onupgradeneeded = () => open.result.createObjectStore('handles');
      open.onerror = () => reject(open.error);
      open.onsuccess = () => {
        const tx = open.result.transaction('handles', mode);
        const req = request(tx.objectStore('handles'));
        tx.oncomplete = () => { open.result.close(); resolve(req.result); };
        tx.onerror = () => { open.result.close(); reject(tx.error); };
      };
    });
  }
  function keepHandle(doc) {
    if (canPick && doc.handle) handles('readwrite', s => s.put(doc.handle, doc.id)).catch(() => {});
  }
  function forgetHandle(id) {
    if (canPick) handles('readwrite', s => s.delete(id)).catch(() => {});
  }
  async function loadHandles() {
    if (!canPick) return;
    for (const d of docs) {
      if (d.kind === 'file' && !d.handle) d.handle = await handles('readonly', s => s.get(d.id)).catch(() => null) ?? null;
    }
    render();
    if (active) onShow?.(active);
  }

  /** Refresh every program's editor state (when the engine's words arrive). */
  function refresh(update) {
    for (const d of docs) if (d !== active) d.state = update(d.state);
    if (active) active.state = editor.state;
  }

  window.addEventListener('pagehide', persist);

  return {
    get active() { return active; },
    get all() { return docs; },
    get canPick() { return canPick; },
    describe, update, refresh, restore, persist, show,
    newProgram, openExample, openShared, openFiles, openFolder, openFile, openDropped, save, close: closeDoc,
    setRunning(doc) { running = doc; render(); },
  };
}
