# Lion's Lair — Tiefenanalyse Pool/Selection (verifiziert 2026-04-25)

## Pattern: A — Pool + TARGET-Selection (wie Cliffs)

**Zweites Puzzle nach Cliffs mit verifiziertem Pool/Selection-Pattern.**

## Verifizierte Init-Struktur (0x79131, 2726 B)

### Schlüssel-Region: Klassifizierungs-Loop bei 0x7932D

```c
// OUTER LOOP — pro Zoombini eine Iteration
for (zb_idx = ...) {
    bx = zb_idx * 4
    eax = far_array[bx + 2]    // Zoombini-Attribut-Bitmask (4 bytes)
    [bp-0x40] = eax

    // BYTE-SWAP: rebuilt as edx (4 nibbles)
    [bp-0x38] = byte_swap(eax)

    // INNER LOOP — über alle Pool-Kandidaten
    for (si = 0; si < N; si++) {
        // Modulo-Division: si / 0x28 (= 40 Slots)
        bx = si / 40
        di = si % 40
        if (bx == di) skip   // diagonale ausschließen

        // Lade Kandidat aus Stack-Array
        eax = stack_array[bx * 4]
        eax &= 0xF                       // Hair nibble
        // Vergleich mit zoombini.[bp-0x38] & 0xF
        ...
    }
}
```

### Vergleich mit Cliffs

| Aspekt | Cliffs | Lions Lair |
|--------|--------|------------|
| Outer-Loop | `[bp-0x1E] = 0` count | `zb_idx` |
| Bitmask-Speicher | `[bp-0x3E]` (rebuild) | `[bp-0x38]` (rebuild) |
| Inner-Loop-Counter | `[bp-0x1A]` | `si` |
| Nibble-Vergleich | `and eax, 0xF; cmp ...` | identisch |
| Pool-Größe | 500 (4×125) | **40 Slots** (`0x28`) |

→ **Praktisch identische Klassifizierungs-Logik**, nur mit 40 statt 500 Pool-Größe.

### Pool-Größe: 40

Lions Lair hat 40 Slots im Pool. Das passt zu:
- 5 (Werte) × 8 (Tunnel-Konfigurationen) = 40
- Oder 4 (Attribut-Typen) × 10 (Permutationen) = 40

## DS-State

```
DS:0xA90E (6x in Init) — primary state
DS:0xA840 (4x)         — Tunnel-Config
DS:0xA852, 0xA85A      — Tunnel-Config
DS:0x9EF4 (3x)         — engine state
```

## Backward Jumps in Init

| Jump | Distance | Bedeutung |
|------|----------|-----------|
| 0x7979A → 0x7932D | **1133 B** | Outer Pool-Loop (über Zoombinis × Kandidaten) |
| 0x7978A → 0x7938F | **1019 B** | Inner Pool-Loop |
| 0x79B93 → 0x799E3 | 432 B | Selection-Loop (Pool-Pick mit random_range) |

Random-Range-Calls bei **0x79970** und **0x799E7** — beide in der Selection-Region nach
dem Pool. Genau wie bei Cliffs!

## Spielmechanik (mit Pattern-A-Verständnis)

- 40 mögliche Tunnel-Konfigurationen werden vorberechnet
- Für jede wird gezählt: wie viele Zoombinis matchen?
- Selection wählt eine Konfiguration mit „guter" Match-Verteilung
- → Auf höheren Diff-Levels wird auch hier eine **gleichverteilte 50/50-Lösung gezielt** angepeilt

## Was noch zu tun

1. Selection-Algorithmus genau identifizieren (analog Cliffs TARGET=N/2 oder anders?)
2. Vergleich mit Cliffs: Werden gleiche Werte angepeilt? (8 von 16?)
3. Verifizieren, ob der „Lion's Paw" am Ende eine separate Mechanik ist (vermutlich nicht)

## Querverweise

- `analysis/ALLERGIC_CLIFFS.md` (Pattern A Referenz)
- `analysis/LIONS_LAIR_VERIFIED.md` (Master-Analyse)
- `Solvers/LionsLairSolver.cs`


---

# v2 (PE32) — Lion's Lair / Tunnels-Code-Karte

> Vollständige Tabelle: `V2_VARIABLE_MAP.md` § 12.

## Code-Lokalisation

| | v1 | v2 |
|---|----|----|
| Code | Seg 48 | `.text` 0x0044FF30..0x00450550 (+ Helper bis 0x453A30) |
| MHK-Loader | — | `0x0044FF30` (1578 B, 1 Caller) |
| Wrapper | — | `0x0044E9D0` |

## Verifizierte / Probable Mappings

| v1 DS | v2 VA | Bedeutung | Konfidenz |
|-------|-------|-----------|-----------|
| `0xA83E` ⚠️ | **`0x004A2B6A`** | DIFFICULTY (0..3) | **Verified** (`movsx eax, word [0x4A2B6A]; cmp eax, 3; ja; jmp [eax*4+0x45059C]`) |
| `0xA79A` rule[0] (13 bytes) | `0x004A2C4D` (8 writes scale=1) | Rule-Struktur 0 | Probable |
| `0xA7A7` rule[1] (13 bytes) | `0x004A2C52` (8 writes scale=1) | Rule-Struktur 1 | Probable |
| `0xA90E` 📊 | `0x004A2B68` (4W) | primary state (benachbart zu Difficulty) | Speculative |
| `0xA840/A852/A85A` | `0x004A2B60/0x004A2B62` | Tunnel-Config | Speculative |
| State machine | `0x004A3830` (indirekt scale=2) | shared state | Speculative |

## Solver-Stand
Difficulty Verified, Rule-Strukturen Probable — reicht für Detection. Eval-
Logik (Rule-Match) noch nicht im Detail durchanalysiert.
