using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;

namespace BattaryChecker.Devices;

/// <summary>
/// One open HID top-level collection. Found via WinRT enumeration, opened with Win32 file I/O:
/// WinRT HidDevice refuses read-write access to desktop apps without package identity.
/// Input reports are queued as raw bytes with the report id first (same layout as the probes).
/// </summary>
public sealed class HidChannel : IDisposable
{
    const string HidClass = "{4D1E55B2-F16F-11CF-88CB-001111000030}";
    const string UsagePageKey = "System.DeviceInterface.Hid.UsagePage";
    const string UsageIdKey = "System.DeviceInterface.Hid.UsageId";

    // Two handles, two streams: one FileStream serialises a write behind its pending read, so a
    // request would wait until the device happened to send some other input report.
    // (RandomAccess would avoid that but rejects unseekable handles such as HID devices.)
    readonly FileStream _reader;
    readonly FileStream _writer;
    readonly int _outputLength;
    readonly CancellationTokenSource _stop = new();
    readonly Channel<byte[]> _input = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
    static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);
    volatile bool _closed; // the read loop ended: device gone or disposed
    int _disposed;
    readonly int _featureLength;

    HidChannel(SafeFileHandle readHandle, SafeFileHandle writeHandle, int inputLength, int outputLength,
        int featureLength)
    {
        _featureLength = featureLength;
        _reader = new FileStream(readHandle, FileAccess.Read, bufferSize: 0, isAsync: true);
        _writer = new FileStream(writeHandle, FileAccess.Write, bufferSize: 0, isAsync: true);
        _outputLength = outputLength;
        _ = Task.Run(() => ReadLoopAsync(inputLength));
    }

    /// <summary>Opens the present collection with this vid/pid/usage page (and usage, if given).</summary>
    public static async Task<HidChannel?> OpenAsync(ushort vid, ushort pid, ushort usagePage, ushort? usage = null)
    {
        var aqs = $"System.Devices.InterfaceClassGuid:=\"{HidClass}\" AND System.DeviceInterface.Hid.VendorId:={vid}" +
                  $" AND System.DeviceInterface.Hid.ProductId:={pid}" +
                  " AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        var found = await DeviceInformation.FindAllAsync(aqs, new List<string> { UsagePageKey, UsageIdKey });
        foreach (var info in found)
        {
            if (Convert.ToUInt16(info.Properties[UsagePageKey]) != usagePage)
                continue;
            if (usage is { } u && Convert.ToUInt16(info.Properties[UsageIdKey]) != u)
                continue;
            if (TryOpen(info.Id) is { } channel)
                return channel;
        }
        return null;
    }

    static HidChannel? TryOpen(string path)
    {
        SafeFileHandle handle, writeHandle;
        try
        {
            handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.Asynchronous);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null; // stale interface, or a collection Windows keeps exclusive (mouse, keyboard)
        }
        try
        {
            writeHandle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, FileOptions.Asynchronous);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            handle.Dispose();
            return null;
        }

        var caps = new ushort[32]; // HIDP_CAPS: Usage, UsagePage, InputLen, OutputLen, FeatureLen, reserved...
        var usable = HidD_GetPreparsedData(handle, out var preparsed);
        if (usable)
        {
            usable = HidP_GetCaps(preparsed, caps) == HidpStatusSuccess && caps[2] > 0 && caps[3] > 0;
            HidD_FreePreparsedData(preparsed);
        }
        if (!usable)
        {
            handle.Dispose(); // no caps, or no input/output reports: useless to us
            writeHandle.Dispose();
            return null;
        }
        return new HidChannel(handle, writeHandle, caps[2], caps[3], caps[4]);
    }

    public ChannelReader<byte[]> Input => _input.Reader;

    /// <summary>Sends an output report; <paramref name="report"/>[0] is the report id.
    /// Zero-padded (or cut) to the collection's output report length.
    /// Throws IOException once the device is gone or a write hangs, so callers reopen
    /// instead of reading timeouts from a dead channel forever.</summary>
    public async Task SendAsync(byte[] report)
    {
        if (_closed || Volatile.Read(ref _disposed) != 0)
            throw new IOException("HID device closed");
        var buffer = new byte[_outputLength];
        report.AsSpan(0, Math.Min(report.Length, buffer.Length)).CopyTo(buffer);
        try
        {
            // A hung write would block every source (polling is sequential). WaitAsync bounds the
            // wait even if the driver ignores CancelIoEx; the channel is then disposed by the caller.
            await _writer.WriteAsync(buffer, _stop.Token).AsTask().WaitAsync(WriteTimeout, _stop.Token);
        }
        catch (TimeoutException)
        {
            throw new IOException("HID write timed out");
        }
    }

    /// <summary>First queued input report that <paramref name="accept"/> takes, or null on timeout.</summary>
    public async Task<byte[]?> ReceiveAsync(Func<byte[], bool> accept, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (await _input.Reader.WaitToReadAsync(cts.Token))
                while (_input.Reader.TryRead(out var report))
                    if (accept(report))
                        return report;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        return null;
    }

    /// <summary>
    /// Reads feature report <paramref name="id"/> and drops the data: some devices change mode on
    /// the read itself (DualSense over Bluetooth starts full 0x31 reports). False if it failed or
    /// timed out. HidD_GetFeature is synchronous and a pad at the edge of Bluetooth range can hold
    /// it for seconds; polling is sequential, so the wait is bounded like a write (the call itself
    /// finishes on its pool thread later).
    /// </summary>
    public async Task<bool> RequestFeatureAsync(byte id, CancellationToken ct)
    {
        if (_featureLength <= 0 || _closed || Volatile.Read(ref _disposed) != 0)
            return false;
        var buffer = new byte[_featureLength];
        buffer[0] = id;
        try
        {
            return await Task.Run(() => HidD_GetFeature(_reader.SafeFileHandle, buffer, (uint)buffer.Length))
                .WaitAsync(WriteTimeout, ct);
        }
        catch (Exception e) when (e is TimeoutException or ObjectDisposedException)
        {
            return false; // no answer in time, or disposed between the check and the call
        }
    }

    /// <summary>Drops replies left over from earlier requests or other software (G HUB).</summary>
    public void DiscardPending()
    {
        while (_input.Reader.TryRead(out _)) { }
    }

    async Task ReadLoopAsync(int inputLength)
    {
        var buffer = new byte[inputLength];
        try
        {
            int read;
            while ((read = await _reader.ReadAsync(buffer, _stop.Token)) > 0)
                _input.Writer.TryWrite(buffer[..read]);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Closed by Dispose or the device went away; the next SendAsync throws and callers reopen.
        }
        finally
        {
            _closed = true;
            _input.Writer.TryComplete();
        }
    }

    /// <summary>Safe to call twice: a source's error path may dispose a channel the poller is disposing.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        _reader.Dispose();
        _writer.Dispose();
        _stop.Dispose();
    }

    const int HidpStatusSuccess = 0x00110000;

    // DllImport, not LibraryImport: the latter needs AllowUnsafeBlocks for the whole project.
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_GetPreparsedData(SafeFileHandle device, out nint preparsed);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_FreePreparsedData(nint preparsed);

    [DllImport("hid.dll")]
    static extern int HidP_GetCaps(nint preparsed, [Out] ushort[] caps);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_GetFeature(SafeFileHandle device, [In, Out] byte[] buffer, uint length);
}
