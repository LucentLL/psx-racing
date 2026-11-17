// THE WATER'S OWN NORMAL for a wet surface's glints (RAIN 2, 2026-11-16).
//
// A street lamp's streak down a wet road and a low beam's are Blinn-Phong
// highlights off the road's normal (PSXLamps.cginc, PSXHeadlights.cginc). A
// raindrop's ring tilts the WATER, not the road: the lamps' diffuse light must
// go on reading the road's normal while their glints read the water's. The two
// tables are walked once for both, so the tilt rides here: a per-fragment
// variable the walk adds to the normal for the glint only.
//
// Zero unless PSXRain2At (PSXRain2.cginc) wrote it this fragment; a shader that
// never writes it compiles the add away, and x + 0 is x, so every picture
// without the rings is unchanged to the bit.
#ifndef PSX_SPEC_BEND_INCLUDED
#define PSX_SPEC_BEND_INCLUDED
static float3 _PSXSpecBend = float3(0.0, 0.0, 0.0);
#endif
