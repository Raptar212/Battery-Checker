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
    // Explorer raises its topmost taskbar over a floating widget when the taskbar is clicked.
    readonly DispatcherTimer _floatTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public event Action? Clicked;
    public event Action? Moved;

    public WidgetWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        ApplyStyle();
        Loaded += (_, _) =>
        {
            PlaceInitially();
            ApplyPin();
        };
        IsVisibleChanged += (_, _) => ApplyPin();
        _dockTimer.Tick += (_, _) => Dock();
        _floatTimer.Tick += (_, _) => KeepFloatingAbove();
        Closed += (_, _) => (_dockTimer.IsEnabled, _floatTimer.IsEnabled) = (false, false); // also when Explorer takes the window down
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
        if (Handle != 0) // a growing widget must not leave its monitor; it may stay parked on the taskbar
        {
            var area = ScreenArea.BoundsOf(this);
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - ActualWidth));
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - ActualHeight));
        }
        // In memory only: a size change can come with every poll; the next save or the exit writes it.
        if (_config.WidgetLeft is not null)
            (_config.WidgetLeft, _config.WidgetTop) = (Left, Top);
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
            Dock();
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
            _dock.KeepAbove(Handle);
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
        if (_dock.Dock(Handle))
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
