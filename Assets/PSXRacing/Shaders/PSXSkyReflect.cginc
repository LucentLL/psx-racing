// THE HOUR'S SKY, AS A SURFACE SEES IT IN A MIRROR.
//
// This is the car paint's sky lookup (it was SkyIn() inside PSXCarPaint.shader
// from the third cut on), moved here VERBATIM on 2026-09-21 so that a second
// surface can reflect the same sky: the WET ROAD in PSX/Lit. The owner's ask
// for the night pass was Need for Speed (2015) - "how much the skybox effects
// the color and mood of the world and cars" - and in those frames the road is
// the biggest mirror on screen: a blue dusk sky lies on the tarmac, and in the
// city the sodium skyglow does. One function for both means the paint and the
// puddle it drives through can never disagree about what the sky looks like.
//
// VERBATIM means it. Not one operation of the body below changed in the move:
// the car paint's pixels are the owner's signed-off look (four cuts of it),
// and PSXCarPaint.shader now calls this through a one-line SkyIn() wrapper, so
// the compiled paint is the same arithmetic in the same order.
//
// What it does, in order: the hour's gradient (horizon to top, sharpened like
// PSXSky's), the equirect panorama through the same rotation and tint the sky
// material uses (TimeOfDay.ApplySky pushes the _PSXSky* globals), or - when a
// scene has no panorama - a hemisphere made from the ambient and sun that ARE
// set; then the haze toward the horizon; then THE LAND band (hillside, trees,
// walls: dark, fog-coloured, a crisp top at LAND_TOP); then the ground below
// the horizon.
//
// THE LAND BAND IS THE INCLUDER'S TO TUNE, and it has to be. The paint wants
// land up to ten degrees (LAND_TOP 0.17): a flank's grazing reflection is the
// dark roadside, which is what cured the white rim-light glow. A ROAD seen from
// a chase camera reflects at R.y of 0.03 to 0.15 - entirely inside that band -
// so with the paint's numbers every wet road in the game would mirror fog x 0.4
// instead of the sky, which is the opposite of the reference. Hence the four
// constants below are #ifndef-guarded: PSXCarPaint.shader defines its own (and
// must keep them in ITS file - LifeSimSelfTest reads them there with a regex),
// PSX/Lit defines a band that stops at the horizon. An includer that defines
// nothing gets the paint's values.
//
// UNIFORM RULES (a uniform declared twice is a compile error, and a global
// that appears in a Properties block reads the material's 0 instead):
//   * The _PSXSky* globals are declared HERE and nowhere else. An includer
//     must not redeclare them.
//   * _PSXFogColor, _PSXAmbient and _PSXLightColor are NOT declared here: every
//     PSX surface shader already declares them in its own uniform block, so
//     this file must be included AFTER that block (Lit and LitTransparent:
//     after the _PSXSnap line; CarPaint: where its _PSXSky* block used to be).
//   * UnityCG.cginc must come first (UNITY_PI).
//
// The panorama is read with tex2Dlod at the caller's blur: no derivatives, so
// it is safe inside any branch, on any GLES driver, and the equirect seam that
// PSXSky.shader has to fight with tex2Dgrad cannot happen here.
#ifndef PSX_SKY_REFLECT_INCLUDED
#define PSX_SKY_REFLECT_INCLUDED

// The car paint's world-in-the-lacquer band: sky above the land, land up to
// LAND_TOP (the sine of ten degrees), a dark road below the horizon. Defaults
// only - see the header for who overrides them and why.
#ifndef LAND_TOP
#define LAND_TOP           0.17
#endif
#ifndef LAND_SOFT
#define LAND_SOFT          0.045
#endif
#ifndef LAND_REFLECT
#define LAND_REFLECT       0.40   // how bright the land is in the reflection (x fog colour)
#endif
#ifndef GROUND_REFLECT
#define GROUND_REFLECT     0.25   // how bright the ground is in the reflection (x fog colour)
#endif

// The hour's sky, as TimeOfDay.ApplySky hands it to the sky
// material - see PSXSky.shader for what each one means there.
sampler2D _PSXSkyTex;
float _PSXSkyAmount;    // 0 = no panorama this scene
float _PSXSkyRotation;  // degrees
float _PSXSkyTint;      // 0..1, how hard the photo wears the hour
float _PSXSkyExposure;
fixed4 _PSXSkyTop;
fixed4 _PSXSkyHorizon;
float _PSXSkySharpness;

// THE DARK WHERE NOTHING IS LIT (NightDark.cs, 2026-10-13): 0 lit / not night
// .. 1 a fully unlit night. The sky (and what the paint reflects of it) keeps
// PSX_SKY_DARK_KEEP of itself at 1 - the same share NightDark.FogKeep leaves
// the fog colour, because the horizon and the fog are one pair. Declared in a
// guarded block in both PSXTone.cginc and PSXSkyReflect.cginc (either may be
// included first). At 0 the keep is exactly 1: day frames are bit-identical.
#ifndef PSX_NIGHTDARK_DECLARED
#define PSX_NIGHTDARK_DECLARED
float _PSXNightDark;
#define PSX_SKY_DARK_KEEP 0.30
inline float PSXNightSkyKeep()
{
    return 1.0 - (1.0 - PSX_SKY_DARK_KEEP) * saturate(_PSXNightDark);
}
#endif

/// What the sky looks like in direction R - the same lookup and
/// the same hour tint PSXSky.shader applies, at a blur picked by
/// the caller (paint is a soft reflection, glass a sharper one).
float3 PSXSkyIn(float3 R, float lod)
{
    float y = R.y;
    float above = pow(saturate(y), 1.0 / max(_PSXSkySharpness, 0.5) * 4.0);
    float3 grad = lerp(_PSXSkyHorizon.rgb, _PSXSkyTop.rgb, above);
    float3 col;
    if (_PSXSkyAmount > 0.001)
    {
        float u = atan2(R.z, R.x) * (0.5 / UNITY_PI) + 0.5 + _PSXSkyRotation / 360.0;
        float v = 0.5 + asin(clamp(y, -1.0, 1.0)) / UNITY_PI;
        // Explicit LOD: no derivatives, so the equirect seam that
        // PSXSky has to fight with tex2Dgrad cannot happen here.
        float3 pano = tex2Dlod(_PSXSkyTex, float4(u, v, 0, lod)).rgb * _PSXSkyExposure;
        float3 tinted = pano * grad * 2.0;
        col = lerp(pano, tinted, _PSXSkyTint);
    }
    else
    {
        // No panorama: a hemisphere from the lighting that IS set.
        col = lerp(_PSXAmbient.rgb * 1.2, saturate(_PSXAmbient.rgb + _PSXLightColor.rgb * 0.5),
                   saturate(y * 1.5 + 0.3));
    }
    // An unlit night's sky is near black (NightDark; the fog colour
    // below is already scaled by the same share in C#).
    col *= PSXNightSkyKeep();
    // Haze toward the horizon, the way the sky's own horizon
    // fades into the fog colour...
    float hz = saturate(1.0 - abs(y) * 6.0);
    col = lerp(col, _PSXFogColor.rgb, hz * hz * 0.6);
    // ...then THE LAND. A road is not an ocean: from the horizon
    // up to about ten degrees, what a panel mirrors is hillside,
    // trees and walls, not sky - dark, in the hour's haze colour,
    // with a crisp top. This is what keeps a flank's grazing
    // reflection from being a white rim, and what draws the
    // bright line along the shoulder of the body above it.
    float3 land = _PSXFogColor.rgb * LAND_REFLECT;
    col = lerp(land, col, smoothstep(LAND_TOP - LAND_SOFT, LAND_TOP + LAND_SOFT, y));
    // ...and below the horizon the reflection is the road: DARK,
    // the way the reference games' environment maps were.
    col = lerp(col, _PSXFogColor.rgb * GROUND_REFLECT, saturate(-y * 4.0));
    return col;
}

// THE WORLD AROUND THE CAR (2026-10-12, CarReflections + Hidden/PSX/EnvPano):
// six views from the watched car's roof stitched into an equirect laid out
// like the sky panorama above (no rotation: it is world-aligned). Only read
// while _PSXEnvOn is 1; the sky lookup stays the fallback.
//
// The caller's lod is in mips OF A 512-WIDE PANORAMA (the size it was built
// at). _PSXEnvLodBias = log2(real width / 512), pushed by CarReflections
// (+1 at desktop's 1024 since 2026-10-15, -1 at phones' 256), so one number is
// one blur in degrees at every size and the wet road's mirror did not change
// when the panorama doubled. Unset (0) = the old arithmetic exactly.
sampler2D _PSXEnvPano;
float _PSXEnvOn;
float _PSXEnvLodBias;
float3 PSXEnvIn(float3 R, float lod)
{
    float u = atan2(R.z, R.x) * (0.5 / UNITY_PI) + 0.5;
    float v = 0.5 + asin(clamp(R.y, -1.0, 1.0)) / UNITY_PI;
    return tex2Dlod(_PSXEnvPano, float4(u, v, 0, max(lod + _PSXEnvLodBias, 0.0))).rgb;
}

// THE WORLD IS NOT INFINITELY FAR (2026-10-23; the owner: the reflections
// slide "in the blue arrow direction" - sideways across the roof - "when they
// should move in the red arrow direction", down the flank). Read by R alone,
// the panorama is at infinity: a panel shows one direction, and a house going
// past can only sweep across the body by angle. CarReflections measures how
// far the nearest solid thing is round the eye in eight headings
// (_PSXEnvRad0/1, metres, k x 45 degrees of atan2(z, x)) and where the eye is
// (_PSXEnvEye.xyz, .w its height over the road); this traces the pixel's own
// reflected ray from where the pixel IS to that cylinder (or to the road
// first) and returns the direction of the hit as the eye sees it - so the
// world flows past along a door at road speed, as in a mirror - and keeps
// flowing between refreshes, because the pixel's offset from the set's eye
// grows as the car moves on. Pixels more than a few metres from the eye (the
// other cars, which borrow the player's panorama) keep R. Unset
// (_PSXEnvEye.w 0): R exactly.
float4 _PSXEnvEye;
float4 _PSXEnvRad0, _PSXEnvRad1;
float PSXEnvRadius(float2 dxz)
{
    // heading in eighths of a turn, then a hat weight per measured heading
    float s = atan2(dxz.y, dxz.x) * (4.0 / UNITY_PI);
    float4 t0 = s - float4(0.0, 1.0, 2.0, 3.0), t1 = s - float4(4.0, 5.0, 6.0, 7.0);
    t0 -= 8.0 * round(t0 * 0.125);
    t1 -= 8.0 * round(t1 * 0.125);
    return dot(saturate(1.0 - abs(t0)), _PSXEnvRad0) + dot(saturate(1.0 - abs(t1)), _PSXEnvRad1);
}
float3 PSXEnvRay(float3 wpos, float3 R)
{
    float3 o = wpos - _PSXEnvEye.xyz;
    // (6 m: the player's nose, a set of faces after its eye at speed)
    float near = (1.0 - smoothstep(6.0, 10.0, length(o))) * step(0.001, _PSXEnvEye.w);
    // |o.xz + t R.xz| = r, the far root (the pixel is inside the cylinder)
    float a = max(dot(R.xz, R.xz), 1e-6);
    float b = dot(o.xz, R.xz);
    float c0 = dot(o.xz, o.xz);
    float r = PSXEnvRadius(R.xz);
    // (a pixel at or past the wall - a long car by a near wall - keeps R)
    near *= saturate((r * r - c0) / (0.25 * r * r));
    float t = (-b + sqrt(max(b * b - a * (c0 - r * r), 0.0))) / a;
    // once more at the heading the ray actually lands on
    r = PSXEnvRadius(o.xz + R.xz * t);
    t = (-b + sqrt(max(b * b - a * (c0 - r * r), 0.0))) / a;
    // ...or the road, if it gets there first
    if (R.y < -1e-4) t = min(t, max((-_PSXEnvEye.w - o.y) / R.y, 0.0));
    return normalize(lerp(R, normalize(o + R * t), near));
}

// HOW MUCH OF THE PANORAMA ONE PIXEL'S MIRROR COVERS (2026-10-23; the owner:
// car windows "flicker reflections inaccurately while driving"). PSXEnvIn
// reads at the caller's fixed blur, which knows nothing of the screen: on a
// curved window, a far car or a phone's 240 lines one pixel's mirror spans
// many panorama texels, the read picks one of them, and the picture sparkles
// as the car moves. This is the angle R turns through across one pixel (R is
// unit, so |ddx(R)| is in radians), in mips of the 512-wide panorama the
// callers' blurs are written in (512 / 2pi texels a radian). A caller reads
// at max(its blur, this). Derivatives: call it in uniform control flow,
// before any branch.
float PSXEnvPixelLod(float3 R)
{
    float3 dx = ddx(R), dy = ddy(R);
    float a = sqrt(max(dot(dx, dx), dot(dy, dy)));
    return log2(max(a * (512.0 / (2.0 * UNITY_PI)), 1e-5));
}
#endif
