#!/usr/bin/env python3
"""
Master analysis: For each puzzle, do a structured first pass:
1. Verify segment boundary from NE header
2. Verify code segment via MHK-filename push (cross-reference)
3. Scan function prologues (looking for the FULL NE prolog: mov ax,ds; nop; inc bp; push bp)
4. Read all relocations (sub-call destinations)
5. Locate random_range calls (seg20:0x04E8)
6. Identify writes to puzzle DS state
7. Identify the largest function (likely Init)

Output: per-puzzle summary that can guide deeper analysis.
"""

import struct
from pathlib import Path
from collections import Counter
from capstone import Cs, CS_ARCH_X86, CS_MODE_16

V1 = Path("/tmp/zb_v1/ZOOMBINI._EX").read_bytes()
md = Cs(CS_ARCH_X86, CS_MODE_16)

# ============================================================
# NE-Header parsing
# ============================================================
NE_OFF = struct.unpack_from("<I", V1, 0x3C)[0]
SEG_TBL = NE_OFF + struct.unpack_from("<H", V1, NE_OFF + 0x22)[0]
ALIGN = 1 << struct.unpack_from("<H", V1, NE_OFF + 0x32)[0]

def seg_info(s):
    sector, length, flags, _ = struct.unpack_from("<HHHH", V1, SEG_TBL + (s-1)*8)
    return sector*ALIGN, length, flags

def get_relocations(seg_num):
    seg_off, seg_len, flags = seg_info(seg_num)
    if not (flags & 0x0100): return []
    reloc_off = seg_off + seg_len
    n = struct.unpack_from("<H", V1, reloc_off)[0]
    out = []
    for r in range(n):
        rec = reloc_off + 2 + r*8
        t, fl, ofs, tgt1, tgt2 = struct.unpack_from("<BBHHH", V1, rec)
        out.append({"type": t, "flags": fl, "offset": ofs, "tgt_seg": tgt1, "tgt_ofs": tgt2,
                    "is_internal": (fl & 3) == 0})
    return out

def find_function_prologues(seg_num):
    """Find both 4-byte NE prologs (mov ax,ds; nop; inc bp; push bp) and bare push bp."""
    seg_off, seg_len, _ = seg_info(seg_num)
    full_prologs = []  # 4-byte NE prolog
    bare_prologs = []  # just push bp; mov bp, sp
    for i in range(seg_off, seg_off + seg_len - 6):
        # Full NE prolog: 8C D8 90 45 (followed by 55 8B EC = push bp; mov bp, sp)
        # 8C D8 = mov ax, ds; 90 = nop; 45 = inc bp
        if V1[i:i+4] == b'\x8c\xd8\x90\x45':
            if V1[i+4:i+7] == b'\x55\x8b\xec':
                full_prologs.append(i)
        elif V1[i:i+3] == b'\x55\x8b\xec':
            # check it's not part of a NE prolog already counted
            if not (i >= 4 and V1[i-4:i] == b'\x8c\xd8\x90\x45'):
                bare_prologs.append(i)
    return full_prologs, bare_prologs

def find_ds_writes(seg_num, addr_range=None):
    """Find all writes to DS-absolute addresses (a3, c7 06, c6 06, 88 06, 89 X6 patterns)."""
    seg_off, seg_len, _ = seg_info(seg_num)
    chunk = V1[seg_off:seg_off+seg_len]
    writes = Counter()
    for ins in md.disasm(chunk, seg_off):
        # Look for absolute-addr writes
        b = ins.bytes
        if len(b) >= 3:
            # mov [abs], ax/al
            if b[0] == 0xa3 and len(b) >= 3:
                addr = struct.unpack_from("<H", b, 1)[0]
                writes[addr] += 1
            # mov word [abs], imm16
            elif b[0] == 0xc7 and b[1] == 0x06:
                addr = struct.unpack_from("<H", b, 2)[0]
                writes[addr] += 1
            # mov byte [abs], imm8
            elif b[0] == 0xc6 and b[1] == 0x06:
                addr = struct.unpack_from("<H", b, 2)[0]
                writes[addr] += 1
            # mov [abs], al
            elif b[0] == 0xa2:
                addr = struct.unpack_from("<H", b, 1)[0]
                writes[addr] += 1
            # mov [abs], reg (89 36/3E/1E/0E/16)
            elif b[0] == 0x89 and b[1] in (0x36, 0x3E, 0x1E, 0x0E, 0x16):
                addr = struct.unpack_from("<H", b, 2)[0]
                writes[addr] += 1
            # mov [abs], reg (88 X6 for byte)
            elif b[0] == 0x88 and (b[1] & 0xC7) == 0x06 and len(b) >= 4:
                addr = struct.unpack_from("<H", b, 2)[0]
                writes[addr] += 1
    return writes

# ============================================================
# Puzzle definitions (verified MHK→segment mapping)
# ============================================================
PUZZLES = [
    ("Pizza Pass",          "Pizza.MHK",     37),
    ("Stone Cold Caves",    "Caves.MHK",     24),
    ("Captain Cajun Ferry", "Ferry.MHK",     41),  # unverified — Ferry.MHK only pushed from seg55
    ("Titanic Tattooed Toads", "Lilly.MHK",  30),
    ("Stone Rise",          "Slides.MHK",    45),
    ("Fleens",              "Fleens.MHK",    27),
    ("Hotel Dimensia",      "Hotel.MHK",     28),
    ("Mudball Wall",        "Net.MHK",       35),
    ("Lion's Lair",         "Tunnels.MHK",   48),
    ("Mirror Machine",      "Smoke.MHK",     42),
    ("Bubblewonder Abyss",  "Maze2.MHK",     34),
]

# ============================================================
# Per-puzzle analysis
# ============================================================
print("="*80)
print("MASTER PUZZLE ANALYSIS — Zoombinis v1 ZOOMBINI._EX")
print("="*80)

for name, mhk, seg_num in PUZZLES:
    print(f"\n{'='*80}")
    print(f"  {name} ({mhk} → Seg {seg_num})")
    print(f"{'='*80}")

    seg_off, seg_len, flags = seg_info(seg_num)
    print(f"  File: 0x{seg_off:06x}..0x{seg_off+seg_len:06x} ({seg_len} bytes = 0x{seg_len:04x})")

    # Function prologues
    full_p, bare_p = find_function_prologues(seg_num)
    total_funcs = len(full_p) + len(bare_p)
    print(f"  Functions: {total_funcs} ({len(full_p)} full NE prolog + {len(bare_p)} bare push bp)")
    if full_p:
        # Compute function sizes (from one prolog to the next)
        all_starts = sorted(full_p + bare_p + [seg_off + seg_len])
        sizes = []
        for i in range(len(all_starts)-1):
            sz = all_starts[i+1] - all_starts[i]
            sizes.append((all_starts[i], sz))
        sizes_sorted = sorted(sizes, key=lambda x: -x[1])
        print(f"  Top 3 by size:")
        for start, sz in sizes_sorted[:3]:
            print(f"    0x{start:06x} (seg-rel 0x{start-seg_off:04x}): {sz} B")

    # Relocations
    relocs = get_relocations(seg_num)
    int_relocs = [r for r in relocs if r["is_internal"]]
    print(f"  Relocations: {len(relocs)} ({len(int_relocs)} internal)")

    # Calls per target segment
    target_dist = Counter()
    for r in int_relocs:
        target_dist[r["tgt_seg"]] += 1
    top_targets = target_dist.most_common(5)
    print(f"  Top 5 call destinations: {[(f'seg{s}', n) for s, n in top_targets]}")

    # random_range calls (seg20:0x04E8)
    rand_calls = [r for r in int_relocs if r["tgt_seg"] == 20 and r["tgt_ofs"] == 0x04E8]
    print(f"  random_range calls (seg20:0x04E8): {len(rand_calls)}")
    if rand_calls:
        for r in rand_calls[:5]:
            print(f"    at seg-rel 0x{r['offset']-1:04x} (file 0x{seg_off + r['offset']-1:06x})")

    # Direct rand calls (seg14:0x0028)
    direct_rand = [r for r in int_relocs if r["tgt_seg"] == 14]
    print(f"  Direct seg14 (rand) calls: {len(direct_rand)}")

    # DS writes to identify puzzle state
    writes = find_ds_writes(seg_num)
    # Filter to puzzle-likely range (typically 0x7000-0xC000)
    puzzle_writes = {a: n for a, n in writes.items() if 0x6000 <= a <= 0xC000}
    top_writes = sorted(puzzle_writes.items(), key=lambda x: -x[1])[:10]
    print(f"  Top DS writes (in puzzle state range 0x6000-0xC000):")
    for addr, count in top_writes:
        print(f"    DS:0x{addr:04x}: {count}x")

    # Verify MHK push site
    DGROUP = 0xD6A00
    mhk_str_pos = V1.find(mhk.encode() + b'\x00')
    if mhk_str_pos > 0 and mhk_str_pos > DGROUP:
        ds_off = mhk_str_pos - DGROUP
        push_pat = bytes([0x68, ds_off & 0xFF, (ds_off >> 8) & 0xFF])
        # Find push site in this segment
        push_in_seg = []
        for i in range(seg_off, seg_off + seg_len - 3):
            if V1[i:i+3] == push_pat:
                push_in_seg.append(i)
        if push_in_seg:
            print(f"  ✓ MHK marker confirmed: '{mhk}' (DS:0x{ds_off:04x}) pushed from this segment")
            for p in push_in_seg[:3]:
                print(f"    at file 0x{p:06x} (seg-rel 0x{p-seg_off:04x})")
        else:
            print(f"  ⚠️ MHK '{mhk}' NOT pushed from this segment (DS:0x{ds_off:04x})")
            # Check if it's pushed from elsewhere
            for s in range(1, 192):
                so, sl, fl = seg_info(s)
                if (fl & 7) != 0: continue  # only code segments
                for i in range(so, so + sl - 3):
                    if V1[i:i+3] == push_pat:
                        print(f"    pushed from seg{s} at file 0x{i:06x}")
                        break
