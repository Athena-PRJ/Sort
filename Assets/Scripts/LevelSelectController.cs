using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sort
{
    public class LevelSelectController : MonoBehaviour
    {
        [Header("Level list (drag your Level1..Level5 ScriptableObjects in order)")]
        [SerializeField] private LevelData[] levels;

        [Header("Level buttons (drag one button entry per level)")]
        [SerializeField] private LevelButtonView[] buttons;

        [Header("Scene")]
        [SerializeField] private string gameSceneName = "SampleScene";

        [Header("Out of lives (optional)")]
        [Tooltip("Shown if the player clicks a level while having 0 lives. Optional — if null, the click is just ignored.")]
        [SerializeField] private GameObject outOfLivesPanel;

        void OnEnable() => Refresh();

        void Refresh()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i >= levels.Length || levels[i] == null) { buttons[i].Hide(); continue; }
                var level = levels[i];
                bool unlocked = LevelProgress.IsUnlocked(level.levelNumber);
                buttons[i].Setup(level, unlocked, OnLevelClicked);
            }
        }

        void OnLevelClicked(LevelData level)
        {
            if (!PlayerEconomy.HasLives)
            {
                if (outOfLivesPanel != null) outOfLivesPanel.SetActive(true);
                return;
            }
            LevelProgress.SelectedLevel = level;
            SceneManager.LoadScene(gameSceneName);
        }

        [System.Serializable]
        public class LevelButtonView
        {
            public Button button;
            public TMP_Text label;
            public GameObject lockIcon;

            public void Setup(LevelData level, bool unlocked, System.Action<LevelData> onClick)
            {
                if (button != null) button.gameObject.SetActive(true);
                // Label text is left untouched — set whatever you want in the editor and it stays.
                if (lockIcon != null) lockIcon.SetActive(!unlocked);
                if (button != null)
                {
                    button.interactable = unlocked;
                    button.onClick.RemoveAllListeners();
                    if (unlocked) button.onClick.AddListener(() => onClick(level));
                }
            }

            public void Hide()
            {
                if (button != null) button.gameObject.SetActive(false);
            }
        }
    }
}
