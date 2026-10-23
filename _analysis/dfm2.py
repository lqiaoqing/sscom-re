#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Sliding-window scan of the DFM area: extract (object name, Caption, OnClick) tuples.

Properties inside one object are sorted alphabetically, so within a window
before each OnClick we find the nearest Caption, and the nearest object header
(shortstring type + shortstring name) before it.
"""
import re
import struct

IMAGE = r"D:\Work\workspace\sscom-re\sscom5.13.1.unpacked.exe"
data = open(IMAGE, "rb").read()
LO, HI = 0x523000 - 0x400000, 0x537000 - 0x400000
blob = data[LO:HI]


def read_shortstring(i):
    if i >= len(blob):
        return None, i
    n = blob[i]
    if n == 0 or n > 120:
        return None, i
    if i + 1 + n > len(blob):
        return None, i
    raw = blob[i + 1:i + 1 + n]
    try:
        return raw.decode("gbk"), i + 1 + n
    except Exception:
        return raw.decode("latin1"), i + 1 + n


def read_value(i):
    """Read a property value at i, return (value, new_i)."""
    if i >= len(blob):
        return None, i
    t = blob[i]
    i += 1
    try:
        if t == 0x06:
            n = blob[i]
            return blob[i + 1:i + 1 + n].decode("gbk", "replace"), i + 1 + n
        if t == 0x07:
            n = blob[i]
            return blob[i + 1:i + 1 + n].decode("latin1"), i + 1 + n
        if t == 0x0C:
            n = struct.unpack("<i", blob[i:i + 4])[0]
            if 0 <= n < 0x10000:
                return blob[i + 4:i + 4 + n].decode("gbk", "replace"), i + 4 + n
        if t == 0x12:
            n = struct.unpack("<i", blob[i:i + 4])[0]
            if 0 <= n < 0x10000:
                return blob[i + 4:i + 4 + 2 * n].decode("utf-16-le", "replace"), i + 4 + 2 * n
        if t == 0x14:
            n = struct.unpack("<i", blob[i:i + 4])[0]
            if 0 <= n < 0x10000:
                return blob[i + 4:i + 4 + n].decode("utf-8", "replace"), i + 4 + n
        if t == 0x08:
            return False, i
        if t == 0x09:
            return True, i
        if t == 0x02:
            return blob[i], i + 1
        if t == 0x03:
            return struct.unpack("<h", blob[i:i + 2])[0], i + 2
        if t == 0x04:
            return struct.unpack("<i", blob[i:i + 4])[0], i + 4
        if t == 0x0E:
            n = struct.unpack("<i", blob[i:i + 4])[0]
            return ("COLL", n), i + 4
    except Exception:
        pass
    return None, i


results = []
# find every "OnClick" property (or other On* events) and every Caption
prop_re = re.compile(rb"\x07On[A-Z][A-Za-z]{2,20}\x07")
cap_positions = [m.start() for m in re.finditer(rb"\x07Caption", blob)]

for m in prop_re.finditer(blob):
    p = m.start()
    name_prop, ni = read_shortstring(p)
    if name_prop is None or not name_prop.startswith("On"):
        continue
    val, vi = read_value(ni)
    if not isinstance(val, str) or not val:
        continue
    # nearest preceding Caption within 400 bytes
    caption = None
    cpos = None
    for cp in reversed(cap_positions):
        if p - 500 <= cp < p:
            cname, ci = read_shortstring(cp)
            if cname == "Caption":
                v, _ = read_value(ci)
                if isinstance(v, str):
                    caption = v
                    cpos = cp
                    break
    # object header: nearest preceding shortstring pair (type, name) before min(p, cpos)
    limit = min(p, cpos) if cpos else p
    objname = None
    # walk backwards over 0x00 candidates near limit; try to parse a valid (type, name) pair
    for back in range(limit, max(0, limit - 400), -1):
        if blob[back] != 0x00:
            continue
        tname, i2 = read_shortstring(back + 1)
        if not tname or not tname.startswith("T") or len(tname) > 40 or not tname[1:2].isalpha():
            continue
        oname, i3 = read_shortstring(i2)
        if oname and oname[:1].isalpha() and re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", oname):
            objname = oname
            break
    results.append((p + LO, objname, caption, name_prop, val))

print("event properties: %d" % len(results))
for off, objname, caption, ev, handler in results:
    print("0x%06x  %-22s %-22s %-12s Caption=%r -> %s" % (off + 0x400000, str(objname), str(caption), ev, caption, handler))
