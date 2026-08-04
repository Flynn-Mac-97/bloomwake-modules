// Darkens and drains the life out of everything outside the Chorus Lanterns' light.
//
// v4 (2026-07-30): world-space overlay quad, TWO fixed-blend passes, zero frame reads.
//   pass 1 (Blend DstColor Zero): frame *= lerp(darkness*tint, 1, lit)   — darken + cool tint
//   pass 2 (Blend One One):       frame += lerp(fog,           0, lit)   — lift blacks toward grey
// Together: col*A + B == a per-channel fade toward a flat misty grey — reads as drained/desaturated
// without sampling the frame. Every approach that read the camera image is dead on this project's
// 2D renderer (fullscreen blit → black; CameraSortingLayerTexture → uninitialized garbage; both go
// through URP's Blitter). Fixed blending cannot fail to bind anything.
//
// The radius here is the SAME number ChorusLantern.IsLit tests against, pushed in by
// LitZoneMaskDriver, so what looks lit and what counts as lit cannot drift apart.
// LitZoneOverlayQuad stretches the quad over the camera view every frame.
Shader "Flynn/LitZoneMask"
{
    Properties
    {
        // Set on the material asset; the driver does not push this one.
        _LitZoneFogLift ("Fog lift (grey added outside)", Range(0, 0.35)) = 0.10
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #define LITZONE_MAX 8

        // (centreX, centreY, radius, feather) per lantern, world units.
        float4 _LitZoneData[LITZONE_MAX];
        // float, not int: Shader.SetGlobalInt does not bind reliably into an int uniform.
        float  _LitZoneCount;
        // Kept for driver compatibility; the fog pass plays the desaturation role now.
        float  _LitZoneSaturation;
        // Brightness multiplier outside: 1 = untouched.
        float  _LitZoneDarkness;
        // Cool tint applied to the dark, so it reads as absence-of-light not just "turned down".
        float4 _LitZoneNightTint;
        float  _LitZoneFogLift;
        // 2:1 iso ground: a ground circle is a screen ellipse. Same value as ChorusLantern.IsoYSquash.
        float  _LitZoneYSquash;

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 world      : TEXCOORD0;
        };

        Varyings Vert(Attributes v)
        {
            Varyings o;
            float3 w = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(w);
            o.world = w.xy;
            return o;
        }

        float LitAt(float2 world)
        {
            int count = (int)(_LitZoneCount + 0.5);
            // Fail LIT, never dark: no zones pushed must leave the image untouched.
            if (count <= 0) return 1.0;
            float lit = 0.0;
            float squash = max(_LitZoneYSquash, 1e-3);
            [unroll(LITZONE_MAX)]
            for (int k = 0; k < LITZONE_MAX; k++)
            {
                if (k >= count) break;
                float4 z = _LitZoneData[k];
                float2 rel = world - z.xy;
                rel.y /= squash;   // ground circle -> screen ellipse (2:1 iso)
                float d = length(rel);
                float f = max(z.w, 1e-4);
                lit = max(lit, 1.0 - smoothstep(z.z - f, z.z, d));
            }
            return saturate(lit);
        }
        ENDHLSL

        Pass
        {
            Name "LitZoneMultiply"
            Blend DstColor Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMul
            half4 FragMul(Varyings i) : SV_Target
            {
                float lit = LitAt(i.world);
                float3 dark = _LitZoneDarkness * _LitZoneNightTint.rgb;
                return half4(lerp(dark, float3(1, 1, 1), lit), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "LitZoneFogLift"
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragAdd
            half4 FragAdd(Varyings i) : SV_Target
            {
                float lit = LitAt(i.world);
                float3 fog = _LitZoneFogLift * _LitZoneNightTint.rgb;
                return half4(lerp(fog, float3(0, 0, 0), lit), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
