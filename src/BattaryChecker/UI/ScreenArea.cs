using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace BattaryChecker.UI;

/// <summary>
/// Work areas of real monitors in WPF units (SystemParameters.WorkArea is the primary monitor only),
/// and window positions in physical pixels: DIPs depend on the DPI of the monitor a window is on.
/// </summary>
static class ScreenArea
{
    /// <summary>The work area of the monitor <paramref name="window"/> is on.</summary>
    public static Rect Of(Window window) =>
        ToDip(window, Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea);

    /// <summary>The whole monitor <paramref name="window"/> is on, taskbar included, in physical pixels.</summary>
    public static (int L, int T, int R, int B) MonitorOf(nint window)
    {
        var b = Forms.Screen.FromHandle(window).Bounds;
        return (b.Left, b.Top, b.Right, b.Bottom);
    }

    /// <summary>A physical position as a WPF position of <paramref name="window"/> on its current monitor.</summary>
    public static Point ToDip(Window window, int x, int y) =>
        (PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity).Transform(new Point(x, y));

    /// <summary>The DPI DPI-unaware and system-aware code saw: the primary monitor's at logon.</summary>
    public static uint SystemDpi() => GetDpiForSystem();

    static Rect ToDip(Window window, System.Drawing.Rectangle pixels)
    {
        var m = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(m.Transform(new Point(pixels.Left, pixels.Top)), m.Transform(new Point(pixels.Right, pixels.Bottom)));
    }

    /// <summary>A WPF position of <paramref name="window"/> in physical pixels of its current monitor.</summary>
    public static (int X, int Y) ToPhysical(Window window, Point dip)
    {
        var p = (PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity).Transform(dip);
        return ((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    /// <summary>
    /// Bounds of every monitor, taskbar included, in physical pixels. Asked fresh each time: Forms.Screen.AllScreens
    /// is cached and may still list a monitor that is gone when WM_DISPLAYCHANGE arrives.
    /// </summary>
    public static List<(int L, int T, int R, int B)> Monitors()
    {
        var list = new List<(int L, int T, int R, int B)>();
        EnumDisplayMonitors(0, 0, (nint _, nint _, ref NativeRect r, nint _) =>
        {
            list.Add((r.Left, r.Top, r.Right, r.Bottom));
            return true;
        }, 0);
        return list;
    }

    public static bool WindowRect(nint window, out (int L, int T, int R, int B) rect)
    {
        var ok = GetWindowRect(window, out var r);
        rect = (r.Left, r.Top, r.Right, r.Bottom);
        return ok;
    }

    /// <summary>Moves a window to a physical position; Windows sends it WM_DPICHANGED if the monitor's DPI differs.</summary>
    public static void MoveTo(nint window, int x, int y) =>
        SetWindowPos(window, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    delegate bool MonitorEnumProc(nint monitor, nint dc, ref NativeRect rect, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
