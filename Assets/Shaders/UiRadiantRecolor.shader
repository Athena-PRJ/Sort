// UiRadiantRecolor.shader
// A UI (Canvas) shader that RECOLORS a sprite while KEEPING its light/dark structure: it desaturates the
// source to grayscale (using its HSV value = max channel) and then multiplies the tint color in. So a
// blue→red recolor stays a proper red gradient (dark areas dark, light areas light) instead of going black
// like a plain multiply does (red × blue ≈ 0).
//
// Pairs with UiRadiantTint: that component still drives the COLOR (it writes the radiant gradient into the
// mesh's vertex color). This shader just changes the blend math from "tex × vertexColor" (the default UI
// multiply) to "value(tex) × vertexColor". Both together = value-preserving radiant recolor.
//
// Usage:
//   1. Create a Material → set its Shader to "Sort/UI Radiant Recolor".
//   2. On each UI Image/SVGImage you want recolored, set its Material field to that material.
//   3. Keep UiRadiantTint on the element (it supplies the tint color / gradient).
//   One shared material is fine — the per-element color comes from each element's vertex colors.
//
// Note: needs per-pixel shading to read, so it works on RASTER sprites (incl. SVGs imported as textured
// sprites). A pure-vector SVG fill (no texture, flat color) has no shading to preserve → shows flat tint.
Shader "Sort/UI Radiant Recolor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0, 4)) = 1

        // Standard UI plumbing (stencil / mask / color mask) so it behaves like UI-Default under masks.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "Transparent"
            "IgnoreProjector"  = "True"
            "RenderType"       = "Transparent"
            "PreviewType"      = "Plane"
            "CanUseSpriteAtlas"= "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _Brightness;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;   // v.color = the radiant gradient from UiRadiantTint
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;

                // Desaturate the source to its HSV "value" (max channel) = its light/dark structure,
                // independent of the original hue. Then apply the tint → keeps shading, replaces color.
                fixed value = max(tex.r, max(tex.g, tex.b)) * _Brightness;

                fixed4 color;
                color.rgb = saturate(value) * IN.color.rgb;
                color.a   = tex.a * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
