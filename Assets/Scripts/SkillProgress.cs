using UnityEngine;

namespace Sort
{
    public enum SkillType
    {
        Rewind,
        Skill2,
        Skill3
    }

    /// <summary>
    /// Persists which skills the player has unlocked. Rewind is always unlocked;
    /// others stay locked until Unlock(skill) is called.
    /// </summary>
    public static class SkillProgress
    {
        const string KEY_PREFIX = "Sort_Skill_";

        public static bool IsUnlocked(SkillType skill)
        {
            if (skill == SkillType.Rewind) return true;
            return PlayerPrefs.GetInt(KEY_PREFIX + skill, 0) == 1;
        }

        public static void Unlock(SkillType skill)
        {
            if (skill == SkillType.Rewind) return;
            PlayerPrefs.SetInt(KEY_PREFIX + skill, 1);
            PlayerPrefs.Save();
        }

        public static void Lock(SkillType skill)
        {
            if (skill == SkillType.Rewind) return; // can't relock Rewind
            PlayerPrefs.DeleteKey(KEY_PREFIX + skill);
            PlayerPrefs.Save();
        }

        public static void ResetAll()
        {
            Lock(SkillType.Skill2);
            Lock(SkillType.Skill3);
        }
    }
}
