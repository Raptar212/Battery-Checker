using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BattaryChecker.UI;

/// <summary>Notification-area icon: chosen devices as bars, rings or a number; fills up while charging, blinks when critical.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly AppConfig _config;
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _widgetItem;
    readonly Forms.ToolStripMenuItem _pinItem;
    // One timer for both animations: a 200 ms charging frame, the blink flips every 3rd tick (0.6 s).
    readonly Forms.Timer _animation = new() { Interval = 200 };
    int _tick;
    bool _blinking, _charging;
    List<DeviceView> _shown = [];
    bool _light;
    Drawing.Icon? _current;

    public event Action? OpenRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? ToggleWidgetRequested;
    public event Action? TogglePinRequested;
    public event Action? ExitRequested;
    public event Action? CheckUpdatesRequested;

    /// <summary>The user clicked the last balloon; the app knows what it offered.</summary>
    public event Action? BalloonClicked;

    public TrayIcon(AppConfig config)
    {
        _config = config;
        var menu = new Forms.ContextMenuStrip();
        _widgetItem = new Forms.ToolStripMenuItem(Text.HideWidget, null, (_, _) => ToggleWidgetRequested?.Invoke());
        _pinItem = new Forms.ToolStripMenuItem(Text.PinToTaskbar, null, (_, _) => TogglePinRequested?.Invoke());
        menu.Items.Add(_widgetItem);
        menu.Items.Add(_pinItem);
        menu.Items.Add(new Forms.ToolStripMenuItem(Text.Refresh, null, (_, _) => RefreshRequested?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem(Text.Settings + "…", null, (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem(Text.CheckUpdatesMenu, null, (_, _) => CheckUpdatesRequested?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem(Text.AboutMenu(Updater.Current.ToString(3)), null, (_, _) => OpenRepo()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(Text.Exit, null, (_, _) => ExitRequested?.Invoke()));

        _icon = new Forms.NotifyIcon { Text = Text.AppName, ContextMenuStrip = menu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                OpenRequested?.Invoke();
        };
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke();
        _animation.Tick += (_, _) =>
        {
            _tick++;
            Redraw();
        };
        Redraw();
        _icon.Visible = true;
    }

    int Critical => AppConfig.ClampCritical(_config.CriticalThreshold, _config.LowThreshold);

    /// <summary>Windows switched its theme: reread the taskbar colour and redraw at once.</summary>
    public void RefreshTheme()
    {
        _light = TrayGlyph.LightTaskbar();
        Redraw();
    }

    public void Update(IReadOnlyList<DeviceView> devices, string tooltip, bool widgetVisible)
    {
        _shown = TrayGlyph.Pick(devices, _config.TrayDevices, TrayGlyph.MaxOf(_config.TrayStyle), _config.DeviceOrder);
        _light = TrayGlyph.LightTaskbar();
        _icon.Text = tooltip;
        _widgetItem.Text = widgetVisible ? Text.HideWidget : Text.ShowWidget;
        _pinItem.Checked = _config.PinToTaskbar;
        _pinItem.Visible = TaskbarDock.CanDock();

        _blinking = _config.BlinkCritical && _shown.Any(d => TrayGlyph.IsCritical(d, Critical));
        _charging = TrayGlyph.Animates(_config.TrayStyle, _shown);
        var animate = _blinking || _charging;
        if (animate != _animation.Enabled)
        {
            _animation.Enabled = animate;
            _tick = 0;
        }
        Redraw();
    }

    static void OpenRepo()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateCheck.RepoUrl) { UseShellExecute = true })
                ?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // no browser registered: nothing useful to do from a menu click
        }
    }

    /// <summary>Shown by Windows 10/11 as a toast from the tray icon.</summary>
    public void Notify(string title, string message, bool info = false) =>
        _icon.ShowBalloonTip(10_000, title, message, info ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);

    void Redraw()
    {
        var size = Forms.SystemInformation.SmallIconSize.Height;
        var blinkOff = _blinking && _tick / 3 % 2 == 1;
        var frame = _charging ? _tick : 0;
        using var bitmap = TrayGlyph.Render(size, _config.TrayStyle, _shown, Critical, blinkOff, _light, frame);

        // GetHicon allocates a GDI handle the Icon does not own: copy it, then free it,
        // otherwise every redraw (30 s poll, 0.2 s animation) leaks one handle.
        var handle = bitmap.GetHicon();
        Drawing.Icon icon;
        try
        {
            icon = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
        _icon.Icon = icon;
        _current?.Dispose();
        _current = icon;
    }

    public void Dispose()
    {
        _animation.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(nint handle);
}
