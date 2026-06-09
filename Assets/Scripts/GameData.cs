namespace Sort
{
    // Color identity is now a designer-defined STRING (the color's NAME), defined per-prefab in
    // ColorPalette (name + BaseMap). The old fixed `PieceColor` enum and `PieceColors.ToUnityColor`
    // extension were removed 2026-06-09 when colors became fully data-driven. See ColorPalette.cs.
    // Anywhere that used to take a PieceColor now takes a `string` color name; Rainbow / Questionmark
    // are still separate flags on Piece (a color name of "" = unset).

    /// <summary>
    /// Difficulty tag shown on the Level badge. Dev picks one per level in LevelData.
    /// The enum name (e.g. "SuperHard") is what gets displayed unless a custom format is used.
    /// </summary>
    public enum LevelDifficulty
    {
        Easy,
        Normal,
        Hard,
        SuperHard,
        Expert
    }

    // PieceTheme enum was removed in Phase 2 of the Prefab Registry refactor — per-level prefab
    // selection now lives on LevelData.piecePrefab (a direct prefab reference sourced from
    // PrefabRegistry). See PrefabRegistry.cs for the catalog model.
}
