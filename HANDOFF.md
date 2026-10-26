# HANDOFF - REAL HEIGHTS round 5 (2026-10-27): the concrete box and the ramp walls fixed; junction WIP + twin hold landed OFF

Branch `real-heights` (worktree `C:\Users\mcgee\PSX Racing-heights`), sandbox `C:\Users\mcgee\PSXRec`
(`$env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'`; its built scenes predate the circuit removal: no scene-index tools there).
Typecheck: `tools\typecheck.ps1 -Project C:\Users\mcgee\PSXRec -Source "C:\Users\mcgee\PSX Racing-heights" -OutDir $env:TEMP\psxtc-heights`.
Full write-up: Docs/CHARLOTTE.md "REAL HEIGHTS - round 5".

## One job for every gate
`CityVSmoothCensus.Run` with `PSX_VS_LAUNCH=lanes` (or `uniform`) and `PSX_VS_THEN_DRIVE=1` runs the census, the launch
audit twice and the drive audit in ONE Unity job (~50 min; the drive numbers match a separate `city-cycle -DriveOnly`);
`PSX_SPOTS="edge:s0:s1:lat0:lat1;..."` adds spot plans (`MedianProfileProbe.Spots`, spot_probe.txt). Mirror only
Scripts/Editor/Shaders into PSXRec and call Unity directly (`city-vsmooth.ps1` without -NoMirror robocopies Art/Resources).
Reports in `C:\Users\mcgee\PSXRec\g1r5` .. `g6r5` (each: vsmooth_census.txt, city_launch_before/after.*, city_drive.txt).

## Shipped
- **50219f8b** (g1r5): the US 74 merge box (I-277 e2321 collapsed into its host: `FacingEdgeAt` / `HostUnionHere`,
  `PSX_CITY_COLLAPSE_HOST=0` = before); the I-277/I-77 interchange ramp walls (`CityElevation.ReachPastEnd`, deck carried
  past its edge's end over the road under, `PSX_CITY_REACH_PAST=0` = before).
- **6fd19d24**: the round-4 junction WIP and the twin hold v2, all OFF by default (game and gates = 50219f8b).

| gate | round 3/4 | 50219f8b |
|---|---|---|
| launch old / lane carry; routes | 109/589, 109/585; 1/0/0 | 109/589, 109/585; 1/0/0 |
| census freeway, ramp | 243/79/19/3, 1937/451/121/10 | 243/78/19/3, 1936/451/121/10 |
| arterial, collector, local | 2139/796/236/16, 5321/246/66/153, 20065/81/10/77 | 2137/794/236/16, 5321/246/66/153, = |
| route p99 g | 1.12/2.11/0.75 | 1.12/2.11/0.76 (sampling shift only) |
| WALLS IN LANES / route BLUNT / default walls across lanes | 2 / 1 / 7 | 0 / 0 / 0 |
| MEDIAN none / two+; INVISIBLE lanes; FALL HOLES route | 43 / 409; 7; 0 | 39 / 406; 3; 0 |
| route rail ends; solve | 3; 30.9 s | 3; 30.0 s |

## OFF switches (measured, see CHARLOTTE.md)
- `PSX_CITY_GROUND_TWINS=1` twin hold v2 (class 5, |dy| <= 1 m, 20 m junction clear + 60 m ease; v1's pin tapers and
  node fades): |dy| p95 0.31 -> 0.04 m but launch 108 (old sampling), uptown route launches 2, freeway 232/80/20/5,
  ramp 1997/469/128/11, a 0.15 m route lane step at e2321 s 43 (clip seam on US 74: the hold moved the merge
  carriageway, not its host). NEXT: keep a carriageway's CLIP zone (not only its seat) / skip merge carriageways; then
  the ramps' seat follow.
- `PSX_CITY_MITRE_SNAP=1` (divided splits only): launch 105/571; census arterial 2157/808/238, collector 5329/251/66 -
  over. The census's snap-aware reading (FSnapL/R) is in but unmeasured alone: measure snap ON with it first.
- `PSX_CITY_VSEAT_ABS=1`: census ramp 1936 -> 2831 (>0.5%). Drop it or rethink.
- `PSX_LAUNCH_UNIFORM=1` (audit only): launch 105 -> 97, unload 571 -> 377, uptown route 1 -> 0 (n3640 was the audit).
  Moves main's baseline too - re-baseline main with it before adopting.

## Next (in order)
1. Twin hold: the clip-zone keep (above), measure ON (`PSX_CITY_GROUND_TWINS=1`), then turn it on.
2. Route rail ends 3 (main 0): e2144 s 500 / e1874 s 188 - the approach union stops at the twin's OSM bridge start and
   the carriageway's own approach rail starts square, KEPT square because its deck starts 0.5-2.6 m in; e9877 s 18 -
   ramp e279's approach rail start. Idea: a V into each carriageway's rail at a ground union end where a rail begins
   within ~3 m (as on decks), or e2144's deck joined to the e2316/e2352 union.
3. Mitre snap with the census fix measured; uniform sampling adopted with a re-baselined main.
4. The 8 off-route deck holes (S Tryon e1107/8, Briar Creek Rd creek bridge).
