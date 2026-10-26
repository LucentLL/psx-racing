# HANDOFF - REAL HEIGHTS round 6 (2026-10-28): the median boxes (start line), the ground V; twin hold v3/v4 measured, still OFF

Branch `real-heights` (worktree `C:\Users\mcgee\PSX Racing-heights`), sandbox `C:\Users\mcgee\PSXRec`
(`$env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'`; its built scenes predate the circuit removal: no scene-index tools there).
Typecheck: `tools\typecheck.ps1 -Project C:\Users\mcgee\PSXRec -Source "C:\Users\mcgee\PSX Racing-heights" -OutDir $env:TEMP\psxtc-heights`.
Full write-up: Docs/CHARLOTTE.md "REAL HEIGHTS - round 6" (round 5 above it).

## One job for every gate
`CityVSmoothCensus.Run` with `PSX_VS_LAUNCH=uniform` and `PSX_VS_THEN_DRIVE=1`: census, the launch audit with the old
node sampling (city_launch_before) then UniformSteps (city_launch_after), and the drive audit, ~50 min. Mirror only
Scripts/Editor/Shaders into PSXRec and call Unity directly. `CityRealHeights.RunMedianLook` (~3 min): one solve, the
`PSX_SPOTS` plans (spot_probe.txt) and the `PSX_PREVIEW_CUSTOM` shots (name:top:x:z:road:size / name:eye:x:z:road:heading,
with `PSX_PREVIEW_SPOTS=custom`). Reports: `C:\Users\mcgee\PSXRec\g1r6`..`g4r6` (gates), `l1r6`..`l3r6` (looks).

## Shipped this round (9ed2f100 = g3r6; the node snap = g5r6)
- Median boxes: the ground-twin walk takes OSM bridges + fills short gaps + counts only DRAWN coverage
  (`PSX_CITY_TWIN_BRIDGES=0` = before); partner-run side by every sample's vote (`PSX_CITY_TWIN_SIDEVOTE=0` = before).
  MEDIAN ACROSS two+ 406 -> 42, none 39 -> 35 (I-277 332 -> 11). The start-line box (e1910/e1920) and the 482 m double
  wall on e2315 are gone (Docs/shots/heights/rh_startline_*, rh_e2315_double_*).
- Union run ends within 0.6 m of a node snapped onto it (a deck V + turned-down Jersey diamond at e2307/e2303): two+ 42 -> 35
  (I-277 4), every other gate = g3r6 (g5r6).
- Ground V at union ends where structure begins (`PSX_CITY_GROUND_V=0` = before): route rail ends 3 -> 1.
- `PSX_CITY_VSEAT_ABS` dropped.
- Twin hold v3/v4 code in, OFF (`PSX_CITY_GROUND_TWINS=1`); the median-step report prints per road and at the owner's
  spots in city_drive.txt line 2 (`MEDIAN STEP per road`), hold on or off.

## Baselines (vs g5r6)
launch old 109/585 routes 1/0/0; **uniform 102/395 routes 0/0/0 (the new launch baseline)**; census freeway 244/80/19/3
(e5906 sampling, see CHARLOTTE.md), ramp 1936/451/121/10, arterial 2137/794/236/16, collector 5321/246/66/153, local
20065/81/10/77; route p99 1.12/2.11/0.76; median two+ 35 / none 35; rail ends 1; WALLS IN LANES 0; INVISIBLE lanes 3;
FALL HOLES route 0 (8 off-route); solve 29.5 s.

## Next (in order)
1. Twin hold: neither variant ships (CHARLOTTE.md round 6 table): v3 follow meets the step (p95 0.02) but costs the
   census ramps (1984/477/131/15) and launch (111); v4 host-keep keeps the ramps but halves the step (0.10) and costs
   US 74 e9094 a launch and Independence p99 0.83. Next idea: hold the pair only where NEITHER side is in a seat zone
   (ramps never move), half shares only, longer eases on the Independence expressway.
2. The 35 two+: I-277 e2438/e2436 (Elizabeth Ave, s 0-2 by the node), e2139/e8488 s 3; I-77 e15241/e1887; East
   Independence Expressway's at-grade junction fans (29) - a median nose at the fan instead of each side's Jersey.
3. e9877 s 18 rail end (ramp e279's approach rail start by I-277's edge line).
4. Mitre snap with the census fix measured alone (`PSX_CITY_MITRE_SNAP=1`).
5. The 8 off-route deck holes (S Tryon e1107/8, Briar Creek Rd creek bridge).
