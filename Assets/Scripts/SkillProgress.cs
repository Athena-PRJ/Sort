using System.Collections.Generic;
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
    /// Tells whether each skill is unlocked. Rewind is always unlocked.
    /// Switch and Magnet unlock when the player completes a LevelData asset that's been flagged
    /// (unlocksSwitchOnCompletion / unlocksMagnetOnCompletion). Unlock state persists in PlayerPrefs.
    /// </summary>
    public static class SkillProgress
    {
        const string KEY_PREFIX = "Sort_Skill_";

        // Cached lowest level-number that flags each skill. Built lazily from LevelLoader.DefaultLevel's chain.
        // Cleared via ClearCache() when the level layout changes (designer flags a different level).
        static Dictionary<SkillType, int> _unlockLevelCache;

        public static bool IsUnlocked(SkillType skill)
        {
            if (skill == SkillType.Rewind) return true;
            return PlayerPrefs.GetInt(KEY_PREFIX + skill, 0) == 1;
        }

        /// <summary>
        /// Persistently unlocks <paramref name="skill"/>. Called by GameManager when the player
        /// completes a LevelData with the matching unlock flag. No-op for Rewind (always unlocked).
        /// </summary>
        public static void Unlock(SkillType skill)
        {
            if (skill == SkillType.Rewind) return;
            PlayerPrefs.SetInt(KEY_PREFIX + skill, 1);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Re-locks <paramref name="skill"/>. Mostly for dev/testing — production gameplay never
        /// re-locks a skill once unlocked.
        /// </summary>
        public static void Lock(SkillType skill)
        {
            if (skill == SkillType.Rewind) return;
            PlayerPrefs.DeleteKey(KEY_PREFIX + skill);
            PlayerPrefs.Save();
        }

        public static void ResetAll()
        {
            Lock(SkillType.Switch);
            Lock(SkillType.Magnet);
            ClearCache();
        }

        /// <summary>
        /// Returns the lowest-numbered LevelData (in the chain from LevelLoader.DefaultLevel via
        /// LevelData.nextLevel) that has the unlock flag set for <paramref name="skill"/>.
        /// Used by the UI to display "Lvl 5" / "Lvl 10" on the locked skill icon.
        /// Returns 0 if no flagged level is found or LevelLoader hasn't initialized yet.
        /// </summary>
        public static int GetUnlockLevel(SkillType skill)
        {
            if (skill == SkillType.Rewind) return 0;

            // Prefer the LevelDatabase — it works in the MainMenu scene (no LevelLoader there) and is the
            // single source of truth for milestones. Fall back to the in-game LevelLoader.nextLevel chain.
            var db = LevelDatabase.Instance;
            if (db != null)
            {
                int n = db.GetSkillUnlockNumber(skill);
                if (n > 0) return n;
            }

            if (_unlockLevelCache == null) BuildUnlockLevelCache();
            return _unlockLevelCache != null && _unlockLevelCache.TryGetValue(skill, out var c) ? c : 0;
        }

        /// <summary>
        /// Force a rebuild of the level-chain cache. Call this if the designer adds/removes unlock
        /// flags from LevelData assets and wants the UI label to refresh without reloading.
        /// </summary>
        public static void ClearCache() { _unlockLevelCache = null; }

        static void BuildUnlockLevelCache()
        {
            _unlockLevelCache = new Dictionary<SkillType, int>();
            var loader = LevelLoader.Instance;
            if (loader == null) return;

            var visited = new HashSet<LevelData>();
            var head = loader.DefaultLevel;
            while (head != null && visited.Add(head))
            {
                if (head.unlocksSwitchOnCompletion && !_unlockLevelCache.ContainsKey(SkillType.Switch))
                    _unlockLevelCache[SkillType.Switch] = head.levelNumber;
                if (head.unlocksMagnetOnCompletion && !_unlockLevelCache.ContainsKey(SkillType.Magnet))
                    _unlockLevelCache[SkillType.Magnet] = head.levelNumber;
                head = head.nextLevel;
            }
        }
    }
}
