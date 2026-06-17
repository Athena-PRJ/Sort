using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sort.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="UiThemePalette"/> that relabels the radiant color slots to the
    /// designer-facing names (In + Out / Not Done / Accent / Text / Backup) without renaming the
    /// underlying fields — so no data migration and all code keeps working. Everything else (sprites)
    /// draws with its normal label.
    /// </summary>
    [CustomEditor(typeof(UiThemePalette))]
    public class UiThemePaletteEditor : Editor
    {
        // field name → pretty label shown in the inspector.
        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "primary",   "In + Out" },
            { "secondary", "Not Done" },
            { "accent",    "Accent" },
            { "thirdary",  "Text" },
            { "backup",    "Backup" },
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var prop = serializedObject.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (prop.name == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(prop);
                    continue;
                }

                if (Labels.TryGetValue(prop.name, out var pretty))
                    EditorGUILayout.PropertyField(prop, new GUIContent(pretty, prop.tooltip), true);
                else
                    EditorGUILayout.PropertyField(prop, true);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
