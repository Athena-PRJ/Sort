using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// 3D "ice" overlay for a frozen Column, assembled from the freeze segment set as HEAD + MIDDLE + TAIL:
    ///   • HEAD  = fixed top group, placed in order (e.g. Start, MidA).
    ///   • MIDDLE = the loopable section (MidB) — either STRETCHED once or REPEATED to fill the variable gap.
    ///   • TAIL  = fixed bottom group, placed in order (e.g. MidC, End).
    /// The segments are a designed continuous sequence (Start→MidA→MidB→MidC→End) whose edges match, so
    /// placing them edge-to-edge reads as one continuous ice column. Parented to the column (inherits the
    /// board tilt) and pushed toward the camera so it covers the pieces. No mesh editing → the freeze FBX do
    /// NOT need Read/Write enabled.
    /// </summary>
    public class FrozenColumnIce : MonoBehaviour, IFrozenOverlay
    {
        public enum MiddleFill { Stretch, Repeat }

        [Header("Segments (stacking order, top → down)")]
        [Tooltip("Fixed TOP group in order — e.g. [freeze_start, freeze_midA].")]
        [SerializeField] private GameObject[] headSegments;
        [Tooltip("The LOOPABLE middle piece (freeze_midB) — stretched or repeated to fill the column height.")]
        [SerializeField] private GameObject middleSegment;
        [Tooltip("Fixed BOTTOM group in order — e.g. [freeze_midC, freeze_end].")]
        [SerializeField] private GameObject[] tailSegments;
        [Tooltip("How the middle fills the gap: Stretch = one MidB scaled along its length (smoothest); " +
                 "Repeat = tile MidB (best when MidB is authored to loop).")]
        [SerializeField] private MiddleFill middleFill = MiddleFill.Repeat;

        [Header("Material")]
        [SerializeField] private Material iceMaterial;
        [Tooltip("Render the ice ON TOP of the pieces (ZTest Always). The reliable cover is Camera Offset below.")]
        [SerializeField] private bool renderOnTop = true;

        [Header("Fit")]
        [Tooltip("Auto-scale (uniformly) so the ice WIDTH matches the column's piece width.")]
        [SerializeField] private bool autoFitWidth = true;
        [Tooltip("Extra width multiplier on top of the auto-fit (1 = exact piece width).")]
        [SerializeField] private float widthPadding = 1f;
        [Tooltip("Manual uniform scale (used as the base; combine with Auto Fit Width).")]
        [Min(0.0001f)] [SerializeField] private float manualScale = 1f;
        [Tooltip("Slightly enlarges each segment along its length so neighbours OVERLAP and the joins are " +
                 "hidden (0–0.3). Raise a touch if you see thin seams between segments.")]
        [Range(0f, 0.3f)] [SerializeField] private float seamOverlap = 0.04f;
        [Tooltip("On columns with this many rows OR FEWER, drop the inner transition pieces (the 2nd head " +
                 "segment / 1st tail segment, e.g. midA / midC) and use just the first head (Start) + middle " +
                 "+ last tail (End). Fewer pieces → they don't crush together on tiny levels. 0 = never simplify.")]
        [SerializeField] private int simplifyAtOrBelowRows = 3;

        [Header("Placement (column-local — tune live)")]
        [Tooltip("Rotation of every segment relative to the column, so the length axis stands up the column.")]
        [SerializeField] private Vector3 segmentLocalEuler = Vector3.zero;
        [Tooltip("Column-local position nudge (X sideways, Y up, Z depth).")]
        [SerializeField] private Vector3 segmentLocalOffset = Vector3.zero;
        [Tooltip("Pushes the whole ice toward the camera (world units) so it sits IN FRONT of the pieces.")]
        [SerializeField] private float cameraOffset = 0.6f;

        [Header("Count label")]
        [Tooltip("3D TextMeshPro (NOT UGUI) child showing the remaining count, rendered on top of the ice.")]
        [SerializeField] private TMP_Text thresholdLabel;
        [Tooltip("Column-local offset for the count label from the column centre.")]
        [SerializeField] private Vector3 labelLocalOffset = Vector3.zero;
        [Tooltip("Label size as a FRACTION of the column's width — so the number auto-shrinks on small " +
                 "columns and grows on big ones, always sitting inside the ice. Per-axis: raise Y for a " +
                 "TALLER number, X for wider. ~0.5 is a good start.")]
        [SerializeField] private Vector3 labelScale = new Vector3(0.5f, 0.6f, 0.5f);

        Column boundColumn;
        readonly List<GameObject> built = new List<GameObject>();
        Material onTopInstance;

        const int OVERLAY_QUEUE = 4000;

        void Awake()
        {
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        public void AttachToColumn(Column col)
        {
            if (col == null) return;
            boundColumn = col;
            Rebuild();
        }

        public void SetRemaining(int remaining)
        {
            if (thresholdLabel != null) thresholdLabel.text = Mathf.Max(0, remaining).ToString();
        }

        void Rebuild()
        {
            if (boundColumn == null) return;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale    = Vector3.one;
            Build(boundColumn);
            PushTowardCamera();
            ConfigureLabelOnTop();
        }

        void Build(Column col)
        {
            ClearBuilt();

            var pieces = new List<Transform>();
            for (int i = 0; i < col.transform.childCount; i++)
            {
                var p = col.transform.GetChild(i).GetComponent<Piece>();
                if (p != null) pieces.Add(p.transform);
            }
            if (pieces.Count == 0) return;
            pieces.Sort((a, b) => b.position.y.CompareTo(a.position.y));

            onTopInstance = (iceMaterial != null && renderOnTop) ? MakeOnTop(iceMaterial) : iceMaterial;

            int n = pieces.Count;
            Vector3 topW = pieces[0].position;
            Vector3 botW = pieces[n - 1].position;
            Vector3 up = (n > 1) ? (topW - botW).normalized : col.transform.up;
            float perPiece = (n > 1) ? Vector3.Distance(topW, botW) / (n - 1) : Extent(pieces[0].gameObject, up, 1f);
            float cover = Vector3.Distance(topW, botW) + perPiece;
            Vector3 topEdge = topW + up * (perPiece * 0.5f);
            Vector3 centreW = (topW + botW) * 0.5f;

            // Width auto-fit: derive a uniform scale from the first segment so the ice matches the column width.
            Vector3 widthDir = col.transform.right;
            float targetWidth = 0f;
            for (int i = 0; i < n; i++) targetWidth = Mathf.Max(targetWidth, Extent(pieces[i].gameObject, widthDir, 0f));
            targetWidth *= Mathf.Max(0.01f, widthPadding);

            float widthScale = manualScale;
            GameObject firstPrefab = First(headSegments) ?? middleSegment ?? First(tailSegments);
            if (autoFitWidth && firstPrefab != null)
            {
                var probe = Spawn(firstPrefab, manualScale);
                float baseW = Extent(probe, widthDir, 0f);
                if (baseW > 1e-4f && targetWidth > 1e-4f) widthScale = manualScale * (targetWidth / baseW);
                built.Remove(probe); Destroy(probe);
            }

            float overlapBoost = 1f + Mathf.Clamp01(seamOverlap);

            // Spawn segments at their NATURAL size and measure each along the column, so their PROPORTIONS
            // are preserved (a start cap stays cap-shaped, a mid stays mid-shaped). We then fit the WHOLE
            // assembly to the column:
            //   • column TALLER than the natural assembly → keep caps natural, stretch only the MIDDLE.
            //   • column SHORTER → scale the WHOLE assembly down uniformly (a small, smooth column) instead
            //     of cramming 5 equal tiny blocks (the bug on short 2×3 levels).
            // Collect head/tail prefabs. On very short columns, drop the inner transition pieces (keep only
            // the first head = Start and the last tail = End) so few segments don't crush together.
            var headPrefabs = new List<GameObject>();
            if (headSegments != null) foreach (var h in headSegments) if (h != null) headPrefabs.Add(h);
            var tailPrefabs = new List<GameObject>();
            if (tailSegments != null) foreach (var t in tailSegments) if (t != null) tailPrefabs.Add(t);
            if (simplifyAtOrBelowRows > 0 && n <= simplifyAtOrBelowRows)
            {
                if (headPrefabs.Count > 1) headPrefabs.RemoveRange(1, headPrefabs.Count - 1); // keep first (Start)
                if (tailPrefabs.Count > 1) tailPrefabs.RemoveRange(0, tailPrefabs.Count - 1); // keep last (End)
            }

            var headGOs = new List<GameObject>(); var headL = new List<float>();
            foreach (var h in headPrefabs) { var g = Spawn(h, widthScale); headGOs.Add(g); headL.Add(Extent(g, up, perPiece)); }
            var tailGOs = new List<GameObject>(); var tailL = new List<float>();
            foreach (var t in tailPrefabs) { var g = Spawn(t, widthScale); tailGOs.Add(g); tailL.Add(Extent(g, up, perPiece)); }

            float headSum = 0f; for (int i = 0; i < headL.Count; i++) headSum += headL[i];
            float tailSum = 0f; for (int i = 0; i < tailL.Count; i++) tailSum += tailL[i];

            // Middle: measure one, decide how many (Stretch = 1; Repeat = enough to fill at natural size).
            var midGOs = new List<GameObject>(); float lmB = 0f;
            if (middleSegment != null)
            {
                var probe = Spawn(middleSegment, widthScale);
                lmB = Extent(probe, up, perPiece);
                if (lmB < 1e-4f) { built.Remove(probe); Destroy(probe); }
                else
                {
                    int nMid = (middleFill == MiddleFill.Stretch)
                        ? 1 : Mathf.Max(1, Mathf.RoundToInt((cover - headSum - tailSum) / lmB));
                    midGOs.Add(probe);
                    for (int m = 1; m < nMid; m++) midGOs.Add(Spawn(middleSegment, widthScale));
                }
            }

            float naturalTotal = headSum + midGOs.Count * lmB + tailSum;
            if (naturalTotal < 1e-4f) return;

            // k = whole-assembly shrink (only when the column is shorter than the natural assembly).
            // midScale = how much the middle stretches to absorb extra length (only when taller).
            float k = (cover < naturalTotal) ? (cover / naturalTotal) : 1f;
            float midNat = midGOs.Count * lmB;
            float midScale = (cover >= naturalTotal && midNat > 1e-4f)
                ? (midNat + (cover - naturalTotal)) / midNat : 1f;

            float cursor = 0f;
            for (int i = 0; i < headGOs.Count; i++) FitPlace(headGOs[i], up, col, topEdge, headL[i], headL[i] * k, overlapBoost, ref cursor);
            for (int i = 0; i < midGOs.Count; i++)  FitPlace(midGOs[i],  up, col, topEdge, lmB,      lmB * k * midScale, overlapBoost, ref cursor);
            for (int i = 0; i < tailGOs.Count; i++) FitPlace(tailGOs[i], up, col, topEdge, tailL[i], tailL[i] * k, overlapBoost, ref cursor);

            if (thresholdLabel != null)
            {
                thresholdLabel.transform.position = centreW + col.transform.TransformVector(labelLocalOffset);
                // Counter the parent's (column/board) non-uniform scale so the number renders square, not
                // squished: world scale becomes uniform = labelScale on every axis.
                var parent = thresholdLabel.transform.parent;
                Vector3 pls = parent != null ? parent.lossyScale : Vector3.one;
                // Scale the number with the column's WIDTH so it auto-fits: small column → small number.
                // (labelScale is a fraction of that width; the /lossyScale counters the column's own scale.)
                float fit = Mathf.Max(1e-4f, targetWidth);
                thresholdLabel.transform.localScale = new Vector3(
                    labelScale.x * fit / Mathf.Max(1e-4f, Mathf.Abs(pls.x)),
                    labelScale.y * fit / Mathf.Max(1e-4f, Mathf.Abs(pls.y)),
                    labelScale.z * fit / Mathf.Max(1e-4f, Mathf.Abs(pls.z)));
            }

#if UNITY_EDITOR
            Debug.Log($"[FrozenColumnIce] built {built.Count} segment(s) over '{col.name}' ({n} rows, {middleFill}).");
#endif
        }

        // ---- helpers ----

        GameObject Spawn(GameObject prefab, float widthScale)
        {
            var go = Instantiate(prefab, transform);
            go.transform.localRotation = Quaternion.Euler(segmentLocalEuler);
            go.transform.localScale = go.transform.localScale * widthScale;
            if (iceMaterial != null)
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                    r.sharedMaterial = onTopInstance != null ? onTopInstance : iceMaterial;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            built.Add(go);
            return go;
        }

        // Scales the segment along the LOCAL axis that currently points most along the column up-direction —
        // auto-detected from the segment's WORLD orientation, so it works no matter how the mesh was modelled
        // or rotated (no Length Axis to guess). This is what makes the middle actually stretch to fill and
        // Seam Overlap actually overlap.
        void ScaleLength(GameObject go, Vector3 up, float factor)
        {
            float dx = Mathf.Abs(Vector3.Dot(go.transform.right, up));
            float dy = Mathf.Abs(Vector3.Dot(go.transform.up, up));
            float dz = Mathf.Abs(Vector3.Dot(go.transform.forward, up));
            Vector3 s = go.transform.localScale;
            if (dx >= dy && dx >= dz) s.x *= factor;
            else if (dy >= dz)        s.y *= factor;
            else                      s.z *= factor;
            go.transform.localScale = s;
        }

        // Scales a segment from its natural length to a target length (+overlap to hide the seam), places it
        // edge-to-edge, and advances the cursor by the un-overlapped slot length.
        void FitPlace(GameObject go, Vector3 up, Column col, Vector3 topEdge,
                      float natLen, float targetLen, float overlapBoost, ref float cursor)
        {
            float f = (natLen > 1e-4f) ? (targetLen / natLen) : 1f;
            ScaleLength(go, up, f * overlapBoost);
            PlaceCenter(go, col, topEdge, up, cursor + targetLen * 0.5f);
            cursor += targetLen;
        }

        void PlaceCenter(GameObject go, Column col, Vector3 topEdge, Vector3 up, float distDown)
        {
            // Put the segment's VISUAL (renderer-bounds) centre exactly at the target — not its transform
            // pivot, which on an FBX is usually at one end. Without this the meshes land offset from where we
            // think and leave gaps between them.
            Vector3 target = topEdge - up * distDown;
            go.transform.position = target;
            Vector3 bc = BoundsCenter(go);
            go.transform.position = target - (bc - target) + col.transform.TransformVector(segmentLocalOffset);
        }

        static Vector3 BoundsCenter(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return go.transform.position;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b.center;
        }

        static GameObject First(GameObject[] arr)
        {
            if (arr != null)
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i] != null) return arr[i];
            return null;
        }

        static float Extent(GameObject go, Vector3 dir, float fallback)
        {
            if (go == null) return fallback;
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return fallback;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            Vector3 s = b.size;
            float ext = Mathf.Abs(dir.x) * s.x + Mathf.Abs(dir.y) * s.y + Mathf.Abs(dir.z) * s.z;
            return ext > 1e-4f ? ext : fallback;
        }

        void PushTowardCamera()
        {
            if (cameraOffset <= 0f) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 toCam = cam.transform.position - transform.position;
            float d = toCam.magnitude;
            if (d > 1e-4f) transform.position += (toCam / d) * cameraOffset;
        }

        static Material MakeOnTop(Material src)
        {
            var m = new Material(src) { name = src.name + " (ice on-top)" };
            if (m.HasProperty("_ZTest")) m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            m.renderQueue = OVERLAY_QUEUE;
            return m;
        }

        void ConfigureLabelOnTop()
        {
            if (thresholdLabel == null) return;
            var mat = thresholdLabel.fontMaterial;
            if (mat == null) return;
            if (mat.HasProperty("_ZTestMode")) mat.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            if (mat.HasProperty("_ZTest"))     mat.SetFloat("_ZTest",     (float)UnityEngine.Rendering.CompareFunction.Always);
            mat.renderQueue = OVERLAY_QUEUE + 1;
        }

        void ClearBuilt()
        {
            for (int i = 0; i < built.Count; i++) if (built[i] != null) Destroy(built[i]);
            built.Clear();
        }

#if UNITY_EDITOR
        bool rebuildQueued;
        void OnValidate()
        {
            if (Application.isPlaying && boundColumn != null) rebuildQueued = true;
        }
        void LateUpdate()
        {
            if (rebuildQueued && Application.isPlaying && boundColumn != null)
            {
                rebuildQueued = false;
                Rebuild();
            }
        }
#endif
    }
}
