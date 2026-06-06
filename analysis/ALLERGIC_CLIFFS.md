# Allergic Cliffs — Mechanik (verifiziert aus Segment 23)

## Korrigierte Code-Lokalisierung

**Cliffs liegen in Segment 23** (NICHT Seg 28 = Hotel; auch NICHT Seg 20 wie zwischendurch vermutet — Seg 20 ist nur der `random_range`-Wrapper).

| Eigenschaft | Wert |
|-------------|------|
| Code-Segment | **23** |
| Datei-Offset | 0x16200 |
| Segment-Länge | 0x20CB = 8395 Bytes |
| Anzahl Funktionen | 11 |
| Relocations | 243 |
| Calls in Seg 14 (rand) direkt | 0 |
| Calls in Seg 20:0x04E8 (random_range) | **7** |

`random_range(min, max)` (Seg 20) ruft seinerseits `rand()` (Seg 14) auf — same pattern wie Caves.

## Funktionskarte (Seg 23)

| # | File-Offset | Seg-Rel | Größe | Rolle |
|---|-------------|---------|-------|-------|
| 1 | 0x162EA | 0x00EA | ~0x379 | ? (Helfer) |
| 2 | 0x16663 | 0x0463 | ~0xCB | ? |
| 3 | 0x1672E | 0x052E | ~0x63 | ? |
| 4 | 0x16791 | 0x0591 | ~0x57 | ? |
| 5 | 0x167E8 | 0x05E8 | ~0x4B5 | ? |
| 6 | 0x16C9D | 0x0A9D | 0x287 | **Action-Dispatcher** (Switch über Action-ID 1/2/3) — enthält den **Eval-Pfad** (case 3) |
| 7 | 0x16F24 | 0x0D24 | 0x13A | **Debug/Cheat-Print** — pusht "Upper bridge accepts:" / "Lower bridge accepts:" |
| 8 | 0x1705E | 0x0E5E | 0x10E | Event-Dispatcher (jump-table) |
| 9 | 0x1716C | 0x0F6C | 0x3B2 | ? |
| 10 | **0x1751E** | **0x131E** | 0xD17 | **INIT-Funktion** (3351 B — größte). Enthält Difficulty-Dispatch und alle 12 Schreibzugriffe auf Allergy-Type/Value-Arrays. |
| 11 | 0x18235 | 0x2035 | ~0x96 | Helfer (returnt was — wird vom Eval-Pfad gerufen) |

## Difficulty-Dispatch (in Init bei Seg23:0x1421)

```asm
mov bx, [0x79DA]          ; CLIFF_DIFFICULTY (0-3)
cmp bx, 3
ja  default               ; out of range → fallback
add bx, bx                ; bx *= 2
jmp [cs:bx + 0x2029]      ; jump-table dispatch
```

| Difficulty | Sprungziel (seg-rel) | File-Offset |
|------------|---------------------|-------------|
| **0** (Not So Easy)    | 0x1423 | 0x17623 |
| **1** (Oh So Hard)     | 0x14E0 | 0x176E0 |
| **2** (Very Hard)      | 0x17F9 | 0x179F9 |
| **3** (Very, Very Hard) | 0x18B9 | 0x17AB9 |

## DS-Variablen — Cliff State (0x79D4..0x7A38)

| Adresse | Bedeutung |
|---------|-----------|
| **0x79DA** | **Difficulty** (0–3) |
| 0x79D4 | engagement_flag (Klippen aktiv) |
| 0x79E0 | ready_flag (Klippe bereit, Zoombini zu prüfen) |
| 0x79E2 | attempt_counter (verglichen gegen 6 → Brücken stürzen ein bei zu vielen Fehlversuchen) |
| 0x79EC | far-Pointer auf Zoombini-Kontext (Eval-Eingang) |
| 0x79EE | cheat_flag |
| 0x79F2 | Zwischen-State (für Phase-Übergänge) |
| 0x79FC, 0x79FE | Array indexed by 0x7A08: erfasste Attribut-Werte |
| 0x7A00, 0x7A02 | Array indexed by 0x7A08: Zoombini-Index (zb_struct+0x1A) |
| 0x7A04, 0x7A06 | Array indexed by 0x7A08: Sprite-Handles |
| **0x7A08** | **phase_counter** (0..2): Slot-Zähler innerhalb einer Klippe |
| 0x7A0A | si-Wert (von FUNC #8 gesetzt) |
| **0x7A0E** | **which_cliff** (0 = Lower, 1 = Upper) |
| **0x7A10** | **num_allergies** — Anzahl aktiver Allergie-Regeln |
| **0x7A11..0x7A15** | **allergy_type[5]** — Attribut-Typen (0=Haare, 1=Augen, 2=Nase, 3=Füße) |
| **0x7A16..0x7A1A** | **allergy_value[5]** — Attribut-Werte (0–4) |
| 0x7A2C | secondary state flag |
| 0x7A2E, 0x7A30 | werden bei Cheat 'R' angezeigt |
| 0x7A36 | Vergleichswert in Eval |

## ⭐ Wie die Schwierigkeitsgrade WIRKLICH funktionieren

Aus dem Disassembly (file 0x017FB6..0x0181D2) — die Allergie-Speicherung:

### Internes Bitfield-Format

Die Init speichert die zufällig gewählte Allergie als **32-bit Bitfield** in `[bp-0x16]`,
mit 4 Bytes für die 4 Attribute:

| Byte | Attribut | TYPE-ID im Code |
|------|----------|------------------|
| Byte 0 (LSB) | Feet | 4 |
| Byte 1 | Nose | 3 |
| Byte 2 | Eyes | 2 |
| Byte 3 (MSB) | Hair | 1 |

Innerhalb jedes Bytes: niedrige 4 Bits = erster Wert, obere 4 Bits = zweiter Wert (nur Diff 1).

### Difficulty 0 — Ein Wert eines Typs

```
ALL_COUNT = 1
finde das einzige Byte != 0:
  ALL_TYPE[0] = (4=Feet, 3=Nose, 2=Eyes, oder 1=Hair je nachdem welches Byte)
  ALL_VAL[0]  = byte & 0xF
```
**Bedeutung:** Klippe akzeptiert Zoombinis mit **genau diesem einen Attribut-Wert**.
Beispiel: „Upper bridge accepts: Nose Red" → nur rote Nasen gehen oben durch, alle anderen unten.

### Difficulty 1 — Zwei Werte desselben Typs (ODER)

```
ALL_COUNT = 2
finde das einzige Byte != 0 (nur eines hat Werte):
  ALL_TYPE[0] = TYPE_FOR_BYTE; ALL_VAL[0] = byte & 0xF
  ALL_TYPE[1] = TYPE_FOR_BYTE; ALL_VAL[1] = (byte >> 4) & 0xF
```
**Bedeutung:** Klippe akzeptiert Zoombinis mit **VAL[0] oder VAL[1]** (ODER-Verknüpfung,
weil ein Zoombini nicht beide Werte gleichzeitig haben kann — gleicher Typ).
Beispiel: „accepts: Nose Red, Nose Green" → Rote ODER Grüne Nase.

### Difficulty 2 — Zwei Werte aus verschiedenen Typen (ODER, Vereinigung)

```
ALL_COUNT = 0
für jedes Byte != 0 (in Reihenfolge Feet → Nose → Eyes → Hair):
  ALL_TYPE[ALL_COUNT] = TYPE_FOR_BYTE
  ALL_VAL[ALL_COUNT]  = byte & 0xF
  ALL_COUNT++
  if ALL_COUNT >= DIFFICULTY: stop
```
**Bedeutung:** Klippe niest, wenn der Zoombini **mindestens EINEN** der aufgelisteten
Werte hat (ODER-Verknüpfung).
Beispiel: „accepts: Nose Red, Eyes Sunglasses" → niest bei roter Nase ODER bei Sonnenbrille
(oder beidem). **Akzeptanz nur**, wenn der Zoombini **weder** rote Nase **noch** Sonnenbrille hat.

### Difficulty 3 — Drei Werte aus verschiedenen Typen (ODER über drei)

Gleiche Logik wie Diff 2, aber 3 Slots werden befüllt.
**Bedeutung:** Klippe niest, wenn der Zoombini eines der drei Allergie-Attribute hat.
Beispiel: „accepts: Nose Red, Eyes Sunglasses, Feet Skates" → niest bei einer der drei
Eigenschaften. Akzeptanz nur, wenn keine der drei zutrifft.

### Match-Rate und Klippen-Verteilung

Die Match-Funktion (FUNC #11 @ 0x18235) durchläuft die Allergie-Liste und setzt
`match=1` bei der **ersten** zutreffenden Eigenschaft (`OR`-Logik). Die mathematische
Wahrscheinlichkeit, dass ein zufälliger Zoombini matched:

| Difficulty | Match-Rate | Verteilung bei 16 Zoombinis |
|------------|------------|------------------------------|
| 0 | 1/5 = **20 %** | ~3 vs ~13 (sehr asymmetrisch) |
| 1 | 2/5 = **40 %** | ~6 vs ~10 |
| 2 | 1 − (4/5)² = **36 %** | ~6 vs ~10 |
| 3 | 1 − (4/5)³ = **48.8 %** | **~8 vs ~8 — fast 50/50** |

→ Auf hohen Schwierigkeitsgraden ist die Verteilung tatsächlich nahezu **gleichverteilt**,
was empirisch beobachtet wird. Diff 3 mit drei ODER-verknüpften Werten erreicht fast genau
50 %, weil die Wahrscheinlichkeit, dass ein Zoombini *keinen* der drei Werte hat, bei
(4/5)³ ≈ 51 % liegt.

### Wieso die zwei Klippen sich „ergänzen"

Der Cheat-Print zeigt nur die Regel **einer** Klippe. Die andere Klippe akzeptiert genau
das Komplement.

`WHICH_CLIFF` (0x7A0E) wird bei Init zufällig auf 0 oder 1 gesetzt — bestimmt, welche
Klippe (Upper/Lower) den Match akzeptiert und welche das Komplement. Das Auswahl-Bit für
„Match → ACCEPT vs. Match → REJECT" steckt vermutlich in `[0x7A0C+2]` (`byte_2` der
Rule-Struktur) und wird beim Match-Helper umgeschaltet.

## Spielmechanik (so weit verifiziert)

### Initialisierung (einmal pro Level)

1. **Engine-Setup** (FUNC #10 Anfang):
   - 3 Tabellen aus DS:0x92E (40 B), DS:0x956 (24 B), DS:0x96E (24 B) auf Stack laden — vermutlich Statisch-Daten für Sprite-Layout / Allergie-Vorlagen
   - Zwei Far-Pointer-Allocations à 0x7D0 (2000) und 0x3E8 (1000) Bytes — Arbeitsspeicher
   - 500 dword-Slots in beiden Arrays nullen

2. **Difficulty-Dispatch** (Seg23:0x1421):
   - `bx = difficulty * 2`, `jmp [cs:bx+0x2029]`
   - 4 Pfade: einer pro Schwierigkeitsgrad
   - Jeder Pfad ruft mehrere `random_range()` für Allergie-Auswahl

3. **Allergie-Befüllung** (12 Schreibzugriffe pro Array):
   - 0x7A11..0x7A15 (5 Typen-Slots) und 0x7A16..0x7A1A (5 Wert-Slots) werden je nach Difficulty teilweise befüllt
   - 0x7A10 (count) wird auf die Anzahl aktiver Allergien gesetzt

### Zoombini-Evaluation (FUNC #6, case 3)

Pseudocode (vereinfacht aus Disassembly):
```c
// Pre-checks
if ([0x9EEA] > 0 && [0x79E0] == 0)   return;  // not ready
if ([0x79E2] >= 6)                    return;  // bridge collapsing
[0x79E0] = 1;                                  // mark active

zb_ptr = lcall_get_active_zoombini();          // far pointer
zb_status = zb_ptr->byte_0x124;
if (zb_status == 8)         return;             // skip
if (zb_ptr->byte_0x127 != 0) return;
if (zb_status == 9) {                           // special path
    if ([0x7A2C] && [0x79F2]) {
        [0x79F2] = 0;
    }
    [0x7A2C] = 0;
    return;
}

// Animate Zoombini onto bridge
lcall_play_animation(zb_ptr, ..., DS:0x8D0);
lcall_walk_to(zb_ptr+0xCE, ..., 8 bytes);

// Phase-state machine (which slot of the bridge is the Zoombini in)
phase = [0x7A08];
if (phase == 1 && zb_ptr->word_0x1A == [0x7A00]) {
    [0x7A08] = 0;       // reset
    accepted = 1;
}
else if (phase == 2) {
    if (zb_ptr->word_0x1A == [0x7A02]) [0x7A08] = 1;
    if (zb_ptr->word_0x1A == [0x7A00]) {
        // Shift slot 0 → backup
        [0x79FC] = [0x79FE];
        [0x7A00] = [0x7A02];
        [0x7A04] = [0x7A06];
        [0x7A08] = 1;
        accepted = 1;
    }
}

// THE ACTUAL ALLERGY CHECK happens via this lcall:
result = lcall_check_allergy(zb_ptr);   // calls into helper FUNC #11 or another segment
if (result != 0) {
    // ACCEPTED — store this Zoombini's data into next slot
    bx = [0x7A08] * 2;
    [0x79FC + bx] = result;
    [0x7A00 + bx] = zb_ptr->word_0x1A;
    feature = lcall_create_sprite(...);
    [0x7A04 + bx] = feature;
    [0x7A08]++;                          // advance to next slot
}
else {
    // REJECTED — bridge sneezes, Zoombini bounces back
    if (accepted_flag != 0)
        lcall_play_sneeze_animation(zb_ptr->coordinate);
}
```

### Wichtige Folgerung

**Es gibt KEINEN „first_time_flag → ACCEPT bypass".** Das war Hotel-Mechanik (Seg 28).
Bei Cliffs prüft `check_allergy()` jeden Zoombini gegen die in der Init festgelegten
Allergien (0x7A11..0x7A15 / 0x7A16..0x7A1A). **Der erste Zoombini kann ablehnt werden,**
genau wie der User empirisch beobachtet hat.

### Debug-Print (FUNC #7, Cheat-Code)

Bei Cheat-Code 'A' (Spieler tippt 'A' auf der Tastatur während Cliffs):
```c
print_zoombini_attributes(...);
print_difficulty(...);

// Loop über aktive Allergien
if ([0x7A0E])  printf("Upper bridge accepts:");
else           printf("Lower bridge accepts:");

for (si = 0; si < [0x7A10]; si++) {
    print_attribute(allergy_type[si], allergy_value[si]);
}
```

→ Die Debug-Ausgabe listet alle Allergie-Regeln der aktiven Klippe.

## Random-Aufrufe in Init

7 `random_range`-Aufrufe insgesamt — verteilt auf die 4 Difficulty-Branches.
Vermutete Verteilung (basierend auf Spielregel):

| Diff | Vermutete Aufrufe |
|------|-------------------|
| 0 | 1× Typ + 1× Wert = 2 |
| 1 | 1× Typ + 2× Werte = 3 |
| 2 | 2× Typ + 2× Werte = 4 |
| 3 | 3× Typ + 3× Werte = 6 |

Plus evtl. 1× zur Wahl welcher Klippe (Upper vs Lower) → Total ~16, aber durch geteilte
Code-Pfade vermutlich 7. Genauere Analyse erfordert Verfolgung der individuellen Branches.

## Nächste Schritte

1. **Branch-Analyse pro Difficulty** — die 4 Init-Pfade einzeln disassemblieren um zu sehen
   wie viele Allergien jeder Schwierigkeitsgrad erzeugt.
2. **`check_allergy`-Helper finden** — vermutlich FUNC #11 (Seg23:0x2035) oder ein lcall
   in andere Segmente. Identifiziert den exakten Vergleichsalgorithmus.
3. **`AllergicCliffsSolver` reaktivieren** mit den korrigierten DS-Adressen:
   - `0x79DA` für Difficulty
   - `0x7A0E` für aktive Klippe
   - `0x7A10` für Anzahl Allergien
   - `0x7A11..0x7A15` und `0x7A16..0x7A1A` für die Regeln
4. **Empirisch verifizieren** — durch Process-Memory-Reading während eines laufenden
   Cliff-Spiels.

## Querverweise

- `_SEGMENT_MAP_KORREKTUR.md` — verifiziertes Code-Mapping (sollte aktualisiert werden:
  Cliffs = Seg 23, nicht Seg 20)
- `HOTEL_DIMENSIA_DEEP.md` — was die alte „Cliff-Doku" tatsächlich analysiert hat
- `_TRUST_AUDIT.md`

---

# v2 (PE32) — Cliff-Code-Karte

> **Stand 2026-04-25.** Statische Analyse von `v2_bin/ZoombinisLJ.exe` — kein Live-
> Memory-Dump. Methodik: harter Anker = `bridge.mhk` push (`0x00405CDE`) +
> `Upper/Lower bridge accepts:` pushes (`0x00406B60`/`0x00406B67`). Ankertreuer
> v2-Code-Bereich: **`0x00403F10..0x00407180`** (12 528 Bytes Code, 27 Sub-Funktionen,
> 126 unique `.data`-VAs).

## v2-Funktions-Karte (Cliff-Region)

Identifiziert über (Direct-Call-Targets ∪ Standard-Prologe ∪ ret-Padding-Heuristik):

| # | VA | Größe | Caller | Rolle (statisch) |
|---|----|------|--------|------------------|
| fn00 | `0x00403F10` | 320 B | 4 (alle aus Cliff-Region) | **Cleanup/Reset**: `cmp [0x4943F8], 0; je return` — wenn Cliff inaktiv, return |
| fn01 | `0x00404050` | 176 B | 0 (intern) | **Init-Wrapper Pfad A**: lock acquire (0x494516), call Loader (`0x405A60`) |
| fn02 | `0x00404100` | 176 B | 0 | Init-Wrapper Pfad B (mit SCRB-Animation 0x1771) |
| fn03 | `0x004041B0` | 417 B | 0 | **Action-Dispatcher** — `cmp eax, 7; jmp [eax*4 + 0x4043C4]` (8 cases vs. 3 in v1) |
| fn04 | `0x00404351` | 1503 B | 0 | Größte Sub-Funktion. Difficulty-Logik. 27 .data-VAs, cmp 32/16/6 |
| fn05 | `0x00404930` | 688 B | **13** | Häufiger Helper (vermutlich Cliff-State-Setter) |
| fn07 | `0x00404C00` | 448 B | 0 | Pool-Init: cmp 625, cmp 25 (5×5×25) |
| fn08 | `0x00404DC0` | 960 B | 0 | **Difficulty-Branch-Body**: `jmp [ecx*4 + 0x404F4C]` mit `ecx = [0x494508]-1` (4 cases) |
| fn09 | `0x00405180` | 224 B | 5 | Pool-Iteration (cmp 625, cmp 50) |
| fn11 | `0x00405290` | 224 B | 1 | Pool-Selection (cmp 125) |
| fn13 | `0x00405470` | 336 B | 3 | **Difficulty-Range-Validator**: `jmp [ecx*4 + 0x40558C]`, schreibt 0x7D0 (= 2000 B Buffer aus v1) |
| fn14 | `0x004055C0` | 432 B | 1 | Pool-Helper (cmp 624, 625) |
| fn15 | `0x00405770` | 752 B | 1 | Setup, cmp 4/7 (Difficulty + something) |
| fn16 | `0x00405A60` | 1408 B | 1 | **MHK-Loader** — `push 'bridge.mhk'`, ruft Engine-MHK-Lader |
| fn19 | `0x004060E0` | 192 B | **12** | **Allergy-Match-Helper**? (häufiger Helper, analog zu v1 FUNC #11 @ 0x18235) |
| fn22 | `0x00406270` | 1264 B | 0 | **Eval-Match-Funktion** — 54 .data-VAs, cmp 6, cmp 3 |
| fn25 | `0x00406860` | 912 B | 0 | **Cheat-Print-Funktion** — pusht `Upper/Lower bridge accepts:` |
| fn26 | `0x00406BF0` | 1424 B | 0 | Match-Body (39 .data-VAs, cmp 100/15/21) |

(Zwischen-Funktionen ohne markante Marker weggelassen.)

## Verifizierte v1↔v2 Variablen-Mappings

> **Verified** = direkter Code-Pattern-Match (gleiche Konstante in cmp/mov, gleiche
> semantische Verwendung). **Probable** = Pattern passt, aber alternative Erklärung
> nicht ausgeschlossen. **Speculative** = Hypothese auf Basis von Read-Kontext.

| Bedeutung | v1 DS-Offset | v2 .data VA | Encoding-Unterschied | Konfidenz | Beleg |
|-----------|--------------|-------------|---------------------|-----------|-------|
| **difficulty** | `0x79DA` (0..3) | **`0x00494508`** (1..4) | v2 ist **1-basiert**! | **Verified** | `0x00405470: mov cx, [0x494508]; ... dec ecx; cmp ecx, 3; ja default; jmp [ecx*4+0x40558C]` — exakt v1's Difficulty-Dispatcher-Pattern |
| **attempt_counter** | `0x79E2` | **`0x004945AA`** | identisch (word, vergleich gegen 6) | **Verified** | `0x004063A6/0x004068B5/0x004070A3: cmp word [0x4945AA], 6` (3 Stellen, alle gegen 6 = bridge-collapses-Schwelle); `0x004070AD: inc word [0x4945AA]` |
| **engagement_flag** | `0x79D4` | **`0x004943F8`** | identisch (word 0/non-0) | **Verified** | `0x00403F14: cmp [0x4943F8], 0` als allererster Check der Cleanup-Funktion (genau wie v1's `cmp [DS:0x79D4], 0`) |
| **lock/active-flag** | `0x79E0` (?) | **`0x00494516`** | unklar, möglicherweise neue Mutex-Variante | **Probable** | `0x00404051: cmp [0x494516], 0; jne return` (lock-pattern); `mov 1` bei Init, `mov 0` bei allen Exit-Pfaden |
| **cliff_initialized** | `0x79E0` (alternativ) | **`0x004945D4`** | identisch | **Probable** | `mov [0x4945D4], 0` (clear vor Init) → `mov [0x4945D4], 1` (set nach Init) → `cmp [0x4945D4], 0; jne` (in Eval als Ready-Check) |
| **state_pointer (buffer)** | `0x79EC` (far-Ptr) | **`0x004943DC`** | v1: 16:16 far-Ptr, v2: flat 32-bit | **Probable** | 31× gelesen als dword in fast allen Sub-Funktionen, niemals als Konstante verglichen — typisches buffer-pointer-Pattern. v1 hat 0x7D0/0x3E8 buffer-Allocations, in fn13 wird `0x7D0` als Konstante geladen |

## Probable / Speculative

| Bedeutung (v1) | v1 | v2 (Hypothese) | Konfidenz | Code-Indizien |
|----------------|----|----|-----------|---------------|
| state_aux_1 | `0x79F2` | `0x004943F4` | Speculative | `cmp 0`, `mov 1`, `mov bx (=0)` Pattern |
| flag_x | `0x79EE` (cheat) | `0x004943C2` | Speculative | binary 0/1 mit zwei Schreibstellen |
| flag_y | `0x7A2C` (?) | `0x004943BC` | Speculative | binary 0/1 |
| phase_counter | `0x7A08` | `0x0049452E` | Speculative | inc/dec/cmp 1, mov 1 |
| compare_value | `0x7A36` (?) | `0x004943E8` | Speculative | `cmp 4` (= max difficulty?) und `cmp 0`. 20 Read/Writes über alle Init+Eval-Pfade |

## Allergie-Arrays (Iteration 2 — VERIFIED)

Die Cheat-Print-Funktion in fn25 (`0x00406B30..0x00406BCC`) wurde vollständig
disassembliert. Pseudo-Code:

```c
if ([0x004945B2] == 0)  printf("Lower bridge accepts:");
else                    printf("Upper bridge accepts:");

al = [0x004945B4];                   // num_allergies (BYTE in v2!)
si = 0;
if (al == 0) goto end;
do {
    eax = sign_extend(si);
    dx  = byte [eax + 0x004945BA];   // allergy_value[si]
    ax  = byte [eax + 0x004945B5];   // allergy_type[si]
    print_attribute(eax_type, edx_value, ...);
    cl = byte [0x004945B4];          // re-read num_allergies
    si++;
} while (si < cl);
```

Das ist eine **byte-für-byte-Übersetzung von v1's Cheat-Print-Loop** (FUNC #7 in v1).
Damit vier weitere Mappings auf Verified gehoben:

| Bedeutung | v1 DS | v2 .data VA | Encoding-Unterschied | Konfidenz |
|-----------|-------|-------------|---------------------|-----------|
| **which_cliff** | `0x7A0E` | **`0x004945B2`** | identisch (word, 0=Lower, ≠0=Upper) | **Verified** |
| **num_allergies** | `0x7A10` | **`0x004945B4`** | v1: word, **v2: byte** | **Verified** |
| **allergy_type[5]** | `0x7A11..0x7A15` | **`0x004945B5..0x004945B9`** | identisch (5 Bytes) | **Verified** |
| **allergy_value[5]** | `0x7A16..0x7A1A` | **`0x004945BA..0x004945BE`** | identisch (5 Bytes) | **Verified** |

Belegt durch direkten Code-Match an `0x00406B50` (which_cliff branch),
`0x00406B73`/`0x00406BB3` (num_allergies load), `0x00406B94` (allergy_value[i]),
`0x00406B9C` (allergy_type[i]).

→ **Cliff-Solver-tauglich für v2.** Ein v2-Solver kann jetzt:

1. Cliff aktiv? → `[0x004943F8] != 0`
2. Difficulty? → `[0x00494508]` (1..4, 1-basiert!)
3. Welche Klippe akzeptiert? → `[0x004945B2]` (0=Lower, sonst Upper)
4. Anzahl Allergien? → `byte [0x004945B4]`
5. Allergie-Regeln? → `byte [0x004945B5+i]` (Typ), `byte [0x004945BA+i]` (Wert)
6. Versuche bisher? → `[0x004945AA]` (max 6, dann bricht Brücke)

Konfidenz Solver: **Verified**.

## Engine-Globals (Cliff nutzt diese)

| VA | Pattern | Hypothese (Engine-weit) |
|----|---------|--------------------------|
| `0x004950C8` | von 12/12 Puzzles gelesen | Speculative: engineFlag (analog v1 0x0xAA32) |
| `0x004A2818` | von 12/12 Puzzles gelesen | Speculative: weiterer globaler Status |
| `0x0049B0E4` / `0x0049B0E6` | von Cliff-Cleanup-Pfad | Speculative: queue-state |
| `0x004A2402` | nur Cliff Eval | Speculative: input-state |
| `0x004A23EA` | Cliff Init / Eval | Speculative: input-state |

## Action-Dispatcher v2 — 8 Cases statt 3

```
0x004041DE  lea  eax, [esp+4]
0x004041E2  push eax
0x004041E3  call 0x4468F0          ; engine getter "current action"
0x004041E8  mov  esi, [esp+0x10]
0x004041EF  movsx eax, si
0x004041F2  dec  eax
0x004041F3  cmp  eax, 7
0x004041F6  ja   0x4043BE          ; default
0x004041FC  jmp  [eax*4 + 0x4043C4]
```

Tabelle bei `0x004043C4`:

| eax | Action | Sprungziel | Bedeutung (Hypothese) |
|----|--------|----|----|
| 0 | 1 | `0x00404203` | (siehe v1 case 1) |
| 1 | 2 | `0x004043BE` (default) | no-op |
| 2 | 3 | `0x00404302` | (in v1: Eval-Pfad) |
| 3 | 4 | `0x004042BC` | NEU vs. v1 |
| 4-7 | 5-8 | `0x00404351` (gemeinsam) | NEU vs. v1, alle in fn04 |

**v2 hat den Action-Dispatcher von 3 auf 8 Cases erweitert.** Die zusätzlichen Cases
5–8 münden alle in fn04 (1503 B), das vermutlich neue Action-Handler enthält
(z. B. für Hotspot-Touches, Help-Trigger, etc. — passt zur dokumentierten v2-
Erweiterung „neues Help-System" in `V1_VS_V2_COMPARISON.md`).

## Lücken (was diese Iteration NICHT geliefert hat)

1. **`allergy_type[5]` und `allergy_value[5]` Arrays** — in v1 bei `0x7A11..15`/`0x7A16..1A`
   als Byte-Arrays. In v2 nicht in den Difficulty-Branches der fn13 (`0x40558C`-Tabelle)
   geschrieben. Vermutung: das ist nicht der eigentliche Allergie-Befüller, sondern ein
   Range-Validator. Die echte Allergie-Befüllung liegt vermutlich in fn08 (`0x00404DC0`,
   960 B) — das ist der größere Difficulty-Branch-Body bei Tabelle `0x404F4C`.
2. **Analog: `num_allergies` (v1 `0x7A10`)** noch nicht lokalisiert.
3. **`which_cliff` (v1 `0x7A0E`)** noch nicht lokalisiert. In der Cheat-Print-Funktion
   (fn25 `0x00406860`) sollte das identifizierbar sein über das Branch-Pattern
   `if (which_cliff) printf("Upper..."); else printf("Lower...")`.
4. **Random-Aufrufe pro Difficulty-Branch noch nicht gezählt.** In v1 7× total. v2
   ruft `0x00457940` (= probable `random_range`) im Cliff-Loader 7× auf — passt zur
   v1-Anzahl, ist aber im Loader, nicht im Init-Body.
5. **Eval-Pseudocode** noch nicht aus fn22 (1264 B) extrahiert.

## Pragmatischer Solver-Status für v2

Mit den 4 Verified-Mappings reicht der Information-Stand für einen **eingeschränkten
v2-Cliff-Solver**, der erkennt:

1. **Ist Cliffs aktiv?** — `[0x004943F8] != 0` (engagement_flag)
2. **Welcher Difficulty?** — `[0x00494508]` (Wert 1..4)
3. **Wieviele Versuche schon?** — `[0x004945AA]` (count, max 6)
4. **Init fertig?** — `[0x004945D4] != 0`

Aber **NICHT** den eigentlichen Allergie-Match (welche Attribute akzeptiert die Klippe),
weil die Allergy-Arrays (Type/Value) noch nicht lokalisiert sind. Solver-Konfidenz für v2
bleibt damit `Speculative` bis nächste Iteration.

## Querverweise

- `analysis/V2_BINARY_MAP.md` — Gesamt-Karte aller 12 Puzzle in v2
- `scratch/cliff_v2_full_output.txt` — vollständiger Roh-Output der Cliff-Region-Analyse
- `scratch/cliff_v2_full.py` — Reproduktions-Skript
