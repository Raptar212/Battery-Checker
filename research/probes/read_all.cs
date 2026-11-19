#:property TargetFramework=net10.0-windows10.0.19041.0
#:property PublishAot=false
#:project ../../src/BattaryChecker/BattaryChecker.csproj
// Probe: run every real battery source once and print what it reads.
// Run: dotnet run research/probes/read_all.cs
using BattaryChecker.Devices;

List<IBatterySource> sources = [new BluetoothSource(), new AstroA50Source(), new DualSenseSource()];
sources.AddRange(LogitechSource.ReceiverPids.Select(pid => new LogitechSource(pid)));

Console.WriteLine($"start {DateTime.Now:HH:mm:ss}");
foreach (var source in sources)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var readings = await source.ReadAsync(CancellationToken.None);
    foreach (var r in readings)
        Console.WriteLine($"  {r.Name,-24} {r.Kind,-8} {r.Channel,-9} connected={r.Connected,-5} {r.Percent?.ToString() ?? "-",3}% charging={r.Charging}  [{source.GetType().Name}, {sw.ElapsedMilliseconds} ms]");
    (source as IDisposable)?.Dispose();
}
Console.WriteLine($"end   {DateTime.Now:HH:mm:ss}");
