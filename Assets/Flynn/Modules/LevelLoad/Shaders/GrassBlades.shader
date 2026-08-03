// Grass card shader for BillboardGrass.
//
// One merged mesh of upright quads; this shader does the per-card work:
//   - picks one of three grass colors per card (vertex COLOR.r)
//   - sways tips with the global wind driven by WindManager
//     (_WindStrength / _WindSpeed / _WindDirection — see Wind.hlsl)
//
// Per-vertex payload written by BillboardGrass:
//   COLOR     = (color rand, extra phase rand, sway weight 0=base 1=tip, 1)
//   TEXCOORD0 = grass_blades.png atlas UV
//   TEXCOORD1 = card base position in WORLD space (wind phase pivot,
//               same convention as _WindPivot in Wind.hlsl)
Shader "Flynn/GrassBlades"
{
    Properties
    {
        _MainTex ("Grass Alpha Atlas", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _ColorA ("Grass Color A", Color) = (0.45, 0.75, 0.32, 1)
        _ColorB ("Grass Color B", Color) = (0.33, 0.62, 0.26, 1)
        _ColorC ("Grass Color C", Color) = (0.55, 0.70, 0.25, 1)
        _WindResponse ("Wind Response", Range(0, 2)) = 1
        _AlphaClip ("Alpha Clip", Range(0, 0.5)) = 0.08

        // Internal shading so a card is a shaded tuft, not a flat chip.
        _RootDarken ("Root Darken", Range(0, 1)) = 0.38
        _TipLighten ("Tip Lighten", Range(0, 0.6)) = 0.16
        _ValueVary ("Per-card Value Variation", Range(0, 0.5)) = 0.14
        _TipDesat ("Tip Desaturate", Range(0, 1)) = 0.18
        _TipFeather ("Tip Alpha Feather", Range(0, 0.6)) = 0.22
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Flynn/Shaders/Wind.hlsl" // shared wind lib (stable infra) - _WindStrength / _WindSpeed / _WindDirection globals

        float4 _ColorA;
        float4 _ColorB;
        float4 _ColorC;
        float _WindResponse;
        float _AlphaClip;
        float _RootDarken;
        float _TipLighten;
        float _ValueVary;
        float _TipDesat;
        float _TipFeather;

        // UnderMist no longer touches grass: sprigs sit on the top surface, and the
        // dissolve is drop-below-contour on the skirt (see IslandSkirtSprite.shader).

        // Per-card color pick + wind sway, shared by all passes.
        // color: r = pick rand, g = phase rand, b = sway weight (0 base, 1 tip).
        // pivot: card base in world space.
        half3 PickGrassColor(float rand)
        {
            return rand < 0.3333 ? _ColorA.rgb
                 : rand < 0.6667 ? _ColorB.rgb
                 : _ColorC.rgb;
        }

        // Base (pre-light) card colour with internal shading: a per-card value
        // jitter so neighbours differ, a root→tip gradient (dark contact shadow
        // at the base, lighter sun-caught tips), and a slight tip desaturation so
        // the tops read airy instead of poster-flat. tip = COLOR.b (0 base, 1 tip).
        half3 ShadeGrassBase(float4 color)
        {
            half3 col = PickGrassColor(color.r);
            col *= 1.0 + (color.g - 0.5) * _ValueVary;              // per-card value
            half tip = saturate(color.b);
            col *= lerp(1.0 - _RootDarken, 1.0 + _TipLighten, tip); // vertical gradient
            half3 gray = dot(col, half3(0.299, 0.587, 0.114)).xxx;
            col = lerp(col, gray, _TipDesat * tip);                 // airy tips
            return col;
        }

        // Feather the top of the card so tips fade into the scene instead of
        // ending on a hard silhouette. tip = COLOR.b.
        half TipAlpha(float tipFrac)
        {
            return 1.0 - _TipFeather * saturate(tipFrac);
        }

        float3 ApplyGrassWind(float3 positionOS, float4 color, float2 pivot)
        {
            float3 posWS = TransformObjectToWorld(positionOS);

            // Same recipe as ApplyWind in Wind.hlsl, phase from the card's
            // own world pivot + a per-card random so neighbours desync.
            float phase = pivot.x * 3.7 + pivot.y * 2.3 + color.g * 6.2831853;
            float phase2 = pivot.x * 1.9 - pivot.y * 1.1;
            float2 dir = normalize(_WindDirection + 0.001);

            float sway = (sin(_Time.y * _WindSpeed + phase)
                        + sin(_Time.y * _WindSpeed * 1.3 + phase2) * 0.3)
                        * _WindStrength * _WindResponse;

            // Horizontal only — matches the project's sprite wind.
            posWS.x += sway * color.b * dir.x;
            return posWS;
        }
        ENDHLSL

        // ── Pass 1: 2D lit (shape lights) ───────────────────────────────
        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment
            #pragma target 2.0
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 pivot      : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                half2  lightingUV : TEXCOORD1;
                #if defined(DEBUG_DISPLAY)
                float3 positionWS : TEXCOORD2;
                #endif
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

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

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            // PERF: grass is lit PER-VERTEX — 4 light samples per card instead of one per
            // pixel through CombinedShapeLightShared, and the mask sample is gone. Cards
            // are a few pixels wide; the difference is invisible, the saving is the whole
            // per-fragment lit path across thousands of mostly-transparent quads.
            // Global-sun modulate convention mirrors IslandTopFill's PainterlyLight.
            Varyings GrassVertex(Attributes v)
            {
                Varyings o = (Varyings)0;

                float3 posWS = ApplyGrassWind(v.positionOS, v.color, v.pivot);
                o.positionCS = TransformWorldToHClip(posWS);
                #if defined(DEBUG_DISPLAY)
                o.positionWS = posWS;
                #endif
                o.uv = v.uv;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);

                half3 col = ShadeGrassBase(v.color);
                half alphaMul = TipAlpha(v.color.b);

                #if USE_SHAPE_LIGHT_TYPE_0
                half4 l0 = SAMPLE_TEXTURE2D_LOD(_ShapeLightTexture0, sampler_ShapeLightTexture0,
                                                o.lightingUV, 0);
                half3 lit = _HDREmulationScale * col * l0.rgb;
                col = lerp(col, lit, _UseSceneLighting);
                #endif

                o.color = half4(col, alphaMul);
                return o;
            }

            half4 GrassFragment(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _AlphaClip);   // skip blending the card's empty pixels
                half a = tex.a * i.color.a;
                return half4(i.color.rgb * tex.rgb * a, a);   // premultiplied
            }
            ENDHLSL
        }

        // ── Pass 2: Unlit fallback ───────────────────────────────────────
        Pass
        {
            Name "ForwardFallback"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 pivot      : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings GrassVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                float3 posWS = ApplyGrassWind(v.positionOS, v.color, v.pivot);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = v.uv;
                half3 col = ShadeGrassBase(v.color);
                o.color = half4(col, TipAlpha(v.color.b));
                return o;
            }

            half4 GrassFragment(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _AlphaClip);
                half a = tex.a * i.color.a;
                return half4(i.color.rgb * tex.rgb * a, a);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Lit-Default"
}
