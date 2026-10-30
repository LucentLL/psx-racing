// THE FREE-ROAM PERF TOUR, IN A REAL BROWSER (city perf round 1, 2026-10-30).
//
//   node tools/perf-tour.mjs <Build/WebGL> --browser chrome|firefox --out <dir>
//        [--routes uptown,tryon,trade,independence,i77s] [--secs 150] [--hour Noon]
//        [--tag name] [--size 1280x720] [--minutes 45] [--ab Class.Field:0,...]
//        [--shots "x,z,yaw,name;..."] [--traffic 0] [--view chase] [--snap 10]
//
// Serves a built player on 127.0.0.1 and opens it in a VISIBLE browser window
// (a throwaway profile, the PC's own GPU, vsync - the owner's setup) with
// ?perftour. Scripts/Dev/PerfTour.cs seeds a career, enters FREE ROAM through
// the front end's own hop and drives each route with the race AI, every frame
// timed by Scripts/Dev/PerfClock.cs; it POSTs each route's frames CSV, spike
// list and summary to __psxperf?name=..., written here to <out>. Ends on
// <tag>_done.txt or the time limit; the browser is closed by its profile path.
// Chrome: occlusion / background throttling off (a covered window keeps its
// frame rate). Firefox: a user.js with autoplay allowed, occlusion tracking
// off, no first-run pages. Read the CSVs with tools/perf_tour_report.py.
import { spawn, execFileSync } from "child_process";
import fs from "fs";
import http from "http";
import os from "os";
import path from "path";

const args = process.argv.slice(2);
const valued = new Set(["--browser", "--out", "--routes", "--secs", "--hour", "--tag", "--size", "--minutes", "--ab", "--shots", "--traffic", "--view", "--exe", "--snap"]);
const dir = path.resolve(args.find((a, i) => !a.startsWith("--") && !valued.has(args[i - 1])) || ".");
const opt = (k, d) => (args.indexOf(k) >= 0 ? args[args.indexOf(k) + 1] : d);
const browser = opt("--browser", "chrome").toLowerCase();
const out = path.resolve(opt("--out", "perf_tour"));
const tag = opt("--tag", browser);
const [W, H] = opt("--size", "1280x720").split("x").map(Number);
const minutes = Number(opt("--minutes", "45"));
const q = new URLSearchParams({ perftour: "1", tag });
for (const [k, v] of [["routes", opt("--routes", "")], ["secs", opt("--secs", "")], ["hour", opt("--hour", "")],
                      ["ab", opt("--ab", "")], ["shots", opt("--shots", "")], ["traffic", opt("--traffic", "")], ["view", opt("--view", "")],
                      ["snap", opt("--snap", "")]])
  if (v) q.set(k, v);

const exes = {
  chrome: ["C:/Program Files/Google/Chrome/Application/chrome.exe", "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe"],
  firefox: ["C:/Program Files/Mozilla Firefox/firefox.exe", "C:/Program Files (x86)/Mozilla Firefox/firefox.exe"],
};
const exe = opt("--exe", (exes[browser] || []).find(p => fs.existsSync(p)) || "");
if (!fs.existsSync(path.join(dir, "index.html"))) { console.log(`NO PLAYER - ${dir} has no index.html`); process.exit(1); }
if (!exe || !fs.existsSync(exe)) { console.log(`NO BROWSER - ${browser} not found (pass --exe)`); process.exit(1); }
fs.mkdirSync(out, { recursive: true });

let done = null;
const got = [];
const types = { ".html": "text/html", ".js": "application/javascript", ".wasm": "application/wasm",
  ".unityweb": "application/octet-stream", ".data": "application/octet-stream", ".json": "application/json",
  ".png": "image/png", ".jpg": "image/jpeg", ".ico": "image/x-icon", ".css": "text/css", ".txt": "text/plain",
  ".bundle": "application/octet-stream" };
const server = http.createServer((req, res) => {
  const u = new URL(req.url, "http://x");
  if (req.method === "POST" && u.pathname.endsWith("/__psxperf")) {
    const name = (u.searchParams.get("name") || "perf").replace(/[^A-Za-z0-9_.-]+/g, "_");
    const chunks = [];
    req.on("data", c => chunks.push(c));
    req.on("end", () => {
      const buf = Buffer.concat(chunks);
      fs.writeFileSync(path.join(out, name), buf);
      got.push(name);
      console.log(`RECEIVED ${name} (${buf.length} bytes)`);
      if (/_summary\.txt$|_env\.txt$/.test(name)) console.log(buf.toString().trim().split(/\r?\n/).map(l => "  " + l).join("\n"));
      if (/_done\.txt$/.test(name)) done = buf.toString().trim();
      res.writeHead(200, { "Content-Type": "text/plain" }); res.end("ok");
    });
    return;
  }
  const rel = decodeURIComponent(u.pathname).replace(/^\/+/, "") || "index.html";
  const file = path.resolve(dir, rel);
  if (!file.startsWith(dir) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { "Content-Type": types[path.extname(file).toLowerCase()] || "application/octet-stream", "Cache-Control": "no-store" });
  fs.createReadStream(file).pipe(res);
});
await new Promise(r => server.listen(0, "127.0.0.1", r));
const url = `http://127.0.0.1:${server.address().port}/index.html?${q.toString()}`;
const sleep = ms => new Promise(r => setTimeout(r, ms));

const profile = fs.mkdtempSync(path.join(os.tmpdir(), `perftour-${browser}-`));
let argv;
if (browser === "firefox") {
  const prefs = {
    "browser.shell.checkDefaultBrowser": false, "browser.startup.homepage_override.mstone": "ignore",
    "browser.aboutwelcome.enabled": false, "startup.homepage_welcome_url": "", "startup.homepage_welcome_url.additional": "",
    "browser.startup.firstrunSkipsHomepage": true, "datareporting.policy.dataSubmissionEnabled": false,
    "toolkit.telemetry.reportingpolicy.firstRun": false, "datareporting.healthreport.uploadEnabled": false,
    "app.update.auto": false, "app.update.enabled": false, "browser.sessionstore.resume_from_crash": false,
    "media.autoplay.default": 0, "media.autoplay.blocking_policy": 0,
    "widget.windows.window_occlusion_tracking.enabled": false, "browser.tabs.warnOnClose": false,
    "browser.translations.automaticallyPopup": false, "extensions.pocket.enabled": false,
  };
  fs.writeFileSync(path.join(profile, "user.js"), Object.entries(prefs).map(([k, v]) => `user_pref(${JSON.stringify(k)}, ${JSON.stringify(v)});`).join("\n") + "\n");
  argv = ["-no-remote", "-new-instance", "-profile", profile, "-width", String(W), "-height", String(H), url];
} else {
  argv = [`--user-data-dir=${profile}`, "--no-first-run", "--no-default-browser-check", `--window-size=${W},${H}`, "--window-position=40,40",
    "--autoplay-policy=no-user-gesture-required", "--disable-features=CalculateNativeWinOcclusion,Translate",
    "--disable-backgrounding-occluded-windows", "--disable-renderer-backgrounding", "--disable-background-timer-throttling",
    "--new-window", url];
}
console.log(`perf tour: ${browser} ${exe}\n  ${url}\n  -> ${out}`);
const proc = spawn(exe, argv, { stdio: "ignore", detached: false });

function killByProfile() {
  const name = browser === "firefox" ? "firefox.exe" : "chrome.exe";
  const ps = `Get-CimInstance Win32_Process -Filter "Name='${name}'" | Where-Object { $_.CommandLine -like '*${path.basename(profile)}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`;
  try { execFileSync("powershell", ["-NoProfile", "-Command", ps], { stdio: "ignore" }); } catch { }
}

let code = 1;
try {
  const deadline = Date.now() + minutes * 60000;
  while (!done && Date.now() < deadline) await sleep(1000);
  await sleep(1500);
  if (done && /^DONE/.test(done)) { console.log(`PERF TOUR OK - ${got.length} files in ${out}`); code = 0; }
  else console.log(`PERF TOUR ${done ? "FAILED - " + done : "TIMED OUT after " + minutes + " min"} (${got.length} files)`);
} finally {
  try { proc.kill(); } catch { }
  killByProfile();
  server.close();
  await sleep(1500);
  try { fs.rmSync(profile, { recursive: true, force: true }); } catch { }
  process.exit(code);
}
