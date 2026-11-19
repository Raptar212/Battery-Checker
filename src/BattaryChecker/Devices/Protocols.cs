namespace BattaryChecker.Devices;

/// <summary>Battery level and charging flag decoded from a device reply.</summary>
public readonly record struct BatteryLevel(int Percent, bool Charging);

/// <summary>
/// Logitech HID++ 2.0 over a receiver. Short report 0x10 (7 bytes) out; replies come as short
/// 0x10 or long 0x11 (20 bytes). Verified on G602 via receiver 046D:C537 (research/probes).
/// </summary>
public static class Hidpp
{
    public const byte ShortReport = 0x10;
    /// <summary>Our software ids (G HUB uses others). Callers rotate through them per request:
    /// a reply does not echo e.g. the feature id asked for, so a late reply to an earlier
    /// request would otherwise match the next one with the same header.</summary>
    public const byte SoftwareId = 0x0A, LastSoftwareId = 0x0F;
    public const ushort BatteryStatusFeature = 0x1000;
    public const ushort UnifiedBatteryFeature = 0x1004;
    public const ushort DeviceNameFeature = 0x0005; // fn 0 name length, fn 1 name chunk, fn 2 type
    const byte Error10 = 0x8F, Error20 = 0xFF;

    public enum Match { None, Reply, Error }

    public static byte[] ShortRequest(byte device, byte feature, byte function, ReadOnlySpan<byte> args = default,
        byte softwareId = SoftwareId)
    {
        var msg = new byte[7];
        msg[0] = ShortReport;
        msg[1] = device;
        msg[2] = feature;
        msg[3] = (byte)((function << 4) | (softwareId & 0x0F));
        args[..Math.Min(args.Length, 3)].CopyTo(msg.AsSpan(4));
        return msg;
    }

    /// <summary>Root (index 0) fn 0: which feature index implements <paramref name="featureId"/>.</summary>
    public static byte[] GetFeatureRequest(byte device, ushort featureId, byte softwareId = SoftwareId) =>
        ShortRequest(device, 0, 0, [(byte)(featureId >> 8), (byte)featureId], softwareId);

    /// <summary>Next id in <see cref="SoftwareId"/>..<see cref="LastSoftwareId"/>, wrapping.</summary>
    public static byte NextSoftwareId(byte current) =>
        current is >= SoftwareId and < LastSoftwareId ? (byte)(current + 1) : SoftwareId;

    /// <summary>Is <paramref name="reply"/> the answer (or an error) to <paramref name="request"/>?</summary>
    public static Match Classify(ReadOnlySpan<byte> request, ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 7 || reply[1] != request[1])
            return Match.None;
        if (reply[2] == request[2] && reply[3] == request[3])
            return Match.Reply;
        if ((reply[2] == Error10 || reply[2] == Error20) && reply[3] == request[2] && reply[4] == request[3])
            return Match.Error;
        return Match.None;
    }

    /// <summary>Feature index from a root.getFeature reply; 0 = not supported.</summary>
    public static byte FeatureIndex(ReadOnlySpan<byte> reply) => reply[4];

    /// <summary>BATTERY_STATUS (0x1000) fn 0: level%, next level%, status (0 discharging, 1-4 charging/full).</summary>
    public static BatteryLevel ParseBatteryStatus(ReadOnlySpan<byte> reply) =>
        new(reply[4], reply[6] is >= 1 and <= 4);

    /// <summary>UNIFIED_BATTERY (0x1004) fn 1: soc%, level mask, charging status (1-2 charging, 3 full).</summary>
    public static BatteryLevel ParseUnifiedBattery(ReadOnlySpan<byte> reply) =>
        new(reply[4], reply[6] is 1 or 2);

    /// <summary>DEVICE_NAME fn 1: up to <paramref name="remaining"/> ASCII chars from byte 4.</summary>
    public static string NameChunk(ReadOnlySpan<byte> reply, int remaining)
    {
        var chars = reply[4..][..Math.Min(remaining, reply.Length - 4)];
        var end = chars.IndexOf((byte)0);
        return System.Text.Encoding.ASCII.GetString(end < 0 ? chars : chars[..end]);
    }

    /// <summary>DEVICE_NAME fn 2 type code to our kind.</summary>
    public static DeviceKind DeviceType(byte code) => code switch
    {
        0 or 2 => DeviceKind.Keyboard,
        3 or 4 or 5 => DeviceKind.Mouse,
        8 => DeviceKind.Headset,
        11 or 12 => DeviceKind.Gamepad,
        _ => DeviceKind.Other,
    };
}

/// <summary>
/// Astro A50 Gen4 base station 9886:002C, vendor HID usage page 0xFF32 (protocol per tdryer/eh-fifty).
/// Request [0x02, cmd] padded to 64 bytes; reply [0x02, status, len, data...], status 2 = ok.
/// </summary>
public static class AstroA50
{
    public const byte GetHeadsetStatus = 0x54;
    public const byte GetBatteryStatus = 0x7C;
    const byte ReportId = 0x02, StatusOk = 2;

    public static byte[] Request(byte command)
    {
        var msg = new byte[64];
        msg[0] = ReportId;
        msg[1] = command;
        return msg;
    }

    /// <summary>Payload of an ok reply, or null (not ours, error, or headset out of range).</summary>
    public static byte[]? Data(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 4 || reply[0] != ReportId || reply[1] != StatusOk)
            return null;
        var len = Math.Min(reply[2], reply.Length - 3);
        return len > 0 ? reply.Slice(3, len).ToArray() : null;
    }

    /// <summary>0x7C: bit 7 = charging, bits 0-6 = percent. The base repeats the last known
    /// value when the headset is gone, so check <see cref="IsHeadsetOn"/> too.</summary>
    public static BatteryLevel ParseBattery(byte data0) => new(data0 & 0x7F, (data0 & 0x80) != 0);

    /// <summary>0x54: bit 0 = docked, bit 1 = powered on.</summary>
    public static bool IsHeadsetOn(byte data0) => (data0 & 0x02) != 0;

    public static bool IsDocked(byte data0) => (data0 & 0x01) != 0;
}

/// <summary>
/// DualSense 054C:0CE6 input reports (layout per Linux hid-playstation). Status byte: low nibble
/// level 0-10, high nibble 0 discharging / 1 charging / 2 full.
/// </summary>
public static class DualSense
{
    const byte BluetoothReport = 0x31, UsbReport = 0x01;
    const int UsbLength = 64;

    /// <summary>
    /// The USB report is exactly 64 bytes. Over Bluetooth Windows pads the short 0x01 report to
    /// the collection's 78 bytes, so "at least 64" would misread its zeros as 5 % (seen on hardware).
    /// </summary>
    public static bool IsUsb(ReadOnlySpan<byte> report) => report.Length == UsbLength && report[0] == UsbReport;

    /// <summary>Battery from an input report, or null for reports without it (short BT 0x01).</summary>
    public static BatteryLevel? Parse(ReadOnlySpan<byte> report)
    {
        byte status;
        if (report.Length > 54 && report[0] == BluetoothReport)
            status = report[54];
        else if (IsUsb(report))
            status = report[53];
        else
            return null;
        var state = status >> 4;
        var percent = state == 2 ? 100 : Math.Min((status & 0x0F) * 10 + 5, 100);
        return new(percent, state == 1);
    }
}
