// Radial-wipe slash arc (Cyanilux-style, flattened to a single quad): a ring band
// in polar UV space, revealed by sweeping _Progress across the span, with a fading
// tail and a wobbled radius for a brushstroke edge. Driven per-instance via
// MaterialPropertyBlock. Quad UVs are remapped to [-1,1]; 0deg = +X.
Shader "Flynn/FXLab/ArcWipe"
{
    Properties
    {
        _Color ("Color", Color) = (1,0.85,0.53,0.9)
        _Progress ("Progress", Range(0,1)) = 0
        _SpanDeg ("Span (deg)", Float) = 140
        _TailDeg ("Tail (deg)", Float) = 70
        _InnerR ("Inner Radius", Float) = 0.30
        _OuterR ("Outer Radius", Float) = 0.48
        _EdgeSoft ("Edge Softness", Float) = 0.05
        _Wobble ("Radius Wobble", Float) = 0.02
        _HeadBoost ("Head Highlight", Float) = 0.6
        _Fade ("Overall Fade", Range(0,1)) = 1
        _Seed ("Wobble Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;
            };

            fixed4 _Color;
            float _Progress, _SpanDeg, _TailDeg;
            float _InnerR, _OuterR, _EdgeSoft, _Wobble, _HeadBoost, _Fade, _Seed;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv = IN.texcoord * 2.0 - 1.0;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float r = length(IN.uv);
                float ang = atan2(IN.uv.y, IN.uv.x);            // -pi..pi, 0 = +X

                float halfSpan = radians(_SpanDeg) * 0.5;
                float head = lerp(-halfSpan, halfSpan, _Progress);
                float tail = radians(max(_TailDeg, 1.0));

                // reveal: solid at the head, fading out over the tail behind it
                float behind = head - ang;
                float sweep = step(0.0, behind) * (1.0 - smoothstep(0.0, tail, behind));

                // thin out toward the span tips so the arc tapers like a crescent
                float tip = 1.0 - smoothstep(halfSpan * 0.75, halfSpan, abs(ang));

                // brushstroke edge: wobble the sampled radius by angle
                float wob = sin(ang * 9.0 + _Seed) * 0.5 + sin(ang * 17.0 - _Seed * 1.3) * 0.5;
                float rr = r + wob * _Wobble;
                float band = smoothstep(_InnerR, _InnerR + _EdgeSoft, rr)
                           * (1.0 - smoothstep(_OuterR - _EdgeSoft, _OuterR, rr));

                // bright leading edge
                float headGlow = 1.0 + _HeadBoost * (1.0 - smoothstep(0.0, radians(14.0), abs(behind)));

                float a = sweep * tip * band * _Fade * _Color.a;
                fixed4 c;
                c.rgb = _Color.rgb * a * headGlow;   // premultiplied
                c.a = a;
                return c;
            }
            ENDCG
        }
    }
}
