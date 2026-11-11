// THE ARCHES FIT THE SHEET'S TYRES (Scripts/ArchFlare.cs says why).
// A borrowed shell on a row whose real tyre is bigger than the shell's own
// has the bodywork over that wheel raised by the shortfall: a flared opening,
// the body itself at its fitted height. Per renderer, in the body MESH frame:
//   _PSXArchHub   (hub y, front axle z, rear axle z, tyre radius)
//   _PSXArchFlare (front raise, rear raise, arch zone half-length, on)
//   _PSXArchX     (front tyre inner face |x|, rear tyre inner face |x|)
// Zero raise (every car whose arches fit) leaves every vertex where it was.
#ifndef PSX_ARCH_FLARE_INCLUDED
#define PSX_ARCH_FLARE_INCLUDED

float4 _PSXArchHub;
float4 _PSXArchFlare;
float4 _PSXArchX;

float3 PSXArchFlare(float3 p)
{
    if (_PSXArchFlare.w < 0.5) return p;
    float R = _PSXArchHub.w, Z = _PSXArchFlare.z;
    float hy = p.y - _PSXArchHub.x;
    // From the hub up over the crown; faded out before the bonnet / waist line.
    float wy = smoothstep(0.0, 0.6 * R, hy) * (1.0 - smoothstep(R + 0.12, R + 0.32, hy));
    float ax = abs(p.x);
    float wF = (1.0 - smoothstep(0.8 * Z, Z + 0.15, abs(p.z - _PSXArchHub.y))) * smoothstep(_PSXArchX.x - 0.10, _PSXArchX.x, ax);
    float wR = (1.0 - smoothstep(0.8 * Z, Z + 0.15, abs(p.z - _PSXArchHub.z))) * smoothstep(_PSXArchX.y - 0.10, _PSXArchX.y, ax);
    p.y += wy * (_PSXArchFlare.x * wF + _PSXArchFlare.y * wR);
    return p;
}

#endif
