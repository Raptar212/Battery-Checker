#:property TargetFramework=net10.0-windows10.0.19041.0
#:property PublishAot=false
// Probe: Bluetooth LE battery (keyboards, mice) via WinRT. Read-only.
// Run: dotnet run research/probes/ble.cs
// Checks two places: the PnP property Windows also uses for Classic ({104EA319-...} 2) on BTHLE
// nodes, and the GATT Battery Service 0x180F / Battery Level 0x2A19 (cached first, then device).
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
const string InstanceKey = "System.Devices.DeviceInstanceId";

var nodes = await DeviceInformation.FindAllAsync(
    "System.Devices.DeviceInstanceId:~<\"BTHLE\"", new List<string> { BatteryKey, InstanceKey }, DeviceInformationKind.Device);
foreach (var n in nodes)
    Console.WriteLine($"node '{n.Name}' battery={n.Properties.GetValueOrDefault(BatteryKey) ?? "-"}  {n.Properties[InstanceKey]}");
Console.WriteLine($"scanned {nodes.Count} BTHLE nodes\n");

var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
foreach (var info in paired)
{
    using var dev = await BluetoothLEDevice.FromIdAsync(info.Id);
    if (dev is null) { Console.WriteLine($"{info.Name}: cannot open"); continue; }
    Console.Write($"{dev.Name}  addr={dev.BluetoothAddress:X12}  status={dev.ConnectionStatus}  appearance={dev.Appearance.Category}/{dev.Appearance.SubCategory}");
    foreach (var mode in new[] { BluetoothCacheMode.Cached, BluetoothCacheMode.Uncached })
    {
        var services = await dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, mode);
        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
        {
            Console.Write($"  {mode}: service {services.Status}");
            continue;
        }
        using var service = services.Services[0];
        var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, mode);
        if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
        {
            Console.Write($"  {mode}: characteristic {chars.Status}");
            continue;
        }
        var read = await chars.Characteristics[0].ReadValueAsync(mode);
        var level = read.Status == GattCommunicationStatus.Success ? DataReader.FromBuffer(read.Value).ReadByte().ToString() : "-";
        Console.Write($"  {mode}: {read.Status} {level}%");
    }
    Console.WriteLine();
}
Console.WriteLine($"paired BLE devices: {paired.Count}");
