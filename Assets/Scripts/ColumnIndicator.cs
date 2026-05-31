using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Per-column status indicator. While unsolved it shows the 'not-done' sprite. When the column
    /// locks it swaps the base sprite to 'done' (same size) and shows a tick overlay on top.
    /// </summary>
    public class ColumnIndicator : MonoBehaviour
    {
        Column column;
        SpriteRenderer baseRenderer;
        Sprite notDoneSprite;
        Sprite doneSprite;
        GameObject tickObject;

        public void Setup(Column col, SpriteRenderer baseSr, Sprite notDone, Sprite done, GameObject tick)
        {
            column = col;
            baseRenderer = baseSr;
            notDoneSprite = notDone;
            doneSprite = done;
            tickObject = tick;

            if (column != null) column.Locked += OnLocked;
            Refresh();
        }

        void OnLocked(Column c) => Refresh();

        void Refresh()
        {
            bool done = column != null && column.IsLocked;
            if (baseRenderer != null) baseRenderer.sprite = done ? doneSprite : notDoneSprite;
            if (tickObject != null) tickObject.SetActive(done);
        }

        void OnDestroy()
        {
            if (column != null) column.Locked -= OnLocked;
        }
    }
}
