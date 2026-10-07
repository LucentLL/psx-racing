# spec-handling handoff (2026-10-07)

Branch `spec-handling` (worktree `C:\Users\mcgee\PSX Racing-roads`), from main 7ac2a20a. Not merged, not pushed.

## Done (session 1)
- `tools/bake_spec_handling.py` bakes into `Resources/rg2_cars.json`: wdFront, springF/R (kgf/mm; the
  centre of the range for the 50 race/rally cars with adjustable stock coilovers), rideF/RMm, gripF/R,
  revLimit (held at the redline for the one car whose sheet limiter is below it: PENNZOIL GT-R JGTC),
  lsdInit/Accel/Decel (driven axle; bracketed cells = not fitted; 4WD takes the rear), peakTorqueNm on
  the PS basis. Run `bake_topspeed.py` after it (both are idempotent and now write the worktree they live in).
- CarController: CoM on the sheet split (`CenterOfMassLocal`); `StockChassisOf` (sheet springs, the FD's
  damping ratio per axle, roll split = weight split + the FD's 7.15% margin, bars floored at 25% of the
  reference); spring preload holds every axle at the FD's mean static sag (+ the sheet's rake, + the F/R
  tyre-radius difference) through a 200 kN/m tyre ramp; per-axle bar and suspension clamps; tyre mu =
  grip / 96 x FD's 1.000 / 1.050; limiter = `CarSpec.RevLimitRPM`; factory LSD = the factory setup (also
  on the AI); drift arm pinned 0.5.
- CarSpec.Decode normalises every curve to hp x 735.5 W (sample-point peak); PeakPowerRPM walks to the
  limiter. Every on-screen hp goes through `CarSpec.ToHp` (PS x 0.9863); prices / fuel / meets still read PS.
- Upgrades: `HasMod` = `Bought` || `FactoryFitted`; shop says FITTED, refunds only Bought, a weld replaces it.
- CarSetupRanges mirrors all of it (self-test "derive the same car" = 0 fields apart).

## Done (session 2)
- 2b1f5c16 HandlingPlayCheck P. The AE86 "plough" (+19.3 deg, 11 deg off line) was the TEST, twice:
  the skidpad took the LAST rise in g (an open-diff FR steps out on the power at full lock: 1.25 g
  plateau at 0.3 of lock, 1.30 g at 1.0) - balance is now the FIRST limit (frozen once g falls 0.03
  under its best): AE86 +1.8, FD +1.8. And the lane change began while Place's 0.6 m drop was still in
  the air (a soft car was measured LANDING) - now 1.5 s straight at 70 first: AE86 6.7 deg off line,
  FD 11.3. Also: rest pitch vs the sheet's rake is a Check (0.3 deg), g/slip curve per 0.1 of lock,
  a yaw/heading trace, PSX_P_CARS (survey any cars) and PSX_P_EXP (chassis experiments per car:
  lsd, fd, com, mu, noinj, splitNN, barsx2, springx2, dampx2).
- a5add189 Top speed: BuildGearRatios clamped the anchor to the REDLINE while PeakPowerRPM and the bake
  go to the limiter. Anchor = peak, capped at limiter - 100 (cut is at limit - 50). Self-test: 0 off
  (was 4, GALANT GTO MR + turbo kit 227/222), no gearing beats stock: worst +0.0% (Chaparral 2D was +3.0%).

## What the model does (read before tuning balance)
- Tyre lateral force is LINEAR in load (C = corneringStiffness x Fz, circle = mu x Fz), so the limit
  balance does not depend on the weight split at all: 22 cars surveyed (Volvo 240, Taurus SHO, 406,
  Elise 190, A310, Stratos, GT40, Chaparral 2D, BMW V12 LMR, JGTC GT-R, Impreza WRC...) all read
  +1.4..+2.2 deg at the first limit. A real front-heavy car understeers more; that needs tyre load
  sensitivity (owner call - it would move the FD the owner tuned).
- The anti-roll bars push the BODY (AddForceAtPosition at the mounts) but are not in wheelLoad, so the
  roll split changes roll attitude only, never grip (AE86 split 67% -> 57%: identical numbers).

## Results (sandbox PSXCity)
- Self-test: 49 FAIL, all DeckRun/Tryon scenes, ReleaseBudget, PSX/Lit beam (the sandbox's scenes).
  0 from this branch.
- handling-play-check (A-H, S, P): ALL BEHAVE. P main -> branch (same harness, on its wheels):
  rest pitch: main sat every car NOSE-UP 0.4-0.6 deg (FD +0.52), branch 0.00 = sheet rake (22 of 22).
  FD 1.33 g +1.8 / lc 12.0 -> 11.3 deg; EG 1.35 +1.6 / 20.1 -> 1.33 +1.7 / 6.9; AE86 1.35 +1.7 / 16.6 ->
  1.29 +1.8 / 6.7; NSX 1.33 +1.9 / 8.4 -> 1.32 +1.8 / 9.8; RUF 1.33 +2.0 / 16.4 -> 1.29 +1.9 / 14.7.
- OPEN: a full-lock keyboard lane change (P / test E input, measured on its wheels) ends 15-32 deg off
  line on light rear-heavy cars: branch Elise 26.6, A310 31.8, Stratos 28.8, R5 Maxi 26.6, Chaparral
  22.3, MR2 18.9, GT40 16.8 - but main already had 10 of 13 sampled cars over 15 (Civic 20.1, AE86 16.6,
  A310 23.7, Stratos 23.7, R5 24.5, Chaparral 24.8). The branch moves it with the weight split (front-
  heavy better, rear-heavy 5-8 deg worse); CoM to the midpoint takes the A310 31.8 -> 22.3, chassis /
  mu / LSD change nothing. Not a spin (body slip <= 12.5 deg). Test E's lobes/settle are the owner's gate.
- Race (-Finish, NONE, seeds 0/1), branch vs main: see the session report; 0 rollovers, 1 spin
  (Supra, a planned AI MISTAKE), retirements 3 vs 1 - all three after planned MISTAKEs.
- The Gillespie player "stuck" at wp 1640 does not reproduce at HEAD (seeds 0, 1) nor on main.
- "no pumping on the open road" FAILs in every race on main too (exposure check, not this branch).

## Left / owner calls
1. Tyre load sensitivity, if front-heavy cars should understeer more than the FD (see above).
2. Rear-heavy cars after a full-lock keyboard lane change (above) - pre-existing, moved by the CoM.
3. Race-car springs at the range centre (12 kgf/mm, ~4 Hz) vs the range minimum; whether a factory LSD
   should open the diff sliders (it does now; the shop's part reads FITTED).
