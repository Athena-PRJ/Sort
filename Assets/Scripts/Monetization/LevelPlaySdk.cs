// Shared LevelPlay SDK initialization. LevelPlay.Init is global (one call per app), but both the rewarded
// and interstitial providers need it ready before creating ad objects — so they funnel through this helper
// which calls Init exactly once and notifies all waiters. Compiled only with SORT_ADS_LEVELPLAY.
#if SORT_ADS_LEVELPLAY
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Services.LevelPlay;

namespace Sort.Monetization
{
    internal static class LevelPlaySdk
    {
        static bool _started;
        static bool _done;
        static bool _succeeded;
        static readonly List<Action<bool>> _waiters = new List<Action<bool>>();

        public static bool IsReady => _done && _succeeded;

        /// <summary>Initialize once with <paramref name="appKey"/>; <paramref name="onDone"/> fires with the
        /// init result (true=success). Safe to call from multiple providers — Init runs only the first time.</summary>
        public static void EnsureInit(string appKey, Action<bool> onDone)
        {
            if (_done) { onDone?.Invoke(_succeeded); return; }
            if (onDone != null) _waiters.Add(onDone);
            if (_started) return;

            _started = true;
            LevelPlay.OnInitSuccess += OnInitSuccess;
            LevelPlay.OnInitFailed  += OnInitFailed;
            LevelPlay.Init(appKey);
        }

        static void OnInitSuccess(LevelPlayConfiguration config) => Finish(true);

        static void OnInitFailed(LevelPlayInitError error)
        {
            Debug.LogWarning($"[AdsService] LevelPlay init failed: {error}");
            Finish(false);
        }

        static void Finish(bool succeeded)
        {
            _done = true;
            _succeeded = succeeded;
            var waiters = _waiters.ToArray();
            _waiters.Clear();
            for (int i = 0; i < waiters.Length; i++) waiters[i]?.Invoke(succeeded);
        }
    }
}
#endif
