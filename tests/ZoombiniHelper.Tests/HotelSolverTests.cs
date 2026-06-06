using ZoombiniHelper;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Tests for the Hotel Dimensia placement solver. The solver is the
/// critical helper for Diff 3 — if it returns a wrong candidate the
/// player can dig themselves into an unsolvable corner because the
/// engine commits each column/row's meaning to whatever lands in it
/// first.
/// </summary>
public class HotelSolverTests
{
    // The Diff-3 dump (memdump-101845) gave us a known-good fixture:
    // axisX = Feet, axisY = Nose, with this 16-zoombini pool and 8
    // boarded cells. Used as the baseline for several tests below.
    private static readonly (byte H, byte E, byte N, byte F)[] FixturePool = new (byte, byte, byte, byte)[]
    {
        (3,1,5,2), (2,5,2,2), (1,4,5,2), (5,5,5,1), (3,3,2,3), (1,3,2,1),
        (3,5,5,2), (3,1,4,5), (4,4,1,3), (1,2,1,4), (4,5,3,1), (4,1,3,1),
        (2,3,4,2), (5,3,1,4), (3,2,1,5), (1,3,4,2),
    };

    // Boarded cells from the original Diff-3 fixture dump (memdump-101845)
    // decoded with column-major addressing (Index = Column * 5 + Row),
    // which matches what the engine actually uses (verified 2026-04-29
    // against the user's visual report on memdump-113414).
    private static readonly HotelState.BoardedCell[] FixtureBoarded = new HotelState.BoardedCell[]
    {
        new(Index: 2,  Row: 2, Column: 0),
        new(Index: 7,  Row: 2, Column: 1),
        new(Index: 8,  Row: 3, Column: 1),
        new(Index: 9,  Row: 4, Column: 1),
        new(Index: 10, Row: 0, Column: 2),
        new(Index: 13, Row: 3, Column: 2),
        new(Index: 15, Row: 0, Column: 3),
        new(Index: 16, Row: 1, Column: 3),
    };

    [Fact]
    public void Solve_NotDifficulty3_ReturnsEmpty()
    {
        var state = MakeState(difficulty: 1, axisX: 4, axisY: 0);
        var result = HotelSolver.Solve(state, ToPool(FixturePool));
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Solve_FreshDiff3_FindsAllCandidatesConsistentWithBoarded()
    {
        var state = MakeState(difficulty: 3, axisX: 4, axisY: 3, boarded: FixtureBoarded);
        var result = HotelSolver.Solve(state, ToPool(FixturePool));

        // 46 was observed empirically; what really matters is that there
        // are some, and that every one places all 16 zoombinis in a
        // non-boarded cell.
        Assert.NotEmpty(result.Candidates);
        AssertEveryCandidateIsCompletePlan(result, FixturePool, axisX: 4, axisY: 3, FixtureBoarded);
    }

    [Fact]
    public void Solve_LiveColumnConstraint_PinsThatColumnAcrossCandidates()
    {
        // Pretend the player dropped a Feet=2 zoombini somewhere in column 3
        // (any of the 5 rooms in column 3 — engine writes the same value
        // to every room of that column). Solver should keep only candidates
        // where perm_X[3] == 2. Column-major indexing: room in col 3 = 3*5+row.
        var cx = MakeConstraint(roomIndex: 3 * 5 + 0, value: 2);
        var state = MakeState(difficulty: 3, axisX: 4, axisY: 3,
                              boarded: FixtureBoarded, constraintX: cx);

        var result = HotelSolver.Solve(state, ToPool(FixturePool));
        Assert.NotEmpty(result.Candidates);
        Assert.All(result.Candidates, p => Assert.Equal(2, p.PermX[3]));
    }

    [Fact]
    public void Solve_LiveRowConstraint_PinsThatRowAcrossCandidates()
    {
        // Regression: row-extraction must use `r % 5` for column-major
        // addressing. Drop a Nose=4 zoombini in row 2 (column 0, room
        // index = 0*5 + 2 = 2 — but cell (col 0, row 2) is boarded in
        // the fixture, so use column 4 row 2 instead = 4*5 + 2 = 22).
        var cy = MakeConstraint(roomIndex: 4 * 5 + 2, value: 4);
        var state = MakeState(difficulty: 3, axisX: 4, axisY: 3,
                              boarded: FixtureBoarded, constraintY: cy);

        var result = HotelSolver.Solve(state, ToPool(FixturePool));
        Assert.NotEmpty(result.Candidates);
        Assert.All(result.Candidates, p => Assert.Equal(4, p.PermY[2]));
    }

    [Fact]
    public void Solve_LiveConstraintsBothAxes_CollapsesToFewCandidates()
    {
        // Two placements should narrow the candidate set substantially.
        // Drop a zb in (col=0, row=0) — engine writes constraintX[0]=Feet
        // and constraintY[0]=Nose. Column-major: room index 0 = col 0, row 0.
        var cx = MakeConstraint(roomIndex: 0, value: 3);     // col 0 = Feet=3
        var cy = MakeConstraint(roomIndex: 0, value: 5);     // row 0 = Nose=5
        var state = MakeState(difficulty: 3, axisX: 4, axisY: 3,
                              boarded: FixtureBoarded, constraintX: cx, constraintY: cy);

        var result = HotelSolver.Solve(state, ToPool(FixturePool));

        Assert.All(result.Candidates, p =>
        {
            Assert.Equal(3, p.PermX[0]);
            Assert.Equal(5, p.PermY[0]);
        });
        Assert.True(result.Candidates.Count < 10,
            $"Expected ≤10 candidates after 1 placement, got {result.Candidates.Count}");
    }

    [Fact]
    public void Solve_InconsistentLiveConstraint_YieldsNoCandidates()
    {
        // Engine boards (col=0, row=2). Claim col=0 means Feet=4 and
        // row=2 means Nose=1. Pool has 2 zbs with (Feet=4, Nose=1), so
        // boarded cell would have count > 0 → impossible.
        // Column-major: room (col=0, row=0) = idx 0; (col=0, row=2) = idx 2.
        var cx = MakeConstraint(roomIndex: 0, value: 4);
        var cy = MakeConstraint(roomIndex: 2, value: 1);
        var state = MakeState(difficulty: 3, axisX: 4, axisY: 3,
                              boarded: FixtureBoarded, constraintX: cx, constraintY: cy);

        var result = HotelSolver.Solve(state, ToPool(FixturePool));
        Assert.Empty(result.Candidates);
    }

    // --- helpers ---

    private static HotelState MakeState(int difficulty, byte axisX, byte axisY = 0, byte axisZ = 0,
                                         IReadOnlyList<HotelState.BoardedCell>? boarded = null,
                                         byte[]? constraintX = null, byte[]? constraintY = null)
        => new HotelState(
            difficulty: difficulty,
            numRooms: 25,
            axisX: axisX, axisY: axisY, axisZ: axisZ,
            boarded: boarded ?? Array.Empty<HotelState.BoardedCell>(),
            cx: constraintX ?? new byte[25],
            cy: constraintY ?? new byte[25]);

    private static byte[] MakeConstraint(int roomIndex, byte value)
    {
        var arr = new byte[25];
        arr[roomIndex] = value;
        return arr;
    }

    private static List<HotelSolver.PoolZb> ToPool((byte, byte, byte, byte)[] tuples)
        => tuples.Select(t => new HotelSolver.PoolZb(t.Item1, t.Item2, t.Item3, t.Item4)).ToList();

    /// <summary>For every surviving candidate, verify that every zoombini
    /// in the pool maps to a non-boarded cell. This is the correctness
    /// invariant the solver promises — every candidate is a complete plan
    /// where all 16 zoombinis fit.</summary>
    private static void AssertEveryCandidateIsCompletePlan(
        HotelSolver.Result result,
        (byte H, byte E, byte N, byte F)[] pool,
        byte axisX, byte axisY,
        IReadOnlyList<HotelState.BoardedCell> boarded)
    {
        var boardedSet = boarded.Select(b => (b.Column, b.Row)).ToHashSet();
        foreach (var perm in result.Candidates)
        {
            foreach (var zb in pool)
            {
                byte x = AttrAt(zb, axisX);
                byte y = AttrAt(zb, axisY);
                int col = Array.IndexOf(perm.PermX, x);
                int row = Array.IndexOf(perm.PermY, y);
                Assert.True(col >= 0 && row >= 0,
                    $"Pool zb {zb} has axis values not present in the permutation");
                Assert.False(boardedSet.Contains((col, row)),
                    $"Candidate would put zb {zb} in boarded cell ({col},{row})");
            }
        }
    }

    private static byte AttrAt((byte H, byte E, byte N, byte F) zb, byte axis) => axis switch
    {
        1 => zb.H, 2 => zb.E, 3 => zb.N, 4 => zb.F,
        _ => (byte)0,
    };
}
