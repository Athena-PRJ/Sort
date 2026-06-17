using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Applies a radiant (gradient) tint to ANY UI Graphic (Image, SVGImage, TMP) as a vertex-color
    /// gradient. Designed for the layered-UI workflow: put one on each art layer (fill / stroke / glyph …)
    /// and configure its color independently.
    ///
    /// Flexible color sourcing for different cases:
    ///   • Color Source = ThemeSlot → pulls from the level's UI Theme Palette (synced across every element
    ///     using that slot; edit the palette, all update).
    ///   • Color Source = Custom → uses this component's OWN gradient (a one-off color, independent of the
    ///     theme) for special cases that shouldn't follow the shared palette.
    /// Plus: gradient DIRECTION (vertical / horizontal / angle / flat) and STRENGTH (how strongly to tint).
    ///
    /// IMPORTANT: this is a vertex-color MULTIPLY (tint), NOT a replace — on light/white base art it matches
    /// the chosen color closely; on dark/colored art it tints toward it. (Board SpriteRenderers get an EXACT
    /// replace via Sort/SpriteSolidTint; Canvas UI shares the theme COLORS, not the exact rendering.)
    /// </summary>
    public class UiRadiantTint : BaseMeshEffect
    {
        public enum ColorSource { ThemeSlot, Custom }
        public enum GradientDirection { Vertical, Horizontal, Angle, Flat }

        [Header("Color source")]
        [Tooltip("ThemeSlot = pull the color from the level's UI Theme Palette (synced, edited in one place). " +
                 "Custom = use THIS component's own gradient below, independent of the theme (one-off cases).")]
        [SerializeField] private ColorSource source = ColorSource.ThemeSlot;

        [Tooltip("Theme slot used when Source = ThemeSlot.")]
        [SerializeField] private UiThemeSlot slot = UiThemeSlot.Accent;

        [Tooltip("Local gradient used when Source = Custom — independent of any theme palette.")]
        [SerializeField] private GradientColor custom = new GradientColor();

        [Tooltip("Used when Source = ThemeSlot but no level/theme is loaded (e.g. menus).")]
        [SerializeField] private GradientColor fallback = new GradientColor();

        [Header("Gradient shape")]
        [Tooltip("Vertical = top→bottom · Horizontal = left→right · Angle = along Angle Degrees · " +
                 "Flat = a single solid color (the gradient's Top).")]
        [SerializeField] private GradientDirection direction = GradientDirection.Vertical;

        [Tooltip("Gradient angle in degrees when Direction = Angle. 0 = left→right, 90 = bottom→top.")]
        [Range(0f, 360f)]
        [SerializeField] private float angleDegrees = 90f;

        [Tooltip("How strongly the tint is applied. 0 = no tint (original art), 1 = full tint.")]
        [Range(0f, 1f)]
        [SerializeField] private float strength = 1f;

        [Header("Recolor material (optional)")]
        [Tooltip("Assign the 'Sort/UI Radiant Recolor' material here and this component sets it on the " +
                 "graphic AUTOMATICALLY — so you don't have to find each Image/SVGImage's Material field. " +
                 "Leave null to keep the graphic's current material (default UI multiply).\n\n" +
                 "NOTE: value-preserving recolor reads a per-pixel texture, so it works on a UI IMAGE whose " +
                 "sprite is RASTER/textured. An SVGImage vector fill has no texture → it will show a flat tint.")]
        [SerializeField] private Material recolorMaterial;

        GradientColor ResolveColor()
        {
            if (source == ColorSource.Custom)
                return custom ?? (custom = new GradientColor());

            var loader = LevelLoader.Instance;
            var level = loader != null ? loader.CurrentLevel : null;
            return level != null ? level.GetThemeColor(slot) : fallback;
        }

        /// <summary>Re-pulls the color and rebuilds the mesh. Called by LevelLoader on build, and OnEnable.</summary>
        public void Apply()
        {
            if (graphic != null) graphic.SetVerticesDirty();
        }

        /// <summary>Assigns the recolor material to this graphic so you don't have to set the Image/SVGImage
        /// Material field by hand. No-op if none assigned (keeps the graphic's current material).</summary>
        void ApplyMaterial()
        {
            if (recolorMaterial != null && graphic != null && graphic.material != recolorMaterial)
                graphic.material = recolorMaterial;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ApplyMaterial();
            Apply();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            ApplyMaterial();
            Apply();   // live-preview tweaks in the editor
        }
#endif

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;

            var g = ResolveColor();
            if (g == null || strength <= 0f) return;

            int count = vh.currentVertCount;
            if (count == 0) return;

            // Pass 1: bounds for whichever axis the direction needs (X, Y, or a projection along the angle).
            float rad = angleDegrees * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minP = float.MaxValue, maxP = float.MinValue;

            var v = new UIVertex();
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var p = v.position;
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                float proj = p.x * dir.x + p.y * dir.y;
                if (proj < minP) minP = proj; if (proj > maxP) maxP = proj;
            }
            float rangeX = Mathf.Max(1e-5f, maxX - minX);
            float rangeY = Mathf.Max(1e-5f, maxY - minY);
            float rangeP = Mathf.Max(1e-5f, maxP - minP);

            // Pass 2: tint each vertex.
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var p = v.position;

                float t;
                switch (direction)
                {
                    case GradientDirection.Horizontal: t = (p.x - minX) / rangeX; break;
                    case GradientDirection.Angle:      t = (p.x * dir.x + p.y * dir.y - minP) / rangeP; break;
                    case GradientDirection.Flat:       t = 1f; break;   // single color = gradient Top
                    default:                           t = (p.y - minY) / rangeY; break; // Vertical
                }

                Color tint   = g.Sample(t);
                Color tinted = v.color * tint;
                // Blend by strength so partial tints are possible (0 = original, 1 = full tint).
                v.color = strength >= 1f ? tinted : Color.Lerp(v.color, tinted, strength);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
