#!/usr/bin/env python3
"""
Find the Eval/Action-Handler function in each puzzle's segment.

Pattern (from Cliffs, verified):
- Action-Handler is called with arg [bp+6] containing action ID (1, 2, 3, ...)
- It has a switch: cmp ax, 1 / je / cmp ax, 2 / je / cmp ax, 3 / je
- The "ACTION 3" branch contains the actual evaluation logic.

Signature search: look for `cmp ax, 1` and `cmp ax, 2` and `cmp ax, 3` close together.
Patterns:
  3D 01 00  cmp ax, 1
  3D 02 00  cmp ax, 2
  3D 03 00  cmp ax, 3
"""

import struct
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_16

V1 = Path("/tmp/zb_v1/ZOOMBINI._EX").read_bytes()

NE_OFF = struct.unpack_from("<I", V1, 0x3C)[0]
SEG_TBL = NE_OFF + struct.unpack_from("<H", V1, NE_OFF + 0x22)[0]
ALIGN = 1 << struct.unpack_from("<H", V1, NE_OFF + 0x32)[0]
def seg_info(s):
    sector, length, flags, _ = struct.unpack_from("<HHHH", V1, SEG_TBL + (s-1)*8)
    return sector*ALIGN, length, flags

def find_funcs(seg_num):
    seg_off, seg_len, _ = seg_info(seg_num)
    funcs = []
    for i in range(seg_off, seg_off + seg_len - 6):
        if V1[i:i+4] == b'\x8c\xd8\x90\x45':
            funcs.append(i)
    funcs.append(seg_off + seg_len)
    return funcs

def find_action_dispatcher(seg_num):
    """Find function with cmp ax, 1 / je / cmp ax, 2 within ~20 bytes."""
    seg_off, seg_len, _ = seg_info(seg_num)
    funcs = find_funcs(seg_num)
    candidates = []

    # Scan for the signature
    for i in range(seg_off, seg_off + seg_len - 8):
        # cmp ax, 1
        if V1[i:i+3] == b'\x3d\x01\x00':
            # Check next ~30 bytes for cmp ax, 2 (3d 02 00)
            window = V1[i+3:i+30]
            if b'\x3d\x02\x00' in window:
                # Find containing function
                func_start = max(f for f in funcs if f <= i)
                # Find function size
                idx = funcs.index(func_start)
                func_size = funcs[idx+1] - func_start
                candidates.append((func_start, func_size, i))

    # Group by function (multiple hits per function = high confidence)
    from collections import Counter
    func_hits = Counter(c[0] for c in candidates)
    return [(f, fs, hits) for (f, fs, _), hits in
            sorted(((f, fs, fs2 if False else 0), func_hits[f]) for f, fs, fs2 in candidates)]

def show(name, seg_num):
    funcs = find_funcs(seg_num)
    cands = find_action_dispatcher(seg_num)
    # Dedupe
    seen = set()
    unique = []
    for f, sz, hits in cands:
        if f not in seen:
            seen.add(f)
            unique.append((f, sz, hits))
    unique.sort(key=lambda x: -x[2])
    print(f"\n=== {name} (Seg {seg_num}) ===")
    print(f"  Action-Dispatcher candidates (cmp ax, 1 + cmp ax, 2 close):")
    for f, sz, hits in unique[:5]:
        idx = funcs.index(f)
        actual_size = funcs[idx+1] - f
        print(f"    Func 0x{f:06x} ({actual_size} B): {hits} cmp-ax-1 hits")

PUZZLES = [
    ("Allergic Cliffs", 23),
    ("Stone Cold Caves", 24),
    ("Captain Cajun Ferry", 26),
    ("Fleens", 27),
    ("Hotel Dimensia", 28),
    ("Tattooed Toads", 30),
    ("Bubblewonder Abyss", 34),
    ("Mudball Wall", 35),
    ("Pizza Pass", 37),
    ("Mirror Machine", 42),
    ("Stone Rise", 45),
    ("Lion's Lair", 48),
]

for name, seg in PUZZLES:
    show(name, seg)
