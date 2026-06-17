using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// Displays the current coins, lives, and refill countdown.
    /// Attach to any GameObject in a UI panel and wire the optional fields in the Inspector.
    /// Also exposes BuyLives() as a public method so a "Buy Lives" button can call it.
    /// </summary>
    public class EconomyHUD : MonoBehaviour
    {
        [Header("Optional UI bindings (leave null if not used)")]
        [SerializeField] private TMP_Text coinsText;
        [SerializeField] private TMP_Text livesText;
        [SerializeField] private TMP_Text livesRefillTimerText;
        [SerializeField] private Button buyLivesButton;

        [Header("Formatting (edit to change the label style)")]
        [SerializeField] private string coinsFormat = "Coins: {0}";
        [SerializeField] private string livesFormat = "Lives: {0}/{1}";
        [SerializeField] private string refillFormat = "Next life in {0:mm\\:ss}";
        [SerializeField] private string fullFormat = "Full";

        void OnEnable()
        {
            PlayerEconomy.Changed += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            PlayerEconomy.Changed -= Refresh;
        }

        float nextTick;
        void Update()
        {
            // Throttle to 1 Hz: the timer is mm:ss (no need for 60 updates/s), and string.Format here
            // allocates a string each call — running it every frame was a steady GC drip → frame spikes.
            // Coin/life CHANGES still refresh instantly via PlayerEconomy.Changed → Refresh().
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;
            if (livesRefillTimerText != null) UpdateRefillTimer();
            if (buyLivesButton != null) buyLivesButton.interactable = CanBuyLives();
        }

        void Refresh()
        {
            if (coinsText != null) coinsText.text = string.Format(coinsFormat, PlayerEconomy.Coins);

            if (livesText != null)
            {
                int max = PlayerEconomy.Config != null ? PlayerEconomy.Config.maxLives : PlayerEconomy.Lives;
                livesText.text = string.Format(livesFormat, PlayerEconomy.Lives, max);
            }

            UpdateRefillTimer();

            if (buyLivesButton != null) buyLivesButton.interactable = CanBuyLives();
        }

        void UpdateRefillTimer()
        {
            if (livesRefillTimerText == null) return;
            if (PlayerEconomy.LivesAtMax) { livesRefillTimerText.text = fullFormat; return; }
            var remaining = PlayerEconomy.TimeUntilNextLifeRefresh;
            livesRefillTimerText.text = string.Format(refillFormat, remaining);
        }

        bool CanBuyLives()
        {
            var cfg = PlayerEconomy.Config;
            if (cfg == null) return false;
            if (PlayerEconomy.LivesAtMax) return false;
            return PlayerEconomy.Coins >= cfg.coinsPerLifePurchase;
        }

        /// <summary>Wire this to a "Buy Lives" UI button. Returns true if the purchase succeeded.</summary>
        public void BuyLives()
        {
            bool ok = PlayerEconomy.TryBuyLives();
            if (!ok) Debug.Log("[EconomyHUD] Couldn't buy lives — not enough coins or already at max.");
        }

        /// <summary>Optional helper button to wipe progress during testing.</summary>
        public void ResetForTesting()
        {
            PlayerEconomy.ResetEconomy();
        }
    }
}
