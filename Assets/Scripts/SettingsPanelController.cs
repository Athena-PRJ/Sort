using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Controls the in-game Settings popup. Opened by the world-space Settings button;
    /// contains buttons that resume the game, restart the current level, or return to main menu.
    /// Also hosts the Haptics on/off toggle (mobile vibration on tap).
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        [Tooltip("The settings popup panel. Disabled at scene start, shown when Open() is called.")]
        [SerializeField] private GameObject panel;

        [Tooltip("If true, the game is paused (Time.timeScale = 0) while the panel is open. " +
                 "Sort is turn-based so this is optional but feels nice.")]
        [SerializeField] private bool pauseTimeWhileOpen = false;

        [Tooltip("Optional UI Toggle that turns mobile haptic feedback (vibration on tap) on/off. " +
                 "Leave null if the Settings panel has no haptics toggle yet — the rest still works. " +
                 "When wired, its state syncs with the saved Haptics.Enabled preference automatically.")]
        [SerializeField] private Toggle hapticsToggle;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Awake()
        {
            if (panel != null) panel.SetActive(false);

            if (hapticsToggle != null)
            {
                hapticsToggle.SetIsOnWithoutNotify(Haptics.Enabled);
                hapticsToggle.onValueChanged.AddListener(OnHapticsToggleChanged);
            }
        }

        void OnDestroy()
        {
            if (hapticsToggle != null) hapticsToggle.onValueChanged.RemoveListener(OnHapticsToggleChanged);
        }

        /// <summary>Wired to the haptics Toggle (and called automatically when its value changes).</summary>
        public void OnHapticsToggleChanged(bool on) => Haptics.Enabled = on;

        public void Open()
        {
            if (panel == null) return;
            panel.SetActive(true);
            // Reflect the saved preference each time the panel opens (e.g. changed elsewhere).
            if (hapticsToggle != null) hapticsToggle.SetIsOnWithoutNotify(Haptics.Enabled);
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
