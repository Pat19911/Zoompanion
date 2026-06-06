namespace ZoombiniHelper.Bubblewonder.Simulator;

/// <summary>
/// Treibt die Simulation eines ZBs PLUS aller dabei ausgelöster Folge-ZBs
/// (Channel-Befreiung, Push) bis kein Pending mehr übrig ist.
///
/// <para>Pro initialer ZB-Wahl liefert <see cref="RunSingle"/> die Liste
/// aller ZB-Outcomes in chronologischer Reihenfolge plus den finalen
/// Grid-Zustand. Folge-ZBs erben den FromPos+Direction des befreienden
/// Events — sie werden nicht erneut von einer Maschine gestartet.</para>
/// </summary>
public static class BubblewonderRunner
{
    /// <summary>Schutz gegen pathologische Endlos-Ketten von Befreiungen.</summary>
    public const int MaxFollowupZbs = 100;

    /// <summary>Simuliert einen ZB ab der gegebenen Maschine und führt alle
    /// dadurch befreiten/geschubsten Folge-ZBs ebenfalls bis zum Ende aus.</summary>
    public static RunResult RunSingle(BubblewonderGridModel grid, SimZb zb, int machineIdx)
    {
        var first = BubblewonderSimulator.Simulate(grid, zb, machineIdx);
        return ContinueWithFollowups(first, zb);
    }

    /// <summary>Gleich wie <see cref="RunSingle"/>, aber Start aus beliebiger
    /// Position+Richtung — z.B. um geparkte Insel-ZBs später loszuschicken.</summary>
    public static RunResult RunFromPosition(
        BubblewonderGridModel grid, SimZb zb, int startPos, Direction startDir)
    {
        var first = BubblewonderSimulator.SimulateFromPosition(grid, zb, startPos, startDir);
        return ContinueWithFollowups(first, zb);
    }

    private static RunResult ContinueWithFollowups(SimResult first, SimZb initialZb)
    {
        var outcomes = new List<ZbOutcome>
        {
            new(initialZb, first.Outcome, first.PathPositions),
        };
        var grid = first.ResultingGrid;

        // Drain Pending-Queues iterativ. Jeder verarbeitete ZB darf neue
        // Pendings auslösen — die landen wieder in der nächsten Runde im Drain.
        for (int i = 0; i < MaxFollowupZbs; i++)
        {
            var (next, isPush) = DequeueNextPending(grid);
            if (next is null) break;

            SimResult result = isPush
                ? StepPushed(grid, (PushedZb)next)
                : StepLiberated(grid, (LiberatedZb)next);
            outcomes.Add(new ZbOutcome(
                ZbFromPending(next), result.Outcome, result.PathPositions));
            grid = result.ResultingGrid;
        }

        return new RunResult(outcomes, grid);
    }

    private static (object? Next, bool IsPush) DequeueNextPending(BubblewonderGridModel grid)
    {
        // Pushes haben Vorrang vor Channel-Befreiungen — der Push passiert
        // physisch früher (Schubser ist schon DA, Channel-Effekt erst durch
        // sein Weiterlaufen). In der Praxis ist die Reihenfolge meist egal,
        // aber wir wollen deterministisch sein.
        if (grid.State.PendingPushedZbs.Count > 0)
        {
            var p = grid.State.PendingPushedZbs[0];
            grid.State.PendingPushedZbs.RemoveAt(0);
            return (p, true);
        }
        if (grid.State.PendingLiberatedZbs.Count > 0)
        {
            var l = grid.State.PendingLiberatedZbs[0];
            grid.State.PendingLiberatedZbs.RemoveAt(0);
            return (l, false);
        }
        return (null, false);
    }

    private static SimResult StepPushed(BubblewonderGridModel grid, PushedZb p) =>
        StartFromAdjacentCell(grid, p.Zb, p.FromPos, p.Direction);

    private static SimResult StepLiberated(BubblewonderGridModel grid, LiberatedZb l) =>
        StartFromAdjacentCell(grid, l.Zb, l.FromPos, l.Direction);

    /// <summary>Befreite/geschubste ZBs treten aus der Sticky-Cell heraus —
    /// sie verarbeiten die Sticky-Cell selbst nicht erneut, sondern starten
    /// einen Schritt weiter in der Auswurf-Richtung.</summary>
    private static SimResult StartFromAdjacentCell(
        BubblewonderGridModel grid, SimZb zb, int fromPos, Direction dir)
    {
        int row = fromPos / 13;
        int col = fromPos % 13;
        switch (dir)
        {
            case Direction.Up:    row--; break;
            case Direction.Right: col++; break;
            case Direction.Down:  row++; break;
            case Direction.Left:  col--; break;
        }
        if (row < 0 || row >= 12 || col < 0 || col >= 13)
        {
            // Direkt aus dem Grid ausgespuckt → gescored.
            return new SimResult(SimOutcome.Scored, new[] { fromPos }, grid);
        }
        return BubblewonderSimulator.SimulateFromPosition(grid, zb, row * 13 + col, dir);
    }

    private static SimZb ZbFromPending(object pending) => pending switch
    {
        LiberatedZb l => l.Zb,
        PushedZb p => p.Zb,
        _ => throw new InvalidOperationException(),
    };
}

public sealed record ZbOutcome(SimZb Zb, SimOutcome Outcome, IReadOnlyList<int> Path);

public sealed record RunResult(IReadOnlyList<ZbOutcome> Outcomes, BubblewonderGridModel FinalGrid)
{
    public int SurvivorCount => Outcomes.Count(o => o.Outcome == SimOutcome.Scored);
    public int DeadCount => Outcomes.Count(o => o.Outcome == SimOutcome.Dead);
    public int TrappedCount => Outcomes.Count(o => o.Outcome == SimOutcome.Trapped);
    public int ParkedCount => Outcomes.Count(o => o.Outcome == SimOutcome.Parked);
}
