# HANDOFF - REAL HEIGHTS round 11 (2026-10-30): the city-wide launches by class; round 10's leftovers

**IN PROGRESS (02:30): the final gate runs on PSXLab (r11gate.ps1 -Tag after, code = commit beb0d7a3); then the traffic
check, the OFF/ON shots, the sheet, the write-up.** Branch `real-heights-r11` (worktree `C:\Users\mcgee\PSX Racing-heights11`,
off integration-1029e 4827fa53), sandbox `C:\Users\mcgee\PSXLab`. No city data regenerated (code only; exporter untouched).
Done so far (A/B on one load, the gate's uniform sampling): launches city-wide 99 -> 87 with the round-11 rules (19 fixed,
1 new; routes 0/0 kept), unloads 401 -> 373; STREETSEAT tested and left OFF (87 -> 89, grades past 16% 3 -> 4). Rules
(each `PSX_CITY_R11_<NAME>=0` the before): CLIMBC1, CRESTCAP, SPILLUNDER (solve); GOREFAN, MITREPOKE, GORESTART, SKIRTPAVE,
INNERMAJOR (tile); audit: INVISIBLE ceiling probe skips a lane surface under a drawn ground sheet (PSX_INVIS_SHEET=0 before).
Left: the gate numbers, traffic check, shots + sheet (Docs/shots/heights11/), CHARLOTTE.md "REAL HEIGHTS - round 11".

# HANDOFF - REAL HEIGHTS round 10 (2026-10-29): the 1'47" box at last; the drive audit's last drivability defects

Branch `real-heights-r10` (worktree `C:\Users\mcgee\PSX Racing-medians10`, off integration-1029c f96586ce = main 1f9ace37 +
medians round 9), sandbox `C:\Users\mcgee\PSXLab` (AGENT_LOCK). Full write-up: Docs/CHARLOTTE.md "REAL HEIGHTS - round 10".
Sheet: `Docs/shots/medians10/real_heights_r10_sheet.png`. No city data regenerated (code only).
Code: `Scripts/City/CityElevation.R10.cs`, `Scripts/City/CityMeshes.Medians10.cs` (new partials) + hooks in
`CityElevation.cs` (UnionAbutments / CarryDeckTo, Ground's deck branch), `CityMeshes.Unions.cs` (one line after the open
flags), `CityMeshes.Medians9.cs` (R9KeepJerseyEnd); census `Editor/CityAudit.Holes.cs`; tools `Editor/CityR10.cs`.
Nothing in the exporter, the line clean-up or CityTraffic.
Sandbox: PSXLab's tracked assets brought to f96586ce first (scratch r10sync.py: of 1188 differing files 4 were older committed
content - charlotte_city/decks/signs.bytes, the URP global settings - copied with CityKit.asset and the missing CityGuide.png /
CityGuide.mat + their metas; the other 1184 are the sandbox's own build products, left), then a scene build (BUILD OK, 35 min).

## Shipped this round (ON by default; each `=0` is the before)
- `PSX_CITY_R10_ABUTFOOT` - one abutment line never puts a deck end more than 1.5 m past its twin's own end (square to its
  own road): 16 ends city-wide; 1'47" e2445's deck 12.3 -> 7.5.
- `PSX_CITY_R10_DECKV` - a deck run end "continued" only by its own partner's padded claim is closed with the deck V (the
  1'47" union Jersey's square hollow end); the rail its leg meets keeps full height; the V is in the MEDIAN census's V
  class. 9 deck ends city-wide.
- `PSX_CITY_R10_SPILL` - a twin deck's median by its abutment (<= 24 m from the deck's end, the median side, <= 8 m past
  the pavement; union decks and their two-arm continuations) is held on the spill-through slope from the bridge seat, the
  soffit's air cap tapering 1.2 -> 0.15 m over the last 4 m. 1'47" box -11 m -> -0.2..-1.2 m.
- `PSX_AUDIT_R10_LOTS` (census) - FALL HOLES: a lone Roads hit ON the drawn lattice (+-0.15 m) is a lot's pavement, not a
  deck with nothing under it. The base's 8 places were all that.

## Left (causes in CHARLOTTE.md round 10)
- INVISIBLE lanes 4: N Tryon e14645 s ~118 (a near-vertical Roads plate under the E 12th St / ramp bridges, also drawn in
  the Tryon route's left lane - not a closing face; needs triangle provenance), E Trade e6167 x2 (a sidewalk strip over
  the junction fan's edge, 16 cm), Cates e21212 (connector e23417 climbing 9.5 % off the node, its ribbon 0.18 m over
  Cates's lane).
- BLUNT route 1: I-277 e9902 s 18, the exit ramp e279's outer rail starts square where the ramp leaves its host's
  pavement; the rail-end plan never reaches it (a first rule was tried, never fired, removed).
- 1'47": a 1 m sliver behind the route's rail start where the drawn lattice dips toward a vertex under the route deck's
  own end (not seen from the road).

## Gates
Measured on PSXLab, one census code, base first (pristine f96586ce code, 20:01-21:19), after = this branch, every round-10 switch
on (21:55-23:07). One job each: `CityVSmoothCensus.Run` with PSX_VS_LAUNCH=uniform + PSX_VS_THEN_DRIVE=1, then
`tools/city-flicker.ps1 -NoSync` (scratch r10gate.ps1).

| gate (PSXLab) | base f96586ce | after real-heights-r10 |
|---|---|---|
| launch, uniform: city-wide LAUNCH / UNLOAD | 99 / 401 | 99 / 401 |
| launch routes uptown / tryon / independence | 0/0, 0/0, 0/0 | 0/0, 0/0, 0/0 |
| census freeway >0.1/0.5/1/2 % / joints; curves; TIGHT sag/crest | 29137/240/76/17/3; 1620; 229/66 | 29135/240/76/17/3; 1618; 228/66 |
| census ramp | 45295/1972/453/122/10; 6507; 1342/349 | identical |
| census arterial | 167631/2063/765/227/15; 18874; 2361/652 | 167633/2063/765/227/15; 18873; 2360/652 |
| census collector | 148279/5388/242/69/152; 18605; 1702/373 | 148279/5388/242/69/152; 18604; 1702/373 |
| census local | 97401/21447/99/9/78; 15024; 1662/101 | identical |
| route p99 uptown / tryon / independence (g) | 1.12 / 2.11 / 0.76 | 1.12 / 2.11 / 0.76 |
| WALLS IN LANES | 0 | 0 |
| FALL HOLES (places near a road) | 8 | 0 (the same 68 rays, now read as lot pavement on the lattice) |
| INVISIBLE lanes / solids | 4 / 816 | 4 / 816 |
| BLUNT on the routes | 1 | 1 |
| MEDIAN CLASSES breaks / open gaps / boxes | 0 / 0 / 0 | 0 / 0 / 0 |
| MEDIAN ACROSS one / two+ / none (6626 rays) | 3852 / 0 / 0 | 3829 / 0 / 0 |
| ...V class / wide (reported) | 115 / 2659 | 143 / 2654 |
| route rail ends met square (reported) | 7 | 6 |
| CITY AUDIT failures | 6 | 5 |
| flicker: depth-tie px / colour flips | 2123 / 3294 | 2125 / 3301 |

Every change explained: no road height moved (structure flags at 16 deck ends and land only), so the census moves by the
drawn sections at those ends alone (freeway >0.1 % -2 and curves -2, arterial >0.1 % +2 and curves -1, TIGHT sag -1
each, 4 fewer drawn sections city-wide); launch and p99 identical. MEDIAN ACROSS: 28 rays now cross a deck V (DECKV's
designed ends, the 1'47" one and Independence's e2338) and are its V class, as round 9's splits are. The flicker
finder's +2 px / +7 flips are scattered +-1 px in views no round-10 rule touches (tryon0-2, uptown0, with identical
drawn pixel counts) and +6 tree flips: run-to-run noise. Route rail ends 7 -> 6: the 1'47" parapet start (inside the V's
leg now). At Independence the DECKV V at e2338's end moved one "other road" rail end 12 m (e2339 -> e2341's rail end
met ahead along its edge line, at the V's mouth); other-road rail ends 35 = 35. Reports: PSXLab r10_base / r10_after
(vsmooth_census.txt, city_launch_after.txt, city_drive.txt, CITY_FLICKER_r10*.md).

## Next
1. BLUNT e9902/e279: find why the rail-end plan does not see that start (span flags at the ramp's emergence), give it the
   gore's turned-down start or a W-beam lead-in.
2. N Tryon's plate: name its triangles (a Roads-mesh provenance probe), then fix its sheet.
3. E Trade / Cates: junction geometry (a fan over the overlap; the sidewalk strip clipped at the fan's edge).

# (round 9 below)
# HANDOFF - CITY HIGHWAY SIGNS round 2 (city-signs-r2, 2026-10-29): posts collide as drawn, the "6", density, bridge-mounted, curve warnings, EXIT ONLY, 1990s lit signs, gores

Branch `city-signs-r2` (worktree `C:\Users\mcgee\PSX Racing-signs2`, off integration-1029d de4a7a10), sandbox
`C:\Users\mcgee\PSXCity` (AGENT_LOCK). Full write-up: Docs/CHARLOTTE.md "HIGHWAY SIGNS - round 2". Sheet (the base or
PSX_CITY_SIGNS2=0 vs round 2, noon + night, every frame labelled): `Docs/shots/highway_signs/highway_signs_r2_sheet.png`.
Code: `Scripts/City/CitySigns.Signs2.cs` (new partial: switches, leg colliders, density rules, bridge/deck mounting,
curve warnings + chevrons, EXIT ONLY, catwalk/luminaires/panel light, gore search, layout check, census stats, shot
spots) + hooks in `CitySigns.Guide.cs` / `CitySigns.cs`; `Shaders/PSXLit.shader` (`_NightVtx`: the legend's depth lead
and the UV2 lamp light), `NightGlow.cs` (halo-only heads), `CityWorld.cs` (sign luminaires into the tile's NightGlow),
`CityMeshes.cs` / `CityMeshes.Bridges.cs` (read-only `GorePaintedGapM`, `GirderDepthOf`), `Editor/PSXRacingBuilder.City.cs`
(guide material night flags), `Editor/CityAudit.Signs.cs` (Kind.Bridge), `Editor/HighwaySignsCheck.cs` (both rounds in
one run), `Editor/CityPreview.cs` (A/B eye shots, look-at views, sampling variants). Data: `tools/city/guide_atlas.py`
-> `Art/City/Signs/CityGuide.png` + `CityGuideFont.cs` (W1 diamonds, chevron, black down arrow; 1024 x 512 kept);
`tools/city/signs.mjs` honours PSX_CITY_CACHE; `Resources/charlotte_signs.bytes` regenerated from the shared cache
(195 -> 197 exits: the base's file predated integration-1029d's map); `export_osm.mjs --check`: EXPORT CHECK OK.

## Shipped (all ON; `PSX_CITY_SIGNS2=0` = round 1, or each `PSX_CITY_SIGNS2_<X>=0`)
- `LEGS` - per post + footing boxes where EndFrame draws them: INVISIBLE SOLIDS 816 -> 124 (the 173 leg boxes -> 0).
- `GLYPH` - the "6" (and every legend's bands at a distance): the green backing 4.6 / 9.5 cm behind the legend won the
  depth test once the 24-bit step passed the gap; legends now lead it by 3 depth steps in PSX/Lit (at most 1 m).
- `DENSITY` - MUTCD 2E.33 / 2A.17: overhead structures 388 (1.72 /km) -> 295 (1.31 /km); NCDOT ~1.01 not reached;
  post-mounted past the clear zone: 39 exit direction signs, 58 advance signs.
- `BRIDGE` - overpass fascia (brackets, 0.35 m off the parapet, foot at the soffit) or a sign bridge on its own deck:
  6 of round 1's 11 exits on structure now signed, + 1 advance sign (all 7 on a fascia; the own-deck path stands 0).
- `CURVES` - W1-1/2/3/4 + W13-1P from CityCurves at Table 2C-4 distances: 207 standing; W1-8 chevrons 920.
- `EXITONLY` - 1988 E11-1 style panels on the 32 lane-drop exits (63 panels).
- `NIGHT` - catwalk + mercury luminaires (1,031 halo dots in the tile's NightGlow draw) + UV2 light on the face.
- `GORES` - gore signs past the painted nose: 134 -> 189 standing of 197; on the painted gore 21 -> 1.

## Gates (PSXCity, base vs branch)
- Drive audit (CityVSmoothCensus + PSX_VS_THEN_DRIVE): WALLS IN LANES 0 -> 0, FALL HOLES 8 -> 8, INVISIBLE lanes 4 -> 4,
  INVISIBLE solids 816 -> 124 (sign-leg boxes 173 -> 0), CITY AUDIT the same 6 failures.
- Flicker finder: depth ties 2,126 -> 2,126 (57 of the 60 ranked pairs the same, each -1..+3 px; 3 swapped at the
  list's 2 px tail, none a sign's); colour flips 3,304 -> 3,236, on the guide draw 173 -> 106 (the rest: not legends,
  catwalks or warnings - structures both rounds draw).
- Sign audit: the base's 1 failing check (a tree on a business pole sign's ground) and nothing else (after the fixes
  in e5b58568 / 5e9c0d08: breakaway posts off deck margins and corner spots, chevron slides, a 30 m viewer; the clash
  check now per sign - a leg's two footings are one structure); LAYOUT CHECK 0 clipped / 0 overlapping / 0 two in a
  plane on 2,318 faces.
- `PSX_CITY_SIGNS2=0` draws as the base: the sheet's frames differ from the base's in 0 px at noon, 4-13 sky px (the
  stars) at night (after da73f907: every guide mesh carries UV2).

## Left
- Density 1.31 /km (the census does not split the overhead 295 by reason yet); 5 of the 11 exits on structure still
  unsigned (3 "no legs" on squeezed twin decks, 1 no overpass, 1 overpass too skewed); 17 exit direction signs no room
  in all (round 1: 23); 78 curve warnings + 114 chevrons with no free ground; 8 gore signs (5 ground taken, 3 the
  gore never opens within 400 m).
- The guide material's `_NightVtx` / `_NightTint` (the legend lead and the lamp light) are in the committed
  `Materials/CityGuide.mat` and re-set by `PSXRacingBuilder.CityGuideMat` on every city scene build and city tool; with
  `_NightVtx` 0 the legends draw as round 1 (bands at a distance) and unlit.

# HANDOFF - REAL HEIGHTS round 9 (2026-10-29): the last median breaks, gaps and boxes on the race routes

Branch `real-heights-r9` (worktree `C:\Users\mcgee\PSX Racing-medians9`, off integration-1029 806561d3), sandbox
`C:\Users\mcgee\PSXLab` (AGENT_LOCK). Full write-up: Docs/CHARLOTTE.md "REAL HEIGHTS - round 9". Sheet (before/after,
chase + top, every place): `Docs/shots/medians9/real_heights_r9_sheet.png`.
Code: `Scripts/City/CityMeshes.Medians9.cs` (new partial) + hooks in `CityMeshes.Unions.cs` (BuildDeckUnions passes,
EmitUnionMedian's split V, EmitUnionLegs' per-leg footprint), `CityMeshes.cs` (own Jersey / rail kept full height at a
split, split-foot section stations, the fan chord's Jersey / no verge under a carried-on median in BuildJunctions);
census `Editor/CityAudit.Median.cs`; tools `Editor/CityRealHeights.cs` (PSX_LOOK_AB=round9, "r9" drives),
`MedianProfileProbe.cs` (spot spec `T`: the land term per column), `CityPreview.cs` (`name:along:...` chase views).

## Shipped this round (ON by default; each `=0` is the before)
- `PSX_CITY_R9_SPLIT` - where the twins part past (or come into) the 9.5 m band the shared median SPLITS: two Jersey
  legs 1:6 to each carriageway's own Jersey, nothing turned down. Includes the cause of the last six BREAKS/GAPS: a
  run end "continued" only by its OWN partner's padded claim (Continued's twin test) was drawn with no end at all.
  Independence 161 / 290 / 2482 / 2600 / 4407, uptown 2211 / 7978.
- `PSX_CITY_R9_BRIDGE` - a gap of up to 60 m between two shared medians, twins within 12 m all along, laid as one
  (uptown 8688 BOX + 8727 OPEN GAP, I-277 e2139 / e8474).
- `PSX_CITY_R9_FANS` - (a) a shared median carried on past a twin's fan whose other arms all leave on its outside
  (Independence at Norland Rd 4507-4537: the grass rectangle cut out of the median); (b) a carriageway's own median
  Jersey carried across its own fan where nothing leaves into the median (every right-in/right-out on Independence:
  the median was open 10-30 m at each, both sides at once at Paul Buck Blvd).
- Census: split V class, WIDE read from the walls (the 1-2 m Independence "boxes" were two continuous walls of the
  11-17 m busway median, counted only by the walk's reason string), rays across the routes' own fans.

## Off / not fixed
- `PSX_CITY_R9_DECKEND2` OFF: the 1'47" pit (I-277 e2435 s 8-17 past the Elizabeth Ave deck) is NOT fixed. Cause: the
  twin deck e2437 starts ~14 m along e2435 (skewed abutments), the land under its footprint is dug to the creek channel
  (CityElevation.Ground's `underDeck` channel rule) and the 8 m lattice carries the dip to s ~17. A slab along the
  longer deck covered most of it but left the median parapet starting square (+1 BLUNT route) or stepping in blocks.
- `PSX_CITY_TWIN_RECIP` stays OFF: on top of round 9 the drive audit is identical (classes, ACROSS; WIDE 32 vs 31).

## Gates
Drive audit (PSXLab r9_ab2, same code but before the 7980 fix and with DECKEND2 still on) vs the base = this branch with round 9 off on
the SAME census code (r9_ab2/city_drive_r8.txt): MEDIAN CLASSES breaks/gaps/boxes 7/3/2 -> 1/0/0 (round 8's own census reported 8/3/10;
the one left, uptown 7980 e8478 s 13 2 m, fixed after: the partner's own Jersey kept full height at its padded end, the
final look r9_ab3's spot probe shows it full height); MEDIAN ACROSS two+ 5 -> 0, none 24 -> 1 (= that ray); split V rays 113,
WIDE-from-walls 32; fan rays none (nothing leaving into the median) 13 -> 0. WALLS IN LANES 0 = 0; FALL HOLES 8 places all
off-route = base; INVISIBLE lanes 4 = 4; INVISIBLE solids 830 = 830 (the base branch's value - round 8's 59 predates the
integration's other passes); BLUNT route 1 -> 2 (+1 at e2435 s 14.5 = DECKEND2, now OFF: expected 1).
NOT YET MEASURED: launch uniform / census per class / route p99 - the gate job (CityVSmoothCensus.Run, PSX_VS_LAUNCH=uniform,
PSX_VS_THEN_DRIVE=1) was started 17:29 on PSXLab with the final defaults: results in C:/Users/mcgee/PSXLab/r9_gate1
(vsmooth_census.txt, city_launch_after.txt, city_drive.txt) when it ends (~18:30). The solve is untouched (medians and fan
chords only), so the census/launch are expected equal to round 8's 102/395, routes 0/0/0, p99 1.12/2.11/0.76 - unverified.

## Next
1. 1'47": grade the land under a deck within the abutment band as a spill-through slope from the bridge seat (not
   the channel), twin medians only; or end both decks on one abutment line.
2. Continued's twin test reads a run's own partner as its continuation city-wide: elsewhere such ends are drawn with
   no end face (fixed here only where the median splits). Audit open-ended union Jerseys.
3. A median-crossing fan's own Jersey ends are still open (EdgeLinesUp is true at every fan node): a proper end
   treatment there.
4. The fan rays count "two+" at a fan where both carriageways carry their own Jersey across (a wide median): fine.

# (round 8 below)

# HANDOFF - ROAD LOGIC: BENDS (road-logic-bends, 2026-10-29): tight bends, ramp advisories, snaps, kinks

Branch `road-logic-bends` (worktree `C:\Users\mcgee\PSX Racing-bends`, base 1bde9af7 = integration-1029 +
road-logic-lanes). Sandbox PSXCity (AGENT_LOCK). Not merged, not pushed. Write-up: Docs/CHARLOTTE.md
"ROAD LOGIC - BENDS"; sheet Docs/traffic_census/bends_before_after.png.
- Exporter: `tools/city/lib/fillet.mjs` BENDS (collapse before the shares; starved-corner merge on freeway
  and trunk mainlines) SHIPPED OFF - `PSX_FILLET_BENDS=1`: freeway spikes 109 -> 30 but MEDIAN ACROSS two+/none
  25/24 -> 31/26, boxes 11 -> 13 on the routes (gate vs base, same job); section ADVS (export_osm.mjs, citydata.mjs, CityMap).
  Data regenerated; `export_osm.mjs --check` OK byte for byte. The re-run on merged code reproduces it.
- Runtime: `Scripts/City/CityCurves.cs` = THE curve advisories (EXIT sign, traffic, census read it);
  CityTraffic: Bisector (smooth lane heading), StretchStraight, Shape's control floor, BezMaxCurvature.
- Spot census: `tools\city-traffic-census.ps1 -Spots Docs\traffic_census\spots_bends.txt -Minutes 30 -NoShots
  -ReportDir <dir>`; shots of the sheet's curves: write census\top6.txt, `-ShotsOnly` with
  `PSX_CENSUS_SHOTEYE=driver+plan`.
## Next
0. Re-fit each divided road's twin carriageways TOGETHER (one offset), then turn BENDS on and re-run the median gate.
1. Corners beside mitred junctions (ramp split/merge ends, fixed in place and direction): about a third of
   the 30 freeway spikes left; needs the junction's arm direction freed (ComputeTrims reads it).
2. Ramp lines swinging off the mainline at the gore (OSM topology): spread over the gore taper.
3. Curve warning signs (W1-2 + W13-1P) from CityCurves spans - the player sees only the exit advisory now.
4. The vertical class (Billy Graham Pkwy crest R 29 m, junction-tile lips): the drawn profile's.
# HANDOFF - REAL HEIGHTS round 8 (2026-10-29): the owner's three median spots met; MEDIAN CLASSES driven down

Branch `real-heights-r8` (worktree `C:\Users\mcgee\PSX Racing-medians8`, off main 70e81508), sandbox `C:\Users\mcgee\PSXLab`
(`$env:PSX_SANDBOX='C:\Users\mcgee\PSXLab'`, AGENT_LOCK). Full write-up: Docs/CHARLOTTE.md "REAL HEIGHTS - round 8".
Code: `Assets/PSXRacing/Scripts/City/CityMeshes.Medians8.cs` (new partial) + hooks in `CityMeshes.Unions.cs`, one line in
`CityMeshes.RailContinuesPast`; tools in `Editor/CityRealHeights.cs`, `Editor/MedianProfileProbe.cs`.

## Shipped this round (all ON by default; each `=0` is the before)
- `PSX_CITY_R8_CHAIN` - a ground/approach run's road across includes the twin pieces' mitred continuations; an edge its
  median reaches gets an exact partner run. 0'54" I-277 e2311 notch: one cross-line at n3523.
- `PSX_CITY_R8_EXACT` - runs meet a partner run where its owner's median is DRAWN (feet of the owner's ends), not its
  padded claim (claims kept for Continued). 4'06" e1887 meets e1885 at 17.6 (was 15.6).
- `PSX_CITY_R8_FILL` - gaps up to 16 m between two drawn medians, twin beside in the band at one level: laid.
  4'06" e1894 57.7..60.3 (the hole between the decks).
- `PSX_CITY_R8_SEAM` - two runs owned on opposite carriageways that hand over draw one cross-line (each to the other's
  edge at the other's end, blended over max(3 m, 1.5 x the move)).
- `PSX_CITY_R8_DECKEND` - a closed Barrier run end whose owner's deck carries on over a pit (twin within 16 m, ground
  0.5 m+ under both roads) carries on to that deck's end, its V there. 1'47" e2437 s 8 -> 0: the pit box covered.
- `PSX_CITY_R8_MEDRAIL` - a divided freeway's median-side approach rail carries on across a plain 2-edge node inside its
  band on both edges (no union there). 1'47"+22 m n3658: the 3 m notch in the rail.

## One job for every gate (as round 7)
`CityVSmoothCensus.Run` with `PSX_VS_LAUNCH=uniform` + `PSX_VS_THEN_DRIVE=1` (~75 min on PSXLab; the drive audit alone
is ~30 min). Looks: `CityRealHeights.RunMedianLookAB` with `PSX_LOOK_AB=round8` (now re-builds the union runs per
pass: `ResetWorldTrims`); `RunDriveAB` (`PSX_DRIVE_AB="base;r8;r8+recip;r8+seam"`); `RunLookThenDrive`; spot probe
`PSX_SPOT_HEIGHTS=1`. Scratch job scripts: r8job.ps1 / look*.ps1 / gate.ps1 (session scratchpad).

## Gates
gate1 (all round-8 switches ON) vs baseline (round 7 g1r7 / this branch with round 8 off = main's medians):
launch uniform 102/395 = 102/395, routes 0/0/0; old 109/585; census per class = g1r7 (freeway 28900/243/80/19 vs
28901/244/80/19); route p99 1.12/2.11/0.76 =; WALLS IN LANES 0; FALL HOLES route 0 (8 off-route) =; INVISIBLE lanes 4 =
base (g1r7 3, older main); BLUNT route 1 = base. MEDIAN ACROSS two+/none 35/35 -> 22/28; MEDIAN CLASSES 17/3/15 -> 8/3/10 (one of the 8, 8690 e2139, is the
continuation fill, now OFF `PSX_CITY_R8_FILLEND=1`: expected 7/3/11 shipped, not re-measured).
Reports: `C:/Users/mcgee/PSXLab/r8_gate1` (gate), `r8_look2/city_drive_base.txt` (base), `r8_final` (A/B shots, drives).

## Next
1. The band-edge transitions (uptown 2211 e2144 s 349, Independence 161/2482/4407/4507/4531): a ground V at a union's
   closed end where the twins part past 9.5 m, its legs meeting each carriageway's own Jersey (the deck V's rule; it
   adds two-wall V stretches the census must exempt like InUnionEndV).
2. Independence's 10 wide-median "boxes" (walls 11-17 m apart at junction fans): the median nose across both Jerseys,
   or the census classing walls further apart than the band as WIDE whatever the walk's reason.
3. Open gaps at 9.4-9.6 m (290 e6407, 2600 e11248, 8727 e8474): one side owns a median the other refuses at 9.54 m.
4. 1'47": ~2.5 m of pit past the abutment line (e2435 s 7.5-10, the land under the bridge) - terrain, not medians.
5. `PSX_CITY_TWIN_RECIP` / `PSX_CITY_UNION_SEAM` stay OFF (measured apart on top of round 8 in r8_final); twin hold OFF.
# HANDOFF - BRIDGES + RAILS (city-bridges-rails, 2026-10-29): the owner's I-77 / Brookshire Fwy photos

Branch `city-bridges-rails` (worktree `C:/Users/mcgee/PSX Racing-bridges`, off main 70e81508). Not merged, not pushed.
Docs/CHARLOTTE.md "THE BRIDGES AND RAILS PASS" has the rules and the numbers; sheet Docs/shots/bridges/bridges_rails_sheet.png.
- Code: Scripts/City/CityMeshes.Bridges.cs (girders, hammerhead piers / bents, bearings, abutments, W-beam runs, OM-3
  markers, high masts, piers' clear zone), Slot.Structure + render-only TileMeshes.structure (CityWorld "Structure"),
  rumble paint in PSXRacingBuilder.City DrawProfileTex (mw profiles). Switches PSX_CITY_BRIDGES / _WBEAM_RUNS / _MASTS=0.
- Assets (sandbox-minted GUIDs, committed): Art/City/Pack/city_structure.png (d07fae16), Materials/CityStructure.mat
  (72177468), Resources/CityKit.asset (98 slots), the mw road textures (rumble). The publish never rebuilds the kit.
- One job: Editor/CityBridgesPass.cs (-executeMethod PSXRacing.EditorTools.CityBridgesPass.Run, PSX_BR_TAG, steps
  stats,shotsab,flicker,drive,launch; run with -force-glcore). Audits identical to main (see the doc).

# HANDOFF - CITY POP-IN + FLICKER (city-popin, 2026-10-28), passes 2+3

Branch `city-popin` (worktree `C:\Users\mcgee\PSX Racing-popin`, off main d133631a). Sandbox PSXRec (shared, AGENT_LOCK.txt).
- Far buildings: `Scripts/City/CityProxies.cs` + `CityMeshes.Proxies.cs` (EmitFootprint in skyline mode, 512 m chunks to
  2 km / 1.2 km phone, one merged mesh, the skyline's 3 own-depth draws, 4 submeshes). `PSX_CITY_PROXIES=0` = off.
- Tile fade + hand-overs: `CityTileFade.cs` (32x32 RGBA per-tile table) + PSX/Lit `_TileFade` modes 1-5 (CITY POP-IN
  blocks in vert/frag). `PSX_CITY_FADE=0` = instant. Skyline copies are mode 5 (per-pixel near/far split).
- Build ahead: CityWorld.NeedScore order, ring+1 lookahead over 25 m/s, 6/12 ms slices over 30 m/s while frames hold.
  `PSX_CITY_AHEAD=0` = the old nearest-first order.
- Flicker finder: `tools\city-flicker.ps1 -Tag X -Both -Shots` (OpenGL core). Results and what is left: Docs/CITY_FLICKER.md.
  Fixed: parts stand on what holds them (CityMeshes.StandOnH), barrier depth offset. 9866 -> 1465 tie px.
- Perf: `tools\popin-perf.ps1 -Tag X [-Before] [-AbSeconds 20]` (perf-probe under the lock, PSX_PERF_AB=popin rows).
- Open: CityFar's far window dots double the proxies' own lit windows within 2 km at night (needs a PSXHalo near cut,
  night-look's file); pack-model lots (29) and frontage boxes / fill houses outside the core have no proxy yet.

# HANDOFF - CITY TRAFFIC skeleton (2026-10-26): Charlotte free-roam traffic, check green, OFF by default

Branch `city-traffic` (worktree `C:\Users\mcgee\PSX Racing-traffic`, off main 8d22fe76). Not merged, not pushed.
Sandbox PSXBuild via `tools\city-traffic-check.ps1 [-NoWatch] [-CityPlay]` (takes/releases AGENT_LOCK.txt, mirrors
ONLY Scripts/Editor/Shaders, never Resources/Art).

## What it is
`Scripts/City/CityTraffic.cs` (new). Free roam only: `CityMode.Start` calls `CityTraffic.Attach`; races keep
TrafficSystem. Other hooks: `CarLights.externalBrake` (brake lamps without a CarController), `TrafficSystem.Keys` internal.
- Cars: TrafficSystem's car (shell on a box lifted 0.22 m, layer 2, CarLights, velocity servo, gravity off, height from
  `Edge.YAt`, turns raycast the junction surface). Knock > 1.5 m/s from the player/world = WRECK; traffic touching
  traffic < 4.5 m/s = counted BUMP.
- Plan = legs. RUN: one lane of an edge, lane centres from `LineModel.ExtentsBefore`'s band laid out as
  `RoadProfiles.LinesFor` paints (b->a lanes from the left edge, TWLTL, a->b lanes). TURN: circular-arc Bezier mouth to
  mouth; freeway ramps get a long turn through the merge/diverge. Routes: weighted random legal turn per node.
- IDM following (arc positions on runs), converging streams (straight > turn > ramp unless clearly ahead), player and
  wreck obstacles, crossing corridor in junctions. Limits = OSM maxspeed or class default; bends braked for.
- Junctions (`CitySignals.All`): red/yellow stop (4 m/s2 dilemma rule), starters wait for the box to clear, LEFT turns
  yield to oncoming everywhere, STOP = full stop then first-come / major-road gap, unsigned minor yields (equal rank:
  right first, 6 s impatience).
- Lanes: mandatory (turn lane, lane ends), passing, keep right (freeway); 3-5 s; gap acceptance; wait 6 m short then
  tighter gaps after 5 s, REPLAN after 14 s (missed turn, never a cut across a lane).
- Density: hour table (03:00 = 0.04 of cap, 08:00/17:00 = 1.0), cap 48 desktop / 24 phone, born 140-380 m unseen,
  weighted class x lanes, recycled past 480 m unseen.
- Switch: `CityTraffic.DefaultOn = false` (flip to ship); `PSX_CITY_TRAFFIC=1/0` overrides; tools set `Enabled`,
  `ClockOverride`, `CapOverride`, `Suspended`.

## Check numbers (run 2, code bc4092e6; report `PSXBuild\PSXRacing_city_traffic_check.txt`) - CITY TRAFFIC OK
| phase (120 s) | active min/mean/max | red stops / green goes / ran red | STOP stops / rolls | lane changes pass/mand/keep-right | off-pave / wrong-way / overlaps |
|---|---|---|---|---|---|
| RUSH 17:15 parked | 39 / 43.3 / 48 | 82 / 209 / 0 | 1 / 0 | 1 / 56 / 0 | 0 of 9567 / 0 / 0 |
| RUSH driving | 13 / 35.1 / 48 | 52 / 104 / 0 | 10 / 0 | 4 / 54 / 5 (3 freeway exits) | 2 of 7733 / 0 / 0 |
| 03:00 parked | 2 / 4.4 / 8 | 9 / 24 / 0 | 0 / 0 | 0 / 2 / 0 | 0 / 0 / 0 |
| 03:00 driving | 1 / 1.9 / 2 | 2 / 7 / 0 | 0 / 0 | 0 | 0 / 0 / 0 |
Whole run: 321 spawned, 0 game exceptions, 0 traffic-on-traffic wrecks, 0 bumps, 2 replans, longest queue 4; player
driven into a car wrecks it. Run 1 had 14 traffic-on-traffic contacts: same-edge gaps compared each car's metres into its
own leg (a car born mid-edge starts its leg there). Fixed with arc positions.
Frame cost (desktop editor): CityTraffic physics step mean 0.47 ms with 37 cars, 0.8-0.96 ms over the rush phases,
0.1-0.15 ms at night; whole-frame A/B vsync-capped (16.70 vs 16.68 ms). Phone not measured.
Pictures (not committed, ~0.7 MB each): `Docs\shots\citytraffic\citytraffic_{signal_queue,stop_sign,freeway,night}.png`.

## Open
1. Wrecks on the world: 2 cars hit `Barriers` on the straight turn at node 1407 (e1329 -> e809, lane 1 -> 2,
   (-1545,5423)); 1 grazed `Ground` on 5 m yield run e10973 (-1362,5377). Check e809's lane slots against its paint
   (`LineModel.LinesAt`) and the barrier/gore there.
2. Ramp merges = 0 in every phase (exits = 3): the merge path is unverified. Few cars ever route up an on-ramp (link
   spawn weight 0.5 x 1 lane; a ramp from a terminal is a 1.2-weight turn vs 6 for straight on), and the stat counts
   completed merge legs only. Test: spawn on an on-ramp link and watch it join.
3. Physics steps > 5 ms: 23 of ~36,000, worst 31-60 ms; 28 ms with only 2 cars at night, so most likely editor GC /
   JIT landing inside the stopwatch, not the O(n^2) work. Confirm with ProfilerMarkers.
4. Sparse at rush (48 cars over the ring), no right-on-red, no turn-bay lanes from turn:lanes, no tempers/weather
   port, random walks not A* trips, no replay in free roam.
5. city-play-check after the traffic check (PID 47120) ran while the coordinator's publish mirrored main into
   PSXBuild: not evidence for this branch. Run it once on the merge.

---

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

---

# HANDOFF - REAL HEIGHTS round 7 (2026-10-29): the two-level union Jersey; the owner's cutouts/boxes counted

Branch `real-heights` (worktree `C:\Users\mcgee\PSX Racing-heights`), sandbox `C:\Users\mcgee\PSXRec`
(`$env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'`; its built scenes predate the circuit removal: no scene-index tools there).
Typecheck: `tools\typecheck.ps1 -Project C:\Users\mcgee\PSXRec -Source "C:\Users\mcgee\PSX Racing-heights" -OutDir $env:TEMP\psxtc-heights`.
Full write-up: Docs/CHARLOTTE.md "REAL HEIGHTS - round 7" (round 6 above it).

## One job for every gate
`CityVSmoothCensus.Run` with `PSX_VS_LAUNCH=uniform` and `PSX_VS_THEN_DRIVE=1` (~55 min): census, launch (old + uniform),
drive audit (now with MEDIAN CLASSES). Looks: `CityRealHeights.RunMedianLookAB` (~8 min): two solves/builds, `_off`/`_on`
shots of `PSX_PREVIEW_CUSTOM` (an eye's 7th field = metres over the 1.2 m eye, 1.3 = chase height), `PSX_LOOK_AB=round7`
toggles the round-7 mesh switches (else the twin hold), `PSX_SPOTS` plans on the first pass, twin_spots.txt (the hold's
per-station WHY at the owner's spots). Reports: `C:\Users\mcgee\PSXRec\g1r7` (gates), `l1r7`..`l3r7` (looks).

## Shipped this round
- Two-level union Jersey (`PSX_CITY_JERSEY_TWOLEVEL=0` = before): each face 0.81 m over its own carriageway, cap sloped.
  The "goes up and down" wall at any step; no run/collider position/ray changes (gates = g5r6).
- Drive audit MEDIAN CLASSES (breaks a/b, open gaps, boxes c) - g1r7: 18 / 3 / 15 places on the routes.
- Hold diag: why 1'06" never moves (e2321 all merge zone; e1394 an 88 m piece between junctions) - see CHARLOTTE.md.

## Baselines (g1r7, all round-7 switches on; shipped = g5r6 + the Jersey top)
launch old 109/585, uniform 102/395, routes 0/0/0; census = g5r6; route p99 1.12/2.11/0.76; MEDIAN two+ 35 / none 36
(35 shipped); classes 18/3/15; rail ends 1; WALLS IN LANES 0; INVISIBLE lanes 3; FALL HOLES route 0 (8 off-route).

## Next (in order) - DONE or carried in round 8 above
1. The 0'54" notch (I-277 e2311 s ~1, uptown 617 m): `PSX_CITY_UNION_SEAM=1` holds a 1.5 m inset overlap past a
   carried-on node end (written, never run) - look at rh7_c054n with it, then gate; or one canonical node cross-line.
2. The 18 breaks / 15 boxes (MEDIAN CLASSES list in g1r7/city_drive.txt): 4'06" e15241 (own parapet s 0-2.5: its foot
   falls on e1894 whose first 4.1 m pair with e1891; s 16-19 a 2 m e1894 piece in no run), 1'47" e2435/e4593 wide median
   past the e2437/e2438 deck V (own approach rails ending sloped in a 14 m grass median), Independence junction fans.
3. `PSX_CITY_TWIN_RECIP` / `PSX_CITY_UNION_SEAM` measured apart (together: none +1 at I-77 e1880 s 45).
4. Twin hold stays OFF; e9877 s 18 rail end; mitre snap alone; 8 off-route deck holes.
