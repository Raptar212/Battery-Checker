# Supported devices

[Русский](#поддерживаемые-устройства)

| Device | Connection | Status |
|---|---|---|
| Any Bluetooth device whose battery Windows already reads (headsets and earbuds over Hands-Free, BLE keyboards and mice) | Bluetooth Classic / LE | ✅ works |
| Logitech G602 | USB receiver `046D:C537` | ✅ tested (the mouse only reports 100 / 50 / 20 / 5 %) |
| Logitech G502 X Plus | Lightspeed receiver `046D:C547` | ✅ tested |
| Other Logitech HID++ 2.0 devices | Lightspeed / Unifying / Bolt receiver | ⚠️ should work, not tested |
| Astro A50 Gen4 | USB base station `9886:002C` | ✅ tested |
| Sony DualSense | USB | ✅ tested |
| Sony DualSense | Bluetooth | ✅ tested, after the checkbox in Settings (see the warning there) |
| Keychron Q6 Max | Bluetooth LE | ✅ tested |
| Keychron keyboards | Keychron Link receiver `3434:D030` | ❌ the receiver does not report battery |
| Gtheos Captain 300 | Bluetooth | ✅ tested |
| Gtheos Captain 300 | 2.4 GHz dongle `4C4A:4155` | ❌ the dongle does not report battery — connect over Bluetooth |
| Older Astro A50, DualSense Edge, DualShock 4, Logitech HID++ 1.0 | — | ❌ not supported |

Your device works with another setup? Something is missing? [Open an issue](https://github.com/Raptar212/Battery-Checker/issues)
with the device model and how it is connected. How each device is read — [PROTOCOLS.md](PROTOCOLS.md).

## Поддерживаемые устройства

| Устройство | Подключение | Статус |
|---|---|---|
| Любое Bluetooth-устройство, у которого Windows сама видит заряд (гарнитуры и наушники через Hands-Free, BLE-клавиатуры и мыши) | Bluetooth Classic / LE | ✅ работает |
| Logitech G602 | USB-приёмник `046D:C537` | ✅ проверено (мышь отдаёт только 100 / 50 / 20 / 5 %) |
| Logitech G502 X Plus | приёмник Lightspeed `046D:C547` | ✅ проверено |
| Другие устройства Logitech HID++ 2.0 | приёмник Lightspeed / Unifying / Bolt | ⚠️ должно работать, не проверено |
| Astro A50 Gen4 | USB-база `9886:002C` | ✅ проверено |
| Sony DualSense | USB | ✅ проверено |
| Sony DualSense | Bluetooth | ✅ проверено, после галочки в настройках (см. предупреждение там) |
| Keychron Q6 Max | Bluetooth LE | ✅ проверено |
| Клавиатуры Keychron | приёмник Keychron Link `3434:D030` | ❌ приёмник не отдаёт заряд |
| Gtheos Captain 300 | Bluetooth | ✅ проверено |
| Gtheos Captain 300 | 2,4 ГГц донгл `4C4A:4155` | ❌ донгл не отдаёт заряд — подключайте по Bluetooth |
| Старые Astro A50, DualSense Edge, DualShock 4, Logitech HID++ 1.0 | — | ❌ не поддерживаются |

Ваше устройство работает в другой связке или его нет в списке? [Создайте issue](https://github.com/Raptar212/Battery-Checker/issues)
с моделью устройства и способом подключения. Как читается каждое устройство — [PROTOCOLS.md](PROTOCOLS.md).
