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
        [Tooltip("Shown when LevelData has no mainBoardSprite assigned AND LevelLoader has no defaultMainBoardSprite.")]
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
        /// Refreshes the sprite from the current LevelData (with fallback chain). Called by
        /// LevelLoader.BuildLevel. Safe to call again if the level changes mid-session.
        /// </summary>
        public void Apply()
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (sr == null) return;

            var loader = LevelLoader.Instance;
            Sprite chosen = loader != null && loader.CurrentLevel != null ? loader.CurrentLevel.mainBoardSprite : null;
            if (chosen == null && loader != null) chosen = loader.DefaultMainBoardSprite;
            if (chosen == null) chosen = fallbackSprite;
            sr.sprite = chosen;

            // Always use Simple draw mode so the GameObject's localScale (authored by designer)
            // fully drives the rendered size. Parent Board.transform handles per-level scaling.
            sr.drawMode = SpriteDrawMode.Simple;
        }
    }
}
