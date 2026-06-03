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

        public bool IsLocked { get; private set; }
        public event Action<Column> Locked;

        // Read-only access for the animation system in PlayerHand (which needs to know
        // where each slot lives in local space to lerp pieces between slots).
        public Vector3 LayoutDirection => layoutDirection;

        // Last-resort fallback when no PrefabRegistry entry pushes a runtime override. Designers
        // are expected to tune pieceSpacing per-prefab via PrefabRegistry.entries[*].pieceSpacing,
        // not here. This const exists so a column without a registry-driven override still lays out
        // SOMETHING reasonable instead of stacking every piece at y=0.
        const float DEFAULT_PIECE_SPACING = 1.1f;

        // Non-serialized runtime override applied by LevelLoader from the active PrefabRegistry entry.
        // <= 0 means "no override active" → PieceSpacing falls back to DEFAULT_PIECE_SPACING.
        [System.NonSerialized] float runtimePieceSpacing = 0f;

        /// <summary>Live spacing for layout — runtime override if set, otherwise the internal fallback const.</summary>
        public float PieceSpacing => runtimePieceSpacing > 0f ? runtimePieceSpacing : DEFAULT_PIECE_SPACING;

        /// <summary>
        /// Applies a per-level cell-height override from the active PrefabRegistry entry.
        /// Pass 0 (or negative) to clear the override and fall back to the internal default.
        /// </summary>
        public void SetRuntimePieceSpacing(float spacing)
        {
            runtimePieceSpacing = spacing;
            Layout();
        }

        void Start()
        {
            Layout();
            EvaluateLock();
        }

        void OnValidate() => Layout();
        void OnTransformChildrenChanged() { if (!Application.isPlaying) Layout(); }

        public void Layout()
        {
            float spacing = PieceSpacing;
            int slot = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                var p = transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                p.transform.localPosition = layoutDirection * (slot * spacing);
                // Use the piece's RestRotation (captured from its prefab's authored localRotation)
                // instead of forcing identity. Lets prefabs like Card.fbx — whose mesh isn't oriented
                // at identity-faces-camera — stay upright when laid out.
                p.transform.localRotation = p.RestRotation;
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
        /// rows of the bottom of the column. Instant — for the animated path, call
        /// <see cref="FindQuestionmarksToReveal"/> first and run AnimateRevealHop on each piece.
        /// </summary>
        public void CheckRevealQuestionmarks(int revealFromBottom)
        {
            foreach (var p in FindQuestionmarksToReveal(revealFromBottom)) p.Reveal();
        }

        /// <summary>
        /// Returns every Questionmark piece in this column whose slot is within
        /// <paramref name="revealFromBottom"/> rows of the bottom AND that hasn't been revealed yet.
        /// Use this when you want to animate the reveal — call AnimateRevealHop on each, then the
        /// usual CheckRevealQuestionmarks call afterward is a no-op for already-revealed pieces.
        /// </summary>
        public List<Piece> FindQuestionmarksToReveal(int revealFromBottom)
        {
            var result = new List<Piece>();
            int total = 0;
            for (int i = 0; i < transform.childCount; i++)
                if (transform.GetChild(i).GetComponent<Piece>() != null) total++;
            if (total == 0) return result;

            int slot = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                var p = transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                if (p.IsQuestionmark && !p.IsRevealed)
                {
                    int slotsFromBottom = (total - 1) - slot;
                    if (slotsFromBottom < revealFromBottom) result.Add(p);
                }
                slot++;
            }
            return result;
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
        /// spins around world-Y, and lands back in slot. Runs all pieces in parallel.
        /// All three knobs are designer-tunable from PlayerHand's celebration fields.
        /// </summary>
        public IEnumerator AnimateCelebration(float duration = 0.4f, float hopDistance = 0.6f, float totalRotationDegrees = 360f)
        {
            // "Up the column" in local space is the reverse of layoutDirection (pieces stack downward by default).
            Vector3 hopDirLocal = -layoutDirection.normalized;
            // Vector3.zero = "let each piece use its own transform.up" — gives a clean spin around the
            // piece's authored vertical instead of a fixed world axis. Avoids the pendulum wobble you'd
            // see from forcing world Y through a piece that's tilted by board rotation.
            Vector3 flipAxis = Vector3.zero;

            var anims = new List<Coroutine>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var piece = transform.GetChild(i).GetComponent<Piece>();
                if (piece == null) continue;
                anims.Add(StartCoroutine(piece.AnimateCelebrate(duration, hopDistance, hopDirLocal, flipAxis, totalRotationDegrees)));
            }

            // Wait for the slowest piece (they all share the same duration so this is equivalent
            // to waiting for any of them, but iterating is safer if you later vary per-piece timing).
            foreach (var co in anims) yield return co;
        }
    }
}
