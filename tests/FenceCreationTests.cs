using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace DeskFences
{
    internal static class FenceCreationTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        private static DesktopIconInfo MakeIcon(string id, int col, int row, DesktopGridMetrics m, int index = 0)
        {
            int px = m.OriginX + col * m.CellWidth;
            int py = m.OriginY + row * m.CellHeight;
            return new DesktopIconInfo
            {
                Index = index,
                Identity = id,
                ParsingName = @"C:\Desktop\" + id + ".txt",
                Name = id,
                ClientPosition = new Point(px, py),
                ScreenBounds = new Rectangle(px + 8, py + 4, m.CellWidth - 16, m.CellHeight - 10),
                IconBounds = new Rectangle(px + 18, py + 6, 48, 48),
                LabelBounds = new Rectangle(px + 8, py + 58, m.CellWidth - 16, 28)
            };
        }

        private static long Key(int col, int row) { return (((long)col) << 32) | (uint)row; }

        private static void CheckIconFreeNewFencePlacement()
        {
            var m = new DesktopGridMetrics { CellWidth = 96, CellHeight = 120, OriginX = 2, OriginY = 2 };
            var primary = new Rectangle(0, 0, 12, 8);
            var secondary = new Rectangle(12, 0, 10, 8);

            // 1. Default spot (1,0) blocked by icons -> must find full 5x4 placement touching ZERO icons!
            var iconCells = new HashSet<long> { Key(1, 0), Key(2, 1), Key(3, 2), Key(0, 0), Key(0, 1) };
            Rectangle placed = FenceManager.FindIconFreeFencePlacement(
                new FenceConfig[0], iconCells, new[] { primary }, primary, new Point(1, 0), new Size(5, 4), m, 1f);
            Check(placed.Width == 5 && placed.Height == 4 && primary.Contains(placed),
                "Full 5x4 icon-free placement should succeed when open area exists. Got " + placed);
            for (int r = placed.Top; r < placed.Bottom; r++)
                for (int c = placed.Left; c < placed.Right; c++)
                    Check(!iconCells.Contains(Key(c, r)), "New fence OuterRect must contain ZERO icons! Hit " + c + "," + r);

            // 2. Only a 4x3 hole exists (no 5x4, no 4x4, no 5x3) -> size must automatically shrink to 4x3 without touching icons!
            var tightCells = new HashSet<long>();
            for (int r = 0; r < primary.Height; r++)
                for (int c = 0; c < primary.Width; c++)
                    if (!(c >= 6 && c < 10 && r >= 2 && r < 5)) // Leave ONLY [6..9] x [2..4] = 4x3 free!
                        tightCells.Add(Key(c, r));
            Rectangle shrunk = FenceManager.FindIconFreeFencePlacement(
                new FenceConfig[0], tightCells, new[] { primary }, primary, new Point(1, 0), new Size(5, 4), m, 1f);
            Check(shrunk == new Rectangle(6, 2, 4, 3),
                "Size must shrink automatically to fit exact 4x3 icon-free hole without affecting icons. Got " + shrunk);

            // 3. Only a 3x3 hole exists -> must shrink to minimum 3x3 without affecting icons!
            var tinyHole = new HashSet<long>();
            for (int r = 0; r < primary.Height; r++)
                for (int c = 0; c < primary.Width; c++)
                    if (!(c >= 2 && c < 5 && r >= 4 && r < 7)) // Leave ONLY 3x3 free!
                        tinyHole.Add(Key(c, r));
            Rectangle minShrunk = FenceManager.FindIconFreeFencePlacement(
                new FenceConfig[0], tinyHole, new[] { primary }, primary, new Point(1, 0), new Size(5, 4), m, 1f);
            Check(minShrunk == new Rectangle(2, 4, 3, 3),
                "Size must shrink to minimum 3x3 hole without affecting icons. Got " + minShrunk);

            // 4. Primary 100% carpeted with icons, secondary free -> must spill to secondary cleanly!
            var fullPrimary = new HashSet<long>();
            for (int r = 0; r < primary.Height; r++)
                for (int c = 0; c < primary.Width; c++)
                    fullPrimary.Add(Key(c, r));
            Rectangle onSecondary = FenceManager.FindIconFreeFencePlacement(
                new FenceConfig[0], fullPrimary, new[] { primary, secondary }, primary, new Point(1, 0), new Size(5, 4), m, 1f);
            Check(!onSecondary.IsEmpty && secondary.Contains(onSecondary) && onSecondary.Width == 5 && onSecondary.Height == 4,
                "When primary has zero icon-free space, new fence must place cleanly on free secondary monitor. Got " + onSecondary);

            // 5. All monitors carpeted with icons -> must return Empty rather than displacing icons!
            Rectangle impossible = FenceManager.FindIconFreeFencePlacement(
                new FenceConfig[0], fullPrimary, new[] { primary }, primary, new Point(1, 0), new Size(5, 4), m, 1f);
            Check(impossible.IsEmpty, "When zero icon-free 3x3 space exists, placement must return Empty instead of disturbing icons.");
        }

        private static void CheckMinimalWrappingFence()
        {
            var m = new DesktopGridMetrics { CellWidth = 96, CellHeight = 120, OriginX = 2, OriginY = 2 };
            var area = new Rectangle(0, 0, 16, 10);

            var singleIcon = MakeIcon("solo", 5, 4, m, 0);
            var loose = new List<DesktopIconInfo>
            {
                singleIcon,
                MakeIcon("a", 5, 3, m, 1),
                MakeIcon("b", 8, 3, m, 2),
                MakeIcon("c", 6, 5, m, 3),
                MakeIcon("inner", 7, 4, m, 4),    // sits inside [5..8]x[3..5] bounding box
                MakeIcon("neighbor", 9, 3, m, 5), // sits on 1-cell border ring of [4..9]x[2..6]
                MakeIcon("edge", 0, 0, m, 6),     // flush against top-left screen edge
            };

            // 1. Wrapping a single interior icon -> exact minimal 3x3 fence, ZERO shift!
            Rectangle soloBox = new Rectangle(singleIcon.ScreenBounds.Left + 10, singleIcon.ScreenBounds.Top + 10, 20, 20);
            var soloPlan = FenceManager.ComputeMinimalWrappingFence(soloBox, area, new List<DesktopIconInfo> { singleIcon }, null, m, 1f);
            Check(soloPlan.HasSelection && soloPlan.FitsInMonitor && !soloPlan.OverlapsExistingFence &&
                  soloPlan.OuterRect == new Rectangle(4, 3, 3, 3) &&
                  soloPlan.ShiftCol == 0 && soloPlan.ShiftRow == 0 &&
                  soloPlan.WrappedIcons.Count == 1 && soloPlan.BorderConflictIcons.Count == 0,
                "Single interior icon must produce exact 3x3 minimal fence with zero movement. Got " + soloPlan.OuterRect);

            // 2. Wrapping scattered icons [5..8]x[3..5] -> exact minimal 6x5 fence (OuterCol=4, OuterRow=2, OuterCols=6, OuterRows=5)
            Rectangle multiBox = Rectangle.Union(loose[1].ScreenBounds, Rectangle.Union(loose[2].ScreenBounds, loose[3].ScreenBounds));
            var multiPlan = FenceManager.ComputeMinimalWrappingFence(multiBox, area, loose, null, m, 1f);
            Check(multiPlan.HasSelection && multiPlan.FitsInMonitor && !multiPlan.OverlapsExistingFence &&
                  multiPlan.OuterRect == new Rectangle(4, 2, 6, 5) &&
                  multiPlan.ShiftCol == 0 && multiPlan.ShiftRow == 0,
                "Scattered selection must produce exact minimal 6x5 bounding fence without shifting. Got " + multiPlan.OuterRect);
            // All icons inside [5..8]x[3..5] (solo, a, b, c, inner) wrapped; neighbor at (9,3) flagged as border conflict!
            Check(multiPlan.WrappedIcons.Count == 5 &&
                  multiPlan.BorderConflictIcons.Count == 1 && multiPlan.BorderConflictIcons[0].Identity == "neighbor",
                "Interior loose icons must be included in wrapped set and adjacent border-ring icon flagged.");

            // 3. Overlap with an existing fence must be detected and rejected!
            var blocker = new FenceConfig { Id = "existing", OuterCol = 4, OuterRow = 2, OuterCols = 4, OuterRows = 4 };
            var blockedPlan = FenceManager.ComputeMinimalWrappingFence(multiBox, area, loose, new[] { blocker }, m, 1f);
            Check(blockedPlan.HasSelection && blockedPlan.OverlapsExistingFence,
                "Wrapping fence overlapping an existing fence must be marked OverlapsExistingFence.");

            // 4. Flush screen-edge icon at (0,0) -> shifts minimally by (+1,+1) so title bar stays on-screen!
            var edgeIcon = loose[6];
            var edgePlan = FenceManager.ComputeMinimalWrappingFence(edgeIcon.ScreenBounds, area, new List<DesktopIconInfo> { edgeIcon }, null, m, 1f);
            Check(edgePlan.HasSelection && edgePlan.FitsInMonitor &&
                  edgePlan.OuterRect == new Rectangle(0, 0, 3, 3) &&
                  edgePlan.ShiftCol == 1 && edgePlan.ShiftRow == 1,
                "Edge icon at (0,0) must clamp OuterRect to (0,0,3,3) with minimal (+1,+1) shift. Got " + edgePlan.OuterRect);
        }

        private static void CheckNativePrivateDesktopCreateAndWrap(string dir)
        {
            var paths = new[] { "BlockA", "BlockB", "Wrap1", "Wrap2", "Wrap3" }
                .Select(n => Path.Combine(dir, "fc_" + n + ".txt")).ToArray();
            foreach (string p in paths) File.WriteAllText(p, Path.GetFileName(p));

            PrivateDesktopView.OnPrivateDesktop(delegate
            {
                const int cell = 64;
                using (var host = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(0, 0, 1280, 520), AutoScaleMode = AutoScaleMode.None, ShowInTaskbar = false })
                using (var panel = new Panel { Dock = DockStyle.Fill })
                using (var view = new PrivateDesktopView(paths))
                using (var controller = new DesktopIconController())
                {
                    host.Controls.Add(panel);
                    panel.Controls.Add(view);
                    host.Show();
                    view.SetSnap(false);
                    NativeMethods.SendMessage(view.Handle, 0x1000 + 53, IntPtr.Zero, new IntPtr(cell | (cell << 16)));
                    view.SetWorkArea(new Rectangle(0, 0, 1280, 520));
                    int[] cols = { 1, 2, 7, 9, 8 };
                    int[] rows = { 0, 1, 2, 2, 3 };
                    for (int i = 0; i < paths.Length; i++)
                        view.Items[i].Position = new Point(cell * cols[i] + 8, cell * rows[i] + 8);
                    view.SetSnap(true);

                    Check(controller.BindHandles(host.Handle, panel.Handle, view.Handle), "Bind private ListView.");
                    typeof(DesktopIconController).GetField("_shellView", Instance).SetValue(controller, new DesktopShellView(view));
                    var icons = controller.GetAllIcons();
                    var grid = controller.GetGridMetrics(icons);
                    Point[] startPositions = view.Items.Cast<ListViewItem>().Select(it => it.Position).ToArray();

                    var cfg = new AppConfig();
                    var mgr = (FenceManager)FormatterServices.GetUninitializedObject(typeof(FenceManager));
                    typeof(FenceManager).GetField("<Config>k__BackingField", Instance).SetValue(mgr, cfg);
                    typeof(FenceManager).GetField("_controller", Instance).SetValue(mgr, controller);
                    typeof(FenceManager).GetField("_attachedGeneration", Instance).SetValue(mgr, controller.HostGeneration);
                    typeof(FenceManager).GetField("_knownIconCells", Instance).SetValue(mgr, new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase));
                    typeof(FenceManager).GetField("_sessionClock", Instance).SetValue(mgr, System.Diagnostics.Stopwatch.StartNew());
                    typeof(FenceManager).GetField("_exiting", Instance).SetValue(mgr, true);

                    var area = new Rectangle(0, 0, 18, 7);

                    // A. Create blank new fence -> must avoid BlockA(1,0), BlockB(2,1) and Wrap1..3, moving ZERO icons!
                    var blankFence = mgr.CreateNewFence(grid, new[] { area }, area, icons, null, null, null, null, false);
                    Check(blankFence != null, "Blank new fence must find an icon-free spot.");
                    foreach (var ic in icons)
                    {
                        Check(!blankFence.IsInOuterRect(ic.GridCol(grid), ic.GridRow(grid)),
                            "New blank fence OuterRect must NOT touch existing icon " + ic.Name);
                    }
                    for (int i = 0; i < paths.Length; i++)
                        Check(view.Items[i].Position == startPositions[i],
                            "Creating a blank fence must move ZERO icons (item " + i + " moved)!");

                    // B. Drag-wrap the 3 loose icons Wrap1(7,2), Wrap2(9,2), Wrap3(8,3) -> exact minimal 5x4 fence, 0px movement!
                    var wrapTargets = icons.Skip(2).ToList();
                    Rectangle dragBox = Rectangle.Union(wrapTargets[0].ScreenBounds,
                        Rectangle.Union(wrapTargets[1].ScreenBounds, wrapTargets[2].ScreenBounds));
                    var plan = FenceManager.ComputeMinimalWrappingFence(dragBox, area, icons, cfg.Fences, grid, 1f);
                    Check(plan.HasSelection && plan.OuterRect == new Rectangle(6, 1, 5, 4) &&
                          plan.ShiftCol == 0 && plan.ShiftRow == 0 && plan.WrappedIcons.Count == 3,
                        "Drag-wrapping 3 icons at (7,2),(9,2),(8,3) must compute exact minimal (6,1,5,4) fence. Got " + plan.OuterRect);

                    var wrappedFence = mgr.CreateFenceWrappingIcons(plan, icons, grid, false);
                    Check(wrappedFence != null && wrappedFence.OuterCol == 6 && wrappedFence.OuterRow == 1 &&
                          wrappedFence.OuterCols == 5 && wrappedFence.OuterRows == 4,
                        "Created wrapping fence must have exact minimal bounds.");
                    Check(wrappedFence.MemberIconIds.Count == 3 &&
                          wrapTargets.All(ic => wrappedFence.MemberIconIds.Contains(ic.Identity)),
                        "All 3 wrapped icons must immediately become members of the new fence.");
                    for (int i = 0; i < paths.Length; i++)
                        Check(view.Items[i].Position == startPositions[i],
                            "Wrapping selected icons in place must move ZERO icons on native ListView (item " + i + " moved)!");

                    // C. Manual drag-in whose Explorer drop lands on the fence's 1-cell border ring while
                    //    the cursor released inside the fence's inner grid (and even if not in _knownIconCells yet!)
                    //    MUST be adopted into the fence's free inner cell rather than expelled outside!
                    var outsider = icons[0]; // BlockA at (1, 0), NOT in wrappedFence (6,1,5,4 -> inner 7..9, 2..3)
                    var gesture = new DesktopDragGesture();
                    typeof(FenceManager).GetField("_desktopDrag", Instance).SetValue(mgr, gesture);
                    typeof(FenceManager).GetField("_membership", Instance).SetValue(mgr, new DesktopMembership());
                    typeof(FenceManager).GetField("_desktopMenu", Instance).SetValue(mgr, new DesktopMenuPlacement());
                    typeof(FenceManager).GetField("_recentContextPlacements", Instance).SetValue(mgr,
                        Activator.CreateInstance(typeof(FenceManager).GetField("_recentContextPlacements", Instance).FieldType, true));
                    typeof(FenceManager).GetField("_forms", Instance).SetValue(mgr, new Dictionary<string, FenceChromeForm>());

                    // Press on BlockA at (1,0), release cursor inside wrappedFence's inner cell (7,2),
                    // while Explorer bumps BlockA onto wrappedFence's left border ring at (6,2)!
                    Point pressPt = new Point(outsider.IconBounds.Left + 8, outsider.IconBounds.Top + 8);
                    Point dropInsideFencePt = new Point(grid.OriginX + 7 * cell + 20, grid.OriginY + 2 * cell + 20);
                    gesture.Begin(pressPt, icons);
                    gesture.Release(dropInsideFencePt, true, new Size(4, 4), 100);
                    view.SetSnap(false);
                    view.Items[0].Position = new Point(cell * 6 + 8, cell * 2 + 8); // landed on left border ring of wrappedFence!
                    view.Items[0].Selected = true;
                    view.SetSnap(true);
                    // Intentionally keep _knownIconCells EMPTY to also prove brand-new icons (wasKnown==false) adopt cleanly!
                    typeof(FenceManager).GetField("_exiting", Instance).SetValue(mgr, false);
                    try
                    {
                        typeof(FenceManager).GetMethod("EnforceAllRules", Instance).Invoke(mgr, new object[] { true, false, false });
                    }
                    finally
                    {
                        typeof(FenceManager).GetField("_exiting", Instance).SetValue(mgr, true);
                        string cfgFile = AppConfig.GetConfigPath();
                        if (cfgFile.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && File.Exists(cfgFile))
                            File.Delete(cfgFile);
                    }

                    Check(wrappedFence.MemberIconIds.Contains(outsider.Identity),
                        "Manual drag-in released inside fence and snapped onto border ring MUST be adopted into the fence, not expelled!");
                    var afterDragIn = controller.GetAllIcons().Single(x => x.Identity == outsider.Identity);
                    Check(wrappedFence.IsInInnerRect(afterDragIn.GridCol(grid), afterDragIn.GridRow(grid)),
                        "Adopted border-ring drag-in must smoothly land inside a free inner slot of the fence! Got (" +
                        afterDragIn.GridCol(grid) + "," + afterDragIn.GridRow(grid) + ")");
                }
            });
        }

        internal static void Run(string dir)
        {
            CheckIconFreeNewFencePlacement();
            CheckMinimalWrappingFence();
            CheckNativePrivateDesktopCreateAndWrap(dir);
        }
    }
}
