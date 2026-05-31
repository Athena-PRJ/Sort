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

        MaterialPropertyBlock mpb;
        MeshRenderer[] cachedRenderers;
        Material[] cachedOriginalMaterials;

        public PieceColor Color => color;
        public bool IsRainbow => isRainbow;
        public bool IsQuestionmark => isQuestionmark;
        public bool IsRevealed => revealed;

        void Awake()
        {
            CacheOriginalMaterials();
            ApplyVisualState();
        }

        void OnValidate()
        {
            // Edit-mode update. OnValidate fires before Awake so we can't rely on cached materials.
            var renderers = GetRenderers();
            if (renderers == null || renderers.Length == 0) return;
            var block = new MaterialPropertyBlock();
            var c = GetTintColor();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", c);
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

                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", tintColor);
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
            PlayerHand.Instance.HandleColumnClick(transform.parent);
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
        /// Column-complete celebration: parabolic hop along <paramref name="hopDirLocal"/> while
        /// rotating <paramref name="totalRotationDegrees"/> around the WORLD-space <paramref name="flipAxisWorld"/>.
        /// Returns to the starting localPosition / localRotation when done.
        /// </summary>
        public IEnumerator AnimateCelebrate(float duration, float hopDistance, Vector3 hopDirLocal, Vector3 flipAxisWorld, float totalRotationDegrees = 360f)
        {
            if (duration <= 0f) yield break;

            Vector3 startLocalPos = transform.localPosition;
            Quaternion startRotWorld = transform.rotation;
            Vector3 hopDir = hopDirLocal.sqrMagnitude > 0f ? hopDirLocal.normalized : Vector3.up;
            Vector3 axis = flipAxisWorld.sqrMagnitude > 0f ? flipAxisWorld.normalized : Vector3.forward;

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);

                // Parabolic hop peaks at u = 0.5: 4u(1-u) ∈ [0,1].
                float hopT = 4f * u * (1f - u);
                transform.localPosition = startLocalPos + hopDir * (hopDistance * hopT);

                // Pre-multiply so the axis is interpreted in WORLD space (camera-relative),
                // independent of whatever rotation the column/board has.
                transform.rotation = Quaternion.AngleAxis(totalRotationDegrees * u, axis) * startRotWorld;

                yield return null;
            }

            transform.localPosition = startLocalPos;
            transform.rotation = startRotWorld;
        }
    }
}
