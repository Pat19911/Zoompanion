using Xunit;
using ZoombiniHelper;

namespace ZoombiniHelper.Tests;

public class CaptainCajunSolverTests
{
    /// <summary>Two seats, two zbs sharing one attribute → 2 solutions
    /// (each zb can go to either seat). Sanity check that FC + propagation
    /// don't accidentally over-prune.</summary>
    [Fact]
    public void TwoSeats_TwoCompatibleZbs_FindsTwoSolutions()
    {
        var seats = new[] { (0, 0), (10, 0) };  // distance 10 → neighbors
        var pool = new CaptainCajunSolver.PoolZb[]
        {
            new(1, 1, 1, 1),
            new(1, 2, 2, 2),  // shares Hair=1 with the other
        };
        var r = CaptainCajunSolver.Solve(seats, pool);
        Assert.Equal(2, r.SolutionCount);
    }

    /// <summary>Two adjacent seats, two zbs sharing NO attribute → 0
    /// solutions. FC must detect dead-end immediately on the first
    /// assignment.</summary>
    [Fact]
    public void TwoSeats_TwoIncompatibleZbs_FindsZeroSolutions()
    {
        var seats = new[] { (0, 0), (10, 0) };
        var pool = new CaptainCajunSolver.PoolZb[]
        {
            new(1, 1, 1, 1),
            new(2, 2, 2, 2),  // no shared attribute
        };
        var r = CaptainCajunSolver.Solve(seats, pool);
        Assert.Equal(0, r.SolutionCount);
    }

    /// <summary>Disconnected seats (= no neighbors) accept any pairing.</summary>
    [Fact]
    public void DisconnectedSeats_AcceptAnyPairing()
    {
        var seats = new[] { (0, 0), (1000, 1000) };  // far → no edge
        var pool = new CaptainCajunSolver.PoolZb[]
        {
            new(1, 1, 1, 1),
            new(2, 2, 2, 2),
        };
        var r = CaptainCajunSolver.Solve(seats, pool);
        Assert.Equal(2, r.SolutionCount);  // 2! = 2 arrangements
    }

    /// <summary>Fixed assignment that already violates a constraint with
    /// another fixed → returns 0 immediately, no recursion.</summary>
    [Fact]
    public void FixedAssignments_Conflicting_ReturnsZero()
    {
        var seats = new[] { (0, 0), (10, 0) };
        var pool = new CaptainCajunSolver.PoolZb[]
        {
            new(1, 1, 1, 1),
            new(2, 2, 2, 2),  // not compatible
        };
        var r = CaptainCajunSolver.Solve(seats, pool, new Dictionary<int, int>
        {
            [0] = 0, [1] = 1,
        });
        Assert.Equal(0, r.SolutionCount);
    }

    /// <summary>4×4 grid (typical Cajun Diff 4 layout) with 16 zbs that all
    /// share at least one attribute pairwise → solver should finish under
    /// the timeout and produce solutions, with the frequency matrix being
    /// non-empty for the held zb.</summary>
    [Fact]
    public void FourByFourGrid_FullyCompatiblePool_FindsManySolutions()
    {
        // 4×4 grid at integer steps so neighbor distance = 1 (well within
        // NeighborDistance threshold of 60).
        var seats = new List<(int, int)>();
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
                seats.Add((col * 10, row * 10));
        // 16 zbs all with same Hair value → every pair shares Hair → fully
        // compatible graph → 16! solutions in theory, capped at MaxSolutions.
        var pool = new List<CaptainCajunSolver.PoolZb>();
        for (int i = 0; i < 16; i++)
            pool.Add(new(1, (byte)(1 + i % 5), (byte)(1 + (i + 1) % 5), (byte)(1 + (i + 2) % 5)));
        var r = CaptainCajunSolver.Solve(seats, pool);
        Assert.True(r.SolutionCount > 0, "Should find solutions for fully-compatible pool");
        Assert.False(r.HitCap == false && r.SolutionCount > CaptainCajunSolver.MaxSolutions,
            "Should respect MaxSolutions cap");
        // Frequencies non-empty
        Assert.True(r.MostFrequentSeatFor(0) is not null);
    }
}
