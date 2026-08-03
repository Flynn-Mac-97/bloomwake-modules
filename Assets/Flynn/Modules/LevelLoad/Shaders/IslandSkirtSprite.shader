// Flynn/IslandSkirtSprite — URP 2D-lit cliff material that tiles a rock
// sprite with alpha cutout along the procedural skirt mesh. The sprite's
// own alpha silhouette creates natural rocky edges and a ragged bottom,
// replacing the smooth gradient+strata look of IslandSkirt.shader.
//
// Mesh contract (same as IslandSkirt):
//   UV0.x = arc length along contour, UV0.y = 0 at island edge, 1 at bottom
//   UV1   = surface space (arc, -drop below contour top in world units) —
//           drives the UnderMist dissolve so it hugs the cliff bottom evenly
//           around the island (a world-Y line only hazed the front section)
//   Vertex colour = baked fake shading (directional, depth layer, crevice AO)
//
// Material _MainTex tiling/offset selects a sub-rect from a sprite sheet.

Shader "Flynn/IslandSkirtSprite"
{
    Properties
    {
        _MainTex("Rock Sprite", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}

        [Header(Tiling)]
        _TileCountH("Tiles Per Unit (H)", Float) = 2.0
        _Overlap("Tile Overlap", Range(0, 0.8)) = 0.3
        _OverlapShade("Overlap Shade", Range(0, 1)) = 0.3
        _MirrorTiles("Mirror Alternate", Range(0, 1)) = 1.0
        _BottomJitter("Bottom Jitter", Range(0, 0.3)) = 0.12
        _SideJitter("Side Jitter", Range(0, 0.2)) = 0.08

        [Header(Depth Layering)]
        // Which overlapping tile draws on top: the one nearer the camera
        // (lowest on screen), read from the baked facing normal, instead of a
        // fixed +arc-over-prev order. Soft = smoothstep width of the flip.
        _LayerBlendSoft("Layer Flip Softness", Range(0.01, 0.5)) = 0.15

        [Header(Contact Shadow)]
        // The UNDER tile darkens in the seam where the OVER tile laps over it
        // (contact/occlusion shadow, not a directional cast). Strength is
        // _OverlapShade above; Reach = how far into the over-tile body we probe
        // = how deep the shadow bites past the seam.
        _ShadowDist("Shadow Reach", Range(0.0, 1.0)) = 0.6

        [Header(Alpha)]
        _AlphaCutoff("Alpha Cutoff", Range(0.01, 0.5)) = 0.1

        [Header(Tint)]
        _Tint("Tint", Color) = (1,1,1,1)

        // Sprite plumbing so the 2D lit passes behave like Sprite-Lit-Default.
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);
        float4 _MainTex_ST;
        float4 _Color;
        half4 _RendererColor;

        float _TileCountH;
        float _Overlap;
        float _OverlapShade;
        float _MirrorTiles;
        float _BottomJitter;
        float _SideJitter;
        float _LayerBlendSoft;
        float _ShadowDist;
        float _AlphaCutoff;
        half4 _Tint;
        float4 _RockRects[8]; // per-variant atlas rect: xy = uv offset, zw = uv scale
        float  _RockCount;    // how many variants are valid (>=1)

        // Deterministic per-tile hash (0-1)
        float Hash11(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

        // Sample a single tile by index. spriteU is 0-1 within the sprite.
        // Per-tile V jitter (bottom) breaks the bottom silhouette.
        // Per-tile U offset (side) shifts the sprite horizontally so its side
        // alpha edges land at different positions per tile — breaks the smooth
        // side silhouette at the island's horizontal extremes. Wraps because
        // the sprite's edges are transparent (alpha=0), so the wrap seam is
        // invisible for small offsets.
        half4 SampleTileSprite(float tileIdx, float spriteU, float v, float offsetV)
        {
            float mirror = lerp(1.0, (fmod(tileIdx, 2.0) > 0.5) ? -1.0 : 1.0, _MirrorTiles);
            float uJitter = (Hash11(tileIdx * 1.618) - 0.5) * _SideJitter;
            float u = frac(spriteU + uJitter);
            u = (u - 0.5) * mirror + 0.5;
            float vJitter = (Hash11(tileIdx) - 0.5) * _BottomJitter;
            float localV = (1.0 - v) + offsetV + vJitter;
            // Pick a random rock variant per tile from the atlas rects.
            int ridx = (int)min(floor(Hash11(tileIdx * 2.393) * _RockCount), _RockCount - 1.0);
            float4 rect = _RockRects[ridx];
            float2 sampleUV = float2(u, localV) * rect.zw + rect.xy;
            return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV);
        }

        // Sprite-tiled cliff colour. uv.y: 0 = island edge, 1 = eroded bottom.
        half4 SkirtSpriteColor(float2 uv, float2 facing)
        {
            float v = saturate(uv.y);

            // --- Front layer: two overlapping tiles ---
            float tileSpan = 1.0 + _Overlap;
            float scaledU  = uv.x * _TileCountH;
            float tileIdx  = floor(scaledU);
            float localU   = frac(scaledU);

            half4 cur  = SampleTileSprite(tileIdx,     localU / tileSpan,         v, 0);
            half4 prev = SampleTileSprite(tileIdx - 1, (localU + 1.0) / tileSpan, v, 0);

            // Smooth visibility — no hard cutoff
            float curSolid  = smoothstep(_AlphaCutoff, _AlphaCutoff + 0.15, cur.a);
            float prevSolid = smoothstep(_AlphaCutoff, _AlphaCutoff + 0.15, prev.a);

            // --- Depth order: the tile nearer the camera draws on top ---
            // facing = baked smoothed outward normal. layerBias = -facing.x > 0
            // where the +arc (cur) side descends toward the nearest-to-cam point
            // (lowest on screen), so tiles cascade outward from the nearest
            // corner on both flanks instead of a fixed +arc-over-prev order.
            float layerBias = -facing.x;
            float curOnTop  = smoothstep(-_LayerBlendSoft, _LayerBlendSoft, layerBias);

            // Composite: draw the top tile over the under tile, order per flip.
            half3 curCol  = cur.rgb  * _Tint.rgb;
            half3 prevCol = prev.rgb * _Tint.rgb;
            half3 col = lerp(lerp(curCol,  prevCol, prevSolid),  // prev on top
                             lerp(prevCol, curCol, curSolid),    // cur on top
                             curOnTop);
            half alpha = max(cur.a, prev.a);

            // --- Seam crease shadow ---
            // A soft occlusion valley centred on each tile boundary, blending
            // symmetrically BOTH ways from the seam so there is no hard start.
            // dSeam is distance to the nearest seam and is continuous across the
            // cell boundary (0 at the seam on either side), so the crease never
            // cuts. _ShadowDist = half-width of the fade, _OverlapShade = depth.
            float w      = max(_ShadowDist, 1e-3);
            float dSeam  = min(localU, 1.0 - localU);            // 0 at seam → 0.5 mid-tile
            float shadow = (1.0 - smoothstep(0.0, w, dSeam)) * _OverlapShade;
            col *= 1.0 - shadow;

            clip(alpha - 0.01);
            return half4(col, alpha);
        }

        // VibeLayers UnderMist hook: atmosphere dissolve for the floating island's
        // underside, driven by DROP below the contour top (UV1.y, world units) so the
        // haze wraps the skirt bottom evenly around the whole island. Globals pushed
        // by UnderMistLayerController; default to 0 = zero effect, so this shader
        // stays fully independent of that module.
        float4 _FlynnUnderFade;        // x = drop where fade starts, y = drop fully gone, z = strength
        half4 _FlynnUnderFadeColor;    // sky color the underside pulls toward

        half4 ApplyUnderFade(half4 col, float drop)
        {
            float span = max(_FlynnUnderFade.y - _FlynnUnderFade.x, 0.001);
            float f = saturate((drop - _FlynnUnderFade.x) / span);
            f = f * f * (3.0 - 2.0 * f) * saturate(_FlynnUnderFade.z);
            col.rgb = lerp(col.rgb, _FlynnUnderFadeColor.rgb, f * 0.55);
            col.a *= 1.0 - f;
            return col;
        }
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
                float2 uv1        : TEXCOORD1; // surface space: arc, depth (unused)
                float2 uv2        : TEXCOORD2; // smoothed outward normal (facing)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                half2  lightingUV : TEXCOORD1;
                float2 detailUV   : TEXCOORD2;
                float2 facing     : TEXCOORD3;
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
                o.detailUV = v.uv1;
                o.uv = v.uv;
                o.facing = v.uv2;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 CombinedShapeLightFragment(Varyings i) : SV_Target
            {
                // i.color carries baked vertex shading (directional, depth, crevice AO)
                // plus _Color and _RendererColor tints — multiplied here, same as
                // the original IslandSkirt shader.
                const half4 main = ApplyUnderFade(i.color * SkirtSpriteColor(i.uv, i.facing),
                                                  -i.detailUV.y);   // UV1.y = -drop
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);
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
                float2 uv1        : TEXCOORD1;
                float2 uv2        : TEXCOORD2; // smoothed outward normal (facing)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 detailUV   : TEXCOORD1;
                float2 facing     : TEXCOORD2;
            };

            Varyings UnlitVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.detailUV = v.uv1;
                o.uv = v.uv;
                o.facing = v.uv2;
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                return ApplyUnderFade(i.color * SkirtSpriteColor(i.uv, i.facing), -i.detailUV.y);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
