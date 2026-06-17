using UnityEditor;

namespace Sort.EditorTools
{
    /// <summary>
    /// Caches the project's <see cref="PrefabRegistry"/> so property drawers don't call
    /// <c>AssetDatabase.FindAssets</c> on EVERY inspector repaint. That per-repaint scan — multiplied by
    /// the ~15 color fields a LevelData shows — was the main cause of the laggy LevelData inspector.
    ///
    /// The cache is static, so it survives across repaints and clears automatically on domain reload
    /// (any script recompile). If you add or move the registry asset mid-session and the drawers don't
    /// pick it up, call <see cref="Invalidate"/> (or just recompile).
    /// </summary>
    public static class RegistryCache
    {
        static PrefabRegistry _registry;

        public static PrefabRegistry Registry
        {
            get
            {
                if (_registry != null) return _registry;
                var guids = AssetDatabase.FindAssets("t:" + nameof(PrefabRegistry));
                if (guids == null || guids.Length == 0) return null;
                _registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(AssetDatabase.GUIDToAssetPath(guids[0]));
                return _registry;
            }
        }

        public static void Invalidate() => _registry = null;
    }
}
