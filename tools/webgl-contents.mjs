// WHAT A WEBGL BUILD ACTUALLY CARRIES - read from the shipped data file, not
// from the scene list that was asked for (asserting the request says nothing
// about what shipped: the pizzeria lesson, see Docs / the build notes).
//
//   node tools/webgl-contents.mjs <Build/WebGL> [--edition MAIN|CITY|ALL] [--source <checkout>] [--quiet]
//
// Decodes Build/WebGL.data.unityweb (Brotli, the UnityWebData1.0 package,
// then data.unity3d's UnityFS blocks - LZ4 decoded here, no npm packages) and
// checks the SERIALIZED files only (globalgamemanagers, levels, sharedassets,
// resources.assets). Code string literals live in global-metadata.dat and are
// deliberately not searched: "charlotte_routes.json" in an error message is a
// string, not the city.
//
// Checks, exit 1 on any failure:
//   * the scene paths in the build settings equal the "scene" lines of
//     psx-build-report.txt (written by PSXBuildWebGL from SceneOrder(edition));
//   * nothing the report says was PARKED is in the player (Resources ships
//     whole, so a parked item that is present means the park did not hold);
//   * the edition's own rules: MAIN has no Resources key "charlotte_*" and no
//     "cityprops/*"; CITY has no "pizzacargo/*";
//   * and PRESENCE, the half that catches a build missing its own data: every
//     Resources asset of the source checkout (--source, default the checkout
//     this script is in) is in the player's Resources container, except what
//     the edition may leave out (MAIN: charlotte_*, cityprops/*; CITY:
//     pizzacargo/*, the root .json stage bakes) - and the build's park list
//     stays inside that allowance.
// Why BuildReport.packedAssets is not the proof: an incremental build that
// reuses its packed content reports none (the MAIN build of 2026-09-29 said
// 0 packed sources; the CITY build beside it listed 3640), and a report is
// what the build SAYS - this reads what the player IS.
import fs from "fs";
import path from "path";
import zlib from "zlib";
import { fileURLToPath } from "url";

const args = process.argv.slice(2);
const valued = new Set(["--edition", "--source"]);
const dir = args.find((a, i) => !a.startsWith("--") && !valued.has(args[i - 1])) || ".";
const edArg = args.indexOf("--edition");
const srcArg = args.indexOf("--source") >= 0 ? args[args.indexOf("--source") + 1] || "" : "";
const quiet = args.includes("--quiet");
const dataFile = fs.existsSync(path.join(dir, "Build")) ? fs.readdirSync(path.join(dir, "Build"))
  .filter(n => /\.data(\.unityweb|\.br|\.gz)?$/.test(n)).map(n => path.join(dir, "Build", n))[0] : dir;
if (!dataFile || !fs.existsSync(dataFile)) { console.log("NO DATA FILE under " + dir); process.exit(2); }
const reportPath = path.join(fs.statSync(dir).isDirectory() ? dir : path.dirname(dataFile), "psx-build-report.txt");
const report = fs.existsSync(reportPath) ? fs.readFileSync(reportPath, "utf8").split(/\r?\n/) : [];
let edition = edArg >= 0 ? (args[edArg + 1] || "").toUpperCase() : "";
if (!edition) { const e = report.find(l => l.startsWith("edition ")); edition = e ? e.slice(8).trim() : "ALL"; }

const raw = fs.readFileSync(dataFile);
let buf;
try { buf = zlib.brotliDecompressSync(raw); } catch { try { buf = zlib.gunzipSync(raw); } catch { buf = raw; } }
if (!buf.toString("latin1", 0, 15).startsWith("UnityWebData1.0")) { console.log("NOT A UnityWebData package: " + dataFile); process.exit(2); }

const headerSize = buf.readUInt32LE(16);
const files = [];
for (let p = 20; p < headerSize;) {
  const off = buf.readUInt32LE(p), size = buf.readUInt32LE(p + 4), nl = buf.readUInt32LE(p + 8);
  files.push({ name: buf.toString("utf8", p + 12, p + 12 + nl), off, size });
  p += 12 + nl;
}

function lz4(src, outLen) {
  const out = Buffer.alloc(outLen);
  let i = 0, o = 0;
  while (i < src.length) {
    const tok = src[i++];
    let lit = tok >> 4;
    if (lit === 15) { let b; do { b = src[i++]; lit += b; } while (b === 255); }
    src.copy(out, o, i, i + lit); i += lit; o += lit;
    if (i >= src.length) break;
    const offs = src[i] | (src[i + 1] << 8); i += 2;
    let ml = tok & 15;
    if (ml === 15) { let b; do { b = src[i++]; ml += b; } while (b === 255); }
    ml += 4;
    for (let k = 0; k < ml; k++) { out[o] = out[o - offs]; o++; }
  }
  return out;
}

// Serialized files: data.unity3d's nodes (and any loose .assets beside it).
const serialized = [];
const du = files.find(f => f.name === "data.unity3d");
if (du) {
  const b = buf.subarray(du.off, du.off + du.size);
  let q = 0;
  const cstr = () => { const e = b.indexOf(0, q); const s = b.toString("latin1", q, e); q = e + 1; return s; };
  cstr(); const ver = b.readUInt32BE(q); q += 4; cstr(); cstr();
  q += 8;
  const cSize = b.readUInt32BE(q), uSize = b.readUInt32BE(q + 4), flags = b.readUInt32BE(q + 8); q += 12;
  if (ver >= 7) q = (q + 15) & ~15;
  let infoC;
  if (flags & 0x80) infoC = b.subarray(b.length - cSize); else { infoC = b.subarray(q, q + cSize); q += cSize; }
  const info = (flags & 0x3f) === 0 ? infoC : lz4(infoC, uSize);
  if (flags & 0x200) q = (q + 15) & ~15;
  let r = 16;
  const nb = info.readUInt32BE(r); r += 4;
  const blocks = [];
  for (let k = 0; k < nb; k++) { blocks.push({ u: info.readUInt32BE(r), c: info.readUInt32BE(r + 4), f: info.readUInt16BE(r + 8) }); r += 10; }
  const nn = info.readUInt32BE(r); r += 4;
  const nodes = [];
  for (let k = 0; k < nn; k++) {
    const off = Number(info.readBigUInt64BE(r)), size = Number(info.readBigUInt64BE(r + 8)); r += 20;
    const e = info.indexOf(0, r); nodes.push({ name: info.toString("utf8", r, e), off, size }); r = e + 1;
  }
  const parts = [];
  for (const bl of blocks) {
    const c = b.subarray(q, q + bl.c); q += bl.c;
    parts.push((bl.f & 0x3f) === 0 ? c : lz4(c, bl.u));
  }
  const data = Buffer.concat(parts);
  for (const n of nodes) serialized.push({ name: n.name, text: data.toString("latin1", n.off, n.off + n.size), size: n.size });
}
for (const f of files)
  if (/\.assets$|^globalgamemanagers|^level\d+$/.test(f.name))
    serialized.push({ name: f.name, text: buf.toString("latin1", f.off, f.off + f.size), size: f.size });

const MiB = n => (n / 1048576).toFixed(2) + " MiB";
console.log("webgl-contents " + dataFile);
console.log("  edition " + edition + "  data " + MiB(raw.length) + " shipped, " + MiB(buf.length) + " unpacked");
if (!quiet) for (const f of files) console.log("  package " + MiB(f.size).padStart(11) + "  " + f.name);

let bad = 0;
const fail = s => { bad++; console.log("  FAIL " + s); };
const ok = s => console.log("  ok   " + s);

// ---- scenes ----
const shippedScenes = new Set();
for (const s of serialized)
  if (s.name === "globalgamemanagers")
    for (const m of s.text.matchAll(/Assets\/[A-Za-z0-9_\/\. -]{3,160}?\.unity/g)) shippedScenes.add(m[0]);
const wantScenes = report.filter(l => /^\s+scene /.test(l)).map(l => l.trim().slice(6).trim());
console.log("  scenes shipped " + shippedScenes.size + ": " + [...shippedScenes].map(s => path.basename(s, ".unity")).join(", "));
if (!wantScenes.length) fail("no psx-build-report.txt scene list beside the build to check against");
else {
  const missing = wantScenes.filter(s => !shippedScenes.has(s));
  const extra = [...shippedScenes].filter(s => !wantScenes.includes(s));
  if (missing.length) fail("scenes the build asked for and does not carry: " + missing.join(", "));
  if (extra.length) fail("scenes the player carries that " + edition + " must not: " + extra.join(", "));
  if (!missing.length && !extra.length) ok("the player's scenes are exactly " + edition + "'s " + wantScenes.length);
}

// ---- Resources: what was parked must not be there, and the edition rules ----
const key = n => n.toLowerCase().replace(/\.(bytes|json|png|txt|asset|prefab)$/, "");
const parked = report.filter(l => /^\s+parked /.test(l)).map(l => l.trim().slice(7).trim());
const rules = [];
for (const p of parked) {
  const k = key(p);
  // A folder parks as "<name>/..." keys; a file as its own key.
  rules.push({ why: "parked " + p, re: new RegExp("(^|[^a-z0-9_])" + k.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") +
                                                    (/\./.test(p) ? "(?![a-z0-9_])" : "/"), "i") });
}
if (edition === "MAIN") {
  rules.push({ why: "MAIN rule: no charlotte_* Resources", re: /(^|[^a-z0-9_])charlotte_(city|bld|dem|routes|thumb)(?![a-z0-9_])/i });
  rules.push({ why: "MAIN rule: no cityprops/* Resources", re: /(^|[^a-z0-9_])cityprops\//i });
}
if (edition === "CITY") rules.push({ why: "CITY rule: no pizzacargo/* Resources", re: /(^|[^a-z0-9_])pizzacargo\//i });
for (const r of rules) {
  const where = [];
  for (const s of serialized) {
    const g = new RegExp(r.re.source, "gi");
    const hits = s.text.match(g);
    if (hits) where.push(s.name + " x" + hits.length);
  }
  if (where.length) fail(r.why + " - found in " + where.join(", "));
  else ok(r.why + " - absent");
}

// ---- Resources: what the edition MUST carry ----
//
// Absence alone passes a build that is missing its OWN data - a CITY player
// with an empty Resources/CityProps (every tower gone) or a MAIN one without
// its stage bakes would clear every rule above. So the other half: every
// Resources asset in the SOURCE checkout (one key per non-folder .meta - the
// source keeps the CityProps metas even though the prefabs are baked in the
// sandbox) must be in the player's Resources container, except what this
// edition is ALLOWED to leave out. That allowance is written here, by rule,
// not read from the build's own park list - a park that took too much must
// fail, not excuse itself:
//   MAIN may leave out charlotte_* (root) and cityprops/*;
//   CITY may leave out pizzacargo/* and the root .json stage bakes (every root
//        .json but rg2_* and charlotte_*);
//   ALL  may leave out nothing.
// And the build's park list must stay inside the allowance.
const mayOmit = k =>
  edition === "MAIN" ? (/^charlotte_[^/]*$/.test(k) || k.startsWith("cityprops/"))
  : edition === "CITY" ? (k.startsWith("pizzacargo/") || (!k.includes("/") && k.endsWith(".json") &&
                                                          !/^(rg2_|charlotte_)/.test(k)))
  : false;
// mayOmit is asked of keys WITH their extension for the root-.json test.
const srcRoot = srcArg || path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const resDir = path.join(srcRoot, "Assets", "PSXRacing", "Resources");
if (!fs.existsSync(resDir)) fail("no source Resources at " + resDir + " to check the player against (--source <checkout>)");
else {
  const want = [];   // { key, withExt }
  const walk = d => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const f = path.join(d, e.name);
      if (e.isDirectory()) { walk(f); continue; }
      if (!e.name.endsWith(".meta")) continue;
      if (/^\s*folderAsset:\s*yes\s*$/m.test(fs.readFileSync(f, "utf8"))) continue;
      const rel = path.relative(resDir, f.slice(0, -5)).split(path.sep).join("/").toLowerCase();
      want.push({ key: rel.replace(/\.[^./]+$/, ""), withExt: rel });
    }
  };
  walk(resDir);
  // The container's keys: 4-byte-aligned, length-prefixed strings in
  // globalgamemanagers (the ResourceManager's m_Container), lower case.
  const keys = new Set();
  const ggm = serialized.find(s => s.name === "globalgamemanagers");
  if (ggm) {
    const b = Buffer.from(ggm.text, "latin1");
    for (let p = 0; p + 8 <= b.length; p += 4) {
      const L = b.readUInt32LE(p);
      if (L < 1 || L > 240 || p + 4 + L > b.length) continue;
      const s = b.toString("latin1", p + 4, p + 4 + L);
      if (/^[a-z0-9_\-\/. ()']+$/.test(s)) keys.add(s);
    }
  }
  const allowed = want.filter(w => mayOmit(w.withExt));
  const must = want.filter(w => !mayOmit(w.withExt));
  const missing = must.filter(w => !keys.has(w.key));
  const byTop = {};
  for (const w of must) {
    const top = w.key.includes("/") ? w.key.split("/")[0] + "/*" : "(root)";
    const e = byTop[top] || (byTop[top] = { n: 0, miss: 0 });
    e.n++; if (!keys.has(w.key)) e.miss++;
  }
  const table = Object.entries(byTop).sort().map(([t, e]) => t + " " + (e.n - e.miss) + "/" + e.n).join(", ");
  if (!ggm) fail("no globalgamemanagers in the player - cannot read its Resources");
  else if (missing.length)
    fail(missing.length + " Resources asset(s) " + edition + " must ship are NOT in the player: " +
         missing.slice(0, 12).map(w => w.withExt).join(", ") + (missing.length > 12 ? ", ..." : "") +
         "  [" + table + "]");
  else ok("every Resources asset " + edition + " must ship is in the player: " + must.length + " of " +
          want.length + " in the source (" + allowed.length + " it may leave out)  [" + table + "]");
  // What the build parked must be inside the allowance.
  const overPark = parked.filter(p => {
    const pk = p.toLowerCase();
    const inside = want.filter(w => w.withExt === pk || w.withExt.startsWith(pk + "/"));
    return inside.length === 0 ? !mayOmit(pk) && !mayOmit(pk + "/x") : inside.some(w => !mayOmit(w.withExt));
  });
  if (overPark.length) fail("the build parked " + overPark.join(", ") + ", which " + edition + " must ship");
  else if (parked.length) ok("everything the build parked is " + edition + "'s to leave out (" + parked.length + ")");
}

console.log(bad ? "WEBGL CONTENTS FAILED (" + bad + ")" : "WEBGL CONTENTS OK - " + edition);
process.exit(bad ? 1 : 0);
