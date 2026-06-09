using TMPro;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Visual for the Break Wall Stack mechanic. Sits over a frozen Column showing the number of
    /// OTHER columns the player still needs to lock before this one unfreezes. Also renders an
    /// optional placeholder fade Quad + a slot for designer-supplied frozen-state art (material).
    ///
    /// Lifecycle:
    ///   - LevelLoader instantiates this prefab parented to the column transform AND calls
    ///     <see cref="AttachToColumn"/> which positions the overlay at the column's piece-centroid
    ///     IN WORLD SPACE and rotates it to lie FLAT on the board surface (coplanar with the column's
    ///     pieces) so it reads as a decal covering the whole column. Robust against board / column rotation.
    ///   - GameManager calls <see cref="SetRemaining"/> on each column lock — the displayed number
    ///     ticks down live as the player progresses.
    ///   - Position+rotation also refresh in LateUpdate so the overlay tracks any camera movement.
    /// </summary>
    public class FrozenOverlay : MonoBehaviour
    {
        [Tooltip("TMP_Text that shows the remaining-columns-to-lock count. Use the 3D TextMeshPro " +
                 "variant (NOT TextMeshProUGUI) so it renders in world space alongside the pieces.")]
        [SerializeField] private TMP_Text thresholdLabel;

        [Tooltip("MeshRenderer of the placeholder fade Quad covering the frozen column. Toggle " +
                 "visibility with 'Show Fade Overlay' so the designer can disable it once proper " +
                 "frozen-state art exists, without removing the number-display child.")]
        [SerializeField] private MeshRenderer fadeQuad;

        [Tooltip("Optional custom material applied to the fade Quad. Use this slot to attach your " +
                 "frozen-state art (e.g. ice-crystal texture, snowflake overlay) when ready — simply " +
                 "create a Material with the art as Base Map and drop it here. When null, the Quad " +
                 "uses its prefab-default material (typically a dark semi-transparent placeholder).")]
        [SerializeField] private Material customArtMaterial;

        [Tooltip("Toggle the fade Quad visibility. ON = placeholder fade visible (default during " +
                 "development). OFF = Quad hidden, only the number-label child stays visible. " +
                 "Useful for testing gameplay without visual clutter.")]
        [SerializeField] private bool showFadeOverlay = true;

        [Tooltip("How far to push the overlay toward the camera (world units) so it renders ON TOP " +
                 "of the pieces. Same idea as TieVisual.cameraOffset — pieces have non-trivial Z-depth " +
                 "(BoxCollider.size.z ≈ 1.0), so a small push past the piece's near-face is needed to " +
                 "avoid clipping. 0.5 is enough for typical Sort# camera; increase if the overlay still " +
                 "ends up partly behind, decrease to keep it tight against the column.")]
        [SerializeField] private float cameraOffset = 0.5f;

        [Tooltip("Overlay orientation.\n" +
                 "ON (default): the overlay is laid flat over the column using the fixed, tunable LOCAL " +
                 "rotation in 'Board Surface Local Euler' below — so it reads as a decal that covers & " +
                 "hides the whole column.\n" +
                 "OFF: legacy billboard — rotates every frame to face Camera.main as a flat screen-" +
                 "facing strip.")]
        [SerializeField] private bool alignToBoardSurface = true;

        [Tooltip("LOCAL rotation (Euler degrees, relative to the column) applied to the overlay when " +
                 "'Align To Board Surface' is ON. Tune this so the overlay lies flat over the column and " +
                 "covers it. Default (85,180,0) matches the standard board tilt — if a level uses a " +
                 "different board angle, adjust X here. Auto-deriving this from the pieces proved " +
                 "unreliable (prefab Quad + board tilt + per-piece RestRotation interact), so it's an " +
                 "explicit, predictable value you can dial in live.")]
        [SerializeField] private Vector3 boardSurfaceLocalEuler = new Vector3(85f, 180f, 0f);

        [Tooltip("Multiplier on the auto-fitted fade-cover size, for fine-tuning how much of the column " +
                 "the overlay covers. (1,1,1) = exactly fits the column's pieces top-to-bottom & width. " +
                 "Raise X/Y to overhang the edges, lower to inset. Like 'Board Surface Local Euler', this " +
                 "is a live Inspector tuning knob.")]
        [SerializeField] private Vector3 fadeCoverScale = Vector3.one;

        [Tooltip("THIS is the knob to move the threshold NUMBER. Local position offset for the count " +
                 "label, relative to the overlay's origin. (0,0,0) = centered. Nudge X/Y to reposition " +
                 "the number; push Z toward the camera to lift it further off the fade cover. The number " +
                 "is already forced to render ON TOP of the fade quad in code — use this only for " +
                 "fine placement.")]
        [SerializeField] private Vector3 labelLocalPosition = Vector3.zero;

        Column boundColumn;

        // Render queue for the fade quad (Overlay range — after opaque + transparent). The threshold
        // number is pushed one above this so it always draws on top of the cover.
        const int FADE_RENDER_QUEUE = 4000;

        void Awake()
        {
            ConfigureFadeQuadMaterial();
            if (fadeQuad != null) fadeQuad.enabled = showFadeOverlay;
            // The overlay is purely visual and sits in front of the column. Disable any colliders on it
            // so it never swallows a tap meant for the (frozen) column's pieces — PlayerHand needs the
            // tap to register on a Piece to play the reject shake.
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        /// <summary>
        /// Forces the threshold-number label to render ON TOP of the fade quad: a render queue one above
        /// the fade cover + ZTest Always so the count is never hidden behind the (always-on-top) overlay.
        /// Works on a material INSTANCE (TMP's <c>fontMaterial</c>) so the shared font asset is untouched.
        /// Property names are guarded — silently skips on shaders that don't expose them. If the label is
        /// a UGUI text whose Canvas overrides draw order, nudge it toward the camera via Label Local
        /// Position (its Z) instead.
        /// </summary>
        void ConfigureLabelOnTop()
        {
            if (thresholdLabel == null) return;
            var mat = thresholdLabel.fontMaterial; // TMP returns a per-instance material
            if (mat == null) return;
            if (mat.HasProperty("_ZTestMode")) mat.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            if (mat.HasProperty("_ZTest"))     mat.SetFloat("_ZTest",     (float)UnityEngine.Rendering.CompareFunction.Always);
            mat.renderQueue = FADE_RENDER_QUEUE + 1;
        }

        /// <summary>
        /// Instances the source material (customArtMaterial if set, else fadeQuad's prefab default)
        /// and forces overlay-friendly render state on the instance:
        ///   - <c>_Cull = Off</c>: both sides of the Quad render → visible regardless of camera angle.
        ///   - <c>_ZTest = Always</c>: skips depth test → renders on top of pieces (their meshes have
        ///     non-trivial Z-depth, so equal-depth Quads can otherwise be clipped).
        ///   - <c>renderQueue = 4000</c> (Overlay): drawn after all opaque + transparent geometry.
        /// Source asset is never modified — we work on an instance.
        /// </summary>
        void ConfigureFadeQuadMaterial()
        {
            if (fadeQuad == null) return;
            Material source = customArtMaterial != null ? customArtMaterial : fadeQuad.sharedMaterial;
            if (source == null) return;

            var instance = new Material(source);
            instance.name = source.name + " (frozen overlay instance)";
            // Properties below only take effect if the shader exposes them (URP/Lit + URP/Unlit do).
            // Silent no-op on shaders without these — guard with HasProperty for cleanliness.
            if (instance.HasProperty("_Cull"))  instance.SetInt("_Cull",  (int)UnityEngine.Rendering.CullMode.Off);
            if (instance.HasProperty("_ZTest")) instance.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            instance.renderQueue = FADE_RENDER_QUEUE;
            fadeQuad.material = instance;
        }

        void LateUpdate()
        {
            // Re-orient every frame to lie flat on the board surface (coplanar with the column's
            // pieces). Cheap and robust if the board ever animates. We also re-push toward the
            // camera every frame so the overlay keeps rendering in front of the pieces.
            if (boundColumn == null) return;
            transform.position = ComputeColumnPieceCentroidWorld(boundColumn);
            AlignRotation(boundColumn);
            PushTowardCamera();
        }

        /// <summary>
        /// Wires the overlay to the column it represents:
        ///   1. Snaps position to the column's piece-centroid in WORLD space (robust to nested transforms).
        ///   2. Pushes that position by <see cref="cameraOffset"/> toward Camera.main so the overlay
        ///      renders ON TOP of the pieces (escapes their depth volume).
        ///   3. Rotates to lie flat on the board surface (coplanar with the column's pieces) — see AlignRotation.
        ///   4. Auto-sizes the FadeQuad so its world extent covers ALL the column's pieces top-to-bottom.
        /// Called by LevelLoader right after Instantiate.
        /// </summary>
        public void AttachToColumn(Column col)
        {
            if (col == null) return;
            boundColumn = col;
            transform.position = ComputeColumnPieceCentroidWorld(col);
            AlignRotation(col);
            PushTowardCamera();
            AutoSizeFadeQuad(col);
            // Force child local position + rotation to zero / identity so the root's transform
            // is the ONLY transform applied to them. If a designer offset the Quad / label in
            // the prefab (e.g. eyeballing the layout in Edit Mode), that offset would compound
            // with the root's rotation and the children would appear shifted / misoriented at
            // runtime — this resets that authorial drift.
            if (fadeQuad != null)
            {
                // Reset local transform — root's face-camera rotation is the ONLY rotation we want
                // applied. Any prefab-authored Quad rotation would compound and misalign the visible face.
                fadeQuad.transform.localPosition = Vector3.zero;
                fadeQuad.transform.localRotation = Quaternion.identity;
            }
            if (thresholdLabel != null)
            {
                // labelLocalPosition is designer-tunable; rotation stays identity so the number
                // inherits the root's face-camera orientation cleanly.
                thresholdLabel.transform.localPosition = labelLocalPosition;
                thresholdLabel.transform.localRotation = Quaternion.identity;
                // Force horizontal+vertical center alignment so the number sits at the label's
                // local origin regardless of how the TMP_Text was authored in the prefab.
                thresholdLabel.alignment = TMPro.TextAlignmentOptions.Center;
                // Make the number draw on top of the fade cover (TMP is initialized by now).
                ConfigureLabelOnTop();
            }
        }

        /// <summary>
        /// Pushes the current "remaining columns to lock before this unfreezes" count to the label.
        /// Caller computes <paramref name="remaining"/> = unlockThreshold − totalLockedSoFar and
        /// calls this each time a column locks. Clamped ≥ 0 so the player never sees a negative count.
        /// </summary>
        public void SetRemaining(int remaining)
        {
            if (thresholdLabel != null)
                thresholdLabel.text = Mathf.Max(0, remaining).ToString();
        }

        // ---------------------------------------------------------------------
        //  Orientation / placement helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Lays the overlay flat over the column so it reads as a decal that covers and hides the
        /// whole column, rather than a screen-facing strip at an odd angle.
        ///
        /// Applies a fixed, designer-tunable LOCAL rotation (<see cref="boardSurfaceLocalEuler"/>,
        /// relative to the column). We deliberately do NOT derive this from the pieces or the camera:
        /// the prefab's Quad orientation + the board tilt + each piece's RestRotation interact in ways
        /// that made auto-derivation land at wrong angles. An explicit value the designer dials in once
        /// is both simpler and predictable.
        ///
        /// Set <see cref="alignToBoardSurface"/> = false to fall back to the legacy camera billboard.
        /// </summary>
        void AlignRotation(Column col)
        {
            if (alignToBoardSurface)
            {
                transform.localRotation = Quaternion.Euler(boardSurfaceLocalEuler);
                return;
            }

            // Legacy billboard: LookRotation toward Camera.main with up = camera up.
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 toCamera = cam.transform.position - transform.position;
            float dist = toCamera.magnitude;
            if (dist <= 1e-4f) return;
            transform.rotation = Quaternion.LookRotation(toCamera / dist, cam.transform.up);
        }

        /// <summary>Average of the column's Piece children's WORLD positions — robust to nested transforms.</summary>
        static Vector3 ComputeColumnPieceCentroidWorld(Column col)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < col.transform.childCount; i++)
            {
                var p = col.transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                sum += p.transform.position;
                count++;
            }
            return count > 0 ? sum / count : col.transform.position;
        }

        /// <summary>
        /// Translates the overlay <see cref="cameraOffset"/> world-units toward Camera.main so it
        /// renders in front of the pieces' Z-depth volume. No-op if cameraOffset ≤ 0 or no main camera.
        /// </summary>
        void PushTowardCamera()
        {
            if (cameraOffset <= 0f) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 toCamera = cam.transform.position - transform.position;
            float dist = toCamera.magnitude;
            if (dist > 1e-4f)
                transform.position += (toCamera / dist) * cameraOffset;
        }

        /// <summary>
        /// Sizes the FadeQuad so its WORLD extent covers the column top-to-bottom (Y) and matches the
        /// pieces' width (X). Computes top→bottom piece distance + per-piece extent for the height,
        /// and the max BoxCollider.size.x × lossyScale.x for the width. Divides by the FrozenOverlay's
        /// lossyScale.x so the Quad's localScale produces the right world size regardless of how the
        /// overlay (or its column parent / board grandparent) is scaled. Designer can leave the Quad's
        /// localScale at any value in the prefab — this method overrides it at runtime.
        /// </summary>
        void AutoSizeFadeQuad(Column col)
        {
            if (fadeQuad == null) return;

            // Measure column extent in WORLD units from piece positions.
            Vector3 topPos = Vector3.zero, bottomPos = Vector3.zero;
            float maxPieceWidth = 0f;
            int count = 0;
            bool hasTop = false;
            for (int i = 0; i < col.transform.childCount; i++)
            {
                var p = col.transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                if (!hasTop) { topPos = p.transform.position; hasTop = true; }
                bottomPos = p.transform.position;
                var bc = p.GetComponent<BoxCollider>();
                if (bc != null)
                {
                    float w = Mathf.Abs(bc.size.x * p.transform.lossyScale.x);
                    if (w > maxPieceWidth) maxPieceWidth = w;
                }
                count++;
            }
            if (count == 0) return;

            // Distance between first and last piece centers + one piece-slot's worth of padding so
            // the overlay covers the top piece's top edge and the bottom piece's bottom edge.
            float spanCenters = Vector3.Distance(topPos, bottomPos);
            float perPiece    = (count > 1) ? spanCenters / (count - 1) : maxPieceWidth;
            float worldHeight = spanCenters + perPiece;
            float worldWidth  = maxPieceWidth;

            // Convert world units to local (divide by FrozenOverlay's lossy scale). Uniform-scale
            // assumption — if your board has non-uniform scale, the Quad may look slightly off.
            float lossy = Mathf.Abs(fadeQuad.transform.lossyScale.x);
            if (lossy < 1e-4f) lossy = 1f;
            // Parent lossy already accounts for FrozenOverlay's chain, so the Quad's localScale × that
            // = world size. Compute targetLocal = worldSize / (lossy / quadLocal.x), i.e. directly set
            // localScale to (worldSize / lossy) using fadeQuad's PARENT lossy.
            float parentLossyX = Mathf.Abs(transform.lossyScale.x);
            float parentLossyY = Mathf.Abs(transform.lossyScale.y);
            if (parentLossyX < 1e-4f) parentLossyX = 1f;
            if (parentLossyY < 1e-4f) parentLossyY = 1f;
            // fadeCoverScale is a designer multiplier on top of the column-fit size (live tuning).
            fadeQuad.transform.localScale = new Vector3(
                (worldWidth  / parentLossyX) * fadeCoverScale.x,
                (worldHeight / parentLossyY) * fadeCoverScale.y,
                fadeCoverScale.z);
        }
    }
}
