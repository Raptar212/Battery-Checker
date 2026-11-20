using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BattaryChecker.UI;

/// <summary>
/// One widget slot. The separator flag travels with the data: a PreviousData trigger resolves a
/// layout pass late, briefly narrowing the pill, and the docked widget jumped.
/// </summary>
public sealed record WidgetItem(DeviceView View, bool Separator);

/// <summary>
/// Small always-on-top pill: connected, not hidden devices. Floating (drag to move) or pinned
/// into the taskbar left of the tray. Click for the panel.
/// </summary>
public partial class WidgetWindow : Window
{
    const double ScreenMargin = 12;
    public const int MaxDevices = 4;
    readonly AppConfig _config;
    readonly TaskbarDock _dock = new();
    readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public event Action? Clicked;
    public event Action? Moved;

    public WidgetWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        Loaded += (_, _) =>
        {
            PlaceInitially();
            ApplyPin();
        };
        IsVisibleChanged += (_, _) => ApplyPin();
        _dockTimer.Tick += (_, _) => Dock();
        Closed += (_, _) => _dockTimer.Stop(); // also when Explorer takes the window down
        // Keep the right edge in place: the pill grows to the left as devices appear,
        // so a widget parked at the right screen edge never slides off it.
        SizeChanged += (_, e) =>
        {
            if (_dock.Docked) // after the HWND has its new size, which Dock reads
                Dispatcher.BeginInvoke(Dock, DispatcherPriority.Background);
            else if (e.WidthChanged && e.PreviousSize.Width > 0)
                Left -= e.NewSize.Width - e.PreviousSize.Width;
        };
    }

    nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Docks or floats per <see cref="AppConfig.PinToTaskbar"/>; a hidden widget frees its taskbar slot.</summary>
    public void ApplyPin()
    {
        var pin = _config.PinToTaskbar && IsVisible;
        _dockTimer.IsEnabled = pin;
        if (pin)
        {
            Dock();
        }
        else if (_dock.Docked)
        {
            Opacity = 1;
            _dock.Release(Handle);
            PlaceInitially();
        }
    }

    /// <summary>Gives the task buttons their space back; call on exit.</summary>
    public void Undock() => _dock.Release(Handle);

    /// <summary>
    /// No slot (taskbar went vertical or auto-hide, Explorer restarting): float meanwhile, so the
    /// widget stays draggable; the timer keeps trying and docks again once a slot is back.
    /// </summary>
    /// <remarks>
    /// Docked, the widget follows the taskbar out of a full-screen app's way. It is topmost and
    /// would stay over the game, so it turns fully transparent instead: a transparent layered
    /// window lets clicks through, and IsVisible (tray menu, slot) stays as it is.
    /// </remarks>
    void Dock()
    {
        if (Handle == 0)
            return;
        if (_dock.Dock(Handle))
        {
            Opacity = _dock.FullscreenForeground(Handle) ? 0 : 1;
            return;
        }
        Opacity = 1;
        if (!_dock.Docked)
            return;
        _dock.Release(Handle);
        PlaceInitially();
    }

    public void Update(IReadOnlyList<DeviceView> devices)
    {
        var shown = devices.Where(d => d.Connected && !d.Hidden).ToList();
        var visible = shown.Take(MaxDevices).ToList();
        Items.ItemsSource = visible.Select((d, i) => new WidgetItem(d, i > 0)).ToList();
        More.Text = Text.MoreDevices(shown.Count - visible.Count);
        More.Visibility = shown.Count > visible.Count ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // A resource reference, not the brush itself: the border follows a theme switch.
        Frame.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty,
            visible.Any(d => d.Level == Level.Low) ? "AccentBrush" : "FrameBrush");
    }

    void PlaceInitially()
    {
        var area = SystemParameters.WorkArea;
        if (_config.WidgetLeft is { } left && _config.WidgetTop is { } top
            && ScreenArea.OnAnyMonitor(this, new Point(left + 10, top + 10)))
        {
            Left = left;
            Top = top;
            return;
        }
        // Default: bottom-right corner of the primary work area, just above the taskbar.
        Left = area.Right - ActualWidth - ScreenMargin;
        Top = area.Bottom - ActualHeight - ScreenMargin;
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_dock.Docked)
        {
            Clicked?.Invoke();
            return;
        }
        var (left, top) = (Left, Top);
        DragMove(); // returns when the button is released
        if (Math.Abs(Left - left) < 2 && Math.Abs(Top - top) < 2)
        {
            Clicked?.Invoke();
            return;
        }
        _config.WidgetLeft = Left;
        _config.WidgetTop = Top;
        Moved?.Invoke();
    }
}
