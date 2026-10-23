// THE REFLECTION MAP, CHECKED IN THE REAL PLAYER (ReflTour.cs) - the WebGL
// half of ReflectionMapCheck, which only ever ran in the editor.
//
//   node tools/refl-tour.mjs <Build/WebGL> [--shots <dir>] [--size 960x540] [--minutes 6] [--chrome <exe>]
//
// Serves a built player on 127.0.0.1, opens it in headless Chrome with
// ?refltour and reads the console: "[ReflTour] ok|FAIL ..." per check (the
// panorama against a CPU trace, a PSX/CarPaint ball against the paint's own
// read), "[ReflTour] SHOW" (a screenshot of both pictures, --shots) and
// "[ReflTour] DONE PASS|FAIL". Exit 0 only on DONE PASS.
import { spawn } from "child_process";
import fs from "fs";
import http from "http";
import os from "os";
import path from "path";

const args = process.argv.slice(2);
const valued = new Set(["--shots", "--size", "--minutes", "--chrome"]);
const dir = path.resolve(args.find((a, i) => !a.startsWith("--") && !valued.has(args[i - 1])) || ".");
const opt = (k, d) => (args.indexOf(k) >= 0 ? args[args.indexOf(k) + 1] : d);
const shots = opt("--shots", "");
const [W, H] = opt("--size", "960x540").split("x").map(Number);
const minutes = Number(opt("--minutes", "6"));
const chromeExe = opt("--chrome", [
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
].find(p => fs.existsSync(p)) || "");

if (!fs.existsSync(path.join(dir, "index.html"))) { console.log(`NO PLAYER - ${dir} has no index.html`); process.exit(1); }
if (!chromeExe || !fs.existsSync(chromeExe)) { console.log("NO BROWSER - pass --chrome <chrome.exe or msedge.exe>"); process.exit(1); }
const edFile = path.join(dir, "psx-edition.txt");
const builtAs = fs.existsSync(edFile) ? fs.readFileSync(edFile, "utf8").split(/\r?\n/)[0].trim() : "";
if (shots) fs.mkdirSync(shots, { recursive: true });

// ---- a static server, localhost only ----
const types = { ".html": "text/html", ".js": "application/javascript", ".wasm": "application/wasm",
  ".unityweb": "application/octet-stream", ".data": "application/octet-stream", ".json": "application/json",
  ".png": "image/png", ".jpg": "image/jpeg", ".ico": "image/x-icon", ".css": "text/css", ".txt": "text/plain" };
const server = http.createServer((req, res) => {
  const rel = decodeURIComponent(new URL(req.url, "http://x").pathname).replace(/^\/+/, "") || "index.html";
  const file = path.resolve(dir, rel);
  if (!file.startsWith(dir) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { "Content-Type": types[path.extname(file).toLowerCase()] || "application/octet-stream", "Cache-Control": "no-store" });
  fs.createReadStream(file).pipe(res);
});
await new Promise(r => server.listen(0, "127.0.0.1", r));
const url = `http://127.0.0.1:${server.address().port}/index.html?refltour=1`;

// ---- headless Chrome over the DevTools protocol ----
const dbgPort = 9400 + Math.floor(Math.random() * 500);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), "refltour-"));
const proc = spawn(chromeExe, [
  "--headless=new", `--remote-debugging-port=${dbgPort}`, `--user-data-dir=${profile}`,
  `--window-size=${W},${H}`, "--no-first-run", "--no-default-browser-check",
  "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--autoplay-policy=no-user-gesture-required",
  "about:blank"], { stdio: "ignore" });
const sleep = ms => new Promise(r => setTimeout(r, ms));
let ws, nextId = 1;
const pending = new Map();
const send = (method, params = {}) => {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((res, rej) => pending.set(id, { res, rej }));
};

const lines = [];          // every console line
let done = null, begin = null;
const verdicts = [];
let shotQueue = Promise.resolve();

function onConsole(raw) {
  lines.push(raw);
  let text = raw;
  const at = text.indexOf("[ReflTour]");
  if (at > 0) text = text.slice(at);
  text = text.split(/\r?\n/)[0];
  if (!/^\[ReflTour\] /.test(text)) return;
  console.log(text);
  if (/^\[ReflTour\] BEGIN /.test(text)) begin = text;
  else if (/^\[ReflTour\] (ok|FAIL) /.test(text)) verdicts.push(text);
  else if (/^\[ReflTour\] DONE /.test(text)) done = text;
  else if (/^\[ReflTour\] SHOW/.test(text) && shots) {
    shotQueue = shotQueue.then(async () => {
      await sleep(1200);
      const r = await send("Page.captureScreenshot", { format: "png" });
      fs.writeFileSync(path.join(shots, "refltour_show.png"), Buffer.from(r.data, "base64"));
    }).catch(() => { });
  }
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
  // A headless tab is never focused, and the template veils an unfocused game.
  await send("Emulation.setFocusEmulationEnabled", { enabled: true });
  console.log(`refl tour: ${dir} (${builtAs || "no psx-edition.txt"}) at ${url}`);
  await send("Page.navigate", { url });
  // The template's START appears when the data has loaded.
  let started = false;
  for (let i = 0; i < 600 && !started; i++) {
    const r = await send("Runtime.evaluate", { expression: "(() => { const b = document.getElementById('play'); return !!b && b.offsetParent !== null; })()", returnByValue: true });
    if (r.result.value) {
      await send("Runtime.evaluate", { expression: "document.getElementById('play').click()", userGesture: true });
      started = true;
    } else await sleep(500);
  }
  if (!started) throw new Error("the page never offered START (the player did not load)");
  const deadline = Date.now() + minutes * 60000;
  while (!done && Date.now() < deadline) await sleep(1000);
  await sleep(1500);
  await shotQueue;
  const problems = [];
  if (!begin) problems.push("the check never began (is ReflTour in this build? was the URL local?)");
  if (!done) problems.push(`no DONE within ${minutes} min (${verdicts.length} checks reported)`);
  else if (!/ PASS$/.test(done)) problems.push(done.replace(/^\[ReflTour\] /, ""));
  if (problems.length) {
    console.log("REFL TOUR FAILED - " + problems.join("; "));
    const errs = lines.filter(l => /exception|error/i.test(l) && !/^\[ReflTour/.test(l)).slice(-8);
    if (errs.length) console.log("console errors:\n  " + errs.map(l => l.slice(0, 220)).join("\n  "));
  } else {
    console.log(`REFL TOUR OK - ${verdicts.length} checks`);
    code = 0;
  }
} catch (e) {
  console.log("REFL TOUR ERROR " + e.message);
} finally {
  try { ws && ws.close(); } catch { }
  proc.kill();
  server.close();
  await sleep(800);
  try { fs.rmSync(profile, { recursive: true, force: true }); } catch { }
  process.exit(code);
}
