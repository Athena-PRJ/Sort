using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Controls the in-game Settings popup. Opened by the world-space Settings button;
    /// contains buttons that resume the game, restart the current level, or return to main menu.
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        [Tooltip("The settings popup panel. Disabled at scene start, shown when Open() is called.")]
        [SerializeField] private GameObject panel;

        [Tooltip("If true, the game is paused (Time.timeScale = 0) while the panel is open. " +
                 "Sort is turn-based so this is optional but feels nice.")]
        [SerializeField] private bool pauseTimeWhileOpen = false;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Awake()
        {
            if (panel != null) panel.SetActive(false);
        }

        public void Open()
        {
            if (panel == null) return;
            panel.SetActive(true);
            if (pauseTimeWhileOpen) Time.timeScale = 0f;
        }

        public void Close()
        {
            if (panel == null) return;
            panel.SetActive(false);
            if (pauseTimeWhileOpen) Time.timeScale = 1f;
        }

        // Wired to UI buttons inside the panel.
        public void OnResumeClicked() => Close();

        public void OnRestartClicked()
        {
            Close();
            if (pauseTimeWhileOpen) Time.timeScale = 1f; // Ensure time is unfrozen before scene reload.
            GameManager.Instance?.Restart();
        }

        public void OnMainMenuClicked()
        {
            Close();
            if (pauseTimeWhileOpen) Time.timeScale = 1f;
            GameManager.Instance?.GoToMainMenu();
        }

        /// <summary>
        /// Quits the application (only works in builds — in the editor it stops Play mode).
        /// </summary>
        public void OnQuitClicked()
        {
            if (pauseTimeWhileOpen) Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
