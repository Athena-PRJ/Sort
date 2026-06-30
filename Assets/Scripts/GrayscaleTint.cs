using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Recolors GRAYSCALE (black-and-white) art by a single colour you pick — paste a hex into the colour
    /// picker (e.g. FDF5DC or 28378C). Works on a world <see cref="SpriteRenderer"/> (Background / MainBoard)
    /// or a UGUI <see cref="Image"/>.
    ///
    /// SpriteRenderer + Preserve Detail = ON → uses the value-preserving <c>Sort/SpriteSolidTint</c> shader
    /// (keeps the art's light/dark, just replaces the hue), so even a DARK colour keeps the bevels readable.
    /// Otherwise it's a plain colour multiply (white→colour, black→black). Updates live in the Editor
    /// (ExecuteAlways), so you can dial colours in without entering Play.
    ///
    /// NOTE: for builds, add <c>Sort/SpriteSolidTint</c> to Project Settings ▸ Graphics ▸ Always Included
    /// Shaders (it's referenced by string at runtime).
    /// </summary>
    [ExecuteAlways]
    public class GrayscaleTint : MonoBehaviour
    {
        [Tooltip("Tint colour. Paste a hex (FDF5DC, 28378C…) into the picker's Hexadecimal field.")]
        [SerializeField] private Color color = Color.white;

        [Tooltip("SpriteRenderer only: keep the art's light/dark detail (value-preserving recolor) instead of " +
                 "a flat multiply. Recommended — a multiply by a dark colour buries the detail.")]
        [SerializeField] private bool preserveDetail = true;

        [Tooltip("How much of the source art's light/dark detail to keep (Preserve Detail mode). " +
                 "1 = full detail, 0 = flat colour.")]
        [Range(0f, 1f)] [SerializeField] private float detailStrength = 1f;

        [Tooltip("Brightens the source before tinting so a dark original doesn't come out too dark (Preserve " +
                 "Detail mode). 1 = as-is.")]
        [Range(0f, 4f)] [SerializeField] private float brightness = 1f;

        static Material _shared;
        SpriteRenderer sr;
        Image img;
        MaterialPropertyBlock mpb;
        Material originalMat;
        bool capturedOriginal;

        void OnEnable() => Apply();
        void OnValidate() { if (isActiveAndEnabled) Apply(); }
        void OnDisable() { if (sr != null && capturedOriginal) sr.sharedMaterial = originalMat; }

        /// <summary>Set the tint colour and re-apply. Called by BoardFrame / BackgroundFrame with the
        /// current theme's per-board / per-background tint so the recolor follows the level theme.</summary>
        public void SetColor(Color c) { color = c; Apply(); }

        /// <summary>Re-applies the tint. Call after changing <see cref="color"/> from code.</summary>
        public void Apply()
        {
            // UGUI Image → plain colour multiply (the sprite/UI shader has no value-preserving path).
            if (img == null) img = GetComponent<Image>();
            if (img != null) { img.color = color; return; }

            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (sr == null) return;

            if (!preserveDetail)
            {
                if (capturedOriginal) sr.sharedMaterial = originalMat;   // revert any recolor material
                sr.color = color;                                        // simple multiply
                return;
            }

            var sh = Shader.Find("Sort/SpriteSolidTint");
            if (sh == null) { sr.color = color; return; }                // shader missing → fall back to multiply
            if (_shared == null || _shared.shader != sh)
                _shared = new Material(sh) { name = "GrayscaleTint (runtime)" };

            if (!capturedOriginal) { originalMat = sr.sharedMaterial; capturedOriginal = true; }
            sr.sharedMaterial = _shared;
            sr.color = Color.white;                                      // neutral; the shader uses _TopColor

            if (mpb == null) mpb = new MaterialPropertyBlock();
            sr.GetPropertyBlock(mpb);
            mpb.SetColor("_TopColor", color);
            mpb.SetColor("_BottomColor", color);
            mpb.SetFloat("_GradientDir", 3f);      // Flat (single colour) — see SpriteRadiantTint.GradientDirection
            mpb.SetFloat("_DetailStrength", detailStrength);
            mpb.SetFloat("_Brightness", brightness);
            sr.SetPropertyBlock(mpb);
        }
    }
}
