# Mudball Wall (NET.MHK, Segment 35) — Deep Reverse Engineering

## Overview

Mudball Wall is a logic deduction puzzle. The player designs mudballs by selecting visual
properties on 2 or 3 axes (5 options each), then throws them at a wall of targets. Each
wall target has a secret correct combination of properties. Targets showing 1-3 dots
indicate how many Zoombinis are rescued on a hit. The player must deduce the rules mapping
property combinations to wall positions through trial and error.

**Internal name**: "Net" (MHK file: `Net.MHK`, string at ds:0x1864)

---

## Segment Map

| Segment | Role |
|---------|------|
| 35 (code) | Puzzle logic, file offset 0x47C00, size 0x3065 bytes |
| 137 (data) | Shared puzzle state: solution grids, slot counts, position assignments |
| 138 (data) | Wall target screen coordinates: (x,y) DWORD per position |
| 139 (data) | Engine control: accepted counter, display state |
| 152 (data) | Zoombini queue: display/animation handles |
| 189 (data) | Engine animation system: completion status checks |
| 191 (DGROUP) | Automatic data segment: globals, string literals, config tables |

---

## Function Map

| Seg Offset | File Offset | Name | Description |
|------------|-------------|------|-------------|
| 0x0000 | 0x47C00 | `Init` | Main initialization, variable setup, MHK loading |
| 0x03BB | 0x47FBB | `LoadAnimation` | Loads animation from MHK resource table |
| 0x0476 | 0x48076 | `SimpleHelper` | Small helper (no BP frame) |
| 0x0494 | 0x48094 | `ToggleUIState` | Toggles visibility of UI elements |
| 0x04FD | 0x480FD | `ResetPuzzleState` | Cleans up and releases puzzle resources |
| 0x0554 | 0x48154 | `MainGameLoop` | Main frame handler: animation sequencing, throw processing |
| 0x0F25 | 0x48B25 | `EventHandler` | Processes mouse clicks on axes and Go button |
| 0x10CD | 0x48CCD | `TimerHandler` | Handles timer events (reset, animation tick) |
| 0x1161 | 0x48D61 | `SetupResources` | Loads sprites, backgrounds, axis selector animations |
| 0x134E | 0x48F4E | `FillGridRow2D` | Fills row/column in 5x5 grid (difficulty 0/1) |
| 0x13C9 | 0x48FC9 | `FillGridRow3D` | Fills row/column in 5x5x5 grid (difficulty 2/3) |
| 0x1461 | 0x49061 | `GenerateGrids` | Generates random solution grids + rule configuration |
| 0x1ABD | 0x496BD | `SpawnZoombini` | Spawns next Zoombini from queue to wall slot |
| 0x1B9B | 0x4979B | `HandleAxisSelection` | Processes axis button click or Go button press |
| 0x1F2D | 0x49B2D | `AnimationUpdate` | Updates animation state for current axis display |
| 0x20E7 | 0x49CE7 | `SetAnimProperties` | Sets animation object frame/display properties |
| 0x213E | 0x49D3E | `UpdateSpritePositions` | Positions sprites based on current selections |
| 0x248B | 0x4A08B | `ProcessThrowResult` | Determines hit/miss, updates score, marks position |
| 0x2657 | 0x4A257 | `EvaluateMatch` | Core: finds which grid position matches current selections |
| 0x2900 | 0x4A500 | `DispatchEvent` | Main event dispatch for animation callbacks |
| 0x2D8D | 0x4A98D | `DistributeSlots` | Distributes Zoombinis across wall target positions |
| 0x2E87 | 0x4AA87 | `AcceptZoombini` | Creates mudball flight animation toward wall target |
| 0x2FF2 | 0x4ABF2 | `HandleTimeout` | Mudball flight step + impact detection |

---

## Data Segment Layout

### DGROUP (Segment 191) — Global Variables

#### Difficulty & Configuration
| DS Offset | Type | Name | Description |
|-----------|------|------|-------------|
| 0x9368 | word | `difficulty` | 0=Not So Easy, 1=Oh So Hard, 2=Very Hard, 3=Very Very Hard |
| 0x94E8 | word | `numPositions` | Grid size: 25 (diff<=1) or 125 (diff>1) |
| 0x94C4 | word | `numZoombinis` | Throws remaining (decremented per throw) |
| 0x94C6 | word | `remainingSlots` | 16 - numZoombinis |
| 0x94C8 | word | `savedNumZoombinis` | Backup of initial numZoombinis |
| 0x94C0 | word | `gameActive` | 1 = game is active |
| 0x94D2 | word | `firstThrow` | 1 = first throw (no previous animation) |

#### Player Selections (Mudball Properties)
| DS Offset | Type | Name | Description |
|-----------|------|------|-------------|
| 0x94F8 | word | `selectedAxis1` | Axis 1 button index (0-4), -1 if unset. Only used at diff >= 2 |
| 0x94FC | word | `selectedAxis2` | Axis 2 button index (0-4), -1 if unset |
| 0x9500 | word | `selectedAxis3` | Axis 3 button index (0-4), -1 if unset |
| 0x94FA | word | `prevAxis1` | Previous axis 1 selection (for animation) |
| 0x94FE | word | `prevAxis2` | Previous axis 2 selection |
| 0x9502 | word | `prevAxis3` | Previous axis 3 selection |

#### Rule Configuration
| DS Offset | Type | Name | Description |
|-----------|------|------|-------------|
| 0x9346 | word | `attrMap[0]` | Which property category (0-2) maps to the display for axis 2 |
| 0x9348 | word | `attrMap[1]` | Which property category maps for axis 1 |
| 0x934A | word | `attrMap[2]` | Which property category maps for axis 3 (diff < 2 only) |
| 0x9522 | word | `permutation` | Evaluation permutation 0-5 (diff 3 only) |

#### Game State
| DS Offset | Type | Name | Description |
|-----------|------|------|-------------|
| 0x932C | word | `totalTargets` | Total number of wall targets with Zoombinis |
| 0x932E | word | `nextZoombiniIdx` | Index of next Zoombini to spawn from queue |
| 0x933C | word | `numColumns` | Number of slot groups in distribution |
| 0x9354 | word | `matchedPosition` | Grid position from last EvaluateMatch() call |
| 0x9332 | word | `scoreAccum` | Total Zoombinis rescued so far |
| 0x9530 | word | `targetScore` | Target score to reach (initially 3, or 2, or totalTargets) |
| 0x9532 | word | `scatterCount` | Counter for "scatter" celebration animation |
| 0x9534 | word | `correctCount` | Count of successful throws |
| 0x950A | word | `acceptCount` | Display counter for accept animations |
| 0x9510 | word | `attemptCount` | Flight step counter (0-5) |
| 0x9514 | word | `hitDots` | Dots at hit position (1-3), or 0 for miss |
| 0x9512 | word | `hitPending` | Flag: pending hit animation to process |
| 0x9516 | word | `spawnPending` | Count of pending spawns |
| 0x9520 | word | `goButtonActive` | Go button press active flag |
| 0x9506 | word | `axisConfirmed` | All axes selected and confirmed |
| 0x94E6 | word | `animBusy` | Animation busy flag |
| 0x94D0 | word | `guessWrong` | Wrong guess flag (set on miss) |
| 0x94CA | word | `wrongCount` | Count of wrong throws |
| 0x9356 | word | `overflowCount` | Count of overflow events (queue full) |
| 0x9504 | word | `spawnCountdown` | Countdown for delayed spawn |
| 0x9350 | word | `activeSlot` | Currently active wall slot (0-2) |
| 0x9358 | word | `pendingEventId` | Pending animation event ID |
| 0x935C | word | `celebrationCount` | End-of-round celebration counter |
| 0x935E | word | `scatterIndex` | Index into scatter position array |
| 0x931E | word | `currentZoombIdx` | Current Zoombini index for display |
| 0x9330 | word | `activeHandle` | Currently active display handle |
| 0x9508 | word | `missFlag` | Set on miss (no dots at position) |
| 0x177E | word | `nextRoundFlag` | Flag to trigger next round music/sound |
| 0x94EA | word | `spriteCount` | Count of active sprites |
| 0x94CC | word | `lastAcceptHandle` | Handle of last accepted Zoombini sprite |
| 0x94CE | word | `collectCount` | Count of Zoombinis collected in current batch |
| 0x951C | word | `wallSlotActive` | Wall slot is actively displaying |
| 0x9538 | word | `frameActive` | Frame processing active flag |
| 0x180E | word | `busyFlag` | Main loop busy/re-entry guard |
| 0x17C0 | word | `animVariant` | Animation variant selector (0, 1, or 2) |

#### Mudball Flight
| DS Offset | Type | Name | Description |
|-----------|------|------|-------------|
| 0x950C | word | `mudballX` | Current mudball screen X position |
| 0x950E | word | `mudballY` | Current mudball screen Y position |
| 0x934C | word | `stepX` | X movement per flight step |
| 0x934E | word | `stepY` | Y movement per flight step |

#### Animation Handles
| DS Offset | Type | Name | Description |
|-----------|------|------|-------------|
| 0x9360-0x9362 | dword | `mhkFilePath` | Far pointer to MHK file path string |
| 0x9364 | word | `initialized` | Puzzle initialized flag |
| 0x9366 | word | `uiVisible` | UI elements visible flag |
| 0x9370 | word | `var_9370` | Misc state |
| 0x9372 | word | `mainAnimLayer` | Main animation layer handle |
| 0x9374 | word | `wallSlotLayer` | Wall slot animation layer |
| 0x9376 | word | `displayContext` | Display context handle |
| 0x9378-0x937A | word[2] | `layerChain` | Layer ordering chain handles |
| 0x9386 | word | `bgSpriteHandle` | Background sprite handle |
| 0x9482 | word | `muddleLayer` | Muddle/launcher layer handle |
| 0x94B8 | word | `goButtonHandle` | Go button animation handle |
| 0x94BA | word | `axis1AnimHandle` | Axis 1 selector animation handle (diff >= 2) |
| 0x94BC | word | `axis2AnimHandle` | Axis 2 selector animation handle |
| 0x94BE | word | `axis3AnimHandle` | Axis 3 selector animation handle |
| 0x94E4 | word | `activeZoombAnim` | Active Zoombini animation handle |
| 0x94D4-0x94DC | word[5] | `sequenceHandles` | Animation sequence chain handles |
| 0x951E | word | `flightAnimHandle` | Mudball flight animation handle |
| 0x9518 | word | `wallHitAnimHandle` | Wall hit animation handle |
| 0x951A | word | `collectAnimHandle` | Zoombini collect animation handle |

#### Arrays (within DGROUP, using resolved negative offsets)
| DS Offset | Type | Name | Description |
|-----------|------|------|-------------|
| 0x9376+bx | word[] | `axisDisplayHandles[5]` | Display handles for 5 axis selectors |
| 0x9388+bx | word[] | `positionSpriteHandles[N]` | Sprite handles per wall position (25/125) |
| 0x936C+bx | word[3] | `slotReturnHandles[3]` | Return animation handles for 3 wall slots |
| 0x9486+bx | word[] | `acceptedSpriteHandles[]` | Sprite handles for accepted/rescued Zoombinis |
| 0x94F2+bx | word[3] | `wallSlotHandles[3]` | Zoombini handles in wall slots (0-2) |

#### Timing
| DS Offset | Type | Name | Description |
|-----------|------|------|-------------|
| 0x9524 | dword | `scatterTimestamp` | Timestamp for scatter animation pacing |
| 0x9528 | dword | `goTimestamp` | Timestamp for Go button timeout |
| 0x952C | dword | `scatterSeed` | Random seed for scatter positions |

### Segment 137 — Shared Puzzle State

All offsets relative to segment 137 base.

| Offset | Type | Name | Description |
|--------|------|------|-------------|
| 0x00FA | word[N] | `grid1[N]` | Solution grid 1 (N=25 or 125) |
| 0x01F4 | word[N] | `grid2[N]` | Solution grid 2 |
| 0x02EE | word[N] | `grid3[N]` | Solution grid 3 (diff 2/3 only) |
| 0x03E8 | word[5] | `tempBuffer` | Temporary rotation buffer |
| 0x03F2 | word[12] | `slotCounts[12]` | Distribution: dots per slot group |
| 0x040A | word[N] | `positionAssignment[N]` | Dots (1-3) at each wall position, -1 if consumed |

### Segment 138 — Wall Target Coordinates

| Offset | Type | Name | Description |
|--------|------|------|-------------|
| pos*4 | word | `targetX[pos]` | Screen X coordinate of wall target |
| pos*4+2 | word | `targetY[pos]` | Screen Y coordinate of wall target |

For 25 positions (diff 0/1): offsets 0x0000-0x0063.
For 125 positions (diff 2/3): offsets 0x0000-0x01F3.
These coordinates are set up by the engine based on MHK resource data.

### Segment 139 — Engine Control

| Offset | Type | Name | Description |
|--------|------|------|-------------|
| 0x0234 | word | `acceptedCount` | Counter of accepted/rescued Zoombinis |

---

## DGROUP Data Tables

### Property Category Names (ds:0x1828, 9 bytes)

Three null-terminated 2-character abbreviations for mudball property categories:

| Index | Abbreviation | Likely Meaning |
|-------|-------------|----------------|
| 0 | "SC" | Stamp/Splat Color |
| 1 | "SH" | Shape |
| 2 | "MC" | Mud Color |

Used in `AnimationUpdate` to label the current axis display.
The `attrMap[]` array selects which category maps to which axis.

### Axis 2 Visual Mapping (ds:0x1832, 10 bytes)

Maps button position (0-4) to internal display value for axis 2:

```
Button 0 -> value 2
Button 1 -> value 3
Button 2 -> value 0
Button 3 -> value 1
Button 4 -> value 4
```

### Axis 3 Visual Mapping (ds:0x183C, 10 bytes)

Maps button position (0-4) to internal display value for axis 3 (also used for axis 1):

```
Button 0 -> value 4
Button 1 -> value 0
Button 2 -> value 2
Button 3 -> value 1
Button 4 -> value 3
```

**Note**: These mapping tables are used ONLY for display/animation in `UpdateSpritePositions`.
The `EvaluateMatch` function compares raw button indices (0-4) directly against grid values.
The grids are generated in "button index" space, not "visual property" space.

### Accept Screen Positions (ds:0x1846, 12 bytes)

Three (x,y) word pairs for accept animation destination positions:

```
Slot 0: (203, 42)
Slot 1: (242, 35)
Slot 2: (283, 28)
```

### Reject Screen Positions (ds:0x1852, 12 bytes)

Three (x,y) word pairs for reject animation positions:

```
Slot 0: (220, 41)
Slot 1: (259, 34)
Slot 2: (300, 27)
```

### Accept Variant Table (ds:0x185E, 6 bytes)

Maps `animVariant` (0-2) to resource offset for accept sprite:

```
animVariant 0 -> offset 1 -> resource 7021 (0x1B6D)
animVariant 1 -> offset 0 -> resource 7020 (0x1B6C)
animVariant 2 -> offset 2 -> resource 7022 (0x1B6E)
```

### Coordinate Table (ds:0x17CA, 64 bytes)

16 (x,y) word pairs defining screen positions for Zoombini queue sprites:

```
Position  0: (233, 392)    Position  8: (115, 368)
Position  1: (209, 378)    Position  9: (114, 342)
Position  2: (196, 390)    Position 10: ( 99, 375)
Position  3: (185, 365)    Position 11: ( 97, 394)
Position  4: (167, 380)    Position 12: ( 95, 346)
Position  5: (160, 408)    Position 13: ( 91, 411)
Position  6: (135, 397)    Position 14: ( 79, 355)
Position  7: (121, 407)    Position 15: ( 62, 404)
```

### Animation Region Tables

| DS Offset | Values | Description |
|-----------|--------|-------------|
| 0x1810 | (500, 1, 600, 27) | Axis 2 animation region |
| 0x1818 | (500, 1, 549, 27) | Axis 2 sub-region |
| 0x1820 | (550, 1, 600, 27) | Axis 3 animation region |

---

## MHK Resource IDs

| Resource ID | Hex | Usage |
|-------------|-----|-------|
| 7000-7004 | 0x1B58-0x1B5C | Muddle/launcher base display resources |
| 7018 | 0x1B6A | Main animation layer base |
| 7020-7022 | 0x1B6C-0x1B6E | Accept mudball sprites (variants 0-2) |
| 7023 | 0x1B6F | Reject animation (difficulty 0-1) |
| 7024 | 0x1B70 | Reject animation (difficulty 2-3) |
| 7027 | 0x1B73 | Accept impact animation |
| 7028-7030 | 0x1B74-0x1B76 | Accept wall-hit animations (by column variant) |
| 7029 | 0x1B75 | Column 0 hit variant |
| 7028 | 0x1B74 | Column 1-3 hit variant |
| 7030 | 0x1B76 | Column 4 hit variant |
| 8000-8004 | 0x1F40-0x1F44 | 5 axis selector display layers |
| 8005 | 0x1F45 | Wall slot animation layer |
| 9000+ | 0x2328+ | Wall position sprites (9000 + offset) |
| 9151 | 0x23BF | Background (difficulty 0-1) |
| 9153 | 0x23C1 | Background (difficulty 2-3) |
| 10000 | 0x2710 | Submit/"Go" button |
| 10001 | 0x2711 | Go button activation animation |
| 10002-10006 | 0x2712-0x2716 | Axis 1 selection indicators (5 buttons) |
| 10007-10011 | 0x2717-0x271B | Axis 2 selection indicators (5 buttons) |
| 10012-10016 | 0x271C-0x2720 | Axis 3 selection indicators (5 buttons) |
| 10018 | 0x2722 | Idle/feedback animation |
| 13744 | 0x36B0 | Celebration resource group |

### Dot Count Resources (per wall position)

Stored in wall position sprites at sub-resource offsets:

| Difficulty | 1 dot | 2 dots | 3 dots |
|-----------|-------|--------|--------|
| 0-1 (5x5) | 0x97 (151) | 0x98 (152) | 0x99 (153) |
| 2-3 (5x5x5) | 0x9A (154) | 0x9B (155) | 0x9C (156) |

Formula: `resourceID = 0x96 + dotCount + (3 if difficulty >= 2 else 0)`

---

## Algorithm — Complete Pseudocode

### Initialization (Init, 0x0000)

```c
void Init() {
    // Copy coordinate table to stack
    memcpy(stack, ds:0x17CA, 0x40);

    // Clear all state
    memset(&globals, 0, ...);  // ~60 global words cleared

    // Get difficulty
    difficulty = GetDifficulty();  // seg44
    numPositions = (difficulty <= 1) ? 25 : 125;

    // Random initial selections (will show as default position)
    selectedAxis1 = rand(4);  // 0-4
    selectedAxis2 = rand(4);
    selectedAxis3 = rand(4);

    // Open MHK resource file
    strcpy(mhkFilePath, "Net.MHK");
    OpenMHK(mhkFilePath);

    // Set up puzzle
    DistributeSlots();
    numZoombinis = numColumns + 7 + (difficulty == 3 ? 1 : 0);
    savedNumZoombinis = numZoombinis;
    remainingSlots = 16 - numZoombinis;

    // Setup resources and generate solution
    SetupResources();
    // ... load animations, register event handlers ...
    GenerateGrids();
}
```

### Grid Generation (GenerateGrids, 0x1461)

This is the core rule generation that determines which mudball property combination
matches each wall position.

```c
void GenerateGrids() {
    // Initialize position data in segment 137
    ClearSharedMemory(seg137, 0x40A, 0, 0xFA);  // clear positionAssignment+grids

    // Three counter arrays ensure each value 0-4 is used exactly once per axis
    int counters1[5] = {0};  // tracks which values used for grid1
    int counters2[5] = {0};  // tracks which values used for grid2
    int counters3[5] = {0};  // tracks which values used for grid3

    // Generate 5 random permutation entries
    for (int di = 0; di < 5; di++) {
        bool valid;
        int r1, r2, r3;

        do {
            r1 = rand(4);  // random 0-4
            r2 = rand(4);
            r3 = rand(4);

            if (difficulty < 2) {
                // 2D: check r1 and r2 are both unused
                valid = (counters1[r1] == 0) && (counters2[r2] == 0);
            } else {
                // 3D: check all three unused
                valid = (counters1[r1] == 0) && (counters2[r2] == 0)
                     && (counters3[r3] == 0);
            }
        } while (!valid);

        counters1[r1]++;
        counters2[r2]++;
        counters3[r3]++;

        // Fill grids based on difficulty
        switch (difficulty) {
            case 0: case 1:  // 5x5 grid
                for (int si = 0; si < 5; si++) {
                    seg137:grid1[di * 5 + si] = r1;  // fill row di with r1
                    seg137:grid2[si * 5 + di] = r2;  // fill column di with r2
                }
                break;

            case 2: case 3:  // 5x5x5 grid
                for (int si = 0; si < 25; si++) {
                    seg137:grid1[di * 25 + si] = r1;  // fill 25-element slice
                }
                for (int si = 0; si < 5; si++) {
                    for (int k = 0; k < 5; k++) {
                        seg137:grid2[di*5 + si*25 + k] = r2;
                        seg137:grid3[si*5 + k*25 + di] = r2;
                        // Note: grid3 also uses r2, not r3
                    }
                }
                break;
        }
    }

    // Post-processing: add complexity via rotation
    if (difficulty == 1) {
        RotateGrid2D();  // rotate grid2 rows
    } else if (difficulty == 3) {
        RotateGrid3D();  // rotate grid1/grid3 slices
    }

    // Assign Zoombinis to random wall positions
    AssignPositions();

    // Create wall position sprites
    CreateWallSprites();

    // Set up attribute mapping (which property category -> which axis)
    SetupAttrMapping();

    // Set up evaluation permutation (difficulty 3 only)
    if (difficulty >= 3) {
        permutation = rand(5);  // 0-5
    }
}
```

### Difficulty 1 Post-Processing: Row Rotation

```c
void RotateGrid2D() {
    int numRotations = rand(1) + 2;  // 2 or 3 rotations

    // Copy grid2 row 0 to temp buffer
    for (int si = 0; si < 5; si++)
        seg137:tempBuffer[si] = seg137:grid2[si];

    // For each subsequent row, apply circular shift
    for (int row = 1; row < 5; row++) {
        for (int iter = 0; iter < numRotations; iter++) {
            int saved = seg137:tempBuffer[4];  // save last element
            for (int si = 4; si > 0; si--)
                seg137:tempBuffer[si] = seg137:tempBuffer[si-1];  // shift right
            seg137:tempBuffer[0] = saved;  // wrap to front
        }
        // Write rotated row back
        for (int si = 0; si < 5; si++)
            seg137:grid2[row * 5 + si] = seg137:tempBuffer[si];
    }
}
```

### Difficulty 3 Post-Processing: 3D Rotation

```c
void RotateGrid3D() {
    int numRotations = rand(1) + 2;  // 2 or 3
    int direction = rand(1);         // 0 or 1
    direction = 0;  // OVERWRITTEN to 0 (intentionally disabled randomization)

    if (direction == 0) {
        // Rotate grid1 along columns for each row-pair
        for (int di = 0; di < 5; di++) {
            for (int col = 1; col < 5; col++) {
                // Copy column slice from grid1 to temp
                for (int si = 0; si < 5; si++)
                    seg137:tempBuffer[si] = seg137:grid1[di*5 + col + si*25 - 2];
                // Circular shift temp by numRotations
                for (int iter = 0; iter < numRotations; iter++) {
                    int saved = seg137:tempBuffer[4];
                    for (int si = 4; si > 0; si--)
                        seg137:tempBuffer[si] = seg137:tempBuffer[si-1];
                    seg137:tempBuffer[0] = saved;
                }
                // Write back
                for (int si = 0; si < 5; si++)
                    seg137:grid1[di*5 + col + si*25] = seg137:tempBuffer[si];
            }
        }
        // Then rotate grid3 along 5-element groups
        for (int pos = 5; pos < numPositions; pos += 5) {
            for (int si = 0; si < 5; si++)
                seg137:tempBuffer[si] = seg137:grid3[pos + si - 10];
            for (int iter = 0; iter < numRotations; iter++) {
                int saved = seg137:tempBuffer[4];
                for (int si = 4; si > 0; si--)
                    seg137:tempBuffer[si] = seg137:tempBuffer[si-1];
                seg137:tempBuffer[0] = saved;
            }
            for (int si = 0; si < 5; si++)
                seg137:grid3[pos + si] = seg137:tempBuffer[si];
        }
    }
    // direction == 1 is disabled (always 0)
}
```

### Attribute Mapping Setup

```c
void SetupAttrMapping() {
    if (difficulty <= 2) {
        // Two-property mode: randomly swap "SC" and "SH"
        int swap = rand(1);
        if (swap) {
            attrMap = {2, 1, 0};  // SC->axis3, SH->axis2
        } else {
            attrMap = {1, 2, 0};  // SH->axis2, SC->axis3
        }
    } else {  // difficulty == 3
        // Three-property mode: random permutation of {0,1,2}
        do {
            attrMap[0] = rand(2);  // 0-2
            attrMap[1] = rand(2);
            attrMap[2] = rand(2);
        } while (attrMap[0]==attrMap[1] || attrMap[1]==attrMap[2] || attrMap[2]==attrMap[0]);

        permutation = rand(5);  // 0-5, selects evaluation ordering
    }
}
```

### Slot Distribution (DistributeSlots, 0x2D8D)

Distributes Zoombinis across wall target positions, determining the dot count (1-3)
for each active target.

```c
void DistributeSlots() {
    // Clear slot counts
    for (int i = 0; i < 12; i++)
        seg137:slotCounts[i] = 0;

    int remaining = totalTargets;  // from engine shared data
    int size = 4;
    int colIndex = 0;

    while (remaining > 0) {
        size--;
        if (size < 1) size = 3;
        seg137:slotCounts[colIndex] = size;
        colIndex++;
        remaining -= size;
    }
    numColumns = colIndex;

    // Redistribute if there's leftover (remaining < 0)
    if (remaining != 0) {
        remaining = -remaining;  // make positive
        // Decrement counts > 2 to absorb leftover
        for (int i = 0; i < numColumns && remaining > 0; i++) {
            if (seg137:slotCounts[i] >= 2) {
                seg137:slotCounts[i]--;
                remaining--;
            }
        }
        // If still leftover, set all to 1
        if (remaining > 0) {
            numColumns = totalTargets;
            for (int i = 0; i < numColumns; i++)
                seg137:slotCounts[i] = 1;
        }
    }
}
```

### Position Assignment (within GenerateGrids)

```c
void AssignPositions() {
    int gridOffset = (difficulty > 1) ? 25 : 0;  // for resource ID calculation

    // Assign each slot group to a random wall position
    for (int di = 0; di < numColumns; di++) {
        int pos;
        do {
            pos = (difficulty < 2) ? rand(24) : rand(124);
        } while (seg137:positionAssignment[pos] != 0);

        seg137:positionAssignment[pos] = seg137:slotCounts[di];
    }

    // Create wall position sprites
    for (int di = 0; di < numPositions; di++) {
        positionSpriteHandles[di] = 0;

        if (seg137:positionAssignment[di] != 0) {
            // Create sprite at position di
            positionSpriteHandles[di] = CreateSprite(
                resourceID: 0x2328 + gridOffset + di,
                layer: 6
            );

            // Set dot count resource
            int dotResource = seg137:positionAssignment[di] + 0x96;
            if (difficulty > 1) dotResource += 3;

            // Store dot resource in sprite properties
            SpriteSetSubResource(positionSpriteHandles[di], dotResource);

            // Add to display
            AddToDisplay(positionSpriteHandles[di], displayContext);
        }
    }
}
```

### Core Evaluation (EvaluateMatch, 0x2657)

Finds which grid position matches the player's current mudball property selections.

```c
int EvaluateMatch() {
    switch (difficulty) {
        case 0: case 1:  // 5x5 grid, 2 properties
            if (attrMap[0] == 2) {
                // grid1 matches axis3, grid2 matches axis2
                for (int pos = 0; pos < numPositions; pos++)
                    if (seg137:grid1[pos] == selectedAxis3 &&
                        seg137:grid2[pos] == selectedAxis2)
                        return pos;
            } else {
                // grid2 matches axis3, grid1 matches axis2
                for (int pos = 0; pos < numPositions; pos++)
                    if (seg137:grid2[pos] == selectedAxis3 &&
                        seg137:grid1[pos] == selectedAxis2)
                        return pos;
            }
            return -1;

        case 2: case 3:  // 5x5x5 grid, 3 properties
            for (int pos = 0; pos < numPositions; pos++) {
                int g1 = seg137:grid1[pos];
                int g2 = seg137:grid2[pos];
                int g3 = seg137:grid3[pos];

                bool match;
                switch (permutation) {
                    case 0: match = (g1==selectedAxis3) && (g2==selectedAxis2) && (g3==selectedAxis1); break;
                    case 1: match = (g1==selectedAxis2) && (g2==selectedAxis3) && (g3==selectedAxis1); break;
                    case 2: match = (g1==selectedAxis1) && (g2==selectedAxis3) && (g3==selectedAxis2); break;
                    case 3: match = (g1==selectedAxis3) && (g2==selectedAxis1) && (g3==selectedAxis2); break;
                    case 4: match = (g1==selectedAxis2) && (g2==selectedAxis1) && (g3==selectedAxis3); break;
                    case 5: match = (g1==selectedAxis1) && (g2==selectedAxis2) && (g3==selectedAxis3); break;
                }
                if (match) return pos;
            }
            return -1;
    }
}
```

#### Permutation Table (Difficulty 2/3)

| `permutation` | grid1 matches | grid2 matches | grid3 matches |
|---|---|---|---|
| 0 | selectedAxis3 | selectedAxis2 | selectedAxis1 |
| 1 | selectedAxis2 | selectedAxis3 | selectedAxis1 |
| 2 | selectedAxis1 | selectedAxis3 | selectedAxis2 |
| 3 | selectedAxis3 | selectedAxis1 | selectedAxis2 |
| 4 | selectedAxis2 | selectedAxis1 | selectedAxis3 |
| 5 | selectedAxis1 | selectedAxis2 | selectedAxis3 |

### Event Handler (0x0F25)

Maps UI events to puzzle actions:

```c
void EventHandler(int si) {
    if (puzzleLocked) return;

    switch (si) {
        case 3:  // Go button
            if (goButtonActive || axisConfirmed || animBusy) return;
            goButtonActive++;
            axisConfirmed++;
            RecordTimestamp();
            HandleAxisSelection(type=0, index=0);  // throw!
            break;

        case 4..8:  // Axis 1 buttons (diff >= 2 only)
            if (difficulty <= 1) return;
            if (axisConfirmed && !animBusy) return;
            HandleAxisSelection(type=1, index=si-4);
            break;

        case 9..13:  // Axis 2 buttons
            if (axisConfirmed && !animBusy) return;
            HandleAxisSelection(type=2, index=si-9);
            break;

        case 14..18:  // Axis 3 buttons
            if (axisConfirmed && !animBusy) return;
            HandleAxisSelection(type=3, index=si-14);
            break;
    }
}
```

### Handle Axis Selection / Throw (HandleAxisSelection, 0x1B9B)

```c
void HandleAxisSelection(int type, int index) {
    // Check if all required axes have valid selections
    bool allValid;
    if (difficulty <= 1)
        allValid = (selectedAxis2 >= 0) && (selectedAxis3 >= 0);
    else
        allValid = (selectedAxis1 >= 0) && (selectedAxis2 >= 0) && (selectedAxis3 >= 0);

    switch (type) {
        case 0:  // GO BUTTON - throw the mudball!
            // Save previous selections
            prevAxis3 = selectedAxis3;
            prevAxis2 = selectedAxis2;
            prevAxis1 = selectedAxis1;

            if (!allValid) return;

            // Play go button animation
            PlayAnimation(goButtonHandle, 0x2711);

            // Find matching wall position
            guessWrong = 0;
            matchedPosition = EvaluateMatch();

            // Determine animation variant based on column
            int column;
            if (difficulty < 2)
                column = matchedPosition % 5;
            else
                column = (matchedPosition % 25) / 5;

            if (column == 0)      animVariant = 1;
            else if (column <= 3) animVariant = 0;
            else                  animVariant = 2;

            // Play wall hit animation
            PlayAnimation(mainAnimLayer, 0x1B74 + animVariant);
            // Create flight animation handle -> flightAnimHandle
            break;

        case 1:  // Axis 1 selection (diff >= 2)
            prevAxis2 = selectedAxis2;
            prevAxis1 = selectedAxis1;
            selectedAxis1 = index;
            if (prevAxis1 == selectedAxis1) return;
            PlayAnimation(axis1AnimHandle, 0x2712 + index);
            if (animBusy) return;
            if (allValid) PlayAcceptSound();
            break;

        case 2:  // Axis 2 selection
            prevAxis2 = selectedAxis2;
            selectedAxis2 = index;
            prevAxis1 = selectedAxis1;
            if (prevAxis2 == selectedAxis2) return;
            PlayAnimation(axis2AnimHandle, 0x2717 + index);
            if (animBusy) return;
            if (allValid) PlayAcceptSound();
            break;

        case 3:  // Axis 3 selection
            prevAxis3 = selectedAxis3;
            prevAxis2 = selectedAxis2;
            prevAxis1 = selectedAxis1;
            selectedAxis3 = index;
            if (prevAxis3 == selectedAxis3) return;
            PlayAnimation(axis3AnimHandle, 0x271C + index);
            if (animBusy) return;
            if (allValid) PlayAcceptSound();
            break;
    }

    // After any non-throw selection, trigger wrong-guess animation if needed
    if (!guessWrong && !animBusy) {
        wrongCount++;
        guessWrong++;
    }
}
```

### Mudball Flight (AcceptZoombini, 0x2E87)

Creates the mudball flight animation from launcher to wall target.

```c
void AcceptZoombini(int position) {
    if (attemptCount != 0) return;  // already in flight

    matchedPosition = position;
    attemptCount++;

    // Read wall target screen coordinates from segment 138
    int targetX = seg138:[position * 4];
    int targetY = seg138:[position * 4 + 2];
    mudballX = targetX;
    mudballY = targetY;

    // Compute flight step sizes (6 steps from launcher to target)
    stepX = (484 - targetX) / 6;   // launcher at x=484
    stepY = (318 - targetY) / 6;   // launcher at y=318

    // Override start position to launcher
    mudballX = 484;  // 0x1E4
    mudballY = 318;  // 0x13E

    // Increment accepted counter in segment 139
    seg139:acceptedCount++;

    // Create mudball sprite with variant-based resource
    int resource = 0x1B6C + acceptVariantTable[animVariant];
    // resource = 7020 + {1, 0, 2}[animVariant]
    int spriteHandle = CreateSprite(resource, layer: 6);
    acceptedSpriteHandles[seg139:acceptedCount] = spriteHandle;

    // Set up sprite with current position and target
    SetupFlightSprite(spriteHandle);
}
```

### Mudball Impact (HandleTimeout, 0x2FF2)

Called on each flight animation tick.

```c
void HandleTimeout(SpriteData* sprite) {
    attemptCount++;

    if (attemptCount > 5 || IsGameOver()) {
        // Impact! Reset attempt counter
        attemptCount = 0;
        sprite->animState = 0;
        sprite->frameIdx = -1;

        // Process the throw result
        ProcessThrowResult(sprite->positionIndex);
    } else {
        // Continue flight: move toward target
        mudballX -= stepX;
        mudballY -= stepY;
    }

    // Update sprite display
    UpdateSpritePositions(sprite);
}
```

### Process Throw Result (ProcessThrowResult, 0x248B)

Determines whether the throw is a hit or miss, and handles scoring.

```c
void ProcessThrowResult(int position) {
    if (position < 0) return;

    // Read wall target coordinates for animation
    int targetX = seg138:[position * 4];
    int targetY = seg138:[position * 4 + 2];
    mudballX = targetX;
    mudballY = targetY;

    acceptCount++;

    // Get/create reject animation sprite
    // (uses resource 0x1B6F for diff<=1, 0x1B70 for diff>=2)

    // Set up sprite at target position with transparency
    sprite->x = -1;
    sprite->y = -1;
    AddToDisplay(spriteHandle, displayContext);

    // Check how many dots at this position
    missFlag = 0;
    hitDots = seg137:positionAssignment[position];

    if (hitDots >= 1) {
        // *** HIT! ***
        correctCount++;       // track successful throws
        scoreAccum += hitDots; // add dots to score (= Zoombinis rescued)
    } else {
        // *** MISS ***
        hitDots = 0;
        goButtonActive = 0;
        missFlag++;
    }

    // Mark position as consumed
    seg137:positionAssignment[position] = -1;  // 0xFFFF
}
```

### Zoombini Spawn (SpawnZoombini, 0x1ABD)

Spawns the next Zoombini from the incoming queue to an available wall slot.

```c
void SpawnZoombini() {
    int spawnPos = (233, 392);  // default spawn position

    // Try slots 2, 1, 0 (prefer highest slot)
    for (int si = 2; si >= 0; si--) {
        if (wallSlotHandles[si] != 0) continue;  // slot occupied

        if (nextZoombiniIdx >= totalTargets) {
            overflowCount++;  // queue exhausted
            continue;
        }

        if (spawnCountdown == 0) return;  // no pending spawns

        spawnPending--;
        if (spawnPending == 0) spawnCountdown = 0;

        // Get Zoombini handle from queue (segment 152)
        int zoombHandle = seg152:[nextZoombiniIdx * 2];

        // Set up Zoombini display
        SetupZoombiniSprite(zoombHandle);

        // Assign to slot
        currentZoombIdx = nextZoombiniIdx;
        wallSlotHandles[si] = zoombHandle;
        nextZoombiniIdx++;
        activeSlot = si;
        wallSlotActive = 1;
        return;
    }

    // All slots full
    spawnPending = 0;
    spawnCountdown = 0;
}
```

---

## Complete Throw Flow

```
1. PLAYER DESIGNS MUDBALL:
   - Clicks 2 axis buttons (diff 0-1) or 3 axis buttons (diff 2-3)
   - Each axis has 5 options (button indices 0-4)
   - Axis 1: si=4-8, resources 10002-10006 (diff >= 2 only)
   - Axis 2: si=9-13, resources 10007-10011
   - Axis 3: si=14-18, resources 10012-10016

2. PLAYER THROWS (clicks Go, si=3):
   - EvaluateMatch() finds the grid position matching the selections
   - A matching position ALWAYS exists (grids are 5x5 permutation matrices)
   - Animation variant chosen based on column of matched position

3. MUDBALL FLIGHT (6 animation steps):
   - Starts at launcher position (484, 318) = bottom-right of screen
   - Moves toward wall target coordinates (from segment 138)
   - stepX = (484 - targetX) / 6
   - stepY = (318 - targetY) / 6

4. IMPACT (ProcessThrowResult):
   - Reads positionAssignment[matchedPos] from segment 137
   - If value >= 1: HIT (value = dot count = Zoombinis rescued)
     * scoreAccum += dots
     * Position marked consumed (set to -1)
   - If value == 0: MISS (no target at that combination)

5. RESCUE ANIMATION (if hit):
   - hitDots Zoombinis animate from wall to exit
   - Wall slot freed for next Zoombini

6. NEXT THROW:
   - numZoombinis decremented
   - If numZoombinis reaches 0: game ends
   - Otherwise: axes reset, player designs next mudball
```

---

## Difficulty Breakdown

| Difficulty | Grid | Axes | Positions | Throws | Properties | Extra Complexity |
|-----------|------|------|-----------|--------|-----------|------------------|
| 0 (Not So Easy) | 5x5 | 2 | 25 | ~7-8 | 2 (SC,SH) | None |
| 1 (Oh So Hard) | 5x5 | 2 | 25 | ~8 | 2 + row rotation | Grid2 rows circularly shifted |
| 2 (Very Hard) | 5x5x5 | 3 | 125 | ~8 | 3 (SC,SH,MC) | None |
| 3 (Very Very Hard) | 5x5x5 | 3 | 125 | ~9 | 3 + 3D rotation + permutation | Grid rotation + random eval ordering |

### Property Categories

The three mudball property categories are abbreviated "SC", "SH", "MC" in the code
(ds:0x1828). These likely correspond to:

| Abbreviation | Likely Meaning | Visual |
|-------------|----------------|--------|
| SC | Stamp Color / Splat Color | Color of the mark/stamp on the mudball |
| SH | Shape | Shape of the mark/stamp |
| MC | Mud Color | Color of the mudball itself |

At difficulty 0-1, only 2 categories are used (SC and SH, randomly assigned to axes).
At difficulty 2-3, all 3 categories are used.

---

## Solver Strategy

### For a Memory-Reading Solver

To solve the puzzle automatically by reading game memory:

1. **Read difficulty**: ds:0x9368
2. **Read grid data** from segment 137:
   - grid1 at offset 0x00FA (word array, N entries)
   - grid2 at offset 0x01F4
   - grid3 at offset 0x02EE (diff >= 2 only)
3. **Read position assignments** from segment 137 at offset 0x040A (word array)
   - Values 1-3 = active targets with that many dots
   - Value 0 = no target (miss position)
   - Value -1 = already consumed
4. **Read attrMap** from ds:0x9346 (3 words) to know which property maps where
5. **Read permutation** from ds:0x9522 (diff 3 only)

For each throw, find positions where positionAssignment > 0 and determine the correct
axis selections by reversing the EvaluateMatch logic:

```
For difficulty 0/1:
  Given target position pos:
    if attrMap[0] == 2:
        selectedAxis3 = grid1[pos]
        selectedAxis2 = grid2[pos]
    else:
        selectedAxis3 = grid2[pos]
        selectedAxis2 = grid1[pos]

For difficulty 2/3:
  Given target position pos and permutation p:
    Use the permutation table to map grid values to axis selections:
    Reverse-lookup which selectedAxis1/2/3 values produce a match at pos.
```

### Priority: Target highest-dot positions first (3 dots > 2 dots > 1 dot) to maximize
rescued Zoombinis per throw.

---

## rand() Calls (16 total)

| # | Seg Offset | rand(N) | Range | Purpose |
|---|-----------|---------|-------|---------|
| 1 | 0x013C | rand(4) | 0-4 | Initial selectedAxis1 |
| 2 | 0x0146 | rand(4) | 0-4 | Initial selectedAxis2 |
| 3 | 0x0150 | rand(4) | 0-4 | Initial selectedAxis3 |
| 4 | 0x14C4 | rand(4) | 0-4 | Grid gen: r1 |
| 5 | 0x14CE | rand(4) | 0-4 | Grid gen: r2 |
| 6 | 0x14D8 | rand(4) | 0-4 | Grid gen: r3 |
| 7 | 0x1677 | rand(1) | 0-1 | Diff 1: rotation count (+2) |
| 8 | 0x1732 | rand(1) | 0-1 | Diff 3: rotation count (+2) |
| 9 | 0x173F | rand(1) | 0-1 | Diff 3: rotation direction (OVERWRITTEN to 0) |
| 10 | 0x18FF | rand(24/124) | 0-24/124 | Position assignment: random target |
| 11 | 0x190B | rand(24/124) | 0-24/124 | Same (in loop) |
| 12 | 0x1A23 | rand(1) | 0-1 | Diff <=2: attribute swap |
| 13 | 0x1A5B | rand(2) | 0-2 | Diff 3: attrMap[0] |
| 14 | 0x1A65 | rand(2) | 0-2 | Diff 3: attrMap[1] |
| 15 | 0x1A6F | rand(2) | 0-2 | Diff 3: attrMap[2] |
| 16 | 0x1AA4 | rand(5) | 0-5 | Diff 3: evaluation permutation |

---

## Cross-Segment Calls

### Internal Calls (within Segment 35)
| Target | Called From | Description |
|--------|-----------|-------------|
| 35:0x03BB | 0x0308, 0x0313, etc. | LoadAnimation |
| 35:0x0494 | 0x0235 | ToggleUIState |
| 35:0x1161 | 0x02D6 | SetupResources |
| 35:0x1461 | 0x06B2 | GenerateGrids (on background load complete) |
| 35:0x1ABD | 0x0681, 0x0880, etc. | SpawnZoombini |
| 35:0x1B9B | 0x0FA5, 0x0FCD, etc. | HandleAxisSelection |
| 35:0x1F2D | 0x1117 | AnimationUpdate (timer event) |
| 35:0x20E7 | 0x19E3 | SetAnimProperties |
| 35:0x213E | 0x0841, 0x0919, etc. | UpdateSpritePositions |
| 35:0x248B | 0x3030 | ProcessThrowResult |
| 35:0x2657 | 0x1E4D | EvaluateMatch |
| 35:0x2900 | 0x0BEB, 0x0CEB, etc. | DispatchEvent |
| 35:0x2D8D | 0x02A9 | DistributeSlots |
| 35:0x2E87 | 0x29BC | AcceptZoombini |
| 35:0x2FF2 | 0x2F52 | HandleTimeout |

### Key External Calls
| Target Segment | Function | Purpose |
|---------------|----------|---------|
| Seg 14 (rand) | 14:0x0028 | Random number generator |
| Seg 1 (GDI) | 1:0x4A8E | Memory copy (memcpy equivalent) |
| Seg 44 (engine) | Various | Resource loading, animation, MHK management |
| Seg 51 (engine) | Various | Sprite creation, display management |
| Seg 53 (engine) | Various | String/resource operations |
| Seg 59 (engine) | Various | Sound/music playback |

---

## DispatchEvent Table (0x2900)

| Event ID | Handler | Description |
|----------|---------|-------------|
| 0xFFFF | 0x2B98 | Animation complete: walk Zoombini to exit |
| 0x0000 | 0x2981 | Toggle sprite visibility |
| 0x0002 | 0x29B6 | Match found: call AcceptZoombini |
| 0x0004 | 0x29C2 | Accept animation done: move Zoombini to wall |
| 0x0014 | 0x2A64 | Collect Zoombini from wall slot |
| 0x001E | 0x2B0A | Reject animation: return to queue position |
| 0x00F0-F3 | 0x2975 | Axis selection change notification |
| 0x00FA-FD | 0x2960 | Axis selection animation update |

---

## Timer Event Table (0x10CD)

| Timer ID | Handler | Description |
|----------|---------|-------------|
| 0x0020 | 0x111F | Reset: restore numZoombinis, clear animBusy |
| 0x004C | 0x1115 | Animation tick: call AnimationUpdate |
| 0x006C | 0x1115 | Same as above |
| 0x016F | 0x110B | End-game sound trigger |

---

## Notes

1. **Rotation direction override**: At offset 0x1746, the random rotation direction
   `[bp-0xE]` (for difficulty 3) is generated by `rand(1)` but immediately overwritten
   to 0 by `mov word [bp-0xE], 0`. Only direction 0 is ever used.

2. **Grid overlap for 5x5x5**: For 125 positions, the coordinate data (seg138, 500 bytes)
   and grid1 (seg137:0x00FA, 250 bytes starting at offset 250) would overlap IF they
   were in the same segment. Since coordinates are in segment 138 and grids in segment 137,
   there is no actual overlap.

3. **The three mudball properties** ("SC", "SH", "MC") each have 5 visual variants
   (button indices 0-4). The visual mapping tables at ds:0x1832 and ds:0x183C scramble
   which visual appearance corresponds to which internal index, but this is purely
   cosmetic -- the EvaluateMatch logic works entirely in button-index space.

4. **Score calculation**: `scoreAccum` (ds:0x9332) accumulates the dot values from
   each hit. The value at `positionAssignment[pos]` (1, 2, or 3) directly equals
   the number of Zoombinis rescued by hitting that target.

5. **Target scoring priority**: Distribution starts with slot sizes 3, 3, 3, 2, 1...
   (decrementing from 4, with minimum 1). Higher-dot targets should be prioritized
   by a solver to maximize rescue count per throw.


---

# v2 (PE32) — Mudball Wall / Net-Code-Karte

> **Status: Loader hat 25 Caller — NEEDS-VERIFY.** Vollständige Tabelle: `V2_VARIABLE_MAP.md` § 8.

## Code-Lokalisation

| | v1 | v2 |
|---|----|----|
| Code | Seg 35 | `.text` 0x0042BC20..0x0042C490 (+ weite Sub-Caller) |
| MHK-Loader | — | `0x0042BC20` (2160 B, **25 Caller**) |

## Verdacht
Die Loader-Funktion mit 25 Callern ist mit hoher Wahrscheinlichkeit ein Engine-
Resource-Loader-Helper (lädt einen MHK-Asset), nicht der Mudball-Init. Die
Funktion pusht auch `picker.mhk` — bestätigt Helper-Theorie.

## Verifizierte / Probable Mappings

| v1 DS | v2 VA | Bedeutung | Konfidenz |
|-------|-------|-----------|-----------|
| — | `0x00494946` | 8-Case-State-Switch (**KEIN Difficulty**, Korrektur Iter. 3) | Verified als State-Machine. Schreibwerte 0,1,2,4,6,7 (max=7) → unmöglich Difficulty |
| `0x9506` 📊 | `0x0049492C` (16 acc) | Wall-State | Probable |
| `0x950A` 📊 | `0x00494930` (12 acc) | Wall-State | Probable (benachbart) |
| `0x94FA` 📊 | `0x0049494C` (10 acc) | Selected axis | Probable |
| `0x9508` 📊 | `0x00494938` (11 acc) | Wall-State | Speculative |
| **DIFFICULTY** (echte, Iter. 3) | — | **`0x0049B242`** | **Probable** — `cmp [VA], 3` (1 site direkt, plus cmp 1/2/3 mehrfach) |
| Static lookups | `0x0048DA84..0x0048DBE0` (13 distinct VAs scale=2) | umfangreiche Word-Lookup | Probable |

## Lücke
Echte Mudball-Init muss unter den 25 Callers von 0x42BC20 lokalisiert werden,
oder es gibt eine direkt aufrufbare Init-Funktion ohne MHK-Push.
