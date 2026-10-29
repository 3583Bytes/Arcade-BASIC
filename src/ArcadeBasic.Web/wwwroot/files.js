// The files programs write — OPEN #1: NAME "todo.board", ACCESS OUTPUT — live in the
// page's memory (Playground.Files.cs), which a reload wipes. So the page keeps a copy in
// the browser and puts it back when it starts. File → Program files… shows them: download
// one to keep it elsewhere, upload one for a program to read, or delete one.

const KEY = 'arcade-basic.files';

export function createFiles({ store, ask }) {
  const dialog = document.getElementById('filesDialog');
  const table = dialog.querySelector('.fileList');
  const empty = dialog.querySelector('.empty');
  const picker = Object.assign(document.createElement('input'), { type: 'file', multiple: true });
  let files = read();   // name → text
  let P = null;
  let lastSync = 0;

  function read() {
    try {
      const saved = JSON.parse(store.get(KEY) ?? '{}');
      return saved && typeof saved === 'object' ? saved : {};
    } catch { return {}; }
  }
  const write = () => store.set(KEY, JSON.stringify(files));

  /** .NET is ready: put the files back where programs look for them. */
  function attach(playground) {
    P = playground;
    const names = Object.keys(files);
    P.PutFiles(names, names.map(n => files[n]));
  }

  /** Keep what the running program has written: at most twice a second, unless `now`. */
  function sync(now = false) {
    if (!P || (!now && performance.now() - lastSync < 500)) return;
    lastSync = performance.now();
    const changed = P.ChangedFiles();
    if (!changed.length) return;
    for (let i = 0; i < changed.length; i += 2) files[changed[i]] = changed[i + 1];
    write();
    if (dialog.open) list();
  }
  // Leaving the page: whatever the program wrote last.
  addEventListener('pagehide', () => sync(true));
  document.addEventListener('visibilitychange', () => { if (document.hidden) sync(true); });

  function download(text, name) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type: 'text/plain' }));
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 10_000);
  }

  function size(text) {
    const bytes = new TextEncoder().encode(text).length;
    return bytes < 1024 ? `${bytes} bytes` : `${(bytes / 1024).toFixed(1)} KB`;
  }

  function list() {
    const names = Object.keys(files).sort((a, b) => a.localeCompare(b));
    empty.hidden = names.length > 0;
    table.replaceChildren(...names.map(name => {
      const row = document.createElement('tr');
      const cell = (content, className) => {
        const td = document.createElement('td');
        if (className) td.className = className;
        td.append(...[content].flat());
        row.append(td);
      };
      const button = (label, onClick, kind = '') => {
        const b = Object.assign(document.createElement('button'), { type: 'button', textContent: label, className: kind });
        b.addEventListener('click', onClick);
        return b;
      };
      cell(name, 'name');
      cell(size(files[name]), 'size');
      cell([button('Download', () => download(files[name], name.split('/').pop())),
            button('Delete', () => remove(name), 'danger')], 'actions');
      return row;
    }));
  }

  async function remove(name) {
    const answer = await ask({
      title: `Delete ${name}?`,
      message: 'A program that reads it will find it missing. Download it first to keep a copy.',
      buttons: [{ label: 'Cancel' }, { label: 'Delete', value: 'delete', kind: 'danger' }],
    });
    if (answer !== 'delete') return;
    delete files[name];
    write();
    P?.DeleteFile(name);
    list();
  }

  picker.addEventListener('change', async () => {
    for (const file of picker.files) {
      const text = await file.text();
      files[file.name] = text;
      P?.PutFiles([file.name], [text]);
    }
    picker.value = '';
    write();
    list();
  });
  dialog.querySelector('.upload').addEventListener('click', () => picker.click());

  function open() {
    sync(true);
    list();
    dialog.showModal();
  }

  /** Forget a file without asking (the self-tests start from a clean slate). */
  function forget(name) {
    delete files[name];
    write();
    P?.DeleteFile(name);
  }

  return { attach, sync, open, forget, get names() { return Object.keys(files); }, text: name => files[name] };
}
