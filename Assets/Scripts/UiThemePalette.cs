using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Keys for per-theme UI sprites (finished art that swaps by difficulty — NOT recolored). Add a value
    /// here when you introduce a new themed UI element, then assign its sprite per theme in each
    /// UiThemePalette and put a <see cref="ThemedSprite"/> with this key on the Image/SpriteRenderer.
    /// </summary>
    public enum UiThemeSprite
    {
        OutOfMovesAdd,   // "Add 10 moves to keep playing" text (per difficulty)
        OutOfMovesAds,   // "Play on (Ads)" button
        OutOfMovesX,     // Out-of-Moves panel close button
        LoseStageRetry,  // "Retry" button
        LoseStageFailed, // "You Failed!" text
        LoseStageX,      // You-Failed panel close button
        // More roles to be added later as needed.
    }

    /// <summary>One per-theme sprite, keyed by <see cref="UiThemeSprite"/>. Serializable for the list below.</summary>
    [System.Serializable]
    public class ThemedSpriteEntry
    {
        public UiThemeSprite key;
        public Sprite sprite;
    }

    /// <summary>
    /// A named UI COLOR theme ("Bộ màu") — the 5 radiant slots used to recolor UI via UiRadiantTint
    /// (and the board indicators). Create several (e.g. "Bộ màu Normal", "Hard", "Very Hard") via
    /// Assets → Create → Sort → UI Theme Palette; a LevelData just PICKS one (LevelData.themeSet) and
    /// every element using a slot re-colors in sync.
    ///
    /// Holds per-theme SPRITES for the board + background (these recolor poorly via radiant, so each theme
    /// supplies its own finished art) PLUS the 5 radiant COLOR slots (for everything that DOES recolor well
    /// via UiRadiantTint / indicators). Other UI (badges/buttons) stays fixed art recolored by slot.
    /// </summary>
    [CreateAssetMenu(menuName = "Sort/UI Theme Palette", fileName = "UiTheme")]
    public class UiThemePalette : ScriptableObject
    {
        [Header("Per-theme sprites (finished art — not recolored)")]
        [Tooltip("Board frame sprite for this theme. BoardFrame applies it to the MainBoard SpriteRenderer. " +
                 "Leave null to keep the renderer's current sprite / BoardFrame fallback.")]
        public Sprite mainBoardSprite;
        [Tooltip("Background image for this theme. BackgroundFrame applies it to the Background UI Image. " +
                 "Leave null to keep the image's current sprite / BackgroundFrame fallback.")]
        public Sprite backgroundSprite;

        [Tooltip("Tint for the MAIN BOARD when it has a GrayscaleTint component (use a B&W board sprite + this " +
                 "per-theme colour to recolor it). White = no recolor. Paste a hex into the picker.")]
        public Color mainBoardTint = Color.white;
        [Tooltip("Tint for the BACKGROUND when it has a GrayscaleTint component (B&W background + this colour). " +
                 "White = no recolor.")]
        public Color backgroundTint = Color.white;

        [Tooltip("Per-theme UI sprites keyed by role (You Failed / Out of Moves / Win panel art, per " +
                 "difficulty). A ThemedSprite component on each Image/SpriteRenderer reads the sprite for " +
                 "its key from THIS theme. Add an entry per role you use; missing keys leave the art as-is.")]
        public List<ThemedSpriteEntry> themedSprites = new List<ThemedSpriteEntry>();

        [Header("Radiant color slots (recolored via UiRadiantTint / indicators)")]
        [Tooltip("IN + OUT radiant — In frames + Out arrows (board status indicators).")]
        public GradientColor primary = new GradientColor();
        [Tooltip("NOT DONE radiant — the Not Done marker inside the In frame while a column is unsolved.")]
        public GradientColor secondary = new GradientColor();
        [Tooltip("ACCENT radiant — SkillBar / action UI.")]
        public GradientColor accent = new GradientColor();
        [Tooltip("TEXT radiant.")]
        public GradientColor thirdary = new GradientColor();
        [Tooltip("BACKUP radiant — spare slot.")]
        public GradientColor backup = new GradientColor();

        /// <summary>Returns this theme's sprite for the given role, or null if not assigned.</summary>
        public Sprite GetThemedSprite(UiThemeSprite key)
        {
            if (themedSprites == null) return null;
            for (int i = 0; i < themedSprites.Count; i++)
                if (themedSprites[i] != null && themedSprites[i].key == key)
                    return themedSprites[i].sprite;
            return null;
        }

        /// <summary>Returns the radiant for a slot (never null — auto-creates a white default if missing).</summary>
        public GradientColor Get(UiThemeSlot slot)
        {
            switch (slot)
            {
                case UiThemeSlot.Secondary: return secondary ?? (secondary = new GradientColor());
                case UiThemeSlot.Accent:    return accent    ?? (accent    = new GradientColor());
                case UiThemeSlot.Thirdary:  return thirdary  ?? (thirdary  = new GradientColor());
                case UiThemeSlot.Backup:    return backup    ?? (backup    = new GradientColor());
                default:                    return primary   ?? (primary   = new GradientColor());
            }
        }
    }
}
