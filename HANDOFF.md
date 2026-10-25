# HANDOFF - REAL HEIGHTS junctions + walls (2026-10-25, WIP, stopped at the context cap)

Branch `real-heights` (worktree `C:\Users\mcgee\PSX Racing-heights`), sandbox `C:\Users\mcgee\PSXRec`
(`$env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'`). Not shipped: the gates still fail. Unity jobs used: 1 of 7.

## Measured (job J1 = `tools/city-vsmooth.ps1 -Launch`, reports in `C:\Users\mcgee\PSXRec\j1_*` + `vsmooth.log`)
J1 had: the new offline landings (data spliced) + the fan's major pavement held (FanMajorOn). It did NOT
have anything listed under "added after J1" below.

| gate | main | prev REAL (116 run) | J1 |
|---|---|---|---|
| launch / unload city-wide | 60 / 408 | 116 / 597 | **120 / 618** (T1 53, T2 54, T3 13) |
| routes launch (uptown/tryon/indep) | 0/0/0 | 1/0/0 | 1/0/0 |
| census arterial >0.5/>1/>2/node | 1886/929/308/16 | 2140/955/305/9 | 2219/987/321/12 |
| collector | 5231/280/98/702 | 5591/283/104/154 | 5079/298/102/147 |
| local | 5480/52/15/138 | 19856/99/16/69 | 19403/94/17/71 |
| freeway / ramp | 290/131/45/4, 2846/691/203/7 | ok | 169/85/21/0, 1825/593/170/3 (ok) |
| route p99 g @150 mph | 1.60 / 1.79 / 0.96 | 1.06 / 2.07 / 0.66 | 1.06 / **2.07** / 0.66 |

J1 launch by cause: fan 23, junction node 16, mitred joint 40, terrain profile 24 (23 @mesh-off-data, mostly
links), ramp seat 9, cones 4. Todd Road #1 dropped 1.14 -> 0.42 m; Gilead Rd fan 0.39 -> 0.32.

## Findings (what is actually launching)
1. FAN FOLD (real, the mechanism the owner's brief names): a fan is triangulated centre -> each arm's corners
   at that arm's centreline height, level across. On a 5% major grade a perpendicular minor arm's mouth is a
   FLAT triangle from the centre, so a through car sees 5% - flat - 5%. Offline proxy
   `tools/city/junction_proxy.mjs` (FAN FOLDS) counts minor-arm corners off the plane: RPRF design 2154 (>0.3 m:
   475) before the new landings, 1493 (210) after; the game's drawn dump 806 (68). Fix tried: `FanSteiner`
   (CityMeshes.Junctions.cs) puts Steiner points along the major pair's pavement edges at its profile height
   and keeps minor-arm line points out of the major's pavement. Effect in J1 small (fans 25 -> 23) - verify the
   points actually land (RefineFan drops points outside triangles; check with FanCheck / a shot of Gilead Rd
   node 5107 (-5379,24564)).
2. SPLIT/MERGE PATH HOOK (audit artefact): 49 of the 116 launches sit at 3-arm nodes with 2+ one-way arms. The
   audit's movement path runs a two-way arm in its right lane (LatFor = hw/2) and a one-way on its centreline,
   which starts on the two-way's centreline at the node -> the path swerves its lane offset sideways inside the
   trims (plan turns 90-114 deg, e.g. West Blvd node 5192); on a real 5.7% grade the hook reads as a crest.
   Added after J1: `CityLaunchAudit.LaneCarry` (PSX_LAUNCH_LANECARRY=0 = old) carries the offset across and
   eases it out over ApproachM. Measure both rules in ONE job: `tools\city-vsmooth.ps1 -Launch -Lanes`
   (city_launch_before = old paths, _after = lane carry; drawn profile on in both). Report both honestly.
3. Census arterial residuals are "no curve here" breaks at the 2nd station (12-25 m) of one-way arms at 4-way
   nodes (`vsmooth_residuals.csv`; scratch script resid.mjs logic: bin by distance to node). The new offline
   landing eases the MAJOR pair too (d0 = 0) with Hermites sampled at 10 m stations -> station breaks the drawn
   pass does not round. NEXT: skip major arms in `landJunctions` (roadprofile.mjs: `const major = ...` ->
   `if (major) continue;`), re-splice, re-measure.
4. Link launches (~40 of 116): terrain-profile @mesh-off-data and ramp-seat climb-outs on ramps - the runtime
   seats (CityElevation PrepareSeats/BranchSeats) pull ramp stations to the host; the RPRF ramp profile beside
   the gore differs. Not addressed. Offline idea: pull link stations inside the host's pavement to the host's
   designed height before the chains are limited.

## Code on the branch (this commit)
- `tools/city/lib/roadprofile.mjs` `landJunctions` rewritten, now runs after SHORT PIECES: every arm leaves the
  node on the plane (node height + major through chord grade, level across); minor arms held for
  hwMaj/sin+3 m; Hermite ease, shortest within R 400 m, else safe R = max(60, 1.5 v^2/g at the audit's judged
  speed), else gentlest; walks on through 2-arm nodes, half way to the next junction; soft masks (abutment 5,
  steep 7) passable; shorter holds tried before giving up; `PSX_LAND_DEBUG=<node>` traces. Report: 10,895 arms
  at 7,271 nodes (preferred 12,017 / safe 3,953 / best 915 / no room 487); arm pairs breaking >4%/>8% through a
  node 4569/796 -> 2817/379. Data `charlotte_city.bytes` is spliced with it (`node tools/city/rprf_splice.mjs`);
  `tools/city/fingerprint.json` NOT updated, `export_osm.mjs --check` NOT run.
- `tools/city/junction_proxy.mjs` (new): offline FAN FOLDS, through-node crest breaks, INTERIOR CRESTS on the
  design (`--city`) or a dump (`--dump real_on.tsv [--col 3|4]`).
- CityMeshes.Junctions.cs: FanMajorOn (PSX_CITY_FANMAJOR=0 off), DistToSeg; cluster case = main pair through
  one member node (added after J1, untested).
- CityMeshes.cs (all added after J1, untested in Unity, typecheck OK):
  - JoinBuriedEnds: smoothstep ease, reach >= 1.5d/3% and sqrt(6 d 300 m) (JoinStraight / PSX_CITY_JOIN_STRAIGHT=1 = old).
  - RETAINING WALLS: `InCut` walls a barriered road's outside cut where the 1V:3H back slope cannot meet the
    land by the section's reach (why = CutWhyNoBackSlope 5; guard = land 12 m out > 2 m); `CutBankAllowed` /
    PSX_CITY_CUTWALL_BANK=1 = old 1V:2H bank. Intended to fix the play-check FAIL (I-277 e1482 s=1065,
    -3014,4222, side L = outside shoulder): the drive-off spot is then skipped as walled; the check still
    needs >= 4 driven with a cut (6 driven before). Only barriered roads (freeways, one-way trunks) get cut
    walls (existing rule `Barriered`).
- CityVSmoothCensus.cs: RETAINING WALLS line (km by why, longest added) + `PSX_VS_LAUNCH=lanes` mode.
- CityAudit.cs: cut-wall why table 6 wide, reports the no-back-slope metres.
- CityRealHeights.cs: `RunJunctionShots` (before = fan from arm heights + straight ease + bank; after = all on).
  Views (PSX_PREVIEW_SPOTS=custom, name:eye:x:z:road:heading):
  `rh_exit3a:eye:-959:4756::-13;rh_gilead:eye:-5404:24546:Gilead:58;rh_i277wall:eye:-2966.5:4177.5:I-277:-38`
  (shots land in `<sandbox>\Screenshots\City\*_before|_after.png`; copy to `Docs/shots/heights/`, not committed).

## Next (in order, lean)
1. Skip major arms in landJunctions (finding 3), re-splice.
2. ONE job: `tools\city-vsmooth.ps1 -Launch -Lanes -OutDir C:\Users\mcgee\PSXRec\j2` -> census (+ walls line),
   launch old paths vs lane carry. Compare with the table above.
3. If fans still ~20: check the FanMajor Steiner points land (FanCheck steinerUsed) at Gilead node 5107.
4. `tools\city-play-check.ps1` (I-277 wall), then RunJunctionShots; update Docs/CHARLOTTE.md REAL HEIGHTS
   (section "Left" + the numbers), fingerprint + `export_osm.mjs --check`, commit.
Tryon p99 2.07 g: worst at e16424 s 18-21 (-1802,5175), 16.7 g single kink - not looked at yet.
