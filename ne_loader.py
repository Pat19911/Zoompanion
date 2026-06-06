"""
Common loader for the Zoombinis NE binary.

Replaces the boilerplate that was duplicated across 30+ analysis scripts:
NE-header parsing, segment-table lookup, function-prolog scan, relocation walk.

Usage:
    from ne_loader import NEBinary, get_disasm

    ne = NEBinary.default()           # Reads $ZOOMBINI_BIN or default path
    seg_off, seg_len, flags = ne.segment_info(28)
    data = ne.segment_data(28)
    prologs = ne.find_prologs(28)
    relocs = ne.relocations(28)

    md = get_disasm()                 # Capstone, 16-bit x86, detail=True
    for ins in md.disasm(data, 0):
        ...
"""
import os
import struct
from pathlib import Path

DEFAULT_BIN_PATH = Path(__file__).parent / "v1_bin" / "ZOOMBINI._EX"


class NEBinary:
    """Parses an NE-format Windows 16-bit executable."""

    def __init__(self, path):
        self.path = Path(path)
        with open(self.path, "rb") as f:
            self.data = f.read()
        ne_off = struct.unpack_from("<I", self.data, 0x3C)[0]
        self.ne_header_offset = ne_off
        self.seg_table_offset = ne_off + struct.unpack_from("<H", self.data, ne_off + 0x22)[0]
        self.alignment_shift = struct.unpack_from("<H", self.data, ne_off + 0x32)[0]
        self.alignment = 1 << self.alignment_shift
        self.num_segments = struct.unpack_from("<H", self.data, ne_off + 0x1C)[0]

    @classmethod
    def default(cls):
        """Use $ZOOMBINI_BIN env var, or fall back to v1_bin/ZOOMBINI._EX."""
        path = os.environ.get("ZOOMBINI_BIN", str(DEFAULT_BIN_PATH))
        return cls(path)

    def segment_info(self, seg_num):
        """Return (file_offset, length, flags) for 1-based segment number."""
        rec = self.seg_table_offset + (seg_num - 1) * 8
        sector, length, flags, _ = struct.unpack_from("<HHHH", self.data, rec)
        return sector * self.alignment, length, flags

    def segment_data(self, seg_num):
        offset, length, _ = self.segment_info(seg_num)
        return self.data[offset:offset + length]

    def segment_bounds(self, seg_num):
        """(start, end) file offsets."""
        offset, length, _ = self.segment_info(seg_num)
        return offset, offset + length

    def find_prologs(self, seg_num):
        """
        Find function prologs in the segment.

        NE-style C compiler emits one of two prolog patterns:
          short:  45 55 8B EC          (inc bp; push bp; mov bp, sp)
          full:   B8 ?? ?? 90 45 55 8B EC  (mov ax, ds; nop; <short>)

        Returns sorted list of seg-offsets (start of full prolog when present).
        """
        data = self.segment_data(seg_num)
        prologs = []
        for i in range(len(data) - 4):
            if data[i:i + 3] == b"\x55\x8b\xec":
                if i >= 1 and data[i - 1] == 0x45:
                    if i >= 5 and data[i - 5] == 0xB8 and data[i - 2] == 0x90:
                        prologs.append(i - 5)
                    else:
                        prologs.append(i - 1)
        return sorted(set(prologs))

    def relocations(self, seg_num):
        """Return list of relocation records for the segment."""
        offset, length, flags = self.segment_info(seg_num)
        if not (flags & 0x0100):
            return []
        reloc_off = offset + length
        n = struct.unpack_from("<H", self.data, reloc_off)[0]
        result = []
        for i in range(n):
            rec = reloc_off + 2 + i * 8
            type_, fl, ofs, tgt1, tgt2 = struct.unpack_from("<BBHHH", self.data, rec)
            result.append({
                "type": type_,
                "flags": fl,
                "offset": ofs,
                "target1": tgt1,
                "target2": tgt2,
                "reltype": ["INTERNAL", "IMPORT_ORD", "IMPORT_NAME", "OS_FIXUP"][fl & 3],
            })
        return result

    def function_bounds(self, seg_num, start):
        """
        Walk forward from start until a retf/retf-imm is found.
        Returns end offset (exclusive). Capstone-based.
        """
        md = get_disasm()
        data = self.segment_data(seg_num)
        seg_len = len(data)
        next_prolog = next((p for p in self.find_prologs(seg_num) if p > start), seg_len)
        pos = start
        while pos < next_prolog:
            insns = list(md.disasm(data[pos:next_prolog], pos))
            if not insns:
                return pos
            for ins in insns:
                if ins.bytes and ins.bytes[0] in (0xCA, 0xCB):
                    return ins.address + ins.size
            pos = insns[-1].address + insns[-1].size
            if pos <= start:
                return pos
        return next_prolog


def get_disasm():
    """Return a configured Capstone disassembler for 16-bit x86 with detail enabled."""
    from capstone import Cs, CS_ARCH_X86, CS_MODE_16
    md = Cs(CS_ARCH_X86, CS_MODE_16)
    md.detail = True
    return md
