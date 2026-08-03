// Flynn/WaterFill — FLAT 2D stylized water for water SpriteShapes (WaterStack
// template). Stardew / pixel-art-water formula, no depth buffer — everything
// distance-driven off a baked contour mask (_FalloffTex, shared ContourFalloff):
//   1. flat body color (no pool gradient — this is the "more 2D" core)
//   2. back-bank wall: flat dark band on the top-facing shore (iso depression)
//   3. ripple rings: thin wobbled lines parallel to the shore, marching inward
//   4. shore line: crisp solid line at the waterline
//   5. sparkle: sparse horizontal glint dashes drifting slowly
//   6. edge blend: alpha ramps out so water sits soft on the land underneath
// Every layer zeroes out via its profile knob. Unlit + transparent.

Shader "Flynn/WaterFill"
{
    Properties
    {
        _MainTex("Fill Texture", 2D) = "white" {}
        _WaterTex("Pixel Water Tile", 2D) = "gray" {}
        _WaterTexScale("Tile World Size", Float) = 1
        _WaterScroll("Tile Scroll (xy)", Vector) = (0.01, 0.004, 0, 0)
        _LayerAnim("Layer Anim", Range(0, 1)) = 0.5
        _BodyColor("Tint", Color) = (1, 1, 1, 1)
        _ShoreColor("Shore/Ring Color", Color) = (0.88, 0.97, 0.95, 1)
        _ShoreWidth("Shore Line Width", Float) = 0.06
        _RingSpacing("Ring Spacing", Float) = 0.35
        _RingWidth("Ring Width", Float) = 0.08
        _RingSpeed("Ring Speed", Float) = 0.15
        _RingStrength("Ring Strength", Range(0, 1)) = 0.55
        _RingWobble("Ring Wobble", Float) = 0.08
        _UnderTex("Underwater Ground Tile", 2D) = "gray" {}
        _UnderTexScale("Underwater Tile World Size", Float) = 1
        _UnderTint("Underwater Tint", Color) = (0.62, 0.66, 0.72, 1)
        _FloorVis("Floor Visibility", Range(0, 1)) = 0.35
        _BankColor("Bank Wall Tint", Color) = (0.55, 0.5, 0.48, 1)
        _BankHeight("Back Bank Height", Float) = 0.22
        _BankOffsetY("Back Bank Y Offset", Float) = 0
        _BankSideInset("Back Bank Side Inset", Float) = 0
        _BankStrength("Back Bank Strength", Range(0, 1)) = 0.9
        _EdgeBlend("Edge Blend Width", Float) = 0.06
        _Alpha("Alpha", Range(0, 1)) = 0.95
        _FalloffTex("Shore Distance (baked)", 2D) = "white" {}
        _FalloffRect("Shore Rect", Vector) = (0, 0, 1, 1)
        _FalloffWorldWidth("Shore Bake Width", Float) = 1
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
            TEXTURE2D(_WaterTex);
            SAMPLER(sampler_WaterTex);
            TEXTURE2D(_UnderTex);
            SAMPLER(sampler_UnderTex);
            TEXTURE2D(_FalloffTex);
            SAMPLER(sampler_FalloffTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _WaterTexScale;
                float4 _WaterScroll;
                half _LayerAnim;
                half4 _BodyColor;
                half4 _ShoreColor;
                float _ShoreWidth;
                float _RingSpacing;
                float _RingWidth;
                float _RingSpeed;
                half _RingStrength;
                float _RingWobble;
                float _UnderTexScale;
                half4 _UnderTint;
                half _FloorVis;
                half4 _BankColor;
                float _BankHeight;
                float _BankOffsetY;
                float _BankSideInset;
                half _BankStrength;
                float _EdgeBlend;
                half _Alpha;
                float4 _FalloffRect;
                float _FalloffWorldWidth;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldPos : TEXCOORD1;
                float2 localPos : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = v.uv;
                o.worldPos = world.xy;
                o.localPos = v.positionOS.xy; // fill mesh = controller local = bake space
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // --- shore distance + interior flag from the baked contour mask ---
                float2 fuv = (i.localPos - _FalloffRect.xy) / max(_FalloffRect.zw, 1e-4);
                half2 shore = SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex, fuv).rg;
                float shoreDist = shore.r * _FalloffWorldWidth;

                // --- 1. body: seamless pixel tile, world-space UV, slow drift.
                //        Subtle top anim = a second offset copy drifting against
                //        the first (no frame animation needed). ---
                float2 wuv = i.worldPos / max(_WaterTexScale, 0.01);
                half3 t1 = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex,
                    wuv + _Time.y * _WaterScroll.xy).rgb;
                half3 t2 = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex,
                    wuv + 0.37 - _Time.y * _WaterScroll.yx * 1.3).rgb;
                half4 col = half4(lerp(t1, t2, 0.5 * _LayerAnim) * _BodyColor.rgb, 1);

                // --- 2. underwater ground, two reads off one texture:
                //        a) FLOOR: shows through the water UNIFORMLY — one opacity
                //           slider, no distance scaling
                //        b) WALL: the far (top) bank's inner face
                half3 under = SAMPLE_TEXTURE2D(_UnderTex, sampler_UnderTex,
                    i.worldPos / max(_UnderTexScale, 0.01)).rgb * _UnderTint.rgb;

                col.rgb = lerp(col.rgb, under, _FloorVis);

                // Orthographic wall test — EXACT, no perspective taper. The band
                // occupies the vertical range [offsetY, offsetY + height] below a
                // TOP shore: inside when stepping up by offsetY, outside when
                // stepping up past the band. Side inset trims the ends via
                // horizontal interior tests. Bottom/side shores never wall.
                float2 rectXY = _FalloffRect.xy;
                float2 rectWH = max(_FalloffRect.zw, 1e-4);
                half inAtOffset = SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex,
                    (i.localPos + float2(0, _BankOffsetY) - rectXY) / rectWH).g;
                half inPastBand = SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex,
                    (i.localPos + float2(0, _BankOffsetY + _BankHeight) - rectXY) / rectWH).g;
                float wallMask = shore.g * inAtOffset * (1.0 - inPastBand);
                if (_BankSideInset > 1e-4)
                {
                    // Shaded ends, not hard cuts: three horizontal interior taps per
                    // side make the wall RAMP out across the inset distance.
                    float sideL = 0, sideR = 0;
                    [unroll]
                    for (int k = 1; k <= 3; k++)
                    {
                        float o = _BankSideInset * k / 3.0;
                        sideL += SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex,
                            (i.localPos - float2(o, 0) - rectXY) / rectWH).g;
                        sideR += SAMPLE_TEXTURE2D(_FalloffTex, sampler_FalloffTex,
                            (i.localPos + float2(o, 0) - rectXY) / rectWH).g;
                    }
                    wallMask *= (sideL / 3.0) * (sideR / 3.0);
                }
                half3 wallCol = under * _BankColor.rgb;
                col.rgb = lerp(col.rgb, wallCol, _BankStrength * wallMask);

                // --- 3. ripple rings: thin wobbled shore-parallel lines, marching
                //        inward. Wobble breaks the concentric-circle read. ---
                float2 wp = i.worldPos;
                wp.y *= 2.0; // iso squash
                float wobble = (ValueNoise(wp / 0.9) - 0.5) * _RingWobble;
                float dw = shoreDist + wobble;
                float phase = frac(dw / max(_RingSpacing, 1e-4) + _Time.y * _RingSpeed);
                float rw = _RingWidth / max(_RingSpacing, 1e-4); // line width in phase units
                float ring = smoothstep(0.0, rw, phase) * smoothstep(rw * 2.0, rw, phase);
                float ringZone = 1.0 - saturate(dw / (max(_RingSpacing, 1e-4) * 3.0));
                col.rgb = lerp(col.rgb, _ShoreColor.rgb, ring * ringZone * _RingStrength);

                // --- 4. crisp shore line at the waterline ---
                float shoreLine = smoothstep(_ShoreWidth, _ShoreWidth * 0.5, shoreDist);
                col.rgb = lerp(col.rgb, _ShoreColor.rgb, shoreLine);

                // --- 5. edge blend: soften onto the land underneath ---
                float edge = _EdgeBlend > 1e-4 ? saturate(shoreDist / _EdgeBlend) : 1.0;
                col.a = _Alpha * edge * tex.a;
                return col;
            }
            ENDHLSL
        }
    }
}
