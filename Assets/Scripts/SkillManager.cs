using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Central hub for the player's in-game skills.
    ///
    /// Cost models:
    /// • Rewind — per-level free uses + coin cost for extras (existing behavior).
    /// • Switch / Magnet — STOCKPILE model: player accumulates uses (PlayerPrefs persistent across
    ///   levels). Each successful skill use decrements PlayerEconomy.SwitchUses / MagnetUses.
    ///   When count is 0, tapping the skill button shows BuyUsesPanel to buy 1 use with coins.
    /// </summary>
    public class SkillManager : MonoBehaviour
    {
        public static SkillManager Instance { get; private set; }

        [Header("Rewind (Skill 1 — always unlocked)")]
        [Tooltip("UI Button used as the Rewind trigger.")]
        [SerializeField] private Button rewindButton;
        [Tooltip("Optional lock overlay GameObject (e.g. an Image with Lock.png) on the Rewind button. Stays hidden since Rewind is always unlocked.")]
        [SerializeField] private GameObject rewindLockOverlay;
        [Tooltip("Root of the UNLOCKED-state visuals — e.g. BoosterStroke (which parents Booster → UsageCount). " +
                 "Shown when usable, hidden when the lock overlay is shown; disabling it cascades to its children, " +
                 "so ONE toggle hides the whole coin. Rewind is always unlocked, so this stays on.")]
        [SerializeField] private GameObject rewindUnlockedVisuals;

        [Header("Switch (Skill 2 — unlocks per LevelData flag)")]
        [SerializeField] private Button switchButton;
        [SerializeField] private GameObject switchLockOverlay;
        [Tooltip("Root of the UNLOCKED-state visuals (e.g. BoosterStroke → Booster → UsageCount). Shown only " +
                 "when the skill is unlocked; hidden (with the lock overlay shown) while locked.")]
        [SerializeField] private GameObject switchUnlockedVisuals;
        [Tooltip("Text label that shows the unlock-level number while the skill is locked (e.g. 'Lvl 5'). Hidden once unlocked.")]
        [SerializeField] private TMP_Text switchUnlockedLevelText;
        [Tooltip("Badge text that shows the count of stored Switch uses (PlayerEconomy.SwitchUses). " +
                 "When 0, tapping the button opens BuyUsesPanel instead of entering Switch mode.")]
        [SerializeField] private TMP_Text switchUsesBadge;

        [Header("Magnet (Skill 3 — unlocks per LevelData flag)")]
        [SerializeField] private Button magnetButton;
        [SerializeField] private GameObject magnetLockOverlay;
        [Tooltip("Root of the UNLOCKED-state visuals (e.g. BoosterStroke → Booster → UsageCount). Shown only " +
                 "when the skill is unlocked; hidden (with the lock overlay shown) while locked.")]
        [SerializeField] private GameObject magnetUnlockedVisuals;
        [Tooltip("Text label that shows the unlock-level number while the skill is locked. Hidden once unlocked.")]
        [SerializeField] private TMP_Text magnetUnlockedLevelText;
        [Tooltip("Badge text that shows the count of stored Magnet uses (PlayerEconomy.MagnetUses).")]
        [SerializeField] private TMP_Text magnetUsesBadge;

        [Header("Lock label formatting")]
        [Tooltip("Format string for the locked-state label. {0} = unlock level number from EconomyConfig.")]
        [SerializeField] private string unlockedLevelFormat = "Lvl {0}";

        [Header("Tester / dev build")]
        [Tooltip("Tester convenience: when ON, every skill is UNLOCKED and topped up with uses + coins on " +
                 "Start — so a device build can try all skills without grinding the unlock levels. Tops up to " +
                 "99 uses / 99999 coins (won't grow unbounded across launches). TURN OFF for the real release.")]
        [SerializeField] private bool devUnlockAllForTesting = false;

        [Header("Buy-uses dialog")]
        [Tooltip("Optional reference to the BuyUsesPanel that appears when the player taps a skill with 0 stored uses. " +
                 "If null, the SkillManager uses BuyUsesPanel.Instance (auto-discovered).")]
        [SerializeField] private BuyUsesPanel buyUsesPanel;

        int rewindUsesLeft;
        public int FreeRewindUsesLeft => rewindUsesLeft;

        void Awake()
        {
            Instance = this;
            // Rewind free uses come from the level. If no level loaded (debug), fall back to 1.
            var loader = LevelLoader.Instance;
            rewindUsesLeft = loader?.CurrentLevel != null ? loader.CurrentLevel.freeRewindUses : 1;

            // Wire button clicks programmatically — but ONLY when the designer has NOT already wired the
            // same call via the Inspector's On Click () list. RemoveAllListeners() drops only runtime
            // listeners; an Inspector "persistent" listener survives it, so blindly AddListener-ing on top
            // of an Inspector-wired button fires the method TWICE per click. For Switch/Magnet that means
            // "enter skill mode" then immediately "cancel skill mode" → the skill appears to do nothing.
            // Guarding on GetPersistentEventCount() makes us robust to EITHER setup (Inspector-wired or
            // not) and guarantees exactly one invocation per click.
            WireSkillButton(rewindButton, UseRewind);
            WireSkillButton(switchButton, UseSwitch);
            WireSkillButton(magnetButton, UseMagnet);
        }

        /// <summary>Adds the runtime onClick listener only if the button isn't already wired in the
        /// Inspector — prevents the double-invocation that silently cancels Switch/Magnet skill mode.</summary>
        static void WireSkillButton(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();                 // clear any stale runtime listeners
            if (button.onClick.GetPersistentEventCount() == 0)   // no Inspector wiring → wire in code
                button.onClick.AddListener(handler);
        }

        void Start()
        {
            // Tester build: unlock + stock every skill so the device build can try them all. Tops up to a
            // target instead of adding each launch, so repeated launches don't grow the stockpile unbounded.
            if (devUnlockAllForTesting)
            {
                SkillProgress.Unlock(SkillType.Switch);
                SkillProgress.Unlock(SkillType.Magnet);
                if (PlayerEconomy.SwitchUses < 99) PlayerEconomy.AddSwitchUses(99 - PlayerEconomy.SwitchUses);
                if (PlayerEconomy.MagnetUses < 99) PlayerEconomy.AddMagnetUses(99 - PlayerEconomy.MagnetUses);
                if (PlayerEconomy.Coins < 99999) PlayerEconomy.AddCoins(99999 - PlayerEconomy.Coins);
            }

            // Subscribe to events that can change a skill button's state.
            if (PlayerHand.Instance != null)
            {
                PlayerHand.Instance.StateChanged += RefreshButtons;
                PlayerHand.Instance.SkillModeChanged += RefreshButtons;
            }
            if (GameManager.Instance != null) GameManager.Instance.StateChanged += RefreshButtons;
            PlayerEconomy.Changed += RefreshButtons;

            RefreshButtons();
        }

        void OnDestroy()
        {
            if (PlayerHand.Instance != null)
            {
                PlayerHand.Instance.StateChanged -= RefreshButtons;
                PlayerHand.Instance.SkillModeChanged -= RefreshButtons;
            }
            if (GameManager.Instance != null) GameManager.Instance.StateChanged -= RefreshButtons;
            PlayerEconomy.Changed -= RefreshButtons;
        }

        // ---------------------------------------------------------------------
        //  Rewind (Skill 1) — unchanged model: per-level free + coin extras
        // ---------------------------------------------------------------------

        public bool CanUseFreeRewind
        {
            get
            {
                if (rewindUsesLeft <= 0) return false;
                if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return false;
                if (PlayerHand.Instance == null) return false;
                return PlayerHand.Instance.CanUndo;
            }
        }

        public bool CanPurchaseRewind
        {
            get
            {
                if (rewindUsesLeft > 0) return false;
                if (PlayerEconomy.Config == null) return false;
                if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return false;
                if (PlayerHand.Instance == null || !PlayerHand.Instance.CanUndo) return false;
                return PlayerEconomy.Coins >= PlayerEconomy.Config.coinsPerRewindUse;
            }
        }

        public bool CanUseRewind => CanUseFreeRewind || CanPurchaseRewind;

        public void UseRewind()
        {
            if (CanUseFreeRewind)
            {
                PlayerHand.Instance.Undo();
                rewindUsesLeft--;
            }
            else if (CanPurchaseRewind)
            {
                if (PlayerEconomy.TrySpendCoins(PlayerEconomy.Config.coinsPerRewindUse))
                    PlayerHand.Instance.Undo();
            }
            else return;

            RefreshButtons();
        }

        // ---------------------------------------------------------------------
        //  Switch (Skill 2) — stockpile model
        // ---------------------------------------------------------------------

        /// <summary>True if the player can enter Switch mode right now using a STORED use.</summary>
        public bool HasStoredSwitchUse => PlayerEconomy.SwitchUses > 0;

        /// <summary>True if the skill is unlocked, the game isn't over, no animation is running.</summary>
        public bool CanUseSwitch
        {
            get
            {
                if (!SkillProgress.IsUnlocked(SkillType.Switch)) return false;
                if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return false;
                if (PlayerHand.Instance == null || PlayerHand.Instance.IsAnimating) return false;
                return true;
            }
        }

        public void UseSwitch()
        {
            Debug.Log($"[SkillManager] UseSwitch() called — unlocked={SkillProgress.IsUnlocked(SkillType.Switch)}, stored={PlayerEconomy.SwitchUses}, canUse={CanUseSwitch}, currentMode={(PlayerHand.Instance != null ? PlayerHand.Instance.CurrentSkillMode.ToString() : "no-hand")}");
            if (PlayerHand.Instance == null) return;
            // Tapping again while already in Switch mode = cancel.
            if (PlayerHand.Instance.CurrentSkillMode == PlayerHand.SkillMode.Switch)
            {
                PlayerHand.Instance.CancelSkillMode();
                RefreshButtons();
                return;
            }
            if (!CanUseSwitch) { Debug.Log("[SkillManager] UseSwitch aborted — CanUseSwitch false."); return; }

            // Stockpile branch:
            // • Has at least 1 stored use → enter Switch mode immediately.
            // • No stored uses → open BuyUsesPanel; on successful purchase, re-enter Switch mode.
            if (HasStoredSwitchUse)
            {
                PlayerHand.Instance.BeginSwitchMode();
            }
            else
            {
                ShowBuyDialog(SkillType.Switch, () =>
                {
                    // After the player buys 1 use, immediately enter Switch mode so they can use it.
                    if (PlayerHand.Instance != null && CanUseSwitch && HasStoredSwitchUse)
                        PlayerHand.Instance.BeginSwitchMode();
                    RefreshButtons();
                });
            }
            RefreshButtons();
        }

        // ---------------------------------------------------------------------
        //  Magnet (Skill 3) — stockpile model
        // ---------------------------------------------------------------------

        public bool HasStoredMagnetUse => PlayerEconomy.MagnetUses > 0;

        public bool CanUseMagnet
        {
            get
            {
                if (!SkillProgress.IsUnlocked(SkillType.Magnet)) return false;
                if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return false;
                if (PlayerHand.Instance == null || PlayerHand.Instance.IsAnimating) return false;
                return true;
            }
        }

        public void UseMagnet()
        {
            Debug.Log($"[SkillManager] UseMagnet() called — unlocked={SkillProgress.IsUnlocked(SkillType.Magnet)}, stored={PlayerEconomy.MagnetUses}, canUse={CanUseMagnet}, currentMode={(PlayerHand.Instance != null ? PlayerHand.Instance.CurrentSkillMode.ToString() : "no-hand")}");
            if (PlayerHand.Instance == null) return;
            if (PlayerHand.Instance.CurrentSkillMode == PlayerHand.SkillMode.Magnet)
            {
                PlayerHand.Instance.CancelSkillMode();
                RefreshButtons();
                return;
            }
            if (!CanUseMagnet) { Debug.Log("[SkillManager] UseMagnet aborted — CanUseMagnet false."); return; }

            if (HasStoredMagnetUse)
            {
                PlayerHand.Instance.BeginMagnetMode();
            }
            else
            {
                ShowBuyDialog(SkillType.Magnet, () =>
                {
                    if (PlayerHand.Instance != null && CanUseMagnet && HasStoredMagnetUse)
                        PlayerHand.Instance.BeginMagnetMode();
                    RefreshButtons();
                });
            }
            RefreshButtons();
        }

        // ---------------------------------------------------------------------
        //  Buy dialog
        // ---------------------------------------------------------------------

        void ShowBuyDialog(SkillType skill, System.Action onPurchased)
        {
            var dlg = buyUsesPanel != null ? buyUsesPanel : BuyUsesPanel.Instance;
            if (dlg == null)
            {
                Debug.LogWarning("[SkillManager] No BuyUsesPanel wired and none found in scene — can't show buy dialog.");
                return;
            }
            dlg.Show(skill, onPurchased);
        }

        // ---------------------------------------------------------------------
        //  Button state
        // ---------------------------------------------------------------------

        // Per-frame Update safety net: catches any case where an event-driven refresh was missed
        // (e.g. PlayerHand.Instance not yet set at SkillManager.Start, or RefreshButtons firing
        // mid-construction with stale conditions). Costs ~6 bool comparisons + assignments per
        // frame — negligible. Logs only when interactable values actually change so it's not noisy.
        void Update() => RefreshButtons();

        bool prevSwitchInteractable, prevMagnetInteractable, prevRewindInteractable;
        bool stateInited;

        void RefreshButtons()
        {
            // Rewind — always unlocked.
            bool rewindTarget = CanUseRewind;
            if (rewindButton != null) rewindButton.interactable = rewindTarget;
            if (rewindLockOverlay != null) rewindLockOverlay.SetActive(false);
            if (rewindUnlockedVisuals != null) rewindUnlockedVisuals.SetActive(true);

            // Switch — locked label OR stock badge.
            bool switchUnlocked = SkillProgress.IsUnlocked(SkillType.Switch);
            bool switchTarget = switchUnlocked && CanUseSwitch;
            if (switchButton != null) switchButton.interactable = switchTarget;
            if (switchLockOverlay != null) switchLockOverlay.SetActive(!switchUnlocked);
            // Hide the coin visuals (BoosterStroke → Booster → UsageCount cascade) while locked.
            if (switchUnlockedVisuals != null) switchUnlockedVisuals.SetActive(switchUnlocked);
            UpdateUnlockedLevelLabel(switchUnlockedLevelText, switchUnlocked, SkillType.Switch);
            UpdateUsesBadge(switchUsesBadge, switchUnlocked, PlayerEconomy.SwitchUses);

            // Magnet — same pattern.
            bool magnetUnlocked = SkillProgress.IsUnlocked(SkillType.Magnet);
            bool magnetTarget = magnetUnlocked && CanUseMagnet;
            if (magnetButton != null) magnetButton.interactable = magnetTarget;
            if (magnetLockOverlay != null) magnetLockOverlay.SetActive(!magnetUnlocked);
            if (magnetUnlockedVisuals != null) magnetUnlockedVisuals.SetActive(magnetUnlocked);
            UpdateUnlockedLevelLabel(magnetUnlockedLevelText, magnetUnlocked, SkillType.Magnet);
            UpdateUsesBadge(magnetUsesBadge, magnetUnlocked, PlayerEconomy.MagnetUses);

            // Only log when something actually changed so the per-frame Update doesn't spam.
            if (!stateInited || switchTarget != prevSwitchInteractable || magnetTarget != prevMagnetInteractable || rewindTarget != prevRewindInteractable)
            {
                Debug.Log($"[SkillManager] interactable changed → rewind:{prevRewindInteractable}→{rewindTarget}, switch:{prevSwitchInteractable}→{switchTarget}, magnet:{prevMagnetInteractable}→{magnetTarget}");
                prevSwitchInteractable = switchTarget;
                prevMagnetInteractable = magnetTarget;
                prevRewindInteractable = rewindTarget;
                stateInited = true;
            }
        }

        void UpdateUnlockedLevelLabel(TMP_Text label, bool unlocked, SkillType skill)
        {
            if (label == null) return;
            if (unlocked) { label.gameObject.SetActive(false); return; }

            label.gameObject.SetActive(true);
            int threshold = SkillProgress.GetUnlockLevel(skill);
            label.text = string.Format(unlockedLevelFormat, threshold);
        }

        /// <summary>
        /// Shows the current stored-use count when the skill is unlocked. Hidden when locked
        /// (so it doesn't overlap with the unlock-level label). Plain integer formatting.
        /// </summary>
        void UpdateUsesBadge(TMP_Text badge, bool unlocked, int count)
        {
            if (badge == null) return;
            if (!unlocked) { badge.gameObject.SetActive(false); return; }
            badge.gameObject.SetActive(true);
            badge.text = count.ToString();
        }

        // ---------------------------------------------------------------------
        //  Dev cheats — right-click the SkillManager component header to access.
        //  Use these to test skills without grinding through Level3 / Level5 first.
        // ---------------------------------------------------------------------

        [ContextMenu("Dev: Unlock Switch + give 5 uses")]
        void DevUnlockSwitch()
        {
            SkillProgress.Unlock(SkillType.Switch);
            PlayerEconomy.AddSwitchUses(5);
            RefreshButtons();
            Debug.Log("[DEV] Switch unlocked, +5 uses. Total SwitchUses = " + PlayerEconomy.SwitchUses);
        }

        [ContextMenu("Dev: Unlock Magnet + give 5 uses")]
        void DevUnlockMagnet()
        {
            SkillProgress.Unlock(SkillType.Magnet);
            PlayerEconomy.AddMagnetUses(5);
            RefreshButtons();
            Debug.Log("[DEV] Magnet unlocked, +5 uses. Total MagnetUses = " + PlayerEconomy.MagnetUses);
        }

        [ContextMenu("Dev: Add 1000 coins (test buying)")]
        void DevAddCoins()
        {
            PlayerEconomy.AddCoins(1000);
            Debug.Log("[DEV] +1000 coins. Total = " + PlayerEconomy.Coins);
        }

        [ContextMenu("Dev: Log Switch/Magnet button state")]
        void DevLogSkillState()
        {
            Debug.Log("======= Skill State BEFORE refresh =======");
            Debug.Log($"Switch — unlocked={SkillProgress.IsUnlocked(SkillType.Switch)}, uses={PlayerEconomy.SwitchUses}, canUse={CanUseSwitch}, " +
                      $"buttonWired={switchButton != null}, buttonInteractable={(switchButton != null ? switchButton.interactable : false)}");
            Debug.Log($"Magnet — unlocked={SkillProgress.IsUnlocked(SkillType.Magnet)}, uses={PlayerEconomy.MagnetUses}, canUse={CanUseMagnet}, " +
                      $"buttonWired={magnetButton != null}, buttonInteractable={(magnetButton != null ? magnetButton.interactable : false)}");
            Debug.Log($"GameManager.IsGameOver={(GameManager.Instance != null ? GameManager.Instance.IsGameOver.ToString() : "no-gm")}, " +
                      $"PlayerHand.IsAnimating={(PlayerHand.Instance != null ? PlayerHand.Instance.IsAnimating.ToString() : "no-hand")}");

            RefreshButtons();

            Debug.Log("======= Skill State AFTER refresh =======");
            Debug.Log($"Switch.interactable={(switchButton != null ? switchButton.interactable : false)}, Magnet.interactable={(magnetButton != null ? magnetButton.interactable : false)}");
        }

        [ContextMenu("Dev: Reset all skill progress (lock everything)")]
        void DevResetSkills()
        {
            SkillProgress.ResetAll();
            // Reset stockpiled uses too so dev can verify locked → empty stockpile state.
            UnityEngine.PlayerPrefs.SetInt("Sort_SwitchUses", 0);
            UnityEngine.PlayerPrefs.SetInt("Sort_MagnetUses", 0);
            UnityEngine.PlayerPrefs.Save();
            RefreshButtons();
            Debug.Log("[DEV] All skills re-locked, stockpiles cleared.");
        }
    }
}
