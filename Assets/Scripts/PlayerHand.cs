using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    public class PlayerHand : MonoBehaviour
    {
        public static PlayerHand Instance { get; private set; }

        [SerializeField] private Transform handAnchor;
        [SerializeField] private Piece heldPiece;

        [Header("Animation")]
        [Tooltip("Master toggle. If off, moves resolve instantly (the old behaviour) — useful for debugging logic.")]
        [SerializeField] private bool useAnimations = true;
        [Tooltip("Phase 1: held piece flies from hand to top slot of the clicked column.")]
        [SerializeField] private float heldToTopDuration = 0.25f;
        [Tooltip("Phase 2: existing column pieces shift down AND the bottom piece flies to hand (in parallel).")]
        [SerializeField] private float shiftAndPopDuration = 0.15f;
        [Tooltip("Small pause between a successful move and the column-complete celebration, so the lock reads as 'earned'.")]
        [SerializeField] private float celebrationDelay = 0.1f;
        [Tooltip("How many clicks can be queued while an animation is playing.")]
        [SerializeField] private int maxQueuedClicks = 2;

        public Piece HeldPiece => heldPiece;
        public Transform HandAnchor => handAnchor;

        // Snapshot of the last move so the Rewind skill can reverse it.
        Column lastMoveColumn;
        bool   lastMoveLockedColumn;

        // Animation gating: while true, new clicks are queued instead of executed.
        bool isAnimating;
        readonly Queue<Transform> clickQueue = new Queue<Transform>();

        public bool CanUndo => lastMoveColumn != null;
        public bool IsAnimating => isAnimating;

        /// <summary>Fired whenever the hand state changes (a move was made or undone).</summary>
        public event System.Action StateChanged;

        /// <summary>
        /// Assigns a piece as the held piece, parents it under the HandAnchor, and resets local position.
        /// Pass null to clear (e.g. when rebuilding the board between levels).
        /// </summary>
        public void SetHeldPiece(Piece p)
        {
            if (p == null) { heldPiece = null; return; }
            PlaceInHand(p);
        }

        void Awake()
        {
            Instance = this;
            if (handAnchor == null) handAnchor = transform;
            if (heldPiece != null) PlaceInHand(heldPiece);
        }

        void Start()
        {
            // Initial sink check — for cases where a level starts with a near-complete column
            // and the player's starting held piece is the matching color.
            UpdateRainbowSinkOpportunities();
        }

        public void HandleColumnClick(Transform column)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            if (column == null || column == handAnchor) return;

            var col = column.GetComponent<Column>();
            if (col == null || col.IsLocked) return;

            // Click queue: if we're mid-animation, hold this click for after.
            // (When the queued click fires, heldPiece will already have been refreshed
            // by the previous turn's pop-to-hand phase.)
            if (isAnimating)
            {
                if (clickQueue.Count < maxQueuedClicks) clickQueue.Enqueue(column);
                return;
            }

            if (heldPiece == null) return;

            if (useAnimations) StartCoroutine(DoMoveAnimated(col));
            else               DoMoveInstant(col);
        }

        // ---------------------------------------------------------------------
        //  Move resolution paths. Both produce the same end state — only timing differs.
        // ---------------------------------------------------------------------

        /// <summary>Original instant behaviour, kept as a fallback when useAnimations is off.</summary>
        void DoMoveInstant(Column col)
        {
            var pieces = SnapshotColumnPieces(col.transform);
            Piece toInsert = heldPiece;
            heldPiece = null;

            toInsert.transform.SetParent(col.transform, worldPositionStays: false);
            toInsert.transform.SetSiblingIndex(0);

            if (pieces.Count > 0) PlaceInHand(pieces[pieces.Count - 1]);

            col.Layout();
            FinalizeMove(col);
        }

        /// <summary>
        /// Animated swap. All three motions run IN PARALLEL starting at t=0 so the column "makes
        /// room" before the held piece lands (no visual collision at the top slot):
        ///   • Held flies from hand → top slot of column      (heldToTopDuration, ease-out)
        ///   • Remaining pieces slide down one slot           (shiftAndPopDuration, smoothstep)
        ///   • Old-bottom piece flies from column → hand      (shiftAndPopDuration, ease-out-back)
        /// With defaults (0.25s / 0.15s), the shift+pop finishes first, then the held piece arrives
        /// into the now-empty slot 0. Total turn time = max of the durations = 0.25s.
        ///
        /// After the column lock check, if the column just locked we wait celebrationDelay and
        /// then play the celebration. Logic state (parent/sibling reassignments, sink checks,
        /// lock eval) updates EAGERLY at the start so the game state machine never lags behind visuals.
        /// </summary>
        IEnumerator DoMoveAnimated(Column col)
        {
            isAnimating = true;

            // --- snapshot + logical reassignment -----------------------------
            var pieces = SnapshotColumnPieces(col.transform);
            Piece toInsert = heldPiece;
            Piece ejected = pieces.Count > 0 ? pieces[pieces.Count - 1] : null;

            heldPiece = null;

            // Reparent the held piece into the column WITHOUT visually snapping (worldPositionStays: true).
            // Its localPosition becomes whatever offset lines up with its current world position;
            // the coroutine will lerp it toward slot 0 (Vector3.zero in column local space).
            toInsert.transform.SetParent(col.transform, worldPositionStays: true);
            toInsert.transform.SetSiblingIndex(0);

            // Reparent the ejected piece to the hand up-front so the pop coroutine can lerp localPos
            // from wherever-it-was straight to Vector3.zero (the hand center).
            if (ejected != null)
                ejected.transform.SetParent(handAnchor, worldPositionStays: true);

            Vector3 layoutDir = col.LayoutDirection;
            float spacing = col.PieceSpacing;

            // --- Kick off all motions concurrently --------------------------
            var animations = new List<Coroutine>();

            // Held → top slot.
            animations.Add(StartCoroutine(toInsert.AnimateLocalTo(
                Vector3.zero, Quaternion.identity, heldToTopDuration, Easing.EaseOut)));

            // Every piece except the ejected one shifts down one slot. pieces[i] was at slot i
            // pre-move, so now (with the new piece at sibling 0) it belongs at slot i+1.
            for (int i = 0; i < pieces.Count - 1; i++)
            {
                var p = pieces[i];
                Vector3 newSlot = layoutDir * ((i + 1) * spacing);
                animations.Add(StartCoroutine(p.AnimateLocalTo(
                    newSlot, Quaternion.identity, shiftAndPopDuration, Easing.SmoothStep)));
            }

            // Old bottom piece → hand center.
            if (ejected != null)
            {
                animations.Add(StartCoroutine(ejected.AnimateLocalTo(
                    Vector3.zero, Quaternion.identity, shiftAndPopDuration, Easing.EaseOutBack)));
            }

            // Wait for the slowest coroutine to finish (typically heldToTop @ 0.25s).
            foreach (var co in animations) yield return co;

            // --- finalize logic ---------------------------------------------
            if (ejected != null) heldPiece = ejected; // Already reparented above; just assign the reference.
            FinalizeMove(col);

            // --- PHASE 3: celebration if the column locked -------------------
            if (col.IsLocked)
            {
                if (celebrationDelay > 0f) yield return new WaitForSeconds(celebrationDelay);
                yield return StartCoroutine(col.AnimateCelebration());
            }

            isAnimating = false;

            // Drain one queued click if any. Recursion is shallow (always re-gates via isAnimating).
            if (clickQueue.Count > 0)
            {
                var next = clickQueue.Dequeue();
                HandleColumnClick(next);
            }
        }

        /// <summary>Snapshots column children into a list of Pieces in their current sibling order.</summary>
        static List<Piece> SnapshotColumnPieces(Transform column)
        {
            var pieces = new List<Piece>(column.childCount);
            for (int i = 0; i < column.childCount; i++)
            {
                var p = column.GetChild(i).GetComponent<Piece>();
                if (p != null) pieces.Add(p);
            }
            return pieces;
        }

        /// <summary>Common post-move logic: reveal Q?, lock eval, move counter, sink check, notify.</summary>
        void FinalizeMove(Column col)
        {
            int revealThreshold = 2;
            if (LevelLoader.Instance != null && LevelLoader.Instance.CurrentLevel != null)
                revealThreshold = LevelLoader.Instance.CurrentLevel.questionmarkRevealFromBottom;
            col.CheckRevealQuestionmarks(revealThreshold);

            col.EvaluateLock();

            lastMoveColumn = col;
            lastMoveLockedColumn = col.IsLocked;

            GameManager.Instance?.NotifyMoveMade();

            // After every swap the held piece may have changed, so re-scan every column
            // to see if any rainbows should sink.
            UpdateRainbowSinkOpportunities();

            StateChanged?.Invoke();
        }

        /// <summary>
        /// Reverses the most recent move: the currently-held piece goes back to the BOTTOM of
        /// the last-clicked column, and that column's TOP piece becomes the new held piece.
        /// If the column got locked by the previous move, it's unlocked again.
        /// </summary>
        public void Undo()
        {
            if (isAnimating) return; // Don't unwind partway through an animated move.
            if (!CanUndo) return;
            if (heldPiece == null) return;
            if (lastMoveColumn == null || lastMoveColumn.transform.childCount == 0) return;

            // The top of the column right now is the piece that was originally held.
            var topChild = lastMoveColumn.transform.GetChild(0);
            var topPiece = topChild != null ? topChild.GetComponent<Piece>() : null;
            if (topPiece == null) return;

            // 1. Unlock first so colliders re-enable and reparent works cleanly.
            if (lastMoveLockedColumn) lastMoveColumn.Unlock();

            // 2. Push the currently-held piece back to the BOTTOM of the column.
            var heldNow = heldPiece;
            heldPiece = null;
            heldNow.transform.SetParent(lastMoveColumn.transform, worldPositionStays: false);
            heldNow.transform.SetSiblingIndex(lastMoveColumn.transform.childCount - 1);

            // 3. Pull the column's top piece back into the hand.
            PlaceInHand(topPiece);

            // 4. Repaint positions and refund the move with GameManager.
            lastMoveColumn.Layout();
            GameManager.Instance?.RefundMove();

            // Clear so the player can't undo the same move twice.
            lastMoveColumn = null;
            lastMoveLockedColumn = false;

            // Held piece changed after undo — recheck sink opportunities.
            UpdateRainbowSinkOpportunities();

            StateChanged?.Invoke();
        }

        /// <summary>
        /// Scans every unlocked column and, if it's one swap away from completion with the
        /// currently-held color, sinks its Rainbow(s) to the bottom so the player can finish next click.
        /// </summary>
        public void UpdateRainbowSinkOpportunities()
        {
            if (heldPiece == null || heldPiece.IsRainbow) return;
            if (GameManager.Instance == null) return;

            var heldColor = heldPiece.Color;
            var cols = GameManager.Instance.Columns;
            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                if (c == null || c.IsLocked) continue;
                if (c.ShouldSinkRainbow(heldColor))
                {
                    c.MoveRainbowsToBottom();
                    c.Layout();
                }
            }
        }

        void PlaceInHand(Piece p)
        {
            p.transform.SetParent(handAnchor, worldPositionStays: false);
            p.transform.localPosition = Vector3.zero;
            p.transform.localRotation = Quaternion.identity;
            // Intentionally do NOT touch localScale — preserve the prefab's scale.
            heldPiece = p;
        }
    }
}
