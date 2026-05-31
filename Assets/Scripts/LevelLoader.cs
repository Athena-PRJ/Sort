using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Builds the board at runtime from a LevelData. Runs before GameManager
    /// so that GameManager.Awake can discover the spawned columns.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class LevelLoader : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject columnPrefab;
        [SerializeField] private GameObject piecePrefab;

        [Header("Scene refs")]
        [SerializeField] private Board board;
        [SerializeField] private PlayerHand playerHand;

        [Header("Default")]
        [Tooltip("Used when no level is selected (e.g. opening the Game scene directly in editor).")]
        [SerializeField] private LevelData defaultLevel;

        [Header("Auto-fit board to grid")]
        [Tooltip("If true, the Board and HandAnchor are uniformly scaled so larger grids appear " +
                 "with smaller pieces — every level fills roughly the same screen area.")]
        [SerializeField] private bool autoFit = true;

        [Tooltip("At this grid size, scale = 1.0. Larger grids scale below 1. " +
                 "X is the column count, Y is the max rows per column.")]
        [SerializeField] private Vector2Int referenceGrid = new Vector2Int(5, 5);

        [Tooltip("Maximum scale (clamps small grids so they don't look gigantic).")]
        [SerializeField] private float maxScale = 1f;

        public static LevelLoader Instance { get; private set; }
        public LevelData CurrentLevel { get; private set; }

        void Awake()
        {
            Instance = this;
            var level = LevelProgress.SelectedLevel != null ? LevelProgress.SelectedLevel : defaultLevel;
            if (level == null)
            {
                Debug.LogError("[LevelLoader] No level selected and no defaultLevel assigned.", this);
                return;
            }
            BuildLevel(level);
        }

        public void BuildLevel(LevelData data)
        {
            if (data == null) { Debug.LogError("[LevelLoader] BuildLevel called with null data."); return; }
            if (columnPrefab == null || piecePrefab == null || board == null || playerHand == null)
            {
                Debug.LogError("[LevelLoader] Missing prefab or scene reference. Wire all serialized fields.", this);
                return;
            }

            CurrentLevel = data;
            ClearBoard();

            foreach (var colConfig in data.columns)
            {
                var columnGO = Instantiate(columnPrefab, board.transform);
                foreach (var pieceCfg in colConfig.pieces)
                {
                    var pieceGO = Instantiate(piecePrefab, columnGO.transform);
                    var piece = pieceGO.GetComponent<Piece>();
                    if (piece != null) piece.SetConfig(pieceCfg);
                }
            }

            board.Layout();

            // Spawn the held piece under the hand anchor and assign it.
            var heldGO = Instantiate(piecePrefab, playerHand.HandAnchor);
            var held = heldGO.GetComponent<Piece>();
            if (held != null) held.SetConfig(data.startingHeldPiece);
            playerHand.SetHeldPiece(held);

            if (autoFit) ApplyAutoFit(data);
        }

        void ApplyAutoFit(LevelData data)
        {
            int cols = Mathf.Max(1, data.columns.Length);
            int rowsMax = 1;
            foreach (var c in data.columns)
                if (c != null && c.pieces != null && c.pieces.Length > rowsMax) rowsMax = c.pieces.Length;

            float refX = Mathf.Max(1, referenceGrid.x);
            float refY = Mathf.Max(1, referenceGrid.y);
            float scaleX = refX / cols;
            float scaleY = refY / rowsMax;
            float scale = Mathf.Min(maxScale, Mathf.Min(scaleX, scaleY));

            board.transform.localScale = Vector3.one * scale;
            if (playerHand.HandAnchor != null)
                playerHand.HandAnchor.localScale = Vector3.one * scale;
        }

        void ClearBoard()
        {
            // Only destroy Column children — leave decorative children (e.g. MainBoard) intact.
            for (int i = board.transform.childCount - 1; i >= 0; i--)
            {
                var child = board.transform.GetChild(i);
                if (child.GetComponent<Column>() != null)
                    Destroy(child.gameObject);
            }
            if (playerHand.HeldPiece != null) Destroy(playerHand.HeldPiece.gameObject);
            playerHand.SetHeldPiece(null);
        }
    }
}
