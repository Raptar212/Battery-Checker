namespace BattaryChecker.UI;

/// <summary>
/// Pure geometry for multi-monitor placement, on rectangles in physical pixels as (Left, Top, Right, Bottom),
/// right and bottom exclusive.
/// </summary>
public static class Placement
{
    /// <summary>Is <paramref name="point"/> inside <paramref name="rect"/>?</summary>
    public static bool Contains((int L, int T, int R, int B) rect, (int X, int Y) point) =>
        point.X >= rect.L && point.X < rect.R && point.Y >= rect.T && point.Y < rect.B;

    /// <summary>Index of the monitor closest to <paramref name="point"/> (distance 0 inside it); -1 for none.</summary>
    public static int Nearest(IReadOnlyList<(int L, int T, int R, int B)> monitors, (int X, int Y) point)
    {
        var (best, bestDistance) = (-1, long.MaxValue);
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            long dx = point.X < m.L ? m.L - point.X : point.X >= m.R ? point.X - m.R + 1 : 0;
            long dy = point.Y < m.T ? m.T - point.Y : point.Y >= m.B ? point.Y - m.B + 1 : 0;
            if (dx * dx + dy * dy < bestDistance)
                (best, bestDistance) = (i, dx * dx + dy * dy);
        }
        return best;
    }

    /// <summary>
    /// Top-left corner that keeps <paramref name="rect"/> inside <paramref name="bounds"/>; a rect larger than the
    /// bounds keeps its left and top edges on them.
    /// </summary>
    public static (int X, int Y) Clamp((int L, int T, int R, int B) rect, (int L, int T, int R, int B) bounds) =>
        (Math.Max(bounds.L, Math.Min(rect.L, bounds.R - (rect.R - rect.L))),
         Math.Max(bounds.T, Math.Min(rect.T, bounds.B - (rect.B - rect.T))));

    /// <summary>A coordinate in DIPs at <paramref name="dpi"/> (96 = 100 %) in physical pixels.</summary>
    public static int DipToPixels(double dip, uint dpi) => (int)Math.Round(dip * dpi / 96);

    /// <summary>Index of the first taskbar whose centre lies on <paramref name="monitor"/>; -1 for none.</summary>
    public static int PickTaskbar(IReadOnlyList<(int L, int T, int R, int B)> taskbars, (int L, int T, int R, int B) monitor)
    {
        for (var i = 0; i < taskbars.Count; i++)
        {
            var t = taskbars[i];
            if (Contains(monitor, (t.L + (t.R - t.L) / 2, t.T + (t.B - t.T) / 2)))
                return i;
        }
        return -1;
    }
}
