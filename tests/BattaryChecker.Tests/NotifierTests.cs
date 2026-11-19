using BattaryChecker.Devices;

namespace BattaryChecker.Tests;

public class NotifierTests
{
    static readonly DateTime T0 = new(2026, 11, 16, 14, 0, 0);

    static BatteryReading Mouse(int percent, bool charging = false, bool connected = true) =>
        new("logi:C537-01", "G602", DeviceKind.Mouse, "2.4 GHz", connected, percent, charging, T0);

    [Fact]
    public void Low_AlertsOnceThenWaitsForCooldown()
    {
        var n = new Notifier();
        Assert.Single(n.Check([Mouse(15)], 20, T0));
        Assert.Empty(n.Check([Mouse(14)], 20, T0.AddMinutes(30)));
        Assert.Single(n.Check([Mouse(12)], 20, T0 + Notifier.LowCooldown));
    }

    [Fact]
    public void Low_RearmsAfterChargingStarts()
    {
        var n = new Notifier();
        Assert.Single(n.Check([Mouse(15)], 20, T0));
        Assert.Empty(n.Check([Mouse(16, charging: true)], 20, T0.AddMinutes(1)));
        Assert.Single(n.Check([Mouse(15)], 20, T0.AddMinutes(2))); // unplugged again: alert at once
    }

    [Fact]
    public void Low_QuietWhileChargingOrDisconnectedOrAboveThreshold()
    {
        var n = new Notifier();
        Assert.Empty(n.Check([Mouse(10, charging: true), Mouse(5, connected: false) with { Id = "x" }, Mouse(21) with { Id = "y" }], 20, T0));
    }

    [Fact]
    public void Full_OnlyAfterSeenCharging()
    {
        var n = new Notifier();
        Assert.Empty(n.Check([Mouse(100)], 20, T0)); // already full at start: quiet
        Assert.Empty(n.Check([Mouse(90, charging: true)], 20, T0.AddMinutes(1)));
        var alerts = n.Check([Mouse(100, charging: true)], 20, T0.AddMinutes(30));
        Assert.Equal(UI.Text.FullBatteryTitle, Assert.Single(alerts).Title);
        Assert.Empty(n.Check([Mouse(100, charging: true)], 20, T0.AddMinutes(31))); // once
    }

    [Fact]
    public void Full_ForgetsChargingWhenUnpluggedEarly()
    {
        var n = new Notifier();
        n.Check([Mouse(90, charging: true)], 20, T0);
        n.Check([Mouse(91)], 20, T0.AddMinutes(1)); // unplugged
        Assert.Empty(n.Check([Mouse(100)], 20, T0.AddMinutes(2)));
    }
}
