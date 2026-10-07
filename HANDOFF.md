# spec-handling handoff (2026-10-07)

Branch `spec-handling` (worktree `C:\Users\mcgee\PSX Racing-roads`), from main 7ac2a20a. Not merged, not pushed.

## Done
- `tools/bake_spec_handling.py` bakes into `Resources/rg2_cars.json`: wdFront, springF/R (kgf/mm; the
  centre of the range for the 50 race/rally cars with adjustable stock coilovers), rideF/RMm, gripF/R,
  revLimit (held at the redline for the one car whose sheet limiter is below it: PENNZOIL GT-R JGTC),
  lsdInit/Accel/Decel (driven axle; bracketed cells = not fitted; 4WD takes the rear), peakTorqueNm on
  the PS basis. Run `bake_topspeed.py` after it (both are idempotent and now write the worktree they live in).
- CarController: CoM on the sheet split (`CenterOfMassLocal`); `StockChassisOf` (sheet springs, the FD's
  damping ratio per axle, roll split = weight split + the FD's 7.15% margin, bars floored at 25% of the
  reference); spring preload holds every axle at the FD's mean static sag (+ the sheet's rake) through a
  200 kN/m tyre ramp; per-axle bar and suspension clamps; tyre mu = grip / 96 x FD's 1.000 / 1.050;
  limiter = `CarSpec.RevLimitRPM`; factory LSD = the factory setup (also on the AI); drift arm pinned 0.5.
- CarSpec.Decode normalises every curve to hp x 735.5 W (sample-point peak); PeakPowerRPM walks to the
  limiter. Every on-screen hp goes through `CarSpec.ToHp` (PS x 0.9863); prices / fuel / meets still read PS.
- Upgrades: `HasMod` = `Bought` || `FactoryFitted`; shop says FITTED, refunds only Bought, a weld replaces it.
- CarSetupRanges mirrors all of it (self-test "derive the same car" = 0 fields apart).
- HandlingPlayCheck P (FD, Civic EG FF, AE86 FR, NSX MR, RUF CTR RR); LifeSimSelfTest SpecHandlingChecks.

## Results so far (sandbox PSXCity, seed 0)
- Self-test (b5661007): all 12 new spec checks ok; gate/open-diff/reset checks fixed. STILL FAILING (mine):
  "every car reaches its build's top speed within 1.5%" - 4 off, worst GALANT GTO MR +turbo kit 227 of
  222 km/h; and "no gearing beats stock by >3%" - Chaparral 2D +3.0%. Both are engines that still pull
  past the redline now that the limiter is the sheet's (up to +2100). The other ~49 failures are DeckRun /
  Tryon scenes, ReleaseBudget, PSX/Lit beam - the PSXCity sandbox's scenes, not this branch.
- Handling P (before -> after, seed-identical): see the final report; A-H + S all still pass.
- LAST COMMIT (pitch): the static sag split now also takes up the F/R tyre-radius difference (NSX sat
  -0.51 deg, RUF -0.14 deg nose-down). Typechecked, NOT play-tested.
- Race (-Finish, NONE): GillespieGap AI 218.5/230.2/245.0 -> 220/228/233 s; player autopilot 243 -> 282 s
  (a "stuck" recovery at 264 s, wp 1640, lat +1.0 - not diagnosed). ChimneyRockRev AI 205.4/206.0/221.2 ->
  210/203/224 s, player 221 -> 224 s; the Supra spun once at 147 s and recovered (was 0 spins). No rollovers.

## Left / next commands
0. Fix the top-speed check above: likely the turbo-kit build's anchor/drag solve vs the power between the
   redline and the sheet limiter (CarSpec.PeakPowerRPM / CarController.DeriveDrag); or cap the physics
   limiter for the ~10 engines whose power is still rising at the redline.
1. Re-run the self-test with b5661007 (PSX_SANDBOX=C:\Users\mcgee\PSXCity, copy rg2_cars.json first):
   `powershell -File tools\quick-selftest.ps1` then `grep -n FAIL C:\Users\mcgee\PSXCity\PSXRacing_selftest_log.txt`
2. Diagnose the Gillespie player "stuck" at wp 1640 and the ChimneyRockRev Supra spin (re-run
   `tools\race-play-check.ps1 -Venues "GillespieGap,ChimneyRockRev" -Seconds 600 -Finish -Traffic NONE`
   with a second seed; compare with main).
3. Owner calls: race-car springs at the range centre (12 kgf/mm, ~4 Hz) vs the range minimum; whether a
   factory LSD should open the diff sliders (it does now; the shop's part reads FITTED).
