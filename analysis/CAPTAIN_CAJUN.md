# Captain Cajun's Ferryboat — Verifizierte Analyse (2026-04-25)

> **KRITISCHE KORREKTUR:** Ferry liegt in **Seg 26**, NICHT in Seg 41 wie bisherige Doku
> behauptete. Verifiziert durch:
> - `Ferry.MHK` push aus Seg 26 (file 0x01D922)
> - `"Play FrogMan SCRB id:"` push aus Seg 26 (file 0x01E6E4)
> Captain Cajun ist offenbar intern als "FrogMan" referenziert.

## Verifizierte Basis

| Eigenschaft | Wert |
|-------------|------|
| Code-Segment | **26** |
| Datei-Offset | 0x01D800..0x01F4BD |
| Segment-Länge | 7357 B (0x1CBD) |
| MHK-Datei | Ferry.MHK (DS:0x0F02) |
| Anzahl Funktionen | 15 |
| Internal Relocations | 274 |
| **Difficulty-Dispatches** | **5** |
| `random_range` (seg20) | 9 |
| Direct `rand()` | 0 |
| DS-State-Range | 0x7C3E..0x7C96 (primär — NICHT 0xA4xx wie früher angenommen!) |

## Funktions-Layout (top 5)

| Offset | Seg-Rel | Größe | Rolle |
|--------|---------|-------|-------|
| 0x01E184 | 0x0984 | 1138 B | **Init** |
| 0x01EA71 | 0x1271 | 931 B | Eval-Handler? |
| 0x01DE15 | 0x0615 | 879 B | Helper |

## Difficulty-Dispatches (5 Stellen)

| File | Branch 0 | Branch 1 | Branch 2 | Branch 3 |
|------|----------|----------|----------|----------|
| 0x01E02C | 0x01E031 | 0x01E084 | 0x01E084 | 0x01E084 |
| 0x01E3AE | 0x01E3B3 | 0x01E3BA | 0x01E3C1 | 0x01E3C8 |
| 0x01E80A | 0x01E80F | 0x01E83A | 0x01E8A7 | 0x01E907 |
| 0x01EE6B | 0x01EE70 | 0x01EE85 | 0x01EE85 | 0x01EE85 |
| 0x01F0A9 | 0x01F0AE | 0x01F0B4 | 0x01F0BA | 0x01F0C0 |

Bei 3 Dispatches sind Diff 1-3 degeneriert (zeigen alle aufs gleiche Ziel) → nur Diff 0 hat
einen separaten Pfad.

## DS-State-Variablen (Init-Schreibziele)

```
DS:0x7C4C (9x)   — vermutlich Sitzplatz-Counter oder primary state
DS:0x7C3E (5x)   — ?
DS:0x7C76 (4x)   — Sitzplatz-Position
DS:0x7C74 (4x)   — Sitzplatz-Position
DS:0x7C50 (4x)   — ?
DS:0x7C4E (4x)   — ?
DS:0x7C6C (4x)   — ?
DS:0x7C96 (4x)   — ?
```

**NICHT 0xA4xx wie in `FerryboatSolver.cs` und alten Dokus!** Die alte Adressen-Annahme
war von Seg 45 (= Stone Rise) abgeleitet, nicht von Seg 26 (= Ferry).

## Manual-Mechanik

- Sitzplatz-Gitter (Linien bis 4×4)
- Jeder Zoombini muss mindestens **ein Attribut** mit jedem Nachbarn teilen
- Höhere Levels: größeres Gitter, mehr Constraints

## Pool/Selection-Hinweise

9 `random_range`-Aufrufe für die Sitzplatz-Anordnung. Vermutlich:
- 1× pro Sitzplatz (max ~16) für Position
- Oder 1× für jede Adjazenz-Constraint-Generierung

**Kein Pool-Generation-Loop sichtbar.**

## Bekannter Ferry-Mechanik-Korrekturbedarf

Die alte `FERRYBOAT_DEEP.md` (jetzt `FERRYBOAT_OR_STONERISE_LEGACY_DEEP.md`) behauptete
DS-Adressen wie `0xA34E` (difficulty), `0xA468` (zb_count), `0xA4A4` (pair_count). **All
diese gehören zu Stone Rise (Seg 45), nicht zu Ferry.**

Die echten Ferry-DS-Adressen liegen im Bereich **0x7C3E..0x7C96**.

## Was noch zu tun

1. **Init komplett disassemblieren** (0x01E184..0x01E5F4)
2. **DS-State-Map der Ferry-Variablen erstellen** mit konkreten Bedeutungen
3. **`FerryboatSolver.cs` neu implementieren** mit korrekten Adressen 0x7Cxx
4. **Eval-Funktion finden** (vermutlich 0x01EA71)

## Querverweise

- `analysis/FERRYBOAT_OR_STONERISE_LEGACY*.md` (alte Doku — Spielregeln richtig, Adressen falsch)
- `Solvers/FerryboatSolver.cs` (Adressen MÜSSEN korrigiert werden)
- `python3 per_puzzle_analyzer.py ferry` (zeigt Init aus Seg 26)


---

# v2 (PE32) — Captain Cajun's Ferry

> **Status: Init nicht eindeutig lokalisiert.** Vollständige Analyse: `V2_VARIABLE_MAP.md` § 3.

## Problem
`ferry.mhk`-Push (`0x0040FF04`) liegt in der WinMain-CFG-Validierung (`0x0040FE20`),
nicht in der Puzzle-Init. Anders als bei v1 ist Ferry über den `ferry.mhk`-String
nicht direkt als Code-Region anhängbar.

## Kandidaten-Liste (Code-Layout zwischen Caves und Fleens)

| Funktion | Größe | Bewertung |
|----------|-------|-----------|
| `0x00408BB0` | 944 B | beste Kandidat (data range puzzle-eigen) |
| `0x00408F60` | 1472 B | ebenfalls plausibel |
| `0x004086A0` | 1296 B | wahrscheinlich Caves-Subroutine |
| `0x00410520` | 3408 B | enthält `Play FrogMan SCRB id:` — Frogman-Helper, puzzle-übergreifend |

## Mappings: keine

Kein Verified/Probable-Mapping möglich solange die Init-Funktion nicht identifiziert
ist. Solver-Übertragung auf v2 erfordert entweder einen zusätzlichen harten Marker
oder Live-Memory-Diff zwischen "Ferry geladen" und "Ferry nicht geladen".
