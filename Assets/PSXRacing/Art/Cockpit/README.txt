COCKPIT ART
===========

Drop two PNGs in this folder. The scene builder picks them up by NAME, wires
them onto the cockpit view and sets their import settings; nothing else needs
touching. Re-run the scene build after adding or changing either one.

    cabin.png     the cabin: roof, A-pillars, dash, door tops, mirror --
                  everything except the steering wheel. The windscreen must be
                  fully TRANSPARENT (alpha 0): that hole is what you drive
                  through. Any area you leave opaque is bodywork.

                  STRETCHED to fill the frame, aspect ignored. A cockpit
                  overlay that letterboxes shows the world where the door card
                  should be, and a gap is worse than a slightly wide dash. Draw
                  it 16:9 and it will be undistorted on a 16:9 display.

    wheel.png     the steering wheel on its own, transparent around and
                  through it, centred in a SQUARE canvas -- it is rotated about
                  the middle of the image, so a wheel that is off-centre in its
                  own PNG will wobble as it turns.

                  Drawn OVER the instruments, the way the rim of a real wheel
                  crosses the bottom of the binnacle. Position and size are
                  tunable on the CockpitView component (wheelFrac, wheelX,
                  wheelDrop) and the gauges follow the wheel, so moving it
                  moves them.

Both are optional. With no cabin.png the cockpit is the GENERIC 3D CABIN
(2026-10-13): a low-poly dash, pillars, mirrors and a steering wheel with
hands, built by tools/cockpit/generic_cockpit.py into Resources/Cockpit and
fitted to each car at runtime (Scripts/CockpitInterior.cs). A cabin.png here
switches that off and brings back the 2D overlay described above.

Any pixel size works. Sizes are fractions of the frame, so a 1280x720 sheet
and a 320x180 one land in the same place -- the small one is just softer.
