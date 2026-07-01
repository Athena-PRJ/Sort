using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Sort
{
    /// <summary>
    /// On-screen performance + thermal HUD for playtesting on device:
    ///   • FPS (current + session min) and frame time in ms vs the target budget (60 FPS = 16.7 ms).
    ///   • Memory: total allocated + Mono/GC heap (MB) — watch for steady growth (a leak) over a session.
    ///   • Battery %, battery temperature (°C) and Android thermal status — the real "is it overheating /
    ///     throttling after a while" signals. Frame time creeping up while load is constant = thermal throttle.
    ///
    /// Auto-spawns once at launch (DontDestroyOnLoad) so it shows on a real APK without any scene wiring.
    /// Toggle with a 3-finger tap (device) or F1 (editor/desktop). The HUD only READS stats — it never
    /// changes the game. ⚠️ Set <see cref="AUTO_SPAWN"/> = false (or delete this file) before a final release.
    /// </summary>
    public class PerfHud : MonoBehaviour
    {
        // Flip to false (or strip this file) before shipping a public release build.
        const bool AUTO_SPAWN = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (!AUTO_SPAWN) return;
            if (FindFirstObjectByType<PerfHud>() != null) return;
            var go = new GameObject("[PerfHud]");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfHud>();
        }

        [Tooltip("Per-frame budget in ms the FPS/frame line is graded against. 16.67 = 60 FPS, 33.3 = 30 FPS.")]
        [SerializeField] float targetFrameMs = 1000f / 60f;
        [Tooltip("How often (s) the FPS / frame-time readout refreshes.")]
        [SerializeField] float sampleInterval = 0.5f;
        [Tooltip("How often (s) to poll battery/thermal (the Android JNI calls are not free — keep this ≥1s).")]
        [SerializeField] float thermalPollInterval = 2f;

        // Hidden by default — the player never sees it. Toggle with F1 (editor/desktop) or a 3-finger tap (device).
        public static bool Visible = false;

        // --- FPS / frame-time sampling ---
        int frames;
        float accumTime;
        float fps;
        float frameMs;
        float worstMs;
        float minFps = float.MaxValue;

        // --- battery / thermal (polled) ---
        float thermalTimer;
        string thermalStatus = "?";
        float batteryTempC = -1f;
        float batteryLevel = -1f;
        float startTime;

        // --- cached display (rebuilt on sample tick so OnGUI does no per-frame string GC) ---
        string hudText = "";
        Color healthColor = Color.green;
        GUIStyle style;
        bool threeFingerLatch;

        void Start()
        {
            startTime = Time.realtimeSinceStartup;
            PollThermal();
            Rebuild();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;   // unscaled so pause / timeScale don't skew it
            accumTime += dt;
            frames++;
            float ms = dt * 1000f;
            if (ms > worstMs) worstMs = ms;

            if (accumTime >= sampleInterval)
            {
                fps = frames / accumTime;
                frameMs = (accumTime / frames) * 1000f;
                if (fps < minFps) minFps = fps;
                frames = 0;
                accumTime = 0f;
                Rebuild();
            }

            thermalTimer += dt;
            if (thermalTimer >= thermalPollInterval) { thermalTimer = 0f; PollThermal(); }

            HandleToggle();
        }

        void HandleToggle()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) Visible = !Visible;

            var ts = Touchscreen.current;
            if (ts != null)
            {
                int active = 0;
                var touches = ts.touches;
                for (int i = 0; i < touches.Count; i++) if (touches[i].press.isPressed) active++;
                if (active >= 3 && !threeFingerLatch) { Visible = !Visible; threeFingerLatch = true; }
                if (active < 3) threeFingerLatch = false;
            }
#endif
        }

        void Rebuild()
        {
            long totalMB = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
            long monoMB  = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);

            var sb = new StringBuilder(256);
            sb.Append("FPS ").Append(Mathf.RoundToInt(fps))
              .Append("  (min ").Append(minFps < float.MaxValue ? Mathf.RoundToInt(minFps) : 0).Append(")\n");
            sb.Append("Frame ").Append(frameMs.ToString("0.0")).Append(" ms  (target ")
              .Append(targetFrameMs.ToString("0.0")).Append(", worst ").Append(worstMs.ToString("0.0")).Append(")\n");
            sb.Append("Mem ").Append(totalMB).Append(" MB   GC ").Append(monoMB).Append(" MB\n");
            sb.Append("Battery ").Append(batteryLevel >= 0 ? Mathf.RoundToInt(batteryLevel * 100) : -1).Append("%");
            sb.Append("   Temp ").Append(batteryTempC > 0 ? batteryTempC.ToString("0.0") + "C" : "n/a").Append("\n");
            sb.Append("Thermal ").Append(thermalStatus).Append("\n");
            sb.Append("Uptime ").Append(FormatTime(Time.realtimeSinceStartup - startTime));
            hudText = sb.ToString();

            // Overall health colour = worst of frame-time / temp / thermal.
            healthColor = Color.green;
            if (frameMs > targetFrameMs * 1.25f) healthColor = Color.red;
            else if (frameMs > targetFrameMs * 1.05f) healthColor = Color.yellow;
            if (batteryTempC > 43f || IsThermalBad()) healthColor = Color.red;
            else if (batteryTempC > 38f) healthColor = (healthColor == Color.red) ? Color.red : Color.yellow;
        }

        bool IsThermalBad()
        {
            // NONE / LIGHT are fine; MODERATE and above mean throttling/overheating.
            return thermalStatus == "MODERATE" || thermalStatus == "SEVERE" ||
                   thermalStatus == "CRITICAL" || thermalStatus == "EMERGENCY" || thermalStatus == "SHUTDOWN";
        }

        void PollThermal()
        {
            batteryLevel = SystemInfo.batteryLevel;   // -1 if the platform doesn't report it

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // Thermal status (PowerManager.getCurrentThermalStatus, API 29+).
                    try
                    {
                        using (var pm = act.Call<AndroidJavaObject>("getSystemService", "power"))
                            thermalStatus = ThermalName(pm.Call<int>("getCurrentThermalStatus"));
                    }
                    catch { thermalStatus = "n/a (API<29)"; }

                    // Battery temperature via the sticky ACTION_BATTERY_CHANGED intent (tenths of °C).
                    try
                    {
                        using (var filter = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED"))
                        using (var intent = act.Call<AndroidJavaObject>("registerReceiver", null, filter))
                        {
                            int t = intent.Call<int>("getIntExtra", "temperature", -1);
                            batteryTempC = t > 0 ? t / 10f : -1f;
                        }
                    }
                    catch { }
                }
            }
            catch { }
#else
            thermalStatus = Application.isEditor ? "editor" : "n/a";
#endif
        }

        static string ThermalName(int s)
        {
            switch (s)
            {
                case 0: return "NONE";
                case 1: return "LIGHT";
                case 2: return "MODERATE";
                case 3: return "SEVERE";
                case 4: return "CRITICAL";
                case 5: return "EMERGENCY";
                case 6: return "SHUTDOWN";
                default: return "?";
            }
        }

        static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 60).ToString("0") + ":" + (s % 60).ToString("00");
        }

        void OnGUI()
        {
            if (!Visible) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.022f)),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft,
                    richText = false
                };
            }

            float margin = Screen.height * 0.012f;
            var size = style.CalcSize(new GUIContent(hudText));
            // CalcSize underestimates multi-line height; recompute from line count.
            int lineCount = 6;
            float h = (style.fontSize + 4) * lineCount + margin;
            float w = Mathf.Max(size.x, Screen.width * 0.42f) + margin * 2;
            var rect = new Rect(margin, margin, w, h);

            Color bg = new Color(0f, 0f, 0f, 0.55f);
            DrawQuad(rect, bg);

            var prev = style.normal.textColor;
            style.normal.textColor = healthColor;
            GUI.Label(new Rect(rect.x + margin, rect.y + margin * 0.5f, rect.width, rect.height), hudText, style);
            style.normal.textColor = prev;
        }

        static Texture2D _px;
        static void DrawQuad(Rect r, Color c)
        {
            if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply(); }
            var gc = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _px);
            GUI.color = gc;
        }
    }
}
