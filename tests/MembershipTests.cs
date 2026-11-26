using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace DeskFences
{
    internal static class MembershipTests
    {
        private static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string path, IntPtr bind, out IntPtr pidl, uint attributes, out uint result);
        [DllImport("shell32.dll")]
        private static extern int SHGetNameFromIDList(IntPtr pidl, uint format, out IntPtr name);

        // Use genuine PIDLs for disposable files in an isolated view. No desktop writes.
        internal sealed class Folder : DesktopShellView.IFolderView
        {
            internal string[] Files;
            internal readonly List<string> Positioned = new List<string>();
            internal readonly List<Point> Targets = new List<Point>();
            internal int PositionCalls;
            public int Item(int index, out IntPtr pidl)
            {
                uint attrs;
                return SHParseDisplayName(Files[index], IntPtr.Zero, out pidl, 0, out attrs);
            }
            public int GetItemPosition(IntPtr pidl, out NativeMethods.POINT point)
            {
                point = new NativeMethods.POINT();
                return 0;
            }
            public int SelectAndPositionItems(uint count, IntPtr[] ids, NativeMethods.POINT[] points, uint flags)
            {
                PositionCalls++;
                Check((flags & 0x80000000u) != 0, "Positioning must preserve the Explorer item's selected and focused states.");
                Check(flags == 0x80000080u && ids.Length == points.Length, "Only position the requested Shell items.");
                for (int i = 0; i < count; i++)
                {
                    Targets.Add(new Point(points[i].X, points[i].Y));
                    IntPtr name;
                    Marshal.ThrowExceptionForHR(SHGetNameFromIDList(ids[i], 0x80028000, out name));
                    try { Positioned.Add(Marshal.PtrToStringUni(name)); }
                    finally { Marshal.FreeCoTaskMem(name); }
                }
                return 0;
            }
            public void GetCurrentViewMode() { throw new NotSupportedException(); }
            public void SetCurrentViewMode() { throw new NotSupportedException(); }
            public void GetFolder() { throw new NotSupportedException(); }
            public int ItemCount(uint flags, out int count) { count = Files.Length; return 0; }
            public void Items() { throw new NotSupportedException(); }
            public void GetSelectionMarkedItem() { throw new NotSupportedException(); }
            public void GetFocusedItem() { throw new NotSupportedException(); }
            public void GetSpacing() { throw new NotSupportedException(); }
            public void GetDefaultSpacing() { throw new NotSupportedException(); }
            public void GetAutoArrange() { throw new NotSupportedException(); }
            public void SelectItem() { throw new NotSupportedException(); }
        }

        internal static void Run(string dir)
        {
            string txt = Path.Combine(dir, "Report.txt"), csv = Path.Combine(dir, "Report.csv");
            File.WriteAllText(txt, "1");
            File.WriteAllText(csv, "2");
            string id = DesktopItemIdentity.ForParsingName(txt);
            string csvId = DesktopItemIdentity.ForParsingName(csv);
            Check(id != csvId && id.StartsWith("file:"), "Two files with the same visible label need distinct keys.");
            var fence = new FenceConfig { OuterCol = 0, OuterRow = 0, OuterCols = 4, OuterRows = 4 };
            fence.MemberIconNames.Add("Report");
            var fences = new List<FenceConfig> { fence };
            var icons = new List<DesktopIconInfo> {
                new DesktopIconInfo { Name="Report", Identity=id, ParsingName=txt, ScreenBounds=new Rectangle(80,80,40,40) },
                new DesktopIconInfo { Name="Report", Identity=csvId, ParsingName=csv, ScreenBounds=new Rectangle(640,0,40,40) }
            };
            var grid = new DesktopGridMetrics { CellWidth = 80, CellHeight = 80 };
            var members = new DesktopMembership();
            members.Reconcile(fences, icons, grid, 0);
            Check(fence.MemberIconIds.Count == 1 && fence.MemberIconIds[0] == id, "Legacy names must migrate to the member inside the fence.");
            var empty = new List<DesktopIconInfo>();
            members.ResetObservations();
            members.Reconcile(fences, empty, grid, 0);
            members.Reconcile(fences, empty, grid, 9000);
            Check(fence.MemberIconIds.Contains(id), "An empty view during Explorer restart must not erase the layout.");
            members.Reconcile(fences, icons, grid, 0);
            var folder = new Folder { Files = new[] {txt, csv} };
            using (var view = new DesktopShellView(folder))
            {
                string item, name;
                view.BeginSnapshot();
                Check(view.ReadItem(0, Point.Empty, out item, out name), "Could not read test file PIDL.");
                var move = new List<KeyValuePair<string, Point>> { new KeyValuePair<string, Point>(item, new Point(160, 80)) };
                Array.Reverse(folder.Files);
                view.Move(move);
                Check(folder.Positioned.Count == 1 && folder.Positioned[0] == txt, "An index change must not move another file.");
                string renamed = Path.Combine(dir, "Renamed.txt");
                File.Move(txt, renamed);
                Check(DesktopItemIdentity.ForParsingName(renamed) == id, "Renaming a file should preserve its identity.");
                File.WriteAllText(txt, "replacement");
                view.Move(move);
                Check(folder.Positioned.Count == 1, "A replacement file must not receive a stale move.");
                icons[0] = new DesktopIconInfo { Name="Report", Identity=DesktopItemIdentity.ForParsingName(txt) };
                members.Reconcile(fences, icons, grid, 10);
                Check(fence.MemberIconIds.Count == 1, "One transient omission must preserve membership.");
                members.Reconcile(fences, icons, grid, 3500);
                Check(fence.MemberIconIds.Count == 0, "A replacement file must not inherit deleted membership.");
            }
        }
    }
}
