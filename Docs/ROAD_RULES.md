# ROAD RULES

The rules every road, junction and roadside generator in the Charlotte city must keep. They come from the owner's own
words (dates given); the gates and probes named under each rule are how a change is judged. A change that breaks a
rule is not shipped, whatever a count says: **a lower count is not a fix; the spot he showed, right in the frames, is.**

## 1. Accuracy and order of work
- **The whole city, not one intersection** (2026-11-26): "The entire city of roads should be accurate." Every hand-back
  leads with the whole-city scorecard by kind of meeting, then his reported spots.
- **His reported spot first** (2026-11-05, 11-16): fix the spot he shows, ship it at once; build what is done, never
  hold a build for one late job.
- **Road geometry is the top priority** (2026-11-05).
- **Real map data and real references** (2026-11-26): follow the real junction. State aerial photos (NC OneMap) may be
  used as small crops per junction for judging only; they never go into the repo, the bake or the game.
- **No brands** (sellable): no real logos or trade dress in signs or textures.

## 2. One owner per surface (the outline)
- **One closed outline per junction** (or cluster of map nodes that is one real junction), solved once per load: every
  corner is an arc tangent to both edges, a rounded nose where two roads run side by side, or straight through - never
  a point.
- **One surface**: inside a junction's outline the pavement is one surface at true height - no slab over a slab, no
  step over 1 cm, no sliver, no lift. Islands and median ends are holes in it, each holding a closed kerbed solid built
  on the hole's own vertices.
- **Each piece has one owner.** Inside an outline nothing of another method may draw (branch clips, gore quads, aprons,
  nose fills, mitre plates). A road's end row shares its vertices with the junction (no T-junction cracks).
- **An outline that cannot be solved** keeps the old method for that junction and logs the reason; the count of such
  junctions is reported.

## 3. Shapes (owner 2026-11-26, "Why is everything angular when the real roads have smooth curves only?")
- **No point, no straight cut** where two pavement edges meet: bay at its host, slip at both ends, fork, split, skew
  crossing, median end, island end. Every meeting is tangent-continuous.
- **A gore is paved to where it is about 2.4 m wide**; the land beyond ends in a rounded nose (radius 0.6-1.2 m).
  Whether the wedge beside a slip and the median are paved, grass or concrete follows the real junction ("either or,
  depends", 11-26); where the aerial does not settle it: paved while the gap is under 2.4 m, raised beyond; median kind
  by width.
- **A turn bay is a lane of its host** (11-26: "What are these roads?"): the host gains the lane, the median narrows,
  the bay opens on a reverse curve (two tangent arcs, R = (L^2 + w^2) / (4 w)) over the AASHTO length, its paint on the
  same curve; it ends at a stop line by a rounded concrete nose.
- **A slip lane is an arc** through its map points, tangent to the road it leaves and the road it joins.
- **No visible facets**: every curved edge within 1 cm of its true curve, and from a 6 m radius up at most 6 degrees of
  turn at any joint. Probe: Editor/CityKinkProbe.cs (rule 6).
- **No stretched textures, no gaps** (11-23): generated surfaces keep their neighbour's texel density; new geometry
  closes against what it meets.

## 4. Nothing in a lane
- **No curb, verge, face, slab or island inside a lane's travelled way** (11-25: "Some turn lanes have curbs in the
  middle of the turn lane"). Gates: CityIslandGate RAISED / OVERLAY / LINEUNDER, the line gate's MEDIAN class.
- **No see-through dividers, no holes** (11-25: "Dividers have holes and transparencies"). Gates: CityIslandGate
  SEETHRU, CityAudit.Holes, CitySeamGates.
- **No barricades in races** (11-16): city races are free routing between checkpoints.
- **No foliage through solids** (11-10).

## 5. Markings (junction marking rules, 2026-11)
- No solid line across a junction mouth; MUTCD yellow on the median side; no painted sliver under 3 m; no line may end
  in the middle of pavement; no line under a raised piece.
- Turn bays: solid white between bay and through lane over the full width, arrows in the bay (one per 40 m), a stop
  line at the nose (MUTCD 2009 3B.04, 3B.16, 3B.20).

## 6. Real widths and specs
- Everything to spec with realistic widths (lanes 3.66 m unless the map says otherwise); taper lengths from MUTCD /
  AASHTO by speed; the source of every number is named in the code beside it.
- Era: city, roads and trees as of 2026; cars and lighting 1999 (Lake Lure: pre-Helene).

## 7. Performance and pop-in
- **Measured cost for every change** (11-16): triangles, draws, build ms per tile, load ms; a junction rule is solved
  once per load, never per tile.
- **Zero pop-in**: geometry, lights and trees identical at every distance.
- Every generator change changes the tile bake fingerprint: an owner build needs a full re-bake.

## 8. Switches, tests and proof
- **In-game toggles, not launchers** (11-23): every new-versus-old switch is a LAB row; a mesh-shaping row is "a city
  bake", not a live flip.
- **Silent tests** (11-25): no test run may reach his speakers (batch mode or -psx-mute; count the muted logs).
- **Proof every stage**: the spot table's PASS / FAIL lines (tools/city/spots), the gallery (aerial | base | new, plan,
  same scale, north up, every frame labelled EDITOR), the scorecard by kind before | after, cost, every existing gate
  no worse than its base. List every edge still straight where the aerial's is curved; if any remains at his spots the
  first line of the hand-back says NOT MET.
- **Keyed fixes**: a fix keyed to graphHash or to an edge / node index is listed (Docs/ROAD_OUTLINE_READERS.md) and
  must be reproduced by the general rule before the map is re-exported.
