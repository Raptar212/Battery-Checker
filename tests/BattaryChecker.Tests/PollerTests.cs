using BattaryChecker.Devices;

namespace BattaryChecker.Tests;

public class PollerTests
{
    sealed class Source(Func<IReadOnlyList<BatteryReading>> read) : IBatterySource
    {
        public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct) => Task.FromResult(read());
    }

    [Fact]
    public async Task PollAsync_StrayCancellationFromOneSourceDoesNotStopPolling()
    {
        var ok = new BatteryReading("a", "A", DeviceKind.Mouse, "USB", Connected: true, Percent: 50,
            Charging: false, UpdatedAt: DateTime.Now);
        using var poller = new DevicePoller(
            [new Source(() => throw new OperationCanceledException()), new Source(() => [ok])],
            TimeSpan.FromMinutes(1));

        await poller.PollAsync(); // must not throw: an escaping OCE would end the poll loop for good

        Assert.Equal([ok], poller.Current);
    }

    static BatteryReading Reading(string id) => new(id, id, DeviceKind.Mouse, "USB", Connected: true, Percent: 50,
        Charging: false, UpdatedAt: DateTime.Now);

    /// <summary>Answers the first call, then never answers again until released; counts calls.</summary>
    sealed class Hanging(BatteryReading first) : IBatterySource
    {
        public readonly TaskCompletionSource<IReadOnlyList<BatteryReading>> Release = new();
        public int Calls;

        public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct) =>
            Interlocked.Increment(ref Calls) == 1 ? Task.FromResult<IReadOnlyList<BatteryReading>>([first]) : Release.Task;
    }

    [Fact]
    public async Task PollAsync_HungSourceKeepsLastReadingAndIsNotCalledAgainUntilDone()
    {
        var hung = new Hanging(Reading("h"));
        var ok = Reading("ok");
        using var poller = new DevicePoller([hung, new Source(() => [ok])], TimeSpan.FromMinutes(1),
            sourceTimeout: TimeSpan.FromMilliseconds(100));

        await poller.PollAsync(); // first answer
        await poller.PollAsync(); // hangs: times out, last reading stands
        Assert.Equal(["h", "ok"], poller.Current.Select(r => r.Id));
        await poller.PollAsync(); // still hung: not called a third time
        Assert.Equal(2, hung.Calls);
        Assert.Equal(["h", "ok"], poller.Current.Select(r => r.Id));

        hung.Release.SetResult([Reading("h2")]);
        await poller.PollAsync(); // answered late in the background: that answer is used
        Assert.Equal(2, hung.Calls);
        Assert.Equal(["h2", "ok"], poller.Current.Select(r => r.Id));
        await poller.PollAsync(); // back to normal: asked again
        Assert.Equal(3, hung.Calls);
    }

    sealed class Blocking : IBatterySource
    {
        public readonly TaskCompletionSource Release = new();
        public int Calls;

        public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Release.Task;
            return [];
        }
    }

    [Fact]
    public async Task PollAsync_ExtraRequestsWhileOneWaitsAreDropped()
    {
        var source = new Blocking();
        using var poller = new DevicePoller([source], TimeSpan.FromMinutes(1));

        var running = poller.PollAsync();
        var waiting = poller.PollAsync();
        var extra = Enumerable.Range(0, 5).Select(_ => poller.PollAsync()).ToArray();
        await Task.WhenAll(extra); // dropped at once, while the first still runs

        source.Release.SetResult();
        await Task.WhenAll(running, waiting);
        Assert.Equal(2, source.Calls);
    }
}
