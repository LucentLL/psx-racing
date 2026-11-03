# Stage accuracy pass: report

Branch `stage-accuracy`, from `pc-max-build` @12cd6345, 2026-11-02. It fixes the factual items F1, F2, F4, F5, F6, F7 and F9 of `Docs/reference/blue-ridge-roads.md`. F3, the viaduct's width, is not done (see "Not done").

## What changed

| # | Fix | Commit | Venues |
|---|---|---|---|
| 1 | **Road paint per real road.** No white edge lines on the Blue Ridge Parkway, except over its tunnel spans. NC 215 and the Chimney Rock park road have none either. The double yellow stays on every road. NC 128, NC 226, NC 226A, US 221/321 and Main St keep their edge lines. | da226508 | BlueRidge; the Parkway legs of BlowingRock and LittleSwitzerland (the Little Switzerland Tunnel stays striped); BeechGap; ChimneyRock |
| 2 | **No red/white reflector posts.** The real roads have none. | 0581529c | BlueRidge, MtMitchell |
| 3 | **Barriers per real road.** The Parkway gets a steel-backed timber guardrail. NC 215 gets weathering-steel W-beam. Chimney Rock gets rustic log post-and-rail. | 3b4f940f | BlueRidge, the loops' Parkway legs, BeechGap, ChimneyRock |
| 4 | **Parkway bridges.** A concrete curb with a two-rail metal railing replaces the dry-stone parapet. | 38d614d1 | BlueRidge: Linn Cove Viaduct and the 7 other Grandfather bridges |
| 5 | **Spruce-fir by real elevation** (metres above sea level). | 3ee4ca50 | all mountain stages |

### Details

**Fix 1: edge lines.**
- `Theme.edgeLines`, `Theme.parkway` and `Theme.parkwaySpans` hold each road's paint rules. `StageEdgeLinesAt(m)` applies them: a tunnel span is striped, the Parkway is not, and everything else follows the theme.
- The loops' Parkway legs were matched waypoint by waypoint against the bake's own Overpass ways (`tools/roads/cache`):
  - Blowing Rock: 0-4204 m (the Parkway and the NPS Cone access) and 9632 m to the line.
  - Little Switzerland: 0-4804 m, including the tunnel at 3992-4180 m, and 9672 m to the line.
- `BuildRoad` splits the ribbon into submeshes by paint as well as by deck.
- Stretches without edge lines use `city_road_track_<w>_noedge_<surface>.png`.
- `LaneAudit` accepts a texture with no edge pair only when its name carries the `_noedge` marker.

**Fix 2: reflector posts.** `postEvery = 0` on BlueRidge and MtMitchell. The W-beam venues never drew these posts.

**Fix 3: barriers.**
- Each run keeps its warrant, its line, its collider solid, its flares and its buried terminals. Only the drawing changes, chord by chord, through `ChordKit`:

  | Kit | Where | Build |
  |---|---|---|
  | `ParkwayTimber` | the Parkway | Rail 0.25 x 0.15 m with its top about 0.70 m over the tarmac. Pack texture `wooden_board_pt_1`, with a new board at each post. A steel backing plate. Posts 0.25 m square, dark timber (`wood_pt_4`), one per station (about 4 m). |
  | `Weathering` | NC 215 | The same W-beam section in `rusty_metal_pt_1`, on weathering-steel posts. |
  | `Rustic` | Chimney Rock | Two hexagonal log rails (PSX Nature `tree_bark_6`) at about 0.48 m and 0.88 m over the tarmac, on 0.18 m split posts. |
  | `Galvanised` | NC 226, NC 226A, US 221/321 | As before. |

- MtMitchell keeps its stone.
- A closed kit is capped where it starts or stops, and is carried 0.1 m past the run's ends, as the beam is.

**Fix 4: bridge railing.**
- Applies on a Parkway venue's decks.
- Curb: concrete, 0.40 m wide, 0.33 m over the deck tarmac.
- Rails: two galvanised rails, 0.15 x 0.08 m, at about 0.41-0.56 m and 0.78-0.93 m over the tarmac. They are set back 0.12 m on the curb's ledge.
- Posts: 0.10 m steel posts every 2 m, standing on the curb.
- The approaches have the timber guardrail.

**Fix 5: species by elevation.**
- `ConiferShareAt(stageBaseM + groundY)` uses these breakpoints:

  | Elevation | Spruce-fir share |
  |---|---|
  | up to 1,000 m | 3% |
  | 1,400 m | 10% |
  | 1,700 m | 65% |
  | from 1,900 m | 85% |

- The bare cell stays at 5%; on the summit it reads as the dead fir snags.
- Each tree takes the same random draws as before, so the forest density is unchanged.
- Old and new spruce-fir share at the road, by venue:

  | Venue | Old | New |
  |---|---|---|
  | MtMitchell | 8-36% | 42-85% |
  | BeechGap | 8-36% | 3-51% |
  | BlueRidge | 8-36% | 8-9% |
  | BlowingRock | 8-36% | 4-7% |
  | LittleSwitzerland | 9-36% | 3-4% |
  | SwissNC226A | 8-36% | 3-4% |
  | GillespieGap | 8-36% | 3% |
  | ChimneyRock | 8-36% | 3% |

## Verification status (stopped at the token budget, 2026-11-02 ~22:40)

- **Compiles:** `tools/typecheck.ps1` passed (`TYPECHECK OK`) after every commit, against PSXCity's references.
- **"Before" evidence:**
  - Built in sandbox PSXCity: a /MIR of this branch at 12cd6345, then `Invoke-SceneBuild` with `PSX_PC_CONTENT=1` (BUILD OK at 22:34, 52 min).
  - Shots, written to `C:\Users\mcgee\PSXRelease\accuracy\before\`:
    - `psx_spot_<venue>_<wp>_<name>_{chase,above}.png`, taken at the study's 2.15 checkpoints. Each lat/lon was mapped to its nearest waypoint (all within 0.5-7.5 m):
      - BlueRidge: 1171 viaduct, 1073 overlook, 1225 rubble wall, 412 small bridge, 1500 forest, 76 high open
      - MtMitchell: 925, 1399, 475
      - BeechGap: 700, 74
      - BlowingRock: 500, 1224
      - LittleSwitzerland: 600, 990 portal, 1020 tunnel, 1624
      - ChimneyRock: 275, 551, 828
      - SwissNC226A: 752
      - GillespieGap: 825
    - `psx_rail_*.png`: RailShots on BlueRidge, BeechGap, ChimneyRock, BlowingRock, LittleSwitzerland and MtMitchell.
    - `psx_forest_*.png`: ForestShots on MtMitchell, BlueRidge and ChimneyRock.
  - The build log is `before\build_log.txt`.
- **NOT YET VERIFIED:**
  - No "after" scene build has run, and there are no after shots.
  - None of these gates has run on the new code: FoliageAudit, LaneAudit, TrackObstacleAudit (ghost barriers on the new kits), the self-test, or race-play-check.
  - Treat commits da226508..3ee4ca50 as unverified until the follow-up below passes.

### Follow-up to finish verification

The session scratchpad holds the scripts (`C:\Users\mcgee\AppData\Local\Temp\claude\C--Users-mcgee-OneDrive-Documents-Game-Development-PSX-Assets-PSX-Racing\336a3f43-26c5-43d3-90a6-412286e98c7c\scratchpad\`):

1. **Optional baseline.** On the still-built before scenes, run `acc_audit.ps1 -Tag before -Steps obstacle,race`. This gives the baseline ghost-barrier and drive-check numbers.
2. **After build and shots.** Run `acc_job.ps1 -Sync code -Tag after`. It mirrors this branch's Scripts/Editor/Shaders, copies Art/Guardrail, runs the scene build with PC content, then takes the same shots into `...\accuracy\after\`.
3. **Gates.** Run `acc_audit.ps1 -Tag after`. It runs the obstacle audit, lane audit, FoliageAudit, the self-test, and race-play-check on BlueRidge, ChimneyRock and BeechGap.
4. **Pairs.** Run `acc_pairs.ps1` to write before/after side-by-side JPGs into `...\accuracy\pairs\`.

Each script takes and releases `AGENT_LOCK.txt` itself and refuses a sandbox that is locked or has a Unity process running.

**What to look at first:**
- **Ghost barriers on the new kits.**
  - The timber rail, the lower log and the bridge curb were sized so they cover the ghost audit's 0.6 m sample. Their drawn top should leave the 1.7 m collider within `GhostOver`.
  - The 2-rail and curb kits are open between parts, which is new to that audit.
- **LaneAudit.** On BlueRidge, slot 0 is now a `_noedge` texture.
- **Self-test.** Check any deck-parapet or wall-material assumption, now that BlueRidge is `guardrail = true`.
- **Shots.** Check the cap and transition geometry at the kit changes: deck ends, and the loops' Parkway/state-road joins.

## Not done, and exceptions

- **F3, the viaduct's width between curbs** (10.4 m real vs about 8.7 m now). Not attempted. One `RoadWidth` drives the ribbon, the lane ladder, the lane audit, the walled shoulder, the wall offsets (`rsWallE`) and the deck builder (`PSXRacingBuilder.cs` around `:4762`). A per-station width ripples through all of them, as the brief anticipated.
- **The loops' own Parkway bridge decks** (Blowing Rock 9688-9808 m, Little Switzerland 9676-9792 m) keep their galvanised W-beam. They were not surveyed; only the Grandfather bridges were.
- **Viaduct approach stone walls** (study 2.5/2.10) are not modelled. The approaches are timber guardrail.
- **Chimney Rock centre line.** It keeps its double yellow, per the brief ("Keep the double yellow centre"). The study saw no markings at all there in Aug 2024 (pre-Helene); that is the owner's call.
- **Parkway access ramps** (Blowing Rock's trunk_link, Little Switzerland's secondary_link) count as state road: edge lines and W-beam. Their real paint is unverified.
- **Mount Mitchell (NC 128)** keeps its stone walls; the study lists NC 128's barriers as an open question.
- **Species elevation** uses the SRTM datum without a canopy-bias correction. The breakpoints are coarser than the bias.
