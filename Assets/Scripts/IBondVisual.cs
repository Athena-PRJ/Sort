using System.Collections;
using System.Collections.Generic;

namespace Sort
{
    /// <summary>
    /// Common surface for a bond visual that binds tied pieces in adjacent columns — the X-shape
    /// <see cref="TieVisual"/> (always 2 pieces) or the bar-shaped <see cref="LockVisual"/> (2..N pieces
    /// spanning a row). Lets <see cref="LevelLoader"/> spawn whichever the level's <see cref="BondStyle"/>
    /// selects, and lets <see cref="PlayerHand"/> break whichever without caring which it is. Implementers
    /// are MonoBehaviours, so callers can treat them as <c>Component</c> for <c>gameObject</c>/lifetime.
    /// </summary>
    public interface IBondVisual
    {
        /// <summary>The pieces this bond spans (2 for a tie, 2..N for a lock). Used to match a broken bond.</summary>
        IReadOnlyList<Piece> Pieces { get; }

        /// <summary>True if this bond includes <paramref name="p"/> (so a broken pair maps to this visual).</summary>
        bool Covers(Piece p);

        /// <summary>Bind to the pieces (left→right order) and snap into place. Called right after Instantiate.</summary>
        void Bind(IReadOnlyList<Piece> pieces);

        /// <summary>Play the one-shot break effect over <paramref name="duration"/> seconds, then self-destruct.</summary>
        IEnumerator PlayBreak(float duration);
    }
}
