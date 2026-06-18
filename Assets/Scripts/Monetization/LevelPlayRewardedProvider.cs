// Real rewarded-ad provider for Unity LevelPlay (com.unity.services.levelplay, "Ads Mediation" v9+).
// This is Unity's SUPPORTED monetization path (the legacy Unity Ads network was retired for monetization
// on 2026-01-31). LevelPlay is ad-unit based: ONE LevelPlayRewardedAd is created from a rewarded AD UNIT
// ID, and a PLACEMENT NAME is passed to ShowAd for capping/reporting — so AdsService's continuePlacementId
// / coinsPlacementId map to LevelPlay PLACEMENT NAMES here, all served by this single shared ad unit.
//
// Compiled only when SORT_ADS_LEVELPLAY is defined. App key + ad unit id come from the AdsService Inspector
// (see AdsService.CreateRewardedProvider). Setup steps: Assets/Scripts/Monetization/README_Monetization.md.
#if SORT_ADS_LEVELPLAY
using System;
using UnityEngine;
using Unity.Services.LevelPlay;

namespace Sort.Monetization
{
    public sealed class LevelPlayRewardedProvider : IRewardedAdProvider
    {
        readonly string _appKey;
        readonly string _adUnitId;

        bool _initialized;
        Action _onInitialized;

        LevelPlayRewardedAd _ad;
        Action<bool> _onShowResult;
        bool _rewardedThisShow;

        public LevelPlayRewardedProvider(string appKey, string adUnitId)
        {
            _appKey = appKey;
            _adUnitId = adUnitId;
        }

        public string Name => "LevelPlay-Rewarded";
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
            _ad = new LevelPlayRewardedAd(_adUnitId);
            _ad.OnAdRewarded      += (info, reward) => _rewardedThisShow = true;
            _ad.OnAdClosed        += info => Resolve(_rewardedThisShow);
            _ad.OnAdDisplayFailed += (info, err) => { Debug.LogWarning($"[AdsService] LevelPlay rewarded show failed: {err}"); Resolve(false); };
            _ad.OnAdLoadFailed    += err => Debug.LogWarning($"[AdsService] LevelPlay rewarded load failed: {err}");
            _ad.LoadAd();
        }

        // placementId is ignored for loading — LevelPlay loads per ad UNIT, not per placement.
        public void Load(string placementId)
        {
            if (_ad == null) { if (_initialized) CreateAd(); return; }
            if (!_ad.IsAdReady()) _ad.LoadAd();
        }

        public void Show(string placementId, Action<bool> onResult)
        {
            if (_ad == null || !_ad.IsAdReady())
            {
                onResult?.Invoke(false);
                Load(placementId);
                return;
            }
            _onShowResult = onResult;
            _rewardedThisShow = false;
            if (string.IsNullOrEmpty(placementId)) _ad.ShowAd();
            else _ad.ShowAd(placementName: placementId);
        }

        // Resolve the pending show once, then reload. Reward is decided by this bool (AdsService grants the
        // coins/continue). OnAdRewarded can arrive just after OnAdClosed; reading _rewardedThisShow at close
        // covers the common ordering.
        void Resolve(bool earned)
        {
            var cb = _onShowResult;
            _onShowResult = null;
            cb?.Invoke(earned);
            _ad?.LoadAd();
        }
    }
}
#endif
