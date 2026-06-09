namespace Sort
{
#if UNITY_EDITOR
    /// <summary>
    /// Editor-only bridge for the "Level Jump" dev window (menu: Sort ▸ Level Jump). The window
    /// stashes the chosen level's asset GUID in <see cref="UnityEditor.SessionState"/> and starts
    /// Play mode; this hook reads that GUID BEFORE the first scene loads and assigns
    /// <see cref="LevelProgress.SelectedLevel"/>, so <see cref="LevelLoader"/> builds that level
    /// instead of the default. SessionState survives the Play-mode domain reload, which a plain
    /// static field would not.
    ///
    /// One-shot: the GUID is erased once consumed, so pressing the normal Play button afterwards
    /// falls back to the usual flow (MainMenu / LevelLoader.defaultLevel). Entirely compiled out of
    /// player builds via the UNITY_EDITOR guard.
    /// </summary>
    public static class LevelQuickPlay
    {
        /// <summary>SessionState key the Level Jump window writes and this hook reads. Public so the window shares it.</summary>
        public const string SessionKey = "Sort.LevelJump.SelectedLevelGuid";

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplySelectedLevel()
        {
            string guid = UnityEditor.SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(guid)) return;

            // Consume once so a subsequent plain Play isn't hijacked by a stale jump request.
            UnityEditor.SessionState.EraseString(SessionKey);

            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return;

            var level = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (level != null)
            {
                LevelProgress.SelectedLevel = level;
                UnityEngine.Debug.Log($"[LevelJump] Playing '{level.name}' (Level {level.levelNumber}).");
            }
        }
    }
#endif
}
