using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sort.EditorTools
{
    /// <summary>
    /// Dev tool: a window listing every LevelData asset with a one-click "▶ Play" that jumps straight
    /// into that level in Play mode — no need to start from Level 1 and climb. Open via the menu bar:
    /// <c>Sort ▸ Level Jump</c>.
    ///
    /// How the jump works: clicking Play stashes the level's asset GUID in SessionState, opens the
    /// gameplay scene if it isn't already active, then enters Play mode. <see cref="LevelQuickPlay"/>
    /// (a runtime hook) reads that GUID before the first scene loads and assigns it to
    /// <see cref="LevelProgress.SelectedLevel"/>, so LevelLoader builds it.
    /// </summary>
    public class LevelJumpWindow : EditorWindow
    {
        const string PrefKeyScene = "Sort.LevelJump.GameScenePath";
        const string DefaultSceneName = "SampleScene";

        Vector2 scroll;
        string filter = string.Empty;
        string gameScenePath;
        readonly List<LevelData> levels = new List<LevelData>();

        [MenuItem("Sort/▶ Level Jump")]
        public static void Open()
        {
            var win = GetWindow<LevelJumpWindow>("Level Jump");
            win.minSize = new Vector2(320, 200);
            win.RefreshLevels();
        }

        void OnEnable()
        {
            gameScenePath = EditorPrefs.GetString(PrefKeyScene, string.Empty);
            if (string.IsNullOrEmpty(gameScenePath) || !System.IO.File.Exists(gameScenePath))
                gameScenePath = FindSceneByName(DefaultSceneName);
            RefreshLevels();
        }

        void RefreshLevels()
        {
            levels.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:LevelData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>(path);
                if (lvl != null) levels.Add(lvl);
            }
            // Order by the in-game level number, then asset name, so the list reads like the level map.
            levels.Sort((a, b) =>
            {
                int c = a.levelNumber.CompareTo(b.levelNumber);
                return c != 0 ? c : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        void OnGUI()
        {
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Game scene", GUILayout.Width(72));
                string display = string.IsNullOrEmpty(gameScenePath) ? "<not found>" : System.IO.Path.GetFileName(gameScenePath);
                if (GUILayout.Button(display, EditorStyles.popup))
                    PickGameScene();
                if (GUILayout.Button("↻", GUILayout.Width(26))) RefreshLevels();
            }
            EditorGUILayout.LabelField("Tap ▶ to play that level instantly (enters Play mode).", EditorStyles.miniLabel);

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Filter", GUILayout.Width(40));
                filter = EditorGUILayout.TextField(filter);
            }

            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Currently in Play mode. Stop play first, then ▶ a level.", MessageType.Info);

            EditorGUILayout.Space(2);
            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (levels.Count == 0)
                EditorGUILayout.HelpBox("No LevelData assets found in the project.", MessageType.Warning);

            foreach (var lvl in levels)
            {
                if (lvl == null) continue;
                if (!string.IsNullOrEmpty(filter) &&
                    lvl.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    lvl.levelNumber.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                    {
                        if (GUILayout.Button("▶", GUILayout.Width(28), GUILayout.Height(20)))
                            PlayLevel(lvl);
                    }
                    EditorGUILayout.LabelField($"Lv {lvl.levelNumber}", GUILayout.Width(48));
                    EditorGUILayout.LabelField($"{lvl.name}  ·  {lvl.difficulty}", GUILayout.MinWidth(80));
                    if (GUILayout.Button("Select", GUILayout.Width(56), GUILayout.Height(20)))
                    {
                        Selection.activeObject = lvl;
                        EditorGUIUtility.PingObject(lvl);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        void PlayLevel(LevelData level)
        {
            if (level == null) return;
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Level Jump", "Stop Play mode first, then click ▶ again.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(gameScenePath) || !System.IO.File.Exists(gameScenePath))
            {
                gameScenePath = FindSceneByName(DefaultSceneName);
                if (string.IsNullOrEmpty(gameScenePath))
                {
                    EditorUtility.DisplayDialog("Level Jump",
                        $"Couldn't find the gameplay scene (looked for '{DefaultSceneName}'). " +
                        "Click the 'Game scene' button to pick it manually.", "OK");
                    return;
                }
            }

            // Open the gameplay scene if it isn't the active one (offer to save the current scene first).
            var active = SceneManager.GetActiveScene();
            if (active.path != gameScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return; // user cancelled the save prompt
                EditorSceneManager.OpenScene(gameScenePath, OpenSceneMode.Single);
            }

            // Hand the chosen level to LevelQuickPlay (survives the Play-mode domain reload).
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(level));
            SessionState.SetString(LevelQuickPlay.SessionKey, guid);

            EditorApplication.isPlaying = true;
        }

        void PickGameScene()
        {
            string start = Application.dataPath;
            string picked = EditorUtility.OpenFilePanel("Pick the gameplay scene", start, "unity");
            if (string.IsNullOrEmpty(picked)) return;
            // Convert absolute path to a project-relative asset path when possible.
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath); // .../<Project>
            if (picked.StartsWith(projectRoot))
                picked = picked.Substring(projectRoot.Length + 1).Replace('\\', '/');
            gameScenePath = picked;
            EditorPrefs.SetString(PrefKeyScene, gameScenePath);
        }

        static string FindSceneByName(string sceneName)
        {
            // Prefer an enabled scene from Build Settings that matches the name, else any matching scene asset.
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && System.IO.Path.GetFileNameWithoutExtension(s.path) == sceneName)
                    return s.path;

            string best = null;
            foreach (var guid in AssetDatabase.FindAssets("t:SceneAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
                    return path;
                best ??= path;
            }
            return best;
        }
    }
}
