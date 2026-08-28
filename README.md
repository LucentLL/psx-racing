# PSX Racing

A PlayStation-era arcade racer built in Unity 6, driven in a Mazda RX-7 Type RS
(FD) around a generated 1.2 km city circuit at sunset.

**▶ Play: https://lucentll.github.io/psx-racing/**

Works on desktop and phones. On mobile the on-screen controls appear on first
touch; tap START to unlock fullscreen and audio.

## Controls

| | Keyboard | Gamepad | Touch |
|---|---|---|---|
| Steer | A / D or arrows | Left stick | Drag the left pad |
| Throttle / Brake | W / S | Triggers | GAS / BRAKE |
| Handbrake | Space | B | E-BRAKE |
| Shift (manual) | Q / E | Shoulders | — (auto) |
| Camera | C | Y | CAM |
| Respawn | R | Select | RESET |
| Pause menu | Esc | Start | MENU |

## The look

Rendered into a 320×240 point-filtered buffer and upscaled, with per-vertex
lighting, vertex snapping, a 4×4 Bayer dither at 5 bits per channel, and 256 px
textures — the PS1's own texture-page ceiling.

Affine texture mapping is per-material rather than global. The car, buildings
and props keep it, because that warp is the look; the road, kerbs, walls and
ground opt out, because affine error scales with triangle size and on a 12 m
road quad it visibly bends the painted centreline.

## The physics

Rigidbody with four raycast suspension corners and a per-wheel friction circle.
The tire curve and the drift layer are ported from an earlier project of mine
(Racing Game 2), then retuned toward Need for Speed Underground / Most Wanted:

- e-brake collapses rear grip to 30 % over a 0.75 s drain, with a press-edge yaw
  kick, so a slide is a deliberate gesture rather than a loss of control
- a wheelspin yaw injector makes throttle rotate the car, faded in with speed
- four-tier yaw damping — a committed slide feels weightless, a released wheel
  straightens cleanly
- a lateral velocity stabilizer at the CG, which is what makes an arcade car feel
  bolted to the road, relaxed while drifting
- gear-scaled engine braking on the rear axle only, so lift-off rotates the car

## The cars

317 cars from the GT4-derived catalog, wearing sixteen body shells: a Skyline
R32, a Supra A80, the FD, a '66 GTO, a '67 Mustang fastback, a '69 Charger and
its winged Daytona, a Pagoda SL, an E30, a Volvo estate, a CX, a supermini, a
Defender pickup, a panel van and a compact truck.

Sixteen shells against 317 cars means most of it is curation. The cars the pack
actually modelled get their own shell, along with their badge-engineered twins —
a Superbird IS a winged Charger. Everything else is scored on body style first,
then continent, era and kerb weight. The shell brings its real geometry with it:
a Charger races on a 2.99 m wheelbase and a supermini on 2.22 m.

`Docs/car_model_mapping.txt` lists every assignment.

## The audio

A sample-based rotary voice: eight RPM band loops on a *geometric* ladder
(`home = idle × (limiter/idle)^frac`), with separate on- and off-throttle takes
crossfaded by pedal position. The ladder has to be geometric because playback
rate is a ratio — a linear one pushes the bottom rungs past the pitch clamp and
you hear two engines at once.

Also: sequential-turbo spool and blow-off driven by a boost proxy, induction
noise as a continuous layer, tire squeal bound to grip *utilisation* so the
tires complain before the slide, and a master biquad tone chain.

## Building

The entire scene is generated — track spline, road, kerbs, walls, scenery, cars,
HUD and race logic — by an editor script. There is no hand-authored scene to
merge.

```
Unity: PSX Racing > Build Scene
```

To rebuild and publish the WebGL build:

```
powershell -ExecutionPolicy Bypass -File tools\build-and-publish.ps1
```

`Assets/PSXRacing/Scenes/CityCircuit.unity` is generated output — edit
`Editor/PSXRacingBuilder.cs`, not the scene.

## Credits

Art: PSX Vehicles by boggle (15 cars), a free compact pickup, plus buildings,
gas station and road assets. Audio: Realistic
Engine Sound and Turbo Sound Pack (Rotary_x8_free set).
