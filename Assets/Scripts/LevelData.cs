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
                 "add entries there to expand the available options. Each entry also carries its own " +
                 "spacing / offset config that LevelLoader applies when this prefab is used.")]
        public GameObject piecePrefab;

        [Tooltip("Frame sprite used for this level's board background. Assign one of Assets/Texture/FullBoard/ (MB_1, MB_2, MB_3, etc.) " +
                 "to give each level its own board color. If left null, LevelLoader uses its defaultMainBoardSprite fallback.")]
        public Sprite mainBoardSprite;

        [Tooltip("Background image displayed behind the gameplay for this level. Assign one of Assets/Texture/Background/ " +
                 "(BG_1, BG_2, BG_3, etc.). If left null, LevelLoader uses its defaultBackgroundSprite fallback.")]
        public Sprite backgroundSprite;

        [Tooltip("Maximum moves the player has before losing. 0 = unlimited.")]
        public int moveLimit = 20;

        [Tooltip("The piece the player starts holding. Can also be a Rainbow.")]
        public PieceConfig startingHeldPiece = new PieceConfig { color = PieceColor.Red };

        [Tooltip("One entry per column. The first PieceConfig is the top piece.")]
        public ColumnConfig[] columns = new ColumnConfig[0];

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
            var colorCounts = new Dictionary<PieceColor, int>();
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

            return result;
        }

        public class ValidationResult
        {
            public List<string> errors = new();
            public Dictionary<PieceColor, int> colorCounts;
            public int columnCount;
            public int rowCount;
            public int rainbowCount;
            public bool IsValid => errors.Count == 0;
        }
    }

    [System.Serializable]
    public class ColumnConfig
    {
        [Tooltip("Pieces from top to bottom.")]
        public PieceConfig[] pieces = new PieceConfig[0];
    }

    [System.Serializable]
    public class PieceConfig
    {
        public PieceColor color = PieceColor.Red;

        [Tooltip("Wildcard — counts as no specific color, auto-sinks to bottom when column is otherwise sorted.")]
        public bool isRainbow;

        [Tooltip("Hidden — shows as '?' until pushed near the bottom of its column, then reveals its true color.")]
        public bool isQuestionmark;
    }
}
