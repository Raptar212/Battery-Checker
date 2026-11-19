"""Read G602 battery through the Logitech receiver (046D:C537) via HID++. Read-only queries.

HID++ short report: 0x10, 7 bytes (Col04, usage page FF00/1); long: 0x11, 20 bytes (Col05, FF00/2).
"""
import sys
import time

import hid

VID, PID = 0x046D, 0xC537
SWID = 0x0A  # our software id, so we ignore G HUB's traffic
FEATURES = {0x1000: "BATTERY_STATUS", 0x1001: "BATTERY_VOLTAGE", 0x1004: "UNIFIED_BATTERY"}


def open_collections():
    short = long_ = None
    for d in hid.enumerate(VID, PID):
        if d["usage_page"] == 0xFF00 and d["usage"] == 1:
            short = hid.device(); short.open_path(d["path"])
        if d["usage_page"] == 0xFF00 and d["usage"] == 2:
            long_ = hid.device(); long_.open_path(d["path"])
    if not (short and long_):
        sys.exit("HID++ collections not found")
    for h in (short, long_):
        h.set_nonblocking(True)
    return short, long_


def request(short, long_, idx, sub, addr, params=b"", timeout=1.0):
    """Send short request; return first matching reply (short or long), or None."""
    msg = bytes([0x10, idx, sub, addr]) + params.ljust(3, b"\0")[:3]
    short.write(msg)
    end = time.time() + timeout
    while time.time() < end:
        for h in (long_, short):
            r = h.read(20)
            if not r or r[1] != idx:
                continue
            if r[2] == sub and r[3] == addr:  # direct answer
                return bytes(r)
            if r[2] in (0x8F, 0xFF) and r[3] == sub and r[4] == addr:  # error answer
                return bytes(r)
        time.sleep(0.005)
    return None


def hidpp20(short, long_, idx):
    found = False
    for fid, name in FEATURES.items():
        r = request(short, long_, idx, 0x00, 0x00 | SWID, fid.to_bytes(2, "big"))  # root.getFeature
        if not r or r[2] == 0xFF or r[4] == 0:
            continue
        fidx = r[4]
        found = True
        func = 0x10 if fid == 0x1004 else 0x00  # unified: get_status is fn 1
        r = request(short, long_, idx, fidx, func | SWID)
        print(f"  {name} (index {fidx}): raw={r.hex(' ') if r else None}")
        if not r:
            continue
        p = r[4:]
        if fid == 0x1000:
            print(f"    level={p[0]}% next={p[1]}% status={p[2]} (0=discharging,1..=charging states)")
        elif fid == 0x1001:
            print(f"    voltage={int.from_bytes(p[0:2], 'big')} mV flags=0x{p[2]:02X}")
        elif fid == 0x1004:
            print(f"    soc={p[0]}% level_mask=0x{p[1]:02X} charging={p[2]} ext_power={p[3]}")
    if not found:
        print("  no battery feature found")


def hidpp10(short, long_, idx):
    for reg, name in ((0x0D, "battery charge"), (0x07, "battery status")):
        r = request(short, long_, idx, 0x81, reg)  # get short register
        print(f"  reg 0x{reg:02X} {name}: raw={r.hex(' ') if r else None}")


def main():
    short, long_ = open_collections()
    for idx in (0x01, 0x02, 0x03, 0xFF):
        r = request(short, long_, idx, 0x00, 0x10 | SWID, b"\0\0\x5a")  # root.getProtocolVersion
        if r is None:
            print(f"idx 0x{idx:02X}: no reply")
            continue
        if r[2] == 0x8F:
            print(f"idx 0x{idx:02X}: HID++ 1.0 (error 0x{r[5]:02X}) raw={r.hex(' ')}")
            hidpp10(short, long_, idx)
        else:
            print(f"idx 0x{idx:02X}: HID++ {r[4]}.{r[5]} raw={r.hex(' ')}")
            hidpp20(short, long_, idx)


if __name__ == "__main__":
    main()
