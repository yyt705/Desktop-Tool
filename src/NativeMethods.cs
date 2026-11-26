using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DeskFences
{
    internal static class NativeMethods
    {
        // Window Messages
        public const int WM_SETREDRAW = 0x000B;
        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const int WM_DPICHANGED = 0x02E0;
        public const uint GW_HWNDNEXT = 2;
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        // Window Styles
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int GWLP_HWNDPARENT = -8;
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_POPUP = 0x80000000;
        public const uint WS_CLIPSIBLINGS = 0x04000000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        // SetWindowPos
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_FRAMECHANGED = 0x0020;

        public const uint SMTO_NORMAL = 0x0000;
        public const int WM_SPAWN_WORKER = 0x052C;

        // ListView messages & styles
        public const int LVM_FIRST = 0x1000;
        public const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
        public const int LVM_GETITEMRECT = LVM_FIRST + 14;
        public const int LVM_GETITEMPOSITION = LVM_FIRST + 16;
        public const int LVM_GETEDITCONTROL = LVM_FIRST + 24;
        public const int LVM_GETITEMSTATE = LVM_FIRST + 44;
        public const int LVM_SETITEMPOSITION32 = LVM_FIRST + 49;
        public const int LVM_GETITEMSPACING = LVM_FIRST + 51;
        public const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
        public const int LVM_GETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 55;
        public const int LVM_GETITEMTEXTW = LVM_FIRST + 115;
        public const int LVM_GETITEMW = LVM_FIRST + 75;

        public const int LVIR_BOUNDS = 0;
        public const int LVIR_ICON = 1;
        public const int LVIR_LABEL = 2;
        public const uint LVIF_TEXT = 0x0001;
        public const uint LVIF_STATE = 0x0008;
        public const uint LVIS_SELECTED = 0x0002;
        public const int LVS_AUTOARRANGE = 0x0100;
        public const int LVS_EX_SNAPTOGRID = 0x00080000;

        public const int SHIL_LARGE = 0x0;
        public const int SHIL_EXTRALARGE = 0x2;
        public const int SHIL_JUMBO = 0x4;
        public const int ILD_TRANSPARENT = 0x00000001;

        // Process memory access
        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint PROCESS_VM_WRITE = 0x0020;
        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_RESERVE = 0x2000;
        public const uint MEM_RELEASE = 0x8000;
        public const uint PAGE_READWRITE = 0x04;

        // Region ops
        public const int RGN_AND = 1;
        public const int RGN_DIFF = 4;

        // DWM Rounded Corners
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        // DPI Awareness Context
        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        // Mouse key state & low-level passive right-click hook
        public const int VK_LBUTTON = 0x01;
        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const uint GA_ROOT = 2;
        public const uint EVENT_SYSTEM_MENUEND = 0x0005;
        public const int LVM_GETNEXTITEM = LVM_FIRST + 12;
        public const int LVNI_FOCUSED = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct MENUBARINFO
        {
            public int cbSize;
            public RECT rcBar;
            public IntPtr hMenu, hwndMenu, hwndSubMenu;
            public uint flags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetMenuBarInfo(IntPtr hwnd, int objectId, int item, ref MENUBARINFO info);
        [DllImport("user32.dll")]
        private static extern int MenuItemFromPoint(IntPtr hwnd, IntPtr menu, POINT point);
        [DllImport("user32.dll")]
        private static extern bool GetMenuItemRect(IntPtr hwnd, IntPtr menu, uint index, out RECT rect);
        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr menu);
        [DllImport("user32.dll")]
        private static extern IntPtr GetSubMenu(IntPtr menu, int index);
        [DllImport("user32.dll")]
        private static extern uint GetMenuState(IntPtr menu, uint index, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetMenuString(IntPtr menu, uint index, StringBuilder text, int size, uint flags);
        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint thread, EnumWindowsProc callback, IntPtr data);
        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        private static IntPtr ClassicPopupMenu(IntPtr hwnd)
        {
            var name = new StringBuilder(32);
            if (GetClassName(hwnd, name, name.Capacity) == 0 || name.ToString() != "#32768") return IntPtr.Zero;
            var info = new MENUBARINFO { cbSize = Marshal.SizeOf(typeof(MENUBARINFO)) };
            return GetMenuBarInfo(hwnd, -4, 0, ref info) ? info.hMenu : IntPtr.Zero; // OBJID_CLIENT
        }

        private static string MenuCaption(IntPtr menu, int index)
        {
            var caption = new StringBuilder(160);
            GetMenuString(menu, (uint)index, caption, caption.Capacity, 0x400); // MF_BYPOSITION
            string text = caption.ToString().Split('\t')[0].Replace("&", "").Trim();
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s*[（(][a-zA-Z][)）]$", "").Trim();
        }

        internal static DesktopMenuCommand ReadClassicMenuItem(IntPtr window, POINT point, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            uint pid;
            uint thread = GetWindowThreadProcessId(window, out pid);
            IntPtr menu = ClassicPopupMenu(window);
            if (menu == IntPtr.Zero || thread == 0) return DesktopMenuCommand.None;
            int index = MenuItemFromPoint(IntPtr.Zero, menu, point);
            RECT rect;
            if (index < 0 || GetSubMenu(menu, index) != IntPtr.Zero || !GetMenuItemRect(IntPtr.Zero, menu, (uint)index, out rect))
                return DesktopMenuCommand.None;
            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return ClassifyClassicItem(menu, index, thread);
        }

        private static DesktopMenuCommand ClassifyClassicItem(IntPtr menu, int index, uint thread)
        {
            uint state = GetMenuState(menu, (uint)index, 0x400);
            if ((state & 0x803) != 0) return DesktopMenuCommand.None; // MF_SEPARATOR, MF_DISABLED, MF_GRAYED
            string caption = MenuCaption(menu, index);
            if (FenceManager.ClassifyMenuItem(caption, false) == DesktopMenuCommand.Paste)
                return DesktopMenuCommand.Paste;
            bool inNew = false;
            EnumThreadWindows(thread, delegate(IntPtr other, IntPtr unused)
            {
                IntPtr parent = ClassicPopupMenu(other);
                if (parent == IntPtr.Zero) return true;
                for (int i = 0; i < GetMenuItemCount(parent); i++)
                {
                    if (GetSubMenu(parent, i) != menu) continue;
                    inNew = FenceManager.IsNewSubmenu(MenuCaption(parent, i));
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return FenceManager.ClassifyMenuItem(caption, inNew);
        }

        internal sealed class HighlightedMenuItem
        {
            public IntPtr Window, Menu;
            public int Index;
            public DesktopMenuCommand Command;

            public bool IsActive()
            {
                // MF_HILITE must still be set on the same enabled leaf at the instant Enter is pressed.
                return IsWindowVisible(Window) && (GetMenuState(Menu, (uint)Index, 0x400) & 0x883) == 0x80 &&
                    GetSubMenu(Menu, Index) == IntPtr.Zero;
            }
        }

        internal static HighlightedMenuItem ReadHighlightedMenuItem(uint thread)
        {
            HighlightedMenuItem match = null;
            EnumThreadWindows(thread, delegate(IntPtr window, IntPtr unused)
            {
                if (!IsWindowVisible(window)) return true;
                IntPtr menu = ClassicPopupMenu(window);
                if (menu == IntPtr.Zero) return true;
                int count = GetMenuItemCount(menu);
                for (int i = 0; i < count; ++i)
                {
                    var item = new HighlightedMenuItem { Window = window, Menu = menu, Index = i };
                    if (!item.IsActive()) continue;
                    item.Command = ClassifyClassicItem(menu, i, thread);
                    if (item.Command != DesktopMenuCommand.None) match = item;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return match;
        }

        // WinEvent hook constants (out-of-context, zero DLL injection)
        public const uint EVENT_OBJECT_CREATE = 0x8000;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint EVENT_OBJECT_REORDER = 0x8004;
        public const uint EVENT_OBJECT_FOCUS = 0x8005;
        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        public delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // 64-bit LVITEMW layout for cross-process LVM_GETITEMTEXTW
        [StructLayout(LayoutKind.Sequential)]
        public struct LVITEM64
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public int _pad1;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public int _pad2;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
            public int _pad3;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        public const uint LWA_ALPHA = 0x00000002;
        public const int SW_HIDE = 0;
        public const uint PW_RENDERFULLCONTENT = 0x00000002;

        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const uint ULW_ALPHA = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            ref POINT pptDst,
            ref SIZE psize,
            IntPtr hdcSrc,
            ref POINT pptSrc,
            int crKey,
            ref BLENDFUNCTION pblend,
            uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// Presents a 32-bit premultiplied ARGB bitmap onto a WS_EX_LAYERED window via UpdateLayeredWindow.
        /// Pixels with Alpha==0 are automatically 100% mouse click-through; translucent pixels composite
        /// directly over LIVE Wallpaper Engine / desktop at 60-120 FPS with silky anti-aliased rounded corners!
        /// </summary>
        public static bool PresentLayeredBitmap(IntPtr hwnd, Bitmap premulBmp, int x, int y)
        {
            if (hwnd == IntPtr.Zero || premulBmp == null || premulBmp.Width <= 0 || premulBmp.Height <= 0)
                return false;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = premulBmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);

                var size = new SIZE { cx = premulBmp.Width, cy = premulBmp.Height };
                var ptSrc = new POINT { X = 0, Y = 0 };
                var ptDst = new POINT { X = x, Y = y };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                if (oldBitmap == IntPtr.Zero || oldBitmap == new IntPtr(-1))
                    return false;
                return UpdateLayeredWindow(hwnd, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero && oldBitmap != new IntPtr(-1)) SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hWnd, ref RECT lpRect, bool bErase);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        public const uint RDW_INVALIDATE = 0x0001;
        public const uint RDW_ALLCHILDREN = 0x0080;
        public const uint RDW_UPDATENOW = 0x0100;

        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("gdi32.dll")]
        public static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

        [DllImport("gdi32.dll")]
        public static extern int SelectClipRgn(IntPtr hdc, IntPtr hrgn);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        public static extern int DwmFlush();

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private static readonly object _timerResLock = new object();
        private static int _highResTimerUsers;

        public static void AcquireHighResTimer()
        {
            lock (_timerResLock)
            {
                if (_highResTimerUsers == 0)
                {
                    try { timeBeginPeriod(1); } catch { }
                }
                _highResTimerUsers++;
            }
        }

        public static void ReleaseHighResTimer()
        {
            lock (_timerResLock)
            {
                if (_highResTimerUsers <= 0) return;
                _highResTimerUsers--;
                if (_highResTimerUsers == 0)
                {
                    try { timeEndPeriod(1); } catch { }
                }
            }
        }

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

        internal struct CaptureDiag
        {
            public bool UsedTrueIconAlpha;
            public bool UsedTrueLabelAlpha;
            public bool UsedFallbackFullContent;
        }

        public static Bitmap CaptureNativeListViewIconsTransparent(
            IntPtr listViewHwnd,
            Rectangle bodyScreenRect,
            List<DesktopIconInfo> members)
        {
            CaptureDiag unused;
            return CaptureNativeListViewIconsTransparent(listViewHwnd, bodyScreenRect, members, out unused);
        }

        internal static Bitmap CaptureNativeListViewIconsTransparent(
            IntPtr listViewHwnd,
            Rectangle bodyScreenRect,
            List<DesktopIconInfo> members,
            out CaptureDiag diag)
        {
            diag = new CaptureDiag();
            if (listViewHwnd == IntPtr.Zero || members == null || members.Count == 0 ||
                bodyScreenRect.Width <= 4 || bodyScreenRect.Height <= 4)
                return null;

            RECT lvRc;
            if (!GetWindowRect(listViewHwnd, out lvRc)) return null;
            int lvW = lvRc.Right - lvRc.Left;
            int lvH = lvRc.Bottom - lvRc.Top;
            if (lvW <= 4 || lvH <= 4) return null;

            // Precompute union bounding box (in screen coords) for each member so background-transparency
            // probing NEVER accidentally samples on top of packed icons, labels, or selection boxes!
            var memberUnionRects = new Rectangle[members.Count];
            var memberExcludeRects = new Rectangle[members.Count];
            int outW = bodyScreenRect.Width;
            int outH = bodyScreenRect.Height;
            const int ringPad = 14;
            Rectangle neededScreen = Rectangle.Inflate(bodyScreenRect, ringPad + 4, ringPad + 4);
            for (int i = 0; i < members.Count; i++)
            {
                var ic = members[i];
                Rectangle u = Rectangle.Union(ic.ScreenBounds, Rectangle.Union(ic.IconBounds, ic.LabelBounds));
                memberUnionRects[i] = Rectangle.Inflate(u, 4, 4);
                memberExcludeRects[i] = Rectangle.Inflate(u, 6, 6);
                neededScreen = Rectangle.Union(neededScreen, memberExcludeRects[i]);
            }

            Rectangle cropRect = Rectangle.Intersect(
                new Rectangle(0, 0, lvW, lvH),
                new Rectangle(neededScreen.X - lvRc.Left, neededScreen.Y - lvRc.Top, neededScreen.Width, neededScreen.Height));
            if (cropRect.Width <= 0 || cropRect.Height <= 0) return null;

            int cropX = cropRect.X;
            int cropY = cropRect.Y;
            int cropW = cropRect.Width;
            int cropH = cropRect.Height;
            int srcStride = cropW * 4;
            int offX = (bodyScreenRect.X - lvRc.Left) - cropX;
            int offY = (bodyScreenRect.Y - lvRc.Top) - cropY;

            // Primary capture: ALWAYS try PrintWindow(hwnd, dc, 0) first to preserve SysListView32's
            // TRUE 32-bit ARGB per-pixel alpha channel (never permanently latch fallback mode!).
            byte[] buf = CaptureListViewOverSolid(listViewHwnd, lvW, lvH, cropRect, 0, 0);
            bool hasTrueIconAlpha = false;
            bool hasTrueLabelAlpha = false;
            byte bgB = 0, bgG = 0, bgR = 0;

            Func<int, int, bool> insideAnyMember = (screenX, screenY) =>
            {
                for (int i = 0; i < memberExcludeRects.Length; i++)
                {
                    if (memberExcludeRects[i].Contains(screenX, screenY))
                        return true;
                }
                return false;
            };

            if (buf != null)
            {
                int bgSamples = 0, bgZeroAlpha = 0;
                long sumB = 0, sumG = 0, sumR = 0;
                Action<int, int> sampleBgAtScreen = (screenX, screenY) =>
                {
                    if (insideAnyMember(screenX, screenY)) return;
                    int sx = (screenX - lvRc.Left) - cropX;
                    int sy = (screenY - lvRc.Top) - cropY;
                    if (sx < 0 || sx >= cropW || sy < 0 || sy >= cropH) return;
                    int si = sy * srcStride + sx * 4;
                    bgSamples++;
                    if (buf[si + 3] == 0)
                    {
                        bgZeroAlpha++;
                        sumB += buf[si];
                        sumG += buf[si + 1];
                        sumR += buf[si + 2];
                    }
                };

                // 1. Sample empty non-icon pixels inside bodyScreenRect
                int stepX = Math.Max(4, outW / 20);
                int stepY = Math.Max(4, outH / 20);
                for (int dy = 2; dy < outH - 2; dy += stepY)
                {
                    for (int dx = 2; dx < outW - 2; dx += stepX)
                        sampleBgAtScreen(bodyScreenRect.X + dx, bodyScreenRect.Y + dy);
                }

                // 2. Also sample the guaranteed-empty outer border ring immediately outside bodyScreenRect
                //    so even a 100%-packed fence with every icon selected still probes pure empty background!
                int ringStepX = Math.Max(6, outW / 14);
                int ringStepY = Math.Max(6, outH / 14);
                for (int dx = 0; dx <= outW; dx += ringStepX)
                {
                    sampleBgAtScreen(bodyScreenRect.Left + dx, bodyScreenRect.Top - ringPad);
                    sampleBgAtScreen(bodyScreenRect.Left + dx, bodyScreenRect.Bottom + ringPad);
                }
                for (int dy = 0; dy <= outH; dy += ringStepY)
                {
                    sampleBgAtScreen(bodyScreenRect.Left - ringPad, bodyScreenRect.Top + dy);
                    sampleBgAtScreen(bodyScreenRect.Right + ringPad, bodyScreenRect.Top + dy);
                }

                if (bgZeroAlpha > 0)
                {
                    bgB = (byte)(sumB / bgZeroAlpha);
                    bgG = (byte)(sumG / bgZeroAlpha);
                    bgR = (byte)(sumR / bgZeroAlpha);
                }

                bool bgIsTransparent = bgSamples > 0 && (bgZeroAlpha * 10 >= bgSamples * 6);
                if (bgIsTransparent)
                {
                    int iconAlphaCount = 0;
                    int labelAlphaCount = 0;
                    foreach (var ic in members)
                    {
                        int ix0 = Math.Max(0, ic.IconBounds.Left - bodyScreenRect.Left);
                        int iy0 = Math.Max(0, ic.IconBounds.Top - bodyScreenRect.Top);
                        int ix1 = Math.Min(outW, ic.IconBounds.Right - bodyScreenRect.Left);
                        int iy1 = Math.Min(outH, Math.Min(ic.IconBounds.Bottom, ic.LabelBounds.Top - 1) - bodyScreenRect.Top);
                        for (int dy = iy0; dy < iy1 && iconAlphaCount < 16; dy++)
                        {
                            int sy = offY + dy;
                            if (sy < 0 || sy >= cropH) continue;
                            for (int dx = ix0; dx < ix1 && iconAlphaCount < 16; dx++)
                            {
                                int sx = offX + dx;
                                if (sx < 0 || sx >= cropW) continue;
                                if (buf[sy * srcStride + sx * 4 + 3] > 10) iconAlphaCount++;
                            }
                        }

                        int lx0 = Math.Max(0, ic.LabelBounds.Left - bodyScreenRect.Left);
                        int ly0 = Math.Max(0, ic.LabelBounds.Top - bodyScreenRect.Top);
                        int lx1 = Math.Min(outW, ic.LabelBounds.Right - bodyScreenRect.Left);
                        int ly1 = Math.Min(outH, ic.LabelBounds.Bottom - bodyScreenRect.Top);
                        for (int dy = ly0; dy < ly1 && labelAlphaCount < 8; dy++)
                        {
                            int sy = offY + dy;
                            if (sy < 0 || sy >= cropH) continue;
                            for (int dx = lx0; dx < lx1 && labelAlphaCount < 8; dx++)
                            {
                                int sx = offX + dx;
                                if (sx < 0 || sx >= cropW) continue;
                                if (buf[sy * srcStride + sx * 4 + 3] > 10) labelAlphaCount++;
                            }
                        }
                    }
                    hasTrueIconAlpha = iconAlphaCount >= 4;
                    hasTrueLabelAlpha = labelAlphaCount >= 1;
                }
            }

            // Fallback to PW_RENDERFULLCONTENT only if PrintWindow(0) truly produced no icon alpha
            bool usedFallback = false;
            if (buf == null || !hasTrueIconAlpha)
            {
                byte[] fullBuf = CaptureListViewOverSolid(listViewHwnd, lvW, lvH, cropRect, 0, PW_RENDERFULLCONTENT);
                if (fullBuf != null)
                {
                    buf = fullBuf;
                    hasTrueIconAlpha = false;
                    hasTrueLabelAlpha = false;
                    usedFallback = true;
                    bgB = bgG = bgR = 0;
                }
            }
            if (buf == null) return null;
            diag.UsedTrueIconAlpha = hasTrueIconAlpha;
            diag.UsedTrueLabelAlpha = hasTrueLabelAlpha;
            diag.UsedFallbackFullContent = usedFallback;

            var outBmp = new Bitmap(outW, outH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var bd = outBmp.LockBits(new Rectangle(0, 0, outW, outH),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                int dstStride = Math.Abs(bd.Stride);
                byte[] dstBuf = new byte[dstStride * outH];

                if (hasTrueIconAlpha && hasTrueLabelAlpha)
                {
                    // Bit-exact single-pass 32-bit premultiplied ARGB copy across every member's full
                    // union rect (icon + shortcut overlay + multi-line label + any selection highlight)
                    // with a visited mask so overlapping padded rects are NEVER double-blended!
                    bool[] copied = new bool[outW * outH];
                    for (int i = 0; i < members.Count; i++)
                    {
                        Rectangle mr = memberUnionRects[i];
                        int x0 = Math.Max(0, mr.Left - bodyScreenRect.Left);
                        int y0 = Math.Max(0, mr.Top - bodyScreenRect.Top);
                        int x1 = Math.Min(outW, mr.Right - bodyScreenRect.Left);
                        int y1 = Math.Min(outH, mr.Bottom - bodyScreenRect.Top);
                        if (x1 <= x0 || y1 <= y0) continue;

                        // Probe local transparent background around this icon in case static wallpaper was painted
                        long lSumB = 0, lSumG = 0, lSumR = 0;
                        int lCnt = 0;
                        for (int dx = x0; dx < x1; dx += 2)
                        {
                            int sx = offX + dx;
                            if (sx < 0 || sx >= cropW) continue;
                            int syTop = offY + y0;
                            if (syTop >= 0 && syTop < cropH)
                            {
                                int si = syTop * srcStride + sx * 4;
                                if (buf[si + 3] == 0) { lSumB += buf[si]; lSumG += buf[si + 1]; lSumR += buf[si + 2]; lCnt++; }
                            }
                            int syBot = offY + (y1 - 1);
                            if (syBot >= 0 && syBot < cropH)
                            {
                                int si = syBot * srcStride + sx * 4;
                                if (buf[si + 3] == 0) { lSumB += buf[si]; lSumG += buf[si + 1]; lSumR += buf[si + 2]; lCnt++; }
                            }
                        }
                        byte locBgB = lCnt > 0 ? (byte)(lSumB / lCnt) : bgB;
                        byte locBgG = lCnt > 0 ? (byte)(lSumG / lCnt) : bgG;
                        byte locBgR = lCnt > 0 ? (byte)(lSumR / lCnt) : bgR;
                        bool locNonBlack = locBgB > 4 || locBgG > 4 || locBgR > 4;

                        for (int dy = y0; dy < y1; dy++)
                        {
                            int sy = offY + dy;
                            if (sy < 0 || sy >= cropH) continue;
                            int rowMask = dy * outW;
                            int dstRow = dy * dstStride;
                            int srcRow = sy * srcStride;
                            for (int dx = x0; dx < x1; dx++)
                            {
                                int mi = rowMask + dx;
                                if (copied[mi]) continue;
                                copied[mi] = true;

                                int sx = offX + dx;
                                if (sx < 0 || sx >= cropW) continue;
                                int si = srcRow + sx * 4;
                                byte a = buf[si + 3];
                                if (a == 0) continue;

                                int b = buf[si], g = buf[si + 1], r = buf[si + 2];
                                if (locNonBlack && a < 255)
                                {
                                    int inv = 255 - a;
                                    b = Math.Max(0, b - (inv * locBgB + 127) / 255);
                                    g = Math.Max(0, g - (inv * locBgG + 127) / 255);
                                    r = Math.Max(0, r - (inv * locBgR + 127) / 255);
                                }
                                int di = dstRow + dx * 4;
                                dstBuf[di] = (byte)Math.Min(a, b);
                                dstBuf[di + 1] = (byte)Math.Min(a, g);
                                dstBuf[di + 2] = (byte)Math.Min(a, r);
                                dstBuf[di + 3] = a;
                            }
                        }
                    }

                    Marshal.Copy(dstBuf, 0, bd.Scan0, dstBuf.Length);
                    return outBmp;
                }

                Action<int, int, byte, byte, byte, byte> blendPixel = (dx, dy, pb, pg, pr, pa) =>
                {
                    if (dx < 0 || dx >= outW || dy < 0 || dy >= outH || pa == 0) return;
                    int di = dy * dstStride + dx * 4;
                    byte da = dstBuf[di + 3];
                    if (da == 0 || pa == 255)
                    {
                        dstBuf[di] = pb;
                        dstBuf[di + 1] = pg;
                        dstBuf[di + 2] = pr;
                        dstBuf[di + 3] = pa;
                    }
                    else
                    {
                        int invA = 255 - pa;
                        dstBuf[di] = (byte)Math.Min(255, pb + (dstBuf[di] * invA + 127) / 255);
                        dstBuf[di + 1] = (byte)Math.Min(255, pg + (dstBuf[di + 1] * invA + 127) / 255);
                        dstBuf[di + 2] = (byte)Math.Min(255, pr + (dstBuf[di + 2] * invA + 127) / 255);
                        dstBuf[di + 3] = (byte)Math.Min(255, pa + (da * invA + 127) / 255);
                    }
                };

                bool nonBlackBg = bgB > 8 || bgG > 8 || bgR > 8;

                foreach (var ic in members)
                {
                    // 1. Copy native icon artwork strictly above LabelBounds.Top - 1 (ZERO icon shadow!)
                    int x0 = Math.Max(0, (ic.IconBounds.Left - bodyScreenRect.Left) - 2);
                    int y0 = Math.Max(0, (ic.IconBounds.Top - bodyScreenRect.Top) - 2);
                    int x1 = Math.Min(outW, (ic.IconBounds.Right - bodyScreenRect.Left) + 2);
                    int y1 = Math.Min(outH, Math.Min(ic.IconBounds.Bottom, ic.LabelBounds.Top - 1) - bodyScreenRect.Top);

                    if (hasTrueIconAlpha)
                    {
                        for (int dy = y0; dy < y1; dy++)
                        {
                            int sy = offY + dy;
                            if (sy < 0 || sy >= cropH) continue;
                            for (int dx = x0; dx < x1; dx++)
                            {
                                int sx = offX + dx;
                                if (sx < 0 || sx >= cropW) continue;
                                int si = sy * srcStride + sx * 4;
                                byte a = buf[si + 3];
                                if (a == 0) continue;
                                int b = buf[si], g = buf[si + 1], r = buf[si + 2];
                                if (nonBlackBg && a < 255)
                                {
                                    int inv = 255 - a;
                                    b = Math.Max(0, b - (inv * bgB + 127) / 255);
                                    g = Math.Max(0, g - (inv * bgG + 127) / 255);
                                    r = Math.Max(0, r - (inv * bgR + 127) / 255);
                                }
                                blendPixel(dx, dy,
                                    (byte)Math.Min(a, b),
                                    (byte)Math.Min(a, g),
                                    (byte)Math.Min(a, r),
                                    a);
                            }
                        }
                    }
                    else
                    {
                        int rw = x1 - x0;
                        int rh = y1 - y0;
                        if (rw > 0 && rh > 0)
                        {
                            bool[] bgMask = new bool[rw * rh];
                            var q = new Queue<int>();
                            Func<int, int, bool> isPureBlack = (lx, ly) =>
                            {
                                int sx = offX + x0 + lx;
                                int sy = offY + y0 + ly;
                                if (sx < 0 || sx >= cropW || sy < 0 || sy >= cropH) return true;
                                int si = sy * srcStride + sx * 4;
                                return buf[si] <= 6 && buf[si + 1] <= 6 && buf[si + 2] <= 6;
                            };

                            for (int lx = 0; lx < rw; lx++)
                            {
                                if (isPureBlack(lx, 0)) { bgMask[lx] = true; q.Enqueue(lx); }
                                int bi = (rh - 1) * rw + lx;
                                if (!bgMask[bi] && isPureBlack(lx, rh - 1)) { bgMask[bi] = true; q.Enqueue(bi); }
                            }
                            for (int ly = 1; ly < rh - 1; ly++)
                            {
                                int li = ly * rw;
                                if (!bgMask[li] && isPureBlack(0, ly)) { bgMask[li] = true; q.Enqueue(li); }
                                int ri = ly * rw + (rw - 1);
                                if (!bgMask[ri] && isPureBlack(rw - 1, ly)) { bgMask[ri] = true; q.Enqueue(ri); }
                            }
                            while (q.Count > 0)
                            {
                                int cur = q.Dequeue();
                                int cx = cur % rw;
                                int cy = cur / rw;
                                if (cx > 0 && !bgMask[cur - 1] && isPureBlack(cx - 1, cy)) { bgMask[cur - 1] = true; q.Enqueue(cur - 1); }
                                if (cx + 1 < rw && !bgMask[cur + 1] && isPureBlack(cx + 1, cy)) { bgMask[cur + 1] = true; q.Enqueue(cur + 1); }
                                if (cy > 0 && !bgMask[cur - rw] && isPureBlack(cx, cy - 1)) { bgMask[cur - rw] = true; q.Enqueue(cur - rw); }
                                if (cy + 1 < rh && !bgMask[cur + rw] && isPureBlack(cx, cy + 1)) { bgMask[cur + rw] = true; q.Enqueue(cur + rw); }
                            }

                            for (int ly = 0; ly < rh; ly++)
                            {
                                int dy = y0 + ly;
                                int sy = offY + dy;
                                if (sy < 0 || sy >= cropH) continue;
                                for (int lx = 0; lx < rw; lx++)
                                {
                                    if (bgMask[ly * rw + lx]) continue;
                                    int dx = x0 + lx;
                                    int sx = offX + dx;
                                    if (sx < 0 || sx >= cropW) continue;
                                    int si = sy * srcStride + sx * 4;
                                    byte b = buf[si], g = buf[si + 1], r = buf[si + 2];
                                    byte maxC = Math.Max(r, Math.Max(g, b));
                                    byte a = maxC >= 28 ? (byte)255 : (byte)Math.Min(255, maxC * 9);
                                    blendPixel(dx, dy,
                                        (byte)Math.Min(a, b),
                                        (byte)Math.Min(a, g),
                                        (byte)Math.Min(a, r),
                                        a);
                                }
                            }
                        }
                    }

                    // 2. Copy native desktop label cleanly (handles both light and dark label text)
                    int lx0 = Math.Max(0, (ic.LabelBounds.Left - bodyScreenRect.Left) - 4);
                    int ly0 = Math.Max(0, (ic.LabelBounds.Top - bodyScreenRect.Top) - 1);
                    int lx1 = Math.Min(outW, (ic.LabelBounds.Right - bodyScreenRect.Left) + 4);
                    int ly1 = Math.Min(outH, (ic.LabelBounds.Bottom - bodyScreenRect.Top) + 2);

                    if (hasTrueLabelAlpha)
                    {
                        for (int dy = ly0; dy < ly1; dy++)
                        {
                            int sy = offY + dy;
                            if (sy < 0 || sy >= cropH) continue;
                            for (int dx = lx0; dx < lx1; dx++)
                            {
                                int sx = offX + dx;
                                if (sx < 0 || sx >= cropW) continue;
                                int si = sy * srcStride + sx * 4;
                                byte a = buf[si + 3];
                                if (a == 0) continue;
                                blendPixel(dx, dy,
                                    (byte)Math.Min(a, buf[si]),
                                    (byte)Math.Min(a, buf[si + 1]),
                                    (byte)Math.Min(a, buf[si + 2]),
                                    a);
                            }
                        }
                    }
                    else
                    {
                        // Extract pure white anti-aliased glyph directly from luminance (subtle 1px Win11 text shadow only)
                        for (int dy = ly0; dy < ly1; dy++)
                        {
                            int sy = offY + dy;
                            if (sy < 0 || sy >= cropH) continue;
                            for (int dx = lx0; dx < lx1; dx++)
                            {
                                int sx = offX + dx;
                                if (sx < 0 || sx >= cropW) continue;
                                int si = sy * srcStride + sx * 4;
                                int db = Math.Max(0, buf[si] - bgB);
                                int dg = Math.Max(0, buf[si + 1] - bgG);
                                int dr = Math.Max(0, buf[si + 2] - bgR);
                                byte lum = (byte)Math.Min(255, Math.Max(dr, Math.Max(dg, db)));
                                if (lum > 24)
                                {
                                    byte sa = (byte)((lum * 85) / 255);
                                    blendPixel(dx, dy + 1, 0, 0, 0, sa);
                                }
                            }
                        }
                        for (int dy = ly0; dy < ly1; dy++)
                        {
                            int sy = offY + dy;
                            if (sy < 0 || sy >= cropH) continue;
                            for (int dx = lx0; dx < lx1; dx++)
                            {
                                int sx = offX + dx;
                                if (sx < 0 || sx >= cropW) continue;
                                int si = sy * srcStride + sx * 4;
                                int db = Math.Max(0, buf[si] - bgB);
                                int dg = Math.Max(0, buf[si + 1] - bgG);
                                int dr = Math.Max(0, buf[si + 2] - bgR);
                                byte lum = (byte)Math.Min(255, Math.Max(dr, Math.Max(dg, db)));
                                if (lum > 14)
                                    blendPixel(dx, dy, lum, lum, lum, lum);
                            }
                        }
                    }
                }

                Marshal.Copy(dstBuf, 0, bd.Scan0, dstBuf.Length);
            }
            finally
            {
                outBmp.UnlockBits(bd);
            }
            return outBmp;
        }

        private static byte[] CaptureListViewOverSolid(IntPtr listViewHwnd, int lvW, int lvH, Rectangle cropRect, byte fillByte, uint printFlags)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hDib = IntPtr.Zero;
            IntPtr oldObj = IntPtr.Zero;
            IntPtr ppvBits = IntPtr.Zero;
            try
            {
                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = lvW;
                bmi.bmiHeader.biHeight = -lvH;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;

                hDib = CreateDIBSection(memDc, ref bmi, 0, out ppvBits, IntPtr.Zero, 0);
                if (hDib == IntPtr.Zero || ppvBits == IntPtr.Zero) return null;
                oldObj = SelectObject(memDc, hDib);

                if (fillByte != 0)
                {
                    int rowBytes = lvW * 4;
                    byte[] fillRow = new byte[rowBytes];
                    for (int i = 0; i < rowBytes; i++) fillRow[i] = fillByte;
                    for (int y = 0; y < lvH; y++)
                        Marshal.Copy(fillRow, 0, IntPtr.Add(ppvBits, y * rowBytes), rowBytes);
                }

                if (!PrintWindow(listViewHwnd, memDc, printFlags))
                    return null;

                int srcStride = lvW * 4;
                int dstStride = cropRect.Width * 4;
                byte[] cropped = new byte[dstStride * cropRect.Height];
                int xOff = cropRect.X * 4;
                for (int row = 0; row < cropRect.Height; row++)
                {
                    IntPtr srcPtr = IntPtr.Add(ppvBits, (cropRect.Y + row) * srcStride + xOff);
                    Marshal.Copy(srcPtr, cropped, row * dstStride, dstStride);
                }
                return cropped;
            }
            catch { return null; }
            finally
            {
                if (oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
                if (hDib != IntPtr.Zero) DeleteObject(hDib);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        public static void EnablePerMonitorDpiV2()
        {
            try
            {
                if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                    SetProcessDPIAware();
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }
        }

        public static float GetWindowDpiScale(IntPtr hwnd)
        {
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi >= 96 && dpi <= 480)
                        return dpi / 96f;
                }
            }
            catch { }

            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return g.DpiX / 96f;
            }
            catch { return 1f; }
        }

        public static float GetDpiScaleForPoint(Point pt)
        {
            try
            {
                IntPtr hMon = MonitorFromPoint(new POINT { X = pt.X, Y = pt.Y }, MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero)
                {
                    uint dx, dy;
                    if (GetDpiForMonitor(hMon, 0, out dx, out dy) == 0 && dx >= 96 && dx <= 480)
                        return dx / 96f;
                }
            }
            catch { }
            return GetWindowDpiScale(IntPtr.Zero);
        }

        public static bool IsBackdropOrderedInInterlayer(IntPtr backdropHwnd, IntPtr parentHwnd, IntPtr insertAfterHwnd)
        {
            if (backdropHwnd == IntPtr.Zero || parentHwnd == IntPtr.Zero || !IsWindow(backdropHwnd)) return true;
            if (GetParent(backdropHwnd) != parentHwnd) return false;
            if (insertAfterHwnd == IntPtr.Zero || insertAfterHwnd == HWND_TOP) return true;
            var sb = new StringBuilder(32);
            IntPtr cur = GetWindow(insertAfterHwnd, GW_HWNDNEXT);
            int guard = 0;
            while (cur != IntPtr.Zero && ++guard < 256)
            {
                if (cur == backdropHwnd) return true;
                sb.Length = 0;
                if (GetClassName(cur, sb, sb.Capacity) > 0 && sb.ToString() == "WorkerW")
                    return false; // Wallpaper Engine / WorkerW slipped in front of our backdrop!
                cur = GetWindow(cur, GW_HWNDNEXT);
            }
            return false;
        }

        public static bool IsSystemLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val is int) return ((int)val) != 0;
                    }
                }
            }
            catch { }
            return true; // Default to Windows 11 Light theme
        }

        public static Color GetSystemAccentColor()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AccentColor");
                        if (val is int)
                        {
                            uint abgr = unchecked((uint)(int)val);
                            byte r = (byte)(abgr & 0xFF);
                            byte g = (byte)((abgr >> 8) & 0xFF);
                            byte b = (byte)((abgr >> 16) & 0xFF);
                            return Color.FromArgb(255, r, g, b);
                        }
                    }
                }
            }
            catch { }
            return Color.FromArgb(0, 103, 192); // Win11 default Fluent Blue
        }

        public static void ApplyWin11RoundedCorners(IntPtr hwnd)
        {
            try
            {
                int pref = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { }
        }

        /// <summary>
        /// Splits the desktop into Icons (SHELLDLL_DefView) and Wallpaper (WorkerW) via 0x052C,
        /// and returns the exact parent & Z-order anchor to place our folder backdrop strictly
        /// BETWEEN the desktop wallpaper and the native desktop icons (supports Win11 24H2/26300 & Win10/11 23H2).
        /// </summary>
        public static bool FindDesktopInterlayerHost(out IntPtr parentHwnd, out IntPtr insertAfterHwnd, bool requestWorker = true)
        {
            parentHwnd = IntPtr.Zero;
            insertAfterHwnd = IntPtr.Zero;

            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return false;

            IntPtr dummy;
            if (requestWorker)
                SendMessageTimeout(progman, WM_SPAWN_WORKER, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out dummy);

            // Case 1: Windows 11 24H2 / Build 26100+ / 26300 Raised Desktop
            // Both SHELLDLL_DefView (icons) and WorkerW (wallpaper) are direct children of Progman.
            IntPtr defViewInProgman = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr workerInProgman = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            if (defViewInProgman != IntPtr.Zero && workerInProgman != IntPtr.Zero)
            {
                parentHwnd = progman;
                insertAfterHwnd = defViewInProgman; // Directly behind icons, directly in front of wallpaper!
                return true;
            }

            // Case 2: Windows 10 / Windows 11 23H2 and earlier
            // Top-level WorkerW_A holds SHELLDLL_DefView; the next top-level WorkerW_B is the wallpaper layer.
            IntPtr defView, listView;
            IntPtr owner = FindDesktopHandles(out defView, out listView);
            IntPtr wallpaperWorker = listView == IntPtr.Zero ? IntPtr.Zero :
                FindWindowEx(IntPtr.Zero, owner, "WorkerW", null);
            uint shellPid, wallpaperPid;
            if (wallpaperWorker != IntPtr.Zero && GetWindowThreadProcessId(owner, out shellPid) != 0 &&
                GetWindowThreadProcessId(wallpaperWorker, out wallpaperPid) != 0 && shellPid == wallpaperPid)
            {
                parentHwnd = wallpaperWorker;
                insertAfterHwnd = HWND_TOP; // Top of wallpaper WorkerW (still behind SHELLDLL_DefView!)
                return true;
            }

            return false;
        }

        public static IntPtr FindDesktopHandles(out IntPtr defView, out IntPtr listView)
        {
            defView = IntPtr.Zero;
            listView = IntPtr.Zero;

            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr dv = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero)
                {
                    IntPtr lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
                    if (lv != IntPtr.Zero)
                    {
                        defView = dv;
                        listView = lv;
                        return progman;
                    }
                }
            }

            IntPtr foundWorker = IntPtr.Zero;
            IntPtr foundDefView = IntPtr.Zero;
            IntPtr foundListView = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                var sb = new StringBuilder(64);
                GetClassName(hWnd, sb, sb.Capacity);
                if (sb.ToString() == "WorkerW")
                {
                    IntPtr dv = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (dv != IntPtr.Zero)
                    {
                        IntPtr lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
                        if (lv != IntPtr.Zero)
                        {
                            foundWorker = hWnd;
                            foundDefView = dv;
                            foundListView = lv;
                            return false;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            defView = foundDefView;
            listView = foundListView;
            return foundWorker != IntPtr.Zero ? foundWorker : progman;
        }
    }

    public struct DesktopIconInfo
    {
        public int Index;
        public string Identity;
        public string ParsingName;
        public bool Selected;
        public string Name;
        public Point ClientPosition;   // LVM_GETITEMPOSITION (SysListView32 client coords)
        public Rectangle ScreenBounds; // LVIR_BOUNDS in physical screen pixels
        public Rectangle IconBounds;   // LVIR_ICON in physical screen pixels
        public Rectangle LabelBounds;  // LVIR_LABEL in physical screen pixels
        internal bool EstimatedBounds;

        public int GridCol(DesktopGridMetrics m)
        {
            return (int)Math.Round((double)(ScreenBounds.Left - m.OriginX) / Math.Max(1, m.CellWidth));
        }

        public int GridRow(DesktopGridMetrics m)
        {
            return (int)Math.Round((double)(ScreenBounds.Top - m.OriginY) / Math.Max(1, m.CellHeight));
        }
    }

    public struct DesktopGridMetrics
    {
        public int CellWidth;
        public int CellHeight;
        public int OriginX;
        public int OriginY;
        public bool AutoArrangeEnabled;
        public bool SnapToGridEnabled;
    }

    /// <summary>
    /// Interacts directly with Windows Explorer's native desktop SysListView32
    /// and calibrates the exact physical pixel grid across any resolution and DPI scale.
    /// </summary>
    public class DesktopIconController : IDisposable
    {
        public IntPtr DesktopOwner { get; private set; }
        public IntPtr DefView { get; private set; }
        public IntPtr ListView { get; private set; }
        public int HostGeneration { get; private set; }
        private uint _hostPid;
        private bool _restoreSnapOnRelease;
        private IntPtr _hProc;
        private IntPtr _remoteMem;
        private IntPtr _localMem;
        private int _remoteBufBytes;
        private readonly Dictionary<string, string> _identityCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool HasCurrentHandles()
        {
            if (ListView == IntPtr.Zero || !NativeMethods.IsWindow(ListView)
                || NativeMethods.GetParent(ListView) != DefView
                || NativeMethods.GetParent(DefView) != DesktopOwner)
                return false;
            uint pid;
            return NativeMethods.GetWindowThreadProcessId(ListView, out pid) != 0 && pid == _hostPid;
        }

        public bool IsDesktopVisible()
        {
            return HasCurrentHandles() && NativeMethods.IsWindowVisible(ListView);
        }

        public int GetDesktopItemCount()
        {
            if (!HasCurrentHandles()) return -1;
            return (int)NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        public int GetRawItemSpacing()
        {
            if (!HasCurrentHandles()) return 0;
            return NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero).ToInt32();
        }

        public DesktopGridMetrics GetLiveStyleMetrics(DesktopGridMetrics cached)
        {
            if (!HasCurrentHandles()) return cached;
            int style = NativeMethods.GetWindowLong(ListView, NativeMethods.GWL_STYLE);
            cached.AutoArrangeEnabled = (style & NativeMethods.LVS_AUTOARRANGE) != 0;
            int exStyle = (int)NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
            cached.SnapToGridEnabled = (exStyle & NativeMethods.LVS_EX_SNAPTOGRID) != 0;
            return cached;
        }

        internal bool TryRefreshIconsLive(List<DesktopIconInfo> cachedSubset, out List<DesktopIconInfo> freshSubset)
        {
            freshSubset = new List<DesktopIconInfo>();
            if (!HasCurrentHandles() || cachedSubset == null) return false;
            if (cachedSubset.Count == 0) return true;
            if (!_shellView.Connect()) return false;

            int count = GetDesktopItemCount();
            if (count <= 0) return false;

            int bufBytes = Math.Max(Marshal.SizeOf(typeof(NativeMethods.RECT)), Marshal.SizeOf(typeof(NativeMethods.POINT))) + 16;
            if (!EnsureRemoteBuffer(bufBytes)) return false;

            IntPtr hProc = _hProc;
            IntPtr remoteMem = _remoteMem;
            IntPtr localMem = _localMem;
            Point lvOrigin = GetListViewScreenOrigin();
            Func<string, string> resolveId = parsingName =>
            {
                string cached;
                if (_identityCache.TryGetValue(parsingName, out cached)) return cached;
                string computed = DesktopItemIdentity.ForParsingName(parsingName);
                _identityCache[parsingName] = computed;
                return computed;
            };

            try
            {
                freshSubset = new List<DesktopIconInfo>(cachedSubset.Count);
                foreach (var item in cachedSubset)
                {
                    if (item.Index < 0 || item.Index >= count) return false;

                    IntPtr okPos = NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMPOSITION, new IntPtr(item.Index), remoteMem);
                    if (okPos == IntPtr.Zero) return false;
                    UIntPtr readBytes;
                    if (!NativeMethods.ReadProcessMemory(hProc, remoteMem, localMem, (UIntPtr)Marshal.SizeOf(typeof(NativeMethods.POINT)), out readBytes))
                        return false;
                    var pt = (NativeMethods.POINT)Marshal.PtrToStructure(localMem, typeof(NativeMethods.POINT));
                    Point clientPos = new Point(pt.X, pt.Y);

                    if (!_shellView.VerifyItemAt(item.Index, clientPos, item.Identity, resolveId))
                        return false;

                    var nativeBounds = ReadItemRect(ListView, hProc, remoteMem, localMem, item.Index, NativeMethods.LVIR_BOUNDS, lvOrigin);
                    var nativeIcon = ReadItemRect(ListView, hProc, remoteMem, localMem, item.Index, NativeMethods.LVIR_ICON, lvOrigin);
                    var nativeLabel = ReadItemRect(ListView, hProc, remoteMem, localMem, item.Index, NativeMethods.LVIR_LABEL, lvOrigin);
                    Rectangle screenRect = nativeBounds ?? new Rectangle(pt.X + lvOrigin.X, pt.Y + lvOrigin.Y, 72, 86);
                    Rectangle iconRect = nativeIcon ?? new Rectangle(screenRect.X + 12, screenRect.Y + 4, 48, 48);
                    Rectangle labelRect = nativeLabel ?? new Rectangle(screenRect.X, iconRect.Bottom + 2,
                        screenRect.Width, Math.Max(20, screenRect.Bottom - iconRect.Bottom));

                    int state = (int)NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMSTATE,
                        new IntPtr(item.Index), new IntPtr(NativeMethods.LVIS_SELECTED));

                    var updated = item;
                    updated.Selected = (state & NativeMethods.LVIS_SELECTED) != 0;
                    updated.ClientPosition = clientPos;
                    updated.ScreenBounds = screenRect;
                    updated.IconBounds = iconRect;
                    updated.LabelBounds = labelRect;
                    updated.EstimatedBounds = !nativeBounds.HasValue || !nativeIcon.HasValue || !nativeLabel.HasValue;
                    freshSubset.Add(updated);
                }
                return true;
            }
            catch (COMException)
            {
                ResetShellView();
                return false;
            }
        }

        internal void InvalidateIdentityCache()
        {
            _identityCache.Clear();
        }

        private readonly DesktopShellView _shellView = new DesktopShellView();
        public bool SnapshotComplete { get; private set; }

        private void ResetShellView()
        {
            SnapshotComplete = false;
            _identityCache.Clear();
            _shellView.Dispose();
        }

        private void ReleaseRemoteMemory()
        {
            if (_localMem != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_localMem);
                _localMem = IntPtr.Zero;
            }
            if (_remoteMem != IntPtr.Zero && _hProc != IntPtr.Zero)
            {
                NativeMethods.VirtualFreeEx(_hProc, _remoteMem, UIntPtr.Zero, NativeMethods.MEM_RELEASE);
                _remoteMem = IntPtr.Zero;
            }
            if (_hProc != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_hProc);
                _hProc = IntPtr.Zero;
            }
            _remoteBufBytes = 0;
        }

        private bool EnsureRemoteBuffer(int requiredBytes)
        {
            if (_hostPid == 0) return false;
            if (_hProc != IntPtr.Zero && _remoteMem != IntPtr.Zero && _localMem != IntPtr.Zero && _remoteBufBytes >= requiredBytes)
                return true;
            ReleaseRemoteMemory();
            _hProc = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE,
                false, _hostPid);
            if (_hProc == IntPtr.Zero) return false;
            _remoteMem = NativeMethods.VirtualAllocEx(_hProc, IntPtr.Zero, (UIntPtr)requiredBytes,
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_READWRITE);
            if (_remoteMem == IntPtr.Zero)
            {
                ReleaseRemoteMemory();
                return false;
            }
            _localMem = Marshal.AllocHGlobal(requiredBytes);
            _remoteBufBytes = requiredBytes;
            return true;
        }

        public void Dispose()
        {
            BindHandles(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            ReleaseRemoteMemory();
        }

        public bool RefreshHandles()
        {
            IntPtr dv, lv;
            IntPtr owner = NativeMethods.FindDesktopHandles(out dv, out lv);
            return BindHandles(owner, dv, lv);
        }

        internal bool BindHandles(IntPtr owner, IntPtr dv, IntPtr lv)
        {
            uint pid = 0;
            if (owner == IntPtr.Zero || dv == IntPtr.Zero || lv == IntPtr.Zero ||
                NativeMethods.GetWindowThreadProcessId(lv, out pid) == 0 ||
                NativeMethods.GetParent(lv) != dv || NativeMethods.GetParent(dv) != owner)
            {
                owner = dv = lv = IntPtr.Zero;
                pid = 0;
            }
            if (owner != DesktopOwner || dv != DefView || lv != ListView || pid != _hostPid)
            {
                RestoreSnapToGrid();
                ResetShellView();
                ReleaseRemoteMemory();
                DesktopOwner = owner;
                DefView = dv;
                ListView = lv;
                _hostPid = pid;
                HostGeneration++;
            }
            return lv != IntPtr.Zero;
        }

        public Point GetListViewScreenOrigin()
        {
            if (!HasCurrentHandles()) return Point.Empty;
            var pt = new NativeMethods.POINT { X = 0, Y = 0 };
            NativeMethods.ClientToScreen(ListView, ref pt);
            return new Point(pt.X, pt.Y);
        }

        public DesktopGridMetrics GetGridMetrics(List<DesktopIconInfo> existingIcons = null)
        {
            float dpiScale = NativeMethods.GetWindowDpiScale(ListView != IntPtr.Zero ? ListView : DesktopOwner);
            var m = new DesktopGridMetrics
            {
                CellWidth = (int)Math.Round(76 * dpiScale),
                CellHeight = (int)Math.Round(100 * dpiScale),
                OriginX = 2,
                OriginY = 2,
                AutoArrangeEnabled = false,
                SnapToGridEnabled = true
            };

            if (!HasCurrentHandles()) return m;

            int style = NativeMethods.GetWindowLong(ListView, NativeMethods.GWL_STYLE);
            m.AutoArrangeEnabled = (style & NativeMethods.LVS_AUTOARRANGE) != 0;

            int exStyle = (int)NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
            m.SnapToGridEnabled = (exStyle & NativeMethods.LVS_EX_SNAPTOGRID) != 0;

            IntPtr spacing = NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero);
            int spVal = spacing.ToInt32();
            int cx = spVal & 0xFFFF;
            int cy = (spVal >> 16) & 0xFFFF;
            if (cx >= 40 && cx <= 400) m.CellWidth = cx;
            if (cy >= 40 && cy <= 400) m.CellHeight = cy;

            Point lvOrigin = GetListViewScreenOrigin();

            var icons = existingIcons ?? GetAllIcons();
            if (icons.Count >= 2 && m.SnapToGridEnabled)
            {
                int observedX = InferGridPitch(icons, true, m.CellWidth);
                int observedY = InferGridPitch(icons, false, m.CellHeight);
                if (observedX >= 48 && observedX <= 360) m.CellWidth = observedX;
                if (observedY >= 48 && observedY <= 360) m.CellHeight = observedY;
            }

            m.OriginX = InferGridOrigin(icons.Select(i => i.ScreenBounds.Left), m.CellWidth, lvOrigin.X);
            m.OriginY = InferGridOrigin(icons.Select(i => i.ScreenBounds.Top), m.CellHeight, lvOrigin.Y);

            return m;
        }

        private static int InferGridPitch(List<DesktopIconInfo> icons, bool horizontal, int fallback)
        {
            var diffs = new List<int>();
            int limit = Math.Min(icons.Count, 64);
            for (int i = 0; i < limit; i++)
            {
                for (int j = i + 1; j < limit; j++)
                {
                    if (horizontal)
                    {
                        if (Math.Abs(icons[i].ScreenBounds.Top - icons[j].ScreenBounds.Top) <= 8)
                        {
                            int d = Math.Abs(icons[i].ScreenBounds.Left - icons[j].ScreenBounds.Left);
                            if (d >= 48 && d <= 360) diffs.Add(d);
                        }
                    }
                    else
                    {
                        if (Math.Abs(icons[i].ScreenBounds.Left - icons[j].ScreenBounds.Left) <= 8)
                        {
                            int d = Math.Abs(icons[i].ScreenBounds.Top - icons[j].ScreenBounds.Top);
                            if (d >= 48 && d <= 360) diffs.Add(d);
                        }
                    }
                }
            }

            if (diffs.Count == 0) return fallback;

            diffs.Sort();
            int bestPitch = fallback;
            int bestScore = -1;

            var candidates = new List<int>();
            foreach (int d in diffs)
            {
                if (d > fallback * 1.65) break;
                if (!candidates.Any(c => Math.Abs(c - d) <= 2))
                {
                    candidates.Add(d);
                    if (candidates.Count >= 24) break;
                }
            }

            foreach (int cand in candidates)
            {
                int score = 0;
                foreach (int d in diffs)
                {
                    int rem = d % cand;
                    if (rem <= 2 || cand - rem <= 2)
                    {
                        int mult = (int)Math.Round((double)d / cand);
                        score += (mult == 1) ? 4 : 1;
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPitch = cand;
                }
            }

            var unitDiffs = diffs.Where(d => Math.Abs(d - bestPitch) <= 2).ToList();
            if (unitDiffs.Count > 0)
            {
                unitDiffs.Sort();
                return unitDiffs[unitDiffs.Count / 2];
            }

            return bestPitch;
        }

        internal static int InferGridOrigin(IEnumerable<int> coords, int pitch, int fallback)
        {
            if (pitch <= 0) return fallback;
            var mods = new List<int>();
            int[] buckets = new int[pitch];
            foreach (int v in coords)
            {
                int m = ((v % pitch) + pitch) % pitch;
                mods.Add(m);
                buckets[m]++;
            }
            if (mods.Count == 0) return ((fallback % pitch) + pitch) % pitch;

            int bestVal = mods[0];
            int bestCount = -1;
            foreach (int m in mods)
            {
                int count = 0;
                for (int d = -2; d <= 2; d++)
                    count += buckets[(m + d + pitch) % pitch];
                if (count > bestCount)
                {
                    bestCount = count;
                    bestVal = m;
                }
            }
            return bestVal;
        }

        public List<DesktopIconInfo> GetAllIcons(bool refreshIdentities = false)
        {
            var result = new List<DesktopIconInfo>();
            SnapshotComplete = false;
            if (!HasCurrentHandles()) return result;
            if (!_shellView.Connect()) return result;
            if (refreshIdentities) _identityCache.Clear();
            _shellView.BeginSnapshot();
            int count = (int)NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (count <= 0)
            {
                SnapshotComplete = count == 0 && HasCurrentHandles() && _shellView.HasItemCount(0);
                return result;
            }

            int lvItemSize = Marshal.SizeOf(typeof(NativeMethods.LVITEM64));
            const int maxChars = 260;
            int textBufBytes = maxChars * 2;
            int totalBytes = lvItemSize + textBufBytes + 64;
            if (!EnsureRemoteBuffer(totalBytes)) return result;

            IntPtr hProc = _hProc;
            IntPtr remoteMem = _remoteMem;
            IntPtr localMem = _localMem;
            if (_identityCache.Count > count * 2 + 64) _identityCache.Clear();
            Func<string, string> resolveId = parsingName =>
            {
                string cached;
                if (_identityCache.TryGetValue(parsingName, out cached)) return cached;
                string computed = DesktopItemIdentity.ForParsingName(parsingName);
                _identityCache[parsingName] = computed;
                return computed;
            };

            try
            {
                IntPtr remoteText = new IntPtr(remoteMem.ToInt64() + lvItemSize + 16);
                Point lvOrigin = GetListViewScreenOrigin();

                for (int i = 0; i < count; i++)
                {
                    // 1. Position
                    IntPtr okPos = NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMPOSITION, new IntPtr(i), remoteMem);
                    if (okPos == IntPtr.Zero) continue;

                    UIntPtr readBytes;
                    if (!NativeMethods.ReadProcessMemory(hProc, remoteMem, localMem, (UIntPtr)Marshal.SizeOf(typeof(NativeMethods.POINT)), out readBytes))
                        continue;
                    var pt = (NativeMethods.POINT)Marshal.PtrToStructure(localMem, typeof(NativeMethods.POINT));

                    // 2. Bounding rects (LVIR_BOUNDS, LVIR_ICON, LVIR_LABEL)
                    var nativeBounds = ReadItemRect(ListView, hProc, remoteMem, localMem, i, NativeMethods.LVIR_BOUNDS, lvOrigin);
                    var nativeIcon = ReadItemRect(ListView, hProc, remoteMem, localMem, i, NativeMethods.LVIR_ICON, lvOrigin);
                    var nativeLabel = ReadItemRect(ListView, hProc, remoteMem, localMem, i, NativeMethods.LVIR_LABEL, lvOrigin);
                    Rectangle screenRect = nativeBounds ?? new Rectangle(pt.X + lvOrigin.X, pt.Y + lvOrigin.Y, 72, 86);
                    Rectangle iconRect = nativeIcon ?? new Rectangle(screenRect.X + 12, screenRect.Y + 4, 48, 48);
                    Rectangle labelRect = nativeLabel ?? new Rectangle(screenRect.X, iconRect.Bottom + 2,
                        screenRect.Width, Math.Max(20, screenRect.Bottom - iconRect.Bottom));

                    // The display label is needed to migrate older layouts. It is never an item key.
                    var request = new NativeMethods.LVITEM64
                    {
                        mask = NativeMethods.LVIF_TEXT | NativeMethods.LVIF_STATE,
                        iItem = i,
                        stateMask = NativeMethods.LVIS_SELECTED,
                        pszText = remoteText,
                        cchTextMax = maxChars
                    };
                    Marshal.StructureToPtr(request, localMem, false);
                    UIntPtr written;
                    if (!NativeMethods.WriteProcessMemory(hProc, remoteMem, localMem, (UIntPtr)lvItemSize, out written) ||
                        NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMW, IntPtr.Zero, remoteMem) == IntPtr.Zero ||
                        !NativeMethods.ReadProcessMemory(hProc, remoteMem, localMem, (UIntPtr)lvItemSize, out readBytes)) continue;
                    var item = (NativeMethods.LVITEM64)Marshal.PtrToStructure(localMem, typeof(NativeMethods.LVITEM64));
                    if (item.pszText == IntPtr.Zero || !NativeMethods.ReadProcessMemory(hProc, item.pszText, localMem, (UIntPtr)textBufBytes, out readBytes)) continue;
                    string label = Marshal.PtrToStringUni(localMem, maxChars);
                    int terminator = label.IndexOf('\0');
                    if (terminator >= 0) label = label.Substring(0, terminator);
                    string identity, parsingName;
                    if (!_shellView.ReadItem(i, new Point(pt.X, pt.Y), out identity, out parsingName, resolveId)) continue;
                    result.Add(new DesktopIconInfo
                    {
                        Index = i,
                        Name = label,
                        Identity = identity,
                        ParsingName = parsingName,
                        Selected = (item.state & NativeMethods.LVIS_SELECTED) != 0,
                        ClientPosition = new Point(pt.X, pt.Y),
                        ScreenBounds = screenRect,
                        IconBounds = iconRect,
                        LabelBounds = labelRect,
                        EstimatedBounds = !nativeBounds.HasValue || !nativeIcon.HasValue || !nativeLabel.HasValue
                    });
                }
            }
            catch (COMException)
            {
                ResetShellView();
                result.Clear();
            }

            SnapshotComplete = result.Count == count && HasCurrentHandles() && _shellView.HasItemCount(count) &&
                count == (int)NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            return result;
        }

        private static Rectangle? ReadItemRect(IntPtr listView, IntPtr hProc, IntPtr remoteMem, IntPtr localMem, int index, int code, Point lvOrigin)
        {
            var reqRect = new NativeMethods.RECT { Left = code, Top = 0, Right = 0, Bottom = 0 };
            Marshal.StructureToPtr(reqRect, localMem, false);
            UIntPtr written;
            if (!NativeMethods.WriteProcessMemory(hProc, remoteMem, localMem, (UIntPtr)Marshal.SizeOf(typeof(NativeMethods.RECT)), out written))
                return null;

            IntPtr ok = NativeMethods.SendMessage(listView, NativeMethods.LVM_GETITEMRECT, new IntPtr(index), remoteMem);
            UIntPtr readBytes;
            if (ok != IntPtr.Zero &&
                NativeMethods.ReadProcessMemory(hProc, remoteMem, localMem, (UIntPtr)Marshal.SizeOf(typeof(NativeMethods.RECT)), out readBytes))
            {
                var rc = (NativeMethods.RECT)Marshal.PtrToStructure(localMem, typeof(NativeMethods.RECT));
                return Rectangle.FromLTRB(
                    rc.Left + lvOrigin.X,
                    rc.Top + lvOrigin.Y,
                    rc.Right + lvOrigin.X,
                    rc.Bottom + lvOrigin.Y);
            }
            return null;
        }

        internal void SuspendSnapToGrid()
        {
            if (_restoreSnapOnRelease || !HasCurrentHandles()) return;
            int previous = SetSnapToGrid(false);
            _restoreSnapOnRelease = (previous & NativeMethods.LVS_EX_SNAPTOGRID) != 0;
        }

        internal bool RestoreSnapToGrid()
        {
            if (!_restoreSnapOnRelease) return false;
            _restoreSnapOnRelease = false;
            if (HasCurrentHandles())
            {
                SetSnapToGrid(true);
                return true;
            }
            return false;
        }

        private int SetSnapToGrid(bool enabled)
        {
            return (int)NativeMethods.SendMessage(
                ListView,
                NativeMethods.LVM_SETEXTENDEDLISTVIEWSTYLE,
                new IntPtr(NativeMethods.LVS_EX_SNAPTOGRID),
                enabled ? new IntPtr(NativeMethods.LVS_EX_SNAPTOGRID) : IntPtr.Zero);
        }

        public IntPtr GetActiveEditControl()
        {
            if (!HasCurrentHandles()) return IntPtr.Zero;
            IntPtr edit = NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETEDITCONTROL, IntPtr.Zero, IntPtr.Zero);
            if (edit != IntPtr.Zero && NativeMethods.IsWindowVisible(edit))
                return edit;
            return IntPtr.Zero;
        }

        public Rectangle GetIconLabelBounds(int index, Rectangle fallback)
        {
            if (!HasCurrentHandles()) return fallback;
            int sz = Marshal.SizeOf(typeof(NativeMethods.RECT));
            if (!EnsureRemoteBuffer(sz)) return fallback;
            Point lvOrigin = GetListViewScreenOrigin();
            return ReadItemRect(ListView, _hProc, _remoteMem, _localMem, index, NativeMethods.LVIR_LABEL, lvOrigin) ?? fallback;
        }

        public void MoveActiveEditControlWithIcon(IntPtr editHwnd, Rectangle oldEditScreenRect, Rectangle oldLabelScreenRect, int iconIndex, Point fallbackDelta)
        {
            if (editHwnd == IntPtr.Zero || ListView == IntPtr.Zero) return;

            var editClientPt = new NativeMethods.POINT { X = oldEditScreenRect.Left, Y = oldEditScreenRect.Top };
            if (!NativeMethods.ScreenToClient(ListView, ref editClientPt))
                return;

            Rectangle expectedLabel = new Rectangle(
                oldLabelScreenRect.X + fallbackDelta.X,
                oldLabelScreenRect.Y + fallbackDelta.Y,
                oldLabelScreenRect.Width,
                oldLabelScreenRect.Height);
            Rectangle newLabel = GetIconLabelBounds(iconIndex, expectedLabel);

            int dx = newLabel.X - oldLabelScreenRect.X;
            int dy = newLabel.Y - oldLabelScreenRect.Y;
            if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2 && (Math.Abs(fallbackDelta.X) > 4 || Math.Abs(fallbackDelta.Y) > 4))
            {
                dx = fallbackDelta.X;
                dy = fallbackDelta.Y;
            }

            NativeMethods.SetWindowPos(
                editHwnd,
                IntPtr.Zero,
                editClientPt.X + dx,
                editClientPt.Y + dy,
                0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            NativeMethods.UpdateWindow(editHwnd);
        }

        public void MoveIconsBatch(List<KeyValuePair<string, Point>> targets)
        {
            if (targets == null || targets.Count == 0 || !HasCurrentHandles()) return;
            try { _shellView.Move(targets); }
            catch (COMException) { ResetShellView(); }
        }

        internal bool HasShellItemCount(int expected)
        {
            return HasCurrentHandles() && _shellView.HasItemCount(expected);
        }

        internal bool VerifyMemberIndices(List<DesktopIconInfo> members)
        {
            return HasCurrentHandles() && _shellView.VerifyItemIndices(members);
        }

        internal bool TryGetLiveIconPosition(string identity, out Point position, bool verifyIdentity = false)
        {
            position = Point.Empty;
            if (string.IsNullOrEmpty(identity) || !HasCurrentHandles()) return false;
            try { return _shellView.ReadPosition(identity, out position, verifyIdentity); }
            catch (COMException) { ResetShellView(); return false; }
        }

        public void SetListViewRedraw(bool enabled)
        {
            if (!HasCurrentHandles()) return;
            NativeMethods.SendMessage(ListView, NativeMethods.WM_SETREDRAW, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
        }

        public void MoveIconsDirectByIndex(List<KeyValuePair<int, Point>> indexMoves)
        {
            if (indexMoves == null || indexMoves.Count == 0 || !HasCurrentHandles()) return;
            int ptSize = Marshal.SizeOf(typeof(NativeMethods.POINT));
            if (!EnsureRemoteBuffer(ptSize)) return;
            foreach (var kv in indexMoves)
            {
                var pt = new NativeMethods.POINT { X = kv.Value.X, Y = kv.Value.Y };
                Marshal.StructureToPtr(pt, _localMem, false);
                UIntPtr written;
                if (NativeMethods.WriteProcessMemory(_hProc, _remoteMem, _localMem, (UIntPtr)ptSize, out written))
                    NativeMethods.SendMessage(ListView, NativeMethods.LVM_SETITEMPOSITION32, new IntPtr(kv.Key), _remoteMem);
            }
        }

        public bool TryGetItemClientPosition(int index, out Point position)
        {
            position = Point.Empty;
            if (index < 0 || !HasCurrentHandles()) return false;
            int ptSize = Marshal.SizeOf(typeof(NativeMethods.POINT));
            if (!EnsureRemoteBuffer(ptSize)) return false;
            if (NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETITEMPOSITION, new IntPtr(index), _remoteMem) == IntPtr.Zero)
                return false;
            UIntPtr readBytes;
            if (!NativeMethods.ReadProcessMemory(_hProc, _remoteMem, _localMem, (UIntPtr)ptSize, out readBytes))
                return false;
            var pt = (NativeMethods.POINT)Marshal.PtrToStructure(_localMem, typeof(NativeMethods.POINT));
            position = new Point(pt.X, pt.Y);
            return true;
        }

        internal struct ParkTiming
        {
            public double SuspendSnapMs;
            public double MoveMs;
            public double VerifyMs;
            public double FlushPaintMs;
        }

        public bool ParkIconsOffscreen(List<DesktopIconInfo> members, bool snapEnabled = true)
        {
            ParkTiming unused;
            return ParkIconsOffscreen(members, snapEnabled, out unused);
        }

        internal bool ParkIconsOffscreen(List<DesktopIconInfo> members, bool snapEnabled, out ParkTiming timing)
        {
            timing = new ParkTiming();
            if (members == null || members.Count == 0) return true;
            if (!HasCurrentHandles()) return false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Point lvOrigin = GetListViewScreenOrigin();
            Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            Rectangle paddedVs = Rectangle.Inflate(vs, 200, 200);

            int liveEx = (int)NativeMethods.SendMessage(ListView, NativeMethods.LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
            bool liveSnap = (liveEx & NativeMethods.LVS_EX_SNAPTOGRID) != 0;
            bool suspendedHere = false;

            SetListViewRedraw(false);
            try
            {
                // CRITICAL (ComCtl v6): Suspend LVS_EX_SNAPTOGRID BEFORE moving offscreen!
                // With SnapToGrid ON, ComCtl v6 clamps negative offscreen coords directly onto
                // the visible top-left desktop cells AND pushes existing top-left icons aside!
                double t0 = sw.Elapsed.TotalMilliseconds;
                if (snapEnabled || liveSnap)
                {
                    if (!_restoreSnapOnRelease)
                    {
                        SuspendSnapToGrid();
                        suspendedHere = _restoreSnapOnRelease;
                    }
                }
                timing.SuspendSnapMs = sw.Elapsed.TotalMilliseconds - t0;

                int negX = Math.Min(-5000, vs.Left - lvOrigin.X - 5000);
                int negY = Math.Min(-5000, vs.Top - lvOrigin.Y - 5000);
                if (TryParkAtBase(members, negX, negY, -120, lvOrigin, paddedVs, sw, ref timing))
                    return true;

                int posX = Math.Max(20000, vs.Right - lvOrigin.X + 5000);
                int posY = Math.Max(20000, vs.Bottom - lvOrigin.Y + 5000);
                if (TryParkAtBase(members, posX, posY, 120, lvOrigin, paddedVs, sw, ref timing))
                    return true;

                // Explorer refused offscreen coords: restore original positions BEFORE re-enabling
                // redraw so zero icons ever flash outside the fence on the desktop!
                var restore = new List<KeyValuePair<int, Point>>(members.Count);
                foreach (var m in members)
                    restore.Add(new KeyValuePair<int, Point>(m.Index, m.ClientPosition));
                MoveIconsDirectByIndex(restore);
                if (suspendedHere) RestoreSnapToGrid();
                return false;
            }
            finally
            {
                SetListViewRedraw(true);
            }
        }

        private bool TryParkAtBase(List<DesktopIconInfo> members, int baseX, int baseY, int stepX, Point lvOrigin, Rectangle paddedVirtualScreen,
            System.Diagnostics.Stopwatch sw, ref ParkTiming timing)
        {
            double tMove = sw.Elapsed.TotalMilliseconds;
            var moves = new List<KeyValuePair<int, Point>>(members.Count);
            for (int i = 0; i < members.Count; i++)
                moves.Add(new KeyValuePair<int, Point>(members[i].Index, new Point(baseX + i * stepX, baseY)));
            MoveIconsDirectByIndex(moves);
            timing.MoveMs += sw.Elapsed.TotalMilliseconds - tMove;

            double tVer = sw.Elapsed.TotalMilliseconds;
            foreach (var m in members)
            {
                Point pos;
                if (!TryGetItemClientPosition(m.Index, out pos) && !TryGetLiveIconPosition(m.Identity, out pos))
                {
                    timing.VerifyMs += sw.Elapsed.TotalMilliseconds - tVer;
                    return false;
                }
                if (paddedVirtualScreen.Contains(pos.X + lvOrigin.X, pos.Y + lvOrigin.Y))
                {
                    timing.VerifyMs += sw.Elapsed.TotalMilliseconds - tVer;
                    return false;
                }
            }
            timing.VerifyMs += sw.Elapsed.TotalMilliseconds - tVer;
            return true;
        }

        public void InvalidateDesktopRect(Rectangle dirtyScreenRect)
        {
            if (!HasCurrentHandles()) return;
            try
            {
                Point lvOrigin = GetListViewScreenOrigin();
                var rc = new NativeMethods.RECT
                {
                    Left = dirtyScreenRect.Left - lvOrigin.X - 8,
                    Top = dirtyScreenRect.Top - lvOrigin.Y - 8,
                    Right = dirtyScreenRect.Right - lvOrigin.X + 8,
                    Bottom = dirtyScreenRect.Bottom - lvOrigin.Y + 8
                };
                NativeMethods.InvalidateRect(ListView, ref rc, true);
            }
            catch { }
        }

        public void FlushDesktopPaint(Rectangle? dirtyScreenRect = null, IEnumerable<Rectangle> extraDirtyScreenRects = null)
        {
            if (!HasCurrentHandles()) return;
            try
            {
                if (dirtyScreenRect.HasValue)
                {
                    Point lvOrigin = GetListViewScreenOrigin();
                    if (extraDirtyScreenRects != null)
                    {
                        foreach (var er in extraDirtyScreenRects)
                        {
                            var erc = new NativeMethods.RECT
                            {
                                Left = er.Left - lvOrigin.X - 12,
                                Top = er.Top - lvOrigin.Y - 12,
                                Right = er.Right - lvOrigin.X + 12,
                                Bottom = er.Bottom - lvOrigin.Y + 12
                            };
                            NativeMethods.InvalidateRect(ListView, ref erc, true);
                        }
                    }

                    Rectangle r = dirtyScreenRect.Value;
                    var rc = new NativeMethods.RECT
                    {
                        Left = r.Left - lvOrigin.X - 8,
                        Top = r.Top - lvOrigin.Y - 8,
                        Right = r.Right - lvOrigin.X + 8,
                        Bottom = r.Bottom - lvOrigin.Y + 8
                    };
                    IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMethods.RECT)));
                    try
                    {
                        Marshal.StructureToPtr(rc, ptr, false);
                        NativeMethods.RedrawWindow(ListView, ptr, IntPtr.Zero,
                            NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_UPDATENOW | NativeMethods.RDW_ALLCHILDREN);
                    }
                    finally { Marshal.FreeHGlobal(ptr); }
                    NativeMethods.UpdateWindow(ListView);
                }
                else
                {
                    NativeMethods.RedrawWindow(ListView, IntPtr.Zero, IntPtr.Zero,
                        NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_UPDATENOW | NativeMethods.RDW_ALLCHILDREN);
                }
            }
            catch { }
        }

        internal bool TryMoveFenceMembers(List<DesktopIconInfo> desktop, List<DesktopIconInfo> members,
            DesktopGridMetrics grid, Point previousDelta, Point delta, Func<bool> current)
        {
            return _shellView.TryMoveFenceMembers(desktop, members, grid, previousDelta, delta, current);
        }
    }
}
