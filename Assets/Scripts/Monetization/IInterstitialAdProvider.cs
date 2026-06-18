using System;

namespace Sort.Monetization
{
    /// <summary>
    /// SDK-agnostic seam for full-screen interstitial ads (no reward — shown e.g. between levels).
    /// <see cref="AdsService"/> talks only to this interface. Interstitials are suppressed when the player
    /// has bought "Remove Ads" (<see cref="PlayerEconomy.AdsRemoved"/>) — that gate lives in AdsService.
    ///
    /// Providers: <see cref="MockInterstitialAdProvider"/> (no SDK) and LevelPlayInterstitialProvider
    /// (real, behind SORT_ADS_LEVELPLAY).
    /// </summary>
    public interface IInterstitialAdProvider
    {
        string Name { get; }
        bool IsInitialized { get; }
        bool IsReady { get; }

        void Initialize(Action onInitialized = null);
        void Load(string placementId);

        /// <summary>Show the interstitial; <paramref name="onClosed"/> fires once when it closes (or fails to show).</summary>
        void Show(string placementId, Action onClosed);
    }
}
