// Flynn/PlatformSkirt — cliff material for RAISED PLATFORM rims (elevation
// terraces), as opposed to Flynn/IslandSkirtSprite which dresses a floating
// island's full-height cliff. Same rock sheet, same overlapping-tile look,
// but two things differ because a platform rim is short and sits ON ground:
//
//  1. WORLD-SPACE UVs, NOT NORMALISED ONES.
//     IslandSkirtSprite reads UV0, whose V is the row fraction scaled by
//     IslandSkirt's `vScale = depth / skirtShape.thickness` — so every
//     elevation tier crops a different top slice of the rock sprite while the
//     horizontal tile pitch stays fixed. A 0.25-tall z1 rim ends up ~7:1 wide
//     per tile against ~1.8:1 on the 0.95-tall island: the rim reads smeared,
//     and the crop lands in the sprite's transparent top padding, which the
//     alpha clip then punches holes through.
//     This shader reads UV1 instead — the mesh's surface space, (arc length,
//     -drop) in WORLD UNITS — and sizes one tile as
//         width = _RimHeight * _TileAspect,  height = _RimHeight,
//     where _RimHeight is ONE GRID STEP rather than the platform's full drop.
//     A taller platform therefore REPEATS that band downward instead of
//     stretching one tile over it, so texel density is identical whether a
//     platform is one step or three steps high.
//
//  2. IT IS OPAQUE.
//     The rock is composited over _BaseFill and alpha is forced to 1 — there is
//     no cutout and no bottom dissolve. A platform rim is the wall between its
//     top surface and the ground below it; anything the rock silhouette does
//     not cover must show cliff, never the background.
//
// Mesh contract (IslandSkirt.cs, strip mode):
//   UV0   = (arc, row fraction x vScale)   — deliberately unused here
//   UV1   = (arc, -drop below the contour top), both in world units
//   UV2   = smoothed outward normal (facing), drives the overlap depth flip
//   COLOR = baked fake shading (directional, depth layer, crevice AO)
//
// _RimHeight is pushed per skirt by IslandSkirt.SyncMaterials — the loader's
// heightStep via IslandSkirt.RimBandHeight, falling back to the skirt's full
// traced depth (one band) when no band height was supplied. Every other property
// comes from IslandVisualProfile's skirtSprite block, same as IslandSkirtSprite.

Shader "Flynn/PlatformSkirt"
{
    Properties
    {
        _MainTex("Rock Sprite", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}

        [Header(Rim)]
        _RimHeight("Rim Height (world)", Float) = 0.25
        _TileAspect("Tile Width / Rim Height", Float) = 1.8

        [Header(Tiling)]
        _Overlap("Tile Overlap", Range(0, 0.8)) = 0.3
        _OverlapShade("Overlap Shade", Range(0, 1)) = 0.3
        _MirrorTiles("Mirror Alternate", Range(0, 1)) = 1.0
        _SideJitter("Side Jitter", Range(0, 0.2)) = 0.08

        [Header(Depth Layering)]
        _LayerBlendSoft("Layer Flip Softness", Range(0.01, 0.5)) = 0.15
        _ShadowDist("Shadow Reach", Range(0.0, 1.0)) = 0.6

        [Header(Fill)]
        // Solid cliff colour behind the rock silhouette. This is what makes the
        // rim gap-proof. Use a MID rock tone, not the darkest one in the sheet —
        // it is what shows wherever the rock alpha thins, so too dark reads as a
        // black band rather than as cliff.
        _BaseFill("Base Fill", Color) = (0.42, 0.38, 0.33, 1)
        _AlphaCutoff("Rock Coverage Cutoff", Range(0.01, 0.5)) = 0.1
        // A platform rim MEETS THE GROUND, so it must not show the rock sprite's
        // eroded bottom silhouette — that is drawn for an island hanging in sky.
        // Crop the bottom of the sprite away and sit on its solid body instead.
        _BottomCrop("Bottom Crop", Range(0, 0.6)) = 0.12

        [Header(Back Layer)]
        _BackLayerEnable("Back Layer", Range(0, 1)) = 1
        _BackOffsetH("Back Offset H", Range(-1, 1)) = 0.5
        _BackOffsetV("Back Offset V", Range(-1, 1)) = 0.15
        _BackShade("Back Shade", Range(0, 1)) = 0.45
        _BackScale("Back Scale", Range(0.5, 2)) = 1.1

        // NO grass lip here. Per IslandSkirt.cs the lip is the separate Fringe
        // mesh; a skirt shader that also draws one double-draws it.

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
        float4 _Color;
        half4 _RendererColor;

        float _RimHeight;
        float _TileAspect;
        float _Overlap;
        float _OverlapShade;
        float _MirrorTiles;
        float _SideJitter;
        float _LayerBlendSoft;
        float _ShadowDist;
        half4 _BaseFill;
        float _AlphaCutoff;
        float _BottomCrop;
        float _BackLayerEnable;
        float _BackOffsetH;
        float _BackOffsetV;
        float _BackShade;
        float _BackScale;
        half4 _Tint;
        float4 _RockRects[8]; // per-variant atlas rect: xy = uv offset, zw = uv scale
        float  _RockCount;    // how many variants are valid (>=1)

        // Deterministic per-tile hash (0-1)
        float Hash11(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

        // One rock tile. spriteU/spriteV are 0-1 inside the sprite in TEXTURE
        // space, so spriteV 1 = sprite top = rim top. V is used whole, so
        // nothing is cropped and the sample never walks off the atlas rect into
        // a neighbouring sprite. Only U is jittered per tile — a V jitter on a
        // short rim would push the sample into the transparent top padding,
        // which is what punches holes in the island shader on a platform.
        half4 SampleTileSprite(float tileIdx, float spriteU, float spriteV, float scale)
        {
            float mirror = lerp(1.0, (fmod(tileIdx, 2.0) > 0.5) ? -1.0 : 1.0, _MirrorTiles);
            float uJitter = (Hash11(tileIdx * 1.618) - 0.5) * _SideJitter;
            float u = frac(spriteU + uJitter);
            u = (u - 0.5) * mirror + 0.5;

            // Scale about the tile centre (back layer reads slightly larger).
            float s = max(scale, 0.01);
            u = saturate((u - 0.5) / s + 0.5);
            float v = saturate((spriteV - 0.5) / s + 0.5);

            // _RockCount is script-pushed, so it reads 0 on a material that has not
            // been synced yet — clamp or the variant index lands at -1.
            float cnt = max(_RockCount, 1.0);
            int ridx = (int)min(floor(Hash11(tileIdx * 2.393) * cnt), cnt - 1.0);
            float4 rect = _RockRects[ridx];
            float2 sampleUV = float2(u, v) * rect.zw + rect.xy;
            return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV);
        }

        // Two overlapping tiles at one arc position, composited nearest-last.
        // Returns rgb premixed and .a = how much rock covers this fragment.
        // `band` offsets the per-tile hash so vertically stacked bands pick different
        // rock variants / mirroring — otherwise a tall rim reads as an obvious
        // photocopy of the same row.
        half4 RockLayer(float scaledU, float spriteV, float facingX, float scale, float band)
        {
            float tileSpan = 1.0 + _Overlap;
            float tileIdx  = floor(scaledU) + band * 37.0;
            float localU   = frac(scaledU);

            half4 cur  = SampleTileSprite(tileIdx,     localU / tileSpan,         spriteV, scale);
            half4 prev = SampleTileSprite(tileIdx - 1, (localU + 1.0) / tileSpan, spriteV, scale);

            float curSolid  = smoothstep(_AlphaCutoff, _AlphaCutoff + 0.15, cur.a);
            float prevSolid = smoothstep(_AlphaCutoff, _AlphaCutoff + 0.15, prev.a);

            // Depth order: the tile nearer the camera draws on top. facing =
            // baked smoothed outward normal; layerBias = -facing.x > 0 where the
            // +arc (cur) side descends toward the nearest-to-camera point.
            float curOnTop = smoothstep(-_LayerBlendSoft, _LayerBlendSoft, -facingX);

            half3 col = lerp(lerp(cur.rgb,  prev.rgb, prevSolid),   // prev on top
                             lerp(prev.rgb, cur.rgb,  curSolid),    // cur on top
                             curOnTop);

            // Seam crease: a soft occlusion valley centred on each tile boundary,
            // symmetric both ways so it never cuts.
            float w      = max(_ShadowDist, 1e-3);
            float dSeam  = min(localU, 1.0 - localU);
            col *= 1.0 - (1.0 - smoothstep(0.0, w, dSeam)) * _OverlapShade;

            return half4(col, max(cur.a, prev.a));
        }

        // Platform rim colour. surfaceUV = UV1 = (arc, -drop), world units.
        //
        // _RimHeight is ONE GRID STEP, not the platform's full drop. The rim repeats
        // that band downward, so a 1-step and a 3-step platform wear rock at exactly
        // the same texel density — the whole point of this shader. Sizing a single
        // tile to the full drop (what this did before) made a 3-step rim's rock 3x
        // the size of a 1-step rim's, which is the stretch being fixed.
        half4 PlatformSkirtColor(float2 surfaceUV, float2 facing)
        {
            float bandH = max(_RimHeight, 1e-3);
            float drop  = max(-surfaceUV.y, 0.0);

            // Bands run from the rim TOP downward, so band 0 is always complete and
            // any remainder lands in the LAST band, cropped at its bottom where it
            // meets the ground — never a half-height band hanging off the lip.
            float t      = drop / bandH;
            float band   = floor(t);
            float vLocal = frac(t);

            // Band top sits at the sprite top; band bottom stops at _BottomCrop rather
            // than the sprite's eroded bottom edge, so each band butts cleanly into
            // the one below instead of showing a ragged sky-island silhouette.
            float spriteV = lerp(1.0, saturate(_BottomCrop), vLocal);

            // One tile is _TileAspect x 1 BAND heights — fixed world size, so tile
            // shape AND size are now identical on every tier.
            float tileSpan   = 1.0 + _Overlap;
            float tileWorldW = bandH * max(_TileAspect, 0.05);
            float scaledU    = surfaceUV.x * (tileSpan / tileWorldW);

            half3 col = _BaseFill.rgb;

            // Back layer first: a darker, offset, slightly larger copy that fills
            // the holes the front silhouette leaves.
            if (_BackLayerEnable > 0.5)
            {
                half4 back = RockLayer(scaledU + _BackOffsetH,
                                       saturate(spriteV - _BackOffsetV * 0.5),
                                       facing.x, _BackScale, band);
                float backSolid = smoothstep(_AlphaCutoff, _AlphaCutoff + 0.15, back.a);
                col = lerp(col, back.rgb * (1.0 - _BackShade), backSolid);
            }

            half4 front = RockLayer(scaledU, spriteV, facing.x, 1.0, band);
            float frontSolid = smoothstep(_AlphaCutoff, _AlphaCutoff + 0.15, front.a);
            col = lerp(col, front.rgb, frontSolid);

            col *= _Tint.rgb;

            // Opaque by construction: the rim is a wall, never a cutout.
            return half4(col, 1.0);
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
                float2 uv         : TEXCOORD0; // unused: normalised, tier-dependent
                float2 uv1        : TEXCOORD1; // surface space: arc, -drop (world units)
                float2 uv2        : TEXCOORD2; // smoothed outward normal (facing)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                half2  lightingUV : TEXCOORD1;
                float2 surfaceUV  : TEXCOORD2;
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
                o.uv = v.uv;
                o.surfaceUV = v.uv1;
                o.facing = v.uv2;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 CombinedShapeLightFragment(Varyings i) : SV_Target
            {
                // i.color carries baked vertex shading plus _Color/_RendererColor.
                // Only the rgb is taken: the rim must stay opaque.
                half4 rim = PlatformSkirtColor(i.surfaceUV, i.facing);
                const half4 main = half4(i.color.rgb * rim.rgb, 1.0);
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
                float2 uv2        : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 surfaceUV  : TEXCOORD0;
                float2 facing     : TEXCOORD1;
            };

            Varyings UnlitVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.surfaceUV = v.uv1;
                o.facing = v.uv2;
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                half4 rim = PlatformSkirtColor(i.surfaceUV, i.facing);
                return half4(i.color.rgb * rim.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
