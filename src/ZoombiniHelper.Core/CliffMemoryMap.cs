namespace ZoombiniHelper;

/// <summary>
/// Static virtual addresses for the v2 binary's Cliff-puzzle (bridge.mhk) state.
/// All VAs assume ImageBase = 0x00400000 (no ASLR observed in the v2 retail build).
///
/// Activity detection lives in <see cref="ZoombiniHelper.Puzzles.PuzzleRegistry"/>;
/// this map only carries the puzzle-specific data needed to render the bridge
/// recommendation (rules + bookkeeping).
///
/// Verified empirically + via code-disasm. See analysis/V2_LIVE_FINDINGS.md.
/// </summary>
public static class CliffMemoryMap
{
    /// <summary>Difficulty index, 0-based (0=Easy..3=Very Hard). Verified
    /// via disasm at 0x004076DE: <c>movsx esi, word ptr [0x49453c]</c>
    /// followed by a 4-entry jump table dispatch on values 0..3.</summary>
    public const nint Difficulty   = 0x0049453C; // word, 0..3 (0=Easy)
    public const nint Attempts     = 0x004945AA; // word, 0..6 (bridge collapses at 6)
    public const nint WhichCliff   = 0x004945B2; // word, 0=Lower accepts, 1=Upper accepts
    public const nint NAllerg      = 0x004945B4; // byte, 1..3
    public const nint AllergyType0 = 0x004945B5; // 5 bytes: 1=Hair, 2=Eyes, 3=Nose, 4=Feet
    public const nint AllergyVal0  = 0x004945BA; // 5 bytes: low+high nibble = variant index 1..5
    public const int  AllergySlots = 5;

    /// <summary>Drag flag — word, =1 while a zoombini is being dragged, =0 when
    /// idle. Reliable binary signal but says nothing about WHICH zoombini is held.
    /// Used by the (still-incomplete) drag-detection in HelperOverlay.</summary>
    public const nint DragOnFlag = 0x00494522;
}
