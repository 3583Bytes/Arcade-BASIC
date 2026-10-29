// Zip files in the browser, for File → Export (export.js). Each entry is deflated with the
// browser's own CompressionStream when that makes it smaller, keeps its Unix permissions
// (so an app's executable stays executable), and has a UTF-8 name.

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  return table;
})();

function crc32(data) {
  let c = 0xFFFFFFFF;
  for (let i = 0; i < data.length; i++) c = CRC_TABLE[(c ^ data[i]) & 0xFF] ^ (c >>> 8);
  return (c ^ 0xFFFFFFFF) >>> 0;
}

async function deflate(data) {
  if (!('CompressionStream' in globalThis)) return null;
  const stream = new Blob([data]).stream().pipeThrough(new CompressionStream('deflate-raw'));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

/** A zip of `files` — [{ name, data (bytes or text), mode (Unix permissions, default
 *  0o644) }] — as a Blob. */
export async function zip(files) {
  const utf8 = new TextEncoder();
  const now = new Date();
  const time = (now.getHours() << 11) | (now.getMinutes() << 5) | (now.getSeconds() >> 1);
  const date = ((now.getFullYear() - 1980) << 9) | ((now.getMonth() + 1) << 5) | now.getDate();
  const parts = [], directory = [];
  let offset = 0;
  for (const file of files) {
    const name = utf8.encode(file.name);
    const data = typeof file.data === 'string' ? utf8.encode(file.data) : file.data;
    const crc = crc32(data);
    const packed = await deflate(data);
    const method = packed && packed.length < data.length ? 8 : 0;   // deflated, or stored
    const body = method ? packed : data;

    const local = new DataView(new ArrayBuffer(30));
    local.setUint32(0, 0x04034b50, true);           // a file's header
    local.setUint16(4, 20, true);                   // needs zip 2.0
    local.setUint16(6, 0x0800, true);               // UTF-8 name
    local.setUint16(8, method, true);
    local.setUint16(10, time, true);
    local.setUint16(12, date, true);
    local.setUint32(14, crc, true);
    local.setUint32(18, body.length, true);
    local.setUint32(22, data.length, true);
    local.setUint16(26, name.length, true);
    parts.push(local, name, body);

    const entry = new DataView(new ArrayBuffer(46));
    entry.setUint32(0, 0x02014b50, true);           // its entry in the directory
    entry.setUint16(4, 0x0314, true);               // made on Unix (so the permissions count), zip 2.0
    entry.setUint16(6, 20, true);
    entry.setUint16(8, 0x0800, true);
    entry.setUint16(10, method, true);
    entry.setUint16(12, time, true);
    entry.setUint16(14, date, true);
    entry.setUint32(16, crc, true);
    entry.setUint32(20, body.length, true);
    entry.setUint32(24, data.length, true);
    entry.setUint16(28, name.length, true);
    entry.setUint32(38, ((0o100000 | (file.mode ?? 0o644)) << 16) >>> 0, true);   // a regular file, these permissions
    entry.setUint32(42, offset, true);
    directory.push(entry, name);
    offset += 30 + name.length + body.length;
  }
  const size = directory.reduce((n, part) => n + part.byteLength, 0);
  const end = new DataView(new ArrayBuffer(22));
  end.setUint32(0, 0x06054b50, true);               // the end of the directory
  end.setUint16(8, files.length, true);
  end.setUint16(10, files.length, true);
  end.setUint32(12, size, true);
  end.setUint32(16, offset, true);
  return new Blob([...parts, ...directory, end], { type: 'application/zip' });
}
