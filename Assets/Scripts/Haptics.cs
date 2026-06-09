using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Lightweight haptic feedback for mobile. <see cref="LightTap"/> fires a short, gentle buzz on
    /// every column tap (wired from <see cref="PlayerHand.OnPieceTapped"/>). The player can turn it off
    /// in Settings — the on/off state persists in PlayerPrefs via <see cref="Enabled"/>.
    ///
    /// Platform notes:
    ///   • Android: a ~18 ms one-shot at low amplitude through the system Vibrator (VibrationEffect on
    ///     API 26+, legacy long-vibrate API otherwise). Wrapped in try/catch so a missing service or a
    ///     locked-down device never throws into gameplay.
    ///   • iOS / others: no-op for now (the built-in Handheld.Vibrate is a long, heavy buzz that feels
    ///     wrong for a per-tap tick — leave a TODO to add UIImpactFeedbackGenerator via a plugin later).
    ///   • Editor: no-op (there's no device to buzz), so testing in Play mode is unaffected.
    /// </summary>
    public static class Haptics
    {
        const string PrefKey = "Sort_HapticsEnabled";
        static bool? cachedEnabled;

        /// <summary>Whether haptics are on. Persisted in PlayerPrefs; defaults to ON. Bind a Settings toggle to this.</summary>
        public static bool Enabled
        {
            get
            {
                if (cachedEnabled == null) cachedEnabled = PlayerPrefs.GetInt(PrefKey, 1) != 0;
                return cachedEnabled.Value;
            }
            set
            {
                cachedEnabled = value;
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>A short, light tap buzz. Safe to call on any platform — no-op if disabled / unsupported.</summary>
        public static void LightTap()
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidVibrate(18, 60); // ~18 ms, low amplitude (1-255) → a subtle tick, not a full buzz
#elif UNITY_EDITOR
            // No device to buzz in the Editor — log so you can VERIFY the call fires and respects the
            // on/off setting: tap a column with haptics ON → a line appears each tap; toggle OFF → silence.
            Debug.Log("[Haptics] LightTap (enabled) — would vibrate on a device.");
#endif
            // iOS / other platforms: intentionally no-op (see class summary).
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _vibrator;
        static int _sdkInt = -1;

        static AndroidJavaObject Vibrator
        {
            get
            {
                if (_vibrator != null) return _vibrator;
                try
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                        _sdkInt = version.GetStatic<int>("SDK_INT");
                }
                catch { _vibrator = null; }
                return _vibrator;
            }
        }

        static void AndroidVibrate(long milliseconds, int amplitude)
        {
            try
            {
                var v = Vibrator;
                if (v == null) return;
                if (!v.Call<bool>("hasVibrator")) return;

                if (_sdkInt >= 26)
                {
                    using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                    {
                        int amp = Mathf.Clamp(amplitude, 1, 255);
                        var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amp);
                        v.Call("vibrate", effect);
                    }
                }
                else
                {
                    v.Call("vibrate", milliseconds); // legacy API (<26): duration only
                }
            }
            catch { /* never let a haptic failure reach gameplay */ }
        }
#endif
    }
}
