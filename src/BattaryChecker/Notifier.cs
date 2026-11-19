using BattaryChecker.Devices;
using BattaryChecker.UI;

namespace BattaryChecker;

public sealed record Alert(string Title, string Message);

/// <summary>
/// Decides when to notify. Low: connected, not charging, at or below the low threshold; repeats
/// at most once per <see cref="LowCooldown"/>, re-armed as soon as the level recovers or charging
/// starts. Full: only after we saw the device charging, so a device already full at start is quiet.
/// </summary>
public sealed class Notifier
{
    public static readonly TimeSpan LowCooldown = TimeSpan.FromMinutes(60);

    sealed class State
    {
        public DateTime? LastLowAlert;
        public bool SeenCharging;
    }

    readonly Dictionary<string, State> _states = [];

    public List<Alert> Check(IEnumerable<BatteryReading> readings, int lowThreshold, DateTime now)
    {
        var alerts = new List<Alert>();
        foreach (var r in readings)
        {
            if (!r.Connected || r.Percent is not { } percent)
                continue;
            if (!_states.TryGetValue(r.Id, out var state))
                _states[r.Id] = state = new State();

            if (percent <= lowThreshold && !r.Charging)
            {
                if (state.LastLowAlert is not { } last || now - last >= LowCooldown)
                {
                    alerts.Add(new Alert(Text.LowBatteryTitle, Text.LowBatteryMessage(r.Name, percent)));
                    state.LastLowAlert = now;
                }
            }
            else
            {
                state.LastLowAlert = null; // recovered or charging: the next drop alerts at once
            }

            if (r.Charging && percent < 100)
            {
                state.SeenCharging = true;
            }
            else if (percent >= 100 && state.SeenCharging)
            {
                alerts.Add(new Alert(Text.FullBatteryTitle, Text.FullBatteryMessage(r.Name)));
                state.SeenCharging = false;
            }
            else if (!r.Charging)
            {
                state.SeenCharging = false; // unplugged before full
            }
        }
        return alerts;
    }
}
