using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Persists per-player level unlock state via PlayerPrefs, and carries
    /// the selected LevelData across scene loads via a static field.
    /// </summary>
    public static class LevelProgress
    {
        const string UNLOCK_KEY = "Sort_HighestUnlocked";
        const int FIRST_LEVEL = 1;

        /// <summary>
        /// LevelSelect sets this before loading the Game scene. LevelLoader reads it on Awake.
        /// Survives scene transitions because it's a static field.
        /// </summary>
        public static LevelData SelectedLevel { get; set; }

        public static int HighestUnlocked
        {
            get => Mathf.Max(FIRST_LEVEL, PlayerPrefs.GetInt(UNLOCK_KEY, FIRST_LEVEL));
        }

        public static bool IsUnlocked(int levelNumber) => levelNumber <= HighestUnlocked;

        public static void MarkCompleted(int levelNumber)
        {
            int newHighest = Mathf.Max(HighestUnlocked, levelNumber + 1);
            PlayerPrefs.SetInt(UNLOCK_KEY, newHighest);
            PlayerPrefs.Save();
        }

        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey(UNLOCK_KEY);
            PlayerPrefs.Save();
        }
    }
}
