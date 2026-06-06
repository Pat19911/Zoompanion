# Zoombinis — Mechanik-Patterns über alle 12 Puzzles (verifiziert 2026-04-25)

Aus systematischer Disassembly-Analyse + Byte-Pattern-Verifikation aller 12 Puzzles.

## Verifizierte Pattern-Verteilung

| Pattern | Puzzles | Pattern-A-Signatur (`and eax, 0xf`) |
|---------|---------|--------------------------------------|
| **A: Pool + TARGET-Selection** | Allergic Cliffs, Lion's Lair | 4× / 7× |
| **B: Probabilistic Weighted** | Mirror Machine | 0 (anderes Pattern) |
| **C: Modular Sequential** | Stone Cold Caves, Bubblewonder Abyss, Pizza Pass, Captain Cajun, Stone Rise, Tattooed Toads, Mudball Wall, Fleens | 0 |
| **D: Retry-Validation (Rejection Sampling)** | Hotel Dimensia | 0 |

## Pattern A: Pool + TARGET-Selection (Cliffs, Lion's Lair)

**Verifiziert** durch direkte Disassembly UND durch Byte-Signatur (`66 83 e0 0f` = `and eax, 0xf`).

```
1. Generiere ALLE möglichen Konfigurationen als 32-bit Bitmasks
   - Cliffs: 500 (4 Triple-Type-Sets × 125 Permutationen)
   - Lion's Lair: 40 Slots
2. Klassifiziere: für jede Konfiguration, zähle wie viele Zoombinis matchen
   (Nibble-Vergleich: jedes der 4 Bytes = ein Attribut-Typ)
3. Setze TARGET = num_zoombinis / 2
4. Suche Konfigurationen mit match_count == TARGET
5. Bei keiner: oszilliere TARGET um ±1, ±2, ...
6. Wähle zufällig aus den passenden
```

→ **Gezielt 50/50-Match-Verteilung.** Ist DIE Cliff-Mechanik die zur „erstaunlich
gleichmäßigen" Verteilung führt.

## Pattern B: Probabilistic Weighted Generation (Mirror Machine)

```
for each (slot) {
    random_value = random_range(0, 100)
    if (random_value <= 70%) {
        generate "informative" pattern from lookup table
    } else {
        generate "confusing" pattern from alternative lookup
    }
}
```

→ **70/30-Wahrscheinlichkeit.** Schwierigkeit über Hinweis-Klarheit, nicht über
Match-Optimierung.

## Pattern C: Modular Sequential Generation (Mehrheit der Puzzles)

```
for each (difficulty) {
    call generator_for_this_difficulty()  // separate Funktion pro Diff
    // Slots werden sequenziell befüllt mit random_range an festen Positionen
    // Lookup-Tabellen (CS-Konstanten) bestimmen Layout
    // Fisher-Yates oder ähnliche shuffle-Algorithmen
}
```

→ **Echt zufällig pro Spielstart**, kein Optimierung. Manche Spiele „glücklich",
manche „unglücklich".

## Pattern D: Retry-Validation / Rejection Sampling (Hotel)

```
RETRY:
    attr_type1 = rand(3)
    attr_type2 = rand(3)
    attr_type3 = rand(3)
    if (!validate(types, difficulty)) goto RETRY
    // ... weitere Verarbeitung
```

→ **Rejection Sampling.** Würfel bis die Validierung passt. Anders als Pool/Selection
weil keine Optimierung — nur „darf gespielt werden". Besonders bei Hotel: wird geprüft
ob die gewählten Attribute genug Varianz unter den 16 Zoombinis haben.

## Pro-Puzzle Bilanz

| # | Puzzle | Pattern | Beweis | Doku |
|---|--------|---------|--------|------|
| 1 | **Allergic Cliffs** | A | 4× `and eax, 0xf` + verifiziert via Init-Disasm | `ALLERGIC_CLIFFS.md` |
| 2 | Stone Cold Caves | C | 0 Pattern-A-Sig + Fisher-Yates verifiziert | `STONE_COLD_CAVES_DEEP_VERIFIED.md` |
| 3 | Captain Cajun Ferry | C | 0 Pattern-A-Sig + nur 9 random_range | `CAPTAIN_CAJUN_FERRY.md` |
| 4 | Fleens | C | 0 Pattern-A-Sig + permutationsbasiert | `FLEENS_NEW.md` |
| 5 | **Hotel Dimensia** | D | 0 Pattern-A-Sig + retry-Loop (0x25230 → 0x250B0 mit rand-calls) | `HOTEL_DIMENSIA_VERIFIED.md` |
| 6 | Tattooed Toads | C | 0 Pattern-A-Sig + Lookup-Tabellen | `TITANIC_TATTOOED_TOADS.md` |
| 7 | Bubblewonder Abyss | C | 0 Pattern-A-Sig + verifiziert via Diff-3-Generator | `BUBBLEWONDER_DEEP_VERIFIED.md` |
| 8 | Mudball Wall | C | 0 Pattern-A-Sig + 13 rand in einer Funktion | `MUDBALL_WALL_VERIFIED.md` |
| 9 | Pizza Pass | C | 0 Pattern-A-Sig + 17 dispatches (level-spezifisch) | `PIZZA_PASS.md` |
| 10 | **Mirror Machine** | B | 0 Pattern-A-Sig + `cmp ax, 0x46` (70%) verifiziert | `MIRROR_DEEP_VERIFIED.md` |
| 11 | Stone Rise | C | 0 Pattern-A-Sig + nur 1 random_range | `STONE_RISE_NEW.md` |
| 12 | **Lion's Lair** | A | 7× `and eax, 0xf` + 40-Slot-Pool verifiziert | `LIONS_LAIR_DEEP_VERIFIED.md` |

## Erwartete Spielbarkeit pro Pattern

### Pattern A — Cliffs, Lion's Lair (gezielt balanciert)
- Verteilung der Zoombinis ist **immer ungefähr 50/50** (auf hohen Schwierigkeitsgraden)
- Kein „Glück" oder „Pech" — der Schwierigkeitsgrad ist konstant
- Solver muss durch Beobachtung deduzieren

### Pattern B — Mirror Machine (probabilistisch)
- 70% klare, 30% verwirrende Hinweise
- Manche Spielstarts haben gute, manche schlechte Hinweise
- Solver muss Wahrscheinlichkeiten einschätzen

### Pattern C — Mehrheit (zufällig)
- Echt zufällig pro Spielstart
- Schwankende Schwierigkeit
- Solver kann auf Glück hoffen oder rechnen

### Pattern D — Hotel (retry-validated)
- Konfiguration ist „spielbar" garantiert (Validierung)
- Aber innerhalb der spielbaren Konfigurationen wieder zufällig
- Solver hat es konsistenter als bei C

## Konsequenz für Solver-Implementierung

| Pattern | Solver-Strategie |
|---------|------------------|
| A | Memory-Read der Pool-Daten + Match-Count → direkte Berechnung. Komplex aber präzise. |
| B | Memory-Read der Reference-Werte + Lookup-Tabellen. Wahrscheinlichkeits-aware. |
| C | Memory-Read der Generator-State (DS-Variablen) → mechanische Lösung. |
| D | Memory-Read der validierten Random-Werte. Wie C. |

## Wichtige Erkenntnis

**Drei tiefenanalysierte Puzzles → drei Patterns. Eine Pattern-Signatur-Suche → Pattern A
in zwei Puzzles bestätigt, alle anderen ausgeschlossen.**

Die Engine ist **modular-mechanisch verschieden** pro Puzzle. Es gibt kein „one size fits all"
Solver-Ansatz. Pro Puzzle muss das passende Pattern verstanden werden.

## Methodische Anmerkung

Pattern-Verifikation via **Byte-Signatur-Suche** ist effizient und reproduzierbar:
- Pattern-A-Signatur: `66 83 e0 0f` (`and eax, 0xf`)
- Pattern-B-Signatur: `random_range(0, 100)` gefolgt von `cmp ax, <imm>` mit 30-90 als imm
- Pattern-D-Signatur: rand-call gefolgt von langer backward jump (Validierungs-Retry)

Das ist viel schneller als komplette Disassembly aller Init-Funktionen — und es liefert
**ehrliche Negative** (z.B. „Stone Cold Caves hat 0 Pattern-A-Vorkommen, also definitiv
nicht Pattern A").

## Querverweise

Pro Puzzle eine eigene Doku-Datei (`analysis/<NAME>.md`).
Cliff-Mechanik komplett: `analysis/ALLERGIC_CLIFFS.md`.
Master-Übersicht: `analysis/_PUZZLE_OVERVIEW.md`.
