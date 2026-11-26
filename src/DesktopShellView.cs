using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DeskFences
{
    // IFolderView positions items by child PIDL. List-view indices are used
    // only to read the current frame's rectangles and selection.
    internal sealed class DesktopShellView : IDisposable
    {
        private const uint SVSI_POSITIONITEM = 0x80;
        private const uint SVSI_NOSTATECHANGE = 0x80000000;

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"),
         InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IShellWindows
        {
            void Count(); void Item(); void NewEnum(); void Register();
            void RegisterPending(); void Revoke(); void OnNavigate(); void OnActivated();
            [PreserveSig] int FindWindowSW(ref object location, ref object root, int windowClass,
                out int hwnd, int options, [MarshalAs(UnmanagedType.IDispatch)] out object desktop);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IShellBrowser browser);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void GetWindow(); void ContextSensitiveHelp(); void InsertMenus(); void SetMenu(); void RemoveMenus();
            void SetStatusText(); void EnableModeless(); void TranslateAccelerator(); void BrowseObject();
            void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
            [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
        }

        [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IFolderView
        {
            void GetCurrentViewMode(); void SetCurrentViewMode(); void GetFolder();
            [PreserveSig] int Item(int index, out IntPtr pidl);
            [PreserveSig] int ItemCount(uint flags, out int count);
            void Items(); void GetSelectionMarkedItem(); void GetFocusedItem();
            [PreserveSig] int GetItemPosition(IntPtr pidl, out NativeMethods.POINT point);
            void GetSpacing(); void GetDefaultSpacing(); void GetAutoArrange(); void SelectItem();
            [PreserveSig] int SelectAndPositionItems(uint count,
                [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex=0)] IntPtr[] pidls,
                [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex=0)] NativeMethods.POINT[] points, uint flags);
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetNameFromIDList(IntPtr pidl, uint flags, out IntPtr name);
        [DllImport("shell32.dll")]
        private static extern uint ILGetSize(IntPtr pidl);

        private sealed class Entry
        {
            internal byte[] Pidl;
            internal string ParsingName;
        }

        private IFolderView _view;
        private readonly Dictionary<string, Entry> _items = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        internal DesktopShellView(IFolderView view = null) { _view = view; }

        internal bool Connect()
        {
            if (_view != null) return true;
            object windows = null, desktop = null, view = null;
            IShellBrowser browser = null;
            try
            {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")));
                object location = 0, root = null;
                int hwnd;
                if (((IShellWindows)windows).FindWindowSW(ref location, ref root, 8, out hwnd, 1, out desktop) != 0 || desktop == null) return false;
                Guid service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                Guid iid = typeof(IShellBrowser).GUID;
                if (((IServiceProvider)desktop).QueryService(ref service, ref iid, out browser) != 0 ||
                    browser.QueryActiveShellView(out view) != 0) return false;
                _view = (IFolderView)view;
                view = null; // retained until Explorer replaces this desktop
                return true;
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
            finally { Release(view); Release(browser); Release(desktop); Release(windows); }
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        internal void BeginSnapshot() { _items.Clear(); }

        internal bool HasItemCount(int expected)
        {
            try
            {
                int count;
                return _view != null && _view.ItemCount(2, out count) == 0 && count == expected;
            }
            catch (COMException) { Dispose(); return false; }
        }

        internal bool ReadItem(int index, Point position, out string id, out string name, Func<string, string> resolveIdentity = null)
        {
            id = null;
            name = null;
            IntPtr pidl = IntPtr.Zero, text = IntPtr.Zero;
            try
            {
                NativeMethods.POINT shellPosition;
                if (_view.Item(index, out pidl) != 0 || pidl == IntPtr.Zero ||
                    _view.GetItemPosition(pidl, out shellPosition) != 0 ||
                    shellPosition.X != position.X || shellPosition.Y != position.Y ||
                    SHGetNameFromIDList(pidl, 0x80028000, out text) != 0) return false;
                name = Marshal.PtrToStringUni(text);
                if (string.IsNullOrEmpty(name)) return false;
                id = resolveIdentity != null ? resolveIdentity(name) : DesktopItemIdentity.ForParsingName(name);
                if (string.IsNullOrEmpty(id) || _items.ContainsKey(id)) return false;
                byte[] bytes = new byte[ILGetSize(pidl)];
                Marshal.Copy(pidl, bytes, 0, bytes.Length);
                _items.Add(id, new Entry { Pidl = bytes, ParsingName = name });
                return true;
            }
            finally
            {
                Marshal.FreeCoTaskMem(text);
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        internal bool VerifyItemAt(int index, Point position, string expectedIdentity, Func<string, string> resolveIdentity = null)
        {
            if (_view == null || string.IsNullOrEmpty(expectedIdentity) || !_items.ContainsKey(expectedIdentity))
                return false;
            IntPtr pidl = IntPtr.Zero, text = IntPtr.Zero;
            try
            {
                NativeMethods.POINT shellPosition;
                if (_view.Item(index, out pidl) != 0 || pidl == IntPtr.Zero ||
                    _view.GetItemPosition(pidl, out shellPosition) != 0 ||
                    shellPosition.X != position.X || shellPosition.Y != position.Y ||
                    SHGetNameFromIDList(pidl, 0x80028000, out text) != 0)
                    return false;
                string name = Marshal.PtrToStringUni(text);
                if (string.IsNullOrEmpty(name)) return false;
                string currentId = resolveIdentity != null ? resolveIdentity(name) : DesktopItemIdentity.ForParsingName(name);
                return StringComparer.OrdinalIgnoreCase.Equals(currentId, expectedIdentity);
            }
            catch (COMException) { return false; }
            finally
            {
                Marshal.FreeCoTaskMem(text);
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        internal bool VerifyItemIndices(List<DesktopIconInfo> members)
        {
            if (_view == null || members == null || members.Count == 0) return true;
            foreach (var member in members)
            {
                IntPtr pidl = IntPtr.Zero, text = IntPtr.Zero;
                try
                {
                    if (_view.Item(member.Index, out pidl) != 0 || pidl == IntPtr.Zero ||
                        SHGetNameFromIDList(pidl, 0x80028000, out text) != 0) return false;
                    string name = Marshal.PtrToStringUni(text);
                    if (string.IsNullOrEmpty(name)) return false;
                    string currentId = DesktopItemIdentity.ForParsingName(name);
                    if (!StringComparer.OrdinalIgnoreCase.Equals(currentId, member.Identity)) return false;
                }
                catch (COMException) { return false; }
                finally
                {
                    Marshal.FreeCoTaskMem(text);
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            return true;
        }

        private static bool SameItem(string key, Entry entry)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(key, DesktopItemIdentity.ForParsingName(entry.ParsingName));
        }

        internal bool ReadPosition(string id, out Point position, bool verifyIdentity = false)
        {
            position = Point.Empty;
            Entry entry;
            if (_view == null || !_items.TryGetValue(id, out entry) || (verifyIdentity && !SameItem(id, entry))) return false;
            var pin = GCHandle.Alloc(entry.Pidl, GCHandleType.Pinned);
            try
            {
                NativeMethods.POINT native;
                if (_view.GetItemPosition(pin.AddrOfPinnedObject(), out native) != 0) return false;
                position = new Point(native.X, native.Y);
                return true;
            }
            finally { pin.Free(); }
        }

        internal static bool CloseEnough(Point actual, Point expected, DesktopGridMetrics grid)
        {
            int tolX = Math.Max(3, Math.Min(6, Math.Max(1, grid.CellWidth) / 12));
            int tolY = Math.Max(3, Math.Min(6, Math.Max(1, grid.CellHeight) / 12));
            return Math.Abs(actual.X - expected.X) <= tolX && Math.Abs(actual.Y - expected.Y) <= tolY;
        }

        private bool PositionsMatch(List<KeyValuePair<string, Point>> targets, DesktopGridMetrics grid)
        {
            foreach (var target in targets)
            {
                Point actual;
                if (!ReadPosition(target.Key, out actual, true) || !CloseEnough(actual, target.Value, grid)) return false;
            }
            return true;
        }

        internal bool TryMoveFenceMembers(List<DesktopIconInfo> desktop, List<DesktopIconInfo> members,
            DesktopGridMetrics grid, Point previousDelta, Point delta, Func<bool> current)
        {
            var view = _view;
            Func<bool> stillCurrent = () => view != null && _view == view && current();
            var previous = new List<KeyValuePair<string, Point>>(members.Count);
            var requested = new List<KeyValuePair<string, Point>>(members.Count);
            var memberIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinationCells = new HashSet<Point>();
            var destinationPositions = new HashSet<Point>();
            bool dispatched = false;
            try
            {
                if (!stillCurrent() || !HasItemCount(desktop.Count)) return false;
                int stepDx = delta.X - previousDelta.X;
                int stepDy = delta.Y - previousDelta.Y;
                var orderedMembers = members;
                if (members.Count > 1 && (stepDx != 0 || stepDy != 0))
                {
                    // Stable sort back-to-front along the movement vector so adjacent icons NEVER self-collide
                    // when Explorer applies LVS_EX_SNAPTOGRID sequentially inside SelectAndPositionItems!
                    orderedMembers = System.Linq.Enumerable.ToList(
                        System.Linq.Enumerable.OrderByDescending(members,
                            m => (long)m.ClientPosition.X * stepDx + (long)m.ClientPosition.Y * stepDy));
                }
                foreach (var member in orderedMembers)
                {
                    var oldPosition = member.ClientPosition;
                    var newPosition = member.ClientPosition;
                    oldPosition.Offset(previousDelta);
                    newPosition.Offset(delta);
                    previous.Add(new KeyValuePair<string, Point>(member.Identity, oldPosition));
                    requested.Add(new KeyValuePair<string, Point>(member.Identity, newPosition));
                    memberIds.Add(member.Identity);
                    destinationPositions.Add(newPosition);
                    destinationCells.Add(new Point(member.GridCol(grid) + delta.X / grid.CellWidth,
                        member.GridRow(grid) + delta.Y / grid.CellHeight));
                }
                // A full desktop snapshot at mouse-down binds cell geometry to PIDLs. Query
                // their live positions without re-enumerating names, file metadata or image rectangles.
                foreach (var original in desktop)
                {
                    if (memberIds.Contains(original.Identity)) continue;
                    Point actual;
                    if (!ReadPosition(original.Identity, out actual)) return false;
                    if (destinationPositions.Contains(actual)) return false;
                    var item = original;
                    item.ScreenBounds.Offset(actual.X - original.ClientPosition.X, actual.Y - original.ClientPosition.Y);
                    if (destinationCells.Contains(new Point(item.GridCol(grid), item.GridRow(grid)))) return false;
                }
                if (!stillCurrent() || !HasItemCount(desktop.Count) || !PositionsMatch(previous, grid) || !stillCurrent()) return false;
                if (members.Count == 0 || previousDelta == delta) return true;
                if (PositionItems(requested, stillCurrent, true, out dispatched) && stillCurrent() &&
                    PositionsMatch(requested, grid) && stillCurrent()) return true;
            }
            catch (COMException) { }

            // Explorer can accept a batch, apply only its first items, or queue its writes.
            // Supersede the entire request, including items still observed at the old position.
            if (dispatched && stillCurrent())
            {
                bool sent;
                try { PositionItems(previous, stillCurrent, false, out sent); }
                catch (COMException) { }
                try
                {
                    if (!stillCurrent()) return false;
                    var remaining = new List<KeyValuePair<string, Point>>();
                    foreach (var target in previous)
                    {
                        Point actual;
                        if (ReadPosition(target.Key, out actual, true) && !CloseEnough(actual, target.Value, grid))
                            remaining.Add(target);
                    }
                    if (remaining.Count != 0) PositionItems(remaining, stillCurrent, false, out sent);
                }
                catch (COMException) { }
            }
            return false;
        }

        internal void Move(List<KeyValuePair<string, Point>> moves)
        {
            bool dispatched;
            PositionItems(moves, null, false, out dispatched);
        }

        private bool PositionItems(List<KeyValuePair<string, Point>> moves, Func<bool> current, bool requireAll, out bool dispatched)
        {
            dispatched = false;
            if (_view == null || moves.Count == 0) return false;
            var pins = new List<GCHandle>(moves.Count);
            var items = new List<IntPtr>(moves.Count);
            var points = new List<NativeMethods.POINT>(moves.Count);
            try
            {
                foreach (var move in moves)
                {
                    Entry entry;
                    // A path-backed child PIDL can resolve to a replacement file
                    // before the next desktop notification or animation frame.
                    if (!_items.TryGetValue(move.Key, out entry) || !SameItem(move.Key, entry))
                    {
                        if (requireAll) return false;
                        continue;
                    }
                    var pin = GCHandle.Alloc(entry.Pidl, GCHandleType.Pinned);
                    pins.Add(pin);
                    items.Add(pin.AddrOfPinnedObject());
                    points.Add(new NativeMethods.POINT { X = move.Value.X, Y = move.Value.Y });
                }
                if (items.Count == 0 || (current != null && !current())) return false;
                dispatched = true;
                int result = _view.SelectAndPositionItems((uint)items.Count, items.ToArray(),
                    points.ToArray(), SVSI_POSITIONITEM | SVSI_NOSTATECHANGE);
                Marshal.ThrowExceptionForHR(result);
                return result == 0;
            }
            finally { foreach (var pin in pins) pin.Free(); }
        }

        public void Dispose()
        {
            _items.Clear();
            Release(_view);
            _view = null;
        }
    }

    internal static class DesktopItemIdentity
    {
        internal static bool ConfirmRemoval(string id, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (id.StartsWith("shell:", StringComparison.Ordinal)) return true;
            try { File.GetAttributes(path); }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            string current = ForParsingName(path);
            return current.StartsWith("file:", StringComparison.Ordinal) &&
                !string.Equals(id, current, StringComparison.OrdinalIgnoreCase);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileInfo
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Modified;
            internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);

        internal static string ForParsingName(string name)
        {
            if (name.StartsWith("::", StringComparison.Ordinal) || !Path.IsPathRooted(name)) return "shell:" + name;
            // Metadata only, including directories and offline reparse points.
            using (var handle = CreateFile(name, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
            {
                FileInfo info;
                if (!handle.IsInvalid && GetFileInformationByHandle(handle, out info) &&
                    (info.IndexHigh != 0 || info.IndexLow != 0))
                {
                    string id = string.Format("file:{0:X8}:{1:X8}{2:X8}", info.Volume, info.IndexHigh, info.IndexLow);
                    return info.Links > 1 && (info.Attributes & 0x10) == 0 ? id + ":" + name : id;
                }
            }
            return "path:" + name;
        }
    }
}
