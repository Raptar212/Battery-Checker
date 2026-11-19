"""List HID interfaces of the target devices: Logitech (046D), Astro (9886), Sony (054C)."""
import hid

VIDS = {0x046D: "Logitech", 0x9886: "Astro", 0x054C: "Sony"}

for d in sorted(hid.enumerate(), key=lambda d: (d["vendor_id"], d["product_id"], d["interface_number"])):
    if d["vendor_id"] not in VIDS:
        continue
    print(
        f"{VIDS[d['vendor_id']]:8} {d['vendor_id']:04X}:{d['product_id']:04X} "
        f"if={d['interface_number']:>2} page=0x{d['usage_page']:04X} usage=0x{d['usage']:04X} "
        f"{d['product_string']!r}"
    )
    print(f"         {d['path'].decode(errors='replace')}")
