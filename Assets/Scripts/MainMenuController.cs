using UnityEngine;

namespace Sort
{
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject levelSelectPanel;

        void Awake()
        {
            // Make sure SelectedLevel is cleared when returning to main menu,
            // so a stale reference doesn't auto-load on the next play.
            LevelProgress.SelectedLevel = null;
            ShowMain();
        }

        public void OnPlayClicked() => ShowLevelSelect();
        public void OnBackToMainClicked() => ShowMain();

        public void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void ShowMain()
        {
            if (mainPanel != null) mainPanel.SetActive(true);
            if (levelSelectPanel != null) levelSelectPanel.SetActive(false);
        }

        void ShowLevelSelect()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (levelSelectPanel != null) levelSelectPanel.SetActive(true);
        }
    }
}
