#ifndef PSX_CABIN_CLIP_INCLUDED
#define PSX_CABIN_CLIP_INCLUDED

// THE PLAYER'S OWN SHELL, CUT AT ITS WINDSCREEN (CabinClip.cs, 2026-10-16).
// While a camera sits inside the player's car (the cockpit, and the mirror
// looking back from it) the shell is drawn only AHEAD of the windscreen's
// base: the bonnet and the wings show through the cabin's glass, as a
// driver sees them, and nothing of the body lands inside the cabin.
//
// _PSXCabinOwn is per RENDERER (a property block on the player's shell
// only), so a rival alongside is never cut, however close it rubs.
// _PSXCabinOn is the global gate, open only for the camera inside.
// _PSXCabinW2L takes a world point into the car's frame; _PSXCabinBox is
// (cut z, half width, top, tail z) in that frame.
float _PSXCabinOwn;
float _PSXCabinOn;
float4x4 _PSXCabinW2L;
float4 _PSXCabinBox;

inline void PSXCabinClip(float3 wpos)
{
    if (_PSXCabinOn * _PSXCabinOwn > 0.5)
    {
        float3 p = mul(_PSXCabinW2L, float4(wpos, 1.0)).xyz;
        if (p.z < _PSXCabinBox.x && p.z > _PSXCabinBox.w && abs(p.x) < _PSXCabinBox.y && p.y < _PSXCabinBox.z)
            discard;
    }
}

#endif
