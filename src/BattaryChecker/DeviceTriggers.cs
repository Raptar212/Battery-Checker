using System.Windows.Interop;
using System.Windows.Threading;

namespace BattaryChecker;

/// <summary>
/// Asks for an early poll when devices come or go, or the PC wakes: otherwise a plugged-in
/// device or a fresh reading after sleep waits up to a full poll interval.
/// </summary>
sealed class DeviceTriggers : IDisposable
{
    const int WM_DEVICECHANGE = 0x0219, DBT_DEVNODES_CHANGED = 0x0007;
    const int WM_POWERBROADCAST = 0x0218, PBT_APMRESUMEAUTOMATIC = 0x0012;

    // A plug-in or a Bluetooth connect is a burst of node changes; poll once it settles.
    static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);
    // After wake receivers and Bluetooth reconnect on their own; give them time first.
    static readonly TimeSpan WakeSettle = TimeSpan.FromSeconds(5);

    readonly HwndSource _window;
    readonly DispatcherTimer _delay = new();

    /// <summary>Raised on the UI thread.</summary>
    public event Action? Changed;

    public DeviceTriggers()
    {
        // A hidden top-level window: both messages are broadcasts, which skip message-only windows.
        _window = new HwndSource(new HwndSourceParameters("BattaryChecker.DeviceTriggers") { WindowStyle = 0 });
        _window.AddHook(WndProc);
        _delay.Tick += (_, _) =>
        {
            _delay.Stop();
            Changed?.Invoke();
        };
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE && wParam == DBT_DEVNODES_CHANGED)
            Restart(Settle);
        else if (msg == WM_POWERBROADCAST && wParam == PBT_APMRESUMEAUTOMATIC)
            Restart(WakeSettle);
        return 0;
    }

    /// <summary>
    /// Each new event restarts the wait, so a burst ends in a single poll. A pending longer wait
    /// is kept: the node churn right after wake must not cut the wake delay short.
    /// </summary>
    void Restart(TimeSpan delay)
    {
        if (_delay.IsEnabled && _delay.Interval > delay)
            delay = _delay.Interval;
        _delay.Stop();
        _delay.Interval = delay;
        _delay.Start();
    }

    public void Dispose()
    {
        _delay.Stop();
        _window.Dispose();
    }
}
