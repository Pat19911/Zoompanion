"""
Smoke tests for ne_loader.

Run with:  python -m pytest tests/        (if pytest installed)
       or: python tests/test_ne_loader.py  (standalone)
"""
import os
import sys
from pathlib import Path

ROOT = Path(__file__).parent.parent
sys.path.insert(0, str(ROOT))

from ne_loader import NEBinary, get_disasm


def test_default_loads():
    ne = NEBinary.default()
    assert ne.alignment == 512, f"expected alignment 512, got {ne.alignment}"
    assert ne.num_segments == 191, f"expected 191 segments, got {ne.num_segments}"


def test_segment_28_is_hotel():
    """Hotel.MHK lives in seg 28 at file 0x23800 (verified by MHK push)."""
    ne = NEBinary.default()
    offset, length, flags = ne.segment_info(28)
    assert offset == 0x23800, f"Hotel seg 28 should be at 0x23800, got 0x{offset:X}"
    assert length == 16016, f"Hotel seg 28 should be 16016 bytes, got {length}"
    # flags: bit 0x100 = HAS_RELOC
    assert flags & 0x0100, "Seg 28 should have relocations"


def test_hotel_eval_function_findable():
    """Eval at file 0x254EA = seg-off 0x1CEA (4-byte NE-prolog, prolog scan finds 0x1CED)."""
    ne = NEBinary.default()
    prologs = ne.find_prologs(28)
    assert 0x1CED in prologs, f"Eval prolog at seg-off 0x1CED missing; found {prologs}"
    assert len(prologs) == 30, f"expected 30 prologs in seg 28, got {len(prologs)}"


def test_hotel_relocations():
    """Seg 28 should have ~622 relocations (verified)."""
    ne = NEBinary.default()
    relocs = ne.relocations(28)
    assert len(relocs) == 622, f"expected 622 relocs in seg 28, got {len(relocs)}"


def test_disasm_works():
    """Smoke: disassemble first instruction of Hotel Eval."""
    ne = NEBinary.default()
    md = get_disasm()
    data = ne.segment_data(28)
    insns = list(md.disasm(data[0x1CED:0x1D00], 0x1CED))
    assert len(insns) > 0, "no instructions decoded"
    # First insn of NE prolog: 'inc bp'
    assert insns[0].mnemonic == "inc"
    assert insns[0].op_str == "bp"


def run_all():
    tests = [
        test_default_loads,
        test_segment_28_is_hotel,
        test_hotel_eval_function_findable,
        test_hotel_relocations,
        test_disasm_works,
    ]
    failed = 0
    for t in tests:
        try:
            t()
            print(f"  ✓ {t.__name__}")
        except AssertionError as e:
            print(f"  ✗ {t.__name__}: {e}")
            failed += 1
        except Exception as e:
            print(f"  ✗ {t.__name__}: {type(e).__name__}: {e}")
            failed += 1
    print(f"\n{len(tests) - failed}/{len(tests)} passed")
    return failed == 0


if __name__ == "__main__":
    sys.exit(0 if run_all() else 1)
