using UnityEngine;

namespace Sort
{
    /// <summary>
    /// A vertical 2-color "radiant" (top → bottom gradient). Used by the central per-level UI theme
    /// (the slots of a <see cref="UiThemePalette"/> a level points to via LevelData.themeSet) so many UI
    /// elements can share — and be re-tuned from — one place. Set top == bottom for a flat color.
    ///
    /// Serialized as a CLASS (not a struct) on purpose: struct Color fields would default to (0,0,0,0)
    /// = transparent black, making everything invisible until set. A class with field initializers
    /// defaults to opaque white instead.
    /// </summary>
    [System.Serializable]
    public class GradientColor
    {
        [Tooltip("Color at the TOP of the element.")]
        public Color top = Color.white;
        [Tooltip("Color at the BOTTOM of the element.")]
        public Color bottom = Color.white;

        public GradientColor() { }
        public GradientColor(Color t, Color b) { top = t; bottom = b; }

        /// <summary>Color at normalized vertical position t (0 = bottom, 1 = top).</summary>
        public Color Sample(float t01) => Color.Lerp(bottom, top, Mathf.Clamp01(t01));
    }

    /// <summary>
    /// Named slots in the per-level UI theme. Each element (In frame, Out arrows, SkillBar, …) picks a
    /// slot; tuning that slot on the LevelData re-colors every element using it, in sync.
    /// </summary>
    // Internal identifiers stay (Primary/Secondary/…) so code & serialized slot ints don't change;
    // [InspectorName] just relabels them in the Inspector dropdowns to the designer-facing names.
    public enum UiThemeSlot
    {
        [InspectorName("In + Out")] Primary,    // In frames + Out arrows (board status indicators)
        [InspectorName("Not Done")] Secondary,  // Not Done icon (inside the In frame while unsolved)
        [InspectorName("Accent")]   Accent,     // SkillBar / action UI
        [InspectorName("Text")]     Thirdary,   // text color
        [InspectorName("Backup")]   Backup      // spare slot
    }
}
