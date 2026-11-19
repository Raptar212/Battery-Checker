# Пробы протоколов (этап 0)

Эталонные скрипты чтения заряда, проверенные на устройствах пользователя 2026-11-16.
Только чтение, в устройства ничего не пишется. Использовались как образец для источников в `src/BattaryChecker/Devices/`
и для быстрой проверки «железо отвечает?».

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install hidapi
.\.venv\Scripts\python.exe -I list_hid.py          # HID-интерфейсы Logitech/Astro/Sony
.\.venv\Scripts\python.exe -I probe_g602.py        # Logitech HID++ через приёмник C537
.\.venv\Scripts\python.exe -I probe_a50.py         # Astro A50 Gen4 через базу
.\.venv\Scripts\python.exe -I probe_dualsense.py   # DualSense; --enable-extended — см. docstring
.\.venv\Scripts\python.exe -I probe_keychron.py     # приёмник Keychron Link (заряда нет)
dotnet run ble.cs                                  # Bluetooth LE: PnP-свойство и GATT
```

Детали протоколов — в `docs/PROTOCOLS.md`.
