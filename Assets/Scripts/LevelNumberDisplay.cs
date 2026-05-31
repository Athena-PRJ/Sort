using TMPro;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Writes the current level's number into a TMP text. Reads from LevelLoader on Start.
    /// Attach to a UI element that shows the level badge (e.g. an Image with Level.png as the
    /// background and a TMP text on top).
    /// </summary>
    public class LevelNumberDisplay : MonoBehaviour
    {
        [Tooltip("TMP text that will be set to the current level number. If null, auto-finds one in children.")]
        [SerializeField] private TMP_Text levelText;
        [Tooltip("Format string. {0} = difficulty (Easy / SuperHard / etc.), {1} = level number.\n" +
                 "Examples:  '{0}: {1}',  '{0} #{1}',  '{0}\\n{1}',  or just '{1}' to hide difficulty.")]
        [SerializeField] private string format = "{0}: {1}";

        void Start()
        {
            if (levelText == null) levelText = GetComponentInChildren<TMP_Text>();
            if (levelText == null) return;

            var loader = LevelLoader.Instance;
            int n = loader != null && loader.CurrentLevel != null ? loader.CurrentLevel.levelNumber : 1;
            var difficulty = loader != null && loader.CurrentLevel != null
                ? loader.CurrentLevel.difficulty
                : LevelDifficulty.Easy;

            levelText.text = string.Format(format, difficulty, n);
        }
    }
}
