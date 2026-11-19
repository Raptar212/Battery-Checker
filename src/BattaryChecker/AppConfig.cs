using System.IO;
using System.Text.Json;

namespace BattaryChecker;

public enum TrayStyle { Bars, Ring, Number }

/// <summary>User settings, stored as JSON in %LOCALAPPDATA%\BattaryChecker\config.json.</summary>
public sealed class AppConfig
{
    public double? WidgetLeft { get; set; }
    public double? WidgetTop { get; set; }
    public List<string> HiddenDevices { get; set; } = [];
    public int LowThreshold { get; set; } = 20;
    public int MediumThreshold { get; set; } = 50;
    public int CriticalThreshold { get; set; } = 5;
    public bool Notifications { get; set; } = true;

    /// <summary>Blink the tray icon while a selected device is at or below the critical level.</summary>
    public bool BlinkCritical { get; set; } = true;

    public TrayStyle TrayStyle { get; set; } = TrayStyle.Bars;

    /// <summary>Devices the tray icon shows; empty = every connected device.</summary>
    public List<string> TrayDevices { get; set; } = [];

    /// <summary>
    /// Over Bluetooth, switch DualSense to full reports (as Steam does) to get its battery level.
    /// Off by default: games that read the pad directly may lose it until it reconnects.
    /// </summary>
    public bool DualSenseFullReports { get; set; }

    /// <summary>Widget docked into the taskbar left of the tray instead of floating.</summary>
    public bool PinToTaskbar { get; set; }

    /// <summary>Ask GitHub for a newer release a minute after start; a manual check is always in the tray menu.</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>User-given names by device id; a missing or blank entry keeps the device's own name.</summary>
    public Dictionary<string, string> DeviceNames { get; set; } = [];

    public const int MinLow = 5, MaxLow = 50, MaxMedium = 95, MinGap = 5, MinCritical = 1;

    /// <summary>Keeps thresholds sane whatever the user typed: low in 5..50, medium at least 5 above.</summary>
    public static (int Low, int Medium) ClampThresholds(int low, int medium)
    {
        low = Math.Clamp(low, MinLow, MaxLow);
        return (low, Math.Clamp(medium, low + MinGap, MaxMedium));
    }

    /// <summary>Critical stays in 1..low-1, so it is always a stricter level than low.</summary>
    public static int ClampCritical(int critical, int low) =>
        Math.Clamp(critical, MinCritical, Math.Max(MinCritical, low - 1));

    /// <summary>
    /// A hand-edited file can be valid JSON yet hold nulls or out-of-range values; fix them on
    /// load so a bad number cannot crash every start (with autostart: every logon).
    /// </summary>
    void Normalize()
    {
        HiddenDevices ??= [];
        TrayDevices ??= [];
        DeviceNames ??= [];
        (LowThreshold, MediumThreshold) = ClampThresholds(LowThreshold, MediumThreshold);
        CriticalThreshold = ClampCritical(CriticalThreshold, LowThreshold);
        if (!Enum.IsDefined(TrayStyle))
            TrayStyle = TrayStyle.Bars;
    }

    /// <summary>The name to show for a device: the user's alias if set, else its own.</summary>
    public string NameOf(string id, string own) =>
        DeviceNames.TryGetValue(id, out var alias) && !string.IsNullOrWhiteSpace(alias) ? alias.Trim() : own;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// The file exists but could not be read or set aside (locked by an antivirus or a sync
    /// client): this run uses defaults and never saves, or it would overwrite the user's file.
    /// ponytail: changes made in such a run are lost on exit; retry the read if that ever bites.
    /// </summary>
    bool _unreadable;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BattaryChecker", "config.json");

    /// <summary>
    /// Missing file = defaults. An unreadable file is moved aside to config.json.bad (never
    /// silently overwritten — the user's settings stay recoverable) and defaults are used.
    /// </summary>
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            return new AppConfig();
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json) ?? new AppConfig();
            config.Normalize();
            return config;
        }
        catch (JsonException)
        {
            try
            {
                File.Move(path, path + ".bad", overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new AppConfig { _unreadable = true }; // cannot set it aside: keep it as it is
            }
            return new AppConfig();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new AppConfig { _unreadable = true };
        }
    }

    /// <summary>
    /// Writes via a temp file so a crash mid-write cannot leave a half file. A file locked at
    /// that moment (antivirus, sync client) is skipped, not fatal: every save writes the whole
    /// config, so the next change — or the save on exit — carries this one too.
    /// </summary>
    public void Save(string path)
    {
        if (_unreadable)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"config save skipped: {e.Message}");
        }
    }
}
