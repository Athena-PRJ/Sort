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
    /// is StackedRows: the Thread asset is ONE horizontal ROW, and copies of it are tiled (butted + slightly
    /// overlapped) down the whole column so it reads as one continuous knit. PerPiece keeps the old
    /// one-copy-per-piece stack if a level wants it.
    /// </summary>
    public class ThreadColumnOverlay : MonoBehaviour
    {
        public enum CoverMode { StackedRows, PerPiece }

        [System.Serializable]
        public class ColorMaterial { public string colorName; public Material material; }

        [Header("Cover")]
        [Tooltip("The Thread cover mesh = ONE horizontal ROW (Thread.fbx). Assign the fbx asset (instantiated " +
                 "as children at runtime) OR an in-prefab child. StackedRows tiles copies of it down the column.")]
        [SerializeField] private GameObject coverRoot;
        [Tooltip("StackedRows = tile the single-row mesh, butted together, down the whole column (default). " +
                 "PerPiece = one copy per piece.")]
        [SerializeField] private CoverMode coverMode = CoverMode.StackedRows;
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
        [Tooltip("StackedRows: stretch the thread's total LENGTH along the column (1 = exactly span the " +
                 "pieces, >1 = a bit longer, extending past both ends; rows get proportionally taller). " +
                 "Bump slightly, e.g. 1.05–1.15, if the thread looks a touch short.")]
        [Min(0.01f)] [SerializeField] private float lengthScale = 1f;
        [Tooltip("StackedRows: how many thread rows to place inside EACH piece cell (fixed density). " +
                 "A column of N pieces gets N × this many rows (e.g. 5 pieces × 7 = 35 rows).")]
        [Min(1)] [SerializeField] private int rowsPerPiece = 7;
        [Tooltip("StackedRows: how much thicker each row is than its slot, so rows overlap and butt together " +
                 "with no gaps while staying distinct (0 = exactly butted, ~0.05–0.2 hides the seams).")]
        [Range(0f, 0.9f)] [SerializeField] private float rowOverlap = 0.08f;
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
        [Tooltip("On unlock, PEEL the Thread rows away from BOTH ends toward the centre over Unlock Duration " +
                 "(synced with the pull clip) instead of hiding them all at once. Off = instant.")]
        [SerializeField] private bool unravelFromEnds = true;
        [Tooltip("Spawn the pull animation at BOTH ends (top + bottom). The top copy is spawned with " +
                 "Top Anim Local Euler so its string points the other way.")]
        [SerializeField] private bool animAtBothEnds = true;
        [Tooltip("Local rotation for the TOP animation copy so it mirrors the bottom one (a string at the top " +
                 "instead of the bottom). Default (0,0,180) flips it in-plane; tune if the mirror looks wrong.")]
        [SerializeField] private Vector3 topAnimLocalEuler = new Vector3(0, 0, 180);

        Column boundColumn;
        readonly List<GameObject> coverInstances = new List<GameObject>();
        bool unlocking;

        bool needsRebuild;

        public void AttachToColumn(Column col)
        {
            boundColumn = col;
            // If Cover Root is an in-prefab child it's the TEMPLATE — hide it (we clone copies).
            if (coverRoot != null && coverRoot.transform.IsChildOf(transform)) coverRoot.SetActive(false);
            Rebuild();
        }

        void Rebuild()
        {
            if (boundColumn == null) return;
            if (coverMode == CoverMode.PerPiece) BuildPerPieceCover(boundColumn);
            else BuildStackedRows(boundColumn);
        }

        // Live-tuning: rebuild when a field changes in Play mode so Cover Local Euler/Offset, Rows Per Piece,
        // width, etc. are visible immediately (the cover is otherwise built only once at attach). Deferred to
        // LateUpdate because Destroy/Instantiate can't run straight from OnValidate.
        void OnValidate()
        {
            if (Application.isPlaying && boundColumn != null && !unlocking) needsRebuild = true;
        }

        void LateUpdate()
        {
            if (!needsRebuild) return;
            needsRebuild = false;
            if (!unlocking) Rebuild();
        }

        /// <summary>
        /// Default mode: the Thread asset is ONE horizontal row; tile copies of it butted together down the
        /// whole column. Each row is width-fit to the column, then its THICKNESS is scaled so exactly
        /// <see cref="rowsPerPiece"/> rows fill each piece cell (N pieces → N × rowsPerPiece rows), with a
        /// slight <see cref="rowOverlap"/> so the seams disappear into a continuous knit. Placement uses the
        /// pieces' ACTUAL world positions (not a derived board angle), so the tilted board is a non-issue.
        /// </summary>
        void BuildStackedRows(Column col)
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
            pieces.Sort((a, b) => b.position.y.CompareTo(a.position.y));   // top → bottom

            int n = pieces.Count;
            Vector3 topW = pieces[0].position;
            Vector3 botW = pieces[n - 1].position;
            // Column direction from ACTUAL piece positions (reliable on the tilted board), pointing up.
            Vector3 up = (n > 1) ? (topW - botW).normalized : col.transform.up;
            float perPiece = (n > 1) ? Vector3.Distance(topW, botW) / (n - 1) : Extent(pieces[0].gameObject, up, 1f);
            // Full column length (both end pieces), optionally stretched a bit by lengthScale (rows get
            // proportionally taller so they stay contiguous). Extra length is added symmetrically at both ends.
            float span = (Vector3.Distance(topW, botW) + perPiece) * Mathf.Max(0.01f, lengthScale);
            Vector3 centreW = (topW + botW) * 0.5f;
            Vector3 topEdge = centreW + up * (span * 0.5f);          // top edge of the (scaled) coverage

            // Target width = widest piece across the column.
            Vector3 widthDir = col.transform.right;
            float targetW = 0f;
            for (int i = 0; i < n; i++) targetW = Mathf.Max(targetW, Extent(pieces[i].gameObject, widthDir, 0f));
            targetW *= Mathf.Max(0.01f, widthPadding);

            Quaternion rot = pieces[0].rotation * Quaternion.Euler(coverLocalEuler);

            // Measure the row on a probe: width-fit uniform scale, then the row's thickness ALONG the column.
            float widthScale = Mathf.Max(0.0001f, manualScale);
            var probe = Instantiate(coverRoot, transform);
            probe.SetActive(true);
            probe.transform.rotation = rot;
            probe.transform.localScale = Vector3.one * widthScale;
            if (autoFit && targetW > 1e-4f)
            {
                float baseW = Extent(probe, widthDir, 0f);
                if (baseW > 1e-4f) widthScale = Mathf.Max(0.0001f, manualScale) * (targetW / baseW);
                probe.transform.localScale = Vector3.one * widthScale;
            }
            float rowH = Extent(probe, up, 0f);   // one row's natural thickness along the column, at width-fit scale
            Destroy(probe);
            if (rowH < 1e-4f) return;

            // FIXED density: rowsPerPiece rows in every piece cell → N pieces = N × rowsPerPiece rows.
            int rows = Mathf.Clamp(n * Mathf.Max(1, rowsPerPiece), 1, 512);
            float spacing = span / rows;
            // Scale each row's THICKNESS (its axis along the column) to the slot + a slight overlap, so the
            // rows butt together with no gaps yet stay DISTINCT (not merged). Width keeps the uniform fit.
            float targetThickness = spacing * (1f + Mathf.Clamp(rowOverlap, 0f, 0.9f));
            float lenFactor = targetThickness / rowH;

            for (int i = 0; i < rows; i++)
            {
                var go = Instantiate(coverRoot, transform);
                go.SetActive(true);
                go.transform.rotation = rot;
                go.transform.localScale = Vector3.one * widthScale;
                ScaleLength(go, up, lenFactor);   // thickness (column axis) only — keeps row width = column width

                // Row centre: march down from the top edge, half a slot in for the first row.
                Vector3 centre = topEdge - up * (spacing * (i + 0.5f));
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

        // Scales a spawned row along the LOCAL axis that currently points most along `dir` (the column
        // direction) — auto-detected from the row's world orientation, so it works whatever way the FBX was
        // modelled/rotated. Used to set each row's thickness independently of its width.
        static void ScaleLength(GameObject go, Vector3 dir, float factor)
        {
            float dx = Mathf.Abs(Vector3.Dot(go.transform.right, dir));
            float dy = Mathf.Abs(Vector3.Dot(go.transform.up, dir));
            float dz = Mathf.Abs(Vector3.Dot(go.transform.forward, dir));
            Vector3 s = go.transform.localScale;
            if (dx >= dy && dx >= dz) s.x *= factor;
            else if (dy >= dz)        s.y *= factor;
            else                      s.z *= factor;
            go.transform.localScale = s;
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
            // Spawn the pull animation FIRST so it plays while the cover peels away — one at each end.
            if (unlockAnimPrefab != null)
            {
                SpawnUnlockAnim(Quaternion.identity);                                 // bottom string
                if (animAtBothEnds) SpawnUnlockAnim(Quaternion.Euler(topAnimLocalEuler)); // mirrored → top string
                SfxManager.Play(SfxId.Unfreeze);   // reuse the unfreeze sfx for the thread snap
            }

            if (unravelFromEnds)
            {
                yield return StartCoroutine(UnravelCovers(unlockDuration));
            }
            else
            {
                for (int i = 0; i < coverInstances.Count; i++)
                    if (coverInstances[i] != null) coverInstances[i].SetActive(false);
                if (unlockDuration > 0f) yield return new WaitForSeconds(unlockDuration);
            }

            Destroy(gameObject);
        }

        void SpawnUnlockAnim(Quaternion localRot)
        {
            var anim = Instantiate(unlockAnimPrefab, transform);
            anim.transform.localPosition = Vector3.zero;
            anim.transform.localRotation = localRot;
            ApplyColorAndOnTop(anim, boundColumn != null ? boundColumn.ThreadColor : null);
        }

        /// <summary>
        /// Peels the cover rows away from BOTH ends toward the centre over <paramref name="duration"/>,
        /// SEQUENTIALLY: the top row wipes horizontally in one direction, the next top row the OTHER
        /// direction, and so on (same for the bottom rows) — a zig-zag zip that reads more natural than an
        /// even shrink-to-centre. Top and bottom advance together, meeting in the middle.
        /// </summary>
        IEnumerator UnravelCovers(float duration)
        {
            var rows = new List<GameObject>();
            for (int i = 0; i < coverInstances.Count; i++)
                if (coverInstances[i] != null) rows.Add(coverInstances[i]);
            int n = rows.Count;
            if (n == 0) { if (duration > 0f) yield return new WaitForSeconds(duration); yield break; }

            // Sort top → bottom so "both ends" are the real top and bottom of the column.
            rows.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
            Vector3 widthDir = boundColumn != null ? boundColumn.transform.right : Vector3.right;

            int steps = (n + 1) / 2;
            float stepTime = duration / Mathf.Max(1, steps);
            for (int s = 0; s < steps; s++)
            {
                bool leftToRight = (s % 2 == 0);   // alternate the wipe direction each row
                int top = s, bot = n - 1 - s;
                if (rows[top] != null) StartCoroutine(WipeRow(rows[top], widthDir, leftToRight, stepTime * 1.3f));
                if (bot != top && rows[bot] != null) StartCoroutine(WipeRow(rows[bot], widthDir, leftToRight, stepTime * 1.3f));
                yield return new WaitForSeconds(stepTime);
            }
        }

        /// <summary>Wipes one row out HORIZONTALLY: shrinks its width to zero while holding one edge fixed
        /// (left→right disappears the left side first, right→left the right side), so it reads like a thread
        /// being pulled sideways rather than shrinking to its centre.</summary>
        IEnumerator WipeRow(GameObject row, Vector3 widthDir, bool leftToRight, float dur)
        {
            if (row == null) yield break;
            var tr = row.transform;
            int axis = WidthAxisIndex(row, widthDir);            // which LOCAL scale axis is the row's width
            Vector3 startScale = tr.localScale;
            Vector3 startPos = tr.position;
            float w = Extent(row, widthDir, 0f);                 // world width
            float sign = leftToRight ? 1f : -1f;                 // + holds the RIGHT edge, - holds the LEFT
            float t = 0f;
            while (t < dur && row != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(1e-4f, dur));
                Vector3 s = startScale; s[axis] = startScale[axis] * (1f - k);
                tr.localScale = s;
                tr.position = startPos + widthDir * (sign * (w * 0.5f) * k);   // keep the anchored edge in place
                yield return null;
            }
            if (row != null) row.SetActive(false);
        }

        // Index (0=X,1=Y,2=Z) of the row's LOCAL scale axis that currently points most along `dir`.
        static int WidthAxisIndex(GameObject go, Vector3 dir)
        {
            float dx = Mathf.Abs(Vector3.Dot(go.transform.right, dir));
            float dy = Mathf.Abs(Vector3.Dot(go.transform.up, dir));
            float dz = Mathf.Abs(Vector3.Dot(go.transform.forward, dir));
            return (dx >= dy && dx >= dz) ? 0 : (dy >= dz ? 1 : 2);
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
            // Renderer (not MeshRenderer) so the ANIMATED thread_anim — a SkinnedMeshRenderer — also gets the
            // tint + on-top state. That's why the anim was rendering BEHIND the board: skinned meshes were skipped.
            var rends = target.GetComponentsInChildren<Renderer>(true);
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
