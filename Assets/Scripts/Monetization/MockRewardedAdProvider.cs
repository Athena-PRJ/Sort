using System;
using UnityEngine;

namespace Sort.Monetization
{
    /// <summary>
    /// Stand-in rewarded-ad provider that needs no SDK. It reports "ready" immediately and, on Show,
    /// grants the reward (optionally after a short fake delay so the UI flow feels real). This is the
    /// default provider whenever no real ad SDK is compiled in, so the continue / free-coins flows can
    /// be exercised in the Editor and in dev builds.
    ///
    /// Drives its optional delay through <see cref="AdsService"/>'s coroutine runner (it is not a
    /// MonoBehaviour itself).
    /// </summary>
    public sealed class MockRewardedAdProvider : IRewardedAdProvider
    {
        readonly MonoBehaviour _runner;
        readonly float _fakeWatchSeconds;
        readonly bool _grantReward;

        /// <param name="runner">Used to schedule the fake watch delay (usually the AdsService).</param>
        /// <param name="fakeWatchSeconds">Simulated time the "ad" plays before the reward resolves.</param>
        /// <param name="grantReward">If false, simulates the player skipping (onResult(false)) — handy for testing the decline path.</param>
        public MockRewardedAdProvider(MonoBehaviour runner, float fakeWatchSeconds = 0.4f, bool grantReward = true)
        {
            _runner = runner;
            _fakeWatchSeconds = Mathf.Max(0f, fakeWatchSeconds);
            _grantReward = grantReward;
        }

        public string Name => "Mock";
        public bool IsInitialized => true;
        public bool IsReady => true;

        public void Initialize(Action onInitialized = null) => onInitialized?.Invoke();

        public void Load(string placementId) { /* always "ready" */ }

        public void Show(string placementId, Action<bool> onResult)
        {
            Debug.Log($"[AdsService] (Mock) Showing rewarded ad for '{placementId}' — will {(_grantReward ? "grant" : "skip")} reward.");
            if (_runner != null && _fakeWatchSeconds > 0f && _runner.isActiveAndEnabled)
                _runner.StartCoroutine(Delayed(onResult));
            else
                onResult?.Invoke(_grantReward);
        }

        System.Collections.IEnumerator Delayed(Action<bool> onResult)
        {
            yield return new WaitForSecondsRealtime(_fakeWatchSeconds);
            onResult?.Invoke(_grantReward);
        }
    }
}
