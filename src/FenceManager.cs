using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Automation;
using Microsoft.Win32;

namespace DeskFences
{
    internal sealed class DesktopDragGesture
    {
        private readonly Dictionary<string, Point> _origins = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        private string _anchor;
        private Point _press, _delta;
        private long _released = -1;
        private bool _anchorMoved;
        public bool Pressed { get { return _anchor != null && _released < 0; } }

        public bool WaitingForItem(string identity)
        {
            return _released >= 0 && _origins.ContainsKey(identity);
        }

        public void Cancel()
        {
            _origins.Clear();
            _anchor = null;
            _released = -1;
            _anchorMoved = false;
        }

        public void Begin(Point press, IEnumerable<DesktopIconInfo> icons)
        {
            Cancel();
            _press = press;
            DesktopIconInfo hit = new DesktopIconInfo();
            int bestPriority = -1;
            double bestDistSq = double.MaxValue;
            foreach (var icon in icons)
            {
                if (string.IsNullOrEmpty(icon.Identity)) continue;
                bool directHit = icon.IconBounds.Contains(press) || icon.LabelBounds.Contains(press);
                bool boxHit = !directHit && icon.ScreenBounds.Contains(press);
                if (!directHit && !boxHit) continue;

                int priority = (directHit ? 2 : 1) + (icon.Selected ? 2 : 0);
                Rectangle refRect = directHit ? (icon.IconBounds.Contains(press) ? icon.IconBounds : icon.LabelBounds) : icon.ScreenBounds;
                double cx = refRect.Left + refRect.Width * 0.5;
                double cy = refRect.Top + refRect.Height * 0.5;
                double distSq = (press.X - cx) * (press.X - cx) + (press.Y - cy) * (press.Y - cy);

                if (priority > bestPriority || (priority == bestPriority && distSq < bestDistSq))
                {
                    bestPriority = priority;
                    bestDistSq = distSq;
                    hit = icon;
                }
            }
            if (string.IsNullOrEmpty(hit.Identity)) return;
            _anchor = hit.Identity;
            foreach (var icon in icons)
                if (!string.IsNullOrEmpty(icon.Identity) && (icon.Identity == _anchor || (hit.Selected && icon.Selected)))
                    _origins[icon.Identity] = icon.ClientPosition;
        }

        public void Release(Point point, bool onDesktop, Size dragThreshold, long now)
        {
            _delta = new Point(point.X - _press.X, point.Y - _press.Y);
            if (_anchor == null || !onDesktop ||
                (Math.Abs(_delta.X) < Math.Max(1, dragThreshold.Width / 2) &&
                 Math.Abs(_delta.Y) < Math.Max(1, dragThreshold.Height / 2)))
            {
                Cancel();
                return;
            }
            _released = now;
        }

        public HashSet<string> MovedItems(IEnumerable<DesktopIconInfo> icons, DesktopGridMetrics metrics, long now)
        {
            var moved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_released < 0) return moved;
            if (now - _released > 800) { Cancel(); return moved; }
            // Allow up to ~1.25 cells of slack so Explorer's snap-to-grid auto-bump onto an adjacent
            // free cell (when dropping near an occupied slot or multi-selecting) is still recognized,
            // while large unrelated Explorer reflows remain rejected.
            int tolX = Math.Max(3, metrics.CellWidth + metrics.CellWidth / 4 + 2);
            int tolY = Math.Max(3, metrics.CellHeight + metrics.CellHeight / 4 + 2);
            foreach (var icon in icons)
            {
                Point origin;
                if (!icon.Selected || string.IsNullOrEmpty(icon.Identity) || !_origins.TryGetValue(icon.Identity, out origin)) continue;
                int dx = icon.ClientPosition.X - origin.X, dy = icon.ClientPosition.Y - origin.Y;
                if ((dx != 0 || dy != 0) &&
                    Math.Abs(dx - _delta.X) <= tolX &&
                    Math.Abs(dy - _delta.Y) <= tolY)
                    moved.Add(icon.Identity);
            }
            _anchorMoved |= moved.Contains(_anchor);
            if (!_anchorMoved) moved.Clear();
            foreach (string id in moved) _origins.Remove(id);
            if (_origins.Count == 0) Cancel();
            return moved;
        }
    }

    public enum FenceSortMode { Name, Type, ModifiedTime }

    internal enum DesktopMenuCommand { None, NewItem, Paste }

    internal sealed class DesktopMenuPlacement
    {
        public string FenceId { get; private set; }
        public Point Click { get; private set; }
        public DesktopMenuCommand Command { get; private set; }
        private HashSet<string> _existing, _pastePaths;
        private long _confirmed;

        public void Cancel()
        {
            FenceId = null;
            Command = DesktopMenuCommand.None;
            _existing = null;
            _pastePaths = null;
        }

        public void Open(string fenceId, Point click)
        {
            Cancel();
            FenceId = fenceId;
            Click = click;
        }

        public bool ShouldCancelOnKey(Keys key, bool releasedPopup)
        {
            if (key == Keys.Escape) return true;
            if (FenceId == null || Command != DesktopMenuCommand.None || releasedPopup) return false;
            switch (key)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.Home: case Keys.End: case Keys.Return:
                    return false;
                default:
                    return true;
            }
        }

        public void Confirm(DesktopMenuCommand command, IEnumerable<string> existing, IEnumerable<string> pastePaths, long now)
        {
            if (FenceId == null) return;
            _existing = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            _pastePaths = new HashSet<string>(pastePaths ?? new string[0], StringComparer.OrdinalIgnoreCase);
            Command = command;
            _confirmed = now;
        }

        public bool Take(DesktopIconInfo icon, bool isBeingEdited, long now)
        {
            if (Command == DesktopMenuCommand.None) return false;
            if (now - _confirmed > 2000) { Cancel(); return false; }
            if (!icon.Selected || string.IsNullOrEmpty(icon.Identity) || _existing.Contains(icon.Identity)) return false;
            if (Command == DesktopMenuCommand.NewItem && isBeingEdited) { Cancel(); return true; }
            if (Command != DesktopMenuCommand.Paste) return false;
            if (!ConsumePasteMatch(icon.ParsingName)) return false;
            if (_pastePaths.Count == 0) Cancel();
            return true;
        }

        private bool ConsumePasteMatch(string actualPath)
        {
            if (_pastePaths == null || string.IsNullOrEmpty(actualPath)) return false;
            if (_pastePaths.Remove(actualPath)) return true;

            // Handle desktop-to-desktop duplicate paste (e.g. "file - 副本.txt", "file - Copy (2).txt")
            string actualDir = Path.GetDirectoryName(actualPath);
            string actualExt = Path.GetExtension(actualPath);
            string actualStem = Path.GetFileNameWithoutExtension(actualPath);
            if (string.IsNullOrEmpty(actualStem)) return false;

            string matchedExpected = null;
            foreach (string expected in _pastePaths)
            {
                string expDir = Path.GetDirectoryName(expected);
                string expExt = Path.GetExtension(expected);
                string expStem = Path.GetFileNameWithoutExtension(expected);
                if (!string.Equals(actualDir, expDir, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(actualExt, expExt, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrEmpty(expStem))
                    continue;
                if (actualStem.StartsWith(expStem + " - 副本", StringComparison.OrdinalIgnoreCase) ||
                    actualStem.StartsWith(expStem + " - Copy", StringComparison.OrdinalIgnoreCase) ||
                    actualStem.StartsWith(expStem + " (", StringComparison.OrdinalIgnoreCase))
                {
                    matchedExpected = expected;
                    break;
                }
            }
            if (matchedExpected != null)
            {
                _pastePaths.Remove(matchedExpected);
                return true;
            }
            return false;
        }

        public bool AwaitingInlineEdit(DesktopIconInfo icon)
        {
            return Command == DesktopMenuCommand.NewItem && icon.Selected && !_existing.Contains(icon.Identity);
        }
    }

    public class FenceManager : ApplicationContext
    {
        private const string StartupRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "DeskFences";

        public AppConfig Config { get; private set; }
        private readonly DesktopMembership _membership = new DesktopMembership();
        private readonly DesktopIconController _controller = new DesktopIconController();
        private readonly Dictionary<string, FenceChromeForm> _forms = new Dictionary<string, FenceChromeForm>();
        private readonly Dictionary<string, Point> _knownIconCells = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);

        private readonly NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private Icon _trayAppIcon;
        private readonly Timer _watchTimer;
        private readonly Timer _burstWatchTimer;
        private readonly Timer _iconAnimTimer;
        private readonly Stopwatch _iconAnimWatch = new Stopwatch();
        private readonly Stopwatch _sessionClock = Stopwatch.StartNew();
        private readonly Control _uiInvoker;
        private NativeMethods.LowLevelMouseProc _mouseHookProc;
        private IntPtr _mouseHook = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _winEventProc;
        private IntPtr _winEventHook = IntPtr.Zero;
        private FileSystemWatcher _desktopWatcher;
        private FileSystemWatcher _commonDesktopWatcher;
        private long _burstUntilMs = -1;
        private bool _desktopDirty = true;
        private DesktopGridMetrics _lastGridMetrics;
        private int _lastRawItemSpacing;
        private bool _lastDropClean;
        private long _lastFullScanMs = -10000;
        private int _lastItemCount = -1;
        private bool _fencesHiddenByUser;
        private bool _lastDesktopVisible = true;

        private readonly DesktopDragGesture _desktopDrag = new DesktopDragGesture();
        private readonly DesktopMenuPlacement _desktopMenu = new DesktopMenuPlacement();
        private bool _nonDesktopPressActive;
        private string _externalDropFenceId;
        private Point _externalDropPt;
        private long _externalDropMs = -1;
        private List<DesktopIconInfo> _inputSnapshot = new List<DesktopIconInfo>();
        private int _pressSequence;
        private NativeMethods.LowLevelMouseProc _keyboardHookProc;
        private IntPtr _keyboardHook;
        private PopupPress _popupPress;
        private NativeMethods.HighlightedMenuItem _highlightedItem;
        private bool _readingMenuHighlight;

        private sealed class PopupPress
        {
            public IntPtr Window;
            public Point Start, End;
            public string[] Existing;
            public uint ClipboardSequence;
            public bool Released, Inspected, MenuEnded;
            public Rectangle Bounds;
            public DesktopMenuCommand Command;
            public long ReleasedAt;
        }

        private class RecentContextPlacement
        {
            public string TargetFenceId;
            public Point TargetCell;
            public long PlacedAtMs;
        }
        private readonly Dictionary<string, RecentContextPlacement> _recentContextPlacements =
            new Dictionary<string, RecentContextPlacement>(StringComparer.OrdinalIgnoreCase);

        // Fallback window for Win11 XAML right-click -> New (strictly cleared on Escape, click-away,
        // or confirmed non-creation menu commands so unrelated background files are never adopted).
        private string _recentRightClickFenceId;
        private Point _recentRightClickPt;
        private long _recentRightClickMs = -1;
        private long _recentRightClickConsumedMs = -1;

        private double _iconAnimDurationMs = 260.0;
        private List<IconSlideTrack> _iconAnimTracks;
        private bool _isAnimatingIcons;
        private List<IconSlideTrack> _pendingDropOutsiderTracks;
        private bool _pendingDropSnapEnabled;

        private IntPtr _interlayerParent = IntPtr.Zero;
        private IntPtr _interlayerInsertAfter = IntPtr.Zero;
        private bool _warnedAutoArrange;
        private bool _isMutatingFence;
        private bool _canMoveMembers;
        private List<DesktopIconInfo> _dragDesktop;
        private bool _isEnforcing;
        private bool _isDrawingRegion;
        private int _attachedGeneration = -1;
        private bool _needsInitialFence;
        private bool _layoutPending;
        private bool _rebindingDesktop;
        private bool _deferredDisplayChange;
        private bool _exiting;
        private bool _exitAfterInteraction;
        private long _nextLayerCheckMs;

        private class IconSlideTrack
        {
            public DesktopIconInfo IconInfo;
            public Point StartPos;
            public Point TargetPos;
            public Point LastSentPos;
        }

        private ToolStripMenuItem _miLock;
        private ToolStripMenuItem _miHideAll;
        private ToolStripMenuItem _miStartup;

        public FenceManager() : this(AppConfig.Load()) { }

        public FenceManager(AppConfig loadedConfig)
        {
            _uiInvoker = new Control();
            var ensureInvokerHandle = _uiInvoker.Handle;

            Config = loadedConfig;
            bool firstRun = Config == null;
            _needsInitialFence = firstRun;
            if (Config == null) Config = new AppConfig();

            _trayIcon = CreateTrayIcon();

            UpdateTrayStates();

            _iconAnimTimer = new Timer { Interval = 10 };
            _iconAnimTimer.Tick += OnIconAnimTimerTick;

            _burstWatchTimer = new Timer { Interval = 8 };
            _burstWatchTimer.Tick += OnBurstWatchTimerTick;

            _watchTimer = new Timer { Interval = 350 };
            _watchTimer.Tick += OnWatchTimerTick;
            RefreshDesktopHostHandles(false);
            _watchTimer.Start();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            InstallPassiveMouseHook();
            InstallDesktopFileWatcher();

            if (firstRun)
            {
                _trayIcon.ShowBalloonTip(
                    4500,
                    "DeskFences 已启动",
                    "分区四边均保留独立图标位；仅手动拖入/拖出可进出分区，拖动分区时碰撞图标会自动挤开。右键托盘可新建更多桌面分区。",
                    ToolTipIcon.Info);
            }
        }

        private void InstallPassiveMouseHook()
        {
            try
            {
                _mouseHookProc = PassiveMouseHookCallback;
                IntPtr hMod = NativeMethods.GetModuleHandle(null);
                _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
                _keyboardHookProc = PassiveKeyboardHookCallback;
                _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, hMod, 0);
            }
            catch (System.Runtime.InteropServices.ExternalException) { }
        }

        private void UninstallPassiveMouseHook()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                try { NativeMethods.UnhookWindowsHookEx(_mouseHook); } catch { }
                _mouseHook = IntPtr.Zero;
            }
        }

        private IntPtr PassiveMouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            int message = (int)wParam;
            if (nCode >= 0 && (message == NativeMethods.WM_LBUTTONDOWN || message == NativeMethods.WM_LBUTTONUP ||
                               message == NativeMethods.WM_RBUTTONDOWN || message == NativeMethods.WM_RBUTTONUP))
            {
                try
                {
                    var info = (NativeMethods.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                    TrackDesktopInput(message, info.pt, (info.flags & 1) == 0); // LLMHF_INJECTED
                }
                catch { ResetDesktopInput(); }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr PassiveKeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (_desktopDrag.Pressed || _desktopMenu.FenceId != null) &&
                ((int)wParam == 0x0100 || (int)wParam == 0x0104))
            {
                var key = (Keys)System.Runtime.InteropServices.Marshal.ReadInt32(lParam);
                bool releasedPopup = _popupPress != null && _popupPress.Released;
                if (_desktopMenu.ShouldCancelOnKey(key, releasedPopup))
                    ResetDesktopInput();
                else if (_desktopMenu.FenceId != null && _desktopMenu.Command == DesktopMenuCommand.None && !releasedPopup)
                {
                    if (key == Keys.Return && _highlightedItem != null && _highlightedItem.IsActive())
                        ConfirmMenuCommand(_highlightedItem.Command, _knownIconCells.Keys.ToArray(), _sessionClock.ElapsedMilliseconds,
                            NativeMethods.GetClipboardSequenceNumber());
                    else
                        QueueMenuHighlightRead();
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private void ClearRightClickFallback()
        {
            _recentRightClickFenceId = null;
            _recentRightClickMs = -1;
            _recentRightClickConsumedMs = -1;
        }

        private void ClearExternalDrop()
        {
            _nonDesktopPressActive = false;
            _externalDropFenceId = null;
            _externalDropMs = -1;
        }

        private void ResetDesktopInput()
        {
            ++_pressSequence;
            _desktopDrag.Cancel();
            _desktopMenu.Cancel();
            _popupPress = null;
            _highlightedItem = null;
            ClearRightClickFallback();
            ClearExternalDrop();
        }

        private void QueueMenuHighlightRead()
        {
            if (_readingMenuHighlight) return;
            uint pid;
            uint thread = NativeMethods.GetWindowThreadProcessId(_controller.ListView, out pid);
            if (thread == 0) return;
            int sequence = _pressSequence;
            _readingMenuHighlight = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                NativeMethods.HighlightedMenuItem item = null;
                try { item = NativeMethods.ReadHighlightedMenuItem(thread); }
                catch (System.Runtime.InteropServices.ExternalException) { }
                try
                {
                    _uiInvoker.BeginInvoke((Action)(() =>
                    {
                        _readingMenuHighlight = false;
                        if (sequence == _pressSequence && _desktopMenu.FenceId != null)
                            _highlightedItem = item;
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private void TrackDesktopInput(int message, NativeMethods.POINT nativePoint, bool physicalMouse)
        {
            if (_isMutatingFence || _isDrawingRegion || _forms.Count == 0 || _controller.ListView == IntPtr.Zero)
            {
                ResetDesktopInput();
                return;
            }
            Point point = new Point(nativePoint.X, nativePoint.Y);
            if (message == NativeMethods.WM_RBUTTONDOWN)
            {
                ResetDesktopInput();
                if (IsExposedDesktopPoint(point.X, point.Y))
                {
                    RecordDesktopBackgroundClick(point);
                }
                return;
            }
            if (message == NativeMethods.WM_RBUTTONUP)
            {
                if (_desktopMenu.FenceId != null && (!IsExposedDesktopPoint(point.X, point.Y) ||
                    Math.Abs(point.X - _desktopMenu.Click.X) > SystemInformation.DragSize.Width ||
                    Math.Abs(point.Y - _desktopMenu.Click.Y) > SystemInformation.DragSize.Height))
                    ResetDesktopInput();
                return;
            }

            if (_desktopMenu.FenceId != null && _desktopMenu.Command == DesktopMenuCommand.None &&
                TrackPopupInput(message, nativePoint, point)) return;

            bool desktop = IsExposedDesktopPoint(point.X, point.Y);
            if (message == NativeMethods.WM_LBUTTONDOWN)
            {
                ResetDesktopInput();
                if (!physicalMouse) return;
                if (!desktop)
                {
                    // Remember presses starting in external windows (e.g. File Explorer folder)
                    // so dragging a file from outside directly into a fence can be attributed.
                    _nonDesktopPressActive = true;
                    return;
                }
                _desktopDrag.Begin(point, _inputSnapshot);
                int sequence = _pressSequence;
                _uiInvoker.BeginInvoke((Action)(() =>
                {
                    // Refresh selection after Explorer processes the press. Keep the cached pre-drop
                    // geometry for fast gestures that finished before this callback ran.
                    if (sequence != _pressSequence || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) == 0) return;
                    var icons = _controller.GetAllIcons();
                    if (sequence == _pressSequence && _controller.SnapshotComplete) _desktopDrag.Begin(point, icons);
                }));
            }
            else
            {
                ++_pressSequence;
                bool externalDrag = _nonDesktopPressActive;
                _nonDesktopPressActive = false;
                if (externalDrag && desktop && physicalMouse)
                {
                    FenceConfig dropZone = FindFenceVisualBodyAt(point);
                    if (dropZone != null)
                    {
                        _externalDropFenceId = dropZone.Id;
                        _externalDropPt = point;
                        _externalDropMs = _sessionClock.ElapsedMilliseconds;
                        _uiInvoker.BeginInvoke((Action)TriggerSubFrameBurstCheck);
                    }
                    return;
                }
                bool pressed = _desktopDrag.Pressed;
                _desktopDrag.Release(point, desktop && physicalMouse, SystemInformation.DragSize, _sessionClock.ElapsedMilliseconds);
                if (pressed && desktop) _uiInvoker.BeginInvoke((Action)TriggerSubFrameBurstCheck);
            }
        }

        private void ConfirmMenuCommand(DesktopMenuCommand command, string[] existing, long now, uint clipboard)
        {
            _desktopMenu.Confirm(command, existing, null, now);
            if (command != DesktopMenuCommand.Paste) return;
            int sequence = _pressSequence;
            var reader = new System.Threading.Thread(delegate()
            {
                try
                {
                    if (clipboard == 0 || clipboard != NativeMethods.GetClipboardSequenceNumber()) return;
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    var paths = Clipboard.GetFileDropList().Cast<string>()
                        .Select(p => Path.GetFileName(p))
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Select(n => Path.Combine(desktop, n)).ToArray();
                    if (clipboard != NativeMethods.GetClipboardSequenceNumber()) return;
                    _uiInvoker.BeginInvoke((Action)(() =>
                    {
                        if (sequence == _pressSequence && _desktopMenu.Command == DesktopMenuCommand.Paste)
                            _desktopMenu.Confirm(command, existing, paths, now);
                    }));
                }
                catch (System.Runtime.InteropServices.ExternalException) { }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            });
            reader.IsBackground = true;
            reader.SetApartmentState(System.Threading.ApartmentState.STA);
            reader.Start();
        }

        private bool TrackPopupInput(int message, NativeMethods.POINT nativePoint, Point point)
        {
            IntPtr hwnd = NativeMethods.WindowFromPoint(nativePoint);
            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            var name = new System.Text.StringBuilder(100);
            NativeMethods.GetClassName(root, name, name.Capacity);
            string cls = name.ToString();
            bool classic = cls == "#32768";
            if (!classic && cls.IndexOf("Xaml", StringComparison.OrdinalIgnoreCase) < 0 &&
                cls.IndexOf("Microsoft.UI.", StringComparison.OrdinalIgnoreCase) < 0 &&
                cls != "Windows.UI.Core.CoreWindow") return false;
            uint pid, desktopPid;
            uint tid = NativeMethods.GetWindowThreadProcessId(root, out pid);
            uint desktopTid = NativeMethods.GetWindowThreadProcessId(_controller.ListView, out desktopPid);
            if (pid != desktopPid || tid == 0 || (tid != desktopTid &&
                NativeMethods.GetAncestor(root, 3) != NativeMethods.GetAncestor(_controller.ListView, 3))) return false;

            if (message == NativeMethods.WM_LBUTTONDOWN)
            {
                var press = new PopupPress { Window = root, Start = point, Existing = _knownIconCells.Keys.ToArray(),
                    ClipboardSequence = NativeMethods.GetClipboardSequenceNumber() };
                _popupPress = press;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    // UI Automation providers can take time and must never block the input hook.
                    Rectangle bounds = Rectangle.Empty;
                    DesktopMenuCommand command = DesktopMenuCommand.None;
                    try
                    {
                        if (NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(nativePoint), NativeMethods.GA_ROOT) == press.Window)
                        {
                            if (classic) command = NativeMethods.ReadClassicMenuItem(press.Window, nativePoint, out bounds);
                            if (command == DesktopMenuCommand.None)
                                command = ReadAutomationMenuCommand(press.Start, pid, out bounds);
                        }
                    }
                    catch (Exception)
                    {
                        // Explorer may dismiss the menu between any two provider calls.
                        command = DesktopMenuCommand.None;
                    }
                    try
                    {
                        _uiInvoker.BeginInvoke((Action)(() =>
                        {
                            if (_popupPress != press) return;
                            press.Bounds = bounds;
                            press.Command = command;
                            press.Inspected = true;
                            FinishAutomationPopup(press);
                        }));
                    }
                    catch (InvalidOperationException) { /* The manager is shutting down. */ }
                });
            }
            else if (_popupPress != null && root == _popupPress.Window)
            {
                _popupPress.End = point;
                _popupPress.Released = true;
                _popupPress.ReleasedAt = _sessionClock.ElapsedMilliseconds;
                FinishAutomationPopup(_popupPress);
            }
            return true;
        }

        private static string NormalizeMenuCaption(string name)
        {
            name = (name ?? "").Split('\t')[0].Replace("&", "").Trim();
            return System.Text.RegularExpressions.Regex.Replace(name,
                @"\s*[（(](?:[a-zA-Z]|Ctrl\s*\+\s*V)[)）]$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        }

        internal static bool IsNewSubmenu(string name)
        {
            string caption = NormalizeMenuCaption(name);
            return string.Equals(caption, "New", StringComparison.OrdinalIgnoreCase) || caption == "新建" || caption == "新增";
        }

        internal static DesktopMenuCommand ClassifyMenuItem(string name, bool inNewMenu)
        {
            name = NormalizeMenuCaption(name);
            if (string.Equals(name, "Paste", StringComparison.OrdinalIgnoreCase) || name == "粘贴" || name == "貼上")
                return DesktopMenuCommand.Paste;
            if (inNewMenu || string.Equals(name, "New folder", StringComparison.OrdinalIgnoreCase) || name == "新建文件夹")
                return DesktopMenuCommand.NewItem;
            return DesktopMenuCommand.None;
        }

        private static DesktopMenuCommand ReadAutomationMenuCommand(Point point, uint pid, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            var walker = TreeWalker.ControlViewWalker;
            var element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            string leaf = null;
            bool inNewMenu = false;
            for (int depth = 0; element != null && depth < 12; depth++, element = walker.GetParent(element))
            {
                var current = element.Current;
                if (current.ProcessId != pid) break;
                string label = current.Name == null ? "" : current.Name.Replace("&", "").Trim();
                var type = current.ControlType;
                if (leaf == null && (type == ControlType.MenuItem || type == ControlType.Button))
                {
                    if (!current.IsEnabled) return DesktopMenuCommand.None;
                    var rect = current.BoundingRectangle;
                    if (rect.IsEmpty) return DesktopMenuCommand.None;
                    bounds = Rectangle.FromLTRB((int)rect.Left, (int)rect.Top, (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom));
                    leaf = label;
                    DesktopMenuCommand direct = ClassifyMenuItem(leaf, false);
                    if (direct != DesktopMenuCommand.None) return direct;
                }
                else if (leaf != null && (type == ControlType.Menu || type == ControlType.MenuItem) && IsNewSubmenu(label))
                {
                    inNewMenu = true;
                    break;
                }
            }
            return leaf == null ? DesktopMenuCommand.None : ClassifyMenuItem(leaf, inNewMenu);
        }

        private void FinishAutomationPopup(PopupPress press)
        {
            if (!press.Released || !press.Inspected) return;
            _popupPress = null;
            if (press.Command != DesktopMenuCommand.None && press.Bounds.Contains(press.Start) && press.Bounds.Contains(press.End))
                ConfirmMenuCommand(press.Command, press.Existing, press.ReleasedAt, press.ClipboardSequence);
            else if (press.MenuEnded)
            {
                _desktopMenu.Cancel();
                // If UIA/classic inspection cleanly resolved a non-creation menu item (e.g. Refresh/Properties),
                // disarm the fallback window immediately so unrelated background files aren't sucked into the fence.
                if (press.Command == DesktopMenuCommand.None && !press.Bounds.IsEmpty)
                    ClearRightClickFallback();
            }
        }

        private bool IsExposedDesktopPoint(int screenX, int screenY)
        {
            IntPtr hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = screenX, Y = screenY });
            if (hwnd == IntPtr.Zero) return false;

            if (!DesktopReady) return false;
            if (hwnd == _controller.ListView || hwnd == _controller.DefView || hwnd == _controller.DesktopOwner)
                return true;

            // Check if WindowFromPoint landed on our own transparent interior of a FenceChromeForm / Backdrop
            foreach (var form in _forms.Values)
            {
                if (form.IsHandleCreated && hwnd == form.Handle)
                {
                    Point local = new Point(screenX - form.Left, screenY - form.Top);
                    int bt = form.RegionBorderThickness + 3;
                    // Right-clicking title bar or resize border opens DeskFences' own menu, NOT desktop menu
                    bool onChrome = local.Y <= form.HeaderHeight || local.X <= bt || local.X >= form.Width - bt || local.Y >= form.Height - bt;
                    return !onChrome;
                }
                if (form.Backdrop != null && form.Backdrop.IsHandleCreated && hwnd == form.Backdrop.Handle)
                    return true;
            }

            return false;
        }

        private FenceConfig FindFenceVisualBodyAt(Point point)
        {
            foreach (var form in _forms.Values)
            {
                if (!form.IsHandleCreated || form.IsDisposed || !form.Visible) continue;
                int bt = form.RegionBorderThickness + 2;
                Rectangle visualBody = new Rectangle(
                    form.Left + bt,
                    form.Top + form.HeaderHeight,
                    Math.Max(0, form.Width - bt * 2),
                    Math.Max(0, form.Height - form.HeaderHeight - bt));
                if (visualBody.Contains(point))
                    return form.Config;
            }
            return null;
        }

        private void RecordDesktopBackgroundClick(Point point)
        {
            FenceConfig hitZone = FindFenceVisualBodyAt(point);
            if (hitZone != null)
            {
                _desktopMenu.Open(hitZone.Id, point);
                _recentRightClickFenceId = hitZone.Id;
                _recentRightClickPt = point;
                _recentRightClickMs = _sessionClock.ElapsedMilliseconds;
                _recentRightClickConsumedMs = -1;
            }
            else
            {
                ClearRightClickFallback();
            }
        }

        private bool IsDesktopOrExplorerForeground()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return true;
            if (!DesktopReady) return false;
            if (fg == _controller.DesktopOwner || fg == _controller.DefView || fg == _controller.ListView)
                return true;

            var sb = new System.Text.StringBuilder(64);
            NativeMethods.GetClassName(fg, sb, sb.Capacity);
            string cls = sb.ToString();
            // Explicitly exclude CabinetWClass (File Explorer folder windows) so files created
            // while using a folder window cannot trigger a stale desktop right-click fallback.
            return cls == "Progman" || cls == "WorkerW" || cls == "XamlExplorerHostIslandWindow" ||
                   cls == "#32768" || cls == "Windows.UI.Core.CoreWindow";
        }

        public static bool ShouldAdoptNewIconInZone(
            string hitFenceId,
            string pendingRightClickFenceId,
            long elapsedSinceRightClickMs,
            long elapsedSinceFirstConsumedMs,
            bool isDesktopOrExplorerForeground)
        {
            if (string.IsNullOrEmpty(hitFenceId) || string.IsNullOrEmpty(pendingRightClickFenceId))
                return false;
            if (!string.Equals(hitFenceId, pendingRightClickFenceId, StringComparison.Ordinal))
                return false;
            if (elapsedSinceRightClickMs < 0 || elapsedSinceRightClickMs > 12000)
                return false;
            if (elapsedSinceFirstConsumedMs >= 0 && elapsedSinceFirstConsumedMs > 2000)
                return false;
            if (!isDesktopOrExplorerForeground)
                return false;
            return true;
        }

        public static bool FindNearestFreeInnerCell(
            FenceConfig fence,
            Point refScreenPt,
            DesktopGridMetrics m,
            HashSet<long> occupied,
            out Point bestCell)
        {
            bestCell = Point.Empty;
            if (fence == null) return false;
            Func<int, int, long> keyOf = (c, r) => (((long)c) << 32) | (uint)r;
            int cw = Math.Max(1, m.CellWidth);
            int ch = Math.Max(1, m.CellHeight);

            bool found = false;
            double bestDistSq = double.MaxValue;
            for (int r = fence.OuterRow + 1; r < fence.OuterRow + fence.OuterRows - 1; r++)
            {
                double cy = m.OriginY + (r + 0.5) * ch;
                double dy = cy - refScreenPt.Y;
                for (int c = fence.OuterCol + 1; c < fence.OuterCol + fence.OuterCols - 1; c++)
                {
                    if (occupied != null && occupied.Contains(keyOf(c, r))) continue;
                    double cx = m.OriginX + (c + 0.5) * cw;
                    double dx = cx - refScreenPt.X;
                    double distSq = dx * dx + dy * dy;
                    if (!found || distSq < bestDistSq)
                    {
                        found = true;
                        bestDistSq = distSq;
                        bestCell = new Point(c, r);
                    }
                }
            }
            return found;
        }

        public static bool FindNearestFreeOutsideCell(
            IEnumerable<FenceConfig> fences,
            Point refScreenPt,
            int maxCols, int maxRows,
            DesktopGridMetrics m,
            HashSet<long> occupied,
            out Point bestCell)
        {
            return FindNearestFreeOutsideCell(fences, refScreenPt, new[] { new Rectangle(0, 0, maxCols, maxRows) },
                m, occupied, out bestCell);
        }

        internal static bool FindNearestFreeOutsideCell(
            IEnumerable<FenceConfig> fences, Point refScreenPt, Rectangle[] areas,
            DesktopGridMetrics m, HashSet<long> occupied, out Point bestCell)
        {
            bestCell = Point.Empty;
            Func<int, int, long> keyOf = (c, r) => (((long)c) << 32) | (uint)r;
            int cw = Math.Max(1, m.CellWidth);
            int ch = Math.Max(1, m.CellHeight);

            var fenceList = fences != null ? fences.ToList() : new List<FenceConfig>();
            bool found = false;
            double bestDistSq = double.MaxValue;

            foreach (Rectangle area in areas)
            {
                for (int r = area.Top; r < area.Bottom; r++)
                {
                    double cy = m.OriginY + (r + 0.5) * ch;
                    double dy = cy - refScreenPt.Y;
                    double dySq = dy * dy;
                    if (dySq > bestDistSq) continue;

                    for (int c = area.Left; c < area.Right; c++)
                    {
                        if (occupied != null && occupied.Contains(keyOf(c, r))) continue;

                        bool inAny = false;
                        foreach (var f in fenceList)
                        {
                            if (f.IsInOuterRect(c, r)) { inAny = true; break; }
                        }
                        if (inAny) continue;

                        double cx = m.OriginX + (c + 0.5) * cw;
                        double dx = cx - refScreenPt.X;
                        double distSq = dx * dx + dySq;
                        if (!found || distSq < bestDistSq)
                        {
                            found = true;
                            bestDistSq = distSq;
                            bestCell = new Point(c, r);
                        }
                    }
                }
            }
            return found;
        }

        private void InstallDesktopWinEventHook()
        {
            UninstallDesktopWinEventHook();
            try
            {
                if (!_controller.HasCurrentHandles()) return;

                uint pid;
                uint tid = NativeMethods.GetWindowThreadProcessId(_controller.ListView, out pid);
                if (pid == 0 || tid == 0) return;

                _winEventProc = OnDesktopWinEvent;
                _winEventHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_SYSTEM_MENUEND,
                    NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero,
                    _winEventProc,
                    pid,
                    tid,
                    NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
            }
            catch { _winEventHook = IntPtr.Zero; }
        }

        private void UninstallDesktopWinEventHook()
        {
            if (_winEventHook != IntPtr.Zero)
            {
                try { NativeMethods.UnhookWinEvent(_winEventHook); } catch { }
                _winEventHook = IntPtr.Zero;
            }
        }

        private void OnDesktopWinEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (hWinEventHook != _winEventHook) return;
            if (eventType == NativeMethods.EVENT_SYSTEM_MENUEND)
            {
                _highlightedItem = null;
                if (_popupPress != null && _popupPress.Released) _popupPress.MenuEnded = true;
                else if (_desktopMenu.Command == DesktopMenuCommand.None) _desktopMenu.Cancel();
                return;
            }
            if (_desktopMenu.FenceId != null && _desktopMenu.Command == DesktopMenuCommand.None &&
                (eventType == 0x0006 || eventType == NativeMethods.EVENT_OBJECT_FOCUS || eventType == 0x8006))
                QueueMenuHighlightRead(); // MENUPOPUPSTART, FOCUS, SELECTION
            if (eventType < NativeMethods.EVENT_OBJECT_CREATE || !DesktopReady ||
                _isMutatingFence || _isEnforcing || _isAnimatingIcons || _isDrawingRegion || _forms.Count == 0)
                return;
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0)
                return;

            long nowMs = _sessionClock.ElapsedMilliseconds;
            bool inContextOrBurst = _desktopMenu.Command != DesktopMenuCommand.None
                || (!string.IsNullOrEmpty(_recentRightClickFenceId) && _recentRightClickMs >= 0 && (nowMs - _recentRightClickMs) <= 12000)
                || (!string.IsNullOrEmpty(_externalDropFenceId) && _externalDropMs >= 0 && (nowMs - _externalDropMs) <= 1800)
                || _recentContextPlacements.Count > 0
                || nowMs <= _burstUntilMs;

            // React within <1ms (sub-frame!) whenever SysListView32 inserts/reorders an item
            // or opens/focuses its inline rename Edit control!
            bool relevant = (hwnd == _controller.ListView &&
                             (eventType == NativeMethods.EVENT_OBJECT_CREATE || eventType == NativeMethods.EVENT_OBJECT_REORDER))
                         || (inContextOrBurst &&
                             (eventType == NativeMethods.EVENT_OBJECT_CREATE ||
                              eventType == NativeMethods.EVENT_OBJECT_SHOW ||
                              eventType == NativeMethods.EVENT_OBJECT_FOCUS ||
                              eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE));
            if (!relevant) return;

            _desktopDirty = true;
            EnforceAllRules(true, true);
        }

        private FileSystemWatcher CreateDesktopDirWatcher(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };
                watcher.Created += OnDesktopFileChanged;
                watcher.Renamed += OnDesktopFileChanged;
                watcher.Deleted += OnDesktopFileChanged;
                return watcher;
            }
            catch { return null; }
        }

        private void InstallDesktopFileWatcher()
        {
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            _desktopWatcher = CreateDesktopDirWatcher(userDesktop);
            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.Equals(commonDesktop, userDesktop, StringComparison.OrdinalIgnoreCase))
                _commonDesktopWatcher = CreateDesktopDirWatcher(commonDesktop);
        }

        private void OnDesktopFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (_uiInvoker != null && !_uiInvoker.IsDisposed && _uiInvoker.IsHandleCreated)
                {
                    _uiInvoker.BeginInvoke((Action)(() =>
                    {
                        _controller.InvalidateIdentityCache();
                        _desktopDirty = true;
                        foreach (var form in _forms.Values) form.ClearDragPreWarm();
                        TriggerSubFrameBurstCheck();
                    }));
                }
            }
            catch { }
        }

        private void TriggerSubFrameBurstCheck()
        {
            if (!DesktopReady || _isMutatingFence || _isDrawingRegion || _forms.Count == 0) return;
            _desktopDirty = true;
            _burstUntilMs = _sessionClock.ElapsedMilliseconds + 450;
            if (!_burstWatchTimer.Enabled)
                _burstWatchTimer.Start();

            if (!_isEnforcing && !_isAnimatingIcons && (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) == 0)
                EnforceAllRules(true, true);
        }

        private void OnBurstWatchTimerTick(object sender, EventArgs e)
        {
            long nowMs = _sessionClock.ElapsedMilliseconds;
            if (nowMs > _burstUntilMs)
            {
                _burstWatchTimer.Stop();
                return;
            }
            if (_isMutatingFence || _isEnforcing || _isAnimatingIcons || _isDrawingRegion || _forms.Count == 0)
                return;
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0)
                return;

            EnforceAllRules(true, true);
        }

        internal bool CanCommitToCurrentDesktop()
        {
            return _attachedGeneration == _controller.HostGeneration && _controller.HasCurrentHandles();
        }

        internal bool DesktopReady
        {
            get { return !_exiting && !_rebindingDesktop && !_layoutPending &&
                _attachedGeneration == _controller.HostGeneration && _controller.HasCurrentHandles(); }
        }

        private void RefreshDesktopHostHandles(bool displayChanged)
        {
            if (_exiting) return;
            if (_rebindingDesktop || _isEnforcing || _forms.Values.Any(form => form.CompletingInteraction))
            {
                _deferredDisplayChange |= displayChanged;
                return;
            }
            displayChanged |= _deferredDisplayChange;
            _deferredDisplayChange = false;
            bool available = _controller.RefreshHandles();
            bool hostChanged = _attachedGeneration != _controller.HostGeneration;
            if (!available && !hostChanged) return;

            bool missingWindow = available && _forms.Values.Any(f =>
                f.IsDisposed || !f.IsHandleCreated || !NativeMethods.IsWindow(f.Handle) ||
                f.Backdrop.IsDisposed || !f.Backdrop.IsHandleCreated || !NativeMethods.IsWindow(f.Backdrop.Handle));
            bool interacting = _isMutatingFence || _forms.Values.Any(f => f.IsInteracting);
            bool layerChanged = false;
            if (available && (hostChanged || displayChanged || (!interacting && _sessionClock.ElapsedMilliseconds >= _nextLayerCheckMs)))
            {
                IntPtr parent, after;
                NativeMethods.FindDesktopInterlayerHost(out parent, out after, hostChanged || displayChanged);
                layerChanged = parent != _interlayerParent || after != _interlayerInsertAfter;
                _interlayerParent = parent;
                _interlayerInsertAfter = after;
                _nextLayerCheckMs = _sessionClock.ElapsedMilliseconds + 2000;
                if (!hostChanged && !_layoutPending && _winEventHook == IntPtr.Zero) InstallDesktopWinEventHook();
                // If Wallpaper Engine started after DeskFences and slipped between SHELLDLL_DefView
                // and our backdrops, re-seat backdrops right behind desktop icons without rebuilding forms.
                if (!layerChanged && !hostChanged && !displayChanged && _interlayerParent != IntPtr.Zero)
                {
                    foreach (var form in _forms.Values)
                    {
                        if (form.Visible && form.Config.BackdropOpacity > 0.01 &&
                            form.Backdrop != null && form.Backdrop.IsHandleCreated &&
                            !NativeMethods.IsBackdropOrderedInInterlayer(form.Backdrop.Handle, _interlayerParent, _interlayerInsertAfter))
                        {
                            form.SendToDesktopBottom();
                        }
                    }
                }
            }
            if (!hostChanged && !layerChanged && !displayChanged && !missingWindow && !_layoutPending) return;

            _rebindingDesktop = true;
            try
            {
                bool sameView = available && !hostChanged;
                FinishIconAnimationImmediately(sameView);
                foreach (var form in _forms.Values.ToList())
                {
                    form.ClearDragPreWarm();
                    form.SettleInteraction(sameView, false);
                }
                _burstWatchTimer.Stop();
                _burstUntilMs = -1;
                ResetDesktopInput();
                _recentContextPlacements.Clear();
                _inputSnapshot = new List<DesktopIconInfo>();
                _knownIconCells.Clear();
                _attachedGeneration = _controller.HostGeneration;
                if (hostChanged)
                {
                    _membership.ResetObservations();
                    UninstallDesktopWinEventHook();
                }
                if (!available)
                {
                    _layoutPending = false;
                    _interlayerParent = _interlayerInsertAfter = IntPtr.Zero;
                    CloseFenceForms();
                    return;
                }
                var icons = _controller.GetAllIcons();
                if (!_controller.SnapshotComplete)
                {
                    _layoutPending = true;
                    CloseFenceForms();
                    return;
                }
                bool rebuild = hostChanged || missingWindow || _layoutPending;
                _layoutPending = false;
                var metrics = _controller.GetGridMetrics(icons);
                if (_needsInitialFence)
                {
                    CreateDefaultInitialFence(metrics);
                    _needsInitialFence = false;
                }
                else
                {
                    ResolveExistingOverlaps(metrics);
                    if (rebuild)
                        RebuildAllForms(metrics);
                    else
                    {
                        foreach (var form in _forms.Values)
                        {
                            form.UpdateDpiScale();
                            form.AttachToDesktopLayer();
                            form.SyncBoundsFromGrid(metrics);
                        }
                    }
                }
                if (_winEventHook == IntPtr.Zero) InstallDesktopWinEventHook();
            }
            finally { _rebindingDesktop = false; }
            InitializeKnownIconsAndEnforce();
        }

        internal bool PrepareFenceInteraction()
        {
            if (!DesktopReady || _forms.Values.Any(form => form.CompletingInteraction)) return false;
            foreach (var form in _forms.Values.ToList())
                form.SettleInteraction(true, true);
            // A nested input message during a Shell call must not replace the in-flight drop.
            return DesktopReady && !_isMutatingFence;
        }

        public void GetInterlayerHost(out IntPtr parentHwnd, out IntPtr insertAfterHwnd)
        {
            parentHwnd = _interlayerParent;
            insertAfterHwnd = _interlayerInsertAfter;
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            PostDesktopUpdate(() => RefreshDesktopHostHandles(true));
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Desktop)
                PostDesktopUpdate(() => RefreshDesktopHostHandles(true));
            else if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
            {
                PostDesktopUpdate(() =>
                {
                    RefreshTrayVisuals();
                    foreach (var form in _forms.Values) form.RefreshVisuals();
                });
            }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            SettleBeforeExit();
            if (!_needsInitialFence) SaveConfig();
        }

        private void PostDesktopUpdate(Action update)
        {
            try
            {
                if (!_exiting && _uiInvoker.IsHandleCreated)
                    _uiInvoker.BeginInvoke((Action)(() => { if (!_exiting) update(); }));
            }
            catch (InvalidOperationException) { } // Shutdown can dispose the invoker between the check and post.
        }

        public IntPtr GetDesktopOwner()
        {
            return _controller.HasCurrentHandles() ? _controller.DesktopOwner : IntPtr.Zero;
        }

        public DesktopGridMetrics GetGridMetrics()
        {
            return _controller.GetGridMetrics();
        }

        private ContextMenuStrip BuildTrayContextMenu(float dpiScale, bool sysLight, Color accent)
        {
            var menu = Win11MenuRenderer.CreateWin11Menu(sysLight, accent, dpiScale);
            menu.Items.Add(Win11MenuRenderer.CreateMenuItem("框选桌面区域新建分区", "\uE7A8", dpiScale, (s, e) => StartRegionDrawNewFence()));
            menu.Items.Add(Win11MenuRenderer.CreateMenuItem("新建桌面分区", "\uE710", dpiScale, (s, e) => CreateNewFence()));
            menu.Items.Add(new ToolStripSeparator());

            _miLock = Win11MenuRenderer.CreateMenuItem("锁定所有分区位置与大小", "\uE72E", dpiScale, (s, e) =>
            {
                Config.Locked = !Config.Locked;
                SaveConfig();
                UpdateTrayStates();
            });
            menu.Items.Add(_miLock);

            _miHideAll = Win11MenuRenderer.CreateMenuItem("隐藏所有分区边框", "\uED1A", dpiScale, (s, e) =>
            {
                _fencesHiddenByUser = !_fencesHiddenByUser;
                SyncDesktopVisibility();
                UpdateTrayStates();
            });
            menu.Items.Add(_miHideAll);

            menu.Items.Add(new ToolStripSeparator());
            _miStartup = Win11MenuRenderer.CreateMenuItem("开机自动启动", "\uE7B5", dpiScale, (s, e) => ToggleStartup());
            menu.Items.Add(_miStartup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Win11MenuRenderer.CreateMenuItem("退出 DeskFences", "\uE7E8", dpiScale, (s, e) => ExitApplication()));
            return menu;
        }

        private static string GetAppVersion()
        {
            var attrs = typeof(FenceManager).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
            if (attrs != null && attrs.Length > 0)
            {
                string ver = ((System.Reflection.AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion;
                if (!string.IsNullOrEmpty(ver)) return ver;
            }
            var v = typeof(FenceManager).Assembly.GetName().Version;
            return v != null ? string.Format("{0}.{1}.{2}", v.Major, v.Minor, v.Build) : "1.0.0";
        }

        private NotifyIcon CreateTrayIcon()
        {
            float dpiScale = NativeMethods.GetWindowDpiScale(IntPtr.Zero);
            bool sysLight = NativeMethods.IsSystemLightTheme();
            Color accent = NativeMethods.GetSystemAccentColor();

            _trayMenu = BuildTrayContextMenu(dpiScale, sysLight, accent);
            _trayAppIcon = CreateAppIcon();

            string trayText = string.Format("DeskFences v{0} - 轻量液态玻璃桌面分区", GetAppVersion());
            if (trayText.Length > 63) trayText = trayText.Substring(0, 63);
            var icon = new NotifyIcon
            {
                Icon = _trayAppIcon,
                Text = trayText,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            icon.DoubleClick += (s, e) => StartRegionDrawNewFence();
            return icon;
        }

        private void RefreshTrayVisuals()
        {
            if (_trayIcon == null) return;
            float dpiScale = NativeMethods.GetWindowDpiScale(IntPtr.Zero);
            bool sysLight = NativeMethods.IsSystemLightTheme();
            Color accent = NativeMethods.GetSystemAccentColor();

            var oldMenu = _trayMenu;
            var oldIcon = _trayAppIcon;
            _trayMenu = BuildTrayContextMenu(dpiScale, sysLight, accent);
            _trayAppIcon = CreateAppIcon();
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Icon = _trayAppIcon;
            UpdateTrayStates();
            if (oldMenu != null) oldMenu.Dispose();
            if (oldIcon != null) oldIcon.Dispose();
        }

        private void SyncDesktopVisibility()
        {
            bool visible = !_fencesHiddenByUser && _controller.IsDesktopVisible();
            _lastDesktopVisible = visible;
            foreach (var form in _forms.Values)
                form.SetDesktopVisibility(visible);
        }

        private static Icon CreateAppIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush bg = new SolidBrush(Color.FromArgb(242, 247, 253)))
                    g.FillEllipse(bg, 1, 1, 30, 30);
                using (Pen border = new Pen(NativeMethods.GetSystemAccentColor(), 2f))
                    g.DrawEllipse(border, 2, 2, 28, 28);

                // Minimalist 2x2 rounded bento-grid zone icon
                Color acc = NativeMethods.GetSystemAccentColor();
                using (Brush b1 = new SolidBrush(acc))
                using (Brush b2 = new SolidBrush(Color.FromArgb(145, acc)))
                {
                    using (GraphicsPath p1 = FenceChromeForm.CreateRoundRectPath(new Rectangle(8, 8, 7, 7), 2)) g.FillPath(b1, p1);
                    using (GraphicsPath p2 = FenceChromeForm.CreateRoundRectPath(new Rectangle(17, 8, 7, 7), 2)) g.FillPath(b2, p2);
                    using (GraphicsPath p3 = FenceChromeForm.CreateRoundRectPath(new Rectangle(8, 17, 7, 7), 2)) g.FillPath(b2, p3);
                    using (GraphicsPath p4 = FenceChromeForm.CreateRoundRectPath(new Rectangle(17, 17, 7, 7), 2)) g.FillPath(b1, p4);
                }

                IntPtr hIcon = bmp.GetHicon();
                Icon copy = (Icon)Icon.FromHandle(hIcon).Clone();
                NativeMethods.DestroyIcon(hIcon);
                return copy;
            }
        }

        private void UpdateTrayStates()
        {
            if (_miLock != null) _miLock.Checked = Config.Locked;
            if (_miHideAll != null) _miHideAll.Checked = _fencesHiddenByUser;
            if (_miStartup != null) _miStartup.Checked = IsStartupEnabled();
        }

        private void CreateDefaultInitialFence(DesktopGridMetrics m)
        {
            Rectangle primary = MonitorGrid.FromWorkArea(Screen.PrimaryScreen.WorkingArea, m);
            var desired = new Point(primary.Left + 2, primary.Top);
            Rectangle area = primary.Width >= 3 && primary.Height >= 3 ? primary :
                MonitorGrid.BestArea(MonitorGrid.GetAreas(m), desired, new Size(5, 4), m);
            if (area.IsEmpty) return;
            int outerCols = Math.Min(5, area.Width);
            int outerRows = Math.Min(4, area.Height);
            Point origin = MonitorGrid.ClampOrigin(area, desired, new Size(outerCols, outerRows));

            var fc = new FenceConfig
            {
                Title = "常用工具",
                OuterCol = origin.X,
                OuterRow = origin.Y,
                OuterCols = outerCols,
                OuterRows = outerRows,
                ThemeId = "liquid_clear",
                BackdropOpacity = 1.0,
                HeaderOpacity = 1.0
            };
            Config.Fences.Add(fc);
            SaveConfig();
            RebuildAllForms(m);
        }

        private void CloseFenceForms()
        {
            foreach (var form in _forms.Values)
            {
                try { form.Close(); form.Dispose(); } catch { }
            }
            _forms.Clear();
        }

        private void RebuildAllForms(DesktopGridMetrics m)
        {
            CloseFenceForms();
            bool visible = !_fencesHiddenByUser && _controller.IsDesktopVisible();
            _lastDesktopVisible = visible;
            foreach (var fc in Config.Fences)
            {
                var form = new FenceChromeForm(this, fc);
                _forms[fc.Id] = form;
                if (visible)
                {
                    form.Show();
                    form.SyncBoundsFromGrid(m);
                    form.SendToDesktopBottom();
                }
                else
                {
                    var ensure = form.Handle;
                    form.SyncBoundsFromGrid(m);
                    form.SetDesktopVisibility(false);
                }
            }
        }

        private void AcquireFreeIconMoveMode(bool snapCurrentlyEnabled)
        {
            if (snapCurrentlyEnabled) _controller.SuspendSnapToGrid();
        }

        private void ReleaseFreeIconMoveModeIfIdle()
        {
            if (_isMutatingFence || _isAnimatingIcons) return;
            _controller.RestoreSnapToGrid();
        }

        public static double EaseOutCubic(double t)
        {
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            double inv = 1.0 - t;
            return 1.0 - inv * inv * inv;
        }

        public static Point InterpolateIconPoint(Point start, Point target, double progress)
        {
            double u = EaseOutCubic(progress);
            int x = (int)Math.Round(start.X + (target.X - start.X) * u);
            int y = (int)Math.Round(start.Y + (target.Y - start.Y) * u);
            return new Point(x, y);
        }

        private void StartIconSlideAnimation(List<IconSlideTrack> tracks, bool snapCurrentlyEnabled)
        {
            if (tracks == null || tracks.Count == 0) return;

            FinishIconAnimationImmediately();

            double maxDist = 0;
            foreach (var tr in tracks)
            {
                double dx = tr.TargetPos.X - tr.StartPos.X;
                double dy = tr.TargetPos.Y - tr.StartPos.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d > maxDist) maxDist = d;
            }

            // Snappy yet silky ease-out cubic duration (~220ms for 1-cell nudge, up to ~340ms for longer glides)
            _iconAnimDurationMs = Math.Max(220.0, Math.Min(340.0, 185.0 + maxDist * 0.28));
            _iconAnimTracks = tracks;
            _isAnimatingIcons = true;

            AcquireFreeIconMoveMode(snapCurrentlyEnabled);
            _iconAnimWatch.Restart();
            _iconAnimTimer.Start();
        }

        private void OnIconAnimTimerTick(object sender, EventArgs e)
        {
            if (!DesktopReady || !_isAnimatingIcons || _iconAnimTracks == null || _iconAnimTracks.Count == 0)
            {
                FinishIconAnimationImmediately(false);
                return;
            }

            double t = _iconAnimWatch.Elapsed.TotalMilliseconds / Math.Max(1.0, _iconAnimDurationMs);
            bool done = (t >= 1.0);
            double clampedT = done ? 1.0 : t;

            var batch = new List<KeyValuePair<string, Point>>(_iconAnimTracks.Count);
            foreach (var tr in _iconAnimTracks)
            {
                Point next = done ? tr.TargetPos : InterpolateIconPoint(tr.StartPos, tr.TargetPos, clampedT);
                if (next != tr.LastSentPos)
                {
                    tr.LastSentPos = next;
                    batch.Add(new KeyValuePair<string, Point>(tr.IconInfo.Identity, next));
                }
            }

            if (batch.Count > 0)
            {
                _controller.MoveIconsBatch(batch);
            }

            if (done)
            {
                _iconAnimTimer.Stop();
                _iconAnimWatch.Stop();
                _iconAnimTracks = null;
                _isAnimatingIcons = false;
                ReleaseFreeIconMoveModeIfIdle();
            }
        }

        private void FinishIconAnimationImmediately(bool applyIconPositions = true)
        {
            if (!_isAnimatingIcons) return;

            _iconAnimTimer.Stop();
            _iconAnimWatch.Stop();

            try
            {
                if (!applyIconPositions || !_controller.HasCurrentHandles() ||
                    _attachedGeneration != _controller.HostGeneration || _iconAnimTracks == null) return;
                var finalBatch = new List<KeyValuePair<string, Point>>(_iconAnimTracks.Count);
                foreach (var tr in _iconAnimTracks)
                    finalBatch.Add(new KeyValuePair<string, Point>(tr.IconInfo.Identity, tr.TargetPos));
                _controller.MoveIconsBatch(finalBatch);
            }
            finally
            {
                _iconAnimTracks = null;
                _isAnimatingIcons = false;
                ReleaseFreeIconMoveModeIfIdle();
            }
        }

        public List<DesktopIconInfo> BeginFenceResize(FenceChromeForm form, out DesktopGridMetrics metrics)
        {
            return BeginFenceMutation(form, false, out metrics);
        }

        public void MoveIconsToClientPositions(List<KeyValuePair<string, Point>> moves)
        {
            if (_canMoveMembers && _attachedGeneration == _controller.HostGeneration && _controller.HasCurrentHandles())
                _controller.MoveIconsBatch(moves);
        }

        internal bool IsBusy
        {
            get { return _isMutatingFence || _isEnforcing || _isAnimatingIcons || _isDrawingRegion || _rebindingDesktop; }
        }

        internal int CurrentHostGeneration { get { return _controller.HostGeneration; } }
        internal long SessionElapsedMs { get { return _sessionClock.ElapsedMilliseconds; } }
        internal int GetDesktopItemCount() { return _controller.GetDesktopItemCount(); }

        internal bool IsDesktopListViewForeground()
        {
            if (!DesktopReady) return false;
            IntPtr fg = NativeMethods.GetForegroundWindow();
            return fg != IntPtr.Zero &&
                   (fg == _controller.DesktopOwner || fg == _controller.DefView || fg == _controller.ListView);
        }

        internal bool TryGetLiveMembersForPreWarm(
            FenceChromeForm form,
            out List<DesktopIconInfo> members,
            out int hostGen,
            out int itemCount)
        {
            members = null;
            hostGen = _controller.HostGeneration;
            itemCount = -1;
            if (!DesktopReady || IsBusy || form == null || form.Config.MemberIconIds.Count == 0)
                return false;

            int generation = _controller.HostGeneration;
            IntPtr view = _controller.ListView;
            var memberSet = new HashSet<string>(form.Config.MemberIconIds, StringComparer.OrdinalIgnoreCase);

            int liveCount = _controller.GetDesktopItemCount();
            if (!_desktopDirty &&
                _inputSnapshot != null && _inputSnapshot.Count > 0 &&
                _lastGridMetrics.CellWidth > 0 && _lastGridMetrics.CellHeight > 0 &&
                form.Config.MemberIconNames.Count == 0 &&
                liveCount == _inputSnapshot.Count &&
                _controller.HasShellItemCount(liveCount) &&
                _controller.GetRawItemSpacing() == _lastRawItemSpacing)
            {
                var metrics = _controller.GetLiveStyleMetrics(_lastGridMetrics);
                if (metrics.AutoArrangeEnabled) return false;
                var candidates = _inputSnapshot.FindAll(item => memberSet.Contains(item.Identity));
                List<DesktopIconInfo> fresh;
                if (_controller.TryRefreshIconsLive(candidates, out fresh) &&
                    DesktopReady && _controller.HostGeneration == generation && _controller.ListView == view)
                {
                    UpdateInputSnapshotSubset(fresh);
                    members = fresh.FindAll(item => form.Config.IsInInnerRect(item.GridCol(metrics), item.GridRow(metrics)));
                    hostGen = generation;
                    itemCount = liveCount;
                    return true;
                }
            }

            // Fallback: full live snapshot (read-only) also primes _inputSnapshot so the upcoming drag hits the 1ms fast path!
            var all = _controller.GetAllIcons(false);
            var fullMetrics = _controller.GetGridMetrics(all);
            if (!_controller.SnapshotComplete || !DesktopReady ||
                _controller.HostGeneration != generation || _controller.ListView != view)
                return false;
            _inputSnapshot = all;
            _lastGridMetrics = fullMetrics;
            _lastRawItemSpacing = _controller.GetRawItemSpacing();
            _lastItemCount = all.Count;
            if (fullMetrics.AutoArrangeEnabled) return false;
            members = all.FindAll(item =>
                memberSet.Contains(item.Identity) &&
                form.Config.IsInInnerRect(item.GridCol(fullMetrics), item.GridRow(fullMetrics)));
            hostGen = generation;
            itemCount = all.Count;
            return true;
        }

        private void UpdateInputSnapshotSubset(List<DesktopIconInfo> refreshed)
        {
            if (_inputSnapshot == null || _inputSnapshot.Count == 0 || refreshed == null || refreshed.Count == 0)
                return;
            foreach (var ic in refreshed)
            {
                if (ic.Index >= 0 && ic.Index < _inputSnapshot.Count &&
                    StringComparer.OrdinalIgnoreCase.Equals(_inputSnapshot[ic.Index].Identity, ic.Identity))
                {
                    _inputSnapshot[ic.Index] = ic;
                }
            }
        }

        public List<DesktopIconInfo> BeginFenceDrag(FenceChromeForm form, out DesktopGridMetrics metrics)
        {
            return BeginFenceMutation(form, true, out metrics);
        }

        private List<DesktopIconInfo> BeginFenceMutation(FenceChromeForm form, bool dragging, out DesktopGridMetrics metrics)
        {
            metrics = new DesktopGridMetrics();
            if (!DesktopReady || _isMutatingFence) return null;
            int generation = _controller.HostGeneration;
            IntPtr view = _controller.ListView;
            bool started = false;
            _isMutatingFence = true;
            _lastDropClean = false;
            try
            {
                FinishIconAnimationImmediately();
                // A preceding resize/collision animation may have temporarily suspended snap.
                // Finish it before inspecting the user's grid settings for a new interaction.
                _controller.RestoreSnapToGrid();

                var memberSet = new HashSet<string>(form.Config.MemberIconIds, StringComparer.OrdinalIgnoreCase);

                // Fast member-only live path on drag pickup: skips scanning all 50-100 non-member desktop icons
                // while still reading 100% live ClientPosition / ScreenBounds / IconBounds / LabelBounds / Selected
                // and verifying each member's ListView index & PIDL identity!
                if (dragging &&
                    !_desktopDirty &&
                    _inputSnapshot != null && _inputSnapshot.Count > 0 &&
                    _lastGridMetrics.CellWidth > 0 && _lastGridMetrics.CellHeight > 0 &&
                    form.Config.MemberIconNames.Count == 0 &&
                    _controller.GetDesktopItemCount() == _inputSnapshot.Count &&
                    _controller.HasShellItemCount(_inputSnapshot.Count) &&
                    _controller.GetRawItemSpacing() == _lastRawItemSpacing)
                {
                    metrics = _controller.GetLiveStyleMetrics(_lastGridMetrics);
                    if (metrics.AutoArrangeEnabled)
                    {
                        if (!_warnedAutoArrange)
                        {
                            _warnedAutoArrange = true;
                            _trayIcon.ShowBalloonTip(
                                5000,
                                "检测到 Windows 开启了“自动排列图标”",
                                "请在桌面空白处右键 → 查看 → 取消勾选“自动排列图标”，分区才能正常带动图标与自动避让。",
                                ToolTipIcon.Warning);
                        }
                        return null;
                    }

                    var candidates = _inputSnapshot.FindAll(item => memberSet.Contains(item.Identity));
                    List<DesktopIconInfo> freshMembers;
                    if (_controller.TryRefreshIconsLive(candidates, out freshMembers) &&
                        DesktopReady && _controller.HostGeneration == generation && _controller.ListView == view)
                    {
                        UpdateInputSnapshotSubset(freshMembers);
                        var fastGrid = metrics;
                        var fastCaptured = freshMembers.FindAll(item =>
                            form.Config.IsInInnerRect(item.GridCol(fastGrid), item.GridRow(fastGrid)));
                        _dragDesktop = _inputSnapshot;
                        _canMoveMembers = true;
                        started = true;
                        return fastCaptured;
                    }
                }

                // Full live scan fallback (used for resize, dirty desktop, item-count/spacing change, or index shift)
                var all = _controller.GetAllIcons(false);
                metrics = _controller.GetGridMetrics(all);
                if (!_controller.SnapshotComplete || !DesktopReady ||
                    _controller.HostGeneration != generation || _controller.ListView != view) return null;
                _inputSnapshot = all;
                _lastGridMetrics = metrics;
                _lastRawItemSpacing = _controller.GetRawItemSpacing();
                _lastItemCount = all.Count;

                if (metrics.AutoArrangeEnabled && !_warnedAutoArrange)
                {
                    _warnedAutoArrange = true;
                    _trayIcon.ShowBalloonTip(
                        5000,
                        "检测到 Windows 开启了“自动排列图标”",
                        "请在桌面空白处右键 → 查看 → 取消勾选“自动排列图标”，分区才能正常带动图标与自动避让。",
                        ToolTipIcon.Warning);
                }
                if (dragging && metrics.AutoArrangeEnabled) return null;
                if (dragging && form.Config.MemberIconNames.Count > 0)
                {
                    if (_membership.Reconcile(Config.Fences, all, metrics, _sessionClock.ElapsedMilliseconds))
                        SaveConfig();
                }
                var gridSnapshot = metrics;
                var captured = all.FindAll(item =>
                    memberSet.Contains(item.Identity) &&
                    (!dragging || form.Config.IsInInnerRect(item.GridCol(gridSnapshot), item.GridRow(gridSnapshot))));
                if (dragging)
                    _dragDesktop = all;
                _canMoveMembers = !metrics.AutoArrangeEnabled;
                if (!dragging && _canMoveMembers && captured.Count > 0)
                    AcquireFreeIconMoveMode(metrics.SnapToGridEnabled);
                started = true;
                return captured;
            }
            finally
            {
                if (!started)
                {
                    _isMutatingFence = false;
                    _canMoveMembers = false;
                    ReleaseFreeIconMoveModeIfIdle();
                }
            }
        }

        internal IntPtr GetDesktopListViewHandle()
        {
            return DesktopReady ? _controller.ListView : IntPtr.Zero;
        }

        internal bool ParkDragMembers(List<DesktopIconInfo> members, bool snapEnabled, Rectangle dirtyScreenRect)
        {
            if (!CanCommitToCurrentDesktop() || !_isMutatingFence || members == null || members.Count == 0) return false;
            bool parked = _controller.ParkIconsOffscreen(members, snapEnabled);
            if (parked)
                _controller.FlushDesktopPaint(dirtyScreenRect);
            return parked;
        }

        internal bool CommitRigidDragDrop(
            FenceChromeForm form,
            List<DesktopIconInfo> capturedMembers,
            DesktopGridMetrics grid,
            Rectangle[] workAreas,
            int startCol, int startRow,
            int targetCol, int targetRow,
            bool clearOutsiders,
            out bool snapRestored)
        {
            snapRestored = false;
            _pendingDropOutsiderTracks = null;
            _pendingDropSnapEnabled = false;
            if (!CanCommitToCurrentDesktop() || !_isMutatingFence || !_canMoveMembers || form == null)
                return false;

            int dCol = targetCol - startCol;
            int dRow = targetRow - startRow;
            int dx = dCol * grid.CellWidth;
            int dy = dRow * grid.CellHeight;

            bool committed = true;
            bool plannedOutsidersFromSnapshot = false;
            var outsiderTracks = new List<IconSlideTrack>();
            var expelledUpdates = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);

            _controller.SetListViewRedraw(false);
            try
            {
                // 1. Plan smooth glide tracks for any conflicting outsiders in the landing zone
                //    (keeping LVS_EX_SNAPTOGRID suspended so they glide smoothly instead of teleporting!).
                if (clearOutsiders && (dCol != 0 || dRow != 0) && _dragDesktop != null && _controller.HasShellItemCount(_dragDesktop.Count))
                {
                    bool blocked;
                    plannedOutsidersFromSnapshot = TryPlanLandingOutsidersFromSnapshot(
                        form, capturedMembers, _dragDesktop, grid, workAreas, targetCol, targetRow, dCol, dRow,
                        outsiderTracks, expelledUpdates, out blocked);
                    if (blocked)
                    {
                        committed = false;
                        outsiderTracks.Clear();
                        expelledUpdates.Clear();
                        dCol = 0; dRow = 0; dx = 0; dy = 0;
                    }
                }

                // 2. Restore parked member icons. Only use LVM_SETITEMPOSITION32 by index when every
                //    member's ListView index is verified unchanged; otherwise use PIDL-only IFolderView
                //    so a mid-drag file add/delete can NEVER teleport an unrelated desktop icon!
                if (capturedMembers != null && capturedMembers.Count > 0)
                {
                    var ordered = (capturedMembers.Count > 1 && (dx != 0 || dy != 0))
                        ? capturedMembers.OrderByDescending(m => (long)m.ClientPosition.X * dx + (long)m.ClientPosition.Y * dy).ToList()
                        : capturedMembers;
                    var directMoves = new List<KeyValuePair<int, Point>>(ordered.Count);
                    var pidlMoves = new List<KeyValuePair<string, Point>>(ordered.Count);
                    foreach (var mem in ordered)
                    {
                        Point targetPt = new Point(mem.ClientPosition.X + dx, mem.ClientPosition.Y + dy);
                        directMoves.Add(new KeyValuePair<int, Point>(mem.Index, targetPt));
                        pidlMoves.Add(new KeyValuePair<string, Point>(mem.Identity, targetPt));
                    }
                    if (_controller.VerifyMemberIndices(ordered))
                        _controller.MoveIconsDirectByIndex(directMoves);
                    _controller.MoveIconsBatch(pidlMoves);
                }

                // 3. Fallback outsider check if desktop item count changed mid-drag (e.g., file created during drag)
                if (committed && clearOutsiders && (dCol != 0 || dRow != 0) && !plannedOutsidersFromSnapshot)
                {
                    var all = _controller.GetAllIcons(true);
                    if (_controller.SnapshotComplete)
                    {
                        bool blocked;
                        PlanLandingOutsiders(form, capturedMembers, all, grid, workAreas, targetCol, targetRow, dCol, dRow,
                            outsiderTracks, expelledUpdates, out blocked);
                    }
                }

                // 4. If outsiders will smoothly glide out right after handoff, keep SnapToGrid suspended
                //    until the slide animation completes; otherwise restore SnapToGrid inside WM_SETREDRAW(false).
                if (outsiderTracks.Count > 0 && !_exiting)
                {
                    _pendingDropOutsiderTracks = outsiderTracks;
                    _pendingDropSnapEnabled = grid.SnapToGridEnabled;
                }
                else
                {
                    if (outsiderTracks.Count > 0)
                    {
                        var immediate = new List<KeyValuePair<string, Point>>(outsiderTracks.Count);
                        foreach (var tr in outsiderTracks)
                            immediate.Add(new KeyValuePair<string, Point>(tr.IconInfo.Identity, tr.TargetPos));
                        _controller.MoveIconsBatch(immediate);
                    }
                    snapRestored = _controller.RestoreSnapToGrid();
                }
            }
            finally
            {
                _controller.SetListViewRedraw(true);
            }

            // Update known cells AND _inputSnapshot so an immediate follow-up manual icon drag
            // (<350ms after dropping the fence) sees the icons' updated coordinates.
            if (capturedMembers != null)
            {
                var movedMap = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
                foreach (var mem in capturedMembers)
                {
                    _knownIconCells[mem.Identity] = new Point(mem.GridCol(grid) + dCol, mem.GridRow(grid) + dRow);
                    movedMap[mem.Identity] = new Point(dx, dy);
                }
                if (_inputSnapshot != null && _inputSnapshot.Count > 0)
                {
                    for (int i = 0; i < _inputSnapshot.Count; i++)
                    {
                        var ic = _inputSnapshot[i];
                        Point shift;
                        if (movedMap.TryGetValue(ic.Identity, out shift))
                        {
                            ic.ClientPosition.Offset(shift);
                            ic.ScreenBounds.Offset(shift);
                            ic.IconBounds.Offset(shift);
                            ic.LabelBounds.Offset(shift);
                            _inputSnapshot[i] = ic;
                        }
                        else if (expelledUpdates.TryGetValue(ic.Identity, out shift))
                        {
                            ic.ClientPosition.Offset(shift);
                            ic.ScreenBounds.Offset(shift);
                            ic.IconBounds.Offset(shift);
                            ic.LabelBounds.Offset(shift);
                            _inputSnapshot[i] = ic;
                        }
                    }
                }
            }

            _controller.FlushDesktopPaint(form.Bounds);
            _lastDropClean = committed && outsiderTracks.Count == 0 && ((dCol == 0 && dRow == 0) || plannedOutsidersFromSnapshot);
            return committed;
        }

        private bool TryPlanLandingOutsidersFromSnapshot(
            FenceChromeForm form,
            List<DesktopIconInfo> capturedMembers,
            List<DesktopIconInfo> desktopSnapshot,
            DesktopGridMetrics grid,
            Rectangle[] workAreas,
            int targetCol, int targetRow,
            int dCol, int dRow,
            List<IconSlideTrack> outsiderTracks,
            Dictionary<string, Point> expelledUpdates,
            out bool blocked)
        {
            blocked = false;
            var memberIds = new HashSet<string>(
                capturedMembers != null ? capturedMembers.Select(c => c.Identity) : Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var liveDesktop = new List<DesktopIconInfo>(desktopSnapshot.Count);
            foreach (var ic in desktopSnapshot)
            {
                if (memberIds.Contains(ic.Identity)) continue;
                Point livePos;
                if (!_controller.TryGetLiveIconPosition(ic.Identity, out livePos))
                    return false;
                var updated = ic;
                int shiftX = livePos.X - ic.ClientPosition.X;
                int shiftY = livePos.Y - ic.ClientPosition.Y;
                if (shiftX != 0 || shiftY != 0)
                {
                    updated.ClientPosition = livePos;
                    updated.ScreenBounds.Offset(shiftX, shiftY);
                    updated.IconBounds.Offset(shiftX, shiftY);
                    updated.LabelBounds.Offset(shiftX, shiftY);
                }
                liveDesktop.Add(updated);
            }

            PlanLandingOutsiders(form, capturedMembers, liveDesktop, grid, workAreas, targetCol, targetRow, dCol, dRow,
                outsiderTracks, expelledUpdates, out blocked);
            return true;
        }

        private void PlanLandingOutsiders(
            FenceChromeForm form,
            List<DesktopIconInfo> capturedMembers,
            List<DesktopIconInfo> desktopItems,
            DesktopGridMetrics grid,
            Rectangle[] workAreas,
            int targetCol, int targetRow,
            int dCol, int dRow,
            List<IconSlideTrack> outsiderTracks,
            Dictionary<string, Point> expelledUpdates,
            out bool blocked)
        {
            blocked = false;
            var memberIds = new HashSet<string>(
                capturedMembers != null ? capturedMembers.Select(c => c.Identity) : Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var proposed = new FenceConfig
            {
                Id = form.Config.Id,
                OuterCol = targetCol,
                OuterRow = targetRow,
                OuterCols = form.Config.OuterCols,
                OuterRows = form.Config.OuterRows
            };
            var proposedFences = new List<FenceConfig>(Config.Fences.Count);
            foreach (var f in Config.Fences)
                proposedFences.Add(string.Equals(f.Id, form.Config.Id, StringComparison.Ordinal) ? proposed : f);

            Func<int, int, long> keyOf = (c, r) => (((long)c) << 32) | (uint)r;
            var occupied = new HashSet<long>();
            var memberDestCells = new HashSet<long>();
            if (capturedMembers != null)
            {
                foreach (var mem in capturedMembers)
                {
                    long k = keyOf(mem.GridCol(grid) + dCol, mem.GridRow(grid) + dRow);
                    occupied.Add(k);
                    memberDestCells.Add(k);
                }
            }

            var outsiders = new List<DesktopIconInfo>();
            foreach (var ic in desktopItems)
            {
                if (memberIds.Contains(ic.Identity)) continue;
                int c = ic.GridCol(grid);
                int r = ic.GridRow(grid);
                if (proposed.IsInOuterRect(c, r))
                    outsiders.Add(ic);
                else
                    occupied.Add(keyOf(c, r));
            }

            if (outsiders.Count == 0) return;

            Rectangle[] areas = workAreas != null && workAreas.Length > 0 ? workAreas : MonitorGrid.GetAreas(grid);
            var plannedTracks = new List<IconSlideTrack>(outsiders.Count);
            var plannedCells = new List<KeyValuePair<string, Point>>(outsiders.Count);
            var plannedShifts = new List<KeyValuePair<string, Point>>(outsiders.Count);
            foreach (var ic in outsiders)
            {
                int curCol = ic.GridCol(grid);
                int curRow = ic.GridRow(grid);
                Point refPt = new Point(
                    ic.ScreenBounds.Left + ic.ScreenBounds.Width / 2,
                    ic.ScreenBounds.Top + ic.ScreenBounds.Height / 2);
                Point destCell;
                if (FindNearestFreeOutsideCell(proposedFences, refPt, areas, grid, occupied, out destCell))
                {
                    occupied.Add(keyOf(destCell.X, destCell.Y));
                    int sx = (destCell.X - curCol) * grid.CellWidth;
                    int sy = (destCell.Y - curRow) * grid.CellHeight;
                    Point targetPt = new Point(ic.ClientPosition.X + sx, ic.ClientPosition.Y + sy);
                    plannedTracks.Add(new IconSlideTrack
                    {
                        IconInfo = ic,
                        StartPos = ic.ClientPosition,
                        TargetPos = targetPt,
                        LastSentPos = ic.ClientPosition
                    });
                    plannedCells.Add(new KeyValuePair<string, Point>(ic.Identity, destCell));
                    plannedShifts.Add(new KeyValuePair<string, Point>(ic.Identity, new Point(sx, sy)));
                }
                else if (memberDestCells.Contains(keyOf(curCol, curRow)))
                {
                    // Desktop has zero free outside slots to move an outsider off a member's cell -> abort safely!
                    blocked = true;
                    return;
                }
            }
            if (plannedTracks.Count > 0)
            {
                outsiderTracks.AddRange(plannedTracks);
                foreach (var kv in plannedCells) _knownIconCells[kv.Key] = kv.Value;
                foreach (var kv in plannedShifts) expelledUpdates[kv.Key] = kv.Value;
            }
        }

        public void EndFenceMutation(FenceChromeForm mutatedForm, bool enforce = true, bool gridDrop = false)
        {
            var pendingTracks = _pendingDropOutsiderTracks;
            bool pendingSnap = _pendingDropSnapEnabled;
            _pendingDropOutsiderTracks = null;
            _pendingDropSnapEnabled = false;
            try
            {
                if (!_exiting) SaveConfig();
                if (enforce && DesktopReady)
                    EnforceAllRules(false, true, gridDrop);
            }
            finally
            {
                _dragDesktop = null;
                _canMoveMembers = false;
                _isMutatingFence = false;
                bool cleanDrop = _lastDropClean && !enforce && (pendingTracks == null || pendingTracks.Count == 0);
                _lastDropClean = false;
                if (!cleanDrop)
                    _desktopDirty = true;
                if (pendingTracks != null && pendingTracks.Count > 0 && CanCommitToCurrentDesktop())
                {
                    if (!_exiting)
                    {
                        StartIconSlideAnimation(pendingTracks, pendingSnap);
                    }
                    else
                    {
                        var immediate = new List<KeyValuePair<string, Point>>(pendingTracks.Count);
                        foreach (var tr in pendingTracks)
                            immediate.Add(new KeyValuePair<string, Point>(tr.IconInfo.Identity, tr.TargetPos));
                        _controller.MoveIconsBatch(immediate);
                        ReleaseFreeIconMoveModeIfIdle();
                    }
                }
                else
                {
                    ReleaseFreeIconMoveModeIfIdle();
                }
            }
        }

        private void InitializeKnownIconsAndEnforce()
        {
            var icons = _controller.GetAllIcons();
            if (!_controller.SnapshotComplete) return;
            var m = _controller.GetGridMetrics(icons);
            _knownIconCells.Clear();
            foreach (var ic in icons)
            {
                _knownIconCells[ic.Identity] = new Point(ic.GridCol(m), ic.GridRow(m));
            }
            EnforceAllRules(false, false);
        }

        private void OnWatchTimerTick(object sender, EventArgs e)
        {
            if (_exiting) return;
            if (_exitAfterInteraction)
            {
                ExitApplication();
                return;
            }
            RetryPendingSave();
            RefreshDesktopHostHandles(false);
            foreach (var form in _forms.Values.ToList()) form.RecoverMissedMouseUp();
            if (!DesktopReady || _isMutatingFence || _isEnforcing || _isAnimatingIcons || _isDrawingRegion || _forms.Count == 0)
                return;

            bool shouldBeVisible = !_fencesHiddenByUser && _controller.IsDesktopVisible();
            if (shouldBeVisible != _lastDesktopVisible)
                SyncDesktopVisibility();

            // Do not interfere while left mouse button is held down (user is actively dragging icons or a fence)
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0)
                return;

            long nowMs = _sessionClock.ElapsedMilliseconds;
            int curCount = _controller.GetDesktopItemCount();
            if (curCount != _lastItemCount)
            {
                _lastItemCount = curCount;
                _desktopDirty = true;
                _controller.InvalidateIdentityCache();
            }

            bool periodicRefresh = (nowMs - _lastFullScanMs) >= 3000;
            bool activeTracking = _desktopMenu.FenceId != null
                || _recentContextPlacements.Count > 0
                || (!string.IsNullOrEmpty(_externalDropFenceId) && _externalDropMs >= 0 && (nowMs - _externalDropMs) <= 2000)
                || (!string.IsNullOrEmpty(_recentRightClickFenceId) && _recentRightClickMs >= 0 && (nowMs - _recentRightClickMs) <= 12000);
            if (!_desktopDirty && !activeTracking && !periodicRefresh)
                return;

            if (periodicRefresh) _controller.InvalidateIdentityCache();
            EnforceAllRules(true, true);
        }

        /// <summary>
        /// Core rule engine guaranteeing ALL user invariants:
        /// 1. 4-edge border ring of every fence is strictly forbidden for icons -> pushed outside all fences.
        /// 2. Only manual user drag-in/drag-out (1 or multiple icons) can change folder membership.
        /// 3. Moving/resizing/creating a fence over existing icons squeezes them outside instead of adopting them.
        /// 4. Brand-new desktop icons spawning inside any fence (border or interior) are illegal -> pushed outside.
        /// </summary>
        private void EnforceAllRules(bool allowManualMembershipChange, bool animateMoves = true, bool gridDrop = false)
        {
            if (!DesktopReady || _isEnforcing || Config.Fences.Count == 0) return;
            FinishIconAnimationImmediately();
            _isEnforcing = true;
            try
            {
                var icons = _controller.GetAllIcons();
                if (!_controller.SnapshotComplete) return;
                _desktopDirty = false;
                _lastFullScanMs = _sessionClock.ElapsedMilliseconds;
                _lastItemCount = icons.Count;
                _inputSnapshot = icons;
                var m = _controller.GetGridMetrics(icons);
                _lastGridMetrics = m;
                _lastRawItemSpacing = _controller.GetRawItemSpacing();
                bool configDirty = _membership.Reconcile(Config.Fences, icons, m, _sessionClock.ElapsedMilliseconds);
                if (configDirty) SaveConfig();
                if (icons.Count == 0)
                {
                    _knownIconCells.Clear();
                    return;
                }
                if (m.AutoArrangeEnabled) return;
                configDirty = false;

                // Each Shell item can belong to one fence.
                var ownerMap = new Dictionary<string, FenceConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (var fc in Config.Fences)
                {
                    foreach (string name in fc.MemberIconIds)
                        ownerMap[name] = fc;
                }

                long nowMs = _sessionClock.ElapsedMilliseconds;

                // Purge expired recent context placements (> 650ms)
                if (_recentContextPlacements.Count > 0)
                {
                    var expired = new List<string>();
                    foreach (var kv in _recentContextPlacements)
                    {
                        if (nowMs - kv.Value.PlacedAtMs > 650)
                            expired.Add(kv.Key);
                    }
                    foreach (string k in expired)
                        _recentContextPlacements.Remove(k);
                }

                var dragged = allowManualMembershipChange ? _desktopDrag.MovedItems(icons, m, nowMs) :
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var contextFence = allowManualMembershipChange && _desktopMenu.Command != DesktopMenuCommand.None ?
                    Config.Fences.FirstOrDefault(f => f.Id == _desktopMenu.FenceId) : null;
                FenceConfig externalDropFence = null;
                if (allowManualMembershipChange && !string.IsNullOrEmpty(_externalDropFenceId))
                {
                    if (_externalDropMs >= 0 && (nowMs - _externalDropMs) <= 1800)
                        externalDropFence = Config.Fences.FirstOrDefault(f => string.Equals(f.Id, _externalDropFenceId, StringComparison.Ordinal));
                    else
                        ClearExternalDrop();
                }
                Point contextClick = contextFence != null ? _desktopMenu.Click : (externalDropFence != null ? _externalDropPt : _recentRightClickPt);
                FenceConfig fallbackContextFence = null;
                bool fgExplorer = false;
                if (allowManualMembershipChange && !string.IsNullOrEmpty(_recentRightClickFenceId))
                {
                    long elapsedClick = _recentRightClickMs >= 0 ? (nowMs - _recentRightClickMs) : -1;
                    long elapsedConsumed = _recentRightClickConsumedMs >= 0 ? (nowMs - _recentRightClickConsumedMs) : -1;
                    if (elapsedClick >= 0 && elapsedClick <= 12000 && (elapsedConsumed < 0 || elapsedConsumed <= 2000))
                    {
                        fgExplorer = IsDesktopOrExplorerForeground();
                        if (fgExplorer)
                            fallbackContextFence = Config.Fences.FirstOrDefault(f => string.Equals(f.Id, _recentRightClickFenceId, StringComparison.Ordinal));
                    }
                    else
                    {
                        ClearRightClickFallback();
                    }
                }
                int editingIndex = -1;
                if ((contextFence != null || fallbackContextFence != null) &&
                    _controller.GetActiveEditControl() != IntPtr.Zero)
                    editingIndex = (int)NativeMethods.SendMessage(_controller.ListView, NativeMethods.LVM_GETNEXTITEM,
                        new IntPtr(-1), new IntPtr(NativeMethods.LVNI_FOCUSED));
                var toExpel = new List<DesktopIconInfo>();
                var toKeepInside = new List<KeyValuePair<DesktopIconInfo, FenceConfig>>();
                var toPlaceInContextZone = new List<KeyValuePair<DesktopIconInfo, FenceConfig>>();
                var toRestoreRecentContext = new List<KeyValuePair<DesktopIconInfo, RecentContextPlacement>>();

                foreach (var ic in icons)
                {
                    int col = ic.GridCol(m);
                    int row = ic.GridRow(m);

                    bool wasKnown = _knownIconCells.ContainsKey(ic.Identity);
                    bool userMoved = wasKnown && (_knownIconCells[ic.Identity].X != col || _knownIconCells[ic.Identity].Y != row);

                    FenceConfig hitOuter = Config.Fences.FirstOrDefault(f => f.IsInOuterRect(col, row));
                    FenceConfig hitInner = Config.Fences.FirstOrDefault(f => f.IsInInnerRect(col, row));
                    FenceConfig hitBorder = (hitOuter != null && hitInner == null) ? hitOuter : null;

                    FenceConfig currentOwner;
                    ownerMap.TryGetValue(ic.Identity, out currentOwner);

                    bool manualDrop = wasKnown && dragged.Contains(ic.Identity);
                    if (allowManualMembershipChange && !manualDrop && _desktopDrag.WaitingForItem(ic.Identity))
                        continue;
                    if (currentOwner == null && contextFence != null && _desktopMenu.Take(ic, ic.Index == editingIndex, nowMs))
                    {
                        if (hitInner == contextFence)
                        {
                            contextFence.MemberIconIds.Add(ic.Identity);
                            ownerMap[ic.Identity] = contextFence;
                            configDirty = true;
                        }
                        else
                            toPlaceInContextZone.Add(new KeyValuePair<DesktopIconInfo, FenceConfig>(ic, contextFence));
                        if (_recentRightClickConsumedMs < 0) _recentRightClickConsumedMs = nowMs;
                        continue;
                    }
                    if (currentOwner == null && !wasKnown && externalDropFence != null &&
                        (hitOuter == externalDropFence || ic.Selected))
                    {
                        if (hitInner == externalDropFence)
                        {
                            if (!externalDropFence.MemberIconIds.Any(n => string.Equals(n, ic.Identity, StringComparison.OrdinalIgnoreCase)))
                                externalDropFence.MemberIconIds.Add(ic.Identity);
                            ownerMap[ic.Identity] = externalDropFence;
                            configDirty = true;
                        }
                        else
                        {
                            toPlaceInContextZone.Add(new KeyValuePair<DesktopIconInfo, FenceConfig>(ic, externalDropFence));
                        }
                        continue;
                    }
                    if (currentOwner == null && !wasKnown && fallbackContextFence != null)
                    {
                        long elapsedClick = _recentRightClickMs >= 0 ? (nowMs - _recentRightClickMs) : -1;
                        long elapsedConsumed = _recentRightClickConsumedMs >= 0 ? (nowMs - _recentRightClickConsumedMs) : -1;
                        if (ShouldAdoptNewIconInZone(fallbackContextFence.Id, _recentRightClickFenceId, elapsedClick, elapsedConsumed, fgExplorer))
                        {
                            if (_recentRightClickConsumedMs < 0) _recentRightClickConsumedMs = nowMs;
                            if (hitInner == fallbackContextFence)
                            {
                                if (!fallbackContextFence.MemberIconIds.Any(n => string.Equals(n, ic.Identity, StringComparison.OrdinalIgnoreCase)))
                                    fallbackContextFence.MemberIconIds.Add(ic.Identity);
                                ownerMap[ic.Identity] = fallbackContextFence;
                                configDirty = true;
                            }
                            else
                            {
                                toPlaceInContextZone.Add(new KeyValuePair<DesktopIconInfo, FenceConfig>(ic, fallbackContextFence));
                            }
                            continue;
                        }
                    }
                    if (currentOwner == null && contextFence != null && _desktopMenu.AwaitingInlineEdit(ic))
                        continue;

                    RecentContextPlacement recent;
                    if (!manualDrop && userMoved && _recentContextPlacements.TryGetValue(ic.Identity, out recent) &&
                        (col != recent.TargetCell.X || row != recent.TargetCell.Y))
                    {
                        toRestoreRecentContext.Add(new KeyValuePair<DesktopIconInfo, RecentContextPlacement>(ic, recent));
                        continue;
                    }

                    if (manualDrop)
                    {
                        _recentContextPlacements.Remove(ic.Identity);

                        // User manually dragged this icon (works for single or multi-icon drags!)
                        if (hitBorder != null)
                        {
                            // Dropped onto one of the 4 border edges -> expel outside & remove membership
                            if (currentOwner != null)
                            {
                                currentOwner.MemberIconIds.RemoveAll(n => string.Equals(n, ic.Identity, StringComparison.OrdinalIgnoreCase));
                                ownerMap.Remove(ic.Identity);
                                configDirty = true;
                            }
                            toExpel.Add(ic);
                        }
                        else if (hitInner != null)
                        {
                            // Legal manual drag-in to hitInner!
                            if (currentOwner != hitInner)
                            {
                                if (currentOwner != null)
                                    currentOwner.MemberIconIds.RemoveAll(n => string.Equals(n, ic.Identity, StringComparison.OrdinalIgnoreCase));
                                if (!hitInner.MemberIconIds.Any(n => string.Equals(n, ic.Identity, StringComparison.OrdinalIgnoreCase)))
                                    hitInner.MemberIconIds.Add(ic.Identity);
                                ownerMap[ic.Identity] = hitInner;
                                configDirty = true;
                            }
                        }
                        else
                        {
                            // Legal manual drag-out outside all fences!
                            if (currentOwner != null)
                            {
                                currentOwner.MemberIconIds.RemoveAll(n => string.Equals(n, ic.Identity, StringComparison.OrdinalIgnoreCase));
                                ownerMap.Remove(ic.Identity);
                                configDirty = true;
                            }
                        }
                    }
                    else
                    {
                        // Fence moved/resized or icon stationary:
                        // 1. Existing member icons MUST stay inside their owner fence (never forced out by resizing!).
                        // 2. Non-member icons overlapped by a fence MUST be expelled outside (never adopted!).
                        if (currentOwner != null)
                        {
                            if (!currentOwner.IsInInnerRect(col, row))
                            {
                                toKeepInside.Add(new KeyValuePair<DesktopIconInfo, FenceConfig>(ic, currentOwner));
                            }
                        }
                        else if (hitOuter != null)
                        {
                            toExpel.Add(ic);
                        }
                    }
                }

                // Resolve placements
                var finalCells = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
                foreach (var ic in icons)
                    finalCells[ic.Identity] = new Point(ic.GridCol(m), ic.GridRow(m));

                if (toKeepInside.Count > 0 || toPlaceInContextZone.Count > 0 || toRestoreRecentContext.Count > 0 || toExpel.Count > 0)
                {
                    var occupied = new HashSet<long>();
                    Func<int, int, long> keyOf = (c, r) => (((long)c) << 32) | (uint)r;
                    foreach (var kv in finalCells)
                        occupied.Add(keyOf(kv.Value.X, kv.Value.Y));

                    Rectangle[] workAreas = MonitorGrid.GetAreas(m);

                    var immediateTracks = new List<IconSlideTrack>();
                    var animTracks = new List<IconSlideTrack>();

                    // 0a. Re-assert any newly created context icon if Explorer's delayed post-insert layout tried to move it back
                    foreach (var pair in toRestoreRecentContext)
                    {
                        DesktopIconInfo ic = pair.Key;
                        RecentContextPlacement rec = pair.Value;
                        int curCol = ic.GridCol(m);
                        int curRow = ic.GridRow(m);
                        occupied.Remove(keyOf(curCol, curRow));
                        occupied.Add(keyOf(rec.TargetCell.X, rec.TargetCell.Y));
                        finalCells[ic.Identity] = rec.TargetCell;
                        int targetClientX = ic.ClientPosition.X + (rec.TargetCell.X - curCol) * m.CellWidth;
                        int targetClientY = ic.ClientPosition.Y + (rec.TargetCell.Y - curRow) * m.CellHeight;
                        immediateTracks.Add(new IconSlideTrack
                        {
                            IconInfo = ic,
                            StartPos = ic.ClientPosition,
                            TargetPos = new Point(targetClientX, targetClientY),
                            LastSentPos = ic.ClientPosition
                        });
                    }

                    // 0b. Right-click newly created icons inside a zone's visual box margin (or full zone):
                    //     ALWAYS move synchronously in 1 instant sub-frame step (never 220ms slide / never toggle SnapToGrid!)
                    //     so it lands directly at the target cell BEFORE or WITH Explorer's inline rename Edit box.
                    foreach (var pair in toPlaceInContextZone)
                    {
                        DesktopIconInfo ic = pair.Key;
                        FenceConfig targetFence = pair.Value;
                        int curCol = ic.GridCol(m);
                        int curRow = ic.GridRow(m);
                        occupied.Remove(keyOf(curCol, curRow));

                        Point destCell;
                        bool placedInside = FindNearestFreeInnerCell(targetFence, contextClick, m, occupied, out destCell);
                        if (placedInside)
                        {
                            if (!targetFence.MemberIconIds.Any(n => string.Equals(n, ic.Identity, StringComparison.OrdinalIgnoreCase)))
                                targetFence.MemberIconIds.Add(ic.Identity);
                            ownerMap[ic.Identity] = targetFence;
                            configDirty = true;
                        }
                        else if (!FindNearestFreeOutsideCell(Config.Fences, contextClick, workAreas, m, occupied, out destCell))
                        {
                            destCell = new Point(curCol, curRow);
                        }

                        occupied.Add(keyOf(destCell.X, destCell.Y));
                        finalCells[ic.Identity] = destCell;
                        _recentContextPlacements[ic.Identity] = new RecentContextPlacement
                        {
                            TargetFenceId = placedInside ? targetFence.Id : null,
                            TargetCell = destCell,
                            PlacedAtMs = nowMs
                        };

                        if (destCell.X != curCol || destCell.Y != curRow)
                        {
                            int targetClientX = ic.ClientPosition.X + (destCell.X - curCol) * m.CellWidth;
                            int targetClientY = ic.ClientPosition.Y + (destCell.Y - curRow) * m.CellHeight;
                            immediateTracks.Add(new IconSlideTrack
                            {
                                IconInfo = ic,
                                StartPos = ic.ClientPosition,
                                TargetPos = new Point(targetClientX, targetClientY),
                                LastSentPos = ic.ClientPosition
                            });
                        }
                    }

                    // 1. Pull displaced member icons back into the nearest free inner slot of their owner fence
                    foreach (var pair in toKeepInside)
                    {
                        DesktopIconInfo ic = pair.Key;
                        FenceConfig fc = pair.Value;
                        int curCol = ic.GridCol(m);
                        int curRow = ic.GridRow(m);
                        occupied.Remove(keyOf(curCol, curRow));

                        Point refPt = new Point(
                            ic.ScreenBounds.Left + ic.ScreenBounds.Width / 2,
                            ic.ScreenBounds.Top + ic.ScreenBounds.Height / 2);
                        Point bestCell;
                        bool foundInner = FindNearestFreeInnerCell(fc, refPt, m, occupied, out bestCell);
                        if (!foundInner) bestCell = new Point(curCol, curRow);

                        occupied.Add(keyOf(bestCell.X, bestCell.Y));
                        finalCells[ic.Identity] = bestCell;
                        if (foundInner && (bestCell.X != curCol || bestCell.Y != curRow))
                        {
                            int targetClientX = ic.ClientPosition.X + (bestCell.X - curCol) * m.CellWidth;
                            int targetClientY = ic.ClientPosition.Y + (bestCell.Y - curRow) * m.CellHeight;
                            animTracks.Add(new IconSlideTrack
                            {
                                IconInfo = ic,
                                StartPos = ic.ClientPosition,
                                TargetPos = new Point(targetClientX, targetClientY),
                                LastSentPos = ic.ClientPosition
                            });
                        }
                    }

                    // 2. Expel all illegal / overlapped non-member icons outside ALL fences
                    // Toggling Explorer's snap style repaints the whole ListView. A snapped
                    // fence drop can put its displaced outsiders straight onto the target cells.
                    var expelledTracks = gridDrop && m.SnapToGridEnabled ? immediateTracks : animTracks;
                    foreach (var ic in toExpel)
                    {
                        int curCol = ic.GridCol(m);
                        int curRow = ic.GridRow(m);
                        occupied.Remove(keyOf(curCol, curRow));

                        Point refPt = new Point(
                            ic.ScreenBounds.Left + ic.ScreenBounds.Width / 2,
                            ic.ScreenBounds.Top + ic.ScreenBounds.Height / 2);
                        Point bestCell;
                        bool found = FindNearestFreeOutsideCell(Config.Fences, refPt, workAreas, m, occupied, out bestCell);
                        if (!found) bestCell = new Point(curCol, curRow);

                        occupied.Add(keyOf(bestCell.X, bestCell.Y));
                        finalCells[ic.Identity] = bestCell;
                        if (found && (bestCell.X != curCol || bestCell.Y != curRow))
                        {
                            int targetClientX = ic.ClientPosition.X + (bestCell.X - curCol) * m.CellWidth;
                            int targetClientY = ic.ClientPosition.Y + (bestCell.Y - curRow) * m.CellHeight;
                            expelledTracks.Add(new IconSlideTrack
                            {
                                IconInfo = ic,
                                StartPos = ic.ClientPosition,
                                TargetPos = new Point(targetClientX, targetClientY),
                                LastSentPos = ic.ClientPosition
                            });
                        }
                    }

                    // 3. Synchronize any icon currently in Explorer's inline-rename Edit box so icon & rename box
                    //    move together in lockstep (and never toggle LVS_EX_SNAPTOGRID while renaming!).
                    IntPtr editHwnd = (immediateTracks.Count > 0 || animTracks.Count > 0)
                        ? _controller.GetActiveEditControl()
                        : IntPtr.Zero;
                    Rectangle editScreenRect = Rectangle.Empty;
                    bool hasEditRect = false;
                    int focusedEditIndex = -1;
                    if (editHwnd != IntPtr.Zero)
                    {
                        focusedEditIndex = (int)NativeMethods.SendMessage(_controller.ListView, NativeMethods.LVM_GETNEXTITEM,
                            new IntPtr(-1), new IntPtr(NativeMethods.LVNI_FOCUSED));
                        NativeMethods.RECT editRc;
                        if (NativeMethods.GetWindowRect(editHwnd, out editRc))
                        {
                            editScreenRect = Rectangle.FromLTRB(editRc.Left, editRc.Top, editRc.Right, editRc.Bottom);
                            hasEditRect = true;
                        }
                    }

                    if (hasEditRect && animTracks.Count > 0)
                    {
                        var editedAnim = animTracks.FirstOrDefault(tr => tr.IconInfo.Index == focusedEditIndex);

                        if (editedAnim != null)
                        {
                            animTracks.Remove(editedAnim);
                            immediateTracks.Add(editedAnim);
                        }
                    }

                    if (immediateTracks.Count > 0)
                    {
                        var immediateMoves = new List<KeyValuePair<string, Point>>(immediateTracks.Count);
                        foreach (var tr in immediateTracks)
                            immediateMoves.Add(new KeyValuePair<string, Point>(tr.IconInfo.Identity, tr.TargetPos));
                        _controller.MoveIconsBatch(immediateMoves);

                        if (hasEditRect)
                        {
                            var matchedEdit = immediateTracks.FirstOrDefault(tr => tr.IconInfo.Index == focusedEditIndex);

                            if (matchedEdit != null)
                            {
                                _controller.MoveActiveEditControlWithIcon(
                                    editHwnd,
                                    editScreenRect,
                                    matchedEdit.IconInfo.LabelBounds,
                                    matchedEdit.IconInfo.Index,
                                    new Point(matchedEdit.TargetPos.X - matchedEdit.StartPos.X, matchedEdit.TargetPos.Y - matchedEdit.StartPos.Y));
                            }
                        }
                    }

                    if (animTracks.Count > 0)
                    {
                        if (animateMoves)
                        {
                            StartIconSlideAnimation(animTracks, m.SnapToGridEnabled);
                        }
                        else
                        {
                            var moves = new List<KeyValuePair<string, Point>>(animTracks.Count);
                            foreach (var tr in animTracks)
                                moves.Add(new KeyValuePair<string, Point>(tr.IconInfo.Identity, tr.TargetPos));
                            _controller.MoveIconsBatch(moves);
                        }
                    }
                }
                else if (_recentContextPlacements.Count > 0)
                {
                    // Edge case: icon was already moved BEFORE Explorer opened its Edit box, and Explorer opened
                    // the Edit box slightly misaligned -> snap Edit box directly onto the icon's LabelBounds!
                    IntPtr editHwnd = _controller.GetActiveEditControl();
                    if (editHwnd != IntPtr.Zero)
                    {
                        int focusedEditIndex = (int)NativeMethods.SendMessage(_controller.ListView, NativeMethods.LVM_GETNEXTITEM,
                            new IntPtr(-1), new IntPtr(NativeMethods.LVNI_FOCUSED));
                        NativeMethods.RECT editRc;
                        if (NativeMethods.GetWindowRect(editHwnd, out editRc))
                        {
                            Rectangle editScreenRect = Rectangle.FromLTRB(editRc.Left, editRc.Top, editRc.Right, editRc.Bottom);
                            foreach (var ic in icons)
                            {
                                if (ic.Index != focusedEditIndex || !_recentContextPlacements.ContainsKey(ic.Identity)) continue;
                                int dx = ic.LabelBounds.Left - editScreenRect.Left;
                                int dy = ic.LabelBounds.Top - editScreenRect.Top;
                                if (Math.Abs(dx) > m.CellWidth / 2 || Math.Abs(dy) > m.CellHeight / 2)
                                {
                                    var targetClient = new NativeMethods.POINT { X = ic.LabelBounds.Left, Y = ic.LabelBounds.Top };
                                    if (NativeMethods.ScreenToClient(_controller.ListView, ref targetClient))
                                    {
                                        NativeMethods.SetWindowPos(
                                            editHwnd,
                                            IntPtr.Zero,
                                            targetClient.X,
                                            targetClient.Y,
                                            0, 0,
                                            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                                        NativeMethods.UpdateWindow(editHwnd);
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }

                // Update known icon positions snapshot
                _knownIconCells.Clear();
                foreach (var kv in finalCells)
                    _knownIconCells[kv.Key] = kv.Value;

                if (configDirty) SaveConfig();
            }
            finally
            {
                _isEnforcing = false;
            }
        }

        public static bool ZonesOverlap(
            int colA, int rowA, int colsA, int rowsA,
            int colB, int rowB, int colsB, int rowsB,
            DesktopGridMetrics metrics, float dpiScale)
        {
            if (FenceConfig.GridRectsOverlap(colA, rowA, colsA, rowsA, colB, rowB, colsB, rowsB))
                return true;

            if (metrics.CellWidth > 0 && metrics.CellHeight > 0)
            {
                int hhA, hhB;
                Rectangle rectA = FenceChromeForm.ComputeGridBounds(colA, rowA, colsA, rowsA, metrics, dpiScale, out hhA);
                Rectangle rectB = FenceChromeForm.ComputeGridBounds(colB, rowB, colsB, rowsB, metrics, dpiScale, out hhB);
                int minGap = Math.Max(6, (int)Math.Round(6 * Math.Max(1f, dpiScale)));
                if (Rectangle.Inflate(rectA, minGap, minGap).IntersectsWith(rectB))
                    return true;
            }
            return false;
        }

        public bool WouldOverlapAnyOtherFence(
            string excludeFenceId,
            int col, int row, int cols, int rows,
            DesktopGridMetrics metrics, float dpiScale)
        {
            foreach (var other in Config.Fences)
            {
                if (!string.IsNullOrEmpty(excludeFenceId) && string.Equals(other.Id, excludeFenceId, StringComparison.Ordinal))
                    continue;
                if (ZonesOverlap(col, row, cols, rows, other.OuterCol, other.OuterRow, other.OuterCols, other.OuterRows, metrics, dpiScale))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds the nearest non-overlapping grid origin (OuterCol, OuterRow) for a zone of size (cols, rows)
        /// within the desktop working area. Prefers minimum pixel Euclidean distance to (desiredCol, desiredRow),
        /// with a tie-breaker favoring the direction toward (fallbackCol, fallbackRow).
        /// </summary>
        public static Point FindNearestNonOverlappingOrigin(
            IEnumerable<FenceConfig> existingFences,
            string excludeFenceId,
            int desiredCol, int desiredRow,
            int cols, int rows,
            int fallbackCol, int fallbackRow,
            int maxCols, int maxRows,
            DesktopGridMetrics metrics, float dpiScale,
            out bool foundValid)
        {
            return FindNearestNonOverlappingOrigin(existingFences, excludeFenceId, desiredCol, desiredRow, cols, rows,
                fallbackCol, fallbackRow, new[] { new Rectangle(0, 0, maxCols, maxRows) }, metrics, dpiScale, out foundValid);
        }

        internal static Point FindNearestNonOverlappingOrigin(
            IEnumerable<FenceConfig> existingFences, string excludeFenceId, int desiredCol, int desiredRow,
            int cols, int rows, int fallbackCol, int fallbackRow, Rectangle[] areas,
            DesktopGridMetrics metrics, float dpiScale, out bool foundValid)
        {
            var size = new Size(cols, rows);
            var desired = new Point(desiredCol, desiredRow);
            var fallback = new Point(fallbackCol, fallbackRow);
            Rectangle nearest = MonitorGrid.BestArea(areas, desired, size, metrics);
            foundValid = false;
            if (nearest.Width < cols || nearest.Height < rows || nearest.IsEmpty) return fallback;

            var others = new List<FenceConfig>();
            if (existingFences != null)
            {
                foreach (var f in existingFences)
                {
                    if (!string.IsNullOrEmpty(excludeFenceId) && string.Equals(f.Id, excludeFenceId, StringComparison.Ordinal))
                        continue;
                    others.Add(f);
                }
            }

            Func<int, int, bool> isFree = (c, r) =>
            {
                foreach (var o in others)
                {
                    if (ZonesOverlap(c, r, cols, rows, o.OuterCol, o.OuterRow, o.OuterCols, o.OuterRows, metrics, dpiScale))
                        return false;
                }
                return true;
            };

            if (nearest.Contains(new Rectangle(desired, size)) && isFree(desiredCol, desiredRow))
            {
                foundValid = true;
                return desired;
            }

            double cw = metrics.CellWidth > 0 ? metrics.CellWidth : 100.0;
            double ch = metrics.CellHeight > 0 ? metrics.CellHeight : 100.0;

            bool found = false;
            Point best = MonitorGrid.ClampOrigin(MonitorGrid.BestArea(areas, fallback, size, metrics), fallback, size);
            double bestDistSq = double.MaxValue;
            double bestFallbackDistSq = double.MaxValue;

            foreach (Rectangle area in areas)
            {
                for (int r = area.Top; r <= area.Bottom - rows; r++)
                {
                    double dy = (r - (double)desiredRow) * ch;
                    double dySq = dy * dy;
                    if (dySq > bestDistSq + 1.0) continue;

                    for (int c = area.Left; c <= area.Right - cols; c++)
                    {
                        double dx = (c - (double)desiredCol) * cw;
                        double distSq = dx * dx + dySq;
                        if (distSq > bestDistSq + 1.0) continue;

                        if (!isFree(c, r)) continue;

                        double fdx = (c - (double)fallbackCol) * cw;
                        double fdy = (r - (double)fallbackRow) * ch;
                        double fbDistSq = fdx * fdx + fdy * fdy;

                        bool better = !found
                            || (distSq < bestDistSq - 1.0)
                            || (Math.Abs(distSq - bestDistSq) <= 1.0 && fbDistSq < bestFallbackDistSq - 1.0);

                        if (better)
                        {
                            found = true;
                            best = new Point(c, r);
                            bestDistSq = distSq;
                            bestFallbackDistSq = fbDistSq;
                        }
                    }
                }
            }

            foundValid = found;
            return best;
        }

        public Point FindNearestNonOverlappingOrigin(
            FenceConfig movingFence,
            int desiredCol, int desiredRow,
            int fallbackCol, int fallbackRow,
            DesktopGridMetrics metrics, float dpiScale,
            out bool foundValid)
        {
            return FindNearestNonOverlappingOrigin(
                Config.Fences,
                movingFence != null ? movingFence.Id : null,
                desiredCol, desiredRow,
                movingFence.OuterCols, movingFence.OuterRows,
                fallbackCol, fallbackRow,
                MonitorGrid.GetAreas(metrics),
                metrics, dpiScale,
                out foundValid);
        }

        private void ResolveExistingOverlaps(DesktopGridMetrics metrics)
        {
            if (Config == null || Config.Fences.Count == 0) return;
            float dpi = Config.Fences.Count > 0
                ? MonitorGrid.GetDpiScaleForCell(Config.Fences[0].OuterCol, Config.Fences[0].OuterRow, metrics)
                : NativeMethods.GetWindowDpiScale(IntPtr.Zero);
            if (FitExistingFences(Config.Fences, MonitorGrid.GetAreas(metrics), metrics, dpi))
                SaveConfig();
        }

        internal static bool FitExistingFences(IEnumerable<FenceConfig> fences, Rectangle[] areas, DesktopGridMetrics metrics, float dpi)
        {
            var placed = new List<FenceConfig>();
            var displaced = new List<FenceConfig>();
            foreach (var fc in fences)
            {
                var bounds = new Rectangle(fc.OuterCol, fc.OuterRow, fc.OuterCols, fc.OuterRows);
                if (areas.Any(a => a.Contains(bounds)) && !placed.Any(p => ZonesOverlap(
                    fc.OuterCol, fc.OuterRow, fc.OuterCols, fc.OuterRows, p.OuterCol, p.OuterRow, p.OuterCols, p.OuterRows, metrics, dpi)))
                    placed.Add(fc);
                else
                    displaced.Add(fc);
            }

            bool changed = false;
            foreach (var fc in displaced)
            {
                var origin = new Point(fc.OuterCol, fc.OuterRow);
                var size = new Size(fc.OuterCols, fc.OuterRows);
                int members = fc.MemberIconNames.Count + fc.MemberIconIds.Count;
                Rectangle[] possible = areas.Where(a => a.Width >= 3 && a.Height >= 3 &&
                    (long)(a.Width - 2) * (a.Height - 2) >= members).ToArray();
                Rectangle area = MonitorGrid.BestArea(possible.Length > 0 ? possible : areas, origin, size, metrics);
                if (area.IsEmpty) continue;
                size.Width = Math.Min(size.Width, area.Width);
                size.Height = Math.Min(size.Height, area.Height);
                if ((long)(size.Width - 2) * (size.Height - 2) < members && possible.Length > 0)
                {
                    // A portrait/landscape change can require more rows or columns for the same members.
                    size.Height = Math.Min(area.Height, 2 + (int)Math.Ceiling((double)members / (size.Width - 2)));
                    size.Width = Math.Max(size.Width, 2 + (int)Math.Ceiling((double)members / (size.Height - 2)));
                }
                Rectangle fitted = FindFittingFence(placed, areas, origin, size, metrics, dpi, members);
                if (fitted.IsEmpty)
                    fitted = new Rectangle(MonitorGrid.ClampOrigin(area, origin, size), size);
                if (fitted.X != fc.OuterCol || fitted.Y != fc.OuterRow || fitted.Width != fc.OuterCols || fitted.Height != fc.OuterRows)
                {
                    fc.OuterCol = fitted.X;
                    fc.OuterRow = fitted.Y;
                    fc.OuterCols = fitted.Width;
                    fc.OuterRows = fitted.Height;
                    changed = true;
                }
                placed.Add(fc);
            }
            return changed;
        }

        private static Rectangle FindFittingFence(IEnumerable<FenceConfig> fences, Rectangle[] areas, Point desired,
            Size size, DesktopGridMetrics metrics, float dpi, int memberCount)
        {
            if (size.Width < 3 || size.Height < 3) return Rectangle.Empty;
            bool found;
            Point origin = FindNearestNonOverlappingOrigin(fences, null, desired.X, desired.Y, size.Width, size.Height,
                desired.X, desired.Y, areas, metrics, dpi, out found);
            if (found) return new Rectangle(origin, size);

            // For each width, find its tallest free rectangle. Reducing height at a fixed
            // origin cannot cause an overlap. Keep the option retaining the most slots.
            Rectangle best = Rectangle.Empty;
            int bestCapacity = -1;
            for (int width = size.Width; width >= 3; width--)
            {
                int requiredSlots = Math.Max(memberCount, bestCapacity + 1);
                int low = Math.Max(3, 2 + (int)Math.Ceiling((double)requiredSlots / (width - 2)));
                int high = size.Height;
                if (low > high) break;
                while (low <= high)
                {
                    int height = low + (high - low) / 2;
                    origin = FindNearestNonOverlappingOrigin(fences, null, desired.X, desired.Y, width, height,
                        desired.X, desired.Y, areas, metrics, dpi, out found);
                    if (found)
                    {
                        best = new Rectangle(origin.X, origin.Y, width, height);
                        bestCapacity = (width - 2) * (height - 2);
                        low = height + 1;
                    }
                    else high = height - 1;
                }
            }
            return best;
        }

        public void CreateNewFence(int? explicitCol = null, int? explicitRow = null, int? explicitCols = null, int? explicitRows = null)
        {
            if (!PrepareFenceInteraction()) return;
            var m = _controller.GetGridMetrics();
            Rectangle preferred = MonitorGrid.FromWorkArea(Screen.FromPoint(Cursor.Position).WorkingArea, m);
            CreateNewFence(m, MonitorGrid.GetAreas(m), preferred, explicitCol, explicitRow, explicitCols, explicitRows);
        }

        private void CreateNewFence(DesktopGridMetrics m, Rectangle[] areas, Rectangle preferred,
            int? explicitCol, int? explicitRow, int? explicitCols, int? explicitRows)
        {
            int idx = Config.Fences.Count;
            var size = new Size(Math.Max(3, explicitCols ?? 5), Math.Max(3, explicitRows ?? 4));
            var desired = new Point(explicitCol ?? (preferred.Left + (idx * 2 + 1) % Math.Max(1, preferred.Width - size.Width + 1)),
                                    explicitRow ?? (preferred.Top + (idx * 2) % Math.Max(1, preferred.Height - size.Height + 1)));
            float dpi = MonitorGrid.GetDpiScaleForCell(desired.X, desired.Y, m);
            Rectangle area = MonitorGrid.BestArea(areas, desired, size, m);
            size.Width = Math.Min(area.Width, size.Width);
            size.Height = Math.Min(area.Height, size.Height);
            Rectangle fitted = FindFittingFence(Config.Fences, areas, desired, size, m, dpi, 0);
            if (fitted.IsEmpty)
            {
                _trayIcon.ShowBalloonTip(
                    3500,
                    "桌面空间不足",
                    "当前桌面已无不重叠的空闲位置，请先调整或删除现有分区。",
                    ToolTipIcon.Info);
                return;
            }

            var fc = new FenceConfig
            {
                Title = "分区 " + (idx + 1),
                OuterCol = fitted.X,
                OuterRow = fitted.Y,
                OuterCols = fitted.Width,
                OuterRows = fitted.Height,
                ThemeId = "liquid_clear",
                BackdropOpacity = 1.0,
                HeaderOpacity = 1.0
            };
            Config.Fences.Add(fc);
            SaveConfig();

            var form = new FenceChromeForm(this, fc);
            _forms[fc.Id] = form;
            if (_fencesHiddenByUser)
            {
                _fencesHiddenByUser = false;
                SyncDesktopVisibility();
                UpdateTrayStates();
            }
            form.Show();
            form.SyncBoundsFromGrid(m);
            form.SendToDesktopBottom();

            // Smoothly glide any icons under the newly created zone outside (strict ownership: only manual drag-in!)
            EnforceAllRules(false, true);
            form.BeginInlineRename();
        }

        public void SortFenceIcons(string fenceId, FenceSortMode mode)
        {
            if (!PrepareFenceInteraction()) return;
            var fence = Config.Fences.FirstOrDefault(f => string.Equals(f.Id, fenceId, StringComparison.Ordinal));
            if (fence == null || fence.MemberIconIds.Count <= 1) return;

            var icons = _controller.GetAllIcons(true);
            if (!_controller.SnapshotComplete) return;
            var m = _controller.GetGridMetrics(icons);
            if (m.AutoArrangeEnabled) return;

            var memberSet = new HashSet<string>(fence.MemberIconIds, StringComparer.OrdinalIgnoreCase);
            var members = icons.Where(ic => memberSet.Contains(ic.Identity)).ToList();
            if (members.Count <= 1) return;

            Func<DesktopIconInfo, bool> isDir = ic =>
            {
                try { return !string.IsNullOrEmpty(ic.ParsingName) && Directory.Exists(ic.ParsingName); }
                catch { return false; }
            };
            Func<DesktopIconInfo, DateTime> modTime = ic =>
            {
                try
                {
                    if (string.IsNullOrEmpty(ic.ParsingName)) return DateTime.MinValue;
                    if (Directory.Exists(ic.ParsingName)) return Directory.GetLastWriteTimeUtc(ic.ParsingName);
                    if (File.Exists(ic.ParsingName)) return File.GetLastWriteTimeUtc(ic.ParsingName);
                }
                catch { }
                return DateTime.MinValue;
            };
            Func<DesktopIconInfo, string> extOf = ic =>
            {
                if (isDir(ic)) return "";
                try { return Path.GetExtension(ic.ParsingName ?? "") ?? ""; }
                catch { return ""; }
            };

            List<DesktopIconInfo> sorted;
            switch (mode)
            {
                case FenceSortMode.Type:
                    sorted = members.OrderByDescending(isDir)
                        .ThenBy(extOf, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(ic => ic.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    break;
                case FenceSortMode.ModifiedTime:
                    sorted = members.OrderByDescending(modTime)
                        .ThenBy(ic => ic.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    break;
                case FenceSortMode.Name:
                default:
                    sorted = members.OrderByDescending(isDir)
                        .ThenBy(ic => ic.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    break;
            }

            var tracks = new List<IconSlideTrack>();
            int innerCols = Math.Max(1, fence.OuterCols - 2);
            int innerRows = Math.Max(1, fence.OuterRows - 2);
            int maxSlots = innerCols * innerRows;
            for (int i = 0; i < Math.Min(sorted.Count, maxSlots); i++)
            {
                var ic = sorted[i];
                int dstCol = fence.OuterCol + 1 + (i % innerCols);
                int dstRow = fence.OuterRow + 1 + (i / innerCols);
                int curCol = ic.GridCol(m);
                int curRow = ic.GridRow(m);
                if (dstCol == curCol && dstRow == curRow) continue;
                int tx = ic.ClientPosition.X + (dstCol - curCol) * m.CellWidth;
                int ty = ic.ClientPosition.Y + (dstRow - curRow) * m.CellHeight;
                tracks.Add(new IconSlideTrack
                {
                    IconInfo = ic,
                    StartPos = ic.ClientPosition,
                    TargetPos = new Point(tx, ty),
                    LastSentPos = ic.ClientPosition
                });
                _knownIconCells[ic.Identity] = new Point(dstCol, dstRow);
            }
            if (tracks.Count > 0)
                StartIconSlideAnimation(tracks, m.SnapToGridEnabled);
        }

        public void StartRegionDrawNewFence()
        {
            if (_isDrawingRegion || !PrepareFenceInteraction()) return;
            var previewMetrics = _controller.GetGridMetrics();
            Rectangle selection;
            _isDrawingRegion = true;
            try
            {
                using (var picker = new RegionDrawOverlayForm(previewMetrics, Config.Fences))
                {
                    if (picker.ShowDialog() != DialogResult.OK) return;
                    selection = picker.SelectedRect;
                }
            }
            finally { _isDrawingRegion = false; }
            if (!PrepareFenceInteraction()) return;
            var m = _controller.GetGridMetrics();
            Rectangle work = Screen.FromRectangle(selection).WorkingArea;
            Rectangle r = Rectangle.Intersect(selection, work);
            if (r.Width < 60 || r.Height < 60) return;
            int innerCol = (int)Math.Round((r.Left - (double)m.OriginX) / Math.Max(1, m.CellWidth));
            int innerRow = (int)Math.Round((r.Top - (double)m.OriginY) / Math.Max(1, m.CellHeight));
            int innerCols = Math.Max(1, (int)Math.Round((double)r.Width / Math.Max(1, m.CellWidth)));
            int innerRows = Math.Max(1, (int)Math.Round((double)r.Height / Math.Max(1, m.CellHeight)));
            Rectangle area = MonitorGrid.FromWorkArea(work, m);
            CreateNewFence(m, new[] { area }, area, innerCol - 1, innerRow - 1, innerCols + 2, innerRows + 2);
        }

        public void DeleteFence(string fenceId)
        {
            if (!_exiting) PrepareFenceInteraction();
            var fc = Config.Fences.FirstOrDefault(f => f.Id == fenceId);
            if (fc != null) Config.Fences.Remove(fc);

            if (string.Equals(_recentRightClickFenceId, fenceId, StringComparison.Ordinal))
                ClearRightClickFallback();
            if (string.Equals(_desktopMenu.FenceId, fenceId, StringComparison.Ordinal))
                _desktopMenu.Cancel();
            if (string.Equals(_externalDropFenceId, fenceId, StringComparison.Ordinal))
                ClearExternalDrop();
            foreach (string k in _recentContextPlacements.Where(kv => string.Equals(kv.Value.TargetFenceId, fenceId, StringComparison.Ordinal)).Select(kv => kv.Key).ToArray())
                _recentContextPlacements.Remove(k);

            FenceChromeForm form;
            if (_forms.TryGetValue(fenceId, out form))
            {
                _forms.Remove(fenceId);
                form.Close();
                form.Dispose();
            }
            _desktopDirty = true;
            SaveConfig();
        }

        private bool _savePending;
        private long _nextSaveAttempt;

        public bool SaveConfig()
        {
            try
            {
                Config.Save();
                _savePending = false;
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException ||
                      ex is System.Security.SecurityException)) throw;
                if (!_savePending && _trayIcon != null)
                    _trayIcon.ShowBalloonTip(6000, "分区布局尚未保存",
                        "请检查配置文件的写入权限和可用空间。程序会自动重试。\n" + ex.Message, ToolTipIcon.Warning);
                _savePending = true;
                _nextSaveAttempt = _sessionClock.ElapsedMilliseconds + 5000;
                return false;
            }
        }

        private void RetryPendingSave()
        {
            if (_savePending && _sessionClock.ElapsedMilliseconds >= _nextSaveAttempt) SaveConfig();
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, false))
                {
                    if (key == null) return false;
                    string val = key.GetValue(AppName) as string;
                    if (string.IsNullOrEmpty(val)) return false;
                    string trimmed = val.Trim().Trim('"');
                    return string.Equals(trimmed, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private void ToggleStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, true))
                {
                    if (key == null) return;
                    if (IsStartupEnabled())
                        key.DeleteValue(AppName, false);
                    else
                        key.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机自启动失败：\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateTrayStates();
        }

        public void ExitApplication()
        {
            if (_exiting) return;
            _exitAfterInteraction = _forms.Values.Any(form => form.CompletingInteraction);
            if (_exitAfterInteraction) return;
            SettleBeforeExit();
            if (!_needsInitialFence && !SaveConfig() &&
                MessageBox.Show("无法保存当前布局。仍然退出并放弃未保存的更改？", AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                _exiting = false;
                if (DesktopReady) InitializeKnownIconsAndEnforce();
                return;
            }
            StopDesktopServices();
            ExitThread();
        }

        private void SettleBeforeExit()
        {
            bool canMove = DesktopReady;
            _exiting = true;
            foreach (var form in _forms.Values.ToList())
                form.SettleInteraction(canMove, false);
            FinishIconAnimationImmediately(canMove);
            ReleaseFreeIconMoveModeIfIdle();
        }

        private void StopDesktopServices()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            _watchTimer.Stop();
            _burstWatchTimer.Stop();
            _iconAnimTimer.Stop();
            UninstallPassiveMouseHook();
            UninstallDesktopWinEventHook();
            if (_desktopWatcher != null)
            {
                try { _desktopWatcher.EnableRaisingEvents = false; _desktopWatcher.Dispose(); } catch { }
                _desktopWatcher = null;
            }
            if (_commonDesktopWatcher != null)
            {
                try { _commonDesktopWatcher.EnableRaisingEvents = false; _commonDesktopWatcher.Dispose(); } catch { }
                _commonDesktopWatcher = null;
            }
            CloseFenceForms();
            _burstWatchTimer.Dispose();
            _iconAnimTimer.Dispose();
            _watchTimer.Dispose();
            _controller.Dispose();
            if (_uiInvoker != null && !_uiInvoker.IsDisposed)
                _uiInvoker.Dispose();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.ContextMenuStrip = null;
                _trayIcon.Icon = null;
                _trayIcon.Dispose();
            }
            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
            if (_trayAppIcon != null)
            {
                _trayAppIcon.Dispose();
                _trayAppIcon = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_exiting)
            {
                SettleBeforeExit();
                if (!_needsInitialFence) SaveConfig();
                StopDesktopServices();
            }
            base.Dispose(disposing);
        }
    }

    internal class RegionDrawOverlayForm : Form
    {
        private readonly DesktopGridMetrics _metrics;
        private readonly List<FenceConfig> _existingFences;
        private readonly Rectangle _tipMonitorBounds;
        private bool _dragging;
        private Point _startPt;
        private Point _curPt;
        public Rectangle SelectedRect { get; private set; }

        public RegionDrawOverlayForm(DesktopGridMetrics metrics, List<FenceConfig> existingFences)
        {
            _metrics = metrics;
            _existingFences = existingFences ?? new List<FenceConfig>();
            _tipMonitorBounds = Screen.FromPoint(Cursor.Position).Bounds;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Color.FromArgb(240, 245, 252);
            Opacity = 0.28;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!IsDisposed && DialogResult == DialogResult.None)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _startPt = e.Location;
                _curPt = e.Location;
                Capture = true;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                _curPt = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging && e.Button == MouseButtons.Left)
            {
                _dragging = false;
                if (Capture) Capture = false;
                Rectangle localRect = GetCurrentRect();
                SelectedRect = new Rectangle(
                    localRect.X + Bounds.X,
                    localRect.Y + Bounds.Y,
                    localRect.Width,
                    localRect.Height);
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private Rectangle GetCurrentRect()
        {
            int x = Math.Min(_startPt.X, _curPt.X);
            int y = Math.Min(_startPt.Y, _curPt.Y);
            int w = Math.Abs(_curPt.X - _startPt.X);
            int h = Math.Abs(_curPt.Y - _startPt.Y);
            return new Rectangle(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float tipDpi = NativeMethods.GetDpiScaleForPoint(new Point(_tipMonitorBounds.Left + _tipMonitorBounds.Width / 2, _tipMonitorBounds.Top + 40));
            Color accent = NativeMethods.GetSystemAccentColor();
            using (Font f = new Font("Microsoft YaHei UI", 16f * tipDpi, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(Color.FromArgb(20, 28, 40)))
            {
                string tip = "按住鼠标左键框选分区内部区域（实时预览吸附网格，Esc 或右键取消）";
                SizeF sz = e.Graphics.MeasureString(tip, f);
                float tipX = (_tipMonitorBounds.Left - Bounds.X) + (_tipMonitorBounds.Width - sz.Width) / 2f;
                float tipY = (_tipMonitorBounds.Top - Bounds.Y) + 40f * tipDpi;
                e.Graphics.DrawString(tip, f, b, tipX, tipY);
            }

            if (_dragging)
            {
                Rectangle localRect = GetCurrentRect();
                Rectangle screenRect = new Rectangle(localRect.X + Bounds.X, localRect.Y + Bounds.Y, localRect.Width, localRect.Height);
                Rectangle work = Screen.FromRectangle(screenRect).WorkingArea;
                Rectangle clipped = Rectangle.Intersect(screenRect, work);

                bool drewSnapped = false;
                if (clipped.Width >= 60 && clipped.Height >= 60 && _metrics.CellWidth > 0 && _metrics.CellHeight > 0)
                {
                    int innerCol = (int)Math.Round((clipped.Left - (double)_metrics.OriginX) / _metrics.CellWidth);
                    int innerRow = (int)Math.Round((clipped.Top - (double)_metrics.OriginY) / _metrics.CellHeight);
                    int innerCols = Math.Max(1, (int)Math.Round((double)clipped.Width / _metrics.CellWidth));
                    int innerRows = Math.Max(1, (int)Math.Round((double)clipped.Height / _metrics.CellHeight));
                    int outerCol = innerCol - 1, outerRow = innerRow - 1;
                    int outerCols = innerCols + 2, outerRows = innerRows + 2;
                    float localDpi = MonitorGrid.GetDpiScaleForCell(outerCol, outerRow, _metrics);

                    bool overlap = false;
                    foreach (var other in _existingFences)
                    {
                        if (FenceManager.ZonesOverlap(outerCol, outerRow, outerCols, outerRows,
                            other.OuterCol, other.OuterRow, other.OuterCols, other.OuterRows, _metrics, localDpi))
                        {
                            overlap = true;
                            break;
                        }
                    }

                    int hh;
                    Rectangle snappedScreen = FenceChromeForm.ComputeGridBounds(outerCol, outerRow, outerCols, outerRows, _metrics, localDpi, out hh);
                    Rectangle snappedLocal = new Rectangle(snappedScreen.X - Bounds.X, snappedScreen.Y - Bounds.Y, snappedScreen.Width, snappedScreen.Height);
                    Color frameColor = overlap ? Color.FromArgb(230, 100, 40) : accent;

                    using (Brush fill = new SolidBrush(Color.FromArgb(65, frameColor)))
                    using (Pen pen = new Pen(frameColor, 2.5f * localDpi))
                    {
                        e.Graphics.FillRectangle(fill, snappedLocal);
                        e.Graphics.DrawRectangle(pen, snappedLocal);
                    }
                    // Draw inner icon grid preview
                    Rectangle innerLocal = new Rectangle(
                        _metrics.OriginX + innerCol * _metrics.CellWidth - Bounds.X,
                        _metrics.OriginY + innerRow * _metrics.CellHeight - Bounds.Y,
                        innerCols * _metrics.CellWidth,
                        innerRows * _metrics.CellHeight);
                    using (Pen dashPen = new Pen(Color.FromArgb(180, frameColor), 1.5f) { DashStyle = DashStyle.Dash })
                        e.Graphics.DrawRectangle(dashPen, innerLocal);
                    drewSnapped = true;
                }

                if (!drewSnapped)
                {
                    using (Brush fill = new SolidBrush(Color.FromArgb(70, accent)))
                    using (Pen pen = new Pen(accent, 2.2f * tipDpi))
                    {
                        e.Graphics.FillRectangle(fill, localRect);
                        e.Graphics.DrawRectangle(pen, localRect);
                    }
                }
            }
        }
    }
}
