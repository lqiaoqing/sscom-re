#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Reconstruct IAT slot -> import name mapping from the unpacked image's .idata.

Layout (observed): for each DLL, a run of dwords holding RVAs of hint/name
structures, immediately followed by a run of bound addresses (0x6.. - 0x7..).
Pairs are matched index by index.

Verified ground truths: ReadFile@0x5164c4, GetTickCount@0x516454,
GetLocalTime@0x516410 (kernel32).
"""
import json
import re
import struct
from pathlib import Path

IMAGE = str(Path(__file__).resolve().parent.parent / "sscom5.13.1.unpacked.exe")
OUT = str(Path(__file__).resolve().parent / "iat_map.json")

IDA_START = 0x516000
IDA_END = 0x51a000

NAME_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_@$?]*$")

data = open(IMAGE, "rb").read()


def read_dword(va):
    off = va - 0x400000
    return struct.unpack("<I", data[off:off + 4])[0]


def read_name(rva):
    off = rva
    if off + 2 >= len(data):
        return None
    hint = struct.unpack("<H", data[off:off + 2])[0]
    if hint > 0x10FF:
        return None
    s = b""
    i = off + 2
    while i < len(data) and data[i] != 0:
        s += bytes([data[i]])
        i += 1
        if i - off > 128:
            return None
    try:
        t = s.decode("ascii")
    except Exception:
        return None
    if len(t) < 3 or not NAME_RE.match(t):
        return None
    return t


def classify(v):
    if v == 0:
        return ("zero", None)
    if v & 0x80000000 and (v & 0x7FFFFFFF) < 0x10000:
        return ("ord", v & 0xFFFF)
    if 0x60000000 <= v <= 0x7FFFFFFF:
        return ("bound", v)
    nm = read_name(v)
    if nm:
        return ("name", nm)
    return ("other", v)


def main():
    # 1. collect classified dwords
    entries = []  # (va, kind, payload)
    va = IDA_START
    while va < IDA_END - 3:
        k, p = classify(read_dword(va))
        entries.append((va, k, p))
        va += 4

    # 2. build runs
    runs = []  # (kind, start_va, list_of_(va,payload))
    cur_kind = None
    cur = []
    for va, k, p in entries:
        if k == cur_kind and k in ("name", "bound", "ord"):
            cur.append((va, p))
        else:
            if cur and cur_kind in ("name", "bound", "ord"):
                runs.append((cur_kind, cur[0][0], cur))
            cur = [(va, p)] if k in ("name", "bound", "ord") else []
            cur_kind = k
    if cur and cur_kind in ("name", "bound", "ord"):
        runs.append((cur_kind, cur[0][0], cur))

    # 3. pair name runs with following bound runs
    pairs = []
    i = 0
    while i < len(runs):
        kind, start, items = runs[i]
        if kind == "name" and len(items) >= 3:
            # find next bound run starting within 0x40 bytes after this run ends
            end = items[-1][0] + 4
            j = i + 1
            while j < len(runs):
                k2, s2, it2 = runs[j]
                if k2 == "bound" and 0 <= s2 - end <= 0x40 and len(it2) >= 3:
                    pairs.append((items, it2, start, s2))
                    i = j
                    break
                if k2 == "name":
                    break
                j += 1
        i += 1

    slots = {}
    unresolved = []
    for names, iats, ns, is_ in pairs:
        n = min(len(names), len(iats))
        for k in range(n):
            slot = iats[k][0]
            nm = names[k][1]
            slots["0x%08x" % slot] = nm
        else:
            if len(names) != len(iats):
                unresolved.append((ns, is_, len(names), len(iats)))

    print("pairs: %d" % len(pairs))
    for names, iats, ns, is_ in pairs:
        print("  names@%08x n=%-3d  iat@%08x n=%-3d  %s .. %s" %
              (ns, len(names), is_, len(iats), names[0][1], names[-1][1]))
    for u in unresolved:
        print("  LEN MISMATCH names@%08x(%d) iat@%08x(%d)" % (u[0], u[2], u[1], u[3]))

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(slots, f, indent=1, sort_keys=True)
    print("wrote %d slot names" % len(slots))

    for name, va2 in [("ReadFile", 0x5164C4), ("GetTickCount", 0x516454), ("GetLocalTime", 0x516410)]:
        got = slots.get("0x%08x" % va2)
        print("check %-14s @%08x -> %s" % (name, va2, got))


if __name__ == "__main__":
    main()
