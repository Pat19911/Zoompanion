# DS-Variablen-Karte aller 12 Puzzles (verifiziert/vermutet 2026-04-25)

Konsolidierte Übersicht aller wichtigen DS-Adressen pro Puzzle.
Konfidenz-Markierung:
- ✅ = direkt aus Init-Disasm verifiziert
- 📊 = Top-Schreibziel aus Master-Analyse (Schreibvolumen)
- ⚠️ = Vermutet aus alter Doku, nicht direkt verifiziert

---

## 1. Allergic Cliffs (Seg 23)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x79DA` | DIFFICULTY (0..3) | ✅ verifiziert |
| `0x79D4` | engagement_flag | ✅ |
| `0x79E0` | ready_flag | ✅ |
| `0x79E2` | attempt_counter (0..6) | ✅ |
| `0x7A0E` | WHICH_CLIFF (0=Lower, 1=Upper) | ✅ |
| `0x7A10` | ALLERGY_COUNT (1..3) | ✅ |
| `0x7A11..0x7A15` | ALLERGY_TYPE[5] (1=Hair..4=Feet) | ✅ |
| `0x7A16..0x7A1A` | ALLERGY_VALUE[5] (0..4) | ✅ |

---

## 2. Stone Cold Caves (Seg 24)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x7A80` | DIFFICULTY (1..4, **1-basiert!**) | ✅ |
| `0x7A82` | Primary attribute type (0..3) | ✅ |
| `0x7A84` | Secondary attribute type | ✅ (für Diff 2+) |
| `0x7A48..0x7A5C` | cave_filter_type[11] | ✅ |
| `0x7A5E..0x7A72` | cave_filter_value[11] (Encoding 1..20) | ✅ |
| `0x7A7C` | counter | 📊 |
| `0x7BAE..0x7BC0` | additional state (genullt in Init) | 📊 |

---

## 3. Captain Cajun's Ferry (Seg 26 — KORRIGIERT)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x7C4C` | primary state pointer | 📊 (9× Schreibziel) |
| `0x7C3E` | ? | 📊 |
| `0x7C76`, `0x7C74` | Sitzplatz-Position | 📊 |
| `0x7C50`, `0x7C4E` | ? | 📊 |
| `0x7C6C`, `0x7C96` | ? | 📊 |

⚠️ Adressen 0xA34E, 0xA468, 0xA4A4 (alte Doku) sind STONE-RISE-Adressen, nicht Ferry!

---

## 4. Fleens (Seg 27)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x7CB2` | primary state | 📊 |
| `0x7CF0` | ? | 📊 |
| `0x7C9E`, `0x7CC0`, `0x7CAE`, `0x7CF6` | state cluster | 📊 |
| `0x7CEA` | difficulty / play_count (0..3) | ⚠️ (alte Doku) |
| Shift/Redirect-Tabellen | im 0x7Cxx Bereich | ⚠️ position unverifiziert |

---

## 5. Hotel Dimensia (Seg 28)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x7E42` | DIFFICULTY (0..3) | ✅ (aus „Cliff-VERIFIED" Doku — eigentlich Hotel) |
| `0x7E44` | first_time_flag | ✅ |
| `0x7E48` | attr_type_1 (0..3) | ✅ |
| `0x7E4A` | attr_type_2 | ✅ |
| `0x7E4C` | attr_type_3 (für Diff 3) | ✅ |
| `0x7E58` | num_slots (25 oder 125) | ✅ |
| `0x7E5E` | primary state pointer | 📊 (9× Schreibziel) |
| `0x7E24` | primary state pointer | 📊 (9×) |
| `0x0316` | table_left[25] (5×5 für Diff 1+2) | ✅ |
| `0x0348` | table_right[25] | ✅ |

---

## 6. Titanic Tattooed Toads (Seg 30)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x8C6C` | primary state | 📊 |
| `0x85E6..0x85EC` | Lilypad-Patterns? | 📊 |
| `0x8C38`, `0x8C40` | Toad-State | 📊 |
| `0x11A4` | difficulty (1..4) | ⚠️ (alte Doku) |
| `0x854A` | anger level | ⚠️ |
| `0x8548` | toad_config | ⚠️ |
| `0x89F4` | toads_arrived | ⚠️ |
| `0x89F0` | toads_required | ⚠️ |
| `0x8A00` | pattern_cycle_max | ⚠️ |
| `0x8C48` | crab_type | ⚠️ |

⚠️ Toads nutzt seg134 als externes Daten-Segment (331 Cross-Calls aus Seg 30).

---

## 7. Bubblewonder Abyss (Seg 34)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x92A2` | Rule-Slot-Counter | ✅ verifiziert in Diff-3-Generator |
| `0x92A4`, `0x92A6` | auxiliary counters | ✅ |
| `0x92AA`, `0x92AC` | random_range Min/Max | ✅ |
| `0x9318` | ? | 📊 |
| `0x926E`, `0x926C` | ? | 📊 |
| `0x9264` | Difficulty? | 📊 |
| `0xFFFF:0x7A8` | Rule-Slot-Array (shared memory) | ✅ |
| `0xFFFF:0x72A` | Lookup-Translation-Tabelle | ✅ |
| `cs:0x1742` (in Seg 34) | Max-Iterations-Tabelle pro Slot | ✅ |
| `0x925A` | difficulty | ⚠️ (alte Doku, nicht direkt verifiziert) |

---

## 8. Mudball Wall (Seg 35)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x9506` | Wall-State | 📊 (10× Schreibziel) |
| `0x950A` | Wall-State | 📊 (8×) |
| `0x9520` | Slot/Position | 📊 (7×) |
| `0x94FA` | Selected axis | 📊 (7×) |
| `0x94FE` | Selected axis | 📊 (6×) |
| `0x9508`, `0x9514` | Wall-State | 📊 |
| `0x952A`, `0x9528` | Slot | 📊 |

⚠️ Alte Solver-Adressen (0x9368, 0x94F8) waren Vermutungen — durch Master-Analyse korrigiert.

---

## 9. Pizza Pass (Seg 37)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x979E` | active troll/topping count | 📊 (7×) |
| `0x96B2`, `0x96C8`, `0x96CC` | Troll-State | 📊 |
| `0x97D2` | Selected toppings bitmask | 📊 (5×) |
| `0x97F4..0x97F8` | Pizza state pointers (FAR ptr) | 📊 |
| `0x96D2` | difficulty (0..3) | ⚠️ (alte Doku) |
| `0x96D6` | num_active_trolls | ⚠️ |
| `0x96D8` | num_toppings | ⚠️ |
| `0x973A` | arno_wants[8] | ⚠️ |
| `0x974A` | willa_wants[8] | ⚠️ |
| `0x975A` | shyler_wants[8] | ⚠️ |
| `[0xFFFF:0x1984]` | Pizza-config-Tabelle (FAR ptr) | ✅ (im Action-Dispatcher gesehen) |

---

## 10. Mirror Machine (Seg 42)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0x9D42` | primary state | 📊 (11× Schreibziel — sehr aktiv) |
| `0x9CA0`, `0x9CA2`, `0x9D44` | state cluster | 📊 |
| `0x9C30..0x9C33` | Reference Zoombini 1 attrs (4 bytes Bitfield) | ✅ |
| `0x9C34..0x9C37` | Reference Zoombini 2 attrs (für Diff 2+) | ⚠️ |
| `0x9D4E` | ? | 📊 |
| `0x9C26` | difficulty | ⚠️ (alte Doku) |
| `0x9CBC` | clue_table | ⚠️ |

---

## 11. Stone Rise (Seg 45)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0xA466` | pair-related state | 📊 (6×) |
| `0xA458` | ? | 📊 (5×) |
| `0xA464`, `0xA456` | ? | 📊 |
| `0xA34C`, `0xA34A`, `0xA452`, `0xA54E` | state | 📊 |
| `0xA4A4` | pair_count? | 📊 (3×, alte Doku) |
| `0xA34E` | level (1..3) | ⚠️ (alte Doku) |
| `0xA468` | zoombini_count | ⚠️ |
| `0xA4A6` | rule_table[16] | ⚠️ |
| `0xA484` | edge_map[16] | ⚠️ |
| Match-Codes: 0x1FE=Hair, 0x1FF=Eyes, 0x200=Nose, 0x201=Feet, 0x1F5=null | ✅ konsistent |

---

## 12. Lion's Lair (Seg 48)

| Adresse | Bedeutung | Konfidenz |
|---------|-----------|-----------|
| `0xA90E` | primary state | 📊 (6×) |
| `0xA840` | Tunnel-Config | 📊 (4×) |
| `0xA852`, `0xA85A` | Tunnel-Config | 📊 |
| `0x9EF4` | engine state | 📊 |
| `0xA838`, `0xA908`, `0xA906`, `0xA86C` | state | 📊 |
| `0xA798` | ruleMode (1=L/R, 2=4-way) | ⚠️ (alte Doku, vermutlich in Eval gelesen) |
| `0xA79A` | rule[0] struct (13 bytes) | ⚠️ |
| `0xA7A7` | rule[1] struct (13 bytes) | ⚠️ |
| `0xA83E` | difficulty (0..3) | ⚠️ |
| Lion's Lair TYPE-Kodierung: 1=Feet, 2=Nose, 3=Eyes, 4=Hair (bestätigt) | ✅ |

---

## Methodische Notizen

### Master-Analyse vs. alte Doku

Die **Master-Analyse** zeigt die Top-DS-Schreibziele der Init-Funktion. Die **alte Doku** hat
Adressen behauptet, die teilweise in **Eval-Funktionen** gelesen werden (also nicht in Init geschrieben). Daher:

- 📊 Master-Top-Writes = wahrscheinlich Init-State / Pool-Daten
- ⚠️ Alte-Doku-Adressen = möglicherweise Eval-Lesungen / Spieler-Selektion

Beide können richtig sein für verschiedene Use-Cases.

### Empirische Validation

Pro Solver muss gegen das laufende Spiel getestet werden. Vorgehen:
1. Spiel starten, Puzzle laden
2. ProcessMemory.cs anhängen + GameState.Calibrate()
3. Solver ausführen, Werte mit sichtbarem Spielzustand vergleichen
4. Wenn Werte nicht passen: alternative Adressen testen oder Memory-Scan

### Konsequenzen für Solver

| Solver | Status |
|--------|--------|
| `AllergicCliffsSolver.cs` | ✅ Mit verifizierten Adressen reimplementiert |
| `StoneColdCavesSolver.cs` | ✅ Mit verifizierten Adressen aus Init-Disasm |
| `HotelDimensiaSolver.cs` | ✅ Adressen aus „Cliff-VERIFIED" (eigentlich Hotel) |
| `BubblewonderAbyssSolver.cs` | ✅ Mit Diff-3-Generator-Adressen |
| `MirrorMachineSolver.cs` | ✅ Mit Reference-Bitfield 0x9C30..0x9C33 |
| `LionsLairSolver.cs` | ⚠️ Alte Adressen + Master-Fallback |
| `FerryboatSolver.cs` | ⚠️ KORRIGIERT auf Seg 26 (0x7Cxx) |
| `FleensSolver.cs` | ⚠️ Adressen vermutet |
| `MudballWallSolver.cs` | ⚠️ Master-Adressen statt alte |
| `PizzaPassSolver.cs` | ⚠️ Adressen vermutet |
| `StoneRiseSolver.cs` | ⚠️ Aus alter Doku + Master-Fallback |
| `TitanicToadsSolver.cs` | ⚠️ Adressen vermutet |
