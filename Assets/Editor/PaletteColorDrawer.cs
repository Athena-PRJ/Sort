using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sort.EditorTools
{
    /// <summary>
    /// Drawer for <see cref="PaletteColorAttribute"/>. Renders a dropdown of the color NAMES the level's
    /// resolved palette defines (the level's prefab palette — Pastel or Plain per
    /// <see cref="LevelData.paletteStyle"/>). The field is a <c>string</c> = the chosen color name.
    ///
    /// Edge cases:
    /// - No LevelData context / no prefab / no registry / empty palette → falls back to a plain text
    ///   field so the level stays editable.
    /// - Current value not among the palette names (stale / empty) → highlighted + tagged, not auto-changed.
    /// - Non-string field → plain PropertyField; never crashes.
    /// </summary>
    [CustomPropertyDrawer(typeof(PaletteColorAttribute))]
    public class PaletteColorDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            List<string> names = ResolveColorNames(property);

            if (names == null || names.Count == 0)
            {
                // No palette context yet → don't trap the designer; show a normal text field.
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            string current = property.stringValue;
            int currentIndex = names.IndexOf(current);

            var options = names.ToArray();
            bool missing = currentIndex < 0; // empty or a name not in this palette
            string text = missing && !string.IsNullOrEmpty(current)
                ? label.text + "  ⚠ '" + current + "' not in palette"
                : label.text;

            Color saved = GUI.contentColor;
            if (missing) GUI.contentColor = new Color(1f, 0.7f, 0.4f);

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.Popup(position, text, currentIndex, options);
            if (EditorGUI.EndChangeCheck() && picked >= 0 && picked < names.Count)
                property.stringValue = names[picked];

            GUI.contentColor = saved;
        }

        /// <summary>
        /// Color names offered in the dropdown = ONLY the palette matching the level's current
        /// <see cref="LevelData.paletteStyle"/> (Pastel → palettePastel, Plain → palettePlain). The two
        /// palettes can define different color sets, so the dropdown must reflect just the active style —
        /// switching Palette Style live re-filters the list. Defensive fallback: if the selected style's
        /// palette is unassigned or empty, fall back to the OTHER one so the designer isn't trapped with
        /// an empty dropdown (normal levels assign both, so this only triggers on a half-set-up prefab).
        /// </summary>
        static List<string> ResolveColorNames(SerializedProperty property)
        {
            var level = property.serializedObject.targetObject as LevelData;
            if (level == null || level.piecePrefab == null) return null;

            var registry = RegistryCache.Registry;   // cached — no per-repaint AssetDatabase scan
            if (registry == null) return null;
            if (!registry.TryGetEntry(level.piecePrefab, out var entry)) return null;

            ColorPalette selected = level.paletteStyle == PaletteStyle.Plain ? entry.palettePlain : entry.palettePastel;
            ColorPalette other    = level.paletteStyle == PaletteStyle.Plain ? entry.palettePastel : entry.palettePlain;

            var names = new List<string>();
            AddNames(names, selected);
            // Only show the other style's colors if the selected one gave us nothing (avoids trapping).
            if (names.Count == 0) AddNames(names, other);
            return names;
        }

        static void AddNames(List<string> into, ColorPalette palette)
        {
            if (palette == null) return;
            foreach (var n in palette.ColorNames())
                if (!into.Contains(n)) into.Add(n);
        }
    }
}
