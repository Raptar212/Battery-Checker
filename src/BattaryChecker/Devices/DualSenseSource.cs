namespace BattaryChecker.Devices;

/// <summary>
/// DualSense: listens to the input reports the pad already sends. Over Bluetooth the pad sends
/// short reports without battery until some app (Steam, etc.) switches it to full 0x31 reports;
/// until then it shows as connected with an unknown level — unless the user opted in
/// (<see cref="AppConfig.DualSenseFullReports"/>), then this source switches it itself.
/// The channel is open only while listening: the pad sends hundreds of reports a second, and a
/// channel left open would copy every one of them between polls.
/// </summary>
public sealed class DualSenseSource(Func<bool>? fullReports = null) : IBatterySource
{
    const ushort Vid = 0x054C, Pid = 0x0CE6, GenericDesktop = 0x01, Gamepad = 0x05;
    // Reading this feature report (calibration) is what switches the pad to full 0x31 reports
    // over Bluetooth — the same step Linux hid-playstation and Steam take. It stays switched
    // until the pad reconnects, so it is asked only while no battery report arrives.
    const byte CalibrationFeature = 0x05;
    // Reports arrive every few ms while the pad is on; this only bounds a silent pad.
    static readonly TimeSpan Listen = TimeSpan.FromSeconds(1);

    BatteryReading? _last;

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        using var channel = await HidChannel.OpenAsync(Vid, Pid, GenericDesktop, Gamepad);
        if (channel is null) // interface gone: the pad is off or unpaired, no grace
            return _last is null ? [] : [_last = _last with { Connected = false }];

        byte[]? any = null;
        bool Accept(byte[] r)
        {
            any ??= r;
            return DualSense.Parse(r) is not null;
        }
        var report = await channel.ReceiveAsync(Accept, Listen, ct);

        // Bluetooth short reports only, and the user opted in: switch the pad, listen again.
        if (report is null && any is not null && !DualSense.IsUsb(any) && fullReports?.Invoke() == true
            && await channel.RequestFeatureAsync(CalibrationFeature, ct))
            report = await channel.ReceiveAsync(Accept, Listen, ct);

        if (any is null) // silent: asleep or gone
            return (_last = Presence.Silent(_last, DateTime.Now)) is { } last ? [last] : [];

        var level = report is null ? null : DualSense.Parse(report);
        _last = new BatteryReading($"sony:{Vid:X4}-{Pid:X4}", "DualSense", DeviceKind.Gamepad,
            DualSense.IsUsb(report ?? any) ? "USB" : "Bluetooth",
            Connected: true,
            Percent: level?.Percent,
            Charging: level?.Charging ?? false,
            UpdatedAt: DateTime.Now);
        return [_last];
    }
}
