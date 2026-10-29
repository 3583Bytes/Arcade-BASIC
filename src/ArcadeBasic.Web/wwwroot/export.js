// File → Export…: the program as something to give people — compiled, without its
// source — zipped in the browser:
//   - a Mac, Windows or Linux app: the desktop player (desktop/player; this site carries one
//     per OS in desktop/, which players.json lists) with the program — in a Mac app's
//     Resources, the app signed ad hoc (macsign.js); appended to the Windows and Linux
//     player as `arcade-basic build` appends one ([program][u32 length]["FB-BCEND"]);
//   - a web page: the player (src/ArcadeBasic.Player, in player/, which files.json lists)
//     with the program as game.bin, for itch.io or any website.
// Each has an icon (icons.js): Arcade BASIC's, a picture in the program, its screen, or a
// picture file.

import { zip } from './zip.js';
import { signApp } from './macsign.js';
import { drawIcon, iconImages, icns, MAC_SIZES, windowsIconSizes, withWindowsIcon } from './icons.js';

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
 *  `onProblems(result)` shows them. `pictures()` are the pictures in its files, for its
 *  icon ([{ file, name, line, width, height, draw() → canvas }]); `screen()` its screen as
 *  it was last, a canvas, or null. `onExported(fileName, bytes)` hears of each export. */
export function createExport({ dialog, programName, compile, pictures, screen, onProblems, onExported }) {
  const form = dialog.querySelector('form');
  const nameInput = form.elements.name;
  const kinds = dialog.querySelector('.kinds');
  const status = dialog.querySelector('.status');
  const after = dialog.querySelector('.after');
  const exportButton = dialog.querySelector('[value="export"]');
  const iconRow = dialog.querySelector('.icon'), iconSelect = form.elements.icon;
  const iconFile = iconRow.querySelector('input[type="file"]'), preview = iconRow.querySelector('canvas');
  let offered = null;   // what this site can export: { page: [path] | null, players: { os: { file, size, universal } } }
  let icons = [];       // the icons to choose from: [{ value, label, source() → a canvas or an image }]
  let arcadeIcon = null, chosenFile = null;   // Arcade BASIC's icon, once fetched; the picture file chosen, { name, image }
  const chosenIcons = new Map();             // the icon chosen for each program, this visit
  let iconNote = '';                         // why an app has the player's own icon instead
  let lastIcon = 'arcade';                   // the icon chosen before "A picture file…", to go back to

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

  // -- The icon

  const defaultIcon = async () => arcadeIcon ??= await createImageBitmap(await (await fetch('icon.png')).blob());

  /** The icons to choose from for `program`, with the one it had last, else a picture named
   *  "icon", else Arcade BASIC's, chosen. */
  function listIcons(program) {
    const found = pictures(), shot = screen();
    icons = [
      { value: 'arcade', label: 'Arcade BASIC’s', source: defaultIcon },
      ...found.map(p => ({
        value: `picture ${p.file} ${p.name || p.line}`, name: p.name,
        label: `${p.name || `The picture on line ${p.line}`} (${p.width} × ${p.height}${p.file === program ? '' : `, in ${p.file}`})`,
        source: () => p.draw(),
      })),
      ...(shot ? [{ value: 'screen', label: 'Its screen, as it was last', source: () => shot }] : []),
      ...(chosenFile ? [{ value: 'file', label: chosenFile.name, source: () => chosenFile.image }] : []),
      { value: 'choose', label: 'A picture file…' },
    ];
    iconSelect.replaceChildren(...icons.map(icon => new Option(icon.label, icon.value)));
    const named = icons.find(icon => icon.name?.toLowerCase() === 'icon');
    iconSelect.value = icons.find(icon => icon.value === chosenIcons.get(program))?.value ?? named?.value ?? 'arcade';
    iconRow.querySelector('.hint').hidden = !!named;
    lastIcon = iconSelect.value;
    showIcon();
  }

  const iconSource = async () => (await icons.find(icon => icon.value === iconSelect.value)?.source?.()) ?? defaultIcon();

  async function showIcon() {
    const ctx = preview.getContext('2d');
    drawIcon(ctx, await iconSource(), preview.width);
  }

  iconSelect.addEventListener('change', () => {
    if (iconSelect.value === 'choose') { iconFile.value = ''; iconFile.click(); return; }
    lastIcon = iconSelect.value;
    chosenIcons.set(programName(), iconSelect.value);
    showIcon();
  });
  iconFile.addEventListener('cancel', () => { iconSelect.value = lastIcon; });
  iconFile.addEventListener('change', async () => {
    const file = iconFile.files[0];
    if (!file) { iconSelect.value = lastIcon; return; }
    try {
      chosenFile = { name: file.name, image: await createImageBitmap(file) };
      chosenIcons.set(programName(), 'file');
    } catch {
      status.textContent = `${file.name} isn't a picture this browser can read.`;
      iconSelect.value = lastIcon;
      return;
    }
    status.textContent = '';
    listIcons(programName());
  });

  // -- The dialog

  async function open() {
    const program = programName();
    nameInput.value = program.replace(/\.bas$/i, '');
    dialog.querySelector('h2').textContent = `Export ${program}`;
    status.textContent = '';
    after.hidden = true;
    kinds.hidden = false;
    iconRow.hidden = false;
    listIcons(program);
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
    iconNote = '';
    try {
      const icon = await iconSource();
      const files = kind === 'web' ? await webPage(title, result.bytecode, icon)
        : kind === 'macos' ? await macApp(name, result.bytecode, icon)
        : kind === 'windows' ? await windowsApp(name, result.bytecode, icon)
        : await linuxApp(name, result.bytecode, icon);
      const blob = await zip(files);
      const zipName = `${name} (${KINDS[kind].suffix}).zip`;
      download(blob, zipName);
      onExported(zipName, blob.size);
      status.textContent = iconNote;
      after.querySelector('.file').textContent = `${zipName} (${megabytes(blob.size)})`;
      for (const what of after.querySelectorAll('[data-kind]')) what.hidden = what.dataset.kind !== kind;
      for (const app of after.querySelectorAll('.app')) app.textContent = kind === 'macos' ? `${name}.app` : kind === 'windows' ? `${name}.exe` : name;
      for (const icon of after.querySelectorAll('.iconFile')) icon.textContent = `${name}.png`;
      after.hidden = false;
      kinds.hidden = true;
      iconRow.hidden = true;
      exportButton.hidden = true;
    } catch (e) {
      status.textContent = `Couldn't export: ${e.message}`;
    } finally {
      exportButton.disabled = false;
    }
  }

  /** The web page: the player's files, its page titled `title` with the icon, and the program. */
  async function webPage(title, bytecode, icon) {
    const escaped = title.replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const files = await Promise.all(offered.page.map(async path => {
      let data = await fetchBytes(`player/${path}`);
      if (path === 'index.html') {
        data = new TextEncoder().encode(new TextDecoder().decode(data).replace(/<title>[^<]*<\/title>/,
          `<title>${escaped}</title>\n  <link rel="icon" href="icon.png">\n  <link rel="apple-touch-icon" href="icon.png">`));
      }
      return { name: path, data };
    }));
    const { 256: image } = await iconImages(icon, [256]);
    return [...files, { name: 'icon.png', data: image.png }, { name: 'game.bin', data: bytecode }];
  }

  /** The Mac app: name.app, the program and the icon in its Resources, signed. */
  async function macApp(name, bytecode, icon) {
    const [player, template, images] = await Promise.all([
      fetchBytes(`desktop/${offered.players.macos.file}`), fetchBytes('desktop/app-Info.plist'), iconImages(icon, MAC_SIZES)]);
    const id = name.toLowerCase().replace(/[^a-z0-9]/g, '-');
    const escaped = name.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');
    const infoPlist = new TextEncoder().encode(new TextDecoder().decode(template).replaceAll('__NAME__', escaped).replaceAll('__ID__', id));
    const resources = { 'Resources/game.bin': bytecode, 'Resources/icon.icns': icns(images) };
    const { executable, codeResources } = await signApp({ executable: player, infoPlist, resources,
      identifier: `io.github.arcadebasic.game.${id}` });
    const app = `${name}.app/Contents`;
    return [
      { name: `${app}/Info.plist`, data: infoPlist },
      { name: `${app}/MacOS/${name}`, data: executable, mode: 0o755 },
      ...Object.entries(resources).map(([path, data]) => ({ name: `${app}/${path}`, data })),
      { name: `${app}/_CodeSignature/CodeResources`, data: codeResources },
    ];
  }

  /** The Windows app: the player with the icon written into it, and the program appended. A
   *  player with no room for it (built before players had) keeps its own icon. */
  async function windowsApp(name, bytecode, icon) {
    let player = await fetchBytes(`desktop/${offered.players.windows.file}`);
    try {
      const images = await iconImages(icon, windowsIconSizes(player));
      player = withWindowsIcon(player, Object.entries(images).map(([size, { png }]) => ({ width: +size, height: +size, data: png })));
    } catch (e) {
      iconNote = `It has the player's own icon: ${e.message}.`;
    }
    return [{ name: `${name}.exe`, mode: 0o755, data: withProgram(player, bytecode) }];
  }

  /** The Linux app: the player with the icon and the program appended; and the icon beside
   *  it, for a launcher. */
  async function linuxApp(name, bytecode, icon) {
    const [player, { 256: image }] = await Promise.all([
      fetchBytes(`desktop/${offered.players.linux.file}`), iconImages(icon, [256])]);
    return [
      { name, mode: 0o755, data: withProgram(player, bytecode, image.png) },
      { name: `${name}.png`, data: image.png },
    ];
  }

  /** The Windows or Linux player with the program appended ([program][u32 length]["FB-BCEND"]),
   *  and before it, if there's one, the icon the Linux player puts on its window
   *  ([PNG][u32 length]["FB-ICEND"]). */
  function withProgram(player, program, icon = null) {
    const parts = [player, ...(icon ? [icon, trailer(icon.length, 'FB-ICEND')] : []), program, trailer(program.length, 'FB-BCEND')];
    const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
    parts.reduce((at, p) => { out.set(p, at); return at + p.length; }, 0);
    return out;
  }

  /** A length (u32, little-endian) and a magic word, after what it measures. */
  function trailer(length, magic) {
    const out = new Uint8Array(12);
    new DataView(out.buffer).setUint32(0, length, true);
    out.set(new TextEncoder().encode(magic), 4);
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
