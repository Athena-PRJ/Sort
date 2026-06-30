using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Thread overlay (standalone special-column mechanic — NOT related to Lock). Covers a Thread column
    /// with a mesh tinted to the column's required color. When the player completes ANY column of that
    /// color, GameManager calls <see cref="PlayUnlock"/>: the cover is hidden, the unlock animation
    /// (thread_anim — fibers pulled away from both ends) plays, then the overlay self-destructs.
    ///
    /// Spawned as a child of the column by <see cref="LevelLoader"/>; sits at the column origin, pushed
    /// toward the camera + optionally drawn on top so it covers the pieces. Default <see cref="CoverMode"/>
    /// is Continuous: ONE cover mesh is scaled to span the whole column (width-fit + length-stretched), so
    /// the (opaque) thread asset covers everything in one seamless piece. PerPiece keeps the old per-tile
    /// stack if a level wants it.
    /// </summary>
    public class ThreadColumnOverlay : MonoBehaviour
    {
        public enum CoverMode { Continuous, PerPiece }
        public enum Axis { X, Y, Z }

        [System.Serializable]
        public class ColorMaterial { public string colorName; public Material material; }

        [Header("Cover")]
        [Tooltip("The Thread cover mesh. Assign EITHER the Thread.fbx asset (instantiated as a child at " +
                 "runtime) OR an in-prefab child. Continuous mode stretches ONE copy over the whole column.")]
        [SerializeField] private GameObject coverRoot;
        [Tooltip("Continuous = one mesh scaled to span the whole column (default). PerPiece = one copy per piece.")]
        [SerializeField] private CoverMode coverMode = CoverMode.Continuous;
        [Tooltip("Per-color materials, picked by the column's Required Color (e.g. 'Red13' → the red thread " +
                 "material). If none matches, the cover keeps its own material.")]
        [SerializeField] private ColorMaterial[] colorMaterials;

        [Header("Placement / render")]
        [Tooltip("Push toward the camera (world units) so the cover renders in front of the column's pieces.")]
        [SerializeField] private float cameraOffset = 0.5f;
        [Tooltip("Draw on top of the pieces (ZTest Always + high queue) if the material exposes _ZTest.")]
        [SerializeField] private bool renderOnTop = true;
        [SerializeField] private int onTopRenderQueue = 4000;

        [Header("Fit")]
        [Tooltip("Auto-scale the cover to match the column width. Off = use the template's authored scale.")]
        [SerializeField] private bool autoFit = true;
        [Tooltip("Cover width as a fraction of the column/piece width (1 = match, >1 = wider).")]
        [SerializeField] private float widthPadding = 1f;
        [Tooltip("Continuous mode: which LOCAL axis of the cover mesh is its length (the direction the thread " +
                 "strands run). Pick whichever value actually lengthens the strands — depends on how the FBX " +
                 "was modelled. Explicit because auto-deriving against the tilted board scales the wrong axis.")]
        [SerializeField] private Axis lengthAxis = Axis.Y;
        [Tooltip("Continuous mode: EXTRA scale along Length Axis, applied after the width-fit. Keep near 1 " +
                 "(e.g. 1.05–1.2) for a slight vertical bump — over-stretching smears the knit into streaks.")]
        [Min(0.01f)] [SerializeField] private float lengthScale = 1f;
        [Tooltip("Base uniform scale applied before the fit.")]
        [Min(0.0001f)] [SerializeField] private float manualScale = 1f;
        [Tooltip("Rotation offset of the cover relative to the pieces (turn it if the mesh faces wrong).")]
        [SerializeField] private Vector3 coverLocalEuler = Vector3.zero;
        [Tooltip("Position nudge of the cover, in the column's frame.")]
        [SerializeField] private Vector3 coverLocalOffset = Vector3.zero;

        [Header("Unlock animation")]
        [Tooltip("Animated thread prefab (thread_anim — its Animator auto-plays the pull-from-both-ends clip). " +
                 "Spawned when the thread unlocks. Leave null to just hide the cover.")]
        [SerializeField] private GameObject unlockAnimPrefab;
        [Tooltip("Seconds the unlock animation plays before the overlay is destroyed (match the clip, ~0.5s).")]
        [SerializeField] private float unlockDuration = 0.5f;

        Column boundColumn;
        readonly List<GameObject> coverInstances = new List<GameObject>();
        bool unlocking;

        public void AttachToColumn(Column col)
        {
            boundColumn = col;
            // If Cover Root is an in-prefab child it's the TEMPLATE — hide it (we clone copies).
            if (coverRoot != null && coverRoot.transform.IsChildOf(transform)) coverRoot.SetActive(false);

            if (coverMode == CoverMode.PerPiece) BuildPerPieceCover(col);
            else BuildContinuousCover(col);
        }

        /// <summary>
        /// Default mode: ONE cover mesh, uniform width-fit to the column so it keeps the asset's natural
        /// proportions (the asset's own height does the covering), centred on the column, pushed toward the
        /// camera, tinted + on top. An optional slight bump along the explicit Length Axis (lengthScale)
        /// nudges vertical coverage — NOT a span-stretch, which smears the knit. Tune via the Fit header.
        /// </summary>
        void BuildContinuousCover(Column col)
        {
            ClearCovers();
            if (coverRoot == null || col == null) return;
            string colorName = boundColumn != null ? boundColumn.ThreadColor : null;
            var cam = Camera.main;

            var pieces = new List<Transform>();
            for (int i = 0; i < col.transform.childCount; i++)
            {
                var p = col.transform.GetChild(i).GetComponent<Piece>();
                if (p != null) pieces.Add(p.transform);
            }
            if (pieces.Count == 0) return;
            pieces.Sort((a, b) => b.position.y.CompareTo(a.position.y));

            int n = pieces.Count;
            Vector3 centreW = (pieces[0].position + pieces[n - 1].position) * 0.5f;

            // Target width = widest piece across the column.
            Vector3 widthDir = col.transform.right;
            float targetW = 0f;
            for (int i = 0; i < n; i++) targetW = Mathf.Max(targetW, Extent(pieces[i].gameObject, widthDir, 0f));
            targetW *= Mathf.Max(0.01f, widthPadding);

            var go = Instantiate(coverRoot, transform);
            go.SetActive(true);
            go.transform.rotation = pieces[0].rotation * Quaternion.Euler(coverLocalEuler);
            go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, manualScale);

            // Uniform width-fit → keeps the asset's natural proportions (so the knit isn't distorted),
            // scaled so its width matches the column.
            if (autoFit && targetW > 1e-4f)
            {
                float baseW = Extent(go, widthDir, 0f);
                if (baseW > 1e-4f) go.transform.localScale = Vector3.one * (manualScale * (targetW / baseW));
            }

            // Slight vertical bump only, along the EXPLICIT local length axis (NOT a span-stretch, and NOT
            // auto-derived from the tilted board — that scaled the depth axis and looked like nothing changed).
            if (!Mathf.Approximately(lengthScale, 1f))
            {
                Vector3 s = go.transform.localScale;
                float f = Mathf.Max(0.01f, lengthScale);
                if (lengthAxis == Axis.X) s.x *= f; else if (lengthAxis == Axis.Y) s.y *= f; else s.z *= f;
                go.transform.localScale = s;
            }

            // Centre on the column, pushed toward the camera, pivot-compensated, + the local nudge.
            Vector3 centre = centreW;
            if (cam != null && cameraOffset > 0f)
            {
                Vector3 toCam = cam.transform.position - centre;
                float d = toCam.magnitude;
                if (d > 1e-4f) centre += (toCam / d) * cameraOffset;
            }
            go.transform.position = centre;
            Vector3 bc = BoundsCenter(go);
            go.transform.position = centre - (bc - centre) + col.transform.TransformVector(coverLocalOffset);

            ApplyColorAndOnTop(go, colorName);
            coverInstances.Add(go);
        }

        /// <summary>Legacy mode: one cover per piece, each sized to its piece and stacked like the column.</summary>
        void BuildPerPieceCover(Column col)
        {
            ClearCovers();
            if (coverRoot == null || col == null) return;
            string colorName = boundColumn != null ? boundColumn.ThreadColor : null;
            var cam = Camera.main;

            for (int i = 0; i < col.transform.childCount; i++)
            {
                var p = col.transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;

                var go = Instantiate(coverRoot, transform);
                go.SetActive(true);
                go.transform.rotation = p.transform.rotation * Quaternion.Euler(coverLocalEuler);
                go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, manualScale);

                if (autoFit)
                {
                    Vector3 wDir = p.transform.right;
                    float targetW = Extent(p.gameObject, wDir, 0f) * Mathf.Max(0.01f, widthPadding);
                    float baseW = Extent(go, wDir, 0f);
                    float s = (baseW > 1e-4f && targetW > 1e-4f) ? manualScale * (targetW / baseW) : manualScale;
                    go.transform.localScale = Vector3.one * s;
                }

                Vector3 centre = p.transform.position;
                if (cam != null && cameraOffset > 0f)
                {
                    Vector3 toCam = cam.transform.position - centre;
                    float d = toCam.magnitude;
                    if (d > 1e-4f) centre += (toCam / d) * cameraOffset;
                }
                go.transform.position = centre;
                Vector3 bc = BoundsCenter(go);
                go.transform.position = centre - (bc - centre) + col.transform.TransformVector(coverLocalOffset);

                ApplyColorAndOnTop(go, colorName);
                coverInstances.Add(go);
            }
        }

        void ClearCovers()
        {
            for (int i = 0; i < coverInstances.Count; i++)
                if (coverInstances[i] != null) Destroy(coverInstances[i]);
            coverInstances.Clear();
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

        static Vector3 BoundsCenter(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return go.transform.position;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b.center;
        }

        /// <summary>Plays the unlock animation then destroys the overlay. Called by GameManager when the
        /// matching color is completed. Safe to call once (re-entrant guarded).</summary>
        public void PlayUnlock()
        {
            if (unlocking) return;
            unlocking = true;
            StartCoroutine(UnlockRoutine());
        }

        IEnumerator UnlockRoutine()
        {
            for (int i = 0; i < coverInstances.Count; i++)
                if (coverInstances[i] != null) coverInstances[i].SetActive(false);

            if (unlockAnimPrefab != null)
            {
                var anim = Instantiate(unlockAnimPrefab, transform);
                anim.transform.localPosition = Vector3.zero;
                anim.transform.localRotation = Quaternion.identity;
                ApplyColorAndOnTop(anim, boundColumn != null ? boundColumn.ThreadColor : null);
                SfxManager.Play(SfxId.Unfreeze);   // reuse the unfreeze sfx for the thread snap
            }

            if (unlockDuration > 0f) yield return new WaitForSeconds(unlockDuration);
            Destroy(gameObject);
        }

        // ---- helpers ----

        // Tints by per-color material (if any) AND applies render-on-top in ONE pass, using a fresh Material
        // INSTANCE per renderer assigned via sharedMaterial. Avoids Renderer.materials (throws on prefab
        // assets) and never mutates the shared material assets. No tint fallback — if no per-color material
        // matches, the cover keeps its own material.
        void ApplyColorAndOnTop(GameObject target, string colorName)
        {
            if (target == null) return;
            Material colorMat = FindColorMaterial(colorName);
            var rends = target.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in rends)
            {
                if (r == null) continue;
                Material src = colorMat != null ? colorMat : r.sharedMaterial;
                if (src == null) continue;
                var inst = new Material(src) { name = src.name + " (thread)" };
                if (renderOnTop)
                {
                    if (inst.HasProperty("_ZTest")) inst.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    inst.renderQueue = onTopRenderQueue;
                }
                r.sharedMaterial = inst;
            }
        }

        Material FindColorMaterial(string colorName)
        {
            if (colorMaterials == null || string.IsNullOrEmpty(colorName)) return null;
            for (int i = 0; i < colorMaterials.Length; i++)
                if (colorMaterials[i] != null && colorMaterials[i].colorName == colorName)
                    return colorMaterials[i].material;
            return null;
        }
    }
}
