// Flynn/IslandSkirt — URP 2D-lit cliff material for the procedural island
// skirt mesh (IslandSkirt.cs). Pass structure cloned from URP 14
// Sprite-Lit-Default so the Universal2D pass participates in 2D lighting
// (Light2D) exactly like a sprite.
//
// Mesh contract: UV.y = 0 at the island edge, 1 at the eroded bottom edge.
// All banding runs in that V space, so the gradient and strata follow the
// noise-displaced silhouette instead of sitting on straight world-Y lines.
// Rock detail is sampled in world XY — the plane the 2D geometry lives in.

Shader "Flynn/IslandSkirt"
{
    Properties
    {
        _DetailTex("Detail (world tiled)", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}

        [Header(Gradient)]
        _TopColor("Grass", Color) = (0.35, 0.55, 0.20, 1)
        _MidColor("Dirt", Color) = (0.45, 0.35, 0.22, 1)
        _BottomColor("Rock", Color) = (0.25, 0.22, 0.18, 1)
        _GrassDepth("Grass Depth", Range(0.02, 0.9)) = 0.15
        _BlendSoftness("Band Softness", Range(0.01, 0.5)) = 0.1

        [Header(Detail)]
        _DetailScale("Detail World Size", Float) = 3.0
        _DetailStrength("Detail Blend", Range(0, 1)) = 1.0
        _StrataCount("Strata Count", Range(0, 12)) = 5
        _StrataDarken("Strata Darken", Range(0, 0.5)) = 0.1
        _StrataWarp("Strata Warp", Range(0, 2)) = 0.5

        [Header(Shading)]
        _TopShadow("Under Lip Shadow", Range(0, 1)) = 0.35
        _TopShadowDepth("Under Lip Shadow Depth", Range(0.01, 0.5)) = 0.15
        _FadeStart("Bottom Fade Start", Range(0, 1)) = 0.75

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

        TEXTURE2D(_DetailTex);
        SAMPLER(sampler_DetailTex);
        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);
        float4 _Color;
        half4 _RendererColor;

        half4 _TopColor;
        half4 _MidColor;
        half4 _BottomColor;
        float _GrassDepth;
        float _BlendSoftness;
        float _DetailScale;
        float _DetailStrength;
        float _StrataCount;
        float _StrataDarken;
        float _StrataWarp;
        float _TopShadow;
        float _TopShadowDepth;
        float _FadeStart;

        // V-space cliff colour. uv.y: 0 = island edge, 1 = eroded bottom.
        // detailUV is the strip's surface space (x = arc length along the
        // contour, y = world depth down the wall), so the detail texture
        // wraps the cliff around corners instead of screen-projecting.
        half4 SkirtColor(float2 uv, float2 detailUV)
        {
            float v = saturate(uv.y);
            half4 rock = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, detailUV / max(_DetailScale, 0.001));

            // grass → dirt → rock, banded in V so it follows the eroded edge
            float dirtT = smoothstep(_GrassDepth - _BlendSoftness, _GrassDepth + _BlendSoftness, v);
            half3 col = lerp(_TopColor.rgb, _MidColor.rgb, dirtT);
            float rockStart = _GrassDepth + (1.0 - _GrassDepth) * 0.55;
            float rockT = smoothstep(rockStart - _BlendSoftness * 2.0, rockStart + _BlendSoftness * 2.0, v);
            col = lerp(col, _BottomColor.rgb, rockT);

            // world-tiled detail layer (neutral when no texture is assigned)
            col *= lerp(half3(1.0, 1.0, 1.0), rock.rgb, _DetailStrength);

            // strata bands, warped by the detail texture so they read organic;
            // masked by dirtT so the grass lip stays clean
            float band = frac(v * _StrataCount + (rock.g - 0.5) * _StrataWarp);
            float stripe = smoothstep(0.35, 0.8, 1.0 - abs(band - 0.5) * 2.0);
            col *= 1.0 - stripe * _StrataDarken * step(0.5, _StrataCount) * dirtT;

            // contact shadow just under the island lip
            col *= 1.0 - _TopShadow * (1.0 - smoothstep(0.0, _TopShadowDepth, v));

            // dissolve toward the bottom (floating in the sky)
            half alpha = 1.0 - smoothstep(_FadeStart, 1.0, v);
            return half4(col, alpha);
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
                float2 uv1        : TEXCOORD1; // surface space: arc, depth
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                half2  lightingUV : TEXCOORD1;
                float2 detailUV   : TEXCOORD2;
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
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 CombinedShapeLightFragment(Varyings i) : SV_Target
            {
                const half4 main = i.color * SkirtColor(i.uv, i.detailUV);
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
                float2 uv1        : TEXCOORD1; // surface space: arc, depth
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 detailUV   : TEXCOORD1;
            };

            Varyings UnlitVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.detailUV = v.uv1;
                o.uv = v.uv;
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                return i.color * SkirtColor(i.uv, i.detailUV);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
