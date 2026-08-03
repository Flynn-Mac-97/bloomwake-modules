// Flynn/IslandTopFill — URP 2D-lit fill material for the island's top
// SpriteShape. Darkens the surface toward the shape boundary using a
// distance-to-edge mask baked by IslandSkirt (chamfer transform, delivered
// via MaterialPropertyBlock as _FalloffTex/_FalloffRect in controller-local
// space). Because the falloff lives in the FILL pass, SpriteShape edge
// sprites (feathered grass) draw on top of it untouched.
//
// Pass structure cloned from URP 14 Sprite-Lit-Default.

Shader "Flynn/IslandTopFill"
{
    Properties
    {
        _MainTex("Fill Texture", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}

        [Header(Edge Falloff)]
        _EdgeDarken("Edge Darken", Range(0, 1)) = 0.3
        _FalloffPower("Falloff Shape", Range(0.25, 4)) = 1.0
        _EdgeTint("Edge Tint", Color) = (1, 1, 1, 1)

        [Header(Edge Roll)]
        _RollAmount("Edge Roll (world units)", Range(0, 1)) = 0.15
        _RollPower("Edge Roll Shape", Range(0.5, 4)) = 1.5

        [Header(Detail)]
        _DetailTex("Detail (world tiled)", 2D) = "white" {}
        _DetailScale("Detail World Size", Float) = 3.0
        _DetailStrength("Detail Blend", Range(0, 1)) = 1.0
        _DetailAspect("Detail Iso Squash (Y/X)", Range(0.25, 1)) = 0.5

        [Header(Surface Variation)]
        _VarStrength("Variation Strength", Range(0, 1)) = 0.35
        _VarScale("Variation World Size", Float) = 7.0
        _VarTintA("Variation Tint A", Color) = (0.87, 0.93, 0.78, 1)
        _VarTintB("Variation Tint B", Color) = (1, 1, 1, 1)
        _DetailAntiTile("Detail Anti Tile", Range(0, 1)) = 0.6

        [Header(Iso Tile Grid)]
        _GridColor("Grid Color (A = opacity)", Color) = (0, 0, 0, 0)
        _GridThickness("Grid Line Thickness (world units)", Range(0.005, 0.3)) = 0.04
        _GridSoftness("Grid Line Softness", Range(0, 2)) = 0.5
        _GridCell("Grid Cell Size XY (world units)", Vector) = (1, 0.5, 0, 0)
        _GridOffset("Grid Offset (half cells XY)", Vector) = (0, 0, 0, 0)
        _GridEdgeFade("Grid Edge Fade (world units, capped by Falloff Width)", Range(0, 2)) = 0.4

        [Header(Painterly Light)]
        [Toggle] _BandedLight("Banded Light", Float) = 1
        _LightBands("Light Bands", Range(1, 6)) = 3
        _BandSoftness("Band Edge Softness", Range(0.005, 0.5)) = 0.08
        _BandWobble("Band Border Wobble", Range(0, 1)) = 0.25
        _BandWobbleScale("Wobble World Size", Float) = 2.0
        _ShadowTint("Shadow Tint", Color) = (0.55, 0.58, 0.78, 1)

        [Header(Paint Layers)]
        // Four brush-edged noise colour layers, authored in IslandVisualProfile
        // and pushed as shader arrays. Each: fbm noise -> thresholded region
        // whose EDGE is broken into brush strokes by a stamp from one of the two
        // sheets; paints the layer colour, stacked over the base grass. Only the
        // toggle + the two sheets live here; per-layer knobs are script-set.
        [Toggle] _PaintLayers("Paint Layers", Float) = 0
        _BrushTex1("Brush Sheet 1", 2D) = "white" {}
        _BrushTex2("Brush Sheet 2", 2D) = "white" {}

        // Baked per-island via MaterialPropertyBlock; declared for defaults.
        _FalloffTex("Falloff Mask (baked)", 2D) = "white" {}
        _FalloffRect("Falloff Rect (baked)", Vector) = (0, 0, 1, 1)
        _FalloffWorldWidth("Falloff World Width (baked)", Float) = 0.5

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        // Island-surface silhouette bit: VibeLayers shadow shaders (skew / blob /
        // cloud) test Stencil Equal 1 so shadows never float in the air past the
        // island edge or land on the skirt. The fill mesh IS the surface
        // silhouette, and it draws well before any shadow in sorting order.
        Stencil
        {
            Ref 1
            WriteMask 1
            Comp Always
            Pass Replace
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "IslandShading.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);
        half4 _MainTex_ST;
        float4 _Color;
        half4 _RendererColor;
        float _FalloffWorldWidth;
        float4 _FalloffTex_TexelSize;
        float _RollAmount;
        float _RollPower;
        half4 _GridColor;
        float _GridThickness;
        float _GridSoftness;
        float4 _GridCell;
        float4 _GridOffset;
        float _GridEdgeFade;

        TEXTURE2D(_BrushTex1);
        SAMPLER(sampler_BrushTex1);
        TEXTURE2D(_BrushTex2);
        SAMPLER(sampler_BrushTex2);
        float  _PaintLayers;
        float4 _LayerColor[4]; // rgb colour, a = opacity
        float4 _LayerNoise[4]; // x scale, y threshold, z edge softness, w offset
        float4 _LayerBrush[4]; // x sheet(0/1), y brush scale, z edge break, w -
        float4 _LayerRange[4]; // x min cell, y max cell, z grid cols, w grid rows

        // Texture-bombed brush field from a stamp sheet (white stroke on a
        // transparent ground → coverage = alpha). Per world cell picks a stamp
        // from the selected range + rotation +
        // jittered centre; a 2x2 neighbourhood is combined by max so stamps
        // scatter and overlap with no visible grid. Returns coverage 0..1.
        float BrushField(TEXTURE2D_PARAM(brushTex, brushSmp), float2 world, float scale, float2 grid, float2 range)
        {
            float2 uv = world / max(scale, 0.001);
            uv.y /= max(_DetailAspect, 0.01); // iso squash, match the surface
            float2 baseCell = floor(uv);
            float cols = max(floor(grid.x + 0.5), 1.0);
            float rows = max(floor(grid.y + 0.5), 1.0);
            float last = cols * rows - 1.0;
            float lo = clamp(floor(range.x + 0.5), 0.0, last);
            float hi = clamp(floor(range.y + 0.5), lo, last);
            float acc = 0.0;
            [unroll] for (int oy = 0; oy <= 1; oy++)
            {
                [unroll] for (int ox = 0; ox <= 1; ox++)
                {
                    float2 cell = baseCell + float2(ox, oy);
                    float2 jit = float2(Hash21(cell), Hash21(cell + 7.3)) - 0.5;
                    float2 center = cell + 0.5 + jit * 0.6;
                    float ang = Hash21(cell + 2.1) * 6.2831853;
                    float ca = cos(ang), sa = sin(ang);
                    float2 d = uv - center;
                    float2 local = float2(d.x * ca - d.y * sa, d.x * sa + d.y * ca) + 0.5;
                    if (local.x < 0.0 || local.x > 1.0 || local.y < 0.0 || local.y > 1.0) continue;
                    // Pick a stamp only from the selected cell range.
                    float idx = min(lo + floor(Hash21(cell + 3.7) * (hi - lo + 1.0)), hi);
                    float2 sub = float2(fmod(idx, cols), floor(idx / cols));
                    float2 stampUV = (sub + saturate(local)) / float2(cols, rows);
                    // White stroke on a transparent ground: coverage = alpha.
                    float v = SAMPLE_TEXTURE2D(brushTex, brushSmp, stampUV).a;
                    acc = max(acc, v);
                }
            }
            return saturate(acc);
        }

        // Per-channel blend of layer colour s over the accumulated base b.
        // mode: 0 Normal, 1 Multiply, 2 Screen, 3 Overlay, 4 Color Dodge,
        //       5 Soft Light, 6 Add.
        half3 BlendLayer(half3 b, half3 s, float mode)
        {
            if (mode < 0.5) return s;
            if (mode < 1.5) return b * s;
            if (mode < 2.5) return 1.0 - (1.0 - b) * (1.0 - s);
            if (mode < 3.5) return b < 0.5 ? (2.0 * b * s) : (1.0 - 2.0 * (1.0 - b) * (1.0 - s));
            if (mode < 4.5) return min(half3(1.0, 1.0, 1.0), b / max(1.0 - s, 1e-4));
            if (mode < 5.5) return (1.0 - 2.0 * s) * b * b + 2.0 * s * b;
            return min(half3(1.0, 1.0, 1.0), b + s);
        }

        // 3-octave fbm from the shared value noise, normalised ~0..1.
        float Fbm2(float2 p)
        {
            float f = 0.5   * ValueNoise(p);
            f      += 0.25  * ValueNoise(p * 2.03 + 11.3);
            f      += 0.125 * ValueNoise(p * 4.11 + 27.7);
            return f / 0.875;
        }

        // Four colour layers stacked over the base. TWO modes per layer,
        // selected by noiseScale (ns.x):
        //   ns.x > 0  — brush-edged noise REGION: fbm defines a blob, a brush
        //               stamp field breaks its EDGE into strokes. Solid interior.
        //   ns.x <= 0 — STAMP-AS-MASK: the blotch textures ARE the pattern. The
        //               procedural hash only SCATTERS them (BrushField); coverage
        //               = the union of stamp alphas, no fbm region. This is the
        //               grass-patch path — one sheet of shaped blotches placed by
        //               noise, not noise shaped by a sheet.
        half3 ApplyPaintLayers(half3 rgb, float2 world, float2 positionOS)
        {
            if (_PaintLayers < 0.5) return rgb;
            // Distance-to-edge (0 at rim, 1 interior) — stamp layers fade into the
            // lip with it so a blotch never hard-cuts on the material silhouette.
            float edgeDist = SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex,
                (positionOS - _FalloffRect.xy) / max(_FalloffRect.zw, 0.0001)).r;
            [unroll] for (int i = 0; i < 4; i++)
            {
                float4 col = _LayerColor[i];
                if (col.a <= 0.001) continue;
                float4 ns = _LayerNoise[i]; // scale, threshold, softness, offset
                float4 br = _LayerBrush[i]; // sheet, brushScale, edgeBreak, blend mode
                float4 rg = _LayerRange[i]; // min, max, cols, rows

                float b = br.x < 0.5
                    ? BrushField(TEXTURE2D_ARGS(_BrushTex1, sampler_BrushTex1), world, br.y, rg.zw, rg.xy)
                    : BrushField(TEXTURE2D_ARGS(_BrushTex2, sampler_BrushTex2), world, br.y, rg.zw, rg.xy);

                float cov;
                if (ns.x <= 0.0)
                {
                    // Stamp coverage IS the layer — the blotch silhouette is the
                    // grassy edge, so keep it CRISP: cut at threshold with a band
                    // no wider than one pixel (fwidth AA) unless edgeSoftness (ns.z)
                    // deliberately widens it. threshold (ns.y) trims faint fringes;
                    // the patch dissolves before the rim (edgeDist) so it tiles
                    // over the surface instead of getting sliced by the silhouette.
                    float thr = max(ns.y, 0.001);
                    float band = max(fwidth(b) * 0.75, ns.z);
                    cov = smoothstep(thr - band, thr + band, b) * col.a
                        * smoothstep(0.0, 0.10, edgeDist);
                }
                else
                {
                    float2 np = world / max(ns.x, 0.001);
                    np.y /= max(_DetailAspect, 0.01);
                    np += ns.w;
                    float n = Fbm2(np);
                    // Brush breaks the noise iso-line into strokes at the boundary.
                    float v = n + (b - 0.5) * br.z;
                    cov = smoothstep(ns.y - ns.z, ns.y + ns.z, v) * col.a;
                }
                rgb = lerp(rgb, BlendLayer(rgb, col.rgb, br.w), cov);
            }
            return rgb;
        }

        // Isometric tile-grid overlay: the two diagonal axes of the iso
        // diamond (s = x/w + y/h, t = y/h - x/w) cross tile boundaries at
        // half-integers, so distance-to-boundary is a frac() away. Sampled
        // in OBJECT space — the grid sticks to the island's tilemap, not
        // the world. fwidth AA keeps lines crisp at any zoom; thickness is
        // in world units perpendicular to the line.
        half3 ApplyIsoGrid(half3 rgb, float2 positionOS)
        {
            if (_GridColor.a <= 0.001) return rgb;
            float2 cell = max(_GridCell.xy, 0.0001);
            float g1 = positionOS.x / cell.x + positionOS.y / cell.y + 0.5 + _GridOffset.x * 0.5;
            float g2 = positionOS.y / cell.y - positionOS.x / cell.x + 0.5 + _GridOffset.y * 0.5;
            float d1 = abs(g1 - round(g1));
            float d2 = abs(g2 - round(g2));
            // axis-units per world-unit (|∇s| = |∇t|): converts thickness
            float grad = length(float2(1.0 / cell.x, 1.0 / cell.y));
            float halfTh = 0.5 * _GridThickness * grad;
            float aa1 = fwidth(g1) * (0.5 + _GridSoftness);
            float aa2 = fwidth(g2) * (0.5 + _GridSoftness);
            float line1 = 1.0 - smoothstep(halfTh - aa1, halfTh + aa1, d1);
            float line2 = 1.0 - smoothstep(halfTh - aa2, halfTh + aa2, d2);
            float lineMask = max(line1, line2);
            // Fade toward the island border (baked distance mask) so lines
            // dissolve into the lip instead of hard-cutting at the fringe.
            // Distance saturates at _FalloffWorldWidth — fades wider than
            // the falloff band just reach full strength at the band's edge.
            if (_GridEdgeFade > 0.001)
            {
                float2 uvF = (positionOS - _FalloffRect.xy) / max(_FalloffRect.zw, 0.0001);
                float dEdge = SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex, uvF).r
                            * max(_FalloffWorldWidth, 0.001);
                lineMask *= smoothstep(0.0, _GridEdgeFade, dEdge);
            }
            return lerp(rgb, _GridColor.rgb, lineMask * _GridColor.a);
        }

        // Falloff mask/rect decls + ApplyEdgeFalloff live in
        // IslandShading.hlsl (shared with Flynn/IslandFringe).

        // UV roll: shift texture samples outward along the mask gradient,
        // growing toward the lip — texture compresses at the edge like a
        // surface curving away. Returns the local-space sample offset.
        float2 RollDelta(float2 positionOS)
        {
            float2 uvF = (positionOS - _FalloffRect.xy) / max(_FalloffRect.zw, 0.0001);
            float f = saturate(SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex, uvF).r);
            if (_RollAmount <= 0.0001 || f >= 0.999) return float2(0, 0);

            // Mask gradient -> local-space inward direction.
            float2 e = _FalloffTex_TexelSize.xy;
            float fx = SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex, uvF + float2(e.x, 0)).r
                     - SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex, uvF - float2(e.x, 0)).r;
            float fy = SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex, uvF + float2(0, e.y)).r
                     - SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex, uvF - float2(0, e.y)).r;
            float2 gradLocal = float2(fx / max(_FalloffRect.z, 0.0001), fy / max(_FalloffRect.w, 0.0001));
            float glen = length(gradLocal);
            if (glen < 1e-5) return float2(0, 0);

            return (-gradLocal / glen) * _RollAmount * pow(1.0 - f, _RollPower);
        }

        // Local-space delta -> sprite-UV delta via the inverse Jacobian built
        // from screen-space derivatives (mapping-agnostic). Fragment-only.
        float2 ApplyDeltaToUV(float2 uv, float2 positionOS, float2 deltaPos)
        {
            float2 dUVdx = ddx(uv),        dUVdy = ddy(uv);
            float2 dPdx  = ddx(positionOS), dPdy = ddy(positionOS);
            float det = dPdx.x * dPdy.y - dPdy.x * dPdx.y;
            if (abs(det) < 1e-8) return uv;
            float sx = ( deltaPos.x * dPdy.y - deltaPos.y * dPdy.x) / det;
            float sy = (-deltaPos.x * dPdx.y + deltaPos.y * dPdx.x) / det;
            return uv + sx * dUVdx + sy * dUVdy;
        }

        // Detail layer following the edge roll so it wraps the lip with the
        // base (shared sampling lives in IslandShading.hlsl).
        half3 ApplyDetail(half3 rgb, float2 positionWS, float2 deltaPosLocal)
        {
            float2 dWorld = mul((float3x3)GetObjectToWorldMatrix(), float3(deltaPosLocal, 0)).xy;
            return ApplyDetailWorld(rgb, positionWS + dWorld);
        }

        // UnderMist no longer touches the top surface: the dissolve is drop-below-
        // contour on the skirt (see IslandSkirtSprite.shader) — the walkable top
        // stays crisp at any haze strength.
        ENDHLSL

        Pass // 2D lit (2D Renderer)
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex CombinedShapeLightVertex
            #pragma fragment CombinedShapeLightFragment

            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                half2  lightingUV : TEXCOORD1;
                float2 positionOS2: TEXCOORD2;
                float2 positionWS2: TEXCOORD3;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif

            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif

            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif

            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif

            Varyings CombinedShapeLightVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.positionOS2 = v.positionOS.xy;
                o.positionWS2 = TransformObjectToWorld(v.positionOS).xy;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

#if USE_SHAPE_LIGHT_TYPE_0
            // Ghibli-style banded light: posterized luminance of the global
            // 2D sun (blend style 0), cool shadow tint, brushy band borders.
            // Assumes the sun is a modulate-style light; other blend styles
            // are ignored here — toggle Banded Light off for the stock path.
            half4 PainterlyLight(half4 color, half2 lightingUV, float2 worldXY)
            {
                if (color.a == 0.0) discard;
                half4 l0 = SAMPLE_TEXTURE2D(_ShapeLightTexture0, sampler_ShapeLightTexture0, lightingUV);
                float lum = max(l0.r, max(l0.g, l0.b));
                half3 hue = lum > 0.001 ? l0.rgb / lum : half3(1.0, 1.0, 1.0);
                float banded = BandLightLevel(saturate(lum), worldXY);
                half3 lightMul = hue * lerp(_ShadowTint.rgb, half3(1.0, 1.0, 1.0), banded);

                half4 finalOutput;
                finalOutput.rgb = _HDREmulationScale * color.rgb * lightMul;
                finalOutput.a = color.a;
                finalOutput = lerp(color, finalOutput, _UseSceneLighting);
                return max(0, finalOutput);
            }
#endif

            half4 CombinedShapeLightFragment(Varyings i) : SV_Target
            {
                float2 rollDelta = RollDelta(i.positionOS2);
                half4 main = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                    ApplyDeltaToUV(i.uv, i.positionOS2, rollDelta));
                main.rgb = ApplyDetail(main.rgb, i.positionWS2, rollDelta);
                main.rgb = ApplyVariation(main.rgb, i.positionWS2);
                main.rgb = ApplyPaintLayers(main.rgb, i.positionWS2, i.positionOS2);
                main.rgb = ApplyIsoGrid(main.rgb, i.positionOS2);
                main.rgb = ApplyEdgeFalloff(main.rgb, i.positionOS2);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);

#if USE_SHAPE_LIGHT_TYPE_0
                if (_BandedLight > 0.5)
                    return PainterlyLight(main, i.lightingUV, i.positionWS2);
#endif
                SurfaceData2D surfaceData;
                InputData2D inputData;

                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(i.uv, i.lightingUV, inputData);

                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass // unlit fallback (3D renderer / previews)
        {
            Tags { "LightMode" = "UniversalForward" "Queue" = "Transparent" "RenderType" = "Transparent" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 positionOS2: TEXCOORD1;
                float2 positionWS2: TEXCOORD2;
            };

            Varyings UnlitVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.positionOS2 = v.positionOS.xy;
                o.positionWS2 = TransformObjectToWorld(v.positionOS).xy;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                float2 rollDelta = RollDelta(i.positionOS2);
                float4 main = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                    ApplyDeltaToUV(i.uv, i.positionOS2, rollDelta));
                main.rgb = ApplyDetail(main.rgb, i.positionWS2, rollDelta);
                main.rgb = ApplyVariation(main.rgb, i.positionWS2);
                main.rgb = ApplyPaintLayers(main.rgb, i.positionWS2, i.positionOS2);
                main.rgb = ApplyIsoGrid(main.rgb, i.positionOS2);
                main.rgb = ApplyEdgeFalloff(main.rgb, i.positionOS2);
                return main;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
