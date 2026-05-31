using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Tiny set of easing functions for hand-rolled tweening. Each takes t in [0,1]
    /// and returns a shaped value in roughly [0,1] (EaseOutBack briefly overshoots > 1).
    /// Use with <see cref="Piece.AnimateLocalTo"/>.
    /// </summary>
    public static class Easing
    {
        public static float Linear(float t) => Mathf.Clamp01(t);

        /// <summary>Symmetric S-curve. Good "smooth glide" for column shift.</summary>
        public static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Decelerates as it lands. Good for held-piece arriving on top of column.</summary>
        public static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
        }

        /// <summary>Accelerates from rest. Good for the "fall back down" half of celebration hops.</summary>
        public static float EaseIn(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
        }

        /// <summary>Decelerates with a slight overshoot. Snappy "pop into hand" feel.</summary>
        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t = Mathf.Clamp01(t);
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
