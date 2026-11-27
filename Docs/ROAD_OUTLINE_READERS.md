# Readers of the junction trims and fans, and fixes keyed to the map export
Written by c7roads round 5, stage 0 e (2026-11-26) from a grep of branch charlotte-c7roads. Pattern: `trims.`, `Trims `, `TrimAt(`, `.Internal(`, `BranchAt(`, `FanRingOf(`, `FanRingPoints(`, `FanKerb`, `PaveEdgeM`.
The outline (Docs/ROAD_RULES.md section 2) rewrites the mesh cut of every arm. Each reader below is marked:
- **control trim**: gameplay control (stop lines, spawns, signal groups, routing, checkpoints, the line model's own  split hook). It keeps TODAY's per-arm trim (a separate `controlTrim`) until it is ported on purpose.
- **outline**: it places or tests things against the DRAWN pavement, so it must read the new outline.
- **generator**: a CityMeshes partial - the mesh itself; replaced or ported stage by stage.

## Runtime: 67 files
| file | refs | what it reads | mark |
|---|---:|---|---|
| Scripts/Checkpoints/CheckpointRace.cs | 2 | PaveEdgeM | control trim |
| Scripts/Checkpoints/RacingTurns.cs | 13 | FanKerb, PaveEdgeM, TrimAt, Trims | outline: the rivals' line must stay on the NEW drawn pavement (OnPavement reads trims and the fan's kerb list) |
| Scripts/Checkpoints/RoutedPath.cs | 4 | PaveEdgeM | control trim |
| Scripts/Checkpoints/RulerHold.cs | 1 | PaveEdgeM | control trim |
| Scripts/Checkpoints/TurnMapDump.cs | 3 | FanKerb, PaveEdgeM | control trim |
| Scripts/City/CityCulverts.cs | 4 | Trims | outline (check) |
| Scripts/City/CityElevation.cs | 12 | PaveEdgeM, TrimAt, Trims | outline: junction heights: the outline's one height function |
| Scripts/City/CityHouses.cs | 12 | TrimAt, Trims, trims | outline: houses / driveways kept off the drawn pavement |
| Scripts/City/CityMap.cs | 1 | PaveEdgeM | control trim |
| Scripts/City/CityMeshes.AuditView.cs | 12 | Trims, trims | generator |
| Scripts/City/CityMeshes.Bays.cs | 4 | Trims, trims | generator |
| Scripts/City/CityMeshes.Bridges.cs | 3 | Trims | generator |
| Scripts/City/CityMeshes.Concrete.cs | 11 | Trims | generator |
| Scripts/City/CityMeshes.CurbMedian.cs | 1 | Trims | generator |
| Scripts/City/CityMeshes.Curbs2.cs | 8 | Trims, trims | generator |
| Scripts/City/CityMeshes.Curbs3.cs | 3 | Trims | generator |
| Scripts/City/CityMeshes.Decks.cs | 1 | Trims | generator |
| Scripts/City/CityMeshes.Dividers.cs | 14 | Trims, trims | generator |
| Scripts/City/CityMeshes.FanDrape.cs | 8 | Trims, trims | generator |
| Scripts/City/CityMeshes.GroundFloors.cs | 3 | Trims | generator |
| Scripts/City/CityMeshes.Hitch.cs | 5 | Trims, trims | generator |
| Scripts/City/CityMeshes.HouseWalls.cs | 1 | Trims | generator |
| Scripts/City/CityMeshes.Hydro.cs | 1 | Trims | generator |
| Scripts/City/CityMeshes.IslandSolids.cs | 15 | PaveEdgeM, Trims, trims | generator |
| Scripts/City/CityMeshes.JunctionLinks.cs | 4 | FanKerb, Trims, trims | generator |
| Scripts/City/CityMeshes.Junctions.cs | 55 | BranchAt, FanRingOf, FanRingPoints, Internal, TrimAt, Trims, trims | generator |
| Scripts/City/CityMeshes.LaneClip.cs | 4 | Trims, trims | generator |
| Scripts/City/CityMeshes.LotEntrances.cs | 5 | Trims | generator |
| Scripts/City/CityMeshes.Lots.cs | 11 | Trims, trims | generator |
| Scripts/City/CityMeshes.LowRise.cs | 1 | Trims | generator |
| Scripts/City/CityMeshes.Marks.cs | 27 | BranchAt, TrimAt, Trims | generator |
| Scripts/City/CityMeshes.Medians10.cs | 1 | Trims | generator |
| Scripts/City/CityMeshes.Medians8.cs | 16 | Trims | generator |
| Scripts/City/CityMeshes.Medians9.cs | 18 | Trims | generator |
| Scripts/City/CityMeshes.Merge.cs | 10 | BranchAt, Trims | generator |
| Scripts/City/CityMeshes.Minor.cs | 38 | TrimAt, Trims, trims | generator |
| Scripts/City/CityMeshes.PartBase.cs | 1 | Trims | generator |
| Scripts/City/CityMeshes.Proxies.cs | 3 | Trims | generator |
| Scripts/City/CityMeshes.R11.cs | 17 | TrimAt, Trims, trims | generator |
| Scripts/City/CityMeshes.Skirts.cs | 8 | Trims, trims | generator |
| Scripts/City/CityMeshes.Skyline.cs | 1 | Trims | generator |
| Scripts/City/CityMeshes.StreetDress.cs | 17 | TrimAt, Trims, trims | generator |
| Scripts/City/CityMeshes.StreetFurniture.cs | 4 | Trims | generator |
| Scripts/City/CityMeshes.TurnPockets.cs | 6 | Internal, TrimAt, Trims | generator |
| Scripts/City/CityMeshes.Unions.cs | 44 | BranchAt, Trims, trims | generator |
| Scripts/City/CityMeshes.Walks.cs | 41 | BranchAt, TrimAt, Trims, trims | generator |
| Scripts/City/CityMeshes.cs | 256 | BranchAt, PaveEdgeM, TrimAt, Trims, trims | generator |
| Scripts/City/CityMode.cs | 5 | FanKerb, Trims | control trim |
| Scripts/City/CityPoles.cs | 11 | PaveEdgeM, Trims | outline: poles kept off the drawn pavement and its clear zone |
| Scripts/City/CityProxies.cs | 2 | Trims | outline (check) |
| Scripts/City/CitySignals.Control.cs | 5 | Trims, trims | control trim |
| Scripts/City/CitySignals.Mitre.cs | 24 | BranchAt, PaveEdgeM, TrimAt, Trims, trims | control trim |
| Scripts/City/CitySignals.Signals2.cs | 9 | TrimAt, Trims, trims | control trim |
| Scripts/City/CitySignals.cs | 37 | BranchAt, PaveEdgeM, TrimAt, Trims, trims | control trim |
| Scripts/City/CitySigns.Guide.cs | 9 | Trims | outline (check) |
| Scripts/City/CitySigns.Signs2.cs | 9 | Trims | outline (check) |
| Scripts/City/CitySigns.cs | 16 | Trims | outline (check) |
| Scripts/City/CitySkyline.cs | 1 | Trims | outline (check) |
| Scripts/City/CityTraffic.Census.cs | 3 | trims | control trim |
| Scripts/City/CityTraffic.Junctions.cs | 2 | trims | control trim |
| Scripts/City/CityTraffic.cs | 19 | TrimAt, Trims, trims | control trim |
| Scripts/City/CityTrees.cs | 3 | Trims | outline (check) |
| Scripts/City/CityWorld.cs | 11 | Trims | outline (check) |
| Scripts/City/LineModel.SplitFrames.cs | 1 | Trims | outline (check) |
| Scripts/City/LineModel.cs | 1 | PaveEdgeM | control trim |
| Scripts/City/RaceRunOff.cs | 7 | Trims | outline (check) |
| Scripts/City/RoadsideOccupancy.cs | 15 | TrimAt, Trims, trims | outline: roadside occupancy against the drawn pavement |

## Editor tools: 58 files (gates, probes, audits)
They judge the drawn result; each is re-pointed to the outline when its stage lands. Listed, not classified one by one:

- Editor/CheckpointSelfTest.cs
- Editor/CheckpointTurnTrace.cs
- Editor/CityAudit.Barriers.cs
- Editor/CityAudit.Bridges.cs
- Editor/CityAudit.Concrete.cs
- Editor/CityAudit.Control.cs
- Editor/CityAudit.ControlMitre.cs
- Editor/CityAudit.Coverage.cs
- Editor/CityAudit.Curbs.cs
- Editor/CityAudit.Holes.cs
- Editor/CityAudit.Hooks.cs
- Editor/CityAudit.Houses.cs
- Editor/CityAudit.Hydro.cs
- Editor/CityAudit.Invisible.cs
- Editor/CityAudit.Junctions.cs
- Editor/CityAudit.LineGate.cs
- Editor/CityAudit.LineGate2.cs
- Editor/CityAudit.LineModel.cs
- Editor/CityAudit.Lines.cs
- Editor/CityAudit.Lots.cs
- Editor/CityAudit.Marks.cs
- Editor/CityAudit.Median.cs
- Editor/CityAudit.MedianGates.cs
- Editor/CityAudit.Merge.cs
- Editor/CityAudit.Minor.cs
- Editor/CityAudit.Network.cs
- Editor/CityAudit.Paint.cs
- Editor/CityAudit.Poles.cs
- Editor/CityAudit.Profile.cs
- Editor/CityAudit.Signs.cs
- Editor/CityAudit.Splits.cs
- Editor/CityAudit.Trees.cs
- Editor/CityAudit.Twin.cs
- Editor/CityAudit.cs
- Editor/CityBudgetProbe.cs
- Editor/CityDebugProbe.cs
- Editor/CityEdgeProbe.cs
- Editor/CityGroundProbe.cs
- Editor/CityIslandGate.cs
- Editor/CityJunctionProbe.cs
- Editor/CityKinkProbe.cs
- Editor/CityLandProbe.cs
- Editor/CityLaunchAudit.cs
- Editor/CityNoseCensus.cs
- Editor/CityPlayCheck.cs
- Editor/CityPreview.Streets.cs
- Editor/CityPreview.cs
- Editor/CityR11.cs
- Editor/CityRefSpots.Junctions.cs
- Editor/CityRefSpots.cs
- Editor/CityRoadsideProbe.cs
- Editor/CityRoadsideShots.cs
- Editor/CitySeamAudit.cs
- Editor/CitySmooth.cs
- Editor/ElevDiffProbe.cs
- Editor/LifeSimSelfTest.cs
- Editor/LineModelProbe.cs
- Editor/MedianProfileProbe.cs

## Fixes keyed to the map export (graphHash, edge or node indices, positions)
A re-export of Charlotte changes graphHash and the indices; each of these must be reproduced by the general rule (or remapped on purpose) before that. Exporter changes come last (stage 7).

| file:line | key | what it does |
|---|---|---|
| Scripts/City/CityMeshes.cs:3691/3701 | BesideSeatGraph + node 5178 | a seat beside the road kept to one node |
| Scripts/City/CityMeshes.CurbMedian.cs:63-81, 143, 152 | CurbMedianGraph 0xb2aa3453 + CurbMedianEdges (22 edges), CurbMedianSkipEdges {10544, 14670} | curbed medians on the owner's two spots; Idlewild launch spot kept without a curb |
| Scripts/City/CityMeshes.Junctions.cs:1250-1342 | NoseFillGraph 0xb2aa3453 + NoseFillNodes {1230} | a nose fill at one node, as shipped |
| Scripts/City/CityMeshes.Junctions.cs:1402-1407 | NoseFillGraph + NoseSheetSkip {5212, 6815} | nose sheets skipped at two nodes |
| Scripts/City/CityMeshes.Junctions.cs:1765-1771 | NoseFillGraph + GoreFillSpots {1230, 809, 701} | gore fills at three nodes |
| Scripts/City/CityMeshes.TurnPockets.cs:78, 116, 215 | CurbMedianGraph + PocketSkipNodes, CurbMedianSkipEdges | turn pockets left as they were at listed nodes |
| Scripts/City/LineModel.cs:2233-2356 | BranchDropGraph + BranchDropNodes | branch lane drops only at the checked nodes |
| Scripts/City/LineModel.Splits.cs:100-143, 275 | SplitsGraph + BfrSplitNodes, BfrHeldBack {7963, 12438, 231, 4025}, BfrBayKeep {16705} | Beatties Ford split nodes, held-back nodes, a kept bay |
| Scripts/City/LineModel.SplitFrames.cs:765 | SplitsGraph | the axis-frame splits' scope |
| Scripts/City/LineModel.RefugeList.cs:14 | RefugeListData (pairs + fingerprints) | the accepted refuge-island pairs (self-checking by fingerprint) |
| Scripts/City/CityElevation.cs:3908 | PlanePosLive {625, 5015} | plane fit kept live at two nodes |
| Scripts/City/CityElevation.VsOrderList.cs:12 | VsOrderData (edge-keyed) | the vertical smoothing's solve order |
| Scripts/City/CityMeshes.FanDrape.cs:59-64 | position: R23Centre (-3725, 6342) + radius | round-23 fan drape scope (Five Points) |
| Scripts/City/CityMeshes.Junctions.cs:201-224 | position (InR23Scope), recomputed from geometry each load | Five Points slip island noses: survive a re-export (keys recomputed) |
| Scripts/Checkpoints/CheckpointFile.cs:161, Scripts/CityTracks.cs:171 | graphHash check | checkpoint and track files tied to the export (rebuilt on a re-export) |
| Scripts/City/CityStations.cs:257-261 | graphHash (charlotte_fuel.bytes) | fuel station data tied to the export |
| Editor/CityAudit.Splits.cs:93, 111 | SplitsGraph + nodes 634, 7868, 9152 | the splits audit's exceptions |
| Editor/CityC7Probe.cs, CityKinkProbe.cs, CityIslandGate.cs; tools/city/spots/junction_spots.csv | positions (and edge / node ids for reading) | the owner's spots: position-keyed, ids for the report only |
