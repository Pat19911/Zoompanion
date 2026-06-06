using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Bubblewonder.Simulator;

namespace ZoombiniHelper.Tests;

public class BubblewonderSpawnStoreTests
{
    [Fact]
    public void Observe_NewCell_ReturnsTrue_ThenFalse()
    {
        var s = new BubblewonderSpawnStore();
        Assert.True(s.Observe(16606, 0, 34, Direction.Down));
        Assert.False(s.Observe(16606, 0, 34, Direction.Down));  // schon bekannt
    }

    [Fact]
    public void Observe_DirectionLater_UpdatesOnce()
    {
        var s = new BubblewonderSpawnStore();
        Assert.True(s.Observe(16606, 0, 53, null));        // erst ohne Richtung
        Assert.True(s.Observe(16606, 0, 53, Direction.Right)); // Richtung nachgereicht → true
        Assert.False(s.Observe(16606, 0, 53, Direction.Up));   // schon gesetzt → keine Änderung
    }

    [Fact]
    public void Get_SeparatesByRegsAndVariant()
    {
        var s = new BubblewonderSpawnStore();
        s.Observe(16606, 0, 53, null);   // variant 0 → Insel (4,1)
        s.Observe(16606, 1, 41, null);   // variant 1 → Insel (3,2)
        Assert.Equal(new[] { 53 }, s.Get(16606, 0).Select(x => x.Pos));
        Assert.Equal(new[] { 41 }, s.Get(16606, 1).Select(x => x.Pos));
        Assert.Empty(s.Get(16608, 0));
    }

    [Fact]
    public void FormatThenParse_Roundtrips()
    {
        var s = new BubblewonderSpawnStore();
        s.Observe(16606, 0, 34, Direction.Down);
        s.Observe(16606, 0, 76, Direction.Down);
        s.Observe(16606, 0, 53, Direction.Right);
        s.Observe(16608, 2, 75, null);

        var reparsed = BubblewonderSpawnStore.Parse(s.Format());

        var v0 = reparsed.Get(16606, 0);
        Assert.Equal(new[] { 34, 53, 76 }, v0.Select(x => x.Pos).OrderBy(p => p));
        Assert.Equal(Direction.Right, v0.Single(x => x.Pos == 53).Dir);
        Assert.Null(reparsed.Get(16608, 2).Single().Dir);
    }

    [Fact]
    public void Parse_SkipsGarbageLines()
    {
        var s = BubblewonderSpawnStore.Parse(
            "# comment\nnonsense\n16606,0 = 34:1,99\n16606 = bad\n,= worse\n");
        Assert.Equal(new[] { 34, 99 }, s.Get(16606, 0).Select(x => x.Pos).OrderBy(p => p));
        Assert.Equal(Direction.Right, s.Get(16606, 0).Single(x => x.Pos == 34).Dir);  // 1 = Right
        Assert.Null(s.Get(16606, 0).Single(x => x.Pos == 99).Dir);
    }
}
