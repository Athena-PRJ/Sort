using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Keeps a world-space SpriteRenderer background ALWAYS filling the camera's view — even when the camera
    /// is tilted (e.g. by <see cref="BoardViewAngle"/>). Each LateUpdate it parks the sprite in front of the
    /// camera at <see cref="distance"/>, facing it, and scales it to cover the frustum (plus a little
    /// overscan). Because it follows the camera, no tilt angle can reveal its edges.
    ///
    /// Attach to a background SpriteRenderer. Put it BEHIND the board: set the SpriteRenderer's Sorting Order
    /// low (e.g. -100) and Distance beyond the board. Pairs with GrayscaleTint for value-preserving recolor.
    /// (If your background is a UI Image instead, the simpler fix is Canvas → Screen Space - Camera with a
    /// large Plane Distance — that auto-fills at any angle with no script.)
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class CameraFillBackground : MonoBehaviour
    {
        [Tooltip("Camera to fill. Auto-uses Camera.main if null.")]
        [SerializeField] private Camera cam;

        [Tooltip("How far in front of the camera to place the backdrop. Must be BEYOND the board so the board " +
                 "renders in front of it.")]
        [SerializeField] private float distance = 40f;

        [Tooltip("Multiplier on the computed fill size so the edges never peek in (1.05–1.2). Higher = safer " +
                 "margin, more of the art cropped.")]
        [SerializeField] private float overscan = 1.1f;

        SpriteRenderer sr;
        Vector3 lastPos; Quaternion lastRot; float lastFov, lastAspect; bool fitted;

        void OnEnable() { sr = GetComponent<SpriteRenderer>(); fitted = false; Fit(); }

        void LateUpdate()
        {
            // Only recompute when the camera actually moved / changed (e.g. it's static after BoardViewAngle
            // sets the angle) — so this is ~free every frame instead of re-fitting the backdrop constantly.
            var c = cam != null ? cam : Camera.main;
            if (c == null) return;
            var t = c.transform;
            if (fitted && t.position == lastPos && t.rotation == lastRot
                && Mathf.Approximately(c.fieldOfView, lastFov) && Mathf.Approximately(c.aspect, lastAspect))
                return;
            Fit();
            lastPos = t.position; lastRot = t.rotation; lastFov = c.fieldOfView; lastAspect = c.aspect; fitted = true;
        }

        void Fit()
        {
            var c = cam != null ? cam : Camera.main;
            if (c == null) return;
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return;

            // Park in front of the camera, facing it.
            transform.position = c.transform.position + c.transform.forward * distance;
            transform.rotation = c.transform.rotation;

            // Frustum size at that distance.
            float h = c.orthographic
                ? c.orthographicSize * 2f
                : 2f * distance * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * c.aspect;

            // Scale the sprite (its own world size = bounds.size) to cover it, plus overscan.
            Vector2 sp = sr.sprite.bounds.size;
            float sx = Mathf.Abs(sp.x) < 1e-4f ? 1f : sp.x;
            float sy = Mathf.Abs(sp.y) < 1e-4f ? 1f : sp.y;
            transform.localScale = new Vector3((w / sx) * overscan, (h / sy) * overscan, 1f);
        }
    }
}
