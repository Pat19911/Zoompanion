# Titanic Tattooed Toads (LILLY.MHK, Segment 30) -- Deep Reverse Engineering

## Overview

Titanic Tattooed Toads is a standalone logic puzzle where the player guides Zoombinis
across a lily-pad grid by placing colored markers on the correct cells. Zoombini
attributes are irrelevant -- this puzzle is about navigating a grid of toads and
matching toad tattoo patterns to lily-pad patterns.

The player does NOT move toads directly. Instead:
1. Toads are auto-spawned and walk across the grid autonomously.
2. The player picks up and drops colored "path markers" onto grid cells.
3. Markers define a path between two endpoints.
4. When a toad reaches a cell with a matching marker, it follows that direction.
5. The goal is to connect the two endpoints with correct markers so toads can traverse.

## Segment Information

| Property       | Value                                    |
|----------------|------------------------------------------|
| Code Segment   | 30                                       |
| File Offset    | 0x2A600                                  |
| Segment Length | 31056 bytes                              |
| MHK File       | Lilly.MHK (DS:0x1302)                    |
| rand() calls   | 0 (deterministic puzzle)                 |
| DS Range       | 0x8500-0x8C00 (primary), plus 0x1260-0x1300 (config) |
| Functions      | ~55                                      |
| Relocations    | 908 total (153 KERNEL imports, 755 internal) |
| External Segs  | 134 (cell grid data), 189 (lily-pad layout) |

## Architecture

### Segments Referenced

| Segment | Purpose                                        |
|---------|------------------------------------------------|
| 30      | This puzzle's code                             |
| 134     | External data: cell grid, patterns, adjacency  |
| 189     | External data: lily-pad screen positions       |
| 1       | Engine core                                    |
| 9       | Timer / timestamp functions                    |
| 17      | Sprite/animation engine                        |
| 20      | Sound playback                                 |
| 29      | Sprite property getters                        |
| 44      | Resource / MHK file functions                  |
| 50      | Memory / sprite allocation                     |
| 51      | Sprite movement / animation control            |
| 53      | MHK resource loading                           |
| 55      | memset (array initialization)                  |
| 57      | Sprite rendering                               |
| 59      | Game state / puzzle completion                 |

## Grid Data Structure (Segment 134)

The puzzle operates on a 12x12 grid. Each cell is a 13-byte record.

### Cell Record (13 bytes per cell)

Base address within segment 134: `row * 0xA9 + col * 0x0D + field_offset`

| Offset | Field Offset | Type | Description                                    |
|--------|-------------|------|------------------------------------------------|
| +0x00  | 0x62C       | word | cell_x: X pixel position of cell               |
| +0x02  | 0x62E       | word | cell_y: Y pixel position of cell               |
| +0x04  | 0x630       | word | cell_x2: X pixel position (right edge)         |
| +0x06  | 0x632       | word | cell_y2: Y pixel position (bottom edge)        |
| +0x08  | 0x634       | byte | occupied: 0=empty, 1=toad currently here       |
| +0x09  | 0x635       | byte | pattern_1: pattern value for toad type 1        |
| +0x0A  | 0x636       | byte | pattern_2: pattern value for toad type 2        |
| +0x0B  | 0x637       | byte | pattern_3: pattern value for toad type 3        |
| +0x0C  | 0x638       | byte | combined_id: composite glyph = type_offset + pattern_1 |

- Row stride: 0xA9 = 169 bytes (13 cells/row * 13 bytes/cell)
- Column stride: 0x0D = 13 bytes

### Cell Pattern System

Each cell can have up to 3 independent pattern values (pattern_1/2/3), corresponding
to three different toad types. Each toad type checks its own pattern field:

| Toad Type (sprite +0xDE) | Pattern Field | Seg134 Offset |
|--------------------------|---------------|---------------|
| 1                        | pattern_1     | +0x635        |
| 2                        | pattern_2     | +0x636        |
| 3                        | pattern_3     | +0x637        |

A toad matches a cell if `cell.pattern_N == toad.tattoo_value` where N is the
toad's type.

### Combined Glyph Calculation

```
combined_id = type_offset_table[pattern_3] + pattern_1
```

Where `type_offset_table` is at seg134:0x25A8:
```
[5, 8, 11, 14, 17, 0, 0, 10, 11, 12, 1, 1]
```

This composite value determines which visual glyph to draw on the lily pad.

## Toad Sprite Data Structure

Toads are game engine sprite objects. The puzzle uses offsets within the sprite's
extended data area (sprite_ptr + 0x30 = "subobject"):

| Sub-Offset | Type | Description                                          |
|------------|------|------------------------------------------------------|
| +0xA6     | word | current X position                                    |
| +0xA8     | word | current Y position                                    |
| +0xAA     | word | anchor X (for dragging)                               |
| +0xAC     | word | anchor Y (for dragging)                               |
| +0xB0     | word | active flag (1 = toad is moving)                      |
| +0xBE     | word | difficulty level at spawn                             |
| +0xC0     | word | click mode (0=toad, nonzero=marker)                   |
| +0xC2     | byte | on_grid flag (1 = placed on grid cell)                |
| +0xC3     | byte | current column index (0..11)                          |
| +0xC4     | byte | current row index (0..11)                             |
| +0xC5     | byte | previous column (before move)                         |
| +0xC6     | byte | previous row (before move)                            |
| +0xC9     | dword| movement target X                                     |
| +0xCD     | dword| movement destination X                                |
| +0xCF     | word | movement destination Y                                |
| +0xD5     | byte | current direction (0=up, 1=right, 2=down, 3=left)     |
| +0xD6     | byte | animation state                                       |
| +0xD9     | word | animation frame / resource ID                         |
| +0xDE     | byte | toad_type (1, 2, or 3)                                |
| +0xDF     | byte | tattoo_value (pattern to match)                       |
| +0xE0     | byte | combined display value                                |
| +0xF2     | word | visit_count matrix (26-byte array: 12 rows * word)    |

### Visit Count Matrix

Each toad maintains a 12x12 visit matrix at subobject offset +0xF2, indexed as:
```
visit_matrix[row * 0x1A + col * 2] (word)
```
Stride per row: 0x1A = 26 bytes (13 words for 12 columns + 1 padding).
This tracks how many times the toad has visited each cell.

When visit_count >= 0x2710 (10000), the matrix is reset to zero (prevents overflow).

## Toad Movement Algorithm (Function 0x41EC)

The pathfinding function is at seg30:0x41EC. It determines where a toad moves next:

```c
// seg30:0x41EC -- returns animation resource ID for the chosen direction,
// or 0 if toad is stuck
word toad_find_next_move(sprite_subobject *toad) {
    // Read current position
    byte col = toad->c3;  // current column
    byte row = toad->c4;  // current row
    byte dir = toad->d5;  // current facing direction

    // Check if visit count overflow -- reset if >= 10000
    if (toad->visit_matrix[row * 0x1A + col * 2] >= 0x2710) {
        // Clear entire 12x12 visit matrix
        for (r = 0; r < 12; r++)
            for (c = 0; c < 12; c++)
                toad->visit_matrix[r * 0x1A + c * 2] = 0;
        toad->visit_matrix[row * 0x1A + col * 2] = 1;
    }

    word current_visits = toad->visit_matrix[row * 0x1A + col * 2];
    if (current_visits == 0) current_visits = 1;
    word original_visits = current_visits;

    // Try all 4 directions, starting from current direction
    byte try_dir = dir;
    byte tried = 0;
    byte reached_edge = 0;
    word best_dir = 5;  // 5 = no direction found

    for (tried = 0; tried < 4 && !reached_edge; tried++) {
        byte new_col = col;
        byte new_row = row;
        byte can_move = 1;

        // Calculate new position based on direction
        switch (try_dir) {
            case 0: // UP -- decrease row
                new_row--;
                if (new_row < 0) { new_row = 0; can_move = 0; }
                break;
            case 1: // RIGHT -- increase column
                new_col++;
                if (new_col > 11) { new_col = 11; can_move = 0; reached_edge = 1; }
                break;
            case 2: // DOWN -- increase row
                new_row++;
                if (new_row > 11) { new_row = 11; can_move = 0; }
                break;
            case 3: // LEFT -- decrease column
                new_col--;
                if (new_col < 0) { new_col = 0; can_move = 0; }
                break;
        }

        if (can_move) {
            // Check if target cell is unoccupied
            if (cell_grid[new_row][new_col].occupied != 0) {
                can_move = 0;
            }

            // Check toad-type-specific pattern matching
            if (can_move) {
                switch (toad->toad_type) {
                    case 1:
                        if (cell_grid[new_row][new_col].pattern_1 != toad->tattoo_value)
                            can_move = 0;
                        break;
                    case 2:
                        if (cell_grid[new_row][new_col].pattern_2 != toad->tattoo_value)
                            can_move = 0;
                        break;
                    case 3:
                        if (cell_grid[new_row][new_col].pattern_3 != toad->tattoo_value)
                            can_move = 0;
                        break;
                }
            }

            // Check visit count -- prefer less-visited cells
            if (can_move) {
                word target_visits = toad->visit_matrix[new_row * 0x1A + new_col * 2];
                if (target_visits < current_visits) {
                    best_dir = try_dir;
                    current_visits = target_visits;
                    // Store candidate position
                    best_col = new_col;
                    best_row = new_row;
                }
            }
        }

        // Rotate to next direction
        try_dir++;
        if (try_dir > 3) try_dir = 0;
    }

    // If reached right edge, check for special "arrived" handling
    if (reached_edge) {
        if (cell_grid[new_row][new_col].field_641 == 0) {
            // Mark arrival
            best_dir = 4;  // special "arrived at edge" direction
            cell_grid[new_row][new_col].field_641 = 1;
        } else {
            best_dir = 5;  // already arrived, no direction
        }
    }

    // Apply result based on chosen direction
    switch (best_dir) {
        case 0: // Move UP
            toad->d5 = 0;
            toad->d9 = direction_anim_table_0[original_visits]; // DS:0x1282
            // Update visit matrix for target cell
            toad->visit_matrix[(row-1)*0x1A + col*2] = original_visits + 1;
            cell_grid[best_row][best_col].occupied = 1;
            return toad->d9;

        case 1: // Move RIGHT
            toad->d5 = 1;
            toad->d9 = direction_anim_table_1[original_visits]; // DS:0x128A
            toad->visit_matrix[row*0x1A + (col+1)*2] = original_visits + 1;
            cell_grid[best_row][best_col].occupied = 1;
            return toad->d9;

        case 2: // Move DOWN
            toad->d5 = 2;
            toad->d9 = direction_anim_table_2[original_visits]; // DS:0x1292
            toad->visit_matrix[(row+1)*0x1A + col*2] = original_visits + 1;
            cell_grid[best_row][best_col].occupied = 1;
            return toad->d9;

        case 3: // Move LEFT
            toad->d5 = 3;
            toad->d9 = direction_anim_table_3[original_visits]; // DS:0x129A
            toad->visit_matrix[row*0x1A + (col-1)*2] = original_visits + 1;
            cell_grid[best_row][best_col].occupied = 1;
            return toad->d9;

        case 4: // Arrived at right edge (success)
            toad->d5 = 1;
            toad->d9 = 0x272F; // "arrived" animation
            return toad->d9;

        default: // Stuck or no valid move
            return 0;
    }
}
```

### Direction Constants and Animation Tables

Direction encoding:
- 0 = Up (decrease row)
- 1 = Right (increase column)
- 2 = Down (increase row)
- 3 = Left (decrease column)

Animation resource tables (DS offsets, each 12 words):
- DS:0x1282 -- direction 0 (up) animations
- DS:0x128A -- direction 1 (right) animations
- DS:0x1292 -- direction 2 (down) animations
- DS:0x129A -- direction 3 (left) animations

## Grid Setup (Function 0x5F02)

The grid setup function initializes the puzzle state:

```c
void setup_grid() {  // seg30:0x5F02
    // Initialize shuffle arrays
    for (i = 0; i < 13; i++) {
        seg134[0x2616 + i*2] = i;  // column identity
        seg134[0x2632 + i*2] = i;  // column shuffle
    }
    seg134[0x2630] = 0;
    seg134[0x264C] = 0;

    // Clear all cell occupied flags
    for (row = 0; row < 12; row++)
        for (col = 0; col < 13; col++)
            cell_grid[row][col].occupied = 0;

    // Determine difficulty parameters
    difficulty = DS:[0x11A4];  // current difficulty level (1-4)
    switch (difficulty) {
        case 1: case 2:
            num_active_toad_types = 0;  // only base patterns
            DS:[0x8C48] = 0;
            break;
        case 3:
            num_active_toad_types = 2;
            // Choose pattern set (3, 4, or 5) based on progression
            chosen_set = random(3, 5);
            DS:[0x8C48] = chosen_set;
            load_pattern_data(chosen_set);
            break;
        case 4:
            num_active_toad_types = 3;
            chosen_set = random(4, 5);
            DS:[0x8C48] = chosen_set;
            load_pattern_data(chosen_set);
            break;
    }

    // Apply pattern rotations/reflections based on random choice
    rotation = random(0, 2);
    apply_grid_transforms(rotation);

    // Randomly remove some columns from the grid
    for (i = 0; i < num_to_remove; i++) {
        pick = random(1, remaining);
        // Shuffle the column availability array
        col_index = seg134[0x2632 + pick*2];
        seg134[0x2616 + col_index*2] = 0;  // disable column
        // Compact array
    }

    // Build adjacency/topology data
    build_grid_topology();  // call 0x5C1E

    // Populate cells with patterns
    for (row = 0; row < 12; row++) {
        for (col = 0; col < 12; col++) {
            // Clear visit/adjacency data
            seg134[row*0x1A + col*2 + 0x21E6] = 0xFFFF;
            cell_grid[row][col].occupied = 0;
            cell_grid[row][col].pattern_1 = 0;
            cell_grid[row][col].pattern_2 = 0;

            // For each of 3 pattern sources:
            for (src = 0; src < 3; src++) {
                // Read pattern from source table
                value = source_tables[src][row*0x18 + col*2];
                if (value != 0) {
                    // Adjust value based on active toad type
                    if (DS:[0x8C48] == src+3)
                        adjusted = value;
                    else
                        adjusted = value + 1;
                    // Clamp to valid range
                    // Source 0: range 1-3
                    // Source 1: range 4-7
                    // Source 2: range 8-12

                    // Check availability and randomly place
                    // (uses seg134:0x25E6 for row/col flags)
                    // (uses seg134:0x25FE for column usage counts)
                }
            }

            // For patterns not set by source tables, generate randomly
            if (pattern_1_not_set) cell_grid[row][col].pattern_1 = random(0, 2);
            if (pattern_2_not_set) cell_grid[row][col].pattern_2 = random(0, 3);
            if (pattern_3_not_set) cell_grid[row][col].pattern_3 = random(0, 4);

            // Calculate combined glyph
            cell_grid[row][col].combined_id =
                seg134[0x25A8 + cell_grid[row][col].pattern_3 * 2]
                + cell_grid[row][col].pattern_1;

            // Calculate pixel positions
            cell_grid[row][col].cell_x =
                X_table[(row+1)*2] + col * 0x23;
            cell_grid[row][col].cell_y =
                Y_table[(row+1)*2] + DS:[0x1268 + col*2];
            cell_grid[row][col].cell_x2 = cell_x + 0x24;
            cell_grid[row][col].cell_y2 = cell_y + 0x1E;
        }
    }

    // At difficulty 2+, set up start/end points and swap them
    if (difficulty > 1) {
        DS:[0x8C4E] = seg134[0x2484]; // start_col
        DS:[0x8C50] = seg134[0x248E]; // start_row
        DS:[0x8C52] = seg134[0x2486]; // end_col
        DS:[0x8C54] = seg134[0x2490]; // end_row
        swap_path_endpoints();  // call 0x5831

        DS:[0x8C4E] = seg134[0x2488]; // alt start_col
        DS:[0x8C50] = seg134[0x2492]; // alt start_row
        DS:[0x8C52] = seg134[0x248A]; // alt end_col
        DS:[0x8C54] = seg134[0x2494]; // alt end_row
        swap_path_endpoints();  // call 0x5831
    }

    // Calculate number of required correct placements
    total_patterns = count_active_patterns + 5;
    DS:[0x854C] = (total_patterns + 5) / 6;  // moves_per_difficulty_step
}
```

## Grid Topology Builder (Function 0x5C1E)

Builds the adjacency data for the 3 toad-type groups (rows 0-2, 3-6, 7-11):

```c
void build_grid_topology() {  // seg30:0x5C1E
    // Copy base topology data for 3 groups
    // Group 0: cells 0-2 (3 cells)
    for (i = 0; i < 3; i++) {
        seg134[0x24CC + i*2] = seg134[0x2526 + i*2]; // X-index
        seg134[0x24D4 + i*2] = seg134[0x2540 + i*2]; // Y-index
        seg134[0x24DC + i*2] = seg134[0x231E + i*2]; // type offset
    }
    // Group 1: cells 3-6 (4 cells)
    for (i = 3; i < 7; i++) {
        seg134[0x24DE + i*2] = seg134[0x2526 + i*2];
        seg134[0x24E8 + i*2] = seg134[0x2540 + i*2];
        seg134[0x24F2 + i*2] = seg134[0x231E + i*2];
    }
    // Group 2: cells 7-11 (5 cells)
    for (i = 7; i < 12; i++) {
        seg134[0x24F4 + i*2] = seg134[0x2526 + i*2];
        seg134[0x2500 + i*2] = seg134[0x2540 + i*2];
        seg134[0x250C + i*2] = seg134[0x231E + i*2];
    }

    // Assign cell patterns from shuffled topology
    // For each of 13 cell types (1..12):
    for (cell_type = 1; cell_type < 13; cell_type++) {
        // Select which group (based on cell_type ranges 1-3, 4-6, 7-12)
        // via jump table at 0x5EEA

        pick = random(0, group_size);
        // Assign from group's data arrays
        seg134[cell_type * 6 + 0x255A] = group_x[pick]; // pattern type
        seg134[cell_type * 6 + 0x255C] = group_y[pick]; // pattern value
        seg134[cell_type * 6 + 0x255E] = group_z[pick]; // additional data

        // Remove picked item from group (compact array)
        group_size--;
    }
}
```

### Cell Pattern Definition Table (seg134:0x255A)

6-byte records, indexed by cell_type (1..12):
```
struct cell_pattern_def {
    word pattern_type;    // +0x00 (0x255A)
    word pattern_value;   // +0x02 (0x255C)
    word extra_data;      // +0x04 (0x255E)
};
```

## Path Endpoint Swap (Function 0x5831)

Swaps pattern data between two grid cells to create alternative paths:

```c
void swap_path_endpoints() {  // seg30:0x5831
    // Swap 4 bytes (pattern values) between start and end cells
    // start = cell_grid[DS:0x8C50][DS:0x8C4E]
    // end   = cell_grid[DS:0x8C54][DS:0x8C52]

    // Read source cell data (bytes at +9, +10, +11, +12 within cell)
    temp[0..3] = start_cell[+9..+12];
    start_cell[+9..+12] = end_cell[+9..+12];
    end_cell[+9..+12] = temp[0..3];

    // Update all active toads that reference swapped cells
    for (i = 0; i < DS:[0x8A0A]; i++) {
        toad = get_toad_sprite(i);
        if (toad != NULL && toad->on_grid) {
            // Reset visit counts for both swapped cells
            toad->visit_matrix[start_row*0x1A + start_col*2] = 0;
            toad->visit_matrix[end_row*0x1A + end_col*2] = 0;

            // Check if toad's current pattern still matches
            // If not at start, check at end (and vice versa)
            recalculate_toad_movement(toad);
        }
    }
}
```

## Toad Placement / Spawning (Function 0x484E)

```c
void place_toads() {  // seg30:0x484E
    DS:[0x8EAA] = 11;  // max shuffle index

    // Initialize shuffle array (0..11)
    for (i = 0; i <= DS:[0x8EAA]; i++)
        DS:[0x12AA + i*2] = i;

    for (si = 0; si < DS:[0x8A0A]; si++) {  // 0x8A0A = 12 (num toads)
        // Pick random toad from shuffle array
        pick = random(0, DS:[0x8EAA]);

        // Get toad's initial row/col from tables
        toad_index = DS:[0x12AA + pick*2];
        initial_row = DS:[0x12C4 + toad_index*2];  // [1,1,1, 2,2,2,2, 3,3,3,3,3]
        initial_col = DS:[0x12DC + toad_index*2];  // [0,1,2, 0,1,2,3, 0,1,2,3,4]

        // Store toad's assigned position
        DS:[0x8582 + si*2] = initial_row;
        DS:[0x859A + si*2] = initial_col;

        // Remove from shuffle (compact)
        for (j = pick; j <= DS:[0x8EAA]; j++)
            DS:[0x12AA + j*2] = DS:[0x12AA + (j+1)*2];
        DS:[0x8EAA]--;

        // Create toad sprite
        sprite_id = create_sprite(resource_0x273B + si, ...);
        DS:[0x866C + si*2] = sprite_id;
    }
}
```

### Initial Toad Positions

Toads are arranged in a triangular pattern on the left side:

```
Row 0: (not used for initial placement)
Row 1: cols 0, 1, 2        (3 toads)
Row 2: cols 0, 1, 2, 3     (4 toads)
Row 3: cols 0, 1, 2, 3, 4  (5 toads)
Total: 12 toads
```

DS:0x12C4 (row indices): `[1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3]`
DS:0x12DC (col indices): `[0, 1, 2, 0, 1, 2, 3, 0, 1, 2, 3, 4]`

## Marker Drag-and-Drop (Function 0x6CDF)

The player interacts by picking up colored markers and dropping them on grid cells.
The marker type determines which toad-type path it affects.

```c
void handle_marker_drag(sprite *clicked_sprite, ...) {  // seg30:0x6CDF
    DS:[0x8C60] = 0;  // clear current selection

    // Determine if this is a toad or a marker based on sprite subobject +0xC0
    if (subobj->c0 == 0) {
        // It's a toad -- set up for dragging
        DS:[0x8EC0] = 1;  // drag mode
    } else {
        // It's a path marker
        DS:[0x8EC0] = 4;  // marker mode
        DS:[0x8C5E] = 4;  // first endpoint mode
    }

    // Main drag loop
    while (DS:[0x8EC0] != 0) {
        // Track mouse position
        get_mouse_position(&mouse_x, &mouse_y);

        // Update sprite position to follow mouse
        // Clamp to screen boundaries

        // Check if dropped on valid cell
        if (mouse_released) {
            if (DS:[0x8EC0] == 3) {  // dropping
                // Find which grid cell the mouse is over
                for (row = 0; row < 12; row++) {
                    for (col = 0; col < 12; col++) {
                        rect = cell_grid[row][col].bounds (with 4px padding);
                        if (point_in_rect(mouse_pos, rect)) {
                            found_cell = true;
                            target_col = col;
                            target_row = row;
                        }
                    }
                }

                if (found_cell && cell_grid[target_row][target_col].occupied == 0) {
                    if (DS:[0x8C5E] == 4) {
                        // First endpoint placed
                        DS:[0x8C4E] = target_col;
                        DS:[0x8C50] = target_row;
                        place_marker_sprite(DS:[0x8C44], target_row, target_col);
                        DS:[0x8C5E] = 5;  // now placing second endpoint
                        DS:[0x8C66] = 1;  // path calculation pending
                    } else if (DS:[0x8C5E] == 5) {
                        // Second endpoint placed
                        DS:[0x8C52] = target_col;
                        DS:[0x8C54] = target_row;
                        place_marker_sprite(DS:[0x8C46], target_row, target_col);
                        DS:[0x8C5E] = 6;  // both endpoints placed

                        // Check if both endpoints are the same cell
                        if (target_col != DS:[0x8C4E] || target_row != DS:[0x8C50]) {
                            // Different cells -- count as a move
                            DS:[0x854E]++;

                            if (DS:[0x854E] >= DS:[0x854C]) {
                                // Reached difficulty step threshold
                                if (DS:[0x854A] < 6) {
                                    DS:[0x854A]++;  // increase difficulty
                                    DS:[0x854E] = 0;

                                    // Update difficulty display sprite
                                    // Check for puzzle completion
                                    if (DS:[0x854A] == 6 && all_toads_arrived) {
                                        trigger_victory();
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
```

## Key DS Offsets for Solver

### Primary Game State

| DS Offset  | Size   | Description                                          |
|------------|--------|------------------------------------------------------|
| 0x11A4     | word   | Current difficulty level (1=Not So Easy, 2=Oh So Hard, 3=Very Hard, 4=Very Very Hard) |
| 0x854A     | word   | Current "anger level" (0-6, increases on wrong moves) |
| 0x854C     | word   | Moves per difficulty step (threshold)                |
| 0x854E     | word   | Current move counter within step                     |
| 0x8580     | word   | Animation flag                                       |
| 0x89EC     | word   | Current toad spawn index (cycles 0..max)             |
| 0x89EE     | word   | Total toads spawned so far                           |
| 0x89F0     | word   | Total number of toads to spawn                       |
| 0x89F4     | word   | Toads that have successfully crossed                 |
| 0x8A00     | word   | Max toad spawn rotation count                        |
| 0x8A02     | word   | Timer state                                          |
| 0x8A04     | dword  | Next spawn timer (timestamp + 480 ticks)             |
| 0x8A08     | word   | Toad spawn state                                     |
| 0x8A0A     | word   | Number of active toads (always 12)                   |
| 0x8A0C     | word   | Additional config                                    |

### Path Markers

| DS Offset  | Size   | Description                                          |
|------------|--------|------------------------------------------------------|
| 0x8C42     | word   | Marker sprite ID (player's draggable marker)         |
| 0x8C44     | word   | Endpoint marker 1 sprite ID                          |
| 0x8C46     | word   | Endpoint marker 2 sprite ID                          |
| 0x8C48     | word   | Active pattern set (3, 4, or 5)                      |
| 0x8C4A     | word   | Current target column for toad interaction            |
| 0x8C4C     | word   | Current highlighted toad index (-1 = none)           |
| 0x8C4E     | word   | Endpoint 1 column                                    |
| 0x8C50     | word   | Endpoint 1 row                                       |
| 0x8C52     | word   | Endpoint 2 column                                    |
| 0x8C54     | word   | Endpoint 2 row                                       |
| 0x8C56     | word   | Path state 1                                         |
| 0x8C58     | word   | Path state 2                                         |
| 0x8C5E     | word   | Marker placement phase (0=idle, 4=place_1st, 5=place_2nd, 6=done) |
| 0x8C60     | word   | Currently selected toad for interaction               |
| 0x8C66     | word   | Path recalculation flag (1=needs recalc)             |
| 0x8C6C     | word   | Game active flag (1=running)                         |
| 0x8C6E     | word   | Sound state                                          |
| 0x8C70     | word   | Additional state                                     |

### Toad Arrays

Negative offsets in the disassembly map to unsigned DS offsets as follows:

| DS Offset  | Disasm Form     | Size      | Description                              |
|------------|-----------------|-----------|------------------------------------------|
| 0x8582     | [bx - 0x7A7E]  | 24 bytes  | Toad assigned row (12 words)             |
| 0x859A     | [bx - 0x7A66]  | 24 bytes  | Toad assigned col (12 words)             |
| 0x85B2-85E0|                 | various   | Sound/resource pointers (3 sets)         |
| 0x85E2     |                 | word      | Special toad movement sprite             |
| 0x85E4     |                 | word      | Toad state 1                             |
| 0x85E6     |                 | word      | Toad state 2                             |
| 0x85E8     |                 | word      | Toad state 3                             |
| 0x85EA     |                 | word      | Toad state 4                             |
| 0x85EC     |                 | word      | Toad cleanup sprite                      |
| 0x85EE     | [bx - 0x7A12]  | 8*N bytes | Spawn config: 8-byte records            |
| 0x85F0     | [bx - 0x7A10]  | per entry | +2: pattern type (byte)                 |
| 0x85F2     | [bx - 0x7A0E]  | per entry | +4: pattern value                       |
| 0x85F4     | [bx - 0x7A0C]  | per entry | +6: extra data                          |
| 0x862A     | [bx - 0x79D6]  | 24 bytes  | Toad sound/layer table (12 words)        |
| 0x8642     | [bx - 0x79BE]  | 42 bytes  | Toad assignment table (21 words)         |
| 0x866C     | [bx - 0x7994]  | 28 bytes  | Toad sprite ID array (14 words)          |
| 0x8688     | [bx - 0x7978]  | varies    | Toad delete queue / tracking array       |
| 0x87A8     | [bx - 0x7858]  | 24+ bytes | Active toad sprite IDs (during gameplay) |

### Spawn Configuration

| DS Offset  | Size      | Description                                      |
|------------|-----------|--------------------------------------------------|
| 0x85EE     | 8*N bytes | Toad spawn config table (8 bytes per entry)      |
|            |           | Field +0: column for next spawn                  |
|            |           | Field +2: pattern type                           |
|            |           | Field +4: pattern value                          |
|            |           | Field +6: extra data                             |
| 0x12AA     | 24 bytes  | Shuffle array for random toad selection           |
| 0x12C4     | 24 bytes  | Initial row table: [1,1,1,2,2,2,2,3,3,3,3,3]    |
| 0x12DC     | 24 bytes  | Initial col table: [0,1,2,0,1,2,3,0,1,2,3,4]    |
| 0x1268     | 24 bytes  | Y-offset table: [2,2,4,4,6,6,8,8,10,10,12,12]   |

### External Segment 134 Key Tables

| Seg134 Offset | Description                                          |
|---------------|------------------------------------------------------|
| 0x062C        | Cell grid base: cell_x positions                     |
| 0x0634        | Cell grid: occupied flags                            |
| 0x0635        | Cell grid: pattern_1 values                          |
| 0x0636        | Cell grid: pattern_2 values                          |
| 0x0637        | Cell grid: pattern_3 values                          |
| 0x0638        | Cell grid: combined glyph IDs                        |
| 0x21E6        | Cell visit/ownership matrix (12x12, 0x1A stride)     |
| 0x231E        | Toad type base indices [0..11]                       |
| 0x2338        | Toad type mapping (6 words)                          |
| 0x2340        | Sprite positions for toad destinations               |
| 0x23E8        | Toad arrival positions (x,y pairs)                   |
| 0x241C        | Hit-test rectangles for grid rows (8 bytes each)     |
| 0x2484        | Path start/end configuration (8 words)               |
| 0x2498        | Screen boundary test rect 1                          |
| 0x24A0        | Screen boundary test rect 2                          |
| 0x24A8        | Screen boundary test rect 3                          |
| 0x24CC-0x251A | Adjacency arrays for 3 groups                       |
| 0x2526        | Row indices for 13 cell types: [1,1,1,2,2,2,2,3,3,3,3,3] |
| 0x2540        | Col indices for 13 cell types: [0,1,2,0,1,2,3,0,1,2,3,4] |
| 0x255A        | Cell pattern definitions (6-byte records, 13 entries)|
| 0x25A8        | Type offset table: [5,8,11,14,17,0,0,10,11,12,1,1]  |
| 0x25BC        | Difficulty progression: [1,1,1,2,2,3,3,4,4,5,5,6...]|
| 0x25E6        | Row/column availability flags (13 words)             |
| 0x25FE        | Column usage counts (13 words)                       |
| 0x2616        | Column enable/identity array (13 words)              |
| 0x2632        | Column shuffle array (13 words)                      |

## Difficulty Levels

| Level | DS:[0x11A4] | Pattern Types | Endpoint Behavior                    |
|-------|-------------|---------------|--------------------------------------|
| 1     | 1           | Type 1 only   | No marker placement needed           |
| 2     | 2           | Type 1 only   | 2 endpoints, base patterns           |
| 3     | 3           | Types 1+2     | 2 endpoints, randomly chosen set 3/4/5|
| 4     | 4           | Types 1+2+3   | 2 endpoints, randomly chosen set 4/5  |

### Anger/Failure Tracking

- DS:[0x854A] = "anger level" (0-6)
- Incremented each time the player places markers forming an incorrect path
- At anger=6, if all toads have arrived -> victory; otherwise -> partial failure
- DS:[0x854C] = number of moves per anger increase (calculated from grid complexity)
- DS:[0x854E] = current move count within step

## Toad Auto-Walk Logic (Main Handler 0x0BDB)

The main tick handler processes toad movement:

1. Check if animation is in progress (0x8ED0)
2. Process queued sprite movements (multiple animation queues at various seg134 offsets)
3. At difficulty 3+, spawn new toads periodically:
   - Check spawn timer (DS:0x8A04 = timestamp + 480)
   - Select next spawn from spawn config table
   - Check if target cell is unoccupied
   - If occupied (seg134:[row*0xA9 + col*0xD + 0x634] != 0), skip
   - Otherwise, create toad sprite and assign type/tattoo
4. For each walking toad:
   - Call 0x41EC (pathfinding) to determine next move
   - If result = 0x272F -> toad has reached the right edge (success animation)
   - If result = 0 -> toad is stuck (failure animation)
   - Otherwise -> play directional walk animation
5. When toad completes walk to new cell:
   - Update `occupied` flag on old cell (clear) and new cell (set)
   - Update toad's column/row indices
   - Recalculate pixel position

## Pattern Rotation / Transform Functions

### Function 0x69F5: Grid Data Rotation

Rotates a 12x12 word grid by 0, 90, 180, or 270 degrees:
- Mode 0: Flip rows vertically (row = 11-row)
- Mode 1: Transpose and flip (180 degree rotation)
- Mode 2: Flip columns horizontally (col = 11-col)

### Function 0x6B7D: Grid Data Reflection

Reflects a 12x12 word grid:
- Mode 0: Horizontal reflection (column reversal)
- Mode 1: Vertical reflection (row reversal)

These transforms are applied to the source pattern tables during grid setup to
create variety in each playthrough.

## Victory / Completion

- Function 0x7866 handles victory when all toads have crossed
- DS:[0x89F4] (toads arrived) is compared to DS:[0x89F0] (total required)
- All Zoombinis pass regardless of attributes

## Key Constants / Resource IDs

| Value  | Meaning                                     |
|--------|---------------------------------------------|
| 0x272F | "Arrived" animation resource                 |
| 0x273B | Base toad sprite resource (+ toad index)     |
| 0x2749 | Toad failure/rejection animation             |
| 0x274A | Toad wrong-direction animation               |
| 0x274B | Toad removal animation                       |
| 0x274C | Toad partial-arrival animation (per type)    |
| 0x274D | Toad full-arrival animation (per type)       |
| 0x2753 | Toad spawn/creation animation                |
| 0x2755 | Toad "fully stuck" animation                 |
| 0x275E | Difficulty display base resource (+ level)   |
| 0x277D | Toad idle animation (+ toad index)           |
| 0x279D | Toad walk-to-position animation              |
| 0x2723 | Marker endpoint animation                    |
| 0x2710 | Visit counter overflow threshold (10000)     |

## Summary for Solver

To solve this puzzle programmatically:

1. **Read the grid**: Cell patterns are at seg134, indexed by `row*0xA9 + col*0xD`.
   Pattern fields are at offsets +0x635 (type1), +0x636 (type2), +0x637 (type3).

2. **Read toad data**: Each toad's type is at subobject+0xDE, tattoo value at +0xDF.
   Current position at +0xC3 (col) and +0xC4 (row).

3. **Match toads to paths**: A toad can traverse a cell only if the cell's
   pattern_N field (for the toad's type N) equals the toad's tattoo_value.

4. **Path endpoints**: DS:[0x8C4E/0x8C50] = endpoint 1 (col/row),
   DS:[0x8C52/0x8C54] = endpoint 2 (col/row). Place markers by writing these.

5. **Trigger recalculation**: Set DS:[0x8C66] = 1 to trigger path swap
   (function 0x5831) which updates pattern data and redirects walking toads.

6. **Monitor progress**: DS:[0x89F4] = toads arrived, DS:[0x89F0] = total needed.
   When equal (and DS:[0x854A] < 6), puzzle is solved.


---

# v2 (PE32) — Titanic Toads / Lilly-Code-Karte

> **Status: Loader hat 10 Caller — NEEDS-VERIFY.** Vollständige Tabelle: `V2_VARIABLE_MAP.md` § 6.

## Code-Lokalisation

| | v1 | v2 |
|---|----|----|
| Code | Seg 30 | `.text` 0x00419770..0x0041FC10 |
| MHK-Loader | — | `0x00419770` (2416 B, **10 Caller** — auffällig) |

## Verdacht
Die Loader-Funktion mit 10 Callern ist möglicherweise ein Shared-Helper, kein
exklusiver Lilly-Init. Mehrere Caller stammen von anderen Puzzle-Wrappern
(0x412760 = Hotel, 0x410520 = Fleens), was die Theorie stützt.

## Difficulty-Dispatcher
```
@0x0041F7B8  cmp esi, 3  ; difficulty in esi
              jmp [esi*4 + 0x41F8DC]
```
Wieder Argument-basiert.

## Probable Mappings (basierend auf Heat-Map)

| v1 DS | v2 VA | Bedeutung | Konfidenz |
|-------|-------|-----------|-----------|
| `0x8C6C` 📊 | `0x004994EC` | primary state (cmp 5, cmp 3) | Probable |
| `0x85E6..0x85EC` | `0x00498B58..0x00498B5B` (byte arrays scale=1) | Lilypad-Patterns | Probable |
| `0x8C38/0x8C40` | `0x00497310/0x00497314` (each 11R) | Toad-State (read-only Tabellen) | Probable |
| Pattern-Tabelle | `0x0049745C..` (mehrere indirekte Reads) | Pattern-Lookup | Speculative |

## Lücke
Echte Lilly-Init unklar. Solver-Übertragung erfordert zusätzliche Iteration.
