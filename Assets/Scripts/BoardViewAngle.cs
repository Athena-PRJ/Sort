using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Tilts the CAMERA to look at the board from slightly above-front, so the 3D pieces show their
    /// side / bottom edges (a 2.5D look) instead of reading as flat top-down tiles. It ORBITS the camera
    /// around the board's centre by <see cref="pitchDegrees"/>, keeping the same distance — so the board
    /// stays framed and every world object (pieces, indicators, frozen overlays, held piece) keeps its
    /// relationship; only the viewing angle changes, revealing the meshes' depth.
    ///
    /// Attach to the Main Camera. Set Target to the board (auto-finds the <see cref="Board"/> if left null).
    /// Tune Pitch live. Works in the Editor too (ExecuteAlways) for instant preview. Use the context-menu
    /// "Capture Base Pose" if you move/re-author the camera, and "Reset To Base" to undo the tilt.
    ///
    /// NOTE: the board auto-fit (LevelLoader) was tuned for the head-on angle, so after picking a pitch you
    /// may want to nudge the board framing (boardCameraPush / fit). The Screen-Space HUD is unaffected.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class BoardViewAngle : MonoBehaviour
    {
        [Tooltip("What to orbit around / keep centred — the board. Auto-finds the Board in the scene if null.")]
        [SerializeField] private Transform target;

        [Tooltip("Downward look angle (degrees). Small values (8–18) reveal the pieces' bottom/side faces " +
                 "like the reference. + tilts one way, - the other — flip the sign if it tilts up.")]
        [Range(-45f, 45f)] [SerializeField] private float pitchDegrees = 12f;

        [Tooltip("Extra orbit around the vertical axis (yaw), if you also want a slight 3/4 side view. " +
                 "Usually 0 for this game.")]
        [Range(-45f, 45f)] [SerializeField] private float yawDegrees = 0f;

        // The authored (un-tilted) camera pose, captured once so the tilt is always applied relative to it
        // (re-applying never drifts). Stored serialized so it survives domain reloads / scene saves.
        [SerializeField, HideInInspector] private Vector3 basePos;
        [SerializeField, HideInInspector] private Quaternion baseRot = Quaternion.identity;
        [SerializeField, HideInInspector] private bool hasBase;

        void OnEnable()
        {
            if (!hasBase) CaptureBase();
            Apply();
        }

        void OnValidate() { if (hasBase) Apply(); }

        [ContextMenu("Capture Base Pose")]
        void CaptureBase()
        {
            basePos = transform.position;
            baseRot = transform.rotation;
            hasBase = true;
        }

        [ContextMenu("Reset To Base")]
        void ResetToBase()
        {
            if (!hasBase) return;
            transform.SetPositionAndRotation(basePos, baseRot);
        }

        void Apply()
        {
            if (!hasBase) return;
            var t = target != null ? target : (FindAnyObjectByType<Board>()?.transform);
            if (t == null) return;

            Vector3 pivot = t.position;
            Vector3 right = baseRot * Vector3.right;     // camera's own right at the base pose
            Quaternion q = Quaternion.AngleAxis(pitchDegrees, right) * Quaternion.AngleAxis(yawDegrees, Vector3.up);

            transform.position = pivot + q * (basePos - pivot);   // orbit the base position around the board
            transform.rotation = q * baseRot;                     // and pitch the look with it
        }
    }
}
