// SpriteSolidTint.shader
// Sprite shader that REPLACES the sprite's RGB entirely with the renderer color, keeping ONLY the
// sprite's silhouette (its alpha). Unlike SpriteRenderer's default material — which MULTIPLIES the
// color onto the texture (so a blue sprite tinted white stays blue) — this makes the rendered pixels
// EXACTLY the chosen color: set SpriteRenderer.color (or LevelData.inColor via MainBoardBuilder) to a
// hex and the shape shows up as precisely that color.
//
// Built on Unity's built-in sprite plumbing (UnitySprites.cginc) so it binds _MainTex (PerRendererData),
// _RendererColor (= SpriteRenderer.color), instancing and pixel-snap exactly like Sprites/Default — it
// renders correctly for SpriteRenderers under URP (same path BoardFrame/indicators already use).
//
// Usage: assign this shader's material to a SpriteRenderer, then set _TopColor / _BottomColor (per-renderer
// via a MaterialPropertyBlock) for a vertical radiant. Equal top/bottom = a flat exact color.
Shader "Sort/SpriteSolidTint"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TopColor ("Radiant Top", Color) = (1,1,1,1)
        _BottomColor ("Radiant Bottom", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0, 4)) = 1
        _GradientDir ("Gradient Dir (0=Vert 1=Horiz 2=Angle 3=Flat 4=Radial)", Float) = 0
        _GradientAngle ("Gradient Angle (deg, for Dir=2)", Float) = 90
        _DetailStrength ("Detail from source (0=flat, 1=full depth)", Range(0,1)) = 1
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha   // premultiplied alpha — matches Sprites/Default

        Pass
        {
        CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment SpriteFragSolid
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            fixed4 _TopColor;
            fixed4 _BottomColor;
            fixed _Brightness;
            float _GradientDir;
            float _GradientAngle;
            fixed _DetailStrength;

            // VALUE-PRESERVING recolor: keep the sprite's light/dark structure (its HSV value = max
            // channel) and apply a vertical TOP→BOTTOM radiant — so a blue board recolored to red stays a
            // proper red gradient (dark→light) instead of going flat/black. Set _TopColor == _BottomColor
            // for a flat radiant. The two colors are driven per-renderer via a MaterialPropertyBlock
            // (MainBoardBuilder for indicators, SpriteRadiantTint for the board / placemat).
            fixed4 SpriteFragSolid(v2f IN) : SV_Target
            {
                fixed4 tex = SampleSpriteTexture(IN.texcoord);

                // Gradient parameter t (0→1) along the chosen direction. The sprite's own RGB is IGNORED —
                // the radiant (Bottom→Top) FILLS the shape; the light/dark is encoded in the two colors you
                // pick. Only the sprite's ALPHA (silhouette) is used.
                float t;
                if      (_GradientDir < 0.5) t = IN.texcoord.y;          // Vertical (bottom→top)
                else if (_GradientDir < 1.5) t = IN.texcoord.x;          // Horizontal (left→right)
                else if (_GradientDir < 2.5)                            // Angle (custom)
                {
                    float rad = radians(_GradientAngle);
                    t = dot(IN.texcoord - 0.5, float2(cos(rad), sin(rad))) + 0.5;
                }
                else if (_GradientDir < 3.5) t = 1.0;                   // Flat (single = Top color)
                else                                                    // Radial: edges/corners → center
                {
                    // Distance from the sprite center, normalized so the center = 0 (Bottom color) and the
                    // edges + 4 corners = 1 (Top color) — color radiates inward from the frame, like the
                    // MainBoard. Use Top = border/edge color, Bottom = center color.
                    t = saturate(length(IN.texcoord - 0.5) * 2.0);
                }

                fixed4 grad = lerp(_BottomColor, _TopColor, saturate(t));

                // OVERLAY the source's luminance detail (bevels / shadows / highlights) onto the flat
                // radiant so the recolor keeps the ORIGINAL's depth & contrast instead of looking washed
                // out. Overlay keeps the radiant's overall brightness (no darkening like a plain multiply):
                // where the source is mid-gray the radiant shows as-is; brighter source → highlight, darker
                // → shadow. _DetailStrength blends it: 0 = flat radiant, 1 = full original depth.
                fixed  srcV  = max(tex.r, max(tex.g, tex.b));
                fixed3 baseC = grad.rgb;
                fixed3 lo    = 2.0 * baseC * srcV;
                fixed3 hi    = 1.0 - 2.0 * (1.0 - baseC) * (1.0 - srcV);
                fixed3 ov    = lerp(lo, hi, step(0.5, baseC));
                fixed3 col   = lerp(baseC, ov, _DetailStrength) * _Brightness;

                fixed4 c;
                c.rgb = col;
                c.a   = tex.a * grad.a;          // shape comes from the sprite's alpha
                c.rgb *= c.a;                    // premultiply for Blend One OneMinusSrcAlpha
                return c;
            }
        ENDCG
        }
    }

    Fallback "Sprites/Default"
}
