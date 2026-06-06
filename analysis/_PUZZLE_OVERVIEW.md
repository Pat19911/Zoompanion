# Zoombinis Puzzle Übersicht — Verifizierte Master-Analyse (2026-04-25)

Konsolidierte Übersicht aller 12 Puzzles, basierend auf systematischer Analyse via NE-Header,
MHK-Push-Cross-References und Funktions-Prolog-Scans.

## Verifizierte Segment-Zuordnung

| # | Puzzle | MHK | Seg | File-Offset | Größe | Funcs | Diff-Disp | rand_range | direct_rand |
|---|--------|-----|-----|-------------|-------|-------|-----------|------------|-------------|
| 1 | **Allergic Cliffs** | Bridge.MHK | **23** | 0x16200 | 8395 B | 11 | 1 | 7 | 0 |
| 2 | Stone Cold Caves | Caves.MHK | **24** | 0x18C00 | 12341 B | 32 | 7 | 12 | 0 |
| 3 | **Captain Cajun Ferry** | Ferry.MHK | **26** | 0x1D800 | 7357 B | 15 | 5 | 9 | 0 |
| 4 | Fleens | Fleens.MHK | **27** | 0x1FE00 | 12259 B | 26 | 2 | 13 | 0 |
| 5 | Hotel Dimensia | Hotel.MHK | **28** | 0x23800 | 16016 B | 30 | 3 | 0 | 19 |
| 6 | Titanic Tattooed Toads | Lilly.MHK | **30** | 0x2A600 | 31056 B | 55 | 16 | 22 | 0 |
| 7 | Bubblewonder Abyss | Maze2.MHK | **34** | 0x3CE00 | **32660 B** | **54** | 8 | **47** | 0 |
| 8 | Mudball Wall | Net.MHK | **35** | 0x47C00 | 12389 B | 21 | 4 | 4 | 16 |
| 9 | Pizza Pass | Pizza.MHK | **37** | 0x4E000 | 24368 B | 42 | **17** | 2 | 43 |
| 10 | Mirror Machine | Smoke.MHK | **42** | 0x5D200 | 25665 B | 47 | 7 | **52** | 0 |
| 11 | Stone Rise | Slides.MHK | **45** | 0x6CA00 | 21089 B | 35 | 4 | 1 | 6 |
| 12 | Lion's Lair | Tunnels.MHK | **48** | 0x76400 | 16890 B | 25 | **18** | 15 | 0 |

Die fett gedruckten Zellen sind besonders auffällig:
- **Lion's Lair**: 18 Difficulty-Dispatches (höchste Difficulty-Spezialisierung)
- **Pizza Pass**: 17 Difficulty-Dispatches + 43 direct rand (anderes Pattern als die anderen)
- **Bubblewonder + Mirror Machine**: extrem viele random_range (47 / 52) + größte Init-Funktionen
  → starkes Indiz für Pool/Selection-Mechanik wie bei Cliffs
- **Hotel**: 0 random_range, 19 direct rand (anders als alle anderen)

## Konfidenzstufen pro Puzzle

| Puzzle | Konfidenz | Doku-Status |
|--------|-----------|-------------|
| **Allergic Cliffs** | HOCH | ✅ Init voll analysiert + Pool/Selection-Algorithmus dokumentiert |
| Stone Cold Caves | HOCH | ✅ Master-Daten verifiziert; Detail-Disasm offen |
| **Captain Cajun Ferry** | HOCH (neu lokalisiert!) | ⚠️ Solver-Adressen müssen neu kalibriert werden |
| Fleens | HOCH | ✅ Master-Daten verifiziert |
| Hotel Dimensia | HOCH | ✅ Init-Funktionen aus alten "Cliff"-Dokus übernommen |
| Titanic Tattooed Toads | HOCH | ✅ Master-Daten verifiziert |
| Bubblewonder Abyss | HOCH | ⚠️ Pool/Selection vermutet, noch nicht verifiziert |
| Mudball Wall | HOCH | ⚠️ Solver-Adressen müssen verifiziert werden |
| Pizza Pass | HOCH | ✅ Master-Daten verifiziert |
| Mirror Machine | HOCH | ⚠️ Pool/Selection vermutet, noch nicht verifiziert |
| Stone Rise | HOCH | ✅ Master-Daten verifiziert |
| Lion's Lair | HOCH | ✅ Master-Daten verifiziert |

## Kategorisierung nach Mechanik-Pattern

### Attribut-basiert + ODER-Match
- Allergic Cliffs (verifiziert)
- Hotel Dimensia (vermutlich)

### Attribut-basiert + Mapping/Permutation
- Fleens (Shift + Redirect)
- Captain Cajun Ferry (Adjazenz-Sharing)
- Stone Rise (Pairing)
- Lion's Lair (Sortierung in Tunnel)
- Stone Cold Caves (Filter pro Wächter)
- Mirror Machine (Reference-Match)

### Nicht-Attribut-basiert
- Pizza Pass (binäre Topping-Auswahl)
- Mudball Wall (Eigenschafts-Mapping → Wall-Position)
- Titanic Tattooed Toads (Pattern-Matching auf Lilypads)
- Bubblewonder Abyss (Sequenz-Logik)

## Pool/Selection-Pattern (gezielte 50/50-Optimierung)

| Puzzle | Random-Calls | Init-Größe | Pool/Selection? |
|--------|--------------|------------|-----------------|
| **Allergic Cliffs** | 7 | 3351 B | ✅ VERIFIZIERT (4×125 = 500 Pool, TARGET=N/2 Selection) |
| **Mirror Machine** | 52 | 4190 B | ⚠️ STARK VERMUTET — höchste random_range-Anzahl |
| **Bubblewonder Abyss** | 47 | 4536 B | ⚠️ STARK VERMUTET — größte Init |
| Titanic Tattooed Toads | 22 | 3939 B | möglich — viele Lilypad-Konfigurationen |
| Lion's Lair | 15 | 2726 B | möglich |
| Fleens | 13 | 1811 B | unwahrscheinlich (kleines Init) |
| Stone Cold Caves | 12 | 2373 B | unwahrscheinlich |
| Captain Cajun Ferry | 9 | 1138 B | unwahrscheinlich (zu kleine Init) |
| Mudball Wall | 20 (4+16) | 2513 B | unwahrscheinlich (deduktiv, nicht gezielt) |
| Pizza Pass | 45 (2+43) | 3046 B | unwahrscheinlich (Trolle haben feste Vorlieben) |
| Stone Rise | 7 (1+6) | 3659 B | unwahrscheinlich |
| Hotel Dimensia | 19 | 2860 B | möglich (Hotel hat 5×5 und 5×5×5 Modi) |

## Was als nächstes für jedes Puzzle zu tun ist

1. **Cliffs** — nichts (komplett analysiert + Doku in `ALLERGIC_CLIFFS.md`)
2. **Mirror Machine, Bubblewonder Abyss** — Pool/Selection verifizieren (Init voll disassemblieren)
3. **Captain Cajun Ferry** — `FerryboatSolver.cs` mit korrekten Adressen 0x7Cxx neu schreiben
4. **Mudball Wall** — Adressen aus 0x950x-Cluster verifizieren
5. **Lion's Lair** — die 18 Difficulty-Dispatches gruppieren
6. **Andere** — Detail-Disasm der Init-Funktion + DS-State-Mapping

## Solver-Status

| Solver | Status |
|--------|--------|
| `AllergicCliffsSolver.cs` | STUB (Code-Sitz neu gefunden — muss reimplementiert werden) |
| `HotelDimensiaSolver.cs` | wahrscheinlich korrekt (nutzt Hotel-Adressen 0x7Exx) |
| `FerryboatSolver.cs` | **FALSCH** — nutzt Stone-Rise-Adressen statt Ferry (0x7Cxx) |
| `StoneRiseSolver.cs` | wahrscheinlich korrekt (Adressen kommen aus seg45) |
| `MudballWallSolver.cs` | UNSICHER — Adressen aus seg34=Bubblewonder fehlabgeleitet |
| `BubblewonderAbyssSolver.cs` | wahrscheinlich korrekt (eine Adresse 0x92A2 stimmt) |
| `PizzaPassSolver.cs` | UNSICHER — Adressen vermutet, nicht verifiziert |
| `StoneColdCavesSolver.cs` | UNSICHER — Adressen müssen verifiziert werden |
| `FleensSolver.cs` | UNSICHER — Adressen müssen verifiziert werden |
| `LionsLairSolver.cs` | UNSICHER — Adressen müssen verifiziert werden |
| `MirrorMachineSolver.cs` | wahrscheinlich teilweise korrekt (0x9C30..33 stimmt mit Cluster) |
| `TitanicToadsSolver.cs` | UNSICHER |

## Querverweise

Pro Puzzle eine eigene Doku-Datei:
- `ALLERGIC_CLIFFS.md` (komplett verifiziert)
- `STONE_COLD_CAVES.md` (verifizierte Übersicht)
- `CAPTAIN_CAJUN_FERRY.md` (KORRIGIERTE Lokalisierung Seg 26)
- `FLEENS_NEW.md`
- `HOTEL_DIMENSIA_VERIFIED.md`
- `TITANIC_TATTOOED_TOADS.md`
- `BUBBLEWONDER_ABYSS_VERIFIED.md`
- `MUDBALL_WALL_VERIFIED.md`
- `PIZZA_PASS.md`
- `MIRROR_MACHINE_VERIFIED.md`
- `STONE_RISE_NEW.md`
- `LIONS_LAIR_VERIFIED.md`

Plus die bestehenden `*_DEEP.md` und `*_KORREKTUR.md` Dateien für Historie und Detail.
