using System.IO;
using BattaryChecker.Devices;
using BattaryChecker.UI;

namespace BattaryChecker.Tests;

public class LevelsTests
{
    [Theory]
    [InlineData(51, Level.High)]
    [InlineData(50, Level.Medium)] // reference widget: 50 % orange, 51 % green
    [InlineData(21, Level.Medium)]
    [InlineData(20, Level.Low)]
    [InlineData(0, Level.Low)]
    [InlineData(null, Level.Unknown)]
    public void Of_UsesInclusiveThresholds(int? percent, Level expected)
    {
        Assert.Equal(expected, Levels.Of(percent, low: 20, medium: 50));
    }

    static BatteryReading Reading(string name, bool connected, int? percent = 50, bool charging = false) =>
        new($"id:{name}", name, DeviceKind.Mouse, "2.4 GHz", connected, percent, charging, DateTime.Now);

    [Fact]
    public void Ordered_ConnectedFirstThenByName()
    {
        var names = Levels.Ordered([Reading("b", false), Reading("c", true), Reading("a", true)]).Select(r => r.Name);
        Assert.Equal(["a", "c", "b"], names);
    }

    [Fact]
    public void Tooltip_ListsOnlyConnectedWithLevel()
    {
        var text = Levels.Tooltip([Reading("Mouse", true, 50, charging: true), Reading("Buds", false, 100), Reading("Pad", true, null)]);
        Assert.Equal("Mouse: 50% ⚡", text);
    }

    [Fact]
    public void Tooltip_FallsBackToAppNameAndFitsLimit()
    {
        Assert.Equal(Text.AppName, Levels.Tooltip([]));
        var many = Enumerable.Range(0, 20).Select(i => Reading($"Very long device name {i}", true));
        Assert.True(Levels.Tooltip(many).Length <= 127);
    }

    [Fact]
    public void DeviceView_DisconnectedIsGreyWithLastLevel()
    {
        var view = new DeviceView(Reading("Buds", false, 100), hidden: false, 20, 50);
        Assert.Equal(Level.Unknown, view.Level);
        Assert.Equal($"{Text.Disconnected} · 100%", view.Subtitle);
        Assert.False(view.Charging);
    }
}

public class PresenceTests
{
    static readonly DateTime Now = new(2026, 11, 16, 14, 0, 0);

    static BatteryReading Seen(TimeSpan ago, bool connected = true) =>
        new("logi:C537-01", "G602", DeviceKind.Mouse, "2.4 GHz", connected, 50, false, Now - ago);

    [Fact]
    public void Silent_RecentlySeenStaysConnected()
    {
        var last = Seen(TimeSpan.FromMinutes(2));
        Assert.Same(last, Presence.Silent(last, Now));
    }

    [Fact]
    public void Silent_AfterGraceGoesDisconnectedKeepingLevel()
    {
        var shown = Presence.Silent(Seen(TimeSpan.FromMinutes(6)), Now);
        Assert.NotNull(shown);
        Assert.False(shown.Connected);
        Assert.Equal(50, shown.Percent);
    }

    [Fact]
    public void Silent_DisconnectedStaysDisconnectedAndUnknownStaysUnknown()
    {
        Assert.False(Presence.Silent(Seen(TimeSpan.FromSeconds(10), connected: false), Now)!.Connected);
        Assert.Null(Presence.Silent(null, Now));
    }
}

public sealed class AppConfigTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("bc-config-").FullName;
    string ConfigPath => Path.Combine(_dir, "config.json");

    [Fact]
    public void Load_MissingFileGivesDefaults()
    {
        var config = AppConfig.Load(ConfigPath);
        Assert.Equal(20, config.LowThreshold);
        Assert.Empty(config.HiddenDevices);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        new AppConfig { WidgetLeft = -188, WidgetTop = 1064, HiddenDevices = ["logi:C537-01"] }.Save(ConfigPath);
        var loaded = AppConfig.Load(ConfigPath);
        Assert.Equal(-188, loaded.WidgetLeft);
        Assert.Equal(["logi:C537-01"], loaded.HiddenDevices);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Load_ValidJsonWithBadValuesIsNormalized()
    {
        File.WriteAllText(ConfigPath, """{ "LowThreshold": 1, "CriticalThreshold": 50, "TrayStyle": 99, "TrayDevices": null }""");
        var config = AppConfig.Load(ConfigPath);
        Assert.Equal(5, config.LowThreshold);
        Assert.Equal(4, config.CriticalThreshold);
        Assert.Equal(TrayStyle.Bars, config.TrayStyle);
        Assert.Empty(config.TrayDevices);
    }

    [Fact]
    public void Load_LockedFileGivesDefaultsAndIsNeverOverwritten()
    {
        new AppConfig { LowThreshold = 30 }.Save(ConfigPath);
        var original = File.ReadAllText(ConfigPath);
        AppConfig config;
        using (new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None)) // antivirus / sync lock
            config = AppConfig.Load(ConfigPath);
        Assert.Equal(20, config.LowThreshold);

        config.WidgetLeft = 100;
        config.Save(ConfigPath); // lock gone, but this run only has defaults: must not write them
        Assert.Equal(original, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Save_LockedFileIsSkippedAndNextSaveWritesEverything()
    {
        var config = new AppConfig { LowThreshold = 30 };
        config.Save(ConfigPath);
        config.WidgetLeft = 100;
        using (new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            config.Save(ConfigPath); // must not throw
        Assert.Null(AppConfig.Load(ConfigPath).WidgetLeft);

        config.LowThreshold = 25;
        config.Save(ConfigPath);
        var loaded = AppConfig.Load(ConfigPath);
        Assert.Equal(100, loaded.WidgetLeft);
        Assert.Equal(25, loaded.LowThreshold);
    }

    [Fact]
    public void Load_CorruptFileIsKeptAsideNotOverwritten()
    {
        File.WriteAllText(ConfigPath, "{ not json");
        var config = AppConfig.Load(ConfigPath);
        Assert.Equal(20, config.LowThreshold);
        Assert.Equal("{ not json", File.ReadAllText(ConfigPath + ".bad"));
    }

    [Theory]
    [InlineData(20, 50, 20, 50)]
    [InlineData(0, 50, 5, 50)] // low floor
    [InlineData(80, 90, 50, 90)] // low ceiling
    [InlineData(30, 31, 30, 35)] // medium at least 5 above low
    [InlineData(20, 100, 20, 95)] // medium ceiling
    public void ClampThresholds_KeepsSaneValues(int low, int medium, int expectedLow, int expectedMedium)
    {
        Assert.Equal((expectedLow, expectedMedium), AppConfig.ClampThresholds(low, medium));
    }

    [Fact]
    public void NameOf_UsesTrimmedAliasOrOwnName()
    {
        var config = new AppConfig { DeviceNames = { ["logi:C537-01"] = " G602 ", ["blank"] = "  " } };
        Assert.Equal("G602", config.NameOf("logi:C537-01", "Wireless Gaming Mouse"));
        Assert.Equal("Own", config.NameOf("blank", "Own"));
        Assert.Equal("Own", config.NameOf("unknown", "Own"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}

public sealed class AutostartTests : IDisposable
{
    // A throwaway key under HKCU, never the real Run key.
    readonly string _key = $@"Software\BattaryChecker.Tests\{Guid.NewGuid():N}";

    [Fact]
    public void Set_TogglesTheValue()
    {
        Assert.False(Autostart.IsEnabled(_key));
        Assert.True(Autostart.Set(true, _key));
        Assert.True(Autostart.IsEnabled(_key));
        Assert.True(Autostart.Set(false, _key));
        Assert.False(Autostart.IsEnabled(_key));
        Assert.True(Autostart.Set(false, _key)); // removing twice is fine
    }

    public void Dispose() =>
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\BattaryChecker.Tests", throwOnMissingSubKey: false);
}

public sealed class CrashLogTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"bc-crash-{Guid.NewGuid():N}");

    [Fact]
    public void Write_AppendsAndStartsANewFilePastTheLimit()
    {
        var path = Path.Combine(_dir, "crash.log");
        CrashLog.Write(new InvalidOperationException("first"), path, new DateTime(2026, 11, 19, 14, 0, 0));
        CrashLog.Write(new InvalidOperationException("second"), path, new DateTime(2026, 11, 19, 14, 1, 0));
        var text = File.ReadAllText(path);
        Assert.Contains("2026-11-19 14:00:00 Battery Checker", text);
        Assert.Contains("first", text);
        Assert.Contains("second", text);

        File.AppendAllText(path, new string('x', (int)CrashLog.MaxBytes));
        CrashLog.Write(new InvalidOperationException("third"), path, DateTime.Now);
        Assert.Contains("first", File.ReadAllText(Path.Combine(_dir, "crash.old.log")));
        Assert.DoesNotContain("first", File.ReadAllText(path));
        Assert.Contains("third", File.ReadAllText(path));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}

public sealed class TaskbarDockTests
{
    static readonly (int, int, int, int) Monitor = (0, 0, 2048, 1152);

    [Theory]
    [InlineData(0, 0, 2048, 1152, true)]     // borderless game
    [InlineData(1, 0, 2047, 1151, true)]     // DPI-virtualised rect, a pixel short
    [InlineData(2, 2, 2046, 1150, true)]     // exactly at the slack
    [InlineData(3, 0, 2048, 1152, false)]    // one past the slack
    [InlineData(-8, -8, 2056, 1120, false)]  // maximised window: stops at the taskbar
    [InlineData(2048, 0, 4096, 1152, false)] // full screen on the other monitor
    public void Covers_FullMonitorOnly(int l, int t, int r, int b, bool expected)
    {
        Assert.Equal(expected, TaskbarDock.Covers((l, t, r, b), Monitor));
    }
}
