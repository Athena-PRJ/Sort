using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Popup that asks the player whether to spend coins to buy 1 use of a skill when they
    /// try to use a skill with 0 stored uses. Wired by SkillManager via Show(SkillType).
    /// </summary>
    public class BuyUsesPanel : MonoBehaviour
    {
        public static BuyUsesPanel Instance { get; private set; }

        [Header("UI bindings")]
        [Tooltip("Root panel GameObject. Toggled active/inactive when shown/hidden.")]
        [SerializeField] private GameObject panel;

        [Tooltip("Text that displays the dialog message (e.g. 'Buy 1 Switch for 80 coins?'). " +
                 "Format: {0}=skill name, {1}=cost in coins.")]
        [SerializeField] private TMP_Text messageText;

        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;

        [Header("Message format")]
        [Tooltip("Localizable message template. {0} = skill name (Switch/Magnet). {1} = coin cost.")]
        [SerializeField] private string messageFormat = "Bạn không có lần dùng {0} nào. Mua 1 lần với {1} coins?";

        [Tooltip("Optional: dialog shown when player tries to buy but has insufficient coins.")]
        [SerializeField] private string notEnoughCoinsFormat = "Không đủ coins! Cần {0} coins (bạn có {1}).";

        SkillType pendingSkill;
        System.Action onPurchased;

        void Awake()
        {
            Instance = this;
            if (panel != null) panel.SetActive(false);
            if (yesButton != null) yesButton.onClick.AddListener(OnYes);
            if (noButton != null) noButton.onClick.AddListener(OnNo);
        }

        /// <summary>
        /// Show the dialog asking to buy 1 use of <paramref name="skill"/>. If the player taps Yes
        /// and has enough coins, deducts coins, adds 1 use, and invokes <paramref name="onSuccess"/>
        /// (e.g. so SkillManager can immediately enter skill mode with the just-purchased use).
        /// </summary>
        public void Show(SkillType skill, System.Action onSuccess)
        {
            if (skill == SkillType.Rewind) return; // Rewind uses a different free/paid model.
            if (panel == null) return;

            pendingSkill = skill;
            onPurchased = onSuccess;

            int cost = GetCostForSkill(skill);
            if (messageText != null)
                messageText.text = string.Format(messageFormat, skill.ToString(), cost);

            panel.SetActive(true);
        }

        public void Hide()
        {
            if (panel != null) panel.SetActive(false);
            onPurchased = null;
        }

        void OnYes()
        {
            int cost = GetCostForSkill(pendingSkill);
            if (PlayerEconomy.Coins < cost)
            {
                // Insufficient coins — flash a message and keep the panel open.
                if (messageText != null)
                    messageText.text = string.Format(notEnoughCoinsFormat, cost, PlayerEconomy.Coins);
                return;
            }

            bool ok = pendingSkill switch
            {
                SkillType.Switch => PlayerEconomy.TryBuySwitchUse(),
                SkillType.Magnet => PlayerEconomy.TryBuyMagnetUse(),
                _ => false
            };

            if (ok)
            {
                var cb = onPurchased;
                Hide();
                cb?.Invoke();
            }
        }

        void OnNo() => Hide();

        static int GetCostForSkill(SkillType skill)
        {
            var cfg = PlayerEconomy.Config;
            if (cfg == null) return 0;
            return skill switch
            {
                SkillType.Switch => cfg.coinsPerSwitchUse,
                SkillType.Magnet => cfg.coinsPerMagnetUse,
                _ => 0
            };
        }
    }
}
