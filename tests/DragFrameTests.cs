using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace DeskFences
{
    internal static class DragFrameTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const int Cell = 64;
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] private static extern bool EqualRgn(IntPtr a, IntPtr b);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

        private static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        private static void Set(object owner, string field, object value)
        {
            owner.GetType().GetField(field, Instance).SetValue(owner, value);
        }

        private sealed class Chrome : FenceChromeForm
        {
            internal Chrome(FenceManager manager, FenceConfig config) : base(manager, config) { }
            internal void Press(int clicks = 1) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, clicks, 45, 15, 0)); }
            internal void MovePointer(int dx, int dy)
            {
                // Drive the ordinary handler without moving the system pointer.
                Point cursor = Cursor.Position;
                typeof(FenceChromeForm).GetField("_dragStartMouse", Instance).SetValue(this,
                    new Point(cursor.X - dx, cursor.Y - dy));
                OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, 45, 15, 0));
            }
            internal void Release()
            {
                Point p = PointToClient(Cursor.Position);
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            }
        }

        // Normal BeginFenceDrag / EndFenceMutation, enforcement, and native HWNDs. Skip the constructor
        // that binds Explorer, registers global hooks and starts watching the user's actual desktop.
        private sealed class Fixture : IDisposable
        {
            internal readonly FenceManager Manager;
            internal readonly AppConfig Config = new AppConfig();
            internal readonly Form Host = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(0, 0, 1280, 500), AutoScaleMode = AutoScaleMode.None, ShowInTaskbar = false };
            internal readonly Panel Panel = new Panel { Dock = DockStyle.Fill };
            internal readonly PrivateDesktopView View;
            internal readonly DesktopIconController Controller = new DesktopIconController();
            internal readonly Timer Animation = new Timer();
            internal readonly Dictionary<string, FenceChromeForm> Forms = new Dictionary<string, FenceChromeForm>();

            internal Fixture(string[] paths, bool snap, int[] customColumns = null, int[] customRows = null)
            {
                View = new PrivateDesktopView(paths);
                Host.Controls.Add(Panel);
                Panel.Controls.Add(View);
                Host.Show();
                View.SetSnap(false);
                const int setIconSpacing = 0x1000 + 53;
                NativeMethods.SendMessage(View.Handle, setIconSpacing, IntPtr.Zero, new IntPtr(Cell | (Cell << 16)));
                View.SetWorkArea(new Rectangle(0, 0, 1280, 500));
                int[] columns = customColumns ?? new[] { 1, 2, 7, 8 };
                int[] rows = customRows ?? new[] { 1, 1, 1, 1 };
                for (int i = 0; i < paths.Length; ++i)
                {
                    View.Items[i].Position = new Point(Cell * columns[i] + 9, Cell * rows[i] + 10);
                    View.Items[i].Selected = i == 0 || i == 2;
                }
                View.SetSnap(snap);
                Check(Controller.BindHandles(Host.Handle, Panel.Handle, View.Handle), "Bind the private native ListView.");
                Set(Controller, "_shellView", new DesktopShellView(View));
                Manager = (FenceManager)FormatterServices.GetUninitializedObject(typeof(FenceManager));
                Set(Manager, "<Config>k__BackingField", Config);
                Set(Manager, "_controller", Controller);
                Set(Manager, "_forms", Forms);
                Set(Manager, "_attachedGeneration", Controller.HostGeneration);
                Set(Manager, "_interlayerParent", Panel.Handle);
                Set(Manager, "_interlayerInsertAfter", NativeMethods.HWND_BOTTOM);
                Set(Manager, "_iconAnimTimer", Animation);
                foreach (string name in new[] { "_membership", "_knownIconCells", "_desktopMenu", "_recentContextPlacements",
                    "_sessionClock", "_iconAnimWatch" })
                {
                    var field = typeof(FenceManager).GetField(name, Instance);
                    field.SetValue(Manager, Activator.CreateInstance(field.FieldType, true));
                }
            }

            internal Chrome AddFence(List<DesktopIconInfo> icons, DesktopGridMetrics grid)
            {
                var fence = new FenceConfig { OuterCol = icons[0].GridCol(grid) - 1, OuterRow = icons[0].GridRow(grid) - 1,
                    OuterCols = 4, OuterRows = 3, MemberIconIds = icons.Select(i => i.Identity).ToList() };
                Config.Fences.Add(fence);
                var chrome = new Chrome(Manager, fence);
                Forms.Add(fence.Id, chrome);
                chrome.Show();
                return chrome;
            }

            public void Dispose()
            {
                Set(Manager, "_exiting", true);
                foreach (var form in Forms.Values) form.Dispose();
                Controller.Dispose();
                Animation.Dispose();
                Host.Dispose();
            }
        }

        private static int[] Pixels(Bitmap image, Rectangle area)
        {
            BitmapData data = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new int[area.Width * area.Height];
                for (int y = 0; y < area.Height; ++y)
                    Marshal.Copy(IntPtr.Add(data.Scan0, data.Stride * y), pixels, y * area.Width, area.Width);
                return pixels;
            }
            finally { image.UnlockBits(data); }
        }

        private static int[] ChromePixels(Chrome form, Rectangle area)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                using (var g = Graphics.FromImage(bitmap)) form.DrawLayeredSurface(g);
                return Pixels(bitmap, area);
            }
        }

        private static int[][] NativePixels(PrivateDesktopView view)
        {
            using (var bitmap = new Bitmap(view.Width, view.Height))
            {
                view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, view.Size));
                return view.Items.Cast<ListViewItem>().SelectMany(item => new[] {
                    Pixels(bitmap, item.GetBounds(ItemBoundsPortion.Icon)),
                    Pixels(bitmap, item.GetBounds(ItemBoundsPortion.Label))
                }).ToArray();
            }
        }

        private static Rectangle BoundsOf(IntPtr window)
        {
            NativeMethods.RECT bounds;
            Check(NativeMethods.GetWindowRect(window, out bounds), "Read native window bounds.");
            return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        }

        private static Rectangle Offset(Rectangle rect, Point delta) { rect.Offset(delta); return rect; }

        private static void CheckDrag(string[] paths, bool shaped)
        {
            // Place a 5th innocent outsider icon at top-left (col 0, row 0) to verify ComCtl6
            // offscreen parking NEVER splashes members onto the top-left desktop or displaces it!
            using (var f = new Fixture(paths, true, new[] { 2, 3, 8, 9, 0 }, new[] { 1, 1, 1, 1, 0 }))
            {
                var icons = f.Controller.GetAllIcons();
                var grid = f.Controller.GetGridMetrics(icons);
                Check(f.Controller.SnapshotComplete && icons.Count == 5 && grid.CellWidth == Cell && grid.CellHeight == Cell &&
                    grid.SnapToGridEnabled, "Five genuine shell identities and real native items must start on the 64-pixel grid.");
                var moving = f.AddFence(icons.GetRange(0, 2), grid);
                var stationary = f.AddFence(icons.GetRange(2, 2), grid);
                Point topLeftOutsiderStart = f.View.Items[4].Position;
                var window = f.View.Handle;
                IntPtr baseline = CreateRectRgn(0, 0, 0, 0);
                IntPtr observed = CreateRectRgn(0, 0, 0, 0);
                try
                {
                    if (shaped)
                    {
                        using (var region = new Region(new Rectangle(Point.Empty, f.View.Size)))
                        using (var g = f.View.CreateGraphics())
                        {
                            region.Exclude(new Rectangle(0, 0, 12, 8));
                            IntPtr installed = region.GetHrgn(g);
                            if (SetWindowRgn(window, installed, true) == 0)
                            {
                                DeleteObject(installed);
                                throw new InvalidOperationException("Install the fixture's original nonrectangular window region.");
                            }
                        }
                    }
                    int regionType = GetWindowRgn(window, baseline);
                    Check(shaped ? regionType == 3 : regionType == 0, "Start in the requested native clipping state.");
                    Rectangle frame = moving.Bounds, otherFrame = stationary.Bounds;
                    IntPtr backdrop = moving.Backdrop.Handle, otherBackdrop = stationary.Backdrop.Handle;
                    Rectangle body = BoundsOf(backdrop), otherBody = BoundsOf(otherBackdrop);
                    var chromeArea = new Rectangle(Point.Empty, frame.Size);
                    var otherArea = new Rectangle(Point.Empty, otherFrame.Size);
                    var bodyArea = Rectangle.FromLTRB(12, moving.HeaderHeight + 10, frame.Width - 12, frame.Height - 12);
                    var chrome = ChromePixels(moving, chromeArea);
                    var clearBody = ChromePixels(moving, bodyArea);
                    var otherChrome = ChromePixels(stationary, otherArea);
                    var artwork = NativePixels(f.View);
                    Check(artwork.All(pixels => pixels.Distinct().Count() >= 2), "Every native thumbnail and caption must contain visible ink.");
                    var timer = (Timer)typeof(FenceChromeForm).GetField("_animTimer", Instance).GetValue(moving);
                    Action<bool> finishDrop = delegate(bool enforceDrop) {
                        moving.SettleInteraction(true, enforceDrop);
                    };
                    Action<int, string> verifyRest = delegate(int deltaCols, string phase) {
                        var delta = new Point(deltaCols * Cell, 0);
                        Check(GetWindowRgn(window, observed) == regionType && (!shaped || EqualRgn(baseline, observed)),
                            phase + " changed the shared native ListView region.");
                        Check(moving.Bounds == Offset(frame, delta) && stationary.Bounds == otherFrame,
                            phase + " ended at wrong chrome bounds. Observed " + moving.Bounds + ", expected " + Offset(frame, delta));
                        Check(NativeMethods.IsWindowVisible(backdrop) && NativeMethods.IsWindowVisible(otherBackdrop) &&
                            moving.Backdrop.Handle == backdrop && stationary.Backdrop.Handle == otherBackdrop &&
                            BoundsOf(backdrop) == Offset(body, delta) && BoundsOf(otherBackdrop) == otherBody,
                            phase + " displaced or hid a fence's backdrop at rest.");
                        Check(ChromePixels(moving, bodyArea).SequenceEqual(clearBody) &&
                            ChromePixels(stationary, otherArea).SequenceEqual(otherChrome),
                            phase + " failed to clear drag surface interior back to Alpha==0 at rest.");
                        Check(!timer.Enabled, phase + " left animation timer running.");
                        Check(f.Controller.GetGridMetrics(f.Controller.GetAllIcons()).SnapToGridEnabled,
                            phase + " failed to restore LVS_EX_SNAPTOGRID at rest.");
                        Check(f.View.Items[4].Position == topLeftOutsiderStart,
                            phase + " displaced the innocent top-left outsider icon!");
                        for (int i = 0; i < icons.Count; ++i)
                        {
                            Point expected = icons[i].ClientPosition;
                            if (i < 2) expected.Offset(delta);
                            Check(f.View.Items[i].Position == expected && f.View.Items[i].Selected == (i == 0 || i == 2),
                                phase + " changed native item " + i + " position or selection. Expected " + expected + ", observed " + f.View.Items[i].Position);
                        }
                        var pixels = NativePixels(f.View);
                        for (int i = 0; i < artwork.Length; ++i)
                            Check(pixels[i].SequenceEqual(artwork[i]), phase + " changed native thumbnail or caption " + i + ".");
                    };
                    Action press = delegate {
                        moving.Press();
                        Check(moving.Capture, "Pressing the title must capture the private desktop's mouse.");
                        typeof(FenceChromeForm).GetField("_dragWorkAreas", Instance).SetValue(moving,
                            new[] { new Rectangle(-2, -2, 24, 12) });
                    };

                    verifyRest(0, "Before dragging");

                    // 1. Plain click & double-click on title bar MUST NOT trigger drag capture, icon parking, or snap toggling!
                    int snapBeforeClick = f.View.SnapChanges;
                    f.View.DirectPositions.Clear();
                    moving.Press(1);
                    Check(!(bool)typeof(FenceChromeForm).GetField("_isDragSurfaceActive", Instance).GetValue(moving),
                        "A zero-movement click must not activate rigid drag surface.");
                    moving.Release();
                    moving.Press(2);
                    Check(!(bool)typeof(FenceChromeForm).GetField("_dragPending", Instance).GetValue(moving) &&
                        !(bool)typeof(FenceChromeForm).GetField("_isDragging", Instance).GetValue(moving),
                        "A double-click on title bar must not initiate drag.");
                    Check(f.View.SnapChanges == snapBeforeClick && f.View.DirectPositions.Count == 0,
                        "Plain click and double-click must not toggle snap or move icons.");

                    // 2. Real drag past threshold: stays strictly snapped to the desktop grid with positive
                    //    hysteresis (zero back-and-forth oscillation at ANY pixel!) while parking members offscreen.
                    f.View.DirectPositions.Clear();
                    press();
                    int prevCol = moving.Config.OuterCol;
                    for (int px = 2; px <= 2 * Cell; px += 2)
                    {
                        moving.MovePointer(px, 0);
                        Check(moving.Config.OuterCol >= prevCol,
                            "Forward pixel sweep oscillated backward at px=" + px + ": " + prevCol + " -> " + moving.Config.OuterCol);
                        prevCol = moving.Config.OuterCol;
                    }
                    Check(moving.Config.OuterCol == 3, "Sweeping 2 cells forward must reach col 3.");
                    for (int px = 2 * Cell; px >= Cell; px -= 2)
                    {
                        moving.MovePointer(px, 0);
                        Check(moving.Config.OuterCol <= prevCol,
                            "Backward pixel sweep oscillated forward at px=" + px + ": " + prevCol + " -> " + moving.Config.OuterCol);
                        prevCol = moving.Config.OuterCol;
                    }
                    Check(moving.Config.OuterCol == 2, "Sweeping back to 1 cell must land cleanly at col 2.");
                    // Small tremor around the 0.5 midpoint must NOT oscillate!
                    moving.MovePointer((int)(0.58 * Cell), 0); // snaps to col 2 (step 1)
                    int settledStepCol = moving.Config.OuterCol;
                    for (int t = 0; t < 4; t++)
                    {
                        moving.MovePointer((int)(0.52 * Cell), 0);
                        Check(moving.Config.OuterCol == settledStepCol, "Tremor near midpoint must not oscillate!");
                        moving.MovePointer((int)(0.48 * Cell), 0);
                        Check(moving.Config.OuterCol == settledStepCol, "Tremor near midpoint must not oscillate!");
                    }
                    moving.MovePointer(Cell, 0);
                    Check((bool)typeof(FenceChromeForm).GetField("_isDragSurfaceActive", Instance).GetValue(moving),
                        "Dragging past threshold must activate the rigid icon drag layer.");
                    Check(NativeMethods.IsWindowVisible(backdrop),
                        "Backdrop must remain continuously visible during drag so glass brightness never flashes.");
                    Check(!ChromePixels(moving, bodyArea).SequenceEqual(clearBody),
                        "Rigid drag layer must composite the captured icons cleanly.");
                    Rectangle visibleHost = new Rectangle(0, 0, f.Host.Width, f.Host.Height);
                    foreach (var pos in f.View.DirectPositions)
                    {
                        Check(!visibleHost.Contains(pos.Value),
                            "Parking member icon " + pos.Key + " splashed visibly onto desktop workarea at " + pos.Value + "!");
                    }
                    Check(f.View.Items[4].Position == topLeftOutsiderStart,
                        "Parking members offscreen must NEVER push existing top-left desktop icons!");
                    Check(!(bool)typeof(FenceChromeForm).GetField("_isDragOverlapping", Instance).GetValue(moving),
                        "Valid drag step must keep normal liquid glass rim.");
                    moving.MovePointer(5 * Cell, 0);
                    Check((bool)typeof(FenceChromeForm).GetField("_isDragOverlapping", Instance).GetValue(moving),
                        "Hovering over stationary fence must show overlap warning rim.");
                    moving.MovePointer(11 * Cell, 0);
                    Check(!(bool)typeof(FenceChromeForm).GetField("_isDragOverlapping", Instance).GetValue(moving),
                        "Moving beyond stationary fence must clear overlap warning rim.");
                    moving.Release();
                    finishDrop(true);
                    verifyRest(11, "After rigid drag drop");
                    Check(!moving.Capture, "A completed drop must release input capture.");

                    // 3. Reverse drag back to original spot and test capture loss recovery
                    press();
                    moving.MovePointer(-11 * Cell, 0);
                    moving.Capture = false;
                    verifyRest(0, "After capture loss recovery");

                    // 4. P0 regression: exiting (_exiting=true) or display/wallpaper rebind (_rebindingDesktop=true)
                    //    mid-drag MUST still unpark and restore icons from offscreen (-5000,-5000)!
                    press();
                    moving.MovePointer(Cell, 0);
                    Check(f.View.Items[0].Position.X < -1000 && f.View.Items[1].Position.X < -1000,
                        "Members must be parked offscreen while dragging.");
                    Set(f.Manager, "_rebindingDesktop", true);
                    moving.SettleInteraction(true, false);
                    Set(f.Manager, "_rebindingDesktop", false);
                    Check(visibleHost.Contains(f.View.Items[0].Position) && visibleHost.Contains(f.View.Items[1].Position),
                        "Rebinding desktop mid-drag must STILL unpark member icons back onto visible desktop!");

                    press();
                    moving.MovePointer(-Cell, 0);
                    Check(f.View.Items[0].Position.X < -1000 && f.View.Items[1].Position.X < -1000,
                        "Members must be parked offscreen while dragging before exit.");
                    Set(f.Manager, "_exiting", true);
                    moving.SettleInteraction(true, false);
                    Set(f.Manager, "_exiting", false);
                    verifyRest(0, "After mid-drag exit settlement");

                    // 5. Mid-drag index shift regression: if ListView indices reorder during drag,
                    //    CommitRigidDragDrop must NOT use stale LVM_SETITEMPOSITION32 indices to move wrong items!
                    press();
                    moving.MovePointer(Cell, 0);
                    string[] origOrder = (string[])f.View.Folder.Files.Clone();
                    string tmpFile = f.View.Folder.Files[0];
                    f.View.Folder.Files[0] = f.View.Folder.Files[4];
                    f.View.Folder.Files[4] = tmpFile;
                    f.View.DirectPositions.Clear();
                    moving.Release();
                    finishDrop(true);
                    Check(f.View.DirectPositions.Count == 0,
                        "Stale index writes must be skipped when ListView indices shifted mid-drag!");
                    f.View.Folder.Files = origOrder;
                    Check(f.View.Folder.Positioned.All(path => path == paths[0] || path == paths[1]),
                        "Drag commits must position precisely the captured native members. Observed: " +
                        string.Join(", ", f.View.Folder.Positioned.Distinct().Select(Path.GetFileName)));
                }
                finally
                {
                    DeleteObject(observed);
                    DeleteObject(baseline);
                }
            }
        }

        private static void CheckDiagonalResize(string[] paths)
        {
            using (var f = new Fixture(paths, true, new[] { 2, 3, 4, 2 }, new[] { 2, 2, 2, 3 }))
            {
                var icons = f.Controller.GetAllIcons();
                var grid = f.Controller.GetGridMetrics(icons);
                var fence = new FenceConfig
                {
                    OuterCol = 1, OuterRow = 1, OuterCols = 5, OuterRows = 4, // 3x2 inner = 6 slots, 4 members
                    MemberIconIds = icons.Select(i => i.Identity).ToList()
                };
                f.Config.Fences.Add(fence);
                var chrome = new Chrome(f.Manager, fence);
                f.Forms.Add(fence.Id, chrome);
                chrome.Show();

                // Drag BottomRight corner diagonally: shrink width 5->3 (-2 cols), expand height 4->6 (+2 rows) => 1x4=4 slots!
                Point br = new Point(chrome.Width - 2, chrome.Height - 2);
                typeof(FenceChromeForm).GetMethod("OnMouseDown", Instance)
                    .Invoke(chrome, new object[] { new MouseEventArgs(MouseButtons.Left, 1, br.X, br.Y, 0) });
                Point cursor = Cursor.Position;
                typeof(FenceChromeForm).GetField("_resizeStartMouse", Instance)
                    .SetValue(chrome, new Point(cursor.X + 2 * Cell, cursor.Y - 2 * Cell));
                // First trigger past threshold, then set workarea and drive diagonal move
                typeof(FenceChromeForm).GetMethod("OnMouseMove", Instance)
                    .Invoke(chrome, new object[] { new MouseEventArgs(MouseButtons.Left, 0, br.X, br.Y, 0) });
                typeof(FenceChromeForm).GetField("_resizeWorkArea", Instance)
                    .SetValue(chrome, new Rectangle(0, 0, 20, 12));
                typeof(FenceChromeForm).GetMethod("OnMouseMove", Instance)
                    .Invoke(chrome, new object[] { new MouseEventArgs(MouseButtons.Left, 0, br.X, br.Y, 0) });
                Check(fence.OuterCols == 3 && fence.OuterRows == 6,
                    "Diagonal corner resize from 5x4 to 3x6 with 4 members must succeed in one move. Got " + fence.OuterCols + "x" + fence.OuterRows);

                // Interrupted resize (applyIconPositions=false) must roll back OuterCols/OuterRows
                chrome.SettleInteraction(false, false);
                Check(fence.OuterCols == 5 && fence.OuterRows == 4,
                    "An interrupted resize without applyIconPositions must roll back geometry.");
            }
        }

        private static void CheckCollision(string[] paths, bool snap, bool gridDrop)
        {
            using (var f = new Fixture(paths, snap))
            {
                var icons = f.Controller.GetAllIcons();
                var grid = f.Controller.GetGridMetrics(icons);
                Check(f.Controller.SnapshotComplete && icons.Count == 2 && grid.SnapToGridEnabled == snap,
                    "Start with both outsiders and the requested grid setting.");
                var fence = new FenceConfig { OuterCol = icons[0].GridCol(grid), OuterRow = icons[0].GridRow(grid),
                    OuterCols = 3, OuterRows = 3 };
                f.Config.Fences.Add(fence);
                Check(icons.All(i => fence.IsInOuterRect(i.GridCol(grid), i.GridRow(grid))), "The dropped fence must overlap both outsiders.");
                int previous = f.View.SnapChanges;
                Set(f.Manager, "_isMutatingFence", true);
                f.Manager.EndFenceMutation(null, true, gridDrop);
                if (gridDrop && snap)
                {
                    Check(!f.Animation.Enabled && f.View.SnapChanges == previous,
                        "A snapped title-bar collision must not toggle snap or start an icon animation.");
                    var folder = f.View.Folder;
                    Check(folder.PositionCalls == 1 && folder.Targets.Distinct().Count() == 2,
                        "Expel both outsiders to distinct cells in one Shell move.");
                    for (int i = 0; i < folder.Positioned.Count; ++i)
                    {
                        var icon = icons.Single(item => item.ParsingName == folder.Positioned[i]);
                        var target = folder.Targets[i];
                        Check(!fence.IsInOuterRect(icon.GridCol(grid) + (target.X - icon.ClientPosition.X) / grid.CellWidth,
                            icon.GridRow(grid) + (target.Y - icon.ClientPosition.Y) / grid.CellHeight),
                            "Each outsider must land outside the fence.");
                    }
                }
                else Check(f.Animation.Enabled && f.View.Folder.PositionCalls == 0, "Other collision paths must still slide to their targets.");
            }
        }

        private static void CheckBlockedDropRelocation(string[] paths)
        {
            using (var f = new Fixture(paths, true))
            {
                var icons = f.Controller.GetAllIcons();
                var grid = f.Controller.GetGridMetrics(icons);
                // Moving fence owns icons[0..1] (at cols 1,2 -> OuterCol 0, OuterCols 4).
                // Stationary fence owns icons[2] (at col 7 -> OuterCol 6, OuterCols 3), leaving icons[3] as a loose outsider at col 8.
                var moving = f.AddFence(icons.GetRange(0, 2), grid);
                var stationary = new FenceConfig
                {
                    OuterCol = icons[2].GridCol(grid) - 1,
                    OuterRow = icons[2].GridRow(grid) - 1,
                    OuterCols = 3,
                    OuterRows = 3,
                    MemberIconIds = new List<string> { icons[2].Identity }
                };
                f.Config.Fences.Add(stationary);
                int snapBefore = f.View.SnapChanges;

                // 1. Release while overlapping stationary fence -> auto-relocates to nearest non-overlapping spot!
                moving.Press();
                typeof(FenceChromeForm).GetField("_dragWorkAreas", Instance).SetValue(moving,
                    new[] { new Rectangle(0, 0, 20, 10) });
                moving.MovePointer(6 * Cell, 0); // directly onto stationary (OuterCol 6)
                moving.Release();
                Check(moving.IsInteracting,
                    "In-flight overlap auto-relocation glide must report IsInteracting so watch timer will not abort it.");
                moving.SettleInteraction(true, true);
                Check(!moving.IsInteracting,
                    "Settled overlap auto-relocation must clear IsInteracting.");
                Check(!(bool)typeof(FenceChromeForm).GetField("_isDragSurfaceActive", Instance).GetValue(moving),
                    "Settled overlap auto-relocation must clear rigid drag surface.");
                Check(!FenceManager.ZonesOverlap(moving.Config.OuterCol, moving.Config.OuterRow,
                    moving.Config.OuterCols, moving.Config.OuterRows,
                    stationary.OuterCol, stationary.OuterRow, stationary.OuterCols, stationary.OuterRows, grid, moving.DpiScale),
                    "Releasing over another fence must auto-relocate to a nearby non-overlapping position.");
                Check(moving.Config.OuterCol != 0 || moving.Config.OuterRow != 0,
                    "Releasing over a distant overlapping fence must relocate near the drop target, not stay at origin.");
                Check(f.Controller.GetGridMetrics(f.Controller.GetAllIcons()).SnapToGridEnabled,
                    "Overlap auto-relocation must cleanly restore LVS_EX_SNAPTOGRID after settling.");

                // 2. Release directly onto loose desktop icon (icons[3]) -> smoothly animates outsider out!
                var freshIcons = f.Controller.GetAllIcons();
                var outsider = freshIcons.Single(i => i.Identity == icons[3].Identity);
                Point outsiderStartPos = outsider.ClientPosition;
                int targetCol = outsider.GridCol(grid) - 1;
                int targetRow = outsider.GridRow(grid) - 1;
                if (!FenceManager.ZonesOverlap(targetCol, targetRow, moving.Config.OuterCols, moving.Config.OuterRows,
                    stationary.OuterCol, stationary.OuterRow, stationary.OuterCols, stationary.OuterRows, grid, moving.DpiScale))
                {
                    int dx = (targetCol - moving.Config.OuterCol) * Cell;
                    int dy = (targetRow - moving.Config.OuterRow) * Cell;
                    moving.Press();
                    typeof(FenceChromeForm).GetField("_dragWorkAreas", Instance).SetValue(moving,
                        new[] { new Rectangle(0, 0, 20, 10) });
                    moving.MovePointer(dx, dy);
                    moving.Release();
                    moving.SettleInteraction(true, true);
                    Check(moving.Config.OuterCol == targetCol && moving.Config.OuterRow == targetRow,
                        "Releasing over a loose desktop icon must land the fence on target.");
                    Check(f.Animation.Enabled,
                        "Conflicting desktop icon must be expelled via smooth transition animation instead of teleporting.");
                    // Complete the smooth slide animation and verify final outside cell + SnapToGrid restored!
                    typeof(FenceManager).GetMethod("FinishIconAnimationImmediately", Instance)
                        .Invoke(f.Manager, new object[] { true });
                    Check(!f.Animation.Enabled, "Finishing outsider slide animation must stop the animation timer.");
                    var afterDrop = f.Controller.GetAllIcons();
                    var movedOutsider = afterDrop.Single(i => i.Identity == icons[3].Identity);
                    Check(movedOutsider.ClientPosition != outsiderStartPos &&
                          !moving.Config.IsInOuterRect(movedOutsider.GridCol(grid), movedOutsider.GridRow(grid)),
                        "The smoothly expelled loose outsider must land outside the dropped fence.");
                    Check(f.Controller.GetGridMetrics(afterDrop).SnapToGridEnabled,
                        "SnapToGrid must be restored once the outsider expulsion animation completes.");
                }

                // 3. Packed fence capture with selected icons must still preserve transparent background
                //    (never falsely fall back to opaque/low-res flood-fill when >50% of body is occupied!).
                var packedMembers = f.Controller.GetAllIcons().Take(2).ToList();
                Rectangle tightBody = Rectangle.Union(packedMembers[0].ScreenBounds, packedMembers[1].ScreenBounds);
                tightBody.Inflate(4, 4);
                using (var packedBmp = NativeMethods.CaptureNativeListViewIconsTransparent(f.View.Handle, tightBody, packedMembers))
                {
                    Check(packedBmp != null && packedBmp.Width == tightBody.Width && packedBmp.Height == tightBody.Height,
                        "Packed fence capture must return exact 1:1 bitmap.");
                    Check(packedBmp.GetPixel(0, 0).A == 0,
                        "Packed fence capture corners outside icons must stay strictly transparent (Alpha==0).");
                }
            }
        }

        internal static void Run(string dir)
        {
            string config = Path.GetFullPath(AppConfig.GetConfigPath());
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Check(config.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && !File.Exists(config),
                "Run in a fresh temporary executable directory so an existing layout stays intact.");
            var paths = new[] { "Alpha", "Bravo", "Gamma", "Delta", "Echo" }.Select(name => Path.Combine(dir, name + ".txt")).ToArray();
            foreach (string path in paths) File.WriteAllText(path, Path.GetFileName(path));
            PrivateDesktopView.OnPrivateDesktop(delegate {
                CheckCollision(paths.Take(2).ToArray(), true, true);
                CheckCollision(paths.Take(2).ToArray(), false, true);
                CheckCollision(paths.Take(2).ToArray(), true, false);
                CheckDrag(paths, false);
                CheckDrag(paths, true);
                CheckBlockedDropRelocation(paths.Take(4).ToArray());
                CheckDiagonalResize(paths.Take(4).ToArray());
            });
        }
    }
}
