using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Bubblewonder.Simulator;

namespace ZoombiniHelper.Tests;

public class BubblewonderRunnerTests
{
    private static int Pos(int row, int col) => row * 13 + col;

    private static CellModel Sticky(int channel) =>
        new(MechanismType.Sticky, channel, new bool[4], null, 0, 0);
    private static CellModel Trigger(int channel) =>
        new(MechanismType.Trigger, channel, new bool[4], null, 0, 0);
    // Ziel-Steinzelle (Typ 0x17): seit dem Goal-Fix scort ein ZB nur hier,
    // nicht mehr durch bloßes Verlassen des Gitters.
    private static CellModel Goal() =>
        new(MechanismType.Goal, 0, new bool[4], null, 0, 0);

    [Fact]
    public void RunSingle_NoFollowups_ReturnsSingleOutcome()
    {
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(11, 5)] = Goal() },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });
        var zb = new SimZb(0x100, 1, 1, 1, 1);

        var run = BubblewonderRunner.RunSingle(grid, zb, 0);

        Assert.Single(run.Outcomes);
        Assert.Equal(SimOutcome.Scored, run.Outcomes[0].Outcome);
        Assert.Equal(1, run.SurvivorCount);
    }

    [Fact]
    public void RunSingle_ChannelLiberation_ContinuesFreedZb()
    {
        // Layout: Sticky bei (3,7) — schon belegt mit ZB-A.
        // Trigger bei (5,5) im selben Channel.
        // ZB-B läuft von (0,5) runter durch Trigger → ZB-A wird befreit.
        // ZB-A war mit Direction.Down eingestiegen → läuft nach Befreiung weiter
        // Down von Pos(3,7), durchquert (4,7)..(11,7) und scored.
        // ZB-B läuft weiter ab (5,5), kommt bis (11,5) und scored.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 7)] = Sticky(channel: 1),
                [Pos(5, 5)] = Trigger(channel: 1),
                [Pos(11, 5)] = Goal(),   // ZB-B läuft unten ins Ziel
                [Pos(11, 7)] = Goal(),   // befreiter ZB-A läuft Down ins Ziel
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) },
            new GridState
            {
                StickyTrappedByCell = new() { [Pos(3, 7)] = new SimZb(0xA0, 1, 1, 1, 1) },
                StickyEntryDirByCell = new() { [Pos(3, 7)] = Direction.Down },
            });
        var zbB = new SimZb(0xB0, 1, 1, 1, 1);

        var run = BubblewonderRunner.RunSingle(grid, zbB, 0);

        // Erwartung: 2 Outcomes — ZB-B (initial) + ZB-A (befreit).
        Assert.Equal(2, run.Outcomes.Count);
        Assert.Equal((ushort)0xB0, run.Outcomes[0].Zb.HeaderId);
        Assert.Equal(SimOutcome.Scored, run.Outcomes[0].Outcome);
        Assert.Equal((ushort)0xA0, run.Outcomes[1].Zb.HeaderId);
        Assert.Equal(SimOutcome.Scored, run.Outcomes[1].Outcome);
        Assert.Equal(2, run.SurvivorCount);
    }

    [Fact]
    public void RunSingle_PushZb_RegistersAsExtraOutcome()
    {
        // Sticky bei (3,5) ist belegt mit ZB-A (Direction.Down war Eintritt).
        // ZB-B läuft von (0,5) Down rein → schubst.
        // Erwartung: 2 Outcomes; ZB-B trapped, ZB-A scored (in Schub-Richtung Down weiter).
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Sticky(channel: 1),
                [Pos(11, 5)] = Goal(),   // geschubster ZB-A läuft Down ins Ziel
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) },
            new GridState
            {
                StickyTrappedByCell = new() { [Pos(3, 5)] = new SimZb(0xA0, 1, 1, 1, 1) },
                StickyEntryDirByCell = new() { [Pos(3, 5)] = Direction.Down },
            });
        var zbB = new SimZb(0xB0, 1, 1, 1, 1);

        var run = BubblewonderRunner.RunSingle(grid, zbB, 0);

        Assert.Equal(2, run.Outcomes.Count);
        Assert.Equal((ushort)0xB0, run.Outcomes[0].Zb.HeaderId);
        Assert.Equal(SimOutcome.Trapped, run.Outcomes[0].Outcome);
        Assert.Equal((ushort)0xA0, run.Outcomes[1].Zb.HeaderId);
        Assert.Equal(SimOutcome.Scored, run.Outcomes[1].Outcome);
        Assert.Equal(1, run.SurvivorCount);
        Assert.Equal(1, run.TrappedCount);
    }

    [Fact]
    public void RunSingle_LiberatedZbAtBottomEdge_ScoresImmediately()
    {
        // Sticky bei (11,5) — letzte Reihe — Eintritt Down. Befreiung → läuft nach unten,
        // direkt aus dem Grid → Scored. Edge-Case für StartFromAdjacentCell.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(11, 5)] = Sticky(channel: 1),
                [Pos(5, 0)] = Trigger(channel: 1),
            },
            new[] { new MachineModel(0, Pos(0, 0), Direction.Down, false) },
            new GridState
            {
                StickyTrappedByCell = new() { [Pos(11, 5)] = new SimZb(0xA0, 1, 1, 1, 1) },
                StickyEntryDirByCell = new() { [Pos(11, 5)] = Direction.Down },
            });
        var zbB = new SimZb(0xB0, 1, 1, 1, 1);

        var run = BubblewonderRunner.RunSingle(grid, zbB, 0);

        Assert.Equal(2, run.Outcomes.Count);
        Assert.Equal(SimOutcome.Scored, run.Outcomes[1].Outcome);
    }
}
