namespace BattaryChecker.Devices;

/// <summary>Astro A50 Gen4 via its USB base station. Sends only the two read queries.</summary>
public sealed class AstroA50Source : IBatterySource, IDisposable
{
    const ushort Vid = 0x9886, Pid = 0x002C, UsagePage = 0xFF32;
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    HidChannel? _channel;
    BatteryReading? _last;

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        _channel ??= await HidChannel.OpenAsync(Vid, Pid, UsagePage);
        if (_channel is null)
            return []; // base not plugged in

        try
        {
            var status = await QueryAsync(_channel, AstroA50.GetHeadsetStatus, ct);
            var on = status is { Length: > 0 } && AstroA50.IsHeadsetOn(status[0]);
            // The base repeats the last level while the headset is off: keep ours instead.
            var battery = on ? await QueryAsync(_channel, AstroA50.GetBatteryStatus, ct) : null;
            var level = battery is { Length: > 0 } ? AstroA50.ParseBattery(battery[0]) : (BatteryLevel?)null;
            _last = new BatteryReading("astro:9886-002C", "Astro A50", DeviceKind.Headset, "Base",
                Connected: on && level is not null,
                Percent: level?.Percent ?? _last?.Percent,
                Charging: level?.Charging ?? false,
                UpdatedAt: level is null ? _last?.UpdatedAt ?? DateTime.Now : DateTime.Now);
            return [_last];
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _channel.Dispose(); // base unplugged mid-read: reopen next time
            _channel = null;
            return _last is null ? [] : [_last with { Connected = false }];
        }
    }

    static async Task<byte[]?> QueryAsync(HidChannel channel, byte command, CancellationToken ct)
    {
        channel.DiscardPending();
        await channel.SendAsync(AstroA50.Request(command));
        var reply = await channel.ReceiveAsync(r => r.Length > 1 && r[0] == 0x02, Timeout, ct);
        return reply is null ? null : AstroA50.Data(reply);
    }

    public void Dispose() => _channel?.Dispose();
}
