# Mirror Machine — Tiefenanalyse Init (verifiziert 2026-04-25)

## Hypothese (vom Master-Pass)

Mirror Machine hat 52 random_range und 4190 B Init — höchste Werte unter den Puzzles.
Erwartet: **Pool/Selection-Pattern wie Cliffs** (TARGET-basierte Optimierung).

## Verifiziertes Ergebnis

**Hypothese WIDERLEGT** — anders gelagert als bei Cliffs.

Mirror Machine nutzt **weighted-random Generierung** mit verschachtelten Loops und
expliziten Wahrscheinlichkeits-Schwellen. Kein TARGET-basierter Match-Optimierungs-Algorithmus.

## Init-Struktur (Func 0x61FCB, 4190 B)

24 random_range-Aufrufe in der Init, davon 13 in **drei eng beieinander liegenden Clustern**
in einem großen verschachtelten Outer-Loop (1431 Bytes Loop-Body).

### Outer Loop bei 0x623CB → 0x62962 (1431 B Body)

```c
[bp-0x10] = 3   // Schwierigkeits-Counter, count down (3 → 2 → 1 → 0)

OUTER LOOP {
    // Initialisiere 8-Element Array auf Stack
    for (i = 0; i < 8; i++) [bp-0x26+i*2] = i

    // Setup
    [bp-0x12] = random_range(0, 4)    // Random index 0..4
    di = 0

    INNER LOOP {  // bei 0x62410 → 0x62957 (1351 B Body)
        if ([bp-0xE] >= max) break

        [bp-4] = random_range(1, [bp-6])    // Random value 1..N

        // *** WAHRSCHEINLICHKEITS-WURF ***
        random_value = random_range(0, 100)
        if (random_value <= 70) {
            // 70% Pfad: bestimmtes Muster
            // bedingung: di == 3 || [bp-0xE] != 0
            ...
        } else {
            // 30% Pfad: alternatives Muster
            ...
        }

        // Lookup in globalen Tabellen
        // DS:[di*2 - 0x6162]   ← Tabelle 1 (Smoke-Reference?)
        // DS:[di*2 - 0x615A]   ← Tabelle 2
        // DS:[di - 0x63D0]     ← Tabelle 3 (Byte-Tabelle)
        // DS:[di - 0x63CC]     ← Tabelle 4

        di++
    }

    [bp-0x10]--
} while ([bp-0x10] >= 0)
```

### Schlüssel-Operation: weighted random

```asm
0x062427: 6a 00          push    0
0x062429: 6a 64          push    0x64        ; max = 100
0x06242b: 9a ...          lcall   random_range
0x062430: 3d 46 00        cmp     ax, 0x46     ; 70 dezimal
0x062433: 7f 0f           jg      ...          ; if > 70, alternate path
```

**70/30-Weighted Random Decision** — anders als Cliffs (welches gezielt 50/50 sucht).

## Mechanik-Interpretation

Mirror Machine generiert **Smoke-Pattern-Schwierigkeits-spezifisch**:
- Outer-Loop läuft 4× (eine Iteration pro Difficulty-Level innerhalb der Setup-Phase?)
- Inner-Loop generiert mehrere Smoke-Muster pro Iteration
- 70/30 weighted random: meistens "informative" Muster, selten "irreführende"
- Globale Lookup-Tabellen liefern die Reference-Werte

**Kein Pool-Generation/Selection wie bei Cliffs.** Auch kein simpler deterministischer
Generator wie bei Bubblewonder. Es ist eine **probabilistische Generation** mit
fest verkabelten Wahrscheinlichkeiten.

## Was das für den Spieler bedeutet

- 70% der Zeit: "klare" Smoke-Hinweise → Spieler kann deduzieren
- 30% der Zeit: "Verwirrer" → erschwert die Deduktion
- Pro Difficulty-Level mehrere Iterationen → komplexere Muster auf höheren Levels

## DS-State-Variablen

```
DS:0x9E96..0x9E9C  ← initial state (kopiert nach 0x9EA6..0x9EAC am Anfang der Init)
DS:0x9EA6..0x9EAC  ← active state copy
DS:0x9C30..0x9C33  ← reference_attrs (4 bytes, vom Solver schon korrekt erfasst)
```

## Was noch zu tun

1. Globale Lookup-Tabellen bei DS:0x9E9E, DS:0x9EA6 lesen — sie definieren die Smoke-Muster
2. Die anderen 8 Funktionen mit random_range analysieren (insbesondere 0x614C4 und 0x61704
   mit je 7 random_range)
3. Eval-Funktion identifizieren (vermutlich 0x5DEBE oder 0x5E629)
4. Solver mit weighted-random-Verständnis aktualisieren

## Kritische Beobachtung: NICHT zufällig vergleichbar zu Cliffs

Cliffs: 7 random_range + Pool von 500 + Selection mit TARGET=N/2 → **gezielt 50/50**
Mirror: 24 random_range in Init + weighted 70/30 + Lookup → **probabilistisch geleitet**

Beides sind "smarte" Algorithmen, aber mit verschiedenen Zielen:
- Cliffs: Match-Verteilung optimieren
- Mirror: Schwierigkeit über Hinweis-Klarheit kontrollieren

## Querverweise

- `analysis/MIRROR_MACHINE.md`, `analysis/MIRROR_MACHINE_VERIFIED.md`
- `Solvers/MirrorMachineSolver.cs`


---

# v2 (PE32) — Mirror Machine / Smoke-Code-Karte

> Vollständige Tabelle: `V2_VARIABLE_MAP.md` § 10.

## Code-Lokalisation

| | v1 | v2 |
|---|----|----|
| Code | Seg 42 | `.text` 0x0043FB70..0x004406E0 (+ Helper bis 0x444070) |
| MHK-Loader | — | `0x0043FB70` (2928 B, 7 Caller — NEEDS-VERIFY) |

## Verifizierte / Probable Mappings

| v1 DS | v2 VA | Bedeutung | Konfidenz |
|-------|-------|-----------|-----------|
| `0x9C30..0x9C33` (Reference Z1) | **`0x0049CD60..0x0049CD63`** | Reference-Bitfield Z1 (4 Bytes) | **Verified** — Code @ 0x004403D2..0x00440403 kopiert 4 Bytes von Zoombini-Struktur (`[eax+0xC0..0xC3]`) nach `[edi*4 + 0x49CD60..63]` (1:1 v1-Pattern) |
| `0x9C34..0x9C37` (Reference Z2) | `0x0049CD64..0x0049CD67` | Reference-Bitfield Z2 | Probable (benachbart) |
| `0x9D42` 📊 | `0x0049C874` (8 acc) | primary state (indirekte scale=2 reads) | Probable |
| `0x9CA0/0x9CA2` | `0x0049CB0C/0x0049CB0E` | state cluster | Probable (benachbart, je 3 acc) |
| `0x9C26` ⚠️ | **`0x0049CD30`** | DIFFICULTY (globale Quelle) | **Verified** (Iter. 2): Caller pusht direkt aus `.data`: `0x00440418: mov cx, word [0x49CD30]; push ecx; call 0x443B20` (Smoke-Eval-Dispatcher); plus 3× `cmp [VA], 3` im Init |

## Difficulty-Dispatcher
```
0x00440418  mov cx, [0x49CD30]    ; aus globaler .data
0x0044041F  push ecx
0x00440420  call 0x443B20          ; Eval-Funktion erhält difficulty als Argument
   ↓
0x00443B25  movsx eax, word [esp+4]
0x00443B2A  dec eax                 ; 1-basiert → 0-basiert
0x00443B2B  cmp eax, 3
0x00443B30  jmp [eax*4 + 0x443BA4]
```
Globale Quelle = `0x0049CD30`, gespeichert 1..4 (decrement vor Vergleich).

## Solver-Stand
**Solver-tauglich auf v2.** Verified-Mappings:
- Difficulty (globale Quelle) bei `0x0049CD30`
- Reference-Bitfield Z1 bei `0x0049CD60..0x0049CD63`
- Reference-Bitfield Z2 wahrscheinlich `0x0049CD64..0x0049CD67`
