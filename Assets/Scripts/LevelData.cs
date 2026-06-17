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

        [Header("UI Theme — radiant color set")]
        [Tooltip("Pick a UI Theme Palette asset ('Bộ màu'). It defines the radiant COLOR slots (In+Out / " +
                 "Not Done / Accent / Text / Backup) shared across this level's UI via UiRadiantTint. Sprites " +
                 "are fixed in the scene now, not here. Leave null = white colors.")]
        public UiThemePalette themeSet;

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

        /// <summary>Per-theme board frame sprite (from themeSet), or null.</summary>
        public Sprite MainBoardSprite  => themeSet != null ? themeSet.mainBoardSprite  : null;
        /// <summary>Per-theme background sprite (from themeSet), or null.</summary>
        public Sprite BackgroundSprite => themeSet != null ? themeSet.backgroundSprite : null;

        // White fallback shared across levels that haven't picked a themeSet (keeps GetThemeColor non-null).
        static GradientColor _defaultTheme;

        /// <summary>Returns the radiant for a theme slot from the assigned themeSet (never null —
        /// falls back to white when no set is assigned).</summary>
        public GradientColor GetThemeColor(UiThemeSlot slot)
        {
            if (themeSet != null) return themeSet.Get(slot);
            return _defaultTheme ?? (_defaultTheme = new GradientColor());
        }

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

            // Rule 7 (tie vs special columns): a tie binds two adjacent columns so they move together,
            // which is incompatible with ANY special-column mechanic (Frozen / Lock Color / Only Stack).
            ValidateTieMechanicConflicts(result);

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
            // not share a column with Lock Color Stack, Break Wall Stack, or a Frozen Columns entry.
            var frozenIdx = new HashSet<int>();
            if (frozenColumns != null)
                foreach (var f in frozenColumns)
                    if (f != null && f.columnIndex >= 0 && f.columnIndex < columns.Length)
                        frozenIdx.Add(f.columnIndex);
            for (int i = 0; i < columns.Length; i++)
            {
                var c = columns[i];
                if (c == null || !c.onlyStackSort) continue;
                if (c.lockColorStack)
                    result.errors.Add($"Column [{i}] uses BOTH Only Stack Sort and Lock Color Stack. A column may use " +
                                      $"only ONE special mechanic — untick one of them.");
                if (c.breakWallStack)
                    result.errors.Add($"Column [{i}] uses BOTH Only Stack Sort and Break Wall Stack. A column may use " +
                                      $"only ONE special mechanic — untick one of them.");
                if (frozenIdx.Contains(i))
                    result.errors.Add($"Column [{i}] uses BOTH Only Stack Sort and Frozen. A column may use only ONE " +
                                      $"special mechanic — remove it from Frozen Columns OR untick Only Stack Sort.");
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
        /// A tie binds two ADJACENT columns (columnA and columnA+1) so their pieces move together. That is
        /// fundamentally incompatible with ANY special-column mechanic — Frozen/Break Wall (the column must
        /// stay inert until it unfreezes), Lock Color Stack (same), and Only Stack Sort (placement-restricted).
        /// If a tie binds such a column, moving the tie drags the special column and tapping the frozen column
        /// moves its tied partner — the Level4 bug. So: ERROR on any tie touching a special column. This is the
        /// rule that guarantees Frozen (and the other mechanics) can never combine with Tie. When adding a NEW
        /// mechanic later, mark its columns in the `special` map below so ties keep excluding it too.
        /// </summary>
        void ValidateTieMechanicConflicts(ValidationResult result)
        {
            if (ties == null || ties.Length == 0 || columns == null || columns.Length == 0) return;
            int n = columns.Length;

            // Label each special column (null = ordinary). First mechanic found wins the label; a column
            // using two mechanics is already flagged by Rule 5a / Rule 6.
            var special = new string[n];
            if (frozenColumns != null)
                foreach (var f in frozenColumns)
                    if (f != null && f.columnIndex >= 0 && f.columnIndex < n)
                        special[f.columnIndex] = "Frozen";
            for (int i = 0; i < n; i++)
            {
                var c = columns[i];
                if (c == null || special[i] != null) continue;
                if (c.breakWallStack)     special[i] = "Break Wall Stack";
                else if (c.lockColorStack) special[i] = "Lock Color Stack";
                else if (c.onlyStackSort)  special[i] = "Only Stack Sort";
            }

            for (int i = 0; i < ties.Length; i++)
            {
                var t = ties[i];
                if (t == null) continue;
                if (t.columnA < 0 || t.columnA >= n - 1) continue; // out-of-range already errored in Rule 4

                int a = t.columnA, b = t.columnA + 1;
                if (special[a] != null)
                    result.errors.Add($"Tie [{i}] binds column {a}, which is a {special[a]} column. A tie can't bind a " +
                                      $"special/frozen column — moving the tie would drag it (and tapping the frozen column " +
                                      $"would move its partner). Remove the tie, or remove the mechanic on column {a}.");
                if (special[b] != null)
                    result.errors.Add($"Tie [{i}] binds column {b}, which is a {special[b]} column. A tie can't bind a " +
                                      $"special/frozen column — moving the tie would drag it (and tapping the frozen column " +
                                      $"would move its partner). Remove the tie, or remove the mechanic on column {b}.");
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

            // THREE distinct gate mechanics (mutually exclusive per column):
            //  • Frozen   (COUNT, frozenColumns)         → breaks when X ANY columns are completed.
            //  • BreakWall(NEIGHBOR, ColumnConfig)       → breaks when its left+right neighbors complete.
            //  • LockColor(COUNT of a color, ColumnConfig)→ breaks when X columns of requiredColor complete.
            var isFrozen     = new bool[n];
            var frozenT      = new int[n];
            var isBreakWall  = new bool[n];
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
                    isFrozen[f.columnIndex] = true;
                    frozenT[f.columnIndex]  = Mathf.Max(1, f.unlockThreshold);
                }
            }
            for (int i = 0; i < n; i++)
            {
                var c = columns[i];
                if (c == null) continue;
                if (c.breakWallStack) isBreakWall[i] = true;
                if (c.lockColorStack)
                {
                    isLockColor[i]  = true;
                    lockColorT[i]   = Mathf.Max(1, c.lockColorUnlockThreshold);
                    lockColorReq[i] = c.requiredColor;
                }
            }

            bool anyGate = false;
            for (int i = 0; i < n; i++) if (isFrozen[i] || isBreakWall[i] || isLockColor[i]) { anyGate = true; break; }
            if (!anyGate) return;

            // 5a: a column may use only ONE freeze mechanic (Frozen / Break Wall / Lock Color).
            for (int i = 0; i < n; i++)
            {
                int gates = (isFrozen[i] ? 1 : 0) + (isBreakWall[i] ? 1 : 0) + (isLockColor[i] ? 1 : 0);
                if (gates > 1)
                    result.errors.Add($"Column [{i}] uses MORE THAN ONE freeze mechanic (Frozen / Break Wall Stack / " +
                                      $"Lock Color Stack). A column may use only ONE — keep one and remove the others.");
            }

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
            for (int i = 0; i < n; i++) if (!isFrozen[i] && !isBreakWall[i] && !isLockColor[i]) openCount++;
            if (openCount == 0)
            {
                result.errors.Add("Every column is gated (Frozen / Break Wall / Lock Color) — there's no open column to " +
                                  "complete first, so nothing can ever unlock. Leave at least one column ungated.");
                return; // reachability below is moot
            }

            // 5d: reachability fixpoint. Optimistically complete every currently-unlocked column; that
            // raises the locked totals, which may unlock gated columns; repeat until stable.
            var unlocked = new bool[n];
            int unlockedCount = 0;
            for (int i = 0; i < n; i++)
                if (!isFrozen[i] && !isBreakWall[i] && !isLockColor[i]) { unlocked[i] = true; unlockedCount++; }

            bool changed = true;
            while (changed)
            {
                changed = false;
                int lockedTotal = unlockedCount; // all currently-unlocked columns are completable → lockable
                for (int i = 0; i < n; i++)
                {
                    if (unlocked[i]) continue;
                    bool canUnlock = false;
                    if (isFrozen[i])
                    {
                        // Frozen (count): breaks once X ANY columns can be completed.
                        canUnlock = lockedTotal >= frozenT[i];
                    }
                    else if (isBreakWall[i])
                    {
                        // Break Wall (neighbor): breaks once its EXISTING neighbors are reachable (so they
                        // can be completed). Edge column needs only its single neighbor. Two ADJACENT break
                        // walls each need the other → neither becomes reachable → flagged as a deadlock below.
                        bool leftOk  = (i == 0)     || unlocked[i - 1];
                        bool rightOk = (i == n - 1) || unlocked[i + 1];
                        canUnlock = leftOk && rightOk;
                    }
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
                result.errors.Add($"Freeze deadlock: column(s) [{string.Join(", ", stuck)}] can never unlock. " +
                                  $"A Break Wall needs its neighbor(s) completed first — two ADJACENT Break Walls each wait " +
                                  $"on the other (circular), or a Lock Color threshold is unreachable. Move a wall so it isn't " +
                                  $"adjacent to another, leave a neighbor ungated, or lower a Lock Color threshold.");
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

        [Tooltip("BREAK WALL STACK: tick to make THIS column a 'stone wall' that breaks when its ADJACENT " +
                 "columns are completed — both the LEFT and RIGHT neighbor (an edge column needs only its " +
                 "single neighbor). NEIGHBOR-based, no threshold. This is DIFFERENT from Frozen (count of X " +
                 "any columns, in LevelData.frozenColumns). MUTUALLY EXCLUSIVE with Lock Color Stack, Only " +
                 "Stack Sort, and Frozen on the same column (validation enforces).")]
        public bool breakWallStack = false;
    }

    /// <summary>
    /// FROZEN entry (COUNT-based): which column starts frozen and how many OTHER columns must be completed
    /// (any color) before it breaks. X = <see cref="unlockThreshold"/>, shown counting down on the
    /// FrozenOverlay. At runtime LevelLoader freezes <see cref="columnIndex"/> via <see cref="Column.Freeze"/>;
    /// GameManager breaks it once total locked columns ≥ X.
    ///
    /// Two SIBLING mechanics live elsewhere (do NOT confuse): NEIGHBOR-based "Break Wall Stack" (breaks when
    /// its left+right neighbors complete) on <see cref="ColumnConfig.breakWallStack"/>; and Lock Color Stack
    /// (count of a SPECIFIC color) on <see cref="ColumnConfig.lockColorStack"/>.
    /// </summary>
    [System.Serializable]
    public class FrozenColumnConfig
    {
        [Tooltip("0-based index of the column that starts FROZEN.")]
        public int columnIndex;

        [Tooltip("X = how many OTHER columns (any color) must be completed before this FROZEN column " +
                 "breaks. Shown on the FrozenOverlay and counts down as columns complete. (NEIGHBOR-based " +
                 "'Break Wall Stack' is a DIFFERENT mechanic — see ColumnConfig.breakWallStack.)")]
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
