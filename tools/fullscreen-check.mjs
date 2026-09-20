// Run Assets/Plugins/WebGL/PSXFullscreen.jslib against a fake DOM.
//
//   node tools\fullscreen-check.mjs
//
// A .jslib is the one file in this project that NOTHING COMPILES. Unity pastes
// it into the emscripten output verbatim, so a typo, a wrong branch or an
// unhandled promise rejection surfaces as a dead button on somebody's phone —
// forty minutes of build and one deploy after it was written, with no log to
// read. Five seconds here instead.
//
// Emscripten's library system is small enough to stand in for: mergeInto,
// autoAddDeps, and the $-prefixed state object hoisted to a module global of
// the same name without the $. The browsers below are the four that matter:
// one that can do fullscreen, an iPhone (element fullscreen is <video>-only
// there), an embed that has not been given the permission, and one that says
// no to the request after saying yes to the call.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const src = fs.readFileSync(
  path.join(here, "..", "Assets", "Plugins", "WebGL", "PSXFullscreen.jslib"), "utf8");

function load(dom) {
  const library = {};
  const sandbox = {
    mergeInto: (lib, obj) => Object.assign(lib, obj),
    autoAddDeps: () => {},
    LibraryManager: { library },
    ...dom,
  };
  const keys = Object.keys(sandbox);
  // `PSXFS` is a parameter so the library functions have something to close
  // over: emscripten hoists the $-prefixed member to exactly that global, and
  // it does so before anything can call them, which is what this imitates.
  new Function(...keys, "PSXFS", src + "\n; PSXFS = PSXFullscreenLib.$PSXFS;")(
    ...keys.map((k) => sandbox[k]), undefined);
  return library;
}

let fails = 0;
const ok = (cond, what) => {
  console.log((cond ? "  ok   " : "  FAIL ") + what);
  if (!cond) fails++;
};

// ---- a browser that can do it -------------------------------------------
const listeners = {};
const store = {};
let fsElement = null;
let requested = null, exited = false, locked = null;

const root = {
  requestFullscreen(opts) { requested = opts; fsElement = root; return Promise.resolve(); },
};
const doc = {
  fullscreenEnabled: true,
  get fullscreenElement() { return fsElement; },
  documentElement: root,
  addEventListener(k, fn) { (listeners[k] = listeners[k] || []).push(fn); },
  exitFullscreen() { exited = true; fsElement = null; return Promise.resolve(); },
};
const chrome = {
  document: doc,
  screen: { orientation: { lock: (o) => { locked = o; return Promise.resolve(); } } },
  localStorage: {
    getItem: (k) => (k in store ? store[k] : null),
    setItem: (k, v) => { store[k] = String(v); },
  },
  window: { dispatchEvent() {} },
  Event: class { constructor(t) { this.type = t; } },
  setTimeout: (fn) => fn(),
};

let lib = load(chrome);
ok(lib.PSXFullscreenSupported() === 1, "chrome: supported");
ok(lib.PSXFullscreenActive() === 0, "chrome: not fullscreen to begin with");
ok(lib.PSXFullscreenSet(1) === 1, "chrome: the request is made");
ok(lib.PSXFullscreenActive() === 1, "chrome: fullscreen once it settles");
ok(store["psx.fullscreen"] === "1", "chrome: a yes is remembered for the template");
ok(requested && requested.navigationUI === "hide", "chrome: asks for no navigation UI");
ok(listeners.fullscreenchange && listeners.fullscreenchange.length === 1,
   "chrome: one resize hook, installed on the first call");
lib.PSXFullscreenSet(1);
ok(listeners.fullscreenchange.length === 1, "chrome: and not a second one");

await new Promise((r) => setImmediate(r));   // let the lock promise run
ok(locked === "landscape", "chrome: landscape locked once fullscreen is granted");

ok(lib.PSXFullscreenSet(0) === 1, "chrome: the exit is made");
ok(exited && lib.PSXFullscreenActive() === 0, "chrome: out of fullscreen");
ok(store["psx.fullscreen"] === "0", "chrome: a NO is remembered too — the template reads this");

// ---- an iPhone: element fullscreen does not exist ------------------------
lib = load({ ...chrome, document: { documentElement: {}, addEventListener() {} } });
ok(lib.PSXFullscreenSupported() === 0, "iphone: unsupported, so no row is offered");
ok(lib.PSXFullscreenActive() === 0, "iphone: never reads as fullscreen");
ok(lib.PSXFullscreenSet(1) === 0, "iphone: a request says plainly that it failed");

// ---- an iframe with no fullscreen permission -----------------------------
lib = load({ ...chrome, document: { ...doc, fullscreenEnabled: false } });
ok(lib.PSXFullscreenSupported() === 0, "embed: fullscreenEnabled false is respected");

// ---- private mode: localStorage throws -----------------------------------
lib = load({
  ...chrome,
  localStorage: { getItem() { throw new Error("denied"); },
                  setItem() { throw new Error("denied"); } },
});
ok(lib.PSXFullscreenSet(1) === 1, "private mode: a dead localStorage does not stop fullscreen");

// ---- a browser that refuses after taking the call ------------------------
lib = load({
  ...chrome,
  document: { ...doc, documentElement: {
    requestFullscreen() { return Promise.reject(new Error("no")); } } },
});
ok(lib.PSXFullscreenSet(1) === 1, "refusal: the call itself does not throw");
let unhandled = null;
process.on("unhandledRejection", (e) => { unhandled = e; });
await new Promise((r) => setImmediate(r));
ok(unhandled === null, "refusal: the rejection is caught, not left to the page");

console.log(fails === 0 ? "\nFULLSCREEN JSLIB OK" : `\nFULLSCREEN JSLIB FAILED (${fails})`);
process.exit(fails === 0 ? 0 : 1);
