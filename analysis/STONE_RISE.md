# Stone Rise — Verifizierte Analyse (2026-04-25)

> **Konfidenz:** HOCH. Marker: `Slides.MHK` push aus Seg 45.
> Diese Doku ergänzt die existierende `STONE_RISE.md` mit Master-Analyse-Daten.

## Verifizierte Basis

| Eigenschaft | Wert |
|-------------|------|
| Code-Segment | **45** |
| Datei-Offset | 0x06CA00..0x071C61 |
| Segment-Länge | 21089 B (0x5261) |
| MHK-Datei | Slides.MHK (DS:0x2330, push bei file 0x06CA53) |
| Anzahl Funktionen | 35 |
| Internal Relocations | 918 |
| **Difficulty-Dispatches** | **4** (relativ wenig) |
| `random_range` (seg20) | 1 (!) |
| Direct `rand()` | 6 |
| External Seg-Calls | seg157 (524x — geteiltes Zoombini-Daten-Segment) |
| DS-State-Range | 0xA34A..0xA54E (primär) |

## Funktions-Layout (top 5)

| Offset | Seg-Rel | Größe | Rolle |
|--------|---------|-------|-------|
| 0x06D2F5 | 0x08F5 | 3659 B | **Init** |
| 0x06E228 | 0x1828 | 1949 B | Eval / Pairing-Logic? |
| 0x06F0B4 | 0x26B4 | 1713 B | Match-Function (laut alter Doku) |

## Difficulty-Dispatches (4)

| File | Diff 0 | Diff 1 | Diff 2 | Diff 3 |
|------|--------|--------|--------|--------|
| 0x06F1F6 | 0x06F1FB | 0x06F24C | 0x06F6D8 | 0x06F6D8 |
| 0x06F87C | 0x06F881 | 0x06F8D3 | 0x06F919 | 0x06F95E |
| 0x06FD06 | 0x06FD0B | 0x06FD3B | 0x06FD5E | 0x06FD81 |
| 0x070028 | 0x07002D | 0x070051 | 0x070075 | 0x070099 |

## DS-State-Variablen (Init-Schreibziele)

```
DS:0xA466 (6x)   — pair-related state
DS:0xA458 (5x)   — ?
DS:0xA464 (4x)   — ?
DS:0xA34C (4x)   — ?
DS:0xA456 (4x)   — ?
DS:0xA34A (3x)   — ?
DS:0xA54E (3x)   — ?
DS:0xA452 (3x)   — ?
DS:0xA4A4 (3x)   — pair_count? (war so in alter Doku)
```

## Manual-Mechanik

- Aufzug-Plattformen mit Pairing-Mechanik
- Zoombinis paaren sich nach gemeinsamen Attributen
- Plattform steigt wenn Paar passt

## Pool/Selection-Hinweise

Nur **1 random_range** + 6 direkte rand-Aufrufe. Das ist wenig — Stone Rise scheint
**weitgehend deterministisch** zu sein. Die 6 direkten rand sind vermutlich:
- Initial-Anordnung der Plattformen (1-2x)
- Animation-Variation (Rest)

**Pool-Generation unwahrscheinlich** bei so wenigen Random-Calls.

## Wichtig: Seg 157 mit 524 Calls

Stone Rise ruft 524× in Seg 157 — das ist das **gemeinsame Zoombini-Daten-Segment** mit
Attributen pro Zoombini. Das ist auch von Ferry, Fleens etc. genutzt.

## Was noch zu tun

1. Init (3659 B) komplett disassemblieren
2. Pairing-Algorithmus identifizieren — wie wird entschieden welche Zoombinis paaren
3. Plattform-Auswahl-Logic verstehen

## Querverweise

- `analysis/STONE_RISE.md` (vorherige Doku — größtenteils korrekt)
- `Solvers/StoneRiseSolver.cs`


---

# v2 (PE32) — Stone Rise / Slides

> **Status: Init nicht eindeutig lokalisiert.** Vollständige Analyse: `V2_VARIABLE_MAP.md` § 11.

## Problem
Wie bei Ferry: `slides.mhk`-Push (`0x00439229`) liegt in der WinMain-CFG-Validierung
(`0x00438E30`), nicht in der Puzzle-Init.

## Match-Code-Verifikation
v1 nutzt für Stone Rise Match-Codes `0x1FE/0x1FF/0x200/0x201/0x1F5`.
**Keine** dieser Konstanten ist in v2-PE als Literal vorhanden — sie liegen
vermutlich in den MHK-Daten (Slides.MHK ist v1↔v2 bitweise identisch laut
`V1_VS_V2_COMPARISON.md`).

## Kandidaten-Liste

| Funktion | Größe | Bewertung |
|----------|-------|-----------|
| **`0x00440850`** | **4704 B** | bester Kandidat — größte Funktion zwischen Smoke und Tunnels, Cmp-Konstanten 1..7,30 (passt zu Difficulty 1..3 + Zoombini-counts) |
| `0x00441E30` | 912 B | „Cheat on" — vermutlich Cheat-Helper |
| `0x00444070` | 2928 B | data range = Smoke-Region — wahrscheinlich Smoke-related |
| `0x00448500` | 2352 B | engine-helper |

**Konfidenz für `0x00440850` als Stone-Rise-Init:** Speculative (Pattern-basiert,
kein harter Marker).

## Lücke
Stone-Rise-Solver für v2 derzeit nicht möglich. Verifikation erfordert
Live-Memory-Diff oder zusätzliche Marker-Suche.
