// Ad-hoc code signing for a Mac app, in the browser — what `codesign --sign -` does — for
// File → Export's Mac apps (export.js). A Mac runs only signed code, and a downloaded app
// whose signature doesn't cover its Info.plist and resources is "damaged"; an app signed
// ad hoc like this opens once you allow it (System Settings → Privacy & Security).
//
// The executable is the desktop player, already signed ad hoc by its linker (on Intel with
// -adhoc_codesign: desktop/player/.cargo/config.toml). Each of its slices gets a signature
// that also seals the bundle — Info.plist, and the resources by the hash of
// _CodeSignature/CodeResources — in that signature's place, the file growing only if it
// needs more room. The signature's structures (Apple's cs_blobs.h) are big-endian; the
// Mach-O around them is little-endian.

const SUPERBLOB = 0xfade0cc0, CODE_DIRECTORY = 0xfade0c02, WRAPPER = 0xfade0b01;
const SLOT_CODE_DIRECTORY = 0, SLOT_REQUIREMENTS = 2, SLOT_SIGNATURE = 0x10000;
const LC_SEGMENT_64 = 0x19, LC_CODE_SIGNATURE = 0x1d;
const CPU_ARM64 = 0x0100000c;
// No requirements beyond the code's own hash: an empty set, as codesign writes for ad hoc.
const REQUIREMENTS = new Uint8Array([0xfa, 0xde, 0x0c, 0x01, 0, 0, 0, 12, 0, 0, 0, 0]);

const utf8 = new TextEncoder();
const sha = async (algorithm, data) => new Uint8Array(await crypto.subtle.digest(algorithm, data));
const base64 = bytes => btoa(String.fromCharCode(...bytes));
const alignUp = (n, to) => Math.ceil(n / to) * to;

/** Sign `executable` (a thin or universal Mach-O) for an app bundle whose Info.plist is
 *  `infoPlist` and whose other files are `resources` ({ 'Resources/game.bin': bytes },
 *  paths from Contents/). Returns { executable, codeResources } — the signed executable,
 *  and Contents/_CodeSignature/CodeResources. */
export async function signApp({ executable, infoPlist, resources, identifier }) {
  const codeResources = await sealResources(resources);
  // Special slots -1, -2, -3: Info.plist, the requirements, the sealed resources.
  const special = [await sha('SHA-256', infoPlist), await sha('SHA-256', REQUIREMENTS), await sha('SHA-256', codeResources)];
  const view = new DataView(executable.buffer, executable.byteOffset, executable.byteLength);
  if (view.getUint32(0) !== 0xcafebabe)
    return { executable: await signSlice(executable, identifier, special), codeResources };

  // Universal: sign each slice, then lay them out again (each at its alignment).
  const count = view.getUint32(4), archs = [];
  for (let i = 0; i < count; i++) {
    const at = 8 + 20 * i;
    archs.push({ cpu: view.getInt32(at), sub: view.getInt32(at + 4), offset: view.getUint32(at + 8),
                 size: view.getUint32(at + 12), align: view.getUint32(at + 16) });
  }
  for (const arch of archs)
    arch.signed = await signSlice(executable.subarray(arch.offset, arch.offset + arch.size), identifier, special);
  let end = 8 + 20 * count;
  for (const arch of archs) { arch.offset = alignUp(end, 2 ** arch.align); end = arch.offset + arch.signed.length; }
  const out = new Uint8Array(end), header = new DataView(out.buffer);
  header.setUint32(0, 0xcafebabe);
  header.setUint32(4, count);
  archs.forEach((arch, i) => {
    const at = 8 + 20 * i;
    header.setInt32(at, arch.cpu);
    header.setInt32(at + 4, arch.sub);
    header.setUint32(at + 8, arch.offset);
    header.setUint32(at + 12, arch.signed.length);
    header.setUint32(at + 16, arch.align);
    out.set(arch.signed, arch.offset);
  });
  return { executable: out, codeResources };
}

/** One 64-bit Mach-O, signed. */
async function signSlice(slice, identifier, special) {
  let m = slice.slice();
  let v = new DataView(m.buffer);
  if (v.getUint32(0, true) !== 0xfeedfacf) throw new Error('the Mac player isn\'t a 64-bit Mac executable');
  const arm = v.getInt32(4, true) === CPU_ARM64;
  let at = 32, text = null, linkedit = null, signature = null;
  for (let i = 0, count = v.getUint32(16, true); i < count; i++) {
    const cmd = v.getUint32(at, true);
    if (cmd === LC_SEGMENT_64) {
      const name = new TextDecoder().decode(m.subarray(at + 8, at + 24)).replace(/\0+$/, '');
      const segment = { at, vmsize: Number(v.getBigUint64(at + 32, true)), fileoff: Number(v.getBigUint64(at + 40, true)),
                        filesize: Number(v.getBigUint64(at + 48, true)) };
      if (name === '__TEXT') text = segment;
      if (name === '__LINKEDIT') linkedit = segment;
    }
    if (cmd === LC_CODE_SIGNATURE) signature = { at, offset: v.getUint32(at + 8, true), size: v.getUint32(at + 12, true) };
    at += v.getUint32(at + 4, true);
  }
  if (!text || !linkedit || !signature)
    throw new Error(`this site's Mac player (its ${arm ? 'Apple Silicon' : 'Intel'} half) isn't signed, so it can't sign apps — its build needs fixing (desktop/player/build.sh)`);

  // Pages as codesign has them: 16 KB on Apple Silicon, 4 KB on Intel.
  const pageShift = arm ? 14 : 12, page = 1 << pageShift;
  const codeLimit = signature.offset, codeSlots = Math.ceil(codeLimit / page);
  const ident = utf8.encode(identifier + '\0');
  const hashOffset = 88 + ident.length + special.length * 32;
  const cdLength = hashOffset + codeSlots * 32;
  const superLength = 12 + 3 * 8 + cdLength + REQUIREMENTS.length + 8;

  if (superLength > signature.size) {
    // More room: the signature runs on past the end of the file, and __LINKEDIT with it.
    const size = alignUp(superLength, 16), grown = new Uint8Array(signature.offset + size);
    grown.set(m.subarray(0, signature.offset));
    m = grown;
    v = new DataView(m.buffer);
    v.setUint32(signature.at + 12, size, true);
    const filesize = signature.offset + size - linkedit.fileoff;
    v.setBigUint64(linkedit.at + 48, BigInt(filesize), true);
    v.setBigUint64(linkedit.at + 32, BigInt(Math.max(linkedit.vmsize, alignUp(filesize, arm ? 0x4000 : 0x1000))), true);
    signature.size = size;
  } else {
    m.fill(0, signature.offset, signature.offset + signature.size);
  }

  const cd = new Uint8Array(cdLength), c = new DataView(cd.buffer);
  c.setUint32(0, CODE_DIRECTORY);
  c.setUint32(4, cdLength);
  c.setUint32(8, 0x20400);                    // version: with the executable segment
  c.setUint32(12, 0x2);                       // flags: ad hoc
  c.setUint32(16, hashOffset);
  c.setUint32(20, 88);                        // the identifier, after this header
  c.setUint32(24, special.length);
  c.setUint32(28, codeSlots);
  c.setUint32(32, codeLimit);
  cd[36] = 32;                                // hash size: SHA-256
  cd[37] = 2;                                 // hash type: SHA-256
  cd[39] = pageShift;
  c.setBigUint64(64, BigInt(text.fileoff));   // the executable segment: __TEXT
  c.setBigUint64(72, BigInt(text.filesize));
  c.setBigUint64(80, 1n);                     // ... of the main executable
  cd.set(ident, 88);
  special.forEach((hash, i) => cd.set(hash, hashOffset - 32 * (i + 1)));
  for (let i = 0; i < codeSlots; i++)
    cd.set(await sha('SHA-256', m.subarray(i * page, Math.min((i + 1) * page, codeLimit))), hashOffset + 32 * i);

  const blob = new Uint8Array(superLength), b = new DataView(blob.buffer);
  b.setUint32(0, SUPERBLOB);
  b.setUint32(4, superLength);
  b.setUint32(8, 3);
  const slots = [[SLOT_CODE_DIRECTORY, 36], [SLOT_REQUIREMENTS, 36 + cdLength], [SLOT_SIGNATURE, 36 + cdLength + REQUIREMENTS.length]];
  slots.forEach(([type, offset], i) => { b.setUint32(12 + 8 * i, type); b.setUint32(16 + 8 * i, offset); });
  blob.set(cd, 36);
  blob.set(REQUIREMENTS, 36 + cdLength);
  b.setUint32(superLength - 8, WRAPPER);       // an empty signature: ad hoc has none
  b.setUint32(superLength - 4, 8);
  m.set(blob, signature.offset);
  return m;
}

/** Contents/_CodeSignature/CodeResources for `resources`: each file's hashes, and the rules
 *  codesign seals an app's files by. */
async function sealResources(resources) {
  const names = Object.keys(resources).sort();
  const files = [], files2 = [];
  for (const name of names) {
    const sha1 = base64(await sha('SHA-1', resources[name])), sha256 = base64(await sha('SHA-256', resources[name]));
    files.push(`\t\t<key>${name}</key>\n\t\t<data>\n\t\t${sha1}\n\t\t</data>`);
    files2.push(`\t\t<key>${name}</key>\n\t\t<dict>\n\t\t\t<key>hash2</key>\n\t\t\t<data>\n\t\t\t${sha256}\n\t\t\t</data>\n\t\t</dict>`);
  }
  const rule = (key, body) => `\t\t<key>${key}</key>\n${body}`;
  const flags = (...entries) => `\t\t<dict>\n${entries.map(e => `\t\t\t${e}`).join('\n')}\n\t\t</dict>`;
  const weight = n => [`<key>weight</key>`, `<real>${n}</real>`];
  const yes = key => [`<key>${key}</key>`, '<true/>'];
  const rules = [
    rule('^Resources/', '\t\t<true/>'),
    rule('^Resources/.*\\.lproj/', flags(...yes('optional'), ...weight(1000))),
    rule('^Resources/.*\\.lproj/locversion.plist$', flags(...yes('omit'), ...weight(1100))),
    rule('^Resources/Base\\.lproj/', flags(...weight(1010))),
    rule('^version.plist$', '\t\t<true/>'),
  ];
  const rules2 = [
    rule('.*\\.dSYM($|/)', flags(...weight(11))),
    rule('^(.*/)?\\.DS_Store$', flags(...yes('omit'), ...weight(2000))),
    rule('^(Frameworks|SharedFrameworks|PlugIns|Plug-ins|XPCServices|Helpers|MacOS|Library/(Automator|Spotlight|LoginItems))/',
         flags(...yes('nested'), ...weight(10))),
    rule('^.*', '\t\t<true/>'),
    rule('^Info\\.plist$', flags(...yes('omit'), ...weight(20))),
    rule('^PkgInfo$', flags(...yes('omit'), ...weight(20))),
    rule('^Resources/', flags(...weight(20))),
    rule('^Resources/.*\\.lproj/', flags(...yes('optional'), ...weight(1000))),
    rule('^Resources/.*\\.lproj/locversion.plist$', flags(...yes('omit'), ...weight(1100))),
    rule('^Resources/Base\\.lproj/', flags(...weight(1010))),
    rule('^[^/]+$', flags(...yes('nested'), ...weight(10))),
    rule('^embedded\\.provisionprofile$', flags(...weight(20))),
    rule('^version\\.plist$', flags(...weight(20))),
  ];
  const dict = entries => `\t<dict>\n${entries.join('\n')}\n\t</dict>`;
  return utf8.encode(`<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
\t<key>files</key>
${dict(files)}
\t<key>files2</key>
${dict(files2)}
\t<key>rules</key>
${dict(rules)}
\t<key>rules2</key>
${dict(rules2)}
</dict>
</plist>
`);
}
