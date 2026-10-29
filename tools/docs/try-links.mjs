// Puts a "▶ Try it" link under each example in the language reference: the example,
// compressed into an address that opens it in the browser IDE — what the IDE's own
// File → Copy a link to this program makes. Run it after adding or changing an example;
// a test (DocsTests) checks that every link still opens its example.
//
//   node tools/docs/try-links.mjs
//
// An example marked <!-- fragment --> on the line before its ```basic isn't a whole
// program: it gets no link, and the test doesn't compile it.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const ide = 'https://3583bytes.github.io/Arcade-BASIC/';
const files = ['docs/keywords.md'];

/** The program as a #code= link carries it: deflated, then base64url (main.js's pack). */
async function pack(text) {
  const stream = new Blob([text]).stream().pipeThrough(new CompressionStream('deflate-raw'));
  return Buffer.from(await new Response(stream).arrayBuffer()).toString('base64')
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

for (const file of files) {
  const full = path.join(repo, file);
  const lines = fs.readFileSync(full, 'utf8').replace(/\r\n/g, '\n').split('\n');
  const out = [];
  let links = 0;
  for (let i = 0; i < lines.length; i++) {
    out.push(lines[i]);
    if (lines[i].trim() !== '```basic') continue;
    const fragment = i > 0 && lines[i - 1].trim() === '<!-- fragment -->';
    let end = i + 1;
    while (lines[end].trim() !== '```') out.push(lines[end++]);
    out.push(lines[end]);
    const code = lines.slice(i + 1, end).join('\n') + '\n';
    if (!fragment) {
      out.push(`[▶ Try it](${ide}#code=${await pack(code)})`);
      links++;
    }
    // (the link that was there before, if any, makes way for the new one)
    i = lines[end + 1]?.startsWith('[▶ Try it](') ? end + 1 : end;
  }
  fs.writeFileSync(full, out.join('\n'));
  console.log(`${file}: ${links} links`);
}
