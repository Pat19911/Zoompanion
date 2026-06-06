# Stone Cold Caves — Tiefenanalyse Hauptgenerator (verifiziert 2026-04-25)

## Pattern: Modular Sequential Generation (Pattern C)

**Verifiziert** durch direkte Disassembly des Haupt-Generators bei file 0x1AE79.

## Funktions-Struktur

| Funktion | File | Größe | Rolle |
|----------|------|-------|-------|
| Init/Setup | 0x18C00 | 2373 B | Setup, Resource-Loading, KEIN random_range |
| **Cave-Generator** | **0x1AE79** | **655 B** | Pro-Difficulty Cave-Slot-Generation |
| Helper | 0x1AC1A | 426 B | 3 random_range — Sub-Generator? |
| Helper | 0x19F0F | 947 B | 2 random_range |
| Helper | 0x19E45 | 174 B | 2 random_range |
| Helper | 0x1B235 | 369 B | 1 random_range |

## Cave-Generator (0x1AE79) — Vollständig analysiert

### Parameter und Setup

```c
void cave_generator(int difficulty_param) {  // [bp+6], 1..4 (1-basiert!)
    // Initialisiere Permutation-Array [bp-0x1A] mit Werten 0..6
    int perm[7];
    for (int i = 0; i < 7; i++) perm[i] = i;

    // Nullt 11 Cave-Slots: [si*2 + 0x7A48] und [si*2 + 0x7A5E]
    for (int i = 0; i < 11; i++) {
        cave_filter_type[i]  = 0;   // DS:0x7A48 + i*2
        cave_filter_value[i] = 0;   // DS:0x7A5E + i*2
    }

    // 1-basiert → 0-basiert
    int diff = difficulty_param - 1;
    if (diff > 3) goto END;

    // Difficulty-Dispatch
    jmp [cs:bx*2 + 0x2500]  // 4 Branches
}
```

### Difficulty-Branches (verifiziert)

```c
// === DIFF 0 (1) "Not So Easy" — alle 5 Höhlen aktivieren ===
case 0:  // file 0x1AECE
    for (si = 1; si < 6; si++) {
        cave_filter_type[si] = 1;   // active
    }
    break;

// === DIFF 1 (2) "Oh So Hard" — 2 zufällige Höhlen ===
case 1:  // file 0x1AEE6
    [bp-6] = 5;                       // remaining
    [bp-8] = random_range(2, 2) = 2;  // num_to_pick (immer 2)
    for (si = 0; si < 2; si++) {
        idx = random_range([bp-6], 1);  // random 1..N
        bx = perm[idx] * 2;
        cave_filter_type[bx] = 1;
        // Fisher-Yates shuffle: remove perm[idx] from array
        for (di = idx; di < [bp-6]+1; di++) {
            perm[di] = perm[di+1];
        }
        [bp-6]--;
    }
    break;

// === DIFF 2 (3) "Very Hard" — 2 Iterationen mit Offset 0 und 5 ===
case 2:  // file 0x1AF4F
    for (loop = 0; loop < 2; loop++) {
        offset = (loop == 0) ? 0 : 5;
        // Re-init perm[0..6]
        for (si = 0; si < 7; si++) perm[si] = si;

        [bp-6] = 5;
        num_to_pick = random_range(2, 2) = 2;
        for (si = 0; si < 2; si++) {
            idx = random_range([bp-6], 1);
            bx = (perm[idx] + offset) * 2;
            cave_filter_type[bx] = 1;
            // Fisher-Yates shuffle (siehe Diff 1)
            ...
        }
    }
    break;

// === DIFF 3 (4) "Very, Very Hard" — leer/anderer Pfad ===
case 3:  // file 0x1AFF5
    // Direkt zu END — Diff 3 wird vermutlich woanders gehandhabt
    break;
```

### Attribut-Wert-Kodierung (zwei Loops nach der Generation)

Für jeden der 11 Slots, falls `cave_filter_type[si] != 0`:

```c
// LOOP 1: PRIMARY ATTRIBUTE (file 0x1AFF7..0x1B06D)
attr_type = [0x7A82]      // primary attribute type (0..3)
if (attr_type > 3) skip
jmp [cs:attr_type*2 + 0x24F8]
// 4 cases: offset = 0, 5, 10, 15
// → cave_filter_value[si] = primary_attr_value + offset

// LOOP 2: SECONDARY ATTRIBUTE (file 0x1B071..0x1B0E2)
attr_type = [0x7A84]      // secondary attribute type
if (attr_type > 3) skip
jmp [cs:attr_type*2 + 0x24F0]
// 4 cases: offset = 0, 5, 10, 15
// Speichert in cave_filter_value[si] (überschreibt!)
```

**20-Wert-Encoding:** Werte 1..5 = Hair, 6..10 = Eyes, 11..15 = Nose, 16..20 = Feet.

## Verifizierte DS-Variablen

| Adresse | Bedeutung |
|---------|-----------|
| `DS:0x7A80` | Difficulty (1..4, 1-basiert) |
| `DS:0x7A82` | Primary attribute type (0..3) |
| `DS:0x7A84` | Secondary attribute type (0..3) — wichtig für Diff 2+ |
| `DS:0x7A48..0x7A5C` | `cave_filter_type[11]` — welche Caves aktiv (0/1) |
| `DS:0x7A5E..0x7A72` | `cave_filter_value[11]` — Attribut-Wert + Offset (1..20) |

## Mechanik-Interpretation

- **5 echte Caves** (Indizes 1..5) plus 6 weitere Slots (Indizes 0, 6..10) für die zweite Achse
- **Diff 0:** Trivialer Test — alle 5 Caves wollen einen Wert
- **Diff 1:** Nur 2 von 5 Caves sind „selektiv" (zufällig gewählt)
- **Diff 2:** 2D-Sortierung: 2× 2 Caves = 4 aktive Filter über zwei verschiedene Achsen
- **Diff 3:** Komplexer (Code in einer anderen Funktion)

**Keine TARGET-Optimierung wie bei Cliffs** — die zufällige Cave-Auswahl ist nicht
balanciert. Daher kann es vorkommen dass auf Diff 2 manche Konfigurationen schwerer sind als andere.

## Was noch zu tun

1. Diff 3 Pfad analysieren (Code möglicherweise in einer der Helper-Funktionen)
2. Helper-Funktionen 0x1AC1A, 0x19F0F, 0x19E45, 0x1B235 analysieren
3. `StoneColdCavesSolver.cs` Adressen verifizieren:
   - 0x7A80 (difficulty) ✅
   - 0x7A82, 0x7A84 (attribute types) ✅
   - 0x7A48 (cave_filter_type[11]) — NICHT 0x7A86 wie Solver behauptet!
   - 0x7A5E (cave_filter_value[11]) — NICHT 0x7A90 wie Solver behauptet!

## Querverweise

- `STONE_COLD_CAVES.md` (verifizierte Übersicht)
- `STONE_COLD_CAVES_DEEP.md` (alte Detail-Analyse, die meisten Aspekte korrekt)
- `Solvers/StoneColdCavesSolver.cs` (⚠️ Adressen müssen korrigiert werden)


---

# v2 (PE32) — Caves-Code-Karte

> Statisch ermittelt. Vollständige Tabelle: siehe `V2_VARIABLE_MAP.md` § 2.

## Code-Lokalisation

| | v1 | v2 |
|---|----|----|
| Code | Seg 24 | `.text` 0x00407B30..0x0040A2F0 |
| MHK-Loader | — | `0x00407B30` (2928 B, 1 Caller) |
| Wrapper-Caller | — | `0x00406210` |
| Difficulty body | — | `0x0040A090` (Init mit BSS-Reset) |

## Verifizierte / Probable Mappings (v1→v2)

| v1 DS | v2 VA | Bedeutung | Konfidenz |
|-------|-------|-----------|-----------|
| `0x7A80` (1..4) | **`0x00494658`** | Difficulty (globale Quelle) | **Verified** (Iter. 2): Caller pusht direkt aus `.data`: `0x0040848F: mov cx, word [0x494658]; push ecx; call 0x40A090`; bestätigt durch zweiten Caller `0x0040A7CE` (gleiches Pattern) |
| `0x7A48..0x7A5C` | `0x004946CC..` (Byte-Array, indizierte Reads) | cave_filter_type[11] | Probable |
| `0x7A5E..0x7A72` | `0x004946D6..` | cave_filter_value[11] | Probable |
| init-zeroed targets | `0x0049465C/60/64/68/6C/70`, `0x004947F4/F8/FC/800/804` | BSS-Reset (Init-Anfang bei 0x40A0AA-0x40A0E8) | Verified als Init-Reset-Targets |

Spezifischer Marker: String `'Hieroglyphs'` @ `0x00409979`.
