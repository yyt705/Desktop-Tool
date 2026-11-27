using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskFences
{
    internal static class InputTests
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static DesktopIconInfo Icon(string id, int x, bool selected)
        {
            return new DesktopIconInfo { Identity = id, Selected = selected,
                ParsingName = @"C:\Desktop\" + id, ClientPosition = new Point(x, 0),
                IconBounds = new Rectangle(x, 0, 32, 32), Index = x / 100 };
        }

        public static void Run()
        {
            var gesture = new DesktopDragGesture();
            var before = new[] { Icon("one", 0, true), Icon("two", 100, true), Icon("three", 200, false) };
            var after = new[] { Icon("one", 100, true), Icon("two", 200, true), Icon("three", 300, true) };
            var metrics = new DesktopGridMetrics { CellWidth = 100, CellHeight = 100 };
            var press = new Point(8, 8);
            var drop = new Point(108, 8);

            Check(gesture.MovedItems(after, metrics, 1).Count == 0, "Explorer reflow is not a drag");
            gesture.Begin(press, before);
            gesture.Release(drop, true, new Size(4, 4), 10);
            var moved = gesture.MovedItems(after, metrics, 11);
            Check(moved.SetEquals(new[] { "one", "two" }), "Only selected and physically moved items may change owner");
            Check(gesture.MovedItems(after, metrics, 12).Count == 0, "A completed drag cannot be reused");

            gesture.Begin(new Point(40, 8), before);
            gesture.Release(drop, true, new Size(4, 4), 20);
            Check(gesture.MovedItems(after, metrics, 21).Count == 0, "A desktop selection rectangle is not a drag");
            gesture.Begin(press, before);
            gesture.Release(drop, false, new Size(4, 4), 30);
            Check(gesture.MovedItems(after, metrics, 31).Count == 0, "Dropping over another application is ignored");
            gesture.Begin(press, before);
            gesture.Release(press, true, new Size(4, 4), 40);
            Check(gesture.MovedItems(after, metrics, 41).Count == 0, "A click is not a drag");
            gesture.Begin(press, before);
            gesture.Release(drop, true, new Size(4, 4), 50);
            Check(gesture.MovedItems(before, metrics, 51).Count == 0, "A pre-drop Explorer snapshot does not spend the gesture");
            Check(gesture.MovedItems(after, metrics, 52).Count == 2, "Delayed delivery of the real drop is handled");
            gesture.Begin(new Point(205, 8), before);
            gesture.Release(new Point(305, 8), true, new Size(4, 4), 60);
            Check(gesture.MovedItems(after, metrics, 61).SetEquals(new[] { "three" }), "Dragging an unselected item selects only that item");
            gesture.Begin(press, before);
            gesture.Release(drop, true, new Size(4, 4), 70);
            Check(gesture.MovedItems(new[] { Icon("one", 600, true) }, metrics, 71).Count == 0, "Unrelated movement of even a selected item is ignored");
            Check(gesture.MovedItems(after, metrics, 5000).Count == 0, "An old gesture is ignored");
            gesture.Begin(press, before);
            gesture.Release(drop, true, new Size(4, 4), 6000);
            var partial = new[] { before[0], after[1], before[2] };
            Check(gesture.MovedItems(partial, metrics, 6001).Count == 0 && gesture.WaitingForItem("two"),
                "A partial Explorer snapshot cannot detach a selected peer before the anchor moves");
            Check(gesture.MovedItems(after, metrics, 6002).SetEquals(new[] { "one", "two" }), "A staggered multi-drop keeps the same participants");
            gesture.Begin(press, before);
            gesture.Cancel();
            gesture.Release(drop, true, new Size(4, 4), 7000);
            Check(gesture.MovedItems(after, metrics, 7001).Count == 0, "Escape or a desktop restart cancels the gesture");

            // ScreenBounds gap hit-testing, hover-halo margin hit-testing, and 2-cell crowded-fence Explorer bump tolerance
            var boxIcon = new DesktopIconInfo { Identity = "gap", Selected = true, ParsingName = @"C:\Desktop\gap.txt",
                ClientPosition = new Point(0, 0), IconBounds = new Rectangle(12, 4, 32, 32),
                LabelBounds = new Rectangle(8, 42, 40, 18), ScreenBounds = new Rectangle(0, 0, 64, 64), Index = 0 };
            gesture.Begin(new Point(28, 38), new[] { boxIcon }); // in gap between IconBounds and LabelBounds
            gesture.Release(new Point(128, 38), true, new Size(4, 4), 8000);
            var bumped = boxIcon;
            bumped.ClientPosition = new Point(200, 0); // Explorer bumped 1 cell right due to occupied slot
            Check(gesture.MovedItems(new[] { bumped }, metrics, 8001).SetEquals(new[] { "gap" }),
                "Clicking inside ScreenBounds and receiving a 1-cell Explorer snap bump must still count as manual drag");

            // Hover-halo click (4px outside ScreenBounds) + 2-cell bump inside a crowded fence
            gesture.Begin(new Point(68, 30), new[] { boxIcon });
            gesture.Release(new Point(168, 30), true, new Size(4, 4), 8100);
            bumped.ClientPosition = new Point(300, 0); // 2 cells bumped from +100 delta
            Check(gesture.MovedItems(new[] { bumped }, metrics, 8101).SetEquals(new[] { "gap" }) &&
                  gesture.DropPoint == new Point(168, 30),
                "Hover-halo press and 2-cell crowded-fence Explorer bump must remain attributed as a manual drag");

            // Rapid back-to-back icon drags: detaching a just-released gesture preserves Icon 1 when Icon 2 is clicked immediately!
            gesture.Begin(press, before);
            gesture.Release(drop, true, new Size(4, 4), 9000);
            var detached = gesture.DetachPendingRelease();
            Check(detached != null && detached.HasPendingRelease && !gesture.HasPendingRelease,
                "Detaching a pending release must preserve the first icon's drop across an immediate second mouse press");
            gesture.Begin(new Point(205, 8), before); // user immediately clicks Icon 3 before Icon 1 drop was consumed
            Check(detached.MovedItems(after, metrics, 9050).SetEquals(new[] { "one", "two" }),
                "Detached prior drop must still attribute Icon 1 & 2 even after a rapid subsequent click");

            var context = new DesktopMenuPlacement();
            var fresh = Icon("new", 0, true);
            context.Open("fence", press);
            Check(!context.Take(fresh, true, 1), "A right-click alone never grants ownership");
            context.Cancel();
            Check(!context.Take(fresh, true, 2), "Canceled menus cannot adopt unrelated new files");
            context.Open("fence", press);
            Check(!context.ShouldCancelOnKey(System.Windows.Forms.Keys.A, true), "Typing while a released click is being inspected keeps its result");
            Check(context.ShouldCancelOnKey(System.Windows.Forms.Keys.A, false), "An unrelated key before any activation disarms the old menu");
            Check(context.ShouldCancelOnKey(System.Windows.Forms.Keys.Escape, true), "Escape dismisses a released pending command");
            Check(!context.ShouldCancelOnKey(System.Windows.Forms.Keys.Down, false) &&
                !context.ShouldCancelOnKey(System.Windows.Forms.Keys.Enter, false), "Navigating an open menu preserves its fence context");
            context.Confirm(DesktopMenuCommand.NewItem, new[] { "one" }, null, 3);
            Check(!context.ShouldCancelOnKey(System.Windows.Forms.Keys.A, false) && !context.ShouldCancelOnKey(System.Windows.Forms.Keys.Enter, false),
                "Typing and finishing an inline filename do not revoke a confirmed New command");
            Check(context.ShouldCancelOnKey(System.Windows.Forms.Keys.Escape, false), "Explicit dismissal still clears the pending result");
            Check(!context.Take(before[0], true, 4), "Renaming an old item does not create a member");
            Check(!context.Take(fresh, false, 4), "A concurrent background creation is ignored");
            Check(context.Take(fresh, true, 5) && !context.Take(Icon("second", 0, true), true, 6), "Only the newly edited item is admitted once");
            context.Open("fence", press);
            context.Confirm(DesktopMenuCommand.Paste, new string[0], new[] { fresh.ParsingName }, 7);
            Check(!context.ShouldCancelOnKey(System.Windows.Forms.Keys.ProcessKey, false), "IME/assistive typing does not revoke a confirmed Paste");
            Check(!context.Take(Icon("other", 0, true), false, 8), "Paste requires an exact clipboard destination");
            Check(!context.Take(Icon("new", 0, false), false, 9), "Unselected background arrivals are ignored");
            Check(context.Take(fresh, false, 10) && !context.Take(fresh, false, 11), "Paste consumes each selected expected path once");
            context.Open("fence", press);
            context.Confirm(DesktopMenuCommand.Paste, new string[0], new[] { @"C:\Desktop\doc.txt", @"C:\Desktop\note.txt" }, 12);
            var cnCopy = new DesktopIconInfo { Identity = "cn", Selected = true, ParsingName = @"C:\Desktop\doc - 副本.txt" };
            var enCopy = new DesktopIconInfo { Identity = "en", Selected = true, ParsingName = @"C:\Desktop\note - Copy (2).txt" };
            Check(context.Take(cnCopy, false, 13) && context.Take(enCopy, false, 14),
                "Paste must recognize localized desktop-to-desktop duplicate copy suffixes");
            Check(FenceManager.ClassifyMenuItem("粘贴(&P)", false) == DesktopMenuCommand.Paste &&
                FenceManager.ClassifyMenuItem("Paste (Ctrl+V)", false) == DesktopMenuCommand.Paste,
                "Classic and modern localized paste labels allow accelerator hints");
            Check(FenceManager.IsNewSubmenu("新建(&W)") && FenceManager.ClassifyMenuItem("Text Document", true) == DesktopMenuCommand.NewItem,
                "An enabled file type inside the New submenu is a creation command");
            Check(FenceManager.ClassifyMenuItem("Refresh", false) == DesktopMenuCommand.None &&
                FenceManager.ClassifyMenuItem("Paste shortcut", false) == DesktopMenuCommand.None,
                "Unknown menu commands cannot authorize placement");
            context.Open("fence", press);
            context.Confirm(DesktopMenuCommand.NewItem, new string[0], null, 30);
            Check(!context.Take(fresh, true, 4000), "Expired command results cannot adopt a later rename");
        }
    }
}
