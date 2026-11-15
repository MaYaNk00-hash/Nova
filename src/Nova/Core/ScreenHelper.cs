using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nova.Core;

public struct RectDip
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public RectDip(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }
}

public static class ScreenHelper
{
    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>
    /// Gets the Working Area (in WPF DIPs) for the monitor containing the specified window.
    /// Falls back to SystemParameters.WorkArea if native calls are unavailable.
    /// </summary>
    public static RectDip GetWorkingArea(Window? window)
    {
        if (window == null)
        {
            var fallback = SystemParameters.WorkArea;
            return new RectDip(fallback.Left, fallback.Top, fallback.Width, fallback.Height);
        }

        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                var fallback = SystemParameters.WorkArea;
                return new RectDip(fallback.Left, fallback.Top, fallback.Width, fallback.Height);
            }

            var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

            if (GetMonitorInfo(hMonitor, ref mi))
            {
                // Convert native pixel coordinates to WPF DIPs using the window's DPI scale factor
                var source = PresentationSource.FromVisual(window);
                double dpiScaleX = 1.0;
                double dpiScaleY = 1.0;

                if (source?.CompositionTarget != null)
                {
                    var matrix = source.CompositionTarget.TransformFromDevice;
                    dpiScaleX = matrix.M11;
                    dpiScaleY = matrix.M22;
                }

                double left = mi.rcWork.Left * dpiScaleX;
                double top = mi.rcWork.Top * dpiScaleY;
                double width = (mi.rcWork.Right - mi.rcWork.Left) * dpiScaleX;
                double height = (mi.rcWork.Bottom - mi.rcWork.Top) * dpiScaleY;

                return new RectDip(left, top, width, height);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to get monitor info via native API, using fallback: {ex.Message}");
        }

        var wa = SystemParameters.WorkArea;
        return new RectDip(wa.Left, wa.Top, wa.Width, wa.Height);
    }

    /// <summary>
    /// Calculates the top-center position for a given window width and height within the work area.
    /// </summary>
    public static Point CalculateTopCenter(RectDip workArea, double windowWidth, double marginTop = 0.0)
    {
        double left = workArea.Left + (workArea.Width - windowWidth) / 2.0;
        double top = workArea.Top + marginTop;
        return new Point(left, top);
    }

    /// <summary>
    /// Clamps the window position so that it does not escape the visible working area.
    /// </summary>
    public static Point ClampToWorkArea(RectDip workArea, double left, double top, double width, double height)
    {
        double clampedLeft = Math.Max(workArea.Left, Math.Min(left, workArea.Right - width));
        double clampedTop = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - height));
        return new Point(clampedLeft, clampedTop);
    }
}

