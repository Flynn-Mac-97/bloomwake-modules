// Flynn/WaterRim — layered rim material for the water SpriteShape's EDGE
// sprites (the grass trim band). The trim sprite is the art; this shader
// layers blend effects over it so the rim melts into the surrounding ground:
//   1. outer fade : alpha feathers out on the land side (uv.y -> 1)
//   2. wet band   : darkens toward the waterline side (uv.y -> 0)
//   3. sway       : gentle breeze wobble, waterline side moves, land side
//                   stays rooted
//   4. tint
// NOTE: assumes the trim sprite is a full-rect single sprite (uv.y spans
// 0..1 across the band height) — true for the generated/authored trim strips.

Shader "Flynn/WaterRim"
{
    Properties
    {
        _MainTex("Sprite Texture", 2D) = "white" {}
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _OuterFade("Outer Fade (band fraction)", Range(0, 1)) = 0.2
        _WetBand("Wet Band (band fraction)", Range(0, 1)) = 0.3
        _WetColor("Wet Multiply", Color) = (0.55, 0.75, 0.8, 1)
        _SwayAmp("Sway Amplitude (world)", Float) = 0.02
        _SwaySpeed("Sway Speed", Float) = 1.2
        _SwayScale("Sway World Wavelength", Float) = 1.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half _OuterFade;
                half _WetBand;
                half4 _WetColor;
                float _SwayAmp;
                float _SwaySpeed;
                float _SwayScale;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS);
                // Breeze: waterline side (uv.y -> 0) sways, land side stays rooted.
                float pin = 1.0 - v.uv.y;
                world.x += sin(_Time.y * _SwaySpeed + world.x / max(_SwayScale, 0.05)
                             + world.y * 1.7) * _SwayAmp * pin;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv)
                          * i.color * _Tint;

                // wet band toward the waterline
                half wet = _WetBand > 1e-3
                    ? smoothstep(_WetBand, 0.0, i.uv.y)
                    : 0.0;
                col.rgb = lerp(col.rgb, col.rgb * _WetColor.rgb, wet * _WetColor.a);

                // outer fade into the surrounding ground
                if (_OuterFade > 1e-3)
                    col.a *= smoothstep(1.0, 1.0 - _OuterFade, i.uv.y);

                return col;
            }
            ENDHLSL
        }
    }
}
