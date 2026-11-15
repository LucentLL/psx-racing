# HANDOFF - EVENTS, round 1 (agent "events", branch `events`, 2026-11-15; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-events` off mountain-nm 9ced6b84. Sandbox PSXPerf. Owner 2026-11-15 (design `Events_Design_2026-11-15.md`, OWNER'S ANSWERS): sanctioned events (closed roads, no traffic, SAT/SUN MORNING or NOON) and street events (public roads with traffic, FRI-SUN NIGHT); 2-3 separate races each, prizes 1-6, a bonus for winning all; schedules fixed and shown before SIGN UP; results raise racing rep (sanctioned) or street rep (street) and the player's clubs' points; street events listed by street rep or passed on at meets and by clubs; damage only.

## What changed
- NEW `Scripts/LifeSim/Events.cs`: the catalogue (10 sanctioned, 6 street; tracks by id with a mountain fallback where a build ships no Charlotte), the schedule (event = every `period` weekends from weekend `phase`; weekend k's Friday = day 1 + 7k), the rule (drivetrain, hp as built, model year, origin, rep), SIGN UP / WITHDRAW, `DueNow`/`RaceAt`, the field (seeded catalogue cars in the event's hp window, "~N HP"), `TrafficFor` (the one place: sanctioned NONE, street LIGHT, MEDIUM on busy city roads, never more), `RecordResult` (prize by place, rep, club points, bonus), `MarkWrongCar`, `SweepInFlight` (DNF), `Tick` (no-shows at the rollover; every event race of the day scored for the clubs, NPC order seeded), standings, word of mouth (`ArrivedAtLot`), notices (mail + diary + pager, mail expires in 14 days). Switch `PSX_EVENTS=0` hides everything (saves keep their event fields).
- NEW `Scripts/LifeSim/LifeHomeScreen.Events.cs`: MAIN day view row WEEK | EVENTS (gold on a race day); the EVENTS page (SANCTIONED / STREET toggles, rows with emblem, rule badges, progress dots, status, 1st prize); an event's page (badges, ENTRY / BONUS / REP / YOUR CAR rows, SIGN UP or WITHDRAW with a confirm, race cards: road, date, block, hour, traffic, field ~hp, prizes 1-6, state); the event race's pre-race page (house: reminder + car list; line: START, wrong car = no-show); calendar words (EVENT · ROOKIE / RACE 1 OF 2, P2 · $300, NO-SHOW); club standings block + a club's EVENT POINTS line; RUN THE X RACE INSTEAD on a clash with a club race (either way).
- Save **v29**: LifeState `evEntries`, `evHeard`, `racingRep`, `evClubScores`, `evNews`; `LifeSimManager.MigrateToEvents` (lists exist, stamps 29). InsuranceR1c's `saveVersion == 28` pin -> `>= 28`.
- Shared-file hooks (each one or two lines): `LifeHomeScreen.cs` Start (Events.SweepInFlight after the blacklist's), Rebuild `case "events"`, GoBack first line (EventsBack), BuildDayBlocks WEEK row, BlockLines first line, BuildHub race row (EventRaceRow), CellWord first lines, BuildPreRace first line (EventPreRaceFirst). `LifeHomeScreen.Clubs.cs` 3 calls. `LifeRules.cs` ApplyRaceResult step 0b (RecordResult before the block is spent) + `else if (eventRace)` branch (no purse payout, no daily cap, no generic rep), rollover `Events.Tick`, NightHasPlans (an early night does not sleep through a signed-up NIGHT race). `RaceHandoff.cs` EventKey/EventRace (+ClearAll). `CarMeets.MarkArrived` (word of mouth once a night). `Town/DepartScreen.cs` the line's RACE row names a due event race and the daily cap does not shut it.
- Tools: `Editor/LifeSimSelfTest.Events.cs` (registered in RunAll), `Editor/EventsPlayCheck.cs` + `tools/events-play-check.ps1`, `Editor/EventsPreview.cs` ("PSX Racing/Preview Events" -> Screenshots/events; pad walk at screen-pixel scale, overlaps, CLIP, nav reach). Metas for the five new scripts committed (PSXPerf GUIDs).

## Defaults (mine, tunable in Events.cs; owner to confirm)
- SANCTIONED: ROOKIE CUP (any car, max 180 hp, $100, 1st $500, bonus $600, every 2 weekends, SAT+SUN MORNING); FRONT DRIVE CUP (FF, 200 hp, $150/$700/$900); CLASSIC CUP (1979 or older, $150/$800/$1000); REAR DRIVE CUP (FR, 280 hp, racing rep 10, 3 races); IMPORT CUP (Japanese, 280 hp, rep 10); CONTINENTAL CUP (European, rep 10); AMERICAN STOCK CUP (American, rep 10, Uptown closed off); MIDSHIP CUP (MR, rep 20); ALL-WHEEL CUP (4WD, 300 hp, rep 20); CAROLINA OPEN (any, rep 40, $600/$3000/$4000).
- STREET: FRIDAY NIGHT RUN (listed, street rep 10, 250 hp); I-277 MIDNIGHT (listed, 25, MEDIUM traffic); INNER CIRCLE NIGHT (listed, 50); TOUGE NIGHT (word: meets + JAPANESE club, 300 hp); US 74 BIG BLOCK NIGHT (word: + AMERICAN club, American-built); Y2K RUN (word: + FUTURE club, 1995 or newer, 320 hp).
- Prizes 2nd-6th = 60/40/25/15/10% of 1st. Racing rep 5/3/2/1/1/1, street rep 6/4/2/1/1/1, bonus +5. No-show -2 racing / -3 street (fee kept); DNF -1/-2; withdraw before the day: nothing back, no rep. Street events listed from street rep 10. Club points 10/6/4/3/2/1 per place; 20 of the player's own points pass one member on that club's board.

## Gates (PSXPerf)
- Self-test: 62 FAIL = base 62, 0 new, 0 gone; all 57 events checks ok.
- Play check `tools\events-play-check.ps1`: 24 ok, 0 FAIL (ROOKIE CUP from the line: NONE traffic, MORNING, the event's field, P2 banked = $300, racing rep +3, JAPANESE club +6, block spent, no daily cap; FRIDAY NIGHT RUN: LIGHT, NIGHT on Tryon, win = $1,000 and street rep +6, rolled the day without a no-show).
- Previews: 12 pages x owner_2000x1077 / landscape_16x9 / phone_640x360 = 36 shots: 0 control overlaps, 0 CLIP, every control reachable, every pad walk to the page end, MAIN still fits.

## NOT DONE / open
- A purse race can still be booked into an event's block (LifeRules.CanBookAt untouched); the event takes the block and the booking is swept as missed.
- The hub's SLEEP caption does not warn about an event race; a DAWN sleep after an all-nighter can sleep through a MORNING race (no-show).
- Racing rep is not in the header (header REP = street rep); it is on the EVENTS page. No rep decay for racing rep. Club standings never reset (no seasons). Not in the month view.
- MAIN-edition fallbacks checked by the self-test only; no race sweep / city check run.

﻿# HANDOFF - GT-STYLE SHOWROOM (agent "showroom", branch gt-showroom, 2026-11-15; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-showroom` off mountain-nm eb6fa63a (PC38 + one wizard pad link). Sandbox PSXCity. Owner 2026-11-15: "I want car viewing and car selection to look like the early Gran Turismo games, with the car rotating (or able to be rotated by player)" (reference: a GT2 garage screen). Same KIND of screen in the game's own MenuKit palette; no GT icons, logos, fonts or credit symbol; the drivetrain diagram is drawn from UI rects.

## What changed
- ROUND 2 (agent showroom2, same day): THE WHOLE BLOCK ON THE FIRST SCREEN (owner's 2000x1077 window cut the band on MY CARS, the car page, the dealer and the viewing). `Showroom.Build` now takes the ROOM from the hero's top to the fold (`HeroRoom(y)` = BodyH + y - 4; the city make page the same; NEW GAME = its panel under the titles) and fits the name rows, the stage and the band into it. Wide canvas (>= 1.6:1 and the hero >= 1.75x as wide as its room: owner, 16:9, phone_wide, 640x360): the band is a 270-300 column BESIDE the stage (price + note, figures, then the buttons standing on the stage's bottom line), the stage takes the rest of the width and all the height under the name. Otherwise (4:3; NEW GAME's 700 panel on 16:9, where that is the bigger car) the band stays UNDER a full-width stage sized from the room actually left (min 140/160, max 400). `ShowroomBlock` on the stage: DOWN from < > to the band's first button, UP back to >, LEFT from a side button to >, DOWN out of the block to the first line under it (nearest across); put back over the watch's geometric graph by `Showroom.AfterGrid` (after MenuNav.Defer in LifeHomeScreen.WireNavigation, CityFrontEnd.WireNavigation and the NEW GAME pick). Gone: `Showroom.Overhead` / `StageHeight`, the viewing's 320 cap. Stage at the owner's window: MY CARS / car page 696x317-323 units (~1338x615 px, 2.2:1, was 1900x433), dealer 696x253, viewing 696x283, NEW GAME 656x358.
- `CarViewer.Studio.cs` (new, partial of CarViewer): the showroom frame. Rendered at the panel's own pixel size (capped 1920 wide), supersampled 2x on PC/editor (1.5x handheld) and filtered down (the URP assets have MSAA off), bilinear on screen. Studio light through the paint shader's own inputs for that one render, then put back: key light high front-left (_PSXLightDir/_PSXLightColor), fill = ambient/sky ambient, and a generated softbox panorama (overhead box, two tall strips behind the car either side, dim front panel, floor) in `_PSXEnvPano` with `_PSXEnvOn`=1 - the rim and shoulder lines as the car turns; the env globals are saved and restored round the render (CarReflections' in a race). Floor: a soft pool of light (UI/Default disc, queue 1000, drawn before the car), a faint back glow, the contact blob plus a wider soft one. Turning: idle spin 18 deg/s, drag with a little inertia, right stick and , . (`CarViewer.TurnInput`), idle spin eases back 3 s after the last input. `Tick(now, dt)` is the clock (Update calls it; the probe drives it). HD car maps come with the shared livery materials (CarHD) as before.
- `CarViewer.cs`: partial; RenderNow -> RenderStudio when a showroom is attached, else `RenderClassic` (the old body, untouched); AttachTo / Snapshot / the inspection poses leave the showroom and draw exactly the old 320x200 point-filtered frame; CarViewerDrag routes horizontal drags to the studio turn (no tap-flip on the big stage).
- `Showroom.cs` (new): the card + stage builder. Big bold italic model name, make and year (+ "3 / 12") right and the page's tag (NEW / the lane / where the car is) left under it, the stage with < > buttons, DRAG TO TURN, the livery's colour name bottom right ONLY when its baked name has colour words (`PaintName`, never invented), then three columns: price/value + note, the drivetrain diagram (body outline, driven wheels/axles/shafts lit, engine block where it sits; FF FR MR RR 4WD) with year / hp / kg, and the page's actions. LEFT/RIGHT on the arrows (d-pad, arrow keys) step the car and keep the cursor (`ShowroomBrowseKeys`, after Selectable.OnMove, the MenuNavScroll pattern); a sideways swipe over the name browses (`ShowroomSwipe`; vertical drags go to the page scroll). LB/RB, PageUp/PageDown untouched.
- Pages (`LifeHomeScreen.Showroom.cs` new + hooks in LifeHomeScreen.cs): car page = the stage at the top (LEFT/RIGHT through the garage), everything it had below (status, bars, IN THIS CAR, SPECS, PARTS + TUNING, INSPECT, WASH, SELL, notes, faults + repairs); MY CARS = one car at a time (OPEN THIS CAR, GET IN / IN THIS CAR / NOT HERE; where it is, fuel, condition, faults), then TOOLBOX / FUEL TRUCK / STORED PARTS / STORED WHEELS, then the list as before; dealer = the lot one car at a time, new first (LOOK IT OVER opens the same viewing a row did: test drive, offers, BUY), the trade-in block under it; the viewing and the classifieds' BUY page show their car on the stage; NEW GAME pick = the three lanes one at a time (DRIVE THIS ONE commits as the close-up does, FULL SHEET opens the old close-up; its BACK returns). City edition PICK A CAR: a make's page has the stage over its list (DRIVE THIS ONE). Arcade car card: its turntable panel is the studio frame (no browse there).
- Switch: `PSX_SHOWROOM=0` (Showroom.On) builds every page as before (each hook falls back to the old code).
- Editor: `ShowroomPreview.cs` (new; "PSX Racing/Preview Showroom", `CaptureWithNav` also runs NavScrollPreview); LifeHomePreview.Shoot redraws a showroom at the laid-out size before the photo and logs it.

## Checks (see the round's numbers below)
- Round 2 (scratchpad `showroom2\preview2.log`, contact sheet `showroom2\showroom_contact_sheet.png`; the viewing now shot at all 5 sizes): FOLD probe (new, `ShowroomPreview.FoldProbe`: depth of the block's lowest part vs the viewport / panel) inside the first screen on 36/36 shots, every page at owner / 16:9 / 4:3 / phone_wide / 640x360; DOWN from < > to the band's first button and UP back, now JUDGED: 24/24; car page DOWN from > = PARTS + TUNING as before; control overlaps 0 in 36; nav reach all controls on 34/34; browse 31/31 (+5 viewing, one car); NavScrollPreview car page: page end REACHED, last fault row ON SCREEN at all 4 sizes, 0 FAIL; `[Showroom] DONE, 0 FAIL(s)`. CLIP: 1 line, sr_mycars/phone_640x360, the side column's note "cut on the RIGHT by its panel, 22 px" - the 640x360 picture shows it wrapped inside the column (the checker's width there is ~1.33x the drawn one). No self-test (layout only).
- Pictures (PSXCity `Screenshots/showroom`, `ShowroomPreview.CaptureWithNav`): NEW GAME pick, MY CARS (+ the 3rd car), car page (+ an RR car), dealer (+ the last used car), a lot viewing, the city edition's make page; owner_2000x1077, landscape_16x9, tablet_4x3, phone_wide, phone_640x360 (the browse/viewing extras at owner + phone_wide); `sr_off_*` = PSX_SHOWROOM=0 (the old car page and MY CARS). Contact sheet of the round: scratchpad `showroom\showroom_contact_sheet.png`.
- Stage drawn at 1900x433 px (owner window, scroll pages), 1823x435 (NEW GAME), 608x152 (640x360).
- Control overlaps: 0 in 33 checks. CLIP: 0 lines on the showroom pages (the run's 39 CLIP lines are all NavScrollPreview's pause-menu settings at 640x360 / 780x340, untouched here). Pad reach (CheckNavReach): every control reachable on all 31 shots.
- BROWSE probe: LEFT / RIGHT on the arrows step -1 / +1 and keep the cursor on 31/31 shots.
- TURN probe: idle 18.0 deg/s; a 240 px drag turned 72.9 deg (pre-layout stage width; ~38 deg on the owner's 1900 px stage), coasted 37 deg (edit-mode drag speed is inflated: dt floor 1/240), 0.1 deg in the 1.6 s after (held), 18.0 deg/s again after 3 s; a virtual pad's right stick read 1.00 and turned the car 100 deg in 0.5 s.
- NavScrollPreview (the owner's pad rule): car page with 10 faults and no money, page end REACHED and the last fault row ON SCREEN at owner / phone_wide / 16:9 / 4:3; 0 FAIL.
- Self-test: 62 FAIL = base 62. One swap, both car-asset checks on the sandbox's own Resources (not synced by these jobs, not touched here): new "every library key has a baked prefab (got flatsix_coupe_87)", gone "every shell has a body and a wheel mesh (got 1 without)".

## What still looks off (from the pictures)
- (Round 2 fixed the cut band.) In the 270-unit side column a 4WD car's line reads "4WD" (the long "ALL-WHEEL DRIVE" form needs ~310), and the NEW GAME note wraps to 3-4 lines. The viewing's side column holds only the figures (that page's price and buttons are under the stage, below the fold).
- The cars are the game's shells: 512x256 point-sampled sheets in the editor (blocky at this size; CarHD should help on PC), and catalog cars sharing one shell look alike on the stage.
- The background is near black with a faint glow; the rim/shoulder highlights are subtle.

## At merge
- New files with hand-made metas: Scripts/LifeSim/CarViewer.Studio.cs, Showroom.cs, LifeHomeScreen.Showroom.cs, Editor/ShowroomPreview.cs. CarViewer is now `partial`.
- Touched: LifeHomeScreen.cs (BuildCarPick, Rebuild, the turntable stick block, DrawCarView, BuildGarage, BuildCarMenu, BuildDealer, WireNavigation: one `Showroom.AfterGrid` line), ArcadeFrontEnd.cs (BuildCarCard, Update), CityFrontEnd.cs (Rebuild, BuildMakePage, CityHero's room, WireNavigation), LifeHomePreview.Shoot (redraw a showroom before the photo).
- Worth a look in the PC build: frame time with a showroom open, the CarHD sheets on the stage, the pad on a real screen (DOWN from the arrows).

## Not done / not verified
- No player build, no Android run, no frame-time measurement of the bigger render (PC: one car at up to 3840x~900 supersampled per frame; handheld 1.5x).
- Editor pictures show the classic 512x256 car sheets (PSX_CAR_HD off in the editor, HD bundle not in the sandbox).
- Paint shop / specs / service / tune keep their side-panel turntable (classic); the arcade car card is not browsable; the market (classifieds list) and junkyard lists are unchanged.
- The pad's DOWN from the arrows could not be judged in the harness (MenuNav's 4-unit line floor vs a 10-world-unit camera canvas); CheckNavReach reports every control reachable.

# HANDOFF - MENU NAVIGATION + ARCADE DAMAGE (agent "uinav", branch ui-nav-arcade, 2026-11-14; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-uinav` off mountain-nm 8d656a01 (PC37). Sandbox PSXCity. Owner 2026-11-14: car faults unreachable on a pad (DOWN wrapped to the top); OPTIONS > VISUALS rows overlapping, no scroll; nothing may overlay a button or text. Coordinator: NEW GAME wizard at 2000x1077 (1.86:1), NEXT over PREMIUM and the plan terms.

## What changed
- MenuNav, central (every page): `MenuNav.ScrollInstead` + `MenuNavScroll` (IMoveHandler, added by MenuNavWatch to the cursor's control inside a ScrollRect content). UP/DOWN scroll half a viewport instead of moving when the move would leave or wrap the page with page left, when more than half a viewport of hidden page lies before the next control, or when the cursor was scrolled out of view. The navigation graph itself is unchanged. A footer control under a list (BACK) no longer throws the list to its top. `MenuNavWatch.applied`: a page's own links after the deferred grid. `PSX_NAV_SCROLL=0`.
- SettingsPanel: the rows in a 322-unit scroll list between the strip and the help line (7 rows show; VISUALS' 13 scroll); help, BACK and the hint never on a row. `PSX_SETTINGS_SCROLL=0`.
- NEW GAME wizard (LifeHomeScreen.BuildWizard): job card + health plan scroll in their own band; NEXT in the band under it, left-aligned, as wide as the plan row; each plan's DOWN = NEXT, NEXT's UP = the picked plan. `PSX_WIZARD_SCROLL=0`.
- Arcade DAMAGE (Scripts/ArcadeDamage.cs new): OFF / VISUAL ONLY / FULL, default FULL (PlayerPrefs psx.arcadeDamage); a row in OPTIONS > GAMEPLAY (arcade sessions only) and on the arcade's own OPTIONS page. CrashStopGap.Arm with no career -> ArcadeDamage.Arm: a DamageLedger with Banks=false, LiveFx=FULL. `DamageLedger.Acts = Banks || LiveFx` now gates OnFail, the bags, hydrolock and ApplyLive (pull, exhaust power, radiator); injury (CrashFates) and the save (Flush/Fold) stay Banks-only. DamageLook.bodyOnly = VISUAL ONLY (dents + glass; no lamps out, steam or bags). RaceManager.RetirePlayer: in an arcade FULL race the DNF ends the player's race (State Finished, FinishPos 0, ArcadeDamage.Dnf); RaceHUD banner "DNF / THE CAR CANNOT GO ON"; arcade RESULT "DNF". `PSX_ARCADE_DAMAGE=0` = none. Career ledgers: Acts == Banks, so unchanged.
- Editor: NavScrollPreview.cs (new, "PSX Racing/Preview Nav Scroll"), ArcadeDamagePlayCheck.cs (new), LifeHomePreview.Shoot probe hook, DebugBenchPreview.ShootPause internal + scroll-aware off-canvas check + the owner's 2000x1077 size.

## Gates
- Typecheck OK (every commit).
- Nav preview (scratchpad uinav\nav.log, 0 FAIL): car page, 10 known faults and $0 (no repair selectable): DOWN from the lowest control scrolled 0>219>437>656>769 of 769 at 2000x1077 and the last fault row came on screen; one more DOWN goes to BACK; UP walks back to the top. VISUALS: pad DOWN x13, every row in view as reached, then BACK; 0 control overlaps at 6 sizes. Wizard: 0 overlaps at 4 sizes, but the pad DOWN path in that run was wrong (- > PREMIUM > -); the explicit plan/NEXT links were added after it and are NOT re-run.
- LifeSimSelfTest: base 62 FAIL (scratchpad uinav\base_selftest.log). Mine: queued behind the stuck play check (uinav\mine_selftest.log), not compared.
- Arcade play check: did NOT run (Unity: "Can't add script behaviour 'ArcadeDamagePlayCheck' because it is an editor script"); its editor sits in play mode in PSXCity until the job helper's 20-minute limit.

## NOT DONE
- Arcade damage unverified in play: move ArcadeDamagePlayCheckRunner to a file of its own name (Editor/ArcadeDamagePlayCheckRunner.cs) and run CrashPlayCheck.Run with PSX_ARCADE_DAMAGE_CHECK=1 (no -quit).
- The self-test FAIL-set diff (mine vs base 62); the wizard pad path re-run; LifeHomePreview.Capture's CLIP report and the ~10-page overlap fixes; the CITY edition has no DAMAGE option.

# HANDOFF - TEST TOWN ROUND 2: COMPLETE THE SHIFT, END DRIVE COSTS ONE BLOCK (agent "testtown", branch test-town = mountain-nm 8d656a01 + this round, 2026-11-14; NOT pushed, NOT merged)

**Owner, PC37:** "When I deliver a pizza to a house, it doesn't give me an option to complete shift. When I End Drive it sends me home and becomes the next calendar day, even though it should advance to the next time block."

**Cause** (`Scripts/Town/TownWorld.cs` CollectOrder): the town's PICK UP clocked on AND spent the whole scheduled shift at the counter (`LifeRules.SpendShift`, as the race-track delivery always had: the NIGHT shift is EVENING + NIGHT, which rolls the day), then END DRIVE's apply-back (`LifeRules.ApplyRaceResult`, the free-roam branch) spent one block more (ActDrive): an evening pickup came home on the next day's MORNING. The race-track flow never hit this, because its drop ended in a race scene whose apply-back is a Delivery (no block).

**Fix:** on the merged map (`LocalDropsHere`) the shift is `PizzaRun.ShiftOn`: clock on at the FIRST run only (attendance as before); the counter hands over the NEXT run without a second clock-on ("NEXT RUN — TAKE THE ORDER", cue "NEXT ORDER — TONY'S"); the shift's blocks are spent when it ENDS — END SHIFT calls the same `SpendShift` rule, then `TownExit.GoHome(.., "main", commute: true)` (no block on top). END DRIVE part-way through a shift is the one block every drive costs (labelled WORK in the day); any arrival home ends the shift (`LifeHomeScreen`). The done panel offers **NEXT ORDER** and **END SHIFT** on a shift (CARRY ON otherwise). Wages unchanged (attendance at the first run, weekly cheque); tips unchanged. `PSX_TOWN_LOCALDROP=0` keeps the old up-front spend for race-track runs. No scene rebuild for this round (runtime + play check only).

**Gates:** typecheck OK. NOT RUN: the self-test against an 8d656a01 base and the extended TownHomePlayCheck (two drops then END SHIFT; END DRIVE after a drop = same day, one block) were queued in PSXCity from 17:27, but agent 'uinav' held its lock without a break (17:17-18:37 a CrashPlayCheck and a 60-min LifeHomePreview.Capture, then a new job at 18:37); the queued job was stopped at 18:40 so it cannot leave a lock behind. To run them: sync this branch into a free sandbox, PSXRacingBuilder.BuildTownMenu (-quit), LifeSimSelfTest.Run (-quit) against the same two calls on an 8d656a01 snapshot, then TownHomePlayCheck.Run WITHOUT -quit and without -nographics (it exits itself; report PSXRacing_townhome_play_check.txt).

# HANDOFF - TEST TOWN: YOUR STREET AT THE TOWN MAP'S WEST END, PIZZA TO A MARKED HOUSE (agent "testtown", branch test-town, 2026-11-14; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-testtown` off mountain-nm 3db94af9. Sandbox PSXCity (lock "testtown", released; car_facts.json(+.meta), rg2_cars.json, DriveSide.csv copied by path). Scratch `scratchpad\testtown\`: runner `job.ps1`, `base_selftest.txt`, `m1_selftest.txt`, play reports `p3_play.txt` / `p4_play.txt`, pictures `pics\townhome_*.png`.

**Owner, 2026-11-14:** "I want Town and Home/Neighborhood merged onto a single map. This area will be purely for testing in debugging ... Delivering pizza is no longer a race track, but to a random house in the neighborhood." Then: "add the neighborhood to one end of the large town map. don't rewire the town."

## What changed
- **Town.unity is the merged map** (`Editor/PSXRacingBuilder.TownHomeEnd.cs` new; small hooks in `PSXRacingBuilder.Town.cs` / `.Neighborhood.cs`, `WorldKit.MeshPrefix`). `BuildTownScene` first builds the street with the street map's OWN functions (BuildNbGround, BuildTownHome, BuildNbPlots, the walk-in rooms, NbRespawns) in its own frame on the empty scene (its ground probes must not find the town), then turns the `HomeEnd` root -90 deg and moves it by (-436, +11.18, +110): the street runs on along the main street's line (z 0), its end at x -268, joined by 110 m of new road (`HomeRoad`: the main street's width, height and tarmac, grass verges) to the main street's end at x -158; your house up the hill at x ~ -490 (the street keeps its 11 m climb). The town is untouched except its WEST wall and WEST zone line (gone: they stood across the new road; a 24 m stub of wall closes the step between the town's north wall z 82 and the home end's z 58) and its kerbs taper flush at the west end. New bounds HomeEndN/S/W. Log: "[Town] home end: ... 12 doors a delivery can go to; the road in is 110 m".
- Street-builder hooks, all behind `nbMerged` (never set for Neighborhood.unity, whose output is unchanged): past the street's end the road profile stops rolling and the land relief fades (flat at the town lawn's height); the lawn lattice stops at the town lawn's west edge (x -240) instead of running 110 m on under it; no run-off verge at the street end; every neighbour gets a `DropDoor` anchor, a `DropMarker` (orange pin over the roof + 5 dots by the kerb, built OFF) and an address (`101`..`162 HOLLY RIDGE CT`). Merged meshes save as `Generated/TownHome_*` so the two scenes cannot reshape each other's ground.
- **Home goes to the town** (`Scripts/Town/TownLayout.cs` new): `TownLayout.HomeSceneIndex` (= Town) is loaded by `LifeHomeScreen.StartTown` (DRIVE, "drivehome"), the "garagewalk" hop, `HomeWalk.Enter` (walk in; inspect return) and `GarageWorld.DriveOut`. The Town's baked spawn is your own drive; GarageWorld(embedded) + HomeArrival wired as in Neighborhood.unity. `TrackCatalog.NeighborhoodSceneIndex` unchanged (door audit still finds the old scene, which stays in the build unused). `DoorTour`'s DRIVE door expects "Town".
- **Pizza to a house** (`TownWorld`, `PizzaRun`, `LifeRules`, `DepartScreen`, `HudMinimap`, `RaceHUD`, `Scripts/Town/DeliveryDoneScreen.cs` new): Tony's PICK UP rolls a RANDOM neighbour (`PizzaRun.DropHouse/DropName`, TrackIndex -1), par = 20 s + road m / 13 m/s (`LifeRules.LocalDropParSeconds`). The house's marker lights, the HUD map (new in town: the builder's road lines via `HudMinimap.SetLocalRoads`) marks it, the cue names the address. The clock (`PizzaRun.LocalSeconds`) runs from the counter until the car STOPS within 22 m of the door (re-arms past 34 m); get out, walk to the door, DELIVER (FootTarget). Paid by `ScoreDelivery` (new `parOverride`) + `SettleDelivery` (the race apply-back's delivery branch lifted out unchanged; both paths call it): tip for time and the order's state (PizzaCargo condition/consumable/hit, latched as before) into the wallet; wages stay on the weekly cheque (ClockOnShift at the counter). Done panel: TIME vs PAR, THE ORDER, TIP, WAGES, CARRY ON. A track-rolled order arriving in the merged town (Pizzeria.unity's walking shift) becomes a house. At the east line with a house order aboard, MAKE THE DELIVERY is shown shut with the address; the east line also offers INSPECT A CAR on the merged map.
- Switches: `PSX_TOWN_HOMEEND=0` at BUILD time = the town as it was (both lines, west wall); at RUN time = home is Neighborhood.unity again. `PSX_TOWN_LOCALDROP=0` (run time) = orders go to a race track as before.
- Self-test (TestTownScene): one zone line on the merged map; drop doors/markers/addresses; map roads; rooms + arrival; the car on your drive over tarmac; level tarmac all along the road in. `Editor/TownHomePlayCheck.cs` (new) plays the loop.

## Build recipe (coordinator)
1. Sync `Assets\PSXRacing\Scripts`, `Editor`, `Shaders` from test-town (robocopy /MIR). No asset/Resources/Art file is new (four new .cs + .meta ride in Scripts/Editor).
2. Rebuild ONLY Town.unity (delete it first; Neighborhood.unity is unchanged, do not rebuild):
   `. <repo>\tools\unity-wait.ps1; Invoke-UnityJob -Log <sb>\townbuild.log -UnityArgs @("-quit","-batchmode","-nographics","-projectPath","<sb>","-executeMethod","PSXRacing.EditorTools.PSXRacingBuilder.BuildTownMenu","-logFile","<sb>\townbuild.log","-accept-apiupdate")`
   Proof: the scene file exists and the log has "[Town] Scene saved: Assets/PSXRacing/Scenes/Town.unity (with the home end)". 3.5 min in PSXCity.
3. Player-only build. No tile re-bake, no CityTileCache.Format bump, no kit slot, nothing under Scripts/City.

## Gates
- Typecheck OK (tools\typecheck.ps1 against PSXCity).
- LifeSimSelfTest: base (3db94af9 code, Town + Neighborhood rebuilt with it in PSXCity) 62 FAIL; mine 62 FAIL; the two FAIL lists are identical (0 only-in-base, 0 only-in-mine). New checks all ok: 1 zone line (east), 12 drop doors + 12 markers off + addresses, 21 map road points, rooms + arrival beside the real car, car on your drive (-490.3, 11.5, 0.0) over road-layer tarmac, road in 0 bad samples, 16/16 respawns on road, kerbs mountable.
- Door audit (the self-test's DoorAudit block): ALL 47/47 venue doors, 5/5 career, 21 scenes; MAIN 27/27 + 20/20 absent + 5/5; CITY 20/20 + 27/27 + 5/5; all ok.
- Play check (TownHomePlayCheck, hidden batch, graphics on; `p4_play.txt`): 24 ok, 0 FAIL, "ALL PLAYED". DRIVE from the house -> Town scene, car 0.3 m from HomeSpawn; driven 475 m (36 s) down your street and the road in to Tony's; PICK UP -> 161 HOLLY RIDGE CT (track -1, par 0:46), only that house's marker lit, map destination set, the HUD map up with the drop on it, boxes on the seat; driven 306 m (25 s) to the house; stopped = arrived, clock held at 0:29; DELIVER at the door -> order gone, panel up, tip $151 = wallet +$151 (INTACT, 0:29 vs 0:46); the panel's 12 lines measured on the drawn glyphs: none clipped, none overlapping each other or the button. (The run before, `p3_play.txt`, caught the club note riding over CARRY ON - fixed.)

## Pictures (scratch `pics\`)
`townhome_1_home_end.png` (the street up the hill to your house), `townhome_2_road_in.png` (from the end of the main street: the road in, your street beyond), `townhome_3_drop_marker.png` (the orange pin over the order's house, a kerb dot), `townhome_4_delivery_done.png` (the done panel).

## Not verified / known
- The player build; a build with PSX_TOWN_HOMEEND=0; night, rain; frame cost of one 735 m map (the street was its own scene).
- Walking into your garage / an on-foot arrival in the merged town, and Pizzeria.unity's walking shift becoming a house: code paths not played.
- Play checks that load Town and expect its old spawn at the west end (FuelPlayCheck, ParkedPlayCheck, HomeMirrorPlayCheck, FirstDrivePlayCheck's town hop) were not run; they may need their start moved.
- The street's lawn meets the town lawn at x -240 at the same height, but its grass runs at 90 deg there (UVs from its own frame). The play check places the walker at doors and turns the car round at the shop's kerb (logged).

# HANDOFF - FAULTS R1c-A "WHAT A CRASH COSTS YOU: THE CAR AND THE DRIVER" (agent "r1ca", branch faults-r1c-crash, 2026-11-13; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr1ca` off mountain-nm 825d5ed7. Sandbox PSXPerf (lock "r1ca", released; car_facts.json(+.meta), rg2_cars.json, DriveSide.csv copied in by path; play checks hidden). Reports: scratchpad `r1ca\` (runner `r1ca.ps1`, `base_selftest.txt`, `h3_selftest.txt`, `h1_crashplay.txt`, `h1_race_census.csv` (before the PC34 fix), `h2_race_census.csv` (after), `r1ca_sheet.png`). Design: OWNER'S ANSWERS 2026-11-13 (idea, KEEP INJURY SIMPLE, NO MODE CHOICE, insurance). Save **v27** (R1c-B takes v28).

## What changed (each switch =0 is the behaviour before)
- THE DRIVER (PSX_INJURY), new `Scripts/LifeSim/CrashFates.cs`: every banked pulse on the career car is an injury score = delta-V x direction (side 1.4, rear 0.7) x a narrow tree/pole 1.3 x the FAIL's intrusion 1.15 x belt off (1.6 head-on, 1.2 side, x2.5 roll) x bags that fired 0.85 x a race/rally car's cage 0.55 x HEALTH (0.9 at 100 .. 1.2 at 0). Under 9.5 nothing; 9.5-15 CONCUSSION; 15-28 HOSPITAL (a broken arm / leg / leg and ribs: 1-2 / 3-5 / 8-14 days, $2.4-4k / $11-18k / $32-60k, off work and the wheel 3-12 weeks x HEALTH 0.8-1.4); 28+ DEATH. No mode choice. New `Scripts/InjuryFx.cs`: the frame at the hit frozen, smeared and ghosted, then dark, the sound with it (concussion 4 s, hospital 5 s, death 6 s), then the pause menu's tow ends the drive (`PauseMenu.EndDriveForInjury`). The injury rides in the damage file (IncidentRec inj/injScore/fail/narrow), folded once by hitId; the home screen processes it once (new `LifeSim/LifeHomeScreen.Fate.cs`): the hospital days pass, the bill goes through `CrashHooks.Hospital` (null = the player pays what they have; the rest is `fate.hospitalOwed` for R1c-B's BILLS), ONE summary page; `DriveRefusal` and `ClockOnRefusal` refuse until recovered; shifts and club events in that span settle as `Duty.Medical` (unpaid, never a miss or a strike). Death: THE CHARLOTTE HERALD's obituary is the only page, the career is written once and then locked (`LifeSimManager.Save`), NEW GAME through the one existing door. The belt: SEAT BELT: ON/OFF row in the pause menu (career drives), saved; on by default.
- A FAIL STAYS (PSX_FAIL_STAYS): a structural FAIL leaves a hidden row (`frame_bent`, `subframe_failed`, `knuckle_broken`, `roof_crushed`, as DamageRules decided) and `OwnedCar.wreckHit`; every door says "WRECKED - IT WON'T DRIVE UNTIL A SHOP FIXES IT" while a FAIL row stands (no part named); a pro inspection always finds it and adds the $55 tow-in; a shop repair adds the tow-in and sends crash rows through `CrashHooks.Repair`.
- THE WRITE-OFF (PSX_WRITE_OFF): after a real crash (band 3+ or a FAIL), if Delmar's estimate of the crash rows (the bag set at the dealer) is over 75% of the value before the hit: KEEP IT (salvage title; the insurer's payout less the scrap) or LET IT GO FOR SALVAGE (the payout, or scrap with no insurance, less a loan). `CrashHooks.TotalLoss` is asked once, at the decision.
- ROOF GLASS (PSX_FAULT_GLASS): a ROOF hit that is not a roll reads one band softer (under 3 m/s a scuff, never a crushed roof); roof_glass is priced by stage $210 / $820 / $1,950 (x the family's flat price). Part 2b's 2.5 m/s $480 contact is a scuff.
- OWNER PC34 (PSX_FAULT_SIDEHIGH): on an upright car a horizontal push is FRONT, REAR or SIDE at any height; ROOF only for a push from above (car frame, and world n.y below -0.5 so a wall leaning over the road is not one) or a car on its side or roof. His three WallColl incidents (dv 2.1 left, 5.49 and 7.62 right) now read SIDE: a scuff, then door dents, no roof and no glass (`TestSideHighPc34`). The window/mirror flicker: every component that writes a car renderer's property block reads it first, so blocks do not wipe each other; not found (NOT DONE).
- SRS (PSX_SRS_DEALER): a spent bag set takes 12% off the value (18% both); dealer parts only (Delmar and DIY refuse).

## Gates (PSXPerf; base = 825d5ed7 by git archive, same sandbox)
| Gate | Result |
|---|---|
| Typecheck | OK |
| Full LifeSimSelfTest vs base | base 4,360 ok / 62 FAIL; head (h3) 4,409 ok / 62 FAIL; NEW 0, GONE 0 |
| Unit tests (FatesR1c) | injury table, recovery vs HEALTH, hospital + medical leave, FAIL undrivable the next day and drivable after Delmar's repair, write-off threshold + keep/junk, death locks the career, roof glass by stage, SRS, PC34: all ok |
| Play checks (hidden; '79 Civic 834 kg, belt on, no bags, HEALTH 100) | 25 km/h wall dv 7.1: nothing; 40 km/h dv 11.2: concussion, the drive ends at 4 s; 65 km/h dv 18.1 (FAIL): hospital, ends at 5 s; 130 km/h tree in a debug career dv 35.7 narrow: death, folded, obituary, career locked. ALL HOLD (`h1_crashplay.txt`) |
| Census, 3 x 150 s Gillespie Gap, 8 cars | FAILS. After the fixes: 84 incidents, 9 damage events, 5 scuff-only = 56% (95% of all incidents cosmetic or less); player $153/race, all from one 6.7 m/s REAR hit (an AI ran into it: panel s3, bar s2, lamp, $460). Before PC34: player $153 from two 1.4 m/s nose nudges, each a cracked cover (panel_damage s1 at the flat $230). The $480 ROOF is gone. |
| Pictures (4:3, the narrowest) | `r1ca_sheet.png`: hospital summary and obituary read right, nothing clipped; the concussion frame is mostly void (the play strip sits 4 km up) but shows the smear; the WRITE-OFF shot shows MAIN (my shot set the value too high for a write-off; TestWriteOff proves the page logic) and its one CLIP is MAIN's pre-existing RECENTLY line, not a fate page |

## NOT DONE
- A correct write-off picture (re-shoot with a value under the estimate) and a concussion picture over a real road.
- The census gate: panel_damage is $230 at every stage (a cracked cover should be ~$100: used cover $40 + paint $60), and the census player is an autopilot that gets rammed; needs stage pricing for panel_damage and a clean-racer definition (struck, not striking), then a re-run.
- The window/mirror flicker (next suspects: repeated brush incidents, CarHD swapping renderers at PaintGloss.Collect).
- No side-glass or mirror row for a high SIDE hit yet (the SIDE table only).
- `CrashHooks.Repair` is called at booking before the money check (a refused booking may still have asked the policy).

## At merge
Save v27 (`MigrateToCrashFates`; R1c-B's v28 goes after). New files (+ .meta): Scripts/LifeSim/CrashFates.cs, Scripts/InjuryFx.cs, Scripts/LifeSim/LifeHomeScreen.Fate.cs, Editor/LifeSimSelfTest.FatesR1c.cs, Editor/InjuryPlayCheck.cs. Shared: DamageModel.cs (ROOF case, Classify worldNy, Incident.injury, Reresolve hook), DamageRules.cs (SideHighOn), DamageLook.cs untouched, LifeSim/DamageBank.cs, DamageHomes.cs, FaultCatalog.cs (4 FAIL rows, dealer-only quote), FaultView.cs, Inspection.cs, LifeRules.cs (DriveRefusal, OrderRepair), Clubs.cs (Duty.Medical, ClockOnRefusal), LifeState.cs (fate, wreckHit, IsMiss, v27), LifeSimManager.cs, LifeHomeScreen.cs (2 lines), LifeHomeScreen.Clubs.cs (1), Room.cs (LeaveCareer internal), PauseMenu.cs; Editor: CrashPlayCheck.cs (1), DamagePlayCheck.cs (1), LifeHomePreview.cs (Shoot internal), LifeSimSelfTest.cs (1), LifeSimSelfTest.DamageR1a.cs (2), LifeSimSelfTest.DamageR1a2.cs (1). No Resources, scene, shader or City change; no re-bake.

# HANDOFF - FAULTS R1a PART 2a "THE CAR SHOWS THE HIT" - the visual half (agent "r1a2v", branch faults-r1a2-visual, 2026-11-13; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr1a2v` off mountain-nm 04c0f15f (R0a, R0b, R0c, D, R1a part 1 merged). Sandbox PSXCity (lock "r1a2v", released; car_facts.json, rg2_cars.json, DriveSide.csv copied in by path). Part 2b (rules: structural FAIL, heat soak, sweeps, census, homes, CarValue) is another agent's, in faults-r1a2r. Reports: scratchpad `r1a2v\` (runner `r1a2v.ps1`; `base_selftest.txt`, `h3_selftest.txt`, `h4_look.txt`, `h4_look_*.png`, `look_sheet.png`, `compare.py`).

## What changed (no save change: everything shown is derived from the rows the car carries and this drive's incidents)
- NEW `Scripts/DamageLook.cs` (+meta): a component on the armed career car (DamageLedger.Arm attaches it). Every 0.2 s it reads the car's rows (OwnedCar.faults, hidden or found) and this drive's incident rows:
  - LAMPS: lamp_out (zone front = headlamp, rear = tail lamp, its side) -> `CarLights.SetLampOut`: no beam in the headlight table, no cone, no lens sprite; on a lamp-map body its lens goes dark and its glow out (PSX/CarPaint `_PSXLampOut`). There are no indicator lamps in the game, so there is nothing to kill there.
  - BODY: panel_damage per zone/side (stage 1 scrape + cracked cover, 2 dent, 3 crumpled), roof_glass (1 cracked glass, 2 dented roof, 3 crushed), bumper_bar only deepens a cover that already shows (the bar is hidden by design). Per renderer through a MaterialPropertyBlock (the PaintGloss pattern, read-modify-write so CarLights' blocks survive); wheels and every AI / traffic car get nothing.
  - STEAM: radiator_leak + a hot engine: wisps from a weep at 98 C+, a hard plume from a holed / crushed core at 85 C+. One small ParticleSystem (the tyre smoke's PSX/Decal material) made the first time it applies, emitting only while it does.
  - SRS: srs_deployed -> a limp bag on the steering wheel (turns with it) and, on a dual car, one over the passenger's dash (PSX/Cabin SOLID, procedural mesh).
  - roof_glass from the seat: a star of cracks on the cockpit windscreen (a quad on the pane's plane, procedural texture).
- `SrsBag` (same file): `AfterResolve` runs after every DamageModel.Resolve in a ledger:
  - CRUSH ORDER (design 3.1's crush order; PSX_FAULT_CRUSH=0 off): a FRONT/REAR hit at band 4 takes the cover (panel stage 3) and the struck side's lamp (both on a square hit); band 5 (10+ m/s) both lamps and, in front, the radiator crushed (stage 3, leak live at once). The owner's 41 m/s Viper into a tree now reads as a wreck.
  - BAGS: a frontal hit (FRONT or a front corner, push within 30 degrees of straight back) on a car with bags (car_facts airbags) fires them with a chance ramping 0 at 3.5 m/s to 1 at 6 m/s (delta-V along the car), on the incident's own spare die (DamageModel's water die, never read by a hit). Row `srs_deployed` (hidden, stamped with R0b's cause by DamageBank like every hit row, mag = bags fired). Career car only. The SRS lamp is R2a's.
- NEW `Shaders/PSXCarDamage.cginc` (+meta), included by PSX/CarPaint: vertex push (a dent 1-2 cm, crumple up to ~8, a crushed roof ~15 toward the box centre; the collider does not move), scrapes to primer in streaks, dark cracks/creases, dent waves in the normal (so reflections show it), cracked-glass web, dark broken lens and `dmgAlive` on the lamp glow. One uniform branch on `_PSXDmgOn`: 0 on every car never hit (paint bit for bit). Works on top of the PSX_CAR_HD normal.

SWITCHES: PSX_FAULT_LOOK=0 (none of the look), PSX_FAULT_LAMPS=0, PSX_FAULT_BODY=0 (also the windscreen crack), PSX_FAULT_STEAM=0, PSX_FAULT_SRS=0 (no bag fires: no row, no look), PSX_FAULT_CRUSH=0 (no crush-order rows).

SHARED FILES TOUCHED (all one-liners or small):
- `Scripts/DamageModel.cs` (4 lines): `Incident.frontCos` field; OnContact sets it (`SrsBag.FrontCos(nL)`); Reresolve calls `SrsBag.AfterResolve(inc, Banks)` after Resolve; Arm calls `DamageLook.Attach(car, L)`.
- `Scripts/CarLights.cs`: `lampOut` mask + `SetLampOut`; the five sprite/cone enable lines add `&& !HeadOut(i)` / `!TailOut(i)`; PushGlobals skips a broken head beam.
- `Scripts/LifeSim/FaultCatalog.cs` (1 row: srs_deployed $1,320 / 2 days, the design's Delmar airbag-set price), `Scripts/LifeSim/FaultView.cs` (1 line: srs_deployed -> Body).
- `Shaders/PSXCarPaint.shader` (5 small edits: include, vertex call, the frag call after the HD normal, shine x (1 - matte), lamp glow x dmgAlive).
- NEW `Editor/DamageLookCheck.cs` (+meta): the watched picture check.
- NOT touched: EngineTemp.cs, Scripts/City, RoadsideRules.cs, Resources, scenes, save format/version.

## Gates (PSXCity; base = 04c0f15f exported by git archive, run in the same sandbox)
| Gate | Result | Report (scratchpad `r1a2v\`) |
|---|---|---|
| Typecheck | OK | `tc` |
| Full LifeSimSelfTest vs base | IDENTICAL FAILURE SET: base 4,288 ok / 62 FAIL, head 4,285 ok / 62 FAIL, NEW 0, GONE 0 (h1 had 1 NEW: srs_deployed needed an inspection-map home - fixed). The 3 fewer oks are the randomised fault-pick test choosing a one-day repair (its multi-day lines did not print) | `base_selftest.txt`, `h3_selftest.txt`, `compare.py` |
| Watched play check (DamageLookCheck, '99 Viper GTS: dual bags, lamp map; real PhysX, dice 0.1) | 16 ok / 0 FAIL. 10 mph nose-in (dv 4.7, band 2): lamp_out L, CarLights.lampOut = 1, bags fired (2). 30 km/h nose-in (dv 8.6 x1.3 crushed bar = band 5): front 1.00, both headlamps out, radiator s3; at 108 C 38 steam puffs. Rear (planted 6 m/s): rear 1.00, a tail lamp out. Cockpit: both bags + the windscreen crack. Folded: srs_deployed hidden, cause impact, zone front, $1,452 (usa), mag 2; every row hidden; the banked rows alone give the same look | `h4_look.txt`, sheet `look_sheet.png` (shots `h4_look_1_night_lamp.png`, `_2_steam`, `_3_cockpit_bags`, `_4_chase_panels`) |

## Found, not mine (for part 2b)
- A 20 km/h physical BACK-IN into a wall on the Viper GTS was classified ROOF (dv 4.6, roof_glass s2), not REAR - DamageModel.Classify's beltline test (`p.y > sh.beltY`) on a low car; the picture uses a planted REAR hit instead.

## Not done / not verified
- PSX/CarLens covers (the B lens variant) still glow their 20-25% share over a broken lamp; the road spill of the tail lamps (StreetLights) is not dimmed; pop-ups are not stuck; the game has no indicator lamps.
- The bags look full rather than limp; the night shot shows the lit right lamp and the dark left one, but the body itself is barely visible.
- Not in a player build; frame cost not measured (one uniform branch, only the career car pays); PSX_CAR_HD only compiled, not shot on an HD body; switches-off pictures not shot.

## What the owner will notice
- A crash shows on the car: scrapes on the panel that hit, a crumpled nose or tail at higher speed, a dented roof and cracked glass after a roof strike.
- A broken headlamp is dark at night - no beam on that side - and a broken tail lamp stays dark; nothing names the part.
- A cracked radiator steams from under the bonnet once the engine is hot; a holed one steams hard.
- A hard frontal hit fires the airbags on cars that have them: the bag hangs off the wheel (and the dash on dual-bag cars) in the cockpit view. An 80 mph tree reads as a wreck: both lamps out, the front at its worst, the core crushed, the bags out.
- The damage stays on the car on the next drive until it is repaired.

## At merge
No save change, no format change, no re-bake (nothing under Scripts/City or RoadsideRules.cs; the shader is not in the tile bake). New files (+ .meta): Scripts/DamageLook.cs, Shaders/PSXCarDamage.cginc, Editor/DamageLookCheck.cs. Shared one-liners: DamageModel.cs (4), CarLights.cs, FaultCatalog.cs, FaultView.cs, Inspection.cs, PSXCarPaint.shader (listed above). If part 2b adds Spares / floor-check / test-drive homes for the hit ids, srs_deployed is a seventh id.

# HANDOFF - FAULTS R1a PART 2b "RULES AND PROOF" (agent "r1a2r", branch faults-r1a2-rules, 2026-11-13; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr1a2r` off mountain-nm 04c0f15f. Sandbox PSXPerf (lock "r1a2r", released). Reports: scratchpad `r1a2r\` (runner `r1a2r.ps1`; `base_selftest.txt`, `h4_selftest.txt`, `h4_crashplay.txt`, `h3_race_census.csv` + `census.py`, `heat.py` = the cooling-curve fit). Note: a VISIBLE editor in PSXPerf stops on "Revert All Window Layouts" (the user-level layout `AppData\Roaming\Unity\Editor-5.x\Preferences\Layouts\current\default-6000.dwlt` holds a maximized window with no backup; not touched) - the play checks ran hidden (batchmode, no -Watch) and work.

## What changed (each switch 0 = part 1)
- STRUCTURAL FAIL (PSX_FAULT_FAIL), new `Scripts/DamageRules.cs`: a hit at the AI's retire line (closing or delta-V >= 17 m/s; a DROP landing at 17), a ROLL that crushed the roof, a broken knuckle (CORNER top band, 20%), a failed subframe (FRONT top band, 50%). The car loses all power (faultAccelMult 0, PlayerCarInput idles it), the HUD's tow line reads THE CAR CANNOT GO ON, the leave row is CALL TOW TRUCK (CareerDrive.NeedsTow; TOW CAR HOME in a debug career), a race marks the player retired = DNF while it goes on (RaceManager.RetirePlayer), the day's line says "out of <venue> after x km (DNF) - the car could not go on". Banked once by hitId like every incident. The owner's PC33 tree (one 41 m/s FRONT pulse) is now a FAIL.
- CRUSH ORDER (PSX_FAULT_CRUSH): from 8 m/s everything ahead of the rails is gone whatever the dice (panel s3, bar s2, the struck lamp or both square on); from 10 m/s the bar is torn away (s3) and the core crushed; bar s2 at 5-8 m/s on its die. DamageBank.SameRow: one nose (tail) panel row whichever side took it, a door per side, a lamp per side - a harder later hit raises the stage of the row the car carries (bar 1 -> 3 tested), never a duplicate.
- BODY FIRST (PSX_FAULT_BODYFIRST): DamageModel.Classify takes the patch's lowest point; on an upright car a side or end push whose patch reaches below the beltline is FRONT/REAR/SIDE (the '99 Viper backed into a wall read ROOF); ROOF stays for a patch wholly above the belt, a push from above, or a car on its roof.
- SPAN (PSX_FAULT_SPAN): a horizontal end push wholly above the belt (a truck bed) is ROOF and also FRONT/REAR (the PC33 RX-7 under a van got ROOF only).
- SCRAPE (PSX_FAULT_SCRAPE): a held contact after its 0.2 s pulse adds no delta-V but keeps its incident open from the last contact, so a swipe along 4 m sections is one incident.
- HOLED CORE (PSX_FAULT_HOLED): a crash-holed core (located model's weep) cools fully above 97% coolant and not at all at 80% (CoolingModel's 55% otherwise; R0a's path untouched). Part 1's 2 %/min took ~48 min to 112 C (fit in heat.py, 15 min at 90 C in play).
- HEAT SOAK (PSX_HEAT_SOAK): OwnedCar.parkedC/parkedDay/parkedSlot (ADDED fields, 0 = cold, no save step); EngineTemp.StampResult -> LifeRules.ApplyHeatResult parks the car at the needle; LifeHomeScreen's fill primes, the applier warms after StartCold: same block 0.25 h, next block 0.5 h (the leg before used its block), more blocks or an overnight cold; 2/3 of the extra heat shed per hour. parkedBrakeC not done (no brake temperature model).
- HOMES (PSX_FAULT_HOMES), new `Scripts/LifeSim/DamageHomes.cs`: Spares (USED RADIATOR, FRONT CLIP, BUMPER UNCRACKED, STRAIGHT FENDER, DOOR SKIN + GLASS, TAIL LIGHT + TRIM, EXHAUST CAT BACK, SHELL PANELS, kinds power/weight); floor check (radiator_leak: green drops / palm / dinner plate under the nose, names nothing); chips (RUNS HOT, RATTLES, GAUGES/LIGHTS after dark; the bar none); CarValue x (1 - panel and roof/glass mag %, cap 60%); debug effect text for every hit row (FaultCatalog entries with ids, EffectSummary, DebugCarOps.Describe - the coordinator's FaultRows/RowLine untouched).

## Gates (PSXPerf; base = 04c0f15f by git archive, same sandbox)
| Gate | Result |
|---|---|
| Typecheck | OK |
| Full LifeSimSelfTest vs base | base 4,318 ok / 62 FAIL; head 4,360 ok / 62 FAIL; NEW 0, GONE 0 (`h4_selftest.txt`) |
| 10k sweep | 9,723 incidents; centre-front 2,176, corner parts 0; FAIL under 5 m/s 0, every 17+ hit FAILs; knuckle 0.19 / subframe 0.53 of top-band hits; no duplicate rows |
| crash-play-check (hidden, '79 Civic 834 kg, dice 0.1) | all ok: walls 15/30/50 bands 2/4/5; 65 km/h wall FAIL (dv 18.1, closing 18.0), CALL TOW TRUCK, race DNF True, 0.00 m/s at full throttle; swipe 23 sections = ONE incident; ROLL banked at rest, 3 quarter-turns, roof_glass s1; 5 m drop band 4; traffic rear-end dv 6.3 vs cap 6.27, FRONT only; holed core 112 C after 8.0 min at 80 km/h (coolant 85%); RESTART (debug career) banked once, reloaded car weeps 0.714, core = banked (`h4_crashplay.txt`) |
| Race census, 3 x 150 s GillespieGap, 8 cars | 78 incidents, 8 damage events; 92% of incidents cosmetic or less, but 2 of 8 damage events scuff-only (25%); 0 FAILs; player (autopilot, 22 contacts) $237/race: a 2.5 m/s ROOF contact (roof_glass s1 at the flat $480) + a 3.3 m/s nose (panel s2 $230) - the $40 gate FAILS (`h3_race_census.csv`) |

## NOT DONE
- srs_deployed (2a's seventh id) homes: no yard label covers an airbag set (needs a Junkyard part, e.g. "AIRBAG SET" $250) and its mag meaning is 2a's; ValueMult reads panel/roof only.
- The $40 clean-racer gate: price roof_glass by stage (glass $150-300 vs roof skin) and find why a rival contact still read ROOF; the census autopilot is not a clean racer.
- A FAIL car towed home drives again next day (R1b's knuckle/subframe/rail rows make it stay broken). No drag from a crushed front (not in the design).

## At merge
No save step, no Resources/scene/shader/City change, no re-bake. New (+ .meta): Scripts/DamageRules.cs, Scripts/LifeSim/DamageHomes.cs, Editor/LifeSimSelfTest.DamageR1a2.cs. Shared: DamageModel.cs (Classify minY, OnContact span/scrape/minY, Grow/GrowInc/Hold/CheckFail, Reresolve, Tick, Resolve band 3-5 + Crush, Incident.fail, ledger Failed - beside 2a's four lines), CareerDrive.cs, EngineTemp.cs (2 lines), PauseMenu.cs, PlayerCarInput.cs, RaceHUD.cs, RaceHandoffApplier.cs, RaceManager.cs, LifeSim/CarMarket.cs, DamageBank.cs, DebugCarOps.cs (2 lines in Describe), FaultCatalog.cs, FaultSymptoms.cs, LifeHomeScreen.cs (1), LifeRules.cs (1), LifeState.cs, Spares.cs; Editor/DamagePlayCheck.cs, LifeSimSelfTest.DamageR1a.cs (1).

# HANDOFF - FAULTS R1a PART 1 "HITS LAND WHERE YOU HIT" - the core half (agent "r1a", branch faults-r1a, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr1a` off mountain-nm 324afc17 (R0a, R0b, R0c, D merged). Sandbox PSXCity (lock "r1a", released; car_facts.json + .meta copied in by path). Design: art workspace `Fault_System_Design_2026-11-11.md` (OWNER'S ANSWERS 1 and 8; Â§2 save rules and "Size and timing"; Â§3.1 located crashes, banking, zone tables; Â§4 BODY/COOLING rows; Â§9; the R1a row of Â§10). Reports: scratchpad `r1a\` (runner `r1a.ps1`; `base_selftest.txt`, `h*_selftest.txt`, `h*_damage.txt`, `h*_crashplay.txt`, `h*_solo_census.csv`, `h*_race_census.csv`; `compare.py`, `census.py`).

## What changed (switch: PSX_FAULT_LOCATED=0 = R0a's stop-gap table at the hit, i.e. the base; PSX_FAULT_CRASH=0 still = the crash from before R0a, which turns this off too)
- NEW `Scripts/DamageModel.cs`. WHERE: every contact in the car's own frame (CarBody.box): FRONT / REAR / SIDE L R (R0a's Classify and centre band), the four CORNERS (patch within tyre radius + 0.15 m of a live wheel hub, in plan; a corner wins), UNDER (the car pushed up from below, outside the tyre patches - an upright car's tyre patch is the suspension's), ROOF (pushed down from above, or a horizontal hit above the beltline = the shell's measured cowl). It sees roof landings and belly strikes before the world-up test, which stays only for DamageScore (AI retirement and the carHP summary unchanged). HOW HARD: delta-V = normal impulse summed over 0.2 s after a collider's Enter / mass, capped at closing x m_other/(m_car+m_other) x 1.2 (traffic 1,400 kg, wall infinite); after 0.2 s a held contact is a scrape (adds nothing); pulses within 4 m and 0.6 s in one zone are ONE incident (its hardest pulse); trees/poles a band up, glancing (<30 deg) a band down (bottom band: a scuff only); a crushed bar x1.3 in its zone. WHAT: the Â§3.1 tables, cumulative, one die per row, re-read on the same dice as a pulse grows (monotone, tested). Rows written: panel_damage (cover cracked / door dent / crushed, value % in mag), bumper_bar (hidden, x1.3), lamp_out (side), radiator_leak (stage 1/2/3 = 0.5/2/6 %/min), exhaust_damage (power x0.97, live), roof_glass (stage 1/2/3 = glass / roof dented / roof crushed). Corner columns (toe / tie rod / arm) write the existing `alignment` row through R0a's one-alignment rule (minor 0.042 / severe 0.14 deg, struck side) until R1b's per-wheel corners; bump-stop strikes at 6/8/10 m/s read the corner columns. Scuffs and dents move paint/body lanes as R0a's did. Rows of later rounds (SRS, oil pan, frame/rails, fan, condenser, mounts, catalyst, lines) are not rolled.
- LIVE: the radiator weeps at once through EngineTemp.radiatorCond/radiatorWeep (R0a's path), the pull through hitSteerPull, the exhaust through faultAccelMult; the next drive carries the leak (DamageLedger.Arm reads the car's radiator_leak row) and a crushed bar (its bumper_bar rows).
- ROLL / DROP / WATER, banked AT THE EVENT (no respawn in careers since R0c): ROLL from the first big tilt to rest (quarter-turns from the swept tilt, its roof pulses inside it, the ROOF table at the hardest roof pulse, at least band 1 once over on the roof; banked once while it lies there); DROP = a fall of 1.5 m or more, landing speed max(measured, sqrt(2gh)), the UNDER table at landing - 3 m/s (frame band over 8 m/s), its belly pulse inside it, the stops striking on their own; WATER = origin 0.5 m under the drawn sea: flood title, hydrolock 50% with the engine running through EngineTemp.Seize (story names no part). The tow / QUIT then banks the leg as R0c does; nothing is banked twice (hitIds).
- NEW `Scripts/LifeSim/DamageBank.cs`: the small damage file `<saveKey>.dmg.<carId>.<drive>.json` in LifeSimManager.CopyDir (990 bytes for two incidents), written by the ledger (debounced 2 s; flushed on exit stamps, a swap, OnDestroy and app pause). Folded by hitId at every exit stamp (CrashStopGap.StampHandoff: finish line, QUIT/tow BankLeg, city exits), PauseMenu EXIT and RESTART (debug only: reloads the damaged car), the apply-back (ApplyRaceResult) and Load (a drive the game never finished). A file is deleted only after the save that carries it (LifeSimManager.Save -> AfterSave). Banks onto the car the file names - never ActiveCar; not armed on a test drive or in the CITY edition. Every row hidden, stamped with R0b's cause (impact, zone 1-6, side, delta-V in hitMps) plus hitId, stage, mag, corner1.
- SAVE v26 = `LifeSimManager.MigrateToDamageBank` (CarFault.hitId/corner1/stage/mag, OwnedCar.history (<=48 CarEvent)/title, CarEvent): every row staged from legacy severity (1 -> 2, 2 -> 3), empty history, clean title; idempotent. Heat soak's parkedC etc. (part 2) read 0 as cold and need no step.
- `CrashStopGap.BumperFor` reads `CarFacts.Get(spec).bumperEra` (chrome / 5mph / else plastic); no facts row: era + origin as before.
- R1a rows get homes: FaultCatalog hit rows (outside the pools: no wear, market or used roll can make them; priced per family), FaultClass (body; radiator_leak cooling), inspection map (PANELS & BUMPERS, RADIATOR CORE, EXHAUST).

## Gates (PSXCity, base = 324afc17 exported by git archive and run in the same sandbox; head = this branch)
| Gate | Result | Report (scratchpad `r1a\`) |
|---|---|---|
| Typecheck | OK | `tc` |
| RunDamage: TestImpactZones, tables, live, TestDamageBank, v26 | 40 ok / 0 FAIL. 52 of 52 shells: nose centre/left/right, tail, both doors, four arches, belly, roof, pillar where they are (front 156, rear 52, side 104, under 104, roof 156, corner 208); the 208 vertical contacts the world-up test drops now land in UNDER/ROOF. Bands, 5-mph/chrome ratings, pole +1, glancing -1, bar x1.3; monotone over 6 zones x 200 dice x 5 bands. Live: same crash re-read upward, holed core weeps 2 %/min at once, FR corner pulls 0.140 deg. Bank: one 990-byte file, folded once by hitId, refold no change, file deleted after the save, same file twice banks nothing, QUIT banks once (and the apply-back after it nothing), a file for a car the career does not own banks nothing, CITY edition untouched, a leftover file banked at the next Load, a test drive arms nothing | `h2_damage.txt` |
| Full LifeSimSelfTest vs base | NOT IDENTICAL: base 4,244 ok / 62 FAIL, head 4,276 ok / 70 FAIL; the 62 the same, 8 NEW, 0 GONE. 7 NEW = R0a's save-safety tests pinned to "the code is at v25" (LastUnstampedVersion + 1): a new career born at v25, migrates to v25, and the old-save backup case builds its old save at CodeVersion - 1 = 25 with no writer stamp, which now reads as Rewritten (.bak v25, a second copy, NEWBIE). Any v26 bump trips them: re-pin them to LifeSimManager.CodeVersion / build the old save at LastUnstampedVersion. 1 NEW = the save tripwire flagged DamageBank.cs: its header comment named the copies folder; reworded in the last commit, NOT re-run | `base_selftest.txt`, `h2_selftest.txt`, `compare.py` |
| crash-play-check (R1a mode, real PhysX, '79 Civic CX 834 kg, dice forced) | 18 ok / 2 FAIL. Walls at 15 / 30 / 50 km/h: ONE FRONT incident each, delta-V 4.30 / 8.49 / 14.14 against closing 4.2 / 8.3 / 13.9 m/s, bands 2 / 4 / 5. 5 m drop: ONE DROP at 10.6 m/s, band 4 (exhaust s2, body), the belly pulse inside it, one stop strike on its own. All 7 incidents folded onto the career car once, the files deleted, every row hidden. FAIL: the swipe touched only one 4 m section (one glancing scuff on the right), so the merge across sections was not exercised in play. FAIL: the 60 km/h roll launch never rolled the car (no incident in 12 s); ROLL banking is unverified in play | `h2_crashplay.txt` |
| Clean lap with zero damage events; race delta-V census / 3-race sample | NOT RUN (budget). The census hook exists (Editor/DamagePlayCheck.cs: PSX_DAMAGE_CENSUS=<csv> makes every responder a non-banking census ledger; runner job `solo` / `race` in `r1a.ps1`, summary `census.py`) | - |

## NOT DONE (part 2)
SRS / airbags; body masks and lamp-light kills (lamp_out is a hidden row with no light effect yet); steam; heat soak (parkedC etc. read 0 as cold: no save step needed); the structural FAIL (17 m/s, roof crush, knuckle / subframe -> tow line, DNF; WATER's hydrolock already seizes through EngineTemp); the 10k-incident sweep; the 112 C radiator timing gate; the traffic rear-end check; the census and clean lap above; an in-play roll and a multi-section swipe; re-pinning R0a's 7 save-safety tests; a re-run after the comment fix; the rows' value (mag) in CarValue (R3); Spares / floor-check / test-drive homes for the six new ids; RESTART's reload of the damaged car is written, not driven.

## What the owner will notice
- Tapping a wall at walking pace leaves a scuff. At about 10 mph nose-first the bumper cover cracks, a headlamp can go, and the radiator may weep, so the needle creeps up later in the drive and on the next drives until it is fixed. At 20-30 mph the radiator is holed and the body takes real damage.
- A hit at a wheel arch, or a hard bump-stop strike, knocks the alignment toward that side; a square hit in the middle never bends a corner.
- Dropping off a ledge, rolling or driving into the water costs: exhaust and floor, roof and glass, a flood title, and a dead engine if it was running.
- Damage is kept the moment it happens: QUIT, the tow truck or the game closing do not undo it, and nothing is counted twice. Every new problem stays hidden until a person finds it.

## At merge
Save v26 (`MigrateToDamageBank`; if another round takes 26 first, renumber it, `LifeState.saveVersion` and the step's `CodeVersion < 26` guard). Copy `Resources/car_facts.json` (+ .meta) into the build sandbox by path (BumperFor reads it; without it, era + origin as before). Nothing under Scripts/City or RoadsideRules.cs; no Resources, scene, shader or ProjectSettings change; no re-bake. Shared files: CrashStopGap.cs, CollisionResponder.cs, EngineTemp.cs, StuckRecovery.cs (one getter), PauseMenu.cs, LifeSim/LifeState.cs, LifeSimManager.cs, LifeRules.cs, FaultCatalog.cs, FaultView.cs, Inspection.cs; Editor/LifeSimSelfTest.cs (one line), LifeSimSelfTest.CrashR0a.cs and .RoomR0c.cs (R0a's ledger tests run with PSX_FAULT_LOCATED=0), CrashPlayCheck.cs (dispatch). New (+ .meta): Scripts/DamageModel.cs, Scripts/LifeSim/DamageBank.cs, Editor/LifeSimSelfTest.DamageR1a.cs, Editor/DamagePlayCheck.cs.

# HANDOFF - FAULTS ROUND D: CAR FACTS + race/rally cars off the career market (agent "factsd", branch faults-d, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultd`, branch faults-d off mountain-nm 7ae0ec13. Sandbox PSXRec (lock "factsd", released). Design: art workspace `Fault_System_Design_2026-11-11.md` (section 2 "Catalog facts", 3.5, the D row of section 10; OWNER'S ANSWERS 9). Reports: scratchpad `factsd\` (`base_selftest.txt`, `head_selftest.txt`, `head_carfacts.txt`, `compare.txt`).

## What round D adds
- `Assets/PSXRacing/Resources/car_facts.json` (+meta): one row per catalog car, all 319, keyed by spec id. Fields: origin + family (jpn/usa/eur: the MAKER's home - RG2's catch-all origin "eur" is corrected on Dome, Sileighty, Spoon, NISMO x4 (jpn) and Chaparral x2, Panoz (usa)), market (US / JDM / EUR / UK; RACE on every built-to-tune row), year, builtToTune, roadLegal, serviceUnit (miles / race hours), priceClass (economy 1.0, sport 1.15, exotic 2.5, race 3.0), stockInternals (highest power stage the stock bottom end takes) + stockBoostBar, engineCode, fuel (carb / mech / efi), eSys (CARB, MECH, EFI-OBD1, EFI-OBD2, EFI-JDM, EFI-RACE), obd (0 none, 1 maker's own, 2 OBD-II), obd1Read (gm_jumper, toyota_te1_e1, ecu_led, analog_meter, jumper_lamp, dealer, none), camDrive + beltMi + interference, ignition, turbo, airbags, rearBrakes, bumperEra (chrome / 5mph / 2.5mph / plastic), ac (R-12 / R-134a / none), partsSource + partsWaitMin/Max (US next morning; JDM-only and European specials up to 21 days; race parts 14-28), knownIssues (free text for the later knownFailures ids), lowConf (the fields the author was unsure of), note.
- Built by `tools/bake_car_facts.py` from the catalog (rg2_cars.json) plus authored engine and car tables; `--check` validates without writing. Re-run it after any catalog change: it refuses to write if a catalog row has no facts row.
- `Scripts/CarFacts.cs`: the loader later rounds read (`CarFacts.Get(spec)`, `RoadLegal`, `CareerSells`, `PartsWaitDays`, `PriceClass`, `CarFact.HasObd2 / HasCodes / HasAirbag / HasTimingBelt / IsInterference / ServicedByHours / LowConfidence(field)`). Without the file it falls back to the catalog's built-to-tune rule.
- OWNER'S ANSWER 9: race and rally cars leave the career market. `CarMarket`: the newspaper, the dealer lot (it kept out only "Race Car" rows - rally, touring and JGTC cars could stand on it) and the starting lanes (a well-paid start's NEW lane could be a new $40k rally car) offer no car that is not road legal; adverts an older build placed are withdrawn at the next refresh. Quick race and debug are untouched; a career that owns one keeps it. Switch `PSX_FACTS_MARKET=0` = today's market.
- NOT BUILT (note for the car-clubs work): **rally cars become a LOANER perk at a car club's max rank** (owner, answer 9). The loaner needs the phantom-car rules (Clubs.cs loaner perk, `live: false`; design section 7 rental).

## Counts
319 rows; 53 not road legal = the 51 built-to-tune rows (23 race, 22 rally, 6 touring/JGTC/dirt) + 2 authored (Shelby GT350R '65 - Shelby's competition model; Lotus Motor Sport Elise '99 - the one-make race-series car; both OWNER: confirm). 14 OBD-II rows (every US-market 1996+ EFI road car, nothing else). 50 carburettor and 12 mechanical-injection rows, none with codes. 236 rows carry at least one low-confidence field (mostly interference / ignition per engine and the A/C switch-over years).

## Gates
| Gate | Result |
|---|---|
| Typecheck (PSXRec) | OK |
| `LifeSimSelfTest.RunCarFacts` (PSXRec; once on WebGL, once on Android) | 37 ok / 0 FAIL both times (`head_carfacts.txt`, `android_carfacts.txt`) |
| ... every catalog row present exactly once | 319 / 319, 0 missing, 0 duplicated, 0 extra |
| ... OBD-II only on US-market 1996+ rows | 14 OBD-II rows, all US-market 1996+ EFI; and every US-market 1996+ EFI road car has it |
| ... no codes on carburettor rows | 50 carburettor + 12 mechanical-injection rows, 0 with codes |
| ... airbags by market and year | 143 rows with a bag; 0 before 1987 (US, JDM) or 1981 (Europe), 0 on race cars, every US 1998+ car dual |
| ... built-to-tune not road legal | 51 / 51 not road legal, RACE market, race hours (+2 authored: 53) |
| ... design pins | '96 240SX OBD-II; Civic Type R EK none; '69 Camaro SS carb, no codes, chrome; '99 Viper OBD-II, dual bags, next-day parts; 787B race hours / 3.0 / 14-28 days; FD rotary + sequential; 2JZ-GTE belt, non-interference; 3.4 SHO chain, interference |
| Newspaper never lists a race or rally car over 200 seeded days | 3 seeds x 200 days: 638 listings in the paper and on the lot, 0 race/rally. Teeth: with `PSX_FACTS_MARKET=0` the same days list 92 of 631 |
| ... older adverts, owned cars, starting lanes | a rally advert an older build placed leaves paper and lot at the next refresh; an owned race car is kept over 30 days of refreshes; 720 starting lanes, 0 race/rally (35 with the switch off - the NEW lane offered new rally cars) |
| Full LifeSimSelfTest, base 7ae0ec13 vs head, same sandbox and target | base 4,147 ok / 63 FAIL; head 4,179 ok / 63 FAIL: the same 63 by check text, 0 new, 0 gone (`compare.txt`). Ok-line delta: +37 car facts; night look -5 = two scene checks (wet road, night windows) SKIPPED in the head run because the mirrored worktree's PSXRacingBuilder.cs has a newer file time than the sandbox's built scenes (a sync artefact; this round does not touch the builder) |

SANDBOX TARGET: `Invoke-UnityJob` (tools/unity-wait.ps1) adds `-buildTarget WebGL` unless told otherwise, so my base job moved PSXRec from Android to WebGL. The head self-test ran on WebGL too (a fair comparison) and the last job (RunCarFacts) ran with `-buildTarget Android`: PSXRec is back on Android. Lesson for the next agent there: pass `-buildTarget Android` explicitly.

## Judgement calls the owner should see (spot check: art workspace `Car_Facts_SpotCheck_2026-11-12.md`, 30 rows)
- Both "Nissan 240SX '96" rows are GT4's US names for JDM cars (180SX Type X and Silvia K's S14, SR20DET). The design says "the '96 240SX has [OBD-II]", so they are US market (OBD-II, US bumpers) with Japanese engine-parts waits (3-7 days). Owner: confirm.
- GT4's other US renames are JDM cars and are marked JDM: Lexus GS300 '91 (Aristo), SC300 '97 (Soarer), IS200 (J) '98 (Altezza), 3000GT (J) (GTO), 300ZX (Fairlady Z).
- Concept / prototype road cars (Camaro IROC-Z Concept, Mitsubishi HSR-II, Dome Zero) stay road legal because the catalog treats them as road cars.
- Catalog data that does not match the real car (noted in the rows, not changed): Buick Special '62 (7456 cc DOHC / 564 PS in the catalog), Honda Accord Coupe Type S (id says 03, name 99, figures of the Acura CL Type-S), Corvette Z06 C5 and MR2 Spyder dated '99 (2001 / 2000 cars).

## Not verified
- No player build, no play check (nothing in play reads the facts except the market). The facts are authored from general knowledge with no web lookups; the validator checks the rules, not each fact. Gauges (damped / true temperature gauge, oil lamp or gauge) and knownFailures fault ids are not authored: R2a / R4 own them once faults_v2.json exists.

## What the owner will notice
- Race and rally cars no longer show up in the newspaper, on the dealer lot or as a new starting car. Quick race and the debug garage still have all of them, and a race car you already own stays yours.
- A table in your art folder (`Car_Facts_SpotCheck_2026-11-12.md`) lists 30 familiar cars with their facts: mark any wrong row. Three rows need your call (the 240SX market, the GT350R and the Motor Sport Elise as race cars).
- Nothing else changes in play yet: later fault rounds read these facts (warning lamps and codes, airbags, bumper damage, timing belts, parts waits, tuning wear).

## At merge
- Shared files: `Scripts/LifeSim/CarMarket.cs` (RefreshListings +5 lines, RefreshLot +1 and one changed condition, EnsureNewStock one changed condition, RollStartingLanes +3 lines), `Editor/LifeSimSelfTest.cs` (one line in RunAll). New: `Scripts/CarFacts.cs`, `Editor/LifeSimSelfTest.CarFacts.cs` (entry point `LifeSimSelfTest.RunCarFacts` -> `PSXRacing_selftest_carfacts.txt`), `Resources/car_facts.json`, `tools/bake_car_facts.py` (+ .meta files with fresh GUIDs).
- NEW RESOURCES FILE: build sandboxes never receive a worktree's Resources by mirror - copy `Assets/PSXRacing/Resources/car_facts.json` and its .meta into the build sandbox BY PATH. Without it the game still runs (the market falls back to the catalog's built-to-tune rule; only the two authored race cars would stay for sale).
- No save version, no City code, no tile re-bake. `CrashStopGap.BumperFor` (R0a) still derives the bumper from origin + year ("until car_facts carries one"); R1a can switch it to `CarFacts.Get(spec).bumperEra`.
# HANDOFF - FAULTS R0c "ROOM FOR TWO" + OWNER'S ANSWERS 1 AND 8 (agent "r0c", branch faults-r0c, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr0c` off mountain-nm 7ae0ec13 (R0a merged). Sandbox PSXCity (lock "r0c", released). Design: art workspace `Fault_System_Design_2026-11-11.md` (OWNER'S ANSWERS 1, 4, 8; Â§8 "Room" and "Selling"; Â§10 R0c row). Reports: scratchpad `r0c\` (runner `r0c.ps1`). NO SAVE CHANGE: no new LifeState/OwnedCar field, no version step (v24 stays current); the trade-in quote is held in memory for the day.

## What changed (switches, each 0 = today's behaviour)
- ROOM (`PSX_ROOM_FOR_TWO`): NEW `Scripts/LifeSim/Room.cs`. `Room.GarageSlots(s)` (= `LifeRules.GarageSlots`) = the house rung's garage (`LifeRules.Housing`, read from housingType) + 1 yard spot (the lawn bay the home street already draws), never less than the stored garageSlots (debug careers keep 6). A v24 one-car career ("house1g", stored 1) has room for 2 with no save change. Every purchase gate and slot display reads it: CarMarket.Buy, Viewings.Adopt, the market/dealer/buy/viewing pages (LifeHomeScreen), GarageWorld's empty-bay prompt.
- MOVE HOUSE (`PSX_MOVE_HOUSE`): under BILLS. Moves to any other rung: the new place's first month as deposit + a $45 truck, one block; rent changes from the next bill (monthlyHousingCost); refused if the cars would not fit. The walk-in still draws the neighbourhood's one house (extra cars stand on the lawn bays it already lays out) - a real 2/3-car house needs house art and garage bays in the Neighborhood scene (GarageSceneBuilder/HomeArrival/CarWhere.GarageBays), not built.
- TRADE-IN (`PSX_TRADE_IN`): on CRESTLINE's own page (reached only from its door in town = the visit). HAVE IT APPRAISED (15 MIN) on the car you came in -> 60% of today's lane-based CarMarket.CarValue (reads no fault, so a hidden fault cannot move it); the quote holds for that car until the day ends (in memory: quitting the game drops it, a new visit re-appraises). SELL IT TO CRESTLINE (not the only car; quote less the loan). The ONLY car goes in PART EXCHANGE: extra buy rows on a Crestline lot car ("... + YOUR CAR"); the old car leaves, the new one is the car you drive out (works with a full garage too). The market page's QUICK-SELL 50% is gone while the switch is on.
- JUNK IT (`PSX_JUNK_IT`): on the yard's page: "WE BUY CARS FOR SCRAP - WE TOW IT AWAY", one row per car, scrap = $0.04/kg + $40 converter (1975+), less any loan; press twice; refused for the only car, a car at a shop or with work on it, or one upside down on its loan. Loans settled, a spare going on back to the shelf, jobs/ads dropped (QuickSell's own cleanup).
- ANSWER 1 (`PSX_NO_RESTART`, `PSX_QUIT_KEEPS`): NEW `Scripts/CareerDrive.cs`. RESTART RACE only where `CareerDrive.RestartOffered`: debug careers, quick race, the CITY edition, standalone editor races; never in a non-debug career (PauseMenu was the only RESTART; the arcade/CITY "AGAIN" buttons are not careers). QUIT from a career RACE banks the leg from the seat before ResultReady is cleared (`CareerDrive.BankQuitLeg` -> the finish line's stamps for distance so far, drift, the R0a crash outcome via CrashStopGap.StampHandoff, tank, gauge -> the ordinary `LifeRules.ApplyRaceResult` as a no-purse, no-block leg -> CrashBank, wear, fuel, heat, bump stops, distance wear rolls); diary: "quit <venue> after x km - the car keeps what happened". Free roam's END DRIVE already banked its session (StampExitResult + R0a's StampCityExit) and still does. QUIT after the flag now keeps the stamped result (it is CONTINUE). Test drives never bank onto the player's car (BankSeizureNow's guards). Debug RESTART is unchanged (throws the leg away; R1a makes it reload the damaged car).
- ANSWER 8 (`PSX_NO_RESET`): in a non-debug career nothing puts the PLAYER's car back on the road: no R / pad X / Select / wheel reset (PlayerCarInput), no RESET CAR row (PauseMenu), no StuckRecovery auto-reset when rolled, pinned, beached, in the water or fallen below the road. The car stays put, StuckRecovery latches `Stranded` (survives the pause; lets go when the car moves / a wheel is back on a road) and prompts "ON ITS ROOF / STUCK / IN THE WATER / OFF THE ROAD + PRESS ESC (START, TAP MENU) - CALL TOW TRUCK"; off-track says "DRIVE BACK TO THE ROAD". The pause menu's leave row becomes "CALL TOW TRUCK - $fee" whenever the car cannot get itself home (stranded, or the engine seized; RaceHUD's seized banner says so): $55 hook-up with 5 miles, then $2.50/mile (town 4 mi $55, Charlotte 10 mi $67, mountain stages 100 mi $293, Bogue 270 mi $717); a driver who cannot cover it pays what is in the wallet. Then the drive ends exactly as QUIT/END DRIVE (the leg banked) and the car is home. KEPT: the under-the-map-floor backstop respawn (a hole in the world), AI recovery (AIDriver), and the old behaviour in debug careers, quick race, the CITY edition and standalone races. RaceManager gained a one-line PlayerDriftSeconds getter.

## Gates
Sandbox PSXCity, code synced from the worktree (head 412ea4f0 for the self-test and play checks; the one later commit, 1c5ac5f9, only shortens the MOVE HOUSE button text). Base = r0a-gate's full self-test of the R0a merged code in the same sandbox (`r0a-gate\gate_selftest.txt`, code identical to 7ae0ec13 - `git diff 9d129960 7ae0ec13` is HANDOFF.md only - and PSXCity untouched since).
| Gate | Result | Report |
|---|---|---|
| Typecheck | OK (every run) | `r0c\tc` |
| Full LifeSimSelfTest vs base | 62 FAIL both, the same 62 by check text: NEW 0, GONE 0 (`r0a-crash\compare_fails.py`); 4,115 ok | `r0c\head2_selftest.txt` |
| R0c self-test (in RunAll; RunRoom alone) | 45 ok / 0 FAIL in the full run | same; first solo run `r0c\head_room.txt` (1 FAIL = a test that planted a rear hit too soft for a body loss; fixed) |
| v24 one-car career buys a 2nd car | saveVersion 24, stored 1 slot + house1g -> derived room 2; buys from the paper; a 3rd refused "garage full (2)", the lot too; debug keeps 6; switch off -> "garage full (1)" | self-test |
| Trade-in needs a visit; quote = 60%; hidden fault does not move it | no appraisal -> refused; $2,293 of $3,822; a planted hidden fault 2293 -> 2293; a hidden fault + a 40-point dent after the visit: quote unchanged all day; next day gone; the ONLY car goes in part exchange (old car out, lot car active, money right); 2nd car sold outright less its loan | self-test |
| Junking pays scrap, car leaves | 1,148 kg -> $86, paid, car gone, keys moved; only car refused; upside-down loan refused | self-test |
| QUIT keeps a planted crash: race and free roam | race QUIT from the seat: hidden bumper + alignment rows, body 80 -> 50, 3.1 mi, fuel 60 -> 51.6, no purse / race / block, diary "quit ..."; switch off banks nothing; END DRIVE (stand-in CityMode, R0a's way): bumper row, body 80 -> 50; play check: the tow's quit banked bumper + alignment, body 80 -> 50 | self-test, `r0c\head2_tow.txt` |
| RESTART absent outside debug careers | rule: career no / debug yes / quick race + standalone yes / switches off yes; source tripwire: the one RESTART row (PauseMenu) reads RestartOffered; play check pause rows: MENU, RESUME, FUEL TRUCK, QUIT RACE (GO HOME), OPTIONS | self-test, tow check |
| Rolled career car not respawned; CALL TOW TRUCK works (visible editor, Gillespie Gap) | TOW CHECK ALL HOLD, 18 ok: 8 s on its roof, teleports 1 -> 1, moved 0.0 m (old watchdog: 4.5 s); prompt "ON ITS ROOF / PRESS ESC - CALL TOW TRUCK"; row "CALL TOW TRUCK - $293"; $600 -> $307; crash banked once; front end loaded; at home 1 bumper row, fee paid once | `r0c\head2_tow.txt` |
| Every player-car respawn gated | 6 call sites read the rule (PauseMenu:186, PlayerCarInput:167/228/260, StuckRecovery:289/410) | self-test |
| Handling play check | 54 ok / 1 FAIL, identical to base (the base's RUF CTR lane change, 10.2 deg / 24.4 deg in both) | `r0c\head2_handling.txt` vs `r0a-gate\gate_handling.txt` |
| Parked play check | 44 ok / 0 FAIL, the same as base (a debug career: unaffected by design) | `r0c\head2_parked.txt` vs `r0a-gate\gate_parked.txt` |

## Not verified / not built
- NOT DONE: the clipped-text picture pass of the new rows (dealer trade-in block, part-exchange rows, the yard's JUNK rows, MOVE HOUSE) - `LifeHomePreview.Capture` renders every tab at every aspect with the clip audit; not run (coordinator's budget stop). Widths were estimated by hand only.
- NOT BUILT: "to the mechanic if it will not run" - the tow takes every car HOME (a seized engine arrives home as it does today); a shop intake job (a PendingPart that holds the car at Delmar) is needed for that. MOVE HOUSE's own houses (the same house is drawn).
- No player build, Android or WebGL. Nothing clicked by hand: the trade-in, part exchange, junk and move pages are proven by the self-test's logic, not by a UI drive. The tow/QUIT path was play-checked on a mountain stage (RaceManager); a tow from Charlotte or the town (CityMode) goes through END DRIVE's existing exit and is proven only in edit mode (R0a's stand-in CityMode). The play check's quit distance (4.0 km) is the progress index read at the test's teleport, not driving.
- Decisions left for the owner: a QUIT race still costs no block and does not use up the day's purse race (today's rule) - say if a quit should count as a DNF; a test drive has no RESTART/reset either and its tow is paid by the player (the seller's car's damage is not banked); the trade-in quote lives in memory (quitting the game drops it).

## What the owner will notice
- His one-car career can buy a second car now (garage + yard); BILLS has MOVE HOUSE to the 2- and 3-car houses (same house drawn).
- At Crestline: "have it appraised" (free, 15 min), then sell it there at 60% or trade it against a car on the lot - even his only car. The yard buys junk cars for scrap.
- No RESTART in his career. QUIT keeps the dents, wear and fuel; it pays nothing.
- A rolled or stuck car stays rolled; ESC -> CALL TOW TRUCK (about $67 in Charlotte, $293 from the mountains) ends the drive and the car is home with its damage.

## At merge
- No save version, no Resources/Art/scene/shader/ProjectSettings change, nothing under Scripts/City or RoadsideRules.cs: a player-only PC build is possible.
- New: Scripts/LifeSim/Room.cs, Scripts/LifeSim/LifeHomeScreen.Room.cs, Scripts/CareerDrive.cs, Editor/LifeSimSelfTest.RoomR0c.cs, Editor/TowPlayCheck.cs (+ their committed .meta).
- Shared files touched (small, local edits): PauseMenu.cs, StuckRecovery.cs, PlayerCarInput.cs, RaceHUD.cs, RaceManager.cs (1 getter), LifeSim/LifeRules.cs (1 forwarder), LifeSim/CarMarket.cs (1 gate), LifeSim/Viewing.cs (1 gate), LifeSim/LifeHomeScreen.cs (slot reads + 5 hook lines), OnFoot/GarageWorld.cs (1 read), Editor/LifeSimSelfTest.cs (1 line in RunAll).
# HANDOFF - FAULTS R0b "STOP THE LEAKS" (agent "r0b", branch faults-r0b, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultr0b` off mountain-nm 7ae0ec13 (R0a merged). Sandbox PSXPerf (lock "r0b", released). Design: art workspace `Fault_System_Design_2026-11-11.md` (OWNER'S ANSWERS, Â§1, Â§2, Â§4 "How the RG2 ids map", Â§6 "Seller viewings"/"Closing the leaks", Â§7, Â§9, the R0b row of Â§10) plus the coordinator's 11-12 add (a stored CAUSE on every fault row). Reports: scratchpad `r0b\` (runner `run.ps1`; base_selftest.txt, h2_*.txt, h3_*.txt).

## What changed (switches; each =0 is today's behaviour; the text fixes have none)
- ONE GATE: new `Scripts/LifeSim/FaultView.cs` - `FaultView.Known/KnownCount/KnownList` replace both KnownFaults copies (LifeHomeScreen, Viewings) and the hand-written hidden filters (garage list, viewing list, pre-race list, 3D garage bench, spares). TestNoPartLeak's tripwire keeps `.hidden` out of the screen files.
- NO EFFECT NUMBERS: the garage, viewing and pre-race fault rows show no "accel x0.93" (a debug career keeps them); the HUD heat shout is "ENGINE OVERHEATING / EASE OFF OR IT WILL LET GO" (no "POWER CUT x%").
- NO COOLING PART NAMES OR FIGURES outside inspection: garage cooling line ("The cooling looks serviceable." / "Cooling system tired - ..." / "The overflow tank reads low."), the COOLANT bar is a word like the other bars, the mechanic's cooling rows show price only (no "45% now"), the pre-race line ("Overflow tank low - top it up ..."; "the cooling is done", no explanation), the result note drops "COOLANT DOWN TO x%", the seizure story says only what the needle did and how long it was flat out. R0a's f60143fb lines are now unconditional (superseded).
- DEBUG INFO only in a debug career (PauseMenu via `FaultLeaks.DebugInfoAllowed`; a drive with no career under it keeps it). `PSX_FAULT_DEBUGGATE=0`.
- FLOOR CHECK describes the puddle (colour, where, how big) and names nothing; the fault stays hidden; cooling_fail (green) and used_fluids_low (drops) join the list; air suspension = "one corner sits lower". `PSX_FAULT_PUDDLE=0`.
- TEST DRIVES (`PSX_FAULT_FELT=0`): no dice reveal; one drive per visit-day; new `TestDriveFeel` (added to the car by RaceHandoffApplier on a test drive only) measures what the drive did (5 s over 30 km/h, 3 s full throttle, 2 s over 3,500 rpm loaded, 2 shifts, a stop from 60, 2 s cornering at 0.3 g, slow full lock, needle at 105 C, 10 s moving, night). NAME WHAT YOU FELT: 10 fixed chips on the viewing page after the day's drive; a TRUE chip (a fault on the car shows on it and the drive met its condition) takes that fault's tier off the current offer, naming nothing (the fault stays hidden, the visit remembers it as felt); a FALSE one ends the haggle and the claims for the day.
- SERVICES BY PART (`PSX_FAULT_BYCLASS=0`): new `FaultClass` gives every RG2 id a class. OIL CHANGE clears only used_fluids_low, TUNE-UP spark plugs, NEW TIRES tire_wear, BODY PATCH/FULL BODY WORK dents and cut-out rust, ROTATION/TOUCH-UP nothing; a service cancels only booked repairs of what it cleared. A seizure removes only engine-internal rows; a rebuild or swap engine-internal + long block (gearbox, charging, sensors stay; cooling_fail goes only if radiator and hoses are both sound). A respray clears the finish row (paint_fade); rust stays.
- USED ROWS OWN IDS (`PSX_FAULT_USEDIDS=0`): "Engine Rebuild Needed"/"Engine Replacement" -> `used_engine_worn` (keeps trans_slip's power and fuel effect, no shift delay; home: HEAD GASKET sub; DONOR ENGINE spare), "Fluid Top-Off Needed" -> `used_fluids_low` (oil_leak's effect; home: OVERFLOW TANK). Remapped in code at catalog load - no Resources file changed. Ad phrases map to matching ids (FaultIds.ForProblem by family); the paper prints "Rusty bodywork" instead of "Cracked windshield" (no glass row until R1a; an old paper's windshield adds no row); the advert's own problem is no longer taken off the visit price twice.
- STARTER LINE: "the <car> <symptom>" from `FaultSymptoms.StarterSymptom` ("idles up and down once it's warm", "smells of hot oil after a drive"), no "worth an inspection".
- CAUSE ON EVERY ROW (coordinator's add): CarFault gains cause, causeNote, bornDay, bornOdo, zone1, hitSide, hitMps. Stamped at every creation path: CrashBank (impact, zone/side/m/s of the leg's hardest hit, or under + wheel side for a bump strike), threshold wear (wear), heat (heat), legacy crash/bottoming rolls (impact), SeedHidden + used rows + starter + yard/swap junk risk (previous_owner), advert (seller), debug bench (debug). Never shown. BlowEngine creates no row.
- SAVE v25 = `LifeSimManager.MigrateToFaultIds`: the used rows onto their ids on owned cars, viewing cars and booked repairs (hidden/diagnosed untouched), every row without a cause -> "legacy", viewing felt lists made. Idempotent; LastUnstampedVersion stays 24.

## Gates (PSXPerf, base = 7ae0ec13 in the same sandbox)
- Typecheck OK. TestNoPartLeak: 12 home-screen pages rendered from one career, each render restored from the same JSON and dice; with every fault (51 ids) HIDDEN at severity 1 and 2 no page shows any line it did not show with no faults (0 differences, 0 number notes), with every fault FOUND 588 lines vs 374 and no effect number; plus pre-race, seizure story, result note, DEBUG INFO gate and a source tripwire (old leak strings, EffectSummary only behind debugMode, no `.hidden` in screen files). Bars = the listed exception.
- Oil change vs hidden gearbox: the gearbox row stays hidden and its booked job stays. TestEngineDeath: gearbox + charging rows survive the seizure and the rebuild (both the seat path and the apply-back path); plugs go at the rebuild.
- NAME WHAT YOU FELT: a true PULLS TO A SIDE takes the cheap tier (x0.92) off the haggled offer and the fault stays hidden; a false BRAKES WEAK (no stop from 60 on that drive) ends the haggle; a clean car's false claim too.
- v25: a v24 career (no v25 fields in the JSON) in -> v25, ids moved, flags kept, legacy causes; Migrate again from 24 = byte-identical; the ladder's worked-career re-run v25..v25 now TESTED and passes. Save safety 77 ok / 0 FAIL.
- Every creation path stamps a cause (7 paths run + a source tripwire over Scripts).
- Leaks job (R0b tests + TestEngineDeath + ladder): 150 ok / 0 FAIL (h3_leaks.txt).
- FULL SELF-TEST (h3_selftest.txt, head 6482f342 code): 4,194 ok / 62 FAIL against base 4,103 ok / 62 FAIL (base_selftest.txt) - the same 62 by check text, 0 new, 0 gone (r0a-crash\compare_fails.py). An earlier head run (h2) had 7 new: 3 parts-shelf pins that said "v24" (re-pinned to the current version) and 4 physics sims (ParkedCarSim handbrake hold, TreeCrashSim stage forest) that ran AFTER the R0b tests and failed; with the R0b tests moved to the end of RunAll they pass again. So something the R0b tests leave in the editor (most likely the home-screen page renders) perturbs those sims; the cause was not isolated - it is test-side only (no runtime file the sims use changed).

## Not verified
- No play check: TestDriveFeel's thresholds were never driven; the chips, the "DRIVEN TODAY" button and the DEBUG INFO row were not looked at on screen (no three-aspect pictures). No player build.
- R3's numbers (value/quotes moving with hidden faults) are not gated; TestNoPartLeak notes them and found none on these pages.

## At merge
- Save step: v25 if nothing else merges a save change first (the ladder test enforces one step per number). Keep LastUnstampedVersion 24.
- New files (with .meta): Scripts/LifeSim/FaultView.cs, FaultSymptoms.cs, TestDriveFeel.cs; Editor/LifeSimSelfTest.LeaksR0b.cs (RunLeaks; RunLeaksR0bTests runs LAST in RunAll).
- Shared files: LifeHomeScreen.cs, LifeRules.cs, PauseMenu.cs, CarMarket.cs, Viewing.cs, Inspection.cs, FaultCatalog.cs, LifeState.cs, LifeSimManager.cs, CrashBank.cs, DebugCarOps.cs, Spares.cs, OnFoot/GarageWorld.cs, CoolingModel.cs, EngineTemp.cs, RaceHUD.cs, RaceHandoffApplier.cs, Editor/LifeSimSelfTest.cs (+ .SaveSafety.cs WorkCareer stamp, .Spares.cs v24 pins). Nothing under Scripts/City, RoadsideRules.cs, Resources or Art.

# HANDOFF - FAULTS R0a MERGED AND GATED: crash stop-gap + save safety (agent "r0a-gate", branch faults-r0a-crash, 2026-11-12; NOT pushed, NOT merged into mountain-nm)

Worktree `C:\Users\mcgee\PSX Racing-faults`, branch faults-r0a-crash = the crash half (4cf8ac09) + the save half (faults-r0a-save 84549b8e), off mountain-nm 19d6d4a3. Sandbox PSXCity (lock "r0a-gate", released). Design: art workspace `Fault_System_Design_2026-11-11.md` (OWNER SUMMARY, Â§1, Â§2 "Save safety" and "Versions", Â§3.1, Â§9 and its R0a stop-gap table, the R0a row of Â§10). Reports: scratchpad `r0a-gate\gate_*.txt` (runner `gate.ps1`); the base runs are the crash half's `r0a-crash\base_*.txt` (19d6d4a3, same sandbox). The two halves' own sections follow this one (crash first, then save); read them for the detail.

## What R0a changes
- CRASHES (switches `PSX_FAULT_CRASH=0` = today's crash, `PSX_FAULT_FAMILY=0` = every roller back on the Japanese pool; both 0 = today exactly). The leg's hardest hit is located (front / rear / side and left / right / centre, from the contact patch in the car's own frame) and sized (normal closing speed, m/s), and the Â§9 table is rolled ONCE, AT THE HIT: scuff (paint -2..-5), body dent, bumper_crack (bumper_dent on chrome and 5-mph bumpers) 70% from 3 m/s front/rear, radiator -25..-60 plus a coolant weep 60% from 6 m/s front, alignment on the struck side 40% from 10 m/s front/rear and 50% from 6 m/s on a flank (severe from 15 / 9), bump stop from 6 m/s. A hit centred on the nose or tail has no side and never pulls. The pull acts at once (CarController.hitSteerPull, 0.042 deg minor / 0.14 deg severe of road wheel; the old fault was 0.25 of lock, about 5 deg), the radiator at once (EngineTemp.radiatorCond + weep); the apply-back (LifeSim/CrashBank.cs) writes exactly what was rolled, no second roll. Every roller maps origin to family (ger, gbr, fra, ita, eur -> eur; usa; jpn). Existing alignment faults in a save also pull in degrees now (no migration). With the stop-gap on, the garage cooling line and the pre-race warning say "the cooling", not "the radiator" (f60143fb - keep it unless R0b lands first).
- SAVES (switch `PSX_SAVE_GUARD=0` = the old Load/Save, no copies; the writer stamp stays). `LifeState.writerVersion` is stamped by every Save() - added WITHOUT a version bump, v24 stays current. A backup file before any migration step (last 3 kept). A save from a newer build loads READ-ONLY and is never written. A v25+ save written back by an older build is detected and the title offers this build's own `.last` copy (written on a new career's first save and the first save after each daily rollover). A load or migration that throws keeps the raw string as a `.failed` copy, leaves the main key alone and starts no game until an explicit, confirmed NEW GAME. Copies are files, never PlayerPrefs: player `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PSX Racing\saves`, editor `<project>\Library\PSXSaveCopies`.

## The merge
- 4481fb79: faults-r0a-save into faults-r0a-crash. Only HANDOFF.md conflicted (kept both sections). LifeSimSelfTest.cs (two Guard lines in RunAll) and LifeRules.cs (Rollover's NoteRollover line) auto-merged beside the crash edits; no code conflict and no overlap in behaviour (no crash code saves; no save code touches the crash path).
- 9d129960 - A CHANGE I MADE AT MERGE, so that a player-only build is possible. The crash half had edited `Scripts/City/CityMode.cs` (StampExitResult's four damage lines -> CrashStopGap.StampHandoff). Every .cs under Scripts/City is in the city tile bake's fingerprint (`Editor/CityTileBake.cs` CodeDirs, :54), so that edit made the baked Charlotte stale: the Win64 build would re-bake, or a player would build every tile live. CityMode.cs is back to 19d6d4a3 byte for byte. Its two callers - PauseMenu.ExitToMenu (:377) and TownVenue's door exit (:440), both outside Scripts/City - now call the new `CrashStopGap.StampCityExit()` on the line after `StampExitResult()`: the finish line's own writer (StampHandoff) over the city session's player car; nothing without a city session or a player car, where StampExitResult stamps nothing either. New `TestCrashCityExit` (in RunCrash and RunAll): a free-roam exit done that way hands the apply-back the same crash, field for field, as the finish line's writer; with no city session it stamps nothing; and a source tripwire checks that every runtime call of StampExitResult is followed by StampCityExit (found: PauseMenu.cs:377, TownVenue.cs:440). This supersedes the crash half's notes below that list City/CityMode.cs among the shared files.

## Gates (PSXCity; base = 19d6d4a3, run by the crash half in the same sandbox; head = 9d129960 code)
| Gate | base 19d6d4a3 | R0a merged |
|---|---|---|
| Typecheck | OK | OK |
| LifeSimSelfTest, full run | 3,898 ok / 62 FAIL | 4,070 ok / 62 FAIL - the same 62 by check text, 0 new, 0 gone (`compare_fails.py`) |
| Crash self-test (RunCrash) | not in base | 96 ok / 0 FAIL (the crash half's 91 + 5 city-exit checks); identical to its final run apart from one random used-car line |
| 500 crashes per origin, walls only: classes | every impact roll = jpn bumper_crack or alignment (10/10 bumper_crack in the switch-off test) | 6 of 6 classes in 7 of 7 origins (per 500: nothing 102, scuff 398, body 205, bumper 142, radiator 38, alignment 59) |
| ... no electrical rows | eur cars rolled jpn pools | only bumper_crack / bumper_dent / alignment rows, also over 3,500 legs banked through ApplyRaceResult |
| ... bumper rows at most 50% | - | per crash outcome 142/444 = 32% (random-seed runs 29-35%): ok. As fault ROWS 142/201 = 71% (66-76%): FAILS as worded - see below |
| Drift, 100 km/h hands-off, minor / severe, left / right, assist OFF / STANDARD / FULL | old pull 0.25 of lock | RX-7 FD +-0.99 / +-3.53 m per 100 m (FULL +-3.49); Civic EG +-0.89 / +-3.18; Viper +-1.00 / +-3.52; pull-free 0.00; a live hit -1.00 m within 3.6 s inside the leg; the next drive through the real bank path -1.08 m. Crash play check 40 ok / 0 FAIL, 46 of 48 lines identical to the crash half's final run (2 differ by 0.01 m in a contact point) |
| Handling, no damage | 52 ok / 1 FAIL (RUF lane change) | 52 ok / 1 FAIL, the same; every physics value identical, only chase-camera readings differ |
| Temp, no damage | 28 ok / 2 FAIL (ambient, pad throttle) | 29 ok / 1 FAIL (ambient); the pad-throttle line flips between runs (crash half: base FAIL, after FAIL, after2 ok, rev ok); the seizure's cost varies 0.12-0.54 run to run |
| Parked, no damage | 44 ok / 0 FAIL | 44 ok / 0 FAIL; 62 of 258 lines differ, by at most 0.1 mm, numbers only |
| TestSaveDowngrade | not in base | 68 ok / 0 FAIL (tripwire over 619 files) |
| TestSaveVersionLadder | not in base | 8 ok / 0 FAIL; 1 NOT TESTED (no step above v24 yet - R0b's step is checked automatically when it lands) |
| Nothing under Scripts/City or Scripts/RoadsideRules.cs changed (`git diff --name-only 19d6d4a3 HEAD -- ...`) | - | prints nothing (it printed CityMode.cs before 9d129960). No Resources, Art, scene, shader, ProjectSettings or Packages file changed either |

Save cost (editor, inside the full run): worked career 25,668 bytes (19 of them the stamp); Save() 0.43 ms guard off / 0.44 on; first save after a rollover (writes the `.last` file) 6.4 ms, once per game day; load 0.58 ms, with a migration 7.2 ms.

## The failing gate (a decision for the owner, not a code defect)
Â§10's "bumper rows at most 50% of the faults" fails when counted as fault rows: 71% on the stratified sweep, 66-76% on random seeds, in all 7 origins. Cause: the design's own Â§9 table. Below 10 m/s (6 on a flank) the bumper is the ONLY fault row a wall hit can write (70% of front/rear hits from 3 m/s), while dents and the radiator are condition losses, not rows; so fault rows are mostly bumpers by construction, whatever the crash mix. Counted per crash it passes: a bumper row on 142 of 500 wall crashes, 32% of what needs repairing. The first round's 45% came from making a quarter of the crashes bump-stop strikes, which nothing measured supports. Options: lower the 70% bumper roll, write the dent as a row, or read the gate per crash. R1a replaces this table with located zone tables anyway.

## Not verified
- No player build, no PerfTour run, Android and WebGL not run. Nothing driven by hand; no gamepad or wheel feel (0.04 deg is subtle by design).
- The city exit path is proven by the self-test with a stand-in CityMode (edit mode) plus the source tripwire, not by a free-roam drive exited in play mode.
- Brake play check not re-run on the merged head (the crash half's base and head runs were identical, 21 of 21 lines; the merge changes no physics).
- RESTART and QUIT still throw the leg's hit away, and the live pull and weep reset at any scene load (R1a's DamageBank). The weep lasts only the drive it happened in; the lower radiator condition and the coolant lost are kept.
- The pause menu's new "pull deg" debug line has not been looked at on screen. On a phone the "older build saved over" note's date splits across its two lines. If the title is skipped (tools, or a build without the Blue Ridge venue), a FAILED career goes straight to the new-game wizard with no warning.

## What the owner will notice
- Light rubbing and kerb taps do nothing or leave a scuff. No more "every crash = cracked bumper + 25% pull".
- From about 7 mph straight into a wall, front or rear, the bumper cracks about 7 times in 10 (the body dents otherwise), hidden until inspected. From about 13 mph nose-first the radiator cracks 6 times in 10: some 10-20 minutes of driving later the temperature needle climbs as the coolant weeps away.
- A hard off-centre hit (about 22 mph front or rear, 13 mph on a door) can knock the alignment out: the car drifts gently toward the struck side at once, about 1 m per 100 m (3-4 m after a big one). Existing alignment faults in his career get the same gentle pull.
- His career is backed up before any upgrade, a build that cannot load it leaves it alone and says so on the title screen, and a newer build's career opens read-only in an older build.

## At merge into mountain-nm / to reach the exe
- PLAYER-ONLY PC BUILD: possible. No tile change (fingerprint sources untouched), no CityTileCache format bump, no kit slot, no scene pass, no new asset files beyond the six new scripts' committed .meta files, no save version bump.
- Keep f60143fb (the cooling text) unless R0b lands first. R0b's step is the first v25; keep `LastUnstampedVersion` at 24.
- When the first v25 build ships, recycle every PC build from before R0a (only a guarded build refuses to write a newer save back).
- Shared files touched by R0a: CarController.cs, CollisionResponder.cs, RaceHandoff.cs, RaceHandoffApplier.cs, RaceManager.cs, EngineTemp.cs, CoolingModel.cs, PauseMenu.cs, Town/TownVenue.cs, TitleScreen.cs, Dev/PerfTour.cs, Dev/PerfTour.Stage.cs, LifeSim/FaultCatalog.cs, LifeSim/LifeRules.cs, LifeSim/LifeHomeScreen.cs, LifeSim/DebugCarOps.cs, LifeSim/LifeSimManager.cs, LifeSim/LifeState.cs, Editor/LifeSimSelfTest.cs. New: Scripts/CrashStopGap.cs, Scripts/LifeSim/CrashBank.cs, Editor/CrashPlayCheck.cs, Editor/LifeSimSelfTest.CrashR0a.cs, Editor/LifeSimSelfTest.SaveSafety.cs, Editor/SaveGuardPreview.cs, tools/crash-play-check.ps1.
- Batch self-tests ran with -quit; the play checks ran watched and closed themselves (they enter play mode, which -quit would end before it starts).

# HANDOFF - FAULTS R0a, CRASH HALF: located, sized, live pull (agent "r0a-crash", branch faults-r0a-crash, 2026-11-12; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faults` off mountain-nm 19d6d4a3. Sandbox PSXCity (lock "r0a-crash", released). Design: art workspace `Fault_System_Design_2026-11-11.md` Â§9 (R0a stop-gap table) and the R0a row of Â§10. The save-safety half of R0a is a separate branch (faults-r0a-save). No save change here: no new LifeState/OwnedCar field, no version step.

## REVIEW ROUND (2026-11-12): 19 findings from 3 adversarial reviewers - all real; 18 fixed as asked, 1 narrowed (F6), none rejected
Commits cbd1725f (runtime), 3c8a8c90 (tests), f60143fb (cooling text, droppable), de395cc2 (patch centre over all contact points; banked-leg test fix), 01fe977e and the later test-only commits (unique test car ids, a fresh drive per play-check run, the wear check's message), and the HANDOFF commit.
- ONE ALIGNMENT, ONE RULE (F1, F8). CarController steers by CrashStopGap.CombinePull(faultSteerPullDeg, hitSteerPull): the worse band wins, a tie goes to the newer hit. CrashBank.ApplyOutcome replaces the car's one alignment row by the same rule (NewAlignWins, >= on a tie). It was the SUM live (minor R carried + minor L hit drove dead straight; same side gave 0.084 deg, the severe band) and strictly-worse when banked, so the next drive pulled differently. TestCrashOneAlignment: live == next drive for all 20 carried x hit pairs; the play check drives both cases.
- THE LEG'S HARDEST HIT, AND THE WORST OF IT (F3, F13). Only a hit that is the leg's new hardest is rolled (CollisionResponder.RecordHit, the rule in one place; the first round's "a later crash rolls fresh and merges" described the ledger, not the game). Merge takes the larger loss per lane, never the sum (6.5 then 7 m/s nose-ins could take 120 off the radiator; the band stops at 60), and a band tie goes to the later hit. Ledger.Hit ignores a softer contact of the same crash and merges a harder one; a bump strike merges after the open crash. TestCrashLive goes through RecordHit: 11, 12 (same crash), 13 m/s (flank) rolled, a later 10 m/s not; stamped body -30 / paint -3.5 (was -52.5 / -7).
- CENTRED HITS, AND WHERE A HIT IS (F5, F9, F14). Classify gives side 0 inside 0.2 of the half-width and Resolve rolls no alignment for side 0 (R1a: a centre-front hit bends no corner). The hit is located by the centre of the box around its contact points (CrashStopGap.PatchCentre), not the impulse-weighted point, which sits on the wall's inner edge. Play check (RX-7 nose into walls at 12.1 m/s; patch centre x vs the impulse-weighted x): left 35% -0.60 vs -0.27; left 60% -0.38 vs +0.19 and left 75% -0.24 vs +0.23 (the weighted point reads the WRONG side); full width 0.00 vs -0.16 -> FRONT CENTRE, no alignment with the alignment die forced; square tail at 11 m/s 0.00 -> REAR CENTRE, no alignment; right 35% +0.60; a right flank into the 200 m barrier +0.94 -> SIDE RIGHT.
- SWITCHES INDEPENDENT (F4). The two legacy impact rolls pass "jpn": PSX_FAULT_CRASH=0 alone is today's crash with PSX_FAULT_FAMILY on (a German car 10/10 bumper_crack at jpn prices; it would have rolled the eur pool's electrical "impact" rows).
- WEAR NEVER CRACKS A BUMPER (F6, narrowed). With the stop-gap on, the player's own wear rolls (RollThresholdFault -> RollWearFault noImpactOnly) skip rows only a hit causes (FaultCatalog.ImpactOnly: bumper_crack, bumper_dent) and roll nothing when nothing else is eligible: 20 long legs on a ruined body lane of a low-mileage car, 0 bumper and 0 body-lane rows (jpn, usa); switch off, 18 bumper rows in the same 20 legs. NOT applied to market rolls (SeedHidden, listings, viewings, salvage parts): a previous owner is a cause the design names (Â§1), and a "brakes" ad on a low-mileage Japanese car would then sell at 45% off with nothing wrong (R0b maps ad phrases to ids; R4c replaces SeedHidden).
- BODY WEAR ROLL AFTER A CRASH (F12). Step 5's body-lane wear roll reads the body from before the leg's crash (PSX_FAULT_CRASH=0: after, as before). TestCrashBankedLegs banks 3,500 swept outcomes through ApplyRaceResult over 5 km legs on all 7 origins (body and paint at 70; engine and tyres new, because a 5 km leg at 40,000 miles takes ~44 off the tyre lane and from 70 that rolls tyre-lane wear rows, which is what the first run of the test caught): the rows added equal the rows rolled on every leg, only bumper_crack / bumper_dent / alignment, no electrical row. 602 of the legs left the body under 40 (430 of them European); read after the crash, the old order would have added about 46 wear rows on them (33 on European cars, half electrical).
- THE GATE (F7, F15). Gated on WALLS ONLY (front, rear, side; every band equally), bump strikes swept on their own, a distinct FNV seed per origin (ger and ita shared one). DECISION for the coordinator/owner: read as REPAIR OUTCOMES bumpers are 32% (random-seed runs 29-35%) - passes; read as FAULT ROWS they are 71% (66-76%) - FAILS in all 7 origins. The table's 70% bumper roll on every front/rear hit from 3 m/s, against alignment rows only from 10 m/s (6 on a flank) and no row for a dent or a radiator, cannot meet a rows reading for wall contact. The first round's 45% came from making a quarter of the 500 bump strikes, which nothing measured supports.
- RNG (F10). Arm seeds the ledger from Guid.NewGuid, so arming takes no UnityEngine.Random draw (PerfTour's InitState replays again).
- PAUSE READOUT (F11). New line "pull deg : <combined> (crash x, carried y)".
- COOLING TEXT (F2). f60143fb: with the stop-gap on the garage line reads "Cooling system tired - the mechanic replaces cooling parts." and the pre-race line "the cooling is done" (no longer than the old lines; that column clips at 60) instead of naming the weakest part, because a crash now lowers the radiator lane. TestEngineTemp's warning check re-pinned. Two lines of R0b in their own commit: keep it unless R0b lands first; do not ship R0a without it or R0b.
- WEEP TEST (F16). Warm-started; the sound car is checked against TestEngineTemp's cruise band and exactly no coolant lost (88.0 C, 100.0%), not against an identical run; with the median weep 0.71: 90.0 -> 90.0 C two minutes after the hit, below 55% coolant at 10.2 min, past 112 C at 17.7 min (bounded 6-18 and leak..30). The handoff carries no weep (asserted): the coolant it cost is banked through EndCoolantPct.
- PLAY CHECK (F17, F18). The next drive is built through StampHandoff -> CrashBank.ApplyOutcome -> FillCarRequestFor -> ReapplyCar; no pull is written by hand. SwapCar now calls CrashStopGap.StartClean (a fresh ledger, no crash pull, no radiatorHit or weep, WorstHit and WorstBottom reset; nothing when no ledger is armed) and DebugCarOps.DriveCar banks the old car's rolled outcome onto the old car first. The check no longer zeroes anything by hand and asserts the swap starts clean after a planted crash. Runs are guarded so a throw is a FAIL line and the editor still exits.
- BRAKE CHECK (F19). A "brake" job (BrakePlayCheck.Run) in the round's runner, run on base 19d6d4a3 (its code exported by git archive, synced the same way) and on the head in PSXCity: the two reports are identical, 21 of 21 lines, 0 FAIL (STOP 43.5 / 42.7 / 44.3 m, TURN, SLIDE on the Civic EG, RX-7 FD and SL 600).
- GATES, review round (scratchpad `r0a-crash\`, PSXCity, base 19d6d4a3; runner run2.ps1, self-tests with -quit, play checks exit themselves). Typecheck OK. Crash self-test (RunCrash) 91 ok / 0 FAIL (fin_crashtest.txt). Full LifeSimSelfTest: base 62 FAIL lines, head 62, NEW 0, GONE 0 by check text (compare_fails.py; 3,989 ok; fin_selftest.txt). An intermediate head run showed one NEW failure, the TestCrashApplyBack id collision fixed in 01fe977e. Crash play check ALL HOLD, 40 ok / 0 FAIL (fin_crashplay.txt): drift unchanged (FD +-0.99 / +-3.53, 3.49 FULL; Civic EG +-0.89 / +-3.18; Viper +-1.00 / +-3.52), live hit -1.00 m inside the leg and the next drive -1.08 m through the real bank path, carried other / same side -1.08 m live and next, the walls as above, both swaps clean after a planted crash. No-damage path, base vs head: handling 52 ok / 1 FAIL in both (the base's own RUF lane-change FAIL), 255 of 260 lines identical and the other 5 differ only in a chase-camera framing value; temp the same verdicts except the pad-throttle line, which flips between runs as the first round recorded (base FAIL, head ok; the ambient line FAILs in both); parked 44 ok / 0 FAIL in both (0.1 mm hub and 0.001 m drift jitter); brake identical (above).

## Causes (line numbers at 19d6d4a3)
- The crash and bump-stop rolls passed no origin (LifeRules.cs:1353, :1367) and RollWearFault defaulted to "jpn" and sent every unknown origin there (FaultCatalog.cs:108, :121). The JPN pool's only impact rows are bumper_crack (body lane) and alignment (tyres lane), and alignment's effect is 0.25 of lock in a coin-flip direction (rg2_faults.json; Aggregate_ FaultCatalog.cs:429; faultSteerPull x maxSteer at CarController.cs:2542/2547 = about 5 deg of road wheel at 100 km/h).
- One uncapped DamageScore summed over the leg (CollisionResponder.cs:220) decided the roll (>= 22); the hardest hit (:221, :408-410) was never handed off (RaceManager.cs:432-435, CityMode.cs:964-967 stamp only DamageScore/HardHits/BottomScore/BottomWorst).
- The fault acted only after the next scene load (Aggregate_ at FillCarRequestFor). Wear rolls passed no origin either (LifeRules.cs:1780), and the heat/market/inspection/viewing rollers passed raw origins, so the 77 ger/gbr/fra/ita/eur cars rolled the Japanese pools.

## What changed (switches: PSX_FAULT_CRASH=0, PSX_FAULT_FAMILY=0; both 0 = today exactly)
- NEW Scripts/CrashStopGap.cs: the Â§9 table as a pure function (Resolve / ResolveBottom; bumper rating 2.2 m/s for origin usa 1973-82, 1.5 chrome before 1973, else 1.1; severe alignment from 15 m/s front/rear, 9 side and bump stop), Classify (zone and side from the contact patch's centre in the car's own frame; side 0 inside 0.2 of the half-width - review round), the career car's Ledger (rolled AT THE HIT, and only for a hit that is the leg's new hardest - CollisionResponder.RecordHit: a harder contact of the same crash within 0.6 s re-reads the same dice, a softer one is ignored, a softer later crash is never rolled; the leg's outcome is the worst of what was rolled, lane by lane, never a sum; damage never un-happens), Arm (guards FromLifeSim && !TestDrive && Edition.HasCareer), StampHandoff (the one writer for both exits). Pull constants 0.042 deg minor / 0.14 deg severe (set by the drift play check). Radiator outcome: radiatorCond -25..-60 live plus a weep (WeepPctPerSec 0.14, pressure-scaled).
- CollisionResponder.cs: WorstHitZone/Side/Local (the centre of the box around the hit's contact points in CarBody.box's frame - review round; WorstHitWeighted keeps the impulse-weighted point for the play check only), WorstBottomWheel, RecordHit (the leg's record rule in one place, the Ledger hook at the new worst hit), the Ledger hook at the new worst bump-stop strike, StartOverForNewCar. DamageScore and WorstHit untouched (AI retirement, delivery scoring, carHP summary unchanged).
- RaceHandoff.cs: WorstHitMps, WorstHitZone (1 front 2 rear 3 side), WorstHitSide (-1/+1), BottomWorstWheel (+1, 0 none), CrashRolled/CrashClasses/CrashPaintLoss/CrashBodyLoss/CrashRadiatorLoss/CrashBumperId/CrashAlignSide/CrashAlignSeverity, all cleared in ClearResult (and so ClearAll); SteerPullDeg (request half) cleared in ClearAll.
- RaceManager.cs, City/CityMode.cs: the 4 stamp lines -> CrashStopGap.StampHandoff (same values plus the new ones).
- CarController.cs: hitSteerPull (beside heatAccelMult, set at the hit, never written by ApplyCarCondition/ReapplyCar) and faultSteerPullDeg (beside faultSteerPull); combined by CrashStopGap.CombinePull (review round: the worse band wins, a tie to the newer hit - never the sum) and added to the steer target in road-wheel degrees only when non-zero (keyboard and wheel paths).
- EngineTemp.cs: radiatorHit and radiatorWeep (0 = sound car), the weep term in LoseCoolant (skipped at 0), the seizure story says "lost coolant after the crash" instead of blaming the hoses.
- RaceHandoffApplier.cs: Arm after ApplyCarCondition(firstRun); radiatorCond re-subtracts radiatorHit so a bench reapply cannot mend it; faultSteerPullDeg from the request; SwapCar calls CrashStopGap.StartClean first (review round).
- NEW Scripts/LifeSim/CrashBank.cs + LifeRules.cs: the apply-back writes what was rolled (paint, body, radiator, hidden bumper row, hidden alignment row with severity = band and pullDir = struck side; one alignment per car, replaced in place when CrashStopGap.NewAlignWins - the worse band, a tie to the newer hit); the RG2 impact rolls run only with PSX_FAULT_CRASH=0, with origin "jpn" as before. DamageScore's body/paint summary kept as it was; the table's located losses come on top. Step 5's body-lane wear roll reads the body from before the leg's crash (review round).
- FaultCatalog.cs: TryFamily/Family/FamilyOf; every roller (RollWearFault with no origin = the car's own, RollUsedFaults, MakeById, PoolRow) reads the family; Aggregate_ puts alignment into steerPullDeg (PullDeg by severity) instead of 0.25 of lock - this also tames existing alignment faults in the owner's save, with no migration; ImpactOnly and RollWearFault(noImpactOnly) (review round). LifeHomeScreen.cs: one line (RaceHandoff.SteerPullDeg) and the COOLING line (review round, f60143fb). DebugCarOps.cs: the bench describes the alignment pull in degrees; DriveCar banks the old car's rolled outcome onto it before a swap (review round). CoolingModel.cs, PauseMenu.cs: review round.
- Tests: NEW Editor/LifeSimSelfTest.CrashR0a.cs (RunCrash for a quick run; in RunAll after TestWearFaultCause), re-pins in LifeSimSelfTest.cs (TestWearFaultCause's crash case stamps a rolled outcome; TestDebugBench reads steerPullDeg/faultSteerPullDeg). NEW Editor/CrashPlayCheck.cs + tools/crash-play-check.ps1. .meta files for the four new scripts.

## Gates, first round (scratchpad `r0a-crash\`, base = 19d6d4a3 in the same sandbox; superseded where the review round says so)
- Typecheck OK. Origins: jpn 212 -> jpn, usa 30 -> usa, ger 21, gbr 20, fra 13, ita 12, eur 11 -> eur; 0 unresolved. A ger/gbr/fra/ita car's wear roll with no origin: 72/72 eur rows at eur prices; FAMILY=0: 12/12 jpn (after_crashtest.txt).
- Sweep, 500 crashes per origin x 7 origins (front/rear/side/bump stop, 125 each, every table band equally, stratified dice): 6 of 6 classes (per 500: nothing 117, scuff 300, body 157, bumper 104, radiator 30, alignment 128); bumper rows 104/232 = 45% of fault rows (seeded random runs 40-47%), 25% of all repair outcomes; rows only bumper_crack / bumper_dent / alignment, no electrical rows; alignment $60 jpn / $66 usa / $94 eur (after_crash_sweep.csv). Walls only, no bump strikes: 71% of rows are bumpers (32% of repair outcomes) - the table makes front/rear hits mostly bumpers by design.
- Drift, 100 km/h hands-off, metres per 100 m after the pull (after_crashplay.txt), assist OFF / STANDARD / FULL identical (the counter-steer assist's 8 deg slip deadzone is never reached): FD minor +-0.99, severe +-3.53 (3.49 FULL); Civic EG +-0.89 / +-3.18; Viper +-1.00 / +-3.52; left exactly mirrors right; pull-free 0.00. Live: hitSteerPull 0 -> -0.042 the same frame as a ledger hit, -0.99 m within 3.6 s; the banked next-drive pull drifts -0.99 m too.
- Real walls: left third of the nose FRONT LEFT (x -0.27 m), right third FRONT RIGHT (+0.27), right flank into a 200 m barrier whose bounds are centred 98 m ahead SIDE RIGHT (x +0.94), tail REAR. Switch off vs on, same hit: WorstHit 12.10 / 12.10 m/s, DamageScore 19.4 / 19.4.
- No-damage path: handling check - every physics column of every telemetry row identical, same verdicts (1 base FAIL, RUF CTR lane change, both); only chase-camera lens/cam readings differ by <= 0.4 deg (frame timing). Temp check - same verdicts; the pad-throttle line flips between runs (FAIL base, FAIL after, ok after2) and after's boil line hit a random seizure (99.4 again in after2). (The first round's "30 min with no weep bit-identical" self-test compared two identical runs and proved nothing; replaced in the review round.) Parked check - 44 ok / 0 FAIL both, drift 0.002 vs 0.003 m, gaps within 0.1 mm.
- LifeSimSelfTest: base 62 FAIL lines, after 62, NEW 0, GONE 0 (compare_fails.py, by check text). Crash pins 63 ok.

## Not done / not verified
- No player build; no visible-editor drive by hand; no gamepad or wheel feel (0.04 deg is subtle by design - the design moves the cue to the cockpit wheel and FFB in R1b/R2c). RESTART and QUIT still throw the leg's hit away (R1a's DamageBank); the live pull and weep reset on any scene load.
- Leaks R0b owns: the garage's COOLING bar still shows the cooling lane (its value, not a part name), and the mechanic's cooling-services page prices each part. The "weakest part: radiator" line and the pre-race "the radiator is done" line say "the cooling" while PSX_FAULT_CRASH is on (review round, f60143fb - two lines of R0b in their own commit). The other pulling faults (control arms 0.15/0.12, ball joint 0.1 of lock) are still shares of lock until R1b.
- The 5-mph bumper uses origin usa as the market stand-in (no catalog row is a 1973-82 US car today). The weep is live only; the radiator condition and the coolant level persist.
- (Review round) A crash that leaves the body lane under 40 still lets body-lane WEAR roll on LATER legs (distance on a worn lane, as before R0a); with the families on, a European car's body pool is half electrical (RG2's own rows). That is wear, not the crash leg; R4b's Wear.Advance owns how body damage and body wear relate. The market's impact-only rows (a seller's cracked bumper) are unchanged (F6, narrowed).
- (Review round) The pause readout's new "pull deg" line was not looked at on screen (debug only). DebugCarOps.DriveCar banking the old car's outcome was not driven through the bench page by hand; SwapCar -> StartClean is covered by the play check (S) and the self-test.

## At merge
- No CityTileCache format change, no kit slot, no new asset files beyond the four scripts' .meta files. No scene rebuild needed: the new fields are runtime state (hitSteerPull/faultSteerPullDeg serialize as 0 on the next bake). Shared files touched: CarController.cs, CollisionResponder.cs, RaceHandoff.cs, RaceHandoffApplier.cs, RaceManager.cs, City/CityMode.cs, EngineTemp.cs, CoolingModel.cs, PauseMenu.cs, LifeSim/FaultCatalog.cs, LifeSim/LifeRules.cs, LifeSim/LifeHomeScreen.cs (2 places), LifeSim/DebugCarOps.cs, Editor/LifeSimSelfTest.cs (RunAll gets one line after TestWearFaultCause, plus the re-pins incl. TestEngineTemp's warning line).

# HANDOFF - FAULTS R0a SAVE SAFETY 11-12 (branch faults-r0a-save off mountain-nm 19d6d4a3; agent "r0a-save"; NOT pushed, NOT merged)

Worktree `C:\Users\mcgee\PSX Racing-faultsave`. Sandbox PSXPerf (lock "r0a-save", released). Design: `Fault_System_Design_2026-11-11.md` Â§2 "Save safety" + "Versions", Â§9 (LifeSimManager row), Â§10 R0a. The crash half of R0a is on faults-r0a-crash (another agent). No version step added: v24 stays current.

## Review round 11-12 (three adversarial reviewers, 15 findings: all fixed, duplicates merged)
- FAILED career, NEW GAME (medium, two reviewers). A career that would not load is a blank state in memory, so `HasCareer` was false and NEW GAME skipped the ERASE confirm; the note called that harmless ("KEPT ... AS A BACKUP. NEW GAME STARTS A SEPARATE CAREER") although the new career replaces the main key and nothing reads `.failed` back. Now NEW GAME goes through the confirm with its own line (`LifeSimManager.FailedEraseLine`), and the note says only what is true: "...IT IS LEFT AS IT WAS, FOR A FIXED BUILD TO LOAD. NEW GAME REPLACES IT." Not done (optional): offering `.last` on a FAILED career - in the main case (a step that throws) `.last` is at the same old version and would hit the same step.
- Re-run point (low, two). A write-back re-ran every step from v25 whatever its stamp. A post-R0a build run with PSX_SAVE_GUARD=0 keeps every field it knows, so those steps would run on live data. Now `s.saveVersion = Mathf.Max(LastUnstampedVersion, s.writerVersion)`.
- `.last` after NEW GAME (low, three). It survived DeleteSave/StartNewGame, so an older exe writing the NEW career back was answered with the erased one (the title's default button). Now NEW GAME moves `.last` into the backups and off its name, and the new career's first save writes its own.
- UseLastCopy (low). It wrote `.last` over the main key before knowing it would load. Now it upgrades `.last` in memory first (BeforeMigrateHook, Migrate, EditionSanitize, StripBuiltCars); one that throws is refused with the main key, the career in play and the copy untouched, is not offered again this run, and the title says "THIS BUILD'S OWN COPY WOULD NOT LOAD, SO THE OLDER BUILD'S CARRIES ON."
- Copies in PlayerPrefs (low). Android's SharedPreferences rewrites its whole XML on every PlayerPrefs.Save(), so `.last`, 3 backups and up to 5 failed copies rode along with every save (up to 10x the career); WebGL caps PlayerPrefs at 1 MB. The copies are now FILES: player `persistentDataPath/saves` (Windows `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PSX Racing\saves`, shared by every PC build like the registry key); editor `<project>\Library\PSXSaveCopies` (an editor's persistentDataPath is the player's own folder, so sandbox tests must never write there). PlayerPrefs holds the career alone, as before R0a. `.last`'s date is the file's write time (a second file for it doubled the rollover save's cost).
- PerfTour (low). env.txt (city and stage tours) gets a `save ...` line from `LifeSimManager.MeasureSaveCost()`: career bytes, Save() median, first save after a rollover, copies. Scratch key only; the player's career is never timed.
- Found in my own picture, not by a reviewer: the failed confirm's second line sat under YES (`MenuKit.Button` pivots on its anchor, so YES spans y 120-174, not 93-147). Moved to 14 over YES. LifeHomePreview's CLIP report compares text with text and read 0, so SaveGuardPreview now also reports any text under a button's face (UNDER A BUTTON TOTAL).
- Tests (low/medium, four findings): a stand-in v25 step (BeforeMigrateHook) so the re-run is observed (from v24, to v25) and the kept copy is v25/w25; the guard-off write-back re-runs from the writer's version; NEW GAME then a write-back offers the new career's copy; a refused trial; files-not-prefs; the NEW GAME doors allowlist; the ladder's second pass is now called what it is ("Migrate has no unguarded code"), and the re-run of steps above v24 on a worked career is logged NOT TESTED until a step above v24 exists (R0b's is then checked automatically); size/time labels fixed (see Gates).

## Causes (before R0a)
- `LifeSimManager.Load` (was :93-109): any throw in FromJson or Migrate was caught, returned null, and `State` became `NewGame()`. The next `Save()` wrote that blank career over the stored one.
- No version guard. An older exe loads a newer save (Migrate's `< N` steps do nothing), drops the fields it does not know, and its next `Save()` writes the career back still marked with the newer version. The newer build then never re-seeds those fields.
- Every PC build shares one registry key (HKCU, `psxRacingLifeSave`), and several builds are on disk.

## What changed (switch `PSX_SAVE_GUARD=0` = the old Load/Save, no copies, no .last; the writer stamp stays)
- `LifeState.writerVersion` (added with no version bump). Every `Save()` stamps it with this build's version, also with the guard off.
- `LifeSimManager` (Load is now `LoadGuarded`):
  - BACKUP. Before the first migration step, the raw string is copied to the file `<key>.bak.v<old>.<yyyyMMdd-HHmmss>.<n>.json`. The last 3 are kept. A string identical to the newest copy is not copied again.
  - NEWER SAVE (saveVersion above this build's). Loaded READ-ONLY and not migrated. `Save()` writes nothing. The title shows a note and CONTINUE (READ-ONLY); the home screen toasts "READ-ONLY: A NEWER BUILD'S SAVE" once.
  - REWRITTEN (v25+ and writerVersion below the save's version: written back by a build that did not know that version). Backed up; the steps above `max(24, writerVersion)` run again. The title offers THIS BUILD'S COPY (`<key>.last.json`, game date and the real date it was saved, trial-loaded first) or OLDER BUILD'S COPY. If no title shows (tools, batch), the older build's copy is kept, re-seeded.
  - `.last`. Written by a new career's first save and by the first save after each daily rollover (`LifeRules.Rollover` -> `NoteRollover`, live career only). NEW GAME moves it into the backups.
  - FAILED (parse or migration throws). The raw string is kept as `<key>.failed.v<old>.<date>.<n>.json` (up to 5). The main key is not touched, and every `Save()` is refused until an explicit, confirmed NEW GAME (`StartNewGame` / `DeleteSave`). The title says so.
  - NEW GAME over a newer or unloadable career copies it into the backups first.
  - Copy writes are files written via a .tmp and a move, all inside try/catch: a copy that fails never costs the save, and PlayerPrefs never holds a copy.
  - `MeasureSaveCost()` for PerfTour.
- `TitleScreen.BuildButtons` + `SaveNote` + `Confirm`: the note sits over the buttons, at most two lines at all three aspects; NEW GAME over a failed career confirms with `FailedEraseLine`; a refused THIS BUILD'S COPY rebuilds the menu.
- `LifeRules.Rollover`: one line at the end. `PerfTour.cs` / `PerfTour.Stage.cs`: the save line.
- Tests: `Editor/LifeSimSelfTest.SaveSafety.cs` (TestSaveDowngrade, TestSaveVersionLadder, `RunSaveSafety`), 2 Guard lines in `LifeSimSelfTest.RunAll`. `Editor/SaveGuardPreview.cs` renders the title notes (6 cases) at three aspects with the clip report.

## Gates (after the review fixes)
- Typecheck OK.
- RunSaveSafety (PSXPerf): SAVE SAFETY SELF-TEST OK, 76 checks, 0 failures; one line NOT TESTED (re-run above v24: no such step yet).
- Whole LifeSimSelfTest in PSXPerf: base (19d6d4a3) 62 FAIL; after (964e6756) 62 FAIL, the identical set (0 new, 0 gone). The failures are city decks, lamps, editions and texture budgets; none are in save code. Its save sections: 76 ok, 0 FAIL. (The last commit after that run moves the failed confirm's text and adds a preview check: title layout and an editor tool only.)
- Writers tripwire (615 .cs files): nothing outside LifeSimManager spells out the key, uses SaveKey/CopyDir without a scratch key, or wipes every pref; the runtime doors to NEW GAME are exactly DoorTour.cs, LifeHomeScreen.cs, PerfTour.cs, TitleScreen.cs (15 editor tools also call them, on the editor's own prefs). `LifeSimManager.Save()` is called 209 times in 37 files - a count, not a check; the guard is inside `Save()`.
- TestSaveVersionLadder: steps exactly v2..v24, once each, in order, each stamping its own number once; every version 1..23 migrates to v24; Migrate has no unguarded code (a second pass at v24 runs nothing - this is NOT a re-run check); the re-run of steps above v24 is NOT TESTED until one exists.
- Save size (measured on this branch): a new career 6,653 bytes; a worked career (10 cars, 60 days, 2 faults, a shelf part, a booking) 25,577 bytes, of which the writer stamp is 19 bytes (so 25,558 bytes before R0a, exact: the stamp is the only field R0a added). PlayerPrefs holds only that; the copies are files (.last 25.0 KB, 3 backups 74.9 KB).
- Save time (editor, registry, median of 5 interleaved batches of 10):
  - Save() 0.42 ms with the guard off (this branch's Save with PSX_SAVE_GUARD=0 = the pre-R0a Save plus one int) and 0.42 ms on; 0.41 ms with .last and 3 backup files beside it.
  - First save after a rollover (+ the .last file): 6.2-6.9 ms (was 0.53 ms with the copy in the registry). A 25 KB file costs 3-4.5 ms on this disk however it is written (measured outside Unity: direct 3.1, new file 3.5, tmp+move 4.6 ms); tmp+move is kept so a crash never leaves no .last. Once per game day, at a door.
  - Load 0.58 ms; a load with a migration (+ a backup file; only the load timed now) 7.2 ms, once per version bump. The earlier 2.25 ms included the test's own string building.
- Title preview: 6 cases x 3 aspects (ok, newer, rewritten, rewritten_refused, failed, failed_confirm): CLIP TOTAL 0 and UNDER A BUTTON TOTAL 0. Sheet: scratchpad `r0a-save\title_saveguard_sheet_review.png`. The first render of failed_confirm had the warning's second line under YES while CLIP read 0; fixed, and the new check covers it. Still: on a phone the Rewritten note's date breaks across its two lines (WED 10 / FEB).

## Not done / not verified
- No player build and no play-mode title: the preview builds the title menu bare, over a flat sky colour.
- PerfTour's save line is pinned by the self-test (MeasureSaveCost) but no tour was run (no player build).
- Android/WebGL: not run. With the copies as files, PlayerPrefs carries one career's worth per save, as before R0a. WebGL (frozen): its files are not synced to IndexedDB, so a copy lasts as long as the tab.
- If the title is skipped (tools; a build without the Blue Ridge venue) a FAILED career goes straight to the wizard with no note. ARCADE returns to the title, so the normal boot always passes the confirm.
- The owner's real save size was not read.

## For the coordinator at merge
- Nothing for the city: no CityTileCache format bump, no kit slot, no scene pass. The new .cs files have committed .meta files with fixed GUIDs.
- Shared-file edits:
  - LifeRules.cs: +4 lines at the end of Rollover.
  - LifeSimSelfTest.cs: +2 lines in RunAll, after TestPartsShelfSave.
  - LifeState.cs: one field after saveVersion.
  - TitleScreen.cs: BuildButtons, SaveNote, Confirm.
  - PerfTour.cs: SeedCareer + one env line; PerfTour.Stage.cs: one env line.
  - LifeSimManager.cs: Save, Load, DeleteSave, StartNewGame and TakeEditionNote, plus the new section.
- R0b (the first bump, to v25):
  - Keep `LastUnstampedVersion` at 24 forever.
  - Write the step as `if (s.saveVersion < 25) MigrateToX(s);` and stamp `s.saveVersion = 25;` (TestSaveVersionLadder checks this).
  - The step must be safe to run a second time on a save that already went through it: TestSaveVersionLadder then runs it twice on a worked career and fails if the career changes.
- When the first v25 build ships, recycle every PC build from before R0a (design Â§2 "Old builds"). Only a build with this guard refuses to write a newer save back.

# HANDOFF - CITY INTEGRATION 11-11 ("cityinteg", branch integ-city-1111; NOT pushed, NOT merged by me)

Base mountain-nm 93903bd7 + fixes-1111 (beacons r1-r3; car/cockpit files kept at mountain-nm, proven: no diff outside the beacon files) + charlotte-curbs-drains 128f9e53 + charlotte-road-seams 9f0e0d08 + charlotte-buildings-1111 (merged three times: 1f001b35, 312abb93, e98adc39) + the coordinator's 9c7d9750 (roof _HouseTint after the material exists). Sandbox PSXPerf (lock "cityinteg", removed). Sheet: scratchpad `cityinteg\city_integ_sheet.png` (base | merged, 14 poses).

## Conflicts and how I resolved them
- fixes-1111: CockpitFit.Table.cs, CockpitInterior.cs, DoorMirrors.Table.cs, CarViewer.cs, CarShell.cs, PSXCarPaint.shader conflicted, and CockpitFitProbe.cs / DoorMirrorMatch.cs auto-merged with old code. All of them take mountain-nm's version (cars2 builds on top of them). HANDOFF.md merged as a union.
- curbs, seams: only HANDOFF.md conflicted. CarController.cs (curbs envelope, OFF) and mountain-nm's parked fix auto-merged; both are kept. PSXLit.shader (buildings _HouseTint) merged cleanly with mountain-nm's texture-round lines.
- buildings 1f001b35 DID NOT COMPILE: HousePolyRoof and the flat counter were spliced inside the roof block. I put them back in order; the branch fixed the same thing itself in 4f6276fe.
- buildings 13ce514b (HouseStepOffRoad, 1 m, units included) covers my 96576db8 (PSX_CITY_HOUSEPARTCLEAR, 0.75 m). I kept the branch's rule and removed mine.
- Added .meta files for 12 new scripts that came in without one.
- CityTileCache.Format: NOT bumped. Every baked class field is listed (treefields_check: 0 unknown, same as base). The new house vertex colours ride the generic colour channel. No new Slot. The fingerprint changes anyway, so the city must be re-baked.

## Fixed here (switch =0 gives the before)
- PSX_CITY_SLABSEAT (RoadSlab.cs): a fan-mouth step face now stops on the ribbon's own edge, instead of going 3 cm through it. Flicker junction|tw3t: 97 -> 0 px.
- PSX_CITY_PANSEAT (Curbs2.cs, plus one line in CityMeshes.cs TileSteps): corner gutter pans are laid after the closing pass, on the drawn junction surface. Flicker junction|structure: 106 -> 0 px.
- PSX_CITY_CURBNOEDGE (Curbs2.cs CurbNoEdgeLines; CityMeshes.cs EmitRibbon / DrawnLines): a curbed walk side has no edge line. Uncurbed roads, freeways and a median side keep theirs. The full-quad spans become three strips (2 extra quads).
- 0.5 mm / 20 cm bug in my own first version: TriGrid.Between(..., any=true) returns the band top, not the surface height (a88d3904).
- New tools: Editor/CityIntegCost.cs (triangles and ms per tile on 3x3 rings) and Editor/CityIntegShots.cs (poses written to a file, so the base code can shoot the same frames).

## Gates, base 93903bd7 | merged (final head)
- Drive audit: uptown, tryon and independence routes + 9 default tiles + box (Wesley Heights courts included): 30 ok/FAIL lines identical. Walls in lanes 0|0, fall holes 0|0, invisible in lanes 0|0, solids 88|88, drawn faces in a lane 120|120. N Graham e20287: 26 lines identical.
- Launch: 91|91 (the same spots, within 1 mm). Unload: 565|563.
- Traffic: CITY TRAFFIC OK twice, 0 traffic-on-traffic bumps.
- Flicker: 460|525.
  - FIXED: the seams' fan-mouth tie and the corner-pan tie.
  - LEFT: Buildings FacadeGlass|RoofFlat 46 px at tryon0, 150-400 m out. It goes with PSX_CITY_LRLOOK=0 + PSX_CITY_LRHEIGHT=0 (478 px then), so it comes from the types round's low-rise. Not found; that is for the buildings owner.
- Seam audit (open metres, merged code, slab off -> on):
  - owner tile 3,442 -> 93 (68 with CURBNOEDGE=0)
  - Uptown 4x4: 106,183 -> 2,980 (2,895)
  - 60-tile sample: 99,105 -> 498 (517)
  - Open steps: 0 at the owner tile. The rest is 0-1 mm T-junction joins. Not 0.
- Windows: cut + buried 9,904 -> 0 (64 tiles). Flat house roofs 0.
- House census: GAP 0 = base, buried p95 0.60 = base.
- Roof closure: 838 -> 845 points see daylight.
- Trees: FAIL 152|152.
- Beacons: far 118/121, near 118/121 within 5 cm.
- Cost, base | merged, ms a tile and triangles a tile:

  | ring | ms | triangles |
  |---|---|---|
  | N Graham | 150 \| 168 | 17,955 \| 29,558 |
  | Uptown | 396 \| 517 | 37,776 \| 46,826 |
  | Plaza Midwood houses | 159 \| 194 | 20,869 \| 39,745 |
  | I-77 | 332 \| 384 | 26,069 \| 39,102 |

  - Growth by mesh: buildingCollider (houses x4.7), structure (pans, lids), roads (slab).
  - Full bake estimate: about 2,000-2,400 s (today 1,711 s), with the tile files about 1.5x larger.

## Seen in the pictures / not done
- N Graham: the edge line is gone. The gutter pan and the plumb curb read right, and the corner pans go round the returns.
- The owner's frame is NOT matched: the seams' pick (n14254 / e20287) is a 2-lane arm, and his screenshot shows 4 lanes.
- Night frames: the sidewalks are blown out in the editor, before and after.
- Atando Ave: my pose shows trees, not the sheds.
- Lexington Ave: rolled curb, porches, chimneys. The house inlet frame landed on a vertical-curb street.
- Not done: no player build, no play-mode drive.
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
# HANDOFF - CHARLOTTE MESH TREES ROUND 2 (charlotte-mesh-trees, ctrees2, 2026-11-11 evening; NOT merged)

Branch `charlotte-mesh-trees` (worktree `C:\Users\mcgee\PSX Racing-ctrees`), mountain-nm fc5e975e merged in first (HANDOFF conflict only). Sandbox PSXLab (lock "ctrees2", released). Nothing under `Scripts/City/` and not `Scripts/RoadsideRules.cs` changed (`git diff --name-only c81f2a59 HEAD -- Assets/PSXRacing/Scripts/City Assets/PSXRacing/Scripts/RoadsideRules.cs` prints nothing): player-only build, no re-bake.

## Causes
- Trees vanished (round 1: 44% at N Graham, 72% Uptown; ALL 2,816 of 2,817 grate trees in round 1's six rings): no owned model is a pruned street tree (every crown starts 1-4 m up and is 5-16 m wide), and the road test held a grate trunk 0.35 m past the MAP's half width - but 17% of Uptown/Graham grates stand 0.4-0.8 m inside it (the street dress steps them out from the DRAWN curb, `CityMeshes.StreetDress.cs` DressSpan, TreeLaneClearM 0.95).
- Audit 11 (round 1): `CityMeshTrees.Gather` skipped every small collider whose top was under foot+0.6 m while the audit counts crown points from 0.4 m (Big Oak's low limbs/flare over low walls); the tile-wide-solid band test was a capsule that is a sphere when the band is shorter than it is wide (missed the band's rim). Found on the way: in edit-mode tools the fit ran BEFORE the tile's sign posts stood (one 05_Poplar pine through a `CityPost`).
- Hitches: the whole tile was fitted in its stand-up frame (play-mode tour: up to 171.8 ms of fitting in one frame, 466 frames over 4 ms).
- Cost: the pine stand-in poplars' LOD0 (20k) and Big Oak (37.6k, 27.9k of it bark) at the nearest level; Meadow poplars' 4th/5th pack levels unused.

## What changed (switches)
- `Editor/PSXRacingBuilder.CityTreeKit2.cs` (new): STREET FORMS (`CityTreeKit.Role.Street`, appended): Meadow poplars 02/03/07 + Forest beeches 01/02/03, each as vase / column / high (18): lowest branch rings cut, root flare drawn in, trunk stretched, crown lifted to 0.43-0.53 of the height and narrowed to 0.42-0.62 of its width; impostor cut at the same height and moved alike. LOD LADDER: triangle budget per level 6.5k/3k/1.3k (Big Oak 10k/3.6k/1.8k from its coarsest pack level, bark twigs thinned by area); poplars take their own pack levels 0-4; leaf clusters thinned + grown elsewhere. City COPIES of the impostor materials x1.3. `PSX_CT_R2KIT=0` = round 1's kit.
- `PSXRacingBuilder.CityTreeKit.cs`: pack levels 0-4 read, ladder by budget; nearest level 65 -> 55 m (102 m on MEDIUM after PopFix).
- `CityTreeKit.cs`: `Role.Street` appended, `Bands` 12 -> 16, `lodTris`.
- `CityMeshTrees.cs`: street trees fit street forms first (stay in their grate; road envelope = the drawn pavement, `GratePave`); other trees try the street forms before a smaller tree; colliders skipped only under 0.3 m; tile-wide solids asked as two boxes (octagon round the band); FIT SLICED (2.5 ms a frame, `PSX_CT_SLICE_MS=0` = round 1's one-frame fit), one physics sync a frame, a tree given up after 160 volume tests; trunk table replaced when a tile's fit ends (trees moved/gone); pool starts at 128 slots, growth refills over frames; `PSX_CT_ROUND1=1` = round 1's fit; trace `PSX_CT_TRACE` (or PSX_PERFOUT/ctrees_trace.csv in a perf tour); `PSX_CT_FLIP=s` flips F8 on a timer.
- `Scripts/Dev/PerfTour.cs` (shared, +4 lines): tour routes `ctr_south` (South Blvd), `ctr_prov` (Providence Rd).
- `Editor/CityRefSpots.MeshTrees.cs` rewritten: cards | round 1 | round 2 at poses where the trees are, cards in the row's dress, drop survey on round 1's 150 tiles, audit, trunks-v-trees check, ring triangles per FOREST DETAIL, pop lag pair, impostor pair.

## Proof (PSXLab, editor, PC profile, MEDIUM)
Sheet: scratchpad `ctrees2\ctrees_round2.png` (19 rows: Uptown Tryon grate block noon/dusk/night + 150 m, N Graham graham_b noon/dusk/night + 150 m, Dilworth summer/fall/winter + 150 m, south subdivision, I-277 John Belk verge, Johnston Rd median; impostor pair). Night + rain from the cockpit with mirrors (play mode): `ctrees2\ctrees_round2_night_cockpit_rain.jpg`. Logs `ctrees2\ctrees2_log_final.txt`, tour CSVs `ctrees2\tour_*`.
- DROPS on round 1's six rings (27,884 trees): round 1 2,912 (10.4%), grate 2,816/2,817; round 2 69 (0.2%), grate 45/2,817 (1.6%). Uptown 26 (1.2%), Graham 21 (0.7%), Dilworth 2, south 1, verge 3, Providence 16. Remaining grate drops: crown band at 4.6-6 m against a wide street's lane (no form clears it) and sign posts / skybridges; others: 160-test cap (median ring 16) and tile-wide Buildings.
- AUDIT 0 on 215 tiles / 38,516 trees (70 M crown points). TRUNKS v DRAWN TREES (replaces CityAudit.RunDrive, which stands tiles without mesh trees): on round 1's 150 tiles, 5,604 solid trees, trunks with no drawn tree 0, solid trees with no trunk 0.
- RING TRIS, 25 tiles from the eye, MEDIUM: Uptown 0.50 M, Graham 0.58, Dilworth 1.45 (round-1 kit 2.08), subdivision 0.70, verge 0.87, median 0.78. LOW/HIGH: Dilworth 1.01/2.66, others 0.38-0.56 / 0.88-1.79.
- PLAY MODE (visible editor, PC, noon, chase, traffic; routes Tryon St -> South Blvd -> Providence Rd, 150 s each): pool 128 slots throughout (no growth), slots used 0-116 and given back as tiles left (no leak). Fit per frame: one-frame fit max 171.8 ms, 466 frames over 4 ms, 32 frames over 100 ms on Tryon; sliced: max 24.1 ms, 35 of 3,113 fitting frames over 4 ms, 0 frames over 100 ms on Tryon/South. Frame p50 cards 17.7/16.7/19.3 ms vs mesh (sliced) 17.6/17.0/17.0 (traffic jams dominate the spread); forest CPU (cull + draw + mirrors/probe) ~0.8 ms. Editor 1080p shot frames +4.5 to +9.4 ms (that path rebuilds the forest's materials every shot: an over-count).
- POP (lag pair, 6 m, 30 steps over 300 m): Dilworth 0.08% of blocks change (worst step 0.61%) at 102 m vs 0.03% at 120 m; subdivision 0.13% vs 0.06%; noise 0. Kept 102 m for the triangle target.
- IMPOSTOR v MESH (Dilworth 150 m, every tree one way then the other): tree-pixel luma 58.1 mesh / 44.5 impostor -> 59.3 after the x1.3 copies; impostors still cover ~40% more pixels (fuller silhouettes).

## What still looks wrong / not done
Uptown's grate trees read as young and thin (narrow forms of slender poplars); a cut beech shows branch stubs up its trunk (Graham 150 m). Many non-street trees against walls became street forms (Dilworth 457), so some yards are gaunter than the cards. The Uptown cards frame shows no grate trees at all where round 2 plants a row (cards for those grates were not drawn in that editor frame - not investigated). 35 fit frames still over 4 ms (one dear tree ~20 ms: search not resumable). Impostor silhouettes fuller than meshes. Not run: a player build; the city drive audit (`CityAudit.RunDrive` stands tiles without mesh trees in edit mode - replaced by the trunks-v-trees check); door-mirror close-ups; snow; dusk/night for more than two places; the houses' porch boxes (`tm.houseBoxes`, roofs agent) are not read (house margin 1.6 m + gable as before).

## Coordinator at merge
1. `tools\store-assets.ps1 -Dest <build sandbox>` (unchanged from round 1).
2. With PSX_PC_CONTENT=1: `-executeMethod PSXRacing.EditorTools.PSXRacingBuilder.BuildCityTreeKit` (rebuilds the kit: 44 species, CT2_* meshes under Assets/PSXRacing/Generated, CT2_city_* impostor materials in the materials folder). Full scene pass and tile re-bake NOT needed; no CityTileCache.Format change, no kit slot.
3. Shared files: `Scripts/Dev/PerfTour.cs` (2 routes), `Scripts/MeshForest*.cs` untouched this round. New: `Editor/PSXRacingBuilder.CityTreeKit2.cs` (+ .meta).

# HANDOFF - CHARLOTTE MESH TREES (charlotte-mesh-trees, ctrees, 2026-11-11; NOT merged)

Branch `charlotte-mesh-trees` (worktree `C:\Users\mcgee\PSX Racing-ctrees`) off mountain-nm c81f2a59. Sandbox PSXLab (lock "ctrees", released). Owner: "Why are you still using old tree cards when I've specifically asked to use Mountain/Meadow dynamic trees, plus free Poplar and Oak from assets."

## Cause
Charlotte's trees were never on the mesh path: `Scripts/City/CityTrees.cs` builds 2 crossed 128 px cards a tree into one mesh a tile; `MeshForest` was only built by the stage builder, as a scene-baked component.

## What changed (no file in Scripts/City touched: the tile bake fingerprint is unchanged, no re-bake, no baked-class change)
- `Scripts/MeshForest.Pool.cs` (new) + small edits in `MeshForest.cs` / `MeshForest.Gpu.cs`: a STREAMED MeshForest - fixed slots (one a live tile, 480 plants each, 96 slots to start, doubles when full) in the same GPU buffers, cull and indirect draws; a slot's chunk cards = the tile's old card renderer (shown only where the chunk is not drawn; still the sun-shadow stand-in). `pooled` + `dressSource` fields; stage forests unchanged.
- `Scripts/CityTreeKit.cs` (new ScriptableObject, Resources/CityTreeKit.asset) + `Editor/PSXRacingBuilder.CityTreeKit.cs` (new): 26 species - 6 Forest beeches (mountain materials/impostors), 2 bare snags, 4 small beeches, Big Poplar, 6 Meadow poplars (parts per LOD merged by hand: pack meshes are Read/Write off; pack 4-view impostor sheets cut to alpha bounds), 2 of them as an evergreen PINE STAND-IN, 4 grey willows (crape myrtle), Big Oak (FBX LOD1-3, its billboard keyed to alpha as impostor). Crown profile per species: 12 height bands of LOD0. PC content only: written when PSX_PC_CONTENT=1, DELETED by any other pass. Hooked into the city scene build (`PSXRacingBuilder.City.cs`, one line) and runnable alone: `-executeMethod PSXRacing.EditorTools.PSXRacingBuilder.BuildCityTreeKit`.
- `Scripts/CityMeshTrees.cs` (new): hooks the existing static `CityWorld.TreeFrameReady` (no CityWorld edit); per tree chooses a species by CityTrees.Species / street stem / young + hash, sizes it (willow oak x1.35 card height, etc.), FITS it as a volume against road clearance (4.6 m over pavement, decks from below), building footprints+roofs (houses +1.6 m for porches/roofs), poles/wires/cobra-heads, signal poles/arms, and every collider round it (tile-wide Buildings/Barriers MeshColliders band by band). Order: other model / smaller (to 0.44) / move up to 3.5 m away from the wall / poplar / small beech / bush / drop. A dropped or moved tree is written back into the TreeTile before it stands up, so trunk colliders follow (nothing solid undrawn). Switch `PSX_CITY_MESHTREES=0` = cards; F8 = cards A/B. Gate: `PlatformProfile.PC` && kit present && compute (phones/web keep cards).
- `tools/store-assets.ps1`: Big Oak (6 files) + Meadow poplars 01,02,03,04,05,07 / grey willow (28 files). Extracted into PSXLab.
- `Editor/CityRefSpots.MeshTrees.cs` (new): `-executeMethod PSXRacing.EditorTools.CityRefSpots.RunMeshTrees` (env PSX_PROFILE=PC, PSX_PC_CONTENT=1): before/after shots, city foliage audit (LOD0 vertices, independent of the fit's bands), cost.

## Proof (PSXLab, editor, PC profile, FOREST DETAIL MEDIUM)
Sheet: scratchpad `ctrees\ctrees_before_after.png` (22 pairs: Graham, Uptown Tryon, Dilworth summer/fall/winter, south (Pineville-Matthews Rd), "fwy_verge" (snapped to Old Pineville Rd, NOT I-77), Providence Rd; eye + 150 m; Graham/Uptown dusk+night).
- 150 tiles fitted: 24,994 mesh trees, 2,890 not fitted (1,069 solid -> trunks removed), 266 moved, 3,722 shrunk, 2,335 swapped; fit 16 ms a tile average (Dilworth 60 ms a tile - in the stand-up frame: a hitch to slice later).
- Drop rate by place: Graham 44%, Uptown 72% (crowns against tower walls: `BIG Buildings` collider 133k candidate refusals), Dilworth 1%, south 0%, verge 0%, Providence 0.1%.
- CITY FOLIAGE AUDIT, 140 tiles, 24,597 trees, 43.7 M crown points: 11 trees through a solid (gate is 0: NOT MET) - all "collider", 9 are Big Oak (low crown points 0.3-0.6 m up next to small colliders; suspect the fit's skip of colliders under foot+0.6 m). Road envelope / building data / poles / signals: 0.
- Cost at the driver's eye (1920x1080, editor render+readback, cards -> mesh): Graham 12.8->19.2 ms, Uptown 12.2->15.8, Dilworth 11.8->17.2, south 10.7->18.9, verge 21.3->20.3, Providence 9.2->15.6. Mesh tris drawn 0.09-2.2 M (south ring 2.17 M: the pine stand-in 05_Poplar_skan LOD0 dominates). GPU buffers ~25 MB at 96 slots.

## Not done / not verified
Pop-in measurement (popwalk/poplag) not run (distances = the stages' PopFix x FOREST DETAIL, impostors to 1.2 km); no player build; no play-mode run (streaming release/grow untested outside edit mode); wind; snow on crowns; mirrors (impostor path inherited, unseen); card trees in the "before" frames wear the dress they stood up in (fall) even in the summer rows; Uptown grate trees mostly dropped (no clear-stem model narrow enough for 3-4 m to a facade); median row spot shows no median trees; far city (CityFar) and CityUnderstory stay cards.

## Coordinator at merge
1. `tools\store-assets.ps1 -Dest <build sandbox>` (Big Oak + Meadow trees; PSXLab already done).
2. With PSX_PC_CONTENT=1: run `PSXRacing.EditorTools.PSXRacingBuilder.BuildCityTreeKit` (or the city scene pass, which calls it). Full scene pass NOT needed. Resources/CityTreeKit.asset rides in the player's resources (not a bundle; the mountain forest ships in its stage scenes). Any non-PC scene pass deletes it.
3. No tile re-bake, no CityTileCache.Format change, no kit slot.
4. Shared-file edits: `Editor/PSXRacingBuilder.City.cs` (1 line), `Scripts/MeshForest.cs`, `Scripts/MeshForest.Gpu.cs` (pooled guards). New script metas committed (stable GUIDs).
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

Owner: "all cockpits should fit inside each car model, with mirrors properly mounted just outside the cockpit window". Sheets: scratchpad `mirrors
2\<shell>.png` (16 shells: ahead / over the dash / look left / look right as base | round 1 | round 2, the forced nose up/down pair with the dark inside off/on, rear-view picture, head-level look right, the mirrors from outside) and `mirrors
2\summary.png`.

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
ï»¿# HANDOFF - CURBS ROUND 2: GATES, ROLLED CURB, WHEEL VS CURB, PAN, LIDS (charlotte-curbs-drains, 2026-11-11 evening; NOT pushed, NOT merged)

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
ï»¿# HANDOFF - ROAD SEAMS / ROAD SLAB (charlotte-road-seams, 2026-11-11; NOT pushed, NOT merged)

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
# HANDOFF - HOUSES ROUND 3 (agent "roofs", charlotte-buildings-1111, 2026-11-11; NOT pushed, NOT merged)

## Drive-audit fix (first item; commit 13ce514b)
- Cause: stoops (round 1's HouseDoor and round 2's units/porches) and stepped unit fronts reached the Wesley Heights court lanes (houses front the court pavement within 1 m): the 0.3-0.9 m solids were the 14 INVISIBLE lane hits. Fix: HouseStepOffRoad (CityMeshes.HouseParts.cs) - no stoop, porch or unit step whose footprint comes within 1 m of any street's pavement (links included); the door stays.
- Drive audit (PSX_DRIVE_EDGES=24042,24054,24039,24041 = three house-court tiles, BOXED scope): INVISIBLE lane hits 14 -> 0; drawn Buildings faces in a lane 196 -> 0 (lanes total 120 = base); solids 88 = base.
- NOTE: CityAudit.RunDrive did not exit after writing city_drive.txt (19:52): Unity pid 45460 on PSXCity is still alive and blocks the sandbox (types' 42500 did the same). Not killed (rule).

## Round 3 changes (switches; =0 = round 2)
- PSX_CITY_HOUSEROOFTINT: shingle tones by era on the RoofTiles bucket's new vertex colours (alpha 255 = a house roof; PSX/Lit _HouseTint 2 tints only those; builder sets it on CityRoofTiles): charcoal, weathered grey, brown, near-black; green/red metal on 13 % of pre-war roofs; porch roofs too.
- Siding: per-house pick of 12 stronger colours (white 1 in 8) instead of a cell's 4 (round 1's PSX_CITY_HOUSETINT).
- PSX_CITY_HOUSEPARTS additions: garage doors (ranch 1-car, new 2-car, street long wall end; driveway side NOT used), dormers (pre-war, 1-2), shutters (half the houses, dark tints), front-door paint, the 45 m rule (no soffits/porch/dormer/garage/shutters on a house no street comes within 45 m of).
- L-corner sliver: buried roof extensions kept inside the plan at both corners; no soffit along a buried extension.
- Tools: Editor/CityRefSpots.Roofs.cs RunStreets (frontage chosen by rule: the street point within 300 m with most house centres 6-60 m out within 40 m along; poses kerb + raised 8 m), roof-closure audit in CityAudit.Windows.cs.

## Numbers (93-tile sample, run before the stoop fix and dormer fix)
- Windows CUT/BURIED 0/0; flat house roofs 0; census GAP 0, buried p95 0.60 (= r1/r2).
- Roof closure: 338 open points of 128,860 (first version counted fit-cut footprints as open; the corrected audit (drawn boxes) is committed but not re-run).
- Triangles a tile 5,010 (r2 4,820, base 1,773). Build ms a tile (r2 -> r3): ring 293 -> 200, OSM houses 219 -> 197, shot places 493 -> 324, worst 1,823 -> 1,068 (base 1,266). Shutters 6,715 pairs, garages 239, porches 941, dormers 0 (fixed after: too tall for the roofs), far-rule houses 913. PerfTour NOT run.

## Not done / generic still
- Pre-war streets are mostly BRICK (Plaza Midwood row reads as brick ranches; real: siding bungalows) - the round 1 brick share per cell needs an era cap. Siding colour variety still weak in frames. Subdivision/west street: no frontage found (fill houses stand 60-75 m from streets). No new street views (sheet uses round 1's). Dormers, the closure audit fix and the 60 m rule not re-shot (sandbox blocked).
- Trees: tm.houseBoxes = fill-house boxes + porch boxes (no heights). Real footprints: trees use f box / f.pts + 0.6 m + 1.41 m pad (RoadsideOccupancy). Volumes to keep crowns out of: wall box + 0.35 m eave / 0.25 m rake overhang, + 1.0 m stoop on the door wall, unit fronts +0.8 m (step + gable overhang); porch box (in houseBoxes) to floor + 3.1 m; roof to eave + up to 3.5 m (blocks 4.5), chimneys +0.6 m over the ridge.

## At merge
- Re-bake. New: RoofTiles bucket has vertex colours (no new slot); CityRoofTiles material needs _HouseTint = 2 (builder line in PSXRacingBuilder.City.cs). PSXLit.shader: one line. CityMeshes.cs: one line (RoofTiles col) + commit 4f6276fe restored the house hooks that 660aecf8 had misplaced (HEAD did not compile).

# HANDOFF - NON-HOUSE LOW-RISE CLASSES (agent "types", charlotte-buildings-1111, 2026-11-11; NOT pushed, NOT merged)

Shares the branch with agent "roofs" (houses). Sandbox PSXCity (lock "types", removed after each job). Owner: "replacing these very unrealistic generic textures ... top priority"; "building textures, glass, marble, concrete ... greatest improvement".

## Causes
- HEIGHTS: the Charlotte building import tags `height` in FEET with no unit (Atando Ave's 1-storey warehouse OSM 324608519 `height=26`, Walmart 22, Lowe's 24, industrial 38); export_osm.mjs parseHeight reads them as metres (tools/city/export_osm.mjs:1512) and style 1 (>= 20 m) + PickFacade's 3.8 m office floor made six-storey blocks. Untagged big boxes in the core got the exporter's 12/16 m guess (export_osm.mjs:1584).
- LOOKS: charlotte_bld.bytes keeps only use/mat/style, so a warehouse, a strip centre, a church, a school, a mill were all the same atlas box; PickFacade (CityMeshes.cs:598) gives style 1 a curtain wall 36 % of the time whatever the use (NoDa's Fat City Lofts, apartments); amenity=place_of_worship/school made churches and schools style-4 shops (export_osm.mjs:1597); a church or mill drawn by OSM building:parts took the hash look of its outline (Casa Viva church on Hamorton Pl: glass).
- SHAMROCK: the 3-storey brick block is NOT a frontage box (Shamrock Dr lies inside the core footprint box x -5844..2605, z 516..8674, where CityBuildings places no frontage boxes); no footprint within 45 m of the pose's right-hand lots is one either (all style-3 houses): not found this round.

## Changes (switches; `=0` = the picture before)
- DATA (no re-export): `tools/city/lowrise.py` (reads the Overpass cache + layers/landuse.json) -> `Scripts/City/CityLowRise.Data.cs` (17,921 rows, base64: point inside the outline, kind work/shop/worship/school/homes/mill/plain, flags height-tagged / levels / brick / metal / office zone / integer height). Matched at load in `CityMeshes.LowRiseApply` (called first in `CityBuildings.Precompute`, 54 ms); a footprint two rows land in keeps the stronger kind; a hidden outline's building:parts take its class look.
- PSX_CITY_LRHEIGHT: outside the Uptown loop, works/shop/plain with an integer height > 13 and no levels: feet -> metres (1,159); big (>= 2,000 m2) low boxes with a guessed 10.5-45 m: one tall storey 6.5-9 m (220); strip centres at least 6.2 m (the shopfront needs it); schools 4.2 m storeys (levels, else 1-2 by size); churches nave 6-8.5 m + gable; halls 8.5 m. 1,379 lowered in all; the 20 largest (footprint, area, kind, h before -> after, lat/lon) are in the shot log `types_log_r2.txt` (copied to scratchpad `types\`): 16 works sheds of 16,000-124,000 m2 (38 -> 11.6 m, 10 -> 8.6 m ...), 3 big-box shops (23 -> 7.0 m), 4 plain; one visible odd catch: a 37,512 m2 plain block at 35.4323,-80.9486 (McGuire nuclear station) 100 -> 30.5 m.
- PSX_CITY_LRLOOK (`Scripts/City/CityMeshes.LowRise.cs`): SHED (3,172): walls drawn by the kit - brick 45 % (painted block to the clerestory, one row of a brick column's windows), tilt-up concrete 35 % (column 7 stone + accent band), ribbed metal 20 % (column 7 panel); metal coping; parapet; a few high windows; dock-high roll-up doors with seals, bumpers and lights on the longest open side wall (1-14), a drive-in door with bollards; an office corner on the street front (storefront glazing lit as an office, glazed door, canopy, its lamp). STRIP (5,352): every shop outside the loop takes covShop (plain bays, canopy, blank sign band, parapet). CHURCH (583, < 1,600 m2): brick nave on its fitted box, one tall storey of the brick column, white gables, shingle gable roof with overhangs, white door surround, a steeple (brick tower, white belfry with louvres, spire) at the street end on 70 % of those < 1,400 m2. HALL (239) / SCHOOL (528) / MILL (18): brick looks (mill = loft or red-sash brick at 4.3 m storeys). APARTMENTS (use 2 outside the loop): never a curtain wall (brick families, home storeys). No new material, slot, bucket or draw: FacadeGlass, RoofTiles, RoofFlat buckets only.
- PSX_CITY_LRFRONTAGE (`CityBuildings.cs`): frontage boxes never within 60 m of a mapped house (0 removed: outside the core coverage drops OSM houses) and at most two storeys outside the core (144 of 5,151 lowered).
- Shared-file edits: `CityMeshes.cs` 6 lines (EndFacade lrActive reset; LowRiseChurch before the non-house path; LowRiseLook after PickFacade; strip covShop + LowRiseBegin + parapet; LowRiseWall in the wall loop; no cornice on a shed). `CityBuildings.cs` (mine).
- Tools: `Editor/CityRefSpots.Types.cs` (RunTypes: 8 places, look-at poses, night at 2, per-tile triangles, ring time, footprint description, PSX_DRIVE_EDGES).

## Proof
- Sheet: scratchpad `types\types_round2.png` (real street view | base (switches 0) | round 2; 8 places noon + Atando and the strip at night). Gates below.
- Tall lists: CityBeacons 35 (towers 90 m+) and CitySkyline 60 towers unchanged; skyline MID-RISE 534 -> 338 (196 sheds/shops that stood 20-45 m on feet heights no longer on the far skyline: intended).
- Cost (the building's tile, triangles base -> r2): Atando 18,578 -> 19,310 (+4 %), Bancroft 8,590 -> 11,826 (+38 %, docks), church 40,699 -> 40,837, strip 17,047 -> 16,855, others +-1 %; ring-of-25 build time within noise (5-9 s both).
- `CityAudit.RunDrive` with PSX_DRIVE_EDGES = the 8 shot streets (21 tiles + the default box), base (my switches 0, 16:55) vs r2 (branch head incl. roofs' houses, 19:00): my streets 74,967 probes both, walls 0 / steps 0 / holes 0 / blunt 0; WALLS IN LANES 0 -> 0; collider-only solids 88 -> 88 (Buildings MeshCollider 6 -> 6, of 1.99 M -> 2.95 M samples); blunt rail ends 8 -> 8, short rails 27 -> 27 (old). NEW in r2: INVISIBLE COLLIDERS lanes 0 -> 14, all `Buildings` back faces / ceilings at Wesley Heights house courts (Summit Greenway Ct (-3909, 5328), Walnut View Dr (-3829, 5787), Uptown West Dr, Cannon View Ln: x -3830..-3933, z 5328..5887) - no low-rise class footprint within 47 m of any of them: the house geometry's (agent "roofs"), not this round's; not split by a switch run (one more 25 min job).

## Still generic / not done
- Shed walls read flat (the parts column's concrete and panel at 2 m x 8 m; metal reads glassy under the metal bit); docks are on a side wall, so the street frames rarely show them (Atando's real docks face the street). Strip front faces its lot (covShop rule), from Albemarle Rd the frame shows the long side. Church/school brick is the office-sash column stretched (real: arched stained glass; tan stone school). NoDa mill is tan loft brick (real: red brick, arched windows); Fat City Lofts is now brick (real: grey panel + mural). Night frames: shed dark but for its office corner (as wanted); strip shopfronts not visible from the pose.
- Not run: player build, WebGL, PerfTour frame time, the city-wide window audit (no house code touched).

## At merge
- Tile geometry and far skyline changed: RE-BAKE the city tiles. CityTileCache.Format NOT changed; no new Slot / bucket / material; no data file regenerated (charlotte_bld.bytes untouched; new generated `CityLowRise.Data.cs` from tools/city/lowrise.py, `--check` supported). Codex requests: art workspace `Charlotte_Texture_Feedback_2026-11-11.md` section H.4; context `Charlotte_Texture_Context/2026-11-11/T_*` (22 files).

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
