namespace ZoombiniHelper;

/// <summary>
/// Static virtual addresses for Hotel Dimensia (hotel.mhk) state in v2.
/// Reverse-engineered from the init/eval functions in
/// 0x00415840..0x00418900 — verified against four live dumps captured
/// 2026-04-29 (one per difficulty 1..4).
/// </summary>
public static class HotelMemoryMap
{
    /// <summary>0-based difficulty (0..3) — UI shows it as 1..4.
    /// Verified: stored from <c>call 0x44be90</c> at 0x00415873.</summary>
    public const nint Difficulty = 0x004967D6;

    /// <summary>Number of rooms in the current grid (25 for Diff 1/2/3,
    /// 125 for Diff 4). Verified: <c>cmp word ptr [0x4967e6], si</c>
    /// throughout the per-room loops.</summary>
    public const nint NumRooms = 0x004967E6;

    /// <summary>Attribute type assigned to the X axis (= columns,
    /// left→right in the visual grid). 0..3 = Hair / Eyes / Nose / Feet,
    /// v2 0-based ordering.
    ///
    /// <para>Engine internally calls this "axis Y" and stores it at
    /// 0x49668C — but the placement handler at <c>store_rule_2D</c>
    /// (0x00417EC0) writes that value to <see cref="ConstraintX"/> in a
    /// pattern that fills an entire COLUMN (5 contiguous cells in
    /// column-major addressing). Verified against memdump-115100: after
    /// one placement, <see cref="ConstraintX"/> shows 5 contiguous
    /// non-zero cells = one column, matching the user's column-axis
    /// expectation.</para></summary>
    public const nint AxisXType = 0x0049668C;

    /// <summary>Attribute type assigned to the Y axis (= rows,
    /// top→bottom). Engine internally calls this "axis X" and stores
    /// it at 0x496686, but the per-placement write fills a ROW.</summary>
    public const nint AxisYType = 0x00496686;

    /// <summary>Attribute type assigned to the Z axis (0..3). Used at
    /// Difficulty 4 only. Verified: written at 0x0041726F
    /// (<c>mov word ptr [0x4967e2], ax</c>) right after the X/Y picks.</summary>
    public const nint AxisZType = 0x004967E2;

    /// <summary>Room state array (25 words, 1 word per cell, only 25 cells
    /// at Diff 1/2/3). During init each cell holds the count of zoombinis
    /// whose (attr_X, attr_Y) maps to it; at Diff 3 a random subset of
    /// empty cells is overwritten with <see cref="BoardedMarker"/>.
    /// Verified: writes at 0x004167B7..0x004167F0 (init) and the
    /// <c>cmp ..., -1</c> at 0x00416DD2 (boarded check).</summary>
    public const nint RoomState = 0x00496014;

    /// <summary>Sentinel value (0xFFFF) written to <see cref="RoomState"/>
    /// for boarded-up cells at Difficulty 3.</summary>
    public const ushort BoardedMarker = 0xFFFF;

    /// <summary>Per-room X-axis (column) attribute value (1..5), 25 words.
    /// After a successful placement, <c>store_rule_2D</c> fills the placed
    /// zoombini's column with this value (5 contiguous cells in column-major
    /// addressing → all rows of one column). Unfilled rooms read as 0.</summary>
    public const nint ConstraintX = 0x00496698;

    /// <summary>Per-room Y-axis (row) attribute value (1..5), 25 words.
    /// Each placement fills the row with this value (5 cells at stride 5
    /// in column-major).</summary>
    public const nint ConstraintY = 0x00495E8C;
}

/// <summary>The 4 zoombini attributes as 0-based axis-type values, the way
/// the v2 binary stores them at <see cref="HotelMemoryMap.AxisXType"/> et al.
/// Convert to 1-based <c>ZoombiniVariants</c> ids by adding 1.</summary>
public enum HotelAxisAttribute
{
    Hair = 0,
    Eyes = 1,
    Nose = 2,
    Feet = 3,
}
