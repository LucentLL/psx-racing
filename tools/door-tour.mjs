// THE DOORS, OPENED IN THE REAL PLAYER - the proof the editor cannot give.
//
//   node tools/door-tour.mjs <Build/WebGL> [--shots <dir>] [--size 960x540] [--minutes 30] [--chrome <exe>]
//
// Since the editions every door finds its scene through
// TrackCatalog.BuildIndexOfScene, and in a PLAYER that is SceneUtility - a
// branch no editor check runs (review, 2026-09-29). This serves a built player
// on 127.0.0.1 (no python, no npm), opens it in headless Chrome with
// ?doortour, and reads the console:
//   * "[Doors] OK|FAIL ..."   DoorAudit, at boot: every door of the edition
//                             resolved against the player's own scene list;
//   * "[DoorTour] PASS|FAIL|SKIP ..." one line per door, walked through the
//                             front end's own buttons and hops (DoorTour.cs);
//   * "[DoorTour] ARRIVED ..." a screenshot is taken there (--shots);
//   * "[DoorTour] DONE ..."   the end.
// Exit 0 only when the audit says OK, the tour says DONE with 0 failed, and
// the edition the player reports is the one psx-edition.txt names.
// The tour is inert on the site (DoorTour.RequestedBy: local hosts only), and
// Chrome runs on a throwaway profile, so the career it seeds dies with it.
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
const minutes = Number(opt("--minutes", "30"));
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
const url = `http://127.0.0.1:${server.address().port}/index.html?doortour=1`;

// ---- headless Chrome over the DevTools protocol ----
const dbgPort = 9400 + Math.floor(Math.random() * 500);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), "doortour-"));
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
let audit = null, done = null, begin = null;
const verdicts = [];
let shotQueue = Promise.resolve();
let shotN = 0;

function onConsole(raw) {
  lines.push(raw);
  // Unity may put something before the message or a stack after it.
  let text = raw;
  const at = text.indexOf("[Door");
  if (at > 0) text = text.slice(at);
  text = text.split(/\r?\n/)[0];
  if (/^\[Doors\] /.test(text)) { audit = text; console.log(text); }
  else if (/^\[DoorTour\] BEGIN /.test(text)) { begin = text; console.log(text); }
  else if (/^\[DoorTour\] (PASS|FAIL|SKIP) /.test(text)) { verdicts.push(text); console.log(text); }
  else if (/^\[DoorTour\] DONE /.test(text)) { done = text; console.log(text); }
  else if (/^\[DoorTour\] ARRIVED /.test(text) && shots) {
    const label = text.replace(/^\[DoorTour\] ARRIVED /, "").replace(/[^A-Za-z0-9]+/g, "_").replace(/^_+|_+$/g, "");
    const name = `door_${String(++shotN).padStart(2, "0")}_${label}.png`;
    shotQueue = shotQueue.then(async () => {
      const r = await send("Page.captureScreenshot", { format: "png" });
      fs.writeFileSync(path.join(shots, name), Buffer.from(r.data, "base64"));
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
  console.log(`door tour: ${dir} (${builtAs || "no psx-edition.txt"}) at ${url}`);
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
  if (shots) {
    const r = await send("Page.captureScreenshot", { format: "png" });
    fs.writeFileSync(path.join(shots, "door_zz_home_after.png"), Buffer.from(r.data, "base64"));
  }

  const failed = verdicts.filter(v => /^\[DoorTour\] FAIL /.test(v)).length;
  const problems = [];
  if (!audit) problems.push("no [Doors] line - DoorAudit never ran (a build from before it?)");
  else if (!/^\[Doors\] OK /.test(audit)) problems.push("the door audit failed");
  if (!begin) problems.push("the tour never began (is DoorTour in this build? was the URL local?)");
  else if (builtAs && !begin.includes(` ${builtAs} `)) problems.push(`the player tours as '${begin}', psx-edition.txt says ${builtAs}`);
  if (!done) problems.push(`no DONE within ${minutes} min (${verdicts.length} doors reported)`);
  else if (!/ 0 failed,/.test(done)) problems.push(done.replace(/^\[DoorTour\] /, ""));
  if (failed && done && / 0 failed,/.test(done)) problems.push(`${failed} FAIL line(s)`);
  if (problems.length) {
    console.log("DOOR TOUR FAILED - " + problems.join("; "));
    const errs = lines.filter(l => /exception|error/i.test(l) && !/^\[Door/.test(l)).slice(-8);
    if (errs.length) console.log("console errors:\n  " + errs.map(l => l.slice(0, 220)).join("\n  "));
  } else {
    console.log(`DOOR TOUR OK - ${verdicts.length} doors, ${builtAs || "edition from the player"}`);
    code = 0;
  }
} catch (e) {
  console.log("DOOR TOUR ERROR " + e.message);
} finally {
  try { ws && ws.close(); } catch { }
  proc.kill();
  server.close();
  await sleep(800);
  try { fs.rmSync(profile, { recursive: true, force: true }); } catch { }
  process.exit(code);
}
