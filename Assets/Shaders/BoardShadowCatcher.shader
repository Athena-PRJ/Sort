// A transparent "shadow catcher": renders NOTHING where the surface is lit and a soft dark tint only where
// the main directional light's realtime shadow falls on it. Laid over the board sprite by BoardShadowCatcher.cs
// so the pieces' shadows appear ON the board face while the board art (the sprite behind) still shows through.
// Opaque pieces (which write depth) occlude it via ZTest, so pieces never darken themselves.
Shader "Sort/BoardShadowCatcher"
{
    Properties
    {
        _Color    ("Shadow Color", Color) = (0,0,0,1)
        _Strength ("Shadow Strength", Range(0,1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "ShadowCatch"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Needed so GetMainLight(shadowCoord) actually samples the shadow map (+ soft filtering).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half  _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                // shadowAttenuation: 1 = fully lit, 0 = fully shadowed → alpha rises only in shadow.
                half a = saturate((1.0h - mainLight.shadowAttenuation) * _Strength) * _Color.a;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
