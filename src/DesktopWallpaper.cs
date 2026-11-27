using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DeskFences
{
    /// <summary>
    /// Captures pixel-exact clean desktop wallpaper crops (zero icons, zero fences) for hiding
    /// stationary original member icons during fence drags WITHOUT ever moving real desktop icons
    /// to offscreen coordinates.
    /// Supports:
    /// 1. Live animated wallpapers hosted as child windows inside WorkerW (e.g. Wallpaper Engine).
    /// 2. Official Windows 8/10/11 IDesktopWallpaper COM API (multi-monitor per-monitor wallpapers,
    ///    slideshows, all 6 scaling modes: Center/Tile/Stretch/Fit/Fill/Span, and solid background).
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

        private sealed class MonitorWallpaperEntry
        {
            public Rectangle Bounds;
            public string Path;
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CachedImage> ImageCache =
            new Dictionary<string, CachedImage>(StringComparer.OrdinalIgnoreCase);

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
            }
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
            if (maskScreenRect.Width <= 0 || maskScreenRect.Height <= 0 || members == null || members.Count == 0)
                return null;

            Bitmap wallpaperCrop = null;
            if (solidFallbackColor.HasValue)
            {
                wallpaperCrop = new Bitmap(maskScreenRect.Width, maskScreenRect.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(wallpaperCrop))
                    g.Clear(Color.FromArgb(255, solidFallbackColor.Value));
            }
            else
            {
                wallpaperCrop = CaptureCleanWallpaperCrop(maskScreenRect, wallpaperWorkerHwnd);
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
                return mask;
            }
            finally
            {
                wallpaperCrop.Dispose();
            }
        }

        internal static Bitmap CaptureCleanWallpaperCrop(Rectangle screenRect, IntPtr wallpaperWorkerHwnd)
        {
            if (screenRect.Width <= 0 || screenRect.Height <= 0) return null;

            // 1. Live animated wallpaper host (e.g. Wallpaper Engine child window inside WorkerW)
            if (wallpaperWorkerHwnd != IntPtr.Zero)
            {
                Bitmap live = TryCaptureLiveWorkerChild(wallpaperWorkerHwnd, screenRect);
                if (live != null) return live;
            }

            // 2. Official Windows IDesktopWallpaper COM API (multi-monitor, slideshows, Fill/Fit/Stretch/Tile/Center/Span)
            Bitmap comCrop = TryRenderFromDesktopWallpaperCom(screenRect);
            if (comCrop != null) return comCrop;

            // 3. Registry fallback (TranscodedWallpaper / solid color)
            return TryRenderFromRegistryFallback(screenRect);
        }

        private static Bitmap TryCaptureLiveWorkerChild(IntPtr workerHwnd, Rectangle screenRect)
        {
            try
            {
                IntPtr child = NativeMethods.GetWindow(workerHwnd, NativeMethods.GW_CHILD);
                while (child != IntPtr.Zero)
                {
                    if (NativeMethods.IsWindowVisible(child))
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

        private static Bitmap TryRenderFromDesktopWallpaperCom(Rectangle screenRect)
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
                            DrawWallpaperIntoRegion(g, spanPath, spanBounds, screenRect, 4);
                    }
                    else
                    {
                        foreach (var e in active)
                        {
                            if (!e.Bounds.IntersectsWith(screenRect)) continue;
                            if (string.IsNullOrEmpty(e.Path)) continue;
                            DrawWallpaperIntoRegion(g, e.Path, e.Bounds, screenRect, pos);
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

        private static Bitmap TryRenderFromRegistryFallback(Rectangle screenRect)
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
                            DrawWallpaperIntoRegion(g, path, System.Windows.Forms.SystemInformation.VirtualScreen, screenRect, 4);
                        }
                        else
                        {
                            foreach (var scr in System.Windows.Forms.Screen.AllScreens)
                            {
                                if (scr.Bounds.IntersectsWith(screenRect))
                                    DrawWallpaperIntoRegion(g, path, scr.Bounds, screenRect, pos);
                            }
                        }
                    }
                }
                return bmp;
            }
            catch { return null; }
        }

        private static void DrawWallpaperIntoRegion(Graphics g, string path, Rectangle monitorRect, Rectangle cropScreenRect, int pos)
        {
            Bitmap img = LoadCachedImage(path);
            if (img == null) return;

            lock (CacheLock)
            {
                if (img.Width <= 0 || img.Height <= 0) return;

                Rectangle localClip = new Rectangle(
                    monitorRect.Left - cropScreenRect.Left,
                    monitorRect.Top - cropScreenRect.Top,
                    monitorRect.Width,
                    monitorRect.Height);

                var state = g.Save();
                try
                {
                    g.SetClip(localClip);
                    using (var attrs = new ImageAttributes())
                    {
                        attrs.SetWrapMode(WrapMode.TileFlipXY);
                        int imgW = img.Width;
                        int imgH = img.Height;
                        int monW = monitorRect.Width;
                        int monH = monitorRect.Height;

                        if (pos == 1) // DWPOS_TILE
                        {
                            for (int ty = 0; ty < monH; ty += imgH)
                            {
                                for (int tx = 0; tx < monW; tx += imgW)
                                {
                                    var dst = new Rectangle(localClip.Left + tx, localClip.Top + ty, imgW, imgH);
                                    if (dst.IntersectsWith(new Rectangle(0, 0, cropScreenRect.Width, cropScreenRect.Height)))
                                        g.DrawImage(img, dst, 0, 0, imgW, imgH, GraphicsUnit.Pixel, attrs);
                                }
                            }
                            return;
                        }

                        RectangleF drawRect;
                        if (pos == 0) // DWPOS_CENTER
                        {
                            drawRect = new RectangleF(
                                localClip.Left + (monW - imgW) * 0.5f,
                                localClip.Top + (monH - imgH) * 0.5f,
                                imgW, imgH);
                        }
                        else if (pos == 2) // DWPOS_STRETCH
                        {
                            drawRect = new RectangleF(localClip.Left, localClip.Top, monW, monH);
                        }
                        else if (pos == 3) // DWPOS_FIT
                        {
                            double scale = Math.Min((double)monW / imgW, (double)monH / imgH);
                            float dw = (float)(imgW * scale);
                            float dh = (float)(imgH * scale);
                            drawRect = new RectangleF(
                                localClip.Left + (monW - dw) * 0.5f,
                                localClip.Top + (monH - dh) * 0.5f,
                                dw, dh);
                        }
                        else // DWPOS_FILL (4) and default
                        {
                            double scale = Math.Max((double)monW / imgW, (double)monH / imgH);
                            float dw = (float)(imgW * scale);
                            float dh = (float)(imgH * scale);
                            drawRect = new RectangleF(
                                localClip.Left + (monW - dw) * 0.5f,
                                localClip.Top + (monH - dh) * 0.5f,
                                dw, dh);
                        }

                        var pts = new[]
                        {
                            new PointF(drawRect.Left, drawRect.Top),
                            new PointF(drawRect.Right, drawRect.Top),
                            new PointF(drawRect.Left, drawRect.Bottom)
                        };
                        g.DrawImage(img, pts, new RectangleF(0, 0, imgW, imgH), GraphicsUnit.Pixel, attrs);
                    }
                }
                finally
                {
                    g.Restore(state);
                }
            }
        }

        private static Bitmap LoadCachedImage(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;
                long writeTicks = File.GetLastWriteTimeUtc(path).Ticks;

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
