using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    [CreateAssetMenu(menuName = "Sort/Level Data", fileName = "Level")]
    public class LevelData : ScriptableObject
    {
        [Tooltip("Level number shown in UI. Use 1 for the first level.")]
        public int levelNumber = 1;

        [Tooltip("Difficulty tag shown in the Level badge (Easy, Normal, Hard, SuperHard, Expert).")]
        public LevelDifficulty difficulty = LevelDifficulty.Easy;

        [PrefabPicker]
        [Tooltip("Which piece prefab this level spawns. Dropdown is sourced from PrefabRegistry — " +
                 "add entries there to expand the available options. The registry entry carries " +
                 "the DEFAULT color palette for this prefab (BoxPastelPalette for Box, etc.); the " +
                 "field below can OVERRIDE that default if this level needs a different style.")]
        public GameObject piecePrefab;

        [Tooltip("Which palette style this level uses. Both styles (Pastel + Plain) are pre-configured " +
                 "in the PrefabRegistry entry for the chosen piecePrefab; this enum just picks one. " +
                 "Designer flow: set up the 2 palette assets once per prefab in PrefabRegistry, then " +
                 "every level just toggles this enum to switch styles — no asset assignment per level.")]
        public PaletteStyle paletteStyle = PaletteStyle.Pastel;

        [Tooltip("Frame sprite used for this level's board background. Assign one of Assets/Texture/FullBoard/ (MB_1, MB_2, MB_3, etc.) " +
                 "to give each level its own board color. If left null, BoardFrame's own fallback sprite is used.")]
        public Sprite mainBoardSprite;

        [Tooltip("Background image displayed behind the gameplay for this level. Assign one of Assets/Texture/Background/ " +
                 "(BG_1, BG_2, BG_3, etc.). If left null, BackgroundFrame's own fallback sprite is used.")]
        public Sprite backgroundSprite;

        [Tooltip("Per-column 'out' arrow shown BELOW each column (Assets/Updated/SVG/out 1·2·3). Pick the " +
                 "variant matching this level's board theme. If null, no out arrow is shown.")]
        public Sprite outSprite;

        [Tooltip("Placemat shown UNDER the player's held piece (Assets/Updated/SVG/place 1·2·3). Pick the " +
                 "variant matching this level's board theme. If null, the hand keeps its existing decoration sprite.")]
        public Sprite placeSprite;

        [Tooltip("Per-column status icon shown ABOVE each column while it is NOT yet solved " +
                 "(Assets/Updated/SVG/not done). MainBoardBuilder spawns one per column.")]
        public Sprite notDoneSprite;

        [Tooltip("Per-column status icon shown ABOVE a column once it IS solved (replaces Not Done, same slot).")]
        public Sprite doneSprite;

        [Tooltip("Tick overlaid on the Done icon when a column is solved (Assets/Updated/SVG/tick).")]
        public Sprite tickSprite;

        [Tooltip("Maximum moves the player has before losing. 0 = unlimited.")]
        public int moveLimit = 20;

        [Tooltip("The piece the player starts holding. Can also be a Rainbow.")]
        public PieceConfig startingHeldPiece = new PieceConfig { color = "" };

        [Tooltip("One entry per column. The first PieceConfig is the top piece.")]
        public ColumnConfig[] columns = new ColumnConfig[0];

        [Tooltip("Tie configurations — designer authors pairs of pieces in adjacent columns that move together. " +
                 "Each entry binds (columnA, row) with (columnA+1, row). When the player clicks any column in a " +
                 "tied chain, all tied columns shift simultaneously: the clicked column's bottom piece pops to " +
                 "hand normally, the OTHER tied columns' bottom pieces wrap back to the top of their own column. " +
                 "Tie breaks when its pieces reach the bottom row.")]
        public TieConfig[] ties = new TieConfig[0];

        [Tooltip("Frozen-column configurations (Break Wall Stack) — each entry locks a column behind a " +
                 "'complete X other columns first' requirement. While frozen, the column blocks ALL " +
                 "interactions: click, Switch, Magnet, even rainbow-sink. When the player has locked " +
                 "X other columns, this column unfreezes automatically and becomes playable. Multiple " +
                 "frozen columns per level are supported.")]
        public FrozenColumnConfig[] frozenColumns = new FrozenColumnConfig[0];

        [Tooltip("The level that follows this one. Used by the 'Next Level' button.")]
        public LevelData nextLevel;

        [Tooltip("Questionmark pieces reveal when their slot is within this many rows of the bottom.")]
        [Min(1)] public int questionmarkRevealFromBottom = 2;

        [Header("Rewards & Skills")]
        [Tooltip("Coins awarded to the player when they complete this level.")]
        [Min(0)] public int coinReward = 10;

        [Tooltip("Number of free Rewind uses on this level. After they run out, the player must spend coins for more.")]
        [Min(0)] public int freeRewindUses = 1;

        [Tooltip("If true, completing this level permanently unlocks the Switch skill for the player. " +
                 "Multiple levels can be flagged — the skill unlocks the first time ANY flagged level is cleared.")]
        public bool unlocksSwitchOnCompletion = false;

        [Tooltip("If true, completing this level permanently unlocks the Magnet skill for the player. " +
                 "Multiple levels can be flagged — the skill unlocks the first time ANY flagged level is cleared.")]
        public bool unlocksMagnetOnCompletion = false;

        void OnValidate()
        {
            var result = Validate();
            if (!result.IsValid)
                Debug.LogWarning($"[LevelData] '{name}' has validation issues — see Inspector for details.", this);
        }

        /// <summary>
        /// Checks that this level is solvable: same number of distinct non-rainbow colors as columns,
        /// each color appearing at least once per column row.
        /// </summary>
        public ValidationResult Validate()
        {
            var result = new ValidationResult();

            if (columns == null || columns.Length == 0)
            {
                result.errors.Add("Level has no columns — set the Columns array size to at least 1.");
                return result;
            }

            result.columnCount = columns.Length;

            // Determine the expected row count (max length across columns; columns must all match it).
            int rowCount = 0;
            for (int i = 0; i < columns.Length; i++)
            {
                int len = columns[i]?.pieces?.Length ?? 0;
                if (len > rowCount) rowCount = len;
            }
            result.rowCount = rowCount;

            if (rowCount == 0)
            {
                result.errors.Add("Columns are empty — set each column's Pieces array size to at least 1.");
                return result;
            }

            // Rule 1: every column must have the same row count.
            for (int i = 0; i < columns.Length; i++)
            {
                int len = columns[i]?.pieces?.Length ?? 0;
                if (len != rowCount)
                    result.errors.Add($"Column [{i}] has {len} pieces but expected {rowCount} — all columns must have the same row count.");
            }

            // Tally pieces by color (excluding rainbows). Questionmarks count as their underlying color.
            var colorCounts = new Dictionary<string, int>();
            int rainbowCount = 0;

            void Count(PieceConfig p)
            {
                if (p == null) return;
                if (p.isRainbow) { rainbowCount++; return; }
                if (!colorCounts.TryGetValue(p.color, out int n)) n = 0;
                colorCounts[p.color] = n + 1;
            }

            foreach (var col in columns)
            {
                if (col?.pieces == null) continue;
                foreach (var p in col.pieces) Count(p);
            }
            Count(startingHeldPiece);

            result.colorCounts = colorCounts;
            result.rainbowCount = rainbowCount;

            // Rule 2: distinct color count must equal column count.
            if (colorCounts.Count != columns.Length)
            {
                var listed = colorCounts.Count == 0 ? "<none>" : string.Join(", ", colorCounts.Keys);
                result.errors.Add(
                    $"Expected exactly {columns.Length} distinct colors (one per column), but found {colorCounts.Count}: [{listed}].");
            }

            // Rule 3: every color must have at least rowCount pieces.
            foreach (var kv in colorCounts)
            {
                if (kv.Value < rowCount)
                    result.errors.Add($"Color '{kv.Key}' has only {kv.Value} pieces — needs at least {rowCount} (one full column).");
            }

            // Rule 4 (ties): each tie must reference a valid adjacent-column pair + valid row,
            // and no two ties may share the same (columnA, row) pair.
            if (ties != null && ties.Length > 0)
            {
                var seenTieKeys = new HashSet<long>();
                for (int i = 0; i < ties.Length; i++)
                {
                    var t = ties[i];
                    if (t == null) continue;
                    if (t.columnA < 0 || t.columnA >= columns.Length - 1)
                    {
                        result.errors.Add($"Tie [{i}]: columnA={t.columnA} is out of range — must be in [0, {columns.Length - 2}] so columnA+1 is valid.");
                        continue;
                    }
                    if (t.row < 0 || t.row >= rowCount)
                    {
                        result.errors.Add($"Tie [{i}]: row={t.row} is out of range — must be in [0, {rowCount - 1}].");
                        continue;
                    }
                    long key = ((long)t.columnA << 32) | (uint)t.row;
                    if (!seenTieKeys.Add(key))
                        result.errors.Add($"Tie [{i}]: duplicate tie at (columnA={t.columnA}, row={t.row}). Each (column-pair, row) can have at most one tie.");
                }
            }

            // Rule 5 (freeze gating): Break Wall Stack (frozenColumns) + Lock Color Stack (per-column
            // tick). Catches conflicts and checks the level is still winnable once columns are gated.
            ValidateFreezeGating(result, rowCount);

            // Rule 6 (Only Stack Sort): per-column color-restriction — the required color must exist
            // and there must be enough columns of it to fill every column restricted to that color.
            ValidateOnlyStackSort(result, rowCount);

            return result;
        }

        /// <summary>
        /// Validates Only Stack Sort columns (each accepts only one color's pieces): the required color
        /// must exist in the level, and there must be enough columns of that color to fill every column
        /// restricted to it (each restricted column ends up that color).
        /// </summary>
        void ValidateOnlyStackSort(ValidationResult result, int rowCount)
        {
            if (columns == null || columns.Length == 0 || rowCount <= 0) return;

            // Mechanic exclusivity: a column may use only ONE special mechanic. Only Stack Sort must
            // not share a column with Lock Color Stack or a Break Wall (Frozen Columns) entry.
            var breakWallIdx = new HashSet<int>();
            if (frozenColumns != null)
                foreach (var f in frozenColumns)
                    if (f != null && f.columnIndex >= 0 && f.columnIndex < columns.Length)
                        breakWallIdx.Add(f.columnIndex);
            for (int i = 0; i < columns.Length; i++)
            {
                var c = columns[i];
                if (c == null || !c.onlyStackSort) continue;
                if (c.lockColorStack)
                    result.errors.Add($"Column [{i}] uses BOTH Only Stack Sort and Lock Color Stack. A column may use " +
                                      $"only ONE special mechanic — untick one of them.");
                if (breakWallIdx.Contains(i))
                    result.errors.Add($"Column [{i}] uses BOTH Only Stack Sort and a Frozen (Break Wall) column. A column " +
                                      $"may use only ONE special mechanic — remove it from Frozen Columns OR untick Only Stack Sort.");
            }

            var colorColumns = new Dictionary<string, int>();
            if (result.colorCounts != null)
                foreach (var kv in result.colorCounts) colorColumns[kv.Key] = kv.Value / rowCount;

            var demandByColor = new Dictionary<string, int>();
            for (int i = 0; i < columns.Length; i++)
            {
                var c = columns[i];
                if (c == null || !c.onlyStackSort) continue;

                int avail = colorColumns.TryGetValue(c.onlyStackColor, out int a) ? a : 0;
                if (avail == 0)
                {
                    result.errors.Add($"Column [{i}] (Only Stack Sort: {c.onlyStackColor}) accepts only {c.onlyStackColor} " +
                                      $"pieces, but this level has none of that color. Add {c.onlyStackColor} pieces or change the color.");
                    continue;
                }
                demandByColor[c.onlyStackColor] = (demandByColor.TryGetValue(c.onlyStackColor, out int d) ? d : 0) + 1;
            }

            foreach (var kv in demandByColor)
            {
                int avail = colorColumns.TryGetValue(kv.Key, out int a) ? a : 0;
                if (kv.Value > avail)
                    result.errors.Add($"{kv.Value} columns are restricted to Only Stack Sort = {kv.Key}, but the level can " +
                                      $"only make {avail} {kv.Key} column(s). At most {avail} column(s) can be restricted to {kv.Key}.");
            }
        }

        /// <summary>
        /// Validates the two freeze mechanics together (Break Wall Stack + Lock Color Stack):
        ///   • a column can't use BOTH mechanics at once (error);
        ///   • a Lock Color threshold can't ask for more columns of a color than the level can make;
        ///   • there must be a reachable unlock order — completing open columns unlocks gated ones,
        ///     and so on — otherwise the level is a freeze deadlock and can never be won.
        /// The reachability pass is OPTIMISTIC about which column solves to which color (the player
        /// chooses the sort order), so it only flags definite deadlocks — it won't false-fail a level.
        /// </summary>
        void ValidateFreezeGating(ValidationResult result, int rowCount)
        {
            if (columns == null || columns.Length == 0 || rowCount <= 0) return;
            int n = columns.Length;

            var isBreakWall  = new bool[n];
            var breakWallT   = new int[n];
            var isLockColor  = new bool[n];
            var lockColorT   = new int[n];
            var lockColorReq = new string[n];

            if (frozenColumns != null)
            {
                foreach (var f in frozenColumns)
                {
                    if (f == null) continue;
                    if (f.columnIndex < 0 || f.columnIndex >= n)
                    {
                        result.errors.Add($"Frozen Columns: columnIndex {f.columnIndex} is out of range [0, {n - 1}].");
                        continue;
                    }
                    isBreakWall[f.columnIndex] = true;
                    breakWallT[f.columnIndex]  = Mathf.Max(1, f.unlockThreshold);
                }
            }
            for (int i = 0; i < n; i++)
            {
                var c = columns[i];
                if (c == null || !c.lockColorStack) continue;
                isLockColor[i]  = true;
                lockColorT[i]   = Mathf.Max(1, c.lockColorUnlockThreshold);
                lockColorReq[i] = c.requiredColor;
            }

            bool anyGate = false;
            for (int i = 0; i < n; i++) if (isBreakWall[i] || isLockColor[i]) { anyGate = true; break; }
            if (!anyGate) return;

            // 5a: a column cannot be BOTH a Break Wall (frozenColumns) and an Lock Color Stack column.
            for (int i = 0; i < n; i++)
                if (isBreakWall[i] && isLockColor[i])
                    result.errors.Add($"Column [{i}] is configured as BOTH a Frozen (Break Wall Stack) column AND an " +
                                      $"Lock Color Stack column. A column can only use one freeze mechanic — remove it " +
                                      $"from Frozen Columns OR untick Lock Color Stack on that column.");

            // How many columns of each color the SOLVED board can make (each color fills count/rows columns).
            var colorColumns = new Dictionary<string, int>();
            if (result.colorCounts != null)
                foreach (var kv in result.colorCounts) colorColumns[kv.Key] = kv.Value / rowCount;

            // 5b: a Lock Color threshold can't exceed how many columns of that color exist.
            for (int i = 0; i < n; i++)
            {
                if (!isLockColor[i]) continue;
                int avail = colorColumns.TryGetValue(lockColorReq[i], out int v) ? v : 0;
                if (lockColorT[i] > avail)
                    result.errors.Add($"Column [{i}] (Lock Color Stack: {lockColorReq[i]} ×{lockColorT[i]}) needs " +
                                      $"{lockColorT[i]} completed {lockColorReq[i]} column(s), but this level can only make " +
                                      $"{avail}. Lower the threshold or add more {lockColorReq[i]} pieces.");
                else if (lockColorT[i] == avail && avail > 0)
                    result.warnings.Add($"Column [{i}] (Lock Color Stack: {lockColorReq[i]} ×{lockColorT[i]}) needs ALL " +
                                       $"{avail} {lockColorReq[i]} column(s) — feasible only if column [{i}] itself does NOT " +
                                       $"solve to {lockColorReq[i]} (a column can't count toward its own unlock).");
            }

            // 5c: need at least one initially-open column to make the first completion.
            int openCount = 0;
            for (int i = 0; i < n; i++) if (!isBreakWall[i] && !isLockColor[i]) openCount++;
            if (openCount == 0)
            {
                result.errors.Add("Every column is frozen (Break Wall and/or Lock Color) — there's no open column to " +
                                  "complete first, so nothing can ever unlock. Leave at least one column ungated.");
                return; // reachability below is moot
            }

            // 5d: reachability fixpoint. Optimistically complete every currently-unlocked column; that
            // raises the locked totals, which may unlock gated columns; repeat until stable.
            var unlocked = new bool[n];
            int unlockedCount = 0;
            for (int i = 0; i < n; i++)
                if (!isBreakWall[i] && !isLockColor[i]) { unlocked[i] = true; unlockedCount++; }

            bool changed = true;
            while (changed)
            {
                changed = false;
                int lockedTotal = unlockedCount; // all currently-unlocked columns are completable → lockable
                for (int i = 0; i < n; i++)
                {
                    if (unlocked[i]) continue;
                    bool canUnlock = false;
                    if (isBreakWall[i])
                        canUnlock = lockedTotal >= breakWallT[i];
                    else if (isLockColor[i])
                    {
                        int availX = colorColumns.TryGetValue(lockColorReq[i], out int v) ? v : 0;
                        int lockedOfX = Mathf.Min(availX, lockedTotal); // optimistic: locked set holds enough of color X
                        canUnlock = lockedOfX >= lockColorT[i];
                    }
                    if (canUnlock) { unlocked[i] = true; unlockedCount++; changed = true; }
                }
            }

            if (unlockedCount < n)
            {
                var stuck = new List<int>();
                for (int i = 0; i < n; i++) if (!unlocked[i]) stuck.Add(i);
                result.errors.Add($"Freeze deadlock: column(s) [{string.Join(", ", stuck)}] can never unlock by completing " +
                                  $"others — the unlock thresholds are too high for any valid order. Lower their thresholds " +
                                  $"or reduce how many columns are gated.");
            }
        }

        public class ValidationResult
        {
            public List<string> errors = new();
            public List<string> warnings = new();
            public Dictionary<string, int> colorCounts;
            public int columnCount;
            public int rowCount;
            public int rainbowCount;
            // Warnings don't block validity — they flag risky-but-possibly-fine setups (e.g. an Only
            // Color threshold that uses every column of its color).
            public bool IsValid => errors.Count == 0;
        }
    }

    [System.Serializable]
    public class ColumnConfig
    {
        [Tooltip("Pieces from top to bottom.")]
        public PieceConfig[] pieces = new PieceConfig[0];

        [Tooltip("LOCK COLOR STACK: tick to make THIS column start frozen, unlockable only by " +
                 "completing other columns of 'Required Color'. Locking columns of other colors does " +
                 "NOT progress the unlock. Leave OFF for a normal column. (Plain Break Wall Stack — any " +
                 "color counts — is configured separately in LevelData.frozenColumns.)")]
        public bool lockColorStack = false;

        [Tooltip("Lock Color Stack: the color whose completed columns count toward unlocking this " +
                 "column. Only columns completed in THIS color advance the unlock; other colors don't. " +
                 "(The FrozenOverlay shows a remaining-count number — it is NOT tinted to this color; " +
                 "surface the color via art on the overlay later if you want it visible.) " +
                 "Ignored when 'Lock Color Stack' is OFF.")]
        [PaletteColor]
        public string requiredColor = "";

        [Tooltip("Lock Color Stack: how many columns of 'Required Color' the player must complete " +
                 "before this column unfreezes. Keep ≤ the number of completable columns in that color. " +
                 "Ignored when 'Lock Color Stack' is OFF.")]
        [Min(1)] public int lockColorUnlockThreshold = 1;

        [Tooltip("ONLY STACK SORT: tick to make THIS column accept ONLY pieces of 'Only Stack Color'. " +
                 "The player cannot drop a non-matching piece here (rainbows are wild). " +
                 "(Tint was removed — the status indicator is NOT colored to show the restriction; " +
                 "render the color's BaseMap as an icon later if you want it visible to the player.) " +
                 "MUTUALLY EXCLUSIVE with Lock Color Stack and Break Wall (Frozen Columns) — a column " +
                 "may use only ONE special mechanic (validation enforces this).")]
        public bool onlyStackSort = false;

        [Tooltip("Only Stack Sort: the ONLY color this column accepts. Ignored when 'Only Stack Sort' " +
                 "is OFF. Make sure the level can actually fill this column with that color.")]
        [PaletteColor]
        public string onlyStackColor = "";
    }

    /// <summary>
    /// Break Wall Stack entry: which column starts locked behind a "complete N OTHER columns first"
    /// gate (any color counts). At runtime LevelLoader freezes <see cref="columnIndex"/> via
    /// <see cref="Column.Freeze"/>, disables its piece colliders, and shows a <see cref="FrozenOverlay"/>
    /// with the remaining locked-count. GameManager unfreezes it when total locked columns ≥
    /// <see cref="unlockThreshold"/>.
    ///
    /// The color-gated sibling (Lock Color Stack) is authored separately — per column on
    /// <see cref="ColumnConfig.lockColorStack"/> — NOT here.
    /// </summary>
    [System.Serializable]
    public class FrozenColumnConfig
    {
        [Tooltip("0-based index of the column that starts frozen.")]
        public int columnIndex;

        [Tooltip("How many OTHER columns must the player lock (complete) before this column " +
                 "unfreezes. Designer must keep this ≤ (totalColumns − frozenColumns.Length) for " +
                 "the level to remain winnable.")]
        [Min(1)] public int unlockThreshold = 1;
    }

    /// <summary>
    /// Designer-authored tie binding two pieces at the same row in adjacent columns.
    /// The right column is implicitly <c>columnA + 1</c>. At runtime LevelLoader resolves the
    /// two Piece instances and wires <see cref="Piece.TiedPartner"/> on both sides; click-time
    /// shift logic (Phase B) then walks the tied-partner graph to find which columns move together.
    /// </summary>
    [System.Serializable]
    public class TieConfig
    {
        [Tooltip("Index of the LEFT column in the tied pair (0-based). The right column is " +
                 "implicitly columnA + 1 — ties only bind directly-adjacent columns. Must be in " +
                 "the range [0, columns.Length - 2].")]
        public int columnA;

        [Tooltip("Row index (0 = top). Both tied pieces sit at this row. Must be in [0, rowCount - 1].")]
        public int row;
    }

    [System.Serializable]
    public class PieceConfig
    {
        [PaletteColor]
        public string color = "";

        [Tooltip("Wildcard — counts as no specific color, auto-sinks to bottom when column is otherwise sorted.")]
        public bool isRainbow;

        [Tooltip("Hidden — shows as '?' until pushed near the bottom of its column, then reveals its true color.")]
        public bool isQuestionmark;
    }
}
