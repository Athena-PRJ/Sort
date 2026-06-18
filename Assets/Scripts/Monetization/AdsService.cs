using System;
using UnityEngine;

namespace Sort.Monetization
{
    /// <summary>
    /// Single entry point for ads (rewarded + interstitial). Game code calls the high-level helpers
    /// (<see cref="ShowContinueAd"/>, <see cref="ShowCoinsAd"/>, <see cref="MaybeShowInterstitial"/>); the
    /// actual network lives behind <see cref="IRewardedAdProvider"/> / <see cref="IInterstitialAdProvider"/>
    /// so it can be swapped without touching gameplay.
    ///
    /// SETUP: drop this on a GameObject in the FIRST scene (survives scene loads via DontDestroyOnLoad).
    /// With no SDK compiled it uses Mock providers (instant) so the flows are testable today. To go live:
    /// install LevelPlay (com.unity.services.levelplay), add the SORT_ADS_LEVELPLAY define, fill the App Key
    /// + Ad Unit IDs below, and turn off Force Mock Provider. See README_Monetization.md.
    /// </summary>
    public class AdsService : MonoBehaviour
    {
        public static AdsService Instance { get; private set; }

        // These are read only inside #if SORT_ADS_LEVELPLAY (CreateRewardedProvider /
        // CreateInterstitialProvider), so without that define they look "unused" — silence CS0414.
#pragma warning disable 0414
        [Header("LevelPlay (used only when SORT_ADS_LEVELPLAY is defined)")]
        [Tooltip("LevelPlay App Key from the dashboard.")]
        [SerializeField] private string levelPlayAppKey = "";
        [Tooltip("Rewarded Ad Unit ID (serves both continue + free-coins placements).")]
        [SerializeField] private string rewardedAdUnitId = "";
        [Tooltip("Interstitial Ad Unit ID (between-levels ads). Leave blank to disable interstitials.")]
        [SerializeField] private string interstitialAdUnitId = "";
#pragma warning restore 0414

        [Header("Placement names (optional — for capping/reporting)")]
        [SerializeField] private string continuePlacementId = "";
        [SerializeField] private string coinsPlacementId = "";
        [SerializeField] private string interstitialPlacementId = "";

        [Header("Rewards")]
        [Tooltip("Coins granted when the player completes a 'watch ad for coins' rewarded ad.")]
        [Min(0)] [SerializeField] private int coinsPerRewardedAd = 100;

        [Header("Interstitial frequency")]
        [Tooltip("Show an interstitial once every N MaybeShowInterstitial() calls (e.g. every 2 level ends). " +
                 "0 = never auto-show. Always suppressed when the player bought Remove Ads.")]
        [Min(0)] [SerializeField] private int interstitialEveryNCalls = 2;

        [Header("Behaviour")]
        [Tooltip("Force the no-SDK mock providers even if a real ad SDK is compiled. Handy for Editor testing.")]
        [SerializeField] private bool forceMockProvider;
        [Tooltip("In the Editor, the mock 'plays' for this long before resolving (feels like a real ad).")]
        [Min(0f)] [SerializeField] private float mockWatchSeconds = 0.4f;

        IRewardedAdProvider _rewarded;
        IInterstitialAdProvider _interstitial;
        int _interstitialCallCount;

        /// <summary>Fired after a rewarded ad finishes. bool = reward earned, string = placement id.</summary>
        public event Action<string, bool> RewardedFinished;

        public bool RewardedReady => _rewarded != null && _rewarded.IsReady;
        public bool InterstitialReady => _interstitial != null && _interstitial.IsReady;
        public string RewardedProviderName => _rewarded != null ? _rewarded.Name : "<none>";

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            _rewarded = CreateRewardedProvider();
            _interstitial = CreateInterstitialProvider();
            Debug.Log($"[AdsService] rewarded={_rewarded.Name}, interstitial={_interstitial.Name}");

            // Both providers share LevelPlaySdk.EnsureInit, so Init runs once even though we call both.
            _rewarded.Initialize(() =>
            {
                _rewarded.Load(continuePlacementId);
                _rewarded.Load(coinsPlacementId);
            });
            _interstitial.Initialize(() => _interstitial.Load(interstitialPlacementId));
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Picks the rewarded provider. Add a branch per SDK behind its define.</summary>
        IRewardedAdProvider CreateRewardedProvider()
        {
            if (!forceMockProvider)
            {
#if SORT_ADS_LEVELPLAY
                return new LevelPlayRewardedProvider(levelPlayAppKey, rewardedAdUnitId);
#endif
            }
            return new MockRewardedAdProvider(this, mockWatchSeconds);
        }

        /// <summary>Picks the interstitial provider.</summary>
        IInterstitialAdProvider CreateInterstitialProvider()
        {
            if (!forceMockProvider)
            {
#if SORT_ADS_LEVELPLAY
                return new LevelPlayInterstitialProvider(levelPlayAppKey, interstitialAdUnitId);
#endif
            }
            return new MockInterstitialAdProvider(this, Mathf.Min(mockWatchSeconds, 0.3f));
        }

        // ----- Rewarded -----

        /// <summary>Rewarded ad for the in-game continue. <paramref name="onResult"/> true only if earned.</summary>
        public void ShowContinueAd(Action<bool> onResult = null) => ShowRewarded(continuePlacementId, onResult);

        /// <summary>MainMenu 'watch ad for coins'. On success credits <see cref="coinsPerRewardedAd"/> coins.</summary>
        public void ShowCoinsAd(Action<bool> onResult = null)
        {
            ShowRewarded(coinsPlacementId, earned =>
            {
                if (earned) PlayerEconomy.AddCoins(coinsPerRewardedAd);
                onResult?.Invoke(earned);
            });
        }

        /// <summary>Low-level rewarded show. Resolves false immediately if no ad is ready.</summary>
        public void ShowRewarded(string placementId, Action<bool> onResult)
        {
            if (_rewarded == null) { onResult?.Invoke(false); return; }
            if (!_rewarded.IsReady)
            {
                Debug.LogWarning($"[AdsService] Rewarded '{placementId}' not ready; loading for next time.");
                _rewarded.Load(placementId);
                onResult?.Invoke(false);
                return;
            }
            _rewarded.Show(placementId, earned =>
            {
                RewardedFinished?.Invoke(placementId, earned);
                onResult?.Invoke(earned);
            });
        }

        // ----- Interstitial -----

        /// <summary>
        /// Called at natural breaks (e.g. before loading the next level). Shows an interstitial only if the
        /// player hasn't bought Remove Ads, the frequency gate is due, and an ad is ready — otherwise it just
        /// invokes <paramref name="onComplete"/>. Always call onComplete-style continuation through this so
        /// flow proceeds whether or not an ad shows.
        /// </summary>
        public void MaybeShowInterstitial(Action onComplete = null)
        {
            if (PlayerEconomy.AdsRemoved || interstitialEveryNCalls <= 0 || _interstitial == null)
            { onComplete?.Invoke(); return; }

            _interstitialCallCount++;
            bool due = (_interstitialCallCount % interstitialEveryNCalls) == 0;
            if (!due || !_interstitial.IsReady)
            {
                if (!_interstitial.IsReady) _interstitial.Load(interstitialPlacementId);
                onComplete?.Invoke();
                return;
            }
            _interstitial.Show(interstitialPlacementId, () => onComplete?.Invoke());
        }
    }
}
