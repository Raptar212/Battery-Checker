# Battery Checker

Лёгкая утилита для Windows: заряд беспроводных устройств — **виджет** у панели задач, **панель**
по клику, **иконка в трее**. Минимум настроек, без вкладок.
Пользователь пишет по-русски, отвечать по-русски. План и история этапов — [`docs/PLAN.md`](docs/PLAN.md).

## Стек и команды

C# / .NET 10 WPF (`net10.0-windows10.0.19041.0`), WinForms только для `NotifyIcon`, тесты xUnit.
Внешних NuGet-зависимостей в приложении нет.

```sh
dotnet build -warnaserror        # предупреждения = ошибки
dotnet test                      # unit-тесты (без железа)
dotnet format --verify-no-changes
dotnet run research/probes/read_all.cs   # все источники разом на реальном железе
dotnet publish src/BattaryChecker -p:PublishProfile=win-x64   # exe → dist/ (нужен .NET 10 Desktop Runtime)
installer\build.ps1              # установщик → artifacts\BatteryChecker-Setup-<версия>.exe (Inno Setup 6, .NET внутри)
```

Видимое имя — «Battery Checker» (установщик, ярлыки, окна). Внутренние имена с опечаткой
`BattaryChecker` (exe, `%LOCALAPPDATA%\BattaryChecker`, значение `Run`) **не менять**: потеряются
настройки и автозапуск у установленных копий. Версия — `<Version>` в csproj.

**Релизы GitHub:** тег `vX.Y.Z`, ассет — `BatteryChecker-Setup-X.Y.Z.exe` (проверка обновлений в
приложении ищет ровно это имя в `releases/latest` и сверяет его `sha256`-digest). Держать не больше
двух релизов: после публикации третьего самый старый удаляется вместе с тегом
(`gh release delete <тег> --cleanup-tag`).

## Карта кода (`src/BattaryChecker/`)

| Файл | Что |
|---|---|
| `Devices/Protocols.cs` | Чистые парсеры: HID++ 2.0, Astro A50, DualSense |
| `Devices/HidChannel.cs` | Открытие HID-коллекции: поиск через WinRT, I/O через Win32 (зона риска) |
| `Devices/*Source.cs` | Источники заряда: Logitech, Astro A50, DualSense, Bluetooth (Classic + LE) |
| `Devices/DevicePoller.cs` | Опрос всех источников раз в 30 с; сбой одного не роняет остальные |
| `Devices/BatteryReading.cs` | Модель, `IBatterySource`, `Presence` (5 мин «сна» до «отключено») |
| `UI/*` | Виджет, панель, трей, настройки, `DeviceView`/`Levels` (правила отображения), `Text` (все строки) |
| `AppConfig.cs` | `%LOCALAPPDATA%\BattaryChecker\config.json`; битый файл → `.bad`, не затирается |
| `Autostart.cs` | `HKCU\…\Run`, источник истины — реестр |
| `Notifier.cs` | Когда уведомлять: низкий (cooldown 60 мин), полный (только после зарядки) |
| `UpdateCheck.cs` / `Updater.cs` | Проверка обновлений: разбор `releases/latest` / сеть, скачивание с SHA-256, запуск установщика |

## Проверено на железе

| Устройство | Подключение | Источник |
|---|---|---|
| Logitech G602 | USB-приёмник `046D:C537` | HID++ 2.0, `0x1000` BATTERY_STATUS (ступени 100/50/20/5) |
| Astro A50 Gen4 | USB-база `9886:002C` | вендорный HID `0xFF32` |
| DualSense | USB / Bluetooth `054C:0CE6` | входные отчёты, пассивно |
| Наушники Bluetooth Classic | Hands-Free | `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` на узле «Hands-Free AG» |
| Keychron Q6 Max | Bluetooth LE (`3434:0860`) | то же свойство на узле `BTHLE\Dev_<адрес>`; Appearance 15/1 |

Не поддерживается: Keychron через приёмник Link `3434:D030` (заряд не отдаёт, см. `docs/PROTOCOLS.md`).

## Протоколы и грабли

Всё проверено на железе — [`docs/PROTOCOLS.md`](docs/PROTOCOLS.md): HID в целом, Logitech HID++,
Astro A50, DualSense, Bluetooth, Keychron Link. **Читать перед правкой `Devices/`.**

## Решения, которые не пересматривать без причины
- Свой код на C#, а не форк BlueGauge (Rust): апстрим не собирается с публичным `tray-icon`
  (нужен незамёрженный PR #302).
- Идеи интерфейса подсмотрены у закрытого приложения из Microsoft Store — **только идеи UX**:
  его код и ассеты не декомпилировать и не копировать.
- WPF рисует программно (`RenderMode.SoftwareOnly`): иначе драйвер видеокарты и Direct3D
  добавляют ~45 МБ своей памяти. Своя память release ≈ 60 МБ.

## Правила
- Код и комментарии — на английском; строки интерфейса — только в `UI/Text.cs`.
- `dotnet format`, `dotnet build -warnaserror` и `dotnet test` зелёные перед каждым PR.
- Ветки `BC-NN`, PR в `main`, заголовок `BC-NN: <что изменилось для пользователя>`.
