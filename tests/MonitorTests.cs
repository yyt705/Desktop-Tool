using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskFences
{
    internal static class MonitorTests
    {
        private static void Check(bool result, string message)
        {
            if (!result) throw new InvalidOperationException(message);
        }

        public static void Run()
        {
            int emptyOrigin = DesktopIconController.InferGridOrigin(new int[0], 76, -1920);
            int populatedOrigin = DesktopIconController.InferGridOrigin(new[] { -1920 + 76, -1920 + 152 }, 76, -1920);
            Check(emptyOrigin == populatedOrigin && emptyOrigin >= 0 && emptyOrigin < 76, "An empty Explorer view must retain the same signed grid numbering when the first icon arrives.");
            var metrics = new DesktopGridMetrics { CellWidth = 100, CellHeight = 80, OriginX = 7, OriginY = 9 };
            Rectangle left = MonitorGrid.FromWorkArea(new Rectangle(-819, -239, 824, 708), metrics);
            Rectangle right = MonitorGrid.FromWorkArea(new Rectangle(207, 89, 700, 560), metrics);
            Check(left == Rectangle.FromLTRB(-8, -3, -1, 5), "Whole cells on the negative monitor must exclude partial edge cells.");
            Check(right == new Rectangle(2, 1, 7, 7), "A taskbar inset must shift the first available row and column.");
            var areas = new[] { left, right };
            var start = new Rectangle(-6, -2, 4, 4);
            Rectangle resized = MonitorGrid.Resize(start, left, -20, -20, true, true, false, false);
            Check(resized == Rectangle.FromLTRB(left.Left, left.Top, start.Right, start.Bottom), "Dragging a negative top-left resize handle must preserve the opposite corner.");
            resized = MonitorGrid.Resize(start, left, 20, 20, false, false, true, true);
            Check(resized == Rectangle.FromLTRB(start.Left, start.Top, left.Right, left.Bottom), "A resize may extend only as far as its own monitor.");
            resized = MonitorGrid.Resize(start, left, -20, -20, false, false, true, true);
            Check(resized.Size == new Size(3, 3), "Shrinking across the opposite edge must retain the 3x3 minimum.");

            bool found;
            Point origin = FenceManager.FindNearestNonOverlappingOrigin(null, null, -6, -2, 3, 3, 4, 2, areas, metrics, 1f, out found);
            Check(found && origin == new Point(-6, -2), "A negative grid position must survive placement.");
            origin = FenceManager.FindNearestNonOverlappingOrigin(null, null, 0, 0, 3, 3, -6, -2, areas, metrics, 1f, out found);
            Check(found && origin == new Point(2, 1), "Dragging to a gap must clamp the whole fence to the nearest working area.");
            var blocker = new FenceConfig { OuterCol = 2, OuterRow = 1, OuterCols = 7, OuterRows = 7 };
            origin = FenceManager.FindNearestNonOverlappingOrigin(new[] { blocker }, null, 2, 1, 3, 3, 2, 1, areas, metrics, 1f, out found);
            Check(found && left.Contains(new Rectangle(origin, new Size(3, 3))), "Placement must continue on another monitor when a monitor is full.");

            Point cell;
            var used = new HashSet<long>();
            bool free = FenceManager.FindNearestFreeOutsideCell(new[] { blocker }, new Point(17, 129), areas, metrics, used, out cell);
            Check(free && cell == new Point(-2, 1), "Outside icon placement must skip the gap and all occupied fences.");
            used.Add(((long)cell.X << 32) | (uint)cell.Y);
            free = FenceManager.FindNearestFreeOutsideCell(new[] { blocker }, new Point(17, 129), areas, metrics, used, out cell);
            Check(free && left.Contains(cell) && cell != new Point(-2, 1), "Outside placement must skip occupied negative cells.");

            var offscreen = new FenceConfig { OuterCol = 100, OuterRow = -100, OuterCols = 10, OuterRows = 5 };
            var placed = new FenceConfig { OuterCol = 2, OuterRow = 1, OuterCols = 3, OuterRows = 3 };
            bool changed = FenceManager.FitExistingFences(new[] { offscreen, placed }, new[] { right }, metrics, 1f);
            Check(changed && right.Contains(new Rectangle(offscreen.OuterCol, offscreen.OuterRow, offscreen.OuterCols, offscreen.OuterRows)),
                "A disconnected monitor must bring even an oversized fence into a real work area.");
            Check(placed.OuterCol == 2 && placed.OuterRow == 1, "Repair must retain valid fences already on a connected monitor.");
            Check(!FenceManager.ZonesOverlap(offscreen.OuterCol, offscreen.OuterRow, offscreen.OuterCols, offscreen.OuterRows,
                placed.OuterCol, placed.OuterRow, placed.OuterCols, placed.OuterRows, metrics, 1f), "Repaired fences must use available free space.");
            Check(!FenceManager.FitExistingFences(new[] { offscreen, placed }, new[] { right }, metrics, 1f), "A stable layout must not dirty the settings file.");
            var lone = new FenceConfig { OuterCol = -50, OuterRow = 0, OuterCols = 4, OuterRows = 4 };
            Check(FenceManager.FitExistingFences(new[] { lone }, new[] { right }, metrics, 1f) &&
                right.Contains(new Rectangle(lone.OuterCol, lone.OuterRow, lone.OuterCols, lone.OuterRows)), "A single disconnected fence must be repaired.");

            var wide = new FenceConfig { OuterCol = 20, OuterRow = 2, OuterCols = 10, OuterRows = 4 };
            for (int i = 0; i < 16; i++) wide.MemberIconIds.Add("item-" + i);
            var portrait = new Rectangle(-8, -6, 5, 12);
            FenceManager.FitExistingFences(new[] { wide }, new[] { portrait }, metrics, 1f);
            Check(portrait.Contains(new Rectangle(wide.OuterCol, wide.OuterRow, wide.OuterCols, wide.OuterRows)) &&
                (wide.OuterRows - 2) * (wide.OuterCols - 2) >= 16, "Rotating a monitor must preserve member capacity when space is available.");
            int headerHeight;
            Rectangle frame = FenceChromeForm.ComputeGridBounds(-4, -2, 3, 3,
                new DesktopGridMetrics { CellWidth = 40, CellHeight = 40 }, 2f, out headerHeight);
            Check(new Rectangle(-160, -80, 120, 120).Contains(frame), "The rendered frame must stay in its reserved cells on small grids.");
        }
    }
}
