// THE PLAYER'S OWN FRAMES - the colour harness's driver (plan C0b).
//
//   node tools/colour/shot-link.mjs <Build/WebGL-SHOT> --manifest shots.json --out <dir>
//        [--size 852x480] [--minutes 30] [--chrome <exe>] [--swiftshader]
//   node tools/colour/shot-link.mjs <Build/WebGL-SHOT> --serve-only --port 8137 --manifest shots.json --out <dir>
//
// Serves a DEVELOPMENT WebGL player (tools\colour\harness-build.ps1) on
// 127.0.0.1, opens it with ?psxshots=shots.json and lets Scripts/Dev/
// ShotLink.cs stand the car on each protocol spot and capture its own final
// frame. Each capture is POSTed back to __psxshot?name=... and written to
// <out>/<name>.png with a sidecar <name>.png.json (source "player", the
// WebGL renderer string, the viewport, and the player's STATE line: hour,
// weather, dress, grade, lens, lines, lamps, the probe texture's format).
//
// Headless mode runs Chrome headless on this PC's GPU (ANGLE/D3D11 - the
// renderer string goes in every sidecar); --swiftshader forces the CPU
// rasteriser instead (a code or so off a phone GPU). The default viewport,
// 852x480, is the editor frames' own size, so the player's 480-line
// framebuffer lands on the canvas 1:1 and the two compare pixel for pixel.
// --serve-only just serves and receives, for
// a real-GPU browser (the Browser pane) pointed at
//   http://127.0.0.1:<port>/index.html?psxshots=shots.json
// and stops when a POST /__psxdone arrives or on Ctrl+C.
// Exit 0 when every shot in the manifest arrived.
import { spawn } from "child_process";
import fs from "fs";
import http from "http";
import os from "os";
import path from "path";

const args = process.argv.slice(2);
const valued = new Set(["--manifest", "--out", "--size", "--minutes", "--chrome", "--port"]);
const dir = path.resolve(args.find((a, i) => !a.startsWith("--") && !valued.has(args[i - 1])) || ".");
const opt = (k, d) => (args.indexOf(k) >= 0 ? args[args.indexOf(k) + 1] : d);
const serveOnly = args.includes("--serve-only");
const manifest = path.resolve(opt("--manifest", "shots.json"));
const out = path.resolve(opt("--out", "player_shots"));
const [W, H] = opt("--size", "852x480").split("x").map(Number);
const swift = args.includes("--swiftshader");
const minutes = Number(opt("--minutes", "30"));
const port = Number(opt("--port", "0"));
const chromeExe = opt("--chrome", [
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
].find(p => fs.existsSync(p)) || "");

if (!fs.existsSync(path.join(dir, "index.html"))) { console.log(`NO PLAYER - ${dir} has no index.html`); process.exit(1); }
if (!fs.existsSync(manifest)) { console.log(`NO MANIFEST - ${manifest}`); process.exit(1); }
const want = JSON.parse(fs.readFileSync(manifest, "utf8")).shots.map(s => s.name);
fs.mkdirSync(out, { recursive: true });

const got = new Map();       // name -> file
const states = new Map();    // name -> STATE json
let renderer = serveOnly ? "real GPU (served; see the browser)" : "";
let doneLine = null;

function writeSidecar(name) {
  const file = path.join(out, name + ".png");
  if (!fs.existsSync(file)) return;
  let st = null;
  try { st = states.has(name) ? JSON.parse(states.get(name)) : null; } catch { st = states.get(name); }
  const sc = { file: name + ".png", source: "player", activeBuildTarget: "WebGL-player", renderer,
    viewport: `${W}x${H}`, headless: !serveOnly, state: st, written: new Date().toISOString() };
  fs.writeFileSync(file + ".json", JSON.stringify(sc, null, 1));
}

// ---- a static server, localhost only, that also receives the frames ----
const types = { ".html": "text/html", ".js": "application/javascript", ".wasm": "application/wasm",
  ".unityweb": "application/octet-stream", ".data": "application/octet-stream", ".json": "application/json",
  ".png": "image/png", ".jpg": "image/jpeg", ".ico": "image/x-icon", ".css": "text/css", ".txt": "text/plain" };
const server = http.createServer((req, res) => {
  const u = new URL(req.url, "http://x");
  if (req.method === "POST" && u.pathname.endsWith("/__psxshot")) {
    const name = (u.searchParams.get("name") || "shot").replace(/[^A-Za-z0-9_.-]+/g, "_");
    const chunks = [];
    req.on("data", c => chunks.push(c));
    req.on("end", () => {
      const file = path.join(out, name + ".png");
      fs.writeFileSync(file, Buffer.concat(chunks));
      got.set(name, file);
      writeSidecar(name);
      console.log(`RECEIVED ${name}.png (${Buffer.concat(chunks).length} bytes)`);
      res.writeHead(200, { "Content-Type": "text/plain" }); res.end("ok");
    });
    return;
  }
  if (req.method === "POST" && u.pathname.endsWith("/__psxstate")) {
    const chunks = [];
    req.on("data", c => chunks.push(c));
    req.on("end", () => { const t = Buffer.concat(chunks).toString(); const sp = t.indexOf(" ");
      if (sp > 0) { states.set(t.slice(0, sp), t.slice(sp + 1)); writeSidecar(t.slice(0, sp)); } res.writeHead(200); res.end("ok"); });
    return;
  }
  if (req.method === "POST" && u.pathname.endsWith("/__psxdone")) {
    doneLine = "served: done posted"; res.writeHead(200); res.end("ok"); return;
  }
  let rel = decodeURIComponent(u.pathname).replace(/^\/+/, "") || "index.html";
  let file = path.resolve(dir, rel);
  if (rel === path.basename(manifest)) file = manifest;
  if (!(file === manifest || file.startsWith(dir)) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { "Content-Type": types[path.extname(file).toLowerCase()] || "application/octet-stream", "Cache-Control": "no-store" });
  fs.createReadStream(file).pipe(res);
});
await new Promise(r => server.listen(port, "127.0.0.1", r));
const url = `http://127.0.0.1:${server.address().port}/index.html?psxshots=${encodeURIComponent(path.basename(manifest))}`;
const sleep = ms => new Promise(r => setTimeout(r, ms));

if (serveOnly) {
  console.log(`SERVING ${dir}\n  open ${url}\n  frames -> ${out}`);
  const deadline = Date.now() + minutes * 60000;
  while (Date.now() < deadline && !doneLine && !want.every(n => got.has(n))) await sleep(1000);
  await sleep(1500);
  const missing = want.filter(n => !got.has(n));
  console.log(missing.length ? `SHOT LINK: ${got.size}/${want.length} frames; missing ${missing.join(", ")}` : `SHOT LINK OK - ${got.size} frames`);
  server.close();
  process.exit(missing.length ? 1 : 0);
}

// ---- headless Chrome over the DevTools protocol ----
if (!chromeExe || !fs.existsSync(chromeExe)) { console.log("NO BROWSER - pass --chrome <chrome.exe or msedge.exe>"); process.exit(1); }
const dbgPort = 9400 + Math.floor(Math.random() * 500);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), "shotlink-"));
const proc = spawn(chromeExe, [
  "--headless=new", `--remote-debugging-port=${dbgPort}`, `--user-data-dir=${profile}`,
  `--window-size=${W},${H}`, "--no-first-run", "--no-default-browser-check",
  "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--autoplay-policy=no-user-gesture-required",
  ...(swift ? ["--use-angle=swiftshader", "--use-gl=angle"] : []),
  "about:blank"], { stdio: "ignore" });
let ws, nextId = 1;
const pending = new Map();
const send = (method, params = {}) => {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((res, rej) => pending.set(id, { res, rej }));
};
const lines = [];
function onConsole(raw) {
  lines.push(raw);
  let text = raw;
  const at = text.indexOf("[ShotLink]");
  if (at < 0) return;
  text = text.slice(at).split(/\r?\n/)[0];
  console.log(text.length > 400 ? text.slice(0, 400) + "..." : text);
  const m = /^\[ShotLink\] STATE (\S+) (\{.*\})$/.exec(text);
  if (m) { states.set(m[1], m[2]); writeSidecar(m[1]); }
  if (/^\[ShotLink\] DONE /.test(text)) doneLine = text;
}

let code = 1;
try {
  let target;
  for (let i = 0; i < 60 && !target; i++) {
    try { target = (await (await fetch(`http://127.0.0.1:${dbgPort}/json/list`)).json()).find(t => t.type === "page"); } catch { }
    if (!target) await sleep(500);
  }
  if (!target) throw new Error("the browser never opened a page");
  ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise(r => ws.addEventListener("open", r));
  ws.addEventListener("message", ev => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.rej(new Error(JSON.stringify(m.error))) : p.res(m.result); }
    if (m.method === "Runtime.consoleAPICalled") onConsole(m.params.args.map(a => a.value ?? a.description ?? "").join(" "));
    if (m.method === "Runtime.exceptionThrown") lines.push("EXCEPTION " + (m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text || ""));
  });
  await send("Runtime.enable");
  await send("Page.enable");
  await send("Emulation.setDeviceMetricsOverride", { width: W, height: H, deviceScaleFactor: 1, mobile: false });
  await send("Emulation.setFocusEmulationEnabled", { enabled: true });
  console.log(`shot link: ${dir} at ${url}`);
  await send("Page.navigate", { url });
  let started = false;
  for (let i = 0; i < 600 && !started; i++) {
    const r = await send("Runtime.evaluate", { expression: "(() => { const b = document.getElementById('play'); return !!b && b.offsetParent !== null; })()", returnByValue: true });
    if (r.result.value) {
      await send("Runtime.evaluate", { expression: "document.getElementById('play').click()", userGesture: true });
      started = true;
    } else await sleep(500);
  }
  if (!started) throw new Error("the page never offered START (the player did not load)");
  try {
    const r = await send("Runtime.evaluate", { returnByValue: true, expression:
      "(() => { const c = document.querySelector('canvas'); const gl = c && (c.getContext('webgl2') || c.getContext('webgl')); if (!gl) return 'no context'; const e = gl.getExtension('WEBGL_debug_renderer_info'); return e ? gl.getParameter(e.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER); })()" });
    renderer = r.result.value || "";
    console.log("renderer: " + renderer);
  } catch { }
  const deadline = Date.now() + minutes * 60000;
  while (!doneLine && Date.now() < deadline) await sleep(1000);
  await sleep(2000);
  for (const n of got.keys()) writeSidecar(n);
  const missing = want.filter(n => !got.has(n));
  if (!doneLine) console.log(`no DONE within ${minutes} min`);
  if (missing.length) {
    console.log(`SHOT LINK FAILED - ${got.size}/${want.length} frames; missing ${missing.join(", ")}`);
    const errs = lines.filter(l => /exception|error/i.test(l) && !/\[ShotLink\]/.test(l)).slice(-10);
    if (errs.length) console.log("console errors:\n  " + errs.map(l => l.slice(0, 240)).join("\n  "));
  } else { console.log(`SHOT LINK OK - ${got.size} frames (${renderer})`); code = 0; }
  fs.writeFileSync(path.join(out, "console.txt"), lines.join("\n"));
} catch (e) {
  console.log("SHOT LINK ERROR " + e.message);
} finally {
  try { ws && ws.close(); } catch { }
  proc.kill();
  server.close();
  await sleep(800);
  try { fs.rmSync(profile, { recursive: true, force: true }); } catch { }
  process.exit(code);
}
