namespace ZoombiniHelper;

/// <summary>
/// Snapshot of Mudball Wall state. Lists every still-active target
/// position with its dot count and the required axis selections.
///
/// <para>Algorithm reverse-engineered from EvaluateMatch in v2 binary
/// (0x0042E55C). For each grid position p with positionAssignment[p] &gt; 0,
/// the engine accepts a mudball whose axis selections satisfy:</para>
///
/// <list type="bullet">
///   <item>Diff 1/2 (2D, 5×5): grid1[p] and grid2[p] are the two needed
///   axis values; <see cref="MudballMemoryMap.AttrMap0"/> decides which
///   grid maps to axis 2 vs axis 3.</item>
///   <item>Diff 3/4 (3D, 5×5×5): grid1/grid2/grid3 are the three needed
///   values; <see cref="MudballMemoryMap.Permutation"/> (0..5) decides
///   which grid maps to which axis.</item>
/// </list>
///
/// <para>At Diff 4 the engine additionally rotates grid1 and grid3
/// cyclically; <see cref="RotationSteps"/> is the recovered shift count.</para>
/// </summary>
public sealed class MudballState
{
    /// <summary>One active wall target. <see cref="Axis1"/>..<see cref="Axis3"/>
    /// are the values to select on each axis (-1 if that axis isn't used at
    /// the current difficulty), already permuted from the raw grid values.
    /// <see cref="Section"/>, <see cref="Row"/>, <see cref="Column"/> are
    /// the on-screen wall coordinates (0-based). Section is -1 at Diff 1/2.</summary>
    public readonly record struct Target(int Dots,
                                         int Axis1, int Axis2, int Axis3,
                                         int Section, int Row, int Column);

    /// <summary>Permutation table from EvaluateMatch (0x0042E5F6..0x0042E812).
    /// Index = permutation value (0..5). Each entry says which axis the
    /// three grid values map to: <c>(g1→axisA, g2→axisB, g3→axisC)</c>.</summary>
    private static readonly (int g1, int g2, int g3)[] PermTable3D = new[]
    {
        (3, 2, 1),  // perm 0: g1=axis3, g2=axis2, g3=axis1
        (2, 3, 1),  // perm 1: g1=axis2, g2=axis3, g3=axis1
        (1, 3, 2),  // perm 2: g1=axis1, g2=axis3, g3=axis2
        (3, 1, 2),  // perm 3: g1=axis3, g2=axis1, g3=axis2
        (2, 1, 3),  // perm 4: g1=axis2, g2=axis1, g3=axis3
        (1, 2, 3),  // perm 5: g1=axis1, g2=axis2, g3=axis3
    };

    /// <summary>1-based difficulty (1..4) — matches the in-game UI.</summary>
    public int Difficulty { get; }

    public IReadOnlyList<Target> ActiveTargets { get; }

    /// <summary>Which property controls the wall's section coordinate.
    /// <c>null</c> at Diff 1/2 (no sections).</summary>
    public MudballProperty? PropertyForSection { get; }

    /// <summary>Which property controls the wall's row (vertical) coordinate.</summary>
    public MudballProperty PropertyForRow { get; }

    /// <summary>Which property controls the wall's column (horizontal) coordinate.</summary>
    public MudballProperty PropertyForColumn { get; }

    /// <summary>Cyclic shift applied to grid1/grid3 at Diff 4 (2 or 3),
    /// 0 when no rotation applies. Recovered from the rotated grid by
    /// inverting both candidates and picking the one that restores the
    /// pre-rotation invariant.</summary>
    public int RotationSteps { get; }

    public bool IsActive => ActiveTargets.Count > 0 || _numPositions is 25 or 125;

    private readonly int _numPositions;

    private MudballState(int diff, int numPositions, int perm, int attrMap0,
                         int rotation, IReadOnlyList<Target> targets,
                         MudballProperty pa1, MudballProperty pa2, MudballProperty pa3)
    {
        Difficulty = diff;
        _numPositions = numPositions;
        ActiveTargets = targets;
        RotationSteps = rotation;

        // Map each wall dimension to its controlling property.
        // grid1 = section, grid2 = row, grid3 = column (per v1 grid generator).
        if (diff >= 3)
        {
            var p3D = PermTable3D[perm % 6];
            PropertyForSection = PropertyOnAxis(p3D.g1, pa1, pa2, pa3);
            PropertyForRow     = PropertyOnAxis(p3D.g2, pa1, pa2, pa3);
            PropertyForColumn  = PropertyOnAxis(p3D.g3, pa1, pa2, pa3);
        }
        else
        {
            PropertyForSection = null;
            // Diff 1/2: attrMap[0]==2 ⇒ grid1→axis3 / grid2→axis2, else swap.
            (PropertyForRow, PropertyForColumn) = attrMap0 == 2 ? (pa3, pa2) : (pa2, pa3);
        }
    }

    public static MudballState Read(IMemoryReader mem)
    {
        int rawDiff = mem.ReadWord(MudballMemoryMap.Difficulty);
        int n       = mem.ReadWord(MudballMemoryMap.NumPositions);
        var pa1 = ToProperty(mem.ReadWord(MudballMemoryMap.AttrMap1));  // Axis 1
        var pa2 = ToProperty(mem.ReadWord(MudballMemoryMap.AttrMap0));  // Axis 2
        var pa3 = ToProperty(mem.ReadWord(MudballMemoryMap.AttrMap2));  // Axis 3

        if (n != 25 && n != 125)
            return new MudballState(rawDiff + 1, n, 0, 0, 0, Array.Empty<Target>(), pa1, pa2, pa3);

        var assignBytes = mem.ReadBytes(MudballMemoryMap.PositionAssignment, n * 2);
        var grid1Bytes  = mem.ReadBytes(MudballMemoryMap.Grid1, n * 2);
        var grid2Bytes  = mem.ReadBytes(MudballMemoryMap.Grid2, n * 2);
        var grid3Bytes  = mem.ReadBytes(MudballMemoryMap.Grid3, n * 2);
        int perm    = mem.ReadWord(MudballMemoryMap.Permutation);
        int attrMap = mem.ReadWord(MudballMemoryMap.AttrMap0);

        var targets = BuildTargets(rawDiff, n, perm, attrMap,
                                   assignBytes, grid1Bytes, grid2Bytes, grid3Bytes);

        // numRotations is only set at Diff 4 (rawDiff == 3); reconstructed
        // from the rotated grid1.
        int rotation = (rawDiff == 3 && grid1Bytes is not null)
            ? DetectRotationSteps(grid1Bytes) : 0;

        return new MudballState(rawDiff + 1, n, perm, attrMap, rotation, targets, pa1, pa2, pa3);
    }

    private static IReadOnlyList<Target> BuildTargets(
        int rawDiff, int n, int perm, int attrMap,
        byte[]? assignBytes, byte[]? g1Bytes, byte[]? g2Bytes, byte[]? g3Bytes)
    {
        if (assignBytes is null || g1Bytes is null || g2Bytes is null || g3Bytes is null)
            return Array.Empty<Target>();

        var targets = new List<Target>();
        for (int p = 0; p < n; p++)
        {
            ushort dots = BitConverter.ToUInt16(assignBytes, p * 2);
            if (dots is 0 or 0xFFFF or > 3) continue;
            int g1 = BitConverter.ToUInt16(g1Bytes, p * 2);
            int g2 = BitConverter.ToUInt16(g2Bytes, p * 2);
            int g3 = BitConverter.ToUInt16(g3Bytes, p * 2);
            var (a1, a2, a3) = ResolveAxes(rawDiff + 1, perm, attrMap, g1, g2, g3);
            int section = rawDiff >= 2 ? p / 25 : -1;
            int row     = rawDiff >= 2 ? (p % 25) / 5 : p / 5;
            int col     = p % 5;
            targets.Add(new Target(dots, a1, a2, a3, section, row, col));
        }
        // High-value targets first.
        targets.Sort((a, b) => b.Dots.CompareTo(a.Dots));
        return targets;
    }

    /// <summary>Apply this round's permutation/attrMap to map raw grid values
    /// onto axis selections (1-based axis indices in the ZB-1 / ZB-2 / ZB-3
    /// rows of the mudball selector). Mirrors EvaluateMatch's dispatch.</summary>
    private static (int a1, int a2, int a3) ResolveAxes(
        int diffOneBased, int perm, int attrMap, int g1, int g2, int g3)
    {
        if (diffOneBased <= 2)
            return attrMap == 2 ? (-1, g2, g1) : (-1, g1, g2);
        var (slotG1, slotG2, slotG3) = PermTable3D[perm % 6];
        var result = new int[4];          // 1-based; index 0 unused
        result[slotG1] = g1;
        result[slotG2] = g2;
        result[slotG3] = g3;
        return (result[1], result[2], result[3]);
    }

    /// <summary>Recover <c>numRotations</c> (2 or 3) by inverting the rotation
    /// for both candidates and picking the one that restores the pre-rotation
    /// invariant (each section uniform across all 25 positions).</summary>
    private static int DetectRotationSteps(byte[] grid1Bytes)
    {
        var g = new int[125];
        for (int i = 0; i < 125; i++) g[i] = BitConverter.ToUInt16(grid1Bytes, i * 2);

        int bestN = 2, bestScore = int.MinValue;
        foreach (int N in new[] { 2, 3 })
        {
            var unrot = (int[])g.Clone();
            for (int di = 0; di < 5; di++)
            for (int col = 1; col < 5; col++)
            {
                var slice = new int[5];
                for (int si = 0; si < 5; si++) slice[si] = unrot[di * 5 + col + si * 25];
                for (int si = 0; si < 5; si++) unrot[di * 5 + col + si * 25] = slice[(si + N) % 5];
            }
            int score = 0;
            for (int s = 0; s < 5; s++)
            {
                int v0 = unrot[s * 25];
                bool uniform = true;
                for (int i = 1; i < 25; i++) if (unrot[s * 25 + i] != v0) { uniform = false; break; }
                if (uniform) score++;
            }
            if (score > bestScore) { bestScore = score; bestN = N; }
        }
        return bestN;
    }

    private static MudballProperty PropertyOnAxis(int axisIdx, MudballProperty p1, MudballProperty p2, MudballProperty p3)
        => axisIdx switch { 1 => p1, 2 => p2, 3 => p3, _ => p1 };

    private static MudballProperty ToProperty(int v) => v switch
    {
        0 => MudballProperty.StampColour,
        1 => MudballProperty.Shape,
        2 => MudballProperty.MudColour,
        _ => MudballProperty.StampColour,
    };
}
