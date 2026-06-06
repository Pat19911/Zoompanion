# Pizza Trolls Puzzle - Complete Reverse Engineering Analysis

## Binary Location

- **Executable**: `iso_mount/ZOOMBINI._EX` (16-bit NE format)
- **Puzzle code**: Segment 37, file offset `0x4E000`, size 24368 bytes (`0x5F30`)
- **Data segment**: Segment 191 (auto DS), file offset `0xD6A00`, size `0xBA52`
- **Resource file**: `Pizza.MHK` (referenced at DS:`0x19C3`)

All "seg offset" values below are relative to segment 37 base (`0x4E000`).
All "DS offset" values are relative to the data segment base (`0xD6A00`).

---

## Executive Summary

The Pizza Trolls puzzle has 1-3 trolls blocking a bridge. Each troll wants specific toppings on a pizza. The player toggles toppings on/off and offers the pizza to a troll. The game checks whether the offered toppings match the troll's randomly-generated preferences.

The core algorithm:
1. At init, a random subset of topping slots are selected and randomly distributed among the active trolls.
2. Each troll's preference array records which toppings it wants (1) or does not want (0).
3. When the player offers a pizza, the game compares the pizza's toppings against the troll's preferences and returns a correctness result.

---

## Difficulty Configuration

The difficulty is stored at `[0x96D2]` (values 0-3). It determines the number of toppings, trolls, and assignment parameters.

| Variable    | DS Offset | Diff 0 | Diff 1 | Diff 2 | Diff 3 |
|-------------|-----------|--------|--------|--------|--------|
| `num_trolls` (`0x96D6`) | | 1 | 2 | 2 | 3 |
| `num_toppings` (`0x96D8`) | | 5 | 7 | 7 | 8 |
| `assign_count` (`0x96DC`) | | 2 | 3 | 3 | 4 |
| `assign_threshold` (`0x96DA`) | | 500 | 800 | 1000 | 1000 |
| `special_flag` (`0x96DE`) | | 0 | 0 | 1 | 2 |
| `max_pizzas` (`0x96D4`) | | 6 | 7 | 7 | 7 |
| `arno_active` (`0x96CC`) | | 1 | 1 | 1 | 1 |
| `willa_active` (`0x96CE`) | | 0 | 1 | 1 | 1 |
| `shyler_active` (`0x96D0`) | | 0 | 0 | 1 | 1 |

**File offsets of difficulty configuration blocks:**
- Diff 0: `0x4E4A7` (seg `0x06A7`)
- Diff 1: `0x4E5BE` (seg `0x07BE`)
- Diff 2: `0x4E702` (seg `0x0902`)
- Diff 3: `0x4E873` (seg `0x0A73`)

---

## Global Variables

### Core State

| DS Offset | Description |
|-----------|-------------|
| `0x96D2` | Difficulty level (0-3) |
| `0x96D4` | Max pizza count for current round |
| `0x96D6` | Number of active trolls |
| `0x96D8` | Number of topping slots (5, 7, 7, or 8) |
| `0x96DA` | Random assignment threshold (out of 1000) |
| `0x96DC` | Number of topping slots to assign in base generation |
| `0x96DE` | Special mode flag (related to extra toppings) |
| `0x96E0` | Difficulty + 5 (used for resource lookup) |

### Troll Satisfaction State

| DS Offset | Description |
|-----------|-------------|
| `0x96CC` | Arno state: 1=active, 2=accepting, 3=satisfied |
| `0x96CE` | Willa state: 0=absent, 1=active, 2=accepting, 3=satisfied |
| `0x96D0` | Shyler state: 0=absent, 1=active, 2=accepting, 3=satisfied |

### Topping Toggle State Variables

These track whether each topping is currently ON (1) or OFF (0) on the player's pizza.

| DS Offset | Topping Index | Meal Array Target | Availability |
|-----------|---------------|-------------------|--------------|
| `0x9706` | 0 | `0x977E` | All difficulties |
| `0x9704` | 1 | `0x9780` | All difficulties |
| `0x9702` | 2 | `0x9782` | All difficulties |
| `0x9700` | 3 | `0x9784` | All difficulties |
| `0x96FE` | 4 | `0x9786` | All difficulties |
| `0x9708` | 5 | `0x9788` | Difficulty >= 1 |
| `0x970A` | 6 | `0x978A` | Difficulty >= 1 |
| `0x970C` | 7 | `0x978C` | Difficulty == 3 only |

### Topping Sprite Handles

| DS Offset | Diff 0 Resource | Diff 1 Resource | Diff 2 Resource | Diff 3 Resource |
|-----------|----------------|----------------|----------------|----------------|
| `0x96EA` | `0x1B5D` | `0x1B67` | `0x1B73` | `0x1B81` |
| `0x96EC` | `0x1B5F` | `0x1B69` | `0x1B75` | `0x1B83` |
| `0x96EE` | `0x1B61` | `0x1B6B` | `0x1B77` | `0x1B85` |
| `0x96F0` | `0x1B63` | `0x1B6D` | `0x1B79` | `0x1B87` |
| `0x96F2` | `0x1B65` | `0x1B6F` | `0x1B7B` | `0x1B89` |
| `0x96F4` | N/A | `0x1B71` | `0x1B7D` | `0x1B8B` |
| `0x96F6` | N/A | N/A | `0x1B7F` | `0x1B8D` |
| `0x96F8` | N/A | N/A | N/A | `0x1B8F` |

### Arrays (8 words each, indexed by topping slot 0-7)

| DS Offset | Array Name | Description |
|-----------|------------|-------------|
| `0x972A` | `base_assign[]` | Base topping assignment flags (generated randomly) |
| `0x973A` | `arno_wants[]` | Arno's desired toppings (1=wants, 0=doesn't) |
| `0x974A` | `willa_wants[]` | Willa's desired toppings |
| `0x975A` | `shyler_wants[]` | Shyler's desired toppings |
| `0x977E` | `meal_state[]` | Current pizza topping state (for display/Meal debug) |
| `0x978E` | `offered[]` | Toppings on the currently offered pizza (used in eval) |

### Other Important Variables

| DS Offset | Description |
|-----------|-------------|
| `0x96A4` | Tray/plate animation sprite handle |
| `0x96A6` | Pizza animation state |
| `0x96B2` | Current troll being served indicator |
| `0x96B4` | Arno's animation handle |
| `0x96B6` | Willa's animation handle |
| `0x96B8` | Shyler's animation handle |
| `0x96C2` | Arno's sprite handle |
| `0x96C4` | Willa's sprite handle |
| `0x96C6` | Shyler's sprite handle |
| `0x96CA` | Background/table sprite handle |
| `0x96E2` | Pizza/meal sprite handle |
| `0x9718` | Current pizza display sprite |
| `0x9720` | Saved value (restored on exit) |
| `0x9724` | Progress indicator (which phase of puzzle) |
| `0x97BC` | Pizza history index (how many pizzas offered so far) |
| `0x97BE` | Flag (set to 1 at init) |
| `0x97C2` | Counter for animation frames |
| `0x97C4` | Some animation parameter |
| `0x97C6` | Animation frame counter |
| `0x97D2` | State flag (set to 1 when game active) |
| `0x97F6-0x97F8` | Saved timer value (dword) |
| `0x979E` | Accept counter |
| `0x97A0` | Pizza history bitmask array (records all previous pizza offerings) |

---

## Key Functions

### 1. Main Init Function
- **File offset**: `0x4E000` (seg `0x0000`)
- **End**: `0x4EBDD` (seg `0x0BDD`)
- **Description**: Initializes all global state, loads sprites per difficulty, sets difficulty parameters, calls assignment generation, starts main loop.

### 2. Generate Base Topping Assignments
- **File offset**: `0x515E1` (seg `0x35E1`)
- **End**: `0x51676` (seg `0x3676`)

**Pseudocode:**
```
function generate_base_assignments():
    memset(base_assign, 0, 16)   // clear 8 words
    all_failed = 1
    skip_slot = -1
    if difficulty == 1:
        skip_slot = 4   // slot 4 is excluded from random assignment on diff 1

    remaining = assign_count   // 2, 3, 3, or 4

    while remaining > 0:
        for si = 0 to num_toppings - 1:
            val = rand(1000)
            if val < assign_threshold AND base_assign[si] == 0 AND si != skip_slot:
                base_assign[si]++
                remaining--
                all_failed = 0

    if all_failed:
        slot = rand(3)     // returns 0-3
        // add ax,ax doubles for word indexing: byte offset 0,2,4,6 = slot index 0,1,2,3
        base_assign[slot]++
```

The fallback picks one of slots 0-3 (the `add ax,ax` is for word-array indexing, converting slot index to byte offset).

### 3. Distribute Toppings to Trolls
- **File offset**: `0x50664` (seg `0x2664`)
- **End**: `0x50A1B` (seg `0x2A1B`)

**Pseudocode:**
```
function distribute_toppings_to_trolls():
    memset(arno_wants, 0, 16)
    memset(willa_wants, 0, 16)
    memset(shyler_wants, 0, 16)

    generate_base_assignments()

    di = 0          // count for arno
    bp_fc = 0       // count for willa
    bp_fa = 0       // count for shyler

    switch (difficulty):

    case 0:  // 1 troll - all toppings go to Arno
        for si = 0 to num_toppings - 1:
            arno_wants[si] = base_assign[si]

    case 1:  // 2 trolls - random split between Arno and Willa
        for si = 0 to num_toppings - 1:
            if base_assign[si] != 0:
                r = rand(1)     // 0 or 1
                if r == 0:
                    arno_wants[si] = 1
                    di++
                else:
                    willa_wants[si] = 1
                    bp_fc++

        // Rebalance: ensure both trolls have at least 1 topping
        if di == 0 AND bp_fc == 0:
            slot = pick_random_assigned_slot()
            // 50/50 chance to assign to either troll
            ...
        // (more rebalancing logic if one troll got zero)

    case 2:  // 2 trolls - random 3-way split (troll 2 only used at diff 3)
    case 3:  // 3 trolls - random 3-way split
        for si = 0 to num_toppings - 1:
            if base_assign[si] != 0:
                r = rand(2)     // 0, 1, or 2
                if r == 0:
                    arno_wants[si] = 1; di++
                elif r == 1:
                    willa_wants[si] = 1; bp_fc++
                else:
                    shyler_wants[si] = 1; bp_fa++

        // Extensive rebalancing follows to ensure each active troll
        // gets at least 1 topping and no troll has 0
        // Uses rand(num_toppings-1) to pick random zoombinis to reassign
```

**Difficulty jump table** at file offset `0x50A1C` (seg `0x2A1C`):
- Diff 0: seg `0x26CF` -> copy base to arno
- Diff 1: seg `0x26ED` -> rand(1) split
- Diff 2: seg `0x2769` -> rand(2) split
- Diff 3: seg `0x2769` -> same as diff 2

### 4. Topping Toggle Handler
- **File offset**: `0x4FE03` (seg `0x1E03`)
- **End**: `0x50276` (seg `0x2276`)

Called when the player clicks a topping button. The click parameter (arg) is:
- arg=3: Offer pizza to troll (resets pizza display, calls `build_pizza_display()`)
- arg=4 through arg=11: Toggle topping 0 through topping 7

**Pseudocode for toggling (e.g., arg=4, topping 0):**
```
function handle_topping_click(arg):
    if pizza_animation_active: return

    if arg == 3:    // Offer pizza
        reset_pizza()
        build_pizza_display()
        return

    topping_idx = arg - 4   // 0 through 7

    // Toggle the state
    topping_state[topping_idx] ^= 1
    meal_state[topping_idx] = topping_state[topping_idx]

    // Update sprite based on current state and difficulty
    sprite = topping_sprites[topping_idx]
    show_sprite(sprite, resource_for_difficulty_and_state)
```

**Jump table** at file offset `0x5027F` (seg `0x227F`):

| Case | Arg | Topping | State Var | Meal Var |
|------|-----|---------|-----------|----------|
| 0 | 3 | Offer pizza | - | - |
| 1 | 4 | Topping 0 | `0x9706` | `0x977E` |
| 2 | 5 | Topping 1 | `0x9704` | `0x9780` |
| 3 | 6 | Topping 2 | `0x9702` | `0x9782` |
| 4 | 7 | Topping 3 | `0x9700` | `0x9784` |
| 5 | 8 | Topping 4 | `0x96FE` | `0x9786` |
| 6 | 9 | Topping 5 | `0x9708` | `0x9788` |
| 7 | 10 | Topping 6 | `0x970A` | `0x978A` |
| 8 | 11 | Topping 7 | `0x970C` | `0x978C` |

Toppings 5 and 6 are gated by `difficulty != 0` (not available at easiest level).
Topping 7 is gated by `difficulty == 3` (only available at hardest level).

### 5. Evaluate Pizza Offering
- **File offset**: `0x50A98` (seg `0x2A98`)
- **End**: `0x50C03` (seg `0x2C03`)

**Pseudocode:**
```
function evaluate_pizza(troll_id) -> int:
    cx = 0    // wrong_count: toppings on pizza that DON'T belong to this troll
    di = 0    // right_count: toppings on pizza that DO belong to this troll
    si = 0    // total_wanted: total toppings this troll wants
    flag = 0  // [bp-4], always 0

    // Select which troll's preference array to use
    switch (troll_id):
        case 0: troll_prefs = arno_wants    // [0x973A]
        case 1: troll_prefs = willa_wants   // [0x974A]
        case 2: troll_prefs = shyler_wants  // [0x975A]

    // Count total desired toppings for this troll
    for dx = 0 to num_toppings - 1:
        if troll_prefs[dx] != 0:
            si++     // this troll wants this topping

    // Compare offered pizza against troll's preferences
    for dx = 0 to num_toppings - 1:
        if offered[dx] != 0:              // this topping is on the pizza
            if troll_prefs[dx] != 0:
                di++                      // correct - troll wants this
            else:
                cx++                      // wrong - troll doesn't want this

    // Determine result
    if flag != 0:
        return 3        // error/special state (never happens normally)

    if cx == 1:
        return 0        // exactly one wrong topping

    if cx > 1:
        return 4        // multiple wrong toppings

    // cx == 0: no wrong toppings on pizza
    // Fall through to final comparison

    if di == si:
        return 2        // PERFECT: all wanted toppings present, no wrong ones
    else:
        return 1        // partial: some right but not complete
```

**Return value meanings:**
| Value | Meaning | Game Response |
|-------|---------|---------------|
| 0 | Exactly one wrong topping | Troll accepts with mild reaction, increments accept counter |
| 1 | Some correct but incomplete, no wrong | Troll rejects, shows wrong-guess animation |
| 2 | Perfect match: all correct, no wrong | Troll is satisfied, advances state |
| 3 | Special/error state | (Appears unused in normal flow) |
| 4 | Multiple wrong toppings | Troll rejects, shows angry animation |

### 6. Pizza Evaluation Handler (Main Dispatcher)
- **File offset**: `0x50C06` (seg `0x2C06`)
- **End**: `0x51293` (seg `0x3293`)

Takes `troll_id` parameter. For each troll, checks satisfaction state, calls `evaluate_pizza()`, and dispatches to result-specific animation/state handlers.

**Jump tables for result dispatch:**

Troll 0 (Arno) at `CS:0x3346` (file `0x51346`):
| Result | Seg Offset | File Offset | Action |
|--------|------------|-------------|--------|
| 0 | `0x2C62` | `0x50C62` | Accept: set state=2, increment counter |
| 1 | `0x2CAE` | `0x50CAE` | Reject: show wrong animation, increment reject counter |
| 2 | `0x2D28` | `0x50D28` | Perfect match animation (not "done" yet, see below) |
| 3 | `0x2DF5` | `0x50DF5` | Error state handler |
| 4 | `0x2D8F` | `0x50D8F` | Multiple wrong animation |

Troll 1 (Willa) at `CS:0x333C` (file `0x5133C`):
| Result | Seg Offset | File Offset |
|--------|------------|-------------|
| 0 | `0x2E72` | `0x50E72` |
| 1 | `0x2EC3` | `0x50EC3` |
| 2 | `0x2F3D` | `0x50F3D` |
| 3 | `0x3018` | `0x51018` |
| 4 | `0x2FAB` | `0x50FAB` |

Troll 2 (Shyler) at `CS:0x3332` (file `0x51332`):
| Result | Seg Offset | File Offset |
|--------|------------|-------------|
| 0 | `0x3093` | `0x51093` |
| 1 | `0x30E4` | `0x510E4` |
| 2 | `0x315E` | `0x5115E` |
| 3 | `0x3239` | `0x51239` |
| 4 | `0x31CC` | `0x511CC` |

### 7. Encode Pizza State (History Recording)
- **File offset**: `0x51AA1` (seg `0x3AA1`)
- **End**: `0x51B32` (seg `0x3B32`)

**Pseudocode:**
```
function encode_pizza_state():
    pizza_history_index++   // [0x97BC]

    mask = 0
    if offered[0] != 0: mask |= 0x01  // [0x978E]
    if offered[1] != 0: mask |= 0x02  // [0x9790]
    if offered[2] != 0: mask |= 0x04  // [0x9792]
    if offered[3] != 0: mask |= 0x08  // [0x9794]
    if offered[4] != 0: mask |= 0x10  // [0x9796]
    if offered[5] != 0: mask |= 0x20  // [0x9798]
    if offered[6] != 0: mask |= 0x40  // [0x979A]
    if offered[7] != 0: mask |= 0x80  // [0x979C]

    pizza_history[pizza_history_index] = mask   // [0x97A0 + index]
```

### 8. Check Pizza Duplicate
- **File offset**: `0x51B33` (seg `0x3B33`)
- **End**: `0x51BB9` (seg `0x3BB9`)

**Pseudocode:**
```
function check_pizza_duplicate() -> bool:
    if pizza_history_index < 0: return false

    // Build bitmask of current pizza
    current_mask = 0
    if offered[0] != 0: current_mask |= 0x01
    if offered[1] != 0: current_mask |= 0x02
    // ... (same pattern for all 8 bits)
    if offered[7] != 0: current_mask |= 0x80

    // Compare against all previous pizzas
    for i = 0 to pizza_history_index:
        if pizza_history[i] == current_mask:
            return true   // duplicate found

    return false
```

### 9. Build Pizza Display
- **File offset**: `0x535D4` (seg `0x35D4`)
- **End**: `0x53951` (seg `0x3951`)

Clears the meal state arrays, resets all topping toggle states to 0, and creates the visual pizza representation based on difficulty level. Each difficulty has different sprite resources for the toppings.

### 10. Pizza Piece Rendering (per troll)
Three near-identical functions render the troll's "ideal pizza" display, removing pieces the troll does NOT want:

- **Arno renderer**: `0x51BBA` (seg `0x3BBA`) - jump table at `CS:0x3D3C`
- **Willa renderer**: `0x51D85` (seg `0x3D85`) - jump table at `CS:0x3F1C`
- **Shyler renderer**: `0x51FB0` (seg `0x3FB0`) - jump table at `CS:0x40F4` (approx)

Each renderer iterates through sprite command entries. For each sprite command ID (starting from `0x9C`), it maps to a topping slot. Groups of 4 consecutive IDs map to the same topping (4 visual variants). If the troll's preference for that topping is 0, the sprite is removed from the display.

Rendering ID-to-topping mapping (for Arno):
| Sprite IDs | Topping Preference Checked |
|------------|---------------------------|
| `0x9C`-`0x9F` (cases 0-3) | `arno_wants[4]` = `[0x9742]` |
| `0xA0`-`0xA3` (cases 4-7) | `arno_wants[3]` = `[0x9740]` |
| `0xA4`-`0xA7` (cases 8-11) | `arno_wants[2]` = `[0x973E]` |
| `0xA8`-`0xAB` (cases 12-15) | `arno_wants[1]` = `[0x973C]` |
| `0xAC`-`0xAF` (cases 16-19) | `arno_wants[0]` = `[0x973A]` |
| `0xB0`-`0xB3` (cases 20-23) | `arno_wants[5]` = `[0x9744]`, gated by diff!=0 |
| `0xB4`-`0xB7` (cases 24-27) | `arno_wants[6]` = `[0x9746]`, gated by diff!=0 |
| `0xB8`-`0xBB` (cases 28-31) | `arno_wants[7]` = `[0x9748]`, gated by diff!=0 |

**Note**: The sprite ID to preference index mapping is NOT sequential. IDs `0x9C`-`0x9F` check slot 4, `0xA0`-`0xA3` check slot 3, etc. The visual ordering of toppings on the pizza does not match the array index ordering.

### 11. Swap and Animate Function
- **File offset**: `0x5395A` (seg `0x395A`)
- **End**: `0x53D03` (seg `0x3D03`)

Called when the game needs to show toppings being placed/swapped on trolls' pizzas. Takes 4 parameters representing zoombini/topping indices and creates the animation sequence. Records the state in `offered[]` and calls `encode_pizza_state()`.

### 12. Troll Expression/Reaction Function
- **File offset**: `0x53353` (seg `0x3353`)
- **End**: `0x53456` (seg `0x3456`)

Controls troll facial expressions. Takes a mood parameter (0-4):
- 0: Neutral/happy
- 1: Sad/disappointed
- 2: Angry
- 3: Very angry
- 4: (same as 3?)

### 13. Meal/Pizza Sprite Creation
- **File offset**: `0x4FC53` (seg `0x1C53`)
- **End**: `0x4FCDA` (seg `0x1CDA`)

Creates or reuses the pizza sprite. Uses difficulty to select the correct resource ID (`0x1B59` + difficulty).

---

## Debug Output

The debug output code is at file offset `0x513C0` (seg `0x33C0`).

It uses `sprintf`-style formatting to create debug strings:

```
Arno     [0x973A] [0x973C] [0x973E] [0x9740] [0x9742] [0x9744] [0x9746] [0x9748]
Willa    [0x974A] [0x974C] [0x974E] [0x9750] [0x9752] [0x9754] [0x9756] [0x9758]
Shyler   [0x975A] [0x975C] [0x975E] [0x9760] [0x9762] [0x9764] [0x9766] [0x9768]
Meal     [0x977E] [0x9780] [0x9782] [0x9784] [0x9786] [0x9788] [0x978A] [0x978C]
```

The 8 values per troll are the `wants[]` arrays (1 = wants this topping, 0 = doesn't).
The Meal values are the current pizza state (1 = topping present, 0 = absent).

Format string file offsets:
- "Arno": DS:`0x19C8` (file `0xD83C8`)
- "Willa": DS:`0x19E8` (file `0xD83E8`)
- "Shyler": DS:`0x1A08` (file `0xD8408`)
- "Meal": DS:`0x1A28` (file `0xD8428`)

---

## Complete Algorithm in Plain Language

### Initialization Phase

1. **Clear all state**: All global variables, arrays, and counters are zeroed.

2. **Load difficulty**: The game reads the difficulty setting (0-3) and configures:
   - Number of toppings available (5 to 8)
   - Number of trolls (1 to 3: Arno, Willa, Shyler)
   - Random assignment parameters

3. **Load sprites**: Based on difficulty, different sets of topping sprites are loaded. Each difficulty level has a unique visual set (different pizza types).

4. **Generate random preferences**:

   a. **Base assignment** (`generate_base_assignments`): Randomly selects which topping slots will be "assigned" (wanted). Iterates through all topping slots; for each, rolls `rand(1000)` and if below threshold, marks that slot. Repeats until the target number of assignments is reached.

   b. **Distribute to trolls** (`distribute_toppings_to_trolls`):
      - **Difficulty 0** (1 troll): Copies base assignments directly to Arno.
      - **Difficulty 1** (2 trolls): For each assigned topping, `rand(1)` decides if it goes to Arno (0) or Willa (1).
      - **Difficulty 2-3** (2-3 trolls): For each assigned topping, `rand(2)` decides: 0=Arno, 1=Willa, 2=Shyler.

   c. **Rebalancing**: After random distribution, the code ensures no active troll has zero toppings. If a troll got nothing, a topping is stolen from the troll with the most and reassigned. This involves multiple `rand(num_toppings-1)` calls to find random members of each troll's group.

   d. **Difficulty 3 extra rebalancing**: Additional logic determines which troll group is largest and redistributes to balance the groups, involving 4 calls to `find_random_member_of_troll_group()`.

### Gameplay Phase

5. **Player interaction**: The player sees a pizza tray and clickable topping buttons. Each click toggles a topping on/off:
   - The state variable (`0x96FE`-`0x970C`) is XORed with 1
   - The meal display array (`0x977E`-`0x978C`) is updated
   - The visual sprite is updated to show the current state

6. **Offer pizza** (click arg=3): Resets the pizza display and constructs the visual representation based on the current difficulty.

### Evaluation Phase

7. **Pizza evaluation** (triggered by offering to a troll):
   - Counts how many toppings on the offered pizza match the troll's preferences (`di` = correct matches)
   - Counts how many toppings on the offered pizza are NOT wanted by the troll (`cx` = wrong)
   - Counts total toppings the troll wants (`si`)

   Results:
   - **Perfect** (return 2): `cx == 0` AND `di == si` -- all wanted toppings present, no unwanted ones
   - **Partial** (return 1): `cx == 0` but `di < si` -- no wrong toppings but missing some
   - **One wrong** (return 0): `cx == 1` -- exactly one wrong topping
   - **Multiple wrong** (return 4): `cx > 1` -- more than one wrong topping

8. **State progression**: When a troll gets a perfect pizza (result=2), its state advances:
   - State 1 -> 2 (accepts offering, shows happy animation)
   - When all trolls reach state 3, the bridge opens and zoombinis pass through

9. **History tracking**: Each offered pizza is recorded as a bitmask in the pizza history array. The game can check for duplicate offerings.

---

## Rand() Call Summary

Total: 43 `rand()` calls in segment 37.

### Cluster 1: Single call at seg `0x0ABC` (file `0x4EABC`)
- `rand(100)` (push `0x64`) - used to determine troll expression/mood after an interaction
  - result > 75 (`0x4B`): mood 3 (very angry/upset)
  - result > 50 (`0x32`): mood 2 (angry)
  - result > 25 (`0x19`): mood 1 (disappointed)
  - result <= 25: mood 0 (neutral)

### Cluster 2: 14 calls at seg `0x26FF`-`0x28E3` (file `0x506FF`-`0x508E3`)
These are in the `distribute_toppings_to_trolls` function:
- `rand(1)` at `0x506FE`: Split between 2 trolls (diff 1)
- `rand(num_toppings-1)` at `0x5073C`: Pick random slot for rebalancing (diff 1 fallback)
- `rand(1000)` at `0x50746`: Coin flip for rebalancing
- `rand(2)` at `0x5077A`: Split between 3 trolls (diff 2/3)
- Multiple `rand(num_toppings-1)` calls for various rebalancing scenarios

### Cluster 3: 28 calls at seg `0x2A48`-`0x2BDC` (file `0x50A48`-`0x50BDC`)
These are in the `find_random_member_of_troll_group` function and the difficulty-3 rebalancing:
- `rand(num_toppings-1)` calls: Pick random topping from a specific troll's group
- Used for the difficulty 3 four-way rebalancing algorithm

---

## Topping Identity Mapping

**OPEN QUESTION**: The exact visual identity of each topping slot is determined by the MHK resource files. Without extracting the sprite resources, the mapping from slot index to actual pizza topping (e.g., pepperoni, mushroom, etc.) cannot be determined from the code alone. The resource IDs are:
- Difficulty 0: `0x1B5D` through `0x1B65` (5 toppings)
- Difficulty 1: `0x1B67` through `0x1B71` (7 toppings)
- Difficulty 2: `0x1B73` through `0x1B7F` (7 toppings)
- Difficulty 3: `0x1B81` through `0x1B8F` (8 toppings)

Each topping has TWO resource IDs (on/off states), selected by adding the state value (0 or 1) to the base resource ID.

---

## Open Questions

1. **Pizza-to-Zoombini Mapping**: The code internally refers to topping slots using the same indexing as "zoombini" slots. It is unclear whether each topping actually maps to a Zoombini attribute (hair, eyes, nose, feet, color) or whether they are purely abstract pizza toppings. The MHK resource extraction would clarify this.

2. **`offered[]` Array Population**: The `offered[]` array at `[0x978E]` is written by the swap/animate function (`0x5395A`), not directly by the toggle handler. The exact mechanism by which the player's current pizza selection gets copied to `offered[]` before evaluation needs further tracing. It appears the eval handler function at `0x50C06` uses `offered[]` which is populated during the animation sequence.

3. **The `[bp-4]` Flag in Evaluation**: The eval function initializes `[bp-4] = 0` and checks it, but it never appears to be set to a non-zero value in the normal flow. This may be a vestigial check or could be set by code not in this segment.

4. **Topping 7 on Difficulty 1**: On difficulty 1, `skip_slot = 4` in the base assignment generator prevents slot 4 from being assigned. However, all 7 slots (0-6) are otherwise available. The reason for excluding specifically slot 4 is unclear.

5. **Sprite ID to Slot Mapping Reversal**: In the pizza piece renderer, sprite IDs `0x9C`-`0x9F` map to slot 4, while IDs `0xAC`-`0xAF` map to slot 0. This reversed ordering suggests the visual layout of toppings on the pizza is inverted relative to the internal array order.

6. **Rand() Seed**: The MSVC LCG (`seed = seed * 214013 + 2531011`) is used throughout. The initial seed source is not in this segment - it is likely set by the main game loop or system timer before calling into the puzzle code.

7. **Result 0 vs Result 2**: When `evaluate_pizza` returns 0 (exactly one wrong topping), the game sets the troll's state to "accepting" (state 2) and increments a counter. This is surprising - having a wrong topping would normally be a failure. This may indicate that the game uses a progressive feeding mechanic where each offering contributes one topping to the troll's pizza, and the eval is checking the INCREMENTAL addition rather than the whole pizza.

---

## Function Offset Summary Table

| Function | Seg Offset | File Offset | Size (approx) |
|----------|------------|-------------|---------------|
| Main init | `0x0000` | `0x4E000` | ~2800 bytes |
| Pizza sprite creation | `0x1C53` | `0x4FC53` | 136 bytes |
| Pizza piece check (Arno meal) | `0x1CDB` | `0x4FCDB` | ~300 bytes |
| Topping toggle handler | `0x1E03` | `0x4FE03` | ~1136 bytes |
| Distribute toppings to trolls | `0x2664` | `0x50664` | ~950 bytes |
| Difficulty jump table (data) | `0x2A1C` | `0x50A1C` | 8 bytes |
| Find random troll member | `0x2A24` | `0x50A24` | 116 bytes |
| Evaluate pizza | `0x2A98` | `0x50A98` | ~870 bytes |
| Eval handler dispatcher | `0x2C06` | `0x50C06` | ~1680 bytes |
| Debug output | `0x33C0` | `0x513C0` | ~448 bytes |
| Troll expression control | `0x3353` | `0x53353` | ~260 bytes |
| Build pizza display | `0x35D4` | `0x535D4` | ~888 bytes |
| Generate base assignments | `0x35E1` | `0x515E1` | 150 bytes |
| Event handler (troll click) | `0x3677` | `0x51677` | ~512 bytes |
| Swap and animate | `0x395A` | `0x5395A` | ~936 bytes |
| Pizza piece renderer (Arno) | `0x3BBA` | `0x51BBA` | ~384 bytes |
| Pizza piece renderer (Willa) | `0x3D85` | `0x51D85` | ~384 bytes |
| Pizza piece renderer (Shyler) | `0x3FB0` | `0x51FB0` | ~384 bytes |
| Encode pizza state | `0x3AA1` | `0x51AA1` | 145 bytes |
| Check pizza duplicate | `0x3B33` | `0x51B33` | 135 bytes |


---

# v2 (PE32) — Pizza Pass-Code-Karte

> Vollständige Tabelle: `V2_VARIABLE_MAP.md` § 9.

## Code-Lokalisation

| | v1 | v2 |
|---|----|----|
| Code | Seg 37 | `.text` 0x00431590..0x004373D0 |
| MHK-Loader | — | `0x00431590` (3904 B, 1 Caller) |
| Wrapper | — | `0x0042FCB0` |
| Cheat-Print | — | `0x00435020..0x00435340` (1280 B, mit `Arno/Willa/Shyler %d`-Markern) |

## Verifizierte Mappings (Iteration 2 — Cheat-Print disassembliert)

| v1 DS | v2 VA | Bedeutung | Konfidenz |
|-------|-------|-----------|-----------|
| `0x96D2` ⚠️ | **`0x0049BC36`** | DIFFICULTY (0..3) | **Verified** (Dispatcher-Source) |
| `0x973A..` ⚠️ | **`0x0049BA34..0x0049BA42`** (8 Worte) | **arno_wants[8]** | **Verified** — 8× sequentielle `movsx [VA]` vor `push "Arno %d×8"` |
| `0x974A..` ⚠️ | **`0x0049BB44..0x0049BB52`** | **willa_wants[8]** | **Verified** |
| `0x975A..` ⚠️ | **`0x0049BC38..0x0049BC46`** | **shyler_wants[8]** | **Verified** |
| — (NEU) | **`0x0049BC00..0x0049BC0E`** | **meal[8]** finale Pizza | **Verified** — push "Meal %d×8" |
| — | `0x0049BBA4` | ~~Arno aktiv-Flag~~ — **WIDERLEGT** | siehe unten |
| — | **`0x0049BB30`** | Willa aktiv-Flag | **Verified** (Live-Dumps) |
| — | `0x0049BB6A` | ~~Shyler aktiv-Flag~~ — **WIDERLEGT** | siehe unten |

### Aktiv-Flag-Korrektur (Live-Dumps 2026-04-28)

Vier Dumps (memdump-155353/-155418/-155435/-155450, je ein Schwierigkeitsgrad)
zeigten:

- `0x0049BBA4` ist in **allen** vier Dumps `= 1` — auch bei Diff 0 wo nur Arno
  aktiv ist UND bei keinem aktiven Troll-Wechsel. Vermutlich generisches
  Pizza-Puzzle-Aktiv-Flag, kein Arno-spezifisches Flag.
- `0x0049BB6A` ist in **allen** vier Dumps `= 0`, auch bei Diff 3 wo Shyler
  eindeutig aktiv ist (zwei wants gesetzt). Adresse stimmt nicht mit Shyler-
  Aktivität überein.
- `0x0049BB30` korreliert sauber: bei Diff 0 fehlend (= 0), bei Diff ≥ 1 = 1.
  Echtes Willa-Flag.

**Ableitung im Code (`PizzaState`)**: Statt sich auf die fragwürdigen
Flag-Adressen zu verlassen, leiten wir „Troll aktiv" aus
`wants[].Any(w != 0)` ab. Das ist trivial und verifiziert (jeder aktive Troll
hat per Definition mindestens einen Wunsch nach der Generation/Verteilung
in `distribute_toppings_to_trolls`).

### `num_trolls`-Tabelle vs. Live-Beobachtung (Diff 2)

Die obenstehende Difficulty-Tabelle behauptet `num_trolls=2` für Diff 2,
gleichzeitig aber `shyler_active=1`. Live-Dump 155435 (Diff 2) zeigt: Shyler
hat tatsächlich Wünsche (`shyler_wants[5]=1`), Arno und Willa auch. Heißt
entweder die `num_trolls`-Spalte ist falsch, oder „aktiv" und „sichtbar im
Bridge-Setup" sind getrennte Konzepte. Für den Helper irrelevant — wir lesen
einfach was im Speicher steht.

### Difficulty-Encoding: 0-basiert im Speicher, 1-basiert in der UI

Verifiziert durch 4 Mismatch-Dumps am 2026-04-28 (memdump-161958 / -162034 /
-162107 / -162133), je einer pro UI-Schwierigkeitsstufe:

| In-game UI | Memory `0x0049BC36` | Aktive Trolle |
|---|---|---|
| Schwierigkeit 1 | 0 | Arno |
| Schwierigkeit 2 | 1 | Arno + Willa |
| Schwierigkeit 3 | 2 | Arno + Willa + Shyler |
| Schwierigkeit 4 | 3 | Arno + Willa + Shyler |

`PizzaState.Read` addiert `+1`, damit die Helper-Anzeige zur UI passt
(analog zum `+1` in `CliffState`).

### Sichtbare Toppings vs. Memory-Slots (Schwierigkeit 2)

Die Doku-Tabelle oben behauptet `num_toppings=7` bei Diff 1 (= UI-Schwierigkeit
2). Live-Beobachtung 2026-04-28: in der Auswahlleiste sind nur **6 Buttons**
sichtbar. Die Slot-zu-Position-Mapping ist:

| Visuelle Position | Memory-Slot | Topping (live identifiziert) |
|---|---|---|
| 1 | 0 | Black dots (Olives?) |
| 2 | 1 | Green stuff (Paprika?) |
| 3 | 2 | Longish black stuff (Tuna?) |
| 4 | 3 | Mushrooms |
| **(übersprungen)** | **4** | **kein Button** — passt zu `skip_slot=4` aus dem Code |
| 5 | 5 | Cherry (ice cream) |
| 6 | 6 | Cream (ice cream) |

Konsequenz für den Helper: bei Schwierigkeit 2 ist „Memory-Slot 5" visuell der
**5. Button** (nicht der 6.). `PizzaToppings.Visible[2]` enthält die sichtbaren
Toppings in genau dieser Reihenfolge — der Renderer zeigt „Position N" basierend
auf dieser Liste, nicht auf dem Memory-Index.

Bei Schwierigkeiten 1, 3, 4 gibt es keinen Skip — Position N = Memory-Slot N-1.
| `0x97D2` 📊 | `0x0049BA14` (12 acc) | active topping bitmask | Probable |
| `0x979E` 📊 | `0x0049BC56` (20 acc) | active count | Probable |

## v2-Erweiterung
Pizza hat in v2 56 zusätzliche SCRBs (siehe `V1_VS_V2_COMPARISON.md`). Im Code-
Layout aber **keine offensichtliche Logik-Erweiterung** — Difficulty-Dispatcher
ist weiterhin 4-case. Die zusätzlichen Hotspots sind reine MHK-Daten ohne neue
Code-Pfade.
