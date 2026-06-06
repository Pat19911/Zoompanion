# Zoompanion

A live helper and solver for *Logical Journey of the Zoombinis*, built on top of a
from-scratch reverse-engineering of the game's puzzle algorithms.

Zoompanion attaches to the **running** game, reads puzzle state directly out of
process memory, and shows live recommendations in a small always-on-top overlay
— which zoombini to send where, which bridge accepts it, which seat or cell it
belongs in. It never modifies the game; it only reads.

> **Which version?** The live overlay currently supports **only the v2 (PE32)
> re-release** (Riverdeep, 2001/2002). The original 1996 16-bit release
> (Brøderbund/TERC, `ZOOMBINI._EX`) is used **only as a reverse-engineering
> reference** for the algorithms — it is **not yet supported** by the live tool.

> ⚠️ Game binaries are **not** included and are not required to build. You need a
> legal copy of the v2 re-release to use the live overlay. The code is MIT-licensed.

---

## What it does

The overlay (`ZoombiniHelper`) runs next to the v2 (PE32) game, auto-detects the
active puzzle, and renders a tailored helper for it. It waits for the game to
start, re-asserts itself on top when the game grabs focus, and survives round
changes without manual interaction.

### Puzzles with a full helper

| Puzzle | What the helper gives you |
|--------|---------------------------|
| **Allergic Cliffs** | Allergy rules + per-zoombini "must / may go on which bridge" |
| **Stone Cold Caves** | 2D cave filter decoded into plain-language accept rules |
| **Pizza Pass** (Trolls) | Exact topping set each active troll wants |
| **Hotel Dimensia** | Axis attributes + a complete Diff-3 placement plan |
| **Fleens!** | The round's swap permutation, brute-forced and explained |
| **Mudball Wall** | Progressive hints: property→wall-dimension mapping + buttons |
| **Stone Rise** | Full solver + a live mini-map highlighting the target slot |
| **Captain Cajun's Ferryboat** | Solver with seat layout mini-map + target seat |
| **Bubblewonder Abyss** | Background solver that produces a stable, step-by-step plan |

Every other puzzle and the between-puzzle locations get a generic "where you are"
panel. Held-zoombini detection is exact (it reads the engine's drag marker), so
recommendations focus on whatever you're currently holding.

### Languages

The overlay UI is fully localized in **German, English, French, Spanish and
Italian**. A one-time picker on first start remembers your choice (next to the
EXE); the solver's internal diagnostics stay German by design.

`F12` writes a memory dump for diagnostics; errors go to
`%TEMP%\zoombini-helper.log`.

---

## Layout

```
.
├── Program.cs                       Entry point (language picker → overlay)
├── ZoombiniHelper.csproj            WinForms app — net8.0-windows
│
├── UI/                              Overlay app (Win32-facing)
│   ├── HelperOverlay.cs             Always-on-top overlay, per-tick orchestration
│   ├── LanguageSelectionForm.cs     First-run language picker
│   ├── ProcessAttacher.cs           Attach to the running game / waiting state
│   ├── HotkeyDispatcher.cs · WindowDragHandler.cs · TopmostEnforcer.cs
│   └── Rendering/                   One renderer per puzzle (+ dispatcher, grid)
│
├── src/ZoombiniHelper.Core/         Pure domain logic — net8.0, no Win32, testable
│   ├── IMemoryReader.cs             Memory-read abstraction (mocked in tests)
│   ├── Puzzles/                     Detection registry + per-puzzle metadata
│   ├── Bubblewonder/                State model, mechanism decode + Simulator/solver
│   ├── Localization/                Loc.T lookup + embedded {de,en,fr,es,it}.json
│   ├── Diagnostics/                 Memory-dump writer + binary-identity check
│   ├── Drag/                        Held-zoombini detection, pickup history
│   ├── *State.cs / *Solver.cs       Per-puzzle snapshots and solvers
│   └── ZoombiniVariants.cs          Localized attribute / variant names
│
├── tests/ZoombiniHelper.Tests/      xUnit tests for Core (run on Linux)
│
├── analysis/                        One canonical .md per puzzle + cross-cutting docs
│   ├── SEGMENT_MAP.md               Authoritative segment ↔ puzzle map
│   ├── ALLERGIC_CLIFFS.md · BUBBLEWONDER.md · HOTEL_DIMENSIA.md · …
│   └── _archive/                    Outdated / duplicate docs
│
├── *.py                            Reusable RE tooling
│   ├── ne_loader.py                 NE parser (v1: segments, prologs, relocations)
│   ├── mohawk_parser.py             MHK archive parser
│   ├── bitmap_decoder.py            tBMP decoder (LZ77 + RLE8)
│   └── locate_markers.py · locate_mhk.py · …
│
├── CLAUDE.md                        Project methodology — read before writing code
├── requirements.txt                Python deps (capstone, pefile)
└── .gitignore                       Excludes binaries, build output, scratch/
```

---

## Build

```bash
# C# (Windows-targeting; cross-compiles on Linux)
dotnet build
dotnet test tests/ZoombiniHelper.Tests          # 171 tests, run on Linux

# Self-contained single-file overlay EXE for a Windows VM next to the game
dotnet publish ZoombiniHelper.csproj -c Release -r win-x64 \
    --self-contained true -p:PublishSingleFile=true -o publish/
```

```bash
# Python RE tooling (analysis only — not needed to run the overlay)
pip install -r requirements.txt
export ZOOMBINI_BIN=/path/to/ZOOMBINI._EX         # v1 NE binary — RE reference
export ZOOMBINI_V2_BIN=/path/to/ZoombinisLJ.exe   # v2 PE32 binary — RE reference
python3 tests/test_ne_loader.py
```

---

## Confidence levels

Memory addresses and decoding rules are tagged in code/docs with one of:

| Level | Meaning |
|-------|---------|
| **Verified** | Init + Eval disassembled **and** empirically validated against the running game |
| **Probable** | Hard code marker (printf or MHK push) verified, but state variables inferred |
| **Speculative** | Educated guess — do not trust without empirical verification |

The reverse-engineering targets v1 (`ZOOMBINI._EX`, 16-bit NE) for the algorithms;
the live overlay reads the v2 (PE32) re-release, whose puzzle logic is identical.

---

## Methodology

Read **`CLAUDE.md`** before writing code. It encodes hard-won lessons about
reverse-engineering this game — every rule has a historical anchor (a real mistake
worth not repeating) — plus the codebase-hygiene rules (cleanup cadence, naming,
archival, where throwaway scripts go).

## License

MIT for the analysis code and helper. Game binaries are **not** included — you need
a legal copy of the original ISO/CD to run the live overlay.
