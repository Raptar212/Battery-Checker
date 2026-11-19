"""Read DualSense battery from its HID input reports (054C:0CE6). Passive by default.

Layout per Linux hid-playstation: status byte at offset 52 of the common input struct.
  BT  report 0x31: struct starts at byte 2 -> status at [54]
  USB report 0x01 (64 bytes): struct starts at byte 1 -> status at [53]
Status: low nibble = level 0..10 (x10%), high nibble = 0 discharging, 1 charging, 2 full.
Over BT the pad sends short 0x01 reports (no battery) until feature report 0x05 is read;
--enable-extended does that, which may confuse DirectInput games until the pad reconnects.
"""
import sys
import time

import hid

VID, PID = 0x054C, 0x0CE6
CHARGE = {0: "discharging", 1: "charging", 2: "full"}


def parse(r):
    if r[0] == 0x31 and len(r) > 54:
        status = r[54]
    elif r[0] == 0x01 and len(r) >= 64:  # USB full report
        status = r[53]
    else:
        return None
    level = min((status & 0x0F) * 10 + 5, 100)
    return level, CHARGE.get(status >> 4, f"0x{status >> 4:X}")


def main():
    devs = hid.enumerate(VID, PID)
    if not devs:
        sys.exit("DualSense not found: turn it on (PS button) and run again")
    h = hid.device(); h.open_path(devs[0]["path"])
    if "--enable-extended" in sys.argv:
        h.get_feature_report(0x05, 41)
    end = time.time() + 2
    seen = set()
    while time.time() < end:
        r = h.read(128, 100)
        if not r:
            continue
        seen.add((r[0], len(r)))
        res = parse(r)
        if res:
            print(f"battery={res[0]}% state={res[1]} (report 0x{r[0]:02X}, {len(r)} bytes)")
            return
    print(f"no battery-bearing report; seen {sorted(seen)}. Try --enable-extended (see docstring).")


if __name__ == "__main__":
    main()
