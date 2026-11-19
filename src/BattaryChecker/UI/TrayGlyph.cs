using Microsoft.Win32;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;

namespace BattaryChecker.UI;

/// <summary>Draws the tray icon: bars, concentric rings or the lowest level as a number.</summary>
public static class TrayGlyph
{
    public const int MaxBars = 4, MaxRings = 3;

    /// <summary>
    /// Devices the icon shows: the user's selection (disconnected ones stay, drawn grey), or
    /// with nothing selected the connected ones with a level, lowest first.
    /// </summary>
    public static List<DeviceView> Pick(IEnumerable<DeviceView> devices, ICollection<string> selected, int max) =>
        selected.Count > 0
            ? devices.Where(d => selected.Contains(d.Id)).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).Take(max).ToList()
            : devices.Where(d => d.Connected && d.Reading.Percent is not null).OrderBy(d => d.Reading.Percent).Take(max).ToList();

    /// <summary>Critical: connected, not charging, at or below the critical level.</summary>
    public static bool IsCritical(DeviceView d, int critical) =>
        d.Connected && !d.Charging && d.Reading.Percent is { } p && p <= critical;

    public static int MaxOf(TrayStyle style) => style == TrayStyle.Ring ? MaxRings : MaxBars;

    /// <summary>Windows' own taskbar theme; read on every poll, so a switch shows within 30 s.</summary>
    public static bool LightTaskbar() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 0) is 1;

    /// <summary>Frames of the charging animation; the tray redraws one per tick while anything charges.</summary>
    public const int ChargeFrames = 8;

    /// <summary>
    /// Level drawn for a device at a charging frame: a charging device fills up from its real
    /// level to 100 % and starts over (frame 0 shows the real level).
    /// </summary>
    public static double Shown(DeviceView d, int frame)
    {
        var p = Math.Clamp(d.Reading.Percent ?? 0, 0, 100);
        return d.Charging ? p + (100 - p) * (frame % ChargeFrames) / (double)(ChargeFrames - 1) : p;
    }

    /// <summary>Whether the icon has a charging animation to run: the Number style shows only the lowest device.</summary>
    public static bool Animates(TrayStyle style, IReadOnlyList<DeviceView> shown) =>
        (style == TrayStyle.Number ? Lowest(shown) is { } d ? [d] : [] : shown)
            .Any(d => d.Charging && d.Reading.Percent < 100);

    static DeviceView? Lowest(IEnumerable<DeviceView> shown) =>
        shown.Where(d => d.Connected && d.Reading.Percent is not null).MinBy(d => d.Reading.Percent);

    /// <param name="blinkOff">The "off" half of a blink: critical devices are drawn empty.</param>
    /// <param name="frame">Charging animation frame, see <see cref="Shown"/>.</param>
    public static Drawing.Bitmap Render(int size, TrayStyle style, IReadOnlyList<DeviceView> shown, int critical,
        bool blinkOff, bool light, int frame = 0)
    {
        var bitmap = new Drawing.Bitmap(size, size);
        using var g = Drawing.Graphics.FromImage(bitmap);
        var fg = light ? Drawing.Color.Black : Drawing.Color.White;
        bool Empty(DeviceView d) => !d.Connected || d.Reading.Percent is null || (blinkOff && IsCritical(d, critical));
        double Level(DeviceView d) => Shown(d, frame);
        switch (style)
        {
            case TrayStyle.Ring:
                DrawRings(g, size, shown, fg, Empty, Level);
                break;
            case TrayStyle.Number:
                DrawNumber(g, size, Lowest(shown), fg, Empty, frame);
                break;
            default:
                DrawBars(g, size, shown, fg, Empty, Level);
                break;
        }
        return bitmap;
    }

    static void DrawBars(Drawing.Graphics g, int size, IReadOnlyList<DeviceView> shown, Drawing.Color fg,
        Func<DeviceView, bool> empty, Func<DeviceView, double> level)
    {
        var n = Math.Max(1, shown.Count);
        var gap = Math.Max(1, size / 16);
        var width = Math.Min((size - gap * (n - 1)) / n, size * 3 / 8);
        var x = (size - (n * width + (n - 1) * gap)) / 2;
        using var track = new Drawing.SolidBrush(Drawing.Color.FromArgb(80, fg));
        for (var i = 0; i < n; i++, x += width + gap)
        {
            g.FillRectangle(track, x, 0, width, size);
            if (i >= shown.Count)
                continue;
            var d = shown[i];
            if (!empty(d))
            {
                // At least an eighth: 3 % must still read as "a little red", not vanish into one pixel.
                var height = Math.Max(size / 8, (int)Math.Round(size * level(d) / 100));
                using var fill = new Drawing.SolidBrush(ToDrawing(Levels.ColorOf(d.Level)));
                g.FillRectangle(fill, x, size - height, width, height);
            }
        }
    }

    static void DrawRings(Drawing.Graphics g, int size, IReadOnlyList<DeviceView> shown, Drawing.Color fg,
        Func<DeviceView, bool> empty, Func<DeviceView, double> level)
    {
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        var single = shown.Count <= 1;
        var thickness = single ? size / 7f : size / 8f;
        var gap = size / 20f;
        using var track = new Drawing.Pen(Drawing.Color.FromArgb(80, fg), thickness);
        var radius = size / 2f - thickness / 2;
        for (var i = 0; i < Math.Max(1, shown.Count); i++, radius -= thickness + gap)
        {
            var rect = new Drawing.RectangleF(size / 2f - radius, size / 2f - radius, 2 * radius, 2 * radius);
            g.DrawEllipse(track, rect);
            if (i < shown.Count && !empty(shown[i]))
            {
                using var arc = new Drawing.Pen(ToDrawing(Levels.ColorOf(shown[i].Level)), thickness);
                g.DrawArc(arc, rect, -90, (float)(360 * level(shown[i]) / 100));
            }
        }

        var inner = Math.Max(0, 2 * (radius + thickness / 2)); // free space inside the innermost ring
        var center = new Drawing.RectangleF((size - inner) / 2, (size - inner) / 2, inner, inner);
        if (single && shown.Count == 1 && !empty(shown[0]) && shown[0].Reading.Percent < 100)
        {
            using var font = new Drawing.Font("Segoe UI", inner * 0.62f, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var brush = new Drawing.SolidBrush(fg);
            DrawCentered(g, shown[0].Reading.Percent!.Value.ToString(), font, brush, center);
        }
    }

    static void DrawNumber(Drawing.Graphics g, int size, DeviceView? lowest, Drawing.Color fg,
        Func<DeviceView, bool> empty, int frame)
    {
        if (lowest is not null && empty(lowest))
            return; // blink "off": nothing at all
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        var text = lowest?.Reading.Percent?.ToString() ?? "–";
        var emSize = size * (text.Length >= 3 ? 0.5f : 0.68f);
        using var path = new Drawing2D.GraphicsPath();
        using var format = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center,
        };
        using var family = new Drawing.FontFamily("Segoe UI");
        // Centred on a point, not in a box: a size-wide box trims "57" to "5".
        path.AddString(text, family, (int)Drawing.FontStyle.Bold, emSize, new Drawing.PointF(size / 2f, size / 2f), format);
        // A dark outline keeps yellow and green readable on a light taskbar and is invisible on a dark one.
        using var outline = new Drawing.Pen(Drawing.Color.FromArgb(200, 0, 0, 0), Math.Max(1.5f, size / 10f))
        {
            LineJoin = Drawing2D.LineJoin.Round,
        };
        g.DrawPath(outline, path);
        using var fill = new Drawing.SolidBrush(ToDrawing(Levels.ColorOf(lowest?.Level ?? Level.Unknown)));
        if (lowest is not { Charging: true, Reading.Percent: < 100 })
        {
            g.FillPath(fill, path);
            return;
        }
        // Charging: the digits fill with colour bottom-up from the real level to full, over and over, on a dim base.
        using var dim = new Drawing.SolidBrush(Drawing.Color.FromArgb(90, fg));
        g.FillPath(dim, path);
        var bounds = path.GetBounds();
        var part = (float)(Shown(lowest, frame) / 100);
        var state = g.Save();
        g.SetClip(new Drawing.RectangleF(0, bounds.Bottom - bounds.Height * part, size, size));
        g.FillPath(fill, path);
        g.Restore(state);
    }

    static void DrawCentered(Drawing.Graphics g, string text, Drawing.Font font, Drawing.Brush brush, Drawing.RectangleF r)
    {
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var format = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center,
        };
        g.DrawString(text, font, brush, r, format);
    }

    static Drawing.Color ToDrawing(System.Windows.Media.Color c) => Drawing.Color.FromArgb(c.R, c.G, c.B);
}
