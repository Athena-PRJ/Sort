#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Sort.EditorTools
{
    /// <summary>
    /// Converts a UI-style board object (a RectTransform with SpriteRenderer + BoardFrame + MainBoardBuilder)
    /// into a clean WORLD GameObject (plain Transform). A RectTransform can't be removed in place, so this
    /// creates a sibling plain-Transform GameObject and copies the board components onto it (preserving all
    /// field values incl. sprites). You then re-point LevelLoader.boardFrame to the new object and delete
    /// the old one.
    ///
    /// Why: the board the 3D pieces sit on must be a world SpriteRenderer (Screen-Space UI always draws over
    /// world objects, so a UI board hides the pieces). See the world/UI split discussion.
    ///
    /// Usage: select the old MainBoardUI in the Hierarchy → Sort → Convert to World MainBoard.
    /// </summary>
    public static class ConvertToWorldBoard
    {
        [MenuItem("Sort/Convert to World MainBoard (selection)")]
        static void Convert()
        {
            var src = Selection.activeGameObject;
            if (src == null) { Debug.LogWarning("[Convert to World MainBoard] Select the old board object first."); return; }

            var go = new GameObject(src.name.Replace("UI", "") + " (World)");
            Undo.RegisterCreatedObjectUndo(go, "Convert to World MainBoard");
            go.transform.SetParent(src.transform.parent, false);
            // Keep the facing rotation (so it still faces the camera), but reset position — LevelLoader's
            // AlignBoardFrameToColumns will re-center it on the piece grid at runtime anyway.
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = src.transform.localRotation;
            go.transform.localScale    = Vector3.one;

            // Copy the board components in render/dependency order. ComponentUtility preserves every
            // serialized field, including the assigned sprite / indicator sprites / size fractions.
            CopyComponent(src.GetComponent<SpriteRenderer>(), go);
            CopyComponent(src.GetComponent<BoardFrame>(), go);
            CopyComponent(src.GetComponent<MainBoardBuilder>(), go);

            Selection.activeGameObject = go;
            Debug.Log($"[Convert to World MainBoard] Created '{go.name}' (plain Transform). " +
                      "Now: 1) assign the board sprite to its SpriteRenderer if empty, " +
                      "2) re-point LevelLoader → Board Frame to it, " +
                      "3) delete the old RectTransform board object.", go);
        }

        static void CopyComponent(Component c, GameObject target)
        {
            if (c == null) return;
            ComponentUtility.CopyComponent(c);
            ComponentUtility.PasteComponentAsNew(target);
        }
    }
}
#endif
