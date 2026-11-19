namespace BattaryChecker.Devices;

/// <summary>
/// Logitech devices behind a USB receiver, via HID++ 2.0. Requests go to the short collection
/// (usage page 0xFF00, usage 1); replies may arrive on it or on the long one (usage 2).
/// Only read functions are called (root.getFeature, DEVICE_NAME, battery status).
/// </summary>
public sealed class LogitechSource(ushort receiverPid) : IBatterySource, IDisposable
{
    /// <summary>Lightspeed / Unifying / Bolt receivers; G602's is C537.</summary>
    public static readonly ushort[] ReceiverPids = [0xC537, 0xC539, 0xC53A, 0xC53F, 0xC547, 0xC52B, 0xC548];

    const ushort Vid = 0x046D, VendorPage = 0xFF00;
    const byte MaxSlot = 6;
    // A sleeping mouse is woken by the receiver first: the first reply can take over a second.
    static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    sealed class Slot
    {
        public byte BatteryFeature;
        public bool Unified;
        public string Name = "Logitech";
        public DeviceKind Kind = DeviceKind.Other;
        public BatteryReading? Last;

        /// <summary>False when the device stopped answering mid-discovery (name unread): discover again next poll.</summary>
        public bool Complete = true;
    }

    readonly Dictionary<byte, Slot> _slots = [];
    HidChannel? _short, _long;
    byte _softwareId = Hidpp.LastSoftwareId;

    /// <summary>A short request with a fresh software id (see <see cref="Hidpp.SoftwareId"/>).</summary>
    byte[] Request(byte index, byte feature, byte function, ReadOnlySpan<byte> args = default) =>
        Hidpp.ShortRequest(index, feature, function, args, _softwareId = Hidpp.NextSoftwareId(_softwareId));

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        if (_short is null || _long is null)
        {
            Close();
            _short = await HidChannel.OpenAsync(Vid, receiverPid, VendorPage, 1);
            _long = await HidChannel.OpenAsync(Vid, receiverPid, VendorPage, 2);
            if (_short is null || _long is null)
            {
                Close();
                return []; // receiver not plugged in
            }
        }

        var result = new List<BatteryReading>();
        try
        {
            for (byte index = 1; index <= MaxSlot; index++)
            {
                if (await ReadSlotAsync(index, ct) is { } reading)
                    result.Add(reading);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Close(); // receiver unplugged mid-read: reopen next time
            result.Clear();
            foreach (var slot in _slots.Values)
                if ((slot.Last = Presence.Silent(slot.Last, DateTime.Now)) is { } last)
                    result.Add(last);
        }
        return result;
    }

    async Task<BatteryReading?> ReadSlotAsync(byte index, CancellationToken ct)
    {
        _slots.TryGetValue(index, out var known);
        var slot = known;
        if (slot is not { Complete: true })
        {
            slot = await DiscoverAsync(index, ct) ?? known;
            if (slot is null)
                return null; // empty slot, or a device we have never seen that is asleep
            if (slot != known)
                slot.Last = known?.Last; // a re-discovered device keeps its last level while asleep
            _slots[index] = slot;
        }

        var request = Request(index, slot.BatteryFeature, slot.Unified ? (byte)1 : (byte)0);
        var reply = await RequestAsync(request, ct);
        var match = reply is null ? Hidpp.Match.None : Hidpp.Classify(request, reply);
        if (match == Hidpp.Match.Error)
            slot.Complete = false; // another device paired on this slot answers differently: look again
        if (match != Hidpp.Match.Reply)
            return slot.Last = Presence.Silent(slot.Last, DateTime.Now); // asleep or off

        var level = slot.Unified ? Hidpp.ParseUnifiedBattery(reply) : Hidpp.ParseBatteryStatus(reply);
        return slot.Last = new BatteryReading($"logi:{receiverPid:X4}-{index:X2}", slot.Name, slot.Kind,
            "2.4 GHz", true, level.Percent, level.Charging, DateTime.Now);
    }

    async Task<Slot?> DiscoverAsync(byte index, CancellationToken ct)
    {
        var unified = await FeatureIndexAsync(index, Hidpp.UnifiedBatteryFeature, ct);
        var status = unified == 0 ? await FeatureIndexAsync(index, Hidpp.BatteryStatusFeature, ct) : (byte)0;
        if (unified == 0 && status == 0)
            return null;
        var slot = new Slot { Unified = unified != 0, BatteryFeature = unified != 0 ? unified : status };

        var nameRequest = Hidpp.GetFeatureRequest(index, Hidpp.DeviceNameFeature, _softwareId = Hidpp.NextSoftwareId(_softwareId));
        var nameReply = await RequestAsync(nameRequest, ct);
        if (nameReply is null)
        {
            slot.Complete = false; // fell asleep before telling its name
            return slot;
        }
        var name = Hidpp.Classify(nameRequest, nameReply) == Hidpp.Match.Reply ? Hidpp.FeatureIndex(nameReply) : (byte)0;
        if (name == 0)
            return slot; // no DEVICE_NAME feature: "Logitech" is all there is
        var length = await RequestAsync(Request(index, name, 0), ct);
        slot.Complete = length is { Length: > 4 };
        if (length is { Length: > 4 })
        {
            var text = "";
            while (text.Length < length[4])
            {
                var chunk = await RequestAsync(Request(index, name, 1, [(byte)text.Length]), ct);
                var part = chunk is null ? "" : Hidpp.NameChunk(chunk, length[4] - text.Length);
                if (part.Length == 0)
                    break;
                text += part;
            }
            if (text.Length > 0)
                slot.Name = text;
        }
        var type = await RequestAsync(Request(index, name, 2), ct);
        if (type is { Length: > 4 })
            slot.Kind = Hidpp.DeviceType(type[4]);
        return slot;
    }

    async Task<byte> FeatureIndexAsync(byte index, ushort feature, CancellationToken ct)
    {
        var request = Hidpp.GetFeatureRequest(index, feature, _softwareId = Hidpp.NextSoftwareId(_softwareId));
        var reply = await RequestAsync(request, ct);
        return reply is not null && Hidpp.Classify(request, reply) == Hidpp.Match.Reply ? Hidpp.FeatureIndex(reply) : (byte)0;
    }

    /// <summary>Sends a request and returns its reply or error from either collection, or null.</summary>
    async Task<byte[]?> RequestAsync(byte[] request, CancellationToken ct)
    {
        _short!.DiscardPending();
        _long!.DiscardPending();
        await _short.SendAsync(request);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bool Accept(byte[] r) => Hidpp.Classify(request, r) != Hidpp.Match.None;
        var a = _short.ReceiveAsync(Accept, Timeout, cts.Token);
        var b = _long.ReceiveAsync(Accept, Timeout, cts.Token);
        var first = await Task.WhenAny(a, b);
        var reply = await first ?? await (first == a ? b : a);
        cts.Cancel();
        return reply;
    }

    void Close()
    {
        _short?.Dispose();
        _long?.Dispose();
        _short = _long = null;
    }

    public void Dispose() => Close();
}
