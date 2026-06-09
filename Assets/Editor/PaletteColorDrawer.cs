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
        /// Color names offered in the dropdown = the UNION of the prefab's Pastel + Plain palette names.
        /// The two palettes are meant to share the same color NAMES (just different textures), so naming
        /// EITHER one populates the dropdown for BOTH styles — the designer names a color once. Keep the
        /// names consistent across the two palettes so a style swap always finds a matching texture.
        /// </summary>
        static List<string> ResolveColorNames(SerializedProperty property)
        {
            var level = property.serializedObject.targetObject as LevelData;
            if (level == null || level.piecePrefab == null) return null;

            var registry = LoadFirstRegistry();
            if (registry == null) return null;
            if (!registry.TryGetEntry(level.piecePrefab, out var entry)) return null;

            var names = new List<string>();
            AddNames(names, entry.palettePastel);
            AddNames(names, entry.palettePlain);
            return names;
        }

        static void AddNames(List<string> into, ColorPalette palette)
        {
            if (palette == null) return;
            foreach (var n in palette.ColorNames())
                if (!into.Contains(n)) into.Add(n);
        }

        static PrefabRegistry LoadFirstRegistry()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(PrefabRegistry));
            if (guids == null || guids.Length == 0) return null;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<PrefabRegistry>(path);
        }
    }
}
