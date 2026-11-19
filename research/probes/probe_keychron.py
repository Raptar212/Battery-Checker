"""Keychron Link receiver 3434:D030, vendor collection usage page 0x8C: what does it report?

Read-only: listens to input reports and reads feature reports; never writes output reports.
Known from community projects (mouse on the Link): feature 0x51 = status query used by the
Keychron Launcher, input 0x54 = unsolicited status push (battery % among the bytes).
"""
import sys
import time

import hid

VID, PID, PAGE = 0x3434, 0xD030, 0x008C

info = next((d for d in hid.enumerate(VID, PID) if d["usage_page"] == PAGE), None)
if info is None:
    sys.exit("Keychron Link 0x8C collection not found")

dev = hid.device()
dev.open_path(info["path"])
dev.set_nonblocking(True)


def hexs(data):
    return " ".join(f"{b:02x}" for b in data)


for report_id in (0x51, 0x52, 0x53, 0x54):
    try:
        data = dev.get_feature_report(report_id, 65)
        print(f"feature 0x{report_id:02x}: {hexs(data)}")
    except (OSError, ValueError) as e:
        print(f"feature 0x{report_id:02x}: {e}")

seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 10
print(f"listening {seconds:g} s for input reports (press keys / Fn combos)...")
end = time.time() + seconds
while time.time() < end:
    data = dev.read(65)
    if data:
        print(f"input: {hexs(data)}")
    else:
        time.sleep(0.02)
dev.close()
