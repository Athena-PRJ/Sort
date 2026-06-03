using UnityEngine;

namespace Sort
{
    public enum PieceColor
    {
        Red, Blue, Green, Yellow, Orange, Purple, Pink, Cyan, Black, White
    }

    /// <summary>
    /// Difficulty tag shown on the Level badge. Dev picks one per level in LevelData.
    /// The enum name (e.g. "SuperHard") is what gets displayed unless a custom format is used.
    /// </summary>
    public enum LevelDifficulty
    {
        Easy,
        Normal,
        Hard,
        SuperHard,
        Expert
    }

    // PieceTheme enum was removed in Phase 2 of the Prefab Registry refactor — per-level prefab
    // selection now lives on LevelData.piecePrefab (a direct prefab reference sourced from
    // PrefabRegistry). See PrefabRegistry.cs for the catalog model.

    public static class PieceColors
    {
        /// <summary>
        /// Maps each PieceColor to its canonical sRGB color. These hex values match the file
        /// naming in Assets/Texture/ImgAO/Box_Lego_#XXXXXX.png so the in-game tint stays in sync
        /// with the AO-rendered art.
        /// </summary>
        public static Color ToUnityColor(this PieceColor c)
        {
            switch (c)
            {
                case PieceColor.Red:    return new Color32(0xCE, 0x14, 0x37, 0xFF); // #CE1437
                case PieceColor.Blue:   return new Color32(0x14, 0x6D, 0xCE, 0xFF); // #146DCE
                case PieceColor.Green:  return new Color32(0x4E, 0xB0, 0x1E, 0xFF); // #4EB01E
                case PieceColor.Yellow: return new Color32(0xEE, 0xC3, 0x1B, 0xFF); // #EEC31B
                case PieceColor.Orange: return new Color32(0xCE, 0x75, 0x14, 0xFF); // #CE7514
                case PieceColor.Purple: return new Color32(0x49, 0x14, 0xCE, 0xFF); // #4914CE
                case PieceColor.Pink:   return new Color32(0xA3, 0x14, 0xCE, 0xFF); // #A314CE (magenta-leaning)
                case PieceColor.Cyan:   return new Color32(0x5E, 0xB1, 0xFF, 0xFF); // #5EB1FF
                case PieceColor.Black:  return new Color32(0x00, 0x00, 0x00, 0xFF); // #000000
                case PieceColor.White:  return new Color32(0xFF, 0xFF, 0xFF, 0xFF); // #FFFFFF
                default:                return UnityEngine.Color.white;
            }
        }
    }
}
