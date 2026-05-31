using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Central hub for the player's in-game skills (rewind, magnet, etc.).
    /// State is per-playthrough — resets every time the scene reloads.
    /// </summary>
    public class SkillManager : MonoBehaviour
    {
        public static SkillManager Instance { get; private set; }

        [Header("Rewind (Skill 1 — always unlocked)")]
        [Tooltip("UI Button used as the Rewind trigger.")]
        [SerializeField] private Button rewindButton;
        [Tooltip("Optional lock overlay GameObject (e.g. an Image with Lock.png) on the Rewind button. Stays hidden since Rewind is always unlocked.")]
        [SerializeField] private GameObject rewindLockOverlay;

        [Header("Skill 2 (locked until SkillProgress.Unlock is called)")]
        [SerializeField] private Button skill2Button;
        [SerializeField] private GameObject skill2LockOverlay;

        [Header("Skill 3 (locked until SkillProgress.Unlock is called)")]
        [SerializeField] private Button skill3Button;
        [SerializeField] private GameObject skill3LockOverlay;

        int rewindUsesLeft;
        public int FreeRewindUsesLeft => rewindUsesLeft;

        void Awake()
        {
            Instance = this;
            // Free uses come from the level. If no level loaded (debug), fall back to 1.
            var loader = LevelLoader.Instance;
            rewindUsesLeft = loader?.CurrentLevel != null ? loader.CurrentLevel.freeRewindUses : 1;
        }

        void Start()
        {
            // Subscribe to the events that can change a skill button's state, instead of polling every frame.
            if (PlayerHand.Instance != null) PlayerHand.Instance.StateChanged += RefreshButtons;
            if (GameManager.Instance != null) GameManager.Instance.StateChanged += RefreshButtons;
            PlayerEconomy.Changed += RefreshButtons;

            RefreshButtons();
        }

        void OnDestroy()
        {
            if (PlayerHand.Instance != null) PlayerHand.Instance.StateChanged -= RefreshButtons;
            if (GameManager.Instance != null) GameManager.Instance.StateChanged -= RefreshButtons;
            PlayerEconomy.Changed -= RefreshButtons;
        }

        /// <summary>True if the player has at least one free Rewind use available.</summary>
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

        /// <summary>True if the player has zero free uses but enough coins to buy one.</summary>
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

        /// <summary>True overall — either a free use is available, or the player can afford a paid one.</summary>
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

        void RefreshButtons()
        {
            // Rewind — always unlocked. Just usability.
            bool canUse = CanUseRewind;
            if (rewindButton != null) rewindButton.interactable = canUse;
            if (rewindLockOverlay != null) rewindLockOverlay.SetActive(false);

            // Skill 2 — locked unless SkillProgress unlocks it. Even if unlocked, it has no logic yet.
            bool s2Unlocked = SkillProgress.IsUnlocked(SkillType.Skill2);
            if (skill2Button != null) skill2Button.interactable = s2Unlocked;
            if (skill2LockOverlay != null) skill2LockOverlay.SetActive(!s2Unlocked);

            // Skill 3 — same pattern.
            bool s3Unlocked = SkillProgress.IsUnlocked(SkillType.Skill3);
            if (skill3Button != null) skill3Button.interactable = s3Unlocked;
            if (skill3LockOverlay != null) skill3LockOverlay.SetActive(!s3Unlocked);
        }
    }
}
