# v2 Binary Map — `ZoombinisLJ.exe` (PE32, 614 KB)

> **Stand 2026-04-25.** Statische Analyse, kein Live-Memory-Dump. Diese Datei ist
> die kanonische Code-Karte des v2-Binaries — Pendant zu `SEGMENT_MAP.md` für v1.
> **Konfidenz-Markierungen folgen CLAUDE.md** (`Verified` / `Probable` / `Speculative`).
>
> Erzeugt durch `pe_loader.py` + `scratch/v2_harvest.py`. Roh-Output liegt unter
> `scratch/v2_harvest_output.txt`.

---

## 1. Build-Info

| Feld | Wert |
|------|------|
| Format | PE32, 32-bit i386 |
| ImageBase | `0x00400000` |
| EntryPoint | `0x0047D0D8` |
| Linker | Microsoft Visual C++ 6.0 |
| Subsystem | GUI |
| Größe | 614 400 B |
| `WinMain`-Funktion | `0x00445920` (size `0x520`) — zeigt typische OS/Heap/Sound/MHK-Init-Strings |

**Sektionen:**

| Name | VA-Bereich | File-Bereich |
|------|-----------|--------------|
| `.text` | `0x00401000`–`0x004854E2` | `0x001000`–`0x086000` |
| `.rdata` | `0x00486000`–`0x0048AEED` | `0x086000`–`0x08B000` |
| `.data` | `0x0048B000`–`0x004A6E38` | `0x08B000`–`0x095000` |
| `.rsrc` | `0x004A7000`–`0x004A7508` | `0x095000`–`0x096000` |

**Importierte DLLs:** `KERNEL32`, `USER32`, `GDI32`, `IFC22` (Riverdeep-Engine, 20 Symbole),
`mss32` (Miles Sound), `binkw32` (Bink Video), `WINMM`, `VERSION`, `ADVAPI32`. Kein direkter
Import von `trinketcore.dll` — Spiellogik liegt komplett in der Haupt-EXE.

---

## 2. Methodik

Pro Puzzle wurde der **MHK-Filename-String** (`bridge.mhk`, `caves.mhk`, …) als
Anker verwendet. Genau eine `push imm32`-Instruktion (Opcode `0x68`) referenziert
jeden String. Die enthaltende Funktion wurde durch Schnitt aus Direct-Call-Targets
(E8 rel32) und VC++6-Standard-Prologen (`55 8B EC`) ermittelt. Das gibt 1481
Funktionsstarts gegenüber 153 reinen Prolog-Treffern — **direct-call-Discovery
findet auch FPO-optimierte Funktionen**, die kein klassisches Stack-Frame haben
(z. B. die `bridge.mhk`-Loader-Funktion).

---

## 3. Klassifikation der MHK-Loader-Funktionen

Spalte „Klasse":
- **LIKELY-INIT** — von einer Puzzle-spezifischen Wrapper-Funktion aufgerufen,
  exakt 1 Caller, MHK-Push früh in der Funktion. Hartes Indiz, dass dies der
  Puzzle-Init-Pfad ist.
- **SETUP** — wird vom `WinMain`-Init-Pfad (`0x00445920`) aufgerufen. Pusht den
  MHK-Namen vermutlich für eine **CFG-Validierung beim Programmstart**, nicht
  für die Spiel-Init. Die echte Puzzle-Init liegt **anderswo** und ist hier
  noch nicht lokalisiert.
- **NEEDS-VERIFY** — viele Caller (>3) oder Caller-Mix mit anderen Puzzles.
  Möglich, dass dies ein Shared-Helper ist statt Puzzle-Init.

| Puzzle | MHK-String VA | Loader-Funktion | Größe | Caller | Klasse | Konfidenz | v1-Pendant (Seg) |
|--------|--------------|-----------------|------|--------|--------|-----------|------------------|
| Cliffs | `0x0048B708` | `0x00405A60` | `0x580` (1408 B) | 1 (`0x403F10`) | LIKELY-INIT | Probable | Seg 23 |
| Caves | `0x0048B9B8` | `0x00407B30` | `0xB70` (2928 B) | 1 (`0x406210`) | LIKELY-INIT | Probable | Seg 24 |
| Ferry | `0x0048BDDC` | `0x0040FE20` | `0x460` (1120 B) | 1 (`WinMain`) | **SETUP** | Anker ungültig | Seg 41 (?) |
| Fleens | `0x0048BEF4` | `0x00411E80` | `0x640` (1600 B) | 1 (`0x410520`) | LIKELY-INIT | Probable | Seg 27 |
| Hotel | `0x0048C298` | `0x004154D0` | `0x930` (2352 B) | 1 (`0x412760`) | LIKELY-INIT | Probable | Seg 28 |
| Lilly | `0x0048D338` | `0x00419770` | `0x970` (2416 B) | **10** | NEEDS-VERIFY | unklar | Seg 30 |
| Maze2 | `0x0048DA14` | `0x004242F0` | `0x11D0` (4560 B) | 1 (`0x4209E0`) | LIKELY-INIT | Probable | Seg 34 |
| Net | `0x0048E1C8` | `0x0042BC20` | `0x870` (2160 B) | **25** | NEEDS-VERIFY | unklar | Seg 35 |
| Pizza | `0x0048F280` | `0x00431590` | `0xF40` (3904 B) | 1 (`0x42FCB0`) | LIKELY-INIT | Probable | Seg 37 |
| Slides | `0x0048F49C` | `0x00438E30` | `0x800` (2048 B) | 1 (`WinMain`) | **SETUP** | Anker ungültig | Seg 45 |
| Smoke | `0x0048FA6C` | `0x0043FB70` | `0xB70` (2928 B) | **7** | NEEDS-VERIFY | unklar | Seg 42 |
| Tunnels | `0x00492D38` | `0x0044FF30` | `0x62A` (1578 B) | 1 (`0x44E9D0`) | LIKELY-INIT | Probable | Seg 48 |

**Wichtig:** Die als „Loader" identifizierten Funktionen rufen das MHK-Archive auf,
sind aber nicht zwingend die einzige oder wichtigste Init-Funktion eines Puzzles.
Übergeordnete **Wrapper-Funktionen** (Spalte „Caller") rufen den Loader und machen
zusätzliche Spielzustands-Vorbereitung. Pro Puzzle existiert also typischerweise
ein zweistufiger Init-Pfad: Wrapper → Loader → MHK-Push + Engine-Calls.

### Wrapper-Funktionen (= Caller des Loaders)

| Wrapper | Größe | Eigene Caller | Lädt Loader für | Beobachtete Strings |
|---------|------|---------------|-----------------|---------------------|
| `0x00403F10` | `0xA20` | 4 | Cliffs | — |
| `0x00406210` | `0x550` | 2 | Caves | — |
| `0x00410520` | `0xD50` | 2 | Fleens | `Play FrogMan SCRB id:` |
| `0x00412760` | `0x950` | 2 | Hotel (+ Lilly + Net via call?) | — |
| `0x004209E0` | `0x3D0` | 3 | Maze2 | `ZBUser`, `ZBtemp` |
| `0x0042FCB0` | `0x360` | 2 | Pizza | — |
| `0x0044E9D0` | `0x750` | 2 | Tunnels | — |

Alle Wrapper rufen sehr früh `0x0045F4E0` und `0x00455580` auf — das sind
**Engine-Setup-Calls** (Bedeutung noch nicht entschlüsselt, aber konstantes Pattern).

---

## 4. Engine-globale `.data`-Variablen

Variablen, die in **mehreren Puzzle-Loader-Funktionen** vorkommen — Hypothese:
gemeinsam genutzte Engine-Globals analog zu v1's `engineFlag` (`DS:0xAA32`),
`stateFlag` (`DS:0x8ED0`), `currentPuzzleIndex` etc.

| VA | Verwendung in Puzzles | Hypothese | Konfidenz |
|----|----------------------|-----------|-----------|
| `0x004950C8` | 12/12 | engineFlag (puzzle-loaded) | Speculative |
| `0x004A2818` | 12/12 | weiterer globaler Status | Speculative |
| `0x004A4B1C` | 10/12 | ? | Speculative |
| `0x004A4878` | 9/12 | ? | Speculative |
| `0x0049B0E6` | 8/12 | ? | Speculative |
| `0x004A36B8` | 5/12 | ? | Speculative |
| `0x004A3830` | 4/12 | ? | Speculative |
| `0x004A4AAA` | 4/12 | ? | Speculative |
| `0x004A2402` | 4/12 | ? | Speculative |

**Alle Hypothesen sind unverifiziert.** Um die Bedeutung zu fixieren, muss pro
Variable Lese-/Schreibkontext disassembliert werden (welche Konstante wird hineingeschrieben,
welche Compare-Werte werden dagegen geprüft, welcher Bedingungssprung folgt). Das ist die
nächste Iteration.

---

## 5. Puzzle-eigene `.data`-Bereiche

Pro Puzzle bündelt der VC++6-Compiler Übersetzungseinheits-Globals in
zusammenhängenden `.data`-Regionen. Diese Bereiche sind die v2-Pendants zu v1's
DS-Variablen-Blöcken.

| Puzzle | `.data`-Region (geschätzt) | Anzahl unique VAs in Loader |
|--------|----------------------------|-----------------------------|
| Cliffs | `0x004944C0`–`0x004945E0` | 17 |
| Caves | `0x00494600`–`0x004948A0` | 62 |
| Ferry | `0x00495A00`–`0x00495AC0` | 25 (in CFG-Setup) |
| Fleens | `0x00495AE0`–`0x00495D80` | 22 |
| Hotel | `0x00495E80`–`0x004969C0` | 76 |
| Lilly | `0x00497400`–`0x00499900` | 109 |
| Maze2 | `0x00499AC0`–`0x0049AC20` | 144 |
| Net | `0x0049B000`–`0x0049B6C0` | 116 |
| Pizza | `0x0049B800`–`0x0049BCA0` | 127 |
| Slides | `0x0049BC00`–`0x0049CA00` | 49 (in CFG-Setup) |
| Smoke | `0x0049CA00`–`0x0049CDC0` | 97 |
| Tunnels | `0x004A2800`–`0x004A3000` | 22 |

> Diese Bereiche sind **grobe Hüllen** aus den im Loader gelesenen/geschriebenen
> Adressen. Sie sind weder vollständig noch garantiert disjunkt — eine echte
> Section-Map mit Größenangaben pro Symbol braucht eine BSS-Sortierung
> (Datenfluss-Analyse).

---

## 6. v1↔v2 Algorithmus-Identität (verifiziert)

In v2 vorhandene Debug-/Marker-Strings, die in v1 zur Verified-Identifikation
einzelner Puzzle-Mechaniken benutzt wurden:

| Marker (v1-Doku) | v2-VA | v2-push-Site | Enclosing Funktion |
|------------------|-------|--------------|--------------------|
| `Upper bridge accepts:` | `0x0048B72C` | `0x00406B60` | `0x00406760`–`0x00407180` |
| `Lower bridge accepts:` | `0x0048B714` | `0x00406B67` | gleiche Funktion |
| `Arno    %d %d %d %d %d %d %d %d` | `0x0048F2F0` | `0x00435115` | `0x00435020`–`0x00435340` |
| `Willa   %d %d %d %d %d %d %d %d` | `0x0048F2D0` | `0x00435167` | gleiche |
| `Shyler  %d %d %d %d %d %d %d %d` | `0x0048F2B0` | `0x004351B9` | gleiche |
| `Play FrogMan SCRB id:` | `0x0048BDE8` | `0x00410FD9` | `0x00410520`–`0x00411270` |
| `bogus snoid script id` | `0x0048FE28` | `0x0044B1A7` | Engine, 62 Caller |
| `Too many main-feature SCRBs` | `0x00492F98` | `0x00456500` | Engine, 17 Caller |
| `Feature Group already used` | `0x0049329C` | `0x00457F6B` | Engine, 63 Caller |
| `bridge.mhk` (lowercase!) | `0x0048B708` | `0x00405CDE` | Cliff-Loader |

**MSVC LCG-Konstante 2531011 (`0x269EC3`)** ist in v2 **genau einmal** vorhanden,
LCG-Multiplikator `214013` als Literal **nicht auffindbar** (vermutlich vom
VC++6-Optimierer in `imul`-Instruktion inlined). v1-Vergleich identisch.

→ **Code-seitig ist die Spiellogik 1:1 portiert** (gleiche printf-Pushes an
korrespondierenden Stellen). Die *Adressen* der globalen Variablen sind
hingegen **anders** und müssen pro Variable neu zugeordnet werden.

---

## 7. Identifizierte Engine-Funktionen (häufige Caller)

| VA | Caller-Anzahl | Hypothese | Konfidenz |
|----|---------------|-----------|-----------|
| `0x00455DB0` | 321 | `memset` oder Generic-Init | Speculative |
| `0x00457940` | 147 | `random_range(min,max)` (v1-Pendant: seg20:0x04E8) | Probable |
| `0x00476DA0` | 129 | `memcpy` oder Array-Setter | Speculative |
| `0x00457F00` | 63 | feature-group-set (Marker `Feature Group already used`) | Verified |
| `0x004565C0` | 46 | SCRB-Add | Probable |
| `0x004564D0` | 17 | SCRB-feature-add (Marker `Too many main-feature SCRBs`) | Verified |
| `0x0044B160` | 62 | snoid-script-execute (Marker `bogus snoid script id`) | Verified |
| `0x00456380` | 530 | `printf`-Engine-Wrapper | Probable |

---

## 8. Lücken (was diese Karte NICHT leistet)

Per CLAUDE.md explizit benannt:

1. **Ferry/Slides echte Init nicht lokalisiert.** Beide MHK-Pushes liegen in der
   `WinMain`-Setup-Routine (CFG-Validierung). Die echten Captain-Cajun- und
   Stone-Rise-Init-Pfade sind über andere harte Marker noch zu finden.
2. **Lilly/Net/Smoke Loader unklar.** Vielfache Caller deuten auf Shared-Helper
   statt Puzzle-spezifischen Init. Eine Iteration tiefer (Disasm der Wrapper
   und Identifikation, ob die Loader-Funktion exklusiv oder generisch ist) ist
   nötig.
3. **Eval-Funktionen nicht systematisch lokalisiert.** Nur Cliffs (`0x00406760`)
   und Pizza (`0x00435020`) haben eindeutige Debug-Marker, die ihre Eval-Funktion
   pinnen. Die anderen 10 Eval-Funktionen müssen über Action-Dispatch-Pattern
   (`cmp ax, 1; je …; cmp ax, 2; je …`) identifiziert werden.
4. **`.data`-Variablen-Bedeutung pro Puzzle.** Pro Puzzle sind dutzende globale
   Adressen ausgelesen, ihre semantische Rolle (Difficulty / Counter / Pool /
   State-Flag) ist statisch noch nicht zugeordnet. Das geht nur durch
   instruction-by-instruction-Disasm der Loader+Wrapper+Eval.
5. **Engine-Globals-Bedeutung.** Hypothesen oben sind Speculative. Zu fixen
   per Lese-/Schreibkontext.
6. **v1↔v2 DS-Mapping.** Pro v1-DS-Variable muss die v2-VA durch
   instruction-by-instruction-Vergleich identifiziert werden. Bisher nur
   strukturell vorbereitet, nicht ausgeführt.

---

## 9. Nächste sinnvolle Iterationen

In Reihenfolge nach Aufwand × Nutzen:

1. **Cliffs vollständig**: Loader+Eval+Wrapper komplett disassemblieren, jede
   `.data`-VA mit v1-DS-Offset abgleichen. Cliffs ist der beste Kandidat, weil
   `Upper/Lower bridge accepts:` die Eval-Funktion eindeutig pinnt.
2. **Hotel vollständig**: Ähnlich gute Marker-Lage, in v1 schon Probable.
3. **Pizza vollständig**: `Arno/Willa/Shyler` pinnen die Eval. Pizza hat in v2
   neue Hotspots (siehe `V1_VS_V2_COMPARISON.md`), Sonderprüfung lohnt.
4. **Caves**: Verified-Marker aus v1 (`Caves.MHK` push) ist in v2 als
   `caves.mhk` identifiziert. Init-Identifikation ähnlich zu Cliffs.
5. **Ferry+Slides Init suchen**: harten Marker pro Puzzle finden (erfordert
   Brain-Storming über Stone-Rise- und Captain-Cajun-spezifische Strings/Werte).
6. **Lilly/Net/Smoke**: Wrapper disassemblieren, ggf. neue Loader-Kandidaten
   finden.
7. Engine-Globals-Bedeutung über Lese/Schreibkontext klären.

Ein einzelnes Puzzle vollständig zu disassemblieren+vergleichen ist eine
substantielle Session (mehrere Tausend Instruktionen, Sub-Calls verfolgen). Ein
realistisches Bottom-up-Vorgehen ist *ein Puzzle pro Iteration*. Diese Karte
ist die Grundlage; sie macht **keine** semantischen Behauptungen, die nicht
durch Marker oder kreuzgeprüfte Code-Pattern gedeckt sind.
