using ZoombiniHelper;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Tests the Stone Rise constraint solver against a verified live fixture
/// (Diff-4 puzzle from memdump-133954). The Python prototype on the same
/// data found exactly 2 valid assignments, both placing the user's actual
/// drops at tile 38 and tile 55. The C# port must match.
/// </summary>
public class StoneRiseSolverTests
{
    // 16-zoombini pool from the live Diff-4 dump.
    private static readonly StoneRiseSolver.PoolZb[] FixturePool = new StoneRiseSolver.PoolZb[]
    {
        new(2,3,4,4), new(5,5,4,4), new(3,4,5,5), new(3,3,5,4),
        new(4,5,5,4), new(2,2,3,2), new(1,5,2,5), new(5,5,3,4),
        new(3,2,1,5), new(1,3,4,5), new(5,1,5,1), new(2,4,4,5),
        new(1,5,4,3), new(5,1,4,2), new(3,5,3,1), new(2,5,1,2),
    };

    // 16 pair-slot tile indices from the dump.
    private static readonly int[] FixtureSlots =
        { 6, 23, 25, 38, 40, 42, 55, 57, 59, 61, 74, 76, 78, 95, 97, 114 };

    // 21 connectors decoded from the dump's tile array. Each entry:
    // (connector tile index, attribute id 1..4, slot tile A, slot tile B).
    // Verified via the Python prototype that solved this exact graph.
    private static readonly (int tile, byte attr, int a, int b)[] FixtureConnectors =
    {
        (14, 2, 23, 6),     // Eyes
        (31, 3, 40, 23),    // Nose
        (32, 2, 23, 42),    // Eyes
        (33, 1, 42, 25),    // Hair
        (46, 2, 55, 38),    // Eyes
        (47, 2, 38, 57),    // Eyes
        (48, 3, 57, 40),    // Nose
        (49, 3, 40, 59),    // Nose
        (50, 3, 59, 42),    // Nose
        (51, 4, 42, 61),    // Feet
        (56, 2, 55, 57),    // Eyes
        (58, 2, 57, 59),    // Eyes
        (60, 2, 59, 61),    // Eyes
        (64, 3, 55, 74),    // Nose
        (65, 4, 74, 57),    // Feet
        (66, 2, 57, 76),    // Eyes
        (67, 1, 76, 59),    // Hair
        (68, 3, 59, 78),    // Nose
        (85, 4, 76, 95),    // Feet
        (87, 2, 78, 97),    // Eyes
        (104, 4, 95, 114),  // Feet
    };

    [Fact]
    public void Solve_Diff4LiveFixture_FindsExactlyTwoSolutions()
    {
        var state = MakeFixtureState();
        var result = StoneRiseSolver.Solve(state, FixturePool);
        Assert.False(result.HitCap);
        Assert.Equal(2, result.SolutionCount);
        Assert.Equal(2, result.Solutions.Count);
    }

    [Fact]
    public void Solve_Diff4LiveFixture_BothSolutionsPlaceUsersActualPicksCorrectly()
    {
        // The user actually placed (3,5,3,1) at tile 55 and (2,5,1,2) at tile 38
        // and the engine accepted them — so both must appear in every solution.
        var state = MakeFixtureState();
        var result = StoneRiseSolver.Solve(state, FixturePool);

        int idxA = Array.FindIndex(FixturePool, z => z == new StoneRiseSolver.PoolZb(3, 5, 3, 1));
        int idxB = Array.FindIndex(FixturePool, z => z == new StoneRiseSolver.PoolZb(2, 5, 1, 2));

        foreach (var sol in result.Solutions)
        {
            Assert.Equal(idxA, sol.SlotTileToZbIndex[55]);
            Assert.Equal(idxB, sol.SlotTileToZbIndex[38]);
        }
    }

    [Fact]
    public void Solve_Diff4LiveFixture_EveryConnectorConstraintSatisfiedInFirstSolution()
    {
        var state = MakeFixtureState();
        var result = StoneRiseSolver.Solve(state, FixturePool);
        Assert.NotEmpty(result.Solutions);

        var plan = result.Solutions[0];
        foreach (var (_, attr, a, b) in FixtureConnectors)
        {
            var zbA = AsArray(FixturePool[plan.SlotTileToZbIndex[a]]);
            var zbB = AsArray(FixturePool[plan.SlotTileToZbIndex[b]]);
            Assert.Equal(zbA[attr - 1], zbB[attr - 1]);
        }
    }

    [Fact]
    public void Solve_Diff4LiveFixture_EachZoombiniUsedExactlyOnce()
    {
        var state = MakeFixtureState();
        var result = StoneRiseSolver.Solve(state, FixturePool);
        Assert.NotEmpty(result.Solutions);

        var plan = result.Solutions[0];
        var used = plan.SlotTileToZbIndex.Values.ToList();
        Assert.Equal(FixturePool.Length, used.Count);
        Assert.Equal(FixturePool.Length, used.Distinct().Count());
    }

    [Fact]
    public void Solve_InactiveState_ReturnsEmpty()
    {
        var empty = new StoneRiseState(difficulty: 1,
            slots: Array.Empty<StoneRiseState.PairSlot>(),
            connectors: Array.Empty<StoneRiseState.Connector>());
        var result = StoneRiseSolver.Solve(empty, FixturePool);
        Assert.Equal(0, result.SolutionCount);
    }

    private static StoneRiseState MakeFixtureState()
    {
        var slots = FixtureSlots.Select(t => new StoneRiseState.PairSlot(t, IsFilled: false)).ToList();
        var connectors = FixtureConnectors
            .Select(c => new StoneRiseState.Connector(c.tile, IsFilled: false,
                                                     AttributeId: c.attr,
                                                     PairTileA: c.a, PairTileB: c.b))
            .ToList();
        return new StoneRiseState(difficulty: 4, slots, connectors);
    }

    private static byte[] AsArray(StoneRiseSolver.PoolZb z) => new[] { z.Hair, z.Eyes, z.Nose, z.Feet };
}
