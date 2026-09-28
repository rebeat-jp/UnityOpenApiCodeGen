'use strict';
const zlib = require('zlib');

function archive(entries) {
  const blocks = [];
  for (const { name, content = '', type = '0', prefix = '', magic = 'ustar\0', version = '00' } of entries) {
    const data = Buffer.from(content);
    const header = Buffer.alloc(512);
    if (Buffer.byteLength(name) > 100) throw new Error('Test tar path is too long');
    header.write(name, 0);
    header.write('0000644\0', 100);
    header.write('0000000\0', 108);
    header.write('0000000\0', 116);
    header.write(data.length.toString(8).padStart(11, '0') + '\0', 124);
    header.write('00000000000\0', 136);
    header.fill(32, 148, 156);
    header.write(type, 156);
    header.write(magic, 257);
    header.write(version, 263);
    header.write(prefix, 345);
    let checksum = 0;
    for (const byte of header) checksum += byte;
    header.write(checksum.toString(8).padStart(6, '0') + '\0 ', 148);
    blocks.push(header, data, Buffer.alloc((512 - data.length % 512) % 512));
  }
  blocks.push(Buffer.alloc(1024));
  return zlib.gzipSync(Buffer.concat(blocks));
}
module.exports = { archive };
