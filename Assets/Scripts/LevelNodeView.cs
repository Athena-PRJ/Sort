using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// One node (hexagon) on the MainMenu level map. Shows the level number, locked/unlocked state, and a
    /// button per difficulty STAGE (Easy / Hard / Very Hard). Spawned and bound by <see cref="LevelMapController"/>.
    /// Put this on your node prefab and wire the display refs; the difficulty buttons are spawned at runtime.
    /// </summary>
    public class LevelNodeView : MonoBehaviour
    {
        [Header("Display")]
        [Tooltip("Shows the node's level number.")]
        [SerializeField] private TMP_Text numberText;
        [Tooltip("Root shown when the node is LOCKED (e.g. a padlock + dimmed hexagon).")]
        [SerializeField] private GameObject lockedRoot;
        [Tooltip("Root shown when the node is UNLOCKED (the playable hexagon + difficulty buttons).")]
        [SerializeField] private GameObject unlockedRoot;

        [Header("Difficulty stage buttons")]
        [Tooltip("Parent the per-difficulty stage buttons spawn under (only when unlocked).")]
        [SerializeField] private Transform stageContainer;
        [Tooltip("Prefab for one difficulty button — needs a Button; an optional TMP_Text child gets the " +
                 "difficulty name. One is spawned per stage in the node.")]
        [SerializeField] private GameObject stageButtonPrefab;

        /// <summary>Configures this node for display. <paramref name="onPlayStage"/> is invoked with the
        /// chosen difficulty's LevelData when a stage button is tapped.</summary>
        public void Bind(LevelNode node, bool unlocked, System.Action<LevelData> onPlayStage)
        {
            if (numberText != null) numberText.text = node != null ? node.number.ToString() : "";
            if (lockedRoot != null) lockedRoot.SetActive(!unlocked);
            if (unlockedRoot != null) unlockedRoot.SetActive(unlocked);

            if (stageContainer == null || stageButtonPrefab == null || node == null) return;

            // Clear any previously-spawned buttons, then spawn one per stage (only when unlocked/playable).
            for (int i = stageContainer.childCount - 1; i >= 0; i--)
                Destroy(stageContainer.GetChild(i).gameObject);
            if (!unlocked) return;

            for (int i = 0; i < node.StageCount; i++)
            {
                var stage = node.GetStage(i);
                if (stage == null) continue;

                var go = Instantiate(stageButtonPrefab, stageContainer);
                var label = go.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = stage.difficulty.ToString();

                var btn = go.GetComponent<Button>();
                if (btn != null)
                {
                    var captured = stage;   // capture per-iteration for the closure
                    btn.onClick.AddListener(() => onPlayStage?.Invoke(captured));
                }
            }
        }
    }
}
