using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Bubblewonder.Simulator;

namespace ZoombiniHelper.Tests;

public class BubblewonderSolverTests
{
    private static int Pos(int row, int col) => row * 13 + col;

    private static CellModel Trap() =>
        new(MechanismType.Trap, 0, new bool[4], null, 0, 0);

    // Ziel-Steinzelle (Typ 0x17). Seit dem Goal-Fix scort ein ZB NUR auf einer
    // solchen Zelle. Frühere Tests nutzten „läuft aus dem Gitter = Score" — das
    // war der Bug. Hier setzen wir die Ziel-Zelle explizit ans erwartete Pfad-Ende.
    private static CellModel Goal() =>
        new(MechanismType.Goal, 0, new bool[4], null, 0, 0);

    private static CellModel Conditional(int attrCode, int variant, Direction matchDir)
    {
        var dirs = new bool[4];
        dirs[(int)matchDir] = true;
        return new(MechanismType.Conditional, 0, dirs, matchDir, attrCode, variant);
    }

    private static CellModel Deflector(Direction dir) =>
        new(MechanismType.StaticDeflector, 0, CellModel.MakeFBits(dir), dir, 0, 0);
    private static CellModel Trigger(int channel) =>
        new(MechanismType.Trigger, channel, new bool[4], null, 0, 0);
    private static CellModel Sticky(int channel) =>
        new(MechanismType.Sticky, channel, new bool[4], null, 0, 0);
    private static CellModel Switch(int channel, params Direction[] active) =>
        new(MechanismType.SwitchActivated, channel, CellModel.MakeFBits(active), active[0], 0, 0);

    [Fact]
    public void Solve_NoMachines_ReturnsZero()
    {
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>(),
            Array.Empty<MachineModel>());
        var result = BubblewonderSolver.SolveBruteForce(grid, new[] { new SimZb(0, 1, 1, 1, 1) });
        Assert.Equal(0, result.Survivors);
    }

    [Fact]
    public void Solve_AllZbsScoreOnFreeGrid()
    {
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(11, 3)] = Goal(), [Pos(11, 9)] = Goal() },
            new[]
            {
                new MachineModel(0, Pos(0, 3), Direction.Down, false),
                new MachineModel(1, Pos(0, 9), Direction.Down, false),
            });
        var zbs = new[]
        {
            new SimZb(0x10, 1, 1, 1, 1),
            new SimZb(0x11, 2, 2, 2, 2),
            new SimZb(0x12, 3, 3, 3, 3),
        };
        var result = BubblewonderSolver.SolveBruteForce(grid, zbs);
        Assert.Equal(3, result.Survivors);
    }

    [Fact]
    public void Solve_BruteForce_AvoidsTrapByPickingRightMachine()
    {
        // Maschine 0 (Pos 0,3) führt zu Trap bei (3,3) — alle ZBs sterben.
        // Maschine 1 (Pos 0,9) ist sicher.
        // Brute-Force muss erkennen: alle 3 ZBs auf Maschine 1.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(3, 3)] = Trap(), [Pos(11, 9)] = Goal() },
            new[]
            {
                new MachineModel(0, Pos(0, 3), Direction.Down, false),
                new MachineModel(1, Pos(0, 9), Direction.Down, false),
            });
        var zbs = new[]
        {
            new SimZb(0x10, 1, 1, 1, 1),
            new SimZb(0x11, 2, 2, 2, 2),
            new SimZb(0x12, 3, 3, 3, 3),
        };
        var result = BubblewonderSolver.SolveBruteForce(grid, zbs);
        Assert.Equal(3, result.Survivors);
        Assert.All(result.Assignments, a => Assert.Equal(1, a.MachineIdx));
    }

    [Fact]
    public void Solve_BruteForce_OptimizesConditionalRouting()
    {
        // Conditional bei (3,5): wenn Hair=1 → wird umgelenkt nach Right (sicher).
        // Wenn nicht: läuft straight runter in Trap bei (5,5).
        // ZB-A (Hair=1) und ZB-B (Hair=2): nur einer (A) überlebt egal welche Maschine.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Conditional(attrCode: 1, variant: 1, matchDir: Direction.Right),
                [Pos(5, 5)] = Trap(),
                [Pos(3, 12)] = Goal(),   // gematchter ZB läuft nach rechts ins Ziel
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });

        var result = BubblewonderSolver.SolveBruteForce(grid, new[]
        {
            new SimZb(0xA, Hair: 1, Eyes: 0, Nose: 0, Feet: 0),
            new SimZb(0xB, Hair: 2, Eyes: 0, Nose: 0, Feet: 0),
        });
        Assert.Equal(1, result.Survivors);  // Nur A überlebt
    }

    [Fact]
    public void Solve_BruteForce_RejectsTooManyZbs()
    {
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>(),
            new[] { new MachineModel(0, 0, Direction.Down, false) });
        var zbs = Enumerable.Range(0, BubblewonderSolver.BruteForceMaxZbs + 1)
            .Select(i => new SimZb((ushort)i, 1, 1, 1, 1))
            .ToList();
        Assert.Throws<InvalidOperationException>(() =>
            BubblewonderSolver.SolveBruteForce(grid, zbs));
    }

    [Fact]
    public void EvaluateHeldZb_TrapMachineFlaggedAsBad_SafeMachineAsBest()
    {
        // Maschine 0 → Trap. Maschine 1 → sicher.
        // ZB hochgehoben + 2 weitere im Pool. Erwartung: Maschine 1 ist BESTE.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(3, 3)] = Trap(), [Pos(11, 9)] = Goal() },
            new[]
            {
                new MachineModel(0, Pos(0, 3), Direction.Down, false),
                new MachineModel(1, Pos(0, 9), Direction.Down, false),
            });
        var held = new SimZb(0xAA, 1, 1, 1, 1);
        var remaining = new[]
        {
            new SimZb(0x10, 1, 1, 1, 1),
            new SimZb(0x11, 2, 2, 2, 2),
        };

        var eval = BubblewonderSolver.EvaluateHeldZb(grid, held, remaining);

        Assert.Equal(2, eval.PerMachine.Count);
        var trapMachine = eval.PerMachine[0];
        Assert.Equal(SimOutcome.Dead, trapMachine.HeldOutcome);
        Assert.False(trapMachine.IsBest);
        var safeMachine = eval.PerMachine[1];
        Assert.Equal(SimOutcome.Scored, safeMachine.HeldOutcome);
        Assert.True(safeMachine.IsBest);
        Assert.Equal(3, eval.OverallMaxSurvivors);  // alle 3 retten
    }

    [Fact]
    public void EvaluateHeldZb_HeldDiesEitherWay_OthersSurvive()
    {
        // Conditional bei (3,5): Hair=1 → Umleitung Right (sicher raus).
        // Hair != 1 → straight Down → Trap (4,5) → tot.
        // Held hat Hair != 1 (stirbt egal über welche Maschine). Andere haben
        // Hair = 1 (überleben). Gesamt: held tot, 2 retten = 2/3.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Conditional(attrCode: 1, variant: 1, matchDir: Direction.Right),
                [Pos(4, 5)] = Trap(),
                [Pos(3, 12)] = Goal(),   // Hair=1-ZBs laufen nach rechts ins Ziel
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });
        var held = new SimZb(0xAA, Hair: 2, Eyes: 0, Nose: 0, Feet: 0);
        var remaining = new[]
        {
            new SimZb(0x10, Hair: 1, Eyes: 0, Nose: 0, Feet: 0),
            new SimZb(0x11, Hair: 1, Eyes: 0, Nose: 0, Feet: 0),
        };

        var eval = BubblewonderSolver.EvaluateHeldZb(grid, held, remaining);

        Assert.Equal(SimOutcome.Dead, eval.PerMachine[0].HeldOutcome);
        Assert.Equal(2, eval.PerMachine[0].FollowupSurvivors);
        Assert.Equal(2, eval.OverallMaxSurvivors);  // 2 von 3
        // KeepIsBest = false weil "schicken" und "zurücklegen" beide 2/3 ergeben
        // (held stirbt sowieso). Aber Engagement: held wegwerfen ist semantisch ok.
    }

    [Fact]
    public void Solve_Greedy_HandlesLargeZbCount()
    {
        // 16 ZBs, 2 Maschinen — Greedy soll terminieren und alle assignen.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(11, 3)] = Goal(), [Pos(11, 9)] = Goal() },
            new[]
            {
                new MachineModel(0, Pos(0, 3), Direction.Down, false),
                new MachineModel(1, Pos(0, 9), Direction.Down, false),
            });
        var zbs = Enumerable.Range(0, 16)
            .Select(i => new SimZb((ushort)i, 1, 1, 1, 1))
            .ToList();
        var result = BubblewonderSolver.SolveGreedy(grid, zbs);
        Assert.Equal(16, result.Assignments.Count);
        Assert.Equal(16, result.Survivors);
    }

    [Fact]
    public void SolveDfs_PopulatesProgress_AndRespectsBudget()
    {
        // Suche mit Fortschritts-Objekt + endlichem Budget: muss terminieren,
        // Knoten zählen und eine Survivor-Zahl liefern (Anti-Hang-Sicherung).
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(11, 3)] = Goal(), [Pos(11, 9)] = Goal() },
            new[]
            {
                new MachineModel(0, Pos(0, 3), Direction.Down, false),
                new MachineModel(1, Pos(0, 9), Direction.Down, false),
            });
        var zbs = Enumerable.Range(0, 6)
            .Select(i => new SimZb((ushort)i, (byte)(i % 5 + 1), 1, 1, 1)).ToList();
        var progress = new SolverProgress();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = BubblewonderSolver.SolveDfs(
            grid, zbs, TimeSpan.FromSeconds(5), progress: progress);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"lief {sw.Elapsed.TotalSeconds:F1}s");
        Assert.True(progress.Nodes > 0, "Knoten-Zähler wurde nicht hochgezählt");
        Assert.True(progress.BestSurvivors >= 0);
        Assert.NotNull(result);
        // Regulär abgeschlossene Suche: Fraction muss (bis auf FP-Rauschen) 1.0
        // erreichen, damit die UI 100 % anzeigen kann. Die hochgerechnete
        // Gesamt-Knotenzahl muss dann ungefähr den besuchten Knoten entsprechen.
        Assert.Contains("optimal", result.Strategy);
        Assert.True(progress.Fraction > 0.999 && progress.Fraction <= 1.0001,
            $"Fraction erreichte nicht 1.0: {progress.Fraction}");
        Assert.True(progress.EstimatedTotalNodes >= progress.Nodes,
            $"Schätzung {progress.EstimatedTotalNodes} < besucht {progress.Nodes}");
        // Grober Fortschritt: Invariante done <= total, RootPercent in [0,100].
        // (Ist die Greedy-Lösung bereits optimal, pruned die Wurzel sofort und
        // baut nie Optionen auf → Total bleibt 0; dann ist die Suche ohnehin in
        // <1 ms fertig und der Lauf-Status wird nie angezeigt.)
        Assert.True(progress.RootBranchesDone <= progress.RootBranchesTotal,
            $"done {progress.RootBranchesDone} > total {progress.RootBranchesTotal}");
        Assert.InRange(progress.RootPercent, 0.0, 100.0);
        if (progress.RootBranchesTotal > 0)
            Assert.Equal(progress.RootBranchesTotal, progress.RootBranchesDone);
    }

    [Fact]
    public void SolveDfs_Timeout_FractionStaysBelowOne()
    {
        // Bei Abbruch durch Cancellation darf Fraction NICHT 1.0 melden — die UI
        // soll ehrlich zeigen, dass nur ein Teil des Suchbaums abgesucht wurde.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(11, 3)] = Goal(), [Pos(11, 9)] = Goal() },
            new[]
            {
                new MachineModel(0, Pos(0, 3), Direction.Down, false),
                new MachineModel(1, Pos(0, 9), Direction.Down, false),
            });
        var zbs = Enumerable.Range(0, 12)
            .Select(i => new SimZb((ushort)i, (byte)(i % 5 + 1), 1, 1, 1)).ToList();
        var progress = new SolverProgress();
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();  // sofort abbrechen

        var result = BubblewonderSolver.SolveDfs(
            grid, zbs, TimeSpan.FromSeconds(5), cts.Token, progress: progress);

        Assert.True(progress.Fraction < 1.0,
            $"Fraction sollte bei Abbruch < 1.0 sein, war {progress.Fraction}");
    }

    [Fact]
    public void SolveDfs_Hard16ZbLowBest_TerminatesQuickly()
    {
        // Worst Case fürs Pruning: best bleibt niedrig → die obere Schranke greift
        // kaum. 16 ZBs (über Conditional in 5 unterscheidbare Klassen), 2 Haupt-
        // + 1 Insel-Maschine, Park→Relaunch möglich, KEIN erreichbares Ziel → best=0.
        // Vor dem StoneArea→Dead-Fix wäre das endlos (offene, ungetrackte Re-Launches).
        var island = new CellModel(MechanismType.Passthrough, 0, new bool[4], null, 0, 0,
            IsIslandMachine: true, MachineIdx: 2);
        var cells = new Dictionary<int, CellModel>
        {
            [Pos(5, 3)] = island,                                  // Haupt-0 parkt hier
            [Pos(3, 9)] = Conditional(attrCode: 1, variant: 1, matchDir: Direction.Right),
            [Pos(1, 9)] = new CellModel(MechanismType.Trigger, 1, new bool[4], null, 0, 0),
            [Pos(2, 9)] = new CellModel(MechanismType.SwitchActivated, 1,
                CellModel.MakeFBits(Direction.Down, Direction.Right), Direction.Down, 0, 0),
        };
        var grid = new BubblewonderGridModel(cells, new[]
        {
            new MachineModel(0, Pos(0, 3), Direction.Down, false),
            new MachineModel(1, Pos(0, 9), Direction.Down, false),
            new MachineModel(2, Pos(5, 3), Direction.Down, true),
        });
        var zbs = Enumerable.Range(0, 16)
            .Select(i => new SimZb((ushort)i, (byte)(i % 5 + 1), 1, 1, 1)).ToList();
        var progress = new SolverProgress();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = BubblewonderSolver.SolveDfs(
            grid, zbs, TimeSpan.FromMinutes(2), progress: progress);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"DFS zu langsam: {sw.Elapsed.TotalSeconds:F1}s, {progress.Nodes:N0} Knoten");
        // Kein Ziel erreichbar → der Reachability-Soundness-Gate beweist das Board IM
        // MODELL unlösbar und gibt SOFORT 0 zurück (statt den nicht-prunebaren Suchraum
        // bis zum Zeitlimit zu durchforsten). Survivors=0, Befund klar als Modell-Befund
        // markiert. (Vorher: „DFS (optimal)" nach längerer Suche.)
        Assert.Equal(0, result.Survivors);
        Assert.Contains("kein Ziel erreichbar im Modell", result.Strategy);
        Assert.Equal(0, progress.Nodes);  // gar nicht erst in den DFS eingestiegen
    }

    [Fact]
    public void SolveDfs_UnreachableGoalConfig_TerminatesFast_NotTimeLimit()
    {
        // REPRODUKTION des live „0 rettbar (Zeitlimit), memo≈38k"-Hängers (plan-log
        // 21:12:38): Die Goal-Maschine M0 trifft Switch S, der nur in state1=Down zum
        // Ziel führt — S sitzt aber in Kanal 99, für den es KEINEN Trigger gibt → S
        // kippt nie → Ziel ist NIE erreichbar. scorableSigs hält die sig dennoch für
        // scorbar (es probiert ALLE Switch-Stellungen, ignoriert deren Erreichbarkeit)
        // → bei best=0 senkt die obere Schranke nichts → ohne Gate würfe der DFS 60s
        // lang durch die Decoy-Switch-Kombinationen, fände nie etwas und meldete
        // „0 rettbar (Zeitlimit)". Mit dem Reachability-Gate: SOFORT 0 + Modell-Befund.
        var cells = new Dictionary<int, CellModel>
        {
            [Pos(4,6)] = Switch(99, Direction.Left, Direction.Down),  // ch99: kein Trigger
            [Pos(4,5)] = Trap(),                                      // S=Left → Falle
            [Pos(11,6)] = Goal(),                                     // nur via S=Down erreichbar
        };
        var state = new GridState();
        state.SwitchStateByCell[Pos(4,6)] = 0;  // S start Left (zu)
        var machines = new List<MachineModel> { new(0, Pos(0,6), Direction.Down, false) };
        // Decoy-Switches MIT Trigger (blähen den Suchraum, gaten aber nichts).
        for (int d = 0; d < 8; d++)
        {
            int col = d < 6 ? d : d + 1;  // Spalten ≠ 6
            machines.Add(new MachineModel(d + 1, Pos(0, col), Direction.Down, false));
            cells[Pos(2, col)] = Trigger(20 + d);
            cells[Pos(6, col)] = Switch(20 + d, Direction.Down, Direction.Up);
            cells[Pos(9, col)] = Trap();
            state.SwitchStateByCell[Pos(6, col)] = 0;
        }
        var grid = new BubblewonderGridModel(cells, machines.ToArray(), state);
        var zbs = Enumerable.Range(0, 16).Select(i => new SimZb((ushort)i, 1, 1, 1, 1)).ToList();
        var progress = new SolverProgress();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = BubblewonderSolver.SolveDfs(grid, zbs, TimeSpan.FromSeconds(60), progress: progress);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"DFS hing (kein Gate?): {sw.Elapsed.TotalSeconds:F1}s, {progress.Nodes:N0} Knoten");
        Assert.Equal(0, result.Survivors);
        Assert.Contains("kein Ziel erreichbar im Modell", result.Strategy);
        Assert.DoesNotContain("Zeitlimit", result.Strategy);
        Assert.DoesNotContain("time-limit", result.Strategy);
    }

    [Fact]
    public void SolveDfs_DoesNotFalselyGate_WhenGoalReachableViaSwitchFlip()
    {
        // Gegenprobe zum Gate: Hier IST das Ziel erreichbar — aber erst nachdem ein
        // Setup-ZB über einen Trigger Switch S umlegt (und dabei stirbt). greedy=0,
        // aber reachability findet einen scorenden Zustand → das Gate darf NICHT
        // greifen, der Solver muss die positive Lösung liefern (nicht fälschlich 0).
        var cells = new Dictionary<int, CellModel>
        {
            [Pos(3,5)] = Switch(1, Direction.Left, Direction.Down),  // state0→Falle, state1→Ziel
            [Pos(3,4)] = Trap(),
            [Pos(5,5)] = Goal(),
            [Pos(2,0)] = Trigger(1),   // flippt S
            [Pos(5,0)] = Trap(),       // Setup-ZB stirbt
        };
        var state = new GridState();
        state.SwitchStateByCell[Pos(3,5)] = 0;
        var grid = new BubblewonderGridModel(cells,
            new[]
            {
                new MachineModel(0, Pos(0,5), Direction.Down, false),  // scoring (gated)
                new MachineModel(1, Pos(0,0), Direction.Down, false),  // setup
            }, state);
        var zbs = Enumerable.Range(0, 5).Select(i => new SimZb((ushort)i, 1, 1, 1, 1)).ToList();

        var result = BubblewonderSolver.SolveDfs(grid, zbs, TimeSpan.FromSeconds(10));

        Assert.DoesNotContain("kein Ziel erreichbar", result.Strategy);  // Gate darf NICHT greifen
        Assert.True(result.Survivors > 0, $"Solver fand keine Lösung (Survivors={result.Survivors})");
    }

    [Fact]
    public void Dfs_parks_then_relaunches_island_zb()
    {
        // ZB kann NUR scoren, wenn er erst auf der Insel geparkt und DANN
        // über die Insel-Maschine wieder losgeschickt wird. Der direkte Weg
        // (Hauptmaschine) endet zwangsläufig auf der Insel-Cell (= Parken);
        // erst der zweite Zug (Insel-Maschine, Down) bringt ihn aus dem Grid.
        // Vor dem Fix vergaß der DFS geparkte ZBs → Survivors=0.
        var islandCell = new CellModel(
            MechanismType.Passthrough, 0, new bool[4], null, 0, 0,
            IsIslandMachine: true, MachineIdx: 1);
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(2, 5)] = islandCell, [Pos(11, 5)] = Goal() },
            new[]
            {
                new MachineModel(0, Pos(0, 5), Direction.Down, false),  // Haupt
                new MachineModel(1, Pos(2, 5), Direction.Down, true),   // Insel
            });
        var zbs = new[] { new SimZb(0xA, 1, 1, 1, 1) };

        var result = BubblewonderSolver.SolveDfs(grid, zbs, TimeSpan.FromSeconds(5));

        Assert.Equal(1, result.Survivors);             // park + relaunch gefunden
        Assert.Equal(2, result.Assignments.Count);     // zwei Züge nötig
        Assert.Equal(0, result.Assignments[0].MachineIdx);  // erst Haupt (parkt)
        Assert.Equal(1, result.Assignments[1].MachineIdx);  // dann Insel (scored)
    }

    [Fact]
    public void Dfs_SolvesParkedZbs_WhenMainPoolEmpty()
    {
        // Gemeldeter Bug (2026-05-29): User folgte dem Plan, am Ende sind ALLE ZBs
        // auf Inseln (Hauptpool leer). Der Solver brach mit „0" ab (early-return bei
        // zbs.Count==0), obwohl die geparkten ZBs über ihre Insel-Maschine
        // re-launchbar und rettbar sind. Hier: 2 ZBs auf der Insel, Pool leer →
        // beide müssen über die Insel-Maschine (Down → Ziel) gerettet werden.
        var islandCell = new CellModel(
            MechanismType.Passthrough, 0, new bool[4], null, 0, 0,
            IsIslandMachine: true, MachineIdx: 1);
        var state = new GridState();
        state.ParkedZbsByMachineIdx[1] = new List<SimZb>
        {
            new(0xA, 1, 1, 1, 1), new(0xB, 2, 2, 2, 2),
        };
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(5, 5)] = islandCell, [Pos(11, 5)] = Goal() },
            new[]
            {
                new MachineModel(0, Pos(0, 5), Direction.Down, false),  // Haupt (ungenutzt)
                new MachineModel(1, Pos(5, 5), Direction.Down, true),   // Insel → Down → Ziel
            },
            state);

        // Hauptpool LEER — nur die geparkten ZBs sind übrig.
        var result = BubblewonderSolver.SolveDfs(grid, Array.Empty<SimZb>(), TimeSpan.FromSeconds(5));

        Assert.Equal(2, result.Survivors);   // beide Insel-ZBs gerettet (vorher: 0)
    }

    [Fact]
    public void Dfs_RescuesTrappedStickyZb_WhenFreedByTrigger()
    {
        // Ein ZB klebt in einer Klebefalle (Sticky) fest. Ein Pool-ZB läuft durch
        // einen Trigger im selben Kanal → befreit den Gefangenen, der dann in seiner
        // Eintrittsrichtung weiterläuft und scort. Der Solver muss BEIDE als gerettet
        // zählen — auch den vorher gefangenen (der nicht im normalen Pool ist).
        // (Gemeldet 2026-05-29: gefangene ZBs wurden „vergessen".)
        var state = new GridState();
        // Sticky bei (3,5), Kanal 1, belegt mit ZB-A (echte Attribute, Eintritt Down).
        state.StickyTrappedByCell[Pos(3, 5)] = new SimZb(0xA, 1, 1, 1, 1);
        state.StickyEntryDirByCell[Pos(3, 5)] = Direction.Down;
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(3, 5)] = Sticky(channel: 1),
                [Pos(5, 0)] = Trigger(channel: 1),   // befreit Kanal 1
                [Pos(11, 5)] = Goal(),               // befreiter ZB-A läuft Down ins Ziel
                [Pos(11, 0)] = Goal(),               // Pool-ZB-B läuft Down ins Ziel
            },
            new[]
            {
                new MachineModel(0, Pos(0, 0), Direction.Down, false),  // ZB-B → Trigger(5,0)
                new MachineModel(1, Pos(0, 5), Direction.Down, false),
            },
            state);
        // Hauptpool: nur ZB-B. ZB-A ist gefangen (im GridState, nicht im Pool).
        var zbs = new[] { new SimZb(0xB, 1, 1, 1, 1) };

        var result = BubblewonderSolver.SolveDfs(grid, zbs, TimeSpan.FromSeconds(5));

        // ZB-B scort (läuft Down über Trigger nach (11,0)), befreit dabei ZB-A,
        // der Down nach (11,5) scort → 2 Survivors (vorher: nur 1, ZB-A vergessen).
        Assert.Equal(2, result.Survivors);
    }

    [Fact]
    public void ResolveSpawnDirection_CountMismatch_UsesNearestSpriteDirection()
    {
        // REGS 16606 (memdump-162215): DetectMachines findet 3 Objekte (alle mit Müll-
        // TargetIdx → „Insel"), aber nur 2 Spawn-Zellen → dirByCell bleibt leer. Der
        // (2,8)-Werfer ist das Sprite (2,10)←Left → Spawn-Richtung muss Left sein, NICHT
        // Down (sonst „runter→tot" im Modell, real „links→Insel" → erster Plan falsch).
        var emptyDir = new Dictionary<int, Direction>();
        var placements = new List<BubblewonderGridModelBuilder.MachinePlacement>
        {
            new(0x14, Pos(2, 10), Direction.Left, true, 178, 292, 19167),
            new(0x15, Pos(2, 11), Direction.Down, true, 193, 309, 211),
            new(0x16, Pos(1, 0),  Direction.Down, true,  90,  93, 8323),
        };
        Assert.Equal(Direction.Left,
            BubblewonderGridModelBuilder.ResolveSpawnDirection(Pos(2, 8), emptyDir, placements));

        // dirByCell kennt die Zelle → die korrekt zugeordnete Richtung gewinnt (kein Fallback).
        var withDir = new Dictionary<int, Direction> { [Pos(2, 8)] = Direction.Up };
        Assert.Equal(Direction.Up,
            BubblewonderGridModelBuilder.ResolveSpawnDirection(Pos(2, 8), withDir, placements));

        // Keine Maschinen erkannt → bleibt beim konservativen Down-Default.
        Assert.Equal(Direction.Down,
            BubblewonderGridModelBuilder.ResolveSpawnDirection(
                Pos(2, 8), emptyDir, new List<BubblewonderGridModelBuilder.MachinePlacement>()));
    }

    [Fact]
    public void PlanSignature_IgnoresIslandMachineCellSwitchState()
    {
        // REGRESSION (2026-06-04, memdump-144039): die Insel-Maschinen-Zelle (10,9) ist
        // gleichzeitig Switch (f0=4) UND Insel-Maschine. Ihr +0x7C ändert sich durch
        // Insel-AKTIVITÄT (ZB kommt an / wird re-gelauncht), NICHT durch einen vom
        // Spieler ausgelösten Trigger. Im Plan-Stabilitäts-Vergleich erzeugte dieser Wert
        // „Switch-Wechsel" ohne echte Schaltung → Dauer-„Abweichung", obwohl der Spieler
        // nur Deflektoren durchlief. Die PLAN-Signatur muss die Insel-Zelle daher ignorieren.
        var islandSwitch = Switch(4, Direction.Left, Direction.Up)
            with { IsIslandMachine = true, MachineIdx = 1 };
        var cells = new Dictionary<int, CellModel>
        {
            [Pos(10, 9)] = islandSwitch,                 // Insel-Maschine UND Switch
            [Pos(3, 5)] = Switch(1, Direction.Left, Direction.Down),  // normaler Switch
        };
        var machines = new[]
        {
            new MachineModel(0, Pos(0, 0), Direction.Down, false),
            new MachineModel(1, Pos(10, 9), Direction.Left, true),   // Insel
        };
        var empty = System.Array.Empty<SimZb>();

        // Zwei States, die sich NUR im Switch-Wert der Insel-Zelle (10,9) unterscheiden.
        var sA = new GridState();
        sA.SwitchStateByCell[Pos(10, 9)] = 0;
        sA.SwitchStateByCell[Pos(3, 5)] = 1;
        var sB = new GridState();
        sB.SwitchStateByCell[Pos(10, 9)] = 3;   // Insel-Zelle anders …
        sB.SwitchStateByCell[Pos(3, 5)] = 1;    // … normaler Switch gleich
        Assert.Equal(
            BubblewonderSolver.DebugStateSignature(
                new BubblewonderGridModel(cells, machines, sA), empty),
            BubblewonderSolver.DebugStateSignature(
                new BubblewonderGridModel(cells, machines, sB), empty));

        // Aber ein ECHTER Switch-Wechsel (3,5) bleibt eine Abweichung.
        var sC = new GridState();
        sC.SwitchStateByCell[Pos(10, 9)] = 0;
        sC.SwitchStateByCell[Pos(3, 5)] = 0;    // normaler Switch anders
        Assert.NotEqual(
            BubblewonderSolver.DebugStateSignature(
                new BubblewonderGridModel(cells, machines, sA), empty),
            BubblewonderSolver.DebugStateSignature(
                new BubblewonderGridModel(cells, machines, sC), empty));
    }

    [Fact]
    public void Signature_TrappedZb_IsPositionBased_StubMatchesRealAttributes()
    {
        // REGRESSION (2026-06-03, plan-log 15:51:59): ein live gelesener gefangener ZB ist
        // attributlos (Stub (0,0,0,0) — die Sticky-Zelle hält nur einen Engine-Handle), die
        // Vorwärts-Simulation legt ihn aber mit ECHTEN Attributen ab. Attribut-basiert matchte
        // die F-Komponente der Signatur daher NIE, sobald ein ZB klebte → Dauer-„Abweichung".
        // Die Signatur muss Klebefallen POSITIONS-basiert führen: Stub und echter ZB an
        // DERSELBEN Zelle → GLEICHE Signatur.
        var cells = new Dictionary<int, CellModel> { [Pos(3, 5)] = Sticky(channel: 1) };
        var machines = new[] { new MachineModel(0, Pos(0, 0), Direction.Down, false) };

        var stubState = new GridState();
        stubState.StickyTrappedByCell[Pos(3, 5)] = new SimZb(0xA, 0, 0, 0, 0);   // live: Stub
        var stubGrid = new BubblewonderGridModel(cells, machines, stubState);

        var realState = new GridState();
        realState.StickyTrappedByCell[Pos(3, 5)] = new SimZb(0xA, 5, 4, 2, 5);   // sim: echt
        var realGrid = new BubblewonderGridModel(cells, machines, realState);

        var empty = System.Array.Empty<SimZb>();
        Assert.Equal(
            BubblewonderSolver.DebugStateSignature(stubGrid, empty),
            BubblewonderSolver.DebugStateSignature(realGrid, empty));

        // Aber eine ANDERE belegte Zelle bleibt eine echte Abweichung (Position zählt).
        var otherState = new GridState();
        otherState.StickyTrappedByCell[Pos(7, 7)] = new SimZb(0xA, 5, 4, 2, 5);
        var otherGrid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(7, 7)] = Sticky(channel: 1) }, machines, otherState);
        Assert.NotEqual(
            BubblewonderSolver.DebugStateSignature(stubGrid, empty),
            BubblewonderSolver.DebugStateSignature(otherGrid, empty));
    }

    [Fact]
    public void WithStickyAttributes_SetsEntryDirFromMovementField()
    {
        // Eintrittsrichtung aus +0x58 (PoolMember.MovementDirRaw), disassembly-
        // verifiziert: 0=Left,1=Down,2=Right,3=Up. Ein nach LEFT (raw 0) in die
        // Falle gelaufener ZB muss als StickyEntryDir = Left geführt werden — NICHT
        // der alte Default Down. (raw 3 = Up als zweiter Check.)
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(3, 5)] = Sticky(1), [Pos(7, 2)] = Sticky(1) },
            new[] { new MachineModel(0, Pos(0, 0), Direction.Down, false) });
        // Festklebende ZBs = losgeschickt (Handle 0x04008001), Grid-Pos auf Sticky-Zelle.
        var pool = new List<PoolMember>
        {
            new() { HeaderId = 0xA, Hair = 1, Eyes = 1, Nose = 1, Feet = 1,
                    Handle = 0x04008001, GridRow = 3, GridCol = 5, MovementDirRaw = 0 }, // Left
            new() { HeaderId = 0xB, Hair = 1, Eyes = 1, Nose = 1, Feet = 1,
                    Handle = 0x04008001, GridRow = 7, GridCol = 2, MovementDirRaw = 3 }, // Up
        };

        var g = BubblewonderGridModelBuilder.WithStickyAttributes(grid, pool);

        Assert.Equal(Direction.Left, g.State.StickyEntryDirByCell[Pos(3, 5)]);
        Assert.Equal(Direction.Up, g.State.StickyEntryDirByCell[Pos(7, 2)]);
    }

    [Fact]
    public void WithStickyAttributes_ReplacesStubWithRealAttrs()
    {
        // Der gefangene ZB wird über seine GRID-POSITION (auf der Sticky-Zelle)
        // gematcht, nicht über HeaderId (der Sticky speichert einen Handle ≠ hdr1A).
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel> { [Pos(3, 5)] = Sticky(1) },
            new[] { new MachineModel(0, Pos(0, 0), Direction.Down, false) });
        var pool = new List<PoolMember>
        {
            new() { HeaderId = 0xA, Hair = 3, Eyes = 4, Nose = 2, Feet = 1,
                    Handle = 0x04008001, GridRow = 3, GridCol = 5 },
        };

        var fixedGrid = BubblewonderGridModelBuilder.WithStickyAttributes(grid, pool);

        var zb = fixedGrid.State.StickyTrappedByCell[Pos(3, 5)];
        Assert.Equal((3, 4, 2, 1), (zb.Hair, zb.Eyes, zb.Nose, zb.Feet));
    }

    [Fact]
    public void LocateOnPlan_TracksProgressAndDetectsDeviation()
    {
        // Plan-Stabilität: Grid mit Conditional auf Haar (macht Haar routing-RELEVANT,
        // sonst sind alle ZBs equivalent/ununterscheidbar). matchDir=Down = gleiche
        // Richtung wie die Maschine → alle ZBs scoren trotzdem über M0 (Down → Ziel),
        // aber sie sind über ihr Haar unterscheidbar.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(5, 5)] = Conditional(attrCode: 1, variant: 1, matchDir: Direction.Down),
                [Pos(11, 5)] = Goal(),
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });
        var zbs = new[]
        {
            new SimZb(0xA, 1, 2, 2, 2), new SimZb(0xB, 2, 2, 2, 2), new SimZb(0xC, 3, 2, 2, 2),
        };
        var plan = BubblewonderSolver.SolveBruteForce(grid, zbs).Assignments;
        Assert.Equal(3, plan.Count);

        // Ausgangszustand → Schritt 0.
        Assert.Equal(0, BubblewonderSolver.LocateOnPlan(grid, zbs, plan, grid, zbs));

        // Nach dem ersten Plan-Zug (ZB plan[0] gescort, raus aus Pool) → Schritt 1.
        var afterFirst = zbs.Where(z => z.HeaderId != plan[0].Zb.HeaderId).ToArray();
        Assert.Equal(1, BubblewonderSolver.LocateOnPlan(grid, zbs, plan, grid, afterFirst));

        // Abweichung: ein Pool mit einem ZB einer Haar-Klasse (5), die im Plan nie
        // vorkommt → auf keinem Plan-Schritt → null (= Aufrufer rechnet neu).
        var deviated = new[] { new SimZb(0xA, 1, 2, 2, 2), new SimZb(0x99, 5, 2, 2, 2) };
        Assert.Null(BubblewonderSolver.LocateOnPlan(grid, zbs, plan, grid, deviated));
    }

    [Fact]
    public void LocateOnPlan_ZbInTransitToIsland_IsNotDeviation()
    {
        // Plan-Stabilität bei ZB UNTERWEGS: ein ZB wurde losgeschickt und läuft gerade
        // zur Insel. Er ist aus dem Pool raus, aber Insel zählt erst beim Ankommen +1.
        // Dieser Transit-Zustand darf NICHT als Abweichung gemeldet werden.
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(5, 5)] = Conditional(attrCode: 1, variant: 1, matchDir: Direction.Down),
                [Pos(11, 5)] = Goal(),
            },
            new[] { new MachineModel(0, Pos(0, 5), Direction.Down, false) });
        var zbs = new[]
        {
            new SimZb(0xA, 1, 2, 2, 2), new SimZb(0xB, 2, 2, 2, 2), new SimZb(0xC, 3, 2, 2, 2),
        };
        var plan = BubblewonderSolver.SolveBruteForce(grid, zbs).Assignments;
        Assert.Equal(3, plan.Count);

        // Realer Transit: ZB plan[0] ist losgeschickt (aus Pool raus), aber noch unterwegs
        // — Grid-State (Insel/Falle/Switches) ist UNVERÄNDERT. = Pool minus plan[0], sonst base.
        var inTransit = zbs.Where(z => z.HeaderId != plan[0].Zb.HeaderId).ToArray();
        var step = BubblewonderSolver.LocateOnPlan(grid, zbs, plan, grid, inTransit);
        Assert.NotNull(step);   // KEINE Abweichung — der ZB folgt dem Plan (unterwegs).
    }

    [Fact]
    public void LocateOnPlan_RecognizesOwnStart_WithIslandAndTrap()
    {
        // Reproduziert den live beobachteten Bug (2026-05-29): Pool leer, ZBs auf
        // Insel + in Klebefalle, Zustand KONSTANT. Der Plan wurde auf genau diesem
        // Zustand berechnet → LocateOnPlan(base, base) MUSS Schritt 0 erkennen
        // (nicht „Abweichung"). Tat es live NICHT → 14ms nach Plan-Übernahme „Abweichung".
        var islandCell = new CellModel(MechanismType.Passthrough, 0, new bool[4], null, 0, 0,
            IsIslandMachine: true, MachineIdx: 1);
        var state = new GridState();
        state.ParkedZbsByMachineIdx[1] = new List<SimZb> { new(0xA, 1, 1, 1, 1), new(0xB, 2, 2, 2, 2) };
        state.StickyTrappedByCell[Pos(3, 5)] = new SimZb(0xC, 3, 3, 3, 3);
        state.StickyEntryDirByCell[Pos(3, 5)] = Direction.Down;
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(5, 5)] = islandCell, [Pos(3, 5)] = Sticky(1), [Pos(11, 5)] = Goal(),
            },
            new[]
            {
                new MachineModel(0, Pos(0, 5), Direction.Down, false),
                new MachineModel(1, Pos(5, 5), Direction.Down, true),
            },
            state);
        var emptyPool = Array.Empty<SimZb>();
        var plan = BubblewonderSolver.SolveDfs(grid, emptyPool, TimeSpan.FromSeconds(5)).Assignments;

        // base == current (identischer Zustand) → muss auf dem Plan liegen (nicht null).
        var step = BubblewonderSolver.LocateOnPlan(grid, emptyPool, plan, grid, emptyPool);
        Assert.NotNull(step);
    }

    [Fact]
    public void Dfs_terminates_on_island_park_loop()
    {
        // Park-Loop: Insel-Maschine bei (5,5) schickt den ZB Up nach (4,5),
        // dort lenkt ein Deflector ihn Down zurück auf (5,5) = wieder geparkt.
        // Ohne Zyklen-Schutz würde der DFS endlos park→losschicken→park
        // rekursieren. Erwartung: terminiert schnell, ZB überlebt nicht.
        var islandCell = new CellModel(
            MechanismType.Passthrough, 0, new bool[4], null, 0, 0,
            IsIslandMachine: true, MachineIdx: 0);
        var deflectorDown = new CellModel(
            MechanismType.StaticDeflector, 0, new bool[4], Direction.Down, 0, 0);
        var state = new GridState();
        state.ParkedZbsByMachineIdx[0] = new List<SimZb> { new(0xB, 1, 1, 1, 1) };
        var grid = new BubblewonderGridModel(
            new Dictionary<int, CellModel>
            {
                [Pos(5, 5)] = islandCell,
                [Pos(4, 5)] = deflectorDown,
            },
            new[] { new MachineModel(0, Pos(5, 5), Direction.Up, true) },
            state);
        var zbs = new[] { new SimZb(0xB, 1, 1, 1, 1) };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = BubblewonderSolver.SolveDfs(grid, zbs, TimeSpan.FromSeconds(5));
        sw.Stop();

        Assert.Equal(0, result.Survivors);          // kein Weg raus
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"Zyklen-Schutz griff nicht — lief {sw.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>Echtes Layout REGS 16608 (aus memdump-074739 rekonstruiert): das
    /// Ziel (unten-links) ist aus dem Kaltstart NICHT erreichbar — jeder ZB stirbt
    /// (Greedy=0). Eine Lösung existiert aber, sobald per Trigger Switch (1,7) auf
    /// „Down" gekippt wird. Regression: der config-gezielte Beam muss eine positive
    /// Lösung finden (sonst hängt der DFS bei 0/16 bis zum Zeitlimit).</summary>
    private static (BubblewonderGridModel Grid, SimZb[] Zbs) Build16608()
    {
        var cells = new Dictionary<int, CellModel>
        {
            [Pos(10,0)] = Goal(), [Pos(10,1)] = Goal(), [Pos(10,2)] = Goal(), [Pos(11,2)] = Goal(),
            [Pos(5,2)] = Trap(), [Pos(1,3)] = Trap(), [Pos(0,7)] = Trap(), [Pos(8,9)] = Trap(), [Pos(5,11)] = Trap(),
            [Pos(1,4)] = Conditional(4,2,Direction.Down),
            [Pos(3,9)] = Conditional(4,5,Direction.Left),
            [Pos(5,5)] = Conditional(1,1,Direction.Up),
            [Pos(5,7)] = Conditional(4,3,Direction.Down),
            [Pos(5,10)] = Conditional(4,5,Direction.Right),
            [Pos(7,2)] = Conditional(1,5,Direction.Up),
            [Pos(7,4)] = Conditional(1,4,Direction.Down),
            [Pos(7,5)] = Conditional(2,2,Direction.Down),
            [Pos(10,8)] = Conditional(4,5,Direction.Up),
            [Pos(3,1)] = Deflector(Direction.Down), [Pos(7,1)] = Deflector(Direction.Down),
            [Pos(9,1)] = Deflector(Direction.Down), [Pos(5,4)] = Deflector(Direction.Left),
            [Pos(9,4)] = Deflector(Direction.Left), [Pos(9,5)] = Deflector(Direction.Left),
            [Pos(11,6)] = Deflector(Direction.Left), [Pos(7,7)] = Deflector(Direction.Left),
            [Pos(5,8)] = Deflector(Direction.Right), [Pos(2,9)] = Deflector(Direction.Down),
            [Pos(7,9)] = Deflector(Direction.Left), [Pos(7,10)] = Deflector(Direction.Right),
            // Stein-Insel am rechten unteren Eck: im Live-Dump enden M1-ZBs bei (10,11)
            // mit „Dead" (keine Falle, kein Rand) ⇒ dort liegt eine StoneArea-Zelle.
            // Mit dem Insel-Park-Fix landen ZBs hier statt zu sterben.
            [Pos(10,11)] = new CellModel(MechanismType.StoneArea, 0, new bool[4], null, 0, 0),
            [Pos(1,5)] = Trigger(4), [Pos(8,4)] = Trigger(5), [Pos(8,5)] = Trigger(6), [Pos(10,7)] = Trigger(7),
            [Pos(3,4)] = Sticky(5), [Pos(11,5)] = Sticky(5), [Pos(10,5)] = Sticky(6),
            [Pos(7,11)] = Switch(4, Direction.Down, Direction.Up),
            [Pos(10,9)] = Switch(4, Direction.Left, Direction.Up) with { IsIslandMachine = true, MachineIdx = 2 },
            [Pos(2,7)] = Switch(6, Direction.Down, Direction.Right),
            [Pos(1,7)] = Switch(7, Direction.Left, Direction.Down, Direction.Up),
            [Pos(10,6)] = Switch(1, Direction.Left, Direction.Down),
        };
        var state = new GridState();
        // Initiale Switch-States (F-Bit-Index) aus der SIM-TRACE.
        state.SwitchStateByCell[Pos(7,11)] = 1;
        state.SwitchStateByCell[Pos(10,9)] = 3;
        state.SwitchStateByCell[Pos(2,7)] = 1;
        state.SwitchStateByCell[Pos(1,7)] = 0;
        state.SwitchStateByCell[Pos(10,6)] = 0;
        var machines = new[]
        {
            new MachineModel(0, Pos(1,8), Direction.Left, false),
            new MachineModel(1, Pos(5,10), Direction.Down, false),
            new MachineModel(2, Pos(10,9), Direction.Left, true),
        };
        var grid = new BubblewonderGridModel(cells, machines, state);
        var zbs = new[]
        {
            new SimZb(0x07,4,4,3,4), new SimZb(0x05,1,1,4,3), new SimZb(0x06,3,5,5,4),
            new SimZb(0x04,2,3,1,1), new SimZb(0x0C,3,5,3,4), new SimZb(0x0B,2,4,2,1),
            new SimZb(0x09,2,4,5,3), new SimZb(0x03,3,2,3,2), new SimZb(0x11,3,5,5,1),
            new SimZb(0x0A,1,3,1,1), new SimZb(0x08,5,5,3,2), new SimZb(0x10,5,1,4,4),
            new SimZb(0x0D,1,5,2,3), new SimZb(0x0F,2,3,3,5), new SimZb(0x0E,2,5,2,3),
            new SimZb(0x12,3,1,2,2),
        };
        return (grid, zbs);
    }

    /// <summary>Synthetisches „Switch-Gated-Goal"-Layout: das Ziel ist nur
    /// erreichbar, nachdem ein ZB über einen Trigger den Switch S umlegt — und
    /// genau der Setup-ZB stirbt dabei. Greedy bleibt bei 0 (es schickt immer
    /// über die erste, scorende-aber-noch-verschlossene Maschine und stirbt),
    /// der config-gezielte Beam muss die Setup→Score-Sequenz finden.</summary>
    private static (BubblewonderGridModel Grid, SimZb[] Zbs) BuildSwitchGate()
    {
        var s = Switch(1, Direction.Left, Direction.Down);   // active idx0=Left, idx1=Down
        var cells = new Dictionary<int, CellModel>
        {
            // Scoring-Maschine M0 @ (0,5)↓ — durch Switch S bei (3,5) verschlossen.
            [Pos(3,5)] = s,            // state0→Left→Falle(3,4); state1→Down→Ziel
            [Pos(3,4)] = Trap(),
            [Pos(5,5)] = Goal(),
            // Setup-Maschine M1 @ (0,0)↓ — läuft über Trigger (2,0) ch1 → flippt S,
            // stirbt dann in Falle (5,0).
            [Pos(2,0)] = Trigger(1),
            [Pos(5,0)] = Trap(),
        };
        var state = new GridState();
        state.SwitchStateByCell[Pos(3,5)] = 0;   // initial: Left → Falle
        var machines = new[]
        {
            new MachineModel(0, Pos(0,5), Direction.Down, false),  // scoring (gated)
            new MachineModel(1, Pos(0,0), Direction.Down, false),  // setup (flippt S)
        };
        var grid = new BubblewonderGridModel(cells, machines, state);
        var zbs = Enumerable.Range(0, 4).Select(i => new SimZb((ushort)i, 1, 1, 1, 1)).ToArray();
        return (grid, zbs);
    }

    [Fact]
    public void Beam_SolvesSwitchGate_WhereGreedyIsZero()
    {
        var (grid, zbs) = BuildSwitchGate();

        // Greedy bleibt bei 0 (schickt stets über die verschlossene Scoring-Maschine).
        var greedy = BubblewonderSolver.SolveGreedy(grid, zbs);
        Assert.Equal(0, greedy.Survivors);

        // Der config-gezielte Beam findet die Setup→Score-Sequenz: 1 ZB legt S um,
        // die restlichen 3 scoren → > 0 (erwartet 3).
        var beam = BubblewonderSolver.SolveBeam(grid, zbs);
        Assert.True(beam.Survivors > 0,
            $"Beam fand die Switch-Gate-Lösung nicht (Survivors={beam.Survivors})");

        // Der mit dem Beam-Floor geseedete DFS liefert ≥ Beam (nicht mehr 0).
        var dfs = BubblewonderSolver.SolveDfs(grid, zbs, TimeSpan.FromSeconds(10));
        Assert.True(dfs.Survivors >= beam.Survivors && dfs.Survivors > 0,
            $"DFS ({dfs.Survivors}) nutzte den Beam-Floor ({beam.Survivors}) nicht");
    }

    /// <summary>REGS 16608 (aus memdump-074739): Greedy=0 (Kaltstart tötet jeden ZB),
    /// aber mit dem Insel-Park-Fix (Stein-Insel = Landepunkt statt Tod, User-verifiziert)
    /// existiert eine Lösung: M1-ZBs landen auf der Insel (10,11), werden re-losgeschickt,
    /// triggern (10,7) → Switch (1,7) kippt auf Down → M0-ZBs scoren. Der vollständige
    /// Reachability-Planer muss das finden. Das ist die 16608-Regression (jetzt grün).</summary>
    [Fact]
    public void Planner_Solves16608_ViaIslandParkAndSwitchFlip()
    {
        var (grid, zbs) = Build16608();
        Assert.Equal(0, BubblewonderSolver.SolveGreedy(grid, zbs).Survivors);

        // Modell intern korrekt: mit (1,7)=Down scort ZB 0x07 von M0.
        var st = grid.CloneState();
        st.SwitchStateByCell[Pos(1,7)] = 1;
        Assert.Equal(SimOutcome.Scored,
            BubblewonderSimulator.Simulate(grid.WithState(st), new SimZb(0x07,4,4,3,4), 0).Outcome);

        // Mit Insel-Park ist ein scorender Zustand erreichbar …
        var reach = BubblewonderReachability.Analyze(grid, zbs);
        Assert.True(reach.AnyScoringStateReachable,
            $"kein scorender Zustand erreichbar (erkundet={reach.ExploredStates})");

        // … und der Planer findet eine positive Lösung (statt 0/16).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var beam = BubblewonderSolver.SolveBeam(grid, zbs);
        sw.Stop();
        Assert.True(beam.Survivors > 0, $"Beam=0 auf 16608 ({sw.Elapsed.TotalSeconds:F1}s)");
    }
}
