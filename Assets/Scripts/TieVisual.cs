using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Runtime visual for a tie binding two pieces in adjacent columns. The prefab has two
    /// Quad children (one for the "tie up" half, one for the "tie down" half) that together
    /// form the X-shape between the two tied pieces.
    ///
    /// Position is updated every LateUpdate to the midpoint of the bound pieces' world positions,
    /// so the visual follows both pieces during animation (tied shifts, magnet gathers, etc.)
    /// without needing to be re-parented or re-laid-out manually.
    ///
    /// Phase A: static display only — both halves are intact and the visual is destroyed
    /// when either bound piece is destroyed. Crack animation + fade-out (when the tie breaks
    /// at the bottom row) are added in Phase C.
    /// </summary>
    public class TieVisual : MonoBehaviour, IBondVisual
    {
        [Tooltip("Upper half of the X — typically a Quad child with material mat_tie_up.mat. " +
                 "On tie break (Phase C), this swaps to tieUpCrackMat and fades alpha 1→0 over " +
                 "the configured fade duration before the visual is destroyed.")]
        [SerializeField] private MeshRenderer tieUpQuad;

        [Tooltip("Lower half of the X — typically a Quad child with material mat_tie_down.mat. " +
                 "Same break behavior as the upper half via tieDownCrackMat.")]
        [SerializeField] private MeshRenderer tieDownQuad;

        [Tooltip("Cracked variant of the upper material — swapped in at the start of CrackAndFade. " +
                 "Leave null to skip the visual swap (only alpha fade plays).")]
        [SerializeField] private Material tieUpCrackMat;

        [Tooltip("Cracked variant of the lower material. Leave null to skip the swap.")]
        [SerializeField] private Material tieDownCrackMat;

        [Tooltip("How far to push the tie toward the camera, in world units, so it renders ON TOP of " +
                 "the pieces it binds instead of being clipped behind them. 0.5 is enough for most camera " +
                 "setups; increase if the tie still appears behind, decrease if it floats too far above " +
                 "the board plane. Set 0 to disable the auto-offset (renders co-planar with pieces).")]
        [SerializeField] private float cameraOffset = 0.5f;

        [Tooltip("If true, every LateUpdate the visual rotates to face Camera.main so the Quad children " +
                 "are seen face-on regardless of board tilt or prefab authored rotation. Designer's per-quad " +
                 "local rotations (e.g. ±15° to make the X shape) are preserved — only the ROOT rotation is " +
                 "overridden. Disable if you want full manual control of the tie's orientation.")]
        [SerializeField] private bool faceCamera = true;

        [Tooltip("Tie HORIZONTAL scale as a fraction of the SPAN between the two tied pieces' outer edges " +
                 "(distance between centers + each piece's half-width, measured in world units).\n" +
                 "1.0 = X spans both pieces exactly edge-to-edge. 0.8 = 20% inset on each side (default — " +
                 "tie sits snugly INSIDE the pair, leaving a small visual margin so it doesn't look like " +
                 "it's overflowing). 1.1 = slightly past edges (emphatic).\n" +
                 "Recomputed every LateUpdate so the tie adapts to per-level auto-fit automatically.")]
        [SerializeField, Range(0.1f, 2f)] private float tieWidthPercent = 0.8f;

        [Tooltip("Tie VERTICAL scale as a fraction of the same pair span. Decoupled from width so a " +
                 "designer can flatten the X — e.g. (Width 0.9, Height 0.5) for a wide thin band across " +
                 "wide pieces, or (Width 0.7, Height 0.9) for a vertical X on tall pieces. 1.0 = same scale " +
                 "as width.")]
        [SerializeField, Range(0.1f, 2f)] private float tieHeightPercent = 0.8f;

        Piece pieceA;
        Piece pieceB;
        // Once break starts, we stop tracking the pieces (which are about to fly apart to hand /
        // top of column) and freeze the visual in place while it fades out. Without this freeze the
        // tie would visibly stretch across the screen during the 0.3s fade.
        bool frozen;
        MaterialPropertyBlock fadeMpb;

        // Overlay rendering (always-on-top) is handled by the shader itself — Sort/TieOverlay
        // bakes ZTest Always + ZWrite Off + Queue Overlay into the pass directive. No runtime
        // material configuration needed. See Assets/Shaders/TieOverlay.shader for details, and
        // make sure all 4 tie materials (up, down, up_crack, down_crack) use that shader.

        public Piece PieceA => pieceA;
        public Piece PieceB => pieceB;

        // --- IBondVisual (a tie always spans exactly its two pieces) ---
        public IReadOnlyList<Piece> Pieces => new[] { pieceA, pieceB };
        public bool Covers(Piece p) => p != null && (p == pieceA || p == pieceB);
        public void Bind(IReadOnlyList<Piece> pieces)
        {
            Bind(pieces != null && pieces.Count > 0 ? pieces[0] : null,
                 pieces != null && pieces.Count > 1 ? pieces[1] : null);
        }

        /// <summary>
        /// Binds this visual to the two tied pieces. Must be called by LevelLoader right after
        /// Instantiate so LateUpdate has valid references on the first frame. Both pieces' own
        /// <see cref="Piece.SetTiedPartner"/> should also be wired by the caller — this method
        /// only configures the visual side.
        /// </summary>
        public void Bind(Piece a, Piece b)
        {
            pieceA = a;
            pieceB = b;
            // Snap to the right position immediately so we don't render at the origin for one frame
            // before LateUpdate kicks in.
            UpdateTransform();
        }

        // Cached so we only re-run the (LookRotation + 2× collider read + scale) UpdateTransform while
        // something actually moved. Pieces are static except during drops/shifts and the camera is fixed
        // during play, so most frames early-out here doing almost nothing.
        Vector3 lastPosA, lastPosB, lastCamPos;
        Quaternion lastCamRot;
        bool hasLastTransform;

        void LateUpdate()
        {
            // Once frozen (break in progress), don't follow pieces — they're separating dramatically.
            if (frozen) return;
            // Destroy the visual if either bound piece is gone (e.g. board torn down between levels).
            if (pieceA == null || pieceB == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 pa = pieceA.transform.position, pb = pieceB.transform.position;
            Camera cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
            Quaternion cr = cam != null ? cam.transform.rotation : Quaternion.identity;
            if (hasLastTransform && pa == lastPosA && pb == lastPosB && cp == lastCamPos && cr == lastCamRot)
                return;   // pieces + camera unchanged → nothing to recompute

            UpdateTransform();
            lastPosA = pa; lastPosB = pb; lastCamPos = cp; lastCamRot = cr; hasLastTransform = true;
        }

        /// <summary>
        /// Tie break animation (Phase C). Freezes position so the visual stays where the break
        /// happened, swaps both quad materials to their cracked variants, then fades alpha 1→0
        /// via MaterialPropertyBlock and destroys the GameObject. Called by PlayerHand when a
        /// tied pair reaches the bottom row of their columns.
        /// Caller is responsible for clearing <see cref="Piece.SetTiedPartner"/> on both bound
        /// pieces — this method only handles the visual side.
        /// </summary>
        /// <summary><see cref="IBondVisual"/> entry point — the tie's break IS its crack+fade.</summary>
        public IEnumerator PlayBreak(float duration) => CrackAndFade(duration);

        public IEnumerator CrackAndFade(float duration)
        {
            frozen = true;

            // Swap to crack variants. They also use the Sort/TieOverlay shader so overlay
            // rendering carries through the fade — no per-material setup here.
            if (tieUpQuad != null && tieUpCrackMat != null) tieUpQuad.sharedMaterial = tieUpCrackMat;
            if (tieDownQuad != null && tieDownCrackMat != null) tieDownQuad.sharedMaterial = tieDownCrackMat;

            if (fadeMpb == null) fadeMpb = new MaterialPropertyBlock();

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                float alpha = 1f - u;
                SetAlpha(tieUpQuad, alpha);
                SetAlpha(tieDownQuad, alpha);
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>Sets the renderer's <c>_BaseColor.a</c> via MPB without touching the shared material.</summary>
        void SetAlpha(MeshRenderer r, float a)
        {
            if (r == null) return;
            r.GetPropertyBlock(fadeMpb);
            fadeMpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, a));
            r.SetPropertyBlock(fadeMpb);
        }

        /// <summary>
        /// Positions the visual at the midpoint of the two bound pieces in WORLD space,
        /// pushes it slightly TOWARD the camera so it renders on top of the pieces (not clipped
        /// behind), rotates it to FACE the camera so the X-shape sprites are never edge-on
        /// regardless of board tilt or prefab authored rotation, AND scales it to span the full
        /// width of both pieces edge-to-edge — the X "locks" the pair visually.
        /// </summary>
        void UpdateTransform()
        {
            Vector3 midpoint = (pieceA.transform.position + pieceB.transform.position) * 0.5f;

            Camera cam = Camera.main;
            if (cam != null)
            {
                // Direction from midpoint toward the camera, normalized. Used for both the Z-order
                // push (offset along this direction) AND the LookRotation (Quad normals face this way).
                Vector3 toCamera = cam.transform.position - midpoint;
                float dist = toCamera.magnitude;
                if (dist > 1e-4f)
                {
                    Vector3 toCameraDir = toCamera / dist;
                    if (cameraOffset > 0f) midpoint += toCameraDir * cameraOffset;
                    if (faceCamera)
                    {
                        // LookRotation makes the GameObject's local +Z axis point along the given
                        // forward vector. Quad's default visible face IS the local +Z side, so we
                        // want +Z to point TOWARD the camera (the camera sees the front of the quad).
                        transform.rotation = Quaternion.LookRotation(toCameraDir, cam.transform.up);
                    }
                }
            }

            transform.position = midpoint;

            // ---- Scale to span both pieces (snug fit by default) ----
            // worldSpan = (distance between piece CENTERS) + (each piece's half-width).
            // Half-widths use BoxCollider.size.x × lossyScale.x so they reflect each piece's
            // current world-space extent (auto-fit + PrefabRegistry pieceScale both factored in).
            // X and Y scale percents are independent so a designer can stretch the X-shape's
            // aspect ratio (e.g. flatter horizontally, taller vertically) without per-level math.
            // localScale is set as worldSpan × percent / parent.lossyScale so the tie's WORLD
            // scale ends up exactly worldSpan × percent regardless of where the tie is parented.
            float halfWidthA = GetPieceHalfWidthWorld(pieceA);
            float halfWidthB = GetPieceHalfWidthWorld(pieceB);
            float centerDist = Vector3.Distance(pieceA.transform.position, pieceB.transform.position);
            float worldSpan = centerDist + halfWidthA + halfWidthB;

            float parentScale = transform.parent != null ? Mathf.Abs(transform.parent.lossyScale.x) : 1f;
            if (parentScale < 1e-4f) parentScale = 1f;
            float baseScale = worldSpan / parentScale;
            // Z follows X (the quad is flat — depth doesn't render, but match X to keep the
            // GameObject visually proportional in Scene gizmos / bounds preview).
            transform.localScale = new Vector3(
                baseScale * tieWidthPercent,
                baseScale * tieHeightPercent,
                baseScale * tieWidthPercent);
        }

        /// <summary>Half of the piece's world-space width along X, computed from its BoxCollider.size and lossyScale.</summary>
        static float GetPieceHalfWidthWorld(Piece p)
        {
            if (p == null) return 0f;
            var bc = p.GetComponent<BoxCollider>();
            if (bc == null) return 0f;
            return Mathf.Abs(bc.size.x) * 0.5f * Mathf.Abs(p.transform.lossyScale.x);
        }
    }
}
