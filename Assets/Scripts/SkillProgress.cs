using UnityEngine;

namespace Sort
{
    public enum SkillType
    {
        Rewind,
        Switch,
        Magnet
    }

    /// <summary>
    /// Skill unlock gating. A skill becomes available once the player has REACHED its unlock LEVEL — the
    /// three thresholds live in ONE place: <see cref="LevelDatabase"/> (rewind/switch/magnetUnlockLevel).
    /// There are NO per-LevelData flags and NO separate persisted unlock state — it's derived purely from
    /// level progress (<see cref="LevelProgress.HighestUnlocked"/>, plus the level currently being played
    /// so an editor Level-Jump straight to a high level still unlocks that level's skills).
    /// </summary>
    public static class SkillProgress
    {
        // Fallback thresholds used ONLY if the LevelDatabase asset is missing. The real source of truth is
        // the LevelDatabase (Resources) so designers tune the numbers once in the Inspector.
        const int DEFAULT_REWIND = 7, DEFAULT_SWITCH = 12, DEFAULT_MAGNET = 15;

        /// <summary>Tester override: when true, every skill reads as unlocked regardless of level reached
        /// (set by SkillManager's dev flag / dev context-menu). Not persisted.</summary>
        public static bool DevUnlockAll;

        /// <summary>True if the player has reached the skill's unlock level (or the tester override is on).</summary>
        public static bool IsUnlocked(SkillType skill)
        {
            if (DevUnlockAll) return true;
            int lvl = GetUnlockLevel(skill);
            return lvl <= 1 || ReachedLevel() >= lvl;
        }

        /// <summary>The level number at which <paramref name="skill"/> unlocks — from LevelDatabase
        /// (single source of truth); falls back to the code defaults if the asset is missing.</summary>
        public static int GetUnlockLevel(SkillType skill)
        {
            var db = LevelDatabase.Instance;
            if (db != null)
            {
                int n = db.GetSkillUnlockNumber(skill);
                if (n > 0) return n;
            }
            switch (skill)
            {
                case SkillType.Rewind: return DEFAULT_REWIND;
                case SkillType.Switch: return DEFAULT_SWITCH;
                case SkillType.Magnet: return DEFAULT_MAGNET;
                default: return 0;
            }
        }

        /// <summary>Dev/testing reset — clears the tester override so unlock reverts to level-derived.</summary>
        public static void ResetAll() { DevUnlockAll = false; }

        // Highest level the player has reached = max(persisted progress, the level currently loaded). The
        // current-level term makes editor Level-Jump (which doesn't bump HighestUnlocked) unlock correctly.
        static int ReachedLevel()
        {
            int reached = LevelProgress.HighestUnlocked;
            var loader = LevelLoader.Instance;
            if (loader != null && loader.CurrentLevel != null)
                reached = Mathf.Max(reached, loader.CurrentLevel.levelNumber);
            return reached;
        }
    }
}
