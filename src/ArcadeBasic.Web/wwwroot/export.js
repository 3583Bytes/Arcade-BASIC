// File → Export…: the program as something to give people — compiled, without its
// source. For now a web page: the player (src/ArcadeBasic.Player, published in this
// site's player/ folder, whose files.json lists its files) with the program as game.bin,
// zipped, to put on itch.io or any website.

import { zip } from './zip.js';

/** The Export dialog. `programName()` names the program showing (its file); `compile()`
 *  compiles it: { name, bytecode (Uint8Array) }, or { problems } — then
 *  `onProblems(result)` shows them. `onExported(fileName, bytes)` hears of each export. */
export function createExport({ dialog, programName, compile, onProblems, onExported }) {
  const form = dialog.querySelector('form');
  const nameInput = form.elements.name;
  const status = dialog.querySelector('.status');
  const after = dialog.querySelector('.after');
  const exportButton = dialog.querySelector('[value="export"]');
  let playerFiles = null;   // the player's files, once fetched: [path]

  /** A file name for `name`: no characters a file system refuses. */
  const fileName = name => name.replace(/[\\/:*?"<>|\x00-\x1f]/g, '').replace(/^[.\s]+|[.\s]+$/g, '') || 'program';

  async function playerList() {
    if (playerFiles) return playerFiles;
    const response = await fetch('player/files.json', { cache: 'no-cache' }).catch(() => null);
    if (!response?.ok) return null;
    return playerFiles = await response.json();
  }

  async function open() {
    const program = programName();
    nameInput.value = program.replace(/\.bas$/i, '');
    dialog.querySelector('h2').textContent = `Export ${program}`;
    status.textContent = '';
    after.hidden = true;
    exportButton.hidden = false;
    exportButton.disabled = false;
    dialog.showModal();
    nameInput.select();
    if (!(await playerList())) {
      status.textContent = 'The player isn\'t on this site, so there\'s nothing to export with. (Build the site with src/ArcadeBasic.Web/build-site.sh.)';
      exportButton.disabled = true;
    }
  }

  async function exportIt() {
    const result = compile();
    if (result.problems) {
      dialog.close();
      onProblems(result);
      return;
    }
    const title = nameInput.value.trim() || result.name.replace(/\.bas$/i, '');
    const name = fileName(title);
    exportButton.disabled = true;
    status.textContent = 'Packing…';
    try {
      const files = await Promise.all((await playerList()).map(async path => {
        const response = await fetch(`player/${path}`);
        if (!response.ok) throw new Error(`the player's ${path} is missing`);
        let data = new Uint8Array(await response.arrayBuffer());
        if (path === 'index.html') data = withTitle(data, title);
        return { name: path, data };
      }));
      files.push({ name: 'game.bin', data: result.bytecode });
      const blob = await zip(files);
      download(blob, `${name}.zip`);
      onExported(`${name}.zip`, blob.size);
      status.textContent = '';
      after.querySelector('.file').textContent = `${name}.zip (${megabytes(blob.size)})`;
      after.hidden = false;
      exportButton.hidden = true;
    } catch (e) {
      status.textContent = `Couldn't export: ${e.message}`;
    } finally {
      exportButton.disabled = false;
    }
  }

  /** The player's page, titled `title` (the player keeps a title it's given). */
  function withTitle(page, title) {
    const escaped = title.replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const html = new TextDecoder().decode(page).replace(/<title>[^<]*<\/title>/, `<title>${escaped}</title>`);
    return new TextEncoder().encode(html);
  }

  const megabytes = bytes => `${(bytes / 1048576).toFixed(1)} MB`;

  function download(blob, name) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 60_000);
  }

  // Export is the form's button, so Enter in the name exports too; once it's done, Enter closes.
  form.addEventListener('submit', e => {
    e.preventDefault();
    if (!exportButton.hidden) { if (!exportButton.disabled) exportIt(); }
    else dialog.close();
  });
  dialog.querySelector('.close').addEventListener('click', () => dialog.close());

  return { open };
}
