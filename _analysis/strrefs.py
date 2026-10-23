#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Scan a function's machine code for immediate operands that reference strings.

Finds `mov reg, imm32` (B8-BF), `push imm32` (68), `cmp reg, imm32` (3D/81) and
reports any imm in the image's data/text range that resolves to a printable
GBK/ASCII string. Output: unique (address, string) pairs in scan order.
Usage: strrefs.py <start_hex> <end_hex> [--all]
"""
import re
import struct
import sys

IMAGE = r"D:\Work\workspace\sscom-re\sscom5.13.1.unpacked.exe"
data = open(IMAGE, "rb").read()


def resolve(va):
    off = va - 0x400000
    if off < 0 or off >= len(data):
        return None
    seg = data[off:off + 160]
    if not seg:
        return None
    # ascii/gbk string starting right at va
    e = seg.find(b"\x00")
    if e < 3:
        return None
    raw = seg[:e]
    try:
        s = raw.decode("gbk")
    except Exception:
        try:
            s = raw.decode("latin1")
        except Exception:
            return None
    if not s:
        return None
    printable = sum(1 for c in s if c.isprintable() or c in "\r\n\t")
    if printable < len(s) * 0.85:
        return None
    return s


def main():
    start = int(sys.argv[1], 16)
    end = int(sys.argv[2], 16)
    blob = data[start - 0x400000:end - 0x400000]
    seen = set()
    out = []
    i = 0
    while i < len(blob) - 5:
        b = blob[i]
        if b in (0xB8, 0xB9, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF):
            imm = struct.unpack("<I", blob[i + 1:i + 5])[0]
        elif b == 0x68:
            imm = struct.unpack("<I", blob[i + 1:i + 5])[0]
        elif b == 0x3D:
            imm = struct.unpack("<I", blob[i + 1:i + 5])[0]
        else:
            i += 1
            continue
        if 0x400000 <= imm < 0x54a000:
            s = resolve(imm)
            if s and len(s) >= 2 and imm not in seen:
                seen.add(imm)
                out.append((start + i, imm, s))
        i += 1
    for at, imm, s in out:
        print("%08x  ->  %08x  %r" % (at, imm, s))


if __name__ == "__main__":
    main()
