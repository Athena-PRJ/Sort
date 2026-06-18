// Real interstitial provider for Unity LevelPlay (com.unity.services.levelplay v9+). Same ad-unit model as
// the rewarded provider: ONE LevelPlayInterstitialAd from an interstitial AD UNIT ID; a placement NAME is
// passed to ShowAd. Compiled only when SORT_ADS_LEVELPLAY is defined. App key + ad unit id come from the
// AdsService Inspector (see AdsService.CreateInterstitialProvider).
#if SORT_ADS_LEVELPLAY
using System;
using UnityEngine;
using Unity.Services.LevelPlay;

namespace Sort.Monetization
{
    public sealed class LevelPlayInterstitialProvider : IInterstitialAdProvider
    {
        readonly string _appKey;
        readonly string _adUnitId;

        bool _initialized;
        Action _onInitialized;

        LevelPlayInterstitialAd _ad;
        Action _onClosed;

        public LevelPlayInterstitialProvider(string appKey, string adUnitId)
        {
            _appKey = appKey;
            _adUnitId = adUnitId;
        }

        public string Name => "LevelPlay-Interstitial";
        public bool IsInitialized => _initialized;
        public bool IsReady => _ad != null && _ad.IsAdReady();

        public void Initialize(Action onInitialized = null)
        {
            _onInitialized = onInitialized;
            LevelPlaySdk.EnsureInit(_appKey, ok =>
            {
                _initialized = ok;
                if (ok) CreateAd();
                _onInitialized?.Invoke();
            });
        }

        void CreateAd()
        {
            if (_ad != null || string.IsNullOrEmpty(_adUnitId)) return;
            _ad = new LevelPlayInterstitialAd(_adUnitId);
            _ad.OnAdClosed        += info => Resolve();
            _ad.OnAdDisplayFailed += (info, err) => { Debug.LogWarning($"[AdsService] LevelPlay interstitial show failed: {err}"); Resolve(); };
            _ad.OnAdLoadFailed    += err => Debug.LogWarning($"[AdsService] LevelPlay interstitial load failed: {err}");
            _ad.LoadAd();
        }

        public void Load(string placementId)
        {
            if (_ad == null) { if (_initialized) CreateAd(); return; }
            if (!_ad.IsAdReady()) _ad.LoadAd();
        }

        public void Show(string placementId, Action onClosed)
        {
            if (_ad == null || !_ad.IsAdReady())
            {
                onClosed?.Invoke();
                Load(placementId);
                return;
            }
            _onClosed = onClosed;
            if (string.IsNullOrEmpty(placementId)) _ad.ShowAd();
            else _ad.ShowAd(placementName: placementId);
        }

        void Resolve()
        {
            var cb = _onClosed;
            _onClosed = null;
            cb?.Invoke();
            _ad?.LoadAd();
        }
    }
}
#endif
