using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Lock bond visual: a "bolt" that clamps tied pieces across adjacent columns. Spatial sibling of
    /// <see cref="TieVisual"/>: identical bind/follow/break MECHANIC, only the look differs.
    ///
    /// • 2 columns: positioned at the two pieces' midpoint, oriented to the board tilt, uniformly scaled
    ///   via LevelLoader's Lock Visual Scale. The middle bar is HIDDEN — use a 2-column prefab whose caps
    ///   are authored however you like (e.g. pulled close + scaled up).
    /// • 3+ columns (span): the two END caps are pinned onto the first and last pieces and ONE middle bar
    ///   is TILED onto each interior column (span − 2 mids: span 3 → 1 mid, span 4 → 2 mids, …), so the
    ///   screws stay evenly spaced like the reference plate — no stretching of a single mid. Author a
    ///   SEPARATE span prefab (start + one mid template + end) and assign it to LevelLoader's Lock Visual
    ///   Span Prefab.
    ///
    /// Each LateUpdate: position (midpoint of end pieces + offset + camera push) and rotation (column
    /// board-tilt frame + baked 90° to lie horizontal + tunable offset). Drawn on top (ZTest Always).
    /// On break the caps split apart + fade.
    /// </summary>
    public class LockVisual : MonoBehaviour, IBondVisual
    {
        [Header("Parts")]
        [Tooltip("End cap pinned onto the FIRST piece (lock_start). On break it slides outward.")]
        [SerializeField] private Transform startCap;
        [Tooltip("Middle-bar TEMPLATE (lock_mid). For a 3+ column span it is tiled once per interior column; " +
                 "hidden for a 2-column lock.")]
        [SerializeField] private Transform middleBar;
        [Tooltip("End cap pinned onto the LAST piece (lock_end). On break it slides outward.")]
        [SerializeField] private Transform endCap;

        [Header("Orientation")]
        [Tooltip("Adopt the column's world rotation so the bar tilts in the same 3D frame as the pieces. " +
                 "Turn OFF to billboard toward the camera.")]
        [SerializeField] private bool matchBoardTilt = true;
        [Tooltip("Rotation applied after the base orient (which already swings the bar 90° horizontal).")]
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;

        [Header("Position")]
        [Tooltip("Offset from the end pieces, in the COLUMN's frame (Y rides the board surface 'up').")]
        [SerializeField] private Vector3 positionOffset = Vector3.zero;
        [Tooltip("Push toward the camera (world units) so the lock sits in front of the pieces.")]
        [SerializeField] private float cameraOffset = 0.8f;

        [Header("Render")]
        [Tooltip("Draw the lock ON TOP of the pieces (ZTest Always + high queue) if the material exposes _ZTest.")]
        [SerializeField] private bool renderOnTop = true;
        [SerializeField] private int onTopRenderQueue = 4000;

        [Header("Break (one-shot when the lock breaks)")]
        [Tooltip("How far each cap slides apart along the bar axis during the break, in the root's LOCAL units.")]
        [SerializeField] private float breakSeparation = 0.4f;
        [Tooltip("Fade all parts to transparent over the break, then destroy (needs a Transparent material).")]
        [SerializeField] private bool breakFade = true;

        readonly List<Piece> pieces = new List<Piece>();
        readonly List<Transform> midInstances = new List<Transform>();   // [0] = template, rest = clones
        Vector3 extraOffset;   // per-spawn nudge from LevelLoader (column frame), added to positionOffset
        bool breaking;
        MaterialPropertyBlock fadeMpb;
        Vector3 midMeshSize = Vector3.one;

        public IReadOnlyList<Piece> Pieces => pieces;
        public bool Covers(Piece p) => p != null && pieces.Contains(p);

        /// <summary>Per-spawn position nudge (column frame, Y = along the board surface 'up'), set by
        /// LevelLoader. Added on top of the prefab's own Position Offset.</summary>
        public void SetExtraOffset(Vector3 o) => extraOffset = o;

        public void Bind(IReadOnlyList<Piece> bound)
        {
            pieces.Clear();
            if (bound != null) for (int i = 0; i < bound.Count; i++) if (bound[i] != null) pieces.Add(bound[i]);

            SetupMids();
            ApplyOnTop();   // after clones exist so they render on top too
            UpdateTransform();
        }

        // Cached so UpdateTransform (Camera.main + InverseTransformPoint per part) only re-runs while a
        // piece or the camera actually moved — static between drops/shifts, so most frames early-out.
        Vector3 lastFirst, lastLast, lastCamPos;
        Quaternion lastCamRot;
        bool hasLastTransform;

        void LateUpdate()
        {
            if (breaking) return;
            if (pieces.Count == 0) { Destroy(gameObject); return; }
            for (int i = 0; i < pieces.Count; i++) if (pieces[i] == null) { Destroy(gameObject); return; }

            // The end pieces drive position+rotation; interior pieces shift in lockstep with them, so the
            // endpoints are a reliable "did anything move" signal.
            Vector3 f = pieces[0].transform.position, l = pieces[pieces.Count - 1].transform.position;
            Camera cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
            Quaternion cr = cam != null ? cam.transform.rotation : Quaternion.identity;
            if (hasLastTransform && f == lastFirst && l == lastLast && cp == lastCamPos && cr == lastCamRot)
                return;

            UpdateTransform();
            lastFirst = f; lastLast = l; lastCamPos = cp; lastCamRot = cr; hasLastTransform = true;
        }

        // Builds one mid per interior column (span − 2). Clones the assigned middleBar template; hides it
        // entirely for a 2-column lock.
        void SetupMids()
        {
            for (int i = midInstances.Count - 1; i >= 1; i--)
                if (midInstances[i] != null) Destroy(midInstances[i].gameObject);
            midInstances.Clear();

            if (middleBar == null) return;
            int want = Mathf.Max(0, pieces.Count - 2);
            if (want == 0) { middleBar.gameObject.SetActive(false); return; }

            var mf = middleBar.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) midMeshSize = mf.sharedMesh.bounds.size;

            middleBar.gameObject.SetActive(true);
            midInstances.Add(middleBar);
            for (int m = 1; m < want; m++)
            {
                var clone = Instantiate(middleBar.gameObject, transform).transform;
                clone.name = middleBar.name + " (mid " + m + ")";
                midInstances.Add(clone);
            }
        }

        void UpdateTransform()
        {
            int n = pieces.Count;
            Vector3 firstW = pieces[0].transform.position;
            Vector3 lastW = pieces[n - 1].transform.position;
            Vector3 rawMid = (firstW + lastW) * 0.5f;

            Transform colA = pieces[0].transform.parent;
            Quaternion colRot = colA != null ? colA.rotation : pieces[0].transform.rotation;

            Vector3 toCamDir = Vector3.back;
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - rawMid;
                float d = toCam.magnitude;
                if (d > 1e-4f) toCamDir = toCam / d;
            }
            Vector3 endShift = toCamDir * cameraOffset + colRot * (positionOffset + extraOffset);

            transform.position = rawMid + endShift;

            if (matchBoardTilt)
                transform.rotation = colRot * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(rotationOffset);
            else if (cam != null)
                transform.rotation = Quaternion.LookRotation(toCamDir, cam.transform.up) * Quaternion.Euler(rotationOffset);

            if (n < 3) return;   // 2-column lock keeps the prefab's authored part layout

            // Pin caps onto the end pieces; tile one mid onto each interior column.
            Vector3 lFirst = LocalOf(pieces[0], endShift);
            Vector3 lLast = LocalOf(pieces[n - 1], endShift);
            if (startCap != null) startCap.localPosition = lFirst;
            if (endCap != null) endCap.localPosition = lLast;

            Vector3 step = LocalOf(pieces[1], endShift) - lFirst;   // one column, in local units
            int axis = DominantAxis(step);
            float spacing = step.magnitude;
            float meshLen = Mathf.Abs(GetComp(midMeshSize, axis));

            for (int k = 0; k < midInstances.Count; k++)
            {
                int pieceIdx = k + 1;                 // interior pieces are 1 .. n-2
                if (pieceIdx > n - 2) break;
                var mid = midInstances[k];
                if (mid == null) continue;
                mid.localPosition = LocalOf(pieces[pieceIdx], endShift);
                if (meshLen > 1e-4f)
                {
                    Vector3 s = mid.localScale;
                    SetComp(ref s, axis, spacing / meshLen);
                    mid.localScale = s;
                }
            }
        }

        Vector3 LocalOf(Piece p, Vector3 endShift) => transform.InverseTransformPoint(p.transform.position + endShift);

        /// <summary><see cref="IBondVisual"/> break: freeze, slide the caps apart along the bar axis while
        /// fading, then destroy. Caller clears the whole group's tie refs.</summary>
        public IEnumerator PlayBreak(float duration)
        {
            breaking = true;

            Vector3 sepDir = Vector3.right;
            if (pieces.Count >= 2 && pieces[0] != null && pieces[pieces.Count - 1] != null)
            {
                Vector3 d = transform.InverseTransformPoint(pieces[pieces.Count - 1].transform.position)
                          - transform.InverseTransformPoint(pieces[0].transform.position);
                if (d.sqrMagnitude > 1e-6f) sepDir = d.normalized;
            }

            Vector3 startHome = startCap != null ? startCap.localPosition : Vector3.zero;
            Vector3 endHome = endCap != null ? endCap.localPosition : Vector3.zero;
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            if (fadeMpb == null) fadeMpb = new MaterialPropertyBlock();

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / Mathf.Max(1e-4f, duration));
                if (startCap != null) startCap.localPosition = startHome - sepDir * (breakSeparation * u);
                if (endCap != null) endCap.localPosition = endHome + sepDir * (breakSeparation * u);
                if (breakFade)
                {
                    float alpha = 1f - u;
                    for (int i = 0; i < renderers.Length; i++) SetAlpha(renderers[i], alpha);
                }
                yield return null;
            }

            Destroy(gameObject);
        }

        // ---- helpers ----

        void ApplyOnTop()
        {
            if (!renderOnTop) return;
            var rends = GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in rends)
            {
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_ZTest")) m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    m.renderQueue = onTopRenderQueue;
                }
            }
        }

        static int DominantAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return 0;
            return ay >= az ? 1 : 2;
        }

        static float GetComp(Vector3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;
        static void SetComp(ref Vector3 v, int axis, float val) { if (axis == 0) v.x = val; else if (axis == 1) v.y = val; else v.z = val; }

        void SetAlpha(MeshRenderer r, float a)
        {
            if (r == null) return;
            r.GetPropertyBlock(fadeMpb);
            fadeMpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, a));
            r.SetPropertyBlock(fadeMpb);
        }
    }
}
