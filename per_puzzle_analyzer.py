#!/usr/bin/env python3
"""Per-puzzle deep analysis: locate Init, disassemble it, find Difficulty-Dispatch,
detect Pool/Selection patterns, identify Match-Algorithm."""

import struct
import sys
from pathlib import Path
from collections import Counter
from capstone import Cs, CS_ARCH_X86, CS_MODE_16

V1 = Path("/tmp/zb_v1/ZOOMBINI._EX").read_bytes()
md = Cs(CS_ARCH_X86, CS_MODE_16)

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

def find_function_starts(seg_num):
    """Returns list of (file_offset, size) for all functions in segment."""
    seg_off, seg_len, _ = seg_info(seg_num)
    starts = []
    for i in range(seg_off, seg_off + seg_len - 6):
        if V1[i:i+4] == b'\x8c\xd8\x90\x45' and V1[i+4:i+7] == b'\x55\x8b\xec':
            starts.append(i)
    starts.append(seg_off + seg_len)
    return [(starts[i], starts[i+1] - starts[i]) for i in range(len(starts)-1)]

def find_diff_dispatch(seg_num):
    """Look for `jmp [cs:bx + imm]` patterns indicating difficulty dispatch."""
    seg_off, seg_len, _ = seg_info(seg_num)
    chunk = V1[seg_off:seg_off+seg_len]
    dispatches = []
    # Pattern: 2E FF A7 XX XX = jmp word ptr cs:[bx + imm16]
    for i in range(len(chunk) - 5):
        if chunk[i:i+3] == b'\x2e\xff\xa7':
            disp = struct.unpack_from("<H", chunk, i+3)[0]
            file_at = seg_off + i
            jt_file = seg_off + disp
            dispatches.append((file_at, disp, jt_file))
    return dispatches

def find_writes_to(seg_num, target_addrs):
    """Find all writes to specified DS addresses in segment."""
    seg_off, seg_len, _ = seg_info(seg_num)
    chunk = V1[seg_off:seg_off+seg_len]
    sites = []
    for ins in md.disasm(chunk, seg_off):
        b = ins.bytes
        for ta in target_addrs:
            ta_b = bytes([ta & 0xFF, (ta >> 8) & 0xFF])
            if ta_b in b and ins.mnemonic == "mov":
                if "ptr [" in ins.op_str.split(",", 1)[0]:
                    sites.append((ins.address, ins.op_str, ta))
                    break
    return sites

def show_instruction_range(start, end, ds_labels=None):
    """Disassemble a code range with labels."""
    if ds_labels is None: ds_labels = {}
    chunk = V1[start:end]
    lines = []
    for ins in md.disasm(chunk, start):
        note = ""
        for addr, label in ds_labels.items():
            if f"0x{addr:x}" in ins.op_str:
                note = f"  ; {label}"
                break
        marker = ""
        if ins.mnemonic == "lcall": marker = "  [LCALL]"
        elif ins.mnemonic == "call": marker = "  [CALL]"
        elif ins.mnemonic in ("jmp","je","jne","jl","jg","jb","ja","jbe","jae","jle","jge"):
            for op in ins.operands:
                if op.type == 1 and op.imm < ins.address:
                    marker = "  [LOOP-BACK]"
                    break
        lines.append(f"  0x{ins.address:06x}: {ins.bytes.hex():<14} {ins.mnemonic:<6} {ins.op_str}{note}{marker}")
    return lines

def analyze(name, seg_num, ds_labels=None):
    print(f"\n{'='*80}")
    print(f"  {name} (Seg {seg_num})")
    print(f"{'='*80}\n")
    seg_off, seg_len, _ = seg_info(seg_num)
    funcs = find_function_starts(seg_num)
    funcs_by_size = sorted(funcs, key=lambda x: -x[1])
    print(f"Largest function: 0x{funcs_by_size[0][0]:06x} ({funcs_by_size[0][1]} B) ← Init candidate")
    print(f"2nd largest:      0x{funcs_by_size[1][0]:06x} ({funcs_by_size[1][1]} B) ← Eval candidate?")

    # Diff dispatches
    disps = find_diff_dispatch(seg_num)
    if disps:
        print(f"\nDifficulty dispatches found: {len(disps)}")
        for at, disp, jt_file in disps:
            jt_words = struct.unpack_from("<4H", V1, jt_file)
            print(f"  jmp [cs:bx+0x{disp:04x}] at file 0x{at:06x}, jump table:")
            for i, w in enumerate(jt_words):
                tgt_file = seg_off + w
                in_seg = "" if seg_off <= tgt_file < seg_off + seg_len else " (OUT OF SEG!)"
                print(f"    diff={i}: cs:0x{w:04x} → file 0x{tgt_file:06x}{in_seg}")
    else:
        print("\nNo difficulty dispatch found (jmp [cs:bx+imm] pattern).")

    # Random calls
    relocs = get_relocations(seg_num)
    rand_calls = [(r["offset"]-1, "rand_range") for r in relocs if r["is_internal"] and r["tgt_seg"] == 20 and r["tgt_ofs"] == 0x04E8]
    direct_rand = [(r["offset"]-1, "direct_rand") for r in relocs if r["is_internal"] and r["tgt_seg"] == 14]
    print(f"\nRandom calls: {len(rand_calls)} via random_range + {len(direct_rand)} direct seg14 calls")

    # Init function disassembly (top 30 lines)
    init_start = funcs_by_size[0][0]
    init_size = funcs_by_size[0][1]
    print(f"\n=== Init function header ({init_start:#x}, first 200 bytes) ===")
    for line in show_instruction_range(init_start, init_start + 200, ds_labels):
        print(line)

if __name__ == "__main__":
    if len(sys.argv) > 1:
        target = sys.argv[1].lower()
        puzzles = {
            "pizza": ("Pizza Pass", 37, {0x96D2: "DIFFICULTY_PIZZA?", 0x96D6: "TROLL_COUNT?"}),
            "caves": ("Stone Cold Caves", 24, {0x7A80: "DIFFICULTY?"}),
            "ferry": ("Captain Cajun Ferry", 26, {}),
            "toads": ("Titanic Tattooed Toads", 30, {}),
            "stone_rise": ("Stone Rise", 45, {0xA34E: "LEVEL?"}),
            "fleens": ("Fleens", 27, {0x7CEA: "DIFFICULTY?"}),
            "hotel": ("Hotel Dimensia", 28, {0x7E42: "DIFFICULTY"}),
            "mudball": ("Mudball Wall", 35, {0x9368: "DIFFICULTY?"}),
            "lions_lair": ("Lion's Lair", 48, {0xA83E: "DIFFICULTY?"}),
            "mirror": ("Mirror Machine", 42, {0x9C26: "DIFFICULTY?"}),
            "bubble": ("Bubblewonder Abyss", 34, {0x925A: "DIFFICULTY?"}),
        }
        if target in puzzles:
            name, seg, lbl = puzzles[target]
            analyze(name, seg, lbl)
        else:
            print(f"Unknown: {target}. Choose: {list(puzzles)}")
