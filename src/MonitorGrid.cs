using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeskFences
{
    internal static class MonitorGrid
    {
        internal static Rectangle FromWorkArea(Rectangle work, DesktopGridMetrics metrics)
        {
            int cw = Math.Max(1, metrics.CellWidth);
            int ch = Math.Max(1, metrics.CellHeight);
            int left = (int)Math.Ceiling((work.Left - (double)metrics.OriginX) / cw);
            int top = (int)Math.Ceiling((work.Top - (double)metrics.OriginY) / ch);
            int right = (int)Math.Floor((work.Right - (double)metrics.OriginX) / cw);
            int bottom = (int)Math.Floor((work.Bottom - (double)metrics.OriginY) / ch);
            return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        internal static Rectangle[] GetAreas(DesktopGridMetrics metrics)
        {
            return Screen.AllScreens.Select(s => FromWorkArea(s.WorkingArea, metrics)).ToArray();
        }

        internal static Point ClampOrigin(Rectangle area, Point desired, Size size)
        {
            return new Point(Math.Max(area.Left, Math.Min(area.Right - size.Width, desired.X)),
                             Math.Max(area.Top, Math.Min(area.Bottom - size.Height, desired.Y)));
        }

        internal static Rectangle Resize(Rectangle start, Rectangle area, int dx, int dy,
            bool moveLeft, bool moveTop, bool moveRight, bool moveBottom)
        {
            int left = moveLeft ? Math.Max(area.Left, Math.Min(start.Right - 3, start.Left + dx)) : start.Left;
            int top = moveTop ? Math.Max(area.Top, Math.Min(start.Bottom - 3, start.Top + dy)) : start.Top;
            int right = moveRight ? Math.Min(area.Right, Math.Max(start.Left + 3, start.Right + dx)) : start.Right;
            int bottom = moveBottom ? Math.Min(area.Bottom, Math.Max(start.Top + 3, start.Bottom + dy)) : start.Bottom;
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        internal static float GetDpiScaleForCell(int col, int row, DesktopGridMetrics metrics)
        {
            int px = metrics.OriginX + col * Math.Max(1, metrics.CellWidth);
            int py = metrics.OriginY + row * Math.Max(1, metrics.CellHeight);
            return NativeMethods.GetDpiScaleForPoint(new Point(px, py));
        }

        // Prefer a monitor where the requested size fits, then the closest position in pixels.
        // When the requested size exceeds every monitor, retain as much of the fence as possible.
        internal static Rectangle BestArea(Rectangle[] areas, Point desired, Size size, DesktopGridMetrics metrics)
        {
            Rectangle best = Rectangle.Empty;
            long bestCells = -1;
            double bestDistance = double.MaxValue;
            foreach (Rectangle area in areas)
            {
                if (area.Width < 3 || area.Height < 3) continue;
                var fitted = new Size(Math.Min(area.Width, size.Width), Math.Min(area.Height, size.Height));
                long cells = (long)fitted.Width * fitted.Height;
                Point origin = ClampOrigin(area, desired, fitted);
                double dx = (origin.X - (double)desired.X) * Math.Max(1, metrics.CellWidth);
                double dy = (origin.Y - (double)desired.Y) * Math.Max(1, metrics.CellHeight);
                double distance = dx * dx + dy * dy;
                if (cells > bestCells || (cells == bestCells && distance < bestDistance))
                {
                    best = area;
                    bestCells = cells;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
