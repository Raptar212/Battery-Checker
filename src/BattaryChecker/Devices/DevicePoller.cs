using System.Diagnostics;

namespace BattaryChecker.Devices;

/// <summary>Polls every source on a fixed interval; one failing or hung source never hides the others.</summary>
public sealed class DevicePoller(IReadOnlyList<IBatterySource> sources, TimeSpan interval, TimeSpan? sourceTimeout = null)
    : IDisposable
{
    /// <summary>A WinRT or HID call that has not answered by then is not going to.</summary>
    /// <remarks>Logitech's worst case — six sleeping slots never seen before, ~3 s each — stays inside it.</remarks>
    public static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(30);

    readonly TimeSpan _timeout = sourceTimeout ?? DefaultSourceTimeout;
    readonly CancellationTokenSource _stop = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    // A source that timed out keeps running in the background; it is not called again until it
    // finishes (sources hold channels in fields and are not re-entrant). Meanwhile its last readings stand.
    readonly Dictionary<IBatterySource, Task<IReadOnlyList<BatteryReading>>> _hung = [];
    readonly Dictionary<IBatterySource, IReadOnlyList<BatteryReading>> _last = [];
    int _queued; // polls running or waiting for the gate: at most one waits, the rest would repeat it

    /// <summary>Raised on a thread-pool thread after each poll with all readings.</summary>
    public event Action<IReadOnlyList<BatteryReading>>? Updated;

    public IReadOnlyList<BatteryReading> Current { get; private set; } = [];

    public void Start() => _ = Task.Run(LoopAsync);

    async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
                await PollAsync();
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Reads all sources now (also used by the panel's refresh button). A call while one poll runs and
    /// another already waits returns at once: the waiting one will read everything anyway.
    /// </summary>
    public async Task PollAsync()
    {
        if (Interlocked.Increment(ref _queued) > 2)
        {
            Interlocked.Decrement(ref _queued);
            return;
        }
        try
        {
            await _gate.WaitAsync(_stop.Token);
        }
        catch
        {
            Interlocked.Decrement(ref _queued);
            throw;
        }
        try
        {
            var all = new List<BatteryReading>();
            foreach (var source in sources)
                all.AddRange(await ReadAsync(source));
            Current = all;
            Updated?.Invoke(all);
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
            _gate.Release();
        }
    }

    async Task<IReadOnlyList<BatteryReading>> ReadAsync(IBatterySource source)
    {
        if (_hung.TryGetValue(source, out var hung))
        {
            if (!hung.IsCompleted)
                return _last.GetValueOrDefault(source, []);
            _hung.Remove(source);
            if (hung.IsCompletedSuccessfully)
                return _last[source] = hung.Result; // it did answer, late: use that instead of asking again
        }
        var read = Task.Run(() => source.ReadAsync(_stop.Token)); // a source that blocks synchronously is caught too
        try
        {
            return _last[source] = await read.WaitAsync(_timeout, _stop.Token);
        }
        catch (TimeoutException) when (!read.IsCompleted) // our wait gave up, not the source's own timeout
        {
            _hung[source] = read;
            Trace.WriteLine($"{source.GetType().Name}: no answer in {_timeout.TotalSeconds:0} s");
            return _last.GetValueOrDefault(source, []);
        }
        // Any failure but our own shutdown — a stray cancellation from a WinRT call
        // included — must not escape, or the loop would end and polling stop for good.
        catch (Exception e) when (e is not OperationCanceledException || !_stop.IsCancellationRequested)
        {
            Trace.WriteLine($"{source.GetType().Name}: {e}");
            _last.Remove(source);
            return [];
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var source in sources)
            (source as IDisposable)?.Dispose();
    }
}
