using System;
using System.Collections;
using UnityEngine;

namespace Sort
{
    [RequireComponent(typeof(Collider))]
    public class Piece : MonoBehaviour
    {
        [SerializeField] private PieceColor color;
        [SerializeField] private bool isRainbow;
        [SerializeField] private bool isQuestionmark;
        [SerializeField] private bool revealed = true;

        [Header("Tinting")]
        [Tooltip("Renderers tinted by the piece color AND material-swapped for special states. " +
                 "If empty, auto-discovers all MeshRenderers in children.")]
        [SerializeField] private MeshRenderer[] targetRenderers;

        [Header("Material overrides for special states")]
        [Tooltip("Shared rainbow material — assign mat_rainbow here. Shown when isRainbow.")]
        [SerializeField] private Material rainbowMaterial;
        [Tooltip("Shared questionmark material — assign mat_questionmark here. Tints the Lego mesh in the 'hidden' color when isQuestionmark && !revealed.")]
        [SerializeField] private Material questionmarkMaterial;

        [Header("Overlay (optional flat-quad child layered on top of the mesh)")]
        [Tooltip("Child GameObject (e.g. a Quad with the '?' symbol texture) shown only when isQuestionmark && !revealed.")]
        [SerializeField] private GameObject questionmarkOverlay;

        [Header("Fallback tints (used if no override material assigned)")]
        [SerializeField] private Color rainbowDisplayColor = new Color(1f, 1f, 1f);
        [SerializeField] private Color questionmarkDisplayColor = new Color(0.55f, 0.55f, 0.6f);

        [Header("Motion trail")]
        [Tooltip("Optional TrailRenderer for racing-game-style motion blur during ARC animations " +
                 "(drop, pop, switch, magnet). Auto-discovered from children in Awake if left null. " +
                 "Emitting is toggled ON before each AnimateLocalArcTo and OFF after — straight-line " +
                 "shifters and rainbow sinks intentionally do NOT trail to avoid visual clutter.\n\n" +
                 "Setup checklist: add TrailRenderer component to the prefab root (or a child), " +
                 "assign a transparent/additive material (e.g. URP Unlit with alpha), set Time (~0.3s), " +
                 "Min Vertex Distance (~0.05), tune Width Curve (wide → narrow) and Color Gradient (alpha fade). " +
                 "Leave Emitting OFF in the prefab — code turns it on per animation.")]
        [SerializeField] private TrailRenderer trail;
        [Tooltip("Trail width as a fraction of the piece's world-space width (BoxCollider.size.x × lossyScale.x). " +
                 "0.75 = trail is 75% as wide as the piece. Recomputed every time the trail starts emitting, so the " +
                 "value automatically scales with PrefabRegistry's pieceScale AND with per-level board auto-fit — no " +
                 "manual retune per prefab/level needed. Set to 0 to disable auto-sizing (designer controls " +
                 "TrailRenderer.widthMultiplier directly).")]
        [SerializeField, Range(0f, 2f)] private float trailWidthPercent = 0.75f;

        // Rainbow animation lives in the shader (Sort/RainbowFlow): _Time.y * _Speed drives the
        // flow procedurally, no per-frame UV scroll needed. The shader's _FlowDir / _Speed /
        // _Saturation properties on mat_rainbow.mat are the tuning surface — open that material
        // in the Inspector to adjust. If you ever switch back to a texture-based rainbow material
        // (one that reads _BaseMap_ST), restore the rainbowScrollVelocity field + Update() block
        // from git history.

        MaterialPropertyBlock mpb;
        MeshRenderer[] cachedRenderers;
        Material[] cachedOriginalMaterials;

        // Captured in Awake from the prefab's authored localScale. ApplyFitScale multiplies this
        // baseline by the fit factor — never overwrites localScale outright. That preserves the
        // prefab's brick proportions (e.g. Lego at (0.479, 0.721, 1)) across every fit pass.
        Vector3 baselineScale = Vector3.zero;

        // Captured in Awake from the prefab's authored localRotation. Layout() in Column and every
        // animation target in PlayerHand use this as the "rest rotation" instead of Quaternion.identity,
        // so prefabs whose mesh doesn't face the camera at identity rotation (e.g. Card.fbx imports
        // sideways) still render upright. Auto-captured — designer just sets the rotation on the prefab
        // as they want it to look, and the system respects that.
        Quaternion restRotation = Quaternion.identity;
        bool restRotationCaptured;

        public PieceColor Color => color;
        public bool IsRainbow => isRainbow;
        public bool IsQuestionmark => isQuestionmark;
        public bool IsRevealed => revealed;

        /// <summary>
        /// The local rotation this piece should sit at when at rest (in a column slot or held in hand).
        /// Auto-captured from the prefab's authored localRotation in Awake. Code paths that previously
        /// used <see cref="Quaternion.identity"/> as the rest rotation target should use this instead.
        /// </summary>
        public Quaternion RestRotation => restRotation;

        void Awake()
        {
            // Capture the prefab's authored scale BEFORE anything else can touch it.
            // ApplyFitScale multiplies from this baseline instead of overwriting localScale,
            // so per-axis prefab proportions (e.g. Lego (0.479, 0.721, 1)) survive every fit pass.
            if (baselineScale == Vector3.zero) baselineScale = transform.localScale;
            // Same idea for rotation: capture the prefab's authored localRotation so Layout()
            // + animation targets respect it instead of forcing identity (which would flip prefabs
            // like Card.fbx that aren't authored at identity-faces-camera).
            if (!restRotationCaptured)
            {
                restRotation = transform.localRotation;
                restRotationCaptured = true;
            }
            // Auto-discover trail if designer didn't wire it in the Inspector. Disable emitting
            // upfront so the piece doesn't leak a trail during the spawn → initial-Layout positioning.
            if (trail == null) trail = GetComponentInChildren<TrailRenderer>(true);
            if (trail != null) trail.emitting = false;
            CacheOriginalMaterials();
            ApplyVisualState();
        }

        /// <summary>
        /// Turns the motion trail on or off. ON also (a) recomputes trail.widthMultiplier so the
        /// width matches the piece's CURRENT world-space width (adapts per level and per
        /// PrefabRegistry pieceScale) and (b) clears stale trail points so the new emission
        /// session starts fresh at the current position (avoids a straight line from the previous
        /// idle location). OFF stops emission but lets existing tail FADE OUT over TrailRenderer.time —
        /// natural decay instead of an abrupt cut.
        /// </summary>
        public void SetTrailEmitting(bool emit)
        {
            if (trail == null) return;
            if (emit)
            {
                // Auto-size: trail width = (piece world width) × trailWidthPercent. Computed at
                // emit-time so it survives PrefabRegistry pieceScale + per-level auto-fit changes.
                // Designer's Width Curve shape (e.g. 1 → 0 for tapered tail) is preserved — the
                // multiplier just scales the curve's amplitude.
                if (trailWidthPercent > 0f)
                {
                    var bc = GetComponent<BoxCollider>();
                    if (bc != null)
                    {
                        float worldWidth = Mathf.Abs(bc.size.x * transform.lossyScale.x);
                        trail.widthMultiplier = worldWidth * trailWidthPercent;
                    }
                }
                if (!trail.emitting) trail.Clear();
            }
            trail.emitting = emit;
        }

        /// <summary>
        /// Scales the piece uniformly relative to its prefab baseline. Multiplies X, Y, AND Z by
        /// <paramref name="factor"/> — matching the visual behavior of the original auto-fit
        /// (which scaled the parent Board transform uniformly). Safe to call multiple times;
        /// always computes from baselineScale, never accumulates.
        /// </summary>
        public void ApplyFitScale(float factor)
        {
            // Safety net: if Awake hasn't run yet, capture baseline from the current localScale.
            if (baselineScale == Vector3.zero) baselineScale = transform.localScale;
            transform.localScale = baselineScale * factor;
        }

        /// <summary>
        /// Trampoline-style land bounce: scales localScale up to <c>baseline * (1 + overshoot)</c> at
        /// the parabola's apex, then back down to baseline by the end. Use after a piece arrives at
        /// its destination (column → hand path) so it reads as "dropped from height, the impact
        /// caused a small puff before settling".
        /// Multiplies on top of the captured <see cref="baselineScale"/> so PrefabRegistry's pieceScale
        /// values are respected — the bounce is a transient amplification, not an absolute size.
        /// No-op if duration or overshoot is non-positive.
        /// </summary>
        public IEnumerator AnimateLandBounce(float duration, float overshoot)
        {
            if (duration <= 0f || overshoot <= 0f) yield break;
            // Safety net if Awake hasn't captured baselineScale yet (e.g. piece was just instantiated).
            if (baselineScale == Vector3.zero) baselineScale = transform.localScale;

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                // Parabola peaks at u=0.5 with value 1.0 → scales baseline by (1 + overshoot) at apex,
                // returns to baseline (factor 1.0) at u=1.
                float bumpFactor = 1f + overshoot * (4f * u * (1f - u));
                transform.localScale = baselineScale * bumpFactor;
                yield return null;
            }
            transform.localScale = baselineScale;
        }

        /// <summary>
        /// Overrides this piece's baseline (the "1.0× reference scale") with <paramref name="scale"/>
        /// and immediately applies it to <c>transform.localScale</c>. Called by LevelLoader right after
        /// Instantiate when a PrefabRegistry entry supplies a non-zero <c>pieceScale</c> for this prefab —
        /// the registry value supersedes the prefab's authored localScale.
        ///
        /// All future <see cref="ApplyFitScale"/> calls compute from THIS baseline, so designers can
        /// tune piece size from one place (the registry) and auto-fit still scales correctly across
        /// non-standard grids.
        /// </summary>
        public void SetBaselineScale(Vector3 scale)
        {
            if (scale.sqrMagnitude < 1e-6f) return;     // Skip zero/near-zero — would make piece invisible.
            baselineScale = scale;
            transform.localScale = scale;
        }

        void OnValidate()
        {
            // Edit-mode update. OnValidate fires before Awake so we can't rely on cached materials.
            var renderers = GetRenderers();
            if (renderers == null || renderers.Length == 0) return;
            var block = new MaterialPropertyBlock();
            var c = GetTintColor();
            bool forceFlatGray = isQuestionmark && !revealed && questionmarkMaterial == null;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", c);
                // Mirror ApplyVisualState's flat-gray fallback so the Inspector preview matches runtime.
                if (forceFlatGray) block.SetTexture("_BaseMap", Texture2D.whiteTexture);
                r.SetPropertyBlock(block);
            }

            if (questionmarkOverlay != null)
                questionmarkOverlay.SetActive(isQuestionmark && !revealed);
        }

        void CacheOriginalMaterials()
        {
            var renderers = GetRenderers();
            if (renderers == null) return;
            cachedOriginalMaterials = new Material[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) cachedOriginalMaterials[i] = renderers[i].sharedMaterial;
        }

        MeshRenderer[] GetRenderers()
        {
            if (targetRenderers != null && targetRenderers.Length > 0) return targetRenderers;
            if (cachedRenderers == null || cachedRenderers.Length == 0)
            {
                var all = GetComponentsInChildren<MeshRenderer>(true);
                if (questionmarkOverlay == null)
                {
                    cachedRenderers = all;
                }
                else
                {
                    // Exclude renderers that live inside the overlay's hierarchy — we don't want
                    // to overwrite the overlay's material when this piece enters a special state.
                    var list = new System.Collections.Generic.List<MeshRenderer>(all.Length);
                    foreach (var r in all)
                    {
                        if (r == null) continue;
                        if (r.transform.IsChildOf(questionmarkOverlay.transform)) continue;
                        list.Add(r);
                    }
                    cachedRenderers = list.ToArray();
                }
            }
            return cachedRenderers;
        }

        Material GetOverrideMaterial()
        {
            if (isRainbow && rainbowMaterial != null) return rainbowMaterial;
            if (isQuestionmark && !revealed && questionmarkMaterial != null) return questionmarkMaterial;
            return null;
        }

        Color GetTintColor()
        {
            // If a material override exists for this state, keep the tint neutral so the material's texture shows pure.
            if (isRainbow && rainbowMaterial != null) return UnityEngine.Color.white;
            if (isQuestionmark && !revealed && questionmarkMaterial != null) return UnityEngine.Color.white;
            // No override: fall back to placeholder color so the player can at least tell the piece is special.
            if (isRainbow) return rainbowDisplayColor;
            if (isQuestionmark && !revealed) return questionmarkDisplayColor;
            return color.ToUnityColor();
        }

        /// <summary>Applies the correct material AND tint for the current state to every target renderer.</summary>
        void ApplyVisualState()
        {
            var renderers = GetRenderers();
            if (renderers == null || renderers.Length == 0) return;

            Material overrideMat = GetOverrideMaterial();
            Color tintColor = GetTintColor();

            // Hidden-Questionmark fallback path: when no questionmarkMaterial is assigned, the gray
            // questionmarkDisplayColor multiplies the original material's Base Map texture — and that
            // texture is colored (e.g. Box's red), so the result looks like dark red instead of true
            // gray. Fix: override _BaseMap with a plain white texture via MPB so the gray tint shows
            // pure. White is the multiplicative identity for shader sampling — texture-color out =
            // tint-color in. Cleared (texture override removed) the moment piece is no longer hidden.
            bool forceFlatGray = isQuestionmark && !revealed && questionmarkMaterial == null;

            if (mpb == null) mpb = new MaterialPropertyBlock();

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                // Swap to override material when in a special state, restore original otherwise.
                if (overrideMat != null)
                {
                    r.sharedMaterial = overrideMat;
                }
                else if (cachedOriginalMaterials != null && i < cachedOriginalMaterials.Length && cachedOriginalMaterials[i] != null)
                {
                    r.sharedMaterial = cachedOriginalMaterials[i];
                }

                // Reset MPB each pass so stale overrides (e.g. _BaseMap from a previous hidden state,
                // or _BaseMap_ST from rainbow scroll) don't leak into the new state.
                r.SetPropertyBlock(null);
                mpb.Clear();
                mpb.SetColor("_BaseColor", tintColor);
                if (forceFlatGray)
                    mpb.SetTexture("_BaseMap", Texture2D.whiteTexture);
                r.SetPropertyBlock(mpb);
            }

            // Show the '?' overlay only when this piece is an unrevealed Questionmark.
            if (questionmarkOverlay != null)
                questionmarkOverlay.SetActive(isQuestionmark && !revealed);
        }

        public void SetColor(PieceColor c) { color = c; ApplyVisualState(); }

        /// <summary>Configures this piece from a LevelData PieceConfig.</summary>
        public void SetConfig(PieceConfig config)
        {
            if (config == null) return;
            color = config.color;
            isRainbow = config.isRainbow;
            isQuestionmark = config.isQuestionmark;
            revealed = !config.isQuestionmark;
            ApplyVisualState();
        }

        /// <summary>Reveals a Questionmark — swaps back to the original material and applies the real color.</summary>
        public void Reveal()
        {
            if (!isQuestionmark || revealed) return;
            revealed = true;
            ApplyVisualState();
        }

        void OnMouseDown()
        {
            if (PlayerHand.Instance == null || transform.parent == null) return;
            PlayerHand.Instance.OnPieceTapped(this);
        }

        // ---------------------------------------------------------------------
        //  Animation helpers (coroutine-based, hand-rolled — no DOTween).
        // ---------------------------------------------------------------------

        /// <summary>
        /// Lerps the piece's localPosition + localRotation to the given target over <paramref name="duration"/>
        /// seconds, shaped by <paramref name="ease"/>. Snaps instantly if duration &lt;= 0.
        /// </summary>
        public IEnumerator AnimateLocalTo(Vector3 targetLocalPos, Quaternion targetLocalRot, float duration, Func<float, float> ease)
        {
            if (duration <= 0f || !gameObject.activeInHierarchy)
            {
                transform.localPosition = targetLocalPos;
                transform.localRotation = targetLocalRot;
                yield break;
            }

            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float raw = Mathf.Clamp01(t / duration);
                float u = ease == null ? raw : ease(raw);
                transform.localPosition = Vector3.LerpUnclamped(startPos, targetLocalPos, u);
                // Slerp uses clamped u so rotation doesn't overshoot even if pos easing does.
                transform.localRotation = Quaternion.Slerp(startRot, targetLocalRot, raw);
                yield return null;
            }
            transform.localPosition = targetLocalPos;
            transform.localRotation = targetLocalRot;
        }

        /// <summary>
        /// Lerps localPosition along a straight line plus a perpendicular parabolic arc shaped by
        /// <paramref name="arcHeight"/> along <paramref name="arcAxisLocal"/>. Use this when a piece
        /// flies between two slots and the motion should "bow out" — e.g. Switch crossover or Magnet
        /// gathering. Pass a NEGATIVE arcHeight to bow in the opposite direction (handy for crossover
        /// pairs that need to avoid colliding).
        /// </summary>
        public IEnumerator AnimateLocalArcTo(Vector3 targetLocalPos, Quaternion targetLocalRot, float duration, float arcHeight, Vector3 arcAxisLocal, Func<float, float> ease)
        {
            if (duration <= 0f || !gameObject.activeInHierarchy)
            {
                transform.localPosition = targetLocalPos;
                transform.localRotation = targetLocalRot;
                yield break;
            }

            // Trail ON for the duration of the arc — racing-game-style motion blur behind the piece.
            // Toggled here (not at the callsite) so EVERY arc motion (drop, pop, switch, magnet)
            // automatically gets the trail. AnimateLocalTo intentionally does NOT toggle so straight-
            // line shifters and rainbow sinks stay clean.
            SetTrailEmitting(true);

            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;
            Vector3 arcAxis = arcAxisLocal.sqrMagnitude > 0f ? arcAxisLocal.normalized : Vector3.up;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float raw = Mathf.Clamp01(t / duration);
                float u = ease == null ? raw : ease(raw);
                Vector3 linear = Vector3.LerpUnclamped(startPos, targetLocalPos, u);
                // Parabola peaks at raw = 0.5 — symmetric in/out so the arc looks natural.
                float bow = 4f * raw * (1f - raw) * arcHeight;
                transform.localPosition = linear + arcAxis * bow;
                transform.localRotation = Quaternion.Slerp(startRot, targetLocalRot, raw);
                yield return null;
            }
            transform.localPosition = targetLocalPos;
            transform.localRotation = targetLocalRot;

            // Stop emitting — existing tail fades over TrailRenderer.time for a natural decay.
            SetTrailEmitting(false);
        }

        /// <summary>
        /// Questionmark-reveal animation. Parabolic hop along <paramref name="hopDirLocal"/> with rotation
        /// around <paramref name="flipAxisWorld"/>. At <paramref name="revealAt"/> normalized time (default
        /// 0.5 — the apex of the hop), the piece flips its `revealed` flag to true and re-applies its
        /// visual state, swapping the questionmark material for the real color. Lands back at the start
        /// position. No-op if the piece isn't a hidden Questionmark.
        /// </summary>
        public IEnumerator AnimateRevealHop(float duration, float hopHeight, Vector3 hopDirLocal, Vector3 flipAxisWorld, float revealAt = 0.5f, float totalRotationDegrees = 360f)
        {
            if (!isQuestionmark || revealed) yield break;
            if (duration <= 0f) { Reveal(); yield break; }

            Vector3 startLocalPos  = transform.localPosition;
            Quaternion startRotWorld = transform.rotation;
            Vector3 hopDir = hopDirLocal.sqrMagnitude > 0f ? hopDirLocal.normalized : Vector3.up;
            // If caller passes Vector3.zero, spin around the piece's OWN forward direction (mapped
            // to world at start) instead of a fixed world axis. Spinning around a global axis
            // through a tilted piece cuts the rotation axis through the mesh at an angle → mesh
            // visibly swings ("pendulum") instead of spinning in place. transform.forward is
            // always aligned with the piece's local Z, which from the top-down-ish camera reads
            // as the in-plane spin axis (piece rotates like a coin face-up).
            Vector3 axis   = flipAxisWorld.sqrMagnitude > 0f ? flipAxisWorld.normalized : transform.forward;

            bool didSwap = false;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);

                // Parabolic hop peaks at u = 0.5 → matches the celebration animation's feel.
                float hopT = 4f * u * (1f - u);
                transform.localPosition = startLocalPos + hopDir * (hopHeight * hopT);
                transform.rotation = Quaternion.AngleAxis(totalRotationDegrees * u, axis) * startRotWorld;

                // Mid-air visual reveal — swap material at the apex of the hop for dramatic effect.
                if (!didSwap && u >= revealAt)
                {
                    revealed = true;
                    ApplyVisualState();
                    didSwap = true;
                }

                yield return null;
            }

            // Belt-and-suspenders: if a very short duration skipped the swap, do it now.
            if (!didSwap) { revealed = true; ApplyVisualState(); }

            transform.localPosition = startLocalPos;
            transform.rotation = startRotWorld;
        }

        /// <summary>
        /// Column-complete celebration: parabolic hop along <paramref name="hopDirLocal"/> while
        /// rotating <paramref name="totalRotationDegrees"/> around <paramref name="flipAxisWorld"/>
        /// (world space). Pass <see cref="Vector3.zero"/> for flipAxisWorld to spin around the
        /// piece's own up direction — that's the right choice for clean in-place rotation when
        /// the piece is tilted by board rotation or has a non-identity RestRotation.
        /// Returns to the starting localPosition / localRotation when done.
        /// </summary>
        public IEnumerator AnimateCelebrate(float duration, float hopDistance, Vector3 hopDirLocal, Vector3 flipAxisWorld, float totalRotationDegrees = 360f)
        {
            if (duration <= 0f) yield break;

            Vector3 startLocalPos = transform.localPosition;
            Quaternion startRotWorld = transform.rotation;
            Vector3 hopDir = hopDirLocal.sqrMagnitude > 0f ? hopDirLocal.normalized : Vector3.up;
            // Fall back to piece's own forward direction (local Z mapped to world) when no axis
            // is given — eliminates the pendulum wobble you'd see spinning around a fixed world
            // axis through a tilted piece. See AnimateRevealHop above for full reasoning. Z is
            // the right choice for top-down board games where pieces read as "lying face up" —
            // spin reads as in-plane rotation around the piece's center.
            Vector3 axis = flipAxisWorld.sqrMagnitude > 0f ? flipAxisWorld.normalized : transform.forward;

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);

                // Parabolic hop peaks at u = 0.5: 4u(1-u) ∈ [0,1].
                float hopT = 4f * u * (1f - u);
                transform.localPosition = startLocalPos + hopDir * (hopDistance * hopT);

                // Pre-multiply: the rotation is applied around the captured world axis, with the
                // piece's start orientation as the base. Same math as before, just a better axis.
                transform.rotation = Quaternion.AngleAxis(totalRotationDegrees * u, axis) * startRotWorld;

                yield return null;
            }

            transform.localPosition = startLocalPos;
            transform.rotation = startRotWorld;
        }
    }
}
