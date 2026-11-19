using BattaryChecker.Devices;

namespace BattaryChecker.Tests;

public class HidppTests
{
    [Fact]
    public void ShortRequest_PacksFunctionAndSoftwareId()
    {
        var req = Hidpp.ShortRequest(device: 0x01, feature: 0x05, function: 0);
        Assert.Equal(new byte[] { 0x10, 0x01, 0x05, 0x0A, 0, 0, 0 }, req);
    }

    [Fact]
    public void GetFeatureRequest_PutsFeatureIdBigEndian()
    {
        var req = Hidpp.GetFeatureRequest(0x01, Hidpp.BatteryStatusFeature);
        Assert.Equal(new byte[] { 0x10, 0x01, 0x00, 0x0A, 0x10, 0x00, 0 }, req);
    }

    [Fact]
    public void Classify_MatchesLongReplyToShortRequest()
    {
        var req = Hidpp.ShortRequest(0x01, 0x05, 0);
        var reply = new byte[20];
        new byte[] { 0x11, 0x01, 0x05, 0x0A, 50, 20, 0 }.CopyTo(reply, 0);
        Assert.Equal(Hidpp.Match.Reply, Hidpp.Classify(req, reply));
    }

    [Fact]
    public void Classify_LateReplyToEarlierRequestDoesNotMatch()
    {
        // getFeature replies do not echo the feature id: only the rotating SWID tells them apart.
        var first = Hidpp.GetFeatureRequest(0x01, Hidpp.UnifiedBatteryFeature, 0x0A);
        var second = Hidpp.GetFeatureRequest(0x01, Hidpp.BatteryStatusFeature, Hidpp.NextSoftwareId(0x0A));
        var lateReplyToFirst = new byte[] { 0x11, 0x01, 0x00, 0x0A, 0x00, 0, 0 };
        Assert.Equal(Hidpp.Match.Reply, Hidpp.Classify(first, lateReplyToFirst));
        Assert.Equal(Hidpp.Match.None, Hidpp.Classify(second, lateReplyToFirst));
    }

    [Theory]
    [InlineData((byte)0x0A, (byte)0x0B)]
    [InlineData((byte)0x0F, (byte)0x0A)] // wraps
    [InlineData((byte)0x01, (byte)0x0A)] // never strays into G HUB's ids
    public void NextSoftwareId_StaysInOurRange(byte current, byte next)
    {
        Assert.Equal(next, Hidpp.NextSoftwareId(current));
    }

    [Fact]
    public void Classify_IgnoresOtherSoftwareId()
    {
        var req = Hidpp.ShortRequest(0x01, 0x05, 0);
        var ghub = new byte[] { 0x11, 0x01, 0x05, 0x01, 50, 20, 0 }; // same call, G HUB's SWID
        Assert.Equal(Hidpp.Match.None, Hidpp.Classify(req, ghub));
    }

    [Theory]
    [InlineData((byte)0x8F)] // HID++ 1.0: empty receiver slot
    [InlineData((byte)0xFF)] // HID++ 2.0
    public void Classify_RecognisesErrors(byte errorTag)
    {
        var req = Hidpp.ShortRequest(0x02, 0x00, 1);
        var reply = new byte[] { 0x10, 0x02, errorTag, 0x00, 0x1A, 0x09, 0 };
        Assert.Equal(Hidpp.Match.Error, Hidpp.Classify(req, reply));
    }

    [Fact]
    public void ParseBatteryStatus_G602Discharging()
    {
        var reply = new byte[] { 0x11, 0x01, 0x05, 0x0A, 50, 20, 0 }; // probe: 50 %, discharging
        Assert.Equal(new BatteryLevel(50, false), Hidpp.ParseBatteryStatus(reply));
    }

    [Theory]
    [InlineData((byte)1, true)]
    [InlineData((byte)3, true)] // charged, still on power
    [InlineData((byte)5, false)] // invalid battery
    public void ParseBatteryStatus_ChargingStates(byte status, bool charging)
    {
        var reply = new byte[] { 0x11, 0x01, 0x05, 0x0A, 100, 0, status };
        Assert.Equal(charging, Hidpp.ParseBatteryStatus(reply).Charging);
    }

    [Fact]
    public void ParseUnifiedBattery_ReadsSocAndCharging()
    {
        var reply = new byte[] { 0x11, 0x01, 0x06, 0x1A, 73, 0x04, 1 };
        Assert.Equal(new BatteryLevel(73, true), Hidpp.ParseUnifiedBattery(reply));
    }

    [Fact]
    public void NameChunk_StopsAtNulAndRemaining()
    {
        var reply = new byte[20];
        new byte[] { 0x11, 0x01, 0x03, 0x1A, (byte)'G', (byte)'6', (byte)'0', (byte)'2', 0 }.CopyTo(reply, 0);
        Assert.Equal("G602", Hidpp.NameChunk(reply, 16));
        Assert.Equal("G6", Hidpp.NameChunk(reply, 2));
    }

    [Fact]
    public void NameChunk_ShortReportDoesNotOverrun()
    {
        var reply = new byte[] { 0x10, 0x01, 0x03, 0x1A, (byte)'M', (byte)'X', (byte)'3' };
        Assert.Equal("MX3", Hidpp.NameChunk(reply, 16));
    }

    [Theory]
    [InlineData((byte)3, DeviceKind.Mouse)]
    [InlineData((byte)0, DeviceKind.Keyboard)]
    [InlineData((byte)8, DeviceKind.Headset)]
    [InlineData((byte)42, DeviceKind.Other)]
    public void DeviceType_Maps(byte code, DeviceKind kind)
    {
        Assert.Equal(kind, Hidpp.DeviceType(code));
    }
}

public class AstroA50Tests
{
    [Fact]
    public void Request_IsPaddedTo64Bytes()
    {
        var req = AstroA50.Request(AstroA50.GetBatteryStatus);
        Assert.Equal(64, req.Length);
        Assert.Equal(new byte[] { 0x02, 0x7C, 0 }, req[..3]);
    }

    [Fact]
    public void Battery_57PercentNotCharging()
    {
        var data = AstroA50.Data(new byte[] { 0x02, 0x02, 0x01, 57, 0, 0 }); // probe: 57 %
        Assert.NotNull(data);
        Assert.Equal(new BatteryLevel(57, false), AstroA50.ParseBattery(data[0]));
    }

    [Fact]
    public void Battery_ChargingBit()
    {
        Assert.Equal(new BatteryLevel(80, true), AstroA50.ParseBattery(0x80 | 80));
    }

    [Theory]
    [InlineData((byte)0)] // no response
    [InlineData((byte)1)] // error, e.g. HID_ERROR_SLAVE_NO_SLAVE
    public void Data_NullUnlessOk(byte status)
    {
        Assert.Null(AstroA50.Data(new byte[] { 0x02, status, 0x01, 57 }));
    }

    [Fact]
    public void Data_ClampsLengthToReport()
    {
        Assert.Equal(new byte[] { 9 }, AstroA50.Data(new byte[] { 0x02, 0x02, 0x10, 9 }));
    }

    [Fact]
    public void HeadsetStatus_Bits()
    {
        Assert.True(AstroA50.IsHeadsetOn(0x02));
        Assert.False(AstroA50.IsDocked(0x02));
        Assert.True(AstroA50.IsDocked(0x03));
        Assert.False(AstroA50.IsHeadsetOn(0x01));
    }
}

public class BluetoothAddressTests
{
    [Theory]
    [InlineData(@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&000105D6_PID&000A\7&1A2B3C4D&0&0123456789AB_C00000000")]
    [InlineData(@"BTHENUM\DEV_0123456789AB\7&1A2B3C4D&0&BLUETOOTHDEVICE_0123456789AB")]
    public void AddressOf_FindsAddress(string instanceId)
    {
        Assert.Equal(0x0123456789ABUL, BluetoothSource.AddressOf(instanceId));
    }

    [Fact]
    public void AddressOf_LowEnergyNode()
    {
        // a BLE keyboard: the BTHLE node that carries the battery property
        Assert.Equal(0xAABBCCDDEEFFUL, BluetoothSource.AddressOf(@"BTHLE\Dev_aabbccddeeff\7&5e6f7a8b&0&aabbccddeeff"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"BTHHFENUM\BTHHFPAUDIO\8&1A2B3C4&0&97")]
    public void AddressOf_NullWithoutAddress(string? instanceId)
    {
        Assert.Null(BluetoothSource.AddressOf(instanceId));
    }

    [Theory]
    [InlineData(0x240404u, DeviceKind.Earbuds)]  // audio/video, headset
    [InlineData(0x002540u, DeviceKind.Keyboard)] // peripheral, keyboard
    [InlineData(0x002580u, DeviceKind.Mouse)]    // peripheral, pointing
    [InlineData(0x0025C0u, DeviceKind.Mouse)]    // peripheral, combo keyboard + pointing
    [InlineData(0x5A020Cu, DeviceKind.Other)]    // phone
    public void ClassicKind_FromClassOfDevice(uint classOfDevice, DeviceKind expected)
    {
        Assert.Equal(expected, BluetoothSource.ClassicKind(classOfDevice));
    }

    [Theory]
    [InlineData(0x0F, 1, DeviceKind.Keyboard)] // probe: Keychron Q6 Max reports 15/1
    [InlineData(0x0F, 2, DeviceKind.Mouse)]
    [InlineData(0x0F, 4, DeviceKind.Gamepad)]
    [InlineData(0x0F, 0, DeviceKind.Other)]
    [InlineData(0x00, 0, DeviceKind.Other)]
    public void LowEnergyKind_FromAppearance(ushort category, ushort subCategory, DeviceKind expected)
    {
        Assert.Equal(expected, BluetoothSource.LowEnergyKind(category, subCategory));
    }
}

public class DualSenseTests
{
    static byte[] Report(byte id, int length, int statusAt, byte status)
    {
        var r = new byte[length];
        r[0] = id;
        r[statusAt] = status;
        return r;
    }

    [Fact]
    public void Bluetooth0x31_LowAndCharging()
    {
        var r = Report(0x31, 78, 54, 0x10); // probe: <10 %, charging
        Assert.Equal(new BatteryLevel(5, true), DualSense.Parse(r));
    }

    [Fact]
    public void Usb0x01_LevelCappedAt100()
    {
        var r = Report(0x01, 64, 53, 0x0A);
        Assert.Equal(new BatteryLevel(100, false), DualSense.Parse(r));
    }

    [Fact]
    public void Full_Is100NotCharging()
    {
        var r = Report(0x31, 78, 54, 0x28);
        Assert.Equal(new BatteryLevel(100, false), DualSense.Parse(r));
    }

    [Fact]
    public void ShortBluetoothReport_HasNoBattery()
    {
        Assert.Null(DualSense.Parse(Report(0x01, 10, 5, 0)));
    }

    [Fact]
    public void ShortBluetoothReportPaddedByWindows_HasNoBatteryAndIsNotUsb()
    {
        var r = Report(0x01, 78, 53, 0); // what ReadFile returns over BT (hardware, 2026-11-16)
        Assert.Null(DualSense.Parse(r));
        Assert.False(DualSense.IsUsb(r));
        Assert.True(DualSense.IsUsb(Report(0x01, 64, 53, 0)));
    }
}
