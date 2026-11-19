using System.Windows;
using System.Windows.Input;
using BattaryChecker.Devices;

namespace BattaryChecker.UI;

/// <summary>One editable device row: an optional alias and whether it shows on the widget.</summary>
public sealed class DeviceSetting
{
    public required string Id { get; init; }
    public required string OwnName { get; init; }
    public string Name { get; set; } = "";
    public bool OnWidget { get; set; } = true;
}

public sealed record SettingsResult(int Low, int Medium, int Critical, bool Autostart, bool Notifications,
    bool Blink, bool DualSenseFullReports, TrayStyle TrayStyle, bool CheckUpdates, IReadOnlyList<DeviceSetting> Devices);

/// <summary>The only settings window: thresholds, tray icon, autostart, device names and widget visibility.</summary>
public partial class SettingsWindow : Window
{
    readonly AppConfig _config;
    readonly List<DeviceSetting> _devices;
    readonly System.Windows.Controls.RadioButton[] _styles;

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
        _styles = [BarsRadio, RingRadio, NumberRadio]; // order of TrayStyle
        for (var i = 0; i < _styles.Length; i++)
        {
            _styles[i].Content = Text.TrayStyles[i];
            _styles[i].IsChecked = (int)config.TrayStyle == i;
        }
        AutostartBox.Content = Text.StartWithWindows;
        NotificationsBox.Content = Text.Notifications;
        NotificationsBox.IsChecked = config.Notifications;
        UpdatesBox.Content = Text.CheckUpdatesOnStart;
        UpdatesBox.IsChecked = config.CheckUpdates;
        DevicesHeader.Text = Text.Devices;
        SaveButton.Content = Text.Save;
        CancelButton.Content = Text.Cancel;

        LowBox.Text = config.LowThreshold.ToString();
        MediumBox.Text = config.MediumThreshold.ToString();
        CriticalBox.Text = config.CriticalThreshold.ToString();
        AutostartBox.IsChecked = autostart;
        _devices = Levels.Ordered(devices).Select(d => new DeviceSetting
        {
            Id = d.Id,
            OwnName = d.Name,
            Name = config.DeviceNames.GetValueOrDefault(d.Id, ""),
            OnWidget = !config.HiddenDevices.Contains(d.Id),
        }).ToList();
        DeviceList.ItemsSource = _devices;
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        // Unparsable input keeps the current value; ClampThresholds fixes out-of-range ones.
        var low = int.TryParse(LowBox.Text.Trim(), out var l) ? l : _config.LowThreshold;
        var medium = int.TryParse(MediumBox.Text.Trim(), out var m) ? m : _config.MediumThreshold;
        var critical = int.TryParse(CriticalBox.Text.Trim(), out var c) ? c : _config.CriticalThreshold;
        (low, medium) = AppConfig.ClampThresholds(low, medium);
        critical = AppConfig.ClampCritical(critical, low);
        var style = (TrayStyle)Math.Max(0, Array.FindIndex(_styles, r => r.IsChecked == true));
        Saved?.Invoke(new SettingsResult(low, medium, critical, AutostartBox.IsChecked == true,
            NotificationsBox.IsChecked == true, BlinkBox.IsChecked == true, DualSenseBox.IsChecked == true,
            style, UpdatesBox.IsChecked == true, _devices));
        Close();
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();

    void OnHeaderDrag(object sender, MouseButtonEventArgs e) => DragMove();
}
