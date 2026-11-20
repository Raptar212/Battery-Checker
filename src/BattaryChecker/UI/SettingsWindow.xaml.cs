using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BattaryChecker.Devices;

namespace BattaryChecker.UI;

/// <summary>One editable device row: an optional alias and whether it shows on the widget.</summary>
public sealed class DeviceSetting : INotifyPropertyChanged
{
    bool _canUp, _canDown;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>False for the first row's up and the last row's down button.</summary>
    public bool CanUp { get => _canUp; set => Set(ref _canUp, value, nameof(CanUp)); }
    public bool CanDown { get => _canDown; set => Set(ref _canDown, value, nameof(CanDown)); }

    void Set(ref bool field, bool value, string name)
    {
        if (field == value)
            return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }

    public required string Id { get; init; }
    public required string OwnName { get; init; }
    public string Name { get; set; } = "";
    public bool OnWidget { get; set; } = true;
}

public sealed record SettingsResult(int Low, int Medium, int Critical, bool Autostart, bool Notifications,
    bool Blink, bool DualSenseFullReports, TrayStyle TrayStyle, ThemeMode Theme, WidgetStyle WidgetStyle, WidgetStyle PinnedWidgetStyle, bool CheckUpdates, IReadOnlyList<DeviceSetting> Devices, IReadOnlyList<string> DeviceOrder);

/// <summary>The only settings window: thresholds, tray icon, autostart, device names and widget visibility.</summary>
public partial class SettingsWindow : Window
{
    readonly AppConfig _config;
    readonly ObservableCollection<DeviceSetting> _devices;

    public event Action<SettingsResult>? Saved;

    public SettingsWindow(AppConfig config, IEnumerable<BatteryReading> devices, bool autostart)
    {
        InitializeComponent();
        _config = config;
        TitleText.Text = Text.Settings;
        LowLabel.Text = Text.LowThreshold;
        MediumLabel.Text = Text.MediumThreshold;
        CriticalLabel.Text = Text.CriticalThreshold;
        ThresholdsHint.Text = Text.ThresholdsHint;
        BlinkBox.Content = Text.BlinkCritical;
        BlinkBox.IsChecked = config.BlinkCritical;
        DualSenseBox.Content = Text.DualSenseFullReports;
        DualSenseHint.Text = Text.DualSenseFullReportsHint; // a warning, not a tooltip: keyboard and Narrator users see it too
        DualSenseBox.IsChecked = config.DualSenseFullReports;
        TrayHeader.Text = Text.TrayStyleLabel.ToUpperInvariant();
        TrayCombo.ItemsSource = Text.TrayStyles; // order of TrayStyle
        TrayCombo.SelectedIndex = (int)config.TrayStyle;
        ThemeHeader.Text = Text.ThemeLabel.ToUpperInvariant();
        ThemeCombo.ItemsSource = Text.Themes; // order of ThemeMode
        ThemeCombo.SelectedIndex = (int)config.Theme;
        FloatingHeader.Text = Text.FloatingWidgetLabel.ToUpperInvariant();
        FloatingCombo.ItemsSource = Text.WidgetStyles; // order of WidgetStyle
        FloatingCombo.SelectedIndex = (int)config.WidgetStyle;
        PinnedHeader.Text = Text.TaskbarWidgetLabel.ToUpperInvariant();
        PinnedCombo.ItemsSource = new[] { Text.WidgetStyles[(int)WidgetStyle.Horizontal], Text.WidgetStyles[(int)WidgetStyle.Compact] };
        PinnedCombo.SelectedIndex = config.PinnedWidgetStyle == WidgetStyle.Compact ? 1 : 0;
        AutostartBox.Content = Text.StartWithWindows;
        NotificationsBox.Content = Text.Notifications;
        NotificationsBox.IsChecked = config.Notifications;
        UpdatesBox.Content = Text.CheckUpdatesOnStart;
        UpdatesBox.IsChecked = config.CheckUpdates;
        OverlayTitle.Text = Text.Devices;
        DeviceColumn.Text = Text.DeviceColumn.ToUpperInvariant();
        OnWidgetColumn.Text = Text.OnWidget.ToUpperInvariant();
        SaveButton.Content = Text.Save;
        CancelButton.Content = Text.Cancel;

        LowBox.Text = config.LowThreshold.ToString();
        MediumBox.Text = config.MediumThreshold.ToString();
        CriticalBox.Text = config.CriticalThreshold.ToString();
        AutostartBox.IsChecked = autostart;
        _devices = new(Levels.ByUserOrder(devices, config.DeviceOrder).Select(d => new DeviceSetting
        {
            Id = d.Id,
            OwnName = d.Name,
            Name = config.DeviceNames.GetValueOrDefault(d.Id, ""),
            OnWidget = !config.HiddenDevices.Contains(d.Id),
        }));
        RefreshMoves();
        DeviceList.ItemsSource = _devices;
        DevicesButton.Content = Text.DevicesButton(_devices.Count);
        DevicesButton.IsEnabled = _devices.Count > 0;
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        // Unparsable input keeps the current value; ClampThresholds fixes out-of-range ones.
        var low = int.TryParse(LowBox.Text.Trim(), out var l) ? l : _config.LowThreshold;
        var medium = int.TryParse(MediumBox.Text.Trim(), out var m) ? m : _config.MediumThreshold;
        var critical = int.TryParse(CriticalBox.Text.Trim(), out var c) ? c : _config.CriticalThreshold;
        (low, medium) = AppConfig.ClampThresholds(low, medium);
        critical = AppConfig.ClampCritical(critical, low);
        var style = (TrayStyle)Math.Max(0, TrayCombo.SelectedIndex);
        var theme = (ThemeMode)Math.Max(0, ThemeCombo.SelectedIndex);
        var widgetStyle = (WidgetStyle)Math.Max(0, FloatingCombo.SelectedIndex);
        var pinnedStyle = PinnedCombo.SelectedIndex == 1 ? WidgetStyle.Compact : WidgetStyle.Horizontal;
        Saved?.Invoke(new SettingsResult(low, medium, critical, AutostartBox.IsChecked == true,
            NotificationsBox.IsChecked == true, BlinkBox.IsChecked == true, DualSenseBox.IsChecked == true,
            style, theme, widgetStyle, pinnedStyle, UpdatesBox.IsChecked == true, _devices, _devices.Select(d => d.Id).ToList()));
        Close();
    }

    void RefreshMoves()
    {
        for (var i = 0; i < _devices.Count; i++)
            (_devices[i].CanUp, _devices[i].CanDown) = (i > 0, i < _devices.Count - 1);
    }

    void OnMoveUp(object sender, RoutedEventArgs e) => Move(sender, up: true);

    void OnMoveDown(object sender, RoutedEventArgs e) => Move(sender, up: false);

    /// <summary>Moves a row and keeps the keyboard focus on the same button of the moved row.</summary>
    void Move(object sender, bool up)
    {
        var item = (DeviceSetting)((FrameworkElement)sender).DataContext;
        var from = _devices.IndexOf(item);
        var to = from + (up ? -1 : 1);
        if (to < 0 || to >= _devices.Count)
            return;
        _devices.Move(from, to);
        RefreshMoves();
        Dispatcher.BeginInvoke(() =>
        {
            if (DeviceList.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject row)
                return;
            var target = FindButton(row, up);
            // At the end of the list the pressed button is now disabled: focus its twin.
            if (target is not { IsEnabled: true })
                target = FindButton(row, !up);
            target?.Focus();
        }, DispatcherPriority.Loaded);
    }

    const string UpName = "UpButton", DownName = "DownButton"; // x:Name in SettingsWindow.xaml

    static Button? FindButton(DependencyObject parent, bool up)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button b && b.Name == (up ? UpName : DownName))
                return b;
            if (FindButton(child, up) is { } found)
                return found;
        }
        return null;
    }

    void OnOpenDevices(object sender, RoutedEventArgs e)
    {
        // The window must not grow for the card: the list scrolls within what is left.
        DeviceScroll.MaxHeight = Math.Max(120, Main.ActualHeight - 150);
        Main.IsEnabled = false; // keeps Tab and clicks out of the dimmed settings
        Overlay.Visibility = Visibility.Visible;
        OverlayClose.Focus();
    }

    void OnCloseDevices(object? sender, RoutedEventArgs? e)
    {
        Overlay.Visibility = Visibility.Collapsed;
        Main.IsEnabled = true;
        DevicesButton.Focus();
    }

    void OnOverlayClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == Overlay)
            OnCloseDevices(null, null);
    }

    /// <summary>Esc closes the device card first; the Cancel button's IsCancel would close the window.</summary>
    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Overlay.Visibility != Visibility.Visible)
            return;
        e.Handled = true;
        OnCloseDevices(null, null);
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();

    void OnHeaderDrag(object sender, MouseButtonEventArgs e) => DragMove();
}
