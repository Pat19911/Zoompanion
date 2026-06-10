using ZoombiniHelper;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Regression tests for the held-zb double-count bug. While dragging, the
/// engine sets the held zb's handle to 0x04001001, which PoolScanner.Scan now
/// returns (it's in ZoombiniHandle.All). The renderer used to append the held
/// zb on top of that, giving the solver N+1 entries for N slots — letting the
/// perfect-matching solver place the held zb's two copies into two slots and
/// strand a real zoombini, so its recommended move dead-ended. The builder
/// must drop the scanned copy and keep exactly one entry per zoombini.
/// </summary>
public class StoneRisePoolBuilderTests
{
    private static PoolMember Zb(nint addr, byte h, byte e, byte n, byte f)
        => new(addr, h, e, n, f, YPosition: 0, SpriteId: 0);

    [Fact]
    public void Build_HeldZbIsAlreadyInScan_NotCountedTwice()
    {
        // Three physical zbs in the scan; the third (addr 30) is the one held.
        var scanned = new List<PoolMember>
        {
            Zb(10, 1, 1, 1, 1),
            Zb(20, 2, 2, 2, 2),
            Zb(30, 3, 3, 3, 3),
        };
        var held = scanned[2];

        var r = StoneRisePoolBuilder.Build(scanned, held);

        // The scanned copy of the held zb is gone from the base members…
        Assert.Equal(2, r.BaseMembers.Count);
        Assert.DoesNotContain(r.BaseMembers, p => p.Address == 30);
        // …and the held zb is re-added exactly once → still 3 total, not 4.
        Assert.Equal(3, r.SolverPool.Count);
        Assert.Equal(2, r.HeldPoolIndex);
        Assert.Equal(new StoneRiseSolver.PoolZb(3, 3, 3, 3), r.SolverPool[r.HeldPoolIndex]);
    }

    [Fact]
    public void Build_NothingHeld_ReturnsScanUnchanged()
    {
        var scanned = new List<PoolMember> { Zb(10, 1, 1, 1, 1), Zb(20, 2, 2, 2, 2) };

        var r = StoneRisePoolBuilder.Build(scanned, held: null);

        Assert.Equal(2, r.BaseMembers.Count);
        Assert.Equal(2, r.SolverPool.Count);
        Assert.Equal(-1, r.HeldPoolIndex);
    }

    [Fact]
    public void Build_HeldZbMatchedByAddressNotAttributes()
    {
        // Two zbs share identical attributes but have distinct node addresses.
        // Only the one actually held (by address) must be removed from the base.
        var scanned = new List<PoolMember> { Zb(10, 4, 4, 4, 4), Zb(20, 4, 4, 4, 4) };
        var held = scanned[0];

        var r = StoneRisePoolBuilder.Build(scanned, held);

        Assert.Single(r.BaseMembers);
        Assert.Equal((nint)20, r.BaseMembers[0].Address);
        Assert.Equal(2, r.SolverPool.Count); // one base + held re-added
    }

    [Fact]
    public void Build_SolverPoolCountEqualsZbCount_EnablesPerfectMatching()
    {
        // The whole point: with N physical zbs (one held) the solver pool is N,
        // matching N slots — so the solver can't strand a zb on a phantom copy.
        var scanned = new List<PoolMember>
        {
            Zb(10, 1, 1, 1, 1), Zb(20, 2, 2, 2, 2),
            Zb(30, 3, 3, 3, 3), Zb(40, 4, 4, 4, 4),
        };
        var held = scanned[1];

        var r = StoneRisePoolBuilder.Build(scanned, held);

        Assert.Equal(scanned.Count, r.SolverPool.Count);
    }
}
