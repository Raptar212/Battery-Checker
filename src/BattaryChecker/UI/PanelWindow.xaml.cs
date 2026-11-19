using System.Windows;
using System.Windows.Controls;

namespace BattaryChecker.UI;

/// <summary>Device list opened by a click on the widget or the tray icon; hides when it loses focus.</summary>
public partial class PanelWindow : Window
{
    const double Gap = 8;
    bool _showDisconnected = true;
    int _disconnectedCount;

    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action<string>? ToggleHiddenRequested;
    public event Action<string>? ToggleTrayRequested;

    public PanelWindow()
    {
        InitializeComponent();
        TitleText.Text = Text.AppName;
        ConnectedHeader.Text = Text.ConnectedHeader;
        NoDevicesTitle.Text = Text.NoDevices;
        NoDevicesHint.Text = Text.NoDevicesHint;
        foreach (var (button, name) in new[] { (RefreshButton, Text.Refresh), (SettingsButton, Text.Settings), (CloseButton, Text.Close) })
        {
            button.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(button, name); // the glyph itself reads as nothing
        }
    }

    DateTime _hiddenAt;

    /// <summary>
    /// Hidden by losing focus a moment ago: the click on the widget or tray icon that took the focus is the
    /// same click that would open it again, so the toggle must treat it as "close".
    /// </summary>
    public bool JustHidden => DateTime.UtcNow - _hiddenAt < TimeSpan.FromMilliseconds(400);

    public void Update(IReadOnlyList<DeviceView> devices)
    {
        var connected = devices.Where(d => d.Connected).ToList();
        var disconnected = devices.Where(d => !d.Connected).ToList();
        _disconnectedCount = disconnected.Count;

        Summary.Text = Text.ConnectedCount(connected.Count);
        ConnectedList.ItemsSource = connected;
        NoDevices.Visibility = connected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DisconnectedList.ItemsSource = disconnected;
        RenderDisconnectedToggle();
    }

    /// <summary>Shows above the widget, centred on it, on the widget's monitor; else at the bottom-right of the primary one.</summary>
    public void ShowNear(Window? anchor)
    {
        Show();
        UpdateLayout();
        var area = anchor is null ? SystemParameters.WorkArea : ScreenArea.Of(anchor);
        if (anchor is null)
        {
            Left = area.Right - ActualWidth - Gap;
            Top = area.Bottom - ActualHeight - Gap;
        }
        else
        {
            var centred = anchor.Left + (anchor.ActualWidth - ActualWidth) / 2;
            Left = Math.Clamp(centred, area.Left, Math.Max(area.Left, area.Right - ActualWidth - Gap));
            Top = anchor.Top - ActualHeight - Gap;
            if (Top < area.Top)
                Top = anchor.Top + anchor.ActualHeight + Gap; // widget near the top edge: open below
        }
        Activate();
    }

    void RenderDisconnectedToggle()
    {
        var visible = _disconnectedCount > 0;
        DisconnectedToggle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DisconnectedToggle.Content = $"{(_showDisconnected ? "▾" : "▸")} {Text.DisconnectedHeader} ({_disconnectedCount})";
        DisconnectedList.Visibility = visible && _showDisconnected ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnToggleDisconnected(object sender, RoutedEventArgs e)
    {
        _showDisconnected = !_showDisconnected;
        RenderDisconnectedToggle();
    }

    void OnEye(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
            ToggleHiddenRequested?.Invoke(id);
    }

    void OnTray(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
            ToggleTrayRequested?.Invoke(id);
    }

    void OnRefresh(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

    void OnSettings(object sender, RoutedEventArgs e)
    {
        Hide();
        SettingsRequested?.Invoke();
    }

    void OnClose(object sender, RoutedEventArgs e) => Hide();

    void OnDeactivated(object? sender, EventArgs e)
    {
        if (IsVisible)
            _hiddenAt = DateTime.UtcNow;
        Hide();
    }

    void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
            Hide();
    }
}
