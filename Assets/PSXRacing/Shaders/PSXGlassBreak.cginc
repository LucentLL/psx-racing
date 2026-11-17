// THE BREAKS GLASS MAKES, shared (damage round 3, 2026-11-17): the hashes, the noises, a crack line that
// never goes thinner than a pixel, the rays round a strike, the pieces of a broken sheet and a CRAZED
// tempered pane. Moved out of PSXCarGlass.cginc (round 2) unchanged, so the cockpit's own glass
// (PSX/CabinGlass) breaks with the same pattern as the shell's.
#ifndef PSX_GLASS_BREAK_INCLUDED
#define PSX_GLASS_BREAK_INCLUDED

float GlsH1(float n) { return frac(sin(n * 91.3458) * 47453.5453); }

float GlsH2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

float GlsN1(float x)
{
    float i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(GlsH1(i), GlsH1(i + 1.0), f);
}

float GlsN2(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(GlsH2(i), GlsH2(i + float2(1, 0)), f.x), lerp(GlsH2(i + float2(0, 1)), GlsH2(i + float2(1, 1)), f.x), f.y);
}

// A crack d metres off, w metres wide, drawn with px metres to the pixel: never thinner than a pixel
// (it would shimmer and vanish at the chase camera), fainter instead.
float GlsLine(float d, float w, float px)
{
    float hw = max(w, px * 1.1) * 0.5;
    return (1.0 - smoothstep(hw, hw + px * 0.75, abs(d))) * saturate(0.45 + 0.55 * w / max(px, 1e-5));
}

// The one ray of the sector (of n round the strike) that angle a falls in: the angle off it; kh = the sector.
float GlsRay(float a, float n, float seed, out float kh)
{
    float D = 6.2831853 / n;
    float k = floor(a / D);
    kh = k - n * floor(k / n);
    float j = GlsH1(kh * 7.31 + seed * 53.0);
    return a - (k + 0.5 + (j - 0.5) * 0.55) * D;
}

// The nearest piece of a broken sheet: x = how far its edge is (0 on a crack, in cells), y = which piece.
float2 GlsCells(float2 p)
{
    float2 ip = floor(p), fp = frac(p);
    float f1 = 8.0, f2 = 8.0, id = 0.0;
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            float2 g = float2(x, y);
            float2 o = float2(GlsH2(ip + g), GlsH2(ip + g + 17.3));
            float2 d = g + o - fp;
            float dd = dot(d, d);
            if (dd < f1) { f2 = f1; f1 = dd; id = GlsH2(ip + g + 5.1); }
            else if (dd < f2) f2 = dd;
        }
    return float2(sqrt(f2) - sqrt(f1), id);
}

// A CRAZED TEMPERED PANE: white cubes (resolved close up, their mean at a distance), the coarser
// cracks between the pieces, the runs out of the strike, powder and a small hole where it was struck.
float3 GlsCrazed(float2 q, float seed, float px)
{
    float r = length(q);
    float2 c1 = GlsCells(q * 95.0 + seed * 7.0);
    float2 c2 = GlsCells(q * 30.0 + seed * 3.0);
    float fine = 1.0 - smoothstep(0.0025, 0.007, px);
    float v = lerp(0.70, lerp(0.58, 0.88, c1.y) * smoothstep(0.0, 0.14, c1.x) + 0.10, fine);
    v *= lerp(1.0, 0.66, GlsLine(c2.x / 60.0, 0.003, px));
    v *= lerp(0.92, 1.06, c2.y);
    float kh;
    float da = GlsRay(atan2(q.y, q.x) + seed * 6.2831853, 19.0, seed, kh);
    v *= lerp(1.0, 0.55, GlsLine(r * da, 0.0036, px) * step(0.03, r) * step(r, 0.22 + 0.5 * GlsH1(kh + seed)));
    v = lerp(v, 0.93, 1.0 - smoothstep(0.02, 0.075, r));
    v = lerp(v, 0.04, 1.0 - smoothstep(0.008, 0.02 + px, r));
    return float3(0.93, 1.0, 0.975) * v;
}

#endif
