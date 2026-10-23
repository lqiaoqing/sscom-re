#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Parse binary DFM resources in .rsrc of the unpacked sscom image.

Walks object records: ShortString type, ShortString name, property list
terminated by 0x00, then child objects terminated by 0x00.
Prints (type, name, Caption, OnClick) plus selected other props.
"""
import struct
import sys

IMAGE = r"D:\Work\workspace\sscom-re\sscom5.13.1.unpacked.exe"
data = open(IMAGE, "rb").read()

RSRC_LO, RSRC_HI = 0x523000 - 0x400000, 0x537000 - 0x400000


def ss(i):
    """read shortstring at file offset i -> (str, new_i) or (None, i)"""
    if i >= len(data):
        return None, i
    n = data[i]
    if n == 0 or n > 200:
        return None, i
    if i + 1 + n > len(data):
        return None, i
    raw = data[i + 1:i + 1 + n]
    try:
        s = raw.decode("gbk")
    except Exception:
        return None, i
    return s, i + 1 + n


def parse_value(i, depth=0):
    """returns (value, new_i) or (None, i) on failure"""
    if i >= len(data) or depth > 12:
        return None, i
    t = data[i]
    i += 1
    if t == 0x00:  # vaNull
        return ("NULL",), i
    if t == 0x01:  # vaList
        items = []
        while True:
            v, ni = parse_value(i, depth + 1)
            if v is None:
                return None, i
            i = ni
            if v == ("NULL",):
                break
            items.append(v)
        return ("LIST", items), i
    if t == 0x02:
        return data[i], i + 1
    if t == 0x03:
        return struct.unpack("<h", data[i:i + 2])[0], i + 2
    if t == 0x04:
        return struct.unpack("<i", data[i:i + 4])[0], i + 4
    if t == 0x05:
        return data[i:i + 10], i + 10
    if t == 0x0F:  # single
        return struct.unpack("<f", data[i:i + 4])[0], i + 4
    if t == 0x10:  # currency (8 bytes)
        return data[i:i + 8], i + 8
    if t == 0x11:  # date (double)
        return struct.unpack("<d", data[i:i + 8])[0], i + 8
    if t == 0x06:
        n = data[i]
        return data[i + 1:i + 1 + n].decode("gbk", "replace"), i + 1 + n
    if t == 0x07:
        n = data[i]
        return data[i + 1:i + 1 + n].decode("latin1"), i + 1 + n
    if t == 0x08:
        return False, i
    if t == 0x09:
        return True, i
    if t == 0x0A:  # binary
        n = struct.unpack("<i", data[i:i + 4])[0]
        if n < 0 or i + 4 + n > len(data):
            return None, i
        return ("BIN", n), i + 4 + n
    if t == 0x0B:  # set of shortstrings terminated by empty shortstring (0x00)
        items = []
        while True:
            s, ni = ss(i)
            if s is None:
                i += 1
                break
            items.append(s)
            i = ni
            if len(items) > 1000:
                return None, i
        return ("SET", items), i
    if t == 0x0C:  # lstring
        n = struct.unpack("<i", data[i:i + 4])[0]
        if n < 0 or n > 0x100000 or i + 4 + n > len(data):
            return None, i
        return data[i + 4:i + 4 + n].decode("gbk", "replace"), i + 4 + n
    if t == 0x12:  # wstring
        n = struct.unpack("<i", data[i:i + 4])[0]
        if n < 0 or n > 0x100000 or i + 4 + 2 * n > len(data):
            return None, i
        return data[i + 4:i + 4 + 2 * n].decode("utf-16-le", "replace"), i + 4 + 2 * n
    if t == 0x13:
        return struct.unpack("<q", data[i:i + 8])[0], i + 8
    if t == 0x14:  # utf8
        n = struct.unpack("<i", data[i:i + 4])[0]
        if n < 0 or i + 4 + n > len(data):
            return None, i
        return data[i + 4:i + 4 + n].decode("utf-8", "replace"), i + 4 + n
    if t == 0x0E:  # collection (empirical: items led by 0x01, collection ends with 0x00)
        items = 0
        while True:
            if i >= len(data):
                return None, i
            if data[i] == 0x00:
                i += 1
                break
            if data[i] == 0x01:
                i += 1
            # one item: property pairs until 0x00
            while True:
                if i >= len(data):
                    return None, i
                if data[i] == 0x00:
                    i += 1
                    break
                pn, ni = ss(i)
                if pn is None:
                    return None, i
                i = ni
                v, ni = parse_value(i, depth + 1)
                if v is None:
                    return None, i
                i = ni
            items += 1
            if items > 5000:
                return None, i
        return ("COLL", items), i
    return None, i


def parse_object(i, depth=0):
    if depth > 16 or i >= len(data):
        return None, i
    typ, i2 = ss(i)
    if typ is None or not typ.startswith("T") or len(typ) > 40 or not all(32 < ord(c) < 127 for c in typ):
        return None, i
    name, i3 = ss(i2)
    if name is None or not all(32 < ord(c) < 127 for c in name) or len(name) > 60:
        return None, i
    i = i3
    props = {}
    while True:
        if i >= len(data):
            return None, i
        if data[i] == 0x00:
            i += 1
            break
        pn, ni = ss(i)
        if pn is None:
            return None, i
        i = ni
        v, ni = parse_value(i, depth + 1)
        if v is None:
            return None, i
        i = ni
        props[pn] = v
    children = []
    while True:
        if i >= len(data):
            return None, i
        if data[i] == 0x00:
            i += 1
            break
        ch, ni = parse_object(i, depth + 1)
        if ch is None:
            return None, i
        children.append(ch)
        i = ni
    return {"type": typ, "name": name, "props": props, "children": children}, i


def walk(obj, out, path):
    p = path + "/" + obj["name"]
    cap = obj["props"].get("Caption")
    clk = None
    for k, v in obj["props"].items():
        if k.startswith("On") and isinstance(v, str):
            clk = (k, v)
    if cap is not None or clk is not None:
        out.append((obj["type"], obj["name"], cap, clk, p))
    for ch in obj["children"]:
        walk(ch, out, p)


def main():
    import re
    found = []
    blob = data[RSRC_LO:RSRC_HI]
    cands = sorted({m.start() + 1 for m in re.finditer(rb"\x00[\x03-\x18]T[A-Za-z0-9_]{2,40}", blob)})
    print("candidates: %d" % len(cands))
    covered_until = -1
    for off in cands:
        if off < covered_until:
            continue
        r, ni = parse_object(RSRC_LO + off)
        if r is None or len(r["props"]) < 3:
            continue
        out = []
        walk(r, out, "")
        if not out:
            continue
        found.append((RSRC_LO + off, r, out))
        covered_until = ni - RSRC_LO

    print("top-level objects found: %d" % len(found))
    for off, r, out in found:
        print("== FORM %s @0x%06x (type=%s name=%s)" % (r["props"].get("Caption", ""), off + 0x400000, r["type"], r["name"]))
        for typ, nm, cap, clk, path in out:
            if cap is None and clk is None:
                continue
            c = ("Caption=%r" % cap) if cap is not None else ""
            k = ("%s=%s" % clk) if clk else ""
            print("   %-28s %-18s %s %s" % (path, typ, c, k))


if __name__ == "__main__":
    main()
