using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Per-column status indicator. The Done sprite is the always-visible FRAME (the slot); the not-done
    /// icon sits INSIDE it while the column is unsolved, and is swapped for the tick once the column locks.
    /// MainBoardBuilder spawns the frame plus the two inner icons (as separate sprites at the same spot)
    /// and wires this component up with the two inner GameObjects. The frame itself is never toggled.
    /// </summary>
    public class ColumnIndicator : MonoBehaviour
    {
        Column column;
        GameObject notDoneObject;
        GameObject tickObject;

        public void Setup(Column col, GameObject notDoneObj, GameObject tickObj)
        {
            column = col;
            notDoneObject = notDoneObj;
            tickObject = tickObj;

            if (column != null) column.Locked += OnLocked;
            Refresh();
        }

        void OnLocked(Column c) => Refresh();

        void Refresh()
        {
            bool done = column != null && column.IsLocked;
            if (notDoneObject != null) notDoneObject.SetActive(!done);
            if (tickObject != null) tickObject.SetActive(done);
        }

        void OnDestroy()
        {
            if (column != null) column.Locked -= OnLocked;
        }
    }
}
