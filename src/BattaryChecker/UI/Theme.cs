using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace BattaryChecker.UI;

/// <summary>Light/dark brushes. Windows hold them by DynamicResource, so swapping them recolours open windows.</summary>
public static class Theme
{
    static readonly Dictionary<string, Color> Dark = new()
    {
        ["SurfaceBrush"] = Color.FromArgb(0xF2, 0x1C, 0x1C, 0x21),
        ["CardBrush"] = Color.FromArgb(0xFF, 0x26, 0x26, 0x2C),
        ["FrameBrush"] = Color.FromArgb(0xFF, 0x3F, 0x3F, 0x46),
        ["TextBrush"] = Color.FromArgb(0xFF, 0xF4, 0xF4, 0xF5),
        ["MutedBrush"] = Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF),
        ["AccentBrush"] = Color.FromArgb(0xFF, 0xEF, 0x44, 0x44),
        ["AccentSoftBrush"] = Color.FromArgb(0x33, 0xEF, 0x44, 0x44),
        ["HoverBrush"] = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF),
        ["PressedBrush"] = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
    };

    static readonly Dictionary<string, Color> Light = new()
    {
        ["SurfaceBrush"] = Color.FromArgb(0xF2, 0xF3, 0xF4, 0xF6),
        ["CardBrush"] = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        ["FrameBrush"] = Color.FromArgb(0xFF, 0xD1, 0xD5, 0xDB),
        ["TextBrush"] = Color.FromArgb(0xFF, 0x11, 0x18, 0x27),
        ["MutedBrush"] = Color.FromArgb(0xFF, 0x6B, 0x72, 0x80),
        ["AccentBrush"] = Color.FromArgb(0xFF, 0xEF, 0x44, 0x44),
        ["AccentSoftBrush"] = Color.FromArgb(0x26, 0xEF, 0x44, 0x44),
        ["HoverBrush"] = Color.FromArgb(0x14, 0x00, 0x00, 0x00),
        ["PressedBrush"] = Color.FromArgb(0x24, 0x00, 0x00, 0x00),
    };

    public static bool IsLight(ThemeMode mode, bool systemLight) =>
        mode == ThemeMode.System ? systemLight : mode == ThemeMode.Light;

    public static IReadOnlyDictionary<string, Color> Palette(bool light) => light ? Light : Dark;

    /// <summary>The Windows apps theme; a missing value means dark, like the tray's taskbar check.</summary>
    public static bool SystemLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 0) is 1;

    static bool? _applied;

    /// <summary>
    /// Puts the brushes for the mode into the resources. Windows sends several change events per
    /// switch, so an unchanged theme leaves the brushes alone; returns whether anything changed.
    /// </summary>
    public static bool Apply(ResourceDictionary resources, ThemeMode mode)
    {
        var light = IsLight(mode, mode == ThemeMode.System && SystemLight());
        if (_applied == light)
            return false;
        _applied = light;
        foreach (var (key, color) in Palette(light))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }
        return true;
    }
}
