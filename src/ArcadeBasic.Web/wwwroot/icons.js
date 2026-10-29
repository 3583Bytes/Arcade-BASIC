// App icons, for File → Export (export.js): one picture drawn at each size an OS wants, and
// packed the way it wants them —
//   - a Mac app's Contents/Resources/icon.icns: PNGs from 32 to 1024 pixels, and the 16 and
//     32 pixel images as ARGB too, as iconutil writes them;
//   - in a Windows player, the icon Explorer and its window show. The player is built with a
//     placeholder icon (desktop/player/icons/make-icons.mjs) whose images each leave room for
//     any PNG of their size, so a game's are written over them, in place;
//   - a PNG appended to a Linux player, which puts it on its window; a web page's icon.png.
// All but iconImages is byte work that runs in Node too: make-icons.mjs makes the player's
// own icons with it.

import { crc32 } from './zip.js';

const ascii = text => new TextEncoder().encode(text);

function concat(parts) {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let at = 0;
  for (const p of parts) { out.set(p, at); at += p.length; }
  return out;
}

// -- PNG ------------------------------------------------------------------------------------

const PNG_SIGNATURE = new Uint8Array([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

/** A PNG chunk: its length, type, data, and the CRC of type and data. */
export function chunk(type, data) {
  const out = new Uint8Array(12 + data.length);
  const v = new DataView(out.buffer);
  v.setUint32(0, data.length);
  out.set(ascii(type), 4);
  out.set(data, 8);
  v.setUint32(8 + data.length, crc32(out.subarray(4, 8 + data.length)));
  return out;
}

/** `rgba` (width × height, 4 bytes a pixel) as a PNG. */
export async function png(rgba, width, height) {
  const stride = width * 4;
  const raw = new Uint8Array(height * (stride + 1));
  for (let y = 0; y < height; y++) {
    const at = y * (stride + 1), row = y * stride;
    if (y === 0) { raw.set(rgba.subarray(0, stride), 1); continue; }
    raw[at] = 2;   // "up": each byte less the one above it, so a repeated row (pixels scaled up) is all 0
    for (let i = 0; i < stride; i++) raw[at + 1 + i] = (rgba[row + i] - rgba[row - stride + i]) & 0xFF;
  }
  const deflated = new Response(new Blob([raw]).stream().pipeThrough(new CompressionStream('deflate')));
  const ihdr = new Uint8Array(13);
  new DataView(ihdr.buffer).setUint32(0, width);
  new DataView(ihdr.buffer).setUint32(4, height);
  ihdr.set([8, 6, 0, 0, 0], 8);   // 8 bits a channel, RGBA, deflate, filtered by row, not interlaced
  return concat([PNG_SIGNATURE, chunk('IHDR', ihdr), chunk('IDAT', new Uint8Array(await deflated.arrayBuffer())),
    chunk('IEND', new Uint8Array(0))]);
}

/** The most bytes a size × size PNG from png() can take: its data stored rather than
 *  compressed (zlib's bound), and the chunks round it. */
export function pngBound(size) {
  const raw = size * (size * 4 + 1);
  return PNG_SIGNATURE.length + 25 + 12 + 12 + raw + (raw >> 10) + 256;
}

// -- Drawing --------------------------------------------------------------------------------

/** `source` (a canvas or an image) drawn on `ctx` as a size × size icon: fitted in, centred,
 *  see-through round it. A small picture scaled up keeps its pixels square (they're the
 *  point); anything else is smoothed. */
export function drawIcon(ctx, source, size) {
  const longest = Math.max(source.width, source.height), scale = size / longest;
  const w = Math.round(source.width * scale), h = Math.round(source.height * scale);
  ctx.clearRect(0, 0, size, size);
  ctx.imageSmoothingEnabled = scale < 1 || longest > 128;
  ctx.imageSmoothingQuality = 'high';
  ctx.drawImage(source, Math.floor((size - w) / 2), Math.floor((size - h) / 2), w, h);
}

/** `source` as an icon (drawIcon) at each of `sizes`: { size: { png, rgba } }. Each is
 *  shrunk from the size before it, when it's shrunk at all, which keeps it sharp. */
export async function iconImages(source, sizes) {
  const images = {};
  let larger = null;
  for (const size of [...new Set(sizes)].sort((a, b) => b - a)) {
    const canvas = new OffscreenCanvas(size, size), ctx = canvas.getContext('2d');
    drawIcon(ctx, larger && Math.max(source.width, source.height) > size ? larger : source, size);
    const rgba = ctx.getImageData(0, 0, size, size).data;
    images[size] = { rgba, png: await png(rgba, size, size) };
    larger = canvas;
  }
  return images;
}

// -- Mac ------------------------------------------------------------------------------------

/** The sizes icns() takes. */
export const MAC_SIZES = [16, 32, 64, 128, 256, 512, 1024];

/** A Mac icon (.icns) from images of MAC_SIZES ({ 16: { png, rgba }, … 1024: … }): the
 *  entries iconutil writes. */
export function icns(images) {
  const entries = [
    ['ic04', argb(images[16].rgba)], ['ic05', argb(images[32].rgba)],              // 16, 32
    ['ic11', images[32].png], ['ic12', images[64].png],                            // 16, 32 @2x
    ['ic07', images[128].png], ['ic13', images[256].png],                          // 128, @2x
    ['ic08', images[256].png], ['ic14', images[512].png],                          // 256, @2x
    ['ic09', images[512].png], ['ic10', images[1024].png],                         // 512, @2x
  ];
  const header = (type, length) => {
    const out = new Uint8Array(8);
    out.set(ascii(type));
    new DataView(out.buffer).setUint32(4, length);
    return out;
  };
  const parts = entries.flatMap(([type, data]) => [header(type, 8 + data.length), data]);
  return concat([header('icns', 8 + parts.reduce((n, p) => n + p.length, 0)), ...parts]);
}

/** An icns ARGB image: "ARGB", then its alpha, red, green and blue, each run-length coded —
 *  a byte n < 128 is n + 1 bytes as they are, n ≥ 128 the next byte n − 125 times. */
function argb(rgba) {
  const pixels = rgba.length / 4, out = [...ascii('ARGB')];
  for (const channel of [3, 0, 1, 2]) {
    const plane = Uint8Array.from({ length: pixels }, (_, i) => rgba[i * 4 + channel]);
    const runAt = i => i + 2 < pixels && plane[i] === plane[i + 1] && plane[i] === plane[i + 2];
    for (let i = 0; i < pixels;) {
      if (runAt(i)) {
        let run = 3;
        while (i + run < pixels && run < 130 && plane[i + run] === plane[i]) run++;
        out.push(0x80 + run - 3, plane[i]);
        i += run;
      } else {
        const start = i;
        while (i < pixels && i - start < 128 && (i === start || !runAt(i))) i++;
        out.push(i - start - 1, ...plane.subarray(start, i));
      }
    }
  }
  return new Uint8Array(out);
}

// -- Windows --------------------------------------------------------------------------------

/** A Windows executable's icon (its first icon group): the group's data, and each image's —
 *  its size, and where its bytes are (`offset`, `size` of them; `entry`, the resource entry
 *  that says where they are). */
function iconGroup(exe) {
  const v = new DataView(exe.buffer, exe.byteOffset, exe.byteLength);
  const pe = exe.length > 0x40 && v.getUint16(0, true) === 0x5A4D ? v.getUint32(0x3C, true) : 0;
  if (!pe || pe + 24 > exe.length || v.getUint32(pe, true) !== 0x4550) throw new Error("the Windows player isn't a Windows program");
  const sectionCount = v.getUint16(pe + 6, true), optional = pe + 24;
  const sections = optional + v.getUint16(pe + 20, true);
  const directories = optional + (v.getUint16(optional, true) === 0x20B ? 112 : 96);   // PE32+ or PE32
  const offsetOf = rva => {
    for (let s = 0; s < sectionCount; s++) {
      const at = sections + s * 40, start = v.getUint32(at + 12, true), length = v.getUint32(at + 16, true);
      if (rva >= start && rva < start + length) return v.getUint32(at + 20, true) + rva - start;
    }
    throw new Error("the Windows player's icon isn't where it says");
  };
  const resources = v.getUint32(directories + 2 * 8, true);   // data directory 2: the resources
  if (!resources) throw new Error('the Windows player has no icon');
  const base = offsetOf(resources);
  // The resources are a tree: types, then names (or numbers), then languages, then the data.
  const entries = at => Array.from({ length: v.getUint16(at + 12, true) + v.getUint16(at + 14, true) },
    (_, i) => ({ id: v.getUint32(at + 16 + i * 8, true), to: v.getUint32(at + 20 + i * 8, true) & 0x7FFFFFFF }));
  const ofType = type => entries(base).find(e => e.id === type);
  const dataOf = named => {
    const entry = base + entries(base + named.to)[0].to;   // its first language's
    return { entry, offset: offsetOf(v.getUint32(entry, true)), size: v.getUint32(entry + 4, true) };
  };
  const groups = ofType(14), icons = ofType(3);   // RT_GROUP_ICON, RT_ICON
  if (!groups || !icons) throw new Error('the Windows player has no icon');
  const group = dataOf(entries(base + groups.to)[0]);
  const sizes = Array.from({ length: v.getUint16(group.offset + 4, true) }, (_, i) => {
    const at = group.offset + 6 + i * 14;
    return { id: v.getUint16(at + 12, true), size: exe[at] || 256 };
  });
  const images = entries(base + icons.to).filter(named => sizes.some(s => s.id === named.id))
    .map(named => ({ id: named.id, ...dataOf(named) }));
  return { group, images, sizes };
}

/** The sizes of the images in a Windows player's icon: those to draw for withWindowsIcon. */
export const windowsIconSizes = exe => iconGroup(exe).sizes.map(s => s.size);

/** The images in a Windows executable's icon: [{ size, data }]. */
export function windowsIconImages(exe) {
  const { images, sizes } = iconGroup(exe);
  return sizes.map(({ id, size }) => {
    const image = images.find(i => i.id === id);
    return { size, data: exe.subarray(image.offset, image.offset + image.size) };
  });
}

/** A copy of the Windows executable `exe` with its icon replaced by `images` ([{ width,
 *  height, data, colours = 0, bits = 32 }]; data a PNG, or an .ico's bitmap): each written
 *  over one of the old images with room for it, the biggest first, and the icon's group
 *  listing them. One with no room left is left out (there's room for any PNG of the
 *  player's own icon's sizes). */
export function withWindowsIcon(exe, images) {
  const out = exe.slice();
  const v = new DataView(out.buffer);
  const { group, images: slots } = iconGroup(out);
  const free = [...slots].sort((a, b) => b.size - a.size);
  const placed = [];
  for (const image of [...images].sort((a, b) => b.data.length - a.data.length)) {
    if (!free.length || image.data.length > free[0].size) continue;
    const slot = free.shift();
    out.fill(0, slot.offset, slot.offset + slot.size);
    out.set(image.data, slot.offset);
    v.setUint32(slot.entry + 4, image.data.length, true);
    placed.push({ ...image, id: slot.id });
  }
  if (!placed.length) throw new Error("the icon doesn't fit in the Windows player");
  placed.sort((a, b) => b.width - a.width);
  placed.forEach((image, i) => {
    const at = group.offset + 6 + i * 14;
    out.set([image.width & 0xFF, image.height & 0xFF, image.colours ?? 0, 0], at);   // 256 is 0
    v.setUint16(at + 4, 1, true);
    v.setUint16(at + 6, image.bits ?? 32, true);
    v.setUint32(at + 8, image.data.length, true);
    v.setUint16(at + 12, image.id, true);
  });
  v.setUint16(group.offset + 4, placed.length, true);
  v.setUint32(group.entry + 4, 6 + placed.length * 14, true);
  return out;
}
