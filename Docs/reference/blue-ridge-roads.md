# Blue Ridge mountain roads: reference study

Written 2026-11-02 for the owner's request: *"Hopefully this can help with modeling and suggesting needed textures. Also, please use road views publicly available since roads in game are based on real roads."*

**Targets (owner decisions, same day).**
- Visual target: PS3/PS4-era fidelity or better, on a PC first (RTX 3070 class, 8 GB VRAM). The examples are GT6, Forza Horizon 2-4, DriveClub and NFS 2015.
- High-resolution textures will come from upscaling the owner's existing PSX texture packs. Each texture entry in section 4 names its pack source and says whether a 4x-8x upscale is realistic.
- Rules that still apply:
  - no brands, real logos or agency emblems;
  - never thin the near forest;
  - fog only in Fog weather, with distant haze as a separate owner A/B;
  - roads must match the real road.
  - The art-workspace rules in `OneDrive\...\PSX Assets\PSX Racing\AGENTS.md` cover seamless tiles, repeat checks, variants and naming.

**Evidence used.**
- **Owner clips (reference only, never shipped).** All are in `C:\Users\mcgee\Videos\`:
  - `2026-11-02 20-13-15`: Linn Cove, fall, 4K;
  - `20-14-26`: overlook;
  - `20-15-03`: Parkway tunnels in fall;
  - `20-18-49`: summer tunnel, phone;
  - `20-21-14`: winter fog.
- **Public street-level imagery** (Google Street View, viewed in the browser, nothing saved). Every checkpoint was taken on the in-game route: coordinates come from the game's own bakes (`Resources/*_stage.json`, `lat0`/`lon0` + `xyz`). Section 2.15 has links that open each view.
- **Documents:**
  - NPS: tunnel table, Linn Cove history, Parkway FAQ;
  - Structurae / FIU bridge database: viaduct cross-section;
  - NCDOT: weathering-steel guardrail provision;
  - NPS/NHL summaries: Parkway shoulders and masonry.
- **Code:** `PSXRacingBuilder.cs`, `PSXRacingBuilder.Stage.cs`, `PSXRacingBuilder.City.cs`, `PSXLit.shader` on branch `pc-max-build`. Read at 4867fbf4; line numbers re-checked at 8c563b29, after the PC-forest commit 2a21a2a7 moved the forest code.
- **In-game images:**
  - two Blue Ridge stage shots (`Docs/shots/weather/wx_blueridge_*.jpg`);
  - today's pipeline-comparison renders (`C:\Users\mcgee\PSXRelease\compare\*_pair.jpg`, from `Editor/Dev/PipelineCompare.cs`). Both show the edge lines, gravel verge, flat rock cut and billboard trees described below.

**Evidence tags:**
- **[SV]** seen in Street View or a public photosphere at the stated spot.
- **[Clip]** seen in an owner clip.
- **[Doc]** stated by a document.
- **[Gen]** general regional knowledge, not checked at the spot. Verify before modelling.

**Caveats.**
- Hurricane Helene (Sept 2024) closed and changed parts of this area: the Parkway near Little Switzerland, Chimney Rock village and the Rocky Broad River.
- Most checkpoint imagery is dated 2020 to Oct 2026, and the Chimney Rock photospheres are from Aug 2024, before Helene.
- Pick one period per venue before modelling it.

---

## 1. What is factually wrong in the game today

These items compare the code to the real road.

| # | In the game | On the real road | Where in code |
|---|---|---|---|
| F1 | **White edge lines are painted along every stage road**, the Parkway included. The track painter always draws them. | The Parkway has **no edge lines, by design** ("allows it to retain a rural feel", Parkway FAQ) [Doc][Clip][SV at 6 Parkway checkpoints]. White edge lines appear only **in and just around the tunnels** [Clip][Doc: NPS clearances are given "at the edgeline striping"]. NC 128, NC 226, NC 226A, US 221, US 321 and Main St do have edge lines [SV]. NC 215 showed none at two checkpoints [SV]. The Chimney Rock park road showed no markings at all in Aug 2024 [SV]. | `PSXRacingBuilder.City.cs:1041`, `:1073-1074` (DrawRoadTexCore); stages use it via `PSXRacingBuilder.cs:1961-1969` |
| F2 | **The Linn Cove Viaduct and the 7 other Grandfather bridges** carry a 0.85 m **dry-stone parapet**. | A **low concrete curb with a two-rail metal bridge railing** on square posts sits on the viaduct and the other bridges [SV: viaduct deck, bridge at 36.0990,-81.7909][Clip]. Stone appears only on the short approach walls. | Deck stations are always walled (`Stage.cs:2498`) in `theme.wall` stone (`PSXRacingBuilder.cs:363`, `Stage.cs:5291-5293`, `:6128`) |
| F3 | **The viaduct is as narrow as the approach road**: a 6.4 m painted road plus a 1.15 m verge each side, so about 8.7 m between parapets. | The deck has **two ~17 ft lanes, no shoulders**, about 34 ft (10.4 m) between curbs, with an overall deck width of 11.4 m [Doc: FIU/TRB summary, Structurae][SV]. It is visibly wider than the approaches. | `TrackCatalog.cs:490`; `Stage.cs:53`, `:177-178`; `PSXRacingBuilder.cs:4762-4763` |
| F4 | **Red-and-white reflector posts** stand behind the stone walls every 16 m on Blue Ridge and Mt Mitchell. The comment says "the real parkway has them". | **No roadside delineator posts** appear at any Parkway checkpoint or in any clip. Small orange/white reflector panels exist only **on tunnel walls** [Clip 20-15-03]. | `PSXRacingBuilder.cs:367-370`, `:389-392`; `Stage.cs:5358-5423` (post height `:5364-5367`) |
| F5 | **Galvanised W-beam on the Parkway sections of both loops.** MountainLoopTheme sets `guardrail = true` for the whole loop. | The Parkway uses **timber guardrail**: a weathered timber rail on short posts [SV 36.0962,-81.8111][Clip]. Low random-rubble stone walls sit at overlooks and bridge approaches [SV 36.0922,-81.8147][Clip]. W-beam is right only on NC 226A, NC 226 and US 321 [SV]. | `PSXRacingBuilder.cs:503-518` (`:511-512`) |
| F6 | **NC 215 (Beech Gap) gets a galvanised-looking W-beam**, tinted lighter. | NC 215 has **weathering-steel (rust-brown) W-beam** on both sides [SV 35.2809,-82.9192 and 35.2974,-82.9124]. NCDOT has a weathering-steel guardrail provision [Doc]. | `PSXRacingBuilder.cs:415`; tint at `Stage.cs:5292` |
| F7 | **Chimney Rock park road**: double yellow plus edge lines, and galvanised W-beam. | Pale, aged asphalt with **no visible markings**, and **rustic timber post-and-rail** at the drops and lots [SV Aug-2024 photospheres at 35.4337,-82.2419 and 35.4317,-82.2425]. | `PSXRacingBuilder.cs:433` (MountainLoopTheme) + F1 |
| F8 | **Tunnels are rectangular boxes**: flat ceiling at 5.2 m, vertical walls, and a flat 36 m x 16 m rock panel with a square hole as the portal. | Parkway tunnels are **arched (horseshoe) bores**. Little Switzerland measures **19'2" (5.84 m) over the centre and 14'3" (4.34 m) over the edge line** [Doc NPS]. The east portal is a **dressed-stone voussoir arch in a rubble face** [SV 35.8510,-82.0846]. Other bores are rough unlined rock with **concrete-lined sections**, edge lines and wall reflectors. All are **unlit** [Doc][Clip]. | `Stage.cs:6884`, `:6898`, `:6944-6966`, `:7060-7080` |
| F9 | **Spruce/fir are picked by height above each stage's own base elevation**, not by real altitude. Chimney Rock (325-586 m), NC 226 (443-853 m) and lower NC 226A get up to **36% spruce/fir billboards**. Mt Mitchell's 1,900-2,000 m summit gets **only 36%**. | Spruce-fir grows only high up, roughly above 1,500-1,700 m. At the top of Mt Mitchell it is near-pure spruce-fir, with grey dead Fraser fir snags [SV 35.7444,-82.2778]. The low venues have white pine, Virginia/pitch/Table Mountain pine and hemlock instead [Gen]. | `Stage.cs:7730-7743` (`InverseLerp(35, 105, groundY)`, with groundY relative to baseM) |
| F10 | **Blowing Rock loop is "nothing built"**: no buildings, lamps or kerbs, and every leg is 6.4 m wide. | The loop runs **Main Street through the village**: shops, sidewalks, curbs, street trees and utility poles [SV Oct 2026]. It returns on **US 321, a 4-5 lane road** with lane lines, edge lines and W-beam [SV 36.1431,-81.6683]. Little Switzerland village and Chimney Rock village (the start of that stage) are also built-up. | `PSXRacingBuilder.cs:503-518` (`buildingEvery = 0`); `TrackCatalog.cs:708` |
| F11 | **Viaduct piers stand every 26 m**: about 15 square piers along the 392 m span. | **7 permanent piers** [Doc NPS]. The precast box girder is iron-oxide tinted to match the rock [Doc NPS]. | `PSXRacingBuilder.cs:4811-4812` |
| F12 | **The Parkway's stone texture is a regular coursed ashlar**, like a building wall. | Parkway walls are **random rubble / random-coursed fieldstone** with deep joints, lichen and rounded or flat caps, about 0.5-0.7 m high where seen [SV 36.0922,-81.8147][Clip]. | `Art/Roads/T (3).jpg`, `PSXRacingBuilder.cs:363`; `Stage.cs:5156` |

**Lower-impact mismatches:**
- A grey gravel verge strip runs on every stage. The Parkway has **mown turf shoulders** of about 4.5 ft [Doc][SV].
- Winter understory goes bare. On the real slopes, rhododendron and laurel stay **evergreen** [Clip 20-21-14].
- There are no mileposts, Parkway signs, overlooks, split-rail fences or culvert headwalls (section 3).

---

## 2. What the real roads look like, feature by feature

### 2.1 Carriageway and lanes

| Road (in-game venue) | Real width | Game `roadWidth` | Verdict |
|---|---|---|---|
| Blue Ridge Parkway: BlueRidge, and the Parkway legs of BlowingRock and LittleSwitzerland | Typically **21 ft (6.4 m) pavement** with **4.5 ft stabilised turf shoulders** [Doc: docsouth/NHL]; two lanes of about 10.5 ft | 6.4 m (`TrackCatalog.cs:490`, `:708`, `:724`) | **matches** |
| Linn Cove Viaduct deck | **~34 ft (10.4 m) between curbs**, two ~17 ft lanes, no shoulders; deck 11.4 m overall [Doc] | Same ribbon as the approach | **wrong** (F3) |
| NC 128 (MtMitchell) | 2-lane park road, about 20 ft, with edge lines and grass shoulders [SV] | 6.1 m (`:589`) | matches |
| NC 215 (BeechGap) | Narrow 2-lane, about 18-20 ft [SV] | 6.1 m (`:613`) | matches |
| NC 226A (SwissNC226A, the loop) | Narrow 2-lane, about 18-20 ft, tight hairpins, driveways lower down [SV] | 6.1 m (`:759`) | matches |
| NC 226 (GillespieGap) | 2-lane primary road with 11 ft lanes and a narrow shoulder [SV] | 6.7 m (`:779`) | matches |
| US 221 Blowing Rock Hwy (BlowingRock) | 2-lane [SV] | 6.4 m | matches |
| US 321 through Blowing Rock (BlowingRock return leg) | **4-5 lanes** (2 + 2 + centre turn lane in places), edge lines, W-beam [SV] | 6.4 m | **wrong** (F10) |
| Chimney Rock park road | Narrow 2-lane, about 6 m at the hairpins [SV] | 6.1 m (`:821`) | matches |

### 2.2 Markings

- **Parkway (all Parkway legs):**
  - **Solid double yellow** centre line, crisp and recently repainted near Linn Cove [Clip][SV].
  - **No edge lines** [Doc][Clip][SV].
  - **Inside tunnels:** white edge lines plus the double yellow; edge striping starts a few tens of metres before some portals [Clip 20-15-03].
- **NC 128, NC 226, NC 226A, US 221:** double yellow plus white edge lines [SV].
- **US 321:** double yellow, white lane lines and edge lines [SV].
- **Main St Blowing Rock:** double yellow, edge line, curbs [SV].
- **NC 215:** faded double yellow; no edge lines visible at two checkpoints [SV]. Verify along the route.
- **Chimney Rock park road:** no visible markings at three checkpoints in Aug 2024 [SV].
- **Game:** two 12 cm yellow lines with a 12 cm gap, and 12 cm white edge lines on every stage (`City.cs:1067-1074`, `:1090`). The line sizes are fine. Edge-line presence must become per road and per segment (F1).

### 2.3 Pavement colour and texture

- **Parkway:**
  - **aged dense-graded asphalt**, mid-grey to blue-grey;
  - exposed light aggregate and polished wheel paths;
  - rectangular darker **patches** and long longitudinal cracks [SV 36.1008,-81.7786; 36.0895,-81.8247];
  - **fresh black overlays** in places, as on the south approach to the viaduct in the owner's clip.
  - The edge is ragged and **unpaved**: it breaks straight into turf, and where cars pull off there is a 10-40 cm worn band of dirt and gravel [SV].
- **Viaduct and Grandfather bridge decks:**
  - light warm-grey **concrete**, iron-oxide tinted [Doc];
  - smooth broom finish, segment joints across the deck, darker sealer patches [SV][Clip].
- **State roads:**
  - darker asphalt;
  - **black crack-sealant lines ("tar snakes")** and patch work;
  - grimy wheel paths; leaf litter piles at the edges in fall [SV][Clip].
- **Chimney Rock:** very pale, bleached and aged surface [SV].
- **Winter (clip 20-21-14):**
  - wet, near-black asphalt with sky reflections;
  - brown wet leaf litter banked against both edges;
  - yellow lines that read strongly in fog.

### 2.4 Shoulders, verges and drainage

- **Parkway:**
  - **mown turf to the pavement edge**, about 4.5 ft, designed as "earthen and sod shoulders" [Doc: NPS repair story][SV][Clip];
  - beyond it, a grassed or **stone-lined swale/ditch** [SV 36.0895,-81.8247];
  - **concrete flume channels** on the uphill side near MP 304 [Clip 20-13-15 at 13.6 s];
  - **drop inlets with cast grates** at the pavement edge [SV 36.0922,-81.8147];
  - **random-rubble culvert headwalls**, an NPS standard detail [Doc: NHL summary].
- **State roads:** narrow grass or gravel shoulders, often only 0.3-1 m before the ditch or cut [SV].
- **Fall and winter:** leaf litter fills the verge and drifts onto the pavement edge [Clip 20-15-03, 20-21-14].
- **Game:**
  - a 0.9 m gravel strip (`dirt_pt_5`) plus 0.2 m of paved shoulder on every stage (`Stage.cs:53`, `:67`; `PSXRacingBuilder.cs:1005`);
  - a 0.8 m foreslope and 0.4 m ditch in cuts (`Stage.cs:125-130`).
  - **Partly right.** The surface should be turf on the Parkway, and drainage details are missing.

### 2.5 Barriers

| Type | Where it really is | Look | Game |
|---|---|---|---|
| **Parkway timber guardrail** | Fills, overlooks and pull-offs all along the Parkway [SV 36.0962,-81.8111][Clip 20-13-15] | Weathered **silver-grey timber rail**, large rectangular section, butt-jointed at short **dark posts**, top about 0.7 m, set about 1.5-2 m off the pavement on turf. Older runs are documented as hardwood rails on **precast concrete posts** (NPS 1957 design sheet, as summarised in search; not read). The owner read the posts as stone. Newer federal work uses steel-backed timber. Verify the post material per site. | **missing**: Parkway legs get stone (BlueRidge/MtMitchell) or W-beam (loops) |
| **Stone guard wall** | Viaduct approaches, overlooks, some curves [SV 36.0922,-81.8147][Clip] | Low, about 0.5-0.7 m, random rubble of grey-brown local stone with lichen and rounded or flat caps | Present, but too uniform and too frequent: it is used for every warranted run and every deck. Wrong pattern (F12). |
| **Bridge railing** (viaduct and Grandfather bridges) | All the 1980s "missing link" bridges [SV][Clip] | Concrete curb about 0.3 m high with a 0.5 m ledge; square metal posts about 2 m apart; **two horizontal rails** (about 0.6 m and 1.0 m), galvanised or aluminium grey | **wrong** (F2) |
| **Galvanised W-beam** | NC 226A, NC 226, US 321 [SV] | Standard W-beam on posts with blockouts; buried or flared ends | Present (`Stage.cs:5730-5733`; good profile). The texture is a corrugated-sheet pack image (`corrugated_steel_pt_3`). |
| **Weathering-steel W-beam** | NC 215 [SV] | Same profile, **rust-brown** patina, both sides of the road | **wrong colour** (F6) |
| **Rustic timber post-and-rail** | Chimney Rock park road and lots [SV Aug 2024] | Round or split timber rails, 2-rail, on timber posts | **wrong** (F7) |
| **Delineator posts** | Not on the Parkway; reflector panels only on tunnel walls [Clip] | n/a | **wrong** (F4) |

### 2.6 Stone masonry beyond walls

- **Tunnel portals:** dressed voussoir ring, rubble face, small wing walls. Most portal masonry was added in the 1950s-60s [Doc]. Other portals are plain blasted rock around the bore [Clip 20-15-03 portal].
- **Culvert headwalls** (random rubble) and bridge abutment facing on older Parkway sections [Doc].
- **Game:** none, apart from the guard walls and the retaining walls under guardrails (`Stage.cs:5326-5342`, `:5976`).

### 2.7 Split-rail fences

- **At Linn Cove** (uphill side opposite the overlook) [SV 36.0962,-81.8111]:
  - stacked/split-rail fence of grey weathered rails;
  - zig-zag ("worm") style with crossed posts;
  - set on the turf 2-3 m off the road.
- **At Rough Ridge and the visitor-centre approach:** the same type, visible in the owner clip (frames 1-4).
- **Game:** missing.

### 2.8 Signs and mileposts

- **Parkway mileposts:**
  - **a short white concrete post** (about 0.6 m) with **black numerals stacked vertically** ("3 / 0 / 4") [Clip 20-13-15 at 13.6 s, MP 304];
  - stands on the turf shoulder, one per mile [Doc NPS: "concrete mileposts"].
- **Parkway signs:**
  - **brown boards with white routed lettering** on brown posts [SV 36.0922,-81.8147][Doc: brown is the NPS guide-sign colour];
  - examples: overlook names with elevation, tunnel names with clearances, "Linn Cove Viaduct".
  - In game: plain brown routed boards with neutral lettering, **no agency arrowhead or emblem**. Place names as words (as `psx-racing-highway-signs` already does for routes).
- **Warning signs:** standard yellow diamonds (curve arrows, chevrons on NC 226A hairpins), stop signs at Parkway access junctions [SV 36.1471,-81.6616], speed limits (Parkway 45 mph).
- **Game:** no signs or mileposts on any stage. Confirmed: no sign, milepost, overlook or fence code in `PSXRacingBuilder.Stage.cs`.

### 2.9 Tunnels

| Item | Real | Game |
|---|---|---|
| Shape | Horseshoe/semicircular arch. Little Switzerland is **19'2" (5.84 m) over the centre** and **14'3" (4.34 m) over the edge line** [Doc NPS table: all 26 tunnels, 17'7"-19'3" centre / 11'7"-14'8" edge]. | 5.2 m flat box (`Stage.cs:6884`): **wrong** (F8) |
| Length | Little Switzerland **575 ft (175 m)**, MP 333.1 [Doc NPS] | 188 m (bake: 3,992-4,180 m): matches |
| Interior | Rough blasted rock (dark grey, wet) with a **concrete-lined** board-formed arch near some portals; **orange/white reflector panels** on the walls at about 1 m, every 10-15 m; **white edge lines** plus double yellow; **no lights** [Doc NPS: "do not have lights inside them"][Clip] | `rock_pt_2` tinted 0.62 on flat walls (`Stage.cs:6918-6919`), no reflectors: **partly** |
| Portal | Dressed-stone arch ring in a random-rubble face, about 8-10 m wide, set into a forested, ferny hillside [SV 35.8510,-82.0846]. Or an **unlined rock face** around the bore [Clip]. Brown name/clearance sign beside it [Clip 20-18-49]. | Flat 36 x 16 m rock panel, rectangular opening (`Stage.cs:6898`, `:7060-7080`): **wrong** |
| Approach | Leaf litter on the shoulders; the Parkway look holds right to the portal [SV][Clip] | n/a |

### 2.10 Linn Cove Viaduct and the Grandfather bridges

**Viaduct:**
- 1,243 ft long, MP about 304.
- **153 precast segments** of about 50 t; **only one is straight** [Doc NPS].
- **7 piers**; concrete **tinted with iron oxide** to match the rock [Doc NPS].
- Single-cell box about 9 ft deep, with an 18 ft bottom flange [Doc FIU].
- Built top-down; only the trees directly under it were cut; a **boulder-strewn cove** lies beneath [Doc NPS].

**Deck:**
- concrete; double yellow; no edge lines;
- curb and two-rail metal railing as described in 2.5 [SV][Clip].

**Approaches:**
- **low stone walls** on the approaches;
- **timber guardrail** and a **split-rail fence** at the overlook just north [SV][Clip].

**Other bridges:** the 8 OSM spans in the BlueRidge bake (88-392 m) were all checked at one, 36.0990,-81.7909. It is the same family: a concrete deck with a two-rail metal railing over the canopy [SV].

**Game:**
- concrete deck, soffit, fascias and square piers every 26 m (`PSXRacingBuilder.cs:4838-4900`);
- concrete ribbon on the deck (`:1958`, `:1966-1969`): **matches**;
- parapet, width and pier count are **wrong** (F2, F3, F11).

### 2.11 Rock cuts, outcrops and boulders

**Parkway cuts:**
- mostly **vegetated slopes** with ragged rock outcrops [SV][Clip 20-15-03];
- near Linn Cove: huge lichen-covered **boulders** and Grandfather's crags (visible on the skyline in the clip at 13.6 s).

**State-road cuts** (NC 226, NC 226A, NC 215):
- steeper blasted faces with **rhododendron rooted on top** and seeping dark stains [SV];
- on NC 215, light rock outcrops show above the road [SV 35.2809,-82.9192].

**Rock colour by venue (approximate [Gen]; check against imagery):**
- Grandfather: buff-grey metasandstone/quartzite.
- Mt Mitchell / Black Mountains: dark mica gneiss/schist.
- Little Switzerland, Gillespie Gap, NC 226/226A (Spruce Pine mica district): grey gneiss with light pegmatite veins and mica glitter.
- Chimney Rock: light pinkish-grey granitic gneiss cliffs.
- NC 215: grey banded gneiss.

**Game:**
- cut banks with `rock_pt_2` and wobbled tops (`Stage.cs:6347`; `PSXRacingBuilder.cs:1004`): **partly**;
- no boulders, outcrop meshes or regional rock colours: **missing**.

### 2.12 Vegetation by elevation and season

| Zone (approximate elevation) | Real species and look [Gen; spot-checked in SV/clips] | Venues |
|---|---|---|
| **Spruce-fir** (above ~1,500-1,700 m) | Red spruce, Fraser fir (dense young fir; **grey dead fir snags** from the balsam woolly adelgid); mountain ash with red berries; blackberry, blueberry and grass openings [SV 35.7444,-82.2778] | MtMitchell (upper ~2/3), BeechGap top, Grandfather's upper slopes |
| **Northern hardwoods** (~1,200-1,600 m) | Yellow birch, American beech (the "beech gaps"), sugar maple, yellow buckeye, black cherry, red maple; red spruce mixed in; **Catawba rhododendron, mountain laurel, blueberry, sand myrtle** on rocky heath (Rough Ridge) | BlueRidge, MtMitchell (bottom), BeechGap (top half), BlowingRock |
| **Oak / cove hardwoods** (~500-1,200 m) | Northern red, chestnut and white oak; hickory; tulip poplar; red maple; sourwood (red in fall); blackgum; **eastern white pine**; hemlock (many dead grey skeletons); **rosebay rhododendron thickets** on north slopes and streams [SV NC 226, NC 226A][Clip 20-21-14] | LittleSwitzerland, SwissNC226A, GillespieGap, BeechGap (lower half) |
| **Dry oak-pine** (~300-700 m, ridges) | Chestnut oak, Table Mountain and pitch pine, Virginia pine, mountain laurel; tulip poplar and hemlock in coves [SV Chimney Rock] | ChimneyRock, lower NC 226A |

**Fall (clips 20-13-15, 20-14-26):**
- a mix of gold (birch, beech, poplar), orange (sugar maple), scarlet (red maple, sourwood), russet (oaks) and dark-green spruce;
- red heath shrubs (blueberry) on balds.
- Peak moves downhill: about the first week of October above 1,500 m, mid-October around Linn Cove, late October to early November below 700 m [Gen].
- The game uses **one fall dress for every elevation** (`Season.cs:249-258`). Elevation-staged fall (green low, peak mid, past-peak high) would match the clips better.

**Winter (clip 20-21-14):**
- bare grey trunks;
- **evergreen rhododendron/laurel understory and dark spruce**;
- brown leaf litter; wet road.
- The game's winter atlas turns the 28% understory bare (`Stage.cs:253`, `:7462`): **wrong for the evergreen shrub layer**.

**Summer (clip 20-18-49, SV):** a tall closed canopy right over the road; ferns; grass verges.

**Game:**
- 16 X-billboards from 128 px cells (`Stage.cs:7143-7170`, `:7749-7774`);
- 7.5 m grid to 45 m, 28% understory, 12-16 m heights (`:230-261`);
- on the PC only, since 2a21a2a7:
  - the band filled to 0.85 keep past 45 m;
  - a level crown card on every tree (`:7362-7383`, `:7700-7712`);
  - a far forest at 12 m pitch to 2.1 km (`:7576-7681`);
- elevation rule wrong (F9);
- no shrub layer, no ferns, no boulders.

### 2.13 Fog, haze and light

**Overlook clip (20-14-26):**
- ridges step from dark blue-green (near) to pale blue-grey (far) in **5-7 distinct layers**;
- **cloud shadows** on the slopes;
- **sun shafts** through cloud gaps.
- The haze is the Blue Ridge's namesake: tree-emitted aerosols plus humidity [Gen].
- Visible distance from overlooks is tens of kilometres.

**Winter clip:** dense radiation/upslope **fog** at road level (Fog weather).

**Game:**
- the stage's far ground reaches 2.3 km (`Stage.cs:264`);
- stage fog band ends at 1,420 m (`:202`);
- distant haze is owner A/B only;
- no ridge rings beyond the DEM chunk, no light shafts.
- **Partly.**

### 2.14 Built environment on the loops

- **Blowing Rock:**
  - Main St: shops and inns, sidewalks with curbs, street trees, utility poles and power lines, parking, crossings [SV Oct 2026];
  - US 321: a 4-5 lane road with commercial frontage [SV].
- **Little Switzerland:** a few chalet-style inns and shops at the NC 226A/Parkway link [Gen].
- **NC 226A lower:** rural houses, driveways with concrete aprons, mailboxes, utility poles [SV 35.8303,-82.0785].
- **Chimney Rock:** Main St shops by the river at the start; the park entrance bridge over the Rocky Broad River (pre-Helene) [Gen][SV].
- **Game:** no buildings, poles or kerbs on any mountain venue (`PSXRacingBuilder.cs:503-518`): **missing**.

### 2.15 Street-level checkpoints

Each link opens Google Street View at that spot and heading. Coordinates come from the game's own bake for that venue. Panorama IDs are given where the view was a Google capture; "photo" means a user photosphere.

| Venue | Feature | Link (lat,lon @ heading) | Imagery / pano |
|---|---|---|---|
| BlueRidge | Viaduct deck: concrete, double yellow, no edge lines, curb + 2-rail railing | [36.09344,-81.81300 @200](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.09344,-81.81300&heading=200); railing close-up @255 | CS8DLDsvoDIQKCFprxBrnw |
| BlueRidge | Timber guardrail (overlook) + split-rail fence, turf shoulders | [36.09625,-81.81114 @177](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.09625,-81.81114&heading=177); rail close-up @135 | YxVMLwMnGdtX7NA43qyFsg |
| BlueRidge | Low rubble wall, drop inlet, brown sign, no edge lines | [36.09218,-81.81466 @204](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.09218,-81.81466&heading=204) | wHnPYmCO5TiMiWsRIHlPnw |
| BlueRidge | Smaller bridge: same deck and railing family | [36.09902,-81.79085 @260](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.09902,-81.79085&heading=260) | BWZ-3WHyMz-mdwVYG3NRJw |
| BlueRidge | Typical forest section: turf, stone-lined ditch, aged asphalt | [36.08952,-81.82468 @263](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.08952,-81.82468&heading=263) | 5W95213AQT0ss5mUDz95vA |
| BlueRidge | High open section: spruce, worn edge, patches | [36.10075,-81.77856 @194](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.10075,-81.77856&heading=194) | QH_7rEjBn8AKoaZexm34IQ |
| BlueRidge | Milepost 304 (white post, vertical numerals) | Owner clip `20-13-15` at 13.6 s | Clip |
| MtMitchell | Edge lines, heath + spruce-fir, dead fir snag | [35.74442,-82.27779 @13](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.74442,-82.27779&heading=13) | BeaG1PdIJ5vrLnZcFDloug (Oct 2025) |
| MtMitchell | Edge lines, spruce-fir (temporary silt fence) | [35.75867,-82.27131 @2](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.75867,-82.27131&heading=2) | FWcCGlF_Hsn_uocbgQNYZg (Oct 2025) |
| MtMitchell | Lower: edge lines, spruce | [35.72988,-82.27938 @28](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.72988,-82.27938&heading=28) | photo, Jun 2020 |
| BeechGap | Weathering-steel W-beam both sides, no edge lines, outcrop | [35.28094,-82.91919 @201](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.28094,-82.91919&heading=201) | nybxWvLCn22nhSnAOmo45g |
| BeechGap | Near top: spruce + hardwoods, weathering steel | [35.29744,-82.91241 @246](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.29744,-82.91241&heading=246) | cZKkc9MxTVjpLVtzFG1-Uw |
| BlowingRock | Parkway leg: forest, no edge lines | [36.15578,-81.68002 @243](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.15578,-81.68002&heading=243) | o64gk24YSjLojAVbAjRCKA (Jul 2024) |
| BlowingRock | US 221: 2-lane, faint edge line | [36.14216,-81.69570 @162](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.14216,-81.69570&heading=162) | P9E8vocAoGayC6scC16-yw |
| BlowingRock | Main St village: curbs, sidewalk, poles | [36.13467,-81.67283 @20](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.13467,-81.67283&heading=20) | Tv0UiDE_a3QpeOMI-J36iQ (Oct 2026) |
| BlowingRock | US 321: multi-lane, W-beam | [36.14311,-81.66833 @20](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.14311,-81.66833&heading=20) | TFE_B0fGNHV4skZM1n9DYg (Oct 2026) |
| BlowingRock | Parkway access junction: stop sign | [36.14714,-81.66157 @222](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=36.14714,-81.66157&heading=222) | wCMCs0HKFcjGF7ZC96_XUw (Jul 2024) |
| LittleSwitzerland | Parkway leg: forest, no edge lines | [35.84986,-82.06845 @263](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.84986,-82.06845&heading=263) | 9iei1awVyUxZh62wL_mYEQ |
| LittleSwitzerland | Tunnel approach | [35.85078,-82.08406 @299](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.85078,-82.08406&heading=299) | PHD2NjQfgwXQyep_Ggiplw |
| LittleSwitzerland | **East portal**: voussoir arch in a rubble face | [35.85101,-82.08457 @298](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.85101,-82.08457&heading=298) | 5nH-ORCkuiYpWxp4cKnIoA |
| LittleSwitzerland / SwissNC226A | NC 226A: edge lines, galvanised W-beam | [35.84785,-82.07946 @55](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.84785,-82.07946&heading=55) | SjRQNyIc5chJi2pkyswJYA (May 2024) |
| SwissNC226A | Hairpin, boulder/rock bank | [35.83304,-82.10463 @180](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.83304,-82.10463&heading=180) | vLfAsUiMKWE9RrtOdwy5yA |
| SwissNC226A | Lower: houses, poles, driveways, edge lines | [35.83031,-82.07851 @138](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.83031,-82.07851&heading=138) | aqp3r5C09n3lDgge4ZlKlg (May 2024) |
| GillespieGap | NC 226: edge lines, W-beam, rhododendron cut, winter | [35.83655,-82.04438 @54](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.83655,-82.04438&heading=54) | 9TUENuCotLDCOg6qkpHDOA |
| ChimneyRock | Rustic timber guardrail, unmarked road | [35.43372,-82.24186 @41](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.43372,-82.24186&heading=41) | photo, Aug 2024 |
| ChimneyRock | Pale unmarked asphalt, post-and-rail at the lot | [35.43169,-82.24252 @242](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.43169,-82.24252&heading=242) | photo, Aug 2024 |
| ChimneyRock | Hairpin, rock island, oak-poplar | [35.42914,-82.24646 @343](https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=35.42914,-82.24646&heading=343) | photo, Aug 2024 |

**Coverage gaps:**
- No Google car coverage was found at several Grandfather points (Rough Ridge, ~1.0 km and ~2.3 km into the stage) or on most of the Chimney Rock park road (photospheres only).
- Mapillary is the next source to try there.

---

## 3. Game vs real: feature comparison

| Feature | Game today (file:line) | Verdict |
|---|---|---|
| Lane widths, Parkway and state roads | `TrackCatalog.cs:490`, `:589`, `:613`, `:708`, `:724`, `:759`, `:779`, `:821` | **matches** |
| Viaduct carriageway width | Approach ribbon carried over the deck; walls at road half + 1.15 m (`Stage.cs:53`, `:177-178`) | **wrong** |
| Centre line | Solid double yellow, 12 cm lines (`City.cs:1067-1071`) | **matches** |
| Edge lines | Always painted (`City.cs:1041`, `:1073-1074`) | **wrong** (Parkway, NC 215?, Chimney Rock); **matches** NC 128, 226, 226A, US 221/321 |
| Tunnel edge lines | Painted (same) | **matches** |
| Asphalt look | Code noise "Grain", 256 px across the road, point-sampled (`City.cs:881-887`, `:1053`; `PSXRacingBuilder.cs:1056-1086`) | **partly**: no aggregate, patches, cracks, tar snakes or wear |
| Concrete bridge deck surface | Concrete ribbon on decks (`PSXRacingBuilder.cs:1958`, `:1966-1969`) | **matches**: joints missing |
| Shoulder material | 0.9 m gravel strip + 0.2 m paved (`Stage.cs:53`, `:67`; `PSXRacingBuilder.cs:1005`) | **partly**: Parkway is turf |
| Drainage (ditches, flumes, inlets, headwalls) | Graded ditch only (`Stage.cs:125-130`) | **missing** |
| Parkway barrier type | Dry stone everywhere warranted (`PSXRacingBuilder.cs:363`, `Stage.cs:6128`) | **wrong**: mostly timber guardrail, with stone at approaches and overlooks |
| Stone wall pattern and height | Regular ashlar `T (3).jpg`, 0.85 m (`Stage.cs:5156`, `:5694`) | **partly / wrong pattern** |
| Bridge parapets | Theme wall (`Stage.cs:2498`, `:5291-5293`) | **wrong** |
| State-road W-beam | Real section, 2 m posts (`Stage.cs:5730-5752`), texture `corrugated_steel_pt_3` (`PSXRacingBuilder.cs:997`) | **matches** shape; texture weak |
| NC 215 W-beam colour | Galvanised tint (`Stage.cs:5292`) | **wrong** |
| Chimney Rock barrier | W-beam (`PSXRacingBuilder.cs:433`) | **wrong** |
| Reflector posts | Red/white, 1.5 m, every 16 m (`Stage.cs:5358-5423`) | **wrong** on Parkway/Mitchell; tunnel wall reflectors **missing** |
| Split-rail fences | none | **missing** |
| Signs, mileposts | none | **missing** |
| Overlooks / pull-offs | none (v2 backlog in memory) | **missing** |
| Tunnel bore | Box, 5.2 m (`Stage.cs:6884`, `:6944-6966`) | **wrong** |
| Tunnel portal | Flat rock panel (`Stage.cs:6898`, `:7060-7080`) | **wrong** |
| Tunnel lighting | None placed | **matches** (real tunnels are unlit); needs eye adaptation |
| Viaduct structure | Box deck, fascia, piers every 26 m (`PSXRacingBuilder.cs:4770`, `:4811-4812`) | **partly**: 7 piers, tinted concrete, segment joints |
| Rock cuts | Battered rock faces (`Stage.cs:6347`), `rock_pt_2` | **partly** |
| Boulders / outcrops | Cliffs are left bare of trees only (`Stage.cs:7445`) | **missing** |
| Forest density near road | 7.5 m grid to 45 m, ≥2,200 trees/km (`Stage.cs:246-249`) | **matches** feel; keep it |
| Tree representation | X-billboards, 128 px cells (`Stage.cs:7749-7774`); level crown cards on the PC (`:7700-7712`) | **wrong** for the new target |
| Species by elevation | Relative to baseM (`Stage.cs:7730-7743`) | **wrong** (F9) |
| Shrub layer (rhododendron, laurel, heath) | Only small copies of the same trees (`Stage.cs:253`) | **missing** |
| Winter evergreen understory | Bare | **wrong** |
| Far forest | PC far forest to 2.1 km (`Stage.cs:7576-7681`); code-drawn mottle beyond (`:7227-7248`) | **partly** |
| Layered far ridges / haze | Ends at 1.5-2.3 km | **missing** (owner A/B for haze) |
| Village buildings, poles, curbs on loops | `buildingEvery = 0` (`PSXRacingBuilder.cs:509`) | **missing** |

---

## 4. Prioritised model and texture list (PS3/PS4-era target, PC first)

### 4.1 Targets

**Texel density** (screen-driven; a racing camera sits 1-1.5 m above the road and passes walls at 0.5-3 m):

| Surface | Target |
|---|---|
| Road surface | **512 px/m** base, plus a micro-detail normal at about 2,000 px/m (a 512² tile per 0.25 m) |
| Roadside hero props within 3 m (rails, walls, posts, bridge railing, portal) | **512-1,024 px/m** (trim sheets) |
| Terrain and rock within 20 m | **256-512 px/m** with a detail layer |
| Mid-ground (20-150 m) | 64-128 px/m |
| Far canopy | 4-20 px/m baked colour |

**Material set (all tiling materials):**
- albedo (sRGB, BC7);
- tangent normal (BC5);
- packed mask: **R = AO, G = roughness, B = height** (BC7, linear); a metal-free set needs no metallic channel; the steel trims carry metallic in a separate mask or in the alpha.
- Label each channel in the repeat previews (AGENTS.md).

**Resolutions:** listed per texture below. Typical sizes:
- 2048² for tiling ground, road, rock and walls;
- 4096² for hero rock and the decal atlas;
- 2048x1024 or 2048x512 trims;
- 2048² leaf and needle atlases per species group;
- 2048-4096 octahedral impostor atlases.

**VRAM** (RTX 3070, 8 GB): a 2K PBR set (3 maps, BC, mips) is about 17 MB, so 50 sets is about 0.85 GB per stage. That fits comfortably next to cars and the city. Use texture streaming for 4K sets.

**AGENTS.md rules that apply:**
- Every repeating material must be seamless on U and V.
- Show a 3x3 repeat, a half-tile offset and an 8x8 reduced repeat from the **shipping export**.
- Check feature continuity, not only edge pixels (plank widths, stone courses, crack networks).
- Record the repeat axes.
- Make **variants** for frequently repeated materials (grass, asphalt, rock, stone wall), with one default per binding and alternates in named folders.
- Candidate PNGs keep the original basename plus the batch suffix (`_overhaul1` in the current rules).
- Enhanced exports are **2x the approved authoring size**, so approve 1024 or 2048 authoring sizes to land at 2048 or 4096.

**Upscale rule of thumb used below:**
- **512 px photographic sources** (PSX Textures II "512" set) survive **4x (to 2048)** with an AI upscaler. Height, normal and roughness can be generated from them, and the pack's own 512 normal maps help. **8x does not survive**: the detail is invented and tiling artefacts multiply.
- **1024 px sources** (PSX Nature `stone_1`/`stone_2`) survive 4x to 4096.
- **256 px sources** need 8x for 2048. They are good only for colour reference, decals or small props at 1024.
- **128 px tree billboards and the 32-64 px PSXForest sheets are not usable** at this target.

Pack roots, abbreviated:
- `T2` = `PSX Assets\PSX Textures II v1.6\PSX Textures II\512\Color Maps\` (normal maps sit beside it in `512\Normal Maps\`)
- `T3` = `PSX Assets\PSX Textures v3.1\PSX Textures\256\Color Maps\` (256 px, with normal maps)
- `NAT` = `PSX Assets\PSX Nature v1.7.3\PSX Nature\Textures\`
- `VEG` = `PSX Assets\PSX Racing\Vegetation\Vegetation\Textures\`
- `RES` = `PSX Assets\Textures\Textures\Result\` (256 px photographic; the other four folders there are stylised palette variants of the same 40)
- `ROADS` = `PSX Assets\PSX Racing\Roads\Roads\Textures\`

### 4.2 Models, in priority order

| P | Model / kit | Spec from the real road | Replaces (code) |
|---|---|---|---|
| 1 | **Road surface rebuild + marking decals** | Per-segment marking rules:<br>- Parkway: double yellow only;<br>- tunnels: plus white edge;<br>- state roads: plus white edge;<br>- Chimney Rock: none, or faded.<br>Markings as separate decal strips with worn masks. Ragged unpaved Parkway edge. Viaduct carriageway about 10.4 m between curbs. | `City.cs:1033-1082`, `PSXRacingBuilder.cs:1849-1972` |
| 1 | **Bridge railing kit** (viaduct + Grandfather bridges) | Concrete curb about 0.3 m with a 0.5 m ledge; square metal posts at about 2 m; two rails at about 0.6 m and 1.0 m; end treatments into stone approach walls. 7 viaduct piers, tinted box girder with segment joints. | `Stage.cs:2498`, `PSXRacingBuilder.cs:4811` |
| 1 | **Parkway timber guardrail kit** | Rail about 0.20 x 0.25 m in about 3 m lengths, butt-jointed on short posts about 0.25 m square, top about 0.7 m, 1.5-2 m off the pavement. Two post variants: concrete and timber. Buried/flared ends. Fall-warrant runs on the Parkway become this instead of stone. | `PSXRacingBuilder.cs:363`, `Stage.cs:6128` |
| 1 | **Stone masonry kit** | Low guard wall 0.5-0.7 m (random rubble, caps), culvert headwall with pipe, overlook wall, bridge abutment facing. Modelled stones for LOD0, a normal-mapped slab for LOD1+. Use it at viaduct approaches, overlooks and headwalls, not on every warranted run. | `Stage.cs:5689-5721` |
| 2 | **Tunnel kit** | Horseshoe bore (Little Switzerland: 5.84 m crown, 4.34 m at the edge line) with sculpted rock interior and concrete-lined sections. Voussoir-arch masonry portal (east) or rock-face portal, with wing walls. Wall reflector panels every 10-15 m. Brown name/clearance sign. Unlit. | `Stage.cs:6884-7080` |
| 2 | **Mesh tree set with LODs + impostors** (replaces X-cards) | About 8 species groups: red spruce; Fraser fir, live and **dead snag**; yellow birch/beech; sugar/red maple; red/chestnut oak; tulip poplar; white pine; hemlock, live and dead. 4 seasonal leaf states (spring, summer, fall, winter-bare), with spruce, fir and pine always green. LOD0-LOD2 meshes plus an octahedral impostor beyond about 60-80 m. **Keep at least the current near density** (7.5 m grid to 45 m, ≥2,200 trees/km). Species by **absolute elevation** (F9). | `Stage.cs:7143-7176`, `:7340-7774` |
| 2 | **Shrub and ground-cover set** | Catawba and rosebay rhododendron, mountain laurel (evergreen all year); blueberry/huckleberry heath (red in fall); blackberry; hay-scented fern (buff in fall); goldenrod/tall grass; mown-turf grass cards; dead logs and stumps (`NAT` has `tree_log_1`, `tree_stump_1` meshes). | none (new); `Stage.cs:253` |
| 2 | **W-beam kit** (galvanised + weathering steel) | Keep the current real section (`Stage.cs:5730-5733`); add blockouts, splice bolts and end terminals. Weathering-steel variant for NC 215. | `PSXRacingBuilder.cs:997-998` |
| 3 | **Roadside furniture** | Parkway mileposts (white concrete post, vertical black numerals, every mile at the real MP). Brown routed-wood signs (overlooks, tunnels, junctions; no emblems). Yellow warning diamonds and chevrons on NC 226A and Chimney Rock hairpins. Stop signs at Parkway access junctions. Split-rail fences at Linn Cove and Rough Ridge. Rustic post-and-rail for Chimney Rock. | none (new) |
| 3 | **Rock kit** | Modular cut faces (blasted, with drill half-casts on state roads), outcrops (Grandfather crags, NC 215 slabs, Chimney Rock cliff), **Linn Cove boulder field** under the viaduct, Rocky Broad river boulders, scree. | `Stage.cs:6347` |
| 3 | **Drainage set** | Stone-lined or paved ditches, concrete flumes, drop-inlet grates, culvert pipes with rubble headwalls. | none (new) |
| 4 | **Overlooks / pull-offs** | Paved pads with stone wall or timber rail, brown name sign, parking stripes absent or faint. At Grandfather: Rough Ridge parking, Yonahlossee Overlook, the Linn Cove visitor centre lot, Beacon Heights (mileposts approximate, verify). | memory backlog |
| 4 | **Village kits** | Blowing Rock Main St (2-3 storey shops/inns, sidewalks, curbs, street trees, poles); US 321 as a multi-lane road; Little Switzerland inns; Chimney Rock Main St; NC 226A rural houses, mailboxes, driveways. Utility poles and lines on state roads. | `PSXRacingBuilder.cs:509` |
| 5 | **Far vista** | Far forest (exists on PC) moved to impostors; seasonal far-canopy colour map; DEM ridge silhouette rings to 30+ km with aerial perspective. The haze strength is the owner's A/B, and there is no fog outside Fog weather. Salvage idea: the shelved `StageRidges.cs` from branch `corridor-forest`. | `Stage.cs:7227-7248`, `:7576-7681` |

### 4.3 Textures: pack source, size, tiling, upscale verdict

**Viability key:**
- **OK**: survives the stated upscale with generated normal, roughness, AO and height.
- **Marginal**: usable as a base, but needs hand-painted or procedural detail on top.
- **No**: use only as colour reference; new art is needed.

| P | Texture (proposed name) | What it shows | Size / tiling | Pack source(s) to upscale | Source res | Viability |
|---|---|---|---|---|---|---|
| 1 | `stage_asphalt_parkway` + 3 variants (aged default, patched, fresh-black overlay) | Aged mid-grey asphalt, exposed light aggregate, polished wheel paths, longitudinal cracks; edge wear done by decal | 2048² per 4x4 m, U+V; plus a 512² micro-normal per 0.25 m | `T2\asphalt_old_pt_1.png`, `_1_1`, `_1_2` + `512\Normal Maps` equivalents; tone check `ROADS\T (1).jpg` | 512 | **Marginal at 4x**: the aggregate reads as uniform noise. Upscale for macro albedo and roughness, and generate real aggregate in the micro-normal. |
| 1 | `stage_asphalt_state` (NC roads, US 321) | Darker, smoother asphalt with tar-snake crack seal and patch seams | 2048² per 4x4 m, U+V | `T2\asphalt_old_pt_1_2.png` | 512 | Marginal at 4x (same reason) |
| 1 | `deck_concrete_tinted` | Warm-grey iron-oxide concrete, broom finish, sealer blotches | 2048² per 4x4 m, U+V; segment joints as decals every 1.5-2.7 m | `T2\concrete_pt_1.png`, `concrete_pt_6.png`; current `Art/City/Pack/concrete_pt_1_city.png` | 512 | **OK at 4x** |
| 1 | `paint_lines_worn` | Yellow and white line masks: wear, chips, faded spots; per-road brightness | 1024x128, V tiles along the road | none: procedural | n/a | **No pack source** |
| 1 | `road_decals_atlas` | Crack networks, tar snakes, rectangular patches, oil drip band, tyre marks, leaf drifts at edges, drop-inlet grate, manhole, puddle masks | 4096² atlas (no tiling) | Leaves: `RES\texture_6.png` (256). Grate: `T2\metal_floor_pt_1.png` (512). Patch tone: `T2\asphalt_old_pt_1_1.png`. Rest is new. | 256-512 | Marginal (grate, patch); **No** (cracks, tar snakes, leaves at 4K) |
| 1 | `timber_guardrail_trim` | Weathered silver-grey rail faces with checks and splits, bolt heads and washers; end grain; dark or concrete post faces; steel backing edge | 2048x1024 trim at about 1,024 px/m, U tiles along the rail | Silver-grey: `T2\wooden_board_pt_1.png`. Grain: `T2\wood_pt_1.png`. End grain: `NAT\log_ends_1.png` (600x300). Concrete posts: `T2\old_concrete_pt_1.png`. | 512 / 600 | **OK at 4x** for colour; checks and splits need painted height |
| 1 | `bridge_rail_trim` | Galvanised/aluminium tube rails, square posts, base plates and bolts; concrete curb face and top | 1024x512 trim | `T2\metal_panels_pt_1.png` (paint/metal tone), `T2\concrete_pt_1.png`; zinc spangle procedural | 512 | Marginal (metal); OK (concrete) |
| 1 | `stone_wall_rubble` + moss variant | Random-rubble fieldstone, grey-brown with lichen, deep raked joints | 2048² per 2x2 m (1,024 px/m), U+V, course continuity checked; plus a 2048x512 capstone trim | **`T2\stone_wall_pt_1.png`** (irregular multi-colour stone, the best match). Moss variant: `RES\texture_26.png` (256). Retire `ROADS\T (3).jpg` (256, regular ashlar). | 512 | **OK at 4x** for albedo; take stone relief from a sculpted height, not from albedo |
| 2 | `rock_cut_gneiss` + regional tints (Grandfather buff-grey, Mitchell dark, Spruce Pine light with mica, Chimney pink-grey) | Blasted and weathered rock, lichen, seep stains | 4096² triplanar per 6 m, plus a 1024² detail per 1 m | **`NAT\stone_2.png`** (grey veined), `NAT\stone_1.png` (brown); `T2\rock_pt_2.png` (current), `rock_pt_4.png` (layered), `rock_pt_1.png` (dark) | **1024** / 512 | **OK at 4x** (`NAT` to 4096; `T2` to 2048) |
| 2 | `boulder_atlas` | Lichen boulders for the Linn Cove field and river rocks | 2048² unique bakes per boulder set | `VEG\Rock.png`, `Rock_01.png` (+`Rock_01_N.png`), `Rock_02.png`; meshes `PSX Nature\stone_1..5.glb` | 512 | **OK at 4x** |
| 2 | `tunnel_rock_bore` | Dark wet blasted rock, soot band, drill traces | 4096² triplanar | `NAT\stone_2.png` darkened; `T2\rock_pt_1_1.png` | 1024 / 512 | OK |
| 2 | `tunnel_liner_concrete` | Board-formed arch concrete with ribs, efflorescence and soot | 2048² per 4 m, U+V | `T2\old_concrete_wall_pt_1..3.png`, `concrete_wall_pt_10.png` / `_11.png` (check for formwork lines) | 512 | OK at 4x (add form-line height if missing) |
| 2 | `portal_voussoir_trim` | Dressed arch-ring stones, keystone, quoins | 2048x1024 trim | Cut from `T2\stone_wall_pt_2.png` (regular ashlar blocks) | 512 | Marginal: needs relayout |
| 2 | `viaduct_segment_concrete` | Box-girder webs and soffit, segment joints, weathering streaks, iron-oxide tint; piers | 2048² per 4 m | `T2\concrete_wall_pt_2.png` (stained), `old_concrete_wall_pt_1..3.png` | 512 | OK at 4x (joints by decal) |
| 2 | `turf_mown` + 3 variants (summer, fall straw, winter dormant) | Mown grass shoulder | 2048² per 4 m, U+V; plus a 1024² grass-card atlas | `T2\grass_pt_3.png`, `grass_pt_4.png`, `grass_dead_pt_1.png`, `grass_dead_pt_2.png`. Cards: `NAT\grass_1..4.png` (`grass_3` is 435x447), `VEG\Grass_P.png` | 512 (cards 256-447) | **OK at 4x** (ground); cards marginal |
| 2 | `verge_dirt_gravel` | Worn pull-off band, dirt with gravel | 2048² per 3 m | `T2\dirt_pt_5.png` (current), `dirt_pt_1.png`, `dirt_pt_6.png`, `dirt_pt_9.png`; `RES\texture_8.png`/`_9` | 512 | OK at 4x |
| 2 | `forest_floor_leaflitter` (fall fresh, winter wet-brown) | Leaf litter, twigs, wet leaves | 2048² per 3 m, U+V | `RES\texture_6.png` (256; the only photographic leaf litter in the packs); twigs `T2\dirt_pt_8.png` (512) | 256 | **No at 8x**: needs new art |
| 2 | `forest_floor_summer` | Moss, ferns, humus | 2048² per 3 m | `T3\grass_1.png` (256, mossy), `T2\dirt_pt_3.png` | 256 / 512 | Marginal |
| 2 | `wbeam_galv_trim` / `wbeam_weathering_trim` | Zinc-grey and rust-brown W-beam, posts, blockouts, terminals | 2048x512 trims | Galvanised: current `T2\corrugated_steel_pt_3.png`, used for metal tone only (its corrugations are sheet ripples, so the W-profile stays geometry). Weathering: **`T2\rusty_metal_pt_1.png`**, `_2`, `_5` | 512 | Marginal (galvanised); **OK at 4x** (weathering) |
| 3 | `parkway_sign_wood` | Brown-stained routed boards; white letters generated from a neutral font; no emblems | 1024x512 trim | `T2\wood_pt_2.png`, `wood_pt_3.png` | 512 | OK at 2-4x |
| 3 | `milepost_concrete` | White-painted concrete post, black numerals | 512x1024 | `T2\painted_concrete_pt_1.png`, `concrete_pt_3.png` | 512 | OK |
| 3 | `split_rail_trim` | Rough split rails, silver-grey, bark remnants | 1024x512 trim | `NAT\tree_bark_6.png` (grey bark, 256), `T2\wood_pt_1.png`, `NAT\log_ends_1.png` | 256-600 | Marginal |
| 3 | `utility_pole` | Creosoted pine pole, crossarm | 512x2048 | `T2\wood_pt_4.png` (current post texture) | 512 | OK |
| 3 | `snow_verge` / `snow_bank` / `rime` | Ploughed snow, slush, rime on spruce | 2048² per 4 m | `T3\snow_1.png`, `snow_2.png`, `snow_3.png`, `snow_melting_1.png` (256); `PSX Assets\SnowTextures\SnowTextures\snowy.png` (512 atlas) | 256 | **No at 8x**: new art (current `RoadCover.png` uses these) |
| 3 | `moss_lichen_decals` | Moss patches, crustose lichen | 1024² decals | `RES\texture_26.png`, `texture_28.png`; `T3\stone_3_1.png` | 256 | OK at 4x (decals only) |
| 4 | `bark_*` (smooth grey birch/beech/maple; furrowed oak/poplar; flaky spruce; silver dead-fir snag) | Tree trunks | 2048x2048 tiling (V along the trunk) | `VEG\BarkDecidious.png` (+`_N`), `VEG\BarkPine.png`; `NAT\tree_bark_5.png`, `tree_bark_6.png`, `pine_bark_1.png`, `pine_bark_2.png`, `pine_bark_2_dead.png` | 256 | **No for hero trunks** (8x); OK to 1024 for mid trees |
| 4 | `leaf_atlas_*` per species group x season | Broadleaf branch cards (green, gold, orange, scarlet, russet), bare winter twigs | 2048² atlases | `NAT\branch_1.png`, `branch_2.png`, `branch_3.png` (green and autumn); `VEG\Branch*.png` (+`_N`); bare: `NAT\fern_3.png` (725x670), `VEG\Branch_T*.png` | 256-725 | Marginal: 4x to 1024 cells only; species-correct leaves are new |
| 4 | `needle_atlas_spruce_fir` | Red spruce and Fraser fir branch cards | 2048² | `NAT\pine_branch_1..3.png` (256x384) | 256 | Marginal (4x to 1024x1536) |
| 4 | `shrub_rhododendron_laurel` | Evergreen leathery leaves, clusters | 2048² atlas | `NAT\fern_1.png` (evergreen shrub card, 256) | 256 | **No**: new art |
| 4 | `shrub_heath_fall` | Blueberry/sumac red shrub | 1024² | `NAT\fern_2.png` (524x444, red-leaved shrub) | 524 | OK at 2-4x |
| 4 | `tallgrass_fern_cards` | Goldenrod, tall grass, hay-scented fern | 1024² | `NAT\grass_3.png` (435x447), `VEG\Grass_P.png`; ferns new | 256-447 | Marginal |
| 5 | `impostor_*` | Octahedral impostors of each tree mesh | 2048-4096 per tree | Baked from the meshes | n/a | n/a |
| 5 | `far_canopy_<season>` | Aerial forest-canopy colour (replaces the code-drawn `FallMottle`) | 4096² per about 200 m (20 px/m), or a per-stage baked colour map | Baked top-down from the impostor forest | n/a | **No pack source** |

### 4.4 What has no usable pack source

These need new authoring: photo-sourced, procedural or baked.

1. **Road paint masks and the road decal atlas**: cracks, tar snakes, patches, oil, tyre polish, leaf drifts, puddle and drying masks.
2. **Real asphalt aggregate at 2048 and above.** `asphalt_old_pt_*` only works as macro colour.
3. **Leaf litter** at ground-tile resolution. The only source is `RES\texture_6.png` at 256.
4. **Rhododendron and laurel foliage**, and **species-correct hardwood leaves** in fall colourways.
5. **Hero tree bark at 2048.** All bark sources are 256.
6. **Snow, slush and rime** at 2048.
7. **Galvanised zinc spangle** for W-beam and bridge rail. Pack steel is corrugated sheet or rust.
8. **Brown routed sign faces and milepost numerals**: procedural from a licensed neutral font.
9. **Far-canopy aerial colour and impostor atlases**: baked from the new tree meshes.
10. **Tunnel wall reflector panels** and similar small decals.

### 4.5 Suggested first batch

These are the textures that move the most pixels:
1. road asphalt + paint decals;
2. `stone_wall_rubble`;
3. `timber_guardrail_trim`;
4. `bridge_rail_trim`;
5. `turf_mown`;
6. `rock_cut_gneiss`;
7. `forest_floor_leaflitter`.

Test them on one 500 m piece of BlueRidge: the viaduct's north end to the overlook at 36.0962,-81.8111. Compare against the Street View views listed in 2.15, from the same camera, in fall and winter dresses.

---

## 5. What the renderer and assets must change for PS3/PS4 fidelity (short)

What the code shows today, and what it would need. A URP/HDRP comparison tool landed on this branch today (`Editor/Dev/PipelineCompare.cs`). Its URP renders on the *same* textures (`PSXRelease\compare\road_pair.jpg`, `viaduct_pair.jpg`) show that per-pixel lighting alone does not fix the gaps in this report: edge lines, gravel verge, billboard trees and a flat far valley all remain. Content and textures have to change as well as the pipeline.

1. **Texture pipeline.**
   - Today: every texture under `Art/` is capped at 256 px, point-filtered, with no mipmaps and uncompressed (`PSXRacingBuilder.cs:1056-1086`; the 256 "PS1 texture-page ceiling" for road profiles at `PSXRacingBuilder.City.cs:881-887`). Road textures are generated noise (`City.cs:1053`).
   - Needed: lift the cap per tier; use **BC7/BC5 compression, mipmaps, trilinear with 8-16x anisotropic filtering, and mip streaming**. Keep Classic as the Android/web tier.
2. **Shading.**
   - Today: `PSX/Lit` lights per **vertex** (Lambert plus hemispheric ambient, `PSXLit.shader:1269-1287`), with no normal, roughness, AO or height maps. The look also rests on a low-line framebuffer and a 5-6-bit dither (memory `psx-racing-renderer-look`).
   - Needed: **per-pixel PBR**, meaning a URP/HDRP Lit-class shader or a custom one with normal, ORM and height. Add detail maps, triplanar mapping for rock, parallax or tessellation for stone walls and cuts, and a deferred decal system for road wear. Make the low-res framebuffer and dither a "Retro" option only.
3. **Shadows and GI.**
   - Today: only cars get a sun shadow map (`SunShadows.Cars.cs`, memory `psx-racing-gt4-trial-mountain-study`). The world has per-vertex baked sky visibility (`PSXRacingBuilder.BakedLight.cs`) and a runtime leaf dapple.
   - Needed:
     - **cascaded shadow maps for the environment** (trees, rails, walls, portals, viaduct);
     - **baked lightmaps or probe GI** plus SSAO/GTAO;
     - reflection probes along the road (the wet-road SSR exists);
     - **auto-exposure** for tunnels;
     - optional volumetric sun shafts. Owner rule: no fog outside Fog weather.
4. **Foliage.**
   - Today: 2-quad X-billboards from 128 px cells in one 512 atlas (`Stage.cs:7143-7176`, `:7749-7774`), plus a level crown card per tree on the PC (`:7700-7712`).
   - Needed:
     - mesh trees with LODs and **octahedral impostors**;
     - GPU-instanced indirect drawing;
     - wind animation;
     - alpha-to-coverage with TAA;
     - a shrub and grass layer.
   - The trunk-collider table (`TreeTrunks`) and the near-density tests carry over: keep ≥2,200 trees/km and the 7.5 m near grid.
5. **Terrain.**
   - Today: near ground is a 12 m lattice and far ground a 60 m lattice from SRTM, which reads canopy tops with up to +33 m bias (memory). One tiled ground texture is used, with a code-drawn mottle beyond 150 m (`Stage.cs:263-264`, `:7227-7248`).
   - Needed:
     - **1-2 m terrain near the road** from USGS 3DEP 1 m lidar (public domain);
     - splat or height-blended layers (turf, dirt, leaf litter, rock, snow);
     - hand-placed rock meshes over cut faces;
     - DEM ridge rings to 30+ km for overlook vistas.
6. **Sky and post.**
   - Today: 1024x512 LDR panoramas (memory `psx-racing-renderer-look`).
   - Needed: **HDR sky (8K)**, physically based sun and sky intensity, TAA, bloom, tonemapping. Khronos PBR Neutral is already used.
7. **Content production.**
   - Today: the stage builder generates every barrier, wall and tunnel from code (`Stage.cs`).
   - Needed: the builder places **authored modular kits** along the spline (guardrail, rail, wall, portal, sign, milepost). It should keep its warrant logic (decks, critical fills, tunnel mouths) and choose the **kit per road and per segment** (Parkway vs state road vs park road).

---

## 6. Open questions and checks before modelling

- **Timber-guardrail post material per site** (precast concrete vs timber) on the Grandfather section. Check Street View at the overlooks.
- **NC 215 edge lines** along the whole route (none seen at 2 points).
- **NC 128 barriers** at the switchback fills. None was seen at 3 points; the game uses stone.
- **Chimney Rock park road after Helene** (markings, rails, surface), and which period to model.
- **Pier positions** for the Linn Cove Viaduct (7 piers): take them from the OSM bridge way, or from public drone footage for appearance only.
- **The US 321 leg of the Blowing Rock loop**: either model it as a multi-lane road with its frontage, or re-route the loop. This is the owner's call.
- **Milepost positions**: the Parkway mileposts sit at whole miles. For BlueRidge that is about MP 302-305, so 3-4 posts; take positions from OSM if tagged.
- **Elevation check**: NPS gives the viaduct at "about 4,100 feet" (1,250 m); the bake reads 1,322-1,335 m. This is consistent with the known SRTM canopy bias. It does not affect the look, but it matters for any absolute-elevation species rule (F9). Use 3DEP or the bake minus the canopy bias.

---

## Sources

- **Owner clips:** `C:\Users\mcgee\Videos\2026-11-02 20-13-15.mp4`, `20-14-26`, `20-15-03`, `20-18-49`, `20-21-14`. Reference only. Contact sheets and detail frames are in session scratchpad `roadvid/`.
- **Google Street View and public photospheres:** the links and panorama IDs in 2.15, viewed 2026-11-02.
- **NPS:**
  - Blue Ridge Parkway tunnel heights: https://nps.gov/blri/planyourvisit/tunnel-heights.htm
  - History of the Linn Cove Viaduct: https://www.nps.gov/blri/learn/historyculture/linn-cove-viaduct.htm
  - Blue Ridge Parkway NHL executive summary (2024): https://home.nps.gov/subjects/nationalhistoriclandmarks/upload/Blue-Ridge-Parkway-NHL_exec-summ_2024-8_508.pdf
- **NPS, via search summaries only (pages not read in full):**
  - Parkway "marked every mile by concrete mileposts": https://www.nps.gov/articles/000/top-10-things-to-know-before-you-go.htm
  - Tunnel portal masonry "added later, mostly in the 1950s and 1960s" (*Highways in Harmony*): https://www.nps.gov/parkhistory/online_books/hih/blue_ridge/blue_ridge8.htm
  - Brown as the NPS guide-sign background (NPS Sign Manual 1988): https://www.npshistory.com/publications/management/signs/nps-sign-manual-1988.pdf
- **Parkway FAQ (no edge lines):** https://www.blueridgeparkway.org/?p=3848
- **Roadway width and turf shoulders:** Documenting the American South, https://docsouth.unc.edu/blueridgeparkway/content/7793/ (per search summary).
- **Shoulder ruts / "earthen and sod shoulders":** https://casite-498466.cloudaccess.net/archives/item/20392-repairs-underway-on-the-blue-ridge-parkway (per search summary).
- **Viaduct cross-section:** FIU bridge database, https://utcdb.fiu.edu/project/1983-linn-cove-viaduct/; Structurae, https://structurae.net/en/structures/linn-cove-viaduct (per search summary).
- **NCDOT weathering-steel beam guardrail provision:** https://connect.ncdot.gov/resources/Specifications/2024DivOpsMaintProvisionDocs/SPI%2008-33.docx (per search summary).
- **NPS Park Practice guardrail sheet (1957):** https://npshistory.com/newsletters/park_practice/design/protection/P-2311.pdf. Scanned and not machine-readable; cited via the search summary only.
- **General Southern Appalachian forest zonation:** Schafale, *Guide to the Natural Communities of North Carolina* (4th approximation, 2012). General knowledge [Gen]; verify species per site.
- **Project:**
  - `Assets/PSXRacing/Editor/PSXRacingBuilder.cs`, `.Stage.cs`, `.City.cs`, `.Season.cs`
  - `Assets/PSXRacing/Shaders/PSXLit.shader`
  - `Assets/PSXRacing/Scripts/TrackCatalog.cs`
  - `Docs/shots/weather/wx_blueridge_*.jpg`
  - Pack folders under `OneDrive\...\PSX Assets\` (read for this study; nothing copied).
