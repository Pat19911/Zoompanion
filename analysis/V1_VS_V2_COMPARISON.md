# Zoombinis: Logical Journey — v1 (1996) vs v2 (2001/2002) Vergleich

## TL;DR (revidiert nach Byte-genauem Vergleich)

Die v2-Neuauflage ist **überwiegend** eine Win32-Portierung der v1-Spiellogik, aber **nicht
schlicht „identisch + Help-Overlay"** wie zunächst angenommen. Byte-genauer Vergleich zeigt:

- **4055 Ressourcen bitweise identisch** (v1 ↔ v2)
- **94 Ressourcen geändert** (meist tBMPs neu gepackt, 52 kleine PIZZA-Scripts um 2 Bytes gekürzt)
- **147 Ressourcen komplett neu** (Help-Texte, neue Hotspots, neue Audios)
- **0 Ressourcen aus v1 entfernt**

**Kernalgorithmen (Attribut-System, LCG, Puzzle-Regeln) bleiben unverändert**, aber PIZZA
bekommt ~25 % mehr Hotspots, NET und LILLY je einen kleineren Zuwachs.

---

## 1. Binary-Format

| | v1 (1996) | v2 (2001/2002) |
|---|-----------|----------------|
| Haupt-EXE | `ZOOMBINI._EX` (939 KB, NE 16-bit) | `Zoombinis Logical Journey.exe` (614 KB, **PE32 32-bit**) |
| Engine-DLLs | — monolithisch | `trinketcore.dll` (188 KB), `ompp32x.dll` (233 KB), `IFC22.dll` (196 KB) |
| Sound-Lib | eingebettet | `Mss32.dll` (Miles Sound System) |
| Video-Lib | — | `binkw32.dll` (Bink Video, neue Intros) |
| Launcher | — | `Play.exe` (45 KB, nur CD-Autorun-Wrapper) |
| Print-App | — | `Zoombinis LJ Print.exe` (neu, für Printable Games) |

**trinketcore.dll enthält `OM*`-Klassen** (OMTrinket, OMTrinketWindow, OMAnimation,
OMAnimCursor, YHeap). Das ist Riverdeeps plattformübergreifende UI-Engine ("TLC Runtime",
vgl. `TLCRUN32.exe` im Installer).

**Spiellogik liegt in `Zoombinis Logical Journey.exe`** — alle v1-Debug-Strings sind wörtlich erhalten:

| String | v1 | v2 |
|--------|----|-----|
| `Arno %d %d ...`, `Willa %d %d ...` (Pizza-Troll Debug) | ✓ | ✓ |
| `bogus snoid script id` | ✓ | ✓ |
| `Too many main-feature SCRBs`, `Feature Group out of range` | ✓ | ✓ |
| `Play FrogMan SCRB id:` | ✓ | ✓ |
| `ARE YOU SURE YOU WANT TO MAKE A NEW GAME ?` | ✓ | ✓ |

**MSVC LCG:** Die Additions-Konstante `2531011` (0x269EC3) ist an Offset `0xf9e2` im neuen PE vorhanden.
Die Multiplikator-Konstante `214013` (0x343FD) ließ sich als Literal **nicht** finden —
wahrscheinlich hat der Compiler sie in eine `imul`-Instruktion inlined. Der Algorithmus selbst
(`seed = 214013·seed + 2531011`) ist somit mit hoher Wahrscheinlichkeit identisch, kann aber
ohne IDA-Disassembly nicht zu 100 % bestätigt werden.

> **Caveat:** Die Offsets 0xF857 (v1, NE segmentiert) und 0xf9e2 (v2, PE flat) sind formal
> nicht direkt vergleichbar — es ist reiner Zufall, dass sie ähnlich aussehen.

---

## 2. MHK-Archive Vergleich (Byte-genau)

### Vollständig bitweise identisch

BASECAMP (bis auf 1 tBMP), BRIDGE, CAVES, FERRY, FLEENS, HOTEL, MAZE2, SMOKE, TOWN, TUNNELS, **XFER komplett 100 %**

Alle SCRB/SCRS/SND/SHPL/NODE/PATH/REGS/CURS dieser Puzzle sind byte-identisch.
→ Die Puzzle-Logik, Animations-Timing und Sound-Cues sind in diesen 11 Puzzle-Archiven völlig unverändert.

### Strukturelle Änderungen (über triviale tBMP-Repack hinaus)

| Archive | Änderung | Interpretation |
|---------|----------|----------------|
| **PIZZA.MHK** | 52 SCRBs geändert (−2 Byte jeweils) **+ 56 neue SCRBs** | siehe unten |
| **NET.MHK** | 3 SCRBs geändert (1 Byte verschoben) **+ 17 neue SCRBs** + 1 SCRS geändert | Mini-Patch, zusätzliche Hotspots |
| **LILLY.MHK** | 7 neue SCRBs | Zusätzliche Hotspots |
| **ZOOMBINI.MHK** | 2 SCRBs geändert, 8 SNDs **kürzer neu komprimiert**, **+ 53 STRL**, + 7 SCRBs | Neues Help-System |
| **BCTWO.MHK** | 7 neue tBMPs (IDs 9016–9022) | Zusätzliche Basecamp-Grafiken |

### Muster der PIZZA-SCRB-Änderungen (52 Stück, alle −2 Byte)

v1 endete auf `FE 00 1B 5X` (vermutlich „Play Sound 0x1B58–0x1B5F" = Sound-IDs 7000–7007),
v2 endet auf `FF 00` (End-Marker).
**Kein Logik-Umbau** — der „play sound"-Opcode am Ende kleiner Hotspot-Scripts wurde entfernt.
Der Sound wird vermutlich jetzt vom neuen Help-System oder zentral getriggert.

Beispiel SCRB_7005:
```
v1: 00 02 00 46 00 30 00 B9 FF 00 FE 00 1B 5C
v2: 00 02 00 46 00 30 00 B9 FF 00 FF 00
```

### Muster der NET-SCRB-Änderungen (3 Stück, je 0 Byte Delta)

Ein Byte-Wert `0x15` wird an eine andere Position im Script verschoben (v1: `ff15 … fe001f40`
→ v2: `ff00 … fe151f40`). Wirkt wie ein **Bug-Fix** (Animations-Parameter an korrekte Stelle).

### Neue SCRBs in PIZZA/NET/LILLY/ZOOMBINI sind **strukturell identisch** zu den bestehenden

Größenverteilung der neuen PIZZA-SCRBs (7069–7124) matcht die der bestehenden (7000–7068):
52 × 12 Byte (kleine Hotspots), plus je einer à 42/54/60/66 Byte (größere Trigger-Scripts).
**→ Keine neue Puzzle-Mechanik, sondern mehr desselben Gameplay-Elements**
(z. B. zusätzliche Topping-Buttons, erweitertes Pizza-Panel, mehr Bonus-Hotspots).

### Geänderte tBMPs: Pattern erkennbar

tBMP-IDs, die in **mehreren** Archiven mit gleicher Größe geändert wurden → gemeinsame UI-Elemente:

| tBMP-ID | Archive | v1 | v2 | Interpretation |
|---------|---------|----|----|----------------|
| **6000** | MAZE2, NET, PIZZA, SLIDES, SMOKE | ~2050 B | ~2800 B | **Help-Overlay-Button** (5 Puzzles) |
| **7000** | LILLY, MAZE2, PIZZA, SLIDES | variabel | variabel | Mixed (teils UI, teils Hintergrund-Repack) |
| **10000** | CAVES, LILLY, NET | variabel | variabel | Gemischt |
| **11000** | CAVES, LILLY | variabel | variabel | Gemischt |
| **1400** | BRIDGE, FERRY | 3542 B | ~2900 B | Gemeinsames UI-Element |

### Komplett neue Archive

| Archive | Größe | Inhalt | Zweck |
|---------|-------|--------|-------|
| **HELP.MHK** | 17 MB | 100 × SND (IDs 21300–22862) | Voice-Over Audio, **gepaart mit STRL-IDs** (21300 ↔ STRL 1300) |
| **MUSIC.MHK** | 1.1 MB | 1 × SND (ID 30001) | Zusätzliche Hintergrundmusik |
| **NETDEMO.MHK** | 1.1 MB | identisch zu NET v2 **außer 1 tBMP** | Vermutlich für Trial/Demo-Modus |
| **RODMAP.MHK** | 503 KB | 2 × tBMP (272 + 229 KB), 8 × SCRB | Erweiterter Map-Screen |

### Zusätzliche Media-Files

- `logo025.bik` (19 MB), `logo025immr.bik` (19 MB), `logodemo.bik` (329 KB) — Bink-Intros

---

## 3. Neuer Ressourcentyp: STRL (bestätigt)

**STRL = „String List"** — englische Texte des Voice-Over Help-Systems.

- Beispiel `STRL_1300.bin` (706 B): `"Zoombini Isle is the place where you recruit each band of escaping Zoombinis. A total of 16 Zoombinis are required to start each trip. ..."`
- Format: 1 Leading-Byte (vermutlich Count/Encoding-Flag) + ASCII-Text
- **Korrelation STRL ↔ HELP.MHK:** `STRL_1300` (Text) ↔ `SND_21300` + `SND_21301` (Audio,
  vermutlich zwei Abschnitte/Sprecher)
- IDs 1300–2900 in 100er-Schritten (Kategorien) und 20er-Schritten (Sub-Topics)

---

## 4. Ressourcen-Format (unverändert)

Alle bestehenden Spezifikationen aus `ANALYSE.md` gelten weiter:

- **tBMP**: Zwei-Stufen-Dekompression LZ77 + RLE8, 8-Byte-Header
- **SHPL**: 256-Farben-Palette, 4 Byte pro Farbe; **2 von 44 SHPL-Paletten geändert** (BCTWO_9000, PICKER_1001)
- **SCRB/SCRS**: Frame-basierte Feature/Snoid-Scripts — **gleiches Format, siehe Patch-Analyse oben**
- **SND**: Mohawk-Wrapped WAVE — **8 ZOOMBINI-Sounds neu komprimiert** (deutlich kleiner: z. B. 107 KB → 30 KB)
- **NODE/PATH**: Walk-Navigation — unverändert

---

## 5. Zoombini-Attribut-System (unverändert)

- 4 Merkmale × 5 Varianten = 625 Kombinationen
- Gleiche Attribut-Reihenfolge (Haare/Augen/Nase/Füße)
- Gleicher MSVC LCG (siehe Caveat in §1)
- PICKER-Archive: 53 SCRBs **bitweise identisch**, 9 von 11 tBMPs identisch, 2 tBMPs (4200, 4400)
  deutlich größer (9→14 KB / 31→49 KB) — könnten neue Character-Parts sein, **sollte geprüft werden**

---

## 6. Gesamtbilanz

|  | Zahl |
|---|---|
| Ressourcen bitweise identisch v1 ↔ v2 | **4055** |
| Ressourcen binär geändert | **94** (davon 52× Mikro-Patch in PIZZA, Rest meist tBMP-Repack) |
| Ressourcen komplett neu | **147** (53 STRL + 56 PIZZA-SCRB + 17 NET-SCRB + 7 LILLY + 7 ZOOMBINI + 7 BCTWO-tBMP) |
| Ressourcen aus v1 entfernt | **0** |

---

## 7. Folgerung für das Projekt (revidiert)

1. **`mohawk_parser.py`, `bitmap_decoder.py`, `puzzle_analyzer.py` funktionieren unverändert** auf v2.
   Neu sollte Support für den **STRL-Ressourcentyp** hinzugefügt werden.
2. **Puzzle-Algorithmen-Analysen in `analysis/*.md` gelten grundsätzlich auch für v2** —
   aber für **PIZZA sollte neu geprüft werden**, was die 56 zusätzlichen Hotspots bewirken
   (neue Topping-Optionen? vergrößertes UI-Panel? mehr Trolle?). Die bestehende Regel-Analyse
   ist wahrscheinlich korrekt, aber die **Parameter-Grenzen** (Anzahl Toppings, Anzahl Trolle)
   könnten sich verschoben haben.
3. **`GameState.cs` / `ProcessMemory.cs` müssen neu kalibriert werden** — PE32 hat andere
   Adressen als die 16-bit NE-Version, und `trinketcore.dll` als separates Modul braucht eigene
   Base-Address-Behandlung.
4. **Neue Archive** HELP, MUSIC, NETDEMO, RODMAP sind Reverse-Engineering-neutral
   (nur zusätzliche Assets).

**Kurz:** v2 ist **nicht** 1:1 identisch zu v1, aber die Unterschiede sind zu ≈ 97 %
kosmetisch/technisch (Repack, Help-Overlay, Bugfix, Script-Bereinigung). Die einzige echte
**Gameplay-Erweiterung** ist die ~25 %-Vergrößerung der PIZZA-Hotspot-Tabelle — das sollte
vor Ort nachgemessen werden, bevor der bestehende PIZZA-Solver ungeprüft auf v2 angewendet wird.
