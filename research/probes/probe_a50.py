"""Read Astro A50 Gen4 battery via the base station (9886:002C, HID usage page 0xFF32). Read-only.

Protocol per eh-fifty (github.com/tdryer/eh-fifty): request [0x02, cmd], reply [0x02, status, len, data...].
"""
import sys
import time

import hid

VID, PID = 0x9886, 0x002C
GET_HEADSET_STATUS, GET_BATTERY_STATUS = 0x54, 0x7C
STATUS = {0: "no-response", 1: "error", 2: "ok"}


def open_dev():
    for d in hid.enumerate(VID, PID):
        if d["usage_page"] == 0xFF32:
            h = hid.device(); h.open_path(d["path"])
            return h
    sys.exit("A50 vendor interface not found")


def request(h, cmd, timeout_ms=1000):
    h.write(bytes([0x02, cmd]).ljust(64, b"\0"))
    end = time.time() + timeout_ms / 1000
    while time.time() < end:
        r = h.read(64, 100)
        if r and r[0] == 0x02:
            data = bytes(r[3 : 3 + min(r[2], len(r) - 3)])
            return STATUS.get(r[1], r[1]), data, bytes(r[:12])
    return None, b"", b""


def main():
    h = open_dev()
    st, data, raw = request(h, GET_HEADSET_STATUS)
    print(f"headset status: {st} raw={raw.hex(' ')}")
    if st == "ok" and data:
        print(f"  docked={bool(data[0] & 1)} on={bool(data[0] & 2)}")
    st, data, raw = request(h, GET_BATTERY_STATUS)
    print(f"battery: {st} raw={raw.hex(' ')}")
    if st == "ok" and data:
        print(f"  charge={data[0] & 0x7F}% charging={bool(data[0] & 0x80)}")


if __name__ == "__main__":
    main()
