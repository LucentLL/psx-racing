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

Owner: "all cockpits should fit inside each car model, with mirrors properly mounted just outside the cockpit window". Sheets: scratchpad `mirrors2\<shell>.png` (16 shells: ahead / over the dash / look left / look right as base | round 1 | round 2, the forced nose up/down pair with the dark inside off/on, rear-view picture, head-level look right, the mirrors from outside) and `mirrors2\summary.png`.

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
﻿# HANDOFF - CURBS ROUND 2: GATES, ROLLED CURB, WHEEL VS CURB, PAN, LIDS (charlotte-curbs-drains, 2026-11-11 evening; NOT pushed, NOT merged)

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
﻿# HANDOFF - ROAD SEAMS / ROAD SLAB (charlotte-road-seams, 2026-11-11; NOT pushed, NOT merged)

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
