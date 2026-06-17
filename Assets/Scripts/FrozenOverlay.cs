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

        [Tooltip("RECOMMENDED frozen art: a 9-SLICED (or Tiled) SpriteRenderer for the ice strip — avoids the " +
                 "stretch-distortion you get from scaling a detailed sprite. Import the freeze sprite in the " +
                 "Sprite Editor with 9-slice BORDERS (crisp top/bottom caps + a stretchable/tileable middle) " +
                 "and set this SpriteRenderer's Draw Mode to Sliced (middle stretches) or Tiled (middle repeats). " +
                 "At runtime the overlay sets its .size to the column's extent, so ONLY the middle adapts to the " +
                 "column length — the caps stay sharp at every size. Leave null to use the legacy fade Quad.")]
        [SerializeField] private SpriteRenderer iceStrip;

        [Tooltip("Toggle the fade Quad visibility. ON = placeholder fade visible (default during " +
                 "development). OFF = Quad hidden, only the number-label child stays visible. " +
                 "Useful for testing gameplay without visual clutter.")]
        [SerializeField] private bool showFadeOverlay = true;

        [Tooltip("Sorting order for the ice-strip SpriteRenderer. Pushed high so the ice draws above the " +
                 "board indicators (sorting order ~10). The ice also gets an always-on-top material at " +
                 "runtime so it renders over the 3D pieces regardless of depth.")]
        [SerializeField] private int iceStripSortingOrder = 50;

        [Tooltip("How far to push the overlay toward the camera (world units) so it renders ON TOP " +
                 "of the pieces. Same idea as TieVisual.cameraOffset — pieces have non-trivial Z-depth " +
                 "(BoxCollider.size.z ≈ 1.0), so a small push past the piece's near-face is needed to " +
                 "avoid clipping. 0.5 is enough for typical Sort# camera; increase if the overlay still " +
                 "ends up partly behind, decrease to keep it tight against the column.")]
        [SerializeField] private float cameraOffset = 0.5f;

        [Tooltip("Manual nudge for the WHOLE overlay, in the COLUMN's local space (so it matches the board " +
                 "tilt): X = left/right on screen, Y = UP/DOWN on screen, Z = depth (toward/away camera). " +
                 "Applied after centering on the column. Use Y to shift the ice up a bit, X sideways. " +
                 "Live-tunable in Play mode (edit it on the FrozenOverlay(Clone) in the Hierarchy).")]
        [SerializeField] private Vector3 overlayOffset = Vector3.zero;

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

        Column boundColumn;

        // Render queue for the fade quad (Overlay range — after opaque + transparent). The threshold
        // number is pushed one above this so it always draws on top of the cover.
        const int FADE_RENDER_QUEUE = 4000;

        void Awake()
        {
            ConfigureFadeQuadMaterial();
            ConfigureIceStripMaterial();
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

            // The ice strip (a SpriteRenderer) sorts by sortingOrder, which beats renderQueue for that
            // renderer type — so without this the number (sortingOrder ~0) draws BEHIND the ice strip
            // (sortingOrder = iceStripSortingOrder) and is invisible. Push the 3D-TMP label's renderer
            // one above the ice. (No-op for a UGUI/CanvasRenderer label, which sorts via its Canvas.)
            var labelRenderer = thresholdLabel.GetComponent<Renderer>();
            if (labelRenderer != null) labelRenderer.sortingOrder = iceStripSortingOrder + 1;
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
        /// <summary>
        /// Makes the ice-strip SpriteRenderer render ON TOP of the 3D pieces. The default sprite material
        /// ZTests LEqual against the pieces' depth and gets occluded (the bug: ice sat behind the column).
        /// We swap in the Sort/SpriteOverlay shader (ZTest Always + Queue Overlay — same idea as the fade
        /// quad and TieOverlay) and bump the sorting order above the board indicators. The SpriteRenderer's
        /// own sprite + 9-slice draw mode are untouched; only the blend/depth state changes.
        /// </summary>
        void ConfigureIceStripMaterial()
        {
            if (iceStrip == null) return;
            iceStrip.sortingOrder = iceStripSortingOrder;

            var sh = Shader.Find("Sort/SpriteOverlay");
            if (sh == null) return;   // shader missing from build → keep default material (still visible if not occluded)
            iceStrip.sharedMaterial = new Material(sh) { name = "IceStrip overlay (instance)" };
        }

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
            if (boundColumn == null) return;
            // Re-apply placement + size EVERY frame so Inspector tweaks (overlayOffset, cameraOffset,
            // boardSurfaceLocalEuler, fadeCoverScale) update LIVE in Play mode. The threshold LABEL is left
            // alone — the designer positions it by hand as a child of this root, so we don't fight them.
            ApplyPlacement();
            AutoSizeFadeQuad(boundColumn);
            AutoSizeIceStrip(boundColumn);
        }

        /// <summary>
        /// Positions the overlay at the column's piece-centroid, lies it flat on the board, pushes it
        /// toward the camera, then applies the manual <see cref="overlayOffset"/>. Shared by AttachToColumn
        /// (initial) and LateUpdate (live), so placement is consistent and Inspector tweaks are live.
        /// </summary>
        void ApplyPlacement()
        {
            if (boundColumn == null) return;
            transform.position = ComputeColumnPieceCentroidWorld(boundColumn);
            AlignRotation(boundColumn);
            PushTowardCamera();
            // Apply the manual nudge in the COLUMN's local space (NOT world): the board is tilted ≈90°, so
            // a world offset's Y would push the overlay into screen-depth (invisible). In column space the
            // convention matches the rest of the board (Vector3.up = up-screen): X = left/right, Y = up/down
            // on screen, Z = depth. TransformVector includes the board scale so the nudge stays proportional.
            transform.position += boundColumn.transform.TransformVector(overlayOffset);
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
            ApplyPlacement();   // position + rotation + camera push + manual overlayOffset
            // Reset child transforms BEFORE auto-sizing. The root's orientation is the ONLY transform we
            // want applied to the children; any prefab-authored offset/rotation/scale would compound with
            // the board tilt and misplace them. For the ice strip the SCALE reset is critical: AutoSizeIceStrip
            // sets SpriteRenderer.size in LOCAL units (dividing world extent by the strip's lossyScale), so a
            // leftover prefab localScale ≠ 1 makes the strip come out too small/large. With localScale = 1 its
            // lossyScale is just the board/column scale, which the division cancels → the strip fits the column.
            if (iceStrip != null)
            {
                iceStrip.transform.localPosition = Vector3.zero;
                iceStrip.transform.localRotation = Quaternion.identity;
                iceStrip.transform.localScale    = Vector3.one;
            }
            if (fadeQuad != null)
            {
                fadeQuad.transform.localPosition = Vector3.zero;
                fadeQuad.transform.localRotation = Quaternion.identity;
            }

            AutoSizeFadeQuad(col);
            AutoSizeIceStrip(col);
            if (thresholdLabel != null)
            {
                // DON'T force the label's transform — the designer positions/rotates it by hand in the prefab
                // (as a child of this overlay root), and it follows the root automatically. Code only ensures
                // it renders ON TOP of the ice. (Earlier we snapped it to labelLocalPosition every frame, which
                // is why moving it had no effect.)
                // Make the number draw on top of the fade cover (TMP is initialized by now).
                ConfigureLabelOnTop();
            }
        }

        /// <summary>
        /// Pushes the current "remaining to unfreeze" count to the label — for Break Wall = how many
        /// adjacent neighbors are still unsolved (2/1→0), for Lock Color = threshold − matching completions.
        /// GameManager computes it and calls this each time a column locks. Clamped ≥ 0.
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
        /// <summary>
        /// Measures the column's WORLD-space extent from its pieces: height = first→last piece-center
        /// distance + one piece-slot of padding (covers the top/bottom edges); width = widest piece.
        /// Returns false if the column has no pieces. Shared by the fade Quad and the ice-strip sizing.
        /// </summary>
        static bool MeasureColumnExtent(Column col, out float worldWidth, out float worldHeight)
        {
            worldWidth = worldHeight = 0f;
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
            if (count == 0) return false;

            float spanCenters = Vector3.Distance(topPos, bottomPos);
            float perPiece    = (count > 1) ? spanCenters / (count - 1) : maxPieceWidth;
            worldHeight = spanCenters + perPiece;
            worldWidth  = maxPieceWidth;
            return true;
        }

        /// <summary>
        /// Sizes the 9-sliced / tiled ice-strip SpriteRenderer to cover the column. Uses SpriteRenderer.size
        /// (NOT transform scale), so with Draw Mode = Sliced/Tiled only the sprite's middle stretches/tiles
        /// and the 9-slice caps stay crisp at any column length — no stretch distortion. Auto-fits every
        /// column with zero per-level tuning. No-op if no ice strip is assigned.
        /// </summary>
        void AutoSizeIceStrip(Column col)
        {
            if (iceStrip == null) return;

            // CRITICAL: SpriteRenderer.size (set below to fit the column) is IGNORED in Simple draw mode —
            // the sprite would render at its native size/aspect instead (the "narrow capsule that overshoots
            // the column" bug). Force it off Simple so the auto-fit takes effect. We pick Sliced (the 9-slice
            // caps stay crisp, middle stretches); if the designer deliberately set Tiled, we leave that.
            if (iceStrip.drawMode == SpriteDrawMode.Simple)
                iceStrip.drawMode = SpriteDrawMode.Sliced;

            if (!MeasureColumnExtent(col, out float worldWidth, out float worldHeight)) return;

            // SpriteRenderer.size is in the renderer's local units → divide world extent by its lossy scale.
            float lx = Mathf.Abs(iceStrip.transform.lossyScale.x); if (lx < 1e-4f) lx = 1f;
            float ly = Mathf.Abs(iceStrip.transform.lossyScale.y); if (ly < 1e-4f) ly = 1f;
            iceStrip.size = new Vector2(
                (worldWidth  / lx) * fadeCoverScale.x,
                (worldHeight / ly) * fadeCoverScale.y);
        }

        void AutoSizeFadeQuad(Column col)
        {
            if (fadeQuad == null) return;
            if (!MeasureColumnExtent(col, out float worldWidth, out float worldHeight)) return;

            // Convert world units to local (divide by the Quad PARENT's lossy scale so localScale × lossy
            // = world size). Uniform-scale assumption — non-uniform board scale may look slightly off.
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
