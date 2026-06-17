using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// One node on the level map (a hexagon in the MainMenu). Groups the difficulty STAGES playable at
    /// this level number — e.g. Easy / Hard / Very Hard variants of "Level 3". The player unlocks nodes
    /// progressively (see <see cref="LevelProgress"/>) and picks one stage to play.
    /// </summary>
    [System.Serializable]
    public class LevelNode
    {
        [Tooltip("Map number (1-based) shown on the node AND used for unlock tracking. Should match the " +
                 "levelNumber on this node's stage LevelData(s).")]
        public int number = 1;

        [Tooltip("Difficulty stages playable at this node, in display order (e.g. Easy, Hard, Very Hard). " +
                 "Each is a LevelData; its own 'difficulty' tag drives the badge. The player picks one.")]
        public List<LevelData> stages = new List<LevelData>();

        public bool HasStages => stages != null && stages.Count > 0;
        public int StageCount => stages != null ? stages.Count : 0;

        /// <summary>The stage LevelData at <paramref name="index"/>, or null if out of range.</summary>
        public LevelData GetStage(int index) =>
            (stages != null && index >= 0 && index < stages.Count) ? stages[index] : null;

        /// <summary>First stage matching a difficulty tag, or null. Handy when stages are keyed by difficulty.</summary>
        public LevelData GetStage(LevelDifficulty difficulty)
        {
            if (stages == null) return null;
            for (int i = 0; i < stages.Count; i++)
                if (stages[i] != null && stages[i].difficulty == difficulty) return stages[i];
            return null;
        }
    }

    /// <summary>
    /// Ordered list of level NODES — the single source of truth for the MainMenu level map, progression
    /// tracking, and skill-unlock milestones. Independent of the in-game LevelLoader chain (LevelData.nextLevel),
    /// so it works in the MainMenu scene where no LevelLoader exists.
    ///
    /// Setup: Assets → Create → Sort → Level Database; put the asset under <b>Assets/Resources/</b> named
    /// exactly <b>"LevelDatabase"</b> so it auto-loads via <see cref="Instance"/>. Fill <see cref="nodes"/>
    /// in play order and assign each node's difficulty stages.
    /// </summary>
    [CreateAssetMenu(menuName = "Sort/Level Database", fileName = "LevelDatabase")]
    public class LevelDatabase : ScriptableObject
    {
        [Tooltip("All level nodes in play order. Node [0] is the first level the player sees.")]
        public List<LevelNode> nodes = new List<LevelNode>();

        static LevelDatabase _instance;
        /// <summary>Auto-loaded from Resources/LevelDatabase (same pattern as EconomyConfig). Null if missing.</summary>
        public static LevelDatabase Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<LevelDatabase>("LevelDatabase");
                return _instance;
            }
        }

        public int NodeCount => nodes != null ? nodes.Count : 0;

        public LevelNode GetNode(int index) =>
            (nodes != null && index >= 0 && index < nodes.Count) ? nodes[index] : null;

        /// <summary>Find the node with this map number, or null.</summary>
        public LevelNode GetNodeByNumber(int number)
        {
            if (nodes == null) return null;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null && nodes[i].number == number) return nodes[i];
            return null;
        }

        /// <summary>True if this node is unlocked for the player (per LevelProgress.HighestUnlocked).</summary>
        public static bool IsNodeUnlocked(LevelNode node) =>
            node != null && LevelProgress.IsUnlocked(node.number);

        /// <summary>
        /// Lowest node number whose ANY stage flags this skill's unlock-on-completion, or 0 if none.
        /// Used by the UI to label a locked skill ("unlocks at Lv N") — works in the MainMenu because it
        /// reads the database, not the runtime LevelLoader chain.
        /// </summary>
        public int GetSkillUnlockNumber(SkillType skill)
        {
            if (skill == SkillType.Rewind || nodes == null) return 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null || node.stages == null) continue;
                for (int s = 0; s < node.stages.Count; s++)
                {
                    var lvl = node.stages[s];
                    if (lvl == null) continue;
                    if (skill == SkillType.Switch && lvl.unlocksSwitchOnCompletion) return node.number;
                    if (skill == SkillType.Magnet && lvl.unlocksMagnetOnCompletion) return node.number;
                }
            }
            return 0;
        }
    }
}
