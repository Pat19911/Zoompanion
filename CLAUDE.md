# CLAUDE.md — Projektregeln für Zoombinis Reverse-Engineering

> **WICHTIG:** Dieses Dokument ist nicht optional. Es entstand aus einer langen Reihe von
> Fehlern, die der User immer wieder einzeln aufdecken musste. Jede Regel hier hat einen
> konkreten historischen Anlass. Bevor du arbeitest: lies, verstehe, halte dich daran.

---

## Was dieses Projekt ist

Reverse-Engineering von **Logical Journey of the Zoombinis** (Brøderbund/TERC, 1996),
einem 16-bit Windows-3.1-Spiel auf der Mohawk-Engine. Ziel: die Puzzle-Algorithmen so
genau verstehen, dass ein Solver gegen das laufende Spiel arbeiten kann (Memory-Reading
während die EXE läuft).

Es gibt eine zweite Version (Riverdeep 2001/2002, Win32 PE), aber die Algorithmen sind
gleich. Reverse-Engineering passiert hauptsächlich an v1 (`ZOOMBINI._EX`, 939536 Bytes,
NE-Format).

---

## Was du NIE tun darfst

Diese Regeln sind das Herzstück. Verstöße haben Stunden Doku-Arbeit erzeugt.

### 1. NIE „verifiziert" / „bestätigt" sagen ohne vollständige Funktion gelesen zu haben

Eine Funktion = Prolog bis Epilog (`retf` oder `retf N`). Nicht „die ersten 30 Zeilen sahen
plausibel aus". Nicht „das Pattern matcht meine Hypothese". **Vollständig.**

**Konkretes Beispiel:** Bei der Allergic-Cliffs-Analyse habe ich FUNC #11 (file 0x18231)
als „Allergie-Check" deklariert, weil die ersten 30 Zeilen einen Match-Loop enthielten.
Tatsächlich war es eine Sprite-Helper-Funktion. Der echte Allergie-Check lag in der
Init-Funktion — komplett anderer Code-Pfad.

### 2. NIE Manual-Wörter wörtlich übersetzen und als Algorithmus verkaufen

Das deutsche Manual sagt „Schnittmenge zweier Merkmalstypen". Das wirkt wie mathematisches
UND. Im Code ist es aber **ODER über zwei Nibbles**. Manual-Texte sind Marketing, nicht
Spezifikation.

### 3. NIE „Generierung" und „Selektion" trennen

Wenn der Code 500 Bitmasks erzeugt, frag dich: **wozu?** Eine zufällige Allergie braucht
keinen 500-Element-Pool. Wenn du nach dem Generation-Loop aufhörst, übersiehst du den
Selection-Loop. **Beim Cliff-Spiel war genau das der ganze Witz**: das Spiel optimiert
explizit auf 50/50-Verteilung durch Auswahl aus dem Pool.

### 4. NIE Funktionen ohne harten Code-Marker einem Puzzle zuordnen

Harte Marker:
- printf-Format-String der vom Code-Segment gepusht wird (`push 0x9A6 → "Upper bridge accepts:"`)
- MHK-Filename-String der vom Code-Segment gepusht wird (`push 0xD3D → "Caves.MHK"`)

Weiche Indizien (NICHT ausreichend):
- DS-Offset-Korrelation
- „macht Sinn weil es nach 5×5-Gitter aussieht"
- Funktionsname in vorhandener Doku

**Konkretes Beispiel:** SEGMENT_MAP.md behauptete „Cliffs in Seg 28 — BESTÄTIGT". Cross-
Reference auf MHK-Filenames zeigte: Hotel.MHK wird von Seg 28 geladen. Cliffs sind in
Seg 23. Die ganze Cliff-Analyse-Doku analysierte tatsächlich Hotel.

### 5. NIE Pseudo-Code in C als Beweis behandeln

Ein C-Pseudo-Code-Block sieht nach Bytegenauigkeit aus, ist aber Interpretation des
Kontroll-Flusses. Compiler-Optimierungen (Sprung-Tabellen, gemeinsame Tails, signed/
unsigned-Vergleiche, Inlining) machen Disasm fehleranfällig.

Wenn du Pseudo-Code schreibst: Markier ihn als **Interpretation** und gib die zugrunde
liegenden Adressen + Bytes mit an. Niemals als „so funktioniert das" verkaufen.

### 6. NIE Segment-Boundaries raten

Aus dem NE-Header lesen. **Immer.**
```python
ne_off = struct.unpack_from("<I", V1, 0x3C)[0]
seg_tbl = ne_off + struct.unpack_from("<H", V1, ne_off + 0x22)[0]
align = 1 << struct.unpack_from("<H", V1, ne_off + 0x32)[0]
sector, length, flags, _ = struct.unpack_from("<HHHH", V1, seg_tbl + (seg_num-1)*8)
file_offset = sector * align
```

**Konkretes Beispiel:** Bei der Cliff-Suche habe ich „Seg 20 = file 0x16C00..0x18C00"
geraten. Tatsächlich liegt Seg 20 bei 0x10A00..0x117FF. Was ich als „Seg 20" disassembliert
habe, war Seg 23. Ich verlor zwei Iterationen, bevor ich das bemerkte.

### 7. NIE Funktions-Prologe als nur `push bp` definieren

NE-Format hat einen 4-Byte-Standard-Prolog: `mov ax, ds; nop; inc bp; push bp`. Wenn du
nur `55 8B EC` (push bp; mov bp, sp) suchst, **findest du Funktions-Starts NICHT**.
Echte Cross-References zu near-calls landen 4 Bytes vor `push bp`.

### 8. NIE „Marketing-Suffixe" wie VERIFIED oder DEEP verwenden

Es gab in diesem Projekt 6 Doku-Files mit `_VERIFIED` oder `_DEEP` im Namen. Stichprobe
zeigte: Marketing. Tatsächliche Verifikation gab es nur für LCG-Konstanten und ein paar
printf-Strings. Wenn du Konfidenz signalisieren willst, **schreib es in den Text** mit
Belegen, nicht in den Dateinamen.

### 9. NIE Skripte falsch benennen, wenn das untersuchte Segment in Wirklichkeit etwas anderes ist

`disasm_mudwall*.py` zielten auf seg34 = Bubblewonder Abyss (nicht Mudball Wall = seg35).
Sechs Skripte wurden so falsch betitelt. Wenn du ein Skript schreibst, **nimm den Namen
aus dem MHK-Marker**, nicht aus deiner Vermutung.

### 10. NIE die Antwort zu schnell für „fertig" erklären, sobald sie zur User-Beobachtung passt

User-Beobachtung „passt" zu deiner Hypothese ist **kein Beweis**. Es heißt nur „nicht
widerlegt". Bevor du fertig deklarierst:
- Hat eine **andere** Hypothese die gleiche Vorhersage?
- Wenn ja: kannst du im Code unterscheiden welche stimmt?

**Konkretes Beispiel:** Bei „warum sind Diff 3 Cliffs gleichverteilt" habe ich als erste
Erklärung `1 - (4/5)³ ≈ 49 %` mathematisch geliefert. Das passt zur User-Beobachtung —
also fertig? **Nein.** Eine andere Hypothese (Spiel optimiert aktiv auf 50/50 Verteilung
via Selection aus 500er Pool) macht die GLEICHE Vorhersage. Im Code unterscheidbar.
Erst der User-Push „werden Kombinationen getargetet?" hat mich gezwungen weiterzulesen.

### 11. NIE Memory-Marker durch Pattern-Matching auf Dumps suchen, wenn du auch disassemblen kannst

Wenn du einen Memory-Marker (Flag-Byte, Index-Tabelle, Heap-Pointer) suchst der eine
bestimmte Spielmechanik markiert: **disassemble das Binary**, finde den Code der ihn
schreibt, lies die Adresse direkt ab. Versuche **nicht**, den Marker durch
„welche Bytes unterscheiden sich zwischen dump A und dump B" oder „welche Bytes haben
3 Fleens und 13 nicht" zu erraten — das findet zufällige Korrelationen, keine echten
Marker, und führt zu instabilen Heuristiken die in der nächsten Runde brechen.

**Konkretes Beispiel (Fleens-Session 2026-04-28):** Für „welche 3 Fleens sitzen auf dem
Baum" habe ich stundenlang Hex-Dumps verglichen, mehrere falsche Marker-Hypothesen
gebaut (`+0x128`, `+0x141 + 0x179`, …), die jedes Mal in der nächsten Runde brachen.
Sobald ich endlich `Zoombinis Logical Journey.exe` mit Capstone disassembliert habe,
fand ich in 5 Minuten:
- Die Setup-Funktion bei `0x00413B82` mit `cmp eax, [0x495C18]` (= Special-Index)
- Die game-internal ZB-Datenstruktur via `[0x4A2818] + 0xB83C + di*0x14`
- Die exakte Logik wie Specials auf ZBs zeigen (1-basierte Loop-Index)

Daraus eine robuste Implementierung: lese Specials → dereferenziere Heap-Pointer →
lese ZB-Attribute → leite Match-Fleen via Permutation ab. Funktioniert in jeder Runde,
unabhängig von Drag-State, Animations-Pulsation oder Walk-Order.

**Regel:** Wenn nach 1-2 Pattern-Matching-Versuchen kein konsistenter Marker gefunden
ist, sofort umsteigen auf Disassembly. Nicht weiter Hex-Dumps durchwühlen.

```python
# Tool-Setup für PE32-Binaries (v2):
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
data = open(EXE_PATH, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True
# 1. Suche nach Referenzen auf bekannte VA: data.find(struct.pack("<I", 0x00495C18))
# 2. Disassemble um die Hits herum: md.disasm(data[file_off-0x40 : file_off+0x100], file_va-0x40)
# 3. Folge der Logik. Heap-Pointer bei `mov eax, dword ptr [VA]` lesen → dereferenzieren.
```

Für NE-Binaries (v1) gilt das gleiche, dort schon vorhanden in `ne_loader.py`.

---

## Was du IMMER tun musst

### Vor jeder Code-Analyse

1. **Vollständigen Code lesen, nicht Stichprobe.** Init-Funktion = von Prolog bis Epilog.
   Kontrollfluss komplett verfolgen, alle bedingten Sprünge nachverfolgen.
2. **Alle Aufrufer einer Funktion finden** über die Relocation-Tabelle. Eine Funktion
   ohne Aufrufer ist tot oder du hast den falschen Einstieg gefunden.
3. **Sub-Calls verfolgen.** Eine Funktion macht 13 lcalls — wozu? Bevor du sie als
   „Eval-Handler" deklarierst, solltest du wissen was die 13 Sub-Calls tun.

### Vor jeder Behauptung über Spielmechanik

1. **Hypothese explizit aussprechen** mit konkreten Vorhersagen (z.B. „bei 16 Zoombinis
   erwarte ich Match-Rate ≈ X %").
2. **Eine alternative Hypothese formulieren**, die die gleichen Beobachtungen erklären
   könnte. Beide gegen den Code prüfen.
3. **Wenn Code und Beobachtung übereinstimmen, prüf eine letzte Runde**: was kann ich
   übersehen haben? Pool-Generation ohne Selection? Validation-Loops? Re-Roll-Pfade?

### Beim Schreiben von Doku

1. **Quellen mitliefern.** „Funktion bei file 0x1751E (= Seg 23, Offset 0x131E), 11
   Funktionen in Seg 23 identifiziert über Prolog-Scan." Nicht „die Init-Funktion".
2. **Konfidenz markieren.** „Verifiziert durch printf-Marker im selben Segment" vs.
   „vermutet basierend auf DS-Offset-Korrelation". Verschiedene Stufen, klar getrennt.
3. **Was du NICHT weißt sagen.** „Diff-1 Spezialpfad bei 0x17E55 nicht analysiert."
   Lieber explizit Lücken benennen als Pseudo-Vollständigkeit suggerieren.

---

## Codebase-Hygiene

> **Anlass dieser Sektion:** Stand 2026-04-25 hatte das Projekt 43 Python-Skripte (davon
> 27 Wegwerf-Iterationen wie `disasm_bubblewonder.py` v1/v2/v3 und 8 × `hotel_init_verify*.py`),
> 44 Markdown-Dateien (davon 11 Duplikate mit `_DEEP`/`_VERIFIED`/`_NEW`/`_OLD` Suffix),
> kein Git-Repository, keine Tests, keine `requirements.txt`, hardcoded `/home/<user>/...`-
> Pfade in Skripten. Ein erfahrener Reviewer würde das in 5 Minuten als nicht-mergeable
> einstufen. **Diese Sektion verhindert Wiederholung.**

### Hard rules

1. **Kein Commit ohne Git-Repo.** Bevor du ein neues Skript anlegst oder eine Datei
   bearbeitest: `git status` ausführen. Wenn nicht initialisiert: STOP und initialisieren.
2. **Wegwerf-Skripte gehören nach `scratch/`.** Wenn ein Skript zur einmaligen Untersuchung
   ist (z. B. „prüfe ob Funktion X bei Adresse Y aufgerufen wird"), dann sofort dort anlegen,
   nicht im Wurzelverzeichnis. Wurzel = wiederverwendbare Tools.
3. **Nach jeder Untersuchungs-Session: konsolidieren.** Erkenntnis → in die kanonische
   `analysis/PUZZLE.md` schreiben. Skript löschen oder nach `scratch/` verschieben.
   Diese Regel ist die wichtigste — sie stoppt den Wildwuchs an der Quelle.
4. **Eine Doku-Datei pro Puzzle, kanonischer Name ohne Suffix.**
   `analysis/HOTEL_DIMENSIA.md` ist die einzige Wahrheit für Hotel. Kein `_DEEP`,
   `_VERIFIED`, `_NEW`, `_OLD`, `_NOTES`, `_HARD_MODES`. Wenn du eine zweite Datei
   schreiben willst weil die alte „obsolet" ist: alte Datei nach `analysis/_archive/`
   verschieben, neue Datei mit dem kanonischen Namen erstellen.
5. **NIE iterativ verschieben** (`script.py` → `script2.py` → `script3.py`). Wenn du
   eine bessere Version brauchst: editiere das Original, bevor du `Ctrl+Z` drückst, hast
   du den git-Stand. Versionierung ist Aufgabe von git, nicht von Dateinamen.
6. **Pfade sind Variablen, nicht Konstanten.** Kein `V1_PATH = "/home/<user>/..."` in
   einem Skript. Stattdessen: `from ne_loader import NEBinary; ne = NEBinary.default()` —
   das nutzt `$ZOOMBINI_BIN` mit Default. Wenn du das umgehst, brichst du Reproduzierbarkeit
   für jeden zweiten Entwickler.
7. **Gemeinsame Logik in Library, nicht copy-paste.** NE-Header-Parsing, Prolog-Scan,
   Segment-Bounds, Capstone-Setup gibt es in `ne_loader.py`. Wenn du diese Logik
   reproduzierst, machst du das Projekt schwerer zu warten. Erweitere `ne_loader.py`,
   schreib keinen neuen Wrapper.
8. **Konfidenz im Code markieren.** Wenn du eine neue Memory-Adresse oder
   Decoding-Annahme einbaust ohne Live-Validierung, markier sie als „Probable"
   oder „Speculative" im Code-Kommentar oder Doku-Eintrag. Sonst suggeriert
   dein Code dem Endnutzer „verifiziert", obwohl es geraten ist.

### Cleanup-Cadence — wann aufgeräumt werden muss

| Auslöser | Aktion |
|----------|--------|
| Nach jeder Investigations-Session | Erkenntnisse in kanonische Doku übertragen, Skripte verschieben/löschen, `git commit` |
| Wenn `*.py` in Wurzel > 15 Dateien | Hygiene-Audit erzwungen: was davon ist wiederverwendbar? Rest nach `scratch/` |
| Wenn `analysis/*.md` Duplikate (gleiches Puzzle 2× mit Suffix) | Sofort: ältere/kleinere Datei nach `_archive/`, neue auf kanonischen Namen |
| Wenn ein Skript hardcoded `/home/<user>/...` enthält | Sofort: `ne_loader.NEBinary.default()` einsetzen |
| Vor jeder neuen Doku-Datei | Frage: gibt es schon eine Datei für dieses Thema? Wenn ja: editieren, nicht zweite erstellen |

### Audit-Befehle (sollten regelmäßig grün sein)

```bash
# Wurzel hat keine Wegwerf-Skripte
ls *.py | grep -E '(_v[0-9]|_scratch|_tmp|_old|_new|_deep|_temp\.)' && echo "FAIL" || echo "OK"

# Keine Marketing-Suffixe in analysis/
ls analysis/ | grep -iE '_(verified|deep|new|old|notes|deprecated)\.md$' && echo "FAIL" || echo "OK"

# Keine doppelten Puzzle-Docs
for puzzle in HOTEL ALLERGIC BUBBLEWONDER FLEENS LIONS MIRROR MUDBALL PIZZA STONE_COLD STONE_RISE TITANIC CAPTAIN; do
  count=$(ls analysis/${puzzle}*.md 2>/dev/null | wc -l)
  [ "$count" -gt 1 ] && echo "FAIL: $puzzle has $count files"
done

# Tests laufen
python3 tests/test_ne_loader.py

# Build sauber
dotnet build --nologo --verbosity quiet
```

Wenn auch nur **eines** rot ist: keine neuen Features, erst Hygiene.

### Beispiel: was diese Session hätte besser machen sollen

Während der Hotel-Disassembly habe ich `hotel_init_verify.py`, dann v2, v3, v4, v5,
dann `hotel_open_items.py`, `hotel_3d_validator.py`, `hotel_action3.py`,
`hotel_callers_verify.py` v1+v2, `hotel_helpers_verify.py`, `hotel_zb_pointer.py`
geschrieben — **12 Skripte für eine einzige Untersuchung**.

Korrekt wäre gewesen:
1. `scratch/hotel_investigation.py` anlegen
2. Iterativ erweitern (kein v2, kein v3 — editieren!)
3. Erkenntnisse während der Untersuchung in `analysis/HOTEL_DIMENSIA.md` schreiben
4. Am Ende der Session: `git rm scratch/hotel_investigation.py` (Erkenntnisse sind ja in der Doku)

---

## Domain-Wissen — was im Binary steckt

### Format
- **NE (New Executable)**, 16-bit Windows 3.1
- **191 Segmente** (62 Code, 129 Daten)
- Segmentierte Adressierung mit Relocations für far calls
- Alignment-Shift = 9 (alle file-Offsets × 512)

### Verifizierte Code-Lokalisierungen (siehe `analysis/SEGMENT_MAP.md` und `_SEGMENT_MAP_KORREKTUR.md`)

| Puzzle | MHK | Segment | File-Offset | Beweis |
|--------|-----|---------|-------------|--------|
| Pizza-Trolle | Pizza.MHK | 37 | 0x4E000 | Arno/Willa/Shyler printf + Pizza.MHK push |
| Stone Cold Caves | Caves.MHK | 24 | 0x18C00 | Caves.MHK push |
| Fleens | Fleens.MHK | 27 | 0x1FE00 | Fleens.MHK push |
| Tattooed Toads | Lilly.MHK | 30 | 0x2A600 | Lilly.MHK push |
| Mudball Wall | Net.MHK | 35 | 0x47C00 | Net.MHK push |
| Mirror Machine | Smoke.MHK | 42 | 0x5D200 | Smoke.MHK push + "Cheat on" |
| Lion's Lair | Tunnels.MHK | 48 | 0x76400 | Tunnels.MHK push |
| Bubblewonder Abyss | Maze2.MHK | 34 | 0x3CE00 | Maze2.MHK push |
| Stone Rise | Slides.MHK | 45 | 0x6CA00 | Slides.MHK push |
| Hotel Dimensia | Hotel.MHK | 28 | 0x23800 | Hotel.MHK push |
| **Allergic Cliffs** | Bridge.MHK | **23** | 0x16200 | "Upper/Lower bridge accepts:" pushes |
| Captain Cajun (Ferry) | Ferry.MHK | 41 (?) | 0x58A00 | Ferry.MHK nur in Engine-seg55 referenziert — UNGEKLÄRT |

### Engine-Segmente
- **Seg 14** (file 0xF800): `rand()` MSVC-LCG. Add-Konstante `2531011` (0x269EC3) verifiziert.
  Multiplier `214013` nicht als Literal gefunden (vermutlich inlined).
- **Seg 20** (file 0x10A00): `random_range(min, max)` bei seg20:0x04E8 — Wrapper um rand().
- **Seg 18** (file 0x10600): generischer MHK-Lader.
- **Seg 44** (file 0x65E00): Engine-Hotspots/Features (von allen Puzzles aufgerufen).
- **Seg 50, 51, 53, 55, 59**: Engine-Core (Sprites, Animation, Sound, Resource).

### Mohawk-Format (MHK-Archive)
- Magic: `MHWK` + `RSRC`
- Resource-Typen: `tBMP` (Bitmaps mit LZ77+RLE8), `SHPL` (Paletten 256× 4B), `SCRB`
  (Hotspot-Scripts), `SCRS` (Snoid-Animation), `SND` (WAVE), `tPAL`, `tMID`, `NODE`,
  `PATH`, `STRL` (nur v2: Help-Strings).
- Parser: `mohawk_parser.py` (funktioniert auf v1 und v2).

### Zoombini-Attribute
- 4 Merkmale × 5 Varianten = 625 Kombinationen.
- Hair (Spiked/Ponytail/Green Cap/Straight/Balding)
- Eyes (Brown/One-eye/Sleepy/Spectacles/Sunglasses)
- Nose (Green/Orange/Red/Purple/Blue)
- Feet (Shoes/Skates/Wheels/Propeller/Springs)
- Cliff-interne TYPE-Kodierung ist 1-basiert: 1=Hair, 2=Eyes, 3=Nose, 4=Feet.

---

## Wichtige Dateien & Verzeichnisse

```
analysis/
  SEGMENT_MAP.md                       ← authoritative Segment-Zuordnung
  _SEGMENT_MAP_KORREKTUR.md            ← Methodik & Korrekturen
  _TRUST_AUDIT.md                      ← Konfidenz-Bewertung aller Dokus (single source of truth)
  _DS_VARIABLES.md                     ← DS-Karte aller Puzzles
  _EVAL_FUNCTIONS.md                   ← Eval-Funktionen pro Puzzle
  _MECHANIK_PATTERNS.md                ← Pattern-Klassifikation A/B/C/D
  _PUZZLE_OVERVIEW.md                  ← Master-Übersicht
  V1_VS_V2_COMPARISON.md               ← v1/v2-Vergleich
  ALLERGIC_CLIFFS.md                   ← Cliff-Mechanik (Verified)
  HOTEL_DIMENSIA.md                    ← Hotel-Mechanik (Probable)
  PIZZA_PASS.md / CAPTAIN_CAJUN.md / STONE_COLD_CAVES.md / LIONS_LAIR.md
  MIRROR_MACHINE.md / BUBBLEWONDER.md / FLEENS.md / MUDBALL_WALL.md
  STONE_RISE.md / TITANIC_TOADS.md
  _archive/                            ← obsolete/duplikate Dokus (NIE neue dort anlegen)

ZoombiniHelper.csproj                  ← App (net8.0-windows, WinForms)
  Pe32ProcessMemory.cs                 ← Win32 ReadProcessMemory-Wrapper
  Pe32GameState.cs                     ← IMemoryReader-Impl (Win32-backed)
  Pe32PuzzleDetector.cs                ← Detect/VerifyBinaryIdentity/DumpFullData
  CliffOverlay.cs                      ← WinForms-Overlay (UI, F12-Diagnose, %TEMP%-Log)
  Program.cs                           ← Entry-Point

src/ZoombiniHelper.Core/               ← Domain-Library (net8.0, ohne Win32)
  IMemoryReader.cs                     ← Memory-Read-Abstraktion (für Tests)
  CliffMemoryMap.cs                    ← Konstanten für Cliff-VAs
  CliffState.cs                        ← Domain-Snapshot: Rules, Held, Bridge-Labels
  ZoombiniVariants.cs                  ← deutsche Anzeigenamen, Konfidenz-Hinweise

tests/ZoombiniHelper.Tests/            ← xUnit-Tests für Core (laufen auf Linux)

ne_loader.py                           ← Wiederverwendbare NE-Library — IMMER DAS hier nutzen
mohawk_parser.py                       ← MHK-Archive-Parser
bitmap_decoder.py                      ← tBMP-Decoder (LZ77+RLE8)
master_puzzle_analysis.py              ← Top-Level-Übersicht aller Puzzle
per_puzzle_analyzer.py                 ← Per-Puzzle Deep-Analyse
puzzle_analyzer.py                     ← Übersichts-Tool
puzzle_deep_template.py                ← Template für neue Puzzle-Analysen
eval_finder.py                         ← Action-Dispatcher-Signatur-Suche
locate_markers.py                      ← Cross-Reference zu printf/MHK-Strings
locate_mhk.py                          ← MHK-Lader-Identifikation
compare_resources.py                   ← v1/v2 MHK-Diff

scratch/                               ← Wegwerf-Iterationen, nicht für Reuse
tests/                                 ← Smoke-Tests (test_ne_loader.py)

v1_bin/ZOOMBINI._EX                    ← v1 NE-Binary (.gitignored, nur lokal)
v2_bin/                                ← v2 PE32+DLLs (.gitignored)
extracted/ v2_extracted/ v2_mhk/       ← Asset-Extraktionen (.gitignored)
```

---

## Tool-Setup

### Standard-Library für alle Disasm-Skripte: `ne_loader.py`

**Nutze IMMER `ne_loader.py`. NIE NE-Header-Parsing inline duplizieren.**

```python
from ne_loader import NEBinary, get_disasm

ne = NEBinary.default()                       # nutzt $ZOOMBINI_BIN oder default
seg_off, seg_len, flags = ne.segment_info(28) # Hotel-Segment
data = ne.segment_data(28)
prologs = ne.find_prologs(28)                 # NE 4-Byte-Prolog-Scan
relocs = ne.relocations(28)                   # alle Relocations
end = ne.function_bounds(28, 0x1CED)          # walk bis retf

md = get_disasm()                              # Capstone, 16-bit, detail=True
for ins in md.disasm(data, 0):
    ...
```

Wenn dir eine Funktion fehlt: **erweitere `ne_loader.py`**, schreib keinen
Inline-Workaround. (Verstoß = Verstoß gegen Hygiene-Regel #7.)

### v1-Binary mounten
```bash
sudo mount -o loop,ro "/home/<user>/Downloads/Logical Journey of the Zoombinis.iso" /tmp/zb_v1
# Binary nach v1_bin/ kopieren oder mit $ZOOMBINI_BIN auf /tmp/zb_v1/ZOOMBINI._EX zeigen
sudo umount /tmp/zb_v1 && rmdir /tmp/zb_v1
```

### Tests laufen
```bash
python3 tests/test_ne_loader.py     # Smoke-Tests (5 Stück)
dotnet build                         # C#-Build sauber halten
```

### Build & Distribution

Standard-Publish (self-contained Single-File-EXE für eine Windows-VM neben dem Spiel):

```bash
dotnet publish ZoombiniHelper.csproj -c Release -r win-x64 \
    --self-contained true -p:PublishSingleFile=true -o publish/ --nologo
```

Die fertige `publish/ZoombiniHelper.exe` dorthin kopieren, wo du sie verteilst
(z. B. ein von der Spiel-VM erreichbarer Sync-Ordner). Jeder Build ist eine
~70-MB-Single-File — nur die neueste behalten, um Platz/Sync-Quota zu schonen.
Lokale `ZoombiniHelper-*.exe` im Wurzelverzeichnis sind gitignored.

> Projekt-/personenspezifische Distributions-Details (konkreter Zielordner etc.)
> stehen in der gitignorierten `LOCAL_NOTES.md`, nicht hier.

Wann triggern:
- Nach jedem User-relevanten Code-Change (neuer Renderer, neue
  Memory-Map-Adresse, neue Solver-Logik)
- Vor einer Test-Session (User sagt "ich teste mal" → frische EXE)
- Auf User-Anfrage ("speichere die neue EXE")

Wann NICHT:
- Refactor ohne Verhaltens-Änderung
- Dokumentations-Updates
- Wenn der User explizit nur Tests laufen lässt

### Ghidra für tiefe Engine-Analyse — wenn adäquat

Wenn `pe_loader.py` an seine Grenzen stößt (z.B. bei vtable-Calls,
Cross-References über die ganze EXE, ungenauen Funktions-Boundaries oder
wenn man Decompiler-Output statt Disasm braucht): **Ghidra einsetzen**.

Headless-Setup ist installiert bei `/opt/ghidra/support/analyzeHeadless`.
Projekt-Verzeichnis: `/tmp/ghidra_zoombini` (oder permanent in `~/ghidra_proj/`).

```bash
# Erst-Import + Auto-Analysis (~10 min einmalig)
/opt/ghidra/support/analyzeHeadless /tmp/ghidra_zoombini ZoombiniProj \
    -import v2_bin/ZoombinisLJ.exe -overwrite

# Skript auf bereits importierter EXE laufen lassen
/opt/ghidra/support/analyzeHeadless /tmp/ghidra_zoombini ZoombiniProj \
    -process ZoombinisLJ.exe \
    -scriptPath scratch/ghidra_scripts \
    -postScript MeinSkript.py \
    -noanalysis
```

Skripte gehören nach `scratch/ghidra_scripts/`. Use cases:
- Cross-Reference-Suche über die ganze EXE (Pattern-Match auf ZB-Attr-Reads etc.)
- Decompiler-Output extrahieren (10× lesbarer als Disasm)
- Funktions-Klassifikation per Pattern (Generator/Validator/Match-Kandidat)

**Wann Ghidra benutzen:**
- Match-Logik versteckt sich in der Engine (vtable, indirect calls)
- Eine Funktion ist >2000 B und Disasm wird unleserlich
- Cross-Refs über mehrere Tausend Funktionen nötig
- Function-Boundaries sind heuristisch falsch (häufig bei Padding nach `ret`)

**Wann NICHT:**
- Einfache statische Adress-Suche (`pe.find_string`, `push_imm32_index` reichen)
- Kleine, lokale Funktionen die in 30 Disasm-Zeilen klar sind
- Wenn ein Memdump die Frage schneller beantwortet (z.B. "welcher Wert steht in VA X")

---

## Methodischer Workflow für eine neue Puzzle-Analyse

1. **MHK-Filename** im Binary suchen → **Code-Segment** über Push-Site identifizieren
2. **NE-Header lesen** → exakte Segment-Boundaries
3. **Funktions-Prologe scannen** (`mov ax, ds; nop; inc bp; push bp` = 4 Bytes vor `55 8B EC`)
4. **Relocations für dieses Segment lesen** → alle Sub-Calls / lcalls auflösen
5. **Init-Funktion finden** (üblicherweise die größte oder die mit den meisten Schreibzugriffen
   auf Spiel-State)
6. **Init komplett von Prolog bis Epilog disassemblieren** — keine Stichprobe
7. **Eval-Funktion finden** (üblicherweise mit Action-Dispatcher case 1/2/3)
8. **Datenflüsse zwischen Init und Eval verfolgen** über DS-Adressen
9. **Hypothese formulieren + Vorhersage** für Match-Rate / Verteilung
10. **Vorhersage gegen User-Beobachtung prüfen** — bei Diskrepanz: weitergraben, nicht
    Hypothese „zurechtbiegen"
11. **Alternative Hypothese formulieren** und versuchen sie zu widerlegen
12. **Erst dann** Doku schreiben — mit Quellen, Konfidenz-Markierungen und expliziten Lücken

---

## Spezifisches Beispiel: Wie die Cliff-Analyse hätte laufen müssen

Was tatsächlich passierte (über mehrere Tage und drei User-Interventionen):
1. Doku behauptete Cliffs in Seg 28 → falsch (war Hotel)
2. Cliff-Strings in Seg 20 entdeckt → fast richtig (eigentlich Seg 23)
3. FUNC #11 als Allergie-Check → falsch (Sprite-Helper)
4. seg44:0x162a als Match → falsch (Cliff-State-Lookup)
5. POST-Dispatch Match-Loop gefunden → endlich der echte Match
6. Match-Logik als „mathematisch ~49%" erklärt → unvollständig
7. Selection-Loop entdeckt → das Spiel optimiert aktiv auf 50/50

Was hätte passieren sollen:
1. MHK-Push für Bridge.MHK suchen → Seg 18 (engine), kein direkter Cliff-Loader
2. Debug-Strings „Upper/Lower bridge accepts:" suchen → Seg 23 push
3. **Nur durch printf-Match identifiziert: Cliffs in Seg 23**
4. NE-Header lesen → Seg 23 ist 8395 Bytes
5. Funktions-Prologe scannen → 11 Funktionen
6. Relocations auflösen → 7× random_range, 161 lcalls insgesamt
7. Größte Funktion (3351 B) als Init identifizieren
8. Init **komplett** disassemblieren — von Prolog (file 0x1751E) bis retf
9. Difficulty-Dispatch finden, alle 4 Branches lesen
10. **Pool-Generation in Diff-3-Branch erkennen** (4×125 = 500 Bitmasks)
11. **Selection-Logic NACH Generation lesen** — TARGET=N/2, Oszillation, Random aus Set
12. Hypothese: „Spiel optimiert auf 50/50" + Vorhersage „Diff 3 immer ungefähr 8 vs 8"
13. Doku schreiben mit Code-Belegen

Schritt 8–11 ist genau das, was ich übersprungen habe. **Mach es nicht so wie ich.**

---

## Wenn du unsicher bist

- Lies dieses Dokument nochmal.
- Lies `analysis/_TRUST_AUDIT.md` für Konfidenz-Bewertungen.
- Frag den User **bevor** du eine Behauptung machst, lieber als ihn die Korrektur erzwingen
  zu lassen.

Kosten einer falschen Behauptung > Kosten einer Rückfrage.
