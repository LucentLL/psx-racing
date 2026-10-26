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
