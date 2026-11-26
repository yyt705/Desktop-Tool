using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DeskFences
{
    internal static class NativeMoveTests
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        // Genuine disposable PIDLs, independently controlled Explorer positions and batch results.
        private sealed class Folder : DesktopShellView.IFolderView
        {
            [DllImport("shell32.dll")]
            private static extern int SHGetNameFromIDList(IntPtr pidl, uint format, out IntPtr name);

            internal readonly MembershipTests.Folder ItemsByIndex = new MembershipTests.Folder();
            internal readonly Dictionary<string, Point> Locations = new Dictionary<string, Point>();
            internal readonly List<string[]> Batches = new List<string[]>();
            internal int ApplyCount = int.MaxValue;
            internal Action AfterBatch;

            private static string Name(IntPtr pidl)
            {
                IntPtr name;
                Marshal.ThrowExceptionForHR(SHGetNameFromIDList(pidl, 0x80028000, out name));
                try { return Marshal.PtrToStringUni(name); }
                finally { Marshal.FreeCoTaskMem(name); }
            }

            public int Item(int index, out IntPtr pidl) { return ItemsByIndex.Item(index, out pidl); }
            public int ItemCount(uint flags, out int count) { return ItemsByIndex.ItemCount(flags, out count); }
            public int GetItemPosition(IntPtr pidl, out NativeMethods.POINT point)
            {
                var pos = Locations[Name(pidl)];
                point = new NativeMethods.POINT { X = pos.X, Y = pos.Y };
                return 0;
            }
            public int SelectAndPositionItems(uint count, IntPtr[] ids, NativeMethods.POINT[] points, uint flags)
            {
                Check(flags == 0x80000080u && ids.Length == points.Length, "Preserve selection in group moves.");
                string[] names = ids.Select(Name).ToArray();
                Batches.Add(names);
                for (int i = 0; i < Math.Min(names.Length, ApplyCount); i++)
                    Locations[names[i]] = new Point(points[i].X, points[i].Y);
                ApplyCount = int.MaxValue;
                if (AfterBatch != null) AfterBatch();
                return 0;
            }
            public void GetCurrentViewMode() { throw new NotSupportedException(); }
            public void SetCurrentViewMode() { throw new NotSupportedException(); }
            public void GetFolder() { throw new NotSupportedException(); }
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
            var files = new[] { "moving-1", "moving-2", "stationary" }
                .Select(name => Path.Combine(dir, name + ".txt")).ToArray();
            var positions = new[] { new Point(10, 10), new Point(10, 90), new Point(330, 90) };
            var folder = new Folder();
            folder.ItemsByIndex.Files = (string[])files.Clone();
            var all = new List<DesktopIconInfo>();
            var grid = new DesktopGridMetrics { CellWidth = 80, CellHeight = 80 };
            using (var view = new DesktopShellView(folder))
            {
                for (int i = 0; i < files.Length; i++)
                {
                    File.WriteAllText(files[i], "drag test");
                    folder.Locations[files[i]] = positions[i];
                    string id, path;
                    Check(view.ReadItem(i, positions[i], out id, out path), "Read original Shell identities.");
                    all.Add(new DesktopIconInfo { Identity = id, ClientPosition = positions[i],
                        ScreenBounds = new Rectangle(positions[i], new Size(48, 60)) });
                }
                var members = all.Take(2).ToList();
                bool current = true;
                Func<int, int, bool> step = (previous, next) => view.TryMoveFenceMembers(all,
                    members, grid, new Point(previous, 0), new Point(next, 0), () => current);

                Array.Reverse(folder.ItemsByIndex.Files);
                Check(step(0, 80) && folder.Batches.Count == 1 && folder.Batches[0].SequenceEqual(files.Take(2)),
                    "A complete group must move in one PIDL batch after index reordering.");
                // Verify ±2px DPI grid-snap rounding by Explorer is accepted without rolling back.
                folder.Locations[files[0]] = new Point(92, 11);
                Check(step(80, 160) && folder.Batches.Count == 2 && folder.Locations[files[0]] == new Point(170, 10),
                    "A small Explorer snap-to-grid rounding adjustment must not roll back a valid step.");
                Check(step(160, 80) && folder.Batches.Count == 3, "Returning from the rounded step must succeed.");
                Check(!step(0, 160) && folder.Batches.Count == 3, "A stale source reference must prevent a write.");
                folder.Locations[files[2]] = new Point(170, 90);
                Check(!step(80, 160) && folder.Batches.Count == 3, "Check an outsider's live occupied cell before dispatch.");
                var unusualLabel = all[2];
                unusualLabel.ScreenBounds.Offset(-60, 0);
                all[2] = unusualLabel;
                Check(!step(80, 160) && folder.Batches.Count == 3,
                    "An outsider with an unusually wide label must still protect its exact native position.");
                folder.Locations[files[2]] = positions[2];
                folder.ApplyCount = 1;
                Check(!step(80, 160) && folder.Locations[files[0]] == new Point(90, 10) &&
                    folder.Locations[files[1]] == new Point(90, 90), "A partial move must restore the last confirmed step.");
                Check(folder.Batches.All(batch => batch.All(name => files.Take(2).Contains(name))) &&
                    folder.Locations[files[2]] == positions[2], "Compensation must preserve every nonmember.");
                folder.ApplyCount = 0;
                int before = folder.Batches.Count;
                Check(!step(80, 160) && folder.Batches.Count == before + 2,
                    "An accepted but deferred batch requires a compensating request for its old positions.");
                folder.AfterBatch = () => current = false;
                folder.ApplyCount = 1;
                before = folder.Batches.Count;
                Check(!step(80, 160) && folder.Batches.Count == before + 1,
                    "A replaced Explorer view must receive no rollback or further writes.");
                folder.AfterBatch = null;
                current = true;
                folder.Locations[files[0]] = new Point(90, 10);
                folder.AfterBatch = delegate
                {
                    folder.AfterBatch = null;
                    File.Move(files[1], Path.Combine(dir, "native-replaced-original.txt"));
                    File.WriteAllText(files[1], "new file in old location");
                };
                before = folder.Batches.Count;
                Check(!step(80, 160) && folder.Locations[files[0]] == new Point(90, 10) &&
                    folder.Batches.Skip(before + 1).All(batch => !batch.Contains(files[1])),
                    "A file replaced during dispatch cannot confirm a move or receive a rollback.");
                folder.Locations[files[1]] = new Point(90, 90);
                before = folder.Batches.Count;
                Check(!step(80, 160) && folder.Batches.Count == before,
                    "A replacement member must prevent the entire native batch, including earlier valid members.");
            }
        }
    }
}
