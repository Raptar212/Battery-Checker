using BattaryChecker.UI;

namespace BattaryChecker.Tests;

public sealed class ThemeTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("bc-theme-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(ThemeMode.System, true, true)]
    [InlineData(ThemeMode.System, false, false)]
    [InlineData(ThemeMode.Dark, true, false)]
    [InlineData(ThemeMode.Dark, false, false)]
    [InlineData(ThemeMode.Light, true, true)]
    [InlineData(ThemeMode.Light, false, true)]
    public void IsLight_ModeAgainstSystem(ThemeMode mode, bool systemLight, bool expected) =>
        Assert.Equal(expected, Theme.IsLight(mode, systemLight));

    [Fact]
    public void Config_ThemeDefaultsToSystem() => Assert.Equal(ThemeMode.System, new AppConfig().Theme);

    [Fact]
    public void Palette_BothThemesHaveTheSameKeys()
    {
        var dark = Theme.Palette(false).Keys.Order().ToList();
        Assert.Equal(dark, Theme.Palette(true).Keys.Order().ToList());
        Assert.Contains("HoverBrush", dark);
    }

    [Theory]
    [InlineData("""{ "Theme": 9 }""", ThemeMode.System)]
    [InlineData("""{ "Theme": 2 }""", ThemeMode.Light)]
    public void Config_ThemeIsNormalizedOnLoad(string json, ThemeMode expected)
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, json);
        Assert.Equal(expected, AppConfig.Load(path).Theme);
    }
}
