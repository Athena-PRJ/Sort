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
        // Cache the validation result so Validate() (loops + dictionaries + freeze reachability) runs
        // ONLY when a field actually changes — not on every inspector repaint (mouse-move, hover, etc.).
        // Re-running it every repaint was a needless cost on top of the per-field drawer work.
        LevelData.ValidationResult _cached;

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            var data = (LevelData)target;
            if (changed || _cached == null) _cached = data.Validate();
            var result = _cached;

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
                    $"  • The non-Rainbow colors must fill all {result.columnCount} columns. A color fills " +
                    $"(count ÷ {result.rowCount}) WHOLE columns and ONE color may fill several columns " +
                    $"(e.g. 2 orange + 2 green + 1 white = 5).\n" +
                    $"  • Give each color a count in multiples of {result.rowCount} so it forms whole columns; " +
                    $"the totals across all colors must add up to {result.columnCount} columns.\n" +
                    $"  • Questionmarks count as their underlying color. Rainbow pieces don't count (they're the leftover).\n" +
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
                // Force a fresh Validate() on next draw — useful after editing without changing fields
                // (e.g. you edited a referenced palette/registry asset).
                _cached = null;
                Repaint();
            }
        }
    }
}
#endif
