using BattaryChecker.Devices;
using BattaryChecker.UI;

namespace BattaryChecker.Tests;

public class TrayGlyphTests
{
    static DeviceView View(string name, bool connected, int? percent, bool charging = false) =>
        new(new BatteryReading($"id:{name}", name, DeviceKind.Mouse, "USB", connected, percent, charging, DateTime.Now),
            hidden: false, low: 20, medium: 50);

    [Fact]
    public void Pick_WithoutSelection_ConnectedWithLevelLowestFirst()
    {
        DeviceView[] all = [View("a", true, 80), View("b", true, 10), View("c", false, 5), View("d", true, null), View("e", true, 40)];
        Assert.Equal(["b", "e"], TrayGlyph.Pick(all, [], max: 2).Select(d => d.Name));
    }

    [Fact]
    public void Pick_WithSelection_KeepsDisconnectedSortedByName()
    {
        DeviceView[] all = [View("z", true, 80), View("m", false, 5), View("a", true, 30)];
        Assert.Equal(["m", "z"], TrayGlyph.Pick(all, ["id:z", "id:m"], max: 4).Select(d => d.Name));
    }

    [Theory]
    [InlineData(true, 5, false, true)]
    [InlineData(true, 6, false, false)]
    [InlineData(true, 3, true, false)] // charging: no alarm
    [InlineData(false, 3, false, false)] // last known level of a disconnected device
    public void IsCritical(bool connected, int percent, bool charging, bool expected)
    {
        Assert.Equal(expected, TrayGlyph.IsCritical(View("x", connected, percent, charging), critical: 5));
    }

    [Theory]
    [InlineData(5, 20, 5)]
    [InlineData(0, 20, 1)]
    [InlineData(25, 20, 19)] // always below low
    [InlineData(5, 1, 1)] // hand-edited low below the settings floor: no ArgumentException
    public void ClampCritical(int critical, int low, int expected)
    {
        Assert.Equal(expected, AppConfig.ClampCritical(critical, low));
    }

    [Fact]
    public void Render_BlinkOffEmptiesCriticalBar()
    {
        DeviceView[] shown = [View("x", true, 3)];
        using var on = TrayGlyph.Render(16, TrayStyle.Bars, shown, critical: 5, blinkOff: false, light: false);
        using var off = TrayGlyph.Render(16, TrayStyle.Bars, shown, critical: 5, blinkOff: true, light: false);
        var red = Levels.ColorOf(Level.Low);
        Assert.Equal((red.R, red.G, red.B), Rgb(on.GetPixel(8, 15)));
        Assert.NotEqual((red.R, red.G, red.B), Rgb(off.GetPixel(8, 15)));
    }

    [Fact]
    public void Shown_ChargingFillsFromLevelToFullThenRestarts()
    {
        var charging = View("c", true, 30, charging: true);
        Assert.Equal(30, TrayGlyph.Shown(charging, 0));
        Assert.Equal(100, TrayGlyph.Shown(charging, TrayGlyph.ChargeFrames - 1));
        Assert.Equal(30, TrayGlyph.Shown(charging, TrayGlyph.ChargeFrames)); // starts over
        Assert.Equal(30, TrayGlyph.Shown(View("n", true, 30), TrayGlyph.ChargeFrames - 1)); // not charging: still
    }

    [Fact]
    public void Render_ChargingBarReachesTopOnLastFrame()
    {
        DeviceView[] shown = [View("x", true, 30, charging: true)];
        using var first = TrayGlyph.Render(16, TrayStyle.Bars, shown, 5, false, false, frame: 0);
        using var last = TrayGlyph.Render(16, TrayStyle.Bars, shown, 5, false, false, frame: TrayGlyph.ChargeFrames - 1);
        var yellow = Levels.ColorOf(Level.Medium);
        Assert.NotEqual((yellow.R, yellow.G, yellow.B), Rgb(first.GetPixel(8, 0)));
        Assert.Equal((yellow.R, yellow.G, yellow.B), Rgb(last.GetPixel(8, 0)));
    }

    [Fact]
    public void Render_NumberChargingAtFullIsFullyColoured()
    {
        DeviceView[] shown = [View("x", true, 100, charging: true)];
        Assert.False(TrayGlyph.Animates(TrayStyle.Number, shown)); // nothing left to fill: no timer
        using var charging = TrayGlyph.Render(16, TrayStyle.Number, shown, 5, false, false, frame: 0);
        using var still = TrayGlyph.Render(16, TrayStyle.Number, [View("x", true, 100)], 5, false, false, frame: 0);
        // Same as a device that is not charging, not a dim "100" with a coloured sliver.
        Assert.All(Enumerable.Range(0, 256), i => Assert.Equal(still.GetPixel(i % 16, i / 16), charging.GetPixel(i % 16, i / 16)));
    }

    [Fact]
    public void Render_NumberChargingFillsUp()
    {
        DeviceView[] shown = [View("x", true, 30, charging: true)];
        Assert.True(TrayGlyph.Animates(TrayStyle.Number, shown));
        using var first = TrayGlyph.Render(16, TrayStyle.Number, shown, 5, false, false, frame: 0);
        using var last = TrayGlyph.Render(16, TrayStyle.Number, shown, 5, false, false, frame: TrayGlyph.ChargeFrames - 1);
        var yellow = Levels.ColorOf(Level.Medium);
        int Count(System.Drawing.Bitmap b) =>
            Enumerable.Range(0, 256).Count(i => Rgb(b.GetPixel(i % 16, i / 16)) == (yellow.R, yellow.G, yellow.B));
        Assert.True(Count(last) > Count(first));
    }

    [Fact]
    public void Animates_NumberStyleOnlyForTheLowestDevice()
    {
        DeviceView[] shown = [View("low", true, 20), View("high", true, 70, charging: true)];
        Assert.False(TrayGlyph.Animates(TrayStyle.Number, shown));
        Assert.True(TrayGlyph.Animates(TrayStyle.Bars, shown));
    }

    [Theory]
    [InlineData(TrayStyle.Bars)]
    [InlineData(TrayStyle.Ring)]
    [InlineData(TrayStyle.Number)]
    public void Render_AllStylesDrawSomething(TrayStyle style)
    {
        DeviceView[] shown = [View("a", true, 57), View("b", true, 15, charging: true), View("c", false, 90), View("d", true, 100)];
        foreach (var size in new[] { 16, 20, 32 })
        {
            using var bitmap = TrayGlyph.Render(size, style, shown.Take(TrayGlyph.MaxOf(style)).ToList(), 5, false, light: true);
            Assert.Equal(size, bitmap.Width);
            Assert.Contains(Enumerable.Range(0, size * size), i => bitmap.GetPixel(i % size, i / size).A > 0);
        }
    }

    static (byte, byte, byte) Rgb(System.Drawing.Color c) => (c.R, c.G, c.B);
}
