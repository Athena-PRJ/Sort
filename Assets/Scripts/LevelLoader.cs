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

        [Tooltip("Prefab spawned for each LevelData.ties entry. Must have a TieVisual component on the " +
                 "root with two Quad children (one for 'tie up' half, one for 'tie down' half) and " +
                 "matching materials. Leave null to disable the tie visual entirely — ties still bind " +
                 "movement (Phase B) but won't render.")]
        [SerializeField] private GameObject tieVisualPrefab;

        [Tooltip("Prefab spawned for each LevelData.frozenColumns entry. Must have a FrozenOverlay " +
                 "component on the root with a TMP_Text child for the remaining-count display and " +
                 "an optional MeshRenderer Quad for the placeholder fade tint. Leave null to skip the " +
                 "visual — the gameplay state (frozen / interaction blocks) still applies.")]
        [SerializeField] private GameObject frozenOverlayPrefab;

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

        [Tooltip("Independent of Auto Fit: auto-sizes the MainBoard FRAME to WRAP each level's grid " +
                 "(width = cols×ColumnSpacing, height = rows×PieceSpacing, × Main Board Grid Padding). " +
                 "Turn this ON while Auto Fit is OFF to get a board that hugs every grid automatically — " +
                 "WITHOUT the global uniform scaling that resizes the pieces/hand. Per-grid fine-tuning " +
                 "(scaleOverrides.mainBoardSizeMultiplier) still applies on top. Needs the board sprite assigned.")]
        [SerializeField] private bool autoSizeBoardToGrid = true;

        [Tooltip("The 'standard' grid size at which Board.columnSpacing, Column.pieceSpacing, and " +
                 "piece prefab scales were authored. The algorithm uses this as the baseline; for " +
                 "other grids it applies a uniform scale multiplier to keep total board area constant.\n\n" +
                 "Default (3, 3). Tune your scene at this grid size, then test other grids — auto-fit handles them.")]
        [SerializeField] private Vector2Int standardGrid = new Vector2Int(3, 3);

        [Tooltip("Per-grid overrides (cols × rows). One entry fine-tunes EVERYTHING for that grid: board " +
                 "scale, MainBoard size + position, column spacing, piece spacing, piece scale, and the " +
                 "status / out indicator size + offsets. Each multiplier ≤0 means 'no change'. All apply " +
                 "live in Play mode. One entry per grid you want to tune.")]
        [SerializeField] private GridScaleOverride[] scaleOverrides = new GridScaleOverride[0];

        [Header("MainBoard")]
        [Tooltip("How much bigger the MainBoard image is than the raw grid extent. 1.0 = exactly the grid; " +
                 "1.2 = 20% margin to account for the frame border drawn into the board art. The board is " +
                 "deterministically scaled to (cols×ColumnSpacing) × (rows×PieceSpacing) × this, so it fits " +
                 "ANY grid size by construction (no per-grid heuristic needed).")]
        [SerializeField] private float mainBoardGridPadding = 1.2f;

        [Tooltip("Optional reference to the BoardFrame component on the MainBoard child GameObject. " +
                 "If left null, LevelLoader auto-finds one via GetComponentInChildren on the Board.")]
        [SerializeField] private BoardFrame boardFrame;

        [Tooltip("If true, after level build, MainBoard's transform.position is auto-shifted so its " +
                 "SpriteRenderer.bounds.center sits exactly at the visual center of the spawned " +
                 "column grid. Removes the need to manually tune MainBoard.localPosition per sprite " +
                 "pivot. Designer's authored position becomes irrelevant — leave at (0,0,0) and let " +
                 "this handle alignment automatically.")]
        [SerializeField] private bool autoAlignBoardFrameToColumns = true;

        [Tooltip("Pushes the board AWAY from the camera by this many world units AFTER it's aligned to the " +
                 "grid. Use it when the board faces the camera (not coplanar with the tilted pieces): the " +
                 "board otherwise sits at mid-grid depth and hides the FAR (top) row. Increase until the top " +
                 "row reappears — the board becomes a flat backdrop behind ALL pieces while staying centered " +
                 "on the grid (works WITH Auto Align). 0 = no push. Live-tunable in Play mode.")]
        [SerializeField] private float boardCameraPush = 0f;

        [Header("Background")]
        [Tooltip("Optional reference to the BackgroundFrame component on the Background GameObject (child of Canvas). " +
                 "If left null, LevelLoader auto-finds one via FindAnyObjectByType at runtime.")]
        [SerializeField] private BackgroundFrame backgroundFrame;

        public static LevelLoader Instance { get; private set; }
        public LevelData CurrentLevel { get; private set; }

        /// <summary>The head of the level chain (typically Level1). SkillProgress walks from this.</summary>
        public LevelData DefaultLevel => defaultLevel;

        // Captured authored scale of Board, HandAnchor, and MainBoard so we can apply F + per-prefab
        // adjusts as multipliers without destroying the designer's manual scene settings. Captured once.
        Vector3 authoredBoardScale = Vector3.one;
        Vector3 authoredHandScale = Vector3.one;
        Vector3 authoredMainBoardScale = Vector3.one;
        // Authored Board position — the per-grid/per-prefab "Main Board Offset" shifts the WHOLE assembly
        // (board + columns + pieces) relative to this, so they move together. Captured once in Awake.
        Vector3 authoredBoardPosition = Vector3.zero;
        bool authoredBoardPositionCaptured;
        // Authored HandAnchor position — the per-grid "Player Hand Offset" shifts the whole hand
        // (held piece + placemat) relative to this. Captured once in Awake.
        Vector3 authoredHandAnchorPosition = Vector3.zero;
        bool authoredHandAnchorCaptured;

        // heldPieceExtraOffset is pushed straight to PlayerHand.HeldPieceLocalOffset (no authored
        // baseline needed — the held piece's "home" is localPosition=offset).

        void Awake()
        {
            Instance = this;

            if (board != null)
            {
                authoredBoardScale = board.transform.localScale;
                authoredBoardPosition = board.transform.localPosition;
                authoredBoardPositionCaptured = true;
            }
            if (playerHand != null && playerHand.HandAnchor != null)
            {
                authoredHandScale = playerHand.HandAnchor.localScale;
                authoredHandAnchorPosition = playerHand.HandAnchor.localPosition;
                authoredHandAnchorCaptured = true;
            }

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

            // Board/background art is fixed in the scene now; BackgroundFrame.Apply only fills a fallback
            // if nothing is assigned. (Auto-discover so the call is harmless if not wired.)
            if (backgroundFrame == null) backgroundFrame = FindAnyObjectByType<BackgroundFrame>(FindObjectsInactive.Include);
            if (backgroundFrame != null) backgroundFrame.Apply();

            // Refresh per-level UI radiants (badges, SkillBar, etc.) so they pull this level's theme COLORS.
            // This — not sprite swapping — is how levels differ visually now.
            var radiants = FindObjectsByType<UiRadiantTint>(FindObjectsInactive.Include);
            for (int i = 0; i < radiants.Length; i++)
                if (radiants[i] != null) radiants[i].Apply();

            // Same for world-space radiants (board frame / placemat SpriteRenderers).
            // Only DISABLED-respecting: a SpriteRadiantTint whose checkbox is off (e.g. MainBoard, which
            // now ships a finished per-theme sprite via BoardFrame instead of being recolored) must NOT
            // be re-applied — otherwise its flat-replace material would override the theme sprite's colors.
            var spriteRadiants = FindObjectsByType<SpriteRadiantTint>(FindObjectsInactive.Include);
            for (int i = 0; i < spriteRadiants.Length; i++)
                if (spriteRadiants[i] != null && spriteRadiants[i].isActiveAndEnabled) spriteRadiants[i].Apply();

            // Per-theme finished SPRITES (You Failed / Out of Moves / Win panel art per difficulty).
            // Inactive panels also refresh when shown (ThemedSprite.OnEnable), so this just covers any
            // themed element already visible at load.
            var themed = FindObjectsByType<ThemedSprite>(FindObjectsInactive.Include);
            for (int i = 0; i < themed.Length; i++)
                if (themed[i] != null) themed[i].Apply();

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
                if (col != null)
                {
                    spawnedColumns.Add(col);
                    // Only Stack Sort: this column will accept only one color's pieces (enforced in
                    // PlayerHand). Set before GameManager/MainBoardBuilder run so the restriction is
                    // live from the first frame.
                    if (colConfig.onlyStackSort)
                        col.SetOnlyStackSort(colConfig.onlyStackColor);
                }
                foreach (var pieceCfg in colConfig.pieces)
                {
                    var pieceGO = Instantiate(piecePrefab, columnGO.transform);
                    var piece = pieceGO.GetComponent<Piece>();
                    if (piece != null)
                    {
                        // SetPalette FIRST so SetConfig's ApplyVisualState call sees the palette
                        // and samples from it on the very first frame (no flash of tinted texture).
                        // Palette comes from the registry entry, not LevelData — single source per prefab.
                        piece.SetPalette(ResolvePiecePalette(data, piecePrefab));
                        piece.SetConfig(pieceCfg);
                        ApplyRegistryPieceScale(piece, piecePrefab);
                    }
                }
            }

            // Apply the per-prefab piece spacing to each spawned column (their authored field
            // stays the same, runtime override takes effect).
            ApplyPieceSpacingOverrideToColumns(piecePrefab, spawnedColumns);

            board.Layout();

            // Wire up ties — designer-authored pairs of pieces in adjacent columns that move together.
            // Spawn the visual prefab between each tied pair and cross-link the Piece.TiedPartner refs.
            // Click-time shift logic (Phase B) walks those refs to find which columns move as a chain.
            SpawnTies(data, spawnedColumns);

            // Initialize frozen columns (Break Wall Stack). Calls Column.Freeze on each configured
            // column (disables colliders → blocks interactions) and spawns the FrozenOverlay visual.
            InitializeFrozenColumns(data, spawnedColumns);

            // Spawn the held piece under the hand anchor.
            var heldGO = Instantiate(piecePrefab, playerHand.HandAnchor);
            var held = heldGO.GetComponent<Piece>();
            if (held != null)
            {
                held.SetPalette(ResolvePiecePalette(data, piecePrefab));
                held.SetConfig(data.startingHeldPiece);
                ApplyRegistryPieceScale(held, piecePrefab);
            }
            playerHand.SetHeldPiece(held);
            // Placemat art is fixed in the scene now (recolored via UiRadiantTint if needed) — no per-level
            // sprite swap. The held piece sits over it via HandAnchorFollowUI when the placemat is UI.

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
            Vector3 assemblyOffset = hasOv ? ov.mainBoardExtraOffset : Vector3.zero;
            Vector3 piOff = hasOv ? ov.heldPieceExtraOffset : Vector3.zero;

            // Per-grid "Main Board Offset" from scaleOverrides, on top of the per-prefab offset.
            GetGrid(CurrentLevel, out int gc, out int gr);
            if (TryGetGridOverride(gc, gr, out var gov)) assemblyOffset += gov.mainBoardOffset;

            // Move the WHOLE assembly (board + columns + pieces) by the offset — applied to the BOARD
            // transform, so the columns/pieces (its children) MOVE WITH the board and stay aligned. Each
            // Override level can position the board where it looks best and the pieces follow. Relative to
            // the authored position → idempotent (re-running converges to authoredPosition + offset).
            if (authoredBoardPositionCaptured)
                board.transform.localPosition = authoredBoardPosition + assemblyOffset;

            // Auto-size the board frame to wrap THIS grid first (so its bounds are correct before we
            // center it below). Runs independently of Auto Fit — see autoSizeBoardToGrid.
            if (autoSizeBoardToGrid) SizeBoardFrameToGrid();

            // Board-FRAME-only depth push: shifts JUST the frame away from the camera so a camera-facing
            // board sits BEHIND the pieces (purely a render-depth nudge — does NOT move the pieces).
            Vector3 frameOffset = Vector3.zero;
            if (Mathf.Abs(boardCameraPush) > 1e-4f)
            {
                var cam = Camera.main;
                if (cam != null) frameOffset = cam.transform.forward * boardCameraPush;
            }

            // Per-grid FRAME-ONLY offset: shifts JUST the board frame relative to the pieces (pieces stay
            // put). gc/gr were computed above. This is the "nudge only the MainBoard up a bit, keep the
            // columns" knob — distinct from mainBoardOffset (which moves the whole assembly together).
            if (TryGetGridOverride(gc, gr, out var fgov)) frameOffset += fgov.mainBoardFrameOffset;

            // Center the board frame on the (now-moved) grid + the depth push + the frame-only offset.
            // The grid centroid already reflects the assembly move above, so the frame lands on the pieces
            // (then shifts by frameOffset).
            if (autoAlignBoardFrameToColumns) AlignBoardFrameToColumns(cols, frameOffset);

            if (playerHand != null)
                playerHand.HeldPieceLocalOffset = piOff;

            // Per-grid PlayerHand offset: move the WHOLE hand (held piece + placemat) via HandAnchor,
            // relative to its authored position → idempotent. gc/gr computed above.
            if (authoredHandAnchorCaptured && playerHand != null && playerHand.HandAnchor != null)
            {
                Vector3 handOff = TryGetGridOverride(gc, gr, out var hgov) ? hgov.playerHandOffset : Vector3.zero;
                playerHand.HandAnchor.localPosition = authoredHandAnchorPosition + handOff;
            }
        }

        void OnValidate()
        {
            // Live-preview support: when designer tweaks a serialized field in Play mode, OnValidate
            // fires; rerun alignment so the change shows immediately in the running game. Skip in
            // Edit mode (no level built yet) and during build/import (CurrentLevel is null).
            if (!Application.isPlaying) return;
            if (CurrentLevel == null) return;

            var pf = ResolvePiecePrefab(CurrentLevel);
            if (pf == null || board == null) { RefreshAlignment(); return; }

            // Full live re-apply so EVERY per-grid override in scaleOverrides updates instantly while
            // tuning in Play mode (RefreshAlignment alone only re-centers — it doesn't re-scale/space):
            var cols = new System.Collections.Generic.List<Column>();
            board.GetComponentsInChildren(true, cols);

            // 1) Column + piece SPACING (per-grid columnSpacing / pieceSpacing multipliers).
            ApplyPrefabSpacingOverride(pf);
            ApplyPieceSpacingOverrideToColumns(pf, cols);

            // 2) BOARD + MainBoard SIZE (scaleMultiplier + mainBoardSizeMultiplier). Runs after spacing so
            //    the frame wraps the new spacing. Idempotent — authored scales captured once in Awake.
            if (autoFit) ApplyAutoFit(CurrentLevel, pf);

            // 3) PIECE size (per-grid pieceScaleMultiplier) — re-push baselines to every live piece.
            var pieces = new System.Collections.Generic.List<Piece>();
            board.GetComponentsInChildren(true, pieces);
            for (int i = 0; i < pieces.Count; i++) ApplyRegistryPieceScale(pieces[i], pf);
            if (playerHand != null && playerHand.HeldPiece != null)
                ApplyRegistryPieceScale(playerHand.HeldPiece, pf);

            // 4) Re-layout with the new spacing, re-align the frame + per-grid MainBoard offset, rebuild indicators.
            board.Layout();
            for (int i = 0; i < cols.Count; i++) if (cols[i] != null) cols[i].Layout();
            RefreshAlignment();
            var mbb = FindFirstObjectByType<MainBoardBuilder>();
            if (mbb != null) mbb.MarkDirty();
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
            float colSpacing = TryGetSpacingOverride(piecePrefab, out var entry) ? entry.columnSpacing : 0f;
            // Per-grid column-spacing multiplier from scaleOverrides.
            if (colSpacing > 0f && CurrentLevel != null)
            {
                GetGrid(CurrentLevel, out int gc, out int gr);
                if (TryGetGridOverride(gc, gr, out var gov) && gov.columnSpacingMultiplier > 0f)
                    colSpacing *= gov.columnSpacingMultiplier;
            }
            // 0 = clear the override so Board's authored value takes effect.
            board.SetRuntimeColumnSpacing(colSpacing);
        }

        /// <summary>
        /// Pushes the registry entry's pieceSpacing to each spawned column as a runtime override.
        /// Each Column keeps its authored pieceSpacing field; only the runtime override changes.
        /// </summary>
        void ApplyPieceSpacingOverrideToColumns(GameObject piecePrefab, System.Collections.Generic.List<Column> columns)
        {
            if (columns == null || columns.Count == 0) return;
            bool found = TryGetSpacingOverride(piecePrefab, out var entry);
            float pieceSpacing = found ? entry.pieceSpacing : 0f;
            // Per-grid piece-spacing multiplier from scaleOverrides.
            if (pieceSpacing > 0f && CurrentLevel != null)
            {
                GetGrid(CurrentLevel, out int gc, out int gr);
                if (TryGetGridOverride(gc, gr, out var gov) && gov.pieceSpacingMultiplier > 0f)
                    pieceSpacing *= gov.pieceSpacingMultiplier;
            }
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i] == null) continue;
                columns[i].SetRuntimePieceSpacing(pieceSpacing); // 0 = clear → authored value
            }
        }

        bool TryGetSpacingOverride(GameObject piecePrefab, out PieceGenEntry match)
        {
            if (registry != null) return registry.TryGetSpacing(piecePrefab, out match);
            match = default;
            return false;
        }

        /// <summary>
        /// Resolves each <see cref="LevelData.TieConfig"/> into a runtime tie: finds the two piece
        /// instances at (columnA, row) and (columnA+1, row), cross-links their <see cref="Piece.TiedPartner"/>
        /// refs, and spawns a TieVisual prefab between them. No-op when there are no ties OR when
        /// tieVisualPrefab is unassigned (the bindings still happen — only the visual is skipped).
        /// </summary>
        void SpawnTies(LevelData data, System.Collections.Generic.List<Column> cols)
        {
            if (data.ties == null || data.ties.Length == 0) return;

            for (int i = 0; i < data.ties.Length; i++)
            {
                var t = data.ties[i];
                if (t == null) continue;
                // Validation already runs in LevelData.Validate (logged via OnValidate); guard at
                // runtime too so a stale/invalid asset doesn't crash the level build.
                if (t.columnA < 0 || t.columnA >= cols.Count - 1)
                {
                    Debug.LogWarning($"[LevelLoader] Tie [{i}] columnA={t.columnA} out of range for level '{data.name}' — skipped.", this);
                    continue;
                }
                var colA = cols[t.columnA];
                var colB = cols[t.columnA + 1];
                if (colA == null || colB == null) continue;

                var pieceA = GetPieceAtRow(colA, t.row);
                var pieceB = GetPieceAtRow(colB, t.row);
                if (pieceA == null || pieceB == null)
                {
                    Debug.LogWarning($"[LevelLoader] Tie [{i}] row={t.row} resolves to a missing piece in level '{data.name}' — skipped.", this);
                    continue;
                }

                // Cross-link tied partners FIRST so Phase B logic can see the binding even if the
                // visual prefab is missing.
                pieceA.SetTiedPartner(pieceB);
                pieceB.SetTiedPartner(pieceA);

                if (tieVisualPrefab != null)
                {
                    var tieGO = Instantiate(tieVisualPrefab, board.transform);
                    var tieVis = tieGO.GetComponent<TieVisual>();
                    if (tieVis != null)
                    {
                        tieVis.Bind(pieceA, pieceB);
                    }
                    else
                    {
                        Debug.LogWarning($"[LevelLoader] tieVisualPrefab is missing a TieVisual component — tie [{i}] won't render.", this);
                        Destroy(tieGO);
                    }
                }
            }
        }

        /// <summary>
        /// Freezes the level's special columns and spawns their FrozenOverlay visuals. Two sources:
        ///   • Break Wall Stack — <see cref="LevelData.frozenColumns"/> (any locked column counts).
        ///   • Lock Color Stack — per-column flag on <see cref="ColumnConfig.lockColorStack"/> (only
        ///     columns completed in the required color count). Authored on the main Columns list so the
        ///     designer ticks a column directly to make it color-gated.
        /// GameManager owns the per-lock countdown + auto-unfreeze afterward — this only builds state.
        /// </summary>
        void InitializeFrozenColumns(LevelData data, System.Collections.Generic.List<Column> cols)
        {
            // --- Break Wall Stack (frozenColumns list, referenced by column index) ---
            if (data.frozenColumns != null)
            {
                for (int i = 0; i < data.frozenColumns.Length; i++)
                {
                    var cfg = data.frozenColumns[i];
                    if (cfg == null) continue;
                    if (cfg.columnIndex < 0 || cfg.columnIndex >= cols.Count)
                    {
                        Debug.LogWarning($"[LevelLoader] FrozenColumn [{i}] columnIndex={cfg.columnIndex} out of " +
                                         $"range for level '{data.name}' (has {cols.Count} cols) — skipped.", this);
                        continue;
                    }
                    var col = cols[cfg.columnIndex];
                    if (col == null) continue;

                    // Frozen = COUNT mode: breaks when X (unlockThreshold) ANY columns are completed.
                    col.Freeze(cfg.unlockThreshold);
                    SpawnFrozenOverlay(col, cfg.unlockThreshold);
                }
            }

            // --- Per-column gates on ColumnConfig (index aligns with cols): Break Wall Stack + Lock Color Stack ---
            if (data.columns != null)
            {
                int n = Mathf.Min(data.columns.Length, cols.Count);
                for (int i = 0; i < n; i++)
                {
                    var cc = data.columns[i];
                    if (cc == null) continue;
                    var col = cols[i];
                    if (col == null) continue;

                    if (cc.breakWallStack)
                    {
                        // Break Wall Stack = NEIGHBOR mode: breaks when its left+right neighbors complete.
                        col.FreezeNeighbors();
                        // Initial overlay number = how many existing neighbors (interior 2, edge 1) — none locked yet.
                        int neighborCount = (i > 0 ? 1 : 0) + (i < cols.Count - 1 ? 1 : 0);
                        SpawnFrozenOverlay(col, neighborCount);
                    }
                    else if (cc.lockColorStack)
                    {
                        col.Freeze(cc.lockColorUnlockThreshold, true, cc.requiredColor);
                        SpawnFrozenOverlay(col, cc.lockColorUnlockThreshold);
                    }
                }
            }
        }

        /// <summary>
        /// Instantiates the FrozenOverlay prefab parented to <paramref name="col"/>, positions/rotates it
        /// over the column, and shows the initial remaining count. No-op if no prefab assigned.
        /// </summary>
        FrozenOverlay SpawnFrozenOverlay(Column col, int threshold)
        {
            if (frozenOverlayPrefab == null) return null;
            var overlayGO = Instantiate(frozenOverlayPrefab, col.transform);
            var overlay = overlayGO.GetComponent<FrozenOverlay>();
            if (overlay == null)
            {
                Debug.LogWarning($"[LevelLoader] frozenOverlayPrefab is missing a FrozenOverlay component " +
                                 $"— overlay for '{col.name}' won't update its remaining count.", this);
                return null;
            }
            // AttachToColumn handles position (world centroid of pieces) AND rotation (lies flat over
            // the column). Robust to board / column rotation — no manual local-space math here.
            overlay.AttachToColumn(col);
            overlay.SetRemaining(threshold);
            return overlay;
        }

        /// <summary>Returns the Piece child of <paramref name="col"/> at <paramref name="row"/> (0-based from top), or null.</summary>
        static Piece GetPieceAtRow(Column col, int row)
        {
            if (col == null || row < 0) return null;
            int pieceCount = 0;
            for (int i = 0; i < col.transform.childCount; i++)
            {
                var p = col.transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                if (pieceCount == row) return p;
                pieceCount++;
            }
            return null;
        }

        /// <summary>
        /// Resolves the ColorPalette for the current level by reading the registry entry for
        /// <paramref name="piecePrefab"/> and indexing into it with <c>data.paletteStyle</c>:
        ///   - PaletteStyle.Pastel → entry.palettePastel
        ///   - PaletteStyle.Plain  → entry.palettePlain
        /// Returns null if there's no registry, no entry, or the selected slot is empty —
        /// Piece.SetPalette(null) then reverts to the legacy tint-the-default-material path.
        /// Designer configures both palettes ONCE per prefab in the registry, then per-level
        /// just toggles the enum to pick a style.
        /// </summary>
        ColorPalette ResolvePiecePalette(LevelData data, GameObject piecePrefab)
        {
            if (data == null || registry == null) return null;
            if (!registry.TryGetEntry(piecePrefab, out var entry)) return null;
            return data.paletteStyle switch
            {
                PaletteStyle.Pastel => entry.palettePastel,
                PaletteStyle.Plain  => entry.palettePlain,
                _                   => null
            };
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

            // Per-grid piece-scale override (independent of board scale) from scaleOverrides.
            float pieceMul = 1f;
            if (CurrentLevel != null)
            {
                GetGrid(CurrentLevel, out int gc, out int gr);
                if (TryGetGridOverride(gc, gr, out var gov) && gov.pieceScaleMultiplier > 0f)
                    pieceMul = gov.pieceScaleMultiplier;
            }
            piece.SetBaselineScale(entry.pieceScale * pieceMul);
        }

        /// <summary>Reads a level's grid size: cols = number of columns, rows = the tallest column.</summary>
        static void GetGrid(LevelData data, out int cols, out int rows)
        {
            cols = (data != null && data.columns != null) ? Mathf.Max(1, data.columns.Length) : 1;
            rows = 1;
            if (data != null && data.columns != null)
                foreach (var c in data.columns)
                    if (c != null && c.pieces != null && c.pieces.Length > rows) rows = c.pieces.Length;
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

            // Per-prefab HandAnchor scale adjust (MainBoard's adjust now lives in SizeBoardFrameToGrid).
            float handAnchorAdjust = 1f;
            if (TryGetSpacingOverride(piecePrefab, out var ov) && ov.handAnchorScaleAdjust > 0f)
                handAnchorAdjust = ov.handAnchorScaleAdjust;

            // Board.transform scale = authored × F. All children inherit (columns, pieces, MainBoard).
            board.transform.localScale = authoredBoardScale * scale;

            // MainBoard frame WRAP (size it to the grid) now lives in SizeBoardFrameToGrid(), run from
            // RefreshAlignment and gated by autoSizeBoardToGrid — so the board can auto-fit each grid even
            // when this global Auto Fit (F) is OFF. (Kept separate so they're independent knobs.)

            // HandAnchor scale AUTO-MATCHES the board pieces so the held piece is the same world size as a
            // piece on the board — it won't resize when it drops onto a column, at ANY grid/scale. A board
            // piece sits under a Column (under Board), so its world scale = the Column's lossyScale; we set
            // HandAnchor's world scale to that × handAnchorAdjust (use ~0.95 to make the held piece a touch
            // smaller). No dependency on the authored hand scale, so no manual matching. Falls back to the
            // old authored×F formula only if no column has spawned yet.
            if (playerHand != null && playerHand.HandAnchor != null)
            {
                var ha = playerHand.HandAnchor;
                var refCol = board.GetComponentInChildren<Column>(true);
                if (refCol != null)
                {
                    Vector3 targetWorld = refCol.transform.lossyScale * handAnchorAdjust;
                    Vector3 parentLossy = ha.parent != null ? ha.parent.lossyScale : Vector3.one;
                    ha.localScale = new Vector3(
                        Mathf.Abs(parentLossy.x) > 1e-5f ? targetWorld.x / parentLossy.x : targetWorld.x,
                        Mathf.Abs(parentLossy.y) > 1e-5f ? targetWorld.y / parentLossy.y : targetWorld.y,
                        Mathf.Abs(parentLossy.z) > 1e-5f ? targetWorld.z / parentLossy.z : targetWorld.z);
                }
                else
                {
                    ha.localScale = authoredHandScale * scale * handAnchorAdjust;
                }
            }
        }

        /// <summary>
        /// Sizes the MainBoard FRAME so it WRAPS the current grid — independent of Auto Fit's global scale.
        /// width = cols×ColumnSpacing, height = rows×PieceSpacing, × Main Board Grid Padding, ÷ the sprite's
        /// native bounds → the board hugs ANY grid automatically. Per-grid mainBoardSizeMultiplier folds in.
        /// No-op without a board sprite. Gated by autoSizeBoardToGrid; called from RefreshAlignment.
        /// </summary>
        void SizeBoardFrameToGrid()
        {
            if (board == null || CurrentLevel == null) return;
            if (boardFrame == null) boardFrame = board.GetComponentInChildren<BoardFrame>(true);
            if (boardFrame == null) return;

            GetGrid(CurrentLevel, out int cols, out int rowsMax);
            var piecePrefab = ResolvePiecePrefab(CurrentLevel);

            float mainBoardAdjust = 1f;
            if (TryGetSpacingOverride(piecePrefab, out var ov) && ov.mainBoardScaleAdjust > 0f)
                mainBoardAdjust = ov.mainBoardScaleAdjust;
            if (TryGetGridOverride(cols, rowsMax, out var gov) && gov.mainBoardSizeMultiplier > 0f)
                mainBoardAdjust *= gov.mainBoardSizeMultiplier;

            boardFrame.Apply(); // ensure the sprite is present so its native bounds are valid
            var sr = boardFrame.GetComponent<SpriteRenderer>();
            float pieceSpacing = FirstColumnPieceSpacing();
            Vector3 mbScale = authoredMainBoardScale; // keeps authored z (depth)
            if (sr != null && sr.sprite != null && pieceSpacing > 0f)
            {
                Vector3 spriteSize = sr.sprite.bounds.size;
                float gridLocalW = cols    * board.ColumnSpacing;
                float gridLocalH = rowsMax * pieceSpacing;
                if (spriteSize.x > 1e-4f) mbScale.x = (gridLocalW * mainBoardGridPadding) / spriteSize.x * mainBoardAdjust;
                if (spriteSize.y > 1e-4f) mbScale.y = (gridLocalH * mainBoardGridPadding) / spriteSize.y * mainBoardAdjust;
            }
            else
            {
                mbScale.x *= mainBoardAdjust;
                mbScale.y *= mainBoardAdjust;
            }
            boardFrame.transform.localScale = mbScale;
        }

        /// <summary>PieceSpacing of the first spawned Column (Board-local units), or 0 if none found yet.</summary>
        float FirstColumnPieceSpacing()
        {
            var col = board != null ? board.GetComponentInChildren<Column>(true) : null;
            return col != null ? col.PieceSpacing : 0f;
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

        /// <summary>
        /// Finds the <see cref="GridScaleOverride"/> entry for a grid size (cols × rows), if any. Used by
        /// MainBoardBuilder to read the per-grid INDICATOR tweaks (size / offsets) from the SAME list that
        /// tunes board scale, so one place fine-tunes both the board and its indicators for a grid.
        /// </summary>
        public bool TryGetGridOverride(int cols, int rows, out GridScaleOverride ov)
        {
            if (scaleOverrides != null)
                for (int i = 0; i < scaleOverrides.Length; i++)
                    if (scaleOverrides[i].gridSize.x == cols && scaleOverrides[i].gridSize.y == rows)
                    { ov = scaleOverrides[i]; return true; }
            ov = default;
            return false;
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
        [Tooltip("Grid size (cols × rows) this override applies to. One entry tunes BOTH the board scale " +
                 "and the status / out indicator UI for this grid.")]
        public Vector2Int gridSize;

        [Tooltip("Uniform BOARD scale multiplier. 1.0 = same as standardGrid, 0.5 = half, 2.0 = double. " +
                 "Set ≤ 0 to leave the board on the area-preserving default and tune ONLY the other fields.")]
        public float scaleMultiplier;

        [Tooltip("PIECE scale multiplier for this grid — multiplies the prefab's pieceScale for the pieces " +
                 "ONLY (independent of the board scale above), so you can make tiles bigger / smaller without " +
                 "resizing the board. ≤ 0 = 1 (no change).")]
        public float pieceScaleMultiplier;

        [Tooltip("Extra WORLD-space offset that MOVES the WHOLE assembly (MainBoard + columns + pieces) for " +
                 "this grid, on top of the auto-centering on the column grid. Board and pieces move together.")]
        public Vector3 mainBoardOffset;

        [Tooltip("Extra WORLD-space offset that shifts ONLY the MainBoard FRAME (the background image) " +
                 "relative to the columns/pieces — the pieces STAY PUT. Use to nudge just the board up/down " +
                 "for better margin balance while the tiles keep their place. The frame is normally " +
                 "auto-centered on the pieces; this offsets it from that center. Try Y first (and/or Z, since " +
                 "the board is tilted) — small values like (0, 0.3, 0). Opposite intent of Main Board Offset.")]
        public Vector3 mainBoardFrameOffset;

        [Tooltip("Offset (in HandAnchor's local space) that moves the WHOLE PlayerHand — the held piece AND " +
                 "the placemat under it — for this grid. Use to reposition the hand up/down/sideways relative " +
                 "to the board. X = left/right, Y = up/down, Z = depth. Independent of the per-prefab " +
                 "'heldPieceExtraOffset' (which only nudges the held piece WITHIN the hand).")]
        public Vector3 playerHandOffset;

        [Tooltip("MainBoard SIZE multiplier for this grid — scales the board image itself (it still wraps " +
                 "the grid; this scales it up / down on top). ≤ 0 = 1 (no change).")]
        public float mainBoardSizeMultiplier;

        [Tooltip("COLUMN spacing multiplier for this grid — multiplies the horizontal gap between columns " +
                 "(the board frame re-wraps to fit). ≤ 0 = 1 (no change).")]
        public float columnSpacingMultiplier;

        [Tooltip("PIECE spacing multiplier for this grid — multiplies the vertical gap between stacked " +
                 "pieces in a column (the board frame re-wraps to fit). ≤ 0 = 1 (no change).")]
        public float pieceSpacingMultiplier;

        [Tooltip("INDICATOR: multiplies ALL status / out / tick icon sizes for this grid (on top of the auto " +
                 "row-compensation). ≤ 0 = 1 (no change).")]
        public float indicatorSizeMultiplier;

        [Tooltip("INDICATOR: extra MainBoard-local nudge for the TOP status frame on this grid (type X / Y / Z).")]
        public Vector3 indicatorStatusOffset;

        [Tooltip("INDICATOR: extra MainBoard-local nudge for the BOTTOM out arrow on this grid.")]
        public Vector3 indicatorOutOffset;
    }

    // Per-prefab layout config now lives on PieceGenEntry in PrefabRegistry.cs. The old
    // PrefabSpacingOverride struct was removed when the spacingOverrides[] array on LevelLoader
    // was promoted into the standalone PrefabRegistry ScriptableObject.
}
