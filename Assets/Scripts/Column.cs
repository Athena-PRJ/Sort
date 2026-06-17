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

        // Frozen state (Break Wall Stack mechanic). >0 = frozen with that many OTHER columns
        // remaining to unlock; 0 = not frozen. Disables child colliders so the column can't be
        // tapped / Switch'd / Magnet'd while frozen. Independent from IsLocked — a frozen column
        // cannot lock until it unfreezes (see EvaluateLock guard below).
        [NonSerialized] int frozenUnlockThreshold;

        // Lock Color Stack (sibling of Break Wall Stack). When frozenLockColor is true, only columns
        // locked in frozenRequiredColor count toward frozenUnlockThreshold (instead of ANY locked
        // column). Both are set by Freeze() and cleared by Unfreeze().
        [NonSerialized] bool frozenLockColor;
        [NonSerialized] string frozenRequiredColor;

        /// <summary>True while this column is frozen by a Break Wall Stack / Lock Color Stack gate.</summary>
        public bool IsFrozen => frozenUnlockThreshold > 0;

        /// <summary>How many columns must lock before this one unfreezes (0 if not frozen).</summary>
        public int FrozenUnlockThreshold => frozenUnlockThreshold;

        /// <summary>True if this frozen column uses the Lock Color Stack rule (color-filtered unlock).</summary>
        public bool FrozenLockColor => frozenLockColor;

        /// <summary>The color NAME whose locked columns count toward unlock — only meaningful when <see cref="FrozenLockColor"/>.</summary>
        public string FrozenRequiredColor => frozenRequiredColor;

        // The ORIGINAL (level-authored) freeze spec, captured the first time this column is frozen and
        // RETAINED across Unfreeze. This lets a Rewind re-freeze the column if undoing a lock drops the
        // unlock progress back below the threshold (the live frozen* fields are cleared on Unfreeze, so
        // they can't be used for that). 0 = this column was never authored as a frozen/lock-color column.
        [NonSerialized] int initialFrozenThreshold;
        [NonSerialized] bool initialFrozenLockColor;
        [NonSerialized] string initialFrozenRequiredColor;
        // Break Wall Stack (neighbor mode): unfreezes when its adjacent columns are completed (no threshold).
        // Retained across Unfreeze so a Rewind that re-locks a neighbor can re-freeze this wall.
        [NonSerialized] bool initialFrozenNeighborMode;

        /// <summary>True if this column was authored as a Break Wall / Lock Color column — even if currently unfrozen.</summary>
        public bool WasFrozenColumn => initialFrozenThreshold > 0 || initialFrozenNeighborMode;

        /// <summary>True if the authored freeze was the Break Wall (neighbor-completion) variant.</summary>
        public bool InitialFrozenNeighborMode => initialFrozenNeighborMode;

        /// <summary>The authored unlock threshold (retained across Unfreeze, for Rewind re-freeze).</summary>
        public int InitialFrozenThreshold => initialFrozenThreshold;

        /// <summary>Whether the authored freeze was the Lock Color variant (retained across Unfreeze).</summary>
        public bool InitialFrozenLockColor => initialFrozenLockColor;

        /// <summary>The authored Lock Color required-color NAME (retained across Unfreeze).</summary>
        public string InitialFrozenRequiredColor => initialFrozenRequiredColor;

        /// <summary>Fires whenever the column transitions in or out of the frozen state. UI/overlay listens.</summary>
        public event Action<Column> FrozenChanged;

        // Only Stack Sort mechanic: when true this column accepts ONLY pieces whose color NAME equals
        // onlyStackColor (rainbows are wild). Enforced in PlayerHand.HandleColumnClick. A permanent
        // per-level property — set once at level build.
        [NonSerialized] bool onlyStackSort;
        [NonSerialized] string onlyStackColor;

        /// <summary>True if this column only accepts pieces of <see cref="OnlyStackColor"/> (Only Stack Sort).</summary>
        public bool IsOnlyStackSort => onlyStackSort;

        /// <summary>The single color NAME this column accepts — only meaningful when <see cref="IsOnlyStackSort"/>.</summary>
        public string OnlyStackColor => onlyStackColor;

        /// <summary>
        /// Marks this column as Only Stack Sort: it will accept only pieces of <paramref name="color"/>.
        /// Called by LevelLoader at level build from the ColumnConfig. Permanent for the level.
        /// </summary>
        public void SetOnlyStackSort(string color)
        {
            onlyStackSort = true;
            onlyStackColor = color;
        }

        /// <summary>
        /// True if <paramref name="p"/> may be placed into this column under the Only Stack Sort rule:
        /// always true when the column isn't Only-Stack-Sort, otherwise only for a matching color or a
        /// Rainbow (wild). Null pieces are rejected.
        /// </summary>
        public bool AcceptsPiece(Piece p)
        {
            if (!onlyStackSort) return true;
            if (p == null) return false;
            return p.IsRainbow || p.Color == onlyStackColor;
        }

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
        public bool IsMonoColor() => TryGetMonoColor(out _);

        /// <summary>
        /// Like <see cref="IsMonoColor"/> but also outputs the shared color when true. Returns false
        /// (and <paramref name="color"/> = default) if the column is empty, mixed-color, or still has a
        /// Rainbow / hidden Questionmark. Used by Lock Color Stack to know which color a locked column is.
        /// </summary>
        public bool TryGetMonoColor(out string color)
        {
            color = null;
            bool seenAny = false;
            string first = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                var p = transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                if (p.IsRainbow) return false;                        // Rainbow must be ejected for lock.
                if (p.IsQuestionmark && !p.IsRevealed) return false;  // Hidden identity blocks mono-check.
                if (!seenAny) { first = p.Color; seenAny = true; }
                else if (p.Color != first) return false;
            }
            if (!seenAny) return false;
            color = first;
            return true;
        }

        /// <summary>
        /// Returns the piece(s) that should sink to the BOTTOM so this column is set up to COMPLETE on
        /// the next drop of a <paramref name="heldColor"/> piece — or an empty list if there's no such
        /// opportunity. Both cases require no still-hidden Questionmark and a sink that actually changes
        /// the order (a blocker sitting above a matching piece):
        ///   • Rainbow arrange: every REAL piece already matches heldColor and ≥1 Rainbow sits above a
        ///     real one → all Rainbows sink. (Preserves the original Rainbow-only behaviour.)
        ///   • Single odd piece: no Rainbows, and EXACTLY ONE real piece differs from heldColor while all
        ///     the rest match → that one "odd" piece sinks. (Generalises the helper to plain colours, so
        ///     ANY column that's one piece away from completion auto-arranges, not just rainbow columns.)
        /// Caller animates the move via <see cref="SinkPiecesToBottom"/>.
        /// </summary>
        public List<Piece> GetSinkTargets(string heldColor)
        {
            var result = new List<Piece>();
            if (string.IsNullOrEmpty(heldColor)) return result;

            var pieces = new List<Piece>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var p = transform.GetChild(i).GetComponent<Piece>();
                if (p != null) pieces.Add(p);
            }
            if (pieces.Count == 0) return result;

            var rainbows = new List<Piece>();
            Piece oddReal = null;
            int oddRealCount = 0, matchCount = 0;
            int firstBlockerSlot = -1, lastMatchSlot = -1;

            for (int slot = 0; slot < pieces.Count; slot++)
            {
                var p = pieces[slot];
                if (p.IsQuestionmark && !p.IsRevealed) return result;   // unknown identity → can't decide
                if (p.IsRainbow)
                {
                    rainbows.Add(p);
                    if (firstBlockerSlot < 0) firstBlockerSlot = slot;
                }
                else if (p.Color == heldColor)
                {
                    matchCount++;
                    lastMatchSlot = slot;
                }
                else
                {
                    oddReal = p;
                    oddRealCount++;
                    if (firstBlockerSlot < 0) firstBlockerSlot = slot;
                }
            }

            // Rainbow arrange (original behaviour): all reals match, ≥1 rainbow sits above a real one.
            if (rainbows.Count > 0 && oddRealCount == 0)
            {
                if (matchCount > 0 && lastMatchSlot > firstBlockerSlot) result.AddRange(rainbows);
                return result;
            }

            // Single odd real piece, everything else matches (NEW): sink it unless it's already the bottom piece.
            if (rainbows.Count == 0 && oddRealCount == 1 && matchCount == pieces.Count - 1)
            {
                if (oddReal != pieces[pieces.Count - 1]) result.Add(oddReal);
                return result;
            }

            return result;
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

        /// <summary>
        /// Reorders children so the given <paramref name="targets"/> sit at the BOTTOM of the column
        /// (keeping their current relative order), with every other piece kept on top in its current
        /// order. Generalises the old MoveRainbowsToBottom — driven by <see cref="GetSinkTargets"/> for
        /// both the rainbow case and a single odd-coloured piece. No-op if targets is null/empty.
        /// </summary>
        public void SinkPiecesToBottom(List<Piece> targets)
        {
            if (targets == null || targets.Count == 0) return;
            var set = new HashSet<Piece>(targets);
            var keep = new List<Transform>();
            var sink = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var t = transform.GetChild(i);
                var p = t.GetComponent<Piece>();
                if (p == null) continue;
                if (set.Contains(p)) sink.Add(t); else keep.Add(t);
            }

            int idx = 0;
            foreach (var t in keep) t.SetSiblingIndex(idx++);
            foreach (var t in sink) t.SetSiblingIndex(idx++);
        }

        public void EvaluateLock()
        {
            if (IsLocked) return;
            // Frozen columns can never lock — they're behind a "complete N others first" gate,
            // so they must unfreeze before they're even allowed to be evaluated for win condition.
            if (IsFrozen) return;
            if (!IsMonoColor()) return;
            // Tie binding takes priority over lock — a tied column shouldn't lock while its pieces
            // are still bound to a neighbour, because the binding can drag locked pieces around on
            // a tied shift, which would visually contradict the "locked" state.
            if (HasActiveTie()) return;

            IsLocked = true;
            for (int i = 0; i < transform.childCount; i++)
            {
                var col = transform.GetChild(i).GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }
            Locked?.Invoke(this);
        }

        /// <summary>
        /// Marks the column as frozen behind <paramref name="threshold"/> column unlocks. Interaction
        /// is blocked by the IsFrozen guards (not by disabling colliders) so a tap still registers and
        /// PlayerHand can play reject feedback. Caller (LevelLoader at level build, GameManager on Undo)
        /// is responsible for spawning the matching FrozenOverlay visual.
        /// No-op if threshold ≤ 0 (use Unfreeze for that). Fires <see cref="FrozenChanged"/>.
        ///
        /// Pass <paramref name="lockColor"/> = true (with <paramref name="requiredColor"/>) for the
        /// Lock Color Stack variant, where only columns completed in that color count toward the
        /// threshold. Default (false) = Break Wall Stack: any locked column counts.
        /// </summary>
        public void Freeze(int threshold, bool lockColor = false, string requiredColor = null)
        {
            if (threshold <= 0) return;
            // Capture the authored spec the first time so a later Rewind can re-freeze this column
            // (UpdateFrozenColumns re-applies Freeze with these retained values). The spec is constant
            // for the level, so capturing once is enough — re-freeze calls pass the same values back.
            if (initialFrozenThreshold == 0)
            {
                initialFrozenThreshold = threshold;
                initialFrozenLockColor = lockColor;
                initialFrozenRequiredColor = requiredColor;
            }
            frozenUnlockThreshold = threshold;
            frozenLockColor = lockColor;
            frozenRequiredColor = requiredColor;
            // NOTE: child colliders are intentionally LEFT ENABLED (see below). Every interaction path already
            // guards on IsFrozen (HandleColumnClick / Switch / Magnet / tied-chain / rainbow-sink), so
            // the move is blocked there — and keeping colliders enabled lets a tap on a frozen column
            // register so PlayerHand can play the "rejected" shake feedback. (Lock, by contrast, DOES
            // disable colliders since a completed column should be fully inert.)
            FrozenChanged?.Invoke(this);
        }

        /// <summary>
        /// Break Wall Stack freeze: this column is "made of stone" and unfreezes when its ADJACENT columns
        /// (left + right; an edge column needs only its single existing neighbor) are completed. The neighbor
        /// check lives in <see cref="GameManager"/> (which knows the column order); this just marks the frozen
        /// state and retains the neighbor mode so a Rewind that re-locks a neighbor can re-freeze the wall.
        /// No threshold (unlike Lock Color Stack). Fires <see cref="FrozenChanged"/>.
        /// </summary>
        public void FreezeNeighbors()
        {
            initialFrozenNeighborMode = true;   // retained across Unfreeze for the Rewind re-freeze path
            frozenUnlockThreshold = 1;          // any > 0 marks IsFrozen; the value is UNUSED in neighbor mode
            frozenLockColor = false;
            frozenRequiredColor = null;
            // Colliders LEFT ENABLED (same as Freeze) so a tap registers → reject shake; IsFrozen guards block play.
            FrozenChanged?.Invoke(this);
        }

        /// <summary>
        /// Removes the frozen state, re-enabling every piece's collider so the column becomes playable
        /// again. Called by GameManager when the unlock-threshold is met. No-op if not currently frozen.
        /// Does NOT auto-trigger EvaluateLock — the player still has to actually solve this column
        /// after it unfreezes. Fires <see cref="FrozenChanged"/>.
        /// </summary>
        public void Unfreeze()
        {
            if (!IsFrozen) return;
            frozenUnlockThreshold = 0;
            frozenLockColor = false;
            for (int i = 0; i < transform.childCount; i++)
            {
                var col = transform.GetChild(i).GetComponent<Collider>();
                if (col != null) col.enabled = true;
            }
            FrozenChanged?.Invoke(this);
        }

        /// <summary>True if any child piece still has <see cref="Piece.IsTied"/>.</summary>
        public bool HasActiveTie()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var p = transform.GetChild(i).GetComponent<Piece>();
                if (p != null && p.IsTied) return true;
            }
            return false;
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
        /// Plays the column-complete celebration: every child piece does an in-plane hop + a single
        /// spin around its own forward axis, then lands back in slot — the SAME motion as the
        /// Questionmark reveal hop. Runs all pieces in parallel. Knobs are on PlayerHand.
        /// </summary>
        public IEnumerator AnimateCelebration(float duration = 0.4f, float hopDistance = 0.6f, float totalRotationDegrees = 360f, float staggerSeconds = 0f)
        {
            // Hop straight UP (local +Y), IN the board plane — exactly like the reveal hop.
            // Do NOT use -layoutDirection: this column's layoutDirection is (0,0,-1) (pieces stack along
            // local Z because the board is tilted ≈90°), so -layoutDirection = local +Z = PERPENDICULAR to
            // the board surface → the piece hops off the board and visibly clips THROUGH the MainBoard
            // during the spin. Vector3.up keeps the hop in-plane, matching the reveal that looks correct.
            Vector3 hopDirLocal = Vector3.up;
            // Vector3.zero = spin around each piece's own forward axis (same as the reveal hop) — a clean
            // in-plane rotation that does NOT pass through the board.
            Vector3 flipAxis = Vector3.zero;

            // Cascade from the TOP piece down: child index 0 = slot 0 = the VISUAL TOP (where dropped
            // pieces land; see DoMoveAnimated / Undo), increasing index = further down. We start the top
            // piece first (i = 0) and ripple downward with a per-piece startDelay so the spins overlap into
            // a chain (piece 2 begins while piece 1 is still spinning) instead of one block flip.
            // staggerSeconds = 0 reproduces the old all-at-once behaviour.
            var anims = new List<Coroutine>();
            int order = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                var piece = transform.GetChild(i).GetComponent<Piece>();
                if (piece == null) continue;
                float startDelay = order * staggerSeconds;
                order++;
                anims.Add(StartCoroutine(piece.AnimateCelebrate(duration, hopDistance, hopDirLocal, flipAxis, totalRotationDegrees, startDelay)));
            }

            // Wait for every piece, including its stagger delay (durations are equal but start times differ).
            foreach (var co in anims) yield return co;
        }
    }
}
