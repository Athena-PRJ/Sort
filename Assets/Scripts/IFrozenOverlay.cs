namespace Sort
{
    /// <summary>
    /// Common surface for a frozen-column visual so the runtime (LevelLoader spawn + GameManager
    /// countdown) can drive EITHER the 2D <see cref="FrozenOverlay"/> (ice-strip sprite / fade quad)
    /// OR the 3D <see cref="FrozenColumnIce"/> (segmented mesh column) without caring which is assigned.
    /// Implementers are MonoBehaviours, so callers can cast to <c>Component</c> for <c>gameObject</c>.
    /// </summary>
    public interface IFrozenOverlay
    {
        /// <summary>Position/orient/size the visual over <paramref name="col"/> right after it spawns.</summary>
        void AttachToColumn(Column col);

        /// <summary>Update the displayed "columns left to unfreeze" count (ticks down as columns lock).</summary>
        void SetRemaining(int remaining);
    }
}
