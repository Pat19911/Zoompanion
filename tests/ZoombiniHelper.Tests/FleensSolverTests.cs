using ZoombiniHelper;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Tests for the constraint-propagation Fleens solver. Strategy: build
/// fleens by applying a known permutation to a zoombini list, then check
/// the solver recovers the same permutation (or one equivalent under
/// pool symmetry).
/// </summary>
public class FleensSolverTests
{
    [Fact]
    public void Solver_IdentityPermutation_RecoversCleanly()
    {
        var zb = SamplePool();
        var perm = MakePermutation(
            typeMap: new byte[] { 0, 1, 2, 3 },
            valueMap0: new byte[] { 1, 2, 3, 4, 5 },
            valueMap1: new byte[] { 1, 2, 3, 4, 5 },
            valueMap2: new byte[] { 1, 2, 3, 4, 5 },
            valueMap3: new byte[] { 1, 2, 3, 4, 5 });
        var fleens = ApplyToBuildFleens(zb, perm);

        var found = FleensSolver.SolveAll(zb, fleens);
        Assert.Contains(found, f => MatchesOnPool(f, perm, zb));
    }

    [Fact]
    public void Solver_TypeShuffleOnly_Recovers()
    {
        var zb = SamplePool();
        // Hair → Nose-slot, Eyes → Feet-slot, Nose → Hair-slot, Feet → Eyes-slot
        var perm = MakePermutation(
            typeMap: new byte[] { 2, 3, 0, 1 },
            valueMap0: new byte[] { 1, 2, 3, 4, 5 },
            valueMap1: new byte[] { 1, 2, 3, 4, 5 },
            valueMap2: new byte[] { 1, 2, 3, 4, 5 },
            valueMap3: new byte[] { 1, 2, 3, 4, 5 });
        var fleens = ApplyToBuildFleens(zb, perm);

        var found = FleensSolver.SolveAll(zb, fleens);
        Assert.Contains(found, f => MatchesOnPool(f, perm, zb));
    }

    [Fact]
    public void Solver_ArbitraryBijection_NotJustCyclic_Recovers()
    {
        // The whole point of the rewrite: handle non-cyclic value maps.
        // This map for slot 0 is {1→3, 2→1, 3→5, 4→4, 5→2} — not any cyclic shift.
        var zb = SamplePool();
        var perm = MakePermutation(
            typeMap: new byte[] { 0, 1, 2, 3 },
            valueMap0: new byte[] { 3, 1, 5, 4, 2 },
            valueMap1: new byte[] { 2, 4, 1, 5, 3 },
            valueMap2: new byte[] { 5, 3, 1, 4, 2 },
            valueMap3: new byte[] { 4, 5, 3, 1, 2 });
        var fleens = ApplyToBuildFleens(zb, perm);

        var found = FleensSolver.SolveAll(zb, fleens);
        Assert.NotEmpty(found);
        Assert.Contains(found, f => MatchesOnPool(f, perm, zb));
    }

    [Fact]
    public void Solver_EmptyInputs_ReturnsEmpty()
    {
        Assert.Empty(FleensSolver.SolveAll(Array.Empty<PoolMember>(), Array.Empty<FleenMember>()));
    }

    [Fact]
    public void Solver_LiveDump_195558_ProducesAtLeastOneSolution()
    {
        // Real data from memdump-195558 (Schwierigkeit 4, mid-round).
        // Distributions per attribute match — the solver must find a permutation.
        var zb = LiveDump195558Pool();
        var fleens = LiveDump195558Fleens();
        var found = FleensSolver.SolveAll(zb, fleens);
        Assert.NotEmpty(found);
    }

    // --- helpers ---

    /// <summary>Same-shape ValueMap helpers: index 0 unused so [1..5]
    /// reads naturally in test code.</summary>
    private static FleensPermutation MakePermutation(
        byte[] typeMap, byte[] valueMap0, byte[] valueMap1, byte[] valueMap2, byte[] valueMap3)
    {
        var vm = new byte[4][];
        vm[0] = ToFullValueMap(valueMap0);
        vm[1] = ToFullValueMap(valueMap1);
        vm[2] = ToFullValueMap(valueMap2);
        vm[3] = ToFullValueMap(valueMap3);
        return new FleensPermutation(typeMap, vm);
    }

    private static byte[] ToFullValueMap(byte[] mapping1To5)
    {
        // Pad to length 6 with index 0 unused.
        var full = new byte[6];
        Array.Copy(mapping1To5, 0, full, 1, 5);
        return full;
    }

    /// <summary>Two permutations are equivalent on a pool if every zoombini
    /// produces the same fleen attrs under both. Different permutations can
    /// satisfy the data when the pool is symmetric (the solver returns
    /// every such option).</summary>
    private static bool MatchesOnPool(FleensPermutation actual, FleensPermutation expected, IReadOnlyList<PoolMember> pool)
    {
        foreach (var z in pool)
        {
            var a = actual.Apply(z.Hair, z.Eyes, z.Nose, z.Feet);
            var e = expected.Apply(z.Hair, z.Eyes, z.Nose, z.Feet);
            for (int i = 0; i < 4; i++) if (a[i] != e[i]) return false;
        }
        return true;
    }

    private static PoolMember[] SamplePool() => new[]
    {
        MakeZb(2, 4, 2, 2), MakeZb(3, 3, 4, 2), MakeZb(1, 5, 2, 1), MakeZb(4, 2, 5, 2),
        MakeZb(2, 4, 1, 5), MakeZb(1, 1, 2, 4), MakeZb(3, 4, 5, 1), MakeZb(4, 4, 3, 4),
        MakeZb(4, 1, 3, 2), MakeZb(3, 4, 1, 5), MakeZb(2, 2, 1, 4), MakeZb(4, 3, 4, 5),
        MakeZb(3, 5, 4, 3), MakeZb(1, 2, 1, 3), MakeZb(5, 1, 4, 3), MakeZb(4, 2, 3, 5),
    };

    private static PoolMember MakeZb(byte h, byte e, byte n, byte f)
        => new PoolMember(0, h, e, n, f, YPosition: 0, SpriteId: 0);

    private static FleenMember[] ApplyToBuildFleens(IReadOnlyList<PoolMember> zb, FleensPermutation perm)
    {
        var result = new FleenMember[zb.Count];
        for (int i = 0; i < zb.Count; i++)
        {
            var v = perm.Apply(zb[i].Hair, zb[i].Eyes, zb[i].Nose, zb[i].Feet);
            result[i] = new FleenMember(0, v[0], v[1], v[2], v[3], YPosition: (ushort)i, SpriteId: 0, TreeMarker: 0);
        }
        return result;
    }

    /// <summary>Live data from memdump-195558.txt (16 zoombinis).</summary>
    private static PoolMember[] LiveDump195558Pool() => new[]
    {
        MakeZb(4,1,3,5), MakeZb(5,2,2,4), MakeZb(2,5,4,4), MakeZb(4,5,2,1),
        MakeZb(2,1,2,2), MakeZb(1,2,2,3), MakeZb(3,1,4,2), MakeZb(2,1,3,3),
        MakeZb(2,4,1,2), MakeZb(5,4,5,1), MakeZb(2,5,1,4), MakeZb(5,1,1,1),
        MakeZb(2,4,5,2), MakeZb(1,3,2,5), MakeZb(2,1,4,4), MakeZb(3,3,5,3),
    };

    /// <summary>Live data from memdump-195558.txt (16 fleens).</summary>
    private static FleenMember[] LiveDump195558Fleens() => new[]
    {
        MakeFl(1,3,4,2), MakeFl(3,2,2,4), MakeFl(5,5,2,3), MakeFl(3,3,3,3),
        MakeFl(1,1,5,5), MakeFl(2,5,5,1), MakeFl(4,4,2,2), MakeFl(1,3,2,5),
        MakeFl(1,2,4,2), MakeFl(1,1,1,5), MakeFl(2,3,4,5), MakeFl(1,3,3,1),
        MakeFl(1,2,1,2), MakeFl(4,1,5,4), MakeFl(5,4,2,1), MakeFl(4,3,1,4),
    };

    private static FleenMember MakeFl(byte a0, byte a1, byte a2, byte a3)
        => new FleenMember(0, a0, a1, a2, a3, YPosition: 0, SpriteId: 0, TreeMarker: 0);
}
