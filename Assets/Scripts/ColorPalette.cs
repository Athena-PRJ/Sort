using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Which palette variant a level wants to use. The PrefabRegistry entry for each prefab
    /// holds BOTH a Pastel palette and a Plain palette; LevelData picks one of these styles
    /// and LevelLoader resolves it to the matching palette at spawn time.
    /// </summary>
    public enum PaletteStyle
    {
        Pastel,
        Plain
    }

    /// <summary>
    /// A palette = a designer-defined LIST of colors, each a NAME + a BaseMap texture (plus a shared
    /// ambient-occlusion map). The color identity in the game is the NAME (a string); there is no fixed
    /// color enum — each prefab decides how many / which colors exist by editing this list.
    ///
    /// The names entered here are exactly what appear:
    ///   • in LevelData's color dropdowns (piece color, Lock/Only-Stack required color) via [PaletteColor]
    ///   • on the pieces in-game (the BaseMap is swapped onto the piece material)
    ///
    /// Two palettes are assigned per prefab in PrefabRegistry (one Pastel + one Plain). For a level to
    /// be style-swappable, give both palettes the SAME set of names (different textures, same names).
    ///
    /// Workflow:
    ///   1. Create → Sort → Color Palette (e.g. "BoxPastelPalette").
    ///   2. Add one entry per color: type a Name (e.g. "Crimson") + drop its BaseMap PNG.
    ///   3. Optionally set the shared AO map.
    ///   4. Assign the palette to the prefab's Pastel/Plain slot in PrefabRegistry.
    /// </summary>
    [CreateAssetMenu(menuName = "Sort/Color Palette", fileName = "ColorPalette")]
    public class ColorPalette : ScriptableObject
    {
        [Tooltip("The colors this palette defines — one entry per color (Name + BaseMap). The names " +
                 "become the options in LevelData's color dropdowns; the BaseMap is what the piece shows " +
                 "in-game. Add as many as this prefab needs; a name with no BaseMap still lists but renders " +
                 "via the tint-the-default-material fallback.")]
        public List<ColorTextureEntry> entries = new List<ColorTextureEntry>();

        [Tooltip("Optional shared AO map sampled INTO _OcclusionMap on URP/Lit shaders. Bakes the " +
                 "prefab's surface detail without affecting color — same AO applies to every color " +
                 "variant. Leave null to skip AO override (material's authored _OcclusionMap stays).")]
        public Texture2D ambientOcclusionMap;

        [Tooltip("AO strength when ambientOcclusionMap is assigned. 0 = no occlusion (flat), 1 = full strength.")]
        [Range(0f, 1f)] public float ambientOcclusionStrength = 1f;

        /// <summary>
        /// Returns the BaseMap texture for the color named <paramref name="colorName"/>, or null if this
        /// palette has no entry with that name. Linear scan — palettes are small.
        /// </summary>
        public Texture2D GetBaseMap(string colorName)
        {
            if (entries == null || string.IsNullOrEmpty(colorName)) return null;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].name == colorName) return entries[i].baseMap;
            return null;
        }

        /// <summary>All distinct, non-empty color names this palette defines. Used by the [PaletteColor] editor dropdown.</summary>
        public List<string> ColorNames()
        {
            var list = new List<string>();
            if (entries == null) return list;
            for (int i = 0; i < entries.Count; i++)
            {
                var n = entries[i].name;
                if (!string.IsNullOrEmpty(n) && !list.Contains(n)) list.Add(n);
            }
            return list;
        }
    }

    /// <summary>One row in the palette: a designer-named color and its BaseMap texture.</summary>
    [System.Serializable]
    public struct ColorTextureEntry
    {
        [Tooltip("Designer-defined color name (e.g. 'Crimson', 'Ocean'). This exact string is the color's " +
                 "identity — it's what LevelData stores and what the dropdown shows. Keep names consistent " +
                 "between this prefab's Pastel and Plain palettes so levels can switch style.")]
        public string name;

        [Tooltip("BaseMap texture applied to a piece of this color.")]
        public Texture2D baseMap;
    }
}
