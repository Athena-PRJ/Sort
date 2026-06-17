using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Lets the held piece's WORLD anchor sit visually on top of a UI placemat that lives INSIDE the
    /// Canvas. Put this on the world-space HandAnchor (the transform PlayerHand parents the held piece to)
    /// and point it at the UI <see cref="uiTarget"/> (e.g. PlayerHandPlace). Each frame it projects the
    /// UI element's screen position into the world at <see cref="distanceFromCamera"/> and moves this
    /// transform there — so the 3D held piece overlays the 2D placemat without putting the 3D mesh under
    /// the Canvas (which would break its scale / sorting).
    ///
    /// Setup:
    ///   • uiTarget       = the PlayerHandPlace RectTransform (in the Canvas).
    ///   • worldCamera    = the camera that renders the 3D board/pieces (usually Main Camera).
    ///   • uiCamera       = the Canvas's render camera for Screen Space - Camera; LEAVE NULL for Overlay.
    ///   • distanceFromCamera = world depth → controls the held piece's apparent SIZE. Tune it until the
    ///     held piece matches the size of the pieces on the board.
    /// </summary>
    [ExecuteAlways]
    public class HandAnchorFollowUI : MonoBehaviour
    {
        [Tooltip("The UI placemat (RectTransform) inside the Canvas that the held piece should sit on.")]
        [SerializeField] private RectTransform uiTarget;

        [Tooltip("Camera that renders the 3D board / held piece. If null, uses Camera.main.")]
        [SerializeField] private Camera worldCamera;

        [Tooltip("The Canvas's render camera — set this only if the Canvas is Screen Space - Camera. " +
                 "Leave NULL when the Canvas is Screen Space - Overlay.")]
        [SerializeField] private Camera uiCamera;

        [Tooltip("World distance from the camera at which to place the anchor. Controls the held piece's " +
                 "apparent SIZE (closer = bigger). Tune until it matches the board pieces.")]
        [SerializeField] private float distanceFromCamera = 10f;

        void LateUpdate()
        {
            if (uiTarget == null) return;
            var wc = worldCamera != null ? worldCamera : Camera.main;
            if (wc == null) return;

            // UI element → screen point (uiCamera = null is correct for Overlay canvases).
            Vector3 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, uiTarget.position);
            screen.z = distanceFromCamera;
            transform.position = wc.ScreenToWorldPoint(screen);
        }
    }
}
