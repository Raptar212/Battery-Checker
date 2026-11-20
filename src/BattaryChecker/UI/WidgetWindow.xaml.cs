using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    (WidgetStyle Style, int Columns)? _applied;
    int _count;
    bool _switchingPin; // a pin-mode restyle resizes the widget; that is not the user's doing
    bool _fallback; // the saved position's monitor is gone: the widget stands in for it, the config keeps it
    // Explorer raises its topmost taskbar over a floating widget when the taskbar is clicked.
    readonly DispatcherTimer _floatTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    // Explorer brings a secondary taskbar up a moment after the display change: look once more later.
    readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public event Action? Clicked;
    public event Action? Moved;

    public WidgetWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        if (!_config.PositionInPixels) // saved by a DPI-unaware version: DIPs at the system DPI
        {
            var dpi = ScreenArea.SystemDpi();
            if (_config.WidgetLeft is { } left && _config.WidgetTop is { } top)
                (_config.WidgetLeft, _config.WidgetTop) = (Placement.DipToPixels(left, dpi), Placement.DipToPixels(top, dpi));
            _config.PositionInPixels = true;
        }
        ApplyStyle();
        Loaded += (_, _) =>
        {
            PlaceInitially();
            ApplyPin();
        };
        IsVisibleChanged += (_, _) => ApplyPin();
        _dockTimer.Tick += (_, _) => Dock();
        _floatTimer.Tick += (_, _) => KeepFloatingAbove();
        _displayTimer.Tick += (_, _) =>
        {
            _displayTimer.Stop();
            OnDisplayChanged(true);
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(Handle)?.AddHook(OnMessage);
        // Same DIP size, another physical one: SizeChanged does not come, the slot must be resized.
        DpiChanged += (_, _) =>
        {
            if (_dock.Docked)
                Dispatcher.BeginInvoke(Dock, DispatcherPriority.Background);
        };
        Closed += (_, _) => (_dockTimer.IsEnabled, _floatTimer.IsEnabled, _displayTimer.IsEnabled) = (false, false, false); // also when Explorer takes the window down
        // Keep the right edge in place: the pill grows to the left as devices appear,
        // so a widget parked at the right screen edge never slides off it.
        SizeChanged += (_, e) =>
        {
            if (_dock.Docked) // after the HWND has its new size, which Dock reads
                Dispatcher.BeginInvoke(Dock, DispatcherPriority.Background);
            else if (!_switchingPin && e.PreviousSize.Width > 0 && (e.WidthChanged || e.HeightChanged))
                KeepEdges(new Vector(e.NewSize.Width - e.PreviousSize.Width, e.NewSize.Height - e.PreviousSize.Height));
        };
    }

    /// <summary>A pinned widget has its own style (Horizontal or Compact): only those fit the taskbar height.</summary>
    WidgetStyle EffectiveStyle => _config.PinToTaskbar ? _config.PinnedWidgetStyle : _config.WidgetStyle;

    /// <summary>
    /// A floating widget grows and shrinks to the left and up, so the right and bottom edges
    /// stay put (clamped to the monitor). A saved position follows, or the widget would jump back on the next start.
    /// </summary>
    void KeepEdges(Vector delta)
    {
        Left -= delta.X;
        Top -= delta.Y;
        if (Handle == 0)
            return;
        // A growing widget must not leave its monitor; it may stay parked on the taskbar. In physical pixels:
        // a DIP size rounds, and the widget stuck out a pixel past the edge.
        var (l, t) = ScreenArea.ToPhysical(this, new Point(Left, Top));
        var (r, b) = ScreenArea.ToPhysical(this, new Point(Left + ActualWidth, Top + ActualHeight));
        var (x, y) = Placement.Clamp((l, t, r, b), ScreenArea.MonitorOf(Handle));
        var dip = ScreenArea.ToDip(this, x, y);
        (Left, Top) = (dip.X, dip.Y);
        // In memory only: a size change can come with every poll; the next save or the exit writes it.
        // Not while standing in for a monitor that is gone: the widget goes back there when it returns.
        if (_config.WidgetLeft is not null && !_fallback)
            (_config.WidgetLeft, _config.WidgetTop) = (x, y);
    }

    /// <summary>Picks the panel and item template of the current style; a pinned widget uses its own (Horizontal or Compact).</summary>
    public void ApplyStyle()
    {
        var style = EffectiveStyle;
        var key = (style, style == WidgetStyle.Tiles ? Math.Clamp(_count, 1, 2) : 0);
        if (_applied == key)
            return;
        _applied = key;
        var vertical = style is WidgetStyle.Vertical or WidgetStyle.Named or WidgetStyle.Tiles;
        Items.ItemsPanel = style == WidgetStyle.Tiles ? TilesPanel(key.Item2)
            : (ItemsPanelTemplate)Resources[vertical ? "ColumnPanel" : "RowPanel"];
        Items.ItemTemplate = (DataTemplate)Resources[style + "Item"];
        Stack.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        More.Margin = vertical ? new Thickness(0, 2, 0, 0) : new Thickness(10, 0, 0, 0);
        More.HorizontalAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        UpdateLayout();
    }

    static ItemsPanelTemplate TilesPanel(int columns)
    {
        var grid = new FrameworkElementFactory(typeof(UniformGrid));
        grid.SetValue(UniformGrid.ColumnsProperty, columns);
        return new ItemsPanelTemplate(grid);
    }

    nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Docks or floats per <see cref="AppConfig.PinToTaskbar"/>; a hidden widget frees its taskbar slot.</summary>
    public void ApplyPin()
    {
        var pin = _config.PinToTaskbar && IsVisible;
        _dockTimer.IsEnabled = pin;
        _floatTimer.IsEnabled = !pin && IsVisible; // never runs for a hidden widget
        _switchingPin = true;
        try
        {
            ApplyStyle();
        }
        finally
        {
            _switchingPin = false;
        }
        if (pin)
        {
            if (!_dock.Docked)
                _dock.Retarget(Handle, DockHint());
            Dock();
            if (!_dock.Docked)
                PlaceInitially(); // no slot: the pinned style must not leave the monitor either
        }
        else if (_dock.Docked)
        {
            Opacity = 1;
            _dock.Release(Handle);
            PlaceInitially();
        }
        KeepFloatingAbove();
    }

    void KeepFloatingAbove()
    {
        if (Handle != 0 && !_dock.Docked && IsVisible)
            _dock.KeepAbove(Handle, DockHint());
    }

    /// <summary>Where the taskbar to dock into is looked for: the centre of the saved floating position, else of the widget.</summary>
    (int X, int Y) DockHint()
    {
        ScreenArea.WindowRect(Handle, out var r);
        var (w, h) = (r.R - r.L, r.B - r.T);
        return _config.WidgetLeft is { } left && _config.WidgetTop is { } top
            ? ((int)Math.Round(left) + w / 2, (int)Math.Round(top) + h / 2)
            : (r.L + w / 2, r.T + h / 2);
    }

    const int WM_DISPLAYCHANGE = 0x007E;

    nint OnMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            // After the message: monitors and taskbars are still being rearranged inside it.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => OnDisplayChanged(false)));
            _displayTimer.Stop();
            _displayTimer.Start();
        }
        return 0;
    }

    /// <summary>
    /// A monitor came or went: re-place a floating widget, re-dock a docked one on the taskbar of its monitor.
    /// The same taskbar as before is not released: the async resize back and the re-dock reading the old list
    /// width left the task buttons laid out wrong for a second. The late second pass acts only on a new taskbar.
    /// </summary>
    void OnDisplayChanged(bool onlyIfRetargeted)
    {
        if (Handle == 0)
            return;
        var docked = _dock.Docked;
        if (!_dock.Retarget(Handle, DockHint()) && onlyIfRetargeted)
            return;
        if (docked)
            Dock();
        if (!_dock.Docked)
            PlaceInitially();
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
        if (Handle == 0 || !_dockTimer.IsEnabled) // a Dock queued before an unpin must not re-dock
            return;
        if (_dock.Dock(Handle, DockHint()))
        {
            Opacity = _dock.FullscreenForeground(Handle) ? 0 : 1;
            return;
        }
        Opacity = 1;
        if (!_dock.Docked)
        {
            KeepFloatingAbove(); // no slot: floating meanwhile
            return;
        }
        _dock.Release(Handle);
        PlaceInitially();
    }

    public void Update(IReadOnlyList<DeviceView> devices)
    {
        var shown = devices.Where(d => d.Connected && !d.Hidden).ToList();
        var visible = shown.Take(MaxDevices).ToList();
        _count = visible.Count;
        ApplyStyle();
        var separators = EffectiveStyle is WidgetStyle.Horizontal or WidgetStyle.Compact;
        Items.ItemsSource = visible.Select((d, i) => new WidgetItem(d, separators && i > 0)).ToList();
        More.Text = Text.MoreDevices(shown.Count - visible.Count);
        More.Visibility = shown.Count > visible.Count ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // A resource reference, not the brush itself: the border follows a theme switch.
        Frame.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty,
            visible.Any(d => d.Level == Level.Low) ? "AccentBrush" : "FrameBrush");
    }

    /// <summary>
    /// The saved position (physical pixels) on its monitor, clamped to it (a pinned style may be larger); its monitor
    /// gone, the nearest one stands in without overwriting it. Nothing saved: the primary monitor's bottom-right corner.
    /// </summary>
    void PlaceInitially()
    {
        _fallback = false;
        var monitors = ScreenArea.Monitors();
        if (Handle == 0 || monitors.Count == 0)
            return;
        if (_config.WidgetLeft is { } left && _config.WidgetTop is { } top)
        {
            var (x, y) = ((int)Math.Round(left), (int)Math.Round(top));
            var corner = (x + 10, y + 10);
            var monitor = monitors[Placement.Nearest(monitors, corner)];
            _fallback = !Placement.Contains(monitor, corner);
            // Move first: on a monitor of another DPI the widget takes its physical size there.
            ScreenArea.MoveTo(Handle, x, y);
            if (!ScreenArea.WindowRect(Handle, out var own))
                return;
            var (cx, cy) = Placement.Clamp(own, monitor);
            if ((cx, cy) != (own.L, own.T))
                ScreenArea.MoveTo(Handle, cx, cy);
            return;
        }
        // Default: bottom-right corner of the primary work area, just above the taskbar. The primary monitor
        // starts at (0, 0); there first, so the DIPs below are the primary monitor's.
        ScreenArea.MoveTo(Handle, 0, 0);
        var area = SystemParameters.WorkArea;
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
        _fallback = false;
        if (ScreenArea.WindowRect(Handle, out var own))
            (_config.WidgetLeft, _config.WidgetTop) = (own.L, own.T);
        _dock.Retarget(Handle, DockHint());
        Moved?.Invoke();
    }
}
