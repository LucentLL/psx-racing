// brsize.mjs <file>... - print {"<file>": {"raw": n, "brotli": n}} for each
// file: Brotli quality 11, window 24 (the size-ledger's proxy for what a
// file adds to the Brotli-compressed WebGL.data.unityweb). Python has no
// Brotli on this machine; node's zlib does.
import { readFileSync } from 'node:fs';
import { brotliCompressSync, constants as Z } from 'node:zlib';
const out = {};
for (const f of process.argv.slice(2)) {
  const b = readFileSync(f);
  out[f] = { raw: b.length, brotli: brotliCompressSync(b, { params: { [Z.BROTLI_PARAM_QUALITY]: 11, [Z.BROTLI_PARAM_LGWIN]: 24, [Z.BROTLI_PARAM_SIZE_HINT]: b.length } }).length };
}
console.log(JSON.stringify(out));
