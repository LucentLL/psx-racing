# PSX Racing Roadmap (2026-08-21)

Two plans: (I) finish the LifeSim port from Racing Game 2, (II) proper racing physics.
Artifact version: https://claude.ai/code/artifact/603964ae-4197-4e0b-b523-09b17c46ed0d
Sources: RG2 repo (`C:\Users\mcgee\code\Racing-Game-2`, src/sim 77 modules), this project's
Scripts/, and the v2 design journal from the original extraction workflow (wf_f1bf0f6a-122).

## THE YARD BECOMES A YARD, AND THE TOWN STOPS SENDING YOU HOME (2026-09-04)

Eight from a playtest. Three of them are the same structural mistake seen from
three angles.

**"Everything I do warps me back home to the garage."** Every shop in the town
is a menu PAGE, and every page lives in scene 0, which is the house. So pulling
onto a body shop's forecourt teleported the player home, and finishing there
left them at home with the car parked a hundred and fifty metres up a street
they were no longer standing in. `TownReturn` is the other half of the trip: it
remembers where the car was left, the pages offer OUT TO THE CAR — COLOURWORKS
instead of a way home, and `TownWorld` puts the car back on the spot. The hop
also stopped charging a second activity slot — one trip into town is one trip.

Two things fell out of looking at that. The town had **no `RaceHandoffApplier`
at all** — every builder but this one and Charlotte's hangs it on the race
manager, and a free-roam map has no race manager — so every drive into town was
in the scene's baked RX-7 whatever the save said. Buy a Charger, paint it green,
drive into town, and you were in a silver FD. And `TownWorld.Start` filled the
dealership and the yard BEFORE deciding where the player stood, so anything
throwing in the scenery aborted the method and left them on the baked spawn,
which is their own driveway, holding nothing. That is almost certainly the
literal "leave Pizzeria with pizza, warp back home in driveway". The arrival
comes first now: a crash in the scenery is not allowed to relocate the player.

**The delivery leaves by the road.** *"When I pick up a pizza for delivery, it
tells me to deliver it inside of the little town map. I should drive to the end
of the road and that transports me to a random race track."* It was launched
from a menu at the junction at the bottom of the player's own street, and the
HUD arrow pointed back at it, so the whole errand happened inside four hundred
metres. Both ends of the main street are `TownEdge` gates now — both, because
the shop is in the middle and either way out is out — and they do not ask
first. You cannot carry somebody's dinner to the edge of town by accident.

**The junkyard is somewhere you SEARCH.** *"Instead of inspecting the car for
all possible parts (including good, bad, worn, shot), looking at the car only
gave me one part to pull. It didn't require an inspection. This defeats the fun
of searching junkyards."* Right on every count — a shell handed over exactly one
part on sight, which is a vending machine with a car drawn on it. A shell now
carries five slots, each with its own grade, and about a third are already
stripped by somebody who got there first. Nothing is named until you LOOK IT
OVER, which is free but is a thing you have to do. `WreckScreen` is the page:
CLEAN / SOLID / SERVICEABLE / ROUGH / SCRAP in front of each part, price, days
to fit, and the gone ones shown struck out rather than hidden — a picked-over
shell has to LOOK picked over, or every car reads the same. Contents are seeded
per (week, wreck, slot), so pulling slot 2 cannot reshuffle slot 3 and a shell
you walked away from is the same shell when you walk back.

And the gate stopped being a shop: *"I don't like that when I drive to the
Junkyard it gives me an option to Walk the Shelves which shows me the News
tab."* WALK THE SHELVES threw the player back to the front end and opened the
classifieds' yard advert. The racked stock stays in the advert, where racked
stock belongs; the compound is for pulling your own, and the gate is now a sign.

**Doors hang on the jamb, not the handle.** *"The gas station door swings open
from the wrong side (the door handle is hinged to the building)."* The old rule
was one line — hinge on whichever end is further from the middle of the opening
— which is right for a matched pair and MEANINGLESS for a single leaf, whose two
ends are equidistant from its own centre. So single doors hinged on whichever
end the comparison happened to favour: a coin flip, and five of the forecourt's
eight had called it wrong. Three tries now, in order of how much they know: the
ARTIST'S OWN PIVOT (a door modelled to open has its origin at the hinge — that
is what an origin is for), then WHICH END IS AGAINST THE BUILDING (probed
against renderer bounds, because HingeDoors runs before the collider pass and
there is nothing to raycast at yet), then the pair rule.

**The car outside the pizzeria is yours.** *"Once inside the Pizzeria, the
exterior is generated with a random car... this only seems to happen when I
clock in to work. When I buy pizza it shows my car outside."* Both observations
were right, and the difference between them is which scene you are in: buying
never leaves the town, so that is the real car on the real apron; clocking on
loads `Pizzeria.unity`, whose kerb carried four grey slabs. The slabs are a
fallback for a scene with no save behind it and `PizzaShift` stands the real
shell and livery on the anchor.

**A car with a knock is not condition 99.** Condition was rolled off the
odometer and the disclosed problem was rolled beside it with no conversation
between them, so the paper printed "10,694 mi · cond 100 · Worn brakes". The
problem string is a REAL fault — `SeedHidden` puts it on the car the moment it
is bought — so the number was the only part of the advert that was lying. A
disclosed fault takes 30 points and caps at 62.

**At-fault incidents are gone**, at the owner's ask: "the player is punished
enough by repairing damages to their car." Counting one crash twice — once in
panels you pay to undo, once on a permanent record you cannot — is one
punishment with two invoices. The save field stays with a note, because a
removed field can take the rest of the object with it through JsonUtility.


## THE DOORS SWING, THE TOWN GETS A TRADE, AND THE WINDOW STOPS LYING (2026-08-31, later)

Four more from the same phone playtest, and three of them are the previous
pass's own fixes seen from the player's side.

**The doors were missing because we deleted them.** *"The doors are missing to
Pizzeria and Convenience store. They should swing open as player moves
through."* Correct on both counts: `WorldKit.OpenDoors` disabled every mesh
named `Door*` so the opening behind it was an opening, which is how "I am
unable to go inside Pizzeria" got fixed and how a hole in a wall got shipped.
It is `HingeDoors` now — the leaf stays, gets a box collider instead of a mesh
one because it MOVES, and hangs on a pivot at its outer jamb. `SwingDoor` opens
it at 3.4 m (a second and a half before you arrive, so there is no state in
which a door stops you), away from whichever side you are standing on, latching
that choice while the door is off its stop so it cannot change its mind and
close through you. Everything is measured: leaves are grouped by plan position
so a double door hinges on opposite jambs, and the width axis is whichever
horizontal extent of the group is longer — never a close call on something
metres wide and centimetres thick, even on a forecourt yawed three degrees off
the world axes. The baked vectors are in the BUILDING's frame, not the world's,
because the restaurants are prefabs CityProps stands up at whatever yaw the
street runs at.

**"I drove to work but was unable to find a pizza inside to deliver."** The
walk-in shop is fine — the self-test now stands in it and confirms four boxes
on the counter, 7.3 m from the spawn with a 4.5 m reach. What the player walked
into was the TOWN's pizzeria, which is a storefront prop with a hollow inside
and no shift in it, and the only hook was on the centre of its 21 m bounding
box, eight metres from the only door. Three fixes: the drive-up trigger is the
whole 26 x 14 apron rather than a 12 x 9 box in the middle of it (park on the
east half of the shop's own frontage and you used to be offered nothing); the
walk-up hook is at the DOORWAY, measured off the model's own leaf
(`WorldKit.DoorwayOf`, called before the hinges go on, because a hinged leaf
can be open); and there is a second hook INSIDE, so walking through the door
cannot find an empty room. Both carry the same offer, rewritten together.

**A garage for the mechanic and one for the paint shop.** MECHANIC SERVICES had
lived four presses down a menu with no address in the world, and RESPRAY did
not exist at all — even though every shell in the pack carries a handful of
baked liveries and nothing had ever let the player choose one. DELMAR AUTO and
COLOURWORKS are two authored units on the south side of the main street (there
is no garage, workshop or spray booth in either art tree), each a slab, a shell
with a hole in the front and a sign saying which trade it is. The front is four
pieces — two piers, a middle post and a header — because a BoxCollider cannot
have a hole in it and a shell built as one box is a building you cannot walk
into, which is the bug the gas station's single slab was.

`Paint` is now the ONE answer to "what colour is this car". That mattered more
than it looked: an owned car had been coloured by three different salts in three
places, so it could be silver on the menu, blue in the garage and silver again
on track. The override is stored as a livery NAME rather than an index, because
the index is a position in an array CarModelBaker rebuilds from the pack. A
respray is also a refinish, so the panels come back at 100%.

**"Power cut out to car just as the race started. My controller felt like it
disconnected."** The screenshot carried the answer: the page's own CLICK HERE TO
RESUME CONTROL banner was up, so `document.hasFocus()` was false. The Gamepad
API only reports to a focused document — Chrome freezes every axis at rest — and
the keyboard goes with it, while `requestAnimationFrame` keeps firing for a
window that is merely unfocused rather than hidden. So the race ran, the field
drove away, and the player's car sat on the grid answering nothing.
`Application.runInBackground` does not cover this: it is about VISIBILITY.
`FocusGuard` holds the clock (and the audio) until focus returns, told by both
Unity's own event and a `SendMessage` from the template's blur/focus listeners.
It thaws on focus, or on any input at all — an input event is proof of focus —
and a platform that hands over input while claiming not to be focused has its
`Application.isFocused` retired for the session, because that flicker loop would
be worse than the bug.

**And the fuel gauge moved.** It sat top-left under the lap counter, which is
where the pause menu's always-visible MENU button lives — a different canvas at
device resolution, so neither layout could see the collision. It covered
"FUEL 100%" in every screenshot the owner sent. It is under the position
readout now.

## THE JOB IS A JOURNEY, AND THE TOWN GROWS DOORS (2026-08-31)

Six asks from a phone playtest, and they share one root: the town shipped as a
drive-past. You could stop at things but not get OUT at them, the work "commute"
was a teleport, and the two shops that sell cars each did half of what a shop
does. This pass makes the town a place made of doors.

**Getting out of the car, anywhere in town.** Reported as *"I can't get out of
the car at the Pizzeria to pick up delivery"* — and there was genuinely no way:
`ForecourtMode` only offered GET OUT at a pump with a thirsty tank.
`anywhereInTown` (set by the town builder alone) now offers it whenever the car
is stopped, on the SECOND verb — E / pad-west, the same pair the walk-in scenes
use — so a venue's F prompt and the door handle never fight over one key. On
touch, the ACTION button falls through to GET OUT only when no pump, venue or
order window is claiming it. On foot, the venues grew `FootTarget` doors
(`TownWorld.BuildFootDoors`): the shop's door clocks you on or sells over the
counter, the dealership's office and the yard's gate open their pages, your own
garage bay ends the day.

**The invisible walls were one box.** The station's collider was a single slab
from the pump line to the back of the lot — fine from a car, and a wall of air
everywhere a walker went ("invisible walls stop me from walking into areas like
one of the gas stations"). `AddStationPieceColliders` replaces it on circuits
AND in town: every station piece gets a local-space box on its own mesh (ground
clutter under 35 cm and the canopy overhead are skipped), so the shop stops you
at its walls and the concrete between them is concrete. The town also gained the
`StoreDoor` the circuits always had, so the 6TWELVE works there on foot.

**GO TO WORK is a drive.** The owner's spec, verbatim: *"player should drive
from home to pizzeria, then pick up pizza, drive to part of map, a new menu to
Random Race to 'deliver'."* `PizzaRun` (new static mailbox beside RaceHandoff)
threads it: DoWork loads the TOWN with a HUD signpost (the slot Charlotte's food
cue uses — same eight-point arrow relative to the car's heading); clocking on at
the shop hops through the front end so the commute banks, then loads the
counter; the shop door with boxes in hand returns you to the town AT THE SHOP
KERB with the cargo rig live on the passenger seat; the JUNCTION menu grows
MAKE THE DELIVERY, which launches the same solo time-trial as before. The drop
is scored against the WORSE of the town leg and the race leg
(`RaceHandoff.CarryCondition`) — a box thrown into the footwell on Main Street
stays thrown. Two rules keep the economy honest: commute legs ride
`RaceHandoff.CommuteLeg` so they bank metres and fuel but never a slot (the
shift's slot is paid once, at the shop door — without this one delivery cost
two thirds of a day), and an order that leaves town any other way is abandoned
with a diary line, never silently carried.

**The dealership sells new AND used, and hands over the keys.**
`CarMarket.RefreshLot` guarantees two current-model-year cars (swapping out
trade-ins, never growing the eight bays); the page splits IN THE SHOWROOM / ON
THE LOT. Test drives are allowed from the lot now — the salesman rides along —
and haggling works there too: small and grudging on a new car (sticker is
sticker, 2-8% when it lands), the full private-seller rules on used, and
`Viewings.Reprice` already discounts every fault an inspection turns up, which
is what makes GET UNDER IT at a dealership worth the trouble.

**A private seller can say NO, and the no is worth money.** `AskStands` /
`AskTestDrive` roll once per visit (28% / 25%); a refusal is permanent for the
visit, locks that access, and banks `Viewing.leverage` — each point makes the
next haggle harder to rebuff and cuts deeper, and reopens a haggle already
spent ("new information, new conversation", the same rule a test-drive find
uses). The raise row on a stranger's driveway is now ASK FOR STANDS the first
time; the dealership never refuses (it is their hoist).

**Backing out no longer loses the car.** Three holes, all real: Escape from an
inspection running on a phantom obeyed the static parent table and landed on
your OWN car's page; the market had no way back to an open conversation; and
WALK AWAY was one press. Now `ParentTab` is state-aware (phantom inspection →
viewing; lot viewing → dealer), the classifieds lead with BACK TO THE SELLER
while a visit is open, and WALK AWAY arms a two-press confirm ("Give up on this
car?") with the safe row first for pad players.

**The yard stopped burying its stock.** The owner: *"junkyard cars don't need
to be half buried. They can be on jack stands or cinder blocks. When parts like
wheels are stripped, they should be removed from the car."* Wreck spots are
upright at grade now (yaw variety only), and the SHELVES decide the stripping:
two wheels per tyre-lane part in stock (floor three, up to three per car), each
bare corner held up by a crosswise two-block cinder stack built into
`CarShell.Spawn` — the stack tops out at hub height, so the body sits exactly
where its wheels would have put it.

## THE PIZZA IS THE SCORE (2026-08-29, second pass)

**The pitch, from the owner, and it settles what this game is:** *"like Initial
D, you're a delivery driver. But for Pizza."*

That is not a theme, it is a mechanic. Initial D's tofu is a cup of water in the
cup holder, and the whole discipline of the driving is not spilling it. So the
cargo stops being a number derived from an impact tally and becomes **an object
on the passenger seat**, with its own physics, its own camera, and the tip
graded off what is left of it.

Asked for: boxes carried HORIZONTALLY with real pizzas in them; a Pizza Cam
showing the box slide around the seat, bounce and flip in a crash, and the pizza
fall or slip out; on mobile that cam sits above the steering wheel; multiple
pizzas per run, stacked, moving independently, top one most at risk.

### How the cargo is simulated, and why it is not parented to the car

A rigidbody does not inherit its parent's motion. A box made a child of a moving
car falls straight out of the back of it, and a box made kinematic is not a
simulation at all. So the cargo lives in its **own place** — a tray four
kilometres under the track, where nothing exists to collide with, nothing casts
rays, and the cargo camera's 2.2 m far plane could not see the world even if
there were — and the car is brought to the cargo:

- the tray takes the car's rotation **with the yaw removed**, so it pitches under
  braking and rolls in a corner (and tips right over when the car does) without
  spinning on its own axis every time the player turns the wheel;
- every loose body gets the car's own measured acceleration applied backwards,
  which IS the pseudo-force a passenger feels. Clamped to 12 g, because a
  finite-difference acceleration through a wall impact is one frame of an
  enormous number and it tunnels a box straight out of the tray.

Everything good falls out of that arithmetic rather than being special-cased. A
car in free fall accelerates at g, so the boxes get −g, cancel gravity and float
— which is what happens to a pizza over a crest. And **"the top one is most at
risk" needs no rule at all**: it is the box with nothing on top of it holding it
down, so it is the one that goes.

The box is a CONTAINER, not a block: floor, four walls, and a ceiling collider
that exists only while the lid is on. Opening the box destroys the ceiling and
cuts the lid loose as its own body — until that moment the pizza rides out every
bump, and after it, physics decides. A box tipped past ~51°, or one that ends up
in the footwell, opens.

Condition per box: jostling (the pizza's motion RELATIVE to its box — the whole
car is moving and none of that matters to the cheese), −0.45 if the pizza leaves
its box, −0.30 if the box went past horizontal, −0.22 if it left the seat. The
ORDER is graded on the mean, because the customer opens every box: one ruined
pizza in three is a third of an order ruined.

`RaceHandoff.CargoCondition` overrides the old `PizzaCondition(damage, hardHits)`
estimate whenever a cargo rig actually ran, and that is the point — a driver who
clouts a wall dead square can keep every box flat, and one who never touches
anything can throw the lot into the footwell on a crest taken too fast. The
impact tally was always a stand-in for this. It stays as the fallback for a
scene with no rig, and for the self-test, which has no scene at all.

### The Pizza Cam

A second camera on four rigidbodies rendering 160x108 point-filtered — roughly
the resolution of the rest of the game, and it costs nothing. Above the steering
wheel on mobile, at the wheel's own reported box (`TouchControls.WheelInset`,
which exists because a fraction of the screen that clears the wheel is a
different fraction every time the panel is retuned); bottom-left without one.

**The camera does not tilt with the car.** It is fixed relative to gravity, so
what the player watches is the SEAT rolling and pitching under the boxes. A
camera bolted to the car would hold the seat still and tilt a background that
isn't there — the attitude is the information.

It exists so the tip is not a punishment. A number that drains for reasons the
player cannot see is a tax; a box visibly walking toward the footwell on the
approach to a corner is a reason to lift.

### Carried horizontally, and the bug that caused it

`Instantiate` + `SetParent(cam, false)` **discards the local rotation chain**,
and the pack's box only lies flat because of the transform it sits under on its
shelf. So the player was handed a 70 cm box stood on its edge like a briefcase.

Fixed at the source: `PizzaCargoBaker` cuts the box, its lid, ten toppings and
ten loose slices out of `Pizzeria_Props.fbx`, **measures** each one's thinnest
axis and turns it up if it is not already, seats it on its base, and scales the
whole family off the box — a real 16-inch box is 41 cm, and the pack's is 70. The
prefabs land in `Resources/PizzaCargo` because a race scene cannot
AssetDatabase-load an FBX, the same reason CityProps exists. The carried stack in
the shop and the cargo on the seat are now built from the same parts, so the
boxes the player picks up ARE the boxes that ride to the drop.

### Multiple pizzas

`LifeRules.RollOrderToppings` rolls 1-3 boxes, weighted toward the small orders
(52/33/15) because every extra box is another independent thing sliding around a
seat — a three-box run should be the night you remember, not the default. Pay is
per box. The counter visibly loses exactly what the player picked up, and the
carried stack is built to `LifeRules.MaxOrderBoxes` and revealed a box at a time,
because the scene is baked once and the order is not rolled until the player is
standing at the counter.

### Verification

`PizzaCargoPreview` photographs the baked parts — one box shut, one open with its
pizza showing, and the three-box order — and LOGS whether each part came out flat
or on its edge. `PizzeriaPreview` now turns the carried stack ON before it
shoots, because it is built disabled and a preview of the scene as saved is a
preview of a player holding nothing: the exact frame the vertical-box bug lived
in. The self-test asserts the box is flat, is 41 cm, is box-thick, that every
topping an order can roll exists, that the pizza FITS IN THE BOX, that an order
is never empty nor taller than the carried stack, and that a ruined cargo
overrides a clean damage score in both directions.

## THE DELIVERY RUN, AND THE JITTER NOBODY COULD SEE (2026-08-29)

Asked for, from a phone playtest of the pizza shop: (1) "many textures are
interfering when moving. this should never happen in buildings or when driving";
(2) "I try to leave the front (and only) door and it doesn't let me leave to make
the delivery"; (3) "later I will add city deliveries, but for now just make it
choose a random race track"; (4) "tip is based on how quickly the track is
completed. wrecked the car damages the pizza and lowers tip. might even get the
delivery denied."

### The jitter: vertex snapping, and five preview tools that hid it

`PSXGlobals.vertexSnap` now defaults to **false**.

The snap quantises a vertex's NDC xy to the framebuffer grid and leaves its
DEPTH alone, so the depth rasterised across a polygon stops describing where
that polygon is. Two surfaces lying on each other — a table top and its trim, a
road and its painted line, a wall and its poster — then disagree about which is
in front, per pixel, and differently every frame. The error is ANGULAR, so its
world size grows with distance without bound, and it is worst on exactly the
surfaces you look at most: floors and roads at a grazing angle, where half a
pixel sideways is a long way forward. It is invisible at 960 lines and vicious
at 240.

The same call `_Affine` got in PSXLit.shader, for the same reason and from the
same person. Both are only ever right on small triangles, and almost nothing in
this game is made of small triangles.

**Why no screenshot pass ever caught it: every preview tool in the project
forces `_PSXSnap` to 0 before it shoots.** CityPreview, TownPreview,
PizzeriaPreview, TireFxPreview, HoistPreview — all five. So every screenshot the
look was ever signed off from was of a renderer the player did not have, and the
one artefact the tools could not show is the one that came back from the device.
PizzeriaPreview now shoots the pair deliberately (`snap_ON_240` / `snap_OFF_240`,
at the game's own resolution and clip planes) so the evidence survives, and
`LifeSimSelfTest.TestVertexSnapOff` opens every scene in `SceneOrder()` and fails
the build if any of them ships with it on — `vertexSnap` is a SERIALISED field,
so turning the default off changes nothing about a scene nobody rebuilt.

### The door

`PizzaShift` put the drive on a hook attached to the player's car, which the
scene builder parks 8.2 m out on the street. The shop front is a sealed shell —
the door leaf is 2.43 m tall so `AddColliders` gives it a MeshCollider like every
other panel in the pack — so that car was never once reachable, and the door
itself refused to open while carrying ("somebody is waiting on that"). A player
who collected an order had no remaining action anywhere in the room.

**A door you cannot walk through has to BE the exit, not guard one.** The door
is now the control: carrying, it reads `OUT TO THE CAR — START THE RUN` and
names the drop and the money; empty-handed it is `CLOCK OFF — GO HOME`. Its
anchor moved to the INSIDE of the threshold (it was 80 cm out on the street side,
reachable only by leaning on the glass) and its range went 2.6 → 3.4 m.

The counter became two-way with it — `PUT THE ORDER BACK` — because the door now
starts the run, so every one of `Drive`'s refusals would otherwise strand a
player holding a pizza with no action in the room and no way out of the scene.
That is the same bug shape this pass exists to fix. The ticket does not re-roll
on a put-back: pay and destination are the shop's decision, not a slot machine.

### The drop, graded

`DeliveryTrackIndex` rolls at random instead of rotating by day, and **rolls from
the venues the car can finish**. Charlotte is still excluded (no finish line, so
nothing to arrive at); everything else is fair game including the strips. The
fuel filter is new and load-bearing: the parkway stage is 6.9 km with no
forecourt on it, and rotating by day made "dispatched somewhere you cannot reach"
a rare unlucky Tuesday where rolling at random would make it one shift in eight.
With nothing in the catalog inside the tank it sends them to the cheapest run
there is rather than refusing the shift.

`LifeRules.ScoreDelivery` is the whole grade, and it exists as ONE function
because two places consume it: the HUD counts the tip down live in the slot that
used to say "POS 1/1", and `ApplyRaceResult` pays it. A readout that promises $40
against a wallet that grants $18 is worse than no readout.

- **Par** = `6 s + RaceMeters / 22 m/s` (35 m/s on a drag event, which is a
  standing start and then flat out — grading a quarter mile against 79 km/h would
  make every strip delivery a free bonus). Measured off the venue's own raced
  distance so it means the same thing at a quarter mile and at 6.9 km.
- **Clock**: 1.25x at 0.6x par, 1.0x on par, sliding to 0.15x by 2.2x par. The
  floor is not zero — a cold pizza is still a delivered pizza.
- **Box**: `1 − (damage − 6) × 0.022 − hardHits × 0.20`. The free allowance is
  there so the job is graded on driving rather than on luck; the hard-hit term is
  separate because a box does not care about total energy, it cares how many
  times the car stopped dead.
- **Refused** below 0.25 condition: no tip at all, and −3 workRep. It is the only
  way the job can go backwards, which is what makes driving carefully worth
  anything. You still ate — leaving the meal off a failed run would mean one
  crash cost the tip AND the dinner.

The counter quote is what the run pays ON PAR, worded that way ("$41 on the door,
beat 2:30 for more") because the live readout starts at the fast multiplier and
"$41 if it's there in 2:30" followed by $51 on the grid reads as the game
inflating a number to take it back.

### Verification

`tools/delivery-pass.ps1` — mirror, scene build, PizzeriaPreview (rendering, so
no `-nographics`), self-test. The self-test grew nine assertions: par is sane at
every venue, quicker pays more, hitting things pays less, a scrape inside the
allowance costs nothing, a wreck is refused and pays zero, no roll escapes the
quoted band, no delivery is routed past the tank, an empty tank gets the shortest
run there is, and no scene ships with vertex snapping on.

## A HOUSE, A TOWN, AND SOMEWHERE TO EAT (2026-08-28)

Asked for: wire the new asset packs (House, Trailer_Park, BurgerPiz, Pizzeria,
food props) into the life sim — "I'd like the player to start with a one car
garage house … Houses can be placed around Charlotte and Emerald Isle similar
to Google Maps" — and fix the rival race that spawned both cars off the track.

**The starter house is real.** The walk-in garage scene is now the player's
lot: the furnished two-storey house from the pack (with its matching collider
mesh), the one-car garage standing OPEN with the active car inside, driveway
and kerb bays for the rest of the fleet, and the garage fixtures (rack, tool
board, bench) rehomed against the garage's own walls. A garage fridge runs
`LifeRules.EatMeal` — same rule as the EAT tab, walk-up-able. The housing
ladder is houses now (`house1g/house2g/house3g`, save v6 renames the old
apartment keys): same rents, same slots, so the economy did not move — but the
starting rung reads "SMALL HOUSE — 1-CAR GARAGE" and the walk-in scene IS it.

**Charlotte grew suburbs and restaurants.** `CityBuildings.B` gained a `kind`:
0 stays a procedural facade box; anything else is a `CityProps` prefab the
streamed tile instantiates (baked to `Resources/CityProps` by the prop baker —
a runtime tile cannot AssetDatabase-load an FBX). Outer-suburb frontage is now
~60% real houses (past 5 km, some trailers); midrise shop streets salt in the
pizzeria pack's eight mid-rise blocks; and ten restaurants — STACK BURGER
drive-thrus alternating with SLICE HOUSE pizzerias — sit on big surface
streets, spaced ≥1.5 km, all deterministic off the edge-index hash. Prefab
lots claim EVERY 18 m occupancy cell under their footprint (all-or-nothing),
because two identical prefabs interpenetrating read as a glitch where two
different procedural boxes just read dense.

**Ordering food is the pump pattern minus the walking.** `DriveThru` is a
trigger volume baked onto the restaurant prefabs: roll in, stop under 4.5
km/h, F/pad-South/touch-ACTION opens a `StoreScreen` with the venue's stock —
eat-now items (health + the hunger clock, like the 6TWELVE) and take-home
packs that ARE the EAT tab's grocery rows made physical (burger family bag =
junk $8→4 meals, pizzeria pies = regular $25→5). One economy, two doors.
`RaceHUD`'s city path draws `DriveThru.Prompt` and relabels the touch button
ORDER. FOOD DELIVERY is back in the job book too (RG2 had it; the port dropped
it): $96/day advertised as tips (roll ±$34-45 around it), and you eat on shift
— junk, because the rollover's opinion of that diet is the correct one.

**Emerald Isle is a town now.** The theme grew `stageHomes`: the quarter mile
runs past ~50 houses and trailers seated on the stage DEM (skipping lots that
are steep, underwater, on the staging box, or on a bridge approach), with one
burger box past the traps and a pizzeria mid-island. Same baked prefabs as
Charlotte, instantiated at build time.

### The scale pass (2026-08-28, same day, after play-testing)

Four reports, three of them one root cause. **"My character appears 3ft tall
(half the door height)"** and **"the car is in the ground of the garage"** and
**"many houses in Emerald Isle are underground"** — plus the older **"at the
gas pumps the character felt eight feet tall"**.

**The pack is not built to real-world scale, and nothing was checking.** Its
interior doors measure 2.51 m against a real 2.03 — 1.23x oversized. The
player's eye is a fixed 1.62 m, so an oversized house does not look like a big
house, it makes the PLAYER look like a child. The house is now scaled from its
own doors (0.81), which lands the garage door at a real 2.84 x 2.43 m and the
garage interior at 3.10 m across. The same correction goes into the city prop
prefabs for the residential models and the single-storey restaurant (whose
6.5 m parapet was a storey and a half too tall); the multi-storey blocks are
left alone, because 13.5 m over four floors is already right.

**The same bug inverted at the pumps.** `PumpHeightM` was 1.85 — a pump BODY
height — applied to a `Fuel_pump` object that measures 2.81 m because it
includes the price display. That shrank the whole forecourt by a sixth. Now
2.2 m, which is a real dispenser over its head.

**The garage floor is 0.71 m above the model's origin, and the collider mesh
has no garage floor in it at all.** The house was being seated at a flat -0.04
with the bays at 0, so the car parked most of a metre under its own slab. The
house is now seated by its MEASURED garage-door base, so the floor lands at
y=0 — which is where the lot's own ground slab is, and that slab is therefore
what the garage stands on.

**Emerald Isle's houses were seated from the height FUNCTION, not the ground.**
`GroundHeightAt` and the terrain that actually gets built disagree — the stage
ground is chunked, surface-masked and pinned to the road corridor after the DEM
is sampled. Lots now raycast the real collider under all four corners and the
centre, seat on the highest, and refuse any site that is wet, steep, or off the
edge of the mesh. (The same lesson as the garage door: measure the thing, not
the function that describes it.)

**"The ground is warping as I walk (we fixed this for race tracks)"** — right,
and for the same reason. The lawn was one 64 x 48 m quad, so the PSX vertex
snap moved four corners and dragged the whole surface between them. Yard,
driveway and street are subdivided meshes now at ~2 m cells.

**Inverted Y.** `LookPrefs` — a PlayerPrefs bool applied at the ONE place every
pitch source is summed, so mouse, right stick, arrow keys and the phone's thumb
drag cannot disagree. Reachable three ways because the walking scenes have no
pause menu: `I` on foot, a LOOK Y row in the pause menu, and a row beside WALK
INTO YOUR HOUSE on the home screen (the only route a touch player has).

Six new self-test assertions, each one the shape of a report that got here by
eye: doors are door-sized, the garage floor is at y=0, bay 0 is on it, the eye
is at human height, the yard is subdivided, and every baked prop is the size
its `CityProps.Def` claims — that last one being the invariant the placement
maths depends on and the one that would have caught the oversized house.

### "Where are the fast food drive-thru and Pizza building?" (2026-08-28)

They were all there. The player spawns at Trade & Tryon, the placement rule
keeps restaurants 1.2-9.5 km out (a drive-thru does not belong among the
towers) and 1.5 km apart, and the far plane is 360 m with fog closing before
it. Ten buildings over 2,574 km of road that you can only see from 360 m away
are findable by accident and no other way. Placement was right; DISCOVERY was
missing.

The free-roam HUD now carries a signpost in the slot the OSM attribution uses
for its first seven seconds: `/^  STACK BURGER  1.4 km`. An eight-point arrow
relative to WHERE THE CAR IS POINTING, not a compass bearing — a player
mid-corner can act on "over your left shoulder" and cannot act on
"north-north-east". `CityWorld` flattens the restaurants out of the tile
buckets once into a food index; `RaceHUD.FoodCue` refreshes on a 0.4 s timer
rather than per frame, because it is a concatenated string on a screen whose
whole design is change-gated. Inside 30 m it drops to the name alone and the
ORDER prompt takes over.

The self-test now fails the build if Charlotte ends up with fewer than three
of each. Those placement gates are strict enough that one tweak could empty
the city of food and leave the new signpost pointing at nothing.

KNOWN, and deliberately not fixed here: Emerald Isle's two restaurants are
SCENERY. `RaceManager.OnCarFinished` disables player input at the traps, so
the car coasts through the shutdown area unable to order, and the island has
no free-roam mode. Making them live means either a free roam for the island or
leaving input enabled through the shutdown area after a drag finish.

**The rival-race spawn.** The 1v1 override teleported the player to
`rival.right * 5.2 m` — measured for the circuits' 2×2 grid. On a drag venue
the field is already abreast in FOUR-car lanes, so the surviving pair sat
lopsided, and on an 11 m stage road the offset ran the player to the wall.
`RaceHandoffApplier` now restages a drag 1v1 onto the two CENTRE lanes off the
TrackPath itself (`±min(roadWidth/6, 2.75)` about the rival's station).
`LifeSimSelfTest` gained `TestGridStaging` — opens every BUILT scene and
asserts all four baked cars AND the 1v1 restage sit inside the road width — so
this class of bug now fails the pipeline instead of a race night.

## BOGUE BANKS — A DRAG RACE ON A GRADE (2026-08-27)

Asked for: "next I'd like to try a drag race on the outer banks. Something
like Emerald Isle works for this, with a large bridge on each end, could drag
over either bridge, or down the outback strip." Pushed back that a high-rise
bridge produces trap speeds not comparable with the flat strips; answer was
"I understand its not traditional and won't have traditional times, but I
find it interesting. why not drag race on a grade? it can be a straight line
race (if the bridges are straight)." They are, and it is.

**Three venues off one barrier island**, all real road, all baked by
`tools/bogue/fetch_bogue.mjs`:

| id | run | profile |
|---|---|---|
| `EmeraldIsle` | 402.3 m | flat — 1.5% max, on the island's longest true straight (2741 m of Emerald Drive) |
| `LangstonBridge` | 1408 m | climb 644 m @ 4.5%, crest, descend 764 m @ 3.8% |
| `AtlanticBeachBridge` | 1140 m | climb 420 m @ 6.2%, crest, descend 720 m @ 4.1% |

(Emerald Isle is on **Bogue Banks** — Crystal Coast, "Southern Outer Banks" —
not the Outer Banks proper. The distinction decides which lighthouses are
road-reachable, which is the next request.)

### Four things this needed that the parkway bake did not

**1. Routing, not chaining.** The parkway is one road with no junctions worth
the name, so `fetch_brp` grows a chain by matching way endpoints. NC-58 forks
at Atlantic Beach — north over the bridge, east along Fort Macon Road — and a
chainer takes whichever way matches first. The first attempt walked 27 km up
the MAINLAND leg of NC-58 toward Jacksonville. `route.mjs` builds a graph and
Dijkstras between anchors, with per-road-class cost so the line does not cut
through beach-house cul-de-sacs. Every venue is then a list of anchors, and
the 72 km island-and-mainland circuit falls out of the same function for free.

**2. The DEM does not know the bridges exist.** SRTM is a radar return off the
water; both spans read as sea level, and the entire point of them is that they
are 20 m in the air. Decks are SYNTHESISED against the real 65 ft Intracoastal
clearance — and the crown goes where the route actually crosses the AIWW,
which OSM maps as a named waterway, not at the midpoint of the span. On
Langston those are 60 m apart and on Atlantic Beach 124 m, which is the
difference between a symmetric hump and a bridge where the climb and the
descent are honestly different lengths. Smoothstep each side of the crown
gives zero grade at both abutments AND at the crown — no kink anywhere — with
peak grade exactly 1.5x the average, which lands both spans in the 4-6% a real
high-rise runs.

**3. A surface mask.** A mountain is ground everywhere; a barrier island is
ocean, sound, sand and scrub. The bake writes a byte grid beside the DEM from
OSM's coastline (land-on-left) and its beach polygons, and the near chunks
split into a scrub submesh and a sand one. The sign of that test is the one
thing that can go catastrophically wrong without throwing — an inverted
coastline produces a beautiful bake of an island that is entirely underwater —
so the bake walks its own route afterwards and fails if tarmac is on open
water with no deck over it.

**4. The sea is one flat plane, not a polygon.** The tempting design builds
water geometry only where the mask says water, and it is wrong: then the
SHORELINE is a boundary you have to keep aligned with the terrain, and every
disagreement is a crack you can see the sky through. A flat plane at a known
height has no shoreline at all — the coast is wherever the ground rises
through it, exact by construction and free. The bake guarantees the clearance
(land held 0.4 m above, seabed 4 m below), so there is nothing to z-fight.

### The catalog flag that had to be split

`TrackDef.drag` meant two things at once — "synthetic flat strip geometry" and
"run this as a drag race" — and the builder gates bridge decks and piers off
it. Setting it on a bridge would have deleted the bridge. The RUNTIME already
made the right distinction (`TrackPath.pointToPoint` for geometry,
`TrackPath.drag` for the top-down camera and trap speed, `HasEnds` for their
union); only the catalog was still conflating them. So: `drag` keeps meaning
synthetic strip, new `dragEvent` carries the presentation, `IsDragEvent` is
what anything player-facing asks. The grid staging then had to learn that a
stage's waypoint 0 is the far end of the lead-in, not the start line — staging
there would have started both bridge runs 150 m back down the causeway.

### Three bugs worth remembering

**Catmull-Rom on unequal segments.** OSM models both bridges as a single
two-vertex way, so the point list arriving at the deck read 40 m, 40 m,
1288 m, and the uniform-parameterisation spline derived an enormous tangent at
that junction: an **11 m-radius hairpin 48 m past the Atlantic Beach start
line**, on the one venue whose whole premise is that it is straight. It
splayed the road ribbon to ~60 m wide, flung the parapets apart, and buried
the grid in its own tarmac. Densifying long segments to 25 m before splining
makes the parameterisation uniform and leaves straight lines exactly straight.
Diagnosed only after guessing wrong once — the first theory was the lead-in
walk turning at a junction, which was worth fixing anyway but was not this.

**The road was in raw ASL and the terrain in baseM-relative metres**, so the
first bake put every road a clean 6 m under its own ground. Sampling and
rebasing now happen in the same expression.

**A drag race can fall off a bridge.** Every other stuck state resolves or is
at least standing on something; falling is above `movingKmh` all the way down,
so the watchdog never armed and the car descended for ever. `StuckRecovery`
now derives a floor from the route's own lowest waypoint and recovers with no
warning banner — there is no freeing yourself from that one — and a stage with
a sea collides its whole near band rather than the mountain's 120 m.

### Still to do

- The full **71.97 km circuit** — island out on NC-58, back on NC-24 through
  Newport, a bridge at each end. Routed and measured; needs a cyclic stage
  (today `stage` implies point-to-point) before it can be a lap.
- **Lighthouse to lighthouse**: Currituck Beach → Bodie Island → Cape Hatteras,
  ~110-120 km of NC-12 with the Basnight and Rodanthe bridges on it. Ocracoke
  is ferry-only, so the road chain ends at Hatteras. 16x the parkway: terrain
  chunks already scale, but the road ribbon at 27,500 stations needs chunking.

## THE BLUE RIDGE PARKWAY (2026-08-27)

Asked for: touge. "I often see Touge games from Japan or California canyon
like Initial D or NFS Carbon. I'm thinking the Blue Ridge Parkway would be a
good idea. Maybe not the entire thing, but sections of it for racing and
sight-seeing. Let's test out adding mountains — that's almost entirely what
the BRP is."

**The section is the Grandfather Mountain mile, southbound.** 7.0 km from
below Rough Ridge, across the Linn Cove Viaduct at two-thirds distance, to a
finish at Beacon Heights — the most famous stretch of the parkway's 469
miles, and the photo the request came with. Southbound puts the viaduct's
outside lane against the Wilson Creek valley and the signature corner right
before the flag.

**Real road, real mountain.** `tools/brp/fetch_brp.mjs` pulls the parkway
centreline from OpenStreetMap (credited in the blurb, like Charlotte) and
elevation from SRTM 1-arc-second — the skadi `.hgt` mirror, raw int16, no
image decoding — then bakes 4 m waypoints into `Resources/brp_stage.json`
and two DEM grids into `Art/BRP` (12 m posts near, 60 m far). All eight real
bridge spans came off the OSM `bridge=yes` tags, and the 392 m one IS the
Linn Cove Viaduct (real length 379). DEM heights along a ledge road carry
the cliff in them, so the profile is smoothed sigma-85 with the grade
clamped at 8.5% and the clamp's own kinks re-rounded — a hard clamp leaves a
vertical hairpin, and the crest-radius floor exists precisely to catch it.
Min corner radius 26.9 m, min crest radius 424 m, max grade 8.5%: a real
mountain road that happens to pass every floor the invented circuits set.

**A stage is the third kind of track.** `TrackDef.stage`: point-to-point
with ENDS like a strip (clamped waypoints, finish at a distance, standing
2x2 start behind a lead-in line), but a real winding road — so everything
that is really about DRAG RACING (staging abreast, trap-speed talk, the
top-down camera) stays on `drag`, and everything that means "the list has
ends" asks the new `TrackPath.HasEnds`. The catalog entry is APPENDED after
Charlotte so every existing save's track index still points where it did.

**The terrain inverts the circuits' rule and keeps their guarantee.** A
circuit grades the land TO the road; here the road came FROM the land, and
the ground truth is the DEM itself — but the corridor is pinned to the road
exactly the way the circuits pin theirs (16 m shelf, 48 m blend, 10 cm
sink), released back to the real slope through bridge spans so the
mountainside falls away under the deck. `GroundHeightAt` branches to the
stage field whenever a stage DEM is loaded, so the pier builder, the
footings and the audits read the mountain without knowing it is one. Ground
is CHUNKED (one 144-grid sized to 7 km would have 50 m cells): 140 near
chunks at 12 m with colliders on the drivable band, 69 far chunks at 60 m,
painted as autumn forest and sunk 0.4 m so the overlap ring cannot
z-fight. Chunk normals come from the height FIELD, not RecalculateNormals —
per-chunk recalculation disagrees along shared borders and draws every
border as a lighting seam across a hillside.

**The forest is the point.** 10,591 crossed-quad billboards from the CC0
"Ultimate Retro PSX Tree Pack" (the owner's new asset drop), sixteen
species composed into one 512 atlas so a whole chunk of forest is one draw
call. Species follow the mountain: spruce-fir probability climbs with
elevation, the fall colours cluster by low-frequency noise the way a
hillside does, true cliffs (50 degrees+) stay bare, and 521 trees stand
UNDER the viaduct decks — the Linn Cove look is a road riding over canopy.
Chunks live on a Foliage layer that `StageCulling` clips at 520 m; the fog
band runs 3.2x the hour presets (PSXGlobals.fogScale, one multiplier, so
the seven-hour table stays one table) under a 1.5 km far plane. What reads
as the Blue Ridge is the ridgelines dissolving into exactly that haze.

**Guard walls only where the mountain says so.** The parkway's low stone
walls appear where the land drops 5 m within 30 m of the shoulder, and on
both sides of every deck — 7.5 of the 14.5 km of roadside. The visible
stone is 0.85 m; the collider is 1.7 m and one 4 m chord per station,
because the first pass used 16 m chords and the obstacle audit correctly
found their sag reaching 1.2 m inside the wall line on tight corners — an
invisible face, at 151 spots, exactly the class of bug that audit was
written for. The uphill side is open: the slope is the barrier.

**Verification carried straight over.** The self-test gained a stage branch
(finish-line contract, corner floor, self-clearance against the stage's own
5.9 m barrier line, and grade checks that CLAMP at the ends — wrapping
measures the fake "grade" between finish and start, which on a stage that
drops 46 m end to end reads as a cliff). TerrainAudit rays the chunked
ground through a collider set and now skips the city's by-design empty
scene instead of reporting a permanent phantom problem. The whole battery
is green: road clear of ground at 0.106 m at its tightest, real gorges
11.2 m deep under 238 span waypoints, 8 decks for 8 spans, nothing solid
inside the barrier line.

**Not in v1** (the stage's own backlog): overlook pull-offs as parking pads
(the data knows where they are), rock-cut faces on the uphill side, a
second BRP section (the fetch script is parameterised by anchor + length),
Japanese cherry species from the same pack for a pink-season variant, the
tree_pack_1.1 bushes as rhododendron understory once its license is known,
and a proper point-to-point rival duel (the grid already stages 2x2).

## THE LAUNCH SCREEN, AND EIGHT HOURS AT A TIME (2026-08-30)

Asked for, from a screenshot of MAIN scrolled to its bottom: the main menu's
options should all be on screen at once rather than a vertical tower running off
it; remove PRACTICE LAP; move NEW GAME to OPTIONS; SLEEP should just be SLEEP
and move the game on eight hours to the next block of the day rather than to
tomorrow; inspecting a car -- your own or at a mechanic or dealer -- should cost
a block; and get the date off the bottom of the menu, where it was making the
menu hard to read.

**MAIN is two columns now.** It was one 460-wide stack about 900 units tall, on
a phone body column that is 437 -- so over half of it lived below a fold, which
is why the reporter's two screenshots were both of the same page. The fix is not
to shrink anything; it is that width is the resource this menu HAS and height is
the one it does not. The narrowest canvas the game runs on is a 4:3 desktop at
912 units across and 562 tall; a wide phone is 1192 across and 437 tall. So the
page spends width: WHERE YOU RACE on the left (today's booking, the map, the
name and numbers, the track and hour arrows, GET IN CAR, the purse, the fuel
warning, the known faults) and WHAT TODAY IS on the right (calendar, free roam,
the shift, SLEEP, the last five days). Both halves fit above the fold on all
three aspects with room left for the conditional rows.

Three things came off the page rather than being rearranged. PRACTICE LAP, which
was a second race button doing a quieter version of the same thing -- gone from
MAIN and from the calendar's booking form; the `practice` FLAG stays in the save
format because an old career can still be holding a booking made under it, and
because the delivery job rides the same no-purse path. NEW GAME, which erases
the career and had no business one press from the button you hit every morning.
And the DEBUG rung that sat above it. Both are on OPTIONS.

**SLEEP is eight hours.** `LifeRules.Sleep` used to roll the whole day from
wherever it was pressed, which made the three-slot clock a one-decision affair:
a morning you did not want to spend cost you the afternoon and the night with
it. It now walks one band -- MORNING, AFTERNOON, NIGHT -- and only the night
sleep turns the calendar over, gives back the +5, and passes `sleptTonight` to
the health ladder. A daytime nap costs the slot and nothing else: it is not an
ACTIVE slot, and it restores no health. That last part is not fussiness. A token
point per nap is two free points a day against a hunger ladder that takes twelve,
and the self-test caught it immediately -- "twenty days without food is
fatal-grade" came back with a driver sitting at 7 health and stable.

Everything that genuinely meant "a day passes" -- the absence ladder, parts
arriving on a promised day, every test that ages a career -- moved to a new
`SleepUntilMorning`. Left on `Sleep` they would each have silently had their day
count divided by three.

**Inspection already cost a block** -- `Inspection.Enter` and
`Inspection.BookPro` have both spent an activity slot since they were written,
and the self-test has asserted it for both -- but only the player's own
inspection ever SAID so. The mechanic and dealer buttons quoted the money and
said nothing about the day, so it quietly got shorter. They say it now.

**The date at the bottom was a toast that never left.** `DoSleep` toasted
`DateLabel(day)` and `Toast` had no expiry at all: a message was destroyed only
by the NEXT message, so after a sleep the date sat in a black bar across the
foot of the page, over the log and the buttons under it, for as long as the
player stayed there -- and the header was already carrying the same date an
arm's length above. The sleep toast now names what changed ("EIGHT HOURS ON --
AFTERNOON") and every toast expires after 4.5 unscaled seconds.

**The preview harness enforces the requirement.** "All of it on one screen" is a
property of the launch screen, not a nicety, and a PNG cannot show what is below
the fold -- so `LifeHomePreview.Shoot` gained a `mustFit` flag that logs an
error when a page's content is taller than its viewport, and MAIN is shot with
it set, once per band of the day, at all three aspects. `tools\menu-check.ps1`
runs the scene build, the self-test and the capture in the one order that makes
the self-test's answer mean anything: the sandbox mirror is a `/MIR`, so it
deletes the built scenes and baked meshes every run, and skipping the build
fails nine checks that read exactly like a regression.

## CHARLOTTE, 1:1 (2026-08-25)

Asked for: base the game on a real city the way Midnight Club used Atlanta and
LA — a 3D Charlotte from the road network already built in the HTML game, "as
close to scale as possible while maintaining LoD, draw distance, and 60-100+
FPS", with two standing rules: every road over water gets a bridge, and every
highway over a road goes over on one.

Full design and the decisions behind it: `Docs/CHARLOTTE.md`. The short form:

**The data was better than the maps suggested.** RG2 carries TWO Charlottes —
the hand-traced PNG layers the reference images render, and a full OSM import
(`fixtures/osm/charlotte_rows.json`) nobody had ported: 3,076 named rows with
lanes, one-way flags, real bridge decks, 840+ real interchange ramps, and the
whole thing invertible to lat/lon. Roads come from the OSM bake; the WATER
only exists in the traced set, so the creeks are co-registered onto the OSM
frame by fitting the one shape both datasets share — the I-485 loop (26 m mean
residual). `tools/city/export_charlotte.mjs` welds, T-snaps 2,629 ramp tips,
classifies every geometric crossing (same-z = junction, else = grade
separation — the bake's own H1327 convention), detects water spans, and bakes
`Resources/charlotte_city.json`: 7,287 edges / 5,383 nodes / 2,574 km, 526
separations, 179 water spans. Attribution: OpenStreetMap, ODbL — on the HUD's
opening seconds and the menu blurb, as required.

**The scale trick is RG2's own, dialled to 1:1.** Layout metres and
cross-section metres are different currencies (the car is real-size at any
layout scale); RG2 ran layout at 1:6 and this port runs it at 1.0 —
`CityMap.LayoutScale`, one knob. The I-485 belt is ~31 km across and the far
edge sits ~16 km out, where float32 quantises at ~2 mm — inside the wobble a
PSX renderer adds on purpose.

**The project's first runtime world.** Every circuit is baked whole into its
scene; a 31 km city cannot be, so `Charlotte.unity` ships nearly empty and
`CityWorld` streams 256 m tiles around the car — a 5×5 ring, one tile build
per frame, the tile under the car force-built so the ground can never lose
the race. The 360 m far plane and the fog that closes before it are what make
this cheap. Meshes are tile-local so precision never depends on distance from
origin. `GroundHeightAt`'s O(track) Gaussian could not come along: the city
ground query is tile-local through a spatial hash, same shelf/sink/blend
shape as the circuits.

**Elevation is solved from FACTS, not painted z.** A freeway is not 14 m in
the air its whole length — z is a stacking order. Every edge follows terrain;
each grade separation lifts its OVER edge clear by 5 m + deck at 4.5%
approaches; humps that overlap merge into viaducts (I-277 emerges by itself);
water spans hold their line while the ground carves a bed. Getting the solver
CONVERGENT was the day's real fight, and each failure mode earned its rule:

- Node reconciliation (a junction takes its highest incident end, so ramps
  meet the mainline they climb to) can lift a road AFTER its overpass solved
  — so raise/reconcile iterate to quiescence and END on a raise.
- A ramp that both MEETS a street at a node and passes under it further along
  is a feedback loop — one South Tryon cluster ratcheted 24 m into the sky.
  Crossing targets LATCH on first computation; three final fresh passes make
  the guarantee exact without re-opening the loop.
- A capped hump reach left a fifteen-metre CLIFF under the four-level stacks,
  which the grade-relax pass "fixed" by hauling the deck down through the
  road it cleared. The cap is gone (a cone fades below terrain on its own)
  and nothing that can LOWER a road runs after the final raise.
- Short viaduct fragments whose nodes disagree by more than they can climb
  raise their low node to what the climb can reach; sub-30 m slivers stay
  honestly steep and the audit reports rather than fails them.

`CityAudit` (menu: PSX Racing/Audit City) holds the invariants: 98.7% of road
length reachable from spawn, every enforced separation clears (worst 4.78 m),
every water crossing decked, no drivable grade past 16%, tile builds
deterministic. `CityPreview` photographs uptown, an overpass, a water bridge
and I-485 without play mode — which is how "every street in the city rendered
only from underneath" was caught: the ribbon quad's corners ran
counter-clockwise, and the junction patches (their own code path) were the
only thing visible from above.

**What a city street is here:** no walls, no kerb ribbon — road ribbon on the
Road layer (grip by layer), painted per-class surfaces DRAWN by the builder
from the same lane ladder the exporter bakes widths from (minor 2-lane,
4-lane arterial, divided grass/asphalt, 8-lane motorway, ramp), junction
patches that also kill z-fighting by construction, buildings seated off road
frontage with one BoxCollider each. Facades are the owner's building pack
(tower glass, midrise, brick) plus a composed 4-shopfront atlas; heights fall
from an uptown tower cluster through midrise to thin suburbia. Driving off
the road is legal Midnight Club behaviour; grass grip is the penalty.

**Mode wiring:** Charlotte is a `TrackCatalog` entry with `city = true`,
LAST, so every scene index holds; `PSXRacingBuilder.Build` branches to a city
baker that reuses the car assembler (extracted to `BuildOneCar`), lighting
and camera/HUD. There is no RaceManager in the scene — `CityMode` is the
session (rolling start, respawn onto the graph, street name for the HUD, the
session ledger), and everything that used to reach for RaceManager.Instance
goes through `DriveSession`, which knows which world it is in. FREE ROAM
launches from the MAIN tab (practice-pattern: costs a slot, burns real fuel,
pays nothing), the pause menu's EXIT stamps the session into RaceHandoff, and
the apply-back banks metres/fuel/wear with a "free roam — X km in Charlotte"
line. The race picker skips the city; the fuel gate refuses under 10% because
there are no pumps out there yet — the fuel truck covers stranding.

**Not in v1** (the city's own backlog): traffic (the graph it needs now
exists), city races (point-to-point through the graph), minimap, in-city gas
stations, street lamps and signal heads, a skyline backdrop past the fog,
residential streets (one Overpass fetch away — needs the owner's nod), real
building footprints, and the PSX Shader Kit pass if its shaders beat PSX/Lit.

## THE PUMPS, AND A GARAGE YOU CAN WALK INTO (2026-08-24)

Asked for: "I would like to actually stop at gas pumps and fill the gas tank
full, instead of pressing a button in the garage" and "a simple 1st person
garage mode where cars and car parts are stored."

### Fuel became a thing in the car

It used to be a number the menu subtracted after a race: distance in, percentage
out, one REFUEL button in the garage. Two circuits had a gas station beside the
road and neither could be reached — the barrier ran past it unbroken and the
model carried a single box collider over the whole of it, pumps included. It was
a photograph of a gas station.

`FuelTank` now burns down in real time on the player car, at exactly
`LifeRules.FuelPctPerMeter` — the same constant the apply-back used, so a race
driven start to finish consumes precisely what it always did. Nothing in the
economy moved; what moved is where the tank is filled. The burn is per METRE and
not per second on purpose: a time-based idle burn drains the tank on the grid
during the countdown and while the player is parked at the pump deciding, and
both are moments the game has taken the controls away.

Below 2% the pickup sucks air and the engine stumbles before it stops, because
the stumble is the only warning a player who ignored the gauge will read.

`RaceHandoff` carries the tank both ways. The apply-back writes the MEASURED end
level rather than re-deriving a burn from the distance — a car that stopped at
the forecourt on lap two covered the same metres as one that did not and is
carrying a completely different amount of fuel, so the odometer can no longer
answer that question.

### Four things the forecourt needed, in order

Each depends on the last, which is why `PlanFuelStop` runs before the terrain
mesh rather than with the rest of the scenery:

1. **Plan** it first, so the ground can be flattened under it. The pad is flat
   ACROSS the road and follows the road's own gradient ALONG it. One constant
   height would be the obvious choice and it is wrong: the apron reaches to the
   kerb, and inside the corridor the ground is pinned level with a road that
   climbs — a single height would step against the racing line by a metre on
   Ridge Pass. The corridor's 10 cm sink survives the pad, or a coarse 8 m
   ground triangle would sit exactly level with the tarmac.
2. **Cut** the barrier. The vertex run stays (so the UV distance either side of
   the opening still lines up) and only the faces and colliders are dropped, on
   the forecourt's side only. `SaveMesh`'s zero-normal guard now counts over the
   vertices triangles actually *use*, because the orphans this leaves are not
   the double-winding bug that guard exists for.
3. **Lay** an apron on the ROAD layer, three centimetres above the ground *at
   each point* rather than above the pad's plane — the difference is a ramp at
   the entrance instead of a lip the car has to bump up. Wheels tell tarmac from
   grass by layer, and a forecourt at field grip is one nobody can stop on.
4. **Place** the station facing the road, turned by where its own `Fuel_pump`
   objects are rather than by an assumption about the model's forward axis, and
   collide only what should stop a car: the shop behind the pumps, and the pump
   islands. Everything between is where the player drives.

### The model is not a building

The pad has to be sized off the station, and sizing it took four wrong answers
before a right one. `Gas_station.fbx` is not a filling station. It is a
DIORAMA — the pack's display scene, complete with a painted skyline 39 m tall,
a checkerboard photography floor, roads, hillsides and a treeline, 300 x 143 m
of it. The builder had always scaled it "so it is 7 m tall", which is to say it
scaled the backdrop to 7 m and took the station down with it to a fifth of its
size. That is why the forecourt looked like a hedge with a shed in it.

Three passes fix it, and the order is the point:

- **Strip** the backdrop and the checkerboard by name.
- **Scale** off the PUMPS. A fuel pump is 1.85 m tall, everywhere, always —
  the only dimension in the file whose real size is knowable. Every other
  candidate measures something that is not the building.
- **Trim** to 30 m around the pumps, judged by how far each mesh REACHES and
  not where its middle is: an 80 m strip of the diorama's road runs right past
  the pumps, so by centre it is local and by reach it is scenery. That one
  distinction is the difference between a 129 m "lot" and the 42 x 41 m one the
  station actually is.

Then the numbers come out like a filling station: 42 m wide, 41 m deep, 14 m to
the top of the price sign, pumps spread over 19 x 11 m, 22 m of open apron
between the kerb and the front of the lot.

### Two things this turned up that were never about fuel

**Imported foliage has always rendered as opaque black quads.**
`PSXMaterialFor` never set `_Cutoff`, so every alpha-masked billboard in every
imported model came through solid — invisible until now only because the one
model full of them was built at a fifth scale behind a barrier nobody could
cross. It now asks the importer whether the source carries alpha, rather than
guessing from the file extension.

**Every reference screenshot this project has produced since the cluster moved
to an overlay is two thirds white rectangle.** The capture pass pulls overlay
canvases in front of the shot camera so the HUD is in frame, and that includes
the canvas whose entire job is to display the framebuffer — which outside play
mode has no texture and draws flat white. It is excluded now, which is the only
reason any of the above could be seen at all.

**Two texture traps, same shape.** `Concrete.jpg` is a wall photograph WITH its
painted skirting board; tiled across the garage floor that skirting is a dark
stripe every three metres. `Asphalt.jpg` is tarmac WITH its painted kerb line;
tiled across the forecourt it is a yellow stripe every eight. These are
photographs of surfaces complete with their trim, and a texture named for the
material it is made of is not a texture named for the surface it belongs on.

### An audit for the thing that fails silently

`TrackObstacleAudit` gained a forecourt check. Everything else in that file asks
whether something solid stands where the player drives; this asks the opposite,
because the way IN is generated rather than authored — a driveway that did not
get cut leaves a pump nobody can reach and nothing else would say a word. It
scans the barrier for an opening, then walks from it to each pump in turn.

Both of its first two answers were wrong in instructive ways. It reported the
pump's own island as the obstacle, because a car parks BESIDE a nozzle and a
line drawn to one ends inside it — it stops five metres short now. Then it
reported Harbor Point walled in, because it measured against collider BOUNDING
BOXES and the shop's 42 m box is yawed to face the road, so on a diagonal
forecourt its world-axis box is half as big again and swallows the pumps
standing in front of it. It uses `ClosestPoint` now. An audit that cries wolf is
worse than no audit.

Every circuit has one now. Harbor Point and the airfield had `gasStation =
false` back when it was set dressing; a circuit with no pumps is one a long race
cannot be finished on. Drag strips still have none and cannot: the run ends at
the traps 400 m in.

### The gate got looser, not tighter

The pre-race gate demanded fuel for the WHOLE race, because the whole race was
the only unit fuel came in. `LifeRules.RequiredFuelPct` now asks only whether
the car can REACH a forecourt. Starting on a half tank and planning a stop is a
strategy, and the gate had no business calling it a mistake — but the screen
says so out loud before you go.

### The truck, and why it exists

A resource you can only buy in one PLACE can strand a player who runs dry
somewhere else, and being stuck with no legal move is not a difficulty setting.
So the garage's REFUEL button became a CALL FUEL TRUCK button at a $40 call-out
fee on top of the fuel, and the same row is in the pause menu for a car that
died mid-lap. The pumps are a quarter of the price; that is the whole point.

`StuckRecovery` stands down at a pump and on an empty tank. A car deliberately
stationary on a forecourt is exactly what "beached" looks like from outside, and
respawning a dry car would teleport it, find it still motionless and fire again.

### The garage is a room

`Scenes/Garage.unity`, built by `GarageSceneBuilder`, LAST in build settings —
`TrackCatalog.SceneIndex` addresses circuits by position, so a scene inserted
before them would silently send every race to the wrong place.

The ROOM is baked and the CONTENTS are not. A scene with four cars in it would
show four cars to a player who owns one, so `GarageWorld` spawns the bays, the
crates and the tool board out of the save at Start — using the same measured
geometry `CarBody` and the menu turntable use, because a third opinion about
where a Charger's wheels go is a third chance to be wrong.

Anything needing a MENU walks the player back to LifeHome on the right tab
(`LifeHomeScreen.PendingTab`) rather than rebuilding it in 3D. The tuning
ladder, the fault quotes and the toolbox are hundreds of lines of rules each,
and a second implementation standing at a workbench would be a second set of
prices to keep in agreement with the first. What happens in the room is what
only makes sense in the room: walking up to a particular car and taking its keys
because you are looking at it.

Targets are found by scoring a registered LIST by angle then distance, not by
raycasting. Half of what is worth looking at is spawned at runtime and has no
authored collider, and a car is a four-metre object whose transform sits at its
axle midpoint — a ray that misses the bodywork by ten centimetres would find
nothing while the player is plainly standing in front of it.

Three control schemes, because the game ships to phones: mouse-look behind a
pointer lock the browser will only grant on a click, a pad, and two thumbs.
Touches are claimed once by where they STARTED and keep the job until they lift,
which is what makes walking and looking at the same time work.

`GarageWorld.PreviewBuild` is public for the screenshot pass — `AddComponent`
does not call Start outside play mode, so every reference shot would otherwise
be a photograph of an empty concrete box.

### What an adversarial review pass found afterwards

Four independent reviewers over the new code, each finding verified by a
separate agent told to refute it: 16 findings, 9 survived. Every one was real,
and none of them were visible in a screenshot or catchable by the audits —
which is the argument for the pass.

**The garage was unusable on a phone.** `GarageTouchPanel` claimed a touch slot
on the frame a finger landed and then `continue`d past the bookkeeping that
records "I still hold this slot" — so the slot was released the same frame it
was taken, and the next frame the touch is `Moved` rather than `Began`, so it
never claimed again. Arriving in the garage on a phone meant no walking, no
looking, and no way to reach the door: a soft-lock whose only exit is reloading
the page. Compounding it, the panel decided whether to exist once at Awake off
`isMobilePlatform || deviceType == Handheld`, which reports DESKTOP on a tablet
browser set to request the desktop site. Out on the circuit that costs a
steering wheel and a keyboard is one tap away; in a room whose door you have to
WALK to, it is the whole session. It reveals on the first touch now.

**Three ways the pumps mishandled money.**

- RESTART RACE reloaded the scene, and the reload re-seeds the tank from
  `StartFuelPct` — written once, before the lights. Fuel bought mid-race has
  already left the wallet and been saved, so restarting after a fill handed
  back the empty tank and charged for the same fill twice.
- The sub-dollar remainder was rounded UP on every release of the trigger
  rather than once per visit. One unbroken hold cost the $12 the prompt quotes;
  the same fill in 22 taps cost $22.
- `SpentThisRace` was reset only in `GasPump.Awake`, and a drag strip has no
  pumps — so no Awake ran, the static survived the scene load, and the strip
  reported the previous circuit's fuel bill as its own, writing a receipt for
  fuel nobody bought into the permanent calendar log.

The fix for all three is the same shape: the unit is a VISIT, not a volume and
not a squeeze of the trigger, and the race's fuel bill accumulates into
`RaceHandoff.FuelSpent` — which is already cleared before every race — instead
of into a counter that resets on a scene load that may not happen.

**A nudge between two overlapping pumps split one fill in half.** The station
carries several named pump objects whose volumes overlap, so a parked car is
routinely inside two; edging forward exits one while still inside the other.
That settled the bill, wrote a log line and zeroed the on-screen receipt
mid-fill. A visit now survives the hand-off and closes only when nothing has
claimed the car for a few frames — which is what driving away looks like.

**And the excuse that went too far.** `StuckRecovery` was told to stand down at
a pump, because a stationary car on a forecourt is one the driver parked. True
of a parked car; not true of one on its roof. A car rolled onto the forecourt
was left there permanently, with the fuel prompt drawn over the banner that
would have told it how to get out. The stand-down is now conditional on the car
being upright and clear of a wall, and a genuinely stuck car's prompt outranks
the nozzle.

## THE CABIN, AT DEVICE RESOLUTION (2026-08-24)

Reported: "gauges are too small and too blurry. steering wheel has wrong
geometry compared to HTML. pedals, shifter and e-brake are missing from HTML."

**The cluster moved off the framebuffer.** It was rasterised into the 240-line
buffer with everything else, on the reasoning that instruments should dither and
crawl along with the picture rather than sit on top of it as crisp modern vector
art. That is right in a still and wrong on a phone: `radiusFrac` of 0.105 is
25 pixels of radius, carrying numerals clamped to an 8-pixel floor, and an
8-pixel glyph out of a dynamic font atlas is a grey smudge whatever you upscale
it with. Both halves of "too small and too blurry" were the same number.

It now has its own `ScreenSpaceOverlay` canvas at device resolution, which also
makes the split deliberate rather than accidental — the touch wheel and pedals
were ALREADY on a screen-resolution overlay, so the frame was never uniformly
240 lines. The CABIN (what you read and what you hold) is at device resolution;
the WORLD, and the race data printed over it, stay at 240.

Three consequences worth naming:

- The dial rasterisers were baked at exactly the layout size and point filtered,
  "so nothing resamples". True while the layout size WAS a framebuffer pixel
  count; false on a scaling canvas, where the same 166 units is 249 device
  pixels on one phone and 332 on another. They now draw at 2x, mipmapped and
  trilinear, so the resampling that is going to happen anyway happens cleanly.
- `LabelStep` sized the numeral count off `radius / 7`, which read as "room" and
  handed a 108-unit speedometer FOURTEEN three-digit labels — a smear across the
  top of the sweep at exactly the size meant to make it readable. Label count is
  bounded by ANGLE and by character count, not by radius: the type scales with
  the dial, so a bigger dial fits the same numbers larger, not more of them.
- The dials used to be placed at a fraction of the frame width chosen to clear
  the wheel on one side and the pedals on the other. A fraction that clears both
  is a different fraction every time the panel is retuned, and it was wrong
  within one build of each of the last two changes. `TouchControls` now PUBLISHES
  `WheelInset` and `PedalsInset` and the cluster centres two dials in the band
  between them, shrinking them if the band is too narrow. Both canvases were
  given identical scaler settings, because a band measured in one currency and
  spent in another is not a measurement.

**The steering wheel had three geometry errors against `index.html`.** Its SVG
draws the rim as a 22-wide stroke on r=89 in a 220-unit viewBox, so every radius
is that number over 100:

- SPOKES were angular wedges 4.5 to 11 degrees wide that got WIDER toward the
  rim. The source spoke is a LINEAR flare — a slab 9 units half-height at r=22
  opening to 25 at r=87 — which is 22 degrees at the hub NARROWING to 16 at the
  rim. Wedges are thin sticks where the original has cast-metal arms, and they
  taper the wrong way. They were also shaded bright down the centre, which turns
  three arms into three beams of light; the source fills them flat and strokes
  the edge.
- STITCHING was 2.4 units wide at 40% duty and fully opaque, against the
  source's 1.2 wide, 29% duty (`dasharray 2.5 6`), at 55% alpha over the
  leather. It read as a bright dotted ring rather than as thread.
- OUTLINES were missing: the source strokes black at r=100 and r=78, which is
  what stops the rim bleeding into the scene behind it.

And one that only a picture catches: the wheel painter does polar maths and
wants y UP, but the shared painter hands every sprite DESIGN coordinates with y
DOWN (the CSS convention the other eight want). The whole wheel was mirrored top
to bottom — the twelve-o'clock marker painted at SIX o'clock, and the three
spokes sat at the reflection of their intended MOMO stance. It survived a review
of the maths and died the moment it was rendered.

**The pedals, handbrake and shifter had no hardware at all.** Each was a tinted
translucent slab with a plain rounded rectangle sliding on it — the same shape
four times in four colours, which is why the panel read as programmer art next
to the browser it was copied from. New `Scripts/TouchArt.cs` draws all nine
sprites from the source's own numbers:

- Pedal linkage: `.ped-base` (36x14 steel mount), `.ped-arm` (5x60 rod, lit down
  its centre) and `.ped-face` — the gas pedal a 26x62 perforated alloy plate with
  the source's seven-hole zigzag, the brake a 30x38 foam pad. The pad rises
  TOWARD its mount as it is pressed and the arm shortens to match, off RG2's
  `ARM_TRAVEL_PX = 28` / `ARM_MIN_SCALE = 32/60`, both driven from one amount so
  they cannot come apart. The old code moved the pad the other way, which read as
  a pedal being pushed off the end of its own linkage.
- Handbrake: the `.ebh-rotor` lever — ribbed grip, chrome release button. On a
  phone the source hides the e-brake's whole pedal stack and shows only this, so
  the lever IS the control. The CSS rotates it `62deg - amt * 75deg` about its
  base; this canvas is orthographic, so that shows up as foreshortening and the
  lever reaches full length as it comes toward you.
- Shifter: a 44px puck lit from the upper left with a 30px bevelled recess
  carrying the gear, on the cyan centre line that gives the throw a datum.
- Bars and fills are TRANSPARENT, as they are in the CSS. The rails of ticks
  down both edges mark the control out and the hardware shows the level; a
  tinted slab behind a drawn pedal is a slab with a pedal on it.

`BarScale` (1.8) is the one number that scales the whole design at once. Parts
drawn to their own dimensions and dropped into a bar of a different shape give
you a different pedal that merely has the same parts, which is what the first
version was.

**`PSX Racing/Preview Touch Control Panel` now renders the cluster too**, and at
1280x720 — the reference resolution both canvases scale from, so every control
appears at its design size with no scale factor to divide out by eye. The
cluster shares the bottom edge with the wheel and the pedals and is the whole
reason those two are pushed into the corners, so a panel shot without it could
not answer the question it exists to answer. Runner: `tools\controls-preview.ps1`.

## THE PUNCH-CLOCK KERB, AND A WALL IN THE GRAVEL (2026-08-23)

Reported from a HarborPoint race: "I got stuck on an invisible barrier on a
track here. Also, where are the curbs, just some analog meter?"

**The kerb was a photograph of a punch clock.** The kerb strip down both sides
of all six circuits, and the start/finish line, were textured with
`Art/GasStation/Textures/Checker.png` — a filename that promises a chequerboard
and a file that contains the Acroprint time recorder off the back-office wall of
the gas station asset pack, teal case, white dial and all. Laid at one repeat
per 2 m of kerb, that is a mile of wall clocks end to end, which is exactly what
the report describes.

It survived every pass because nothing in the build ever LOOKS at a texture,
only at its path, and `Checker.png` reads correctly in a diff. Both markings are
now drawn by `GenerateTrackTextures()` into `Art/Track/`: 1 m red/white bands
with a dark line down each long edge (0.9 m of high chroma between grey tarmac
and grey gravel reads as a light source without one, and night is when the kerb
matters most), and an actual chequer for the line. Drawn rather than sourced, so
no pack update can put the clock back.

**The invisible barrier was one warehouse corner, 0.57 m inside the barrier
line.** `PlaceBuildings` seats each block at `WallOffset + BuildingClearance`
past its own waypoint, along that one waypoint's normal — one distance, from one
point, on a curve. A 20 m warehouse laid tangentially beside HarborPoint's
20.2 m minimum-radius corner has its far corners raked round toward the inside
of the bend, and one of them came to rest 9.43 m off the centreline: inside the
10 m barrier, out in the gravel, on the Solid layer with no renderer of its own.
Buildings are now pushed outward until EVERY corner of the footprint clears the
barrier line measured against the WHOLE path — iteratively, because moving a
building changes which stretch of road is nearest to it — and dropped outright
when no offset on that normal works, rather than seated where a car can reach
them. The same pass fixes the footing sample, which measured the ground about
the mesh ORIGIN rather than about the middle of the footprint.

This is the second building collider to end up where a car can reach it. The
first (2026-08-21) was a SIZING error and was audited against the tarmac; this
one is a PLACEMENT error out in the run-off, which that audit could not see.

**`TrackObstacleAudit` could not have found it, by construction.** It measured
±(roadWidth/2 + 0.6 m) — the tarmac — while HarborPoint's barriers stand at
10 m, leaving 4 m of legal gravel either side that it never looked at and
reported as CLEAR. Running wide onto that gravel is a normal part of a lap. It
now measures TWO bands, ON TRACK and RUN-OFF, out to the barrier line, off the
same `WallOffset` and `KerbWidth` constants the builder places from.

Three classes of false positive went with it, all of which had been masking the
report. Concave `MeshCollider`s have no `ClosestPoint` — Unity hands the query
point straight back — so the road, the ground and every bridge deck reported as
touching the centreline; they are surfaces you drive on, and are now named,
counted and skipped rather than measured wrongly. Bridge piers reported as
intruding because the height gate allowed anything down to 1.5 m below the road,
and a pier's top is 1.22 m under its own deck; the gate is now the road surface
itself, since a car sitting on the road cannot be stopped by something entirely
beneath it. And six "invisible walls" lying across the end of both drag strips
were `TrackPath.GetTangent` handing back `Vector3.zero`: on a strip `Wrap`
CLAMPS, so at the last waypoint both samples are the same point, and a zero
right-vector measures every barrier as touching the centreline. That zero also
reached `GetRotation` (`LookRotation` of nothing), the AI's lateral basis and
respawn; it now steps BACKWARD off the end of the strip instead.

**And a second instrument, asking the question the other way round.** The audit
above measures collider geometry against the barrier line, which it does well
for boxes and cannot do at all for concave meshes — so the road, the ground and
every bridge deck are skipped there. Those three are surfaces you drive on, so
skipping them is right, but it leaves a hole: an abutment cap, a fold in the
ground, a ribbon crossing itself are all concave mesh. New
`Editor/TrackSweepAudit.cs` (menu: PSX Racing/Sweep Track For Blockages) instead
PUTS THE CAR THERE — an OverlapBox the size of the player's collider, at ride
height, every 0.5 m across the full width of every waypoint, 58,000 probes over
the six circuits. Whatever kind of collider is in the way, a box that does not
fit is a car that does not fit.

It is allowed to use physics queries, which the bounds audit deliberately is
not, only because it starts with a CONTROL PROBE inside the barrier where there
is definitely a wall. An edit-mode physics scene that never got populated
answers every query with "nothing", which is indistinguishable from a clean
circuit; if the control probe comes back empty the tool reports that it cannot
sweep rather than a false all-clear. Post-fix it finds nothing on any circuit
except the barrier itself, which is the intended limit and is filtered.
New `tools\obstacle-audit.ps1` and `tools\sweep-audit.ps1`, each of which mirrors, rebuilds and then audits in one go —
the audit reads scenes, and the source scenes are always stale.
## DRAG STRIPS, AND FOUR CAMERA/LIGHT CORRECTIONS (2026-08-23)

**Two strips: 1/4 mile (402.336 m) and 1/8 (201.168 m).** A strip is not a
loop, and that distinction runs deeper than it looks: `TrackCatalog.Sample`
emits a straight instead of a cyclic spline, `TrackPath` clamps every index
walk instead of wrapping (an AI that reaches the shutdown area and WRAPS turns
round and drives back up the strip at the field), the road, kerb and wall
ribbons stop at the last waypoint instead of closing one segment back onto
waypoint 0 — which on a straight is 700 m of road laid back down on top of
itself — and the grid stages ABREAST on the line rather than in a rolling 2×2,
because a staggered grid decides a drag race before the tree does. `RaceManager`
finishes on a distance rather than a lap count and records a trap speed; the HUD
prints the ET and the trap instead of a best lap, because "best lap" is the
circuit's answer to a question the strip did not ask.

**Top-down is now a drag-strip view only.** On a circuit it is a novelty that
makes the car impossible to place against a corner whose entry is off-screen;
on a strip, where the only question is which car is ahead, it is the clearest
view in the game. The cycle simply does not reach it elsewhere, and a top-down
saved from a strip does not follow the player onto a circuit.

**The bonnet camera now sits at the MEASURED base of the windscreen.**
`CarModelBaker` bins the body mesh's vertices by Z, takes the highest point in
each bin, and walks back from the nose until the profile climbs away from the
bonnet line — that bin is the cowl, and it is stored on the shell as `cowlZ` /
`cowlY`. A height profile is the right instrument because it needs no material
names, no sub-object names and no UV convention, none of which the vehicle pack
has consistently. The old fixed fraction of car length gave a Superbird and a
1970s Civic the same bonnet, which is not close to true. A cab-over van
correctly measures a cowl at the nose — that is not a failure, that is a van.

**The close chase went UP, not down.** At 1.48 m the lens was level with the
roofline of the car it followed, so the view could not see past its own car —
reported as "too low to the ground making it hard to see in front". 2.07 m
keeps the car filling the frame and gives the road back.

**Every car runs selective-yellow headlights.** The catalog is 1960s-90s
machinery; a white LED is decades out of period, tungsten and halogen both burn
yellow, and French cars were legally required to. The pool on the tarmac
matches — a warm lamp throwing a white pool reads as two different lights.

## THE GRID OFF THE ISLAND (2026-08-23)

Reported: "I was spawned off the track and outside the walls and couldn't get
back on the track."

`BuildCars` placed each grid row by extrapolating the START LINE'S TANGENT
backwards — `pts[0] - tangent * back`. That is only correct when the start line
sits on a straight. The three polar-generated circuits all begin at their
easternmost control point, which on an elongated oval is the APEX OF A HAIRPIN,
so projecting 28.5 m back along a straight line put the player's row on the far
side of the barrier with no way back onto the road. The city circuit was fine
purely because its waypoint 0 happens to sit on a straight.

The grid now walks BACK ALONG THE PATH — index arithmetic on the waypoint list,
with `RightAt` for the lateral offset — so every row lands on the racing line
whatever the circuit is doing there.

## THE BLANK TOUCH PANEL (2026-08-23)

`TouchControls.MakeLabel` took a caption as a parameter and never assigned
`Text.text`. Every label on the touch panel — GAS, BRAKE, E-BRK, CAM, RESET —
has therefore been an unlabelled slab since the panel was written; only the
shifter's gear number appeared, because TouchShifter rewrites it every frame.

It survived a whole pass ABOUT the legibility of those buttons (which darkened
their backgrounds and made the type solid to fix "two blank grey slabs") because
a missing caption and a low-contrast one are the same picture. And it is the
real answer to "I don't see an option to change camera angle": the button that
cycles the six views was a blank rectangle in the corner.

`Preview Touch Control Panel` now logs an **error** when any Text has no font,
no string, or no alpha — a rendered PNG cannot tell those three apart, so the
instrument has to say so itself. The CAM button also prints the view it is
currently in underneath the word CAM.

## INSPECTION (2026-08-23) — L5's hidden faults, and the garage that shows them

Ported from RG2's `docs/INSPECT_SPEC.md` + `sim/inspectComponents.ts` +
`sim/inspectOwnCar.ts`. This is the "hidden faults + inspection" half of L5,
the phase that had not been started.

**The fault model needed nothing new.** `CarFault` has carried a `hidden` flag
since the port landed and literally nothing ever set it — `RollWearFault` wrote
`hidden = false` with a comment saying the hidden layer was a later pass. This
is that pass. A car bought off the newspaper now gets 0-3 undisclosed faults
(`Inspection.SeedHidden`, weighted by mileage and condition) on top of whatever
the seller admitted to.

**A hidden fault afflicts the car.** `FaultCatalog.Aggregate_` used to skip
hidden faults, which would have made inspecting a pure cost — you pay a time
slot to be told about a bill. Counting them means a rough used car really is
down on power from the first race and INSPECT is how you find out why. What
detection gates is the LISTING, not the affliction; every fault list in the
menus now counts through `KnownFaults`.

**The X-ray is the HTML game's own geometry** (`LifeSim/CarXray.cs`, ported from
`render/carBody/xrayDrivetrain.ts`). FF puts the block transverse across the
front axle; FR and 4WD sit its front face just over that axle with the box
behind it; MR hangs it between cabin and rear axle; RR puts it behind the axle
with the gearbox reaching forward. Cylinder count and arrangement come from the
catalog's own engine string, and the wheelbase, track and tyre size are the
measurements off the shell the car is actually wearing. The first version here
was a generic front-engined box diagram, which is a lie about every mid-engined
car in a 317-car catalog — and the source's layout maths had already absorbed
several rounds of "the driveshaft is off-centre" and "the engine is too far back
on trucks" that re-deriving would have re-earned. Two deliberate departures:
RG2's `max(1.6, L*0.055)`-style floors are dropped (they exist for sprites of
40-60 units; at 4.3 metres they would BE the car), and a UGUI Image cannot be a
tapered polygon, so the gearbox is a rect and every shaft is a rotated one.

**Eight components, thirty-six sub-checks, one roll each.**
`p = 0.5 + skill×0.003 + tools + access`, clamped to 0.05-0.95, straight from
the spec. Underside checks are −0.10 on a jack and +0.15 on a lift; the
borescope is +0.15 inside the engine; brakes without an impact wrench cap at
0.15; frame rails refuse outright without a lift. Entering costs an activity
slot (the gym pattern) and each sub latches per car per day, so a failed check
reads "looks fine" until tomorrow rather than letting the player tap until the
dice agree. A free floor-check on opening any component rolls the leak faults
at a flat 25% — the user's own "no leaks are seen on the garage floor" line.

**Most checks find nothing, and that is the feature.** Eleven of the
thirty-six have no fault ids at all; checking the clutch linkage and being
told it moves cleanly through the gates IS the fiction. The self-test asserts
the thing that would actually be broken: that **every id the fault pools can
roll has a home somewhere on the map**, because a fault with nowhere to be
found is one the player can never diagnose and nothing would ever say so.

**Toolbox** (`LifeSim/Toolbox.cs`): floor jack (free with the first car), LED
lamp $35, impact wrench $180, borescope $260, two-post lift $2,200 —
deliberately the most expensive thing in the game outside the cars.

**The garage now shows the cars.** The switcher returned early when there was
only one car to choose between, so a one-car garage listed nothing at all;
it is now always a list, with a paint swatch, drivetrain, mileage, worst
condition stat and known-fault count per row. Reported as "I don't see cars in
the garage."

## PRESENTATION PASS (2026-08-23) — cameras, circuits, hours, and a garage you can look at

Four features, one theme: the game had one view, one track and one time of day,
and nothing to look at between races.

**Six camera views** (`ChaseCamera`). Chase, close chase, roof, bonnet, front
bumper, top-down — cycled with C, gamepad north, the pause menu, or the CAM
button on the touch pad. That button had existed since the touch controls
landed and was wired to NOTHING, so on a phone there was no way to change view
at all; it is the same one line of plumbing as the keyboard path. Every mounted
view is positioned off the car's own BoxCollider rather than from constants,
because CarBody resizes that box to whichever of the sixteen shells the player
is driving — a bumper cam pinned at a fixed 1.9 m sits inside the nose of a Land
Rover and a metre in front of a supermini. The choice persists in PlayerPrefs,
and the HUD flashes the view's name for 2.4 s after a switch — and once on
the grid, with the control that changes it, because six views are worth nothing
to a player who never learns there is more than one. The touch button names the
view it is in.

**Four circuits** (`TrackCatalog`, runtime). The shape of a track used to be a
`ControlPoints` array inside the scene builder, which is fine for one track and
useless for four: the LifeSim has to name a circuit, quote its length before a
race and draw it in a picker, and none of that is reachable from editor-only
code. So the shapes moved into runtime code and the builder consumes them
through the same resampler — the length the menu quotes IS the length the car
drives. Layouts 2-4 were generated as polar loops, `r(t) = R(1 + Σ aₖcos(kt+φₖ))`,
which cannot self-intersect however the harmonics are tuned, then checked for
minimum corner radius and self-clearance; both checks are now in the self-test,
so a future edit that puts a 12 m hairpin in fails loudly.

| circuit | length | laps | character |
| --- | --- | --- | --- |
| Sunset City GP | 1168 m | 3 | the original — downtown, close walls |
| Harbor Point | 824 m | 4 | narrow, technical, concrete and corrugated hoarding |
| Ridge Pass | 1632 m | 2 | long and flowing, dry-stone walls, trees |
| Airfield Sprint | 1468 m | 2 | two long straights joined by hairpins |

Laps differ so every race is ~3.3 km, which is what keeps ONE fuel and wear
economy honest across four circuits. The LOOK of each is a `Theme` in the
builder — ground, barrier and tree textures plus scenery density — keyed by
track id and asserted by the self-test, because a missing theme is invisible: it
just builds another city circuit. One scene per track, `[0] LifeHome` then the
catalog in order; `TrackCatalog.SceneIndex` is the only place that contract is
written down, and the self-test checks Build Settings against it.

**Seven hours** (`TimeOfDay`). Dawn, morning, noon, afternoon, sunset, dusk,
night — replacing three hard-coded arrays in RaceHandoffApplier that only knew
morning/afternoon/night. Fog does most of the work: the draw distance is 360 m
and the circuits are up to 660 m across, so what reads as "time of day" is
mostly the colour the world fades into and how close in it starts, which is
exactly how a PS1 game got its atmosphere. The LifeSim still has three activity
slots — the whole economy is built on three actions a day — so the seven fold
into three bands and the day number picks within the band, meaning racing the
morning slot on Tuesday and on Wednesday are not the same picture. A TIME row
beside the track picker overrides that for any race — it started as a
practice-only cycle tucked under the practice button, which is how you end up
with seven skies and a player who reports there is no way to change the time of
day.

Dark hours light the world: a new additive `PSX/Glow` pass draws headlight and
tail-light sprites on every car plus the pool they throw on the tarmac, and
street lamps along the barriers (`NightGlow`). Brake lights are independent of
the hour — they come on whenever the car is braking, day or night, because that
is the one lighting cue that tells you what the car in front is about to do.

**A garage you can look at** (`CarViewer`). The garage could name a car and
price it but never show it, which after the vehicle pack landed is a strange
thing for a garage to be: the player picks between 317 cars wearing sixteen
bodies and had no way to see which one they bought short of starting a race. A
320x200 point-filtered turntable renders the shell and livery the car actually
races in, on the garage tab and on the market's buy page. It is a component on
the LifeHome object rather than on the panel, because Rebuild destroys the whole
body on every button press and a render texture reallocated per keypress is how
a menu starts stuttering. Vertical drags are handed back to the enclosing
ScrollRect — the viewer sits at the top of the garage, which is where a thumb
starts a flick.

**Verification.** `Tools > PSX Racing > Capture Screenshots` now sweeps every
circuit from four angles, every hour from two, and every camera view — framed
through ChaseCamera's own offset and FOV tables rather than a copy of them,
because all of these fail SILENTLY: a barrier textured with the wrong JPEG, a
ground plane that does not reach its own back straight, a headlight quad buried
in the bodywork, a bonnet cam looking out from inside the windscreen. The
obstacle audit runs on all four circuits against each one's own road width, and
the self-test gained sections for circuit geometry, the hour table and the
camera list.

**Not done.** Every circuit is FLAT. The road ribbon, the ground plane and its
collider all live at y = 0, and giving one of them elevation means giving all
three of them elevation plus a shoulder mesh to join road to ground — which is
a pass of its own, and the one that would make Ridge Pass deserve its name.

## CAR IMPORT, PASS 3 (2026-08-22) — bodywork

Pass 2 closed everything about a car except its shape, and said so: "the RX-7
mesh still stands in for all 317 — that is the one deliberate hole." This pass
closes it with a 15-vehicle pack from the same artist who made the FD, plus a
free compact pickup. Sixteen shells against 317 cars, so the job is curation,
not lookup.

**The pack, identified.** The author shipped reference art beside the meshes, so
the Americans and the R32 are named by their own blueprints and the A80 by its
cutaway. The European folder ships none, so those were identified from the shape
and the factory colour names on the liveries — "Signal Red" and "Ivory" on a
1960s hardtop roadster is a Pagoda. Every identification is corroborated by the
geometry the baker measures: R32 2.62 m against a real 2.615, A80 2.55 against
2.55, E30 2.55 against 2.57, Defender 110 2.80 against 2.79.

**`Art/Car/Models/<key>/` — three OBJs per model, generated.**
`tools/carmodels/export_models.mjs` splits the pack into a body file and two
axle files. The split is at the FILE boundary because Unity's OBJ importer
splits on MATERIAL, not on object: the pack paints a whole car from one 128×128
sheet, so an OBJ carrying `o body` and `o wheel_FL` imports as a single merged
mesh named "default" with the axles gone. Front and rear axles are separate
files so the baker never guesses which end is the nose — an overhang heuristic
looked sound until the cab-over van turned up with its front axle further from
the bumper than its rear one is from the tailgate. One model comes in as an
empty OBJ and is round-tripped from its GLB through Blender.

**`CarModelBaker` (editor) → `Resources/CarModels/<key>.prefab`.** Every number
a shell carries is MEASURED off the imported mesh — axles, track, tyre radius,
body box, and which way Unity's importer landed the car. A hand-written table
would be four numbers per model that quietly disagree with the geometry the
moment the pack updates. The FD is the deliberate exception: it keeps its
literal 2.425 / 1.46 / 0.31 and its pinned wheel material, because it is the car
every handling decision was made against and the self-test asserts it has not
moved. Liveries bake to one shared material each, with a mean colour so a car
can be given the paint its catalog entry claims.

**`CarModelLibrary` — which car wears what.** Two passes: 155 hand-mapped (the
cars the pack actually is, their badge-engineered twins — a Superbird IS a
winged Charger, a Cougar IS a Mustang — and a dozen calls the scorer gets wrong
on principle), then 162 scored on body class, then continent, era and weight.
Body leads because a Civic in a Charger reads as broken while a Civic in a
French supermini only reads as a substitution. Era is the one axis that can go
NEGATIVE: rewarding a close year is not enough, or the scorer happily dresses a
1992 supercar in a 1965 roadster on body class alone. 14 of 16 shells race;
`Docs/car_model_mapping.txt` lists all 317 assignments.

**`CarBody` (runtime) fits the chassis to the shell**, not just the skin —
collider, blob shadow, wheelbase, track and tyre radius, through a new
`CarController.RebuildGeometry()`. A '69 Charger is 60 cm longer in the
wheelbase than the FD, and having it turn in like an FD is a bigger lie than the
mesh swap fixes. `CarBody.applyGeometry` turns that half off if a handling
change ever needs isolating from the body.

**The two shells no catalog car can wear** — the panel van and the compact
pickup — became roadside parking. Nothing in a GT4-derived list is a 1950s
delivery van, and the scorer is barred from reaching for them, so the worst-
matched car on a grid can never turn up to a race in a van.

**Verification.** `Tools > PSX Racing > Preview Car Models` renders all sixteen
to PNG from two angles without play mode; every failure mode here is visual and
silent (a car facing backwards, wheels beside the arches, a livery landing on
the wrong half of the sheet) and none of them throw. `LifeSimSelfTest` gained a
car-models section covering prefab presence, the FD's frozen numbers, a
road-car sanity band on every measurement, and that all 317 resolve.

## CAR IMPORT, PASS 2 (2026-08-22) — engines, aspiration, and the tuning ladder

P3 brought the 317-car catalog across as PHYSICS. This pass brings across
everything else a car is, short of its bodywork: what it sounds like, whether it
makes boost, and how far it can be built. The RX-7 mesh still stood in for all
317 — the one deliberate hole, closed by Pass 3 above.

**Catalog (`Resources/rg2_cars.json`, re-baked).** Five fields added per car:
`eType` and `asp` (raw GT4 engine type + aspiration), `dispCc`, `engineFamily`,
and the upgrade endpoints `builtHp` / `minKg`. The last three are ANSWERS, not
inputs — `resolveEngineFamily` and `getUpgradeHeadroom` are 700 lines of RG2
rules over GT4_SPECS, and baking what they return is exactly as correct as
porting them while costing the Unity build nothing. 317/317 resolve to a family;
137 turbo, 4 supercharged, 176 NA.

**Engine voices (28 families, `Resources/Engines/<family>/`).** Every car now
speaks with its own engine instead of the 13B-REW. 560 clips imported from the
Skril pack — the 15-take band ladder plus the intake layer and the two one-shots
— encoded once to Ogg Vorbis q8 on the way in. WAV would have put 180 MB in a
repo whose history is already 105 MB and force-pushes a WebGL build through it;
30 MB of Ogg costs one generation of a codec on broadband engine noise, which
is not audible. `EngineVoiceLibrary` owns the band ladders (they used to live in
the builder) and loads families lazily; `EngineAudio.SetFamily` rebuilds the
voice, and RaceHandoffApplier calls it per car for the player AND the AI field.

- **Import settings are split.** The core set keeps Vorbis q1.0 preloaded. The
  engine families are q0.8 and `preloadAudioData = false` — 560 clips
  decompressed at scene load is hundreds of megabytes of PCM in a browser tab,
  and a race touches five families. `EngineVoiceLibrary.Clip` pulls sample data
  on first reference, so only what races is ever decompressed.
- **EngineAudio does not build in Awake.** RaceHandoffApplier learns which car
  this is during Start, one phase later; building the default in Awake
  decompressed ~6 MB of RX-7 per car that nothing then played.
- **Forced induction is gated on data.** `TurboAudio` grew an `Aspiration` mode:
  turbo keeps the three-layer spool/max/blow-off rig, supercharger gets a
  belt-whine layer with no lag and no blow-off (a blower makes boost the instant
  the crank turns), and NA gets silence. 176 of 317 cars were getting sequential
  twin-turbo blow-off they do not have.

**Tuning ladder (five categories x four stages).** `CarTune` holds the pure
curves on the race side; `LifeSim/Upgrades` adds prices, days, skill gates and
per-car state. The `up*` fields have been sitting unread in `OwnedCar` since L3.

- Power scales the torque CURVE (never in place — `CarSpec`s are shared out of
  the catalog, so scaling `spec.curveNm` would tune every opponent too, and
  compound each race). Weight, drag, downforce, inertia and chassis rates all
  re-derive together inside `ApplySpec`, which is why the tune is a parameter to
  it rather than a pass afterwards.
- Brakes are capped by the TYRE stage (`BrakeGCapStock` 1.05 g): bigger brakes
  resist fade, they do not raise peak mu. Suspension maps RG2's turn-rate
  multiplier onto `corneringStiffness` — the input that moves turn rate in a
  raycast-wheel car — hard-capped at 13, the ceiling the drift tuning was set
  against.
- DIY vs shop survives; RG2's two-step "order a kit, then install it" does not.
  That needs an `ownedParts` inventory, and a menu-based build has no garage to
  walk into. Jobs queue into the same `pendingParts` list the repairs use, so a
  build costs real days and lands through the same rollover.
- **Drag is now solved from STOCK torque.** It was solved from whatever curve
  the car had, which after a power stage meant solving for MORE drag and pinning
  terminal velocity at the stock number — a full engine build would have
  accelerated harder and topped out at exactly the same speed. Invisible until
  someone timed it.
- **Two one-off mods** from RG2's PARTS_SHOP, which are not a ladder: WELD DIFF
  ($150-ish, skill 35) and SUPERCHARGER ($3000-ish, skill 85). The blower is a
  Roots curve — flat +30% to 60% of the rev range, tapering to +15% at redline —
  and is offered on NATURALLY ASPIRATED cars only. RG2's modular port allows it
  on anything because `CatalogCar` never grew the per-car `canSC` flag, but its
  own docs say the monolith excluded turbo cars, and `asp` is right there in the
  baked spec. Fitting one also switches the car's audio to the blower whine.
  The welded diff has no direct analogue here (there is no left/right diff in
  the model), so it scales the wheelspin ratio — the input to the yaw injector —
  by 1.3: the driven wheels break away together, which is the mod's whole point.
- New screen: GARAGE > PARTS + TUNING. Buy screen now shows the engine line
  (layout, displacement, aspiration, build ceiling) — with one body for 317
  cars, that line is what distinguishes two listings.

**Self-test** gained `TestEngineVoices` (every car names a family, every family
has every clip the ladder plays — an unimported family is SILENT, not a crash)
and `TestUpgrades` (stages only ever improve, brakes never out-run tyres, all
20 stages quote and land through Sleep on both the cheapest and the dearest car
in the catalog).

**Known cost:** WebGL.data grows by ~30 MB. Lower `EngineClipQuality` in
`PSXRacingBuilder` to trade it back, but not toward 0.65 — `AudioToneChain`'s
+7.5 dB low shelf at 110 Hz re-amplifies exactly what a low-bitrate Vorbis
encoder discards down there, which is what "sounds 1980s arcade, no bass" was.

## DEVICE BUGS, ROUND 3 (2026-08-22) — menus and instructions

8. **"Options are off screen — I can't repair the car."** Two causes stacked.
   (a) The body scrolls, but **a ScrollRect is driven by drag events routed from
   whatever Graphic is under the finger** — every MenuKit label is
   `raycastTarget = false` (they must be, or they swallow clicks meant for the
   buttons behind them) and a bare RectTransform is not hit-testable, so a drag
   on a label or on empty space scrolled NOTHING. Only a drag that happened to
   start on a button worked. `ScrollBody` now puts a transparent Image on the
   viewport. (b) `MenuKit.Label` pivoted on its ANCHOR regardless of text
   alignment, so every left-aligned label was centred on its x and hung half its
   width further left than the call site wrote — with 500-800 unit labels that
   put the garage's car name and repair rows off the left edge. Labels now pivot
   on their ALIGNMENT, so x means the edge the text starts from. Buttons still
   take a centre (`MenuKit.ColLeft`/`ColRight`).
   All hard-coded x margins (-610/-400/-360/-300/-230) became `ColL`/`ColR`/
   `ColW`, derived from the real canvas. The MARKET tab's two-column layout is
   now a single column: its listings ran to +104 units while its garage column
   started at +120, so on a handheld they were one long car name from
   overlapping. The body scrolls, so length is free and width is not.
9. **"Notifications tell me to use PC controls."** The finish screen said PRESS R
   on a device with no keyboard. It now names the control the player has — TAP
   RESET (TOP RIGHT) when the touch layer is up — and the RESET/CAM buttons were
   16% white on a bright outdoor scene, i.e. two blank grey slabs; darker ground,
   solid type, larger.

`LifeHomePreview` now renders EVERY tab (three of these bugs were on tabs nobody
had rendered) and logs content-vs-viewport height plus whether the drag catcher
is present, per tab per aspect — a static screenshot can never show whether a
screen can be scrolled.

## DEVICE BUGS, ROUND 2 (2026-08-21) — controls

5. **Steering wheel turned the wrong way.** `TouchWheel.TryAngle` took
   `Atan2` of the point `ScreenPointToLocalPointInRectangle` returns — which is
   relative to the PIVOT, and the hit zone is pivoted at its bottom-left corner
   (0,0) so it can anchor into the corner of the screen. So rotation was
   measured about a point 150 units down-and-left of the hub the player is
   actually turning: the 15% dead zone guarded the corner instead of the centre
   (so dragging through the real hub could wrap +/-pi and snap to full lock),
   small movements near the hub produced huge angle swings, and on the side
   nearer the origin the sign of the delta inverted outright. Now measured from
   `self.rect.center`, which is correct for any pivot.
6. **Brake permanently engaged at 30%.** `TouchPedal.Amount` was both the value
   the car read AND the value the gauge drew, so `ReflectState` — the mirror
   that lets keyboard players see the pedals move — was a feedback loop:
   PlayerCarInput forces `brake = 0.3f` while input is disabled on the starting
   grid, that got written into `Amount`, the input layer read it straight back
   out as a real brake request, and wrote it in again next frame. Nothing could
   clear it. Display is now a separate `displayAmount`; only a finger writes
   `Amount`. **An output must never be readable as an input.**
7. **E-brake engaged in the wrong direction.** It is a LEVER, not a pedal. RG2
   wires it `addSliderPedal('ebrkBtn', …, { ignoreInvert: true })` with the
   comment "so it always reads 'pull bottom to engage' like a real handbrake" —
   `dir = -1` against the pedals' `+1`. The port gave all three controls the
   pedals' direction. New `TouchPedal.topMounted`: drag DOWN to engage, fill
   hangs from the top, thumb rides the leading edge downward.

Verified two ways. `PSX Racing/Preview Touch Control Panel` renders the
ASSEMBLED panel at known control values (gas 0.75 / e-brake 0.4 / steer +0.5),
because every control bug so far has been in wiring or geometry, not artwork,
and dumping the sprites alone could never have shown any of them. Edit mode does
not run lifecycle callbacks, so the tool invokes `Awake` by reflection.

And `PSX Racing/Run Controls Self-Test` (`Editor/ControlsSelfTest.cs`) now DRIVES
real pointer events into TouchPedal and TouchWheel and asserts which way each
moves: pedal up engages / down does not, e-brake down engages / up does not, a
mirrored 0.3 brake never becomes a real request, and the wheel's sign follows
the ROTATION rather than which side of the rim was gripped. These controls have
shipped wrong twice; all four bugs compiled cleanly and looked fine in a
screenshot, so behaviour is the only thing worth asserting. Writing it caught
two errors in the test's own geometry — the wheel accumulates from its current
rotation (a second sweep from a turned wheel only unwinds it), and clockwise at
9 o'clock means the hand goes UP.

## DEVICE BUGS (2026-08-21, from a phone playtest) — all four fixed

1. **Throttle stuck on with no finger on the screen.** Every analog touch
   control latched on pointer-down and cleared only on pointer-up, and on mobile
   that up event routinely never arrives — the browser claims the touch for a
   gesture, focus is lost, or the EventSystem retargets the id. New
   `TouchPointerWatch.AnyPointerDown()`; pedal, wheel and shifter all release
   when no pointer exists anywhere. Deliberately NOT a per-id check: the
   EventSystem's pointerId and the Input System's touchId are not contractually
   the same number, and a wrong id match would release a control the player IS
   holding, which is worse than the bug.
2. **Invisible barriers around the track.** 19 building colliders reached clear
   across the racing line, on the Solid layer with no renderer of their own.
   Two compounding errors: the collider was sized from `Renderer.bounds` (a
   WORLD AABB, so a building yawed to face the road reported its DIAGONAL, up to
   1.41x its footprint) and that inflated size was then applied along the
   building's own rotated axes; and placement used `extents * 0.5f` when extents
   is ALREADY a half-size, seating every building about half its own width too
   close. Now measured with `LocalBounds()` — an oriented box in the building's
   own frame — and placed off its road-facing FACE at a named clearance.
   New `Editor/TrackObstacleAudit.cs` (menu: PSX Racing/Audit Track Obstacles)
   reports anything reaching inside +/-6.6 m of the centreline; it now runs
   clean. Measure with `Collider.ClosestPoint`, not `bounds` — auditing rotated
   boxes by their AABB reproduces the very error being hunted.
3. **Race view boxed in black bars.** The framebuffer was a fixed 320x240 shown
   4:3 letterboxed, so a 2.24:1 phone lost about a third of its screen.
   PSXCameraOutput now locks the VERTICAL resolution at 240 lines and takes the
   width from the display (clamped 256-960, even numbers only so the 4x4 dither
   tile does not crawl), rebuilding on orientation/resize. Locking lines is what
   preserves the era — pixel size, dither scale and HUD type all key off the line
   count, and the PS1 was itself a fixed-lines/variable-width machine.
4. **Menus unreadable on mobile.** Three-part fix: the type scale went up ~20%;
   `MenuKit.DesignHeight` drops from 720 to 560 units on handhelds (the scaler
   matches height, so that number IS the magnification — together about 1.6x);
   and the body became a ScrollRect, because a 560-unit column cannot show what
   a 720-unit one did and the content should move rather than the type shrink.
   `MenuKit.HalfWidth` was added so columns stop being absolute offsets that
   only fit one canvas.

Two traps worth remembering, both hit here. `MenuKit.Label`/`Button` centre
their rect on the x they are handed and TEXT ALIGNMENT DOES NOT MOVE THE RECT,
so a column is placed from its edge offset by half its own width — left column
clipped off-screen, right column printed through its neighbour. That arithmetic
now lives in `MenuKit.ColLeft`/`ColRight`. And the menu preview tool rendered
into a RenderTexture while MenuKit read the real `Screen`, so it silently
validated the DESKTOP layout and labelled it "phone" —
`MenuKit.ScreenSizeOverride` lets the preview impersonate a device properly.

## P4 — LATERAL & DRIFT POLISH (2026-08-21)

- **Downforce is derived per-car, not configured.** New
  `downforceWeightFractionAtVmax = 0.35` solves the coefficient as
  `fraction * m * g / vmax^2`. Expressed as a fraction so it means the same
  thing on all 317 cars — a 950 kg hatchback and a 1700 kg GT both gain 35% of
  their own weight where they are hardest to hold, instead of one gaining 60%
  and the other 15% off a shared number. Lands the reference FD at a 1.05
  coefficient, which is exactly where the handling notes wanted it by hand; the
  old flat 0.35 gave the FD 11% of its weight, enough to measure and not to
  feel. Applied to the body, so it reaches grip through spring compression
  rather than being poked into the friction circle.
- **One-tick-stale `Drifting` fixed** by a slip pre-pass. `UpdateDriftState` ran
  after `TireForces`, which put the tick's consumers on two sides of the mode
  switch: steering and the gesture layer read LAST tick's answer while the yaw
  damper and injector read this one, so for one tick of every mode change the
  car steered as if gripping while being damped as if sliding. `RefreshSlipAngles()`
  measures slip from THIS tick's velocity using last tick's contact geometry and
  steer angle — strictly fresher than what the steering saw before — and the
  state machine now runs before anything reads it.
- **~20 unnamed constants promoted** out of `ApplyYawLayer` and
  `ApplyLateralStabilizer` into a documented block. Values unchanged; the point
  is that a tuning session should not have to find them by reading the
  algorithm.
- DriftSeconds already shipped with L2. Still deliberately kept: inferred
  wheelspin, the flat brake model, the CG-applied stabilizer.

## P2 + L4 — CONSEQUENCES & THE BLACKLIST (2026-08-21)

Verified by sandbox scene build (BUILD OK) + `PSX Racing/Run LifeSim Self-Test`
(90+ assertions, all pass). New file: `Scripts/LifeSim/Blacklist.cs`.
Save format is at v4 (blacklist lists + `atFaultIncidents` + `lastAnyRaceDay`).

**P2 — collision consequences.**
- `atFaultIncidents`: CollisionResponder now counts DISCRETE hits (≥6 m/s
  closing, square-on, one per 0.6 s) alongside the continuous DamageScore. The
  insurer wants incidents, not energy. Capped at 2/race so one bad night cannot
  spend L5's whole six-incident allowance. Shown on BILLS; the premium
  multiplier itself is still L5's job.
- AI proximity: each AI eases up to 2.4 m off line and lifts when it is closing
  on a car within 14 m ahead and 2.6 m across. Deviation from the plan — this
  reads transforms directly rather than RaceManager's progress cache, because
  "am I about to hit them" is a position question and progress-along-track
  answers a different one. Not overtaking logic, deliberately.
- AIDriver moved Update → FixedUpdate. It writes CarController's inputs and
  reads physics state, so on Update how hard the field drove depended on the
  player's frame rate.
- Recovery: the 4 s stuck timer drops to 1.5 s while CollisionResponder reports
  wall contact (a pinned car never recovers on its own), plus a wrong-way clock
  (2 s below −0.3 alignment above 3 m/s) — the lookahead steering will otherwise
  chase a car spun past 90° around in a circle indefinitely.
- Damage → body/paint wear + impact-cause fault was already live from P1/L2.

**Open decision 3 is closed: the field is catalog-built.** `RaceHandoff` carries
`OpponentSpecIds` / `OpponentSkills` (parallel ';'-joined lists) and
RaceHandoffApplier respecs the grid, retiring any car past the end of the list.
`LifeRules.FillOpponentField` draws 3 cars priced 0.65–1.20× the player's car,
the band opening upward with street tier (tier 3: 0.89–1.65×), skills spread
±0.04 around 0.88 + tier×0.04. Before this a beater and a supercar raced the
same four RX-7s.

**L4 — blacklist.** Ten rivals, ranks 10→1, gates and taunts ported from RG2.
Open = all lower ranks beaten AND wins ≥ gate AND rep ≥ gate; rep decay can
re-lock an unfought rival, defeats are permanent, the call-out latches one-shot
per rank and its mail expires after 3 days. Challenge = 1v1 in the rival's
signature car at a tuned skill (0.90 → 1.05 up the ladder), started from the new
RIVALS tab, side-by-side on the grid. Purse `400 + (10−rank)×220`; a win adds
+2 rep on top of the normal tier gain.
- Deviation from RG2: rival cars resolve to the PRICIEST match of the first
  matching name pattern, not the first. The catalog is price-sorted, so "first"
  meant cheapest — KAZE got a $13.5k 185 hp FC when the same pattern also held
  an $18.5k 215 hp one. Same car, better example of it. The self-test prints the
  whole resolved roster, since a catalog re-bake can silently change it.
- A challenge does NOT burn the one-purse-race-a-day cap (there are ten in a
  career and each needs its own gate). Rep decay therefore moved off
  `lastRaceDay` onto the new `lastAnyRaceDay`, or a player working the ladder
  would decay while racing constantly.
- `BlacklistRival.venue` is carried and deliberately unused: RG2 ignores it too
  (every challenge there is a meet drag), and this build has one circuit.

## P5 — CONTROLS & HANDLING (2026-08-21, after the "swings back and forth" report)

Root cause was INPUT, not the tire model:
- Touch throttle/brake were BINARY (`Pressed ? 1f : 0f`). First gear makes ~2x the
  force the rear tires hold, so binary throttle pins wheelspinRatio instantly, and
  wheelspin is the direct input to the yaw injector that rotates the car.
- The steering pad was RELATIVE (origin = wherever the finger landed), so neutral
  drifted and every correction overshot.

Shipped: `TouchWheel.cs` (rotary accumulation, +/-pi unwrap, 15%-radius hub dead
zone, accumulates from current rotation, +/-165 deg clamp, axis = rot/165),
`TouchPedal.cs` (relative-drag travel, anchors at lastAmt, full lift on release),
`TouchShifter.cs` (40-of-53 throw, one shift per drag, no tap-to-shift). All
multi-touch by pointer id. Wheel art is generated procedurally (see
`PSX Racing/Preview Touch Control Art` to dump the sprites to PNG).

`PlayerCarInput` now passes analog sources RAW — CarController already rate-limits
road wheels at 220 deg/s, and the old MoveTowards was a second filter in series
adding lag. Release uses RG2's slew: instant attack, instant direction flip,
rate-limited only unwinding to centre (3.0 units/s).

Three P3 regressions fixed at the same time:
1. Gear ratios were derived from RG2's `gearSpeeds`, which LOOK per-car but come
   from GEAR_PATTERNS — a generic table keyed only on gear count. Its 6-speed row
   implies a 5.88 ratio spread vs a real box's 4.98, giving every six-speed a first
   gear ~24% short and inflating wheelspin ~50%. Now uses fixed per-gear-count
   shapes (the 6-speed row IS the FD's real box) anchored to redline-at-vmax.
   FD is now 3.65/2.11/1.46/1.05/0.84/0.73 vs the real 3.483/2.015/1.391/1.0/0.806/0.7.
2. `ApplySpec` changed mass without rescaling springs, dampers, anti-roll or
   staticWheelLoad — every non-RX-7 ran FD suspension. `ScaleChassisToMass()` added.
3. Impact grace could be re-armed by every contact of a wall scrape, holding the
   stabilizers down for the length of the wall (same self-feeding shape as the old
   drift-latch bug). Now requires severity > 0.15 (~1.4 m/s into the surface).

NOT touched: the yaw injector and counter-steer assist. Tuning them against binary
input would have been tuning against the wrong signal. Re-evaluate with analog.

## STATUS — P1, L1, L2, P3, L3 SHIPPED (2026-08-21)

Verified by batchmode scene build (BUILD OK) + `PSX Racing/Run LifeSim Self-Test`
(60+ assertions, all pass; writes `PSXRacing_selftest_log.txt`).

New files: `Scripts/CollisionResponder.cs`, `Scripts/CollisionAudio.cs`,
`Scripts/RaceHandoffApplier.cs`, `Scripts/CarCatalog.cs`,
`Scripts/LifeSim/FaultCatalog.cs`, `Scripts/LifeSim/CarMarket.cs`,
`Editor/LifeSimSelfTest.cs`, `Resources/rg2_faults.json`, `Resources/rg2_cars.json`.

Save format is at v3 (`LifeSimManager.Migrate`): v2 retired v1's synthetic
uncatalogued faults, v3 added `OwnedCar.specId` / `catalogPrice`.

Both data files are BAKED from RG2, not hand-copied. To re-bake after an RG2
change: esbuild-bundle a TS entry importing the tables with `--alias:@=./src` and
run it against `Assets/PSXRacing/Resources`.
  - faults: FAULT_POOLS (45) / FAULT_EFFECTS (39) / USED_FAULTS (36).
    USED_FAULTS is module-private in RG2, so bake from a generated copy with the
    const exported rather than editing the game's source.
  - cars: CAR_CATALOG filtered to ACCESSIBLE_CAR_IDS, minus bikes, minus the five
    JOB_VEHICLE_IDS (ambulance/tow_truck/police_cruiser/semi_truck/box_truck —
    they carry 10- and 13-speed truck gearboxes and RG2 excludes them from its
    own classifieds). Yields 317 cars, every one with a torque curve.
    Peak torque is DERIVED: scale the normalized curve so peak power == catalog
    hp. Gear ratios are derived from per-gear speed bounds, anchored so the
    engine hits redline at the car's spec'd top speed.

Three open decisions surfaced while building (see the artifact's callout):
1. The seeded 73,300-mile RX-7 starts at ~76 condition; one 3.5 km race takes tires
   76 -> 38 (mileage ramp 1.73x), so a new save gets a fault on race one. Either seed
   the car healthier or drop `RaceWearScale` to ~0.6. Needs playtest evidence.
2. Health has no consumer. 20 days without food drives it to 0 and nothing happens.
   Faithful to RG2 (health only gates gym level 3 there) — a design decision, not a bug.
3. ~~The AI field is still four hardcoded RX-7s~~ — CLOSED, see P2 above.

NOTE: sandbox builds do NOT write back to the user's project. Re-run the scene build
in the user's editor (`psx_autobuild.flag` + focus) to regenerate CityCircuit.unity
with the collision + handoff components. Also: `robocopy /MIR` in the sandbox scripts
overwrites the sandbox's freshly-built scene with the user's stale one, so verify a
built scene BEFORE the next sync.

---

## PART I — Finishing the LifeSim

### Current state (audited 2026-08-21)

- **Shipped v1**: slot clock + single Rollover() pipeline; 7-job economy (FOOD DELIVERY and
  TRAFFIC COP dropped from RG2's 9), payday flat 22% tax, 55% hire, firing ladder; bills +
  credit on the 1st; health/hunger/sleep; groceries; race apply-back (tier purse, rep, wear,
  fuel, odo, threshold faults); wizard; 5 tabs (MAIN/GARAGE/EAT/BILLS/JOBS).
- **Declared but unwired**: RaceHandoff fault-handicap fields (neither end), purse fields
  (ApplyRaceResult pays from its own table), DriftSeconds (read by wear math, never written
  → drift wear always 0), CarLoan/BankLoan (decremented, never populated), mail list,
  Housing ladder table, mechSkill, up* stages, garageSlots, TimeSlot (sent, never read).
- **Missing**: repairs (wear is a one-way ratchet — breaks after ~20 races), car
  catalog/market, loans UI, housing moves, gym, newspaper, mail UI, blacklist, hidden
  faults/inspection, upgrades, insurance record multipliers, calendar tab, delete-save UI.

### L1 — Garage pass (repairs) — FIRST

- Bake fault data via a ~20-line node script in RG2: FAULT_POOLS (44), FAULT_EFFECTS (41),
  USED_FAULTS (36) → `Assets/PSXRacing/Resources/rg2_faults.json`. Preserve row order
  (RNG-parity warning in RG2 comments).
- Replace LifeRules.RollThresholdFault with diagnoseFault.ts port: mileage tier <60k/<150k,
  one fault per stat, severe prefers cost ≥ $100, jpn ×1.0 in v1, faults visible
  ("DIAGNOSED:" on race result; hidden layer is L5).
- Venues: DIY / MECH ×2 / DEALER ×3 instant, cap $12,000, PendingPart queue ticked in
  Rollover. Math (repairCost.ts):
  `diff = (mech?55:45) + min(20, floor(cost/100)*3)`;
  `carCostMult = clamp(sqrt(price/15000), .6, 3.5)`;
  `laborFactor = clamp(.45 + (cost-150)/450*.55, .45, 1)`; `effMult = 1+(ccm-1)*lf`.
  DIY days `max(1, round(max(1, days+ceil(diff/25)) / (1+max(0,skill-diff)/6)))`.
  Skill gain: challenge=diff−skill; ≥0 → 3+min(5,round(c/8)); else max(0, 2+round(c/10)).
  Intent: skill starts 15, diffs ~45-55 → early game is mechanic-priced; DIY affordability
  IS the progression.
- Mechanic services menu (8 rows: Oil Change $50/+15 eng … Full Service $500/+30 all);
  services clear faults on their stat lane. Fuel grades 87 $0.99 / 93 $1.24 / 110 $2.49,
  free for FUEL TANKER.
- Garage UI: per-fault 3 venue buttons, IN PROGRESS lane, QUICK-SELL 50% of value − payoff
  (never the only car). `getCarValue = catalogPrice*(eng*.3+tires*.15+body*.3+paint*.25)/100
  * max(.2, 1−odoMiles/200000)`; paidPrice stands in for catalogPrice until L3.
- Acceptance test: 35 days + 20 races; wear recoverable, 5 paydays, 1 bills fire.

### L2 — Race wiring (small, high leverage)

- ComputeFaultEffects aggregator (accel/fuel/grip/brake multiply; steerPull adds signed with
  cached ±1 dir; shiftMult/engineWearMult max; HUD flags OR) → fill RaceHandoff request.
- CarController application points: AccelMult×drive force, GripMult×mu, BrakeMult×brake,
  SteerPull steering bias, ShiftMult×shift time; RpmFlutter/HideGauges → RaceHUD.
  RaceHandoffApplier no-ops when !FromLifeSim.
- DriftSeconds: accumulate while Drifting at speed, stamp on finish (shared with P4).
- TimeSlot lighting: 07:00/13:00/20:00 presets, headlights binary at night.
- Purse from handoff = single source; HUD shows the offer. PRACTICE LAP: costs slot, no
  purse/rep/lastRaceDay.
- v1.5 option — breakdown race events: pFrame = (odo<5000 ? 5e-6 : 5e-5) *
  (1−(eng+tires+body)/300) * wearMult; pRace = 1−(1−pFrame)^(60*raceSeconds).
  ENGINE STALL (3 s cut, −15 eng) / FLAT TIRE (DNF, −20 tires) / OVERHEATING (DNF, −15
  eng); DNF = last, +1 rep, $50 tow.

### L3 — Cars & money

- Bake catalog from GT4_DB (380 rows; accessibility ≥100 hp) → rg2_catalog.json with
  id/name/hp/kg/drv/price/gears/color. v1: curate ~40-60 cars, FR/MR only until P3 adds
  FF/4WD.
- OwnedCar.catalogPrice + saveVersion 2 migration (paidPrice → catalogPrice). Field names
  are save API (JsonUtility silently resets renames).
- Newspaper classifieds: 5 listings/day, expiry day+3..7, cond max(15, 100−mi/2500+rand),
  price MSRP*(0.3+cond/200), ×0.55 problem disclosure (30% of used), exclude owned + job
  vehicles, refresh in Rollover.
- Dealer lot: 8 rows, 15% new at MSRP, cond 40-89, no pre-faults, RESHUFFLE.
- Finance: cash · used 48mo @10.5% 15% down · new 60mo @8.5% 10% down; APR + tier adj
  (EXC −.005, GOOD 0, FAIR +.015, POOR +.03, BAD +.06; lease needs ≥GOOD). Amortized
  payment: P*(r(1+r)^n)/((1+r)^n−1), r=apr/12. Monthly decrement already exists in bills.
- Used pre-faults from USED_FAULTS (v1: detected split only). Tier detect/priceMult:
  cheap .30/.92, moderate .50/.80, extensive .70/.65, severe .60/.40.
- Selling: ad at 0.9×value → daily offers (chance .45+.10/day cap .85, skip weekends,
  offer .5-.95×value) as mail ACCEPT/DECLINE; refuse upside-down; trade-in 50%. Makes MAIL
  tab real.
- Starting lanes replace RX-7 seed: BEATER (15-39 cond, 100-220k mi, $400-3k, paid,
  cheapest fault surfaced) / USED RELIABLE / NEW LOAN / LEASE (36mo, residual 45%, MF
  .0035). H1287: money never deducted, only the loan follows. targetMonthly =
  max(80, dailyPay*20*.25).
- CarSpec drives CarController (see P3). RX-7 mesh recolored by catalog color; stats carry
  identity in v1.
- Jobs to 9 (cheap): FOOD DELIVERY ($0 + $2-10 tips, free daily meal), TRAFFIC COP ($115 +
  ticket bonus) as data + perk flags.
- Price formulas (three coexist by design): calcUsedPrice → starting lanes only;
  MSRP*(0.3+cond/200) → lot/newspaper; getCarValue → owned resale/insurance.

### L4 — Blacklist (needs L3 rivals)

- Roster rank 10→1 (wins/rep gates): JUICE Civic 3/10 · PENNY Roadster 4/18 · DEACON
  Silvia 5/25 · KAZE RX-7 FC 7/33 · BIG SAL Cuda 9/41 · WRENCH Impreza 11/50 · DUCHESS
  S2000 13/58 · PREACHER Supra 15/66 · GHOST R34 18/75 · CALLAHAN RUF CTR2 20/85.
- Open = all lower beaten AND wins ≥ gate AND rep ≥ gate; decay can re-lock unfought;
  defeats permanent; one-shot page latch, 3-day expiry. Pager → mail/toast ("COME TAKE MY
  SPOT" / "LADDER MOVES"). Port the 2 taunt lines per rival.
- Race = 1v1 vs named AI in the rival's catalog car, tuned skill. RG2 ignores declared
  venue (all meet drags) — matching is fidelity. Rival challenges don't burn the 1/day cap.
- Out of scope (unimplemented in RG2 too): pink slips, boss-car uniqueness.
- Gates tuned against +6/+4/+2 rep and 1-race/day cap — retune together or not at all.

### L5 — Deep life (piecewise)

- Housing ladder 6 tiers (apt1br $425 … Nice Home $189k @ $1,325/mo). Newspaper REAL
  ESTATE listings; approval: min down by tier 5/10/15/20%, DTI ≤ 35%, loan ≤ 4× annual
  income, 360mo @ 7.5%+adj. Garage slots from tier — ENFORCE the cap (RG2 never did).
  Eviction at 3 missed home payments.
- Bank loans: caps $50k/25k/10k/3.5k/denied; APR 9.5/11.5/14.5/18.5/24%; DTI gate; −5
  credit originate, +15 payoff. Port bankLoan.ts (RG2 has a duplicate impl in finance.ts —
  bankLoan.ts is live).
- Gym: levels $0/$10/$20 → fit +2/+4/+6, health +1/+2/+3 minus hunger penalties; consumes
  slot; once/day; closes the fitness loop (decays daily with no raise path today).
- Hidden faults + inspection: H1309 (wear rolls → hidden with vague symptom; INSPECT sells
  the name). DIY once/day vs detectChance; shop $120 skill 65 / $360 skill 90 at
  clamp(detect + skill*.003 + .15, .05, .95); reveals every ~0.8-3.2 mi.
- Upgrades 5×5: headroom overrides (FD 500, Supra 700, R34 560…), POWER_STAGE_FRAC
  [0,.45,.7,.88,1], $55/hp + $12/kg, two-step DIY (kit ships 2 days → install). Feeds
  CarSpec.
- Insurance multipliers (+15%/ticket cap 10, +25%/incident cap 6) — incident source =
  P2 collision damage → atFaultIncidents.
- Also: calendar grid, coffee buff, connections (mechanicDiscount @10 visits,
  dispatcherTrust, sceneRegular, localDeals @60 days), save export/import JSON (iOS Safari
  evicts storage ~7 idle days).

### Part I hazards

1. JsonUtility renames silently reset → saveVersion bump + migration always.
2. Unit trap (third time): WPX_PER_M 6.2746 and all converted constants ONLY in LifeRules'
   unit-bridge block; unit-test 2400 m → ~39% fuel, ~15 tires.
3. Coupled balance: blacklist gates ↔ rep gains ↔ race cap; rent ↔ purse; RaceWearScale
   moves the whole repair economy.

---

## PART II — Proper racing physics

Audit verdict: longitudinal (15-pt torque curve, 6-speed, engine braking), lateral
(slip-angle + friction circle + stabilizer), drift (state machine + injector + damping
tiers) are solid. **Collision is absent**: zero OnCollision* in 29 scripts, walls friction
.05 / bounce 0 (combine Minimum), stabilizers erase impacts in ~3 ticks, no audio/VFX/
shake/damage, AI collision-blind.

### P1 — Collision foundation (feel) — FIRST

- Tunneling: 80 m/s × 0.02 s = 1.6 m/tick vs 0.35 m walls. Player rb →
  ContinuousDynamic, AI → ContinuousSpeculative; thicken walls outward ~1 m; overlap the
  292 segment boxes (seam-snag prevention).
- New CollisionResponder.cs (first collision code): classify contact normal vs velocity —
  glancing <~30° → scrape state (loop + sparks + mild scrub); hard >~60° →
  impulse-proportional scrub + camera shake + tiered impact one-shot. Keep wall friction
  low (NFS rail-slide is desirable); code supplies angle-aware consequence.
- Post-impact grace ~0.5 s scaled by impulse: fade ApplyLateralStabilizer + counter-steer
  assist toward 0 so hits knock the car off line. THE change that makes collisions exist.
- ChaseCamera trauma shake (impulse → trauma, shake ∝ trauma², fast decay).
- Audio: only skid_loop.wav exists. Add 3 impact one-shots + 1 scrape loop through
  AudioToneChain (flagged missing since the audio port).
- Suspension ray hygiene: rays have no layer mask + QueriesHitTriggers on → wheels can
  "ground" on walls/buildings. Own layer for walls/buildings, mask ray to ground+road.

### P2 — Collision consequences

- Damage: scaled impulse sum → RaceHandoff.DamageScore → body/paint wear on apply-back;
  past threshold roll an impact-cause fault (pools tag cause: wear/impact/ignition/
  cooling); big hits → atFaultIncidents (feeds L5 insurance). 14-zone model stays deferred.
- AI proximity: neighbor check from RaceManager's per-frame cache → lateral bias away +
  throttle lift when closing. Not overtaking logic, just "don't drive through the player."
- AIDriver Update() → FixedUpdate (currently framerate-dependent on physics state).
- Wall-pin / wrong-way detection → faster respawn (today: 4 s under 1 m/s only).

### P3 — Drivetrain / CarSpec

- Extract CarSpec from hardcoded FD (280 PS / 1280 kg / FR / 6-speed): mass, torque scale,
  gears, final drive, mu, drag, inertia dims (currently a literal that disagrees with the
  collider 4.28×1.76×1.23 vs 4.1×1.72×1.0). Built from catalog hp/kg/drv; upgrades modify
  the spec (power→torque, weight→mass, brakes→brakeForce, susp→spring/damper/ARB,
  tires→mu).
- Fix rear force split: driveForce*0.5 per wheel loses half the output if one rear is
  airborne → redistribute to the loaded wheel, clamp by its circle. Same in brake split.
- Drive layouts: add FF (front drive, injector off, lift-off rotation) and 4WD (fixed
  split, reduced injector). Until then catalog is FR/MR-gated.
- topSpeedMps 64.75 is only a normalizer (real vmax ~80 m/s from drag). Derive drag from
  per-car spec'd top speed (recommended) or rename. Delete stale staticWheelLoad
  initializer.
- Parking brake below 0.3 m/s (matters when a track has gradient).

### P4 — Lateral & drift polish

- Downforce 0.35 → try 1.0-1.2 (handling notes' next knob); per-spec after P3.
- Fix one-tick-stale Drifting (UpdateDriftState runs after TireForces) — slip pre-pass or
  reorder.
- DriftSeconds accumulation (shared with L2) → optional drift-score line on results.
- Promote ~15 unnamed inline constants in the yaw/drift layer (injector 2.0/1.5/0.20,
  damping 1.8/2.2/4.0/0.45, assist ×15, gates .05/.35 …) into the named const block
  before the next tuning session.
- Deliberately keep: inferred wheelspin (no wheel rotational state), flat brake model,
  CG-applied stabilizer — arcade-correct for the NFS target.

### Suggested order

P1 → L1 → L2 → (P3 + L3 together) → P2 → L4 → P4/L5 ongoing.
Everything through P4 is shipped. **L5 is the only phase not started** — housing
ladder, bank loans, gym, hidden faults + inspection, upgrades 5x5, and the
insurance multiplier that `atFaultIncidents` is already feeding. L5 was the next
thing in progress when the phone playtest came back; the four device bugs above
took priority and L5 has not been begun.
