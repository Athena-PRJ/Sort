using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Spawns the per-column STATUS UI around the gameplay grid: a "not-done → done + tick" icon ABOVE
    /// each column and an "out" arrow BELOW each column. Sprites come from the active LevelData.
    ///
    /// The board renders in a TILTED 3D plane (≈85° on X) and is auto-fit per level, so the icon
    /// placement + size are derived RELATIVE TO THE BOARD'S ACTUAL EDGES at build time — they adapt to
    /// any grid (2x2, 4x4, …) automatically. The tunable numbers below are FRACTIONS of the board's
    /// height (so they stay proportional), plus the in-plane rotation. Only X is auto-aligned per column.
    ///
    /// The board's Board-local vertical extent is read from THIS MainBoard's sprite: localScale.y ×
    /// sprite.height (the X≈90° board rotation maps the sprite's height onto Board-local Z). Indicators
    /// parent to the BOARD (uniform F) so MainBoard's non-uniform image-fit scale doesn't distort them.
    /// </summary>
    public class MainBoardBuilder : MonoBehaviour
    {
        [Header("Icon rotation (lie in the board plane)")]
        [Tooltip("Local rotation (Euler°) so icons lie FLAT in the tilted board plane and face the camera. " +
                 "Match the board tilt — ≈ (85, 0, 0) on the standard board.")]
        [SerializeField] private Vector3 indicatorRotation = new Vector3(85f, 0f, 0f);

        [Header("Size — fraction of the board's height (auto-scales per grid)")]
        [Tooltip("DONE-frame size (the status slot). This frame is ALWAYS shown and is the container the " +
                 "not-done icon and the tick sit inside. Bumped up a touch from the old 0.05.")]
        [SerializeField] private float doneSizeFraction = 0.06f;
        [Tooltip("Inner NOT-DONE icon size — sits INSIDE the done frame while the column is unsolved. " +
                 "Keep ≤ Done Size Fraction so it fits within the frame.")]
        [SerializeField] private float notDoneSizeFraction = 0.045f;
        [Tooltip("Inner TICK icon size — sits inside the done frame once the column is solved.")]
        [SerializeField] private float tickSizeFraction = 0.03f;
        [Tooltip("Bottom OUT arrow size as a fraction of the board's height.")]
        [SerializeField] private float indicatorSizeFraction = 0.05f;

        [Header("Placement — fractions of board height (auto-adapt per grid)")]
        [Tooltip("Gap of the TOP status icon from the board's top edge, as a fraction of board height. " +
                 "0 = right at the edge, + = further up, - = slightly onto the board.")]
        [SerializeField] private float statusEdgeGapFraction = 0f;
        [Tooltip("Gap of the BOTTOM out arrow from the board's bottom edge, as a fraction of board height. " +
                 "0 = right at the edge, + = further below.")]
        [SerializeField] private float outEdgeGapFraction = 0f;
        [Tooltip("How far the icons lift toward the camera (Board-local Y) so they render in front of the " +
                 "board, as a fraction of board height. Small positive (~0.1) is plenty.")]
        [SerializeField] private float liftFraction = 0.1f;
        [Tooltip("Manual position nudge for the DONE status frame, in Board-local units (type values here): " +
                 "X = sideways, Y = toward the camera, Z = along the tilted board (up/down). Lets you fine-" +
                 "tune the slot's placement on top of the auto-computed position.")]
        [SerializeField] private Vector3 statusIconOffset = Vector3.zero;

        [Header("Column spacing")]
        [Tooltip("Horizontal spacing between indicators (column → column). 0 = AUTO: matches " +
                 "Board.ColumnSpacing so each icon sits over its column. Set > 0 to override.")]
        [SerializeField] private float columnSpacingOverride = 0f;

        [Header("Sorting")]
        [Tooltip("SpriteRenderer sorting order (tick draws at +1 so it sits over the done icon).")]
        [SerializeField] private int indicatorSortingOrder = 10;

        float cellWidth;
        Transform indicatorParent;
        readonly List<GameObject> spawned = new();
        int lastCols, lastRows;
        bool dirty;

        void Start()
        {
            var level = LevelLoader.Instance != null ? LevelLoader.Instance.CurrentLevel : null;
            if (level == null || level.columns == null || level.columns.Length == 0) return;

            int cols = level.columns.Length;
            int rows = 0;
            foreach (var c in level.columns)
                if (c != null && c.pieces != null && c.pieces.Length > rows) rows = c.pieces.Length;
            if (rows == 0) return;

            Build(cols, rows);
        }

        // Live tuning: rebuild when a serialized value changes during Play.
        void OnValidate() { if (Application.isPlaying) dirty = true; }
        void Update() { if (dirty && lastCols > 0) { dirty = false; Build(lastCols, lastRows); } }
        [ContextMenu("Rebuild")] void RebuildContext() { if (lastCols > 0) Build(lastCols, lastRows); }

        public void Build(int cols, int rows)
        {
            lastCols = cols;
            lastRows = rows;
            Clear();

            var board = GetComponentInParent<Board>();
            // Parent to the Board (uniform F) so MainBoard's non-uniform image-fit scale doesn't distort spacing.
            indicatorParent = board != null ? board.transform : transform;
            cellWidth = columnSpacingOverride > 0f ? columnSpacingOverride
                      : (board != null && board.ColumnSpacing > 0f ? board.ColumnSpacing : 2.7f);

            var level   = LevelLoader.Instance != null ? LevelLoader.Instance.CurrentLevel : null;
            var columns = GameManager.Instance != null ? GameManager.Instance.Columns : null;
            if (level == null) return;

            // --- Board's actual Board-local vertical (Z) extent + center, read at build time so the
            // icons track the board no matter the grid size / which MB sprite the level uses. ---
            var boardSR = GetComponent<SpriteRenderer>();
            float spriteH = (boardSR != null && boardSR.sprite != null) ? boardSR.sprite.bounds.size.y : 1f;
            float boardExtentZ = Mathf.Abs(transform.localScale.y) * spriteH;       // height mapped to Board-local Z (board tilted ≈90°)
            if (boardExtentZ < 1e-3f) boardExtentZ = rows * cellWidth;              // fallback if sprite not ready
            float boardCenterZ = transform.localPosition.z;
            float topEdgeZ = boardCenterZ + boardExtentZ * 0.5f;
            float botEdgeZ = boardCenterZ - boardExtentZ * 0.5f;

            float statusZ = topEdgeZ + boardExtentZ * statusEdgeGapFraction;
            float outZ    = botEdgeZ - boardExtentZ * outEdgeGapFraction;
            float lift    = boardExtentZ * liftFraction;
            float doneScale    = boardExtentZ * doneSizeFraction;
            float notDoneScale = boardExtentZ * notDoneSizeFraction;
            float tickS        = boardExtentZ * tickSizeFraction;
            float outScale     = boardExtentZ * indicatorSizeFraction;

            float leftX = -((cols - 1) * cellWidth) * 0.5f;
            Quaternion rot = Quaternion.Euler(indicatorRotation);

            for (int c = 0; c < cols; c++)
            {
                float x = leftX + c * cellWidth;

                // Top status: the DONE sprite is the always-visible FRAME (the slot). The not-done icon
                // sits INSIDE it while the column is unsolved; on lock ColumnIndicator hides not-done and
                // shows the tick. The inner icons share the frame's position so they read as "inside" it.
                Sprite frameSprite = level.doneSprite != null ? level.doneSprite : level.notDoneSprite;
                if (frameSprite != null)
                {
                    var sPos = new Vector3(x, lift, statusZ) + statusIconOffset;
                    var frameGO = Spawn(frameSprite, sPos, rot, new Vector3(doneScale, doneScale, 1f), indicatorSortingOrder);

                    // Inner not-done — only when there's a distinct done frame for it to sit inside.
                    GameObject notDoneGO = null;
                    if (level.doneSprite != null && level.notDoneSprite != null)
                        notDoneGO = Spawn(level.notDoneSprite, sPos, rot, new Vector3(notDoneScale, notDoneScale, 1f), indicatorSortingOrder + 1);

                    // Inner tick (shown on lock).
                    GameObject tickGO = null;
                    if (level.tickSprite != null)
                    {
                        tickGO = Spawn(level.tickSprite, sPos, rot, new Vector3(tickS, tickS, 1f), indicatorSortingOrder + 1);
                        tickGO.SetActive(false);
                    }

                    var ind = frameGO.AddComponent<ColumnIndicator>();
                    Column col = (columns != null && c < columns.Count) ? columns[c] : null;
                    ind.Setup(col, notDoneGO, tickGO);
                }

                // Bottom out arrow.
                if (level.outSprite != null)
                {
                    var oPos = new Vector3(x, lift, outZ);
                    Spawn(level.outSprite, oPos, rot, new Vector3(outScale, outScale, 1f), indicatorSortingOrder);
                }
            }
        }

        GameObject Spawn(Sprite sprite, Vector3 localPos, Quaternion localRot, Vector3 localScale, int order)
        {
            var go = new GameObject("BoardIndicator");
            go.transform.SetParent(indicatorParent != null ? indicatorParent : transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;

            spawned.Add(go);
            return go;
        }

        void Clear()
        {
            foreach (var go in spawned)
            {
                if (go == null) continue;
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
            spawned.Clear();
        }
    }
}
