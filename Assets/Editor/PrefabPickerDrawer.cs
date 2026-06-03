using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sort.EditorTools
{
    /// <summary>
    /// Drawer for <see cref="PrefabPickerAttribute"/>. Renders a dropdown sourced from the
    /// project's <see cref="PrefabRegistry"/> asset. The number of options equals the number
    /// of valid entries in the registry — designers cannot pick a prefab outside the catalog,
    /// which is the whole point of the refactor.
    ///
    /// Edge cases handled:
    /// - No registry asset found → field disabled with "(No PrefabRegistry asset found)" label.
    /// - Registry exists but has 0 entries → field disabled with "(PrefabRegistry has no entries)".
    /// - Current value not in registry (stale) → dropdown shown red + "⚠ stale" tag.
    /// - Wrong field type (non-GameObject) → fallback message; doesn't crash.
    /// </summary>
    [CustomPropertyDrawer(typeof(PrefabPickerAttribute))]
    public class PrefabPickerDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                EditorGUI.LabelField(position, label, new GUIContent("[PrefabPicker] only works on GameObject fields."));
                return;
            }

            var registry = LoadFirstRegistry();
            if (registry == null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.LabelField(position, label, new GUIContent("(No PrefabRegistry asset found)"));
                return;
            }

            // Collect prefabs from registry entries (skip null piecePrefab entries).
            var prefabs = new List<GameObject>();
            for (int i = 0; i < registry.entries.Length; i++)
            {
                var p = registry.entries[i].piecePrefab;
                if (p != null) prefabs.Add(p);
            }

            if (prefabs.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.LabelField(position, label, new GUIContent("(PrefabRegistry has no entries)"));
                return;
            }

            // Find current selection (by reference).
            var currentValue = property.objectReferenceValue as GameObject;
            int currentIndex = -1;
            string[] options = new string[prefabs.Count];
            for (int i = 0; i < prefabs.Count; i++)
            {
                options[i] = prefabs[i].name;
                if (prefabs[i] == currentValue) currentIndex = i;
            }

            // Stale = value exists but isn't in registry anymore (registry was edited after LevelData was set).
            bool isStale = currentValue != null && currentIndex < 0;
            var renderedLabel = isStale
                ? new GUIContent(label.text + "  ⚠ stale",
                                 $"'{currentValue.name}' is no longer in PrefabRegistry. Pick a current entry " +
                                 "or add this prefab back to the registry.")
                : label;

            Color savedColor = GUI.contentColor;
            if (isStale) GUI.contentColor = new Color(1f, 0.55f, 0.55f);

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.Popup(position, renderedLabel.text, currentIndex, options);
            if (EditorGUI.EndChangeCheck() && picked >= 0 && picked < prefabs.Count)
            {
                property.objectReferenceValue = prefabs[picked];
            }

            GUI.contentColor = savedColor;
        }

        /// <summary>
        /// Loads the first PrefabRegistry asset found via AssetDatabase. Multiple registries are
        /// not supported — keep one canonical registry per project. AssetDatabase search runs at
        /// edit-time only; doesn't ship in build.
        /// </summary>
        static PrefabRegistry LoadFirstRegistry()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(PrefabRegistry));
            if (guids == null || guids.Length == 0) return null;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<PrefabRegistry>(path);
        }
    }
}
