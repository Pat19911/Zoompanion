# Drag-Detection — wie das Spiel den gehaltenen Zoombini identifiziert

> **Kanonische Datei. Stand: 2026-04-27.** Ergebnis von Phase 0 (RE) des Drag-Detection-Plans.
> Konfidenz: **Verified durch direktes Disasm der Pickup-Funktion in v2 PE32**.

## TL;DR

Der gehaltene Zoombini ist nicht über eine globale `.data`-Variable identifizierbar. Er
trägt sein eigenes Identitäts-Flag im Linked-List-Knoten:

| Adresse | Größe | Bedeutung |
|---|---|---|
| `[node + 0x12C]` | byte | `0` = nicht gehalten, `1` = gerade gehalten |

Strategie: Linked-List bei `*(0x004A35C0)` walken, Knoten mit `[+0x12C] == 1` finden.

## Adress-Korrekturen

| Was im Briefing/alten Code stand | Tatsächlich |
|---|---|
| Drag-Flag `[0x00494928]` | **Falsch.** `0x494928` ist ein Cliff-Sprite-Animation-Slot. Verifiziert: in 2 von 5 Drag-aktiven Memdumps = 0. |
| Drag-Flag `[0x00494522]` | **Korrekt.** In allen 5 Memdumps mit Drag = 1. Writer ist `fn 0x00406760` (`mov word ptr [0x494522], 1` bei `0x004068C7`). |
| Pool-Index `[0x0049B0E0]` | **Falsch (Sackgasse).** Writer `fn 0x0042BC20` ist eine Sprite-Animation-Tick-Funktion. Der Block `0x49B0E0..0x49B0F0` ist Animations-State (Frame-Counter, Phase, Timer). Die in den Dumps gesehenen Werte 10/12/1/2/2 waren zufällig im 0..15-Range eines Frame-Index. |

## Wie das Spiel intern den Zoombini findet

Code-Trace der Pickup-Funktion `fn 0x00406760` (PE32, ImageBase 0x00400000):

```asm
; Vor dem Pickup (Caller setzt das Handle)
0x004068C3  lea  eax, [esp+0x10]            ; Pointer auf Stack-Slot
0x004068C7  mov  word ptr [0x494522], 1     ; Drag-Flag = 1
0x004068D0  push eax
0x004068D1  call 0x4468f0                   ; vermutl. Hit-Test mit Maus-Pos
0x004068D6  mov  ecx, dword ptr [esp+0x14]  ; ecx = Handle (vom Stack)
0x004068DD  push 1
0x004068DF  push 1
0x004068E1  push ecx                        ; Handle-Argument
0x004068E2  call 0x455f70                   ; Linked-List-Resolver: Handle → Node-Ptr
0x004068E7  mov  esi, eax                   ; esi = Node des gehaltenen Zoombini

; Plausibilitätschecks am Knoten
0x004068F1  mov  al, byte ptr [esi+0x128]   ; (1) lese state-byte
0x004068F9  cmp  al, 8
0x004068FB  je   0x406a52                   ; wenn state=8 → abort
0x00406901  cmp  byte ptr [esi+0x12c], bl   ; (2) ist „held"-flag schon 1?
0x00406907  jne  0x406a52                   ; wenn ja → abort

; Später, beim Pickup-Erfolg (validiert)
0x004064FB  mov  byte ptr [esi+0x12c], 1    ; markiert Knoten als gehalten ⭐
```

### Was `+0x128` und `+0x12C` sind

| Offset | Beobachtetes Verhalten | Interpretation |
|---|---|---|
| `+0x128` (byte) | Pickup-Pre-Check liest, abort wenn `==8`. | Vermutl. „state machine"-Byte (Animation-State). |
| `+0x12C` (byte) | Pickup-Pre-Check liest, abort wenn `!=0`; Pickup-Erfolg setzt `=1`. | **„Bin gerade gehalten"-Flag.** |
| `+0x1A` (word) | Vom Pickup gelesen + verglichen mit `[0x4945A4]`. | Vermutl. Y-Position (passt zu PoolScanner-Annahme). |
| `+0x20` (dword) | Vom Resolver `0x455F70` als Handle-Identity gelesen. | Handle-Wert. |
| `+0xC0..+0xC3` (4 bytes) | Hair/Eyes/Nose/Feet 1..5. | **Verifiziert** über alle Pool-Records. |

### Die echte Pickup-Frage: woher kommt das Handle?

Nicht über eine Global. `0x406760` bekommt das Handle als Funktions-Argument vom
Caller `fn 0x00406270`. Dort wird es vor dem Call vom Stack gelesen — der ein
Argument der Caller-Funktion ist. Das deutet darauf hin, dass das Handle vom
**UI-Event-Loop weitergereicht** wird (Maus-Click → Hit-Test in `0x4468f0` →
Sprite-Index → Handle).

Das ist auch der Grund warum keine Drag-Identity in `.data` zu finden ist: das
Spiel nutzt das gewöhnliche Win32/Engine-Event-Modell, kein globaler Indirektor.

## Strategie für den ZoombiniHelper

Da kein globaler Identifier existiert, **nehmen wir die direkte Konsequenz aus
dem Code:** wir lesen das `[node+0x12C]`-Byte für jeden Knoten in der Linked-Liste.

```text
1. Linked-Liste bei *(0x004A35C0) walken
2. Pro Knoten: lese byte bei (node+0x12C)
3. Knoten mit Wert 1 = gehaltener Zoombini
4. Attribute am selben Knoten bei (+0xC0..+0xC3)
```

Das funktioniert puzzleübergreifend — die Linked-Liste ist Engine-State, die
Felder `+0x12C` und `+0xC0..C3` werden vom Pickup-Code für jedes Puzzle gleich
benutzt.

### Was zu beachten ist

- **Knoten-Größe**: unser bisheriger `WalkObjectList` liest nur `0xC4` Bytes
  pro Knoten — das ist zu wenig. `+0x12C` braucht mindestens `0x130` Bytes.
- **Drag-Flag-Prüfung als sanity check**: `[0x494522] == 1` muss gelten,
  sonst kann `[+0x12C] == 1` nicht stimmen. Diskrepanz = Memory-Read-Race oder
  stale data.
- **Race condition**: zwischen "Pickup gesetzt" und "+0x12C geschrieben"
  liegen mehrere Code-Pfade (Validierungen, Sound). In ungünstigen Frames
  könnte `[0x494522] = 1` aber `[+0x12C] = 0` sein. Praktisch unwahrscheinlich
  bei 200 ms Tick.

## Offene Punkte

- **Live-Verifikation der `+0x12C`-These** auf laufendem Spiel mit kontrolliertem
  Pickup-Protokoll (5–10 Pickups, je `WalkObjectList` mit 0x130-byte Reads).
- **Disasm von `0x4468F0`** wenn wir mal die Hit-Test-Mathematik brauchen
  (z.B. um Geste-zu-Zoombini-Mapping zu rekonstruieren). Heute nicht nötig.

## Was nicht mehr untersucht werden muss

- `[0x49B0E0]`, `[0x49B0E4]`, `[0x49B0E6]`, `[0x49B0F0]` — alle Sprite-Animation,
  keine Drag-Identity.
- `[0x4A23E8]`, `[0x4A2402]` — Writer in `fn 0x44B3D0` (nicht analysiert,
  aber außerhalb des Drag-Pfads).
- `[0x494928..0x49492E]` — Cliff-Sprite-Slots, nicht Held-Zoombini-Attribute.

## Quellen

- v2-Binary disasm via `pe_loader.PEBinary.default()` (= `v2_bin/ZoombinisLJ.exe`)
- Skripte: `scratch/find_drag_writers.py`, `scratch/disasm_drag_writers.py`
- 5 Live-Memdumps mit drag=1 in dem Dump-Ordner neben der EXE (`memdump-*.txt`)
