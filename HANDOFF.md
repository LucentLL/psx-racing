# HANDOFF - REAL HEIGHTS junctions (2026-10-25 night, last round of the week; NOT shippable yet)

Branch `real-heights` (worktree `C:\Users\mcgee\PSX Racing-heights`), sandbox `C:\Users\mcgee\PSXRec`
(`$env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'`). Typecheck from the worktree needs the sandbox's references:
`tools\typecheck.ps1 -Project C:\Users\mcgee\PSXRec -Source "C:\Users\mcgee\PSX Racing-heights" -OutDir $env:TEMP\psxtc-heights`.
Reports: `C:\Users\mcgee\PSXRec\jm` (main), `j2`, `j3` (this commit's data), `j4_playcheck_out.txt`, `j5_shots_out.txt`.
Full write-up with the table: Docs/CHARLOTTE.md, REAL HEIGHTS "junctions, round 2".

## Gates (every launch number from `city-vsmooth.ps1 -Launch -Lanes`: old paths / lane carry, drawn profile on)
| gate | main bcf6298b | J3 = this commit |
|---|---|---|
| launch / unload old paths | 60 / 408 | 118 / 595 FAIL |
| launch / unload lane carry | 59 / 417 | 119 / 593 FAIL |
| routes launch | 0/0/0 | 1/0/0 (uptown e2422 n3640, 0.17 m) |
| census fw / ramp | 290/131/45/4, 2846/691/203/7 | 182/97/24/1, 1820/574/170/3 ok |
| arterial / collector / local | 1886/929/308/16, 5231/280/98/702, 5480/52/15/138 | 2125/919/296/13, 5365/293/94/154, 20088/99/16/77 |
| route p99 g @150 | 1.60/1.79/0.96 | 1.06/2.11 FAIL/0.68 |
| play-check | - | PASS (I-277 by S College St walled, 5 driven) |
J2 (`PSX_LAND_MAJOR_HOLD=0` when splicing) = 124/620 lane carry, census 172/88/25/0, 1759/581/160/3, 2112/908/282/9,
4890/306/101/150, 18329/99/15/69, p99 1.06/2.07/0.69.

## What the measurements say (read before changing anything)
1. The excess launches are the MESH, not the design: 72 of 119 are `@mesh-off-data` (main 30 of 59); the design's own
   break through the node is 0.2-1% at most of J1's launch nodes (scratch `nodecmp.mjs`). n3350 e2481: runtime station
   8.5 m out 0.65 m under the design (seated on its other host; last 8.5 m at 10.4%: SeatBranches / SplitTwoHosts /
   ClimbOut), the gore 0.26 m over its ribbon. n4444 e3176: a surface 0.3 m over the stations 14-24 m out, 9.5 m off
   the host's centreline (not the host). e6798: drawn 0.7 m over its seated stations. Lane carry is not the cause
   (main -1/+9, branch +1/-2).
2. Tryon p99: 123/123 samples past it within 30 m of a junction, worst 17-24 m out = trims (e16383 s 17, e16424 s 18),
   present with stations drawn straight; the major tangent did not move it. Main has the same kind (119/120).
3. FAN STEINER points land (115,720 of 117,765; 0 folded; Gilead 35/35).
4. Offline proxies improved a lot (fan folds 1493 -> 889, through-node 12/59 -> 11/43); the game's count barely moved.

## Next (in order, lean)
1. Mesh dump at n3350 / n4444 / e6798 (which surface the path samples), then the runtime seats on RPRF data (RealOn):
   seat only inside the host's pavement, ease on the design, no two-host 10% step. Re-measure `-Launch -Lanes`.
2. Tryon trims: `city-vsmooth.ps1 -Routes` A/B with `PSX_CITY_FANMAJOR=0 PSX_CITY_JOIN_STRAIGHT=1`, then a section dump
   of e16424 s 15-25 (n957).
3. Census: arterial > 0.5% "no curve here" knots; locals > 1% (99 vs 52), collectors > 1% (293 vs 280).
Screenshots (not committed): Docs/shots/heights/rh_gilead_before|after, rh_i277wall_before|after, rh_exit3a_round2
(+ the earlier rh_exit3a / rh_davidson pairs).
