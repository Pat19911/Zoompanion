namespace ZoombiniHelper.Bubblewonder.Simulator;

/// <summary>
/// Sucht eine Sequenz <c>(ZB → Maschine)</c> die möglichst viele ZBs überleben lässt.
///
/// <para>Strategien:</para>
/// <list type="bullet">
///   <item><b>Brute-Force</b>: alle Permutationen × alle Maschinen-Zuordnungen,
///         für kleine ZB-Mengen (≤ <see cref="BruteForceMaxZbs"/>) optimal.</item>
///   <item><b>Greedy</b>: pick zu jedem Zeitpunkt den ZB+Maschine der die
///         höchste lokale Survivor-Rate liefert. Schnell, nicht garantiert
///         optimal, aber für N=16 die einzige praktikable Option.</item>
/// </list>
/// </summary>
public static class BubblewonderSolver
{
    /// <summary>Brute-Force ist bis zu dieser ZB-Anzahl realistisch.
    /// 6! × 3^6 ≈ 525k Sequenzen — noch im Sekundenbereich.</summary>
    public const int BruteForceMaxZbs = 6;

    /// <summary>Findet die beste ZB→Maschine-Sequenz mit Brute-Force.
    /// Wirft <see cref="InvalidOperationException"/> wenn N &gt; <see cref="BruteForceMaxZbs"/>.</summary>
    public static SolverResult SolveBruteForce(
        BubblewonderGridModel grid, IReadOnlyList<SimZb> zbs)
    {
        if (zbs.Count > BruteForceMaxZbs)
            throw new InvalidOperationException(
                $"Brute-Force mit {zbs.Count} ZBs zu teuer (max {BruteForceMaxZbs}). " +
                $"Nutze {nameof(SolveGreedy)} stattdessen.");
        if (grid.Machines.Count == 0)
            return new SolverResult(Array.Empty<Assignment>(), 0, "Keine Maschinen");

        SolverResult best = new(Array.Empty<Assignment>(), -1, "init");
        foreach (var perm in Permutations(zbs.ToArray()))
        {
            foreach (var machineSeq in MachineSequences(grid.Machines.Count, perm.Length))
            {
                var (survivors, assignments) = Evaluate(grid, perm, machineSeq);
                if (survivors > best.Survivors)
                {
                    best = new SolverResult(assignments, survivors,
                        $"Brute-Force {perm.Length}!×{grid.Machines.Count}^{perm.Length}");
                    if (survivors == zbs.Count) return best;  // Optimum
                }
            }
        }
        return best;
    }

    /// <summary>Greedy-Suche: zu jedem Schritt wird die (ZB, Maschine)-Kombination
    /// gewählt die die meisten Survivors liefert (initialer ZB + Folge-ZBs aus
    /// Pending-Queues mitgezählt).</summary>
    public static SolverResult SolveGreedy(
        BubblewonderGridModel grid, IReadOnlyList<SimZb> zbs)
    {
        if (grid.Machines.Count == 0)
            return new SolverResult(Array.Empty<Assignment>(), 0, "Keine Maschinen");

        var remaining = zbs.ToList();
        var assignments = new List<Assignment>();
        var currentGrid = grid;
        int totalSurvivors = 0;

        while (remaining.Count > 0)
        {
            var bestStep = FindBestNextStep(currentGrid, remaining);
            if (bestStep is null) break;
            assignments.Add(new Assignment(bestStep.Zb, bestStep.MachineIdx));
            totalSurvivors += bestStep.RunResult.SurvivorCount;
            currentGrid = bestStep.RunResult.FinalGrid;
            // Entferne ALLE im RunResult abgehandelten ZBs (initialer + befreite/
            // geschubste Folge-ZBs), nicht nur den initialen — sonst werden
            // Channel-befreite ZBs später nochmal als initial gepickt und
            // doppelt gezählt.
            foreach (var outcome in bestStep.RunResult.Outcomes)
                remaining.RemoveAll(z => z.HeaderId == outcome.Zb.HeaderId);
        }
        return new SolverResult(assignments, totalSurvivors,
            $"Greedy ({zbs.Count} ZBs)");
    }

    private static GreedyStep? FindBestNextStep(
        BubblewonderGridModel grid, IReadOnlyList<SimZb> candidates)
    {
        GreedyStep? best = null;
        // Hauptpool-ZBs × Hauptmaschinen
        for (int z = 0; z < candidates.Count; z++)
        {
            for (int m = 0; m < grid.Machines.Count; m++)
            {
                if (grid.Machines[m].IsIsland) continue;
                var run = BubblewonderRunner.RunSingle(grid, candidates[z], m);
                if (best is null || run.SurvivorCount > best.RunResult.SurvivorCount)
                    best = new GreedyStep(candidates[z], m, run);
            }
        }
        // Geparkte ZBs × jeweilige Insel-Maschine. Vor dem Sim-Run muss der
        // geparkte ZB aus der Park-Liste raus, sonst doppelt im Outcome.
        foreach (var (machineIdx, parkedList) in grid.State.ParkedZbsByMachineIdx.ToList())
        {
            for (int p = 0; p < parkedList.Count; p++)
            {
                var preState = grid.CloneState();
                preState.ParkedZbsByMachineIdx[machineIdx].RemoveAt(p);
                var preGrid = grid.WithState(preState);
                var run = BubblewonderRunner.RunSingle(preGrid, parkedList[p], machineIdx);
                if (best is null || run.SurvivorCount > best.RunResult.SurvivorCount)
                    best = new GreedyStep(parkedList[p], machineIdx, run);
            }
        }
        return best;
    }

    private static (int Survivors, Assignment[] Assignments) Evaluate(
        BubblewonderGridModel grid,
        IReadOnlyList<SimZb> zbsInOrder,
        IReadOnlyList<int> machineIndicesInOrder)
    {
        int totalSurvivors = 0;
        var assignments = new Assignment[zbsInOrder.Count];
        var current = grid;
        for (int i = 0; i < zbsInOrder.Count; i++)
        {
            assignments[i] = new Assignment(zbsInOrder[i], machineIndicesInOrder[i]);
            var run = BubblewonderRunner.RunSingle(current, zbsInOrder[i], machineIndicesInOrder[i]);
            totalSurvivors += run.SurvivorCount;
            current = run.FinalGrid;
        }
        return (totalSurvivors, assignments);
    }

    private static IEnumerable<T[]> Permutations<T>(T[] items)
    {
        if (items.Length == 0) { yield return Array.Empty<T>(); yield break; }
        if (items.Length == 1) { yield return items; yield break; }
        for (int i = 0; i < items.Length; i++)
        {
            var rest = items.Take(i).Concat(items.Skip(i + 1)).ToArray();
            foreach (var p in Permutations(rest))
            {
                var result = new T[items.Length];
                result[0] = items[i];
                Array.Copy(p, 0, result, 1, p.Length);
                yield return result;
            }
        }
    }

    private static IEnumerable<int[]> MachineSequences(int machineCount, int slots)
    {
        var current = new int[slots];
        while (true)
        {
            yield return (int[])current.Clone();
            int i = slots - 1;
            while (i >= 0)
            {
                current[i]++;
                if (current[i] < machineCount) break;
                current[i] = 0;
                i--;
            }
            if (i < 0) yield break;
        }
    }

    private sealed record GreedyStep(SimZb Zb, int MachineIdx, RunResult RunResult);

    /// <summary>Vollständige DFS-Search mit Bounds, Memoization, Time-Limit und
    /// optionalem CancellationToken. Findet die global beste (ZB→Maschine)-Sequenz
    /// auch über Setup-Moves.</summary>
    public static SolverResult SolveDfs(
        BubblewonderGridModel grid, IReadOnlyList<SimZb> zbs,
        TimeSpan? timeBudget = null,
        CancellationToken cancellationToken = default,
        Action<int>? onNewBest = null,
        SolverProgress? progress = null)
    {
        // Auch geparkte Insel-ZBs zählen als lösbar: sind ALLE ZBs auf Inseln
        // (Hauptpool leer), kann der Solver sie trotzdem über ihre Insel-Maschine
        // re-launchen und retten. Nur abbrechen wenn es WIRKLICH keine ZBs gibt
        // (weder Pool noch geparkt) oder keine Maschinen.
        bool hasParked = grid.State.ParkedZbsByMachineIdx.Any(kv => kv.Value.Count > 0);
        if ((zbs.Count == 0 && !hasParked) || grid.Machines.Count == 0)
            return new SolverResult(Array.Empty<Assignment>(), 0, "DFS (empty)");

        // Kein Zeitbudget übergeben (null) = unbegrenzt: der Solver rechnet bis
        // zum BEWIESENEN Optimum durch. Abbruch nur über das CancellationToken
        // (z.B. wenn sich das Layout ändert). Der Fortschritt ist live sichtbar,
        // also bleibt die UI nicht „blind" während einer langen Suche.
        var deadline = timeBudget is { } tb ? DateTime.UtcNow + tb : DateTime.MaxValue;
        // Untere Schranke: die BESSERE aus Greedy und config-gezielter Beam-Suche.
        // Der Beam findet auch in „Switch-erst-öffnen"-Layouts (z.B. 16608) eine
        // positive Lösung, wo Greedy 0 liefert — damit greift das DFS-Pruning
        // sofort statt den ganzen 0-Survivor-Raum zu durchforsten.
        var greedy = SolveGreedy(grid, zbs);
        // Beam nur wenn Greedy NICHTS findet (= der pathologische „erst Switches
        // öffnen"-Fall, in dem das DFS-Pruning sonst nie greift). Bei Greedy>0
        // greift das Pruning bereits → Beam-Overhead vermeiden (schnelle Layouts).
        var floor = greedy;
        if (greedy.Survivors == 0)
        {
            // SOUNDNESS-GATE gegen den „0 rettbar (Zeitlimit)"-Hänger: Bevor der DFS
            // 60s lang einen nicht-prunebaren Suchraum durchforstet, prüfe mit der
            // Reachability-Über-Approximation (unbegrenzte ZBs), ob ÜBERHAUPT ein
            // scorender Zustand erreichbar ist. Lief die BFS VOLLSTÄNDIG (nicht
            // gedeckelt) und fand keinen, ist das Board IM MODELL beweisbar unlösbar
            // (mehr ZBs als real → erreicht selbst sie nichts, gibt es real nichts).
            // Dann SOFORT 0 zurück mit klarem Modell-Befund — statt 60s zu verbrennen
            // (die scorableSigs-Schranke ist hier wirkungslos, weil sie ALLE Switch-
            // Stellungen für erreichbar hält und so die obere Schranke nicht senkt).
            // Da reale Boards ohne Spielerfehler 100% lösbar sind, signalisiert dieser
            // Befund einen LAYOUT-LESEFEHLER, kein Solver-Limit → gezielt diagnostizierbar.
            var reach = BubblewonderReachability.Analyze(grid, zbs);
            if (reach.Complete && !reach.AnyScoringStateReachable)
                return new SolverResult(Array.Empty<Assignment>(), 0,
                    $"kein Ziel erreichbar im Modell (erkundet={reach.ExploredStates})");

            var beam = SolveBeam(grid, zbs);
            if (beam.Survivors > floor.Survivors) floor = beam;
        }
        var best = new SolverResult(floor.Assignments, floor.Survivors,
            $"DFS (floor: {floor.Strategy})");
        // Fortschritt sofort mit der Untergrenze füllen (sonst zeigt die UI
        // nichts bis zur ersten echten Verbesserung).
        if (progress is not null) progress.BestSurvivors = floor.Survivors;

        // Such-Kandidaten = Pool + geparkte Insel-ZBs (aktiv schickbar). Gefangene
        // Sticky-ZBs sind KEINE eigenen Züge (sie werden befreit), gehören aber in
        // die Scorability-Berechnung, damit ihr Sig als „kann scoren" gilt.
        var allZbs = zbs.ToList();
        foreach (var (_, parkedList) in grid.State.ParkedZbsByMachineIdx)
            allZbs.AddRange(parkedList);
        var scorabilityZbs = new List<SimZb>(allZbs);
        scorabilityZbs.AddRange(grid.State.StickyTrappedByCell.Values);

        // Einmal pro Solve: relevante Attribute (für Equivalence-Klassen) und die
        // Menge der Signaturen, die ÜBERHAUPT scoren können (über alle erreichbaren
        // Switch-Konfigurationen). Beides ist grid-statisch → nicht pro Knoten neu.
        var relevantAttrs = RelevantAttributes(grid);
        var scorableSigs = ComputeScorableSigs(grid, scorabilityZbs, relevantAttrs);

        var path = new List<Assignment>();
        bool timedOut = false;
        // Memoization: state → max additional survivors. Reduziert exponentiell
        // wenn das Spiel viele permutationssymmetrische Pfade hat.
        var memo = new Dictionary<string, int>();
        // Zyklen-Schutz für Insel-Park-Loops (park → losschicken → wieder
        // geparkt im selben State). Hält die State-Keys im aktuellen Pfad-Stack.
        var visitedInPath = new HashSet<string>();
        Search(grid, allZbs.ToHashSet(new ZbHandleComparer()), 0, path,
            ref best, deadline, cancellationToken, ref timedOut, memo, visitedInPath, onNewBest, progress,
            relevantAttrs, scorableSigs, span: 1.0);

        string label = cancellationToken.IsCancellationRequested ? "DFS (cancelled)"
                     : timedOut ? "DFS (time-limit)"
                     : "DFS (optimal)";
        return new SolverResult(best.Assignments, best.Survivors,
            $"{label}, memo={memo.Count}");
    }

    /// <summary>Liefert die Attribute (1=Hair, 2=Eyes, 3=Nose, 4=Feet) die von
    /// mindestens einer Conditional-Cell im Grid abgefragt werden. Nur diese
    /// sind fürs Routing relevant — die anderen können bei der Equivalence-
    /// Klassen-Bildung weggeworfen werden.</summary>
    internal static byte[] RelevantAttributes(BubblewonderGridModel grid)
    {
        var attrs = new HashSet<byte>();
        for (int p = 0; p < 12 * 13; p++)
        {
            var cell = grid.CellAt(p);
            if (cell.Type == MechanismType.Conditional && cell.ConditionalAttrCode > 0)
                attrs.Add((byte)cell.ConditionalAttrCode);
        }
        return attrs.OrderBy(a => a).ToArray();
    }

    /// <summary>Kanonische Signatur: nur die relevanten Attribute. ZBs mit
    /// gleicher Signatur sind aus Solver-Sicht ununterscheidbar.</summary>
    internal static long CanonicalSig(SimZb zb, byte[] relevantAttrs)
    {
        long sig = 0;
        foreach (var a in relevantAttrs)
        {
            byte val = a switch
            {
                1 => zb.Hair, 2 => zb.Eyes, 3 => zb.Nose, 4 => zb.Feet, _ => (byte)0,
            };
            sig = sig * 8 + val;  // 5 mögliche Werte (1..5) + 0 → passt in 3 Bits
        }
        return sig;
    }

    /// <summary>Maximale Anzahl Switch-Konfigurationen die durchprobiert werden.
    /// Darüber wird die Optimierung übersprungen (Fallback auf lose Schranke).</summary>
    private const int MaxScorabilityConfigs = 8192;

    /// <summary>Berechnet (einmal pro Solve) die Menge der ZB-Signaturen die
    /// ÜBERHAUPT scoren können — über ALLE Switch-Konfigurationen (jeder Switch
    /// unabhängig auf jede seiner aktiven Richtungen) × alle Maschinen.
    ///
    /// <para>Das ist eine <b>Über-Approximation</b> der Erreichbarkeit (es nimmt
    /// an, jede Switch-Kombination sei erreichbar — real braucht es ggf. die
    /// richtigen Trigger-Durchläufe). Dadurch ist die abgeleitete obere Schranke
    /// stets gültig: eine Klasse die hier als „nicht scoring-fähig" markiert wird,
    /// kann auch real NIE scoren → sicher prunebar. Liefert null wenn zu viele
    /// Konfigurationen (dann keine Optimierung).</para></summary>
    private static HashSet<long>? ComputeScorableSigs(
        BubblewonderGridModel grid, IReadOnlyList<SimZb> allZbs, byte[] relevantAttrs)
    {
        // Repräsentant pro Equivalence-Klasse.
        var reps = new Dictionary<long, SimZb>();
        foreach (var z in allZbs) reps.TryAdd(CanonicalSig(z, relevantAttrs), z);

        // Switch-Zellen + ihre möglichen aktiven State-Indizes.
        var switches = new List<(int Pos, int[] States)>();
        for (int p = 0; p < 12 * 13; p++)
        {
            var c = grid.CellAt(p);
            if (c.Type != MechanismType.SwitchActivated) continue;
            var st = new List<int>();
            for (int i = 0; i < 4; i++) if (c.HasDirectionAtStateIndex(i)) st.Add(i);
            if (st.Count > 0) switches.Add((p, st.ToArray()));
        }
        long configCount = 1;
        foreach (var s in switches)
        {
            configCount *= s.States.Length;
            if (configCount > MaxScorabilityConfigs) return null;  // zu teuer → lose Schranke
        }

        // Alle Konfigurationen aufzählen (kartesisches Produkt).
        var configs = new List<Dictionary<int, int>>();
        void Enumerate(int i, Dictionary<int, int> cur)
        {
            if (i == switches.Count) { configs.Add(new(cur)); return; }
            foreach (var st in switches[i].States) { cur[switches[i].Pos] = st; Enumerate(i + 1, cur); }
        }
        Enumerate(0, new Dictionary<int, int>());

        var scorable = new HashSet<long>();
        foreach (var (sig, zb) in reps)
        {
            bool canScore = false;
            foreach (var cfg in configs)
            {
                var state = grid.CloneState();
                foreach (var (pos, st) in cfg) state.SwitchStateByCell[pos] = st;
                var g2 = grid.WithState(state);
                // Alle Maschinen inkl. Insel (= deckt Re-Launch nach Parken ab).
                for (int m = 0; m < grid.Machines.Count; m++)
                {
                    if (BubblewonderSimulator.Simulate(g2, zb, m).Outcome == SimOutcome.Scored)
                    { canScore = true; break; }
                }
                if (canScore) break;
            }
            if (canScore) scorable.Add(sig);
        }
        return scorable;
    }

    private static int Search(
        BubblewonderGridModel grid, HashSet<SimZb> remaining,
        int currentSurvivors, List<Assignment> path,
        ref SolverResult best, DateTime deadline,
        CancellationToken ct, ref bool timedOut,
        Dictionary<string, int> memo,
        HashSet<string> visitedInPath,
        Action<int>? onNewBest,
        SolverProgress? progress,
        byte[] relevantAttrs,
        HashSet<long>? scorableSigs,
        double span)
    {
        // span = Anteil des Gesamt-Suchbaums den dieser Aufruf abdeckt. Vertrag:
        // bis dieser Aufruf zurückkehrt, ist genau `span` zu progress.Fraction
        // addiert worden (von hier oder von den Kindern). So erreicht Fraction
        // exakt 1.0 wenn die Wurzel-Suche regulär durchläuft.
        // Abbruch/Timeout addieren NICHT — dann bleibt Fraction < 1.0 stehen.
        if (ct.IsCancellationRequested) return 0;
        if (DateTime.UtcNow > deadline) { timedOut = true; return 0; }
        if (progress is not null) System.Threading.Interlocked.Increment(ref progress.Nodes);

        // Wurzel des Suchbaums = leerer Pfad. Nur hier wird der grobe, immer
        // sichtbare „Suchzweig X/N"-Fortschritt geführt.
        bool isRoot = path.Count == 0;

        // Best bei JEDEM Knoten werten, nicht nur am Blatt: ein Pfad der mit
        // einem dauerhaft geparkten ZB endet erreicht "remaining leer" nie,
        // würde sonst nie gewertet. currentSurvivors ist monoton und mit dem
        // aktuellen path garantiert erreichbar.
        if (currentSurvivors > best.Survivors)
        {
            best = new SolverResult(path.ToArray(), currentSurvivors, "DFS-current");
            if (progress is not null) progress.BestSurvivors = currentSurvivors;
            onNewBest?.Invoke(currentSurvivors);
        }

        if (remaining.Count == 0) { if (progress is not null) progress.Fraction += span; return 0; }

        // Pruning #1: schärfste gültige obere Schranke = nur ZBs deren Klasse
        // ÜBERHAUPT scoren kann (über irgendeine erreichbare Switch-Konfig).
        // Wer nie scoren kann (z.B. wenn das Ziel von keiner Maschine erreichbar
        // ist), trägt 0 bei → bei best=0 wird sofort geschnitten statt den
        // ganzen Suchraum zu durchforsten. Über-Approximation der Erreichbarkeit
        // → niemals wird eine echte Lösung weggeschnitten.
        int upperRemaining = scorableSigs is null
            ? remaining.Count
            : remaining.Count(z => scorableSigs.Contains(CanonicalSig(z, relevantAttrs)));
        // Noch in Klebefallen gefangene ZBs zählen ebenfalls zum Survivor-Potential:
        // jeder kann durch einen späteren Zug (Trigger-Befreiung) noch scoren. Ohne
        // sie wäre die Schranke zu niedrig → ein Zweig der eine Falle befreit würde
        // fälschlich vorzeitig geschnitten (das „beweist" ein zu kleines Optimum).
        // +1 pro gefangenem ZB ist gültige Über-Approximation.
        foreach (var (_, tz) in grid.State.StickyTrappedByCell)
            if (scorableSigs is null || scorableSigs.Contains(CanonicalSig(tz, relevantAttrs)))
                upperRemaining++;
        if (currentSurvivors + upperRemaining <= best.Survivors)
        { if (progress is not null) progress.Fraction += span; return 0; }

        // Memoization: state-key nutzt Equivalence-Klassen-Counts statt
        // konkrete HeaderIds — verschiedene Permutationen äquivalenter ZBs
        // ergeben den GLEICHEN Sub-Problem-State.
        string key = StateKeyEquivalence(grid, remaining, relevantAttrs);
        if (memo.TryGetValue(key, out var cachedAdditional))
        { if (progress is not null) progress.Fraction += span; return cachedAdditional; }

        // Zyklen-Schutz: wenn dieser State im aktuellen Pfad-Stack schon
        // besucht wurde, ist es ein Insel-Park-Loop ohne Fortschritt
        // (park → losschicken → wieder geparkt, gleicher State). Abbrechen
        // OHNE memo zu setzen (der Erst-Besuch des States setzt memo korrekt).
        if (!visitedInPath.Add(key))
        { if (progress is not null) progress.Fraction += span; return 0; }

        // Equivalence-Klassen + Insel-Park-Handling: gemeinsamer Helper (DFS +
        // Beam routen damit garantiert identisch).
        var options = GenerateOptions(grid, remaining, relevantAttrs);
        options.Sort((a, b) => b.Run.SurvivorCount.CompareTo(a.Run.SurvivorCount));

        // Fortschritt: jeder der `options.Count` Zweige bekommt einen gleichen
        // Anteil von `span`; jedes Kind addiert seinen Anteil selbst beim
        // Zurückkehren → in Summe wieder genau `span`. Gibt es gar keine Option
        // (Sackgasse), ist dieser Teilbaum hier fertig → span selbst gutschreiben.
        double childSpan = options.Count > 0 ? span / options.Count : 0.0;
        if (options.Count == 0 && progress is not null) progress.Fraction += span;

        // Wurzel-Zweige zählen: Gesamtzahl der ersten Züge jetzt bekannt.
        if (isRoot && progress is not null) progress.RootBranchesTotal = options.Count;

        int bestAdditional = 0;
        foreach (var opt in options)
        {
            if (ct.IsCancellationRequested) return bestAdditional;
            if (DateTime.UtcNow > deadline) { timedOut = true; return bestAdditional; }

            // Geparkte ZBs (Outcome Parked) bleiben OFFEN — sie landen im
            // FinalGrid in ParkedZbsByMachineIdx und können später über ihre
            // Insel-Maschine wieder losgeschickt werden (das ist der Kern der
            // Insel-Mechanik). Nur terminale Outcomes (Scored/Dead/Trapped/…)
            // verlassen das offene Set.
            var consumedHeaderIds = opt.Run.Outcomes
                .Where(o => o.Outcome != SimOutcome.Parked)
                .Select(o => o.Zb.HeaderId).ToHashSet();
            var newRemaining = new HashSet<SimZb>(
                remaining.Where(z => !consumedHeaderIds.Contains(z.HeaderId)),
                remaining.Comparer);

            path.Add(new Assignment(opt.Zb, opt.MachineIdx));
            int subResult = Search(opt.Run.FinalGrid, newRemaining,
                currentSurvivors + opt.Run.SurvivorCount,
                path, ref best, deadline, ct, ref timedOut, memo, visitedInPath, onNewBest, progress,
                relevantAttrs, scorableSigs, childSpan);
            path.RemoveAt(path.Count - 1);

            // Ein Wurzel-Zweig ist fertig abgesucht → grober Fortschritt steigt.
            if (isRoot && progress is not null)
                System.Threading.Interlocked.Increment(ref progress.RootBranchesDone);

            int totalAdditional = opt.Run.SurvivorCount + subResult;
            if (totalAdditional > bestAdditional) bestAdditional = totalAdditional;
        }

        visitedInPath.Remove(key);
        memo[key] = bestAdditional;
        return bestAdditional;
    }

    /// <summary>Erzeugt die Zug-Optionen für einen State (Equivalence-Klassen +
    /// Insel-Park-Handling + Null-Move-Filter). Von DFS UND Beam genutzt, damit
    /// beide garantiert identisch routen.</summary>
    internal static List<(SimZb Zb, int MachineIdx, RunResult Run)> GenerateOptions(
        BubblewonderGridModel grid, IEnumerable<SimZb> remaining, byte[] relevantAttrs)
    {
        var options = new List<(SimZb Zb, int MachineIdx, RunResult Run)>();
        var seenSigPerMachine = new HashSet<(long Sig, int M)>();
        foreach (var zb in remaining)
        {
            int? parkedMachine = FindParkedMachine(grid.State, zb);
            if (parkedMachine is { } pm)
            {
                long sig = CanonicalSig(zb, relevantAttrs);
                if (!seenSigPerMachine.Add((sig, pm))) continue;
                var preState = grid.CloneState();
                preState.ParkedZbsByMachineIdx[pm].RemoveAll(z => z.HeaderId == zb.HeaderId);
                var preGrid = grid.WithState(preState);
                var run = BubblewonderRunner.RunSingle(preGrid, zb, pm);
                bool nullMove =
                    run.Outcomes.Count == 1
                    && run.Outcomes[0].Outcome == SimOutcome.Parked
                    && FindParkedMachine(run.FinalGrid.State, zb) == pm
                    && run.FinalGrid.State.WaypointContextSig() == grid.State.WaypointContextSig();
                if (nullMove) continue;
                options.Add((zb, pm, run));
            }
            else
            {
                long sig = CanonicalSig(zb, relevantAttrs);
                for (int m = 0; m < grid.Machines.Count; m++)
                {
                    if (grid.Machines[m].IsIsland) continue;
                    if (!seenSigPerMachine.Add((sig, m))) continue;
                    var run = BubblewonderRunner.RunSingle(grid, zb, m);
                    options.Add((zb, m, run));
                }
            }
        }
        return options;
    }

    /// <summary>Standard-Beamweite für <see cref="SolveBeam"/>.</summary>
    public const int DefaultBeamWidth = 400;

    /// <summary>Config-gezielte Beam-Suche: findet schnell eine GUTE (nicht
    /// notwendig optimale) Lösung, indem sie States nach einer Heuristik
    /// priorisiert, die das Öffnen von Scoring-Wegen belohnt.
    ///
    /// <para>Motivation (REGS 16608): aus dem Kaltstart stirbt jeder ZB →
    /// Greedy=0 → der DFS-Bound greift nie → Explosion. Die echte Lösung verlangt
    /// erst Switches per Trigger umzulegen, bevor irgendwer scort. Die Heuristik
    /// <c>Survivors·BIG + (#ZBs die in der AKTUELLEN Config scoren können)</c>
    /// treibt die Suche genau dorthin: Züge die einen Trigger durchlaufen und
    /// damit Switches öffnen, erhöhen die Scorability → werden im Beam gehalten.</para>
    ///
    /// <para>Das Ergebnis dient als <b>positive untere Schranke</b> für den DFS;
    /// damit greift dessen Pruning sofort und er kann das Optimum beweisen/finden,
    /// statt 0-Survivor-Zweige endlos zu durchforsten.</para></summary>
    public static SolverResult SolveBeam(
        BubblewonderGridModel grid, IReadOnlyList<SimZb> zbs, int beamWidth = DefaultBeamWidth)
    {
        // Geparkte Insel-ZBs sind ebenfalls lösbar (re-launchbar) — nur abbrechen
        // wenn es weder Pool- noch geparkte ZBs gibt.
        bool hasParked = grid.State.ParkedZbsByMachineIdx.Any(kv => kv.Value.Count > 0);
        if ((zbs.Count == 0 && !hasParked) || grid.Machines.Count == 0)
            return new SolverResult(Array.Empty<Assignment>(), 0, "Beam (empty)");

        var relevantAttrs = RelevantAttributes(grid);
        var allZbs = zbs.ToList();
        foreach (var (_, parkedList) in grid.State.ParkedZbsByMachineIdx)
            allZbs.AddRange(parkedList);

        // Repräsentant je Signatur über den GANZEN Pool (für config-Scorability).
        var repBySig = new Dictionary<long, SimZb>();
        foreach (var z in allZbs) repBySig.TryAdd(CanonicalSig(z, relevantAttrs), z);

        // Reachability über den VOLLSTÄNDIGEN Zustand (Switches + Stickys + Insel):
        // Distanz jedes Zustands zu einem scorenden. Gibt dem Beam den Gradienten
        // auch zu Lösungen die Sticky-Befreiung oder Insel-Parken brauchen — nicht
        // nur reine Switch-Flips.
        var reach = BubblewonderReachability.Analyze(grid, zbs);

        var startRemaining = allZbs.ToHashSet(new ZbHandleComparer());
        var best = new SolverResult(Array.Empty<Assignment>(), 0, "Beam");
        var frontier = new List<BeamState>
        {
            new(grid, startRemaining, 0, Array.Empty<Assignment>()),
        };
        var seen = new HashSet<string>();
        // Scorability pro Switch/Sticky-Config cachen: welche ZB-Signaturen können
        // in DIESER Config überhaupt scoren. Configs sind wenige → Heuristik wird
        // nach Erstberechnung zu reinen Set-Lookups (sonst pro State teuer).
        var scorableByConfig = new Dictionary<string, HashSet<long>>();
        // Tiefe: jeder Schritt verbraucht (meist) mindestens einen ZB; Insel-
        // Re-Launches können zusätzliche Schritte brauchen → großzügig deckeln.
        int maxDepth = allZbs.Count * 2 + 4;

        for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<(double H, BeamState S)>();
            foreach (var st in frontier)
            {
                if (st.Survivors > best.Survivors)
                    best = new SolverResult(st.Path, st.Survivors, "Beam");
                if (st.Remaining.Count == 0) continue;

                foreach (var opt in GenerateOptions(st.Grid, st.Remaining, relevantAttrs))
                {
                    var consumed = opt.Run.Outcomes
                        .Where(o => o.Outcome != SimOutcome.Parked)
                        .Select(o => o.Zb.HeaderId).ToHashSet();
                    var newRem = new HashSet<SimZb>(
                        st.Remaining.Where(z => !consumed.Contains(z.HeaderId)),
                        st.Remaining.Comparer);
                    var newPath = new Assignment[st.Path.Count + 1];
                    for (int i = 0; i < st.Path.Count; i++) newPath[i] = st.Path[i];
                    newPath[^1] = new Assignment(opt.Zb, opt.MachineIdx);

                    var ns = new BeamState(opt.Run.FinalGrid, newRem,
                        st.Survivors + opt.Run.SurvivorCount, newPath);
                    // Dedup über Equivalence-State (gleiche Restmenge + Wegpunkt-
                    // Kontext = gleicher Teilbaum). Tiefe NICHT in den Key, damit
                    // ein State der auf zwei Pfaden gleich teuer ist nur einmal lebt.
                    string key = ns.Survivors + "#" + StateKeyEquivalence(ns.Grid, newRem, relevantAttrs);
                    if (!seen.Add(key)) continue;
                    next.Add((Heuristic(ns, relevantAttrs, scorableByConfig, repBySig, reach), ns));
                }
            }
            next.Sort((a, b) => b.H.CompareTo(a.H));
            frontier = next.Take(beamWidth).Select(x => x.S).ToList();
        }
        return best with { Strategy = $"Beam (width={beamWidth})" };
    }

    /// <summary>Heuristik-Wert eines Beam-States: gesicherte Survivors dominieren
    /// (×100000); danach die Zahl der ZBs die in der AKTUELLEN Config scoren können
    /// (×100); zuletzt der Reachability-Gradient (näher an einem scorenden Zustand
    /// = besser), der den Beam durch die nötige Zug-Sequenz lenkt.</summary>
    private static double Heuristic(
        BeamState st, byte[] relevantAttrs,
        Dictionary<string, HashSet<long>> scorableByConfig, Dictionary<long, SimZb> repBySig,
        BubblewonderReachability reach)
    {
        // Welche Signaturen scoren in DIESER Config? Pro Config einmal über den
        // GANZEN Pool berechnen (nicht nur Reststand → Cache bleibt vollständig).
        string cfg = st.Grid.State.WaypointContextSig();
        if (!scorableByConfig.TryGetValue(cfg, out var scorable))
        {
            scorable = new HashSet<long>();
            foreach (var (sig, zb) in repBySig)
                for (int m = 0; m < st.Grid.Machines.Count; m++)
                    if (BubblewonderSimulator.Simulate(st.Grid, zb, m).Outcome == SimOutcome.Scored)
                    { scorable.Add(sig); break; }
            scorableByConfig[cfg] = scorable;
        }
        int scorableNow = 0;
        var seenSig = new HashSet<long>();
        foreach (var zb in st.Remaining)
        {
            long sig = CanonicalSig(zb, relevantAttrs);
            if (seenSig.Add(sig) && scorable.Contains(sig)) scorableNow++;
        }
        // Gradient: je näher der aktuelle Zustand an einem scorenden liegt, desto
        // besser (auch wenn JETZT noch nichts scort) — treibt den Beam gezielt durch
        // die nötige Zug-Sequenz (Switch-Flip / Sticky-Befreiung / Insel-Parken).
        // Unbekannt/unerreichbar = neutral.
        double gradient = reach.DistanceToScore(st.Grid) is int d ? (50 - d) * 5.0 : 0.0;
        return st.Survivors * 100000.0 + scorableNow * 100.0 + gradient;
    }

    private sealed record BeamState(
        BubblewonderGridModel Grid, HashSet<SimZb> Remaining,
        int Survivors, IReadOnlyList<Assignment> Path);

    /// <summary>State-Key mit Equivalence-Klassen-Counts. ZBs werden auf ihre
    /// kanonische Signatur reduziert (= nur relevante Attribute), und im Key
    /// stehen die Counts pro Signatur — Permutations-symmetrische States haben
    /// den identischen Key.</summary>
    private static string StateKeyEquivalence(
        BubblewonderGridModel grid, IReadOnlyCollection<SimZb> remaining, byte[] relevantAttrs)
    {
        var sb = new System.Text.StringBuilder();
        // Counts pro Signatur (Hauptpool)
        var counts = new Dictionary<long, int>();
        foreach (var zb in remaining)
        {
            long sig = CanonicalSig(zb, relevantAttrs);
            counts[sig] = counts.GetValueOrDefault(sig) + 1;
        }
        foreach (var (sig, cnt) in counts.OrderBy(kv => kv.Key))
            sb.Append(sig).Append('x').Append(cnt).Append(',');
        sb.Append('|');
        foreach (var (pos, idx) in grid.State.SwitchStateByCell.OrderBy(kv => kv.Key))
            sb.Append(pos).Append(':').Append(idx).Append(',');
        sb.Append('|');
        // Sticky: Position + Signatur des gefangenen ZBs (nicht HeaderId)
        foreach (var (pos, zb) in grid.State.StickyTrappedByCell.OrderBy(kv => kv.Key))
            sb.Append(pos).Append(':').Append(CanonicalSig(zb, relevantAttrs)).Append(',');
        sb.Append('|');
        // Geparkt pro Maschine: Counts pro Signatur
        foreach (var (mIdx, plist) in grid.State.ParkedZbsByMachineIdx.OrderBy(kv => kv.Key))
        {
            sb.Append(mIdx).Append(':');
            var pCounts = new Dictionary<long, int>();
            foreach (var p in plist)
            {
                long sig = CanonicalSig(p, relevantAttrs);
                pCounts[sig] = pCounts.GetValueOrDefault(sig) + 1;
            }
            foreach (var (sig, cnt) in pCounts.OrderBy(kv => kv.Key))
                sb.Append(sig).Append('x').Append(cnt).Append(',');
            sb.Append(';');
        }
        return sb.ToString();
    }

    internal static int? FindParkedMachine(GridState state, SimZb zb)
    {
        foreach (var (mIdx, list) in state.ParkedZbsByMachineIdx)
            if (list.Any(z => z.HeaderId == zb.HeaderId)) return mIdx;
        return null;
    }

    /// <summary>Vollständige Zustands-Signatur für die PLAN-STABILITÄT: welche
    /// ZB-Signaturen (Equivalence-Klassen) im Pool / auf Inseln / in Klebefallen
    /// sitzen, plus die Switch-Stellungen. Real-gelesen und (re-)simuliert liefern
    /// bei gleichem Spielzustand denselben String — kein Doppelzählen (jede ZB-
    /// Kategorie genau einmal).</summary>
    /// <summary>Diagnose: Zustands-Signatur eines Grids (für das Plan-Log).
    /// Berechnet die relevanten Attribute selbst.</summary>
    public static string DebugStateSignature(BubblewonderGridModel grid, IEnumerable<SimZb> poolZbs) =>
        FullStateSignature(grid, poolZbs, RelevantAttributes(grid));

    internal static string FullStateSignature(
        BubblewonderGridModel grid, IEnumerable<SimZb> poolZbs, byte[] relevantAttrs)
    {
        var sb = new System.Text.StringBuilder();
        void AppendCounts(IEnumerable<SimZb> zbs, char tag)
        {
            var c = new Dictionary<long, int>();
            foreach (var z in zbs) { long s = CanonicalSig(z, relevantAttrs); c[s] = c.GetValueOrDefault(s) + 1; }
            sb.Append(tag);
            foreach (var (s, n) in c.OrderBy(kv => kv.Key)) sb.Append(s).Append('x').Append(n).Append(',');
            sb.Append('|');
        }
        AppendCounts(poolZbs, 'P');
        AppendCounts(grid.State.ParkedZbsByMachineIdx.SelectMany(kv => kv.Value), 'I');
        // Klebefallen POSITIONS-basiert (NICHT attribut-basiert): ein live gelesener
        // gefangener ZB ist attributlos (Stub (0,0,0,0) — die Sticky-Zelle hält in +0x86
        // nur einen Engine-Handle, keine Attribute; BuildGridState), während die Vorwärts-
        // Simulation (HandleSticky) den ZB mit ECHTEN Attributen ablegt. Attribut-basiert
        // würde die F-Komponente daher NIE matchen, sobald ein ZB klebt → Dauer-„Abweichung"
        // bei jedem Trap-Ereignis (belegt: plan-log 15:51:59, F0x1→F0x2, beide CanonicalSig=0).
        // Die belegten Positionen werden dagegen zuverlässig gelesen und sind auf beiden
        // Seiten identisch → stabil, und eine ANDERE belegte Zelle gilt weiter als Abweichung.
        sb.Append('F');
        foreach (var pos in grid.State.StickyTrappedByCell.Keys.OrderBy(p => p))
            sb.Append(pos).Append(',');
        sb.Append('|');
        // Insel-Maschinen-Zellen aus dem Switch-Teil der PLAN-Signatur AUSSCHLIESSEN:
        // so eine Zelle ist gleichzeitig Switch (f0=4) UND Insel-Maschine; ihr +0x7C
        // ändert sich durch Insel-AKTIVITÄT (ZB kommt an / wird re-gelauncht), NICHT
        // durch einen vom Spieler ausgelösten Trigger. Im Plan-Stabilitäts-Vergleich
        // erzeugte dieser belegungs-gekoppelte Wert „Switch-Wechsel", die keiner echten
        // Schaltung entsprachen → Dauer-„Abweichung", obwohl der Spieler nur Deflektoren
        // durchlief (User-belegt 2026-06-04, memdump-144039: pos139=(10,9) Insel+Switch,
        // 139:3→0 ohne Trigger). Fürs ROUTING (Re-Launch-Richtung) bleibt der Wert in
        // SwitchStateByCell erhalten — er wird nur aus dem DEVIATIONS-Vergleich genommen.
        var islandCells = grid.Machines.Where(m => m.IsIsland)
            .Select(m => m.StartCellIndex).ToHashSet();
        foreach (var (pos, st) in grid.State.SwitchStateByCell.OrderBy(kv => kv.Key))
            if (!islandCells.Contains(pos))
                sb.Append(pos).Append(':').Append(st).Append(',');
        return sb.ToString();
    }

    /// <summary>Plan-Stabilität: Wo auf der zuvor berechneten Plan-Sequenz liegt der
    /// aktuelle Spielzustand? Re-simuliert den Plan ab <paramref name="baseGrid"/>
    /// Schritt für Schritt (gleiche Park-Behandlung wie der DFS) und vergleicht die
    /// <see cref="FullStateSignature"/> jedes Zwischenzustands mit dem aktuellen.
    ///
    /// <para>Rückgabe: Schritt-Index 0..N (= so viele Plan-Schritte wurden bereits
    /// ausgeführt) wenn der aktuelle Zustand AUF dem Plan-Pfad liegt; <c>null</c>
    /// wenn er ABWEICHT (dann soll der Aufrufer neu rechnen). Damit bleibt die
    /// Empfehlung stabil, solange der User dem Plan folgt — nur echte Abweichungen
    /// (anders platzierter ZB / Modell≠Realität) lösen eine Neuberechnung aus.</para></summary>
    public static int? LocateOnPlan(
        BubblewonderGridModel baseGrid, IReadOnlyList<SimZb> basePoolZbs,
        IReadOnlyList<Assignment> plan,
        BubblewonderGridModel currentGrid, IReadOnlyList<SimZb> currentPoolZbs)
    {
        var relevantAttrs = RelevantAttributes(baseGrid);
        string target = FullStateSignature(currentGrid, currentPoolZbs, relevantAttrs);

        var pool = new HashSet<SimZb>(basePoolZbs, new ZbHandleComparer());
        var grid = baseGrid;
        if (FullStateSignature(grid, pool, relevantAttrs) == target) return 0;

        for (int i = 0; i < plan.Count; i++)
        {
            var a = plan[i];
            int? pm = FindParkedMachine(grid.State, a.Zb);

            // TRANSIT-Zustand (Schritt i läuft GERADE): der ZB ist losgeschickt — aus dem
            // Pool bzw. von der Insel raus —, läuft aber noch auf dem Grid; sein Outcome
            // (Insel/Falle/Switch-Flip) ist noch NICHT sichtbar. VOR der Modifikation merken.
            BubblewonderGridModel transitGrid;
            var transitPool = pool;
            if (pm is { } parkedT)
            {
                var ts = grid.CloneState();
                ts.ParkedZbsByMachineIdx[parkedT].RemoveAll(z => z.HeaderId == a.Zb.HeaderId);
                transitGrid = grid.WithState(ts);
            }
            else
            {
                transitGrid = grid;
                transitPool = new HashSet<SimZb>(pool, new ZbHandleComparer());
                transitPool.RemoveWhere(z => z.HeaderId == a.Zb.HeaderId);
            }

            // Voller Schritt: ZB läuft bis zum Outcome.
            RunResult run;
            if (pm is { } parkedMachine)
            {
                var pre = grid.CloneState();
                pre.ParkedZbsByMachineIdx[parkedMachine].RemoveAll(z => z.HeaderId == a.Zb.HeaderId);
                run = BubblewonderRunner.RunSingle(grid.WithState(pre), a.Zb, parkedMachine);
            }
            else
            {
                run = BubblewonderRunner.RunSingle(grid, a.Zb, a.MachineIdx);
                pool.RemoveWhere(z => z.HeaderId == a.Zb.HeaderId);
            }
            // Terminale Outcomes (nicht Parked) verlassen das Spiel → aus Pool raus.
            foreach (var o in run.Outcomes)
                if (o.Outcome != SimOutcome.Parked)
                    pool.RemoveWhere(z => z.HeaderId == o.Zb.HeaderId);
            grid = run.FinalGrid;

            // Post-Outcome bevorzugt (höherer Fortschritt; deckt scorende ZBs, deren
            // Transit==Post ist). Sonst Transit: ZB ist sichtbar unterwegs (z.B. zur Insel,
            // die erst beim Ankommen +1 zählt) → KEINE Abweichung.
            if (FullStateSignature(grid, pool, relevantAttrs) == target) return i + 1;
            if (FullStateSignature(transitGrid, transitPool, relevantAttrs) == target) return i;
        }
        return null;  // aktueller Zustand nicht auf dem Plan-Pfad → Abweichung
    }

    private sealed class ZbHandleComparer : IEqualityComparer<SimZb>
    {
        public bool Equals(SimZb? x, SimZb? y) => x?.HeaderId == y?.HeaderId;
        public int GetHashCode(SimZb obj) => obj.HeaderId.GetHashCode();
    }

    /// <summary>Reaktiver Modus: User hat einen ZB hochgehoben. Bewertet pro
    /// Maschine was passiert wenn er DIESEN ZB jetzt darüber schickt, plus
    /// "was wäre wenn er ihn zurücklegt" als Vergleichsbaseline.
    ///
    /// <para>Entscheidet so die Frage: "Schick diesen ZB jetzt durch — wo lang —
    /// oder leg ihn besser zurück und nimm einen anderen?"</para></summary>
    public static HeldZbEvaluation EvaluateHeldZb(
        BubblewonderGridModel grid, SimZb heldZb, IReadOnlyList<SimZb> remainingPool)
    {
        // Baseline: ZB zurücklegen → der ganze Pool (heldZb + remaining) wird
        // optimal gelöst.
        var fullPool = remainingPool.Append(heldZb).ToList();
        int ifKept = SolveAvailable(grid, fullPool);

        // Pro Maschine: ZB jetzt darüber schicken, dann Folgepool optimal.
        int bestTotal = -1;
        var perMachine = new List<MachineEvaluation>(grid.Machines.Count);
        for (int m = 0; m < grid.Machines.Count; m++)
        {
            var run = BubblewonderRunner.RunSingle(grid, heldZb, m);
            int followup = SolveAvailable(run.FinalGrid, remainingPool);
            int total = run.SurvivorCount + followup;
            var heldOutcome = run.Outcomes.Count > 0 ? run.Outcomes[0].Outcome : SimOutcome.Dead;
            perMachine.Add(new MachineEvaluation(m, heldOutcome, run.SurvivorCount,
                followup, total, IsBest: false));
            if (total > bestTotal) bestTotal = total;
        }
        // IsBest setzen: alle Maschinen mit max-total markieren.
        for (int i = 0; i < perMachine.Count; i++)
        {
            if (perMachine[i].TotalSurvivors == bestTotal && bestTotal >= ifKept)
                perMachine[i] = perMachine[i] with { IsBest = true };
        }
        bool keepIsBest = ifKept > bestTotal;
        int overallMax = Math.Max(ifKept, bestTotal);
        return new HeldZbEvaluation(heldZb, perMachine, ifKept, overallMax, keepIsBest);
    }

    /// <summary>Wählt automatisch Brute-Force oder Greedy je nach Pool-Größe.
    /// Liefert die Survivor-Anzahl der besten gefundenen Sequenz.</summary>
    private static int SolveAvailable(BubblewonderGridModel grid, IReadOnlyList<SimZb> pool)
    {
        if (pool.Count == 0) return 0;
        if (grid.Machines.Count == 0) return 0;
        var result = pool.Count <= BruteForceMaxZbs
            ? SolveBruteForce(grid, pool)
            : SolveGreedy(grid, pool);
        return result.Survivors;
    }
}

public sealed record MachineEvaluation(
    int MachineIdx,
    SimOutcome HeldOutcome,
    int HeldZbAndImmediateFollowups,
    int FollowupSurvivors,
    int TotalSurvivors,
    bool IsBest);

public sealed record HeldZbEvaluation(
    SimZb HeldZb,
    IReadOnlyList<MachineEvaluation> PerMachine,
    int IfKeptSurvivors,
    int OverallMaxSurvivors,
    bool KeepIsBest);

/// <summary>Eine geplante Aktion: schicke <see cref="Zb"/> über Maschine
/// <see cref="MachineIdx"/>.</summary>
public sealed record Assignment(SimZb Zb, int MachineIdx);

public sealed record SolverResult(
    IReadOnlyList<Assignment> Assignments,
    int Survivors,
    string Strategy);

/// <summary>Live-Fortschritt einer laufenden DFS-Suche (info-only, für die UI).
/// <see cref="Nodes"/> wird per <see cref="System.Threading.Interlocked"/>
/// hochgezählt; <see cref="BestSurvivors"/> ist die bisher beste Survivor-Zahl.</summary>
public sealed class SolverProgress
{
    public long Nodes;
    public int BestSurvivors;

    /// <summary>Anteil des (geprunten) Suchbaums der bereits vollständig
    /// abgesucht ist, 0.0 .. 1.0. Erreicht genau 1.0 wenn die Suche regulär
    /// fertig ist (bei Timeout/Abbruch bleibt der Wert &lt; 1.0 stehen und zeigt
    /// damit ehrlich wie weit die Suche kam). Schreibt nur der Solver-Thread
    /// (Single-Writer), die UI liest nur — ein leicht veralteter Lesewert ist
    /// für eine Fortschrittsanzeige unkritisch.</summary>
    public double Fraction;

    /// <summary>Aktueller Fortschritt als Prozent (0 .. 100), gedeckelt.</summary>
    public double Percent => Math.Min(100.0, Fraction * 100.0);

    /// <summary>Geschätzte Gesamt-Knotenzahl des Suchbaums, hochgerechnet aus
    /// den bisher besuchten Knoten und dem bereits abgesuchten Anteil. Der Wert
    /// stabilisiert sich während die Suche läuft. 0 solange noch zu wenig
    /// abgesucht ist, um sinnvoll hochzurechnen.</summary>
    public long EstimatedTotalNodes
    {
        get
        {
            double f = Fraction;
            long n = System.Threading.Interlocked.Read(ref Nodes);
            return f > 0.0005 ? (long)(n / f) : 0;
        }
    }

    /// <summary>Anzahl der Zweige an der Wurzel des Suchbaums (= mögliche erste
    /// Züge: Repräsentant-ZB × Maschine). Wird einmal gesetzt sobald die Wurzel
    /// ihre Optionen aufgebaut hat. Das ist die <b>immer sichtbare, grobe</b>
    /// Fortschrittsachse — im Gegensatz zu <see cref="Fraction"/> hängt sie nicht
    /// vom (anfangs winzigen) Anteil tiefer Blätter ab.</summary>
    public int RootBranchesTotal;

    /// <summary>Anzahl der Wurzel-Zweige die bereits vollständig abgesucht sind.
    /// Steigt monoton bis <see cref="RootBranchesTotal"/>.</summary>
    public int RootBranchesDone;

    /// <summary>Grober, stets sichtbarer Fortschritt als Prozent (0 .. 100) über
    /// die abgeschlossenen Wurzel-Zweige. 0 solange die Wurzel ihre Optionen noch
    /// nicht kennt.</summary>
    public double RootPercent =>
        RootBranchesTotal > 0 ? 100.0 * RootBranchesDone / RootBranchesTotal : 0.0;
}
