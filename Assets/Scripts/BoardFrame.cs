using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Single-sprite board background. Reads the sprite from LevelData.mainBoardSprite (or the
    /// LevelLoader fallback) and applies it to its SpriteRenderer.
    ///
    /// This component does NOT touch transform — Board.transform's uniform scale (set by
    /// LevelLoader's auto-fit) automatically propagates to this GameObject since it's a child.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class BoardFrame : MonoBehaviour
    {
        [Tooltip("Shown when the level's LevelData has no mainBoardSprite assigned. Each level should set " +
                 "its own mainBoardSprite; this is just a last-resort placeholder.")]
        [SerializeField] private Sprite fallbackSprite;

        [Tooltip("Optional sorting order so the frame draws behind pieces.")]
        [SerializeField] private int sortingOrder = -10;

        SpriteRenderer sr;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// Ensures draw mode is correct and that there's a sprite to measure for auto-fit. The board art
        /// is now FIXED in the scene (recolored per level via UiRadiantTint), so this no longer swaps the
        /// sprite per level — it only fills in <see cref="fallbackSprite"/> if the renderer has none.
        /// Called by LevelLoader before the board auto-fit (which reads this renderer's bounds).
        /// </summary>
        public void Apply()
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (sr == null) return;

            // Per-theme board sprite (LevelData.themeSet.mainBoardSprite). If the theme provides one, use it;
            // else keep the scene-assigned sprite; else fall back. This lets each theme ship finished board art.
            var loader = LevelLoader.Instance;
            Sprite chosen = loader != null && loader.CurrentLevel != null ? loader.CurrentLevel.MainBoardSprite : null;
            if (chosen != null) sr.sprite = chosen;
            else if (sr.sprite == null && fallbackSprite != null) sr.sprite = fallbackSprite;

            // Always Simple draw mode so the GameObject's localScale drives the rendered size.
            sr.drawMode = SpriteDrawMode.Simple;

            // If a GrayscaleTint is present, drive its colour from the theme so a B&W board sprite gets
            // recolored per level/difficulty (white tint = no recolor).
            var tint = GetComponent<GrayscaleTint>();
            if (tint != null && loader != null && loader.CurrentLevel != null)
                tint.SetColor(loader.CurrentLevel.MainBoardTint);
        }
    }
}
