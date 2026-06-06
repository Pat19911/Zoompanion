using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Bubblewonder.Simulator;

namespace ZoombiniHelper.Tests;

public class BubblewonderSimulatorTests
{
    private static CellModel Trap(int channel = 0) =>
        new(MechanismType.Trap, channel, new bool[4], null, 0, 0);

    private static CellModel Deflector(Direction dir, int channel = 0) =>
        new(MechanismType.StaticDeflector, channel, CellModel.MakeFBits(dir), dir, 0, 0);

    private static CellModel Switch(int channel, params Direction[] activeDirs) =>
        new(MechanismType.SwitchActivated, channel, CellModel.MakeFBits(activeDirs),
            activeDirs[0], 0, 0);

    private static CellModel Trigger(int channel) =>
        new(MechanismType.Trigger, channel, new bool[4], null, 0, 0);

    private static CellModel Sticky(int channel) =>
        new(MechanismType.Sticky, channel, new bool[4], null, 0, 0);

    private static CellModel Conditional(int attrCode, int variant, Direction matchDir, int channel = 0) =>
        new(MechanismType.Conditional, channel, CellModel.MakeFBits(matchDir),
            matchDir, attrCode, variant);

    // Ziel-Steinzelle (Typ 0x17). Seit dem Goal-Fix scort ein ZB NUR auf einer
    // solchen Zelle — nicht mehr durch bloßes Verlassen des Gitters.
    private static CellModel Goal() =>
        new(MechanismType.Goal, 0, new bool[4], null, 0, 0);

    private static SimZb DefaultZb() => new(0x100, Hair: 1, Eyes: 1, Nose: 1, Feet: 1);

    private static int Pos(int row, int col) => row * 13 + col;

    [Fact]
    public void StraightDown_Scores()
    {
        // Maschine bei (0,5) zeigt nach unten; Ziel-Zelle am unteren Ende (11,5).
        // ZB läuft straight runter und scort auf der Ziel-Zelle.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(11, 5)] = Goal() },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });
        var result = BubblewonderSimulator.Simulate(grid, DefaultZb(), 0);
        Assert.Equal(SimOutcome.Scored, result.Outcome);
        Assert.Equal(12, result.PathPositions.Count);  // 12 rows, scored auf (11,5)
    }

    [Fact]
    public void TrapKills()
    {
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(3, 5)] = Trap() },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });
        var result = BubblewonderSimulator.Simulate(grid, DefaultZb(), 0);
        Assert.Equal(SimOutcome.Dead, result.Outcome);
        Assert.Equal(Pos(3, 5), result.PathPositions[^1]);
    }

    [Fact]
    public void Deflector_RedirectsZb()
    {
        // ZB läuft runter, trifft Deflector der nach Rechts zeigt → läuft nach rechts
        // bis zur Ziel-Zelle am rechten Rand (3,12) und scort dort.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Deflector(Direction.Right),
                [Pos(3, 12)] = Goal(),
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });
        var result = BubblewonderSimulator.Simulate(grid, DefaultZb(), 0);
        Assert.Equal(SimOutcome.Scored, result.Outcome);
        // Pfad: (0,5),(1,5),(2,5),(3,5) → ab da rechts: (3,6),(3,7)...(3,12)
        Assert.Equal(Pos(3, 12), result.PathPositions[^1]);
    }

    [Fact]
    public void Conditional_MatchRedirects_NoMatchPasses()
    {
        // Conditional bei (3,5): wenn Hair=1 → nach rechts. ZB hat Hair=1 → wird umgelenkt.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Conditional(attrCode: 1, variant: 1, matchDir: Direction.Right),
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });

        // ZB mit Hair=1 (matcht) → Umlenkung
        var matched = BubblewonderSimulator.Simulate(
            grid, new SimZb(0x100, Hair: 1, Eyes: 0, Nose: 0, Feet: 0), 0);
        Assert.Equal(Pos(3, 12), matched.PathPositions[^1]);

        // ZB mit Hair=2 (matcht NICHT) → straight durch
        var unmatched = BubblewonderSimulator.Simulate(
            grid, new SimZb(0x101, Hair: 2, Eyes: 0, Nose: 0, Feet: 0), 0);
        Assert.Equal(Pos(11, 5), unmatched.PathPositions[^1]);
    }

    [Fact]
    public void Sticky_TrapsZb()
    {
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(3, 5)] = Sticky(channel: 1) },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });
        var result = BubblewonderSimulator.Simulate(grid, DefaultZb(), 0);
        Assert.Equal(SimOutcome.Trapped, result.Outcome);
        Assert.Equal((ushort)0x100, result.ResultingGrid.State.StickyTrappedByCell[Pos(3, 5)].HeaderId);
    }

    [Fact]
    public void Trigger_FreesStickyInSameChannel()
    {
        // Layout:  Sticky(channel=1) bei (3,5),  Trigger(channel=1) bei (8,5).
        // Maschine 0 → ZB-A landet im Sticky.
        // Maschine 1 → ZB-B läuft durch Trigger → Sticky-Channel-Effekt: ZB-A wird befreit.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Sticky(channel: 1),
                [Pos(8, 5)] = Trigger(channel: 1),
            },
            new[]
            {
                new MachineModel(0, Pos(0, 5), Direction.Down, false),  // → Sticky
                new MachineModel(1, Pos(0, 7), Direction.Down, false),  // läuft straight, kein Trigger im Weg
            });

        var zbA = new SimZb(0x100, 1, 1, 1, 1);
        var resultA = BubblewonderSimulator.Simulate(grid, zbA, 0);
        Assert.Equal(SimOutcome.Trapped, resultA.Outcome);

        // Jetzt ZB-B durch Trigger → wir brauchen ein Layout wo seine Bahn den Trigger trifft.
        var grid2 = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Sticky(channel: 1),
                [Pos(8, 5)] = Trigger(channel: 1),
            },
            new[]
            {
                new MachineModel(0, Pos(0, 5), Direction.Down, false),
            },
            resultA.ResultingGrid.State);

        // ZB-B in den Trigger schicken → Pfad: (0,5)→(3,5) klebt fest? Nein, Sticky ist schon belegt
        // mit ZB-A. Ein zweiter ZB der reinläuft... was passiert? Im Code: trapped wird
        // überschrieben. Für jetzt akzeptieren wir das.
        // Stattdessen: simuliere direkt den Channel-Effekt.
        var zbB = new SimZb(0x101, 1, 1, 1, 1);
        // Hack: machen wir's ohne neuen Grid — direkt die Befreiung prüfen via Trigger durchläuft
        // (Test braucht ein Grid wo der ZB nur den Trigger trifft, nicht den Sticky)
        var grid4 = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Trigger(channel: 1),
                [Pos(7, 7)] = Sticky(channel: 1),
                [Pos(11, 5)] = Goal(),   // ZB-B läuft durch den Trigger weiter runter ins Ziel
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) },
            new GridState
            {
                StickyTrappedByCell = new() { [Pos(7, 7)] = new SimZb(0x100, 1, 1, 1, 1) },
            });

        var resultB = BubblewonderSimulator.Simulate(grid4, zbB, 0);
        Assert.Equal(SimOutcome.Scored, resultB.Outcome);
        Assert.False(resultB.ResultingGrid.State.StickyTrappedByCell.ContainsKey(Pos(7, 7)));
        Assert.Contains(resultB.ResultingGrid.State.PendingLiberatedZbs,
            l => l.Zb.HeaderId == 0x100);
    }

    [Fact]
    public void Sticky_Liberation_KeepsEntryDirection()
    {
        // ZB-A läuft mit Direction.Down in den Sticky bei (3,5) — klebt fest.
        // Trigger im selben Channel feuert. ZB-A wird befreit + sollte Direction.Down behalten.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Sticky(channel: 1),
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });
        var zbA = new SimZb(0x200, 1, 1, 1, 1);
        var afterA = BubblewonderSimulator.Simulate(grid, zbA, 0);
        Assert.Equal(SimOutcome.Trapped, afterA.Outcome);
        Assert.Equal(Direction.Down, afterA.ResultingGrid.State.StickyEntryDirByCell[Pos(3, 5)]);

        // Channel-Effekt simulieren: setze Trigger ein und schicke ZB-B durch.
        var grid2 = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Sticky(channel: 1),
                [Pos(7, 0)] = Trigger(channel: 1),
            },
            new[] { new MachineModel(0, Pos(0, 0), Direction.Down, false) },
            afterA.ResultingGrid.State);
        var zbB = new SimZb(0x201, 1, 1, 1, 1);
        var afterB = BubblewonderSimulator.Simulate(grid2, zbB, 0);
        Assert.Single(afterB.ResultingGrid.State.PendingLiberatedZbs);
        var lib = afterB.ResultingGrid.State.PendingLiberatedZbs[0];
        Assert.Equal((ushort)0x200, lib.Zb.HeaderId);
        Assert.Equal(Direction.Down, lib.Direction);  // Eintrittsrichtung beibehalten
    }

    [Fact]
    public void Sticky_Push_ZbWegVomSchubser()
    {
        // ZB-A läuft mit Direction.Down rein, klebt bei (3,5).
        // ZB-B läuft auch von oben rein (Direction.Down) und schubst.
        // Erwartung: ZB-A wird Down ausgeworfen (= weg von Schubser, der von oben kam).
        // ZB-B nimmt den Sticky-Platz ein.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Sticky(channel: 1),
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });

        var zbA = new SimZb(0x300, 1, 1, 1, 1);
        var afterA = BubblewonderSimulator.Simulate(grid, zbA, 0);

        var grid2 = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(3, 5)] = Sticky(channel: 1) },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) },
            afterA.ResultingGrid.State);
        var zbB = new SimZb(0x301, 1, 1, 1, 1);
        var afterB = BubblewonderSimulator.Simulate(grid2, zbB, 0);

        Assert.Equal(SimOutcome.Trapped, afterB.Outcome);
        Assert.Single(afterB.ResultingGrid.State.PendingPushedZbs);
        var push = afterB.ResultingGrid.State.PendingPushedZbs[0];
        Assert.Equal((ushort)0x300, push.Zb.HeaderId);
        Assert.Equal(Direction.Down, push.Direction);  // weiter nach unten = weg vom Schubser
        Assert.Equal((ushort)0x301, afterB.ResultingGrid.State.StickyTrappedByCell[Pos(3, 5)].HeaderId);
    }

    [Fact]
    public void Switch_UsesCurrentState_DoesNotChangeOnPassthrough()
    {
        // Switch bei (3,5) mit aktiven Richtungen Right + Down. Initial: state-Index
        // für Right = FBitIndexFor(Right). ZB läuft rein, wird nach Rechts umgelenkt.
        // Switch-State soll unverändert bleiben.
        int rightStateIdx = CellModel.FBitIndexFor(Direction.Right);
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Switch(channel: 1, Direction.Right, Direction.Down),
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) },
            new GridState { SwitchStateByCell = new() { [Pos(3, 5)] = rightStateIdx } });

        var result = BubblewonderSimulator.Simulate(grid, DefaultZb(), 0);
        Assert.Equal(Pos(3, 12), result.PathPositions[^1]);
        Assert.Equal(rightStateIdx, result.ResultingGrid.State.SwitchStateByCell[Pos(3, 5)]);
    }

    [Fact]
    public void Trigger_CyclesSwitchInSameChannel_RoundRobinThroughActiveDirs()
    {
        // Switch mit aktiven Richtungen Right + Down. Initial: Right.
        // Trigger feuert → Switch cyclt zur nächsten aktiven Richtung.
        // F-Bit-Indices: Right=2, Down=1. Cycle von 2 → 3 (leer) → 0 (leer) → 1 (Down).
        int rightIdx = CellModel.FBitIndexFor(Direction.Right);
        int downIdx = CellModel.FBitIndexFor(Direction.Down);
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(1, 5)] = Trigger(channel: 1),
                [Pos(8, 5)] = Switch(channel: 1, Direction.Right, Direction.Down),
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) },
            new GridState { SwitchStateByCell = new() { [Pos(8, 5)] = rightIdx } });

        var result = BubblewonderSimulator.Simulate(grid, DefaultZb(), 0);
        Assert.Equal(downIdx, result.ResultingGrid.State.SwitchStateByCell[Pos(8, 5)]);
    }

    [Fact]
    public void Trigger_CyclingSkipsInactiveBits()
    {
        // Switch hat nur Up + Down aktiv. Initial: Up.
        // F-Bit-Indices: Up=3, Down=1. Cycle von 3 → 0 (leer) → 1 (Down).
        int upIdx = CellModel.FBitIndexFor(Direction.Up);
        int downIdx = CellModel.FBitIndexFor(Direction.Down);
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(1, 5)] = Trigger(channel: 1),
                [Pos(8, 5)] = Switch(channel: 1, Direction.Up, Direction.Down),
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) },
            new GridState { SwitchStateByCell = new() { [Pos(8, 5)] = upIdx } });

        var result = BubblewonderSimulator.Simulate(grid, DefaultZb(), 0);
        Assert.Equal(downIdx, result.ResultingGrid.State.SwitchStateByCell[Pos(8, 5)]);
    }
}
