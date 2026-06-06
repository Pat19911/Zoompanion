#!/usr/bin/env python3
"""Standardized deep analysis tool — for any puzzle.
Usage: python3 puzzle_deep_template.py <seg_num> <init_offset> <init_size>
Outputs:
- Backward jumps (loops) with distances
- random_range distribution per function
- Cluster analysis of random_range calls in init
- DS write addresses in init
- random_range arguments (min, max) for each call
"""
import struct
import sys
from pathlib import Path
from collections import Counter, defaultdict
from capstone import Cs, CS_ARCH_X86, CS_MODE_16

V1 = Path("/tmp/zb_v1/ZOOMBINI._EX").read_bytes()
md = Cs(CS_ARCH_X86, CS_MODE_16)

NE_OFF = struct.unpack_from("<I", V1, 0x3C)[0]
SEG_TBL = NE_OFF + struct.unpack_from("<H", V1, NE_OFF + 0x22)[0]
ALIGN = 1 << struct.unpack_from("<H", V1, NE_OFF + 0x32)[0]
def seg_info(s):
    sector, length, flags, _ = struct.unpack_from("<HHHH", V1, SEG_TBL + (s-1)*8)
    return sector*ALIGN, length, flags

def reloc_lookup(seg_num):
    seg_off, seg_len, _ = seg_info(seg_num)
    reloc_off = seg_off + seg_len
    n = struct.unpack_from("<H", V1, reloc_off)[0]
    out = {}
    for r in range(n):
        rec = reloc_off + 2 + r*8
        t, fl, ofs, tgt1, tgt2 = struct.unpack_from("<BBHHH", V1, rec)
        if (fl & 3) == 0:
            out[seg_off + ofs - 1] = (tgt1, tgt2)
    return out

def find_funcs(seg_num):
    seg_off, seg_len, _ = seg_info(seg_num)
    funcs = []
    for i in range(seg_off, seg_off + seg_len - 6):
        if V1[i:i+4] == b'\x8c\xd8\x90\x45':
            funcs.append(i)
    funcs.append(seg_off + seg_len)
    return funcs

def analyze(seg_num, init_off, init_size):
    print(f"\n{'='*78}\n  DEEP: Seg {seg_num}, Init 0x{init_off:06x} ({init_size} B)\n{'='*78}")
    rl = reloc_lookup(seg_num)
    INIT_END = init_off + init_size

    # Backward jumps
    chunk = V1[init_off:INIT_END]
    backward = []
    for ins in md.disasm(chunk, init_off):
        if ins.mnemonic in ("jmp","je","jne","jl","jg","jb","ja","jbe","jae","jle","jge","jz","jnz"):
            tgt = None
            if len(ins.bytes) == 2 and ins.bytes[0] in [0xEB] + list(range(0x70, 0x80)):
                disp = struct.unpack("<b", ins.bytes[1:2])[0]
                tgt = ins.address + 2 + disp
            elif len(ins.bytes) == 3 and ins.bytes[0] == 0xE9:
                disp = struct.unpack("<h", ins.bytes[1:3])[0]
                tgt = ins.address + 3 + disp
            elif len(ins.bytes) == 4 and ins.bytes[0] == 0x0F and 0x80 <= ins.bytes[1] <= 0x8F:
                disp = struct.unpack("<h", ins.bytes[2:4])[0]
                tgt = ins.address + 4 + disp
            if tgt is not None and tgt < ins.address:
                backward.append((ins.address, tgt, ins.address - tgt))

    backward.sort(key=lambda x: -x[2])
    print(f"\nBackward jumps in Init: {len(backward)}")
    print("Top 10:")
    for src, tgt, dist in backward[:10]:
        print(f"  0x{src:06x} → 0x{tgt:06x}  ({dist} B back)")

    # random_range sites + arguments
    rand_sites = sorted([s for s, t in rl.items()
                          if t == (20, 0x04E8) and init_off <= s < INIT_END])
    print(f"\nrandom_range calls in Init: {len(rand_sites)}")
    print("With pushed args (the two pushes immediately before lcall):")
    for site in rand_sites:
        # Look 8 bytes back for two `push imm` instructions
        ctx = V1[site-8:site]
        # try to parse as: push imm; push imm; lcall
        args = []
        i = 0
        while i < len(ctx) and len(args) < 2:
            b = ctx[i]
            if b == 0x6A:  # push imm8
                args.append(struct.unpack("<b", ctx[i+1:i+2])[0])
                i += 2
            elif b == 0x68:  # push imm16
                args.append(struct.unpack("<h", ctx[i+1:i+3])[0])
                i += 3
            elif b == 0xff:  # push reg/mem
                # push word [bp+disp8] = ff 76 disp8  or  push word [...] = ff 36 ...
                if ctx[i+1] == 0x76:
                    args.append(f"[bp+{ctx[i+2]:#x}]")
                    i += 3
                elif ctx[i+1] == 0x36:
                    addr = struct.unpack("<H", ctx[i+2:i+4])[0]
                    args.append(f"[0x{addr:04x}]")
                    i += 4
                else:
                    break
            else:
                i += 1
        if len(args) >= 2:
            print(f"  0x{site:06x}: random_range({args[0]}, {args[1]})")
        else:
            print(f"  0x{site:06x}: (args unclear, prev: {ctx.hex()})")

    # DS writes (in puzzle range)
    writes = Counter()
    for ins in md.disasm(chunk, init_off):
        b = ins.bytes
        if len(b) >= 3:
            if b[0] == 0xA3:
                addr = struct.unpack_from("<H", b, 1)[0]
                writes[addr] += 1
            elif b[0] == 0xC7 and b[1] == 0x06:
                addr = struct.unpack_from("<H", b, 2)[0]
                writes[addr] += 1
            elif b[0] == 0xC6 and b[1] == 0x06:
                addr = struct.unpack_from("<H", b, 2)[0]
                writes[addr] += 1
            elif b[0] == 0xA2:
                addr = struct.unpack_from("<H", b, 1)[0]
                writes[addr] += 1
    print(f"\nTop 15 DS writes in Init:")
    for addr, count in sorted(writes.items(), key=lambda x: -x[1])[:15]:
        print(f"  DS:0x{addr:04x}: {count}x")

    # Internal call destinations
    call_dests = Counter()
    for site, (t1, t2) in rl.items():
        if init_off <= site < INIT_END:
            call_dests[(t1, t2)] += 1
    print(f"\nTop 8 lcall destinations in Init:")
    for (s, o), n in call_dests.most_common(8):
        if (s, o) == (20, 0x04E8): continue
        print(f"  seg{s}:0x{o:04x}: {n}x")

if __name__ == "__main__":
    seg_num = int(sys.argv[1])
    init_off = int(sys.argv[2], 16)
    init_size = int(sys.argv[3])
    analyze(seg_num, init_off, init_size)
