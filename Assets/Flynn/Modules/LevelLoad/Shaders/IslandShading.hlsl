// Shared surface shading for the floating-island top fill and its grass
// fringe — one source for the detail layer, procedural variation and the
// painterly light banding, so the two materials read as one surface.
// IslandSkirt syncs the uniforms from the fill material to the fringe
// material every regen.
#ifndef FLYNN_ISLAND_SHADING_INCLUDED
#define FLYNN_ISLAND_SHADING_INCLUDED

TEXTURE2D(_DetailTex);
SAMPLER(sampler_DetailTex);
TEXTURE2D(_FalloffTex);
SAMPLER(sampler_FalloffTex);
float4 _FalloffRect; // xy = local min, zw = local size
float _EdgeDarken;
float _FalloffPower;
half4 _EdgeTint;
float _DetailScale;
float _DetailStrength;
float _DetailAspect;
float _DetailAntiTile;
float _VarStrength;
float _VarScale;
half4 _VarTintA;
half4 _VarTintB;
float _BandedLight;
float _LightBands;
float _BandSoftness;
float _BandWobble;
float _BandWobbleScale;
half4 _ShadowTint;

// Cheap hash/value noise for the procedural passes — no texture fetch,
// stable in world space.
float Hash21(float2 p)
{
    p = frac(p * float2(127.1, 311.7));
    p += dot(p, p + 34.5);
    return frac(p.x * p.y);
}

float ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash21(i);
    float b = Hash21(i + float2(1, 0));
    float c = Hash21(i + float2(0, 1));
    float d = Hash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Posterize a light level into hand-painted bands. Border position
// wobbles with the detail texture so band edges read brushy, not CG.
float BandLightLevel(float lum, float2 worldXY)
{
    float wob = (SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex,
        worldXY / max(_BandWobbleScale, 0.001)).g - 0.5) * _BandWobble;
    float scaled = saturate(lum + wob) * _LightBands;
    float cell = floor(scaled);
    float soft = smoothstep(0.5 - _BandSoftness, 0.5 + _BandSoftness, scaled - cell);
    return saturate((cell + soft) / max(_LightBands, 1.0));
}

// Edge falloff darken. f = 1 interior, 0 at the boundary; the baked mask
// lives in the SpriteShape controller's local space, so sample with the
// surface's object-space position. Fringe geometry outside the border
// reads f = 0 (full dip) — blade tips continue the fill's edge shade.
half3 ApplyEdgeFalloff(half3 rgb, float2 positionOS)
{
    float2 uvF = (positionOS - _FalloffRect.xy) / max(_FalloffRect.zw, 0.0001);
    float f = SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex, uvF).r;
    f = pow(saturate(f), _FalloffPower);
    half3 edge = rgb * _EdgeTint.rgb * (1.0 - _EdgeDarken);
    return lerp(edge, rgb, f);
}

// World-tiled detail layer. Neutral when no texture is assigned.
half3 ApplyDetailWorld(half3 rgb, float2 world)
{
    float2 uvD = world / max(_DetailScale, 0.001);
    // iso foreshortening: the screen's Y is the ground plane receding
    // at an angle, so a ground tile appears squashed vertically
    uvD.y /= max(_DetailAspect, 0.01);
    half4 detail = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, uvD);
    // Anti-tiling: second sample rotated 90° and rescaled so its repeat
    // never aligns with the first; low-frequency noise picks which sample
    // dominates per region, hiding the grid without flattening contrast
    // the way a fixed 50/50 blend would.
    if (_DetailAntiTile > 0.001)
    {
        float2 uv2 = float2(-uvD.y, uvD.x) * 0.7317 + 41.7;
        half4 d2 = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, uv2);
        float w = ValueNoise(world / max(_DetailScale * 2.9, 0.001));
        detail = lerp(detail, d2, saturate(w * _DetailAntiTile * 1.4));
    }
    return rgb * lerp(half3(1.0, 1.0, 1.0), detail.rgb, _DetailStrength);
}

// Procedural macro variation: two-octave value noise in world space
// (iso-squashed like the detail layer) drifts the surface between two
// grass tones, breaking the visible detail-texture repeat.
half3 ApplyVariation(half3 rgb, float2 world)
{
    if (_VarStrength <= 0.001) return rgb;
    float2 p = world / max(_VarScale, 0.001);
    p.y /= max(_DetailAspect, 0.01);
    float n = ValueNoise(p) * 0.65 + ValueNoise(p * 2.63 + 17.17) * 0.35;
    half3 tint = lerp(_VarTintA.rgb, _VarTintB.rgb, n);
    return rgb * lerp(half3(1.0, 1.0, 1.0), tint, _VarStrength);
}

#endif // FLYNN_ISLAND_SHADING_INCLUDED
