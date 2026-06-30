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
        [Header("Icon rotation (local offset on the board plane)")]
        [Tooltip("Extra LOCAL rotation (Euler°) for the indicators. They are now CHILDREN of MainBoard, so " +
                 "they already lie flat on the board plane and face the camera with it — leave this (0,0,0) " +
                 "unless you want a small manual tweak. (Renamed from the old board-space rotation, which no " +
                 "longer applies now that indicators are attached to MainBoard.)")]
        [SerializeField] private Vector3 indicatorLocalRotation = Vector3.zero;

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
        [Tooltip("The row count your size fractions are tuned for (the standard grid's rows — usually 3). " +
                 "Icon sizes are auto-multiplied by standardRows/rows so they stay PROPORTIONAL TO THE " +
                 "COLUMNS on grids with a different row count (otherwise a 2-row board's icons look small " +
                 "and a 5-row board's look too big). At this row count the size is unchanged.")]
        [SerializeField] private int standardRowsForIconScale = 3;

        [Header("Placement — fractions of board height (auto-adapt per grid)")]
        [Tooltip("Gap of the TOP status icon from the board's top edge, as a fraction of board height. " +
                 "0 = right at the edge, + = further up, - = slightly onto the board.")]
        [SerializeField] private float statusEdgeGapFraction = 0f;
        [Tooltip("Gap of the BOTTOM out arrow from the board's bottom edge, as a fraction of board height. " +
                 "0 = right at the edge, + = further below.")]
        [SerializeField] private float outEdgeGapFraction = 0f;
        [Tooltip("Small depth nudge along MainBoard-local Z (off the board surface), as a fraction of the " +
                 "sprite's half-height. The icons already draw on top via sorting order, so 0 is usually " +
                 "fine; use a small value only if you want them to physically sit slightly off the board.")]
        [SerializeField] private float liftFraction = 0.1f;
        [Tooltip("Manual position nudge for the DONE status frame, in Board-local units (type values here): " +
                 "X = sideways, Y = toward the camera, Z = along the tilted board (up/down). Lets you fine-" +
                 "tune the slot's placement on top of the auto-computed position.")]
        [SerializeField] private Vector3 statusIconOffset = Vector3.zero;

        [Header("Column spacing")]
        [Tooltip("Horizontal spacing between indicators (column → column). 0 = AUTO: matches " +
                 "Board.ColumnSpacing so each icon sits over its column. Set > 0 to override.")]
        [SerializeField] private float columnSpacingOverride = 0f;
        [Tooltip("Spreads the indicators horizontally OUT from the board centre (1 = directly over each " +
                 "column, >1 = wider apart, <1 = closer). Use this when scaled-up icons sit too close " +
                 "together — it moves them apart without changing their size.")]
        [SerializeField] private float indicatorSpreadX = 1f;

        [Header("Sorting")]
        [Tooltip("SpriteRenderer sorting order (tick draws at +1 so it sits over the done icon).")]
        [SerializeField] private int indicatorSortingOrder = 10;

        [Header("Indicator sprites (fixed — generated per column)")]
        [Tooltip("These are the ONLY runtime-generated visuals: one set per column, so their COUNT follows " +
                 "the level's column count. The sprites themselves don't change per level, so assign them " +
                 "ONCE here (not per LevelData). Colors come from the level's UI Theme Palette via the radiant.")]
        [SerializeField] private Sprite inSprite;       // always-visible status FRAME ('In' slot)
        [SerializeField] private Sprite notDoneSprite;  // marker inside the In frame while unsolved
        [SerializeField] private Sprite tickSprite;     // shown on lock
        [SerializeField] private Sprite outSprite;      // 'out' arrow below each column

        [Header("Indicator shadows (optional — drawn BEHIND the icon, follow show/hide)")]
        [Tooltip("Shadow behind the Not-Done (arrow) icon — e.g. arrow_shadow. Spawned as a child so it " +
                 "hides/shows with the arrow. Leave null for none.")]
        [SerializeField] private Sprite notDoneShadowSprite;
        [Tooltip("Shadow behind the Tick icon — e.g. tick_shadow.")]
        [SerializeField] private Sprite tickShadowSprite;
        [Tooltip("Shadow behind the bottom Out arrow — optional (e.g. arrow_shadow).")]
        [SerializeField] private Sprite outShadowSprite;
        [Tooltip("Size of the shadow relative to its icon (1 = the shadow sprite's own size, which is " +
                 "slightly larger than the icon → it peeks out as a drop shadow).")]
        [SerializeField] private float shadowScaleMultiplier = 1f;
        [Tooltip("Local offset of the shadow from its icon (drop direction). The shadow art usually has the " +
                 "offset baked in, so 0 is fine; nudge if you want more drop.")]
        [SerializeField] private Vector3 shadowLocalOffset = Vector3.zero;
        [Tooltip("Point INSIDE the shadow sprite that is anchored to the arrow's centre and scaled around " +
                 "(0.5,0.5 = the sprite's middle). renderer bounds only know the full quad, not where the dark " +
                 "pixels are — so if the shadow's dark part sits LOW in its image and grows downward, lower Y " +
                 "(~0.4–0.45) until it scales symmetrically around the arrow.")]
        [SerializeField] private Vector2 shadowPivot = new Vector2(0.5f, 0.5f);

        // Per-grid indicator tweaks (size multiplier + status/out offsets) live on LevelLoader.scaleOverrides
        // — the SAME per-grid list that tunes board scale — so one place fine-tunes the board + its indicators.

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

        /// <summary>Request a rebuild next frame. Called by LevelLoader when scaleOverrides change in Play mode.</summary>
        public void MarkDirty() => dirty = true;

        public void Build(int cols, int rows)
        {
            lastCols = cols;
            lastRows = rows;
            Clear();

            var board = GetComponentInParent<Board>();
            // Attach indicators to MainBoard (THIS transform) so they move / rotate / scale WITH the board
            // and stay glued to its top / bottom edges. MainBoard's non-uniform image-fit scale is undone
            // per-icon below (size ÷ MainBoard scale) so the icons aren't stretched.
            indicatorParent = transform;
            cellWidth = columnSpacingOverride > 0f ? columnSpacingOverride
                      : (board != null && board.ColumnSpacing > 0f ? board.ColumnSpacing : 2.7f);

            // Row-count compensation + per-grid override. Icon sizes are a fraction of board HEIGHT, which
            // grows with row count — so without compensation a 2-row board's icons look small next to its
            // (wider) columns and a 5-row board's look big. rowComp = standardRows / rows keeps icon size
            // PROPORTIONAL TO COLUMN WIDTH on any grid, and is exactly 1 at the standard row count. The
            // per-grid manual tweak is read from LevelLoader.scaleOverrides — the SAME per-grid list that
            // tunes board scale — so ONE place fine-tunes the board + its indicators for each grid.
            float rowComp = (float)Mathf.Max(1, standardRowsForIconScale) / Mathf.Max(1, rows);
            GridScaleOverride ov = default;
            bool hasOv = LevelLoader.Instance != null && LevelLoader.Instance.TryGetGridOverride(cols, rows, out ov);
            float sizeMul = rowComp * (hasOv && ov.indicatorSizeMultiplier > 0f ? ov.indicatorSizeMultiplier : 1f);
            Vector3 statusExtra = hasOv ? ov.indicatorStatusOffset : Vector3.zero;
            Vector3 outExtra    = hasOv ? ov.indicatorOutOffset : Vector3.zero;
            // Per-grid indicator spread overrides the global one when set (>0); else use the global value.
            float spreadX = (hasOv && ov.indicatorSpread > 0f) ? ov.indicatorSpread : indicatorSpreadX;

            var level   = LevelLoader.Instance != null ? LevelLoader.Instance.CurrentLevel : null;
            var columns = GameManager.Instance != null ? GameManager.Instance.Columns : null;
            if (level == null) return;

            // Indicators are CHILDREN of MainBoard, so we work in MainBoard-LOCAL space: the sprite's own
            // bounds give the top / bottom edges, each column's X is projected into local space, and Z = 0
            // keeps the icons ON the board plane (sortingOrder draws them on top) — that's why they no
            // longer float off the surface and now move with the board.
            var boardSR = GetComponent<SpriteRenderer>();
            Bounds sb = (boardSR != null && boardSR.sprite != null) ? boardSR.sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
            Vector3 cen = sb.center;
            float halfH = sb.extents.y, halfW = sb.extents.x;

            // MainBoard's own (non-uniform image-fit) local scale — divide each icon's size by it so a
            // child ends up the SAME world size it had when it was parented to the uniform Board.
            Vector3 mbScale = transform.localScale;
            float mbx = Mathf.Abs(mbScale.x) < 1e-4f ? 1f : mbScale.x;
            float mby = Mathf.Abs(mbScale.y) < 1e-4f ? 1f : mbScale.y;

            float boardExtentZ = Mathf.Abs(mby) * sb.size.y;                        // board height in Board-local units (drives icon size)
            if (boardExtentZ < 1e-3f) boardExtentZ = rows * cellWidth;

            float statusY = cen.y + halfH * (1f + statusEdgeGapFraction);           // at / just above the sprite's top edge
            float outY    = cen.y - halfH * (1f + outEdgeGapFraction);              // at / just below the bottom edge
            float liftZ   = halfH * liftFraction;                                   // small in-plane-normal nudge (sortingOrder already draws on top)
            float doneScale    = boardExtentZ * doneSizeFraction * sizeMul;
            float notDoneScale = boardExtentZ * notDoneSizeFraction * sizeMul;
            float tickS        = boardExtentZ * tickSizeFraction * sizeMul;
            float outScale     = boardExtentZ * indicatorSizeFraction * sizeMul;

            float leftX = -((cols - 1) * cellWidth) * 0.5f;                         // fallback X spread only
            Quaternion rot = Quaternion.Euler(indicatorLocalRotation);

            for (int c = 0; c < cols; c++)
            {
                Column col = (columns != null && c < columns.Count) ? columns[c] : null;
                // Column X in MainBoard-local: project the ACTUAL column into our local space (robust to
                // board scale / position / per-prefab offset, so icons always sit over their column).
                // Fallback to an even spread across the sprite if columns aren't available yet.
                float colX = col != null
                    ? transform.InverseTransformPoint(col.transform.position).x
                    : cen.x + (cols <= 1 ? 0f : Mathf.Lerp(-halfW, halfW, (float)c / (cols - 1)) * 0.8f);

                // Spread the indicators out from the board centre (per-grid override, else the global value).
                colX = cen.x + (colX - cen.x) * spreadX;

                // Top status: the IN sprite is the always-visible FRAME (the slot). The not-done icon
                // sits INSIDE it while the column is unsolved; on lock ColumnIndicator hides not-done and
                // shows the tick. Sizes are divided by MainBoard's scale to undo its non-uniform image-fit.
                Sprite frameSprite = inSprite != null ? inSprite : notDoneSprite;
                if (frameSprite != null)
                {
                    var sPos = new Vector3(colX, statusY, liftZ) + statusIconOffset + statusExtra;
                    var frameGO = Spawn(frameSprite, sPos, rot, new Vector3(doneScale / mbx, doneScale / mby, 1f), indicatorSortingOrder);
                    // In frame uses the PRIMARY radiant (from the level's theme set): a vertical gradient that
                    // REPLACES the sprite's colors (exact), keeping only its shape. Shared with the Out
                    // arrows below so the board indicators read as one synchronized theme.
                    ApplyRadiant(frameGO, level.GetThemeColor(UiThemeSlot.Primary));

                    // Inner not-done — only when there's a distinct In frame for it to sit inside.
                    GameObject notDoneGO = null;
                    if (inSprite != null && notDoneSprite != null)
                    {
                        notDoneGO = Spawn(notDoneSprite, sPos, rot, new Vector3(notDoneScale / mbx, notDoneScale / mby, 1f), indicatorSortingOrder + 2);
                        // Not Done uses the SECONDARY radiant (independent of the In frame's Primary).
                        ApplyRadiant(notDoneGO, level.GetThemeColor(UiThemeSlot.Secondary));
                        AttachShadow(notDoneGO, notDoneShadowSprite);   // shadow sits behind (frame < shadow < arrow)
                    }

                    // Inner tick (shown on lock).
                    GameObject tickGO = null;
                    if (tickSprite != null)
                    {
                        tickGO = Spawn(tickSprite, sPos, rot, new Vector3(tickS / mbx, tickS / mby, 1f), indicatorSortingOrder + 2);
                        AttachShadow(tickGO, tickShadowSprite);
                        tickGO.SetActive(false);
                    }

                    var ind = frameGO.AddComponent<ColumnIndicator>();
                    ind.Setup(col, notDoneGO, tickGO);
                }

                // Bottom out arrow.
                if (outSprite != null)
                {
                    var oPos = new Vector3(colX, outY, liftZ) + outExtra;
                    var outGO = Spawn(outSprite, oPos, rot, new Vector3(outScale / mbx, outScale / mby, 1f), indicatorSortingOrder + 1);
                    // Out arrows share the PRIMARY radiant with the In frames (one synced theme).
                    ApplyRadiant(outGO, level.GetThemeColor(UiThemeSlot.Primary));
                    AttachShadow(outGO, outShadowSprite);
                }
            }
        }

        // Shared material for the In frame's solid-color replacement. One instance reused across all
        // frames — the per-column color comes from each SpriteRenderer.color (_RendererColor), which is
        // per-renderer even with a shared material. Null if the shader isn't found (caller falls back).
        static Material _solidTintMat;
        static Material SolidTintMaterial()
        {
            if (_solidTintMat == null)
            {
                var sh = Shader.Find("Sort/SpriteSolidTint");
                if (sh != null) _solidTintMat = new Material(sh) { name = "InFrameSolidTint (runtime)" };
            }
            return _solidTintMat;
        }

        // Applies a radiant (top→bottom gradient) to a spawned indicator SpriteRenderer: swaps in the
        // solid-tint material and sets _TopColor/_BottomColor per-renderer via a MaterialPropertyBlock
        // (so one shared material serves every indicator with its own colors). Falls back to a flat
        // multiply tint (sr.color) if the shader is missing, so the indicator still draws.
        void ApplyRadiant(GameObject go, GradientColor g)
        {
            if (go == null || g == null) return;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) return;

            var mat = SolidTintMaterial();
            if (mat != null)
            {
                sr.sharedMaterial = mat;
                var mpb = new MaterialPropertyBlock();
                sr.GetPropertyBlock(mpb);
                mpb.SetColor("_TopColor", g.top);
                mpb.SetColor("_BottomColor", g.bottom);
                sr.SetPropertyBlock(mpb);
            }
            else
            {
                sr.color = g.top;   // fallback: flat multiply tint with the top color
            }
        }

        // Spawns a shadow sprite as a CHILD of the icon, one sorting order BEHIND it. Being a child, it
        // follows the icon's show/hide (ColumnIndicator toggles the arrow/tick) and its world scale, so the
        // shadow stays glued behind the icon. The shadow art is left untinted (it carries its own dark color).
        void AttachShadow(GameObject icon, Sprite shadowSprite)
        {
            if (icon == null || shadowSprite == null) return;
            var isr = icon.GetComponent<SpriteRenderer>();
            var go = new GameObject("Shadow");
            go.transform.SetParent(icon.transform, false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(shadowScaleMultiplier, shadowScaleMultiplier, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = shadowSprite;
            sr.sortingOrder = (isr != null ? isr.sortingOrder : indicatorSortingOrder) - 1;

            // Anchor the chosen point of the shadow sprite (Shadow Pivot) onto the arrow's centre, accounting
            // for the current scale — so raising Shadow Scale Multiplier expands the shadow symmetrically
            // around THAT point instead of growing from the sprite's quad pivot. Lower Shadow Pivot Y if the
            // dark pixels sit low in the image.
            Vector3 target = isr != null ? isr.bounds.center : icon.transform.position;
            Bounds sb = shadowSprite.bounds;   // local sprite bounds (relative to its pivot)
            Vector3 anchorLocal = sb.center + new Vector3((shadowPivot.x - 0.5f) * sb.size.x,
                                                          (shadowPivot.y - 0.5f) * sb.size.y, 0f);
            go.transform.position = target - go.transform.TransformVector(anchorLocal);
            go.transform.localPosition += shadowLocalOffset;        // optional manual drop nudge
            // Not added to `spawned`: it's a child of the icon and is cleared when the icon is destroyed.
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
