using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskFences
{
    /// <summary>
    /// Authentic Windows 11 WinUI 3 MenuFlyout renderer with Segoe Fluent Icons,
    /// generous item spacing, rounded hover pills, and DWM rounded corners on root & submenus.
    /// </summary>
    internal class Win11MenuRenderer : ToolStripProfessionalRenderer, IDisposable
    {
        private readonly bool _isLight;
        private readonly Color _accent;
        private readonly float _dpiScale;
        private Font _glyphFont;

        public Win11MenuRenderer(bool isLight, Color accent, float dpiScale = 1f) : base(new Win11MenuColorTable(isLight))
        {
            _isLight = isLight;
            _accent = accent;
            _dpiScale = Math.Max(1f, dpiScale);
            _glyphFont = new Font("Segoe Fluent Icons", 13f * _dpiScale, FontStyle.Regular, GraphicsUnit.Pixel);
            RoundedEdges = false;
        }

        public void Dispose()
        {
            if (_glyphFont != null)
            {
                _glyphFont.Dispose();
                _glyphFont = null;
            }
        }

        public static ContextMenuStrip CreateWin11Menu(bool isLight, Color accent, float dpiScale)
        {
            var renderer = new Win11MenuRenderer(isLight, accent, dpiScale);
            var font = new Font("Microsoft YaHei UI", 13f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel);
            var menu = new ContextMenuStrip
            {
                Renderer = renderer,
                Font = font,
                ShowImageMargin = true,
                ShowCheckMargin = false,
                Padding = new Padding((int)(4 * dpiScale), (int)(5 * dpiScale), (int)(4 * dpiScale), (int)(5 * dpiScale)),
                DropShadowEnabled = true,
                ImageScalingSize = new Size((int)(16 * dpiScale), (int)(16 * dpiScale))
            };
            menu.HandleCreated += (s, e) => NativeMethods.ApplyWin11RoundedCorners(menu.Handle);
            menu.Disposed += (s, e) =>
            {
                font.Dispose();
                renderer.Dispose();
            };
            return menu;
        }

        public static ToolStripMenuItem CreateMenuItem(string text, string fluentGlyph, float dpiScale, EventHandler onClick = null)
        {
            var item = new ToolStripMenuItem(text, null, onClick)
            {
                Tag = fluentGlyph,
                Padding = new Padding(0, (int)(5 * dpiScale), (int)(8 * dpiScale), (int)(5 * dpiScale))
            };
            return item;
        }

        public static void StyleSubMenu(ToolStripMenuItem parent, bool isLight, Color accent, float dpiScale)
        {
            var dd = parent.DropDown as ToolStripDropDownMenu;
            if (dd != null)
            {
                var renderer = new Win11MenuRenderer(isLight, accent, dpiScale);
                var font = new Font("Microsoft YaHei UI", 13f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel);
                dd.Renderer = renderer;
                dd.Font = font;
                dd.ShowImageMargin = true;
                dd.ShowCheckMargin = false;
                dd.Padding = new Padding((int)(4 * dpiScale), (int)(5 * dpiScale), (int)(4 * dpiScale), (int)(5 * dpiScale));
                dd.DropShadowEnabled = true;
                dd.HandleCreated += (s, e) => NativeMethods.ApplyWin11RoundedCorners(dd.Handle);
                dd.Disposed += (s, e) =>
                {
                    font.Dispose();
                    renderer.Dispose();
                };
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Color bg = _isLight ? Color.FromArgb(249, 249, 250) : Color.FromArgb(44, 44, 46);
            using (Brush b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Color border = _isLight ? Color.FromArgb(222, 224, 228) : Color.FromArgb(68, 68, 72);
            using (Pen p = new Pen(border, 1f))
                e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (e.Item.Selected && e.Item.Enabled)
            {
                int mx = (int)(4 * _dpiScale);
                Rectangle r = new Rectangle(mx, 1, e.Item.Width - mx * 2, e.Item.Height - 2);
                Color hover = _isLight ? Color.FromArgb(237, 238, 241) : Color.FromArgb(60, 60, 64);
                using (GraphicsPath path = FenceChromeForm.CreateRoundRectPath(r, Math.Max(4, (int)(5 * _dpiScale))))
                using (Brush b = new SolidBrush(hover))
                    e.Graphics.FillPath(b, path);
            }

            // Draw Windows 11 Segoe Fluent Icon glyph in the left margin when provided (and item is not checked)
            var mi = e.Item as ToolStripMenuItem;
            string glyph = e.Item.Tag as string;
            if (!string.IsNullOrEmpty(glyph) && (mi == null || !mi.Checked))
            {
                e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Color iconCol = !e.Item.Enabled
                    ? (_isLight ? Color.FromArgb(165, 165, 165) : Color.FromArgb(110, 110, 110))
                    : (_isLight ? Color.FromArgb(54, 58, 66) : Color.FromArgb(228, 230, 236));
                int iconBox = (int)(26 * _dpiScale);
                Rectangle gr = new Rectangle((int)(6 * _dpiScale), 0, iconBox, e.Item.Height);
                Font glyphFont = _glyphFont;
                if (glyphFont != null)
                {
                    using (Brush gb = new SolidBrush(iconCol))
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        e.Graphics.DrawString(glyph, glyphFont, gb, gr, sf);
                    }
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            e.TextColor = !e.Item.Enabled
                ? (_isLight ? Color.FromArgb(158, 160, 166) : Color.FromArgb(118, 120, 126))
                : (_isLight ? Color.FromArgb(26, 26, 28) : Color.FromArgb(245, 245, 247));
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            int pad = (int)(10 * _dpiScale);
            Color line = _isLight ? Color.FromArgb(230, 232, 236) : Color.FromArgb(64, 64, 68);
            using (Pen p = new Pen(line, 1f))
                e.Graphics.DrawLine(p, pad, y, e.Item.Width - pad, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = e.ImageRectangle;
            int cx = r.Left + r.Width / 2;
            int cy = r.Top + r.Height / 2;
            using (Pen p = new Pen(_accent, Math.Max(1.6f, 1.8f * _dpiScale)))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                Point p1 = new Point(cx - (int)(5 * _dpiScale), cy);
                Point p2 = new Point(cx - (int)(1 * _dpiScale), cy + (int)(4 * _dpiScale));
                Point p3 = new Point(cx + (int)(5 * _dpiScale), cy - (int)(4 * _dpiScale));
                e.Graphics.DrawLines(p, new[] { p1, p2, p3 });
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = _isLight ? Color.FromArgb(96, 98, 104) : Color.FromArgb(196, 198, 204);
            base.OnRenderArrow(e);
        }
    }

    internal class Win11MenuColorTable : ProfessionalColorTable
    {
        private readonly bool _isLight;
        public Win11MenuColorTable(bool isLight) { _isLight = isLight; }
        public override Color ImageMarginGradientBegin { get { return _isLight ? Color.FromArgb(249, 249, 250) : Color.FromArgb(44, 44, 46); } }
        public override Color ImageMarginGradientMiddle { get { return ImageMarginGradientBegin; } }
        public override Color ImageMarginGradientEnd { get { return ImageMarginGradientBegin; } }
    }

    internal static class FrostedGlassTexture
    {
        private static Bitmap _grainTile;
        private static TextureBrush _grainBrush;
        private static readonly object _lock = new object();

        public static TextureBrush GetBrush()
        {
            lock (_lock)
            {
                if (_grainBrush != null) return _grainBrush;
                const int sz = 96;
                _grainTile = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
                var bd = _grainTile.LockBits(new Rectangle(0, 0, sz, sz), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] buf = new byte[Math.Abs(bd.Stride) * sz];
                    var rng = new Random(2026);
                    for (int y = 0; y < sz; y++)
                    {
                        int row = y * bd.Stride;
                        for (int x = 0; x < sz; x++)
                        {
                            int idx = row + x * 4;
                            // Delicate frosted glass micro-grain (subtle alternating bright/soft frost sparkles)
                            bool bright = rng.Next(2) == 0;
                            byte v = bright ? (byte)255 : (byte)210;
                            buf[idx] = v;
                            buf[idx + 1] = v;
                            buf[idx + 2] = v;
                            buf[idx + 3] = (byte)rng.Next(10, 26); // Subtle frosted tactility
                        }
                    }
                    Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                }
                finally
                {
                    _grainTile.UnlockBits(bd);
                }
                _grainBrush = new TextureBrush(_grainTile, WrapMode.Tile);
                return _grainBrush;
            }
        }
    }

    /// <summary>
    /// Interlayer Apple Liquid Glass backdrop embedded strictly BETWEEN Wallpaper Engine / Desktop Wallpaper (WorkerW)
    /// and Native Desktop Icons (SHELLDLL_DefView).
    /// Uses SetLayeredWindowAttributes(LWA_ALPHA) (proven 100% visible inside Windows 11 Build 26300 Progman!)
    /// with low translucent alpha (~18-24%) so LIVE 60 FPS Wallpaper Engine shines directly through in real time!
    /// </summary>
    public class FenceBackdropForm : Form
    {
        private IntPtr _interlayerParent = IntPtr.Zero;
        private IntPtr _interlayerInsertAfter = IntPtr.Zero;
        private bool _isEmbeddedInInterlayer;

        private Color _topColor = Color.FromArgb(255, 255, 255);
        private Color _midColor = Color.FromArgb(240, 246, 255);
        private Color _botColor = Color.FromArgb(248, 252, 255);
        private byte _lastAlpha = 0;
        private int _lastRgnW = -1, _lastRgnH = -1, _lastRgnR = -1;
        private int _lastDstX = int.MinValue, _lastDstY = int.MinValue, _lastW = -1, _lastH = -1;
        private bool _lastVisible;

        public FenceBackdropForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW
                            | NativeMethods.WS_EX_LAYERED
                            | NativeMethods.WS_EX_TRANSPARENT
                            | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING && !_isEmbeddedInInterlayer)
            {
                var wp = (NativeMethods.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.WINDOWPOS));
                wp.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                wp.flags &= ~NativeMethods.SWP_NOZORDER;
                wp.flags |= NativeMethods.SWP_NOACTIVATE;
                Marshal.StructureToPtr(wp, m.LParam, false);
            }
            base.WndProc(ref m);
        }

        public void AttachToInterlayer(IntPtr parentHwnd, IntPtr insertAfterHwnd, IntPtr fallbackOwner)
        {
            _lastDstX = int.MinValue;
            _lastVisible = false;
            if (!IsHandleCreated)
            {
                var ensure = Handle;
            }

            if (NativeMethods.IsWindow(parentHwnd) &&
                (insertAfterHwnd == NativeMethods.HWND_TOP || NativeMethods.GetParent(insertAfterHwnd) == parentHwnd))
            {
                _interlayerParent = parentHwnd;
                _interlayerInsertAfter = insertAfterHwnd;

                uint style = unchecked((uint)NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE));
                style = (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD | NativeMethods.WS_CLIPSIBLINGS;
                NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_STYLE, new IntPtr(unchecked((int)style)));

                NativeMethods.SetParent(Handle, parentHwnd);

                // Re-apply WS_EX_LAYERED + LWA_ALPHA AFTER SetParent (required by Win11 24H2/26300 Progman Raised Desktop!)
                int exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
                NativeMethods.SetLayeredWindowAttributes(Handle, 0, 52, NativeMethods.LWA_ALPHA);
                _lastAlpha = 52;

                if (NativeMethods.GetParent(Handle) == parentHwnd)
                {
                    _isEmbeddedInInterlayer = true;
                    NativeMethods.SetWindowPos(Handle, insertAfterHwnd, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
                    return;
                }
            }

            _isEmbeddedInInterlayer = false;
            _interlayerParent = _interlayerInsertAfter = IntPtr.Zero;
            uint fallbackStyle = unchecked((uint)NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE));
            if ((fallbackStyle & NativeMethods.WS_CHILD) != 0)
            {
                NativeMethods.SetParent(Handle, IntPtr.Zero);
                fallbackStyle = (fallbackStyle & ~NativeMethods.WS_CHILD) | NativeMethods.WS_POPUP;
                NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_STYLE, new IntPtr(unchecked((int)fallbackStyle)));
            }
            NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWLP_HWNDPARENT, fallbackOwner);
            NativeMethods.SetLayeredWindowAttributes(Handle, 0, 52, NativeMethods.LWA_ALPHA);
            _lastAlpha = 52;
            SendToDesktopBottom();
        }

        public void HideBackdrop()
        {
            _lastVisible = false;
            if (!IsHandleCreated) return;
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
        }

        public void SendToDesktopBottom()
        {
            if (!IsHandleCreated) return;
            IntPtr after = _isEmbeddedInInterlayer ? _interlayerInsertAfter : NativeMethods.HWND_BOTTOM;
            NativeMethods.SetWindowPos(
                Handle, after,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        public void SyncVisuals(Rectangle screenBounds, Color bodyGlaze, Color topCaustic, Color bottomBounce, double tintScale, int cornerRadius)
        {
            if (!IsHandleCreated)
            {
                var ensure = Handle;
            }

            if (tintScale <= 0.01)
            {
                HideBackdrop();
                return;
            }

            int w = Math.Max(4, screenBounds.Width);
            int h = Math.Max(4, screenBounds.Height);
            int cr = Math.Max(4, cornerRadius);

            // Compute delicate frosted Liquid Glass opacity (~28% frosted for liquid_clear, scaled by preset)
            int targetAlpha = Math.Max(22, Math.Min(185, (int)Math.Round((bodyGlaze.A + 20) * tintScale)));
            byte alphaByte = (byte)targetAlpha;

            Color newTop = Color.FromArgb(255, topCaustic.R, topCaustic.G, topCaustic.B);
            Color newMid = Color.FromArgb(255, bodyGlaze.R, bodyGlaze.G, bodyGlaze.B);
            Color newBot = Color.FromArgb(255, bottomBounce.R, bottomBounce.G, bottomBounce.B);
            bool colorChanged = (newTop != _topColor || newMid != _midColor || newBot != _botColor);
            _topColor = newTop;
            _midColor = newMid;
            _botColor = newBot;

            if (alphaByte != _lastAlpha)
            {
                _lastAlpha = alphaByte;
                NativeMethods.SetLayeredWindowAttributes(Handle, 0, alphaByte, NativeMethods.LWA_ALPHA);
            }

            int dstX = screenBounds.X;
            int dstY = screenBounds.Y;
            if (_isEmbeddedInInterlayer && _interlayerParent != IntPtr.Zero)
            {
                var pt = new NativeMethods.POINT { X = screenBounds.X, Y = screenBounds.Y };
                NativeMethods.ScreenToClient(_interlayerParent, ref pt);
                dstX = pt.X;
                dstY = pt.Y;
            }

            bool needShow = !_lastVisible || !NativeMethods.IsWindowVisible(Handle);
            bool posChanged = dstX != _lastDstX || dstY != _lastDstY;
            bool sizeChanged = w != _lastW || h != _lastH;
            if (needShow || posChanged || sizeChanged)
            {
                // Avoid re-linking Progman's child Z-order on every 10ms glide frame!
                // Re-specifying hwndInsertAfter without SWP_NOZORDER 100x/sec floods Explorer's
                // desktop UI thread during overlap auto-relocation glides.
                uint swpFlags = NativeMethods.SWP_NOACTIVATE;
                if (needShow)
                    swpFlags |= NativeMethods.SWP_SHOWWINDOW;
                else
                    swpFlags |= NativeMethods.SWP_NOZORDER;
                if (!sizeChanged && !needShow)
                    swpFlags |= NativeMethods.SWP_NOSIZE;
                if (!posChanged && !needShow)
                    swpFlags |= NativeMethods.SWP_NOMOVE;

                IntPtr after = _isEmbeddedInInterlayer ? _interlayerInsertAfter : NativeMethods.HWND_BOTTOM;
                NativeMethods.SetWindowPos(Handle, after, dstX, dstY, w, h, swpFlags);
                _lastDstX = dstX;
                _lastDstY = dstY;
                _lastW = w;
                _lastH = h;
                _lastVisible = true;
            }

            if (w != _lastRgnW || h != _lastRgnH || cr != _lastRgnR)
            {
                _lastRgnW = w; _lastRgnH = h; _lastRgnR = cr;
                IntPtr full = NativeMethods.CreateRoundRectRgn(0, -cr, w + 1, h + 1, cr * 2, cr * 2);
                IntPtr clip = NativeMethods.CreateRectRgn(0, 0, w + 1, h + 1);
                IntPtr res = NativeMethods.CreateRectRgn(0, 0, 0, 0);
                NativeMethods.CombineRgn(res, full, clip, NativeMethods.RGN_AND);
                NativeMethods.DeleteObject(full);
                NativeMethods.DeleteObject(clip);
                if (NativeMethods.SetWindowRgn(Handle, res, true) == 0)
                    NativeMethods.DeleteObject(res);
                Invalidate();
                Update();
            }
            else if (colorChanged)
            {
                Invalidate();
                Update();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            using (LinearGradientBrush b = new LinearGradientBrush(r, _topColor, _botColor, 90f))
            {
                b.InterpolationColors = new ColorBlend(3)
                {
                    Colors = new[] { _topColor, _midColor, _botColor },
                    Positions = new[] { 0.0f, 0.35f, 1.0f }
                };
                e.Graphics.FillRectangle(b, r);
            }
            // Delicate frosted glass micro-grain texture
            e.Graphics.FillRectangle(FrostedGlassTexture.GetBrush(), r);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _isEmbeddedInInterlayer && IsHandleCreated)
            {
                try
                {
                    NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
                    NativeMethods.SetParent(Handle, IntPtr.Zero);
                }
                catch { }
                _isEmbeddedInInterlayer = false;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// True per-pixel translucent Apple Liquid Glass frame rendered via UpdateLayeredWindow:
    /// - At rest: interior pixels have Alpha == 0 (100% natively mouse click-through to Windows Desktop!),
    ///   while the sheer translucent Title Bar + Directional Specular Rim show LIVE Wallpaper Engine underneath
    ///   with silky GPU-antialiased rounded corners (zero jagged SetWindowRgn!).
    /// - Explorer draws the icons throughout a drag. The frame follows confirmed whole-cell moves.
    /// </summary>
    public class FenceChromeForm : Form
    {
        private readonly FenceManager _manager;
        public FenceConfig Config { get; private set; }
        public FenceBackdropForm Backdrop { get; private set; }

        public float DpiScale { get; private set; }
        private int _headerHeight = 40;
        public int HeaderHeight { get { return _headerHeight; } }
        public int RegionBorderThickness { get { return Math.Max(4, (int)Math.Round(4 * DpiScale)); } }
        public int CornerRadius { get { return Math.Max(14, (int)Math.Round(16 * DpiScale)); } }
        private int CornerGrab { get { return Math.Max(16, (int)Math.Round(18 * DpiScale)); } }

        private bool _hoverMenuBtn;
        private bool _desktopVisible = true;
        private bool _startingInteraction;
        private bool _releasedDuringStart;
        private bool _dragPending;
        private bool _isDragging;
        private bool _isDragSurfaceActive;
        private bool _dragReleasePending;
        private bool _dragRelocated;
        private bool _isDragOverlapping;
        private Bitmap _dragNativeIconsLayer;
        private Point _dragStartMouse;
        private Point? _lastDragCandidate;
        private bool _dragDpiChanged;
        private int _dragStartOuterCol;
        private int _dragStartOuterRow;
        private int _dragStepCol;
        private int _dragStepRow;
        private DesktopGridMetrics _dragMetrics;
        private Rectangle[] _dragWorkAreas;
        private List<DesktopIconInfo> _capturedMembers;
        private bool _membersParked;

        private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
        private ResizeEdge _pendingResizeEdge = ResizeEdge.None;
        private ResizeEdge _resizeEdge = ResizeEdge.None;
        private Point _resizeStartMouse;
        private int _resizeStartCol, _resizeStartRow, _resizeStartCols, _resizeStartRows;
        private DesktopGridMetrics _resizeMetrics;
        private Rectangle _resizeWorkArea;

        private class ResizeMemberTrack
        {
            public string Identity;
            public int Col;
            public int Row;
            public Point CurrentClientPos;
            public Point AnimTargetClientPos;
        }
        private List<ResizeMemberTrack> _resizeTracks;

        private Form _renamePopup;
        private Font _titleFont;

        // Smooth frame-rate-independent exponential spring animation
        private readonly Timer _animTimer;
        private readonly Stopwatch _animWatch = new Stopwatch();
        private double _animLastMs;
        private double _curX, _curY, _curW, _curH;
        private Rectangle _animTargetBounds;
        private bool _releasePending;
        private bool _completingInteraction;
        internal bool CompletingInteraction { get { return _startingInteraction || _completingInteraction; } }
        internal bool IsInteracting
        {
            get
            {
                return _startingInteraction || _dragPending || _pendingResizeEdge != ResizeEdge.None ||
                       _isDragging || _dragReleasePending ||
                       _resizeEdge != ResizeEdge.None || _releasePending || _completingInteraction;
            }
        }

        public FenceChromeForm(FenceManager manager, FenceConfig config)
        {
            _manager = manager;
            Config = config;
            DpiScale = NativeMethods.GetWindowDpiScale(IntPtr.Zero);
            RebuildFonts();
            Backdrop = new FenceBackdropForm();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            _animTimer = new Timer { Interval = 10 };
            _animTimer.Tick += OnAnimTimerTick;
        }

        private void RebuildFonts()
        {
            if (_titleFont != null) _titleFont.Dispose();
            _titleFont = new Font("Microsoft YaHei UI", 13.5f * DpiScale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateDpiScale();
            AttachToDesktopLayer();
            SyncBoundsFromGrid(_manager.GetGridMetrics());
        }

        public void UpdateDpiScale()
        {
            float newScale = NativeMethods.GetWindowDpiScale(Handle);
            if (Math.Abs(newScale - DpiScale) > 0.02f)
            {
                DpiScale = newScale;
                RebuildFonts();
            }
        }

        public void AttachToDesktopLayer()
        {
            IntPtr owner = _manager.GetDesktopOwner();
            if (IsHandleCreated)
            {
                NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWLP_HWNDPARENT, owner);
            }
            IntPtr interParent, interAfter;
            _manager.GetInterlayerHost(out interParent, out interAfter);
            Backdrop.AttachToInterlayer(interParent, interAfter, owner);
            SendToDesktopBottom();
        }

        public void SendToDesktopBottom()
        {
            if (IsHandleCreated)
            {
                NativeMethods.SetWindowPos(
                    Handle, NativeMethods.HWND_BOTTOM,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
            Backdrop.SendToDesktopBottom();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING)
            {
                var wp = (NativeMethods.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.WINDOWPOS));
                wp.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                wp.flags &= ~NativeMethods.SWP_NOZORDER;
                Marshal.StructureToPtr(wp, m.LParam, false);
            }
            else if (m.Msg == NativeMethods.WM_DPICHANGED)
            {
                if (_isDragging)
                {
                    // Use one physical desktop grid for the whole gesture, including monitor crossings.
                    _dragDpiChanged = true;
                    m.Result = IntPtr.Zero;
                    return;
                }
                int newDpi = m.WParam.ToInt32() & 0xFFFF;
                base.WndProc(ref m);
                if (newDpi >= 96 && newDpi <= 480)
                {
                    DpiScale = newDpi / 96f;
                    RebuildFonts();
                    if (IsHandleCreated && !Disposing)
                        BeginInvoke((Action)SyncAfterDpiChange);
                }
                return;
            }
            base.WndProc(ref m);
        }

        private void SyncAfterDpiChange()
        {
            if (IsDisposed || !IsHandleCreated || !_manager.DesktopReady) return;
            if (_isDragging)
            {
                _dragDpiChanged = true;
                return;
            }
            if (_resizeTracks != null)
            {
                AnimateToBounds(ComputeGridBounds(_resizeMetrics));
                RefreshVisuals();
            }
            else SyncBoundsFromGrid(_manager.GetGridMetrics());
        }

        public static Rectangle ComputeGridBounds(
            int outerCol, int outerRow, int outerCols, int outerRows,
            DesktopGridMetrics m, float dpiScale, out int headerHeight)
        {
            int innerLeft = m.OriginX + (outerCol + 1) * m.CellWidth;
            int innerTop = m.OriginY + (outerRow + 1) * m.CellHeight;
            int innerRight = m.OriginX + (outerCol + outerCols - 1) * m.CellWidth;
            int innerBottom = m.OriginY + (outerRow + outerRows - 1) * m.CellHeight;

            // Balanced Apple Liquid Glass framing around InnerRect:
            // generous left/right/bottom margins + raised title bar with clean breathing space above icons
            // and a neat ~14-18px gap between vertically stacked zones.
            int sidePad = Math.Max(14, (int)Math.Round(m.CellWidth * 0.32));
            int bottomPad = Math.Max(12, (int)Math.Round(m.CellHeight * 0.26));
            headerHeight = Math.Min(Math.Max(34, m.CellHeight - 16), Math.Max(38, (int)Math.Round(42 * dpiScale)));
            int minInterZoneGap = Math.Max(12, (int)Math.Round(12 * dpiScale));
            int maxSafeTopGap = Math.Max(8, m.CellHeight - headerHeight - bottomPad - minInterZoneGap);
            int topInnerGap = Math.Max(8, Math.Min(maxSafeTopGap, (int)Math.Round(m.CellHeight * 0.18)));
            int titleBottomY = innerTop - topInnerGap;
            int titleTopY = titleBottomY - headerHeight;

            // The outer ring already belongs to one monitor's working area.
            int fx = Math.Max(innerLeft - m.CellWidth, innerLeft - sidePad);
            int fy = Math.Max(innerTop - m.CellHeight, titleTopY);
            int fw = Math.Min(innerRight + m.CellWidth - fx, Math.Max(140, innerRight + sidePad - fx));
            int fh = Math.Min(innerBottom + m.CellHeight - fy, Math.Max(headerHeight + 60, innerBottom + bottomPad - fy));

            return new Rectangle(fx, fy, fw, fh);
        }

        public Rectangle ComputeGridBounds(DesktopGridMetrics m)
        {
            int hh;
            Rectangle r = ComputeGridBounds(Config.OuterCol, Config.OuterRow, Config.OuterCols, Config.OuterRows, m, DpiScale, out hh);
            _headerHeight = hh;
            return r;
        }

        public void SyncBoundsFromGrid(DesktopGridMetrics m)
        {
            CloseRenamePopup();
            SettleInteraction(_manager.CanCommitToCurrentDesktop(), false);
            _animTimer.Stop();
            _animWatch.Stop();
            Bounds = ComputeGridBounds(m);
            if (_desktopVisible)
            {
                RedrawLayeredSurface();
                SyncBackdrop();
            }
        }

        public void SetDesktopVisibility(bool visible)
        {
            if (_desktopVisible == visible) return;
            _desktopVisible = visible;
            if (!IsHandleCreated || IsDisposed) return;
            if (!visible)
            {
                CloseRenamePopup();
                Hide();
                if (Backdrop != null && !Backdrop.IsDisposed) Backdrop.HideBackdrop();
            }
            else
            {
                Show();
                RedrawLayeredSurface();
                SyncBackdrop();
                SendToDesktopBottom();
            }
        }

        private void CloseRenamePopup()
        {
            if (_renamePopup != null)
            {
                var p = _renamePopup;
                _renamePopup = null;
                try { p.Close(); p.Dispose(); } catch { }
            }
        }

        public void RefreshVisuals()
        {
            if (!_desktopVisible) return;
            RedrawLayeredSurface();
            SyncBackdrop();
        }

        private void OnAnimTimerTick(object sender, EventArgs e)
        {
            if (!_animTimer.Enabled) return;
            if (!_manager.DesktopReady)
            {
                SettleInteraction(false, false);
                return;
            }
            double nowMs = _animWatch.Elapsed.TotalMilliseconds;
            double dt = Math.Min(0.05, Math.Max(0.004, (nowMs - _animLastMs) / 1000.0));
            _animLastMs = nowMs;
            StepAnimation(dt);
        }

        private void StepAnimation(double dt)
        {
            // Crisp, taut exponential spring (34.0) so grid steps feel immediately responsive.
            double alpha = 1.0 - Math.Exp(-dt * 34.0);

            _curX += (_animTargetBounds.X - _curX) * alpha;
            _curY += (_animTargetBounds.Y - _curY) * alpha;
            _curW += (_animTargetBounds.Width - _curW) * alpha;
            _curH += (_animTargetBounds.Height - _curH) * alpha;

            bool settled =
                Math.Abs(_animTargetBounds.X - _curX) < 0.6 &&
                Math.Abs(_animTargetBounds.Y - _curY) < 0.6 &&
                Math.Abs(_animTargetBounds.Width - _curW) < 0.6 &&
                Math.Abs(_animTargetBounds.Height - _curH) < 0.6;

            int x = settled ? _animTargetBounds.X : (int)Math.Round(_curX);
            int y = settled ? _animTargetBounds.Y : (int)Math.Round(_curY);
            int w = settled ? _animTargetBounds.Width : (int)Math.Round(_curW);
            int h = settled ? _animTargetBounds.Height : (int)Math.Round(_curH);

            bool sizeChanged = (Bounds.Width != w || Bounds.Height != h);
            bool posChanged = (Bounds.X != x || Bounds.Y != y);

            if (posChanged || sizeChanged)
            {
                if (sizeChanged)
                {
                    Bounds = new Rectangle(x, y, w, h);
                    RedrawLayeredSurface();
                }
                else
                {
                    Bounds = new Rectangle(x, y, w, h);
                }

                if (Config.BackdropOpacity > 0.01)
                    SyncBackdrop();
            }

            if (settled) FinishAnimation();
            else ApplyResizeFrame(alpha, false);
        }

        private void ApplyResizeFrame(double alpha, bool settled)
        {
            if (_resizeTracks == null || _resizeTracks.Count == 0) return;
            var moves = new List<KeyValuePair<string, Point>>(_resizeTracks.Count);
            foreach (var tr in _resizeTracks)
            {
                Point next = tr.AnimTargetClientPos;
                if (!settled)
                {
                    int x = (int)Math.Round(tr.CurrentClientPos.X + (next.X - tr.CurrentClientPos.X) * alpha);
                    int y = (int)Math.Round(tr.CurrentClientPos.Y + (next.Y - tr.CurrentClientPos.Y) * alpha);
                    next = new Point(x, y);
                    if (next == tr.CurrentClientPos) continue;
                }
                tr.CurrentClientPos = next;
                moves.Add(new KeyValuePair<string, Point>(tr.Identity, next));
            }
            if (moves.Count > 0) _manager.MoveIconsToClientPositions(moves);
        }

        private void FinishAnimation()
        {
            _animTimer.Stop();
            _animWatch.Stop();
            if (_dragReleasePending)
                CompleteRigidDrag(true, true);
            else if (_releasePending)
                CompleteResize(true, true);
            else
                ApplyResizeFrame(1.0, true);
        }

        private void AnimateToBounds(Rectangle target)
        {
            _animTargetBounds = target;

            if (Bounds == target)
            {
                FinishAnimation();
                return;
            }

            if (!_animTimer.Enabled)
            {
                _curX = Bounds.X;
                _curY = Bounds.Y;
                _curW = Bounds.Width;
                _curH = Bounds.Height;
                _animWatch.Restart();
                _animLastMs = 0;
                _animTimer.Start();
                // Step immediately on frame 0 so the fence reacts on the very same mouse event
                // instead of waiting 15.6ms for the first WinForms timer tick.
                StepAnimation(0.016);
            }
        }

        public void SyncBackdrop()
        {
            if (Backdrop == null || Backdrop.IsDisposed) return;
            if (!_desktopVisible || Config.BackdropOpacity <= 0.01)
            {
                Backdrop.HideBackdrop();
                return;
            }
            FenceTheme theme = FenceTheme.Resolve(Config.ThemeId);
            int inset = 2;
            int hh = HeaderHeight;
            Rectangle bodyRect = new Rectangle(
                Location.X + inset,
                Location.Y + hh,
                Math.Max(4, Width - inset * 2),
                Math.Max(4, Height - hh - inset));
            Backdrop.SyncVisuals(bodyRect, theme.BodyGlaze, theme.TopCaustic, theme.BottomBounce, Config.BackdropOpacity, Math.Max(6, CornerRadius - inset));
        }

        private void EnterRigidDragSurface()
        {
            DisposeDragVisuals();
            _membersParked = false;

            bool hasMembers = _capturedMembers != null && _capturedMembers.Count > 0;
            if (!hasMembers)
            {
                _isDragSurfaceActive = false;
                return;
            }

            int hh = HeaderHeight;
            Rectangle bodyScreenRect = new Rectangle(Location.X, Location.Y + hh, Width, Math.Max(2, Height - hh));
            IntPtr lv = _manager.GetDesktopListViewHandle();
            if (lv != IntPtr.Zero)
                _dragNativeIconsLayer = NativeMethods.CaptureNativeListViewIconsTransparent(lv, bodyScreenRect, _capturedMembers);

            if (_dragNativeIconsLayer == null)
            {
                _isDragSurfaceActive = false;
                return;
            }

            // 1. Present ONLY the transparent captured icons on FenceChromeForm while keeping the exact
            //    same FenceBackdropForm continuously visible underneath, then latch 1 DWM frame so
            //    captured icons are guaranteed visible BEFORE Explorer synchronously clears the real ones!
            _isDragSurfaceActive = true;
            RedrawLayeredSurface();
            try { NativeMethods.DwmFlush(); } catch { }

            // 2. Park real icons offscreen (with SnapToGrid suspended BEFORE moving, inside WM_SETREDRAW(false))
            //    and synchronously flush ONLY this fence's rect so old real icons NEVER linger behind when moving!
            if (_manager.ParkDragMembers(_capturedMembers, _dragMetrics.SnapToGridEnabled, Bounds))
            {
                _membersParked = true;
            }
            else
            {
                ExitRigidDragSurface();
            }
        }

        private void ExitRigidDragSurface()
        {
            _isDragSurfaceActive = false;
            DisposeDragVisuals();
            if (!Disposing && !IsDisposed)
            {
                RedrawLayeredSurface();
                SyncBackdrop();
            }
        }

        private void DisposeDragVisuals()
        {
            if (_dragNativeIconsLayer != null)
            {
                _dragNativeIconsLayer.Dispose();
                _dragNativeIconsLayer = null;
            }
        }

        /// <summary>
        /// Paints the entire per-pixel alpha Apple Liquid Glass frame into a 32-bit premultiplied ARGB
        /// bitmap and presents it via UpdateLayeredWindow.
        /// At rest, interior pixels remain strictly Alpha==0 -> 100% native Windows desktop click-through!
        /// </summary>
        public void RedrawLayeredSurface()
        {
            if (!IsHandleCreated || Width <= 4 || Height <= 4) return;
            using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bitmap)) DrawLayeredSurface(g);
                NativeMethods.PresentLayeredBitmap(Handle, bitmap, Left, Top);
            }
        }

        internal void DrawLayeredSurface(Graphics g)
        {
            FenceTheme theme = FenceTheme.Resolve(Config.ThemeId);
            int w = Width;
            int h = Height;
            int hh = HeaderHeight;
            int cr = CornerRadius;
            int bt = RegionBorderThickness;

            g.Clear(Color.FromArgb(0, 0, 0, 0));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // 1. Header bar: clearly legible frosted Liquid Glass (~65% opaque, unaffected by body 0% toggle)
            using (GraphicsPath hdrPath = CreateTopRoundedPath(new Rectangle(1, 1, w - 2, hh - 1), Math.Max(6, cr - 1)))
            {
                Color hdrGlaze = theme.HeaderGlaze;
                using (Brush hb = new SolidBrush(hdrGlaze))
                    g.FillPath(hb, hdrPath);

                Color topCaustic = theme.TopCaustic;
                using (LinearGradientBrush caustic = new LinearGradientBrush(
                    new Rectangle(1, 1, w - 2, hh),
                    topCaustic,
                    Color.FromArgb(0, 255, 255, 255),
                    90f))
                {
                    g.FillPath(caustic, hdrPath);
                }
                g.FillPath(FrostedGlassTexture.GetBrush(), hdrPath);
            }

            if (_isDragSurfaceActive)
            {
                // During rigid drag: FenceBackdropForm continuously provides the exact same frosted Liquid Glass
                // body underneath (zero hide/show, zero double-glass flash!), while FenceChromeForm composites
                // ONLY the transparent captured native icons at 1:1 pixel lock!
                if (_dragNativeIconsLayer != null)
                {
                    using (GraphicsPath bodyPath = CreateBottomRoundedPath(new Rectangle(1, hh, w - 2, Math.Max(2, h - hh - 1)), Math.Max(6, cr - 1)))
                    {
                        g.SetClip(bodyPath);
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.None;
                        g.DrawImage(
                            _dragNativeIconsLayer,
                            new Rectangle(0, hh, _dragNativeIconsLayer.Width, _dragNativeIconsLayer.Height),
                            new Rectangle(0, 0, _dragNativeIconsLayer.Width, _dragNativeIconsLayer.Height),
                            GraphicsUnit.Pixel);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.ResetClip();
                    }
                }
            }
            else
            {
                // At rest: give the slim resize rim an almost-invisible hit-testable glaze (Alpha=8)
                // while interior remains strictly Alpha==0 (100% native Windows desktop click-through!).
                using (GraphicsPath outerGrab = CreateRoundRectPath(new Rectangle(0, 0, w - 1, h - 1), cr))
                using (Pen grabPen = new Pen(Color.FromArgb(8, 255, 255, 255), bt * 2f))
                {
                    g.SetClip(outerGrab);
                    g.DrawPath(grabPen, outerGrab);
                    g.ResetClip();
                }
                g.CompositingMode = CompositingMode.SourceCopy;
                using (Brush clearInterior = new SolidBrush(Color.Transparent))
                    g.FillRectangle(clearInterior, bt, hh, Math.Max(0, w - bt * 2), Math.Max(0, h - hh - bt));
                g.CompositingMode = CompositingMode.SourceOver;
            }

            // 3. Whisper-thin liquid glass hairline separating header & body
            using (Pen divPen = new Pen(theme.DividerColor, 1f))
            {
                g.DrawLine(divPen, (int)(12 * DpiScale), hh - 1, w - (int)(12 * DpiScale), hh - 1);
            }

            // 4. Signature Apple Liquid Glass Directional Specular Rim
            DrawLiquidGlassRim(g, theme, w, h, cr);

            // 5. Minimalist 2x2 rounded bento-grid zone icon (replaces yellow folder icon)
            int iconSz = (int)Math.Round(14 * DpiScale);
            int iconX = (int)Math.Round(14 * DpiScale);
            int iconY = (hh - iconSz) / 2;
            DrawZoneGridIcon(g, new Rectangle(iconX, iconY, iconSz, iconSz), theme);

            // 6. Title text
            int textX = iconX + iconSz + (int)Math.Round(9 * DpiScale);
            Rectangle menuRect = GetMenuButtonRect();
            Rectangle titleRect = new Rectangle(textX, 0, Math.Max(30, menuRect.Left - textX - 6), hh);
            string title = string.IsNullOrEmpty(Config.Title) ? "未命名分区" : Config.Title;
            using (StringFormat sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center })
            {
                if (theme.TextShadow)
                {
                    using (Brush sb = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
                        g.DrawString(title, _titleFont, sb, new Rectangle(titleRect.X, titleRect.Y + 1, titleRect.Width, titleRect.Height), sf);
                }
                using (Brush tb = new SolidBrush(theme.TextColor))
                    g.DrawString(title, _titleFont, tb, titleRect, sf);
            }

            // 7. Rounded pill hover + 3 dots menu button
            if (_hoverMenuBtn)
            {
                using (GraphicsPath mp = CreateRoundRectPath(menuRect, Math.Max(6, (int)(7 * DpiScale))))
                using (Brush hb = new SolidBrush(Color.FromArgb(theme.TextShadow ? 48 : 26, theme.TextShadow ? Color.White : Color.Black)))
                    g.FillPath(hb, mp);
            }

            float dotR = Math.Max(1.15f, 1.25f * DpiScale);
            float dotGap = 4.4f * DpiScale;
            float cx = menuRect.Left + menuRect.Width / 2f;
            float cy = menuRect.Top + menuRect.Height / 2f;
            using (Brush db = new SolidBrush(Color.FromArgb(_hoverMenuBtn ? 255 : 210, theme.TextColor)))
            {
                for (int i = -1; i <= 1; i++)
                {
                    float dx = cx + i * dotGap - dotR;
                    g.FillEllipse(db, dx, cy - dotR, dotR * 2, dotR * 2);
                }
            }
        }

        private void DrawLiquidGlassRim(Graphics g, FenceTheme theme, int w, int h, int cr)
        {
            Rectangle outerR = new Rectangle(0, 0, w - 1, h - 1);
            if (outerR.Width <= 4 || outerR.Height <= 4) return;

            using (GraphicsPath shadowPath = CreateRoundRectPath(outerR, cr))
            using (Pen darkEdge = new Pen(Color.FromArgb(46, 0, 0, 0), Math.Max(1f, 1.0f * DpiScale)))
                g.DrawPath(darkEdge, shadowPath);

            Rectangle rimR = new Rectangle(1, 1, w - 3, h - 3);
            float strokeW = Math.Max(1.3f, 1.55f * DpiScale);
            using (GraphicsPath rimPath = CreateRoundRectPath(rimR, Math.Max(4, cr - 1)))
            {
                if (_isDragOverlapping)
                {
                    // Soft warm amber-coral rim indicates overlap (will auto-relocate to nearest free spot on release)
                    using (Pen warnPen = new Pen(Color.FromArgb(235, 255, 162, 92), Math.Max(2.0f, 2.2f * DpiScale)))
                        g.DrawPath(warnPen, rimPath);
                }
                else
                {
                    using (LinearGradientBrush rimGrad = new LinearGradientBrush(rimR, theme.RimTopLeft, theme.RimBottomRight, 42f))
                    {
                        rimGrad.InterpolationColors = new ColorBlend(3)
                        {
                            Colors = new[] { theme.RimTopLeft, theme.RimMid, theme.RimBottomRight },
                            Positions = new[] { 0.0f, 0.48f, 1.0f }
                        };
                        using (Pen rimPen = new Pen(rimGrad, strokeW))
                            g.DrawPath(rimPen, rimPath);
                    }
                }
            }
        }

        private static GraphicsPath CreateTopRoundedPath(Rectangle r, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height * 2));
            var path = new GraphicsPath();
            if (d <= 1) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath CreateBottomRoundedPath(Rectangle r, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            if (d <= 1) { path.AddRectangle(r); return path; }
            path.AddLine(r.X, r.Y, r.Right, r.Y);
            path.AddLine(r.Right, r.Y, r.Right, r.Bottom - radius);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath CreateRoundRectPath(Rectangle r, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            if (d <= 1) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawZoneGridIcon(Graphics g, Rectangle r, FenceTheme theme)
        {
            int gap = Math.Max(2, r.Width / 6);
            int cell = Math.Max(3, (r.Width - gap) / 2);
            int rad = Math.Max(1, cell / 3);

            Rectangle[] cells = new[]
            {
                new Rectangle(r.X, r.Y, cell, cell),
                new Rectangle(r.X + cell + gap, r.Y, cell, cell),
                new Rectangle(r.X, r.Y + cell + gap, cell, cell),
                new Rectangle(r.X + cell + gap, r.Y + cell + gap, cell, cell)
            };

            if (theme.TextShadow)
            {
                using (Brush sb = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
                {
                    foreach (var c in cells)
                    {
                        using (GraphicsPath sp = CreateRoundRectPath(new Rectangle(c.X, c.Y + 1, c.Width, c.Height), rad))
                            g.FillPath(sb, sp);
                    }
                }
            }

            Color primary = Color.FromArgb(235, theme.TextColor);
            Color secondary = Color.FromArgb(160, theme.TextColor);
            using (Brush b1 = new SolidBrush(primary))
            using (Brush b2 = new SolidBrush(secondary))
            {
                for (int i = 0; i < 4; i++)
                {
                    using (GraphicsPath p = CreateRoundRectPath(cells[i], rad))
                        g.FillPath((i == 0 || i == 3) ? b1 : b2, p);
                }
            }
        }

        private Rectangle GetMenuButtonRect()
        {
            int btnSz = (int)Math.Round(28 * DpiScale);
            int padR = (int)Math.Round(8 * DpiScale);
            return new Rectangle(Width - btnSz - padR, (HeaderHeight - btnSz) / 2, btnSz, btnSz);
        }

        private ResizeEdge HitTestResizeEdge(Point pt)
        {
            if (_manager.Config.Locked) return ResizeEdge.None;

            int bt = RegionBorderThickness + 3;
            int cg = CornerGrab;

            bool left = pt.X <= bt;
            bool right = pt.X >= Width - bt;
            bool top = pt.Y <= bt;
            bool bottom = pt.Y >= Height - bt;

            bool nearLeft = pt.X <= cg;
            bool nearRight = pt.X >= Width - cg;
            bool nearTop = pt.Y <= cg;
            bool nearBottom = pt.Y >= Height - cg;

            if ((top && nearLeft) || (left && nearTop)) return ResizeEdge.TopLeft;
            if ((top && nearRight) || (right && nearTop)) return ResizeEdge.TopRight;
            if ((bottom && nearLeft) || (left && nearBottom)) return ResizeEdge.BottomLeft;
            if ((bottom && nearRight) || (right && nearBottom)) return ResizeEdge.BottomRight;
            if (left) return ResizeEdge.Left;
            if (right) return ResizeEdge.Right;
            if (top) return ResizeEdge.Top;
            if (bottom) return ResizeEdge.Bottom;
            return ResizeEdge.None;
        }

        private static Cursor EdgeToCursor(ResizeEdge edge)
        {
            switch (edge)
            {
                case ResizeEdge.Left:
                case ResizeEdge.Right: return Cursors.SizeWE;
                case ResizeEdge.Top:
                case ResizeEdge.Bottom: return Cursors.SizeNS;
                case ResizeEdge.TopLeft:
                case ResizeEdge.BottomRight: return Cursors.SizeNWSE;
                case ResizeEdge.TopRight:
                case ResizeEdge.BottomLeft: return Cursors.SizeNESW;
                default: return Cursors.Default;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if ((e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) || !_manager.PrepareFenceInteraction())
                return;

            if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu(e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            // Ignore double-click's 2nd MouseDown so OnMouseDoubleClick opens inline rename cleanly
            // without triggering capture, icon parking, or snap toggling.
            if (e.Clicks >= 2) return;

            if (GetMenuButtonRect().Contains(e.Location))
            {
                ShowContextMenu(new Point(GetMenuButtonRect().Left, GetMenuButtonRect().Bottom + 2));
                return;
            }

            ResizeEdge edge = HitTestResizeEdge(e.Location);
            if (edge != ResizeEdge.None)
            {
                _animTimer.Stop();
                _pendingResizeEdge = edge;
                _resizeStartMouse = Cursor.Position;
                Capture = true;
                return;
            }

            if (e.Y <= HeaderHeight && !_manager.Config.Locked)
            {
                _animTimer.Stop();
                _dragPending = true;
                _dragStartMouse = Cursor.Position;
                Capture = true;
            }
        }

        private bool StartActualResize(ResizeEdge edge)
        {
            _pendingResizeEdge = ResizeEdge.None;
            if (!_manager.PrepareFenceInteraction())
            {
                if (IsHandleCreated && Capture) Capture = false;
                return false;
            }
            _resizeStartCol = Config.OuterCol;
            _resizeStartRow = Config.OuterRow;
            _resizeStartCols = Config.OuterCols;
            _resizeStartRows = Config.OuterRows;
            var members = CaptureMembers(false, out _resizeMetrics);
            if (members == null)
            {
                if (IsHandleCreated && Capture) Capture = false;
                return false;
            }
            _resizeEdge = edge;
            _resizeWorkArea = MonitorGrid.BestArea(MonitorGrid.GetAreas(_resizeMetrics),
                new Point(_resizeStartCol, _resizeStartRow), new Size(_resizeStartCols, _resizeStartRows), _resizeMetrics);
            _resizeTracks = new List<ResizeMemberTrack>(members.Count);
            foreach (var ic in members)
            {
                _resizeTracks.Add(new ResizeMemberTrack
                {
                    Identity = ic.Identity,
                    Col = ic.GridCol(_resizeMetrics),
                    Row = ic.GridRow(_resizeMetrics),
                    CurrentClientPos = ic.ClientPosition,
                    AnimTargetClientPos = ic.ClientPosition
                });
            }
            CaptureOrFinish();
            return _resizeEdge != ResizeEdge.None;
        }

        private bool StartActualDrag()
        {
            _dragPending = false;
            if (!_manager.PrepareFenceInteraction())
            {
                if (IsHandleCreated && Capture) Capture = false;
                return false;
            }
            _isDragOverlapping = false;
            _dragReleasePending = false;
            _dragRelocated = false;
            _lastDragCandidate = null;
            _dragStartOuterCol = Config.OuterCol;
            _dragStartOuterRow = Config.OuterRow;
            _dragStepCol = 0;
            _dragStepRow = 0;
            _capturedMembers = CaptureMembers(true, out _dragMetrics);
            if (_capturedMembers == null)
            {
                if (IsHandleCreated && Capture) Capture = false;
                return false;
            }

            _isDragging = true;
            if (_dragWorkAreas == null || _dragWorkAreas.Length == 0)
                _dragWorkAreas = MonitorGrid.GetAreas(_dragMetrics);
            EnterRigidDragSurface();
            CaptureOrFinish();
            return _isDragging;
        }

        private List<DesktopIconInfo> CaptureMembers(bool dragging, out DesktopGridMetrics metrics)
        {
            _startingInteraction = true;
            _releasedDuringStart = false;
            try
            {
                return dragging ? _manager.BeginFenceDrag(this, out metrics) : _manager.BeginFenceResize(this, out metrics);
            }
            finally { _startingInteraction = false; }
        }

        private void CaptureOrFinish()
        {
            if (_releasedDuringStart)
                SettleInteraction(_manager.CanCommitToCurrentDesktop(), false);
            else
                Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragPending)
            {
                Point cur = Cursor.Position;
                if (Math.Abs(cur.X - _dragStartMouse.X) >= 2 || Math.Abs(cur.Y - _dragStartMouse.Y) >= 2)
                {
                    if (StartActualDrag())
                        MoveDragTo(cur);
                }
                return;
            }

            if (_isDragging)
            {
                MoveDragTo(Cursor.Position);
                return;
            }

            if (_pendingResizeEdge != ResizeEdge.None)
            {
                Point cur = Cursor.Position;
                if (Math.Abs(cur.X - _resizeStartMouse.X) < 4 && Math.Abs(cur.Y - _resizeStartMouse.Y) < 4)
                    return;
                if (!StartActualResize(_pendingResizeEdge))
                    return;
            }

            if (_resizeEdge != ResizeEdge.None)
            {
                Point cur = Cursor.Position;
                int dCol = (int)Math.Round((double)(cur.X - _resizeStartMouse.X) / Math.Max(1, _resizeMetrics.CellWidth));
                int dRow = (int)Math.Round((double)(cur.Y - _resizeStartMouse.Y) / Math.Max(1, _resizeMetrics.CellHeight));

                if (_resizeWorkArea.IsEmpty) return;

                bool moveRight = _resizeEdge == ResizeEdge.Right || _resizeEdge == ResizeEdge.TopRight || _resizeEdge == ResizeEdge.BottomRight;
                bool moveBottom = _resizeEdge == ResizeEdge.Bottom || _resizeEdge == ResizeEdge.BottomLeft || _resizeEdge == ResizeEdge.BottomRight;
                bool moveLeft = _resizeEdge == ResizeEdge.Left || _resizeEdge == ResizeEdge.TopLeft || _resizeEdge == ResizeEdge.BottomLeft;
                bool moveTop = _resizeEdge == ResizeEdge.Top || _resizeEdge == ResizeEdge.TopLeft || _resizeEdge == ResizeEdge.TopRight;

                var start = new Rectangle(_resizeStartCol, _resizeStartRow, _resizeStartCols, _resizeStartRows);
                Rectangle candidate = MonitorGrid.Resize(start, _resizeWorkArea, dCol, dRow, moveLeft, moveTop, moveRight, moveBottom);
                int candCol = candidate.X;
                int candRow = candidate.Y;
                int candCols = candidate.Width;
                int candRows = candidate.Height;

                // Prevent resizing into any other zone and protect member capacity.
                // Test the combined diagonal candidate FIRST so reshaping wide-short <-> narrow-tall succeeds smoothly.
                int memberCount = _resizeTracks != null ? _resizeTracks.Count : 0;
                int newCol = Config.OuterCol;
                int newCols = Config.OuterCols;
                int newRow = Config.OuterRow;
                int newRows = Config.OuterRows;

                bool jointValid = (candCols - 2) * (candRows - 2) >= memberCount &&
                    !_manager.WouldOverlapAnyOtherFence(Config.Id, candCol, candRow, candCols, candRows, _resizeMetrics, DpiScale);
                if (jointValid)
                {
                    newCol = candCol;
                    newRow = candRow;
                    newCols = candCols;
                    newRows = candRows;
                }
                else
                {
                    if (candCol != Config.OuterCol || candCols != Config.OuterCols)
                    {
                        int step = candCols > Config.OuterCols ? -1 : 1;
                        int tryCols = candCols;
                        int rightEdge = _resizeStartCol + _resizeStartCols;
                        while (true)
                        {
                            int tryCol = moveLeft ? (rightEdge - tryCols) : candCol;
                            bool fitsMembers = (tryCols - 2) * (newRows - 2) >= memberCount;
                            if (fitsMembers && !_manager.WouldOverlapAnyOtherFence(Config.Id, tryCol, newRow, tryCols, newRows, _resizeMetrics, DpiScale))
                            {
                                newCol = tryCol;
                                newCols = tryCols;
                                break;
                            }
                            if (tryCols == Config.OuterCols) break;
                            tryCols += step;
                        }
                    }

                    if (candRow != Config.OuterRow || candRows != Config.OuterRows)
                    {
                        int step = candRows > Config.OuterRows ? -1 : 1;
                        int tryRows = candRows;
                        int bottomEdge = _resizeStartRow + _resizeStartRows;
                        while (true)
                        {
                            int tryRow = moveTop ? (bottomEdge - tryRows) : candRow;
                            bool fitsMembers = (newCols - 2) * (tryRows - 2) >= memberCount;
                            if (fitsMembers && !_manager.WouldOverlapAnyOtherFence(Config.Id, newCol, tryRow, newCols, tryRows, _resizeMetrics, DpiScale))
                            {
                                newRow = tryRow;
                                newRows = tryRows;
                                break;
                            }
                            if (tryRows == Config.OuterRows) break;
                            tryRows += step;
                        }
                    }
                }

                if (newCol != Config.OuterCol || newRow != Config.OuterRow || newCols != Config.OuterCols || newRows != Config.OuterRows)
                {
                    Config.OuterCol = newCol;
                    Config.OuterRow = newRow;
                    Config.OuterCols = newCols;
                    Config.OuterRows = newRows;

                    PlanResizeMemberTargets();
                    AnimateToBounds(ComputeGridBounds(_resizeMetrics));
                }
                return;
            }

            bool hm = GetMenuButtonRect().Contains(e.Location);
            if (hm != _hoverMenuBtn)
            {
                _hoverMenuBtn = hm;
                RedrawLayeredSurface();
            }

            ResizeEdge hoverEdge = HitTestResizeEdge(e.Location);
            if (hoverEdge != ResizeEdge.None)
                Cursor = EdgeToCursor(hoverEdge);
            else if (hm)
                Cursor = Cursors.Hand;
            else
                Cursor = Cursors.Default;
        }

        private void MoveDragTo(Point cursor)
        {
            if (!_isDragging) return;
            int cw = _dragMetrics.CellWidth, ch = _dragMetrics.CellHeight;
            if (cw <= 0 || ch <= 0) return;

            int dx = cursor.X - _dragStartMouse.X;
            int dy = cursor.Y - _dragStartMouse.Y;

            // Strict desktop grid stepping with positive Schmitt-trigger hysteresis (0.56 / 0.44):
            // guarantees zero back-and-forth oscillation at any mouse position or cell boundary.
            _dragStepCol = ComputeGridStep(dx, cw, _dragStepCol);
            _dragStepRow = ComputeGridStep(dy, ch, _dragStepRow);
            var desired = new Point(_dragStartOuterCol + _dragStepCol, _dragStartOuterRow + _dragStepRow);
            var size = new Size(Config.OuterCols, Config.OuterRows);
            Rectangle area = MonitorGrid.BestArea(_dragWorkAreas, desired, size, _dragMetrics);
            if (area.Width < size.Width || area.Height < size.Height) return;
            Point target = MonitorGrid.ClampOrigin(area, desired, size);
            if (_lastDragCandidate == target) return;
            _lastDragCandidate = target;

            Config.OuterCol = target.X;
            Config.OuterRow = target.Y;

            bool overlapping = _manager.WouldOverlapAnyOtherFence(
                Config.Id, target.X, target.Y, size.Width, size.Height, _dragMetrics, DpiScale);
            if (overlapping != _isDragOverlapping)
            {
                _isDragOverlapping = overlapping;
                RedrawLayeredSurface();
            }
            AnimateToBounds(ComputeGridBounds(_dragMetrics));
        }

        internal static int ComputeGridStep(int deltaPx, int cellPx, int currentStep)
        {
            double cells = deltaPx / (double)cellPx;
            const double threshold = 0.56;
            if (cells >= currentStep + threshold)
                return (int)Math.Floor(cells + (1.0 - threshold));
            if (cells <= currentStep - threshold)
                return (int)Math.Ceiling(cells - (1.0 - threshold));
            return currentStep;
        }

        private void PlanResizeMemberTargets()
        {
            if (_resizeTracks == null || _resizeTracks.Count == 0) return;

            var occupied = new HashSet<long>();
            Func<int, int, long> keyOf = (c, r) => (((long)c) << 32) | (uint)r;

            var displaced = new List<ResizeMemberTrack>();
            foreach (var tr in _resizeTracks)
            {
                if (Config.IsInInnerRect(tr.Col, tr.Row))
                    occupied.Add(keyOf(tr.Col, tr.Row));
                else
                    displaced.Add(tr);
            }

            foreach (var tr in displaced)
            {
                int bestCol = tr.Col;
                int bestRow = tr.Row;
                int bestDist = int.MaxValue;

                for (int r = Config.OuterRow + 1; r < Config.OuterRow + Config.OuterRows - 1; r++)
                {
                    for (int c = Config.OuterCol + 1; c < Config.OuterCol + Config.OuterCols - 1; c++)
                    {
                        if (!occupied.Contains(keyOf(c, r)))
                        {
                            int dist = Math.Abs(c - tr.Col) + Math.Abs(r - tr.Row);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestCol = c;
                                bestRow = r;
                            }
                        }
                    }
                }

                if (bestDist < int.MaxValue)
                {
                    int dx = (bestCol - tr.Col) * _resizeMetrics.CellWidth;
                    int dy = (bestRow - tr.Row) * _resizeMetrics.CellHeight;
                    tr.Col = bestCol;
                    tr.Row = bestRow;
                    tr.AnimTargetClientPos = new Point(tr.AnimTargetClientPos.X + dx, tr.AnimTargetClientPos.Y + dy);
                    occupied.Add(keyOf(bestCol, bestRow));
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (_dragPending || _pendingResizeEdge != ResizeEdge.None)
            {
                _dragPending = false;
                _pendingResizeEdge = ResizeEdge.None;
                if (IsHandleCreated && Capture) Capture = false;
                return;
            }
            if (_startingInteraction)
            {
                _releasedDuringStart = true;
                return;
            }
            if (_isDragging)
            {
                if (Capture) MoveDragTo(Cursor.Position);
                PrepareRigidDragRelease();
                AnimateToBounds(_animTargetBounds);
            }
            else if (_resizeEdge != ResizeEdge.None)
            {
                PrepareResizeRelease();
                AnimateToBounds(_animTargetBounds);
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture)
            {
                if (_dragPending || _pendingResizeEdge != ResizeEdge.None)
                {
                    _dragPending = false;
                    _pendingResizeEdge = ResizeEdge.None;
                    return;
                }
                if (_isDragging || _resizeEdge != ResizeEdge.None)
                    SettleInteraction(_manager.CanCommitToCurrentDesktop(), true);
            }
        }

        internal void RecoverMissedMouseUp()
        {
            if (_dragPending || _pendingResizeEdge != ResizeEdge.None)
            {
                if (!Capture || (Control.MouseButtons & MouseButtons.Left) == 0)
                {
                    _dragPending = false;
                    _pendingResizeEdge = ResizeEdge.None;
                    if (IsHandleCreated && Capture) Capture = false;
                }
                return;
            }
            if ((_isDragging || _resizeEdge != ResizeEdge.None) &&
                (!Capture || (Control.MouseButtons & MouseButtons.Left) == 0))
                SettleInteraction(_manager.CanCommitToCurrentDesktop(), true);
        }

        private void PrepareResizeRelease()
        {
            _resizeEdge = ResizeEdge.None;
            _releasePending = true;
            if (IsHandleCreated && Capture) Capture = false;
            _animTargetBounds = ComputeGridBounds(_resizeMetrics);
        }

        private void PrepareRigidDragRelease()
        {
            _isDragging = false;
            _dragReleasePending = true;
            if (IsHandleCreated && Capture) Capture = false;
            bool wasOverlapping = _isDragOverlapping;
            if (_isDragOverlapping)
            {
                _isDragOverlapping = false;
                RedrawLayeredSurface();
            }

            bool foundSpot;
            Rectangle[] areas = _dragWorkAreas != null && _dragWorkAreas.Length > 0
                ? _dragWorkAreas
                : MonitorGrid.GetAreas(_dragMetrics);
            Point resolved = FenceManager.FindNearestNonOverlappingOrigin(
                _manager.Config.Fences,
                Config.Id,
                Config.OuterCol, Config.OuterRow,
                Config.OuterCols, Config.OuterRows,
                _dragStartOuterCol, _dragStartOuterRow,
                areas, _dragMetrics, DpiScale,
                out foundSpot);

            Config.OuterCol = foundSpot ? resolved.X : _dragStartOuterCol;
            Config.OuterRow = foundSpot ? resolved.Y : _dragStartOuterRow;
            _animTargetBounds = ComputeGridBounds(_dragMetrics);
            _dragRelocated = wasOverlapping || Bounds != _animTargetBounds;
        }

        internal void SettleInteraction(bool applyIconPositions, bool enforce)
        {
            if (_completingInteraction) return;
            _dragPending = false;
            _pendingResizeEdge = ResizeEdge.None;
            if (_isDragging || _dragReleasePending)
            {
                if (_isDragging)
                {
                    if (applyIconPositions)
                    {
                        PrepareRigidDragRelease();
                    }
                    else
                    {
                        _isDragging = false;
                        if (IsHandleCreated && Capture) Capture = false;
                        Config.OuterCol = _dragStartOuterCol;
                        Config.OuterRow = _dragStartOuterRow;
                        _animTargetBounds = ComputeGridBounds(_dragMetrics);
                    }
                }
                CompleteRigidDrag(applyIconPositions, enforce);
                return;
            }
            if (!_releasePending)
            {
                if (_resizeEdge == ResizeEdge.None) return;
                PrepareResizeRelease();
            }
            CompleteResize(applyIconPositions, enforce);
        }

        private void CompleteRigidDrag(bool applyIconPositions, bool enforce)
        {
            if (_completingInteraction) return;
            _completingInteraction = true;
            _dragReleasePending = false;
            _isDragging = false;
            _animTimer.Stop();
            _animWatch.Stop();

            var captured = _capturedMembers;
            bool wereParked = _membersParked;
            bool relocated = _dragRelocated;
            Rectangle[] workAreas = _dragWorkAreas;
            _capturedMembers = null;
            _membersParked = false;
            _dragRelocated = false;
            _lastDragCandidate = null;
            _dragWorkAreas = null;
            bool moved = Config.OuterCol != _dragStartOuterCol || Config.OuterRow != _dragStartOuterRow;

            try
            {
                if (!applyIconPositions)
                {
                    Config.OuterCol = _dragStartOuterCol;
                    Config.OuterRow = _dragStartOuterRow;
                    moved = false;
                }

                if (_dragDpiChanged && IsHandleCreated && !Disposing && _manager.CanCommitToCurrentDesktop())
                {
                    UpdateDpiScale();
                    _animTargetBounds = ComputeGridBounds(_dragMetrics);
                }
                _dragDpiChanged = false;

                if (!Disposing && !IsDisposed && Bounds != _animTargetBounds)
                {
                    bool sizeChanged = Bounds.Width != _animTargetBounds.Width || Bounds.Height != _animTargetBounds.Height;
                    Bounds = _animTargetBounds;
                    if (sizeChanged) RedrawLayeredSurface();
                    SyncBackdrop();
                }

                // After an overlap recovery / spring glide, let DWM latch the final stopped glass
                // position for 1 vsync BEFORE touching SysListView32 so Explorer's desktop thread
                // is completely settled when icons return.
                if (relocated && !Disposing && !IsDisposed && _isDragSurfaceActive)
                {
                    try { NativeMethods.DwmFlush(); } catch { }
                }

                // Zero-flash DWM-synchronized handoff:
                // Use CanCommitToCurrentDesktop() (NOT DesktopReady!) so exiting or display/wallpaper
                // changes mid-drag ALWAYS restore parked member icons back from offscreen!
                bool snapRestored = false;
                if ((wereParked || moved) && _manager.CanCommitToCurrentDesktop())
                {
                    bool committed = _manager.CommitRigidDragDrop(
                        this, captured, _dragMetrics, workAreas,
                        _dragStartOuterCol, _dragStartOuterRow,
                        Config.OuterCol, Config.OuterRow,
                        applyIconPositions && enforce,
                        out snapRestored);
                    if (!committed)
                    {
                        Config.OuterCol = _dragStartOuterCol;
                        Config.OuterRow = _dragStartOuterRow;
                        moved = false;
                        _animTargetBounds = ComputeGridBounds(_dragMetrics);
                        if (!Disposing && !IsDisposed && Bounds != _animTargetBounds)
                        {
                            Bounds = _animTargetBounds;
                            SyncBackdrop();
                        }
                    }
                }

                // 2. Wait for DWM to ingest & present Explorer's synchronously painted frame.
                if (!Disposing && !IsDisposed && _isDragSurfaceActive && (wereParked || moved))
                {
                    int minFlushes = (relocated || snapRestored) ? 4 : 2;
                    int minMs = (relocated || snapRestored) ? 64 : 32;
                    var handoffWatch = Stopwatch.StartNew();
                    try
                    {
                        int flushes = 0;
                        while (flushes < minFlushes || handoffWatch.ElapsedMilliseconds < minMs)
                        {
                            if (NativeMethods.DwmFlush() != 0) break;
                            flushes++;
                            if (flushes >= 24) break;
                        }
                    }
                    catch { }
                }

                // 3. Immediately clear the drag icon overlay back to Alpha==0.
                _isDragOverlapping = false;
                ExitRigidDragSurface();
            }
            finally
            {
                try { _manager.EndFenceMutation(this, false, moved); }
                finally { _completingInteraction = false; }
            }
        }

        private void CompleteResize(bool applyIconPositions, bool enforce)
        {
            if (_completingInteraction) return;
            _completingInteraction = true;
            _releasePending = false;
            _animTimer.Stop();
            _animWatch.Stop();
            try
            {
                if (applyIconPositions)
                {
                    if (!Disposing && !IsDisposed && Bounds != _animTargetBounds)
                    {
                        Bounds = _animTargetBounds;
                        SyncBackdrop();
                        RedrawLayeredSurface();
                    }
                    ApplyResizeFrame(1.0, true);
                }
                else
                {
                    Config.OuterCol = _resizeStartCol;
                    Config.OuterRow = _resizeStartRow;
                    Config.OuterCols = _resizeStartCols;
                    Config.OuterRows = _resizeStartRows;
                }
            }
            finally
            {
                _resizeTracks = null;
                try { _manager.EndFenceMutation(this, enforce); }
                finally { _completingInteraction = false; }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverMenuBtn)
            {
                _hoverMenuBtn = false;
                RedrawLayeredSurface();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && e.Y <= HeaderHeight && !GetMenuButtonRect().Contains(e.Location))
            {
                BeginInlineRename();
            }
        }

        public void BeginInlineRename()
        {
            if (!_manager.PrepareFenceInteraction()) return;
            if (_renamePopup != null) return;
            FenceTheme theme = FenceTheme.Resolve(Config.ThemeId);
            int padL = (int)Math.Round(38 * DpiScale);
            int boxH = (int)Math.Round(26 * DpiScale);
            int boxW = Math.Max(80, GetMenuButtonRect().Left - padL - 6);

            var popup = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                TopMost = true,
                StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(Location.X + padL, Location.Y + (HeaderHeight - boxH) / 2, boxW, boxH)
            };
            var box = new TextBox
            {
                Text = Config.Title,
                Font = _titleFont,
                BackColor = theme.IsLightMenu ? Color.FromArgb(245, 248, 252) : Color.FromArgb(36, 42, 54),
                ForeColor = theme.IsLightMenu ? Color.FromArgb(24, 30, 40) : Color.White,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle
            };

            bool committed = false;
            Action<bool> finish = save =>
            {
                if (committed) return;
                committed = true;
                if (save)
                {
                    string val = box.Text.Trim();
                    if (!string.IsNullOrEmpty(val))
                    {
                        Config.Title = val;
                        _manager.SaveConfig();
                    }
                }
                _renamePopup = null;
                try { popup.Close(); popup.Dispose(); } catch { }
                RedrawLayeredSurface();
                SendToDesktopBottom();
            };

            box.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Enter) { ev.SuppressKeyPress = true; finish(true); }
                else if (ev.KeyCode == Keys.Escape) { ev.SuppressKeyPress = true; finish(false); }
            };
            box.LostFocus += (s, ev) => finish(true);

            popup.Controls.Add(box);
            _renamePopup = popup;
            popup.Show();
            popup.Activate();
            box.Focus();
            box.SelectAll();
        }

        private void ShowContextMenu(Point localPt)
        {
            FenceTheme theme = FenceTheme.Resolve(Config.ThemeId);
            bool menuLight = theme.IsLightMenu;
            var menu = Win11MenuRenderer.CreateWin11Menu(menuLight, theme.AccentColor, DpiScale);

            menu.Items.Add(Win11MenuRenderer.CreateMenuItem("重命名分区", "\uE8AC", DpiScale, (s, e) => BeginInlineRename()));

            var sortSub = Win11MenuRenderer.CreateMenuItem("整理此分区图标", "\uE8CB", DpiScale);
            Win11MenuRenderer.StyleSubMenu(sortSub, menuLight, theme.AccentColor, DpiScale);
            sortSub.DropDownItems.Add(Win11MenuRenderer.CreateMenuItem("按名称紧凑排列", null, DpiScale, (s, e) => _manager.SortFenceIcons(Config.Id, FenceSortMode.Name)));
            sortSub.DropDownItems.Add(Win11MenuRenderer.CreateMenuItem("按类型紧凑排列", null, DpiScale, (s, e) => _manager.SortFenceIcons(Config.Id, FenceSortMode.Type)));
            sortSub.DropDownItems.Add(Win11MenuRenderer.CreateMenuItem("按修改时间排列", null, DpiScale, (s, e) => _manager.SortFenceIcons(Config.Id, FenceSortMode.ModifiedTime)));
            menu.Items.Add(sortSub);
            menu.Items.Add(new ToolStripSeparator());

            var themeSub = Win11MenuRenderer.CreateMenuItem("液态玻璃风格", "\uE790", DpiScale);
            Win11MenuRenderer.StyleSubMenu(themeSub, menuLight, theme.AccentColor, DpiScale);
            foreach (var preset in FenceTheme.PresetList)
            {
                string tid = preset.Key;
                var mi = Win11MenuRenderer.CreateMenuItem(preset.Value, null, DpiScale, (s, e) =>
                {
                    Config.ThemeId = tid;
                    _manager.SaveConfig();
                    RefreshVisuals();
                });
                mi.Checked = string.Equals(Config.ThemeId, tid, StringComparison.OrdinalIgnoreCase);
                themeSub.DropDownItems.Add(mi);
            }
            menu.Items.Add(themeSub);

            var bgSub = Win11MenuRenderer.CreateMenuItem("玻璃通透度", "\uE706", DpiScale);
            Win11MenuRenderer.StyleSubMenu(bgSub, menuLight, theme.AccentColor, DpiScale);
            var bgOptions = new[]
            {
                new KeyValuePair<string, double>("关闭底板（纯透明）", 0.0),
                new KeyValuePair<string, double>("极清透（60%）", 0.60),
                new KeyValuePair<string, double>("标准液态玻璃（推荐）", 1.0),
                new KeyValuePair<string, double>("微润泽（140%）", 1.40),
            };
            foreach (var opt in bgOptions)
            {
                double val = opt.Value;
                var mi = Win11MenuRenderer.CreateMenuItem(opt.Key, null, DpiScale, (s, e) =>
                {
                    Config.BackdropOpacity = val;
                    _manager.SaveConfig();
                    RefreshVisuals();
                });
                mi.Checked = Math.Abs(Config.BackdropOpacity - val) < 0.08;
                bgSub.DropDownItems.Add(mi);
            }
            menu.Items.Add(bgSub);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Win11MenuRenderer.CreateMenuItem("新建分区", "\uE710", DpiScale, (s, e) => _manager.CreateNewFence()));
            menu.Items.Add(Win11MenuRenderer.CreateMenuItem("框选桌面区域新建分区", "\uE7A8", DpiScale, (s, e) => _manager.StartRegionDrawNewFence()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Win11MenuRenderer.CreateMenuItem("删除此分区（不影响桌面图标）", "\uE74D", DpiScale, (s, e) => _manager.DeleteFence(Config.Id)));

            menu.Closed += (s, e) =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke((Action)(() => { if (!menu.IsDisposed) menu.Dispose(); }));
                else if (!menu.IsDisposed)
                    menu.Dispose();
            };
            menu.Show(this, localPt);
            if (menu.IsHandleCreated) NativeMethods.SetForegroundWindow(menu.Handle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                if (_isDragging || _dragReleasePending || _resizeEdge != ResizeEdge.None || _releasePending)
                    SettleInteraction(_manager.CanCommitToCurrentDesktop(), false);
                DisposeDragVisuals();
                if (_animTimer != null)
                {
                    _animTimer.Stop();
                    _animTimer.Dispose();
                }
                if (_renamePopup != null)
                {
                    try { _renamePopup.Close(); _renamePopup.Dispose(); } catch { }
                    _renamePopup = null;
                }
                if (_titleFont != null) _titleFont.Dispose();
                if (Backdrop != null && !Backdrop.IsDisposed)
                {
                    Backdrop.Close();
                    Backdrop.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
