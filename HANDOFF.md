# HANDOFF - branch tyre-load (from main 56f32403), 2026-10-07

NOT merged, NOT pushed. Sandbox C:\Users\mcgee\PSXCity holds the branch (rg2_cars.json included).

## Commits
- 325ecdb9 Factory parts are not tuneable; race/rally springs from a tarmac ride frequency
- 1f40891d Tyre load sensitivity + anti-roll bars feed the tyre loads; AI grip reads the static split
- eaade6ae Built cars take no parts and run their sheet; spring/ride sliders fenced to the sheet
- 02827361 HandlingPlayCheck P "kit" / "nobar" experiment tokens
- (next) this handoff

## Built cars (owner: "Factory parts are not tuneable (exception is race cars and rally cars ...)")
- CarSpec.IsBuiltToTune = race | rally | raceSusp (touring, JGTC GT-R, 155 TI, Escudo): 51 cars.
- Shop refuses EVERY part on them: all ladders but the seat, every mod (weld + blower too), the turbo kit.
  Words: Upgrades.AlreadyBuilt -> RACE CAR / RALLY CAR / RACE-SPEC - ALREADY BUILT. Garage (LifeHomeScreen),
  specs/market line, debug bench (DebugCarPanel/DebugCarOps), junkyard, job completion all go through it.
  Market/resale never valued parts (no change needed).
- Physics: CarTune.BoughtOf/HandlingOf(IsBuiltToTune) = no stages at all -> the SHEET. The old race kit
  (stage-4 brakes/susp/tyres = grip x1.20, stiffness x1.25 to 13, brakes to 1.26 g) is gone.
  Build top speed / hp / kg / torque = sheet; CanFitTurboKit false. ApplySpec and CarSetupBasis.FromSpec agree.
- LifeSimManager.StripBuiltCars runs on every load (after EditionSanitize): old rally/touring builds come off,
  refunded once, turbo flag cleared, seat kept.
- Measured (HandlingPlayCheck P, kit = old race kit re-applied):
  Lister Storm (race): skidpad 1.48 -> 1.34 g, 100-0 87.0 -> 101.6 m, lane change slip 5.3 -> 5.2.
  Stratos Rally (could BUY the kit on main): 1.63 -> 1.47 g, 84.9 -> 99.1 m, lane-change slip 15.1 -> 13.3;
  power/weight ceiling 410 hp NA / 521 turbo kit / 869 kg -> sheet 274 hp / 1035 kg.
- Two race cars have FIXED sheet springs (Gathers Drider CIVIC, S800 RSC): built-to-tune, generic spring slider.

## Slider fences (built cars)
- bake_spec_handling.py writes springMinF/MaxF/MinR/MaxR (kgf/mm) + rideMinMm/MaxMm (mean F/R) on the 49
  raceSusp cars. CarSetupBasis.SheetFences (FromSpec, FromController, SetupBaseline.BasisFor) -> spring rows
  span the sheet's Stock Springs Min..Max (4-20 / 3-20), ride height = default + (Min..Max - stock centre),
  still floored at 0.20 m. Road cars keep 0.70x..1.45x and -80/+20 mm.

## RUF CTR verdict: load sensitivity on a 40:60 car, NOT a defect (no fix)
P lane change, heading after / body slip: base 22.1 / 8.9; ls=0;lg=1;nobar 14.1 / 7.4 (= main exactly);
bars feeding tyres only (ls=0;lg=1) 13.4 / 7.3; load sensitivity only (nobar) 22.7 / 8.9; stagger removed
(mu) 27.3 / 10.1. Yaw settles to ~0, slip < 15, skidpad +1.8 deg understeer unchanged: it ends pointing off
line, it does not spin. Same group as Elise 31, A310 34, MR2 23, GT40 23 deg.

## Results (PSXCity)
- sandbox-selftest: 49 FAIL, all sandbox-only (DeckRun scenes, ReleaseBudget, beam, window mask) plus
  "TryonSprint catalog and path agree on the finish" (city scene data). New pins all ok.
- aigrip-play-check (8 cars): measured/predicted mean 0.982, median 0.995. Taurus SHO 0.805 / 0.941 / 0.988 /
  0.958 (R25..R140: FF on full throttle at tight radius; Civic R25 0.876 too). Elise ran off the test strip as
  the 8th car (xLane walks 1.6 km per car) - alone: R25 1.053, R45 1.020.
- HandlingPlayCheck A-H, S, P (default 5 P cars), final run: 54 ok, 1 FAIL = the RUF's P lane-change "off
  line" 22.1 deg (8.9 slip) - the known load-sensitivity result above, not a spin.
- race-play-check: see RACE below.

## RACE (GillespieGap + ChimneyRockRev, 600 s -Finish, Traffic NONE; main = 56f32403 run in PSXCity, own run)
Finish s (Charger / Supra / Skyline / player RX-7 autopilot); RET = retired 2-4 s after a scripted MISTAKE.
| race | main | branch |
|---|---|---|
| Gillespie s0 | RET / 234 / 230 / 235 | 221 / RET / 232 / 247 |
| Gillespie s1 | 205 / 218 / RET / 229 | 206 / 223 / 245 / 225 |
| Chimney Rev s0 | 204 / 218 / 210 / 224 | 211 / RET / 210 / 231 |
| Chimney Rev s1 | 210 / 218 / RET / 214 | 213 / 222 / RET / 208 |
Spins 0 / recoveries 0 / no rollovers in all 8 races. Hard hits main 5+2+1+3, branch 3+0+2+1. Slides main
0/18/17/43, branch 0/17/9/41. Same-car rival times +0..+7 s on the branch. Every race (main too) FAILs
"no pumping on the open road" (sky gain 1.12-1.34): pre-existing, not this branch.
