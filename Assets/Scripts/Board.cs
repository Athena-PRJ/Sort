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
        [Tooltip("Distance between adjacent columns along the local X axis.")]
        [SerializeField] private float columnSpacing = 1.6f;

        [Tooltip("If true, also clamps column local Y and Z to zero. If false, designer can offset each column manually.")]
        [SerializeField] private bool flattenYZ = true;

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

            float total = (cols.Count - 1) * columnSpacing;
            float start = -total * 0.5f;

            for (int i = 0; i < cols.Count; i++)
            {
                var pos = cols[i].localPosition;
                pos.x = start + i * columnSpacing;
                if (flattenYZ) { pos.y = 0; pos.z = 0; }
                cols[i].localPosition = pos;
            }
        }
    }
}
