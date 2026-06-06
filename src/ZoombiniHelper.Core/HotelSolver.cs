namespace ZoombiniHelper;

/// <summary>
/// Constraint solver for Hotel Dimensia Difficulty 3 (boarded mode). The
/// engine pre-decides a hidden 5-element X/Y permutation during init,
/// boards a random subset of cells whose <c>(perm_X[col], perm_Y[row])</c>
/// combo has zero zoombinis in the pool, and then wipes the permutation
/// from memory. We recover compatible permutations by:
///
/// <list type="number">
///   <item>Counting how many pool zoombinis fall into each (X, Y)
///   attribute combo (0..N per cell).</item>
///   <item>Enumerating all 5! × 5! = 14400 permutation pairs and keeping
///   the ones where every boarded cell maps to a 0-count combo and every
///   filled <see cref="HotelState.ConstraintX"/>/<see cref="HotelState.ConstraintY"/>
///   entry agrees with the candidate.</item>
/// </list>
///
/// <para>From a fresh-init state (no placements yet) the boarded cells
/// alone usually leave dozens of candidates. After one or two successful
/// placements the live constraints almost always collapse the set to a
/// single permutation, at which point every remaining zoombini has
/// exactly one correct cell.</para>
///
/// <para>Every surviving candidate is a complete valid placement plan —
/// following any of them places all 16 zoombinis without needing a
/// boarded cell. The renderer just commits to the first one and tells
/// the player where the held zoombini belongs under that plan.</para>
/// </summary>
public sealed class HotelSolver
{
    public readonly record struct PoolZb(byte Hair, byte Eyes, byte Nose, byte Feet);

    /// <summary>One viable assignment of attribute values to grid columns
    /// and rows. <c>PermX[col]</c> = the X-axis attribute value (1..5)
    /// that lives in that column; <c>PermY[row]</c> the Y-axis value
    /// for that row.</summary>
    public readonly record struct Permutation(byte[] PermX, byte[] PermY);

    public sealed class Result
    {
        /// <summary>All permutations consistent with boarded cells + live
        /// constraints. Empty when the puzzle isn't a Diff-3 grid or when
        /// existing placements have made the puzzle unsolvable.</summary>
        public IReadOnlyList<Permutation> Candidates { get; init; } = Array.Empty<Permutation>();
    }

    private static readonly byte[][] Perms5 = BuildPerms();

    public static Result Solve(HotelState state, IReadOnlyList<PoolZb> pool)
    {
        if (state.Difficulty != 3 || state.AxisX == 0 || state.AxisY == 0)
            return new Result();

        var count = ComputePoolCount(pool, state.AxisX, state.AxisY);
        return new Result { Candidates = EnumerateCandidates(state, count) };
    }

    private static int[,] ComputePoolCount(IReadOnlyList<PoolZb> pool, byte axisX, byte axisY)
    {
        // Index 0 unused; valid attribute values are 1..5.
        var count = new int[6, 6];
        foreach (var z in pool)
        {
            byte x = AttrOf(z, axisX);
            byte y = AttrOf(z, axisY);
            if (x is >= 1 and <= 5 && y is >= 1 and <= 5)
                count[x, y]++;
        }
        return count;
    }

    private static List<Permutation> EnumerateCandidates(HotelState state, int[,] count)
    {
        var liveX = ExtractPerColumn(state.ConstraintX);
        var liveY = ExtractPerRow(state.ConstraintY);
        var boarded = state.Boarded;

        var result = new List<Permutation>();
        foreach (var px in Perms5)
        {
            if (!MatchesLive(px, liveX)) continue;
            foreach (var py in Perms5)
            {
                if (!MatchesLive(py, liveY)) continue;
                if (!AllBoardedAreEmpty(px, py, boarded, count)) continue;
                result.Add(new Permutation((byte[])px.Clone(), (byte[])py.Clone()));
            }
        }
        return result;
    }

    /// <summary>Collapse the per-room ConstraintX array to a per-column
    /// X-value array. The engine indexes rooms column-major
    /// (room <c>r</c> sits in column <c>r / 5</c>, row <c>r % 5</c>) — see
    /// <see cref="HotelState"/> for verification. Returns 0 for columns
    /// no zoombini has hit yet.</summary>
    private static byte[] ExtractPerColumn(IReadOnlyList<byte> constraintX)
    {
        var perCol = new byte[5];
        for (int r = 0; r < 25; r++)
        {
            byte v = constraintX[r];
            if (v is >= 1 and <= 5) perCol[r / 5] = v;
        }
        return perCol;
    }

    /// <summary>Collapse the per-room ConstraintY array to a per-row
    /// Y-value array. Column-major addressing → row is <c>r % 5</c>.</summary>
    private static byte[] ExtractPerRow(IReadOnlyList<byte> constraintY)
    {
        var perRow = new byte[5];
        for (int r = 0; r < 25; r++)
        {
            byte v = constraintY[r];
            if (v is >= 1 and <= 5) perRow[r % 5] = v;
        }
        return perRow;
    }

    private static bool MatchesLive(byte[] candidate, byte[] live)
    {
        for (int i = 0; i < 5; i++)
            if (live[i] != 0 && candidate[i] != live[i])
                return false;
        return true;
    }

    private static bool AllBoardedAreEmpty(byte[] px, byte[] py,
                                           IReadOnlyList<HotelState.BoardedCell> boarded,
                                           int[,] count)
    {
        foreach (var b in boarded)
            if (count[px[b.Column], py[b.Row]] != 0)
                return false;
        return true;
    }

    private static byte AttrOf(PoolZb z, byte axisAttrId) => axisAttrId switch
    {
        1 => z.Hair, 2 => z.Eyes, 3 => z.Nose, 4 => z.Feet,
        _ => (byte)0,
    };

    private static byte[][] BuildPerms()
    {
        var result = new List<byte[]>(120);
        Permute(new byte[] { 1, 2, 3, 4, 5 }, 0, result);
        return result.ToArray();
    }

    private static void Permute(byte[] a, int k, List<byte[]> sink)
    {
        if (k == a.Length - 1) { sink.Add((byte[])a.Clone()); return; }
        for (int i = k; i < a.Length; i++)
        {
            (a[k], a[i]) = (a[i], a[k]);
            Permute(a, k + 1, sink);
            (a[k], a[i]) = (a[i], a[k]);
        }
    }
}
