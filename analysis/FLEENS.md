# Fleens -- Deep Reverse-Engineering (Segment 27)

## Segment Metadata

| Property | Value |
|----------|-------|
| Code Segment | 27 |
| File Offset | 0x1FE00 |
| Size | 0x2FE3 (12259 bytes) |
| MHK File | Fleens.MHK (DS:0x1018) |
| Relocation Count | 308 |
| rand() calls | 0 (uses seg20:0x04E8 random_range instead) |

### Cross-Segment References (from Seg 27)

| Target Segment | Count | Purpose |
|----------------|-------|---------|
| Seg 20 | 13 | Random number generation (0x04E8 = random_range) |
| Seg 44 | 53 | Engine: sprites, animation, drag/drop, UI |
| Seg 50 | 27 | Sound playback |
| Seg 51 | 78 | Entity/sprite management |
| Seg 53 | 14 | Resource management (MHK loading) |
| Seg 130 | 36 | Shared data segment (Fleen/Zoombini mapping) |
| Seg 152 | 5 | Zoombini entity ID array |
| Seg 27 (self) | 32 | Internal function calls |

## Puzzle Overview

Fleens is an **attribute-mapping puzzle**. Each Zoombini has 4 attributes
(hair, eyes, nose, feet) with 5 possible values each. A set of Fleen
creatures appear on the field, each visually representing one Zoombini's
attributes -- but potentially **permuted** (both values and attribute types).

The player must figure out the mapping rule by trial and observation, then
click each Zoombini so it walks to its matching Fleen.

## Function Map (Segment 27 Internal)

| Address | Description |
|---------|-------------|
| 0x0000 | `fleens_init()` -- zero all state variables |
| 0x00EA | `fleens_main_setup()` -- load resources, create sprites, start puzzle |
| 0x04CB | `set_button_sprite(type, active, show)` -- UI button display |
| 0x0578 | `init_buttons()` -- set up button sprites |
| 0x0596 | `update_ambient_sound(timer_val)` -- ambient/music control |
| 0x0600 | `cleanup()` -- free resources on exit |
| 0x06A8 | `tick()` -- main game loop tick (handles matching, timers, animations) |
| 0x0AE5 | `event_handler(event_type)` -- input dispatch (1=start, 2=help, 3=click) |
| 0x0E54 | `render_fleen_parts(obj_ptr)` -- draw Fleen body parts from script |
| 0x0EE1 | `init_sprite_slots()` -- initialize 0x3B sprite sub-objects |
| 0x0F51 | `play_fleen_entrance_anim(resource_id)` -- entrance animation |
| 0x0F9F | `set_fleen_idle_anim(obj_ptr)` -- idle breathing/movement cycle |
| 0x1194 | `update_fleen_appearance(out_ptr, zoombini_ptr)` -- **CORE: apply attribute mapping** |
| 0x1B29 | `setup_puzzle_round()` -- **CORE: generate permutation, create Fleens** |
| 0x1F25 | `create_fleen_entity(buffer_ptr)` -- instantiate a Fleen sprite |
| 0x1FD1 | `get_sound_resource(type, zoombini_ptr)` -- pick sound effect |
| 0x20EC | `handle_notification(code)` -- handle engine notifications (e.g. 0x88) |
| 0x215C | `animation_event_dispatch(code, obj_ptr)` -- animation state machine events |
| 0x227E | `animation_callback(code, obj_ptr)` -- **CORE: main animation state machine** |
| 0x292A | `get_anim_resource(type, zoombini_ptr)` -- pick animation resource |
| 0x2A06 | `script_callback(code, obj_ptr)` -- secondary animation handler |
| 0x2ACB | `wrong_match_callback(code, obj_ptr)` -- handles wrong match animation |
| 0x2B90 | Similar to 2ACB for different wrong-match state |
| 0x2C91 | `process_match_queue()` -- animate queued correct matches |
| 0x2D63 | `post_correct_callback(code, obj_ptr)` -- after correct match animation |
| 0x2E44 | Similar state callback |
| 0x2F0D | `end_of_round(code, obj_ptr)` -- determine pass/fail result |

## Core Algorithm: Puzzle Generation (`setup_puzzle_round`, 0x1B29)

```c
void setup_puzzle_round() {
    int difficulty = get_difficulty();  // seg44:0x07E0
    DS[0x7e18] = difficulty;           // number of Fleens to create

    // === SELECT SPECIAL CREATURES ===
    // "Special creatures" are boss-type Fleens drawn from alternate pools
    DS[0x7cb4] = random_range(1, difficulty);  // special creature index 1

    if (difficulty >= 2) {
        do {
            DS[0x7cb6] = random_range(1, difficulty);
        } while (DS[0x7cb6] == DS[0x7cb4]);   // must differ from #1
    }

    if (difficulty >= 3) {
        do {
            DS[0x7cb8] = random_range(1, difficulty);
        } while (DS[0x7cb8] == DS[0x7cb4] || DS[0x7cb8] == DS[0x7cb6]);
    }

    // === GENERATE ATTRIBUTE VALUE ROTATION ===
    // Stored at shared_state+0x0C..0x0F (4 bytes, one per attribute type)
    byte *shared = ptr_at(DS[0x2188]);
    int play_count = DS[0x7cea];

    // Regenerate if first play, or if play_count is 1 or 3
    if (shared[0x0C] == 0 || play_count == 1 || play_count == 3) {
        for (int i = 0; i < 4; i++) {
            shared[0x0C + i] = random_range(1, 5);  // shift 1..4
        }
    }
    // shift=1 means IDENTITY (no rotation)
    // shift=2..4 means cyclically rotate attribute values

    // === GENERATE ATTRIBUTE TYPE SHUFFLING (higher difficulty) ===
    // Stored at shared_state+0x10..0x13
    if (play_count > 1) {
        // Only regenerate if not yet set, or if play_count == 3
        if (shared[0x10] == 0 || play_count == 3) {
            shared[0x10] = random_range(2, 4);  // first type target: 2 or 3

            // Fill remaining slots with random permutation of {1,2,3,4}
            // using random-without-replacement (seg18:0x0098)
            uint32_t used_mask = 1 << (shared[0x10] - 1);
            for (int i = 1; i < 4; i++) {
                int picked = random_no_replacement(&used_mask, 4, 0);
                shared[0x10 + i] = lookup_table[picked];
                // lookup_table at DS:0x1010 = {1, 2, 3, 4}
            }
        }
    } else {
        // Low difficulty: no type shuffling
        for (int i = 0; i < 4; i++) {
            shared[0x10 + i] = 0;
        }
    }

    // === CREATE FLEENS ===
    for (int z = 0; z < difficulty; z++) {
        if (!zoombini_alive(z)) continue;

        byte fleen_attrs[4];

        // Apply permutation to each attribute
        for (int a = 0; a < 4; a++) {
            byte original = zoombini_attribute(z, a);  // 1..5
            byte shift = shared[0x0C + a];             // 1..4

            // Modular rotation formula
            byte permuted = ((original + shift - 2) % 5) + 1;

            // Apply type shuffling
            byte target_slot = shared[0x10 + a];
            if (target_slot != 0) {
                fleen_attrs[target_slot - 1] = permuted;
            } else {
                fleen_attrs[a] = permuted;
            }
        }

        // Determine creature pool (special vs normal)
        bool is_special = (z+1 == DS[0x7cb4] ||
                          z+1 == DS[0x7cb6] ||
                          z+1 == DS[0x7cb8]);
        // ... select position and pool based on is_special

        // Create the Fleen sprite
        int fleen_handle = create_fleen_entity(&fleen_attrs, ...);

        // Store the Zoombini-Fleen pairing
        seg130_word[z]     = fleen_handle;  // seg130:[z*2]
        seg130_byte[z+0x20] = 0;           // seg130:[z+0x20] = unmatched
    }
}
```

## Core Algorithm: Attribute Rendering (`update_fleen_appearance`, 0x1194)

This function reads a Zoombini's attributes and looks up the corresponding
Fleen sprite resources. It uses a switch on the animation state (NOT difficulty)
to determine the body-part layout.

```c
// The switch at 0x1257 selects the attribute-to-body-part mapping:
// Switch value comes from the animation script data, NOT directly from difficulty.

switch (anim_state) {  // [bp-0x16], values 0..3
    case 0:  // Standard layout
        body[0] = hair_sprite(zoombini.hair);     // +0xBF
        body[1] = 0;                              // empty slot
        body[2] = eyes_sprite(zoombini.eyes);     // +0xBE
        body[3] = nose_sprite(zoombini.nose);     // +0xBD
        body[4] = feet_sprite(zoombini.feet);     // +0xBC
        break;

    case 1:  // Eyes repositioned
        body[0] = hair_sprite(zoombini.hair);
        body[1] = eyes_sprite(zoombini.eyes);
        body[2] = 0;
        body[3] = nose_sprite(zoombini.nose);
        body[4] = feet_sprite(zoombini.feet);
        break;

    case 2:  // Attribute types rotated
        body[0] = 0;
        body[1] = nose_sprite(zoombini.nose);
        body[2] = eyes_sprite(zoombini.eyes);
        body[3] = hair_sprite(zoombini.hair);
        body[4] = feet_sprite(zoombini.feet);
        break;

    case 3:  // Different rotation
        body[0] = 0;
        body[1] = hair_sprite(zoombini.hair);
        body[2] = eyes_sprite(zoombini.eyes);
        body[3] = nose_sprite(zoombini.nose);
        body[4] = feet_sprite(zoombini.feet);
        break;
}
```

### Sprite Lookup Tables (seg130, pre-initialized)

| Table | Offset | Values (index 0..5) |
|-------|--------|---------------------|
| Hair | 0x11C | 0, 275, 290, 305, 328, 347 |
| Eyes | 0x128 | 0, 15, 30, 45, 60, 75 |
| Nose | 0x134 | 0, 90, 109, 128, 147, 166 |
| Feet | 0x140 | 0, 185, 203, 221, 239, 257 |

Index 0 is unused (represents "no attribute"). Indices 1-5 map to the
5 possible values of each attribute.

## Core Algorithm: Matching

The matching is **index-based**, not position-based:

```c
// When player clicks a Zoombini (event_handler case 3, at 0x0C65):
void on_zoombini_clicked(int zoombini_entity_id) {
    // Search the parallel arrays for this Zoombini
    for (int i = 0; i < DS[0x7d1c]; i++) {
        if (seg152_word[i] == zoombini_entity_id) {
            seg130_byte[i + 0x20] = 1;            // mark as matched
            int fleen_handle = seg130_word[i];     // get the Fleen
            // ... start walk-to-fleen animation
            break;
        }
    }
}
```

When the Zoombini reaches its correct Fleen, the match is recorded:

```c
// In tick() at 0x07F8:
DS[0x7CCA + match_count*2] = zoombini_entity_id;  // matched pair log
DS[0x7CD8 + match_count*2] = fleen_handle;         // matched pair log
DS[0x7CC6]++;  // increment match counter
```

## Permutation Mathematics

### Value Rotation

For a Zoombini attribute value `v` (1-5) and shift `s` (1-4):

```
permuted = ((v + s - 2) mod 5) + 1
```

| Shift | Original 1 | 2 | 3 | 4 | 5 |
|-------|-----------|---|---|---|---|
| 1 (identity) | 1 | 2 | 3 | 4 | 5 |
| 2 | 2 | 3 | 4 | 5 | 1 |
| 3 | 3 | 4 | 5 | 1 | 2 |
| 4 | 4 | 5 | 1 | 2 | 3 |

Each attribute type can have a **different** shift, so hair might shift by 2
while eyes shifts by 3, etc.

### Type Shuffling

On higher difficulty (play_count >= 2), the 4 attribute types are also
shuffled. The type_map is a permutation of {1,2,3,4}:

```
type_map[0] = random from {2, 3}
type_map[1..3] = remaining values from {1,2,3,4} in random order
```

This means attribute type `i` (0=hair, 1=eyes, 2=nose, 3=feet) is
rendered in body slot `type_map[i] - 1` instead of slot `i`.

### Inverse Formulas (for a solver)

To find which Zoombini matches a Fleen:

```python
def inverse_rotate(permuted_val, shift):
    """Undo the value rotation."""
    return ((permuted_val - shift) % 5) + 1
    # Equivalently: ((permuted_val + 5 - shift) % 5) + 1 for non-negative mod

def solve_match(fleen_visual_attrs, shift, type_map):
    """Given a Fleen's visual attributes, find the original Zoombini attributes."""
    zoombini_attrs = [0, 0, 0, 0]
    for a in range(4):
        if type_map[a] != 0:
            slot = type_map[a] - 1
        else:
            slot = a
        permuted_val = fleen_visual_attrs[slot]
        zoombini_attrs[a] = inverse_rotate(permuted_val, shift[a])
    return zoombini_attrs
```

## Complete DS Offset Map

### Puzzle State Variables

| DS Offset | Size | Description |
|-----------|------|-------------|
| 0x7C98 | word | Pending sound effect index |
| 0x7C9A | word | Round result (0xD = all passed) |
| 0x7C9C | word | Background sprite handle |
| 0x7C9E | word | Match progress counter (used for special creature checks) |
| 0x7CA0 | word | Animation layer handle (idle) |
| 0x7CA4 | word | Animation layer handle (active) |
| 0x7CA6 | word | UI layer handle |
| 0x7CAE | word | "Can send hint" flag |
| 0x7CB0 | word | Current Fleen handle being processed |
| 0x7CB2 | word | Current game sub-state (0=idle, 1=animating) |
| 0x7CB4 | word | Special creature index 1 (1-based Zoombini index) |
| 0x7CB6 | word | Special creature index 2 |
| 0x7CB8 | word | Special creature index 3 |
| 0x7CBA | word | Trigger: wrong match animation |
| 0x7CBC | word | Trigger: correct match animation |
| 0x7CBE | word | Trigger: exit animation |
| 0x7CC0 | word | Trigger: process match queue |
| 0x7CC2 | word | "Zoombini returning" flag |
| 0x7CC4 | word | "Fleen walking away" flag |
| 0x7CC6 | word | Match counter (number of completed matches, max 7) |
| 0x7CC8 | word | Auto-match timer counter |
| 0x7CCA..0x7CD6 | 7 words | Matched Zoombini entity IDs log |
| 0x7CD8..0x7CE4 | 7 words | Matched Fleen handles log |
| 0x7CE6 | word | Current entity being animated |
| 0x7CE8 | word | Current Fleen in animation |
| 0x7CEA | word | Play count / session counter (determines difficulty progression) |
| 0x7CEC | word | "Match in progress" flag |
| 0x7CEE | word | Pending Zoombini entity ID (waiting to match) |
| 0x7CF0 | word | Pending Fleen handle (waiting to match) |
| 0x7CF2..0x7CF5 | dword | MHK file handle |
| 0x7CF6 | word | "Resources loaded" flag |
| 0x7CF8 | word | "All fleens ready" flag (set when 3 specials done) |
| 0x7CFA | word | "Puzzle active" flag |
| 0x7CFC | word | "Match attempt in progress" flag |
| 0x7CFE | word | "Blocked from input" flag |
| 0x7D00..0x7D03 | 4 bytes | Sound resource buffer 1 |
| 0x7D04..0x7D07 | 4 bytes | Sound resource buffer 2 |
| 0x7D08..0x7D0B | 4 bytes | Animation resource buffer 1 |
| 0x7D0C..0x7D0F | 4 bytes | Animation resource buffer 2 |
| 0x7D10..0x7D13 | dword | Sprite X-position table pointer |
| 0x7D14..0x7D17 | dword | Sprite Y-position table pointer (horiz) |
| 0x7D18..0x7D1B | dword | Sprite Y-position table pointer (vert) |
| 0x7D1C | word | Total number of Fleens/Zoombinis in this round |
| 0x7D1E..0x7D2D | 4 dwords | Sub-sprite table pointers (for 4 body part layers) |
| 0x7E0A | word | Fleen entrance animation counter (counts down from 8) |
| 0x7E0C..0x7E0F | dword | Last auto-match timestamp |
| 0x7E10..0x7E13 | dword | Auto-match interval (0x78 or 0x3C ticks) |
| 0x7E14..0x7E17 | dword | Random bitmask for auto-match selection |
| 0x7E18 | word | Difficulty level (= number of Fleens) |

### Shared Puzzle State (via far pointer at DS:0x2188)

| Offset from ptr | Size | Description |
|------------------|------|-------------|
| +0x0C..+0x0F | 4 bytes | Attribute value shifts (1=identity, 2-4=rotate) |
| +0x10..+0x13 | 4 bytes | Attribute type shuffle map (0=no shuffle, 1-4=target slot) |
| +0x38 | word | Puzzle completion/state flags |
| -0x56C4 + i*0x13 | byte | Zoombini `i` alive flag |
| -0x56CC + i*0x13..+3 | 4 bytes | Zoombini `i` attribute values (hair, eyes, nose, feet) |
| -0x56C3 + i*0x13..+9 | 10 bytes | Zoombini `i` additional visual/state data |

### Segment 130 (Fleen Data)

| Offset | Size | Description |
|--------|------|-------------|
| 0x00..0x1E | 16 words | Fleen sprite handles (parallel with seg152) |
| 0x20..0x2F | 16 bytes | Match flags: 0=unmatched, 1=matched, 2=exiting |
| 0x30..0x11B | | Sub-sprite object data (runtime) |
| 0x11C..0x127 | 6 words | Hair sprite lookup table (index 0..5) |
| 0x128..0x133 | 6 words | Eyes sprite lookup table |
| 0x134..0x13F | 6 words | Nose sprite lookup table |
| 0x140..0x14B | 6 words | Feet sprite lookup table |

### Segment 152 (Zoombini IDs)

| Offset | Size | Description |
|--------|------|-------------|
| 0x00..0x1E | 16 words | Zoombini entity IDs (parallel with seg130) |

## Fleen Positions (DS:0x0FC2)

16 Fleen positions, stored as (x, y) word pairs:

| Index | X | Y |
|-------|---|---|
| 0 | 238 | 368 |
| 1 | 185 | 417 |
| 2 | 155 | 448 |
| 3 | 197 | 396 |
| 4 | 160 | 357 |
| 5 | 164 | 384 |
| 6 | 150 | 416 |
| 7 | 116 | 357 |
| 8 | 130 | 386 |
| 9 | 109 | 418 |
| 10 | 117 | 448 |
| 11 | 74 | 348 |
| 12 | 89 | 384 |
| 13 | 67 | 418 |
| 14 | 76 | 450 |
| 15 | 56 | 379 |

## Difficulty Progression

The difficulty is controlled by two variables:
- `DS[0x7e18]` = number of Fleens (from the engine's difficulty system)
- `DS[0x7cea]` = play count (how many times this puzzle has been attempted)

| Play Count | Value Rotation | Type Shuffle | Effect |
|------------|---------------|--------------|--------|
| 0 or 1 | Random shifts (may include identity) | None | Attributes may be cyclically shifted but types stay in place |
| 2 | Keep previous shifts | Generate new shuffle | Attribute types are also permuted |
| 3 | Regenerate shifts | Regenerate shuffle | Full re-randomization of both layers |

Additionally:
- `DS[0x7c9e]` tracks match progress within a round
- When `DS[0x7c9e] >= 3`, auto-hints are triggered (`DS[0x9edc] = 1`)
- Auto-match timer varies: 0x78 ticks if `DS[0x9904] != 0`, else 0x3C ticks

## Game Flow

1. `fleens_init()` zeroes all state
2. `fleens_main_setup()` loads MHK resources, creates background, calls `setup_puzzle_round()`
3. `setup_puzzle_round()` generates permutation and creates Fleen sprites
4. Entrance animation plays (8 Fleens entering one by one, countdown at 0x7E0A)
5. Player interaction:
   - Click Zoombini -> Zoombini walks to its Fleen automatically
   - The correct Fleen is determined by the parallel array lookup
   - Match is recorded in the log arrays at 0x7CCA/0x7CD8
6. After 7 matches (or timeout): `process_match_queue()` plays reward animations
7. `end_of_round()` determines outcome:
   - All 16 matched -> `DS[0x7c9a] = 0x0D` (full pass)
   - 9-15 matched -> `DS[0x7c9a] = count - 8` (partial pass)
   - 8 or fewer -> `DS[0x7c9a] = 0` (fail)

## Solver Pseudocode

```python
def solve_fleens(zoombini_attrs_list, fleen_visual_list, shift, type_map):
    """
    Given:
      zoombini_attrs_list: list of 16 Zoombinis, each [hair, eyes, nose, feet] (1-5)
      fleen_visual_list: list of 16 Fleens, each [slot0, slot1, slot2, slot3] (1-5)
      shift: [s0, s1, s2, s3] the value rotation per attribute type (1-4)
      type_map: [t0, t1, t2, t3] the type shuffle (0 or 1-4)

    Returns: list of (zoombini_index, fleen_index) pairs
    """
    matches = []
    for zi, z_attrs in enumerate(zoombini_attrs_list):
        # Compute what this Zoombini's Fleen should look like
        expected_fleen = [0, 0, 0, 0]
        for a in range(4):
            permuted = ((z_attrs[a] + shift[a] - 2) % 5) + 1
            slot = (type_map[a] - 1) if type_map[a] != 0 else a
            expected_fleen[slot] = permuted

        # Find matching Fleen
        for fi, f_visual in enumerate(fleen_visual_list):
            if f_visual == expected_fleen:
                matches.append((zi, fi))
                break

    return matches

# To DEDUCE the shift and type_map from observations:
# 1. Try one Zoombini and see which Fleen it matches
# 2. From the Zoombini's known attrs and Fleen's visual attrs,
#    deduce shift values and type mapping
# 3. Verify with a second pair to confirm
```

## Analysis Status

**COMPLETE** -- Full algorithm reconstructed including:
- Puzzle generation with value rotation and type shuffling
- The inverse formula for solving
- All DS offsets needed for runtime inspection
- Complete game flow from init to result determination


---

# v2 (PE32) — Fleens-Code-Karte

> Vollständige Tabelle: `V2_VARIABLE_MAP.md` § 4.

## Code-Lokalisation

| | v1 | v2 |
|---|----|----|
| Code | Seg 27 | `.text` 0x00411E80..0x004124C0 |
| MHK-Loader | — | `0x00411E80` (1600 B, 1 Caller) |
| Wrapper | — | `0x00410520` (3408 B, „Play FrogMan SCRB id:") |
| Difficulty body | — | `0x00413390` |

## Difficulty-Dispatcher

```
0x004133FE  cmp ecx, 3
0x00413407  jmp [ecx*4 + 0x413784]    (4 cases)
```
Difficulty kommt als CX-Register (vorher aus `[ebx+0xC4]` einer Struktur kopiert) —
**Argument-basiert via Strukturfeld**, nicht direkt aus globaler VA.

## Probable Mappings

| v1 DS | v2 VA | Bedeutung | Konfidenz |
|-------|-------|-----------|-----------|
| `0x7CEA` ⚠️ | `0x00495D30` | difficulty/play_count (cmp 3) | Probable |
| `0x7CB2` 📊 | `0x00495C1A` | primary state | Speculative |
| Static lookups | `0x0048BEBC..0x0048BEE0` | Lookup-Tabellen (4 reads scale=2) | Probable |

## Lücke
Globale Difficulty-Quelle ist `[ebx+0xC4]` Struktur-Field. Welche globale Pointer-VA
ist `ebx`? Trace-Iteration ausstehend.
