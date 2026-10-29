// File → Export…: the program as something to give people — compiled, without its
// source — zipped in the browser:
//   - a Mac, Windows or Linux app: the desktop player (desktop/player; this site carries one
//     per OS in desktop/, which players.json lists) with the program — in a Mac app's
//     Resources, the app signed ad hoc (macsign.js); appended to the Windows and Linux
//     player as `arcade-basic build` appends one ([program][u32 length]["FB-BCEND"]);
//   - a web page: the player (src/ArcadeBasic.Player, in player/, which files.json lists)
//     with the program as game.bin, for itch.io or any website.

import { zip } from './zip.js';
import { signApp } from './macsign.js';

const KINDS = {
  macos: { label: 'Mac app', suffix: 'Mac' },
  windows: { label: 'Windows app', suffix: 'Windows', about: 'For Windows 10 and 11.' },
  linux: { label: 'Linux app', suffix: 'Linux', about: 'For Linux desktops with WebKitGTK, which most have.' },
  web: { label: 'Web page', suffix: 'web', about: 'To put on itch.io or any website. It plays in every browser, on any computer.' },
};

/** The OS this browser is on: macos, windows or linux. */
function ownOs() {
  const platform = navigator.userAgentData?.platform ?? navigator.platform ?? '';
  return /mac/i.test(platform) ? 'macos' : /win/i.test(platform) ? 'windows' : /linux/i.test(platform) ? 'linux' : '';
}

/** The Export dialog. `programName()` names the program showing (its file); `compile()`
 *  compiles it: { name, bytecode (Uint8Array) }, or { problems } — then
 *  `onProblems(result)` shows them. `onExported(fileName, bytes)` hears of each export. */
export function createExport({ dialog, programName, compile, onProblems, onExported }) {
  const form = dialog.querySelector('form');
  const nameInput = form.elements.name;
  const kinds = dialog.querySelector('.kinds');
  const status = dialog.querySelector('.status');
  const after = dialog.querySelector('.after');
  const exportButton = dialog.querySelector('[value="export"]');
  let offered = null;   // what this site can export: { page: [path] | null, players: { os: { file, size, universal } } }

  const fetchJson = async path => {
    const response = await fetch(path, { cache: 'no-cache' }).catch(() => null);
    return response?.ok ? response.json() : null;
  };
  const fetchBytes = async path => {
    const response = await fetch(path);
    if (!response.ok) throw new Error(`${path} is missing from this site`);
    return new Uint8Array(await response.arrayBuffer());
  };

  async function whatsOffered() {
    if (offered) return offered;
    const [page, desktop] = await Promise.all([fetchJson('player/files.json'), fetchJson('desktop/players.json')]);
    return offered = { page, players: Object.fromEntries((desktop?.players ?? []).map(p => [p.os, p])) };
  }

  /** A file name for `name`: no characters a file system refuses. */
  const fileName = name => name.replace(/[\\/:*?"<>|\x00-\x1f]/g, '').replace(/^[.\s]+|[.\s]+$/g, '') || 'program';
  const megabytes = bytes => `${(bytes / 1048576).toFixed(1)} MB`;

  function listKinds() {
    const { page, players } = offered;
    const available = Object.keys(KINDS).filter(kind => kind === 'web' ? page : players[kind]);
    const chosen = available.includes(ownOs()) ? ownOs() : available.includes('web') ? 'web' : available[0];
    kinds.replaceChildren(...available.map(kind => {
      const about = kind === 'macos'
        ? `For Macs with Apple Silicon${players.macos.universal ? ' or Intel' : ''}.`
        : KINDS[kind].about;
      const size = players[kind] ? ` · about ${megabytes(players[kind].size)}` : '';
      const label = document.createElement('label');
      label.className = 'kind';
      label.innerHTML = `<input type="radio" name="kind" value="${kind}"><span><b></b><small></small></span>`;
      label.querySelector('input').checked = kind === chosen;
      label.querySelector('b').textContent = KINDS[kind].label;
      label.querySelector('small').textContent = about + size;
      return label;
    }));
    return available.length > 0;
  }

  async function open() {
    const program = programName();
    nameInput.value = program.replace(/\.bas$/i, '');
    dialog.querySelector('h2').textContent = `Export ${program}`;
    status.textContent = '';
    after.hidden = true;
    kinds.hidden = false;
    exportButton.hidden = false;
    exportButton.disabled = true;
    kinds.replaceChildren();
    dialog.showModal();
    nameInput.select();
    await whatsOffered();
    if (listKinds()) exportButton.disabled = false;
    else status.textContent = 'This site has no players to export with. (Build it with src/ArcadeBasic.Web/build-site.sh.)';
  }

  async function exportIt() {
    const kind = form.elements.kind?.value;
    if (!kind) return;
    const result = compile();
    if (result.problems) {
      dialog.close();
      onProblems(result);
      return;
    }
    const title = nameInput.value.trim() || result.name.replace(/\.bas$/i, '');
    const name = fileName(title);
    exportButton.disabled = true;
    status.textContent = kind === 'macos' ? 'Packing and signing…' : 'Packing…';
    try {
      const files = kind === 'web' ? await webPage(title, result.bytecode)
        : kind === 'macos' ? await macApp(name, result.bytecode)
        : [{ name: kind === 'windows' ? `${name}.exe` : name, mode: 0o755,
             data: withProgram(await fetchBytes(`desktop/${offered.players[kind].file}`), result.bytecode) }];
      const blob = await zip(files);
      const zipName = `${name} (${KINDS[kind].suffix}).zip`;
      download(blob, zipName);
      onExported(zipName, blob.size);
      status.textContent = '';
      after.querySelector('.file').textContent = `${zipName} (${megabytes(blob.size)})`;
      for (const what of after.querySelectorAll('[data-kind]')) what.hidden = what.dataset.kind !== kind;
      for (const app of after.querySelectorAll('.app')) app.textContent = kind === 'macos' ? `${name}.app` : kind === 'windows' ? `${name}.exe` : name;
      after.hidden = false;
      kinds.hidden = true;
      exportButton.hidden = true;
    } catch (e) {
      status.textContent = `Couldn't export: ${e.message}`;
    } finally {
      exportButton.disabled = false;
    }
  }

  /** The web page: the player's files, its page titled `title`, and the program. */
  async function webPage(title, bytecode) {
    const files = await Promise.all(offered.page.map(async path => {
      let data = await fetchBytes(`player/${path}`);
      if (path === 'index.html') {
        const escaped = title.replace(/&/g, '&amp;').replace(/</g, '&lt;');
        data = new TextEncoder().encode(new TextDecoder().decode(data).replace(/<title>[^<]*<\/title>/, `<title>${escaped}</title>`));
      }
      return { name: path, data };
    }));
    return [...files, { name: 'game.bin', data: bytecode }];
  }

  /** The Mac app: name.app, the program in its Resources, signed. */
  async function macApp(name, bytecode) {
    const [player, template] = await Promise.all([
      fetchBytes(`desktop/${offered.players.macos.file}`), fetchBytes('desktop/app-Info.plist')]);
    const id = name.toLowerCase().replace(/[^a-z0-9]/g, '-');
    const escaped = name.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');
    const infoPlist = new TextEncoder().encode(new TextDecoder().decode(template).replaceAll('__NAME__', escaped).replaceAll('__ID__', id));
    const { executable, codeResources } = await signApp({ executable: player, infoPlist,
      resources: { 'Resources/game.bin': bytecode }, identifier: `io.github.arcadebasic.game.${id}` });
    const app = `${name}.app/Contents`;
    return [
      { name: `${app}/Info.plist`, data: infoPlist },
      { name: `${app}/MacOS/${name}`, data: executable, mode: 0o755 },
      { name: `${app}/Resources/game.bin`, data: bytecode },
      { name: `${app}/_CodeSignature/CodeResources`, data: codeResources },
    ];
  }

  /** The Windows or Linux player with the program appended. */
  function withProgram(player, program) {
    const out = new Uint8Array(player.length + program.length + 12);
    out.set(player);
    out.set(program, player.length);
    new DataView(out.buffer).setUint32(player.length + program.length, program.length, true);
    out.set(new TextEncoder().encode('FB-BCEND'), player.length + program.length + 4);
    return out;
  }

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
