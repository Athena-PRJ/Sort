using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Auto-positions Column children along the local X axis with even spacing,
    /// centered around the Board's origin. Re-runs in the editor whenever children change.
    /// </summary>
    [DisallowMultipleComponent]
    public class Board : MonoBehaviour
    {
        [Tooltip("If true, also clamps column local Y and Z to zero. If false, designer can offset each column manually.")]
        [SerializeField] private bool flattenYZ = true;

        // Last-resort fallback when no PrefabRegistry entry pushes a runtime override. Designers
        // are expected to tune columnSpacing per-prefab via PrefabRegistry.entries[*].columnSpacing,
        // not here. This const exists so a scene without a registry assigned still lays out
        // SOMETHING reasonable instead of stacking every column at x=0.
        const float DEFAULT_COLUMN_SPACING = 1.6f;

        // Non-serialized runtime override applied by LevelLoader from the active PrefabRegistry entry.
        // <= 0 means "no override active" → ColumnSpacing falls back to DEFAULT_COLUMN_SPACING.
        // Always resets to 0 on script reload, so the saved scene never carries an auto-fit value.
        [System.NonSerialized] float runtimeColumnSpacing = 0f;

        /// <summary>Live spacing for layout — runtime override if set, otherwise the internal fallback const.</summary>
        public float ColumnSpacing => runtimeColumnSpacing > 0f ? runtimeColumnSpacing : DEFAULT_COLUMN_SPACING;

        /// <summary>
        /// Applies a per-level cell-width override from the active PrefabRegistry entry.
        /// Pass 0 (or negative) to clear the override and fall back to the internal default.
        /// </summary>
        public void SetRuntimeColumnSpacing(float spacing)
        {
            runtimeColumnSpacing = spacing;
            Layout();
        }

        void Start() => Layout();

        void OnValidate()
        {
            // Fires when any serialized field is changed in the Inspector — including during Play mode.
            Layout();
        }

        void OnTransformChildrenChanged()
        {
            // Only auto-layout in the editor; at runtime LevelLoader builds children explicitly.
            if (!Application.isPlaying) Layout();
        }

        public void Layout()
        {
            var cols = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i).GetComponent<Column>() != null)
                    cols.Add(transform.GetChild(i));
            }
            if (cols.Count == 0) return;

            float spacing = ColumnSpacing;
            float total = (cols.Count - 1) * spacing;
            float start = -total * 0.5f;

            for (int i = 0; i < cols.Count; i++)
            {
                var pos = cols[i].localPosition;
                pos.x = start + i * spacing;
                if (flattenYZ) { pos.y = 0; pos.z = 0; }
                cols[i].localPosition = pos;
            }
        }
    }
}
