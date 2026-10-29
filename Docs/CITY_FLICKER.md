# Charlotte flicker - the finder, its findings, what was fixed (city-popin, 2026-10-28)

Owner, 2026-10-28 (NFS 2015 frames as the bar): "textures flicker as they fight for layering"; 2026-10-01: "I regularly drive over texture pop in from overlap".

## The finder

`tools\city-flicker.ps1` (Assets/PSXRacing/Editor/CityFlickerFinder.cs), run in a sandbox under **OpenGL core** (`-force-glcore`: the game is WebGL - 24-bit depth, no reversed Z; D3D11's float depth hides most ties). 12 spots on the race routes (Tryon Sprint's first kilometre from its start in South End: tryon0..3; Uptown Loop: uptown0..3; Independence: indep0..1; I-277 900 m either side of uptown: i277s, i277n), each seen from the road 0 m, 150 m and 400 m before it, with the chase near plane (0.25 m) and the cockpit's (0.12 m). Every draw is rendered in a flat ID (PSX/Lit `_PSXIdOn`; other shaders wear Hidden/PSX/FlickerId with their material's depth state) twice with ONLY the near plane moved (x1.01): x and y are identical to the bit, so every pixel whose ID changes is a depth tie - two surfaces fighting - and the two IDs name them. A 2 cm nudge is rendered too (its count includes moving edges, so it is not ranked). `-Both` measures every view with the far buildings hidden and shown. Diagnostics: each big pair's first tie pixel ray-cast into the world (tile, renderer, road edges, footprints); a view with 300+ tie px is saved with the ties in magenta (Screenshots/flick_*.png).

## Totals (depth-tie pixels, 72 views of 960x540, ~26 M drawn pixels)

| run | far buildings off | far buildings on |
|---|---|---|
| BEFORE (j4: main's geometry; back views stood the eye at the spot's height) | 9889 | 9866 |
| after the parts fix (j5, eye on the road) | 2029 | 1717 |
| **AFTER** (j6: + barrier depth offset) | 1777 | **1465** |

-85% overall. The far buildings themselves add no fight (on is 312 px BELOW off: past the squeeze start they draw in their own depth, where the tiles' own copies used to tie near the far plane).

## BEFORE - ranked (j4, far buildings on)

| # | A | B | px | mean dist m | worst views |
|---|---|---|---|---|---|
| 1 | Buildings [CityFacadeGlass] | Buildings [CityFacadeGlass] | 8170 | 329 | i277s@150m/cockpit 1972, tryon2@0m/cockpit 1869, tryon2@0m/chase 1858 |
| 2 | Barriers [CityConcrete] | Roads [CityRoad_mw3_asphalt_old] | 293 | 37 | uptown0@150m/cockpit 38, uptown0@0m/cockpit 32, uptown2@150m/cockpit 28 |
| 3 | Barriers [CityConcrete] | Roads [CityConcrete] | 192 | 106 | i277s@0m/chase 70, i277s@0m/cockpit 38, indep0@0m/chase 25 |
| 4 | Buildings [CityFacadeGlass] | Ground [CityPavement] | 161 | 77 | tryon1@150m/cockpit 15, tryon0@400m/chase 13, tryon1@0m/cockpit 11 |
| 5 | Barriers [CityConcrete] | Roads [CityRoad_mw4_concrete_old] | 100 | 46 | uptown2@0m/cockpit 46, uptown2@150m/cockpit 23, uptown2@0m/chase 22 |
| 6 | Roads [CityRoad_mw3_concrete_old] | Roads [CityRoad_mw4_concrete_old] | 89 | 8 | uptown1@0m/chase 44, uptown1@0m/cockpit 44, uptown2@150m/chase 1 |
| 7 | Ground [CityPavement] | Kerbs [CityConcrete] | 83 | 65 | tryon0@400m/chase 14, i277s@0m/cockpit 12, tryon0@150m/cockpit 10 |
| 8 | Barriers [CityConcrete] | Ground [CityPavement] | 74 | 60 | uptown2@150m/cockpit 37, uptown2@150m/chase 8, tryon3@400m/cockpit 6 |
| 9 | Roads [CityRoad_junction_asphalt_old] | Roads [CityRoad_mw3_asphalt_old] | 60 | 51 | i277s@400m/cockpit 45, i277s@400m/chase 13, uptown0@150m/cockpit 2 |
| 10 | Barriers [CityConcrete] | Roads [CityRoad_ramp1_asphalt_new] | 55 | 18 | uptown2@150m/chase 20, uptown0@150m/cockpit 18, uptown2@150m/cockpit 10 |
| 11 | Ground [CityGround] | Roads [CityConcrete] | 53 | 49 | uptown3@150m/cockpit 18, uptown3@0m/cockpit 11, uptown1@400m/cockpit 7 |
| 12 | Ground [CityPavement] | Roads [CityRoad_junction_asphalt_new] | 42 | 24 | uptown2@400m/chase 17, uptown2@400m/cockpit 16, tryon0@400m/cockpit 5 |

Row 1 (8170 px, 83%): ONE building - footprint 26043, a 102 m tower at (-3075, 4390) by I-277 and West Morehead Street, on the tile line between Tile_-13_17 and Tile_-12_17 - whose measured tiers (parts 32061 h83, 32062 h96, 32066 h89, all min_height 0) each rose from the ground inside it: every flush wall was the same face twice, and it flickered from 170 m to 450 m out (tryon2, uptown2, i277s, tryon3).

## Fixed at the cause

1. **A part stands on what of its own building holds it** (CityMeshes.StandOnH, EmitFootprint): its walls start at the roof of the tallest drawn footprint of its building whose plan holds it (its outline, a lower tier; 0.3 m slack), stacked; a part wholly under that roof is inside it and draws nothing twice. Tiles, far buildings and the skyline alike (the skyline counts only its own elements). Picture unchanged; the doubled faces are gone.
2. **Median barriers win their foot line** (PSX/Lit `Offset [_ZOffF],[_ZOffU]`, 0 everywhere; CityWorld.BarrierMat: the barriers' concrete copy at -1,-1).
3. **The far plane band**: the far buildings (CityProxies) draw the city past the squeeze start in the skyline's own depth; the tiles' copies step out there (PSX/Lit CITY POP-IN, one dither for both sides) - no tie between the two copies.

## AFTER - ranked (j6, far buildings on)

| # | A | B | px | mean dist m | worst views |
|---|---|---|---|---|---|
| 1 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_mw3_asphalt_old] | 338 | 24 | i277n@0m/cockpit 43, uptown2@150m/cockpit 37, i277s@0m/cockpit 27 |
| 2 | Buildings [CityFacadeGlass] | Ground [CityPavement] | 184 | 53 | tryon1@400m/cockpit 38, uptown2@400m/cockpit 16, i277s@150m/cockpit 11 |
| 3 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_mw4_concrete_old] | 95 | 31 | uptown2@0m/cockpit 44, uptown2@0m/chase 19, uptown2@150m/cockpit 13 |
| 4 | Roads [CityRoad_mw3_concrete_old] | Roads [CityRoad_mw4_concrete_old] | 88 | 8 | uptown1@0m/chase 44, uptown1@0m/cockpit 44 |
| 5 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_mw4_asphalt_old] | 86 | 24 | i277n@400m/chase 21, i277n@400m/cockpit 21, uptown0@400m/cockpit 13 |
| 6 | Ground [CityPavement] | Kerbs [CityConcrete] | 61 | 76 | i277s@150m/cockpit 12, uptown2@400m/cockpit 7, i277s@400m/cockpit 6 |
| 7 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_mw4_asphalt_new] | 49 | 21 | uptown3@0m/cockpit 13, uptown3@0m/chase 11, uptown3@400m/cockpit 10 |
| 8 | Barriers [CityConcrete (barrier)] | Ground [CityPavement] | 43 | 107 | i277s@0m/cockpit 17, i277s@0m/chase 10, uptown2@150m/cockpit 4 |
| 9 | Barriers [CityConcrete (barrier)] | GuardrailDrawn [CityFurniture] | 40 | 37 | uptown1@150m/chase 17, uptown1@150m/cockpit 14, uptown0@150m/chase 3 |
| 10 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_mw3_concrete_old] | 35 | 58 | uptown0@0m/cockpit 9, uptown1@0m/cockpit 9, uptown0@0m/chase 4 |
| 11 | JunctionFurniture [CitySignals] | SignalLenses [CitySignalLamps] | 34 | 181 | i277n@0m/cockpit 5, uptown0@150m/cockpit 4, uptown1@0m/cockpit 4 |
| 12 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_mw2_asphalt_old] | 31 | 8 | uptown1@400m/chase 16, uptown1@400m/cockpit 15 |
| 13 | Barriers [CityConcrete (barrier)] | Roads [CityConcrete] | 27 | 172 | i277s@400m/cockpit 7, i277s@150m/cockpit 6, i277s@150m/chase 5 |
| 14 | Ground [CityGround] | Ground [CityGround] | 24 | 28 | tryon3@400m/cockpit 18, tryon3@400m/chase 3, i277n@150m/cockpit 3 |
| 15 | Barriers [CityConcrete (barrier)] | Ground [CityGround] | 23 | 78 | indep1@150m/cockpit 5, uptown3@0m/cockpit 2, uptown3@150m/cockpit 2 |
| 16 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_ramp1_asphalt_new] | 23 | 22 | uptown0@150m/cockpit 20, uptown0@150m/chase 3 |
| 17 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_mw5_concrete_old] | 20 | 45 | uptown0@400m/cockpit 13, uptown0@400m/chase 3, i277n@400m/chase 2 |
| 18 | Ground [CityGround] | Roads [CityConcrete] | 16 | 93 | uptown3@0m/cockpit 10, uptown3@0m/chase 5, indep0@400m/cockpit 1 |
| 19 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_mw2_concrete_old] | 16 | 16 | uptown1@400m/cockpit 13, uptown1@400m/chase 3 |
| 20 | Kerbs [CityConcrete] | Roads [CityRoad_mw3_asphalt_old] | 15 | 58 | i277s@400m/cockpit 6, i277s@0m/chase 4, i277s@400m/chase 3 |
| 21 | Ground [CityGround] | Kerbs [CityConcrete] | 15 | 82 | indep0@150m/cockpit 4, uptown3@0m/chase 2, uptown3@0m/cockpit 2 |
| 22 | (sky) | Trees [CityTrees] | 13 | 633 | uptown0@400m/cockpit 5, i277s@150m/cockpit 5, indep0@0m/chase 2 |
| 23 | Buildings [CityFacadeGlass] | Buildings [CityFacadeGlass] | 13 | 192 | uptown2@400m/chase 7, uptown2@400m/cockpit 6 |
| 24 | Barriers [CityConcrete (barrier)] | Roads [CityRoad_mw4_concrete_new] | 13 | 15 | uptown3@150m/cockpit 9, uptown3@150m/chase 4 |

## Left

**For city-streets (sidewalk / curb / verge / lot surfaces - listed, not touched):** Ground[CityPavement] vs Kerbs (61 px, i277s@150m, uptown2@400m); Kerbs vs Roads mw3 (15, i277s); Ground[CityGround] vs Kerbs (15, indep0, uptown3); Buildings[FacadeGlass] vs Ground[CityPavement] (184, tryon1@400m, uptown2@400m: building feet on the pavement); Barriers vs Ground[CityPavement] (43, i277s@0m).

**Roads (mine, not fixed this pass):** Roads mw3_concrete vs mw4_concrete at (-1549, 107.8, 3700) - I-277 where edge 2344 (prof17, 18.8 m) meets edge 1393 (prof16, 15.2 m) beside link 7338 and bridge 14177: two carriageway ribbons overlap at the lane-count change, 44 px at 8 m from the eye (uptown1). Barriers vs road surfaces remain the largest class (~700 px, 8-60 m): the foot line at grazing angles; the -1,-1 offset took ~250 px off it, a larger one or ending the barrier faces exactly at the deck is next.

**Small, unassigned:** JunctionFurniture vs SignalLenses (34 px at 180 m: the lens quads on the head faces) - a lens depth offset; Ground vs Ground (24, tryon3@400m).

## West Trade Street - twin footprints (west-trade-flicker, 2026-10-29)

Owner's video (live WebGL, free roam westbound on West Trade Street at ~100 mph, chase camera): "not the orange brick building, the tannish cream one before that. flickers probably 30 times while driving by".

**The spot:** West Trade Street westbound, edge 5780 (-2933, 5390) -> (-3036, 5483) and edge 5781 on to (-3157, 5588), the long stone building on the right between South Cedar Street and the next block (finder spots `wtrade0..2`).

**Cause - one building, two footprints.** charlotte_bld.bytes held footprint 13904 (h 24.8) and 31553 (h 28.2) on ONE outline, and 13903/31552 (28.2/28.2) on the next: OSM's building way and the multipolygon relation whose outer ring is that way, and the exporter took both (addPoly per way, then per relation outer). Every wall was the same face twice. They tie in depth; since the building pass (2026-10-28) each record wears the floors of its own height - 7 floors of 3.54 m against 7 of 4.03 m - so the tie shows as window rows that jump at every wall and change from frame to frame. Both records are in one tile's Buildings mesh: ONE draw, one ID whichever wins, so the ID finder saw only 53 px of it (Buildings vs Buildings, wtrade1@150m).

**Fix - twin footprints flagged hidden** (`tools/city/lib/twins.mjs`; `export_osm.mjs` runs it on its records before writing, `PSX_CITY_TWINS=0` skips it; `tools/city/bld_twins.mjs --write` flagged the shipped file: 41 flag bytes, same size, same indices). A footprint is a twin when one of its walls lies on a same-facing wall of another non-part footprint (5 cm, 0.5 m long, 0.5 m of shared height) and that other one holds all of it: the SAME outline (the lower top is the twin; equal tops, the later record), or wholly INSIDE one whose top is no lower (0.3 m slack). The keeper stands as it was - the silhouette and look are the keeper's. A hidden outline draws nothing in the tiles, the far buildings or the skyline; CitySkyline now also skips a hidden outline with no parts when it picks its towers.

City-wide: 64 pairs of non-part footprints shared a face (131,245 m2 of doubled wall); 41 twins flagged (28 one outline twice, 87,972 m2; 13 inside another, 30,257 m2). Two of the keepers are listed parking decks (20058, 36000): the solid box that stood on or inside each deck's outline is gone and the deck draws as the deck. One twin is itself a listed deck (15628, inside 31542): EmitDeck runs before the hidden test, so its deck still draws in the tiles exactly as before.

**Left drawn (their shared walls need cutting, not removing):** 12 pairs - 5 where the inner building RISES above the outer (the 138.6 m tower 13866 inside its 31 m podium 32 at (-2741, 4419), uptown; 31542/31543 at (-1383, 3402); 20058/31528; 61969/62022; 62260/63494) and 7 that only overlap (18479/29597, 27949/30646, 30270/31709, 36692/36693, 55679/57882, 57881/57882). `node tools/city/bld_twins.mjs` lists them.

**The finder now counts COLOUR FLIPS** too: the shaded picture at the same two near planes (post off); a pixel whose colour changes is a visible depth tie, one draw or two. Ranked by the draw on top, written into the per-view table and the magenta pictures. The default run now has 15 spots (wtrade0..2 added).

**Measured** (`tools\city-flicker.ps1 -Spots wtrade0,wtrade1,wtrade2`, OpenGL core, 18 views; the after run with `-BldFrom` the flagged file):

| | ID ties px | COLOUR FLIPS px | of which Buildings [CityFacadeGlass] | worst view (wtrade2@150m cockpit / chase) |
|---|---|---|---|---|
| BEFORE (main's data) | 675 | **69,930** | 69,218 | 39,937 / 21,271 |
| AFTER (twins flagged) | 667 | **1,211** | 507 | 79 / 26 |

-98% of what a player sees fight at the spot; no view keeps 300 flipped pixels (the magenta pictures are only written over 300). What is left is the ID finder's old list (facade feet on the pavement, signal lenses, kerbs) and a few dozen pixels on other buildings.

**The see-through poles in the same video are the SPEED BLUR, not the tile fade.** The posts the owner drives past are the street lamps (CityPoles.EmitLamp: a black post with a box head). Their fade share (b, ModeTreeFrame) ramps 0 to 1 once over 0.5 s when the tile's tree frame is planted (PlantTrees runs once per tile) and is 1 for an unbuilt tile, so it cannot stay part-way; and in the video the posts near the tunnel's centre stay solid in the same frames where the near ones ghost, and one post goes from solid to ghost as it moves out of the centre within half a second - one tile, one share. The finder's `-ShotBlurMph 100` frame (edit mode: every share whole) shows it: through the game's ZOOM blur at 100 mph the nearest lamp post at wtrade1 40 m back is gone entirely and the far ones stay solid (Screenshots/popin_wtrade1_40m_proxies_*_blur100mph.png). A 16-tap gather along the radial line averages a 2-4 px post with 30-100 px of facade, so it reads as glass. That is the owner's blur table (100 mph: substantial), not a fault; changing it is his call.
