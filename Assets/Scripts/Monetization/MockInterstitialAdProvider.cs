using System;
using UnityEngine;

namespace Sort.Monetization
{
    /// <summary>
    /// Stand-in interstitial provider that needs no SDK. Reports ready immediately and, on Show, invokes
    /// onClosed (optionally after a short fake delay). Default whenever no real ad SDK is compiled, so the
    /// between-levels interstitial flow can be exercised in the Editor.
    /// </summary>
    public sealed class MockInterstitialAdProvider : IInterstitialAdProvider
    {
        readonly MonoBehaviour _runner;
        readonly float _fakeShowSeconds;

        public MockInterstitialAdProvider(MonoBehaviour runner, float fakeShowSeconds = 0.3f)
        {
            _runner = runner;
            _fakeShowSeconds = Mathf.Max(0f, fakeShowSeconds);
        }

        public string Name => "Mock";
        public bool IsInitialized => true;
        public bool IsReady => true;

        public void Initialize(Action onInitialized = null) => onInitialized?.Invoke();
        public void Load(string placementId) { }

        public void Show(string placementId, Action onClosed)
        {
            Debug.Log($"[AdsService] (Mock) Showing interstitial for '{placementId}'.");
            if (_runner != null && _fakeShowSeconds > 0f && _runner.isActiveAndEnabled)
                _runner.StartCoroutine(Delayed(onClosed));
            else
                onClosed?.Invoke();
        }

        System.Collections.IEnumerator Delayed(Action onClosed)
        {
            yield return new WaitForSecondsRealtime(_fakeShowSeconds);
            onClosed?.Invoke();
        }
    }
}
