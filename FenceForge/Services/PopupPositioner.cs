using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FenceForge.Services;

public static class PopupPositioner
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int MdtEffectiveDpi = 0;

    public static void PositionNearPointer(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        uint dpiX = 96;
        uint dpiY = 96;
        try
        {
            _ = GetDpiForMonitor(monitor, MdtEffectiveDpi, out dpiX, out dpiY);
        }
        catch (DllNotFoundException)
        {
            dpiX = dpiY = 96;
        }
        catch (EntryPointNotFoundException)
        {
            dpiX = dpiY = 96;
        }

        var widthDip = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var heightDip = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
        var width = Math.Max(1, (int)Math.Ceiling(widthDip * dpiX / 96d));
        var height = Math.Max(1, (int)Math.Ceiling(heightDip * dpiY / 96d));
        var gap = Math.Max(8, (int)Math.Round(12 * dpiX / 96d));

        var left = cursor.X + gap;
        var top = cursor.Y + gap;
        if (left + width > info.Work.Right)
        {
            left = cursor.X - gap - width;
        }

        if (top + height > info.Work.Bottom)
        {
            top = cursor.Y - gap - height;
        }

        left = Math.Clamp(left, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        top = Math.Clamp(top, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rectangle Monitor;
        public Rectangle Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
