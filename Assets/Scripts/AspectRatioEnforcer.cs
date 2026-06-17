using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Forces this camera to render at a FIXED target aspect ratio (default 1408:3080 — the design
    /// resolution used in the editor Game view) on EVERY device, by letterboxing / pillarboxing.
    ///
    /// How: the camera's normalized viewport <see cref="Camera.rect"/> is shrunk to the largest CENTERED
    /// rectangle that has the target aspect; the leftover screen edges become bars. So the world (board,
    /// columns, pieces) is framed EXACTLY the same regardless of the device's real screen aspect — what you
    /// see in the editor at 1408x3080 is what ships. This fixes "board off-screen / displaced on device".
    ///
    /// A tiny secondary camera (auto-created at runtime, depth-1, draws nothing) clears the WHOLE screen to
    /// <see cref="barColor"/> first, so the bars are a clean color instead of leftover framebuffer garbage.
    ///
    /// NOTE on UI: a Screen Space - Overlay Canvas always spans the FULL physical screen (Unity draws it
    /// over the camera output), so HUD/skill-bar are NOT letterboxed by this. They adapt via their anchors.
    /// If you also want the UI pinned to the same 1408:3080 frame, parent gameplay UI under a RectTransform
    /// sized to the letterbox rect (ask and I'll add a matching UI 'safe frame' helper).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AspectRatioEnforcer : MonoBehaviour
    {
        [Tooltip("Target aspect = Target Width / Target Height. Default 1408 / 3080 matches the editor design resolution.")]
        [SerializeField] private float targetWidth = 1408f;
        [SerializeField] private float targetHeight = 3080f;

        [Tooltip("Color of the letterbox/pillarbox bars (area outside the target-aspect viewport). Alpha 0 = no bar camera.")]
        [SerializeField] private Color barColor = Color.black;

        [Tooltip("Fixed VERTICAL field of view, in degrees. The camera is forced to NON-physical so this FOV " +
                 "stays constant on every aspect — otherwise a Physical Camera + Gate Fit recomputes the FOV " +
                 "per aspect and the board gets cropped out of frame (the device bug). 27 matches the design " +
                 "look at 1408x3080; tune if the board sits too big/small.")]
        [SerializeField] private float verticalFieldOfView = 27f;

        Camera cam;
        Camera barCam;
        int lastW, lastH;
        float lastTarget;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            Apply();
        }

        void OnDisable()
        {
            // Restore full viewport so disabling the component doesn't leave the camera letterboxed.
            if (cam != null) cam.rect = new Rect(0f, 0f, 1f, 1f);
            if (barCam != null) Destroy(barCam.gameObject);
        }

        void Update()
        {
            // Re-apply only when the screen size or target changes (orientation change, resolution switch).
            float target = SafeTarget();
            if (Screen.width != lastW || Screen.height != lastH || !Mathf.Approximately(target, lastTarget))
                Apply();
        }

        float SafeTarget()
        {
            if (targetWidth <= 0f || targetHeight <= 0f) return 0.4571f; // 1408/3080 fallback
            return targetWidth / targetHeight;
        }

        void Apply()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;

            lastW = Screen.width;
            lastH = Screen.height;
            lastTarget = SafeTarget();

            // Force a NON-physical camera with a FIXED vertical FOV. The scene camera is a Physical Camera
            // with Gate Fit = Fill, which RECOMPUTES the field of view per aspect ratio — so on a device
            // whose aspect differs from the editor, the vertical FOV changes and the board is cropped out of
            // view (the reported bug). A plain vertical FOV is aspect-invariant: the board frames identically
            // on every screen, and the letterbox below trims the extra to lock the exact 1408:3080 look.
            if (cam.orthographic == false)
            {
                cam.usePhysicalProperties = false;
                if (verticalFieldOfView > 0f) cam.fieldOfView = verticalFieldOfView;
            }

            float target = lastTarget;
            float window = (Screen.height <= 0) ? target : (float)Screen.width / Screen.height;

            Rect r;
            if (window > target)
            {
                // Screen is WIDER than target → pillarbox (bars on the left & right).
                float w = target / window;
                r = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            else
            {
                // Screen is TALLER/narrower than target → letterbox (bars on top & bottom).
                float h = window / target;
                r = new Rect(0f, (1f - h) * 0.5f, 1f, h);
            }
            cam.rect = r;

            EnsureBarCamera();
        }

        /// <summary>Spawns (once) a depth-(cam.depth-1) camera that clears the full screen to barColor so
        /// the letterbox bars render as a clean color rather than uninitialized framebuffer contents.</summary>
        void EnsureBarCamera()
        {
            if (barColor.a <= 0f)
            {
                if (barCam != null) { Destroy(barCam.gameObject); barCam = null; }
                return;
            }
            if (barCam == null)
            {
                var go = new GameObject("LetterboxBars");
                go.transform.SetParent(transform, worldPositionStays: false);
                barCam = go.AddComponent<Camera>();
                barCam.cullingMask = 0;                       // renders no geometry — only the clear color
                barCam.clearFlags = CameraClearFlags.SolidColor;
                barCam.rect = new Rect(0f, 0f, 1f, 1f);       // ALWAYS full screen (fills the bars)
                barCam.useOcclusionCulling = false;
                barCam.allowHDR = false;
                barCam.allowMSAA = false;
            }
            barCam.depth = cam.depth - 1;                     // render BEFORE the main camera
            barCam.backgroundColor = barColor;
        }
    }
}
