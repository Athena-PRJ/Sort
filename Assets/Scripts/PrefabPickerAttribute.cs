using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Mark a <see cref="GameObject"/> field with this attribute to get a dropdown that pulls its
    /// options from the project's <see cref="PrefabRegistry"/> ScriptableObject. The drawer lives
    /// in Assets/Editor/PrefabPickerDrawer.cs — runtime build doesn't ship it.
    ///
    /// Usage:
    ///   [PrefabPicker] public GameObject piecePrefab;
    /// </summary>
    public class PrefabPickerAttribute : PropertyAttribute { }
}
