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
//     stays inside that allowance;
//   * and SHADERS: every shader the runtime code names (a runtime .cs string
//     literal that is a .shader's declared name, or Shader.Find's argument) is
//     the own name of a Shader object in the player - Shader.Find finds nothing
//     else there (2026-09-29: CITY without PSX/Glow, no car lamps) - and the
//     build report's "runtime-shader" list (PSXBuildWebGL / RuntimeShaders.cs)
//     is the same list;
//   * and the CITY KIT's shaders (Resources/CityKit.asset: the streamed city,
//     its canopy trees and lamp posts draw with its materials, which no scene
//     holds) are Shaders in the player whenever the edition must ship the
//     kit, and the report's "kit-shader" list is this read of the kit.
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
  for (const n of nodes) serialized.push({ name: n.name, text: data.toString("latin1", n.off, n.off + n.size), size: n.size,
                                           buf: data.subarray(n.off, n.off + n.size) });
}
for (const f of files)
  if (/\.assets$|^globalgamemanagers|^level\d+$|unity_builtin_extra$/.test(f.name))
    serialized.push({ name: f.name, text: buf.toString("latin1", f.off, f.off + f.size), size: f.size,
                      buf: buf.subarray(f.off, f.off + f.size) });

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

// ---- every shader the runtime NAMES must be a Shader in the player ----
//
// 2026-09-29: /city/ went live with no lamp glow on any car and "CarLights:
// PSX/Glow shader missing" x6. CarLights makes its materials by Shader.Find,
// which in a player finds only a shader the build packed; MAIN's scenes kept
// PSX/Glow by accident and CITY's do not. The rule is RuntimeShaders.cs's
// (Assets/PSXRacing/Editor), applied here independently to the same files: a
// runtime .cs (under Assets, in no Editor folder) naming a shader in a string
// literal - the declared name of a .shader under Assets, anywhere, or the
// argument of Shader.Find("...") / the const an identifier argument names.
// Comments do not count. Each name must be the OWN name of a Shader object
// (class 48) in the player's serialized files - found through the object
// table, not by searching the bytes, so a fallback name or a code string is
// not a shader. And the build's "runtime-shader" list (psx-build-report.txt)
// must be this same list: two scans of one rule that disagree mean one is
// wrong.
function lexCs(s) {
  // Comments blanked (newlines kept), strings kept; each literal's text and
  // line. The same lexer as RuntimeShaders.Lex.
  let i = 0, line = 1;
  const out = [], lits = [];
  const at = k => (k < s.length ? s[k] : "\0");
  const copy = () => { const c = s[i++]; out.push(c); if (c === "\n") line++; };
  const blank = () => { const c = s[i++]; out.push(c === "\n" ? "\n" : " "); if (c === "\n") line++; };
  function code(hole) {
    let depth = 0;
    while (i < s.length) {
      const c = s[i], d = at(i + 1);
      if (c === "/" && d === "/") { while (i < s.length && s[i] !== "\n") blank(); continue; }
      if (c === "/" && d === "*") {
        blank(); blank();
        while (i < s.length && !(s[i] === "*" && at(i + 1) === "/")) blank();
        if (i < s.length) { blank(); blank(); }
        continue;
      }
      if (hole) {
        if (c === "{") depth++;
        else if (c === "}") { if (depth === 0) return; depth--; }
      }
      if (c === "'") {
        copy();
        while (i < s.length && s[i] !== "'" && s[i] !== "\n") { if (s[i] === "\\") copy(); if (i < s.length) copy(); }
        if (i < s.length && s[i] === "'") copy();
        continue;
      }
      let j = i, verbatim = false, interp = false;
      while (j < s.length && j - i < 2 && (s[j] === "@" || s[j] === "$")) { if (s[j] === "@") verbatim = true; else interp = true; j++; }
      if (j < s.length && s[j] === '"') { while (i < j) copy(); str(verbatim, interp); continue; }
      copy();
    }
  }
  function str(verbatim, interp) {
    const from = line;
    copy();
    let text = "", holes = false;
    while (i < s.length) {
      const c = s[i];
      if (verbatim && c === '"' && at(i + 1) === '"') { copy(); copy(); text += '"'; continue; }
      if (c === '"') { copy(); break; }
      if (!verbatim && c === "\n") break;
      if (!verbatim && c === "\\") { copy(); if (i < s.length) { text += "\u0001"; copy(); } continue; }
      if (interp && c === "{") {
        if (at(i + 1) === "{") { copy(); copy(); text += "{"; continue; }
        holes = true; copy(); code(true); if (i < s.length) copy();
        continue;
      }
      if (interp && c === "}" && at(i + 1) === "}") { copy(); copy(); text += "}"; continue; }
      text += c; copy();
    }
    if (!holes) lits.push({ text, line: from });
  }
  code(false);
  return { code: out.join(""), lits };
}

// A SerializedFile's object table (format 14+, no type trees - a player's):
// [{ cls, off, size }], or null when the bytes are not a serialized file.
function objectTable(b) {
  if (!b || b.length < 48) return null;
  const version = b.readUInt32BE(8);
  if (version < 14 || version > 40) return null;
  let q = 16;
  const big = b[q] !== 0; q += 4;
  let dataOffset;
  if (version >= 22) {
    q += 4;
    const fileSize = Number(b.readBigUInt64BE(q)); q += 8;
    dataOffset = Number(b.readBigUInt64BE(q)); q += 8;
    q += 8;
    if (fileSize !== b.length) return null;
  } else {
    if (b.readUInt32BE(4) !== b.length) return null;
    dataOffset = b.readUInt32BE(12);
  }
  const i32 = () => { const v = big ? b.readInt32BE(q) : b.readInt32LE(q); q += 4; return v; };
  const u32 = () => { const v = big ? b.readUInt32BE(q) : b.readUInt32LE(q); q += 4; return v; };
  const i64 = () => { const v = Number(big ? b.readBigInt64BE(q) : b.readBigInt64LE(q)); q += 8; return v; };
  q = b.indexOf(0, q) + 1;                    // the Unity version string
  i32();                                      // target platform
  if (b[q++] !== 0) return null;              // type trees: not a player's file
  const types = [];
  for (let n = i32(), k = 0; k < n; k++) {
    const cls = i32();
    if (version >= 16) q += 1;                // stripped
    if (version >= 17) q += 2;                // script type index
    if ((version < 16 && cls < 0) || (version >= 16 && cls === 114)) q += 16;
    q += 16;                                  // type hash
    types.push(cls);
  }
  const objs = [];
  for (let n = i32(), k = 0; k < n; k++) {
    q = (q + 3) & ~3;
    i64();                                    // path id
    const start = version >= 22 ? i64() : u32();
    const size = u32();
    const cls = types[i32()];
    if (dataOffset + start + size > b.length) return null;
    objs.push({ cls, off: dataOffset + start, size });
  }
  return objs;
}

// A Shader object's own name. Its m_Name is empty in a player; the parsed
// form's name is the first length-prefixed string holding a "/" that is
// followed by two more strings (custom editor, fallback). A fallback name
// comes after it, so it is never taken for the shader's own.
function shaderOwnName(ob) {
  const pstr = p => {
    if (p + 4 > ob.length) return null;
    const L = ob.readUInt32LE(p);
    if (L > 255 || p + 4 + L > ob.length) return null;
    const s = ob.toString("latin1", p + 4, p + 4 + L);
    return /^[\x20-\x7e]*$/.test(s) ? { s, next: (p + 4 + L + 3) & ~3 } : null;
  };
  for (let p = 0; p + 12 <= ob.length; p += 4) {
    const S = pstr(p);
    if (!S || S.s.length < 3 || !S.s.includes("/")) continue;
    const C = pstr(S.next); if (!C) continue;
    const F = pstr(C.next); if (!F) continue;
    if (F.s && !F.s.includes("/")) continue;
    return S.s;
  }
  return null;
}

{
  const assets = path.join(srcRoot, "Assets");
  const walkFiles = (d, ext, acc = []) => {
    if (!fs.existsSync(d)) return acc;
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const f = path.join(d, e.name);
      if (e.isDirectory()) walkFiles(f, ext, acc);
      else if (e.name.endsWith(ext)) acc.push(f);
    }
    return acc;
  };
  const rel = f => path.relative(assets, f).split(path.sep).join("/");
  const declared = new Map();
  for (const f of walkFiles(assets, ".shader")) {
    const m = /^\s*Shader\s+"([^"]+)"/m.exec(lexCs(fs.readFileSync(f, "utf8")).code);
    if (m && !declared.has(m[1])) declared.set(m[1], "Assets/" + rel(f));
  }
  const needs = new Map();
  const add = (name, site) => { const s = needs.get(name) || []; if (!s.includes(site)) s.push(site); needs.set(name, s); };
  const consts = new Map(), idents = [], indirect = [];
  const csFiles = walkFiles(assets, ".cs").map(f => ({ f, r: rel(f) })).filter(x => !x.r.split("/").includes("Editor"))
                                          .sort((a, b) => (a.r < b.r ? -1 : a.r > b.r ? 1 : 0));
  for (const { f, r } of csFiles) {
    const { code, lits } = lexCs(fs.readFileSync(f, "utf8"));
    for (const l of lits) if (declared.has(l.text)) add(l.text, r + ":" + l.line);
    for (const m of code.matchAll(/\bconst\s+string\s+([A-Za-z_]\w*)\s*=\s*"((?:[^"\\\n]|\\.)*)"/g)) consts.set(m[1], m[2]);
    for (const m of code.matchAll(/\bShader\s*\.\s*Find\s*\(\s*(?:@?"((?:[^"\\\n]|\\.)*)"|([A-Za-z_][\w.]*))\s*\)/g)) {
      const site = r + ":" + code.slice(0, m.index).split("\n").length;
      if (m[1] !== undefined) add(m[1], site); else idents.push({ id: m[2], site, call: m[0].replace(/\s+/g, "") });
    }
  }
  for (const x of idents) {
    const last = x.id.slice(x.id.lastIndexOf(".") + 1);
    if (consts.has(last)) add(consts.get(last), x.site); else indirect.push(x.site + " " + x.call);
  }
  const want = [...needs.keys()].sort();

  // The player's shaders, by their own names.
  const own = new Map();
  let shaderObjs = 0, unnamed = 0;
  for (const s of serialized) {
    const objs = objectTable(s.buf);
    if (!objs) continue;
    for (const o of objs) {
      if (o.cls !== 48) continue;
      shaderObjs++;
      const n = shaderOwnName(s.buf.subarray(o.off, o.off + o.size));
      if (n === null) { unnamed++; continue; }
      own.set(n, (own.get(n) || 0) + 1);
    }
  }
  const dup = [...own].filter(([, c]) => c > 1).map(([n, c]) => n + " x" + c);
  console.log("  shaders in the player: " + shaderObjs + " Shader objects, " + own.size + " names" +
              (unnamed ? ", " + unnamed + " unread" : "") + (dup.length ? "; packed more than once: " + dup.join(", ") : ""));
  if (!csFiles.length) fail("no runtime .cs under " + assets + " to learn the runtime's shader names from (--source <checkout>)");
  else if (!shaderObjs) fail("no Shader object found in the player's serialized files - cannot check the runtime's shaders");
  else {
    const missing = want.filter(n => !own.has(n));
    if (missing.length)
      fail(missing.length + " shader(s) the runtime names are NOT in the player - Shader.Find returns null there: " +
           missing.map(n => n + " (" + needs.get(n).join(" ") + ")").join(", ") +
           ". Add each to GraphicsSettings' Always Included Shaders (see RuntimeShaders.cs).");
    else ok("every shader the runtime names is in the player: " + want.length + " (" + want.join(", ") + ")" +
            (indirect.length ? "  [indirect lookups, fed by those literals: " + indirect.join("; ") + "]" : ""));
  }
  // The build's own list (RuntimeShaders) must be this one.
  const listed = report.filter(l => /^\s+runtime-shader /.test(l)).map(l => l.trim().slice(15).split(" | ")[0].trim()).sort();
  if (!listed.length)
    console.log("  note the build report lists no runtime shaders (a build from before RuntimeShaders) - the player check above stands alone");
  else if (listed.join("\n") !== want.join("\n"))
    fail("the build's runtime-shader list and this scan disagree - build: " + listed.join(", ") + "; here: " + want.join(", ") +
         " (RuntimeShaders.cs and tools/webgl-contents.mjs must apply one rule; or the source moved since the build)");
  else ok("the build's runtime-shader list is this scan's (" + listed.length + ")");

  // ---- the city kit's shaders: in the player wherever the kit is ----
  //
  // The other way the runtime reaches a shader (RuntimeShaders.Kit): the
  // streamed city, its canopy trees (WP-08) and its lamp posts draw with the
  // materials of Resources/CityKit.asset, which CityKit.Get() loads - no scene
  // holds them. They ride in with the kit, so an edition that must ship the
  // kit (every Resources asset it may not leave out, above) must have each
  // one as a Shader in WebGL.data: every shader a kit material uses and every
  // one the kit's own shaders list names, read here from the source asset by
  // GUID. And the build's "kit-shader" list must be this one.
  const kitFile = path.join(assets, "PSXRacing", "Resources", "CityKit.asset");
  // "  kit-shader NAME | always-included|with the kit | packed|... | path | fields"
  const reportKitRows = report.filter(l => /^\s+kit-shader /.test(l)).map(l => l.trim().slice(11).split(" | ").map(x => x.trim()));
  // An engine shader cannot be named from the source (no .shader, no GUID of
  // its own); the build lists it, this read counts it, neither compares it.
  const reportKit = reportKitRows.filter(c => c[2] !== "engine").map(c => c[0]).sort();
  const reportKitNone = report.find(l => /^kit-shaders none/.test(l));
  if (!fs.existsSync(kitFile)) {
    if (reportKit.length) fail("the build lists kit shaders (" + reportKit.join(", ") + ") and the source has no " + kitFile);
    else console.log("  note no city kit in the source (" + kitFile + ") - no kit shaders to ask for");
  } else if (mayOmit("citykit.asset")) {
    console.log("  note " + edition + " may leave the city kit out - its shaders are not asked for");
  } else {
    const guidIn = f => { const m = /guid:\s*([0-9a-f]{32})/.exec(fs.readFileSync(f, "utf8")); return m ? m[1] : null; };
    const shaderByGuid = new Map();
    for (const f of walkFiles(assets, ".shader")) {
      const m = /^\s*Shader\s+"([^"]+)"/m.exec(lexCs(fs.readFileSync(f, "utf8")).code);
      const g = fs.existsSync(f + ".meta") ? guidIn(f + ".meta") : null;
      if (m && g) shaderByGuid.set(g, m[1]);
    }
    const matByGuid = new Map();
    for (const f of walkFiles(assets, ".mat.meta")) { const g = guidIn(f); if (g) matByGuid.set(g, f.slice(0, -5)); }
    const kit = fs.readFileSync(kitFile, "utf8");
    const kitNames = new Map();   // name -> what reaches it
    const reach = (name, what) => { const s = kitNames.get(name) || new Set(); s.add(what); kitNames.set(name, s); };
    const unread = [];
    // the kit's own shaders list: fileID 4800000 references (a .shader asset)
    for (const m of kit.matchAll(/\{fileID:\s*4800000,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}/g)) {
      const n = shaderByGuid.get(m[1]);
      if (n) reach(n, "shaders list"); else unread.push("shaders list " + m[1]);
    }
    // every material it names (slots, lamp posts, trees, the reserved fields)
    let mats = 0, engine = 0;
    for (const m of kit.matchAll(/\{fileID:\s*2100000,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*2\}/g)) {
      mats++;
      const mf = matByGuid.get(m[1]);
      if (!mf || !fs.existsSync(mf)) { unread.push("material " + m[1] + " (no .mat in the source)"); continue; }
      const sm = /m_Shader:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32})/.exec(fs.readFileSync(mf, "utf8"));
      const n = sm ? shaderByGuid.get(sm[2]) : null;
      if (n) reach(n, "materials");
      else if (sm && /^0{16}[ef]0{15}$/.test(sm[2])) engine++;    // the engine's own (built-in extra)
      else unread.push("material " + path.basename(mf) + (sm ? " on shader " + sm[2] + " (not a project .shader)" : " with no m_Shader"));
    }
    if (engine) console.log("  note " + engine + " city kit material(s) use an engine shader - not nameable from the source, not checked here");
    const wantKit = [...kitNames.keys()].sort();
    if (unread.length) fail("the city kit names " + unread.length + " thing(s) this check cannot resolve to a project shader: " +
                            unread.slice(0, 8).join("; ") + (unread.length > 8 ? "; ..." : ""));
    if (!wantKit.length) fail("the city kit (" + mats + " materials) resolves to no shader at all");
    else if (shaderObjs) {
      const missingKit = wantKit.filter(n => !own.has(n));
      if (missingKit.length)
        fail(missingKit.length + " city kit shader(s) are NOT in the player - the city, its trees or its lamp posts draw pink there: " +
             missingKit.join(", ") + " (is Resources/CityKit.asset in the player? see the Resources lines above)");
      else ok("every city kit shader is in the player: " + wantKit.length + " (" +
              wantKit.map(n => n + " via " + [...kitNames.get(n)].join("+")).join(", ") + "; " + mats + " kit materials)");
    }
    if (reportKitNone) fail("the build says " + reportKitNone.trim() + ", and " + edition + " must ship the city kit");
    else if (!reportKit.length)
      console.log("  note the build report lists no kit shaders (a build from before the kit check) - the player check above stands alone");
    else if (reportKit.join("\n") !== wantKit.join("\n"))
      fail("the build's kit-shader list and this read of the kit disagree - build: " + reportKit.join(", ") + "; here: " +
           wantKit.join(", ") + " (RuntimeShaders.ScanKit and tools/webgl-contents.mjs must read one kit; or the kit moved since the build)");
    else ok("the build's kit-shader list is this read of the kit's (" + reportKit.length + ")");
  }
}

console.log(bad ? "WEBGL CONTENTS FAILED (" + bad + ")" : "WEBGL CONTENTS OK - " + edition);
process.exit(bad ? 1 : 0);
