"""Reusable loader for the v2 PE32 binary (ZoombinisLJ.exe).

Counterpart to ne_loader.py for the 16-bit v1. Provides:

  - VA / RVA / file-offset conversion
  - String index of all printable ASCII strings in .data and .rdata
  - Cross-reference: which `push imm32` instructions reference a given VA
  - Function-bound walking from a given VA until ret / retn
  - Capstone disassembler in 32-bit x86 mode

Use NEBinary's twin: PEBinary.default() respects $ZOOMBINI_V2_BIN, defaulting to
v2_bin/ZoombinisLJ.exe inside the project root.
"""
from __future__ import annotations

import os
import re
import struct
from dataclasses import dataclass, field
from functools import cached_property
from typing import Iterator

import capstone
import pefile


_PROJECT_ROOT = os.path.dirname(os.path.abspath(__file__))
_DEFAULT_PATH = os.environ.get(
    "ZOOMBINI_V2_BIN",
    os.path.join(_PROJECT_ROOT, "v2_bin", "ZoombinisLJ.exe"),
)


@dataclass
class Section:
    name: str
    va: int          # virtual address (incl. ImageBase)
    rva: int         # relative virtual address
    vsize: int       # virtual size
    file_off: int
    file_size: int
    raw: bytes = field(repr=False)

    def contains_va(self, va: int) -> bool:
        return self.va <= va < self.va + self.vsize


class PEBinary:
    """Load a PE32 binary and expose RE-friendly accessors."""

    def __init__(self, path: str = _DEFAULT_PATH) -> None:
        self.path = path
        with open(path, "rb") as f:
            self._raw = f.read()
        self._pe = pefile.PE(data=self._raw, fast_load=False)
        self.image_base: int = self._pe.OPTIONAL_HEADER.ImageBase
        self.entry_va: int = self.image_base + self._pe.OPTIONAL_HEADER.AddressOfEntryPoint
        self.sections: list[Section] = []
        for s in self._pe.sections:
            name = s.Name.rstrip(b"\x00").decode("ascii", "replace")
            raw = self._raw[s.PointerToRawData : s.PointerToRawData + s.SizeOfRawData]
            self.sections.append(
                Section(
                    name=name,
                    va=self.image_base + s.VirtualAddress,
                    rva=s.VirtualAddress,
                    vsize=s.Misc_VirtualSize,
                    file_off=s.PointerToRawData,
                    file_size=s.SizeOfRawData,
                    raw=raw,
                )
            )

    @classmethod
    def default(cls) -> "PEBinary":
        return cls(_DEFAULT_PATH)

    # -- address translation --------------------------------------------------

    def section_for_va(self, va: int) -> Section | None:
        for s in self.sections:
            if s.contains_va(va):
                return s
        return None

    def va_to_file_off(self, va: int) -> int | None:
        s = self.section_for_va(va)
        if s is None:
            return None
        rel = va - s.va
        if rel >= s.file_size:
            return None  # in BSS-like uninitialized vsize tail
        return s.file_off + rel

    def read_va(self, va: int, n: int) -> bytes:
        """Read n bytes starting at virtual address va. Returns b'' on miss."""
        s = self.section_for_va(va)
        if s is None:
            return b""
        rel = va - s.va
        if rel + n > s.file_size:
            n = max(0, s.file_size - rel)
        return s.raw[rel : rel + n]

    # -- C-string extraction --------------------------------------------------

    @cached_property
    def strings(self) -> dict[int, str]:
        """Map VA -> ASCII string for every >= 4-char printable null-terminated
        run in .rdata / .data. Indexes BOTH canonical starts (preceded by NUL)
        AND positions referenced by `push imm32`, since VC++6 string-pooling
        can merge tails ('foo.mhk' starting mid-byte after 'somefoo.mhk').
        """
        out: dict[int, str] = {}
        # Pass 1: canonical (NUL-prefixed) starts
        for s in self.sections:
            if s.name not in (".rdata", ".data"):
                continue
            data = s.raw
            n = len(data)
            i = 0
            while i < n:
                if i > 0 and data[i - 1] != 0:
                    i += 1
                    continue
                start = i
                while i < n and 0x20 <= data[i] < 0x7F:
                    i += 1
                if i > start + 3 and i < n and data[i] == 0:
                    out[s.va + start] = data[start:i].decode("ascii")
                i += 1
        # Pass 2: every push-imm32 target that points into .rdata/.data
        # and reads as a printable ASCII run gets added (catches packed tails).
        for ref_va in self.push_imm32_index:
            if ref_va in out:
                continue
            sec = self.section_for_va(ref_va)
            if sec is None or sec.name not in (".rdata", ".data"):
                continue
            rel = ref_va - sec.va
            if rel < 0 or rel >= sec.file_size:
                continue
            data = sec.raw
            j = rel
            while j < sec.file_size and 0x20 <= data[j] < 0x7F:
                j += 1
            if j > rel + 3 and j < sec.file_size and data[j] == 0:
                out[ref_va] = data[rel:j].decode("ascii")
        return out

    def find_string(self, needle: str) -> list[int]:
        """Return all VAs whose string equals or contains needle (substring match)."""
        return [va for va, s in self.strings.items() if needle in s]

    # -- code disassembly -----------------------------------------------------

    @cached_property
    def md(self) -> capstone.Cs:
        m = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        m.detail = True
        return m

    def disasm_at(self, va: int, max_bytes: int = 256) -> Iterator[capstone.CsInsn]:
        s = self.section_for_va(va)
        if s is None or s.name != ".text":
            return iter([])
        rel = va - s.va
        chunk = s.raw[rel : rel + max_bytes]
        return self.md.disasm(chunk, va)

    # -- push-imm32 cross references -----------------------------------------

    @cached_property
    def push_imm32_index(self) -> dict[int, list[int]]:
        """Map referenced-VA -> list of code VAs that do `push <ref>` (opcode 0x68).

        This is the cheap-and-effective xref method on VC++6 32-bit code:
        every printf-style call pushes its format string via 0x68 imm32. Only
        scans .text sections.
        """
        idx: dict[int, list[int]] = {}
        for s in self.sections:
            if s.name != ".text":
                continue
            data = s.raw
            n = len(data)
            i = 0
            while i < n - 4:
                if data[i] == 0x68:  # PUSH imm32
                    imm = struct.unpack_from("<I", data, i + 1)[0]
                    idx.setdefault(imm, []).append(s.va + i)
                    i += 5
                else:
                    i += 1
        return idx

    @cached_property
    def call_index(self) -> dict[int, list[int]]:
        """Map call-target-VA -> list of caller VAs (E8 rel32 direct calls only).

        Indirect calls (FF 15 / FF D0) are skipped — they need import-resolution.
        """
        idx: dict[int, list[int]] = {}
        for s in self.sections:
            if s.name != ".text":
                continue
            data = s.raw
            n = len(data)
            i = 0
            while i < n - 4:
                if data[i] == 0xE8:
                    rel = struct.unpack_from("<i", data, i + 1)[0]
                    target = s.va + i + 5 + rel
                    idx.setdefault(target, []).append(s.va + i)
                    i += 5
                else:
                    i += 1
        return idx

    # -- function bounds ------------------------------------------------------

    def function_end(self, start_va: int, max_bytes: int = 0x4000) -> int:
        """Walk forward from start_va until first ret / retn that isn't part
        of an interior basic block. Returns VA of the byte AFTER the ret.

        Heuristic: stops at first 0xC2 imm16 or 0xC3 not preceded by INT3.
        For VC++6 functions this is reliable; report-only, not authoritative.
        """
        last = start_va
        for ins in self.disasm_at(start_va, max_bytes):
            last = ins.address + ins.size
            if ins.mnemonic in ("ret", "retn", "retf"):
                return last
            if ins.mnemonic == "jmp" and ins.size == 5:
                # tail-call / dispatch — could be end-of-function. Be conservative.
                # We don't stop here; ret usually follows in same func tail.
                pass
        return last

    # -- prolog scan ----------------------------------------------------------

    @cached_property
    def function_starts_by_prolog(self) -> list[int]:
        """All VAs in .text that look like a VC++6 function prolog.

        Standard VC++6 prologs:
          55 8B EC                  push ebp; mov ebp, esp
          55 8B EC 81 EC ?? ?? ?? ?? sub esp, NN  (with locals)
          55 8B EC 83 EC ??         sub esp, NN8  (small locals)
          55 8B EC 6A FF            __try frame
        A function-start preceded by 0xCC (INT3 padding) is more confident.
        """
        starts: list[int] = []
        for s in self.sections:
            if s.name != ".text":
                continue
            for m in re.finditer(rb"\x55\x8b\xec", s.raw):
                starts.append(s.va + m.start())
        return starts

    @cached_property
    def function_starts(self) -> list[int]:
        """Union of (a) all direct-call targets and (b) all standard prologs.

        Direct-call targets (E8 rel32) catch FPO-optimized functions that lack
        the canonical `55 8B EC` prolog. This is the authoritative function-
        start set for grouping push-sites into their enclosing function.
        """
        s = set(self.call_index.keys()) | set(self.function_starts_by_prolog)
        # Filter to .text only
        return sorted(va for va in s if (sec := self.section_for_va(va)) and sec.name == ".text")

    def enclosing_function(self, va: int) -> tuple[int, int] | None:
        """Return (start_va, end_va) of the function containing va.

        Bounds: start = nearest known function-start <= va,
                end   = nearest known function-start > va, then trimmed back
                        past trailing 0xCC (INT3) padding.

        Functions can have multiple `ret`s (early returns), so we DON'T stop
        at the first one. Returns None only if va is before the first
        known function start.
        """
        import bisect
        starts = self.function_starts
        i = bisect.bisect_right(starts, va) - 1
        if i < 0:
            return None
        start = starts[i]
        end_excl = starts[i + 1] if i + 1 < len(starts) else start + 0x10000
        sec = self.section_for_va(start)
        if sec is None:
            return None
        # Trim trailing 0xCC (INT3 alignment padding inserted by VC++6 linker)
        rel_end = end_excl - sec.va
        rel_end = min(rel_end, sec.file_size)
        while rel_end > start - sec.va and sec.raw[rel_end - 1] == 0xCC:
            rel_end -= 1
        return (start, sec.va + rel_end)


def _smoke() -> None:
    pe = PEBinary.default()
    print(f"path        : {pe.path}")
    print(f"ImageBase   : 0x{pe.image_base:08X}")
    print(f"#sections   : {len(pe.sections)}")
    print(f"#strings    : {len(pe.strings)}")
    print(f"#push-xrefs : {len(pe.push_imm32_index)}")
    print(f"#call-tgts  : {len(pe.call_index)}")
    print(f"#prologs    : {len(pe.function_starts_by_prolog)}")
    # sanity: locate a known marker
    for needle in ("Upper bridge accepts:", "pizza.mhk", "bridge.mhk"):
        hits = pe.find_string(needle)
        if hits:
            va = hits[0]
            xrefs = pe.push_imm32_index.get(va, [])
            print(f'  "{needle}": VA 0x{va:08X}, {len(xrefs)} push xrefs')


if __name__ == "__main__":
    _smoke()
