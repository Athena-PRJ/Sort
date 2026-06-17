using UnityEngine;
using UnityEngine.UI;
using Unity.VectorGraphics;

namespace Sort
{
    /// <summary>
    /// OBSOLETE / inert. Badge art (Level / Move) is now FIXED in the scene and recolored per level via
    /// <see cref="UiRadiantTint"/>, so this no longer swaps sprites from LevelData. The class is kept only
    /// so existing scene components don't become "missing script"; Apply() just fills a fallback sprite if
    /// the graphic has none. Safe to remove the component from your badge objects.
    /// </summary>
    public class LevelBadgeFrame : MonoBehaviour
    {
        [Tooltip("Optional placeholder sprite, applied only if the graphic has no sprite assigned.")]
        [SerializeField] private Sprite fallbackSprite;

        public void Apply()
        {
            if (fallbackSprite == null) return;

            var svg = GetComponent<SVGImage>();
            if (svg != null) { if (svg.sprite == null) svg.sprite = fallbackSprite; return; }
            var img = GetComponent<Image>();
            if (img != null && img.sprite == null) img.sprite = fallbackSprite;
        }
    }
}
