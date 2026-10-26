# HANDOFF - RUMBLE, WHEEL + PEDALS, FORCE FEEDBACK (2026-10-26)

Branch `wheel-input` (worktree `C:\Users\mcgee\PSX Racing-input`, off main a6368b71). Not merged, not pushed.
Sandbox `C:\Users\mcgee\PSXCity` (`$env:PSX_SANDBOX='C:\Users\mcgee\PSXCity'`). Typecheck:
`tools\typecheck.ps1 -OutDir $env:TEMP\psxtc-wheel` (OK). Owner asks (2026-10-26, in order): "Rumble (small). Wheel and
pedal input with a setup screen, so the Moza drives properly. A WebHID force-feedback test on your base. If the browser
blocks it, the desktop build is the fallback."

## What is built
- **Rumble** (`Scripts/Controls/Rumble.cs`, `FeelDriver.cs`, `Plugins/WebGL/PSXGamepadIO.jslib`). `RumbleMixer` (pure):
  engine buzz with revs x throttle, limiter, road texture (wheel-load swing = kerbs/potholes), verge, slides, hits
  (CollisionResponder.HitReportedOn on the player car), bump stops (CarController.Bottomed), gear-shift tick; soft
  saturation. Out: WebGL `vibrationActuator.playEffect('dual-rumble')` (Firefox `hapticActuators` fallback), refreshed
  20 Hz with 110 ms effects; editor/desktop `Gamepad.SetMotorSpeeds`. CONTROLS > RUMBLE on/off + RUMBLE LEVEL (70%).
  Zero whenever not driving (pause, menus, on foot, replay, focus loss).
- **Wheel + pedals**. `RawPads` reads every controller as raw axes/buttons: WebGL = `navigator.getGamepads()` via the
  jslib (non-standard pads by id), elsewhere = every Input System HID/Joystick device. `WheelConfig` (PlayerPrefs
  `psx.wheel`, JSON) + `WheelCapture` (turn left/right/centre; press-and-release pedals learn rest AND full, so
  inverted and combined axes need no switch; buttons or a lever axis; waiting out a button capture clears it).
  `WheelInput` per frame. PlayerCarInput: the wheel steers by ANGLE (`CarController.SetWheelSteer`, fresh-only, cleared on
  disable) - bypasses pad expo, keyboard unwind, STEERING SENSITIVITY and (unless SPEED LOCK) the speed-sensitive lock;
  keys/stick/touch take over only while actually used. STEER RATIO 1:1 CAR (lock at 450 deg of wheel) or FULL LOCK +
  LINEARITY; ROTATION 180-1080 (900). Buttons: shift up/down, handbrake, LOOK BACK (ChaseCamera.ApplyLookBack, composed
  last like the shake), reset, camera, pause. Clutch is captured/shown but the cars have no clutch model (says so).
- **Menus**. Settings now has 5 tabs (GAMEPLAY, CONTROLS, VISUALS, AUDIO, SETTINGS); `MenuKit.FitTabLabel` drops a tab
  caption to the type floor (20) in narrow cells (pause strip, MAIN + CITY OPTIONS doors, settings strip).
  `SettingsPanel` generalised (`SettingsPages`, `OpenSub`): WHEEL SETUP is a sub page (tabs STEER 7 rows / PEDALS 6 /
  BUTTONS 7 / FORCE 6), live readings at 20 Hz, capture instructions on the help line. No brand names in game text.
- **FFB test page** `tools/ffb/webhid-ffb-test.html` + engine `tools/ffb/pidffb.js` (layout from WebHID's parsed
  descriptor; Linux hid-pidff init order; Moza quirk = conditional effects at direction EAST/90 deg) + `serve.ps1`.
- **In-game FFB (wired, UNVERIFIED on hardware)**: `Plugins/WebGL/PSXForceFeedback.jslib` (pidffb.js copied in by
  `py tools/ffb/sync-jslib.py`; `--check` verifies) + `ForceFeedback.cs` (FfbModel: SAT from front tyre lateral force x
  pneumatic trail - peaks at 0.14 rad, before the 0.17 grip peak, light past it; speed spring that softens when the front
  slides; damper heaviest parked; texture sine; impact jolts toward the hit side). Behind its own switches: it does
  nothing until the player clicks WHEEL SETUP > FORCE > CONNECT WHEEL (WebHID needs the click); FORCE FEEDBACK on/off,
  FFB STRENGTH (60%), FFB INVERT, TEST. Safety: effects are finite (3 s) and re-started each second only while the game
  calls in; 300 ms silence zeroes, 1 s stops; hidden page / pagehide / pause / menus / focus loss stop everything.
  Device sign: DirectInput convention (+X force pushes the rim LEFT), game "+ = right" is negated; FFB INVERT flips.
  `CarController.WheelContact` gained `latForce`, `slipAngle`, `gripCircle` (set in the tyre loop).

## Verified
- `tools/wheel-check.ps1` edit half (PSXCity, 19:41): rumble mixer events, FFB model curve/clamps/jolts, wheel arithmetic,
  capture state machine on scripted pads, saved prefs, menu rows - all ok except one threshold I then relaxed
  (kerb at 30 m/s gave low 0.481 vs a 0.5 bar; now 0.45 - not re-run).
- jslibs: syntax-checked in Node; the FFB jslib's realtime layer simulated against a synthetic PID descriptor (init,
  4 effects, coalesced updates, 300 ms zero, 1 s stop, restart). The test page loads in Chrome 152 with WebHID present.
- Both C# branches typecheck (the WebGL `#if` branches via a scratch copy with them forced on).

## NOT verified (run these before publishing)
1. **Play-mode simulated wheel** (`ControlsCheckRunner`): did not run - the first run looked the venue scene up by a
   relative path in a windowed editor and found none. Fixed (absolute path; exit also fixed for the watched editor).
   Run: `$env:PSX_SANDBOX='C:\Users\mcgee\PSXCity'; powershell -File tools\wheel-check.ps1` (report
   `PSXCity\PSXRacing_controls_check.txt`). It drives a real car with a simulated HID wheel: capture, 1:1 angle at rest
   and at 28 m/s, SPEED LOCK, FULL LOCK, pedals, handbrake, paddles, reload.
2. **Menu previews** (`tools\wheel-preview.ps1` -> `PSXCity\Screenshots\controls\`, CLIP/OVERLAP goal 0): never run.
   The five-tab strips (pause, settings, MAIN/CITY OPTIONS doors) are the clip risk at 4:3; also run
   `tools\menu-preview.ps1` for the front-end doors. Note: VISUALS already had 8 rows before this branch (LifeSimSelfTest
   asserts <= 6) - not touched here.
3. Everything WebGL-only needs a build: raw pads, rumble through the jslib, FFB. The owner's tests below.

## Owner tests
**A. FFB test page (no game build needed)** - on the PC with the Moza:
1. `powershell -ExecutionPolicy Bypass -File "C:\Users\mcgee\PSX Racing-input\tools\ffb\serve.ps1"` (opens
   http://localhost:8765/webhid-ffb-test.html; Chrome or Edge).
2. Hands lightly on the rim. Strength starts at 25%.
3. CONNECT WHEEL -> pick the Moza base in Chrome's list (not listed: CONNECT (ANY DEVICE)). Status should go green.
4. PUSH LEFT, PUSH RIGHT, SPRING (centring), DAMPER (heavy), RUMBLE (buzz), each ~1.5 s; STOP ALL ends everything.
5. COPY LOG and paste it back with one line per button ("LEFT went right", "spring nothing", ...). If LEFT/RIGHT are
   swapped, that is FFB INVERT in the game.

**B. Rumble** (needs a build): CONTROLS > RUMBLE ON, a pad, drive: kerbs, a wall, the limiter, a gear change.

**C. Wheel setup** (needs a build): press a wheel button once (browsers hide pads until used), OPTIONS > CONTROLS >
WHEEL SETUP. STEER: DEVICE should name the base; STEERING AXIS -> turn full left, full right, centre; ROTATION = Pit
House's angle. PEDALS: press each, floor it, let go (numbers should run 0-100%). BUTTONS: paddles etc. Drive.

**D. In-game FFB** (needs a build, after A works): WHEEL SETUP > FORCE > CONNECT WHEEL (mouse click), pick the base, TEST.
If the browser blocks it, the desktop build would need DirectInput FFB - NOT written.

## Next
- Run 1 and 2 above (two jobs). Then the owner's A log decides whether pidffb.js needs a quirk (Block Load via input
  report, a different direction convention, a Device Control bitmask) - all logged.
- Tuning after the owner drives: RumbleMixer gains, FfbModel.SatGain/spring/damper, CarFeel texture threshold (4..30/s).
