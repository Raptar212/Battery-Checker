#:property TargetFramework=net10.0-windows10.0.19041.0
#:property PublishAot=false
// Probe: Bluetooth Classic battery + real connection state via WinRT. Read-only.
// Run: dotnet run research/probes/bt_classic.cs
// Battery lives on a BTHENUM child node (e.g. "Hands-Free AG") as {104EA319-...} 2 and keeps the
// last known value after disconnect, so connection comes from BluetoothDevice.ConnectionStatus.
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
const string InstanceKey = "System.Devices.DeviceInstanceId";

var nodes = await DeviceInformation.FindAllAsync(
    "System.Devices.DeviceInstanceId:~<\"BTHENUM\"", new List<string> { BatteryKey, InstanceKey }, DeviceInformationKind.Device);

foreach (var n in nodes)
{
    if (n.Properties[BatteryKey] is not byte level) continue;
    var id = n.Properties[InstanceKey] as string ?? "";
    var m = Regex.Match(id, @"[&_]([0-9A-F]{12})(?:_|$)", RegexOptions.IgnoreCase);
    if (!m.Success) { Console.WriteLine($"{n.Name}: {level}% (no address in {id})"); continue; }
    var address = Convert.ToUInt64(m.Groups[1].Value, 16);
    using var dev = await BluetoothDevice.FromBluetoothAddressAsync(address);
    Console.WriteLine($"{dev?.Name ?? n.Name}: {level}%  node='{n.Name}'  status={dev?.ConnectionStatus}  addr={m.Groups[1].Value}");
}
Console.WriteLine($"scanned {nodes.Count} BTHENUM nodes");
