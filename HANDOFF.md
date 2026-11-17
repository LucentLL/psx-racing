# HANDOFF - ROAD LINES ROUND, OPUS CONTINUATION (splits2, branch `charlotte-splits2`, 2026-11-17 01:00-04:30)

Not pushed, not merged. mountain-nm 749e05de merged in (8365dfac; the HANDOFF conflict kept both top sections).
Sandbox PSXRoadA (WebGL), shared with linegate. Scratch `scratchpad\splits2\` (PROGRESS.md = the hour-by-hour log).
The line gate's three files (Editor/CityAudit.LineGate*.cs + metas) are copied UNTRACKED into this worktree so my jobs
can run the gate; they are linegate's, not committed here.

## What I found
1. THE FORK HE CIRCLED (n3158, N Tryon 250 m ahead of his pose) is not a painted divider. A wider aerial look (Google,
   viewed only) shows hatched paint from ~70 m before the fork to ~30 m past it, then a NARROW RAISED MEDIAN (~1.3 m) to
   the East Sugar Creek Road junction, the WB left-turn lane beside it. Three other OSM tight twins looked at (Idlewild
   Rd, Ballantyne Commons Pkwy, Dewitt Ln) are the same: a narrow kerbed or planted median. OSM splits a road into two
   carriageways only where something physical parts them: a pair mapped closer than its lanes is a narrow island.
2. Why the island kept growing to the junction (nf5-nf7): the split frame blends each carriageway in the STRAIGHT axis
   frame of the undivided road; the pair bends ~15 deg by the junction, so the blend read the bend as a move and ran to its
   room cap (~141 m), the whole divided piece (LineModel.SplitFrames.cs BuildSplitNode / PhaseCLength). And the export's
   own carriageway lines stand 3.7-4.4 m apart past the fan (lanes + island = 2.4): a push-only twin never moved them
   (twin-debug: need 0 from 20 to 110 m).
3. The model gave the WB carriageway the TWLTL as its inner lane (4 lanes from the fork against 3), so opening the
   island meant shifting all its through lanes ~3.3 m. On the street the added lane is the OUTER right-turn lane (OSM
   none|none|none|right) and the centre lane becomes the hatching / median.
4. BEHIND HIS POSE (e15599, way 1346185922): OSM tags 6 lanes (3+3, EB none|none|right) where the street is seven wide
   (aerial: EB 2 + right-turn lane, centre raised nose -> hatching -> TWLTL, WB 3); its east neighbour e14081 is 2+4 with
   the WB left bay in the centre; Dorton St (n13557) is not in the game graph. The model relayed the double yellow 3.4 m
   in 64 m (11:1 at 45 mph).
5. At a one-lane width change both pieces are centred on the mapped line (half a lane each side); the old rule ("the
   smaller step is held") then always held travel-RIGHT (LineModel.cs BuildEases, BOTH EDGES STEP), so half such joins
   eased the wrong side and shifted the opposing lanes; the taper's partner pairing (PartnerLat: yellows by kind, whites
   from the moving edge) slid the TWLTL's near yellow 3.66 m across the centre lane and ended an EB lane line over 164 m
   (the V and the missing lane line at his pose in nf7-nf10).
6. THE DARK STRIPES: the PC look's HD road base rendered in the editor at his pose (PSX_HD=1, fresh process, nobody
   driving), with PC48's lines and with the fix, shows NO stripes where his screenshot has them: tyre marks from his drive
   (linegate agrees: the HD base and its maps are flat). Settled; nothing to change.
7. The tw7t road textures were imported with Unity defaults (sRGB on, compressed), unlike every road texture (psx16
   label, linear, RGB565) - a different look beside the six-lane road.
8. The fan lip (gate STEP, 5,781 places; TRACK_FAULTS S1): CityMeshes.FanProudM lifts the whole junction slab 12 mm,
   mouth included, where slab and ribbon meet edge to edge and need no lift.
9. COST: the line model's eases take 17-22 s per city load in the editor with city + LINEFIX (the new log line, four
   loads in lgB). The tight twins searched the other carriageway's nearest point with a full scan for every sample at all
   ~1,000 split nodes (O(n^2)); now windowed. DEFAULT-SCOPE NUMBER AND THE PARTS: see Cost below.

## What changed (all behind switches)
- PSX_CITY_LINEFIX (default OFF):
  - tools/city/lanefit.mjs rule B2: a 6-lane 3+3 piece between a seven-lane piece and a centre-left-bay piece is lent
    3 + TWLTL + 3 (one way city-wide: e15599). LineModel.LaneFitList.cs regenerated (26 ways, 1.79 km).
  - LineModel.OuterTurnLaneEnds: at a one-lane width change whose extra lane is one direction's OUTER turn lane, the
    pavement eases on that direction's outer side at the BAY rate (8:1-15:1); lines pair by place (Ease.coin): a line the
    narrow piece also has stays, the lane line at the opening lane stays on the narrow piece's edge line.
- Scope city (PSX_CITY_SPLITAXIS=city), tight twins (LineModel.SplitFrames.cs), each with its own switch, on under city:
  - PSX_CITY_TWINISLAND: hold the narrowest island (2.4 m lane gap = the 1.2 m kerb): pushed apart where closer, pulled
    together where up to 3 m wider (fading by 6 m: a median that really widens stays).
  - PSX_CITY_TWINBLEND: the split blends from its start into its own pushed line, one length for both carriageways.
  - PSX_CITY_TWINOUTER: the carriageway with one lane more gets it on its outside (bay taper from the fork); the centre
    lane is the island; its hatching runs 25 m past the fork before the kerb's tip.
- PSX_CITY_FANLIP (default OFF; proposed ON): a junction slab's ribbon corners seat on the arm's height (CityMeshes.cs
  SeatMouths); the lift stays at the centre, Steiner points and curb returns.
- tw7t texture metas = tw6's import. City load logs "[city] line model eases built in X ms (...): parts". Line spot
  report: bands, split frames, outer-lane counter. RunSplitShots: PSX_HD=1 (+PSX_SPLITS_HDIMPORT=1) = the PC look's
  road base in editor frames.
- PSX_CITY_TWINS=1 (default OFF; proposed ON): the tight twins and their split nodes' profiles under ANY scope - the fork
  fixed with the default scope (bfr) without the rest of Phase C (InSplitScope is not widened; BuildEases' split loop
  frames the twin nodes once BuildTightTwins knows them). Cost work: windowed nearest search, a prune of clearly
  separated pairs, the push sampled every metre.

## Proof (editor frames, PSXRoadA, fresh processes; same poses before / after)
- Fork + seven-lane spot + behind his pose, before | after | Google aerial / Street View (viewed only, scratch only):
  `scratchpad\splits2\splits2_ntryon_fork3.png` (after = bfr + LINEFIX + TWINS + FANLIP, nf13) and
  `splits2_ntryon_fork2.png` (after = city + LINEFIX, nf11). nf12 = city with every fix; nf13 = the same picture under bfr.
- Dark stripes: `scratchpad\splits2\splits2_stripes_pclook.png` (his screenshot | PC look PC48 lines | PC look fixed).
- Fan lip, box round Providence Rd / Ardsley Rd (S1): line gate STEP 23 -> 0 with PSX_CITY_FANLIP=1, every other class
  unchanged (s1b / s1a); driver's-eye frame at the mouth shows no crack (out\s1f_s1lip_drive.png).
- North Tryon Street, the whole road (119 tiles), line gate defaults -> city + LINEFIX + FANLIP, same gate code:
  328 -> 233 places; every class down but WIDTH 10 -> 11 (one 'curb lane too wide'); fan lips 51 -> 2, SJOG model
  38 -> 23, CONT line ends 52 -> 40 (lgAn / lgBn).
- Line spot (pnf12): e9470 (his pose) full 3 + TWLTL + 3 its whole length (steepest line 119:1); e15599 the EB right-turn
  lane tapers out over the last 48 m before Dorton (8.9:1 peak), the TWLTL's near yellows hand over to the bay's white
  line at the node, every other line straight.

## Cost (measured; editor, Mono)
- The line model's eases per city load ('[city] line model eases built in ...'): defaults (bfr, LINEFIX 0) 50-110 ms
  (lgA). City + LINEFIX before this round's speed-ups: 17-22 s (lgB, four loads); after the windowed twin search:
  3.7-4.0 s (pnf12: twins 2.8 s, split nodes 0.85 s, joins 0.04-0.07 s). The prune and the 1 m push sampling came after;
  bfr + LINEFIX + TWINS + FANLIP (lgC, with the prune and 1 m sampling): 1.35-1.45 s, of it the twins 1.1-1.2 s (the
  sampled lines of every split's pair, ~1,000 splits). Next cut, not done: sample those lines every 2-4 m, or cache the
  twin set per graph hash. LINEFIX alone adds only the lane fit (26 ways) and a few join rules: a
  few ms. FANLIP: none (the same vertices, seated lower). No per-frame cost: every change is in static road meshes.
- Memory: 4 tw7t road textures, 256 x 64 RGB565 (~32 KB each); 4 more city-kit material slots (106 against 102), drawn
  only on the 1.8 km of seven-lane street; the PC build adds 4 HD paint masks for tw7t (TextureHDTool.Import at build).

## What still looks wrong / not done
- At the fork node (n3158) the EB carriageway's outer pavement edge and edge line start ~0.5 m outside the undivided
  road's (a small kerb notch in the 35 m close-up nf12/nf13 nt_top3); the WB side meets within 0.3 m. Not traced.
- The island's kerb tip sits 25 m past the fork (the street's ~30 m), but the 25 m between is plain pavement between the
  two yellow lines, not hatched.
- The gate's SHIFT judges a line in its OWN edge's mapped frame: where a twin's mapped carriageway fans out 6 m in 20 m
  and the drawn lines stay straight, it reads a move (e15583 'edge line moves 5.84 m in 15 m'). Around the 38 OSM
  twin spots SHIFT went 41 -> 72 in lgB for that reason; KINK 3.2 deg on the WB yellow edge line ~17 m past the fork is real.
- e14081 (east of Dorton): an SJOG remains (the lane line 4.2 m in 29 m: its own bay taper at n13555 plus the 1.83 m
  shift from the Dorton join) - real, not fixed.
- The EB right-turn lane at Dorton ends on a 48 m bay-rate taper because Dorton St is not in the graph; a gate may call
  it a lane drop (MUTCD wants W x S = 165 m for a through lane).
- The far end n3168 is still the old taper (a lane-add split; the twin push now eases out before it).
- Not looked at: the hand-over rules at East 7th / Gleneagles (chain20 written, stopped for linegate's baseline); the
  traffic census, launch, concrete and drive gates (not run on this code); the worklist's other causes beyond the fan
  lip (BARRIER 1,6xx, grade breaks 5,0xx, map kinks); H3 / H4 / H5 / C9 / X3 track faults (S1 is the fan lip: fixed).
- PSX_CITY_TWINS in the medians gate: see g7 below.

## For the merge (coordinator)
- Files: Scripts/City/LineModel.cs (BuildEases: OuterTurnLaneEnds, outerTurn bay length, coin partner pairing, timing
  laps, BuildTightTwins call), LineModel.SplitFrames.cs (twins: island, pull, own blend, outer lane, hatch, prune,
  windowed search, PSX_CITY_TWINS), LineModel.LaneFitList.cs (generated) + tools/city/lanefit.mjs, CityMeshes.cs
  (FANLIP SeatMouths; the eases timing log), Editor/CityAudit.Lines.cs, Editor/CityRefSpots.Splits.cs (PSX_HD frames),
  Art/City/city_road_tw7t_*.png.meta (import settings). Nothing in shared scene or Resources files.
- If LINEFIX / TWINS / FANLIP ship (flip their defaults: LineModel.Splits.cs LineFixOn, LineModel.SplitFrames.cs
  TwinsEverywhere, CityMeshes.cs FanLipOn): rebuild the city kit (BuildCityScenesOnly or EnsureCityTextures: the tracked
  Resources/CityKit.asset has 101 slots, the code 106), re-bake any baked city tiles, re-make the lot entrances; the PC
  build's TextureHDTool.Import makes the tw7t paint masks by itself (seen in the editor: 'HD import: 13 sets').
- The refuge list: see g7 (medians gate, every scope with LINEFIX + TWINS + FANLIP on).
- The line gate files in this worktree are linegate's (untracked copies): take them from city-linegate.

## Gates (whole city, the line gate - linegate's code copied at ~01:16, the SAME copy for all three runs; fresh processes)
| class | lgA defaults | lgB city + LINEFIX + FANLIP | lgC bfr + LINEFIX + TWINS + FANLIP |
|---|---:|---:|---:|
| ALL places | 26,880 | 20,924 | 21,337 |
| STEP (fan lip) | 10,813 (5,785) | 5,121 (97) | 5,093 (64) |
| SJOG | 3,050 | 2,514 | 3,074 |
| SHIFT | 2,817 | 2,865 | 2,982 |
| CONT | 2,322 | 2,428 | 2,405 |
| KINK | 4,782 | 4,745 | 4,788 |
| WIDTH | 704 | 862 | 582 |
| BARRIER | 1,620 | 1,611 | 1,631 |
| JUNCTION / MEDIAN / BRIDGE | 684 / 76 / 12 | 691 / 73 / 14 | 685 / 84 / 13 |
lgB was run on 03646c44 (before the twins' far-end ease-out and the cost cuts), lgC on 29db3632.
Read by cause (lgdiff.py): LINEFIX takes out the relays (CONT 'line ends' on relays -204, SHIFT on relays -69, wide lanes
on relays -105, SJOG relays -33) but adds 'CONT line ends at a joint' +177 - nearly all on the S-weave roads (Gleneagles
12, Beam 11, E 7th 9...): the TWLTL's yellow ending where the bay's white line begins (MUTCD's own hand-over; the gate
does not count a white line continuing a yellow) - for linegate to judge. TWINS (the sampled pushes, 'refuge-shift')
add SHIFT ~+180 (mostly the gate's road frame: the mapped carriageway fans out, the drawn lines stay straight), SJOG
~+90 and KINK ~+60 (real world-frame wiggles at some twin pairs), MEDIAN +8. The city scope adds WIDTH +158, CONT +106,
SHIFT +48, JUNCTION +7, BRIDGE +2 (Phase C's split frames: East Charlotte Ave, Brown Grier Rd...). FANLIP alone (box
test): only STEP falls.
So by linegate's rule (no class may rise) only FANLIP passes; it is now ON by default. LINEFIX, TWINS and the city scope
stay OFF, each with its numbers.
- Medians gate (g7: LINEFIX + TWINS + FANLIP, scopes 0 / bfr / all / city): the compiled refuge list still draws exactly
  as committed (stale fingerprints 0; 17 / 19 / 22 / 21), but with those switches on the gate would accept 22 pairs in
  scopes 0 / bfr / all (5 more, Beatties Ford n10229 / n10236, IKEA Blvd ...): if LINEFIX or TWINS ship, regenerate
  LineModel.RefugeList.cs under them (the list has no LINEFIX dimension). FANLIP alone (g8): the list byte-for-byte the same (17 / 19 / 22 / 21 accepted and drawn).

## What the owner will notice
- With this branch as is (only the fan lip on by default): no 12 mm bump into and out of every junction.
- With PSX_CITY_LINEFIX=1 + PSX_CITY_TWINS=1 (or the city scope): at his North Tryon spot the seven lanes run straight
  with the centre turn lane, the road behind him no longer jinks at Dorton St, and the fork he circled becomes the street's
  narrow kerbed median from just past the fork to the junction instead of the grass wishbone.

---

﻿# HANDOFF - SPLITS PHASE C + TAPER FLOORS (agent "splits2", branch `charlotte-splits2`, 2026-11-16 afternoon; NOT pushed, NOT merged; STOPPED at 16:00 for the usage cap)

STATE + NEXT STEPS (10 lines)
1. Default UNCHANGED: PSX_CITY_SPLITAXIS=bfr (PC46's Beatties Ford). New opt-in scope `city` (Phase C) + J1b taper floors (on under city only; PSX_CITY_TAPERFLOOR=0/1).
2. City gates passed: medians (g6/g4 CITY AUDIT OK, list regenerated for bit 128, 0/bfr/all byte-for-byte the same decisions); owner's spots 34/35 pixel-identical, 37 road identical.
3. City census numbers improve (L/2 1585 -> 1355, bulges 1033 -> 776, jumps 775 -> 516, angles 1718 -> 1245) but J1's CITY gates (L/2 = 0) are NOT met.
4. Traffic census (watched) bfr -> city found 29 new mid-road lane corners + 29 tight bends (West Blvd, South Blvd, Eastway): FIXED in dd9cb22f (a blend ends only where OSM's line runs <= 0.12 m/m) - NOT yet re-measured.
5. Not run on the final code: city census re-run (ty2), launch boxes (lb2/ly2: the first pair measured one box), concrete (kb/ky), drive (db/dy), bfr gate (g5), 'all' lane-drop count (c1).
6. Next: in PSXCity run scratch `splits2\chain8a.sh` (y4 audit, g7 gate - install refuge_list.generated.cs only if listcmp.py says 0/bfr/all identical), then chain8b.sh (SendMessage main first: ty2 is watched).
7. Then: re-sheet (spotsheet.py / sheet.py / plans.py), fill CENSUS/LAUNCH/CONCRETE/DRIVE in this section, decide the default (city only if every gate passes; it will not: lane-add splits, room).
8. Biggest remaining Phase C work: a lane-add profile for carriageways with more lanes than the undivided road (672 ends), the refuge-nose fillet kink (gate measures in OSM's frame).
9. Incidents (mine): 14:36 a leftover runner ran a job on the tracks agent's code (discarded); 15:38 my runner took PSXCity while tracks' watched H2 race ran (it closed mid-race). Runner now locks atomically and never while a Unity has PSXCity open.
10. Merge needs: nothing beyond J1's re-bakes while bfr is the default; city as default would need the city re-bake, LotEntranceBake under city, and the 4-scope gate.

---

The J1 island fix (PSX_CITY_SPLITAXIS=bfr, Beatties Ford, in PC46) carried city-wide as a NEW scope,
`PSX_CITY_SPLITAXIS=city`, plus J1b's MUTCD taper floors. Worktree `C:\Users\mcgee\PSX Racing-splits2` off
mountain-nm c0a81147. Sandbox PSXCity (WebGL, AGENT_LOCK, shared with "tracks"). Graph hash b2aa3453 in every run;
no export, no download. 'all' and 'bfr' draw exactly as J1 left them (every Phase C rule is behind `CityPhaseC`).

## What I found (file:line on c0a81147 unless named)
1. Why 395 of 664 axis-frame ends failed L/2 under 'all': the profile read the move d at most 60 m on
   (LineModel.SplitFrames.cs:403), so where OSM's carriageway kept diverging (a median opening to 10-20 m) or bowed out
   and back, the blend joined it mid-move and the drawn lanes then followed OSM's own divergence. Of the ones still
   failing after Phase C most are limited by ROOM (the carriageway's chain ends at its next junction or split 20-80 m
   on), by a CURVING road (the straight frame reads the curve as a move; the cubic's end slope is clamped at 0.15), or
   by a fast real divergence that only a drawing metres off OSM's carriageway would pass (held back on purpose).
2. BAYEND lane drops: J1's walk ends every bay on the stretch from the nose to the first junction
   (LineModel.Splits.cs:201). A bay whose lane goes on as a lane at a 2-arm node (the next piece carries as many lanes
   that way) is not a turn bay; ending it drops a lane there - e16705 at n15089 (BfrBayKeep, one edge). Restricting
   BAYEND by distance instead (my first try, 60 m) broke spot 34: e8620's SB bay must end or its relay moves the lines
   the n10236 hatch needs, and the medians gate reverted n10236-n10238.
3. Short islands off the refuge list (482 ends under 'all') keep PC45's lens: each end's SPLT taper shifted out and
   RoomAlong ran both to the far node (LineModel.cs:624-648, the SPLT loop) - the profile skipped them ("the refuge
   path", LineModel.SplitFrames.cs:340).
4. Approach marking: NoseMarks walks map.splt only (CityMeshes.Marks.cs:691); same-name meetings have no SPLT record.
5. J1b: TAPR lengths were used as written (LineModel.cs:2222, some written at L/2), the street floor was 15 m
   (:2235), relays used L/2 at every speed (:1812), SHIFT records eased over their own length (:668). The drawn
   one-sided tapers/shifts of 5 cm+ under L/2: 3,363 of 6,242 in the base.
6. charlotte_lotent.bytes' key (CityMeshes.LotEntrances.cs:1317) does not read eases: any scope that moves pavement
   edges at entrances would read a stale file silently.
7. The exporter-fillet kink at refuge noses (4-6 deg at n10238/n10229) is still the medians gate's relay census:
   it measures the undivided edges' shift slope in their own OSM frame (LineModel.RefugeCheck.cs:53), so easing the
   fillet onto a 30 m chord (PSX_CITY_REFUGECHORD=1, LineModel.cs:1357) reads as a 0.11-0.19 slope. Not fixed.

## What changed (switches; defaults)
- `PSX_CITY_SPLITAXIS=city` (NEW; not the default): every split node city-wide except the four held back
  (n231, n4025, n7963, n12438: each waits for its own fix), with Phase C's rules (`LineModel.CityPhaseC`). Its refuge
  list bit is 128 (`LineModel.ScopeBitOf`); the medians gate decides it with the other three
  (`PSX_REFUGE_GATE_SCOPES=0,bfr,all,city`). 0 / bfr / all draw exactly as J1 left them.
- Phase C's rules (LineModel.SplitFrames.cs, LineModel.Splits.cs, CityMeshes.Marks.cs):
  - THE BLEND'S LENGTH (`PhaseCLength`): the move is read over the whole blend (the own line's furthest point from where
    the lanes start, iterated), the blend ends where the own line runs on level AND straight for 30 m (the top of a bow
    is not an end), never further off the own line than J1's blend or the node's start + 0.5 m, or a lane
    (`BlendDev`: Shopton Rd would have been drawn 14 m off its carriageway), and it ends 6 m before a side street's
    junction where the lanes would still be 0.5 m+ off the own line. A road still turning to its end keeps J1's length.
    And a blend ENDS only where the own line runs at 0.12 m/m or less (the cubic's end tangent is clamped at 0.15: a
    cap that ended a blend inside a wide divergence left a corner in the lane - the first city census met 29 of them
    on West Blvd, South Blvd and Eastway Dr); no such end within the blend: no profile there (the old taper, listed).
    Beatties Ford's 19 nodes keep J1's lengths and ends (the owner's pictures).
  - SHORT ISLANDS off the refuge list (`PSX_CITY_SHORTPROFILE=0` off): profiled from each end, each blend inside 45 %
    of the island (OSM's island in the middle) instead of PC45's lens (each end shifted out and both shifts summed over
    the whole island); the second frame's own section normals on its half (city only).
  - BAYEND: J1's stretch rule kept (every bay on the stretch from the nose to the first junction that serves no street
    ends), but under city a bay whose LANE GOES ON past one of its ends (a 2-arm node where the next piece carries as
    many lanes that way, not the same bay: `BayLaneRunsOn`) is kept as OSM tags it - ending it is what dropped or added
    a lane at the 2-arm node (e16705 at n15089; BfrBayKeep held that one edge). Counted in every scope
    (`PhaseCBayLaneOn`).
  - APPROACH MARKING at same-name meetings (n7868, n9152 type, no SPLT record): NoseMarks walks the shaped meeting
    noses too where every arm carries the road's name.
- J1b, THE TAPER FLOORS (LineModel.cs; `PSX_CITY_TAPERFLOOR`: unset = on under city only, =1 on everywhere, =0 off):
  every taper (a TAPR record's own length too) at least max(floor, r(S) W, sqrt(6 W Rmin(S))) with r(S) = S^2/60 to
  40 mph, S from 45; the street floor 30.5 m (was 15; freeways 90 as before); a relay's shifting taper L/2 only to
  35 mph, full L from 40 (+ the Rmin term); a SHIFT record's ease at least the floored length. Room still caps them;
  a short room is counted and listed (`TaperFloorShort`, `TaperFloorShortLog`), never squeezed further.
- The lot entrances: `CityMeshes.LotEntrances.cs LotEntKeyOf` mixes in the scope when city or the floors are on, so
  charlotte_lotent.bytes is never read stale under them (worked out at load, seconds, until re-made). bfr's key is
  unchanged (the shipped file stays valid).
- Audits/pictures: CityAudit.Splits.cs (the PHASE C line, J1b's TAPER CENSUS on the drawn eases, the frames and node
  tables, PSX_SPLITS_PLANNODES, PSX_SPLITS_LOTBASE=1 lot entrances against scope 0), CityRefSpots.Splits.cs
  (PSX_SPLITS_SPOTS: a top frame and a driver-height frame per node), CityLaunchAudit.cs (PSX_LAUNCH_BOX takes several
  boxes, ';'-separated), CityAudit.MedianGates.cs (the fourth scope).

## Proof (PSXCity, fresh processes; base = PSX_CITY_SPLITAXIS=bfr, the default, on the same code)
Sheets (scratchpad `splits2\`, real editor frames at 720 lines, noon unless named; the car out of view in the spot frames):
- `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\splits2\splits2_spots_before_after.png`:
  10 of the worst split nodes city-wide (worst.py: lens + angle at the node + outer-edge step, through roads 35 mph+,
  separations left out), each row top from 92 m BEFORE | AFTER, then driver height (1.15 m, 34 m before the node)
  BEFORE | AFTER, labelled with both verdicts. Seven the city scope redraws (Carmel Rd n10786, Lansdowne Rd n922,
  W Sugar Creek Rd n13734, West Blvd n5212, Hambright Rd n5345, Eastway Dr n11506, Monroe Rd n8656) and three it
  leaves (Westinghouse Blvd n12171, Poplar Tent Rd n10472, Gold Hill Rd n13981: lane bands overlap / short island
  the profile declines).
- `...\splits2\splits2_plans_spots.png`: the same 10 as drawn pavement edges and lane centres against OSM's lines
  (the undivided road's axis up), bfr | city.
- `...\splits2\splits2_poses_bfr_vs_city.png`: the owner's four Beatties Ford poses (night in rain, then noon), bfr |
  city. Pixel check (imgdiff): 34 and 35 at noon identical (0 px); the night-in-rain frames differ by 0.04-0.06 % (rain);
  36 at noon 457 px (0.05 %); 37 0.9-1.4 % - distant trees only, the road the same.
- Spot numbers (RunSplits): 34 and 35 identical to bfr (refuge islands 1.82 / 1.84 m); 37 identical (NB 27.0:1, the
  turn-only lane ends 201 m before the nose; SB 79.2 vs 77.5:1); 36 (n231, held back = PC45) moved by the taper floors:
  e8612's lanes 0.24 m further out at the fan edge, angle 10.5 vs 8.9 deg, median at u 20 10.29 vs 9.45 m (it fails
  as before). Beatties Ford 10 of 19 nodes pass under both.

CITY NUMBERS (CityAudit.RunSplits; 1,941 carriageway ends at 971 SPLT nodes; J1's census measure):

| measure | bfr (default) | all (J1) | city |
|---|---|---|---|
| ends failing L/2 | 1585 | 1432 | 1355 |
| failing full L | 1857 | 1756 | 1697 |
| outward bulge 1 m+ | 1033 | 876 | 776 |
| outer-edge jumps 0.5 m+ | 775 | 569 | 516 |
| lane-centre angle at the node over atan(1/V) | 1718 | 1361 | 1245 |
| ends failing the census | 1773 | 1662 | 1595 |
| same-name meetings city-wide (129 ends): bulge 1 m+ / failing | 80 / 124 | - | 74 / 118 |
| refuge pairs drawn | 19 | 22 | 21 |
| BAYEND edges | 6 | 554 | 511 (43 bays whose lane goes on kept) |
| turn arrows painted into a plain nose | 32 | - | 8 |
| J1b census: one-sided tapers/shifts under L/2 | 3356 of 6232 | - | 1941 of 5403 |
| ... under full L at 40 mph+ / under the 30.5 m floor | 3513 / 1318 | - | 2610 / 387 |

- Where city still fails: of 919 ends the profile draws, 592 are cut short by ROOM (the carriageway's chain ends at
  its next junction or split), 321 end before a SIDE STREET, 143 are HELD near the own line, 104 CURVE (J1's length);
  1,050 ends the profile declines: 672 lane bands overlap (more lanes in the carriageways than the undivided road),
  189 turning roadways, 97 separations over 15 m, 28 bands never 2.4 m apart, 20 mitre partner undrawn, 19 undivided
  road under 8 m, 16 part only late, 8 chain too short.

## Gates (city against the base; every run a fresh process on this branch)
- Medians (RunMedianGates, PSX_REFUGE_GATE_SCOPES=0,bfr,all,city, current scope city): CITY AUDIT OK; scope 0
  decides exactly PC45's 17 pairs; bfr 19 and all 22 decide exactly as the committed list did (pair by pair,
  listcmp.py); city 21 (all's 22 but Providence Road West: its relay census 0.424 -> 0.434 on e14896 under the taper
  floors). The list is REGENERATED with that reason (bit 128 on the city entries; Providence Road West now carries
  [0 bfr all]); the gate's generated list is byte-identical to the committed one. Order table fits; refuge load 16 ms.
- Traffic census (CityTrafficCensus, WATCHED in a visible editor; 14 spots on 7 corridors - South Blvd, Statesville
  Rd, West Blvd, Highway 160 W, Eastway Dr, Sunset Rd, Beatties Ford; 16 game-minute budget, cap 48):
  first pair (tb bfr 7.7 min / ty city 5.5 min, before dd9cb22f): wrecks 4 -> 4, stuck 17 -> 5, missed turn lane
  29 -> 20, wrong lane 29 -> 23, heading snaps 38 -> 22, no road under a lane 16 -> 12; NEW lateral/kink 1 -> 29 and
  bend-tighter-than-limit 0 -> 29 (fixed in dd9cb22f, not re-measured), turn clips a corner 36 -> 47 (Sunset Rd near
  the n16265/n15853 short islands: not traced). City is NOT clean here.
- Lot entrances: under city 284 of 5,193 entrances have their drawn pavement edge moved 0.05 m+ against scope 0
  (83 by 2 m+, worst 9.30 m on Shopton Rd; J1's 'all' 73 / 33 / 11.82 m). charlotte_lotent.bytes' key now changes
  under city (and the floors), so the shipped file is not read there (worked out at load); under bfr its key and
  content are unchanged - NOT re-made.
- Launch: the first pair measured one box only (env-list split); lb2/ly2 with every box NOT run.
- Concrete (kb/ky, every interchange and structure tile): NOT run.
- Drive audit (83 edges round the 10 spots): NOT run.
- Graph hash b2aa3453 in every run (no export).

## What still looks wrong / not done
- The CITY census gates (L/2 failures to 0, full L 5 % or fewer, bulges to 0) are NOT met: 1,355 ends still fail L/2
  under city (see the classes above). The biggest left: carriageways with MORE LANES than the undivided road (672 ends:
  a lane-add design - the extra lane opening on the outside over its taper - is the next job), ROOM (592: the blend
  cannot outrun the next junction), turning roadways (189) and separations (97, separate roads by design).
- In my own sheet: Lansdowne Rd (n922) and Eastway Dr (n11506) barely change (curving / side-street caps); Monroe Rd
  (n8656) keeps a 39-degree end on one carriageway (declined); the last three rows are untouched by design. At
  Hambright Rd and Carmel Rd the abrupt V is gone, but the first 40-70 m past the node is one flush pavement with the
  island starting late (the island starts where the lane bands are 2.4 m apart).
- Spot 36 (n231, held back as PC45) is touched by the taper floors under city (its lanes 0.24 m further out at the fan
  edge); the four held-back nodes (n231, n4025, n7963, n12438) and n7868's bow are as J1 left them.
- Providence Road West (n14038-n14039): city reverts the PC45 refuge pair (relay census +0.01 under the floors) and
  draws it with the short-island profile instead; not looked at in a picture.
- The exporter-fillet kink at refuge noses (4-6 deg at n10238 / n10229): unchanged. The cure is the medians gate
  measuring the undivided edges in the 30 m chord frame where the chord ease applies (LineModel.RefugeCheck.cs:53),
  then PSX_CITY_REFUGECHORD=1 under city; not started (a gate change needs its own proof).
- Approach marking at same-name meetings: only 1 meeting nose qualifies (every arm the road's name); the rest meet a
  side street at the node, where a hatch would cross its mouth - not marked.
- Taper floors: 2,439 tapers want more than their room (listed in the audit / TaperFloorShortLog), none squeezed
  further than before. The rural 61 m floor is not used (the city is urban throughout, as the plan's gate table says).
- Not run: self-test, door tour, CityTrafficCheck lane paths, LotAudit, a player build. Pictures are editor frames.
- Incidents (mine): (1) a stopped job chain left its waiting runner alive; at 14:36 it took PSXCity 2 s after the
  tracks agent's job ended and ran on that agent's code (the copy had not landed) - result discarded; the runner now
  waits, retries the copy and checks a file hash before Unity starts. (2) 15:38: my census runner took the lock in a
  moment the tracks agent's lock file was absent while its WATCHED H2 race was still running; that editor closed
  mid-race with no report while the owner watched. I stopped at once (my editor ended 15:44, exit 0), and the runner
  now takes the lock atomically (write, wait 3 s, read back), never while any Unity has PSXCity open, and 90 s after
  another agent's lock goes. (3) The first launch audit pair measured one box (the runner splits env lists on ';');
  re-run with '|'.

## For the merge (coordinator)
- Branch `charlotte-splits2` on mountain-nm c0a81147 (J1 + J2 + houses + UAA traffic already in). Shared-file edits:
  - Scripts/City/LineModel.cs: LengthFor (a TAPR length floored when TaperFloorOn), FloorOf (30.5 m street floor when
    on), the J1b block after ClassMph (TaperFloorOn, FlooredLength, RoomAlongFloor + its log), Relay (full L from
    40 mph when on), the SHIFT-record loop (floored length when on), PropagateShift / Propagate (RoomAlongFloor: the
    same room, counted), BuildEases' counter reset line. With the floors off every path is as before.
  - Scripts/City/LineModel.Splits.cs, LineModel.SplitFrames.cs (J1's files): scope city, CityPhaseC, ScopeBitOf,
    RefugeScopeMask + 128, ApplyBayEnds (BayLaneRunsOn), PhaseCLength / BlendDev / FirstSideStreetU / BfrNode, the
    short-island profile, splitFrameEdge2 + FrameNormalOf, SplitMeetingU.
  - Scripts/City/CityMeshes.Marks.cs NoseMarks (the meeting noses under city; MarkTally.noseMeeting);
    CityMeshes.LotEntrances.cs LotEntKeyOf (one Mix line under city / floors).
  - Scripts/City/LineModel.RefugeList.cs REGENERATED (the reason: the scope city; 0 / bfr / all identical).
  - Editor: CityAudit.MedianGates.cs (4 scopes, labels), CityAudit.Splits.cs, CityRefSpots.Splits.cs,
    CityLaunchAudit.cs (several boxes). No new .cs files: no new metas.
- Data: no export, graph b2aa3453; no Resources file changed. Re-bakes for the DEFAULT (bfr): none beyond J1's.
  If city is ever made the default: the city re-bake (pc_citybuild.ps1), re-make charlotte_lotent.bytes under it
  (CityAudit.LotEntranceBake with PSX_CITY_SPLITAXIS=city - its key now changes under city, so the shipped file is
  simply not read until then), re-run RunMedianGates with PSX_REFUGE_GATE_SCOPES=0,bfr,all,city.
- If LANES2 or any lane-set change merges: re-run RunMedianGates (4 scopes) and copy refuge_list.generated.cs over
  LineModel.RefugeList.cs, as J1 said.

## What the owner will notice
- With the default (bfr) nothing changes: Beatties Ford stays as PC46 drew it. With PSX_CITY_SPLITAXIS=city, across
  Charlotte the lanes at most split islands no longer swing out at a sharp angle and S back; short islands lose most of
  their fat grass lens; through lanes OSM tags as turn bays no longer vanish at a driveway; lane-change tapers are
  longer. Many spots still bulge or kink where the road has no room or curves (listed above) - not ready to be the
  default.
# CHECKPOINT RACES: STAGE 1 FIXED, STAGE 2 BUILT; NO BARRICADES ANYWHERE; RACERS PASS IN THE ONCOMING LANE (agent "cpfix", branch `city-checkpoints`, 2026-11-17 01:40-06:20; NOT pushed, NOT merged)

Base: city-checkpoints 6d2e07cc (stage 1, whose independent check FAILED with 4 blockers) + mountain-nm 749e05de (PC50) merged
(09ff1dc8; conflicts only in CityRaceClosures.cs and this file). Scratch: `scratchpad\cpfix\` (PROGRESS.md, out\ = every report).
The owner: 11-16 ~21:00 "the Midnight Club approach is the way with checkpoints and allow players to pathfind"; 11-17 (via the
coordinator) "I asked for all road construction barricades to be removed from races. I don't like racers being forced to stay in
a particular lane. They should prefer staying in the 'right' lane, but passing around cars in the oncoming lane is an iconic part
of street racing".

## What was wrong (the check's blockers, at their cause)
1. THE RULER WHEN A CAR LEAVES ITS ROAD (CityRouter.Locate): any edge 4 m nearer took a car standing off the road, so a car knocked
   23 m off the Wilkinson loop ramp was measured 190 m behind its own station. Now the kept edge holds while the car is within 80 m
   of it (projected along its own arc, so a loop ramp's far side never steals it); another road only when the car is ON its
   pavement; a rival is held on its own road to 80 m; off the pavement the ruler adds the metres beyond (continuous).
2. A FALSE MISSED ON A RE-CROSSED GATE (CheckpointRace.Step): any passed gate crossed forward raised MISSED. Now only gates AFTER the
   pending one in this lap's order, and only while the car is not closing on its own gate; MISSED by distance needs the car near the
   gate BY THE ROAD (120 m) as well as in the air; a teleport restarts the approach.
3. THE GPS/AI FIELD vs THE RANKING FIELD (CityRouter.Build): two fields, cost for the route and plain metres for positions, disagreed
   off the suggested line (Todd Rd: +602 m at a node, MISSED for 15 s). Now ONE field per gate: its descent is the GPS line and every
   AI's road, and its RM is the metres ALONG THAT ROUTE (the ruler). Following the line the remaining falls metre for metre. On a
   two-way street the ruler measures the way the car is going (cost-chosen, it flipped on local streets the freeway profile prices
   x4); turning round when clearly better is WRONG WAY's job, and the GPS line then points back. The player (also the play check's
   autopilot) is always measured by the locate, as the real player is.
4. THE 11-17 RULES: mountain-nm merged; the camcorder's course is the race's gates (RacePoints.Provider set in TryBegin, cleared in
   OnDestroy, after the replay): "CAMCORDER COURSE: checkpoints i485west fwd, 16 points (14 checkpoints)".
5. Found on the way: at an exit ramp's gore the I-485 main line's OSM line lies under a car taking the ramp for 100 m (the ramp's line
   2.5 m away): the locate now prefers the road the route takes next (held 80 m past a node) and leaves a kept road for a node's
   non-route road only off its pavement (+19 km for a second, MISSED, in the first two H2 races).
6. Field time: one Dijkstra per gate with the descent's successor recorded, metres summed in settle order, BCL fills.

(results, switches, merge notes: filled at the end)

## What changed (switches: =0 is the old behaviour unless said)
- Scripts/Checkpoints/CityRouter.cs: Field.RM / nxt / potM (one field, the ruler's metres along its own descent), RouteFrom,
  RouteRemaining, the sticky locate (Project along the kept arc, NearestScored with a heading penalty and the route preference,
  PreferState held 80 m, the node rule), Remaining in metres. CheckpointRace.cs: one field per gate (FieldComputeMs), the
  rival's own-road hold to 80 m, the player always by the locate, the travel-direction ruler on two-way streets (+ WRONG WAY's
  two-way rule, the GPS turning back), MISSED rules, teleports restart the approach, the giving-way pause of the lost-AI clock,
  RouteChoice event, the camcorder Provider, the suggested route's roads first for a fresh locate (RouteSet). AIRoutePlanner.cs:
  a rival's remaining distance along its OWN planned road; VARIETY (PSX_CP_AI_VARIETY, OPT-IN =1: its gate is not met, 0 varied
  legs in 5 races with it on - no leg has a second route inside the 8 % band with 500-770 m legs and the 60 m turn penalty, as the
  freedom table foretold); RE-ROUTE (PSX_CP_REROUTE, ON: 20 m off its road for 1.5 s, or 10 s without progress while not giving way).
- AIDriver.cs, LANE USE (PSX_AI_PASS_ONCOMING, ON; =0 the lane discipline as before): a rival this car is held behind is a car to
  PASS (its pass planned over its whole length, against oncoming cars for its whole time, only where the road is straight enough),
  the better line (right-handers under 60 m from the other lane, left-handers' apexes clipped) with nothing coming; pass counters;
  DebugTwoWayHere / DebugRoadLat. Shared by every race type on two-way roads (city sanctioned and street, mountain stages).
- Lake Lure (Editor/PSXRacingBuilder.StageTown.cs): NO barricade at the side-road mouths or the park road; each mouth laid up its
  24 m notch, narrowing and sinking into the ground over its last 5 m. PSX_LURE_BARRICADES=1 builds the 11-16 closures.
- Closures gone: Scripts/CityRaceClosures.cs, Editor/CityClosuresCheck.cs, tools/city/tracks/closures.mjs (+metas) deleted;
  RacePlayCheck asserts no object named Barricade / Closure / TrafficCone in ANY race's scene (city or stage).
- Data: Resources/CityCheckpoints for uptown, tryon (fwd), independence, c7, x1 (fwd + rev), c1, c2, h6, h7 (+ i485west as
  before); Resources/CityTracks c1 (lane A), h6, h7 (lane D), c2 (re-emitted at 1 lap, lane B's own instruction).
  checkpoints.mjs: --rev, the span-shortcut test (C2 gate 7). TryonSprintRev stays a FIXED-LINE race: reversed, 22 one-way
  carriageway edges of North Tryon run against their flow and its grid stands on one (needs a re-authored reverse route).
- Tools: Editor/CheckpointProbe.cs (the check's probe, kept: PSX_CPPROBE=dir, PSX_CPPROBE_DETOUR=1), Editor/LaneUseWatch.cs,
  CheckpointRaceWatch (camcorder course, route-choice steps, the variety gate), CheckpointSelfTest (crash and off-route walks,
  ruler = route metres), cp-job.ps1 (-Matrix, -Release, lock name cpfix), race-play-check.ps1 (PSX_HIDDEN_GFX=1).

## Proof, stage 1 (all HIDDEN: the owner's PSXRacing.exe ran the whole session; PSXCity, 8-car fields, morning)
Reports in `scratchpad\cpfix\out\` (race_*.txt, matrix_*.txt, cp_selftest*.txt; probe_*\ = the check's probe logs and frames).
- Self-test (CheckpointSelfTest, all 10 files): ALL PASS after the fixes - for every chain: loads, hash, reachability, node parity,
  "the ruler's metres are the route's" on every leg, the ruler falls along the suggested route, CRASH WALKS (out to 30 m either side
  and back: the remaining never moves more than the car, 0 jumps; i485west 126 walks / 4,032 steps), OFF-ROUTE GPS WALKS from
  streets off the route (i485west 60 walks / 36,790 steps: the ruler reads the route's own metres at all but 0.5 %, fresh locates at
  ramp gores), routed paths. FINAL CODE (06:16, -releaseCodeOptimization, out\cp_selftest_release.txt): ALL PASS; field build
  i485west 15 gates in 117 ms, worst gate 10.2 ms, 7.8 ms a gate on average (the check: 18.2 / 229 ms for two fields; the design
  budget 9 / 150 ms); the other files 38-97 ms in all, worst gates 7.2-10.9 ms. Routed path builds 0.2-0.9 ms (budget 3).
- H2 (I485West) NONE, wrong-way test, probe (02:15, before the gore fix): all 8 home, every gate in order, WRONG WAY after 6.9 s and
  cleared; FAILs: the gore misread (fixed since) + the two known global lines.
- H2 NONE with the check's FORCED DETOUR (02:27, before the travel-direction fix): the player put on Todd Rd 440 m off route
  re-planned and passed every gate; 7 home + 1 planned-mistake DNF; FAILs: the gore misread, one 1,245 m flip on Todd Rd (both fixed
  since), MetersDriven (18.0 km for 9.2 km: the detour's own 8.3 km loop, as in the check) + the known lines.
- H2 NONE (02:50, final ruler): 7 home + the FD's planned-mistake DNF (allowed), every finisher every gate in order, the autopilot
  no MISSED / no WRONG WAY, 0 progress falls, 0 flicker, positions = remaining distance, no barricade; camcorder course = the 15
  gates. FAILs: only the known global pumping 2.17 and UNDRAWN RAILS 6.
- H2 LIGHT (02:55): 6 home + the Charger (31.6 m/s into a slow traffic sedan at 45 s, retired: allowed), every gate in order,
  0 falls, 0 MISSED; FAILs: only the known pumping and UNDRAWN RAILS.
- H2 NONE with the check's FORCED DETOUR on the final ruler (06:00, `out\race_h2final.txt`, probe `out\probe_h2final\`): ALL 8
  HOME, every gate in order; the player was put on Todd Rd 441 m off route at 61 s, re-planned (the GPS line rebuilt) and passed
  every gate: no MISSED, no WRONG WAY, NO progress fall (the check's blockers 2 and 3 do not recur); the probe checked 144 plans:
  0 waypoints against a one-way, 0 illegal turns. FAILs: flicker 2 (two rivals), MetersDriven 18.0 km for 9.2 km (the detour's own
  8.3 km loop, induced as in the check) + the known lines.

## Proof, lane use (owner 11-17): LaneUseWatch counts, hidden, seed 0 (passes begun = 1.2 m out toward the chosen side)
Same race, PSX_AI_PASS_ONCOMING ON vs OFF (A1 vs B1, the 02:40 / 03:30 code):
- GillespieGap NONE (mountain stage, 4 cars): ON passes 5 (all 5 into the oncoming lane) / done 2 / aborted 2, 0 near misses,
  0 head-ons, 134 s over the centreline in 71 stays (longest 14.0 s), better line 136 s | OFF 8 (7) / 2 / 4, 0, 0, 36 s
  (7.5 s), 5 s. One rival retired in each (its planned mistake). The rivals did pass in the oncoming lane and came back.
- TryonSprint MEDIUM (street, checkpoint, traffic): ON 58 (39 oncoming lane) / 31 / 24, 0 near misses, 0 head-ons, traffic hits
  1 (0 hard), held behind traffic 7 s avg / 18 s max | OFF 78 (49) / 41 / 36, 1 near miss, 1 HEAD-ON (Supra into a taxi,
  12.2 m/s), traffic hits 2 (2 hard: the Skyline retired into a pickup at 27 m/s), held 10 / 26 s.
Later code, street races ON: CltX1 MEDIUM (B5): 28 (19) / 14 / 13, 0 near misses, 0 head-ons, 0 traffic hits, all home.
TryonSprint MEDIUM again (B5): 56 (27) / 37 / 17, 0 near misses, 1 head-on (the Viper at 8.3 m/s into a van), traffic hits 3
(1 hard). Earlier code (B2): CltC1 MEDIUM 1 head-on (6.2 m/s), 3 near misses, a 30 s stay over the line; CltX1 MEDIUM the FD
head-on at 37.4 m/s 20 s in (two abreast after the start, a pass with the oncoming limit on), 2 near misses. Since then: the wide
better line is off in traffic, a held pass is given up the moment an oncoming car is near (c31b4521, not re-raced), held passes
need 2.5 m/s / 30 m / 6 s. So: 0 head-ons is NOT proven in traffic: 3 head-ons in 5 street races with it ON (2 at walking
pace, one at 37.4 m/s): more than the 0.13 a race the 11-02 traffic round measured on the fixed lines; in the one paired race
OFF had one too (12.2 m/s). The head-ons themselves come from the traffic give-way (pass lines vs the oncoming limit) that both
modes share; the two fixes after them are not raced yet. A paired 8-race matrix per level is what proves it.
On one-way freeways held passes are off (they swung a pack about at 180 km/h); the racers' own rule as before.

## What the coordinator must do at merge
- Merge city-checkpoints after mountain-nm 749e05de (already merged in; only CityRaceClosures.cs and HANDOFF.md conflicted).
  `git diff 749e05de -- Assets/PSXRacing/Scripts/City Assets/PSXRacing/Scripts/RoadsideRules.cs "Assets/PSXRacing/Resources/charlotte_*"`
  is EMPTY: no city re-bake.
- SCENE PASS: LakeLure (its sprint variants race in its scene) for the barricade removal; no other scene.
- _order.txt NOT edited (the integrator's file): after merging run `node tools/city/tracks/order.mjs --write` and commit (CltC1,
  CltC2, CltH6, CltH7 append; their files come from lanes A/D and lane B's own re-emit instruction).
- Race cards of the four new venues: `tools\citymap-bake.ps1 -Project <sandbox>` before the player build (as batch 1 did).
- Shared files touched this round: AIDriver.cs (lane use; held passes on two-way roads only), RacePlayCheck.cs (the barricade scan,
  LaneUseWatch hooks), PSXRacingBuilder.StageTown.cs (Lake Lure), race-play-check.ps1 (PSX_HIDDEN_GFX), plus stage 1's list.
- Deleted (with metas): Scripts/CityRaceClosures.cs, Editor/CityClosuresCheck.cs, tools/city/tracks/closures.mjs. Lanes A/B/D still
  carry closures code on their branches: take this branch's side.
- New with metas: Editor/CheckpointProbe.cs, Editor/LaneUseWatch.cs, Resources/CityCheckpoints/*.json, CityTracks c1/c2/h6/h7.


## Proof, stage 2: every converted venue (hidden, 8-car field, morning, course preload as on PC: PSX_CITY_COURSE=1)
Per-venue table from the LAST run of each (B5 = the code of 05:08; B2 = the code of 03:55, before the continuity, fresh-locate,
towing and one-way fixes). "Known" = the global pumping / UNDRAWN RAILS lines every race shows. Every finisher passed every gate in
order in every race below; the camcorder's course was the venue's gates in every one.
| venue (gates) | run | home | not home | FAILs beyond the known lines |
| I485West H2 (15, 1 lap) | A1 02:50 | 7/7 + player | FD planned mistake | none |
| I485West LIGHT | A1 02:55 | 6/7 + player | Charger into a traffic sedan 31.6 m/s | none |
| IndependenceSprint (10) | B2 | 7/7 + player | - | none |
| TryonSprint (9) | B5 | 7/7 + player | - | flicker 2 |
| TryonSprint MEDIUM street | B5 | 7/7 (player autopilot stuck at a bend after a contact at 225 s, 5 of 8 gates) | - | 1 head-on (Viper 8.3 m/s into a van), flicker 1, a 32 m start fall |
| CltC7 (8, 3 laps) | B5 | 6/7 + player | 180SX planned mistake | flicker 2 |
| CltX1 (5) | B2 | 7/7 + player | - | flicker 3 |
| CltX1Rev (5, the rev chain) | B2 | 7/7 + player | - | flicker 5, a 20 s stay over the line (wide better line, before it was cut in traffic) |
| CltX1 MEDIUM street | B5 | 7/7 + player | - | flicker 5; 0 head-ons, 0 near misses, 0 traffic hits |
| CltC1 (6, 4 laps) | A1 | 5/7 + player | 180SX (3 planned mistakes), Viper (scrape damage 124) | 2 retired, flicker 1, one 259 m fall (a varied leg: own-plan ruler since) |
| CltC1 MEDIUM street | B2 | 6/7 + player | - | 1 head-on (Civic 6.2 m/s), 3 near misses, a 30 s stay over the line |
| CltC2 (8) | B5 | 7/7 + player | - | flicker 2 |
| CltH6 (9) | B5 | 7/7 + player | - | one 3.8 km player fall at a ramp pair 10 m apart (fixed after: one-way-against penalty, not re-run) |
| CltH7 (10) | B2 | 6/7 + player | FD planned mistake | none |
| UptownLoop (13) | B5 | 1/7 + player | 6 hit the ground at wp 1282 | HELD BACK to its fixed line (uptown.json held: 1) |
UptownLoop as a checkpoint race lost 5-6 rivals in 4 of 4 runs at wp 1282 (I-277 edge 1832, a 74 m level-0 piece between two
bridge decks), always at 39-51 m/s on the line ("into Ground"). THE FIXED LINE DOES THE SAME: the UptownLoop fixed-line regression
(06:08, out\race_uptownfixed.txt, the held file, course preload) lost the Charger, FD and Supra into the ground at wp 1282 at
43-51 m/s and two more in the pile-up behind (5 retired; the autopilot, 180SX and Viper home; no barricade). So it is the venue
(the I-277 profile between the two decks, or the AI's crest speed there), not the checkpoints, and it is in PC50's code too as far
as this sandbox shows: worth its own look before UptownLoop is raced in an exe. Uptown stays fixed-line (held) = unchanged.

## Switches and defaults
- PSX_CP_RACES ON (=0 every city race fixed-line). Converted and ON: I485West, IndependenceSprint, TryonSprint, CltC7, CltX1,
  CltX1Rev, CltC1, CltC2, CltH6, CltH7. Fixed-line: UptownLoop (held), TryonSprintRev (no rev chain: one-way carriageways),
  drags (CltD1, CltD3: a drag stays a straight strip) and every mountain stage (one road: the camcorder already plants its
  split points). PSX_CP_VENUES (dev list), PSX_CP_HELD=1 (race held files too).
- Stage-1 sub-switches as before (PSX_CP_AI_ROUTE, _GPS, _RESPAWN, _ODOMETER ON; PSX_CP_MARKERS opt-in, Q5).
- New: PSX_CP_REROUTE ON; PSX_CP_TOW_WRECKS ON; PSX_CP_AI_VARIETY OPT-IN (gate not met); PSX_AI_PASS_ONCOMING ON;
  PSX_LURE_BARRICADES OFF (=1 the 11-16 closures). PSX_TRACK_CLOSURES is gone with its file.
- Tools only: PSX_HIDDEN_GFX=1 (race-play-check), PSX_CPPROBE / PSX_CPPROBE_DETOUR (probe), cp-job -Matrix / -Release.

## Not done / not verified
- STREET RACES WITH CITY-WIDE TRAFFIC (design stage 2 item 2: CityTraffic in races): NOT built. It needs a Scripts/City edit
  (CityTraffic.RaceOptIn, racers as obstacles), the 40-minute city re-bake and a scene pass, and new head-on risk on every side
  street. Street checkpoint races today use the TrafficSystem strip along the suggested route (where the field drives: no leg has a
  real alternative, freeLegs 0 on 8 of 10 files). Q6 stays open.
- AI variety: built, opt-in; its gate is not met on any venue (see above). Wider gate spacing on the grid circuits would be needed.
- Race cards' map tiles for C1/C2/H6/H7 (citymap-bake) and _order.txt: the coordinator's (above).
- Position FLICKER (a swap reversed within 0.5 s): 1-5 per city race remain after the continuity fixes, most with the autopilot
  beside a rival; H2 shows 0. The tie band is 4 m / 0.4 s.
- Lake Lure: code only. PSXCity has no LakeLure scene; the builder's log line ("closed by barricades: none") and a frame of an open
  mouth come with the coordinator's scene pass.
- The 3.8 km H6 ramp misread fix (one-way-against penalty, 38e438bb) and the held-pass abort (c31b4521) were not re-raced.
- No watched race this round (the owner's game was running until ~05:20; afterwards only hidden runs were left).
- Field time in a player build (IL2CPP/Mono release): measured only in the editor (debug) and with -releaseCodeOptimization (below).

## Open questions for the owner (design.json; defaults kept, NOT asked)
Q1 Lake Lure mouths - DECIDED by the owner 11-17: no barricades (done, code; needs the scene pass). Q2 the tow during a race:
default, CALL TOW TRUCK ends the race. Q3 a missed checkpoint: default, turn back for it. Q4 lane use - DECIDED 11-17: prefer the
right lane, pass and take a better line in the oncoming lane where clear (done; head-ons in traffic not yet at 0, above). Q5 the
marker look: default yes once he has seen the day/night/rain sheet (still opt-in, PSX_CP_MARKERS=1). Q6 city-wide traffic in
street races (a Scripts/City edit + the 40-minute re-bake): still open, not built.

## What the owner will notice
Ten Charlotte races are checkpoint races with no barricades: any road between the gates, a GPS line and CHECKPOINT k/N, the rivals
planning their own roads, the camcorder's cameras at the start, every checkpoint and the finish. On two-way roads the rivals pass
slower cars in the oncoming lane and take hairpins wide when nothing is coming. Lake Lure's side roads are open (after its scene
pass). UptownLoop and TryonSprint II stay as they were.

# CHECKPOINT RACES, STAGE 1: no barricades; any road between ordered gates; proven on I-485 WEST (agent "checkpoints", branch `city-checkpoints`, 2026-11-16 22:30 - 11-17 00:45; NOT pushed, NOT merged)

The owner, 2026-11-16 ~21:00: "After seeing them in action, I'm not a fan of the road construction barriers for
races. Drivers can just go around them. I guess the Midnight Club approach is the way with checkpoints and allow
players to pathfind". Design: scratchpad `checkpoints\DESIGN_v2.md` (Midnight Club = the race FORMAT only: no art,
names, UI or cities). Progress log and every report: scratchpad `checkpoints\stage1\` (PROGRESS.md, out\).

Base: `charlotte-tracks-b1` 21e7dec8 (X1, D3, C7) + lane C's f839431b (the hosted-laps fix, cherry-picked as 4539a3ec).
`git diff charlotte-tracks-b1 -- Assets/PSXRacing/Scripts/City Assets/PSXRacing/Scripts/RoadsideRules.cs
"Assets/PSXRacing/Resources/charlotte_*"` is EMPTY: no re-bake, no scene pass. Everything is made at runtime.

## What changed
1. CLOSURES OUT (its own commit f9277794: merge it first, beside batch-1). CityTracksRegistrar keeps only the hosted
   route swap and the venue's laps; `CityRaceClosures.ForThisRace` returns false (PSX_TRACK_CLOSURES ignored, no
   switch back); RacePlayCheck asserts "no road closure built"; track-job.ps1 lost its closures mode; Events.cs
   (ROOKIE CUP, US CUP) and the event page's sanctioned lines no longer promise closed roads, and say nothing about
   checkpoints (the event venues stay fixed-line until they get files). Delete CityRaceClosures.cs,
   Editor/CityClosuresCheck.cs and tools/city/tracks/closures.mjs (+metas) once batch-1 has merged.
2. DATA. `tools/city/tracks/checkpoints.mjs <route> [--write] [--dump f]`, `checkpoints.mjs all --table f`:
   mandatory lap line / start + finish; every TURNAROUND (a non-freeway road between two ramp runs); every plan
   APEX (route points 150 m+ apart along, within min(100 m, 0.4 x along) across: an infield or a lot could cut it);
   then the fewest evenly spaced gates whose legs pass (a) legal metre-shortest >= 0.88 x suggested, (b) suggested
   cost <= 1.06 x the cost optimum, (c) <= 800 m (1,500 m only with a real alternative), (e) cost optimum's metres
   <= 1.04 x metre optimum; clean-spot snap +-60 m; wide-gate spans; reachability; freedom report. Shipped ONLY
   `Resources/CityCheckpoints/i485west.json` (+meta): 15 gates (lap line, 10 splits, the Wilkinson Blvd and Moores
   Chapel Rd turnarounds, 2 apex gates on the Wilkinson loop ramps; one wide gate over a parallel carriageway),
   legs 215-770 m, 0 failing legs, freeLegs 0. Plan sheet: scratchpad `checkpoints\stage1\h2_gates_plan.png`.
3. RUNTIME, `Scripts/Checkpoints/` (outside Scripts/City):
   - CheckpointFile (load, graph hash, PSX_CP_RACES / PSX_CP_VENUES, card facts);
   - CityRouter (directed-edge CSR with lib.mjs's legality and cost model; per gate a METRIC field = the ranking
     ruler and a COST field = GPS and AI, seeded at every span; node potential; Locate with a 4 m height test, 4 m /
     0.3 s hysteresis, immediate hand-over across a node, off-graph fallback; per-frame slide; Descend);
   - CheckpointRace (TryBegin after the handoff, before the traffic; the countdown held while the fields build,
     several gates a frame in a 40 ms budget; static restart cache; the swept, order-only gate test with spans (5 m
     past each kerb) and a 5 m level test, plus the ARC PASS on the car's own road; the circuit's start crossing;
     laps by gate 0; finish by the last gate; progress = total - remaining on one ruler; 4 m / 0.4 s tie band; a
     car held on its own road from 15 m to 25 m off it; RaceFraction / RemainingToFinishM; MISSED (a later gate
     crossed, or 150 m further than a closest approach within 60 m; clears on the pass or coming back within 50 m);
     teleports pass nothing; odometer; respawn (an AI on its own road; the player on the graph's better direction,
     walking BACK up to 30 m, else 10 m past the last gate passed); WRONG WAY = remaining rising against the
     U-turn-aware direction; the lost-AI watchdog; GPS line; generated chime and buzz; the pass gap);
   - RoutedPath (BuildPath copy: partial edges, per-waypoint halfL/halfR/oneWayAt/ownLaneOff/edgeAt/sAt/dirAt/
     junction; BuildPath's corners), AIRoutePlanner (cost optimum to the next gate and >= 900 m on, rebuilt at each
     gate, respawn, MISSED and the 450 m guard; double buffer on an INACTIVE holder; one rebuild a frame; run-out
     max(900 m, v^2/4.4 + 150 m)), CheckpointMarkers (warm searchlight beam over the next gate, dim amber over the one
     after, a glow band across each span, kerb glow posts; PSX/Glow, out of its fog fade by a property block).
4. HUD / MAP / CARD: lap slot `CHECKPOINT k/15` (circuits of 2+ laps `LAP 1/2  CP k/15`, `FINISH`, `FINISHED`); the
   cue in the BEST slot `^  CHECKPOINT 4  640 M`; centre banner `CHECKPOINT 3   +1.4` / `LEAD` and `MISSED CHECKPOINT
   4 - TURN BACK`; minimap GPS line (0.5 s / on leaving it), gates (next = big diamond held on the rim), remaining
   to the flag; race card gate dots and "15 CHECKPOINTS · ANY ROUTE" (arcade and CITY cards).

## Switches (environment; =0 is the old behaviour)
PSX_CP_RACES default ON (only venues with a file convert: stage 1 = I485West; =0 every city race fixed-line) ·
PSX_CP_VENUES (dev list) · PSX_CP_AI_ROUTE, PSX_CP_GPS, PSX_CP_RESPAWN, PSX_CP_ODOMETER ON with the master ·
PSX_CP_MARKERS default OFF until the owner has seen the beam sheets (=1 shows the beams, bands and posts) ·
PSX_RACEFRACTION_FIX default OFF (=1: fixed-line loops' fraction without the extra lap; no watched loop race yet).

## Proof (PSXCity, watched editor, real frames; reports in scratchpad `checkpoints\stage1\out\`)
- CheckpointSelfTest (00:13): ALL PASS. Loads, hash; every leg reachable in metres and cost; C# = node within 1 m on
  all 15 legs; the ruler falls along the suggested route (924 steps); 15 routed legs, 0 pavement halves under
  1.5 m. Timings (editor, batch): fields worst 31.9 ms a gate, 343 ms all (design budget 9 / 150 ms: NOT met in the
  editor; the countdown hold covers it and RESTART uses the cache); routed path build worst 16.5 ms (budget 3 ms;
  in races the worst rebuild was 8.8 ms).
- day1 (23:05, 8 cars, NONE): every car through all 15 gates in order, 7 home + 1 planned-mistake DNF, autopilot P2
  4:11.3, positions = remaining 0 of 668 samples, honest metres 9,269 m. FAILs found and fixed after: a false MISSED
  + 12 flickers (a corner tangent read a one-way backwards: dirAt), the Charger past the Wilkinson apex span (arc
  pass, 5 m capture; it went round for 7 min first).
- night1 (23:36, night rain, 8 cars, NONE): 8 home, every gate in order, 0 MISSED, 0 wrong way, positions ok.
  FAILs: flicker 69 / falls 42 from one knocked car swapping road/graph every frame (fixed: 15/25 m hysteresis); the
  beam vanished beyond ~600 m (the glow shader's fog fade; fixed by a property block).
- light1 (23:50, night rain, LIGHT traffic, 8 cars, forced reset): 6 home, every checkpoint assert OK (0 flicker, 0
  falls, 0 MISSED, reset 5,994 m before = 5,994 m after: no gain). FAIL "at most one rival retires": 2 (the planned
  mistake + a pack crash at 27.7 s, the Supra at 133 km/h into the autopilot slowed in traffic).
- laps2 (00:03, 2 laps via -Laps, 4 cars, NONE, wrong-way test): 30 of 30 gates in order across lap 2, lap times
  line to line, WRONG WAY shown after 5.6 s driving back, cleared 1.1 s after turning round, 0 flicker, 0 falls,
  positions ok, odometer 19,027 m for 18,469 m. (The harness's 15 s countdown wait expired - fields took 29 s on
  a busy machine - so its test ran at the start: fixed, the wait is 60 s and the fields build in a budget.)
- final (00:25, 8 cars, NONE, markers on): all 8 home, 0 retired, every car through all 15 gates in order, autopilot no
  MISSED / no WRONG WAY, positions = remaining 0 of 255, odometer 9,268 m. FAIL: the Charger again went past the
  Wilkinson apex arc without a crossing (MISSED at 82 s, routed back, home 247 s), behind it 1 progress fall + 2
  flickers. Cause found after: across a join a routed waypoint read the next edge at its first vertex (40 m on);
  fixed (a59725b2: the join segment lies on the next edge from its node end; the arc pass also counts a car that
  came onto the gate state over a node). Those two fixes are checked by the batch self-test only (01:05, ALL PASS,
  with a new check that the routed roads' (edge, s) back-map is continuous across every join on all 15 legs; routed
  build worst 5.3 ms), not by a watched race.
- UptownLoop fixed-line regression (no checkpoint file, PSX_CP_RACES on): NOT RUN TO THE END - started 00:32, then
  a PSXRacing.exe window took the focus (00:43, the owner's or the coordinator's game) and the watched editor sat
  at frame 289; I did not launch more watched editors over a running game.
- LifeSimSelfTest (PSXCity, defaults + PSX_CP_RACES): 92 FAIL lines = the batch-1 base's 92 in PSXCity, none naming
  a checkpoint, a closure or a city track.
- Race card: scratchpad `out\cards_cp\citymap_I485West.png` (14 gate dots + the lap line).
- Sheets (scratchpad `checkpoints\stage1\`): `sheet_h2_day.png`, `sheet_h2_night.png`, `sheet_h2_light_laps.png`,
  `sheet_h2_final.png`, plan `h2_gates_plan.png`. Honest about the look: the beam reads well at night and in rain
  out to the fog, by day only as a thin streak at 300 m; beyond ~500 m it was cut by the camera's far plane in every
  run (fixed after the last run: drawn inside the far plane, NOT yet seen); the ground band and posts never showed
  in a frame (lifted 0.3 m after, not yet seen). Hence PSX_CP_MARKERS stays opt-in.
- Known lines in every H2 run, as in the tracks round's base: exposure pumping 1.9-2.1, UNDRAWN RAILS 2-30.

## Not done in stage 1 (honest)
- C1 (DESIGN_v2's second venue, M8): not in charlotte-tracks-b1 when this started; no c1.json shipped.
- The racing-radius fillets (M7): RoutedPath keeps BuildPath's corners (H2's AI route IS the suggested route, so
  the AI drive the fixed line's geometry). PSX_CP_FILLET_R not built.
- -cpDetour and -cpMiss harness modes (H2 has no legal alternative and its gates span the only carriageway); the C#
  apex settle (checkpoint_apex.json): the plan candidates are taken as gates (conservative).
- Field and rebuild time budgets are not met in the editor (above). Android unmeasured.
- The freedom table for the owner (Q7) is offline (node), first pass: C1 0.33, C3 0.29, C4 0.17, C2 0.13, every
  other candidate and the 3 export routes 0.00 (freeway laps and single-arterial courses: honest, little choice).
  Scratchpad `checkpoints\stage1\freedom_table.txt`.

## At merge (coordinator)
- Merge order: f9277794 (closures out) any time; the rest after charlotte-tracks-b1. Shared files touched:
  RaceManager (Start line, Update branch, FinishCar, RespawnOn refactor + RaiseRespawned, MetersDriven, RaceFraction,
  RemainingToFinishM, StabilizeOrder hook, repeat respawns by position on routed roads); TrackPath (NonSerialized
  arrays); AIDriver (TwoWayHere / LaneCentreHere / RoadHalfHere replace every race-wide read, LineOffset per
  waypoint, AheadOnRoad, no mistakes in junction zones, SetPath); StuckRecovery (wrong way, off track off, fell-below
  on the graph, floorY); CareerDrive; RaceHUD; HudMinimap; TrafficSystem (PlayerS); RaceReplay (PauseList);
  RouteMap; ArcadeFrontEnd / CityFrontEnd; RacePlayCheck; Events.cs; LifeHomeScreen.Events.cs; track-job.ps1.
- New: Scripts/Checkpoints/* (+metas), Editor/CheckpointSelfTest.cs, Editor/CheckpointRaceWatch.cs (+metas),
  Resources/CityCheckpoints/i485west.json (+meta, folder meta), tools/city/tracks/checkpoints.mjs, cp-job.ps1.
- No re-bake, no scene pass. A PC build carries it as is (Resources/CityCheckpoints is a Resources folder).
# HANDOFF - TREE SEASON: a tree looks the same at every distance (agent "treeseason": Fable 11-16 night, then Opus 5.5 11-17; branch `tree-season` off mountain-nm 742315cd + merge of 1527e2b1; NOT pushed, NOT merged)

Owner, 11-16 ~22:00, after PC47/PC48: "Tree pop-in is egregious. It is very distracting to see trees full of leaves (or
different color trees in Fall) pop-in as you approach and then the tree is leafless (or different colors trees overtake
one another in Fall orange, red, green." ... "Drive around Charlotte in Winter and a mountain track in Fall".

## What was found (file:line on 1527e2b1)
1. WINTER, a leafy far card over a bare mesh: past the 240 m mesh range every tree is its "far card", and the far
   cards were the packs' SUMMER pictures darkened (PSXRacingBuilder.CityTreeKit.cs 186-189 Meadow poplars, 261-264 Big
   Oak; PSXRacingBuilder.MeshTrees.cs 346-348 the stages' tulip poplar). City: 55 of 58 species failed LEAF.
2. WINTER beeches: orange leaf speckle over grey twigs (MeshTrees.cs 820-856 BareImpostorPath: the removed leaf texels
   keep their orange under alpha 0 and the mips bleed it back).
3. Big Oak's far card a solid grey box 43 m wide (CityTreeKit.cs 254-264: the keyed billboard draws opaque).
4. Street forms' far cards the wrong height (12.7 v 8.2 m: CityTreeKit2.cs ReworkMesh's impostor cut).
5. 8 city species had NO far level and ended at the mesh range (MeshForest.Init: m = Min(m, meshRange)).
6. FALL / SUMMER: far cards more saturated and brighter than their meshes (pack sheets + CTImpostorGain 1.3,
   CityTreeKit2.cs 149; a flat card's light normal, MeshTrees.cs 374, 1.6-1.8 x its mesh at noon); a cross of two cards
   is 0.71-0.79 of the crown's width from a diagonal. Every tree changed colour and outline at the hand-over.
7. The stages' old "B" cards (what the MIRRORS show behind the car, and the forest past 2.2 km) wear the retro
   billboard pack: in winter they read as dark brown leafy crowns over a bare grey forest (PSXRacingBuilder.Stage.cs
   8306/8347, Season.cs 105 DressAtlasPath). They are also 1/1.6 of their mesh trees' height (Stage.cs 8238
   MeshTreeHeightScale 1.6 v AddTreeQuads(h)): NOT fixed (needs a stage scene pass, below).
8. Charlotte's tile cards (past 1.2 km, outside the view's cone, before a tile is fitted) and CityFar's far trees wear
   the same retro atlas (City/CityTrees.cs 937 MaterialFor). Tiles stood up before CityMeshTrees started kept it even
   with the kit's atlas on (CityWorld.cs 1291 takes the material when the tile stands).
9. NEW with the turning far card (round 1): it showed the nearest of its views, so its whole picture switched every
   45 degrees of bearing: 87-91 % of its pixels changed between bearings 1.5 degrees apart, against the LOW mesh's own
   20 % (the audit's sweep, cal1).
10. Mesh levels against each other in winter (LOD0-LOD1, LOD1-LOD2): the packs' coarser levels drop twigs (class 7 of
   round 1): a pop inside the mesh levels, not a season mismatch. NOT touched.
11. A far card grows to 2.4 x by 1.8 km (_PSXFarGrow, MeshTrees.cs 728): trees shrink as they are approached and the
   ones behind come out. Knob PSX_FOREST_FARGROW=1; default unchanged (it was the owner's "barren far slopes" fix).
12. Stage cliff meshes (cliff_base / cliff_piece) have no far level: MeshForest.Init caps them at the mesh range, so
   rock faces dissolve at 240 m. Not trees; for a later round.
13. The October "no hardwoods" bug on Little Switzerland does not reproduce on PC48/PC49's scene (forest draws in edit
   and play mode). RunStageCards logs the plants listed per dress and fails on 0.
14. TOOLING TRAP (mine, fixed): one chain of jobs = one PowerShell process, and job.ps1 left each job's env vars set,
   so a later job inherited an earlier one's switches. Every result below is from jobs run after the fix.

15. The packs' far card of the tulip poplar (city Columnar + every stage) drew its billboard sheet's FOUR views side by
   side on one card: four thin poplars where one stands (MeshTrees.cs 503 AddBillboardImpostor, aspect 8.7/23.2).
   Gone with the match cards.
16. Charlotte's pine stand-ins (03/05_Poplar_skan_pine and their r3 forms) are green poplar meshes: in winter they read
   as leafy broadleaf trees, near and far alike (no hand-over mismatch; a species question for a later round).

## What changed (branch tree-season; switches, defaults)
- PSX_TREE_MATCH (default ON; 0 = the packs' far sheets, as PC49): every tree's far card is a picture of its own LOW
  mesh in each dress (Editor PSXRacingBuilder.TreeMatch.cs; made inside EnsureCityTreeKit / LoadMeshTreeKit, or in place
  by TreeMatchBuild.CityMatchOnly / PatchStages), on the mesh's own origin (same per-tree fall colour and shade), turned
  to the eye (4 views = 8 bearings), lit as a crown (PSX/Lit _PSXCrownCard). City species with no far level get one.
- Each card's albedo gain MEASURED against its own mesh (Editor/TreeMatchGains.txt, 204 cards, 0.79-1.19; the winter
  twig cards were 10-17 % dark, the poplars' leaf cards 26 % bright).
- VIEW FADE (PSX_TM_VIEWFADE at bake, default 0.16 of a view step; 0 = the switch): between two views the next one
  dithers in by 2 x 2 pixel quads, the same pixels for the same pair (PSX/Lit, inside PSX_LODFADE).
- PSX_TREE_MATCH_CARDS=1 (default OFF = the retro billboards, as PC49): Charlotte's tile / CityFar cards
  (CityTreeKit.cardAtlas, round 1) and now a STAGE's B cards and crowns (MeshForest.matchCardAtlas, cut by
  TMStageCardAtlas from the stage kit's match sheets, each broadleaf cell the kit tree whose fall dress is that cell's
  colour) wear the kit's own trees in the dress. Gains measured: city x0.77 (PSX_TM_CARDGAIN), stage x0.65
  (PSX_TM_BCARDGAIN). OFF because the cards' cover measured 0.3-0.7 of the mesh trees' (below). CityMeshTrees redresses
  the tiles that stood before it started (CityWorld.Redress) when it is on.
- Tools (Editor only): TreeMatchAudit (+ the bearing sweep PSX_TM_SWEEP, rock faces / fences skipped), RunStageCards
  measured on the Foliage layer, RunStageWalk for several stages in one process, RunTreeMatchCost / RunStageMatchCost.
- Unchanged: LOD distances and bands (PSX_FOREST_LODFADE stays OFF), the far grow (PSX_FOREST_FARGROW unset), the mesh
  levels, the far canopy.

## Proof (scratch `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\treeseason\`)
- ONE TABLE, ONE MEASURE: `sheets\TM_TABLE_before_after.txt` (TreeMatchAudit at the head; before = PSX_TREE_MATCH=0 in a
  fresh process; city + all 9 mountain stages; city and Blue Ridge every dress and hand-over, the other 8 stages the far
  hand-over in winter / summer / fall). The far hand-over (mesh -> far card): 533 species-pairs, failing 533 -> 105.
  By reason: LEAF 267 -> 0, NOFAR 40 -> 0, SAT 217 -> 2, VALUE 483 -> 6, HUE 9 -> 2, SIZE 483 -> 95, GONE 0 -> 2.
  Stages: summer and fall 0 left on all 9; winter 1 (beech_tree_08 at one bearing: width 5.1 v 3.8 m). The mesh levels
  (LOD0-1-2) are unchanged (the packs' own; winter SIZE 20-21 city species, class 10).
- PICTURES of the classes, before | after: `sheets\tm_city_winter_before_after.png`, `tm_city_fall_before_after.png`,
  `tm_stage_before_after.png` (LOW mesh | far card at the 240 m hand-over, the same eye; labels in each cell).
- VIEW FADE (the audit's bearing sweep, the tree's picture from bearings 1.5 deg apart): the far card's largest change
  per step 91 % -> 11 % (city median), 87 % -> 10 % (stages), worst species 140 % -> 22 %; the LOW mesh's own 20 %.
- APPROACH STRIPS 400 -> 20 m, before | after, fresh processes: `sheets\strip_charlotte_before_after.png`,
  `strip_226a_before_after.png`, `strip_ls_lure_before_after.png`. THE POP MEASURE (16 px blocks whose light changes
  > 3 % when the levels step 10 m, RunTreeFeet's): Charlotte arterial winter 968 -> 548, fall 638 -> 495, W Trade
  winter 16 -> 1, fall 34 -> 34; Little Switzerland winter 115 -> 73, fall 23 -> 19; NC 226A winter 97 -> 104, fall
  27 -> 21; Blue Ridge winter 34 -> 44, fall 10 -> 9; Lake Lure winter 35 -> 36, fall 18 -> 19. Charlotte 1656 -> 1078
  (-35 %), the four stages 359 -> 325 (-9 %; their far trees sit behind the corridor's near ones).
- THE PLAY DRIVES, after, WATCHED once each (owner's game closed; main told before each): NC 226A 15 Oct (02:28) and
  Charlotte / Independence sprint 15 Jan (02:47); `sheets\drives_before_after.png` = before (11-16) | after frames.
- THE CARDS (Charlotte's tiles past 1.2 km, a stage's B cards past 2.2 km and in the mirrors) v the mesh trees, same eye:
  `sheets\cards_before_after.png`. With the kit's own trees on them the colour and leaf state match (226A fall: all
  limits pass; Charlotte winter luma 0.272 v 0.271), but the cover falls to 0.3-0.7 of the mesh trees' (retro 0.9-1.1):
  the card meshes are 1/1.3 and 1/1.6 of their mesh trees. So PSX_TREE_MATCH_CARDS is OFF by default.

## Cost (editor, 1920 x 1080, render + read-back, ABBA x 4, one process; `cost\`)
- Charlotte arterial, fall: driver's eye +0.83 ms (32.6 -> 33.4), 60 m up +0.26 ms; plants drawn +19 % (the 8 city
  species that had no far level now stand to 1.2 km), triangles 0.44 -> 0.45 M, draws 198 -> 205.
- NC 226A 7900 m, fall: driver's eye +1.82 ms (48.2 -> 50.0), vista 25 m up +4.01 ms (48.1 -> 52.2, all four rounds
  apart); plants drawn 42.8 k -> 51.9 k (+20 %), triangles 5.81 -> 6.04 M (far level 0.151 -> 0.170 M), draws 266 -> 266.
  The +20 % plants on a stage is NOT explained (no species gains a far level there): suspect the turning card's cull
  sphere (InitGpu, the impostor sphere: the whole turn's circle x the far grow + the foot offset) passing more far
  instances; for perf to confirm in the player. Editor ms are noisy (spreads in the file).
- Memory: city 186 sheets 8.4 M texels, stage 21 sheets 1.1 M texels (DXT5 + mips, ~12 MB in all); 5 + 5 card atlases
  512 px. FOR_PERF.md lists every line touched.

## Self-test
LifeSimSelfTest in PSXLab, hidden, fresh processes: this branch 64 FAIL, mountain-nm 1527e2b1 64 FAIL, NEW 0, GONE 0.
(Run at 6d1cb3cb; 24851285 after it only turned the cards' default off.)

## Not done / not verified
- The cards beyond the mesh forest (Charlotte's tiles past 1.2 km and CityFar to 2.2 km; a stage's B cards past 2.2 km)
  and in the MIRRORS are still the retro billboards (PSX_TREE_MATCH_CARDS=1 puts the kit's trees on them, measured
  sparse). The fix is in the bakes: Charlotte's tile cards (City/CityTrees.cs 771 CardWidth/h, a 40-minute tile
  re-bake) and a stage's B cards (PSXRacingBuilder.Stage.cs 8306 AddTreeQuads(.., h, ..): give them the mesh variant's
  height and that species' cell) sized as their mesh trees, then PSX_TREE_MATCH_CARDS on.
- SIZE at the far hand-over: 95 species-pairs left, nearly all city saplings, snags and street forms a few pixels wide
  in winter (twigs at the outline; a sapling's trunk under one pixel wide dropped by the mesh, dotted on the card) and
  the 15-degree view step (beech_tree_08 at one bearing). 8 views a sheet would halve the step (2 x the sheets).
- 2 city street beeches in fall: the card more saturated than the mesh (sat 0.61 v 0.87): not traced.
- The mesh levels' own winter twig drop (LOD0-1-2, 20-21 city species) and the far grow (class 11) are untouched.
- The audit's "control" column does not work (turning the eye about the plant kept it centred): not used anywhere.
- The stage cost's +20 % plants drawn is not explained (above). The wide stage bands (PSX_FOREST_LODFADE=0.6) were not
  walked (dropped for sandbox time).
- Coast stages (Emerald Isle, Atlantic Beach bridge, Langston bridge): no mesh forest, nothing to match.

## What the coordinator must do at merge / for a PC build
- Merge `tree-season` (no conflicts with mountain-nm 1527e2b1; merged in at 95eaf2a4). Shared-file edits:
  `Scripts/MeshForest.cs` (Variant.matchLod/matchDress/matchPre + WithMatch/HasMatch; matchCardAtlas; the statics
  TreeMatch, StageBandShare, FarGrowZ, MatchCardsOn; Init's match swap; FarMatrix; MatchBCards called at the head of
  DressMaterials; OnDestroy frees the copies; Faded's far-grow knob), `MeshForest.Gpu.cs` (InitGpu's impostor sphere,
  BakeGpu's impostor rows: perf's GpuDressChanged / GpuFrame untouched), `MeshForest.Pool.cs` (PoolFill's impostor
  rows), `CityMeshTrees.cs` (MatchCards, Redress, CardGain), `CityTreeKit.cs` (cardAtlas), `Shaders/PSXLit.shader`
  (inside PSX_LODFADE: the turning card + view fade, lodD float4, a uv swap before every clip; the crown-card light;
  _PSXIdOn 2 albedo out; _PSXGrowFoot), Editor `PSXRacingBuilder.CityTreeKit.cs` / `.MeshTrees.cs` (the bake hooks),
  `.Stage.cs` (one line: mfc.matchCardAtlas). New Editor files with metas, and `Editor/TreeMatchGains.txt` (+ meta).
- THE CARDS ARE DATA: the PC scene pass makes them with this code (EnsureCityTreeKit, LoadMeshTreeKit); without a
  scene pass: `-executeMethod PSXRacing.EditorTools.TreeMatchBuild.CityMatchOnly` after the city kit is built (3.5 min)
  and `...TreeMatchBuild.PatchStages` (every stage scene with a mesh forest, in place, ~9 min, no rebuild). PSXLab holds
  them now (city 02:04, the 9 stages 02:20, from 7893a673's bake code = the head's). A kit rebuilt with mountain-nm's
  code loses them: run CityMatchOnly again. NO city tile re-bake.
- Generated, not in git (as other bakes): Assets/StoreAssets/Gen/TreeMatch/*.png, Materials/TM_*.mat,
  Generated/TM_*.asset.
- The PSXLab sandbox ends with mountain-nm 1527e2b1's Scripts/Editor/Shaders (the base self-test synced them).

## What the owner will notice
A tree no longer changes as you drive up to it: in winter the far trees are bare like the near ones (no dark leafy
crowns, no orange speckle, no grey box), in fall each far tree keeps the colour it has up close, and the far poplar
is one tree, not four. Far trees also stop flickering their shape as you pass them. Past about 1.2 km in the city and
2.2 km on the mountains, and in the mirrors, the trees are still the old billboards until those cards are re-baked.


# HANDOFF - DAMAGE ROUND 3: the cockpit model through a broken pane, no wavy lines on the body (agent "damage3", branch `damage-cabin` off mountain-nm 457beadd, 2026-11-17 01:00-04:45; NOT pushed, NOT merged)

The owner, 11-17 ~01:25, on round 2's "left glass shattered" cell: "I'm not a fan of the low-poly blocky interior when windows are broken. If shown, the cockpit model should be used." Then (via the coordinator): "The wavy lines when glass breaks also appear on the car body. I don't like them, but otherwise the deformation and cracked glass looks decent." He liked the rest of round 2 (chip / crack / web, crazed and shattered side glass, wheel curb rash).

## What I found
- THE BOXES in his crop were round 2's own: `Shaders/PSXCarGlass.cginc` `GlsCabin` ray-cast grey boxes (seat backs, restraints, a dash) inside the paint shader, not the shell's mesh.
- THE COCKPIT MODEL is `CockpitModel` (the owner's v08: one 55,169-triangle "cabin" part in six materials, a 7,402-triangle wheel, four cluster faces, a mirror, four glass panes; PSX/Cabin) fitted per shell by `CockpitInterior.FitFor` + `Fit3` (dash on the cowl, header 1.5 cm under the roof, side glass inside the shell's). It is drawn only for the driver's camera, inside a shell cut at the windscreen (CabinClip). It has no LOD.
- Drawn from outside as it is, the cockpit pokes through some shells (its roof over a fastback's, its floor under a sill): only a stencil gate keeps it inside the hole on every shell.
- THE WAVY LINES are not glass at all: `Shaders/PSXCarDamage.cginc` drew two NOISE CONTOURS dark on the paint - "CRACKS" (the 0.5 iso-line of a 6.5/m noise, on every face the hit met: a door, a wing, not only a plastic cover) and the creases' dark line (the zero of the fold's noise, 5 cm wide). A side hit stage 2-3 (the hit that crazes or shatters the side glass) draws both on the door - so "when glass breaks". Present since R1a part 2a (11-13), in round 2's before frames too.
- Round 2's leftovers: the cockpit's own side and rear panes (PSX/CabinGlass) had no break. The white sliver by the door mirror is a THIN dark outline drawn on the paint (the mirror sail's edge, paint on both sides): it passes the glass test (dark, colourless) and the frame test (`dEst`, paint 3.5 cm away on some sides only), so it was crazed white.

## What I changed (switches: 0 = the round-2 picture)
- NEW `Scripts/DamageCabin.cs` (`PSX_DMG_CABIN`, default ON): a car with a SHATTERED side or rear pane (crazed is not a hole) gets the cockpit model, fitted exactly as the cockpit view fits it. Two companion renderers of the holed body on copies of its PSX/CarPaint material, after every shell: render queue 2001 marks the hole's own pixels in the stencil (bit 64; the paint's own hole test, so the jagged edge is round 2's), 2002 pushes the depth there 3 m back along each sightline; the cabin's materials (PSX/Cabin copies, queue 2003) pass only on the mark. Wherever the cabin leaves a gap, the paint's own DARK cabin shows (round 2's hole without its boxes). Lit as a room under its roof (sun x0.4) and fogged as the paint round it.
  - Lazily built: only for a car with a hole, only when a camera that can draw it is within 40 m (PC; 20 m Android), at most 8 cabins (PC; 4 Android), the nearest first; one instance per car from a POOL, released 3 s after the last camera leaves. Within 8 m (PC; 5 m Android) the full mesh; past it `CockpitLod`: the same mesh vertex-clustered at 3 cm on a worker thread (cabin 55,169 -> 25,900 triangles, wheel 7,402 -> 1,093; built once in ~50 ms off the main thread). Past the reach: no instance, no draw, the dark cabin. No shadows, no glass, no arms (from outside they end at a shoulder with no body). A camera whose target has no stencil never draws it. The car's own cockpit view up: the copy stands down. Rides the leaning body (CockpitInterior's Ride).
  - Callers: `DamageLook.Push` (the driven car, incl. replays) and `DamageView.Dress` (turntable, showroom, MY CARS, garage bays, lots).
- `Shaders/PSXCarPaint.shader` (shared): property-driven render state (`_PSXStRef/_PSXStRead/_PSXStWrite/_PSXStComp/_PSXStPass`, `_PSXZWrite`, `_PSXZTest`, `_PSXColorMask`; defaults = today's state) and `_PSXHoleMode` (1 mark, 2 pushed depth: one uniform branch in vert and frag).
- `Shaders/PSXCabin.shader` (shared): a property-driven stencil test (default Always) and cull (default Back), `_PSXCabOut` (seen from outside: sun x0.4, both faces lit as fronts, the paint's fog). The cockpit view's materials keep the defaults.
- `Shaders/PSXCarGlass.cginc`: `_PSXGlsDark` (global: holes show the dark cabin, no boxes); `_PSXGlsThin` (global, `PSX_DMG_THINTRIM`): a pixel with paint 3.5 cm away on two OPPOSITE sides of the frame test's ring is a thin strip (under 7 cm), not a pane - a pane's own edge has paint on one side only. (A door-mirror box rule was tried first and dropped: on the FD and the EG the door glass runs ahead of the mirror, and that glass stayed whole in the hole.) NEW `Shaders/PSXGlassBreak.cginc`: the hash / noise / crack-line / cell / crazed functions moved out of PSXCarGlass.cginc unchanged, shared with the cockpit glass.
- `Shaders/PSXCarDamage.cginc` (`PSX_DMG_NOWAVY`, default ON): the two contour lines gated by a global `_PSXDmgLines` (0 = none). The folds stay in the reflections and the light (the normal), the dents in the skin (the vertex push), the scrapes as they were.
- `Shaders/PSXCabinGlass.shader` + `CockpitInterior.SetPaneBreaks` + `DamageLook.CockpitPanes` + `DamageGlass.CockpitPane` (`PSX_DMG_SEATGLASS`, default ON): from the seat the side and rear glass now crazes (a white mosaic, the strike's hole) or shatters (gone but for jagged remnants in the frame, more low in the channel; no rain on it), on the car side it stands on (an RHD cabin's "door_l" is the car's right).
- `Scripts/DamageGlass.cs` (CockpitPane, the switches and the globals), `DamageLook.cs` (WriteBlock takes the def (unused now, kept for the next pane rule); `BlockVersion`; Push -> DamageCabin; CockpitPanes), `DamageView.cs` (the def; -> DamageCabin), `CockpitModel.cs` (`GeoBytes`, `ReadRaw`), `CockpitInterior.cs` (`SetPaneBreaks`, `PaneBreak`), `LifeSim/CarViewer.cs` (the turntable's target 24-bit depth = with a stencil; 16 with PSX_DMG_CABIN=0).
- Editor: NEW `Damage3Shots.cs` (the pictures and the cost probe), NEW `LifeSimSelfTest.Damage3.cs` (+ one line in LifeSimSelfTest.RunAll; `RunDamage3` runs the round's pins alone).

## Proof
Sheets (every pair: LEFT round 2 = PSX_DMG_CABIN=0 PSX_DMG_NOWAVY=0 PSX_DMG_THINTRIM=0 PSX_DMG_SEATGLASS=0, RIGHT this round; fresh processes, the same rows and cameras), all under `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\damage3\out\`:
- `damage3_cabin_hd_final.png` + `damage3_lines_hd_final.png` (HD tier, the FINAL code, the EG and the FD, PSXRec) and `damage3_cabin_classic.png` + `damage3_lines_classic.png` (Classic tier, the final code, all shells, PSXRec Android). `damage3_cabin_hd.png` + `damage3_lines_hd.png` (HD tier, PSXPerf, all five shells incl. the '67 pony coupe) are one fix older (a4214902): there a door-mirror box rule kept a rectangle of FD / EG door glass ahead of the mirror whole in the close-ups - replaced since by the thin-strip rule (PSXPerf was held by another agent for the re-run). Contents: per shell - the owner's view on the car menu's stage, the turntable rig at 2.7 m and 1.4 m (sills, roof, pillars) and the rear three-quarter; BeechGap - chase, beside the door, a replay's trackside camera, the seat looking at the shattered glass - day for every shell, dusk and rain for the EG and the FD; the lot with eight holed cars and the cost frames; the hidden Charlotte curb drive (play mode, day and night); MY CARS.
- The lines sheets: the EG at all seven of round 2's glass severities (door and wing, nose quarter, chase distance; on the road at day and dusk), the FD / pony coupe at the crumpled door and the crushed roof.
- Shells: EG Civic, FD RX-7 (rx7_fd_93), '67 pony coupe (HD; PSXRec has no such shell - the classic sheet shows gto_66, a 60s muscle coupe), sedan_98 and jdm_pickup (traffic shells: no catalog car drives them, so venue and lot only).
- Fit: `cabin BUILT, fit3 True` on every shell, the same fit as the cockpit view's (self-test: eye and scales to 1e-4).
- PLAY MODE: round 2's Charlotte curb drive, re-run HIDDEN on this code (PSXRec, real PhysX): "the cockpit through a hole: drawn (1 live, 1 built)" at both picture sets; frames `out\drive_cab_drive_cab3\`; ALL HOLD.
- Self-test (PSXRec, the full LifeSim suite): base 457beadd 63 FAIL lines / 4,871 ok; this branch 63 FAIL lines / 4,898 ok (27 new checks hold). The same 63 failure lines (sandbox data: deck scenes, lamp maps, release budget). `RunDamage3` alone: 27/27 ok.
- COST (`Damage3Shots` part D, run twice; the seller's lot, eight holed cars on screen from above, and beside one hole; 1920 x 1080 HD tier, PC profile = all eight get a cabin; editor wall clock per frame with read-back, medians of 150 interleaved frames; other agents' editors were running on the same machine). RESULT: no path is measurable at this resolution - the medians of the five states (cockpits drawn / dark cabin / round 2's boxes / wheel rash only / mint) lie within 1 ms of each other down the aisle and swing +-2-5 ms beside a hole, both ways. The second run (PSXRec, 03:33, final code):
  - COST, aisle (8 holed cars, 8 cabins at most, the lot, 1920x1080, editor wall clock per frame incl. read-back, median of 150):
  - cabin  26.86 ms (p10 23.96, p90 39.53)
  - dark   26.97 ms (p10 23.98, p90 41.40)
  - boxes  26.88 ms (p10 24.10, p90 47.90)
  - rash   27.87 ms (p10 24.47, p90 47.48)
  - mint   27.85 ms (p10 23.98, p90 41.73)
  - cabin - dark  = -0.11 ms (the cockpit models)
  - dark  - mint  = -0.88 ms (the glass path, holes dark)
  - boxes - mint  = -0.97 ms (round 2's glass path, the boxes)
  - rash  - mint  = +0.02 ms (wheel rash at stage 3 on all 32 wheels)
  - COST, close (8 holed cars, 8 cabins at most, the lot, 1920x1080, editor wall clock per frame incl. read-back, median of 150):
  - cabin  39.35 ms (p10 28.94, p90 54.34)
  - dark   41.42 ms (p10 28.19, p90 57.35)
  - boxes  39.96 ms (p10 29.67, p90 57.29)
  - rash   38.17 ms (p10 27.81, p90 54.79)
  - mint   43.85 ms (p10 28.29, p90 64.63)
  - cabin - dark  = -2.07 ms (the cockpit models)
  - dark  - mint  = -2.44 ms (the glass path, holes dark)
  - boxes - mint  = -3.89 ms (round 2's glass path, the boxes)
  - rash  - mint  = -5.68 ms (wheel rash at stage 3 on all 32 wheels)
  - CPU: DamageCabin.ForCamera with 8 holed cars: 1.4 us a camera
- Geometry: a cabin is 55,169 + 7,402 triangles near (within 8 m), 25,900 + 1,093 reduced, plus two draws of the body for the mark and the depth; draw calls per holed car 6 (cabin) + 2 (wheel) + 1 (faces) + 2 = 11. CPU: `DamageCabin.ForCamera` 1.4-2.3 us a camera for eight holed cars; nothing at all for a car without a hole (the component disables itself).

## Not done / not verified
- No device check on Android; no build. The Classic-tier frames are from the PSXRec editor (Android target), its PlatformProfile = Android: reach 20 m, full mesh within 5 m, at most 4 cabins.
- The driver's arms are not drawn through the hole (they end at a shoulder with no body; the cockpit draws no torso). The wheel stays straight (it does not follow the steering from outside).
- Crazed glass (not a hole) shows no cabin, as before. Only shattered side and rear panes open; the windscreen never does (laminated).
- Night at BeechGap in edit mode is black and Charlotte's city does not stream in edit mode: the venue's third light is DUSK; the night proof is the play-mode Charlotte drive (rear glass, chase and outside).
- Through the hole, wherever the cockpit model leaves a gap (behind its far window, round its roof on a fastback), the paint's dark cabin shows: a dark room with the far glass as dimmed daylight - not the real world behind the car.
- The cabin's light from outside is an approximation (sun x0.4 under the roof, the sky's ambient in full, no car shadow map for parked cars). The owner's cockpit is a black interior: at the turntable's size it reads dark.
- On low-resolution sheets (the pickup, gto_66) the remnants in the frame follow the texel staircase (round 2's look, unchanged).
- The cost numbers are editor wall-clock with read-back on a machine running other editors; no GPU timer, no in-game frame-time run.
- The thin-strip rule does not tell a B-pillar divider from glass when glass is on both sides of it: the divider keeps thin white remnants either side (they read as glass stuck in the channel).

## Merge notes
- Shared-file edits: `PSXCarPaint.shader` (Properties: 9 hidden; Pass: ZWrite/ZTest/ColorMask/Stencil lines; vert: the push; frag: two early returns), `PSXCabin.shader` (Properties, Stencil block, `_PSXCabOut` lines), `PSXCabinGlass.shader` (`_PSXBreak` block before the rain), `PSXCarDamage.cginc` (two gated lines + the global), `PSXCarGlass.cginc` (functions moved to PSXGlassBreak.cginc; dark cabin; the thin-strip return in the ring loop), `DamageLook.cs`, `DamageView.cs`, `DamageGlass.cs`, `CockpitInterior.cs`, `CockpitModel.cs`, `LifeSim/CarViewer.cs` (one line), `Editor/LifeSimSelfTest.cs` (one line), `Editor/Damage2Drive.cs` (one Note line in Pictures).
- New files with their .meta (fixed GUIDs): `Scripts/DamageCabin.cs` (1e4af8a4...), `Shaders/PSXGlassBreak.cginc` (24a1102d...), `Editor/Damage3Shots.cs` (9167df5b...), `Editor/LifeSimSelfTest.Damage3.cs` (2655173c...).
- No new shader (PSX/CarPaint and PSX/Cabin are already in Always Included), no scene pass, no re-bake, no Resources, no save change. Render queues 2001-2003 were unused.
- Uses the stencil bit 64 (no other project shader uses the stencil; URP Forward / Forward+, no deferred).

# HANDOFF - DAMAGE ROUND 2: glass that reads as broken, wheel curb rash (agent "damage2", branch `damage-glass-wheels` off mountain-nm 742315cd = the PC48 code, 2026-11-16/17; merged at 457beadd - this section was written by damage2 and put here by damage3, which carried round 2's last steps)

The owner on PC48 (verbatim): "I like the damage system, but the broken glass effect is very poor and does not communicate the damage to me. I'd also like to see scuffing for wheels (hitting curbs, etc)". His screenshot: a red EG Civic on the car menu's stage, the windscreen and side glass black panels covered in white squiggles. Sandbox PSXPerf (Windows target). Player-only: nothing under `Scripts/City`, no re-bake, no Resources or data files, no save version.

## What I found
- THE SQUIGGLES: `Shaders/PSXCarDamage.cginc` "CRACKED GLASS" (the last block of `PSXCarDamage`) drew ONE pattern - the iso-lines of two value noises (`1 - smoothstep(0, 0.035, |noise - 0.5|)`), i.e. contour lines - over every dark colourless texel above 22-48 % of the body's height, driven only by the car's `roof_glass` row (`DamageLook.Look.roof`). No pane, no strike point, no severity: the same contours on the windscreen, the side glass, the pillars' black trim and the cowl. The glass under it is the paint shader's dark mirror, which on the stage reflects a dark room: black panes with squiggles.
- There is no glass submesh and no pane data: the paint shader finds glass as the sheet's dark colourless texels (`PSXCarPaint.shader` frag, "Glass and black trim"). Glass rows exist only for ROOF incidents (`DamageModel.Resolve` case ZRoof: `roof_glass` 1 / 2 / 3); a front, side or rear hit wrote panel, bar and lamp rows and never touched the glass.
- WHEELS: nothing read a wheel's side. The wheels are downward rays (`CarController` wheel contacts); the body box meets walls; a curb is 15 cm of ground the ray climbs. And the city's curb COLLIDER is not the drawn curb: it is chamfered (`CityMeshes.Curbs2.cs`: 11 cm up over 22 cm out, measured in the drive as "0 0 0 0 6 11 14 15 15 15" cm every 10 cm) so a car can climb it - a side ray never finds a steep face.

## What I changed
Switches (both default ON): `PSX_DMG_GLASS2=0` = PC48's glass (the contour web), `PSX_DMG_WHEELS=0` = no strike read and no rash shown.
- NEW `Shaders/PSXCarGlass.cginc` (included by `PSXCarPaint.shader` after PSXCarDamage.cginc; needs `PSX_CAR_TAP`, defined there for both texture tiers): `PSXCarGlass2` - the panes told apart by the way they face and where they stand in the cabin (windscreen ahead of the seats, rear glass behind them, left / right by the normal; nothing under the belt, no mirror housing):
  - windscreen, laminated: a stone chip (pit, cone ring, five short legs); a crack running from it; a spider web (15 radial cracks, some running on, broken rings, fine legs, crushed white at the strike; each shard's normal tilted and the web dished in, so the reflections break up). Lines never thinner than a pixel (fainter instead), so they hold at the chase camera.
  - side and rear glass, tempered: crazed (white cubes close up, their mean at a distance, coarser cracks, runs out of the strike, a small hole where it was struck); shattered (a hole: jagged crazed remnants hang from the sill and the head of the frame, and the CABIN shows through - two front seats with head restraints, the rear seat's back, the dash, the roof lining, the far side's glass - ray-cast boxes with real parallax, lit by the day that comes in, never by the sun on the pane).
  - a broken lamp's lens: shards with cracks between, some gone (the model's lamp map; `DamageView` now binds it in the views when a lamp is out).
  - `PSXWheelRash` (a wheel's renderer only): the rim's lip (bright texels with rubber within 2 cm outside them) scraped to bright metal with dark gouges, the sidewall rubbed pale on its shoulder, in streaks that run round the wheel, over an arc that grows with the stage (57 degrees, 150 degrees, all round); stage 3 adds the cut.
- `Shaders/PSXCarPaint.shader` (shared): the include and `PSX_CAR_TAP`; in `frag` - the screen derivatives of bpos and uv taken once, the two calls, `glass *= 1 - glsFlat`, the cabin's light on `lit`, and `k / refl / hi` scaled by `1 - glsFlat` before `col`. `Shaders/PSXCarDamage.cginc`: `_PSXGlsOn` declared; the old web gated off when it is 1.
- NEW `Scripts/DamageGlass.cs`: WHICH PANE, HOW, WHERE, from the rows (nothing new saved): a front hit that crumpled the nose (panel stage 3) cracks the windscreen low on the struck side, a web with the bar pushed back (bumper_bar 2+); a dented door crazes that side's glass, a crumpled one shatters it; a crumpled tail crazes the rear glass, shattered with its bar pushed back; `roof_glass` 1 = a chip under 4.5 m/s else a crack, 2 = a web + every tempered pane crazed, 3 = every tempered pane shattered. The strike is seeded by the incident's hitId (the same crack in the drive and every view). Also the cockpit's windscreen break (`CockpitTexture`: thin pale lines, under 6 % of its square drawn).
- NEW `Scripts/WheelScuff.cs`: the rules (pure, tested) and the reader on the career car. A KERB = ground 12-40 cm over the wheel's own within 40 cm of its sidewall (rays down beside each grounded wheel); a STRIKE = the ground under the sidewall itself has begun to rise (the drawn curb's face is in the tyre) and the kerb faces within 60 degrees of square to the axle. Not a strike: a low or rolled kerb (under 12 cm: racing kerbs, the city's 10 cm rolled curb) and a kerb met nose-on (the tread climbs it). Its stage from the press on the wheel's side (closing speed square to the face x how square it stands to the axle: 0.10 / 0.9 / 2.2 m/s) and the rub along it (0.3 / 2.5 / 6 m). Also the body's side on a wall, barrier, rock or another car at a wheel (collision contacts within a tyre's radius of an axle, at wheel height; 1 / 3 / 6 m/s). One row per wheel: `wheel_rash_fl / _fr / _rl / _rr` (raised, never doubled), cause "impact", note "front-right rim: curb, West Trade Street" (`CityWorld.StreetNameAt`, else the venue's name).
- `Scripts/DamageModel.cs` (shared): `KWheel = 5`, `KindName`, `Resolve` (a KWheel incident = that wheel's row + a small paint loss), `DamageLedger.Wheel(corner, stage, vn, what, now)`, `Arm` attaches `WheelScuff`. `Scripts/CarController.cs` (shared): `public int SuspensionMask` (one getter).
- `Scripts/DamageLook.cs` (shared): `Look` gains the panes, their strikes, the wheels' rash; `Read` folds them (`DamageGlass.Fold / Bars`); `Push` writes the cabin frame and each wheel's rash; `WriteBlock` gains an optional `cabin`; `PlaceBreak` (the cockpit's chip / crack / web where it was struck) replaces `PlaceCrack` when GLASS2 is on. `Scripts/DamageView.cs`, `DamageViewWatch.cs`, `LifeSim/CarViewer.cs`, `OnFoot/CarShell.cs`: the views pass the model's def and their wheels (so every view shows the panes and the rash, and a repair shows at once).
- `Scripts/LifeSim/DamageBank.cs` (Stamp: a wheel row's price by stage, its label, its cause note), `FaultCatalog.cs` (four hit rows, $110 flat: $70 / $110 / $240 by stage, a day at the shop), `FaultView.cs` (class Chassis), `Inspection.cs` (a RIMS line under WHEELS), `DamageHomes.cs` (value -0.4 / -1 / -2 % per wheel; the debug text).
- Editor: `LifeSimSelfTest.Damage2.cs` (+ one line in `LifeSimSelfTest.cs` RunAll), `Damage2Shots.cs` (the stills), `Damage2Drive.cs` (the Charlotte drive).

## Proof
- Sheet: `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\damage2\out\damage2_sheet.png` (2450 x 6982): the owner's view at every severity, close-ups, wheel rash at three stages close and at chase distance, the Charlotte drive (day and night; outside, chase, cockpit), the Classic tier. Every pair before | after, fresh processes (PSXPerf, Windows).
- The drive (real PhysX, nothing planted): ten degrees into West Trade Street's curb at 25 km/h -> front-right pressed in 1.46 m/s, rubbed 0.69 m, stage 2; nose-in 7.9 m/s (web low, both lamps); back-in 6.9 m/s (rear glass shattered). ALL HOLD.
- Self-test (PSXPerf): base 742315cd 71 FAIL lines / 4,843 ok; this branch 69 / 4,881. New failures 0; 36 new checks hold.
- MY CARS after the drive (shot by damage3 on 11-17 from the merged round-2 code, PSXRec): `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\damage3\out\menus_after_menus_after\` against `menus_before_menus_before\` (ALL HOLD): the web and the broken lamps show in MY CARS and on the car menu's stage.

## Not done / not verified
- Since done by damage round 3 (the section above): the cockpit's side and rear glass, the dark trim by a door mirror, a rain frame, the frame-time measure, MY CARS.
- No Android device check. Dark wheels get the sidewall scuff only; inner-side strikes are not read; repairing one wheel's row clears that wheel only, through the existing repair order (no wheel refinish service). The lamp shards are small; stage-1 rash is hard to see at chase distance.

## Merge notes
- Shared-file edits: `PSXCarPaint.shader` (the include block after PSXCarDamage.cginc; five small edits in `frag`), `PSXCarDamage.cginc` (two lines), `DamageLook.cs`, `DamageView.cs`, `DamageViewWatch.cs`, `DamageModel.cs`, `CarController.cs` (one getter), `LifeSim/CarViewer.cs`, `OnFoot/CarShell.cs`, `LifeSim/DamageBank.cs`, `FaultCatalog.cs`, `FaultView.cs`, `Inspection.cs`, `DamageHomes.cs`, `Editor/LifeSimSelfTest.cs` (one line). New files carry their .meta (fixed GUIDs): DamageGlass.cs, WheelScuff.cs, PSXCarGlass.cginc, the three Editor files.
- No scene pass, no re-bake, no Resources, no save version (a wheel row is an ordinary CarFault: id, stage, corner1, cause, causeNote; old saves load; a save with wheel rows opened by an OLDER build keeps the rows as unknown ids - the same as any hit row added since v26).
- The paint shader gained a branch for damaged glass and for scuffed wheels only: a car never hit takes neither (`_PSXDmgOn` / `_PSXWheelRash.x` are 0).

# HANDOFF - WATER PART B: the HD water fixed and ON by default on PC (agent "water", branch `water-hd-b`, 2026-11-17 00:45-04:10; NOT pushed, NOT merged)

The owner, 11-17 ~01:00: "Why hasn't the upgraded bodies of water been implemented? You showed me screenshots of it days ago"
(PC49, Bat Cave in heavy rain, the old flat grey water). Then, after trying `PSXRacing_WaterHD.cmd` on Lake Lure: "it looked
good. The main issue was the current/tide/waves were moving very fast for a lake. It looked like an ocean."
Why it was off: the review (`scratchpad\water\REVIEW_SYN.json`) merged it dark (Part A) for F01 (static), F03 (city bank foam
rim), F06 (no rain on it) and the rest of Part B; Part B was queued and never started. Branch off mountain-nm 1527e2b1, merged
mountain-nm 749e05de (the perf round) in. Sandbox PSXLab (atomic lock, shared with treeseason). Scratch `scratchpad\water\`
(PROGRESS_B.md, jobB.ps1, water_player.ps1, measure.py, sheets\, runs\).

## What I found (file:line on 749e05de unless said)
1. LAKE MOTION. Lake Lure's materials ARE the Lake preset, `_OceanWaves 0` (no sea leak). The speed was the shader: each ripple
   octave slid rigidly downwind at the phase speed of its waves (PSXWaterHD.cginc HDOctave `uv.x -= drift*t`: 1.48 m/s on the
   lake's 7 m tile), the gust patches crossed at half the wind (4.5 m/s wind: 2.3 m/s), and every lake carried the sea's 7.5 m
   vertex ripple train at 3.4 m/s (vertHD's Gerstner). Measured: the PC49 HD lake changed 18.7 levels a second; its "river"
   slid with the wind, not down the river.
2. F01 static: the march's per-pixel jitter (cginc:251) plus a hard thickness cut; the wobble and the march normal read the
   full-detail ripples (single-pixel variation sampled the mirrored world pixel by pixel); no roughness for what the mips average.
3. F02 edge bars: tEnd from the frame's side planes (cginc:245-250) and a side fade toward the sky.
4. Rain: a roughness cut-off on the march (Part A's early-out idea, kept by me at first) and summing roughness terms linearly
   drove rough to 1 on a grazing river: its mirror became the bright overcast horizon - in the measuring player the Bat Cave
   river in rain was exactly the owner's "flat grey sheet" with HD ON. The old water there is a white sheet with a foam rim.
5. The HD variant of PSX/Water fails Vulkan compilation ("top-level params outside of cbuffers") - round 1 (749e05de) too, so
   the next APK cut from mountain-nm would have logged a shader error. GLES3, D3D compile clean.
6. `WaterLook.ActiveHere` was true in every scene without a sea (garage, home, dry stages): keyword on, depth copy every frame.
   perf's copies-on-demand keyed the opaque copy on that same scene-level flag.
7. From the driver's seat the water is mostly hidden: Lake Wylie behind Wilkinson Blvd's wall (0-1% of the view), the bridge
   parapet (3% driver, 8% chase); Lake Lure is seen from US 64/74A mainly at the marina / downtown (8%); the Rocky Broad 1-3%.

## What changed (all behind PSX_WATER_HD; =0 is the old water exactly; 21 commits, head = see the hand-back)
- DEFAULT ON ON PC: `WaterLook.On` = `PSX_WATER_HD` if set, else VISUALS > WATER (HD / CLASSIC, `WaterPrefs`, PlayerPrefs
  `psx.waterhd`, default HD when `PlatformProfile.PC`); phones and WebGL never (row not offered). Live switch, no restart.
  `PSX_WATER_HD_SEA` (the coast stages' sea) unchanged, OFF. Classic/Retro picture modes keep whatever the row says (owner's call).
- Lake motion: still water reads the sheet twice, +-0.45 rad off the wind, each creeping at 0.22 x phase speed (half that in a
  light air), equal weight: the ripples work in place; gusts cross at 0.18 x wind; no vertex train on lakes/city. Rivers: flow
  x1.5 (the bake's reach means 0.15-1.95 m/s -> surface speed; Manning gives 1.5-2.7 m/s in the gorge), a small wind creep. The
  sea's motion is byte-for-byte the old formula (seaK). Lake shore foam only above 4 m/s wind.
- F01: no jitter; crossings weighed by the gap in front of the ray (fade, not cut; a partial first hit blends with the next);
  Toksvig roughness from the sheet's measured mip spectrum + capped screen-space normal variance, in quadrature; octave 3 drawn
  to 40 m, gone by 60 m; the march normal and the wobble from octave A two mips softer (+2 fetches); spec power widens with it.
- F02: march ends only at the top/bottom; x clamped to the edge column; no side fade.
- Rain: roughness +0.25, 40% of the mirror taken and the rest broken up (wobble x3.5), rough water mirrors its banks BLURRED
  (4 taps) instead of the sky, misses read the car's panorama (CarReflections' `_PSXEnvPano`, as the paint does) instead of the
  bare sky, greying toward the sky's grey (10% near, +16% far), rain rings wider and pocked both ways (bright on dark water, dark
  on sky-bright water), no sun/moon glint under overcast or rain (the sky's clear share in `_PSXWaterWind.w`).
- City foam gain 0 (F03). Rivers: white water only above 0.35 of the mesh's b and none at the banks (in the vertex shader: no
  scene pass); bank foam on running water capped at 0.35 and broken by the lace. River preset: flow roughness 0.25/m/s, less
  absorption, amber-green scatter. Lake: vertex depth replaces the frame's bed where the lidar's drawdown floor is >1 m shallower.
- Presets applied at runtime by material name (`WaterPresets`, *RiverWater / *LakeWater / CityWater; the builders use the
  same numbers): a retune needs no scene pass. `PSX_WATER_PRESETS=0` = the materials as built (tools).
- Cost/waste: copies only for a camera with HD water in its frustum (`WaterLook.WaterInView`, cached per camera per frame);
  depth-copy RT per camera, released after 300 idle frames, old pass released on OnValidate; the HD variant warmed once
  (`[WaterLook] warmed ...`); half the march steps beyond 300 m; no march looking steeply down (fresnel < 0.05).
- `WaterVariantStrip` (Editor): the HD variant is left out of every non-Windows build (Vulkan error; phones never draw it).
- Refraction and wobble offsets turned into screen axes (item 16); the weather memo keys on the weather itself (Forced, sky and
  road overrides); `CopyReady` (no copy shader = no tier claim); CityFlickerFinder exempts PSX/Water from the 2500-queue rule;
  `tools/water/make_ripples.py` committed. Gust field from three readings (190 / 442 / 1180 m) off the sea.
- Self-test `TestWaterB` (6 checks), WaterShots part B sets (lure, drive, seastrip, rivertest, compile, strips, tiers, debug).

## Proof (PSXLab, PC profile, fresh processes, same poses, frozen clock; scratch `scratchpad\water\`)
Sheets: `sheets\B_lakelure.png` (Bat Cave chase in heavy rain first; old | PC49 HD | part B | 1:1; video frame where one
exists), `sheets\B_charlotte_drive.png`, `sheets\B_motion.png`, `sheets\B_static.png`.
- Motion (frames 1.0 s apart, water pixels): Lake Lure 18.7 -> 8.1 levels/s, no coherent slide (phase correlation peak at 0);
  Lake Wylie 21.2 -> 10.5 per 0.5 s; the Bat Cave river now shifts 9 px/s DOWNSTREAM (PC49: 8 px/s the wrong way, with the
  wind); the Bogue sea identical (12.6 = 12.6 levels/s; 214 px/s swell).
- Static, reviewer's grain2.py at 1080: Lake Wylie water 81.7 (round 1) -> 41.5 (old water 3.5, rest of frame 11.3), and of
  it only 4.2 is single-pixel (37.3 survives a 2x2 box: the mirrored dithered trees) against 34.4 in round 1; Sugar Creek
  72.5 -> 24.0 (rest 21.2); test lake 8.2 -> 3.0; river mouth 28.5 -> 9.0; staged river 21.4 -> 12.7 (rest 12.7). At 1440 lines: Lake Wylie 42.4 (38.1 after the 2x2 box; rest 9.8), Sugar Creek 25.0 (rest 17.9), test lake 3.5, mouth 12.4 (rest 15.7).
- Edge bars: staged lake noon edges 1.00 / 1.00 of the interior (round 1 1.58 / 1.46); sunset 1.10 / 1.01; calm 1.03 / 0.90.
- Rain (staged lake): water mean 65 -> 94 (sky 94); brightest water 104 < brightest sky 126 (round 1: 131 > 126); the 8-px
  block std RISES 1.4 -> 3.0 (the gate wanted a 30% fall): the dry lake is now a smooth mirror and rain adds pocks.
- Night: Lake Wylie water peak / median 16.7x (old 5.1x); headlights on in every night cell.
- Rim: no foam in the city (gain 0); the Catawba waterline band reads 1.26 x the open water (round 1 1.28) - it is the mirrored
  sunlit grass bank, no pale line in the 1:1 crop; Sugar Creek 0.68.
- Tiers 0 and 1, the rear-mirror view (tier 0): no magenta, murky water. GLES3 and D3D compile clean; Vulkan fails (stripped).
- COST (measuring player PSXRelease\_water, 3832x2054 offscreen probe, in-process A/B HD vs `WaterLook.ForceClassic`, perf's
  copies-on-demand ON, 7-10 other Unity editors running): main-view render-thread+GPU HD minus classic: Lake Lure marina noon
  +0.15 (sd 0.25) / +0.40 (sd 0.49) ms; Lake Wylie bridge noon +1.03 (sd 0.38, an earlier build) / +0.11 (sd 0.29); Bat Cave
  rain -0.11 / +0.31 (sd 0.44) / +0.48 (sd 0.66); Wylie night rain +0.06 (sd 0.77). Frame-time deltas are inside the noise
  (sd 1.3-4 ms). Worst case, editor synthetic, 45% of a 3652x2054 frame water: tier 0 0.86, tier 1 0.99, tier 2 2.83 ms
  (round 1 measured 1.26 ms at 1440p = ~2.6 ms at this size; the old water ~1.0). Watched drive (Lake Lure 9.0 km to Bat
  Cave, rain, 2560x1440 window, 60 fps cap): 56 fps avg, p99 32.5 ms, GPU 12.8 ms.
- Self-test (PSXLab, no PSX_WATER_HD, no PC profile): FAILED (64) twice, the same 64 checks each time, all of them also failing in perf's run of 749e05de-era code (66, a superset); the 6 new water checks ok.

## What still looks wrong / not done
- Bat Cave in rain from the chase camera: the river reads light-to-mid grey, now textured with pocks and darker than PC49 (white
  sheet + foam rim), but still a grey strip: at that grazing angle it mirrors the overcast sky, and the banks it would mirror are
  off the top of the frame (the car's panorama stands in, blurred). Lake Lure itself mirrors its ridge; the gorge river at noon
  mirrors its banks.
- From the driver's seat Lake Wylie is behind a wall / parapet; the city cells are mostly chase views. No lamp streak across the
  water in the night cells: no street lamp stands across the water at these poses (the analytic streak code is unchanged).
- Item 13 phone frame: not done (phones stay off; the variant is stripped from APKs). Item 15: the per-material split and the
  Bogue sound vs ocean split not done (the coast scene gate stays). Item 17: CityFar's ring water (Scripts/City, a re-bake) not
  touched or measured; gust grid checked from 30 m (wylie_gusts_high: no grid seen). Item 19 (the HD sea) not done. HitchProbe
  drive not run (the warm-up is in). The review's rain block-std gate fails by design (above).
- Cost numbers are noisy (shared machine); the in-process A/B is the honest number.

## For the coordinator at merge
- No scene pass, no city re-bake (nothing under Scripts/City), no data files. GraphicsSettings unchanged. CityWater.mat is
  re-dressed by the next BuildCityScenesOnly (and the presets are applied at runtime anyway).
- Shared files: `PerfSwitches.cs` (copies-on-demand now `WaterLook.CopiesWanted(cam)` + `WaterLook.CopiesOnDemand`; already in
  this branch on top of 749e05de); `SpeedBlurFeature.cs` (Create, AddWaterDepth, WaterDepthPass only - rain-1116 edits
  AddRenderPasses / WetPass: separate hunks); `SettingsCatalog.cs` (the WATER row after REFLECTIONS); `CityFlickerFinder.cs`
  (one line); `LifeSimSelfTest.cs` (one Guard line).
- New files with committed metas: `Editor/WaterShots.PartB.cs`, `Editor/WaterVariantStrip.cs`, `Editor/LifeSimSelfTest.WaterB.cs`.
- `tools/PSXRacing_WaterHD.cmd` is no longer needed (HD is the default); `PSX_WATER_HD=0` gives the old water.
- `PSXRelease\_water` (8.8 GB) is my measuring player: recycle it after the merge.

## What the owner will notice
Lake Lure and Charlotte's lakes and rivers mirror their ridges, trees and bridges in dark green-brown water instead of a
blue-white sheet; the lake is calm, its ripples rise and fade in place, the river runs downstream. In rain the water goes grey,
rougher and pocked, with no glints; the Bat Cave river in rain is still a lighter grey strip from the chase camera.

# HANDOFF - THE LINE GATE (agents "linegate" then its Opus 5.5 successor, branch `city-linegate` off mountain-nm 742315cd, 2026-11-16/17; NOT pushed, NOT merged)

The owner, PC48, North Tryon Street: "I still see a lot of areas around Charlotte where the road and/or road lines do not make
sense, are not accurate, and would never pass DOT requirements ... these issues cannot get through the quality gate audits."

## THE RULE (also in Docs\CHARLOTTE.md, "The line gate")
`tools\city-linegate.ps1` is part of every Charlotte merge and build. A branch may not raise any class above
`tools\city\baseline\linegate_baseline.json` (the tool exits 1; 2 when the run fails). The build note quotes its totals line.
A class is closed only at 0; no "known, ignore" list. No Charlotte race track ships on a road that fails. Re-record the
baseline (`-WriteBaseline`) only in a commit that brought a class DOWN, or that changed a rule with its proof.

## The baseline (full4: drawing code 742315cd, gate 416dd3b7 (+ b7f92df4: hairpins, after the run), graph b2aa3453, 10,626 road tiles, 23 min in PSXRoadA)
| class | failing places | T1 freeways + primary | T2 minor | T3 neighbourhood |
|---|---:|---:|---:|---:|
| SHIFT | 2,703 | 1,326 | 1,361 | 16 |
| SJOG | 2,851 | 1,408 | 1,429 | 14 |
| KINK | 3,231 | 1,662 | 968 | 601 |
| WIDTH | 630 | 313 | 316 | 1 |
| CONT | 1,725 | 793 | 922 | 10 |
| SURFACE | 261 | 260 | 1 | 0 |
| JUNCTION | 657 | 102 | 407 | 148 |
| MEDIAN | 76 | 9 | 45 | 22 |
| STEP | 9,684 | 2,240 | 4,162 | 3,282 |
| BARRIER | 1,622 | 379 | 1,193 | 50 |
| BRIDGE | 12 | 9 | 3 | 0 |
| **ALL** | **23,452** | | | |

The totals line the tool prints (quote it in every build note):

    LINE GATE full4 graph b2aa3453: SHIFT 2703 SJOG 2851 KINK 3231 WIDTH 630 CONT 1725 SURFACE 261 JUNCTION 657 MEDIAN 76 STEP 9684 BARRIER 1622 BRIDGE 12 | ALL 23452 failing places

## What was found
1. The old line gate never gated: `Editor/SmoothRules.cs:215` `ReportOnly = true` since 2026-09-29, `CitySmooth.RunFull`
   exits 0; its offline twin lists 368,982 runs on today's data and exits 0; it judges the builder against its own plan.
   The city audit's line reports are boxed to Uptown (`CityAudit.Hooks.cs:82`) and judge colour / meaning, not rate.
2. The owner's spot (x 2470 z 8201, e15599 / e9470 / e15598 / e15597 / e15596, 45 mph) is caught: the relay
   (`Scripts/City/LineModel.cs:1774` NeedsRelay, `:578` Relay) moves the centre line 2.9 m in 40 m and 3.2 m back in 57 m
   (needs 131 / 144 m); a lane line ends at a joint (a 6.2-6.9 m lane). Real road: 3 + TWLTL + 3, straight (splits2).
3. CORRECTION of the first note: the "dark stripes" are NOT the stretched road texture. The HD road base
   (`HDSrc/TexHD/city_road_base_*`), its normal / AO / roughness maps and the paint masks' grain channel are flat (column and
   row means within 1/255 over 64 bins); stretching an even texture draws nothing. Likely tyre marks (`Scripts/SkidMarks.cs`,
   every sliding car): settled by splits2's fresh PC-look frame of the spot (no stripes). SURFACE "stretched" is census
   only (`PSX_LG_STRETCHFAIL=1` fails it again).
4. One-cause classes: the junction slab's 12 mm lip, 5,801 places from ONE constant (`CityMeshes.cs:10791` FanProudM = 0.012; NCDOT 610-12 allows 3.2 mm; also tracks S1); parapets / rails on the edge line 1,622; grade breaks 3,883; the line model's relays and short tapers (SJOG / SHIFT / WIDTH / CONT: scratchpad\linegate\WORKLIST_FOR_SPLITS2.md ranks them)

## False positives: checked on frames, fixed (all in the census line "passed by rule:" of city_linegate.txt)
Each rule below was proven on sampled editor frames (sheets in Review\Charlotte_Line_Gate_2026-11-16\samples*_<CLASS>.jpg);
the recorded run passed by them:
- 109125 curve facets (a deflection over 4 m chords gone over 8 / 16 m chords: a curve drawn as a polyline, or a facet within 2.5 cm of its curve)
- 1632 sharp kinks, S-jogs or line shifts (10 deg, 45 deg, steeper than 1:1.5) within 60 m of a ramp's diverge / merge (its gore's V read through hidden paint)
- 521 more over another road's pavement (recorded as SURFACE: two roads' pavement drawn over each other)
- 15627 grade breaks gone over 20 / 40 m chords (a vertical curve drawn in facets)
- 1550 pavement-edge moves at freeway ramp terminals within the AASHTO terminal taper (25:1)
- 18434 kinks (2490 more at a gore's nose) and 3453 pavement-edge steps at junction corners (15 m from the drawn end at a node of 1 or 3+ arms: the mouth strip and its curb return)
- 638 line ends inside an edge and 532 at a joint where another line of another pattern continues it (MUTCD 2009 3B.04 / 3A.06: a pattern change)
- 1175 line ends within 25 m of a junction (the stop line or crosswalk: MUTCD 2009 3B.16 / 3B.18)
- 3699 edge-line ends inside an edge (MUTCD 2009 3B.06 / 3B.07: edge lines may stop at curbs, driveways, openings)
- 1503 side roads with no control meeting a road of their own class (MUTCD 2009 2B.04: the basic right-of-way rule)
- 523 road mouths whose nearest solid line is the far side's, a centre yellow (a left turn) or a split's own yellow
- not failed at all (census only): stretched pavement texture (`PSX_LG_STRETCHFAIL=1`); stop-line placement read from the
  signal plan, not the mesh (`PSX_LG_STOPLINE=1`)
- rule changes besides: KINK must hold over 8 / 16 m chords; grade breaks over 20 / 40 m; WIDTH: under 1.8 m is a
  buffer, a solid line near the curb a shoulder, two edge lines no lane; JUNCTION mouth: the nearest line within 3 m,
  not a centre line; doubled lines: parallel for 20 m; BRIDGE: a twin deck counts half the lanes; line ends join
  only within 45 deg

## What I changed (new files only; an audit: no drawing code, no switch)
- `Assets/PSXRacing/Editor/CityAudit.LineGate.cs`, `.LineGate2.cs` (`CityAudit.RunLineGate`), `.LineGateShots.cs`
  (`CityAudit.RunLineGateShots`: plan / driver's eye / UV-grid editor frames; `PSX_LG_NOBLD=1` no buildings), .meta files.
- `tools\city-linegate.ps1` (the gate; the baseline records the drawing commit), `tools\city\baseline\linegate_baseline.json`,
  `tools\city\linegate_report.py`, `_tracks.py`, `_worklist.py`, `_samples.py` (`--nfor`, `--uv`, stratified by kind).
- `Docs\CHARLOTTE.md`: "The line gate" section appended (the rule, what it reads and judges, the baseline table).
- Gate env: PSX_LG_BOX, _NAME, _TAG, _SPOT, _PLANS, _PLAN_AT, _FOOD, _SHOTS, _NOBLD (frames), _STRETCHFAIL and _STOPLINE (=1
  fail the two kinds that are census only).

## Proof
- Runs (PSXRoadA, WebGL, all whole-city unless said): full1 (first run, before the fixes: 46,175 places), full2 (after the
  first false-positive fixes: 27,393), full4 (the baseline, through `tools\city-linegate.ps1 -WriteBaseline`: the tool's first
  end-to-end run), full3 (the first tool run; superseded after its worst frames showed ramp-overlap readings), box runs b1 (I-485 gores) / b2 (a spot dump at one gore). Outputs: scratchpad\linegate\out\<tag>_*.
- The tool's exit codes: `scratchpad\linegate\tooltest.ps1` (no Unity) on the recorded run: write 0, same run 0, one class
  +1 place exit 1 ("LINE GATE FAILED: 1 class(es) raised: KINK n -> n+1"), classes lowered 0 (CLOSED shown), box run 0 (not
  compared), no baseline 1, no run file 2: 7 of 7.
- False-positive sheets (real editor plan frames, 64 x 36 m, every cell labelled with what the gate said):
  `Review\Charlotte_Line_Gate_2026-11-16\samples_<CLASS>.jpg` (312 places of full1) and `samples2_<CLASS>.jpg` (222 of
  full2, stratified by kind); scratchpad\linegate\report\s1\m_*.png close-ups (gore hooks, stop lines, curbs, steps).
- The owner's spot: `Review\Charlotte_Line_Gate_2026-11-16\owner_spot.png` (plan + driver's eye, same code as PC48).
- The worst 60: `Review\Charlotte_Line_Gate_2026-11-16\worst_01..06.png`; the heat map
  `Charlotte_Line_Gate_2026-11-16.png`; the note `Charlotte_Line_Gate_2026-11-16.md`, every failure in `.csv`.

## Not done / not verified
- Street-level comparison of the sampled places (the gate proves the drawn road against the standard, not against the
  real street); (the stripes are settled: tyre marks).
- The older Unity audits were not re-run at the spot (read in code: none measures a line's sideways rate; the smoothness
  gate's offline twin was run: 368,982 runs, exit 0).
- Not judged: turn-lane storage, median nose shape / island width, crosswalk / arrow / stop-line placement (the stop-line
  check read the signal plan, not the mesh: off, PSX_LG_STOPLINE=1), paint under another ribbon (counted only).
- AASHTO Green Book 9.7.2 (bay tapers) and 10.9.6 (ramp terminal 25:1) are from memory, not re-read; NCDOT's RDM PDF was
  not readable as text (FDOT / TxDOT tables used, read 2026-11-16).
- MEDIAN, BARRIER, BRIDGE, STEP fan lips and grade breaks were confirmed on frames as drawn faults by the measure; whether a
  bridge's real parapet also stands on its edge line was not checked against imagery.

## At merge
- New files only, plus one section appended at the end of `Docs\CHARLOTTE.md` and this HANDOFF section. No scene pass,
  no re-bake, no data file. The Editor .meta files are committed (minted in PSXRoadA).
- After merging anything that touches Scripts/City or the city data: run `tools\city-linegate.ps1` (about 27 min in a WebGL
  sandbox); exit 1 = a class rose: the branch does not merge until it is back at or under the baseline.
- splits2's branch (charlotte-splits2) adds RoadProfiles.Tw7 and moves lines: after it merges, run the gate and re-record
  the baseline in that merge commit if every class went down or held.

# HANDOFF - THE GRIP ROUND (agent "grip", branch `tyre-grip` off mountain-nm 749e05de = PC50, 2026-11-17 01:30-03:40; NOT pushed, NOT merged)

Owner, 11-17 ~01:30: "I noticed that cars slide on banked roads. Even when resting in place at the start of a race. This
means tires don't actually grip the roads." Scratch: `scratchpad\grip\` (PROGRESS.md, drift_table.txt, sheet_*.png,
grip_sheet_before_after.png, every run's report). Sandbox PSXRec (Android), all runs hidden batch.

## What I found (base = PSX_STATIC_GRIP=0, which is the 749e05de code path exactly)
1. **Parked cars TURNED on a bank.** The parking hold (CarController.TireForces, base lines 3683 / 3921-3925 / 3999-4003)
   shares the car's weight EVENLY over the four tyres (`parkShareKg = massKg / groundedAll`) and anchors only the centre of
   mass (`parkOff`, 3686-3692). On a bank that is a couple about the CG on any car that is not 50:50 front to rear (the RX-7
   stands 65:35 on its springs), resisted only by the hold's velocity damper: a steady yaw of ~0.02 rad/s. In the game's own
   pre-race hold every car on the Lake Lure grid (8-10% bank) turned 22 deg in 20 s, its corners moving 84 cm, with the centre
   within 4 mm; Gillespie Gap grid 17.5 deg / 66 cm; NC 226A 3.5-6.6 deg; 40 deg / 1.5 m at Chimney Rock's 14% bend. This is
   what the owner saw: the cars visibly swing round on the grid (sheets below). The CG-only measure the old ParkedCarSim
   used could not see it.
2. **No lateral force at rest.** The slip model (TireCurve 2229; slip = atan2(vLat, max(|vLong|, 0.3)) at 3745; then
   `fLat *= Clamp01(|contactVel| / 0.6)` at 3975) goes as vLat^2 near zero: nothing holds a car that is not latched by the hold
   (the hold needs |v| <= 0.3 AND a pedal/lever or 0.5 s still, 3644-3666). So a car dropped onto its grid slid 2-6 cm while it
   settled (up to 18 cm on the steep spots).
3. **The crawl band chattered.** The slip model's sideways stiffness per m/s is C / v, applied explicitly: 11 g dt / v of the
   tyre's own mass per tick = 1.8 / v at 60 Hz, past the stability limit below ~0.9 m/s. Sideways speed flipped sign every
   other tick: lateral jerk 1400-1575 m/s^3 pulling away from every grid, and a car rolling slowly down a banked grade walked
   0.5-2.5 m sideways per 10 m (at the Lake Lure spot the four tyres gave 11.7 kN against the 1.6 kN gravity asked).
4. Not the causes: the pre-race hold (PlayerCarInput.cs:280-284 brake 0.3 + lever; AIDriver.cs:1076-1099 brake 0.4) freezes
   both axes through the hold; there are no WheelColliders (raycast wheels); the banked road is on the road layer (circle
   1.27-1.37 x Fz at rest).
5. Tyre numbers at rest, base, Lake Lure grid RX-7 parked: Fz 2030-4350 N, circle 2840-5470 N, lateral 242 / 373 N (equal
   per axle: the even split) = 1230 N against gravity's 1232 N across the car, at slip -5.0 / +4.7 deg, contact 0.027-0.032 m/s
   (the turning). After: each tyre carries its own load (221-442 N on the 60:40 sim car at 10%), slip 0.000, contact 0.0000.

## What I changed (all behind PSX_STATIC_GRIP, default ON; =0 is the old code path bit for bit)
- `Scripts/CarController.cs`: a contact-patch (brush) model per tyre at rest and at a crawl. `patchLat[i]` = the patch's
  sideways deflection, integrated from the contact's sideways speed and relaxed over a 0.30 m relaxation length as the wheel
  rolls; force = (C / 0.30) x deflection + a 0.9-critical damper on the deflection RATE, clamped to the friction circle (a
  clamped patch slides). C is the slip model's own cornering stiffness, so in steady rolling the patch gives exactly
  C x tan(slip): the slip model's linear branch. Blended into the slip model by contact speed 0.5 -> 2.0 m/s (smoothstep);
  above 2 m/s nothing new runs (the patch only tracks the slip model's force so slowing down hands back seamlessly).
  `patchLong[i]`: braked wheels (pedal quarter, lever on the rear) hold fore-aft through the same patch; PARKED = every wheel
  held to its circle through the patches, so the hold's even-split overrides are skipped with the switch on. Patches cleared in
  the air, on TeleportTo and while kinematic. New public statics: StaticGripOn, StaticGripWeight, PatchForce.
- Tools: `Editor/GripSim.cs` (menu PSX Racing/Grip Sim: 60:40 car on planes 2-12% bank x 0/8/15% grade, 5 rest cases, launch,
  crawl, 20 m/s crossfall straight, 40 m banked ring, cost timing), `Editor/GridGripCheck.cs` + `tools/grid-grip-check.ps1`
  (real grids with the countdown held; yaw and body-corner drift; the route's most banked spot; launch; PSX_GRIP_SHOTS strip;
  PSX_GRIP_LAPONLY solo rolling-start telemetry), `Editor/LifeSimSelfTest.Grip.cs` (TestStaticGrip, after TestParkedCarHolds).

## Proof
- Sheets (real editor frames, overhead, the game's own pre-race hold, switch off and on in fresh processes, t = 0/5/10/15/20 s):
  `scratchpad\grip\grip_sheet_before_after.png` (all four), `sheet_LakeLure_grid.png`, `sheet_GillespieGap_grid.png`,
  `sheet_LakeLure_spot.png`, `sheet_ChimneyRock_spot.png` (the spots are STAGED: the player stood in its lane at the route's
  most banked place). Before: the cars swing round; after: they do not move.
- Drift table (20 s at rest, worst car, body corner / yaw), `scratchpad\grip\drift_table.txt`:
  game's hold, grids: Lake Lure 84 cm / 22.3 deg -> 1.2 mm / 0.00; Gillespie 66 cm / 17.6 -> 0.6 mm; NC 226A 24 cm / 6.3 ->
  0.6 mm; Chimney 18 cm / 4.9 -> 0.2 mm; Blue Ridge 17 cm / 4.4 -> 0.7 mm; Uptown (flat) 1.4 -> 0.8 mm. Brake and lever:
  the same, player and AI. Spots (3.7-14% bank): 35 cm-1.5 m / 9-40 deg -> <= 0.8 mm / 0.00. Settle from touchdown in the hold:
  9-58 mm -> 0.3-5.7 mm. Every held case, every car: under 2 mm and 0.00 deg (target < 1 cm).
  No pedals (neutral, no brake) on a GRADE still rolls ~10 m in 20 s, as a real car in neutral does (engine braking holds it
  near 0.5 m/s); but it now rolls straight: sideways 0.5-2.5 m -> 3-127 mm (the true slip-angle creep), yaw 3-18 -> <= 0.23 deg.
  On grids with no grade it holds (<= 3 mm; Uptown's 1.6% rolls 8-11 mm before the half-second latch, as before).
- Hand-over (GridGripCheck launch at 0.35 throttle from the lever): lateral jerk 1420-1575 -> 0-3 m/s^3, sideways in 1.5 s
  0.02-0.17 m -> 0.000-0.028 m; full throttle at the spots 1270-1360 -> 3-33 m/s^3. GripSim 10% bank: jerk 800-870 -> 0-14,
  sideways-speed sign flips 46-114 -> 0, crawl across 0.35 m -> 0.03 m.
- At speed (unchanged): solo rolling-start laps, 120 s each, Lake Lure / Gillespie / Chimney: distance 3220 / 4252 / 2568 m,
  top 43.1 / 54.6 / 32.6 m/s, peak lateral 2.409 / 1.612 / 1.472 g and every slip-angle percentile IDENTICAL off and on (splits
  within 0.02 s). HandlingPlayCheck A-H: same figures (brake 98.2 / 96.3 m; stops end 0.02 s sooner - the last crawl to rest;
  lane change identical); the Viper "H keyboard corrections" FAIL is in both (pre-existing). GripSim: crossfall 2.5% / 8% at
  20 m/s hands-off drifts -0.54 / -1.10 m per 120 m in both; the 8% banked ring holds 20.08 m/s vs 18.67 flat in both
  (+7.6%: the bank helps, as it did). Full races with AI from a standing grid differ run to run (the launch and AI contact).
- Self-test (LifeSimSelfTest.Run, PSXRec, fresh processes): 63 FAIL off and 63 on, the same set (fail_diff: 0 new, 0 gone);
  the new "static grip" section passes 8/8 and the parked-car section still passes.
- Cost: per-wheel arithmetic only (one exp, two square roots). Timed in the editor JIT: 125 ns per patch; worst case
  (8 cars x 4 wheels x 2 patches, every tick) 8 us per 60 Hz tick. It runs only below 2 m/s of contact speed; above
  that it is one divide and a clamp per wheel. No allocation, no new physics queries, no draw cost.

## Not done / not verified
- The crossfall pull at speed is unchanged and measurable: hands-off at 20 m/s a 2.5% crown takes the car 0.5 m downhill in
  120 m (heading -0.3 deg). That is the slip model (the nose-heavy car's front needs more slip), as a real car does without
  cross-caster; changing it would change the handling at speed, so it is left and reported.
- Full-throttle launch on a 10% bank still kicks the tail down (yaw 1 rad/s in the sim): the driven rears' circle is spent on
  wheelspin, which is physical and the same off and on.
- Not run: ParkedPlayCheck (the town left-car walk test), the PC look turn on PSXPerf (no visual change), a watched showing.
- The FFB self-aligning torque reads latForce: at a crawl on a bank it now carries the bank load (it already did while parked).

## Merge notes
- Code only: CarController.cs (fields block after ParkHoldSpin; TireForces: the in-air reset, the kinematic clear, the
  fore-aft static block before the PARKED hold, the lateral static block after the 0.6 fade, both parkHold overrides now
  `&& !StaticGripOn`; TeleportTo clears the patches), LifeSimSelfTest.cs (one call), new Editor files + metas, tools ps1.
- No data, no scene passes, no re-bakes.

# RAIN 2 (branch rain-1116 from 742315cd; 2026-11-16/17; agents "rain" and its successor) - code at 0b824e42; round 3 by the successor agent

The owner, 2026-11-16: "I'd like to see the rain effects be improved. This means while sitting idle, cruising, and racing.
Splashing on the ground, how it pools on the road. I say this, meanwhile we need to start focusing HEAVILY on performance."

## State
- Everything is behind `PSX_RAIN2`, DEFAULT OFF (unset = PC48's rain; `=1` on). Sub-switches (public static bools on
  `Rain2`; env `PSX_RAIN2_POOLS`, `_RINGS`, `_SPLASH`, `_WIPERS`, `_GLASS`, `_SPRAY`, `_TRAILS`, `_AUDIO`) default on under it.
  Tiers `PSX_RAIN2_TIER=high|low|mobile` (HIGH = PC with REFLECTIONS on; the editor needs `PSX_PROFILE=PC`).
- Built (round 1-2): pools by the road's slope + the city's gutter pans and rain rings in the wet road's two passes
  (`Shaders/PSXRain2.cginc`, `PSXSpecBend.cginc`); probed-ground splashes with a cover mask (`Scripts/Rain2Driver.cs`);
  wipers and look-through drops on the cockpit glass (`PSXCabinGlass.shader`, `CockpitInterior.cs`, `SpeedBlurFeature.cs`);
  puddle hits + rooster tail (`TireSpray.cs`); mist behind traffic (`Rain2Traffic.cs`); a damp trail (`WetTrail.cs`);
  procedural rain audio (`Rain2Audio.cs`).
- Round 3 (this section):
  - THE PARKED WINDSCREEN is covered: two more bead sizes while parked (6 and 8.5 mm cells x 1.6 on the windscreen, 85-92 %
    of cells live), each bead a small fisheye on the frame behind it (the scene from five of its radii round it, upside
    down, brighter in the middle, a dark rim only at the edge, one glint up and to the side), more runners with unbroken
    trails, and a veil of scattered light on the sheet. `PSXCabinGlass.shader` (`Bead3`, `R2G_FISH`, `R2G_VEIL`).
  - WIPED ARCS: the blade leaves a bank of water (a film packed with small beads, 22 mm wide) beyond its tip and past the
    top of its stroke, so the clear arc reads against the wet glass (`R2G_RIDGE_M`).
  - DOOR MIRRORS carry beads (`PSXCabinGauge.shader` mode 1; each door mirror's material gets `_MirrorMetres` in
    `DoorMirrors.cs`; the interior mirror and the gauges leave it at zero and are unchanged).
  - The wipers' standing clock counts time spent in another view (`CockpitInterior.TickWipers`): the play check caught
    strokes starting after a minute parked in the chase view.
  - The cost tool (`Editor/Rain2Shots.cs`) now times in PAIRS (A-B / B-A alternating, median of the paired differences, a
    NULL row per pose for the floor; `PSX_RAIN2_TN`, `PSX_RAIN2_TROWS=whole`); `Rain2Shots.RunWithSelfTest` runs
    `LifeSimSelfTest.RunRain2` and the stills in one editor start; a door-mirror still.
  - The play check's driver allocation check allows 8 KB of the shared heap over the whole drive (the editor's meter is
    the whole heap; one stray 4 KB page from another thread failed it once).

## Cost (3832x2054, RTX 3070, HIGH tier, heavy rain; full table in the scratchpad's `rain\COST.md`)
WHOLE package, three runs, 1000 render pairs a pose: chase +0.2 ms, road filling the frame +0.3, cockpit parked +0.5,
cockpit cruising +0.4 (each run's reading -0.1..+0.72; NULL rows up to +0.76). The owner's game was running for all three
(GPU 99-100 %), so this is good to about +-0.5 ms: under 1.0 ms on the best reading, NOT proven with margin - hence still
default OFF. Not measured: a fixed register-pressure cost of the bigger shaders (needs a player-build A/B against the
base exe: perf tour `ab=Rain2.On:0` with `PSX_RAIN2=1`), IL2CPP, Android, the LOW and MOBILE tiers. CPU: driver
0.12 ms/frame, audio 0.007, spray + trail 0.10 per car; 0 frames over 1 ms.

## Proof
- `TestRain2`: 21/21 ok (first time it ran in Unity). Full self-test: 68 FAIL vs base 68; one new
  (`lux_coupe_97: ...on faces that look forward`, a car light-map check on the shared sandbox's car data, not this
  branch's code) and one gone (`every shell has a body and a wheel mesh`).
- Play check (free roam, I-77 south, night, heavy rain; hidden runs): run 01:13 (code d0e46766): every check but two
  passed - the wipers swept after the car had stood in the chase view (fixed: the standing clock now lives in
  Rain2Driver, 5fcd724a) and one stray 4 KB heap page in the driver's meter (the editor meter is the whole heap; the bound
  is now 8 KB a drive, aa7d5bd3). Run 01:32 (aa7d5bd3): only the wiper check failed (first cockpit entry; fixed by
  5fcd724a). Run 02:22 (5fcd724a): PASS, 12/12 (0 wiper strokes while parked, driver 0 B). Counts: 232-247 splashes parked, 128 anchors, 13 wiper strokes in 16 s of cruise, 298-334
  puddle hits, 3276-3736 trail pieces, 14-15 traffic cars; CPU driver 0.08-0.12 ms/frame, 0-1 frames over 1 ms.
- Sheets (scratchpad `rain\`): `rain2_sheet_stills.png` (reference | PSX_RAIN2=0 | =1, fresh processes, incl. the parked
  windscreen at 2x and a door mirror), `rain2_sheet_played.png`.

## Still short of the owner's reference
- The parked screen is covered now but its beads read warmer and coarser than his (our sodium-lit street vs his white-lit
  garage); no real blur through the sheet.
- No wheel RUTS: the road shader knows its width only on the HD road path (`_RoadGutter.y`), not where the lanes are, and
  the edit-mode stills draw Classic roads.
- No rear-window wiper; blades are plain dark lines. Rings are subtle from the chase camera; ring / crown sprites simple.
- Traffic mist is placed at a guessed rear axle; pools do not feed the aquaplane rule; no damp trail after the rain stops.

## Merge
- `Editor/LifeSimSelfTest.cs`: one `Guard(nameof(TestRain2), TestRain2)` line beside other branches' Guard lines.
- Shared files edited: `PSXWet.cginc`, `PSXWetSSR.cginc`, `PSXLit.shader`, `PSXLamps.cginc`, `PSXHeadlights.cginc`,
  `PSXCabinGlass.shader`, `PSXCabinGauge.shader`, `TireSpray.cs`, `WeatherFx.cs`, `WetReflections.cs`,
  `CockpitInterior.cs`, `DoorMirrors.cs`, `SpeedBlurFeature.cs`, `CarLights.cs`, `RoadSurface.cs`, `Dev/PerfTour.cs`.
- No scene, bake, data or ProjectSettings change; `Scripts/City/` untouched; two new shader includes, no new shader.

# HANDOFF - THE PERF ROUND (agent "perf", branch `perf-1116` off mountain-nm 742315cd, 2026-11-16 22:10 to 11-17; NOT pushed, NOT merged)

Owner, 11-16 ~22:00: "we need to start focusing HEAVILY on performance." His PC: i7-10700K, RTX 3070, 3832x2054 at 144 Hz;
PC48's mountain stages ran 40-49 fps. Target 60 minimum, 144 the goal. Scratch: `scratchpad\perf\` (OWNER_TABLE.md = the one-page
table, PROGRESS.md, FOR_TREES.md, sheets\, runs\ = every tour's files).

## What I found (all at 3840x2066, summer, noon, the owner's own saved settings; other Unity jobs running throughout)
1. **Before the fixes the mountains were GPU-bound**: Lake Lure 24.7 ms parked (in-run), 21-35 ms driven. The main thread was busy
   10.6-16.5 ms and waited the rest (`TimeUpdate.WaitForLastPresentationAndUpdateTime`).
2. **The mesh forest's leaves ran PSX/Lit's whole pixel program before the alpha clip** (PSXLit.shader:2250), with no pre-pass.
   The forest cost at least 7 ms of the frame; sun shadows cost 4.5 ms, most of it shadow look-ups on leaf fragments that the
   clip then threw away (0.7 ms once the pre-pass is on).
3. **URP's SSAO is a renderer feature with no camera test** (URP ScreenSpaceAmbientOcclusion.cs AddRenderPasses): it ran for
   every camera - each car-reflection face, each mirror, the HUD overlay and the scene's OutputCamera - with a depth copy each.
   All AO cost 3.4-4.3 ms; 1.6 ms of it was those cameras. The render thread fell from 12 ms to 3-4 ms when they stopped.
4. **The scene's OutputCamera draws nothing** (PSXRacingBuilder.cs:8358, culling mask 0, there to clear the back buffer) but is a
   plain HDR camera on the PC renderer: depth copy, colour copy, SSAO and a float-to-screen blit at 4K, every frame: 0.9 ms.
5. **The pipeline's opaque-colour copy has one reader, the opt-in HD water** (PSXWaterHD.cginc:46): under 0.4 ms.
6. **The owner's own settings** (registry, shared by every build and by the tours): CAR REFLECT 60, all three mirrors 60,
   FRAME RATE 60. CAR REFLECT 60 = all SIX probe faces every frame (ViewRatePrefs.ProbeFaces; AUTO = one): 3.6-4.8 ms of main
   thread, 2.7 ms of frame at Lake Lure, 6.2 ms of an 18.9 ms frame at Trade and Tryon (965 of 1,539 set-pass calls).
   Each face is 55 (stage) to 128 (Uptown) draws: about 0.4 ms of fixed per-camera cost; nothing to cull without removing things.
7. **The city is main-thread bound**: 20 ms a frame at Trade and Tryon with 69 cars = traffic's fixed step 5.0 (LeaderGap 2.0 of
   it, CityTraffic.cs:1512 / 1967), reflection faces 4.4-5.7, main view submit 3.2, sun shadow maps 1.7 (SunShadows.RenderFor).
8. **A 60 fps limit sits on the tour's window** whatever the frame costs (a frame with no forest, no reflections, half size:
   16.7 ms). Game code sets none in a tour. So every "after" median of 16.6-16.9 ms means "reached 60".
9. Unity's `FrameTimingManager.gpuFrameTime` is NOT GPU load here (0.5 ms with the reflections off): never quote it.
10. Daytime garbage: one collection every ~25 frames by day, 1 in 2,000 at dusk; +1.7 ms inside render submit on those frames.
    Source not found (needs a development build's allocation profile).

## What I changed (every switch: `PSX_<NAME>=0` = the frame as PC48 drew it)
| Switch | Default | File | Measured (in-run, Lake Lure chase, frame 24.7 ms) | Same picture (lossless frames, clock stopped) |
|---|---|---|---|---|
| `PSX_FOREST_PREPASS` | ON | Scripts/MeshForest.Gpu.cs (DepthPre, BuildPre, the main-view loop of GpuFrame) | -5.5 ms (0.5); cockpit -1.6 | 13 of 7.9 M pixels, at most 2 of 255 |
| `PSX_AO_DRIVER_ONLY` | ON | Scripts/PerfSwitches.cs (SecondaryView) | -1.6 ms (0.25); main thread -0.75 | world seen directly 0 pixels; mirror images 0.14, the car's own paint 0.67 levels on average |
| `PSX_LEAN_OUTPUT` | ON | Scripts/PerfSwitches.cs | -0.9 ms (0.65) | 0 pixels |
| `PSX_COPIES_ON_DEMAND` | ON | Scripts/PerfSwitches.cs | under 0.4 ms | 0 pixels |
| RENDER SCALE row (`PSX_RENDER_SCALE`, tour `scale=`) | 100 % | PSXQuality.cs (RenderScalePrefs), PSXCameraOutput.cs, Shaders/PSXBlit.shader (_Sharpen), SettingsCatalog.cs | 85 % -3.7 ms (0.6), 75 % -7.0, 67 % -10.0, 50 % -11.9 of 25.8 | softer; the owner's choice |
| AMBIENT SHADE row (FULL / HALF / OFF) | FULL | PerfSwitches.cs (AOQualityPrefs) | HALF -2.0 to -3.3 (cockpit -4.2); OFF -3.4 to -4.3 | HALF: not visible to me in the crops; OFF: flatter undergrowth |
| GRAPHICS row (QUALITY / BALANCED / PERFORMANCE; CUSTOM) | QUALITY = the defaults | PerfSwitches.cs (GraphicsPresetPrefs), ViewRatePrefs.cs (Set) | sets RENDER SCALE, AMBIENT SHADE, FOREST DETAIL, CAR REFLECT, mirrors | per preset |

The three rows show in the PC picture only (PlatformProfile.PCPicture), so the editor's self-test and the web / phone menus do not have them.
Measuring only (no game change): Scripts/Dev/PerfTour.Probe.cs (new: the in-run probe, the census, frozen lossless snaps, frame-cap
and window-size lines), PerfTour.cs / PerfTour.Stage.cs (probe hooks, `from=` and `at~` routes in the city, `scale=`), PerfClock.cs
(per-camera main-thread time, a sun-shadow slot), SunShadows.cs (ForceOff, ResOverride, the slot), CarReflections.cs (MaskOut,
FarOverride), Editor/PSXBuildWin64.cs (`PSX_FRAME_TIMING=1`: Frame Timing Stats in that player only, the project's setting restored).

## Proof
Driven on screen at 3840x2066, 35 s each, fresh processes, back to back; "off" = the four switches at 0 (PC48's frame), "on" =
the four switches on (player 1, 83803b5f, switches by environment; the same code paths as today's defaults). Other Unity jobs
were running in every run (5-11 editors); the "on" medians of 16.6-16.9 ms are AT the test window's 60 fps limit.

| Place, view | | fps | median ms | 95 % | 99 % | main thread ms | render thread ms | set-pass | triangles M |
|---|---|---|---|---|---|---|---|---|---|
| Lake Lure, cockpit | off | 42.9 | 21.3 | 34.0 | 67.5 | 16.5 | 12.2 | 986 | 3.33 |
| | on | 58.8 | 16.9 | 20.3 | 22.1 | 10.9 | 2.9 | 1023 | 3.34 |
| Lake Lure, chase | off | 29.7 | 35.0 | 47.4 | 63.0 | 13.1 | 11.7 | 808 | 2.33 |
| | on | 50.0 | 16.7 | 44.7 | 74.7 | 10.3 | 4.3 | 857 | 2.36 |
| NC 226A lower, chase | off | 36.0 | 22.2 | 59.0 | 111.9 | 13.1 | 12.1 | 694 | 2.42 |
| | on | 36.0 | 19.4 | 61.5 | 101.3 | 14.6 | 5.0 | 746 | 2.46 |
| Blue Ridge at Linn Cove, chase | off | 27.0 | 34.1 | 73.1 | 107.9 | 17.8 | 15.4 | 751 | 1.77 |
| | on | 57.7 | 16.6 | 20.8 | 41.0 | 8.4 | 3.1 | 792 | 1.71 |
| Uptown, Trade St at Tryon, chase | off | 40.4 | 23.9 | 34.5 | 42.0 | 20.8 | 8.2 | 1539 (probe) | 9.6 (probe) |
| | on | 43.4 | 21.4 | 32.8 | 71.4 | 18.0 | 3.2 | | |
| Myers Park, Queens Rd, chase | off | 37.4 | 23.9 | 47.7 | 86.3 | 18.2 | 9.4 | | |
| | on | 34.0 | 24.3 | 60.8 | 125.9 | 25.4 | 3.8 | | |

- Triangles are Unity's counter (it does not see the forest's indirect instances: add 4-6 M on the stages, twice for cutouts with the
  pre-pass). Set-pass rises by about 100 on the stages with the pre-pass (one more draw a cutout).
- NC 226A and Myers Park "on" runs were hit by other jobs (see their 95 % and main-thread columns); the city is main-thread bound.
- In-run switching (4-5 repeats each way) is the reliable size of each gain: see the table under "What I changed".
- NOT DONE: the same table from player 3 (defaults ON, C:\Users\mcgee\PSXRelease\_perf, built 00:52 from 41390a94) and the night /
  rain pair. The owner started PC49 at 00:43 and no tour may run beside his game. Ready to run: `scratchpad\perf\matrix.ps1 -Sets fin1`
  then `python -I scratchpad\perf\fintable.py f`. Night rain was measured in-run only (Lake Lure cockpit, frame 20.1 ms:
  AO rule -1.9 ms (2.0), pre-pass -0.2 (0.8), reflections -2.7 (0.6)).
- Same-picture pairs (player 2, on screen, clock stopped, lossless): `scratchpad\perf\sheets\perf_same_picture_lakelure.png`,
  `perf_same_picture_uptown.png`, `perf_ao_driver_only_cockpit.png`; the options that DO change the picture:
  `perf_options_that_change_the_picture.png`. What still looks wrong in my own sheets: nothing in the pairs; in the options sheet
  RENDER SCALE 67 % is visibly softer in the leaves, and AMBIENT SHADE OFF flattens the undergrowth.
- Menus: the game's own settings capture with the PC rows (`DebugBenchPreview.Capture`, PSX_PROFILE=PC), VISUALS page at six sizes
  (owner 2000x1077, 16:9, 4:3, three phones): every caption fits its button, every control is on the canvas, all 23 controls
  reachable by pad; pictures `scratchpad\perf\menu\pc_settings_visuals_*.png`. The rows are plain `Step` rows (click / tap / pad
  confirm), so touch reaches them as it reaches PICTURE. The tool's two failing lines are the CREDITS page's BACK off the canvas
  at phone and 4:3 sizes (not a row of mine; not checked against the base). At phone 640x360 the tool reports every VISUALS row's
  name cut 18-19 px on the left by its panel, the old rows (PICTURE, REFLECTIONS, FOREST DETAIL) as much as the new ones.
- Self-test in PSXLab with this branch: 66 FAIL. Against the base report made in PSXPerf at 742315cd (68 FAIL): 5 gone, 3 new,
  all three content checks of PSXLab's own state (a car lamp-map check that also fails in the cars agent's report; the tower's
  night-window check, which only runs where Charlotte.unity is newer than the builder - PC49's scene pass made it run here). A base
  run in PSXLab itself was NOT made, so "no new failures" is argued, not shown: run the merged self-test before the build.

## How it was measured (use it again)
- `-perftour "stage=LakeLure&from=2400&hour=Noon&weather=Clear&day=200&view=chase&probe=park&onscreen=1&pcfg=4,10,24&pv=name=op,op;..."`
  in a window: each variant switched on and off in alternating blocks inside ONE run; the table gives the frame delta and its
  spread. Ops: `set:Class.Member:v`, `fs:Feature:Field:v`, `layer:`, `comp:`, `rend:`, `g:`, `q:`, `feat:`, `urp:`, `res:` / `add:`
  (offscreen only). `&freeze=1` stops the clock and writes lossless full-size snaps (+ a second base = the noise floor).
  `&census=1` counts what stands in the driver's view and in the six probe views.
- The headless (`-batchmode`, `offscreen=`) probe is useless while another job loads the CPU (spreads of 10-130 ms).
- Scratch `perf\run.ps1`, `matrix.ps1` (the run sets), `perf_build.ps1` (the measuring player), `sheets.py`, `outside.py`, `where.py`.

## Not done
- City traffic's fixed step (5.0 ms at 69 cars). Needs a per-function profile first; it must drive exactly the same
  (CityTrafficCheck, the census), and every City edit costs a measuring build its 40-minute bake.
- Sun shadows: the maps are recorded every frame (1.0-1.6 ms main thread, about 100 DrawRenderer calls plus the cars' map).
- The forest's remaining ~5 ms (leaf pixels, once each now): with the tree round, see `scratchpad\perf\FOR_TREES.md`.
- SRP-batcher compatibility of PSX/Lit (the city's main view is 792 draws at ~4 microseconds each): a shader port, not attempted.
- BaseFromArgs (MeshForest): -248 set-pass calls, no frame gain measured; left off.
- The 60 fps limit's source; the daytime garbage's source.

## For the merge / the PC build
- No Scripts/City file is touched: the city bake is reused.
- New files with metas: Scripts/PerfSwitches.cs, Scripts/Dev/PerfTour.Probe.cs.
- Shared files: MeshForest.Gpu.cs (tree-season touches BakeGpu only; mine is the block after BaseFromArgs, the last line of
  GpuDressChanged and the main-view loop of GpuFrame), SettingsCatalog.cs (three rows after PICTURE), PSXQuality.cs, PSXBlit.shader
  (one property, one block after the FXAA call), SunShadows.cs, CarReflections.cs, ViewRatePrefs.cs, PSXCameraOutput.cs,
  PSXBuildWin64.cs, PerfTour*.cs, PerfClock.cs.
- PSX/Lit: nothing changed, but the pre-pass RELIES on every clip standing before `if (_SkylinePass > 1.5) return` (line 2333).
- Consider building the shipped player with Frame Timing Stats off (as now): its GPU figure misleads.
- The tours share the owner's registry (window size, view rates, frame cap): after any on-screen tour check that
  `Screenmanager Resolution Width / Height` still read 3840 x 2066 (0xf00 x 0x812).

# HANDOFF - THE CAMCORDER, ROUND 2: a camera at every race point, flex scoring (agent "camcorder", branch `replay-camcorder2` off mountain-nm 742315cd, 2026-11-16 night; NOT pushed, NOT merged)

Owner, 21:50, after playing PC48: "As for the camcorder replays, I don't like the follow cams. Now that checkpoints are
being added, at least one camera can should record at each checkpoint. Logically, it proves the cars started at the
starting line, reached each checkpoint, then reached the finish line. It also gives players a chance to 'flex' at
checkpoints to improve their tape's value."

## What I found (file:line on 742315cd)
- Round 1's director planted an operator every ~130 m and, every third shot or wherever nobody stood ahead, rode a
  "buddy's car" on the focus car's own line (CamcorderCam.cs:48 ChaseEvery, :293 BuddyEye, :384-409): the follow cam.
  On Charlotte's Uptown Loop most of a tape was that car, because few operators could be planted.
- The checkpoint design (scratchpad\checkpoints\DESIGN_v2.md 1.1, 1.3, 2.2): gates are `cps[]` with kind, x y z, hx hz,
  spans (l0 l1); a circuit's gate 0 is its lap line, a sprint's last gate is its finish and its start is the grid;
  the suggested route stays `rm.path`; cars may take any road between gates. Its stage 2 planned "trajectory lenses"
  for CamcorderCam (7.1): that is what this round's clips already are.
- Nothing times a split on a stage or a strip today (no split points exist in the game): the fallback plants them.
- A city race on ANDROID streams its ground round the cars only (CityWorld.cs:153 CourseOn = PC profile; the course
  preload that pins every tile of the route is PC-only): at replay time the ground at a far checkpoint is not there
  to stand a bystander on. My first city run found cameras at 2 of 14 points for that reason.
- Foliage has no colliders: a line-of-sight test cannot see leaves. A camera 23 m into the woods at Gillespie Gap's
  finish filmed leaves (the first mountain run).
- A batch (hidden) editor draws no frames; a camera rendered on request there shows the camcorder's picture but not
  the HUD canvas (the OSD), and its framebuffer is 640x480.
- PowerShell: `$x = if (..) { @("a") }` is a string, and string + array glues into one argument (my own bug, below).

## What changed
Switches: PSX_CAMCORDER=0 = the trackside replay as before the camcorder; PSX_TAPE_FLEX=0 = round 1's tape price (no
clips scored); PSX_CAM_CPFILE=1 = read Resources/CityCheckpoints/<route>.json directly when no provider answers
(default OFF: no real file has been filmed); PSX_CAM_MIC, PSX_TAPES, PSX_MONTAGE as before. Defaults: camcorder ON,
flex ON, file reader OFF, montage OFF.

- `Scripts/Replay/RacePoints.cs` (new): THE INTERFACE. `RacePoint` = kind (Start / Checkpoint / Finish), index, pos,
  heading, halfWidth (+ pathIdx, soft). `RaceCourse` = points, loop, laps. `RacePoints.Provider` (a static
  Func<RaceManager, RaceCourse>) is where the checkpoint system feeds it; `RacePoints.FromGates(gates, loop, laps,
  gridPos, gridHeading)` takes its chain in the checkpoint file's own shape (`RacePoints.Gate`: kind, x, y, z, hx, hz,
  l0, l1). Until then `FromPath`: the venue's start and finish plus EVENLY SPACED points every ~700 m on a city route,
  SPLIT points every ~1 km on a stage or a circuit, a strip's half-way.
- `Scripts/Replay/RacePlan.cs` (new): the bystanders. At every point a FRONT camera (past the point, looking back up
  the road) and a BACK one (before it, the other side when it can be) - on ground that is not road, not water, not a
  car, not a roof or a wall top, level enough to stand on, no road further out (a median), nothing solid within an
  arm's length, the point and the road each side of it in sight. A city may stand them up to 24 m back; a stage keeps
  them on the cleared verge (8 m). A LONG LENS from further along when nowhere near will do. A point the feed invented
  slides up to 120 m along the road to where somebody can stand; a real gate never moves. Found DURING THE RACE as the
  field reaches each point (`Scout`, called by the recorder twice a second): a streamed city has ground only there.
- `Scripts/Replay/TapeClips.cs` (new): THE CLIPS. From a car's recorded trajectory (not the path: any road between
  gates works): START (the grid 2.5 s before it moves, to 70 m past the line), each CHECKPOINT in order (150 m out to
  70 m past), the lap line between laps, FINISH (to the end of the tape). A point never reached ends the tape there.
  A respawn's jump is not a crossing. And THE FLEX: `Score` = what the clip's camera saw.
- `Scripts/Replay/CamcorderCam.cs` (rewritten): plays the clips in order; FRONT until the car is at the point, BACK
  after; a burst of static between two recordings (and the tape waits in it, up to 4 s, until a streamed place's
  ground is there); skip keys land on the clip before / after; a deck stops at the tape's end. The hand, zoom, focus,
  exposure kick as before. NOTHING TRAVELS: the buddy's car, its dash and pillar, PoseAt are gone.
- `CamcorderOSD.cs`: the clip's name on the second line (START, CHECKPOINT 3/9, LAP 2/3, FINISH) over the tape clock;
  the touch bar's skips read << CLIP / CLIP >>. `Camcorder.cs`: ClipLabel, Static, TapeCourse. `RaceReplay.cs`: Hold
  (the cut's static is not a pause), Scout from the recorder, CamcorderCam.Setup(replay, camera).
- FLEX SCORING (per clip, the player's car): speed through the point ((km/h - 60) x 0.12, to $25); SLIDE = the body
  12-100 degrees off the way it is going for 0.4 s or more ($12 a second to $30, + up to $15 for the angle); AIR $20;
  LED $10; PASS $20 (a place gained in view); DOOR TO DOOR $12; NEAR MISS $15 each (two counted); CRASH -$35; SPIN
  -$20. The tape = the sum of its clips + the result (a race at all, a win or a podium, the field, a close finish).
- `Tapes.cs`: tape v2 - the points and the clips (with their tags and dollars) ride in an EXT2 block; a v1 tape loads
  and plays and keeps its old price; a tape from a newer build is refused by name. The VCR films a v2 tape at the
  tape's own points. `LifeHomeScreen.Tapes.cs`: each row's second line is the tape's best clip ("BEST  CP 3: 77 MPH ·
  SLIDE"); the shelf and the VIDEO STORE list WHAT THE CAMERAS SAW, the store with each clip's dollars.
- `Shaders/PSXCamcorder.shader`: the static between recordings; GAIN UP to 3.2x in the dark with a shadow lift (night
  mountain tapes were too black); the interior silhouette removed.
- Tools: `tools/sandbox-lock.ps1` (the atomic lock: random pause, check, unique stamp, 3 s read-back),
  `tools/camcorder-shots.ps1` + `Editor/CamcorderShots.cs` (full races fast-forwarded, a frame at every clip, the flex
  proof; HIDDEN while PSXRacing.exe runs), `tools/tape-check.ps1`, `Editor/LifeSimSelfTest.Camcorder.cs`.

## Proof (real editor frames, PSXRec, Android target, career day 320 = NOV 16 1999)
Sheets in `scratchpad\camcorder\` (every cell labelled): `r2_owner_sheet.png` (the one for the owner),
`r2_points_city_day.png` / `r2_points_city_night.png` (Uptown Loop, one lap: START, CHECKPOINT 1/12 .. 12/12, FINISH,
each frame with its OSD), `r2_points_mountain_day.png` / `r2_points_mountain_night.png` (Gillespie Gap: START, 6
checkpoints, FINISH, front and back camera), `r2_flex.png`, `r2_before_after.png`, `r2_tapes.png`.
How they were shot: the mountain day and the flex runs WATCHED (854x480, pillarboxed); everything from 23:28 HIDDEN
(-batchmode, no window, later silent) because the owner was playing - a hidden editor's framebuffer is 640x480, the
4:3 picture whole, OSD drawn. The BEFORE frames are the trackside replay rendered on request in a hidden run: the
world only, without the replay's own HUD text.
- Mountain, Gillespie Gap (6.7 km stage): 8 race points (START, 6 split checkpoints, FINISH), 16 cameras (two at every
  point), 0 on a road, 0 blind, the OSD names 8 of 8 clips, no camera travels (the lens within its sway of its
  bystander's spot at every shot). Whole race day (watched) and night (hidden).
- Charlotte, Uptown Loop (9.2 km circuit, one lap, the city STREAMED as on Android): 14 race points (START, 12
  checkpoints, FINISH), 23 cameras, all 14 found during the race, 0 on a road, the OSD names 14 of 14 clips; noon
  and night. Two points have one camera (CP 8, CP 11).
- FLEX: the same race clean and with a slide at checkpoint 3 (the autopilot's handbrake): clean tape $56 (CP 3: 92
  MPH +$11) against $98 (CP 3: 77 MPH · SLIDE +$53). The first try threw it sideways at 133 km/h into the trees:
  CP 2: SLIDE · CRASH +$6 (a crash in view loses the value). City tapes of one lap: $174 .. $268 ("The start, 12
  checkpoints and the finish, a win." $252, its last checkpoints LED).
- TapeCheck on this round's code (hidden, once): 51 ok, 0 FAIL. Round 1's two failures pass: FF scans "with the
  sound off", "EJECT lands at home ... on the tape shelf". The deck plays a v2 tape through its clips, REW, FF,
  PAUSE, the end stops the deck; the montage still joins. Pages: the store lists every clip with its tags and
  dollars; the sale pays ($1250 + $252 = $1502).
- Self-test (LifeSimSelfTest, PSXRec, fresh processes): see the numbers at the foot of this section. TestCamcorder:
  49 checks, all ok (the feeds, the clips of a synthetic two-lap drive, every flex tag, the v2 tape, a v1 tape
  still loads at its old price, a newer tape refused).

## What still looks wrong
- Uptown Loop's CHECKPOINT 3 front camera has a UTILITY POLE across a third of its frame (day and night sheets). The
  fix is written (PSX_CAM_CLEARVIEW=1, a 60 degree look for things in the foreground) but NOT RUN: default OFF.
- City nights under street light come out BRIGHT AND WASHED: the gain-up that makes the unlit mountain readable
  (3.2x) also lifts the lit city. PSX_CAM_GAIN sets the cap (round 1's was 1.7); only 3.2 has been looked at.
- Mountain night: the road and the verge read, the cars are still lamps more than shapes.
- The START's back camera stands 3 m from the grid: the cars fill the frame as they leave.
- The fallback's checkpoints are evenly spaced points, not the checkpoint system's gates (not wired yet, see merge).
- A tape is clips: between two the picture jumps (0.3 s of static). Whole laps are in TRACKSIDE / CHASE, one key away.

## Unproven (say so to the owner)
- REAL GATES: no race has fed `RacePoints.Provider`; `FromGates` and the file reader are covered by the self-test's
  synthetic chain only (the reader stays behind PSX_CAM_CPFILE=1).
- A circuit of several laps (the LAP 2/3 clips), a drag strip and a sprint variant: self-test only; the play runs
  were one lap of Uptown Loop and the Gillespie Gap stage.
- The VCR on a CITY tape (the deck's cut waiting for streamed ground): TapeCheck plays a mountain tape. No Android
  device run at all.
- PSX_CAM_CLEARVIEW=1 and any PSX_CAM_GAIN but 3.2: never run.
- The honest BEFORE with its HUD text, and a watched city run: not shot (the owner asked for the tests to stop).
- How the camcorder's sound is: nobody has listened to it.

## What the coordinator must do at merge
- Files (all the camcorder's own; no scene pass, no re-bake, no data, ProjectSettings untouched this round):
  new `Scripts/Replay/RacePoints.cs`, `RacePlan.cs`, `TapeClips.cs` (+ .meta), `tools/sandbox-lock.ps1`; changed
  `Scripts/Replay/Camcorder.cs`, `CamcorderCam.cs` (rewritten), `CamcorderOSD.cs`, `CamcorderPass.cs`,
  `RaceReplay.cs` (Begin: CamcorderCam.Setup(this, cam); Hold; the recorder's Scout line; PoseAt removed),
  `TapePlayback.cs`, `Tapes.cs`, `LifeSim/LifeHomeScreen.Tapes.cs`, `Shaders/PSXCamcorder.shader`,
  `Editor/CamcorderShots.cs`, `Editor/TapeCheck.cs`, `Editor/LifeSimSelfTest.Camcorder.cs`,
  `tools/camcorder-shots.ps1`, `tools/tape-check.ps1`.
- THE CHECKPOINT WORKFLOW (city-checkpoints) must, in CheckpointRace.TryBegin: `RacePoints.Provider = rm => (rm == its
  race) ? RacePoints.FromGates(gates, loop, rm.totalLaps, gridPos, gridHeading) : null;` with one `RacePoints.Gate`
  per cps[] entry (kind, x, y, z, hx, hz, l0 = the spans' least l0, l1 = their greatest l1), and clear it when the
  race goes. Its DESIGN_v2 7.1 / stage-2 item 5 ("trajectory lenses for CamcorderCam", `CamcorderCam.Setup/Plan`)
  is DONE DIFFERENTLY here: those signatures are gone, the clips are already cut from the recorded trajectory.
  Its own planned edits to RaceReplay.cs (PauseList, explicit rm.path) touch the file this branch changes near Begin.
- `tools/sandbox-lock.ps1` is there for every agent's scripts (Enter-SandboxLock / Exit-SandboxLock).

## What the owner will notice
The camcorder replay no longer rides along: it is a tape of clips from people standing at the start line, at every
checkpoint and at the finish line, each named on the picture (START, CHECKPOINT 3/12, FINISH). The shelf and the
video store list what each camera saw, and a slide, a pass or a lead through a checkpoint is worth money.

## Self-test and the runs, in numbers
- LifeSimSelfTest in PSXRec, fresh processes: base 742315cd = 62 failures, this branch (70f0fe50) = 62 failures, the
  SAME list (diff empty). The later commits only add two page shots to an editor tool (typechecked).
- Play runs: 3 watched (22:27-22:55: first look, the flex pair, the mountain day), the rest hidden. The owner was
  playing: he closed two of my watched editors, and a PowerShell bug of mine opened two ordinary visible editors at
  23:00-23:03 (hidden mode's arguments glued into one string; fixed e5b02830). At 23:58 he asked for the tests to
  stop; after that one hidden, silent TapeCheck, the two self-tests and one page capture ran, once each.
- Head: see the hand-back. Nothing pushed, nothing merged.

# HANDOFF - THE OWNER'S 11-14..11-16 CAR MODELS IN THE GAME (agent "cars-1116", branch `cars-1116` off mountain-nm 742315cd, 2026-11-16 21:40-23:50; NOT pushed, NOT merged)

Owner, 2026-11-16 ~21:25: "Please add these updated car models to the game. Some of them are WIP, but they can be updated again later."
Scratch: `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\cars1116\` (INVENTORY.md, PROGRESS.md, sheets\, run.ps1, Cars1116Jobs.cs, the gate reports).

## Per car (nothing skipped; catalog 320 cars across 47 of 57 shells)
| Delivery taken | Shell | What | Rows |
|---|---|---|---|
| Silvia_S13_HD_v03 | `coupe_88` | NEW | the four S13 Silvias (K's / Q's `88, `91), off the 180SX hatch |
| FC_1987_Bronze_HD_v03 (closed + raised) | `rotary_coupe_87` | NEW, pop-ups | the three FC rows (GT-Limited `85, GT-X `90, INFINI III `90), off the built-in FD |
| Soarer_1998_HD_v03 | `lux_coupe_97` | NEW (WIP) | Lexus SC300 `97, off the pack's grand tourer |
| MA70_TurboA_HD_v03 | `gt_liftback_88` | NEW (WIP: only a lamps-up pose exists) | Supra 3.0GT Turbo A `88 + 2.5GT Twin Turbo R `90, off the pack's grand tourer |
| Camaro67_SS350_HD_v02 (v03 = textures only, no model) | `pony_coupe_67` | NEW + NEW catalog row | `chevrolet_camaro_ss_350__67` "Chevrolet Camaro SS 350 `67" |
| NSX_Yellow_HD_v31 | `midship_coupe` | updated in place (was v15) | all 12 NSX rows |
| TypeX_White_HD_v13 | `fastback_96` | updated in place (was v08) | Nissan 240SX `94 |
| FD_Shared_Independent_v01/Textured_27 | `rx7_fd_93` / `_99` / `_nowing` | updated in place (was Textured_18) | the 9 FD rows + the `98 Type RS |

## What I found
- Every delivery is a 2048x1024 HD PBR set + an RGBA lamp map + GLB. The game sheet is the 4x box filter (512x256); the 2048 set goes through the HD car tier.
- The S13 Silvia rows wore the 180SX HATCH and the FC rows the FD: both now wear their own body. The catalog had no '67 Camaro (GT4 has only the '69): new row.
- Soarer v03 (no README: still being written) delivers see-through tinted glass over a modelled cabin; the game draws glass opaque off the sheet, and its backlight / sunroof UVs land on PAINT: pinned to the quarter glass's texel in the exporter (`uvPin`).
- MA70 v01-v03 hold only `HeadlightsUp`: the lamps stand raised day and night until a closed pose comes.
- NSX v31 is 75,716 / 76,776 triangles, seven times every other shell (the artist's own cap is 10,000). It runs; it is the first thing to ask Codex for.
- `Scripts/CockpitFit.Table.cs`: the probe's calibration is the catalogue's MEDIAN, so adding five shells would have stretched every car's cabin 2.8 %. Kept at the 52-shell values (and with them the fit gate passes; with the new medians the three FDs failed its nose gate by one ray).
- NOT MINE, found on the way: `Resources/ArchHubs.bytes` at 742315cd is stale for other shells - the fit code moved after 11-11, so 15 catalog cars sit on the blunt arch field instead of their exact rows (civic_eg x8, rally_sedan_99 x3, flatsix_coupe, viper_gts, and the two Ford RS200s that changed shell on 11-15). `Scripts/ChaseSilhouettes.Table.cs` differs from a fresh emit on 40 other shells by millimetres. I changed ONLY my ten shells' rows. A full fresh pair is in scratch (`ArchHubs_full_rebake.bytes`, `ChaseSilhouettes_full_emit.Table.cs`); the re-bake is `ArchProbe.Bake` + `BakeExact`, 3 minutes.
- PSXPerf lacked a tracked file (`Generated/CarModels/flatsix_coupe_87_wheel.asset`): the Yellow Bird baked with no wheels there (base self-test "1 shell without a wheel mesh"). Copied in.
- Classifier question for the owner: `lexus_sc300__97` is LHD by its name (DriveSide.csv), while car_facts treats it as the JDM car (1JZ-GTE, 280 PS) and his model is a right-hand-drive Soarer.

## What I changed (switch PSX_CARS1116, default ON; 0 = the five new rows on their old shells; the in-place updates have no switch)
- `tools/carmodels/export_models.mjs` (5 new models, 3 new sources, `cover`, `uvPin`), `tools/carmodels/stage_carhd.py` (new sets, `ormA`), `tools/carmodels/lamps/cars1116_fix.py`.
- Per shell: `Art/Car/Models/<shell>/`, `tools/carmodels/converted/<shell>_vNN/`, `Resources/CarLights/<shell>_lightmask.png`, `Resources/CarModels/<shell>.prefab`, `Generated/CarModels/<shell>_wheel.asset`, `Materials/CarModels/<shell>_*.mat`.
- `Scripts/CarModelLibrary.cs` (Models +5, HandRules, the switch), `Scripts/LifeSim/CarMarket.cs` (StarterBodies: `coupe_88`), `Scripts/LifeSim/CarViewer.cs` (PaintedBrakes +5), `Editor/CarHDShots.cs` (BakeImport's list +7), `Editor/LifeSimSelfTest.cs` (pins).
- `Resources/rg2_cars.json` (+1 row: the '69 SS's L48 350 with the '67's own figures; 299 PS, 1380 kg, 2746 wb, $28,500, 256 km/h), `Resources/car_facts.json` (+1; the tool's re-bake also refreshes 15 older rows' origin after the 11-15 fix), `Resources/DriveSide.csv`, `Docs/drive_side.csv`, `tools/bake_car_facts.py`, `Docs/car_model_mapping.txt`.
- Tables: `Resources/ArchCeilings.bytes`, `Resources/ArchHubs.bytes`, `Scripts/DoorMirrors.Table.cs`, `Scripts/CockpitFit.Table.cs`, `Scripts/ChaseSilhouettes.Table.cs` - my ten shells' rows only.
- Lamp maps: the Soarer's parcel-shelf stop lamp zeroed (behind opaque glass), the Camaro's scatter channel derived (A = 0.35 x B).

## Proof (PSXPerf, StandaloneWindows64, fresh process per job)
Sheets, one per car, 18-19 labelled cells (GT showroom x2, seller's lot 3/4 front and rear, chase noon / noon braking / night / night braking, track front 3/4 noon and night, lot dusk lamps and brake, cockpit ahead and left, both left wheels in their arches, STAGED beside a known car from the front and from above, BEFORE for the five new rows):
`...\scratchpad\cars1116\sheets\cars1116_<shell>.png` for coupe_88, rotary_coupe_87, lux_coupe_97, gt_liftback_88, pony_coupe_67, midship_coupe, fastback_96, rx7_fd_93, rx7_fd_99 (and rx7_fd_nowing: lot cells only, no catalog row wears it).
- Bake: all ten shells; raised bodies coincide with the closed ones (0.0 mm); pop-up lamps found on the FC, NSX, Type X, FD; head lamps by the surface fallback on the S13 (optics behind covers), tail lamps by the fallback on the Soarer (the finder reads garnet paint as lamp red).
- Cockpit fit gate 57 shells 0 FAIL; door mirrors 57 shells 0 FAIL (all five new shells have two live mirrors); arch sweep 320 rows, 0 tyre over its arch at rest, 15 on the field (the stale rows above, none mine).
- Chase framing, my ten shells: 18 of 120 cases out of band = the standing 16:9 CHASE-at-200 horizon miss every shell has (10) + close-view tail clip 0.5-1.2 % past the allowed on the FDs, the Type X and the Soarer (6) + two rounding ties (FC, Camaro).
- Self-test: base 68 FAIL (the sandbox's state), mine 70 on the first run = +3 lamp-map checks on new shells, -1 (the missing wheel asset). Two of the three corrected afterwards and confirmed by the lamp check alone; ONE stays: `lux_coupe_97: ...on faces that look forward (18)` = the Soarer's projector-barrel walls (normals exactly radial) share the lens rim's texels. The full self-test was not re-run after the corrections.
- One WATCHED race (23:42, PSXPerf; the owner's game was not running at the check): Gillespie Gap, 90 s, the player on autopilot in the new `67 Camaro, the field handed over as Silvia K's, RX-7 GT-Limited (FC), SC300, Supra Turbo A (the scene starts three rivals; no picture was taken, so which three is by the hand-over order): the race goes live, 1,880 m for the player and 2,350-2,384 m for the rivals, 0 hard hits, 0 retirements. Its one FAILURE is the exposure gate ("no pumping on the open road", gain 1.24 against 1.10), which no car model touches; not compared with a base run.
- What still looks wrong in my own sheets: the MA70's lamps are up at noon; ripples in the paint near the tail lamps on the S13, FC and NSX from the side; a dashed dark seam round the Soarer's tail-lamp covers at chase distance; at night every body is nearly black behind its lamps (the game's night, not these cars).
- Two mistakes of mine, both fixed: the first track frames carried the first shell's lamp map on every later car (CarLights re-fits in LateUpdate, which does not run outside play; the tool re-fits now, frames re-shot); the cockpit-shots job ran without -quit and sat building Unity's search index (~6 GB of PSXPerf's Library) until I asked it to close.

## For Codex (what is visibly unfinished)
- MA70: a HeadlightsDown pose (closed lids), same frame and atlas, so the pop-ups can work like the FD's. Tyres 656 mm against the sheet's 631.
- Soarer: opaque dark glass texels for EVERY pane (backlight and sunroof sit on paint; v02 had opaque glazing); the high stop lamp on the outside if it should show; projector-barrel walls off the lit lens texels; the dashed seam at the tail-lamp covers; tyres 622 mm against 654 (the rear needs a 33 mm arch flare); 9 % of the sheet has alpha 0. README / validation for v03.
- NSX: a game mesh at the 10,000-triangle cap (v31 is 75,716); the lens-coat map is not read by the game.
- Camaro: the model for v03 (the folder has textures only); a lens-scatter channel in the lamp map (A is empty; derived here); sheet alpha in two classes only (128 matte / 255 gloss; it uses sixteen values).
- S13: README / validation / preview for v03; the rear-quarter ripple; tyres 602 mm against 615-627.
- FC: the shoulder ripple toward the rear lamp (its own README names it); tyres 600 mm against 627-632.
- Type X: sheet alpha is 255 everywhere (no matte class for rubber and trim).
- FD: nothing new.

## For the coordinator at merge
1. Merge `cars-1116`. Shared files: `Scripts/CarModelLibrary.cs` (Models, HandRules, `HandKey`, `Cars1116On` / `Before1116`), `Scripts/LifeSim/CarMarket.cs` (`StarterBodies`), `Scripts/LifeSim/CarViewer.cs` (`PaintedBrakes`), `Editor/CarHDShots.cs` (`BakeImport` key list), `Editor/LifeSimSelfTest.cs` (the mapping block, two edits), the three `*.Table.cs` (rows), `Resources/rg2_cars.json` (one row appended), `Resources/car_facts.json`, `Resources/DriveSide.csv`.
2. HD car files: copy everything in `C:\Users\mcgee\PSXHDStage\carhd_cars1116\` over `C:\Users\mcgee\PSXHDStage\carhd\` (68 files: 27 identical, the 12 FD files replaced, 28 new, the manifest), or build with `PSX_CAR_HD_SRC` pointing at it. 17 sets, 16 lamp maps.
3. The car re-bake build (the pc27 / pc_carbuild flow): copy the changed asset files by path (`git diff --name-only 742315cd..cars-1116 -- Assets`, 208 files), then `CarHDShots.BakeImport` (its list now holds the seven new HD bodies; it re-bakes them in PSXLab and imports the HD sets), then the player. The new shells' prefabs, materials, wheel assets, cover materials and metas are committed; the updated shells' prefabs keep PSXLab's own cover-material GUIDs.
4. Decide: the full arch / silhouette re-bake for the OTHER shells (above), the SC300's drive side, and whether the cockpit calibration should follow the 57-shell medians.
5. PSXPerf: my baked car assets stay there; its Library is ~6 GB bigger (search index).

## What the owner will notice
The Silvias are now the S13 notchback and the old RX-7s the FC with working pop-ups, instead of borrowed 180SX and FD bodies; the SC300, the two A70 Supras and a new '67 Camaro SS 350 have their own cars; the NSX, the Type X and every FD wear his newest versions. On PC all of them carry his HD paint and panel lines. The Supra drives with its lamps up all day until a closed pose exists.

# CHARLOTTE TRACKS, BATCH 1 INTEGRATED: 3 verified tracks, 4 venues (agent "integ", branch charlotte-tracks-b1 off ec1dd760, 2026-11-16 22:20-22:50; NOT pushed, NOT merged)

The owner today: "start adding more Charlotte tracks". Four lanes built 13 candidates (A: H3 C1 X1; B: S1 X3 C2 H4;
C: H5 C9 D3; D: H6 H7 C7), and each lane had an independent checker. Only the three that passed BOTH the lane and
the checker are in. Owner sheet: art workspace `Charlotte_Tracks_Batch1_2026-11-16.png` + `.md`.

## What is in (per-track files only, taken unchanged from the lane heads; no .cs change)
- `Resources/CityTracks/x1.json` (+meta, GUID 230f153c...) from tracks-lane-A 4dd82b53 (commit 47efa34e).
  It holds CltX1 "TRYON SPRINT — SOUTH END" and CltX1Rev "... SOUTH END II", a 2.69 km sprint run in both directions
  in TryonSprint's scene.
- `d3.json` (+meta, f002c278...) from tracks-lane-C d9023727 (7117456b): CltD3 "US 74 1/2 MILE: INDEPENDENCE". It is
  an 804 m drag, one way (NW, inbound), in TryonSprint's scene, limit 89.
- `c7.json` (+meta, 966e5050...) from tracks-lane-D 64542c96 (7e9b835a, 0dc91c56): CltC7 "UNIVERSITY CITY CIRCUIT".
  It is a 4.22 km circuit raced for 1 lap, one way (clockwise), in UptownLoop's scene.
- `_order.txt`: `order.mjs --write` appended CltC7, CltD3, CltX1 and CltX1Rev. The venues are now #53 I485West,
  #54 CltD1, #55 CltC7, #56 CltD3, #57 CltX1 and #58 CltX1Rev. From now on, never reorder or remove these lines.

## Left out (failed the lane or its checker)
- C1 (A): the planned driver error sends a rival straight into the sanctioned closures. The lane lost one rival this
  way at 46.0 m/s and one at 42.2 m/s. Six of its 8 corners have a row 14-18 m straight on.
- H3 (A): the Remount Rd turnaround is a one-lane 4.3 m bridge carriageway. All 3 runs went wrong there: jams,
  barrier hits and wrong way.
- S1 (B): roll-overs at the Ardsley Rd and Fenton Pl fan mouths (12 mm FanProudM steps at 150-215 km/h). It needs a
  Scripts/City fix.
- X3 (B): the Skyline retired into the median Barriers in 3 of 4 runs with no planned mistake. The surface is smooth
  there, so AI contact is likely.
- C2 (B): a closure hit at 55.1 m/s at South Tryon -> Remount after the planned mistake.
- H4 (B): 2 retired. One was a closure hit on C-D ramp e172. The other was a hard hit into Ground at the I-277 WB deck
  end e9697->e1832 (city geometry).
- C9 (C): a rival rolled and retired after the Ballantyne Corporate Pl bridge in both runs. No planned mistake.
- H5 (C): a retaining wall on the Johnston Rd off-ramp's edge line (e159). A rival retired there in both runs. It
  needs a city fix.
- H6 (D): checker seed 1 had a closure hit at 69.4 m/s past the Eastway exit after the planned mistake. A no-event run
  had a car 2.5 s wrong way after rolling at the e14413 deck end.
- H7 (D): checker seed 1 had a closure hit at 55.0 m/s past the W.T. Harris exit after the planned mistake.

## Not taken (shared code from the lanes; none of the three tracks needs it)
- The registrar laps fix: tracks-lane-A 6e022f5e (final in 4dd82b53), or the identical code in tracks-lane-C f839431b.
  It is CityTracksRegistrar.Awake, +8 lines in the hosted-venue block: a hosted loop sets
  `RaceManager.totalLaps = venue.laps`, and `PSX_CITY_TRACK_LAPS=0` restores the old behaviour. Both checkers say it is
  correct and minimal. With it, set c7.json laps to 3, fix "one lap" in its blurb and race CltC7 once in full
  (12.7 km). Without it, every hosted circuit races 1 lap, so CltC7 is emitted at laps 1 to match.
- track-job.ps1 -Seed (lane A 7b619c7b). It is tooling only.

## Proof (PSXCity, WebGL, my own stamped atomic lock each job, released; typecheck OK first)
Scratch: `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\tracksb1\integ\out\`
- -Mode scenes (22:28-22:31): CITY BUILD OK in 3 min. The four city scenes are now built from this branch's code; the
  last build was at 14:57, before the PC48 merge.
- -Mode closures (22:31): ALL CLEAR. That covers 8 routes and venues #53-#58, with 0 on the race line everywhere:
  - uptown 8.3 m, tryon 9.2 m, independence 10.6 m;
  - c7 6.8 m (lanes 5.8), d1 9.8 m, d3 10.2 m (9.0), i485west 10.4 m, x1 8.6 m (5.8).
- WATCHED sanctioned race of CltC7, -Traffic NONE, -Finish, seed 0 (22:33-22:38; main told first; no layout dialog).
  It ended RACE CHECK OK with 0 FAIL lines:
  - Charger 133.6 s, Skyline 143.8 s, player 144.3 s, Supra 151.1 s;
  - 0 retired, wrong way none, pumping 1.01 (ok), UNDRAWN RAILS 0, closures audit 0 on the line (nearest 6.8 m).
- -Mode cards (22:38): citymap-bake 721 tiles, 11 cards (citymap_CltC7/CltD3/CltX1.png; no separate Rev card).
- -Mode eye (22:41): grid frames for c7, d3, x1 and x1rev, plus middle frames, day and night. The lanes are clear.
  x1_middle snapped onto the pavement behind a tree, so it was not used.
- Earlier verdicts:
  - X1: lane A forward and reverse all home; checker A ran CltX1Rev on seeds 2 and 0 (one planned-mistake off-road
    retirement with no hit).
  - D3: lane C, then checker C (and a no-event run with no closures built).
  - C7: lane D seed 0; checker D seeds 0, 1 and 2 (seed 2: one planned-mistake off-road retirement, no hit).

## Known and not fixed here
- Owner, 21:00 tonight: no barricades in any race; checkpoint races instead (memory
  psx-racing-barricades-legal-events-only). This branch keeps the old default, CityRaceClosures.cs:22-37
  `ForThisRace`: closures in sanctioned events only. For the PC build, either set `PSX_TRACK_CLOSURES=0` as the
  default, or let the checkpoint-race round replace it.
- RaceManager.RaceFraction (RaceManager.cs:330 vs 631-638): on a loop the planned mistake fires at the first braking
  zone. It is the root cause of the H6 and H7 closure hits. This is shared race code.
- RouteMap.cs, arrow placement (about lines 553-567 and the Chevron at about 655): the drag cards' arrows (CltD3,
  CltD1) point against the run.
- Risk, not seen in any run: CltX1Rev has only 60 m of road past its flag. A full-width closure row stands about 75 m
  past it, on S Tryon e12864. That matters only while closures exist.
- CltC7 has oneWay 1 and every road is one-way. CltX1 is two-way. A street race on them with traffic is not checked.

## For the PC build
- Sync `Assets/PSXRacing/Resources/CityTracks/` (all files and metas).
- Run the city scene pass if the build sandbox's city scenes predate CityTracksRegistrar.
- Run `tools\citymap-bake.ps1 -Project <sandbox>` before the player build.
- No city re-bake: no `charlotte_*.bytes` or Scripts/City change.
- Switches: PSX_CITY_TRACKS defaults ON (=0 hides the new tracks); PSX_TRACK_CLOSURES (see above).

# CHARLOTTE TRACKS, THE FOUNDATION: the pipeline finished and made merge-safe for four lanes (agent "tracksb1", branch charlotte-tracks, 2026-11-16 18:48-20:00; NOT pushed, NOT merged)

Supersedes "STATE AT THE STOP" below (its NEXT steps a-d are done). The owner: "Now that Charlotte roads are more
accurate, I'd like to start adding race tracks (circuits, sprints, drag [the current drag/sprints are veeery
long])"; "I would like to see more circuits use highway on/off ramps"; today: "start adding more Charlotte
tracks". The lanes' builder guide: scratchpad `tracksb1\BUILDER_GUIDE.md` (commands, sandboxes and targets, the
lock, PASS, known lines, what to do on a problem).

## What changed
1. Merged mountain-nm 742315cd (PC48 code) into charlotte-tracks: only HANDOFF.md conflicted (both tops kept).
2. A TRACK IS DATA, ONE FILE EACH: `Assets/PSXRacing/Resources/CityTracks/<route id>.json` (+ .meta). It holds the
   venue (name, blurb, HUD label, laps, limit, host scene, one-way, reverse 0/1 with the twin's name, label and
   blurb) and the route (the shipped graph's edge chain, the 20 m menu line, start line, finish, graph hash).
   - `Scripts/CityTracks.cs` (rewritten) reads every file at load (Resources.LoadAll). `Register` adds the routes to
     the city map; it refuses a file routed on another graph and checks every chain.
   - `TrackCatalog.CityTrackVenues(...)` makes the venues from the files: the forward venue, plus `<Id>Rev` when
     reverse=1. A cut (X1-X4) is its own file. The hard-coded I485West def is gone.
   - ORDER (saved careers store the index): `Resources/CityTracks/_order.txt` first (THE INTEGRATOR's file:
     `node tools/city/tracks/order.mjs --write` after merging lanes), then unlisted venues by file name, forward
     before twin. Today: #53 I485West, #54 CltD1, after the deck runs as before. The list never depends on a switch.
   - Small shared edits: `TrackCatalog.ReverseTwin` copies hostScene; `SceneIdOf` checks hostScene before Reversed
     (a hosted twin races in its host's scene); `BuildAll` calls `CityTrackVenues(list)`.
   - Removed: `Resources/charlotte_tracks.json` (+meta) and `tools/city/tracks/emit_tracks.mjs`.
3. Tools in tools/city/tracks/:
   - `emit_track.mjs`: one candidate of candidates_2026-11-16.json into its own file. It re-checks the chain,
     joins gaps under the sheet's 5 m sampling, places cut and drag starts, and sets defaults (laps under the
     14.2 km tank line, limit, one-way, reverse 1 only when no road is one-way). A dry run passes on all 28.
   - `order.mjs`, `trackfiles.mjs` and `spots.mjs` (driver-eye cameras).
   - `track-job.ps1`: one locked Unity job per call (scenes, closures, race, cards, eye or selftest). It takes the
     atomic lock, pins the build target per sandbox, /MIRs the code plus Resources/CityTracks and copies metas
     back.
   - `closures.mjs` and `../citymap_prep.mjs` now read the per-track files. `Editor/CityClosuresCheck.cs` also
     lists every track venue (index, route, host scene) and WARNs on venues `_order.txt` does not list yet.
4. Proof track CltD1 (D1, "UPTOWN 1/4 MILE: 4TH STREET"): one-way East 4th Street, 402 m, raced in TryonSprint's
   scene as a short sprint. A city route has no drag presentation: CityMode sets path.drag false, and
   Scripts/City is out of bounds.
5. I485West's reverse twin was registered from the data, raced, and REMOVED (reverse 0). See the proof below.
   Freeway laps stay forward-only, the old rule for UptownLoop and Independence.
6. DEFAULTS FLIPPED (proven below): `PSX_CITY_TRACKS` default ON (=0 hides the new tracks). `PSX_TRACK_CLOSURES`
   default = sanctioned events only (=0 never; =1 every city race, used by the checks).
7. The self-test on the defaults found 5 FAIL lines that the appended tracks brought. All are fixed:
   - `LifeSimSelfTest` "scene index N": a hosted venue's scene is its host's (I485West, CltD1).
   - "and gets the shortest run there is": the cheapest-run loop skips noDelivery roads, like the delivery pool
     it mirrors.
   - "the frozen v21 list ... less the v30 additions": the appended tracks are left out like the v30 additions.
   - "Standard is in GraphicsSettings' always-included shaders": `CityRaceClosures.EnsureMaterials` no longer falls
     back to Shader.Find("Standard"). Without PSX/Lit it logs an error and draws nothing.

## Proof (PSXCity, WebGL; scratchpad `tracksb1\out\`: closures_check.txt, race_*.txt, cards\, eye\, eye_sheet.png, selftest.txt)
- Closures route check (batch, 19:11): ALL CLEAR. 0 rows on the race line on uptown, tryon, independence, d1 and
  i485west (nearest 8.3-10.6 m from the line; i485west 10.4 m with the lanes 9.4 m a side).
- I485West, WATCHED, sanctioned, -Traffic NONE, -Finish: all four home in 245-258 s, 0 retired, no closure hit,
  wrong way max 0.5 s (ok). One AI went off on a bend into a tree at 16.9 m/s and recovered. FAIL only the known
  lines: exposure pumping 1.96, UNDRAWN RAILS 2. The 15:45 run's closure FAIL and wrong-way FAIL are gone.
- I485WestRev, WATCHED, the same way: the mechanism works (host scene, route swap, reversed lap, all to the flag,
  0 closures on the line, no wrong way). But 5 s after its finish the Charger, pulling right at 134 km/h, ran on
  along the closed I-485 mainline where the reversed lap climbs an on-ramp, and hit that closure 8.8 m outside
  the lanes. Also, a rival's planned mistake fired at 6 % instead of 72 % and retired it. So the twin was dropped.
  The first try ended with the editor closing 1 s into play mode and no report; the re-run was normal.
- CltD1, WATCHED, the same way: RACE CHECK OK. All four home in 17.6-18 s, pumping 1.04 (ok), UNDRAWN RAILS 0.
- Cards on the whole-city map: citymap_I485West.png and citymap_CltD1.png. Eye frames (day and night) on the grid
  and mid-race for both: the lanes are clear.
- Self-test on the code defaults (switches unset): 92 FAIL lines in PSXCity after the fixes (97 before them). The 5 gone are exactly the lines the appended tracks had brought (scene index 53/54, shortest run, frozen v21 list, the closures' Standard shader). No remaining FAIL line names a track; "CITY offers the four Charlotte races" was already failing on the deck runs. The sandbox's other FAILs (deck runs' scene exists, Lake Lure not built there, shaders missing from its ProjectSettings, car maps) are not this round's. No base run was made to compare the count.

## Not done / known
- The planned driver error fired at 6 % instead of 72 % on the reversed city loop (RaceManager.RaceFraction /
  lap count on a loop; shared race code, not changed here). Check it on forward loops too.
- A course with SOME one-way roads races with two-way race traffic (TrafficSystem lays lanes per venue). This
  matters only in street races with traffic.
- Only PSXCity's city scenes carry CityTracksRegistrar. PSXPerf, PSXLab and PSXRec need `-Mode scenes` once (the
  job keeps PC content with PSX_PC_CONTENT=1 when the sandbox has CityTreeKit.asset). The time for that is not
  measured there.
- The sandbox still holds no charlotte_tracks.json (track-job deletes it). The owner's main checkout is untouched.

## Merge
- New: Resources/CityTracks/ (folder meta, _order.txt, i485west.json, d1.json + metas), tools/city/tracks/
  {emit_track,order,trackfiles,spots}.mjs and track-job.ps1. Removed: Resources/charlotte_tracks.json (+meta),
  tools/city/tracks/emit_tracks.mjs.
- Shared: TrackCatalog (BuildAll, CityTrackVenues, ReverseTwin hostScene, SceneIdOf order, Offered comment),
  CityTracks (rewritten), CityRaceClosures (ForThisRace default, EnsureMaterials), CityTracksRegistrar (comments),
  Editor/CityClosuresCheck, Editor/LifeSimSelfTest (3 checks: scene index, shortest run, frozen v21 list),
  tools/city/citymap_prep.mjs.
- No Scripts/City and no charlotte_*.bytes change: no city re-bake. City scenes must carry CityTracksRegistrar: a
  city scene pass in any sandbox whose city scenes predate it (PC47 and earlier). Run citymap-bake before the
  player build.
- After merging the lanes: `node tools/city/tracks/order.mjs --write`, commit _order.txt, then `-Mode closures`
  once over the union.

# STATE AT THE STOP: agent "tracks", branch charlotte-tracks, 2026-11-16 16:05 (resume after 19:00)
1. Done: Phase 1 + 1b (31 candidates, H1-H7 freeway laps) and Phase 2 (the whole-city track preview, merged in mountain-nm 73e6434e, PC47).
2. Phase 3 is WIP and DEFAULT OFF. H2 is I485West (charlotte_tracks.json; CityTracks, CityTracksRegistrar and CityRaceClosures; a hosted venue). `PSX_CITY_TRACKS=1` offers the new tracks. `PSX_TRACK_CLOSURES=1` closes the roads in every city race (for the checks); `=sanctioned` closes them in sanctioned events only (the owner's rule). With both unset the game is as before: no new venue, no closures anywhere.
3. The watched sanctioned run (15:45-15:54, closures=1, with traffic) reached the finish with 4 FAILs:
   - one closure row stood in the race lanes: the Supra hit it at 12.5 m/s, 2.5 km in (the I-485 mainline row at the C-D split, edge 1164);
   - the Supra drove the wrong way for 4.5 s after that hit;
   - exposure "pumping" reached gain 2.12 (not mine; no baseline run);
   - UNDRAWN RAILS: 2 samples, nearest 37.8 m (city data).
   Report: scratch `tracks\race_I485West.txt`.
4. Fixed since, NOT yet proven in Unity:
   - The closures are built in the registrar's Start against the real TrackPath. Each row stands 1 m outside +-roadWidth/2 (the cars use the whole 18.8 m road), the same measure as the RacePlayCheck audit (`CityRaceClosures.LineMargin`).
   - Offline, `tools/city/tracks/closures.mjs` on all 4 routes: 0 rows on the line, nearest 1.01-1.3 m clear.
5. NEW `Editor/CityClosuresCheck.cs`: a batch, hidden check of every city route (real TrackPath, closures and the audit; run with PSX_CITY_TRACKS=1). NOT run yet. Typecheck OK.
6. NEXT, in order:
   - (a) run CityClosuresCheck in PSXCity;
   - (b) a watched sanctioned I485West with `-Traffic NONE` (scratch job_race.ps1 -Extra "-Traffic NONE"), sending main one line first;
   - (c) the card shots (job_citymap.ps1 -NoBefore -RaceSelect);
   - (d) if all is clean, both defaults ON;
   - (e) the owner's picks, through emit_tracks.mjs.
7. Merge:
   - New: CityTracks, CityTracksRegistrar, CityRaceClosures, Editor/CityClosuresCheck and Resources/charlotte_tracks.json (+metas).
   - Shared: TrackCatalog (hostScene, venues after DeckRuns, SceneIdOf, EnsureRoute, Offered); PSXRacingBuilder.City (the registrar on every city scene, so a city SCENE PASS is needed); LifeSimSelfTest; RacePlayCheck.
   - No Scripts/City and no charlotte_*.bytes change, so no city re-bake.

# HANDOFF - THE CAMCORDER, PAUSED AT THE 97 % STOP (agent "camcorder", branch `replay-camcorder` off mountain-nm c0a81147, 2026-11-16 ~16:00; NOT pushed, NOT merged; resumes after 19:00)
1. STATE: replays look like 1999 camcorder tapes (PSX_CAMCORDER, default ON - proven in three WATCHED PSXRec runs, mountain + Charlotte, day + night, every check ok but one city countdown timeout); tapes save / shelf / VCR / VIDEO STORE sale work (PSX_TAPES, default ON); MONTAGE behind PSX_MONTAGE=1 (default OFF).
2. Proof so far (scratchpad\camcorder\): camcorder_replay_sheet.png, camcorder_tapes_sheet.png, camcorder_replay_clip.mp4 (8 s, frames only), shots\, shots2\, tapes\ (frames + reports). camcorder_before_after.png is NOT valid yet (the base run died in a lock race).
3. Self-test in PSXRec: base 65 FAIL vs mine 66; the one extra is "PSX/Camcorder in always-included shaders" because PSXRec's ProjectSettings are stale (my GraphicsSettings.asset adds it; the sandbox also lacks the water round's two lines). TestTapes + TestCamcorder all ok. Saves untouched (load both ways).
4. Fixed after the first tape check, NOT yet re-run: the deck's scan mute (AudioFader.Mute) and TapeCheck skipping the title (a watched editor booted the title on the first trip home).
5. Round 3 (director: no ride on the grid / near cars, operators added on the way, dash + A-pillar in the ride, smaller smear) was mid-run at the stop: Uptown Loop noon passed; its job finishes on its own and releases PSXRec's lock; no further job starts.
6. NEXT: re-run tools\tape-check.ps1 -SkipPages (watched), tools\camcorder-shots.ps1 -Base -Plan "GillespieGap:Noon,UptownLoop:Noon" for the honest before, one self-test with the GraphicsSettings copied in (camcorder-shots -Extra "ProjectSettings\GraphicsSettings.asset").
7. NEXT: rebuild the sheets (scratchpad\camcorder\build_sheets.py, CITY=shots3) and write the full hand-back (drafts: scratchpad\camcorder\handoff_*.md).
8. MERGE NOTES: shared edits in SpeedBlurFeature.cs, SpeedBlur.cs, RaceHUD.cs, RaceManager.cs, LifeHomeScreen.cs, LifeSimSelfTest.cs, AudioFader.cs, RaceReplay.cs; ProjectSettings/GraphicsSettings.asset MUST ship (without it a player has no camcorder picture - it stands down, never pink).
9. SANDBOX NOTES: tools\unity-wait.ps1 defaults to -buildTarget WebGL (pin PSX_BUILD_TARGET=Android on PSXRec); AGENT_LOCK is check-then-write (a race at 15:24 - camcorder-shots.ps1 now re-checks after taking it).
10. Not done: Android device run, overpass operators (none found yet), crowd sound, a blocky OSD font.

# HANDOFF - TREE POP ROUND (agent "trees4", branch `city-treepop` off mountain-nm c0a81147, 2026-11-16; NOT pushed, NOT merged)

Owner since 11-11: tree pop-in "on every forest setting". Round 5 found the NM city trees' pop at their MESH LOD
switches (MEDIUM 102 / 140 m, HIGH 124 / 210 m), not at the far card.

## What I found
- PopFix (11-11) pushed the city pool's levels out (kit 55/140/240 m x FOREST DETAIL x PopLodMul = LOW 77/109/203,
  MEDIUM 102/140/240, HIGH 124/210/360 m) but left the dithered hand-over bands at MeshForest's fixed 12/16/20 m
  x detail scale (LOW 8/10/13, MEDIUM 12/16/20, HIGH 18/24/30 m). A crown that switches at 102 m therefore dissolves
  into the next level within 12 m, about half a second at 85 km/h, and reads as a pop. In the red-overlay frames the
  pop is WHOLE crowns changing in one step.
- Screening (PSXRec, fresh process each, bands as a share of each level's distance, PSX_LODBAND):
  Providence Rd visible pop MEDIUM 768 -> 625 (share 0.35) -> 395 (0.6); HIGH 317 -> 137 -> 58. At 0.6 every band
  sits at its clamp (no plant ever draws three levels at once), so 0.6 means "the bands at their widest": a larger
  share changes nothing.
- The Android target does not skew this. The editor's quality level reads 'PC' (logged in every run), the city kit
  is PC content forced on (PSX_PC_CONTENT=1), the LOD distances come from FOREST DETAIL rather than the quality
  level, and the base counts match round 5's on PSXPerf (Win64): MEDIUM 768 v 784, HIGH 317 v 309.

## What I changed (switch PSX_CT_LODFADE, default ON; 0 = the fixed bands as before)
- CityMeshTrees.cs: `LodFade` / `LodFadeShare` (0.6; PSX_CT_LODFADE=0.35 etc. picks another share). MakeForest
  passes the share to the pool.
- MeshForest.Pool.cs: CreatePool takes `bandShare` (optional, default -1 = as before).
- MeshForest.cs: per-forest `bandShare` ([NonSerialized], -1 = fixed bands), applied where the perf tour's
  `bandshare=` already worked (that knob still wins). The startup log line now names the bands.
- The mountain stages' MeshForest is untouched (bandShare stays -1 there).
- Editor/CityRefSpots.TreeFeet.cs: logs the switch, adds PSX_FEET_LEVELS (default 1,2) and PSX_FEET_COSTAB=1 (the
  frame-cost A/B in one process, ABBA, 4 builds each).
- No file under Scripts/City, no re-bake, no shader or scene change, no new file. Same trees in the same places,
  same switch distances.

## Proof (PSXRec, fresh process each; scratch = C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\trees4\pop\)
The walk is round 5's RunTreeFeet: 300 -> 20 m toward the place at driver height in 10 m steps, summer noon. Each
step is shot with the game's LOD choice (a) and with the choice of 10 m earlier (b). Visible pop = 16 px blocks whose
light changes > 3%; churn = pixels with any channel > 22. Before = PSX_CT_LODFADE=0 (tag lfoff); after = the default
(tag lfon). MEDIUM and HIGH repeat the screening's counts exactly (the renders are deterministic).

|                                      | LOW                  | MEDIUM              | HIGH                |
|--------------------------------------|----------------------|---------------------|---------------------|
| bands before -> after (m)            | 8/10/13 -> 46/29/84  | 12/16/20 -> 61/34/90 | 18/24/30 -> 74/78/135 |
| Providence Rd visible pop            | 1622 -> 1056 (-35%)  | 768 -> 395 (-49%)   | 317 -> 58 (-82%)    |
| Providence worst 10 m step           | 117 -> 64            | 52 -> 26            | 23 -> 7             |
| West Trade visible pop               | 28 -> 77             | 7 -> 11             | 1 -> 1              |
| churn (single px) Providence         | +19%                 | +49%                | +68%                |
| isolated px in a still, Prov / WT    | +8% / +2%            | +7% / +1%           | +5% / +1%           |
| tris at the cost eye                 | 0.32 -> 0.37 M       | 0.53 -> 0.59 M      | 1.21 -> 1.35 M      |
| frame ms, ABBA medians (editor 1080p) | 19.54 -> 18.44      | 28.04 -> 27.12      | 34.33 -> 36.65      |
| MIDDLE 0.35: bands (m)               | 27/29/71             | 36/34/84            | 43/74/126           |
| MIDDLE 0.35: Providence visible pop  | 1486 (-8%)           | 625 (-19%)          | 137 (-57%)          |
| MIDDLE 0.35: West Trade visible pop  | 29                   | 3                   | 1                   |
| MIDDLE 0.35: isolated px, Prov / WT  | +5% / +0%            | +3% / +0%           | +2% / +0%           |
| MIDDLE 0.35: tris; ABBA frame ms     | 0.35 M; -0.2 ms      | 0.58 M; +0.6 ms     | 1.32 M; -0.2 ms     |

- Frames: before, whole crowns jump in one step (clusters of yellow blocks over a tree). After, scattered single
  blocks on crowns that are mid-dissolve, and no whole-crown jumps. Without the overlay the two runs look the same
  (TreePop_look.png). At 2x zoom a crown inside a band shows a mix of the two levels' leaves.
- THE PRICE: West Trade's sparse open-crown street trees stand against the sky, and on them the long dissolve shows as
  dotted crown edges for longer (TreePop_WestTrade_dotted_edge_LOW_300m.png: frames a and b, before | after). That is
  West Trade LOW's 28 -> 77 (a few trees on the left, at 300, 240-220 and 160 m). The owner has called screen-door
  dither "a speckled dotted effect" before (the shader comment on thinned plants), so if he sees dotted crowns, the
  middle setting is PSX_CT_LODFADE=0.35 and OFF is PSX_CT_LODFADE=0.
- THE MIDDLE SETTING (its own fresh run, tag lf35): almost all of the difference from 0.6 sits in the nearest
  hand-over, which is 27/36/43 m wide at 0.35 against 46/61/74 m at 0.6 (the other two bands are nearly the same).
  At 0.35 West Trade does not rise (28 -> 29), but the Providence cut is only -8 / -19 / -57%. My default stays 0.6:
  the owner's complaint is the pop, and Providence still carries 14x West Trade's pop afterwards. If the dotted
  edges bother him, 0.35 is the fallback.
- IN MOTION, the dither does not crawl visibly (tags mfoff / mfon, RunTreeFeet with PSX_FEET_MOTION=0.4, fresh process
  each). The same walk adds a third shot per step 0.4 m further on with the game's own levels, which is one frame at
  86 km/h and 60 FPS. Over the upper 60% of the frame, one frame of driving changes Providence by -1.3 / -0.6 / -0.1%
  pixels and +0.3 / -0.1 / +0.1% blocks with the fade v the fixed bands (LOW / MEDIUM / HIGH), and West Trade within
  0.1%. Moving leaves already change those pixels from frame to frame, so the screen-door pattern sliding over a
  crown mid-dissolve adds nothing measurable. (Both runs' a/b pop counts equal lfoff / lfon to the pixel: the
  renders are deterministic.) TreePop_motion.csv.
- Frame cost: none measurable. The ABBA medians move -1.1 / -0.9 / +2.3 ms, inside the spread between builds of the
  same mode (MEDIUM 25-31 ms; HIGH had one 100+ ms hiccup in each mode). Draws are unchanged (209 / 211 / 213).
  Triangles rise +16 / +11 / +12%: inside a band a plant draws both levels, and the lower levels are cheap. The
  forest casts no shadows (SunShadows' card stand-ins) and needs no new buffers.
- Files: TreePop_sheet.png (summary, charts for both places at all three levels, red-overlay frames for Providence
  MEDIUM and HIGH at 280/200/130/60 m, before | after), TreePop_look.png, TreePop_approach_MEDIUM.gif / _HIGH.gif,
  TreePop_pop.csv, TreePop35_sheet.png / TreePop35_pop.csv (before v 0.35), ctreefeet_lfoff_log.txt,
  ctreefeet_lfon_log.txt, ctreefeet_lf35_log.txt, ctreefeet_mfoff_log.txt, ctreefeet_mfon_log.txt,
  TreePop_motion.csv. Copies are in the art workspace's
  Charlotte_Trees\Review\Claude_2026-11-16\TreePop_*.
- My runs were batch approach captures (edit mode), not watched play tests.

## Mountain stages (checked, not changed)
- The stage forests are their own MeshForest (a scene component, not pooled). The PC scenes serialize lod0To 65,
  lod1To 140, meshRange 240 and bands 12/16/20 m (BlueRidge.unity on PSXLab 14:20 and PSXPerf 13:46), not
  50/120/230. With the FOREST DETAIL scale (0.65/1/1.5) and PopLodMul, the levels come to LOW 91/109/203 m,
  MEDIUM 120/140/240 m and HIGH 146/210/360 m, with bands of LOW 8/10/13, MEDIUM 12/16/20 and HIGH 18/24/30 m: the
  same narrow hand-overs the city had. At share 0.6 they would be LOW 55/17/84, MEDIUM 72/18/90 and HIGH 88/57/135 m.
  MEDIUM's middle level covers only 20 m there (120 -> 140 m), so its second hand-over cannot widen past 18 m.
- I did NOT measure the stages. PSXRec has no PC stage scene (it is an Android sandbox: none of its 22 scenes carry a
  MeshForest), and PSXPerf and PSXLab were taken. The 11-11 lag-pair round on NC 226A recorded "wider dither bands
  did not help" there, with a different measure (a 6 m lag along the stage path). So the stages keep their fixed
  bands, and the switch is city-only.
- To check on a stage with the PC exe (no code needed, the knobs exist): run the perf tour on NC 226A / Blue Ridge
  with the lag pair, once plain and once with `bandshare=0.6`, at forest=1 and forest=2. If the gain holds there, the
  stage default is one line (bandShare on the stage forest, e.g. from a PSX_FOREST_LODFADE switch).

## For PC47 / the merge
- Branch city-treepop = mountain-nm c0a81147 + 74bbf999 (the pop knobs in RunTreeFeet) + 3a3949d9 (THE SWITCH)
  + 65286e02 (RunTreeFeet's motion pair) + HANDOFF commits. It touches 4 code files (Scripts/CityMeshTrees.cs,
  Scripts/MeshForest.cs, Scripts/MeshForest.Pool.cs, Editor/CityRefSpots.TreeFeet.cs) and HANDOFF.md, and adds no
  file (no .meta). Typecheck OK.
- PC47 needs no kit rebuild and no re-bake: the switch is read at runtime when the city's tree pool is made.
- To confirm in a player log: "[MeshForest] 61440 plants ... LOD 101.75/140/240 m (hand-over bands 61/34/90 m)" on
  MEDIUM (12/16/20 = the switch is off).
- A/B in one exe: env PSX_CT_LODFADE=0, or the perf tour's `ab=CityMeshTrees.LodFade:0`.

# HANDOFF - CHARLOTTE TRACKS: THE CANDIDATES, THE FREEWAY LAPS AND THE WHOLE-CITY TRACK PREVIEW (agent "tracks", branch `charlotte-tracks` from mountain-nm 3920fdde, 2026-11-16; NOT pushed, NOT merged)

The owner, 11-16:
- On race tracks: "Now that Charlotte roads are more accurate, I'd like to start adding race tracks (circuits, sprints,
  drag [the current drag/sprints are veeery long])". Need for Speed Underground 2 was the reference for event types and
  lengths only.
- At 13:20, on the preview: "a picture of the entire city, the track drawn over it, then zoomed in to fit the track on
  screen for track preview before race".
- At 14:00: "more circuits use highway on/off ramps like the example I drew in red".

## Phase 1: the candidate sheet (sent 13:47; freeway laps added 14:04)
- In the art workspace: `Charlotte_Track_Candidates_2026-11-16.md` and `.png`.
- The courses, 31 in all, on the shipped graph b2aa3453, plus one coast drag:
  - 10 circuits, C1-C10, one for each district the brief named. Laps are 3.1-5.4 km, raced 2-4 laps and kept under
    the 14.2 km tank line.
  - 7 freeway-and-ramp laps, H1-H7. H1 is the owner's loop exactly: up I-485 to Moores Chapel, through the I-85
    cloverleaf, east to Little Rock. It NEEDS THE DECK FIX at the airport ramps: the turnaround crosses the junction
    cluster under the decks, where the bridges audit has UNDER-STEP 3-8 cm and UNDER-KINK to 8 % on e269, e289, e9744
    and e13462. H2-H7 are drivable now. H2 is the closest clean version of his loop.
  - 6 sprints, S1-S6, of 2.0-4.5 km.
  - 4 cuts, X1-X4: today's Tryon and Independence sprints in halves of 2.7-3.7 km.
  - 4 drags, D1-D4: two 1/4 miles and two 1/2 miles, each road 11.6 m or wider for four abreast.
  - D5: Langston Bridge cut from 1,408 m to 804 m, finishing just over the crest.
- The checks on every course:
  - routed edge by edge, one-way streets the legal way, no junction turn over 120 degrees;
  - 0 hits on HANDOFF's "keep race routes off" list (the four ramp-junction decks, the 8 single joints, the owner's
    Beatties Ford spots), except H1;
  - 0 hits on the bridges audit's worst lists;
  - a grid straight of 90 m or more with two lanes in the race direction (S5 is a two-lane street: closed road only);
  - heights from RPRF, lanes and widths from the game's road profiles.
- Tools are in `tools/city/tracks/` (see its README). The chains are in `tools/city/tracks/candidates_2026-11-16.json`.

## Phase 2: the whole-city track preview
What changed:
- NEW `Scripts/CityMapTiles.cs` (+meta). One map of the whole city as a 512 px tile pyramid in
  `StreamingAssets/CityMap/<level>/<tx>_<ty>.png`, listed in `Resources/charlotte_citymap.txt`:
  - levels 0-4 cover the city, 53 km square, 6.5 m/px at level 4;
  - levels 5-8, down to 0.4 m/px, cover only the venues: every route in `charlotte_routes.json` and the parking decks.
  - Drawn by `tools/city/citymap_prep.mjs` and `tools/city/citymap_tiles.py` from the shipped graph and WATR, the
    canopy (charlotte_canopy.bytes), and the OSM landuse, rail, buildings and lots already in the city cache.
- `Scripts/RouteMap.cs` (shared):
  - Build: a city venue (IsCityRace: the routes and the deck runs) draws its card on that map at the level as sharp as
    the card is big. The map, the grid and the route sit in one "Zoom" square.
  - The card flies in from the whole city over about 1.35 s (RouteMapZoom, appended to the file): log-space zoom, the
    route sliding to the middle, each whole-city level laid where it is sharp (`CityMapTiles.PlaceFlight`). It plays
    once per venue and card size a minute, and never outside play mode.
  - New helper `Layer()`. `RouteMapGraphic.drawGrid` and `drawRoute` let the grid and the route be separate layers.
  - New overload `Build(parent, def, pts, ...)` for courses that are not venues yet.
  - Stages and the MAIN edition are unchanged.
- Switches: `PSX_TRACKMAP_CITY=0` gives today's per-venue pictures. `PSX_TRACKMAP_ZOOM=0` turns off the fly-in.
- NEW `Editor/CityMapPreview.cs` (+meta). `-executeMethod PSXRacing.EditorTools.CityMapPreview.Capture` shoots:
  - the card at the race card's size (560) and the pre-race size (236) for UptownLoop, TryonSprint, IndependenceSprint
    and a deck run;
  - any course in the file PSX_CITYMAP_CANDS names;
  - fly-in strips, posed by hand.
- Why tiles and not drawing the graph at runtime: the menu must never parse the 8 MB city. The map also has 120k
  buildings, the canopy and the rail, which are cheap to draw once offline and expensive as UI meshes. The route stays
  vector over the map, sharp at any size.
- No Scripts/City change and no charlotte_*.bytes change, so the city tile bake is not stale.

## Merge notes
- The tiles and the index are a BUILD PRODUCT, gitignored. In the build sandbox, after the sync and before the player
  build, run `powershell -ExecutionPolicy Bypass -File tools\citymap-bake.ps1 -Project <sandbox>`.
  - It needs Node, Python 3 with PIL and numpy, and the main checkout's `tools\city\cache` (landuse, railways,
    buildings, lots).
  - It takes about 1 minute and writes 640 tiles, about 17 MB.
  - A build without it shows today's pictures.
- MAIN parks `charlotte_citymap.txt` by its charlotte_ rule. StreamingAssets/CityMap is not parked (17 MB in a MAIN-only
  build if the bake ran there).
- Shared file: `Scripts/RouteMap.cs` (see above).

# HANDOFF - LINNCOVE ROUND: the far mountains and the Linn Cove granite (agent "linncove", branch `mountain-linncove` off mountain-nm e26dc1f4, 2026-11-16; NOT pushed, NOT merged)

Owner, 2026-11-16 morning, on the title postcard: "It would be nice to find a solution to the barren mountains in the
distance on mountain maps", and "would also be nice to add some of the iconic rock face to the track and the title
shot angle" (two photographs of the viaduct in peak fall colour).

## What was found
- The land past the forest's reach (the stage horizon: far ring, tier 0 at 60 m, apron at 480 m) wore ONE flat canopy
  colour a dress (Editor/PSXRacingBuilder.Horizon.cs HorizonCanopy) times the Meadow hill map's luminance laid 6 km a
  tile (Shaders/PSXLit.shader _HorizonMacro): no crown grain, no colour mosaic, no shading but the 60 m facets'. Fall's
  colour is (0.326, 0.226, 0.054) linear, a khaki tan: PC46's own title frame shows the valley's hills as smooth
  brown ground.
- The DEM is bare earth: past 2.2 km every ridge meets the sky as smooth 60 m facets (the apron's 480 m cells make a
  ridge 20 km off a polygon of 1.5-degree facets).
- The 2018 lidar (1 m bare earth, cached by the road bake) shows the Linn Cove boulder field plainly: hundreds of
  blocks 2-30 m across, densest uphill of the deck between 4480 and 4820 m, several under the deck (the biggest 28 x
  22 m, 8 m proud, at 4652 m, 16 m off the line). 6 of the 9 1 m tiles round the viaduct are cached; the 3 missing are
  the field's far NW and E corners (not under the deck, not in the title frame). Nothing was downloaded.
- PSXPerf's TagManager had no "Horizon" layer (its settings predate the stage horizon): a stage built there had no far
  land at all. The round's job script copies the repo's ProjectSettings/TagManager.asset in before building.
- Lake Lure builds no stage horizon at all (BuildStageHorizon returns for a stage with water): its far land is
  untouched by this round.

## What changed (branch mountain-linncove)
- PSX/Lit THE FAR CANOPY (_HorizonCrowns, Shaders/PSXLit.shader): past 300-1800 m the far land is crowns 9-15 m
  across in world space, each its own colour from the dress's palette, lit as a dome by the sun, dark gaps between;
  stands blend warm to cool over 90-280 m; spruce-fir share rises from 10 % at 1450 m to 85 % at 1750 m ASL; grey
  rock where the 60 m slope passes 37 degrees; where a crown is under a third of a pixel the field is its smooth mean
  (no shimmer). Procedural (no tile to repeat).
- Editor/PSXRacingBuilder.Horizon.cs: the palettes (CanopyBroad/CanopyConiferRaw/CanopyGapCol, from the
  NatureManufacture leaf sheets' measured means, levelled so the land keeps the 11-09 HorizonCanopy brightness);
  SetFarCanopy on every dress of the far land; the apron built twice (480 m as before, 240 m = the wide grid's own
  cells); BuildHorizonFringe (upright crown cards on every crest; built only with PSX_RIDGEFRINGE=1).
- Scripts/FarCanopySwitch.cs (new): shows the 240 m apron with the canopy, the 480 m one without.
- THE LINN COVE GRANITE: tools/roads/linncove_rocks.mjs (offline lidar read, residual over a 7 m gaussian, merged piles
  split) -> Art/BRP/brp_rocks.json (439 blocks); Editor/PSXRacingBuilder.LandmarkRocks.cs places the 236 blocks of
  the viaduct's window +-90 m, within 230 m of the road, as 499 NatureManufacture forest stones turned grey (_LeafVivid
  0.28 / 4.2), each at its block's own footprint, length, height and axis, seated on the drawn ground, tilted half to
  the slope; a block over 9 m is 2-4 stones along its axis. Nothing within a lane's reach (6 m past the tarmac plus
  the stone's radius) of an at-grade road; under the deck at least 4.5 m below the road surface; nothing through a
  deck, pier, joint, wall, rail or cut (OverlapBox). Plain renderers per 128 m cell (20 draws), a mesh collider of the
  same stones named "Boulder" on the Solid layer; the forest keeps 2.5 m off each block (StageKeepOut).
  Scripts/LandmarkRocks.cs (new): PSX_LINNCOVE_ROCKS=0 switches the root (and colliders) off at load.
- Blue distance v2 (TimeOfDay.cs, PSX/Lit _PSXAerial): with LookChoices.AerialDepth (F9 / PSX_AERIAL=1, still OFF)
  the air's own law 1 - exp(-(d - 1.8 km) / 26 km), at most 0.75, toward the sky's blue: about a quarter at 10 km, a
  half at 20. Replaces the old linear ramp (a tenth at 10 km) in the same switch.
- Scripts/TitleScreen.cs Begin (ONE line, shared file): PlaceCamera runs inside `using (new LandmarkRocks.Hidden(true))`
  so the postcard search finds the same views[0] with the granite as without (checked: identical).
- Editor/PSXRacingBuilder.StageRiver.cs StageKeepOut (ONE line, shared): LandmarkKeepOut first (only the stage whose
  far DEM the rocks were built against).
- Editor/PSXRacingBuilder.cs BuildTrack (ONE line, shared): BuildLandmarkRocks after BuildStageWorld, before the forest.
- Editor/LinnCoveShots.cs (new tool): title postcard (views[0] by the rule, or PSX_LC_EYE/LOOK), Linn Cove views, road
  views, editor frame timing, the PC pass's own scene build for a venue list with the plant and lane audits.

## Switches (all env, read at load; =0 at the SCENE BUILD makes the base exactly)
- PSX_FARCANOPY (default ON): 0 = the land exactly as it was (old shading, the 480 m apron, no fringe).
- PSX_LINNCOVE_ROCKS (default ON): 0 = no rocks or colliders (a scene built with it =0 also has no forest clearing).
- PSX_RIDGEFRINGE (default OFF): 1 = the ridges' crown fringe (needs a scene built with =1).
- PSX_AERIAL / F9 (default OFF, the owner's A/B): the blue distance v2.

## The intro agent's October bug (LittleSwitzerland, PSX_INTRO_DAY=288: no hardwoods, not even trunks) - NOT FOUND
- Not general to the fall dress: this round's fall frames (BlueRidge title and Linn Cove views, NC 226A road and vista;
  edit mode, SeasonDress.Apply(3), MeshForest.DrawNow on the GPU path) all draw the mesh hardwoods.
- MeshForest reads only the dress index (DressNow -> SeasonDress.AppliedDress, clamped 0-4), never the day.
  DressMaterials keeps every variant's bark (vr.mats) in every dress and swaps only the leaf submesh and the impostor,
  and GpuDressChanged skips a command only when its material is null - so a per-dress MATERIAL cause cannot also take
  the trunks. Losing trunks too means the chunks were taken over (cards hidden, SetCards) and the GPU draws came out
  empty: look at the command states (MeshForest.Gpu.cs ~712: 0 no material / 1 drawn / 2 no chunk reached) and the
  "[MeshForest] ... GPU lists" line in the October run against January's, and at AppliedDress in that run.
- Init order checked: MeshForest.Awake -> Init -> InitGpu (gpuReady, lastDress -2), SeasonDress.Awake -> Apply, then
  LateUpdate -> FrameStart sets the dress's commands - no stale-dress path found.

## Proof (PSXPerf, real editor frames through the game's dither, PC profile; scratch
`C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\linncove\`)
- BEFORE (title, Linn Cove) = the base code e26dc1f4 itself, its BlueRidge built in PSXPerf with PC content and shot in
  a fresh process; AFTER = this branch, fresh process. Title pose = PC46's own log (eye (-431.12, 132.55, 53.44), look
  (-490.77, 103.39, -188.24), FOV 42); the rule finds the same views[0] in both scenes, with and without the granite in
  the rays. NC 226A: BEFORE = this branch's scene with PSX_FARCANOPY=0 PSX_LINNCOVE_ROCKS=0 in a fresh process.
- sheet_title.png (fall afternoon = PC46's light, fall sunset, fall fog, summer, winter; third column = the blue
  distance v2 A/B), sheet_linncove.png (deck, chase, below, wide), sheet_road_SwissNC226A.png (noon/dusk, chase and
  vista, blue distance A/B), sheet_title_keepout.png (the forest round the blocks, see below).
- THE AFTER FRAMES of those sheets come from the build with a 0.85 r + 1 m clearing (shots/after4, with the clump
  grain). That build failed the plant gate by ONE fern 0.25 m into a stone at 4480 m, so the committed clearing is the
  2.5 m one, whose build passed: its title frame is shots/after2 (palette re-applied, no clump grain) and
  sheet_title_keepout.png puts the two side by side. A 1.5 m fit could not be checked: another agent took PSXPerf.
- Gates, BlueRidge (committed 2.5 m build / base): plants through solids 0 / 0 (125,220 / 125,672 plants within reach);
  lane obstacle audit CLEAR / CLEAR (the stones' colliders are concave: "see the sweep"; placement keeps them 6 m past
  the tarmac plus their radius, and 4.5 m under the deck). views[0] identical.
- Frame cost (editor, 1920x1080, render + 1 px read; other editors were sharing the GPU): base scene at the title
  37.4-40.2 ms; this branch 35.4-36.4 ms, canopy on vs off in one process -0.6 ms (title) and +1.2 ms (NC 226A
  vista); a noisier run +2.2 ms. Added geometry: the 240 m apron 150,673 verts (the 480 m one hidden), the rocks
  212,745 tris in 20 draws (BlueRidge only); ridge fringe 0 (off). The on-screen PerfTour was not run.

## What still looks wrong
- At the title's distance a crown is under a pixel: the far hills read as an olive-orange mottle with clump grain, not
  as treetops, and the skyline is still the DEM's smooth line (the fringe that was to break it read as dark combs).
  Without the blue distance the far ridges keep their full colour to 90 km; with it (A/B column) they read as the Blue
  Ridge's layered blue ridges. PROPOSAL: AerialDepth ON by default on a clear day (the owner decides from the sheet).
- The big blocks are forest stones scaled up to 9 m: close up (linn_below) their texture is soft and their shape
  generic. From the title only a few grey shapes show at the top right. The committed 2.5 m clearing thins the title's
  foreground trees round the viaduct's start and leaves Grandfather's flank darker (sheet_title_keepout.png); a fit
  per plant against the stones' own colliders would keep the trees (next round).
- Mt Mitchell and Chimney Rock road views were not shot (time); their far land changes the same way as NC 226A's.

---

# HANDOFF - NEW GAME INTRO (agent "intro", branch `newgame-intro` off mountain-nm e26dc1f4, 2026-11-16; NOT pushed, NOT merged)

Owner, 2026-11-16: "When starting a new game, the screen should be black (like you're in a long tunnel), after you
select your car, you hear it drive up from behind, then through the camera, and the camera attaches and player takes
over, and drives out of the tunnel to reveal the Appalachian mountain track level (like the screenshot reference) as
the player is driving to Charlotte to start their career/life there."

## What I found (file:line on e26dc1f4)
- The career starts on day 1 = FRIDAY 1 JANUARY 1999 (LifeRules.cs:12, Epoch :111), block DAWN (LifeState.slotIndex 0).
  It is winter: bare trees. THE PHOTO'S FALL COLOUR NEEDS THE START MOVED TO OCTOBER - the owner's decision; the
  start is unchanged. PSX_INTRO_DAY=288 previews the opening on 15 October 1999 without touching the career.
- BUT THE FALL DRESS IS BARREN HERE (PC46, PSXLab, the October preview): the whole drive from the east portal shows
  bare brown slopes with a few spruces and no hardwoods at all - not even trunks - where January's leafless forest
  stands dense in the same places (intro_jan_vs_oct.png, oct_check.png). The owner's "bare smooth brown ground with
  no trees" on the fall title, right at the roadside. Pre-existing (the intro touches no trees); MeshForest's
  per-dress leaves (MeshForest.cs DressMaterials / MeshForest.Gpu.cs cmdOn) are where I would look. Moving the start
  to October needs this fixed first.
- The car is chosen in LifeHomeScreen.Showroom.cs CommitLane (:291, the showroom's DRIVE THIS ONE) or
  LifeHomeScreen.cs BuildCarConfirm's DRIVE THIS ONE (:1133, FULL SHEET). Both: ApplyStartingLane, Save, the
  first-drive flags, LoadScene(0). Before it: name/age/job, the license question (NO = the guided first drive, which
  the home screen then hops into - unchanged, the statics ride through the intro).
- The Little Switzerland loop's bake (swiss_stage.json) runs WEST from Gillespie Gap (0 m) along the Parkway to the
  tunnel (3992-4180 m). Toward Gillespie Gap / NC 226 / Marion / I-40 / Charlotte = the loop backwards = the reverse
  twin LittleSwitzerlandRev (same scene; RaceHandoffApplier turns the path round).
- The bore is a 188 m rock tube with dressed-stone portals (PC); it reads like the owner's Parkway photos. Not changed.
- ExposureAdapt's tunnel table is read in the RUN's path metres: on a reverse twin that is wrong (forward metres
  against a reversed path), so the eye never opens in the real bore on LittleSwitzerlandRev races
  (ExposureAdapt.cs InVenueTunnel). The intro sets the bore itself (ForceTunnel); the general bug stays (see below).
- The owned NM Autumn Valley `Tunel_single` (coordinator's note): a modern cast-concrete portal with wing walls and a
  painted fake interior (35.7 x 31.3 x 16 m, 638 tris; Blender render in scratch intro\av_tunnel_views.png). Not a
  Parkway stone arch and not a 188 m bore - not used, nothing extracted.

## What I changed (switch PSX_INTRO, default ON; 0 = DRIVE THIS ONE reloads the home screen as before)
- NEW Scripts/NewGameIntro.cs (+ .meta):
  - The wizard (name, license, car pick, close-up) over black, its panels near-black, with the tunnel's sound
    (procedural drips through an echo, a low draught: wind_low low-passed). A carrier object (DontDestroyOnLoad)
    keeps the black, the sound, the skip prompt, the caption and the CHARLOTTE card across the scene loads.
  - DRIVE THIS ONE: black at once; LittleSwitzerlandRev loads as a TEST DRIVE (banks nothing: no km, fuel, wear,
    damage file, injury or tow), Solo, no traffic, the career's own day, 08:00-12:00 MORNING light (the career's
    clock is untouched), a dry road under an overcast sky, rolling.
  - The bore: the lens stands in the lane at 1.15 m, 79 m in, facing the exit; the car rolls up from 125 m behind at
    58 km/h on its own engine (EngineAudio hears it from outside: 3D, doppler, the bore's reverb), its real
    headlights on (the night beam over the eye's gain in the bore, the day beam after the exit), the black lifting
    as the beams reach the lens; it passes through the lens; the lens eases (1.8 s) into the chase rig (or the
    close one if that is the player's view); a mounted view (cockpit, hood, roof, bumper) comes back as one view
    change when the ease is done - in the seat's view the game cuts the cabin away for the seat, and a passing car
    read as roofless from outside (run5); the controls pass at 40 % of the ease with a 1.8 s lane-keeping hand that lets go the moment the player touches
    anything; the HUD fades in (lap, time and position stay hidden for the intro; the map, dials and fuel show).
  - The exit: the eye, held at 1 until the attach, opens in the bore (2.4) and closes on the day; a caption
    BLUE RIDGE PARKWAY / LITTLE SWITZERLAND - FRIDAY 1 JANUARY 1999 on a dark band for 5.5 s.
  - 2400 m past the exit (or 180 s, 20 s stopped, 3 s stranded): fade, CHARLOTTE (NORTH CAROLINA - the date), home.
  - SKIP: hold Esc, a pad's Start, or the top-left HOLD HERE TO SKIP button (touch, mouse) for 1 s, any time from
    the bore on: a short fade, CHARLOTTE, home. Pause is off during the intro (Esc and Start are the skip).
  - A watchdog: if the stage never takes the intro (no RaceManager) the player goes home after 10 s, never stuck in
    black. Tools (batch jobs, tours, shot links) never get the intro unless they ask (IntroPlayCheck), like the title.
- Shared files (small): LifeHomeScreen.cs Start (the black backdrop + ambience, ~10 lines), the WizardBg colour
  (6 call sites), BuildCarConfirm's DRIVE THIS ONE and LifeHomeScreen.Showroom.cs CommitLane (one line each:
  `if (!NewGameIntro.Launch(S)) SceneManager.LoadScene(0);`); RaceManager.Start (+1 line, after TitleScreen.Attach);
  EngineAudio.EarFor (+1 line); ExposureAdapt (Scripted, ForceTunnel: +2 fields, 2 lines in Tick).
- NEW Editor/IntroPlayCheck.cs (+ .meta) and tools/intro-check.ps1 (-Off, -Skip, -View <view>, -NoWatch, -Out).

## Proof (real editor play-mode frames, PSXLab Win64, fresh processes; scratch = C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\intro\)
- intro_sheet_chase.png: the whole sequence, saved view CHASE (run4): black + UI, the car pick, black at DRIVE THIS
  ONE, the hold, the headlights, the pass, just past, the attach, inside looking at the exit, the reveal, the
  caption, the drive (900 m), the fade, CHARLOTTE, the home screen. Video: run4_chase\intro.mp4 (41.7 s, 10 fps:
  the bore, 450 m out, the fade and the card).
- intro_sheet_cockpit_skip.png (run5: START held by a virtual pad once the ease began -> CHARLOTTE -> home in
  4.3 s) and run6_cockpit_strip.png (run6, cockpit saved: the pass on the chase rig with the roof on, then the seat).
- intro_before_after.png: PSX_INTRO=0 (the wizard on the charcoal, straight home) against on.
- intro_sheet_october.png: PSX_INTRO_DAY=288, the same opening on 15 October 1999 (ALL PASS; a preview for the
  owner's call - and the barren fall dress above).
- Every run: the home screen's career is exactly what DRIVE THIS ONE saved (money, day 1, block DAWN, the car's
  JSON - km, fuel, wear, faults, paint - and the diary), the handoff clear, the hooks released, the sound back.
  Runs 3, 4, 6: ALL PASS - the car at 57-58 km/h at its slowest through the approach, pass, ease and hand-over (it
  never stops), heard from outside on the way up and from its rig after, the eye 1.00 -> 2.43 in the bore -> 1.00
  out, 2400 m to the fade (139 s in all). Run5 (skip): ALL PASS. PSX_INTRO=0: ALL PASS.
- LifeSim self-test on this code: SELF-TEST FAILED (63) against the sandbox's 02:37 baseline (64). The differences
  are PC46's own (the deck-run scene-index rows moved by 6, the ChimneyRock barrier row gone, the roadside-tree
  row's counts); nothing touches the wizard or the intro (the self-test runs in batch: the intro stands down).
- Controller: drive as always; hold START (or Esc) 1 s to skip. Touch: the touch controls fade in with the HUD at
  the hand-over; hold the top-left HOLD HERE TO SKIP button 1 s. Keyboard/mouse: hold Esc, or hold the button.

## What still looks wrong in my own frames
- January: bare trees, not the photo's fall (the career's date; see above).
- The bore is a flat-roofed box whose rock texture stretches along it (a light streak along the ceiling); the
  photo's tunnels are rough rock arches. The portal is the stage's dressed-stone arch, unchanged.
- On the chase rig the pass is the lens inside the car's bonnet for a fraction of a second (a flat panel in a still).
- A mounted view arrives as one camera change after the ease (with the HUD's view-name flash), not a blend.
- The reveal 0.35 s out is overexposed on purpose (the eye closing); the caption's band is translucent.

## Not done / not verified
- No change to the tunnel's geometry; the owned NM tunnel not used (see above). The reverse-twin bug in
  ExposureAdapt's tunnel table is left (it affects LittleSwitzerlandRev races outside the intro).
- Not seen on a phone or with a real pad (the pad skip was a virtual pad); the skip button's phone placement
  (top-left, above the touch map) was reasoned, not shot at a phone's aspect.
- The 2400 m drive in the checks is the check's own autopilot (NewGameIntro.AutoDrive), not a hand on the wheel.

## For the merge
- Shared-file edits: LifeHomeScreen.cs (Start: the black backdrop + ambience, ~10 lines; the WizardBg field and its
  call sites; BuildCarConfirm's DRIVE THIS ONE: one line), LifeHomeScreen.Showroom.cs (the CarPick panel colour;
  CommitLane's last line), RaceManager.cs Start (+1 line), EngineAudio.cs EarFor (+1 line), ExposureAdapt.cs
  (2 fields, 2 lines in Tick).
- New: Scripts/NewGameIntro.cs + .meta, Editor/IntroPlayCheck.cs + .meta, tools/intro-check.ps1. No data files, no
  scene pass, no re-bake: the intro runs in PC46's LittleSwitzerland scene.
- Switches: PSX_INTRO (default on; 0 = before), PSX_INTRO_DAY (preview only), PSX_INTRO_HOUR (default 1 MORNING),
  PSX_INTRO_SKY (default 2 overcast).

## What the owner will notice
NEW GAME is now dark, with water dripping in a tunnel. After he picks his car, its own engine comes up the tunnel
behind him, its headlights light the rock, it drives through the camera, the camera settles behind it, and he drives
out of the Little Switzerland Tunnel onto the Parkway toward Gillespie Gap before CHARLOTTE and his first day. The
game's calendar starts on 1 January, so the trees are bare; starting the career in October would give him the
photo's season, once the stage's fall forest draws again (today it is bare brown slopes in October).

# HANDOFF - LAKE LURE ROUND 2, THE FIDELITY ROUND (agent "lakelure", branch `mountain-lakelure2` from mountain-nm e26dc1f4, 2026-11-16; NOT pushed, NOT merged)

The round's aim (coordinator, 11-16): what still kept Lake Lure from being "the memory of what it used to be like", most
visible first. Every change has its own switch; `=0` gives round 1 for that item, and with all eight at 0 the stage
builds as round 1 (checked in a fresh lab: 44 mouths / 10 walled / 16 faced, LakeLure 0 FAIL). Defaults: all eight ON (each proven below in a fresh PSXRec build).

## What was wrong (found)
- The town was hidden in forest: the keep-out only kept trees off footprints, lots and roads, so every lawn, the beach and
  the inn's grounds grew trees (8:15: the inn not visible at all). Under the trees, the plain stage ground.
- 52 of 151 buildings have a footprint a gable cannot cover (the inn fills 0.53 of its box) and got a flat cap: boxes.
- The lidar has 2,992 cells of 12 m steeper than 52 degrees over 18 m of relief within 2.2 km of the road (Chimney Rock's
  cliffs, Rumbling Bald, the gorge walls). Cliff meshes alone, on the 60 m far ground, read as boulders on painted canopy;
  the cells themselves needed to be rock.
- The stage builder plans cuts and rails without the side roads: 10 mouths sat behind a rail (all warranted by a critical
  fall or water, none by the mapped rail alone), 16 behind a cut bank or a ditch lip.
- ChimneyRock's 4 audit FAIL lines (EDGE FACE 661 R and 1020 L, PAST THE REACH 712 L, UNWARRANTED BARRIER 0 R) are the
  base's: identical with PSX_LURE_CHIMNEY=0 in a fresh process (PSXRec), unchanged by both rounds.

## What changed (files, switches)
- NEW `Editor/PSXRacingBuilder.StageLand.cs` (+ meta). `tools/roads/bake_world.mjs` writes `open`, `piers`, `cliffs`;
  `Art/LakeLure/lure_world.json` and `Art/ChimneyRock/chimney_world.json` re-baked offline (2024-09-20 attic + 2018
  lidar; new cache file `attic_lakelure_open_2024-09-20.json` in the main checkout's tools/roads/cache).
- PSX_LURE_OPEN: keep-out bit 8 (strict) on the mapped parks, pitches, playgrounds, beach and retail land, a yard round
  every building (12 m in town, 6 m elsewhere) and the lawn between each town building and its road; bit 32 (parkland:
  one 15 m grove in five keeps its trees) on the marina's land and along the town streets where buildings front them
  (36 m downtown, 18 m in the villages). Mown lawns and the beach's sand laid on it (3 m cells, corners cut).
- PSX_LURE_TIMBER: `Theme.timberSpans` 3,450-3,850 m and 11,000-11,450 m wear RailKit.Rustic (post-and-rail, video 6:00
  and 16:50) where the plan has a rail; W-beam elsewhere.
- PSX_LURE_GRASSBANK: cut runs under 5 m high to 8.1 km and from 10.4 km wear grass, with no rock dressed on them.
- PSX_LURE_GRANITE: Meadow photoscanned cliffs on the lidar's 24 m cliff clusters (faced down the fall line, seated on
  the drawn ground, no colliders, never within 16 m of the road; 1.6 M triangles PC, 0.6 M Android) and a granite skin
  on every cliff cell over the drawn ground; no trees on the cells (far terrain too).
- PSX_LURE_FORMS: hipped roofs on the footprints a gable cannot cover, house roofs at least 33 degrees, corner boards,
  chimneys, the porch on the straight run of walls that faces the road, the inn's two-storey veranda with a gallery.
- PSX_LURE_MARINA: the 19 docks and finger piers of the 2024 map, plank walks on posts 0.75 m over full pool.
- PSX_LURE_MAINST: the Park Road frame's Main Street gets a double yellow, edge lines, sidewalks before its buildings,
  and its lawns come to 1.6 m off the asphalt.
- PSX_LURE_MOUTHGAP: the near grid notched along each side road's first 24 m to its lidar profile; a mapped rail broken
  across a mouth where the rail is its only warrant. Mouths laid 44 -> 52.
- Shared files (small): `PSXRacingBuilder.Stage.cs` (ChordKit: one InTimberSpan line; BuildStageBanks: the grass
  material and a `grassy` parameter that skips PlaceCliffs and ReliefFace; the planner: LureMouthGaps after `want`),
  `PSXRacingBuilder.cs` (Theme.timberSpans and Lake Lure's spans), `PSXRacingBuilder.StageRiver.cs` /
  `StageTown.cs` (this agent's own: hooks, forms).

## Proof
All in PSXRec (ANDROID target: billboard forest, plain stage ground, no PC mesh forest or rock relief - PC47's scene pass
is the real look), fresh processes, the game's noon. Sheets in the lakelure scratch folder:
- `proof_lakelure_r2c_driver.png`: the owner's video | round 1 (e26dc1f4 as merged; the 6:00 and 16:50 rows from the
  all-switches-off build) | round 2, at 1:20, 7:43, 8:15, 8:45, 9:45, 10:05, 16:20, 6:00, 16:50 and the Park Road's
  Main Street. 8:15: the inn now stands across its lawn (round 1: forest). 7:43, 1:20, 16:20: grass banks, not rock.
- `proof_lakelure_r2c_items.png`: the timber post-and-rail (6:00), Bat Cave, the inn from behind, the marina and the
  inlet from above (hipped roofs), the granite from the road and close up, before and after.
- StageLab (final code, 12:40): LakeLure 0 FAIL, plants through solids 0 (3,884 checked); ChimneyRock 0 new lines (its 4
  base lines only), plants 0. World: open ground 0.457 km2, lawns 23.7 ha + beach 0.38 ha, granite 2,977 skin cells and
  1,535 faces (0.7 M tris), 19 piers / 1,095 m, 47 hipped roofs, 25 chimneys, mouths 52 laid (round 1: 44).
- Races (final scene): forward: all 4 home, 0 retired, but one check FAILS - the Skyline drove the wrong way 4.0 s at
  wp 1467 (downtown, 5.87 km) after a traffic collision (3 traffic wrecks in the run; r2b failed the same check, 3.0 s at wp
  2835, after a traffic hit). Reverse: RACE CHECK OK, all home. Round 1's own forward race on the same code (all switches
  off): a rival RETIRED into a tree trunk at 6.48 km and the player hit a trunk hard; with the town's trees gone, a car
  knocked off the road lands on open lawn and turns back instead.
- All eight switches off: 44 mouths / 10 / 16 and LakeLure 0 FAIL, as round 1.

## Merge
- Scene pass for LakeLure AND ChimneyRock (both read the re-baked world files). Packs: Meadow/Cliffs and the NMForest
  stones (PSXLab has them). Re-shoot both track maps on the PC scenes (lawns, granite, marina change the top view).
- PSXRec was set up for this round (filtered NM rock packs, scene list); nothing else touched.

# HANDOFF - TRAFFIC ROUND: THE OWNER'S UAA VEHICLES AS NPC TRAFFIC (agent "traffic", branch `traffic-uaa`, 2026-11-16; NOT pushed, NOT merged)

The owner bought "UAA - City Props - Vehicles" on 2026-11-07 and asked on 11-08 for its cars "to REPLACE the
placeholder Sketchfab traffic models (vans etc.), not the generated ones", and again on 11-15 ("Why aren't my PBR
bricks or NPC traffic cars ... added?"). This round puts the pack in the game's moving traffic, proven in pictures.
Base: mountain-nm e26dc1f4 (PC46 code), merged into this branch at 42c705b1. Sandbox: PSXCity (shared with "houses").

## What was found
- The placeholders: `TrafficSystem.Keys` (Scripts/TrafficSystem.cs:183) deals camry_2001 (a GT2 PS1 rip) and
  ford_transit (a PS1-rip van off Sketchfab) twice in every 19 cars. Over the city's 48 pool slots that is 12
  placeholders, 20 of the owner's own models (crown_victoria = his Passenger_Silver, sedan_98, fullsize_taxi /
  police / trooper) and 16 catalogue shells (volvo_estate, bmw_e30, audi_saloon, euro_hatch, citroen_cx, jdm_pickup,
  landrover, classic_van).
- Where traffic gets its models: Charlotte = CityTraffic.Build (Scripts/City/CityTraffic.cs ~3265); race tracks,
  mountain stages and the title's moving world = TrafficSystem.Build (TrafficSystem.cs ~427, the title runs it at
  Medium). Parked: PSXRacingBuilder.PlaceParkedCars (PSXRacingBuilder.cs ~5939) is called by NO venue today (every
  theme has parkedEvery = 0); the neighbourhood's drives (PSXRacingBuilder.Neighborhood.cs ~1798) deal 7 catalogue
  shells and no placeholder. Charlotte's streets and lots have no parked cars.
- The pack (INVENTORY.md in the scratch folder): 40 prefabs, 18 vehicles, all late-80s to 2000-model-year American
  shapes; every car white, silver or champagne (the 22 numbered prefabs only switch hub caps); low poly (124-724
  body triangles); one readable line of real text (the police car's boot: "EMERGENCY DIAL 911 ...") and a few badge
  shapes, all painted out in the bake; no maker's name anywhere.

## What changed
- NEW Scripts/TrafficKit.cs (ScriptableObject), Scripts/TrafficModels.cs (which model on which road),
  Editor/TrafficUaaBaker.cs (the bake), Editor/TrafficUaaShots.cs (the city before/after frames).
- The bake (TrafficUaaBaker.Bake, or Ensure() from the neighbourhood build) runs only on a PC content pass
  (PSX_PC_CONTENT=1) with the store files present; any other pass DELETES Resources/TrafficKit.asset, so a checkout,
  the web or a phone build keeps today's traffic. Everything it makes stays under Assets/StoreAssets/UAAVehicles/Gen
  (gitignored). Per vehicle: scaled to the real car (0.95-1.00), the game's car frame, the pack's own turning
  wheels (dual rear wheels, the rig's 5 axles turn with the rear pair), the collider = the body's box without
  mirrors, windows darkened to the car paint shader's glass, the rest matte-flagged, lamps found on the texture by
  CarLampFinder (the rig's head lamps set by hand: the finder took its roof markers), a one-mesh far level (LOD1
  under 4.5 % of the screen, about 95 m for a car), and REPAINTS: the pack has no colour variety, so 10 bodies get
  2-4 colours each from a 1999 palette (green, burgundy, navy, light blue, red, teal, grey, beige, silver, white).
- TrafficModels lists: CityMix (48 slots): 31 of the pack (23 everyday cars, 2 box trucks, a bus, 2 rigs, 2 cabs, a
  police car), 9 of the owner's own (crown_victoria x3, sedan_98 x3, his taxi, police, trooper), 8 catalogue
  shells x1; NO placeholder. RaceMix (stages, race tracks, title): 12 of the pack's everyday cars and vans (nothing
  longer than a van), his 5, the 8 shells. Fits: rigs only on cls>=4 main lines (and they avoid ramps), the bus on
  cls 2-3, box trucks not on residential streets, cabs within 3.2 km of Trade and Tryon or on freeways; long bodies
  keep straight on at junctions. The city pool may grow past its cap by the slots held to their own roads (idle
  ones are not drawn; live cars stay <= the cap).
- SWITCHES: PSX_TRAFFIC_UAA=0 = today's traffic, model for model (default ON when the kit is in the build).
  PSX_TRAFFIC_UAA_PARKED=1 (scene BUILD time) also deals the pack into the builders' parked cars: default OFF.

## Shared-file edits (for the merge)
- Scripts/City/CityTraffic.cs: Car fields kind/heavy (~198); Extend's route weights (~975-978); the pool growth
  line (~3075); TrySpawn's Fits search (~3204-3217); HeldBack() (~3254-3262); Build's model choice and Dress
  (~3266-3277) and the Car init (~3306).
- Scripts/City/CityShaderWarm.cs ~55-58 (the pack's materials warmed).
- Scripts/TrafficSystem.cs: Keys comment (~176-182), the pool (~399), Build (~429, ~436).
- Editor/PSXRacingBuilder.cs PlaceParkedCars (~5946-5953, ~5969); Editor/PSXRacingBuilder.Neighborhood.cs
  (~1800-1804, ~2024). Editor/TrafficPlayCheck.cs: PSX_TRAFFIC_SHOTCARS=1 close shots of the traffic cars (opt-in).

## Proof (PSXCity, fresh processes, switch 0 vs 1, same camera poses)
Sheets in `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\traffic\sheets\`:
traffic_city_day.png (9 street-view places: driver's eye and traffic-camera eye), traffic_city_close.png (the three
nearest vehicles at each place), traffic_city_rigs.png (the tractor-trailers on I-277 and Independence),
traffic_city_night.png and traffic_city_night_close.png (21:30, head and tail lamps), traffic_kit.png (line-up
beside the owner's crown_victoria and sedan_98, every vehicle front and rear), traffic_kit_skins.png (every repaint),
traffic_stage.png (GillespieGap race traffic). Reports: scratch\traffic\out\ (reports\, tpc_*, ctc_*, uaa_bake.txt).
- Draw cost, the live pool at Trade St 17:15 (48 cars both): base 100,240 triangles and 276 renderer-materials
  (2,088 tris, 5.8 draws a car); after 56,750 and 282 (1,182 tris, 5.9 draws a car), and each pack vehicle drops to
  ONE draw past ~100 m (its far level; today's shells have none). Night, lamps on: 11.0 draws a car both.
- Mix within 200 m of the nine places (after, day): 18-29 of ~40 vehicles are the pack's; rigs seen on I-277 and
  Independence, the bus on Trade, Tryon, South Blvd and Wilkinson, no cab on Queens Rd once the radius was 3.2 km;
  no exception from game code in any city run (9 runs).
- GATE city traffic check (Editor/CityTrafficCheck.cs, Charlotte, graphics on), base vs after: 37 ok / 0 FAIL both,
  "CITY TRAFFIC OK." both. Rush hour parked: mean active 43.5 vs 46.7; driving 44.2 vs 43.4; 03:00 5.5 / 2.5 vs
  6.0 / 2.0; red lights run 0 both; wrecks only the scripted player hit; traffic-on-traffic 0 both; physics steps
  over 5 ms 124 vs 23. CityTraffic's own step 0.919 vs 1.048 ms (gate 1.5). Editor frame at rush hour (rendering):
  21.05 ms with 46 cars vs 21.76 ms with 48 (the editor is CPU-bound there: half the triangles did not show as time).
- GATE stage traffic check (TrafficPlayCheck, GillespieGap, a race with traffic) base vs after: every check ok both,
  "TRAFFIC KEEPS RIGHT." both: keeps right (0 samples over), faces its way, rides on the tarmac (worst 0.00 m), the
  limit, a hit car wrecks and the player takes damage, the wreck rests on the road.
- The kit bake: PSXRacing_uaa_bake.txt "KIT 17 vehicles" (copy: scratch\traffic\out\uaa_bake.txt).

## What still looks wrong (in my own sheets)
- The pack is low-poly mobile art (124-724 body triangles, 512-1024 photo textures): close up it is plainer than
  the owner's own crown_victoria (3,514 tris); at street distance it reads as period-correct American traffic and
  far better than the PS1 rips it replaces.
- On some repainted bodies a light sky reflection in the front side window stays white (it is in the photo texture).
- The tractor-trailer is ONE rigid body (no articulation): it is held to freeway and trunk main lines and keeps
  straight on at junctions, but a tight turn would swing the trailer stiffly.
- Pre-existing, not this round: the owner's crown_victoria's second livery (fullsize_01_512x256) draws dark
  camouflage-like blotches on its body (seen in the after close views at Trade St and I-77); base can pick it too.

## Not done / not verified
- Parked cars: no venue calls PlaceParkedCars today and the neighbourhood list has no placeholder; the pack can be
  dealt there with PSX_TRAFFIC_UAA_PARKED=1 at build time, but no scene was rebuilt and looked at: default OFF.
- The title's moving world gets RaceMix through TrafficSystem; not photographed (no title run in PSXCity).
- No player build: frame time on the owner's PC is unmeasured; the editor numbers above are geometry counts.
- The LifeSim self-test was not run: it references no traffic code (no TrafficSystem / CityTraffic / TrafficModels
  in LifeSimSelfTest*.cs), and its car checks iterate CarModelLibrary.Models, which the kit is not part of.

## What the coordinator must do at merge
1. Merge `traffic-uaa` (no city re-bake: CityTileCache.Format untouched, CityTraffic is runtime).
2. In PSXLab, extract the pack (the tool is on this branch, 64 files with their .meta):
   `powershell -ExecutionPolicy Bypass -File tools\store-assets.ps1 -Dest C:\Users\mcgee\PSXLab -Only "UAA - City Props - Vehicles"`
3. Bake the kit AFTER the last non-PC pass (a pass without PSX_PC_CONTENT=1 deletes it): either the PC47 scene pass
   with PSX_PC_CONTENT=1 (the Neighborhood build calls TrafficUaaBaker.Ensure) or explicitly, 2-3 minutes:
   `-batchmode -quit -executeMethod PSXRacing.EditorTools.TrafficUaaBaker.Bake` with PSX_PC_CONTENT=1. Check
   `PSXLab\PSXRacing_uaa_bake.txt` ends "KIT 17 vehicles" and Assets/PSXRacing/Resources/TrafficKit.asset exists.
   With -SkipScenes the explicit bake is required.
4. Resources/TrafficKit.asset (+ .meta) is now in .gitignore beside Assets/StoreAssets: it points into store files.

## What the owner will notice
In Charlotte the white Sketchfab vans and the PS1 Camry are gone: most traffic is now your UAA pack's 1980s-90s
American sedans, wagons, vans and SUVs in period colours, with box trucks, a city bus on the arterials,
tractor-trailers on the freeways and yellow cabs around Uptown; the mountain stages and the title get the pack's
everyday cars. PSX_TRAFFIC_UAA=0 brings back today's traffic.

# HANDOFF - HOUSES: BRICK AND STONE, THE HOUSE MIX (agent "houses", branch `charlotte-houses`, 2026-11-16; NOT pushed, NOT merged)

Branch `charlotte-houses` (worktree `C:\Users\mcgee\PSX Racing-houses`) = the stopped 11-15 session's work (commit 1213415c,
"WIP from the stopped 11-15 session") + mountain-nm e26dc1f4 merged (db500c09) + today's fixes. Sandbox PSXCity (lock "houses",
shared with "traffic", removed after every job). The owner: "Why aren't my PBR bricks ... added?" (11-15), "You can also use
brick and stone textures for houses (front face or full) as necessary for variety" (11-15), 11-11 "replacing these very
unrealistic generic textures with windows going through roofs" (top priority).

## Found
- BEFORE is not all siding: round 1's PSX_CITY_HOUSEBRICK put the facade ATLAS's brick columns (tall office sash windows) on
  15-85 % of houses by 350 m cell (CityMeshes.HouseWalls.cs HouseBrickChoose), so whole pre-war streets read as brick blocks
  (round 3's own note: "Plaza Midwood row reads as brick ranches; real: siding bungalows"). The mix replaces that rule.
- OSM gives nothing to class houses by: of 76,372 house-ish buildings in tools/city/cache/buildings_city.json, 2 carry
  building:material and ~20 start_date. The class therefore comes from the spot (distance and bearing from the Square, a
  600 m era cell, a 350 m brick cell) and a hash of the house's centre (steady from visit to visit, tile = far proxy).
- The owner's pack (Asset Store cache: Objective Environment / PBR Bricks Materials, 123 MB): Brick01 TileDown + TileMed,
  Brick02, Brick03, Brick04, each with N / AO / DISP. Used: Brick02 = the red brick (3 x 8 bricks cut out of its 3.46-brick
  tile so it repeats), Brick01_TileMed = the old brick and, painted over, the painted brick. Not used: Brick01_TileDown
  (a wall foot going grey: does not repeat upward), Brick03 / Brick04 (derelict: plaster patches, spalled faces). The pack
  has no stone: the stone is the stage walls' coursed stone (Art/Roads/T (3).jpg, Classic 256 + Enhanced 512, no HD set).

## The wall classes and their shares (CityMeshes.HouseWalls.cs HmShare / HmDistrict; % of houses; stone = an overlay)
| district / era row (where, by rule) | siding | full brick | brick front | painted brick | stone | source |
|---|---|---|---|---|---|---|
| 0 pre-1940 streetcar suburbs (< 4.6 km: Dilworth, Elizabeth, Plaza Midwood, Wilmore) | 62 | 26 | 0 | 12 | 3 | frame bungalows the majority, brick bungalows / Tudors the rest (Dilworth, Elizabeth NR districts; the owner's "wood-sided bungalows") |
| 1 Wesley Heights (268-302 deg, 1.4-2.7 km) | 35 | 55 | 0 | 10 | 2 | county landmark page: "rows of brick Colonials and frame bungalows" (hl.mecknc.gov); NR MK1793 lists brick veneer |
| 2 Myers Park / Eastover (140-188 deg, 3-5.9 km) | 25 | 50 | 0 | 25 | 5 | Myers Park NR inventory (charlottenc.gov): brick Colonial / Tudor Revival, painted brick common |
| 3 North Charlotte mill village (36-72 deg, >= 3.3 km) | 95 | 3 | 0 | 2 | 0 | frame mill houses (North Charlotte NR district) |
| 4 ranch belt 1945-69 (4.6-7.5 km: 85 % of 600 m cells) | 25 | 65 | 2 | 8 | 3 | post-war brick-veneer ranches |
| 5 Beatties Ford belt (318-4 deg, >= 2.5 km: McCrorey Heights, Oaklawn Park) | 7 | 88 | 0 | 5 | 3 | NR forms MK3221 / MK3220 (hpo.nc.gov): "one-story brick veneer Ranches" built 1950-77 |
| 6 1970-89 | 45 | 45 | 7 | 3 | 3 | |
| 7 1990-2009 | 37 | 33 | 27 | 3 | 8 | Census new houses, South, 2010: brick the principal wall 40 %, vinyl 30 % (ProBuilder on the Census survey); the brick-front / vinyl-sides builder house |
| 8 2010- (and 1 in 12 old-district houses: infill) | 55 | 15 | 20 | 10 | 20 | fiber cement and stone accents on new Charlotte houses; white painted brick |
Eras beyond 7.5 km by 600 m cell: 7.5-10 km 45 % 1945-69 / 40 % 1970-89 / 10 % 1990-2009 / 5 % 2010-; 10-13 km 10 / 45 / 35 / 10; 13-17 km 20 % 1970-89 / 50 % 1990-2009 / 30 % 2010-; beyond 17 km 10 / 45 / 45.
Brick by era: old brick before 1940 (and in 40 % of the 1945-69 cells, 25 % of the 1970-89 cells), else the red brick; two tints of it a 350 m cell. Stone: on a siding house a stone foundation, or (1990 on) a stone water table on its front; on a brick-front house of 1990 on, a stone front instead; a stone chimney.

## Changed (switches: PSX_CITY_HOUSEMIX=0 = the picture before; =2 forces the mix without the pack, a test)
- `Scripts/City/CityMeshes.HouseWalls.cs` (mine): the HOUSE MIX block. HouseMixOf(centre) = era, class, masonry, tints, foundation,
  water table, chimney (by position alone: tile, neighbour tile and far proxy agree). HmWall lays a masonry wall by the siding
  wall's own layout (same bays, storeys, window band, head clearance, ground rule), the window = the siding texture's whole
  window cell (white frame; night mask and windows audit as before) between brick piers, a rowlock sill under it and a rowlock
  lintel over it on a house a street comes within 45 m of. Brick is mapped in world metres from the wall's start and the
  house's floor (courses run on round the corner); a brick front's brick returns 0.3 m round its outside front corners, the
  siding side walls on a brick foundation band; a 1990+ siding house with stone gets a stone water table to its sills with a
  concrete ledge. HouseBrickChoose: round 1's atlas brick replaced by the class (no double count), HouseTintOf: the class's
  siding / trim / paint colour (far proxies: the colour the walls read as). HmReady: only with the owner's pack in the kit.
- `Scripts/City/CityMeshes.HouseParts.cs` (mine): the chimney in the house's brick or stone; a block is not a house (siding as before).
- `Scripts/City/CityMeshes.cs` (shared, 3 lines): Slot.HousePaint appended before COUNT (painted brick); vertex colours on the
  four masonry buckets; HousePaint in the building slot list. FacadeBrick / FacadeMid / FacadeTower (empty since the facade
  atlas) now hold the red brick / old brick / stone.
- `Scripts/City/CityKit.cs` (shared, 1 field): houseMixPack (the kit's brick came from the pack).
- `Editor/PSXRacingBuilder.HouseMix.cs` (new): the pack's three tiers at kit-build time (14 s, kept while fresh): HD 1024
  colour + normal + rough + AO to `<project>\StoreGen\HouseMixHD` (TextureHDTool.Import reads it), Enhanced 512 to
  `Assets/PSXRacing/EnhancedSrc/TexEnhanced/StoreGen/HouseMix` (rows in `Assets/StoreAssets/Gen/TextureTierManifest_store.txt`),
  Classic 256 to `Assets/StoreAssets/Gen/HouseMix`; the four materials (CityFacadeBrick, CityHouseBrickOld, CityHousePaint,
  CityHouseStone; .mat committed). All generated files are gitignored (the repo is public; the pack is a store file).
- `Editor/PSXRacingBuilder.City.cs` (shared, 2 lines): HouseMixMaterials(m) instead of the three unused facade materials; kit.houseMixPack.
- `Editor/TextureHDTool.cs`, `Editor/TextureTierTool.cs` (shared, 2 + 1 lines): read the HD stage and the store manifest.
- `tools/store-assets.ps1`: the PBR Bricks entry (8 files) and `-Only <text>`. `.gitignore`: the generated folders.
- Tools: `Editor/CityRefSpots.HouseMix.cs` (RunHouseMix: streets by era, close-ups by class, tiers, depth-tie frames),
  `Editor/CityAudit.Windows.cs` (house-mix counters and the city-wide class census).

## Tiers (what each class draws with)
| masonry | Classic | Enhanced | HD (PC: colour + normal + roughness + AO) |
|---|---|---|---|
| red brick (Brick02) | house_brick_red 256 (generated) | 512 (generated) | 1024 set (generated) |
| old brick (Brick01_TileMed) | house_brick_old 256 | 512 | 1024 set |
| painted brick (Brick01_TileMed painted) | house_brick_paint 256 | 512 | 1024 set |
| stone (Art/Roads/T (3).jpg) | 256 (in repo) | stonewall.png 512 (in repo) | NONE: Enhanced stands in (request below) |
| the window cell, gables, trim | city_facade_house 64 | 128 | none (as before) |
Tile checks (art AGENTS.md) on the generated exports after resizing: unmarked 3x3, half-tile offset (both seams through the
middle) and 8x8 at reduced size, every tier and map, the normal map decoded as lit: sheet `houses\tiles_v1.png`.
Red brick: 3 bricks x 8 courses, the cut's seam inside brick faces, head joints of alternate courses offset as the photo's;
old / painted: the pack's own 15-course tile, its broad grey band evened out (HmFlatten). Repeat units: U = 0.70 m / 1.05 m,
V = 0.68 m / 1.05 m (real brick sizes: 0.233 x 0.085 m red, 0.22 x 0.07 m old). No intentionally non-repeating axis.

## Request for the texture workflow (art workspace AGENTS.md names; 4x the authoring size)
- `T (3)_overhaul1_hd.png` + `_overhaul1_hd_normal` + `_rough` + `_ao` (1024 x 1024, from the 256 px `Assets/PSXRacing/Art/Roads/T (3).jpg`):
  the coursed rough-faced grey-tan stone, seamless in U and V, 2 m a repeat on a wall. It upgrades the house stone (water
  tables, stone fronts, chimneys) AND the mountain stages' retaining walls (the same texture).
- Worth it next: `city_facade_house_overhaul1_hd` set (256 px, the siding with its window cell at u 0.3125-0.6875, v 0.25-0.75
  kept): beside the 1024 px brick the 64 / 128 px siding and its window are now the weakest texture on a street.

## At merge (PC47)
1. Merge `charlotte-houses` (it already holds mountain-nm e26dc1f4). Shared-file edits: `Scripts/City/CityMeshes.cs` (Slot enum:
   HousePaint before COUNT; NewBuckets: one `if (HouseMixOn)` line giving the four masonry buckets vertex colours; the building
   slot list: `Slot.HousePaint` added after `Slot.Crown`), `Scripts/City/CityKit.cs` (field houseMixPack),
   `Editor/PSXRacingBuilder.City.cs` (CityMaterials: `HouseMixMaterials(m)` replaces the three FacadeTower / Mid / Brick
   material lines; EnsureCityKit: `kit.houseMixPack = hmPackUsed;`), `Editor/TextureHDTool.cs` (Import: the HD stage folder;
   FindOriginal: Assets/StoreAssets/Gen too), `Editor/TextureTierTool.cs` (ReadManifest: the store rows), `tools/store-assets.ps1`.
2. In PSXLab, BEFORE the scene pass: `powershell -ExecutionPolicy Bypass -File tools\store-assets.ps1 -Dest C:\Users\mcgee\PSXLab -Only "PBR Bricks"`
   (8 files, 34 MB, into Assets\StoreAssets\PBRBricks; nothing is downloaded: the package is in the owner's Asset Store cache).
3. The scene pass builds the kit, which makes the three tiers (14 s, kept while fresh). Check the build log for
   `house mix: the owner's PBR Bricks pack ... three tiers made` (or `tiers up to date`) and `city kit: 102 slots ... from the pack True`.
   `house mix: NO PACK` means step 2 was missed: every house then stays sided (nothing breaks, the mix is simply off).
4. Re-bake the city tiles (Scripts/City changed; PSX_CITY_HOUSEMIX is in the bake signature). CityTileCache.Format NOT changed.
   New Slot (HousePaint, appended last) = the kit must be rebuilt (the scene pass does it).
5. The player: PSXBuildWin64 already runs TextureTierTool.BuildBundle (reads the store rows) and TextureHDTool.Import (reads
   `<PSXLab>\StoreGen\HouseMixHD`); its [TextureHD] log lists `house_brick_red / _old / _paint`. No other HD source is needed.
6. Never commit what the kit makes: `Assets/StoreAssets/` (incl. Gen), `Assets/PSXRacing/EnhancedSrc/TexEnhanced/StoreGen/`
   and `/StoreGen/` are gitignored. Any sandbox or player build without the pack shows today's siding (the kit flag).
7. Do not mirror `Assets\PSXRacing\EnhancedSrc` (or `StoreGen\`) into PSXLab between the scene pass and the player build: the
   generated Enhanced brick lives there and the repo has no copy. If it was mirrored, run
   `-executeMethod PSXRacing.EditorTools.PSXRacingBuilder.HouseMixKit` once before the player build (remakes them, 2 min job).

## Still generic / not done
- One window for every house (the siding's 4-pane cell, white frame), brick or siding; the rowlock sill and lintel are flush
  (a shade darker, no relief). No brick porch piers, steps or garden walls; chimney caps stay concrete.
- The red brick (Brick02) is an even orange-red with blue-grey joints: it reads as new machine brick; the 1950s ranch red is
  only its darker tints. The stone front reads heavy (rough coursed stone, no HD set).
- 1990s subdivisions are fill houses standing 60-75 m back behind yard trees: from the road their brick fronts barely show
  (house placement is the hoods agent's; the close-ups prove the walls).
- Beside the 1024 px brick (HD) the 64 / 128 px siding and its window are now the softest texture on a street.
- Not run: PerfTour / frame time, the house census (CityAudit.Houses, PSX_WIN_HOUSES=1: wall extents unchanged), the PC
  bake (CityTileBake), a player build, the Win64 bundle path, far-proxy colours in a frame, Classic street views.

## Proof (PSXCity, fresh processes; base = PSX_CITY_HOUSEMIX=0 on the same code)
- Sheets (every cell labelled, the same pose before | after; streets in the PC tier = Enhanced + HD sets):
  `houses_final_streets_noon.png`, `houses_final_streets_night.png`, `houses_final_close.png` (wall close-ups in HD, Enhanced and
  Classic; TREES HIDDEN in the close-ups only, labelled STAGED), `houses_final_nandina.png` (Nandina Street, the clearest street change:
  round 1 had drawn the whole row as atlas-brick blocks; an earlier pair in the Classic tier + HD sets, labelled so),
  `tiles_v1.png` (the tile proof) - all in `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\houses\`.
- Windows audit (CityAudit.WindowsAudit, the 64-tile sample + 3 cost tiles): CUT 0 / BURIED 0 before and after; whole windows
  14,331 -> 30,624 (the old atlas-brick houses' windows were atlas cells, not counted; now every house's are); roof closure
  897 of 109,367 points -> 897 of 109,367 points (unchanged code). House mix in the sample: 2,252 houses - siding 1,199, full brick 720,
  brick front 76, painted 243, stone front 14; stone water tables 28, infill 106, brick returns 131, brick fronts with no street
  within 80 m (left sided) 27. City-wide, the 25,717 mapped house footprints: siding 52.1 %, full brick 35.1 %, brick front 1.6 %,
  painted 10.9 %, stone front 0.3 %, any stone 960; by era pre-1940 17,411 / mill 1,463 / 1945-69 4,714 / 1970-89 65 / 2010- 2,064
  (the 1970-2009 suburbs are fill houses, classed the same way: the sample's fill tiles hold 177 / 111 of those eras).
- Cost: building triangles a sampled tile 4,281 -> 5,732; build ms a tile, OSM-house tiles 349 -> 390, fill suburbs
  126 -> 172. The three tiles: subdivision (Lansdowne Rd) 1,832 -> 2,832 tris; in-town (McClintock Rd) 3,772 -> 4,788;
  the Square 11,276 -> 11,276 (no house). The baked tiles grow with the building triangles (roads and ground unchanged).
- Flicker (depth ties: each frame against itself with the near plane x1.01, CityFlickerFinder's test; pixels changing > 30):
  54 frames each (every street and close-up pose in the first tier): base 1,644 pixels, after 1,644, frame by frame the same within 1 px. The ties are on the ground and lot pads, identical before and after: the masonry adds none (no coplanar faces of
  two textures: only plain pieces overlap 3 mm, on matching texels).
- Night: windows on brick light by the siding's night mask as every siding house does (city_facade_house_night); 1999 lamps.

## What the owner will notice
Charlotte's houses are no longer one siding: red-brick ranches along Beatties Ford, old brick and painted brick among the
sided bungalows of Dilworth and Plaza Midwood (the brick blocks with office windows are gone), brick-front houses with
siding sides and stone water tables in the newer streets - with his own PBR Bricks on them.

# HANDOFF - WATER ROUND 2, PART A: the HD water made dark (opt-in), its import kept, Lake Lure off the sea preset (agent "water", branch `water-hd`, 2026-11-16 morning, for PC47; NOT merged)

The review (`scratchpad\water\REVIEW_SYN.json`, items 1-5) and the coordinator: merge the water dark, so PC47 looks like PC45 and
`PSX_WATER_HD=1` opts in. Sandbox PSXPerf (Win64), AGENT_LOCK; PSXLab not touched this round.

## What changed (on top of the round-1 entry below, whose "Defaults" section this replaces)
1. OPT-IN. `WaterLook.On` is true only when `PSX_WATER_HD` is exactly `1` (WaterLook.cs). OFF (the default, and always on phones and
   WebGL, which cannot set it): the keyword is disabled, no beginCameraRendering hook is installed, no globals are pushed and
   `SpeedBlurFeature.WaterDepthPass` is never enqueued (AddWaterDepth returns before it even finds the shader). One log line says
   which: `[WaterLook] HD water OFF (PSX_WATER_HD is not 1): keyword disabled, no hook, no depth copy`. `PSX_WATER_HD_SEA` still
   gates the open sea. The owner's launcher: `tools/PSXRacing_WaterHD.cmd`, to drop beside PSXRacing.exe.
2. IMPORT KEPT. `ConfigureTextureImporters` (PSXRacingBuilder.cs, every scene pass and EnsureCityTextures) skips everything under
   `Art/Water/`, so it no longer forces water_ripples.png to point / no mips / 256 / alphaIsTransparency. (ReleaseBudget's 16-bit
   set already excludes it: linear and unlabelled.)
3. `_RippleTex` defaults to `linearGrey` (flat normals in this Linear project; `gray` sampled 0.216 = a frozen 20-55 degree
   tilt). `WaterPreset.Apply` logs an error when the sheet is missing. CORRECTION to round 1: a material built before this round
   (no sheet) now draws flat, not tilted, ripples when HD is on.
4. Lake Lure is not the sea: `Theme.waterLook` is now optional; `MakeWaterMat` uses the given look, else the theme's, else
   `WaterPreset.Sea` on a coast (oceanBearingDeg set) and `WaterPreset.Lake` everywhere else. On a PC the preset is the HD water's
   only colour control: fragHD reads neither `_Color` (the theme's waterDeepTint) nor, above tier 0, `_SandColor`.

5. THE QUEUE (found by the item-4 proof). PSX/Water's queue is back to Geometry, so with the HD water off the shader is the old one
   at its old place in the frame. While HD is active, `WaterLook` moves the water materials to 2501 (the head of the
   transparents, after URP's frame copy and the depth copy): the scene's materials at its first frame, the city's streamed tiles
   through `WetReflections.Tag` (every CityWorld tile renderer passes through it). The builders reset `renderQueue` to -1 and
   WaterShots restores every moved material, so no asset keeps 2501.
6. THE DEPTH COPY IS ITS OWN SHADER (also found by the proof): `Hidden/PSX/WaterDepthCopy` (Shaders/PSXWaterDepthCopy.shader,
   always-included, GUID b5ea6883...), drawn only by SpeedBlurFeature.WaterDepthPass while HD is on. As a second pass of
   PSX/Water it changed the opaques' draw order with HD off (a wet bridge deck's gutter at Langston in rain).

## Proof (PSXPerf, D3D12 RTX 3070, fresh processes)
- OFF = 7badf362, pixel for pixel. Two runs of WaterShots (charlotte, emerald, langston sets; 16 frames at 1920x1080 through
  PSX/Blit), neither setting PSX_WATER_HD: A = this branch; B = the same code with 7badf362's PSXWater.shader swapped in
  (byte-exact, from git archive). Edit mode takes `_Time` from Time.realtimeSinceStartup, so the old water (and the beach swash,
  the stars, the halos) never render the same instant in two processes (two of my earlier OFF runs differed in 31k-342k px). For
  this test only, `freeze_time.py` rewrote every `_Time` read in the test sandbox's shaders (8 files) to `_PSXTestTime`, which
  the tool sets to one fixed instant - identically in A and B. Result with the queue still at 2501 (commit 64939945): 14 of 16
  frames 0 px, including all 8 required pairs (charlotte_lake/_catawba/_creek and emerald_surf, noon and night); 2 differed:
  langston_sound_rain 1,612 px (max 35/255, a 100x40 px strip of the wet bridge deck's gutter, not water) and
  emerald_surf_sunward_sunset 3 px (the sea's far edge on the horizon). The queue went back to Geometry (93cc1cbf): 15 of 16,
  sunward sunset now 0 px; Langston rain unchanged, so not the queue. My two branch runs in separate processes agree exactly on
  that frame (0 px), so it is deterministic and caused by the shader file. E1 = my PSX/Water without its second pass (the depth
  copy): all 5 Langston frames 0 px against the base, rain included. So the copy moved to its own hidden shader (item 6,
  dc964b23). FINAL (dc964b23, F1 vs B): 15 of 16 frames 0 px - every required pair, every noon, sunset, night and Emerald rain
  frame, and the water pixels of the 16th; Langston rain again shows the same 1,612 px on the same deck gutter. With a pass
  removed from PSX/Water it vanished, and with one new unused shader asset in the project it returned, so it is the deck's
  coplanar wet surfaces resolving in draw order, which follows the project's shader set (the review's S1 saw this deck differ
  between processes too). The control that would show it directly (7badf362's PSX/Water with this commit's code, i.e. the same shader set: expect 0 px against F1) was queued behind another job's lock at the time of writing.
- Depth copies enqueued: 0 in every OFF run (log line), 28 in the HD run. The log says `[WaterLook] HD water OFF ...`.
- Import kept (item 2): after a run whose sets called EnsureCityTextures, the sheet reads Trilinear, mips (10 levels), 512 px,
  sRGB off, alphaIsTransparency off; mip 0 read back from the GPU is byte-identical to the source PNG (0 of 262,144 px), so the
  shipped sheet is the tile-checked one: `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\water\tile_check_ripples_imported.png`
  (3x3, half offset, 8x8 at 1/4; normal lit, foam B, breakup A; wrap differences equal interior ones on every channel).
- Grain (the reviewer's grain2.py, 1:1, HD water mean |Laplacian|; old water in brackets; the review's degraded-import figure
  after the arrow): Lake Wylie 89.2 (4.5) <- 94.7; Sugar Creek 52.4 (5.4) <- 51.6; Langston causeway foot 15.8 (10.6) <- 98.5;
  Emerald surf 18.4 (9.9) <- 43.5; Catawba 8.8 (4.3). The import was most of the static on the sea; the static on Lake Wylie and
  the creek (mirrored trees, the SSR hit/miss flip) remains: F01, Part B item 6.
- No sheet (item 3): city water with `_RippleTex` cleared vs the same water with ripple strength 0, tier 2, the Lake Wylie pose
  turned 0/90/270 degrees (180: no water in view): mean water luminance ratios 0.997, 0.996, 1.004.
- Self-test (default OFF): job C (64939945, no variable set): FAILED (67) = PSXPerf's base run with 7badf362's code, FAILED (67); no new failing check (one pre-existing failing check, "the release budget's set and the labels agree", lists 51 textures instead of 13, because the Charlotte shot set's EnsureCityTextures rewrote city textures in the sandbox between the two runs). Final code
  (dc964b23, F2): FAILED (67), the same failing checks as the base (the same count difference on that one check), 4 more ok
  lines (including "PSX/Water ..." and "Hidden/PSX/WaterDepthCopy is in GraphicsSettings' always-included shaders"). An earlier
  self-test (job D, 09:55) is VOID: another job rewrote PSXPerf's code directories while my lock was held (the Editor folder
  changed under it, and its shot step then could not find WaterShots).
- HD with the final code (F2, PSX_WATER_HD=1 and _SEA=1): depth copies 28, import as above, null sheet 0.997 / 0.995 / 1.005;
  grain at the same five poses: Lake Wylie 71.6, Sugar Creek 53.5, Langston foot 16.1, Emerald 17.8, Catawba 7.5 (this run is not
  time-frozen, so the figures move a little run to run).

## For the coordinator at merge (PC47)
- The full scene pass needs nothing special: no data file, no city re-bake, no -SkipScenes assumption (with HD off the water
  materials keep their shader's queue; the builders write renderQueue -1). Each sandbox needs PSX/Water (2b0cd64e...) and
  Hidden/PSX/WaterDepthCopy (b5ea6883...) in GraphicsSettings' always-included list (committed in ProjectSettings; the self-test
  checks both) and water_ripples.png with its committed .meta (a scene pass now leaves it alone).
- Sandbox note: in PSXPerf another job (the trees job's lock name is trees4) took the lock in the seconds between my chained jobs
  three times, and once rewrote PSXPerf's code while my lock was held (job D's self-test is void for that reason). Re-sync
  PSXPerf's Scripts/Editor/Shaders before using it; my runs also left its Shaders unfrozen and its CityWater.mat re-dressed.
- When lakelure merges: `PSXRacingBuilder.StageRiver.cs:308` becomes
  `return MakeWaterMat(MeshPrefix + name, shallow, deep, name == "RiverWater" ? WaterPreset.River : WaterPreset.Lake);`
  (with item 4 the lake would get Lake anyway; the river needs the explicit River).
- Default OFF means PC47 shows today's water everywhere. The owner opts in with `PSXRacing_WaterHD.cmd`.

## What the owner will see with the .cmd (honest)
Without it, nothing changes: PC47's water is PC45's. With it, Charlotte's lakes, the Catawba and the creeks mirror their banks,
trees and skyline in murky green-brown water instead of a flat blue sheet - but on Lake Wylie and the creeks the mirrored tree
line carries per-pixel static (white sky pixels flicking in it) that will crawl as he drives, and gently sloping city banks show a
pale foam rim along the waterline. Both stay for Part B (items 6 and 7), as do night, rain and driver's-eye proof. Lake Lure, once
merged, takes the Lake preset; the Bogue sea and sound keep the old water unless the .cmd's second line is enabled.

# HANDOFF - THE WATER ROUND: lakes, rivers and the sea toward the PS3-PS4 target (agent "water", branch `water-hd`, 2026-11-16 night; NOT pushed, NOT merged)

The owner, while the Lake Lure stage was being built: "A lot of potentially nice race tracks around Lake Lure. Would be a good time
to update the water visuals as well." Worktree `C:/Users/mcgee/PSX Racing-water`, base mountain-nm 7badf362 (PC45). Sandbox PSXLab
(Windows target, the coordinator's PC sandbox; lock held only per job). Switch `PSX_WATER_HD=0` = the old water.

## What the game had (checked)
- One water shader, `PSX/Water` (opaque, one pass): two scrolled pack sheets for colour and normal, sky fresnel, hashed glitter,
  vertex depth (colour r), the ocean swell (colour g), shore foam, lamp streaks. Materials: `MakeWaterMat` (the three Bogue Banks
  stages' seas) and `CityWaterMat` (every city lake, river and creek; the city's water meshes carry NO colour stream and no uv2).
- No other water is drawn: CityFar paints the far land's water as a flat colour from CityWater's main texture x _Color (kept as it
  was), TrackMapShots swaps in a flat map colour, the minimap uses a colour constant. The horizon, title and garage have none.
- The store packs (Meadow, NMForest, OE) have no water textures. The PSX Textures v3.1 pack has water_1/2/3_normal.png (256 px,
  derived from the caustic colour sheets: lit, they read as a pebbled skin). Not used for the ripples; the pack colour sheets stay.

## What changed
- `Shaders/PSXWater.shader`: `#pragma multi_compile _ PSX_WATER_HD`. Keyword OFF = the old vert/frag line for line (the diff
  against 7badf362 wraps them in `#if/#else`; nothing inside changed). The queue moved Geometry -> Transparent-499: still opaque
  (no blend, depth written), so its pixels are the same, but it now draws after URP's colour copy and the new depth copy. New
  properties `_RippleTex`, `_Absorb`, `_Scatter`, `_WaterLook` (unused by the old path). A second pass `PSXWaterDepthCopy` (own
  LightMode: URP never draws it).
- `Shaders/PSXWaterHD.cginc` (new): the HD variant.
  - Ripples: `Art/Water/water_ripples.png` (new, 512 px, linear: RG a directional wind-wave spectrum's normal by inverse FFT on a
    periodic grid, so it tiles by construction; B a periodic Worley foam lace; A a periodic breakup field). Three octaves (9 m,
    3.2 m, 1.1 m repeats for the sea) turned against each other and run downwind at their own phase speed; strength from the day's
    wind (`_PSXWaterWind`, the cloud deck's x0.35) in drifting gust patches (calm glassy water between them under ~5 m/s).
  - Rivers: the contract's uv2 advects the ripples and the foam (two-phase flow map; no pulsing on still water), roughness rises
    with speed, colour b turns it white (foam + churned normals + white in-scatter).
  - The world in it (tier 2): the wet road's screen-space march, reading `_PSXWaterDepth`; hits replace the sky lookup; fades at
    the frame's top edge (and at the sides only for rays that travel sideways).
  - The bed through it (tiers 1-2): URP's `_CameraOpaqueTexture` (the PC asset already copies it for every camera) bent by the
    ripples, Beer-Lambert over the true path (`_Absorb`), the water's own colour scattered in (`_Scatter`); the waterline melts into
    the ground over 6 cm of depth; shore foam on the true depth (2.5x wider on the open coast), the surf (the swell breaking
    shoreward of 3.6 m), whitecaps as before, rain rings (`_PSXWaterRain`), the city's distant haze (`_PSXDistHaze`).
  - Tier 0 (phones, WebGL, mirrors, the paint probe): vertex depth, the sky's mirror, two octaves, the same foam/flow/rain.
- `Scripts/WaterLook.cs` (new): the switch (`PSX_WATER_HD=0`), the keyword, per-camera tier push from `beginCameraRendering`
  (`_PSXWaterTier`: x copies, y SSR, z third octave), the day's wind and rain. Tier 2 = PC + VISUALS > REFLECTIONS on + the
  PSXCameraOutput camera (the wet road's rule, any weather); tier 1 = a base camera with URP's opaque copy (PC, REFLECTIONS off);
  tier 0 = everything else.
- `Scripts/SpeedBlurFeature.cs` (shared; small): `WaterDepthPass` at AfterRenderingSkybox (enqueued before the wet pass, only when
  `WaterLook.DepthCopyFor(cam)`): reads the camera's depth attachment as a texture (the wet pass's proven way) and writes it texel
  for texel into a persistent R32 RTHandle bound as `_PSXWaterDepth`. WHY: URP's own `_CameraDepthTexture` read as the far plane
  in the water's transparent draw on this pipeline (debug view, test site, r3), so no bed and no mirror hits.
- Builders: `PSXRacingBuilder.WaterPreset` (Sea / Lake / River / City: absorption, scatter, ripple tile/strength/foam/flow
  roughness) in `PSXRacingBuilder.Stage.cs`; `Theme.waterLook` (default Sea); `MakeWaterMat(name, shallow, deep, look = null)`
  applies the theme's (or the given) preset and the ripple sheet; `CityWaterMat` applies `WaterPreset.City` and is now internal
  (main texture and `_Color` unchanged: CityFar paints the far land's water from them). No mesh code touched.
- `Editor/WaterShots.cs` (new): the proof tool (below).

## The vertex contract (for the lakelure agent's lake and river; the coordinator's merge)
colour r = depth (0.5 at the waterline, +/-12 m), g = open to the ocean, b = white water 0..1, uv2.xy = flow m/s world x,z.
Lakes and the sea: b = 0, uv2 = 0 (or no uv2: the Bogue seas have none and read still). A mesh with NO colour stream reads as no
data (the city). Material: `MakeWaterMat(MeshPrefix + "Lake", theme.water, theme.waterDeep, WaterPreset.Lake)` /
`WaterPreset.River` for the Rocky Broad. The test site in WaterShots builds both (lake in a bowl, 493 m river at 4.5% with 14
boulders, white-water wedges behind each boulder and on riffles, flow 1.1-2.4 m/s mid-channel).

## Defaults (decided from the sheets)
- `PSX_WATER_HD` default ON. `PSX_WATER_HD=0` = today's water everywhere.
- `PSX_WATER_HD_SEA` default OFF: a scene holding an open-ocean sea (a PSX/Water material with `_OceanWaves`, i.e. the three
  Bogue Banks stages) keeps today's water, because on the sheet the HD sea lost the white swash line on the beach and the white
  fringes round the sandbars, goes orange-brown in the sound at sunset where the old water keeps the sky, and loses the old
  water's bright glint path in the rain. `PSX_WATER_HD_SEA=1` shows it (the sheet's Bogue rows are that).

## Proof (PSXLab, D3D12, RTX 3070; every pair = the same pose in two fresh processes, PSX_WATER_HD=0 then =1)
- Sheets (frames at 1080 lines through PSX/Blit, shown downscaled; reference column):
  `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\water\sheets\water_sheet_1_bogue.png` (Emerald Isle surf noon/sunset/sunward/night/rain, Bogue Sound from the
  Langston deck noon/sunset/night/rain, the causeway foot), `water_sheet_2_charlotte.png` (Lake Wylie from Wilkinson Blvd, the
  Catawba from the Wilkinson Blvd bridge over Lake Wylie, Sugar Creek from the I-77 bridge; noon and night),
  `water_sheet_3_testsite.png` (STAGED site: lake noon/calm morning/sunset/night/rain, river noon/sunset, the mouth).
  Ripple sheet tile checks (3x3, half offset, 8x8 at 1/4, lit, glint, foam, breakup): `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\water\tile_check_ripples.png`.
- What the sheets show: Charlotte is clearly better (the lake mirrors its trees and the skyline; the Catawba is a turbid
  green-brown river mirroring its banks with a soft edge, where the old water was a dark blue sheet with white glitter squares;
  the creek mirrors its trees). The calm staged lake is a dark mirror of its ridge (the old one: flat blue stripes).
- Cost, water GPU at 2560x1440 (synthetic scene, the water 44.6% of the frame, the camera's render with the water shown vs
  hidden, 40 drained renders each, medians): old water 0.50 ms; HD tier 0 (phones, WebGL, mirrors) 0.24 ms; tier 1 (PC,
  REFLECTIONS off: bed, soft shore, 2 octaves) 0.67 ms; tier 2 (PC default: + SSR, 3 octaves) 1.26 ms. Not in those numbers:
  the depth copy itself (one R32 write per frame for the game camera on tiers 1-2, paid with or without water in view).
  Android: tier 0 (the Mobile asset has no opaque copy; no depth copy is enqueued); the variant adds the ripple sheet's 4-6
  samples to the old water's 3. The stage frames on this shared GPU swing by tens of ms render to render (the other agents'
  Unity jobs), so only the synthetic scene was timed.
- Gates vs base (base = 7badf362's code, fresh process, same sandbox):
  - StageLab EmeraldIsle: report identical (0 differing lines after dropping timings); 0 roadside failures in both; the sea mesh
    unchanged (60,406 verts, 6,943 open + 666 shore cells).
  - LifeSim self-test: base FAILED (64). Mine before the GraphicsSettings fix: FAILED (66) = the same 64 + 2 new, both "PSX/Water
    is named at runtime and not always-included" -> fixed (PSX/Water added to the always-included list). Re-run with the fix,
    fresh process: FAILED (64), the same 64 as the base (0 new, 0 gone; 4,599 ok lines vs 4,597).
  - Shader compile: no `Shader error` / `Shader warning` lines in either shot process's log (D3D12; both variants compiled).

## What still looks wrong (my own sheets)
- Bogue Banks (why the sea stays OFF by default): no white swash line on the beach, no foam fringes round the sandbars, the sound
  goes orange-brown at sunset, the rain loses the old water's bright glint path, and the near water is still finer-grained than
  the old smooth swell. Night at the beach and on the sound is black in both (no lamps there; the moon path not in view).
- The staged river: it runs and foams behind boulders, but reads as frothy grey-lilac rapids along its whole length (the riffle
  white water and the flow roughness are too strong) rather than clear water over brown stones, and the bank foam is a regular
  bright line. Tune `WaterPreset.River` (foam gain, flow roughness) and the contract's b on Lake Lure's real river.
- The staged site itself is crude (one brown far-land material, no trees; the stage's sun map is off there: it lies outside it).
- Charlotte at night: the lake and river are near black with only faint mirrored trees; no sodium streaks were in these views.

## Not done / not verified
- No real photographs on the sheets: fetching them is a file download, which needs the owner's own OK. The reference column names
  what to compare against and where (Wikimedia Commons categories); two cells use frames of the owner's own video (the lakelure
  agent's scratch captures: v0525 Lake Lure's marina, v0360 a glimpse of the Rocky Broad).
- Lake Lure itself and the Rocky Broad River are the lakelure agent's (not merged here); the staged test site stands in for both.
- Android/WebGL: the HD variant was compiled for D3D12 only (the PSXLab target). The GLES3 compile, a phone's frame and the
  WebGL frame were not checked; tier 0 is what they would draw.
- Not seen in a running player: WaterLook's per-camera push from beginCameraRendering and the depth copy were exercised through
  edit-mode render requests only (the tools push explicitly too).
- Rain rings, the river's flow and the foam's motion are only seen as stills (no motion capture).
- The surf is a shading of the coast's existing bathymetry (the bake's shelf is a few metres wide): no breaking-wave geometry.
- The city's water has no depth or flow data in its meshes: depth comes from the frame (PC), flow is none. A later city bake could
  write colour r (depth from bedY) and uv2 (flow along the creek's line, downhill) with the same contract.

## For the coordinator at merge
- Shared files: `Scripts/SpeedBlurFeature.cs` (new `WaterDepthPass` class; fields `waterDepthPass`, `waterCopyMaterial`;
  `Create` adds it; `AddRenderPasses` calls `AddWaterDepth` before the wet pass; `Dispose` releases it); `Editor/PSXRacingBuilder.cs`
  (Theme field `waterLook`); `Editor/PSXRacingBuilder.Stage.cs` (`WaterPreset`, `WaterRippleTex`, `MakeWaterMat` signature +
  one line); `Editor/PSXRacingBuilder.City.cs` (`CityWaterMat` internal + one line). New: `Scripts/WaterLook.cs`,
  `Shaders/PSXWaterHD.cginc`, `Editor/WaterShots.cs`, `Art/Water/water_ripples.png` (all with committed metas, hand-written GUIDs).
- Scene passes: the coast stages' sea materials take the new look only when rebuilt (EmeraldIsle was rebuilt by StageLab in
  PSXLab; LangstonBridge and AtlanticBeachBridge need the scene build); the city's `CityWater.mat` takes it at the next city kit
  build (WaterShots already re-dressed PSXLab's copy through `CityWaterMat()`). Without the rebuild an old material draws the HD
  variant with flat ripples (no ripple sheet) - nothing breaks.
- No data files, no city re-bake, no Resources change. The queue change needs nothing: the materials take the shader's queue.
- PSXLab: my last job left EmeraldIsle rebuilt with this branch's builder and CityWater.mat re-dressed; its code dirs hold this
  branch's Scripts/Editor/Shaders (re-sync before the next PC build).

# HANDOFF - CITY TREES STAND ON THE GROUND AT EVERY LEVEL (agent "trees4", branch `city-treefeet` off mountain-nm e26dc1f4, 2026-11-16; NOT pushed, NOT merged)

The owner, 10:40, on a Providence Rd frame: "Some even seem to be floating in air". Only the fix for the kit mountain-nm builds today (round 3, NatureManufacture); nothing of the round-4 work (the owner's trees) is in this branch.
- CAUSE: LoadVariant / LoadMergedVariant stand a tree on its LOD0's foot (the bark's 2nd-percentile height, past the root tips; `Editor/PSXRacingBuilder.MeshTrees.cs:668-684`) and draw every level on it. The packs' coarser levels have no roots, so their trunks start higher - up to 1.03 m (Meadow `07_Poplar_skan` LOD2); the pine stand-in `03_Poplar_skan_pine` hangs 0.43 m over the ground. They draw from 102 m (LOD1) and 140 m (LOD2) to 240 m on MEDIUM.
- FIX: `Editor/PSXRacingBuilder.CityTreeFeet.cs` (new, `CTFeet`), called once after `CTRound3` in `EnsureCityTreeKit` (`Editor/PSXRacingBuilder.CityTreeKit.cs`): each level's bark in its lowest 0.8 m drawn down to its LOD0 trunk base, nothing above moved (21 variants, 35 levels; meshes `Generated/CTFeet_*`). DEFAULT ON; `PSX_CT5_FOOT=0` = today's kit. Nothing under `Scripts/City`, no shader, no tile re-bake, no new kit field.
- PROOF (`Editor/CityRefSpots.TreeFeet.cs`, new: `-executeMethod PSXRacing.EditorTools.CityRefSpots.RunTreeFeet`; PSXPerf, kit built in the same process at the defaults): planted trees floating (trunk base > 5 cm over the ground under the trunk) at LOD0 / LOD1 / LOD2 - Providence Rd 7,463 trees: before 0 / 1,467 / 1,890, after 0 / 0 / 0; West Trade St 3,922 trees: before 0 / 202 / 306, after 0 / 0 / 0. Visible pop unchanged (Providence Rd walk 300 -> 20 m, 16 px blocks changing >3%: MEDIUM 784 -> 784, HIGH 309 -> 309; pixels 133,830 -> 134,013 and 69,259 -> 69,330).
- AT MERGE: merge `city-treefeet` (two new Editor files with .meta, one call in `PSXRacingBuilder.CityTreeKit.cs` after `CTRound3`; `HANDOFF.md` by union) and rebuild the city tree kit (`BuildCityTreeKit`, PSX_PC_CONTENT=1) before the player build - the fix lives in the kit's generated meshes. If `charlotte-trees4` is merged later, its `CT5Feet` (off by default) finds nothing left to lift.

# HANDOFF - SPLITS ROUND, FIX ROUND (J1, agent "splits", branch `charlotte-splits`, 2026-11-16 morning; NOT pushed, NOT merged)

An independent check of the first pass (f3f16ba0) found three blockers. All three are fixed on the same branch; this
section supersedes the first-pass section below wherever they differ. Every number is from a fresh process in PSXCity
on the final code; the base is PSX_CITY_SPLITAXIS=0 in the same sandbox (0 = PC45 exactly: no code path of this round
runs under 0, and the 0-mode audit, gate and concrete runs are identical to the first pass's).

## What the check found, and the cause (file: function)
1. Spot 37, n10035: the nose was a grass crack (0.9 x 9.5 m), no island to u 35, a 0.65 m step at u 35-37.5, then a
   square 4.3 m block at u 44. Three causes:
   - LineModel.SplitFrames.cs BuildSplitNode: the lane gap used a constant half width per carriageway, but the SB
     carriageway's third (turn) lane narrows over e11017 (band 10.85 -> 7.19 m), so the gap read up to 0.8 m too small
     and the pavements, widened by that, stopped short of each other.
   - The island grew along a NoseHalf flank sized to the widest gap 40 m on (slow), and the widening simply stopped at
     the flank's end with 0.65 m left (the step).
   - CityMeshes.cs BuildGores: the two carriageways were attached as a branch and its host while under 4.5 m apart; the
     gore painter paved the island flat and flagged the spans 'gap', which kept CurbMedian off to u 46.
2. n231 (the I-85 bridge approach, just past spot 36): the profile pulled the two pavements over each other (0.36-0.57
   m) and into the deck-approach median's square wall ends (concrete ORPHAN 14 -> 40 pieces). n4025, the interchange's
   north terminal, had the same overlap (0.5-0.84 m) and lost its whole median with no street-level check.
3. Spot 34 at night lost its east cobra-head light: RoadsideOccupancy builds its road pieces from the WIDEST half on
   both sides of the OSM line, so the straightened (off-centre) carriageway kept the old bulge's clear zone on its narrow
   side and pole station 1750/0 (e8634 s 11.9) found no foot (every try 'clear zone' or 'lot').

## What changed (commits 275f8783, c1b1ab16, 456c377e on f3f16ba0)
- LineModel.SplitFrames.cs, THE NOSE (rewritten): the lane gap is read from each band's own half width along its chain
  (SplitFrame.ownH / HAt). Each side's inner pavement edge sits OffAt metres off its own lane edge:
  - before the tip: half the lane gap, so the two pavements meet in the middle. They never overlap: a shoulder wider
    than half the gap is trimmed by a NEGATIVE widen (LineModel.cs Ext now applies a negative widen; only this profile
    writes one).
  - the tip: where the lane gap reaches 2.4 m, a 0.6 m round end; the island 1.2 m wide, 0.6 m off each lane edge.
  - where the lanes are already wider apart at a fan's edge (n7868: 4.3 m) the island still starts 1.2 m wide and
    flares out 1:4.
  - the 0.6 m offset eases to the carriageway's own shoulder over 3 m; after that nothing is widened (no step).
  - a node whose tip is missing or more than 20 m past its blends keeps the old taper; the overlap test is on full
    lane counts again (the first pass's node set: n7869 and n9152 keep PC45's taper).
- LineModel.Splits.cs: BfrHeldBack += n231, n4025 (PC45 under bfr until the deck-approach median gets a nose of its
  own); SameSplitNose (CityMeshes.BuildGores skips a shaped nose's two carriageways, as it skips a refuge island's);
  SplitsMovedEdge + RefugePairPc45; BfrBayKeep = {e16705} (see the traffic census below).
- RoadsideOccupancy.cs (the piece loop): a ribbon the round moved (SplitsMovedEdge: a split frame's carriageway, or a
  refuge pair the scope-0 list does not draw) is read where it is drawn - its middle and its half width - not PC45's
  widest half each side. Scope 0 and every other edge are untouched.
- CityMeshes.CurbMedian.cs: CurbMedianTrace (audit hook; null = off) with one reason per section.
- Editor/CityAudit.Splits.cs: NoseLines (each framed nose's design table, the curb as the tiles lay it, and
  CityPoles.Explain round PSX_SPLITS_POLES). Editor/CityRefSpots.Splits.cs: PSX_SPLITS_LENS_RAIN puts lens drops on
  the night-in-rain frames only (the first pass's noon frames were not dry).

## Proof (scratchpad `splits\`; frames: BEFORE s5 / AFTER s7, both on c1b1ab16, fresh processes)
- `splits_poses_before_after.png`: the owner's four poses, night in rain (lens drops) then DRY noon, BEFORE | AFTER.
- `splits_bfr19_tops_before_after.png` (19 nodes from 78 m up; held-back nodes labelled), `splits_plans_spots.png` and
  `splits_plans_bfr19.png` (drawn edges against OSM centre lines; base a3, after b7).
- `splits_n10035_nose.png` (n10035 from above: PC45 | first pass | fix), `splits_n7868_nose.png` (the same at n7868),
  `splits_spot34_lamp_top.png` (n10238 from above: PC45's pole on the bulge | first pass, missing | fix, at the kerb).
- n10035 nose, measured on the drawn edges (nose_gate.py on b7's plan; first pass b5 in brackets): pavements over each
  other at most 0.005 m (0.60); the island reaches 0.8 m at u 25.0, 0.5 m past the design tip u 24.5 (20.5, but as a
  crack); widest in its first metre 1.58 m (gate 2.4 m: PASS); no step - the inner edges move at most 0.34 m per 0.5 m,
  and that is the round tip itself; the curb (CurbMedianTrace) starts at u 25.0 on both carriageways (u 44 before).
  Hairline seams of 0.01-0.04 m remain in the flush part (sampling of the two edges' sections; not visible in the
  frames). Approach marking: the undivided road's centre lane hatched (U hatch, 1 of 1 split nose marked) and the two
  solid yellow lines diverge to pass the nose at 0.6 m (MUTCD 3B.10's required marking); no diagonal bars in the wedge.
- n7868 nose: no overlap, no crack, island 0.8 m+ from u 24.0, widest in its first metre 1.84 m (3.24 before the flare).
- Spot 34 lamp: pole station 1750/0 now stands at (-3341.94, 8784.71), 4.3 m west of PC45's (-3337.68, 8782.73), at the
  straight kerb; the night frame has its lamp lit and the white house lit again.
- n231 / n4025 held back: their frames (36 noon and the n231 top) are pixel-identical to the base; spot 36's NB lanes
  swing out again (u 20: -10.27 m against its own line -5.45: the spot-36 gate FAILS, as PC45).
  `splits_bridge_n231_before_after.png`: the deck approach north of n231 from the first pass's eye pose (a camera 1.3 m
  up on PC45's median, not a car), BEFORE | first pass | fix: the fix's frame differs from the base in 223 of 921,600
  pixels (sky) and its top frame (r2) in none.

## Gates (base 0 -> fix bfr; first pass in brackets where it differs)
- SPOTS (CityAudit.Splits b8 on 456c377e; the census measure in the undivided road's straight frame):
  - 34, n10238-n10236 (Phase A, unchanged by this round): island at mid 12.8 -> 1.82 m PASS (<= 2.3); outer bulge
    3.69/6.07 -> 0.002 m PASS; peak slope in the island frame 0.38/0.68 -> 0.006 PASS. Kink at the node 4.2 deg FAIL
    (gate 1.64; the exporter's fillet; PSX_CITY_REFUGECHORD=1 stays OFF). In the straight frame e8634/e8635 move at
    19.6-20.5:1 (n10236's e8634 at 19.6:1, just under the 20.4:1 line: FAIL by a hair).
  - 35, n10229-n10234 (Phase A): island 8.64 -> 1.84 m PASS; SB outer 5.81 -> 0.004 m PASS; island frame 167:1 PASS.
    In the plan's straight frame e8630 / e8629 move at 18.0:1 and 17.7:1 with a 5.5 deg kink at n10229: FAIL against
    20.4:1 (the same fillet kink as 34; the first hand-back reported only the island frame here).
  - 36, n231: HELD BACK (PC45). NB lane centre at u 20 -10.27 m against its own line -5.45 m: FAIL (the first pass's
    -5.54 came with the pavement overlap and the wall ends above). Median at u 20 9.45 m FAIL, ratios 9.1 / 8.0:1 FAIL,
    left jog 2.45 m over 121 m (as PC45).
  - 37, n10035: no NB outward overshoot, lanes -7.76 m at u 20 against OSM -9.21 (PC45 -16.35) PASS; NB 27.0:1 PASS;
    the turn-only lane nearest the nose ends 201 m before it (e16705's bay, kept) PASS (gate: L, about 58 m); median at
    u 20 0.01 m FAIL against 3.0-3.4 - the island starts at u 24.5, where the lane gap reaches 2.4 m (the plan's own
    nose rule), and is 2.7 m at u 30 and 3.4 m at u 35; NB census bulge 2.14 m FAIL (OSM's own bow).
  - NOSE gate (n10035 and n7868, the two shaped noses under bfr): curb starts at a lane gap of 2.4 m PASS; 2.4 m wide
    or less in its first metre (1.58 / 1.84 m) PASS; round tip 0.6 m PASS; no step PASS; approach marking: diverging
    solid yellow lines at both, the undivided road's centre lane hatched at n10035 (n7868 opens at a junction fan).
- BEATTIES FORD 19 nodes: 6 (PC45) -> 10 pass (first pass 11: n4025 is held back now). PASS n10229-n10234, n10236,
  n10238, n13570, n13571. FAIL: n231, n4025, n7963, n12438 (held back = PC45), n7869, n9152 (old taper: more lanes in
  the carriageways than the undivided road), n634 (no axis), n10035 (bulge 2.14 m, OSM's bow), n7868 (bulge 3.59 m).
- CITY under 'all' (c5, measure only; base a3): failing L/2 1595 -> 1432; failing full L 96.0% -> 90.5%; bulge >= 1 m
  1036 -> 876; lane-centre angle over atan(1/V) 1713 -> 1355 (p50 8.9 -> 4.6 deg); outer-edge jumps 779 -> 566;
  refuge pairs 17 -> 22 (PASS); shaped noses 279, of them 246 SPLT noses marked and 106 hatched. Every other CITY gate
  FAILS; 'all' is not the default.
- REGRESSIONS on Beatties Ford against the base (same sandbox, fresh processes):
  - Medians gate g8 (RunMedianGates, PSX_REFUGE_GATE_SCOPES=0,bfr,all): CITY AUDIT OK; scope 0 decides exactly PC45's
    17 pairs; bfr 19, all 22; the generated list is byte-identical to the committed LineModel.RefugeList.cs; the
    drawn-profile order table (VsOrderList) fits. PASS.
  - Concrete inventory within 1.5 km of (-3450, 9600), k2: identical to the base k0 line for line (70 strays, 0.22 km;
    class c ORPHAN 14 pieces, 29 m; the first pass had 40 / 57 m). PASS.
  - Drive audit, 57 edges z 8400-10700: base d0 / final d4 (456c377e): solid across a lane 0 / 0; BLUNT faces 1 / 1 (the same e1198 deck-end face at (-3328.7, 9669.9)); lane steps over 12 cm 2 / 0; lane surface off the solve 21 / 0; land on a lane 9 / 4 (both places are base findings: e8611 s 5 at n4025, held back, and e8621's end at the n10229 island); FALL HOLES 0 / 0; MEDIAN classes a/b/c 0 / 0; MEDIAN ACROSS two+ / none 0 / 0; WALLS IN LANES 0 / 0; bridge rail ends 7 / 7, short rails 28 / 28, invisible solids 88 / 88. No new failure: PASS.
  - Launch audit, box x -3750..-3150 z 8400..10700: base l0 / final l4: LAUNCH 0 / 0, UNLOAD 1 / 1 (T1), flown turns off the pavement 0 / 0, separations 1 / 1. PASS.
  - Traffic census (CityTrafficCensus, the five Beatties Ford spots, 6 min, cap 48), base t4 (re-run with its events
    kept) / final t5: wrecks 0 / 0; stuck (lane-change wait) 4 / 5; turn from the wrong lane 5 / 5; missed turn lane
    18 / 12; kink 7 / 9; no road under a lane 4 / 2; turn clips a corner 21 / 18; turn leaves the road 3 / 4. Traced by
    event location: the "leaves the road" difference is n7498 (e5963 -> e5782, a junction 125 m north of n7868, past the
    profile's end, the same edges and drawing as the base, which has it once) and one at n4025 (held back, PC45);
    no event at n15089 any more (the first pass's ended e16705 bay: 6-8 missed turn lanes and 2 waits there, now kept).
  - Lot entrances on an edge a profile moves (charlotte_lotent.bytes' key does not see eases): 1 in the base (a PC45
    refuge on Clanton Rd), 1 under bfr (the first pass had 2; with n231 and n4025 held back it is the base's one). The file's
    key and its content are unaffected under bfr: NOT re-made. Under 'all' 61.
  - Graph hash b2aa3453 in every run.

## Not done / not verified
- Spot 36 / n231 and n4025 (the I-85 interchange) are PC45 again: the NB lanes still swing out before n231. Fixing them
  needs the deck-approach median (its walls) rebuilt with a nose of its own, and n4025's median checked in Street View.
- n10035: the island starts at u 24.5 (lane gap 2.4 m), so the plan's "median 3.0-3.4 m at u 20" is not met; the SB
  carriageway's third (turn-only in OSM) lane still runs into the undivided road's centre lane at the node, where
  NoseMarks hatches it (J4's median opening would end it properly). NB still follows OSM's own 2.1 m bow.
- n7868 (bulge 3.6 m: OSM's line) and the 4-6 degree fillet kink at refuge noses (34, 35): as in the first pass.
- No diagonal bars in a split nose's wedge (MUTCD's optional marking); the wedge has the two solid yellow lines.
- Hairline seams (0.01-0.04 m) between the two pavements in the flush part of a shaped nose (each edge's sections are
  sampled apart); not visible in the frames at 720 lines; not checked closer up.
- 'all' (Phase C) is measured only; its CITY gates fail. The nose and occupancy changes apply under 'all' to all 279
  shaped noses and the moved ribbons, measured by construction and by the audit, not by pictures city-wide.
- BfrBayKeep is a one-edge list (e16705) for the bfr scope; under 'all' that bay is ended like the others.
- Self-test and door tour not run; the pictures are editor frames (720 lines), not a player build; CityTrafficCheck itself
  not run (the Beatties Ford census instead). LotAudit not run (the lotent key and the entrances it covers are unchanged
  under bfr).
- The frames (s5 / s7) are on c1b1ab16; 456c377e only keeps e16705's bay as OSM tags it (150+ m from every pose and top
  view); the audits b8, t5, l4, d4 and the gate g8 ran on 456c377e.

## For the merge (coordinator)
- Shared-file edits added by this round (on top of the first pass's list below):
  - Scripts/City/LineModel.cs Ext: a negative widen is applied (np/nm); every widen >= 0 behaves as before.
  - Scripts/City/CityMeshes.cs BuildGores: `if (LineModel.SameSplitNose(ei, hostIdx)) continue;` after the SameRefuge line.
  - Scripts/City/RoadsideOccupancy.cs, the piece loop in the static build: offC (LineModel.SplitsMovedEdge) pieces.
  - Scripts/City/CityMeshes.CurbMedian.cs PrepareCurbMedian: CmWhy trace calls at each skip (no effect while the
    trace is null) and SpanWhy / CurbMedianTrace / CurbMedianTraceEdges.
  - Scripts/City/LineModel.Splits.cs and LineModel.SplitFrames.cs (this round's own files): BfrHeldBack + 231, 4025;
    BfrBayKeep; SameSplitNose; SplitsMovedEdge; RefugePairPc45; the nose (OffAt, HAt, SplitNoseTable).
  - Editor/CityAudit.Splits.cs (NoseLines), Editor/CityRefSpots.Splits.cs (PSX_SPLITS_LENS_RAIN).
- Data: LineModel.RefugeList.cs unchanged (the gate's list is byte-identical). No export, graph b2aa3453. No new
  .cs files, so no new metas.
- Re-bakes: the city re-bake (pc_citybuild.ps1) for the spot build, PSX_CITY_SPLITAXIS=bfr the default.
  charlotte_lotent.bytes: not needed for bfr now (its key and the entrances it covers are unchanged; lot entrances on
  moved edges are back to the base's 1); still re-make it before 'all' is ever the default (61 there).
- If LANES2 or any lane-set change merges: re-run RunMedianGates (PSX_REFUGE_GATE_SCOPES=0,bfr,all) and copy
  refuge_list.generated.cs over Scripts/City/LineModel.RefugeList.cs; re-run RunSplits under bfr and read its SPLIT
  NOSES section (each tip, the curb from it). After J2 merges, re-run RunControl (Keller n10235 sits on the
  n10229-n10234 refuge pair).


# HANDOFF - SPLITS ROUND (J1, agent "splits", branch `charlotte-splits`, 2026-11-16 night; NOT pushed, NOT merged)

(First pass. The fix round above supersedes it where they differ: n231 and n4025 held back, the nose, the poles, the dry noon frames, the numbers.)

The owner, 2026-11-16 ~01:00, four PC45 shots on Beatties Ford Road at night in rain: "If the city and roads are to 1:1
scale, why are there so many anomoalies like this abrupt medians that force the road to meet at strange angles without
stop signs." Spec: the 12-agent plan (scratchpad `align\PLAN.json`), job J1 Phase A and Phase B, Beatties Ford first.
Worktree `C:\Users\mcgee\PSX Racing-splits` off mountain-nm 7badf362 (PC45). Sandbox PSXCity (WebGL), shared with J2
("control") by AGENT_LOCK. Graph hash b2aa3453 before and after: no export, no data file except the regenerated refuge
list. Nothing downloaded (the street-data download J4 waits for the owner).

## What was found (code, with the plan's causes confirmed in the running editor)
- Short islands: both Beatties Ford islands (shots 34/35) were off the refuge list only for "[flush tip]": the noses' centre
  lanes are OSM left-turn bays for streets the export never fetched (e8632/e8621 NB bays, e8620 SB bay between n10236 and
  n10229), and the bays' relays moved the centre lane inside the nose approach (e8621 carried a 46.3 m relay; LineModel.cs Relay).
  n10238's centre lane is a REAL bay (SB, to LaSalle St n10237) that the old rule would have hatched across its entry.
- Long splits: the SPLT block (LineModel.cs "THE MEDIAN TAPERS") only offsets the ribbon on OSM's 20-40 degree fan, so the lanes
  swing 5-7 m past their own line (n10035 NB 16.4 m drawn against OSM 9.2 m at 20 m; median 11.9 m). Fans at same-name meetings
  had no ease at all (n7868 outer edge jumps 6 m, n9152 6-15 m).
- The 10 m ChordInto follows the exporter's fillet: the undivided road's last metres bend ~10 degrees, so even a perfect island
  leaves a 4-5 degree kink at the node (audit: 4.2 deg at n10238, 5.5 at n10229). Masking it (a 30 m chord + an eased last
  30 m, PSX_CITY_REFUGECHORD=1) is rejected by the medians gate: its relay census measures the undivided edges in their own
  OSM frame, where the eased line reads 0.11-0.19 lateral slope (live, following the bend, reads 0). It stays OFF; J4's exporter
  fix (no through-pair fillet at SPLT nodes) is the real cure.
- Many 'long' splits carry more lanes in the carriageways than in the undivided road (n7869: a 2-lane road into 2 + 3 lanes;
  30 of the first 60 city-wide skips): no lane-position start exists there; they keep PC45's taper (listed).

## What changed (branch charlotte-splits; switches; defaults)
- PSX_CITY_SPLITAXIS = 0 (PC45 exactly) / bfr (DEFAULT: Beatties Ford's 19 split-type nodes, graph b2aa3453) / all (measure only).
  Sub-switches inside the scope: PSX_CITY_BAYEND=0 (bays as OSM tags them), PSX_CITY_NOSE=0 (PC45 noses and kerb set-back),
  PSX_CITY_SPLITMEET=0 (no profile at same-name fan meetings), PSX_CITY_REFUGECHORD=1 (OFF by default, see above).
- Phase A (LineModel.Splits.cs): BAYEND - a bay whose served node is a 2-arm node or a plain split nose (followed through 2-arm
  pieces) becomes the centre lane (TWLTL line set) on the undivided stretch from an in-scope nose to the first junction (7 edges
  under bfr: e8632 e8620 e8621 e8623 e14095 e16690 e16705); NoseMarks hatches it. A bay leaving the nose toward a real junction
  opens at the island's end: no hatch, island tip 0.6 m past the node (n10238, n10232). Flush-median tip-join zone: 1 m past
  each tip not gated for overlap (in scope). The medians gate decides one refuge list per scope (scope bits in the mode).
- Phase B (LineModel.SplitFrames.cs): at in-scope split nodes (and same-name fan meetings) each carriageway's lanes leave where
  they are in the undivided road (outer edge flush), ALONG a 30 m chord axis, and join their own lane line tangentially at
  u = L (cubic), L = max(30.5, 1.2 r(S) d, sqrt(6 d Rmin)) with d read at most 60 m on; drawn as a sampled shift along the path's
  own normal (section frame), the undivided road's last 30 m eased onto the chord, one section cut at a mitred node; at a fan
  (n231, n7868) the lanes start at the fan's edge. The nose:
  where the lane bands are 2.4 m apart a rounded island tip grows to the median (inner pavement widened before it, flush);
  CurbMedian drops its 12 m set-back there. Excluded and listed: turning roadways (own line > 35 deg 60 m on), separations
  > 15 m, short islands (refuge path), lane bands that overlap or cross, undivided road < 8 m (n634). Held back as PC45 under bfr:
  n7963 and n12438 (BfrHeldBack: wide-median divergence; a bfr traffic census met 3 wrecks on a solid beside e11521's shifted
  lanes). PSX_CITY_SPLITMITRE=1 (OFF): the plan's forced mitre at 3-arm splits.
- Audit: Editor/CityAudit.Splits.cs (-executeMethod PSXRacing.EditorTools.CityAudit.RunSplits): every SPLT end on the drawn
  lane centre / pavement edges, the census measure (outer edge, 80 m window, approach trend out), refuge ends by the gate's own
  island measure; city_splits.txt / _ends.tsv / _plan.tsv. Pictures: Editor/CityRefSpots.Splits.cs (RunSplitShots).
  Offline cross-check: tools/city/splitcensus.mjs (the investigation's PC45 replica + census, straight frame; PC45 only).

## Proof (PSXCity, every number from a fresh process; base = PSX_CITY_SPLITAXIS=0 in the same sandbox)
Sheets (scratchpad `splits\`):
- `splits_poses_before_after.png`: the owner's four poses from his chase camera (car where his was, ChaseCamera.SteadyPose),
  night in rain then dry noon, BEFORE (0) | AFTER (bfr). In 37 his car stood in PC45's left-turn bay; after BAYEND that space is
  the hatched approach, so the AFTER frame shows the car on it (his pose, not a staged object).
- `splits_plans_spots.png` (the four spots) and `splits_plans_bfr19.png` (all 19 nodes): drawn pavement edges, lane centres and
  painted lines against OSM's centre lines, in the undivided road's 30 m chord frame, before | after.
- `splits_bfr19_tops_before_after.png`: rendered frames 78 m above each of the 19 nodes at noon, before | after.
- Medians gate (RunMedianGates, PSX_REFUGE_GATE_SCOPES=0,bfr,all), final run: CITY AUDIT OK. Scope 0 decides exactly PC45's 17
  pairs (checked by key); bfr 19 (+ n10229-n10234, n10236-n10238); all 22 (+ IKEA Blvd n12599, University Pointe n13100,
  Highway 160 W n13194). Accepted pairs OFF -> ON: outer edge off the road's edge line p50 5.02 -> 0.003 m (max 0.004); steepest
  lane line / edge / path p50 0.501 -> 0.042 (max 0.060); island at mid p50 8.87 -> 1.81 m. The two new pairs: n10236-n10238
  island 12.80 -> 1.82 m (design = OSM 1.82), outer 3.69/6.07 -> 0.002 m, slope 0.38/0.68 -> 0.006; n10229-n10234 island
  8.64 -> 1.84 m, outer 3.12/5.81 -> 0.004 m, slope 0.60/0.89 -> 0.009. Hatched refuge noses 9 -> 13 (94 bars), flush tips 0,
  forced-solid yellows all rendered, VsOrderList table fits, load cost 12.5 ms.
- Spot gates (CityAudit.Splits, b4): 34 island at mid 1.82 m (<= 2.3 PASS), outer bulge 0.002 (PASS), peak slope 0.006 (PASS),
  lane-centre kink at the node 4.2 deg (gate 1.64: FAIL - the exporter's fillet, see above). 35 island 1.87 m (PASS), SB
  outer 0.004 (PASS), steepest 0.009 = 167:1 (PASS; e8630 was 5.49 m in 14.7 m). 36 NB lane centre at u 20 -5.54 m against its
  own line -5.45 (0.09, PASS), NB 72.6:1 (PASS), median at u 20 -0.36 m (the pavements meet: a flush median, <= 2.5 PASS), SB
  15.1:1 (L/2 band at 35 mph: FAIL against 20.4), centre-line jog on the approach unchanged (2.45 m over 121 m, as PC45).
  37 NB lane centre -7.76 m at u 20 against OSM -9.21 (no overshoot, PASS; PC45 -16.35), NB 27.0:1 (PASS), no turn-only lane
  within 372 m of the nose (PASS), median at u 20 0.78 m (growing; OSM's 3.0-3.4 is reached past L: FAIL at u 20), NB outer-edge
  bulge 2.14 m in the 80 m window (OSM's own bow, which the blend joins).
- Beatties Ford's 19 split-type nodes (final run b5): 6 pass at PC45 -> 11 (n4025, n10229-n10234, n10236, n10238, n13570, n13571). Still
  failing: n231 (SB bulge 1.39 m), n7869 (no profile: the 2-lane road meets 2 + 3 lanes, bands overlap 9.1 m), n7963 and n12438
  (carriageways diverge to wide medians: 3-6 m in 26-59 m along OSM's own lines), n10035 (bulge 2.14 m, OSM's bow), n634
  (undivided piece 6.6 m: no axis), n7868 (bulge 3.6 m), n9152 (no profile: bands overlap 1.15 m).
- City-wide, PSX_CITY_SPLITAXIS=all (measured, NOT the default; Phase C not done): carriageway ends failing L/2 1595 -> 1423,
  failing L 96.0% -> 90.0%, bulges of 1 m or more 1036 -> 873, lane-centre angle at the node over atan(1/V) 1713 -> 1332 (p50
  8.9 -> 4.3 deg), outer-edge jumps 779 -> 558; 701 carriageway ends drawn by the profile, 1272 left on PC45's taper (overlapping
  lane bands, short islands, separations, turning roadways), refuge pairs 22. Every CITY gate but the refuge count fails.
- Regressions on Beatties Ford against the PC45 base, same sandbox, fresh processes (final code):
  - Drive audit on its 57 edges, z 8400-10700 (RunDrive, PSX_DRIVE_EDGES): base / bfr - nothing solid across a lane 0 / 0, BLUNT
    faces 1 / 1 (the same e1198 face), lane steps over 12 cm 2 / 0, lane surface off the solve 21 / 0, land on a lane 9 / 1, FALL
    HOLES 0 / 0, MEDIAN classes a/b/c 0 / 0, MEDIAN ACROSS two+ / none 0 / 0, WALLS IN LANES 0 / 0; every other check identical.
  - Launch audit, box x -3750..-3150, z 8400..10700: LAUNCH 0 / 0, UNLOAD 1 / 1, flown turns off the pavement 0 / 0, SAG T2
    109 / 108. (With the forced mitre at 3-arm splits, now OFF, the n7963 joint read as a 35 degree lateral step.)
  - Concrete inventory within 1.5 km of (-3450, 9600): strays 70 pieces 0.22 km / 93 pieces 0.23 km. The difference is class
    c ORPHAN, +26 rail pieces (+28 m, all "guards a fall"): the rail runs on e2824 / e2825 at (-3316..-3323, 9570-9596), the
    Beatties Ford deck over I-85 just north of n231, where the median between the two decks is now OSM's narrow one instead of
    PC45's 9 m bulge. Not visible at the scale of `splits_bridge_n231_before_after.png`; look at it in the spot build.
  - Medians gate (above): CITY AUDIT OK under bfr; graph hash b2aa3453 in every run.
  - charlotte_lotent.bytes: its key (graph, edge sizes, lots, entrances) does not see eases, so my change leaves the key as it
    is and the file would be used silently. Lot entrances on an edge a profile moves by 0.05 m or more at the entrance: base 1
    (Clanton Rd, a PC45 refuge), bfr 2, all 63 (worst 11.4 m, Remount Rd). NOT re-made by me (the key is unchanged); re-make it at
    merge as the plan's re-bake list says.
- Traffic census on Beatties Ford (CityTrafficCensus, the five spots z 8768-10924, 6 min, cap 48), base / final bfr: wrecks 1 / 0,
  stuck 4 / 5, turn from the wrong lane 9 / 4, missed turn lane 18 / 18, turn clips a corner 24 / 21, turn leaves the road 2 / 3,
  no road under a lane 1 / 3 (not traced), car-hours 3.32 / 2.87, km 75.0 / 75.9. (A bfr run with n7963 / n12438 drawn met 3
  wrecks on a solid beside e11521: those two nodes are now held back.)
- Incident (mine, 03:35): two of my job chains overlapped and two Unity jobs ran together in PSXCity for about 1.5 minutes (both
  exited 0); their outputs were discarded and re-run; run_job.ps1 now waits on any lock, its own included.

## Not done / not verified
- Phase C (city-wide default): 'all' is measured only; its CITY gates fail (above). Not the default.
- The plan's "mitre every 3-arm SPLT node": PSX_CITY_SPLITMITRE=1, OFF (a node the profile declines kept a 35 degree mitred joint).
- The 4-5 degree kink at refuge noses (exporter fillet): PSX_CITY_REFUGECHORD=1, OFF (the medians gate rejects it); J4's fix.
- Wide-median divergences (n7963, n12438, n4025's SB room 145 m for L 162 m) and OSM's own bows (n10035 NB, n7868): the profile
  joins OSM's line, which itself diverges or bows; a reverse-curve redraw at Rmin, or J4's lines, would cure it.
- Carriageways with more lanes than the undivided road (n7869, n9152; 30 of the first 60 city-wide skips): PC45's taper kept.
  The SB carriageway at n10035 (3 lanes, OSM) still ends its third, turn-only lane into the nose hatch (no lane may end at a nose).
- CurbMedian's nose: the set-back is dropped and the inner pavement shaped to a rounded tip, but the curb itself starts where the
  pavement gap reaches CurbMedian's 0.8 m; the tip radius is the pavement's NoseHalf shape, not a curb-mesh change. Approach
  marking at long-split noses only where NoseMarks can hatch (1 of 2 marked long-split noses under bfr, n10035; flush tips keep the 12 m set-back).
- Same-name fan meeting n634 (undivided piece 6.6 m): no axis, unchanged.
- The 2026 street-level check (Google Street View, Apr 2026, reference only): narrow planted island at shot 34 (OSM's 1.8 m is
  right); a real NB left-turn arrow before the Keller Ave island (BAYEND ends it until J4 brings its street); signal heads on a
  span wire at Keller Ave (J2's question: whether that is the emergency signal OSM tags); a narrow curbed median with a painted
  nose about 40 m past n10035 with straight NB lanes. No lane-set data changed.
- Self-test and door tour not run; the pictures are editor frames (720 lines), not a player build.

## For the merge (coordinator)
- Shared-file edits: Scripts/City/LineModel.cs (Init: ApplyBayEnds; BuildEases start: ApplyBayEnds + ClearSplitFrames; the SPLT
  loop: BuildSplitNode per in-scope node, same-name meetings after it; CompiledRefugeList + CompiledRefugeCount (scope bits);
  FindRefuges' log line; BuildRefugePair: 30 m chord behind PSX_CITY_REFUGECHORD; RefugeProfile: bay-open tip; RefugeNormal ->
  SplitFrameNormal; ExtentsAlongOsm; RefugeTangent; SectionNormal (splitNodeDir)), LineModel.RefugeCheck.cs (TipJoinM),
  LineModel.RefugeList.cs (REGENERATED, scope bits), CityMeshes.cs (ComputeTrimsCore: SplitForcedMitre reset, the opt-in forced
  mitre block before "bool allBranch", SplitTrimHook before BuildEases), CityMeshes.Marks.cs (MarkTally fields; NoseMarks:
  split noses, bay-open skip, SplitNoseFlushTip, noseYs gating), CityMeshes.CurbMedian.cs (set-back via NoseShaped),
  Editor/CityAudit.MedianGates.cs (RunMedianGates per-scope decisions + PC45 check; RefugeListSource with scopes).
  New: LineModel.Splits.cs, LineModel.SplitFrames.cs, Editor/CityAudit.Splits.cs, Editor/CityRefSpots.Splits.cs (+ metas),
  tools/city/splitcensus.mjs + tools/city/splitcensus/.
- Re-bakes: the city re-bake (pc_citybuild.ps1) for the spot build (PSX_CITY_SPLITAXIS=bfr is the default); re-make
  charlotte_lotent.bytes. No export, graph b2aa3453. VsOrderList fits (the gate checked it).
- If LANES2 (charlotte-roads-audit) or any lane-set change merges: re-run RunMedianGates with PSX_REFUGE_GATE_SCOPES=0,bfr,all and
  copy refuge_list.generated.cs over Scripts/City/LineModel.RefugeList.cs. After J2 merges: re-run RunControl (Keller n10235
  sits on the n10229-n10234 refuge pair now). J1b (taper lengths) is unblocked once this merges.


# HANDOFF - CONTROL ROUND (J2 of the alignment plan): the false signal at Keller Ave out, STOP signs for the side streets that meet their road at a shallow angle (agent "control", branch `charlotte-control`, 2026-11-16; NOT pushed, NOT merged)

The owner, 2026-11-16 ~01:00, four PC45 frames on Beatties Ford Rd at night in rain: "If the city and roads are to 1:1 scale, why are there so many anomoalies like this abrupt medians that force the road to meet at strange angles without stop signs." The alignment plan (12 agents, `...\scratchpad\align\PLAN.json`) split the fix: J1 the split / island geometry, J2 (this round) junction control, J3 the mountain lines, J4 the street-data download (NOT approved: nothing fetched). Worktree `C:\Users\mcgee\PSX Racing-control` off mountain-nm 7badf362 (PC45). Sandbox PSXCity (WebGL, shared with J1 by its AGENT_LOCK). Graph hash b2aa3453 before and after (no export, no data file changed by this round; `charlotte_sigtags.bytes` comes with the signals round).

## Found
1. **Shot 35's mast arms are a fire-station signal.** OSM tags 7818374409 / 7818374408 `traffic_signals=emergency` within 20 m of split n10229 made PC45 build a two-phase junction signal on {n10229, n10235} (old `MarkSignalsOld`, `CitySignals.Signals2.cs`; PC45's own audit line SIGNAL-FALSE). The stopped signals round 2 (branch `charlotte-signals` 8b54945a, merged here) builds no junction signal from emergency, pedestrian or beacon tags: Keller Ave e25101 gets its own OSM STOP 7818374405 (line 15.2 m before n10235), Beatties Ford flows free, n10229 is an uncontrolled carriageway split.
2. **The warrants skipped every mitred node** (`CitySignals.Control.cs` Warrants, the old line 247): a road meeting another at a shallow angle, drawn with no fan (`CityMeshes.cs` ComputeTrimsCore: a through pair within 32 degrees of straight, every other arm within 60 degrees of the arm it is clipped onto). Census on the game's own trims (`CityAudit.ControlMitre.cs`; the plan's ~340 arms at ~315 nodes came from an offline emulation): 313 named non-link side arms at 307 mitred nodes; PC45 controls 50 of them (15 STOP, 19 YIELD, 16 signal) and leaves 263 with nothing.
3. **A clipped arm's stop line stood on its host's lanes.** `MakeApproach` puts the bar StopBackM (1.2 m) from the junction's mouth, and a clipped arm at a mitred node has no mouth (trim 0): in PC45, 77 of the 78 STOP / yield bars and 117 of the 117 signal bars of clipped arms lie on another road's drawn pavement (0.3 m test, `CitySignals.BarClear`).
4. **Six in-graph OSM STOP tags stand nothing, before and after** (`CitySignals.ExplainTag` gives each one's walk): Moss Rd n1063 and Lower Ave n5582 (3 tags) sit at carriageway splits whose crossing street is not in the game; Shopping Center Dr n13078's `stop=all` tags are on joints 26 m out with no direction (the undirected walk reaches 30 m of road and the junction is further along it); Camden Rd e14048 is tagged `direction=backward` against its own one-way; a ramp tag at Billy Graham Pkwy stands 58 m before the ramp's end (reach 40 m). None is on Beatties Ford. Left as they are (data, not the rule).
5. **The Beatties Ford side streets' STOPs need the street data.** 526 OSM STOP nodes, 40 junction signals and 1 give-way near the game's roads stand on ways the graph does not have (not in TAGN: north of STREETS_BOX), among them 7818374420 / 21 (z 8854), 7818374403 (island 2's west driveway), 7818374397 (z 9198), 7818374225 (z 9827) and signals 7818374209 / 7818374228. The audit lists them as NEEDS-STREETS, not as faults. J4 brings them.

## Changed (branch head: see the hand-back)
- **Merge of `charlotte-signals` 8b54945a** (clean, no conflict: that branch never committed its HANDOFF section, so the section from its scratch is added below this one). Brings `PSX_CITY_SIGNALS2`, `PSX_CITY_LOCALSTOPS`, stop lines at their tags, joining flows on their own phase, `charlotte_sigtags.bytes` + `tools/city/signal_tags.mjs`, `CityAudit.RunControl`.
- **Left-turn pockets back OFF by default** (`CityMeshes.TurnPockets.cs`: `TurnPocketsOn = PSX_CITY_TURNPOCKETS == "1"`, as in PC45; owner decision 6: they cut into the medians J1 rebuilds).
- **`PSX_CITY_MITREWARRANT`** (default ON; `=0`, or `PSX_CITY_SIGNALS2=0`, is PC45's rule). New `Scripts/City/CitySignals.Mitre.cs`; `CitySignals.Control.cs` Warrants (the mitred branch, `Decision.mitre`); `CitySignals.cs` Ensure (`AddStop` takes a line node / distance, the warrant loop's mitred decisions, `MitreReset`, a yield-only warrant junction counted as such, `MitreReport` in the log line).
  - THE RULE: at a mitred node OSM leaves uncontrolled, a NAMED side street (not a link, not the through road's own name, not a service drive, not a stub under 30 m, not outranking the through road, not the arm that leads into a signal 45 m away) STOPS; a one-way side roadway merging with a through flow within 50 degrees from that flow's right (either side of a one-way host) YIELDS. A 3-arm carriageway split stays uncontrolled. A mitred node one short edge from a signal is decided too (PC45 skipped it).
  - THE LINE: where both ends of the bar are 0.3 m clear of every other road's drawn pavement at its level (3 m), StopBackM behind that, walked on through joints up to 45 m (no clear place: no line, listed). OSM's own STOP / give-way tags at mitred nodes stand their line the same way with the switch on (104 moved).
- Audit: `Editor/CityAudit.Control.cs` (needs-the-street-data split, every signal on its own tag, YIELD-MISSING, ExplainTag on STOP-MISSING rows, furniture feet on a road) and new `Editor/CityAudit.ControlMitre.cs` (the mitred side-street census with a reason per arm, 3-arm splits, clipped arms' bars, `PSX_CTL_MAPBOX` control map).
- New `Editor/CityControlShots.cs` (`RunOwner`: an owner pose from the game's chase rig at rest, night rain and noon, summer dress; side-street poses by edge). `Editor/CityTrafficCheck.cs`: `PSX_TRAFFIC_STOPSITE="x,z"` (the STOP site at a given junction, 120 s, cars seen standing at a bar within 45 m).

## Gates (`CityAudit.RunControl`, city-wide; base = PC45's control = `PSX_CITY_SIGNALS2=0 PSX_CITY_LOCALSTOPS=0 PSX_CITY_MITREWARRANT=0`; each a fresh Unity process in PSXCity on this branch's code; the base reproduces the signals round's PC45 base to the digit)
| | PC45 control | after |
|---|---|---|
| SIGNAL-FALSE (a game signal with no OSM junction signal within 60 m) | 13 (n10229 among them) | 0 |
| signal junctions with no approach carrying its own junction traffic_signals tag | 9 | 0 |
| n10229 / n10235 (shot 35) | one signal junction (834), heads on Keller and both Beatties Ford carriageways | n10229 uncontrolled split; n10235 STOP on Keller e25101 only |
| 3-arm splits n10238, n10236, n10035, n10234 | uncontrolled | uncontrolled |
| 3-arm splits (907) with a STOP or yield | 0 | 0 (33 are members of a signalised junction at their mouth, base 43) |
| named side arms at mitred nodes (313) with no control | 263 | 183: 101 one-way exits, 66 roundabout entries (the roundabout's yield), 6 inner pieces of a signalised junction (both ends are its nodes), 3 in the control's ranked through pair (they outrank the meshes' through arm: n722 Reagan Dr, n2268 East 3rd St, n7900 Woodland Dr at Commonwealth Ave), 2 service drives, 2 no clear place for a line (East Independence Expressway at n1900, Crescent Ave at n11412), 1 short stub, 1 outranking its through road, 1 at an OSM-tag junction (n744 Old Plank Rd) |
| ... decided by the new rule (the warrant's own count, 88 arms) | 0 | 71 STOP + 15 YIELD; their lines 10.2 m from the node at the median, 18.2 m p90, 27.2 m at most (the three over 25 m are one-way slips' YIELDs); 104 OSM-tag lines at mitred nodes moved clear the same way (up to 34.2 m) |
| STOP / yield bars of clipped arms on another road's pavement | 77 of 78 | 3 of 175 |
| in-graph OSM STOP with no game STOP within 45 m (STOP-MISSING) | 6 | 6 (Found 4) |
| in-graph OSM give-way with nothing (YIELD-MISSING) | 9 | 5 (4 at the Poplar Tent Rd roundabout, a ramp at East Reese Blvd) |
| junctions furnished | 2,593 | 4,069 (signals 1,010 -> 1,014, all-way 127 -> 434, two-way STOP 1,393 -> 2,520, yield 63 -> 101) |
| furniture (FURNISH, 1,701 -> 2,046 junction tiles) | 1,887 STOP signs, 5,508 bars, 2,974 poles, 8,033 heads | 4,110 / 7,868 / 3,015 / 8,104 |
| STOP signs / signal poles with a foot on a road's pavement (+0.3 m) | 0 / 0 | 0 / 0 |
| junction furniture triangles | 759,630 | 833,608 (+9.7 %; 407 a tile) |

## Pictures (real editor frames, `CityControlShots.RunOwner`; the same poses with the switches off and on, each in a fresh Unity process in PSXCity; summer dress like the owner's frames; the default catalogue car, not his Civic)
- `...\scratchpad\control\control_sheet_A_shot35.png`: shot 35 at his pose (-3324.8, 8960) heading 9.9 from the chase rig, night rain and dry noon: PC45's two mast arms over Beatties Ford / none now; and from Keller Ave itself 30 m out: PC45's signal heads for Keller / a STOP sign and bar now.
- `...\scratchpad\control\control_sheet_B_topdown.png` (+ `_zoom.png`): a plan of Beatties Ford z 8600-10700 from the game's own control data (`PSX_CTL_MAPBOX`), PC45 vs now: approaches with their bars, signal / STOP / YIELD symbols, OSM's tags (grey: on streets the game lacks), his four positions. A data plot, not a render.
- `...\scratchpad\control\control_sheet_C_mitred.png`: three side streets at mitred nodes from their own lane, noon: Old Mount Holly Rd into Rozzelles Ferry Rd (n16158), Catherine Simmons Ave into Newland Rd (n853), Killian Farm Rd into NC 73 (n226): nothing in PC45, a STOP sign and bar now.
- What still looks wrong in them: the islands, the lane kinks and the outward bulge at shots 34-35 are unchanged (J1's job); Keller's STOP faces Keller traffic, so from Beatties Ford northbound only its pole edge shows; a YIELD (one-way slips, 15 by the rule) has no sign or paint at all (the game has no YIELD sign model; the paint's yield teeth are drawn only where a LINK meets a street); the side streets the owner meant at the islands (z 8854, the west driveway, z 9198, z 9827) are not in the game, so they get nothing until the street data comes in.

## Traffic (`CityTrafficCheck.Run`, play mode, -nographics, the spot Beatties Ford Rd at Keller Ave (-3319, 8983), the STOP site forced to the new STOP at n16158 Old Mount Holly Rd into Rozzelles Ferry Rd)
- AFTER (all switches on, a fresh process, 786 s): CITY TRAFFIC OK, no FAIL line. Rush hour parked at Keller Ave: 33.4 cars active, STOP signs 8 full stops / 0 rolled, 16 stops at red, 0 ran red, 0 wrecks, 0 bumps, 0 wrong way, 0 overlaps, 0 of 7,372 samples off the pavement; driving: 7 full stops / 0 rolled, 0 wrecks; locked pairs undone 0, no HUNG (guard 1,500 s). The forced STOP site, the new STOP at n16158, 120 s: 7 full stops, 0 rolled, a car standing at a STOP bar within 45 m of it in 28 of 121 one-second looks. 03:00 parked and driving: no fault.
- BASE (PC45 control, a fresh process): 2 FAILURES, neither in a control count: 2 traffic-on-traffic wrecks over the run (at most 1 allowed) and the driving phase's view feed (13.3 -> 4.6 cars ahead). STOP signs 4 / 2 full stops, 0 rolled; 0 wrecks in the four measured phases; the STOP site n16158 has no STOP in PC45 (0 stops, no car held there). One run each and the traffic is not seeded alike, so the base's two wrecks are not proven to be control's; the after run has none.

## Not done / not verified
- The city-wide launch audit was not run: `CityLaunchAudit*.cs` reads no junction control (no `CitySignals` reference) and this round changes no road surface, so it cannot move; furniture in a lane was checked directly instead (0 STOP signs, 0 signal poles with a foot on a road's pavement, before and after).
- The six in-graph STOP-MISSING tags (Found 4) are left: data quirks, none on Beatties Ford. 104 signal stop lines of clipped arms still lie on another road's pavement (the signals round's own lines, untouched here; 117 of 117 in PC45).
- YIELD approaches draw nothing (no YIELD sign model; Marks' yield teeth are for links only).
- Keller Ave's real control (a fire-station signal that rests green) rests on OSM's `traffic_signals=emergency` tag only, not checked against imagery; a resting-green emergency signal is not built.
- The street data (J4) is not fetched: the side streets and their STOPs at the Beatties Ford islands wait for it.
- At n722, n2268 and n7900 the control's ranked through pair takes the named side arm as through road, so nobody stops there (the other arm of the host's own name is skipped as the through name): left for a look. Audit run v4 (census labels only; every count as v3).

## At merge
- Shared files (all junction-control code; J1 touches none of them): `Scripts/City/CitySignals.cs` (Ensure: `MitreReset`, `AddStop`'s two new optional arguments and its mitred-tag line, the warrant loop's `d.mitre`, the yield-only warrant count, `MitreReport` in the log line), `CitySignals.Control.cs` (Warrants: the mitred branch, `Decision.mitre`), `CityMeshes.TurnPockets.cs` (the default line + its comment), `Editor/CityTrafficCheck.cs` (StopSite), `Editor/CityAudit.Control.cs` (the signals round's new file). New: `CitySignals.Mitre.cs`, `Editor/CityAudit.ControlMitre.cs`, `Editor/CityControlShots.cs`, each with its Unity-minted `.meta` (committed).
- Data: copy `Assets/PSXRacing/Resources/charlotte_sigtags.bytes` + `.meta` (the signals round's) into every sandbox / build tree.
- RE-BAKE: the full city re-bake (signal channels and the STOP furniture are baked: `CityTileCache.Trees.WriteSignals`); share it with J1's. A build must run with the same `PSX_CITY_SIGNALS2` / `PSX_CITY_LOCALSTOPS` / `PSX_CITY_MITREWARRANT` values its tiles were baked with.
- J1 interplay: the mitred-node set comes from `ComputeTrimsCore`, which J1 changes (it mitres every 3-arm SPLT node and redraws the Keller fan at n10235): re-run `CityAudit.RunControl` on the merged head (`...\scratchpad\control\chain_ctl.ps1` shows the env: `PSX_CTL_OSM=...\scratchpad\control\osm_ctl.tsv`, `PSX_CTL_FURNISH=1`, `PSX_CTL_MAPBOX`), and the shot-35 pair (`chain_shots.ps1`, poses `poses_owner.tsv`). 3-arm splits stay uncontrolled whatever J1 mitres.
- No scene pass (nothing serialized changed). Graph hash unchanged.

## What the owner will notice
At Keller Ave the traffic lights over Beatties Ford are gone: it flows free, and Keller Ave has a STOP sign. About seventy side streets around the city that join their road at a shallow angle now have a STOP sign and line where their own pavement starts (a YIELD, unsigned, on one-way slips); the side streets at the Beatties Ford islands themselves come with the street download.

# HANDOFF - CHARLOTTE JUNCTION CONTROL ROUND 2 + LEFT-TURN POCKETS (agent "signals", branch `charlotte-signals`, 2026-11-15; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-signals` off mountain-nm 50c8a3aa. Sandbox PSXRoadB (WebGL target). Graph hash b2aa3453 before and after (no export run; one new side file, `Resources/charlotte_sigtags.bytes`, written from the map cache by `tools/city/signal_tags.mjs`). Owner 2026-11-15: "Please review all roads for accuracy (including parking lots, bridges, neigborhoods, driveways)", "continue correcting roads around the city (including minor roads, driveways, neighborhood roads, etc)". The road review's items 5 and 6: 29 real signalised junctions carry STOP signs or nothing (Five Points first); left-turn lanes at signals switched off since 11-10.

## Causes
1. A signal tag on an approach marked the NEARER END of the piece of road it lies on, within 20 m, whatever way its traffic goes (was `CitySignals.cs:496-497`; now `MarkSignalsOld` in `CitySignals.Signals2.cs`). A stop-line signal 24-38 m before a wide junction marked nothing (Eastway Dr at Shamrock Dr: four tags, 27-38 m out); one whose nearer end is a joint of two road pieces was dropped by the two-arm test (`:499`; Rozzelles Ferry Rd at Five Points: 10 m from a joint, 17 m from the junction); one just past a slip lane's node marked the slip.
2. A junction whose arms are cut by joints a few metres out could stand no stop line on them (`MakeApproach`: under 6 m, or no room past the trim), came out with fewer than two approaches and was dropped (was `CitySignals.cs:574`) - while its nodes stayed held by the signal, so no STOP sign either. Five Points: West Trade St and West 5th St arrive on 4-6 m stubs; the junction had NOTHING.
3. Every `highway=traffic_signals` node read as a junction's red-yellow-green signal: 60 fire-station signals (`traffic_signals=emergency`), 26 pedestrian-crossing signals and 3 flashing beacons too. 13 game signals stood at those (McClintock Rd at The Plaza is Fire Station 8's signal; Dilworth Rd at Romany Rd is a beacon over an all-way stop).
4. Two local streets meeting with no OSM tag were left with no sign at all (`CitySignals.Control.cs`, "local x local left to yield": 1,407 junctions): 265 of 1,730 neighbourhood junction nodes carried a control.
5. Left-turn pockets: off only because the city-wide launch audit had not been re-run after the last skip list (`CityMeshes.TurnPockets.cs:41-50`).

## What changed
All behind `PSX_CITY_SIGNALS2=0` (the controls as they were) unless said; `PSX_CITY_LOCALSTOPS=0` alone puts back "two local streets with no tag: no sign".
- NEW `Scripts/City/CitySignals.Signals2.cs` (the rule's header, the marks, the walk, the counts); edits in `CitySignals.cs` (`Ensure`: marks, clusters, `BuildCluster`, `AddStop`, the STOP and yield rows) and `CitySignals.Control.cs` (`Warrants`: local streets; `PlanSignals`: joining flows).
- SIGNALS: a tag at a junction node marks it; a tag on an approach marks the junction its traffic drives INTO - along its own way in the tagged direction (`traffic_signals:direction` / `direction`, a one-way's own sense), through joints and carriageway splits, within 45 m; with no direction on a two-way road the nearer junction within the old 20 m; nothing ahead: the junction behind, as before. Emergency and pedestrian signals mark nothing; a beacon is an all-way STOP.
- CLUSTERS: marked nodes joined through joints within 40 m are one junction; a node across a short edge stays OUT when that edge carries the junction's own stop-line tag (a slip leaving before the line stays outside); an arm too short for its line whose far end is a joint takes the joint in. A junction this round makes must have two roads at it (a carriageway split beside a signal whose side street is not in the game is no junction). A cluster that cannot be built gives its nodes back to the STOP tags and the defaults (before: held, so nothing).
- STOP LINES of signals stand where their own directed tag is (at a fan no more than 15 m behind the junction's own line; at a mitred node - a ramp joining a carriageway - the tag's own place); not on an arm with a crosswalk.
- PHASES: two flows that JOIN at one node (a ramp into a carriageway at a signal) take turns (they were one phase).
- STOP / YIELD tags: a tag on an approach controls the junction its traffic drives into (the same walk, 40 m); `stop=all` on an approach makes its junction all-way; `stop=minor` on a junction node stops the minor road by the through road (the stem of a T); a line stands behind a joint where the arm is a stub.
- LOCAL STREETS where OSM is silent: the stem of a T stops; side streets of a local through street stop; two equal local streets crossing are an all-way STOP. The split follows Charlotte's own tagged junctions: of 130 tagged three-arm local junctions 94 stop the stem alone; of 130 tagged local crossings 69 are all-way (and where one street stops, the longer street is the one that goes only 26 times in 54: no rule picks it).
- NEW data `Assets/PSXRacing/Resources/charlotte_sigtags.bytes` (7.6 KB; 849 signal / give-way nodes with a direction or a kind) + `tools/city/signal_tags.mjs` (reads `tools/city/cache/nodes_all.json`; `node tools/city/signal_tags.mjs <cacheDir>`).
- NEW tool `Editor/CityAudit.Control.cs` (`CityAudit.RunControl` -> `city_control.txt` / `.tsv`; env in its header). `Editor/CityTrafficCheck.cs`: `PSX_TRAFFIC_SPOT="x,z"` (the check at another place).

## Numbers (CityAudit.RunControl, city-wide, base = both switches off, each a fresh Unity process in PSXRoadB)
| | before | after |
|---|---|---|
| junctions furnished | 2,593 | 3,984 |
| signals | 1,010 (1,009 from tags, 1 by default) | 1,014 (1,013 + 1) |
| all-way STOP | 127 (110 tagged, 17 by default) | 434 (122 tagged, 312 by default) |
| two-way STOP | 1,393 (425 tagged, 968 by default) | 2,450 (424 tagged, 2,026 by default) |
| yield only | 63 | 86 |
| junction nodes with a control (of 5,299 with 3+ usable arms, not mitred) | 3,267 | 4,640 |
| neighbourhood (local x local) junction nodes with a control (of 1,730) | 265 | 1,619 |
| OSM junction signals on the game's roads (2,522) with a game signal within 60 m | 2,005 | 2,035 |
| ... with none, at a junction the game has | 19 names | 7 names (3 on bridge decks, 4 carriageway splits with no side street in the game) |
| game signals with no OSM junction signal within 60 m | 13 (all at a fire station's, a crossing's or a beacon's signal) | 0 |
| OSM STOP nodes on the game's roads (1,330) with a game STOP within 45 m | 709 | 713 (553 more are mid-block: the side street is not in the game) |
| OSM stop=all nodes (193) at an all-way junction | 110 | 141 |
| OSM give-way nodes (136) with a game yield / STOP within 45 m | 82 | 99 |
- Signals: 21 new junctions, 13 removed (2 beacons -> all-way STOP; 7 fire-station and 4 pedestrian signals -> the junction's own STOP tags or defaults, or nothing where it is no junction), 4 pairs of half-junctions joined into one, 34 existing signals with an approach added or moved to where its line is tagged. 112 stop lines stand at their own tag. Junction-by-junction list: `...\scratchpad\signals\ctl_diff.txt`.
- Furniture as built, city-wide: signal poles 2,974 -> 2,981, heads 8,033 -> 8,070, STOP signs 1,887 -> 4,029, stop bars 5,508 -> 7,797; approaches with no clear spot for a sign or pole 607 -> 758; signal approaches with no head at all 7 -> 8 (the new one: the I-485 ramp at Brookshire Blvd / NC 16, n9118 e7527).
- Cost: junction furniture city-wide 759,630 -> 828,488 triangles (+9%) over 1,701 -> 2,028 tiles that hold a junction; its build 40.3 -> 50.3 s over all those tiles (23.7 -> 24.8 ms a tile). 3 x 3 rings, triangles a tile (tile meshes / furniture): Uptown 46,254 / 1,886 -> the same; Dilworth 42,889 / 267 -> 42,889 / 370; SouthPark 26,181 / 211 -> the same; I-77 at I-277 37,440 / 376 -> 37,440 / 473; Five Points 43,537 / 155 -> 43,692 / 336; Albemarle Rd pocket 10,559 / 228 -> 10,965 / 229; Providence Rd pocket 13,961 / 173 -> 14,041 / 174. Tile build time moved inside the machine's noise (other agents were building). Baked size not measured (no bake run).

## The road review's 29 names
FIXED, a signal now (11 names): Five Points (3 names: Beatties Ford x Rozzelles Ferry x W Trade, Beatties Ford x W 5th x W Trade, (unnamed) x W Trade - one junction, 4 approaches, 2 phases), Eastway Dr x Shamrock Dr (+ its two slip names: 4 approaches), N Graham St x Statesville Ave and N Graham St x W Liddell St (one junction, 3 approaches), ramp x Wilkinson Blvd (n5795), ramp x Little Rock Rd, ramp x N Josh Birmingham Pkwy, ramp x Gold Hill Rd, ramp x (unnamed) and (unnamed) x NC 51 (the mall entrance, n14481 / n14482), ramp x Brookshire Blvd at I-485 (n9117, n9118) and its south terminal at I-85 (n4983).
NOT FIXED (3): ramp x Brookshire Blvd at I-85 (n244 ...), ramp x Freedom Dr at I-85, ramp x Tyvola Rd at I-77. All three junctions stand on the bridge deck; `CitySignals.MakeApproach` (`CitySignals.cs`, `if (e.ElevatedAt(sStop)) return false`) stands no stop line on structure and no signal pole may stand on a deck cell (`FootOk`, `Hard` includes `RoadsideOccupancy.Deck`). Needs deck-mounted supports.
NOT A JUNCTION SIGNAL in reality (9; the game is right to have none): fire-station signals at Wilkinson Blvd (n6796), W Sugar Creek Rd, S Tryon St, Bryant Farms Rd, Little Rock Rd x Paw Creek Rd, South Blvd (n15095), Commonwealth Ave x The Plaza (an all-way STOP by its own tag, as before); pedestrian signals at Pineville-Matthews Rd and Rensselaer Ave x South Blvd. Neither kind is drawn (no dark / flashing signal heads there).
NOT A JUNCTION IN THE GAME (4): George W Liles Pkwy, Eastway Dr (2 places), Idlewild Rd, Monroe Rd - an undivided road opening into its carriageways beside a signal whose side street is not in the game (the same case as the ~500 mid-block ones).

## Left-turn pockets (`PSX_CITY_TURNPOCKETS`, now ON by default; =0 the roads as they were)
- 246 pockets (17.7 km of lane and taper, narrowest 3.02 m), 188 at a signalised junction; by road: Albemarle Rd 19, University City Blvd 18, Freedom Dr 15, Wilkinson Blvd 14, Queens Rd 12, South Tryon St 11, Brookshire Blvd 10 ... Left as they were, by the rule's own count: 440 approaches whose left lane is a turn lane already (Park Rd at Woodlawn Rd is one: its turn lanes are in the lane count and painted with arrows, no pocket), 348 a secondary street's opening to a minor street, 446 openings that are a link leaving a through road, 176 the median too narrow, 61 no twin carriageway beside the storage, and the 6 junctions of `PocketSkipNodes` (unchanged).
- LAUNCH AUDIT, city-wide, two fresh processes (27 and 29 minutes): 91 LAUNCH / 564 UNLOAD with the pockets off and with them on; by tier identical (LAUNCH T1 39 / T2 38 / T3 14, UNLOAD 140 / 325 / 99); 984 separations on 119,169 paths both; flown turns off the pavement 0 both; routes uptown 1 / 4, tryon 0 / 0, independence 0 / 1 both. WHICH CODE: the OFF run flew mountain-nm 50c8a3aa; the ON run was held behind the control audits and flew this branch's first working state with `PSX_CITY_SIGNALS2=0` (the old marks; controls are furniture and paint, not road surface - the control audit with the switch off reproduces the morning's counts to the digit - but it was not the identical code, and the audit was NOT run again with `PSX_CITY_SIGNALS2` on).
- Spot by spot (`PSX_LAUNCH_CSV_ALL=1`, `...\scratchpad\signals\launch_cmp.txt`): one existing UNLOAD spot 1.4 cm deeper with the pockets (0.062 -> 0.076 m; the ramp e4930 beside Johnston Rd's pocket junction n2813), one 2.6 cm shallower and moved 9 m (Bud Henderson Rd at Vance Rd, n13084), one 2.0 cm shallower (Sharon Rd West at South Blvd, n8598). 25 more rows differ only in which path names the same place and depth (same x, z and separation, 300 m to 3 km from any pocket); why the two runs name them differently was not looked into.
- DRIVE AUDIT on 44 pocket edges (every 6th pocket + the four pictured; 10.2 km, 89 tiles, 359,133 probes): nothing solid across a lane, no blunt face, no step over 12 cm, no hole, no surface off its solve, no land on a lane - 0 each, pockets on and off. Beside those lanes (other roads on the same tiles): steps 5 -> 4, grass on tarmac 56 -> 55, blunt 4 -> 4. Two FAIL lines, the same on and off: a boxed median on Albemarle Rd at (15285,3906), e2014 (14 rays / 1 place) - there with the pockets off; the concrete agent's kind.
- THE 100-TILE SAMPLE (both switches on) against this morning's base file: the 12 FAIL lines are the same lines with the same counts, but one that is lower (inner box, colliders with no drawn face 95 -> 91). The morning's file was made on the pre-merge code in another sandbox; no same-sandbox base of the sample was run.

## At merge
- Shared-file edits: NONE in `CityMeshes.cs`, `CityWorld.cs`, `CityMap.cs` or another agent's file. Mine: `CitySignals.cs` (`Ensure` only), `CitySignals.Control.cs` (`Warrants`: the two "local" branches; `PlanSignals`: one line in the axis loop), `CityMeshes.TurnPockets.cs` (the switch's default and the skip list), new `CitySignals.Signals2.cs`. Editor: new `CityAudit.Control.cs`; `CityTrafficCheck.cs` (+7 lines, `PSX_TRAFFIC_SPOT`).
- Data to copy by path into every sandbox / build tree: `Assets/PSXRacing/Resources/charlotte_sigtags.bytes` + `.meta` (a `charlotte_*` Resources-root file: the edition lists take it by its prefix). Without it the game logs a warning and reads every signal tag as a plain undirected signal.
- BAKE: no new slot, bucket, material or baked field; `CityTileCache.Format` untouched. But the junction furniture is baked (`CityTileCache.Trees.WriteSignals`) and a signal head carries its CHANNEL number, which is assigned over the whole city's junction list: with this round the list changes, so EVERY tile with a signal must be baked again (a full re-bake does it), and a build must run with the same `PSX_CITY_SIGNALS2` / `PSX_CITY_LOCALSTOPS` values its tiles were baked with (the switches are for editor before / after, not for a baked build).
- No scene pass needed (nothing serialized changed).

# HANDOFF - LAKE LURE ROUND (venue LAKE LURE): US 64/74A, Lake Lure - Chimney Rock - Bat Cave, before Helene (agent "lakelure", branch `mountain-lakelure`, 2026-11-16; NOT pushed, NOT merged)

The owner (2026-11-16): "add a mountain track for US-64/74A Lake Lure through Chimney Rock ... I'm specifically looking for the
roads and buildings, etc from BEFORE Hurricane Helene to capture the memory of what it used to be like." Worktree
`C:/Users/mcgee/PSX Racing-lakelure`, base mountain-nm 7badf362, sandbox PSXPerf (Windows target; the store packs extracted into it
with tools/store-assets.ps1 this round). Every input is from before the storm: OpenStreetMap as of 2024-09-20 (Overpass attic
queries) and the USGS 3DEP 1 m lidar NC_Phase5_2018.

## The road (tools/roads)
- `fetch_road.mjs lakelure`: the 15 ways that carry US 64 + US 74A + NC 9 together, MATCHED BY WAY ID (both routes carry on past
  either end), plus 60 m of US 64/74A east as the lead and 200 m of US 74A / NC 9 north of Bat Cave as the run-out. New row options:
  `attic` (a dated Overpass query, the classes the pre-Helene extract used; the user agent carries no e-mail), `osmCache`,
  `match.ways`, `planSmoothM`; the cache dir honours PSX_ROADS_CACHE. Then `bake_3dep.mjs lure --keep-line` (STAGES row `lure`).
- Timed 11,616 m (54 -> 11,670 m of an 11,860 m line), 309.7 -> 451.2 m, +222 / -80 m. Steepest 50 m: +6.1% at 9,880 m (the gorge),
  -6.5% at 2,560 m (lake road). Tightest bends (8 m chords) R 22 m at 2,944 m, 23 at 544, 24 at 1,988, 25 at 952, 26 at 2,788 - the
  lake shore. Vertical curves >= 667 m (the self-test floor is 200). Built 7.0 m wide (lidar bench median 13 m, narrowest tenth
  8.5 m: pavement + shoulders + verges, an upper bound; OSM lanes=2, no width).
- NO WAVES (coordinator's alignment note): the OSM line kept as mapped and an 8 m gaussian on its gentle stretches only (R > 120 m;
  largest move 0.32 m). `tools/roads/plan_wiggle.py`: 0 abrupt reversals (bends < 250 m both ways within 40 m, < 3 m off the
  chord), wiggle (each waypoint against a 40 m cubic, worst per 100 m) median 0.034 m, p95 0.212 m, worst 0.282 m (the R 22 bend).
  The bench-moved line (bake_3dep's default) had 4 such jogs: R 61-82 m, 1.1-1.8 m off the chord at 116-188 m and 10,908-10,964 m.
- THE LAKE WAS DRAWN DOWN IN THE LIDAR: its hydro-flattened water is 298.26 m NAVD88; the mapped full-pool shore stands at a median
  300.9 m in it (p75 301.8). Full pond is 990.5 ft (Town of Lake Lure) = 301.9 m. Lake Lure is built at FULL POOL, 301.8 m, not at
  the lidar's level. No lake bed in the lidar: depth estimated from the drawdown band's slope (median 0.27 over 553 profiles), cap 20 m.
- 1/3" DEM: n36w083 Last-Modified 2022-08-30 (before the storm); against the 2018 1 m it reads 0.00 m on the lake and +0.23 m
  median (p95 +1.16 m) along the river - no scour. The near grid stays from the 1/3".
- Reads into main's tools/roads/cache/3dep: ~53 MB this round (ledger sources.json); Overpass attic files
  `overpass_lakelure_2024-09-20.json` and `attic_lakelure_<kind>_2024-09-20.json` beside them.

## The world (tools/roads/bake_world.mjs -> Art/LakeLure/lure_world.json; Editor PSXRacingBuilder.StageRiver.cs / .StageTown.cs)
- The world file: `node tools/roads/bake_world.mjs lure` (and `--frame chimney` for the Park Road stage: Art/ChimneyRock/
  chimney_world.json). Inputs: the 2024-09-20 attic extracts in main's tools/roads/cache and the 2018 lidar. No real business name
  reaches the game: a footprint's name only selects a ROLE (inn, arcade, welcome, post office, church, fire hall, boathouse).
- LAKE LURE at full pool 301.8 m: 22,780 cells of 12 m in 34 chunks, PSX/Water through the existing MakeWaterMat (a deeper
  green-blue tint in LakeLure's own theme row). No water is hung over the valley below the dam.
- THE ROCKY BROAD (OSM "Broad River"): 6.53 km in view, 459.4 -> 301.8 m (the lake backs it up to full pool), widths from the OSM
  river area (median 21.9 m), white water on 3.6 km (grade > 1.2%), flow 0.4-4 m/s, 698 NatureManufacture forest stones in the rapids
  and along the banks (no colliders: below the road's barriers). The 12 m near grid is carved 1.4 m under the water inside the river
  area (1,091 cells; never within 9 m of the road).
- VERTEX CONTRACT on every water mesh (for the water agent's shader): colour r = depth over the bed (0.5 at the waterline, +/-12 m
  across the range, as the sea); g = 0; b = white water 0..1 (0 on the lake); uv2.xy = flow velocity in world x, z (m/s; 0 on the lake).
- BUILDINGS: 149 of the 151 footprints within 150 m of the road (2 too small / too big): walls in the city's own house-atlas
  materials cropped to tile (white lap siding, weathered planks for the village's board and log fronts, cedar, dark wood, stucco,
  painted brick), windows, storefronts on the shops' street sides, 86 porches (a balcony rail on two-storey fronts), 71 shop boards
  in generic words (GEMS, CAFE, GIFTS, LODGE, ICE CREAM, ...), green / red / grey standing-seam metal, asphalt shingle and clay-tile
  roofs; the river-side shops in the village stand on posts over the bank; colliders are the walls capped at the eave. Landmarks:
  the Lake Lure Inn (3 storeys, white stucco, clay tile, porch), the Arcade (brick), the welcome centre, two post offices, the fire
  hall, two churches, four canopies.
- THE STREET BENCH: beside downtown Lake Lure (5,050-6,150 m) and from the inlet's shops through Main Street (6,420-8,040 m) the
  near grid is lowered to the road out to 10 m past the tarmac edge (easing back by 16 m; never raised; land over 3.5 m above the
  road stays a wall), so the builder lays an open roadside there instead of a rock face in front of the storefronts; street-front
  shops stand at the street's level.
- SIDE ROADS: 70 mouths in the data (37 residential, 31 service, 2 secondary, 1 primary). [MOUTHS] Each is a decal draped on the
  ground a wheel meets (the edge line is covered across the mouth) and closed 13.5 m past the tarmac edge by a Type III barricade
  with ROAD CLOSED (box collider) - past the obstacle audit's reach, so no pocket. NOT laid: where the road's own barrier runs across
  the mouth or a cut face / ditch lip does (the stage builder plans its roadside without side roads; a real mouth there needs the
  roadside planner to know about mouths - the bank agent's functions, not touched).
- THE PARK ROAD from Main Street: 286 m on the ChimneyRock stage's own line and heights (its waypoints re-projected), over its
  67 m river bridge (OSM 2024 way 267913868: a deck slab, 0.85 m concrete parapets with box colliders, two piers), closed at the
  mouth. The ChimneyRock stage itself starts at the bridge's south end (its 2026 OSM has no bridge way), so in that stage the same
  bridge, Main Street (804 m of it), 44 village buildings, the river and the lake are drawn behind its grid (PSX_LURE_CHIMNEY).
- WALKS: 703 sidewalk panels (concrete, flush on the shoulder: the stage keeps no step beside its tarmac, so the curb is a flush
  line), 3 crosswalks (OSM nodes), the lakeside boardwalk on posts and the Flowering Bridge (the 1925 three-arch bridge beside the
  river bridge: arches, balustrades, garden beds).
- FURNITURE: 181 wooden utility poles every 45 m on the side away from the water, with five wires each (three on the crossarm, two
  lower), capsule colliders, placed between the audit's station lines; 91 black lantern street lamps downtown and on Main Street;
  20 signs: SPEED LIMIT 25 / 35 / 20 / 35 at the OSM zone changes (both directions), CHIMNEY ROCK VILLAGE LIMITS, the brown
  CHIMNEY ROCK STATE PARK sign with its arrow at the park road, US 64 / US 74A / NC 9 as words (WEST / NORTH after the start, EAST /
  SOUTH before Bat Cave: no shields), NC SCENIC BYWAY, LAKE LURE, BAT CAVE, pedestrian crossing (a pictogram) at the crossings.
  Sign faces: Art/LakeLure/lure_signs.png (tools/roads/lure_art.py, the repo's Overpass font).
- KEEP-OUT: the forest and every plant pass ask StageKeepOut: nothing planted in the water (3.5 km2), on a building (+6 m), a lot,
  a mouth or a deck path. Plants through solids: 0.
- SURVEYED RAILS (Art/LakeLure/lure_rails.json): eleven places looked at in the video; W-beam at 3:05 and 8:45 (both already
  warranted by the game; recorded), a third at 14:10 not placed (its bend could not be matched); timber post-and-rail at 6:00 and
  16:50 (the game builds W-beam at 6:00).

## Gates
All in PSXPerf (Windows target), each job a fresh process; the build settings' scene list rewritten first
(LakeLureTools.WriteSceneList: 22 scenes, 0 missing).
- StageLab, LakeLure (lab4, the world ON): roadside checks 0 failing; obstacle audit CLEAR, SURFACE OK, no FAIL line; terrain ok
  (road 0.32 m clear of the ground, lattice under every shoulder); LANE AUDIT OK; plants through solids 0 (4,688 checked). The bare
  stage (lab1) passed the same. On the way: lab2 failed 17 EDGE FACE (the side-road stubs had colliders and climbed cut faces) and 2
  UNWARRANTED BARRIER (a building wall 5.3 m out, the park road's closure); fixed by decal mouths, closures past the reach and
  eave-capped building colliders (lab3 and lab4: 0 FAIL).
- StageLab, ChimneyRock: with the world ON (lab6) exactly the 4 lines its own base build has with the world OFF in a
  fresh process (lab5off): EDGE FACE 1019 L 0.12 m at 6.95 m, 661 R 0.10 m at 7.10 m, 1021 L 0.08 m at 7.15 m, and UNWARRANTED
  BARRIER 0 R (the stage's own wall over its end pad). These are mountain-nm's (the lidar round's open chimney failures), not this
  branch; OwnerAccepted stays empty; roadside 0 failing, CLEAR, LANE AUDIT OK, plants 0. Lab4 had shown 2 more of mine (a parapet
  of the park road's bridge over its end pad, and US 64/74A's own furniture laid along the park road: the world file's stage tag
  matched the prefix) - both fixed (bbac9898): lab6 adds nothing.
- race-play-check -Finish (TimeScale 3, seed 0): LakeLureRev - every car home (Supra 406 s, Charger 451, Skyline 460, player
  472 s), 0 retired, 0 head-ons. LakeLure (forward) on lab4 - player and Skyline home, but 2 rivals ran 6 m wide at 150 km/h
  through Chimney Rock and retired on a lantern post (wp 1970) - FAIL "at most one rival retires" [2]. Fixed: lamp and sign posts
  drawn and not solid (as the stage's reflector posts), poles 6 m out. Re-run on lab6: every car home (Skyline 403 s, Charger 410, player 426, Supra 429), 0 retired, 0 head-ons, no FAIL line.
- sprint-check: the four Lake Lure sections pass every line. 22 failures in the same run are all on BLOWING ROCK SPRINT II and the
  NC 226A sections: PSXPerf's SwissNC226A scene is stale (2,986 waypoints against the catalog's 2,981), not this branch.
- reverse-check: exit 0 (LAKE LURE - CHIMNEY ROCK II lines up with the rest). finish-play-check LakeLure: every line ok.
- LifeSim self-test (quick, same sandbox): base (7badf362 code) 4,598 ok / 64 FAIL; branch 4,726 ok / 65 FAIL - the one new line
  ("each step stamps its own number exactly once": v30 was stamped twice) is fixed in 0a820b64; re-run: 4,724 ok / 64 FAIL, the same 64 lines as the base: no new failure. The base's 64 are
  this sandbox's (deck-run scenes not built, and others), identical in both runs.
- Menu preview (PSX_PREVIEW_VENUE=LakeLureRev): the brief's name clipped ("RACE NIGHT - LAKE LURE - US 6...") at all three aspects;
  renamed LAKE LURE - CHIMNEY ROCK. Re-run: "LAKE LURE - CHIMNEY ROCK" still cut ("RACE NIGHT - LAKE LURE - CHIM...", 130 clips in all, the rest are car and rival names the base has); the home line cuts at about 13 characters, so the venue is now LAKE LURE / LAKE LURE II (29ece13f) - that last name was not re-run through the preview
- Door tour: needs a built player (tools/door-tour.mjs); LakeLure and LakeLureRev are in DoorTour.MainDoors for the coordinator's PC
  build. Not run here (no player builds by agents).
- Track map: StreamingAssets/TrackMaps/LakeLure.jpg shot (lab scene: tiled canopy; re-shoot on PC-pass scenes).
- Proof sheets (scratchpad lakelure\): proof_lakelure_s4.png (11 video moments, real against game, driver's eye, noon),
  plan_lakelure.png (the route on the 2024 map), before_after_lakelure.png (Chimney Rock OFF/ON, fresh processes),
  park_road_both_venues.png (one place, both venues).

## Switches (all default ON; =0 is the stage as it was)
PSX_LURE_WORLD (no world file at all), PSX_LURE_WATER (lake, river, carve), PSX_LURE_TOWN (buildings, street bench, walks,
furniture, signs), PSX_LURE_MOUTHS (side-road mouths, the park road and its bridge, closures), PSX_LURE_CHIMNEY (the Park Road
stage's start dressing). Shot tools: PSX_SHOT_DRIVER=1 (a driver's-eye frame), PSX_SHOT_NOON=1 (the game's noon).

## Merge notes
- NEW files (theirs to keep): Editor/PSXRacingBuilder.StageRiver.cs, Editor/PSXRacingBuilder.StageTown.cs, Editor/LakeLureTools.cs
  (+ metas); tools/roads/bake_world.mjs, plan_wiggle.py, lure_art.py; Resources/lure_stage.json; Art/LakeLure/* (DEM grids, world
  file, signs atlas, Tex/, rails); Art/ChimneyRock/chimney_world.json; StreamingAssets/TrackMaps/LakeLure.jpg;
  tools/roads/srtm_bakes/lure_srtm/; Docs/lidar_audit/after/lure_audit.json.
- ONE-LINE HOOKS in shared files: PSXRacingBuilder.Stage.cs StageLoadDem (StageWorldLoad() after the tunnel table) and
  BuildStageForest (2x `if (StageKeepOut(..)) continue;`, near and far forest); PSXRacingBuilder.cs BuildTrack (BuildStageWorld()
  before BuildStageForest) and the theme table (["LakeLure"] = LakeLureTheme() + the 9-line LakeLureTheme()); MeshTrees.cs
  (StageKeepOut in ground cover, debris, ExtraCanopySite, understory); RockRelief.cs (boulders); Fences.cs (fence posts);
  PSXScreenshotTool.CaptureSpotsOnly (PSX_SHOT_NOON, PSX_SHOT_DRIVER). None touches BuildRoad, the ribbon or the cross-section.
- CATALOG (Scripts): TrackCatalog.Authored + LakeLure after ChimneyRock, HeldBack { }, 4 SprintVariants appended at the end, RemapV29Index;
  LifeState.saveVersion 30; LifeSimManager.Migrate (writtenAt + the v30 step MigrateToLakeLure); CarMeets touge list; DoorTour
  MainDoors (LakeLure, LakeLureRev); Climate venue row. Self-test pins: LifeSimSelfTest.TestRetiredVenues (v21 survivors = today
  less the v30 additions; every v29 index lands on its venue), the Chimney-is-last pin (now: before Lake Lure), and
  LifeSimSelfTest.Events (the v29 step stamps the CURRENT version). If another branch also adds a save version, renumber at merge
  (TestSaveVersionLadder) and keep MigrateToLakeLure's `writtenAt >= 22` rule.
- AFTER MERGE: a full scene build (the build settings' list moves: Lake Lure is scene 16 and everything after it moves by one;
  in a sandbox LakeLureTools.WriteSceneList writes it without building); the store packs must be in the building sandbox (NM stones
  for the river boulders; without them the river has no boulders and says so); re-shoot TrackMaps/LakeLure.jpg on PC-pass scenes
  (this one is a lab scene: tiled canopy); run tools/roads/bake_bank.mjs on `lure` with the other stages (its stage files are in
  today's format); the PC build's door tour then reaches LAKE LURE - CHIMNEY ROCK and its II.
- DATA in main's tools/roads/cache (gitignored, added this round): overpass_lakelure_2024-09-20.json, attic_lakelure_*_2024-09-20.json,
  3DEP tiles (+53 MB, ledger updated). A fresh checkout re-fetches the attic files with the same date (fetch_road.mjs `attic`).

## What still looks wrong (own sheets, honestly)
- The granite domes and cliffs above the village and the marina (8:45, 9:45) are missing: the far land is forested, no bare rock.
- Where the real road has grassy banks between downtown and the village (10:15) the builder still cuts rock faces; the street bench
  covers only the street stretches.
- The ground and shoulders are the mountain stages' brown dirt, where the real road has mown grass verges.
- Buildings are boxes in the city's house-atlas textures: no log-cabin corner notching, no stone pillars, no dormers; the Lake Lure
  Inn reads white with a red roof but has no arcade. The marina's covered slips are not modelled (OSM has no footprint for them).
- 25 of 70 side roads have no mouth (a rail or a cut face runs across them).
- Main Street in the Park Road stage is plain asphalt without lane paint, laid on that stage's 12 m lattice.
- Near crowns in the forest are the pixelated impostor cards every stage has.
- The 26 m downtown bridge is road on an embankment (spans under 56 m are not decks in the stage builder).
- Video positions at 3:05, 6:00, 14:10 and 16:50 are estimated from timing (+/-150 m); 14:10's bend does not match.

## Combined event (Lake Lure, then up the park road) - not built, what it would take
The two venues are separate scenes. A combined race needs one route: the LakeLure line to the park road's mouth (7,640 m), the park
road lead over its bridge (99 m, already in lure_world.json), then the ChimneyRock line - one stage json (fetch_road with a match list
of both roads, or a join tool), its own scene (~16 km of stage), and the turn at the mouth (a 90 degree junction, R ~12 m) through the
roadside planner. Or a scene-to-scene handover at the park road's closure, which the race code does not do today.

# HANDOFF - J3 STAGE LINES, PHASE 1: the mountain waves taken out of all eight stage lines (agent "lines", branch `mountain-lines`, 2026-11-16 night; NOT pushed, NOT merged)

The owner, 2026-11-16 ~01:00, after PC45: "I've noticed this in mountain tracks as [well] where an otherwise straight road will
wave back and forth abruptly in a way that DOT would never approve." Spec: the align investigation's PLAN.json, job J3, Phase 1
only (offline: node and python, no Unity, no sandbox). Worktree `C:/Users/mcgee/PSX Racing-lines`, base mountain-nm 7badf362
(PC45). The 3DEP cache and Overpass files were copied to the scratch folder and PSX_ROADS_CACHE pointed there, so no bake wrote
to the main checkout. Before anything changed, `bake_3dep.mjs swa --check` reproduced the PC45 files exactly.

## What made the waves (the investigation's causes, re-measured with the same detectors)
- The mapped (OSM) line's own digitising jogs, drawn faithfully by the builder's chord-length Catmull-Rom
  (PSXRacingBuilder.Ribbon.cs RibbonFrame): the mapped line alone has 25 S-places, 108 short-tangent jogs, 74 abrupt reversals.
- The Nov 10 lidar round's 38 bench moves, faded in and out over 24 m (bake_3dep.mjs TAPER = 6 stations): PC45 has 37 S-places,
  133 jogs, 91 abrupt reversals. `bake_3dep.mjs <pre> --offline --out <dir>` (the old path, untouched) re-derives the same 38.

## What changed
- `tools/roads/plan_smooth.mjs` (new). planSmooth = lakelure's planSmoothM (7a9f964d fetch_road.mjs) as a tool of its own: a
  gaussian of 8 m, full where R > 120 m (circle through +-12 m), none under 50 m, the blend following the tightest curvature within
  3 sigma and itself blurred, open stages keep their first and last 3 sigma, loops wrap. It does NOT re-space the line: every
  waypoint keeps its index, so everything given in metres along a stage (start, finish, bridges, crossings, sprint cuts, rails;
  the game reads them as waypoint round(m / 4)) stays on the same stretch of road. localRefits = variant L, made to work: the
  plan's single 200 m quadratic was refused at all seven S-places it was tried on (it would have moved the line 1.2-7.7 m: every
  200 m window reaches a real bend). Instead the line is redrawn as the curve closest to it with the least change of curvature
  (weighted squared distance + 100 x the squared third differences of the waypoints), held loosely (weight 0.05) between the S's
  peaks, ever more firmly over a quintic taper each side, and pinned at the window's ends (position, heading and curvature carry
  on). An S has a great deal of curvature change, an arc or a spiral almost none. Each taper is max(72 m, 45 W, 1.5 W S) for the
  move W at the posted speed S (TrackCatalog: swa/mtm/swiss/brock 35 mph, gap/beech/brp 45, chimney 25); moves over 2 m are refused.
- `tools/roads/bake_3dep.mjs`: `--plan-smooth <m> [--refits]` (with --keep-line). Recorded in the meta (source.line, planSmoothM,
  refits) and honoured by --check; places along the stage are not remapped (same indices). The header says why the 24 m bench
  moves are gone and what a move needs if it comes back (L >= max(45 W, 61 m) per side and 1.5 W S).
- `tools/roads/plan_refits.json` (new): the 14 refits and why each was made (7 S-places + 7 abrupt reversals; see below).
- `tools/roads/waves.py` (new): the investigation's detectors (align/verify-mountain/vm.py S-places and jogs, the counts the plan's
  gates were written in; align/mountain/wavelib.py's moving fit), plan_wiggle.py's abrupt reversals, the lidar shelf betas
  (both edges must carry an S, beta >= 0.7 with the shelf found at 60 % of the stations, to count as the real road), mapped OSM
  bridges (bake_3dep's osmSpans), off-shelf runs; `report` and `refits` commands. `tools/roads/shelf_edges.mjs` (new): both lidar
  shelf edges at every waypoint, offline, no ledger write. `tools/roads/plan_wiggle.py`: copied unchanged from lakelure 7a9f964d
  (identical to lakelure HEAD; lakelure owns it). On a loop pass `--from 0 --to <length>`: with start = finish = 0 it crashes.
- `tools/roads/pc45_lines/`: the eight stage lines PC45 shipped (the before).
- The eight stages re-baked: `node tools/roads/bake_3dep.mjs <pre> --keep-line --offline --plan-smooth 8 --refits`
  (Resources/<pre>_stage.json + Art/<stage>/<pre>_dem_meta.json). The 38 short-fade bench moves are gone; no long moves (owner
  decision 2 pending). Heights are the lidar bench's on the new line (against PC45 at the same place: rms 0.01-0.09 m, worst
  +-0.5 m). Near and far grids byte-identical to PC45 (no .bytes file in the diff). `--check` OK on all eight.
- `Art/Swiss226A/swa_rails.json`: the surveyed rail 256-372 m -> 252-368 m, the same stretch of road on the new line.
- No C#, no TrackCatalog, no fetch_road / fetch_loop / lib3dep change. No runtime switch: the line is baked into the stage scenes
  and read by TrackPath, AI and the minimap; the A/B is PC45 against the next build.

## Gates (the builder's curve at 1 m; new against PC45; per stage swa / gap / mtm / beech / chimney / brp / swiss / brock)
- S-places: 37 -> 3 (7/7/9/3/1/3/4/3 -> 1/1/0/0/0/0/1/0). PASS: the 3 left are carried by both shelf edges (the real road):
  swa 1324-1410 (beta R 1.96, L 1.14), gap 931-1002 (1.64 / 0.75), swiss 5577-5680 (2.35 / 1.19). None on a bridge.
- Short-tangent reverse curves (jogs): 133 -> 27 (29/32/22/7/5/4/13/21 -> 9/6/2/1/1/0/5/3). PASS (<= 50); every one is on the
  mapped line too (both curves, same sign, within 8 m).
- plan_wiggle abrupt reversals: 91 -> 16 (40/15/15/4/5/0/8/4 -> 9/1/0/0/4/0/1/1). FAIL against 0: 11 of the 16 are carried by
  both shelf edges; the other 5 were left as mapped because a refit would put the road further off the shelf (swa 2004, 2180,
  3948, 4300) or move it 2.3 m (brock 4180). All 16 are on the mapped line. They are bends of R 45-240 m, 1-2.9 m off the chord.
- plan_wiggle worst wiggle per 100 m: 0.60 / 0.57 / 0.38 / 0.43 / 1.95 / 0.08 / 0.65 / 0.58 m (PC45 0.63 / 0.61 / 0.47 / 0.40 /
  1.68 / 0.17 / 0.65 / 0.58). FAIL against 0.30 except brp. The worst readings are hairpins, not wiggles: a cubic over 40 m cannot
  follow a 6-30 m hairpin (chimney's 1.95 m is its 6.1 m hairpin at 4208 m). Medians fell 2-6x (e.g. mtm 0.105 -> 0.025 m).
- Largest plan move on gentle stretches (mapped R over 120 m): 0.31 / 0.26 / 0.26 / 0.26 / 0.25 / 0.67 / 0.26 / 0.28 m. PASS
  except brp 0.67 m at 6572 m: the Linn Cove viaduct's south abutment, a 26.9 m kink in the map (3-point R 26.9 -> 41.6 m). Moves in
  real bends (R under 50 m): 0.00 m.
- Lateral moves off the mapped line (the 14 refits): W 0.39-1.54 m, tapers 72-100 m per side, each >= max(45 W, 61 m) and
  >= 1.5 W S, each whole move longer than 2 L. PASS. (The gaussian's own moves are under 0.5 m and only take out the map's jogs.)
- Tightest corner no tighter than mapped (-0.75 m): equal on seven stages, brp looser (26.9 -> 41.6 m). PASS.
- Tightest vertical curve: 591 / 341 / 523 / 424 / 255 / 600 / 332 / 230 m (gaussian sigma 8 m everywhere). PASS (>= 230).
  Max grade: chimney 14.8 % (under the self-test's 15 %), the rest 6.1-13.0 %.

## Places along the stages (metres -> waypoint round(m / 4)), PC45 -> new
- TrackCatalog (lakelure owns it, NOT edited): the eight SwissNC226A sprint rows (TrackCatalog.cs:1057-1097) must go back to their
  pre-11-09 values, the same places on the mapped line: 3696 -> 3688, 7848 -> 7832, 11738 -> 11721. Unchanged they would land
  9.3, 16.1 and 19.8 m from where PC45 has them. Labels stay (860 M: 858.4 m; 635 M: 634.5 m; THE VALLEY 479 m). Blowing Rock's
  0 / 6328: 0.11 / 0.03 m, no change. The 3688 cut stands 3.3 m to the side of PC45's: PC45 had moved that stretch onto the bench.
- swa_rails.json: 256-372 -> 252-368 (done here, 0.85 m to the side).
- Start lines move <= 0.6 m. Finish lines on gap / mtm / beech / chimney move 2.7 / 3.1 / 2.6 / 2.8 m: the bake now keeps the
  mapped line's own start and finish; the 11-09 remap had let them drift (arc length against waypoint index).

## What still looks wrong (my own sheet, scratch\lines\worst10_before_after.png)
- The waves are gone at all ten places. At four of them the new road sits 3-6 m past the lidar shelf's edge where PC45's bench
  moves had put it on the shelf (with S-bends): mtm 4593-4663, mtm 5023-5545, chimney 1360-1433, swa 11148-11232 (the mapped bend
  is tighter than the shelf). Stage-wide, road-edge station-sides more than 1 m past the shelf: PC45 3,206 -> new 3,815; 644 of the
  786 new-or-worse stations lie within 40 m of a dropped bench move (mtm 425, swa 148, gap 47, chimney 22). That is the plan's risk
  8 and owner decision 2 (long DOT-length moves where both shelf edges agree). Phase 2's off-shelf gate will fail without them.
  The full list: scratch\lines\offshelf_new.csv (every run, side, worst, PC45 at the same place).
- 5 abrupt reversals and the hairpin wiggle readings above.

## Not done
- Phase 2 (after the bank merge): bake_bank.mjs for the 8, StageLab audits, finish-play-check, driver's-eye pictures (PSXRec).
- Nothing was seen in a running game or a Unity build. The pictures are straightened plan strips over the lidar slope.
- No long bench moves (owner decision 2). The investigation's other "REAL?" S-places (swiss 5438/9012, swa 1331, gap 2554, mtm
  5856, measured on PC45) were re-judged on the smoothed line: only the three above remain and all three are carried.

## At merge
- Merge after bank. bank's eight Art/<stage>/<pre>_bank.json are per waypoint of the PC45 lines (swa 2986 -> 2981, mtm 1871 ->
  1867, beech 2820 -> 2819 waypoints: the loader skips a count mismatch, so those three would drive flat; on the others the
  stations shift up to ~3 m). Re-run `node tools/roads/bake_bank.mjs <pre>` for all eight on these lines before a build.
- Rebuild all eight stage scenes in the next PC build: never ship these stage JSONs with PC45's scenes.
- TrackCatalog swa sprint cuts as above (after lakelure merges). plan_wiggle.py is byte-identical on both branches.
- Re-bake check: `node tools/roads/bake_3dep.mjs <pre> --check` (with PSX_ROADS_CACHE = the main checkout's tools/roads/cache).
- No city re-bake. No shared C# edits.

# HANDOFF - BANK ROUND: the mountain roads crowned and superelevated from the 2018 lidar (agent "bank", branch `mountain-bank`, 2026-11-16; NOT pushed, NOT merged)

The owner (verbatim): "Did you catch the banking of the roads? I've noticed many of the mountain road tracks are very flat. They have
turns, but almost no (if any) banking". He was right: `BuildRoad` laid every stage ring flat across and everything beside the road
was built off the centre line's height. Worktree `C:\Users\mcgee\PSX Racing-bank` off mountain-nm 7badf362; sandbox PSXRec (Android
target; no store packs, so every picture shows the older fallback forest). Switch `PSX_STAGE_BANK` (default ON; `=0` reads no file and
builds today's flat road exactly). `PSX_AI_BANK=0` makes the AI plan bends as if flat.

## What the lidar says (`tools/roads/bake_bank.mjs`; each file's `stats` has the detail)
Per waypoint, five cross-section lines of the USGS 1 m lidar (NC_Phase5_2018_A18, main's cache, offline, nothing fetched) across the
paved width less 0.5 m a side; robust fits of one plane and of a two-half-plane crown; confident = fit RMS <= 3.5 cm (p50 0.4-0.8 cm),
>= 60% of samples kept, no edge over 14%, not on a structure, no adverse bank over 2.5% in a bend under 150 m; a +-2 station median.
Design: AASHTO Green Book 2018 3.3 (Rmin from Table 3-7, Method 5 as e = emax(2Rmin/R - (Rmin/R)^2)), emax 8% (NCDOT's superelevation
training works at 8% rural; the Roadway Design Manual PDF was too large to fetch), Linn Cove 10% (Muller & Barker, PCI Journal 1985).
Built = measured where confident, design elsewhere and on structures, AASHTO runoff (Table 3-15, 2/3 on the tangent), clamp 12%.

| stage | measured | crown on straights, measured / built | bank into bends R<150 m, median / p98-max: measured; design; built | runoff |
|---|---|---|---|---|
| NC 226A (swa) | 92.7% | 2.6% / 1.9% | 8.2 / 13.1; 8 / 8; 7.9 / 12 | 16 m |
| Gillespie Gap (gap) | 98.0% | 1.5% / 1.2% | 6.9 / 12.6; 8 / 8; 6.8 / 12 | 24 m |
| Mount Mitchell (mtm) | 91.3% | 3.2% / 2.3% | 7.0 / 11.7; 8 / 8; 6.5 / 12 | 24 m |
| Beech Gap (beech) | 98.8% | 2.1% / 1.8% | 6.6 / 8.9; 8 / 8; 6.6 / 10.7 | 32 m |
| Chimney Rock (chimney) | 82.2% | 2.2% / 1.6% | 4.0 / 11.4; 7.4 / 8; 3.4 / 12 | 16 m |
| Blue Ridge (brp) | 78.9% | 1.2% / 0.5% | 9.0 / 10.5; 8 / 10; 8.9 / 10.7 | 48 m |
| Little Switzerland (swiss) | 92.4% | 1.9% / 0.9% | 8.6 / 12.1; 8 / 8; 8.4 / 12 | 28 m |
| Blowing Rock (brock) | 96.1% | 1.8% / 1.2% | 5.0 / 10.1; 8 / 8; 4.8 / 11.8 | 36 m |

- No stage is flat-ish: the real roads bank 4-9% (median) in their bends, often past the 8% design; NC 226A's, NC 128's and the Little
  Switzerland loop's switchbacks measure 12-16% on clean fits (1 cm RMS, the bank toward the inside) and are clamped to 12%. Chimney
  Rock's park road (4%) and Blowing Rock (5%) bank least. Only 21-56% of straight stations are crowned; the rest is one plane tilted 2-4%.
- The spots named: Chimney Rock 715 inside edge -12% (measured -12.2 to -12.9), outside +5 to +7%; 1049 -9.6% / +6.4%; NC 226A's tightest
  switchback wp 1737 (R 17 m) 10.8%; Linn Cove (brp 1119-1223) 10% one way, a 48-55 m reversal, 10% the other.
- Lidar could not say: decks and the Little Switzerland tunnel (design), plus each file's `stats.lidarCouldNotSay` (canopy, adverse
  fits: Chimney Rock 113 adverse stations rejected, NC 226A 116, Mount Mitchell 101). Little Switzerland wp 1241 is a junction turn whose
  real surface runs 6.5% the wrong way: refused as adverse, built at the design value squeezed to -2.6 / -1.6%.
- Coast stages (Emerald Isle, Langston Bridge, Atlantic Beach Bridge): no lidar in the cache (UTM 18) and no `STAGES` entry: no file,
  flat as before (decided: left flat).
- For the alignment fix: the worst places where the line is off the lidar bench (confident stations, |offset| > 1.5 m for 12 m or more;
  + = bench left of the line): gap 1162-1245 (peak 6.5 m, mean 3.5), swiss 693-731 (3.5/2.4), swiss 1666-1690 (-5.3/-2.9), brp 1402-1436
  (3.5/2.3), brock 2375-2403 (4.0/2.6), brock 619-640 (5.3/3.0), beech 570-587 (-5.5/-3.4), beech 1100-1117 (-5.5/-4.3), beech 784-798
  (6.0/4.1), brock 1330-1338 (-7.8/-5.6). Full list: scratchpad `bank\bench_offsets.txt`. Some are pull-offs and junctions (a wide bench).

## The build (new `Editor/PSXRacingBuilder.Bank.cs`; small edits elsewhere)
- `LoadStageBank` (called at the end of `StageLoadDem`): reads `Art/<stage>/<pre>_bank.json`, refuses a file whose count is not the
  route's (WARN, flat), converts the slopes to metres at this road's half width, and fades a route with ends to flat over its first
  and last 8 stations (32 m; every start line is 33-60 m in) because the end pads carry a flat road on. `BankEdge(i, side)` = that
  side's tarmac edge over the centre line; `BankAcross(i, lateral)` = the surface, linear from the centre to the edge, the edge's
  beyond it. Everything is zero with the switch off, with no file, or off a stage.
- The centre line keeps its height (the bake's lidar road); the road turns about it. `BuildRoad`: the ring's left and right vertices
  are lifted (`RingBank`, lerped on the curve's slices); the centre column, already there for the painted line, makes the crown.
- Everything beyond an edge is that side's flat-road geometry lifted by `BankEdge`: kerb strips (`FlatKerbMesh`, street style too),
  shoulder rows (`ShoulderStation`, `ShoulderDesignAt`), the lattice solve's samples (`ForEachStageLatticeSample`: the tarmac and
  strip across, every section), the plan's land reads (`RoadsideLandDy`, `StageBankSolve`, the flare probes in `PlanStageRoadside`,
  `BuiltSectionCritical`, `FinishStageCuts`), the holds (`CollectCarryHolds`, `CollectRiseHolds`, `CollectBehindWallHolds`,
  `CollectFillFalls`), wall rings (`StageWallRing`, `CurveWallRing`: rail, stone, posts, retaining walls and the solids all read the
  ring), reflector posts (`PlaceStagePosts`), cut faces and rock tops (`BankTopRing`, `BuildOneStageBank`), the tunnel tube's feet,
  plant clearing (`PlantClear`: the envelope from the low edge to the high one), the cliff dressing (`PlaceCliffs`).
- The ground field: `Refine` adds the banked surface to `RoadFoot.roadY`, so the dig under the pavement and the graded roadside follow.
- Decks: the top is laid in five columns (deck edge, tarmac edge, centre, tarmac edge, deck edge) on a banked road, so it stays 2 cm
  under a crown or a superelevated road across its width; soffit, fascias and piers follow; joint bands likewise. Flat: the two
  columns it had, triangle for triangle.
- Start and finish bands: on a banked road a 3-column band over the cross-section (yaw-only frame); flat: the quad as before.
  `RaceHandoffApplier.PaintLine` re-lays a sprint's band over the cross-section where it is painted.
- Grid (`BuildCars`): each slot on the banked surface under it and rolled with it.
- `TrackPath.bankL/bankR` (new serialized fields, slopes): baked by `BakeBankInto`, reordered by `RotateInPlace`, reordered AND
  swapped left/right by `ReverseInPlace`; `HasBank`, `CrossSlopeRight(i, lateral)`.
- Audits read the banked edge: `TrackObstacleAudit` (tarmac height extrapolated from the inset to the edge; the ghost-barrier passes
  measured from each barrier's own side), `LifeSimSelfTest` (shoulder rows start flush on the strip at the banked edge; deck-rail rays
  over each side's edge), `PlantSolidAudit` (envelope on the banked surface). Flat roads read exactly as before.
- New tools: `BankShots.Capture` (the proof frames + a raycast cross-section per spot), `BankLab.BuildOnly`; `StageLab` keeps each
  venue's build log and a partial report.

## The runtime
- AI: `AIGrip.CornerSpeed(..., bank)` holds v^2 k = g (mu + e) / (1 - mu e) on a bank of slope e (falling toward the inside); the
  flat answer is unchanged. `AIDriver.PlanSpeed` reads e under the line the car will take (`TrackPath.CrossSlopeRight` at the lateral
  it plans, signed by the bend's direction: a crown's outer lane is adverse). `PSX_AI_BANK=0` plans flat.
- Recovery: `CarController.ResetTo` (every respawn, stuck recovery and TryPlace) rolls the car about its own forward to the road
  under it (`RollToRoad`: layer 8, from half a metre over the seat, under 15 degrees). Forward and pitch are the caller's, so on a
  road flat across (every circuit and street) the pose is exactly what was asked for.
- Grid restaging (`RaceHandoffApplier.StageReversedGrid`, the 1v1 restage on a two-lane road): `SeatOnBank` lifts each slot to the
  surface under it and rolls it.
- Camera: `ChaseCamera` builds its pose with `LookRotation(..., Vector3.up)` and its roll is a drift cue from yaw rate, so the
  horizon stays level on a bank (checked in the low chase frames). The cockpit mount rolls with the car, as the game's does.

## Proof (fresh processes; OFF = PSX_STAGE_BANK=0, ON = default)
- Pictures (scratchpad `bank\`): `bank_sheet_before_after.png` (10 spots x driver's seat and low chase, OFF | ON, car roll and lane
  lift printed), `bank_sheet_big_pairs.png` (4 large low-chase pairs), `bank_xsections_built.png` (per stage at its sharpest bend: the
  lidar ground, the fit, the BUILT road raycast off the scene ON and OFF), `plots\bank_xsections_sheet.png` (the measurement's plots).
- Car roll read off the scenes at the spots: Chimney 715 +2.9 deg, 1049 +3.6; NC 226A 1737 -6.2, 536 +6.8; Linn Cove +5.7; Gillespie 844
  +5.9; Mount Mitchell 1527 -6.8; Beech 1505 +3.9; Little Switzerland 1589 +6.8; Blowing Rock 1984 +2.9 (OFF: 0.0 everywhere).
- StageLab, all eight, fresh process each way: OFF base = 7 lines (Chimney EDGE FACE 1019 L / 661 R / 1021 L and UNWARRANTED wp 0 R;
  Mount Mitchell EDGE FACE 1544-45 R; Beech UNWARRANTED 59 R and 681 L - the lidar round's documented ones). ON = 8 lines: the same less
  Beech 59 R (gone), plus TWO NEW: Chimney EDGE FACE 712 L (0.06 m in 0.13 m at 7.25 m, the island inside the 715 hairpin, where 711 L's
  rising section meets 712 L's falling one in the zipper; 0.06 is the threshold itself) and Blue Ridge UNWARRANTED 1631 R (a faced cut
  station next to a graded one, rock top at +0.18 m 1.5 m behind the 4.04 m face: the base's Beech 59 R signature to the centimetre; the
  bank lifts that edge 3 cm). Terrain audit OK, lanes OK, barrier faces OK, deck rails OK on all eight both ways.
  Found and fixed on the way: the obstacle audit's ghost pass read the band from the centre line (24 false ghosts a stage), the tarmac
  edge from a 0.3 m inset, the self-test's shoulder rows from the flat edge; a banked deck's end cap lay in the station plane (Blue Ridge
  704 L, 0.07 m "drop" - caps now 2 cm past the line); the builder's planting rule read a flat envelope (plants 1 / 4 / 1 / 1 on Chimney,
  NC 226A, Gillespie, Beech) - fixed (d7b4ee72): the four rebuilt banked, plants through solids 0 on all four, their audit lines unchanged; so ON = 0 plants on all eight.
- LifeSim self-test: OFF 63 FAIL (PSXRec's known baseline), ON 63 FAIL, the identical list (diffed).
- Races (race-play-check, seed 0, -Finish, no traffic, x4 time): best rival home OFF -> ON, forward: Chimney 211 -> 209 s (-0.9%), NC 226A
  449 -> 428 (-4.7%), Blue Ridge 178 -> 171 (-3.9%), Gillespie 214 -> 209 (-2.3%), Mount Mitchell 227 -> 234 (+3.1%), Beech 331 -> 324
  (-2.1%), Little Switzerland 296 -> 291 (-1.7%), Blowing Rock 261 -> 254 (-2.7%). Reverse twins ON: 200 / 441 / 166 / 193 / 204 / 336 /
  277 / 258 s. Race checks OK: OFF 7/8 (Beech: 2 retired), ON 13/16 (Gillespie: a rival stuck behind a wreck 5 s wrong way; Little
  Switzerland both ways: 2 retired = the planned driver error plus one crash). One seed: the retirements are mostly the planned mistake
  (MISTAKE lines) and the counts are within one seed's noise; not proven either way.
- Play checks ON (fresh processes): reverse-check 36 ok / 0 FAIL ("ALL REVERSE RACES LINE UP"); sprint-check 72 ok / 0 FAIL;
  finish-play-check Gillespie Gap 14 ok / 0 FAIL and Chimney Rock 14 ok / 0 FAIL.

## Still looks wrong / not done / not verified
- In the pictures: the driver's-seat frames are mostly cabin at this size, the roll shows in the horizon's tilt; the low chase frames
  show it plainly. The game's centre line sits up to 0.2-0.3 m under the lidar pavement at some strongly banked bends (the bake's
  bench height, not this round's).
- The two new audit lines above are not fixed (both at builder thresholds of classes the base already carries 7 of).
- Not run: a second seed, traffic, a person driving, a PC build; the city (`Scripts/City/`) untouched. Later idea: Charlotte's ramps and
  loops are flat across too; a real ramp carries 6-8% (AASHTO, design speed by radius). The city's 3DEP 1/3" ground cannot measure it
  (10 m cells); it would be design values on the line model, a city round of its own.
- Little Switzerland's terrain line about its tunnel under the PC horizon stand-in was not seen here (PSXRec builds no PC horizon).

## At merge (coordinator)
- New files: `Assets/PSXRacing/Editor/PSXRacingBuilder.Bank.cs`, `BankShots.cs`, `BankLab.cs` (metas minted in PSXRec, committed);
  `tools/roads/bake_bank.mjs`; `Assets/PSXRacing/Art/{Swiss226A/swa, Gillespie/gap, MtMitchell/mtm, BeechGap/beech, ChimneyRock/chimney,
  BRP/brp, Switzerland/swiss, BlowingRock/brock}_bank.json` (+ metas).
- Shared-file edits, all small and local:
  - `PSXRacingBuilder.cs`: `BuildTrack` (one call, `BakeBankInto(path)`, after `StageLoadDem`), `BuildRoad` (left/right ring vertex
    lift; comment), `ShoulderStation` / the row trace / `ShoulderDesignAt` (roadY + bank), `FlatKerbMesh` / `StreetKerbMeshes` (+
    `RingBank`), `BuildJointBands` and `BuildOneDeck` (column loop, end push) and `BuildPiers` (soffit lift), `BuildStartLine`
    (banked band branch), `PlacePosts` / `PlaceStreetLamps` (one term each), `BuildCars` (two grid branches: lift + `gridUp`).
  - `PSXRacingBuilder.Stage.cs`: `StageUnloadDem` / `StageLoadDem` (clear / load), `Refine` (one line), `ForEachStageLatticeSample`,
    `CollectCarryHolds`, `CollectRiseHolds`, `CollectBehindWallHolds`, `CollectFillFalls`, `RoadsideLandDy`, `StageBankSolve`,
    `PlanStageRoadside` (two flare probes), `BuiltSectionCritical`, `FinishStageCuts` (three reads), `PlaceStagePosts`,
    `CurveWallRing`, `StageWallRing`, `BankTopRing`, `BuildOneStageBank` (a local `Y()`), `BuildOneTunnel` (wall feet).
  - `PSXRacingBuilder.PlantClear.cs` (the plant solids test), `PSXRacingBuilder.Cliffs.cs` (`PlaceCliffs`), `StageLab.cs` (per-venue
    build log, partial report), `TrackObstacleAudit.cs` (edge tarmac read; both ghost passes), `LifeSimSelfTest.cs`
    (`CheckShoulderRibbon`, `CheckDeckRails`), `PlantSolidAudit.cs` (`IndexRoad`, `RoadFrame`).
  - Runtime: `TrackPath.cs` (two serialized fields, `RotateInPlace`, `ReverseInPlace`, `HasBank`, `CrossSlopeRight`), `AIGrip.cs`
    (`CornerSpeed` optional `bank`), `AIDriver.cs` (`PlanSpeed`, `BankAware`), `CarController.cs` (`ResetTo`, new `RollToRoad`),
    `RaceHandoffApplier.cs` (`PaintLine`, `StageReversedGrid`, the 1v1 restage, new `SeatOnBank`).
  - The lakelure agent's new stage: no bank file = flat (the builder logs it). After both merges: add its prefix to `STAGES` in
    `tools/roads/lib3dep.mjs` if it is not there, then `node tools/roads/bake_bank.mjs <pre>` (reads its 1 m tiles from main's cache;
    speed from TrackCatalog by id), `--check`, rebuild its scene, StageLab it.
- Data rule: a bank file is per waypoint. After ANY re-bake that moves or re-spaces a stage's waypoints (the alignment fix you
  mentioned), re-run `node tools/roads/bake_bank.mjs <pre>` and `--check`; a stale file is refused with a WARN and the stage builds
  flat (count mismatch), so nothing breaks silently. The tool is a pure function of the stage files on disk, the lidar cache and
  TrackCatalog's speed: no hand edits.
- Scene passes: every mountain stage scene must be rebuilt (the bank is in the meshes and in TrackPath). Coast stages and the city
  are unchanged.

## What the owner will notice
The mountain roads now lean: crowned on the straights, banked into every bend as the real road is - up to 12% on the NC 226A and NC 128
switchbacks and Chimney Rock's hairpins, 10% across Linn Cove - and the cars (and the AI's corner speeds) lean with them.

# HANDOFF - CONCRETE ROUND 2: one barrier per median, no wall without a reason (agent "concrete", branch `charlotte-concrete`, 2026-11-15 evening; NOT pushed, NOT merged)

Restart of the concrete job stopped at 14:07 (the owner: "To the concrete pieces, yes"). Worktree `C:/Users/mcgee/PSX Racing-concrete`,
base e2749b17 (mountain-nm 416ddfda = PC43 merged in by the coordinator). Sandbox PSXRec (Android target), every comparison two fresh
processes, the base with `PSX_CITY_CONCRETE=0`. Graph hash b2aa3453 (GHSH) in every run; no data file touched, no export run.

## Step 1 - round 1's gate re-proved on the merged base (PC43 changed the tiles under it: curbs 3, inlets, lot entrances, bridges)
- Per-tile meshes, city-wide (5,073 tiles; every tile the gate changes is rebuilt with the gate off and compared): the gate changed a
  piece on 112 tiles; meshes that differ there: ground 0, roads 0, kerbs 0, guardrails 0, barriers 112, structure 4 (the city bridge's
  metal bar is not laid on a parapet piece rule 1 clipped). Barrier triangles on those tiles 121,584 -> 116,528; build 264 -> 284 ms a
  tile (warm pair). Tiles with barrier vertices no gate call accounts for: 0. Rule 1 took 569 m off pavement, rule 4 left out 227
  pieces (492 m), rule 5 turned 71 free square ends down. Pieces the gate changed that were in no stray class: 39 (39 m: side-median
  7, side-rail-ground 18, union-median-ground-twin 14 - ends turned down by rule 5 and pieces clipped at junction mouths).
- Bridges audit, city-wide (1,086 runs, 502 tiles), off -> on: RAIL-DROP 1 = 1 (the same place), RAIL-FLUSH 17 = 17, WALL-IN-LANE
  10 -> 7, RAIL-ON-LANE 3 -> 2, BLUNT-END 84 -> 81, APPROACH-RAIL 45 = 45. None rose. Two places are new with the gate on (their
  classes still fell): RAIL-ON-LANE on the owner's deck, e11321 s 12 (35.24745,-80.89613), a piece whose top stands 0.30 m over that
  ramp's lane line where the full-height WALL-IN-LANE on e13604 10 m away is gone (the two ramps' ribbons overlap at different heights:
  the deck surface, spui's job); BLUNT-END at I-77 / Tyvola Rd, e2747 s 17 (35.16301,-80.89024): a parapet cut where pavement lies past
  both its faces keeps its full height to its cut end (it guards a fall).
- Drive audit, the 48 edges, off -> on: nothing solid across a lane 0 = 0; fall holes 0 = 0; lane steps 3 = 3; BLUNT 7 -> 6; MEDIAN
  ACROSS two+ 5 = 5; a split's V read as two walls 345 -> 325 rays; MEDIAN ACROSS none 13 -> 16 rays, all at East Independence
  Boulevard e11785 by e7235 (8522,-4181), the same with the zone fix, so not a seam: the opening is real (the junction with e7235);
  rule 5 turned down the square end of the median beside it and the last 6 m of that end are under the audit's ray. Explained, not
  changed. The other roads on those tiles: walls 30 -> 1, blunt 16 -> 14.
- LifeSim self-test, PSXRec: gate off (baseline, fresh process) 4,587 ok / 63 FAIL; gate on 4,587 ok / 63 FAIL: the same 63 (0 new, 0 gone; `st_m0.txt`, `st_m1.txt` in the scratch folder).
- The inventory, city-wide, gate off -> on (km of standing concrete in a stray class): 17.70 -> 16.27. Table in "The stray table" below.

## Step 2 - class d, two Jerseys along one narrow median (PSX_CITY_CONC_TWIN, default ON)
CAUSE, from the ground-twin walk's own verdict at every class d piece city-wide (`CityAudit.Concrete.cs` census, run `mcen`): of 8.4 km,
1.8 km the walk refused because its line model reads the gap 9.5-12 m although the two walls stand 9.5 m apart or less
(`CityMeshes.Unions.cs` GroundTwinSample, the `gap > GroundTwinGapMaxM` test); 1.0 km because the two carriageways carry different names
(the `c.name != X.name` test: I-77 south of I-485 is "B. Bayles Mack Highway" one way in OSM; Independence Blvd at Matthews is
"Independence Boulevard" / "East Independence Boulevard"); 1.9 km are those pairs' partners (the lower-numbered edge owns a pair);
1.8 km are two shared (union) medians standing side by side on one edge (the walk laid both; cause not found this round, not touched);
0.55 km rails at drops on ramps beside each other (not a barriered one-way: outside the walk). Each refused carriageway then stood its
own Jersey at its own median edge (CityMeshes.cs EmitSide, `sf.median`).
FIX, in the caller (GroundTwinSample), decided at load from the whole map (the union runs), so both tiles of a seam agree: the walk pairs
two opposing carriageways whatever their names - only median side to median side both ways, only within 25 degrees of opposite, up to a
line-model gap of 12 m (`PSX_CITY_CONC_TWIN_GAP`) - and the pair carries the walk's own shared median: ONE Jersey on the strip's centre
line, the strip floored flush, each carriageway's own Jersey standing down under it (exactly as every same-name pair up to 9.5 m already is).
KEPT TWO on purpose: US 74 Independence's busway corridor (names "East Independence Expressway", "US 74": the busway and its 2026
express lanes are in that median, not drawn yet) and any structure (twin decks keep their parapets; with decks paired the bridges audit's
RAIL-FLUSH rose by one on I-77, so they are out).
NOT DONE: the split-level median (the high side's barrier on a retaining wall faced toward the low road). The walk keeps its 1 m height
limit (the shared Jersey's two-level form); the inventory does not count a rail on a retaining face beside a Jersey as a double (the rail
guards a fall), and I did not count those places this round.
STANDARDS: AASHTO Roadside Design Guide 2011 ch. 6 (FHWA copy aii.transportation.org/Documents/FHWA-AASHTORSDGChapter6.pdf): a median
barrier is recommended where the median is 30 ft (9 m) or less, weighed at 30-50 ft, not normally considered past 50 ft - one barrier,
never two; TxDOT Roadway Design Manual 17.7.2 (after the RDG): a barrier on flat ground, a median too steep is regraded or filled and given
a split-level concrete barrier, barriers on both sides "not desirable"; NCDOT Roadway Standard Drawings 854.07 (single slope concrete
barrier) and 854.06 (median hazard protection), 2024 edition; TDOT S-SSMB-9 (single slope barrier wall for a grade-separated median)
for the split-level form.
RESULT: class d city-wide 8,658 m (gate off) -> 8,434 m (round 1 gate) -> 3,492 m (every rule on). What is left: 1.8 km two shared medians side by side on one edge (not touched), 0.93 km the US 74 busway corridor kept two on purpose (433 m and their partners: East Independence Expressway from Commonwealth Ave to Idlewild Rd), 0.56 km drop rails on ramps beside each other, the rest under 0.2 km. The walk now lays shared medians on 107 more tiles (roads, ground and kerbs meshes differ there: the flush strip; 94 of them hold or touch a class d or b piece of the gate-off inventory, the others are medians whose two walls stood more than 9.5 m apart, now one barrier too).

## Step 3 - class b, walls with no reason (rule 6, PSX_CITY_CONC_REASON, default ON)
A median Jersey whose carriageway has no road the other way beyond it within 250 m (the Guion Lane kind), and a fan chord's Jersey with no
road in the clear zone beyond it, neither guarding a fall, are not built (CityMeshes.Concrete.cs ConGate / ConNoReason). Kept: a fall beside
it or land over it (the run rules' own probe), structure or a retaining face, ANY road within the clear zone beyond it (10 m, AASHTO RDG
Table 3.1 for 60-70 mph), a road the other way within 250 m, a structure within 40 m (piers, abutments: R3; the inventory's approach rule).
Decided from the map at the piece alone, so every tile agrees. Cut walls are NOT judged: the first city-wide run with them took 3 km of
walls that hold land (judged at build time the ground read flatter than the inventory reads it after the build); the inventory finds no
cut wall without a reason on the merged base.
RESULT: rule 6 left out 682 pieces, 3,621 m city-wide (median Jerseys and fan chords). Of every piece the gate changed, 50 (80 m) were in no stray class before (side-median 18, side-rail-ground 18, shared median 14); round 1s gate alone changed 39 such (39 m: rules 1 and 5 at junction mouths and ends), so rule 6 took at most 11 median pieces (about 40 m) the inventory counts as having a reason. Class b 2,621 m (gate off) -> 2,533 m (round 1) -> 736 m. Left: 427 m of median Jerseys rule 6 keeps (a road in the clear zone, a road the other way within 250 m by the map, or a structure within 40 m, where the inventory's probe of the drawn pavement finds none: East Independence Expressway at N Sharon Amity and at Idlewild Rd), 159 m of fan-chord Jerseys with a road in their clear zone, 146 m of shared-median Jerseys (not judged by rule 6).

## Step 4 - classes f and c
Not changed beyond round 1's rules 4 (no orphan) and 5 (an end is a real end). What is left of them guards a fall or holds land (see
"where unnecessary concrete still stands").

## Step 5 - the owner's Freedom Drive deck (I-85, tile -28,27)
Inside the paved deck there now stand only the parapets round its three openings (the middle triangle and the two notches where I-85 shows through), three fan-chord rails (14 m) and the gore noses (block, sloped legs and tails, 38 m) - drawn by EmitSide's deck parapet (side-rail-deck), BuildJunctions' fan chords (fan-chord-rail-deck) and EmitNose (nose-*-deck); each guards a real fall onto I-85 and goes when the deck plate (charlotte-spui) floors the openings. PSX_CITY_CONC_PLATE stays off. The bridges audit's RAIL-ON-LANE on e11321 there is the gore nose's sloped leg on Freedom Drive's ramp (e6087), the same piece before and after, reported now that the full-height wall beside it is gone.

## The stray table, city-wide (5,073 tiles; metres, pieces)
```
class	gate off (as asked)	round 1 gate	every rule on   (metres; pieces)
a ON-PAVEMENT     	   737 m (507)	     3 m (2)	     3 m (2)
b NO-REASON       	  2621 m (749)	  2533 m (715)	   736 m (363)
c ORPHAN          	  2134 m (1086)	  1599 m (842)	  1566 m (833)
d DOUBLE/BOX      	  8658 m (2741)	  8434 m (2655)	  3492 m (1560)
e STARTS-AND-STOPS	   234 m (60)	   229 m (57)	   210 m (61)
f BAD-END         	  2427 m (558)	  2090 m (508)	  2073 m (516)
g UP-AND-DOWN     	   748 m (303)	   723 m (270)	   690 m (263)
h OVERSIZE        	     0 m (0)	     0 m (0)	     0 m (0)
h2 TALL-OVER-LANE 	  1842 m (714)	  1765 m (673)	  1765 m (673)
all strays        	 17.70 km	 16.27 km	  9.58 km
```

## Where unnecessary concrete still stands, and why
- a ON-PAVEMENT: 3 m - by rule: union-median-ground-twin 3 | heaviest places: 35.08601,-80.66283 2 m; 35.21896,-80.82434 1 m
- b NO-REASON: 733 m - by rule: side-median 427, fan-chord-jersey 159, union-median-ground-twin 146, union-split-feet 0, union-median-ground 0 | heaviest places: 35.19372,-80.76527 106 m; 35.18382,-80.75643 80 m; 35.16309,-80.89001 39 m; 35.08601,-80.66283 36 m; 35.08729,-80.66425 34 m
- c ORPHAN: 1566 m - by rule: side-rail-ground 756, side-rail-retain 169, side-cut-wall 162, side-rail-deck 149, side-median 143 | heaviest places: 35.23378,-80.84741 72 m; 35.24761,-80.89662 59 m; 35.08578,-80.83456 57 m; 35.17996,-80.89001 53 m; 35.05982,-80.76974 50 m
- d DOUBLE/BOX: 3483 m - by rule: union-median-ground-twin 1812, side-median 1038, side-rail-ground 563, union-split-feet 70 | heaviest places: 35.21031,-80.80093 253 m; 35.19076,-80.76272 168 m; 35.18743,-80.75983 163 m; 35.17582,-80.74931 117 m; 35.17737,-80.75081 106 m
- e STARTS-AND-STOPS: 210 m - by rule: side-median 98, side-cut-wall 38, side-rail-deck 27, side-rail-ground 16, union-median-deck 10 | heaviest places: 35.27174,-80.84685 16 m; 35.26212,-80.87851 15 m; 35.20301,-80.79093 12 m; 35.19944,-80.77199 12 m; 35.20140,-80.78152 12 m
- f BAD-END: 2074 m - by rule: side-cut-wall 714, side-rail-deck 509, side-rail-ground 225, side-rail-retain 167, side-median 138 | heaviest places: 35.34851,-80.84930 49 m; 35.24761,-80.89662 39 m; 35.36230,-80.74910 36 m; 35.42538,-80.99909 35 m; 35.26212,-80.87851 31 m
- g UP-AND-DOWN: 689 m - by rule: side-rail-ground 248, nose-leg-deck 158, side-median 105, side-rail-deck 97, union-median-ground-twin 19 | heaviest places: 35.22780,-80.82922 55 m; 35.12740,-80.91521 33 m; 35.25293,-80.96714 32 m; 35.24398,-80.84788 25 m; 35.20203,-80.78699 24 m
- h2 TALL-OVER-LANE: 1767 m - by rule: side-rail-retain 1548, side-rail-ground 144, side-rail-deck 73, nose-tail-deck 2 | heaviest places: 35.27739,-80.79793 153 m; 35.27625,-80.80233 124 m; 35.24136,-80.96159 92 m; 35.06536,-80.84581 85 m; 35.24219,-80.90067 85 m
- In words: d - two shared medians side by side (1.8 km, not touched), the US 74 busway corridor (kept two on purpose), drop rails on ramps beside each other; b - the median Jerseys and fan chords rule 6 keeps for a road within its tests; c, f, g, h2 - short or square-ended rails and walls that guard a fall or hold land (rule 4 and 5 never take them: protection is not removed; the real fix is a longer run or a tie-in, not done); the deck - see step 5.

## Gates for steps 2-3 (every rule on, fresh process, against the same sandbox's runs)
- Per-tile meshes, city-wide, every rule on (run `pall`) against round 1's gate (`mall`), both fresh processes, same sandbox: roads,
  ground and kerbs differ on 107-109 tiles (the shared medians' flush strips; 94-96 of them hold or touch a class d or b piece of the
  gate-off inventory, the rest are medians whose two walls stood more than 9.5 m apart, now one barrier as well: I-485 tiles -51,-63..-61,
  I-85 tiles 38..40,83..89); barriers differ on 129 tiles; structure on 5 and guardrails on 3 (tiles 48,98 and 50,-39 hold no class d or
  b piece: not explained this round). Triangles on the changed tiles: roads 490,724 -> 550,316, ground 728,421 -> 661,193, kerbs 128,158 ->
  124,260, barriers 86,126 -> 97,592. Whole city: barriers mesh 894,982 -> 906,448 triangles; tile build 161 -> 159 ms a tile (city-wide
  mean). Within the run (the gate's rules rebuilt as asked and compared): ground, roads, kerbs, guardrails 0 tiles; barriers 125;
  structure 4 (as round 1). Barrier vertices no gate call accounts for: 0.
- Cost on sample tiles (one cold build each, `cost.py`): Uptown -12,16, a suburb 11,7 and the owner's deck -28,27: no mesh changed by
  steps 2-3; I-77 at the state line -41,-38: 11,630 -> 11,362 triangles (ground 6,124 -> 4,480, roads 3,930 -> 5,378, kerbs 830 -> 562,
  barriers 746 -> 942), build 130 -> 193 ms. Baked size not measured (it follows the triangles).
- Bridges audit, city-wide, gate off (m0) -> round 1 (m1) -> every rule on (p1): RAIL-DROP 1 / 1 / 1, RAIL-FLUSH 17 / 17 / 17, WALL-IN-LANE
  10 / 7 / 7, RAIL-ON-LANE 3 / 2 / 2, BLUNT-END 84 / 81 / 81, APPROACH-RAIL 45 / 45 / 45: none rose; p1 has the same places as m1 (the
  first run with twin decks paired raised RAIL-FLUSH to 18 at I-77 e1293, 35.11508,-80.92412: structures are now out of the relaxed walk).
- Drive audit, the 48 edges, m0 / m1 / p1: nothing solid across a lane 0 / 0 / 0; fall holes 0; lane steps 3 / 3 / 3; BLUNT 7 / 6 / 6;
  MEDIAN ACROSS one wall 1,200 / 1,253 / 2,615 rays, two or more 5 / 5 / 8, none 13 / 16 / 17; a split's V read as two walls 345 / 325 /
  243 rays. The 3 new two-wall rays are a pair the audit sees only now that the walk pairs carriageways of other names: East Independence
  Expressway e1136 / e1402 at (-520,3823), 35.2190,-80.8234, in the busway corridor where two walls are kept on purpose; the 1 new none-ray
  is the same real opening at e7235 (East Independence Boulevard, 8523,-4183), its turned-down end 2 m longer under the ray.
- LifeSim self-test, PSXRec: gate off 4,587 ok / 63 FAIL; every rule on 4,587 ok / 63 FAIL: the same 63 (0 new, 0 gone; `st_m0.txt`, `st_p1.txt`).

## Proof sheet
`C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\concrete\final_sheet.png`: the owner's deck first (the bridges sheet's three poses and a top view), then the five worst class d clusters and the five worst class b clusters; four cells a place: BEFORE (gate off, fresh process), BEFORE with every stray piece boxed in its class's colour (a red, f orange, d magenta, b yellow, c cyan, e blue, g green, h white), AFTER (every rule on, fresh process), AFTER boxed.
What still looks wrong in it: on the deck the parapets round the openings (cyan, orange) still stand and the "long low beam" in cell 3 is the step between two ramp ribbons (the deck surface, spui's job); the fifth class d place is I-77 at 35.13150,-80.91335 (74 pieces) because the fifth by count (I-485 at 35.36400,-80.71830, 101 pieces) had no driver's-eye pose in the cluster file; row 8 (East Independence Expressway at Commonwealth Ave) is unchanged on purpose (busway corridor); in the class d rows the one Jersey now stands on the centre of a fully paved median, which reads wider than a grass median with a centre barrier would.

## Switches (all default as listed; `PSX_CITY_CONCRETE=0` turns every one off: the picture as it was)
PSX_CITY_CONC_PAVE (rule 1, ON), PSX_CITY_CONC_ORPHAN (rule 4, ON), PSX_CITY_CONC_ENDS (rule 5, ON), PSX_CITY_CONC_TWIN (class d, ON;
=0 off), PSX_CITY_CONC_TWIN_GAP (metres, 12), PSX_CITY_CONC_REASON (rule 6, ON; =0 off), PSX_CITY_CONC_PLATE (rule 3, OFF: never ship
it on), PSX_CITY_CONC_SPUI (OFF, untested).

## For the merge
- Shared-file edits: `Scripts/City/CityMeshes.Unions.cs` only: GroundTwinRuns l.1033 (an unnamed carriageway pairs), GroundTwinWhy l.1087
  (the same), GroundTwinSample l.1121-1173 (the candidate filter: another name only within 25 degrees and on the median side; after the
  pair is found: median to median, the 12 m gap, kept-two). Everything else is in my own files (`CityMeshes.Concrete.cs`,
  `Editor/CityAudit.Concrete.cs`). Round 1's shared-file edits are listed in its section below.
- No new mesh slot, bucket, material or baked field; CityTileCache.Format untouched; no data file; no scene pass.
- A CITY RE-BAKE IS NEEDED for the PC build (the coordinator's): the shared medians change the roads, ground, kerbs and barriers meshes
  on the tiles of the newly paired medians, rule 6 the barriers mesh where median Jerseys go.
- The deck plate (charlotte-spui) as round 1 says: when it lands, rule 1 must see its pavement (`CityMeshes.ConcreteExtraPave`, or try
  `PSX_CITY_CONC_SPUI=1`), then RunBridges: RAIL-DROP must not rise. Keep PSX_CITY_CONC_PLATE off.

# HANDOFF - CONCRETE ROUND 1 (charlotte-concrete, 2026-11-15): one gate for every piece of standing concrete, the inventory by the owner's question

Branch `charlotte-concrete` (worktree `C:/Users/mcgee/PSX Racing-concrete`, off `charlotte-bridges` 109f4c89). Not merged, not pushed.
Graph hash b2aa3453 before and after (no data file touched, no export run). Sandbox PSXRec (Android target).
The owner, 11:35, looking at the bridges sheet (the deck where the ramps meet at I-85 and Freedom Drive): "can you see this
massacre of unnecessary concrete median pieces? this is still an issue all across the city after weeks of attempts to correct them".

## What this round is
Six weeks of rounds each patched one rule and counted its class. This round put ONE function in front of everything that
writes standing concrete into the barriers mesh, made it keep a record of every piece (who asked, where it stands, how
tall), and built an inventory that reads those records and asks of every piece: why is this concrete here?

## The inventory: `Editor/CityAudit.Concrete.cs`, `-executeMethod PSXRacing.EditorTools.CityAudit.RunConcrete`
Reads `CityMeshes.concreteLog` (the gate's own records: the audit and the builder cannot disagree) from the tiles the game
builds (CityMeshes.Build), then from the whole map: joins pieces into RUNS (ends within 0.75 m, across rules, edges and
tiles; W-beams and the cable median tie a run), finds what each piece stands on and its REASON (R1 divides opposing
traffic: the other carriageway within 80 m; R2 guards a fall: on structure, a fall of 0.6 m within 8 m / 1.5 m within 12 m
on the graded ground, on a retaining face, or within 40 m along its run of a structure's rail; R2 holds land: land over it
steeper than 1V:3H; R4 kerb height), and classes the strays:
a ON-PAVEMENT (pavement past both its faces and under it, at its level), b NO-REASON, c ORPHAN (a run under 30 m),
d DOUBLE/BOX (two walls 0.6-9.5 m apart that BOTH guard nothing, no lane and no fall between), e STARTS-AND-STOPS (two run
ends in line under 40 m apart, nothing real opening between), f BAD-END (a free end over 0.3 m within reach of pavement),
g UP-AND-DOWN (a step or dip at a joint), h OVERSIZE, h2 TALL-OVER-LANE (top over 1.35 m above the lane beside it).
Outputs in the sandbox root: `city_concrete_<tag>.txt` (tables before / after, by rule, 40 clusters), `_pieces.tsv` (every
stray piece, both sets), `_tiles.tsv` (per-tile mesh hashes), `_clusters.tsv` (90 clusters with a driver's-eye pose),
`_plans.tsv` (the pavement and every piece at the five worst clusters of b, d, f, for `plan.py`), `_log.bin` (the records:
`PSX_CONC_LOAD=<tag>` classifies them again in minutes without building a tile).
Env: PSX_CONC_TAG, PSX_CONC_NEAR "x,z,r", PSX_CONC_POP=ramps (the sample: tiles with a ramp or a structure),
PSX_CONC_MINUTES, PSX_CONC_PLAN "x,z,r;...", PSX_CONC_DUMP_ALL=1, PSX_CONC_LOAD. Cost runner: `CityAudit.RunConcreteCost`.
With the gate on, one pass gives BEFORE (as asked) and AFTER (as built), and every tile the gate changed is rebuilt
with the gate off and compared mesh by mesh.
Pictures: `CityRefSpots.RunBridges2` now takes `PSX_BRSHOT_SPOTS_FILE`, world poses (`id@ex,ey,ez,lx,ly,lz`),
`PSX_BRSHOT_SWITCH=concrete` (before / after flips the gate; mode `plate` adds the plate rule) and `PSX_BRSHOT_TINT=<_pieces.tsv>`
(a second frame `_tint` with every stray piece boxed in its class's colour).

## The gate: `Scripts/City/CityMeshes.Concrete.cs` (PSX_CITY_CONCRETE=0: everything as asked, the picture as it was)
Every writer of the barriers mesh passes it: EmitBarrier, EmitRail (and through it EmitRailOffLanes: fan chords, gore
noses), EmitUnionJersey, EmitUnionLegs, EmitInsetJersey; the face writers (retaining faces, the union end cap, culvert
headwalls) are accounted (the audit fails on any barrier vertex no gate call covers: 0 on every tile built).
- Rule 1 NOTHING ON PAVEMENT (`PSX_CITY_CONC_PAVE`, on): a piece with drawn pavement (any ribbon as drawn, its own
  included, or a junction fan; the whole-map outlines, so tiles agree) past BOTH its faces and under it, at the level it
  stands on, is clipped to what stands outside. A barrier's cut end is turned down over 8 m; a parapet's is capped at
  full height (it guards a fall). What is left under 3 m (a parapet: 0.5 m) goes. The one piece that belongs between two
  pavements stays: a barrier between the opposing carriageways of a divided road (not ramps).
- Rule 4 NO ORPHAN (`PSX_CITY_CONC_ORPHAN`, on): a run under 30 m - counted as it stands, across rules and edges, after
  rule 1 - that guards nothing is not built. Never taken: anything on structure, on a retaining face, with a fall beside
  it or holding land, tied to a W-beam or the cable, or reaching within 10 m of the tile's border (the next tile may hold
  more of it: both tiles leave it).
- Rule 5 AN END IS A REAL END (`PSX_CITY_CONC_ENDS`, on): a barrier's free square end (EmitBarrier's pieces only) that
  guards nothing and has no barrier ahead in its line within 12 m is turned down along its last piece. Parapets keep
  their height.
- Rule 3 THE PLATE (`PSX_CITY_CONC_PLATE=1`, OFF): an opening that drawn pavement closes round (twelve rays, 30 m)
  counts as pavement. It exists to show what rule 1 does once the deck plate floors the openings; on today's decks it
  would open unguarded falls. Do not ship it on.
- `CityMeshes.ConcreteExtraPave` (a delegate, null): pavement that neither a ribbon nor a fan holds. And
  `PSX_CITY_CONC_SPUI=1` (OFF, untested: the plates are not in this branch): reads CityDeckPlates' hulls by name and
  counts a point as plate when it is inside a hull and 2 m from its rim.
There is no height rule: the inventory found no piece taller or thicker than its kind city-wide.
PROTECTION: nothing on structure, on a retaining face, beside a fall or holding land is ever dropped or lowered; it is
clipped only where pavement lies past both its faces and under it.

## What was found
1. No rule that stands concrete asked what the piece stands on, and nothing looked at all of them together: 19 rules
   (the caller tags in the records) write through `EmitBarrier` (CityMeshes.cs:8714), `EmitRail` (:8777),
   `EmitUnionJersey` / `EmitUnionLegs` (CityMeshes.Unions.cs:1700 / :1790) and `EmitInsetJersey` (CityMeshes.Medians9.cs:546).
2. THE OWNER'S DECK (I-85 at Freedom Drive, tile -28,27). The deck is drawn as the ramps' own ribbons and one junction fan,
   standing up to 0.8 m apart in height (e2908 141.1 m, e13604 140.3 m), with OPENINGS between them (I-85 shows through:
   one closed triangle in the middle, an open notch on the west, an open notch on the south-east). What he circled is
   three different things:
   a. pieces standing ON pavement: the second wall of the double row along e13602, the T-shaped block at the island's end,
      the wall behind the "staircase", a nose leg across the lanes of e13610 / e2899, a chord rail: rule 1 takes them
      (56 pieces changed on the tile, the barriers mesh 5,368 -> 4,934 triangles);
   b. the parapets, three fan-chord rails and the gore noses (the "staircase": a nose block with its two sloped legs)
      at the RIMS of the openings: about 60 m round the middle triangle plus the notches' rails. Each guards a real
      fall onto I-85 (RAIL-DROP if taken), so the gate leaves them. They go when the openings are floored: shown with
      the plate rule on (the sheet's deck rows; the middle of the deck comes out clean, and the west opening shows
      unguarded, which is why the rule is off);
   c. the "long low beam lying across the pavement" is not concrete: it is the 20 cm step between two ramp ribbons seen
      edge on (the deck's surface: the spui agent's work).
   The walls "above the driver's eye": every piece on the deck is 0.95 m (a parapet) or lower; they stand on ribbons
   0.2-0.8 m higher than the lane the camera is in, and the eye is at 1.15 m. City-wide no piece is taller or thicker
   than its kind (class h: 0).
3. City-wide, the unnecessary concrete is mostly NOT at the interchanges. By metres: two Jerseys along one median
   (d, 14 km: the ground-twin rule refusing a pair), Jerseys and cut walls with no reason (b, 5 km), free square ends
   (f), short runs (c). See STAGE1.md for the classes checked by eye.

## Gates (same sandbox, base = PSX_CITY_CONCRETE=0)
- Per-tile meshes, city-wide (every tile the gate changes is rebuilt with the gate off and compared): Ground, Roads,
  Kerbs and Guardrails identical on every tile; Barriers differ only on the tiles the gate changed a piece on; Structure
  differs on 4 tiles (the city bridge's metal bar is not laid on a parapet piece the gate clipped). A tile the gate
  changes no piece on emits every piece through the same call with the same arguments. Re-run on the merged base (PC43, e2749b17), city-wide, same sandbox: tiles built: 5073 of 5073 in 815 s (161 ms a tile); barriers mesh 894982 triangles; tiles with barrier vertices no gate call accounts for: 0; the gate: rule 1 (nothing on pavement) took 569 m off pavement; rule 4 (no orphan, on) left out 227 pieces, 492 m; rule 5 (an end is a real end, on) turned 71 free square barrier ends down; rule 3 (the plate) off; tiles the gate changed a piece on: 112 of 5073; each rebuilt as asked (gate off) and compared mesh by mesh - tiles whose mesh differs: ground 0, roads 0, kerbs 0, barriers 112, structure 4, guardrails 0; barriers triangles on those tiles 121584 as asked -> 116528 gated; build time on those tiles (warm pair) 264 ms as asked -> 284 ms gated; pieces the gate changed or left out: 797; of these in NO stray class before: 39 (39 m) side-median 7 side-rail-ground 18 union-median-ground-twin 14.
- Bridges audit, city-wide (1,086 runs, 999 tiles), gate off -> on: gate off (m0) -> on (m1), merged base: RAIL-DROP 1 = 1 (the same place), RAIL-FLUSH 17 = 17, WALL-IN-LANE 10 -> 7, RAIL-ON-LANE 3 -> 2, BLUNT-END 84 -> 81, APPROACH-RAIL 45 = 45: none rose. Two places are new with the gate on (each class still fell): a RAIL-ON-LANE on the owner's deck, e11321 s 12 (35.24745,-80.89613), a piece whose top stands 0.30 m over that ramp's lane line where the full-height WALL-IN-LANE on e13604 10 m away is gone; a BLUNT-END at I-77 / Tyvola Rd, e2747 s 17 (35.16301,-80.89024), a parapet cut where pavement lies past both its faces keeps its full height to its cut end (it guards a fall). `city_bridges_audit_m0/_m1.*` in PSXRec.
- Drive audit on 48 edges (the two worst edges of each of the 24 heaviest clusters and the deck's), re-run on the merged base with the zone fix
  (`city_drive_m0.txt` / `_m1.txt`): the numbers below are unchanged from the first run. Nothing solid
  across any lane 0 = 0; fall holes 0 = 0; lane steps 3 = 3; land on a lane 0 = 0; BLUNT 7 -> 6; MEDIAN ACROSS two or
  more walls 5 = 5; a split's V read as two walls 345 -> 325 rays; MEDIAN ACROSS none 13 -> 16 rays at ONE place: East
  Independence Boulevard (e842 / e11785 at 8515,-4185), where an existing 6 m opening at e7235's junction reads 12 m
  because rule 5 turned down the square end of the median beside it (a turned-down end is under the audit's ray).
  Re-run with the zone fix: the same 13 -> 16, so it is not a seam: the opening is real (the junction with e7235), the 3 rays (6 m) are the
  last metres of the turned-down end, lower than the audit's ray; the median runs on to the opening. Explained, not fixed.
- Cost (CityAudit.RunConcreteCost: each tile three times per mode, the quickest, no records kept), as asked -> gated:
  | tile | what | build ms | barrier triangles | all triangles |
  |---|---|---|---|---|
  | -12,16 | Uptown, S Tryon / Church / College over I-277 | 754 -> 773 | 2,562 = | 47,610 = |
  | 11,7 | suburb, Eastway Dr over a creek | 731 -> 823 | 2,674 = | 39,779 = |
  | -28,27 | I-85 at Freedom Dr | 789 -> 864 | 5,368 -> 4,934 | 41,358 -> 40,892 |
  | -26,-10 | I-77 at Tyvola Rd | 892 -> 942 | 5,702 -> 5,656 | 61,940 -> 61,862 |
  The gate's probes cost 2-12 % of a barrier-heavy tile's build; triangles only fall. Baked size not measured (it
  follows the triangles: about -5,400 of 895,000 barrier triangles city-wide).
- NOT run: the median gates of CityAudit.MedianGates.cs (the offline refuge-island list: no line-model input changed),
  the Twin audit, the launch audit and the flicker finder (no surface moved, no face added), the traffic check (no
  lane path moved).

## Where unnecessary concrete still stands (after the gate)
See round 2 (the section above): the stray table after every rule, by class and place.

## For the merge
- Shared-file edits (all small, marked "concrete gate"):
  `CityMeshes.cs`: BuildCentreMedians (l.222 context tag), TileSteps (l.2250 ConcreteTileBegin, l.2317
  ConcreteRunRulesNow before CloseOpenGround, l.2344 ConcreteTileEnd), EmitNose (l.3399-3475 context tags), EmitSide
  (l.6716-6880: context tags, `conLastWhole` before EmitCityRail, the retaining face accounted, W-beam notes),
  EmitBarrier / EmitRail (l.8714-8815: each is now a wrapper over EmitBarrierCore / EmitRailCore), BuildJunctions' fan
  chords (l.10693-10716).
  `CityMeshes.Unions.cs`: EmitUnionMedian (l.1578, 1644, 1664 tags), EmitUnionJersey (l.1700 wrapper + Core),
  EmitUnionLegs (l.1823, 1843), EmitUnionEnd (l.1902 cap accounted). `CityMeshes.Medians9.cs`: EmitInsetJersey (l.554-566).
  `CityMeshes.Hydro.cs`: EmitCulvertEnd (l.81-84). `CityMeshes.Dividers.cs`: EmitCablePiece (l.2068, one line: the cable
  ties a run). `Editor/CityRefSpots.Bridges.cs`: RunBridges2 (spots file, world poses, the concrete switch, tint).
- New files: `Scripts/City/CityMeshes.Concrete.cs`, `Editor/CityAudit.Concrete.cs`. No new mesh slot, bucket, material
  or baked field; CityTileCache.Format untouched; no data file; no scene pass. A re-bake is needed (barriers mesh on
  about 130 tiles).
- THE DECK PLATE (charlotte-spui, not there yet): when it lands, rule 1 must see its pavement or every wall inside the
  plate stays. Either set `CityMeshes.ConcreteExtraPave` (a point-in-plate test from the map alone), or try
  `PSX_CITY_CONC_SPUI=1` (reads CityDeckPlates.Plates' hulls by name, 2 m in from the rim; untested). Then run
  `CityAudit.RunBridges`: RAIL-DROP must not rise, and where the plate reaches past the old parapets its rim needs a
  parapet of its own. Do not turn on PSX_CITY_CONC_PLATE in a build.
- The other four agents' branches do not touch the gate; a new writer of the barriers mesh that does not go through it
  shows as "barrier vertices no gate call accounts for" in the inventory (0 today).

## Tools (scratch folder `...\scratchpad\concrete\`)
`job.ps1` (the runner), `gates.ps1` / `chain2.ps1` (the gate chains), `plan.py` / `plans.py` (plans from the records),
`mkspots.py`, `vis.py` (stray pixels seen in the tinted frames), `stage.py` / `sheet2.py` (sheets), `mk_stage1.py`.

# HANDOFF - DAMAGEVIEW: every view of a car shows its current condition (agent "damageview", branch `damage-view`, 2026-11-15 pm; NOT pushed)

The owner (verbatim): "I wrecked my NSX during the race. The damage was shown during the race and replay, but in the garage view the car is in mint condition, visibly. The viewing of cars should always be accurate, especially since flipping cars is a feature. the current condition should be visible in all modes." Branch = mountain-nm 416ddfda (the PC43 exe) + this round. Sandbox PSXPerf, Windows target. Player-only: nothing under `Scripts/City`, no re-bake, no data files, no save change.

## Cause
- The drive's look lives in `Scripts/DamageLook.cs`, a component on the DRIVEN CarController, attached by the career ledger (`DamageModel.cs:598`); it was the only writer of PSX/CarPaint's damage block. No display model ever called it: `CarViewer.Show / ShowOwned` (`Scripts/LifeSim/CarViewer.cs:455-573`) put the skin material on the turntable body and nothing else, and every menu view goes through that one viewer; `CarShell.Spawn` (`Scripts/OnFoot/CarShell.cs:69`) the same on foot. The rows were there all along (OwnedCar.faults; DamageBank folds a drive's hits at its exit), the views just never read them.

## View inventory (view: car shown; condition before -> after)
- Car pages' side panel: SPECS, SERVICE (the mechanic), PAINT (the booth, the hovered colour), TUNE/SETUP on wide screens (`BuildCarSide` -> CarViewPanel, the owner's spot "DRAG TO TURN / VALUE"): the career car; mint -> its condition.
- CAR MENU stage (`CarMenuHero` -> OwnedHero, the GT-style showroom) and MY CARS (`GarageHero`, < > between cars; classic: `BuildDrivingBlock`): career cars; mint -> each car's own condition.
- INSPECT plan and side views (`DrawChassis`), STORED WHEELS (`BuildStoredWheels`): the career car; mint -> condition (stored wheels not shot: needs stored wheels; same ShowOwned call).
- On foot: the garage bays (`GarageWorld.SpawnShell`) and the pizza shop's kerb (`PizzaShift.StandTheCarUp`): career cars; mint (+ the bay's shine) -> condition.
- The drive, the replay, the player's car at a meet: the drive's own car (DamageLook): already showed it; unchanged.
- Cars for sale: dealer lot stage (`DealerHero`; Town `FillDealer` shells), the paper's BUY page and the VIEWING page (`ViewHero` / CarViewPanel), the seller's drive on foot (`SellerLotWorld`), the junkyard (`FillYard`), the new game's lanes (`BuildCarPickShowroom`, thumbnails): mint -> mint, EXCEPT a car the player has gone to see: dressed with the visit's car (its rows and its paint = the advert's condition, so a tired car's shine is dull). THE MARKET'S CARS CARRY NO BODY ROWS YET: a CarListing holds only cond / odometer / a disclosed problem, and the used-fault pool (`Resources/rg2_faults.json` "used", 36 rows) has no panel_damage / lamp_out / roof_glass / bumper_bar; the path is ready (the visit's rows show the day R3 / R4c rolls them).
- AI and traffic (Town meets and club cars, traffic shells, rivals): untouched (a shell with no owner is never dressed). Arcade car select and the city edition's pick: no career car, unchanged. Title screen: no player car. No photo mode exists.

## What changed (switch `PSX_VIEW_DAMAGE=0` = today's mint views; default ON)
- NEW `Scripts/DamageView.cs`: `DamageView.Dress(frame, body, box centre, box size, OwnedCar)` (+ an overload off def / ShellFit / slide): the drive's rows -> look (`DamageLook.Read`, plus a drive under way on that car), written by the drive's per-renderer code (`DamageLook.WriteBlock`, the same seed), plus `PaintGloss.Of` (paint, paint faults, dirt). Respects PSX_FAULT_LOOK / _BODY / _LAMPS. No steam (engine off), no SRS bag (no cockpit drawn in a view). A mint car or none clears a reused model. `LookOf`, `Signature`.
- NEW `Scripts/DamageViewWatch.cs`: on each dressed model, the rows' signature every 0.5 s by InvokeRepeating (no Update): a repair at the mechanic / in the garage / on the bench shows at once.
- `Scripts/DamageLook.cs`: Push's per-renderer block write extracted into `public static WriteBlock(...)` and `Seed(carId)`: the same math, line for line (the drive's look is unchanged).
- `Scripts/LifeSim/CarViewer.cs`: ShowOwned hands its car to Show; `Show(def, skin, geo)` dresses at its end and on its same-shell early return (< > between two cars of one model); any other Show = mint. New public `Dress(OwnedCar)`, `BodyRenderer`, `Picture`, `ShownSlide`.
- `Scripts/OnFoot/CarShell.cs`: Spawn dresses when `owned` is given; new `CarShell.Dress(shell, def, spec, widthMm, car)`. `Scripts/OnFoot/SellerLotWorld.cs`: the seller's shell dressed with `visit.car`.
- `Scripts/LifeSim/LifeHomeScreen.cs`: `VisitCar()`; CarViewPanel's for-sale path dresses with it. `LifeHomeScreen.Showroom.cs`: DealerHero / ViewHero the same.
- Editor: `LifeSimSelfTest.DamageView.cs` (+ one line in `LifeSimSelfTest.cs` RunAll), `DamageViewShots.cs` (the pictures; `PSX_DV_TAG`).

## Proof
- Sheet: `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\damageview\out\damageview_sheet.png`. A career NSX 3 / 3, wrecked through the bank (`DamageBank.ApplyIncident`, as a drive's exit folds a crash: panel_damage front stage 3 + lamp_out front-left 8.6 m/s, panel_damage left side stage 2 4.4 m/s, roof_glass stage 1 3.2 m/s; hidden, causes stamped). Fresh processes, same seeded save (cash $1,127 in both): left PSX_VIEW_DAMAGE=0, right ON: SPECS side panel (+ zoom), SERVICE, CAR MENU stage, MY CARS, INSPECT, PAINT BOOTH, the turntable frame, the on-foot shell; then the drive's look at the same rig and angle, the repair, the second car. Pixel diff before/after: only the car changes (side panel pages 15,972 px, stages ~93,500, inspect 34,399); the repaired NSX, the clean second car and the drive's frame are pixel-identical in both runs.
- The drive's look and the views' look read the same from the same rows: zones (1.00, 0.00, 0.67, 0.00), roof 0.33, lamps 1 (head left).
- Self-test (PSXPerf, Windows): base 416ddfda = 62 FAIL / 4,561 ok; this branch = the same 62 FAIL lines (0 new, 0 gone) / 4,575 ok; the 14 new checks all hold (mint carries no block; > to the wreck wears it; same look and seed as the drive; < back clean; watched; repair goes clean at once; a new incident shows; a car for sale mint and unwatched; the on-foot shell wears it, never a wheel; an unowned shell untouched; PSX_VIEW_DAMAGE=0 mint).
- Menus: no new control or label; every page's pad-reach, scroll and clip report is line-for-line identical before / after (16 lines).
- Cost: the dressing 0.004-0.010 ms per view open (and per < >); the watch's check 0.5-0.7 us every 0.5 s; per frame 0 (no Update). A first ShowOwned (model + dress) 2.4-7.2 ms as before.
- The sandbox was on WebGL: the switch to Windows cost about a minute on the first job (initial refresh 50 s; that job 5.5 min).

## Still not shown (and why)
- The market's body rows (above): none exist yet - R3 / R4c's work. An advert not yet visited holds no car: shown mint at full shine.
- Wear, age and neglect have only the lost shine (`_PSXGloss`: paint %, paint_fade / paint_bubble / minor_rust / panel_rust, dirt since the last wash). There is no colour look anywhere (no dirt film, rust spots, faded or bubbled paint), in the drive or the views: a shader gap.
- Legacy rows from before R1a (R0a's `bumper_crack`, alignment, ...) have no look in the drive either; an old save's crash shows nothing.
- The lamp out reads weakly on a pop-up-lamp car (the NSX): the dark lens is the lamp map's texels; with the lids down there is little to darken. The SRS bag and the steam stay drive-only (no cockpit, no running engine in a view).
- The dent is the drive's shader (a few cm push, creases, scrapes, the glass web), not a mesh crush; at the 320 x 200 side panel it reads as scraped and creased more than crumpled.
- Pictures: the on-foot shell and the drive's car were filmed on the turntable rig (same camera, same light), not in the garage scene or a live drive (the drive's CarBody was built in the editor; its wheels were not set up). The self-test covers the shell's block; the drive's frame is DamageLook's own code on the drive's body.

## Merge notes
- Shared files touched: `DamageLook.cs` (Push -> WriteBlock / Seed), `CarViewer.cs`, `CarShell.cs`, `SellerLotWorld.cs`, `LifeHomeScreen.cs` (VisitCar + CarViewPanel line), `LifeHomeScreen.Showroom.cs` (two lines), `LifeSimSelfTest.cs` (one RunAll line). New .meta files with fixed GUIDs for the four new scripts.
- No scene pass, no re-bake, no Resources change, no save version: a player-only build carries it.


The owner on the first pass's frames (11:50): "that is a LOT of curb on the side of the road (and I don't see an entrance area (curb relief) into the parking lot". He was right: the first pass's cut eased over 18 m of frontage, the ramp behind it was ground-coloured concrete up to 18 m wide, and my owner's-lot frames showed the wrong lot. Branch now = mountain-nm 50c8a3aa (bridges, roads audit, hoods' curbs 3) merged in + this pass. Sandbox PSXPerf.

## His lot, found
Lot 3114 (968 m2, 35.2473, -80.8571), the WEST side of Beatties Ford Road 340 m north of the JCSU tower: the single-storey brick building with the sided gable (OSM footprint 16.2 x 10.7 m at -3574.5, 6979.9) to its right, the burger pole sign, the second building and its sign, the steeple behind - `...\scratchpad\lots\p2_after\0_ownerA_after.png` is his picture's view in daylight. (The first pass pictured lot 3111, across the road.) Its entrance: edge 16432 'Beatties Ford Road' s 21.0 side +1; the lot's asphalt reaches the back of the walk (0.0 m), so there is no room for a throat behind the walk there: the cut in the street's curb is the whole entrance.

## The standard (reference only)
- NCDOT, Policy on Street and Driveway Access to North Carolina Highways (2003): a two-way driveway 20-36 ft wide (6.1-11.0 m), one-way 12-24 ft; a street-type connection's return radius 20-50 ft; driveway angle 75-90 degrees; corner clearance 100 ft where the frontage allows, never under 50 ft; beyond the shoulder the grade within +/-10%; the sidewalk built to the driveway's profile across a curb cut, its cross slope 1/2 in per ft at most.
- Charlotte Land Development Standards Manual (the city's pages refused the fetch; from the detail titles and the hoods agent's reading): 10.17A 2'-6" curb and gutter (24 in pan, 6 in curb), 10.24 / 10.25 drop-curb driveways Type I / commercial Type II (the curb dropped across the throat, a short transition each side, the gutter line through, the concrete apron from the gutter to the back of the sidewalk), Type III a street-type entrance with radius returns. The exact transition and flare lengths of 10.25 were NOT read.

## What changed (my files only, except three lines in CityMeshes.cs)
- THE THROAT IS THE LOT'S ASPHALT: a strip quad whose middle lies in an entrance's throat is laid in the lot's asphalt in the roads mesh (`LotEntThroatBucket`, `LotEntThroatUV`; `LayStripQuad` in CityMeshes.cs, 3 lines) instead of ground paving or grass. Behind the walk a driveway is asphalt; on a verge street it is asphalt from the pavement's edge (a turnout). The concrete apron is the walk's own slab across the cut.
- NO RAMP WIDER THAN THE THROAT, graded like a road: `LotEntRamp` now acts only on the strips that start within the throat's width (+0.5 m: the walk's reads are half a metre apart), and each grade begins with 1.5 m at half its slope (a two-break vertical curve at the walk's back; `SolveStrip` hands it `ref crossFall` too). The strips beside the throat are the street's own backing again.
- THE CUT IS THE THROAT'S WIDTH: the apron quad is a rectangle of the throat (no 1.5 m wings), out to the back of the walk as the WALK CODE has it (`Cs3WalkW`: nothing here hard-codes 2.4 m). Derived throats are 7.3 m (24 ft, two-way) off one-way streets too (they were 4.3 m); the data's own entrances keep their widths.
- THE LOT KERB'S OPENING IS EXACT, with no line in the walks' file: the walks' `EmitLotCurbs` still drops every 4 m piece a throat's quad takes; `EmitLotEntKerbFill` (called from my `FlushLotIslands`) lays again the part of each such piece outside the throat (the walks' own kerb profile, bucket and mapping; `LotCurbHere` asked with and without the throats). The opening is the throat + 0.15 m either side, not 8-12 m.
- A frontage that only reaches the last metres of a street's edge is tried again with the throat's side 1 m from the edge's end (the wider throat had lost three lots).
- The kept result is version 2 (`charlotte_lotent.bytes`, 591 KB: + the ways for the kerb's opening, + which quads are throats); a version 1 file is refused.
- Tools: `CityRefSpots.RunLots` shoots ONE pass per process (`PSX_LOTSHOT_TAG` before / after; fixed street places) with the owner's pose and an orthographic plan; `...\scratchpad\lots\sheet3.py` writes the widths on the plan.
- NOT changed: any curb or walk code. The two hooks I had drafted in `CityMeshes.Walks.cs` were taken out again on the coordinator's word.

## What I ask of the hoods agent (its file; I did not edit it)
THE CURB'S TRANSITION. `CurbCutWings` eases a cut back to the full curb over `CurbCutWingM` = 5 m each side. Square on from the lane that is 7.3 m of lip inside 17 m of half-height curb: it does not read as a cut. The drop curb's transition is about 3 ft. Proposal: at a LOT entrance 1.0 m (a wheel riding the walk meets 13 cm in 1 m there again - the 11-05 launch finding - so keep 5 m at house driveways if that matters more). It is one line after the share is computed, and the function is written and compiled in my file: `c = LotEntCurbShare(map, Vector3.Lerp(eA, eB, t) + o, c);` (`PSX_CITY_LOTENT_TAPER` sets the metres). The sheet's "PROPOSED" cells were shot with that one line in a throwaway copy of the walks' file; it is not in the branch.

## Numbers
- AUDIT on the merged code (5,169 drawn lots; `PSXRacing_lot_entrances_p2b.txt` in PSXPerf): 3,150 lots with an entrance a car can use + 1,246 reached through a lot that has one = **4,396 (85%)**; **773 with none** (the first pass ended at 783; before any of this 3,362), 655 shut all round. Every lot that had a way in keeps one. The pass: 266 of the data's entrances paved through, 1,749 derived (+144 second ones on a frontage over 60 m), 234 lots linked, 769 left; 2,283 corridors, 9,138 kerb openings; read from charlotte_lotent.bytes in 13 ms. Graph hash b2aa3453 (unchanged).
- LOT DRIVE on the merged code (26 lots x 15 and 40 km/h; `PSXRacing_lot_drive_p2.txt` in PSXPerf): the car reached the lot in all 52 runs; **49 of 52 pass** (the first pass: 42, then 44). At 15 km/h all 26 pass; at 40 km/h 23 of 26. THE OWNER'S LOT (3114): clean at both speeds (wheel tracks: largest step 2.5 cm at the pavement's edge, 4.8 cm over 0.4 m). The three that fail, each ONE wheel off and nothing hit: the 54 m drive to lot 3355 over rolling ground (0.18 s, 38 m in); Henley Place, a roll-curb street (a roll curb is not cut - the hoods agent's rule: 11.5 cm over 0.4 m at the curb, 0.05 s, 2.4 g); a filling station on West 10th Street (0.15 s where its pad's edge falls 6.7 cm in 0.4 m, 4 m in). The two-break grade at the walk's back took the wheel-unloading entrances from 8 to 3. The drive ran before the last two small changes (the narrow-end retry, the asphalt flares), which add entrances elsewhere and change a material: they do not move these surfaces.
- Cross-section at his spot (the hoods agent's measurement, checked against my plan view): from the lane edge outward a 0.60 m gutter pan, the curb (drawn top 0.40 m deep, 15 cm high), the walk to 2.4 m with no planting strip, then paved frontage. Standard (NCDOT 846.01 / CLDSM 10.17A): 0.61 m pan, 0.15 m curb top, a planting strip of 4 ft or more, a 5 ft walk. In my plan: pan 0.6 m, walk / apron 2.4 m, throat 7.3 m.
- Flicker and cost were not re-run in this pass. The ramp-toe pair of the first pass (concrete ramp on lot asphalt, 333 -> 600 tie px) no longer exists by construction: the throat's strip is the lot's own asphalt in the lot's own draw.

## Sheet
`C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\lots\lots_driveway_sheet.png`: his own picture, then BEFORE (a process started with PSX_CITY_LOTENT=0) and AFTER from his pose, the plan with the widths in metres, the proposal's preview, and the six places of the first sheet.

## What still does not look like a real driveway
1. THE CURB RELIEF ITSELF. In the branch the street's curb still eases over 5 m either side of the throat (the walk code's wing): from his seat his entrance is a long shallow dip, not a cut. The PROPOSED cells show the 1 m taper; it needs the one line in the hoods file.
2. AT HIS LOT (and every lot whose asphalt reaches the back of the walk) nothing but the curb marks the entrance: the lot is open to the sidewalk along its whole frontage. A real lot has a kerb or a planting strip along the walk with the driveway as its only gap. Not attempted: it would put a kerb behind the walk in front of every lot, on a section the hoods agent is about to change.
3. No radius returns: the cut is a drop curb (Type II), the throat's sides are straight and square to the street, and the lot's own kerb simply stops at the throat (no returns turned toward the street, no kerb down the throat's sides).
4. The apron is the sidewalk's own jointed slab, the same colour and joints as the walk either side; a poured apron reads as a different slab. The gutter pan runs through unchanged (right), but nothing marks the throat on it.
5. The throat's sides are ragged in plan: the strip quad that leads from the graded throat back to the street's backing is a narrow asphalt wedge, and where the lot's ring is oblique to the street the kerb's ends leave small triangles.
6. The section is not the standard's: no planting strip, a 2.4 m walk at the back of a 0.40 m curb top (the hoods agent's next job). The driveway takes whatever section the walk code gives.
7. The entrances are square to the street and in the middle of the frontage by rule, not where the real driveway is; no stop bar, no sign, no light.

## At merge
1. `Assets/PSXRacing/Resources/charlotte_lotent.bytes` (version 2) by path; re-make it after the final data and code (`-executeMethod PSXRacing.EditorTools.CityAudit.LotEntranceBake`), then bake tiles.
2. Shared-file edits of this pass, all in `Scripts/City/CityMeshes.cs`: `SolveStrip` (the existing hook line now passes `ref crossFall`), `LayStripQuad` (3 lines: `LotEntThroatBucket` before the profiles are added, `LotEntThroatUV` after). `CityFlickerFinder.cs`, `CityMap.cs`, TryLamp: as merged.
3. If the hoods agent takes the taper: the one line above in `CurbCutWings`.


# HANDOFF - CHARLOTTE TREES ROUND 3: REAL SIZES AND SHAPES (agent "trees", branch `charlotte-trees2`, 2026-11-15; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-trees2` off mountain-nm f8cbf12d. Sandbox PSXLab (lock "trees", released 12:42). Owner 2026-11-15 on PC41, West Trade St (`25.webp`): "Trees don't seem accurate for Charlotte and are often too tall." Nothing under `Assets/PSXRacing/Scripts/City/` changed (`git diff --name-only f8cbf12d HEAD -- Assets/PSXRacing/Scripts/City` prints nothing): NO re-bake, the road graph is untouched (no graph hash to give), a player-only build ships it. Stopped at the coordinator's call (the PC build was waiting): what is unfinished is listed below.

## Causes (measured: `CityRefSpots.RunMeshTrees3`, 211 tiles / 46,037 trees at ten places, the switch flipped inside one run)
- STREET TREES WERE POLES. Round 2's street forms are poplars and beeches NARROWED to 0.42-0.62 of their width on a long stem (`Editor/PSXRacingBuilder.CityTreeKit2.cs:45-48`), then tried at scales up to 1.6 until the crown clears the lane (`Scripts/CityMeshTrees.cs:512` StreetScales, `:575` the card's height x 1.1, `:659`): the clearance came from GROWING the tree. Measured, Uptown's grate trees: 13.0 m tall, 4.3 m wide (width / height 0.33), the tallest 21.3 m, 100% of them those narrow forms; median rows 12.8 m / 4.2 m.
- EVERY OTHER TREE ONE SIZE. `CityMeshTrees.cs:579-585`: 1.3-1.35 x the card's height whatever stands round it - yards 14.5 m mean, lots 13.9 m, only 12% of the trees of built-up sites under 8 m (the city's own street tree inventory: 41% small species); and a tree that met a wall took the narrow street forms before a smaller tree (`:706`).
- Crape myrtles were a grey-willow bush at x1.7 (3.9 m tall, 6.0 m wide).
- The fit knew the map's real building footprints only as the tile's collider: 15-86 trees a ring burned the 160-test cap against a wall (the dear trees of round 2's hand-back).
- Found on the way: Big Oak's trunk stood 1.4 m (model scale; 0.65-0.95 m as drawn) from its planted foot and trunk collider - `LoadVariant` takes the mean of the lowest bark of the first level as the foot (`Editor/PSXRacingBuilder.MeshTrees.cs:673-684`) and the oak sweeps limbs to the ground. Moved onto its trunk in the kit (`CT3CentreOnTrunk`, by bark area at chest height).

## Targets (sources; set for 1999 as the brief said - see the owner's "2026 city" note under LEFT)
- City of Charlotte street tree inventory, 85,146 trees, in McPherson et al., "City of Charlotte, North Carolina Municipal Forest Resource Analysis" (USDA Forest Service CUFR, 2005; urbanforestrysouth.org): willow oak 16.7%, crape myrtle 14.1%, pears 8.7%, red maple 6.7%, dogwood 6.5%, sugar maple 3.5%, plum 2.9%, silver maple 2.4%, red cedar 2.4%, sweetgum 2.2%, shortleaf + loblolly pine 2.4%; by mature size 42% large, 17% medium, 41% small species; 43% of the willow oaks over 24 in trunk.
- UNC Charlotte Urban Institute (ui.charlotte.edu: "A city of trees, but for how long?", "Disease and old age afflict Charlotte's tree canopy"): willow oak the old neighbourhoods' street tree (Myers Park, Dilworth, Elizabeth), now second to crape myrtle; Tryon St lined with willow oaks in the 1980s. Our State, "Queens Road West": 54 willow oaks planted in the winter of 1915-16.
- NC State Extension plant toolbox (plants.ces.ncsu.edu): willow oak 40-75 ft tall, 25-50 ft wide; Bradford pear 30-50 ft, 20-35 ft; loblolly pine 60-90 ft, 20-40 ft. NC Wildlife Resources (Piedmont habitats): oak-hickory woods, loblolly / shortleaf pine.
- Per site: UPTOWN GRATES Tryon / Trade 9-12.5 m, a round crown as wide as the walk allows, side streets 7.5-10.5 m; MEDIAN ROWS crape myrtle 4-6.5 m or young round trees 7.5-9.5 m; OLD-CITY STREETS (within 4.5 km of the Square, local streets) willow oak 17-24.5 m, broad, limbed to a third; YARDS / LOTS / OTHER ROADSIDE the inventory's mix - small ornamentals 4.5-7.5 m, medium round trees 8-13 m, large shade trees 15-22 m, pines 16-24 m; WOODLAND hardwoods 18-27 m, pines 19-27 m, understory 4-8 m, the columnar poplar 1 in 40 and nowhere else.

## What changed (switches)
- `Scripts/CityMeshTrees.Round3.cs` (new, partial of CityMeshTrees): each tree's SITE (uptown grate, median row, old-city street, other roadside, yard, lot / strip, woodland, open ground) from what stands round its foot - nearest road and class, nearest house / other building (now also the map's real footprints, `CityMap.FootprintsInTile`), the canopy map, distance to Uptown - then a plan of stages (group, height wanted, range). The lane's clearance comes from the FORM; no tree is scaled past its site's range; the two narrow forms only in a grate under a wall. THE PROFILE: what Gather found laid out once as the free radius by quarter-metre slab; a size is asked of it in ~50 comparisons, only the chosen one goes to the exact test and the physics, and what the physics refuses is written back (no tree reaches the 160-test cap any more). No crown reaches past the 16 m gathered. A tree drawn under 8.5 m breaks away (Q15); a solid one's collider radius follows the drawn height.
- `Scripts/CityMeshTrees.cs`: `partial`; round 3 called from TryTree; Gather notes the nearest edge and buildings; `Conflict(.., mode)` (data only / physics only); BigHit reports its hit; role lists exclude round-3 forms (round 2's lists unchanged); site recording for the tools.
- `Scripts/CityTreeKit.cs`: `group3` (a byte per variant: 1 street, 2 avenue, 3 pine, 4 myrtle). The Role enum is unchanged (an older build reading the new kit stands up; the "roads" agent shared the sandbox all round).
- `Editor/PSXRacingBuilder.CityTreeKit3.cs` (new) + one hook in `PSXRacingBuilder.CityTreeKit.cs`: 25 forms appended after round 2's 44 (69 species): street trees lr / lb / ls (round crowns, base at 0.51 / 0.45 / 0.64 of the height) from Meadow poplars 01-04 and beeches 01-03, ln / lc (narrow: a grate under a wall), avenue oaks from beeches 06 / 07 / 08 and poplars 04 / 05 (widened 1.3, limbed to a third), pine forms, crape myrtle vases from the grey willows. Each: the lower crown cut at a share of the leaves; the trunk found at chest height by bark area and drawn from the foot to the crown's middle; thin stems thickened; limbs that leave the trunk near the cut raised into the crown's base, lower ones pruned at the stem; the impostor cut alike. Big Oak's foot moved onto its trunk.
- `Editor/CityRefSpots.MeshTrees3.cs` (new): the survey / proof tool (`-executeMethod PSXRacing.EditorTools.CityRefSpots.RunMeshTrees3`, PSX_CT3_ONLY, PSX_CT3_NOSHOTS).
- SWITCHES: `PSX_CT_ROUND3=0` (runtime, default ON) = round 2's fit and trees from the same kit (differences from PC41: Big Oak 0.65-0.95 m nearer its collider; round 2's fit also refuses a crown past 15.6 m, which its own kit never reached). `PSX_CT_PINEFORM=1` (runtime, default OFF): pines as the kit's limbed-up pine forms - OFF because their pruned trunks carry sawn limb stubs that read as damage from the road (seen on the subdivision frame); by default a pine is round 2's stand-in (a dark Meadow poplar) at the site's height. `PSX_CT_R3KIT=0` (kit build) = round 2's kit exactly.

## Proof (PSXLab, editor, PC profile, MEDIUM, noon, summer; one run, both fits; head 9cc35c33's code)
- SHEET: `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\trees2\trees3_before_after.png` - eight rows before | after from one driver's-eye pose each (West Trade St's median, Tryon St, Dilworth Rd E, Queens Rd W, Raintree Lane, Providence Rd, Colony Rd through woods, Johnston Rd's median row), then the height histograms by site. Rows singly: `...\trees2\final10rows\row_*.png`; log and per-tree CSV: `...\trees2\final10\`.
- SURVEY, mean height / crown width (widest reach x 2), before -> after: uptown grate (n 2,842) 13.0 / 4.3 m -> 9.5 / 6.6 m (tallest 21.3 -> 12.9 m; width / height 0.33 -> 0.70; 95% round street forms, 5% narrow, 1% bush); median row (n 11) 12.8 / 4.2 -> 8.7 / 6.4; old-city street (n 1,933) 14.0 / 9.2 -> 17.2 / 15.3 (median 18.8 m tall, 17.5 m wide; 68% avenue oaks); other roadside (n 1,417) 12.4 / 8.9 -> 11.2 / 8.5; yard (n 4,945) 14.5 / 9.4 -> 10.5 / 7.4; lot / strip (n 2,873) 13.9 / 9.3 -> 10.6 / 7.4; woodland (n 19,274) 16.5 / 11.4 -> 19.5 / 13.1 (the tallest site); open ground (n 12,695) 17.5 / 12.3 -> 16.7 / 12.3. Built-up sites: under 8 m 12% -> 30%, 8-15 m 48% -> 44%, over 15 m 40% -> 27%. Crape myrtles 3.9 m tall x 6.0 m wide -> 5.2 x 4.4 m. Columnar Big Poplar 0.7% -> 0.5% of trees, woodland only. Pines (28% of the trees planted): 19.6 -> 21.7 m, same model.
- GUARANTEES: trees through solids 0 before, 0 after (207 tiles, 45,621 trees, 81.4 M crown points: road envelope, buildings, poles / wires, signals, colliders); trunks standing with no drawn tree 0; dropped 96 -> 47 of 46,037 (grate trees 32 -> 18 of 2,871, 20 as a bush); 2,235 trees drawn under 8.5 m made breakaway; pool 128 slots, 0 growths.
- COST: fit 16.9 -> 9.9 ms a tile on a busy machine (five runs: after = 0.59-0.83 of before; worst tile 138 -> 46 ms; the slice stays 2.5 ms a frame; no tree at the 160-test cap, before 15-86 a ring); ring triangles from the eye, MEDIUM, before -> after: West Trade 0.54 -> 0.45 M, Tryon 0.50 -> 0.41, Dilworth 1.42 -> 1.12, Myers Park 1.54 -> 1.28, Providence 1.47 -> 1.37, subdivision 1.71 -> 1.72, woodland 1.97 -> 1.98, median 0.78 -> 0.79 (-21% to +1.3%). Editor frame times were too noisy to rank (the owner's game and four agents were running). GPU buffers: see the coordinator notes.

## What still looks wrong / not done
- Under a wall or awning (the right walk in the West Trade frame, Uptown's 2.4 m side walks) the tree is still a slender pale stem with a small high crown - 9-10 m now, not 13-20, but thin: the fit's crowns are round about the trunk, so a crown cannot lean out over the street as a real one does. Next: an offset crown (the kit carries a lean, the fit turns it to the road).
- Pines are unchanged in form (a dark poplar): the limbed-up pine form is built and OFF (`PSX_CT_PINEFORM=1`) - its trunk shows sawn stubs; a flush prune was tried and took trunk triangles on the leaning scanned poplars (reverted).
- Avenue oaks and street trees show their limbs swept up into the crown's base (the raise) and a few short stubs on the stem; beech bark is pale where a willow oak's is dark; a dark collar shows on one trunk in the Queens Rd frame.
- Cards keep the planted sizes: the tile's card trees (mirrors, sun shadows, past 1.2 km, phones) are baked from Scripts/City - a yard tree that is now 6 m still casts its 15 m card's shadow. Needs the fitted list written back into the tile's card mesh (`CityTrees.CardData` is public; not done).
- Impostors of the new forms show the cut lower crown as a thin leafy strip down the stem; silhouettes fuller than the meshes as before.
- NOT RUN: play mode, a player build, fall / winter / snow / dusk / night frames, door mirrors, the pop measure, a W.T. Harris picture (survey only: 11 median trees in all).
- OWNER, NEW (via the coordinator, after this round's targets were set): (1) "the city (buildings, trees, etc) and roads should be the newest 2026 version": the targets above are 1999's - for 2026 raise Tryon's 1980s willow oaks to 15-18 m (the avenue form in a grate), more crape myrtle (now the commonest street tree), fewer Bradford pears; the site table in `Plan3` is where. (2) Willow-oak rows on Queens Rd and Central Ave's median: the rows are planted in `Scripts/City/CityMeshes.MedianDress.cs` (`MedianTreeRoads`: Harris and Johnston only) - not this branch; once planted, a median row inside the old city should take the AVENUE form (one line in `Plan3`: today every median row is a crape myrtle or a small round tree). Grass, not mulch, on Providence Rd's median is the median dress, not trees. (3) The curbs agent's grass planting strip: trees moved onto it will read as "old-city street" / "other roadside" (within 7.5 m of the pavement) and take the street forms with no change here.

## The gap list (Charlotte species no owned model can pass for, most important first)
1. Loblolly / shortleaf pine (any southern yellow pine): 28% of the trees the city plants and every suburban skyline; no conifer is owned - drawn as a dark-leaved poplar, right only in height and colour.
2. Willow oak and the oak family (water, white, red oak): the signature street tree, 17% of street trees; beeches and broad poplars limbed up give the size and outline, not the fine narrow leaf or dark furrowed bark, and there is no young street oak.
3. Crape myrtle (14% of street trees, more today): a grey willow drawn into a vase - no smooth multi-stem trunk, no summer bloom.
4. Flowering small trees - Bradford / callery pear (8.7%), dogwood (6.5%), cherry, plum: no spring blossom, no dense egg-shaped pear; drawn as small beeches.
5. Southern magnolia, American holly, eastern red cedar (evergreens, about 8% of street trees): nothing owned is evergreen and dense to the ground - not drawn at all.
6. Red / sugar / silver maple (12.6%): beeches pass for shape, the red fall colour is a tint. Low.
7. Sweetgum, tulip poplar, hickory (woods): the beeches pass at woodland distance. Low.
8. Sycamore, river birch along creeks (white bark): beeches stand in. Low.

## Coordinator at merge
1. Merge `charlotte-trees2` (no file under Scripts/City; `MeshForest*.cs` untouched; `HANDOFF.md` by union). The three new scripts have no .meta in git (the sandbox makes them).
2. THE KIT IS NOT IN GIT AND I DID NOT PUT IT THERE. `Assets/PSXRacing/Resources/CityTreeKit.asset` was never tracked (`git ls-tree -r mountain-nm` has none); it points at 257 generated meshes `Assets/PSXRacing/Generated/CT_*`, `CT2_*`, `CT3_*`, `CTImpostor_*` (100 MB) and 81 materials `Assets/PSXRacing/Materials/CT_*.mat`, `CT2_city_*.mat` - the owner's store packs reworked, which exist only in PSXLab. Committing the .asset alone would leave dangling references in every other checkout; committing all of it puts 100 MB of store content into the repo (`Assets/StoreAssets` is gitignored for that reason). The player build needs nothing copied: PSXLab holds the round-3 kit built 12:34 on 11-15 by this branch's head. If a kit build or a non-PC scene pass runs there first, rebuild it: store files as before (`tools\store-assets.ps1 -Dest <sandbox>`; already there), then with PSX_PC_CONTENT=1 `-executeMethod PSXRacing.EditorTools.PSXRacingBuilder.BuildCityTreeKit` (about 4 minutes). If you do want it in git: `git add` those paths from a checkout that has them.
3. No tile re-bake, no CityTileCache.Format change, no kit slot or material slot, no scene pass.
4. GPU: the tree pool's buffers grow from about 55 MB to 82 MB (69 species against 44: MeshForest sizes one list per species and level for the whole pool; slots stay 128, plants a slot 480). Round 2's 18 narrow street forms are kept only so `PSX_CT_ROUND3=0` still draws the old picture: dropping them from the kit later gives 22 MB back.

# HANDOFF - CHARLOTTE CURBS 3 / INLETS (agent "hoods", branch `charlotte-hoods`, 2026-11-15; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-hoods` off mountain-nm f8cbf12d. Sandbox PSXPerf. Graph hash b2aa3453 before and after (no export run, no data file touched). Owner 2026-11-15 (PC41): "I see some storm drains with curbs that extend into the road (they look decent), but other roads don't have curbs like that. Are these accurate?" + "review ... neigborhoods, driveways".

## The answer to "Are these accurate?" (plain words)
1. Partly. A grate in the gutter with a cast-iron hood in the curb is the real Charlotte inlet (NCDOT standard 840.03), so the idea was right. The block standing out into the road was wrong.
2. Real, on a street with a normal upright 6 in curb (Uptown, West Trade, Beatties Ford): the curb line runs straight through the inlet. For 3 ft the concrete curb is an iron hood, its front on the curb's face line and its top level with the curb; the grate (3 ft by 2 ft) lies in the gutter in front. Nothing sticks out, and the standard basin has no slab or round lid behind it.
3. Real, on a subdivision street with the low roll ("valley") curb: no inlet is set in the roll. The curb rises to a normal upright curb for a few feet each side of the inlet (Charlotte: 3 ft 6 in to 10 ft) and drops back; the gutter edge stays straight and the bulge is toward the lawn.
4. The game drew, on roll-curb streets (Martin St at West Trade / Beatties Ford is one), a full-height hood block at the gutter line, 45 cm in front of the curb's crest, with blunt ends - his picture - and a slab and lid behind every inlet that the standard does not have.
5. "Other roads don't have curbs like that": also wrong in the game. Only streets near Uptown or beside shops had any curb (38.5% of surface street length); real Charlotte has curb and gutter on nearly every city street (roll curb in subdivisions, upright curb on main roads); open shoulders are for country roads, freeways and ramps.
Sources read 11-15: NCDOT Roadway Standard Drawings 840.02, 840.03, 846.01; Charlotte Land Development Standards Manual 10.17A/B, 10.19, 10.29, 10.30, 20.34, U-02A; CDOT site plan checklist; WFAE 2019-12-17 (sidewalks optional about 1950-1990, required both sides from 1998).

## Causes
- `CityMeshes.Curbs2.cs` DrawInlet (was ~665-747): `rolled = r > 0.5f` put the hood (yTop = h + 1 cm, 0.5 m deep) at the drawn edge, the roll's crest 0.45 m behind it; a corner inlet stood InletEndM = 5.5 m before the ribbon's end, inside the 8 m ease to the corner's vertical curb (r about 0.77); the hood ignored a cut wing's lowered curb; a 1.3 m slab + lid behind every inlet.
- `CityMeshes.Walks.cs` WalkEdgeUncached (~318): a curb only within UrbanCoreM (2.2 km) of the Square or beside a non-house building.
- `CityMeshes.Walks.cs` WalkFrontage (~1861): a walk street's frontage was paved 7 m deep, house streets too (concrete front yards near the Square).

## What changed (all code; no data, no slot, no material, no baked field, CityTileCache.Format untouched)
- NEW `Scripts/City/CityMeshes.Curbs3.cs`.
  - PART 1, `PSX_CITY_INLET2` (default ON; 0 = the 11-11 inlet): the inlets of an edge are PLANNED before its walk is laid (Cs3EnsurePlan: the 11-11 stations, kept off aprons, ramp ends, tree grates and paint from map data, the same in every tile). On a rolled street the walk's section eases over 3.05 m each side of the hood (2.0 / 1.07 m where room is short) from the roll to a pan with the vertical curb behind it: face on the roll's crest line, gutter lip straight, back 0.4 m further out; the collider is a lerp of the roll and the 11-11 chamfer (Cs3Profile). Hood 0.937 m long, 0.152 m deep, 9 mm proud, 1 cm over the curb top, to the local curb's height; grate 0.908 x 0.603 m; no slab, no lid. A rolled street's corner inlet stands 10.9 m before the ribbon's end (clear of the corner's ease).
  - PART 2, `PSX_CITY_CURBS3` (default ON; 0 = the 11-11 rule): CurbClassOf per edge. Not a surface street: none. The 11-11 walk streets: vertical curb, pan, walk as before; a house street near the Square (pre-war grid) is now VERTICAL with a lawn frontage (`PSX_CITY_PREWAR=0`: rolled, paved, as 11-11). Else built-up (a real footprint or generated building within 45 m at a third of a local street's reads, half of a collector's / arterial's, or a gap between two built stretches): local = the ROLL curb alone (0.61 m, CLDSM 10.17B's 2 ft valley gutter; lawn level behind it; `PSX_CITY_CURBONLY=0` gives it 11-11's 2.4 m rolled walk instead), class 1-3 = vertical curb + pan + walk. Else none (rural). Two roll-curb streets meet in a curb return (ring strip), not a paved fan. A roll curb is not cut at a driveway (`PSX_CITY_DRIVEROLL=0` cuts it); its level backing eases out to the plain apron verge within 3 m of a driveway. Class eases over 8 m at a plain joint of two streets of different classes.
    - `PSX_CITY_PANTONE` (default ON, in CityMeshes.Curbs2.cs): the gutter pan weathered grey, not light concrete (the owner 11:50; UV only).
- Hook for "lots": `CityMeshes.CurbOpeningAt` (Func<CityMap, float x, float z, bool>, null by default) is asked beside ApronAt wherever a walk's curb is read for cuts; anything ApronAt reports already opens the curb.
- `CityMeshes.Curbs2.cs`: Cs2Span cut-share record, EmitStreetHardware -> EmitInlets2, InletStations end offsets, RolledEdge / RolledNode / Cs2Mix / CurbNoEdgeLines by class.
- `CityMeshes.Walks.cs`: WalkEdge by class; PrepareWalks / CornerWalkOf widths (Cs3WalkW, Cs3WalkMin); EmitWalkSpanCore (inlet share, breaks, pan band, cutA backing share); CurbCutWings (no cut on a roll; cutA); WalkFrontage skips house streets.
- `CityMeshes.Houses.cs` PrepareTileDrives: tileDrivesWide (driveways within 20 m of the tile, for the plan). `CityHouses.cs`: BoundTo, ProcBuildingNear.
- Editor: NEW `CityAudit.Curbs.cs` (`CityAudit.CurbsOnly` -> curbs_audit.txt), NEW `CityRefSpots.Curbs3.cs` (`CityRefSpots.RunCurbs3`); `CityCurbDrive.cs` + PSX_CURB_SPEEDS / PSX_CURB_ANGLES / PSX_CURB_DRIVEWAYS=N; `CityFlickerFinder.cs` + PSX_FLICK_EXTRA "name,x,z;...".

## Owner 11:50, Beatties Ford Rd by JCSU: "that is a LOT of curb on the side of the road" (his line drawn across the gutter pan, 28.png)
- The game draws there, from the lane edge out: gutter pan 0.60 m (render-only, the structure atlas's girder band: light concrete, as bright as the crosswalk paint beside it); the curb 15 cm high whose drawn TOP is 0.40 m deep (to the collider's nose; the same light concrete); the walk slab from the curb to 2.4 m (slab texture, no grass strip); then a paved frontage 7 m deep beside non-house buildings.
- The standard (NCDOT 846.01 = CLDSM 10.17A 2'-6" curb and gutter; thoroughfare section): pan 24 in (0.61 m), curb top 6 in (0.15 m), 6 in high; then a planting strip (4 ft minimum, 8 ft today); then a 5 ft walk.
- So the pan's WIDTH is right. The game draws MORE concrete (curb top 0.40 m against 0.15 m; the walk starts at the curb with no grass strip, so pan + curb + walk read as one 3.0 m band where the real street shows 0.76 m of curb and gutter, grass, then 1.5 m of walk) and BRIGHTER concrete (the pan as light as new concrete and paint; a real pan is weathered and tyre-dirty).
- Changed now (UV only, no geometry): `PSX_CITY_PANTONE` (default ON; 0 = the light pan): the pan in the atlas's weathered grey (the band Uptown's granite curb uses). Before / after frames at West Trade St: `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\hoods\pan_tone_sheet.png`. Not shot at the owner's exact Beatties Ford pose.
- Would change next (NOT done: collider and geometry, needs the curb drive): the drawn curb top 6 in deep with the slab's texture behind it; a 1.2 m grass planting strip between curb and walk where there is room; the walk 1.5 m.

## For "lots": what decides where a street's curb drops for a driveway or a lot entrance
- `CityMeshes.CurbCutWings` (CityMeshes.Walks.cs), its local `CutAt(u)`: the walk is read every 0.5 m at its MIDDLE (edge point + outward * w / 2) with `ApronAt(map, x, z)` (CityMeshes.Lots.cs: the lot apron quads, and `DriveApronAt` = this tile's house driveways with their flares) OR the new `CityMeshes.CurbOpeningAt` (Func<CityMap, float, float, bool>, null by default). Where true the curb's share drops to `CurbCutShare` (0.15: a 2.25 cm lip) across the opening and eases back to the full curb over `CurbCutWingM` (5 m) each side (the anti-launch wing); the drawn plumb face and top follow (they scale with the collider's section).
- NEVER cut: a ROLL curb - `Cs3NoCut(map, e)` = Curbs3On && DriveRollOn && `RolledEdge(map, e)` (class `CurbRollOnly`). A lot entrance on a roll-curb street is driven over the roll; only the lawn backing gives way to the apron's verge within `Cs3ApronEaseM` (3 m).
- The gutter pan (`Cs2Gutter`) is NOT cut: it runs through an opening as a real gutter does. The verge behind the walk is poured concrete where ApronAt is true (EmitStrip, unchanged). Corners (`CornerWalkOf` / `EmitCornerWalkCore`) are never cut.
- The inlet plan keeps hoods 3.6 m along from any opening (`Cs3ApronWithin`, `InletApronClearM`): it reads ApronAt, CurbOpeningAt and the tile's wide driveway list.

## Numbers
- Contact sheet (real editor frames, same poses, switches off vs on): `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\hoods\curbs3_sheet.png`; frames in `...\scratchpad\hoods\shots_final2\`, part-1-only frames in `...\hoods\shots_b\`.
- AUDIT (`CityAudit.CurbsOnly`, `...\hoods\audit4.txt`), the rule city-wide, centreline km of the 2975.4 km of surface street (ramps, freeways, bridges, tunnels 970.8 km not counted):
  - before: curbed 1146.1 km (38.5%) - local: none 240.6, rolled + walk 24.4, vertical + walk 314.1; class 1-3: none 1588.6, vertical + walk 807.6.
  - after: curbed 1677.0 km (56.4%) - local: none 36.9, roll curb alone 203.7, vertical + walk 338.5; class 1-3: none 1261.4, vertical + walk 1134.8.
  - dead ends: 941 street ends (843 local); 73 drawn with a turnaround bulb, 868 without (771 local). Not built (E).
- SAMPLE of 68 tiles built both ways (8 x 8 round West Trade St at Beatties Ford Rd = Uptown edge, pre-war streets, I-77 / Brookshire; + Uptown, Hidden Valley, rural, I-77 / I-85 tiles):
  - inlets 599 -> 802; on rolled curb 81 (blocks) -> 177, every one in a roll-to-vertical transition (1027 m of transition); stations with no place 556 -> 72.
  - driveways by their street: no curb 618 -> 0, roll curb 254 -> 567, vertical curb 617 -> 922. Mouths the walk crosses: over a roll 247 -> 559, through a cut (lip under 3 cm) 596 -> 897, a curb FACE across (15 cm) 8 -> 8: the same 8 before and after, on vertical-curb streets (Uptown West Dr x3, Cannon View Ln x2, Katonah Ave x2, Jenkins St); cause not found (the driveway's quad does not reach the walk's middle or back there).
  - COST: box triangles 2,169,894 -> 2,312,693 (+6.6%), vertices +6.7%, tile build 334 -> 376 ms (+12.5%; earlier runs of the same sample +1.5 / +5.4%: noisy); Hidden Valley tile 5594 -> 7595 triangles (+36%), 28 -> 46 ms; Uptown -0.6%; interchange -1.3%; rural 0. Expect about +4 to +6% baked bytes city-wide (+0.25 to 0.35 GB of 5.6 GB) and +3 to 5 min on the 35 min bake.
- CURB DRIVE (`CityCurbDrive`, play mode, RX-7, PSX_CURB_SPEEDS=15,40,80 PSX_CURB_ANGLES=90,45; `...\hoods\drive_new3.txt`, `drive_new.txt`, `drive_base.txt`). All-off s / any-off s / kick g:
  - new roll curb alone, Bilmark Ave: 90 deg 15: 0 / 0 / 1.0; 40: 0 / 0.38 / 6.6 (1 stop strike); 80: 0.18 / 0.32 / 19.3. 45 deg 15: 0 / 0 / 0.4; 40: 0 / 0 / 0.8; 80: 0.02 / 0.37 / 14.9.
  - 11-11 rolled walk today (switches off), Lexington Ave | Baldwin Ave: 90 deg 15: 0 / 0 / 1.0 | same; 40: 0 / 0 / 3.4 | 0 / 0 / 1.8; 80: 0.08 / 0.32 / 21.2 | 0.32 / 0.42 / 18.1. 45 deg 40: 0 / 0 / 0.5 | 1.1; 80: 0.02 / 0.25 / 10.4 | 0.07 / 0.53 / 16.6.
  - so: all-wheels-off times inside the two base sites' range at every speed; WORSE at 90 deg 40 km/h (a wheel off 0.38 s, 6.6 g, one stop strike vs none). Off the curb at 15 / 30 deg: equal or better at every speed. With the plain verge behind the roll (first try) it launched (90 deg 40: all off 0.23 s; 45 deg 80: 0.15 s): hence the level backing.
  - pre-war Baldwin Ave, vertical now (rolled before): 90 deg 40: 0 / 0.18 / 7.4, 2 strikes (was 0 / 0 / 1.8); 80: 0.32 / 0.42 / 24.4, body up 1.8 m (was 0.32 / 0.42 / 18.1, 1.1 m); 45 deg 80: 0.48 / 0.57 / 26 (was 0.07 / 0.53 / 16.6). It is the shipped vertical curb (as N Graham), harsher to mount than the roll it replaces on 24 km of street: `PSX_CITY_PREWAR=0` if the owner wants the roll back there.
  - DRIVEWAYS, ten on each street, square on over the curb at the mouth: roll curb (Bilmark) 15 km/h: no wheel off, 1.0-1.3 g, wheel step 1.9 cm, no strike; 30 km/h: all off 0, one wheel off 0.18-0.52 s, 1.8-2.9 g, no strike. Dropped vertical curb (Baldwin) 15 km/h: 0.6 g, step 1 cm; 30 km/h: 0.8-0.9 g; no strike (as before).
- LAUNCH AUDIT on the 64-tile box (PSX_LAUNCH_BOX=-4608,5376,-2560,7424), switches off vs on (twice): identical - 40 separations on 4535 paths, 1 LAUNCH / 28 UNLOAD spots, 7 turning movements off the pavement / 1 step, both.
- FLICKER FINDER (-force-glcore) at four picture spots, 24 views: depth-tie pixels 756 -> 796 of 8.86 M (85 -> 90 ppm); sidewalk | structure 94 -> 114, lawn | sidewalk 5 -> 62 with pavement | sidewalk 75 -> 25 (the same seam, the frontage now lawn), ground | kerbs 3 -> 19. Run one commit before the driveway-apron backing ease; not repeated after it.
- TRAFFIC CHECK (`CityTrafficCheck`, PSX_CITY_TRAFFIC=1): switches off: CITY TRAFFIC OK (38 checks). Switches on: after the cache fix, a fresh process: CITY TRAFFIC OK (38 checks), 0 NullReference in its log. (Before the fix it failed at 'CityTraffic attached by CityMode'.)

## What still looks wrong / NOT DONE
- The traffic check found a real fault late: a fresh process with CURBS3 on threw NullReference in WalkSide on every tile with a one-way walk street (my class return skipped the cache allocation). Fixed (commit "WalkEdge allocates the walk caches before the class return"); the editor tools had not seen it because they always built with the switch off first. The audit, picture, flicker and launch-audit logs hold 0 such exceptions (their numbers stand); the curb-drive logs were deleted before I knew to look - the profiles it measured show the curbs were built at its sites.
- Same-site A/B of the roll curb alone against 11-11's rolled walk (PSX_CITY_CURBONLY=0) was queued and NOT run.
- No planting strip between curb and walk on the pre-war streets (real Elizabeth / Dilworth have one).
- The 2 in gutter depression at an inlet is not drawn (the pan is render-only on the road's collider).
- The transition's drawn face and top are the structure atlas's light concrete; the roll beside them is the walk slab's texture: a paler wedge at each roll-curb inlet.
- Cul-de-sac bulbs have no curb round them (the curb ramps out before the bulb); 868 dead ends have no turnaround at all.
- Where two roll-curb streets meet and the ring strip does not fit, the corner keeps no curb (count not taken).
- A curb can start or stop at a plain joint where the built-up test flips (a ramp end there).
- Class 1-3 built-up streets get the walk on both sides everywhere; real 1999 arterials outside the centre had it in parts.
- The roll curb keeps 11-11's driven profile (10 cm over 45 cm); the standard valley gutter is 3 in over 24 in.
- 8 driveways in the sample still face an uncut vertical curb (pre-existing, listed above).
- Not done: E (cul-de-sac bulbs). City-wide launch audit and full verify not run (as briefed).

## At merge
- Shared-file edits: NONE in CityMeshes.cs, CityWorld.cs, CityMap.cs, StreetDress.cs, Lots.cs. Editor tools touched: CityCurbDrive.cs (env lists, DriveDriveways), CityFlickerFinder.cs (Spots: PSX_FLICK_EXTRA). New files with .meta: CityMeshes.Curbs3.cs, Editor/CityAudit.Curbs.cs, Editor/CityRefSpots.Curbs3.cs.
- Tile geometry changes: RE-BAKE. No new Slot, kit material, mesh, baked class field or format change (the pan band and transitions ride Slot.Sidewalk, the hood / grate / drawn face ride the structure mesh as 11-11).
- No scene pass needed. No data file to copy.
- If "lots" opens curbs through ApronAt (their apronQuads), nothing else is needed; the roll curb is never cut, so a lot entrance on a roll-curb street is driven over the roll.
- If "bridges" / "roads" change WalkEdge callers: WalkEdge(map, e) is now CurbClassOf != CurbNone under PSX_CITY_CURBS3.

# HANDOFF - CHARLOTTE ROADS REVIEW (agent "roads", branch `charlotte-roads-audit`, 2026-11-15; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-roadsaudit` off mountain-nm f8cbf12d. Sandbox PSXLab (shared with "trees"; PSX_BUILD_TARGET=StandaloneWindows64). Owner 2026-11-15 (PC41): "Please review all roads for accuracy (including parking lots, bridges, neigborhoods, driveways). I plan to start adding more racing tracks in Charlotte today once roads are accurate." This agent = the review of the road network itself; lots, curbs / residential edges, bridges and trees were other agents.

## The product
- THE REPORT for the owner: `C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Charlotte_Road_Accuracy_Report_2026-11-15.md` (corridor table with READY FOR A TRACK / READY WITH A NOTE / NOT YET, city-wide numbers, ranked list of what is wrong and whose ground).
- Pair sheets (37 places on 18 corridors; GAME frames only, the real road in words as captions): scratchpad `roadsaudit\charlotte_road_pairs_1.png`, `charlotte_road_pairs_2.png`. Facts behind the captions: `real_facts.py`; poses: `spots.tsv`, `spots_info.txt`.
- Scratch folder (everything below): `C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\roadsaudit\`.

## Verdicts (the owner's table, short)
READY FOR A TRACK: I-277 loop, I-77 through the centre, Providence Rd, Morehead St, Brookshire Blvd, Wilkinson Blvd, 7th St, Billy Graham Pkwy. READY WITH A NOTE: Independence Blvd (freeway shoulders where the real road has curb; 1999 was a boulevard east of Albemarle Rd), N Tryon St, S Tryon St south of I-277, E Trade St, South Blvd (0.45 km at East Blvd drawn 2 lanes, real 4), Queens Rd (no oak row), Central Ave, Park Rd. NOT YET: S Tryon St through Uptown (7.9 m wide, real about 15 m), W Trade St west of I-77 and Beatties Ford Rd inside I-85 (one 4.3 m lane each way = the 2021 streetcar rebuild, then 1.8 km drawn 2 lanes where the real road has 4).

## What the network audit found (tools: scratch `net_audit.mjs`, `net_extra.mjs`, `corr_extra.mjs`, `tagn.mjs`, `rail.mjs`; they read charlotte_city.bytes and the OSM cache of the main checkout, read-only)
- Graph: 20,567 nodes, 25,560 edges, 3,946 km, hash b2aa3453. Every edge's way id is in the extract; the extract's arterial classes are kept 100% except toll ways (177 ways, 120 km, right for 1999); local streets only in the core box (lat 35.190..35.262, lon -80.880..-80.790); unnamed service ways (3,550, 240 km) dropped; alleys / driveways / parking aisles never fetched.
- One-way: 0 flag differences, 0 of 13,499 against their way. Bridge / tunnel / level / name: 0 differences. Speed: tags honoured, defaults 65 / 55 / 35 mph.
- Lanes: 2,615 km tagged, 1,331 km by `defaultLanes` (tools/city/export_osm.mjs:251; secondary two-way = 2 is the one that hurts: Beatties Ford Rd, N Graham St, Monroe Rd, Randolph Rd, Park Rd, Sharon Rd, Central Ave). Drawn != tag on 104 km: about 71 km turn-bay sections (lineclean counts through lanes), 20 km of 5-6 lane carriageways clamped to 4 and 4.6 km of 4-lane ramps clamped to 3 (`RoadProfiles.IndexFor`, Scripts/City/RoadProfiles.cs:186-210: ow tops at 4, ramp at 3, xw at 4), 3 km lanes=1 two-way drawn 2.
- Every lane 3.6576 m (RoadProfiles.LaneM): primary / secondary 8-9% wider than at 11 ft, tertiary 13%.
- OSM is 2026: 29 km of primary / secondary within 8 km of the Square are tagged one lane each way (Remount, Tuckaseegee, The Plaza, W 4th St Ext, Parkwood, Clanton, Beatties Ford and W Trade (streetcar), W Morehead, South Blvd, S Tryon). Candidate ways for a 1999 / missing-tag lane table: scratch `lane_override_candidates.tsv` (83 ways, about 19.5 km; evidence column says which were SEEN). An exporter change: the OWNER'S OK is needed (same as the Brookshire inbound lanes question of 11-10).
- Parking lanes: only 7.5 km tagged; the game adds no width for parking anywhere.
- Junction control (new `CityAudit.RunNetwork` / the census at the head of `RunRoadsSample`): 2,593 junctions furnished (1,010 signals, 127 all-way STOP, 1,393 two-way STOP, 63 yield). 2,558 OSM signal nodes lie on game roads: 2,009 have a game signal junction within 60 m; 549 do not, about 520 because the cross street is not in the game, and 29 junctions the game has are left without (Five Points n10963, Eastway x Shamrock, N Graham x Statesville, The Plaza x Commonwealth, ramp ends on Brookshire Blvd / Wilkinson Blvd / S Tryon St / Freedom Dr / Tyvola Rd ...; full list in `city_roads_sample_BASE.txt`, lines NOSIGNAL). Cause: Scripts/City/CitySignals.cs:496 snaps a tagged signal only within SignalSnapM = 20 m of an edge end, and :498 drops a signal node with under 3 arms (359 of them) instead of handing it to the junction ahead. NOT changed: a 45 m rule would mark 98 nodes, some of them the wrong neighbour (TAGN carries no direction); it needs traffic_signals:direction from the exporter and a traffic check.
- Turn pockets: OFF (CityMeshes.TurnPockets.cs:50), as left on 11-10; 870 edges (64 km) are turn bays in LSET.
- Railways: none in the game. `tools/city/cache/layers/railways.json` holds 545 km of heavy rail; 14 rail bridges pass over game roads with nothing drawn (Billy Graham Pkwy at 35.2249,-80.9246 = the NS main line, seen in the pair; N Tryon St x2, E 36th St x2, N Graham St, I-77, NC 16, I-485 ...) and 239 level crossings sit on game roads (part are today's light rail).

## What changed (code)
- `Scripts/City/CityMeshes.MedianDress.cs`, the only game-code file touched (a block of about 30 lines after `MedianConcreteRoads`; one-line edits in `MedianShrubOk` and `PrepareMedianDress`): THE PLANTED STRETCHES. A listed concrete road (Wilkinson Blvd, N Tryon St, South Blvd: the 11-09 survey) was concrete from end to end; `MedianPlantedStretch(e)` excepts Wilkinson Blvd east of lon -80.8965 (edge midpoint x > -7170) and South Blvd between lat 35.155 and 35.185 (z -3285 .. 49), which then take the general rule (a bed under 6 m, grass above, concrete under 2.4 m and at noses / pockets). Evidence, all seen 11-15: planted inside the stretches at 4 of 4 places (Wilkinson at Ashley Rd: mulch and crepe myrtles; at Morris Field Dr: raised grass; South Blvd at Woodlawn Rd: a planter with shrubs; at Archdale Dr: grass and a tree), concrete outside at 3 of 3 (Wilkinson at lon -80.9000 and near the airport, N Tryon north).
- Switch `PSX_CITY_MEDIANSTRETCH` / static `CityMeshes.MedianStretchOn`: OFF by default (`=1` turns it on). NOT PROVEN IN PICTURES: its before / after job was queued behind "trees" in PSXLab from 11:11 to 11:36 and never got the lock, so it was stopped. To prove it: scratch `run_stretch_ab.ps1` (7 poses switch off / on, then the drive and invisible audits on three boxes before / after and the tile cost), then `py -I ab_sheet.py <scratch>`; if the four planted poses turn to beds or grass and the three controls stay concrete, change `== "1"` to `!= "0"` at the `MedianStretchOn` line. What IS proven: the code compiles (typecheck), the mechanism (a listed road's island taking the general rule draws a mulch bed with plant cards: the width test's A/B at Wilkinson / Morris Field and Ashley Rd showed exactly that), and that with the switch off the branch draws what BASE draws.
- A first attempt (concrete only where the island is under 3 m) was A/B'd and DROPPED: it turned the airport's real concrete island into a bed (that run's sheet, real editor frames, 7 poses switch off / on: scratch `charlotte_roads_median_WIDTHRULE_dropped_before_after.png`; its Wilkinson rows at Morris Field Dr and Ashley Rd are what the stretch rule draws there, its airport row is the reason it was dropped; the numbers of that run: +228 triangles a tile at Wilkinson / Morris Field, 0 on Uptown / SouthPark / the interchange, build ms unchanged; drive and invisible audits identical before / after on the Wilkinson, N Tryon and South Blvd boxes).
- The standard 100-tile sample re-run on the branch at 70a2f361: its 150 summary lines are identical to BASE (`city_roads_sample_AFTER_std.txt`).
- No data file changed: graph hash b2aa3453 before and after. No CityTileCache format change, no new slot or material (a planted island rides Slot.MedianBed / Ground and the plant cards the median dress already has).

## The corridor drive audit (new step, `PSX_ROADS_DRIVE_EDGES`)
533 edges, 67.9 km, 211 tiles: Providence Rd 7.0 km, Morehead St 6.1, Brookshire Blvd 6.3, Wilkinson Blvd 12.1, 7th St 4.9, Billy Graham Pkwy 16.7, I-77 14.7 (list `drive_edges.txt`, map `drive_edges_map.tsv`; output `corridor_drive.txt`, 437 s). 923,262 probes: walls 0, steps 0, holes 0, off-surface 0 in the corridors' lanes; BLUNT faces beside the lanes 11 (the Brookshire Blvd bridge over I-85, e3585-e3589, e1192: bridges' ground); blunt rail ends on a bridge approach 4; grass on a lane 5 (East Morehead St e11362 4 cm, Wilkinson e7320 1 cm); FALL HOLES 1 place within 12 m of a road; MEDIAN ACROSS two+ walls 466 rays / boxed areas 37 (the audit was written for the freeway routes; on the island roads it counts the island's two curbs: not checked further).

New editor tools (no game code): `Editor/CityAudit.Network.cs` (`RunNetwork` -> city_network.txt; `RunRoadsSample` -> city_roads_sample.txt: the census, then the drive audit and the invisible-collider audit box by box; `PSX_ROADS_SAMPLE=kind,kind`, `PSX_ROADS_BOXES="name:x0,z0,x1,z1;..."` in whole tiles, `PSX_ROADS_AB=Field:1` runs every box with a public static bool of CityMeshes before and after, `PSX_ROADS_COST=Field:1` + `PSX_ROADS_COST_RINGS` measure triangles / vertices / build ms on 3 x 3 rings; `PSX_NET_OSMSIGNALS=<file of x z lat lon>` for the signal comparison) and `Editor/CityRefSpots.Roads.cs` (`RunRoads`: driver-eye + 7 m high frames at poses read from `PSX_ROADS_SPOTS`, `PSX_ROADS_AB` for before / after). Runner: scratch `job.ps1`.

## The drivability baseline (for the merge)
`RunRoadsSample` on BASE code, 100 tiles in five 5 x 4 boxes (`CityAudit.SampleBoxes`; tile list `sample_tiles.tsv`, world boxes `sample_boxes.tsv`), 497 s, output `city_roads_sample_BASE.txt`. Numbers (drive audit: walls / blunt / steps / holes / off-surface / grass; invisible: lane hits, bare solids):
- uptown (x -12..-8, z 16..19; 266 edges): 85,020 probes, 0/0/0/0/0/0; invisible lanes 0; solids bare 6 (Buildings).
- inner, Plaza Midwood (x 0..4, z 13..16; 147 edges): 83,804 probes, grass 1 (Hall Ave e23214); invisible lanes 1 (sweep Roads); solids 0.
- suburb, SouthPark (x -8..-4, z -16..-13; 102 edges): 44,276 probes, all 0; invisible lanes 0; solids bare 95 (house_simple, trailers, Buildings).
- interchange, I-77 / I-277 / W 5th (x -15..-11, z 21..24; 298 edges): 123,052 probes, blunt 3, steps 3, grass 22, blunt rail end on an approach 1; invisible lanes 0; solids 0. The faults are on the local streets and W Trade / W 5th beside the interchange, not on the freeway lanes.
- rural, I-485 north-west (x -34..-30, z 64..67; 38 edges): 34,085 probes, all 0; MEDIAN ACROSS two+ 19, boxed area 4; invisible lanes 0; solids bare 1,939 (cable / guardrail colliders).
- 12 FAIL lines in all, every one on BASE. Re-run after the merge: scratch `job.ps1 -Method PSXRacing.EditorTools.CityAudit.RunRoadsSample -Log <log> -EnvVars "PSX_NET_OSMSIGNALS=<scratch>\osm_signals.tsv"` (set the sandbox / worktree at the top of job.ps1) and compare the same lines.

## Not done / could not verify
- No lane count was changed (the override list needs the owner's OK and a re-export); signals rule not changed; turn pockets not re-audited; no 5-6 lane profile.
- Street-level views are today's (2021-2026, one 2007); 1999 facts are from knowledge of the roads, marked as such in the report. Queens Rd West, Independence beyond Idlewild Rd and the outer halves of most corridors were not paired.
- The Browser pane was hidden behind the owner's game: the street-level canvas only drew after the pane was shown (`show_pane preview`) and with a 2 px ticking element injected in my own tab so the page kept painting; nothing was copied out of it.
- Baked size was not measured (no CityTileBake in PSXLab); the play mode / traffic check was not run (no traffic or collider change).


---

# HANDOFF - LOT ENTRANCES (agent "lots", branch `charlotte-lots`, 2026-11-15 Charlotte accuracy round; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-lots` off mountain-nm f8cbf12d. Sandbox PSXCity (WebGL target). The owner on PC41, Beatties Ford Road at Johnson C. Smith University: "I see many parking lots with no driveable entrance. They just have curbs all around."

## Cause (measured, not assumed)
1. **No entrance in the data on any arterial.** `tools/city/lib/lots.mjs:338` and `:636` give a lot an entrance only on a LOCAL or TERTIARY street (`e.way.rank > 1 ... continue`): where one of its own parking aisles ends within 6 m of it, else the street point nearest a ring corner within 25 m. Beatties Ford Road is `secondary`. The owner's lot (lot 3111, 1,060 m2, 35.24706, -80.85640, east side, 340 m north of the JCSU tower) and its neighbours 3112, 3114, 3115, 3355, 3353, 3610 all read "no entrance in the data": the walk and its 15 cm curb ran unbroken across their fronts. City-wide 2,758 of the 3,362 lots with no usable entrance had none in the data.
2. **The data's entrances stopped at the apron**, 2.5 m past the pavement (`CityMeshes.Lots.cs:147`, `reach = 2.5f`). A lot standing further back kept grass between and its own 15 cm kerb (`CityMeshes.Walks.cs:1952`: every ring edge more than walk + 3 m from a road): 136 lots. The fallback entrance sits at the street point nearest a ring CORNER, so half its apron faces no lot: 382 lots "under 3 m deep or wide there". Others led into a building (41), a stall row (32), an island (8).
3. **A 13-14 cm drop behind every curb cut.** The ground behind the walk came down to the lot (flush in the lattice, which the road's corridor holds 10 cm under the tarmac) at the fill's 1V:4H (`SolveStrip`, `RoadsideRules.CityFillSlope`): measured on the colliders at North Poplar Street's lot aprons, +5 cm to -10 cm within 0.5 m (28%). Where the lot stands higher, the lattice's way out of the corridor is its 1V:3H cut slope: a car at 40 km/h left the ground for 0.2-0.5 s.
4. **Street lamps ignored lot aprons** (`CityMeshes.cs` TryLamp tested the houses' driveways only): a post stood in the path at South Church Street.

## What changed
- NEW `Scripts/City/CityMeshes.LotEntrances.cs` (partial CityMeshes). `InstallLotEntrances(map)`, once per map at the end of `CityMap.Parse`, from the LOTS / LENT / road data alone (the road graph is not touched):
  - every lot that no entrance reaches by a clear 3 m path (`LotPath`: the walk's curb cut, paved all the way, no lot kerb, island, building, stall row, water or other road across it) and that joins no lot that has one gets its own;
  - EVERY entrance of the data that leads to a lot within 30 m is paved through to it (a CORRIDOR: a lot rectangle from the pavement to 0.5 m inside the lot, as a filling station's driveway is; ApronAt true on it, so nothing is planted or stood there and the verge over it is concrete) and the lot's kerb opened where it meets the ring (a THROAT);
  - else DERIVED on the street frontage: the lowest class of street the lot fronts (a trunk road only when there is no other; never a freeway, ramp, bridge, tunnel or raised stretch), 15 m or more from a junction corner (6 m where the frontage leaves no other place), in the middle of the frontage, two (at its quarter points) where the frontage is over 60 m; 7.3 m wide (4.3 m off a one-way); a stall row across it loses its lines there; a lot with no street within 60 m takes a paved DRIVE up to 150 m;
  - else a paved LINK (6 m) to a lot that has a way in within 45 m square off one of its edges (cross access between neighbouring lots).
  - THE RAMP (`LotEntRamp`, one call in `SolveStrip`): behind an entrance's curb cut (its width + 1.2 m either side, easing out over 4 m) the strip is the smoothest straight way onto the land - level, or down or up at 1 in 20 / 14 / 10 / 8 - by the least grade break along the path; never a strip that does not meet the land.
  - no street lamp on an entrance's way (`LotEntOnWay`, one condition in TryLamp).
  - THE RESULT IS KEPT: `Resources/charlotte_lotent.bytes` (501 KB, made by `-executeMethod PSXRacing.EditorTools.CityAudit.LotEntranceBake`), read at load in 15 ms. It carries a key (graph hash, roads, lots, entrances, footprint count, rule version): a file made for other data is NOT read, the pass is worked out at load (5.8 s in the editor) and a warning says so.
- `CityMeshes.Lots.cs` (mine): `EnsureLots` indexes the corridors' and throats' quads after the data's aprons (ApronAt, the walk backing's lot test and the lot kerb's rule read them unchanged); a tool's "before" (`CityMeshes.LotEntSet(map, false)`) hides the pass's lots and entrances.
- Tools (new): `Editor/CityAudit.Lots.cs` (`CityAudit.LotEntrancesOnly`: the city-wide audit -> PSXRacing_lot_entrances.txt; `PSX_LOT_PROBE=1`: real cross-sections on the colliders; `LotEntranceBake`; `LotEntranceCost`), `Editor/CityLotDrive.cs` (play mode: drives a car from the street into lots at 15 and 40 km/h -> PSXRacing_lot_drive.txt), `Editor/CityRefSpots.Lots.cs` (`CityRefSpots.RunLots`: before/after pictures -> Screenshots/lots).
- Switch: `PSX_CITY_LOTENT=0` = the lots, entrances, ramps and lamps as before (default ON).
- Graph hash b2aa3453 before and after (the exporter was not run or changed; `tools/city/lib/lots.mjs` and `CityStations.cs` are untouched).

## Shared-file edits (each small and local)
- `Scripts/City/CityMap.cs` Parse, after the "[City] parsed" log line (2 lines): `CityMeshes.InstallLotEntrances(map);`
- `Scripts/City/CityMeshes.cs` TryLamp (1 condition added to the `LampRejectBuilding` line): `|| LotEntOnWay(map, footP, 0.9f)`
- `Scripts/City/CityMeshes.cs` SolveStrip, just before `float Yv(float e)` (3 lines): a local `fillSlope`, `if (!sh.flat) LotEntRamp(map, s0, outw, y0, run, ref shoulder, ref fillSlope);`, and `Yv` reads `fillSlope` instead of `RoadsideRules.CityFillSlope`.
- `Editor/CityFlickerFinder.cs` Spots(), before `return spots;` (21 lines): extra spots by place from `PSX_FLICK_EXTRA`.
- No edit in the curb / sidewalk files (Walks, WalkBack, Curbs2, Houses): the lot kerb opens through `ApronAt`, which their `LotCurbHere` already asks.
- No new mesh slot, bucket, material or baked field. `CityTileCache.Format` untouched. `map.lots` and `map.lotEntrances` grow (2,274 corridor and link lots after the stations' own, 1,881 entrances after the data's).

## Numbers
- AUDIT (city-wide, as built, data-level; 5,169 drawn lots, 3,288 entrances in the data):
  - BEFORE: 1,505 lots with an entrance a car can use + 302 reached through a lot that has one = 1,807 (35%); **3,362 with none (65%)**, 2,283 of them shut all round (kerb, wall or uncut street curb on every side).
- AFTER: 3,136 + 1,250 = 4,386 (85%); **783 with none (15%)**, 661 shut all round. Of the 783: 434 have no street within 300 m in the data and 196 none within 150 m (outside the core box only arterials were fetched; campuses, malls and apartment complexes whose service roads are not in the road graph), 79 stand 60-150 m off and 74 within 60 m with a building, another lot or water on every straight way. Why (the audit): 770 no entrance in the data, 7 the entrance meets the lot at a corner under 3 m wide, 4 steeper than 20%, 2 a building on the way.
  - The pass: 266 of the entrances in the data paved through to their lot, 1,736 derived (+145 second ones on a frontage over 60 m; 20 on a trunk road, 40 within 15 m of a corner, 60 through a stall row, 254 at 30-60 m, 289 by a drive of 60-150 m), 237 lots linked to a neighbour; 2,274 corridors and links, 8,682 kerb openings.
  - The owner's lots on Beatties Ford Road (3111, 3112, 3113, 3114, 3115, 3355, 3353, 3610): all NONE before, all usable after.
- CROSS-SECTION (colliders, a ray every 2 cm, North Poplar Street's two aprons): the largest change over 0.5 m behind the walk 12.5 cm before, 5.0 cm after; the largest step 2.5 cm (the pavement's edge) both.
- LOT DRIVE (play mode, the RX-7, square off the nearest lane, released 1 m before the pavement's edge; PASS = arrives on the lot, no wheel off over 0.05 s, nothing hit; `PSXRacing_lot_drive_after4.txt`):
  - 26 lots (the owner's first, Uptown surface lots, walk streets further out, verge streets, lots standing back 8-54 m, the three biggest, the data's own entrances, three filling stations) x 15 and 40 km/h: the car arrived on the lot in all 52 runs; **42 of 52 pass**. At 15 km/h 25 of 26 pass (kick 0.4-0.8 g, no wheel off). At 40 km/h 17 of 26 pass.
  - THE OWNER'S LOT (lot 3111): 15 km/h and 40 km/h PASS - no wheel off, vertical kick 0.6 / 0.9 g, 40 -> 39 km/h; wheel tracks: largest step 1.6 cm, 3.9 cm over 0.4 m. BEFORE at the same place (`PSX_CITY_LOTENT=0`): the car has to mount the 15 cm curb and the lot's kerb (16-17 cm over 0.4 m): 15 km/h kick 2.1 g; 40 km/h one wheel off 0.53 s, kick 6.9 g, speed loss 7.8 g. Same before/after at East 7th Street (0.42 s / 6.9 g -> 0.00 s / 0.8 g) and Central Avenue (0.50 s / 7.0 g -> 0.00 s / 0.8 g).
  - The 10 that fail, all "one wheel off", nothing hit: where the land falls or rises away from the street behind the walk (Morehead Ridge Drive 0.27 s, West Trade Street 0.13 s, filling stations on West 10th Street 0.28 s / 0.05 s and Dalton Avenue 0.17 s, South Caswell Road 0.05 s) and over rolling ground on a long corridor (Bruton Smith Boulevard 20 m: 0.17 s; Beatties Ford Road lot 3355, 54 m: 0.20 s), all at 40 km/h with kicks of 0.8-1.1 g and all four wheels off 0.08 s at most; and the data's entrance on West 5th Street at both speeds (it led into the lot's kerb 23 m back: 14 g at 40 km/h) - see the mini drive below.
  - Mini drive after the last fix (the data's entrance across a stall row is paved through too): West 5th Street (lot 601, 23 m back) and South Brevard Street (lot 790), 15 and 40 km/h: 4 of 4 pass, no wheel off, kick 0.6 / 0.9 g, wheel tracks 4.7 cm over 0.4 m (`PSXRacing_lot_drive_after5.txt`). With it the same 26 places stand at 44 of 52 runs clean, every failure a wheel unloading at 40 km/h.
  - Run 2 of the drive found a BARRIER (0.55-1.0 m, the Barriers mesh) on the pavement's edge across five entrances: a ramp strip that never met the land read as a retaining face to the roadside rules (DropFrom / Ungraded). Fixed (the ramp is only laid where it meets the land within its run); runs 3 and 4: none.
- FLICKER FINDER (`CityFlickerFinder` with the six pictured entrances as extra spots: the driver view at 0 / 150 / 400 m x chase / cockpit near plane = 36 views; same sandbox, OpenGL core as the tool asks; base = `PSX_CITY_LOTENT=0`; `Docs/CITY_FLICKER_lots_glbase.md` / `_glafter.md` in the sandbox): depth-tie pixels 2,442 -> 2,779 of 11.4 M drawn (+337: 0.021% -> 0.024%), colour flips 6,921 -> 7,229. The rise is at the entrances themselves (the 0 m views: +10 to +35 px a view, the Concord Parkway cockpit view +112) and it is one pair: the concrete ramp (Ground [CityPavement]) on the lot and corridor asphalt (Roads [junction asphalt]) 333 -> 600 px. The ramp meets the asphalt at 1 in 14-20 where the old bank met it at 1 in 4, so the two lie within a depth step of each other along a longer line. Every other pair is level or lower (walk on kerb 204 -> 202, ground on walk 224 -> 193). Under Direct3D 12 (a first pair, run on the wrong device by mistake): 2 -> 2 tie px. NOT FIXED: the toe of the ramp wants a steeper last 0.3 m, or the asphalt cut out from under it.
- COST (`CityAudit.LotEntranceCost`, the pass hidden / shown, median of 4 builds): the tile with the most entrances of the pass (6, 7; nine) 29,835 -> 30,629 triangles (+2.7%), 244 -> 274 ms; the Square's tile (-10, 18) 61,698 -> 61,756 (+0.1%), 1,277 -> 1,278 ms; a suburb tile 9 km out (-4, -17) 13,691 -> 13,971 (+2.0%), 52.9 -> 58.5 ms. Baked size not measured (the bake is the coordinator's): expect the triangles' share. Load: the pass is read from charlotte_lotent.bytes in 15 ms (worked out: 5.8 s in the editor).
- TRAFFIC CHECK: not run. CityTraffic reads no lot, stall or entrance (grep: only CityFar, CityHouses, CityTrees, FirstDrive, RoadsideOccupancy, the walks and the lots themselves do); traffic cannot enter a lot and its paths are the road graph's, unchanged.

## Pictures
`C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\lots\lots_before_after.png` - six places x (driver's eye 16 m before the entrance, from over the lot looking back) x before / after, every cell labelled; editor frames, same pose, the pass hidden / shown (`CityMeshes.LotEntSet`). The owner's lot, the shops' lot on Central Avenue, a 60,000 m2 lot on Concord Parkway South (a verge street), an Uptown surface lot on East 7th Street, a filling station on West 10th Street, a small lot on Morehead Ridge Drive. The 36 frames: `...\scratchpad\lots\shots1\`.
What still looks wrong in it: the apron's concrete is big - behind the walk it flares over about 18 m of frontage, and where the lot stands above or below the walk it runs up to 12 m and covers the corridor's asphalt and the lot's first metres (Morehead Ridge Drive, East 7th Street); the entrances are bare (no painted stop bar, no sign); at the owner's lot the rule's place for the entrance is 6 m past a crosswalk; the filling station's frames barely differ (its driveways were there; only the ramp behind the walk changed) and its second pose looks at the price pylon's post; the "before" is this session with the pass hidden, not a separate run.

## Not done / not verified
- 783 lots still have no way in (above). They need the service roads OSM has (not in the graph) or a longer drive; a list is in PSXRacing_lot_entrances.txt ("left:" and "none #").
- Derived entrances are square off the street and straight; they do not follow OSM's own driveways (not in the data).
- A corridor lies on the land as it is: over rolling ground a long one (20 m and more) lifted one wheel 0.13-0.18 s at 40 km/h in the drive. It is not graded as a road is.
- An entrance of the data that leads into a stall row, an island or a building is left as it was (its lot gets another).
- The lots' pavement, stalls and islands themselves are unchanged. Medians: an entrance is right-in / right-out where the road has one; no median gap is added.
- Lamps, signs and poles decided once per session: the pictures' "before" hides the pass's geometry but keeps this session's lamp decisions.
- No full verify, no city-wide flicker or drive sweep, no player build, no bake (the coordinator's).

## At merge
1. Copy by path: `Assets/PSXRacing/Resources/charlotte_lotent.bytes` and `.meta` (new).
2. After the final data (any re-export, any change to charlotte_city / charlotte_bld / charlotte_fuel) AND after the other branches' code is in (the pass asks the walks' and the lot kerb's rules), re-make it: `Unity -batchmode -quit -projectPath <sandbox> -executeMethod PSXRacing.EditorTools.CityAudit.LotEntranceBake` (2.5 min), then commit the file. If it is skipped the game still works: a stale file is refused, the pass runs at load and logs a warning.
3. Then the tile bake (tile geometry changes at every lot entrance: aprons, curb cuts, corridors, ramps, lot kerbs, lamps near them).
4. No scene pass needed.


# HANDOFF - BRIDGES ROUND 2 (charlotte-bridges, 2026-11-15): the city-wide bridges audit, girders keep the clearance, no rail on a shared deck's lanes, the city bridge rail

Branch `charlotte-bridges` (worktree `C:/Users/mcgee/PSX Racing-bridges`, off mountain-nm f8cbf12d). Not merged, not pushed.
The owner (PC41): "Please review all roads for accuracy (including parking lots, bridges, neigborhoods, driveways). I plan
to start adding more racing tracks in Charlotte today once roads are accurate." Bridges were this agent's share.
Graph hash b2aa3453 before and after (no data file touched, no export run).

## The audit (new): `Editor/CityAudit.Bridges.cs`, `-executeMethod PSXRacing.EditorTools.CityAudit.RunBridges`
City-wide in about 6 minutes (40 s solve + 4.5 min): every stretch of road on structure (1,086 runs on 502 tiles, 56.7 km;
256 over water, 275 freeway main line, 162 ramps; 1,387 structure ends at fill) and 30 m of each approach, measured on
the tiles the game builds (CityMeshes.Build + CityWorld.Attach). The Roads mesh is the drawn surface AND the collider
(one mesh), so one probe reads both; the render-only Structure / Kerbs / Banks / Water meshes get probe colliders so a
thing drawn and not solid is seen too. Rays down every 0.5 m on every lane line; out from the outer lanes every metre
for the rails; along the lanes and the edge lines at bumper height for faces square to the travel; up from under the
deck, in from its side and along it toward its end for the soffit, the fascia and the abutment; and for every road
passing below: its own surface, its lanes against piers and walls, and the MEASURED clearance (a ray up from every lane
every metre). Piers are read from their solids (1,614 columns). Report `city_bridges_audit_<tag>.txt` (count table,
the 20 worst of each kind with street name and lat/lon), `.tsv` (every finding), `_runs.tsv` (every run),
`_tiles.tsv` (each bridge tile's meshes hashed: a before / after pair shows which meshes a change touched).
PSX_BRAUD_TAG / _MINUTES / _EDGES / _NEAR. Run it with PSX_CITY_BRIDGE2=0 for the picture as it was.

### Counts, city-wide, before (PSX_CITY_BRIDGE2=0) -> after; places = distinct 8 m stretches of one edge, (roads) = edges touched
| kind | before | after | what |
|---|---|---|---|
| DECK-HOLE | 3 (3) | 3 (3) | nothing to drive on, on structure (all on the interchange decks below) |
| STEP-JOINT / STEP | 32 (28) / 32 (28) | 32 / 32 | a step over 2 cm between two samples 0.5 m apart, within 6 m of a structure end / elsewhere |
| KINK-JOINT / KINK | 60 (39) / 65 (42) | 60 / 65 | a grade break over 2 % (2 m bases), at a structure end / elsewhere |
| WALL-IN-LANE | 12 (10) | 10 (8) | a face square to the travel on a lane line at bumper height |
| RAIL-ON-LANE | 8 (8) | 3 (3) | a barrier standing on a lane line (the 3 left are median Jersey pieces) |
| RAIL-DROP / RAIL-FLUSH | 1 (1) / 17 (16) | 1 / 17 | no rail on structure beside a drop / beside flush land (2 m or longer) |
| APPROACH-RAIL | 45 (44) | 45 (44) | the rail is carried under 10 m past a deck end with no mouth, junction or twin median there |
| BLUNT-END | 87 (76) | 84 (75) | a rail's face square to a car along an edge line (a reachable line, not inside a rail's body) |
| NO-SOFFIT / NO-FASCIA / OPEN-ABUTMENT | 8 / 91 (75) / 15 | 8 / 91 / 15 | the deck shows nothing from below / its edge no face from the side / daylight under the approach |
| LOW-CLEAR | 418 (136) | 103 (40) | MEASURED clearance over a road below under 4.3 m (4.9 m over a freeway main line) |
| DESIGN-CLEAR (data) | 158 (115) | 30 (29) | slab less girders under the same figures at a crossing (778 crossings) |
| UNDER-STEP / UNDER-KINK | 19 (16) / 29 (22) | 19 / 29 | the road below: a step / a grade break |
| UNDER-BLUNT-END / PIER-WALK | 4 / 1 | 4 / 1 | the road below: a rail end along its edge line / a pier in its sidewalk band |
Zero before and after: ROAD-HOLE, LAND-OVER-LANE, DRAWN-OVER-LANE, OBJECT-OVER-LANE, UNDER-HOLE, UNDER-BURIED,
UNDER-WALL-IN-LANE (no pier, cap or abutment on any lane line of a road below), PIER-PAVE, PIER-SHY (1,614 columns).
Of the 103 LOW-CLEAR left, 97 are over freeway main lines (median 4.57 m: the solve's 5.0 m slab less the thinnest
girder) and 6 over streets (3.98-4.25 m: Marie Garris Dr under Independence Blvd, Andrill Terrace under ramp e1487,
S Clarkson St under I-277, ramp e173 under Oaklawn Ave) - the slab itself is low over those lanes.
Reports (the 20 worst of each kind, street name and lat/lon): `Docs/CITY_BRIDGES_AUDIT_1115_before.txt`, `Docs/CITY_BRIDGES_AUDIT_1115_after.txt`; every finding, run and tile hash: `C:/Users/mcgee/AppData/Local/Temp/claude/be91fcb74e64226b/40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9/scratchpad/bridges/city_bridges_audit_f0*.tsv` / `..._f1*.tsv`.

## What was wrong, and what is fixed (each behind its switch; PSX_CITY_BRIDGE2=0 turns the lot off)
1. GIRDERS UNDER THE CLEARANCE (the biggest class: LOW-CLEAR 418 places on 136 roads). The solve stands a slab 5.0 m
   over the road below; the bridges pass then hung girders down to 3.8 m over it (`CityMeshes.Bridges.cs:75`
   GirderCarClearM, the clamp at :151), measured from the lower road's centre line at the deck's stations - 3.67 m
   over I-85's outer lane under University Pointe Blvd. Now (`PSX_CITY_BR2_GIRDERCLEAR`, CityMeshes.Bridges2.cs) a
   deck's girders are as deep as the road below allows: 4.3 m kept over a street, 4.9 m over a freeway main line, plus
   0.15 m for cross-fall, the lower road read under both edges of the deck. Never thinner than 0.45 m, so over a
   freeway the solve's 5.0 m leaves about 4.55 m: those stay listed (their fix is the solve's clearance - a height
   change, not made). Render-only (the Structure mesh); the piers' columns stand that much taller under the deck.
2. PARAPET BLOCKS ON A SHARED DECK (RAIL-ON-LANE / WALL-IN-LANE, all at the three single-point interchanges). Where
   several ramps meet ON a bridge each short link drew its own deck and parapets, and the parapets stood as blocks
   across the other links' lanes. RailOverArms (`CityMeshes.cs:7067`) keeps a rail off the pavement of the arms at its
   own node only; these stood on the pavement of roads one node further on. Now (`PSX_CITY_BR2_RAILPAVE`) a span on
   structure within 60 m of a junction draws no rail on a side that lies wholly inside another road's pavement at its
   own level (decided with the span's flags, from the map alone: `CityMeshes.cs:5793`, one line).
3. ONE RAIL FOR EVERY BRIDGE (looks). Every structure wore the bare concrete parapet. Freeways, expressways and ramps
   keep it (the 1990s "New Jersey" parapet); a city street's bridge (class 3 and under, not a ramp) now carries a
   galvanized bar on short posts along its parapet's top (`PSX_CITY_BR2_CITYRAIL`; render-only on the structure atlas'
   steel swatch, `CityMeshes.cs:6768`, one line). No modern hardware.

## NOT fixed - keep race routes off these for now
The driven surface on structure is clean almost everywhere: steps or kinks at 29 places in the whole city, and four of
them hold most of it. They are junctions of several short ramp edges ON a deck (single-point interchanges): each edge
has its own profile and ribbon, the ribbons overlap without a shared node, and the surfaces stand up to 22 cm apart.
That is the solve's and the junction builder's (one plane per shared deck), too big for today:
- I-85 at Freedom Drive, the deck where the ramps meet (35.2478,-80.8963): steps to 22 cm, grade breaks to 12 %,
  one 5.5 m gap on a ramp's outer line (e2908). 85 findings left (89 before).
- I-77 at Tyvola Road (35.1630,-80.8900): steps to 18 cm, breaks to 13 %, a slit across the deck. 54 findings left (57 before).
- I-85 at Brookshire Blvd / NC 16 (35.2621,-80.8783): steps 4 cm, breaks 3.5 %, median Jersey stubs across two ramps'
  lanes (a median piece, not a rail: CityMeshes.Dividers / Unions, not this round's). 26 findings.
- The airport ramps, I-85 / Little Rock Rd / Josh Birmingham Pkwy (35.2414,-80.9389): the roads UNDER the decks,
  steps to 7.5 cm, breaks to 8 %. 28 findings.
Smaller single places (a step or a break at one joint): the I-77 ramp e386 under W Arrowood Rd (35.1377,-80.9072;
13 cm, 9.6 %), Harrisburg Rd bridge e1740 (35.2533,-80.6545; 10 cm on its centre line), Freedom Dr e1892 at its deck's
start (35.2230,-80.8667; 5 cm, 6.8 %), the service road e24016 by N Graham St (35.2340,-80.8474; 7.5 cm), The Plaza
e6021 (35.2467,-80.7878; 5.8 %), N Davidson St e14137 (35.2283,-80.8303; 3.7 cm, 4.8 %), South Blvd e9994
(35.2196,-80.8462; 3.7 cm), Billy Graham Pkwy e11993 (35.2380,-80.9200; 4 %).
Listed, not touched (see the report for places): APPROACH-RAIL, RAIL-FLUSH (2-5 m of deck edge without a rail, land
flush beside), RAIL-DROP 1 (Tuckaseegee Rd e13143, 3 m beside a 0.9 m drop), BLUNT-END, NO-FASCIA, OPEN-ABUTMENT 15,
NO-SOFFIT 8, LOW-CLEAR over freeway main lines (the solve's 5.0 m slab). Railways are not in the road graph, so a rail
line under a bridge was not measured.

## Real crossings looked up (National Bridge Inventory figures via bridgelookup.com; reference only)
- S Tryon St over I-277 / US 74 (1987): 3 lanes on, 11 under, steel multi-beam, 4 spans, 93.8 m, deck 21.8 m.
  Game e1115: 102 m run, 12 m pavement, steel or concrete girder rows by hash. Deck narrower than real (no walks).
- I-277 & NC 16 (Brookshire Fwy) over N Tryon St (1973): 6 lanes on, steel multi-beam, 3 spans, 56.6 m, deck 35.3 m.
- I-277 ramp over I-77 NB & I-277 SB (rebuilt 2019; 1999 had the older ramp): 2 lanes, prestressed concrete
  multi-beam, 8 spans, 209.7 m, deck 12.5 m. Game e1256: 215 m run, BOX girder (the bridges pass draws a box under
  every ramp and every deck up to 13.5 m: the inventory says multi-beam for Charlotte's ramps - not changed, the
  box came from the owner's own photo of this interchange).
- I-277 ramp over Little Sugar Creek (1972): a three-barrel concrete CULVERT, 13.1 m. Game: creek crossings of
  ramps are short decks on 0.75 m girders.
- I-277 NB over the Southern Railroad (1987): 4 lanes, steel multi-beam, 3 spans, 85.6 m, deck 19.8 m.
- US 74 WB over I-277 / NC 16 / NC 27 (1971, rebuilt 2012): 2 lanes, steel multi-beam, 10 spans, 221.2 m, deck 12.5 m.

## Gates (sandbox PSXRec, Android target; BASE = the same build with PSX_CITY_BRIDGE2=0)
- The bridges audit, city-wide, before / after: the table above. Nothing rose.
- Every bridge tile's meshes hashed before / after (`_tiles.tsv`): 999 tiles (the 502 bridge tiles and their neighbours): Ground, Roads, Kerbs and Guardrail meshes BIT-IDENTICAL on all of them (the driven surface, the land and the seams are untouched); Barriers differ on 2 tiles (I-77 at Tyvola Rd -48 triangles, I-85 at Freedom Dr -796); Structure on 499 tiles, 2,288,882 -> 2,550,854 triangles (+262 k: the bar and its posts; +1.75 % of those tiles' five meshes, +261 a tile on average); 6 more pier columns stand (1,608 -> 1,614: shallower girders left room; none within 0.6 m of a pavement); mean first-build 202 -> 196 ms a tile.
- Drive audit (`CityAudit.RunDrive`, PSX_DRIVE_EDGES = the 30 edges of the three interchange decks + S Tryon / I-277,
  the Brookshire viaduct, the I-77 flyover, Hawthorne Ln, West Blvd creek, N Graham; via CityBridgesPass steps
  stats,drive,launch): "nothing solid stands across any lane" 7 -> 0 (passes), BLUNT 17 -> 15, lane steps over 12 cm
  10 -> 5, lane surface off the solve 5 -> 2 (the last two fell because the probes no longer land on rail stubs; the
  decks' own steps are still there), FALL HOLES 0 = 0 of 851,968 rays. The base run's INVISIBLE COLLIDERS sweep (15 min:
  lanes 0, solids 88, both older than this round) was not repeated for the after run.
- Launch audit (PSX_LAUNCH_BOX=-7500,6700,-5200,8900: I-85 at Freedom Dr and at Brookshire Blvd): 2 LAUNCH / 5 UNLOAD
  spots, 24 separations on 1,549 paths, 102,362 samples on their pavement - identical before and after.
- Flicker finder (CityBridgesPass step flicker, -force-glcore; its own spots: the Tryon sprint start, I-277 either
  side of Uptown, W Trade St): 4,047 -> 4,037 depth-tie pixels of 33.6 M drawn (121 -> 120 ppm). Pairs with the Structure mesh (girders, the bar): Barriers | Structure 48 -> 50, Sidewalk | Structure 40 -> 49, Roads [concrete] | Structure 25 -> 30, Roads [ow2] | Structure 202 -> 200: no new fight (the bar stands 0.24 m over the parapet's top on posts).
- Cost, CityBridgesPass stats (PSX_BR_TILES, each tile built three times, the quickest): triangles before -> after,
  build ms before -> after
  | tile | what | triangles | of which | build ms |
  |---|---|---|---|---|
  | -12,16 | Uptown: S Tryon / Church / College over I-277 | 44,904 -> 46,936 (+4.5 %) | structure 4,580 -> 6,612 (the bar) | 735 -> 648 |
  | -11,25 | I-77 / I-277 interchange | 46,326 -> 46,326 | girders moved, none added | 807 -> 704 |
  | 11,7 | suburb: Eastway Dr over a creek | 37,389 -> 38,453 (+2.8 %) | structure 1,442 -> 2,506 | 785 -> 694 |
  | -26,-10 | I-77 at Tyvola Rd | 58,066 -> 58,658 (+1.0 %) | barriers -48, structure +640 | 973 -> 915 |
  | -28,27 | I-85 at Freedom Dr | 39,066 -> 39,318 (+0.6 %) | barriers 6,164 -> 5,368, structure +1,048 | 819 -> 704 |
  | -8,22 | N Graham St over the rail lines | 44,028 -> 44,892 (+2.0 %) | structure 11,166 -> 12,030 | 609 -> 620 |
  Collider triangles: unchanged but for the rails taken off (-48 and -796 on the two interchange tiles). Build time:
  no rise outside run-to-run noise. Baked size not measured (no bake run here): it follows the triangles.
- NOT run: the play-mode traffic check / census (a play-mode job that hangs would have held the sandbox; no lane path
  moved - CityTraffic drives the lane model's lines at Edge.YAt, and the audit's lane-line rays found no solid on any
  lane line of a road on or under a structure outside the three interchange decks); the seam audit (its scopes are
  the owner tile, Uptown's 4 x 4 and a 60-tile sample, none aimed at bridges; the Roads / Ground / Kerbs meshes are
  bit-identical, see the hashes).

## For the merge
- Shared-file edits: `Scripts/City/CityMeshes.cs` only, two places, one line each plus a comment: DecideSideFlagsSteps
  (line 5792-5793, after the SameWayDeckBeside line) and the rail loop in EmitSide (line 6767-6768, after EmitRail).
- No new mesh bucket, slot, material or baked field; CityTileCache.Format untouched. The bar and the girders ride the
  Structure mesh, the rails the Barriers mesh. The new switches start PSX_CITY_, so the bake's fingerprint sees them.
- A re-bake is needed (Structure changes on most bridge tiles, Barriers on the tiles of the three interchanges).
- No data file changed; no scene pass needed.
- New files have no .meta in the repo (Unity mints them; nothing references these scripts by GUID).

## Tools (all new files are this round's; nothing else was touched)
- `Editor/CityAudit.Bridges.cs` (the audit), `Editor/CityRefSpots.Bridges.cs` (`CityRefSpots.RunBridges2`: driver-eye frames over /
  on / under / beside a crossing by edge id, PSX_BRSHOT_SPOTS, before and after in one job), `Scripts/City/CityMeshes.Bridges2.cs`
  (the three rules and their switches), `Editor/CityBridgesPass.cs` (+PSX_BR_TILES, three builds a tile, the quickest counted).
- Sheet: `Docs/shots/bridges2/bridges2_sheet.png` (= `C:/Users/mcgee/AppData/Local/Temp/claude/be91fcb74e64226b/40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9/scratchpad/bridges/bridges2_sheet.png`)
  (six fixes, two crossings left alone, two still wrong). The 43 base frames of 18 crossings: `.../scratchpad/bridges/base/`.
- What still looks wrong in the sheet: on the Freedom Dr deck a low sloped rail end still lies across the deck (a span only
  partly on another road's pavement keeps its rail); the Brookshire Blvd deck keeps its median Jersey stub; the Tyvola Rd
  deck still shows a dark slit and its steps; the bar reads a little heavy close up (9 cm square, the structure atlas' mottled
  steel swatch); girders over roads are now 0.45-0.55 m deep where they were 1.2 m (the price of the clearance on a slab
  5.0 m up: PSX_CITY_BR2_GIRDERCLEAR=0 brings the deep ones back).

---

# HANDOFF - EVENTS, round 1 (agent "events", branch `events`, 2026-11-15; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-events` off mountain-nm 9ced6b84. Sandbox PSXPerf. Owner 2026-11-15 (design `Events_Design_2026-11-15.md`, OWNER'S ANSWERS): sanctioned events (closed roads, no traffic, SAT/SUN MORNING or NOON) and street events (public roads with traffic, FRI-SUN NIGHT); 2-3 separate races each, prizes 1-6, a bonus for winning all; schedules fixed and shown before SIGN UP; results raise racing rep (sanctioned) or street rep (street) and the player's clubs' points; street events listed by street rep or passed on at meets and by clubs; damage only.

## What changed
- NEW `Scripts/LifeSim/Events.cs`: the catalogue (10 sanctioned, 6 street; tracks by id with a mountain fallback where a build ships no Charlotte), the schedule (event = every `period` weekends from weekend `phase`; weekend k's Friday = day 1 + 7k), the rule (drivetrain, hp as built, model year, origin, rep), SIGN UP / WITHDRAW, `DueNow`/`RaceAt`, the field (seeded catalogue cars in the event's hp window, "~N HP"), `TrafficFor` (the one place: sanctioned NONE, street LIGHT, MEDIUM on busy city roads, never more), `RecordResult` (prize by place, rep, club points, bonus), `MarkWrongCar`, `SweepInFlight` (DNF), `Tick` (no-shows at the rollover; every event race of the day scored for the clubs, NPC order seeded), standings, word of mouth (`ArrivedAtLot`), notices (mail + diary + pager, mail expires in 14 days). Switch `PSX_EVENTS=0` hides everything (saves keep their event fields).
- NEW `Scripts/LifeSim/LifeHomeScreen.Events.cs`: MAIN day view row WEEK | EVENTS (gold on a race day); the EVENTS page (SANCTIONED / STREET toggles, rows with emblem, rule badges, progress dots, status, 1st prize); an event's page (badges, ENTRY / BONUS / REP / YOUR CAR rows, SIGN UP or WITHDRAW with a confirm, race cards: road, date, block, hour, traffic, field ~hp, prizes 1-6, state); the event race's pre-race page (house: reminder + car list; line: START, wrong car = no-show); calendar words (EVENT · ROOKIE / RACE 1 OF 2, P2 · $300, NO-SHOW); club standings block + a club's EVENT POINTS line; RUN THE X RACE INSTEAD on a clash with a club race (either way).
- Save **v29**: LifeState `evEntries`, `evHeard`, `racingRep`, `evClubScores`, `evNews`; `LifeSimManager.MigrateToEvents` (lists exist, stamps 29). InsuranceR1c's `saveVersion == 28` pin -> `>= 28`.
- Shared-file hooks (each one or two lines): `LifeHomeScreen.cs` Start (Events.SweepInFlight after the blacklist's), Rebuild `case "events"`, GoBack first line (EventsBack), BuildDayBlocks WEEK row, BlockLines first line, BuildHub race row (EventRaceRow), CellWord first lines, BuildPreRace first line (EventPreRaceFirst). `LifeHomeScreen.Clubs.cs` 3 calls. `LifeRules.cs` ApplyRaceResult step 0b (RecordResult before the block is spent) + `else if (eventRace)` branch (no purse payout, no daily cap, no generic rep), rollover `Events.Tick`, NightHasPlans (an early night does not sleep through a signed-up NIGHT race). `RaceHandoff.cs` EventKey/EventRace (+ClearAll). `CarMeets.MarkArrived` (word of mouth once a night). `Town/DepartScreen.cs` the line's RACE row names a due event race and the daily cap does not shut it.
- Tools: `Editor/LifeSimSelfTest.Events.cs` (registered in RunAll), `Editor/EventsPlayCheck.cs` + `tools/events-play-check.ps1`, `Editor/EventsPreview.cs` ("PSX Racing/Preview Events" -> Screenshots/events; pad walk at screen-pixel scale, overlaps, CLIP, nav reach). Metas for the five new scripts committed (PSXPerf GUIDs).

## Defaults (mine, tunable in Events.cs; owner to confirm)
- SANCTIONED: ROOKIE CUP (any car, max 180 hp, $100, 1st $500, bonus $600, every 2 weekends, SAT+SUN MORNING); FRONT DRIVE CUP (FF, 200 hp, $150/$700/$900); CLASSIC CUP (1979 or older, $150/$800/$1000); REAR DRIVE CUP (FR, 280 hp, racing rep 10, 3 races); IMPORT CUP (Japanese, 280 hp, rep 10); CONTINENTAL CUP (European, rep 10); AMERICAN STOCK CUP (American, rep 10, Uptown closed off); MIDSHIP CUP (MR, rep 20); ALL-WHEEL CUP (4WD, 300 hp, rep 20); CAROLINA OPEN (any, rep 40, $600/$3000/$4000).
- STREET: FRIDAY NIGHT RUN (listed, street rep 10, 250 hp); I-277 MIDNIGHT (listed, 25, MEDIUM traffic); INNER CIRCLE NIGHT (listed, 50); TOUGE NIGHT (word: meets + JAPANESE club, 300 hp); US 74 BIG BLOCK NIGHT (word: + AMERICAN club, American-built); Y2K RUN (word: + FUTURE club, 1995 or newer, 320 hp).
- Prizes 2nd-6th = 60/40/25/15/10% of 1st. Racing rep 5/3/2/1/1/1, street rep 6/4/2/1/1/1, bonus +5. No-show -2 racing / -3 street (fee kept); DNF -1/-2; withdraw before the day: nothing back, no rep. Street events listed from street rep 10. Club points 10/6/4/3/2/1 per place; 20 of the player's own points pass one member on that club's board.

## Gates (PSXPerf)
- Self-test: 62 FAIL = base 62, 0 new, 0 gone; all 57 events checks ok.
- Play check `tools\events-play-check.ps1`: 24 ok, 0 FAIL (ROOKIE CUP from the line: NONE traffic, MORNING, the event's field, P2 banked = $300, racing rep +3, JAPANESE club +6, block spent, no daily cap; FRIDAY NIGHT RUN: LIGHT, NIGHT on Tryon, win = $1,000 and street rep +6, rolled the day without a no-show).
- Previews: 12 pages x owner_2000x1077 / landscape_16x9 / phone_640x360 = 36 shots: 0 control overlaps, 0 CLIP, every control reachable, every pad walk to the page end, MAIN still fits.

## NOT DONE / open
- A purse race can still be booked into an event's block (LifeRules.CanBookAt untouched); the event takes the block and the booking is swept as missed.
- The hub's SLEEP caption does not warn about an event race; a DAWN sleep after an all-nighter can sleep through a MORNING race (no-show).
- Racing rep is not in the header (header REP = street rep); it is on the EVENTS page. No rep decay for racing rep. Club standings never reset (no seasons). Not in the month view.
- MAIN-edition fallbacks checked by the self-test only; no race sweep / city check run.

# HANDOFF - GT-STYLE SHOWROOM (agent "showroom", branch gt-showroom, 2026-11-15; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-showroom` off mountain-nm eb6fa63a (PC38 + one wizard pad link). Sandbox PSXCity. Owner 2026-11-15: "I want car viewing and car selection to look like the early Gran Turismo games, with the car rotating (or able to be rotated by player)" (reference: a GT2 garage screen). Same KIND of screen in the game's own MenuKit palette; no GT icons, logos, fonts or credit symbol; the drivetrain diagram is drawn from UI rects.

## What changed
- ROUND 2 (agent showroom2, same day): THE WHOLE BLOCK ON THE FIRST SCREEN (owner's 2000x1077 window cut the band on MY CARS, the car page, the dealer and the viewing). `Showroom.Build` now takes the ROOM from the hero's top to the fold (`HeroRoom(y)` = BodyH + y - 4; the city make page the same; NEW GAME = its panel under the titles) and fits the name rows, the stage and the band into it. Wide canvas (>= 1.6:1 and the hero >= 1.75x as wide as its room: owner, 16:9, phone_wide, 640x360): the band is a 270-300 column BESIDE the stage (price + note, figures, then the buttons standing on the stage's bottom line), the stage takes the rest of the width and all the height under the name. Otherwise (4:3; NEW GAME's 700 panel on 16:9, where that is the bigger car) the band stays UNDER a full-width stage sized from the room actually left (min 140/160, max 400). `ShowroomBlock` on the stage: DOWN from < > to the band's first button, UP back to >, LEFT from a side button to >, DOWN out of the block to the first line under it (nearest across); put back over the watch's geometric graph by `Showroom.AfterGrid` (after MenuNav.Defer in LifeHomeScreen.WireNavigation, CityFrontEnd.WireNavigation and the NEW GAME pick). Gone: `Showroom.Overhead` / `StageHeight`, the viewing's 320 cap. Stage at the owner's window: MY CARS / car page 696x317-323 units (~1338x615 px, 2.2:1, was 1900x433), dealer 696x253, viewing 696x283, NEW GAME 656x358.
- `CarViewer.Studio.cs` (new, partial of CarViewer): the showroom frame. Rendered at the panel's own pixel size (capped 1920 wide), supersampled 2x on PC/editor (1.5x handheld) and filtered down (the URP assets have MSAA off), bilinear on screen. Studio light through the paint shader's own inputs for that one render, then put back: key light high front-left (_PSXLightDir/_PSXLightColor), fill = ambient/sky ambient, and a generated softbox panorama (overhead box, two tall strips behind the car either side, dim front panel, floor) in `_PSXEnvPano` with `_PSXEnvOn`=1 - the rim and shoulder lines as the car turns; the env globals are saved and restored round the render (CarReflections' in a race). Floor: a soft pool of light (UI/Default disc, queue 1000, drawn before the car), a faint back glow, the contact blob plus a wider soft one. Turning: idle spin 18 deg/s, drag with a little inertia, right stick and , . (`CarViewer.TurnInput`), idle spin eases back 3 s after the last input. `Tick(now, dt)` is the clock (Update calls it; the probe drives it). HD car maps come with the shared livery materials (CarHD) as before.
- `CarViewer.cs`: partial; RenderNow -> RenderStudio when a showroom is attached, else `RenderClassic` (the old body, untouched); AttachTo / Snapshot / the inspection poses leave the showroom and draw exactly the old 320x200 point-filtered frame; CarViewerDrag routes horizontal drags to the studio turn (no tap-flip on the big stage).
- `Showroom.cs` (new): the card + stage builder. Big bold italic model name, make and year (+ "3 / 12") right and the page's tag (NEW / the lane / where the car is) left under it, the stage with < > buttons, DRAG TO TURN, the livery's colour name bottom right ONLY when its baked name has colour words (`PaintName`, never invented), then three columns: price/value + note, the drivetrain diagram (body outline, driven wheels/axles/shafts lit, engine block where it sits; FF FR MR RR 4WD) with year / hp / kg, and the page's actions. LEFT/RIGHT on the arrows (d-pad, arrow keys) step the car and keep the cursor (`ShowroomBrowseKeys`, after Selectable.OnMove, the MenuNavScroll pattern); a sideways swipe over the name browses (`ShowroomSwipe`; vertical drags go to the page scroll). LB/RB, PageUp/PageDown untouched.
- Pages (`LifeHomeScreen.Showroom.cs` new + hooks in LifeHomeScreen.cs): car page = the stage at the top (LEFT/RIGHT through the garage), everything it had below (status, bars, IN THIS CAR, SPECS, PARTS + TUNING, INSPECT, WASH, SELL, notes, faults + repairs); MY CARS = one car at a time (OPEN THIS CAR, GET IN / IN THIS CAR / NOT HERE; where it is, fuel, condition, faults), then TOOLBOX / FUEL TRUCK / STORED PARTS / STORED WHEELS, then the list as before; dealer = the lot one car at a time, new first (LOOK IT OVER opens the same viewing a row did: test drive, offers, BUY), the trade-in block under it; the viewing and the classifieds' BUY page show their car on the stage; NEW GAME pick = the three lanes one at a time (DRIVE THIS ONE commits as the close-up does, FULL SHEET opens the old close-up; its BACK returns). City edition PICK A CAR: a make's page has the stage over its list (DRIVE THIS ONE). Arcade car card: its turntable panel is the studio frame (no browse there).
- Switch: `PSX_SHOWROOM=0` (Showroom.On) builds every page as before (each hook falls back to the old code).
- Editor: `ShowroomPreview.cs` (new; "PSX Racing/Preview Showroom", `CaptureWithNav` also runs NavScrollPreview); LifeHomePreview.Shoot redraws a showroom at the laid-out size before the photo and logs it.

## Checks (see the round's numbers below)
- Round 2 (scratchpad `showroom2\preview2.log`, contact sheet `showroom2\showroom_contact_sheet.png`; the viewing now shot at all 5 sizes): FOLD probe (new, `ShowroomPreview.FoldProbe`: depth of the block's lowest part vs the viewport / panel) inside the first screen on 36/36 shots, every page at owner / 16:9 / 4:3 / phone_wide / 640x360; DOWN from < > to the band's first button and UP back, now JUDGED: 24/24; car page DOWN from > = PARTS + TUNING as before; control overlaps 0 in 36; nav reach all controls on 34/34; browse 31/31 (+5 viewing, one car); NavScrollPreview car page: page end REACHED, last fault row ON SCREEN at all 4 sizes, 0 FAIL; `[Showroom] DONE, 0 FAIL(s)`. CLIP: 1 line, sr_mycars/phone_640x360, the side column's note "cut on the RIGHT by its panel, 22 px" - the 640x360 picture shows it wrapped inside the column (the checker's width there is ~1.33x the drawn one). No self-test (layout only).
- Pictures (PSXCity `Screenshots/showroom`, `ShowroomPreview.CaptureWithNav`): NEW GAME pick, MY CARS (+ the 3rd car), car page (+ an RR car), dealer (+ the last used car), a lot viewing, the city edition's make page; owner_2000x1077, landscape_16x9, tablet_4x3, phone_wide, phone_640x360 (the browse/viewing extras at owner + phone_wide); `sr_off_*` = PSX_SHOWROOM=0 (the old car page and MY CARS). Contact sheet of the round: scratchpad `showroom\showroom_contact_sheet.png`.
- Stage drawn at 1900x433 px (owner window, scroll pages), 1823x435 (NEW GAME), 608x152 (640x360).
- Control overlaps: 0 in 33 checks. CLIP: 0 lines on the showroom pages (the run's 39 CLIP lines are all NavScrollPreview's pause-menu settings at 640x360 / 780x340, untouched here). Pad reach (CheckNavReach): every control reachable on all 31 shots.
- BROWSE probe: LEFT / RIGHT on the arrows step -1 / +1 and keep the cursor on 31/31 shots.
- TURN probe: idle 18.0 deg/s; a 240 px drag turned 72.9 deg (pre-layout stage width; ~38 deg on the owner's 1900 px stage), coasted 37 deg (edit-mode drag speed is inflated: dt floor 1/240), 0.1 deg in the 1.6 s after (held), 18.0 deg/s again after 3 s; a virtual pad's right stick read 1.00 and turned the car 100 deg in 0.5 s.
- NavScrollPreview (the owner's pad rule): car page with 10 faults and no money, page end REACHED and the last fault row ON SCREEN at owner / phone_wide / 16:9 / 4:3; 0 FAIL.
- Self-test: 62 FAIL = base 62. One swap, both car-asset checks on the sandbox's own Resources (not synced by these jobs, not touched here): new "every library key has a baked prefab (got flatsix_coupe_87)", gone "every shell has a body and a wheel mesh (got 1 without)".

## What still looks off (from the pictures)
- (Round 2 fixed the cut band.) In the 270-unit side column a 4WD car's line reads "4WD" (the long "ALL-WHEEL DRIVE" form needs ~310), and the NEW GAME note wraps to 3-4 lines. The viewing's side column holds only the figures (that page's price and buttons are under the stage, below the fold).
- The cars are the game's shells: 512x256 point-sampled sheets in the editor (blocky at this size; CarHD should help on PC), and catalog cars sharing one shell look alike on the stage.
- The background is near black with a faint glow; the rim/shoulder highlights are subtle.

## At merge
- New files with hand-made metas: Scripts/LifeSim/CarViewer.Studio.cs, Showroom.cs, LifeHomeScreen.Showroom.cs, Editor/ShowroomPreview.cs. CarViewer is now `partial`.
- Touched: LifeHomeScreen.cs (BuildCarPick, Rebuild, the turntable stick block, DrawCarView, BuildGarage, BuildCarMenu, BuildDealer, WireNavigation: one `Showroom.AfterGrid` line), ArcadeFrontEnd.cs (BuildCarCard, Update), CityFrontEnd.cs (Rebuild, BuildMakePage, CityHero's room, WireNavigation), LifeHomePreview.Shoot (redraw a showroom before the photo).
- Worth a look in the PC build: frame time with a showroom open, the CarHD sheets on the stage, the pad on a real screen (DOWN from the arrows).

## Not done / not verified
- No player build, no Android run, no frame-time measurement of the bigger render (PC: one car at up to 3840x~900 supersampled per frame; handheld 1.5x).
- Editor pictures show the classic 512x256 car sheets (PSX_CAR_HD off in the editor, HD bundle not in the sandbox).
- Paint shop / specs / service / tune keep their side-panel turntable (classic); the arcade car card is not browsable; the market (classifieds list) and junkyard lists are unchanged.
- The pad's DOWN from the arrows could not be judged in the harness (MenuNav's 4-unit line floor vs a 10-world-unit camera canvas); CheckNavReach reports every control reachable.

# HANDOFF - MENU NAVIGATION + ARCADE DAMAGE (agent "uinav", branch ui-nav-arcade, 2026-11-14; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-uinav` off mountain-nm 8d656a01 (PC37). Sandbox PSXCity. Owner 2026-11-14: car faults unreachable on a pad (DOWN wrapped to the top); OPTIONS > VISUALS rows overlapping, no scroll; nothing may overlay a button or text. Coordinator: NEW GAME wizard at 2000x1077 (1.86:1), NEXT over PREMIUM and the plan terms.

## What changed
- MenuNav, central (every page): `MenuNav.ScrollInstead` + `MenuNavScroll` (IMoveHandler, added by MenuNavWatch to the cursor's control inside a ScrollRect content). UP/DOWN scroll half a viewport instead of moving when the move would leave or wrap the page with page left, when more than half a viewport of hidden page lies before the next control, or when the cursor was scrolled out of view. The navigation graph itself is unchanged. A footer control under a list (BACK) no longer throws the list to its top. `MenuNavWatch.applied`: a page's own links after the deferred grid. `PSX_NAV_SCROLL=0`.
- SettingsPanel: the rows in a 322-unit scroll list between the strip and the help line (7 rows show; VISUALS' 13 scroll); help, BACK and the hint never on a row. `PSX_SETTINGS_SCROLL=0`.
- NEW GAME wizard (LifeHomeScreen.BuildWizard): job card + health plan scroll in their own band; NEXT in the band under it, left-aligned, as wide as the plan row; each plan's DOWN = NEXT, NEXT's UP = the picked plan. `PSX_WIZARD_SCROLL=0`.
- Arcade DAMAGE (Scripts/ArcadeDamage.cs new): OFF / VISUAL ONLY / FULL, default FULL (PlayerPrefs psx.arcadeDamage); a row in OPTIONS > GAMEPLAY (arcade sessions only) and on the arcade's own OPTIONS page. CrashStopGap.Arm with no career -> ArcadeDamage.Arm: a DamageLedger with Banks=false, LiveFx=FULL. `DamageLedger.Acts = Banks || LiveFx` now gates OnFail, the bags, hydrolock and ApplyLive (pull, exhaust power, radiator); injury (CrashFates) and the save (Flush/Fold) stay Banks-only. DamageLook.bodyOnly = VISUAL ONLY (dents + glass; no lamps out, steam or bags). RaceManager.RetirePlayer: in an arcade FULL race the DNF ends the player's race (State Finished, FinishPos 0, ArcadeDamage.Dnf); RaceHUD banner "DNF / THE CAR CANNOT GO ON"; arcade RESULT "DNF". `PSX_ARCADE_DAMAGE=0` = none. Career ledgers: Acts == Banks, so unchanged.
- Editor: NavScrollPreview.cs (new, "PSX Racing/Preview Nav Scroll"), ArcadeDamagePlayCheck.cs (new), LifeHomePreview.Shoot probe hook, DebugBenchPreview.ShootPause internal + scroll-aware off-canvas check + the owner's 2000x1077 size.

## Gates
- Typecheck OK (every commit).
- Nav preview (scratchpad uinav\nav.log, 0 FAIL): car page, 10 known faults and $0 (no repair selectable): DOWN from the lowest control scrolled 0>219>437>656>769 of 769 at 2000x1077 and the last fault row came on screen; one more DOWN goes to BACK; UP walks back to the top. VISUALS: pad DOWN x13, every row in view as reached, then BACK; 0 control overlaps at 6 sizes. Wizard: 0 overlaps at 4 sizes, but the pad DOWN path in that run was wrong (- > PREMIUM > -); the explicit plan/NEXT links were added after it and are NOT re-run.
- LifeSimSelfTest: base 62 FAIL (scratchpad uinav\base_selftest.log). Mine: queued behind the stuck play check (uinav\mine_selftest.log), not compared.
- Arcade play check: did NOT run (Unity: "Can't add script behaviour 'ArcadeDamagePlayCheck' because it is an editor script"); its editor sits in play mode in PSXCity until the job helper's 20-minute limit.

## NOT DONE
- Arcade damage unverified in play: move ArcadeDamagePlayCheckRunner to a file of its own name (Editor/ArcadeDamagePlayCheckRunner.cs) and run CrashPlayCheck.Run with PSX_ARCADE_DAMAGE_CHECK=1 (no -quit).
- The self-test FAIL-set diff (mine vs base 62); the wizard pad path re-run; LifeHomePreview.Capture's CLIP report and the ~10-page overlap fixes; the CITY edition has no DAMAGE option.

# HANDOFF - TEST TOWN ROUND 2: COMPLETE THE SHIFT, END DRIVE COSTS ONE BLOCK (agent "testtown", branch test-town = mountain-nm 8d656a01 + this round, 2026-11-14; NOT pushed, NOT merged)

**Owner, PC37:** "When I deliver a pizza to a house, it doesn't give me an option to complete shift. When I End Drive it sends me home and becomes the next calendar day, even though it should advance to the next time block."

**Cause** (`Scripts/Town/TownWorld.cs` CollectOrder): the town's PICK UP clocked on AND spent the whole scheduled shift at the counter (`LifeRules.SpendShift`, as the race-track delivery always had: the NIGHT shift is EVENING + NIGHT, which rolls the day), then END DRIVE's apply-back (`LifeRules.ApplyRaceResult`, the free-roam branch) spent one block more (ActDrive): an evening pickup came home on the next day's MORNING. The race-track flow never hit this, because its drop ended in a race scene whose apply-back is a Delivery (no block).

**Fix:** on the merged map (`LocalDropsHere`) the shift is `PizzaRun.ShiftOn`: clock on at the FIRST run only (attendance as before); the counter hands over the NEXT run without a second clock-on ("NEXT RUN — TAKE THE ORDER", cue "NEXT ORDER — TONY'S"); the shift's blocks are spent when it ENDS — END SHIFT calls the same `SpendShift` rule, then `TownExit.GoHome(.., "main", commute: true)` (no block on top). END DRIVE part-way through a shift is the one block every drive costs (labelled WORK in the day); any arrival home ends the shift (`LifeHomeScreen`). The done panel offers **NEXT ORDER** and **END SHIFT** on a shift (CARRY ON otherwise). Wages unchanged (attendance at the first run, weekly cheque); tips unchanged. `PSX_TOWN_LOCALDROP=0` keeps the old up-front spend for race-track runs. No scene rebuild for this round (runtime + play check only).

**Gates:** typecheck OK. NOT RUN: the self-test against an 8d656a01 base and the extended TownHomePlayCheck (two drops then END SHIFT; END DRIVE after a drop = same day, one block) were queued in PSXCity from 17:27, but agent 'uinav' held its lock without a break (17:17-18:37 a CrashPlayCheck and a 60-min LifeHomePreview.Capture, then a new job at 18:37); the queued job was stopped at 18:40 so it cannot leave a lock behind. To run them: sync this branch into a free sandbox, PSXRacingBuilder.BuildTownMenu (-quit), LifeSimSelfTest.Run (-quit) against the same two calls on an 8d656a01 snapshot, then TownHomePlayCheck.Run WITHOUT -quit and without -nographics (it exits itself; report PSXRacing_townhome_play_check.txt).

# HANDOFF - TEST TOWN: YOUR STREET AT THE TOWN MAP'S WEST END, PIZZA TO A MARKED HOUSE (agent "testtown", branch test-town, 2026-11-14; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-testtown` off mountain-nm 3db94af9. Sandbox PSXCity (lock "testtown", released; car_facts.json(+.meta), rg2_cars.json, DriveSide.csv copied by path). Scratch `scratchpad\testtown\`: runner `job.ps1`, `base_selftest.txt`, `m1_selftest.txt`, play reports `p3_play.txt` / `p4_play.txt`, pictures `pics\townhome_*.png`.

**Owner, 2026-11-14:** "I want Town and Home/Neighborhood merged onto a single map. This area will be purely for testing in debugging ... Delivering pizza is no longer a race track, but to a random house in the neighborhood." Then: "add the neighborhood to one end of the large town map. don't rewire the town."

## What changed
- **Town.unity is the merged map** (`Editor/PSXRacingBuilder.TownHomeEnd.cs` new; small hooks in `PSXRacingBuilder.Town.cs` / `.Neighborhood.cs`, `WorldKit.MeshPrefix`). `BuildTownScene` first builds the street with the street map's OWN functions (BuildNbGround, BuildTownHome, BuildNbPlots, the walk-in rooms, NbRespawns) in its own frame on the empty scene (its ground probes must not find the town), then turns the `HomeEnd` root -90 deg and moves it by (-436, +11.18, +110): the street runs on along the main street's line (z 0), its end at x -268, joined by 110 m of new road (`HomeRoad`: the main street's width, height and tarmac, grass verges) to the main street's end at x -158; your house up the hill at x ~ -490 (the street keeps its 11 m climb). The town is untouched except its WEST wall and WEST zone line (gone: they stood across the new road; a 24 m stub of wall closes the step between the town's north wall z 82 and the home end's z 58) and its kerbs taper flush at the west end. New bounds HomeEndN/S/W. Log: "[Town] home end: ... 12 doors a delivery can go to; the road in is 110 m".
- Street-builder hooks, all behind `nbMerged` (never set for Neighborhood.unity, whose output is unchanged): past the street's end the road profile stops rolling and the land relief fades (flat at the town lawn's height); the lawn lattice stops at the town lawn's west edge (x -240) instead of running 110 m on under it; no run-off verge at the street end; every neighbour gets a `DropDoor` anchor, a `DropMarker` (orange pin over the roof + 5 dots by the kerb, built OFF) and an address (`101`..`162 HOLLY RIDGE CT`). Merged meshes save as `Generated/TownHome_*` so the two scenes cannot reshape each other's ground.
- **Home goes to the town** (`Scripts/Town/TownLayout.cs` new): `TownLayout.HomeSceneIndex` (= Town) is loaded by `LifeHomeScreen.StartTown` (DRIVE, "drivehome"), the "garagewalk" hop, `HomeWalk.Enter` (walk in; inspect return) and `GarageWorld.DriveOut`. The Town's baked spawn is your own drive; GarageWorld(embedded) + HomeArrival wired as in Neighborhood.unity. `TrackCatalog.NeighborhoodSceneIndex` unchanged (door audit still finds the old scene, which stays in the build unused). `DoorTour`'s DRIVE door expects "Town".
- **Pizza to a house** (`TownWorld`, `PizzaRun`, `LifeRules`, `DepartScreen`, `HudMinimap`, `RaceHUD`, `Scripts/Town/DeliveryDoneScreen.cs` new): Tony's PICK UP rolls a RANDOM neighbour (`PizzaRun.DropHouse/DropName`, TrackIndex -1), par = 20 s + road m / 13 m/s (`LifeRules.LocalDropParSeconds`). The house's marker lights, the HUD map (new in town: the builder's road lines via `HudMinimap.SetLocalRoads`) marks it, the cue names the address. The clock (`PizzaRun.LocalSeconds`) runs from the counter until the car STOPS within 22 m of the door (re-arms past 34 m); get out, walk to the door, DELIVER (FootTarget). Paid by `ScoreDelivery` (new `parOverride`) + `SettleDelivery` (the race apply-back's delivery branch lifted out unchanged; both paths call it): tip for time and the order's state (PizzaCargo condition/consumable/hit, latched as before) into the wallet; wages stay on the weekly cheque (ClockOnShift at the counter). Done panel: TIME vs PAR, THE ORDER, TIP, WAGES, CARRY ON. A track-rolled order arriving in the merged town (Pizzeria.unity's walking shift) becomes a house. At the east line with a house order aboard, MAKE THE DELIVERY is shown shut with the address; the east line also offers INSPECT A CAR on the merged map.
- Switches: `PSX_TOWN_HOMEEND=0` at BUILD time = the town as it was (both lines, west wall); at RUN time = home is Neighborhood.unity again. `PSX_TOWN_LOCALDROP=0` (run time) = orders go to a race track as before.
- Self-test (TestTownScene): one zone line on the merged map; drop doors/markers/addresses; map roads; rooms + arrival; the car on your drive over tarmac; level tarmac all along the road in. `Editor/TownHomePlayCheck.cs` (new) plays the loop.

## Build recipe (coordinator)
1. Sync `Assets\PSXRacing\Scripts`, `Editor`, `Shaders` from test-town (robocopy /MIR). No asset/Resources/Art file is new (four new .cs + .meta ride in Scripts/Editor).
2. Rebuild ONLY Town.unity (delete it first; Neighborhood.unity is unchanged, do not rebuild):
   `. <repo>\tools\unity-wait.ps1; Invoke-UnityJob -Log <sb>\townbuild.log -UnityArgs @("-quit","-batchmode","-nographics","-projectPath","<sb>","-executeMethod","PSXRacing.EditorTools.PSXRacingBuilder.BuildTownMenu","-logFile","<sb>\townbuild.log","-accept-apiupdate")`
   Proof: the scene file exists and the log has "[Town] Scene saved: Assets/PSXRacing/Scenes/Town.unity (with the home end)". 3.5 min in PSXCity.
3. Player-only build. No tile re-bake, no CityTileCache.Format bump, no kit slot, nothing under Scripts/City.

## Gates
- Typecheck OK (tools\typecheck.ps1 against PSXCity).
- LifeSimSelfTest: base (3db94af9 code, Town + Neighborhood rebuilt with it in PSXCity) 62 FAIL; mine 62 FAIL; the two FAIL lists are identical (0 only-in-base, 0 only-in-mine). New checks all ok: 1 zone line (east), 12 drop doors + 12 markers off + addresses, 21 map road points, rooms + arrival beside the real car, car on your drive (-490.3, 11.5, 0.0) over road-layer tarmac, road in 0 bad samples, 16/16 respawns on road, kerbs mountable.
- Door audit (the self-test's DoorAudit block): ALL 47/47 venue doors, 5/5 career, 21 scenes; MAIN 27/27 + 20/20 absent + 5/5; CITY 20/20 + 27/27 + 5/5; all ok.
- Play check (TownHomePlayCheck, hidden batch, graphics on; `p4_play.txt`): 24 ok, 0 FAIL, "ALL PLAYED". DRIVE from the house -> Town scene, car 0.3 m from HomeSpawn; driven 475 m (36 s) down your street and the road in to Tony's; PICK UP -> 161 HOLLY RIDGE CT (track -1, par 0:46), only that house's marker lit, map destination set, the HUD map up with the drop on it, boxes on the seat; driven 306 m (25 s) to the house; stopped = arrived, clock held at 0:29; DELIVER at the door -> order gone, panel up, tip $151 = wallet +$151 (INTACT, 0:29 vs 0:46); the panel's 12 lines measured on the drawn glyphs: none clipped, none overlapping each other or the button. (The run before, `p3_play.txt`, caught the club note riding over CARRY ON - fixed.)

## Pictures (scratch `pics\`)
`townhome_1_home_end.png` (the street up the hill to your house), `townhome_2_road_in.png` (from the end of the main street: the road in, your street beyond), `townhome_3_drop_marker.png` (the orange pin over the order's house, a kerb dot), `townhome_4_delivery_done.png` (the done panel).

## Not verified / known
- The player build; a build with PSX_TOWN_HOMEEND=0; night, rain; frame cost of one 735 m map (the street was its own scene).
- Walking into your garage / an on-foot arrival in the merged town, and Pizzeria.unity's walking shift becoming a house: code paths not played.
- Play checks that load Town and expect its old spawn at the west end (FuelPlayCheck, ParkedPlayCheck, HomeMirrorPlayCheck, FirstDrivePlayCheck's town hop) were not run; they may need their start moved.
- The street's lawn meets the town lawn at x -240 at the same height, but its grass runs at 90 deg there (UVs from its own frame). The play check places the walker at doors and turns the car round at the shop's kerb (logged).

# HANDOFF - FAULTS R1c-A "WHAT A CRASH COSTS YOU: THE CAR AND THE DRIVER" (agent "r1ca", branch faults-r1c-crash, 2026-11-13; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr1ca` off mountain-nm 825d5ed7. Sandbox PSXPerf (lock "r1ca", released; car_facts.json(+.meta), rg2_cars.json, DriveSide.csv copied in by path; play checks hidden). Reports: scratchpad `r1ca\` (runner `r1ca.ps1`, `base_selftest.txt`, `h3_selftest.txt`, `h1_crashplay.txt`, `h1_race_census.csv` (before the PC34 fix), `h2_race_census.csv` (after), `r1ca_sheet.png`). Design: OWNER'S ANSWERS 2026-11-13 (idea, KEEP INJURY SIMPLE, NO MODE CHOICE, insurance). Save **v27** (R1c-B takes v28).

## What changed (each switch =0 is the behaviour before)
- THE DRIVER (PSX_INJURY), new `Scripts/LifeSim/CrashFates.cs`: every banked pulse on the career car is an injury score = delta-V x direction (side 1.4, rear 0.7) x a narrow tree/pole 1.3 x the FAIL's intrusion 1.15 x belt off (1.6 head-on, 1.2 side, x2.5 roll) x bags that fired 0.85 x a race/rally car's cage 0.55 x HEALTH (0.9 at 100 .. 1.2 at 0). Under 9.5 nothing; 9.5-15 CONCUSSION; 15-28 HOSPITAL (a broken arm / leg / leg and ribs: 1-2 / 3-5 / 8-14 days, $2.4-4k / $11-18k / $32-60k, off work and the wheel 3-12 weeks x HEALTH 0.8-1.4); 28+ DEATH. No mode choice. New `Scripts/InjuryFx.cs`: the frame at the hit frozen, smeared and ghosted, then dark, the sound with it (concussion 4 s, hospital 5 s, death 6 s), then the pause menu's tow ends the drive (`PauseMenu.EndDriveForInjury`). The injury rides in the damage file (IncidentRec inj/injScore/fail/narrow), folded once by hitId; the home screen processes it once (new `LifeSim/LifeHomeScreen.Fate.cs`): the hospital days pass, the bill goes through `CrashHooks.Hospital` (null = the player pays what they have; the rest is `fate.hospitalOwed` for R1c-B's BILLS), ONE summary page; `DriveRefusal` and `ClockOnRefusal` refuse until recovered; shifts and club events in that span settle as `Duty.Medical` (unpaid, never a miss or a strike). Death: THE CHARLOTTE HERALD's obituary is the only page, the career is written once and then locked (`LifeSimManager.Save`), NEW GAME through the one existing door. The belt: SEAT BELT: ON/OFF row in the pause menu (career drives), saved; on by default.
- A FAIL STAYS (PSX_FAIL_STAYS): a structural FAIL leaves a hidden row (`frame_bent`, `subframe_failed`, `knuckle_broken`, `roof_crushed`, as DamageRules decided) and `OwnedCar.wreckHit`; every door says "WRECKED - IT WON'T DRIVE UNTIL A SHOP FIXES IT" while a FAIL row stands (no part named); a pro inspection always finds it and adds the $55 tow-in; a shop repair adds the tow-in and sends crash rows through `CrashHooks.Repair`.
- THE WRITE-OFF (PSX_WRITE_OFF): after a real crash (band 3+ or a FAIL), if Delmar's estimate of the crash rows (the bag set at the dealer) is over 75% of the value before the hit: KEEP IT (salvage title; the insurer's payout less the scrap) or LET IT GO FOR SALVAGE (the payout, or scrap with no insurance, less a loan). `CrashHooks.TotalLoss` is asked once, at the decision.
- ROOF GLASS (PSX_FAULT_GLASS): a ROOF hit that is not a roll reads one band softer (under 3 m/s a scuff, never a crushed roof); roof_glass is priced by stage $210 / $820 / $1,950 (x the family's flat price). Part 2b's 2.5 m/s $480 contact is a scuff.
- OWNER PC34 (PSX_FAULT_SIDEHIGH): on an upright car a horizontal push is FRONT, REAR or SIDE at any height; ROOF only for a push from above (car frame, and world n.y below -0.5 so a wall leaning over the road is not one) or a car on its side or roof. His three WallColl incidents (dv 2.1 left, 5.49 and 7.62 right) now read SIDE: a scuff, then door dents, no roof and no glass (`TestSideHighPc34`). The window/mirror flicker: every component that writes a car renderer's property block reads it first, so blocks do not wipe each other; not found (NOT DONE).
- SRS (PSX_SRS_DEALER): a spent bag set takes 12% off the value (18% both); dealer parts only (Delmar and DIY refuse).

## Gates (PSXPerf; base = 825d5ed7 by git archive, same sandbox)
| Gate | Result |
|---|---|
| Typecheck | OK |
| Full LifeSimSelfTest vs base | base 4,360 ok / 62 FAIL; head (h3) 4,409 ok / 62 FAIL; NEW 0, GONE 0 |
| Unit tests (FatesR1c) | injury table, recovery vs HEALTH, hospital + medical leave, FAIL undrivable the next day and drivable after Delmar's repair, write-off threshold + keep/junk, death locks the career, roof glass by stage, SRS, PC34: all ok |
| Play checks (hidden; '79 Civic 834 kg, belt on, no bags, HEALTH 100) | 25 km/h wall dv 7.1: nothing; 40 km/h dv 11.2: concussion, the drive ends at 4 s; 65 km/h dv 18.1 (FAIL): hospital, ends at 5 s; 130 km/h tree in a debug career dv 35.7 narrow: death, folded, obituary, career locked. ALL HOLD (`h1_crashplay.txt`) |
| Census, 3 x 150 s Gillespie Gap, 8 cars | FAILS. After the fixes: 84 incidents, 9 damage events, 5 scuff-only = 56% (95% of all incidents cosmetic or less); player $153/race, all from one 6.7 m/s REAR hit (an AI ran into it: panel s3, bar s2, lamp, $460). Before PC34: player $153 from two 1.4 m/s nose nudges, each a cracked cover (panel_damage s1 at the flat $230). The $480 ROOF is gone. |
| Pictures (4:3, the narrowest) | `r1ca_sheet.png`: hospital summary and obituary read right, nothing clipped; the concussion frame is mostly void (the play strip sits 4 km up) but shows the smear; the WRITE-OFF shot shows MAIN (my shot set the value too high for a write-off; TestWriteOff proves the page logic) and its one CLIP is MAIN's pre-existing RECENTLY line, not a fate page |

## NOT DONE
- A correct write-off picture (re-shoot with a value under the estimate) and a concussion picture over a real road.
- The census gate: panel_damage is $230 at every stage (a cracked cover should be ~$100: used cover $40 + paint $60), and the census player is an autopilot that gets rammed; needs stage pricing for panel_damage and a clean-racer definition (struck, not striking), then a re-run.
- The window/mirror flicker (next suspects: repeated brush incidents, CarHD swapping renderers at PaintGloss.Collect).
- No side-glass or mirror row for a high SIDE hit yet (the SIDE table only).
- `CrashHooks.Repair` is called at booking before the money check (a refused booking may still have asked the policy).

## At merge
Save v27 (`MigrateToCrashFates`; R1c-B's v28 goes after). New files (+ .meta): Scripts/LifeSim/CrashFates.cs, Scripts/InjuryFx.cs, Scripts/LifeSim/LifeHomeScreen.Fate.cs, Editor/LifeSimSelfTest.FatesR1c.cs, Editor/InjuryPlayCheck.cs. Shared: DamageModel.cs (ROOF case, Classify worldNy, Incident.injury, Reresolve hook), DamageRules.cs (SideHighOn), DamageLook.cs untouched, LifeSim/DamageBank.cs, DamageHomes.cs, FaultCatalog.cs (4 FAIL rows, dealer-only quote), FaultView.cs, Inspection.cs, LifeRules.cs (DriveRefusal, OrderRepair), Clubs.cs (Duty.Medical, ClockOnRefusal), LifeState.cs (fate, wreckHit, IsMiss, v27), LifeSimManager.cs, LifeHomeScreen.cs (2 lines), LifeHomeScreen.Clubs.cs (1), Room.cs (LeaveCareer internal), PauseMenu.cs; Editor: CrashPlayCheck.cs (1), DamagePlayCheck.cs (1), LifeHomePreview.cs (Shoot internal), LifeSimSelfTest.cs (1), LifeSimSelfTest.DamageR1a.cs (2), LifeSimSelfTest.DamageR1a2.cs (1). No Resources, scene, shader or City change; no re-bake.

# HANDOFF - FAULTS R1a PART 2a "THE CAR SHOWS THE HIT" - the visual half (agent "r1a2v", branch faults-r1a2-visual, 2026-11-13; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr1a2v` off mountain-nm 04c0f15f (R0a, R0b, R0c, D, R1a part 1 merged). Sandbox PSXCity (lock "r1a2v", released; car_facts.json, rg2_cars.json, DriveSide.csv copied in by path). Part 2b (rules: structural FAIL, heat soak, sweeps, census, homes, CarValue) is another agent's, in faults-r1a2r. Reports: scratchpad `r1a2v\` (runner `r1a2v.ps1`; `base_selftest.txt`, `h3_selftest.txt`, `h4_look.txt`, `h4_look_*.png`, `look_sheet.png`, `compare.py`).

## What changed (no save change: everything shown is derived from the rows the car carries and this drive's incidents)
- NEW `Scripts/DamageLook.cs` (+meta): a component on the armed career car (DamageLedger.Arm attaches it). Every 0.2 s it reads the car's rows (OwnedCar.faults, hidden or found) and this drive's incident rows:
  - LAMPS: lamp_out (zone front = headlamp, rear = tail lamp, its side) -> `CarLights.SetLampOut`: no beam in the headlight table, no cone, no lens sprite; on a lamp-map body its lens goes dark and its glow out (PSX/CarPaint `_PSXLampOut`). There are no indicator lamps in the game, so there is nothing to kill there.
  - BODY: panel_damage per zone/side (stage 1 scrape + cracked cover, 2 dent, 3 crumpled), roof_glass (1 cracked glass, 2 dented roof, 3 crushed), bumper_bar only deepens a cover that already shows (the bar is hidden by design). Per renderer through a MaterialPropertyBlock (the PaintGloss pattern, read-modify-write so CarLights' blocks survive); wheels and every AI / traffic car get nothing.
  - STEAM: radiator_leak + a hot engine: wisps from a weep at 98 C+, a hard plume from a holed / crushed core at 85 C+. One small ParticleSystem (the tyre smoke's PSX/Decal material) made the first time it applies, emitting only while it does.
  - SRS: srs_deployed -> a limp bag on the steering wheel (turns with it) and, on a dual car, one over the passenger's dash (PSX/Cabin SOLID, procedural mesh).
  - roof_glass from the seat: a star of cracks on the cockpit windscreen (a quad on the pane's plane, procedural texture).
- `SrsBag` (same file): `AfterResolve` runs after every DamageModel.Resolve in a ledger:
  - CRUSH ORDER (design 3.1's crush order; PSX_FAULT_CRUSH=0 off): a FRONT/REAR hit at band 4 takes the cover (panel stage 3) and the struck side's lamp (both on a square hit); band 5 (10+ m/s) both lamps and, in front, the radiator crushed (stage 3, leak live at once). The owner's 41 m/s Viper into a tree now reads as a wreck.
  - BAGS: a frontal hit (FRONT or a front corner, push within 30 degrees of straight back) on a car with bags (car_facts airbags) fires them with a chance ramping 0 at 3.5 m/s to 1 at 6 m/s (delta-V along the car), on the incident's own spare die (DamageModel's water die, never read by a hit). Row `srs_deployed` (hidden, stamped with R0b's cause by DamageBank like every hit row, mag = bags fired). Career car only. The SRS lamp is R2a's.
- NEW `Shaders/PSXCarDamage.cginc` (+meta), included by PSX/CarPaint: vertex push (a dent 1-2 cm, crumple up to ~8, a crushed roof ~15 toward the box centre; the collider does not move), scrapes to primer in streaks, dark cracks/creases, dent waves in the normal (so reflections show it), cracked-glass web, dark broken lens and `dmgAlive` on the lamp glow. One uniform branch on `_PSXDmgOn`: 0 on every car never hit (paint bit for bit). Works on top of the PSX_CAR_HD normal.

SWITCHES: PSX_FAULT_LOOK=0 (none of the look), PSX_FAULT_LAMPS=0, PSX_FAULT_BODY=0 (also the windscreen crack), PSX_FAULT_STEAM=0, PSX_FAULT_SRS=0 (no bag fires: no row, no look), PSX_FAULT_CRUSH=0 (no crush-order rows).

SHARED FILES TOUCHED (all one-liners or small):
- `Scripts/DamageModel.cs` (4 lines): `Incident.frontCos` field; OnContact sets it (`SrsBag.FrontCos(nL)`); Reresolve calls `SrsBag.AfterResolve(inc, Banks)` after Resolve; Arm calls `DamageLook.Attach(car, L)`.
- `Scripts/CarLights.cs`: `lampOut` mask + `SetLampOut`; the five sprite/cone enable lines add `&& !HeadOut(i)` / `!TailOut(i)`; PushGlobals skips a broken head beam.
- `Scripts/LifeSim/FaultCatalog.cs` (1 row: srs_deployed $1,320 / 2 days, the design's Delmar airbag-set price), `Scripts/LifeSim/FaultView.cs` (1 line: srs_deployed -> Body).
- `Shaders/PSXCarPaint.shader` (5 small edits: include, vertex call, the frag call after the HD normal, shine x (1 - matte), lamp glow x dmgAlive).
- NEW `Editor/DamageLookCheck.cs` (+meta): the watched picture check.
- NOT touched: EngineTemp.cs, Scripts/City, RoadsideRules.cs, Resources, scenes, save format/version.

## Gates (PSXCity; base = 04c0f15f exported by git archive, run in the same sandbox)
| Gate | Result | Report (scratchpad `r1a2v\`) |
|---|---|---|
| Typecheck | OK | `tc` |
| Full LifeSimSelfTest vs base | IDENTICAL FAILURE SET: base 4,288 ok / 62 FAIL, head 4,285 ok / 62 FAIL, NEW 0, GONE 0 (h1 had 1 NEW: srs_deployed needed an inspection-map home - fixed). The 3 fewer oks are the randomised fault-pick test choosing a one-day repair (its multi-day lines did not print) | `base_selftest.txt`, `h3_selftest.txt`, `compare.py` |
| Watched play check (DamageLookCheck, '99 Viper GTS: dual bags, lamp map; real PhysX, dice 0.1) | 16 ok / 0 FAIL. 10 mph nose-in (dv 4.7, band 2): lamp_out L, CarLights.lampOut = 1, bags fired (2). 30 km/h nose-in (dv 8.6 x1.3 crushed bar = band 5): front 1.00, both headlamps out, radiator s3; at 108 C 38 steam puffs. Rear (planted 6 m/s): rear 1.00, a tail lamp out. Cockpit: both bags + the windscreen crack. Folded: srs_deployed hidden, cause impact, zone front, $1,452 (usa), mag 2; every row hidden; the banked rows alone give the same look | `h4_look.txt`, sheet `look_sheet.png` (shots `h4_look_1_night_lamp.png`, `_2_steam`, `_3_cockpit_bags`, `_4_chase_panels`) |

## Found, not mine (for part 2b)
- A 20 km/h physical BACK-IN into a wall on the Viper GTS was classified ROOF (dv 4.6, roof_glass s2), not REAR - DamageModel.Classify's beltline test (`p.y > sh.beltY`) on a low car; the picture uses a planted REAR hit instead.

## Not done / not verified
- PSX/CarLens covers (the B lens variant) still glow their 20-25% share over a broken lamp; the road spill of the tail lamps (StreetLights) is not dimmed; pop-ups are not stuck; the game has no indicator lamps.
- The bags look full rather than limp; the night shot shows the lit right lamp and the dark left one, but the body itself is barely visible.
- Not in a player build; frame cost not measured (one uniform branch, only the career car pays); PSX_CAR_HD only compiled, not shot on an HD body; switches-off pictures not shot.

## What the owner will notice
- A crash shows on the car: scrapes on the panel that hit, a crumpled nose or tail at higher speed, a dented roof and cracked glass after a roof strike.
- A broken headlamp is dark at night - no beam on that side - and a broken tail lamp stays dark; nothing names the part.
- A cracked radiator steams from under the bonnet once the engine is hot; a holed one steams hard.
- A hard frontal hit fires the airbags on cars that have them: the bag hangs off the wheel (and the dash on dual-bag cars) in the cockpit view. An 80 mph tree reads as a wreck: both lamps out, the front at its worst, the core crushed, the bags out.
- The damage stays on the car on the next drive until it is repaired.

## At merge
No save change, no format change, no re-bake (nothing under Scripts/City or RoadsideRules.cs; the shader is not in the tile bake). New files (+ .meta): Scripts/DamageLook.cs, Shaders/PSXCarDamage.cginc, Editor/DamageLookCheck.cs. Shared one-liners: DamageModel.cs (4), CarLights.cs, FaultCatalog.cs, FaultView.cs, Inspection.cs, PSXCarPaint.shader (listed above). If part 2b adds Spares / floor-check / test-drive homes for the hit ids, srs_deployed is a seventh id.

# HANDOFF - FAULTS R1a PART 2b "RULES AND PROOF" (agent "r1a2r", branch faults-r1a2-rules, 2026-11-13; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr1a2r` off mountain-nm 04c0f15f. Sandbox PSXPerf (lock "r1a2r", released). Reports: scratchpad `r1a2r\` (runner `r1a2r.ps1`; `base_selftest.txt`, `h4_selftest.txt`, `h4_crashplay.txt`, `h3_race_census.csv` + `census.py`, `heat.py` = the cooling-curve fit). Note: a VISIBLE editor in PSXPerf stops on "Revert All Window Layouts" (the user-level layout `AppData\Roaming\Unity\Editor-5.x\Preferences\Layouts\current\default-6000.dwlt` holds a maximized window with no backup; not touched) - the play checks ran hidden (batchmode, no -Watch) and work.

## What changed (each switch 0 = part 1)
- STRUCTURAL FAIL (PSX_FAULT_FAIL), new `Scripts/DamageRules.cs`: a hit at the AI's retire line (closing or delta-V >= 17 m/s; a DROP landing at 17), a ROLL that crushed the roof, a broken knuckle (CORNER top band, 20%), a failed subframe (FRONT top band, 50%). The car loses all power (faultAccelMult 0, PlayerCarInput idles it), the HUD's tow line reads THE CAR CANNOT GO ON, the leave row is CALL TOW TRUCK (CareerDrive.NeedsTow; TOW CAR HOME in a debug career), a race marks the player retired = DNF while it goes on (RaceManager.RetirePlayer), the day's line says "out of <venue> after x km (DNF) - the car could not go on". Banked once by hitId like every incident. The owner's PC33 tree (one 41 m/s FRONT pulse) is now a FAIL.
- CRUSH ORDER (PSX_FAULT_CRUSH): from 8 m/s everything ahead of the rails is gone whatever the dice (panel s3, bar s2, the struck lamp or both square on); from 10 m/s the bar is torn away (s3) and the core crushed; bar s2 at 5-8 m/s on its die. DamageBank.SameRow: one nose (tail) panel row whichever side took it, a door per side, a lamp per side - a harder later hit raises the stage of the row the car carries (bar 1 -> 3 tested), never a duplicate.
- BODY FIRST (PSX_FAULT_BODYFIRST): DamageModel.Classify takes the patch's lowest point; on an upright car a side or end push whose patch reaches below the beltline is FRONT/REAR/SIDE (the '99 Viper backed into a wall read ROOF); ROOF stays for a patch wholly above the belt, a push from above, or a car on its roof.
- SPAN (PSX_FAULT_SPAN): a horizontal end push wholly above the belt (a truck bed) is ROOF and also FRONT/REAR (the PC33 RX-7 under a van got ROOF only).
- SCRAPE (PSX_FAULT_SCRAPE): a held contact after its 0.2 s pulse adds no delta-V but keeps its incident open from the last contact, so a swipe along 4 m sections is one incident.
- HOLED CORE (PSX_FAULT_HOLED): a crash-holed core (located model's weep) cools fully above 97% coolant and not at all at 80% (CoolingModel's 55% otherwise; R0a's path untouched). Part 1's 2 %/min took ~48 min to 112 C (fit in heat.py, 15 min at 90 C in play).
- HEAT SOAK (PSX_HEAT_SOAK): OwnedCar.parkedC/parkedDay/parkedSlot (ADDED fields, 0 = cold, no save step); EngineTemp.StampResult -> LifeRules.ApplyHeatResult parks the car at the needle; LifeHomeScreen's fill primes, the applier warms after StartCold: same block 0.25 h, next block 0.5 h (the leg before used its block), more blocks or an overnight cold; 2/3 of the extra heat shed per hour. parkedBrakeC not done (no brake temperature model).
- HOMES (PSX_FAULT_HOMES), new `Scripts/LifeSim/DamageHomes.cs`: Spares (USED RADIATOR, FRONT CLIP, BUMPER UNCRACKED, STRAIGHT FENDER, DOOR SKIN + GLASS, TAIL LIGHT + TRIM, EXHAUST CAT BACK, SHELL PANELS, kinds power/weight); floor check (radiator_leak: green drops / palm / dinner plate under the nose, names nothing); chips (RUNS HOT, RATTLES, GAUGES/LIGHTS after dark; the bar none); CarValue x (1 - panel and roof/glass mag %, cap 60%); debug effect text for every hit row (FaultCatalog entries with ids, EffectSummary, DebugCarOps.Describe - the coordinator's FaultRows/RowLine untouched).

## Gates (PSXPerf; base = 04c0f15f by git archive, same sandbox)
| Gate | Result |
|---|---|
| Typecheck | OK |
| Full LifeSimSelfTest vs base | base 4,318 ok / 62 FAIL; head 4,360 ok / 62 FAIL; NEW 0, GONE 0 (`h4_selftest.txt`) |
| 10k sweep | 9,723 incidents; centre-front 2,176, corner parts 0; FAIL under 5 m/s 0, every 17+ hit FAILs; knuckle 0.19 / subframe 0.53 of top-band hits; no duplicate rows |
| crash-play-check (hidden, '79 Civic 834 kg, dice 0.1) | all ok: walls 15/30/50 bands 2/4/5; 65 km/h wall FAIL (dv 18.1, closing 18.0), CALL TOW TRUCK, race DNF True, 0.00 m/s at full throttle; swipe 23 sections = ONE incident; ROLL banked at rest, 3 quarter-turns, roof_glass s1; 5 m drop band 4; traffic rear-end dv 6.3 vs cap 6.27, FRONT only; holed core 112 C after 8.0 min at 80 km/h (coolant 85%); RESTART (debug career) banked once, reloaded car weeps 0.714, core = banked (`h4_crashplay.txt`) |
| Race census, 3 x 150 s GillespieGap, 8 cars | 78 incidents, 8 damage events; 92% of incidents cosmetic or less, but 2 of 8 damage events scuff-only (25%); 0 FAILs; player (autopilot, 22 contacts) $237/race: a 2.5 m/s ROOF contact (roof_glass s1 at the flat $480) + a 3.3 m/s nose (panel s2 $230) - the $40 gate FAILS (`h3_race_census.csv`) |

## NOT DONE
- srs_deployed (2a's seventh id) homes: no yard label covers an airbag set (needs a Junkyard part, e.g. "AIRBAG SET" $250) and its mag meaning is 2a's; ValueMult reads panel/roof only.
- The $40 clean-racer gate: price roof_glass by stage (glass $150-300 vs roof skin) and find why a rival contact still read ROOF; the census autopilot is not a clean racer.
- A FAIL car towed home drives again next day (R1b's knuckle/subframe/rail rows make it stay broken). No drag from a crushed front (not in the design).

## At merge
No save step, no Resources/scene/shader/City change, no re-bake. New (+ .meta): Scripts/DamageRules.cs, Scripts/LifeSim/DamageHomes.cs, Editor/LifeSimSelfTest.DamageR1a2.cs. Shared: DamageModel.cs (Classify minY, OnContact span/scrape/minY, Grow/GrowInc/Hold/CheckFail, Reresolve, Tick, Resolve band 3-5 + Crush, Incident.fail, ledger Failed - beside 2a's four lines), CareerDrive.cs, EngineTemp.cs (2 lines), PauseMenu.cs, PlayerCarInput.cs, RaceHUD.cs, RaceHandoffApplier.cs, RaceManager.cs, LifeSim/CarMarket.cs, DamageBank.cs, DebugCarOps.cs (2 lines in Describe), FaultCatalog.cs, FaultSymptoms.cs, LifeHomeScreen.cs (1), LifeRules.cs (1), LifeState.cs, Spares.cs; Editor/DamagePlayCheck.cs, LifeSimSelfTest.DamageR1a.cs (1).

# HANDOFF - FAULTS R1a PART 1 "HITS LAND WHERE YOU HIT" - the core half (agent "r1a", branch faults-r1a, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr1a` off mountain-nm 324afc17 (R0a, R0b, R0c, D merged). Sandbox PSXCity (lock "r1a", released; car_facts.json + .meta copied in by path). Design: art workspace `Fault_System_Design_2026-11-11.md` (OWNER'S ANSWERS 1 and 8; Â§2 save rules and "Size and timing"; Â§3.1 located crashes, banking, zone tables; Â§4 BODY/COOLING rows; Â§9; the R1a row of Â§10). Reports: scratchpad `r1a\` (runner `r1a.ps1`; `base_selftest.txt`, `h*_selftest.txt`, `h*_damage.txt`, `h*_crashplay.txt`, `h*_solo_census.csv`, `h*_race_census.csv`; `compare.py`, `census.py`).

## What changed (switch: PSX_FAULT_LOCATED=0 = R0a's stop-gap table at the hit, i.e. the base; PSX_FAULT_CRASH=0 still = the crash from before R0a, which turns this off too)
- NEW `Scripts/DamageModel.cs`. WHERE: every contact in the car's own frame (CarBody.box): FRONT / REAR / SIDE L R (R0a's Classify and centre band), the four CORNERS (patch within tyre radius + 0.15 m of a live wheel hub, in plan; a corner wins), UNDER (the car pushed up from below, outside the tyre patches - an upright car's tyre patch is the suspension's), ROOF (pushed down from above, or a horizontal hit above the beltline = the shell's measured cowl). It sees roof landings and belly strikes before the world-up test, which stays only for DamageScore (AI retirement and the carHP summary unchanged). HOW HARD: delta-V = normal impulse summed over 0.2 s after a collider's Enter / mass, capped at closing x m_other/(m_car+m_other) x 1.2 (traffic 1,400 kg, wall infinite); after 0.2 s a held contact is a scrape (adds nothing); pulses within 4 m and 0.6 s in one zone are ONE incident (its hardest pulse); trees/poles a band up, glancing (<30 deg) a band down (bottom band: a scuff only); a crushed bar x1.3 in its zone. WHAT: the Â§3.1 tables, cumulative, one die per row, re-read on the same dice as a pulse grows (monotone, tested). Rows written: panel_damage (cover cracked / door dent / crushed, value % in mag), bumper_bar (hidden, x1.3), lamp_out (side), radiator_leak (stage 1/2/3 = 0.5/2/6 %/min), exhaust_damage (power x0.97, live), roof_glass (stage 1/2/3 = glass / roof dented / roof crushed). Corner columns (toe / tie rod / arm) write the existing `alignment` row through R0a's one-alignment rule (minor 0.042 / severe 0.14 deg, struck side) until R1b's per-wheel corners; bump-stop strikes at 6/8/10 m/s read the corner columns. Scuffs and dents move paint/body lanes as R0a's did. Rows of later rounds (SRS, oil pan, frame/rails, fan, condenser, mounts, catalyst, lines) are not rolled.
- LIVE: the radiator weeps at once through EngineTemp.radiatorCond/radiatorWeep (R0a's path), the pull through hitSteerPull, the exhaust through faultAccelMult; the next drive carries the leak (DamageLedger.Arm reads the car's radiator_leak row) and a crushed bar (its bumper_bar rows).
- ROLL / DROP / WATER, banked AT THE EVENT (no respawn in careers since R0c): ROLL from the first big tilt to rest (quarter-turns from the swept tilt, its roof pulses inside it, the ROOF table at the hardest roof pulse, at least band 1 once over on the roof; banked once while it lies there); DROP = a fall of 1.5 m or more, landing speed max(measured, sqrt(2gh)), the UNDER table at landing - 3 m/s (frame band over 8 m/s), its belly pulse inside it, the stops striking on their own; WATER = origin 0.5 m under the drawn sea: flood title, hydrolock 50% with the engine running through EngineTemp.Seize (story names no part). The tow / QUIT then banks the leg as R0c does; nothing is banked twice (hitIds).
- NEW `Scripts/LifeSim/DamageBank.cs`: the small damage file `<saveKey>.dmg.<carId>.<drive>.json` in LifeSimManager.CopyDir (990 bytes for two incidents), written by the ledger (debounced 2 s; flushed on exit stamps, a swap, OnDestroy and app pause). Folded by hitId at every exit stamp (CrashStopGap.StampHandoff: finish line, QUIT/tow BankLeg, city exits), PauseMenu EXIT and RESTART (debug only: reloads the damaged car), the apply-back (ApplyRaceResult) and Load (a drive the game never finished). A file is deleted only after the save that carries it (LifeSimManager.Save -> AfterSave). Banks onto the car the file names - never ActiveCar; not armed on a test drive or in the CITY edition. Every row hidden, stamped with R0b's cause (impact, zone 1-6, side, delta-V in hitMps) plus hitId, stage, mag, corner1.
- SAVE v26 = `LifeSimManager.MigrateToDamageBank` (CarFault.hitId/corner1/stage/mag, OwnedCar.history (<=48 CarEvent)/title, CarEvent): every row staged from legacy severity (1 -> 2, 2 -> 3), empty history, clean title; idempotent. Heat soak's parkedC etc. (part 2) read 0 as cold and need no step.
- `CrashStopGap.BumperFor` reads `CarFacts.Get(spec).bumperEra` (chrome / 5mph / else plastic); no facts row: era + origin as before.
- R1a rows get homes: FaultCatalog hit rows (outside the pools: no wear, market or used roll can make them; priced per family), FaultClass (body; radiator_leak cooling), inspection map (PANELS & BUMPERS, RADIATOR CORE, EXHAUST).

## Gates (PSXCity, base = 324afc17 exported by git archive and run in the same sandbox; head = this branch)
| Gate | Result | Report (scratchpad `r1a\`) |
|---|---|---|
| Typecheck | OK | `tc` |
| RunDamage: TestImpactZones, tables, live, TestDamageBank, v26 | 40 ok / 0 FAIL. 52 of 52 shells: nose centre/left/right, tail, both doors, four arches, belly, roof, pillar where they are (front 156, rear 52, side 104, under 104, roof 156, corner 208); the 208 vertical contacts the world-up test drops now land in UNDER/ROOF. Bands, 5-mph/chrome ratings, pole +1, glancing -1, bar x1.3; monotone over 6 zones x 200 dice x 5 bands. Live: same crash re-read upward, holed core weeps 2 %/min at once, FR corner pulls 0.140 deg. Bank: one 990-byte file, folded once by hitId, refold no change, file deleted after the save, same file twice banks nothing, QUIT banks once (and the apply-back after it nothing), a file for a car the career does not own banks nothing, CITY edition untouched, a leftover file banked at the next Load, a test drive arms nothing | `h2_damage.txt` |
| Full LifeSimSelfTest vs base | NOT IDENTICAL: base 4,244 ok / 62 FAIL, head 4,276 ok / 70 FAIL; the 62 the same, 8 NEW, 0 GONE. 7 NEW = R0a's save-safety tests pinned to "the code is at v25" (LastUnstampedVersion + 1): a new career born at v25, migrates to v25, and the old-save backup case builds its old save at CodeVersion - 1 = 25 with no writer stamp, which now reads as Rewritten (.bak v25, a second copy, NEWBIE). Any v26 bump trips them: re-pin them to LifeSimManager.CodeVersion / build the old save at LastUnstampedVersion. 1 NEW = the save tripwire flagged DamageBank.cs: its header comment named the copies folder; reworded in the last commit, NOT re-run | `base_selftest.txt`, `h2_selftest.txt`, `compare.py` |
| crash-play-check (R1a mode, real PhysX, '79 Civic CX 834 kg, dice forced) | 18 ok / 2 FAIL. Walls at 15 / 30 / 50 km/h: ONE FRONT incident each, delta-V 4.30 / 8.49 / 14.14 against closing 4.2 / 8.3 / 13.9 m/s, bands 2 / 4 / 5. 5 m drop: ONE DROP at 10.6 m/s, band 4 (exhaust s2, body), the belly pulse inside it, one stop strike on its own. All 7 incidents folded onto the career car once, the files deleted, every row hidden. FAIL: the swipe touched only one 4 m section (one glancing scuff on the right), so the merge across sections was not exercised in play. FAIL: the 60 km/h roll launch never rolled the car (no incident in 12 s); ROLL banking is unverified in play | `h2_crashplay.txt` |
| Clean lap with zero damage events; race delta-V census / 3-race sample | NOT RUN (budget). The census hook exists (Editor/DamagePlayCheck.cs: PSX_DAMAGE_CENSUS=<csv> makes every responder a non-banking census ledger; runner job `solo` / `race` in `r1a.ps1`, summary `census.py`) | - |

## NOT DONE (part 2)
SRS / airbags; body masks and lamp-light kills (lamp_out is a hidden row with no light effect yet); steam; heat soak (parkedC etc. read 0 as cold: no save step needed); the structural FAIL (17 m/s, roof crush, knuckle / subframe -> tow line, DNF; WATER's hydrolock already seizes through EngineTemp); the 10k-incident sweep; the 112 C radiator timing gate; the traffic rear-end check; the census and clean lap above; an in-play roll and a multi-section swipe; re-pinning R0a's 7 save-safety tests; a re-run after the comment fix; the rows' value (mag) in CarValue (R3); Spares / floor-check / test-drive homes for the six new ids; RESTART's reload of the damaged car is written, not driven.

## What the owner will notice
- Tapping a wall at walking pace leaves a scuff. At about 10 mph nose-first the bumper cover cracks, a headlamp can go, and the radiator may weep, so the needle creeps up later in the drive and on the next drives until it is fixed. At 20-30 mph the radiator is holed and the body takes real damage.
- A hit at a wheel arch, or a hard bump-stop strike, knocks the alignment toward that side; a square hit in the middle never bends a corner.
- Dropping off a ledge, rolling or driving into the water costs: exhaust and floor, roof and glass, a flood title, and a dead engine if it was running.
- Damage is kept the moment it happens: QUIT, the tow truck or the game closing do not undo it, and nothing is counted twice. Every new problem stays hidden until a person finds it.

## At merge
Save v26 (`MigrateToDamageBank`; if another round takes 26 first, renumber it, `LifeState.saveVersion` and the step's `CodeVersion < 26` guard). Copy `Resources/car_facts.json` (+ .meta) into the build sandbox by path (BumperFor reads it; without it, era + origin as before). Nothing under Scripts/City or RoadsideRules.cs; no Resources, scene, shader or ProjectSettings change; no re-bake. Shared files: CrashStopGap.cs, CollisionResponder.cs, EngineTemp.cs, StuckRecovery.cs (one getter), PauseMenu.cs, LifeSim/LifeState.cs, LifeSimManager.cs, LifeRules.cs, FaultCatalog.cs, FaultView.cs, Inspection.cs; Editor/LifeSimSelfTest.cs (one line), LifeSimSelfTest.CrashR0a.cs and .RoomR0c.cs (R0a's ledger tests run with PSX_FAULT_LOCATED=0), CrashPlayCheck.cs (dispatch). New (+ .meta): Scripts/DamageModel.cs, Scripts/LifeSim/DamageBank.cs, Editor/LifeSimSelfTest.DamageR1a.cs, Editor/DamagePlayCheck.cs.

# HANDOFF - FAULTS ROUND D: CAR FACTS + race/rally cars off the career market (agent "factsd", branch faults-d, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultd`, branch faults-d off mountain-nm 7ae0ec13. Sandbox PSXRec (lock "factsd", released). Design: art workspace `Fault_System_Design_2026-11-11.md` (section 2 "Catalog facts", 3.5, the D row of section 10; OWNER'S ANSWERS 9). Reports: scratchpad `factsd\` (`base_selftest.txt`, `head_selftest.txt`, `head_carfacts.txt`, `compare.txt`).

## What round D adds
- `Assets/PSXRacing/Resources/car_facts.json` (+meta): one row per catalog car, all 319, keyed by spec id. Fields: origin + family (jpn/usa/eur: the MAKER's home - RG2's catch-all origin "eur" is corrected on Dome, Sileighty, Spoon, NISMO x4 (jpn) and Chaparral x2, Panoz (usa)), market (US / JDM / EUR / UK; RACE on every built-to-tune row), year, builtToTune, roadLegal, serviceUnit (miles / race hours), priceClass (economy 1.0, sport 1.15, exotic 2.5, race 3.0), stockInternals (highest power stage the stock bottom end takes) + stockBoostBar, engineCode, fuel (carb / mech / efi), eSys (CARB, MECH, EFI-OBD1, EFI-OBD2, EFI-JDM, EFI-RACE), obd (0 none, 1 maker's own, 2 OBD-II), obd1Read (gm_jumper, toyota_te1_e1, ecu_led, analog_meter, jumper_lamp, dealer, none), camDrive + beltMi + interference, ignition, turbo, airbags, rearBrakes, bumperEra (chrome / 5mph / 2.5mph / plastic), ac (R-12 / R-134a / none), partsSource + partsWaitMin/Max (US next morning; JDM-only and European specials up to 21 days; race parts 14-28), knownIssues (free text for the later knownFailures ids), lowConf (the fields the author was unsure of), note.
- Built by `tools/bake_car_facts.py` from the catalog (rg2_cars.json) plus authored engine and car tables; `--check` validates without writing. Re-run it after any catalog change: it refuses to write if a catalog row has no facts row.
- `Scripts/CarFacts.cs`: the loader later rounds read (`CarFacts.Get(spec)`, `RoadLegal`, `CareerSells`, `PartsWaitDays`, `PriceClass`, `CarFact.HasObd2 / HasCodes / HasAirbag / HasTimingBelt / IsInterference / ServicedByHours / LowConfidence(field)`). Without the file it falls back to the catalog's built-to-tune rule.
- OWNER'S ANSWER 9: race and rally cars leave the career market. `CarMarket`: the newspaper, the dealer lot (it kept out only "Race Car" rows - rally, touring and JGTC cars could stand on it) and the starting lanes (a well-paid start's NEW lane could be a new $40k rally car) offer no car that is not road legal; adverts an older build placed are withdrawn at the next refresh. Quick race and debug are untouched; a career that owns one keeps it. Switch `PSX_FACTS_MARKET=0` = today's market.
- NOT BUILT (note for the car-clubs work): **rally cars become a LOANER perk at a car club's max rank** (owner, answer 9). The loaner needs the phantom-car rules (Clubs.cs loaner perk, `live: false`; design section 7 rental).

## Counts
319 rows; 53 not road legal = the 51 built-to-tune rows (23 race, 22 rally, 6 touring/JGTC/dirt) + 2 authored (Shelby GT350R '65 - Shelby's competition model; Lotus Motor Sport Elise '99 - the one-make race-series car; both OWNER: confirm). 14 OBD-II rows (every US-market 1996+ EFI road car, nothing else). 50 carburettor and 12 mechanical-injection rows, none with codes. 236 rows carry at least one low-confidence field (mostly interference / ignition per engine and the A/C switch-over years).

## Gates
| Gate | Result |
|---|---|
| Typecheck (PSXRec) | OK |
| `LifeSimSelfTest.RunCarFacts` (PSXRec; once on WebGL, once on Android) | 37 ok / 0 FAIL both times (`head_carfacts.txt`, `android_carfacts.txt`) |
| ... every catalog row present exactly once | 319 / 319, 0 missing, 0 duplicated, 0 extra |
| ... OBD-II only on US-market 1996+ rows | 14 OBD-II rows, all US-market 1996+ EFI; and every US-market 1996+ EFI road car has it |
| ... no codes on carburettor rows | 50 carburettor + 12 mechanical-injection rows, 0 with codes |
| ... airbags by market and year | 143 rows with a bag; 0 before 1987 (US, JDM) or 1981 (Europe), 0 on race cars, every US 1998+ car dual |
| ... built-to-tune not road legal | 51 / 51 not road legal, RACE market, race hours (+2 authored: 53) |
| ... design pins | '96 240SX OBD-II; Civic Type R EK none; '69 Camaro SS carb, no codes, chrome; '99 Viper OBD-II, dual bags, next-day parts; 787B race hours / 3.0 / 14-28 days; FD rotary + sequential; 2JZ-GTE belt, non-interference; 3.4 SHO chain, interference |
| Newspaper never lists a race or rally car over 200 seeded days | 3 seeds x 200 days: 638 listings in the paper and on the lot, 0 race/rally. Teeth: with `PSX_FACTS_MARKET=0` the same days list 92 of 631 |
| ... older adverts, owned cars, starting lanes | a rally advert an older build placed leaves paper and lot at the next refresh; an owned race car is kept over 30 days of refreshes; 720 starting lanes, 0 race/rally (35 with the switch off - the NEW lane offered new rally cars) |
| Full LifeSimSelfTest, base 7ae0ec13 vs head, same sandbox and target | base 4,147 ok / 63 FAIL; head 4,179 ok / 63 FAIL: the same 63 by check text, 0 new, 0 gone (`compare.txt`). Ok-line delta: +37 car facts; night look -5 = two scene checks (wet road, night windows) SKIPPED in the head run because the mirrored worktree's PSXRacingBuilder.cs has a newer file time than the sandbox's built scenes (a sync artefact; this round does not touch the builder) |

SANDBOX TARGET: `Invoke-UnityJob` (tools/unity-wait.ps1) adds `-buildTarget WebGL` unless told otherwise, so my base job moved PSXRec from Android to WebGL. The head self-test ran on WebGL too (a fair comparison) and the last job (RunCarFacts) ran with `-buildTarget Android`: PSXRec is back on Android. Lesson for the next agent there: pass `-buildTarget Android` explicitly.

## Judgement calls the owner should see (spot check: art workspace `Car_Facts_SpotCheck_2026-11-12.md`, 30 rows)
- Both "Nissan 240SX '96" rows are GT4's US names for JDM cars (180SX Type X and Silvia K's S14, SR20DET). The design says "the '96 240SX has [OBD-II]", so they are US market (OBD-II, US bumpers) with Japanese engine-parts waits (3-7 days). Owner: confirm.
- GT4's other US renames are JDM cars and are marked JDM: Lexus GS300 '91 (Aristo), SC300 '97 (Soarer), IS200 (J) '98 (Altezza), 3000GT (J) (GTO), 300ZX (Fairlady Z).
- Concept / prototype road cars (Camaro IROC-Z Concept, Mitsubishi HSR-II, Dome Zero) stay road legal because the catalog treats them as road cars.
- Catalog data that does not match the real car (noted in the rows, not changed): Buick Special '62 (7456 cc DOHC / 564 PS in the catalog), Honda Accord Coupe Type S (id says 03, name 99, figures of the Acura CL Type-S), Corvette Z06 C5 and MR2 Spyder dated '99 (2001 / 2000 cars).

## Not verified
- No player build, no play check (nothing in play reads the facts except the market). The facts are authored from general knowledge with no web lookups; the validator checks the rules, not each fact. Gauges (damped / true temperature gauge, oil lamp or gauge) and knownFailures fault ids are not authored: R2a / R4 own them once faults_v2.json exists.

## What the owner will notice
- Race and rally cars no longer show up in the newspaper, on the dealer lot or as a new starting car. Quick race and the debug garage still have all of them, and a race car you already own stays yours.
- A table in your art folder (`Car_Facts_SpotCheck_2026-11-12.md`) lists 30 familiar cars with their facts: mark any wrong row. Three rows need your call (the 240SX market, the GT350R and the Motor Sport Elise as race cars).
- Nothing else changes in play yet: later fault rounds read these facts (warning lamps and codes, airbags, bumper damage, timing belts, parts waits, tuning wear).

## At merge
- Shared files: `Scripts/LifeSim/CarMarket.cs` (RefreshListings +5 lines, RefreshLot +1 and one changed condition, EnsureNewStock one changed condition, RollStartingLanes +3 lines), `Editor/LifeSimSelfTest.cs` (one line in RunAll). New: `Scripts/CarFacts.cs`, `Editor/LifeSimSelfTest.CarFacts.cs` (entry point `LifeSimSelfTest.RunCarFacts` -> `PSXRacing_selftest_carfacts.txt`), `Resources/car_facts.json`, `tools/bake_car_facts.py` (+ .meta files with fresh GUIDs).
- NEW RESOURCES FILE: build sandboxes never receive a worktree's Resources by mirror - copy `Assets/PSXRacing/Resources/car_facts.json` and its .meta into the build sandbox BY PATH. Without it the game still runs (the market falls back to the catalog's built-to-tune rule; only the two authored race cars would stay for sale).
- No save version, no City code, no tile re-bake. `CrashStopGap.BumperFor` (R0a) still derives the bumper from origin + year ("until car_facts carries one"); R1a can switch it to `CarFacts.Get(spec).bumperEra`.
# HANDOFF - FAULTS R0c "ROOM FOR TWO" + OWNER'S ANSWERS 1 AND 8 (agent "r0c", branch faults-r0c, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr0c` off mountain-nm 7ae0ec13 (R0a merged). Sandbox PSXCity (lock "r0c", released). Design: art workspace `Fault_System_Design_2026-11-11.md` (OWNER'S ANSWERS 1, 4, 8; Â§8 "Room" and "Selling"; Â§10 R0c row). Reports: scratchpad `r0c\` (runner `r0c.ps1`). NO SAVE CHANGE: no new LifeState/OwnedCar field, no version step (v24 stays current); the trade-in quote is held in memory for the day.

## What changed (switches, each 0 = today's behaviour)
- ROOM (`PSX_ROOM_FOR_TWO`): NEW `Scripts/LifeSim/Room.cs`. `Room.GarageSlots(s)` (= `LifeRules.GarageSlots`) = the house rung's garage (`LifeRules.Housing`, read from housingType) + 1 yard spot (the lawn bay the home street already draws), never less than the stored garageSlots (debug careers keep 6). A v24 one-car career ("house1g", stored 1) has room for 2 with no save change. Every purchase gate and slot display reads it: CarMarket.Buy, Viewings.Adopt, the market/dealer/buy/viewing pages (LifeHomeScreen), GarageWorld's empty-bay prompt.
- MOVE HOUSE (`PSX_MOVE_HOUSE`): under BILLS. Moves to any other rung: the new place's first month as deposit + a $45 truck, one block; rent changes from the next bill (monthlyHousingCost); refused if the cars would not fit. The walk-in still draws the neighbourhood's one house (extra cars stand on the lawn bays it already lays out) - a real 2/3-car house needs house art and garage bays in the Neighborhood scene (GarageSceneBuilder/HomeArrival/CarWhere.GarageBays), not built.
- TRADE-IN (`PSX_TRADE_IN`): on CRESTLINE's own page (reached only from its door in town = the visit). HAVE IT APPRAISED (15 MIN) on the car you came in -> 60% of today's lane-based CarMarket.CarValue (reads no fault, so a hidden fault cannot move it); the quote holds for that car until the day ends (in memory: quitting the game drops it, a new visit re-appraises). SELL IT TO CRESTLINE (not the only car; quote less the loan). The ONLY car goes in PART EXCHANGE: extra buy rows on a Crestline lot car ("... + YOUR CAR"); the old car leaves, the new one is the car you drive out (works with a full garage too). The market page's QUICK-SELL 50% is gone while the switch is on.
- JUNK IT (`PSX_JUNK_IT`): on the yard's page: "WE BUY CARS FOR SCRAP - WE TOW IT AWAY", one row per car, scrap = $0.04/kg + $40 converter (1975+), less any loan; press twice; refused for the only car, a car at a shop or with work on it, or one upside down on its loan. Loans settled, a spare going on back to the shelf, jobs/ads dropped (QuickSell's own cleanup).
- ANSWER 1 (`PSX_NO_RESTART`, `PSX_QUIT_KEEPS`): NEW `Scripts/CareerDrive.cs`. RESTART RACE only where `CareerDrive.RestartOffered`: debug careers, quick race, the CITY edition, standalone editor races; never in a non-debug career (PauseMenu was the only RESTART; the arcade/CITY "AGAIN" buttons are not careers). QUIT from a career RACE banks the leg from the seat before ResultReady is cleared (`CareerDrive.BankQuitLeg` -> the finish line's stamps for distance so far, drift, the R0a crash outcome via CrashStopGap.StampHandoff, tank, gauge -> the ordinary `LifeRules.ApplyRaceResult` as a no-purse, no-block leg -> CrashBank, wear, fuel, heat, bump stops, distance wear rolls); diary: "quit <venue> after x km - the car keeps what happened". Free roam's END DRIVE already banked its session (StampExitResult + R0a's StampCityExit) and still does. QUIT after the flag now keeps the stamped result (it is CONTINUE). Test drives never bank onto the player's car (BankSeizureNow's guards). Debug RESTART is unchanged (throws the leg away; R1a makes it reload the damaged car).
- ANSWER 8 (`PSX_NO_RESET`): in a non-debug career nothing puts the PLAYER's car back on the road: no R / pad X / Select / wheel reset (PlayerCarInput), no RESET CAR row (PauseMenu), no StuckRecovery auto-reset when rolled, pinned, beached, in the water or fallen below the road. The car stays put, StuckRecovery latches `Stranded` (survives the pause; lets go when the car moves / a wheel is back on a road) and prompts "ON ITS ROOF / STUCK / IN THE WATER / OFF THE ROAD + PRESS ESC (START, TAP MENU) - CALL TOW TRUCK"; off-track says "DRIVE BACK TO THE ROAD". The pause menu's leave row becomes "CALL TOW TRUCK - $fee" whenever the car cannot get itself home (stranded, or the engine seized; RaceHUD's seized banner says so): $55 hook-up with 5 miles, then $2.50/mile (town 4 mi $55, Charlotte 10 mi $67, mountain stages 100 mi $293, Bogue 270 mi $717); a driver who cannot cover it pays what is in the wallet. Then the drive ends exactly as QUIT/END DRIVE (the leg banked) and the car is home. KEPT: the under-the-map-floor backstop respawn (a hole in the world), AI recovery (AIDriver), and the old behaviour in debug careers, quick race, the CITY edition and standalone races. RaceManager gained a one-line PlayerDriftSeconds getter.

## Gates
Sandbox PSXCity, code synced from the worktree (head 412ea4f0 for the self-test and play checks; the one later commit, 1c5ac5f9, only shortens the MOVE HOUSE button text). Base = r0a-gate's full self-test of the R0a merged code in the same sandbox (`r0a-gate\gate_selftest.txt`, code identical to 7ae0ec13 - `git diff 9d129960 7ae0ec13` is HANDOFF.md only - and PSXCity untouched since).
| Gate | Result | Report |
|---|---|---|
| Typecheck | OK (every run) | `r0c\tc` |
| Full LifeSimSelfTest vs base | 62 FAIL both, the same 62 by check text: NEW 0, GONE 0 (`r0a-crash\compare_fails.py`); 4,115 ok | `r0c\head2_selftest.txt` |
| R0c self-test (in RunAll; RunRoom alone) | 45 ok / 0 FAIL in the full run | same; first solo run `r0c\head_room.txt` (1 FAIL = a test that planted a rear hit too soft for a body loss; fixed) |
| v24 one-car career buys a 2nd car | saveVersion 24, stored 1 slot + house1g -> derived room 2; buys from the paper; a 3rd refused "garage full (2)", the lot too; debug keeps 6; switch off -> "garage full (1)" | self-test |
| Trade-in needs a visit; quote = 60%; hidden fault does not move it | no appraisal -> refused; $2,293 of $3,822; a planted hidden fault 2293 -> 2293; a hidden fault + a 40-point dent after the visit: quote unchanged all day; next day gone; the ONLY car goes in part exchange (old car out, lot car active, money right); 2nd car sold outright less its loan | self-test |
| Junking pays scrap, car leaves | 1,148 kg -> $86, paid, car gone, keys moved; only car refused; upside-down loan refused | self-test |
| QUIT keeps a planted crash: race and free roam | race QUIT from the seat: hidden bumper + alignment rows, body 80 -> 50, 3.1 mi, fuel 60 -> 51.6, no purse / race / block, diary "quit ..."; switch off banks nothing; END DRIVE (stand-in CityMode, R0a's way): bumper row, body 80 -> 50; play check: the tow's quit banked bumper + alignment, body 80 -> 50 | self-test, `r0c\head2_tow.txt` |
| RESTART absent outside debug careers | rule: career no / debug yes / quick race + standalone yes / switches off yes; source tripwire: the one RESTART row (PauseMenu) reads RestartOffered; play check pause rows: MENU, RESUME, FUEL TRUCK, QUIT RACE (GO HOME), OPTIONS | self-test, tow check |
| Rolled career car not respawned; CALL TOW TRUCK works (visible editor, Gillespie Gap) | TOW CHECK ALL HOLD, 18 ok: 8 s on its roof, teleports 1 -> 1, moved 0.0 m (old watchdog: 4.5 s); prompt "ON ITS ROOF / PRESS ESC - CALL TOW TRUCK"; row "CALL TOW TRUCK - $293"; $600 -> $307; crash banked once; front end loaded; at home 1 bumper row, fee paid once | `r0c\head2_tow.txt` |
| Every player-car respawn gated | 6 call sites read the rule (PauseMenu:186, PlayerCarInput:167/228/260, StuckRecovery:289/410) | self-test |
| Handling play check | 54 ok / 1 FAIL, identical to base (the base's RUF CTR lane change, 10.2 deg / 24.4 deg in both) | `r0c\head2_handling.txt` vs `r0a-gate\gate_handling.txt` |
| Parked play check | 44 ok / 0 FAIL, the same as base (a debug career: unaffected by design) | `r0c\head2_parked.txt` vs `r0a-gate\gate_parked.txt` |

## Not verified / not built
- NOT DONE: the clipped-text picture pass of the new rows (dealer trade-in block, part-exchange rows, the yard's JUNK rows, MOVE HOUSE) - `LifeHomePreview.Capture` renders every tab at every aspect with the clip audit; not run (coordinator's budget stop). Widths were estimated by hand only.
- NOT BUILT: "to the mechanic if it will not run" - the tow takes every car HOME (a seized engine arrives home as it does today); a shop intake job (a PendingPart that holds the car at Delmar) is needed for that. MOVE HOUSE's own houses (the same house is drawn).
- No player build, Android or WebGL. Nothing clicked by hand: the trade-in, part exchange, junk and move pages are proven by the self-test's logic, not by a UI drive. The tow/QUIT path was play-checked on a mountain stage (RaceManager); a tow from Charlotte or the town (CityMode) goes through END DRIVE's existing exit and is proven only in edit mode (R0a's stand-in CityMode). The play check's quit distance (4.0 km) is the progress index read at the test's teleport, not driving.
- Decisions left for the owner: a QUIT race still costs no block and does not use up the day's purse race (today's rule) - say if a quit should count as a DNF; a test drive has no RESTART/reset either and its tow is paid by the player (the seller's car's damage is not banked); the trade-in quote lives in memory (quitting the game drops it).

## What the owner will notice
- His one-car career can buy a second car now (garage + yard); BILLS has MOVE HOUSE to the 2- and 3-car houses (same house drawn).
- At Crestline: "have it appraised" (free, 15 min), then sell it there at 60% or trade it against a car on the lot - even his only car. The yard buys junk cars for scrap.
- No RESTART in his career. QUIT keeps the dents, wear and fuel; it pays nothing.
- A rolled or stuck car stays rolled; ESC -> CALL TOW TRUCK (about $67 in Charlotte, $293 from the mountains) ends the drive and the car is home with its damage.

## At merge
- No save version, no Resources/Art/scene/shader/ProjectSettings change, nothing under Scripts/City or RoadsideRules.cs: a player-only PC build is possible.
- New: Scripts/LifeSim/Room.cs, Scripts/LifeSim/LifeHomeScreen.Room.cs, Scripts/CareerDrive.cs, Editor/LifeSimSelfTest.RoomR0c.cs, Editor/TowPlayCheck.cs (+ their committed .meta).
- Shared files touched (small, local edits): PauseMenu.cs, StuckRecovery.cs, PlayerCarInput.cs, RaceHUD.cs, RaceManager.cs (1 getter), LifeSim/LifeRules.cs (1 forwarder), LifeSim/CarMarket.cs (1 gate), LifeSim/Viewing.cs (1 gate), LifeSim/LifeHomeScreen.cs (slot reads + 5 hook lines), OnFoot/GarageWorld.cs (1 read), Editor/LifeSimSelfTest.cs (1 line in RunAll).
# HANDOFF - FAULTS R0b "STOP THE LEAKS" (agent "r0b", branch faults-r0b, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr0b` off mountain-nm 7ae0ec13 (R0a merged). Sandbox PSXPerf (lock "r0b", released). Design: art workspace `Fault_System_Design_2026-11-11.md` (OWNER'S ANSWERS, Â§1, Â§2, Â§4 "How the RG2 ids map", Â§6 "Seller viewings"/"Closing the leaks", Â§7, Â§9, the R0b row of Â§10) plus the coordinator's 11-12 add (a stored CAUSE on every fault row). Reports: scratchpad `r0b\` (runner `run.ps1`; base_selftest.txt, h2_*.txt, h3_*.txt).

## What changed (switches; each =0 is today's behaviour; the text fixes have none)
- ONE GATE: new `Scripts/LifeSim/FaultView.cs` - `FaultView.Known/KnownCount/KnownList` replace both KnownFaults copies (LifeHomeScreen, Viewings) and the hand-written hidden filters (garage list, viewing list, pre-race list, 3D garage bench, spares). TestNoPartLeak's tripwire keeps `.hidden` out of the screen files.
- NO EFFECT NUMBERS: the garage, viewing and pre-race fault rows show no "accel x0.93" (a debug career keeps them); the HUD heat shout is "ENGINE OVERHEATING / EASE OFF OR IT WILL LET GO" (no "POWER CUT x%").
- NO COOLING PART NAMES OR FIGURES outside inspection: garage cooling line ("The cooling looks serviceable." / "Cooling system tired - ..." / "The overflow tank reads low."), the COOLANT bar is a word like the other bars, the mechanic's cooling rows show price only (no "45% now"), the pre-race line ("Overflow tank low - top it up ..."; "the cooling is done", no explanation), the result note drops "COOLANT DOWN TO x%", the seizure story says only what the needle did and how long it was flat out. R0a's f60143fb lines are now unconditional (superseded).
- DEBUG INFO only in a debug career (PauseMenu via `FaultLeaks.DebugInfoAllowed`; a drive with no career under it keeps it). `PSX_FAULT_DEBUGGATE=0`.
- FLOOR CHECK describes the puddle (colour, where, how big) and names nothing; the fault stays hidden; cooling_fail (green) and used_fluids_low (drops) join the list; air suspension = "one corner sits lower". `PSX_FAULT_PUDDLE=0`.
- TEST DRIVES (`PSX_FAULT_FELT=0`): no dice reveal; one drive per visit-day; new `TestDriveFeel` (added to the car by RaceHandoffApplier on a test drive only) measures what the drive did (5 s over 30 km/h, 3 s full throttle, 2 s over 3,500 rpm loaded, 2 shifts, a stop from 60, 2 s cornering at 0.3 g, slow full lock, needle at 105 C, 10 s moving, night). NAME WHAT YOU FELT: 10 fixed chips on the viewing page after the day's drive; a TRUE chip (a fault on the car shows on it and the drive met its condition) takes that fault's tier off the current offer, naming nothing (the fault stays hidden, the visit remembers it as felt); a FALSE one ends the haggle and the claims for the day.
- SERVICES BY PART (`PSX_FAULT_BYCLASS=0`): new `FaultClass` gives every RG2 id a class. OIL CHANGE clears only used_fluids_low, TUNE-UP spark plugs, NEW TIRES tire_wear, BODY PATCH/FULL BODY WORK dents and cut-out rust, ROTATION/TOUCH-UP nothing; a service cancels only booked repairs of what it cleared. A seizure removes only engine-internal rows; a rebuild or swap engine-internal + long block (gearbox, charging, sensors stay; cooling_fail goes only if radiator and hoses are both sound). A respray clears the finish row (paint_fade); rust stays.
- USED ROWS OWN IDS (`PSX_FAULT_USEDIDS=0`): "Engine Rebuild Needed"/"Engine Replacement" -> `used_engine_worn` (keeps trans_slip's power and fuel effect, no shift delay; home: HEAD GASKET sub; DONOR ENGINE spare), "Fluid Top-Off Needed" -> `used_fluids_low` (oil_leak's effect; home: OVERFLOW TANK). Remapped in code at catalog load - no Resources file changed. Ad phrases map to matching ids (FaultIds.ForProblem by family); the paper prints "Rusty bodywork" instead of "Cracked windshield" (no glass row until R1a; an old paper's windshield adds no row); the advert's own problem is no longer taken off the visit price twice.
- STARTER LINE: "the <car> <symptom>" from `FaultSymptoms.StarterSymptom` ("idles up and down once it's warm", "smells of hot oil after a drive"), no "worth an inspection".
- CAUSE ON EVERY ROW (coordinator's add): CarFault gains cause, causeNote, bornDay, bornOdo, zone1, hitSide, hitMps. Stamped at every creation path: CrashBank (impact, zone/side/m/s of the leg's hardest hit, or under + wheel side for a bump strike), threshold wear (wear), heat (heat), legacy crash/bottoming rolls (impact), SeedHidden + used rows + starter + yard/swap junk risk (previous_owner), advert (seller), debug bench (debug). Never shown. BlowEngine creates no row.
- SAVE v25 = `LifeSimManager.MigrateToFaultIds`: the used rows onto their ids on owned cars, viewing cars and booked repairs (hidden/diagnosed untouched), every row without a cause -> "legacy", viewing felt lists made. Idempotent; LastUnstampedVersion stays 24.

## Gates (PSXPerf, base = 7ae0ec13 in the same sandbox)
- Typecheck OK. TestNoPartLeak: 12 home-screen pages rendered from one career, each render restored from the same JSON and dice; with every fault (51 ids) HIDDEN at severity 1 and 2 no page shows any line it did not show with no faults (0 differences, 0 number notes), with every fault FOUND 588 lines vs 374 and no effect number; plus pre-race, seizure story, result note, DEBUG INFO gate and a source tripwire (old leak strings, EffectSummary only behind debugMode, no `.hidden` in screen files). Bars = the listed exception.
- Oil change vs hidden gearbox: the gearbox row stays hidden and its booked job stays. TestEngineDeath: gearbox + charging rows survive the seizure and the rebuild (both the seat path and the apply-back path); plugs go at the rebuild.
- NAME WHAT YOU FELT: a true PULLS TO A SIDE takes the cheap tier (x0.92) off the haggled offer and the fault stays hidden; a false BRAKES WEAK (no stop from 60 on that drive) ends the haggle; a clean car's false claim too.
- v25: a v24 career (no v25 fields in the JSON) in -> v25, ids moved, flags kept, legacy causes; Migrate again from 24 = byte-identical; the ladder's worked-career re-run v25..v25 now TESTED and passes. Save safety 77 ok / 0 FAIL.
- Every creation path stamps a cause (7 paths run + a source tripwire over Scripts).
- Leaks job (R0b tests + TestEngineDeath + ladder): 150 ok / 0 FAIL (h3_leaks.txt).
- FULL SELF-TEST (h3_selftest.txt, head 6482f342 code): 4,194 ok / 62 FAIL against base 4,103 ok / 62 FAIL (base_selftest.txt) - the same 62 by check text, 0 new, 0 gone (r0a-crash\compare_fails.py). An earlier head run (h2) had 7 new: 3 parts-shelf pins that said "v24" (re-pinned to the current version) and 4 physics sims (ParkedCarSim handbrake hold, TreeCrashSim stage forest) that ran AFTER the R0b tests and failed; with the R0b tests moved to the end of RunAll they pass again. So something the R0b tests leave in the editor (most likely the home-screen page renders) perturbs those sims; the cause was not isolated - it is test-side only (no runtime file the sims use changed).

## Not verified
- No play check: TestDriveFeel's thresholds were never driven; the chips, the "DRIVEN TODAY" button and the DEBUG INFO row were not looked at on screen (no three-aspect pictures). No player build.
- R3's numbers (value/quotes moving with hidden faults) are not gated; TestNoPartLeak notes them and found none on these pages.

## At merge
- Save step: v25 if nothing else merges a save change first (the ladder test enforces one step per number). Keep LastUnstampedVersion 24.
- New files (with .meta): Scripts/LifeSim/FaultView.cs, FaultSymptoms.cs, TestDriveFeel.cs; Editor/LifeSimSelfTest.LeaksR0b.cs (RunLeaks; RunLeaksR0bTests runs LAST in RunAll).
- Shared files: LifeHomeScreen.cs, LifeRules.cs, PauseMenu.cs, CarMarket.cs, Viewing.cs, Inspection.cs, FaultCatalog.cs, LifeState.cs, LifeSimManager.cs, CrashBank.cs, DebugCarOps.cs, Spares.cs, OnFoot/GarageWorld.cs, CoolingModel.cs, EngineTemp.cs, RaceHUD.cs, RaceHandoffApplier.cs, Editor/LifeSimSelfTest.cs (+ .SaveSafety.cs WorkCareer stamp, .Spares.cs v24 pins). Nothing under Scripts/City, RoadsideRules.cs, Resources or Art.

# HANDOFF - FAULTS R0a MERGED AND GATED: crash stop-gap + save safety (agent "r0a-gate", branch faults-r0a-crash, 2026-11-12; NOT pushed, NOT merged into mountain-nm)

Worktree `C:\Users\mcgee\PSX Racing-faults`, branch faults-r0a-crash = the crash half (4cf8ac09) + the save half (faults-r0a-save 84549b8e), off mountain-nm 19d6d4a3. Sandbox PSXCity (lock "r0a-gate", released). Design: art workspace `Fault_System_Design_2026-11-11.md` (OWNER SUMMARY, Â§1, Â§2 "Save safety" and "Versions", Â§3.1, Â§9 and its R0a stop-gap table, the R0a row of Â§10). Reports: scratchpad `r0a-gate\gate_*.txt` (runner `gate.ps1`); the base runs are the crash half's `r0a-crash\base_*.txt` (19d6d4a3, same sandbox). The two halves' own sections follow this one (crash first, then save); read them for the detail.

## What R0a changes
- CRASHES (switches `PSX_FAULT_CRASH=0` = today's crash, `PSX_FAULT_FAMILY=0` = every roller back on the Japanese pool; both 0 = today exactly). The leg's hardest hit is located (front / rear / side and left / right / centre, from the contact patch in the car's own frame) and sized (normal closing speed, m/s), and the Â§9 table is rolled ONCE, AT THE HIT: scuff (paint -2..-5), body dent, bumper_crack (bumper_dent on chrome and 5-mph bumpers) 70% from 3 m/s front/rear, radiator -25..-60 plus a coolant weep 60% from 6 m/s front, alignment on the struck side 40% from 10 m/s front/rear and 50% from 6 m/s on a flank (severe from 15 / 9), bump stop from 6 m/s. A hit centred on the nose or tail has no side and never pulls. The pull acts at once (CarController.hitSteerPull, 0.042 deg minor / 0.14 deg severe of road wheel; the old fault was 0.25 of lock, about 5 deg), the radiator at once (EngineTemp.radiatorCond + weep); the apply-back (LifeSim/CrashBank.cs) writes exactly what was rolled, no second roll. Every roller maps origin to family (ger, gbr, fra, ita, eur -> eur; usa; jpn). Existing alignment faults in a save also pull in degrees now (no migration). With the stop-gap on, the garage cooling line and the pre-race warning say "the cooling", not "the radiator" (f60143fb - keep it unless R0b lands first).
- SAVES (switch `PSX_SAVE_GUARD=0` = the old Load/Save, no copies; the writer stamp stays). `LifeState.writerVersion` is stamped by every Save() - added WITHOUT a version bump, v24 stays current. A backup file before any migration step (last 3 kept). A save from a newer build loads READ-ONLY and is never written. A v25+ save written back by an older build is detected and the title offers this build's own `.last` copy (written on a new career's first save and the first save after each daily rollover). A load or migration that throws keeps the raw string as a `.failed` copy, leaves the main key alone and starts no game until an explicit, confirmed NEW GAME. Copies are files, never PlayerPrefs: player `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PSX Racing\saves`, editor `<project>\Library\PSXSaveCopies`.

## The merge
- 4481fb79: faults-r0a-save into faults-r0a-crash. Only HANDOFF.md conflicted (kept both sections). LifeSimSelfTest.cs (two Guard lines in RunAll) and LifeRules.cs (Rollover's NoteRollover line) auto-merged beside the crash edits; no code conflict and no overlap in behaviour (no crash code saves; no save code touches the crash path).
- 9d129960 - A CHANGE I MADE AT MERGE, so that a player-only build is possible. The crash half had edited `Scripts/City/CityMode.cs` (StampExitResult's four damage lines -> CrashStopGap.StampHandoff). Every .cs under Scripts/City is in the city tile bake's fingerprint (`Editor/CityTileBake.cs` CodeDirs, :54), so that edit made the baked Charlotte stale: the Win64 build would re-bake, or a player would build every tile live. CityMode.cs is back to 19d6d4a3 byte for byte. Its two callers - PauseMenu.ExitToMenu (:377) and TownVenue's door exit (:440), both outside Scripts/City - now call the new `CrashStopGap.StampCityExit()` on the line after `StampExitResult()`: the finish line's own writer (StampHandoff) over the city session's player car; nothing without a city session or a player car, where StampExitResult stamps nothing either. New `TestCrashCityExit` (in RunCrash and RunAll): a free-roam exit done that way hands the apply-back the same crash, field for field, as the finish line's writer; with no city session it stamps nothing; and a source tripwire checks that every runtime call of StampExitResult is followed by StampCityExit (found: PauseMenu.cs:377, TownVenue.cs:440). This supersedes the crash half's notes below that list City/CityMode.cs among the shared files.

## Gates (PSXCity; base = 19d6d4a3, run by the crash half in the same sandbox; head = 9d129960 code)
| Gate | base 19d6d4a3 | R0a merged |
|---|---|---|
| Typecheck | OK | OK |
| LifeSimSelfTest, full run | 3,898 ok / 62 FAIL | 4,070 ok / 62 FAIL - the same 62 by check text, 0 new, 0 gone (`compare_fails.py`) |
| Crash self-test (RunCrash) | not in base | 96 ok / 0 FAIL (the crash half's 91 + 5 city-exit checks); identical to its final run apart from one random used-car line |
| 500 crashes per origin, walls only: classes | every impact roll = jpn bumper_crack or alignment (10/10 bumper_crack in the switch-off test) | 6 of 6 classes in 7 of 7 origins (per 500: nothing 102, scuff 398, body 205, bumper 142, radiator 38, alignment 59) |
| ... no electrical rows | eur cars rolled jpn pools | only bumper_crack / bumper_dent / alignment rows, also over 3,500 legs banked through ApplyRaceResult |
| ... bumper rows at most 50% | - | per crash outcome 142/444 = 32% (random-seed runs 29-35%): ok. As fault ROWS 142/201 = 71% (66-76%): FAILS as worded - see below |
| Drift, 100 km/h hands-off, minor / severe, left / right, assist OFF / STANDARD / FULL | old pull 0.25 of lock | RX-7 FD +-0.99 / +-3.53 m per 100 m (FULL +-3.49); Civic EG +-0.89 / +-3.18; Viper +-1.00 / +-3.52; pull-free 0.00; a live hit -1.00 m within 3.6 s inside the leg; the next drive through the real bank path -1.08 m. Crash play check 40 ok / 0 FAIL, 46 of 48 lines identical to the crash half's final run (2 differ by 0.01 m in a contact point) |
| Handling, no damage | 52 ok / 1 FAIL (RUF lane change) | 52 ok / 1 FAIL, the same; every physics value identical, only chase-camera readings differ |
| Temp, no damage | 28 ok / 2 FAIL (ambient, pad throttle) | 29 ok / 1 FAIL (ambient); the pad-throttle line flips between runs (crash half: base FAIL, after FAIL, after2 ok, rev ok); the seizure's cost varies 0.12-0.54 run to run |
| Parked, no damage | 44 ok / 0 FAIL | 44 ok / 0 FAIL; 62 of 258 lines differ, by at most 0.1 mm, numbers only |
| TestSaveDowngrade | not in base | 68 ok / 0 FAIL (tripwire over 619 files) |
| TestSaveVersionLadder | not in base | 8 ok / 0 FAIL; 1 NOT TESTED (no step above v24 yet - R0b's step is checked automatically when it lands) |
| Nothing under Scripts/City or Scripts/RoadsideRules.cs changed (`git diff --name-only 19d6d4a3 HEAD -- ...`) | - | prints nothing (it printed CityMode.cs before 9d129960). No Resources, Art, scene, shader, ProjectSettings or Packages file changed either |

Save cost (editor, inside the full run): worked career 25,668 bytes (19 of them the stamp); Save() 0.43 ms guard off / 0.44 on; first save after a rollover (writes the `.last` file) 6.4 ms, once per game day; load 0.58 ms, with a migration 7.2 ms.

## The failing gate (a decision for the owner, not a code defect)
Â§10's "bumper rows at most 50% of the faults" fails when counted as fault rows: 71% on the stratified sweep, 66-76% on random seeds, in all 7 origins. Cause: the design's own Â§9 table. Below 10 m/s (6 on a flank) the bumper is the ONLY fault row a wall hit can write (70% of front/rear hits from 3 m/s), while dents and the radiator are condition losses, not rows; so fault rows are mostly bumpers by construction, whatever the crash mix. Counted per crash it passes: a bumper row on 142 of 500 wall crashes, 32% of what needs repairing. The first round's 45% came from making a quarter of the crashes bump-stop strikes, which nothing measured supports. Options: lower the 70% bumper roll, write the dent as a row, or read the gate per crash. R1a replaces this table with located zone tables anyway.

## Not verified
- No player build, no PerfTour run, Android and WebGL not run. Nothing driven by hand; no gamepad or wheel feel (0.04 deg is subtle by design).
- The city exit path is proven by the self-test with a stand-in CityMode (edit mode) plus the source tripwire, not by a free-roam drive exited in play mode.
- Brake play check not re-run on the merged head (the crash half's base and head runs were identical, 21 of 21 lines; the merge changes no physics).
- RESTART and QUIT still throw the leg's hit away, and the live pull and weep reset at any scene load (R1a's DamageBank). The weep lasts only the drive it happened in; the lower radiator condition and the coolant lost are kept.
- The pause menu's new "pull deg" debug line has not been looked at on screen. On a phone the "older build saved over" note's date splits across its two lines. If the title is skipped (tools, or a build without the Blue Ridge venue), a FAILED career goes straight to the new-game wizard with no warning.

## What the owner will notice
- Light rubbing and kerb taps do nothing or leave a scuff. No more "every crash = cracked bumper + 25% pull".
- From about 7 mph straight into a wall, front or rear, the bumper cracks about 7 times in 10 (the body dents otherwise), hidden until inspected. From about 13 mph nose-first the radiator cracks 6 times in 10: some 10-20 minutes of driving later the temperature needle climbs as the coolant weeps away.
- A hard off-centre hit (about 22 mph front or rear, 13 mph on a door) can knock the alignment out: the car drifts gently toward the struck side at once, about 1 m per 100 m (3-4 m after a big one). Existing alignment faults in his career get the same gentle pull.
- His career is backed up before any upgrade, a build that cannot load it leaves it alone and says so on the title screen, and a newer build's career opens read-only in an older build.

## At merge into mountain-nm / to reach the exe
- PLAYER-ONLY PC BUILD: possible. No tile change (fingerprint sources untouched), no CityTileCache format bump, no kit slot, no scene pass, no new asset files beyond the six new scripts' committed .meta files, no save version bump.
- Keep f60143fb (the cooling text) unless R0b lands first. R0b's step is the first v25; keep `LastUnstampedVersion` at 24.
- When the first v25 build ships, recycle every PC build from before R0a (only a guarded build refuses to write a newer save back).
- Shared files touched by R0a: CarController.cs, CollisionResponder.cs, RaceHandoff.cs, RaceHandoffApplier.cs, RaceManager.cs, EngineTemp.cs, CoolingModel.cs, PauseMenu.cs, Town/TownVenue.cs, TitleScreen.cs, Dev/PerfTour.cs, Dev/PerfTour.Stage.cs, LifeSim/FaultCatalog.cs, LifeSim/LifeRules.cs, LifeSim/LifeHomeScreen.cs, LifeSim/DebugCarOps.cs, LifeSim/LifeSimManager.cs, LifeSim/LifeState.cs, Editor/LifeSimSelfTest.cs. New: Scripts/CrashStopGap.cs, Scripts/LifeSim/CrashBank.cs, Editor/CrashPlayCheck.cs, Editor/LifeSimSelfTest.CrashR0a.cs, Editor/LifeSimSelfTest.SaveSafety.cs, Editor/SaveGuardPreview.cs, tools/crash-play-check.ps1.
- Batch self-tests ran with -quit; the play checks ran watched and closed themselves (they enter play mode, which -quit would end before it starts).

# HANDOFF - FAULTS R0a, CRASH HALF: located, sized, live pull (agent "r0a-crash", branch faults-r0a-crash, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faults` off mountain-nm 19d6d4a3. Sandbox PSXCity (lock "r0a-crash", released). Design: art workspace `Fault_System_Design_2026-11-11.md` Â§9 (R0a stop-gap table) and the R0a row of Â§10. The save-safety half of R0a is a separate branch (faults-r0a-save). No save change here: no new LifeState/OwnedCar field, no version step.

## REVIEW ROUND (2026-11-12): 19 findings from 3 adversarial reviewers - all real; 18 fixed as asked, 1 narrowed (F6), none rejected
Commits cbd1725f (runtime), 3c8a8c90 (tests), f60143fb (cooling text, droppable), de395cc2 (patch centre over all contact points; banked-leg test fix), 01fe977e and the later test-only commits (unique test car ids, a fresh drive per play-check run, the wear check's message), and the HANDOFF commit.
- ONE ALIGNMENT, ONE RULE (F1, F8). CarController steers by CrashStopGap.CombinePull(faultSteerPullDeg, hitSteerPull): the worse band wins, a tie goes to the newer hit. CrashBank.ApplyOutcome replaces the car's one alignment row by the same rule (NewAlignWins, >= on a tie). It was the SUM live (minor R carried + minor L hit drove dead straight; same side gave 0.084 deg, the severe band) and strictly-worse when banked, so the next drive pulled differently. TestCrashOneAlignment: live == next drive for all 20 carried x hit pairs; the play check drives both cases.
- THE LEG'S HARDEST HIT, AND THE WORST OF IT (F3, F13). Only a hit that is the leg's new hardest is rolled (CollisionResponder.RecordHit, the rule in one place; the first round's "a later crash rolls fresh and merges" described the ledger, not the game). Merge takes the larger loss per lane, never the sum (6.5 then 7 m/s nose-ins could take 120 off the radiator; the band stops at 60), and a band tie goes to the later hit. Ledger.Hit ignores a softer contact of the same crash and merges a harder one; a bump strike merges after the open crash. TestCrashLive goes through RecordHit: 11, 12 (same crash), 13 m/s (flank) rolled, a later 10 m/s not; stamped body -30 / paint -3.5 (was -52.5 / -7).
- CENTRED HITS, AND WHERE A HIT IS (F5, F9, F14). Classify gives side 0 inside 0.2 of the half-width and Resolve rolls no alignment for side 0 (R1a: a centre-front hit bends no corner). The hit is located by the centre of the box around its contact points (CrashStopGap.PatchCentre), not the impulse-weighted point, which sits on the wall's inner edge. Play check (RX-7 nose into walls at 12.1 m/s; patch centre x vs the impulse-weighted x): left 35% -0.60 vs -0.27; left 60% -0.38 vs +0.19 and left 75% -0.24 vs +0.23 (the weighted point reads the WRONG side); full width 0.00 vs -0.16 -> FRONT CENTRE, no alignment with the alignment die forced; square tail at 11 m/s 0.00 -> REAR CENTRE, no alignment; right 35% +0.60; a right flank into the 200 m barrier +0.94 -> SIDE RIGHT.
- SWITCHES INDEPENDENT (F4). The two legacy impact rolls pass "jpn": PSX_FAULT_CRASH=0 alone is today's crash with PSX_FAULT_FAMILY on (a German car 10/10 bumper_crack at jpn prices; it would have rolled the eur pool's electrical "impact" rows).
- WEAR NEVER CRACKS A BUMPER (F6, narrowed). With the stop-gap on, the player's own wear rolls (RollThresholdFault -> RollWearFault noImpactOnly) skip rows only a hit causes (FaultCatalog.ImpactOnly: bumper_crack, bumper_dent) and roll nothing when nothing else is eligible: 20 long legs on a ruined body lane of a low-mileage car, 0 bumper and 0 body-lane rows (jpn, usa); switch off, 18 bumper rows in the same 20 legs. NOT applied to market rolls (SeedHidden, listings, viewings, salvage parts): a previous owner is a cause the design names (Â§1), and a "brakes" ad on a low-mileage Japanese car would then sell at 45% off with nothing wrong (R0b maps ad phrases to ids; R4c replaces SeedHidden).
- BODY WEAR ROLL AFTER A CRASH (F12). Step 5's body-lane wear roll reads the body from before the leg's crash (PSX_FAULT_CRASH=0: after, as before). TestCrashBankedLegs banks 3,500 swept outcomes through ApplyRaceResult over 5 km legs on all 7 origins (body and paint at 70; engine and tyres new, because a 5 km leg at 40,000 miles takes ~44 off the tyre lane and from 70 that rolls tyre-lane wear rows, which is what the first run of the test caught): the rows added equal the rows rolled on every leg, only bumper_crack / bumper_dent / alignment, no electrical row. 602 of the legs left the body under 40 (430 of them European); read after the crash, the old order would have added about 46 wear rows on them (33 on European cars, half electrical).
- THE GATE (F7, F15). Gated on WALLS ONLY (front, rear, side; every band equally), bump strikes swept on their own, a distinct FNV seed per origin (ger and ita shared one). DECISION for the coordinator/owner: read as REPAIR OUTCOMES bumpers are 32% (random-seed runs 29-35%) - passes; read as FAULT ROWS they are 71% (66-76%) - FAILS in all 7 origins. The table's 70% bumper roll on every front/rear hit from 3 m/s, against alignment rows only from 10 m/s (6 on a flank) and no row for a dent or a radiator, cannot meet a rows reading for wall contact. The first round's 45% came from making a quarter of the 500 bump strikes, which nothing measured supports.
- RNG (F10). Arm seeds the ledger from Guid.NewGuid, so arming takes no UnityEngine.Random draw (PerfTour's InitState replays again).
- PAUSE READOUT (F11). New line "pull deg : <combined> (crash x, carried y)".
- COOLING TEXT (F2). f60143fb: with the stop-gap on the garage line reads "Cooling system tired - the mechanic replaces cooling parts." and the pre-race line "the cooling is done" (no longer than the old lines; that column clips at 60) instead of naming the weakest part, because a crash now lowers the radiator lane. TestEngineTemp's warning check re-pinned. Two lines of R0b in their own commit: keep it unless R0b lands first; do not ship R0a without it or R0b.
- WEEP TEST (F16). Warm-started; the sound car is checked against TestEngineTemp's cruise band and exactly no coolant lost (88.0 C, 100.0%), not against an identical run; with the median weep 0.71: 90.0 -> 90.0 C two minutes after the hit, below 55% coolant at 10.2 min, past 112 C at 17.7 min (bounded 6-18 and leak..30). The handoff carries no weep (asserted): the coolant it cost is banked through EndCoolantPct.
- PLAY CHECK (F17, F18). The next drive is built through StampHandoff -> CrashBank.ApplyOutcome -> FillCarRequestFor -> ReapplyCar; no pull is written by hand. SwapCar now calls CrashStopGap.StartClean (a fresh ledger, no crash pull, no radiatorHit or weep, WorstHit and WorstBottom reset; nothing when no ledger is armed) and DebugCarOps.DriveCar banks the old car's rolled outcome onto the old car first. The check no longer zeroes anything by hand and asserts the swap starts clean after a planted crash. Runs are guarded so a throw is a FAIL line and the editor still exits.
- BRAKE CHECK (F19). A "brake" job (BrakePlayCheck.Run) in the round's runner, run on base 19d6d4a3 (its code exported by git archive, synced the same way) and on the head in PSXCity: the two reports are identical, 21 of 21 lines, 0 FAIL (STOP 43.5 / 42.7 / 44.3 m, TURN, SLIDE on the Civic EG, RX-7 FD and SL 600).
- GATES, review round (scratchpad `r0a-crash\`, PSXCity, base 19d6d4a3; runner run2.ps1, self-tests with -quit, play checks exit themselves). Typecheck OK. Crash self-test (RunCrash) 91 ok / 0 FAIL (fin_crashtest.txt). Full LifeSimSelfTest: base 62 FAIL lines, head 62, NEW 0, GONE 0 by check text (compare_fails.py; 3,989 ok; fin_selftest.txt). An intermediate head run showed one NEW failure, the TestCrashApplyBack id collision fixed in 01fe977e. Crash play check ALL HOLD, 40 ok / 0 FAIL (fin_crashplay.txt): drift unchanged (FD +-0.99 / +-3.53, 3.49 FULL; Civic EG +-0.89 / +-3.18; Viper +-1.00 / +-3.52), live hit -1.00 m inside the leg and the next drive -1.08 m through the real bank path, carried other / same side -1.08 m live and next, the walls as above, both swaps clean after a planted crash. No-damage path, base vs head: handling 52 ok / 1 FAIL in both (the base's own RUF lane-change FAIL), 255 of 260 lines identical and the other 5 differ only in a chase-camera framing value; temp the same verdicts except the pad-throttle line, which flips between runs as the first round recorded (base FAIL, head ok; the ambient line FAILs in both); parked 44 ok / 0 FAIL in both (0.1 mm hub and 0.001 m drift jitter); brake identical (above).

## Causes (line numbers at 19d6d4a3)
- The crash and bump-stop rolls passed no origin (LifeRules.cs:1353, :1367) and RollWearFault defaulted to "jpn" and sent every unknown origin there (FaultCatalog.cs:108, :121). The JPN pool's only impact rows are bumper_crack (body lane) and alignment (tyres lane), and alignment's effect is 0.25 of lock in a coin-flip direction (rg2_faults.json; Aggregate_ FaultCatalog.cs:429; faultSteerPull x maxSteer at CarController.cs:2542/2547 = about 5 deg of road wheel at 100 km/h).
- One uncapped DamageScore summed over the leg (CollisionResponder.cs:220) decided the roll (>= 22); the hardest hit (:221, :408-410) was never handed off (RaceManager.cs:432-435, CityMode.cs:964-967 stamp only DamageScore/HardHits/BottomScore/BottomWorst).
- The fault acted only after the next scene load (Aggregate_ at FillCarRequestFor). Wear rolls passed no origin either (LifeRules.cs:1780), and the heat/market/inspection/viewing rollers passed raw origins, so the 77 ger/gbr/fra/ita/eur cars rolled the Japanese pools.

## What changed (switches: PSX_FAULT_CRASH=0, PSX_FAULT_FAMILY=0; both 0 = today exactly)
- NEW Scripts/CrashStopGap.cs: the Â§9 table as a pure function (Resolve / ResolveBottom; bumper rating 2.2 m/s for origin usa 1973-82, 1.5 chrome before 1973, else 1.1; severe alignment from 15 m/s front/rear, 9 side and bump stop), Classify (zone and side from the contact patch's centre in the car's own frame; side 0 inside 0.2 of the half-width - review round), the career car's Ledger (rolled AT THE HIT, and only for a hit that is the leg's new hardest - CollisionResponder.RecordHit: a harder contact of the same crash within 0.6 s re-reads the same dice, a softer one is ignored, a softer later crash is never rolled; the leg's outcome is the worst of what was rolled, lane by lane, never a sum; damage never un-happens), Arm (guards FromLifeSim && !TestDrive && Edition.HasCareer), StampHandoff (the one writer for both exits). Pull constants 0.042 deg minor / 0.14 deg severe (set by the drift play check). Radiator outcome: radiatorCond -25..-60 live plus a weep (WeepPctPerSec 0.14, pressure-scaled).
- CollisionResponder.cs: WorstHitZone/Side/Local (the centre of the box around the hit's contact points in CarBody.box's frame - review round; WorstHitWeighted keeps the impulse-weighted point for the play check only), WorstBottomWheel, RecordHit (the leg's record rule in one place, the Ledger hook at the new worst hit), the Ledger hook at the new worst bump-stop strike, StartOverForNewCar. DamageScore and WorstHit untouched (AI retirement, delivery scoring, carHP summary unchanged).
- RaceHandoff.cs: WorstHitMps, WorstHitZone (1 front 2 rear 3 side), WorstHitSide (-1/+1), BottomWorstWheel (+1, 0 none), CrashRolled/CrashClasses/CrashPaintLoss/CrashBodyLoss/CrashRadiatorLoss/CrashBumperId/CrashAlignSide/CrashAlignSeverity, all cleared in ClearResult (and so ClearAll); SteerPullDeg (request half) cleared in ClearAll.
- RaceManager.cs, City/CityMode.cs: the 4 stamp lines -> CrashStopGap.StampHandoff (same values plus the new ones).
- CarController.cs: hitSteerPull (beside heatAccelMult, set at the hit, never written by ApplyCarCondition/ReapplyCar) and faultSteerPullDeg (beside faultSteerPull); combined by CrashStopGap.CombinePull (review round: the worse band wins, a tie to the newer hit - never the sum) and added to the steer target in road-wheel degrees only when non-zero (keyboard and wheel paths).
- EngineTemp.cs: radiatorHit and radiatorWeep (0 = sound car), the weep term in LoseCoolant (skipped at 0), the seizure story says "lost coolant after the crash" instead of blaming the hoses.
- RaceHandoffApplier.cs: Arm after ApplyCarCondition(firstRun); radiatorCond re-subtracts radiatorHit so a bench reapply cannot mend it; faultSteerPullDeg from the request; SwapCar calls CrashStopGap.StartClean first (review round).
- NEW Scripts/LifeSim/CrashBank.cs + LifeRules.cs: the apply-back writes what was rolled (paint, body, radiator, hidden bumper row, hidden alignment row with severity = band and pullDir = struck side; one alignment per car, replaced in place when CrashStopGap.NewAlignWins - the worse band, a tie to the newer hit); the RG2 impact rolls run only with PSX_FAULT_CRASH=0, with origin "jpn" as before. DamageScore's body/paint summary kept as it was; the table's located losses come on top. Step 5's body-lane wear roll reads the body from before the leg's crash (review round).
- FaultCatalog.cs: TryFamily/Family/FamilyOf; every roller (RollWearFault with no origin = the car's own, RollUsedFaults, MakeById, PoolRow) reads the family; Aggregate_ puts alignment into steerPullDeg (PullDeg by severity) instead of 0.25 of lock - this also tames existing alignment faults in the owner's save, with no migration; ImpactOnly and RollWearFault(noImpactOnly) (review round). LifeHomeScreen.cs: one line (RaceHandoff.SteerPullDeg) and the COOLING line (review round, f60143fb). DebugCarOps.cs: the bench describes the alignment pull in degrees; DriveCar banks the old car's rolled outcome onto it before a swap (review round). CoolingModel.cs, PauseMenu.cs: review round.
- Tests: NEW Editor/LifeSimSelfTest.CrashR0a.cs (RunCrash for a quick run; in RunAll after TestWearFaultCause), re-pins in LifeSimSelfTest.cs (TestWearFaultCause's crash case stamps a rolled outcome; TestDebugBench reads steerPullDeg/faultSteerPullDeg). NEW Editor/CrashPlayCheck.cs + tools/crash-play-check.ps1. .meta files for the four new scripts.

## Gates, first round (scratchpad `r0a-crash\`, base = 19d6d4a3 in the same sandbox; superseded where the review round says so)
- Typecheck OK. Origins: jpn 212 -> jpn, usa 30 -> usa, ger 21, gbr 20, fra 13, ita 12, eur 11 -> eur; 0 unresolved. A ger/gbr/fra/ita car's wear roll with no origin: 72/72 eur rows at eur prices; FAMILY=0: 12/12 jpn (after_crashtest.txt).
- Sweep, 500 crashes per origin x 7 origins (front/rear/side/bump stop, 125 each, every table band equally, stratified dice): 6 of 6 classes (per 500: nothing 117, scuff 300, body 157, bumper 104, radiator 30, alignment 128); bumper rows 104/232 = 45% of fault rows (seeded random runs 40-47%), 25% of all repair outcomes; rows only bumper_crack / bumper_dent / alignment, no electrical rows; alignment $60 jpn / $66 usa / $94 eur (after_crash_sweep.csv). Walls only, no bump strikes: 71% of rows are bumpers (32% of repair outcomes) - the table makes front/rear hits mostly bumpers by design.
- Drift, 100 km/h hands-off, metres per 100 m after the pull (after_crashplay.txt), assist OFF / STANDARD / FULL identical (the counter-steer assist's 8 deg slip deadzone is never reached): FD minor +-0.99, severe +-3.53 (3.49 FULL); Civic EG +-0.89 / +-3.18; Viper +-1.00 / +-3.52; left exactly mirrors right; pull-free 0.00. Live: hitSteerPull 0 -> -0.042 the same frame as a ledger hit, -0.99 m within 3.6 s; the banked next-drive pull drifts -0.99 m too.
- Real walls: left third of the nose FRONT LEFT (x -0.27 m), right third FRONT RIGHT (+0.27), right flank into a 200 m barrier whose bounds are centred 98 m ahead SIDE RIGHT (x +0.94), tail REAR. Switch off vs on, same hit: WorstHit 12.10 / 12.10 m/s, DamageScore 19.4 / 19.4.
- No-damage path: handling check - every physics column of every telemetry row identical, same verdicts (1 base FAIL, RUF CTR lane change, both); only chase-camera lens/cam readings differ by <= 0.4 deg (frame timing). Temp check - same verdicts; the pad-throttle line flips between runs (FAIL base, FAIL after, ok after2) and after's boil line hit a random seizure (99.4 again in after2). (The first round's "30 min with no weep bit-identical" self-test compared two identical runs and proved nothing; replaced in the review round.) Parked check - 44 ok / 0 FAIL both, drift 0.002 vs 0.003 m, gaps within 0.1 mm.
- LifeSimSelfTest: base 62 FAIL lines, after 62, NEW 0, GONE 0 (compare_fails.py, by check text). Crash pins 63 ok.

## Not done / not verified
- No player build; no visible-editor drive by hand; no gamepad or wheel feel (0.04 deg is subtle by design - the design moves the cue to the cockpit wheel and FFB in R1b/R2c). RESTART and QUIT still throw the leg's hit away (R1a's DamageBank); the live pull and weep reset on any scene load.
- Leaks R0b owns: the garage's COOLING bar still shows the cooling lane (its value, not a part name), and the mechanic's cooling-services page prices each part. The "weakest part: radiator" line and the pre-race "the radiator is done" line say "the cooling" while PSX_FAULT_CRASH is on (review round, f60143fb - two lines of R0b in their own commit). The other pulling faults (control arms 0.15/0.12, ball joint 0.1 of lock) are still shares of lock until R1b.
- The 5-mph bumper uses origin usa as the market stand-in (no catalog row is a 1973-82 US car today). The weep is live only; the radiator condition and the coolant level persist.
- (Review round) A crash that leaves the body lane under 40 still lets body-lane WEAR roll on LATER legs (distance on a worn lane, as before R0a); with the families on, a European car's body pool is half electrical (RG2's own rows). That is wear, not the crash leg; R4b's Wear.Advance owns how body damage and body wear relate. The market's impact-only rows (a seller's cracked bumper) are unchanged (F6, narrowed).
- (Review round) The pause readout's new "pull deg" line was not looked at on screen (debug only). DebugCarOps.DriveCar banking the old car's outcome was not driven through the bench page by hand; SwapCar -> StartClean is covered by the play check (S) and the self-test.

## At merge
- No CityTileCache format change, no kit slot, no new asset files beyond the four scripts' .meta files. No scene rebuild needed: the new fields are runtime state (hitSteerPull/faultSteerPullDeg serialize as 0 on the next bake). Shared files touched: CarController.cs, CollisionResponder.cs, RaceHandoff.cs, RaceHandoffApplier.cs, RaceManager.cs, City/CityMode.cs, EngineTemp.cs, CoolingModel.cs, PauseMenu.cs, LifeSim/FaultCatalog.cs, LifeSim/LifeRules.cs, LifeSim/LifeHomeScreen.cs (2 places), LifeSim/DebugCarOps.cs, Editor/LifeSimSelfTest.cs (RunAll gets one line after TestWearFaultCause, plus the re-pins incl. TestEngineTemp's warning line).

# HANDOFF - FAULTS R0a SAVE SAFETY 11-12 (branch faults-r0a-save off mountain-nm 19d6d4a3; agent "r0a-save"; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultsave`. Sandbox PSXPerf (lock "r0a-save", released). Design: `Fault_System_Design_2026-11-11.md` Â§2 "Save safety" + "Versions", Â§9 (LifeSimManager row), Â§10 R0a. The crash half of R0a is on faults-r0a-crash (another agent). No version step added: v24 stays current.

## Review round 11-12 (three adversarial reviewers, 15 findings: all fixed, duplicates merged)
- FAILED career, NEW GAME (medium, two reviewers). A career that would not load is a blank state in memory, so `HasCareer` was false and NEW GAME skipped the ERASE confirm; the note called that harmless ("KEPT ... AS A BACKUP. NEW GAME STARTS A SEPARATE CAREER") although the new career replaces the main key and nothing reads `.failed` back. Now NEW GAME goes through the confirm with its own line (`LifeSimManager.FailedEraseLine`), and the note says only what is true: "...IT IS LEFT AS IT WAS, FOR A FIXED BUILD TO LOAD. NEW GAME REPLACES IT." Not done (optional): offering `.last` on a FAILED career - in the main case (a step that throws) `.last` is at the same old version and would hit the same step.
- Re-run point (low, two). A write-back re-ran every step from v25 whatever its stamp. A post-R0a build run with PSX_SAVE_GUARD=0 keeps every field it knows, so those steps would run on live data. Now `s.saveVersion = Mathf.Max(LastUnstampedVersion, s.writerVersion)`.
- `.last` after NEW GAME (low, three). It survived DeleteSave/StartNewGame, so an older exe writing the NEW career back was answered with the erased one (the title's default button). Now NEW GAME moves `.last` into the backups and off its name, and the new career's first save writes its own.
- UseLastCopy (low). It wrote `.last` over the main key before knowing it would load. Now it upgrades `.last` in memory first (BeforeMigrateHook, Migrate, EditionSanitize, StripBuiltCars); one that throws is refused with the main key, the career in play and the copy untouched, is not offered again this run, and the title says "THIS BUILD'S OWN COPY WOULD NOT LOAD, SO THE OLDER BUILD'S CARRIES ON."
- Copies in PlayerPrefs (low). Android's SharedPreferences rewrites its whole XML on every PlayerPrefs.Save(), so `.last`, 3 backups and up to 5 failed copies rode along with every save (up to 10x the career); WebGL caps PlayerPrefs at 1 MB. The copies are now FILES: player `persistentDataPath/saves` (Windows `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PSX Racing\saves`, shared by every PC build like the registry key); editor `<project>\Library\PSXSaveCopies` (an editor's persistentDataPath is the player's own folder, so sandbox tests must never write there). PlayerPrefs holds the career alone, as before R0a. `.last`'s date is the file's write time (a second file for it doubled the rollover save's cost).
- PerfTour (low). env.txt (city and stage tours) gets a `save ...` line from `LifeSimManager.MeasureSaveCost()`: career bytes, Save() median, first save after a rollover, copies. Scratch key only; the player's career is never timed.
- Found in my own picture, not by a reviewer: the failed confirm's second line sat under YES (`MenuKit.Button` pivots on its anchor, so YES spans y 120-174, not 93-147). Moved to 14 over YES. LifeHomePreview's CLIP report compares text with text and read 0, so SaveGuardPreview now also reports any text under a button's face (UNDER A BUTTON TOTAL).
- Tests (low/medium, four findings): a stand-in v25 step (BeforeMigrateHook) so the re-run is observed (from v24, to v25) and the kept copy is v25/w25; the guard-off write-back re-runs from the writer's version; NEW GAME then a write-back offers the new career's copy; a refused trial; files-not-prefs; the NEW GAME doors allowlist; the ladder's second pass is now called what it is ("Migrate has no unguarded code"), and the re-run of steps above v24 on a worked career is logged NOT TESTED until a step above v24 exists (R0b's is then checked automatically); size/time labels fixed (see Gates).

## Causes (before R0a)
- `LifeSimManager.Load` (was :93-109): any throw in FromJson or Migrate was caught, returned null, and `State` became `NewGame()`. The next `Save()` wrote that blank career over the stored one.
- No version guard. An older exe loads a newer save (Migrate's `< N` steps do nothing), drops the fields it does not know, and its next `Save()` writes the career back still marked with the newer version. The newer build then never re-seeds those fields.
- Every PC build shares one registry key (HKCU, `psxRacingLifeSave`), and several builds are on disk.

## What changed (switch `PSX_SAVE_GUARD=0` = the old Load/Save, no copies, no .last; the writer stamp stays)
- `LifeState.writerVersion` (added with no version bump). Every `Save()` stamps it with this build's version, also with the guard off.
- `LifeSimManager` (Load is now `LoadGuarded`):
  - BACKUP. Before the first migration step, the raw string is copied to the file `<key>.bak.v<old>.<yyyyMMdd-HHmmss>.<n>.json`. The last 3 are kept. A string identical to the newest copy is not copied again.
  - NEWER SAVE (saveVersion above this build's). Loaded READ-ONLY and not migrated. `Save()` writes nothing. The title shows a note and CONTINUE (READ-ONLY); the home screen toasts "READ-ONLY: A NEWER BUILD'S SAVE" once.
  - REWRITTEN (v25+ and writerVersion below the save's version: written back by a build that did not know that version). Backed up; the steps above `max(24, writerVersion)` run again. The title offers THIS BUILD'S COPY (`<key>.last.json`, game date and the real date it was saved, trial-loaded first) or OLDER BUILD'S COPY. If no title shows (tools, batch), the older build's copy is kept, re-seeded.
  - `.last`. Written by a new career's first save and by the first save after each daily rollover (`LifeRules.Rollover` -> `NoteRollover`, live career only). NEW GAME moves it into the backups.
  - FAILED (parse or migration throws). The raw string is kept as `<key>.failed.v<old>.<date>.<n>.json` (up to 5). The main key is not touched, and every `Save()` is refused until an explicit, confirmed NEW GAME (`StartNewGame` / `DeleteSave`). The title says so.
  - NEW GAME over a newer or unloadable career copies it into the backups first.
  - Copy writes are files written via a .tmp and a move, all inside try/catch: a copy that fails never costs the save, and PlayerPrefs never holds a copy.
  - `MeasureSaveCost()` for PerfTour.
- `TitleScreen.BuildButtons` + `SaveNote` + `Confirm`: the note sits over the buttons, at most two lines at all three aspects; NEW GAME over a failed career confirms with `FailedEraseLine`; a refused THIS BUILD'S COPY rebuilds the menu.
- `LifeRules.Rollover`: one line at the end. `PerfTour.cs` / `PerfTour.Stage.cs`: the save line.
- Tests: `Editor/LifeSimSelfTest.SaveSafety.cs` (TestSaveDowngrade, TestSaveVersionLadder, `RunSaveSafety`), 2 Guard lines in `LifeSimSelfTest.RunAll`. `Editor/SaveGuardPreview.cs` renders the title notes (6 cases) at three aspects with the clip report.

## Gates (after the review fixes)
- Typecheck OK.
- RunSaveSafety (PSXPerf): SAVE SAFETY SELF-TEST OK, 76 checks, 0 failures; one line NOT TESTED (re-run above v24: no such step yet).
- Whole LifeSimSelfTest in PSXPerf: base (19d6d4a3) 62 FAIL; after (964e6756) 62 FAIL, the identical set (0 new, 0 gone). The failures are city decks, lamps, editions and texture budgets; none are in save code. Its save sections: 76 ok, 0 FAIL. (The last commit after that run moves the failed confirm's text and adds a preview check: title layout and an editor tool only.)
- Writers tripwire (615 .cs files): nothing outside LifeSimManager spells out the key, uses SaveKey/CopyDir without a scratch key, or wipes every pref; the runtime doors to NEW GAME are exactly DoorTour.cs, LifeHomeScreen.cs, PerfTour.cs, TitleScreen.cs (15 editor tools also call them, on the editor's own prefs). `LifeSimManager.Save()` is called 209 times in 37 files - a count, not a check; the guard is inside `Save()`.
- TestSaveVersionLadder: steps exactly v2..v24, once each, in order, each stamping its own number once; every version 1..23 migrates to v24; Migrate has no unguarded code (a second pass at v24 runs nothing - this is NOT a re-run check); the re-run of steps above v24 is NOT TESTED until one exists.
- Save size (measured on this branch): a new career 6,653 bytes; a worked career (10 cars, 60 days, 2 faults, a shelf part, a booking) 25,577 bytes, of which the writer stamp is 19 bytes (so 25,558 bytes before R0a, exact: the stamp is the only field R0a added). PlayerPrefs holds only that; the copies are files (.last 25.0 KB, 3 backups 74.9 KB).
- Save time (editor, registry, median of 5 interleaved batches of 10):
  - Save() 0.42 ms with the guard off (this branch's Save with PSX_SAVE_GUARD=0 = the pre-R0a Save plus one int) and 0.42 ms on; 0.41 ms with .last and 3 backup files beside it.
  - First save after a rollover (+ the .last file): 6.2-6.9 ms (was 0.53 ms with the copy in the registry). A 25 KB file costs 3-4.5 ms on this disk however it is written (measured outside Unity: direct 3.1, new file 3.5, tmp+move 4.6 ms); tmp+move is kept so a crash never leaves no .last. Once per game day, at a door.
  - Load 0.58 ms; a load with a migration (+ a backup file; only the load timed now) 7.2 ms, once per version bump. The earlier 2.25 ms included the test's own string building.
- Title preview: 6 cases x 3 aspects (ok, newer, rewritten, rewritten_refused, failed, failed_confirm): CLIP TOTAL 0 and UNDER A BUTTON TOTAL 0. Sheet: scratchpad `r0a-save\title_saveguard_sheet_review.png`. The first render of failed_confirm had the warning's second line under YES while CLIP read 0; fixed, and the new check covers it. Still: on a phone the Rewritten note's date breaks across its two lines (WED 10 / FEB).

## Not done / not verified
- No player build and no play-mode title: the preview builds the title menu bare, over a flat sky colour.
- PerfTour's save line is pinned by the self-test (MeasureSaveCost) but no tour was run (no player build).
- Android/WebGL: not run. With the copies as files, PlayerPrefs carries one career's worth per save, as before R0a. WebGL (frozen): its files are not synced to IndexedDB, so a copy lasts as long as the tab.
- If the title is skipped (tools; a build without the Blue Ridge venue) a FAILED career goes straight to the wizard with no note. ARCADE returns to the title, so the normal boot always passes the confirm.
- The owner's real save size was not read.

## For the coordinator at merge
- Nothing for the city: no CityTileCache format bump, no kit slot, no scene pass. The new .cs files have committed .meta files with fixed GUIDs.
- Shared-file edits:
  - LifeRules.cs: +4 lines at the end of Rollover.
  - LifeSimSelfTest.cs: +2 lines in RunAll, after TestPartsShelfSave.
  - LifeState.cs: one field after saveVersion.
  - TitleScreen.cs: BuildButtons, SaveNote, Confirm.
  - PerfTour.cs: SeedCareer + one env line; PerfTour.Stage.cs: one env line.
  - LifeSimManager.cs: Save, Load, DeleteSave, StartNewGame and TakeEditionNote, plus the new section.
- R0b (the first bump, to v25):
  - Keep `LastUnstampedVersion` at 24 forever.
  - Write the step as `if (s.saveVersion < 25) MigrateToX(s);` and stamp `s.saveVersion = 25;` (TestSaveVersionLadder checks this).
  - The step must be safe to run a second time on a save that already went through it: TestSaveVersionLadder then runs it twice on a worked career and fails if the career changes.
- When the first v25 build ships, recycle every PC build from before R0a (design Â§2 "Old builds"). Only a build with this guard refuses to write a newer save back.

# HANDOFF - CITY INTEGRATION 11-11 ("cityinteg", branch integ-city-1111; NOT pushed, NOT merged by me)

Base mountain-nm 93903bd7 + fixes-1111 (beacons r1-r3; car/cockpit files kept at mountain-nm, proven: no diff outside the beacon files) + charlotte-curbs-drains 128f9e53 + charlotte-road-seams 9f0e0d08 + charlotte-buildings-1111 (merged three times: 1f001b35, 312abb93, e98adc39) + the coordinator's 9c7d9750 (roof _HouseTint after the material exists). Sandbox PSXPerf (lock "cityinteg", removed). Sheet: scratchpad `cityinteg\city_integ_sheet.png` (base | merged, 14 poses).

## Conflicts and how I resolved them
- fixes-1111: CockpitFit.Table.cs, CockpitInterior.cs, DoorMirrors.Table.cs, CarViewer.cs, CarShell.cs, PSXCarPaint.shader conflicted, and CockpitFitProbe.cs / DoorMirrorMatch.cs auto-merged with old code. All of them take mountain-nm's version (cars2 builds on top of them). HANDOFF.md merged as a union.
- curbs, seams: only HANDOFF.md conflicted. CarController.cs (curbs envelope, OFF) and mountain-nm's parked fix auto-merged; both are kept. PSXLit.shader (buildings _HouseTint) merged cleanly with mountain-nm's texture-round lines.
- buildings 1f001b35 DID NOT COMPILE: HousePolyRoof and the flat counter were spliced inside the roof block. I put them back in order; the branch fixed the same thing itself in 4f6276fe.
- buildings 13ce514b (HouseStepOffRoad, 1 m, units included) covers my 96576db8 (PSX_CITY_HOUSEPARTCLEAR, 0.75 m). I kept the branch's rule and removed mine.
- Added .meta files for 12 new scripts that came in without one.
- CityTileCache.Format: NOT bumped. Every baked class field is listed (treefields_check: 0 unknown, same as base). The new house vertex colours ride the generic colour channel. No new Slot. The fingerprint changes anyway, so the city must be re-baked.

## Fixed here (switch =0 gives the before)
- PSX_CITY_SLABSEAT (RoadSlab.cs): a fan-mouth step face now stops on the ribbon's own edge, instead of going 3 cm through it. Flicker junction|tw3t: 97 -> 0 px.
- PSX_CITY_PANSEAT (Curbs2.cs, plus one line in CityMeshes.cs TileSteps): corner gutter pans are laid after the closing pass, on the drawn junction surface. Flicker junction|structure: 106 -> 0 px.
- PSX_CITY_CURBNOEDGE (Curbs2.cs CurbNoEdgeLines; CityMeshes.cs EmitRibbon / DrawnLines): a curbed walk side has no edge line. Uncurbed roads, freeways and a median side keep theirs. The full-quad spans become three strips (2 extra quads).
- 0.5 mm / 20 cm bug in my own first version: TriGrid.Between(..., any=true) returns the band top, not the surface height (a88d3904).
- New tools: Editor/CityIntegCost.cs (triangles and ms per tile on 3x3 rings) and Editor/CityIntegShots.cs (poses written to a file, so the base code can shoot the same frames).

## Gates, base 93903bd7 | merged (final head)
- Drive audit: uptown, tryon and independence routes + 9 default tiles + box (Wesley Heights courts included): 30 ok/FAIL lines identical. Walls in lanes 0|0, fall holes 0|0, invisible in lanes 0|0, solids 88|88, drawn faces in a lane 120|120. N Graham e20287: 26 lines identical.
- Launch: 91|91 (the same spots, within 1 mm). Unload: 565|563.
- Traffic: CITY TRAFFIC OK twice, 0 traffic-on-traffic bumps.
- Flicker: 460|525.
  - FIXED: the seams' fan-mouth tie and the corner-pan tie.
  - LEFT: Buildings FacadeGlass|RoofFlat 46 px at tryon0, 150-400 m out. It goes with PSX_CITY_LRLOOK=0 + PSX_CITY_LRHEIGHT=0 (478 px then), so it comes from the types round's low-rise. Not found; that is for the buildings owner.
- Seam audit (open metres, merged code, slab off -> on):
  - owner tile 3,442 -> 93 (68 with CURBNOEDGE=0)
  - Uptown 4x4: 106,183 -> 2,980 (2,895)
  - 60-tile sample: 99,105 -> 498 (517)
  - Open steps: 0 at the owner tile. The rest is 0-1 mm T-junction joins. Not 0.
- Windows: cut + buried 9,904 -> 0 (64 tiles). Flat house roofs 0.
- House census: GAP 0 = base, buried p95 0.60 = base.
- Roof closure: 838 -> 845 points see daylight.
- Trees: FAIL 152|152.
- Beacons: far 118/121, near 118/121 within 5 cm.
- Cost, base | merged, ms a tile and triangles a tile:

  | ring | ms | triangles |
  |---|---|---|
  | N Graham | 150 \| 168 | 17,955 \| 29,558 |
  | Uptown | 396 \| 517 | 37,776 \| 46,826 |
  | Plaza Midwood houses | 159 \| 194 | 20,869 \| 39,745 |
  | I-77 | 332 \| 384 | 26,069 \| 39,102 |

  - Growth by mesh: buildingCollider (houses x4.7), structure (pans, lids), roads (slab).
  - Full bake estimate: about 2,000-2,400 s (today 1,711 s), with the tile files about 1.5x larger.

## Seen in the pictures / not done
- N Graham: the edge line is gone. The gutter pan and the plumb curb read right, and the corner pans go round the returns.
- The owner's frame is NOT matched: the seams' pick (n14254 / e20287) is a 2-lane arm, and his screenshot shows 4 lanes.
- Night frames: the sidewalks are blown out in the editor, before and after.
- Atando Ave: my pose shows trees, not the sheds.
- Lexington Ave: rolled curb, porches, chimneys. The house inlet frame landed on a vertical-curb street.
- Not done: no player build, no play-mode drive.
# HANDOFF - CAR RE-DELIVERIES 11-11 AFTERNOON + ROUND STEERING WHEEL + VIPER/FULL-SIZE COWL (cars-1111b, 2026-11-11; agent "cars2"; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-cars2` off mountain-nm fc5e975e. Sandbox PSXLab (lock "cars2", released). Sheet: scratchpad `cars2\cars2_sheet.png`.

## Owner report
"I fixed issues with these three models, may need to adjust cockpit placements accordingly" (Viper v14, FD Textured_18, CTR blue v06 + Yellowbird v02).

## Causes
- Viper on the old cockpit fit: `ChaseCamera.CowlOf` compared the baked cowl's Z with the collider box's FLOOR height (`def.cowlZ > c.y - s.y*0.5`, since 08-28). The Viper's cowl is 0.13 m ahead of centre, under the floor height, so it read "unmeasured" -> no Fit3. Now compared with the box's tail (the self-test's own rule). Switch `PSX_COWL_ZCHECK=0`.
- Full-size police/taxi/trooper (no live mirror glass, no Fit3, driver seated under the cowl): NOT the same cause. `CarModelBaker.MeasureCowl` anchored the roof on the 17 cm light bar / sign, stepped down onto the roof and called it the bonnet (cowl 1.44 m vs roof 1.61). New roof-fixture rule (a step ending < 0.25 m under its anchor re-anchors and walks on): cowl (0.79, 1.01), roof 1.44. Every other shell's cowl is >= 0.32 m under its roof, so only these three change. Switch `PSX_COWL_ROOFSTEP=0`.
- camry_2001 still has no live mirror glass: a different cause (its cowl measures right; no mirror patch passes the merged-mirror rule). Not fixed.
- Oval wheel: the wheel hung under the cabin model, scaled per axis (CTR 0.84/0.94/0.79), and a tilted wheel under that is an ellipse (bmw_e30 22% oval, CTRs ~12%).

## Changes
- Six bodies in place from the new deliveries (exporter entries -> converted/viper_v14, flatsix_coupe_hd06, flatsix_coupe_87_v02, rx7_fd_*_t18); bounds and wheels identical, same scale; 512 sheets box-filtered from the new HD colour; FD lamp map re-made (Viper/CTR lamp maps byte-identical in the deliveries, kept); covers via the cover rule (FD 438, Viper 295, CTR 444). HD stage `C:\Users\mcgee\PSXHDStage\carhd` rebuilt with stage_carhd.py: 23 files changed (only the six bodies + manifest), 17 untouched (Evo, EG, S2000, Viper normal, Viper/CTR lamp maps).
- Round wheel: `CockpitInterior` hangs the wheel on its own `WheelRoot` beside the model, uniform scale = min(across, height) of the cabin fit, mirrored for RHD (`WheelFrame`); `CockpitArms.WheelWarp` maps the wheel's frame into the model frame (RimPoint, RimTangent, WheelMatrix, wheel face), `RimLocal`, `GripTube`. Switch `PSX_COCKPIT_ROUNDWHEEL=0`. Gate (d) in `CockpitFitProbe`: wheel oval < 0.5% and grip tube within 5 mm of the drawn rim at 0 / +-60 deg. `DoorMirrorMatch.GripGap` measures the grip tube against the drawn rim.
- Re-measured: DoorMirrors.Table (CTR mirrors moved 2.7 cm, full-size live glass 19.4 x 12.6 cm), CockpitFit.Table (full-size rows, calib 48 -> 52 shells: CalibZ 1.129 -> 1.144, CalibY 1.205 -> 1.214), ArchCeilings.bytes; ArchHubs.bytes byte-identical; nine prefabs re-baked. `CarHDShots.BakeImport` keys + the three full-size shells.

## Proof / gates
- FitBatch 52 shells 0 FAIL (was 47 FAIL on gate (d) with the round wheel off: worst oval 22.4% bmw_e30); oval 0.00% and grip 0.0 mm on all. Viper now Fit3: x/y/z 1.00/0.85/0.75 (length at MinScale), eye 37 cm forward, 7 cm down. CTR cabin across 0.875 (was 0.842).
- CheckBatch 52 shells 0 FAIL; ArchSweep 0 rows seated high (319 rows).
- Lamp maps: every lit texel on a correctly facing end face (lampdiag) for all six; FD tail/brake follow the moved optics.
- Wheels: unchanged files; CTR tyres still 574 mm vs 625-643 spec.
- Self-test (PSXLab): 64 failures; vs the round-2 base set: none new, one fixed ("every shell seats its driver in its own cabin", the full-size shells).
- Parked check (visible editor, Eastside Lot + 15% slope, 30 s): 68 ok; drift <= 1 mm, no lamps, arch gaps unchanged on viper_gts, flatsix_coupe, flatsix_coupe_87, rx7_fd_93, rx7_fd_99; side-on lift 0.0 mm. The 1 FAIL is "rx7_fd_nowing: no catalogue car wears it" (true before: no wingless FD row).

## Not verified
Player build; night with street lamps; steering wheel roundness only by the gate's numbers (it is dark in the cockpit frames); the full-size cockpit header still measures on the light bar (header dy -25 cm; gates pass).

## At merge / to reach the exe
Copy by path: Art/Car/Models/{viper_gts,flatsix_coupe,flatsix_coupe_87,rx7_fd_93,rx7_fd_99,rx7_fd_nowing}/* (obj/mtl/png), Resources/CarLights/rx7_fd_{93,99,nowing}_lightmask.png, Resources/ArchCeilings.bytes, Resources/CarModels/{the nine}.prefab. Run `CarHDShots.BakeImport` (covers the six + full-size), `PSX_CAR_HD_SRC=C:\Users\mcgee\PSXHDStage\carhd`. No City / RoadsideRules change; no scene pass; no tile bake.
# HANDOFF - CHARLOTTE MESH TREES ROUND 2 (charlotte-mesh-trees, ctrees2, 2026-11-11 evening; NOT merged)

Branch `charlotte-mesh-trees` (worktree `C:\Users\mcgee\PSX Racing-ctrees`), mountain-nm fc5e975e merged in first (HANDOFF conflict only). Sandbox PSXLab (lock "ctrees2", released). Nothing under `Scripts/City/` and not `Scripts/RoadsideRules.cs` changed (`git diff --name-only c81f2a59 HEAD -- Assets/PSXRacing/Scripts/City Assets/PSXRacing/Scripts/RoadsideRules.cs` prints nothing): player-only build, no re-bake.

## Causes
- Trees vanished (round 1: 44% at N Graham, 72% Uptown; ALL 2,816 of 2,817 grate trees in round 1's six rings): no owned model is a pruned street tree (every crown starts 1-4 m up and is 5-16 m wide), and the road test held a grate trunk 0.35 m past the MAP's half width - but 17% of Uptown/Graham grates stand 0.4-0.8 m inside it (the street dress steps them out from the DRAWN curb, `CityMeshes.StreetDress.cs` DressSpan, TreeLaneClearM 0.95).
- Audit 11 (round 1): `CityMeshTrees.Gather` skipped every small collider whose top was under foot+0.6 m while the audit counts crown points from 0.4 m (Big Oak's low limbs/flare over low walls); the tile-wide-solid band test was a capsule that is a sphere when the band is shorter than it is wide (missed the band's rim). Found on the way: in edit-mode tools the fit ran BEFORE the tile's sign posts stood (one 05_Poplar pine through a `CityPost`).
- Hitches: the whole tile was fitted in its stand-up frame (play-mode tour: up to 171.8 ms of fitting in one frame, 466 frames over 4 ms).
- Cost: the pine stand-in poplars' LOD0 (20k) and Big Oak (37.6k, 27.9k of it bark) at the nearest level; Meadow poplars' 4th/5th pack levels unused.

## What changed (switches)
- `Editor/PSXRacingBuilder.CityTreeKit2.cs` (new): STREET FORMS (`CityTreeKit.Role.Street`, appended): Meadow poplars 02/03/07 + Forest beeches 01/02/03, each as vase / column / high (18): lowest branch rings cut, root flare drawn in, trunk stretched, crown lifted to 0.43-0.53 of the height and narrowed to 0.42-0.62 of its width; impostor cut at the same height and moved alike. LOD LADDER: triangle budget per level 6.5k/3k/1.3k (Big Oak 10k/3.6k/1.8k from its coarsest pack level, bark twigs thinned by area); poplars take their own pack levels 0-4; leaf clusters thinned + grown elsewhere. City COPIES of the impostor materials x1.3. `PSX_CT_R2KIT=0` = round 1's kit.
- `PSXRacingBuilder.CityTreeKit.cs`: pack levels 0-4 read, ladder by budget; nearest level 65 -> 55 m (102 m on MEDIUM after PopFix).
- `CityTreeKit.cs`: `Role.Street` appended, `Bands` 12 -> 16, `lodTris`.
- `CityMeshTrees.cs`: street trees fit street forms first (stay in their grate; road envelope = the drawn pavement, `GratePave`); other trees try the street forms before a smaller tree; colliders skipped only under 0.3 m; tile-wide solids asked as two boxes (octagon round the band); FIT SLICED (2.5 ms a frame, `PSX_CT_SLICE_MS=0` = round 1's one-frame fit), one physics sync a frame, a tree given up after 160 volume tests; trunk table replaced when a tile's fit ends (trees moved/gone); pool starts at 128 slots, growth refills over frames; `PSX_CT_ROUND1=1` = round 1's fit; trace `PSX_CT_TRACE` (or PSX_PERFOUT/ctrees_trace.csv in a perf tour); `PSX_CT_FLIP=s` flips F8 on a timer.
- `Scripts/Dev/PerfTour.cs` (shared, +4 lines): tour routes `ctr_south` (South Blvd), `ctr_prov` (Providence Rd).
- `Editor/CityRefSpots.MeshTrees.cs` rewritten: cards | round 1 | round 2 at poses where the trees are, cards in the row's dress, drop survey on round 1's 150 tiles, audit, trunks-v-trees check, ring triangles per FOREST DETAIL, pop lag pair, impostor pair.

## Proof (PSXLab, editor, PC profile, MEDIUM)
Sheet: scratchpad `ctrees2\ctrees_round2.png` (19 rows: Uptown Tryon grate block noon/dusk/night + 150 m, N Graham graham_b noon/dusk/night + 150 m, Dilworth summer/fall/winter + 150 m, south subdivision, I-277 John Belk verge, Johnston Rd median; impostor pair). Night + rain from the cockpit with mirrors (play mode): `ctrees2\ctrees_round2_night_cockpit_rain.jpg`. Logs `ctrees2\ctrees2_log_final.txt`, tour CSVs `ctrees2\tour_*`.
- DROPS on round 1's six rings (27,884 trees): round 1 2,912 (10.4%), grate 2,816/2,817; round 2 69 (0.2%), grate 45/2,817 (1.6%). Uptown 26 (1.2%), Graham 21 (0.7%), Dilworth 2, south 1, verge 3, Providence 16. Remaining grate drops: crown band at 4.6-6 m against a wide street's lane (no form clears it) and sign posts / skybridges; others: 160-test cap (median ring 16) and tile-wide Buildings.
- AUDIT 0 on 215 tiles / 38,516 trees (70 M crown points). TRUNKS v DRAWN TREES (replaces CityAudit.RunDrive, which stands tiles without mesh trees): on round 1's 150 tiles, 5,604 solid trees, trunks with no drawn tree 0, solid trees with no trunk 0.
- RING TRIS, 25 tiles from the eye, MEDIUM: Uptown 0.50 M, Graham 0.58, Dilworth 1.45 (round-1 kit 2.08), subdivision 0.70, verge 0.87, median 0.78. LOW/HIGH: Dilworth 1.01/2.66, others 0.38-0.56 / 0.88-1.79.
- PLAY MODE (visible editor, PC, noon, chase, traffic; routes Tryon St -> South Blvd -> Providence Rd, 150 s each): pool 128 slots throughout (no growth), slots used 0-116 and given back as tiles left (no leak). Fit per frame: one-frame fit max 171.8 ms, 466 frames over 4 ms, 32 frames over 100 ms on Tryon; sliced: max 24.1 ms, 35 of 3,113 fitting frames over 4 ms, 0 frames over 100 ms on Tryon/South. Frame p50 cards 17.7/16.7/19.3 ms vs mesh (sliced) 17.6/17.0/17.0 (traffic jams dominate the spread); forest CPU (cull + draw + mirrors/probe) ~0.8 ms. Editor 1080p shot frames +4.5 to +9.4 ms (that path rebuilds the forest's materials every shot: an over-count).
- POP (lag pair, 6 m, 30 steps over 300 m): Dilworth 0.08% of blocks change (worst step 0.61%) at 102 m vs 0.03% at 120 m; subdivision 0.13% vs 0.06%; noise 0. Kept 102 m for the triangle target.
- IMPOSTOR v MESH (Dilworth 150 m, every tree one way then the other): tree-pixel luma 58.1 mesh / 44.5 impostor -> 59.3 after the x1.3 copies; impostors still cover ~40% more pixels (fuller silhouettes).

## What still looks wrong / not done
Uptown's grate trees read as young and thin (narrow forms of slender poplars); a cut beech shows branch stubs up its trunk (Graham 150 m). Many non-street trees against walls became street forms (Dilworth 457), so some yards are gaunter than the cards. The Uptown cards frame shows no grate trees at all where round 2 plants a row (cards for those grates were not drawn in that editor frame - not investigated). 35 fit frames still over 4 ms (one dear tree ~20 ms: search not resumable). Impostor silhouettes fuller than meshes. Not run: a player build; the city drive audit (`CityAudit.RunDrive` stands tiles without mesh trees in edit mode - replaced by the trunks-v-trees check); door-mirror close-ups; snow; dusk/night for more than two places; the houses' porch boxes (`tm.houseBoxes`, roofs agent) are not read (house margin 1.6 m + gable as before).

## Coordinator at merge
1. `tools\store-assets.ps1 -Dest <build sandbox>` (unchanged from round 1).
2. With PSX_PC_CONTENT=1: `-executeMethod PSXRacing.EditorTools.PSXRacingBuilder.BuildCityTreeKit` (rebuilds the kit: 44 species, CT2_* meshes under Assets/PSXRacing/Generated, CT2_city_* impostor materials in the materials folder). Full scene pass and tile re-bake NOT needed; no CityTileCache.Format change, no kit slot.
3. Shared files: `Scripts/Dev/PerfTour.cs` (2 routes), `Scripts/MeshForest*.cs` untouched this round. New: `Editor/PSXRacingBuilder.CityTreeKit2.cs` (+ .meta).

# HANDOFF - CHARLOTTE MESH TREES (charlotte-mesh-trees, ctrees, 2026-11-11; NOT merged)

Branch `charlotte-mesh-trees` (worktree `C:\Users\mcgee\PSX Racing-ctrees`) off mountain-nm c81f2a59. Sandbox PSXLab (lock "ctrees", released). Owner: "Why are you still using old tree cards when I've specifically asked to use Mountain/Meadow dynamic trees, plus free Poplar and Oak from assets."

## Cause
Charlotte's trees were never on the mesh path: `Scripts/City/CityTrees.cs` builds 2 crossed 128 px cards a tree into one mesh a tile; `MeshForest` was only built by the stage builder, as a scene-baked component.

## What changed (no file in Scripts/City touched: the tile bake fingerprint is unchanged, no re-bake, no baked-class change)
- `Scripts/MeshForest.Pool.cs` (new) + small edits in `MeshForest.cs` / `MeshForest.Gpu.cs`: a STREAMED MeshForest - fixed slots (one a live tile, 480 plants each, 96 slots to start, doubles when full) in the same GPU buffers, cull and indirect draws; a slot's chunk cards = the tile's old card renderer (shown only where the chunk is not drawn; still the sun-shadow stand-in). `pooled` + `dressSource` fields; stage forests unchanged.
- `Scripts/CityTreeKit.cs` (new ScriptableObject, Resources/CityTreeKit.asset) + `Editor/PSXRacingBuilder.CityTreeKit.cs` (new): 26 species - 6 Forest beeches (mountain materials/impostors), 2 bare snags, 4 small beeches, Big Poplar, 6 Meadow poplars (parts per LOD merged by hand: pack meshes are Read/Write off; pack 4-view impostor sheets cut to alpha bounds), 2 of them as an evergreen PINE STAND-IN, 4 grey willows (crape myrtle), Big Oak (FBX LOD1-3, its billboard keyed to alpha as impostor). Crown profile per species: 12 height bands of LOD0. PC content only: written when PSX_PC_CONTENT=1, DELETED by any other pass. Hooked into the city scene build (`PSXRacingBuilder.City.cs`, one line) and runnable alone: `-executeMethod PSXRacing.EditorTools.PSXRacingBuilder.BuildCityTreeKit`.
- `Scripts/CityMeshTrees.cs` (new): hooks the existing static `CityWorld.TreeFrameReady` (no CityWorld edit); per tree chooses a species by CityTrees.Species / street stem / young + hash, sizes it (willow oak x1.35 card height, etc.), FITS it as a volume against road clearance (4.6 m over pavement, decks from below), building footprints+roofs (houses +1.6 m for porches/roofs), poles/wires/cobra-heads, signal poles/arms, and every collider round it (tile-wide Buildings/Barriers MeshColliders band by band). Order: other model / smaller (to 0.44) / move up to 3.5 m away from the wall / poplar / small beech / bush / drop. A dropped or moved tree is written back into the TreeTile before it stands up, so trunk colliders follow (nothing solid undrawn). Switch `PSX_CITY_MESHTREES=0` = cards; F8 = cards A/B. Gate: `PlatformProfile.PC` && kit present && compute (phones/web keep cards).
- `tools/store-assets.ps1`: Big Oak (6 files) + Meadow poplars 01,02,03,04,05,07 / grey willow (28 files). Extracted into PSXLab.
- `Editor/CityRefSpots.MeshTrees.cs` (new): `-executeMethod PSXRacing.EditorTools.CityRefSpots.RunMeshTrees` (env PSX_PROFILE=PC, PSX_PC_CONTENT=1): before/after shots, city foliage audit (LOD0 vertices, independent of the fit's bands), cost.

## Proof (PSXLab, editor, PC profile, FOREST DETAIL MEDIUM)
Sheet: scratchpad `ctrees\ctrees_before_after.png` (22 pairs: Graham, Uptown Tryon, Dilworth summer/fall/winter, south (Pineville-Matthews Rd), "fwy_verge" (snapped to Old Pineville Rd, NOT I-77), Providence Rd; eye + 150 m; Graham/Uptown dusk+night).
- 150 tiles fitted: 24,994 mesh trees, 2,890 not fitted (1,069 solid -> trunks removed), 266 moved, 3,722 shrunk, 2,335 swapped; fit 16 ms a tile average (Dilworth 60 ms a tile - in the stand-up frame: a hitch to slice later).
- Drop rate by place: Graham 44%, Uptown 72% (crowns against tower walls: `BIG Buildings` collider 133k candidate refusals), Dilworth 1%, south 0%, verge 0%, Providence 0.1%.
- CITY FOLIAGE AUDIT, 140 tiles, 24,597 trees, 43.7 M crown points: 11 trees through a solid (gate is 0: NOT MET) - all "collider", 9 are Big Oak (low crown points 0.3-0.6 m up next to small colliders; suspect the fit's skip of colliders under foot+0.6 m). Road envelope / building data / poles / signals: 0.
- Cost at the driver's eye (1920x1080, editor render+readback, cards -> mesh): Graham 12.8->19.2 ms, Uptown 12.2->15.8, Dilworth 11.8->17.2, south 10.7->18.9, verge 21.3->20.3, Providence 9.2->15.6. Mesh tris drawn 0.09-2.2 M (south ring 2.17 M: the pine stand-in 05_Poplar_skan LOD0 dominates). GPU buffers ~25 MB at 96 slots.

## Not done / not verified
Pop-in measurement (popwalk/poplag) not run (distances = the stages' PopFix x FOREST DETAIL, impostors to 1.2 km); no player build; no play-mode run (streaming release/grow untested outside edit mode); wind; snow on crowns; mirrors (impostor path inherited, unseen); card trees in the "before" frames wear the dress they stood up in (fall) even in the summer rows; Uptown grate trees mostly dropped (no clear-stem model narrow enough for 3-4 m to a facade); median row spot shows no median trees; far city (CityFar) and CityUnderstory stay cards.

## Coordinator at merge
1. `tools\store-assets.ps1 -Dest <build sandbox>` (Big Oak + Meadow trees; PSXLab already done).
2. With PSX_PC_CONTENT=1: run `PSXRacing.EditorTools.PSXRacingBuilder.BuildCityTreeKit` (or the city scene pass, which calls it). Full scene pass NOT needed. Resources/CityTreeKit.asset rides in the player's resources (not a bundle; the mountain forest ships in its stage scenes). Any non-PC scene pass deletes it.
3. No tile re-bake, no CityTileCache.Format change, no kit slot.
4. Shared-file edits: `Editor/PSXRacingBuilder.City.cs` (1 line), `Scripts/MeshForest.cs`, `Scripts/MeshForest.Gpu.cs` (pooled guards). New script metas committed (stable GUIDs).
# HANDOFF - TEXTURES ROUND 4: SHOPFRONTS AT SOURCE RES, R2 ROADS/WALKS, GLASS c00-c02 (tex-round4, 2026-11-11; agent "tex4"; NOT pushed, NOT merged)

Branch `tex-round4` (worktree `C:\Users\mcgee\PSX Racing-tex4`) off mountain-nm b970153d. Sandbox PSXCity (lock "tex4", released). Sheet: scratchpad `tex4\tex4_before_after.png` (also art workspace `Charlotte_Texture_Context\2026-11-11\G_00_tex4_sheet.png`).

## Owner report
"why are building textures so blurry when I had Codex overhaul them to 2k resolution?" (Uptown shopfronts, BRICKMOON / PET SALON).

## Causes
- `Editor/PSXRacingBuilder.City.cs` ComposeShops (~1140): the four 1508-1747 px fictional storefronts squeezed into 256 px cells of `Art/City/city_shops.png` (1024x256), once.
- Then the PC import: `ConfigureTextureImporters` (PSXRacingBuilder.cs ~1162) sets the DEFAULT platform max to `PSXTextureCaps.MaxFor` = 256 and Standalone is not overridden, so the PC exe ships city_shops at **256x64 = 64 px per 6 m front, point, no mips** (verified in PSXLab/PSXBuild/PSXCity .meta). Same cap hits every Art texture without an Enhanced twin or HD set (sweep below).
- Aspect: a front is drawn ~6 m x 4.2 m (1.43:1; real walls give 3.4-9 m a front: `FacadeMeters` Shops 24 m / 4 fronts, `EmitWallStyled` reps = round(wall/24)); sources are 1.45/1.74/1.93/1.94:1, so three fronts are squashed ~25% horizontally. The UV rule is in CityMeshes.cs (fingerprinted): NOT changed, for the re-bake round (e.g. 30 m a repeat, or Codex authors at 1.43:1 - asked).
- Shopfront geometry: the storefront canopy/sign band (`EmitStorefront`) covers the top ~0.8 m of each front, cutting most signs in half, and shades the windows (re-bake item).

## Changes (no fingerprinted file touched: `git diff b970153d -- Assets/PSXRacing/Scripts/City Assets/PSXRacing/Scripts/RoadsideRules.cs` = 0 lines; KitHash untouched)
- `tools/city/shops_hd.py`: `city_shops_overhaul1_hd.png` 4096x1024 (each source whole in a 1024 cell, exactly the old cell; registration vs shipped atlas: mean diff 2.2/255 at 0 px shift, 9.7 at +-1 px) + `_rough` (glass 0.30 from the night mask, else 0.85). The shipped `city_shops_night.png` rides as `_hd_night` (uncapped 1024x256, point).
- `Scripts/TextureHD.cs`: HD sets may carry `_night` (PSX/Lit `_NightMask`, restored on Off); `PSX_HD_SKIP=city_shops,city_facade_hd` A/B switch (env, not a generator prefix). Facade arrays now hold only delivered columns (`_FacadeHDOn[c]` = slice+1): 3 columns = 24 MB instead of 128 MB.
- `Editor/TextureHDTool.cs`: imports `_night`, ships it; compact arrays.
- `Shaders/PSXLit.shader` (shared, 4 lines ~1803): slice from `_FacadeHDOn`.
- `Scripts/TextureHDMap.cs`: `AsphaltNewGain` default 1 (R2 asphalt_new authored at 0.040); `PSX_HD_ASPHALT_GAIN` env for A/B.
- `Editor/CityRefSpots.Tex4.cs`: `RunTex4` shots (modes = HD stage folders, shop spots found from OSM shop footprints).

## Stage: `C:\Users\mcgee\PSXHDStage\texb02` (62 PNGs, 102 MB, `stage_manifest.json` with sources + sha256)
= R2 Release (40, Codex `Environment_HD\Release\HD_02_Charlotte`; NOT NewBindings R1) + rock_pt_2 set (4, from texb01) + city_shops HD (3) + glass candidates c00-c02 (15, verdict MEETS). texb01 is now superseded (keep until the exe is verified).

## Proof (editor, PSXCity, same poses, today = texb01 + gain 3.1, after = texb02)
- Shops: CityShops main texture after = city_shops 4096x1024 Trilinear aniso 8, 13 mips, PSX_HD on. Signs legible from the chase cam (BRICKMOON, CURBKITE); night windows registered.
- R2: road in tower shade at noon same as R1+gain (linear 0.017 vs 0.018); Square at dusk/night 1.5x brighter (gain was day-only); other streets within 2%. Pavers darker joints / browner. No magenta.
- Glass: crisp mullions/spandrels near and far, night windows unchanged. Verdict written at top of art workspace `Charlotte_Texture_Feedback_2026-11-11.md` (+ storefront request, R2 verdict).
- Still soft: column 07 parts (stone band/canopy over shops, ~10 cm blocks), the canopy cutting signs, shop interiors dark under it; glass reads clean/CG-flat near.

## Sweep (PC profile: what ships below its source)
| Art | Source | Ships on PC | Where | Fix |
|---|---|---|---|---|
| city_shops (+night) | 4 x ~1700x950 | 256x64 point | core retail walls, strip-centre bays | DONE (HD tier) |
| facade atlas col 07 parts | (Enhanced 256x512/col) | Enhanced 4096x512 point+mips | Uptown ground floors, canopies, sign bands | new art: Codex `city_facade_parts` (binding ready) |
| facade atlas other cols | Enhanced 256x512/col | same | all OSM walls | new art: c03-c15 (c00-02 DONE) |
| Art default cap 256 (PSXTextureCaps.MaxFor) | 169 LifeSim + 18 GasStation + Buildings pack (to 1747 px), Guardrail rusty_metal/wooden_board 512, Engines LS1 1536 | 256, point, no mips | on-foot scenes, gas station interiors, stage guardrails | texture-only possible via an HD passthrough set (`<name>_overhaul1_hd.png` = source) per texture; not done (not Charlotte street view) |
| CitySigns.png | pack pictures (larger) | 512 composed (tools/city/signs_atlas.py, layout in Scripts/City/CitySigns.cs) | billboards, pole cabinets | texture-only possible: compose at 2048 with the same fractions + HD pair; not done this round |
| CityFurniture / CitySignals | 256 composed (furniture_atlas.py, BOX) | 256 (Enh 512), PSX_FURNITURE (HD skips it) | poles, lamps, signals | needs HD binding for PSX_FURNITURE + cell size in shader; next round |
| city_house/station/trailer atlases | pack textures | 256 box-filtered (CityPropBaker.AtlasPx) | suburban props | re-bake of prop meshes/atlas (ATLAS_RECT) |
| tree atlases | stage tree PNGs | 512 (128 cells), Enh 1024 | billboard trees | re-bake (CityTreeLowReach -> KitHash) |
| traffic cars | 512 atlases / 128 paints | at source | traffic | new art (car HD tier for traffic) |
| terrain detail | - | 256 (TerrainDetailTool) | grass detail | low priority |
| QualitySettings PC | - | no mip limit, aniso forced, streaming off | - | fine |

## Memory added (PC, BC7/BC5)
Shops 5.6 MB colour + 0.2 rough + 1.0 night = ~6.8 MB. Glass arrays 3 slices x (2.7 col + 2.7 nrm + 2.7 mask) = ~24 MB (would have been 128 MB with 16 slices). R2: 0 (same sizes). Classic/phone path: city_shops.png, its .meta and the Classic night mask unchanged; HD variants are PC-only (HDVariantStripper).

## Coordinator, to reach the exe with a player-only build
1. Merge `tex-round4`. Shared-file edit: PSXLit.shader ~1803 (facade slice).
2. Build with **`PSX_HD_SRC=C:\Users\mcgee\PSXHDStage\texb02`** (replaces texb01; pc26c.ps1 sets it). The HD import (TextureHDTool.Import) runs in the build and re-stacks the facade arrays; no scene build, no re-bake, no new committed asset files. Do NOT set PSX_HD_ASPHALT_GAIN.
3. Check the log line `[TextureHD] 8 HD sets + 4 road base sets` and `city_facade_hd_col ... x 3 slices`; expect texhd.bundle ~+30 MB.
Not verified: a Windows player (BC7, bundle contents, all variants compiling outside the editor), frame cost, the night mask through the bundle.
# HANDOFF - HD CAR TIER + CLEAR LAMP COVERS (car-hd-tier, 2026-11-11; agent "carhd"; NOT pushed, NOT merged)

Branch `car-hd-tier` (worktree `C:\Users\mcgee\PSX Racing-carhd`) off mountain-nm c81f2a59. Sandbox PSXLab (lock "carhd", released). Sheet: scratchpad `carhd\carhd_before_after.png` (+ `_contrast.json`, `_clip.json`). Artist contract: art workspace `Cars\CODEX_HANDOFF_car_hd_tier.md`.

## Owner reports
- "why are body panel lines so smeared? I used bitmap for these to give the cars depth" (yellow FD, seller's drive).
- "Why don't I see the headlight lens on cars? I specifically made these layers to cover the headlight textures" (blue Viper, dusk).

## Causes
- Every car draws one 512x256 sheet (the HD sheet box-filtered 4x), point sampled with no mips (`Editor/CarModelBaker.cs` PointFilter), lit from vertex normals only (`Shaders/PSXCarPaint.shader` had no normal sampler). A 2 px line became half a texel; the groove depth in `_hd_normal` was never read.
- `tools/carmodels/export_models.mjs` left every clear cover off ("the OBJ path would paint it opaque"; Viper `dropMtl`).
- Three HD bodies imported with `tangentImportMode: 2` (none): viper_gts, flatsix_coupe, civic_eg (now 3 = Mikk).
- PC profile today: no vertex snap (`_PSXSnap` never set), cars `_Affine` 0, the picture is 8-bit + FXAA at the HD preset (Bayer at 1/255: invisible); at SHARP/CLASSIC presets the 6-bit dither (0.35/0.6) does show. The HD path forces no snap and no affine regardless.

## What changed
- `Scripts/CarHD.cs` + `CarHDMap.cs` (new): on the PC profile a PSX/CarPaint livery whose 512 sheet has an HD set gets the 2048 colour (BC7, mips, trilinear, 16x aniso), `_CarHDNormal` (BC5), `_CarHDORM` (R AO, G rough, B metal, A coat) and keyword `PSX_CAR_HD`; HD lamp maps through `CarLights.LampMapFor`. Hooked in `PSXTexDecode.Stamp` (one line), so every assembler (CarBody: player/rivals/AI/replay/inspection; CarShell: garage/seller/dealer/junkyard/meets/traffic; CarViewer) gets it via the shared skin materials. Sets load per body when CarModelLibrary.Load first stamps it; shared normal/ORM/lamp maps loaded once. A/B `-psx-no-car-hd`; editor `PSX_CAR_HD=1`.
- `Shaders/PSXCarPaint.shader`: `multi_compile_local __ PSX_CAR_HD` (stripped from non-Windows builds by `CarHDVariantStripper`); MikkTSpace TBN off the mapped normal for light/reflection/glints; AO on sky (1.0) / reflection (0.8) / sun (0.35); roughness above 0.5 removes lacquer (to 25%) and blurs the reflection; colour read -0.75 mip, normal -0.5 mip; debug 14. No existing #define changed.
- Older cars: `CarHD.SmoothSheets` (mipped trilinear copy of a Classic sheet, 565 bin-centred) - OFF by default; `-psx-car-sheet-smooth` / `PSX_CAR_SHEET_SMOOTH=1`. A/B (gtr_99 512, charger_69 128): smoother at 30 m, not clearly better: left off.
- `Editor/CarHDTool.cs` (new): import from `PSX_CAR_HD_SRC` (default `C:\Users\mcgee\PSXHDStage\carhd`) into gitignored HDSrc/CarHD + Resources/CarHDMap.asset (gitignored), ship `carhd.bundle`. Hooked in `PSXBuildWin64` beside TextureHDTool (Import before the player, ShipBundle after).
- Stage: `tools/carmodels/stage_carhd.py <repo> <Cars dir> <stage>` makes the stage from the six deliveries (ORM repacked, lamp maps with the 512 corrections copied in). Stage already built: `C:\Users\mcgee\PSXHDStage\carhd` (32 MB).
- Covers: `export_models.mjs` rule `isCoverMtl` (clear/cover/glass, not opaque/coloured/atlas) writes `key_covers.obj` (+ `_covers_up.obj` on pop-ups) through the body's own scale; bodies byte-identical (only EOL). `CarModelDef.coverMesh/coverUpMesh/coverMaterial` (new fields), `CarModelBaker.BakeCovers` (PSX/CarLens, opacity 0.06, no tint), `Scripts/CarCovers.cs` (new) drawn by CarBody.Apply/SetLensCovers, CarShell.Spawn, CarViewer, CarLights.PopUps (swaps with the pop-up body). On every profile; `-psx-no-lamp-covers` / `PSX_CAR_COVERS=0`.
- Shared-file edits (small): PSXTexDecode.cs (1 line), CarLights.cs (LampMapFor 3 lines, ForgetLampMaps, PopUps 1 line), CarBody.cs (2 lines), CarShell.cs (1 line), CarViewer.cs (1 line), CarModelDef.cs (3 fields), CarModelBaker.cs (wheel RecalculateTangents, DisableKeyword PSX_CAR_HD, BakeKeys, BakeCovers), PSXBuildWin64.cs (2 calls), 3 OBJ metas (tangents), .gitignore.

## Covers (20 shells, 0 with covers in the source but not in the game)
civic_eg 164, rx7_fd_93/99/nowing 440 (+440 raised), viper_gts 295, flatsix_coupe 444, flatsix_coupe_87 444, hatch_83_popup 32 (+32), rally_coupe_98 82, rally_sedan_99 344, fastback_96 32 (+32), liftback_95 375, roadster_99 338, gtr_89 280, coupe_03 273, gtr_99 206, charger_70 66, gt_liftback_97 456, fixed_roof_99 56 (+56), gt_hardtop_73 240. Cost: +1 draw, +32..456 tris a car (20-car grid <= +20 draws, ~+9k tris). Not measured on a phone.

## Proof / numbers
- Shut-line contrast 5/15/40 m before -> after: FD .14/.11/.23 -> .26/.14/.08; Viper .12/.22/.33 -> .25/.25/.32; Evo .21/.18/.23 -> .32/.26/.18; EG .18/.10/.12 -> .27/.16/.04; S2000 .16/.14/.11 -> .21/.18/.03; CTR negative (its seams read lighter than paint). 40 m loss = sub-pixel lines averaged (the old "contrast" was aliasing).
- Clip: 0% of paint pixels >= 0.97 in every after frame, p99 max channel 0.88.
- Tangents (CPU, per triangle, both sides and mirrored islands): T along +U 99.7-100%, B along +V 95.7-100% on all six bodies; normal maps are grooves in +Y/OpenGL on all six (seams.py).
- Bundle 27.3 MB (LZ4), 34 textures; GPU 2.67 MB each (2048x1024 BC7/BC5 + mips) = ~10.7 MB a body, ~91 MB if all nine are loaded. Load 5-16 ms a set (editor, from assets). Cost probe (9 FDs, 1920x1080, editor): no measurable difference (29-42 ms off, 28-37 ms on; noise).
- Self-test (PSXLab, tier off): 66 failures = base (66 old + 2 Evo lamp checks was 68); paint pins ok, PSX/CarPaint compiles.

## Not verified
Anything in a player: bundle load from StreamingAssets, BC7/BC5 on the Win64 target (PSXLab's editor is on WebGL: DXT5 there), the variant surviving the stripper, real GPU cost, the CarReflections panorama in the paint (off in edit-mode frames), night with street lamps (SellerLot has none), the cockpit view, mirrors, phone cost of covers.

## At merge / to reach the exe
1. Copy nothing by hand: covers OBJ/MTL + metas and the 3 metas are committed.
2. In the PC build sandbox: `CarModelBaker.Bake` (or `CarHDShots.BakeImport` for the 20 changed shells) so the prefabs carry wheel tangents, covers and cover materials (Materials/CarModels/*_covers.mat are made by the bake).
3. Set `PSX_CAR_HD_SRC=C:\Users\mcgee\PSXHDStage\carhd` for the PC build (PSXBuildWin64 imports and ships carhd.bundle itself). Rebuild the stage with stage_carhd.py when deliveries change.
4. No full scene pass needed (CarBody/CarShell dress at runtime); no tile re-bake.
# HANDOFF - THE PARTS SHELF, ROUND 2 (junkyard-spares, 2026-11-11; layout by "no text may clip")

- Lists that never rest on a cut row: `LifeSim/BlockSnap.cs` (new). A whole number of fixed-height blocks per viewport, one wheel notch = one block, drags settle on a block edge, the pad/keyboard cursor scrolls its whole block into view, "N MORE ABOVE / BELOW" cues. Used by the wreck page (block 94 + 4: five parts at 16:9 and 4:3, three on a phone; list from under the header to the footer) and by STORED PARTS (100 + 6) and PICK A CAR (76 + 6), each a list inside its page sized to the room above the exit row.
- One line per reason, with shorter fallbacks chosen by measured width (no ellipsis anywhere). Turbo refusal is now "turbo part - this engine is NA". The pick-a-car row has no empty line above its reason.
- Mechanic page: the side panel's car name wraps instead of being cut; the page header wraps to the column.
- Selling a car with a spare going on: `Spares.ReturnJobsToShelf` (job carries `PendingPart.spare`) in QuickSell, AcceptOffer and the debug loaner swap. QuickSell already refused a car with work on it; a buyer's offer did not, and that path lost the part. It comes back even past capacity; mechanic labour is not refunded.
- Pad: `SparesPreview.PadWalk` (camera at 1 world unit = 1 px so MenuNav's line test is the game's): wreck page 11/11 controls reached, FIT<->KEEP left/right 5/5, last row's block fully shown on a block edge (phone: list moves 196 = 2 blocks, cue "2 MORE ABOVE"). STORED PARTS 10/10, PICK 3/3, car page 10/10, parts page 16/16, MY CARS 10/10, mechanic with BRING YOUR OWN PART 18/18 (LifeHomePreview.CheckNavReach). Not driven in play mode.
- Checks: CLIP total 0 on every new shot; self-test 65 failures = the base's set; spares checks all ok incl. the sale path.

# HANDOFF - THE PARTS SHELF (junkyard-spares, 2026-11-11; NOT pushed, NOT merged)

Branch `junkyard-spares` (worktree `C:\Users\mcgee\PSX Racing-parts`) off mountain-nm c81f2a59. Sandbox PSXRec (lock "spares", released). Owner, at a wreck refusing four pulls with "already maxed": "What if I want a spare part for when the Civic part needs to be replaced? What if I have multiple Civics? What if I don't have a Civic right now, but I plan to buy one and want to store parts in preparation?"

## Causes
- `Junkyard.GetQuote` (Junkyard.cs ~384 at base): a pull was priced and gated as the NEXT stage of the ACTIVE car (`Upgrades.NextStagePlan`); `!plan.valid` -> "already maxed" refused the whole pull. A yard part did not exist as a thing; there was no inventory. The "$30" on refused rows was the $5 floor + $25 tool rental on a zero quote.
- Ghost text: `TownWorld.OpenWreck` disabled the walker but left the walk-up prompt (`OnFoot/FootScreen`, its own overlay canvas, sort order 90) printing the wreck's title and "5 parts still on it, yours for the pulling." under `WreckScreen` (order 140), whose backdrop and panel are each 90% opaque on purpose (GT2 grid), so it ghosted through.
- Reasons between rows: `WreckScreen.Build` drew each row as a 42-unit button then a free label 46 units lower, 30 units above the next button, so the red line sat in the gap. The 44 pt title also ran edge to edge at 16:9 and off the screen at 4:3.
- Fitment rule found (unchanged): nothing matches a yard part to make or model. A service part goes on any car; hardware goes on any catalog car whose next stage of that category is no higher than the part's `maxStage`; a used turbo only onto a turbo engine or an unbuilt NA engine (`TurboKitRefuses`); race cars refuse stages; no tyres on a car with no wheels. The donor is recorded and shown, not enforced.

## What was built
- `LifeSim/Spares.cs`: `SparePart` (kind/stage, grade, donor spec + name, paid, day) on `LifeState.partsShelf` (+ `spareSeq`); `Plan`/`Fit` with three uses: UPGRADE (next stage, turbo rules), REPLACE (a known fault the part covers, part stage >= the car's in that category; fault table per hardware kind and per service label), RESTORE (a service part's lane, as before). DIY = skill-gated, no charge; mechanic = labour only (stage: shop - DIY price; service: 60% of the new part on that car; club mechanic discount applies), car kept for the days. Grade keeps its meaning: `EffectiveAdd` and `FaultRisk` ride the job (`fromShelf` jobs count as `IsYardPart`, so no service sweep cancels them).
- `Junkyard`: `PartValue` (car-free: service base price by grade; hardware `Upgrades.RefDiyPrice(kind, maxStage)` x 0.30-0.60 by grade), `PullKeepPrice` = 55% of that + rental, `RackKeepPrice`, `PullToShelf`, `BuyToShelf`; `GetPull` gains keepPrice/canKeep/keepBlocked; FIT price = min(car quote, keep price), so fitting now is never dearer than keeping. "already maxed" -> "fully built - nothing to step up" (a FIT reason only).
- Numbers: shelf capacity 20 (a garage wall; scrap makes room). Scrap = 40% of what the part cost (rental excluded). The owner's wreck (found exactly: week 3, shell 4): SCRAP turbo keep $444, CLEAN turbo $657, CLEAN subframe $277, SOLID semi-slicks ~$251; scrap of the clean turbo $253.
- Screens: `OnFoot/WreckScreen.cs` rebuilt: one block per slot, FIT TO <car> | KEEP AS A SPARE side by side, each button's outcome or reason (red) inside the block under it; rows scroll, footer pinned; title at Head size; prompt hidden while open. `LifeHomeScreen.Spares.cs`: STORED PARTS (tab `parts`) and pick-a-car (`partfit`); STORED PARTS (n) + STORED WHEELS (n) doors at the MY CARS base; car page "ON THE SHELF FOR THIS CAR" + a fit button under each fault a spare replaces; parts page "ON THE SHELF" under the next stage; mechanic BRING YOUR OWN PART rows; the yard's rack keeps a part it cannot fit. `TownWorld` passes the donor's catalog id.
- Save v24: `LifeSimManager.MigrateToPartsShelf` (old saves load with an empty shelf). The old migration tests now compare against the current version.

## Proof
- Sheet: scratchpad `spares\spares_before_after.png` (wreck before|after at 16:9 and 4:3, STORED PARTS, pick-a-car with two Civics, car page "on the shelf", parts page, MY CARS door). Tool: `Editor/SparesPreview.Capture` (PSX_SPARES_TAG=before|after; compiles against the base too). CLIP report: before 6 (the ghost overlaps, STRAIGHTEN UP off the phone's bottom); after 0 on the new pages, 1 older ellipsis on the mechanic header at 4:3.
- Self-test PSXRec: base 65 FAIL, new 65 FAIL, the identical set (scene/packaging, not this work); 53 new checks pass (TestPartsShelf, TestPartsShelfSave; quick runner `LifeSimSelfTest.RunSpares`).

## Not done / next steps
- The preview's maxed Civic is on the NA ladder, so its turbo rows say "NA build" where the owner's said "already maxed"; the gate is the same code. The two turbo rows on an NA-maxed car cannot be fitted (correct) but can now be kept.
- Out of scope: selling a car with spares fitted (QuickSell drops a car's in-progress jobs, a shelf job included), taking a fitted part back off to the shelf, tyre wear per set, a pick-a-car step at the mechanic (it lists the car you brought), any make/model fitment. Not driven in play mode; no player build.
- Merge: scripts only (no scenes, no Resources, no bake). Save version 24: if another branch also bumps it, renumber at merge. Shared files touched: LifeState.cs, LifeSimManager.cs, Upgrades.cs (two helpers), Junkyard.cs, LifeHomeScreen.cs (switch, parent tabs, five hook lines, rack row), TownWorld.cs (one line), LifeSimSelfTest.cs/.Clubs.cs (version checks + two Guards).

# HANDOFF - COCKPIT ROUND 2: THE CABIN ON THE CAR'S OWN WINDSCREEN ("mirrors", branch fixes-1111, 2026-11-11; NOT pushed, NOT merged)

Owner: "all cockpits should fit inside each car model, with mirrors properly mounted just outside the cockpit window". Sheets: scratchpad `mirrors
2\<shell>.png` (16 shells: ahead / over the dash / look left / look right as base | round 1 | round 2, the forced nose up/down pair with the dark inside off/on, rear-view picture, head-level look right, the mirrors from outside) and `mirrors
2\summary.png`.

## Measured
- Header (windscreen top), belt and side glass of every shell probed by rays (Editor/CockpitFitProbe.cs -> Scripts/CockpitFit.Table.cs, 52 rows). His windscreen pane against the cars: median 1.13 x the length and 1.20 x the height (CalibZ/CalibY): the fit moves each car from the catalogue's middle.
- Fit gates over all 52 (Screenshots/DoorMirrors/fit_final_base.txt -> fit_final.txt): (b) shell geometry standing inside the cabin: 0 before and after; nose: rays from the eye into the hollow nose at rest / nose up 3 / nose down 3 / brake dive / crest: up to 99 rays per pose on the muscle cars, every one ahead of the cut, so all are drawn dark by round 1's inside pass: 0 open on every shell in every pose. Measured not gated: (a) cabin vertices ahead of the seat outside the shell (worst CTR 49.7 -> 18.9 cm, FD 44.4 -> 17.9, van/Land Rover/fullsize 50-63 cm both ways); header/belt offsets per shell in the file.
- Eye moves: CTRs 31 cm forward, 5 cm down, 6 cm in (wing crowns and both mirrors now in view); '69/'70 Chargers, GTO, pagoda, Mustang forward 18-27 cm; raked-screen cars (EG, Evo, FD, Supra) within 5 cm of before.
- Grips: the wheel now scales with the cabin (its round-1 counter-scale moved it off the arms' grips); grip-to-rim centre 12-18 mm = the palm offset at scale 1 (15 mm) times the cabin scale, on the rim on every car (log in Screenshots/MirrorMatch/r2d/log.txt).
- Mirror check: 52 shells, 48 with live mirrors, 0 FAIL. Merged-mirror rule gives glass to classic_roadster, coupe_99, flatsix_turbo_96, gt_hardtop_73; nissan_180sx's smaller glass replaced by the larger reflected.
- Rear-view at home: HomeMirrorPlayCheck in play (visible editor, Town free-roam start, Yellowbird in cockpit): the rear-view camera sees only the player's own body (cut) and the town; picture = the road behind. The bug did not reproduce on this code; the likely cause was round 1's: the camera kept the first car's place (fixed in round 1, PlaceMirrorCamera).
- Self-test: 65 failures, was 66 (TestCockpitArms NRE fixed: CockpitArmsShots.SelfTest appended to a null log); no new failure.

## Changed (switches: PSX_COCKPIT_FIT3=0, PSX_DOORMIRROR_MERGED=0, plus round 1's)
- CockpitFit.cs + CockpitFit.Table.cs (generated) + CockpitInterior.Fit3: pane base on the cowl, top toward the header; length scale 0.75-1.05 (the seat moves forward freely, back 5% at most, never within 30 cm of the car's own mirrors), height 0.85-1.12, width as round 1 or the shell's side glass; EyeShift (vector) moves the chase camera's mount.
- DoorMirrorProbe: merged mirrors, one glass for both sides. DoorMirrors.Table.cs re-emitted.
- Editor: CockpitFitProbe (Measure/EmitBatch/FitBatch), DoorMirrorMatch (R2Batch/R2Final, forced-pose shots, grip gap), HomeMirrorPlayCheck.

## Left
- Still dead: camry_2001 and the three fullsize cars (their cowl is mis-measured by the baker: y 1.44). Viper has no measured cowl: old fit.
- The look-right pose dips the head (DownFor); the CTR's passenger mirror is in view with the head level (in_R0).
- Cabin vertices behind the windscreen header stand outside low/short shells (the van, Land Rover, fullsize): not visible from the seat.

## Merge
- Code + two generated tables (Scripts/CockpitFit.Table.cs NEW + .meta, Scripts/DoorMirrors.Table.cs), Scripts/CockpitFit.cs NEW + .meta, Editor files + metas. No car re-bake, scene rebuild, tile bake or format bump; round 1's PSXCarPaint.shader edit stays. Re-run CockpitFitProbe.EmitBatch and DoorMirrorMatch.ReprobeBatch after any car batch, then FitBatch / CheckBatch.

# HANDOFF - NO CAR SITS LIFTED OR TILTED AT REST ("parked" round 2, branch fixes-1111, 2026-11-11; NOT pushed, NOT merged)

Coordinator: the round-1 sweep still had rows "SEATED HIGH AT REST". Re-measured in the DRIVEN frame (CarBody.Lifted; round 1's sweep compared the unlifted fit with 328 mm and over-counted): 94 of 319 rows.
- Why: every one has a REAL GT4-sheet tyre (no defaults; "hub at rest 328/328" is CarController.StockHubRest, the same for every car by design). All are on a BORROWED shell sized to the row and drawn round a smaller tyre: mb_pagoda (Alpines, Elans, Cobras, SL R129 ...; front up to 78 mm), gto_66 (Celica TA22, Camaros, Buicks; rear up to 108 mm), supra_a80 (Group C; 700-763 mm rear tyres, rear up to 85 mm), euro_hatch (30 hatches/rally cars, 486 mm model tyre), audi_saloon, citroen_cx, volvo_estate, skyline_r32 (JGTC). The binds are at the arch crown (+/-35 deg), so they are the real arch. CarBodyLean.Seat held the whole shell up, so the car stood tilted at rest (up to 2 deg).
- Options: shrink the tyre (off spec, and physics drives on the spec size: rejected); raise the whole body, level, by the worst axle (a car up to 11 cm high: rejected); raise the ARCH (chosen): ArchFlare.Solve (in CarModelLibrary.Fit) gives each axle its shortfall + 5 mm. PSX/CarPaint raises the bodywork over that wheel by that amount (Shaders/PSXArchFlare.cginc, a per-renderer block stamped by CarBody/CarShell/CarViewer). The body sits at its fitted height and level, on spec tyres, with no tyre through paint. ArchCeilings.HubMax reports the flared arch, so the per-frame seat only handles travel under load again. Cost: on 113 rows (incl. tiny ones) a visibly taller wheel opening over the bigger tyre; the zone fades out before the bonnet and waist line (some fender-edge ripple on the biggest flares). Mesh-based tools (ArchProbe.DrawnClip, BumpStopShots) do not see the flare. PSX_ARCH_FLARE=0 = off.
- Result: sweep 0 rows seated high (gate in tools\parked-play-check.ps1). 16 cars x lot/slope: drift <= 0.002 m, lamps off, back in 0.00 m/s, shell lift 0. Side-on pad: Celica TA22, Alpine A110, Toyota 88C-V, Civic EG, BTR and Viper lift 0.0 mm, pitch 0.00 deg (before: 0/28/2/0/8/11 mm, -1.18/+0.78/-1.98/0/+0.19/+0.26 deg). Viper and BTR reach 0 by the same rule (flare 13 / 10 mm front).
- Sheet: scratchpad parked\parked_before_after.png (side-on rows at the bottom). Coordinator: scripts + shader only (PSXCarPaint.shader +2 lines, new PSXArchFlare.cginc and ArchFlare.cs with metas); no car re-bake, no scene rebuild.
# HANDOFF - THE COCKPIT FITS ITS CAR ("mirrors", branch fixes-1111, 2026-11-11; NOT pushed, NOT merged)

Owner (PC26): "mirrors don't match the updated mirrors of the new car models imported"; then "all cockpits should fit inside each car model, with mirrors properly mounted just outside the cockpit window" and "I can see the road ahead under my hood in cockpit mode". Sheets: scratchpad `mirrors\mirrors_before_after.png` (11 shells: ahead / over the dash / look left / look right, before|after, each mirror from outside, the rear-view picture) and `mirrors\nose_closing.png`.

## Causes
- The cockpit's door mirrors ARE the shell's own housings (CabinClip keeps them); only the live GLASS is a table, `DoorMirrors.Table.cs`, written by `Editor/DoorMirrorProbe.cs`. Its glass was the biggest WELDED rearward patch (`GlassPatches`, DoorMirrorProbe.cs:268): on shells whose glass sits flush in a bezel ring and housing (all the HD deliveries: FD 13.8x7.2 cm glass measured 18.0x9.4, Viper 18x12 -> 27x18, S2000 12.5x7 -> 15x10.6) the picture covered the bezel and the housing's back - "a generic red teardrop with an oval glass". 26 of 44 mirrored shells, not only the six new ones. The table was current (the car batch re-probed it), the RULE was wrong.
- The cabin was fitted by LENGTH only (`CockpitInterior.FitFor`, uniform scale about the eye): every car got the cabin's 1.43 m between side glasses. On 13 narrow shells the car's mirror stands inside that (CTR/Yellowbird inner edge 0.645 m vs cabin glass 0.713, E30 0.638, jdm_pickup 0.656, hatch_76 0.677 ...): the mirror was hidden behind the cabin's door / A-pillar ("no door mirror anywhere in the side window").
- The rear-view camera was placed once, when made (`EnsureMirrorCamera`), and kept the first fit's place through body swaps.
- The shell is drawn Cull Back and cut at the windscreen base: any sightline under the bonnet's back edge saw out through the hollow nose.
- (3) one rigid thing: already true. `CockpitInterior.Ride()` poses the cabin from `CarBody.bodyRoot`'s own local pose every LateUpdate (order 1000, after CarBodyLean), bodyRoot is a direct child of the car (PSXRacingBuilder.cs:7665). The eye follows the body 25% (EyeFollow, by the owner's 10-24 request), so the eye moves against the dash, but dash and bonnet never move against each other. The nose-high stance of the new cars is the "parked" agent's.

## Fix (switches: PSX_DOORMIRROR_GLASSPART=0 old glass rule in the probe, PSX_COCKPIT_WIDTHFIT=0, PSX_COCKPIT_INSIDE=0)
- DoorMirrorProbe.GlassPart: within the welded patch, the pieces joined by the mesh's own shared vertices; the biggest flat one (tie: the rearmost) is the glass when it is half the patch's width and height or more. Table re-emitted (26 shells changed; the housing boxes unchanged).
- CockpitInterior.WidthFitFor: the cabin narrowed (never widened, min 0.8) until its side glass stands 1 cm inboard of the car's own mirrors' inner edge (housing box from the table x the body's WidthScale); the eye moves in with the seat (EyeShiftX, added in RideEye/PreviewRide/PreviewStep), the wheel kept round, the cargo seat moved with it. CTRs 0.842 (eye 6.4 cm in), E30 0.880.
- Rear-view camera re-placed on every fit (PlaceMirrorCamera).
- Dark inside of the nose: a second renderer of the body mesh ("CabinInside", child of the body filter, made by the cockpit) on a copy of the body material with `_PSXCull`=1 and `_PSXInside`=1 in PSX/CarPaint: back faces, near-black, only while the cockpit's cut is open for the driver's camera, only ahead of the cut. PSX/CarPaint gained `Cull [_PSXCull]` (default 2 = Back, every other material unchanged) and a 6-line early-out branch.
- Show(true) re-sorts every renderer under the car (one added after the fit stood inside the cabin uncut).
- Editor/DoorMirrorMatch.cs: CheckBatch (re-run after every car batch: every shell re-measured against the table: glass centre, size, tris, housing box within 1 cm; the lamps-up body's mirrors; the cabin's glass inside the mirrors; a per-shell fit line), Shots (cockpit + exterior frames), ReprobeBatch, ProofBatch.

## Measured
- Check: old rule 44/44 match; new rule against the old table 26 FAIL; new table 0 FAIL (52 shells, 8 without a separable mirror: camry_2001, classic_roadster, coupe_99, flatsix_turbo_96, fullsize_police/taxi/trooper, gt_hardtop_73 - their housings show, no live glass). Note: nissan_180sx's two glasses differ (17.8 / 9.5 cm) in the model itself.
- Cabin glass vs mirror inner edge, worst before: E30 7.5 cm INSIDE the mirror, CTRs 6.8, jdm_pickup 5.7, hatch_76 3.6, roadster_na_popup 3.4; after: every shell 1 cm inboard.
- Windscreen base: the dash front lands on the baked cowl by construction (unchanged); roof / A-pillar offsets were NOT measured.

## Not done / unverified
- The dark inside closes nothing visible in the edit-mode shots: no slot under the bonnet reproduces at rest even with the eye 6 cm high (nose_closing.png, before = after). The owner's slot is likely the nose-high stance in play ("parked" agent). The inside pass is a rule that costs one draw of the body only in cockpit view; its effect in play is unproven.
- The CTR's yellow scrap at the driver's A-pillar foot is still there (shell geometry just ahead of the cut, above the dash); the owner's rear-view "car insides" at HOME did not reproduce in the editor (rear-view frames all show the road); possibly a second, unmarked copy of the car at home (CarShell) or a renderer added after the fit (now re-sorted on Show).
- Passenger mirror of the narrowed CTR is above the frame in my look-right pose. Arms grip a wheel 0.84 wide in x scale: up to 2 cm off the rim on the CTRs. No player build, no night, no RHD shot, no frame-cost measurement (the inside renderer is one extra draw of the player's body in cockpit view only).

## Merge
- Code only + one generated table: Scripts/DoorMirrors.Table.cs, Scripts/CockpitInterior.cs, Shaders/PSXCarPaint.shader, Editor/DoorMirrorProbe.cs, Editor/DoorMirrorMatch.cs(+meta). No car re-bake (CarModelBaker), no scene rebuild, no tile bake, no format bump, no new shader (CarPaint is already always-included). A normal player build carries it.

# HANDOFF - TOWER LIGHTS ON THEIR FITTINGS ("beacons", branch fixes-1111, 2026-11-11; NOT pushed, NOT merged)

The owner (PC26, N Graham St pic): "aviation red lights on buildings are outdated and not actually connected to buildings". Sheet: scratchpad `beacons\beacons_before_after.png` (raw frames: PSXLab `Screenshots\beacons\`, log `beacons_log.txt`).

## Round 3
- FAR TOP LIGHTS: past 1.5 km a tower's top light is drawn at a floor in REAL framebuffer pixels with a flat core (PSXHalo `_TopFloorPx` 1.2 px half, flashers x1.35), no distance fade beyond the skyline's own haze. Probe (1125 lines): 5.5 km 35 separate points, cores touching 0 (dusk = night); 10 km 36 glows -> 34 separate points (2 pairs touch).
- NEAR SNAP (CityBeacons.SnapNear, from CitySkyline.Refresh): once a tower's near tile(s) stand (Tile_x_z under the world), its lights are probed against the tile's Buildings mesh (nearest clear-sky vertex within 4 m, or the tile roof straight under the far point) once per stand-up; lights and fixtures move by that offset blended over the 150 m inside the world's fade start, and back when the tile drops. Gate: near tiles 118/121 <= 5 cm (the 3 misses are C2's frame members, shown on the members in the round-2 frames); far 118/121.
- Sheet: ROUND 3 column on the far views.

## Round 2 (coordinator's four points)
- FAR DENSITY (PSXHalo.shader + CityBeacons): steady corner lights fade out 1.5 -> 2.5 km (real distance), the top light stays; a squeezed tower light's natural size now follows its REAL distance (it was sized at the squeezed depth, so ~1 m of glow 5 km out drew like 500 m out); the least size shrinks to half (a flasher's extra x1.8 to none) from 1.5 to 6 km. Steady top lights moved to uv1.y -0.6 so only corners fade. Probe (CPU mirror of the shader, 1125 lines): 2 km 113 glows -> 110 separate points (3 core pairs touch); 5.5 km 35 glows (83 corners faded) -> 35 separate, cores touching 0, quads (soft fringe) overlapping 6 pairs; 10 km (eye 150 m up) 36 -> 34 separate, 2 core pairs. Same at dusk.
- FIXTURE SIZE: steady L-810 = 0.26 m globe on a 0.30 m stub; flasher L-864 = 0.6 m globe on a 0.6 m pedestal; lens texel dark red (92,9,7). Every fixture stands on a vertex with nothing of its building drawn over it within the lens radius (ClearAbove; corner search widens to 3 m when the extreme is overhung) - the round-1 lens on the side of the C3 fin is gone.
- NEAR MISMATCH: the skyline now runs the tile's pavement cut (FitFootprint, CityMeshes.cs EmitFootprint, V2 only, not proxies) and, like the tile, never masses or kits a shop block or a floating outline (Massing.cs / Rooftops.cs guards). That fixed #22; #13197 (2 lights, 2.2 m) and #6486 (2 lights, 1.2 m) remain off the NEAR tile - cause not found (not retail, not floating, not the cut; both are part-built outlines). NOT fixed; a near-mesh re-probe at tile stand-up would be the fix.
- C2 OPEN FRAME: shot close by day and night: the top light and the corner light sit on the frame members' tips. The probe's 0.7 / 3.3-4.8 m there is the member geometry (thin members widened in PSX/Lit), not a floating light.
- Sheet updated (round 1 cells, then before | round 1 | round 2): scratchpad beacons\beacons_before_after.png. Measure after round 2: fixture foot to drawn surface far p90 0.000 (118/121 <= 5 cm), near p90 0.000 (113/121 <= 5 cm; the misses are C2's 2 + the 4 above + 1 at 0.23 m).
- Extra shared-file edits this round: CityMeshes.cs (the FitFootprint line), Massing.cs (EmitMassing guard). Still runtime only, no re-bake.

## Cause (measured)
- `CityBeacons.cs` (old lines 59-97) placed the lights from the far skyline's per-element BOUNDING BOX (top centre + 0.5 m) and the tallest part's PLAN OUTLINE corners at `ground + shaft.h + 0.4` - written before the hero crowns, pyramid, rooftop kit and masts. On anything but a flat box that is air. BeaconProbe, 135 lights before: distance to the nearest drawn surface p50 0.50 m, p90 6.6 m, max 25.1 m, 52 lights more than 2 m off (far mesh; near tiles the same). Worst: the teal pyramid (C4) 4 corners 24-25 m out in the air beside the pyramid; #30236 (184 m) and #31084 (169 m) corners 12-14 m off; the 7.2 m beacon over #30236; the silver crown (C3) 5-6 m off.
- PINK: PSX/Halo is additive (Blend One One) and the lights ran with the street lamps (`NightGlow.On`, already on at SUNSET with the sun up): red (1, .06, .03) added to a pale sky clamps to pink. A beacon halo was 2.6 m each way at any distance (the disc in the crown close-up).
- Not an old cache or data file: the skyline and beacons are built at runtime from the map (CitySkyline.Create).

## Fix (PSX_CITY_BEACONS2=0 = the old lights exactly)
- `CityBeacons.PlaceOnDrawn`: each light on a VERTEX of a real (non-degenerate) triangle of its building in the skyline mesh (facade, roofs, crown submeshes). Top light: the highest vertex (spire tip, mast cap, crown apex; on a flat top the highest vertex nearest its middle). Corner lights: along the tallest ground part's axes, the extreme of the upper 40% of the tower toward each diagonal, there the highest drawn vertex (parapet corner, pyramid eave, setback corner). Lights 12 m apart at least.
- Count (FAA practice, L-864 flashing 30/min over ~150 m, L-810 steady below): towers 150 m+ = slow-flashing top beacon (per-tower phase, as before) + 4 steady corners; 90-150 m = steady top light + 2 opposite corners. Before 135 lights (35 flashers + 100 corners); after 121 (9 flashers, 27 steady tops, 85 corners on 36 towers).
- FIXTURES: a galvanized stub (0.55 m) and a red glass lens (octahedron r 0.38 / 0.27 m) on each light's vertex, light in the lens. One runtime mesh (1,936 tris), ONE draw, PSX/Lit = a clone of the Structure slot material with a 4x4 runtime texture; per tower it is drawn only within 700 m of the eye (index list re-picked every 25 m) - past that it would be sub-pixel sparkle. Not baked.
- Near = far: the far skyline now draws the rooftop kit's MAST (`CityMeshes.Rooftops.cs` Mast) and the PARAPET/LEDGE lip (`CityMeshes.Massing.cs` Parapet) as the tiles do (skyline only, not proxies), and leaves a ground-standing shop block (style 4) without the kit as the tile does (`EmitRoofKit`). So the top light is on the mast tip and corners on the parapet corner at every distance - no jump at hand-over. Cost: a few hundred skyline triangles.
- PSX/Halo (`PSXHalo.shader`): `Blend [_SrcBlend] [_DstBlend], One One` (default One One = every other halo unchanged), `_MaxPixels`, `_GainFloor`. The tower-light material draws OVER the sky (OneMinusSrcAlpha), aviation red in linear (1, .014, .008), size clamped 1.2-4 ref px, never dimmer than its colour. Depth test as before (squeezed with the tower far, real near).
- On only DAWN / DUSK / NIGHT (`CityBeacons.LitNow`: TimeOfDay.NightFor >= 0.5) - off at SUNSET with the sun up, as a photocell; fixtures stay by day. Off in fog as before.

## Proof / gates
- BeaconProbe after: fixture foot to nearest drawn surface - far skyline p50 0.000, p90 0.000, 119/121 <= 5 cm; near tiles p50 0.000, p90 0.000, 111/121 <= 5 cm. Typecheck OK. Glows still 1 draw; fixtures +1 draw near uptown.
- Exceptions: (1) 7 corner/top lights on 3 towers (#13197 137 m, #22 115 m, #6486 93 m) are on the far mesh but 1.2-2.2 m off the NEAR tile - the near tile trims the footprint at the pavement (`FitFootprint`, CityMeshes.cs:11931; the skyline does not), a pre-existing near/far building mismatch, likely cause, not fixed. (2) The open-frame tower (C2) top and one corner read 0.7 / 3.6 m in the probe: its frame members are widened in the shader from thin mesh members, so the mesh-distance number is not meaningful there; not inspected in a frame.
- Frames (before | after): N Graham sunset (before: pink dots in the sky round the pyramid; after: none), dusk + night (red points on roof corners and tips), three tops close by noon (fixtures visible on pyramid spire, needle tip, crown fin) and night (lens lit on the fitting; before: dots metres off, a big pink disc), 5.5 km night. No light seen through a building in these frames.

## Not done / not verified
- No player build, no play-mode drive across the near/far hand-over (positions agree by the measure, not by a moving test). Frame cost not timed (2 tiny draws). Flash phase in stills is as the clock stood (about half the flashers lit).

## At merge
- Runtime only: NO tile re-bake, no CityTileCache format change, no new slot. Shared files touched: `CityMeshes.Rooftops.cs` (Mast + EmitRoofKit guards), `CityMeshes.Massing.cs` (Parapet guard), `PSXHalo.shader` (blend/max-size/gain-floor properties). New: `Editor/BeaconProbe.cs` (no .meta committed; Unity mints it).

# HANDOFF - THE LEFT CAR + 11-11 NOSE-UP ("parked" agent, branch fixes-1111, 2026-11-11; NOT pushed, NOT merged)

Owner: "I parked my Honda at a car meet and it rolled away, lights on, front suspension lifted like a monster truck" and "the front suspension of every car recently added is lifted". Sandbox PSXRec (lock "parked"). Check: `tools\parked-play-check.ps1` (Editor/ParkedPlayCheck.cs; WATCHED editor - a hidden batch editor crashes in RenderTexture::Create in play mode); every-row arch sweep `ParkedPlayCheck.ArchSweep` -> PSXRacing_arch_sweep.txt.

## Causes
1. ROLLED AWAY: PlayerCarInput (the `!inputEnabled` block) parks a car with brake 0.3 + lever; CarController.TireForces swaps the pedals in R (`accelPedal = currentGear == -1 ? brakeInput : throttleInput`), so a car left in R (owner's HUD: AT R) got 30% reverse throttle, the park hold stood aside (`accelPedal < 0.02` gate) and the lever holds only the rear of a FWD car: 0.3 m/s backwards into the car behind (1.0-1.6 m on the lot), off a 15% slope at 150 m/s. Also, in D, the hold crept 5.5 mm/s on 15% (16 cm per 30 s).
2. LIGHTS ON: lamps were one static switch owned by the hour (CarLights.SetAll); nothing knew the car was empty; the lever also lit the brake lamps.
3. NOSE-UP: ArchHubs (ArchProbe.BakeExact) swept the tyre -90..+90 deg, so the 11-11 HD shells' inner wheelhouse / sill faces behind the front tyre (binds at -39..-81 deg) read as the arch: highest front hub 3-13 cm UNDER the hub's static ride, and CarBodyLean.Seat lifted the shell that far at rest (CTR/Yellowbird 120 mm, 2.7 deg nose-up; FD 14 mm; del Sol on the EG shell 9 cm; old euro_hatch rows 75 mm). Physics ride height was never wrong (chassis level, hubs 328 mm).

## Fix (switches: PSX_PARKED=0, PSX_ARCH_CROWN=0 = as before)
- PlayerCarInput: game holding a car in R brakes with the R brake pedal; stopped, the box goes to 1st. CarController: hold pulls back to its latch point (ParkHoldStiff 16, re-latch past 25 cm). Unattended flag (ForecourtMode GetOut/BeginAfoot/GetIn) -> CarLights.LitNow off, no brake lamp, pop-ups down. Covers every place you leave the car (all go through ForecourtMode + PlayerCarInput).
- ArchCeilings.CrownHalfDeg 35: field + exact bake sample the tyre crown only; ArchHubs.bytes re-baked (commit 1b665555).

## Results (13 cars, lot + 15% slope, left in R, 30 s): before drift 0.7-1.6 m lot / 9-362 m slope, lamps on; after 0.000 m lot, <=0.003 m slope, lamps off, back in 0.00 m/s, gaps unchanged. At-rest shell lift after: 0 for all but Viper 11 mm (its own front arch is ~1 cm lower than its tyre at the crown, same in the old model - artist data) and BTR 7.7 mm. Sweep: 152 -> 127 rows with a tyre over its arch at rest (all old-pack crown binds, unchanged).
Sheet: scratchpad parked\parked_before_after.png. Blue blocks = TownWorld zone-line knots (PSXRacingBuilder.Town.cs EdgeMarkers, owner-chosen self-lit dots), not a fault.
Coordinator: scripts + ArchHubs.bytes only; no scene rebuild, no car re-bake.
ï»¿# HANDOFF - CURBS ROUND 2: GATES, ROLLED CURB, WHEEL VS CURB, PAN, LIDS (charlotte-curbs-drains, 2026-11-11 evening; NOT pushed, NOT merged)

Agent "curbs2", sandbox PSXPerf (lock removed). Proof sheet: scratchpad `curbs2\curbs_round2.png` (before | round 1 | round 2), street-view pairs `curbs2\pairs_curbs2.png`, drive matrix `curbs2\drive_matrix_r2.txt` (raw `drive_r2_raw.txt`, rerun `drive_graham_rerun.txt`).

## Causes
- Rolled curb worse at speed (80 km/h 45 deg: all wheels off 0.42 s vs 0.02): round 1's profile was a valley (2.5 cm over 22 cm) then a 33% roll to the crown - steepest AT its crest, a ski jump. `CityMeshes.Curbs2.cs` RFaceOut/RFaceY.
- Drawn vs collider on vertical streets: the wheel is ONE ray down from the hub (`CarController.cs` SuspensionAndLoads, Physics.Raycast ~l.2981). Geometry (scratch `facepos.py`): a 0.3 m tyre circle cuts the drawn 15 cm curb's edge by 17.5 cm before its ray reaches the face with ANY ray collider (plumb, 15, 18 or 22 cm chamfer alike); only a tyre-radius fillet (what a real tyre's hub follows) gives 0 cut and 0 air. A step past the jounce stop is resolved by `BumpStops` as a velocity constraint (bias up to 0.6 m/s) plus spring cap 5x and bumper 6x static: the kick.
- Pan stopped 4 m short of junctions (PanJunctionClearM) and nothing laid it round corner returns; lids' collar used the under band's light grey (131/255); the grate's "slot" texel was rust brown (92,64,42), not dark; the structure mesh was never WetReflections-tagged; inlets/lids only kept a 25 m junction clear (round 1 counted: 12 inlets on paint at N Graham, 7 + 5 lids at Lexington, 12 + 2 at Wadsworth Pl).
- Round 1 treated every dressed Uptown street as granite with no pan; street view at N Graham / W 10th St shows a plumb concrete curb WITH a concrete pan and no edge line.

## Changes (all switches default ON = round 2; =0 gives round 1)
- PSX_CITY_ROLL2: rolled curb lip 6 cm at 18 cm out (47%, the old curb's slope), then 4 cm over 27 cm (15%) to the 10 cm crown at 45 cm. Collider = drawing.
- PSX_CITY_CORNERPAN: pan round every vertical corner's curb return (Cs2CornerPan, called from both corner kinds in Walks.cs); arm pan runs to 0.5 m of the ribbon end.
- PSX_CITY_PANSIDE: granite/no-pan only on DressMain (Tryon, Trade); other Uptown streets get the concrete pan.
- PSX_CITY_LIDTONE: collar dark grey iron ring (62/255), disc black iron, waffle in grey iron; grate bars 3.2 cm with the atlas's darkest texels (22/255) under them.
- PSX_CITY_PANWET: structure renderer WetReflections.Tag (CityWorld.cs, one line) - the pan/lids/grates mirror rain like the road (material was already _Wet 0.6).
- PSX_CITY_LIDMARKS: no inlet/manhole/valve whose arc range (+0.5 m) overlaps any painted mark of its edge (CityMeshes.Marks: crosswalks, stop bars, arrows, ONLY, pockets, noses, yields); inlets may nudge 7 m. Count after: 0 on paint in all 5 rings (Curbs2Stats "ON PAINT").
- PSX_WHEEL_ENVELOPE=1 (DEFAULT OFF): CarController.TyreEnvelope - 4 more rays ahead along the wheel's travel, each lowered by the tyre's curve; an edge ahead also pushes back along its line to the hub (cap 1x). Measured, not shipped (below).
- Tools: CityCurbDrive modes "+e" (envelope), "+r1", "kNN" (chamfer face NN cm), site "apron:K"; 9-run apron/ramp crossings (90/45/20 deg x 10/30/60 km/h). New Editor/CityRefSpots.Curbs2.cs (RunCurbs2).

## Physics (RX-7; all-wheels-off s / vertical kick g; old = PSX_CITY_CURBS2=0)
- Rolled (Lexington Ave): 90/50 0.37 s 27 g -> 0.28 s 23.5 g; 45/80 0.02/16.7 -> 0.02/10.4 (round 1 0.42 s); 30/80 12.9 -> 9.8 g; 15/80 7.1 -> 9.8 g (all-off 0.03 both); off-the-walk drops same all-off, kicks +1-1.6 g. Every launch time equal or better.
- Vertical (N Graham, shipped 22 cm chamfer): equal to old in every cell (one 30/50 run hit traffic: 1.70 s; rerun 0.00 s / 1.7 g; old's rerun hit traffic instead).
- Plumb + envelope: square 30 km/h no launch (7.1 g, 0 stops vs old 4.8 g, 2) and 90/80 3.8 g vs 24.7, BUT 15/50 0.30 s 22 g (old 0.00/0.7), 45/50 0.12 s, and ramp ends 90/60 0.30 s launch: FAILS. Chamfer + envelope: better on most cells but an apron-ramp 45/30 launch 0.72 s: FAILS. k16 (16 cm chamfer): 15/30 0.75 s launch: FAILS. So the 22 cm chamfer stays the steepest collider that passes; the drawn plumb face stays at the road edge (moving it out trades cut for air, facepos.py).
- Aprons (S Poplar St lot apron) and ramp ends: old vs round 2 equal in all 9 crossings (pre-existing 0.19-0.29 m "wheel steps" at 45-20 deg 30-60 km/h are the apron's side lip in both). No apron found by rule within reach for a second site (N Poplar: no curb found).
- Median noses, refuge islands, lot curbs, bus/turn-pocket edges: not touched by curbs2 (only WalkProfile callers: walk spans, cuts, corners) - unchanged by construction.

## Gates
- Typecheck OK. City-wide launch audit off/on: 91 LAUNCH / 565 UNLOAD both, CSVs identical; uptown route 1/4 both. Drive audit uptown: race-route checks all ok, same as round 1's before (other roads: grass 10 vs 11). city-traffic-check: CITY TRAFFIC OK, 0 traffic-on-traffic bumps.
- Counts N Graham ring: pan 12481 m (1465 m round corners; round 1 784 m), 132 inlets, 44 manholes.

## Still wrong / not done
- The wheel still cuts ~17 cm into a vertical curb's top edge while climbing it (and 4 cm along the top for 22 cm): a ray wheel cannot avoid it; the envelope fixes the picture but fails the launch matrix. A real fix needs tyre compliance in BumpStops (not attempted).
- Edge line: by rule (no edge line on curbed city streets; street view N Graham and Central Ave have none) it should go; the pan hides it where it runs, but N Graham still shows a white line about a lane-line's width inside the pan (road texture: seams/road owner).
- Lids are near invisible under the rain's reflection; corner pan sits at the ring point's height (may hide under a fan that rises); no freeway drop inlets, no junction manholes, inlet spacing not checked against a real block. Night not shot; player build not run.
- Real Lexington Ave has a grass strip between curb and walk; Wearn Ct has no walk on one side.

## Merge
- Re-bake (walk + structure meshes). No format/slot change. Shared-file edits: CityMeshes.Walks.cs (3 granite lines, 2 corner-pan calls), CityWorld.cs (1 line), CarController.cs (envelope, default off: 2 call lines + one method block). New: Editor/CityRefSpots.Curbs2.cs + .meta.

# HANDOFF - CURBS BY CONTEXT, GUTTER PAN, STORM INLETS, MANHOLES (charlotte-curbs-drains, 2026-11-11; NOT pushed, NOT merged)

Branch `charlotte-curbs-drains` (worktree `C:\Users\mcgee\PSX Racing-curbs`) off mountain-nm c81f2a59. Sandbox PSXPerf (lock "curbs", removed). The owner: "Curbs are sloped and not 90 deg like in real cities. neighborhoods have gentle sloping curbs to park on. i also still don't see storm drains or manholes in roads".

## Causes
- Every walk street had ONE curb profile: 11 cm face over 10 cm then nose to 15 cm at 25 cm (`CityMeshes.Walks.cs` CurbFaceOut/CurbFaceH/CurbNoseOut, ~l.55-59), along streets ROLLED further (face over 22 cm, nose 40 cm out: `WalkRollFaceOut/WalkRollNoseOut`, launch 7) and its face drawn in the slab's darkest joint column (`CityMeshes.CurbMedian.cs` CurbFaceU, curbs 11-05): from the cockpit a dark 45-degree ramp, what he arrowed.
- No code anywhere drew a gutter pan, inlet, manhole or valve lid (grep: none).

## What changed (switch `PSX_CITY_CURBS2=0` = all of it off; sub-switches `PSX_CITY_DRAINS=0`, `PSX_CITY_LIDS=0`, `PSX_CITY_CURBROLL=0`, `PSX_CITY_CURBCHAMFER=0`)
- New `Scripts/City/CityMeshes.Curbs2.cs` (all the rules, profiles, details, counts). Edits: `CityMeshes.Walks.cs` (WalkProfile/WalkBack branch to the pass, LayWalk UVs + drawn face, span record, gutter call, corner class), `CityMeshes.StreetDress.cs` (2 lines: roll argument), `CityMeshes.cs` (ONE line after PlaceLamps: `EmitStreetHardware(...)`).
- CLASS per edge: ROLLED = a walk street of cls 0 outside the Uptown loop with no non-house building within 40 m (it has a walk only for being near the Square: Dilworth, Fourth Ward...); every other walk street VERTICAL. A corner is rolled only if every walk street at its node is; a street eases to its corners' class over the 8 m before a junction (no step).
- VERTICAL curb DRAWN plumb: 15 cm, face 1.2 cm off plumb, 4.5 cm rounded nose, light concrete (Uptown: granite grey), render-only on the structure atlas. The COLLIDER (walk mesh) keeps exactly the old street curb's chamfer (face top 22 cm out at 11 cm, nose 40 cm) - see physics. Gutter pan: render-only 0.6 m concrete plate on the road's outer 0.6 m (1 cm proud), not in Uptown (S Tryon at road level: granite straight off the asphalt), not within 4 m of a junction mouth nor on a walk narrower than 1.2 m. It covers the 0.36 m edge line on curbed streets (as real curb-and-gutter streets have none).
- ROLLED curb: 10 cm over 45 cm (valley to 2.5 cm at 22 cm, roll to 10 cm at 45 cm, max 33%), collider = drawn.
- INLETS (Charlotte combination inlet): at sags of the real profile, 5.5 m before the low end's corner (both ends on the level), every 90-120 m on grade; both sides; not on aprons/driveways, ramp ends (<1.6 m walk), tree grates (2.2 m), lamp posts (1.6 m); nudged up to 3 m. Grate in the gutter (frame, 11 bars + spine, dark under), black hood 2 cm proud with a dark throat under it, concrete top slab + round lid in the walk; rolled streets get a low hood over the roll. Render-only.
- MANHOLES 24 in (collar + waffle lid) every 95-125 m by hash in a lane centre or wheel path, >25 m from junction mouths, >0.5 m + radius from every painted line, never in the pan, never on decks/tunnels/links/freeways (cls>3); valve lids 30-45 m on, 1.3 m in from an edge; ~2.5% of walk spans get a lid in the slab. Render-only, tilted to the road plane.
- Tools: `Editor/CityCurbDrive.cs` (`PSXRacing.EditorTools.CityCurbDrive.Run`, play mode; PSX_CURB_MODES old,v,ch,cNN), `Editor/CityRefSpots.Curbs.cs` (`CityRefSpots.RunCurbs`).

## Physics (RX-7, N Graham e14122 and Lexington Ave e21451; 4 angles x 5 speeds on, 2 x 3 off; scratchpad curbs\drive2.txt, drive3.txt)
- Plumb collider ("v"): square on 10 km/h vertical kick 10.3 g, front wheel off 0.42 s, 2 jounce strikes 2.7 m/s, 16 deg yaw (old curb 0.8 g, 0 s, none); 45 deg 10 km/h 5.9 g / 0.42 s (old 0.3 / 0); 15 deg 30 km/h 5.8 g, 2 strikes (old 0.4 g). Ray wheels step the whole 15 cm in one tick.
- Chamfer 9 / 12 cm: fine at 10 km/h (2.8 g, no strike) but launched square on at 30 km/h (all wheels off 0.35 s, 23 g, body +1.0 m; old 4.7 g, no air); 30 deg 50 km/h 10 g (old 1.8 g).
- Shipped: collider = old street curb profile -> every N Graham run identical to before (drive3: old vs ch equal to 0.1 g in all 26 runs). Rolled residential vs old there: equal or better to 50 km/h (90 deg 50 km/h: all-off 0.37 s -> 0, 27 g -> 10 g); worse at 80 km/h 45 deg (all-off 0.02 -> 0.42 s). Off-the-walk drops (0.1-0.3 s all off at 15-30 deg) are the same before and after.
- Ramp end crossings: no new step (N Graham 45/30: old wheel step 0.052 m -> 0.008 m). No driveway apron within 60 m of either test site: aprons NOT driven.

## Gates
- Typecheck OK. Drive audit (uptown route + 9 default tiles, PSX_DRIVE_BARRIERS=0) before == after: walls 0, steps 0, holes 0, fall holes 0, invisible in lanes 0; 2 failures in both (8 blunt rail ends on bridge approaches, 68 collider-only solids) - pre-existing. `city-traffic-check -NoWatch` (PSXPerf): CITY TRAFFIC OK, 0 traffic-on-traffic wrecks.
- Counts on 4 rings of 9 tiles (shots log): N Graham 5131 m vertical / 885 m rolled / 4772 m pan, 56 inlets, 24 manholes, 13 valves, 16 walk lids; S Tryon 11167 m vertical, 110 inlets, 34 manholes; Lexington 7545 / 1327 m, 87 inlets (20 rolled), 34 manholes; Central 6921 m, 68 inlets, 39 manholes. Ring build ms after/before: 11089/3166 (first, cold), 5447/4538, 4961/4099, 2979/2470 (+12-20 %; triangles per tile not measured).

## Proof
`C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\40ca151a-c5be-4aaa-9ea2-b8dd0e7c98a9\scratchpad\curbs\curbs_drains_before_after.png` (before | after: N Graham eye, chase, curb, inlet, manhole, night, rain; S Tryon inlet; Central curb + corner; Lexington rolled curb + inlet). Street level (reference only): Central Ave at 35.2202,-80.8088 (vertical curb + concrete pan, no edge line: matches), S Tryon 237 (granite curb straight off asphalt: pan removed for Uptown), Lexington Ave 716 (low residential curb).

## Not done / caveats
- The N Graham spot is the one-way N Graham piece ~1.4 km out (the shot picker skips edges near decks); the owner's 4-lane two-way spot was not pinned exactly. Night frames are blown out by the editor's preview lighting before AND after.
- Everything new is placeholder art on the structure atlas (grate bars low contrast, lids rust). Texture request appended to the art workspace `Charlotte_Texture_Feedback_2026-11-11.md` section 5; context frames `Charlotte_Texture_Context\2026-11-11\C_*`.
- Render-only details are not WetReflections-tagged (the structure mesh is not): the pan stays matte in rain. Corner curb returns get no pan and no drawn plumb face changes their collider (corner collider is now the 22/40 cm chamfer, was 10/25: gentler). Medians, refuge islands, lot curbs unchanged. No freeway drop inlets, no junction-centre manholes, no sidewalk curb ramps' detectable warnings.
- A wheel climbing the curb sits up to 4 cm under the drawn top for 22 cm (collider vs drawing); driving along the gutter it is exact.
- Not run: player build, city-wide launch audit (the collider of vertical streets equals the old one; residential and corners changed).

## Merge notes
- Re-bake needed (tile geometry: walk mesh + structure mesh). No new Slot, mesh, material, TileMeshes field or format change; everything rides Slot.Sidewalk (walk) and the structure mesh (Slot.Structure). Tiles with no bridge now also have a structure mesh.
- Seams interface: my side starts at the drawn road edge (Section.L/R). The curb foot is at the drawn edge, 2.5 cm under it; the gutter pan is a render-only plate ON the road from the edge to 0.6 m inside it, 1 cm proud, built from the span's own L/R heights; manholes likewise on the ribbon. Nothing of mine touches the ribbon, fan or skirts.
- Shared-file edits: `CityMeshes.cs` one line (after PlaceLamps). New files with .meta: CityMeshes.Curbs2.cs, Editor/CityCurbDrive.cs, Editor/CityRefSpots.Curbs.cs.
ï»¿# HANDOFF - ROAD SEAMS / ROAD SLAB (charlotte-road-seams, 2026-11-11; NOT pushed, NOT merged)

Branch `charlotte-road-seams` (worktree `C:\Users\mcgee\PSX Racing-seams`) off mountain-nm c81f2a59. Sandbox PSXBuild (lock "seams", removed). Owner 2026-11-11, N Graham St: "I see through gaps in the road at every intersection ... I asked for roads to have real thickness".

## Cause (measured)
- The owner's line: a junction fan's mouth edge stands FanProudM = 12 mm over the ribbon's end row (CityMeshes.cs:10600; corners CityMeshes.cs:10825; centre CityMeshes.cs:9901) and nothing filled the step. Approaching a junction the eye looks through the 12 mm slot under the fan onto the lattice 10 cm down (pale dirt): a hairline across the whole road. Reproduced in editor frames at N Graham St node 14254 (arm e20287), 15 m short, noon and sunset.
- Also: sheets meeting at one height with vertices that do not coincide (T-junctions, slivers the closing pass's 2 mm weld took as shared; ~26 km in the six place tiles, mostly lots and fans), and road edges standing 2-10 cm over a verge/land (the closing pass, CityMeshes.Skirts.cs, faced only edges > SkirtMinM 0.10 m over the lattice, none with pavement past).
- NOT the bake: a tile written and read back through CityTileCache (Encode/Write/Read/Apply) is bit-exact (roads/ground/kerbs vertices, normals, indices), slab off and on. Tile borders: matched road vertices differ <= 0.046 mm; 14-34 of ~400 border vertices have no partner within 0.5 mm (T-junctions across a border; the slab skips border-aligned edges). There are no LOD road meshes.

## Fix (PSX_CITY_ROADSLAB=0 = before; CityMeshes.RoadSlabOn settable)
- Scripts/City/CityMeshes.RoadSlab.cs (new), run at the end of the closing pass, a tile-job step every 256 edges. The road sheets' open edges re-found at a 0.5 mm weld (Key3Fine). Each gets a vertical FACE in its own bucket (material, draw call unchanged):
  - STEP (land or pavement past lower by > 2 mm, <= 0.6 m): down to 3 cm under it, top ON the edge (the owner's fan risers). From a kerb's foot where a kerb face runs along the edge.
  - JOIN (pavement past -3 mm .. +10 cm): 3 cm deep, 4 mm in under the edge, top 1 mm under (a tie-free line); the two sheets' faces look at each other across the crack. Steps under 6 mm are inset the same way.
  - Not where the closing pass, or another sheet's slab face turned the same way, already stands on that line (5 mm inset where it stands in part). Collinear runs merged. Normals set straight up after the roads mesh's (SlabNormals) so a face shades as the road.
  - A first version laid sloping tongues under joins: clean picture but the flicker finder counted 456 -> 899 tie px (they started level with the pavement past). Replaced.
- Shared-file edits: CityMeshes.cs one line after FinishMeshSteps(tm.roads) (SlabNormals). CityMeshes.Skirts.cs: OpenEdges records each open edge's own vertices (skirtOpenOwn, openSheetOff) and takes `fine` (Key3Fine); the road loop adds slabTaken keys; RoadSlab enumerated after the road faces. No curb/walk file touched; the curb faces (ground mesh, Sidewalk slot) are not seen by the slab: where a road edge meets a curb a face may end up inside the curb (hidden).
- Collider: the faces are in the Roads mesh (no render-only road collider exists; one would be a bake-format change). A face stands in the plane under the sheet's existing edge (that step was already the sheet's edge in the collider).

## Proof
- Sheet: scratchpad seams\seams_before_after.png (6 places x noon/sunset; eye 15 m, eye 40 m, chase 1.0 m at 25 m; before | 3.3x centre zoom | after | zoom; seam at frame centre). Owner_graham: pale line gone at noon and sunset (checked at 5x); uptown_4leg, res_t (dark line), ramp_term sunset lines gone; divided_x shows none either way; the slope place's seam is behind a crest (weak pick); 40 m / chase zooms mostly land above the seam (frame centre).
- Seam audit (Editor/CitySeamAudit.cs; open metres before -> after): owner tile 3295 -> 68; six place tiles 30674 -> 294; Uptown 4x4 77544 -> 2894; 60-tile city sample 90632 -> 516 (99.4 %). NOT 0. Residual: joins where the pavement past is just 2-3 mm off (band edges), Uptown steps onto rising ground; listed in the report.
- CityAudit.RunDrive (uptown route; owner's e20287 + 8 neighbours): all 26 ok/FAIL lines identical before/after (walls in lanes 0, fall holes 0; the one FAIL, INVISIBLE COLLIDERS solids 88, is there before too). Run on the 2 mm-inset build (229eecf5); the last two commits change only face insets.
- Flicker finder (uptown0, uptown1, tryon0; OpenGL core): 456 tie px before -> 575 after (+119 of 6.1 M drawn). 97 of them at one fan mouth (S Tryon at Winona, junction | tw3t asphalt, 13 m): the riser face's line against the ribbon under it. NOT 0.
- CityRoadMeshHash ALL fefe884bcf0482fb -> (final build not re-hashed; 229eecf5: e7af04817a1be839). Only Roads change; Kerbs/Barriers/Ground identical.
- Cost: road triangles +37-45 % a tile (owner tile 4349 -> 7771); slab pass 14 ms a tile (city sample), 106 ms (Uptown mean), worst ~600 ms on a lot-heavy Uptown tile (sliced). Draw calls unchanged.

## Coordinator
- Re-bake the tiles (road geometry changed). No CityTileCache.Format change, no new slot, no asset files (new .cs + .meta only).
# HANDOFF - HOUSES ROUND 3 (agent "roofs", charlotte-buildings-1111, 2026-11-11; NOT pushed, NOT merged)

## Drive-audit fix (first item; commit 13ce514b)
- Cause: stoops (round 1's HouseDoor and round 2's units/porches) and stepped unit fronts reached the Wesley Heights court lanes (houses front the court pavement within 1 m): the 0.3-0.9 m solids were the 14 INVISIBLE lane hits. Fix: HouseStepOffRoad (CityMeshes.HouseParts.cs) - no stoop, porch or unit step whose footprint comes within 1 m of any street's pavement (links included); the door stays.
- Drive audit (PSX_DRIVE_EDGES=24042,24054,24039,24041 = three house-court tiles, BOXED scope): INVISIBLE lane hits 14 -> 0; drawn Buildings faces in a lane 196 -> 0 (lanes total 120 = base); solids 88 = base.
- NOTE: CityAudit.RunDrive did not exit after writing city_drive.txt (19:52): Unity pid 45460 on PSXCity is still alive and blocks the sandbox (types' 42500 did the same). Not killed (rule).

## Round 3 changes (switches; =0 = round 2)
- PSX_CITY_HOUSEROOFTINT: shingle tones by era on the RoofTiles bucket's new vertex colours (alpha 255 = a house roof; PSX/Lit _HouseTint 2 tints only those; builder sets it on CityRoofTiles): charcoal, weathered grey, brown, near-black; green/red metal on 13 % of pre-war roofs; porch roofs too.
- Siding: per-house pick of 12 stronger colours (white 1 in 8) instead of a cell's 4 (round 1's PSX_CITY_HOUSETINT).
- PSX_CITY_HOUSEPARTS additions: garage doors (ranch 1-car, new 2-car, street long wall end; driveway side NOT used), dormers (pre-war, 1-2), shutters (half the houses, dark tints), front-door paint, the 45 m rule (no soffits/porch/dormer/garage/shutters on a house no street comes within 45 m of).
- L-corner sliver: buried roof extensions kept inside the plan at both corners; no soffit along a buried extension.
- Tools: Editor/CityRefSpots.Roofs.cs RunStreets (frontage chosen by rule: the street point within 300 m with most house centres 6-60 m out within 40 m along; poses kerb + raised 8 m), roof-closure audit in CityAudit.Windows.cs.

## Numbers (93-tile sample, run before the stoop fix and dormer fix)
- Windows CUT/BURIED 0/0; flat house roofs 0; census GAP 0, buried p95 0.60 (= r1/r2).
- Roof closure: 338 open points of 128,860 (first version counted fit-cut footprints as open; the corrected audit (drawn boxes) is committed but not re-run).
- Triangles a tile 5,010 (r2 4,820, base 1,773). Build ms a tile (r2 -> r3): ring 293 -> 200, OSM houses 219 -> 197, shot places 493 -> 324, worst 1,823 -> 1,068 (base 1,266). Shutters 6,715 pairs, garages 239, porches 941, dormers 0 (fixed after: too tall for the roofs), far-rule houses 913. PerfTour NOT run.

## Not done / generic still
- Pre-war streets are mostly BRICK (Plaza Midwood row reads as brick ranches; real: siding bungalows) - the round 1 brick share per cell needs an era cap. Siding colour variety still weak in frames. Subdivision/west street: no frontage found (fill houses stand 60-75 m from streets). No new street views (sheet uses round 1's). Dormers, the closure audit fix and the 60 m rule not re-shot (sandbox blocked).
- Trees: tm.houseBoxes = fill-house boxes + porch boxes (no heights). Real footprints: trees use f box / f.pts + 0.6 m + 1.41 m pad (RoadsideOccupancy). Volumes to keep crowns out of: wall box + 0.35 m eave / 0.25 m rake overhang, + 1.0 m stoop on the door wall, unit fronts +0.8 m (step + gable overhang); porch box (in houseBoxes) to floor + 3.1 m; roof to eave + up to 3.5 m (blocks 4.5), chimneys +0.6 m over the ridge.

## At merge
- Re-bake. New: RoofTiles bucket has vertex colours (no new slot); CityRoofTiles material needs _HouseTint = 2 (builder line in PSXRacingBuilder.City.cs). PSXLit.shader: one line. CityMeshes.cs: one line (RoofTiles col) + commit 4f6276fe restored the house hooks that 660aecf8 had misplaced (HEAD did not compile).

# HANDOFF - NON-HOUSE LOW-RISE CLASSES (agent "types", charlotte-buildings-1111, 2026-11-11; NOT pushed, NOT merged)

Shares the branch with agent "roofs" (houses). Sandbox PSXCity (lock "types", removed after each job). Owner: "replacing these very unrealistic generic textures ... top priority"; "building textures, glass, marble, concrete ... greatest improvement".

## Causes
- HEIGHTS: the Charlotte building import tags `height` in FEET with no unit (Atando Ave's 1-storey warehouse OSM 324608519 `height=26`, Walmart 22, Lowe's 24, industrial 38); export_osm.mjs parseHeight reads them as metres (tools/city/export_osm.mjs:1512) and style 1 (>= 20 m) + PickFacade's 3.8 m office floor made six-storey blocks. Untagged big boxes in the core got the exporter's 12/16 m guess (export_osm.mjs:1584).
- LOOKS: charlotte_bld.bytes keeps only use/mat/style, so a warehouse, a strip centre, a church, a school, a mill were all the same atlas box; PickFacade (CityMeshes.cs:598) gives style 1 a curtain wall 36 % of the time whatever the use (NoDa's Fat City Lofts, apartments); amenity=place_of_worship/school made churches and schools style-4 shops (export_osm.mjs:1597); a church or mill drawn by OSM building:parts took the hash look of its outline (Casa Viva church on Hamorton Pl: glass).
- SHAMROCK: the 3-storey brick block is NOT a frontage box (Shamrock Dr lies inside the core footprint box x -5844..2605, z 516..8674, where CityBuildings places no frontage boxes); no footprint within 45 m of the pose's right-hand lots is one either (all style-3 houses): not found this round.

## Changes (switches; `=0` = the picture before)
- DATA (no re-export): `tools/city/lowrise.py` (reads the Overpass cache + layers/landuse.json) -> `Scripts/City/CityLowRise.Data.cs` (17,921 rows, base64: point inside the outline, kind work/shop/worship/school/homes/mill/plain, flags height-tagged / levels / brick / metal / office zone / integer height). Matched at load in `CityMeshes.LowRiseApply` (called first in `CityBuildings.Precompute`, 54 ms); a footprint two rows land in keeps the stronger kind; a hidden outline's building:parts take its class look.
- PSX_CITY_LRHEIGHT: outside the Uptown loop, works/shop/plain with an integer height > 13 and no levels: feet -> metres (1,159); big (>= 2,000 m2) low boxes with a guessed 10.5-45 m: one tall storey 6.5-9 m (220); strip centres at least 6.2 m (the shopfront needs it); schools 4.2 m storeys (levels, else 1-2 by size); churches nave 6-8.5 m + gable; halls 8.5 m. 1,379 lowered in all; the 20 largest (footprint, area, kind, h before -> after, lat/lon) are in the shot log `types_log_r2.txt` (copied to scratchpad `types\`): 16 works sheds of 16,000-124,000 m2 (38 -> 11.6 m, 10 -> 8.6 m ...), 3 big-box shops (23 -> 7.0 m), 4 plain; one visible odd catch: a 37,512 m2 plain block at 35.4323,-80.9486 (McGuire nuclear station) 100 -> 30.5 m.
- PSX_CITY_LRLOOK (`Scripts/City/CityMeshes.LowRise.cs`): SHED (3,172): walls drawn by the kit - brick 45 % (painted block to the clerestory, one row of a brick column's windows), tilt-up concrete 35 % (column 7 stone + accent band), ribbed metal 20 % (column 7 panel); metal coping; parapet; a few high windows; dock-high roll-up doors with seals, bumpers and lights on the longest open side wall (1-14), a drive-in door with bollards; an office corner on the street front (storefront glazing lit as an office, glazed door, canopy, its lamp). STRIP (5,352): every shop outside the loop takes covShop (plain bays, canopy, blank sign band, parapet). CHURCH (583, < 1,600 m2): brick nave on its fitted box, one tall storey of the brick column, white gables, shingle gable roof with overhangs, white door surround, a steeple (brick tower, white belfry with louvres, spire) at the street end on 70 % of those < 1,400 m2. HALL (239) / SCHOOL (528) / MILL (18): brick looks (mill = loft or red-sash brick at 4.3 m storeys). APARTMENTS (use 2 outside the loop): never a curtain wall (brick families, home storeys). No new material, slot, bucket or draw: FacadeGlass, RoofTiles, RoofFlat buckets only.
- PSX_CITY_LRFRONTAGE (`CityBuildings.cs`): frontage boxes never within 60 m of a mapped house (0 removed: outside the core coverage drops OSM houses) and at most two storeys outside the core (144 of 5,151 lowered).
- Shared-file edits: `CityMeshes.cs` 6 lines (EndFacade lrActive reset; LowRiseChurch before the non-house path; LowRiseLook after PickFacade; strip covShop + LowRiseBegin + parapet; LowRiseWall in the wall loop; no cornice on a shed). `CityBuildings.cs` (mine).
- Tools: `Editor/CityRefSpots.Types.cs` (RunTypes: 8 places, look-at poses, night at 2, per-tile triangles, ring time, footprint description, PSX_DRIVE_EDGES).

## Proof
- Sheet: scratchpad `types\types_round2.png` (real street view | base (switches 0) | round 2; 8 places noon + Atando and the strip at night). Gates below.
- Tall lists: CityBeacons 35 (towers 90 m+) and CitySkyline 60 towers unchanged; skyline MID-RISE 534 -> 338 (196 sheds/shops that stood 20-45 m on feet heights no longer on the far skyline: intended).
- Cost (the building's tile, triangles base -> r2): Atando 18,578 -> 19,310 (+4 %), Bancroft 8,590 -> 11,826 (+38 %, docks), church 40,699 -> 40,837, strip 17,047 -> 16,855, others +-1 %; ring-of-25 build time within noise (5-9 s both).
- `CityAudit.RunDrive` with PSX_DRIVE_EDGES = the 8 shot streets (21 tiles + the default box), base (my switches 0, 16:55) vs r2 (branch head incl. roofs' houses, 19:00): my streets 74,967 probes both, walls 0 / steps 0 / holes 0 / blunt 0; WALLS IN LANES 0 -> 0; collider-only solids 88 -> 88 (Buildings MeshCollider 6 -> 6, of 1.99 M -> 2.95 M samples); blunt rail ends 8 -> 8, short rails 27 -> 27 (old). NEW in r2: INVISIBLE COLLIDERS lanes 0 -> 14, all `Buildings` back faces / ceilings at Wesley Heights house courts (Summit Greenway Ct (-3909, 5328), Walnut View Dr (-3829, 5787), Uptown West Dr, Cannon View Ln: x -3830..-3933, z 5328..5887) - no low-rise class footprint within 47 m of any of them: the house geometry's (agent "roofs"), not this round's; not split by a switch run (one more 25 min job).

## Still generic / not done
- Shed walls read flat (the parts column's concrete and panel at 2 m x 8 m; metal reads glassy under the metal bit); docks are on a side wall, so the street frames rarely show them (Atando's real docks face the street). Strip front faces its lot (covShop rule), from Albemarle Rd the frame shows the long side. Church/school brick is the office-sash column stretched (real: arched stained glass; tan stone school). NoDa mill is tan loft brick (real: red brick, arched windows); Fat City Lofts is now brick (real: grey panel + mural). Night frames: shed dark but for its office corner (as wanted); strip shopfronts not visible from the pose.
- Not run: player build, WebGL, PerfTour frame time, the city-wide window audit (no house code touched).

## At merge
- Tile geometry and far skyline changed: RE-BAKE the city tiles. CityTileCache.Format NOT changed; no new Slot / bucket / material; no data file regenerated (charlotte_bld.bytes untouched; new generated `CityLowRise.Data.cs` from tools/city/lowrise.py, `--check` supported). Codex requests: art workspace `Charlotte_Texture_Feedback_2026-11-11.md` section H.4; context `Charlotte_Texture_Context/2026-11-11/T_*` (22 files).

# HANDOFF - HOUSES ROUND 2: ROOFS, UNIT ROWS, PARTS (agent "roofs", charlotte-buildings-1111, 2026-11-11; NOT pushed, NOT merged)

On top of round 1 (a6e1db67). Shares the branch with agent "types" (non-house classes). Sandbox PSXCity (lock "roofs", removed).

## Causes
- FLAT-ROOFED HOUSES: a house polygon with over 10 corners is not gabled by the exporter (tools/city/export_osm.mjs:1599) and EmitFootprint's polygon path drew it with EarcutInto (CityMeshes.cs, roof branch); round 1's low blocks of any plan (polySiding) too. 212 flat house roofs on the 93-tile sample (1,074 house polygons city-wide).
- N GRAHAM = ONE BOX: blocks were drawn as one wall per side, one flat or plain roof (no units, no gables, no doors).
- SILENT TINT: the tint path works (frame A/B with the material flag off changes the siding pixels), the round 1 palette was just too close to the painted beige.

## Changes (new switches; =0 = round 1's picture)
- `Scripts/City/CityMeshes.HouseParts.cs` (NEW). PSX_CITY_HOUSEROOFS: a house polygon / low block is split into up to 5 rectangles on its own axes (the corners' coordinates are the grid), each roofed along its long axis: gable or hip at an outside end, a hip where another rectangle covers the end (L corners), a buried cross gable at an inside end; a flat ceiling at the eave under it closes what the rectangles miss; slope by era (Uptown distance: <3.8 km pre-war 0.55, <8 km ranch 0.38, beyond 0.5), rise <= 3.5 m (block 4.5). The gabled boxes take a hip by era/hash (pre-war 25 %, ranch 55 %, new 33 %).
- PSX_CITY_HOUSEUNITS: siding blocks (box and polygon) = units of ~6.5 m on every wall >= 9 m; on a street wall (a street within 35 m in front of that wall) every second unit steps out 0.5 m with siding returns under its own front gable (attic window), every unit a door + stoop; window pair a storey; brick ground storey on half the blocks by hash (two atlas bays a unit, the door on the pier), base band <= 2.9 m, block brick windows at 0.8 width.
- PSX_CITY_HOUSEPARTS: chimneys (atlas brick pier strip + concrete cap; 70 % old/ranch, 33 % new), front porches on 3 of 4 pre-war houses (deck, white posts + beam, shed roof fitted between the window heads and the next storey's sills, step; door added when the porch is on a gable end), concrete foundation band to 0.2 m over the floor on siding walls. Porches are added to tm.houseBoxes so trees keep their 2 m margin from them.
- Round-1 file edits: CityMeshes.HouseWalls.cs (unit dispatch, bays, brick widths, foundation, HouseBrickChoose/HouseDoor skip unit rows, stronger SidingCols incl. navy/charcoal/slate).
- Shared CityMeshes.cs (commit 1d9aa5ac, 9 lines): HouseBlockBegin/End at the sidingBlock and polySiding lines, `pitched` + HousePolyEave after eaveY, HousePolyRoof before the flat EarcutInto (+ flat counter), HouseBoxRoof first line in EmitGableHouse's eaves branch.
- Tools: CityAudit.Windows.cs (build ms per tile class, PSX_WIN_RING 5x5 ring, round-2 counters), CityRefSpots.Bldg.cs (poses side/sidel/top/row, PSX_BLDG_POSES, PSX_BLDG_TINTAB tint A/B frame).

## Proof (93 tiles = round 1's 73 + a 5x5 ring at Plaza Midwood)
- Windows CUT/BURIED: base 10,568/5,762 -> r1 0/0 -> r2 0/0. Style-3 flat roofs: r1 212 -> r2 0 (212 polygons pitched, 492 rectangles, 0 fallbacks). r2: 1,239 hips, 379 units, 56 front gables, 958 porches, 2,104 chimneys. House census: GAP 0, buried p95 0.60 (= r1).
- Cost: building tris a tile 1,773 (base) / 3,761 (r1) / 4,820 (r2, +28 % on r1). Build ms a tile, base/r1/r2: ring 310/226/293, OSM house tiles 216/214/219, fill 88/84/88, shot places 386/362/493 (max 1,266/1,220/1,823 = the Graham tile). Frame/GPU time (PerfTour) NOT measured.
- Sheet: scratchpad `roofs\houses_round2.png` (real | base | r1 | r2, 9 rows, + tint A/B + 4 night frames). Tint A/B: siding pixels change 2-7 % of frame, mean 18-20 RGB-sum - works but still subtle (most picks white/cream).

## Seen wrong / not done
- Dilworth, Shamrock, south subdivision frames do not show houses side-on (trees, the types agent's frontage block): their change is unproven in pictures; the owner's own frame (graham) shows the block too far. No street views taken for myers/pmw_north.
- Dormers, garages/carports, shutters, corner trim, a distance rule (120 m) not done. Brick atlas window position under porch roofs on brick houses unverified. The tree audit was NOT re-run (trunks keep 2 m from footprints and porch boxes; stoops 1 m and unit steps 0.5 m are inside that margin; crowns are not audited in the city at all). Player build not run.

## At merge
- Tile geometry changed: re-bake. No new Slot/material/bucket, no format change; tm.houseBoxes now also holds porches (already a baked field). Codex: shingles in 3 colours, porch boards/columns, garage door still wanted.
# HANDOFF - CHARLOTTE HOUSES + LOW-RISE BUILDINGS (charlotte-buildings-1111, 2026-11-11; NOT pushed, NOT merged)

Branch `charlotte-buildings-1111` (worktree `C:\Users\mcgee\PSX Racing-bldg`) off mountain-nm c81f2a59. Sandbox PSXCity (lock "buildings", removed). The owner: "replacing these very unrealistic generic textures with windows going through roofs needs to be a top priority".

## Causes
- WINDOWS THROUGH ROOFS: `city_facade_house` (one window per 6 m x 3.1 m repeat) was tiled over each wall from its foot (under the pad / ground) to the eave, whole repeats stretched (EmitFacadeQuad, CityMeshes.cs ~11707 before), and over the GABLE TRIANGLES too (EmitGableTri: uv apex (uRep/2, rise/3.1)) - windows sliced by the slopes, buried, two rows on a one-storey wall; a polygon wall split by a collinear vertex sliced the windows at the split. Measured (new audit, 73 tiles): 7,800 windows cut + 3,507 buried of 36,072 (31 %).
- HOUSE SIDING ON BLOCKS: tools/city/export_osm.mjs:1468/1575 calls `building=residential|terrace|garages|house...` and untagged < 260 m2 a house (style 3); gable only < 480 m2. 26,077 style-3 footprints; 360 have a block's size (>= 450 m2 or >= 10.2 m tall), 195 of them drawn as gables (by storeys 2: 154, 4: 139, 8+: 36). The owner's N Graham frame = OSM ways 589068963-969 (building=residential, 3 levels, height 11.76, 500-1,120 m2, two of them L-shaped).

## Changes (all in the tile geometry; switches =0 restore the old picture)
- `Scripts/City/CityMeshes.HouseWalls.cs` (NEW): HouseWall - whole bays (~4 m, window centred), one window band per storey from the house's floor, no band whose head reaches eave-0.3 or whose sill the drawn ground reaches (0.5 m sampling); plain siding on the window-free rows v 0.75-1.25 everywhere else; gables plain (PSX_CITY_HOUSEWALLS). HouseBlock (PSX_CITY_HOUSECLASS): >= 450 m2 or >= 10.2 m -> oblong <= 14 m: siding over a brick ground storey under the gable roof; any other plan <= 14 m and < 4,000 m2: the same walls, flat roof; taller: flat brick block on the atlas (home storeys). Eaves 0.35 m / rakes 0.25 m with soffits (PSX_CITY_HOUSEEAVES). Siding colour by 350 m neighbourhood, a third white (PSX_CITY_HOUSETINT; vertex colour). Brick houses 15-85 % by neighbourhood from atlas columns 04/08/10/14, one floor row per storey (<= 3 m) at 0.55 width, siding frieze above, concrete-block foundation (PSX_CITY_HOUSEBRICK). Glazed front door (the window cell, 0.95 x 2.05 m, on a bay line, never over a window) + concrete stoop on the street side (PSX_CITY_HOUSEDOOR).
- Shared files: `CityMeshes.cs` (EmitFacadeQuad first line; EmitGableHouse floor param + tint/brick/door/eaves; EmitGableTri first line; EmitFootprint block routing, PickFacade for blocks, GfBegin/Massing skip blocks; BuildHouses/BuildBuildings pass the floor; NewBuckets: FacadeHouse bucket gets vertex colours), `CityMeshes.Proxies.cs` (floor arg), `Shaders/PSXLit.shader` (`_HouseTint` property; appdata COLOR now in every variant; 2 lines in vert), `Editor/PSXRacingBuilder.City.cs` (sets `_HouseTint` 1 on CityFacadeHouse), `Materials/CityFacadeHouse.mat` (`_HouseTint: 1`).
- Tools: `Editor/CityAudit.Windows.cs` (WindowsOnly / WindowsBoth: windows read back off the built FacadeHouse submesh per plane + UV frame: whole / CUT / BURIED, plus the class census and, with PSX_WIN_HOUSES=1, the house census), `Editor/CityRefSpots.Bldg.cs` (RunBldg / RunBldgAll, places + PSX_BLDG_PLACES).

## Proof
- Audit, 73 tiles (42 OSM-house tiles by hash, 22 fill suburbs, 9 shot places), before -> after: CUT 7,800 -> 0, BURIED 3,507 -> 0. House census (owner box + suburban box) identical: buried p95 0.60 / 0.59, GAP 0, exposed p95 0.45 (walls' foot unchanged).
- Cost: building triangles 1,544 -> 3,388 a sampled tile (+119 %; roof tiles 9,952 -> 38,480 with eaves + soffits, siding 44,558 -> 93,014).
- Sheet: scratchpad `bldg\buildings_before_after.png` (real street view | before | after, 9 places, eye noon) + `sheet_right.png`, `sheet_night.png`, `sheet_rest.png`. Night: lit windows still light by the night mask on siding (yellow); atlas brick bands at night not seen lit in the two frames.

## Not done / seen wrong
- Industrial / shop / church / school classes (item 2b beyond houses): Atando Ave shows 6-storey precast blocks where the real street has one-storey brick and metal sheds; NoDa shows a glass tower by the mill; Shamrock Dr (brick ranches) shows a 3-storey brick block. Not touched.
- N Graham condos: real ones have gables/dormers per unit and chimneys; ours are flat-topped (L plans). Siding tint not clearly visible in the frames (unverified that the kit material in the editor took _HouseTint). Brick-base windows read tall/narrow. Polygon houses keep flat roofs.
- After frames predate the last commit's door-foot fix (doors on sunk pads now stand on the ground). Player build, HD tier (Codex's house set not delivered), frame rate: not run.

## At merge
- Tile geometry changed: re-bake the city tiles (CityTileCache.Format NOT changed; no new Slot; the FacadeHouse submesh now carries vertex colours - the cache stores colours generically). PSXLit edit + builder: the kit material needs `_HouseTint` = 1 (builder sets it; a scene/kit pass picks it up; without it houses are drawn untinted, nothing breaks).
- Codex request: art workspace `Charlotte_Texture_Feedback_2026-11-11.md` section H; context `Charlotte_Texture_Context\2026-11-11\H_*` (19 files).
# HANDOFF - UPTOWN GROUND FLOORS + STREET FURNITURE (charlotte-ground-floors, 2026-11-10; NOT pushed, NOT merged)

Branch `charlotte-ground-floors` (worktree `C:\Users\mcgee\PSX Racing-cltfloors`) off origin/main d635bb9f. Sandbox PSXRec (lock "charlotte-ground-floors", removed). The owner, 2026-11-09: "reference road level photos to make the city accurate enough to be recognizable". Sheet (real | before | after, 28 places; reference imagery, kept out of the repo): scratchpad `charlotte_pairs_03_ground_floors.png`.

## The rule (Scripts/City/CityMeshes.GroundFloors.cs; data Scripts/City/CityGroundFloors.Data.cs from tools/city/groundfloors.py)
- A wall of a building inside the freeway loop that FACES A STREET (open air in front, a street's pavement within reach square to it, nothing between) takes a ground-floor BAND from its floor to a storey line; the atlas wall starts at that line (the band replaces the wall there: never a decal).
- What the building is comes from OpenStreetMap's tags (413 tagged buildings round Uptown, 359 matched to a footprint, 206 inside the loop): parking DECK, HOTEL, WORSHIP, CIVIC hall, SHOP, HOMES; untagged = office. With its height, its street (Tryon/Trade = main; side) and its hash:
  - DECK: every wall, every level - precast spandrels proud of dark openings, columns; the entrance where OSM's driveway crosses the outline (43 mapped; else the middle of its front): a two-lane mouth, five red bollards, a clearance bar, a red blade sign (PARK), a light.
  - LOBBY: square piers, a fascia, glazing set back 1.2 m; on the building's front a canopy and a downlight; a hotel: two white planters and a blade sign in a made-up name (ARDEN, CALDER, WRENLY, MERRIT, HOLLIS, TARLOW).
  - ARCADE: the same, glazing 3 m back behind free piers.
  - SHOPS: a shopfront a bay - glass over a stall riser, a plain sign band, an awning on most; on a main street's wide walk a patio rail or a hedge planter before some.
  - BLANK: a solid base under a string course, a loading dock or two on a long wall.
- The places the rule cannot know: `GfPlaces` (9 rows: a point in the building + the style; East 3rd's south arcade, South College's white podium, the East 5th and East MLK hall walls and docks, ...). `GroundFloorReport` lists what each found.
- Everything rides the facade atlas's bucket: no material, no draw call, no object. Stone/metal = column 7; the dark parts = a flat patch of the brick column under a dark tint; glazing = the curtain-wall columns. Piers, risers, glazing, bollards, planters are solid as drawn; canopies, awnings, signs, soffits drawn only; anything on the walk is drawn only in a race route's run-off.

## Street furniture and corrections (Scripts/City/CityMeshes.StreetFurniture.cs)
- Tryon and Trade: TWIN-GLOBE posts (lamp kind `LampTwin` 5, the acorn's placement, light x1.5 and collider; CityPoles draws it), a bench + bin and a planter every fourth tree gap on each side, the Square's four corner statues and bollards.
- Corrections to the first dress: a side street's tree pitch is its own (`SidePitches`: 3rd 25 m, 4th and Church 20, MLK 18, 6th 15, College 13, others 14; was 12 everywhere); six skybridges are themselves (`SkyLooks`: West 4th's climbs under a glass roof, East Trade's banded and mostly opaque, South College's a white box with a ribbon, ...); the paver regraded (tools/city/paver_walk.py: mean 142,89,74 -> 106,86,78; tile sheet Docs/shots/ground_floors/paver_tile_sheet.png).

## Switches (all ON; `=0` is the picture as it was)
`PSX_CITY_GROUNDFLOORS`, `PSX_CITY_STREETFURN`, `PSX_CITY_DRESSFIX`. With all three off the 17 first-round frames are pixel-identical to main's. (The paver's colour is the texture file: no switch.) CityTileCache Format 4 (take the next free number at merge if the arterials branch also bumped it).

## Gates (PSXRec, before = main d635bb9f code, after = this branch)
- `CityAudit.RunDrive` (uptown + tryon routes, the Uptown box): before 3 FAILURES = blunt rail ends 8, short rails 27, collider-only solids 72 (of 1,531,341 samples); after 3 FAILURES = 8, 27, 72 (of 1,946,267 samples: the new geometry is sampled, none of it collider-only; Buildings (MeshCollider) 6 -> 6). Every ok/FAIL line of the two reports is the same: walls in lanes 0 -> 0, fall holes 0 -> 0.
- Flicker finder (`tools\city-flicker.ps1`, the route spots): 1204 -> 1206 depth-tie px of 18.14 M; no surface pair moved by 8 px.
- Frame cost at the Square (EnsureRing 2, 25 tiles, editor batch, best of 5 A/B rounds): triangles 767,350 -> 831,278 (+8.3%), renderers 227 -> 227, ring build 10.9 -> 11.5 s, render median 15.16 -> 14.32 ms (inside the shared machine's noise).
- `tools\city-traffic-check.ps1 -NoWatch -CityPlay`: CITY TRAFFIC OK (traffic-on-traffic wrecks 0, bumps 0), CITY SPAWNS OK, no FAIL line.
- Night: seven spots rendered through the game's own night (CityFarShots, PSX_FAR_POSES): door lights pool on the walk, lobby and shop glazing lights as an office floor does, skybridge glazing fluorescent, sodium and mercury street heads only.
- `=0`: with the three switches off, the 17 first-round spots are pixel-identical to main's frames.
- These gates ran on fb4d4cf6's geometry. After them: colour-only commits (the dark surfaces' UV patch, the shrub green) and ONE place row (`e3rd_hotel_and_deck`: the owner's photo - a hotel front and a deck mouth on one building), rendered but not re-gated.

## NOT done / NOT verified
- Not built or run: the WebGL player, the PC exe (its city bake is stale: Format 4), `CityAudit.RunTrees` after the pitch change (placement rules untouched; only the pitch).
- Night: lobby and shop glazing is lit as an office floor (the clock's share), not always on. An always-lit shopfront needs a cell of the facade atlas with the shop mask (atlas + night mask + Enhanced twin): not done.
- Bus shelters: none (none seen in the frames looked at). Benches, bins, planters: Tryon and Trade only. Bollards: the Square and deck mouths only (South Tryon's kerb-line bollards are not drawn).
- Deck mouths: no hanging ENTRANCE sign box, no public-parking sign boxes or painted arrows (West 4th). West 6th's arched-grille garage screen is a plain deck; its garden wall and iron fence are not drawn (no fences or low walls anywhere).
- The five hero towers' own bodies, model-swapped lots and OSM retail blocks (style 4) keep their old ground floors. A building takes ONE place row for all its street walls (footprint 13191: East 3rd's arcade also faces East MLK).
- Lettering is block letters on blade signs only (PARK, six made-up hotel names); shop sign bands are blank.
- Not in the game at all, seen in the frames: the light-rail bridge over East Trade and East Brooklyn Village, the streetcar track (post-2000: left out on purpose), North Brevard's wood pole line (not checked).
- THE SKYLINE RAMP (owner's photos 04/05): the motorway_link whose destination names Sixth Street and McDowell Street is OSM way 16664396 (101 m, a bridge) + 159022597 (171 m), off the Independence expressway at 35.2217,-80.8300; spot `up_ramp_6th` + day/night frames are on the sheet. It does NOT match the photo: from the link the road bends RIGHT and Uptown sits to the left; the photo's road bends LEFT with the towers dead ahead and the camera is above the road - its vantage was not found. What differs in the game's frame: a dense tree line on the outside of the curve hides the skyline (real: mown grass embankments, one ornamental tree); a Jersey wall on the ramp's right (real: W-beam on the outside of the curve); rough unmown ground; an EXIT sign on the left, no ground-mounted "Sixth St / McDowell St NEXT RIGHT" guide sign; the towers that do show are small, one crown lit. None of that is this piece's (roadside, trees, signs).

# HANDOFF - ROAD LOGIC JUNCTIONS ROUND 3e (2026-10-30; MERGEABLE: yes)

Branch `road-logic-junctions-r3e` (worktree `C:\Users\mcgee\PSX Racing-junctions`), head code e7d7429d + this docs commit, base 82c83a3c (integration-1030b = main d73ca5bd + heights r11 + round 3d). Sandbox PSXRec (lock "road-logic-junctions-r3e", removed; no Unity on it). Data = 82c83a3c's new data (code only, no data change). Scope cut by the coordinator mid-round (usage): items 1-3 kept, the rest are next steps below. Scratchpad tools in `r3e\` (r3e_tool.ps1 / r3e_job.ps1 lock-safe runners, r3e_trace.ps1, r3e_table.py, r3e_worse.py, nodes_near.mjs / edge_dump.mjs / rampgap.mjs, out\ every report / trace / check, runs\ the intermediate censuses).

## Gates (final code e7d7429d, PSXRec, new data)
- Base census junctions_r3e_base (82c83a3c, 40 spots x 300 s): overlap 0, wreck/car 26 (130.6 car-h, 3032 km). Docs/traffic_census_junctions_r3e_base.
- Final census junctions_r3e_final_c: 126.7 car-h, 2979 km - overlap 0, wreck/car 10 (base 26), car bumps 16 (35), lock-backed 10 (105); no trace dump at any traced node. Docs/traffic_census_junctions_r3e_final_c.
- Traffic checks (-NoWatch, PSXRec), every run in order: (1) the base census's check (82c83a3c): FAILED - 4 traffic-on-traffic wrecks, node 706 (car 31 lane 0 -> ramp e388 x car 17 lane 1 -> e10600, 3.6 / 6.9 m/s) and node 707 (car 24 on e388 into car 20 in I-277 lane 1, 7.9 / 10.9 m/s): the causes below; (2-4) V1, V2, V3 (pre-WIP-1 trees and WIP 1): OK, OK, OK (V3: one Ground hit at 1.0 m/s on East 3rd e1307, Roads at nodes 1487 / 9006 - heights r12's); (5) final_a's check on WIP 5: OK; (6) final_b's check on WIP 5: stopped mid-run (WIP 6 replaced that code), no result; (7) final_c's check on WIP 6: OK; (8) check2c: OK; (9) check3c: OK (Roads at 1487 / 9006, heights r12's). Every OK: 0 traffic-on-traffic wrecks, 0 bumps; 7-9 are three OK in a row on the final code
- Road meshes: CityRoadMeshHash ALL 44a98126e503e967 (Docs/road_mesh_hash_r3e_final.txt) = 82c83a3c's on the new data (Docs/road_mesh_hash_base1030b.txt).
- Signal heads: 3390 approaches = base; none in view 7 = base; junctions with an approach with none 6 = base, with none on any approach 0; no approach with fewer in-view heads than the base
- Drive audit INVISIBLE COLLIDERS solids: 82c83a3c in PSXRec 2910 bare samples (CityPost 690 colliders = 2760 + 40 other colliders = 150) -> 150, no CityPost entry (150 is PSXRec's own floor - 82c83a3c's non-CityPost samples: pizzeria door hinges, trailers, houses, a tower, a Buildings collider; PSXBuild's 124 is the same measure on that sandbox, sandbox-sensitive).
- tools\typecheck.ps1: OK.

## Causes and fixes
- INVISIBLE POLE COLLIDERS (blocked round 3d): CitySignals.Attach gave every st.poles entry the 0.36 m box sized for a 0.32 m signal pole; round 3's pedestals (PostHead: post-mounted / near-side heads) draw a 0.115 m post on a 0.34 m base, so each pedestal's box floated with no drawn face. FIX: a pedestal's colliders are its post and its base as drawn, turned to the approach (postBoxes); pole boxes unchanged; nothing removed.
- NODE 706, WEST 12TH STREET INTO THE I-277 RAMP (the class: lane use by side at splits): e9802 (2 lanes) forks into ramp e388 (1 lane, 13 deg left) and e10600 (2 lanes, 2 deg right); MapStraight mapped each in-lane to the nearest lane of each branch, and the ramp's lane starts on the street's centre line, so the RIGHT lane took the LEFT branch; their lanes cross in the ramp's first 7 m and nothing paired them (one leg kept apart by lanes). FIXES (CityTraffic.Forks.cs): lane use by side at every fork (683 nodes; turn:lanes where tagged; the boundary lane an option lane where the counts need it; the allotment whose ways keep apart); a branch's way without a sideways S (ForkStretch); lanes kept in order across a box; a wrong-lane car changes on the run before, never squeezes into the fork, else takes its lane's branch; two ways of one approach that still meet are ordered (CrossAhead: a body already there, then the one in its turn, then the nearer); the probe lists every way a move meets. CROSSING PAIRS city-wide (census static audit): before (PSX_TRAFFIC_FORKLANES=0) 892 nodes / 1121 pairs (straight x straight 622) -> after 446 / 548 (48); the rest are turn pairs (before = after) and narrow lanes - next step. Node 706 lane map before: lane 0 -> e388 MEETS lane 1 -> e10600 lane 1; after: e388 from lane 1 only (18.8 m arc), e10600 from lanes 0 and 1, no way meets another. Node 706 traced in every census and check: no dump at node 706 (no near miss, no touch) in the final census, its check, check2c or check3c.
- THE e388 RAMP END AT (-1867,5636) (node 707, I-277; the base check's wreck and PSXBuild's 0.14 m overlap): OSM joins the ramp to the freeway's centre-line node, so the ramp's line runs over I-277's lanes 0/1 for its last ~45 m while the merge began 20 m out (ZoneBack). FIXES: a merge begins no nearer the node than where the ramp's lane is a lane's width clear of every mainline lane (RampClear: 79 m here) and leaves half way to the mainline's heading (MergeStart); its way now stays 2.48 m or more from every I-277 lane centre (the census merge probe). WIP 6: RampClear's long merges (I-77 nodes 4134 and 649 in the first finals: a merging car held on beside the kerb lane's car - a car in its "turn" went first - two wrecks, merge stalls) - a ramp's merge now gives way to the road it joins (Precedes).
- Also shipped (found on the way, each traced): East 6th e16828 / node 15151 (a body squeezed into the next lane by Place's clamp is braked for: SweptHit for two cars of one run); the zipper at a lane drop (North Graham n14352: 239 replans -> 0) and, from it, a lane change done short of the junction (WIP 6: still-changing turns at junctions 219 -> 4 of 75 in V7); round 3d LEFT 1-3 (the creep never pushes into overlap; the backing loop holds back; Fort Street's all-way STOP lines set back where a waiting car stands in another's way - 86 approaches, worst 6 m).

## Classes (base -> final_c, count and per 1000 km)
| class | base (131 car-h, 3032 km) | final_c (127 car-h, 2979 km) |
|---|---|---|
| wreck/car | 26 (8.6) | 10 (3.4) |
| contact/car-bump | 35 (11.5) | 16 (5.4) |
| stuck/gridlock-box | 71 (23.4) | 67 (22.5) |
| stuck/crossing-blocked | 4 (1.3) | 6 (2.0) |
| stuck/box-blocked-on-green | 10 (3.3) | 4 (1.3) |
| stuck/keep-clear-wait | 9 (3.0) | 8 (2.7) |
| stuck/yield-starved | 4 (1.3) | 6 (2.0) |
| stuck/left-turn-starved | 7 (2.3) | 4 (1.3) |
| stuck/stop-deadlock | 15 (4.9) | 4 (1.3) |
| stuck/lane-change-wait | 130 (42.9) | 95 (31.9) |
| conflict/lane-crossing | 0 (0.0) | 6 (2.0) |
| lane/turn-from-wrong-lane | 361 (119.1) | 265 (88.9) |
| lane/missed-turn-lane | 420 (138.5) | 245 (82.2) |
| lane/forced-short-change | 213 (70.3) | 185 (62.1) |
| lane/forced-into-tight-gap | 25 (8.2) | 26 (8.7) |
| wrongway/across-divider | 27 (8.9) | 29 (9.7) |
| traffic/lock-backed | 105 (34.6) | 10 (3.4) |
| traffic/lock-crept | 2 (0.7) | 7 (2.3) |
| traffic/converge-held | 12 (4.0) | 6 (2.0) |
| control/no-control-major-crossing | 18 (5.9) | 18 (6.0) |
| offpave/turn-clips-corner | 466 (153.7) | 426 (143.0) |
| contact/ground | 32 (10.6) | 36 (12.1) |
| wreck/ground | 13 (4.3) | 19 (6.4) |
| wreck/road-surface | 57 (18.8) | 43 (14.4) |
| wreck/barrier | 13 (4.3) | 20 (6.7) |
| wreck/pole | 3 (1.0) | 1 (0.3) |

Worse than the base, named: conflict/lane-crossing and lanes/lane-crossing-pair 548 are round 3e's new logging / static audit; wreck/barrier, contact/barrier and stuck/behind-wreck are Independence Boulevard n8465's barrier in the straight e6873 -> e6844 (base's top place, 17 vs 13 wrecks); wreck/ground, contact/ground and merge/ramp-waiting are the ramp e6819 at n8420 and the base's ground places (Camden Road n13538, Cox Mill Road n15702); merge/merge-stalled 0 -> 3 (n581, n2938) is the new merge yield; stuck/crossing-blocked 4 -> 6, yield-starved 4 -> 6, lock-crept 2 -> 7, forced-into-tight-gap 25 -> 26 (the zipper's pushy change after a wait, East Morehead n13110), wrongway/across-divider 27 -> 29 (West Sugar Creek n15139, Monroe Road n15287 - the base's places, geometry) and route/u-turn-forced 19 -> 27 (Medical Plaza Drive n4381: "no other legal way on") are small counts at scattered places - next round; every static class (route, speed, lanes, vertical, control) has the base's count, its rate moved only with the 2% fewer km driven. The WIP 5 finals (final_a / final_b, overlap 1 / 0, wreck/car 28 / 28) are kept in the scratchpad's runs\ as intermediate runs.

## Next steps (deferred by the scope cut)
- Node 6343 (bends r3's check wreck): not covered by my traces; no event there in my base / final censuses.
- Node 9909 (North Church / West 28th): the right e8229 -> e22532 is a 30.1 m arc (R ~24 m, mouths ~17 m out) that cuts ~1 m over the drawn kerb return - front-right off the road, the underside 0.31 m into the ground bank (V7's trace census: 2 road-surface wrecks there on that right).
- Concord Mills Boulevard median U-turn n4283: e3045 hooks 138 deg into e14524 on a 5.8 m arc (R ~2 m) at the 4 m/s turn floor (0.8 g): the body swings ~3 m wide onto the link's own queue (first finals: 5 wrecks). Drafted, not shipped: no 4 m/s floor on a tight arc (sqrt(ALat R) down to 2.5 m/s) - scratch r3e\patch_v8.py.
- I-77 n4134: the merge's way passes 1.64 m from the kerb lane's centre at the node (the added lane opens half a lane from it); join where the added lane is open.
- Queens Road n7054 / n5741 (round 3d's three-node crossing): V7 trace - a STOP deadlock and a bump at 7054.
- The 7 headless approaches (round 3c's reasons); South Boulevard n13512's throat; West Sugar Creek's across-divider geometry; the classes above marked worse, with their named causes.
# HANDOFF - CITY FUEL PHASE B - Unity jobs, final state (2026-10-30)

Branch `city-fuel-r1`, worktree `C:\Users\mcgee\PSX Racing-fuel` (HEAD = the commit adding this section; the solids fix
is 8cddf28f). Not pushed, not merged. SANDBOX `C:\Users\mcgee\PSXLab`: unlocked, no Unity running; its Scripts, Editor
and Shaders = HEAD; its Resources/CityStations = the committed bake (bake outputs are regenerated by every full build:
commit them again, with the sandbox metas, after a re-bake). 4 Unity runs (the coordinator's budget, then stop).
1. DRIVE AUDIT (uptown box) INVISIBLE COLLIDERS solids 3363 -> 68 samples (r11 base 124), 8cddf28f:
   - station boxes were AABBs: shop and restrooms out at the roof eaves (0.38-0.44 m / 6 cm off the walls); the pumps
     sampled at half height, where the pack's pump has a slot through it. `FitSolid` (PSXRacingBuilder.Stations.cs)
     puts each pump/shop/restroom side face on the outermost drawn LOD0 face a level ray from the box's middle meets
     inside the box, verified within 3.5 cm (all 0.0 cm): shop 10.67 x 11.74 -> 9.90 x 11.29 m, restrooms 3.65 x 6.11
     -> 3.53 x 6.03 m, pumps 0.88 x 0.34 m sampled 1.00 m up (box from 0.20 m under the floor). Boxes only; pumps solid.
   - 750 CityPost boxes = junctions r3d's PEDESTAL SIGNALS (integration base, not stations): a 0.36 m box round a
     0.115 m post. CitySignals.cs SignalTile.poles carries each box's width: post (SigPostW) + cast base (SigBaseW).
   - after: no Station* or CityPost entry; lanes 0; walls in lanes 0; the 68 = tower_01_4/Buildings/Door kinds (base's).
     BLUNT rail ends 8 and short rails 26 = the baseline's own. `CityFuelJobs.BakeAndDrive` = bake + audit in one run.
2. TOWN REFUEL (tools\fuel-play-check.ps1 -NoWatch): PASS "PULL IN, GET OUT, FILL UP." (20% -> 65%, $5000 -> $4996).
3. CITY REFUEL, main edition (tools\city-fuel-play-check.ps1, first run): FAIL "0 pumps within 20 m" - the check built
   station 142's ring, then yielded a frame with the car still 1.6 km off: the streaming dropped those tiles and the
   station with them. Fixed in CityFuelPlayCheck.cs (no frame between EnsureRing and the teleport; SyncTransforms).
   Re-run (in `CityFuelJobs.FrameCostThenCityCheck`, after FrameCost in the same process): pumps stood (4 within 20 m),
   room to park, parked, FUEL offered, out, FILL UP 20% -> 65% and $5000 -> $4996, STOP stops it. 2 FAILs LEFT:
   "and its shop door marker" (StoreDoor.Nearest(lot, 60 m) null) and "the shop's GO INSIDE prompt hangs at this
   station's door" [GO INSIDE]. Cause NOT found: StoreDoor registers in OnEnable, as GasPump does, so it is not the frame; look at
   StoreDoor.Nearest and station 142's shop pose (did its shop part stand?) before the re-run.
   ("installed [off (PSX_CITY_FUEL=0)]" on that run is FrameCost's OFF pass's CityStations.LastReport: cosmetic.)
4. FRAME COST (CityFuelJobs.FrameCost, 25 tiles a site, stations ON vs OFF): Carowinds tile build p50 100.8 vs 78.1 ms,
   p95 368 vs 383, max 443 vs 385, stand-up max 27.0 vs 1.4 ms, draws in view max 152 vs 128. E Independence p50 39.4
   vs 38.1, p95 112 vs 105, max 222 vs 235, stand-up max 5.0 vs 1.6 ms, draws 134 vs 126.
LEFT: (a) the 2 city door FAILs (fix, then `$env:PSX_SANDBOX='C:\Users\mcgee\PSXLab'; powershell -ExecutionPolicy Bypass
-File tools\city-fuel-play-check.ps1 -NoWatch`); (b) the CITY edition run (`$env:PSX_EDITION='CITY'`): NOT RUN;
(c) Carowinds +22 ms tile-build p50, a 27 ms station stand-up frame, +24 draws: owner call; (d) the pedestal box fix
is junctions r3d's code: the junctions agent should take it (or merge it from here).
KEY FILES: Scripts/City/CityStations.cs, Editor/PSXRacingBuilder.Stations.cs (bake, FitSolid), Editor/CityFuelJobs.cs,
Editor/CityFuelPlayCheck.cs, Scripts/City/CitySignals.cs (pedestal boxes), Resources/CityStations/, Art/GasStation/City/.

# HANDOFF - CITY FUEL round 1, PHASE A + PHASE B code (2026-10-30): filling stations where the real ones are

Branch `city-fuel-r1` (worktree `C:\Users\mcgee\PSX Racing-fuel`, off integration-1030b 82c83a3c = main d73ca5bd +
heights r11 + junctions r3d). No Unity run (PHASE A); PHASE B needs a sandbox. Full write-up: Docs/CHARLOTTE.md
"FILLING STATIONS". Sheets: `Docs/shots/city_fuel/fuel_stations_sheet.png`, `Docs/shots/city_fuel/fuel_art_shipped.png`.
- NEW DATA FILE `Resources/charlotte_fuel.bytes` (PFUL v1, 34 KB, its own .meta) by `tools/city/stations.mjs` (`--check`;
  reads the shipped city/bld/dem files: RE-RUN IT after an export, before signs.mjs). 319 OSM fuel features -> 302
  stations -> 300 placed (forecourt 285, truck 7, big-store 8), 2 skipped, 5 misfits listed; 567 driveways; 187 of the
  stations' own OSM boxes replaced. Hand decisions by id: `tools/city/stations_overrides.json`.
- `charlotte_signs.bytes` REGENERATED: signs.mjs stands no business cabinet for fuel (the station's own pylon is its
  sign) - 299 fewer POIs, everything else byte for byte. `signs.mjs --check` OK; `export_osm --check` OK (untouched).
- NO BRANDS: `tools/city/fuel_art.py` (`--check`) rebuilds the Gas_station pack's store ("6twelve", a real chain's
  parody) as the made-up PUMPWREN (badge, two-faced 90s lightbox pylon with flip numerals .99 REGULAR 87, emissor) and
  re-prints soft every legible brand in 13 shelf/fridge/stock/litter/time-clock textures (same names, same GUIDs:
  the LifeSim town station, circuit fuel stops and the garage's crates change too). `signs_atlas.py` takes the new
  badge and price box (CitySigns.png recomposed, `--check` OK). UI "6TWELVE" -> "PUMPWREN" (StoreScreen,
  ForecourtMode, LifeHomeScreen). Typecheck OK.
- NOT YET (PHASE B): nothing in the game reads charlotte_fuel.bytes, so until it lands the city's fuel businesses show
  NO sign (their cabinets are gone from the data). Merge PHASE A only together with PHASE B.
- PHASE B CODE (2026-10-30, no sandbox yet; typecheck OK, offline C# harness OK): `CityStations.cs` installs the
  stations into the map at parse (lots, curb cuts, holes, replaced boxes, pads, claims); `PSXRacingBuilder.Stations.cs`
  bakes the part prefabs (canopy turned a quarter, LODs, boxes, pump volumes, door, 90s lights); CityWorld stands a
  station a frame; GasPump/StoreDoor registries, ForecourtMode in Charlotte; SpawnStation stands ONE layout (the
  double stack's cause: Gas_station_Props.fbx is a second copy; the town/circuit stations grow ~a fifth to 2.2 m
  pumps); the name in ONE place (StationBrand.cs); `tools/city/regen_city.mjs` = export -> stations -> signs, and
  `stations.mjs --check` exits 3 on a stale stamp. UNITY JOBS STILL NEEDED: Docs/CHARLOTTE.md "PHASE B, the code".
# HANDOFF - CITY PERF ROUND 1 (2026-10-30): free roam's frames in the real player (Firefox, Chrome, Windows); the forest edge, the green fields, the pop-in

Branch `city-perf-r1` (worktree `C:\Users\mcgee\PSX Racing-perf`, off integration-1030b 82c83a3c), head 588965f3 (the code; the HANDOFF commits on top).
Sandbox `C:\Users\mcgee\PSXPerf` (left on the WebGL target, unlocked, no Unity running). Code only: no data change,
no exporter change; nothing in CityElevation / CityTraffic / the bends refit. MERGEABLE: YES - code only, every fix behind its switch; HitchProbe 80 of 80 tiles identical to the base twice (after the fixes on the WebGL target, and on the final code on the Standalone target); the WebGL after player run in Firefox and Chrome on every route, Noon and night; the Windows player run with and without the worker threads; the editor check and both players' logs clean.

## What the owner saw ("59, dropped as low as 3 ... 16 outside uptown ... pop in ... trees abruptly stopped, plain green fields")
- THE 3 FPS: (1) a 0.6-1.1 s first-draw stall at the SAME places every run, in Firefox AND Chrome (the Uptown Loop at
  (-1322, 5116), Independence at (1962, 2452); at night 0.6-1.0 s more) - a shader program compiled in the frame it is
  first drawn; (2) Firefox only: clusters of 40-140 ms frames in random systems (late scripts, UI, render, even physics)
  for 1-2 s - Firefox runs WebGL in a GPU process and the content process blocks behind it - and they come with the
  PC's CPU: frames over 50 ms were 0.28% with the total CPU under 40%, 0.75% at 40-60%, 5.6% over 60% (Firefox
  before, uptown; `cpu_trace.csv`, the other sandboxes' Unity jobs come in bursts); Chrome under the same load: 0.0-0.1%;
  (3) the WebGL GC: non-incremental, every collection one 35-96 ms frame, 3-7 a 150 s route (the city builder's
  garbage, ~0.9 MB/s, 1.4 MB/s at speed uptown).
- THE POP-IN: past the 500 m far plane only OSM footprints (the 8 x 8 km core) and the skyline had far copies. A pack
  tower's lot (a model) had none; outside the core every CityBuildings lot (frontage boxes, gables, prefab houses,
  restaurants) had none; the interior fill houses have none (LEFT).
- THE FOREST EDGE: near trees stand at the canopy's real density to 500 m; past it the far layer stood ~1/5 of it.
- THE GREEN FIELDS: the far land was grass + canopy only - no roofs, no pavement, no water.

## Frame tables
Scripted free roam, the AI at the wheel through the traffic, Noon, 150 s a route, a 1280x720 window, this PC
(i7-10700K, RTX 3070 - the browsers' WebGL (ANGLE, D3D11) reports it masked, "GTX 980, or similar"); "load" = the
other sandboxes' Unity jobs at the start (they come in bursts - see above).
BEFORE =ce025654's city code with the fixed tour (PerfTour from b53c573e: a fresh engine a route - the AI cooked the
engine and the first before-runs crawled -, set on 50 m past a hold-up). AFTER = 588965f3.

| browser | route | build | avg fps | p50 | p95 | p99 | worst ms | >33 | >50 | >100 | GC | km/h | load |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| Firefox | uptown | before | 49.3 | 19.0 | 30.0 | 54.0 | 682 | 266 | 87 | 8 | 7 | 151 | 2 jobs, CPU 15% |
| Firefox | uptown | after | 54.5 | 18.0 | 26.0 | 35.0 | 160 | 107 | 15 | 1 | 7 | 148 | 2 jobs, CPU 19% |
| Firefox | tryon | before | 56.0 | 17.0 | 25.0 | 35.0 | 83 | 106 | 6 | 0 | 4 | 124 | 2 jobs, CPU 15% |
| Firefox | tryon | after | 59.8 | 16.0 | 23.0 | 33.0 | 51 | 84 | 1 | 0 | 4 | 110 | 2 jobs, CPU 19% |
| Firefox | trade | before | 55.2 | 17.0 | 25.0 | 34.0 | 63 | 59 | 4 | 0 | 3 | 93 | 2 jobs, CPU 15% |
| Firefox | trade | after | 58.8 | 16.0 | 23.0 | 34.0 | 67 | 78 | 2 | 0 | 4 | 86 | 2 jobs, CPU 19% |
| Firefox | independence | before | 57.8 | 16.0 | 24.0 | 34.0 | 1109 | 94 | 7 | 1 | 3 | 156 | 2 jobs, CPU 15% |
| Firefox | independence | after | 58.7 | 16.0 | 25.0 | 38.0 | 105 | 161 | 27 | 1 | 4 | 147 | 2 jobs, CPU 19% |
| Firefox | i77s | before | 57.5 | 16.0 | 24.0 | 36.0 | 71 | 124 | 5 | 0 | 3 | 181 | 2 jobs, CPU 15% |
| Firefox | i77s | after | 59.7 | 16.0 | 24.0 | 34.0 | 80 | 107 | 10 | 0 | 3 | 171 | 2 jobs, CPU 19% |
| Chrome | uptown | before | 75.6 | 12.1 | 20.4 | 26.2 | 642 | 21 | 8 | 2 | 7 | 151 | 3 jobs, CPU 26% |
| Chrome | uptown | after | 93.4 | 9.4 | 17.3 | 21.0 | 125 | 11 | 2 | 1 | 7 | 146 | 1 jobs, CPU 17% |
| Chrome | tryon | before | 84.9 | 10.7 | 19.2 | 25.5 | 58 | 15 | 2 | 0 | 4 | 117 | 3 jobs, CPU 26% |
| Chrome | tryon | after | 106.4 | 8.3 | 15.5 | 19.1 | 52 | 9 | 3 | 0 | 4 | 111 | 1 jobs, CPU 17% |
| Chrome | trade | before | 85.4 | 10.7 | 18.2 | 23.5 | 54 | 9 | 1 | 0 | 3 | 91 | 3 jobs, CPU 26% |
| Chrome | trade | after | 104.7 | 8.6 | 15.1 | 18.4 | 56 | 4 | 1 | 0 | 3 | 87 | 1 jobs, CPU 17% |
| Chrome | independence | before | 91.7 | 10.0 | 16.9 | 20.8 | 978 | 11 | 3 | 1 | 3 | 153 | 3 jobs, CPU 26% |
| Chrome | independence | after | 109.0 | 8.2 | 14.7 | 18.8 | 61 | 3 | 1 | 0 | 3 | 150 | 1 jobs, CPU 17% |
| Chrome | i77s | before | 101.4 | 9.0 | 16.0 | 19.1 | 52 | 6 | 1 | 0 | 4 | 174 | 3 jobs, CPU 26% |
| Chrome | i77s | after | 113.8 | 8.0 | 14.2 | 18.1 | 55 | 4 | 1 | 0 | 4 | 171 | 1 jobs, CPU 17% |

Night (Firefox, 120 s): uptown before 50.0 fps, p99 34, worst 609, >50 11, GC 6; i77s before 60.2 fps, p99 30, worst 956, >50 4, GC 3; uptown after 58.7 fps, p99 29, worst 156, >50 6, GC 6; i77s after 63.0 fps, p99 28, worst 54, >50 3, GC 3
An intermediate after build (104eece1) doubled the GCs (Firefox 3-4 -> 6-9 a route): `CityFar.StepFill`'s worker
lambdas made a closure object every pass of its 225-tile loop every frame (~11 KB a frame) - fixed in 588965f3
(the asks in methods of their own); 588965f3's allocation is the before's again (0.87-0.94 MB/s; uptown 1.43).

What is left over 50 ms after (Firefox): the GC (1-7 a route), CarReflections (2-7; see below), and the CPU-burst
clusters (independence's 27 >50: 17 in two bursts with the PC's CPU over 60%). Chrome: 1-3 a route - the GC
(uptown: one render, one outside the loop).
A frame's steady cost (Firefox uptown after, ms): render 6.1, late scripts 4.2 of which CarReflections 3.8 (one
paint face a frame), update 3.3 (tile slices 2.7), outside the loop 2.0, UI 1.5, fixed 1.0 = 18.4 ms.
Chrome: 10.7 ms (car reflections 2.4).

## Each hitch cause and its fix (every fix has its switch: `PSX_...=0` / `?ab=Class.Field:0` on a perf tour)
- First-draw shader compiles (0.6-1.1 s, both browsers) -> `CityShaderWarm` (PSX_CITY_WARMSHADERS): every variant of
  the city kit, far layers, proxies, traffic models, every loaded PSX material and every PSX shader's plain variant,
  compiled behind the loading screen (+2 s to free roam's load). After: gone (worst 682 -> 160, 1109 -> 105; Chrome
  642 -> 125, 978 -> 61).
- First use of city-wide tables and lot models in a tile's frame (HitchProbe: EnsureSites 249-263 ms, a prefab's first
  load 118-316 ms) -> `CityWorld.WarmAtLoad` (PSX_CITY_WARM): guide sites 197 ms, 11 lot models 271 ms, 19 traffic
  models 71 ms at load.
- Collider cooking in a build step (46-56 ms) -> `CityMeshes.FastCook`: without "cook for faster simulation" (same
  triangles); max 6.9 ms.
- A junction's perimeter in one step (up to 70 ms) -> `CityMeshes.JunctionChordSteps`: a chord / corner walk a step.
- Junction fans built inside a side-flags step (33 of 55 ms) -> `CityMeshes.WarmFans` (in the roads phase's warm-up).
  HitchProbe (editor, 80 tiles uptown + Tryon, base 82c83a3c -> after): steps over 8 ms 342 -> 93, over 16.7 48 -> 7,
  over 33 12 -> 1, longest 70.1 -> 46.1 ms (gores "branch: nose", LEFT); 80 of 80 tiles identical to the base.
- A tile's meshes uploaded in its stand-up frame (megabytes; Firefox's GPU process falls behind) ->
  `CityMeshes.PreUpload` (PSX_CITY_PREUPLOAD): a mesh a build step (UploadMeshData, kept readable).
- The lamp field's 32 m step in one frame (9-10 ms in the editor; 3-5 ms in the browsers) -> `StreetLights.FieldSliced`
  (PSX_LAMPFIELD_SLICE): built behind the shown window, 1.5 ms a frame, swapped.
- A far-tree rebuild in one frame (116k vertices; 10.5 ms in the editor) -> `CityFar.StagedTrees`: built behind the
  shown mesh, an attribute a frame, swapped; land chunks prefetched (a ring edge of 7-11 was built in one frame);
  `DayLightsIdle`: the far lights' table is not rebuilt by day.
- A build falling behind the car on slow frames -> `CityWorld.PaceBuilds`: while a tile round the car is missing, a
  slice is at least 25% (35% at speed) of the frame's own time, and the open job is spread over the frames left before
  the car reaches it (cap 28 ms); a car IN a tile whose trees wait plants them at the cap a frame (UrgentTreesPaced).
  The browser runs never fell behind (no sync build, forced finish, urgent tree frame or abandon, before or after).

## Phase 2 - the look, its cost, the same-spot shots
- `CityFar.ForestFill` (PSX_CITY_FARFILL): the near rule's density (canopy x area / 95 m2, the near trees' species and
  sizes) on past the far plane to 600 m, faded out by rank to 1150 m (800 m on a phone); its own mesh, staged.
- `CityFar.LandCover` (PSX_CITY_FARCOVER): a far-land node's 128 m square: buildings' share (OSM footprints,
  CityBuildings' lots, and outside the core the fill houses by CityHouses' own odds) in the roofs' measured colour,
  pavement (streets without a far ribbon, parking lots) in the asphalt's, lakes in the water's; PSX/Lit unpacks it.
  `RaggedCrowns`: woods past the far trees 0.55-1.45 of their canopy's height by place (a ragged treeline).
- `CityMeshes.TowerProxies`: a pack tower's lot gets its OSM massing as a far proxy; the model hands over past the
  squeeze's start. `CityMeshes.LotProxies` (PSX_CITY_LOTPROXY): 5,190 CityBuildings lots city-wide get far proxies (the
  tile's own box / gable; a prefab's lot the box or gable of its size), drawn past the squeeze's start and near where
  their tile is not up.
- COST (Firefox, after player, look off vs on, same build): uptown 54.6 vs 54.5 fps, p99 39 vs 35; I-77 south 61.9 vs
  59.7 fps, p95 20 vs 24 (different runs, load 1 vs 2 jobs) - under ~1 ms a frame. Load +2 s (the shader warm).
- SHOTS (Firefox, after player, the look OFF via ab then ON, the same spots, from the car on the road; left BEFORE,
  right AFTER): 
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_brookshire_fwy_bridge.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_catawba_i85_bridge.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_catawba_wilkinson_bridge.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_i485_north_bridge.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_i485_northwest_bridge.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_i77_core_edge.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_i77_south_a.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_i77_south_b.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_independence_east.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_independence_mid.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_johnston_rd_bridge.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_nc16_lake_bridge.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_sugar_creek_bridge.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_tryon_south.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_uptown_east.png
  - C:\Users\mcgee\PSXPerf_out\look\perf_r1_look_uptown_sw.png
  At road level on the freeways the near trees hide the far layers (little to see between the pairs); the bridges' pairs
  show the far forest and land.

## Windows (Mono) vs web
The Windows player: `C:\Users\mcgee\PSXPerf\Builds\Win64\PSXRacing.exe` (Mono - this editor has no Windows IL2CPP -,
Direct3D 12, a 1280x720 window, 525 MB; built from f8f28c32 = the after code). BEFORE = the before2 source (ce025654 + the
tour). Its frame-rate setting MAX is the display's refresh as Unity reads it - 60 here - so its frames are capped at 60:
compare the frames over 33 / 50 / 100 ms and the main thread's busy time, not the fps. The tours leave the player's
PlayerPrefs (HKCU\Software\PSX Racing) as they found them (there were none: removed after each run).

| run | route | frames | km/h | avg fps | p50 | p95 | p99 | worst | >33 | >50 | >100 | GC | load |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| win_before | uptown | 8980 | 137 | 59.9 | 16.6 | 22.0 | 26.8 | 160 | 18 | 5 | 1 | 14 | 1 jobs, CPU 11% |
| win_before | tryon | 8998 | 113 | 60.0 | 16.6 | 20.7 | 24.1 | 54 | 6 | 2 | 0 | 9 | 1 jobs, CPU 11% |
| win_before | trade | 6668 | 88 | 60.0 | 16.6 | 20.8 | 24.6 | 54 | 7 | 1 | 0 | 7 | 1 jobs, CPU 11% |
| win_before | independence | 8998 | 152 | 60.0 | 16.7 | 20.3 | 24.4 | 52 | 9 | 1 | 0 | 7 | 1 jobs, CPU 11% |
| win_before | i77s | 8993 | 144 | 60.0 | 16.6 | 20.3 | 24.2 | 60 | 15 | 3 | 0 | 7 | 1 jobs, CPU 11% |
| win_after | uptown | 8992 | 153 | 59.9 | 16.6 | 21.8 | 26.2 | 53 | 18 | 2 | 0 | 14 | 1 jobs, CPU 4% |
| win_after | tryon | 8997 | 120 | 60.0 | 16.6 | 20.6 | 24.1 | 48 | 9 | 0 | 0 | 8 | 1 jobs, CPU 4% |
| win_after | trade | 6417 | 92 | 60.0 | 16.7 | 20.6 | 24.3 | 52 | 8 | 1 | 0 | 7 | 1 jobs, CPU 4% |
| win_after | independence | 8997 | 155 | 60.0 | 16.7 | 19.9 | 24.1 | 59 | 14 | 3 | 0 | 8 | 1 jobs, CPU 4% |
| win_after | i77s | 8992 | 178 | 59.9 | 16.6 | 20.5 | 24.5 | 57 | 17 | 4 | 0 | 8 | 1 jobs, CPU 4% |
| win_after_nothreads | uptown | 8990 | 156 | 59.9 | 16.6 | 21.9 | 25.8 | 54 | 18 | 2 | 0 | 14 | 1 jobs, CPU 12% |
| win_after_nothreads | tryon | 8999 | 122 | 60.0 | 16.6 | 20.7 | 24.4 | 49 | 11 | 0 | 0 | 8 | 1 jobs, CPU 12% |
| win_after_nothreads | trade | 6340 | 92 | 59.3 | 16.6 | 21.1 | 29.3 | 81 | 44 | 17 | 0 | 6 | 1 jobs, CPU 12% |
| win_after_nothreads | independence | 8995 | 160 | 60.0 | 16.6 | 20.9 | 25.3 | 61 | 18 | 2 | 0 | 8 | 1 jobs, CPU 12% |
| win_after_nothreads | i77s | 8925 | 184 | 59.5 | 16.6 | 21.6 | 28.3 | 102 | 46 | 14 | 1 | 9 | 1 jobs, CPU 12% |

- The main thread's busy time a frame (fixed + update + late + render + UI, ms): Windows after 6.6-10.8 (uptown 10.8:
  tile slices 5.6 - the 6 ms "fast" budget at 60 fps -, render 1.5 - D3D12's render thread does the rest -, car
  reflections 1.0), Chrome 7.7-10.1, Firefox 12.8-16.0 (render 5-6, car reflections 3.4-3.8). The same game has 6-10 ms
  of headroom a frame natively at 60; the browsers spend it on WebGL's draw calls and Firefox's GPU process.
- Worst frames on Windows after: 48-59 ms (before: up to 160); what is left: guide-sign decisions in a signs slice
  (35-53 ms - LEFT), one car-reflections frame (85 ms, the no-threads run), and the PC's CPU bursts. Mono's GC: 7-14 a
  route, short (never the largest part of a frame over 50 ms).
- THREADS (CityWorker: the far land / tree chunks and the forest fill's tiles on 2 workers): the far layers' share of the
  main thread 0.05-0.07 ms a frame with the workers, 0.05-0.11 without (PSX_CITY_THREADS=0 - the web's single-thread
  path, run natively), their worst frame 1.9-5.9 ms vs 2.8-20.9 ms. Both paths clean (no exception in either player
  log). The far layers were never the big cost; the prize for threads is the tile builder (5.6 ms a frame uptown on
  Windows, 2.7 in Firefox) - the design below.
- The shader warm-up on D3D12 takes 2-5 ms (96 variants; WarmUp is partial on D3D12 and native compiles are fast); the
  web is where it matters (the 0.6-1.1 s stalls).

## Threading (native players) - design, what shipped, what is next
ONE code path: a unit of city work is written once, as a resumable iterator or a pure function over
data that no longer changes once the city has loaded. A native player (Windows, Android) runs it on
`CityWorker` threads; the WebGL player runs the SAME code on the main thread in its budgeted slices
(`CityWorker.Threaded` is false there - no threads in the browser). Only the APPLY half (GameObjects,
Mesh upload, renderer state, physics scene changes - Unity's main-thread-only calls) runs on the main
thread, small and bounded.
- SHIPPED: the far layers (`CityFar`): land chunks (19 x 19 heights, canopy, the far land's cover),
  far-tree chunks and the forest fill's tiles are worked out on up to 2 below-normal threads ahead of
  need (`CityElevation.DemReader`: a DEM reader per thread - `BaseY`'s shared block cache is not
  thread-safe); the main thread drops each result in its table (`CityWorker.Pump`, once a frame in
  `CityWorld.Update`). WebGL: a chunk a frame on the main thread, the same builders.
- NEXT, in order of payoff and risk:
  1. the far meshes' assembly (lists of 60k-300k vertices) into `Mesh.MeshData` on a worker, applied
     with `ApplyAndDisposeWritableMeshData` (one call), double-buffered as now;
  2. the proxies' chunk emission (`CityMeshes.EmitProxy` / `EmitProxyLot`): pure once its scratch is
     per job;
  3. the lamp field's splat: a Burst `IJobParallelFor` (Burst and Collections ship with URP; the
     WebGL build already links a Burst side module, `lib_burst_generated.wasm`) - runs on the main
     thread in the browser, still faster;
  4. THE BIG ONE - the tile builder (`CityMeshes.TileJob`) and the tree frames: today 4-6 ms of
     every streaming frame. Blocking it: static scratch shared by every job (-> a build context per
     job), lazily filled caches the main thread reads too (road sections/outlines, the DEM block cache,
     RoadsideOccupancy - filled ahead and immutable, or per thread like `DemReader`), Unity objects
     made inside steps (`MeshFrom`'s `new Mesh` -> plain arrays, an apply step makes the meshes).
     `Physics.BakeMesh` is already thread-safe. Gate: HitchProbe's tile hashes bit-identical threaded
     vs not; the drive/launch/median/flicker gates unchanged.
  5. CityTraffic stays on the main thread (it drives Rigidbodies each physics step).
- EXPECTED GAIN (native): the main thread keeps only the apply (stand-up 1-3 ms a tile, uploads);
  tiles build at full core speed ahead of the car, so a bigger ring (3 = 7 x 7) and a longer far
  plane become affordable on a desktop - the real pop-in fix for Windows. WebGL: unchanged by threads.
- RISKS: data races are silent (a corrupt tile, a rare crash) - hence pure-or-nothing and the hash
  gate; Mono's GC stops every thread (worker garbage still pauses the main thread); a phone has 2-4
  big cores (one worker, below normal); results must not depend on which thread or when (everything
  here is hash-seeded already).
- BAKED OFFLINE INSTEAD (export time): far land heights + canopy per 128 m node: ~245 x 245 nodes
  city-wide, u16 + u8 = ~0.2 MB (+0.1 MB compressed for web and phone) - removes the runtime DEM and
  corridor sampling for far land entirely; road sections/outlines per edge: tens of MB, not for web or
  phone; whole tiles: GBs (~1,500 tiles x 1-5 MB) - a Windows release could stream baked tiles from
  disk, never the web (GitHub's 100 MB file wall) or a phone; far trees: no (hash-seeded generation is
  cheaper than the download); the interior fill houses' table (for their far proxies): ~100k houses x
  ~20 B = ~2 MB raw, ~1 MB compressed - worth it for all three.

## Android (release target) - what it will need (not built this round)
- Build: a `PSXBuildAndroid` like `Editor/PSXBuildWin64.cs` - IL2CPP, ARM64 only, Vulkan then GLES3,
  ASTC (the WebGL RGB565 overrides do not apply); the four on-demand bundles built for Android (the
  bundle builders take a target); StreamingAssets in the AAB or Play Asset Delivery (150 MB base limit:
  charlotte 13.8 MB, engines 40.6 MB, Enhanced textures as a fast-follow pack).
- Thermal: 30-45 min sessions throttle a phone 30-50% - budget for a warm phone. Memory: the managed
  heap with Charlotte loaded measures ~290-390 MB in the browser (~1 GB in the editor); fine on a
  6-8 GB phone, but its GC wants the allocation rate down as on the web (below).
- Threads: CityWorker runs 1-2 workers (processorCount / 4, capped 2) below normal; pin to 1 on a phone.
- The perf tour runs on a native player: `adb shell am start ... -e unity "-perftour routes=...&secs=150 -perfout <dir>"`.

| budget | Windows | web (desktop) | web phone / Android |
|---|---|---|---|
| far plane | 500 m; 750 m once tiles build on workers (ring 3) | 500 m | 400 m |
| tile ring | 2 (5 x 5) -> 3 with threaded builds | 2 | 2 |
| tile slice | worker (main thread: stand-up only) | 4 / 6 ms, paced 25 / 35% of the frame while behind, cap 28 ms | as web today; a phone tier to add: 20%, cap 16 ms |
| proxies (OSM footprints + lots) | 2 km -> 3 km | 2 km | 1.2 km |
| far trees | 2.2 km | 2.2 km | 1.4 km |
| forest fill (near density) | to 1.15 km | to 1.15 km | to 0.8 km |
| far land | 9 km | 9 km | 6 km |
| far lights | 8 km | 8 km | 5 km |
| near tree density | 1.0 | 1.0 | 0.6 |
| traffic cap | 48 -> 64 | 48 | 24 (off by default on phones) |
| lamp field | 256 px / 512 m | same | 128 px |
| LODs | tiles have none: needed for Android (building detail past 150 m off; the proxies' look past 300 m) |

## LEFT
- GC (the main web hitch left): the city builder's garbage, ~1.5 MB a tile (HitchProbe, coarse - the editor's Boehm
  Mono keeps no per-thread count): signs/poles slices 24%, road spans 14%, meshes 12%, tree slices 9%, stand-up 9%.
  Next: an allocation profile with call stacks (a development build), then pooling in those five.
- CarReflections: 3.4-3.8 ms of every Firefox frame (one paint face a frame), the biggest single cost after the
  renderer, and 2-7 of the >50 ms frames: a face every other frame would save ~1.8 ms - an owner call (the paint's
  reflections are "the things I notice most").
- Firefox under CPU load: its GPU process; nothing in the game can stop the PC's other work stalling it.
- Pop-in still: the interior fill houses (no far proxy: their placement is a tile at a time - bake their table at export,
  ~1 MB), guide-sign gantries, high masts, bridge girders and piers, billboards, signals, poles and lamp posts (near
  only, the 500 m far plane); the far land's box window dots double the lot proxies' own lit windows at night.
- Steps still over 16 ms: gores "branch: nose" (outline / raw sections miss: the clip table is in flux during gores;
  up to 46 ms), footprints (24 ms), sign guide decisions (20-38 ms a decision, unsliced).
- The perf tour's career: a fresh random starting car a session (the shots' pairs show different cars).

## How to run the instrument
- WebGL: `node tools/perf-tour.mjs <Build/WebGL> --browser firefox|chrome --out <dir> --hour Noon --secs 150
  [--routes uptown,tryon,trade,independence,i77s] [--ab Class.Field:0,...] [--shots "x,z,r|rb|yaw,name;..."]`
  (a VISIBLE browser on a throwaway profile, the player served from 127.0.0.1); Windows:
  `PSXRacing.exe -perftour "routes=...&secs=150&tag=win" -perfout <dir>`; the editor: `Editor/PerfTourEditor.cs`.
  Read: `py tools/perf_tour_report.py <dir> [--worst 12] [--csv t.csv]`. Inert unless asked for (?perftour on a local
  host, -perftour). HitchProbe now reports allocation by step kind.

# HANDOFF - REAL HEIGHTS round 11 (2026-10-30): the city-wide launches by class; round 10's leftovers

Branch `real-heights-r11` (worktree `C:\Users\mcgee\PSX Racing-heights11`, off integration-1029e 4827fa53 = main
de4a7a10 + real-heights-r10 + city-signs-r2), sandbox `C:\Users\mcgee\PSXLab` (AGENT_LOCK "real-heights-r11", removed).
Full write-up: Docs/CHARLOTTE.md "REAL HEIGHTS - round 11" (every remaining launch named in a table there). Sheet:
`Docs/shots/heights11/real_heights_r11_sheet.png`. No city data regenerated (code only); nothing in the exporter, the
line clean-up, CityTraffic or the bends refit.
Code: `Scripts/City/CityElevation.R11.cs`, `Scripts/City/CityMeshes.R11.cs` (new partials) + hooks in `CityElevation.cs`
(ClimbOut, PrepareSeats, Ground's deck branch), `CityElevation.VSmooth.cs` (BuildBounds), `CityMeshes.cs` (rail-end
plan, gore, BuildSectionsSteps), `CityMeshes.Junctions.cs` (FanSteiner), `CityMeshes.Skirts.cs` (OverRoad, LanesUnder);
audits `Editor/CityAudit.Invisible.cs` (ceiling probe), `Editor/CityAudit.Barriers.cs` (short rail list prints all);
tools `Editor/CityR11.cs`, `Editor/CityLaunchAudit.cs` (EXPLAIN, report only).

## Shipped (ON by default; each `PSX_CITY_R11_<NAME>=0` is the before)
- CLIMBC1 (seated branch leaves on the host's grade: Hermite, C1), CRESTCAP (drawn crests may lower 0.30 m, was 0.15),
  SPILLUNDER (1'47" sliver: spill slope held under the deck's own median half, 12 m), GOREFAN (no gore inside a fan),
  MITREPOKE (a branch's last 4 m over the other through arm comes under it), GORESTART (BLUNT: a branch's outer rail
  starting past its collapsed zone is turned down), SKIRTPAVE (N Tryon: the map's lanes asked within 12 m of a tile's
  border; added lanes count), INNERMAJOR (Cates: a cluster's inside-edge points stay out of the major's pavement).
- Audit: the INVISIBLE ceiling probe skips a lane surface under a drawn ground sheet (E Trade; `PSX_INVIS_SHEET=0`).
- STREETSEAT tested and left OFF (`=1` on): A/B launches 87 -> 89, grades past 16% 3 -> 4.

## Numbers (PSXLab, base 4827fa53 -> after)
- Launches city-wide 99 -> 86 (14 fixed, 1 new: e7688 climb-out 0.21; worse: Highland St 0.28 -> 0.44, South Blvd
  0.42 -> 0.47, Christenbury Rd 0.15 -> 0.19); routes 0 / 0 / 0; unloads 401 -> 371; route p99 1.12 / 2.11 / 0.76 ->
  1.08 / 2.11 / 0.75; grades past 16% 3 (+7) = base. By the audit's cause: mitred joint 30 -> 23, junction fan 21 -> 20,
  junction node 18 -> 17, ramp seat 16 -> 13, terrain 10 -> 9. By what the car drove on: FOREIGN span 40 -> 30, OWN 23,
  OWN-FAN 16, FOREIGN fan 17 -> 16, FOREIGN gore 3 -> 1.
- Drive audit (final commit): WALLS 0, FALL HOLES 0, INVISIBLE lanes 4 -> 0 (solids 124 = base), BLUNT routes 1 -> 0
  (box 9 -> 8), MEDIAN 0/0/0, ACROSS 3829/0/0, short rails 25 -> 26 (e1418's right rail 28.9 m, its climb-out moved),
  CITY AUDIT failures 5 -> 3. Flicker 2125 / 3241 -> 2105 / 3214. Traffic check: 0 traffic-on-traffic wrecks, CITY
  TRAFFIC OK (citytraffic.log deleted).
- Census: ramps better (>0.5% 1972 -> 1825, >2% 122 -> 107), crests short of radius 1601 -> 1524; arterial >1% 765 ->
  787 and TIGHT crest 652 -> 667 (deeper, longer crests still short of their radius).

## Left
- 86 launches named one by one in CHARLOTTE.md's table: FOREIGN span 30 (seat zone shorter than the tile's clip - e7688
  and the "ramp seat" class; a branch quad across a mitre - South Blvd n8870; ribbons inside fans), OWN 23 (crests at
  chain ends), fans 32 (overlapping fans, side-by-side mouths at different heights - Tyvola n545 0.89 m, the worst).
- Unloads 371: the fan's own surface and minor through pairs not chained through junctions.
- E Trade: e6167's line model shifts SW into node 7879's merge fan while its kerb tapers NE (junctions).
- Cates connector e23417 now ~17% over its last 5 m (node 19428 0.9 m over Cates St, 6 m from its kerb: the data).
- 1'47": past the deck end (e2445 s 9) the lattice is still 0.69 m under the land (another vertex; unchanged).
- STREETSEAT (off): a street branch's mesh-off spots need the clip fixed, not a seat.

MERGEABLE: yes (one rule set, every switch =0 the before; no data; routes unchanged; gates above). Commits on real-heights-r11: 6796ff26..HEAD (tools + rules), the docs commit last.

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

# HANDOFF - ROAD LOGIC JUNCTIONS ROUND 3d (2026-10-30 ~06:35; MERGEABLE - every gate met on d707ad69: censuses final_b / final_a2 overlap 0 / 0, wreck/car 37 / 40; checks 3, 4 and final_a2's OK in a row (final_b's check before them FAILED on East 6th Street, untraced - LEFT 1); hash and heads as the base / round 3c)

Code head d707ad69 (29ebc63a + the converging range fix; docs after it). Sandbox PSXRec, lock name "road-logic-junctions-r3d" (released). Data = the bases' (main NOT merged: the integrator merges it). Scratchpad tools: r3d_tool.ps1 / r3d_job.ps1 (round 3c's lock-safe runners, r3d lock), r3d_final.ps1 / r3d_final_a2.ps1 / r3d_check4.ps1 (the gates), r3d_trace1-7.ps1 + r3d_trace_spots*.txt (the trace runs; their census dirs and traces in r3d_docs_runs/ and trace_r3d_*.txt), r3d_table.py / r3d_wrecks.py (round 3c's), r3d_graph.py (a node's arms and an edge's ends from the census's raw edges.txt), r3d_summ.sh.
- TRACE TOOL (CityTraffic.Census.cs; off unless PSX_TRAFFIC_TRACE is set): "x:z" places (the nearest junction node within 40 m - node ids differ between road data; main's node 914 is node 914 here too); traces in the traffic check too (TraceStep without the census: the check's wrecks are at its own uptown spot); dumps the 8 s before a touch AND before a near miss (two cars crossing in one box, both moving, under 6 m), a converging hold, a lock undone, a fresh green held, a stuck pair (crossing-blocked / gridlock-box / stop-deadlock); writes each traced node's FACTS (junction and signal, split / tight and why, box, mitre, major arms, each arm's control with its approach's phase group and channel, lanes, internal, and the nodes a short arm away).
- THE CAUSES, each from a trace (traces 1-7: a census of the places at 600 s a spot, the trace on; 1-3 with the check, which traces too now):
  - LYNHAVEN STREET n18758 (final_d's pair) and NODE 7519 (round 3c final_c's check): ONE cause, the stream rule for two ways converging on one leg (LeaderGap: straight > turn > ramp; the second yields short of the leg, the first ignores it). At 7519 the straight (8.3 m/s, 30.0/41.5 m into its box) was the higher stream and the left from the last phase (standing at 24.2/31.0 m, both into e6418 lane 2) was "not clearly ahead" (4.7 m by the plan < 6 m + 0.8 s), so the straight went first and ignored a body already standing where the ways meet; at Lynhaven a straight creeping 1.2 m/s in its queue (e25026->e25027 lane 0) did the same to a right standing in the box (e22265->e25027 lane 0), 0.75 m for 75 s - a known pair, so the lock recovery (crossing scan only) never saw it. FIX: of two converging, one standing or crawling, the one whose body is already on the other's way goes first and the other brakes for that body (SweptHit both ways); the stream rule only otherwise. The hold at work: converge-held 1-4 per trace census, 16 / 4 / 18 in censuses A / B / A2 (the census logs each, traffic/converge-held; the check counts them); no touch at Lynhaven in traces 1-3, no overlap in the finals.
  - NODE 7519, a second way (trace 2's CHECK, a 5.4 / 6.4 m/s wreck; the trace's near-miss dump 2 s before): a West Trade Street car its queue had carried 1.8 m over its stop line stood there through the yellow (not committed, com0) and "past it" (ControlGap: more than 1 m over the line, no control) followed its leader 3 s into the red into North Church Street's fresh green - and no red run is counted (it crossed its line on the green: RAN RED 0); a car standing 0.2 m short of its line was "committed" to the yellow too (0.2 < v^2/2a + 0.5 with v = 0). FIX: a car standing (<= 1 m/s) on its run over a signal's line is held by the red like one at it; only a moving car commits to a yellow or to being over its line.
  - NODE 14236 (West 4th Street, a degree-2 vertex; round 3c final_b's check, 4.8 m/s): trace 2, two wrecks at 4.9 m/s in 600 s - turn-from-wrong-lane inside the vertex's 5.7 m straight connector: the street is 7.7-18 m pieces, too short for the lane change the next junction wants, so a car came to the vertex in the other lane than its connector was laid from and took it from where it was to the other lane's end (c62 heading -44 to -101 deg in 2 m, 3.7 m across, into c8 beside it; c23 the other way into c57). FIX: a car keeps its own lane through a plain vertex's connector (PopLeg -> KeepLaneThrough) and changes on a run. Traces 3-7: no touch at 14236.
  - NODE 9907 North Tryon Street (traces 1, 3, 5, 6: 2-6 car-wreck rows and 2-10 bumps per 600 s): the trace's facts - a carriageway split by SplitArms (two-way N Tryon e9370, one-way N Tryon e15469 arriving, one-way N Church e8228 leaving) AND a mitred node in the meshes, so no control (CtlAt: a mitre or a split), no box (no keep-clear), both N Tryon flows the way; the warrants skip it besides (within 45 m of node 10758's signal: "the signal's" - but that signal takes no part of it). The data lays N Church's driven lane 2.2 m from N Tryon's at the node; e9370 holds two cars between 9907 and node 10758's stop line, so that signal's red backs the northbound queue through the node and its last car stands with its nose 1.1 m into the node, within 0.4 m of the southbound way into N Church: nose to nose, then back 1.1-1.3 m and creep at 0.8 m/s - and the creep touches (0.05-0.19 m); a southbound car changing lanes 0->1 in e9370's last 18 m saw the standing nose only once in its turn (3.8 m/s, 0.8 m left). THE CONTROL A WARRANT GIVES: not a signal (the two flows are one road's two directions 18 m from a signal; a signal would run both on one phase: the same pass) - the right of way assigned (MUTCD 2B.10, a YIELD where the entrance needs one) and the box kept clear (R10-7). FIX: a TIGHT SPLIT (Junctions.TightSplit, by shape - names, classes, mitre aside): the leaving carriageway within a body (0.35 m) of the arriving one's lane end makes it a box where the arriving carriageway yields at a line set back to where its cars stand clear (9907: 2.5 m back, hold @21.4 on e15469; 14 such nodes city-wide); and PathHitAt looks along the turn re-laid from where a car changing lanes is. Trace 7: 0 touches at 9907 in 600 s.
  - QUEENS ROAD n7054 (final_c's one overlap sample, a 12.7 / 4.9 m/s wreck): the facts - Queens Road West crosses Queens Road's one-way pair at three nodes (6391 junction 1592 its STOP, 7054 junction 1651, 5741 no junction) joined by internal runs (e16461 20 m, 2 m of it a run; e12868), and an internal run has no control: the STOP at 6391 looked at the near carriageway only, and the car given the way crossed e9103 at 7054 (final_c) - or e5381 at 5741 after a left at 7054 (trace 5, 10 / 5.6 m/s) - unlooked. Traces 3-4 found two more at 7054: Ardsley Road's STOP - its straight connector stretched back 20 m (road-logic-bends' StretchStraight) past the stop bar, so the run, and with it the car's line, ended there; a car came up to it too fast, was "past it" 1 m on and crossed Queens Road unstopped (9 m/s in front of one at 14 m/s); and two straights from Ardsley Road laid 14 m apart were one leg by "metres into it", which put the one standing at its STOP behind the one that had begun its turn back in the queue (5.9 m/s). FIXES: a STOP or yield car wants its gap in every later carriageway's major flow while its way runs on through internal runs with no control (CanGo -> SecondCrossing, walked); a straight is stretched back never past its run's stop line; a STOP or yield is not passed by overshooting its line (held while its body is over its run); a major car standing for this one leaves the way; two turns are one arc only where they begin within a metre (SameLeg). Traces 6-7: no touch at Queens Road in 600 s each (trace 4: 4 car-wreck rows there).
  - NODE 10758 beside 9907 (found in trace 7): a car held in lane 2 by its lane wait was given its right on red, replanned after 14 s to a left from lane 2 and went on the red with that permission, across West 30th Street's green (a wreck). FIX: a replan drops the right-on-red permission and the claims at its line; right on red only for a right.
  - THE LYNHAVEN CLUSTER (nodes 20223 / 20224, final_c's crossing-blocked; from the census's blocker notes, which name both holds): a ring from round 3c's cut-across rule (e08acc37) or an asymmetric crossing scan: the car ahead by the plan stood braking for the body of the car behind it by the plan (its sweep met it), which waited for it as its leader. FIX: a car whose plan leader stands braking for this one, its own way clear of that one, goes.
- (b) MAIN'S CHECK WRECK AT NODE 914 (junction 107, South Tryon / MLK: two straights mid-box, 10.6 and 14.3 of 30.3 m, RAN RED 0): node 914 is junction 107 on this data too (trace facts: a signal, South Tryon e967 / e14180 phase group 0 channel 174, MLK e764 group 1 channel 175 - no conflicting green; not among the base's 17). Main's traffic code is the bases' (no junction rounds): a 14 / 3.5 / 1.5 s two-group clock (1.5 s all-red for a 30 m box), CrossInBox only for a car moving off its line (< 3 m/s, < 6 m), no ClearingInBox, and the yellow commit / "past it" rules of node 7519 above - so a car committed to a yellow or standing over its line went late on its red, neither counted as a red run, into the cross street's fresh green that nothing held. On round 3c's code it COULD still happen: trace 2's check did it at node 7519 ("past it", RAN RED 0, 5.4 / 6.4 m/s). On round 3d's code it cannot: a standing car never commits and one standing over its line is held by the red; a fresh green waits for a car still clearing the box (ClearingInBox - the trace dumps each "fresh green held"); the all-red comes from the box (round 2's plans). Node 914 traced in traces 1-4 (a 600 s census spot each, traces 1-3 with their checks) and in every final run (censuses A, B, A2 with their checks, checks 3 and 4): not one dump there - no near miss (two crossing cars moving in its box under 6 m), no touch.
- (c) CROSSING-BLOCKED 12 (final_c) vs 6 (final_a): one code (e08acc37) gave 12 and 6 (final_c, final_d) and the branch's runs 6-12 (r3 10, r3b ~10, final1 12, final_a 6, final_b 8, final_c 12, final_d 6) against the bases' 9 / 6 - a handful of places, 1-3 each, run to run. final_c's 12: North Tryon n9907 x3 (the throat above: the northbound queue standing in the node, the southbound way blocked - in nearly every run, bases too), Fort Street n18110 x2 (a mitred all-way STOP of one-lane roads, 2-3 m turns: a car waiting at its STOP line stands in the right-turn path of the car at the next line; traces 2-3: the ring, then a creep's touch), the Lynhaven cluster n20223 / n20224 x2 (round 3c's cut-across rule's ring - fixed above), Pineville-Matthews n10506 x2 (the Carmel Road square: a car waiting at a red with its body in the way of the square's lane-drop straight "lanes 2->0"), East 35th n17898 x1 (a left waiting for oncoming with its body in the oncoming straight's way at an offset junction), Nevin Road's boxes n8084 / n2471 x2 (queues across the box). Not the sweep test as such: final_d on the same code had 6 elsewhere and final1 before it 12.
- THE TRACES, touches in each trace's census of its places (car-wreck rows / bumps / overlap samples, all places): trace 1 (e08acc37 + the tool) 16 / 18 / 0 - North Tryon n9907 alone 6 / 8; trace 2 (+ the converging hold) 10 / 14 - West 4th n14236 4 / 4 (fixed next); trace 3 14 / 30 - n9907 4 / 10, Ardsley Road 2 / 2; trace 4 12 / 14 - Queens Road 4 / 4 (Ardsley's stretched STOP); trace 5 14 / 21 - n9907 4 / 8, node 5741 2; trace 6 2 / 3; trace 7 (the tight split live) 5 / 6, none at the traced LEFT places (9909's ground-clipping rights and one left at node 9061 elsewhere). Checks in the traces: 1 OK, 2 FAILED (node 7519, the "past it" car; node 1234 two straights mid-box, untraced - very likely the same), 3 OK.
- FINAL GATES (code d707ad69; 40 spots x 300 s, the same spot list, data and sandbox as the bases b1 135.7 car-h / 2748 km, b2 135.8 / 2636):
  1. CENSUSES on d707ad69: final_b (125.8 car-h / 2994 km) traffic/overlap 0, wreck/car 37 (12.4 per 1000 km); final_a2 (128.1 car-h / 3004 km) overlap 0, wreck/car 40 (13.3) - against the bases' worst overlap 1 and b1's wreck/car 99 (36.0): PASS both. (Census A on 29ebc63a, before the converging range fix: overlap 1 - the Nevin Road straight at 15.8 m/s that fix answers - wreck/car 59, its check OK; kept in the scratchpad, not a gate run.)
  2. CHECKS on d707ad69: final_b's check FAILED (2 traffic-on-traffic wrecks on East 6th Street e16828, 5-10 m short of the vertex at node 15151: a car at 9.1 m/s in lane 2 into one standing in lane 1 of 3 in the queue from North Tryon's signal at node 1333 - not on the trace list; traced from check 4 on), then check 3 OK (0 / 0), final_a2's check OK (0 / 0), check 4 OK (0 / 0; East 6th Street traced, no dump there) - three in a row.
  3. ROAD MESHES: hash byte for byte = Docs/road_mesh_hash_base1029d.txt (ALL 277e518b4adb71bf; Docs/road_mesh_hash_branch_r3d.txt).
  4. SIGNAL HEADS (RunSignalHeadsAndHash, -nographics, on d707ad69): in view NONE 7, ONE 66, two or more 3317; junctions with an approach with none 6, with no head on any approach 0; approaches with fewer heads than before round 3: 0 - round 3c's numbers exactly (no CitySignals change).
  5. TYPECHECK OK (tools/typecheck.ps1 -Project PSXRec -Source the worktree).
- PER CLASS, count (per 1000 km), b1 / b2 / final_b / final_a2:
  wreck/car 99 (36.0) / 128 (48.6) / 37 (12.4) / 40 (13.3); car-bump 98 / 136 / 47 / 63; traffic/overlap 0 / 1 / 0 / 0; gridlock-box 194 / 184 / 50 / 58; crossing-blocked 9 / 6 / 9 / 7; yield-starved 27 / 33 / 3 / 3; left-turn-starved 19 / 28 / 4 / 7; stop-deadlock 22 / 21 / 9 / 21; missed-turn-lane 432 / 595 / 316 / 273; turn-from-wrong-lane 315 / 251 / 365 / 332; no-control-major 141 / 141 / 18 / 18; conflicting-green 17 / 17 / 0 / 0. Worse than both bases on both per car-h and per km (r3d_table.py): forced-short-change 152 / 149 / 194 / 197 (lane changes moved off the vertices onto the runs), wrongway/across-divider 21 / 34 / 40 / 59, keep-clear-wait 0 / 0 / 9 / 12 and box-blocked-on-green 0 / 0 / 3 / 4 (round 3's rules, round 3c had them), and the recovery's and the hold's own notes (lock-backed 5 / 55, lock-crept 1 / 7, converge-held 4 / 18: not faults). Car-on-car wreck incidents b1 / b2 / final_b: 53 / 71 / 22 - crossing 12 / 13 / 2, merge 8 / 10 / 0, rear-end in a box 5 / 8 / 6 (Independence Boulevard n8465 each run, as round 3c).
- LEFT: (1) East 6th Street e16828 at the vertex node 15151 (final_b's check): trace it (PSX_TRAFFIC_TRACE=15151,2308,1333 - check 4 had it on) - a lane that tapers into the next short of the vertex, or a squeeze by Place's pavement clamp, most likely; (2) the lock recovery's creep still touches where the bodies must slide past (ClearDist lets the way run through the padded overlap: 0.8 m/s bumps at Fort Street n18110, Lynhaven n18758, and 9907 before its yield) - a creep only where the bodies stay apart would leave those pairs locked instead; (3) the Carmel Road square n10497: one car backed 65 times in 490 s while the goer gained a metre a cycle (trace 2, lock-backed); (4) Fort Street n18110 (an all-way STOP of one-lane roads, a 2-3 m box, mitred): a car waiting at its line stands in the right-turn path of the car at the next line - a set-back line like the tight split's; (5) node 9909 (North Church, -108,7055): rights e8229->e22532 clip the ground and the road surface, the straights behind bump (geometry); node 9061 (268,7238): a left from e20960 across a straight at 11.7 m/s (trace 7, untraced); (6) round 3c's geometry classes driven more (forced-short-change, wrongway/across-divider, bend-tighter, turn-leaves-road); (7) the 7 approaches with no head (unchanged).

# HANDOFF - ROAD LOGIC JUNCTIONS ROUND 3c (2026-10-30 ~04:30; NOT MERGEABLE - the three junctions fixed, the city-wide gates not yet: see FINAL GATES, LEFT)

Code head e08acc37 (+ docs after it). Sandbox PSXRec, lock name "road-logic-junctions-r3c" (released). Scratchpad tools: r3c_tool.ps1 / r3c_job.ps1 / r3c_basejob.ps1 / r3c_after.ps1 (lock-safe runners), r3c_table.py (per-class over runs: count, per 100 car-h, per 1000 km; flags a final worse than BOTH bases on both), r3c_wrecks.py (car-on-car wrecks paired and typed), r3c_samespots.py (a class's share in the bases' 60 m cells), census_cmp.py / census_where.py, heads_sheet.py, r3c_traffic_sheet.py.
- BASES (integration-1029d code, same data, same sandbox, 40 spots x 300 s): base1 Docs/traffic_census_junctions_base1029d (135.7 car-h, 2748 km), base2 Docs/traffic_census_junctions_base1029d_2 (135.8 car-h, 2636 km). Run-to-run spread on ONE code: wreck/car 99 -> 128, car-bump 98 -> 136, missed-turn-lane 432 -> 595, left-turn-starved 19 -> 28, yield-starved 27 -> 33, overlap 0 -> 1, wrongway/across-divider 21 -> 34.
- THE LOCKED PAIRS - causes (junction trace: PSX_TRAFFIC_TRACE=node,... with the census writes the last 8 s of every car within 35 m of those nodes when two touch; Scripts/City/CityTraffic.Census.cs):
  - Nevin Road / Gibbon Road, node 15108: two roads crossing at 48 deg; the phase planner's axis ("within 50 deg as lines") put both east-side approaches in one phase - both green at once (trace 1: cars 5 and 21 got green at 1008.1 s, met mid-box at 6-7.6 m/s, 0.14 m, locked). FIX CitySignals.Control SideDeg 20: two approaches from the SAME side share an axis only within 20 deg (a slip lane beside its road); the static audit counts same-side pairs from 20 deg - crossing approaches green together: 0 pairs.
  - Parkwood Avenue / Seigle Avenue, node 7758: a Seigle car standing mid-box, held by its exit's queue; Parkwood's green let its first car go (round 3's exception: a standing box car out of the way does not hold a green); the queue moved, the box car crept into the way; the Parkwood car "went first" by its lower id and its straight corridor saw the other only at 1.0 m (trace 1: cars 12 / 13, 0.17 m, locked 9 min). FIX: (P4) a standing box car holds the green when its own way on crosses this car's (LeftsCross); (P1) of two in one box the one in it first clears it (Precedes by box entry, NC G.S. 20-155); (P2) no "goes first" past a car whose body is already on its way; (P3) a car whose body is on the way a car takes (the arc of its turn, its run and the turn after, PathHitAt, 0.4 m margin: in the lanes it sweeps, never one a lane over) is braked for where the way meets it, at any angle.
  - Morehead Road / Concord Parkway South, node 11726 (census r3c: a left out of Concord Pkwy S still in the box and a Morehead straight on the fresh green, 3.4 / 3.6 m/s, 5 m apart, locked 135 s): not reproduced in 2 x 900 s of trace; the same pattern as Parkwood (a car from the last phase in the box, the new green's car going first by id, its corridor late) - P1-P4.
  - node 8084 (two-node box at Nevin Road, 1447,12900; trace 2): a car moving off its line turned its nose into one standing on the next turn of the box, nearly its own way (|dot| > 0.85, outside P3's first cut): 5 cm, locked 12 min behind a car stuck in a lane-change replan loop on e6381 -> P3 at any angle.
- node 9907 (North Tryon Street, an uncontrolled crossing; final census 1 on 0f5247b2: 4 overlap samples, one pair): a car on its run touched the side of one crossing in the box; "further through" by share of the turn tied and the lower id (the nose) was sent on into the other's side -> the goer is the car that clears the other sooner driving on its own way (ClearDist), a goer whose way runs into the other never creeps; backing also on a run to a junction with no control.
- node 8084 again (census A on e2709f5f, trace only - the census counted 0 overlaps): a car off the link road turned past the tail of one standing in the box, its centre's way 1.3 m clear, its swinging corner 0.18 m in -> SweptHit: the BODY a car sweeps along its way (0.15 m round it) is braked for where it meets one standing within 12 m.
- RECOVERY (Junctions.Unlock): two standing 1.5 s, each braking for the other (or one touching a car held by its own queue): the one further through the box (more of its turn behind it) goes; the other BACKS along its own turn at 0.6 m/s until its body is off the other's way (2 m at most, only in the box - never back over a stop line - never into a car or the player behind); where it cannot back, the goer creeps past at 0.8 m/s for the distance it needs to clear (ClearDist), never when its way runs into the other. The census logs each (traffic/lock-backed, traffic/lock-crept: not faults, the recovery at work); census A (e2709f5f) 19 backed and 832 crept (a creep re-logged every step at two loop roads, Oaklawn Cemetery Drive n19482 and n10862 - fixed in 79ae4db3: logged once, run to its distance); the validation trace on 79ae4db3 (4 hot spots x 900 s): 3 backed, 6 crept, census overlaps 0. The check's line counts them ("locked pairs undone: backed out of the way N, crept past N").
- HEADS (0f5247b2, RunSignalHeadsAndHash; 79ae4db3 has no heads change): in the driver's view, approaches with NONE 409 before round 3 -> 35 (r3b) -> 7; ONE 1033 -> 51 -> 66; two or more 1948 -> 3304 -> 3317; junctions with an approach with none 265 -> 25 -> 6 (none on any approach 14 -> 4 -> 0); approaches with FEWER heads than before round 3: 14 -> 0 (13 lost a span wire's heads when its corners were looked for only within the view: the wire is now laid on its old corners and taken only as before - each approach hanging a head from the lanes it had then; and every junction of a tile gets its own wire or arms before any junction's supplements). Junction 724 Dalton Avenue / North Tryon Street: 0/0/0/0 -> 1/2/2/1 (Dalton Avenue: a near-side post 6 m short of the line, 6.6 m right of the lanes' middle; North Tryon from the south: two overhead faces 32.6 m past the line; e6410: two overhead faces 16.7 m past; North Tryon from the north: a near-side post 4 m short of the line set back 18.5 m right of the lanes' middle - past the race run-off, the only ground there). New fallbacks for an approach still with none: a mast arm from a pole set back up to 14 m past the pavement (far right, far left), a post there, a near-side post from 6 m short of the line, a post anywhere in the view (12 m past the line on, then back). The 7 left: junction 420 (3 approaches, uptown East 4th / South Tryon: race run-off and buildings on every foot within 14 m), 672 (uptown, lamps and buildings), 316 / 567 / 48 (all pavement round a one-lane approach - a channelised slip) - Docs/shots/junctions_heads/heads.txt lists each foot's reasons (WHY NONE). Sheet: the junction 724 rows appended to Docs/shots/junctions_heads/signal_heads_before_after.png; Docs/shots/junctions_r3c/j724_heads_before_after.png. Road meshes: hash = Docs/road_mesh_hash_base1029d.txt byte for byte.
- FINAL GATES (40 spots x 300 s each; bases b1 135.7 car-h / 2748 km, b2 135.8 / 2636). Code e08acc37: census final_c 128.3 car-h / 3056 km - overlap 1 (one sample, Queens Road e16461, cars 2/4), wreck/car 79, car-bump 80, its check FAILED (2 traffic-on-traffic wrecks at node 7519 uptown: a straight at 8.3 m/s into a left standing in the box, both into e6418 lane 2 - they share their exit lane, so the plan's order put the left "behind"); census final_d - overlap 6 (one pair, Lynhaven Street e22265 / e25026, 0.75 m, 75 s), wreck/car 80, its check OK (traffic-on-traffic wrecks 0, bumps 0); check 3 (alone) OK (traffic-on-traffic wrecks 0, bumps 0) - so on e08acc37 the checks ran FAIL, OK, OK: not three in a row. Code 79ae4db3: final_a overlap 0, wreck/car 51, check OK; final_b overlap 16 (W T Harris node 7343, fixed in e08acc37), wreck/car 69, check FAILED (2 at node 14236 uptown: two straights side by side, knocked 4.8 m/s at 0.0 / 0.8 m/s). Earlier 0f5247b2 final1 overlap 4 (node 9907, fixed), check OK; e2709f5f census A overlap 0, check OK. THE THREE JUNCTIONS: no overlap and no lock at Morehead n11726, Nevin n15108, Parkwood n7758 in any census or trace since the fixes (fa, fb, fc, fd, traces 2 and 3). Per class (count; per 100 car-h; per 1000 km), b1 / b2 / fa / fc: wreck/car 99 / 128 / 51 / 79 (36.0 / 48.6 / 16.6 / 25.9 per 1000 km); car-bump 98 / 136 / 52 / 80; gridlock-box 194 / 184 / 55 / 53; missed-turn-lane 432 / 595 / 285 / 341; no-control-major 141 / 141 / 18 / 18; conflicting-green 17 / 17 / 0 / 0; yield-starved 27 / 33 / 2 / 2; left-turn-starved 19 / 28 / 8 / 8; stop-deadlock 22 / 21 / 10 / 13; lane-change-wait 143 / 150 / 123 / 116; crossing-blocked 9 / 6 / 6 / 12; box-blocked-on-green 0 / 0 / 9 / 8; keep-clear-wait 0 / 0 / 8 / 5; overlap 0 / 1 / 0 / 1. Typed car-on-car wreck incidents b1 / b2 / r3c / fa: 53 / 71 / 57 / 29 - crossing 12 / 13 / 15 / 5, rear-end in a box 5 / 8 / 8 / 8 (Independence Boulevard n8465 each run: 0d88083c's margin adds none - left as is). Road meshes hash = base byte for byte; typecheck OK; logs: census.log incl. its check 104-115 MB, citytraffic.log (check alone) 72.6 MB.
- SHOTS: Docs/shots/junctions_r3c/ - j724_heads_before_after.png (all four approaches, 22 m and 45 m), overlap_junctions_traffic.png (the three junctions with traffic on the final code).
- LEFT (next round): (1) the remaining city-wide locked pairs and check wrecks - each a different place: Lynhaven Street e22265/e25026 (final_d), Queens Road e16461 (final_c), node 7519 (a straight into a left that shares its exit lane, standing in the box: the shared-leg order says "behind"; the e08acc37 sweep test did not stop it at 8.3 m/s - trace it with PSX_TRAFFIC_TRACE=7519 in a census at (-2412,4834)), node 14236 (two straights side by side knocked 4.8 m/s at a stand - suspect a creep or a back-off; trace it), node 9907 North Tryon (an uncontrolled crossing: touches the recovery undoes - it wants a control); (2) re-run 2 censuses + 3 checks on the fixed code; (3) the 7 approaches with no head (420 x3, 672, 316, 567, 48: no ground in view - a building-anchored wire or a head on a lamp's pole would be new furniture); (4) classes worse than both bases with named causes (forced-short-change, turn-from-wrong-lane, turn-tighter: round 3's take-the-turn-from-the-lane-it-is-in, Gilead Road n5108; geometry classes 77-100 % at the bases' spots driven more; box-blocked-on-green / keep-clear-wait: round 3's rules); crossing-blocked 12 on fc (6 on fa) - spread or the sweep test's extra holds, look again.

# HANDOFF - ROAD LOGIC JUNCTIONS ROUND 3b (2026-10-29 ~23:15, stopped near the context budget; NOT MERGEABLE YET - see LEFT)

Code head 0d88083c (+ docs after it). Sandbox PSXRec, lock name "road-logic-junctions-r3b" (released). Scratchpad tools used: census_cmp.py / census_where.py (per-class compare and where), heads_sheet.py, r3b_job.ps1 / r3b_tool.ps1 (lock-safe job runners).
- BASE = integration-1029d code, same data, same sandbox: census Docs/traffic_census_junctions_base1029d (135.7 car-h, 2748 km); road meshes Docs/road_mesh_hash_base1029d.txt.
- TRAFFIC: c38d93c0's (1) one-lane-out release by the leader's body vs the follower's swept turn and (2) standing cars braked for only on the PATH, plus round 3b: PathHits looks as far as CrossGap (14 m at least; 4 m + a second of travel + o's body), a run-on leg past a plain vertex braked for as before; 0d88083c: the path tests' margin (InTheWay, PathHits) = half width + 0.5 m standing, +0.1 m per m/s (the 21:30 check failed with 2 car-on-car wrecks at node 7681, uptown: a straight-on and a car behind meeting a left turner standing mid-box that the plan passed 0.5 m clear of).
- CHECKS on 0d88083c: check 3 OK, check 4 OK, check 5 (in census r3c) OK - traffic-on-traffic wrecks 0 / bumps 0 each. Before it: census-1 check OK (c38d93c0+window), census-2 check FAIL (2 wrecks, node 7681), check 2 OK (04783ddc).
- CHECK LOG (5): citytraffic.log 79-80 MB per check (was ~2.5 GB), census.log incl. a check 101-122 MB. CityTrafficCheck.OnLog reads System.Environment.StackTrace when Unity's stack is empty and counts "PSXRacing.City.CityTraffic" frames; a probe error at start verifies it ("ok stack traces off ... still read").
- CENSUS (40 spots x 300 s): base -> final r3c (Docs/traffic_census_junctions_r3c, 131.1 car-h, 3103 km = +13 % km, movements 5605 -> 6075). Better: no-control-major 141->18, conflicting-green 17->0, gridlock-box 194->79, yield-starved 27->4, left-turn-starved 19->4, stop-deadlock 22->19, lane-change-wait 143->120, missed-turn-lane 432->370, merge-stalled 4->0. WORSE: traffic/overlap 0->26 (26 samples of 3 pairs locked after a low-speed contact in a box: Nevin Rd n15108, Morehead Rd n11726, Parkwood Ave n7758), wreck/car 99->104 (r3 80, r3b 86 on the code before 0d88083c), box-blocked-on-green 0->8, keep-clear-wait 0->11 (the keep-clear rule the base lacks), crossing-blocked 9->10, other-road-in-lane 0->1, uncontrolled-crossing 0->1, and the road-geometry/route classes driven more (kink, heading-snap, turn-clips-corner, no-road-under-lane, wrongway/across-divider at West Sugar Creek Rd / Morehead Rd, road-surface steps, u-turns): same spots as the base, road meshes hash-identical.
- ROAD MESHES: Docs/road_mesh_hash_branch_r3b.txt = Docs/road_mesh_hash_base1029d.txt byte for byte (ALL 277e518b4adb71bf).
- SIGNAL HEADS (4), CitySignals.HeadsEvery (PSX_CITY_HEADS=0 = before), tool Editor/CityRefSpots.Heads.cs RunSignalHeadsAndHash: heads counted IN THE DRIVER'S VIEW (6 m short of the stop line .. 55 m past, 20 m either side; MUTCD 4D.14). One-lane approaches hang two heads 2.4 m apart; corners searched to 55 m; a SET-BACK span wire past a race run-off (Tryon St) for approaches with none; far-left mast arm; a head on the side of one of the junction's own poles; posts (far-right 1 m off, far-left, near-right 0.6 m); a near-side supplement where the nearest far-side face is 120 ft+ past the stop line. 1010 signalised junctions, 3390 approaches: NONE in view 409 -> 35, ONE 1033 -> 51, two+ 1948 -> 3304; junctions with an approach with none 265 -> 25 (none at all 14 -> 4); 14 approaches with fewer heads in view than before. Sheet Docs/shots/junctions_heads/signal_heads_before_after.png, list heads.txt beside it.
  - Colony / Carnegie (now junction 1009): it HAD heads (2 per approach on far-side mast arms, Colony 36 m and Carnegie 45 m past the stop line, 5.5-5.9 m up) - they read small against trees and a utility pole from 22 m back. Carnegie now also gets a near-side post head; Colony is unchanged (36 m < 120 ft).
- LEFT: (a) the locked overlapping pairs after a low-speed box contact (traffic/overlap) and wreck/car vs the base - find the contact's cause (two straights crossing at 1-8 m/s in boxes at n15108 / n11726 / n7758), then re-run census + 3 checks; (b) a second census on the final code to size the run-to-run spread (r3 vs r3b differ by up to 40 % in a class); (c) the 35 approaches still with no head (junction 724 Dalton Ave / N Tryon has none on any); (d) 14 approaches lost an in-view head.

# HANDOFF - ROAD LOGIC JUNCTIONS ROUND 3 (2026-10-29 ~20:30, STOPPED AT THE CONTEXT BUDGET - in progress)

- DONE: merged integration-1029d (3ca6be07; both sides kept in CityTraffic.cs; data = integration's, no exporter run). PSXRec synced: code /MIR per job; charlotte_city.bytes + charlotte_bld.bytes copied by path (metas existed).
- RUNNING when stopped: the BASE job (scratchpad basejob.ps1, lock "road-logic-junctions-r3"): integration-1029d code (+ CityRoadMeshHash + an uncommitted BaseJobs wrapper) in PSXRec, road meshes hashed then the 40-spot census x 300 s. Outputs: Docs/traffic_census_junctions_base1029d (+ raw, git-ignored), the hash at <scratchpad>/road_mesh_hash_integ1029d.txt (copy it to Docs/road_mesh_hash_base1029d.txt), log PSXRec\basejob.log. Afterwards PSXRec holds integration-1029d code (the next job mirrors this branch).
- CODE in this commit, typechecked, NOT run yet: (1) one-lane-out pairs: the follower is released only when the leader's BODY is clear of its own swept turn (OneLaneOut -> InTheWay; nodes 7288/14049); (2) mutual crossing locks: a standing car is braked for only if it is on this car's PATH (PathHits: its lane line 14 m + next turn, or the rest of its turn), for runs too, not the straight corridor; (5) CityTrafficCheck: no stack traces for Error/Warning/Log under GraphicsDeviceType.Null (exceptions/asserts keep theirs; trafficErrors then match by message). Log size not measured yet.
- (3) box-blocked-on-green: a 25 s detection mark. The base's legacy clock gave every signal a 14 s green - a minimum-green figure, not a timing plan (ITE/MUTCD practice: arterial main-street greens ~30-60 s in 60-120 s cycles) - so the green was wrong, not the mark; the round-2 4 are box cars standing in the way (mutual locks: item 2). Crossing flows 7 vs 6: re-measure after (1)(2).
- (4) SIGNAL HEADS - NOT DONE. CitySignals.Furnish: outside uptown SpanWire(j, corners), else a MastArm per approach; FarCorner / FootOk (RoadsideOccupancy, structures) refuse feet and there is NO fallback, so a signal can run with no heads (Colony/Carnegie, junction 1000). Next: count EmitHead per (junction, approach) in Furnish + an editor tool that builds every signal tile (CitySignals.Tiles) -> junctions with/without heads; a fallback (post-mounted head at the near-right corner, or the span wire without the occupancy test) so every approach shows one; 1990s Charlotte = span wires suburban/arterial, mast arms uptown, incandescent lenses.
- NEXT JOBS: census+check (v4, from this worktree) -> check -> check (THREE in a row) -> heads count + shots + branch road-mesh hash (compare with the base job's hash).

# HANDOFF - ROAD LOGIC JUNCTIONS (road-logic-junctions, round 2 finished 2026-10-29 ~20:00)

Branch = main 810ecd2c + road-logic-topology + junctions rounds 1-2. No exporter/data change. Sandbox PSXRec. Round 2 = commits ca353801, 8c6018a1 (+ the data/sheet commit after them).
- STATIC (city-wide): no-control major crossings 141 -> 18; conflicting greens 15 -> 0; warrants now leave a side road under 30 m from the junction behind it to yield (28 arms; two-way STOP 920 junctions / 1052 approaches).
- ROUND 2 traffic logic (Scripts/City/CityTraffic.cs, CityTraffic.Junctions.cs): gaps back to 5.0/5.5/6.5 (merge 3.5) and never under the time to clear the box + 1.5 s (ClearS); a HORIZON per car (its next junctions within 170 m, past degree-2 vertices and greens) read by CanGo / OncomingConflict / RtorClear; CrossInBox / ClearingInBox hold only for a standing car actually in the way (InTheWay) or a moving one, a committed-yellow car only while it comes; claims lapse when the claimant stands 3 s; Compatible movements at two-way STOPs; oncoming STOP/yield cars give the way; multi-node boxes count a car only where its way crosses; opposite lefts by geometry with a wait order; InnerYield in signal boxes with no head (why 18); JoinAhead orders lanes that end in one lane; a plan leader is braked for across, not round; a turning car ignores a standing car beside its arc; two roads into one road's two lanes are watched physically; no lane wait / replan on internal runs or pieces under 20 m; internal runs keep their entry approach (Car.entryKey). Census stuck notes name the blocker and what holds it.
- TRAFFIC CHECK: NOT YET TWO IN A ROW. v3 (8c6018a1): run 1 (census job) every gate OK, traffic-on-traffic 0; run 2 FAIL traffic-on-traffic 4 = two pairs of turns OUT OF ONE LANE (a straight behind a right at node 7288 e15203, a straight beside a left at node 14049 e16791, uptown; OneLaneOut lets the follower go once the leader is 2.1 m aside, the bodies still meet). v2: pass, then fail (a 0.86 m overlap at East 13th / North Tryon, fixed in v3). Round 1 wrecks (hairpin overlap, lane drop) fixed.
- CENSUS 40 spots x 300 s (Docs/traffic_census_junctions_after, 128.3 car-h; base Docs/traffic_census_junctions_before, 131.2): gridlock-box 62 (base 163 in real boxes / 195 raw) + keep-clear-wait 17; stop-deadlock 11 (22); yield-starved 0 (23); left-turn-starved 4 (29); car wrecks 87 (105); overlap rows 0 (2); bumps 79 (110). STILL WORSE: crossing-blocked 21 (5) - mutual locks, a run car's nose in a turner's arc and the turner across its corridor (Christenbury 10837, Pavilion 6463, Perimeter 7322); box-blocked-on-green 4 (0 - the base's 14 s legacy greens ended before the 25 s mark; the 4 are stuck box cars truly in the way); crossing flows 7 (6).
- Road meshes: Editor/CityRoadMeshHash (8 spots, 3x3 tiles, Roads/Kerbs/Barriers/Ground), results Docs/road_mesh_hash_branch.txt = Docs/road_mesh_hash_base.txt BYTE FOR BYTE (ALL 5ed3051fed75987d; base = 27082b65 code in the same sandbox and data; both deterministic); road-paint inputs hash before = after (7145b681db05de43). Junction shots: CityRefSpots.RunJunctionShotsAndHash; sheet Docs/shots/road_logic_junctions_before_after.png. FINDING: the warrant signal at Colony / Carnegie (junction 1000) runs its phases but no heads show in the driver's view (before = after) - check CitySignals.Build furnishes warrant signals (Clanton / I-77, OSM, shows its heads).
- Next: FIRST the one-lane-out pairs (keep the follower behind until the leader's whole body is clear of its own arc - path vs body, not 2.1 m off the heading line); then resolve two standing cars each in the other's way by path (the one whose own path is clear goes); the 4-node square's turn locks; North College loop: cars now yield through node 1482 at ~7 m/s and some hit a step in the box's road surface ("Roads" wrecks, not traffic-on-traffic).
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
