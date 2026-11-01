# PSX Racing — texture inventory (for the overhaul)

Source of truth: the shipped WebGL build report (`PSXBuild/PSXRacing_webgl_report_ALL.txt`, 2026-10-31 build, LIVE main@e44bc14f). **689 textures ship.** Every path below is under `C:\Users\mcgee\PSX Racing\Assets\PSXRacing\` unless it says otherwise.

How a texture was made decides how you replace it:

- **PACK**: a copy of a PSX pack texture. You can swap the file one-for-one.
- **COMPOSED**: an atlas built by a script from pack art. Edit the sources or the script, or repaint the atlas but **keep its cell/column layout**.
- **CODE**: drawn by C# at build time. **These are the weakest and the best overhaul targets.** Replacing one means swapping the painter for a file.
- **NIGHT MASK**: a mask that must stay lined up pixel-for-pixel with its day texture (R = A = glass, G = window id, B = shopfront). If you repaint a facade, its mask has to be regenerated: `py tools/night/window_masks.py` (facades) or `py tools/city/facade_atlas.py` (the atlas).

The Enhanced (HD) tier already exists. It lives in `EnhancedSrc/TexEnhanced/` and downloads as `texenhanced.bundle`. It maps 78 originals. The map is `Editor/TextureTierManifest.txt` → `Resources/TextureTierMap.asset`. New HD art can plug into that same pipeline, so the Classic textures can stay as the low/far tier.

---

## 0. CODEX BRIEF: overhaul Charlotte UPTOWN first

**Goal:** make the uptown frame (East Trade St / Tryon / I-277 loop, day and night) look like the art target. That target is a PS2-era GT3 / NFS Underground 2 city with the gritty colour of NFS 2015, at PSX/PS2 texel density. Work the files in the priority order below. Each tier is a separate delivery, so Claude can integrate and ship one tier before the next one starts.

### Delivery rules (all tiers)
1. **Two sizes per texture.** CLASSIC is the exact current size (in the tables below). ENHANCED is exactly 2× each side. They go into the existing Classic/Enhanced switch (`EnhancedSrc/TexEnhanced`, the same pipeline overhaul1 used).
2. **Same layout, same UVs.** Atlases keep every cell, band and column exactly where they are now. Tiling textures must tile seamlessly on both axes. Never change aspect ratios.
3. **Night-mask registration.** Facades with lit windows have a mask that marks every window pixel. **Repaint inside the existing window grid:** glass where glass is now, wall where wall is now. Change the look of the glass, frames and wall, not where they are. Claude regenerates the masks after integration (`tools/night/window_masks.py`, `tools/city/facade_atlas.py`) and checks registration.
4. **No brands, logos, real trademarks or readable real-world text** (the game is to be sold). Only the sign atlases carry fictional names.
5. **90s city tech:** no LEDs, no modern LED glass. Lit windows are warm yellow or fluorescent white-green.
6. **Format:** sRGB PNG, opaque RGB unless the table says RGBA. No baked strong shadows or sun direction: the game lights everything itself. Keep the average brightness of each texture within ±10% of the current file. Road and deck colours must not shift, because the lane paint and the wet look are tuned to them.
7. **Hand-off folder:** `OneDrive\...\PSX Assets\PSX Racing\Environment_Overhaul2_Uptown\<TierN>\`. Include a `texture_registry_overhaul2.json` in the overhaul1 format: for each file, `classic_asset_path`, source sha256, and the sizes delivered.

### Tier 1: what fills the uptown frame (do these first)
| # | Texture (asset path) | Classic size | HD art today? | What to do |
|---|---|---|---|---|
| 1 | **Facade atlas** `Art/City/Facade/city_facade_atlas.png` | 2048×256 RGB (16 columns × 128×256) | Only a script upscale (4096×512), never repainted | Repaint **columns 0, 1, 2** (silver / blue / teal curtain walls), **5** (granite office) and **6** (ribbon-window office). These are the uptown towers. Keep 8 floors × 8 bays per repeat and the mullion/spandrel positions. Crisper mullions, real spandrel panels, glass with sky/interior variation instead of flat colour. |
| 2 | **Facade atlas column 7, "structure parts"** (same file, x 896–1023) | 128×256 | Upscale only | **Every crown, parapet, cornice, rooftop plant box, lift housing, mast and canopy is textured from this one column.** Stone grain (left half), aluminium panels (x 64–95 of the column), louvres (x 96–127). Neutral grey, linear mean ≈ 0.5, because vertex colour tints it. **The biggest single upgrade for skyline tops.** |
| 3 | **Flat roofs** `Art/City/Pack/concrete_pt_2_city.png` | 256 RGB, tiling | Yes (Core_cityflatroof) but plain concrete | Repaint as a 90s commercial roof: gravel ballast / membrane seams / drains. Every tower top, podium roof and crown slab wears it, and it's seen from the I-277 decks. |
| 4 | **Road surfaces**: the four base surfaces | 64×64 tiles now (`city_road_junction_{asphalt,concrete}_{new,old}.png` are the bare surface) | Junctions + `tw2` only; ~97 profile files have none | **Deliver four seamless base surfaces only:** asphalt new, asphalt old, concrete new, concrete old. 256² Classic / 512² Enhanced, **no lane markings**. Claude will change the road painter to put the lane paint on top for all ~100 road profiles, so one delivery fixes every road. Today the roads are code noise, the weakest surface in your screenshot. |
| 5 | **Sidewalks + curbs** `Art/City/Pack/concrete_pt_4_walk.png` | 256 RGB, tiling | **No** | Broom-finished slab, 1.5 m panels (3 per repeat), expansion joints, light gum/stain grime. |
| 6 | **Pavement / plazas / lots** `Art/City/Pack/concrete_pt_5_city.png` | 256 RGB, tiling | Yes | Refine for uptown plazas (lighter, larger pavers). Optional this tier. |

### Tier 2: street level and the freeway
| # | Texture | Classic size | HD today? | Notes |
|---|---|---|---|---|
| 7 | **Bridge / I-277 structure atlas** `Art/City/Pack/city_structure.png` | 512² RGB, 7 horizontal bands | **No** | Keep the band rows exactly: 0 girder web 0–63, 1 weathering-steel girder 64–127, 2 pier column 128–255, 3 pier cap 256–319, 4 undersides 320–383, 5 abutment 384–447, 6 misc cells 448–511 (OM-3 marker, bearing pad, …). Each band tiles in U. |
| 8 | **Street furniture atlas** `Art/City/Furniture/CityFurniture.png` | 256² RGB, 4 cells of 128 | **No** | Wood pole, galvanised metal, black paint (uptown acorn posts), wire. Tiling inside each cell. |
| 9 | **Signals atlas** `Art/City/Signals/CitySignals.png` | 256², 4 cells | **No** | Metal, black paint, STOP face (octagon must stay in its cell), stop-bar white. |
| 10 | **Storefront row** `Art/City/city_shops.png` (+ `Art/City/Night/city_shops_night.png`) | 1024×256 RGBA | **No**: the old HD version was skipped because it was made from branded art | Fully de-branded street-level shops. Keep the window/door positions (night mask). |
| 11 | **Concrete walls / medians** `Art/City/Pack/concrete_pt_1_city.png` | 256 tiling | Yes | Refine if time allows. |
| 12 | **Tower pack models** `Art/LifeSim/Skyscrapers/textures/building_01/03/04/08.png` | 256² RGBA | Yes (Earlier_Regional / tower03) | These are the white-grid towers in your screenshot. Refine with Tier 1's curtain-wall language so the pack towers and the OSM towers match. |

### Tier 3: night and detail
| # | Texture | Classic size | HD today? | Notes |
|---|---|---|---|---|
| 13 | **Rooms behind lit windows** `Resources/CityRooms.png` | 256² RGBA; **each channel a separate luminance image** (R wallpaper wall, G ceiling tile, B carpet, A wood floor) | No | Deliver the four grayscale source images; Claude packs them (`tools/night/room_atlas.py`). |
| 14 | **Roadside / shop sign faces** `Art/City/Signs/CitySigns.png` (+ `_night`) | 512² RGB | No | Fictional brands only (STACK BURGER, SLICE HOUSE, …). Keep the cell layout. |
| 15 | **Freeway guide signs** `Art/City/Signs/CityGuide.png` | 1024×512 | No | **Low priority, and the letter cells must not move**: `Scripts/City/CityGuideFont.cs` indexes them. Only the green field, the border pieces and the truss-metal cell are safe to repaint. |
| 16 | Suburban siding `Art/City/city_facade_house.png` | 64² (code-drawn) | 128² HD exists | Not uptown, but it is code-drawn: siding with one window. A future tier. |

**Don't touch:** lane-paint layouts, `Art/Track/*` (start grid, joints), night masks (Claude regenerates them), the 101 per-profile road files (replaced by item 4), tree atlases (done in overhaul1).

---

## 1. CHARLOTTE (city) — the highest-priority overhaul

### 1a. Things I modeled in code (buildings, crowns, rooftops, bridges)
None of these have their own texture. They are all drawn from the textures below plus a per-vertex colour (the colour picks the tint and the atlas column). **Improving these few files upgrades every procedural building in the city.**

| What you see | Texture it wears | Path | Kind |
|---|---|---|---|
| **Every OSM building's walls**: towers, mid-rises, podiums, massing steps | Facade atlas, 16 columns × 128×256 (2048×256; Enhanced 4096×512) | `Art/City/Facade/city_facade_atlas.png` | COMPOSED (`tools/city/facade_atlas.py`) |
| ↳ its night windows | | `Art/City/Facade/city_facade_atlas_night.png` | NIGHT MASK (2.7 MB, the largest texture in the game) |
| **Crowns, parapets, cornices, belts, canopies, sign bands, rooftop plant/lift housings, masts** (rooftop kit, hero crowns C1–C4: spired, open-frame, silver, pyramid) | **Facade atlas column 7, "structure parts"**: stone grain (x 0–63), aluminium panels (x 64–95), louvres (x 96–127) | same atlas, column 7 | COMPOSED (sources in `Art/City/Facade/Source/`) |
| **Every flat roof**: tower tops, podium roofs, crown slabs, far proxies | concrete_pt_2 | `Art/City/Pack/concrete_pt_2_city.png` (CityRoofFlat) | PACK |
| Pitched roofs (houses, low buildings) | Buildings pack roof tiles | `Art/City/city_roof_tiles.jpg` | PACK copy of `Roof_tiles.jpg` |
| Stadium bowl (hero C5) | Facade atlas + concrete_pt_2 | as above | |
| **Bridges**: girders, plate girders, piers, pier caps, bearings, abutments, OM-3 markers, high masts | Structure atlas, 512² in 7 horizontal bands | `Art/City/Pack/city_structure.png` | COMPOSED (`tools/city/structure_atlas.py`) |
| Lit rooms behind night windows (interior mapping) | 4 pack interiors packed as luminance: wallpaper, ceiling tile, carpet, wood floor | `Resources/CityRooms.png` | COMPOSED (`tools/night/room_atlas.py`) |
| Far city / skyline lights, beacons, floodlit crowns | no texture: shader + vertex colour | `Scripts/City/CityFar.cs`, `CityBeacons.cs`, `CityMeshes.NightCrowns.cs` | CODE (shader) |

**What the facade atlas columns hold:**

| Col | Look | Source |
|---|---|---|
| 0 | Silver-grid curtain wall | skyscraper_pack building_03/04/08 panes; frames laid out in code |
| 1 | Blue glass curtain wall | same |
| 2 | Teal glass curtain wall | same |
| 3 | Precast stone, punched windows | Buildings `building_10` |
| 4 | Brick, punched windows | Buildings `building_01` |
| 5 | Granite office, punched windows | PSX Textures concrete_2 + pack glass; grid laid out in code |
| 6 | Precast office, ribbon windows | same parts |
| 7 | **Structure parts (crowns, cornices, roof gear)** | concrete_2, metal_panels_pt_4, metal_shutters_pt_1 |
| 8 | Red-brown brick, wide sash windows | Buildings `building_03` |
| 9 | Tan glazed brick | Buildings `building_07` |
| 10 | Mottled brick, gold frames | Buildings `building_09` |
| 11 | Grey brick tenement | Buildings `building_04` |
| 12 | Red brick, patterned piers | Buildings `building_05` |
| 13 | Loft, big mullioned windows | Buildings `building_02` |
| 14 | Tan brick, paired sash | Buildings `building_06` |
| 15 | Modern apartment | painted_concrete_wall_pt_1 + pack glass |

Rules to keep: the column layout is mirrored in `CityMeshes.FacadeLooks` and in PSX/Lit's `FAC_*` tables, so a new atlas must keep the same 16 columns and the same floors per repeat.

`Art/City/Facade/Source/` holds the atlas sources. These ship: skyscraper_building_04, skyscraper_building_08. These are in the folder but are not in the build: skyscraper_building_03, psx_concrete_2, psx2_metal_panels_4, psx2_metal_shutters_1.

### 1b. Older single-facade materials (still in the kit)
| Material | Path | Night mask | Kind |
|---|---|---|---|
| CityFacadeTower | `Art/City/city_facade_tower.jpg` | `Art/City/Night/city_facade_tower_night.png` | PACK (Buildings building_01) |
| CityFacadeMid | `Art/City/city_facade_mid.jpg` | `Art/City/Night/city_facade_mid_night.png` | PACK (building_10) |
| CityFacadeBrick | `Art/City/city_facade_brick.jpg` | none | PACK (brick_modern_02) |
| CityShops (storefront row) | `Art/City/city_shops.png` | `Art/City/Night/city_shops_night.png` | COMPOSED from Shops/Shops_05/_12/_23. No Enhanced version (the Enhanced one was made from branded art) |
| **CityFacadeHouse (all suburban siding)** | `Art/City/city_facade_house.png` (64 px) | `Art/City/Night/city_facade_house_night.png` | **CODE: plain siding, one window. Weak.** |

### 1c. Ground, streets, water
| Surface | Path | Kind |
|---|---|---|
| **All 101 city road surfaces**: one file per road profile × 4 surfaces (asphalt new/old, concrete new/old). Profiles: tw = two-way, ow = one-way, mw/xw = multi-lane, ramp1–3, junction | `Art/City/city_road_<profile>_<surface>.png` | **CODE: hash-noise grain + painted lanes (`PSXRacingBuilder.City.cs` `Grain`/`DrawProfileTex`). Weakest surface in the game; it is the road in your screenshot.** |
| Grass / lawns | `Art/City/Pack/grass_7_city.png` | PACK (PSX Textures v3.1 grass_7); season-tinted |
| Pavement / lots | `Art/City/Pack/concrete_pt_5_city.png` | PACK |
| Structural concrete (walls, medians) | `Art/City/Pack/concrete_pt_1_city.png` | PACK |
| Sidewalks + curbs | `Art/City/Pack/concrete_pt_4_walk.png` | PACK |
| Creek banks / ditches (red clay) | `Art/City/Pack/dirt_pt_7_city.png` | PACK |
| Lakes / rivers | `Art/Water/water_2.png` + `water_1.png` (deep) | PACK (shared with the beach) |

Not used anymore: `Art/City/city_grass.png`, `city_concrete.png`, `city_pavement.png`, `city_water.png`, `city_roof_flat.png`, `city_facade_atlas` predecessors.

### 1d. Street furniture, signs, signals
| What | Path | Kind |
|---|---|---|
| Utility poles, wires, cobra-heads, street lamps, acorn posts | `Art/City/Furniture/CityFurniture.png` | COMPOSED (`tools/city/furniture_atlas.py`: wood_pt_4 + house Metal.jpg) |
| Stop signs, signal poles, mast arms, signal heads, stop bars | `Art/City/Signals/CitySignals.png` | COMPOSED (`tools/city/signals_atlas.py`) |
| Shop / billboard / roadside sign faces | `Art/City/Signs/CitySigns.png` + `CitySigns_night.png` | COMPOSED (`tools/city/signs_atlas.py`), fictional brands |
| Freeway guide signs (letters, arrows, borders, truss metal) | `Art/City/Signs/CityGuide.png` (1024×512) | COMPOSED (`tools/city/guide_atlas.py`; the font table is `Scripts/City/CityGuideFont.cs`) |
| Lamp posts (separate material) | `Art/LifeSim/House/Textures/Metal.jpg` | PACK |
| City trees | `Art/BRP/Gen/TreeAtlas*.png` (5 seasons) | shared with the mountains |

### 1e. Pack models standing in the city
| Model family | Textures | Path |
|---|---|---|
| Uptown towers (tower_01…08) | building_01, _03, _04, _08 | `Art/LifeSim/Skyscrapers/textures/` |
| Low city blocks (city_building_03…18) | building_01.jpg, building_07.jpg, building_10.jpg, brick_modern_02.jpg, Roof_tiles.jpg, ConcreteBare.png, Shops_03.png, Shops_04.png | `Art/Buildings/Textures/` |
| Houses | baked atlas | `Art/City/Props/city_house_atlas.png` (from House pack textures) |
| Trailers | baked atlas | `Art/City/Props/city_trailer_atlas.png` (from `Art/LifeSim/Trailer/Textures/`: 7 ship) |
| Gas stations (canopies, shop, pumps) | baked atlas + glass night | `Art/City/Props/city_station_atlas.png`, `Art/GasStation/City/StationGlass_night.png` |
| Burger drive-thru / pizzeria shells | Burger + Pizzeria pack textures | `Art/LifeSim/Burger/Textures/`, `Art/LifeSim/Pizzeria/Textures/` |

---

## 2. MOUNTAINS
Venues: Blue Ridge, Mt Mitchell, Beech Gap, Blowing Rock, Little Switzerland, Swiss NC226A, Gillespie Gap, Chimney Rock.

| Surface | Path | Kind |
|---|---|---|
| Road | `Art/City/city_road_track_<width>_<surface>.png` | CODE (shared painter, see §4) |
| Ground (near) | `Art/GasStation/Textures/Ground.jpg` (tile 13 m) | PACK |
| Far slopes, per season | `Art/BRP/Gen/FallMottle.png`, `Mottle_Spring/Summer/Winter/Snow.png` | CODE (colour bands); Enhanced exists. This is the "barren far slopes" on next week's list |
| Turf, per season | `Art/BRP/Gen/Turf_Spring/Summer/Winter/Snow.png` | CODE; Enhanced exists |
| Forest billboards, 5 seasons, 4×4 cells of 128 px | `Art/BRP/Gen/TreeAtlas.png` (fall), `_Spring`, `_Summer`, `_Winter`, `_Snow` | COMPOSED from `Art/BRP/Trees/tree*.png` (16 CC0 trees); Enhanced exists |
| Stone guard walls | `Art/Roads/T (3).jpg` | PACK (Roads) |
| Rock cut banks | `Art/Roadside/rock_pt_2.png` | PACK |
| Gravel shoulders | `Art/Roadside/dirt_pt_5.png` | PACK |
| W-beam guardrail | `Art/Guardrail/corrugated_steel_pt_3.png` | PACK |
| Guardrail / reflector posts | `Art/Guardrail/wood_pt_4.png` (+ `Art/Track/Post.png`) | PACK / CODE |

Not shipped: `Art/BRP/Gen/CutBank.png`, `Art/BRP/Gen/Shoulder.png` (old code-drawn versions, replaced).

---

## 3. BEACH (Bogue Banks)
Venues: Emerald Isle, Langston Bridge, Atlantic Beach Bridge.

| Surface | Path | Kind |
|---|---|---|
| Road | `city_road_track_*` (§4) | CODE |
| Dune scrub / ground | `Art/Beach/grass_pt_3.png` (tile 11 m) | PACK |
| Sand | `Art/Beach/sand_texture_21.png` (tile 16 m) | PACK |
| Marsh / cordgrass | `Art/Beach/grass_dead_pt_4.png` | PACK |
| Town yards | `Art/Beach/dirt_texture_8.png` | PACK |
| Sea (shallow / deep) | `Art/Water/water_2.png`, `water_1.png` | PACK (shared with the city) |
| Bridge parapets | `Art/Roads/T (4).jpg` (concrete) | PACK (Roads) |

Not shipped: `Art/Bogue/Gen/Sea/Sand/Marsh/Scrub/Shoulder.png` (old code-drawn versions, replaced).

---

## 4. SHARED BY EVERY VENUE
| What | Path | Kind |
|---|---|---|
| **Circuit and stage road surfaces**, one per road width (61–180 dm) × one-way × 4 surfaces | `Art/City/city_road_track_*_{asphalt,concrete}_{new,old}.png` | **CODE (same noise painter as the city roads)** |
| Road joints / seams | `Art/Track/Joint.png` (tiny) | CODE (the pattern is the design) |
| Start grid | `Art/Track/StartGrid.png` | CODE (the pattern is the design) |
| Snow / slush cover over roads | `Resources/RoadCover.png` (R snow, G slush, B packed, A extent) | COMPOSED (`tools/roadcover/make_cover.py` from v3.1 snow_1/2, snow_melting_1) |
| Ground micro-detail (grass/dirt/rock grain up close) | `Resources/TerrainDetail.png` | COMPOSED (`tools/terraindetail/make_detail.py`) |
| Skies, 10 times of day / weather | `Resources/Sky/sky_{dawn,morning,noon,afternoon,sunset,dusk,night,overcast,overcast_low,storm}.png` | photo panoramas |
| Volumetric clouds | `Resources/Clouds/CloudShape.bytes` (3D noise) | CODE |
| Rain streaks, tyre spray, tyre marks | runtime textures in `Scripts/WeatherFx.cs`, `TireSpray.cs` | CODE |
| Water | `Art/Water/water_1.png`, `water_2.png` | PACK |
| Misc roads-pack images that ship (tree sprites / legacy) | `Art/Roads/Ar (4).png`, `Ar (5).png`, `Ar (6).png`, `T (1).jpg` | PACK; probably only on the legacy circuit |

---

## 5. Not environment (listed so the inventory is complete)
- **Cars:** 162 files in `Art/Car/Models/<car>/textures/` (mostly one `<car>_atlas_512.png` each), 10 paint swatches in `Art/Car/textures/`, 11 night light masks in `Resources/CarLights/`.
- **Interiors / life sim:** 242 files across `Art/LifeSim/House/Textures` (116), `Burger/Textures` (31), `Pizzeria/Textures` (49), `PizzeriaScene/Textures` (46). Also `Art/LifeSim/Seat/*_atlas.png` and `Art/LifeSim/Groceries/Sodas2L.png`.
- **Gas station pack:** 50 files in `Art/GasStation/Textures/`. Its interior, plus Ground.jpg (mountain ground) and Tree.png / Tree_01.png.
- **Garage:** `Art/Engines/LS1_V8.png`.
- **UI / maps:** `Resources/RouteMaps/*.png` (8 race maps), `Resources/charlotte_thumb.png`.
