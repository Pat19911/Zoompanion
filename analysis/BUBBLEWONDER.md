# Bubblewonder Abyss — Mechanik-Modell (Stand 2026-05-03)

> Kanonische Doku des Spielmechanik-Modells für Bubblewonder Abyss.
> Frühere Versionen (Reverse-Engineering-Forensik, Heat-Maps, Hypothesen-Iterationen)
> liegen in `_archive/`.

## Spielziel

N Zoombinis (Diff 1: ~8, Diff 4: 16) müssen aus einem **Pool** durch ein
**12×13-Grid** zum Ziel-Bereich kommen. Auf dem Grid stehen Mechanismen
(Pfeile, Filter, Schalter, Klebefelder, Fallen). Der Spieler entscheidet:
**welcher Zoombini wird über welchen Start-Pfeil losgeschickt, in welcher Reihenfolge.**
Das ist die **einzige** Spielereingabe.

Alles was danach passiert, ist **vollständig deterministisch**.

## Pool / Maschinen / Insel

- **Pool**: alle ZBs die noch nicht losgeschickt wurden.
- **Bubble-Maschinen**: Spawn-Punkte am Grid. Jede Maschine hat einen festen
  Start-Pfeil. Mehrere Maschinen pro Layout möglich (Diff 4: bis zu 3).
- **Insel-Maschinen** (nur Diff 4): Maschinen in den Eck-Zonen
  (oben-links: row≤3 UND col≤3, ODER unten-rechts: row≥9 UND col≥9).
  ZBs die im Grid in eine Insel-Maschine **eintreten**, werden dort
  **geparkt** statt zu sterben/scoren. Der Spieler kann sie später von dort
  losschicken — das ist die Diff-4-Strategie ("ZB auf Insel verfrachten,
  dann durch andere ZBs Switches umlegen, dann den geparkten loschicken").

Hardcoded Spawn-Maschinen pro REGS-ID: siehe
`src/ZoombiniHelper.Core/Bubblewonder/BubblewonderSpawnMappings.cs`.

## Cell-Typen (REGS-f0)

REGS-Records sind 10 16-bit-Words pro Cell, gespeichert in der Mohawk-REGS-Resource.
Im Live-Memory liegen sie als Little-Endian-Kopie ab `bubble[+0x6C]`.

| f0 | Type | Verhalten beim ZB-Durchlauf |
|----|------|-----------------------------|
| 1 | **Trap** | ZB stirbt |
| 2 | **Conditional** | wenn ZB-Attribut matcht (Attr-Code `+0x82`, Variant `+0x84`) → ZB wird umgelenkt in REGS-F4..F7-Richtung. Wenn nicht → läuft straight durch. |
| 3 | **Static-Deflector** | ZB wird immer umgelenkt in REGS-F4..F7-Richtung. |
| 4 | **Switch** | ZB wird in **aktuelle** aktive Richtung umgelenkt (Index in `bubble[+0x7C]`). Switch ändert sich **nicht** durch eigenen Durchlauf — nur durch externen Trigger. |
| 5 | **Sticky** | ZB wird **eingefangen** (`bubble[+0x86]` = Hdr1A). Drei Befreiungs-Modi: (a) **Channel-Hit**: anderer ZB läuft durch eine Cell im gleichen Channel (F3) → befreiter ZB läuft in seiner ursprünglichen **Eintrittsrichtung** weiter. (b) **Push**: ein anderer ZB läuft direkt in den belegten Sticky → eingesperrter ZB wird in **Schub-Richtung** weggeschubst (= weg vom Schubser), Schubser nimmt seinen Platz. |
| 6 | **Trigger** | ZB läuft straight durch. Target-Switch (Hdr1A in `bubble[+0x166]`) wird umgeschaltet. |

**Direction-Encoding (REGS-Internal):** F4..F7 sind 4 Direction-Bits in
fester Reihenfolge: F4=Up, F5=Right, F6=Down, F7=Left. Pro Cell sind 1..N
dieser Bits gesetzt (= mögliche Auswurf-Richtungen).

## Pfad-Endung pro ZB

Ein ZB-Pfad endet wenn er in eine dieser Cells läuft:

- **Trap-Cell** → ZB tot
- **Insel-Maschine** → ZB geparkt (re-spawn-bar vom Spieler)
- **Sticky-Cell** (leer) → ZB festgeklebt (wartet auf Befreiung)
- **Ziel-Bereich** (verlässt das Grid am Ausgang) → ZB gescored

## Spielzustand

Der vollständige Grid-Zustand zwischen ZB-Würfen besteht aus:

1. **Switch-States**: pro f0=4 Cell der aktuelle Richtungs-Index (`+0x7C`).
2. **Sticky-Belegung**: pro f0=5 Cell der aktuell festgeklebte ZB (`+0x86`)
   oder 0 wenn leer.
3. **Insel-Parkplätze**: ZBs die im Grid landeten und auf Re-Spawn warten.
4. **Pool**: ZBs die noch nicht los waren.

Das ist endlich und überschaubar — Solver-relevant.

## Solver-Architektur

Aus dem Modell folgt:

```
Solver(REGS, ZB-Pool):
    GridState = InitialState(REGS)
    Reihenfolge = Search:
        für jede mögliche (ZB → Maschine)-Sequenz:
            simuliere Schritt-für-Schritt
            bewerte: wieviele überleben?
        wähle Sequenz mit max Survivors

Simulator-Step(GridState, ZB, Maschine) → (NewGridState, Outcome):
    Position = Maschine.StartCell
    Direction = Maschine.StartDirection
    while True:
        Cell = Grid[Position]
        nach Cell.Type:
            Trap          → return (GridState, Dead)
            Conditional   → if matches(ZB, Cell): Direction = pick(Cell.F4..F7)
                            else: pass through
            StaticDefl    → Direction = pick(Cell.F4..F7)
            Switch        → Direction = Cell.ActiveDirection
            Trigger       → toggle(GridState[Cell.TargetSwitch])
            Sticky        → if (GridState[Cell].Channel-Pair-Has-ZB):
                                free both, return (GridState_updated, BothScore?)
                            else: GridState[Cell].Trapped = ZB
                                  return (GridState, Trapped)
            Insel-Maschine→ return (GridState, Parked)
        Position = next(Position, Direction)
        if Position out-of-grid: return (GridState, Scored)
```

## Disasm-Erkenntnisse: Trigger-Effekt-Funktion (2026-05-03)

`fn 0x0042A950` ist die zentrale **Trigger-Effekt-Funktion**. Aufgerufen vom
ZB-Cell-Enter-Handler (`fn 0x00425750` bei `0x425CEF`) wenn ein ZB eine
Cell betritt die einen Channel-Effekt auslöst.

**Pseudocode:**
```c
void TriggerEffect(uint16_t cell_handle):
    cell = handle_to_object(cell_handle)
    if (cell == 0) return
    cell[+0x128] = 2

    channel = cell.prop1                        // = REGS-F3 = "Farbe"
    // Lookup pro Channel: Counter + Handle-Array
    switch (channel - 1):
        case 0: count = *0x49a2c4; arr = 0x49ab74; break
        case 1: count = *0x49a3a0; arr = 0x49a474; break
        case 2: count = *0x49a28c; arr = 0x49a058; break
        case 3: count = *0x49a398; arr = 0x49aadc; break
        case 4: count = *0x49a81c; arr = 0x49ac44; break
        case 5: count = *0x49a880; arr = 0x49a240; break
        case 6: count = *0x49a87c; arr = 0x49a5a4; break

    // Loop rückwärts durch alle Cells im selben Channel:
    for (i = count; i > 0; i--):
        target = handle_to_object(arr[i-1])
        if (target == 0) continue

        if (target.regs[0] == 4):               // Switch-Cell
            target[+0x7C]++                      // increment State
            if (target[+0x7C] > 3) target[+0x7C] = 0
            // Skip leere Direction-Bits (round-robin durch nur AKTIVE):
            while (target.regs[F4 + state] == 0):
                target[+0x7C]++
                if (target[+0x7C] > 3) target[+0x7C] = 0
            target[+0x128] = 3

        if (target.regs[0] == 5):               // Sticky-Cell
            trapped = target[+0x86]
            if (trapped != 0):
                // Befreie ZB: häng in globale Liberation-Queue
                idx = *0x49a470
                *(0x49a2d0 + idx*2) = trapped
                *0x49a470 = idx + 1
                target[+0x86] = 0                // Cell jetzt leer
```

### Resultierende Antworten:

**Frage 1 — Switch-Toggle:** ✓ **Round-robin durch nur die aktiven F4..F7-Direction-Bits**.
State (+0x7C) wird incrementiert, gewrappt bei >3, dann übersprungen
solange der entsprechende F-Eintrag 0 ist.

**Frage 2 — Sticky-Befreiung:** ✓ Sticky wird **per Channel** befreit, nicht
per direktem Trigger-Pointer. Der befreite ZB wird in eine globale
Liberation-Queue (`0x49a2d0`) eingereiht — die Auswurf-Richtung muss
in einer separaten Tick-Phase entschieden werden (die diese Queue
verarbeitet — TODO finden).

**Frage 3 — Befreier-Pass-Through:** Der "befreiende" ZB läuft seinen Weg
weiter. Die Cell die er betritt ist **nicht** die Sticky selbst — es ist
eine **andere Cell mit gleichem Channel** (z.B. ein Trigger). Der ZB läuft
straight durch diese Cell, und der Channel-Effekt befreit alle Stickies
im gleichen Channel.

### Wichtige Korrektur zu vorher:

Die Mechanik ist **channel-basiert** (REGS-F3 = "Farbe"), nicht Pointer-basiert.
Der `+0x166`-Pointer in Trigger-Cells ist vermutlich nur ein Convenience-Cache
auf den ersten Switch im Channel — die echte Engine-Logik läuft über die
Channel-Lookup-Tabellen bei `0x49a0b4` (per-position Handle) und die
Pro-Channel-Arrays.

### Noch offen:

- **Wer verarbeitet die Liberation-Queue (`0x49a2d0`)?** Der befreite ZB
  muss irgendwann weiterlaufen — in welche Richtung? Vermutlich
  Sticky-eigene F4..F7. → muss noch gefunden werden, ist aber nicht
  blocking für Simulator-V1 (kann initial straight-down annehmen).
- **Welche Cell-Typen rufen `TriggerEffect` auf?** Bisher nur fn 0x425750
  als Caller bekannt. Vermutlich der ZB-Cell-Enter-Handler. Trigger (f0=6)
  ist offensichtlich, aber vielleicht auch Conditional (f0=2) bei Match?

## ZB-Outcome-Klassifikation: Score vs Insel-Park vs Falle (2026-05-26)

> **Verifiziert (Disasm + Live, 2 unabhängige Runden).** Anlass: der Tracker
> meldete insel-geparkte und in Fallen gefangene ZBs als „Scored" und lernte
> deren Position als Goal-Cell — das verschmutzte die Goal-Cell-Liste
> systematisch.

### Der Marker: `ZB[+0x76]` (Outcome-Typ)

Die PROCESS-Funktion `fn 0x00425a00` klassifiziert pro ZB das Outcome über
eine **Jump-Table bei `0x00425ee4`**, indiziert durch `ZB[+0x76]` (0..3):

| `+0x76` | Sprungziel | Effekt | Bedeutung |
|---------|-----------|--------|-----------|
| 0 | 0x425aa4 | `[+0x20] := 1` (transient) | nicht durch (Pool-artig) |
| 1 | 0x425ac4 | `[+0x20] := 0x8001` | nicht durch |
| 2 | 0x425adf | `[+0x20] := 1` (transient) | nicht durch |
| 3 | 0x425af7 | `[+0x20] := 0x4008001`, Win-Check | **auf Steininsel = echtes Score** |

Fall 3 ruft `0x447d20` (zählt ZBs auf der Steininsel), vergleicht gegen
`*0x49a39c` (= benötigte Anzahl, live = 16) und setzt bei Vollzahl das
Gewinn-Flag `*0x49a55c = 1`.

### Live-Verifikation (2 Runden, F12-Dumps)

| Runde | durch (+0x76) | gefangen/Falle (+0x76) | Insel-Park |
|-------|---------------|------------------------|------------|
| oben-links | 1× ZB = **3** | 2× ZB = 0, 1 | leer |
| unten-rechts | 2× ZB = **3** | 3× ZB = 0, 2, 2 | leer |

In **keinem** Fall hatte ein nicht-durchgelaufener ZB `+0x76 == 3`. Der
durchgelaufene hatte **immer** genau 3.

### Wichtige Negativ-Befunde (NICHT als Marker brauchbar)

- **`ScoredHandlesCount` (`0x49a206`)** bleibt auch bei echtem Score **0** —
  ist NICHT der Score-Counter (eher generischer Listen-Counter). Die Liste
  `0x49a3a4` enthält gepoppte/gefangene/geparkte ZBs gemischt; der Tracker
  las `liste[0]` ungated → Stale-/Fehlklassifikation.
- **`ZB[+0x20]`** ist für *alle* aktiven ZBs (durch, gefangen, geparkt)
  `0x04008001` — unterscheidet nur "losgeschickt" von "im Pool", NICHT das
  Outcome. Nur `+0x76` diskriminiert.
- **Pool-Cluster-y-Heuristik** ist mehrdeutig: ein durchgelaufener ZB auf der
  Steininsel erscheint als abgesetzter Cluster (wie eine Insel) — `+0x76` löst
  das eindeutig.

### Implementierung im Helper

`BubblewonderTracker.ScanSteinInselScored()` walkt pro Tick die Engine-Liste
und merkt jeden ZB-Knoten (Handle 1 / 0x04008001 / 0x04001001) mit
`+0x76 == 3` in `_steinInselScoredZbs` (einmal gesetzt bleibt es — Fall 3 ist
final). NUR diese ZBs tragen eine Goal-Cell bei (`LearnedGoalCells`) und
werden als "Scored" klassifiziert.

## Ziel-Erkennung: die vier Steinbereiche via Zelltyp-Tabelle `0x499efc` (2026-05-27)

> **Verifiziert (Ghidra-Disasm + Live über 62 Dumps / 8 Layouts / Diff 1-4).**
> Anlass: der Solver meldete „16/16 überleben", führte ZBs aber in Fallen/zurück
> in den Pool. Ursache war eine naive Annahme im Simulator: **jeder Gitter-Austritt
> = Score**. Tatsächlich scort ein ZB nur, wenn er die **Ziel-Steininsel** erreicht.

### Der Mechanismus

Die Engine hält eine **Zelltyp-Tabelle bei VA `0x00499EFC`**: 1 Word pro Grid-Zelle,
Index = `row*13 + col` (156 Zellen). Pro Tick liest PROCESS (`fn 0x00425a00`) den
Typ und dispatcht. Werte:

| Typ | Bedeutung |
|-----|-----------|
| 0 | leer |
| 1–6 | REGS-Mechanik (Trap/Conditional/Deflector/Switch/Sticky/Trigger) |
| **0x14** | Steinbereich **Start** (Bildschirm unten-links, wo ZBs spawnen) |
| **0x15** | Steinbereich **Zwischenstation** (oben-links) |
| **0x16** | Steinbereich **Zwischenstation** (unten-rechts) |
| **0x17** | Steinbereich **ZIEL** (oben-rechts) |

Der Handler `fn 0x00425f30` (Dispatch-Fälle 0x14–0x17) setzt
`ZB[+0x76] := celltype − 0x14` (also 0x17 → 3) und bei `==3` zusätzlich das
Insel-Flag `ZB[+0x12C] := 1`. PROCESS-Fall 3 ruft den Insel-Zähler `fn 0x00447d20`
(zählt ZBs mit `+0x12C != 0`), vergleicht gegen `*0x49a39c` (benötigte Anzahl) und
setzt bei Vollzahl das Win-Flag `*0x49a55c = 1`. Das schließt den Kreis zur
[+0x76-Outcome-Sektion](#zb-outcome-klassifikation-score-vs-insel-park-vs-falle-2026-05-26).

### Verifikation

- **Disasm:** Ghidra-Dekompilat von PROCESS + `fn 0x425f30` (siehe
  `scratch/ghidra_scripts/FindGoalWriter*.java`).
- **Live:** Rekonstruktion der Tabelle aus **62 Bubblewonder-Dumps** über 8 REGS-IDs
  (0x40d8…0x40e0, Diff 1-4). In **jedem** Dump: **100 % Match** der Tabelle (Typ 1-6)
  gegen die sichtbaren REGS-Mechanismen. Steinzellen **identisch in allen Dumps**:
  - **Ziel (0x17): immer `(10,0) (10,1) (10,2) (11,2)`** (4 Zellen, oben-rechts)
  - Start (0x14): `(0,10)(1,10)(2,10)(2,11)(2,12)` · Zwischen (0x15): `(0,2)(1,0)(1,1)(1,2)` · Zwischen (0x16): `(10,11)(10,12)(11,11)`
- Die beiden Zwischenstationen decken sich exakt mit `BubblewonderSpawnMappings.IsIslandCell`
  (`(row≤3&&col≤3) || (row≥9&&col≥9)`).

### Implementierung

`BubblewonderGridModelBuilder.AddStoneCells` liest die Tabelle live
(`BubblewonderMemoryMap.CellTypeTable`) und fügt `0x17`→`MechanismType.Goal`,
`0x14/0x15/0x16`→`MechanismType.StoneArea` hinzu (diese Zellen stehen NICHT in den
REGS). Der Simulator scort nur auf `Goal`; Austritt über eine andere Kante →
`Dead` (kein Score). Die alte, vor dem 2026-05-26-Fix verseuchte
`BubblewonderKnownMaps.GoalCellsByFingerprint`-Tabelle wurde geleert — die
Live-Tabelle ist map-unabhängig und braucht kein Lernen.

### Pfeil-„Störung" von Zugangspunkten

Da die Tabelle den *tatsächlichen* Typ jeder Zelle liefert, ist ein durch einen Pfeil
„gestörter" Zugangspunkt automatisch abgedeckt: liegt dort ein Deflektor (Typ 3),
ist die Zelle nicht `0x17`, der ZB wird umgelenkt bevor er ankommt — kein Score.

### Live Pool/Insel-Klassifikation (Folgefix 2026-05-27)

> Anlass: ein in einer **Falle** steckender ZB wurde als „auf Insel" gemeldet, weil
> die alte Trennung Pool↔Insel rein über **y-Pixel-Cluster** (`PoolClusterer`) lief —
> ein abgesetztes y sah aus wie eine Insel.

`BubblewonderPoolClassifier.Split` trennt jetzt über **harte Engine-Signale**:

- **Handle** (`+0x20`): `0x00000001` = im Pool (verfügbar) · `0x04001001` = hochgehoben
  · `0x04008001` = **losgeschickt** (auf dem Grid).
- Ein losgeschickter ZB (`0x04008001`) zählt nur als **insel-geparkt**, wenn seine
  Grid-Zelle (Position `+0x72`/`+0x74`) in der Zelltyp-Tabelle eine **Zwischenstation
  `0x15`/`0x16`** ist. `0x17` = gescort, Trap (Typ 1) / sonst = nicht verfügbar (weder
  Pool noch Insel).

Live-Beleg: gefangener ZB `0x0011 (5,1,5,4)` hatte Handle `0x04008001`, Grid `(6,6)`
(= Trap), `+0x76=0`, `+0x12C=0` → korrekt **nicht** als Insel. `WithParkedZbs` bekommt
die bereits klassifizierte Insel-Liste (kein y-Cluster mehr).

## Bildschirm-Orientierung: Grid ist TRANSPONIERT dargestellt (2026-05-26)

> **Live kalibriert über vier unabhängige Anker.** Anlass: die UI-Lage-Angaben
> ("mitte-rechts" aus row/col) stimmten nicht mit dem überein was der Spieler
> auf dem perspektivisch gezeichneten Feld sieht.

Die interne `(row, col)`-Achse ist **nicht** die Bildschirm-Achse — das Feld
wird transponiert (um die Diagonale gespiegelt) dargestellt:

| intern | Bildschirm |
|--------|-----------|
| `row` 0→11 | horizontal **links→rechts** |
| `col` 0→12 | vertikal **oben→unten** |
| Richtung `Left`  | wirft **oben** |
| Richtung `Down`  | wirft **rechts** |
| Richtung `Up`    | wirft links |
| Richtung `Right` | wirft unten |

**Anker (alle konsistent):**
- Maschine intern `Left` → Spieler sieht Wurf nach oben
- Maschine intern `Down` → Wurf nach rechts
- gefangener ZB intern `(3,4)` → Spieler: "oben-links"
- rechte Insel intern `(10,11)` → Spieler: "unten-rechts"

Umgesetzt in `BubblewonderRenderer.MachineLocation`. **Wichtig für jede neue
UI-Lage-/Richtungs-Angabe:** immer durch diese Transposition übersetzen, nie
row/col direkt als oben/unten/links/rechts ausgeben.

## Layout-Aufbau: REGS-Resource, Cell-Position, Maschinen (2026-05-29, Ghidra-verifiziert)

> **Anlass:** 16606 empfahl Pool-ZBs über die Insel-Maschine → ZB stirbt. Tiefe
> Ghidra-Analyse der Board-Init-Kette, um die Maschinen-Spawn-Positionen statt per
> Heuristik (x-Rang, Eckzone) sauber abzuleiten. Skripte: `scratch/ghidra_scripts/
> Decomp{Diff4Builder,BoardSetup,CellPlace,RegsLoader,CellPixel}.java` →
> `/tmp/ghidra_zoombini/O_*.txt`.

### Routing-Mechanik (verifiziert, fn `0x42abe0`)
Jump-Table `0x42adb0` über ZB-Feld `+0x58` (Richtung): **0=Left (col−1), 1=Down
(row+1), 2=Right (col+1), 3=Up (row−1)**, Clamp auf `[0, 0xc]`. Bei Eintritt in eine
Sticky-Zelle (celltype 5): gefangener ZB (`+0x86`) wird in Befreiungs-Queue `0x49a2d0`
geschoben + dessen `+0x58` gesetzt. → `BubblewonderRunner.cs:97-100` stimmt **exakt**.
**Routing ist nicht die Fehlerquelle.**

### Cell-Grid-Position = (F1, F2) (verifiziert, fn `0x427710`)
Die Cell-Erstellung kopiert pro REGS-Record 10 Words nach Bubble `+0x6c..` und schreibt
`0x499efc[bubble[+0x6e]*13 + bubble[+0x70]] = bubble[+0x6c]`. Also **Grid-Position =
F1·13 + F2** (REGS word1/word2 = `+0x6e/+0x70`), celltype = F0. **Achtung:** das ist
**nicht** `+0x72/+0x74` (= F3/F4, separate Properties). `RegsRecord.PositionIndex =>
F1*13+F2` (RegsRecord.cs) ist damit korrekt. Verifiziert: REGS-16608-Records reproduzieren
die Live-Conditional- **und** Switch-Positionen aus memdump-100217 zu **100 %**.

### REGS-Loader = reiner Byte-Swap (fn `0x447c90`)
Lädt die `REGS`-Resource und byte-swappt jedes Word (BE→LE) — **keine Reorganisation**.
Damit ist die Disk-Resource (big-endian, via `mohawk_parser`) logisch identisch zur
Speicher-Version → REGS-Records sind offline direkt nutzbar für alle Cell-Positionen/
-Typen/-Richtungen. (Hinweis: die angezeigte REGS-ID ≠ tatsächlich geladene Variante —
Variations-Counter `0x49b09e` wählt; memdump „16606" lud faktisch das 16608-Layout.)

### Maschinen: Definition statisch, Spawn-Zelle ist Laufzeit (fn `0x4249fd`)
Board-Init erstellt Maschinen aus den **REGS-Header-Words** (`*(0x49aba8 + 2..18)`, bis
9 Defs): 16608 = `[3,5,8]`, 16606 = `[2,4,12]`. Pro Def `d`:
- Maschinen-Sprite = **`SCRB 7000+d`**, Record (BE, 12 B) = `[type=2, dirCode, pixelX,
  pixelY, …]`. **Verifiziert** gegen Dump: SCRB 7007=(dir2,90,93) ↔ Maschine 0x0016 Down;
  7004=(dir2,193,309); 7002=(dir1,178,292). dirCode 1=Left/2=Down/3=Right/4=Up.
- `+0x8a` (TargetIdx) = `0x48d674[d-1]` = **Pool-Gruppe**: `==1` → eigene Ziel-Bubble
  (= Insel-Spawn), `==0` → Hauptpool-Werfer. So ist die Insel-Maschine **exakt** erkennbar
  (16608 slots [2,4,7] → TargetIdx [0,0,1] → slot 7 = Insel).
- **Cell→Pixel-Tabelle = REGS-Resource `16000`** (676 B = 169 Einträge 13×13, je (x,y)
  int16 BE; nach `0x49a20c`).

**Definitiver Negativbefund:** Maschinen-Sprite-Pixel → Standort-Zelle **≠ ZB-Spawn-Zelle**,
auch mit der perspektivisch exakten Tabelle. 16608: Sprite-Pixel mappen auf Standorte
**(2,10)/(2,11)/(1,0)** (Rand/Stein), die echten Spawn-Zellen sind **(1,8)/(5,10)/(10,9)**.
Die Spawn-Zelle ist **keine statische Größe** — sie entsteht aus einer Laufzeit-Verkettung
(Maschine → TargetIdx → `ActionTargetHandles 0x49a820` → Ziel-Bubble → deren Position).
**Folge:** Sprite-Pixel können die Spawn-Zelle prinzipiell nicht liefern (erklärt den
gescheiterten Tabellen-Ansatz). Die Spawn-Zelle wird live aus dem ersten ZB-Pfad gelernt
(Hawk-Modus) — der einzig mögliche Weg, und zuverlässig, da Routing + Cell-Position
verifiziert sind.

### Launch-Mechanismus + Insel-Spawn (Ghidra 2026-05-30)

**Anlass:** Der Solver schickte einen von der Insel re-gelaunchten ZB in den Tod, weil
seine angenommene Insel-Spawn-Zelle falsch war (Vorhersage „überlebt", real Falle).

**Decompiliert (`scratch/ghidra_scripts/DecompLaunchSpawn.java` + `ForceCallbacksAsFunctions.java`):**
- **Action-Slot-Callback `FUN_00426bc0`** (Message-Dispatch). Der Wieder-Losschick-Fall
  (`case 0x40/0x4a/0x54`) reiht `slot.+0x94` (ZB-Handle) in die **Launch-Queue** `0x49a448`
  (Count `0x49a414`) ein. `case 0x32` löst `ActionTargetHandles[slot.+0x8a]` (0x49a820) zur
  Ziel-Bubble auf — das ist aber NUR die **Maschinen-Feuer-Animation** (`FUN_00456a60`/`456cb0`,
  Sprite), NICHT die Spawn-Zelle. (Empirisch: `ActionTargetHandles=[0x1E,0x13]`, diese Bubbles
  liegen bei (0,0)/y=22 — nicht an der Spawn-Zelle.)
- **Die ZB-Grid-Zelle (`+0x72`/`+0x74`) wird NICHT im Bubblewonder-Code (0x424–0x42c)
  geschrieben**, sondern ausschließlich von **Engine-Bewegungsfunktionen bei `0x46xxxx`**
  (Sprite/Movement), während der ZB Zelle für Zelle läuft.

**Folge (Code-belegt):** Es gibt **keine einfache statische Spawn-Zellen-Formel** — die
Spawn-Zelle entsteht aus einer Laufzeit-Kette (Slot-Feuer → Queue → Engine-Movement). ABER:

**Die Spawn-Zellen sind KONSTANT pro REGS-Layout** (Geometrie ist statisch). Cross-Dump-
Beleg (20+ Dumps):

| REGS | Werfer-Maschinen (Spawn-Pos) | Insel-Maschine (Re-Launch-Spawn) |
|------|------------------------------|----------------------------------|
| 16606 | (2,8)=34, (5,11)=76 | **(4,1)=53** (memdump-173112, Event-Log) |
| 16608 | (5,10)=75, (1,8)=21, (10,9)=139 | (noch nicht sauber isoliert) |

**KORREKTUR-BEFUND:** Das hardcoded `BubblewonderSpawnMappings[16606] = {34,76,41}` hatte die
**Insel-Maschine auf (3,2)=41 — FALSCH**. Real ist sie **(4,1)=53** (Re-Launch-ZB startete
laut Event-Log bei Pos 53 → (4,2) → … → (4,5)=Falle). Mit der falschen Zelle (3,2) simulierte
der Solver eine andere (überlebende) Route → tödliche Fehlempfehlung. Die Werfer-Werte
(2,8)/(5,11) stimmen mit den Beobachtungen.

**ZUSATZ-PROBLEM:** Die Insel-Erkennung `IsIslandCell(pos)` ist eine **Eckzonen-Heuristik**
(`row≤3 && col≤3` ODER `row≥9 && col≥9`). (4,1) hat row=4 → fällt RAUS. Die Heuristik kann
die echte Insel-Spawn-Zelle also nicht als Insel erkennen → die Insel-Zelle muss **explizit
pro REGS markiert** werden, nicht geometrisch geraten.

**Weg drumrum (erster Versuch):** Spawn-Zellen pro REGS mit explizit markierter Insel-Zelle.
16606-Insel zunächst auf (4,1) korrigiert (1× beobachtet) — **siehe aber Daten-Mining unten,
das diesen Einzelwert relativiert.**

### Daten-Mining über 283 Dumps (2026-05-30)

Aggregation der **echten Pfad-Anfänge** (erste aktivierte Position pro ZB aus den
Event-Timelines) über alle verfügbaren Bubblewonder-Dumps:

| REGS | Werfer (stabil, hohe Counts) | Insel-Re-Launch-Kandidaten |
|------|------------------------------|----------------------------|
| 16606 | (2,8)=34 (95×), (5,11)=76 (20×) | **(3,2)=41 (4×)** vs **(4,1)=53 (1×)** — WIDERSPRUCH |
| 16608 | (5,10)=75 (76×), (1,8)=21 (51×), (10,9)=139 (6×) | (10,4)=134, (11,4)=147 — unklar |

**ZENTRALER BEFUND:** Die **Werfer-Spawns sind rock-solid konstant pro REGS-ID** (hunderte
konsistente Beobachtungen). Der **Insel-Re-Launch-Spawn ist es NICHT** — für 16606 zeigen die
Pfade ZWEI verschiedene Spawn-Zellen mit verschiedenen Routen:
- (3,2)→(3,3)… in 4 Dumps (174518/183226/203329/210742)
- (4,1)→(4,2)→…→(4,5)=Falle in 1 Dump (173112, variant=0)

Da die Werfer über alle Dumps gleich bleiben, ändert sich nicht das ganze Layout — nur der
**Insel-Spawn variiert** (sehr wahrscheinlich an den **Variation-Counter** gebunden:
`BubblewonderState.Variant`; REGS-ID ≠ Layout). **Folge:** Ein einzelner hardcoded Insel-Wert
pro REGS-ID ist GRUNDSÄTZLICH falsch — egal ob (3,2) oder (4,1). Genau das hat den ZB-Tod
verursacht (Solver simulierte ab falscher Insel-Zelle → Vorhersage „überlebt", real Falle).

**RICHTIGER WEG (umgesetzt):** Insel-Spawn NICHT raten. Live beobachten und **pro (REGS,
Variant) persistieren** (`BubblewonderSpawnStore`, Datei neben der EXE) — die Live-Beobachtung
ist immer korrekt (= Realität), und nach dem ersten Durchlauf einer (REGS,Variant)-Kombination
ist sie dauerhaft bekannt. Werfer bleiben hardcoded (verifiziert konstant); der Insel-Spawn
kommt ausschließlich aus Beobachtung/Persistenz. Vor der ersten Beobachtung einer Insel-
Maschine wird NICHT durch sie geroutet (kein tödliches Raten mehr).

## „Folge dem Plan → trotzdem Abweichung": Wurzel ist ein ROUTING-Fehler, NICHT Switch-Lesen (2026-06-03)

> **Anlass:** Plan-Zeiger fror im Live-Log fest + „Abweichung", obwohl der User dem Plan folgte.

### Zwischen-Irrtum (dokumentiert, damit er nicht wiederkehrt)
Erste Ghidra-Hypothese war: Der Simulator liest den Switch-Zustand aus Bubble-`+0x7C` (=
*initiale* Lade-Richtung), die Engine hält den echten Zustand in der Aggregator-Bitmap
`heap+0x52..0x54` (geschrieben von `FUN_0042B8C0`, gelesen von `FUN_004219F0`) → `+0x7C` sei
stale. **Das war FALSCH** — widerlegt durch einen kontrollierten Vorher/Nachher-Dump:

| | `+0x7C` per-Zelle (Sim liest das) | Bitmap `0x52..0x54` |
|---|---|---|
| vorher (`memdump-102900`) | (0,4)=0 (1,8)=0 (3,1)=1 | A=01 B=01 C=0010 |
| nachher (`memdump-102917`, 1 Switch umgelegt) | (0,4)=**2** (1,8)=**2** (3,1)=**2** | A=01 B=01 C=0010 — **unverändert** |

→ **`+0x7C` ist live & korrekt; die Bitmap ändert sich NICHT beim Umschalten.** Unser
Switch-Lesen ist richtig. Die Bitmap ist (entgegen der ersten These) NICHT die Routing-Quelle —
vermutlich kumulativ/Einmal-Effekte. **Lehre:** erst der empirische Test (nicht die Decompile-
Interpretation) hat es entschieden — CLAUDE.md-Regel #5/#10.

### Die echte Wurzel (Live-Log + SIM-TRACE, gleiche Runde)
Die bestätigte Abweichung (`bubblewonder-plan.log` 10:29:14) zeigt die Divergenz exakt:
```
PLAN-BASIS-SIG: …2770x1,2772x1… |I|F| 4:0, 21:0, 40:1, 89:0,115:1
AKTUELLE  SIG:  …     2772x1… |I|F| 4:2, 21:2, 40:2, 89:0,115:1
```
ZB `2770` losgeschickt (raus aus Pool) **und** die drei Channel-4-Switches pos 4/21/40
(=(0,4)/(1,8)/(3,1)) gingen 0/0/1→2/2/2 (Channel-4-Trigger sitzt auf **(6,10)**, REGS f3=4).
Die Re-Simulation des Plan-Schritts reproduziert diese Switch-Stellung **nicht** → zu Recht
„Abweichung". Switch-Zustände **gehören** in die Signatur (User-Prinzip bestätigt).

**Warum reproduziert der Sim es nicht — der eigentliche Bug:** Die SIM-TRACE im selben Dump
flaggt es selbst. **Jeder** von Maschine **M0@(2,8)** gestartete ZB läuft im Simulator stur
Spalte 8 hinunter `(2,8)→(3,8)→…→(11,8)` und **aus dem Grid** — getaggt
`⚠ LÄUFT AUS DEM GRID (Routing-Bug)`. Dadurch erreichen die simulierten Pfade die realen
Routing-/Trigger-Zellen nicht → der Sim sagt weder Channel-Flips noch Überlebenswege voraus.
Folge: der neu gerechnete Plan war **„0 rettbar, DFS (time-limit)"** (10:30:15) für ein
Board, das der Mensch lösen kann. (M1@(5,11)-Pfade laufen dagegen korrekt durch (6,10).)

**Schlussfolgerung:** Switch-Mismatch in der Signatur ist ein **Folgesymptom** eines
**Routing-Modell-Fehlers** — ZBs ab (mind.) M0 werden falsch geroutet (geradeaus aus dem
Grid). Das ist die zu fixende Wurzel, nicht das Switch-Lesen.

### PINPOINT (2026-06-03, `memdump-152546`, 16608): Richtungstabelle verdreht
Realer Tracker-Pfad ZB 0x0012 (4,1,4,1): `(1,8)→(1,7)→(1,6)→(1,5)→(1,4)→(1,3)=Falle` — läuft an
Switch (1,7) **geradeaus links** weiter. Switch (1,7) `state=3`. **Sim routet `state=3`→Up**
(→(0,7), Dead). Code: `BubblewonderGridModel.cs:70` `FBitToDirection = {0=Left,1=Down,2=Right,3=Up}`.
Echte Konvention (= Memory-Map `+0x7C`-Doku): **`{0=Up,1=Right,2=Down,3=Left}`** — genau
**umgekehrt**. `state=3`→Left (✓ real), Sim liefert Up (✗).

**Querprobe (alle 5 Switches im Dump):** unter `{0=Up,1=Right,2=Down,3=Left}` ist der jeweilige
`state` stets eine **aktive** Richtung ((1,7)s3=Left, (2,7)s1=Right, (7,11)s3=Left, (10,6)s0=Up,
(10,9)s0=Up) — durchgängig konsistent. Zweite kaputte Spur stützt es: M1-ZBs real unten bei
Sticky (10,5)/(11,5), Sim verendet oben bei (5,11); Deflektor (7,10) `f8=2` → Sim **Right**,
korrekt **Down** (Richtung Boden). → **Verdrehte Tabelle = jeder Switch/Deflektor lenkt falsch =
„0 rettbar" bei lösbarem Board.**

**KORREKTUR des Pinpoints (gleiche Session, beobachtete Routing-Übergänge):** Das „Tabelle global
drehen" war FALSCH — der **beobachtete** Übergang an Deflektor (11,6) (Bit0 → Ausgang **Left**)
bestätigt `FBitToDirection[0]=Left`; ein Drehen hätte Deflektoren gebrochen. Die Bit→Richtungs-
Tabelle ist also **korrekt**. Der Bug sitzt enger: in der **Switch-`state`→Ausgang-Logik**
(`DirectionAtStateIndex` nutzt `FBitToDirection[state]`, was für (1,7) state=3 fälschlich Up
liefert statt real Left).

**Beobachtete Switch-Übergänge (Abschnitt „Routing-Beobachtungen pro Cell", mehrere Dumps) —
passen zu KEINER simplen `state→Richtung`-Tabelle:**

| Switch | aktive Bits (FBitToDir) | state | Eintritt | realer Ausgang |
|--------|--------------------------|-------|----------|----------------|
| (7,11) | {Down,Up}                | 1     | Right    | **Down** (bit1) |
| (1,7)  | {Left,Down,Up}           | 3     | Left     | **Left** (bit0) |
| (6,11) | {Left,Down,Right}        | 0     | Down     | **Left** (bit0) |
| (10,6) | {Left,Down}              | 0     | Left     | **Left/Down** (nicht-det.) |

state 0→Left, 1→Down, 3→Left → keine Index-Formel passt; evtl. spielt Eintrittsrichtung und/oder
die round-robin-Reihenfolge der aktiven Bits mit hinein.

### Disassembly der Switch-Mechanik (Ghidra, 2026-06-03)
- **`sw_0042ABE0` (ZB-Bewegung):** `switch(zb+0x58)` mit `{0:col−1=Left, 1:row+1=Down, 2:col+1=Right,
  3:row−1=Up}`, Clamp [0,0xc]. **Bestätigt `FBitToDirection` endgültig** (Bewegung).
- **`sw_0042A950` (Switch-Zyklus bei Channel-Trigger):** für jeden Switch (celltype `+0x6c`==4) im
  Channel: `state(+0x7C) = (state+1)`, `if(state>3) state=0`; dann `while(*(obj+state*2+0x74)==0)`
  weiter inkrementieren → `state` landet auf nächstem **aktiven** Slot. Slots `+0x74/76/78/7A` =
  aktive-Richtungs-Flags (0=inaktiv). Setzt `obj+0x4a=3`.
- **Cell-Entry (`sw_0042ABE0` Kopf):** `zb+0x58 = cell+0x4c` **unter Match-Bedingung**
  (`*(cell+0x54) == f(zb-attr)`) — das ist der Conditional-Pfad; wo der **Switch** `zb+0x58` setzt
  (aus `state`/Slot) ist noch nicht gelesen.

### BESTÄTIGTE WURZEL der ständigen „Abweichung" (2026-06-03, Signatur-Diff): Klebefallen-Stub
Plan-Log druckt bei jeder bestätigten Abweichung `PLAN-BASIS-SIG` vs `AKTUELLE SIG`. Diff eines
Paares im selben Layout (15:51:59):
- Pool: ZB `2837` weg (= ausgeführter Launch-Schritt, erwartet).
- Switches `4/21/40: 0/0/1→2/2/2` (= korrektes Channel-4-Zyklen, verifiziert).
- **Falle: `F0x1 → F0x2`, beide gefangenen ZBs mit CanonicalSig `0`.**

`Sig 0` = der Stub `(0,0,0,0)` aus `BubblewonderGridModelBuilder.BuildGridState:462` (live gelesene
Sticky-Zelle hält in `+0x86` nur einen Engine-Handle, keine Attribute). Die **Vorwärts-Simulation**
(`HandleSticky`) legt den gefangenen ZB dagegen mit **echten** Attributen ab. → Die `F`-Komponente
der `FullStateSignature` (zählt Trapped nach `CanonicalSig`/Attributen) matcht **nie**, sobald ein
ZB in einer Klebefalle landet → **Abweichung bei JEDEM gefangenen ZB**, obwohl Pool + Switches
korrekt vorhergesagt sind. `WithStickyAttributes` füllt den Stub nur, wenn der ZB genau als
`Launched` auf der Sticky-Zelle gelesen wird — sonst bleibt Sig 0. **DAS ist die Wurzel der
Plan-Instabilität (ständiges Neurechnen) — NICHT Switches/Pathing (beide verifiziert korrekt).**

**FIX-PLAN:** In `FullStateSignature` die `F`-Komponente (Klebefallen) **positions-basiert statt
attribut-basiert** kodieren (welche Sticky-Zellen belegt sind), ODER trapped-ZBs auf BEIDEN Seiten
(live + forward-sim) gleich darstellen (Stub), damit live-Read und Re-Simulation übereinstimmen.
Positions-basiert erkennt echte Abweichung weiter (andere Zelle belegt) und ist stabil (Positionen
werden zuverlässig gelesen). Validieren: Abweichung darf bei reinem Trap-Ereignis NICHT mehr feuern,
echte Abweichung (anderer ZB/Zelle) weiterhin schon. Beleg: Log 15:51:59 BASIS vs AKTUELLE.

### AUFLÖSUNG (2026-06-03, Vorher/Nachher-Test memdump-155110→155127): Switch-System ist KORREKT
Kontrollierter Test (ZB läuft auf **Trigger** (6,10) f3=4, der **entfernte** Channel-4-Schalter
umlegt — NICHT im direkten Laufweg). Beobachtete state-Änderung: (0,4) 0→2, (1,8) 0→2, (3,1) 1→2.
Gegen die dekompilierte Engine-Regel (state++ mod4, inaktive Slots `*(obj+state*2+0x74)==0`
überspringen, Slot-Index = `FBitToDirection`-Bit) gerechnet:
(0,4) 0→(1 skip Down)→2=Right ✓ · (1,8) ✓ · (3,1) 1→2=Right ✓ — **exakte Übereinstimmung**.
Plus direkter Switch-Redirect (6,11) Down→Left bei state=0 = `FBitToDirection[0]=Left` ✓.
→ **Richtungstabelle UND Zyklus-Modell des Simulators sind VERIFIZIERT KORREKT.** Der frühere
„(1,7) state3→Up"-Widerspruch war ein **Snapshot-Artefakt** (state zur Dump-Zeit ≠ state zur
Durchlauf-Zeit, da der Switch dazwischen zykelte). **Switches sind NICHT der Bug.**

### ECHTE offene Wurzel: PATHING (Live-Log bubblewonder-plan.log)
Trotz korrektem Switch-System feuert live bei **fast jedem Zug** „ABWEICHUNG (bestätigt)" + oft
**„0 rettbar" als `DFS (optimal)`** (Suche abgeschlossen → Modell hält Board für unlösbar). Da
Switches stimmen, muss die **ZB-Pfad-/Outcome-Vorhersage** falsch sein: der simulierte Pfad
trifft andere Zellen/Trigger als der reale → Switch-Stellungen nach einem Schritt stimmen nicht
→ Signatur-Mismatch → Abweichung; und Wege zu Zielen werden nicht gefunden → „0 rettbar". Konkret
weiter verdächtig: M0@(2,8)-ZBs laufen im Sim geradeaus Spalte 8 aus dem Grid (Dump-Flag
„umlenkende Zelle fehlt"), Conditional-/Spawn-Behandlung. **NÄCHSTER SCHRITT:** EINEN realen
vollständigen ZB-Pfad (Zellsequenz Launch→Endpunkt, aus Event-Timeline) gegen den Sim-Pfad
diffen → erste abweichende Zelle = der Pathing-Bug. **KEIN Switch-Fix nötig.**

**WICHTIGE EINSCHRÄNKUNG der Beobachtungstabelle (Denkfehler korrigiert):** Der dort genutzte
`state` ist der **Dump-Snapshot**, NICHT der state, den der ZB beim **Durchlaufen** hatte. Switches
zyklen bei jedem Channel-Trigger (`sw_0042A950`) — also kann der state zwischen Durchlauf und Dump
gewechselt haben. Gegencheck mit `exit = FBitToDirection[state]` (= aktuelles Sim-Modell):
(6,11) s0→Left ✓, (7,11) s1→Down ✓, (1,7) **snapshot** s3→Left passt NICHT zu `[3]=Up` — **aber**
wenn 0x0012 bei state 0 durchlief, ist es konsistent. → **Die Richtungs-Tabelle ist möglicherweise
doch korrekt**; der wahre Fehler liegt evtl. darin, dass der **Switch-`state` im Solver-Modell ≠
dem realen state beim Durchlaufen** ist (Trigger-Timing/-Reihenfolge), oder die M0-Pfade aus
anderem Grund scheitern. **Status: NICHT gelöst, NICHT verifiziert.** Mehrere Hypothesen wurden
durch Messung/Disasm verworfen (Switch-Stale-Read; globales Tabelle-Drehen; simple state→dir-Tabelle).
NÄCHSTER SCHRITT: realen Switch-`state`-Verlauf während eines ZB-Durchlaufs gegen das Sim-Modell
prüfen (z.B. Dump direkt nach EINEM Switch-ZB-Durchlauf, state + tatsächlich genommener Pfad).
**KEIN Code-Change** in dieser Session. Skript: `scratch/ghidra_scripts/DecompSwitchRouting.java`.

## „0 rettbar (Zeitlimit)" = Modell-Befund, nicht Solver-Schwäche (2026-06-04)

**Anlass:** Auf manchen frischen Boards meldete der Solver `0 rettbar, N Schritte
(DFS (time-limit), memo≈38k)` (z.B. plan-log 21:12:38). Da reale Boards ohne
Spielerfehler **zu 100% lösbar** sind (User-Prinzip), kann „0 rettbar" auf einem
frischen Board NICHT korrekt sein — es ist ein Symptom, kein Ergebnis.

**Untersuchung (verifiziert über synthetische Reproducer + Diff gegen die echten
DFS-Komponenten, nicht nur Plausibilität):**

- Lösbare Boards werden korrekt + schnell gelöst, sobald die Reachability einen
  Gradienten liefert: 16608 → Beam findet 16/16 (~6 s), DFS bestätigt optimal;
  synthetische „N-Gate"-Boards (Setup-ZBs sterben) → optimal in ms.
- Der Hänger entsteht NUR, wenn **kein scorender Zustand erreichbar** ist, aber
  die DFS-Schranke `scorableSigs` das nicht erkennt: `ComputeScorableSigs`
  probiert **alle** Switch-Stellungen durch und ignoriert, ob ein Trigger sie je
  herstellt (Über-Approximation). Hält sie eine Stellung für scorbar, die real
  unerreichbar ist (z.B. Goal-Switch in einem Kanal OHNE Trigger), bleibt
  `upperRemaining` hoch → bei `best=0` senkt die obere Schranke nichts → **kein
  Pruning** → der DFS durchforstet 60 s lang den (oft durch Decoy-Switches
  aufgeblähten) Suchraum, findet nie einen Survivor, meldet `0 (time-limit)`.
- Reproduziert in `SolveDfs_UnreachableGoalConfig_TerminatesFast_NotTimeLimit`:
  vor dem Fix `memo=4416, Knoten=39.745` (vgl. live `memo=38316`).

**Diagnose:** „0 rettbar" auf einem frischen Board ⇒ **im Modell ist kein Ziel
erreichbar** ⇒ das Layout wurde falsch gelesen (z.B. Goal-Zelle nicht erkannt,
Switch/Conditional/Channel falsch dekodiert, Insel-Spawn nicht gelernt). Es ist
**kein** Solver-Performance-Problem.

**Fix (BubblewonderSolver.SolveDfs + BubblewonderReachability):** Bevor der DFS
startet (und nur wenn Greedy=0), läuft die Reachability-BFS. Hat sie den
erreichbaren Zustandsraum **vollständig** erkundet (`Complete==true`, nicht am
`maxStates`-Deckel abgebrochen) und **keinen** scorenden Zustand gefunden, ist das
ein belastbarer Beweis (Über-Approximation mit unbegrenzten ZBs erreicht ≥ alle
realen Zustände): Board im Modell unlösbar → SOFORT `0` mit Strategy
`"kein Ziel erreichbar im Modell (erkundet=N)"`, statt 60 s zu verbrennen. Der
Worker zeigt dann `⚠ Kein Ziel im Modell erreichbar — Layout-Lesefehler?` statt
des irreführenden `⏱ Zeitlimit`. Bei gedeckelter BFS (`Complete==false`) wird
NICHT gegated (kein Schluss möglich) → normaler DFS.

**Soundness:** Die Reachability nutzt die echte `BubblewonderRunner`-Mechanik und
reduziert ZBs nur auf ihre routing-relevante Signatur, die als unbegrenzt
verfügbar gilt — sie kann also nur MEHR Zustände erreichen als der reale Pool.
Erreicht selbst sie keinen scorenden Zustand, gibt es real keinen → der Gate
schneidet nie eine echte Lösung weg (Gegenprobe:
`SolveDfs_DoesNotFalselyGate_WhenGoalReachableViaSwitchFlip`).

**Offen / nächster Schritt:** Tritt der `⚠ Kein Ziel im Modell erreichbar`-Befund
live auf, ist die WURZEL im **Layout-Lesen** zu suchen (Memdump im Moment ziehen,
gelesene Cells/Switches/Goal gegen das sichtbare Board prüfen) — nicht im Solver.

## ★ Meilenstein: erste verifizierte 16/16-Runde (2026-06-05)

REGS 16608 komplett gelöst — `memdump-135217`: alle 16 ZBs (Header 0x03–0x12) `→ Scored`
bei (10,1)/(10,2), **0 NotScored, 0 Tote**.

**Wichtigste Lektion daraus:** Routing und Mechanik des **Simulators waren die ganze Zeit
korrekt** (Hawk-Modus verifizierte jeden Pfad exakt). Die Bugs, die Runden scheitern ließen,
saßen NICHT im Simulator, sondern in der **Klassifikation / Signatur / Anzeige** der UI:

1. **Fehl-Abweichungs-Kaskade** (jede löste unnötige 60s-Neuberechnung aus, die oft nur
   N<16 fand): Phantom-Insel-Doppelzählung (stale `+0x76`), Insel-Maschinen-Zelle im
   Switch-Vergleich, Insel-ZB Stub-Attribute, Mid-Walk-Halbzustand. → alle behoben.
2. **Abweichungs-Erkennung zu aggressiv**: vergleicht einen ständig-im-Update-Live-Zustand
   exakt mit dem Modell → Race-Mismatches an jedem Übergang (Switch/Sticky/Insel). → jetzt
   konservativ (3,5 s Debounce + „kein ZB unterwegs", In-Transit-Halt).
3. **Richtungs-Label** „läuft nach X": rechnete die Richtung aus der NETTO-Pfad-Verschiebung
   (Anfang→Ende) statt aus dem **ersten Zug** (= sichtbare Wurf-Richtung). Belegt
   (`memdump-080136`): die empfohlene, **scorende** Maschine M0(1,8) wirft nach OBEN, läuft
   dann lang runter zum Ziel → Netto „rechts" → Label „nach rechts" → der Spieler nahm die
   RECHTE (Todes-)Maschine. Fix: `EffectivePathDirection` nutzt `path[0]→path[1]`. Bewiesen
   per Reconstruction-Test (M0=Scored/Label-neu „oben"; M1=Dead/„rechts").

**Methoden-Lehre (hart gelernt):** NICHT reflexhaft Nutzer-Beobachtungen zustimmen —
mit Reconstruction/Simulation **beweisen** (echte Switch-Stände + ZB-Attribute aus dem Dump),
bevor man einen Fix als richtig erklärt. Bildschirm-Transposition: intern Up=links,
Down=rechts, Left=oben, Right=unten (BubblewonderRenderer `MachineLocation`).

## Verifizierte Memory-Adressen / Offsets

Vollständig in `src/ZoombiniHelper.Core/Bubblewonder/BubblewonderMemoryMap.cs`.
Alle Konstanten sind Live-verifiziert oder per Disasm-Pattern-Match bestätigt.

Wichtigste Offsets pro Bubble-Object:

| Offset | Inhalt |
|--------|--------|
| `+0x1A` | Hdr1A (= eindeutiger Cell-Identifier in der Engine-Liste) |
| `+0x6C..+0x7F` | REGS-Record-Copy (10 LE-Words) |
| `+0x72` | prop1 (= F3, Engine-Property A) |
| `+0x74` | prop2 (= F4, Engine-Property B) |
| `+0x7C` | Switch-State-Index (0=Up, 1=Right, 2=Down, 3=Left) |
| `+0x82` | Conditional-Attribut-Code (1=Hair, 2=Eyes, 3=Nose, 4=Feet) |
| `+0x84` | Conditional-Variant (1..5) |
| `+0x86` | Sticky-Trapped-ZB-Hdr1A (0=leer) |
| `+0x166` | Trigger-Target-Switch-Hdr1A |
| `+0xC8` | Cell-State (3=ready für Match) |
| `+0xE0` | Animation-Event-Type |
| `+0xE2` | Primary Active-Flag |
| `+0x12C` | Secondary Active-Flag |

## REGS-Resource-IDs

Per Mohawk-Resource-Loader pro Difficulty:

| Diff | REGS-IDs | # Maschinen | Insel? |
|------|----------|-------------|--------|
| 1 | 16600, 16601 | 2 | nein |
| 2 | 16602, 16603 | 1-2 | nein |
| 3 | 16604, 16605 | 1 | nein |
| 4 | 16606, 16607, 16608 | 3 | **ja** |
| 5 (Bonus) | 16609 | ? | ? |

REGS 16607 noch nicht live beobachtet — Engine wählt sie nur zufällig in Diff 4.

## Quellen / Konfidenz

- **Verifiziert (Live + Code):** Cell-Type-Klassifikation per f0, Conditional-
  Attribut/Variant per +0x82/+0x84, Switch-State per +0x7C, Trigger-Target per
  +0x166, Sticky-Trapped per +0x86, Spawn-Maschinen per Hardcoded-Mapping
  über 35+ Live-Dumps.
- **Verifiziert (User-Testimony 2026-05-03):** Gesamte Spielmechanik
  (Determinismus, Insel-Konzept, Sticky-Channel-Pairing, Switch-via-Trigger).
- **Verifiziert (Live + Disasm 2026-05-26):**
  - **Switch-Toggle channel-basiert (F3):** ein Trigger schaltet ALLE Switches
    mit gleichem F3 round-robin durch deren aktive F4..F7-Bits. Live bestätigt:
    Trigger F3=4 legte beide F3=4-Switches um (pos 102: ↓Down→↑Up, pos 139:
    ↑Up→←Left), kein anderer Switch betroffen. `+0x166`-Pointer-Modell widerlegt
    (zeigte 75% ins Leere). Deckt 1..4 aktive Richtungen ab (3-Wege live gesehen).
  - **ZB-Outcome via `+0x76`:** `==3` = auf Steininsel (echtes Score), sonst
    nicht durch. Siehe Sektion oben.
- **Offen:** Sticky-Auswurf-Richtung (Liberation-Queue 0x49a2d0),
  Sticky-Befreier-Pass-Through.
