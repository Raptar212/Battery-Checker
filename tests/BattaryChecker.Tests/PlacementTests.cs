using BattaryChecker.UI;

namespace BattaryChecker.Tests;

public sealed class PlacementTests
{
    // The owner's layout: primary 2048x1152 at 0,0; the second monitor on its left, 43 px higher.
    static readonly (int L, int T, int R, int B) Primary = (0, 0, 2048, 1152), Second = (-1920, -43, 0, 1157);
    static readonly List<(int L, int T, int R, int B)> Monitors = [Primary, Second];

    [Theory]
    [InlineData(100, 100, 0)]
    [InlineData(-100, 100, 1)]
    [InlineData(0, 0, 0)] // left edge of the primary belongs to it, right edge of the second does not
    [InlineData(-1, 0, 1)]
    [InlineData(-3000, 500, 1)] // a monitor that is gone on the far left
    [InlineData(5000, -500, 0)]
    [InlineData(100, 1155, 0)] // below the primary by 4 px, by 101 px left of the second's edge
    public void Nearest_PicksTheClosestMonitor(int x, int y, int expected) => Assert.Equal(expected, Placement.Nearest(Monitors, (x, y)));

    [Fact]
    public void Nearest_NoMonitors() => Assert.Equal(-1, Placement.Nearest([], (0, 0)));

    [Theory]
    [InlineData(100, 100, 100, 100)] // inside: as is
    [InlineData(2000, 1130, 1948, 1112)] // over the bottom-right corner
    [InlineData(-50, -20, 0, 0)]
    [InlineData(1948, 1112, 1948, 1112)] // exactly touching the edges
    public void Clamp_KeepsTheRectInside(int x, int y, int ex, int ey) =>
        Assert.Equal((ex, ey), Placement.Clamp((x, y, x + 100, y + 40), Primary));

    [Fact]
    public void Clamp_TooLargeKeepsTopLeftOnTheBounds() =>
        Assert.Equal((0, 0), Placement.Clamp((500, 500, 3500, 2500), Primary));

    [Fact]
    public void PickTaskbar_ByCentreOnTheMonitor()
    {
        List<(int L, int T, int R, int B)> taskbars = [(0, 1112, 2048, 1152), (-1920, 1117, 0, 1157)];
        Assert.Equal(0, Placement.PickTaskbar(taskbars, Primary));
        Assert.Equal(1, Placement.PickTaskbar(taskbars, Second));
        Assert.Equal(-1, Placement.PickTaskbar(taskbars, (3000, 0, 4000, 1000)));
        Assert.Equal(-1, Placement.PickTaskbar([], Primary));
    }

    [Theory]
    [InlineData(1500, 96u, 1500)] // 100 %: the same numbers, old configs keep their place
    [InlineData(1000, 144u, 1500)] // 150 %
    [InlineData(-1280, 120u, -1600)] // a monitor left of the primary, 125 %
    [InlineData(100.4, 96u, 100)]
    public void DipToPixels_ScalesBySystemDpi(double dip, uint dpi, int expected) => Assert.Equal(expected, Placement.DipToPixels(dip, dpi));

    [Fact]
    public void Config_PositionMarkerDefaultsToDip() => Assert.False(new AppConfig().PositionInPixels);

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2047, 1151, true)]
    [InlineData(2048, 100, false)]
    [InlineData(100, -1, false)]
    public void Contains_RightAndBottomExclusive(int x, int y, bool expected) => Assert.Equal(expected, Placement.Contains(Primary, (x, y)));
}
