# Eval/Action-Handler-Funktionen aller 12 Puzzles (verifiziert 2026-04-25)

Identifiziert via Byte-Signatur-Suche nach Action-Dispatcher (cmp ax, 1 / cmp ax, 2 / cmp ax, 3
in enger Folge). Action-Handler verarbeiten Spieler-Interaktionen mit case-Verzweigungen
(Action 1 = Setup, Action 2 = Placement, Action 3 = Evaluate).

| # | Puzzle | Seg | Eval-Funktion | Größe | Konfidenz |
|---|--------|-----|---------------|-------|-----------|
| 1 | Allergic Cliffs | 23 | **0x16C99** | 647 B | ✅ HOCH (3× cmp-ax-1) |
| 2 | Stone Cold Caves | 24 | **0x19A74** | 605 B | ✅ HOCH (2×) |
| 3 | Captain Cajun Ferry | 26 | **0x1E184** oder 0x1DC6D | 1138 B / 203 B | ⚠️ |
| 4 | Fleens | 27 | **0x21929** | 1020 B | ⚠️ (ist auch der Generator!) |
| 5 | Hotel Dimensia | 28 | **0x254EA** | 1439 B | ✅ (bekannt aus alter Doku) |
| 6 | Tattooed Toads | 30 | **0x30502** | 2468 B | ✅ HOCH (3× cmp-ax-1) |
| 7 | Bubblewonder Abyss | 34 | **0x41AF0** oder 0x3F085 | 1302 B / 1796 B | ⚠️ |
| 8 | Mudball Wall | 35 | **0x48B25** | 424 B | ⚠️ (klein) |
| 9 | Pizza Pass | 37 | **0x50664** oder 0x4EBE6 | 960 B / 217 B | ⚠️ |
| 10 | Mirror Machine | 42 | **0x5DCAD** oder 0x5E629 | 316 B / 2633 B | ⚠️ |
| 11 | Stone Rise | 45 | **0x6CDAD** oder 0x6D2F5 | 203 B / 3659 B | ⚠️ |
| 12 | Lion's Lair | 48 | **0x76FEC** | 1462 B | ⚠️ |

## Action-Dispatcher-Pattern (verifiziert bei Cliffs FUNC #6 @ 0x16C99)

```c
void action_handler(int action) {
    if ([anim_state] != 0) {
        // Animation in progress, defer
        return;
    }

    switch (action) {
        case 1: setup_animation();              break;
        case 2: handle_placement();             break;
        case 3: evaluate_zoombini_at_cliff();   break;  // ← Match-Logik
    }
}
```

## Match-Logik (verifiziert bei Cliffs in Action 3)

```c
case 3:  // Evaluate
    if (engagement_flag != 0) return;
    if (attempt_counter >= 6) return;  // bridge collapsing

    zoombini = get_active_zoombini();
    cliff_choice = get_cliff_choice();

    // Call check function (lcall to seg44:0x162a or seg44:0x16fe)
    result = check_zoombini_against_allergy(zoombini, cliff_choice);

    if (result == ACCEPT) {
        store_zoombini_data(zoombini, cliff_choice);
        animate_walk_across();
    } else {
        // Sneeze!
        play_sneeze_animation();
        attempt_counter++;
    }
```

## Struktur-Übersicht der Match-Helper-Aufrufe

Je nach Pattern unterscheidet sich der Match-Aufruf:

| Pattern | Match-Helper |
|---------|--------------|
| A (Cliffs, Lion's Lair) | Match wird bereits in Init pre-computed (Pool/Selection); Eval liest nur die vorberechnete Lösung |
| B (Mirror) | Eval liest 70/30-Pattern aus globalen Lookup-Tabellen |
| C (Caves, Pizza, Ferry, Fleens, Toads, Bubble, Mudball, Stone Rise) | Eval liest die Init-Werte direkt aus DS und vergleicht |
| D (Hotel) | Eval liest die validierten Werte (post-retry) |

## Was noch zu tun

Pro Puzzle die action-3-Branch (Match-Logik) komplett disassemblieren — das wäre eine
weitere Iteration für vollständige Match-Algorithmen. Für funktionierende Solver oft
nicht zwingend nötig, da das Spiel die Lösung in DS speichert (wir lesen nur).

## Querverweise

- `eval_finder.py` — Skript zur Reproduktion der Identifikation
- `analysis/ALLERGIC_CLIFFS.md` — Vollständige Eval-Analyse (Referenz)
- `analysis/_MECHANIK_PATTERNS.md` — Pattern-Klassifikation
- `analysis/_DS_VARIABLES_VERIFIED.md` — DS-Karte aller Puzzles
