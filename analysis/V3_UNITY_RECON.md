# Zoombinis v3 (Unity / Steam-Remake) — Erste Recon

**Quelle**: `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Zoombinis/`

## Engine

- **Unity** mit IL2CPP-Backend (Unity 2022/2023, IL2CPP-Metadata-Version **31**)
- 64-bit (PE32+)
- `GameAssembly.dll` 41 MB native compiled
- `global-metadata.dat` 10 MB Klassen/Methoden/Felder-Tabelle
- **Alle C#-Class- und Field-Namen intakt** — keine Obfuscation

## Vorteile vs Mohawk

| | Mohawk (v1+v2) | Unity (v3) |
|---|----------------|------------|
| Symbol-Information | Keine | Komplett (Klassen, Felder, Methoden) |
| State-Discovery | Statisch fixe Adressen | GameObject-Instanzen auf GC-Heap |
| Pro Puzzle | Tagelange Disasm | Field-Read aus Controller |
| Constraint-Logik | In SCRB-Bytecode versteckt | In Klassen-Methoden lesbar |

## Identifizierte Puzzle-Controller (12)

Alle sind im IL2CPP-Metadata gefunden. Jeder ist ein einzelner Controller mit allen
relevanten State-Feldern:

| Klasse | Felder | Wichtigste State-Felder |
|--------|--------|------------------------|
| **AllergicCliffsController** | 34 | `_zoombinis`, `_firstCriterion`, `_secondCriterion`, `_thirdCriterion`, `_attempts`, `_zoombinisCrossed` |
| **StoneColdCavesController** | 45 | `_zoombinis`, `_firstCriterion`..`_fourthCriterion`, `_caveTrolls`, `_attempts`, `_zoombinisPassed` |
| **PizzaPassController** | 37 | `_zoombinis`, `_trolls`, `_currentZoombini`, `_currentTries`, `_minToppings`, `_maxPizzaToppings` |
| **HotelDimensiaController** | 23 | `_zoombinis`, `_traitCounts`, `_num_rows`, `_num_cols`, `_doors`, `_solutionAttributes`, `_barredDoorNumbers` |
| **FleensController** | 62 | `_zoombinis`, `_fleens`, `_currentZoombini`, `_currentFleen`, `attributeMap`, `_zmbFleenMatch` |
| **MudballWallController** | 38 | `_zoombinis`, `_currentMudballConfig`, `_mudballTile`, `_boulderType`, `_zoombinisToLaunch` |
| **BubblewonderAbyssController** | 53 | `_zoombinis`, `_grid`, `_zoombiniLocation`, plus große `LEVEL_*_GRIDS`-Konstanten |
| **LionsLairController** | 23 | `_zoombinis`, `_bodyParts`, `_typesOfParts`, `_rowsOfHieroglyphs`, `_solutionAttributes` |
| **MirrorMachineController** | 25 | `_zoombinis`, `_shards`, `_currentLevel`, `_mirrorLevel0`..`3` |
| **FerryboatController** | 30 | `_zoombinis`, `_boatConfigurations`, `_boatConfiguration`, `_puzzleLevel`, `_incorrectZoombini`, `_incorrectSeatIndex` |
| **StoneRiseController** | 36 | `_zoombinis`, `_zoombiniData`, `_zoombiniTileMap`, `_poweredGridTiles`, `_finished` |

Plus Hilfs-Klassen wie `StoneRiseGrid`, `StoneRiseNode`, `MudballWallGrid`, `BubblewonderAbyssGrid`,
`MudballWallTile`, `BubblewonderAbyssTile`, `StoneRiseTile`, `StoneRiseConnection`.

## StoneRiseGrid Beispiel (komplett dekodiert)

```csharp
public class StoneRiseGrid {
    /* 0*/ X _nose;                    // Nose-Symbol-Connector
    /* 1*/ X _noseActive;
    /* 2*/ X _hair;                    // Hair-Connector
    /* 3*/ X _hairActive;
    /* 4*/ X _eyes;                    // Eyes-Connector
    /* 5*/ X _eyesActive;
    /* 6*/ X _feet;                    // Feet-Connector
    /* 7*/ X _feetActive;
    /* 8*/ X _connectionDiagonalLeft;
    /* 9*/ X _connectionDiagonalLeftActive;
    /*10*/ X _connectionDiagonalRight;
    /*11*/ X _connectionDiagonalRightActive;
    /*12*/ X _connectionHorizontal;
    /*13*/ X _connectionHorizontalActive;
    /*14*/ X _nodeLookup;              // ← Dictionary node-id → StoneRiseNode
    /*15*/ X _roots;                   // Liste von Wurzel-Knoten
    /*16*/ X _singleton;               // Static instance
}

public class StoneRiseNode {
    /* 0*/ X _position;
    /* 1*/ X _tile;          // → StoneRiseTile (welcher Stein hier)
    /* 2*/ X _parentTiles;
    /* 3*/ X _powered;       // Bool — Stein leuchtet
    /* 4*/ X _left;          // Nachbar-Knoten
    /* 5*/ X _down;
    /* 6*/ X _right;
}
```

## Was zu tun ist (= geschätzter Aufwand)

| Schritt | Aufwand |
|---------|---------|
| 1. **Field-Offsets extrahieren** aus IL2CPP-Metadata-Tables (parameters/types/etc.) — ergibt für jede Klasse die exakten Bytes-Offsets jedes Feldes | **3-4 h** |
| 2. **Mono-Runtime-Reader** schreiben — finden der aktiven Controller-Instanz auf dem GC-Heap. Ansätze: a) Heap-Scan nach VTable-Pointer  b) Unity-GameObject-Tree walken über `UnityPlayer.dll` symbols  c) Mono-API direkt | **8-12 h** (R&D-intensiv) |
| 3. **Backend-Abstraktion** in C#: `IGameBackend` mit `MohawkBackend` + `UnityBackend` | **3-4 h** |
| 4. **Per Puzzle Unity-State-Decoder** | **~2-3 h × 12 Puzzles = 24-36 h** |

**Gesamt: ~40-55 h** für vollständigen v3-Support. Etwa 1-2 Wochen fokussierte Arbeit.

## Pragmatische Strategie

Statt alles auf einmal:
1. **Phase 1**: Mono-Reader-Prototyp + EIN Puzzle (z.B. StoneRise) — 12-15 h
   → Beweist Machbarkeit, validiert die Architektur
2. **Phase 2**: Backend-Abstraktion einbauen, Helper läuft auf v2 ODER v3
3. **Phase 3**: Restliche 11 Puzzles dazu

## Tools die fehlen

- **Il2CppDumper** (https://github.com/Perfare/Il2CppDumper) — generiert vollständige
  C#-Pseudo-Klassen aus den Metadaten. Wir haben einen rudimentären Python-Parser
  geschrieben, aber Il2CppDumper macht das industriell sauber inkl. Field-Offsets.
- **dnSpy** oder **ILSpy** — falls die DummyDLL aus Il2CppDumper benutzt wird
- **Cheat Engine** mit Mono-Plugin — für Live-Inspection des Heaps

## Fazit

Die Steam-Version ist **wesentlich besser zugänglich** als die Mohawk-Versionen.
Aber sie braucht eine ganz andere Memory-Reading-Architektur. Die existierende
Helper-Infrastruktur für v2 bleibt unverändert; v3 wäre ein paralleler Backend.
