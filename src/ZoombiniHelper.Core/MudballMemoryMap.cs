namespace ZoombiniHelper;

/// <summary>
/// Static virtual addresses for Mudball Wall (net.mhk) state in v2.
/// Reverse-engineered from EvaluateMatch (0x0042E55C..0x0042E828) and
/// GenerateGrids (0x0042D6B0). All offsets verified against four live
/// dumps captured 2026-04-29.
/// </summary>
public static class MudballMemoryMap
{
    /// <summary>0-based difficulty (0..3). Verified across 4 dumps.</summary>
    public const nint Difficulty = 0x0049B242;

    /// <summary>Word array of dot counts per wall position.
    /// Entries: 0 = no target, 1/2/3 = active target with that many dots,
    /// 0xFFFF = already consumed (after a successful hit). Length is
    /// 25 (Diff 0/1) or 125 (Diff 2/3).</summary>
    public const nint PositionAssignment = 0x0049B250;

    /// <summary>Word array of grid values 0..4. EvaluateMatch compares
    /// these against the player's selected axis indices to find the
    /// matching wall position. Length matches PositionAssignment.</summary>
    public const nint Grid1 = 0x0049B760;
    public const nint Grid2 = 0x0049B528;
    public const nint Grid3 = 0x0049B42C; // diff 2/3 only — diff 0/1 leaves all-zero

    /// <summary>Number of wall positions (25 or 125).</summary>
    public const nint NumPositions = 0x0049B3F8;

    /// <summary>Player's current axis selections (button index 0..4, or
    /// -1/0xFFFF if unset). Axis1 is only present at Diff &gt;= 2; in
    /// Diff 0/1 only Axis2 and Axis3 matter.</summary>
    public const nint SelectedAxis1 = 0x0049B3FA;
    public const nint SelectedAxis2 = 0x0049B422;
    public const nint SelectedAxis3 = 0x0049B41C;

    /// <summary>Evaluation permutation 0..5 (Diff 3 only). Determines
    /// which of the 6 grid↔axis bindings the engine uses. At Diff 0/1/2
    /// always 0 (= grid1→axis3, grid2→axis2, grid3→axis1).</summary>
    public const nint Permutation = 0x0049B86A;

    /// <summary><c>attrMap[i]</c> = which property category (0=SC, 1=SH,
    /// 2=MC) is shown on the corresponding axis. Indexing inherited from
    /// v1: <c>attrMap[0]</c> → Axis 2, <c>attrMap[1]</c> → Axis 1,
    /// <c>attrMap[2]</c> → Axis 3. VAs verified by disassembly of
    /// <c>SetupAttrMapping</c> at 0x0042DB46..0x0042DBA3:
    /// <code>
    ///   mov word ptr [0x49b12c], di  ; attrMap[0]
    ///   mov word ptr [0x49b13a], si  ; attrMap[1]
    ///   mov word ptr [0x49b130], 0   ; attrMap[2]
    /// </code></summary>
    public const nint AttrMap0 = 0x0049B12C;
    public const nint AttrMap1 = 0x0049B13A;
    public const nint AttrMap2 = 0x0049B130;
}

/// <summary>The 3 visual property categories the player has to coordinate.</summary>
public enum MudballProperty
{
    /// <summary>SC — the colour of the splat / stamp inside the shape.</summary>
    StampColour = 0,
    /// <summary>SH — the shape (circle, triangle, square, star, …).</summary>
    Shape       = 1,
    /// <summary>MC — the colour of the mudball itself.</summary>
    MudColour   = 2,
}
