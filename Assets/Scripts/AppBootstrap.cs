using UnityEngine;

namespace Sort
{
    /// <summary>
    /// App-wide startup config. Most importantly: forces 60 FPS. Android caps the frame rate at the
    /// display's default swap interval (often 30 FPS) UNLESS you set <see cref="Application.targetFrameRate"/>
    /// explicitly, and vSyncCount must be 0 for targetFrameRate to take effect. Runs automatically before
    /// the first scene loads — no GameObject required.
    /// </summary>
    public static class AppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            // vSync OFF so targetFrameRate is honored (otherwise the frame rate is locked to the display).
            QualitySettings.vSyncCount = 0;
            // Target 60 FPS. On a 60 Hz screen this runs at 60; on 90/120 Hz screens it caps at 60.
            Application.targetFrameRate = 60;
        }
    }
}
