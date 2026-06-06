# Hotel Dimensia — Die zwei härtesten Modi erklärt

Aus direkter Disassembly (Seg 28, file 0x25234..0x2542C für Diff 2 und 0x26A0F + 0x26630 für Diff 3).

## Die 4 Schwierigkeitsstufen

| Difficulty | Manual-Name | Mechanik |
|------------|-------------|----------|
| 0 | Not So Easy | 1D — 5 Räume in einer Reihe, 1 Attribut |
| 1 | Oh So Hard | 2D — 5×5 Gitter, 2 Attribute |
| **2** | **Very Hard** | **2D — 5×5 mit vernagelten Räumen** |
| **3** | **Very, Very Hard** | **3D — 5×5×5 Würfel (125 Räume!)** |

---

## Difficulty 2: Vernagelte Räume — was passiert wirklich

### Init-Algorithmus (file 0x25234..0x2542C, ~510 B)

```c
if (difficulty != 2) goto FINALIZE;

// Phase 1: Zwei Achsen-Permutationen bauen (für x und y)
clear_used_x_flags()  // [bp-0x14], 5 slots
clear_used_y_flags()  // [bp-0x1E], 5 slots

for (slot = 0; slot < 5; slot++) {
    // Würfel x-Wert, retry wenn schon used
    do { x = rand(4); } while (used_x[x] != 0);
    used_x[x] = 1;
    
    // Würfel y-Wert, retry wenn schon used
    do { y = rand(4); } while (used_y[y] != 0);
    used_y[y] = 1;
    
    store_rule(x+1, y+1, slot * 6);  // → table_left/table_right
}

// Phase 2: Alle Zoombinis ins 5×5-Gitter abbilden
count_matrix[5][5] = {0}
for each zoombini z {
    attr1 = z.attr[attr_type_1]
    attr2 = z.attr[attr_type_2]
    
    // Finde die Zelle (row, col) wo table_left[r][c] == attr1 UND table_right[r][c] == attr2
    for (row = 0; row < 5; row++) {
        for (col = 0; col < 5; col++) {
            if (table_left[row][col] == attr1 &&
                table_right[row][col] == attr2) {
                count_matrix[row][col]++;
            }
        }
    }
}

// Phase 3: Sammle ALLE leeren Zellen (count_matrix[i] == 0)
empty_cells = []
for (i = 0; i < 25; i++) {
    if (count_matrix[i] == 0) {
        empty_cells.append(i);
    }
}

// Phase 4: Wähle Anzahl zu vernagelnden Räume
num_picks = rand(num_empty - 1) + 1   // mindestens 1
if (num_picks > 8)         num_picks = 8;        // Maximum 8
if (num_picks > num_empty) num_picks = num_empty;

// Phase 5: Zufällig ein paar leere Zellen vernageln
for (pick = 0; pick < num_picks; pick++) {
    do { idx = rand(num_empty - 1); }
    while (count_matrix[empty_cells[idx]] is already marked);
    
    count_matrix[empty_cells[idx]] = 0xFFFF;  // markiert als "vernagelt"
    sprite_variation[pick] = rand(3);          // 1 von 4 Brettervariationen
}
```

### Was bedeutet das spielerisch?

**Wichtig: Vernagelte Räume sind IMMER leere Räume.** Kein Zoombini würde ohnehin
hineingehen. Der Spielcode bestätigt das eindeutig — nur Zellen mit `count_matrix[i] == 0`
(also Zellen ohne Zoombini) kommen in den `empty_cells`-Pool, aus dem zum Vernageln gewählt
wird.

### Aber WARUM macht das Diff 2 schwerer als Diff 1?

Bei 16 Zoombinis und 25 Räumen sind genau **9 Räume leer** (25 - 16 = 9, vorausgesetzt jeder
Zoombini hat einen einzigartigen Raum was bei Diff 1+2 garantiert ist).

Bei Diff 1 (Oh So Hard, 2D ohne Vernagelung):
- Spieler sieht alle 25 Räume
- Spieler kann durch **Ausschluss** deduzieren: „Wenn ich 16 Zoombinis platziert habe, sind
  die übrigen 9 Räume leer."
- Mit jedem korrekt platzierten Zoombini schrumpft der Lösungsraum.
- Information durch leere Räume: man kann sie als Hinweis lesen.

Bei Diff 2 (Very Hard, mit Vernagelung):
- Bis zu 8 von 9 leeren Räumen sind **vernagelt** und können nicht mehr betrachtet werden.
- Damit fehlt dem Spieler die **Ausschluss-Information**: er kann nicht sicher sein dass
  ein vernagelter Raum „unwichtig" ist — er weiß nur, dass er ihn nicht ausprobieren kann.
- Die Permutation der Achsen muss aus weniger Datenpunkten rekonstruiert werden.
- Praktisch: weniger Versuchsfeld, weniger Hinweis-Information, schwerere Deduktion.

### Beispiel

Stell dir vor du hast die ersten 8 Zoombinis korrekt platziert. Bei Diff 1 siehst du jetzt
das 5×5-Muster zur Hälfte gefüllt und kannst die Permutationen interpolieren. Bei Diff 2
sind 8 der noch leeren 17 Räume **vernagelt** — du siehst nur 9 wirklich offene Räume + 8
geheime. Die verbleibenden 8 Zoombinis musst du in 17 mögliche Räume platzieren, von denen
8 unsichtbar gemacht sind.

### Visuelle Variation

Pro vernageltem Raum wird `rand(3)` gewürfelt (= 4 mögliche Bretter-Sprites). Das ist nur
**Kosmetik** — alle Vernagelten haben dieselbe Funktion (= zugesperrt).

---

## Difficulty 3: 3D-Würfel — was passiert wirklich

### Init-Validation (file 0x25207..0x2522B)

```c
if (difficulty == 3) {
    // 3 Attribut-Typen würfeln (war schon davor in init)
    // Alle drei MÜSSEN verschieden sein
    if (attr_type_1 != attr_type_2 &&
        attr_type_2 != attr_type_3 &&
        attr_type_1 != attr_type_3) {
        valid++;  // ok
    }
    // ANDERS als Diff 0/1/2: KEINE Vielfalt-Prüfung der Zoombini-Gruppe!
    // Das Spiel akzeptiert einfach 3 verschiedene Attribut-Typen.
}
```

**Wichtig:** Bei Diff 3 macht das Spiel **keine Validierung der Zoombini-Vielfalt**. Bei
Diff 0/1/2 wird geprüft ob die aktuelle Zoombini-Gruppe genug verschiedene Werte pro Typ
hat (Schwellen 4 oder 5). Bei Diff 3 ist das egal — die 3D-Mechanik benötigt diese
Validation nicht.

### `store_rule_3d` (file 0x26A0F)

Statt einen 5×5×5 = **125-Element-Würfel** zu speichern, nutzt das Spiel **drei separate
5-Element-Tabellen** (eine pro Achse):

```c
void store_rule_3d(int slot, int val_x, int val_y, int val_z) {
    // Decode slot (0..124) in 3D-Koordinaten
    int z_idx = slot % 5;
    int y_idx = slot / 25;
    int rest  = slot % 25;
    int x_idx = rest / 5;
    
    // Speichere in DREI getrennte Permutations-Tabellen
    table_x[x_idx] = val_x   // bei 0x316 (5 entries)
    table_y[y_idx] = val_y   // bei 0x348 (5 entries)
    table_z[z_idx] = val_z   // bei 0x37A (5 entries)
}
```

**Total: 15 Permutations-Werte** statt 125. Sehr platzsparend.

### `check_rule_3d` (file 0x26630)

Beim Platzieren eines Zoombinis im 3D-Raum prüft das Spiel ob seine Koordinate in alle drei
Achsen-Permutationen passt:

```c
int check_rule_3d(int slot, int val_1, int val_2, int val_3) {
    if (cheat_flag) return 1;
    
    // Decode 3D coords
    int z_idx = slot % 5;
    int y_idx = slot / 25;
    int rest  = slot % 25;
    int x_idx = rest / 5;
    
    // Check: alle 3 Achsen müssen für diesen Slot besetzt sein
    if (table_x[x_idx] != 0 &&
        table_y[y_idx] != 0 &&
        table_z[z_idx] != 0) {
        // Alle Werte gefüllt → check ob sie passen (an val_1, val_2, val_3)
        ...
    }
    
    return 0/1;
}
```

### Was bedeutet das spielerisch?

Pro Achse hat der **Hotel-Code** eine **Permutation** (welcher Attribut-Wert geht in welche
Schicht). Beispiel:

- **x-Achse (Spalten):** Haar-Permutation, z.B. [3, 1, 4, 5, 2]
  - Spalte 0 = Spiked Hair (Wert 3)
  - Spalte 1 = Ponytail (Wert 1)
  - Spalte 2 = Straight (Wert 4)
  - usw.

- **y-Achse (Zeilen):** Augen-Permutation, z.B. [2, 4, 1, 5, 3]
- **z-Achse (Tiefe):** Nasen-Permutation, z.B. [5, 2, 3, 1, 4]

Ein Zoombini mit (Spiked Hair, Sleepy Eyes, Red Nose) muss in den Raum:
- x = position_of(3) in Permutation = 0
- y = position_of(3) in Permutation = 4
- z = position_of(3) in Permutation = 2
- → Raum-Index = 0*5 + 4 + 2*25 = **54**

### Die Schwierigkeit

**Mathematisch:**
- Pro Achse: 5! = 120 mögliche Permutationen
- Drei Achsen: 120³ ≈ **1.728.000 mögliche Konfigurationen**
- Spieler hat nur ~16 Zoombinis × ~6 Versuche = ~96 Tests
- Theoretischer Informationsbedarf: log₂(1.728.000) ≈ 21 Bits

**Praktisch:**
- Pro erfolgreich platziertem Zoombini lernt der Spieler **3 Permutations-Werte**
  (eine pro Achse)
- Nach 5 erfolgreichen Platzierungen sind theoretisch alle 15 Werte aufdeckbar
- Aber: Jeder Versuch (auch fehlgeschlagene) liefert Information

**Strategie für Diff 3:**
1. **Erste Zoombinis als „Sonde" einsetzen.** Platziere sie an verschiedenen Koordinaten,
   um die Permutationen zu kalibrieren.
2. **Pro Achse eine Permutation isoliert deduzieren.** Wenn Zoombini A und B sich nur in
   einer Achse unterscheiden, lernt man pro Versuch genau einen Permutations-Wert.
3. **Logik wie Sudoku:** Wenn 4 von 5 Permutations-Werten einer Achse bekannt sind, ist der
   5. impliziert.
4. **Reihenfolge wählen:** Achten dass die ersten Zoombinis verschiedene Achsen-Werte
   haben, sonst „verschwendet" man Versuche.

### Engine-interne 3D-Speicherung

| DS-Adresse | Inhalt |
|------------|--------|
| `0x0316..0x031F` | 5-Element-Permutation x-Achse |
| `0x0348..0x0351` | 5-Element-Permutation y-Achse |
| `0x037A..0x0383` | 5-Element-Permutation z-Achse |
| `0x7E48` | x-Achse Attribut-Typ (welcher Attribut wird auf x sortiert) |
| `0x7E4A` | y-Achse Attribut-Typ |
| `0x7E4C` | z-Achse Attribut-Typ |

→ Solver kann diese 15 Permutations-Werte direkt aus DS lesen + die 3 Attribut-Typen.
Damit ist die 3D-Lösung trivial **rechenbar** wenn man Memory-Reading hat.

---

## Vergleich der zwei härtesten Modi

| Aspekt | Diff 2 (Vernagelt) | Diff 3 (3D) |
|--------|--------------------|--------------|
| Räume | 25 (5×5) | 125 (5×5×5) |
| Attribute | 2 | 3 |
| Sichtbare Räume | 17-25 (Vernagelte unsichtbar) | 125 |
| Permutations-Werte | 10 (2 Achsen × 5) | 15 (3 Achsen × 5) |
| Zusätzliche Mechanik | Bis zu 8 leere Zellen vernagelt | Komplett 3D-Navigation |
| Information pro Versuch | ~2 Bit | ~3 Bit |
| Schwierigkeit (Manual) | „Very Hard" | „Very, Very Hard" |

### Welcher ist objektiv schwerer?

**Diff 3 hat mehr theoretische Information** zu erschließen (1.7M Konfigurationen vs.
~14.000 für Diff 2). Aber:

- Diff 3 gibt **mehr Information pro Versuch** (3 Achsen-Werte gleichzeitig)
- Diff 2 gibt **weniger Information** durch versteckte Räume

Manual-Empfindung:
- Diff 2 fühlt sich „fies" an weil die UI-Versteckung wirkt wie Sabotage
- Diff 3 fühlt sich „schwierig" an weil die Mathematik komplex ist

Beide sind objektiv hart — auf verschiedene Weise.

---

## Praktische Solver-Implementation

```csharp
// Hotel Diff 3 Solver — direkte Memory-Reading-Lösung
public string SolveDiff3(GameState game) {
    int axisX = game.ReadWord(0x7E48);  // attr_type 1
    int axisY = game.ReadWord(0x7E4A);  // attr_type 2  
    int axisZ = game.ReadWord(0x7E4C);  // attr_type 3
    
    var permX = game.ReadWordArray(0x0316, 5);  // 5 values
    var permY = game.ReadWordArray(0x0348, 5);
    var permZ = game.ReadWordArray(0x037A, 5);
    
    return $"Achsen: {axisX}/{axisY}/{axisZ}, " +
           $"PermX: [{string.Join(",",permX)}], " +
           $"PermY: [{string.Join(",",permY)}], " +
           $"PermZ: [{string.Join(",",permZ)}]";
}

// Hotel Diff 2 Solver — finde vernagelte Räume + 5×5 Permutation
public string SolveDiff2(GameState game) {
    int axisX = game.ReadWord(0x7E48);
    int axisY = game.ReadWord(0x7E4A);
    
    var tableLeft = game.ReadWordArray(0x0316, 25);   // x-axis values
    var tableRight = game.ReadWordArray(0x0348, 25);  // y-axis values
    var cliffAssign = game.ReadWordArray(0x02EE, 8);  // boarded room sprite IDs
    
    // Vernagelte Räume sind die mit count_matrix[i] == 0xFFFF
    // (in shared seg, hier vereinfacht)
    
    return $"Achsen: {axisX} × {axisY}, vernagelte Räume: bis zu 8";
}
```

## Querverweise

- `analysis/HOTEL_DIMENSIA_DEEP.md` — vollständige Init-Disassembly (war früher fälschlich „Cliff-VERIFIED")
- `analysis/HOTEL_DIMENSIA_VERIFIED.md` — Master-Daten
- `Solvers/HotelDimensiaSolver.cs`
- file 0x26A0F (`store_rule_3d`) und 0x26630 (`check_rule_3d`) für die 3D-Mechanik


---

# v2 (PE32) — Hotel-Code-Karte

> Vollständige Tabelle: `V2_VARIABLE_MAP.md` § 5.

## Code-Lokalisation

| | v1 | v2 |
|---|----|----|
| Code | Seg 28 | `.text` 0x004154D0..0x004191D0 (+ Helper bis 0x483xxx) |
| MHK-Loader | — | `0x004154D0` (2352 B, 1 Caller) |
| Wrapper | — | `0x00412760` |
| Difficulty body | — | `0x00417230` |
| Reachable functions | — | 60 (sehr groß) |

## Verifizierte Mappings (v1→v2)

| v1 DS | v2 VA | Bedeutung | Konfidenz |
|-------|-------|-----------|-----------|
| `0x7E42` (0..3) | **`0x004967D6`** | DIFFICULTY | **Verified** (Iter. 2): `mov bx, [0x4967D6]; cmp bx, 2/3` mehrfach in Init-Pfad bei 0x00417268, 0x00417275, 0x004173AB, 0x00417563 |
| **constraint_X[25]** | `FFFF:0x0316` | **`0x00496690..`** | **Verified** (Iter. 2): Cell-Compare-Pattern in Init: `cmp word [eax*2 + 0x496690], bp` und `cmp word [edx*2 + 0x496690], 4` mit eax/edx als 0..24-Index |
| **constraint_Y[25]** | `FFFF:0x0348` | **`0x00496698..`** | **Verified** — analog 0x004174B2: `cmp word [eax + 0x496698], si` |
| Match-Cell-Array | — | **`0x00495E8C..`** | Probable: `cmp [eax + 0x495E8C], dx` in Match-Loop |
| Box-State-Array | `FFFF:0x?` | `0x00496014..` | Probable: indirekt mit indizierten reads/writes |
| `0x7E44` | `0x004967D4` (cmp 0) | first_time_flag | Probable (binary check, benachbart) |
| `0x7E5E` 📊 | `0x00496588` | primary state pointer | Probable (33 acc, hottest puzzle-own) |

## Architektur-Erkenntnis (Iteration 2)

**Hotel hat in v2 die Constraint-Tabellen aus dem v1 shared FFFF-Segment in normale `.data` migriert.**
v1 nutzte `LocateHotelSharedBase()` via Pattern-Scan, v2 hat sie direkt bei
`0x00496690` und `0x00496698`. Das vereinfacht den v2-Solver erheblich — kein
Memory-Pattern-Scan mehr nötig.

## Difficulty-Dispatcher in Eval (`0x00417679`)

WICHTIG: Diese Dispatcher liest aus `[esp+0x14]` — das ist die **Action-ID**
(1, 2, 3, 4 für verschiedene Game-Aktionen), NICHT Difficulty. Difficulty wird
direkt aus `[0x004967D6]` gelesen (siehe oben).

```
mov esi, [esp+0x14]      ; Action-ID
movsx eax, si
dec eax                  ; 1-basiert → 0-basiert
cmp eax, 3
ja default
jmp [eax*4 + 0x417C2C]
```

## Static Lookups
- `0x0048BFC0` (scale=4, 5 reads) → 5×N-Tabelle (passt zu 5 Hotel-Slots)
- `0x0048C028` (scale=4, 3 reads)

## Hauptlücke
Hotel ist 60-Funktionen groß. Eval-Logik (room-status, constraint-checking) noch
nicht im Detail durchanalysiert. Difficulty + first_time_flag reichen für Solver-
Detektion, aber für tatsächliche Constraint-Auswertung muss die Match-Funktion
identifiziert werden.
