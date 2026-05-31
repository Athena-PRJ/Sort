using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sort
{
    [DisallowMultipleComponent]
    public class Column : MonoBehaviour
    {
        [Tooltip("Direction each subsequent piece moves from the top, in local space.")]
        [SerializeField] private Vector3 layoutDirection = new Vector3(0, -1, 0);

        [Tooltip("Distance between piece centers along the layout direction.")]
        [SerializeField] private float pieceSpacing = 1.1f;

        public bool IsLocked { get; private set; }
        public event Action<Column> Locked;

        // Read-only access for the animation system in PlayerHand (which needs to know
        // where each slot lives in local space to lerp pieces between slots).
        public Vector3 LayoutDirection => layoutDirection;
        public float   PieceSpacing    => pieceSpacing;

        void Start()
        {
            Layout();
            EvaluateLock();
        }

        void OnValidate() => Layout();
        void OnTransformChildrenChanged() { if (!Application.isPlaying) Layout(); }

        public void Layout()
        {
            int slot = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                var p = transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                p.transform.localPosition = layoutDirection * (slot * pieceSpacing);
                p.transform.localRotation = Quaternion.identity;
                slot++;
            }
        }

        /// <summary>
        /// True if all pieces share the same color AND no Rainbow or hidden Questionmark is present.
        /// Rainbows must be ejected (replaced by a real piece) before a column can lock.
        /// </summary>
        public bool IsMonoColor()
        {
            bool seenAny = false;
            PieceColor first = default;
            for (int i = 0; i < transform.childCount; i++)
            {
                var p = transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                if (p.IsRainbow) return false;                        // Rainbow must be ejected for lock.
                if (p.IsQuestionmark && !p.IsRevealed) return false;  // Hidden identity blocks mono-check.
                if (!seenAny) { first = p.Color; seenAny = true; }
                else if (p.Color != first) return false;
            }
            return seenAny;
        }

        /// <summary>
        /// True when (a) every non-Rainbow piece in this column matches <paramref name="heldColor"/>,
        /// (b) at least one Rainbow is above a real piece, and (c) no Questionmark is still hidden.
        /// In that case, the player is one ejection-swap away from completing the column,
        /// so the Rainbow should sink to the bottom to enable it.
        /// </summary>
        public bool ShouldSinkRainbow(PieceColor heldColor)
        {
            bool seenAny = false;
            int firstRainbowSlot = -1;
            int lastNonRainbowSlot = -1;
            int slot = 0;

            for (int i = 0; i < transform.childCount; i++)
            {
                var p = transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;

                if (p.IsRainbow)
                {
                    if (firstRainbowSlot == -1) firstRainbowSlot = slot;
                }
                else
                {
                    if (p.IsQuestionmark && !p.IsRevealed) return false; // can't determine match yet
                    if (p.Color != heldColor) return false;              // a real piece doesn't match held
                    seenAny = true;
                    lastNonRainbowSlot = slot;
                }

                slot++;
            }

            if (firstRainbowSlot == -1) return false; // no rainbow present
            if (!seenAny) return false;                // column is all rainbows, undefined
            return lastNonRainbowSlot > firstRainbowSlot;
        }

        /// <summary>
        /// Reveals any Questionmark pieces whose slot is within <paramref name="revealFromBottom"/>
        /// rows of the bottom of the column.
        /// </summary>
        public void CheckRevealQuestionmarks(int revealFromBottom)
        {
            // Count total piece slots.
            int total = 0;
            for (int i = 0; i < transform.childCount; i++)
                if (transform.GetChild(i).GetComponent<Piece>() != null) total++;
            if (total == 0) return;

            int slot = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                var p = transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                if (p.IsQuestionmark && !p.IsRevealed)
                {
                    int slotsFromBottom = (total - 1) - slot;
                    if (slotsFromBottom < revealFromBottom) p.Reveal();
                }
                slot++;
            }
        }

        /// <summary>Moves every Rainbow piece to the bottom of the column (in their original relative order).</summary>
        public void MoveRainbowsToBottom()
        {
            var nonRainbow = new List<Transform>();
            var rainbows = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var t = transform.GetChild(i);
                var p = t.GetComponent<Piece>();
                if (p == null) continue;
                if (p.IsRainbow) rainbows.Add(t);
                else nonRainbow.Add(t);
            }

            int idx = 0;
            foreach (var t in nonRainbow) t.SetSiblingIndex(idx++);
            foreach (var t in rainbows)   t.SetSiblingIndex(idx++);
        }

        public void EvaluateLock()
        {
            if (IsLocked) return;
            if (!IsMonoColor()) return;

            IsLocked = true;
            for (int i = 0; i < transform.childCount; i++)
            {
                var col = transform.GetChild(i).GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }
            Locked?.Invoke(this);
        }

        public void Unlock()
        {
            if (!IsLocked) return;
            IsLocked = false;
            for (int i = 0; i < transform.childCount; i++)
            {
                var col = transform.GetChild(i).GetComponent<Collider>();
                if (col != null) col.enabled = true;
            }
        }

        /// <summary>
        /// Plays the column-complete celebration: every child piece hops up the column,
        /// cartwheels around world-Z, and lands back in slot. Runs all pieces in parallel.
        /// </summary>
        public IEnumerator AnimateCelebration(float duration = 0.4f, float hopDistance = 0.6f)
        {
            // "Up the column" in local space is the reverse of layoutDirection (pieces stack downward by default).
            Vector3 hopDirLocal = -layoutDirection.normalized;
            // World Y axis = vertical spin (pieces rotate like a top around world-up).
            Vector3 flipAxis = Vector3.up;

            var anims = new List<Coroutine>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var piece = transform.GetChild(i).GetComponent<Piece>();
                if (piece == null) continue;
                anims.Add(StartCoroutine(piece.AnimateCelebrate(duration, hopDistance, hopDirLocal, flipAxis)));
            }

            // Wait for the slowest piece (they all share the same duration so this is equivalent
            // to waiting for any of them, but iterating is safer if you later vary per-piece timing).
            foreach (var co in anims) yield return co;
        }
    }
}
