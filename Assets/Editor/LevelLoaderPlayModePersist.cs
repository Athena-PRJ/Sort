#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sort.EditorScripts
{
    /// <summary>
    /// Auto-persists LevelLoader's <c>autoAlignBoardFrameToColumns</c> toggle across Play → Edit
    /// mode transitions. Captures the value on ExitingPlayMode into EditorPrefs and writes it back
    /// to the scene's LevelLoader on EnteredEditMode, marking the scene dirty so it survives subsequent saves.
    ///
    /// History: this script used to also persist LevelLoader's spacingOverrides[] array. That field
    /// moved into the PrefabRegistry ScriptableObject during the Phase 1 refactor — changes to
    /// ScriptableObjects persist automatically across Play → Edit (asset data, not scene data), so
    /// the array no longer needs this dance. Only the boolean toggle remains here.
    /// </summary>
    [InitializeOnLoad]
    public static class LevelLoaderPlayModePersist
    {
        const string PREF_KEY = "Sort_LevelLoader_PlayModeSnapshot";

        [System.Serializable]
        class Snapshot
        {
            public bool autoAlign;
            public bool hasValue;
        }

        static LevelLoaderPlayModePersist()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) CapturePlayModeValues();
            else if (change == PlayModeStateChange.EnteredEditMode) RestoreToScene();
        }

        static Sort.LevelLoader FindLoader()
        {
            return Object.FindAnyObjectByType<Sort.LevelLoader>(FindObjectsInactive.Include);
        }

        static void CapturePlayModeValues()
        {
            var loader = FindLoader();
            if (loader == null) return;

            var so = new SerializedObject(loader);
            var autoAlign = so.FindProperty("autoAlignBoardFrameToColumns");
            if (autoAlign == null) return;

            var snap = new Snapshot { autoAlign = autoAlign.boolValue, hasValue = true };
            EditorPrefs.SetString(PREF_KEY, JsonUtility.ToJson(snap));
        }

        static void RestoreToScene()
        {
            var json = EditorPrefs.GetString(PREF_KEY, "");
            if (string.IsNullOrEmpty(json)) return;
            EditorPrefs.DeleteKey(PREF_KEY);

            var loader = FindLoader();
            if (loader == null) return;

            Snapshot snap;
            try { snap = JsonUtility.FromJson<Snapshot>(json); }
            catch { return; }
            if (snap == null || !snap.hasValue) return;

            var so = new SerializedObject(loader);
            var autoAlign = so.FindProperty("autoAlignBoardFrameToColumns");
            if (autoAlign == null) return;

            autoAlign.boolValue = snap.autoAlign;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(loader);
            EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);

            Debug.Log("[LevelLoaderPlayModePersist] Restored Play-mode autoAlign toggle to scene. " +
                      "Save the scene (Ctrl+S) to persist on disk.");
        }
    }
}
#endif
