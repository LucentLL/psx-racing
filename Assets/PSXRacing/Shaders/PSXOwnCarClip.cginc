#ifndef PSX_OWN_CAR_CLIP_INCLUDED
#define PSX_OWN_CAR_CLIP_INCLUDED

// A CAR'S OWN SPRAY AND SMOKE STAY OUT OF IT (owner, 2026-10-19: "rain splash
// goes through car, cockpit, rear bumper, etc. Smoke does, too"). A puff is
// a camera-facing card a metre or two across, centred near a wheel; the part
// of it that pokes INSIDE the car is nearer the eye than the car's far walls
// - from the seat, nearer than the dash and the door cards - so it was drawn
// in the cabin, through the windows and round the bumper. The emitter hands
// its renderer the car's body box (OwnCarClip.cs, a property block):
// _PSXOwnW2L takes a world point into the car's frame, _PSXOwnBoxC is the
// box's centre there (w 1 = on), _PSXOwnBoxH its half size. A fragment inside
// it is not drawn. Off (w 0) on everything else that uses the shader.
float4x4 _PSXOwnW2L;
float4 _PSXOwnBoxC;
float4 _PSXOwnBoxH;

// Fix pass 1 (owner 11-14: "a square appears around the car when smoking tires"): the clip is
// for an eye INSIDE the box (the cockpit, a hood or bumper eye). From outside, the body's own
// depth already hides what is behind it, and the box (wider than the body, as tall as the roof
// over the bonnet and the boot) only cut a hard-edged box out of the cloud round the car - lit
// by the car's own lamps at night. _PSXOwnBoxH.w 1 (PSX_SMOKE_BOXFIX=0): every eye, as before.
inline void PSXOwnCarClip(float3 wpos)
{
    if (_PSXOwnBoxC.w > 0.5)
    {
        float3 e = abs(mul(_PSXOwnW2L, float4(_WorldSpaceCameraPos, 1.0)).xyz - _PSXOwnBoxC.xyz);
        if (_PSXOwnBoxH.w > 0.5 || (e.x < _PSXOwnBoxH.x && e.y < _PSXOwnBoxH.y && e.z < _PSXOwnBoxH.z))
        {
            float3 p = abs(mul(_PSXOwnW2L, float4(wpos, 1.0)).xyz - _PSXOwnBoxC.xyz);
            if (p.x < _PSXOwnBoxH.x && p.y < _PSXOwnBoxH.y && p.z < _PSXOwnBoxH.z) discard;
        }
    }
}

#endif
