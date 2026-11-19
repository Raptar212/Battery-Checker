using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BattaryChecker.Devices;

/// <summary>
/// Bluetooth devices whose battery Windows already reads: Classic through the Hands-Free profile
/// (on a BTHENUM child node), LE through the Battery Service (on the BTHLE\Dev_ node). Both land in
/// {104EA319-...} 2 and keep the last value after disconnect, so the connection state comes
/// separately from <see cref="BluetoothDevice"/> / <see cref="BluetoothLEDevice"/>.
/// Read-only: never connects to a device.
/// </summary>
public sealed partial class BluetoothSource : IBatterySource
{
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    const string InstanceKey = "System.Devices.DeviceInstanceId";
    const string Filter = "System.Devices.DeviceInstanceId:~<\"BTHENUM\" OR System.Devices.DeviceInstanceId:~<\"BTHLE\"";

    // Last poll's readings: a device whose lookup fails this time (radio off, removed mid-poll)
    // keeps its card instead of vanishing. Only the poller calls this source, one poll at a time.
    Dictionary<ulong, BatteryReading> _last = [];

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        DeviceInformationCollection nodes;
        try
        {
            nodes = await DeviceInformation.FindAllAsync(
                Filter, new List<string> { BatteryKey, InstanceKey }, DeviceInformationKind.Device);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            System.Diagnostics.Trace.WriteLine($"Bluetooth enumeration: {e.Message}");
            return [.. _last.Values];
        }
        var result = new Dictionary<ulong, BatteryReading>();
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            if (node.Properties.TryGetValue(BatteryKey, out var value) is false || value is not byte level)
                continue;
            var instanceId = node.Properties.TryGetValue(InstanceKey, out var id) ? id as string : null;
            if (AddressOf(instanceId) is not { } address || result.ContainsKey(address))
                continue;
            try
            {
                var (name, kind, connected) = instanceId!.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase)
                    ? await LowEnergyAsync(address)
                    : await ClassicAsync(address);
                result[address] = new BatteryReading($"bt:{address:X12}", name ?? node.Name, kind, "Bluetooth",
                    connected, Math.Min((int)level, 100), Charging: false, DateTime.Now);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                System.Diagnostics.Trace.WriteLine($"Bluetooth {address:X12}: {e.Message}");
                if (_last.TryGetValue(address, out var last))
                    result[address] = last;
            }
        }
        _last = result;
        return [.. result.Values];
    }

    static async Task<(string?, DeviceKind, bool)> ClassicAsync(ulong address)
    {
        using var device = await BluetoothDevice.FromBluetoothAddressAsync(address);
        return (device?.Name is { Length: > 0 } n ? n : null, ClassicKind(device?.ClassOfDevice.RawValue ?? 0),
            device?.ConnectionStatus == BluetoothConnectionStatus.Connected);
    }

    static async Task<(string?, DeviceKind, bool)> LowEnergyAsync(ulong address)
    {
        using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        return (device?.Name is { Length: > 0 } n ? n : null,
            LowEnergyKind(device?.Appearance.Category ?? 0, device?.Appearance.SubCategory ?? 0),
            device?.ConnectionStatus == BluetoothConnectionStatus.Connected);
    }

    /// <summary>The 12-hex-digit device address inside a BTHENUM / BTHLE instance id, if any.</summary>
    public static ulong? AddressOf(string? instanceId)
    {
        var m = instanceId is null ? null : AddressRegex().Match(instanceId);
        return m is { Success: true } ? Convert.ToUInt64(m.Groups[1].Value, 16) : null;
    }

    /// <summary>Class of Device: major class in bits 8–12; for peripherals bit 6 = keyboard, bit 7 = pointing.</summary>
    public static DeviceKind ClassicKind(uint classOfDevice) => (classOfDevice >> 8 & 0x1F) switch
    {
        0x04 => DeviceKind.Earbuds,
        0x05 => (classOfDevice & 0xC0) == 0x40 ? DeviceKind.Keyboard : DeviceKind.Mouse,
        _ => DeviceKind.Other,
    };

    /// <summary>GAP Appearance: category 0x0F = HID, sub 1 keyboard, 2 mouse, 4 gamepad.</summary>
    public static DeviceKind LowEnergyKind(ushort category, ushort subCategory) => (category, subCategory) switch
    {
        (0x0F, 1) => DeviceKind.Keyboard,
        (0x0F, 2) => DeviceKind.Mouse,
        (0x0F, 4) => DeviceKind.Gamepad,
        _ => DeviceKind.Other,
    };

    [GeneratedRegex(@"[&_]([0-9A-F]{12})(?:_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AddressRegex();
}
