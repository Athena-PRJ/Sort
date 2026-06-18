using System;

namespace Sort.Monetization
{
    /// <summary>
    /// SDK-agnostic seam for a rewarded-video ad network. <see cref="AdsService"/> talks ONLY to this
    /// interface, so the ad network can be swapped without touching game code.
    ///
    /// Ship-time providers in this project:
    ///   • <see cref="MockRewardedAdProvider"/> — always available, "rewards" instantly. Used in the Editor
    ///     and whenever no real SDK is compiled, so the continue / free-coins flows are fully testable now.
    ///   • LevelPlayRewardedProvider — real Unity LevelPlay, compiled only when SORT_ADS_LEVELPLAY is defined.
    ///
    /// To add another network: implement this interface and return it from
    /// <see cref="AdsService.CreateRewardedProvider"/> behind your own define symbol.
    /// </summary>
    public interface IRewardedAdProvider
    {
        /// <summary>Human-readable provider name, for logging.</summary>
        string Name { get; }

        /// <summary>True once the SDK finished initializing.</summary>
        bool IsInitialized { get; }

        /// <summary>True when a rewarded ad is loaded and ready to <see cref="Show"/>.</summary>
        bool IsReady { get; }

        /// <summary>Initialize the SDK. <paramref name="onInitialized"/> fires when ready (success or not — check IsInitialized).</summary>
        void Initialize(Action onInitialized = null);

        /// <summary>Pre-load a rewarded ad so it is ready to show. Safe to call repeatedly.</summary>
        void Load(string placementId);

        /// <summary>
        /// Show the rewarded ad for <paramref name="placementId"/>. <paramref name="onResult"/> is invoked
        /// exactly once: true if the player earned the reward (watched to completion), false if the ad was
        /// skipped, failed, or was unavailable. The provider should auto-reload after closing.
        /// </summary>
        void Show(string placementId, Action<bool> onResult);
    }
}
