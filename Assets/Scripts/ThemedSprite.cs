using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Swaps an Image's (or SpriteRenderer's) sprite to the CURRENT level's per-theme art for a given role,
    /// so the same panel/button shows different finished art per difficulty (Easy / Hard / Very Hard…) with
    /// ZERO per-element code. The art lives on <see cref="UiThemePalette.themedSprites"/> (keyed by
    /// <see cref="UiThemeSprite"/>); <see cref="LevelData.themeSet"/> picks which palette.
    ///
    /// Usage: put this on each themed Image (You Failed board/title/Retry button, Out of Moves board/title/
    /// buttons, …), pick its <see cref="key"/>, and assign that sprite per theme in every UiThemePalette.
    /// Adding a new themed element = add a UiThemeSprite enum value + assign it per palette + drop this on the
    /// element. No new fields, no new scripts.
    ///
    /// Applies in OnEnable (panels read the theme the moment they're shown mid-level) and via LevelLoader's
    /// refresh on build (for elements already visible at load).
    /// </summary>
    public class ThemedSprite : MonoBehaviour
    {
        [Tooltip("Which per-theme sprite role this element shows. Sprites are assigned per theme on each " +
                 "UiThemePalette (themedSprites list). Add to the UiThemeSprite enum for new roles.")]
        [SerializeField] private UiThemeSprite key = UiThemeSprite.OutOfMovesAdd;

        [Tooltip("Target UI Image. Auto-found on this GameObject if left null.")]
        [SerializeField] private Image targetImage;
        [Tooltip("Target world SpriteRenderer (use instead of Image for world objects). Auto-found if null.")]
        [SerializeField] private SpriteRenderer targetRenderer;

        void Awake()
        {
            if (targetImage == null) targetImage = GetComponent<Image>();
            if (targetRenderer == null) targetRenderer = GetComponent<SpriteRenderer>();
        }

        void OnEnable() => Apply();

        /// <summary>Reads the current level's theme and applies this role's sprite. Called by LevelLoader on
        /// build and on OnEnable. No-op if the theme has no sprite for this key (leaves the authored art).</summary>
        public void Apply()
        {
            if (targetImage == null && targetRenderer == null)
            {
                targetImage = GetComponent<Image>();
                targetRenderer = GetComponent<SpriteRenderer>();
            }

            var loader = LevelLoader.Instance;
            var palette = loader != null && loader.CurrentLevel != null ? loader.CurrentLevel.themeSet : null;
            if (palette == null) return;

            var sprite = palette.GetThemedSprite(key);
            if (sprite == null) return;   // role not assigned for this theme → keep the authored sprite

            if (targetImage != null) targetImage.sprite = sprite;
            if (targetRenderer != null) targetRenderer.sprite = sprite;
        }
    }
}
