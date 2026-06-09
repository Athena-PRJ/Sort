// TieOverlay.shader
// URP-compatible unlit shader for the tie X-shape sprites. The pass has ZTest Always +
// ZWrite Off + Cull Off baked directly into the SubShader, so the tie quads ALWAYS render
// on top of pieces regardless of depth buffer state — no per-material configuration needed.
//
// Why a dedicated shader instead of just setting _ZTest on URP/Unlit at runtime:
//   Material._ZTest only takes effect if the shader exposes the property AND uses it via
//   `ZTest [_ZTest]` in the pass directive. Some Unity/URP versions don't reliably propagate
//   the property to the rasterizer state, causing the change to silently no-op. Baking the
//   state into the shader pass guarantees the GPU receives `Always` regardless of material
//   tweaks downstream.
//
// Drop-in usage:
//   1. Open mat_tie_up.mat, mat_tie_down.mat, mat_tie_up_crack.mat, mat_tie_down_crack.mat
//   2. Change Shader from "Universal Render Pipeline/Unlit" to "Sort/TieOverlay"
//   3. The Base Map texture you assigned stays — the property name is identical
//   4. Press Play — tie now renders on top of every piece, every angle, every grid size
Shader "Sort/TieOverlay"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color (tinted by alpha for fade)", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }
        LOD 100

        Pass
        {
            Name "TieOverlay"
            // BAKED render state — no [_PropertyName] indirection, so material setters can't
            // override these. Always renders on top, never writes depth, both faces visible.
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            // Texture declared OUTSIDE the cbuffer (URP convention).
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Sample the tie sprite (e.g. "tie up.png") and multiply by base color. Alpha
                // channel comes from BOTH the texture's alpha AND _BaseColor.a — so the Phase C
                // crack-fade (which lowers _BaseColor.a via MaterialPropertyBlock) works on top
                // of the sprite's own transparency cutouts.
                half4 texCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                return texCol * _BaseColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
