namespace BattaryChecker.Devices;

public enum DeviceKind { Mouse, Headset, Earbuds, Gamepad, Keyboard, Other }

/// <summary>One device's battery state as last read from its source.</summary>
/// <param name="Id">Stable key across restarts (used for hiding and names in config).</param>
/// <param name="Channel">How it is connected, shown in the panel: "Bluetooth", "2.4 GHz", "Base".</param>
/// <param name="Percent">Null when the device never reported a level.</param>
public sealed record BatteryReading(
    string Id,
    string Name,
    DeviceKind Kind,
    string Channel,
    bool Connected,
    int? Percent,
    bool Charging,
    DateTime UpdatedAt);

/// <summary>
/// Wireless HID devices (Logitech mice, DualSense) stop answering when they doze off, which looks
/// the same as being switched off. Keep such a device connected with its last level until it has
/// been silent for <see cref="SleepGrace"/>.
/// </summary>
public static class Presence
{
    public static readonly TimeSpan SleepGrace = TimeSpan.FromMinutes(5);

    /// <summary>What to show for a device that did not answer this poll.</summary>
    public static BatteryReading? Silent(BatteryReading? last, DateTime now) =>
        last is null ? null
        : last.Connected && now - last.UpdatedAt < SleepGrace ? last
        : last with { Connected = false };
}

public interface IBatterySource
{
    Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct);
}
