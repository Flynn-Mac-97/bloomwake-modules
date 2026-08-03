// Flynn/IslandFringe — URP 2D-lit material for the procedural grass fringe
// mesh (IslandSkirt.BuildFringe). The blade strip texture is a white-with-
// alpha MASK: it only carves the blade silhouette. Colour comes from the
// same surface stack as the island top fill — fill texture (world tiled),
// shared detail layer, procedural variation and painterly light banding
// (IslandShading.hlsl) — so fringe and top read as one material. IslandSkirt
// syncs the shared uniforms from the fill material every regen.
//
// Mesh contract: UV0 = (arc repeats, strip V); vertex colour = tint ramp
// (root -> tip darken). Fill texture is sampled in OBJECT space — the fringe
// child sits at identity under the same transform as the SpriteShape, whose
// fill UVs are local-position based, so the pattern lines up at the root.
//
// Pass structure cloned from Flynn/IslandTopFill (URP 14 Sprite-Lit-Default).

Shader "Flynn/IslandFringe"
{
    Properties
    {
        _MainTex("Blade Strip (mask or coloured art + alpha)", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}

        [Header(Blade Art)]
        [Toggle] _ArtColored("Art Is Coloured (off = white mask)", Float) = 0
        _ArtBlendReach("Art Root Blend (fraction of blade)", Range(0.05, 1)) = 0.45
        _ArtRecolor("Art Recolor To Surface", Range(0, 1)) = 0

        [Header(Surface Match)]
        _FillTex("Island Fill (local tiled)", 2D) = "white" {}
        _FillScale("Fill World Size", Float) = 1.0

        [Header(Edge Falloff)]
        _EdgeDarken("Edge Darken", Range(0, 1)) = 0.3
        _FalloffPower("Falloff Shape", Range(0.25, 4)) = 1.0
        _EdgeTint("Edge Tint", Color) = (1, 1, 1, 1)
        // Baked per-island by IslandSkirt; declared for defaults.
        _FalloffTex("Falloff Mask (baked)", 2D) = "white" {}
        _FalloffRect("Falloff Rect (baked)", Vector) = (0, 0, 1, 1)

        [Header(Detail)]
        _DetailTex("Detail (world tiled)", 2D) = "white" {}
        _DetailScale("Detail World Size", Float) = 3.0
        _DetailStrength("Detail Blend", Range(0, 1)) = 1.0
        _DetailAspect("Detail Iso Squash (Y/X)", Range(0.25, 1)) = 0.5
        _DetailAntiTile("Detail Anti Tile", Range(0, 1)) = 0.6

        [Header(Surface Variation)]
        _VarStrength("Variation Strength", Range(0, 1)) = 0.35
        _VarScale("Variation World Size", Float) = 7.0
        _VarTintA("Variation Tint A", Color) = (0.87, 0.93, 0.78, 1)
        _VarTintB("Variation Tint B", Color) = (1, 1, 1, 1)

        [Header(Painterly Light)]
        [Toggle] _BandedLight("Banded Light", Float) = 1
        _LightBands("Light Bands", Range(1, 6)) = 3
        _BandSoftness("Band Edge Softness", Range(0.005, 0.5)) = 0.08
        _BandWobble("Band Border Wobble", Range(0, 1)) = 0.25
        _BandWobbleScale("Wobble World Size", Float) = 2.0
        _ShadowTint("Shadow Tint", Color) = (0.55, 0.58, 0.78, 1)

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
        #include "IslandShading.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);
        TEXTURE2D(_FillTex);
        SAMPLER(sampler_FillTex);
        half4 _MainTex_ST;
        float4 _Color;
        half4 _RendererColor;
        float _FillScale;
        float _ArtColored;
        float _ArtBlendReach;
        float _ArtRecolor;

        // Blade strip -> surface colour. bladeT = 0 at the rooted inner edge,
        // 1 at the tips (mesh uv1.x). White-mask art takes the island surface
        // stack for colour; coloured art blends from the surface colour at
        // the root (seamless against the top fill) into its own colours up
        // the blade. positionOS matches the SpriteShape's local-position
        // fill UVs; positionWS feeds the shared world passes.
        // UnderMist no longer touches the fringe: the lip sits at surface level all
        // around the island, and the dissolve is drop-below-contour on the skirt
        // (see IslandSkirtSprite.shader).

        half4 FringeSurface(half4 vertColor, float2 uv, float bladeT, float2 positionOS, float2 positionWS)
        {
            half4 m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            half4 col;
            // mask art carries blade shape in r*a; coloured art in a alone
            col.a = m.a * lerp(m.r, 1.0, _ArtColored) * vertColor.a;
            half3 surface = vertColor.rgb
                * SAMPLE_TEXTURE2D(_FillTex, sampler_FillTex, positionOS / max(_FillScale, 0.001)).rgb;
            surface = ApplyDetailWorld(surface, positionWS);
            surface = ApplyVariation(surface, positionWS);
            half3 art = m.rgb * vertColor.rgb; // island tint still applies
            // Recolor: art luminance carries the blade shading, the profile's
            // surface colour carries the hue — coloured art follows profile
            // restyles instead of keeping its baked-in green.
            float lum = dot(m.rgb, half3(0.299, 0.587, 0.114));
            art = lerp(art, surface * lum * 2.0, _ArtRecolor);
            float artT = smoothstep(0.0, max(_ArtBlendReach, 0.001), bladeT) * _ArtColored;
            col.rgb = lerp(surface, art, artT);
            col.rgb = ApplyEdgeFalloff(col.rgb, positionOS);
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
                float2 uv1        : TEXCOORD1; // x = 0 root -> 1 blade tip
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                half2  lightingUV : TEXCOORD1;
                float2 positionOS2: TEXCOORD2;
                float3 positionWS2: TEXCOORD3; // z = blade root->tip
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
                o.positionWS2 = float3(TransformObjectToWorld(v.positionOS).xy, v.uv1.x);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

#if USE_SHAPE_LIGHT_TYPE_0
            // Same banded painterly light as Flynn/IslandTopFill, so blades
            // and top surface fall into the same light bands.
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
                half4 main = FringeSurface(i.color, i.uv, i.positionWS2.z, i.positionOS2, i.positionWS2.xy);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);

#if USE_SHAPE_LIGHT_TYPE_0
                if (_BandedLight > 0.5)
                    return PainterlyLight(main, i.lightingUV, i.positionWS2.xy);
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
                float2 uv1        : TEXCOORD1; // x = 0 root -> 1 blade tip
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 positionOS2: TEXCOORD1;
                float3 positionWS2: TEXCOORD2; // z = blade root->tip
            };

            Varyings UnlitVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.positionOS2 = v.positionOS.xy;
                o.positionWS2 = float3(TransformObjectToWorld(v.positionOS).xy, v.uv1.x);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                return FringeSurface(i.color, i.uv, i.positionWS2.z, i.positionOS2, i.positionWS2.xy);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
