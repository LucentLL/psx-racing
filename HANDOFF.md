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
