// RainbowFlow.shader
// URP-compatible shader that animates a rainbow gradient flowing across the mesh.
// Default flow direction is "top-left to bottom-right" in object space.
// Uses _Time so it animates continuously, including while idle.
//
// Tunable material properties (all visible in the Inspector):
//   _Speed         — how fast colors cycle (higher = faster).
//   _Scale         — tiling of the gradient across the mesh (higher = more bands).
//   _FlowDir       — object-space direction the gradient flows along.
//                    Default (1,-1,0) gives the top-left → bottom-right diagonal.
//   _Saturation    — 0..1 colour saturation.
//   _Brightness    — output multiplier (>1 = bloom-friendly punchy colors).
//   _ShadeMix      — 0..1 blend of a cheap NdotL-style shade so the rainbow still
//                    reads as a 3D Lego piece instead of looking like flat plastic.
//   _GradientTex   — OPTIONAL custom color strip (long thin PNG). When _UseGradient
//                    is ON, the shader samples this texture using `hue` as the U
//                    coordinate instead of using procedural HSV. Set the texture's
//                    Wrap Mode to "Repeat" and design it so the leftmost and
//                    rightmost pixels match — otherwise you'll see a seam each loop.
//   _UseGradient   — toggle: 0 = procedural HSV rainbow (default), 1 = sample PNG.
//
// Drop-in usage:
//   1. Create a new Material that uses "Sort/RainbowFlow".
//   2. Assign that Material to Piece prefab's `rainbowMaterial` slot.
//   3. (Optional) drop a custom gradient PNG into _GradientTex and tick _UseGradient.
Shader "Sort/RainbowFlow"
{
    Properties
    {
        _Speed("Speed", Float) = 0.35
        _Scale("Scale (bands per unit)", Float) = 0.7
        _FlowDir("Flow Direction (object space)", Vector) = (1, -1, 0, 0)
        _Saturation("Saturation", Range(0,1)) = 1.0
        _Brightness("Brightness", Range(0,3)) = 1.0
        _ShadeMix("Shade Mix (3D feel)", Range(0,1)) = 0.35
        [NoScaleOffset] _GradientTex("Color Gradient PNG (used when 'Use Gradient' is on)", 2D) = "white" {}
        [Toggle] _UseGradient("Use Gradient PNG (off = procedural HSV)", Float) = 0
        [HideInInspector] _BaseColor("BaseColor (tint, kept neutral)", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0; // pass object-space position so the gradient
                                                // is anchored to the mesh, not the world.
                float3 normalWS    : TEXCOORD1;
            };

            // Texture declared OUTSIDE the cbuffer (URP convention).
            TEXTURE2D(_GradientTex);
            SAMPLER(sampler_GradientTex);

            CBUFFER_START(UnityPerMaterial)
                float  _Speed;
                float  _Scale;
                float4 _FlowDir;
                float  _Saturation;
                float  _Brightness;
                float  _ShadeMix;
                float  _UseGradient;
                float4 _BaseColor;
            CBUFFER_END

            // Standard HSV → RGB (Hue in [0,1], S/V in [0,1]).
            float3 HSVtoRGB(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS  = IN.positionOS.xyz;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // 1. Scalar gradient coord: project object-space position onto the flow direction.
                //    Default _FlowDir = (1,-1,0) → coord rises moving top-left → bottom-right.
                float3 dir = normalize(_FlowDir.xyz);
                float  t   = dot(IN.positionOS, dir) * _Scale + _Time.y * _Speed;

                // 2. Wrap t into [0,1] and use as the sampling coordinate.
                float hue = frac(t);

                // Two color sources, picked by the _UseGradient toggle:
                //   - procedural HSV: full spectrum, no texture needed
                //   - texture lookup: sample the user-supplied PNG along its U axis,
                //     letting artists author exact colors (e.g. only the 10 PieceColors).
                float3 rgbHSV = HSVtoRGB(float3(hue, _Saturation, 1.0));
                float3 rgbTex = SAMPLE_TEXTURE2D(_GradientTex, sampler_GradientTex, float2(hue, 0.5)).rgb;
                // Desaturate texture sample if user dropped _Saturation below 1, to keep that knob meaningful.
                float3 rgbTexSat = lerp(dot(rgbTex, float3(0.299, 0.587, 0.114)).xxx, rgbTex, _Saturation);
                float3 rgb = lerp(rgbHSV, rgbTexSat, _UseGradient);

                // 3. Optional cheap shading so the cube still looks 3D, not stickerlike.
                //    NdotL against the main directional light, mixed in by _ShadeMix.
                Light mainLight = GetMainLight();
                float nDotL = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float shade = lerp(1.0, nDotL * 0.7 + 0.3, _ShadeMix);

                float3 finalRGB = rgb * shade * _Brightness * _BaseColor.rgb;
                return half4(finalRGB, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
