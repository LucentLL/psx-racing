# HANDOFF - REAL HEIGHTS round 4 (2026-10-26 night): live-report hotfixes + junction WIP

Branch `real-heights` (worktree `C:\Users\mcgee\PSX Racing-heights`), sandbox `C:\Users\mcgee\PSXRec`
(`$env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'`; its built scenes predate the circuit removal: no scene-index tools there).
Typecheck: `tools\typecheck.ps1 -Project C:\Users\mcgee\PSXRec -Source "C:\Users\mcgee\PSX Racing-heights" -OutDir $env:TEMP\psxtc-heights`.
Reports in `C:\Users\mcgee\PSXRec`: gates `j4r3` (round 3), `j1r4` (mitre snap WIP), `j2r4` / `j3r4` (twin hold on, raw /
smoothed); drive audits `d1r4_city_drive.txt` (HEAD before the hotfix) .. `d5r4_city_drive.txt` (85c9345c);
`median_r4a.txt` (MedianProfileProbe slices, I-277); `probe_r4a.txt` (launch probe, mitred joints).

## Shipped this round (hotfix for the owner's live reports: concrete chunks, medians, the fall off I-277)
- 7f920b1c + 85c9345c: shared ground median runs on through approach rail bands (`PSX_CITY_TWIN_BANDS=1` = before);
  a union run's end met by a run on the twin carriageway is continued (`PSX_CITY_TWIN_CONTINUE=0`); isolated rails
  < 30 m off structure dropped (`PSX_CITY_KEEP_SHORT_RAILS=1`); piers clear a road's drawn (LineModel) extent.
- Narrow-median twin HEIGHT hold `CityElevation.HoldGroundTwins` is in but OFF (`PSX_CITY_GROUND_TWINS=1`): |dy| p95
  0.31 -> 0.02 m, but launch 109 -> 113, ramp node joints 10 -> 29, uptown route launches 1 -> 2 (j3r4).
- Audits: drive audit FALL HOLES (`CityAudit.Holes.cs`), SHORT RAILS / PARAPETS (`CityAudit.Barriers.cs`), the solve's
  twin report line; `MedianProfileProbe` takes `PSX_MED_EDGES="e:s0:s1;..."`.

| drive audit (city-cycle -DriveOnly) | main | real heights (d1) | 85c9345c (d5) |
|---|---|---|---|
| MEDIAN ACROSS none (open) / two+ walls | 65 / 577 | 107 / 631 | 43 / 409 |
| solid across a route lane | 0 | 2 | 0 |
| WALLS IN LANES (route) / BLUNT | 0 / 0 | 2 / 1 | 2 / 1 |
| route rail ends (BLUNT rail ends) | 0 | 1 | 3 |
| default tiles: walls across lanes | 0 | 7 | 7 |
| INVISIBLE lanes / solids | 5 / 43 | 22 / 56 | 7 / 56 |
| short isolated rails < 30 m / Jersey stubs | - / 0 | 63 / 0 | 24 / 0 |
| FALL HOLES (no collider, 111 route tiles) | - | 0 | 0 (8 off-route decks with nothing under) |

Launch/census gates with the hold off = round 3 (j4r3): launch 109/589 old, 109/585 lane carry; routes 1/0/0; census
freeway 243/79/19/3, ramp 1937/451/121/10, arterial 2139/796/236/16, collector 5321/246/66/153, local 20065/81/10/77;
route p99 1.12/2.11/0.75. The solve is untouched with the hold off, so Exit 3A (+0.28 m) and untagged structure hold.

## Next (in order)
1. I-277 e2321 s 65-67 (uptown 2,486 m, the US 74 merge): e1394's union median END stands in e2321's lane (WALLS IN
   LANES 2, BLUNT 1) - likely the owner's "concrete box ahead" at 1'06". The approach walk stops at gap < SqueezeM, so
   the Jersey ends where it no longer fits between the edge lines: end it where gap >= the Jersey's footprint (~1.2 m)
   with its sloped end, for approach AND ground runs; then re-count MEDIAN ACROSS none.
2. The twin height hold: the regressions come from ramps at junction nodes (seated stations follow, the node does not).
   Move the junction node with the mainline (and its ramps' ClimbOut) or keep the hold off within ~60 m of a ramp node.
3. Default-tile walls across lanes (7) at the I-277/I-77 interchange ramps (e9823, e654, e5220, e9779): the ramp runs
   ~6 m under I-277 (John Belk) on FILL with the plans overlapping - an untagged crossing (needs a deck / DeckReach).
4. Route rail ends 3 (e9877 approach verge, e2311 deck approach gaps) and the 8 off-route deck holes (S Tryon e1107/8,
   Briar Creek Rd creek bridge: nothing collides under the deck).
5. Then the junction items (round 4 plan below).

## Junction round 4 WIP (NOT committed: `git stash list` -> "r4-junction-wip"; backup C:\Users\mcgee\PSXRec\r4_junction_wip.patch)
- Mitred joints are mostly DIVIDED-ROAD SPLITS (South Blvd n8848, West Blvd n5192, Providence Rd W n14006): the other
  carriageway is a branch clipped onto its twin, its square end runs 2-3 m on over the two-way arm's pavement at its
  own grade, 5-9 cm proud (probe_r4a). `CityMeshes.MitreSnap` brings those vertices 1 cm under the other through arm:
  launch 109 -> 97 (old) / 103 (lane carry), mitred joints 35 -> 29 (j1r4); but census ramp/arterial/collector rose
  (+8/+44/+43 >0.5%: the census averages the snapped edge vertices) - tag snapped sections (RoadTap flag) so the
  census reads the unsnapped vertex, then commit.
- uptown n3640's "launch" is the AUDIT: at a mitred node the flown path sampled the node point twice (arm A's last,
  arm B's first) and the flight reads every sample 1 m on (a phantom 1 m flat: g0 -4.5% vs -9.3%, 0.16 m). WIP:
  `CityLaunchAudit.UniformSteps` (A ends on its trim, fan walked by arc length, B starts a step on: `pr.bOff`) - not
  yet measured; measure it alone (mitre snap vs mitre snap + uniform) since it changes main's baseline too.
- Seated stations drawn ON the host's drawn surface (`VsSeatAbsOn`, e4913: ramp 0.2-0.34 m under its host for its seat,
  the clipped strip dropped at the zone end) - not yet measured.
- Tryon p99 is the census's own reading (edge-vertex averages + the chord across fans); the lane line is smooth
  (probe_r4a e16424 at 0.5 m: top = drawn to 1 mm).
