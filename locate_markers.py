#!/usr/bin/env python3
"""
Round 2: more thorough cross-reference. Search for additional push patterns
and look at the *call sites* near each marker push to identify the calling segment.
"""

from pathlib import Path

BIN = Path("v1_bin/ZOOMBINI._EX").read_bytes()
DGROUP_FILE = 0xD6A00

SEGMENTS = [
    (1,  0x00200, 0x6000,  "?"),
    (14, 0x0F800, 0x800,   "rand()"),
    (17, 0x14C00, 0x1000,  "memcpy?"),
    (18, 0x15C00, 0x1000,  "engine"),
    (20, 0x16C00, 0x2000,  "engine? wrapper?"),
    (24, 0x18C00, 0x3035,  "claimed: Caves"),
    (27, 0x1FE00, 0x2FE3,  "claimed: Fleens"),
    (28, 0x23800, 0x3E90,  "claimed: Cliffs/Hotel"),
    (30, 0x2A600, 0x7950,  "claimed: Lilly/Toads"),
    (33, 0x38200, 0x3E42,  "Map/Picker"),
    (34, 0x3CE00, 0x7F94,  "claimed: Mudball"),
    (35, 0x47C00, 0x3065,  "claimed: Net"),
    (36, 0x4BC00, 0x1B5D,  "Picker"),
    (37, 0x4E000, 0x5F30,  "claimed: Pizza"),
    (41, 0x58A00, 0x345C,  "claimed: Ferry A"),
    (42, 0x5D200, 0x6441,  "claimed: Smoke/Mirror"),
    (44, 0x65E00, 0x5F53,  "engine"),
    (45, 0x6CA00, 0x5261,  "claimed: Ferry B"),
    (48, 0x76400, 0x41FA,  "claimed: Tunnels"),
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
    out = []
    pos = 0
    while True:
        idx = BIN.find(needle, pos)
        if idx < 0: break
        out.append(idx)
        pos = idx + 1
    return out

MARKERS = [
    (b"Upper bridge accepts:", 0x9a6),
    (b"Lower bridge accepts:", 0x9bc),
    (b"Hieroglyphs",           0xd47),
    (b"Play FrogMan SCRB id:", 0xf0c),
    (b"Left / Bottom accept:", 0x49ac),
    (b"Left / Top accept:",    0x49c2),
    (b"Right accepts:",        0x4a00),
    (b" Cheat on ",            0x20e5),
]

# Multiple push patterns:
def make_patterns(ds_off):
    lo = ds_off & 0xFF
    hi = (ds_off >> 8) & 0xFF
    return [
        (bytes([0x68, lo, hi]),                 "push imm16"),                     # 3-byte
        (bytes([0xB8, lo, hi]),                 "mov ax, imm16"),                  # mov ax,imm
        (bytes([0xBB, lo, hi]),                 "mov bx, imm16"),
        (bytes([0xB9, lo, hi]),                 "mov cx, imm16"),
        (bytes([0xBA, lo, hi]),                 "mov dx, imm16"),
        (bytes([0xBE, lo, hi]),                 "mov si, imm16"),
        (bytes([0xBF, lo, hi]),                 "mov di, imm16"),
        # ModR/M variants storing imm to memory? unlikely for string addrs
    ]

print(f"{'Marker':<28} {'patterns found across binary'}")
print("-"*120)

for s, ds_off in MARKERS:
    print(f"\n=== {s.decode():<25} (ds:0x{ds_off:04x}) ===")
    str_locations = find_all(s)
    print(f"   string itself at: {[hex(x) for x in str_locations]}")
    for pat, desc in make_patterns(ds_off):
        sites = find_all(pat)
        sites_in_code = []
        for ofs in sites:
            seg, lab = file_to_seg(ofs)
            if seg is not None and seg != 191:
                # ensure not data segment containing the literal itself
                # (the string may itself contain a 0x68 byte that aligns)
                if not any(s_ofs <= ofs < s_ofs+len(s) for s_ofs in str_locations):
                    sites_in_code.append((ofs, seg, lab))
        if sites_in_code:
            seg_set = set(s for _,s,_ in sites_in_code)
            seg_str = ", ".join(f"seg{x}" for x in sorted(seg_set))
            print(f"   {desc:<22} → {len(sites_in_code)} hits in segments: {seg_str}")
