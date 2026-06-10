using ZoombiniHelper;
using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Bubblewonder.Simulator;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Regression tests for the "ZB lands on island → solver recomputes" bug
/// (gemeldet 2026-06-10, memdump-080339, REGS 16606 Diff-4).
///
/// <para>Wurzel: Wenn das Board keine erkannte Insel-Maschine hat (im Dump:
/// „Maschinen: 2", beide Hauptwerfer), dann (a) verwarf <see cref="BubblewonderGridModelBuilder.WithParkedZbs"/>
/// die real existierenden Insel-ZBs komplett, und (b) der Simulator wertete eine
/// Insel-Landung als <see cref="SimOutcome.Dead"/>. Der Live-Read klassifizierte
/// dieselbe Landung aber als geparkt → die <c>FullStateSignature</c> matchte nie
/// → bei JEDER Insel-Landung „neu rechnen". Fix: beide Seiten parken den ZB unter
/// <see cref="GridState.StrandedIslandMachineIdx"/> (geparkt, aber nicht
/// re-schickbar) → Live und Plan-Simulation stimmen überein.</para>
/// </summary>
public class BubblewonderIslandStrandingTests
{
    private static CellModel Stone() =>
        new(MechanismType.StoneArea, 0, new bool[4], null, 0, 0);

    private static CellModel Goal() =>
        new(MechanismType.Goal, 0, new bool[4], null, 0, 0);

    private static int Pos(int row, int col) => row * 13 + col;

    private static PoolMember Pm(ushort hdr, byte h, byte e, byte n, byte f) =>
        new(Address: hdr, Hair: h, Eyes: e, Nose: n, Feet: f, YPosition: 0, SpriteId: 0, HeaderId: hdr);

    [Fact]
    public void StoneArea_NoIslandMachine_ParksStrandedInsteadOfDying()
    {
        // Werfer (0,5) → Down, Stein-Insel-Zelle bei (3,5), KEINE Insel-Maschine.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(3, 5)] = Stone() },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, IsIsland: false) });

        var result = BubblewonderSimulator.Simulate(grid, new SimZb(0x11, 3, 4, 2, 5), 0);

        // Stirbt NICHT — parkt (gestrandet).
        Assert.Equal(SimOutcome.Parked, result.Outcome);
        var stranded = result.ResultingGrid.State.ParkedZbsByMachineIdx
            .GetValueOrDefault(GridState.StrandedIslandMachineIdx);
        Assert.NotNull(stranded);
        Assert.Contains(stranded!, z => z.HeaderId == 0x11);
    }

    [Fact]
    public void WithParkedZbs_NoIslandMachine_RecordsStrandedNotDropped()
    {
        // Nur ein Werfer, keine Insel-Maschine — früher wurden die geparkten ZBs
        // hier still verworfen (return grid).
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>(),
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, IsIsland: false) });

        var result = BubblewonderGridModelBuilder.WithParkedZbs(grid, new[] { Pm(0x11, 3, 4, 2, 5) });

        var stranded = result.State.ParkedZbsByMachineIdx
            .GetValueOrDefault(GridState.StrandedIslandMachineIdx);
        Assert.NotNull(stranded);
        Assert.Contains(stranded!, z => z.HeaderId == 0x11);
    }

    [Fact]
    public void LocateOnPlan_PlannedIslandLanding_IsOnPlan_NotDeviation()
    {
        // DAS ist der gemeldete Bug: ein Plan-ZB, der planmäßig auf der Insel landet,
        // darf KEINE „Abweichung" auslösen.
        var cells = new Dictionary<int, CellModel> { [Pos(3, 5)] = Stone() };
        var machines = new[] { new MachineModel(0, Pos(0, 5), Direction.Down, IsIsland: false) };
        var baseGrid = new BubblewonderGridModel(cells, machines);

        var zb = new SimZb(0x11, 3, 4, 2, 5);
        var basePool = new[] { zb };
        var plan = new[] { new Assignment(zb, 0) };

        // Live-Zustand, wie der Renderer ihn baut: ZB als Insel-geparkt klassifiziert
        // (WithParkedZbs → Stranded-Index), Hauptpool leer.
        var liveGrid = BubblewonderGridModelBuilder.WithParkedZbs(baseGrid, new[] { Pm(0x11, 3, 4, 2, 5) });
        var livePool = System.Array.Empty<SimZb>();

        int? step = BubblewonderSolver.LocateOnPlan(baseGrid, basePool, plan, liveGrid, livePool);

        Assert.NotNull(step);  // auf Plan — nicht null (= Abweichung)
    }

    [Fact]
    public void Solver_StrandedIslandZb_NotCountedAsSurvivor()
    {
        // Ein gestrandeter Insel-ZB ist NICHT re-schickbar → darf keinen erfundenen
        // Gewinn-Pfad öffnen. Ohne Ziel-Zelle ist die ehrliche Antwort 0 Survivors.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>(),
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, IsIsland: false) });
        var stranded = BubblewonderGridModelBuilder.WithParkedZbs(grid, new[] { Pm(0x11, 3, 4, 2, 5) });

        var result = BubblewonderSolver.SolveGreedy(stranded, System.Array.Empty<SimZb>());

        Assert.Equal(0, result.Survivors);
        Assert.Empty(result.Assignments);
    }
}
