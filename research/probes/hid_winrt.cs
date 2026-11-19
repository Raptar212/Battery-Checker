#:property TargetFramework=net10.0-windows10.0.19041.0
#:property PublishAot=false
// Probe: can WinRT Windows.Devices.HumanInterfaceDevice open our devices' HID collections?
// Enumerates and opens read-only; sends nothing. Run: dotnet run research/probes/hid_winrt.cs
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;

const string HidClass = "{4D1E55B2-F16F-11CF-88CB-001111000030}";
(ushort vid, ushort pid, string name)[] targets =
    [(0x046D, 0xC537, "Logitech receiver"), (0x9886, 0x002C, "Astro A50 base"), (0x054C, 0x0CE6, "DualSense")];

foreach (var (vid, pid, name) in targets)
{
    var aqs = $"System.Devices.InterfaceClassGuid:=\"{HidClass}\" AND System.DeviceInterface.Hid.VendorId:={vid} AND System.DeviceInterface.Hid.ProductId:={pid} AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
    var props = new List<string> { "System.DeviceInterface.Hid.UsagePage", "System.DeviceInterface.Hid.UsageId" };
    var found = await DeviceInformation.FindAllAsync(aqs, props);
    Console.WriteLine($"{name} {vid:X4}:{pid:X4}: {found.Count} collections");
    foreach (var d in found)
    {
        var page = Convert.ToUInt16(d.Properties["System.DeviceInterface.Hid.UsagePage"]);
        var usage = Convert.ToUInt16(d.Properties["System.DeviceInterface.Hid.UsageId"]);
        string result;
        try
        {
            using var dev = await HidDevice.FromIdAsync(d.Id, FileAccessMode.Read);
            result = dev is null ? "null (blocked or in use)" : "opened";
            using var rw = await HidDevice.FromIdAsync(d.Id, FileAccessMode.ReadWrite);
            result += rw is null ? ", read-write: null" : ", read-write: opened";
        }
        catch (Exception e) { result = $"{e.GetType().Name}: {e.Message}"; }
        Console.WriteLine($"  page 0x{page:X4} usage 0x{usage:X4}: {result}");
    }
}
