namespace ZoombiniHelper;

/// <summary>
/// Static virtual addresses for Stone Cold Caves (caves.mhk) state in the
/// v2 binary. ImageBase = 0x00400000.
///
/// All entries verified by reverse-engineering the match function at
/// <c>0x00452CA0</c> and the per-cave dispatch jump-table at
/// <c>0x00452EAC</c>. The cave struct itself is read by the engine at
/// <c>0x00451137: call 0x452ca0</c> with <c>push 0x4a2c48</c> as the
/// struct pointer.
/// </summary>
public static class CavesMemoryMap
{
    /// <summary>1-based difficulty (1=Easy "Not So Easy"..4=Very Very Hard).
    /// Verified via cross-diff of 4 live dumps (one per difficulty).</summary>
    public const nint Difficulty = 0x00499B1C;

    /// <summary>Base of the cave-filter struct. Layout:
    /// <code>
    /// +0x00 word   axis-count (1 = single axis, 2 = dual axis 2x2 grid)
    /// +0x04 byte   filter_count for axis 1 (number of OR-conditions)
    /// +0x05..+0x09 5 bytes: attr_type per filter (1=Hair..4=Feet)
    /// +0x0A..+0x0E 5 bytes: variant per filter (1..5)
    /// +0x10 word   axis-2 enable
    /// +0x12 byte   filter_count for axis 2
    /// +0x13..+0x17 5 bytes: attr_type axis 2
    /// +0x18..+0x1C 5 bytes: variant axis 2
    /// </code></summary>
    public const nint CaveStruct = 0x004A2C48;
}
