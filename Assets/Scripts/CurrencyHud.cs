using Sort.Monetization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sort
{
    /// <summary>
    /// MainMenu HUD for Coins + Lives. Binds to <see cref="PlayerEconomy.Changed"/> for instant coin/life
    /// updates, and ticks the life-refresh countdown once a second (time passes continuously, so the timer
    /// is polled, not event-driven). Drop on a HUD object and assign the Texts/buttons you have.
    ///
    /// Testable now with placeholder TMP texts — no art needed. Wire real art when it arrives.
    /// </summary>
    public class CurrencyHud : MonoBehaviour
    {
        [Header("Coins")]
        [SerializeField] private TMP_Text coinsText;

        [Header("Lives")]
        [SerializeField] private TMP_Text livesText;
        [Tooltip("Countdown to the next life refill (e.g. '59:12'). Shows the Full Label when lives are max.")]
        [SerializeField] private TMP_Text lifeTimerText;
        [Tooltip("Optional root shown ONLY while a refill is counting down (hidden when lives are full).")]
        [SerializeField] private GameObject lifeTimerRoot;
        [SerializeField] private string fullLabel = "FULL";

        [Header("Buttons (optional)")]
        [Tooltip("Buy a batch of lives with coins (PlayerEconomy.TryBuyLives). Auto-disabled when at max.")]
        [SerializeField] private Button buyLifeButton;
        [Tooltip("Watch a rewarded ad for free coins. Routes through AdsService (coin amount is set there). " +
                 "Falls back to the amount below if no AdsService is present.")]
        [SerializeField] private Button watchAdButton;
        [Tooltip("Coins granted by the fallback when no AdsService exists (Editor testing).")]
        [Min(0)] [SerializeField] private int fallbackAdCoins = 100;

        float nextTick;

        void OnEnable()
        {
            PlayerEconomy.Changed += Refresh;
            if (buyLifeButton != null) buyLifeButton.onClick.AddListener(BuyLife);
            if (watchAdButton != null) watchAdButton.onClick.AddListener(WatchAdForCoins);
            Refresh();
        }

        void OnDisable()
        {
            PlayerEconomy.Changed -= Refresh;
            if (buyLifeButton != null) buyLifeButton.onClick.RemoveListener(BuyLife);
            if (watchAdButton != null) watchAdButton.onClick.RemoveListener(WatchAdForCoins);
        }

        void Update()
        {
            // Tick the countdown ~1×/sec (Changed doesn't fire as time elapses).
            if (Time.unscaledTime >= nextTick)
            {
                nextTick = Time.unscaledTime + 1f;
                RefreshLifeTimer();
            }
        }

        void Refresh()
        {
            if (coinsText != null) coinsText.text = PlayerEconomy.Coins.ToString();
            if (livesText != null) livesText.text = PlayerEconomy.Lives.ToString();
            if (buyLifeButton != null) buyLifeButton.interactable = !PlayerEconomy.LivesAtMax;
            RefreshLifeTimer();
        }

        void RefreshLifeTimer()
        {
            bool full = PlayerEconomy.LivesAtMax;
            if (lifeTimerRoot != null) lifeTimerRoot.SetActive(!full);
            if (lifeTimerText == null) return;
            if (full) { lifeTimerText.text = fullLabel; return; }

            var t = PlayerEconomy.TimeUntilNextLifeRefresh;
            lifeTimerText.text = t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes:00}:{t.Seconds:00}";
        }

        /// <summary>Buy Life button → spend coins for a life batch (no-op if at max / not enough coins).</summary>
        public void BuyLife() => PlayerEconomy.TryBuyLives();

        /// <summary>Watch Ad button → show a rewarded ad via AdsService; coins are credited on success.
        /// If no AdsService is in the scene, grants the fallback amount immediately (Editor testing).</summary>
        public void WatchAdForCoins()
        {
            if (AdsService.Instance != null) AdsService.Instance.ShowCoinsAd();
            else GrantCoinsFromAd(fallbackAdCoins);
        }

        /// <summary>Call from a rewarded-ad success callback to grant coins (the MainMenu 'watch ad' flow).</summary>
        public void GrantCoinsFromAd(int amount) => PlayerEconomy.AddCoins(amount);
    }
}
