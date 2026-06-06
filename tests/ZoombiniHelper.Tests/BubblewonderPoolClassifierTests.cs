using ZoombiniHelper;
using ZoombiniHelper.Bubblewonder;

namespace ZoombiniHelper.Tests;

public class BubblewonderPoolClassifierTests
{
    /// <summary>Minimal-Fake: liefert nur die Zelltyp-Tabelle bei ihrer VA.</summary>
    private sealed class CellTableMem : IMemoryReader
    {
        private readonly byte[] _table;
        public CellTableMem(byte[] table) => _table = table;
        public byte[]? ReadBytes(nint va, int n) =>
            va == BubblewonderMemoryMap.CellTypeTable
                ? _table[..System.Math.Min(n, _table.Length)]
                : new byte[n];
        public ushort ReadWord(nint va) => 0;
        public byte ReadByte(nint va) => 0;
    }

    private static byte[] TableWith(params (int row, int col, int type)[] cells)
    {
        var t = new byte[12 * 13 * 2];
        foreach (var (row, col, type) in cells)
        {
            int pos = row * 13 + col;
            t[pos * 2] = (byte)(type & 0xFF);
            t[pos * 2 + 1] = (byte)((type >> 8) & 0xFF);
        }
        return t;
    }

    private static PoolMember Zb(ushort id, uint handle, int row = 0xFFFF, int col = 0xFFFF,
        ushort outcome = 0xFFFF) =>
        new(Address: id, Hair: 1, Eyes: 1, Nose: 1, Feet: 1, YPosition: 0, SpriteId: 0,
            HeaderId: id, Handle: handle, GridRow: (ushort)row, GridCol: (ushort)col,
            OutcomeType: outcome);

    [Fact]
    public void Split_TrappedZb_IsNeitherPoolNorIsland()
    {
        // (10,1) = 0x15 Zwischenstation; (6,6) = 1 Trap.
        var mem = new CellTableMem(TableWith((10, 1, 0x15), (6, 6, 1)));
        var pool = new[]
        {
            Zb(0x05, 0x00000001),                  // im Pool
            Zb(0x06, 0x00000001),                  // im Pool
            Zb(0x11, 0x04008001, row: 6,  col: 6), // in Falle (6,6) — der gemeldete Bug
            Zb(0x0D, 0x04008001, row: 10, col: 1), // auf Zwischenstation — echte Insel
        };

        var (main, island) = BubblewonderPoolClassifier.Split(pool, mem);

        Assert.Equal(new ushort[] { 0x05, 0x06 },
            main.Select(p => p.HeaderId).OrderBy(x => x).ToArray());
        Assert.Equal(new ushort[] { 0x0D }, island.Select(p => p.HeaderId).ToArray());
        // Der gefangene ZB taucht NIRGENDS auf (nicht „auf Insel", nicht im Pool).
        Assert.DoesNotContain(main, p => p.HeaderId == 0x11);
        Assert.DoesNotContain(island, p => p.HeaderId == 0x11);
    }

    [Fact]
    public void Split_ParkedZb_RestStateHandle_CountsAsIsland()
    {
        // Bug 2026-05-30 (memdump-073530): ein gerade auf die Insel verfrachteter ZB
        // kommt zur Ruhe → Handle 0x00008001 (0x04008001 ohne das 0x04000000-Bit).
        // Er steht auf (1,2)=0x15 und MUSS als Insel zählen, nicht erneut zum
        // Hochheben empfohlen werden.
        var mem = new CellTableMem(TableWith((1, 2, 0x15)));
        var pool = new[]
        {
            Zb(0x05, 0x00000001),                  // im Pool
            Zb(0x0D, 0x00008001, row: 1, col: 2),  // geparkt auf Zwischenstation
        };

        var (main, island) = BubblewonderPoolClassifier.Split(pool, mem);

        Assert.Equal(new ushort[] { 0x05 }, main.Select(p => p.HeaderId).ToArray());
        Assert.Equal(new ushort[] { 0x0D }, island.Select(p => p.HeaderId).ToArray());
    }

    [Fact]
    public void Split_Outcome76_IsIsland_EvenWhenGridFlickeredToZero()
    {
        // KERNFIX: +0x76 ∈ {1,2} = auf Zwischenstation (Insel) — STABIL, auch wenn die
        // Grid-Position auf (0,0) flackert (genau der Fall, der Insel-ZBs immer wieder
        // „nicht erkannt" ließ: memdump-180314, ZB 0x0003 +0x76=2 aber grid zeitweise (0,0)).
        var mem = new CellTableMem(TableWith());   // leere Tabelle → (0,0) wäre KEINE Insel
        var pool = new[]
        {
            Zb(0x05, 0x00000001, row: 0, col: 0, outcome: 0),   // echter Pool
            Zb(0x03, 0x00000001, row: 0, col: 0, outcome: 2),   // Grid flackert (0,0), aber +0x76=2 → Insel
        };

        var (main, island) = BubblewonderPoolClassifier.Split(pool, mem);

        Assert.Equal(new ushort[] { 0x05 }, main.Select(p => p.HeaderId).ToArray());
        Assert.Equal(new ushort[] { 0x03 }, island.Select(p => p.HeaderId).ToArray());
    }

    [Fact]
    public void Split_HeldZbWithStaleOutcome76_CountsAsMainPool()
    {
        // Ein von der Insel hochgehobener ZB behält +0x76=2 (stale). Da er in der HAND ist
        // (Held-Handle), muss er Haupt-Pool sein, NICHT Insel (sonst Doppelzählung 16/15).
        var mem = new CellTableMem(TableWith());
        var pool = new[] { Zb(0x07, 0x04001001, outcome: 2) };  // Held + stale +0x76

        var (main, island) = BubblewonderPoolClassifier.Split(pool, mem);

        Assert.Single(main);
        Assert.Empty(island);
    }

    [Fact]
    public void Split_GoalCellZb_IsNotIsland()
    {
        // Ein ZB auf der ZIEL-Zelle (0x17) ist gescort, keine re-losschickbare Insel.
        var mem = new CellTableMem(TableWith((10, 0, 0x17)));
        var pool = new[] { Zb(0x20, 0x04008001, row: 10, col: 0) };

        var (main, island) = BubblewonderPoolClassifier.Split(pool, mem);

        Assert.Empty(main);
        Assert.Empty(island);
    }

    [Fact]
    public void Split_HeldZb_CountsAsMainPool()
    {
        var mem = new CellTableMem(TableWith());
        var pool = new[] { Zb(0x07, 0x04001001) };  // hochgehoben

        var (main, island) = BubblewonderPoolClassifier.Split(pool, mem);

        Assert.Single(main);
        Assert.Empty(island);
    }
}
