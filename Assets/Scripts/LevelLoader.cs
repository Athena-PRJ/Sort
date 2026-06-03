using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Builds the board at runtime from a LevelData and applies a uniform scale per level so
    /// the total board footprint stays roughly the same across grid sizes.
    ///
    /// Designer workflow:
    ///   1. Set Board.columnSpacing, Column.pieceSpacing, and piece prefab Scale to look right
    ///      at the STANDARD grid (default 3×3).
    ///   2. The algorithm computes a uniform scale F per level and applies it to Board.transform
    ///      and PlayerHand.HandAnchor.transform. All children (columns, pieces, MainBoard, slot
    ///      decoration, held piece) inherit F automatically.
    ///   3. Default formula: F = sqrt(standardArea / currentArea). Total board area stays
    ///      ~constant across square grids.
    ///   4. Designer can override F per specific grid size via scaleOverrides.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class LevelLoader : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject columnPrefab;

        [Tooltip("The Prefab-to-gen registry — single source of truth for every piece prefab + its " +
                 "spacing/offset config. Each LevelData.piecePrefab picks one of the prefabs registered " +
                 "here. Add an entry to expose a new prefab to designers; no code changes needed.")]
        [SerializeField] private PrefabRegistry registry;

        [Header("Scene refs")]
        [SerializeField] private Board board;
        [SerializeField] private PlayerHand playerHand;

        [Header("Default")]
        [Tooltip("Used when no level is selected (e.g. opening the Game scene directly in editor).")]
        [SerializeField] private LevelData defaultLevel;

        [Header("Auto-fit: uniform scale per level")]
        [Tooltip("If true, scales Board.transform and HandAnchor.transform uniformly so total board area " +
                 "stays roughly the same across grid sizes. If false, no scaling — Board.columnSpacing, " +
                 "Column.pieceSpacing, and piece prefab scales are used as-is for every level.")]
        [SerializeField] private bool autoFit = true;

        [Tooltip("The 'standard' grid size at which Board.columnSpacing, Column.pieceSpacing, and " +
                 "piece prefab scales were authored. The algorithm uses this as the baseline; for " +
                 "other grids it applies a uniform scale multiplier to keep total board area constant.\n\n" +
                 "Default (3, 3). Tune your scene at this grid size, then test other grids — auto-fit handles them.")]
        [SerializeField] private Vector2Int standardGrid = new Vector2Int(3, 3);

        [Tooltip("Optional per-grid scale overrides. If a level's grid (cols × rows) matches an entry " +
                 "here, that scale multiplier is used instead of the default sqrt(standardArea/currentArea) " +
                 "formula. Use this to fine-tune specific grids (e.g. 'I want 4×4 at 0.8 instead of 0.75').")]
        [SerializeField] private GridScaleOverride[] scaleOverrides = new GridScaleOverride[0];

        [Tooltip("If true, MainBoard.localScale.x/y are additionally multiplied by (cols / standardGrid.x, " +
                 "rows / standardGrid.y) so the frame wraps the actual grid extent tightly per level. " +
                 "Without this, MainBoard inherits only Board's area-preserving uniform F, making it " +
                 "look oversized for smaller grids (e.g. a 2x2 wearing a 3x3-sized frame).")]
        [SerializeField] private bool autoFitMainBoardToGrid = true;

        [Header("MainBoard")]
        [Tooltip("Fallback FullBoard sprite used when a LevelData has no mainBoardSprite assigned. " +
                 "Drag one of Assets/Texture/FullBoard/MB_*.png here.")]
        [SerializeField] private Sprite defaultMainBoardSprite;

        [Tooltip("Optional reference to the BoardFrame component on the MainBoard child GameObject. " +
                 "If left null, LevelLoader auto-finds one via GetComponentInChildren on the Board.")]
        [SerializeField] private BoardFrame boardFrame;

        [Tooltip("If true, after level build, MainBoard's transform.position is auto-shifted so its " +
                 "SpriteRenderer.bounds.center sits exactly at the visual center of the spawned " +
                 "column grid. Removes the need to manually tune MainBoard.localPosition per sprite " +
                 "pivot. Designer's authored position becomes irrelevant — leave at (0,0,0) and let " +
                 "this handle alignment automatically.")]
        [SerializeField] private bool autoAlignBoardFrameToColumns = true;

        [Header("Background")]
        [Tooltip("Fallback background sprite used when a LevelData has no backgroundSprite assigned. " +
                 "Drag one of Assets/Texture/Background/BG_*.png here.")]
        [SerializeField] private Sprite defaultBackgroundSprite;

        [Tooltip("Optional reference to the BackgroundFrame component on the Background GameObject (child of Canvas). " +
                 "If left null, LevelLoader auto-finds one via FindAnyObjectByType at runtime.")]
        [SerializeField] private BackgroundFrame backgroundFrame;

        public static LevelLoader Instance { get; private set; }
        public LevelData CurrentLevel { get; private set; }

        /// <summary>The head of the level chain (typically Level1). SkillProgress walks from this.</summary>
        public LevelData DefaultLevel => defaultLevel;

        /// <summary>Fallback sprite when LevelData has none. Used by BoardFrame.Apply().</summary>
        public Sprite DefaultMainBoardSprite => defaultMainBoardSprite;

        /// <summary>Fallback background when LevelData has none. Used by BackgroundFrame.Apply().</summary>
        public Sprite DefaultBackgroundSprite => defaultBackgroundSprite;

        // Captured authored scale of Board, HandAnchor, and MainBoard so we can apply F + per-prefab
        // adjusts as multipliers without destroying the designer's manual scene settings. Captured once.
        Vector3 authoredBoardScale = Vector3.one;
        Vector3 authoredHandScale = Vector3.one;
        Vector3 authoredMainBoardScale = Vector3.one;

        // heldPieceExtraOffset is pushed straight to PlayerHand.HeldPieceLocalOffset (no authored
        // baseline needed — the held piece's "home" is localPosition=offset).

        void Awake()
        {
            Instance = this;

            if (board != null) authoredBoardScale = board.transform.localScale;
            if (playerHand != null && playerHand.HandAnchor != null)
                authoredHandScale = playerHand.HandAnchor.localScale;

            // Find BoardFrame (auto-discover) so we can capture MainBoard's authored scale too.
            if (boardFrame == null && board != null) boardFrame = board.GetComponentInChildren<BoardFrame>(true);
            if (boardFrame != null) authoredMainBoardScale = boardFrame.transform.localScale;

            var level = LevelProgress.SelectedLevel != null ? LevelProgress.SelectedLevel : defaultLevel;
            if (level == null)
            {
                Debug.LogError("[LevelLoader] No level selected and no defaultLevel assigned.", this);
                return;
            }
            BuildLevel(level);
        }

        public void BuildLevel(LevelData data)
        {
            if (data == null) { Debug.LogError("[LevelLoader] BuildLevel called with null data."); return; }
            if (columnPrefab == null || board == null || playerHand == null)
            {
                Debug.LogError("[LevelLoader] Missing column prefab or scene reference. Wire all serialized fields.", this);
                return;
            }

            var piecePrefab = ResolvePiecePrefab(data);
            if (piecePrefab == null)
            {
                Debug.LogError($"[LevelLoader] LevelData '{data.name}' has no piecePrefab assigned. " +
                               "Open the LevelData asset and pick one from the dropdown.", this);
                return;
            }

            CurrentLevel = data;
            ClearBoard();

            // Apply per-level background. Auto-discover if not wired.
            if (backgroundFrame == null) backgroundFrame = FindAnyObjectByType<BackgroundFrame>(FindObjectsInactive.Include);
            if (backgroundFrame != null) backgroundFrame.Apply();

            // Apply per-prefab spacing override BEFORE spawning columns so the layout uses the
            // right spacing from the start. Look up the override matching this level's prefab;
            // if none found, Board/Column use their designer-authored serialized spacings.
            ApplyPrefabSpacingOverride(piecePrefab);

            // Spawn columns + pieces. They use the (possibly overridden) Board.ColumnSpacing
            // and Column.PieceSpacing values via runtime override.
            var spawnedColumns = new System.Collections.Generic.List<Column>();
            foreach (var colConfig in data.columns)
            {
                var columnGO = Instantiate(columnPrefab, board.transform);
                var col = columnGO.GetComponent<Column>();
                if (col != null) spawnedColumns.Add(col);
                foreach (var pieceCfg in colConfig.pieces)
                {
                    var pieceGO = Instantiate(piecePrefab, columnGO.transform);
                    var piece = pieceGO.GetComponent<Piece>();
                    if (piece != null)
                    {
                        piece.SetConfig(pieceCfg);
                        ApplyRegistryPieceScale(piece, piecePrefab);
                    }
                }
            }

            // Apply the per-prefab piece spacing to each spawned column (their authored field
            // stays the same, runtime override takes effect).
            ApplyPieceSpacingOverrideToColumns(piecePrefab, spawnedColumns);

            board.Layout();

            // Spawn the held piece under the hand anchor.
            var heldGO = Instantiate(piecePrefab, playerHand.HandAnchor);
            var held = heldGO.GetComponent<Piece>();
            if (held != null)
            {
                held.SetConfig(data.startingHeldPiece);
                ApplyRegistryPieceScale(held, piecePrefab);
            }
            playerHand.SetHeldPiece(held);

            if (autoFit) ApplyAutoFit(data, piecePrefab);

            // After everything else (columns laid out, autoFit scaled), align MainBoard's visual
            // center to the column grid + apply per-prefab fine-tune offsets. Re-runnable so
            // OnValidate can call it again when designer tweaks values in Play-mode Inspector.
            RefreshAlignment();
        }

        /// <summary>
        /// Re-runs the MainBoard / HandAnchor alignment using the CURRENT serialized values on
        /// LevelLoader. Idempotent — safe to call multiple times. Used by both BuildLevel (one
        /// time per level) and OnValidate (every time designer tweaks an offset in Play mode).
        /// </summary>
        public void RefreshAlignment()
        {
            if (CurrentLevel == null) return;
            if (board == null) return;

            var piecePrefab = ResolvePiecePrefab(CurrentLevel);
            if (piecePrefab == null) return;

            // Re-gather currently-spawned columns. board.GetComponentsInChildren picks them up
            // wherever they live in the Board's hierarchy.
            var cols = new System.Collections.Generic.List<Column>();
            board.GetComponentsInChildren(true, cols);

            bool hasOv = TryGetSpacingOverride(piecePrefab, out var ov);
            Vector3 mbOff = hasOv ? ov.mainBoardExtraOffset : Vector3.zero;
            Vector3 piOff = hasOv ? ov.heldPieceExtraOffset : Vector3.zero;

            if (autoAlignBoardFrameToColumns) AlignBoardFrameToColumns(cols, mbOff);

            if (playerHand != null)
                playerHand.HeldPieceLocalOffset = piOff;
        }

        void OnValidate()
        {
            // Live-preview support: when designer tweaks a serialized field in Play mode, OnValidate
            // fires; rerun alignment so the change shows immediately in the running game. Skip in
            // Edit mode (no level built yet) and during build/import (CurrentLevel is null).
            if (!Application.isPlaying) return;
            if (CurrentLevel == null) return;
            RefreshAlignment();
        }

        /// <summary>
        /// Shifts boardFrame.transform.position so its SpriteRenderer.bounds.center sits at the
        /// world-space centroid of all spawned pieces PLUS <paramref name="extraOffset"/>. Idempotent:
        /// each call computes delta from current state to target, so repeated calls converge to the
        /// same end position regardless of starting point.
        /// </summary>
        void AlignBoardFrameToColumns(System.Collections.Generic.List<Column> columns, Vector3 extraOffset)
        {
            if (boardFrame == null) return;
            var sr = boardFrame.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return;
            if (columns == null || columns.Count == 0) return;

            // Centroid of all piece world positions = visual center of the column grid.
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var col in columns)
            {
                if (col == null) continue;
                for (int i = 0; i < col.transform.childCount; i++)
                {
                    var t = col.transform.GetChild(i);
                    if (t.GetComponent<Piece>() == null) continue;
                    sum += t.position;
                    count++;
                }
            }
            if (count == 0) return;
            Vector3 gridCenter = sum / count;

            // Target world position for sprite center = gridCenter + offset. Delta moves us there.
            Vector3 delta = (gridCenter + extraOffset) - sr.bounds.center;
            boardFrame.transform.position += delta;
        }

        /// <summary>
        /// Looks up the per-prefab entry in <see cref="registry"/> for <paramref name="piecePrefab"/>
        /// and pushes its columnSpacing onto Board as a runtime override. If no match (or no registry),
        /// clears the runtime override so Board falls back to its authored serialized value.
        /// </summary>
        void ApplyPrefabSpacingOverride(GameObject piecePrefab)
        {
            if (board == null) return;
            if (TryGetSpacingOverride(piecePrefab, out var entry))
            {
                board.SetRuntimeColumnSpacing(entry.columnSpacing);
            }
            else
            {
                // Clear any previous override so the authored value takes effect.
                board.SetRuntimeColumnSpacing(0f);
            }
        }

        /// <summary>
        /// Pushes the registry entry's pieceSpacing to each spawned column as a runtime override.
        /// Each Column keeps its authored pieceSpacing field; only the runtime override changes.
        /// </summary>
        void ApplyPieceSpacingOverrideToColumns(GameObject piecePrefab, System.Collections.Generic.List<Column> columns)
        {
            if (columns == null || columns.Count == 0) return;
            bool found = TryGetSpacingOverride(piecePrefab, out var entry);
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i] == null) continue;
                columns[i].SetRuntimePieceSpacing(found ? entry.pieceSpacing : 0f);
            }
        }

        bool TryGetSpacingOverride(GameObject piecePrefab, out PieceGenEntry match)
        {
            if (registry != null) return registry.TryGetSpacing(piecePrefab, out match);
            match = default;
            return false;
        }

        /// <summary>
        /// Pushes the registry entry's <see cref="PieceGenEntry.pieceScale"/> onto the freshly-spawned
        /// piece as its baseline. No-op when the registry has no entry for this prefab, or when the
        /// entry's pieceScale is zero (designer's signal to "use the prefab's authored localScale instead").
        /// </summary>
        void ApplyRegistryPieceScale(Piece piece, GameObject piecePrefab)
        {
            if (piece == null || registry == null) return;
            if (!registry.TryGetEntry(piecePrefab, out var entry)) return;
            if (entry.pieceScale.sqrMagnitude < 1e-6f) return; // Zero scale → keep prefab's authored.
            piece.SetBaselineScale(entry.pieceScale);
        }

        /// <summary>
        /// Called by <see cref="PrefabRegistry.OnValidate"/> (Editor + Play mode only) when the designer
        /// tweaks any field on the registry asset. Re-applies the active prefab's spacing, pieceScale,
        /// MainBoard offset, and held-piece offset to every live Piece in the scene so the tweak is
        /// visible without restarting Play mode. Cheap: just MPB-free transform writes + a board.Layout().
        /// </summary>
        public void OnRegistryChanged()
        {
            if (CurrentLevel == null) return;
            var piecePrefab = ResolvePiecePrefab(CurrentLevel);
            if (piecePrefab == null || board == null) return;

            // Re-push spacing override onto Board, and onto every Column already spawned.
            ApplyPrefabSpacingOverride(piecePrefab);

            var cols = new System.Collections.Generic.List<Column>();
            board.GetComponentsInChildren(true, cols);
            ApplyPieceSpacingOverrideToColumns(piecePrefab, cols);

            // Re-push pieceScale to every live piece (board + held).
            var pieces = new System.Collections.Generic.List<Piece>();
            board.GetComponentsInChildren(true, pieces);
            for (int i = 0; i < pieces.Count; i++)
                ApplyRegistryPieceScale(pieces[i], piecePrefab);

            if (playerHand != null && playerHand.HeldPiece != null)
                ApplyRegistryPieceScale(playerHand.HeldPiece, piecePrefab);

            // Re-layout columns + re-align MainBoard frame with the (possibly new) offsets.
            board.Layout();
            for (int i = 0; i < cols.Count; i++) if (cols[i] != null) cols[i].Layout();
            RefreshAlignment();
        }

        /// <summary>
        /// Picks the piece prefab for <paramref name="data"/>. Reads <see cref="LevelData.piecePrefab"/>
        /// directly — there is no fallback chain. If the field is null, the caller must handle it
        /// (typically by logging an error and aborting BuildLevel).
        /// </summary>
        GameObject ResolvePiecePrefab(LevelData data)
        {
            return data != null ? data.piecePrefab : null;
        }

        /// <summary>
        /// Applies a uniform scale factor F to Board.transform and HandAnchor.transform based on the
        /// current level's grid size. Per-prefab scale adjusts (from the matching PrefabRegistry entry)
        /// optionally fine-tune MainBoard and HandAnchor for the specific piece prefab being used. All children
        /// (columns, pieces, slot decoration, held piece) inherit Board.scale automatically.
        /// </summary>
        void ApplyAutoFit(LevelData data, GameObject piecePrefab)
        {
            int cols    = Mathf.Max(1, data.columns.Length);
            int rowsMax = 1;
            foreach (var c in data.columns)
                if (c != null && c.pieces != null && c.pieces.Length > rowsMax) rowsMax = c.pieces.Length;

            float scale = ComputeScaleMultiplier(cols, rowsMax);

            // Look up per-prefab adjusts (default to 1.0 if no entry / non-positive value).
            float mainBoardAdjust = 1f;
            float handAnchorAdjust = 1f;
            if (TryGetSpacingOverride(piecePrefab, out var ov))
            {
                if (ov.mainBoardScaleAdjust > 0f)  mainBoardAdjust  = ov.mainBoardScaleAdjust;
                if (ov.handAnchorScaleAdjust > 0f) handAnchorAdjust = ov.handAnchorScaleAdjust;
            }

            // Board.transform scale = authored × F. All children inherit (columns, pieces, MainBoard).
            board.transform.localScale = authoredBoardScale * scale;

            // MainBoard gets an EXTRA per-prefab multiplier on top of its authored scale.
            // Its world scale = Board.scale × MainBoard.localScale = (authored×F) × (authoredMB × adjust).
            // When autoFitMainBoardToGrid is on, also multiply local x/y by grid-vs-standard ratio so
            // the frame shrinks for smaller grids (e.g. 2x2 → ~67% the width/height of 3x3 standard).
            // This compensates for F growing as grids shrink, which would otherwise oversize MainBoard.
            if (boardFrame == null && board != null)
                boardFrame = board.GetComponentInChildren<BoardFrame>(true);
            if (boardFrame != null)
            {
                Vector3 mbScale = authoredMainBoardScale * mainBoardAdjust;
                if (autoFitMainBoardToGrid)
                {
                    float stdCols = Mathf.Max(1, standardGrid.x);
                    float stdRows = Mathf.Max(1, standardGrid.y);
                    mbScale.x *= cols    / stdCols;
                    mbScale.y *= rowsMax / stdRows;
                    // z stays the depth multiplier — sprites are flat so z doesn't affect visual size.
                }
                boardFrame.transform.localScale = mbScale;
                boardFrame.Apply();
            }

            // HandAnchor is NOT a child of Board, so it gets F applied directly,
            // PLUS the per-prefab adjust as an extra multiplier.
            if (playerHand != null && playerHand.HandAnchor != null)
                playerHand.HandAnchor.localScale = authoredHandScale * scale * handAnchorAdjust;
        }

        /// <summary>
        /// Computes the uniform scale multiplier F for a given grid size. Checks scaleOverrides
        /// first; falls back to sqrt(standardArea / currentArea) which preserves total board area.
        /// </summary>
        float ComputeScaleMultiplier(int cols, int rows)
        {
            // Designer override has priority.
            if (scaleOverrides != null)
            {
                for (int i = 0; i < scaleOverrides.Length; i++)
                {
                    var ov = scaleOverrides[i];
                    if (ov.gridSize.x == cols && ov.gridSize.y == rows && ov.scaleMultiplier > 0f)
                        return ov.scaleMultiplier;
                }
            }

            // Default: preserve total board area.
            int sCols = Mathf.Max(1, standardGrid.x);
            int sRows = Mathf.Max(1, standardGrid.y);
            float standardArea = (float)sCols * sRows;
            float currentArea  = (float)cols * rows;
            if (currentArea < 0.0001f) return 1f;
            return Mathf.Sqrt(standardArea / currentArea);
        }

        void ClearBoard()
        {
            // Only destroy Column children — leave decorative children (e.g. MainBoard) intact.
            for (int i = board.transform.childCount - 1; i >= 0; i--)
            {
                var child = board.transform.GetChild(i);
                if (child.GetComponent<Column>() != null)
                    Destroy(child.gameObject);
            }
            if (playerHand.HeldPiece != null) Destroy(playerHand.HeldPiece.gameObject);
            playerHand.SetHeldPiece(null);
        }
    }

    /// <summary>Designer-set scale override for a specific grid size, overriding the area-preserving default.</summary>
    [System.Serializable]
    public struct GridScaleOverride
    {
        [Tooltip("Grid size (cols × rows) this override applies to.")]
        public Vector2Int gridSize;

        [Tooltip("Uniform scale multiplier. 1.0 = same as standardGrid, 0.5 = half size, 2.0 = double.")]
        public float scaleMultiplier;
    }

    // Per-prefab layout config now lives on PieceGenEntry in PrefabRegistry.cs. The old
    // PrefabSpacingOverride struct was removed when the spacingOverrides[] array on LevelLoader
    // was promoted into the standalone PrefabRegistry ScriptableObject.
}
