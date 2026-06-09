using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Controls the in-game Settings popup. Opened by the world-space Settings button;
    /// contains buttons that resume the game, restart the current level, or return to main menu.
    /// Also hosts the Haptics on/off control (mobile vibration on tap) as a Button so it can carry
    /// custom on/off Sprites.
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        [Tooltip("The settings popup panel. Disabled at scene start, shown when Open() is called.")]
        [SerializeField] private GameObject panel;

        [Tooltip("If true, the game is paused (Time.timeScale = 0) while the panel is open. " +
                 "Sort is turn-based so this is optional but feels nice.")]
        [SerializeField] private bool pauseTimeWhileOpen = false;

        [Header("Haptics (mobile vibration)")]
        [Tooltip("Optional Button that toggles haptic feedback on/off. Drag your haptics Button here and " +
                 "it's wired automatically (no OnClick setup needed). Leave null if there's no haptics " +
                 "button yet — everything else still works.")]
        [SerializeField] private Button hapticsButton;

        [Tooltip("Optional Image whose sprite swaps to show the current haptics state (the Button's own " +
                 "Image, or a child icon). Leave null to skip the visual swap.")]
        [SerializeField] private Image hapticsIcon;

        [Tooltip("Sprite shown on Haptics Icon when haptics are ON.")]
        [SerializeField] private Sprite hapticsOnSprite;

        [Tooltip("Sprite shown on Haptics Icon when haptics are OFF.")]
        [SerializeField] private Sprite hapticsOffSprite;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Awake()
        {
            if (panel != null) panel.SetActive(false);

            if (hapticsButton != null) hapticsButton.onClick.AddListener(ToggleHaptics);
            RefreshHapticsVisual();
        }

        void OnDestroy()
        {
            if (hapticsButton != null) hapticsButton.onClick.RemoveListener(ToggleHaptics);
        }

        /// <summary>
        /// Flips haptics on/off and updates the icon. Wired automatically to <see cref="hapticsButton"/>;
        /// also public so a Button's OnClick can call it directly if you prefer manual wiring (don't do
        /// both — that would toggle twice per press).
        /// </summary>
        public void ToggleHaptics()
        {
            Haptics.Enabled = !Haptics.Enabled;
            RefreshHapticsVisual();
        }

        void RefreshHapticsVisual()
        {
            if (hapticsIcon == null) return;
            var sprite = Haptics.Enabled ? hapticsOnSprite : hapticsOffSprite;
            if (sprite != null) hapticsIcon.sprite = sprite;
        }

        public void Open()
        {
            if (panel == null) return;
            SfxManager.Play(SfxId.Button);
            panel.SetActive(true);
            RefreshHapticsVisual(); // reflect the saved state each time the panel opens
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
