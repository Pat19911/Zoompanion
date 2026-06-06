# v2 Variable Map — alle 12 Puzzles

> **Stand 2026-04-25.** Statische Analyse von `v2_bin/ZoombinisLJ.exe` (PE32, VC++6.0).
> **Konfidenz-Markierungen pro CLAUDE.md**: `Verified` (direkter Code-Pattern-Match
> mit v1), `Probable` (Heuristisches Match, alternative Erklärung möglich),
> `Speculative` (nur Pattern-Indikation).
>
> Erzeugt durch `pe_loader.py` + `scratch/v2_puzzle_deepdive.py`.
> Pro Puzzle wurde der MHK-Loader als Anker verwendet, transitive Direct-Calls
> bis Tiefe 60 Funktionen verfolgt, alle `.data`-Zugriffe geharvested,
> Difficulty-Dispatcher-Pattern (`dec; cmp; ja; jmp [reg*4+tbl]`) gesucht,
> Cmp-Konstanten und indirekte Array-Zugriffe ausgewertet.

---

## Querverweise zu v1

- `_DS_VARIABLES.md` — v1 DS-Karte (Quelle der Mappings)
- `V1_VS_V2_COMPARISON.md` — Resource/Build-Vergleich
- `V2_BINARY_MAP.md` — v2 Funktions-Karte
- `ALLERGIC_CLIFFS.md` — vollständige v2-Sektion (Beispiel-Tiefe)

---

## 1. Allergic Cliffs — siehe `ALLERGIC_CLIFFS.md` § v2-Sektion

Vollständig durchanalysiert mit **8 Verified Mappings** (difficulty, attempt_counter,
engagement_flag, lock, which_cliff, num_allergies, allergy_type[5], allergy_value[5]).
Solver-tauglich.

---

## 2. Stone Cold Caves (v1 Seg 24)

**Loader:** `0x00407B30` (2928 B, 1 Caller `0x00406210` Wrapper).
**Reachable functions:** 16. **Code-Region:** ~`0x00407B30..0x0040A2F0`.
**Eval body:** `0x0040A090` (Difficulty als Argument von Caller).

### Difficulty-Dispatcher (zwei separate)

```
@0x0040A222  jmp [eax*4 + 0x0040A2D0]  (4 cases)  src=[eax + 0x004947F6]  ; Strukturfeld
@0x0040A284  jmp [eax*4 + 0x0040A2E0]  (4 cases)  src=[esp+0x1c]          ; Argument
```

### Mappings (Iteration 2 — Caller-Trace)

| Bedeutung (v1) | v1 DS | v2 .data VA | Konfidenz | Beleg |
|----------------|-------|-------------|-----------|-------|
| **DIFFICULTY** (globale Quelle) | `0x7A80` (1..4) | **`0x00494658`** | **Verified** | Caller pusht aus globaler `.data`: `0x0040848F: mov cx, word [0x494658]; push ecx; call 0x40A090` und identisch bei `0x0040A7CE` (zweiter Caller) |
| Primary attribute type | `0x7A82` | `0x004945EA` (?) | Probable | zweiter Dispatcher mit cmp 4 cases — analog Diff |
| Top write-target | — | `0x004A4876` (15 acc, 13W) | — | gleicher BSS-reset wie bei Cliffs (engine-shared?) |
| Cave-State | — | `0x004A5400` (11 acc, 9W) | Probable: primary state | engine-region |
| **cave_filter_type[11]** | `0x7A48..0x7A5C` | **`0x004946CC..`** (8 reads scale=1) | Probable | indizierte Reads `byte [eax + 0x4946CC]`, 4 Lese-Sites |
| **cave_filter_value[11]** | `0x7A5E..0x7A72` | **`0x004946D6..`** (4 reads scale=1) | Probable | 0x4946D6 = 0x4946CC + 10 (Byte-Array, Distanz passt zu 11 Slots) |
| Counter | `0x7A7C` | `0x00494658` (cmp 2, cmp 4) | Speculative | `cmp [VA], 2` und `cmp [VA], 4` |
| Pool-array | — | `0x004947F4` (writes scale=2) | Speculative | indizierter Schreibzugriff |

### Spezifischer Marker
String `'Hieroglyphs'` @ `0x00409979` (Caves-spezifischer Debug-Push) ✓ verifiziert.

---

## 3. Captain Cajun's Ferry (v1 Seg 26) — **echte Init noch nicht verifiziert**

**Problem:** `ferry.mhk`-Push (`0x0040FF04`) liegt in der `WinMain`-CFG-Validierung
(`0x0040FE20`). Die echte Captain-Cajun-Init muss anders lokalisiert werden.

### Kandidaten (Code-Layout zwischen Caves und Fleens, `0x408000..0x40FE20`)

| Funktion | Größe | Caller | Cmp-Konstanten | Bewertung |
|----------|-------|--------|----------------|-----------|
| `0x004086A0` | 1296 B | 2 | 2,3,6,30 | wahrscheinlich Caves-Subroutine (data range schließt Caves ein) |
| `0x00408BB0` | 944 B | 3 | 20 | Plausibler Ferry-Kandidat (data range 0x4946xx..0x494874 = puzzle-eigen) |
| `0x00408F60` | 1472 B | 2 | 3,12 | Kandidat |
| `0x00410520` | 3408 B | 2 | 2,3,4,5,6,8,9,11,65,70 — `Play FrogMan SCRB id:` | wahrscheinlich Frogman-Helper (puzzle-übergreifend) |
| `0x004118F0` | 688 B | 2 | 3,8 | klein, vermutlich Helper |

**Beste Kandidaten für Ferry-Init:** `0x00408BB0` oder `0x00408F60`.
**Konfidenz-Niveau:** Speculative — kein harter Marker.

### Mapping-Status

Keine v2-VAs verifiziert. v1 hat für Ferry nur Master-Top-Writes-Adressen (📊),
keine Init-Disasm-Verified-Adressen. Solver-Übertragung auf v2 derzeit unmöglich
ohne weitere Analyse.

### Lücke (offen)
1. Ferry-spezifischer harter Marker (Debug-String / SCRB-ID-Konstante) suchen.
2. Wenn keine vorhanden: Live-Run-Validierung erforderlich, um Init-Funktion
   per Memory-Diff zu identifizieren.

---

## 4. Fleens (v1 Seg 27)

**Loader:** `0x00411E80` (1600 B, 1 Caller `0x00410520`).
**Reachable functions:** 8. **Code-Region:** ~`0x00411E80..0x004124C0`.

### Difficulty-Dispatcher

```
@0x00413407  jmp [reg*4 + 0x00413784]  (4 cases)  src=?
```

### Mappings

| Bedeutung (v1) | v1 DS | v2 .data VA | Konfidenz | Beleg |
|----------------|-------|-------------|-----------|-------|
| difficulty/play_count | `0x7CEA` ⚠️ | **`0x00495D30`** | **Probable** | `cmp [VA], 3`; primary state hottest VA (8 acc, 7R) |
| Primary state | `0x7CB2` 📊 | `0x00495C1A` (5 acc) | Speculative | mid-frequency mixed R/W |
| State cluster | `0x7C9E/CC0/CAE` | `0x00495B2C` (5 acc) | Speculative | mit indizierten Schreibungen |
| State cluster | — | `0x00495B08`, `0x00495B0C` | Speculative | benachbarte VAs |
| Static-data lookup | — | `0x0048BEBC..0x0048BEE0` | Probable | mehrfach indizierte Reads scale=2 (4 reads je) — Lookup-Tabellen |

---

## 5. Hotel Dimensia (v1 Seg 28)

**Loader:** `0x004154D0` (2352 B, 1 Caller `0x00412760`).
**Eval body:** `0x00417230` (3216 B) — Difficulty-Init + Match-Logik integriert.
**Reachable functions:** 60 (sehr groß, viele Sub-Funktionen).
**Code-Region:** ~`0x004154D0..0x004191D0` plus `0x0044A990` (Helper).

### Difficulty-Dispatcher

```
@0x00417679  jmp [eax*4 + 0x00417C2C]  (4 cases)  src=[esp+0x14]      ; Action-ID, NICHT difficulty
@0x00417940  jmp [reg*4 + 0x00417C3C]  (4 cases)
@0x00415C07  jmp [tbl 0x00415DE4]        (?cases)  src=[0x004967D6]   ; Difficulty-Switch
```

In Init-Path of fn 0x00417230: `mov bx, word [0x4967D6]; cmp bx, 2; ...; cmp bx, 3`
mehrfach (5+ Vergleichsstellen) → **Difficulty `0x004967D6` Verified**.

### Mappings (Iteration 2 — Init durchanalysiert)

| Bedeutung (v1) | v1 DS | v2 .data VA | Konfidenz | Beleg |
|----------------|-------|-------------|-----------|-------|
| **DIFFICULTY** | `0x7E42` (0..3) | **`0x004967D6`** | **Verified** | `mov bx, [0x4967D6]; cmp bx, 2/3` mehrfach in Init-Pfad (0x00417268, 0x00417275, 0x004173AB, 0x00417563) |
| **constraint_X[25]** (5×5-Tabelle) | `FFFF:0x0316` (shared mem) | **`0x00496690..`** (Word-Array, scale=2 reads) | **Verified** | Cell-Compare-Pattern in Init: `cmp word [eax*2 + 0x496690], bp` und `cmp word [edx*2 + 0x496690], 4` mit eax/edx als 0..24-Index |
| **constraint_Y[25]** | `FFFF:0x0348` | **`0x00496698..`** (Word-Array, scale=2 reads) | **Verified** | Analog 0x004174B2: `cmp word [eax + 0x496698], si` |
| Match-Cell-Array | — | **`0x00495E8C..`** (Word-Array, scale=2/1) | Probable | `cmp [eax + 0x495E8C], dx` in Match-Loop (verifiziert als Constraint-Vergleich) |
| Box-State-Array | `FFFF:0x?` | **`0x00496014..`** (Word-Array, scale=2) | Probable | indirekt mit indizierten reads/writes |
| **first_time_flag** | `0x7E44` | `0x004967D4` (cmp 0) | Probable | benachbart zu Difficulty, binary check |
| Primary state | `0x7E5E` 📊 | **`0x00496588`** (33 acc, 24R/9W) | Probable | hottest puzzle-own VA mit mixed access |
| Top write target | — | `0x004967E0` (14 acc, 13W) | Speculative | one-shot init-write |
| Top read | — | `0x004967D6` (48 acc, 47R) | (= Difficulty) | massiv read in Eval |
| State count? | — | `0x00496154` (cmp 9, 11, 12) | Speculative | counter-pattern |
| Slot/box state | `0x0316`/`0x0348` (table) | indirekt @ scale=2 base `0x00496014`, `0x00495E8C`, `0x00496698` | Probable | indizierte Word-Arrays (`mov [eax*2 + base]`) |
| attr-types | `0x7E48..0x7E4C` | nicht eindeutig | Speculative | mehrere Word-VAs in `0x004966XX`-Range |

### Static-data lookups
- `0x0048BFC0` (scale=4, 5 reads) — passt zu 5×N-Tabelle (Hotel hat 25 Räume)
- `0x0048C028` (scale=4, 3 reads)

### Beobachtung
Hotel ist das größte Puzzle (60 Sub-Funktionen, 243 unique VAs, 959 Accesses).
Die Top-VAs `0x004967D4..0x004967E8` und `0x00496580..0x004966XX` sind sehr aktiv —
typisch für ein State-Tracking-Puzzle.

---

## 6. Titanic Tattooed Toads / Lilly (v1 Seg 30)

**Loader:** `0x00419770` (2416 B, **10 Caller** — NEEDS-VERIFY!).
**Reachable functions:** 16.

### Difficulty-Dispatcher

```
@0x0041F7B8  jmp [reg*4 + 0x0041F8DC]  (4 cases)  src=?
@0x0041CB24  jmp [reg*4 + 0x0041CB74]  (4 cases)  src=?
@0x0041FCFA  jmp [reg*4 + 0x0041FDF8]  (4 cases)  src=?
@0x0041F9E2  jmp [reg*4 + 0x0041FBF8]  (4 cases)  src=?
```

### Mappings

| Bedeutung (v1) | v1 DS | v2 .data VA | Konfidenz | Beleg |
|----------------|-------|-------------|-----------|-------|
| Primary state | `0x8C6C` 📊 | **`0x004994EC`** (13 acc) | Probable | hottest writes; `cmp [VA], 5`, `cmp [VA], 3` |
| difficulty | `0x11A4` ⚠️ | unklar | Speculative | mehrere 4-case-Dispatcher ohne klar erkannte src |
| Lilypad-pattern | `0x85E6..0x85EC` | indirekt @ `0x00498B58..0x00498B5B` (byte arrays) | Probable | scale=1 mit byte writes (4 benachbarte VAs) |
| Toad-state | `0x8C38/0x8C40` | `0x00497310/0x00497314` | Speculative | jeweils 11 reads (read-only) |
| State cluster | — | `0x00496E7A/0x00496E7C` | Speculative | beide 7 acc 5R/2W |

### Lücke
Lilly hat **10 Caller** auf den Loader — ungewöhnlich viele. Das deutet darauf
hin, dass `0x00419770` ein wiederverwendbarer Loader-Helper ist statt einer
exklusiven Lilly-Init. Die "echte" Lilly-Init könnte eine darübergelegene
Funktion sein. **NEEDS-VERIFY.**

---

## 7. Bubblewonder Abyss / Maze2 (v1 Seg 34)

**Loader:** `0x004242F0` (4560 B, 1 Caller `0x004209E0`).
**Reachable functions:** 35.

### Difficulty-Dispatcher

```
@0x00427813  jmp [reg*4 + 0x004279D8]  (8 cases)  src=[0x0049A20C]
@0x00427560  jmp [reg*4 + 0x00427634]  (5 cases)
@0x0042B312  jmp [reg*4 + 0x0042B640]  (7 cases)
@0x004273FC  jmp [reg*4 + 0x00427488]  (5 cases)
@0x00424FC0  jmp [reg*4 + 0x00425498]  (11 cases)
```

### Mappings

| Bedeutung (v1) | v1 DS | v2 .data VA | Konfidenz | Beleg |
|----------------|-------|-------------|-----------|-------|
| **Rule-Slot-Counter** | `0x92A2` ✅ | **`0x0049A2CE`** | **Probable** | hottest VA überhaupt (144 acc, 96R/48W), passt zu Slot-Counter |
| **Auxiliary state** | `0x92A4/0x92A6` | `0x0049A2CC` (36 acc) | Probable | benachbart zu 0x49A2CE, mixed access |
| Primary buffer | — | `0x0049A274` (127 acc, 44R/83W) | Probable: dynamisches Schreibziel | 2nd hottest, indirekte Writes scale=1 und scale=2 |
| **8-Case-State-Switch** (NICHT Difficulty!) | — | `0x0049A20C` | **Verified als Dispatcher-Source** — aber 8 cases (`cmp 7`) → das ist State/Action, nicht Difficulty (max 4) |
| **DIFFICULTY** (Korrektur nach Validierung) | `0x925A` ⚠️ | **`0x0049A82C`** | **Verified** | `cmp [0x49A82C], 3` UND `cmp [0x49A82C], 4` (beide cmp-Werte 3 und 4 vorhanden — **stärkste Difficulty-Signatur**) |
| Rule-Slot-Array | `0xFFFF:0x7A8` (shared) | indirekt @ scale=2 mit `0x0049A4A8..0x0049A4AE` | Probable | 5+ indizierte Reads (passt zu 5-Slot-Tabelle) |
| Lookup-Tabelle | `0xFFFF:0x72A` | indirekt @ `0x0048D690..0x0048D998` (scale=2/4) | Probable | mehrfache statische Array-Reads in `.data`-Const-Region |

### Static-data
String `'Map/Go Buttons'` @ `0x004252EE` (UI-Marker).

---

## 8. Mudball Wall / Net (v1 Seg 35)

**Loader:** `0x0042BC20` (2160 B, **25 Caller** — NEEDS-VERIFY).
**Reachable functions:** 60. **Code-Region weit gestreut** (durch viele Caller).

### Difficulty-Dispatcher

```
@0x0040DE2B  jmp [eax*4 + 0x0040DF00]  (8 cases)  src=[0x00494946]
@0x0042BCA7  jmp [eax*4 + 0x0042BE7C]  (15 cases) src=[0x0049B0E8]
@0x00431200  jmp [eax*4 + 0x00431254]  (4 cases)  src=[0x004A2818]   ← engine
```

### Mappings

| Bedeutung (v1) | v1 DS | v2 .data VA | Konfidenz | Beleg |
|----------------|-------|-------------|-----------|-------|
| **8-Case-State-Switch** (NICHT Difficulty!) | — | **`0x00494946`** | **Verified** | `0x0040DE15: movsx eax, word [0x494946]; cmp eax, 7; ja default; jmp [eax*4+0x40DF00]` — 8 cases mit Schreibwerten 0..7 (siehe direct-writes Validierung). Das ist eine **State-Machine-Variable**, nicht Difficulty. |
| **DIFFICULTY** (Mudball-Korrektur nach Validierung) | — | **`0x0049B242`** | **Probable** | `cmp [0x49B242], 3` (1 site) plus `cmp 1/2/3` Pattern an mehreren Stellen — passt zu Difficulty 0..3 |
| Wall-State | `0x9506` 📊 | **`0x0049492C`** (16 acc) | Probable | hottest |
| Wall-State | `0x950A` 📊 | `0x00494930` (12 acc) | Probable | benachbart |
| Selected axis | `0x94FA` 📊 | `0x0049494C` (10 acc) | Speculative | mid-frequency 5R/5W |
| Wall-State | `0x9508` 📊 | `0x00494938` (11 acc) | Speculative | benachbart |
| Slot/Position | `0x9520` 📊 | `0x00494928` (9 acc) | Speculative | |
| State cluster | — | `0x0049B242` (cmp 1/2/3, 7 acc) | Probable: state machine variable |  passt zu 3-state machine |
| Static lookup | — | `0x0048DA84..0x0048DBE0` (scale=2, 13 distinct VAs) | Probable | umfangreiche Word-Lookup-Tabelle |

### Lücke
**25 Caller** auf `0x0042BC20` ist auffällig. Möglicherweise ist diese Funktion
ein **MHK-Resource-Loader-Helper** (lädt einen MHK-Asset auf Anfrage), nicht
die eigentliche Mudball-Init. Die echte Init könnte eine andere große Funktion
in der Caller-Hierarchie sein.

String `'picker.mhk'` wird in dieser Funktion gepusht — bestätigt Engine-Helper-Theorie.

---

## 9. Pizza Pass (v1 Seg 37)

**Loader:** `0x00431590` (3904 B, 1 Caller `0x0042FCB0`).
**Eval (Cheat-Print):** `0x00435020..0x00435340` (1280 B) — `Arno/Willa/Shyler/Meal`-Marker.
**Reachable functions:** 9.

### Difficulty-Dispatcher

```
@0x0043408E  jmp [eax*4 + 0x00434410]  (4 cases)  src=[0x0049BC36]
```

### Mappings (Iteration 2 — Cheat-Print disassembliert)

| Bedeutung (v1) | v1 DS | v2 .data VA | Konfidenz | Beleg |
|----------------|-------|-------------|-----------|-------|
| **DIFFICULTY** | `0x96D2` ⚠️ | **`0x0049BC36`** | **Verified** | `0x0043407A: movsx eax, word [0x49BC36]; cmp eax, 3; ja default; jmp [eax*4+0x434410]` — direkter Dispatcher |
| **arno_wants[8]** (Word-Array) | `0x973A..` ⚠️ | **`0x0049BA34..0x0049BA42`** (8 Worte) | **Verified** | Cheat-Print 0x004350CA-0x00435104: 8× `movsx, word [0x49BA34..0x49BA42]` direkt vor `push "Arno %d %d %d %d %d %d %d %d"` |
| **willa_wants[8]** (Word-Array) | `0x974A..` ⚠️ | **`0x0049BB44..0x0049BB52`** (8 Worte) | **Verified** | analog 0x00435120-0x00435157, push "Willa %d %d..." |
| **shyler_wants[8]** (Word-Array) | `0x975A..` ⚠️ | **`0x0049BC38..0x0049BC46`** (8 Worte) | **Verified** | analog 0x00435172-0x004351AC, push "Shyler %d %d..." |
| **meal[8]** — finales Pizza-Wunsch (NEU dokumentiert) | — | **`0x0049BC00..0x0049BC0E`** (8 Worte) | **Verified** | analog 0x004351C4-0x004351FB, push "Meal %d %d..." |
| **Trolle-aktiv-Flags** (3 Stk.) | — | **`0x0049BBA4`** (Arno?), **`0x0049BB30`** (Willa?), **`0x0049BB6A`** (Shyler?) | **Verified** | Cheat-Print 0x00435259/0x00435288/0x004352B7: jeweils `cmp [VA], si; jne; call ...` — 3 separate Anwesenheits-Checks |
| Active count | `0x979E` 📊 | `0x0049BC56` (20 acc) | Probable | hottest mixed |
| Active topping bitmask | `0x97D2` 📊 | `0x0049BA14` (12 acc, 4R/8W) | Probable | indirekte writes scale=1 |
| Troll counts | `0x96D6/0x96D8` | `0x0049BC38` (cmp, 4W) | Speculative | benachbart zu Difficulty |

### Solver-Stand für Pizza

Mit 7 Verified Mappings (Difficulty + 4 Arrays + 3 Trolle-Flags) ist Pizza in v2
**solver-tauglich** auf demselben Niveau wie Cliffs. Lesen aus den Word-Arrays
gibt direkten Zugriff auf:
- Welche 3 Trolle sind aktiv (Arno/Willa/Shyler-Flags)
- Was jeder Troll mag (8 Slots)
- Was die finale Pizza ist (8 Slots)
- Welche Difficulty (1..3 → cmp eax, 3)

### v2-Erweiterung (verifiziert)
Das **vierte „Meal"-Array** in v2 ist im v1-Doku nicht dokumentiert. Möglicherweise
existiert es auch in v1, wurde aber nie analysiert. Es speichert die finale
Pizza-Konfiguration (Spieler-Eingabe), während die anderen drei Arrays die
Wunsch-Targets sind.

### Beobachtung Pizza-Erweiterung
PIZZA hat in v2 56 zusätzliche SCRBs (siehe `V1_VS_V2_COMPARISON.md`). Im Code-Layout
keine offensichtliche Erweiterung — die Logik ist gleich, nur die Hotspot-Tabellen
in der MHK sind größer. Difficulty-Dispatcher hat weiterhin nur 4 Cases.

---

## 10. Mirror Machine / Smoke (v1 Seg 42)

**Loader:** `0x0043FB70` (2928 B, **7 Caller** — NEEDS-VERIFY).
**Reachable functions:** 12.
**Difficulty-Quelle:** Argument-basiert (`movsx eax, [esp+4]; dec eax; cmp eax, 3`),
nicht direkt aus globaler VA. Aufrufer pusht den Difficulty-Wert aus einer .data-VA
(Trace nicht trivial isolierbar).

### Difficulty-Dispatcher

```
@0x00443B30  jmp [reg*4 + 0x00443BA4]  (4 cases)  src=[esp+4]   ← Argument-basiert
@0x0043FB98  jmp [reg*4 + 0x0043FC38]  (6 cases)  src=?
```

### Mappings

| Bedeutung (v1) | v1 DS | v2 .data VA | Konfidenz | Beleg |
|----------------|-------|-------------|-----------|-------|
| **DIFFICULTY** (globale Quelle) | `0x9C26` ⚠️ | **`0x0049CD30`** | **Verified** | Caller pusht direkt aus globaler `.data`: `0x00440418: mov cx, word [0x49CD30]; push ecx; call 0x443B20` (Smoke-Eval-Dispatcher) |
| Primary state | `0x9D42` 📊 | `0x0049C874` (8 acc) | Probable | indirekte Reads scale=2 |
| State cluster | `0x9CA0/0x9CA2` | `0x0049CB0C/0x0049CB0E` | Speculative | benachbart |
| **Reference Z1 attrs (4 bytes)** | `0x9C30..0x9C33` | **`0x0049CD60..0x0049CD63`** | **Verified** | Code @ 0x004403D2..0x00440403: kopiert 4 Bytes von `[eax+0xC0..0xC3]` (Zoombini-Struktur) nach `[edi*4 + 0x49CD60..63]` — exakt das v1-Reference-Bitfield-Pattern |
| Reference Z2 attrs (4 bytes) | `0x9C34..0x9C37` | `0x0049CD64..0x0049CD67` (?) | Probable | folgend (analog zu v1-Layout) |
| Comparison-state | — | `0x0049CA92` | Probable | `cmp di, [0x49CA92]; jne` als Logik-Branch |

---

## 11. Stone Rise / Slides (v1 Seg 45) — **echte Init noch nicht verifiziert**

**Problem:** `slides.mhk`-Push (`0x00439229`) liegt in der `WinMain`-CFG-Validierung
(`0x00438E30`). Echte Stone-Rise-Init muss anders lokalisiert werden.

### Kandidaten (Code-Layout zwischen Smoke und Tunnels, `0x440000..0x44E000`)

| Funktion | Größe | Caller | Cmp-Konstanten | data range | Bewertung |
|----------|-------|--------|----------------|------------|-----------|
| `0x00440850` | **4704 B** | 2 | 1..7, 30 | `0x48F6DC..0x4A4AF8` (sehr breit) | Largest, plausibel als Stone-Rise-Init |
| `0x00441E30` | 912 B | 2 | 8, 32, 76, 367 | breit | "Cheat on" — vermutlich Cheat-Helper, puzzle-übergreifend |
| `0x00444070` | 2928 B | 2 | 1..5, 8, 40, 70 | `0x49CA94..0x49CD72` (= Smoke-Region!) | Smoke-related |
| `0x00448500` | 2352 B | 1 | 1, 2, 4, 6, 10, 17, 107, 112, 200, 239 | breit | engine? |
| `0x0044A990` | 1792 B | 3 | 3..6, 16, 32 | sehr eng `0x49B0E8..0x4A35B8` | wahrscheinlich Hotel-Helper |
| `0x0044F190` | 1136 B | 1 | — (`Fireworks/Cheat Text`-Strings) | — | Cheat-Display, nicht Stone-Rise |

**Bester Kandidat:** `0x00440850` — größte Funktion mit puzzle-typischen Cmp-Konstanten
1..7 (passt zu Difficulty 1..3 + zoombini-counts 1..16).
**Konfidenz:** Speculative — kein harter Marker, Pattern-basiert.

### v1-Match-Codes nicht in v2-Code

v1 nutzt für Stone Rise Match-Codes `0x1FE` (Hair), `0x1FF` (Eyes), `0x200` (Nose),
`0x201` (Feet), `0x1F5` (null). **Keiner dieser Werte ist in v2-PE als Literal vorhanden.**
Erwartung: Match-Codes sind in MHK-Daten gespeichert, nicht im Code (das Format wurde
nicht geändert — siehe `V1_VS_V2_COMPARISON.md` Slides.MHK = bitweise identisch).

### Lücke
Echte Stone-Rise-Init bleibt **Speculative**. Nächster Schritt: Im Run mit Memory-Diff
zwischen "Stone Rise gestartet" und "Stone Rise nicht gestartet" identifizieren,
welche `.data`-Region geschrieben wird.

---

## 12. Lion's Lair / Tunnels (v1 Seg 48)

**Loader:** `0x0044FF30` (1578 B, 1 Caller `0x0044E9D0`).
**Reachable functions:** 8.

### Difficulty-Dispatcher

```
@0x004500EC  jmp [reg*4 + 0x0045058C]  (4 cases)  src=?
@0x004504B9  jmp [reg*4 + 0x0045059C]  (4 cases)  src=[0x004A2B6A]
```

### Mappings

| Bedeutung (v1) | v1 DS | v2 .data VA | Konfidenz | Beleg |
|----------------|-------|-------------|-----------|-------|
| **DIFFICULTY** | `0xA83E` ⚠️ | **`0x004A2B6A`** | **Verified** | `0x004504A7: movsx eax, word [0x4A2B6A]; cmp eax, 3; ja default; jmp [eax*4+0x45059C]` — direkter Dispatcher |
| Primary state | `0xA90E` 📊 | `0x004A2B68` (4 acc, 4W) | Speculative | benachbart zu Difficulty |
| **rule[0/1] structs (13 byte each)** | `0xA79A`/`0xA7A7` | `0x004A2C4D`/`0x004A2C52` | **Probable** | beide 8 writes scale=1 — passt zu zwei 13-Byte-Strukturen |
| Tunnel-Config | `0xA840/A852/A85A` | `0x004A2B60/0x004A2B62` | Speculative | benachbart, je 2 writes |
| ruleMode | `0xA798` ⚠️ | `0x004A2C3E/A2C48/A2C4A` | Speculative | Sequenz benachbarter Writes |
| State pointer | — | `0x0049293A` (2 reads scale=1 in fn 0x44FF30) | Speculative: static lookup |  read-only Lookup |

### Static-data
String `'Out of Memory.'` @ `0x00453295` — Generic Engine-Trace, nicht puzzle-spezifisch.

---

## Engine-Globals (puzzle-übergreifend)

In allen Puzzle-Loadern referenziert:

| VA | Verwendung | Hypothese | Konfidenz |
|----|-----------|-----------|-----------|
| `0x004950C8` | 12/12 Puzzles | `engineFlag` (puzzle loaded) | **Probable** |
| `0x004A2818` | 12/12 Puzzles + dispatcher-Source bei Hotel/Net | globaler State-Pointer | **Probable** |
| `0x004A4B1C` | 10/12 Puzzles | Speculative |
| `0x004A4878` | 9/12 Puzzles | Speculative |
| `0x0049B0E6` | 8/12 Puzzles | Speculative |
| **`0x004A4AAA`** | mehrere Puzzles, immer `cmp 0` | Speculative: globaler Mode-Schalter | beobachtbar in Cliff/Hotel/Pizza/Smoke/Tunnels |
| `0x004A36B8` | 5/12 Puzzles | Speculative |
| `0x004A3830` | 4/12 Puzzles + indirekte Writes scale=2 | shared state-array | Probable |

---

## Difficulty-Encoding

| Puzzle | v1 | v2 | Diff |
|--------|----|----|----|
| Cliffs | 0..3 | **1..4** | +1 (1-basiert) |
| Caves | 1..4 | 1..4 | identisch |
| Hotel | 0..3 | 0..3 | identisch (cmp [VA], 3 direkt) |
| Pizza | 0..3 | 0..3 | identisch |
| übrige | meist 1..4 | meist 1..4 | identisch |

**Cliffs ist die einzige bekannte Encoding-Inkonsistenz** zwischen v1 und v2.

---

## Solver-Auswirkung (Stand nach Iteration 2)

Pro Solver-File in `Solvers/` müssen die DS-Adressen für v2 ausgetauscht werden.
Das setzt voraus, dass:

1. Ein PE32-Memory-Reader implementiert ist (PE hat keinen DS-Segment-Anker).
2. Die hier vorgeschlagenen v2-VAs durch Live-Memory-Validation bestätigt wurden.

### Solver-tauglich auf v2 (Verified-Mappings)
| Puzzle | Stand |
|--------|-------|
| **Allergic Cliffs** | ✅ 8 Verified Mappings (vollständig) |
| **Pizza Pass** | ✅ 7 Verified (Difficulty + 4 Wunsch-Arrays + 3 Trolle-Flags) |
| **Stone Cold Caves** | ✅ Difficulty Verified (`0x00494658`), Filter-Arrays Probable |
| **Hotel Dimensia** | ✅ Difficulty + Constraint-Tabellen Verified (Match-Logik direkt im `.data`) |
| **Mirror Machine** | ✅ Difficulty Verified (`0x0049CD30`), Reference-Bitfield Verified |
| **Tunnels (Lion's Lair)** | ✅ Difficulty + Rule-Structs Probable |
| **Maze2 (Bubblewonder)** | Probable: Slot-Counter + Difficulty-Source |

### Solver-machbar mit zusätzlicher Iteration
- **Mudball Wall**: Wall-State Probable, aber 8-case-Dispatcher unklar (erweiterte Mechanik?)
- **Fleens**: Argument-Trace nötig

### Solver für v2 derzeit **NICHT** machbar
- **Captain Cajun's Ferry** — kein eindeutiger Init-Pfad gefunden. Wrapper-Kandidaten
  (0x004086A0, 0x00408F60) zeigen Caves-Data-Range — wahrscheinlich Caves-Subroutinen,
  nicht Ferry. Möglich: Ferry wird als Engine-Trigger ohne separaten Wrapper geladen.
- **Stone Rise** — Wrapper-Kandidat `0x00440850` (4704B, engine-setup) nutzt
  überraschenderweise dieselbe `.data`-Region wie Mirror Machine (0x49C..-0x49D...).
  Möglicherweise ist Stone Rise eng mit Mirror Machine verzahnt, oder 0x00440850 ist
  KEIN Stone-Rise-Wrapper sondern ein erweiterter Mirror-Helper.
- **Titanic Tattooed Toads / Lilly** — Loader 0x00419770 hat 10 verschiedene Wrapper-
  Caller, von denen einige andere Puzzle sind (Hotel, Fleens, Stone-Rise-Kandidat).
  Das spricht stark dafür, dass `0x00419770` ein **shared MHK-Resource-Loader** ist
  (lädt Lilly-Sprites/Sounds für andere Puzzle). Echte Toads-Init muss separat
  gefunden werden.

---

## Lücken (alle 12 Puzzles)

1. **Slides + Ferry**: echte Init-Funktionen über Live-Memory-Diff oder zusätzliche
   Marker-Suche. Ohne diesen Schritt kein Solver für v2 möglich.
2. **Lilly + Net + Smoke**: Loader sind generisch (viele Caller). Echte
   puzzle-spezifische Init muss als Wrapper-Funktion eine Ebene höher gefunden
   werden.
3. **Engine-Globals-Bedeutung**: `0x004950C8`, `0x004A2818`, `0x004A4AAA` haben
   nur Hypothesen. Per-Variable-Disasm der Lese-/Schreibstellen (Pattern-Match
   gegen v1's `engineFlag` bei `DS:0xAA32`).
4. **`.data`-Region-Hüllen** sind in `V2_BINARY_MAP.md` grob; per Symbol-Größe
   ist nicht eindeutig.
5. **Eval-Funktionen pro Puzzle**: außer Cliffs (`0x00406760`) und Pizza
   (`0x00435020`) sind die Eval-Funktionen nicht spezifisch identifiziert,
   nur als Sub-Funktionen in der reachable-Menge.
6. **Mudball-8-case-Dispatcher**: in v1 ist Difficulty 0..3 oder 1..4. In v2
   `cmp eax, 7; jmp [eax*4+tbl]` mit 8 cases. Möglicherweise wurde Mudball in
   v2 erweitert (mehr Schwierigkeitsgrade?). Verifikation ausstehend.
