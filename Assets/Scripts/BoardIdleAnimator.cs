using System.Collections;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Plays a gentle "wind gust" idle animation across the board's pieces when the player hasn't
    /// interacted for a while, then repeats periodically until they tap again. Each piece tilts
    /// slightly and returns; a per-column delay makes the gust ripple across the board like wind
    /// crossing it. Pure game-feel — no gameplay effect.
    ///
    /// Setup: drop this component on the Board GameObject (or any always-active object in the scene).
    /// It resets its idle timer off <see cref="PlayerHand.AnyInteraction"/>, reads the live column list
    /// from <see cref="GameManager"/>, and drives <see cref="Piece.AnimateWindSway"/>. All knobs below
    /// are Inspector-tunable at any time.
    /// </summary>
    public class BoardIdleAnimator : MonoBehaviour
    {
        [Header("Idle trigger")]
        [Tooltip("Master on/off for the idle wind sway.")]
        [SerializeField] private bool idleEnabled = true;
        [Tooltip("Seconds of no player interaction before the first gust plays.")]
        [SerializeField] private float idleThreshold = 10f;
        [Tooltip("Seconds between repeated gusts while the player stays idle.")]
        [SerializeField] private float repeatInterval = 6f;

        [Header("Gust shape")]
        [Tooltip("Peak tilt of each piece during a gust (degrees). Keep small for a subtle breeze.")]
        [SerializeField] private float swayAngle = 7f;
        [Tooltip("How long each piece's sway lasts (seconds) — rises to the peak tilt then returns.")]
        [SerializeField] private float swayDuration = 0.9f;
        [Tooltip("Delay added per column index so the gust ripples across the board like wind crossing " +
                 "it (seconds per column). 0 = every column sways in unison.")]
        [SerializeField] private float perColumnDelay = 0.08f;

        float nextGustTime;
        bool gustRunning;

        void OnEnable()
        {
            PlayerHand.AnyInteraction += ResetIdle;
            ResetIdle();
        }

        void OnDisable() => PlayerHand.AnyInteraction -= ResetIdle;

        void ResetIdle() => nextGustTime = Time.time + idleThreshold;

        void Update()
        {
            if (!idleEnabled || gustRunning) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            if (Time.time >= nextGustTime) StartCoroutine(PlayGust());
        }

        IEnumerator PlayGust()
        {
            gustRunning = true;

            var columns = GameManager.Instance != null ? GameManager.Instance.Columns : null;
            float maxDelay = 0f;
            if (columns != null)
            {
                for (int c = 0; c < columns.Count; c++)
                {
                    var col = columns[c];
                    if (col == null) continue;
                    float delay = c * perColumnDelay;
                    if (delay > maxDelay) maxDelay = delay;
                    for (int i = 0; i < col.transform.childCount; i++)
                    {
                        var p = col.transform.GetChild(i).GetComponent<Piece>();
                        // Run the sway ON the piece so it survives if this animator is toggled, and
                        // stops cleanly if the board is torn down between levels.
                        if (p != null) p.StartCoroutine(p.AnimateWindSway(swayAngle, swayDuration, delay));
                    }
                }
            }

            // Wait for the whole ripple (slowest column's delay + its sway) before allowing the next
            // gust, so gusts never overlap on the same piece (which would compound the tilt).
            yield return new WaitForSeconds(maxDelay + swayDuration);
            nextGustTime = Time.time + repeatInterval;
            gustRunning = false;
        }
    }
}
