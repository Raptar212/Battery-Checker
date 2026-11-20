using System.Globalization;

namespace BattaryChecker.UI;

/// <summary>
/// All user-visible strings in one place: Russian when Windows' display language is Russian, English otherwise.
/// Chosen once at start; there is no language setting.
/// </summary>
public static class Text
{
    /// <summary>Settable for tests only; the app never changes it after start.</summary>
    public static bool Russian { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    static string T(string ru, string en) => Russian ? ru : en;

    public static string AppName => "Battery Checker";
    public static string Connected => T("Подключено", "Connected");
    public static string Disconnected => T("Отключено", "Disconnected");
    public static string ConnectedHeader => T("ПОДКЛЮЧЁННЫЕ", "CONNECTED");
    public static string DisconnectedHeader => T("ОТКЛЮЧЁННЫЕ", "DISCONNECTED");
    public static string NoDevices => T("Нет устройств с данными о заряде", "No devices reporting a battery level");
    public static string NoDevicesHint => T(
        "Показываются: Bluetooth-устройства, у которых Windows сама видит заряд; "
        + "Logitech через USB-приёмник (HID++ 2.0); Astro A50 Gen4; DualSense по USB, по Bluetooth — "
        + "после галочки в настройках. Подключите устройство — оно появится в течение пары секунд.",
        "Shown: Bluetooth devices whose battery Windows already reads; Logitech through a USB receiver (HID++ 2.0); "
        + "Astro A50 Gen4; DualSense over USB, and over Bluetooth after the checkbox in Settings. "
        + "Connect a device and it appears within a couple of seconds.");
    public static string Welcome => T(
        "Battery Checker работает в трее. Клик по виджету или значку — список устройств, правый клик по значку — меню.",
        "Battery Checker runs in the tray. Click the widget or the icon for your devices, right-click the icon for the menu.");
    public static string Close => T("Закрыть", "Close");
    public static string ShowWidget => T("Показать виджет", "Show widget");
    public static string HideWidget => T("Скрыть виджет", "Hide widget");
    public static string Refresh => T("Сканировать устройства", "Scan devices");
    public static string Exit => T("Выход", "Exit");
    public static string Hide => T("Скрыть с виджета", "Hide from widget");
    public static string Show => T("Показать на виджете", "Show on widget");
    public static string Settings => T("Настройки", "Settings");
    public static string LowThreshold => T("Низкий заряд, % (5–50)", "Low battery, % (5–50)");
    public static string MediumThreshold => T("Средний заряд, % (до 95)", "Medium battery, % (up to 95)");
    public static string CriticalThreshold => T("Критический заряд, % (ниже низкого)", "Critical battery, % (below low)");
    public static string ThresholdsHint => T(
        "Цвет: выше среднего — зелёный, до среднего — жёлтый, до низкого — красный. "
        + "На критическом и ниже иконка в трее мигает.",
        "Color — above medium: green; up to medium: yellow; up to low: red. "
        + "At critical and below, the tray icon blinks.");
    public static string BlinkCritical => T("Мигать иконкой в трее при критическом заряде", "Blink the tray icon at critical battery");
    public static string DualSenseFullReports => T("Показывать заряд DualSense по Bluetooth", "Show DualSense battery over Bluetooth");
    public static string DualSenseFullReportsHint => T(
        "Переключает геймпад в полный режим, как Steam. Игры без Steam Input, читающие геймпад напрямую, "
        + "могут перестать его видеть — тогда выключите и переподключите геймпад.",
        "Switches the controller to full mode, as Steam does. Games without Steam Input that read the controller "
        + "directly may stop seeing it — then turn this off and reconnect the controller.");
    public static string ThemeLabel => T("Тема", "Theme");
    public static string[] Themes => Russian ? ["Системная", "Тёмная", "Светлая"] : ["System", "Dark", "Light"]; // order of ThemeMode
    public static string FloatingWidgetLabel => T("Свободный виджет", "Floating widget");
    public static string TaskbarWidgetLabel => T("В панели задач", "In the taskbar");
    public static string[] WidgetStyles => Russian
        ? ["Горизонтальный", "Вертикальный", "Компактный", "Плитка", "С именами"]
        : ["Horizontal", "Vertical", "Compact", "Tiles", "With names"]; // order of WidgetStyle
    public static string TrayStyleLabel => T("Иконка в трее", "Tray icon");
    public static string[] TrayStyles => Russian ? ["Столбики", "Кольца", "Число"] : ["Bars", "Rings", "Number"]; // order of AppConfig.TrayStyle
    public static string AddToTray => T("Показывать в трее", "Show in the tray");
    public static string RemoveFromTray => T("Убрать из трея", "Remove from the tray");
    public static string PinToTaskbar => T("Закрепить на панели задач", "Pin to taskbar");
    public static string StartWithWindows => T("Запускать вместе с Windows", "Start with Windows");
    public static string Devices => T("Устройства", "Devices");
    public static string DevicesButton(int n) => T($"Устройства ({n})", $"Devices ({n})");
    public static string DeviceColumn => T("Устройство", "Device");
    public static string OnWidget => T("На виджете", "On widget");
    public static string Notifications => T("Уведомлять о низком и полном заряде", "Notify about low and full battery");
    public static string LowBatteryTitle => T("Низкий заряд", "Low battery");
    public static string FullBatteryTitle => T("Заряжено", "Charged");
    public static string Save => T("Сохранить", "Save");
    public static string Cancel => T("Отмена", "Cancel");

    public static string CheckUpdatesMenu => T("Проверить обновления", "Check for updates");
    public static string CheckUpdatesOnStart => T("Проверять обновления при запуске", "Check for updates on start");
    public static string UpdateInProgress => T("Обновление уже проверяется или скачивается.", "An update is already being checked or downloaded.");
    public static string UpdateHashMismatch => T("скачанный файл не совпадает с опубликованным (SHA-256)",
        "the downloaded file does not match the published one (SHA-256)");
    public static string UpdateNotStarted => T("установщик не запустился", "the installer did not start");
    public static string UpdateDownloaded => T("Установщик скачан. Закройте настройки и нажмите здесь, чтобы обновить.",
        "Installer downloaded. Close Settings and click here to update.");
    public static string GitHubTimeout => T("GitHub не ответил вовремя", "GitHub did not answer in time");
    public static string GitHubUnreachable => T("нет связи с GitHub", "no connection to GitHub");
    public static string AutostartFailed => T("Windows не разрешила изменить автозапуск (ограничение политики).",
        "Windows did not allow changing autostart (a policy restriction).");

    public static string ConnectedCount(int n) => T($"Подключено устройств: {n}", $"Connected devices: {n}");
    public static string AboutMenu(string version) => T($"Battery Checker {version} — страница на GitHub", $"Battery Checker {version} — GitHub page");
    public static string MoreDevices(int n) => $"+{n}";
    public static string UpdateNoInstaller(string version) => T($"у версии {version} нет установщика с SHA-256",
        $"version {version} has no installer with a SHA-256");
    public static string GitHubStatus(int code) => T($"GitHub ответил {code}", $"GitHub answered {code}");
    public static string UpdateCheckFailed(string reason) => T($"Не удалось проверить обновления: {reason}.", $"Could not check for updates: {reason}.");
    public static string UpdateFailed(string reason) => T($"Обновление не установлено: {reason}.", $"Update not installed: {reason}.");
    public static string UpToDate(string version) => T($"Установлена последняя версия ({version}).", $"You have the latest version ({version}).");
    public static string UpdateAvailable(string version) => T($"Доступна версия {version} — нажмите, чтобы обновить.",
        $"Version {version} is available — click to update.");
    public static string UpdateDownloading(string version) => T($"Скачивается версия {version}…", $"Downloading version {version}…");
    public static string UpdateQuestion(string latest, string current) => T(
        $"Доступна новая версия {latest} (установлена {current}).\n\n"
        + "«Да» скачает установщик и запустит его, программа закроется. Настройки сохранятся.",
        $"A new version {latest} is available (installed: {current}).\n\n"
        + "\"Yes\" downloads the installer and starts it; the app closes. Your settings are kept.");
    public static string LowBatteryMessage(string name, int percent) => $"{name}: {percent}%";
    public static string FullBatteryMessage(string name) => $"{name}: 100%";
}
