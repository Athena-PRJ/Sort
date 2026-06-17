#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Unity.VectorGraphics;

namespace Sort.EditorTools
{
    /// <summary>
    /// Builds a LAYERED UI prefab: a root RectTransform with one child Graphic per layer (e.g. shadow /
    /// board / fill / stroke). Each layer optionally carries a <see cref="UiRadiantTint"/> on a chosen
    /// theme slot, so you recolor each part independently (the workflow we set up for fill+stroke art).
    ///
    /// Open via menu: Sort → Build Layered UI.
    ///
    /// Default graphic = UI Image (NOT SVGImage): a plain Image reliably supports UiRadiantTint's
    /// vertex-gradient mesh modifier, and an SVG imports as a Sprite you can drop straight onto an Image.
    /// Switch to SVGImage only if you've verified recolor works on it.
    ///
    /// STRUCTURE produced (so you can rebuild/tweak by hand if needed):
    ///   Root (RectTransform, sizeDelta = Size)
    ///     ├─ Layer0  (RectTransform stretched to root) + Image(sprite) [+ UiRadiantTint(slot)]
    ///     ├─ Layer1  …
    ///     └─ …                          ← list order = back → front (first child draws BEHIND)
    /// </summary>
    public class BuildLayeredUiWindow : EditorWindow
    {
        enum GraphicKind { Image, SVGImage }

        class Layer
        {
            public Sprite sprite;
            public string name = "";
            public bool tint = true;
            public UiThemeSlot slot = UiThemeSlot.Accent;
        }

        string prefabName = "NewLayeredUI";
        string outputFolder = "Assets/Prefabs/UI";
        GraphicKind kind = GraphicKind.Image;
        Vector2 size = new Vector2(120f, 120f);
        bool preserveAspect = true;
        readonly List<Layer> layers = new List<Layer> { new Layer { name = "fill" } };
        Vector2 scroll;

        [MenuItem("Sort/Build Layered UI")]
        static void Open() => GetWindow<BuildLayeredUiWindow>("Build Layered UI");

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Builds a layered UI prefab — root RectTransform + one child Graphic per layer (back → front). " +
                "Tick 'Recolor' on a layer to add a UiRadiantTint with the chosen slot.\n\n" +
                "UI only renders under a Canvas: select a Canvas child BEFORE Build to drop a live instance " +
                "there automatically, or drag the saved prefab onto your Canvas/panel afterward.", MessageType.Info);

            prefabName   = EditorGUILayout.TextField("Prefab Name", prefabName);
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            kind = (GraphicKind)EditorGUILayout.EnumPopup(new GUIContent("Graphic Type",
                "Image = standard UI Graphic (recommended, reliable recolor). SVGImage = Vector Graphics " +
                "component — use only if you verified UiRadiantTint works on it."), kind);
            size = EditorGUILayout.Vector2Field("Size (px)", size);
            preserveAspect = EditorGUILayout.Toggle("Preserve Aspect", preserveAspect);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layers (top of list = BACK)", EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Layer {i}", EditorStyles.boldLabel, GUILayout.Width(64));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("▲", GUILayout.Width(26)) && i > 0)              { (layers[i - 1], layers[i]) = (layers[i], layers[i - 1]); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("▼", GUILayout.Width(26)) && i < layers.Count-1) { (layers[i + 1], layers[i]) = (layers[i], layers[i + 1]); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("✕", GUILayout.Width(26)))                       { layers.RemoveAt(i); GUIUtility.ExitGUI(); }
                EditorGUILayout.EndHorizontal();

                l.sprite = (Sprite)EditorGUILayout.ObjectField("Sprite", l.sprite, typeof(Sprite), false);
                l.name   = EditorGUILayout.TextField("Name (blank = sprite name)", l.name);
                l.tint   = EditorGUILayout.Toggle("Recolor (UiRadiantTint)", l.tint);
                if (l.tint)
                    l.slot = (UiThemeSlot)EditorGUILayout.EnumPopup("    Slot", l.slot);

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("+ Add Layer")) layers.Add(new Layer());

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(layers.Count == 0 || string.IsNullOrEmpty(prefabName)))
                if (GUILayout.Button("Build Prefab", GUILayout.Height(30)))
                    Build();
        }

        void Build()
        {
            var root = new GameObject(prefabName, typeof(RectTransform));
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = size;

            foreach (var l in layers)
            {
                string goName = !string.IsNullOrEmpty(l.name) ? l.name
                              : (l.sprite != null ? l.sprite.name : "Layer");
                var go = new GameObject(goName, typeof(RectTransform));
                go.transform.SetParent(root.transform, false);

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;   // stretch to fill the root
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

                if (kind == GraphicKind.SVGImage)
                {
                    var img = go.AddComponent<SVGImage>();
                    img.sprite = l.sprite;
                    img.preserveAspect = preserveAspect;
                }
                else
                {
                    var img = go.AddComponent<Image>();
                    img.sprite = l.sprite;
                    img.preserveAspect = preserveAspect;
                    img.raycastTarget = false;   // layers are visual only; the root/button handles input
                }

                if (l.tint)
                {
                    var tint = go.AddComponent<UiRadiantTint>();
                    var so = new SerializedObject(tint);
                    var slotProp = so.FindProperty("slot");
                    if (slotProp != null) { slotProp.enumValueIndex = (int)l.slot; so.ApplyModifiedProperties(); }
                }
            }

            EnsureFolder(outputFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{prefabName}.prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(prefab);

            // QoL: if a UI object (under a Canvas) is selected, drop a live instance in so it's visible
            // immediately. UI only renders under a Canvas — a prefab dragged into empty hierarchy space
            // becomes a scene root and stays invisible.
            var sel = Selection.activeGameObject;
            bool placed = false;
            if (sel != null && sel.GetComponentInParent<Canvas>() != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, sel.transform);
                var rt = inst.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = Vector2.zero;
                Undo.RegisterCreatedObjectUndo(inst, "Instantiate Layered UI");
                Selection.activeGameObject = inst;
                placed = true;
            }

            Debug.Log($"[Build Layered UI] Created {path} ({layers.Count} layers)." +
                      (placed ? " Instanced under the selected Canvas object."
                              : " Drag it ONTO your Canvas/panel to see it (UI only renders under a Canvas)."), prefab);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            string cur = parts[0];                       // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
#endif
