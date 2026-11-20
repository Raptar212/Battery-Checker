# Battery Checker

[Русский](#русский)

A small Windows utility that shows the battery level of your wireless devices — mice, keyboards,
headsets, earbuds, gamepads — in one place: a widget by the taskbar (or pinned into it), a panel on
click, and a tray icon. Including devices whose battery Windows itself does not show.

## Supported devices

Tested devices and connections — [docs/DEVICES.md](docs/DEVICES.md).

## Install

1. Download `BatteryChecker-Setup-X.Y.Z.exe` from [Releases](https://github.com/Raptar212/Battery-Checker/releases/latest).
2. Run it. Windows SmartScreen may warn about an unknown publisher — the installer is not code-signed
   (signing costs money; the source code is all here). Click **More info → Run anyway**.
3. Pick the options (start with Windows, desktop shortcut) and finish.

Requirements: Windows 10 version 2004 or later, or Windows 11; x64. No administrator rights; .NET is included.
The app installs to `%LOCALAPPDATA%\Programs\Battery Checker`.

## Use

- **Click** the widget or the tray icon — the device list. A second click closes it.
- **Right-click** the tray icon — menu: hide the widget, pin it into the taskbar (Windows 10 taskbar only),
  scan devices, settings, check for updates.
- In the list: the **eye** hides a device from the widget, the **pin** chooses what the tray icon shows.
- **Settings:** battery thresholds and colors, notifications, tray icon style, light/dark theme (follows
  Windows by default), widget style (horizontal, vertical, compact, tiles with rings, with names; horizontal
  or compact when pinned), start with Windows, device names, order and which ones the widget shows.
- Works with several monitors: sharp at any scale, pins into the taskbar of the widget's monitor, and
  moves to the nearest monitor when its own is disconnected.

The interface is in Russian on Russian Windows and in English otherwise.

## Privacy and network

The app reads battery levels locally. The only network request it makes is to GitHub, to check for a
newer release: once, a minute after start (can be turned off in Settings), or by hand from the tray menu.
An update is downloaded only after you agree, and installed only if its SHA-256 matches the release.

## Files and uninstall

- Settings: `%LOCALAPPDATA%\BattaryChecker\config.json`
- Crash log (attach it to a bug report): `%LOCALAPPDATA%\BattaryChecker\crash.log`
- Uninstall: *Settings → Apps → Battery Checker*. Settings stay in `%LOCALAPPDATA%\BattaryChecker` —
  delete that folder for a full cleanup.

(`Battary` with an "a" in these paths is intentional: renaming them would lose existing users' settings.)

## Build from source

Requires the .NET 10 SDK on Windows 10 2004+.

```sh
dotnet build
dotnet test
installer\build.ps1   # installer → artifacts\BatteryChecker-Setup-<version>.exe (needs Inno Setup 6)
```

How each device is read is documented in [docs/PROTOCOLS.md](docs/PROTOCOLS.md).

## License

[MIT](LICENSE)

---

## Русский

Небольшая утилита для Windows: заряд беспроводных устройств — мышей, клавиатур, гарнитур, наушников,
геймпадов — в одном месте: виджет у панели задач (или внутри неё), панель по клику и иконка в трее.
В том числе устройств, чей заряд Windows сама не показывает.

### Какие устройства поддерживаются

Проверенные устройства и способы подключения — [docs/DEVICES.md](docs/DEVICES.md).

### Установка

1. Скачайте `BatteryChecker-Setup-X.Y.Z.exe` со страницы [Releases](https://github.com/Raptar212/Battery-Checker/releases/latest).
2. Запустите. Windows SmartScreen может предупредить о неизвестном издателе — установщик не подписан
   (подпись платная, весь исходный код открыт здесь). Нажмите **Подробнее → Выполнить в любом случае**.
3. Выберите опции (запуск вместе с Windows, ярлык на рабочем столе) и завершите установку.

Требования: Windows 10 версии 2004 и новее или Windows 11; x64. Права администратора не нужны, .NET внутри.
Программа ставится в `%LOCALAPPDATA%\Programs\Battery Checker`.

### Как пользоваться

- **Клик** по виджету или значку в трее — список устройств. Повторный клик закрывает его.
- **Правый клик** по значку в трее — меню: скрыть виджет, закрепить его на панели задач (только панель
  задач Windows 10), сканировать устройства, настройки, проверить обновления.
- В списке: **глаз** скрывает устройство с виджета, **скрепка** выбирает, что показывает иконка в трее.
- **Настройки:** пороги заряда и цвета, уведомления, вид иконки в трее, светлая/тёмная тема (по умолчанию
  как в Windows), вид виджета (горизонтальный, вертикальный, компактный, плитка с кольцами, с именами; в
  панели задач — горизонтальный или компактный), автозапуск, имена устройств, их порядок и что видно на виджете.
- Несколько мониторов: чётко при любом масштабе, закрепление в панели задач монитора виджета, при
  отключении монитора виджет переходит на ближайший.

Интерфейс на русском, если Windows на русском, иначе на английском.

### Приватность и сеть

Заряд читается локально. Единственный сетевой запрос — к GitHub, проверить, нет ли новой версии:
один раз через минуту после запуска (отключается в настройках) или вручную из меню трея. Обновление
скачивается только после вашего согласия и ставится, только если его SHA-256 совпадает с релизом.

### Файлы и удаление

- Настройки: `%LOCALAPPDATA%\BattaryChecker\config.json`
- Журнал падений (приложите к сообщению об ошибке): `%LOCALAPPDATA%\BattaryChecker\crash.log`
- Удаление: *Параметры → Приложения → Battery Checker*. Настройки остаются в `%LOCALAPPDATA%\BattaryChecker` —
  удалите папку для полной очистки.

(`Battary` через «a» в путях оставлено намеренно: переименование потеряет настройки у тех, кто уже
пользуется программой.)

### Сборка из исходников

Нужен .NET 10 SDK на Windows 10 2004+; команды — в английском разделе выше.

### Лицензия

[MIT](LICENSE)
