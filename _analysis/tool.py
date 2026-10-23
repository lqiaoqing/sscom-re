#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Capstone toolkit for sscom5.13.1.unpacked.exe static analysis.

Image: base 0x400000, file offset == RVA (VA - 0x400000).
"""
import argparse
import json
import os
import re
import struct
import sys

import capstone

IMAGE = r"D:\Work\workspace\sscom-re\sscom5.13.1.unpacked.exe"
BASE = 0x400000

with open(IMAGE, "rb") as _f:
    DATA = _f.read()

# IAT: slot VA -> import name (reconstructed by imports.py from .idata pairs)
IAT = {}
_MAP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "iat_map.json")
if os.path.exists(_MAP):
    with open(_MAP, encoding="utf-8") as _f:
        IAT = {int(k, 16): v for k, v in json.load(_f).items()}

# sections: (name, va_start, va_end, file_off, rawsize) — from the image's section table
SECTIONS = [
    (".text", 0x401000, 0x4ED000, 0x1000, 0xEC000),
    (".data", 0x4ED000, 0x514000, 0xED000, 0x27000),
    (".tls", 0x514000, 0x515000, 0x114000, 0x1000),
    (".rdata", 0x515000, 0x516000, 0x115000, 0x1000),
    (".idata", 0x516000, 0x51A000, 0x116000, 0x4000),
    (".edata", 0x51A000, 0x523000, 0x11A000, 0x9000),
    (".rsrc", 0x523000, 0x537000, 0x123000, 0x14000),
    (".reloc", 0x537000, 0x547000, 0x137000, 0x10000),
    (".aspack", 0x547000, 0x549000, 0x147000, 0x2000),
    (".adata", 0x549000, 0x54A000, 0x149000, 0x1000),
]

MD = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
MD.detail = True


def v2o(va):
    for name, start, end, off, rawsize in SECTIONS:
        if start <= va < end:
            return off + (va - start)
    return None


def o2v(off):
    for name, start, end, off0, rawsize in SECTIONS:
        if off0 <= off < off0 + rawsize:
            return start + (off - off0)
    return None


def read(va, n):
    off = v2o(va)
    if off is None:
        return b""
    return DATA[off:off + n]


def is_ascii_printable(bs):
    try:
        s = bs.decode("ascii")
    except Exception:
        return None
    if all(32 <= ord(c) < 127 for c in s):
        return s
    return None


def try_show_string(va, maxlen=80):
    bs = read(va, maxlen)
    if not bs:
        return None
    # ascii run
    m = re.match(rb"[\x20-\x7e]{2,}", bs)
    if m and len(m.group(0)) >= 4:
        return repr(m.group(0).decode("ascii"))
    # gbk run
    try:
        s = bs.decode("gbk", "ignore")
        s = s.split("\x00")[0]
        if s and any("\u4e00" <= ch <= "\u9fff" for ch in s):
            return repr(s[:40])
    except Exception:
        pass
    return None


def fmt_insn(insn, show_detail):
    text = "%08x  %-24s %s" % (insn.address, insn.mnemonic, insn.op_str)
    notes = []
    for op in insn.operands:
        if op.type == capstone.x86.X86_OP_MEM and op.mem.base == 0 and op.mem.index == 0 and op.mem.disp:
            va = op.mem.disp & 0xFFFFFFFF
            if va in IAT:
                notes.append("-> " + IAT[va])
            else:
                st = try_show_string(va)
                if st:
                    notes.append("-> str " + st)
        if op.type == capstone.x86.X86_OP_IMM:
            v = op.imm & 0xFFFFFFFF
            if show_detail:
                st = try_show_string(v)
                if st:
                    notes.append("-> str " + st)
                elif v in IAT:
                    notes.append("-> imp " + IAT[v])
    if notes:
        text += "   ; " + " | ".join(notes)
    return text


def cmd_disas(args):
    va = int(args.addr, 16)
    n = args.count
    code = read(va, 16 * n + 64)
    i = 0
    for insn in MD.disasm(code, va):
        print(fmt_insn(insn, args.detail))
        i += 1
        if i >= n:
            break


def cmd_hexdump(args):
    va = int(args.addr, 16)
    n = args.len
    data = read(va, n)
    for off in range(0, len(data), 16):
        chunk = data[off:off + 16]
        hexs = " ".join("%02x" % b for b in chunk)
        asc = "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)
        print("%08x  %-47s  %s" % (va + off, hexs, asc))


def _decode_gbk(pattern):
    try:
        return pattern.encode("gbk")
    except Exception:
        return None


def cmd_find(args):
    """Find byte patterns. Supports: text:'...' (gbk if non-ascii), hex:001122, addr:0x... (dword le)."""
    jobs = []
    for pat in args.patterns:
        if pat.startswith("text:"):
            s = pat[5:]
            bs = _decode_gbk(s)
            if bs is None:
                print("BADPAT", pat)
                continue
            jobs.append((pat, bs))
        elif pat.startswith("hex:"):
            jobs.append((pat, bytes.fromhex(pat[4:].replace(" ", ""))))
        elif pat.startswith("addr:"):
            v = int(pat[5:], 16)
            jobs.append((pat, struct.pack("<I", v)))
        else:
            jobs.append(("text:" + pat, _decode_gbk(pat)))
    for label, needle in jobs:
        if needle is None:
            print("BADPAT", label)
            continue
        idxs = []
        start = 0
        while True:
            i = DATA.find(needle, start)
            if i < 0:
                break
            idxs.append(i)
            start = i + 1
        print("== %s : %d hits" % (label, len(idxs)))
        for i in idxs[:args.limit]:
            va = o2v(i)
            print("   file 0x%06x  va 0x%08x" % (i, va if va else -1))


def parse_table(va, max_records=2000):
    """Parse a Delphi published-method table. Returns list of (name, code, size, off)."""
    recs = []
    off = va
    for _ in range(max_records):
        hdr = read(off, 7)
        if len(hdr) < 7:
            break
        size, code, namelen = struct.unpack("<HIB", hdr)
        if namelen == 0 or namelen > 250:
            break
        nameb = read(off + 7, namelen)
        if len(nameb) != namelen:
            break
        try:
            name = nameb.decode("ascii")
        except Exception:
            break
        if not re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", name):
            break
        recs.append((name, code, size, off))
        off += size
    return recs


def cmd_mt(args):
    va = int(args.addr, 16)
    recs = parse_table(va)
    print("records: %d  (table 0x%x .. 0x%x)" % (len(recs), va, va + sum(r[2] for r in recs)))
    for name, code, size, off in recs:
        print("  %08x  %-40s size=%d" % (code, name, size))


def cmd_ref(args):
    """Find little-endian dword occurrences of a value inside .text-ish sections."""
    v = int(args.value, 16)
    needle = struct.pack("<I", v)
    print("refs to 0x%x" % v)
    n = 0
    for name, start, end, off, rawsize in SECTIONS:
        if name.lower() not in (".text", ".itext", "code", ".data", ".rdata"):
            continue
        if name.lower() not in (".text", ".itext", "code"):
            continue
        blob = DATA[off:off + rawsize]
        st = 0
        while True:
            i = blob.find(needle, st)
            if i < 0:
                break
            va = start + i
            st = i + 1
            n += 1
            print("   at 0x%08x (%s)" % (va, name))
            if args.show:
                back = max(0, i - 16)
                code = blob[back:back + 32]
                base = start + back
                k = 0
                for insn in MD.disasm(code, base):
                    marker = ">>" if insn.address <= va < insn.address + insn.size else "  "
                    print("     %s %s" % (marker, fmt_insn(insn, True)))
                    k += 1
                    if k > 6:
                        break
    print("total %d" % n)


def cmd_iat(args):
    for va, name in sorted(IAT.items()):
        if args.pattern.lower() in name.lower():
            print("0x%08x  %s" % (va, name))


def main():
    ap = argparse.ArgumentParser(prog="tool")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("d", help="disassemble")
    p.add_argument("addr")
    p.add_argument("-n", "--count", type=int, default=40)
    p.add_argument("--detail", action="store_true", help="also annotate immediate strings")
    p.set_defaults(func=cmd_disas)

    p = sub.add_parser("x", help="hex dump")
    p.add_argument("addr")
    p.add_argument("-l", "--len", type=int, default=128)
    p.set_defaults(func=cmd_hexdump)

    p = sub.add_parser("f", help="find pattern")
    p.add_argument("patterns", nargs="+")
    p.add_argument("--limit", type=int, default=30)
    p.set_defaults(func=cmd_find)

    p = sub.add_parser("mt", help="parse method table")
    p.add_argument("addr")
    p.set_defaults(func=cmd_mt)

    p = sub.add_parser("ref", help="find dword refs in code")
    p.add_argument("value")
    p.add_argument("--show", action="store_true")
    p.set_defaults(func=cmd_ref)

    p = sub.add_parser("iat", help="find IAT entry")
    p.add_argument("pattern")
    p.set_defaults(func=cmd_iat)

    args = ap.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
