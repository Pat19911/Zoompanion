# Zoombini Puzzle — Segment-zu-Puzzle Zuordnung (verifiziert)

**Stand: 2026-04-25**, basierend auf Cross-Reference von MHK-Filenamen-Strings und
Debug-Format-Strings im Code (siehe `_SEGMENT_MAP_KORREKTUR.md` für die Methodik).

> ⚠️ Die alte Version dieser Datei (`_DEPRECATED_SEGMENT_MAP.md`) hatte mehrere
> falsche Zuordnungen, die in den Einzeldokus weiterverwendet wurden.

## Verifizierte Zuordnung

| Puzzle | MHK-Datei | **Code-Segment** | Datei-Offset | Beweis |
|--------|-----------|------------------|--------------|--------|
| Pizza-Trolle | Pizza.MHK | **37** | 0x4E000 | `Pizza.MHK` push + Arno/Willa/Shyler printf |
| Stone Cold Caves | Caves.MHK | **24** | 0x18C00 | `Caves.MHK` push |
| Fleens | Fleens.MHK | **27** | 0x1FE00 | `Fleens.MHK` push |
| Titanic Tattooed Toads | Lilly.MHK | **30** | 0x2A600 | `Lilly.MHK` push |
| Mudball Wall | Net.MHK | **35** | 0x47C00 | `Net.MHK` push |
| Mirror Machine | Smoke.MHK | **42** | 0x5D200 | `Smoke.MHK` push + "Cheat on" debug |
| Lion's Lair | Tunnels.MHK | **48** | 0x76400 | `Tunnels.MHK` push + "Left/Top/Bottom/Right accept" debug ¹ |
| Bubblewonder Abyss | Maze2.MHK | **34** | 0x3CE00 | `Maze2.MHK` push |
| Stone Rise | Slides.MHK | **45** | 0x6CA00 | `Slides.MHK` push |
| Hotel Dimensia | Hotel.MHK | **28** | 0x23800 | `Hotel.MHK` push |
| Allergic Cliffs | Bridge.MHK | **23** (verifiziert) | 0x16200 | Init-Funktion mit difficulty-dispatch und 12 Schreibzugriffen auf Allergy-Arrays in Seg 23 ² |
| Captain Cajun's Ferry | Ferry.MHK | **26** (verifiziert) | 0x1D800 | Ferry.MHK push aus Seg 26 + "Play FrogMan SCRB id:" push aus Seg 26 ³ |

¹ Der Debug-String "Left/Top/Bottom/Right accept" passt mechanisch nicht offensichtlich
zu Lion's Lair (Pfad-Puzzle, kein 4-Quadranten-System). MHK-Marker ist aber eindeutig.
Vermutlich generische Debug-Schablone.

² Bridge.MHK selbst wird nur von seg18 (engine, generischer Lader) referenziert.
Die echte Cliff-Spiellogik wurde durch Disassembly von Seg 23 (file 0x16200..0x182CB,
8395 B, 11 Funktionen) identifiziert. Die "Upper/Lower bridge accepts:"-Pushes liegen in
Seg 23, nicht in Seg 20. Seg 20 ist nur der `random_range(min, max)`-Wrapper (bei seg20:0x04E8).
Siehe `ALLERGIC_CLIFFS.md` für Details.

³ **KORRIGIERT 2026-04-25:** Ferry liegt in Seg 26 (file 0x1D800..0x1F4BD, 7357 B).
Captain Cajun ist intern als "FrogMan" referenziert. Frühere SEGMENT_MAP-Behauptung
"Seg 41+45 = Ferry" war doppelt falsch (Seg 45 ist Stone Rise, Ferry ist Seg 26).

## Engine- und Hilfssegmente

| Segment | Datei-Offset | Funktion |
|---------|-------------|----------|
| 14 | 0x0F800 | `rand()` (LCG-Implementierung) |
| 18 | 0x15C00 | Generischer MHK-Lader |
| 20 | 0x16C00 | Cliffs-Code (vermutet) |
| 33 | 0x38200 | Map / Picker |
| 36 | 0x4BC00 | Picker |
| 44 | 0x65E00 | Engine: Hotspots/Features (Zoombini.MHK lädt hier) |
| 50, 51, 53, 55, 59 | 0x7C000+ | Engine-Core (von allen Puzzles aufgerufen) |

## Konfidenz-Stufen für jede Zuordnung

| Konfidenz | Bedeutung | Anzahl |
|-----------|-----------|--------|
| **HOCH** (zwei unabhängige Marker) | MHK-Push **und** Debug-Printf in selbem Segment | Pizza, Mirror, Lion's Lair |
| **HOCH** (MHK-Push alleine) | MHK-Filename wird vom Segment gepusht | Caves, Fleens, Lilly, Mudball, Bubblewonder, Stone Rise, Hotel |
| **MITTEL** (Indizien) | Cliffs (Debug-Strings in seg20, kompakte Größe plausibel) | Cliffs |
| **NIEDRIG** (Vermutung) | Ferry (kein dedicated MHK-Push gefunden) | Ferry |

## Korrekturen gegenüber der alten SEGMENT_MAP

| Alte Behauptung | Tatsächlich |
|-----------------|-------------|
| Allergic Cliffs in Seg 28 | Hotel in Seg 28; Cliffs vermutlich Seg 20 |
| Hotel in Seg 28 (geteilt) | Hotel **alleine** in Seg 28 |
| Mudball in Seg 34 (Maze2.MHK) | **Bubblewonder** in Seg 34 (Maze2.MHK) |
| Net-Puzzle in Seg 35 | Net.MHK in Seg 35 = **Mudball** ✓ |
| Ferry in Seg 41+45 | Seg 45 = **Stone Rise**; Seg 41 vermutlich Ferry |
| Tunnels/Stone Rise in Seg 48 | Seg 48 = Lion's Lair (Tunnels.MHK); Stone Rise separat in Seg 45 |
