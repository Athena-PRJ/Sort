using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Procedurally builds the decorative board as a frame around the piece grid, plus per-column
    /// indicators (not-done base + tick overlay) above and 'out' arrows below.
    ///
    /// Each part type is sized independently so the frame fits any grid size:
    ///   - center      : per-cell fill (its size = the gameplay column/piece spacing)
    ///   - top/bottom  : horizontal borders (own thickness, one per column)
    ///   - left/right  : vertical borders — 1 'first' cap + many 'middle' tiles + 1 'last' cap
    ///   - corners     : own size
    ///   - indicators  : own size; tick layers on top of not-done
    ///
    /// NOTE: 'place' (the slot behind the held piece) is NOT part of this board — place it manually
    /// under the HandAnchor. The board cells use 'center'.
    ///
    /// Lives as a child of the Board so it inherits the auto-fit scale.
    /// </summary>
    public class MainBoardBuilder : MonoBehaviour
    {
        [Header("Frame sprites")]
        [SerializeField] private Sprite topLeftCorner;
        [SerializeField] private Sprite topRightCorner;
        [SerializeField] private Sprite bottomLeftCorner;
        [SerializeField] private Sprite bottomRightCorner;
        [SerializeField] private Sprite topEdge;
        [SerializeField] private Sprite bottomEdge;
        [SerializeField] private Sprite leftFirst;
        [SerializeField] private Sprite leftMiddle;
        [SerializeField] private Sprite leftLast;
        [SerializeField] private Sprite rightFirst;
        [SerializeField] private Sprite rightMiddle;
        [SerializeField] private Sprite rightLast;
        [Tooltip("Per-cell fill behind the pieces.")]
        [SerializeField] private Sprite center;

        [Header("Indicator sprites")]
        [Tooltip("Shown while the column is unsolved.")]
        [SerializeField] private Sprite notDoneSprite;
        [Tooltip("Replaces not-done (same size) when the column is solved.")]
        [SerializeField] private Sprite doneSprite;
        [Tooltip("Tick overlaid on top of 'done' when the column is solved.")]
        [SerializeField] private Sprite tickSprite;
        [SerializeField] private Sprite outSprite;

        [Header("Cell size")]
        [Tooltip("If true, cellWidth/cellHeight are read from Board.columnSpacing and the first Column's pieceSpacing at build time. Keep this ON unless you have a specific reason to override.")]
        [SerializeField] private bool autoSyncCellSize = true;
        [Tooltip("Manual override — used only when Auto Sync Cell Size is OFF. When auto-sync is on, these stay informational only.")]
        [SerializeField] private float cellWidth = 2.7f;
        [SerializeField] private float cellHeight = 3.8f;

        [Header("Outline boost (scale parts up + pull inward)")]
        [Tooltip("Multiplies every frame part's rendered size. >1 makes all outlines thicker (parts overlap).")]
        [SerializeField] private float partScale = 1f;
        [Tooltip("Pulls every border part (edges + corners) toward the board center, so scaling them up doesn't enlarge the overall board.")]
        [SerializeField] private float borderInset = 0.2f;

        [Header("Top / bottom border thickness")]
        [SerializeField] private float topHeight = 1.6f;
        [SerializeField] private float bottomHeight = 2.4f;

        [Header("Side border (1 first + many middle + 1 last)")]
        [SerializeField] private float sideWidth = 1.77f;
        [SerializeField] private float sideFirstHeight = 1.0f;
        [SerializeField] private float sideMiddleHeight = 1.0f;
        [SerializeField] private float sideLastHeight = 1.0f;

        [Header("Corners")]
        [SerializeField] private float cornerWidth = 1.88f;
        [SerializeField] private float cornerHeight = 1.88f;
        [Tooltip("Sorting order for corners. Higher than the frame so the oversized corners draw over the edges and hide seams.")]
        [SerializeField] private int cornerSortingOrder = -8;

        [Header("Indicators")]
        [SerializeField] private float indicatorWidth = 1.88f;
        [SerializeField] private float indicatorHeight = 1.88f;
        [SerializeField] private float indicatorAboveGap = 0.2f;
        [SerializeField] private float outBelowGap = 0.2f;
        [Tooltip("Tick overlay size — smaller than the indicator so the not-done outline still shows around it.")]
        [SerializeField] private float tickWidth = 1.2f;
        [SerializeField] private float tickHeight = 1.2f;

        [Header("Depth & sorting")]
        [SerializeField] private float frameZ = 0.3f;
        [SerializeField] private float indicatorZ = -0.1f;
        [SerializeField] private int frameSortingOrder = -10;
        [SerializeField] private int indicatorSortingOrder = 10;

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

        // --- Live tuning -------------------------------------------------------

        void OnValidate() { if (Application.isPlaying) dirty = true; }

        void Update()
        {
            if (dirty && lastCols > 0 && lastRows > 0)
            {
                dirty = false;
                Build(lastCols, lastRows);
            }
        }

        [ContextMenu("Rebuild")]
        void RebuildContext()
        {
            if (lastCols > 0 && lastRows > 0) Build(lastCols, lastRows);
        }

        // --- Build -------------------------------------------------------------

        /// <summary>
        /// Reads cellWidth from the parent Board.columnSpacing and cellHeight from the first
        /// Column.pieceSpacing found at runtime. Skips silently if either source isn't available
        /// (e.g. in edit mode before LevelLoader has spawned columns).
        /// </summary>
        void SyncCellSizeFromGameplay()
        {
            var board = GetComponentInParent<Board>();
            if (board != null) cellWidth = board.ColumnSpacing;

            var anyColumn = (GameManager.Instance != null && GameManager.Instance.Columns.Count > 0)
                ? GameManager.Instance.Columns[0]
                : null;
            if (anyColumn != null) cellHeight = anyColumn.PieceSpacing;
        }

        public void Build(int cols, int rows)
        {
            lastCols = cols;
            lastRows = rows;
            Clear();

            // Auto-sync cell size with the gameplay components, so the frame never drifts
            // when the designer rescales Board.columnSpacing or Column.pieceSpacing.
            if (autoSyncCellSize) SyncCellSizeFromGameplay();

            float leftX      = -((cols - 1) * cellWidth) * 0.5f;
            float bottomRowY  = -(rows - 1) * cellHeight;

            float topEdgeY    = cellHeight * 0.5f + topHeight * 0.5f;
            float bottomEdgeY  = bottomRowY - cellHeight * 0.5f - bottomHeight * 0.5f;
            float leftEdgeX    = leftX - cellWidth * 0.5f - sideWidth * 0.5f;
            float rightEdgeX   = leftX + (cols - 1) * cellWidth + cellWidth * 0.5f + sideWidth * 0.5f;

            // Per-cell center fill — scaled up so neighboring cells overlap, creating inter-cell outlines.
            if (center != null)
                for (int c = 0; c < cols; c++)
                    for (int r = 0; r < rows; r++)
                        Spawn(center, leftX + c * cellWidth, -r * cellHeight, frameZ, cellWidth * partScale, cellHeight * partScale, frameSortingOrder);

            // Top & bottom edges — scaled up and inset toward center.
            for (int c = 0; c < cols; c++)
            {
                float x = leftX + c * cellWidth;
                Spawn(topEdge,    x, topEdgeY - borderInset,    frameZ, cellWidth * partScale, topHeight * partScale,    frameSortingOrder);
                Spawn(bottomEdge, x, bottomEdgeY + borderInset, frameZ, cellWidth * partScale, bottomHeight * partScale, frameSortingOrder);
            }

            // Side borders — 1 first + many middle + 1 last, inset toward center.
            BuildSideBorder(leftEdgeX  + borderInset, rows, leftFirst,  leftMiddle,  leftLast);
            BuildSideBorder(rightEdgeX - borderInset, rows, rightFirst, rightMiddle, rightLast);

            // Corners — oversized, inset diagonally toward center, drawn above the edges to hide seams.
            float ci = borderInset;
            Spawn(topLeftCorner,     leftEdgeX  + ci, topEdgeY    - ci, frameZ, cornerWidth * partScale, cornerHeight * partScale, cornerSortingOrder);
            Spawn(topRightCorner,    rightEdgeX - ci, topEdgeY    - ci, frameZ, cornerWidth * partScale, cornerHeight * partScale, cornerSortingOrder);
            Spawn(bottomLeftCorner,  leftEdgeX  + ci, bottomEdgeY + ci, frameZ, cornerWidth * partScale, cornerHeight * partScale, cornerSortingOrder);
            Spawn(bottomRightCorner, rightEdgeX - ci, bottomEdgeY + ci, frameZ, cornerWidth * partScale, cornerHeight * partScale, cornerSortingOrder);

            BuildIndicators(cols, leftX, topEdgeY, bottomEdgeY);
        }

        void BuildSideBorder(float x, int rows, Sprite first, Sprite middle, Sprite last)
        {
            float gridTop    =  cellHeight * 0.5f;
            float gridBottom = -(rows - 1) * cellHeight - cellHeight * 0.5f;

            float w = sideWidth * partScale; // thickness gets the outline boost

            // First cap (top) and last cap (bottom).
            Spawn(first, x, gridTop - sideFirstHeight * 0.5f,    frameZ, w, sideFirstHeight, frameSortingOrder);
            Spawn(last,  x, gridBottom + sideLastHeight * 0.5f,  frameZ, w, sideLastHeight,  frameSortingOrder);

            // Middle tiles fill the remaining span.
            float middleTop    = gridTop - sideFirstHeight;
            float middleBottom = gridBottom + sideLastHeight;
            float span = middleTop - middleBottom;
            if (span > 0.001f && sideMiddleHeight > 0.001f)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt(span / sideMiddleHeight));
                float h = span / count; // distribute evenly so they tile seamlessly
                for (int i = 0; i < count; i++)
                    Spawn(middle, x, middleTop - h * (i + 0.5f), frameZ, w, h, frameSortingOrder);
            }
        }

        void BuildIndicators(int cols, float leftX, float topEdgeY, float bottomEdgeY)
        {
            var columns = GameManager.Instance != null ? GameManager.Instance.Columns : null;

            float statusY = topEdgeY + topHeight * 0.5f + indicatorAboveGap + indicatorHeight * 0.5f;
            float outY    = bottomEdgeY - bottomHeight * 0.5f - outBelowGap - indicatorHeight * 0.5f;

            for (int c = 0; c < cols; c++)
            {
                float x = leftX + c * cellWidth;

                // Base indicator — starts as not-done, swaps to done when the column locks.
                var baseGO = Spawn(notDoneSprite, x, statusY, indicatorZ, indicatorWidth, indicatorHeight, indicatorSortingOrder);

                // Tick overlay — covers the arrow, smaller than the base so the outline shows.
                var tickGO = Spawn(tickSprite, x, statusY, indicatorZ - 0.01f, tickWidth, tickHeight, indicatorSortingOrder + 1);
                tickGO.SetActive(false);

                var ind = baseGO.AddComponent<ColumnIndicator>();
                Column col = (columns != null && c < columns.Count) ? columns[c] : null;
                ind.Setup(col, baseGO.GetComponent<SpriteRenderer>(), notDoneSprite, doneSprite, tickGO);

                // Out arrows below.
                Spawn(outSprite, x, outY, indicatorZ, indicatorWidth, indicatorHeight, indicatorSortingOrder);
            }
        }

        GameObject Spawn(Sprite sprite, float x, float y, float z, float w, float h, int order)
        {
            var go = new GameObject("BoardPart");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, y, z);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;

            if (sprite != null)
            {
                Vector3 size = sprite.bounds.size;
                if (size.x > 0f && size.y > 0f)
                    go.transform.localScale = new Vector3(w / size.x, h / size.y, 1f);
            }

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
