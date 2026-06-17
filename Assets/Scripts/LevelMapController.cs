using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Sort
{
    /// <summary>
    /// Builds the MainMenu level map from <see cref="LevelDatabase"/>: spawns one <see cref="LevelNodeView"/>
    /// per node, locked/unlocked per <see cref="LevelProgress"/>. Tapping a difficulty stage selects it
    /// (<see cref="LevelProgress.SelectedLevel"/>) and loads the gameplay scene.
    ///
    /// Logic is ready now — assign the node prefab + container + scene name when the MainMenu art exists.
    /// </summary>
    public class LevelMapController : MonoBehaviour
    {
        [Tooltip("Source of truth. If null, uses LevelDatabase.Instance (Resources/LevelDatabase).")]
        [SerializeField] private LevelDatabase database;
        [Tooltip("Node prefab (has a LevelNodeView). One is spawned per level node.")]
        [SerializeField] private LevelNodeView nodeViewPrefab;
        [Tooltip("Parent the node views spawn under (e.g. a vertical layout along the map's center bar).")]
        [SerializeField] private Transform nodeContainer;
        [Tooltip("Scene to load when a stage is chosen (the gameplay scene).")]
        [SerializeField] private string gameSceneName = "SampleScene";

        [Header("Lives gate")]
        [Tooltip("Require at least 1 life to START a level (life is only DEDUCTED on Failed). If 0 lives, " +
                 "fires On Out Of Lives instead of loading — hook a 'no lives / buy lives' popup there.")]
        [SerializeField] private bool requireLifeToPlay = true;
        [SerializeField] private UnityEvent onOutOfLives;

        void Start() => Build();

        /// <summary>Clears and rebuilds the map from the database + current unlock progress.</summary>
        public void Build()
        {
            var db = database != null ? database : LevelDatabase.Instance;
            if (db == null)
            {
                Debug.LogWarning("[LevelMapController] No LevelDatabase (assign one or put 'LevelDatabase' in Resources/).", this);
                return;
            }
            if (nodeViewPrefab == null || nodeContainer == null)
            {
                Debug.LogWarning("[LevelMapController] Assign Node View Prefab + Node Container.", this);
                return;
            }

            for (int i = nodeContainer.childCount - 1; i >= 0; i--)
                Destroy(nodeContainer.GetChild(i).gameObject);

            for (int i = 0; i < db.NodeCount; i++)
            {
                var node = db.GetNode(i);
                if (node == null) continue;
                var view = Instantiate(nodeViewPrefab, nodeContainer);
                view.Bind(node, LevelDatabase.IsNodeUnlocked(node), PlayStage);
            }
        }

        /// <summary>Selects a difficulty stage and loads the gameplay scene (gated by lives if enabled).</summary>
        public void PlayStage(LevelData stage)
        {
            if (stage == null) return;
            if (requireLifeToPlay && !PlayerEconomy.HasLives)
            {
                onOutOfLives?.Invoke();   // out of lives → show a buy-lives / wait popup
                return;
            }
            LevelProgress.SelectedLevel = stage;
            SceneManager.LoadScene(gameSceneName);
        }
    }
}
