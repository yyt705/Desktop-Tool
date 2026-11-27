using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeskFences
{
    // Real SysListView32 painting, item geometry, selection and genuine PIDLs for temporary files.
    // Only the IFolderView that receives the application's position requests is substituted.
    internal sealed class PrivateDesktopView : ListView, DesktopShellView.IFolderView
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(IntPtr desktop);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr pidl, uint format, out IntPtr name);

        internal readonly MembershipTests.Folder Folder;
        internal int SnapChanges;
        internal readonly List<KeyValuePair<int, Point>> DirectPositions = new List<KeyValuePair<int, Point>>();
        private bool _insideShellPosition;
        private readonly ImageList artwork;

        internal static void OnPrivateDesktop(Action run)
        {
            // Omit DESKTOP_SWITCHDESKTOP. All test windows and capture messages belong to this separate desktop.
            IntPtr desktop = CreateDesktop("DeskFencesTests-" + Guid.NewGuid().ToString("N"),
                IntPtr.Zero, IntPtr.Zero, 0, 0xcf, IntPtr.Zero);
            if (desktop == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            ExceptionDispatchInfo failure = null;
            try
            {
                var driver = new Thread(delegate() {
                    try
                    {
                        if (!SetThreadDesktop(desktop)) throw new System.ComponentModel.Win32Exception();
                        Application.EnableVisualStyles();
                        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
                        run();
                    }
                    catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
                });
                driver.SetApartmentState(ApartmentState.STA);
                driver.Start();
                // Pump the owning STA while WinForms and Shell release cross-thread objects at thread exit.
                while (!driver.Join(20)) Application.DoEvents();
                if (failure != null) failure.Throw();
            }
            finally { CloseDesktop(desktop); }
        }

        internal PrivateDesktopView(string[] paths)
        {
            Folder = new MembershipTests.Folder { Files = (string[])paths.Clone() };
            View = View.LargeIcon;
            Dock = DockStyle.Fill;
            BorderStyle = BorderStyle.None;
            AutoArrange = false;
            HideSelection = false;
            Scrollable = false;
            artwork = new ImageList { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
            LargeImageList = artwork;
            Color[] colors = { Color.Tomato, Color.Gold, Color.DeepSkyBlue, Color.LimeGreen, Color.MediumOrchid };
            foreach (string path in paths)
            {
                using (var bitmap = new Bitmap(24, 24))
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(colors[Items.Count % colors.Length]);
                    g.FillRectangle(Brushes.Black, 4, 4, 4, 9);
                    artwork.Images.Add(bitmap);
                    // ImageList materializes its backing HIMAGELIST lazily.
                    GC.KeepAlive(artwork.Handle);
                }
                Items.Add(Path.GetFileNameWithoutExtension(path), Items.Count);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && artwork != null) artwork.Dispose();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.LVM_SETEXTENDEDLISTVIEWSTYLE &&
                (message.WParam.ToInt64() & NativeMethods.LVS_EX_SNAPTOGRID) != 0) SnapChanges++;
            base.WndProc(ref message);
            if (message.Msg == NativeMethods.LVM_SETITEMPOSITION32 && !_insideShellPosition)
            {
                int idx = message.WParam.ToInt32();
                if (idx >= 0 && idx < Items.Count)
                    DirectPositions.Add(new KeyValuePair<int, Point>(idx, Items[idx].Position));
            }
        }

        internal void SetWorkArea(Rectangle rect)
        {
            SetWorkAreas(new[] { rect });
        }

        internal void SetWorkAreas(Rectangle[] rects)
        {
            if (rects == null || rects.Length == 0) return;
            int sz = Marshal.SizeOf(typeof(NativeMethods.RECT));
            IntPtr ptr = Marshal.AllocHGlobal(sz * rects.Length);
            try
            {
                for (int i = 0; i < rects.Length; i++)
                {
                    var wa = new NativeMethods.RECT { Left = rects[i].Left, Top = rects[i].Top, Right = rects[i].Right, Bottom = rects[i].Bottom };
                    Marshal.StructureToPtr(wa, IntPtr.Add(ptr, i * sz), false);
                }
                NativeMethods.SendMessage(Handle, 0x1041, new IntPtr(rects.Length), ptr); // LVM_SETWORKAREAS
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        internal void SetSnap(bool enabled)
        {
            IntPtr snap = new IntPtr(NativeMethods.LVS_EX_SNAPTOGRID);
            NativeMethods.SendMessage(Handle, NativeMethods.LVM_SETEXTENDEDLISTVIEWSTYLE, snap, enabled ? snap : IntPtr.Zero);
        }

        public int Item(int index, out IntPtr pidl) { return Folder.Item(index, out pidl); }
        public int ItemCount(uint flags, out int count) { return Folder.ItemCount(flags, out count); }

        public int GetItemPosition(IntPtr pidl, out NativeMethods.POINT point)
        {
            IntPtr name;
            Marshal.ThrowExceptionForHR(SHGetNameFromIDList(pidl, 0x80028000, out name));
            try
            {
                Point p = Items[Array.IndexOf(Folder.Files, Marshal.PtrToStringUni(name))].Position;
                point = new NativeMethods.POINT { X = p.X, Y = p.Y };
                return 0;
            }
            finally { Marshal.FreeCoTaskMem(name); }
        }

        public int SelectAndPositionItems(uint count, IntPtr[] pidls, NativeMethods.POINT[] points, uint flags)
        {
            int first = Folder.Positioned.Count;
            int result = Folder.SelectAndPositionItems(count, pidls, points, flags);
            _insideShellPosition = true;
            try
            {
                for (int i = first; i < Folder.Positioned.Count; ++i)
                    Items[Array.IndexOf(Folder.Files, Folder.Positioned[i])].Position = Folder.Targets[i];
            }
            finally { _insideShellPosition = false; }
            return result;
        }

        public void GetCurrentViewMode() { throw new NotSupportedException(); }
        public void SetCurrentViewMode() { throw new NotSupportedException(); }
        public void GetFolder() { throw new NotSupportedException(); }
        void DesktopShellView.IFolderView.Items() { throw new NotSupportedException(); }
        public void GetSelectionMarkedItem() { throw new NotSupportedException(); }
        public void GetFocusedItem() { throw new NotSupportedException(); }
        public void GetSpacing() { throw new NotSupportedException(); }
        public void GetDefaultSpacing() { throw new NotSupportedException(); }
        public void GetAutoArrange() { throw new NotSupportedException(); }
        public void SelectItem() { throw new NotSupportedException(); }
    }
}
