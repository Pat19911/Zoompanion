# Trust Audit aller Reverse-Engineering-Dokus

Geschrieben **nach** der Cliff-Korrektur, in der sich gezeigt hat dass eine Doku mit
"VERIFIED" im Namen mehrere unbelegte Annahmen enthielt. Wenn das **eine** so falsch war,
muss man fragen wie die anderen aussehen. Hier die ehrliche Bilanz.

---

## Hierarchie der Beweisarten (von stark nach schwach)

1. **Printf-Format-Debug-String im Code referenziert** ←  Stärkster Beweis. Solche Strings
   (`"Arno    %d %d %d %d %d %d %d %d"`) werden direkt aus Code-Sites gepusht und können
   per Cross-Reference zur Funktion zurückverfolgt werden.
2. **MSVC-LCG-Konstanten an Code-Site** — beweist dass dort `rand()` aufgerufen wird,
   sagt aber nichts über das Puzzle.
3. **MHK-Datei-Name als String** — beweist nur dass irgendein Code-Segment diese Datei
   lädt; sagt nichts über Code-Logik.
4. **DS-Offset-Korrelation** — Indiz, kein Beweis. Globale Variablen können von mehreren
   Funktionen geteilt werden.
5. **Funktions-Pseudo-Code aus Disassembly** — Interpretation, nicht Beweis. Kann an
   Stellen mit komplexer Indirektion (Segment-fixups, Far-Calls) trügerisch werden.

---

## Pro-Puzzle-Bewertung der harten Code-Marker

| Puzzle | MHK | Vermutetes Segment | **Harter Debug-String?** | Konfidenz |
|--------|-----|--------------------|--------------------------|-----------|
| Pizza-Trolle | PIZZA | 37 | ✅ `"Arno %d %d %d %d %d %d %d %d"`, `"Willa..."`, `"Shyler..."`, `"Meal..."` | **HOCH** |
| Allergic Cliffs | BRIDGE | 28 | ✅ `"Upper bridge accepts:"`, `"Lower bridge accepts:"` | **MITTEL**¹ |
| Stone Cold Caves | CAVES | 24 | ✅ `"Hieroglyphs"` | **HOCH** |
| Captain Cajun (Ferry) | FERRY | 41 + 45 | ✅ `"Play FrogMan SCRB id:"` | **HOCH** |
| Lion's Lair | TUNNELS | 48 | ✅ `"Left / Bottom accept:"`, `"Right accepts:"` | **HOCH** |
| Mirror Machine (Smoke) | SMOKE | 42 | ✅ `"Cheat on"` (schwach — generischer Debug) | **MITTEL** |
| **Hotel Dimensia** | HOTEL | 28 (geteilt mit Cliffs?) | ❌ **kein Code-Marker** — nur UI-Strings | **NIEDRIG** |
| **Fleens** | FLEENS | 27 | ❌ **kein Code-Marker** — nur UI-Strings | **NIEDRIG** |
| **Mudball Wall** | NET | 35 | ❌ **kein Code-Marker** — nur "Net.MHK" Filename + UI | **NIEDRIG** |
| **Tattooed Toads** | LILLY | 30 | ❌ **kein Code-Marker** — nur "Lilly.MHK" + UI | **NIEDRIG** |
| **Bubblewonder Abyss** | MAZE2 | ? | ❌ **kein Code-Marker** | **NIEDRIG** |
| **Stone Rise** | SLIDES | ? | ❌ **kein Code-Marker** | **NIEDRIG** |

¹ Cliffs hat zwar harte Strings, aber die **Funktions-Identifikation innerhalb von Seg 28**
ist unzuverlässig (siehe `ALLERGIC_CLIFFS_KORREKTUR.md`).

---

## Konkrete Folgerungen pro Doku

### 🟢 Wahrscheinlich grundsätzlich korrekt (harte Marker, einfache Logik)

- `STONE_COLD_CAVES.md` / `STONE_COLD_CAVES_DEEP.md` — Marker `"Hieroglyphs"` ist eindeutig.
  Aber: kein einziger Solver-Test bisher gegen reales Spielverhalten. Empfohlene
  Validierungs-Stichprobe.
- `PIZZA_TROLLS.md` — Marker `"Arno"`/`"Willa"`/`"Shyler"` sind printf-Format-Strings
  der Debug-Ausgabe. Wahrscheinlich korrekt lokalisiert. **Aber** v2-Vergleich zeigt
  56 zusätzliche SCRBs (siehe v2-Vergleich).
- `FERRYBOAT.md` / `FERRYBOAT_DEEP.md` — Marker `"Play FrogMan SCRB id:"` eindeutig.
- `LIONS_LAIR.md` — Marker `"Left / Bottom accept:"` etc. eindeutig.

### 🟡 Lokalisierung wahrscheinlich korrekt, **aber Funktionszuordnung im Segment ungeprüft**

- `ALLERGIC_CLIFFS.md` / `ALLERGIC_CLIFFS_VERIFIED.md` — siehe Korrektur. Segment 28
  enthält Cliff-Strings, aber 0x238B0 (mit `num_slots=25`/`125`) gehört wahrscheinlich
  zu **Hotel Dimensia**. Innerhalb desselben Segments. Welche Funktion welche ist:
  unklar.
- `MIRROR_MACHINE.md` — Marker `"Cheat on"` ist generisch. Lokalisierung Seg 42
  möglicherweise korrekt, aber nicht stark belegt.

### 🔴 **Kein Code-Marker** — Lokalisierung beruht nur auf MHK-Dateinamen oder DS-Offsets

- `HOTEL_DIMENSIA.md` — Es gibt **keinen** printf/Debug-String der Hotel-Code identifiziert.
  Die Behauptung "Seg 28 (geteilt)" ist eine Vermutung, basiert auf den 5×5/125-Slot-Hinweisen.
- `FLEENS.md` / `FLEENS_DEEP.md` — Seg 27 wird nur durch indirekte Indizien zugeordnet.
- `LILLY_TOADS.md` / `LILLY_TOADS_DEEP.md` — Seg 30 nur durch `"Lilly.MHK"` Filename.
  Kein Debug-Marker im Code.
- `MUDBALL_WALL.md` / `MUDBALL_WALL_DEEP.md` — Seg 35 nur durch `"Net.MHK"`. Kein
  Debug-Marker im Code.
- `BUBBLEWONDER_ABYSS.md` — Segment-Lokalisierung in `SEGMENT_MAP.md` nicht einmal
  vollständig (Tabelle hat keine Zeile für Bubblewonder!).
- `STONE_RISE.md` — ebenso.

---

## Wiederkehrende Schwachstellen, die ich in mindestens drei Dokus eingebaut habe

1. **„VERIFIED" / „DEEP" im Dateinamen ohne reproduzierbare Verifikation.**
   Diese Suffixe sind Marketing. Tatsächlich verifiziert sind nur die LCG-Konstanten und
   einige printf-Strings. Pseudo-Code aus Disassembly ist Interpretation, nicht Verifikation.

2. **„Bedingungslos ausgeführt" / „nirgends gesetzt" / „immer N" — als Tatsachen formuliert.**
   Solche Aussagen verlangen vollständige Funktions-Coverage und Cross-Reference. Ohne
   die Engine-Aufruftabelle (Dispatch von Puzzle zu Init-Funktion) sind sie Vermutungen.
   Beispiel: `first_time_flag` in Cliff-Doku — als „nirgends gesetzt" deklariert, war
   tatsächlich gesetzt — aber in einer Funktion die wahrscheinlich gar nicht zu Cliffs gehört.

3. **Indirekte Indizien als Identifikation behandelt.**
   „Segment X lädt MHK Y → Segment X **ist** das Y-Puzzle" — falsch. Das Lade-Segment
   kann ein Wrapper, Loader, oder Pre-Cache sein.

4. **Pseudo-Code mit C-Syntax suggeriert Bytegenauigkeit.**
   Ein Pseudo-Code-Block wie
   ```c
   if (variety[attr_type_1] == 5 && variety[attr_type_2] == 5 ...)
   ```
   sieht nach exakter Übersetzung aus, ist aber tatsächlich Interpretation der Sprung-Struktur.
   Kontroll-Fluss-Rekonstruktion aus Assembly ist fehleranfällig (insbesondere bei
   Compiler-Optimierungen wie Sprung-Tabellen, gemeinsamen Tails, signed/unsigned-Vergleichen).

5. **Konstanten ohne Fundnachweis.**
   „Der LCG-Multiplikator 214013 liegt bei Offset 0xF857" — der Multiplikator wurde
   tatsächlich nicht als Literal gefunden (nur die Add-Konstante). Das hätte deklariert sein müssen.

---

## Konkrete fragwürdige Stellen — Liste für Re-Verifikation

| Datei | Zeile/Stelle | Problem |
|-------|--------------|---------|
| `ALLERGIC_CLIFFS_VERIFIED.md` | "memset … BEDINGUNGSLOS" | Disassembly nicht eindeutig — Sprungziel falsch interpretiert |
| `ALLERGIC_CLIFFS_VERIFIED.md` | "Such-Pfad: Spalte 4 des 5×5-Gitters" | Code liest Indizes 4..8 konsekutiv, **nicht** Spalte 4 |
| `ALLERGIC_CLIFFS_VERIFIED.md` | "first_time_flag nirgends gesetzt" | Tatsächlich gesetzt bei 0x238D1 — aber in Hotel-Kontext |
| `SEGMENT_MAP.md` | "Hotel Dimensia: Segment 28 (geteilt!) WAHRSCHEINLICH" | "Geteilt" ist Vermutung, kein Beweis |
| `SEGMENT_MAP.md` | "Net-Puzzle (Caves?)" | Sogar die SEGMENT_MAP weiß selbst nicht was Mudball ist |
| Alle `*_DEEP.md` | Funktions-Listen mit C-Pseudo-Code | Kontroll-Fluss-Annahmen ungetestet |
| `*_DEEP.md` Funktionsnamen wie `EvalAdjacencySeg45` | erfunden, nicht aus Symbol-Tabelle | Beschreibend, nicht offiziell |

---

## Was tatsächlich belastbar ist

1. **MSVC-LCG bei Seg 14 / Datei-Offset 0xF800** — Add-Konstante 2531011 direkt gefunden.
   Multiplikator 214013 nicht als Literal gefunden, aber Mechanik (high16, mod) ist
   Standard-MSVC.
2. **Mohawk-Engine Ressourcen-Format** — Format ist über ScummVM dokumentiert und durch
   `mohawk_parser.py` praktisch verifiziert (4055 Ressourcen wurden korrekt extrahiert
   und bytegenau gegen v2 verglichen).
3. **MHK-Inhalte** — was in welchem Archiv liegt, ist verifiziert.
4. **Bitmap-Format (LZ77 + RLE8)** — funktioniert in der Praxis (Bitmaps werden korrekt dekodiert).
5. **Zoombini-Attribut-Struktur (4 Merkmale × 5 Werte)** — durch Bitmap-Inhalte und
   Spielmechanik verifiziert.
6. **6 Puzzles mit harten Debug-String-Markern** — Segment-Zuordnung wahrscheinlich korrekt,
   einzelne Funktions-Zuordnung unsicher.

---

## Was ich künftig anders machen würde

1. **Keine "VERIFIED"-Dokus ohne reproduzierbare Test-Skripte.** Stattdessen Test-Tools
   die die Behauptung gegen das laufende Spiel prüfen.
2. **Behauptungen mit Konfidenz-Tags markieren.** „BESTÄTIGT (Marker: <string>)" vs.
   „VERMUTET (Indiz: <indikator>)" vs. „SPEKULATION".
3. **Dispatch-Tabelle der Engine zuerst lokalisieren.** Wenn man weiß welche
   Funktion-Liste vom Engine pro Puzzle-Eintrag aufgerufen wird, sind alle Funktions-
   Zuordnungen automatisch belastbar. Ohne diese Tabelle ist alles Vermutung.
4. **Empirischer Solver vor Disassembly-Solver.** Ein Solver der durch Beobachten lernt
   ist gegen Doku-Fehler robust. Disassembly-Solver brechen unbemerkt wenn die Doku falsch ist.

---

## Konkrete Empfehlung

Behandle die Reverse-Engineering-Dokus als **Hypothesen, nicht als Fakten**. Vor jedem
Solver-Einsatz: empirisch testen.

Solver-Schreiber sollten nicht aus den Dokus arbeiten, sondern direkt aus der Spiel-
Beobachtung. Die Dokus sind nützlich als Orientierung, aber nicht als Spezifikation.
