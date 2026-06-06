namespace ZoombiniHelper.Bubblewonder.Simulator;

/// <summary>
/// Deterministische Simulation eines einzelnen ZB-Durchlaufs durch das Grid.
///
/// <para>Liefert das Outcome (gescored / gestorben / geparkt / festgeklebt),
/// den vollständigen Pfad und den neuen Grid-State (Switch-Stellungen,
/// Sticky-Belegung, sowie Pending-Folge-ZBs aus Channel-Befreiung oder Push).</para>
///
/// <para>Die Pending-Queues werden vom <see cref="BubblewonderRunner"/>
/// in Folge-Simulations-Steps fortgeführt — der Simulator selbst bleibt
/// auf einen einzelnen ZB-Schritt beschränkt.</para>
/// </summary>
public static class BubblewonderSimulator
{
    private const int GridCols = 13;
    private const int GridRows = 12;

    /// <summary>Maximum Schritte pro ZB-Pfad — Schutz gegen Endlos-Loops
    /// (sollte praktisch nie erreicht werden, da das Grid endlich ist und
    /// Switches deterministisch).</summary>
    public const int MaxStepsPerZb = 200;

    /// <summary>Simuliert einen ZB ab der angegebenen Maschine.</summary>
    public static SimResult Simulate(BubblewonderGridModel grid, SimZb zb, int startMachineIdx)
    {
        if (startMachineIdx < 0 || startMachineIdx >= grid.Machines.Count)
            return new SimResult(SimOutcome.InvalidStart, new[] { -1 }, grid);
        var machine = grid.Machines[startMachineIdx];
        return SimulateFromPosition(grid, zb, machine.StartCellIndex, machine.StartDirection);
    }

    /// <summary>Simuliert einen ZB ab beliebiger Position + Richtung — wird
    /// für Pending-Folge-ZBs (befreit/geschubst) genutzt.</summary>
    public static SimResult SimulateFromPosition(
        BubblewonderGridModel grid, SimZb zb, int startPos, Direction startDir)
    {
        var path = new List<int> { startPos };
        var state = grid.CloneState();
        int pos = startPos;
        Direction dir = startDir;

        for (int step = 0; step < MaxStepsPerZb; step++)
        {
            // Beim Start: Cell-Effekte (Deflector, Conditional, Switch, …) wirken
            // normal — eine Maschinen-Cell mit Pfeil dreht den ZB direkt. NUR
            // die Insel-Park-Logik wird skipped, sonst würde der ZB sich von
            // seiner eigenen Insel-Maschine selbst parken.
            bool isStart = step == 0;
            var cell = grid.CellAt(pos);

            // Bekannte Goal-Cell? Nur dann sofort Scored, wenn die Cell passiv
            // ist (keine direction-führende Mechanik). Wenn dort jetzt ein
            // Deflector / Conditional / Switch sitzt, gewinnt die Mechanik —
            // schickt sie den ZB out-of-grid, fängt das die normale Sim-Logik
            // weiter unten als Scored auf. Schickt sie ihn zurück ins Grid,
            // ist die historisch gelernte Goal-Cell in dieser Konfiguration
            // halt kein Exit mehr.
            if (state.KnownGoalCells.Contains(pos) && IsPassiveCell(cell))
                return Done(SimOutcome.Scored, path, grid, state);
            switch (cell.Type)
            {
                case MechanismType.Trap:
                    return Done(SimOutcome.Dead, path, grid, state);

                case MechanismType.StaticDeflector:
                    dir = cell.PrimaryDirection ?? dir;
                    break;

                case MechanismType.Conditional:
                    if (cell.MatchesZb(zb))
                        dir = cell.PrimaryDirection ?? dir;
                    break;

                case MechanismType.SwitchActivated:
                    // Switch-State entscheidet die Richtung — wird NICHT durch
                    // eigenen Durchlauf verändert.
                    if (state.SwitchStateByCell.TryGetValue(pos, out int stateIdx))
                        dir = cell.DirectionAtStateIndex(stateIdx) ?? dir;
                    else
                        dir = cell.PrimaryDirection ?? dir;
                    break;

                case MechanismType.Trigger:
                    ApplyChannelEffect(grid, state, cell.Channel);
                    break;

                case MechanismType.Sticky:
                    if (isStart) break;  // ZB darf von eigener Sticky-Cell starten
                    return HandleSticky(zb, pos, dir, path, grid, state);

                case MechanismType.Goal:
                    // Zelltyp 0x17 = Ziel-Steininsel (oben-rechts). Nur das ist ein Score.
                    return Done(SimOutcome.Scored, path, grid, state);

                case MechanismType.StoneArea:
                    // Stein-Insel (0x15/0x16 Zwischenstation, 0x14 Start-Insel): der ZB
                    // LANDET hier und PARKT — er stirbt NICHT (User-verifiziert 2026-05-28:
                    // „landen sie da nicht auf eine Insel?"). Er wird der nächstgelegenen
                    // Insel-Maschine zugeordnet und ist darüber re-losschickbar. (Früher
                    // fälschlich Dead → genau deshalb fand das Modell für Layouts wie 16608
                    // keinen Weg.) Tracking an einer Maschine + Zyklen-Schutz im Solver
                    // verhindern das alte „endloses Re-Launch"-Problem.
                    if (isStart) break;
                    if (TryParkOnNearestIsland(zb, pos, grid, state))
                        return Done(SimOutcome.Parked, path, grid, state);
                    // Keine Insel-Maschine bekannt → toter Endpunkt (konservativ).
                    return Done(SimOutcome.Dead, path, grid, state);

                case MechanismType.Passthrough:
                case MechanismType.Toggle:
                case MechanismType.Unknown:
                    dir = cell.PrimaryDirection ?? dir;
                    break;
            }

            // Insel-Maschinen: ZB landet hier und parkt — aber NUR wenn er
            // nicht gerade von dieser Maschine GESTARTET wurde (= isStart=false).
            if (!isStart && cell.IsIslandMachine && cell.MachineIdx is int machineIdx)
            {
                if (!state.ParkedZbsByMachineIdx.TryGetValue(machineIdx, out var parkedList))
                    state.ParkedZbsByMachineIdx[machineIdx] = parkedList = new List<SimZb>();
                parkedList.Add(zb);
                return Done(SimOutcome.Parked, path, grid, state);
            }

            int nextPos = StepInDirection(pos, dir);
            if (nextPos < 0)
                // Verlässt das Gitter, OHNE eine Ziel-Zelle (0x17) erreicht zu haben.
                // Das ist KEIN Score — echtes Scoren passiert ausschließlich auf einer
                // Goal-Zelle (oben-rechts). Frühere Annahme „jede Kante = Score" war der
                // Kernfehler (zählte Pool-Rückläufer/Fallen als Erfolg → Fantasie-„16/16").
                return Done(SimOutcome.Dead, path, grid, state);

            pos = nextPos;
            path.Add(pos);
        }
        return Done(SimOutcome.MaxStepsExceeded, path, grid, state);
    }

    /// <summary>Cell ist "passiv" = keine Mechanik die den ZB aktiv routet.
    /// Goal-Cell-Heuristik darf nur bei passiven Cells sofort Scored zurückgeben;
    /// alle anderen Cells müssen ihre Mechanik durchlaufen lassen.</summary>
    private static bool IsPassiveCell(CellModel cell) =>
        cell.Type is MechanismType.Passthrough or MechanismType.Unknown;

    /// <summary>Parkt einen ZB, der auf einer Stein-Insel gelandet ist, an der
    /// nächstgelegenen Insel-Maschine (= deren Re-Launch-Punkt). True wenn geparkt,
    /// false wenn das Grid keine Insel-Maschine hat.</summary>
    private static bool TryParkOnNearestIsland(
        SimZb zb, int landingPos, BubblewonderGridModel grid, GridState state)
    {
        int landRow = landingPos / GridCols, landCol = landingPos % GridCols;
        int? bestIdx = null;
        int bestDist = int.MaxValue;
        foreach (var m in grid.Machines)
        {
            if (!m.IsIsland) continue;
            int mr = m.StartCellIndex / GridCols, mc = m.StartCellIndex % GridCols;
            int dist = Math.Abs(mr - landRow) + Math.Abs(mc - landCol);
            if (dist < bestDist) { bestDist = dist; bestIdx = m.Index; }
        }
        if (bestIdx is not int idx) return false;
        if (!state.ParkedZbsByMachineIdx.TryGetValue(idx, out var list))
            state.ParkedZbsByMachineIdx[idx] = list = new List<SimZb>();
        list.Add(zb);
        return true;
    }

    private static SimResult HandleSticky(
        SimZb zb, int pos, Direction dir, List<int> path,
        BubblewonderGridModel grid, GridState state)
    {
        // Sticky belegt → Push: alter ZB raus in Schub-Richtung, neuer rein.
        if (state.StickyTrappedByCell.TryGetValue(pos, out var existing))
        {
            state.PendingPushedZbs.Add(new PushedZb(existing, pos, dir));
        }
        state.StickyTrappedByCell[pos] = zb;
        state.StickyEntryDirByCell[pos] = dir;
        return Done(SimOutcome.Trapped, path, grid, state);
    }

    private static SimResult Done(
        SimOutcome outcome, List<int> path,
        BubblewonderGridModel grid, GridState state) =>
        new(outcome, path, grid.WithState(state));

    /// <summary>Wendet den Channel-Effekt an (cycle alle Switches im Channel,
    /// befreie alle Stickies im Channel). Mirrors fn 0x42A950 in v2 PE32.</summary>
    private static void ApplyChannelEffect(
        BubblewonderGridModel grid, GridState state, int channel)
    {
        foreach (var (cellIdx, cell) in grid.CellsInChannel(channel))
        {
            if (cell.Type == MechanismType.SwitchActivated)
            {
                int oldState = state.SwitchStateByCell.GetValueOrDefault(cellIdx);
                state.SwitchStateByCell[cellIdx] = NextActiveDirectionIndex(cell, oldState);
            }
            else if (cell.Type == MechanismType.Sticky &&
                     state.StickyTrappedByCell.TryGetValue(cellIdx, out var trapped))
            {
                // Channel-Befreiung: ZB läuft in seiner ursprünglichen
                // Eintrittsrichtung weiter ("die Richtung in die er vorher schon
                // gelaufen ist").
                var entryDir = state.StickyEntryDirByCell.GetValueOrDefault(cellIdx, Direction.Down);
                state.PendingLiberatedZbs.Add(new LiberatedZb(trapped, cellIdx, entryDir));
                state.StickyTrappedByCell.Remove(cellIdx);
                state.StickyEntryDirByCell.Remove(cellIdx);
            }
        }
    }

    /// <summary>Round-robin durch nur die aktiven Direction-Bits in F4..F7.
    /// Mirrors die Loop in fn 0x42A950 (cycle bis ActiveDirections[idx] != 0).</summary>
    private static int NextActiveDirectionIndex(CellModel cell, int currentState)
    {
        int next = currentState;
        for (int i = 0; i < 4; i++)
        {
            next = (next + 1) & 0x03;
            if (cell.HasDirectionAtStateIndex(next))
                return next;
        }
        return currentState;  // alle leer → bleibt
    }

    private static int StepInDirection(int pos, Direction dir)
    {
        int row = pos / GridCols;
        int col = pos % GridCols;
        switch (dir)
        {
            case Direction.Up:    row--; break;
            case Direction.Right: col++; break;
            case Direction.Down:  row++; break;
            case Direction.Left:  col--; break;
        }
        if (row < 0 || row >= GridRows || col < 0 || col >= GridCols)
            return -1;
        return row * GridCols + col;
    }
}

public enum Direction : byte { Up = 0, Right = 1, Down = 2, Left = 3 }

public enum SimOutcome
{
    Scored,
    Dead,
    Parked,
    Trapped,
    InvalidStart,
    MaxStepsExceeded,
}

public sealed record SimZb(ushort HeaderId, byte Hair, byte Eyes, byte Nose, byte Feet);

public sealed record SimResult(
    SimOutcome Outcome,
    IReadOnlyList<int> PathPositions,
    BubblewonderGridModel ResultingGrid);

/// <summary>Minimal-State zwischen ZB-Durchläufen (deterministisch).</summary>
public sealed class GridState
{
    /// <summary>Cells die als "Scored"-Endpoint aus Live-Beobachtungen
    /// bekannt sind. Wenn ein ZB auf so einer Cell landet → Scored, ohne
    /// weiterlaufen zu müssen. Optional — wenn leer, fällt der Sim auf die
    /// "out-of-grid = Scored"-Heuristik zurück.</summary>
    public HashSet<int> KnownGoalCells { get; init; } = new();

    public Dictionary<int, int> SwitchStateByCell { get; init; } = new();
    public Dictionary<int, SimZb> StickyTrappedByCell { get; init; } = new();
    /// <summary>Pro belegtem Sticky: Eintrittsrichtung des aktuell gefangenen ZBs.
    /// Wird beim Channel-Befreien als Auswurf-Richtung verwendet
    /// ("ZB läuft in die Richtung weiter in die er vorher schon gelaufen ist").</summary>
    public Dictionary<int, Direction> StickyEntryDirByCell { get; init; } = new();
    /// <summary>Pro Insel-Maschine die Liste der dort geparkten ZBs (Reihenfolge =
    /// Park-Reihenfolge). Der Solver kann jeden geparkten ZB über die jeweilige
    /// Insel-Maschine wieder losschicken.</summary>
    public Dictionary<int, List<SimZb>> ParkedZbsByMachineIdx { get; init; } = new();
    /// <summary>ZBs die in dieser Simulation per Channel-Trigger befreit wurden.
    /// Werden vom Runner in Folge-Simulation weitergeführt.</summary>
    public List<LiberatedZb> PendingLiberatedZbs { get; init; } = new();
    /// <summary>ZBs die per Push aus einem Sticky weggeschoben wurden.
    /// Direction = Schub-Richtung (= weg vom Schubser).</summary>
    public List<PushedZb> PendingPushedZbs { get; init; } = new();

    public GridState Clone() => new()
    {
        KnownGoalCells = new(KnownGoalCells),
        SwitchStateByCell = new(SwitchStateByCell),
        StickyTrappedByCell = new(StickyTrappedByCell),
        StickyEntryDirByCell = new(StickyEntryDirByCell),
        ParkedZbsByMachineIdx = ParkedZbsByMachineIdx.ToDictionary(kv => kv.Key, kv => new List<SimZb>(kv.Value)),
        PendingLiberatedZbs = new(PendingLiberatedZbs),
        PendingPushedZbs = new(PendingPushedZbs),
    };

    /// <summary>Wegpunkt-Kontext-Signatur: alles was den Weg eines ZB durchs
    /// Grid beeinflusst — Switch-Stellungen und welche Sticky-Cells belegt sind.
    /// Zwei States mit gleicher Signatur lassen jeden ZB exakt gleich laufen.</summary>
    public string WaypointContextSig()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (pos, idx) in SwitchStateByCell.OrderBy(kv => kv.Key))
            sb.Append(pos).Append(':').Append(idx).Append(',');
        sb.Append('|');
        foreach (var pos in StickyTrappedByCell.Keys.OrderBy(k => k))
            sb.Append(pos).Append(',');
        return sb.ToString();
    }
}

public sealed record LiberatedZb(SimZb Zb, int FromPos, Direction Direction);
public sealed record PushedZb(SimZb Zb, int FromPos, Direction Direction);
