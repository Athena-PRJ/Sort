using UnityEngine;

namespace Sort
{
    /// <summary>
    /// The "Prefab-to-gen" catalog — single source of truth for every piece prefab the game
    /// can spawn, plus the per-prefab layout values LevelLoader needs at runtime.
    ///
    /// LevelLoader reads this registry to:
    ///   1. Look up a LevelData.piecePrefab's spacing/offset config.
    ///   2. (Editor only) Provide the dropdown of valid prefabs LevelData can pick from.
    ///
    /// To add a new theme (e.g. a "Stamp" prefab):
    ///   1. Build the prefab (must have a Piece + Collider, see Piece.cs contract).
    ///   2. Add an entry to this registry's `entries[]` array.
    ///   3. Tune its columnSpacing / pieceSpacing / offsets at the 3x3 baseline.
    ///   4. The new prefab automatically appears in every LevelData's piecePrefab dropdown.
    ///
    /// No code changes needed to support new themes. The dropdown of LevelData options grows
    /// with the registry — that's the design goal of this refactor.
    /// </summary>
    [CreateAssetMenu(menuName = "Sort/Prefab Registry", fileName = "PrefabRegistry")]
    public class PrefabRegistry : ScriptableObject
    {
        [Tooltip("Prefab-to-gen: every piece prefab the game can spawn, with its per-prefab " +
                 "spacing/offset config. LevelData.piecePrefab picks one of these.")]
        public PieceGenEntry[] entries = new PieceGenEntry[0];

        /// <summary>
        /// Editor-only hot-reload: when the designer tweaks any field on this asset DURING Play mode,
        /// push the new values to every live Piece via <see cref="LevelLoader.OnRegistryChanged"/>.
        /// Re-applies pieceScale + columnSpacing + pieceSpacing + offsets so changes are visible
        /// without leaving Play mode. Bypassed entirely in builds (UNITY_EDITOR guard).
        ///
        /// Per the workflow note: tweak values in Play to find what looks right, then exit Play to
        /// keep the persisted values. ScriptableObject changes survive Play→Edit transitions
        /// automatically (asset data, not scene data) — but you only see the change apply in real
        /// time because of THIS hook.
        /// </summary>
        void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
            if (LevelLoader.Instance == null) return;
            LevelLoader.Instance.OnRegistryChanged();
#endif
        }

        /// <summary>
        /// Looks up the entry whose <see cref="PieceGenEntry.piecePrefab"/> matches <paramref name="prefab"/>.
        /// No validation on the entry's spacing/scale values — use this when you want the raw entry,
        /// e.g. to read pieceScale even if columnSpacing wasn't filled in. See also <see cref="TryGetSpacing"/>.
        /// </summary>
        public bool TryGetEntry(GameObject prefab, out PieceGenEntry entry)
        {
            if (entries != null && prefab != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].piecePrefab == prefab)
                    {
                        entry = entries[i];
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }

        /// <summary>
        /// Same as <see cref="TryGetEntry"/> but additionally requires columnSpacing &amp; pieceSpacing
        /// to be positive — used by LevelLoader when applying the runtime spacing override. If either
        /// is non-positive the lookup returns false, signalling "use Board/Column's own default".
        /// </summary>
        public bool TryGetSpacing(GameObject prefab, out PieceGenEntry entry)
        {
            if (!TryGetEntry(prefab, out entry)) return false;
            return entry.columnSpacing > 0f && entry.pieceSpacing > 0f;
        }

        /// <summary>True iff <paramref name="prefab"/> is registered (regardless of value validity).</summary>
        public bool Contains(GameObject prefab) => TryGetEntry(prefab, out _);
    }

    /// <summary>
    /// One entry in <see cref="PrefabRegistry"/>: the piece prefab + every per-prefab visual
    /// param the LevelLoader needs. Same field set as the legacy PrefabSpacingOverride —
    /// only the role changed: this IS the prefab catalog, not a side-table of overrides on
    /// top of a separate prefab list.
    /// </summary>
    [System.Serializable]
    public struct PieceGenEntry
    {
        [Tooltip("The piece prefab this entry registers. Drag in Lego.prefab, Box.prefab, Card.prefab, etc. " +
                 "Each LevelData picks one of these via its piecePrefab dropdown.")]
        public GameObject piecePrefab;

        [Tooltip("PASTEL color palette for this prefab — soft, muted colors (e.g. Box_pastel_red.png). " +
                 "Set once here; every level using this prefab and PaletteStyle.Pastel inherits it. " +
                 "Leave null to fall back to tint-the-default-material.")]
        [UnityEngine.Serialization.FormerlySerializedAs("colorPalette")]
        public ColorPalette palettePastel;

        [Tooltip("PLAIN (trơn) color palette for this prefab — vibrant, saturated colors (e.g. Box_red.png). " +
                 "Set once here; every level using this prefab and PaletteStyle.Plain inherits it. " +
                 "Leave null to fall back to tint-the-default-material.")]
        public ColorPalette palettePlain;

        [Tooltip("Standard piece localScale at the 3x3 baseline grid. Tune this until the piece looks the right " +
                 "size relative to the MainBoard frame at a 3x3 level — that's the authored reference. Auto-fit " +
                 "(per-level F multiplier on Board.transform) then scales pieces for other grid sizes (2x2, 4x4, " +
                 "5x5, etc.) AUTOMATICALLY, no per-grid tuning needed.\n\n" +
                 "Vector3 (not float) so per-axis proportions survive — e.g. Lego's tall-thin shape needs " +
                 "(0.479, 0.721, 1) to look right. Set to (0, 0, 0) to fall back to the prefab's authored localScale.")]
        public Vector3 pieceScale;

        public enum DepthAxis { X, Y, Z }

        [Tooltip("Extra THICKNESS multiplier on the piece's DEPTH axis (applied ON TOP of Piece Scale / the " +
                 "prefab's authored scale) — fattens the piece so its 3D side/bottom edge is easier to see. " +
                 "1 (or 0) = no change. Works even when Piece Scale is (0,0,0).")]
        public float pieceDepthMultiplier;

        [Tooltip("Which LOCAL axis is the piece's THICKNESS (the thin axis perpendicular to its face). " +
                 "Block-style tiles are usually Z (their smallest collider dimension).")]
        public DepthAxis pieceDepthAxis;

        [Tooltip("Column spacing (X distance between adjacent columns) for this prefab. " +
                 "Overrides Board's internal default at runtime when this prefab is used.")]
        public float columnSpacing;

        [Tooltip("Piece spacing (Y distance between adjacent pieces within a column) for this prefab. " +
                 "Overrides Column.pieceSpacing at runtime when this prefab is used.")]
        public float pieceSpacing;

        [Tooltip("Extra uniform multiplier on top of MainBoard's authored localScale when this prefab is used. " +
                 "1.0 = no change. Leave at 0 to mean 'use 1.0' — code clamps non-positive values to 1.0.")]
        public float mainBoardScaleAdjust;

        [Tooltip("Extra uniform multiplier on top of HandAnchor's authored localScale (after per-grid F) when this " +
                 "prefab is used. 1.0 = no change. Leave at 0 to mean 'use 1.0' — code clamps non-positive values to 1.0.")]
        public float handAnchorScaleAdjust;

        [Tooltip("Extra WORLD-space offset added to MainBoard's position AFTER auto-align, per prefab. " +
                 "Use when this prefab's sprite has padding so bounds.center doesn't match the frame's " +
                 "interior center. Each prefab has its own art, so this is per-prefab not global.")]
        public Vector3 mainBoardExtraOffset;

        [Tooltip("Extra LOCAL-space offset for the held piece (relative to HandAnchor), per prefab. " +
                 "Applied every swap / undo / rebuild via PlayerHand.HeldPieceLocalOffset so the piece " +
                 "stays visually aligned with HandPlace for this prefab's art.")]
        public Vector3 heldPieceExtraOffset;
    }
}
