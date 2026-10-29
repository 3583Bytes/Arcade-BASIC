// Arcade BASIC's icon — drawn below as a picture, a character a pixel, the way programs keep
// theirs — made into the files that need it:
//   icons/icon.png    128 × 128: the player's window icon on Linux (built into the player)
//   icons/icon.ico    the Windows player's icon. Each image is padded (a "paDd" chunk) to the
//                     most a PNG of its size can take, so File → Export can write a game's icon
//                     over it in place (src/ArcadeBasic.Web/wwwroot/icons.js)
//   icons/icon.icns   a Mac app's, which `arcade-basic build --desktop` gives the apps it makes
//   src/ArcadeBasic.Web/wwwroot/icon.png   16 × 16: the IDE's, and File → Export's when you
//                     don't choose one
// After changing the picture, run it with Node 22 or later and rebuild the player:
//   node desktop/player/icons/make-icons.mjs

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { png, pngBound, chunk, icns, MAC_SIZES } from '../../../src/ArcadeBasic.Web/wwwroot/icons.js';

const PICTURE = [
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkggkkkkkkk',
  'kkkkkkggggkkkkkk',
  'kkkkkggggggkkkkk',
  'kkkkggkggkggkkkk',
  'kkkkggggggggkkkk',
  'kkkkkkgkkgkkkkkk',
  'kkkkkgkggkgkkkkk',
  'kkkkgkgkkgkgkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
];
// Opaque to its edges, so macOS rounds its corners as it does an app's, and other systems show a tile.
const COLOURS = { k: [0x16, 0x18, 0x1d, 255], g: [0x55, 0xff, 0x55, 255] };
const WINDOWS_SIZES = [16, 24, 32, 48, 64, 256];

/** The picture at size × size, its pixels scaled up square. */
function rgbaAt(size) {
  const n = PICTURE.length, rgba = new Uint8Array(size * size * 4);
  for (let y = 0; y < size; y++)
    for (let x = 0; x < size; x++)
      rgba.set(COLOURS[PICTURE[Math.floor(y * n / size)][Math.floor(x * n / size)]], (y * size + x) * 4);
  return rgba;
}

const image = async size => { const rgba = rgbaAt(size); return { rgba, png: await png(rgba, size, size) }; };

/** `data` (a PNG) padded to `length` bytes with a chunk of zeros before its IEND. */
function padded(data, length) {
  const room = length - data.length - 12;
  if (room < 0) throw new Error(`a PNG of ${data.length} bytes is more than ${length}`);
  const out = new Uint8Array(length);
  out.set(data.subarray(0, data.length - 12));
  out.set(chunk('paDd', new Uint8Array(room)), data.length - 12);
  out.set(data.subarray(data.length - 12), length - 12);
  return out;
}

/** An .ico of PNGs, each padded to the most a PNG of its size takes. */
async function ico(sizes) {
  const images = await Promise.all(sizes.map(async size => padded((await image(size)).png, pngBound(size))));
  const header = new Uint8Array(6 + 16 * sizes.length), v = new DataView(header.buffer);
  v.setUint16(2, 1, true);              // an icon
  v.setUint16(4, sizes.length, true);
  let offset = header.length;
  sizes.forEach((size, i) => {
    const at = 6 + i * 16;
    header.set([size & 0xFF, size & 0xFF], at);   // 256 is 0
    v.setUint16(at + 4, 1, true);
    v.setUint16(at + 6, 32, true);
    v.setUint32(at + 8, images[i].length, true);
    v.setUint32(at + 12, offset, true);
    offset += images[i].length;
  });
  return Buffer.concat([header, ...images]);
}

const here = path => fileURLToPath(new URL(path, import.meta.url));
const mac = Object.fromEntries(await Promise.all(MAC_SIZES.map(async size => [size, await image(size)])));
const files = {
  'icon.png': (await image(128)).png,
  'icon.ico': await ico(WINDOWS_SIZES),
  'icon.icns': icns(mac),
  '../../../src/ArcadeBasic.Web/wwwroot/icon.png': (await image(PICTURE.length)).png,
};
for (const [path, data] of Object.entries(files)) {
  writeFileSync(here(path), data);
  console.log(`${path}: ${data.length} bytes`);
}
