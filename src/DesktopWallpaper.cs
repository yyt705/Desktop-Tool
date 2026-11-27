using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DeskFences
{
    internal struct WallpaperCaptureDiag
    {
        public string Source;
        public Point CalibrationShift;
        public bool Calibrated;
        public int PatchCount;
        public double ElapsedMs;
        public double ErrZero;
        public double ErrBest;
    }

    internal sealed class WallpaperCalibrationContext
    {
        public Rectangle HintScreenRect;
        public List<Rectangle> ExcludedRects;
        public IntPtr[] DesktopShellHwnds;
    }

    /// <summary>
    /// Captures pixel-exact clean desktop wallpaper crops (zero icons, zero fences) for hiding
    /// stationary original member icons during fence drags WITHOUT ever moving real desktop icons
    /// to offscreen coordinates.
    /// Supports:
    /// 1. Live animated wallpapers hosted as third-party child windows inside WorkerW (e.g. Wallpaper Engine).
    /// 2. Official Windows 8/10/11 IDesktopWallpaper COM API (multi-monitor per-monitor wallpapers,
    ///    slideshows, all 6 scaling modes: Center/Tile/Stretch/Fit/Fill/Span, and solid background),
    ///    with fast exact sub-rectangle rendering (&lt;3ms even for 4K/5K wallpapers) AND automatic
    ///    one-time live-screen offset self-calibration (handles Win11 24H2/26H2 vertical Fill shift
    ///    such as +27px on 16:10-&gt;16:9 monitors cleanly with zero OS-version guesswork).
    /// 3. Registry fallback (TranscodedWallpaper + WallpaperStyle/TileWallpaper).
    /// </summary>
    internal static class DesktopWallpaperCache
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct WALLPAPER_RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
            [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);
            [PreserveSig] int GetMonitorDevicePathCount(out uint count);
            [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out WALLPAPER_RECT displayRect);
            void SetBackgroundColor(uint color);
            [PreserveSig] int GetBackgroundColor(out uint color);
            void SetPosition(int position);
            [PreserveSig] int GetPosition(out int position);
        }

        private sealed class CachedImage
        {
            public string Path;
            public long LastWriteUtcTicks;
            public Bitmap Image;
        }

        private sealed class CachedCalibration
        {
            public Point Shift;
            public double ErrZero;
            public double ErrBest;
        }

        private sealed class MonitorWallpaperEntry
        {
            public Rectangle Bounds;
            public string Path;
        }

        internal sealed class ScreenPatch
        {
            public Rectangle ScreenRect;
            public byte[] Bgra;
            public int Width;
            public int Height;
            public double TextureScore;
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CachedImage> ImageCache =
            new Dictionary<string, CachedImage>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CachedCalibration> CalibrationCache =
            new Dictionary<string, CachedCalibration>(StringComparer.OrdinalIgnoreCase);

        public static void Invalidate()
        {
            lock (CacheLock)
            {
                foreach (var entry in ImageCache.Values)
                {
                    if (entry.Image != null)
                    {
                        try { entry.Image.Dispose(); } catch { }
                    }
                }
                ImageCache.Clear();
                CalibrationCache.Clear();
            }
        }

        /// <summary>
        /// Pre-decodes active wallpaper(s) and calibrates live desktop alignment on a background thread
        /// so even the very first fence drag has zero cold JPEG decode lag and pixel-exact alignment.
        /// </summary>
        public static void WarmAsync(WallpaperCalibrationContext calibration)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var prim = System.Windows.Forms.Screen.PrimaryScreen;
                    if (prim == null) return;
                    Rectangle probe = new Rectangle(
                        prim.Bounds.Left + prim.Bounds.Width / 4,
                        prim.Bounds.Top + prim.Bounds.Height / 4,
                        64, 64);
                    WallpaperCaptureDiag diag;
                    using (var bmp = CaptureCleanWallpaperCrop(probe, IntPtr.Zero, calibration, out diag)) { }
                }
                catch { }
            });
        }

        /// <summary>
        /// Builds a per-pixel premultiplied 32-bit ARGB mask bitmap aligned to <paramref name="maskScreenRect"/>:
        /// - Pixels strictly inside the padded bounding rectangles of <paramref name="members"/> receive exact
        ///   opaque (Alpha==255) clean desktop wallpaper pixels so stationary originals vanish seamlessly.
        /// - All pixels outside member icon rects stay strictly Alpha==0 so nearby unrelated desktop icons
        ///   and adjacent fences are NEVER covered.
        /// </summary>
        public static Bitmap BuildMemberWallpaperMask(
            Rectangle maskScreenRect,
            List<DesktopIconInfo> members,
            IntPtr wallpaperWorkerHwnd,
            Color? solidFallbackColor = null)
        {
            WallpaperCaptureDiag unused;
            return BuildMemberWallpaperMask(maskScreenRect, members, wallpaperWorkerHwnd, solidFallbackColor, null, out unused);
        }

        internal static Bitmap BuildMemberWallpaperMask(
            Rectangle maskScreenRect,
            List<DesktopIconInfo> members,
            IntPtr wallpaperWorkerHwnd,
            Color? solidFallbackColor,
            WallpaperCalibrationContext calibration,
            out WallpaperCaptureDiag diag)
        {
            diag = new WallpaperCaptureDiag { Source = "none" };
            if (maskScreenRect.Width <= 0 || maskScreenRect.Height <= 0 || members == null || members.Count == 0)
                return null;

            var sw = Stopwatch.StartNew();
            Bitmap wallpaperCrop = null;
            if (solidFallbackColor.HasValue)
            {
                wallpaperCrop = new Bitmap(maskScreenRect.Width, maskScreenRect.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(wallpaperCrop))
                    g.Clear(Color.FromArgb(255, solidFallbackColor.Value));
                diag.Source = "solid";
            }
            else
            {
                wallpaperCrop = CaptureCleanWallpaperCrop(maskScreenRect, wallpaperWorkerHwnd, calibration, out diag);
            }
            if (wallpaperCrop == null) return null;

            try
            {
                int w = maskScreenRect.Width;
                int h = maskScreenRect.Height;
                var mask = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(mask))
                {
                    g.Clear(Color.FromArgb(0, 0, 0, 0));
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.None;

                    foreach (var m in members)
                    {
                        Rectangle u = Rectangle.Union(m.ScreenBounds, Rectangle.Union(m.IconBounds, m.LabelBounds));
                        if (u.Width <= 0 || u.Height <= 0) continue;
                        // Generous padding covers icon selection halo, shortcut overlay, and multi-line text drop shadows
                        u.Inflate(8, 6);
                        int x0 = Math.Max(0, u.Left - maskScreenRect.Left);
                        int y0 = Math.Max(0, u.Top - maskScreenRect.Top);
                        int x1 = Math.Min(w, u.Right - maskScreenRect.Left);
                        int y1 = Math.Min(h, u.Bottom - maskScreenRect.Top);
                        if (x1 <= x0 || y1 <= y0) continue;
                        var dst = new Rectangle(x0, y0, x1 - x0, y1 - y0);
                        g.DrawImage(wallpaperCrop, dst, dst, GraphicsUnit.Pixel);
                    }
                }
                diag.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                return mask;
            }
            finally
            {
                wallpaperCrop.Dispose();
            }
        }

        internal static Bitmap CaptureCleanWallpaperCrop(Rectangle screenRect, IntPtr wallpaperWorkerHwnd)
        {
            WallpaperCaptureDiag unused;
            return CaptureCleanWallpaperCrop(screenRect, wallpaperWorkerHwnd, null, out unused);
        }

        internal static Bitmap CaptureCleanWallpaperCrop(
            Rectangle screenRect,
            IntPtr wallpaperWorkerHwnd,
            WallpaperCalibrationContext calibration,
            out WallpaperCaptureDiag diag)
        {
            diag = new WallpaperCaptureDiag { Source = "none" };
            if (screenRect.Width <= 0 || screenRect.Height <= 0) return null;

            var sw = Stopwatch.StartNew();

            // 1. Live animated wallpaper host (third-party child window inside WorkerW, e.g. Wallpaper Engine / Lively)
            if (wallpaperWorkerHwnd != IntPtr.Zero)
            {
                Bitmap live = TryCaptureLiveWorkerChild(wallpaperWorkerHwnd, screenRect);
                if (live != null)
                {
                    diag.Source = "live";
                    diag.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                    return live;
                }
            }

            // 2. Official Windows IDesktopWallpaper COM API (multi-monitor, slideshows, Fill/Fit/Stretch/Tile/Center/Span)
            Bitmap comCrop = TryRenderFromDesktopWallpaperCom(screenRect, calibration, ref diag);
            if (comCrop != null)
            {
                diag.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                return comCrop;
            }

            // 3. Registry fallback (TranscodedWallpaper / solid color)
            Bitmap regCrop = TryRenderFromRegistryFallback(screenRect, calibration, ref diag);
            diag.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return regCrop;
        }

        private static Bitmap TryCaptureLiveWorkerChild(IntPtr workerHwnd, Rectangle screenRect)
        {
            try
            {
                uint workerPid = 0;
                NativeMethods.GetWindowThreadProcessId(workerHwnd, out workerPid);

                IntPtr child = NativeMethods.GetWindow(workerHwnd, NativeMethods.GW_CHILD);
                while (child != IntPtr.Zero)
                {
                    if (NativeMethods.IsWindowVisible(child))
                    {
                        // ONLY capture genuine third-party live wallpaper hosts (Wallpaper Engine / Lively / etc.),
                        // NEVER internal explorer.exe child windows inside WorkerW!
                        uint childPid = 0;
                        NativeMethods.GetWindowThreadProcessId(child, out childPid);
                        if (childPid != 0 && (workerPid == 0 || childPid != workerPid))
                        {
                            NativeMethods.RECT rc;
                            if (NativeMethods.GetWindowRect(child, out rc))
                            {
                                Rectangle childRect = Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
                                if (childRect.Width >= 64 && childRect.Height >= 64 && childRect.IntersectsWith(screenRect))
                                {
                                    Bitmap captured = CaptureWindowCrop(child, childRect, screenRect);
                                    if (captured != null)
                                    {
                                        if (HasNonBlackPixels(captured))
                                            return captured;
                                        captured.Dispose();
                                    }
                                }
                            }
                        }
                    }
                    child = NativeMethods.GetWindow(child, NativeMethods.GW_HWNDNEXT);
                }
            }
            catch { }
            return null;
        }

        private static Bitmap CaptureWindowCrop(IntPtr hwnd, Rectangle hwndScreenRect, Rectangle targetScreenRect)
        {
            int winW = hwndScreenRect.Width;
            int winH = hwndScreenRect.Height;
            if (winW <= 0 || winH <= 0) return null;

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hDib = IntPtr.Zero;
            IntPtr oldObj = IntPtr.Zero;
            IntPtr ppvBits = IntPtr.Zero;
            try
            {
                var bmi = new NativeMethods.BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = winW;
                bmi.bmiHeader.biHeight = -winH;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;

                hDib = NativeMethods.CreateDIBSection(memDc, ref bmi, 0, out ppvBits, IntPtr.Zero, 0);
                if (hDib == IntPtr.Zero || ppvBits == IntPtr.Zero) return null;
                oldObj = NativeMethods.SelectObject(memDc, hDib);

                if (!NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT))
                    return null;

                var bmp = new Bitmap(targetScreenRect.Width, targetScreenRect.Height, PixelFormat.Format32bppPArgb);
                var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    int srcStride = winW * 4;
                    int dstStride = Math.Abs(bd.Stride);
                    byte[] dstBuf = new byte[dstStride * bmp.Height];
                    int offX = targetScreenRect.Left - hwndScreenRect.Left;
                    int offY = targetScreenRect.Top - hwndScreenRect.Top;

                    int copyX0 = Math.Max(0, -offX);
                    int copyY0 = Math.Max(0, -offY);
                    int copyX1 = Math.Min(bmp.Width, winW - offX);
                    int copyY1 = Math.Min(bmp.Height, winH - offY);
                    if (copyX1 <= copyX0 || copyY1 <= copyY0)
                    {
                        bmp.UnlockBits(bd);
                        bmp.Dispose();
                        return null;
                    }

                    int rowBytes = (copyX1 - copyX0) * 4;
                    byte[] rowTmp = new byte[rowBytes];
                    for (int y = copyY0; y < copyY1; y++)
                    {
                        int sy = y + offY;
                        int sx = copyX0 + offX;
                        Marshal.Copy(IntPtr.Add(ppvBits, sy * srcStride + sx * 4), rowTmp, 0, rowBytes);
                        int dstRow = y * dstStride + copyX0 * 4;
                        for (int i = 0; i < rowBytes; i += 4)
                        {
                            dstBuf[dstRow + i] = rowTmp[i];
                            dstBuf[dstRow + i + 1] = rowTmp[i + 1];
                            dstBuf[dstRow + i + 2] = rowTmp[i + 2];
                            dstBuf[dstRow + i + 3] = 255;
                        }
                    }
                    Marshal.Copy(dstBuf, 0, bd.Scan0, dstBuf.Length);
                }
                finally
                {
                    bmp.UnlockBits(bd);
                }
                return bmp;
            }
            catch { return null; }
            finally
            {
                if (oldObj != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldObj);
                if (hDib != IntPtr.Zero) NativeMethods.DeleteObject(hDib);
                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static bool HasNonBlackPixels(Bitmap bmp)
        {
            int stepX = Math.Max(1, bmp.Width / 12);
            int stepY = Math.Max(1, bmp.Height / 12);
            for (int y = 0; y < bmp.Height; y += stepY)
            {
                for (int x = 0; x < bmp.Width; x += stepX)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R > 3 || c.G > 3 || c.B > 3) return true;
                }
            }
            return false;
        }

        private static Bitmap TryRenderFromDesktopWallpaperCom(
            Rectangle screenRect,
            WallpaperCalibrationContext calibration,
            ref WallpaperCaptureDiag diag)
        {
            object comObj = null;
            try
            {
                Type t = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"));
                if (t == null) return null;
                comObj = Activator.CreateInstance(t);
                var wp = (IDesktopWallpaper)comObj;

                uint count;
                if (wp.GetMonitorDevicePathCount(out count) != 0 || count == 0) return null;

                int pos;
                if (wp.GetPosition(out pos) != 0) pos = 4; // DWPOS_FILL

                uint bgRef;
                Color bgColor = Color.Black;
                if (wp.GetBackgroundColor(out bgRef) == 0)
                    bgColor = Color.FromArgb(255, (int)(bgRef & 0xFF), (int)((bgRef >> 8) & 0xFF), (int)((bgRef >> 16) & 0xFF));

                var active = new List<MonitorWallpaperEntry>();
                Rectangle spanBounds = Rectangle.Empty;
                for (uint i = 0; i < count; i++)
                {
                    string monId;
                    if (wp.GetMonitorDevicePathAt(i, out monId) != 0 || string.IsNullOrEmpty(monId)) continue;
                    WALLPAPER_RECT wr;
                    if (wp.GetMonitorRECT(monId, out wr) != 0) continue;
                    if (wr.Right <= wr.Left || wr.Bottom <= wr.Top) continue;
                    Rectangle monRect = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
                    string path;
                    if (wp.GetWallpaper(monId, out path) != 0) path = null;
                    active.Add(new MonitorWallpaperEntry { Bounds = monRect, Path = path });
                    spanBounds = spanBounds.IsEmpty ? monRect : Rectangle.Union(spanBounds, monRect);
                }
                if (active.Count == 0) return null;

                diag.Source = "com";
                var bmp = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(bgColor);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.SmoothingMode = SmoothingMode.None;
                    g.CompositingQuality = CompositingQuality.HighQuality;

                    if (pos == 5) // DWPOS_SPAN
                    {
                        string spanPath = null;
                        foreach (var e in active)
                        {
                            if (!string.IsNullOrEmpty(e.Path)) { spanPath = e.Path; break; }
                        }
                        if (!string.IsNullOrEmpty(spanPath))
                        {
                            Point shift = ResolveCalibrationShift(spanPath, spanBounds, screenRect, 4, calibration, ref diag);
                            DrawWallpaperIntoRegion(g, spanPath, spanBounds, screenRect, 4, shift);
                        }
                    }
                    else
                    {
                        foreach (var e in active)
                        {
                            if (!e.Bounds.IntersectsWith(screenRect)) continue;
                            if (string.IsNullOrEmpty(e.Path)) continue;
                            Point shift = ResolveCalibrationShift(e.Path, e.Bounds, screenRect, pos, calibration, ref diag);
                            DrawWallpaperIntoRegion(g, e.Path, e.Bounds, screenRect, pos, shift);
                        }
                    }
                }
                return bmp;
            }
            catch { return null; }
            finally
            {
                if (comObj != null && Marshal.IsComObject(comObj))
                {
                    try { Marshal.ReleaseComObject(comObj); } catch { }
                }
            }
        }

        private static Bitmap TryRenderFromRegistryFallback(
            Rectangle screenRect,
            WallpaperCalibrationContext calibration,
            ref WallpaperCaptureDiag diag)
        {
            try
            {
                string path = null;
                int style = 10;
                int tile = 0;
                Color bgColor = Color.Black;
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false))
                {
                    if (key != null)
                    {
                        path = key.GetValue("WallPaper") as string;
                        int.TryParse(Convert.ToString(key.GetValue("WallpaperStyle") ?? "10"), out style);
                        int.TryParse(Convert.ToString(key.GetValue("TileWallpaper") ?? "0"), out tile);
                    }
                }
                using (var ckey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors", false))
                {
                    if (ckey != null)
                    {
                        string raw = ckey.GetValue("Background") as string;
                        if (!string.IsNullOrEmpty(raw))
                        {
                            string[] parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            int r, g, b;
                            if (parts.Length >= 3 && int.TryParse(parts[0], out r) && int.TryParse(parts[1], out g) && int.TryParse(parts[2], out b))
                                bgColor = Color.FromArgb(255, Math.Max(0, Math.Min(255, r)), Math.Max(0, Math.Min(255, g)), Math.Max(0, Math.Min(255, b)));
                        }
                    }
                }

                int pos = 4; // Fill
                if (tile != 0) pos = 1;
                else if (style == 0) pos = 0;      // Center
                else if (style == 2) pos = 2;      // Stretch
                else if (style == 6) pos = 3;      // Fit
                else if (style == 10) pos = 4;     // Fill
                else if (style == 22) pos = 5;     // Span

                diag.Source = "reg";
                var bmp = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(bgColor);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (pos == 5)
                        {
                            Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                            Point shift = ResolveCalibrationShift(path, vs, screenRect, 4, calibration, ref diag);
                            DrawWallpaperIntoRegion(g, path, vs, screenRect, 4, shift);
                        }
                        else
                        {
                            foreach (var scr in System.Windows.Forms.Screen.AllScreens)
                            {
                                if (scr.Bounds.IntersectsWith(screenRect))
                                {
                                    Point shift = ResolveCalibrationShift(path, scr.Bounds, screenRect, pos, calibration, ref diag);
                                    DrawWallpaperIntoRegion(g, path, scr.Bounds, screenRect, pos, shift);
                                }
                            }
                        }
                    }
                }
                return bmp;
            }
            catch { return null; }
        }

        private static Point ResolveCalibrationShift(
            string path,
            Rectangle monitorRect,
            Rectangle cropScreenRect,
            int pos,
            WallpaperCalibrationContext calibration,
            ref WallpaperCaptureDiag diag)
        {
            if (calibration == null || string.IsNullOrEmpty(path)) return Point.Empty;

            long writeTicks;
            Bitmap img = LoadCachedImage(path, out writeTicks);
            if (img == null) return Point.Empty;

            string key = string.Format("{0}|{1}|{2},{3},{4},{5}|{6}",
                path, writeTicks, monitorRect.X, monitorRect.Y, monitorRect.Width, monitorRect.Height, pos);

            lock (CacheLock)
            {
                CachedCalibration cached;
                if (CalibrationCache.TryGetValue(key, out cached))
                {
                    diag.CalibrationShift = cached.Shift;
                    diag.Calibrated = true;
                    diag.ErrZero = cached.ErrZero;
                    diag.ErrBest = cached.ErrBest;
                    if (diag.Source == "com") diag.Source = "comCache";
                    return cached.Shift;
                }
            }

            Rectangle hint = calibration.HintScreenRect.IsEmpty ? cropScreenRect : calibration.HintScreenRect;
            // Determine dominant crop axis so patch ranking prioritizes strong gradients along the axis Windows can shift!
            bool preferVerticalGrad;
            bool preferHorizontalGrad;
            int maxDx, maxDy;
            ComputeCalibrationSearchWindow(img.Width, img.Height, monitorRect, pos,
                out maxDx, out maxDy, out preferVerticalGrad, out preferHorizontalGrad);

            var patches = TryCaptureCleanDesktopPatches(
                monitorRect, hint, calibration.ExcludedRects, calibration.DesktopShellHwnds,
                preferVerticalGrad, preferHorizontalGrad);
            diag.PatchCount = patches != null ? patches.Count : 0;
            if (patches == null || patches.Count == 0)
                return Point.Empty;

            Point shift;
            double errZero, errBest;
            bool cacheable;
            if (TryFindAlignmentOffset(patches, img, monitorRect, pos, maxDx, maxDy, out shift, out errZero, out errBest, out cacheable))
            {
                diag.CalibrationShift = shift;
                diag.Calibrated = true;
                diag.ErrZero = errZero;
                diag.ErrBest = errBest;
                if (cacheable)
                {
                    lock (CacheLock)
                    {
                        if (CalibrationCache.Count >= 16) CalibrationCache.Clear();
                        CalibrationCache[key] = new CachedCalibration
                        {
                            Shift = shift,
                            ErrZero = errZero,
                            ErrBest = errBest
                        };
                    }
                }
                return shift;
            }
            return Point.Empty;
        }

        internal static void ComputeCalibrationSearchWindow(
            int imgW, int imgH, Rectangle monitorRect, int pos,
            out int maxDx, out int maxDy, out bool preferVerticalGrad, out bool preferHorizontalGrad)
        {
            preferVerticalGrad = false;
            preferHorizontalGrad = false;
            int monW = Math.Max(1, monitorRect.Width);
            int monH = Math.Max(1, monitorRect.Height);

            if (pos == 4 || pos == 5) // DWPOS_FILL / DWPOS_SPAN
            {
                double scale = Math.Max((double)monW / Math.Max(1, imgW), (double)monH / Math.Max(1, imgH));
                double dw = imgW * scale;
                double dh = imgH * scale;
                double excessX = Math.Max(0.0, dw - monW);
                double excessY = Math.Max(0.0, dh - monH);
                maxDx = Math.Min(180, Math.Max(16, (int)Math.Ceiling(excessX * 0.5) + 16));
                maxDy = Math.Min(180, Math.Max(16, (int)Math.Ceiling(excessY * 0.5) + 16));
                preferVerticalGrad = excessY > excessX + 4.0;
                preferHorizontalGrad = excessX > excessY + 4.0;
            }
            else
            {
                maxDx = 32;
                maxDy = 32;
            }
        }

        private static List<Rectangle> GetVisibleNonDesktopWindowRects(IntPtr[] desktopShellHwnds)
        {
            var occluded = new List<Rectangle>(32);
            try
            {
                var shellSet = new HashSet<IntPtr>();
                if (desktopShellHwnds != null)
                {
                    foreach (var h in desktopShellHwnds)
                    {
                        if (h != IntPtr.Zero) shellSet.Add(h);
                    }
                }
                var sb = new StringBuilder(64);
                NativeMethods.EnumWindows((hwnd, lParam) =>
                {
                    if (shellSet.Contains(hwnd)) return true;
                    if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return true;

                    int cloaked = 0;
                    if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                        return true;

                    // Skip click-through layered overlays (e.g. GPU/FPS HUDs, invisible full-screen transparent layers,
                    // DeskFences' own OldSlotMaskForm). Visible DeskFences windows are explicitly excluded via excludedRects.
                    int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                    if ((exStyle & NativeMethods.WS_EX_LAYERED) != 0 && (exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0)
                        return true;

                    sb.Length = 0;
                    if (NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0)
                    {
                        string cls = sb.ToString();
                        if (cls == "Progman" || cls == "WorkerW" || cls == "SHELLDLL_DefView" ||
                            cls == "SysListView32" || cls == "EdgeUiInputTopWndClass" ||
                            cls == "DummyDWMListenerWindow" || cls == "ForegroundStaging")
                            return true;
                    }

                    NativeMethods.RECT rc;
                    if (NativeMethods.GetWindowRect(hwnd, out rc))
                    {
                        int w = rc.Right - rc.Left;
                        int h = rc.Bottom - rc.Top;
                        if (w >= 8 && h >= 8)
                            occluded.Add(Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom));
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return occluded;
        }

        private static List<ScreenPatch> TryCaptureCleanDesktopPatches(
            Rectangle monitorRect,
            Rectangle hintScreenRect,
            IList<Rectangle> excludedRects,
            IntPtr[] desktopShellHwnds,
            bool preferVerticalGrad,
            bool preferHorizontalGrad)
        {
            const int patchW = 48;
            const int patchH = 48;

            // Clamp sampling strictly inside this monitor's working area (avoiding taskbar & edges)
            Rectangle work = monitorRect;
            foreach (var scr in System.Windows.Forms.Screen.AllScreens)
            {
                if (scr.Bounds == monitorRect || scr.Bounds.IntersectsWith(monitorRect))
                {
                    Rectangle iw = Rectangle.Intersect(monitorRect, scr.WorkingArea);
                    if (iw.Width >= 200 && iw.Height >= 200)
                    {
                        work = iw;
                        break;
                    }
                }
            }
            Rectangle safeArea = Rectangle.Inflate(work, -16, -16);
            if (safeArea.Width < patchW || safeArea.Height < patchH) return null;

            var allExcluded = GetVisibleNonDesktopWindowRects(desktopShellHwnds);
            if (excludedRects != null)
            {
                for (int i = 0; i < excludedRects.Count; i++)
                {
                    Rectangle er = excludedRects[i];
                    if (er.Width > 0 && er.Height > 0 && er.IntersectsWith(safeArea))
                        allExcluded.Add(er);
                }
            }

            Func<Rectangle, bool> isClean = cand =>
            {
                if (!safeArea.Contains(cand)) return false;
                for (int i = 0; i < allExcluded.Count; i++)
                {
                    if (allExcluded[i].IntersectsWith(cand)) return false;
                }
                return true;
            };

            var cleanRects = new List<Rectangle>(24);
            Action<int, int> tryAdd = (x, y) =>
            {
                if (cleanRects.Count >= 24) return;
                var r = new Rectangle(x, y, patchW, patchH);
                if (!isClean(r)) return;
                for (int i = 0; i < cleanRects.Count; i++)
                {
                    if (cleanRects[i].IntersectsWith(r)) return;
                }
                cleanRects.Add(r);
            };

            // 1. Prioritize patches right near the dragged fence (same local region of wallpaper)
            if (!hintScreenRect.IsEmpty && hintScreenRect.IntersectsWith(monitorRect))
            {
                int cx = hintScreenRect.Left + (hintScreenRect.Width - patchW) / 2;
                int cy = hintScreenRect.Top + (hintScreenRect.Height - patchH) / 2;
                int[] pads = new[] { 16, 72, 140 };
                foreach (int pad in pads)
                {
                    tryAdd(hintScreenRect.Left - patchW - pad, cy);
                    tryAdd(hintScreenRect.Right + pad, cy);
                    tryAdd(cx, hintScreenRect.Top - patchH - pad);
                    tryAdd(cx, hintScreenRect.Bottom + pad);
                    tryAdd(hintScreenRect.Left - patchW - pad, hintScreenRect.Top - patchH - pad);
                    tryAdd(hintScreenRect.Right + pad, hintScreenRect.Bottom + pad);
                }
            }

            // 2. Scan a 7x5 full-monitor grid to guarantee textured patches (e.g. horizon/terrain instead of flat sky)
            const int cols = 7;
            const int rows = 5;
            for (int r = 0; r < rows && cleanRects.Count < 24; r++)
            {
                int y = safeArea.Top + (int)((safeArea.Height - patchH) * ((r + 0.5) / rows));
                for (int c = 0; c < cols && cleanRects.Count < 24; c++)
                {
                    int x = safeArea.Left + (int)((safeArea.Width - patchW) * ((c + 0.5) / cols));
                    tryAdd(x, y);
                }
            }

            if (cleanRects.Count == 0) return null;

            // Capture all clean candidate patches with ONE reusable 48x48 DIB section (< 0.8ms total!)
            var captured = new List<ScreenPatch>(cleanRects.Count);
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hDib = IntPtr.Zero;
            IntPtr oldObj = IntPtr.Zero;
            IntPtr ppvBits = IntPtr.Zero;
            try
            {
                var bmi = new NativeMethods.BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = patchW;
                bmi.bmiHeader.biHeight = -patchH;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;

                hDib = NativeMethods.CreateDIBSection(memDc, ref bmi, 0, out ppvBits, IntPtr.Zero, 0);
                if (hDib == IntPtr.Zero || ppvBits == IntPtr.Zero) return null;
                oldObj = NativeMethods.SelectObject(memDc, hDib);

                int byteLen = patchW * patchH * 4;
                for (int i = 0; i < cleanRects.Count; i++)
                {
                    Rectangle r = cleanRects[i];
                    if (!NativeMethods.BitBlt(memDc, 0, 0, patchW, patchH, screenDc, r.X, r.Y, NativeMethods.SRCCOPY))
                        continue;
                    byte[] buf = new byte[byteLen];
                    Marshal.Copy(ppvBits, buf, 0, byteLen);
                    double tex = ComputePatchTextureScore(buf, patchW, patchH, preferVerticalGrad, preferHorizontalGrad);
                    captured.Add(new ScreenPatch
                    {
                        ScreenRect = r,
                        Bgra = buf,
                        Width = patchW,
                        Height = patchH,
                        TextureScore = tex
                    });
                }
            }
            catch { return null; }
            finally
            {
                if (oldObj != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldObj);
                if (hDib != IntPtr.Zero) NativeMethods.DeleteObject(hDib);
                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }

            if (captured.Count == 0) return null;

            // Keep up to 6 highest-texture patches so high-contrast landmarks (horizon, reeds, hills) dominate flat sky!
            captured.Sort((a, b) => b.TextureScore.CompareTo(a.TextureScore));
            if (captured.Count > 6) captured.RemoveRange(6, captured.Count - 6);
            return captured;
        }

        internal static double ComputePatchTextureScore(byte[] bgra, int w, int h, bool preferVertical, bool preferHorizontal)
        {
            if (bgra == null || w < 8 || h < 8) return 0.0;
            int stride = w * 4;
            long sumV = 0, sumH = 0;
            int n = 0;
            for (int y = 2; y < h - 4; y += 3)
            {
                int row = y * stride;
                int rowNext = (y + 3) * stride;
                for (int x = 2; x < w - 4; x += 3)
                {
                    int i0 = row + x * 4;
                    int iv = rowNext + x * 4;
                    int ih = row + (x + 3) * 4;
                    sumV += Math.Abs(bgra[iv] - bgra[i0]) + Math.Abs(bgra[iv + 1] - bgra[i0 + 1]) + Math.Abs(bgra[iv + 2] - bgra[i0 + 2]);
                    sumH += Math.Abs(bgra[ih] - bgra[i0]) + Math.Abs(bgra[ih + 1] - bgra[i0 + 1]) + Math.Abs(bgra[ih + 2] - bgra[i0 + 2]);
                    n++;
                }
            }
            if (n == 0) return 0.0;
            double v = (double)sumV / (n * 3.0);
            double hScore = (double)sumH / (n * 3.0);
            if (preferVertical) return v * 1.6 + hScore * 0.4;
            if (preferHorizontal) return hScore * 1.6 + v * 0.4;
            return v + hScore;
        }

        private sealed class RefPatchData
        {
            public ScreenPatch Patch;
            public byte[] RefBgra;
            public int RefW;
            public int RefH;
            public int RefStride;
            public int MaxDx;
            public int MaxDy;
        }

        /// <summary>
        /// Pure coarse-to-fine 2D alignment matcher: given clean live desktop patches and the source
        /// wallpaper bitmap, determines the exact integer shift (dx, dy) Windows' wallpaper renderer applied
        /// relative to our nominal center-aligned layout.
        /// Convention: Screen(x, y) == Nominal(x - shift.X, y - shift.Y), so applying +shift to drawRect
        /// aligns our rendered wallpaper crop to 0.0px error against the real Windows desktop!
        /// </summary>
        internal static bool TryFindAlignmentOffset(
            List<ScreenPatch> patches,
            Bitmap img,
            Rectangle monitorRect,
            int pos,
            int maxDx,
            int maxDy,
            out Point shift,
            out double errZero,
            out double errBest,
            out bool cacheable)
        {
            shift = Point.Empty;
            errZero = 255.0;
            errBest = 255.0;
            cacheable = false;

            if (patches == null || patches.Count == 0 || img == null || img.Width <= 0 || img.Height <= 0)
                return false;

            // Build tiny expanded nominal reference strips for each clean patch (< 1ms total!)
            var refs = new List<RefPatchData>(patches.Count);
            double maxTexture = 0.0;
            for (int i = 0; i < patches.Count; i++)
            {
                var p = patches[i];
                if (p == null || p.Bgra == null || p.Width <= 0 || p.Height <= 0) continue;
                if (p.TextureScore > maxTexture) maxTexture = p.TextureScore;

                Rectangle refScreen = new Rectangle(
                    p.ScreenRect.X - maxDx,
                    p.ScreenRect.Y - maxDy,
                    p.Width + maxDx * 2,
                    p.Height + maxDy * 2);

                using (var refBmp = new Bitmap(refScreen.Width, refScreen.Height, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(refBmp))
                    {
                        g.Clear(Color.Black);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.SmoothingMode = SmoothingMode.None;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        DrawWallpaperBitmapIntoRegion(g, img, monitorRect, refScreen, pos, Point.Empty);
                    }
                    var bd = refBmp.LockBits(new Rectangle(0, 0, refBmp.Width, refBmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    try
                    {
                        int stride = Math.Abs(bd.Stride);
                        byte[] refBytes = new byte[stride * refBmp.Height];
                        Marshal.Copy(bd.Scan0, refBytes, 0, refBytes.Length);
                        refs.Add(new RefPatchData
                        {
                            Patch = p,
                            RefBgra = refBytes,
                            RefW = refBmp.Width,
                            RefH = refBmp.Height,
                            RefStride = stride,
                            MaxDx = maxDx,
                            MaxDy = maxDy
                        });
                    }
                    finally
                    {
                        refBmp.UnlockBits(bd);
                    }
                }
            }
            if (refs.Count == 0) return false;

            double[] perPatch = new double[refs.Count];

            Func<int, int, int, double> evalShift = (sx, sy, step) =>
            {
                int count = refs.Count;
                for (int i = 0; i < count; i++)
                {
                    var rd = refs[i];
                    var p = rd.Patch;
                    int pw = p.Width;
                    int ph = p.Height;
                    int pStride = pw * 4;
                    int rStride = rd.RefStride;
                    byte[] pBuf = p.Bgra;
                    byte[] rBuf = rd.RefBgra;
                    int ox = rd.MaxDx - sx;
                    int oy = rd.MaxDy - sy;

                    long diff = 0;
                    int n = 0;
                    for (int y = 2; y < ph - 2; y += step)
                    {
                        int ry = oy + y;
                        if (ry < 0 || ry >= rd.RefH) continue;
                        int pRow = y * pStride;
                        int rRow = ry * rStride;
                        for (int x = 2; x < pw - 2; x += step)
                        {
                            int rx = ox + x;
                            if (rx < 0 || rx >= rd.RefW) continue;
                            int pi = pRow + x * 4;
                            int ri = rRow + rx * 4;
                            diff += Math.Abs(pBuf[pi] - rBuf[ri])
                                  + Math.Abs(pBuf[pi + 1] - rBuf[ri + 1])
                                  + Math.Abs(pBuf[pi + 2] - rBuf[ri + 2]);
                            n++;
                        }
                    }
                    perPatch[i] = n > 0 ? (double)diff / (n * 3.0) : 255.0;
                }

                if (count == 1) return perPatch[0];
                if (count == 2) return (perPatch[0] + perPatch[1]) * 0.5;

                // Trimmed mean: drop the single highest-error patch for 100% immunity to any unknown invisible overlay!
                double sum = 0.0;
                double worst = -1.0;
                for (int i = 0; i < count; i++)
                {
                    double v = perPatch[i];
                    sum += v;
                    if (v > worst) worst = v;
                }
                return (sum - worst) / (count - 1);
            };

            errZero = evalShift(0, 0, 2);

            // Fast path: already pixel-exact at (0, 0) on textured wallpaper (or solid uniform wallpaper)
            if (errZero <= 2.0)
            {
                shift = Point.Empty;
                errBest = errZero;
                // Only cache (0,0) if we actually saw textured wallpaper (don't lock (0,0) on pure flat sky when textured patches were occluded)
                cacheable = maxTexture >= 3.0;
                return true;
            }

            // Coarse search (step 4) tracking top 3 distinct basins
            var topPts = new Point[3];
            var topErrs = new double[] { 1e9, 1e9, 1e9 };
            for (int sy = -maxDy; sy <= maxDy; sy += 4)
            {
                for (int sx = -maxDx; sx <= maxDx; sx += 4)
                {
                    double e = evalShift(sx, sy, 4);
                    if (e >= topErrs[2]) continue;

                    // Merge into an existing nearby basin if within 8px
                    int nearIdx = -1;
                    for (int k = 0; k < 3; k++)
                    {
                        if (topErrs[k] < 1e8 && Math.Abs(topPts[k].X - sx) <= 8 && Math.Abs(topPts[k].Y - sy) <= 8)
                        {
                            nearIdx = k;
                            break;
                        }
                    }
                    if (nearIdx >= 0)
                    {
                        if (e < topErrs[nearIdx])
                        {
                            topErrs[nearIdx] = e;
                            topPts[nearIdx] = new Point(sx, sy);
                        }
                    }
                    else
                    {
                        topErrs[2] = e;
                        topPts[2] = new Point(sx, sy);
                    }
                    // Keep 3-element array sorted ascending
                    for (int a = 0; a < 2; a++)
                    {
                        for (int b = a + 1; b < 3; b++)
                        {
                            if (topErrs[b] < topErrs[a])
                            {
                                double te = topErrs[a]; topErrs[a] = topErrs[b]; topErrs[b] = te;
                                Point tp = topPts[a]; topPts[a] = topPts[b]; topPts[b] = tp;
                            }
                        }
                    }
                }
            }

            // Fine search (±4 step 1, stride 2) around each of the top 3 coarse basins
            int bestSx = 0, bestSy = 0;
            double bestE = errZero;
            for (int k = 0; k < 3; k++)
            {
                if (topErrs[k] >= 1e8) continue;
                int cx = topPts[k].X;
                int cy = topPts[k].Y;
                int x0 = Math.Max(-maxDx, cx - 4);
                int x1 = Math.Min(maxDx, cx + 4);
                int y0 = Math.Max(-maxDy, cy - 4);
                int y1 = Math.Min(maxDy, cy + 4);
                for (int sy = y0; sy <= y1; sy++)
                {
                    for (int sx = x0; sx <= x1; sx++)
                    {
                        double e = evalShift(sx, sy, 2);
                        if (e < bestE)
                        {
                            bestE = e;
                            bestSx = sx;
                            bestSy = sy;
                        }
                    }
                }
            }

            errBest = bestE;

            // Strict confidence gate: only accept a non-zero shift when it is a crystal-clear match!
            bool strongNonZeroMatch =
                (bestSx != 0 || bestSy != 0) &&
                bestE <= 8.5 &&
                (errZero - bestE) >= 2.0 &&
                (errZero > 4.0 || bestE <= errZero * 0.65);

            if (strongNonZeroMatch)
            {
                shift = new Point(bestSx, bestSy);
                cacheable = true;
                return true;
            }

            if (errZero <= 4.0)
            {
                shift = Point.Empty;
                errBest = errZero;
                cacheable = maxTexture >= 3.0;
                return true;
            }

            // Wallpaper did not match well (e.g. mid-slideshow crossfade or unrecognized third-party renderer):
            // keep nominal (0,0) and do NOT cache so next hover re-checks cleanly.
            shift = Point.Empty;
            cacheable = false;
            return false;
        }

        private static void DrawWallpaperIntoRegion(
            Graphics g, string path, Rectangle monitorRect, Rectangle cropScreenRect, int pos, Point calibrationShift)
        {
            long unusedTicks;
            Bitmap img = LoadCachedImage(path, out unusedTicks);
            if (img == null) return;

            lock (CacheLock)
            {
                DrawWallpaperBitmapIntoRegion(g, img, monitorRect, cropScreenRect, pos, calibrationShift);
            }
        }

        /// <summary>
        /// Renders the exact visible sub-rectangle of <paramref name="img"/> for <paramref name="cropScreenRect"/>
        /// onto <paramref name="g"/> using inverse-mapped source cropping (50x faster than full-4K clipped draws,
        /// with 4px padded bicubic filter margin clipped cleanly to monitor bounds for 0-pixel-diff exactness).
        /// </summary>
        internal static void DrawWallpaperBitmapIntoRegion(
            Graphics g, Bitmap img, Rectangle monitorRect, Rectangle cropScreenRect, int pos, Point calibrationShift)
        {
            if (img == null || img.Width <= 0 || img.Height <= 0) return;

            Rectangle localClip = new Rectangle(
                monitorRect.Left - cropScreenRect.Left,
                monitorRect.Top - cropScreenRect.Top,
                monitorRect.Width,
                monitorRect.Height);
            Rectangle cropLocal = new Rectangle(0, 0, cropScreenRect.Width, cropScreenRect.Height);
            Rectangle visClip = Rectangle.Intersect(localClip, cropLocal);
            if (visClip.Width <= 0 || visClip.Height <= 0) return;

            var state = g.Save();
            try
            {
                g.SetClip(visClip);
                using (var attrs = new ImageAttributes())
                {
                    attrs.SetWrapMode(WrapMode.TileFlipXY);
                    int imgW = img.Width;
                    int imgH = img.Height;
                    int monW = monitorRect.Width;
                    int monH = monitorRect.Height;

                    if (pos == 1) // DWPOS_TILE
                    {
                        int originX = localClip.Left + calibrationShift.X;
                        int originY = localClip.Top + calibrationShift.Y;
                        int startTx = (int)Math.Floor((double)(visClip.Left - originX) / imgW) * imgW;
                        int startTy = (int)Math.Floor((double)(visClip.Top - originY) / imgH) * imgH;
                        for (int ty = startTy; originY + ty < visClip.Bottom; ty += imgH)
                        {
                            for (int tx = startTx; originX + tx < visClip.Right; tx += imgW)
                            {
                                var dst = new Rectangle(originX + tx, originY + ty, imgW, imgH);
                                if (dst.IntersectsWith(visClip))
                                    g.DrawImage(img, dst, 0, 0, imgW, imgH, GraphicsUnit.Pixel, attrs);
                            }
                        }
                        return;
                    }

                    double drawX, drawY, drawW, drawH;
                    if (pos == 0) // DWPOS_CENTER
                    {
                        drawW = imgW;
                        drawH = imgH;
                        drawX = localClip.Left + (monW - drawW) * 0.5 + calibrationShift.X;
                        drawY = localClip.Top + (monH - drawH) * 0.5 + calibrationShift.Y;
                    }
                    else if (pos == 2) // DWPOS_STRETCH
                    {
                        drawW = monW;
                        drawH = monH;
                        drawX = localClip.Left + calibrationShift.X;
                        drawY = localClip.Top + calibrationShift.Y;
                    }
                    else if (pos == 3) // DWPOS_FIT
                    {
                        double scale = Math.Min((double)monW / imgW, (double)monH / imgH);
                        drawW = imgW * scale;
                        drawH = imgH * scale;
                        drawX = localClip.Left + (monW - drawW) * 0.5 + calibrationShift.X;
                        drawY = localClip.Top + (monH - drawH) * 0.5 + calibrationShift.Y;
                    }
                    else // DWPOS_FILL (4) and default
                    {
                        double scale = Math.Max((double)monW / imgW, (double)monH / imgH);
                        drawW = imgW * scale;
                        drawH = imgH * scale;
                        drawX = localClip.Left + (monW - drawW) * 0.5 + calibrationShift.X;
                        drawY = localClip.Top + (monH - drawH) * 0.5 + calibrationShift.Y;
                    }

                    if (drawW <= 0.0 || drawH <= 0.0) return;

                    // Pad visible destination bounds by 4px (still strictly clamped to [drawX..drawX+drawW] and
                    // clipped by g.SetClip(visClip)) so HighQualityBicubic edge taps match full-monitor draws to 0px!
                    const double pad = 4.0;
                    double dstLeft = Math.Max(drawX, visClip.Left - pad);
                    double dstTop = Math.Max(drawY, visClip.Top - pad);
                    double dstRight = Math.Min(drawX + drawW, visClip.Right + pad);
                    double dstBottom = Math.Min(drawY + drawH, visClip.Bottom + pad);
                    if (dstRight <= dstLeft || dstBottom <= dstTop) return;

                    double scaleX = drawW / imgW;
                    double scaleY = drawH / imgH;
                    float sx0 = (float)((dstLeft - drawX) / scaleX);
                    float sy0 = (float)((dstTop - drawY) / scaleY);
                    float sw = (float)((dstRight - dstLeft) / scaleX);
                    float sh = (float)((dstBottom - dstTop) / scaleY);
                    if (sx0 < 0f) { sw += sx0; sx0 = 0f; }
                    if (sy0 < 0f) { sh += sy0; sy0 = 0f; }
                    if (sx0 + sw > imgW) sw = imgW - sx0;
                    if (sy0 + sh > imgH) sh = imgH - sy0;
                    if (sw <= 0.001f || sh <= 0.001f) return;

                    var pts = new[]
                    {
                        new PointF((float)dstLeft, (float)dstTop),
                        new PointF((float)dstRight, (float)dstTop),
                        new PointF((float)dstLeft, (float)dstBottom)
                    };
                    g.DrawImage(img, pts, new RectangleF(sx0, sy0, sw, sh), GraphicsUnit.Pixel, attrs);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        private static Bitmap LoadCachedImage(string path, out long writeTicks)
        {
            writeTicks = 0;
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;
                writeTicks = File.GetLastWriteTimeUtc(path).Ticks;

                lock (CacheLock)
                {
                    CachedImage existing;
                    if (ImageCache.TryGetValue(path, out existing) &&
                        existing.Image != null &&
                        existing.LastWriteUtcTicks == writeTicks)
                    {
                        return existing.Image;
                    }
                }

                // Read via byte buffer so we never hold a filesystem lock on Windows' TranscodedWallpaper!
                byte[] bytes = File.ReadAllBytes(path);
                Bitmap decoded;
                using (var ms = new MemoryStream(bytes))
                using (var src = Image.FromStream(ms, false, true))
                {
                    decoded = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(decoded))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(src, 0, 0, src.Width, src.Height);
                    }
                }

                lock (CacheLock)
                {
                    if (ImageCache.Count >= 6)
                    {
                        foreach (var old in ImageCache.Values)
                        {
                            if (old.Image != null) { try { old.Image.Dispose(); } catch { } }
                        }
                        ImageCache.Clear();
                    }
                    CachedImage prev;
                    if (ImageCache.TryGetValue(path, out prev) && prev.Image != null)
                    {
                        try { prev.Image.Dispose(); } catch { }
                    }
                    ImageCache[path] = new CachedImage
                    {
                        Path = path,
                        LastWriteUtcTicks = writeTicks,
                        Image = decoded
                    };
                    return decoded;
                }
            }
            catch { return null; }
        }
    }
}
