using UnityEngine;

namespace Sort
{
    /// <summary>
    /// World-space equivalent of <see cref="UiRadiantTint"/>: recolors a SpriteRenderer (the world board
    /// frame, placemat, etc.) using a theme slot, via the Sort/SpriteSolidTint shader (value-preserving —
    /// keeps the sprite's light/dark, replaces the hue with a vertical radiant). Reads the level's UI Theme
    /// Palette so it stays in sync with the UI elements that share the slot.
    ///
    /// Attach to the world MainBoard / PlayerHandPlace SpriteRenderer and pick a Slot. LevelLoader refreshes
    /// it on build. (Board indicators are recolored separately by MainBoardBuilder using the same shader.)
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteRadiantTint : MonoBehaviour
    {
        [Tooltip("Theme slot that drives this sprite's radiant (synced with UI elements using the same slot).")]
        [SerializeField] private UiThemeSlot slot = UiThemeSlot.Primary;

        [Tooltip("Use a local custom gradient instead of the theme slot (one-off cases).")]
        [SerializeField] private bool useCustom = false;
        [SerializeField] private GradientColor custom = new GradientColor();

        [Tooltip("Used when there is no current level (e.g. menus).")]
        [SerializeField] private GradientColor fallback = new GradientColor();

        [Tooltip("Brightness lift: multiplies the source's brightness before tinting, so a dark/muted " +
                 "original sprite doesn't make the recolor too dark. 1 = as-is; >1 brightens (e.g. 1.5–2 " +
                 "for a deep-blue board); clamped at full inside the shader.")]
        [Range(0f, 4f)]
        [SerializeField] private float brightness = 1f;

        // NOTE: order must match the shader's _GradientDir (0=Vertical … 4=Radial).
        public enum GradientDirection { Vertical, Horizontal, Angle, Flat, Radial }

        [Tooltip("Radiant direction: Vertical (bottom→top) · Horizontal (left→right) · Angle (custom degrees) " +
                 "· Flat (single = Top color) · Radial (edges + 4 corners → center, like the MainBoard — set " +
                 "Top = border/edge color, Bottom = center color). The sprite's own colors are ignored; the " +
                 "radiant fills the shape, so light/dark comes from the two colors you pick.")]
        [SerializeField] private GradientDirection direction = GradientDirection.Vertical;

        [Tooltip("Angle in degrees when Direction = Angle. 0 = left→right, 90 = bottom→top.")]
        [SerializeField] private float angleDegrees = 90f;

        [Tooltip("How much of the ORIGINAL sprite's depth (bevels/shadows/highlights) to overlay onto the " +
                 "radiant. 0 = flat radiant only (washed/clean); 1 = full original depth & contrast. ~0.7–1 " +
                 "to keep the art's richness while recoloring.")]
        [Range(0f, 1f)]
        [SerializeField] private float detailStrength = 1f;

        SpriteRenderer sr;
        MaterialPropertyBlock mpb;

        // The renderer's authored material (a normal sprite material). Captured before we swap in the
        // flat-replace recolor material, so disabling this component cleanly reverts the sprite to its
        // own colors — e.g. MainBoard showing its finished per-theme sprite from BoardFrame.
        Material originalMat;
        bool capturedOriginal;

        // Shared value-preserving material (Sort/SpriteSolidTint). Per-renderer colors come from the MPB,
        // so one material serves every tinted sprite. Cleared on domain reload.
        static Material _sharedMat;
        static Material SharedMaterial()
        {
            if (_sharedMat == null)
            {
                var sh = Shader.Find("Sort/SpriteSolidTint");
                if (sh != null) _sharedMat = new Material(sh) { name = "SpriteRadiantTint (runtime)" };
            }
            return _sharedMat;
        }

        GradientColor Resolve()
        {
            if (useCustom) return custom ?? (custom = new GradientColor());
            var loader = LevelLoader.Instance;
            var level = loader != null ? loader.CurrentLevel : null;
            return level != null ? level.GetThemeColor(slot) : fallback;
        }

        /// <summary>Re-applies the material + radiant colors. Called by LevelLoader on build, and OnEnable.</summary>
        public void Apply()
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (sr == null) return;

            // Remember the authored material once, so OnDisable can restore the sprite's own look.
            if (!capturedOriginal) { originalMat = sr.sharedMaterial; capturedOriginal = true; }

            var mat = SharedMaterial();
            if (mat != null) sr.sharedMaterial = mat;

            var g = Resolve();
            if (g == null) return;

            if (mpb == null) mpb = new MaterialPropertyBlock();
            sr.GetPropertyBlock(mpb);
            mpb.SetColor("_TopColor", g.top);
            mpb.SetColor("_BottomColor", g.bottom);
            mpb.SetFloat("_Brightness", brightness);
            mpb.SetFloat("_GradientDir", (float)(int)direction);
            mpb.SetFloat("_GradientAngle", angleDegrees);
            mpb.SetFloat("_DetailStrength", detailStrength);
            sr.SetPropertyBlock(mpb);
        }

        void OnEnable() => Apply();

        // Restore the authored material so the sprite shows its own colors again (the finished per-theme
        // board art) instead of staying stuck on the flat-replace recolor material.
        void OnDisable()
        {
            if (sr != null && capturedOriginal) sr.sharedMaterial = originalMat;
        }
    }
}
