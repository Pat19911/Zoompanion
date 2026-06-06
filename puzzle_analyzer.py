#!/usr/bin/env python3
"""
Zoombinis: Logical Journey — Puzzle Logic Analyzer

Decodes the puzzle data from the Mohawk (.MHK) archives and explains
how each mini-game's logic works technically.

Based on reverse engineering of the MHK resources and cross-referencing
with the ScummVM Mohawk engine documentation.

Resource types used by Zoombinis:
  SCRB  = Feature Script (frame-based hotspot/animation tables)
  SCRS  = Snoid Script (Zoombini character animation sequences)
  NODE  = Walk Node (pathfinding node coordinates)
  PATH  = Walk Path (pathfinding connections)
  SHPL  = Shape List (sprite index lists)
  REGS  = Registration points (sprite positioning offsets)
  SND   = Sound effects (nested MHWK/WAVE)
  tBMP  = Bitmaps (including compound shapes for Zoombini parts)
  tPAL  = Palettes
  tMID  = MIDI music
"""

import struct
import sys
import os
from pathlib import Path
from collections import defaultdict
from mohawk_parser import MohawkArchive


# Zoombini attribute system
ZOOMBINI_TRAITS = {
    'hair': ['Spiked', 'Ponytail', 'Green Cap', 'Straight', 'Balding'],
    'eyes': ['Brown', 'One-eye', 'Sleepy', 'Spectacles', 'Sunglasses'],
    'nose': ['Green', 'Orange', 'Red', 'Purple', 'Blue'],
    'feet': ['Shoes', 'Skates', 'Wheels', 'Propeller', 'Springs'],
}

# Puzzle file mapping
PUZZLE_FILES = {
    'PIZZA':   'Allergic Cliffs (Pizza Pass) — Set theory / Venn diagrams',
    'BRIDGE':  'Stone Cold Caves — Carroll diagrams / attribute filtering',
    'CAVES':   'Stone Cold Caves (Part 2)',
    'FLEENS':  'Fleens! — Attribute mapping / matching',
    'HOTEL':   'Hotel Dimensia — Multi-dimensional sorting',
    'LILLY':   'Titanic Tattooed Toads — Pathfinding / pattern matching',
    'MAZE2':   'Mudball Wall — Combinatorial deduction',
    'NET':     "Captain Cajun's Ferryboat — Graph adjacency / shared attributes",
    'SMOKE':   'Smoke Signals',
    'TUNNELS': 'Stone Rise Tunnels',
    'FERRY':   'Ferry crossing',
    'SLIDES':  'Slides',
    'TOWN':    'Zoombiniville (goal)',
    'PICKER':  'Character Creation (Zoombini maker)',
    'MAP':     'Road Map / Level Select',
    'BASECAMP':'Basecamp (staging area)',
    'BCTWO':   'Basecamp 2',
    'XFER':    'Transfer screen',
}

DIFFICULTY_LEVELS = {
    1: 'Not So Easy',
    2: 'Oh So Hard',
    3: 'Very Hard',
    4: 'So Very Hard',
}


def parse_scrb_frames(data):
    """Parse a SCRB (Feature Script) resource into frames.

    SCRB format:
    - Sequence of frames, each terminated by 0xFF00
    - Each frame contains [shapeID, x, y, ...] hotspot entries
    - 0xFExx = sound trigger (next uint16 is sound resource ID)
    - 0xFFxx (x != 0) = frame end with event code
    """
    frames = []
    pos = 0
    while pos < len(data):
        frame_entries = []
        while pos < len(data) - 1:
            val = struct.unpack_from('>H', data, pos)[0]
            if val >= 0xFF00:
                # Frame terminator
                frame_entries.append(('END', val & 0xFF))
                pos += 2
                break
            elif val >= 0xFE00:
                # Sound trigger
                sound_code = val & 0xFF
                if pos + 2 < len(data):
                    sound_id = struct.unpack_from('>H', data, pos + 2)[0]
                    frame_entries.append(('SOUND', sound_code, sound_id))
                    pos += 4
                else:
                    pos += 2
            else:
                # Regular data value
                frame_entries.append(('DATA', val))
                pos += 2
        frames.append(frame_entries)
    return frames


def parse_scrs(data):
    """Parse a SCRS (Snoid Script) resource.

    Similar to SCRB but with animation variant info for Zoombini characters.
    """
    frames = []
    pos = 0

    # SCRS may have a header with entry count and variant
    if len(data) < 4:
        return {'raw': data.hex()}

    # Parse as sequence of frame groups terminated by 0xFF00
    groups = []
    current_group = []
    while pos < len(data) - 1:
        val = struct.unpack_from('>H', data, pos)[0]
        pos += 2
        if val == 0xFF00:
            groups.append(current_group)
            current_group = []
        else:
            current_group.append(val)
    if current_group:
        groups.append(current_group)

    return groups


def analyze_puzzle_scrb(archive_name, archive):
    """Analyze SCRB resources for a specific puzzle."""
    scrb_tag = None
    for tag in archive.types:
        tag_str = tag.decode('ascii', errors='replace').replace('\x00', '')
        if tag_str == 'SCRB':
            scrb_tag = tag
            break

    if not scrb_tag:
        return

    resources = archive.types[scrb_tag]
    print(f"\n  SCRB Analysis ({len(resources)} scripts):")

    # Group by ID prefix (thousands digit = difficulty level or category)
    id_groups = defaultdict(list)
    for res in resources:
        prefix = res['id'] // 1000
        id_groups[prefix].append(res)

    for prefix, group in sorted(id_groups.items()):
        sizes = [r['size'] for r in group]
        avg_size = sum(sizes) / len(sizes) if sizes else 0
        print(f"    ID range {prefix}xxx: {len(group)} scripts, "
              f"sizes {min(sizes)}-{max(sizes)} bytes (avg {avg_size:.0f})")

        # Analyze first large script in the group
        large = [r for r in group if r['size'] > 100]
        if large:
            res = large[0]
            data = archive.data[res['offset']:res['offset']+res['size']]
            frames = parse_scrb_frames(data)
            sound_count = sum(1 for f in frames for e in f if e[0] == 'SOUND')
            print(f"      Example SCRB_{res['id']}: {len(frames)} frames, "
                  f"{sound_count} sound triggers")


def analyze_node_path(archive_name, archive):
    """Analyze NODE and PATH resources for puzzle navigation."""
    for tag in archive.types:
        tag_str = tag.decode('ascii', errors='replace').replace('\x00', '')
        if tag_str == 'NODE':
            for res in archive.types[tag]:
                data = archive.data[res['offset']:res['offset']+res['size']]
                if len(data) >= 2:
                    count = struct.unpack_from('>H', data, 0)[0]
                    nodes = []
                    for i in range(count):
                        if 2 + i*4 + 4 <= len(data):
                            x, y = struct.unpack_from('>HH', data, 2 + i*4)
                            nodes.append((x, y))
                    print(f"\n  NODE_{res['id']}: {count} navigation points")
                    for i, (x, y) in enumerate(nodes):
                        print(f"    Point {i}: ({x}, {y})")

        if tag_str == 'PATH':
            for res in archive.types[tag]:
                data = archive.data[res['offset']:res['offset']+res['size']]
                if len(data) >= 2:
                    count = struct.unpack_from('>H', data, 0)[0]
                    print(f"\n  PATH_{res['id']}: {count} path(s)")
                    # Show path node sequence
                    path_nodes = list(data[2:])
                    # Filter out trailing zeros
                    path_nodes = [n for n in path_nodes if n > 0]
                    print(f"    Node sequence: {path_nodes}")


def analyze_tBMP(archive_name, archive):
    """Analyze bitmap resources."""
    for tag in archive.types:
        tag_str = tag.decode('ascii', errors='replace').replace('\x00', '')
        if tag_str == 'tBMP':
            resources = archive.types[tag]
            print(f"\n  Bitmaps ({len(resources)} images):")
            for res in resources:
                data = archive.data[res['offset']:res['offset']+res['size']]
                if len(data) >= 12:
                    width = struct.unpack_from('>H', data, 0)[0]
                    height = struct.unpack_from('>H', data, 2)[0]
                    bpp = struct.unpack_from('>H', data, 4)[0]
                    # width might be sub-image count for compound shapes
                    if bpp > 256:
                        # This is likely a compound shape
                        print(f"    tBMP_{res['id']}: Compound shape, "
                              f"{width} sub-images, {res['size']} bytes")
                    else:
                        print(f"    tBMP_{res['id']}: {width}x{height} "
                              f"pixels, {bpp}bpp, {res['size']} bytes")


def analyze_sounds(archive_name, archive):
    """Analyze sound resources."""
    for tag in archive.types:
        tag_str = tag.decode('ascii', errors='replace').replace('\x00', '')
        if tag_str == 'SND':
            resources = archive.types[tag]
            # Group by ID prefix
            id_groups = defaultdict(int)
            total_size = 0
            for res in resources:
                prefix = res['id'] // 1000
                id_groups[prefix] += 1
                total_size += res['size']
            print(f"\n  Sounds ({len(resources)} samples, "
                  f"{total_size/1024/1024:.1f} MB total):")
            for prefix, count in sorted(id_groups.items()):
                print(f"    ID range {prefix}xxx: {count} sounds")


def explain_puzzle_logic(puzzle_name):
    """Print explanation of how a puzzle works algorithmically."""
    explanations = {
        'PIZZA': """
  === ALLERGIC CLIFFS (Pizza Pass) ===
  Mathematisches Konzept: Mengenlehre / Venn-Diagramme

  Mechanik:
  - 2 Klippenwächter ("Allergische Klippen"), je links und rechts
  - Jeder Wächter akzeptiert/lehnt Zoombinis basierend auf Attributen ab
  - Der Spieler muss durch Versuch und Irrtum die Regel herausfinden

  Schwierigkeitsgrade:
    Level 1 (Not So Easy):
      - Ein Wächter lehnt EIN bestimmtes Merkmal ab
      - z.B. "Lehnt alle mit Sonnenbrillen ab"
      - Einfache Menge vs. Komplement

    Level 2 (Oh So Hard):
      - Ein Wächter lehnt 2 Merkmale DESSELBEN Typs ab
      - z.B. "Lehnt Ponytail UND Spiked ab" (akzeptiert nur 3 von 5 Haartypen)

    Level 3 (Very Hard):
      - Wächter lehnt Zoombinis ab, die 2 Merkmale VERSCHIEDENER Typen
        NICHT haben (Schnittmenge)
      - z.B. "Akzeptiert nur grüne Nase UND Rollschuhe"

    Level 4 (So Very Hard):
      - 3 Merkmale aus verschiedenen Typen, keine garantierte
        algorithmische Lösung

  Technische Implementierung:
  - SCRB_7xxx: Animations-/Hotspot-Daten für Level 1
  - SCRB_8xxx: Level 2, SCRB_9xxx: Level 3, SCRB_10xxx: Level 4
  - SCRB_12xxx: Gemeinsame Ressourcen (Zoombini-Erscheinungsdaten)
  - SCRS: Zoombini-Charakter-Animationen (Laufen, Reagieren, Jubeln)
  - NODE: 5 Navigationsknoten (Positionen auf der Klippe)
  - PATH: Verbindung der Knoten (Reihenfolge der Wegpunkte)
  - SND: Reaktions-Sounds (Niesen, Jubeln, Dialog)
  - Die Regel-Generierung erfolgt im Hauptprogramm (EXE) per Zufallsgenerator
    (LCG: seed = 214013 * seed + 2531011, identisch mit MSVC rand())
""",
        'BRIDGE': """
  === STONE COLD CAVES ===
  Mathematisches Konzept: Carroll-Diagramme / Attribute-Filterung

  Mechanik:
  - 4 Höhlen, bewacht von Stein-Troll-Paaren
  - Jeder Troll filtert nach Attributkriterien
  - Zoombinis müssen in die richtige Höhle sortiert werden

  Schwierigkeitsgrade:
    Level 1: Ein Troll blockt alles, einer blockt ein Merkmal
    Level 2: Zwei Trolle filtern nach verschiedenen Einzelmerkmalen
             (2D Carroll-Diagramm)
    Level 3: Trolle filtern nach 2 Merkmalen eines Typs (2/3 Split)
    Level 4: Wächter nutzen verschiedene Merkmal-Typen mit Negation
""",
        'NET': """
  === CAPTAIN CAJUN'S FERRYBOAT ===
  Mathematisches Konzept: Graphfärbung / Adjazenz

  Mechanik:
  - Zoombinis müssen auf Sitzplätze der Fähre gesetzt werden
  - JEDER Zoombini muss mindestens EIN Attribut mit JEDEM
    direkten Sitznachbarn teilen
  - Mathematisch: Einbettung in einen Kompatibilitätsgraphen

  Schwierigkeitsgrade:
    Level 1: Lineare Sitzreihe (Kettengraph)
    Level 2: 2×8 Gitter
    Level 3: 4×4 Gitter
    Level 4: Gedrehtes 4×4 Gitter (bis zu 6 Nachbarn pro Sitz)
""",
        'FLEENS': """
  === FLEENS! ===
  Mathematisches Konzept: Attribut-Mapping / Bijektion

  Mechanik:
  - 16 Fleens entsprechen 16 Zoombinis
  - Zoombinis in Hecken platzieren, um passende Fleens anzulocken
  - Muss in ~6 Zügen abgeschlossen werden

  Schwierigkeitsgrade:
    Level 1-2: Direkte Attribut-Zuordnung
               (Zoombini-Haare → Fleen-Haare, etc.)
    Level 3-4: Zufällige Zuordnung
               (Zoombini-Haare könnten Fleen-Augen entsprechen)
""",
        'HOTEL': """
  === HOTEL DIMENSIA ===
  Mathematisches Konzept: Mehrdimensionale Sortierung

  Mechanik:
  - Zoombinis in Hotelzimmer einordnen
  - Ein Merkmal-Typ pro Achse (Zeile/Spalte/Tiefe)

  Schwierigkeitsgrade:
    Level 1: 5×1 (1 Merkmal = 1D)
    Level 2: 5×5 Gitter (2 Merkmale = 2D)
    Level 3: 5×5 mit blockierten Zimmern
    Level 4: 5×5×5 Würfel (3 Merkmale = 3D!)
""",
        'MAZE2': """
  === MUDBALL WALL ===
  Mathematisches Konzept: Kombinatorische Deduktion

  Mechanik:
  - Schlammkugeln (variierend in Farbe, Form, Innenfarbe) auf
    ein Gitter werfen
  - Die Eigenschaften der Kugel bestimmen den Treffer-Ort
  - Durch Ausprobieren die Zuordnung herausfinden

  Schwierigkeitsgrade:
    Level 1: 5×5 Gitter, ein Attribut pro Achse
    Level 2: 5×5 mit komplexerer Zuordnung
    Level 3-4: 5×5×5 Gitter (ein Attribut wählt die Sektion,
               zwei bestimmen Zeile/Spalte)
""",
        'LILLY': """
  === TITANIC TATTOOED TOADS (Seerosenblätter) ===
  Mathematisches Konzept: Pfadfindung / Mustervergleich

  Mechanik:
  - Kröten mit Tätowierungen (Farbstreifen, Formdesigns)
    springen über Seerosenblätter, die ihren Markierungen entsprechen
  - Jede Kröte ist zweimal verwendbar
  - Höhere Schwierigkeit: Tausch-Zauberstäbe und Krabben
""",
    }

    if puzzle_name in explanations:
        print(explanations[puzzle_name])
    else:
        info = PUZZLE_FILES.get(puzzle_name, 'Unbekanntes Puzzle')
        print(f"\n  {puzzle_name}: {info}")
        print("  (Detaillierte Analyse noch nicht implementiert)")


def analyze_puzzle(mhk_path):
    """Full analysis of a puzzle MHK file."""
    archive = MohawkArchive(str(mhk_path))
    name = mhk_path.stem

    print(f"\n{'='*70}")
    print(f"  PUZZLE: {name}")
    info = PUZZLE_FILES.get(name, 'Unbekannt')
    print(f"  {info}")
    print(f"{'='*70}")

    # Explain the puzzle logic
    explain_puzzle_logic(name)

    # Technical analysis
    print(f"  --- Technische Ressourcen-Analyse ---")
    analyze_tBMP(name, archive)
    analyze_node_path(name, archive)
    analyze_sounds(name, archive)
    analyze_puzzle_scrb(name, archive)


def main():
    data_dir = sys.argv[1] if len(sys.argv) > 1 else 'iso_mount/DATA'

    print("╔══════════════════════════════════════════════════════════════════╗")
    print("║  ZOOMBINIS: LOGICAL JOURNEY — Puzzle-Logik-Analyse             ║")
    print("║  Mohawk Engine (Brøderbund, 1996)                              ║")
    print("╚══════════════════════════════════════════════════════════════════╝")

    print("\n  Zoombini-Attribut-System:")
    print("  Jeder Zoombini hat 4 Merkmale mit je 5 Varianten (= 625 Kombinationen)")
    print()
    for trait, variants in ZOOMBINI_TRAITS.items():
        print(f"    {trait:5s}: {', '.join(variants)}")
    print(f"\n  Zoombini-ID = (hair-1)×125 + (eyes-1)×25 + (nose-1)×5 + (feet-1)")
    print(f"  Zufallsgenerator: MSVC LCG (seed = 214013 × seed + 2531011)")

    # Analyze key puzzle files
    puzzle_order = ['PIZZA', 'BRIDGE', 'NET', 'FLEENS', 'HOTEL',
                    'LILLY', 'MAZE2', 'SMOKE', 'TUNNELS']

    data_path = Path(data_dir)
    for puzzle_name in puzzle_order:
        mhk_path = data_path / f'{puzzle_name}.MHK'
        if mhk_path.exists():
            try:
                analyze_puzzle(mhk_path)
            except Exception as e:
                print(f"\n  FEHLER bei {puzzle_name}: {e}")

    print(f"\n{'='*70}")
    print("  Analyse abgeschlossen.")
    print(f"{'='*70}")


if __name__ == '__main__':
    main()
