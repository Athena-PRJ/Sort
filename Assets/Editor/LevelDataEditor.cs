#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sort.EditorTools
{
    /// <summary>
    /// Custom inspector for LevelData. Draws the default fields, then shows a validation
    /// summary so designers see immediately whether their level is solvable.
    /// </summary>
    [CustomEditor(typeof(LevelData))]
    public class LevelDataEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            var data = (LevelData)target;
            var result = data.Validate();

            // Summary block always shown so designers see the current shape of the level.
            var summary = new StringBuilder();
            summary.AppendLine($"Columns: {result.columnCount}");
            summary.AppendLine($"Rows per column: {result.rowCount}");
            summary.AppendLine($"Required per color: ≥ {result.rowCount}");
            if (result.rainbowCount > 0)
                summary.AppendLine($"Rainbow pieces: {result.rainbowCount}");
            if (result.colorCounts != null && result.colorCounts.Count > 0)
            {
                summary.Append("Color counts: ");
                bool first = true;
                foreach (var kv in result.colorCounts)
                {
                    if (!first) summary.Append(",  ");
                    summary.Append($"{kv.Key} = {kv.Value}");
                    first = false;
                }
            }
            else
            {
                summary.Append("Color counts: <none>");
            }
            EditorGUILayout.HelpBox(summary.ToString(), MessageType.Info);

            if (result.IsValid)
            {
                EditorGUILayout.HelpBox("Level is VALID — solvable.", MessageType.Info);
            }
            else
            {
                foreach (var err in result.errors)
                    EditorGUILayout.HelpBox(err, MessageType.Error);

                EditorGUILayout.HelpBox(
                    $"To make this level valid:\n" +
                    $"  • Use exactly {result.columnCount} distinct non-Rainbow colors.\n" +
                    $"  • Each of those colors must appear ≥ {result.rowCount} times across columns + the held piece.\n" +
                    $"  • Questionmarks count as their underlying color. Rainbow pieces don't count.\n" +
                    $"  • A column can't be both a Frozen (Break Wall) and a Lock Color Stack column.\n" +
                    $"  • Gated columns must be unlockable by completing others (no freeze deadlock).\n" +
                    $"  • An Only Stack Sort column's color must exist, and no more columns can be restricted\n" +
                    $"    to a color than the level can make of it.",
                    MessageType.Warning);
            }

            // Warnings (non-blocking) — shown whether or not the level is valid.
            if (result.warnings != null)
                foreach (var warn in result.warnings)
                    EditorGUILayout.HelpBox(warn, MessageType.Warning);

            if (GUILayout.Button("Re-validate"))
            {
                // Forces a redraw with fresh results — useful after editing without changing fields.
                Repaint();
            }
        }
    }
}
#endif
