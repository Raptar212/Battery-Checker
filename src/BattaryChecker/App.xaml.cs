using System.Windows;
using BattaryChecker.Devices;
using BattaryChecker.UI;

namespace BattaryChecker;

/// <summary>Wires sources → poller → widget, panel and tray. No main window.</summary>
public partial class App : Application
{
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    Mutex? _singleInstance;
    AppConfig _config = new();
    DevicePoller? _poller;
    WidgetWindow? _widget;
    PanelWindow? _panel;
    TrayIcon? _tray;
    IReadOnlyList<BatteryReading> _readings = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A few icons and labels do not need the GPU; software rendering keeps the Direct3D
        // device and the graphics driver (~100 MB mapped on Intel) out of the process.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        _singleInstance = new Mutex(true, @"Local\BattaryChecker", out var first);
        if (!first)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        var firstRun = !System.IO.File.Exists(AppConfig.DefaultPath);
        _config = AppConfig.Load(AppConfig.DefaultPath);
        // A crash must not leave the task buttons squeezed around an empty slot.
        DispatcherUnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception, CrashLog.DefaultPath, DateTime.Now);
            _widget?.Undock();
        };
        // Same for a crash on a pool thread; the window belongs to the UI thread.
        // ponytail: a killed process (Task Manager) cannot clean up; Explorer re-lays out the
        // buttons on its next change, and the next start re-docks or releases the slot.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.ExceptionObject as Exception, CrashLog.DefaultPath, DateTime.Now);
            Dispatcher.Invoke(() => _widget?.Undock());
        };

        Theme.Apply(Resources, _config.Theme);
        _themeHandler = (_, args) =>
        {
            if (args.Category == Microsoft.Win32.UserPreferenceCategory.General)
                Dispatcher.BeginInvoke(() =>
                {
                    if (_exiting) // queued before OnExit unsubscribed: the tray may be disposed
                        return;
                    Theme.Apply(Resources, _config.Theme);
                    _tray?.RefreshTheme();
                });
        };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += _themeHandler;
        CreateWidget();

        _tray = new TrayIcon(_config);
        _tray.OpenRequested += TogglePanel;
        _tray.RefreshRequested += Refresh;
        _tray.SettingsRequested += OpenSettings;
        _tray.ToggleWidgetRequested += ToggleWidget;
        _tray.TogglePinRequested += TogglePin;
        _tray.ExitRequested += Quit;
        _tray.CheckUpdatesRequested += () => CheckForUpdates(manual: true);
        _tray.BalloonClicked += () =>
        {
            if (_balloonUpdate is { } release)
                OfferUpdate(release);
        };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Stop(); // once per start
            CheckForUpdates(manual: false);
        };
        if (_config.CheckUpdates)
            _updateTimer.Start(); // a minute later: right after logon the network may not be up yet
        if (firstRun)
        {
            Balloon(Text.Welcome); // the tray icon may sit in the overflow: say where the app went
            SaveConfig(); // once: the next start is not the first
        }

        List<IBatterySource> sources =
        [
            .. LogitechSource.ReceiverPids.Select(pid => new LogitechSource(pid)),
            new AstroA50Source(),
            new DualSenseSource(() => _config.DualSenseFullReports),
            new BluetoothSource(),
        ];
        _poller = new DevicePoller(sources, PollInterval);
        _poller.Updated += readings => Dispatcher.BeginInvoke(() => OnPolled(readings));
        _poller.Start();

        _triggers = new DeviceTriggers();
        _triggers.Changed += Refresh;
    }

    DeviceTriggers? _triggers;

    Microsoft.Win32.UserPreferenceChangedEventHandler? _themeHandler;

    bool _exiting;

    void CreateWidget()
    {
        var widget = new WidgetWindow(_config);
        _widget = widget;
        widget.Clicked += TogglePanel;
        widget.Moved += SaveConfig;
        // A docked widget is owned by Explorer's taskbar; if Explorer restarts and takes the
        // window with it, bring the widget back. A closed window is never kept: Show() on it throws.
        widget.Closed += (_, _) =>
        {
            widget.Undock();
            if (_widget == widget)
                _widget = null;
            if (_exiting || Dispatcher.HasShutdownStarted || !MayRecreate(DateTime.Now))
                return; // over the limit: the tray's "show widget" still brings it back
            // A second later: a closing Explorer would otherwise take each new window down at once.
            // A timer, not async/await, so an exception still reaches DispatcherUnhandledException.
            var delay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                if (_exiting || _widget is not null)
                    return; // exiting, or the user already brought it back from the tray
                CreateWidget();
                Render(_readings);
            };
            delay.Start();
        };
        widget.Show();
    }

    readonly Queue<DateTime> _recreated = new();

    /// <summary>At most 5 widget re-creations a minute: no endless create/destroy loop.</summary>
    bool MayRecreate(DateTime now)
    {
        while (_recreated.Count > 0 && now - _recreated.Peek() > TimeSpan.FromMinutes(1))
            _recreated.Dequeue();
        if (_recreated.Count >= 5)
            return false;
        _recreated.Enqueue(now);
        return true;
    }

    readonly Notifier _notifier = new();

    /// <summary>After each poll: redraw, then alerts (only here, so UI toggles never re-alert).</summary>
    void OnPolled(IReadOnlyList<BatteryReading> readings)
    {
        var named = Render(readings);
        var alerts = _notifier.Check(named, _config.LowThreshold, DateTime.Now);
        if (_config.Notifications)
            foreach (var alert in alerts)
            {
                _balloonUpdate = null; // this balloon replaces an update offer: its click must not install
                _tray?.Notify(alert.Title, alert.Message, info: alert.Title == Text.FullBatteryTitle);
            }
    }

    readonly System.Windows.Threading.DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    bool _updating; // an update check or download is under way
    UpdateRelease? _balloonUpdate; // the release the last balloon offers; null when it said something else
    (UpdateRelease Release, string Path)? _downloaded; // verified installer waiting for the settings window to close

    void Balloon(string message, bool info = true, UpdateRelease? offer = null)
    {
        _balloonUpdate = offer;
        _tray?.Notify(Text.AppName, message, info);
    }

    /// <summary>Automatic: silent unless a newer release exists. Manual: always says how it went.</summary>
    async void CheckForUpdates(bool manual)
    {
        if (_updating)
        {
            if (manual)
                Balloon(Text.UpdateInProgress);
            return;
        }
        _updating = true;
        UpdateRelease? release;
        try
        {
            release = await Updater.FindNewer();
        }
        catch (Exception e) // async void: anything left would crash the app
        {
            if (manual) // automatic: no network is normal, try again next start
                Balloon(Text.UpdateCheckFailed(Updater.Reason(e)), info: false);
            return;
        }
        finally
        {
            _updating = false;
        }
        if (_exiting)
            return;
        if (release is null)
        {
            if (manual)
                Balloon(Text.UpToDate(Updater.Current.ToString(3)));
        }
        else if (manual)
            OfferUpdate(release);
        else
            Balloon(Text.UpdateAvailable(release.Version.ToString(3)), offer: release);
    }

    bool _asking; // the update question is on screen: a second click must not open another one

    void OfferUpdate(UpdateRelease release)
    {
        if (_updating || _asking)
            return;
        if (_downloaded is { } d && d.Release == release)
        {
            InstallUpdate(release); // already agreed and downloaded, only waited for the settings window
            return;
        }
        _asking = true;
        MessageBoxResult answer;
        try
        {
            answer = MessageBox.Show(Text.UpdateQuestion(release.Version.ToString(3), Updater.Current.ToString(3)),
            Text.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes,
            MessageBoxOptions.DefaultDesktopOnly); // a tray app has no window to own it: keep it from opening behind others
        }
        finally
        {
            _asking = false;
        }
        if (answer == MessageBoxResult.Yes && !_exiting)
            InstallUpdate(release);
    }

    /// <summary>
    /// Downloads in the background, starts the installer and exits, so its "close applications" page has nothing to
    /// close. Not while the settings window is open: exiting would drop its unsaved input.
    /// </summary>
    async void InstallUpdate(UpdateRelease release)
    {
        if (_updating)
            return;
        _updating = true;
        try
        {
            if (_downloaded is not { } d || d.Release != release)
            {
                Balloon(Text.UpdateDownloading(release.Version.ToString(3)));
                _downloaded = (release, await Updater.Download(release));
            }
            if (_exiting)
                return; // the user chose "exit" during the download
            if (_settings is not null)
            {
                Balloon(Text.UpdateDownloaded, offer: release);
                return;
            }
            Updater.Run(_downloaded.Value.Path);
        }
        catch (Exception e) // async void: anything left would crash the app; nothing here is worse than "not installed"
        {
            _downloaded = null;
            Balloon(Text.UpdateFailed(Updater.Reason(e)), info: false);
            return;
        }
        finally
        {
            _updating = false;
        }
        Quit();
    }

    /// <summary>Redraws every view; returns the readings with user names applied.</summary>
    List<BatteryReading> Render(IReadOnlyList<BatteryReading> readings)
    {
        _readings = readings; // own names: the settings window shows them as placeholders
        var named = readings.Select(r => r with { Name = _config.NameOf(r.Id, r.Name) }).ToList();
        List<DeviceView> Views(ICollection<string> inTray) => Levels.Ordered(named)
            .Select(r => new DeviceView(r, _config.HiddenDevices.Contains(r.Id), _config.LowThreshold,
                _config.MediumThreshold, inTray.Contains(r.Id)))
            .ToList();
        // The pin lights up for what the tray icon really shows, implicit choice included.
        _trayShown = TrayGlyph.Pick(Views([]), _config.TrayDevices, TrayGlyph.MaxOf(_config.TrayStyle))
            .Select(d => d.Id).ToHashSet();
        var views = Views(_trayShown);
        _widget?.Update(views);
        _panel?.Update(views);
        _tray?.Update(views, Levels.Tooltip(named), _widget?.IsVisible ?? false);
        return named;
    }

    void TogglePanel()
    {
        if (_panel is { IsVisible: true })
        {
            _panel.Hide();
            return;
        }
        if (_panel is { JustHidden: true })
            return; // this very click took the focus and closed it: a second click opens it again
        if (_panel is null)
        {
            _panel = new PanelWindow();
            _panel.RefreshRequested += Refresh;
            _panel.SettingsRequested += OpenSettings;
            _panel.ToggleHiddenRequested += ToggleHidden;
            _panel.ToggleTrayRequested += ToggleTray;
        }
        Render(_readings);
        _panel.ShowNear(_widget is { IsVisible: true } ? _widget : null);
    }

    SettingsWindow? _settings;

    void OpenSettings()
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_config, _readings, Autostart.IsEnabled());
        _settings.Saved += ApplySettings;
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    void ApplySettings(SettingsResult result)
    {
        (_config.LowThreshold, _config.MediumThreshold) = (result.Low, result.Medium);
        _config.CriticalThreshold = result.Critical;
        _config.Notifications = result.Notifications;
        _config.BlinkCritical = result.Blink;
        _config.DualSenseFullReports = result.DualSenseFullReports;
        _config.TrayStyle = result.TrayStyle;
        _config.Theme = result.Theme;
        Theme.Apply(Resources, _config.Theme);
        _config.CheckUpdates = result.CheckUpdates;
        if (!result.CheckUpdates)
            _updateTimer.Stop(); // turned off within the first minute: skip the pending start check
        foreach (var device in result.Devices)
        {
            if (string.IsNullOrWhiteSpace(device.Name))
                _config.DeviceNames.Remove(device.Id);
            else
                _config.DeviceNames[device.Id] = device.Name.Trim();
            _config.HiddenDevices.Remove(device.Id);
            if (!device.OnWidget)
                _config.HiddenDevices.Add(device.Id);
        }
        SaveConfig();
        if (result.Autostart != Autostart.IsEnabled() && !Autostart.Set(result.Autostart))
            Balloon(Text.AutostartFailed, info: false);
        Render(_readings);
    }

    void ToggleHidden(string id)
    {
        if (!_config.HiddenDevices.Remove(id))
            _config.HiddenDevices.Add(id);
        SaveConfig();
        Render(_readings);
    }

    HashSet<string> _trayShown = [];

    /// <summary>The first toggle turns the implicit choice (all connected) into an explicit one.</summary>
    void ToggleTray(string id)
    {
        if (_config.TrayDevices.Count == 0)
            _config.TrayDevices.AddRange(_trayShown);
        if (!_config.TrayDevices.Remove(id))
            _config.TrayDevices.Add(id);
        SaveConfig();
        Render(_readings);
    }

    void TogglePin()
    {
        _config.PinToTaskbar = !_config.PinToTaskbar;
        SaveConfig();
        _widget?.ApplyPin();
        Render(_readings);
    }

    void ToggleWidget()
    {
        if (_widget is null)
            CreateWidget();
        else if (_widget.IsVisible)
            _widget.Hide();
        else
            _widget.Show();
        Render(_readings);
    }

    /// <summary>Flags the exit before WPF closes the windows, so the widget is not brought back.</summary>
    void Quit()
    {
        _exiting = true;
        _widget?.Undock();
        Shutdown();
    }

    void Refresh() => _ = _poller?.PollAsync();

    void SaveConfig() => _config.Save(AppConfig.DefaultPath);

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _updateTimer.Stop();
        if (_themeHandler is not null)
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= _themeHandler;
        if (_singleInstance is not null) // a second instance never loaded the config: saving would write defaults
            SaveConfig(); // retry a save that hit a locked file earlier
        _triggers?.Dispose();
        _poller?.Dispose();
        _widget?.Undock();
        _tray?.Dispose();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
