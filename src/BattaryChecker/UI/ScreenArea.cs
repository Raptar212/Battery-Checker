using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace BattaryChecker.UI;

/// <summary>Work areas of real monitors in WPF units; SystemParameters.WorkArea is the primary monitor only.</summary>
static class ScreenArea
{
    /// <summary>The work area of the monitor <paramref name="window"/> is on.</summary>
    public static Rect Of(Window window) =>
        ToDip(window, Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea);

    /// <summary>
    /// Is <paramref name="point"/> on some monitor (taskbar included: a floating widget may be parked on it)?
    /// The virtual-screen rectangle also covers the holes of an L-shaped layout or a monitor that is gone,
    /// where a widget would be out of reach.
    /// </summary>
    public static bool OnAnyMonitor(Window window, Point point) =>
        Forms.Screen.AllScreens.Any(s => ToDip(window, s.Bounds).Contains(point));

    static Rect ToDip(Window window, System.Drawing.Rectangle pixels)
    {
        var m = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(m.Transform(new Point(pixels.Left, pixels.Top)), m.Transform(new Point(pixels.Right, pixels.Bottom)));
    }
}
