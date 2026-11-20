using System.Runtime.InteropServices;

namespace BattaryChecker.UI;

/// <summary>
/// Docks a window into the primary taskbar, left of the tray: shrinks Explorer's task-button
/// list (MSTaskSwWClass) by the window's width and parks the window in the freed slot, owned by
/// the taskbar, so the widget sits above the taskbar. Being topmost it does not drop below a
/// full-screen app with the taskbar; <see cref="FullscreenForeground"/> tells the caller to hide it.
/// Explorer re-lays out the list on its own (new buttons, tray icons, restart), so the caller
/// repeats <see cref="Dock"/> on a timer; every call is idempotent.
/// Owning across processes couples the widget's input queue with Explorer's: keep blocking work
/// off the UI thread.
/// ponytail: assumes the task list is the rightmost rebar band (no Links/Address toolbars) and
/// trusts whichever window has class Shell_TrayWnd; check the owner process if that ever matters.
/// </summary>
public sealed class TaskbarDock
{
    const int Margin = 4; // px between the widget and the tray / task buttons

    nint _taskbar, _rebar, _taskList;
    nint _ownOwner; // WPF's hidden owner that keeps a ShowInTaskbar=False window off the taskbar and Alt+Tab

    public bool Docked { get; private set; }

    /// <summary>
    /// Places the window; false if there is no slot: Explorer down, vertical or auto-hide taskbar
    /// (ponytail: none of them supported, the widget floats instead).
    /// </summary>
    public bool Dock(nint window)
    {
        // Own rect is in physical pixels, the same space as Explorer's rects.
        // AppBar state is a cross-process message: ask only once the cheap checks pass.
        if (!Find() || !GetWindowRect(_rebar, out var bar) || !GetWindowRect(_taskList, out var list)
            || !GetWindowRect(window, out var own) || AutoHide())
            return false;
        var (width, height) = (own.Right - own.Left, own.Bottom - own.Top);
        if (bar.Bottom - bar.Top > bar.Right - bar.Left)
            return false;

        var slot = width + 2 * Margin;
        var listWidth = bar.Right - slot - list.Left;
        if (listWidth <= 0)
            return false;

        // Own first, shrink after: if Explorer refuses the owner, the buttons keep their room.
        var owner = GetWindowLongPtr(window, GWLP_HWNDPARENT);
        if (owner != _taskbar)
        {
            if (!Docked)
                _ownOwner = owner; // not after an Explorer restart: then "owner" is the dead taskbar
            SetWindowLongPtr(window, GWLP_HWNDPARENT, _taskbar);
            if (GetWindowLongPtr(window, GWLP_HWNDPARENT) != _taskbar)
                return false;
        }
        if (list.Right - list.Left != listWidth)
            ResizeList(bar, list, listWidth);
        var top = bar.Top + (bar.Bottom - bar.Top - height) / 2;
        SetWindowPos(window, 0, bar.Right - slot + Margin, top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        KeepAboveTaskbar(window);
        Docked = true;
        return true;
    }

    /// <summary>
    /// Explorer re-raises its topmost taskbar over the owned widget now and then (seen on Win10:
    /// the widget vanished until the taskbar was clicked). Below it, the widget goes right above it.
    /// </summary>
    void KeepAboveTaskbar(nint window)
    {
        for (var h = GetWindow(window, GW_HWNDPREV); h != 0; h = GetWindow(h, GW_HWNDPREV))
        {
            if (h != _taskbar)
                continue;
            var above = GetWindow(_taskbar, GW_HWNDPREV);
            SetWindowPos(window, above == 0 ? HWND_TOPMOST : above, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            return;
        }
    }

    /// <summary>
    /// For a floating widget: the same z-order check without docking (cheap, no cross-process
    /// message). Only the order relative to the taskbar changes, never the position.
    /// </summary>
    public void KeepAbove(nint window)
    {
        // Only the taskbar itself is needed: Windows 11's has no rebar or task list to dock into.
        Find();
        if (IsWindow(_taskbar))
            KeepAboveTaskbar(window);
    }

    /// <summary>Gives the slot back to the task buttons and restores the window's own owner.</summary>
    public void Release(nint window)
    {
        if (!Docked)
            return;
        Docked = false;
        SetWindowLongPtr(window, GWLP_HWNDPARENT, _ownOwner);
        if (IsWindow(_rebar) && IsWindow(_taskList) && GetWindowRect(_rebar, out var bar) && GetWindowRect(_taskList, out var list))
            ResizeList(bar, list, bar.Right - list.Left);
    }

    /// <summary>Async: a hung Explorer must not freeze our UI thread (or exit) waiting for the resize.</summary>
    void ResizeList(Rect bar, Rect list, int width) =>
        SetWindowPos(_taskList, 0, list.Left - bar.Left, list.Top - bar.Top, width, list.Bottom - list.Top,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);

    /// <summary>
    /// SHAppBarMessage is a synchronous SendMessage to Explorer: asked on every 1 s tick, a hung
    /// Explorer would freeze our UI each second. The answer is cached for 10 s instead.
    /// </summary>
    bool AutoHide()
    {
        var now = Environment.TickCount64;
        if (now - _autoHideAt < 10_000 && _autoHideAt != 0)
            return _autoHide;
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        _autoHide = (SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
        _autoHideAt = now;
        return _autoHide;
    }

    bool _autoHide;
    long _autoHideAt;

    /// <summary>
    /// The foreground window covers the taskbar's whole monitor: a full-screen or borderless game,
    /// a video. Explorer drops the taskbar behind such a window; the docked widget, being topmost,
    /// would stay on top, so the caller hides it. The desktop itself also spans the monitor.
    /// </summary>
    public bool FullscreenForeground(nint own)
    {
        var fg = GetForegroundWindow();
        if (fg == 0 || fg == own || fg == _taskbar || !GetWindowRect(fg, out var r))
            return false;
        var cls = new System.Text.StringBuilder(32);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW")
            return false;
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(_taskbar, MONITOR_DEFAULTTOPRIMARY), ref info))
            return false;
        var m = info.Monitor;
        return Covers((r.Left, r.Top, r.Right, r.Bottom), (m.Left, m.Top, m.Right, m.Bottom));
    }

    /// <summary>
    /// A window covers the monitor, give or take <paramref name="slack"/> px: a DPI-unaware game
    /// gets a virtualised rect that rounding can leave a pixel short of the monitor.
    /// </summary>
    public static bool Covers((int L, int T, int R, int B) w, (int L, int T, int R, int B) m, int slack = 2) =>
        w.L <= m.L + slack && w.T <= m.T + slack && w.R >= m.R - slack && w.B >= m.B - slack;

    /// <summary>
    /// The Windows 10 taskbar has a rebar with the task list to make room in; a taskbar without them (Windows 11's
    /// XAML taskbar) cannot host the widget, so "pin to taskbar" is not offered there. Briefly false while
    /// Explorer restarts; the tray menu asks again on its next update.
    /// </summary>
    public static bool CanDock() => Lookup().TaskList != 0;

    /// <summary>The window chain docking needs: taskbar, its rebar, the task list inside it (0 where missing).</summary>
    static (nint Taskbar, nint Rebar, nint TaskList) Lookup()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        var rebar = taskbar == 0 ? 0 : FindWindowEx(taskbar, 0, "ReBarWindow32", null);
        return (taskbar, rebar, rebar == 0 ? 0 : FindWindowEx(rebar, 0, "MSTaskSwWClass", null));
    }

    /// <summary>Handles survive until Explorer restarts; then they are looked up again.</summary>
    bool Find()
    {
        if (IsWindow(_taskbar) && IsWindow(_rebar) && IsWindow(_taskList))
            return true;
        (_taskbar, _rebar, _taskList) = Lookup();
        return _taskList != 0;
    }

    const int GWLP_HWNDPARENT = -8;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_ASYNCWINDOWPOS = 0x4000;
    const uint GW_HWNDPREV = 3;
    static readonly nint HWND_TOPMOST = -1;

    [DllImport("user32.dll")]
    static extern nint GetWindow(nint hwnd, uint cmd);
    const uint ABM_GETSTATE = 4;
    const nint ABS_AUTOHIDE = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint FindWindowEx(nint parent, nint childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    struct AppBarData
    {
        public uint Size;
        public nint Window;
        public uint CallbackMessage, Edge;
        public Rect Rect;
        public nint Param;
    }

    [DllImport("shell32.dll")]
    static extern nint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll")]
    static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
