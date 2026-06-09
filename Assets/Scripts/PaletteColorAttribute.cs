using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Mark a <c>string</c> color field on a <see cref="LevelData"/> to get a dropdown of the color
    /// NAMES the level's resolved palette defines (the prefab's Pastel/Plain palette for the current
    /// PaletteStyle). The designer picks a name instead of typing it; the stored value is that name.
    ///
    /// The drawer lives in Assets/Editor/PaletteColorDrawer.cs and is editor-only. At runtime the field
    /// is a plain string (the color identity), so this attribute has zero gameplay effect — it only
    /// changes how the field is drawn in the Inspector.
    ///
    /// Usage:
    ///   [PaletteColor] public string color;
    /// </summary>
    public class PaletteColorAttribute : PropertyAttribute { }
}
