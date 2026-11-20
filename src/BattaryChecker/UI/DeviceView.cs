using System.Windows.Media;
using BattaryChecker.Devices;

namespace BattaryChecker.UI;

public enum Level { High, Medium, Low, Unknown }

/// <summary>Display rules shared by the widget, the panel and the tray.</summary>
public static class Levels
{
    /// <summary>Threshold values are inclusive: with low 20 / medium 50, 50 % is medium and 51 % high.</summary>
    public static Level Of(int? percent, int low, int medium) =>
        percent is not { } p ? Level.Unknown : p <= low ? Level.Low : p <= medium ? Level.Medium : Level.High;

    public static Color ColorOf(Level level) => level switch
    {
        Level.High => Color.FromRgb(0x10, 0xB9, 0x81),
        Level.Medium => Color.FromRgb(0xF5, 0x9E, 0x0B),
        Level.Low => Color.FromRgb(0xEF, 0x44, 0x44),
        _ => Color.FromRgb(0x6B, 0x72, 0x80),
    };

    /// <summary>
    /// Dash pattern of the tile ring's level arc, in stroke-thickness units. The stroke is
    /// centred on a circle of circumference pi*(D-T); round caps add T/2 at each end, so the
    /// dash is shortened by one thickness. Null = no arc (0 % or unknown); gap 0 = full ring.
    /// </summary>
    public static (double Dash, double Gap)? RingDash(int? percent, double diameter, double thickness)
    {
        if (percent is not > 0)
            return null;
        var circumference = Math.PI * (diameter - thickness) / thickness;
        if (percent >= 100)
            return (circumference, 0);
        var dash = Math.Max(0, circumference * percent.Value / 100.0 - 1);
        return (dash, circumference - dash);
    }

    /// <summary>Connected devices first, then by name.</summary>
    public static IEnumerable<BatteryReading> Ordered(IEnumerable<BatteryReading> readings) =>
        readings.OrderByDescending(r => r.Connected).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Tray tooltip: one line per connected device, cut to Windows' 127-char limit.</summary>
    public static string Tooltip(IEnumerable<BatteryReading> readings)
    {
        var lines = readings.Where(r => r.Connected && r.Percent is not null)
            .Select(r => $"{r.Name}: {r.Percent}%{(r.Charging ? " ⚡" : "")}");
        var text = string.Join("\n", lines);
        if (text.Length == 0)
            text = Text.AppName;
        return text.Length <= 127 ? text : text[..126] + "…";
    }
}

/// <summary>One device as the views show it.</summary>
public sealed class DeviceView(BatteryReading reading, bool hidden, int low, int medium, bool inTray = false)
{
    const double BatteryInnerWidth = 15.6; // 20 outline - 2×1.2 border - 2×1 padding (App.xaml "Battery")

    public BatteryReading Reading { get; } = reading;
    public string Id => Reading.Id;
    public string Name => Reading.Name;
    public bool Hidden { get; } = hidden;
    public bool Connected => Reading.Connected;
    public bool Charging => Reading.Connected && Reading.Charging;
    public Level Level => Reading.Connected ? Levels.Of(Reading.Percent, low, medium) : Level.Unknown;
    public Brush LevelBrush => Freeze(new SolidColorBrush(Levels.ColorOf(Level)));
    public const double RingDiameter = 40, RingThickness = 4; // WidgetWindow.xaml tile ring

    public bool RingVisible => Reading.Percent is > 0;

    /// <summary>Empty = solid ring (full).</summary>
    public DoubleCollection RingDashArray
    {
        get
        {
            var ring = Levels.RingDash(Reading.Percent, RingDiameter, RingThickness);
            var dashes = ring is { Gap: > 0 } r ? new DoubleCollection { r.Dash, r.Gap } : [];
            dashes.Freeze();
            return dashes;
        }
    }

    public string PercentText => Reading.Percent is { } p ? $"{p}%" : "—";
    public double FillWidth => Reading.Percent is { } p ? Math.Max(1.5, BatteryInnerWidth * Math.Clamp(p, 0, 100) / 100.0) : 0;
    public double Opacity => Reading.Connected ? 1 : 0.55;

    public string Subtitle => Reading.Connected
        ? $"{Text.Connected} · {Reading.Channel}"
        : Reading.Percent is { } p ? $"{Text.Disconnected} · {p}%" : Text.Disconnected;

    /// <summary>Segoe MDL2 / Fluent icon glyph for the device kind.</summary>
    public string Glyph => Reading.Kind switch
    {
        DeviceKind.Mouse => "",
        DeviceKind.Headset or DeviceKind.Earbuds => "",
        DeviceKind.Gamepad => "",
        DeviceKind.Keyboard => "",
        _ => "",
    };

    /// <summary>Shown by the tray icon right now, picked by the user or implicitly.</summary>
    public bool InTray { get; } = inTray;
    public double TrayOpacity => InTray ? 1 : 0.4;
    public string PinGlyph => InTray ? "" : ""; // Pinned / Pin
    public string TrayTip => InTray ? Text.RemoveFromTray : Text.AddToTray;

    // Segoe MDL2 on Windows 10 has no "Hide" (ED1A): the closed eye is the open one crossed out in XAML.
    public double EyeOpacity => Hidden ? 0.4 : 1;
    public string EyeTip => Hidden ? Text.Show : Text.Hide;

    static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
