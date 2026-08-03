// Flynn/ShinglePlane — prototype shingle plane. Renders a sprite on a quad and
// darkens it with a gradient toward the "under" edge (the side the next plane
// laps over). Because the gradient lives in the quad's OWN UV (0..1 = the whole
// sprite), it spans the entire plane and can never cut off at a sprite edge —
// the neighbouring plane simply draws on top and covers the darkest part, so
// the exposed under-plane shows the shadow fading across it. Unlit for fast
// prototyping; swap to a 2D-lit pass when porting into the island.
Shader "Flynn/ShinglePlane"
{
    Properties
    {
        _MainTex("Sprite", 2D) = "white" {}
        _Tint("Tint", Color) = (1,1,1,1)
        _AlphaCutoff("Alpha Cutoff", Range(0.0, 0.5)) = 0.1

        [Header(Under Edge Shadow)]
        _ShadowStrength("Shadow Strength", Range(0, 1)) = 0.5
        // u where the shadow begins (0 = exposed edge) and how far it ramps to
        // full darkness at the under edge (u = 1). Whole gradient is inside the
        // sprite UV, so no hard cutoff is possible.
        _ShadowStart("Shadow Start (u)", Range(0, 1)) = 0.0
        _ShadowSoft("Shadow Softness", Range(0.02, 1)) = 1.0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            half4  _Tint;
            float  _AlphaCutoff;
            float  _ShadowStrength;
            float  _ShadowStart;
            float  _ShadowSoft;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv; // RAW 0..1 quad UV — the shadow ramp reads this
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Sample through _MainTex_ST so an atlas sub-rect maps onto the
                // full 0..1 quad; the shadow ramp still uses the raw quad UV.
                float2 texUV = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texUV);
                clip(c.a - _AlphaCutoff);

                // Shadow ramp inside this quad's UV: bright at u=0 (exposed
                // edge) -> dark at u=1 (under edge, covered by the next plane).
                float g = smoothstep(_ShadowStart, _ShadowStart + _ShadowSoft, i.uv.x);
                half3 rgb = c.rgb * _Tint.rgb * i.color.rgb * (1.0 - g * _ShadowStrength);
                return half4(rgb, c.a * _Tint.a * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
