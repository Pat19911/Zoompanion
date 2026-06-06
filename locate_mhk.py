#!/usr/bin/env python3
"""
Round 3: which segment loads which MHK file?
Each puzzle code-segment must reference its own MHK filename string.
This is the strongest available marker for puzzles WITHOUT debug printf strings.
"""

from pathlib import Path

BIN = Path("v1_bin/ZOOMBINI._EX").read_bytes()
DGROUP_FILE = 0xD6A00

SEGMENTS = [
    (1,  0x00200, 0x6000,  "?"),
    (14, 0x0F800, 0x800,   "rand()"),
    (17, 0x14C00, 0x1000,  "memcpy?"),
    (18, 0x15C00, 0x1000,  "engine"),
    (20, 0x16C00, 0x2000,  "engine?"),
    (24, 0x18C00, 0x3035,  "?"),
    (27, 0x1FE00, 0x2FE3,  "?"),
    (28, 0x23800, 0x3E90,  "?"),
    (30, 0x2A600, 0x7950,  "?"),
    (33, 0x38200, 0x3E42,  "Map?"),
    (34, 0x3CE00, 0x7F94,  "?"),
    (35, 0x47C00, 0x3065,  "?"),
    (36, 0x4BC00, 0x1B5D,  "Picker?"),
    (37, 0x4E000, 0x5F30,  "Pizza"),
    (41, 0x58A00, 0x345C,  "?"),
    (42, 0x5D200, 0x6441,  "?"),
    (44, 0x65E00, 0x5F53,  "engine"),
    (45, 0x6CA00, 0x5261,  "?"),
    (48, 0x76400, 0x41FA,  "?"),
    (50, 0x7C000, 0x400A,  "engine"),
    (51, 0x80C00, 0x36CA,  "engine"),
    (53, 0x87C00, 0x1303,  "engine"),
    (55, 0x92C00, 0xB0EC,  "engine"),
    (59, 0xA6600, 0xA60E,  "engine core"),
]

def file_to_seg(fo):
    for s, off, sz, lab in SEGMENTS:
        if off <= fo < off + sz:
            return s, lab
    return None, "data?"

def find_all(needle):
    out = []; pos = 0
    while True:
        idx = BIN.find(needle, pos)
        if idx < 0: break
        out.append(idx); pos = idx + 1
    return out

# All MHK files referenced in DGROUP
MHK_NAMES = [
    b"Bridge.MHK", b"bridge.MHK", b"BRIDGE.MHK",
    b"Caves.MHK", b"caves.MHK", b"CAVES.MHK",
    b"Pizza.MHK", b"pizza.MHK", b"PIZZA.MHK",
    b"Ferry.MHK", b"ferry.MHK", b"FERRY.MHK",
    b"Lilly.MHK", b"lilly.MHK", b"LILLY.MHK",
    b"Slides.MHK", b"slides.MHK", b"SLIDES.MHK",
    b"Fleens.MHK", b"fleens.MHK", b"FLEENS.MHK",
    b"Hotel.MHK", b"hotel.MHK", b"HOTEL.MHK",
    b"Net.MHK", b"net.MHK", b"NET.MHK",
    b"Tunnels.MHK", b"tunnels.MHK", b"TUNNELS.MHK",
    b"Smoke.MHK", b"smoke.MHK", b"SMOKE.MHK",
    b"Maze2.MHK", b"maze2.MHK", b"MAZE2.MHK",
    b"Picker.MHK", b"picker.MHK", b"PICKER.MHK",
    b"Map.MHK", b"map.MHK", b"MAP.MHK",
    b"Town.MHK", b"town.MHK", b"TOWN.MHK",
    b"Basecamp.MHK", b"basecamp.MHK", b"BASECAMP.MHK",
    b"Bctwo.MHK", b"bctwo.MHK", b"BCTWO.MHK",
    b"Xfer.MHK", b"xfer.MHK", b"XFER.MHK",
    b"Zoombini.MHK", b"zoombini.MHK", b"ZOOMBINI.MHK",
]

print(f"{'MHK Filename':<20} {'String at':>12} {'DS-off':>10}  {'Pushed from segments'}")
print("-"*120)

# Group by canonical name
seen_names = set()
for name in MHK_NAMES:
    canonical = name.lower().decode()
    if canonical in seen_names: continue
    locs = find_all(name)
    if not locs: continue
    seen_names.add(canonical)
    for loc in locs:
        if loc < DGROUP_FILE:
            continue
        ds_off = loc - DGROUP_FILE
        if ds_off > 0xFFFF: continue
        # Find push imm16 of this offset
        push_pat = bytes([0x68, ds_off & 0xFF, (ds_off >> 8) & 0xFF])
        push_sites = find_all(push_pat)
        # Also look at mov di/si/bx/cx patterns
        all_segs = []
        for opcode_byte, regname in [(0x68, "push"), (0xBE, "mov si"), (0xBF, "mov di"),
                                      (0xBB, "mov bx"), (0xB9, "mov cx"), (0xBA, "mov dx"),
                                      (0xB8, "mov ax")]:
            pat = bytes([opcode_byte, ds_off & 0xFF, (ds_off >> 8) & 0xFF])
            for site in find_all(pat):
                seg, _ = file_to_seg(site)
                if seg and seg != 191:
                    all_segs.append((seg, regname, site))
        seg_set = sorted(set(s for s,_,_ in all_segs))
        seg_str = ", ".join(f"seg{s}" for s in seg_set) if seg_set else "—"
        print(f"  {name.decode():<18} 0x{loc:08x}  0x{ds_off:06x}   {seg_str}")
