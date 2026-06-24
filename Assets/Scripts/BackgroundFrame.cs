using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Per-level background image. Reads the sprite from LevelData.backgroundSprite (or the
    /// LevelLoader fallback) at runtime and applies it to its UI Image component. Attach this
    /// to the Background GameObject (child of Canvas) that has an Image component.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class BackgroundFrame : MonoBehaviour
    {
        [Tooltip("Shown when the level's LevelData has no backgroundSprite assigned. Each level should set " +
                 "its own backgroundSprite; this is just a last-resort placeholder.")]
        [SerializeField] private Sprite fallbackSprite;

        Image image;

        void Awake()
        {
            image = GetComponent<Image>();
        }

        /// <summary>
        /// Re-applies the sprite from the current level. Called by LevelLoader.ApplyAutoFit.
        /// Safe to call again if the level changes mid-session.
        /// </summary>
        public void Apply()
        {
            if (image == null) image = GetComponent<Image>();
            if (image == null) return;

            // Per-theme background sprite (LevelData.themeSet.backgroundSprite). If the theme provides one,
            // use it; else keep the scene sprite; else fall back. Each theme ships its own finished background.
            var loader = LevelLoader.Instance;
            Sprite chosen = loader != null && loader.CurrentLevel != null ? loader.CurrentLevel.BackgroundSprite : null;
            if (chosen != null) image.sprite = chosen;
            else if (image.sprite == null && fallbackSprite != null) image.sprite = fallbackSprite;

            // If a GrayscaleTint is present, drive its colour from the theme so a B&W background gets
            // recolored per level/difficulty (white tint = no recolor).
            var tint = GetComponent<GrayscaleTint>();
            if (tint != null && loader != null && loader.CurrentLevel != null)
                tint.SetColor(loader.CurrentLevel.BackgroundTint);
        }
    }
}
