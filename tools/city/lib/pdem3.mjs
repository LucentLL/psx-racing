// pdem3.mjs - PDEM v3: the ground grid in blocks, each block delta-coded
// (WP-13, plan R5). The reader in CityElevation.cs decodes one block at a
// time into a small cache, so the grid is never resident whole; the build's
// Brotli does the entropy coding on top of the residuals.
//
//   u32 'PDEM' | i32 3 | u32 nx, nz | f32 x0, z0, cell, base, scale
//   | u32 B (cells per block side) | u32 nbx, nbz
//   | u32 off[nbx * nbz + 1] (byte offsets into the payload, block (bx, bz)
//     at index bz * nbx + bx; the last entry is the payload's length)
//   | payload
//
// Block (bx, bz) holds the nodes ix = bx*B .. min(bx*B + B, nx - 1) and
// iz = bz*B .. min(bz*B + B, nz - 1): one node of overlap with the next
// block, so every grid cell (ix, iz)-(ix+1, iz+1) lies inside ONE block and a
// bilinear read never straddles two. Nodes are row-major (x fastest), each a
// zigzag varint residual of its u16 against a planar prediction from the
// nodes already read: left + up - upleft, the left alone on the first row,
// the one above in the first column, 0 for the first node.

export const PDEM_MAGIC = 0x4D454450;   // "PDEM"
export const PDEM_V3 = 3;
export const PDEM_BLOCK = 32;

/// Encode a u16 grid (Uint16Array nx * nz, row-major) as PDEM v3 bytes.
export function encodePdem3({ nx, nz, x0, z0, cell, base, scale, q, B = PDEM_BLOCK }) {
  const nbx = Math.ceil((nx - 1) / B), nbz = Math.ceil((nz - 1) / B);
  const blocks = [];
  let total = 0;
  const tmp = Buffer.alloc((B + 1) * (B + 1) * 3 + 16);
  for (let bz = 0; bz < nbz; bz++)
    for (let bx = 0; bx < nbx; bx++) {
      const ix0 = bx * B, iz0 = bz * B;
      const w = Math.min(B, nx - 1 - ix0) + 1, h = Math.min(B, nz - 1 - iz0) + 1;
      let p = 0;
      for (let j = 0; j < h; j++)
        for (let i = 0; i < w; i++) {
          const at = (ii, jj) => q[(iz0 + jj) * nx + ix0 + ii];
          const pred = i === 0 && j === 0 ? 0 : j === 0 ? at(i - 1, 0) : i === 0 ? at(0, j - 1)
                     : at(i - 1, j) + at(i, j - 1) - at(i - 1, j - 1);
          const r = at(i, j) - pred;
          let z = r >= 0 ? r * 2 : -r * 2 - 1;               // zigzag
          while (z >= 0x80) { tmp[p++] = (z & 0x7F) | 0x80; z >>>= 7; }
          tmp[p++] = z;
        }
      const b = Buffer.from(tmp.subarray(0, p));
      blocks.push(b); total += b.length;
    }
  const head = Buffer.alloc(4 * 2 + 4 * 2 + 4 * 5 + 4 * 3 + 4 * (nbx * nbz + 1));
  let o = 0;
  head.writeUInt32LE(PDEM_MAGIC, o); o += 4;
  head.writeInt32LE(PDEM_V3, o); o += 4;
  head.writeUInt32LE(nx, o); o += 4; head.writeUInt32LE(nz, o); o += 4;
  for (const v of [x0, z0, cell, base, scale]) { head.writeFloatLE(v, o); o += 4; }
  head.writeUInt32LE(B, o); o += 4; head.writeUInt32LE(nbx, o); o += 4; head.writeUInt32LE(nbz, o); o += 4;
  let off = 0;
  for (const b of blocks) { head.writeUInt32LE(off, o); o += 4; off += b.length; }
  head.writeUInt32LE(off, o); o += 4;
  return Buffer.concat([head, ...blocks]);
}

/// Decode PDEM v3 bytes back to the whole u16 grid (tools only; the game
/// decodes a block at a time).
export function decodePdem3(buf) {
  let o = 0;
  const u32 = () => { const v = buf.readUInt32LE(o); o += 4; return v; };
  const f32 = () => { const v = buf.readFloatLE(o); o += 4; return v; };
  if (u32() !== PDEM_MAGIC) throw new Error('PDEM v3: bad magic');
  const version = buf.readInt32LE(o); o += 4;
  if (version !== PDEM_V3) throw new Error('PDEM v3: version ' + version);
  const nx = u32(), nz = u32();
  const x0 = f32(), z0 = f32(), cell = f32(), base = f32(), scale = f32();
  const B = u32(), nbx = u32(), nbz = u32();
  const offs = new Uint32Array(nbx * nbz + 1);
  for (let k = 0; k < offs.length; k++) offs[k] = u32();
  const pay = o;
  if (pay + offs[offs.length - 1] !== buf.length) throw new Error(`PDEM v3: payload ${buf.length - pay} B, index says ${offs[offs.length - 1]}`);
  const q = new Uint16Array(nx * nz);
  for (let bz = 0; bz < nbz; bz++)
    for (let bx = 0; bx < nbx; bx++) {
      const ix0 = bx * B, iz0 = bz * B;
      const w = Math.min(B, nx - 1 - ix0) + 1, h = Math.min(B, nz - 1 - iz0) + 1;
      let p = pay + offs[bz * nbx + bx];
      const end = pay + offs[bz * nbx + bx + 1];
      const blk = new Int32Array(w * h);
      for (let j = 0; j < h; j++)
        for (let i = 0; i < w; i++) {
          let z = 0, sh = 0, c;
          do { c = buf[p++]; z += (c & 0x7F) * 2 ** sh; sh += 7; } while (c & 0x80);
          const r = z % 2 ? -(z + 1) / 2 : z / 2;
          const pred = i === 0 && j === 0 ? 0 : j === 0 ? blk[i - 1] : i === 0 ? blk[(j - 1) * w]
                     : blk[j * w + i - 1] + blk[(j - 1) * w + i] - blk[(j - 1) * w + i - 1];
          const v = pred + r;
          if (v < 0 || v > 65535) throw new Error(`PDEM v3: block ${bx},${bz} node ${i},${j} decodes to ${v}`);
          blk[j * w + i] = v;
          const gi = (iz0 + j) * nx + ix0 + i;
          if ((i > 0 || bx === 0) && (j > 0 || bz === 0)) q[gi] = v;
          else if (q[gi] !== v) throw new Error(`PDEM v3: block ${bx},${bz} overlap node ${i},${j} is ${v}, its neighbour says ${q[gi]}`);
        }
      if (p !== end) throw new Error(`PDEM v3: block ${bx},${bz} read ${p - pay - offs[bz * nbx + bx]} B of ${end - pay - offs[bz * nbx + bx]}`);
    }
  return { version, nx, nz, x0, z0, cell, base, scale, B, nbx, nbz, q };
}
