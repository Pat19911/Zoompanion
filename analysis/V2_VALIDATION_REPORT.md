# v2 Statische Validierung — Bilanz

> **Stand 2026-04-25.** Validierung der Verified-Mappings aus
> `V2_VARIABLE_MAP.md` rein aus dem Code, ohne laufendes Spiel.
> Methodik: alternative Hypothesen prüfen, Konsistenz zwischen Init und Eval
> nachweisen, v1↔v2 Cross-Validation über reproduzierbare Konstanten.

---

## Methodik

Vier Validierungs-Tests:

1. **V1 — Difficulty-Schreibwerte**: Eine Difficulty-Variable wird nur mit
   Werten 0..3 oder 1..4 geschrieben. Werte > 4 disqualifizieren die Hypothese.
2. **V2 — Cliffs-Array-Konsistenz**: Wenn `allergy_type[5]` und `allergy_value[5]`
   bei `0x004945B5/0x004945BA` liegen, müssen sie sowohl in der Eval-Funktion
   gelesen ALS AUCH im Init-Branch geschrieben werden.
3. **V3 — v1↔v2-Pattern-Cross-Validation**: Reproduzierbare v1-Pattern
   (z. B. „7× random_range in Cliff-Init") müssen in v2 wiederfindbar sein.
4. **V4 — Pizza-Array-Init-Setter**: Die Wunsch-Arrays müssen Init-Schreiber
   haben (sonst wären sie statische Daten, keine dynamischen Wünsche).

---

## V1 — Difficulty-Schreibwerte

Pro Difficulty-Kandidat zähle direkte (`mov [VA], imm`) und indirekte
(`mov reg, [VA]; cmp reg, imm`) Vergleiche.

| Puzzle | Kandidat | Direkte cmp-Werte | Indirekte cmp-Werte | Schreibwerte | Bewertung |
|--------|---------|-------------------|---------------------|--------------|-----------|
| **Cliffs** | `0x00494508` | nur 0 | **3** | 0xFFFF (3×) + 0 (1×) + reg (1×) | **Verified** durch indirect `dec; cmp 3` Pattern; 0xFFFF = "no level loaded" Sentinel |
| **Caves** | `0x00494658` | 2, 4 | — | (über Caller-reg) | **Verified** durch direkte cmp-Range 2/4 |
| **Hotel** | `0x004967D6` | 2, 3 (5 Sites) | — | (über reg) | **Verified** stark durch 5 cmp-Sites |
| **Pizza** | `0x0049BC36` | **0, 1, 2, 3** (alle 4!) | — | (über reg) | **⭐ Stärkste Verifikation** — alle Difficulty-Werte direkt vergleichen |
| **Tunnels** | `0x004A2B6A` | — | 3 | (über reg) | **Verified** durch indirect cmp 3 |
| **Smoke** | `0x0049CD30` | 3, 4 | — | (über Caller-reg) | **Verified** durch cmp-Range |
| **Bubblewonder** | ~~`0x0049A20C`~~ → `0x0049A82C` | **3, 4** an `0x49A82C` | — | — | **Korrektur** (siehe unten) |
| **Mudball** | ~~`0x00494946`~~ → `0x0049B242` | 3 an `0x49B242` | — | direkte writes 0,1,2,4,6,7 an `0x494946` | **Korrektur** (siehe unten) |

### Korrigierte Befunde

**Bubblewonder** — alte Annahme `0x0049A20C` als Difficulty falsifiziert:
- Dispatcher-Source mit 8 Cases (`cmp 7`) → kein Difficulty (max 4)
- Echte Difficulty: **`0x0049A82C`** — `cmp [VA], 3` UND `cmp [VA], 4` vorhanden
- Begründung: `0x0049A20C` ist ein State-/Action-Switch, der Wert von Strukturfeld kommt

**Mudball** — alte Annahme `0x00494946` als Difficulty falsifiziert:
- Direkte Schreibwerte umfassen **6 und 7** — unmöglich für Difficulty (max 4)
- 7 verschiedene `mov [VA], imm`-Sites mit Werten 0,1,2,4,6,7 → State-Machine
- Echte Difficulty: **`0x0049B242`** — `cmp [VA], 3` an mehreren Stellen
- Begründung: `0x00494946` zählt Animation-Phasen oder Action-IDs

---

## V2 — Cliffs-Allergy-Arrays (allergy_type/value)

**Hypothese**: `allergy_type[5] @ 0x004945B5..B9`, `allergy_value[5] @ 0x004945BA..BE`

### Reads
- **Eval/Cheat-Print** (fn `0x00406760`):
  - `0x00406B9C: movzx ax, byte [eax + 0x004945B5]` ← allergy_type[i]
  - `0x00406B94: movzx dx, byte [eax + 0x004945BA]` ← allergy_value[i]
  - mit `eax = sign_extend(si)`, si Loop-Index 0..num_allergies-1

### Writes
- **Init-Body** (fn `0x004074C9`, 1159 B, 1 Caller):
  - **16 Writes** auf `allergy_type[0..1]` (0x4945B5/B6) verteilt über
    Difficulty-Branches
  - **16 Writes** auf `allergy_value[0..1]` (0x4945BA/BB)
  - Plus 3 indizierte Writes (`mov [reg + 0x4945B5/BA]`) — Loop-Body

### Bewertung

**✅ Validiert.** Das Pattern ist konsistent:
- Init-Body schreibt Arrays
- Eval-Body liest sie mit gleichem Index-Pattern (`movzx … [eax + base]`)
- Index-Range 0..4 (5 Slots, byte-offset)
- Type/Value-Distanz exakt 5 Bytes (ein Byte pro Slot × 5 Slots)

Eine alternative Hypothese (z. B. „diese VAs sind ein anderes 5-Byte-Strukt")
wäre möglich, aber die Reads stehen direkt nach Cheat-Print-Branch
(`if(which_cliff) printf("Upper..."); else printf("Lower...")`) und werden in
einer Loop iteriert mit Bound `[0x004945B4]` (= num_allergies). Das ist die
exakte v1-Logik 1:1 reproduziert.

---

## V3 — v1↔v2-Pattern-Cross-Validation (Cliffs)

| v1-Beobachtung | v2-Beobachtung | Bewertung |
|----------------|----------------|-----------|
| 7× `random_range`-Calls in Cliff-Init | **7× direct calls to `0x00457940`** in fn `0x00405A60` (Cliff-Loader) | ⭐ **exakte Übereinstimmung** |
| LCG add-Konstante `2531011` (0x269EC3) | 1 Vorkommen in v2-PE | identisch |
| Pool-Konstante 125 (Diff-3 Subset) | 1 Site `cmp ?, 125` in Cliff-Region | konsistent |
| TARGET 50 (50/50-Optimierung) | 2 Sites `cmp ?, 50` | konsistent |
| Pool-Konstante 500 (4×125) | 0 explizite Sites | implizit (Compiler hat 4×125 nicht als Literal aufgelöst) |

**Bewertung**: Die statistische Wahrscheinlichkeit, dass v2 zufällig genau
**7** random_range-Calls hat (gleich wie v1 7), ist verschwindend gering.
Plus identische LCG-Konstante. Das ist eine **starke Identitäts-Bestätigung**:
v2-Cliff-Init ist eine Re-Compilation des v1-Cliff-Init-Codes.

---

## V4 — Pizza-Array-Init-Setter

| Array | Direct Writes | Indirect Writes (scale=1/2) | Bewertung |
|-------|---------------|------------------------------|-----------|
| `arno_wants[8]` (`0x49BA34..0x49BA42`) | 0 | 6 | Loop-basierte Init |
| `willa_wants[8]` (`0x49BB44..0x49BB52`) | 0 | 6 | Loop-basierte Init |
| `shyler_wants[8]` (`0x49BC38..0x49BC46`) | 0 | 4 | Loop-basierte Init |
| **`meal[8]`** (`0x49BC00..0x49BC0E`) | **8 sequenziell** | 0 | **Unrolled Init** |

### Bemerkenswertes für `meal[8]`

8 direkte writes mit exakt 2-Byte-Schritten:
```
0x00433BBF mov [0x0049BC00]
0x00433C55 mov [0x0049BC02]
0x00433CEB mov [0x0049BC04]
0x00433D81 mov [0x0049BC06]
0x00433E17 mov [0x0049BC08]
[+3 weitere bei +0x0E gesamt]
```

Ein Compiler erzeugt diese Reihenfolge nur, wenn der Source-Code 8 separate
Variablen wäre (z. B. `meal_0, meal_1, ..., meal_7`) ODER ein 8-Word-Array,
das per Schleife (entrollt) initialisiert wird. Beide Interpretationen führen
in der Praxis zum gleichen Memory-Layout.

**Bewertung**: ✅ Validiert als 8-Slot-Array. Die anderen 3 Arrays haben
indirekte indizierte Writes — direkter Beweis für Loop-Iteration über
Array-Index.

---

## Aktualisierte Zähler

| Kategorie | Stand vor Validierung | Stand nach Validierung |
|-----------|----------------------|------------------------|
| Verified Mappings | 27 | 26 (1 falsch identifiziert + 2 Korrekturen) |
| Korrigierte Mappings | 0 | 2 (Bubblewonder, Mudball) |
| Cross-validierte v1↔v2 | 0 | 4 (random_range count, LCG, Pool 125, TARGET 50) |
| Solver-tauglich auf v2 | 5 | **6** (Bubblewonder kommt dazu, weil echte Difficulty `0x49A82C` jetzt Verified) |

### Pro Puzzle Final-Status (rein statisch)

| Puzzle | Difficulty Verified | Match-Logik | Solver-fähig auf v2? |
|--------|---------------------|-------------|----------------------|
| **Cliffs** | `0x00494508` | 8 Variablen + Match-Loop nachgewiesen | **JA, vollständig** |
| **Pizza** | `0x0049BC36` | 4 Arrays + 3 Aktiv-Flags Verified | **JA, vollständig** |
| **Hotel** | `0x004967D6` | constraint_X/Y direkt in `.data` | **JA** (statisch reicht) |
| **Caves** | `0x00494658` | Filter-Arrays Probable | JA (Difficulty + Filter-Probable) |
| **Mirror Machine** | `0x0049CD30` | Reference-Bitfield Verified | JA |
| **Bubblewonder** | `0x0049A82C` (Korrektur) | Rule-Slot-Counter | JA (Difficulty + Slot-Counter) |
| **Tunnels** | `0x004A2B6A` | Rule-Strukturen Probable | JA (eingeschränkt) |
| **Mudball** | `0x0049B242` (Korrektur) | Wall-State Probable | JA (eingeschränkt) |
| **Fleens** | Argument-basiert, globale Quelle nicht isoliert | — | NEIN (statisch) |
| **Lilly (Toads)** | Loader generic | — | NEIN |
| **Stone Rise** | Init nicht lokalisiert | — | NEIN |
| **Captain Cajun** | Init nicht lokalisiert | — | NEIN |

---

## Was die Validierung NICHT geleistet hat

1. **Engine-Globals**: Die Engine-Variable-Hypothesen für `0x004950C8`, `0x004A2818`,
   `0x004A4AAA` bleiben Speculative.
2. **Hotel-Match-Sub-Funktionen**: 60 reachable functions; nur Init-Pattern
   bestätigt, einzelne Match-Sub-Routinen nicht durchanalysiert.
3. **Lilly/Stone Rise/Ferry**: Alle drei bleiben ohne Init-Anchor in v2.
   **Statische Grenze erreicht**.
4. **Live-Validierung gegen das laufende Spiel** ist weiterhin der Schritt,
   der Speculative → Verified hebt für die letzten 4 Puzzle.

---

## Pragmatische Implikation

Die statische Code-Analyse hat gezeigt:

- **6 Puzzle solver-tauglich auf v2** mit Verified-Mappings (Cliffs, Pizza, Hotel,
  Caves, Mirror Machine, Bubblewonder)
- **2 Puzzle eingeschränkt** (Tunnels, Mudball — Difficulty Verified, Match-Logik Probable)
- **4 Puzzle bleiben Lücke** (Fleens, Lilly, Stone Rise, Ferry)

Für 6 Puzzle reicht der statische Stand für eine Solver-Portierung von v1 auf
v2 — die anderen 6 sind in v1 ebenfalls nur Probable/Speculative gewesen.
Die v2-Portierung ist also **nicht schlechter** als der v1-Stand.
