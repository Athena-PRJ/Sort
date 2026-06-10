Shader "Sort/CometTrail"
{
    // A soft, translucent "comet / shooting-star" trail with tiny twinkling sparkle dots.
    // Additive (Blend SrcAlpha One) so it glows on the dark board background and plays nicely with
    // Bloom. Designed for a TrailRenderer: UV.y is across the ribbon width; the trail's Color gradient
    // (set alpha 1 -> 0) drives the head->tail fade via the vertex colour. Fully procedural — no texture.
    Properties
    {
        [HDR] _BaseColor      ("Glow Color (HDR)", Color)              = (0.75, 0.88, 1.0, 1.0)
        _Intensity            ("Glow Intensity", Range(0, 4))          = 1.3
        _Softness             ("Edge Softness (across width)", Range(0.02, 1)) = 0.55
        _SparkleScale         ("Sparkle Density", Range(2, 256))       = 64
        _SparkleSpeed         ("Sparkle Twinkle Speed", Range(0, 30))  = 8
        _SparkleAmount        ("Sparkle Amount", Range(0, 1))          = 0.45
        _SparkleBoost         ("Sparkle Brightness", Range(0, 8))      = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Blend SrcAlpha One      // additive glow
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Intensity;
                float _Softness;
                float _SparkleScale;
                float _SparkleSpeed;
                float _SparkleAmount;
                float _SparkleBoost;
            CBUFFER_END

            // Cheap 2D hash -> [0,1].
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Soft falloff across the ribbon width -> wispy translucent edges.
                float across = IN.uv.y;
                float edge = smoothstep(0.0, _Softness, across) * smoothstep(0.0, _Softness, 1.0 - across);

                // Head -> tail fade comes from the TrailRenderer Color gradient (vertex colour).
                float vfade = IN.color.a;

                // The soft comet body.
                float body = edge * vfade;

                // Procedural twinkling sparkles: sparse bright dots scattered along the streak that
                // blink over time -> the "đốm sáng li ti" look, without a particle system.
                float2 sp     = float2(IN.uv.x * _SparkleScale, across * max(1.0, _SparkleScale * 0.2));
                float2 cell   = floor(sp);
                float  twinkle = floor(_Time.y * _SparkleSpeed);
                float  rnd    = hash21(cell + twinkle);
                float2 f      = frac(sp) - 0.5;
                float  dotMask = saturate(1.0 - dot(f, f) * 6.0);          // soft round dot in the cell
                float  spark  = step(1.0 - _SparkleAmount * 0.2, rnd) * dotMask * _SparkleBoost * body;

                half3 rgb = _BaseColor.rgb * IN.color.rgb * _Intensity;
                float a   = saturate(body * _BaseColor.a + spark);          // additive: framebuffer += rgb * a
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
