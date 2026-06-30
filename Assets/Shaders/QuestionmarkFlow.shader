Shader "Sort/QuestionmarkFlow"
{
    // Tiles a single "?" sprite into a grid where EACH symbol is rotated by a RANDOM angle (up to
    // _MaxAngle, e.g. 135°) for a scattered/tilted look, and the whole field SCROLLS continuously along a
    // diagonal. The scatter is a RIGID wallpaper that just slides: each "?" keeps its OWN fixed angle for
    // the entire level (angle = hash of its cell, which travels with it), and a per-piece seed makes every
    // piece's wallpaper unique. Edges are kept CRISP via fwidth alpha-sharpening (no mip mush). Transparent
    // + always-on-top so it overlays a hidden-Questionmark piece. Assign to mat_qm_overlay; put the ? sprite
    // in Base Map (single centred "?" with a transparent margin; Wrap Mode = Repeat).
    Properties
    {
        [MainTexture] _BaseMap ("Base Map (single ? sprite)", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1,1,1,1)
        _Tiling   ("Tiling (X,Y count across the piece)", Vector) = (3,4,0,0)
        _Spacing  ("Symbol size within each cell (smaller = more room to rotate)", Range(0.1,1)) = 0.6
        _MaxAngle ("Max random rotation per symbol (degrees)", Range(0,180)) = 135
        _FlowDir  ("Scroll direction (diagonal)", Vector) = (1,-1,0,0)
        _Speed    ("Scroll speed", Float) = 0.12
        _SeedScale ("Per-piece randomize (0 = all pieces identical)", Range(0,1)) = 1
        _AlphaCutoff ("Edge cutoff (alpha-sharpen pivot)", Range(0,1)) = 0.5
        _EdgeSoftness ("Edge softness (lower = crisper)", Range(0.2,3)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                float4 _Tiling;
                float  _Spacing;
                float  _MaxAngle;
                float4 _FlowDir;
                float  _Speed;
                float  _SeedScale;
                float  _AlphaCutoff;
                float  _EdgeSoftness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float2 seed : TEXCOORD1; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                // Per-piece seed from the object's world-space origin (column 3 of the model matrix).
                // Each piece sits at a different world position → a different, fixed scatter pattern,
                // so the "?" no longer rotate in lockstep across pieces.
                float3 worldOrigin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                OUT.seed = worldOrigin.xy;
                return OUT;
            }

            // Stable per-cell pseudo-random in [0,1).
            float hash (float2 cell)
            {
                return frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Slide the whole rigid wallpaper diagonally over time. Because the angle below is keyed to
                // cellId in this SCROLLED space, each "?" carries its own fixed angle as it drifts — the
                // orientations never re-roll, the field just moves.
                float2 dir = length(_FlowDir.xy) > 1e-4 ? normalize(_FlowDir.xy) : float2(0.7071, -0.7071);
                float2 uv  = IN.uv * _Tiling.xy + dir * (_Time.y * _Speed);

                float2 cellId = floor(uv);
                float2 local  = frac(uv) - 0.5;   // centred in the cell

                // Decorrelate each piece: offset the hash input by a per-piece seed so two pieces never
                // show the same orientation pattern. Scaled by a non-integer so neighbouring pieces (often
                // exactly 1 unit apart) don't alias onto each other's cell ids.
                float2 seedOff = IN.seed * 113.37 * _SeedScale;

                // Each cell gets its own fixed random rotation (up to _MaxAngle) → scattered/tilted look.
                float ang = hash(cellId + seedOff) * radians(_MaxAngle);
                float si = sin(ang), co = cos(ang);
                float2 r = float2(local.x * co - local.y * si, local.x * si + local.y * co);

                // Spacing: shrink the symbol within its cell (leaves room so a rotated ? doesn't clip).
                float invSpacing = 1.0 / max(0.1, _Spacing);
                r *= invSpacing;
                float2 c = r + 0.5;

                // Crisp sampling: frac(uv) jumps at every cell boundary, which poisons the GPU's automatic
                // ddx/ddy and collapses to the blurriest mip along those seams. Derive the texture-coordinate
                // gradients analytically from the CONTINUOUS uv (rotated + scaled the same way as `local`) and
                // sample with an explicit gradient so mip selection is correct → no seam blur.
                float2 dudx = ddx(uv), dudy = ddy(uv);
                float2 dcdx = float2(dudx.x * co - dudx.y * si, dudx.x * si + dudx.y * co) * invSpacing;
                float2 dcdy = float2(dudy.x * co - dudy.y * si, dudy.x * si + dudy.y * co) * invSpacing;
                half4 tex = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, c, dcdx, dcdy);
                float inside = step(0.0, c.x) * step(c.x, 1.0) * step(0.0, c.y) * step(c.y, 1.0);

                // De-blur: re-crisp the glyph edge. Whatever mip the GPU picked (soft when minified), remap
                // the alpha so the 0.5 contour becomes a hard edge that's only ~1px wide on screen. This
                // keeps the "?" sharp even while scrolling, instead of the muddy minified look.
                float aw = max(fwidth(tex.a) * _EdgeSoftness, 1e-5);
                float a  = saturate((tex.a - _AlphaCutoff) / aw + 0.5);

                half4 col;
                col.rgb = tex.rgb * _BaseColor.rgb;
                col.a   = a * _BaseColor.a * inside;
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
