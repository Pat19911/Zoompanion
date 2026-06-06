# v2 Live-Findings (Session 2026-04-26)

> **Konsolidiertes Wissen aus Live-Spiel-Tests mit dem Overlay-Tool und
> Code-Disassembly. KRITISCH zu erhalten — diese Information ist die
> Grundlage für den Cliff-Solver.**

---

## Setup

- **Spiel-Binary**: `Zoombinis Logical Journey.exe`, 614 400 Bytes, identisch
  zur Analyse-Binary `v2_bin/ZoombinisLJ.exe` (verifiziert via Memory-Read der
  6 Marker-Strings).
- **Spielort**: Windows 11 VM, installiert in
  `C:\Program Files (x86)\The Learning Company\Zoombinis Logical Journey(TM)\`
- **ImageBase**: `0x00400000`, kein ASLR.
- **Tool-Setup**: WinForms-Overlay mit `WS_EX_NOACTIVATE`, läuft als
  Always-on-top, schreibt Memory-Dumps in den Ordner neben der EXE.

---

## Cliff-Memory-Layout (Live-Verified)

| VA | Größe | Bedeutung | Konfidenz |
|----|------|-----------|-----------|
| `0x004945AA` | word | `attempts` (0..6, Bridge-collapse bei 6) | **Verified** |
| `0x004945B2` | word | `which_cliff` (0=Lower akzeptiert, 1=Upper akzeptiert) | **Verified** |
| `0x004945B4` | byte | `n_allerg` (1..3) — Anzahl Allergie-Slots | **Verified** |
| `0x004945B5..0x004945B9` | 5×byte | `allergy_type[i]` (1=Hair, 2=Eyes, 3=Nose, 4=Feet) | **Verified** |
| `0x004945BA..0x004945BE` | 5×byte | `allergy_value[i]` — siehe Encoding unten | **Verified** |

---

## Encoding von `allergy_value` (KRITISCH — aus Code-Disasm verifiziert)

**KEIN Bitfield wie ich anfangs dachte.** Tatsächlich: Byte mit zwei
4-bit-Indizes (low + high nibble).

- **Difficulty 0 (n_allerg=1)**: nur `low_nibble = value & 0x0F` ist die
  Variante. High Nibble = 0.
- **Difficulty 1 (n_allerg=2)**: zwei Werte gleichen Typs, gespeichert in
  separaten Slots:
  - `allergy_value[0]` = erster Wert (low nibble)
  - `allergy_value[1]` = zweiter Wert (low nibble)
  - Beide haben `allergy_type[0]` = `allergy_type[1]` = derselbe Type
- **Difficulty 2/3**: mehrere unterschiedliche Types in unterschiedlichen
  Slots, jeder mit eigenem Wert.

Code-Beleg: fn `0x004074C9` (1159 B, 1 Caller bei `0x004074BC`),
Difficulty-Dispatch-Tabelle bei `0x00407938`:
```
Diff 0 → 0x004076FB  (n_allerg=1, einfache Variante)
Diff 1 → 0x0040777C  (n_allerg=2, zwei Werte selber Type)
Diff 2 → 0x0040784F  (geteilt mit Diff 3)
Diff 3 → 0x0040784F  (n_allerg=2 oder 3, mehrere Types)
```

EBX wird vor dem Decode mit dem Pool-Element befüllt. Pool ist 4×125=500 Bytes,
generiert in Iteration bei `0x004074CD..0x00407571` mit `[ebp-4]=125` als
Schleifen-Counter. Die 4 Bytes von `ebx` codieren die 4 Attribut-Gruppen:
```
Byte 0 (LSB) = Feet allergy
Byte 1 = Nose allergy
Byte 2 = Eyes allergy
Byte 3 (MSB) = Hair allergy
```
Pro Byte: low nibble = erster Wert, high nibble = zweiter Wert (nur Diff 1).

---

## Variante-Reihenfolge (1-basierter Index in v1-style Reihenfolge)

Teilweise live-verifiziert (Zellen mit ✓), Rest ist v1-Annahme. Bei Hair noch
gar keine Live-Bestätigung — wenn der Solver dort falsch zeigt, ist diese
Tabelle der Fix-Punkt.

| Index | Hair | Eyes | Nose | Feet |
|-------|------|------|------|------|
| 1 | Spitze Haare | **Große Augen** ✓ | Grüne Nase | Schuhe |
| 2 | Pferdeschwanz | Ein Auge | Orange Nase | Rollschuhe |
| 3 | Grüne Mütze | Müde Augen | **Rote Nase** ✓ | Räder |
| 4 | Gerade Haare | **Brille** ✓ | Lila Nase | **Sprungfedern** |
| 5 | Glatze | Sonnenbrille | **Blaue Nase** ✓ | **Propeller** ✓ |

Live-verifizierte Zellen mit ✓:
- Eyes 1=Große Augen (val=1 Test)
- Eyes 4=Brille (val=4 Test, „Brille-Zoombinis mussten unten durch")
- Nose 3=Rot (val=3 Test mit nur Rot durchgelassen)
- Nose 5=Blau (val=5 Test, „untere brücke hat nur blaue nasen")
- Feet 5=Propeller (val=5 Test, „nur propeller gehen unten durch")
  → ergab Tausch der v1-Reihenfolge bei Feet (Springs ↔ Propeller)

**Hair-Reihenfolge** ist statisch übernommen aus v1 — noch ungeprüft.
Etwaige weitere Vertauschungen zeigen sich beim Spielen.

---

## Match-Logik (ODER, nicht UND)

- Bei Diff 1: zwei Werte gleichen Typs, ODER-verknüpft („Rote Nase oder Blaue Nase")
- Bei Diff 2: zwei verschiedene Types, ODER-verknüpft („Rote Nase oder Brille")
- Bei Diff 3: drei verschiedene Types, ODER-verknüpft

Klippe niest, wenn der Zoombini **mindestens EINE** der Allergie-Eigenschaften
hat. Akzeptiert nur, wenn KEINE der Eigenschaften zutrifft. Belege:
1. v1-Doku (analysis/ALLERGIC_CLIFFS.md) sagt das explizit.
2. Datenstruktur ist Liste unabhängiger (Type, Value)-Paare — typisch für ODER.
3. Bei UND wäre Diff 2/3 praktisch unspielbar (Match-Wahrscheinlichkeit 1/125).
4. User-Spielerfahrung: alle Klippen ohne Fehler durchgespielt mit ODER-Annahme.

---

## Match-Funktion — GEFUNDEN: `0x00407950` (144 B, 1 Caller `0x00406A0B`)

**Vollständig disassembliert und entschlüsselt** (2026-04-26).

### Aufruf aus Cliff-Eval

In fn `0x00406760` wird sie so aufgerufen:
```asm
@0x00406A06  push 0x004945B0   ; Pointer auf Cliff-State (= struct base)
@0x00406A0B  call 0x00407950   ; → Match-Funktion
```
Plus weitere Argumente auf dem Stack.

### Cliff-State Struktur (Pointer = `0x004945B0`)

Der Pointer `0x004945B0` ist die Basis einer Struct mit folgendem Layout:
```
ptr +  0:  word  ?  (vermutlich init/state flag)
ptr +  2:  word  which_cliff  (= 0x004945B2: 0=Lower akzeptiert, 1=Upper)
ptr +  4:  byte  n_allerg     (= 0x004945B4)
ptr +  5:  byte  allergy_type[0..4]  (= 0x004945B5..0x004945B9)
ptr + 10:  byte  allergy_value[0..4] (= 0x004945BA..0x004945BE)
```
Das ist EIN zusammenhängender 15-Byte-Block ab `0x004945B0`.

### Match-Pseudocode

```c
int match_cliff(int unused_arg1, struct CliffState* state, void* zoombini_ptr, int mode) {
    int matched = 0;
    for (int i = 0; i < state->n_allerg; i++) {
        int type = state->allergy_type[i];   // 1..4
        int val  = state->allergy_value[i];  // 0..15 (4-bit nibble)
        // Zoombini attributes are 4 bytes at zb_ptr+0xC0..0xC3
        // type=1 → Hair (zb_ptr+0xC0), type=2 → Eyes (+0xC1), etc.
        int zb_attr = ((char*)zoombini_ptr)[0xBF + type];
        if (zb_attr == val) matched = 1;   // ODER-Logik!
    }
    int result = matched ? state->which_cliff : (state->which_cliff == 0 ? 1 : 0);
    if (mode == 2) result = !result;
    return !result;   // final negation, returns 1 = "this cliff accepts him"
}
```

### Konkrete Code-Stellen

```
@0x0040796F  movzx cx, byte [edx+4]              ; cl = n_allerg
@0x00407980  lea   eax, [edx+0xA]                ; eax = &allergy_value[0]
@0x0040798A  mov   cl, byte [eax-5]              ; cl = allergy_type[i]
@0x0040798D  mov   bl, byte [eax]                ; bl = allergy_value[i]
@0x0040798F  movsx ecx, byte [ecx+ebp+0xBF]      ; ecx = zb.attr[type]
@0x00407997  cmp   ecx, ebx                      ; zb.attr == allergy_value?
@0x00407999  jne   0x4079A0                      ; mismatch → continue
@0x0040799B  mov   edi, 1                        ; MATCH! flag = 1
@0x004079A0  inc   eax                           ; next slot
@0x004079A1  dec   esi
@0x004079A2  jne   0x407986                      ; loop n_allerg times
```

### Verifizierte Erkenntnisse aus Match-Funktion

1. **ODER-Logik bestätigt**: `mov edi, 1` setzt match-flag bei JEDEM Treffer,
   ohne Loop-Abbruch. Das ist klassisches OR. Bei UND wäre `cmp` + `jne` zum
   exit (Mismatch beendet alles).

2. **Zoombini-Attribute liegen bei `[zb_ptr + 0xC0..0xC3]`**:
   - 0xC0 = Hair (type 1)
   - 0xC1 = Eyes (type 2)
   - 0xC2 = Nose (type 3)
   - 0xC3 = Feet (type 4)

3. **`allergy_value[i]` ist direkt eine ganze Zahl 0..15** (kein Bitfield) —
   wird mit dem Zoombini-Attribut direkt verglichen (`cmp ecx, ebx`).

4. **Bei Difficulty 1** (n_allerg=2, beide gleichen Typs): der Loop iteriert
   2x über denselben Type, mit zwei verschiedenen Werten → ODER-Verknüpfung
   („Hair=val0 ODER Hair=val1").

5. **Bei Difficulty 2/3** (n_allerg=2/3, verschiedene Types): der Loop
   iteriert über verschiedene Types, mit jeweils einem Wert → ODER über
   verschiedene Eigenschaften („Hair=val0 ODER Eyes=val1 ODER Nose=val2").

6. **Match-Result bestimmt welche Klippe akzeptiert**: bei `matched && which_cliff=0`
   → Lower akzeptiert; bei `matched && which_cliff=1` → Upper akzeptiert; bei
   `!matched` → die andere Klippe akzeptiert.

---

## Tool-Status

`ZoombiniHelper-overlay.exe` (147 MB, .NET 8 self-contained) neben der EXE:
- Always-on-top, ohne Fokus-Klau (`WS_EX_NOACTIVATE`)
- Liest alle 5 Cliff-Slots, zeigt aktive Allergien
- Statische Variante-Tabelle eingebaut (siehe oben)
- F12 = Memory-Dump nach `memdump-HHmmss.txt` neben der EXE
- Detection: triggert wenn `n_allerg ∈ {1,2,3}` und mindestens ein
  `allergy_type ∈ {1..4}`

---

## Pool-Generation und Difficulty-Selektion

### Cliff-Init-Funktionsbaum

```
fn 0x00407405 ..0x004074C9  (Wrapper / Difficulty-Source)
  └─ call 0x004074C9 (Pool-Generator + Decoder)
       ├─ Pool-Loop bei 0x004074CD..0x00407571 ([ebp-4]=125 = 5×5×5)
       ├─ Pool-Selection (random_range Aufrufe in Sub-Funktion)
       ├─ ebx-Bitfield wird befüllt (4 Bytes für 4 Attribut-Gruppen)
       └─ Difficulty-Switch bei 0x00407938:
            esi=0 → 0x004076FB  (Diff 0: 1 Wert)
            esi=1 → 0x0040777C  (Diff 1: 2 Werte selber Type)
            esi=2 → 0x0040784F  (Diff 2/3: mehrere Types)
            esi=3 → 0x0040784F
```

### EBX-Bitfield-Layout (kritisch)

Vor dem Decode enthält `ebx` ein 32-bit Pattern aus dem Pool:
```
Byte 3 (MSB)  Byte 2       Byte 1       Byte 0 (LSB)
[Hair value][Eyes value][Nose value][Feet value]
  4-bit       4-bit         4-bit       4-bit
```
Bei Diff 0: nur 1 Byte ≠ 0 (= 1 Allergie eines Typs).
Bei Diff 1: 1 Byte mit BEIDEN Nibbles ≠ 0 (= 2 Werte selber Type).
Bei Diff 2: 2 Bytes ≠ 0 (= 2 Allergien verschiedener Types).
Bei Diff 3: 3 Bytes ≠ 0 (= 3 Allergien verschiedener Types).

### Werte-Range im ebx-Bitfield

Aus Pool-Generation (0x004074CD..): Bytes werden mit Bitmasken `0xF000F`,
`0x10000`, `0x1000000` etc. konstruiert. Plus `cmp bl, 6; jne` filtert
ungültige Werte. Resultat: jedes Nibble nimmt Werte 1..5 an (5 Varianten),
Wert 6+ wird im Pool ausgefiltert.

### Random-Auswahl-Kette (vollständig aufgespürt)

```
fn 0x004074C9 (Cliff-Init Body)
  ├─ baut deterministischen 125-Element-Pool (5×5×5 Kombinationen)
  ├─ call 0x00401070 (random_in_range Wrapper)        ← Pool-Index wählen
  │    └─ call 0x0040F9A0 (rand() — LCG core)
  ├─ ebx = pool[index*4..index*4+3]                   ← gewähltes 32-bit Pattern
  └─ Difficulty-Switch dekodiert ebx in allergy_type[i]/value[i]
```

Plus zweiter `call 0x00401070` für `which_cliff` (0 oder 1).

**fn `0x00401070`** (32 B, 275 Callers) — generischer Random-Range-Helper:
```c
int random_in_range(int min, int max) {
    int range = max - min;
    int r = rand_lcg(range);   // call 0x40F9A0
    return r + min;
}
```

**fn `0x0040F9A0`** ist die echte LCG-Random-Funktion (entspricht v1's
seg14 rand()). LCG-Konstante `2531011` ist bei `0xF9E2` im PE statisch
eingebettet und durch Memory-Match-Test verifiziert.

### Cliff-Loader (`0x00405A60`) random_range Calls

Die 7 random_range-Calls bei `0x00457940` in fn `0x00405A60` sind
**NICHT** für Allergie-Generation, sondern für **SCRB-Animations-IDs**
(Bridge-Frames, Zoombini-Animations-Sprites, Position/Sound-Variation):

| # | (min, max)        | Bedeutung |
|---|-------------------|-----------|
| 1 | (20000, 29999)    | Game-Seed oder ID-Pool |
| 2 | (1200, 1201)      | SCRB-Variant 2-stufig |
| 3 | (1000, 1099)      | SCRB-Range 100 IDs |
| 4 | (1216, 1226)      | SCRB-Range 11 IDs |
| 5 | (1202, 1213)      | SCRB-Range 12 IDs |
| 6 | (1214, 1215)      | SCRB-Variant 2-stufig |
| 7 | (175, 199)        | Position oder Sound (25 Werte) |

→ Cliff-Animation/Visualisierung wird zufällig variiert, aber die
**Allergie-Generation passiert separat im Init-Body via `0x00401070`**.

## Roadmap: Per-Zoombini-Solver für Ferry & Co.

Cliff-Solver gibt globale Regeln. Bei Ferry, Hotel, Toads etc. brauchen wir
puzzle-spezifische Anweisungen pro Zoombini ("dieser Zoombini auf Sitz 7").

### Was wir schon wissen (aus Cliff-Match-Disasm)

- Zoombini-Attribute liegen bei `[zb_ptr + 0xC0..0xC3]`:
  - 0xC0 = Hair, 0xC1 = Eyes, 0xC2 = Nose, 0xC3 = Feet (Werte 1..5)
- Zoombini-Index liegt bei `[zb_ptr + 0x1A]` (word)
- Cliff-Eval ruft Match mit `esi` als Zoombini-Pointer auf, kommt vom Engine-Layer
- (Pool-Liste der 16 Gruppen-Zoombinis ist noch nicht lokalisiert; nicht nötig
  für Cliff-Solver, evtl. relevant für künftige Puzzle wie Ferry/Hotel.)

### UI-Modus (implementiert)

Hybrid:
- **Default**: globale Regeln (welche Brücke niest, was sind die Allergien)
- **Beim Drag**: spezifische Anweisung für gehaltenen Zoombini (→ obere/untere)
- **Beim Loslassen**: zurück zu globalen Regeln

Funktioniert generisch — Cliff zeigt "diese Brücke", Ferry könnte "diese
Sitzposition" zeigen, Hotel "dieses Zimmer". Übertragbar auf andere Puzzle,
sobald deren Drag-Slots lokalisiert sind.

## Engine-Globals (relevante VAs außerhalb Cliff-Region)

Aus früheren Diagnose-Tests:
- `0x004A2818` ≠ 0 wenn Spiel läuft — vermutlich `state_pointer`
- `0x004A4878` ändert sich mit Cliff-Status — Cliff-related state
- `0x004950C8` = 0 in unseren Tests — vermutlich engineFlag
- `0x0049B0E6` = 0 in Tests — anderer Status

---

## DRAG-Slot — gelöst 2026-04-26

**Live-verifiziert (Memory-Diff zwischen idle / drag-A / drag-B):**

Der gerade gehaltene Zoombini wird mit seinen 4 Attribut-Indizes (1..5) in
4 aufeinanderfolgende Words geschrieben:

| VA | Größe | Bedeutung |
|----|-------|-----------|
| `0x00494928` | word | Hair-Index (1..5) |
| `0x0049492A` | word | Eyes-Index (1..5) |
| `0x0049492C` | word | Nose-Index (1..5) |
| `0x0049492E` | word | Feet-Index (1..5) |

- Idle (kein Zoombini): alle vier Words = 0
- Drag (Zoombini hochgehoben + festgehalten): plausibler 1..5-Wert pro Word
- Drop (Loslassen): zurück zu 0

Damit reicht ein simpler Words-1..5-Range-Check zur Drag-Detection — kein
Pointer-Resolve, keine Handle-Auflösung nötig. Implementiert in `CliffState.cs`
(Read-Methode, `Held`-Property).

### Sackgassen, die NICHT der Drag-Container sind (für Future-You)
1. **`[0x00495908]`** = DWORD-Pointer auf ein 224-Byte Engine-State-Objekt
   (Klasse mit VTable `0x00487BCC`, Tag `"PORT"`). Das Objekt selbst ändert
   sich beim Drag NICHT — ist ein Cursor/Viewport-State, kein Drag-Container.
   17 Funktionen lesen es; einzige Schreibstelle ist `0x00447880` (Init).
2. **`0x004A1178..82`, `0x0049E170..78`, `0x004A0174..7C`** waren erste
   Memory-Diff-Kandidaten — alle nicht-pointer-artig, kein zb_ptr.
3. **DRAG-RECORD bei `0x00494AB0..0x00494ADF`** — sieht aus wie ein 16-Byte-
   Slot-Array mit Heap-Pointern (`0x03051F20` etc.), aber die dereferenzierten
   Bytes bei `+0xC0..0xC3` sind keine plausiblen Attribute → das ist kein
   Zoombini-Objekt-Pointer.

### Cliff-Eval-Aufrufkette (zur Vollständigkeit)
- Match-Fn: `0x00407950` (1 Caller)
- Cliff-Eval: `0x00406760` (1 Caller bei `0x004063C9`, in fn `0x00406210`)
- Beim Drop wird `zb_ptr` aus einem Handle-Resolve gewonnen:
  `call 0x4468f0(buf)` → `mov ecx,[buf]` → `call 0x455f70(ecx,1,1)` → `eax=ptr`
- Engine-Helpers: `0x004468F0` `GetObjectUnderMouse`, `0x00455F70`
  `ResolveHandleToObject`, `0x00447880` `PickupAtMouse`
- Im Resolved-Objekt: Attribute bei `+0xC0..0xC3`, Index bei `+0x1A` (word)

Diese Kette wird vom Tool **nicht** ausgenutzt — der direkte Drag-Slot-Read
ist einfacher und genauso zuverlässig.

---

## Wichtige bisherige User-Beobachtungen (zum Validieren)

1. **Determinismus**: Cliff-Konfiguration bleibt identisch über Spielstarts mit
   selbem Save (gleiche Zoombini-Gruppe). Indikation: seed-basierte oder
   save-game-gespeicherte Pool-Generation.
2. **Spielmechanik OK**: User hat alle Klippen mit dem Tool fehlerfrei
   durchgespielt — bestätigt, dass die Bit-Decodierung korrekt ist und
   ODER-Logik stimmt.
3. **Beim Drag&Drop wird Spiel minimiert** ohne `WS_EX_NOACTIVATE` — gefixt.
4. **F12-Hotkey funktioniert** auch wenn Spiel im Vordergrund (via
   `GetAsyncKeyState`).
