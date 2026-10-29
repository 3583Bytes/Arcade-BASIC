// For the Pages workflow, on Windows: a test icon — orange above, blue below — written into
// the Windows player as File → Export writes a game's (src/ArcadeBasic.Web/wwwroot/icons.js),
// for the workflow to check that Windows reads it.
//   node desktop/player/icons/check-windows-icon.mjs <player.exe> <out.exe>

import { readFileSync, writeFileSync } from 'node:fs';
import { png, windowsIconSizes, withWindowsIcon } from '../../../src/ArcadeBasic.Web/wwwroot/icons.js';

const [player, out] = process.argv.slice(2);
const exe = new Uint8Array(readFileSync(player));
const sizes = [...new Set(windowsIconSizes(exe))];
const images = await Promise.all(sizes.map(async size => {
  const rgba = new Uint8Array(size * size * 4);
  for (let i = 0; i < size * size; i++) rgba.set(i < size * size / 2 ? [255, 128, 0, 255] : [0, 0, 255, 255], i * 4);
  return { width: size, height: size, data: await png(rgba, size, size) };
}));
writeFileSync(out, withWindowsIcon(exe, images));
console.log(`${out}: the player with a test icon (${sizes.join(', ')} pixels)`);
