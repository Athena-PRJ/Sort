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

        [Tooltip("Optional: the decorative slot/badge sprite (typically 'HandPlace') that visually frames " +
                 "the held piece. As a child of HandAnchor, it auto-inherits HandAnchor's scale that " +
                 "LevelLoader applies per level — no separate scaling logic needed here.")]
        [SerializeField] private Transform handSlotDecoration;

        [Header("Animation")]
        [Tooltip("Master toggle. If off, moves resolve instantly (the old behaviour) — useful for debugging logic.")]
        [SerializeField] private bool useAnimations = true;

        [Header("▸ Click → drop — MAIN feel knobs")]
        [Tooltip("FLIGHT SPEED + RESPONSIVENESS (seconds). Time the held piece takes to fly hand → column top. " +
                 "This is THE knob for how snappy a tap feels — it's the dominant cost before a move resolves and " +
                 "the next tap can act. LOWER = faster flight AND quicker response. ~0.12 = snappy, ~0.25 = floaty.")]
        [SerializeField] private float heldToTopDuration = 0.18f;
        [Tooltip("FLIGHT HEIGHT (world units). Apex clearance of the held-piece lob: how far the top of the arc " +
                 "sits ABOVE the higher endpoint (hand vs board slot). The piece always rises this far above BOTH " +
                 "hand and board, then drops into the slot — a clean lob that never clips the tilted board. " +
                 "0 = just grazes the higher endpoint; ~0.5–1.5 = a clearly visible hop. Live-tunable in Play mode.")]
        [SerializeField] private float dropArcHeight = 1.0f;
        [Tooltip("BOWLING-THROW sideways curve: max world-units the held piece swings to one side at mid-flight, " +
                 "scaled by the clicked column's horizontal position — OUTER columns curve more, the CENTER " +
                 "column gets a small RANDOM lean so it isn't dead straight. The piece swings out then hooks " +
                 "back into the slot. 0 = straight lob; ~0.4–0.8 = a gentle hook. Negate to flip the curve side.")]
        [SerializeField] private float bowlingLateralBow = 0.6f;
        [Tooltip("FLIGHT SPIN: degrees the piece rotates (around its facing axis) while flying hand → top slot, " +
                 "or wrapping bottom → top on a tie — a thrown/tumble 'force' feel. Use a MULTIPLE OF 360 so it " +
                 "LANDS UPRIGHT with no pop (360 = one gentle full turn). A literal 180 lands the icon rotated " +
                 "half-way. 0 = no spin.")]
        [SerializeField] private float dropSpinDegrees = 360f;
        [Tooltip("WHEN the flight spin finishes, as a fraction of the flight (0–1). 1 = spins all the way to " +
                 "touchdown; LOWER finishes the turn EARLIER and lets the piece settle before landing (e.g. " +
                 "0.7 = done at 70%, settled for the last 30%). Decelerates smoothly into the settle.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float dropSpinCompleteAt = 0.7f;
        [Tooltip("ROOM-MAKING SPEED (seconds). Time for the column's existing pieces to shift down one slot AND " +
                 "the bottom piece to pop to hand — runs CONCURRENTLY with the flight above, so total move time = " +
                 "max(this, flight speed). Keep ≤ flight speed so the slot is empty as the held piece lands.")]
        [SerializeField] private float shiftAndPopDuration = 0.10f;

        [Header("▸ Pop & land bounce (secondary motion)")]
        [Tooltip("How high the EJECTED (popped) piece arcs above the direct line on its way from column → hand. " +
                 "Smaller than dropArcHeight since the pop is the secondary motion — too big a pop steals focus " +
                 "from the drop.")]
        [SerializeField] private float popArcHeight = 0.8f;
        [Tooltip("Duration of the trampoline-style land bounce when the ejected piece arrives in hand. " +
                 "Plays AFTER the arc completes — piece sits in hand for this many seconds while scaling " +
                 "up at the apex then back to baseline.")]
        [SerializeField] private float landBounceDuration = 0.18f;
        [Tooltip("Peak scale overshoot during the land bounce. 0.15 = piece grows 15% larger at the apex " +
                 "(baseline × 1.15) before settling back to baseline. 0 disables the bounce entirely.")]
        [SerializeField] private float landBounceOvershoot = 0.15f;

        [Header("▸ Pacing")]
        [Tooltip("Small pause between a successful move and the column-complete celebration, so the lock reads as 'earned'.\n" +
                 "Celebration runs in BACKGROUND (fire-and-forget) — input is unlocked before celebration finishes, " +
                 "so player can immediately tap another column. Locked column's own colliders are disabled, so no re-click risk.")]
        [SerializeField] private float celebrationDelay = 0.05f;
        [Tooltip("How many clicks can be queued while an animation is playing.")]
        [SerializeField] private int maxQueuedClicks = 2;

        [Header("Celebration animation (column complete)")]
        [Tooltip("Total duration of the hop+flip per piece when a column locks. Tuned to match the " +
                 "Questionmark-reveal hop feel (~0.5s) — a single graceful flip, not a long spin.")]
        [SerializeField] private float celebrationDuration = 0.5f;
        [Tooltip("How high each piece hops during celebration. Kept in line with the reveal hop (~0.7).")]
        [SerializeField] private float celebrationHopHeight = 0.7f;
        [Tooltip("How many full turns each piece makes during celebration (spins around the piece's own " +
                 "axis, like the Questionmark reveal). 1 = a single clean 360° flip (recommended). Values " +
                 ">1 read as excessive mid-air spinning — keep at 1 to match the reveal animation.")]
        [SerializeField] private float celebrationRotations = 1f;
        [Tooltip("Delay between consecutive pieces' spins so the celebration cascades from the TOP piece " +
                 "down the column (a ripple) instead of every piece flipping at once. Each piece starts " +
                 "this many seconds after the one above it; spins still overlap. 0 = old all-at-once flip. " +
                 "~0.06–0.10 reads as a nice chain.")]
        [SerializeField] private float celebrationStagger = 0.08f;

        [Header("Switch skill animation")]
        [Tooltip("Flight time for each piece during a Switch swap.")]
        [SerializeField] private float switchFlightDuration = 0.35f;
        [Tooltip("HIGH hop (in-plane, up-screen) for one of the two swapped pieces. Both pieces now arc UP " +
                 "(like the rainbow-sink swap) at DIFFERENT heights instead of one up / one down — the old " +
                 "down-arc dipped the piece BEHIND the tilted board and it vanished for a moment.")]
        [SerializeField] private float switchArcHeight = 1.2f;
        [Tooltip("LOW hop for the other swapped piece. Keep it different from the high hop so the two pieces " +
                 "cross at different screen heights and never overlap / pass through each other.")]
        [SerializeField] private float switchArcHeightLow = 0.5f;

        [Header("Magnet skill animation")]
        [Tooltip("Flight time for each piece during a Magnet gather/displace.")]
        [SerializeField] private float magnetFlightDuration = 0.45f;
        [Tooltip("Bow magnitude (in local Y) for pieces flying between columns.")]
        [SerializeField] private float magnetArcHeight = 1.0f;
        [Tooltip("Smaller bow used for pieces that just shift within a column (e.g. compacting source columns).")]
        [SerializeField] private float magnetArcHeightSmall = 0.25f;
        [Tooltip("Delay between consecutive piece launches so the gather reads as a sequence, not a snap.")]
        [SerializeField] private float magnetStaggerSeconds = 0.04f;

        [Header("Rainbow sink animation")]
        [Tooltip("Time each affected piece takes to lerp to its new slot when a rainbow sinks to the bottom.")]
        [SerializeField] private float rainbowSinkDuration = 0.25f;
        [Tooltip("Hop height (in-plane, up-screen) for the piece SINKING down to the bottom. Pieces now " +
                 "arc up and swap places instead of sliding straight through each other. This is the BIG " +
                 "hop — keep it different from the 'up' hop below so crossing pieces pass at different " +
                 "screen heights and never visually overlap.")]
        [SerializeField] private float sinkArcHeightDown = 1.0f;
        [Tooltip("Hop height for the piece(s) being DISPLACED upward by the sink. The SMALL hop — must " +
                 "differ from the 'down' hop so the two pieces don't fly through each other.")]
        [SerializeField] private float sinkArcHeightUp = 0.45f;

        [Header("Tie shift + break animation")]
        [Tooltip("Tie SYNC lead: fraction of the held-piece flight (heldToTopDuration) that elapses BEFORE " +
                 "the rest of the tied chain rotates. The held piece launches first and leads; once it has " +
                 "flown this fraction of the way (i.e. is NEARING the board), EVERY tied column rotates as " +
                 "ONE synchronized beat — same start, same duration — all landing together with the held " +
                 "piece. This replaces the old per-piece distance-based speed (which made short shifts finish " +
                 "long before long bottom→top wraps → the desynced look). 0 = whole chain moves together from " +
                 "t=0; ~0.35 = a little anticipation then a clean synchronized snap. Range 0-0.9.")]
        [SerializeField, Range(0f, 0.9f)] private float tieSyncLeadFraction = 0.35f;
        [Tooltip("Fade-out duration when a tied pair reaches the bottom row and the tie breaks. " +
                 "TieVisual swaps to crack materials then alpha fades 1→0 over this many seconds, " +
                 "then destroys itself.")]
        [SerializeField] private float tieBreakFadeDuration = 0.3f;

        [Header("Questionmark reveal animation")]
        [Tooltip("REVEAL SPEED. Total time (seconds) for the hop + mid-air color swap. LOWER = faster reveal.")]
        [SerializeField] private float revealHopDuration = 0.35f;
        [Tooltip("How far the piece hops up (in local units) during the reveal animation.")]
        [SerializeField] private float revealHopHeight = 0.7f;
        [Tooltip("How many full turns the piece flips during the reveal. 1 = a single clean flip (recommended, " +
                 "matches the celebration); 0.5 = a half flip. Higher reads as over-spinning — keep at 1.")]
        [SerializeField] private float revealRotations = 1f;
        [Tooltip("WHEN the real color appears, as a fraction of the hop (0 = start, 0.5 = apex, 1 = land). " +
                 "Lower reveals the original piece SOONER; higher waits longer before the swap.")]
        [Range(0f, 1f)]
        [SerializeField] private float revealAt = 0.5f;
        [Tooltip("Optional pause between the move resolving and the reveal hop starting, so the reveal reads as 'earned'.")]
        [SerializeField] private float revealDelay = 0.03f;

        [Header("Reject feedback (shake)")]
        [Tooltip("Play a small shake on the held piece when the player taps a column that rejects the " +
                 "move — a frozen / Lock Color Stack column (still locked), or an Only Stack Sort column " +
                 "that doesn't accept the held color. Pure game-feel; no gameplay effect.")]
        [SerializeField] private bool shakeOnReject = true;
        [Tooltip("Total duration of the reject shake (seconds).")]
        [SerializeField] private float rejectShakeDuration = 0.28f;
        [Tooltip("Peak rotation of the reject shake (degrees) — wobble amplitude, decays to 0 over the duration.")]
        [SerializeField] private float rejectShakeAngle = 12f;
        [Tooltip("How many back-and-forth wobbles fit in the duration. Higher = faster, buzzier shake.")]
        [SerializeField] private float rejectShakeOscillations = 3f;

        public Piece HeldPiece => heldPiece;
        public Transform HandAnchor => handAnchor;
        public Transform HandSlotDecoration => handSlotDecoration;

        // Per-prefab visual offset for the held piece, applied inside PlaceInHand so every swap /
        // undo / level build keeps the held piece at the designer-tuned spot relative to handAnchor.
        // Set via HeldPieceLocalOffset (LevelLoader pushes the per-prefab override here).
        Vector3 heldPieceLocalOffset = Vector3.zero;
        public Vector3 HeldPieceLocalOffset
        {
            get => heldPieceLocalOffset;
            set
            {
                heldPieceLocalOffset = value;
                if (heldPiece != null) heldPiece.transform.localPosition = value;
            }
        }

        // Snapshot of the last move so the Rewind skill can reverse it.
        Column lastMoveColumn;
        bool   lastMoveLockedColumn;

        // Animation gating: while true, new clicks are queued instead of executed.
        bool isAnimating;
        readonly Queue<Transform> clickQueue = new Queue<Transform>();

        // True while a reject-shake is playing on the held piece, so rapid wrong-taps don't stack
        // shakes (which would compound the rotation offset). Cosmetic only — does NOT gate input.
        bool heldShaking;

        public bool CanUndo => lastMoveColumn != null;
        public bool IsAnimating => isAnimating;

        // Active skill-targeting mode. While set, piece taps go through the matching handler
        // instead of the default column-drop. Cleared on completion or cancel.
        public enum SkillMode { None, Switch, Magnet }
        SkillMode skillMode = SkillMode.None;
        Piece switchPickA;

        public SkillMode CurrentSkillMode => skillMode;
        public Piece SwitchFirstPick => switchPickA;
        public bool IsInSkillMode => skillMode != SkillMode.None;

        /// <summary>Fired whenever the hand state changes (a move was made or undone).</summary>
        public event System.Action StateChanged;

        /// <summary>Fired when entering, advancing, or leaving a skill-targeting mode. UI listens to update hints.</summary>
        public event System.Action SkillModeChanged;

        /// <summary>Fired on every piece tap (any column / skill pick). BoardIdleAnimator uses it to reset its idle timer.</summary>
        public static event System.Action AnyInteraction;

        /// <summary>
        /// Assigns a piece as the held piece, parents it under the HandAnchor, and resets local position.
        /// Pass null to clear (e.g. when rebuilding the board between levels).
        /// </summary>
        public void SetHeldPiece(Piece p)
        {
            if (p == null) { heldPiece = null; return; }
            PlaceInHand(p);
        }

        /// <summary>
        /// Optional helper to set the held-piece placemat (HandSlotDecoration) sprite at runtime. No longer
        /// called by LevelLoader (the placemat art is fixed in the scene now); kept for manual/scripted use.
        /// No-op if there's no decoration / SpriteRenderer / Image / sprite. Works on a world SpriteRenderer
        /// or a UGUI Image.
        /// </summary>
        public void SetPlaceSprite(Sprite sprite)
        {
            if (sprite == null || handSlotDecoration == null) return;
            // World-space placemat (SpriteRenderer) — on the decoration or a child.
            var sr = handSlotDecoration.GetComponent<SpriteRenderer>();
            if (sr == null) sr = handSlotDecoration.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null) { sr.sprite = sprite; return; }
            // Or a UGUI placemat (Image), on the decoration or a child.
            var img = handSlotDecoration.GetComponent<UnityEngine.UI.Image>();
            if (img == null) img = handSlotDecoration.GetComponentInChildren<UnityEngine.UI.Image>(true);
            if (img != null) { img.sprite = sprite; return; }

            Debug.LogWarning($"[PlayerHand] placeSprite is set but '{handSlotDecoration.name}' has no " +
                             "SpriteRenderer/Image to receive it — wire Hand Slot Decoration to the placemat " +
                             "object (e.g. HandPlace) and make sure it has a SpriteRenderer or Image.", this);
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

        /// <summary>
        /// Central dispatch for piece taps. Routes through the current skill mode when one is
        /// active, otherwise falls back to the normal column-drop flow.
        /// </summary>
        public void OnPieceTapped(Piece piece)
        {
            if (piece == null) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;

            // Any tap counts as interaction — reset the board's idle wind-sway timer.
            AnyInteraction?.Invoke();

            // Light haptic on every column/piece tap (mobile). No-op on desktop / when disabled in Settings.
            Haptics.LightTap();

            switch (skillMode)
            {
                case SkillMode.Switch: HandleSwitchPick(piece); return;
                case SkillMode.Magnet: HandleMagnetPick(piece); return;
                default:               HandleColumnClick(piece.transform.parent); return;
            }
        }

        public void HandleColumnClick(Transform column)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            if (column == null || column == handAnchor) return;

            var col = column.GetComponent<Column>();
            if (col == null || col.IsLocked) return;
            // Frozen columns block ALL interactions (the gating mechanic). Colliders are also
            // disabled in Column.Freeze, but check here defensively in case the click came from
            // a code path that bypasses the collider raycast (queued click, undo replay, etc.).
            if (col.IsFrozen) { RejectFeedback(); return; }

            // Click queue: if we're mid-animation, hold this click for after.
            // (When the queued click fires, heldPiece will already have been refreshed
            // by the previous turn's pop-to-hand phase.)
            if (isAnimating)
            {
                if (clickQueue.Count < maxQueuedClicks) clickQueue.Enqueue(column);
                return;
            }

            if (heldPiece == null) return;

            // Only Stack Sort: this column accepts only its configured color (rainbows are wild).
            // Reject a non-matching held piece — the player must place it elsewhere. Checked at
            // execution time (not when queued) since the held piece can change between queued clicks.
            if (!col.AcceptsPiece(heldPiece)) { RejectFeedback(); return; }

            // Valid drop confirmed → click SFX (rejected taps play their own sound in RejectFeedback).
            SfxManager.Play(SfxId.ColumnClick);

            if (useAnimations)
            {
                // Detect tied chain: BFS via Piece.TiedPartner. If the clicked col is bound to one
                // or more neighbors, ALL of them shift together. Clicked col ejects to hand; the
                // others wrap bottom → top. Tied pair that ends up at the bottom row breaks.
                var chain = FindTiedChain(col);
                if (chain.Count > 1)
                    StartCoroutine(DoTiedShiftAnimated(col, chain));
                else
                    StartCoroutine(DoMoveAnimated(col));
            }
            else
            {
                // Instant path doesn't support tied shifts — fall back to plain instant (ignores ties).
                DoMoveInstant(col);
            }
        }

        /// <summary>
        /// Game-feel only: shakes the held piece to signal a rejected tap (frozen column, or an Only
        /// Stack Sort color mismatch). No-op if disabled, no held piece, mid-move, or already shaking.
        /// Does NOT set isAnimating, so the player can keep interacting normally.
        /// </summary>
        void RejectFeedback()
        {
            // Sound plays on every rejected tap, even when the shake is disabled / already running.
            SfxManager.Play(SfxId.Reject);
            if (!shakeOnReject || heldPiece == null || isAnimating || heldShaking) return;
            StartCoroutine(ShakeHeldPiece());
        }

        IEnumerator ShakeHeldPiece()
        {
            heldShaking = true;
            var p = heldPiece;
            if (p != null)
                yield return p.AnimateShake(rejectShakeDuration, rejectShakeAngle, rejectShakeOscillations);
            heldShaking = false;
        }

        /// <summary>
        /// Returns the set of columns that move together with <paramref name="clicked"/>: itself plus
        /// every column reachable by walking <see cref="Piece.TiedPartner"/> refs transitively. Locked
        /// columns are skipped (they shouldn't have active ties per design, but defensive).
        /// </summary>
        List<Column> FindTiedChain(Column clicked)
        {
            var chain = new List<Column> { clicked };
            var visited = new HashSet<Column> { clicked };
            var queue = new Queue<Column>();
            queue.Enqueue(clicked);

            while (queue.Count > 0)
            {
                var col = queue.Dequeue();
                for (int i = 0; i < col.transform.childCount; i++)
                {
                    var p = col.transform.GetChild(i).GetComponent<Piece>();
                    if (p == null || !p.IsTied) continue;
                    var partner = p.TiedPartner;
                    if (partner == null || partner.transform.parent == null) continue;
                    var partnerCol = partner.transform.parent.GetComponent<Column>();
                    if (partnerCol == null || visited.Contains(partnerCol)) continue;
                    if (partnerCol.IsLocked) continue;
                    // Frozen partner columns are excluded from the tied chain — clicking the
                    // non-frozen side won't drag the frozen column's pieces along (they're locked
                    // behind the unfreeze gate). Tie visual may visually stretch in this edge case;
                    // designer should avoid configuring ties that span frozen columns when possible.
                    if (partnerCol.IsFrozen) continue;
                    visited.Add(partnerCol);
                    chain.Add(partnerCol);
                    queue.Enqueue(partnerCol);
                }
            }

            return chain;
        }

        // ---------------------------------------------------------------------
        //  Skill mode entry / cancel (called by SkillManager).
        // ---------------------------------------------------------------------

        public bool BeginSwitchMode()
        {
            if (isAnimating) return false;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return false;
            skillMode = SkillMode.Switch;
            switchPickA = null;
            SkillModeChanged?.Invoke();
            return true;
        }

        public bool BeginMagnetMode()
        {
            if (isAnimating) return false;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return false;
            skillMode = SkillMode.Magnet;
            SkillModeChanged?.Invoke();
            return true;
        }

        public void CancelSkillMode()
        {
            if (skillMode == SkillMode.None) return;
            skillMode = SkillMode.None;
            switchPickA = null;
            SkillModeChanged?.Invoke();
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
            UpdateRainbowSinkOpportunities();  // Instant snap — no animation in this path.
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
        /// <summary>
        /// Lean factor in [-1, 1] for the bowling-throw curve, from the clicked column's horizontal position:
        /// −1 = far left, +1 = far right (Board-local X, columns are centered at 0). A near-center column
        /// gets a small RANDOM lean so the middle isn't dead straight. 0 columns / single column → random.
        /// </summary>
        float ComputeLateralLean(Column clicked)
        {
            float RandomLean() => (UnityEngine.Random.value < 0.5f ? -1f : 1f) * UnityEngine.Random.Range(0.25f, 0.5f);
            if (clicked == null) return 0f;

            float clickedX = clicked.transform.localPosition.x;
            float maxX = Mathf.Abs(clickedX);
            var gm = GameManager.Instance;
            if (gm != null)
                foreach (var c in gm.Columns)
                    if (c != null) maxX = Mathf.Max(maxX, Mathf.Abs(c.transform.localPosition.x));

            if (maxX < 1e-3f) return RandomLean();              // single / centered column
            float lean = Mathf.Clamp(clickedX / maxX, -1f, 1f);
            if (Mathf.Abs(lean) < 0.15f) lean = RandomLean();   // dead-center column → gentle random side
            return lean;
        }

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
            // from wherever-it-was straight to the per-prefab hand offset.
            if (ejected != null)
                ejected.transform.SetParent(handAnchor, worldPositionStays: true);

            Vector3 layoutDir = col.LayoutDirection;
            float spacing = col.PieceSpacing;

            // --- Kick off all motions concurrently --------------------------
            var animations = new List<Coroutine>();

            // Held → top slot. WORLD-space lob (AnimateWorldArcTo): the piece rises straight up on
            // screen above BOTH the hand and the board by dropArcHeight, then descends into slot 0 —
            // a clean golf-shot drop. We deliberately do NOT bow along the column's local "up"
            // (−LayoutDirection): the board is tilted ≈90°, so that axis points perpendicular to the
            // board face and the lob would clip THROUGH the board/pieces (same trap AnimateCelebration
            // documents). World-up is robust to the tilt.
            // Bowling-throw lateral curve: lean depends on the clicked column's position; center → random.
            float lean = ComputeLateralLean(col);
            Vector3 latDir = Camera.main != null ? Camera.main.transform.right : Vector3.right;
            animations.Add(StartCoroutine(toInsert.AnimateWorldArcTo(
                Vector3.zero, toInsert.RestRotation, heldToTopDuration, dropArcHeight, Easing.SmoothStep,
                emitTrail: true, lateralDir: latDir, lateralBow: lean * bowlingLateralBow,
                spinDegrees: dropSpinDegrees, spinCompleteAt: dropSpinCompleteAt)));

            // Every piece except the ejected one shifts down one slot — keep STRAIGHT line because
            // they move only 1 piece-slot's worth of distance, an arc here would look unnecessarily
            // jumpy. pieces[i] was at slot i pre-move; with the new piece at sibling 0 it's now slot i+1.
            for (int i = 0; i < pieces.Count - 1; i++)
            {
                var p = pieces[i];
                Vector3 newSlot = layoutDir * ((i + 1) * spacing);
                animations.Add(StartCoroutine(p.AnimateLocalTo(
                    newSlot, p.RestRotation, shiftAndPopDuration, Easing.SmoothStep)));
            }

            // Old bottom piece → hand center: ARC then BOUNCE. Arc reads as "pops out of the column
            // and falls into the player's hand". Bounce reads as the impact: piece briefly puffs up
            // (like landing on a trampoline) then settles to baseline. Sequential within this single
            // coroutine; runs in PARALLEL with the held-piece arc and the shifters via the animations list.
            if (ejected != null)
            {
                animations.Add(StartCoroutine(EjectedArcThenBounce(ejected, heldPieceLocalOffset)));
            }

            // Wait for the slowest coroutine to finish (typically heldToTop @ 0.25s).
            foreach (var co in animations) yield return co;

            // --- assign held ref (already reparented above) ------------------
            if (ejected != null) heldPiece = ejected;

            // --- PHASE 2.5: animated reveal of any newly-eligible Questionmarks --
            // Runs BEFORE FinalizeMove so the lock check happens with revealed=true.
            int revealThreshold = 2;
            if (LevelLoader.Instance != null && LevelLoader.Instance.CurrentLevel != null)
                revealThreshold = LevelLoader.Instance.CurrentLevel.questionmarkRevealFromBottom;
            var toReveal = col.FindQuestionmarksToReveal(revealThreshold);
            if (toReveal.Count > 0)
            {
                if (revealDelay > 0f) yield return new WaitForSeconds(revealDelay);
                var revealAnims = new List<Coroutine>();
                foreach (var p in toReveal)
                    revealAnims.Add(StartCoroutine(p.AnimateRevealHop(revealHopDuration, revealHopHeight, Vector3.up, Vector3.zero, revealAt, revealRotations * 360f)));
                foreach (var co in revealAnims) yield return co;
            }

            // --- finalize logic (revealed pieces are already revealed; CheckRevealQuestionmarks is a no-op) ---
            FinalizeMove(col);

            // --- PHASE 3: celebration if the column locked (fire-and-forget) ---
            // Purely decorative. Locked column's colliders are already disabled in EvaluateLock,
            // so the player can't re-click it. Letting celebration run in background lets the
            // player tap OTHER columns immediately for a more responsive feel.
            if (col.IsLocked)
                StartCoroutine(CelebrateColumnDeferred(col));

            // --- PHASE 4: animated rainbow sink in other columns -------------
            // Kept INLINE (awaited) because the sink signals "this column is one swap away from
            // completion" — the player needs to see the rainbow at the bottom before deciding
            // the next move, otherwise they'd miss a setup play.
            yield return StartCoroutine(UpdateRainbowSinkOpportunitiesAnimated());

            isAnimating = false;

            // Drain one queued click if any. Recursion is shallow (always re-gates via isAnimating).
            if (clickQueue.Count > 0)
            {
                var next = clickQueue.Dequeue();
                HandleColumnClick(next);
            }
        }

        /// <summary>
        /// Ejected piece path: arc from its previous column slot to the hand, then play a trampoline-
        /// style land bounce. Sequential inside this coroutine; the OUTER animations list runs this
        /// in parallel with the held-piece arc and shifters, so the held + ejected motions overlap.
        /// </summary>
        /// <summary>
        /// Tied shift: every column in <paramref name="chain"/> moves on the same beat. The
        /// <paramref name="clickedCol"/> performs a normal Sort# move (held piece in at top, bottom
        /// pops to hand). Non-clicked columns CIRCULARLY rotate (bottom wraps to top, everyone else
        /// shifts down 1) — no piece leaves these columns.
        /// After the animations land, any tied pair now at the bottom row of its column (or already
        /// past it, i.e. ejected/wrapped) breaks: the visual plays Crack+Fade and the
        /// <see cref="Piece.TiedPartner"/> refs are cleared on both sides.
        /// Move counter advances by 1 (the tied shift is one player action). Rewind is BLOCKED for
        /// tied shifts — restoring multi-column + tie state is complex; players use Switch/Magnet
        /// for surgical changes anyway.
        /// </summary>
        IEnumerator DoTiedShiftAnimated(Column clickedCol, List<Column> chain)
        {
            isAnimating = true;

            // --- Snapshot per-col data BEFORE any mutation ---
            var snaps = new Dictionary<Column, List<Piece>>();
            var bottomsBefore = new HashSet<Piece>();
            foreach (var col in chain)
            {
                var snap = SnapshotColumnPieces(col.transform);
                snaps[col] = snap;
                if (snap.Count > 0) bottomsBefore.Add(snap[snap.Count - 1]);
            }

            // --- Reparent / sibling-reorder upfront so logical state is correct before animation ---
            Piece toInsert = heldPiece;
            Piece ejected = (snaps[clickedCol].Count > 0) ? snaps[clickedCol][snaps[clickedCol].Count - 1] : null;
            heldPiece = null;

            // Clicked col: held → top; ejected → handAnchor (worldPosStays so visuals don't snap).
            if (toInsert != null)
            {
                toInsert.transform.SetParent(clickedCol.transform, worldPositionStays: true);
                toInsert.transform.SetSiblingIndex(0);
            }
            if (ejected != null)
                ejected.transform.SetParent(handAnchor, worldPositionStays: true);

            // Non-clicked cols: bottom moves to sibling 0 (becomes new top). Stays a child of its col.
            foreach (var col in chain)
            {
                if (col == clickedCol) continue;
                var snap = snaps[col];
                if (snap.Count == 0) continue;
                var bottom = snap[snap.Count - 1];
                bottom.transform.SetSiblingIndex(0);
            }

            // --- Spawn all motion coroutines: ONE SYNCHRONIZED BEAT ---
            // The held piece LEADS (flies from hand over heldToTopDuration). Once it has flown
            // tieSyncLeadFraction of the way (nearing the board), EVERY tied column rotates together:
            // same start (boardLag) + same duration (boardDur), so the clicked column's down-shift and
            // every linked column's bottom→top wrap move on the exact same frame and land WITH the held
            // piece. Replaces the old distance-based per-piece speed that desynced short vs long moves.
            float boardLag = heldToTopDuration * tieSyncLeadFraction;
            float boardDur = Mathf.Max(0.01f, heldToTopDuration - boardLag);

            var animations = new List<Coroutine>();

            // Held piece leads — world-up lob (no lag, no clip-through), trail on, with the flight spin.
            if (toInsert != null)
                animations.Add(StartCoroutine(toInsert.AnimateWorldArcTo(
                    Vector3.zero, toInsert.RestRotation, heldToTopDuration, dropArcHeight, Easing.SmoothStep,
                    emitTrail: true, spinDegrees: dropSpinDegrees, spinCompleteAt: dropSpinCompleteAt)));

            foreach (var col in chain)
            {
                var snap = snaps[col];
                if (snap.Count == 0) continue;
                Vector3 layoutDir = col.LayoutDirection;
                float spacing = col.PieceSpacing;

                if (col == clickedCol)
                {
                    // Down-shifters: snap[0..n-2] → slot i+1, on the synchronized beat (straight line — 1 slot).
                    for (int i = 0; i < snap.Count - 1; i++)
                    {
                        var p = snap[i];
                        Vector3 newSlot = layoutDir * ((i + 1) * spacing);
                        animations.Add(StartCoroutine(DelayedLine(p, newSlot, boardDur, boardLag)));
                    }
                    // Ejected bottom → hand (arc + bounce), on the synchronized beat.
                    if (ejected != null)
                        animations.Add(StartCoroutine(EjectedArcThenBounce(ejected, heldPieceLocalOffset, boardDur, boardLag)));
                }
                else
                {
                    // Non-clicked: bottom wraps to top via a world-up lob (matches the held drop, no clip),
                    // on the synchronized beat.
                    var bottom = snap[snap.Count - 1];
                    animations.Add(StartCoroutine(DelayedWorldArc(bottom, Vector3.zero, boardDur, dropArcHeight, boardLag, dropSpinDegrees, dropSpinCompleteAt)));
                    // Other pieces shift down 1 slot, on the synchronized beat.
                    for (int i = 0; i < snap.Count - 1; i++)
                    {
                        var p = snap[i];
                        Vector3 newSlot = layoutDir * ((i + 1) * spacing);
                        animations.Add(StartCoroutine(DelayedLine(p, newSlot, boardDur, boardLag)));
                    }
                }
            }

            foreach (var co in animations) yield return co;

            // --- Bind ejected as new held ---
            if (ejected != null) heldPiece = ejected;

            // --- Tie break detection: union of pieces at the bottom BEFORE shift (now ejected/wrapped)
            //     and pieces at the bottom AFTER shift. Any tied piece in this union → its tie broke.
            var bottomsAfter = new HashSet<Piece>();
            foreach (var col in chain)
            {
                var cur = SnapshotColumnPieces(col.transform);
                if (cur.Count > 0) bottomsAfter.Add(cur[cur.Count - 1]);
            }

            var brokenTies = new List<(Piece a, Piece b)>();
            var seenInBreak = new HashSet<Piece>();
            void TryQueueBreak(Piece p)
            {
                if (p == null || !p.IsTied || seenInBreak.Contains(p)) return;
                var partner = p.TiedPartner;
                if (partner == null) return;
                brokenTies.Add((p, partner));
                seenInBreak.Add(p);
                seenInBreak.Add(partner);
            }
            foreach (var p in bottomsBefore) TryQueueBreak(p);
            foreach (var p in bottomsAfter) TryQueueBreak(p);

            // --- Fire crack+fade on each broken tie's visual, clear the refs ---
            if (brokenTies.Count > 0)
            {
                SfxManager.Play(SfxId.TieBreak);
                var allTies = FindObjectsByType<TieVisual>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var (a, b) in brokenTies)
                {
                    foreach (var tv in allTies)
                    {
                        if ((tv.PieceA == a && tv.PieceB == b) || (tv.PieceA == b && tv.PieceB == a))
                        {
                            StartCoroutine(tv.CrackAndFade(tieBreakFadeDuration));
                            break;
                        }
                    }
                    a.SetTiedPartner(null);
                    b.SetTiedPartner(null);
                }
            }

            // --- Animated Q? reveal for any column in the chain ---
            int revealThreshold = 2;
            if (LevelLoader.Instance != null && LevelLoader.Instance.CurrentLevel != null)
                revealThreshold = LevelLoader.Instance.CurrentLevel.questionmarkRevealFromBottom;
            var revealAnims = new List<Coroutine>();
            foreach (var col in chain)
            {
                var toReveal = col.FindQuestionmarksToReveal(revealThreshold);
                foreach (var p in toReveal)
                    revealAnims.Add(StartCoroutine(p.AnimateRevealHop(revealHopDuration, revealHopHeight, Vector3.up, Vector3.zero, revealAt, revealRotations * 360f)));
            }
            if (revealAnims.Count > 0 && revealDelay > 0f) yield return new WaitForSeconds(revealDelay);
            foreach (var co in revealAnims) yield return co;

            // --- Snap layouts + lock evaluation for every col in chain ---
            foreach (var col in chain) col.Layout();
            foreach (var col in chain) col.EvaluateLock();

            // --- Fire celebrations (background, doesn't block input) ---
            foreach (var col in chain)
            {
                if (col.IsLocked)
                    StartCoroutine(CelebrateColumnDeferred(col));
            }

            // Block rewind: tied moves restore would need to recover multi-col state + tie bindings,
            // which is complex. Players have Switch/Magnet for surgical changes.
            lastMoveColumn = null;
            lastMoveLockedColumn = false;

            GameManager.Instance?.NotifyMoveMade();
            StateChanged?.Invoke();

            yield return StartCoroutine(UpdateRainbowSinkOpportunitiesAnimated());

            isAnimating = false;

            // Drain queued click if any.
            if (clickQueue.Count > 0)
            {
                var next = clickQueue.Dequeue();
                HandleColumnClick(next);
            }
        }

        IEnumerator EjectedArcThenBounce(Piece p, Vector3 targetLocalPos)
        {
            yield return StartCoroutine(p.AnimateLocalArcTo(
                targetLocalPos, p.RestRotation, shiftAndPopDuration, popArcHeight, Vector3.up, Easing.SmoothStep));
            yield return StartCoroutine(p.AnimateLandBounce(landBounceDuration, landBounceOvershoot));
        }

        /// <summary>
        /// Tie-sync overload: waits <paramref name="delay"/> so the pop joins the synchronized board
        /// beat, then arcs to hand over <paramref name="arcDuration"/> and plays the land bounce.
        /// </summary>
        IEnumerator EjectedArcThenBounce(Piece p, Vector3 targetLocalPos, float arcDuration, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            yield return StartCoroutine(p.AnimateLocalArcTo(
                targetLocalPos, p.RestRotation, arcDuration, popArcHeight, Vector3.up, Easing.SmoothStep));
            yield return StartCoroutine(p.AnimateLandBounce(landBounceDuration, landBounceOvershoot));
        }

        /// <summary>Waits <paramref name="delay"/> then lerps the piece in a straight line to its slot
        /// (used for the 1-slot down-shifters on the synchronized tie beat).</summary>
        IEnumerator DelayedLine(Piece p, Vector3 targetLocalPos, float duration, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            yield return StartCoroutine(p.AnimateLocalTo(targetLocalPos, p.RestRotation, duration, Easing.SmoothStep));
        }

        /// <summary>Waits <paramref name="delay"/> then flies the piece to its slot via a WORLD-up lob
        /// (used for the non-clicked columns' bottom→top wrap on the synchronized tie beat).</summary>
        IEnumerator DelayedWorldArc(Piece p, Vector3 targetLocalPos, float duration, float apexClearance, float delay, float spinDegrees = 0f, float spinCompleteAt = 1f)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            yield return StartCoroutine(p.AnimateWorldArcTo(targetLocalPos, p.RestRotation, duration, apexClearance, Easing.SmoothStep, spinDegrees: spinDegrees, spinCompleteAt: spinCompleteAt));
        }

        /// <summary>
        /// Background celebration coroutine — does NOT block input. Waits the configured
        /// celebrationDelay then plays the column's per-piece hop+spin. Caller fires this
        /// with <c>StartCoroutine</c> (no <c>yield return</c>) to keep responsiveness up.
        /// </summary>
        IEnumerator CelebrateColumnDeferred(Column col)
        {
            if (col == null) yield break;
            if (celebrationDelay > 0f) yield return new WaitForSeconds(celebrationDelay);
            yield return StartCoroutine(RunCelebration(col));
        }

        /// <summary>
        /// One source of truth for the celebration params. Every "column just locked" callsite
        /// (regular move, Switch lock, Magnet lock) routes through here so tuning the Inspector
        /// fields once affects all paths.
        /// </summary>
        IEnumerator RunCelebration(Column col)
        {
            if (col == null) yield break;
            SfxManager.Play(SfxId.ColumnComplete);
            yield return StartCoroutine(col.AnimateCelebration(
                celebrationDuration, celebrationHopHeight, celebrationRotations * 360f, celebrationStagger));
        }

        // ---------------------------------------------------------------------
        //  Skill 2 — Switch. Player picks two pieces in different columns; they swap.
        // ---------------------------------------------------------------------

        void HandleSwitchPick(Piece piece)
        {
            if (isAnimating) return;
            var col = piece.transform.parent != null ? piece.transform.parent.GetComponent<Column>() : null;
            if (col == null || col.IsLocked) return;
            // Frozen columns block Switch — even though the piece's collider is disabled while
            // frozen, this is a defensive check for any code path that bypasses the raycast.
            if (col.IsFrozen) return;
            // Hidden Questionmark has no known color — swapping it leaves the game state inscrutable.
            if (piece.IsQuestionmark && !piece.IsRevealed) return;
            // Tied pieces cannot be Switch'd — design rule. Swapping one half of a tie across columns
            // would either drag its partner across (mechanically weird) or break the tie unexpectedly.
            if (piece.IsTied) return;

            if (switchPickA == null)
            {
                switchPickA = piece;
                SkillModeChanged?.Invoke();
                return;
            }

            // Tapping the same piece twice clears the first pick (gives the player an "oops" out).
            if (piece == switchPickA)
            {
                switchPickA = null;
                SkillModeChanged?.Invoke();
                return;
            }

            var colA = switchPickA.transform.parent != null ? switchPickA.transform.parent.GetComponent<Column>() : null;
            if (colA == null) { CancelSkillMode(); return; }

            // Same column → blocked (no point per design).
            if (col == colA) return;
            // Same effective color (both non-Rainbow, same enum) → blocked (no point).
            if (!piece.IsRainbow && !switchPickA.IsRainbow && piece.Color == switchPickA.Color) return;

            // Spend ONE stored Switch use. If the stockpile is empty, abort silently —
            // SkillManager should have prevented entry into Switch mode without a use available.
            if (!PlayerEconomy.TrySpendSwitchUse()) return;

            var pieceA = switchPickA;
            var pieceB = piece;
            switchPickA = null;
            skillMode = SkillMode.None;
            SkillModeChanged?.Invoke();

            StartCoroutine(DoSwitchAnimated(pieceA, colA, pieceB, col));
        }

        IEnumerator DoSwitchAnimated(Piece a, Column colA, Piece b, Column colB)
        {
            isAnimating = true;
            SfxManager.Play(SfxId.Switch);

            int idxA = a.transform.GetSiblingIndex();
            int idxB = b.transform.GetSiblingIndex();

            Vector3 targetLocalA = colB.LayoutDirection * (idxB * colB.PieceSpacing);
            Vector3 targetLocalB = colA.LayoutDirection * (idxA * colA.PieceSpacing);

            // Reparent without snapping; localPosition is now whatever lines up with current world pos.
            a.transform.SetParent(colB.transform, worldPositionStays: true);
            a.transform.SetSiblingIndex(idxB);
            b.transform.SetParent(colA.transform, worldPositionStays: true);
            b.transform.SetSiblingIndex(idxA);

            // Both pieces hop UP (Vector3.up = in-plane, up-screen — same axis as the rainbow-sink swap and
            // the celebration hop) but at DIFFERENT heights, so where their paths cross they sit at
            // different screen heights and never overlap. Previously B used -switchArcHeight (a DOWN arc),
            // which on the ≈90°-tilted board dipped it BEHIND the board surface → the piece went invisible
            // for a moment. Target rotation = each piece's RestRotation so they land upright.
            var animA = StartCoroutine(a.AnimateLocalArcTo(targetLocalA, a.RestRotation, switchFlightDuration, switchArcHeight,    Vector3.up, Easing.SmoothStep));
            var animB = StartCoroutine(b.AnimateLocalArcTo(targetLocalB, b.RestRotation, switchFlightDuration, switchArcHeightLow, Vector3.up, Easing.SmoothStep));
            yield return animA;
            yield return animB;

            colA.Layout();
            colB.Layout();

            // Animated reveal for any newly-eligible Questionmarks in either touched column.
            int revealThreshold = 2;
            if (LevelLoader.Instance != null && LevelLoader.Instance.CurrentLevel != null)
                revealThreshold = LevelLoader.Instance.CurrentLevel.questionmarkRevealFromBottom;
            var toRevealAB = new List<Piece>();
            toRevealAB.AddRange(colA.FindQuestionmarksToReveal(revealThreshold));
            toRevealAB.AddRange(colB.FindQuestionmarksToReveal(revealThreshold));
            if (toRevealAB.Count > 0)
            {
                if (revealDelay > 0f) yield return new WaitForSeconds(revealDelay);
                var revealAnims = new List<Coroutine>();
                foreach (var p in toRevealAB)
                    revealAnims.Add(StartCoroutine(p.AnimateRevealHop(revealHopDuration, revealHopHeight, Vector3.up, Vector3.zero, revealAt, revealRotations * 360f)));
                foreach (var co in revealAnims) yield return co;
            }

            colA.EvaluateLock();
            colB.EvaluateLock();

            // Celebrate any newly-locked column.
            if (colA.IsLocked)
            {
                if (celebrationDelay > 0f) yield return new WaitForSeconds(celebrationDelay);
                yield return StartCoroutine(RunCelebration(colA));
            }
            if (colB.IsLocked)
            {
                if (celebrationDelay > 0f) yield return new WaitForSeconds(celebrationDelay);
                yield return StartCoroutine(RunCelebration(colB));
            }

            // Skill use breaks the Rewind chain — undo across a switch would be confusing.
            lastMoveColumn = null;
            lastMoveLockedColumn = false;

            yield return StartCoroutine(UpdateRainbowSinkOpportunitiesAnimated());
            StateChanged?.Invoke();

            isAnimating = false;
        }

        // ---------------------------------------------------------------------
        //  Skill 3 — Magnet. Player clicks a piece; all same-color pieces in unlocked columns
        //  gather into that piece's column, displaced non-matches fill the holes left behind.
        // ---------------------------------------------------------------------

        struct MagnetCandidate
        {
            public Piece piece;
            public Column src;
            public int srcSlot;
            public int srcColIdx;
        }

        void HandleMagnetPick(Piece piece)
        {
            if (isAnimating) return;
            var col = piece.transform.parent != null ? piece.transform.parent.GetComponent<Column>() : null;
            if (col == null || col.IsLocked) return;
            // Frozen columns block Magnet — both as the source piece's column AND as a gather target.
            // Defensive check on top of the Column.Freeze collider disable.
            if (col.IsFrozen) return;
            if (piece.IsRainbow) return;                       // No color to gather.
            if (piece.IsQuestionmark && !piece.IsRevealed) return;
            // Tied columns are OFF-LIMITS to Magnet (same as frozen): gathering INTO or pulling FROM a
            // column that has an active tie would drag a tied piece across columns / disturb the tie —
            // a visible bug. Block the whole clicked column, not just the case where the tapped piece is tied.
            if (col.HasActiveTie()) return;

            // Compute the plan BEFORE spending coins so we can refuse no-op magnets
            // (e.g. the clicked color only exists in the target column already).
            var plan = BuildMagnetPlan(col, piece.Color);
            if (plan == null || plan.openings.Count == 0)
            {
                // Magnet wouldn't move anything — cancel without spending.
                CancelSkillMode();
                return;
            }

            // Spend ONE stored Magnet use. If empty, abort — SkillManager should have shown the
            // BuyUsesPanel before letting the player enter Magnet mode with 0 uses.
            if (!PlayerEconomy.TrySpendMagnetUse()) return;

            skillMode = SkillMode.None;
            SkillModeChanged?.Invoke();

            StartCoroutine(DoMagnetAnimated(plan));
        }

        class MagnetPlan
        {
            public Column targetCol;
            public string targetColor;
            public int slotCount;
            public List<MagnetCandidate> gathered;       // pieces that gather into targetCol
            public List<Piece> kept;                     // non-match pieces staying in targetCol (top of column)
            public List<Piece> displaced;                // non-match pieces leaving targetCol
            public List<MagnetCandidate> openings;       // empty slots in source cols (1:1 with displaced)
            public Dictionary<Column, List<Piece>> srcIncoming; // displaced assignments per source column
            public HashSet<Piece> gatheredSet;
            public HashSet<Column> affectedSources;
        }

        MagnetPlan BuildMagnetPlan(Column targetCol, string targetColor)
        {
            var allCols = GameManager.Instance != null ? GameManager.Instance.Columns : null;
            if (allCols == null) return null;

            int targetIdx = -1;
            for (int i = 0; i < allCols.Count; i++) if (allCols[i] == targetCol) { targetIdx = i; break; }
            if (targetIdx < 0) return null;

            int slotCount = CountPieces(targetCol);
            if (slotCount == 0) return null;

            // Collect every same-color piece in any UNLOCKED, UNFROZEN column (target included).
            // Skip Rainbow (wildcard, by design doesn't get magneted) and hidden Questionmark
            // (its real color isn't yet known to the player). Frozen columns are completely
            // off-limits to Magnet — pieces inside aren't pulled out, and they don't receive
            // displaced pieces either.
            var candidates = new List<MagnetCandidate>();
            for (int ci = 0; ci < allCols.Count; ci++)
            {
                var c = allCols[ci];
                if (c == null || c.IsLocked) continue;
                if (c.IsFrozen) continue;
                if (c.HasActiveTie()) continue;   // tied columns are off-limits — never gather from / displace into a tie
                int slot = 0;
                for (int i = 0; i < c.transform.childCount; i++)
                {
                    var p = c.transform.GetChild(i).GetComponent<Piece>();
                    if (p == null) continue;
                    if (!p.IsRainbow && !(p.IsQuestionmark && !p.IsRevealed) && p.Color == targetColor)
                        candidates.Add(new MagnetCandidate { piece = p, src = c, srcSlot = slot, srcColIdx = ci });
                    slot++;
                }
            }
            if (candidates.Count == 0) return null;

            // Prefer closer columns; within the same column prefer pieces closer to the bottom
            // (they'll end up at the bottom of the target column anyway, less visual travel).
            int targetIdxClosure = targetIdx;
            candidates.Sort((a, b) =>
            {
                int da = Mathf.Abs(a.srcColIdx - targetIdxClosure);
                int db = Mathf.Abs(b.srcColIdx - targetIdxClosure);
                if (da != db) return da.CompareTo(db);
                return b.srcSlot.CompareTo(a.srcSlot);
            });

            int gatherCount = Mathf.Min(candidates.Count, slotCount);
            var gathered = candidates.GetRange(0, gatherCount);

            // Non-matches in target column, in sibling order (top first).
            var targetPieces = SnapshotColumnPieces(targetCol.transform);
            var gatheredSet = new HashSet<Piece>();
            foreach (var g in gathered) gatheredSet.Add(g.piece);

            var nonMatchInTarget = new List<Piece>();
            foreach (var p in targetPieces) if (!gatheredSet.Contains(p)) nonMatchInTarget.Add(p);

            int keepCount = Mathf.Clamp(slotCount - gatherCount, 0, nonMatchInTarget.Count);
            var kept      = nonMatchInTarget.GetRange(0, keepCount);
            var displaced = nonMatchInTarget.GetRange(keepCount, nonMatchInTarget.Count - keepCount);

            // One opening per gathered piece that came from OUTSIDE the target column.
            var openings = new List<MagnetCandidate>();
            foreach (var g in gathered) if (g.src != targetCol) openings.Add(g);

            // Pair displaced[i] → openings[i].src by order (counts are equal by construction).
            var srcIncoming = new Dictionary<Column, List<Piece>>();
            var affectedSources = new HashSet<Column>();
            for (int i = 0; i < displaced.Count && i < openings.Count; i++)
            {
                var src = openings[i].src;
                affectedSources.Add(src);
                if (!srcIncoming.TryGetValue(src, out var list)) { list = new List<Piece>(); srcIncoming[src] = list; }
                list.Add(displaced[i]);
            }
            // affectedSources also includes any column that lost a piece, even if it didn't receive one
            // (can happen when displaced.Count < openings.Count — i.e. fewer non-matches than openings).
            foreach (var g in gathered) if (g.src != targetCol) affectedSources.Add(g.src);

            return new MagnetPlan
            {
                targetCol       = targetCol,
                targetColor     = targetColor,
                slotCount       = slotCount,
                gathered        = gathered,
                kept            = kept,
                displaced       = displaced,
                openings        = openings,
                srcIncoming     = srcIncoming,
                gatheredSet     = gatheredSet,
                affectedSources = affectedSources,
            };
        }

        IEnumerator DoMagnetAnimated(MagnetPlan plan)
        {
            isAnimating = true;
            SfxManager.Play(SfxId.Magnet);

            // --- 1. Reparent gathered & displaced pieces (worldPositionStays so visuals don't snap) ---
            foreach (var g in plan.gathered)
            {
                if (g.piece.transform.parent != plan.targetCol.transform)
                    g.piece.transform.SetParent(plan.targetCol.transform, worldPositionStays: true);
            }
            for (int i = 0; i < plan.displaced.Count && i < plan.openings.Count; i++)
            {
                plan.displaced[i].transform.SetParent(plan.openings[i].src.transform, worldPositionStays: true);
            }

            // --- 2. Assign final sibling order. Target = kept (top) + gathered (bottom). ---
            int si = 0;
            foreach (var k in plan.kept)     k.transform.SetSiblingIndex(si++);
            foreach (var g in plan.gathered) g.piece.transform.SetSiblingIndex(si++);

            // --- 3. Each affected source column: incoming displaced first (top), then remaining originals. ---
            foreach (var src in plan.affectedSources)
            {
                var incoming = plan.srcIncoming.TryGetValue(src, out var list) ? list : null;
                // Original children at this point: [original remaining + incoming displaced + (possibly) gathered still parented here]
                // Gathered pieces from this src were reparented to target in step 1 — they're not children anymore.
                var current = SnapshotColumnPieces(src.transform);
                var remaining = new List<Piece>();
                foreach (var p in current)
                {
                    if (incoming != null && incoming.Contains(p)) continue;
                    remaining.Add(p);
                }
                int s = 0;
                if (incoming != null) foreach (var p in incoming) p.transform.SetSiblingIndex(s++);
                foreach (var p in remaining) p.transform.SetSiblingIndex(s++);
            }

            // --- 4. Animate every piece toward its new local slot. ---
            var anims = new List<Coroutine>();
            int stagger = 0;

            // Gathered → bottom of target column. Stagger by source distance so the chain reads visually.
            for (int i = 0; i < plan.gathered.Count; i++)
            {
                var p = plan.gathered[i].piece;
                Vector3 target = SlotLocal(plan.targetCol, p.transform.GetSiblingIndex());
                float delay = stagger++ * magnetStaggerSeconds;
                anims.Add(StartCoroutine(DelayedArc(p, target, magnetFlightDuration, magnetArcHeight, delay)));
            }

            // Kept pieces in target may have moved sibling index (rare — usually slot 0 stays slot 0,
            // but if slot count >= kept count + gathered count we'd otherwise have gaps). Re-seat them.
            for (int i = 0; i < plan.kept.Count; i++)
            {
                var p = plan.kept[i];
                Vector3 target = SlotLocal(plan.targetCol, p.transform.GetSiblingIndex());
                anims.Add(StartCoroutine(DelayedArc(p, target, magnetFlightDuration, magnetArcHeightSmall, 0f)));
            }

            // Displaced → top of source columns. Stagger continues the chain.
            for (int i = 0; i < plan.displaced.Count && i < plan.openings.Count; i++)
            {
                var p = plan.displaced[i];
                Vector3 target = SlotLocal(plan.openings[i].src, p.transform.GetSiblingIndex());
                float delay = stagger++ * magnetStaggerSeconds;
                anims.Add(StartCoroutine(DelayedArc(p, target, magnetFlightDuration, magnetArcHeight, delay)));
            }

            // Each source column's remaining pieces may have shifted slot — re-seat them.
            foreach (var src in plan.affectedSources)
            {
                var current = SnapshotColumnPieces(src.transform);
                foreach (var p in current)
                {
                    if (plan.gatheredSet.Contains(p)) continue;
                    if (plan.srcIncoming.TryGetValue(src, out var list) && list.Contains(p)) continue;
                    Vector3 target = SlotLocal(src, p.transform.GetSiblingIndex());
                    anims.Add(StartCoroutine(DelayedArc(p, target, magnetFlightDuration, magnetArcHeightSmall, 0f)));
                }
            }

            foreach (var co in anims) yield return co;

            // --- 5. Finalize: layout, reveal Q?, eval locks, celebrate, refresh hand state. ---
            plan.targetCol.Layout();
            foreach (var src in plan.affectedSources) src.Layout();

            int revealThreshold = 2;
            if (LevelLoader.Instance != null && LevelLoader.Instance.CurrentLevel != null)
                revealThreshold = LevelLoader.Instance.CurrentLevel.questionmarkRevealFromBottom;
            plan.targetCol.CheckRevealQuestionmarks(revealThreshold);
            foreach (var src in plan.affectedSources) src.CheckRevealQuestionmarks(revealThreshold);

            plan.targetCol.EvaluateLock();
            foreach (var src in plan.affectedSources) src.EvaluateLock();

            // Celebrate any newly-locked column. Target first (most expected), then sources.
            if (plan.targetCol.IsLocked)
            {
                if (celebrationDelay > 0f) yield return new WaitForSeconds(celebrationDelay);
                yield return StartCoroutine(RunCelebration(plan.targetCol));
            }
            foreach (var src in plan.affectedSources)
            {
                if (src.IsLocked)
                {
                    if (celebrationDelay > 0f) yield return new WaitForSeconds(celebrationDelay);
                    yield return StartCoroutine(RunCelebration(src));
                }
            }

            lastMoveColumn = null;
            lastMoveLockedColumn = false;

            yield return StartCoroutine(UpdateRainbowSinkOpportunitiesAnimated());
            StateChanged?.Invoke();

            isAnimating = false;
        }

        static int CountPieces(Column col)
        {
            int n = 0;
            for (int i = 0; i < col.transform.childCount; i++)
                if (col.transform.GetChild(i).GetComponent<Piece>() != null) n++;
            return n;
        }

        static Vector3 SlotLocal(Column col, int slot)
        {
            return col.LayoutDirection * (slot * col.PieceSpacing);
        }

        IEnumerator DelayedArc(Piece p, Vector3 targetLocalPos, float duration, float arcHeight, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            yield return StartCoroutine(p.AnimateLocalArcTo(targetLocalPos, p.RestRotation, duration, arcHeight, Vector3.up, Easing.SmoothStep));
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

        /// <summary>
        /// Common post-move logic: instant Q? reveal (no-op if animated path already revealed them),
        /// lock eval, move counter, StateChanged. Does NOT run the rainbow-sink scan — callers do that
        /// separately so the animated path can yield on the animated sink.
        /// </summary>
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

            SfxManager.Play(SfxId.Rewind);

            // Capture what we need, then CLEAR the undo state SYNCHRONOUSLY so a second tap during the
            // animation can't re-trigger (CanUndo flips false immediately; isAnimating also guards input).
            var col = lastMoveColumn;
            bool wasLocked = lastMoveLockedColumn;
            var heldNow = heldPiece;
            heldPiece = null;
            lastMoveColumn = null;
            lastMoveLockedColumn = false;

            isAnimating = true;
            StartCoroutine(DoUndoAnimated(col, heldNow, topPiece, wasLocked));
        }

        /// <summary>
        /// Animated reverse of the last move (Rewind): the held piece arcs from the hand back DOWN into the
        /// column's bottom slot while that column's TOP piece arcs back UP into the hand, and the remaining
        /// pieces slide one slot toward the top. Mirrors <see cref="DoMoveAnimated"/> so a rewind reads as
        /// the move "playing backwards" rather than an instant snap. State was already cleared in Undo().
        /// </summary>
        IEnumerator DoUndoAnimated(Column col, Piece heldNow, Piece topPiece, bool wasLocked)
        {
            // Unlock first so colliders re-enable and reparenting works cleanly.
            if (wasLocked) col.Unlock();

            // Reparent WITHOUT snapping (worldPositionStays) so each piece keeps its current on-screen
            // position as the arc's start: heldNow stays at the hand, topPiece stays at slot 0.
            heldNow.transform.SetParent(col.transform, worldPositionStays: true);
            heldNow.transform.SetSiblingIndex(col.transform.childCount - 1);   // bottom slot
            topPiece.transform.SetParent(handAnchor, worldPositionStays: true);

            // Capture start positions + each remaining piece's new slot (topPiece is gone to the hand).
            Vector3 layoutDir = col.LayoutDirection;
            float spacing = col.PieceSpacing;
            var startLocal = new Dictionary<Piece, Vector3>();
            var targets = new Dictionary<Piece, Vector3>();
            int slot = 0;
            for (int i = 0; i < col.transform.childCount; i++)
            {
                var p = col.transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                startLocal[p] = p.transform.localPosition;
                targets[p] = layoutDir * (slot * spacing);
                slot++;
            }

            var anims = new List<Coroutine>();

            // Held piece → bottom slot: WORLD-up lob (robust to the ≈90° board tilt, same as the forward drop).
            if (targets.TryGetValue(heldNow, out var heldTarget))
                anims.Add(StartCoroutine(heldNow.AnimateWorldArcTo(
                    heldTarget, heldNow.RestRotation, heldToTopDuration, dropArcHeight, Easing.SmoothStep, emitTrail: true)));

            // Top piece → hand: arc + land bounce, exactly like a forward move's ejected piece.
            anims.Add(StartCoroutine(EjectedArcThenBounce(topPiece, heldPieceLocalOffset)));

            // Remaining pieces shift one slot toward the top — short straight slides.
            foreach (var kv in targets)
            {
                var p = kv.Key;
                if (p == heldNow) continue;
                if ((startLocal[p] - kv.Value).sqrMagnitude < 1e-6f) continue;
                anims.Add(StartCoroutine(p.AnimateLocalTo(kv.Value, p.RestRotation, shiftAndPopDuration, Easing.SmoothStep)));
            }

            foreach (var co in anims) yield return co;

            // The top piece is now the held piece (matches the old PlaceInHand result).
            heldPiece = topPiece;

            col.Layout();                       // snap to exact final slots, neutralizing float drift
            GameManager.Instance?.RefundMove();
            isAnimating = false;

            // Held piece changed after undo — recheck sink opportunities.
            UpdateRainbowSinkOpportunities();
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Scans every unlocked column and, if it's one swap away from completion with the
        /// currently-held color, sinks its Rainbow(s) to the bottom so the player can finish next click.
        /// Instant version — used by the instant move path and by Undo.
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
                if (c.IsFrozen) continue;   // Frozen columns are inert — no auto-sink, no shifts.
                var targets = c.GetSinkTargets(heldColor);
                if (targets.Count > 0)
                {
                    c.SinkPiecesToBottom(targets);
                    c.Layout();
                }
            }
        }

        /// <summary>
        /// Animated variant: when one or more columns need a rainbow sink, lerps every affected
        /// piece from its current position to its new slot in parallel. Awaits all sinks before
        /// returning. Safe to call when no column needs sinking — completes immediately.
        /// </summary>
        public IEnumerator UpdateRainbowSinkOpportunitiesAnimated()
        {
            if (heldPiece == null || heldPiece.IsRainbow) yield break;
            if (GameManager.Instance == null) yield break;

            var heldColor = heldPiece.Color;
            var cols = GameManager.Instance.Columns;
            var sinkAnims = new List<Coroutine>();
            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                if (c == null || c.IsLocked) continue;
                if (c.IsFrozen) continue;   // Frozen columns are inert — no auto-sink, no shifts.
                var targets = c.GetSinkTargets(heldColor);
                if (targets.Count > 0)
                    sinkAnims.Add(StartCoroutine(AnimateSink(c, targets)));
            }
            foreach (var co in sinkAnims) yield return co;
        }

        /// <summary>
        /// Animates a single column's auto-sink shuffle (rainbows, or a single odd-coloured piece —
        /// see <see cref="Column.GetSinkTargets"/>). Captures current piece positions, reorders siblings
        /// via <see cref="Column.SinkPiecesToBottom"/>, then lerps each piece from its old position to
        /// the new slot. Pieces whose position doesn't change (already in place) are skipped.
        /// </summary>
        IEnumerator AnimateSink(Column col, List<Piece> sinkTargets)
        {
            var pieces = SnapshotColumnPieces(col.transform);
            if (pieces.Count == 0) yield break;

            // The odd / Rainbow piece is about to slide to the bottom (column is one drop from completion).
            SfxManager.Play(SfxId.Sink);

            // Capture old positions BEFORE reordering — sibling-index changes don't move objects,
            // but we still want the lerp source to be wherever each piece currently sits.
            var startLocal = new Dictionary<Piece, Vector3>(pieces.Count);
            foreach (var p in pieces) startLocal[p] = p.transform.localPosition;

            // Reorder siblings so the sink targets drop to the bottom.
            col.SinkPiecesToBottom(sinkTargets);

            // Compute target positions from the new sibling order. Don't call Layout() yet —
            // that would snap pieces instantly; we want to lerp instead.
            var targets = new Dictionary<Piece, Vector3>(pieces.Count);
            int slot = 0;
            for (int i = 0; i < col.transform.childCount; i++)
            {
                var p = col.transform.GetChild(i).GetComponent<Piece>();
                if (p == null) continue;
                targets[p] = col.LayoutDirection * (slot * col.PieceSpacing);
                slot++;
            }

            // Arc each piece from its captured start to its new slot in parallel. Pieces used to slide
            // STRAIGHT down the column line, so a piece going down and another coming up passed THROUGH
            // each other on screen. Instead they now hop up (in-plane, up-screen along Vector3.up — same
            // axis as the celebration/reveal hop) and swap. The piece sinking to the bottom hops HIGH and
            // the displaced piece(s) hop LOW, so where their paths cross they sit at different screen
            // heights and never visually overlap.
            var anims = new List<Coroutine>();
            foreach (var p in pieces)
            {
                Vector3 start = startLocal[p];
                Vector3 end   = targets.TryGetValue(p, out var t) ? t : start;
                if ((start - end).sqrMagnitude < 1e-6f) continue;
                p.transform.localPosition = start; // ensure starting position is clean

                // Direction along the column: moving toward the bottom slot (the sinker) = negative along
                // LayoutDirection; being pushed up = positive. Different hop heights per direction keep the
                // crossing pieces apart.
                float along = Vector3.Dot(end - start, col.LayoutDirection);
                float arcHeight = along < 0f ? sinkArcHeightDown : sinkArcHeightUp;
                anims.Add(StartCoroutine(p.AnimateLocalArcTo(end, p.RestRotation, rainbowSinkDuration, arcHeight, Vector3.up, Easing.SmoothStep)));
            }
            foreach (var co in anims) yield return co;

            // Snap to exact final positions to neutralize any tiny float error.
            col.Layout();
        }

        void PlaceInHand(Piece p)
        {
            p.transform.SetParent(handAnchor, worldPositionStays: false);
            // Use the per-prefab offset (defaults to zero) so the held piece visually lines up with
            // HandPlace for whichever prefab is loaded — survives swaps, undos, and rebuilds.
            p.transform.localPosition = heldPieceLocalOffset;
            // Use the piece's RestRotation (captured from its prefab) instead of identity — prefabs
            // like Card.fbx need a non-identity rotation to face camera correctly.
            p.transform.localRotation = p.RestRotation;
            // Intentionally do NOT touch localScale — preserve the prefab's scale.
            heldPiece = p;
        }
    }
}
