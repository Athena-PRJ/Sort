using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Sort
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Board")]
        [Tooltip("Parent containing all Column children. If null, finds Columns anywhere in the scene.")]
        [SerializeField] private Transform board;

        [Header("Move limit")]
        [Tooltip("Move limit used when no LevelData is loaded. LevelLoader overrides this per level.")]
        [SerializeField] private int moveLimit = 20;
        [SerializeField] private TMP_Text movesText;

        [Header("Scenes")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        [Header("End-game UI")]
        [SerializeField] private GameObject winPanel;
        [SerializeField] private GameObject losePanel;

        [Header("Events")]
        [SerializeField] private UnityEvent onWin;
        [SerializeField] private UnityEvent onLose;

        readonly List<Column> columns = new();
        int lockedCount;
        int movesUsed;

        public bool IsWon { get; private set; }
        public bool IsLost { get; private set; }
        public bool IsGameOver => IsWon || IsLost;
        public int MovesRemaining => Mathf.Max(0, moveLimit - movesUsed);
        public IReadOnlyList<Column> Columns => columns;

        /// <summary>Fired when the game ends (win or lose) — lets UI refresh without polling.</summary>
        public event System.Action StateChanged;

        void Awake()
        {
            Instance = this;

            // If a level is loaded, take its move limit instead of the inspector default.
            var loader = LevelLoader.Instance;
            if (loader != null && loader.CurrentLevel != null) moveLimit = loader.CurrentLevel.moveLimit;

            columns.Clear();
            var seen = new HashSet<Column>();
            var found = new List<Column>();
            if (board != null)
                board.GetComponentsInChildren(true, found);
            else
                found.AddRange(FindObjectsByType<Column>(FindObjectsInactive.Include));

            foreach (var c in found)
            {
                if (c == null) continue;
                // Skip columns that live in DontDestroyOnLoad or in a different scene.
                if (c.gameObject.scene != gameObject.scene) continue;
                if (!seen.Add(c)) continue;
                columns.Add(c);
            }

            foreach (var c in columns) c.Locked += OnColumnLocked;

            if (winPanel != null) winPanel.SetActive(false);
            if (losePanel != null) losePanel.SetActive(false);
            UpdateMovesUI();
        }

        void Start()
        {
            // Catch columns that locked themselves during their own Start (before we subscribed).
            lockedCount = 0;
            foreach (var c in columns) if (c.IsLocked) lockedCount++;
            CheckWin();
        }

        void OnDestroy()
        {
            foreach (var c in columns) if (c != null) c.Locked -= OnColumnLocked;
        }

        public void NotifyMoveMade()
        {
            if (IsGameOver) return;
            movesUsed++;
            UpdateMovesUI();
            if (!IsWon && moveLimit > 0 && movesUsed >= moveLimit)
                Lose();
        }

        /// <summary>
        /// Called by skills that undo a move (e.g. Rewind). Restores the move counter and
        /// recomputes lock progress in case the undone move had locked a column.
        /// </summary>
        public void RefundMove()
        {
            if (IsGameOver) return;
            movesUsed = Mathf.Max(0, movesUsed - 1);
            UpdateMovesUI();

            // Re-tally locked columns — the undone move may have unlocked one.
            lockedCount = 0;
            foreach (var c in columns) if (c != null && c.IsLocked) lockedCount++;
        }

        public void Restart()
        {
            // Reloads the current scene with the current LevelProgress.SelectedLevel still set,
            // so LevelLoader rebuilds the same level fresh.
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// Wired to the WinPanel's "Next Level" button. Advances to the LevelData referenced by
        /// CurrentLevel.nextLevel. If there is no next level (final level or unwired), returns to main menu.
        /// </summary>
        public void NextLevel()
        {
            var loader = LevelLoader.Instance;
            var next = loader != null && loader.CurrentLevel != null ? loader.CurrentLevel.nextLevel : null;
            if (next == null) { GoToMainMenu(); return; }

            LevelProgress.SelectedLevel = next;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void GoToMainMenu()
        {
            LevelProgress.SelectedLevel = null;
            SceneManager.LoadScene(mainMenuSceneName);
        }

        void OnColumnLocked(Column c)
        {
            lockedCount++;
            LogProgress();
            CheckWin();
        }

        void LogProgress()
        {
            var unsorted = new System.Text.StringBuilder();
            foreach (var c in columns)
                if (c != null && !c.IsLocked) unsorted.Append(c.name).Append(' ');
            Debug.Log($"[GameManager] {lockedCount}/{columns.Count} columns locked. Still unsorted: [{unsorted.ToString().TrimEnd()}]");
        }

        void CheckWin()
        {
            if (IsWon || IsLost) return;
            if (columns.Count > 0 && lockedCount >= columns.Count)
            {
                IsWon = true;
                if (winPanel != null) winPanel.SetActive(true);

                var loader = LevelLoader.Instance;
                if (loader != null && loader.CurrentLevel != null)
                {
                    LevelProgress.MarkCompleted(loader.CurrentLevel.levelNumber);
                    PlayerEconomy.AddCoins(loader.CurrentLevel.coinReward);

                    // Persistent skill unlocks driven by per-LevelData flags. The skill stays
                    // unlocked across runs and across other levels.
                    if (loader.CurrentLevel.unlocksSwitchOnCompletion) SkillProgress.Unlock(SkillType.Switch);
                    if (loader.CurrentLevel.unlocksMagnetOnCompletion) SkillProgress.Unlock(SkillType.Magnet);
                }

                Debug.Log("Sort: player wins!");
                onWin?.Invoke();
                StateChanged?.Invoke();
            }
        }

        void Lose()
        {
            if (IsLost || IsWon) return;
            IsLost = true;
            PlayerEconomy.DeductLife();
            if (losePanel != null) losePanel.SetActive(true);
            Debug.Log("Sort: out of moves — player loses.");
            onLose?.Invoke();
            StateChanged?.Invoke();
        }

        void UpdateMovesUI()
        {
            if (movesText == null) return;
            // Move.png already contains the "Moves" wording — show just the number.
            movesText.text = moveLimit > 0 ? MovesRemaining.ToString() : "∞";
        }
    }
}
