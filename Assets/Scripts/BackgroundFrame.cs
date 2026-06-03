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
        [Tooltip("Shown when LevelData has no backgroundSprite assigned AND LevelLoader has no defaultBackgroundSprite either.")]
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

            var loader = LevelLoader.Instance;
            Sprite chosen = loader != null && loader.CurrentLevel != null ? loader.CurrentLevel.backgroundSprite : null;
            if (chosen == null && loader != null) chosen = loader.DefaultBackgroundSprite;
            if (chosen == null) chosen = fallbackSprite;

            if (chosen != null) image.sprite = chosen;
        }
    }
}
