using BattaryChecker.UI;

namespace BattaryChecker.Tests;

public sealed class WidgetStyleTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("bc-widget-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Config_WidgetStyleDefaultsToHorizontal() => Assert.Equal(WidgetStyle.Horizontal, new AppConfig().WidgetStyle);

    [Theory]
    [InlineData("""{ "WidgetStyle": 9 }""", WidgetStyle.Horizontal)]
    [InlineData("""{ "WidgetStyle": -1 }""", WidgetStyle.Horizontal)]
    [InlineData("""{ "WidgetStyle": 3 }""", WidgetStyle.Tiles)]
    public void Config_WidgetStyleIsNormalizedOnLoad(string json, WidgetStyle expected)
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, json);
        Assert.Equal(expected, AppConfig.Load(path).WidgetStyle);
    }

    [Theory]
    [InlineData("""{ "PinnedWidgetStyle": 1 }""", WidgetStyle.Horizontal)]
    [InlineData("""{ "PinnedWidgetStyle": 9 }""", WidgetStyle.Horizontal)]
    [InlineData("""{ "PinnedWidgetStyle": 2 }""", WidgetStyle.Compact)]
    public void Config_PinnedStyleKeepsOnlyHorizontalOrCompact(string json, WidgetStyle expected)
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, json);
        Assert.Equal(expected, AppConfig.Load(path).PinnedWidgetStyle);
    }

    [Fact]
    public void Config_DeviceOrderDropsBlanksAndDuplicates()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{ "DeviceOrder": ["a", "", null, "b", "a"] }""");
        Assert.Equal(["a", "b"], AppConfig.Load(path).DeviceOrder);
    }

    [Fact]
    public void Config_NullDeviceOrderBecomesEmpty()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{ "DeviceOrder": null }""");
        Assert.Empty(AppConfig.Load(path).DeviceOrder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void RingDash_NoArcWithoutLevel(int? percent) => Assert.Null(Levels.RingDash(percent, 40, 4));

    [Fact]
    public void RingDash_FullRingHasNoGap()
    {
        var (dash, gap) = Levels.RingDash(100, 40, 4)!.Value;
        Assert.Equal(0, gap);
        Assert.Equal(Math.PI * 36 / 4, dash, 6);
    }

    [Fact]
    public void RingDash_HalfIsHalfTheCircleMinusTheRoundCaps()
    {
        var (dash, gap) = Levels.RingDash(50, 40, 4)!.Value;
        var circumference = Math.PI * 36 / 4; // in stroke thicknesses
        Assert.Equal(circumference, dash + gap, 6);
        Assert.Equal(circumference / 2 - 1, dash, 6);
    }

    [Theory]
    [InlineData(100, 0, true)]
    [InlineData(50, 2, true)]
    [InlineData(0, 0, false)]
    [InlineData(null, 0, false)]
    public void DeviceView_RingProperties(int? percent, int dashEntries, bool visible)
    {
        var reading = new BattaryChecker.Devices.BatteryReading("id", "Mouse", BattaryChecker.Devices.DeviceKind.Mouse,
            "2.4 GHz", true, percent, false, DateTime.Now);
        var view = new DeviceView(reading, false, 20, 50);
        Assert.Equal(dashEntries, view.RingDashArray.Count);
        Assert.Equal(visible, view.RingVisible);
    }

    [Fact]
    public void RingDash_TinyLevelStillDrawsADot()
    {
        var (dash, gap) = Levels.RingDash(1, 40, 4)!.Value;
        Assert.Equal(0, dash);
        Assert.True(gap > 0);
    }
}
