using System.Collections;
using System.Collections.Generic;
using Sort.Monetization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        [Tooltip("The 'You Failed!' panel. Shown after the player runs out of moves AND declines the " +
                 "Out-of-Moves continue. Deducts 1 life; its Retry button calls Restart().")]
        [SerializeField] private GameObject losePanel;

        [Header("Win bonus ad (the '+N / x2' button on the Win panel)")]
        [Tooltip("Extra coins granted when the player watches the rewarded ad on the Win panel. " +
                 "Wire that button's OnClick to ClaimWinAdBonus().")]
        [SerializeField] private int winAdBonusCoins = 60;
        [Tooltip("Optional: the 'watch ad for bonus' Button. It is DIMMED (interactable=false → its " +
                 "Disabled Color) while the ad plays so it can't be tapped twice. Set a dark Disabled Color " +
                 "on the Button for the greyed-out look.")]
        [SerializeField] private Button winAdButton;
        bool winAdClaimed;

        [Header("Out of Moves (continue offer)")]
        [Tooltip("Shown FIRST when the player runs out of moves — offers a paid/ad continue before failing. " +
                 "If null, running out of moves goes straight to the You Failed (lose) panel.")]
        [SerializeField] private GameObject outOfMovesPanel;
        [Tooltip("Coin cost of the 'Play on' (coins) continue button.")]
        [SerializeField] private int continueCoinCost = 900;
        [Tooltip("How many extra moves a continue (coins or ad) grants.")]
        [SerializeField] private int continueMovesBonus = 10;
        [Tooltip("Fired when the player taps 'Play on (Ads)'. Hook your rewarded-ad SDK here; on ad SUCCESS " +
                 "call GrantContinue(). (ContinueWithAd() currently grants immediately as a placeholder — " +
                 "replace that with a real ad gate when the SDK is integrated.)")]
        [SerializeField] private UnityEvent onRequestRewardedAd;

        [Header("Events")]
        [SerializeField] private UnityEvent onWin;
        [SerializeField] private UnityEvent onLose;

        readonly List<Column> columns = new();
        int lockedCount;
        int movesUsed;

        public bool IsWon { get; private set; }
        public bool IsLost { get; private set; }
        // True while the Out-of-Moves panel is up awaiting the player's decision. Folded into IsGameOver
        // so it blocks board input (PlayerHand) + skills (SkillManager) without extra wiring; cleared on
        // continue (resume) or decline (→ Lose).
        bool awaitingContinue;
        public bool IsGameOver => IsWon || IsLost || awaitingContinue;
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
            if (outOfMovesPanel != null) outOfMovesPanel.SetActive(false);
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
                HandleOutOfMoves();
        }

        // ---------------------------------------------------------------------
        //  Out of Moves → continue (coins / ad) or fail. The board behind is blocked because awaitingContinue
        //  folds into IsGameOver (PlayerHand + SkillManager already gate on it). The Out-of-Moves panel should
        //  also have a full-screen raycast-blocking backdrop so UI behind it can't be tapped.
        // ---------------------------------------------------------------------

        /// <summary>Ran out of moves. Show the continue offer; if no panel is wired, fail immediately.</summary>
        void HandleOutOfMoves()
        {
            if (IsGameOver) return;
            if (outOfMovesPanel == null) { Lose(); return; }   // fallback: straight to You Failed
            awaitingContinue = true;                            // blocks input + skills (via IsGameOver)
            outOfMovesPanel.SetActive(true);
            StateChanged?.Invoke();                             // refresh skill buttons to disabled
        }

        /// <summary>'Play on (coins)' — spend the cost, then continue. No-op if not enough coins.</summary>
        public void ContinueWithCoins()
        {
            if (!awaitingContinue) return;
            if (!PlayerEconomy.TrySpendCoins(continueCoinCost)) return; // not enough → button does nothing
            GrantContinue();
        }

        /// <summary>
        /// 'Play on (Ads)' — show a rewarded ad and grant the continue ONLY if the player earns the reward.
        /// Routed through <see cref="AdsService"/> when one is present (it uses the Mock provider until a
        /// real SDK is compiled, so this works in the Editor). The <see cref="onRequestRewardedAd"/> event
        /// still fires for any extra hooks. Falls back to an immediate grant only if no AdsService exists.
        /// </summary>
        public void ContinueWithAd()
        {
            if (!awaitingContinue) return;
            onRequestRewardedAd?.Invoke();

            if (AdsService.Instance != null)
                AdsService.Instance.ShowContinueAd(earned => { if (earned) GrantContinue(); });
            else
                GrantContinue();   // no AdsService in scene → placeholder immediate grant
        }

        /// <summary>Adds the bonus moves, hides the panel, and resumes play (NO life lost, NO You Failed).</summary>
        public void GrantContinue()
        {
            if (!awaitingContinue) return;
            awaitingContinue = false;
            moveLimit += continueMovesBonus;            // +N moves → MovesRemaining becomes N
            if (outOfMovesPanel != null) outOfMovesPanel.SetActive(false);
            UpdateMovesUI();
            StateChanged?.Invoke();                     // re-enable skills
        }

        /// <summary>'X' / decline — close the offer and FAIL (deduct a life, show You Failed).</summary>
        public void DeclineContinue()
        {
            if (!awaitingContinue) return;
            awaitingContinue = false;
            if (outOfMovesPanel != null) outOfMovesPanel.SetActive(false);
            Lose();   // You Failed: deducts 1 life + shows losePanel
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

            // Re-evaluate frozen gates against the new (lower) progress: refresh overlay counts AND
            // re-freeze any column whose unlock progress just dropped below its threshold. Without this,
            // a Rewind that undoes a contributing lock would leave an already-unfrozen column permanently
            // open with a stale overlay. UpdateFrozenColumns is two-directional, so it both re-freezes
            // and unfreezes as the recomputed progress dictates.
            UpdateFrozenColumns();
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
        /// Shows an interstitial first (when one is due and the player hasn't bought Remove Ads) via
        /// AdsService.MaybeShowInterstitial, then navigates — navigation runs regardless of whether an ad showed.
        /// </summary>
        public void NextLevel()
        {
            // Continue button: maybe show an interstitial first, then advance.
            if (AdsService.Instance != null) AdsService.Instance.MaybeShowInterstitial(GoToNextLevel);
            else GoToNextLevel();
        }

        /// <summary>Advances to the next level (or main menu if none) WITHOUT showing an interstitial.
        /// Used directly after the Win bonus rewarded ad so the player isn't shown two ads back-to-back.</summary>
        void GoToNextLevel()
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

        /// <summary>
        /// Win panel "watch ad for bonus coins" button ('+N / x2'). Dims the button (so it can't be tapped
        /// again), shows a rewarded ad, and ON REWARD EARNED adds <see cref="winAdBonusCoins"/> coins then
        /// AUTO-ADVANCES to the next level (no separate Continue tap, and no extra interstitial). If the ad
        /// is skipped/fails, re-enables the button so the player can retry or press Continue. With no
        /// AdsService in scene it grants immediately (Editor testing).
        /// </summary>
        public void ClaimWinAdBonus()
        {
            if (!IsWon || winAdClaimed) return;
            if (winAdButton != null) winAdButton.interactable = false;   // dim + block double-tap

            System.Action<bool> onResult = earned =>
            {
                if (!earned)
                {
                    if (winAdButton != null) winAdButton.interactable = true;   // failed → let them retry
                    return;
                }
                winAdClaimed = true;
                PlayerEconomy.AddCoins(winAdBonusCoins);
                GoToNextLevel();   // auto-continue, skip the between-levels interstitial
            };

            if (AdsService.Instance != null) AdsService.Instance.ShowRewarded(string.Empty, onResult);
            else onResult(true);
        }

        void OnColumnLocked(Column c)
        {
            lockedCount++;
            UpdateFrozenColumns();
            UpdateThreadColumns(c);
            LogProgress();
            CheckWin(c);
        }

        /// <summary>
        /// Thread mechanic (independent): when a column completes, unlock every Thread column whose required
        /// color matches the just-completed column's color — its overlay plays the unlock animation and the
        /// column becomes playable.
        /// </summary>
        void UpdateThreadColumns(Column lockedCol)
        {
            if (lockedCol == null || !lockedCol.TryGetMonoColor(out var color) || string.IsNullOrEmpty(color)) return;
            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                if (col == null || !col.IsThreaded || col.ThreadColor != color) continue;
                col.RemoveThread();
                var overlay = col.GetComponentInChildren<ThreadColumnOverlay>(true);
                if (overlay != null) overlay.PlayUnlock();
            }
        }

        /// <summary>
        /// Recomputes each frozen-origin column's unlock progress, refreshes its FrozenOverlay count,
        /// and reconciles its frozen state in BOTH directions: auto-unfreezes a column once its
        /// threshold is met, and RE-freezes one whose progress later drops below the threshold (e.g. a
        /// Rewind undoes a contributing lock). Called from <see cref="OnColumnLocked"/> (after each lock,
        /// so the countdown ticks down live) and from <see cref="RefundMove"/> (after an undo).
        /// </summary>
        void UpdateFrozenColumns()
        {
            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                // Consider every column AUTHORED as frozen (even if currently unfrozen) so a Rewind that
                // drops the unlock progress can RE-freeze it — not just the currently-frozen ones.
                if (col == null || !col.WasFrozenColumn) continue;
                // A frozen-origin column that already unfroze AND was solved stays done — never re-freeze
                // a completed column.
                if (col.IsLocked) continue;

                // THREE unlock conditions. Break Wall Stack (neighbor mode): breaks once its ADJACENT columns
                // (left + right; edge column needs only its single neighbor) are LOCKED. Otherwise count-based:
                // Frozen counts ANY locked columns vs threshold; Lock Color Stack counts locked columns of the
                // required color. Use the AUTHORED spec (Initial*) so values survive an Unfreeze (Rewind re-freeze).
                bool shouldBeFrozen;
                int remaining;   // shown on the overlay number
                if (col.InitialFrozenNeighborMode)
                {
                    bool hasLeft  = i > 0;
                    bool hasRight = i < columns.Count - 1;
                    bool leftDone  = !hasLeft  || (columns[i - 1] != null && columns[i - 1].IsLocked);
                    bool rightDone = !hasRight || (columns[i + 1] != null && columns[i + 1].IsLocked);
                    shouldBeFrozen = !(leftDone && rightDone);
                    // How many EXISTING neighbors are still unsolved (interior 2 → 0, edge 1 → 0).
                    remaining = ((hasLeft && !leftDone) ? 1 : 0) + ((hasRight && !rightDone) ? 1 : 0);
                }
                else
                {
                    // Frozen (count ANY locked columns) OR Lock Color Stack (count locked of a specific color).
                    int progress = col.InitialFrozenLockColor
                        ? CountLockedOfColor(col.InitialFrozenRequiredColor, col)
                        : lockedCount;
                    shouldBeFrozen = progress < col.InitialFrozenThreshold;
                    remaining = col.InitialFrozenThreshold - progress;
                }

                // Either FrozenOverlay (2D) or FrozenColumnIce (3D) — both implement IFrozenOverlay.
                var overlay = col.GetComponentInChildren<IFrozenOverlay>(true);
                var overlayGo = (overlay as Component) != null ? ((Component)overlay).gameObject : null;

                if (shouldBeFrozen)
                {
                    // Re-freeze if a Rewind reopened this gate (no-op via the IsFrozen check if already frozen).
                    if (!col.IsFrozen)
                    {
                        if (col.InitialFrozenNeighborMode) col.FreezeNeighbors();
                        else col.Freeze(col.InitialFrozenThreshold, col.InitialFrozenLockColor, col.InitialFrozenRequiredColor);
                    }
                    if (overlay != null)
                    {
                        if (overlayGo != null && !overlayGo.activeSelf) overlayGo.SetActive(true);
                        overlay.SetRemaining(remaining);
                    }
                }
                else
                {
                    if (col.IsFrozen) { col.Unfreeze(); SfxManager.Play(SfxId.Unfreeze); }
                    if (overlayGo != null && overlayGo.activeSelf) overlayGo.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Counts locked columns whose single completed color equals <paramref name="color"/>, excluding
        /// <paramref name="exclude"/> (the frozen column itself). Used by Lock Color Stack to decide when
        /// its threshold of same-colored completions has been met.
        /// </summary>
        int CountLockedOfColor(string color, Column exclude)
        {
            int n = 0;
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                if (c == null || c == exclude || !c.IsLocked) continue;
                if (c.TryGetMonoColor(out var cc) && cc == color) n++;
            }
            return n;
        }

        void LogProgress()
        {
            var unsorted = new System.Text.StringBuilder();
            foreach (var c in columns)
                if (c != null && !c.IsLocked) unsorted.Append(c.name).Append(' ');
            Debug.Log($"[GameManager] {lockedCount}/{columns.Count} columns locked. Still unsorted: [{unsorted.ToString().TrimEnd()}]");
        }

        void CheckWin(Column lastLocked = null)
        {
            if (IsWon || IsLost) return;
            if (columns.Count > 0 && lockedCount >= columns.Count)
            {
                // Mark won immediately so input + skills are blocked (IsGameOver) during the win delay,
                // then refresh UI; the panel + rewards come after the pause so the final celebration shows.
                IsWon = true;
                StateChanged?.Invoke();
                StartCoroutine(WinSequence(lastLocked));
            }
        }

        /// <summary>Waits for the LAST column's celebration to fully play out (its real length, computed from
        /// the column's piece count + the celebration pacing) plus a small <see cref="PlayerHand.WinCompleteDelay"/>
        /// buffer, then reveals the Win panel + grants rewards — so the panel never pops up mid-animation.</summary>
        IEnumerator WinSequence(Column lastLocked)
        {
            var hand = PlayerHand.Instance;
            float delay = 0f;
            if (hand != null)
            {
                if (lastLocked != null) delay += hand.GetCelebrationDuration(CountPieces(lastLocked));
                delay += hand.WinCompleteDelay;
            }
            if (delay > 0f) yield return new WaitForSeconds(delay);

            SfxManager.Play(SfxId.Win);
            if (winPanel != null) winPanel.SetActive(true);

            var loader = LevelLoader.Instance;
            if (loader != null && loader.CurrentLevel != null)
            {
                LevelProgress.MarkCompleted(loader.CurrentLevel.levelNumber);
                PlayerEconomy.AddCoins(loader.CurrentLevel.coinReward);
                // Skill unlocks are derived from level reached (LevelDatabase thresholds) — completing
                // a level bumps HighestUnlocked above, which is all SkillProgress.IsUnlocked needs.
            }

            Debug.Log("Sort: player wins!");
            onWin?.Invoke();
            StateChanged?.Invoke();
        }

        /// <summary>Number of piece children in a column (overlays/effects aren't Pieces, so they don't count).</summary>
        static int CountPieces(Column col)
        {
            if (col == null) return 0;
            int n = 0;
            for (int i = 0; i < col.transform.childCount; i++)
                if (col.transform.GetChild(i).GetComponent<Piece>() != null) n++;
            return n;
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
