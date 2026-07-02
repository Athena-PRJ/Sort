using UnityEngine;
using UnityEngine.Rendering;

namespace Sort
{
    /// <summary>
    /// Turns a 3D mesh (the "Platform") into the game BACKGROUND: a lit plane tinted by the level's theme
    /// that — unlike the UI background Image — is a real 3D surface, so it RECEIVES the board's and pieces'
    /// shadows.
    ///
    /// It does NOT touch the transform at all — position, rotation AND scale are entirely yours. It only
    /// applies the theme background colour, receives shadows, and disables the collider. Colour follows the
    /// level's Background Tint from the UI Theme Palette (the same value BackgroundFrame uses), so it
    /// re-themes per level automatically.
    ///
    /// Put it on the Platform object and disable the old UI Background. Uses URP/Lit (always in builds).
    /// Keep the platform within the RP asset's Shadow Distance (45) of the camera so it catches shadows.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class LitBackgroundFill : MonoBehaviour
    {
        [Tooltip("Use the current level's Background Tint from the UI Theme Palette (same source as the UI " +
                 "background). Off = use the manual Colour below.")]
        [SerializeField] private bool useThemeTint = true;
        [Tooltip("Manual colour — used when Use Theme Tint is off, or before a level has loaded.")]
        [SerializeField] private Color color = new Color(0.53f, 0.54f, 0.58f, 1f);
        [Range(0f, 1f)] [SerializeField] private float smoothness = 0.05f;
        [Tooltip("Receive shadows — the whole point. Leave ON.")]
        [SerializeField] private bool receiveShadows = true;

        MeshRenderer mr;
        Material mat;
        Color lastColor;

        void OnEnable() { Setup(); ApplyColor(); }

        void OnDisable() { if (mat != null) { Destroy(mat); mat = null; } }

        void Setup()
        {
            if (mr == null) mr = GetComponent<MeshRenderer>();
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;   // a background shouldn't block taps

            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(sh) { name = "LitBackground (runtime)" };
            }
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;   // background never casts
            mr.receiveShadows = receiveShadows;
        }

        Color ThemeColor()
        {
            if (useThemeTint)
            {
                var loader = LevelLoader.Instance;
                if (loader != null && loader.CurrentLevel != null) return loader.CurrentLevel.BackgroundTint;
            }
            return color;
        }

        void LateUpdate()
        {
            // Re-apply only when the theme colour changes (e.g. a new level loads). No transform work.
            if (ThemeColor() != lastColor) ApplyColor();
        }

        void ApplyColor()
        {
            Setup();
            Color c = ThemeColor();
            if (mat != null) mat.SetColor("_BaseColor", c);
            lastColor = c;
        }
    }
}
