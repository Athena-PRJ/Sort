#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Unity.VectorGraphics;

namespace Sort.EditorTools
{
    /// <summary>
    /// Batch-converts SVGImage components to plain UI Image, preserving sprite / color / raycast /
    /// preserveAspect. Use this on the layered UI prefabs whose SVGs are imported as TEXTURED (raster)
    /// sprites: a raster sprite belongs on a UI Image (binds _MainTex), which is what the value-preserving
    /// recolor shader needs — SVGImage is for vector sprites and doesn't feed the shader a texture.
    ///
    /// Usage:
    ///   • Select one or more PREFAB assets in the Project window (e.g. Prefabs/UI/*) → run the menu →
    ///     every SVGImage inside each prefab is converted and the prefab is saved.
    ///   • OR select GameObjects in the scene / open prefab stage → converts those + their children in place.
    /// </summary>
    public static class ConvertSvgImageToImage
    {
        [MenuItem("Sort/Convert SVGImage → Image (selection)")]
        static void Convert()
        {
            int prefabs = 0, components = 0;

            foreach (var obj in Selection.objects)
            {
                // Case 1: a prefab asset selected in the Project window.
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    int n = ConvertTree(root, useUndo: false);
                    if (n > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        prefabs++; components += n;
                    }
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            // Case 2: GameObjects selected in the scene / prefab stage.
            foreach (var go in Selection.gameObjects)
            {
                if (EditorUtility.IsPersistent(go)) continue;   // already handled as a prefab asset above
                int n = ConvertTree(go, useUndo: true);
                components += n;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Convert SVGImage → Image] Converted {components} component(s)" +
                      (prefabs > 0 ? $" across {prefabs} prefab(s)." : "."));
        }

        static int ConvertTree(GameObject root, bool useUndo)
        {
            if (root == null) return 0;
            var svgs = root.GetComponentsInChildren<SVGImage>(true);
            int converted = 0;
            foreach (var svg in svgs)
            {
                if (svg == null) continue;
                ConvertOne(svg, useUndo);
                converted++;
            }
            return converted;
        }

        static void ConvertOne(SVGImage svg, bool useUndo)
        {
            var go = svg.gameObject;

            // Snapshot the values worth keeping.
            Sprite sprite   = svg.sprite;
            Color  color    = svg.color;
            bool   raycast  = svg.raycastTarget;
            bool   preserve = svg.preserveAspect;

            // Swap the Graphic: a GameObject can hold only one, so remove SVGImage first.
            if (useUndo) Undo.DestroyObjectImmediate(svg);
            else         Object.DestroyImmediate(svg, true);

            var img = useUndo ? Undo.AddComponent<Image>(go) : go.AddComponent<Image>();
            img.sprite         = sprite;
            img.color          = color;
            img.raycastTarget  = raycast;
            img.preserveAspect = preserve;
            img.type           = Image.Type.Simple;
            // Material is intentionally left at default — assign the recolor material via the layer's
            // UiRadiantTint (Recolor Material field), so it's consistent and survives re-conversion.
        }
    }
}
#endif
