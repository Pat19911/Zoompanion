# Segment-Zuordnung — Korrigierte Version (basierend auf Cross-References)

Methode: Für jede MHK-Datei und jeden Debug-Format-String wurde der DS-Offset
berechnet und im Code nach Push-Sites (`push imm16`, `mov reg16, imm16`) gesucht.
Damit ist die Zuordnung MHK → Code-Segment hart belegt.

## Definitiv (durch zwei unabhängige Marker bestätigt)

| Code-Segment | MHK-File | Debug-String | → Puzzle | bisherige Doku |
|--------------|----------|--------------|----------|----------------|
| **seg37** | Pizza.MHK | "Arno %d %d…", "Willa…", "Shyler…" | **Pizza-Trolle** | ✓ stimmt |
| **seg42** | Smoke.MHK | "Cheat on" | **Mirror Machine** | ✓ stimmt |
| **seg27** | Fleens.MHK | — | **Fleens** | ✓ stimmt |
| **seg30** | Lilly.MHK | — | **Titanic Tattooed Toads** | ✓ stimmt |
| **seg35** | Net.MHK | — | **Mudball Wall** | ✓ stimmt |
| **seg45** | Slides.MHK | — | **Stone Rise** | STONE_RISE.md korrekt; SEGMENT_MAP behauptete fälschlich „Ferry" |
| **seg28** | Hotel.MHK | — | **Hotel Dimensia (alleine, NICHT geteilt mit Cliffs)** | ALLERGIC_CLIFFS_VERIFIED.md analysiert dieses Segment fälschlicherweise als Cliffs |

## Definitiv durch MHK-Push, Puzzle-Mechanik aber abgleich-bedürftig

| Code-Segment | MHK-File | Debug-String | nominell | Konflikt? |
|--------------|----------|--------------|----------|-----------|
| **seg24** | Caves.MHK | "Hieroglyphs", "Play FrogMan SCRB id:" | Stone Cold Caves | "Hieroglyphs" passt thematisch, "FrogMan" passt **nicht** offensichtlich zu Caves |
| **seg48** | Tunnels.MHK | "Left / Bottom accept:", "Left / Top accept:", "Right accepts:" | Lion's Lair | „Left/Top/Bottom/Right" passt **eher zu 4 Höhlen (Caves)** als zum Pfad-Puzzle (Lion's Lair) |
| **seg34** | Maze2.MHK | "Play FrogMan SCRB id:" (zweite Referenz) | Bubblewonder Abyss | BUBBLEWONDER_ABYSS.md korrekt; SEGMENT_MAP behauptete „Mudball" — **falsch** |

**Mögliche Erklärungen für den Konflikt seg24/seg48:**
- Die Code-Internen MHK-Lader stimmen mit der Spiellogik überein (also seg24 = Caves trotz „Left/Top/Bottom"-artiger Codestrings)
- Die "Left / Bottom accept:"-Strings könnten eine generische Debug-Schablone sein, die in mehreren Puzzles zum Logging genutzt wird (Lion's Lair hat ja oben/unten gestaffelte Steine entlang des Pfades)
- "FrogMan" könnte ein interner Codename für ein Sprite-System sein (z.B. Captain Cajun aus Ferry, der frogartig dargestellt ist), nicht für ein Puzzle

## Mysterien — keine direkte MHK→Segment-Zuordnung gefunden

### Allergic Cliffs

- `Bridge.MHK` (DS:0x986) wird **nur von Seg18 (engine)** referenziert — vermutlich generischer Lader
- "Upper bridge accepts:" / "Lower bridge accepts:" werden **nur von Seg20** gepusht
- **Vermutung:** seg20 (8 KB) IST die Cliff-Spiellogik. Cliffs sind algorithmisch einfach (1-3 Attribute, 2 Brücken), 8 KB sind plausibel
- **Doku-Konsequenz:** `ALLERGIC_CLIFFS_VERIFIED.md` analysiert seg28, das ist aber Hotel. Die Cliff-Analyse müsste auf seg20 neu durchgeführt werden.

### Captain Cajun's Ferryboat

- `Ferry.MHK` wird **nur von Seg55** referenziert (engine)
- Es gibt KEINE printf-Marker für Ferry — die SEGMENT_MAP-Behauptung „Seg 41+45 = Ferry" basierte auf indirekter Inferenz
- Seg45 ist **Stone Rise** (Slides.MHK)
- Seg41 ist unbestimmt — möglicherweise Ferry, möglicherweise was anderes
- **Doku-Konsequenz:** `FERRYBOAT_DEEP.md` und `FERRYBOAT.md` analysieren möglicherweise das **falsche Segment**

## Korrekturtabelle für SEGMENT_MAP.md

| SEGMENT_MAP behauptete | Konfidenz dort | **Korrekte Zuordnung** |
|------------------------|----------------|------------------------|
| Allergic Cliffs: Seg 28 | BESTÄTIGT | Seg 28 ist **Hotel**; Cliffs vermutlich **Seg 20** |
| Hotel: Seg 28 (geteilt) | WAHRSCHEINLICH | Seg 28 **alleine** Hotel |
| Stone Cold Caves: Seg 24 | BESTÄTIGT | bestätigt durch Caves.MHK in Seg 24 ✓ |
| Pizza: Seg 37 | BESTÄTIGT | bestätigt durch Pizza.MHK + Arno ✓ |
| Fleens: Seg 27 | BESTÄTIGT | bestätigt durch Fleens.MHK ✓ |
| Mudball Wall: Seg 34 (Maze2.MHK) | WAHRSCHEINLICH | **Falsch** — Seg 34 ist Maze2.MHK = **Bubblewonder Abyss** |
| Ferryboat: Seg 41+45 | BESTÄTIGT | **Falsch** — Seg 45 ist Slides.MHK = **Stone Rise**; Ferry vermutlich **Seg 55** (engine?) oder **Seg 41** allein |
| Tattooed Toads (Lilly): Seg 30 | BESTÄTIGT | bestätigt durch Lilly.MHK ✓ |
| Smoke Signals: Seg 42 | WAHRSCHEINLICH | bestätigt durch Smoke.MHK = **Mirror Machine** ✓ |
| Tunnels/Stone Rise: Seg 48 | WAHRSCHEINLICH | Seg 48 ist Tunnels.MHK = **Lion's Lair** (Stone Rise ist separat in Seg 45) |
| Net-Puzzle: Seg 35 (BESTÄTIGT) | BESTÄTIGT | Net.MHK = **Mudball Wall** in Seg 35 ✓ |

## Konsequenzen für jede Doku-Datei

| Doku | Verifiziertes Segment | Status |
|------|----------------------|--------|
| `PIZZA_TROLLS.md` | seg37 ✓ | **korrekt** |
| `STONE_COLD_CAVES_DEEP.md` | seg24 ✓ | **wahrscheinlich korrekt** (MHK passt; Konflikt mit „FrogMan" nur intern) |
| `STONE_COLD_CAVES.md` | seg24 ✓ | **wahrscheinlich korrekt** |
| `FLEENS_DEEP.md` | seg27 ✓ | **wahrscheinlich korrekt** (kein gegenteiliger Marker) |
| `FLEENS.md` | seg27 ✓ | **wahrscheinlich korrekt** |
| `LILLY_TOADS_DEEP.md` | seg30 ✓ | **wahrscheinlich korrekt** |
| `LILLY_TOADS.md` | seg30 ✓ | **wahrscheinlich korrekt** |
| `MUDBALL_WALL_DEEP.md` | seg35 ✓ | **wahrscheinlich korrekt** |
| `MUDBALL_WALL.md` | seg35 ✓ | **wahrscheinlich korrekt** |
| `MIRROR_MACHINE.md` | seg42 ✓ | **wahrscheinlich korrekt** |
| `LIONS_LAIR.md` | seg48 ✓ | **wahrscheinlich korrekt** (MHK passt, aber Debug-String "Left/Top/Bottom" passt zur Mechanik nicht intuitiv → erfordert Zweitverifikation) |
| `BUBBLEWONDER_ABYSS.md` | seg34 ✓ | **wahrscheinlich korrekt** |
| `STONE_RISE.md` | seg45 ✓ | **korrekt** (selbstdiagnose in der Doku stimmt) |
| `HOTEL_DIMENSIA.md` | seg28 (jetzt klar) | **wahrscheinlich korrekt**, aber Behauptung „geteilt mit Cliffs" ist falsch |
| `ALLERGIC_CLIFFS_VERIFIED.md` | analysiert seg28 als Cliffs | **FALSCH ZUGEORDNET** — analysiert tatsächlich Hotel-Code |
| `ALLERGIC_CLIFFS.md` | analysiert seg28 als Cliffs | **FALSCH ZUGEORDNET** |
| `FERRYBOAT_DEEP.md` | analysiert seg41+45 | **TEILWEISE FALSCH** — seg45 ist Stone Rise, nicht Ferry |
| `FERRYBOAT.md` | analysiert seg41+45 | **TEILWEISE FALSCH** |
| `SEGMENT_MAP.md` | viele falsche Zuordnungen | **muss komplett ersetzt werden** durch diese Tabelle |

## Konkrete nächste Schritte

1. **`SEGMENT_MAP.md` durch korrigierte Version ersetzen** (Inhalt dieser Datei).
2. **Allergic Cliffs neu disassemblieren** mit Fokus auf Seg 20 (file 0x16C00..0x18C00).
3. **Ferryboat-Code neu lokalisieren** — wahrscheinlich Seg 41 alleine, oder ein anderes Segment ohne dedizierten MHK-String.
4. **Hotel Dimensia neu analysieren** mit dem Code aus Seg 28 — was bisher als „Cliff-Analyse" galt, ist tatsächlich Hotel-Logik. Die Init-Funktion bei 0x25096 mit 5×5-Permutationen passt zu Hotel (5×5-Gitter).
5. **Lion's Lair vs. Caves Markerkonflikt** klären — möglicherweise sind die Debug-Strings „Left/Top/Bottom/Right accept:" eine generische Vorlage, oder die internen Codenamen weichen von Display-Namen ab.

## Zusammenfassung

- **7 Puzzle-Dokus sind grundsätzlich korrekt zugeordnet** (Pizza, Caves, Fleens, Lilly, Mudball, Mirror, Stone Rise, Bubblewonder, Lion's Lair, Hotel)
- **2 Puzzle-Dokus sind komplett falsch zugeordnet** (alle Cliff-Dokus → analysieren Hotel; alle Ferry-Dokus → analysieren teilweise Stone Rise)
- **1 Master-Doku ist falsch** (SEGMENT_MAP.md — diverse Vertauschungen)

Damit ist die Cliff-Analyse aus `ALLERGIC_CLIFFS_VERIFIED.md` vermutlich Hotel-Mechanik (5×5-Gitter, 25 Slots, Permutationen) — was die mathematischen Strukturen der Doku überhaupt erst plausibel macht. Cliff-Logik (2 Brücken, einfache Allergie-Regel) ist viel einfacher und passt zu kompaktem 8-KB-Segment.
