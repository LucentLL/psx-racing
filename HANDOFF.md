# HANDOFF - NIGHT PAINT (branch `night-paint` off b80d004b, worktree `C:\Users\mcgee\PSX Racing-paint`, sandbox PSXCity, 2026-10-26) - committed, not merged/pushed

Owner (Linn Cove, rain, bumper cam, purple rival in his low beams): "headlights ... do not reflect in the paint, but instead make
objects appear matte and flat". CAUSE (measured off his own frame): the beige panel is linear (0.78, 0.56, 0.27) = Halogen.linear
x 2 lamps x HEAD_GLINT 0.25 x GlintIntensity 2 = (1.0, 0.72, 0.34) through the paint shoulder - the beams' GLINT, not their light.
Blinn-Phong pow 120 is a ~14 deg lobe; a rival 11 m ahead is ~10 deg wide, and with the lamps beside the eye (chase/bumper sit on
the lamp line) every texel of its flat rear sat "at the mirror angle" -> one halogen tone, on a panel the low-beam cutoff leaves
unlit. Reproduced (BlueRidge night chase): glint > half the pixel on 33% of the rival's paint, paint mean sRGB (98,71,53).
Beam cone (PSX/Beam) over the rival's paint: +0.0005 linear (0.4%) - not a veil, left unchanged.

FIX (PSXCarPaint + PSXHeadlights/PSXLamps.cginc; PSX/Lit and every other includer keep the old Blinn path, literal specOn 1):
* Lamp glints -> each LENS'S MIRROR IMAGE (PSXLampImage, specOn 2): GGX-shaped lobe of radius sqrt((lens/dist)^2 + rough^2),
  energy-kept, x the coat's own k; roughness by finish IMG_ROUGH_* (clean 0.03, dull 0.12, wet 0.022, glass 0.018 rad);
  HEAD_IMAGE 700, LAMP_IMAGE 30; hot cores go toward white (IMG_WHITE). Old HEAD_/LAMP_GLINT defines removed.
* IMG_CROWN 0.35: the lamps' mirror ray only uses an ellipsoid-crowned normal (CROWN_C/H = a typical shell's box), because the
  pack's flat panels either miss a sharp image or get washed by a wide one. Result: a compact hot spot where the curve faces the lamp.
* Beams shade the body by true N.L (facingOn 2) - the road's saturating `facing` gave every panel the whole plateau.
* CLEAR coat at dusk/night at CLEAR_NIGHT 0.6 (was day-only): clean/wet cars mirror the lit street and lamps; a quarter of paint tint kept.
* Rain beads glint with the lamps' light (DROP_LAMP 0.35). Debug channel 13 = the glints.

AFTER (BlueRidge night chase): glint > half the pixel on 11% of the rival's paint (the spot), paint mean sRGB (52,16,17) =
purple under halogen; glint > 0.1 on 118 of 5814 car px, core max 2.2 linear (whitened, shouldered).
DAY: Charlotte noon chase before vs after (run "after1", same tool state): 0 of 1,440,000 px differ (the day path is unchanged by
construction: CLEAR_NIGHT multiplies out at _PSXPaintWorld.x = 1, lamp tables are empty). NOTE: run "after2" showed 2004 px (0.14%,
max 17 codes) of speckle in the GLASS reflections only, after the tool had moved the cars for the lamp shots earlier in the run
(different city tiles in the panorama, most likely) - not re-checked (job budget).
GLES3: PSX/CarPaint + PSX/Lit compile for WebGL 2 (VolCloudsCheck, RESULT PASS). PSX/Beam unchanged.

SHEETS (not committed): Docs/shots/nightpaint/nightpaint_blueridge_rival.png (owner frame + before/after chase x night/night rain/
dusk rain, after bumper, gloss 1/0.3), nightpaint_charlotte.png (rival night chase/bumper; black car under a street lamp dry/rain),
nightpaint_day_noon.png. Raw: C:\Users\mcgee\PSXCity\Screenshots\np_{before,after1,after2,after}_d3d_*.
TOOL: ReflectionMapCheck part "nightpaint" (PSX_NP_VENUES/RIVAL/PLAYER/GAP/VIEWS); raw .f32 d00/03/08/11/12/13 + _nocone.

OPEN: (1) gloss 0.3 vs 1 barely differ in these frames - the headlamp image lands on the dark plate recess, which the paint
classes as glass (glass sharpness ignores gloss). (2) Dusk rain: the rival's flat rear mirrors the wet road in a near-uniform
grey-lilac (the rain film, F0 0.12) - lower CLEAR_NIGHT / the film's low-hemisphere share if the owner dislikes it. (3) "Before"
bumper frames of the first baseline were the chase pose (SteadyPose is chase-only; tool fixed) - the before-bumper is the owner's
frame. (4) Charlotte lamp before/after differ in setup (white scene car, lamp 13 m off vs black Supra moved near a lamp).
(5) Not tried on phones (needs a WebGL build); the cost is one crowned normal + the same table walks as before.

---

# HANDOFF - PAINT SHINE (branch `paint-shine`, 2026-10-26) - commits 13d06002 + 67043205, not merged/pushed

Owner: a new clean R34 on W Trade St at a clear noon showed no paint reflection (glass only; roof in rain).
Cause (measured, raw channels): clean coat k 0.08-0.11 on the boot / ~0.2 roof from the chase eye, reflection tinted 45% by the
paint, sky dimmed by day - a blue car added a few % of blue sky to blue paint. The map itself was fine (refl-tour PASS).

Fix = PSXCarPaint.shader "THE CLEAR COAT BY DAY" (CLEAR_* defines), by day only, x max(gloss, wet); dusk/night/dull bit-identical:
F0/FMAX 0.08/0.60 -> 0.12/0.95; (1-N.V)^2 rise where the mirrored world is bright (lum 0.10-0.35), Schlick 5 elsewhere;
tint 0.45 -> 0.08; DAY_SKY_EXPOSE dim -> 0; ENV_HDR x0.4; darks env*min((lum/0.35)^0.5,1). Wet film = max(wet, clear).

Numbers (W Trade St noon chase, paint-pixel display gradient before -> after): R34 0.0118 -> 0.0155, Charger 0.0117 -> 0.0138,
white audi_saloon 0.0063 -> 0.0078; gloss 0 identical; 0% paint > 0.90 everywhere (no white wash); rain grad up slightly.
GLES3: PSX/CarPaint compiles (ReflectionMapCheck gles + VolCloudsCheck, which now lists PSX/CarPaint).

Sheets (not committed): Docs/shots/paint/paint_noon_gloss.png, paint_sunset_rain.png, paint_owner_frame.png, paint_flank_34.png.
Tool: PSX_REFLMAP_PARTS=paint (+PSX_PAINT_CARS, PSX_PAINT_OUT) in Editor/ReflectionMapCheck; raw .f32 debug channels 0/5-12
let the mix be rebuilt offline (scratchpad paintsim.py, not in repo).

OPEN: from a rear three-quarter (paint_flank_34.png) a clean Charger's flank beside the brick wall goes dark brown - Schlick
at FMAX 0.95 mirroring the dark wall/road (k ~0.5 at grazing, was ~0.3). If the owner dislikes it: keep SHINE_FMAX (0.60)
where softW is 0 (dark world) and CLEAR_FMAX only where bright. Not verified (job budget spent). Night not re-shot (unchanged by construction).

---

# HANDOFF - REAL HEIGHTS seats, round 3 (2026-10-25 late; NOT shippable yet)

Branch `real-heights` (worktree `C:\Users\mcgee\PSX Racing-heights`), sandbox `C:\Users\mcgee\PSXRec`
(`$env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'`). Typecheck from the worktree needs the sandbox's references:
`tools\typecheck.ps1 -Project C:\Users\mcgee\PSXRec -Source "C:\Users\mcgee\PSX Racing-heights" -OutDir $env:TEMP\psxtc-heights`.
Reports: `C:\Users\mcgee\PSXRec\jm` (main), `j3` (round 2), `j4r3` (THIS COMMIT), `j5r3` (+ a mitre try, taken back),
`j6r3_playcheck_out.txt`, `j7r3_selftest_out.txt`, probes `probe1/2/3.txt` (the surfaces under the launch paths).
Full write-up: Docs/CHARLOTTE.md, REAL HEIGHTS "seats, round 3".

## Gates (`city-vsmooth.ps1 -Launch -Lanes`)
| gate | main bcf6298b | J3 round 2 | J4 = this commit |
|---|---|---|---|
| launch / unload old paths | 60 / 408 | 118 / 595 | 109 / 589 FAIL |
| launch / unload lane carry | 59 / 417 | 119 / 593 | 109 / 585 FAIL |
| routes launch | 0/0/0 | 1/0/0 | 1/0/0 FAIL (uptown n3640 e2422 s1, 0.16 m at 140 km/h) |
| census freeway | 290/131/45/4 | 182/97/24/1 | 243/79/19/3 ok |
| ramp | 2846/691/203/7 | 1820/574/170/3 | 1937/451/121/10 (node joints FAIL) |
| arterial | 1886/929/308/16 | 2125/919/296/13 | 2139/796/236/16 (>0.5% FAIL) |
| collector | 5231/280/98/702 | 5365/293/94/154 | 5321/246/66/153 (>0.5% = 0.9% chords) |
| local | 5480/52/15/138 | 20088/99/16/77 | 20065/81/10/77 (>1% FAIL) |
| route p99 g @150 | 1.60/1.79/0.96 | 1.06/2.11/0.68 | 1.12/2.11/0.75 (Tryon FAIL) |
| play-check | - | PASS | PASS (CITY SPAWNS OK) |
| self-test finish | FAIL on main | - | TryonSprint / IndependenceSprint agree; UptownLoop no finish (62 other FAILs = sandbox packaging: deck-run scenes, engine families, shells, release budget) |

## What changed (all RealOn; switches in brackets)
- Seats only where the ramp is IN its host's pavement (`CityMeshes.SeatIntoOnly`, `PSX_CITY_SEAT_INTO=0` off); a station
  at the zone's end and every <= 5 m inside (`PrepareSeats`); no station-past-the-end. Fixed n3350/n4444/e2910/e4260's
  host-pavement stand-offs (probe2/3). Mesh-off launches 72 -> 57.
- Gore seam strips under both pavements (`EmitBranch`). MeetPavement drops eased along the edge (`EaseMeets`,
  `PSX_CITY_MEET_EASE=0` off).
- Finish index one definition: `TrackCatalog.RouteIndex`; route-with-ends path pitch = Spacing x arcScale.
- Instruments: `CityLaunchAudit.Probe` + `PSX_LAUNCH_PROBE_EDGE="edge:s0:s1[:sec]"` (surfaces under a launch line);
  `CityRealHeights.RunSeatShots` (PSX_PREVIEW_CUSTOM views, old seats _before / new _after; NOT run - job budget).

## Next (in order)
1. Climb-outs (20 vs 4): short connectors between two hosts 1-2 m apart (e4260 Billy Graham, e2910 n685) now climb
   9-13% between the two pavements; the cosine ease between two seats peaks at pi/2 x the mean grade. Use a K-limited
   S (two parabolas) or let the zone end ease the host-side 2-3 m; then SplitTwoHosts' 13% trigger.
2. Mitred joints (35 vs 11): dump a few (`PSX_LAUNCH_PROBE_EDGE`) - n3640 (uptown) is the drawn line itself
   (-7.8 -> -9.2% through a two-arm node), not the mesh. Arms' drawn mean at the mitre moved nothing (taken back).
3. Tryon p99: e16424's sections ARE on the stations; the census reads span EDGE vertices (SideY) + the fan chord, so
   find what moves the edge vertices at s 19.5 (dump `tm.tap` edge verts, not strip columns); run the
   `PSX_CITY_FANMAJOR=0 PSX_CITY_JOIN_STRAIGHT=1 -Routes` A/B.
4. e6798 (inside both hosts 0.85 m apart), ramp node joints 10 vs 7, arterial "no curve here" residuals.
Screenshots: none new this round (the old pairs in Docs/shots/heights/ stand).
