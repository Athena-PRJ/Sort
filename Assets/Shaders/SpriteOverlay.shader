// SpriteOverlay.shader
// A SpriteRenderer shader identical to the built-in "Sprites/Default" EXCEPT it bakes
// ZTest Always + Queue "Overlay" into the pass — so the sprite ALWAYS renders on top of the
// 3D pieces (which write depth) regardless of how close/far it sits. Used by FrozenOverlay's
// ice-strip: a normal sprite material would ZTest LEqual against the pieces and get occluded.
//
// Reuses Unity's UnitySprites.cginc (SpriteVert/SpriteFrag), so it fully supports SpriteRenderer
// features — vertex color, flip, pixel-snap, AND the 9-slice / tiled draw modes (the SpriteRenderer
// generates the sliced geometry; this shader just rasterizes it on top of everything).
//
// Build note: assigned at runtime via Shader.Find in FrozenOverlay → add to
// Project Settings → Graphics → Always Included Shaders so it survives device builds.
Shader "Sort/SpriteOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Overlay"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always               // <-- the whole point: ignore depth, draw over the 3D pieces
        Blend One OneMinusSrcAlpha  // premultiplied in SpriteFrag (matches Sprites/Default)

        Pass
        {
        CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment SpriteFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
        ENDCG
        }
    }
}
