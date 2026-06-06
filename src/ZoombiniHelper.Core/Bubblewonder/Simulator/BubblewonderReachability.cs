namespace ZoombiniHelper.Bubblewonder.Simulator;

/// <summary>
/// Reachability-Analyse über den <b>vollständigen</b> Bubblewonder-Spielzustand
/// (Switch-Stellungen + Sticky-Belegung + Insel-Parken), als Heuristik-Grundlage
/// für den Planer.
///
/// <para><b>Problem, das diese Klasse löst:</b> In manchen Layouts (z.B. REGS
/// 16608) scort aus dem Kaltstart kein ZB — der Weg zum Ziel öffnet sich erst
/// durch eine SEQUENZ von Zügen (Trigger flippen Switches, ZBs füllen/befreien
/// Stickys, ZBs parken auf der Insel und starten neu). Greedy bleibt dann bei 0,
/// und eine Survivor-basierte Heuristik ist „flach" (überall 0) — die Suche hat
/// keinen Gradienten und verliert sich.</para>
///
/// <para><b>Ansatz:</b> Wir bauen per BFS den Graphen der vom Start aus
/// erreichbaren Spielzustände auf (Übergang = „schicke einen ZB einer Signatur
/// von einer Maschine/Insel los", mit der echten <see cref="BubblewonderRunner"/>-
/// Mechanik inkl. Folge-ZBs). Zustände, in denen irgendein Zug einen Survivor
/// liefert, sind „scorend". Eine Rückwärts-BFS liefert für jeden Zustand die
/// minimale Zugzahl bis zu einem scorenden Zustand — das ist der Gradient, den
/// der Planer braucht.</para>
///
/// <para><b>Abstraktion/Konfidenz:</b> ZBs werden auf ihre routing-relevante
/// Signatur reduziert und als unbegrenzt verfügbar angenommen (eine Sig kann
/// mehrfach geschickt werden). Das ist eine <i>Über-Approximation</i> der
/// Erreichbarkeit: ein als erreichbar markierter Scoring-Zustand könnte real
/// mehr ZBs einer Sig brauchen als vorhanden. Für eine <b>Heuristik</b> (Such-
/// Lenkung, nicht Korrektheits-Beweis) ist das genau richtig — sie lenkt zum
/// Ziel, der eigentliche Planer prüft die konkrete Machbarkeit.</para>
/// </summary>
public sealed class BubblewonderReachability
{
    private readonly Dictionary<string, int> _distToScore;

    /// <summary>Anzahl vom Start aus erkundeter Zustände (Diagnose).</summary>
    public int ExploredStates { get; }

    /// <summary>True wenn die Vorwärts-BFS den erreichbaren Zustandsraum
    /// VOLLSTÄNDIG erkundet hat (Queue leer gelaufen, NICHT am <c>maxStates</c>-Deckel
    /// abgebrochen). Nur dann ist <see cref="AnyScoringStateReachable"/>==false ein
    /// belastbarer Beweis, dass im Modell kein Ziel erreichbar ist (die Über-
    /// Approximation mit unbegrenzten ZBs kann nur MEHR Zustände erreichen als real,
    /// also: erreicht selbst sie keinen scorenden Zustand, gibt es real auch keinen).
    /// Bei einem gedeckelten Lauf ist die Map unvollständig → kein Schluss möglich.</summary>
    public bool Complete { get; }

    private BubblewonderReachability(Dictionary<string, int> distToScore, int explored, bool complete)
    {
        _distToScore = distToScore;
        ExploredStates = explored;
        Complete = complete;
    }

    /// <summary>Minimale Zugzahl von diesem Zustand bis zu einem scorenden
    /// Zustand. 0 = hier scort schon ein Zug. <c>null</c> = nicht im Graphen
    /// (unerreichbar oder Analyse gedeckelt) → Heuristik bleibt neutral.</summary>
    public int? DistanceToScore(BubblewonderGridModel grid) =>
        _distToScore.TryGetValue(StateKey(grid, RelevantAttributesOf(grid)), out var d) ? d : null;

    /// <summary>True wenn die Analyse einen scorenden Zustand gefunden hat — dann
    /// existiert (über-approximativ) ein Weg zu mindestens einem Survivor.</summary>
    public bool AnyScoringStateReachable => _distToScore.ContainsValue(0);

    /// <summary>Anzahl analysierter Zustände (Diagnose).</summary>
    public int StateCount => _distToScore.Count;

    private byte[]? _relevantAttrsCache;
    private byte[] RelevantAttributesOf(BubblewonderGridModel grid) =>
        _relevantAttrsCache ??= BubblewonderSolver.RelevantAttributes(grid);

    /// <summary>Baut die Reachability-Map. <paramref name="maxStates"/> deckelt
    /// die BFS gegen pathologische Layouts; wird sie erreicht, enthält die Map nur
    /// den bis dahin erkundeten Teil (Heuristik bleibt für den Rest neutral).</summary>
    public static BubblewonderReachability Analyze(
        BubblewonderGridModel grid, IReadOnlyList<SimZb> zbs, int maxStates = 30000)
    {
        var relevantAttrs = BubblewonderSolver.RelevantAttributes(grid);

        // Ein Repräsentant pro Signatur — der „Vorrat" für Reachability-Züge.
        var sigReps = new Dictionary<long, SimZb>();
        foreach (var z in zbs)
            sigReps.TryAdd(BubblewonderSolver.CanonicalSig(z, relevantAttrs), z);
        var poolReps = sigReps.Values.ToList();

        var startKey = StateKey(grid, relevantAttrs);
        var stateByKey = new Dictionary<string, BubblewonderGridModel> { [startKey] = grid };
        var edges = new Dictionary<string, List<string>> { [startKey] = new() };
        var scoring = new HashSet<string>();

        var queue = new Queue<string>();
        queue.Enqueue(startKey);
        bool capped = false;
        while (queue.Count > 0)
        {
            if (stateByKey.Count > maxStates) { capped = true; break; }
            var key = queue.Dequeue();
            var state = stateByKey[key];

            // „Verfügbare" ZBs für Optionen: alle Pool-Signaturen (unbegrenzt) plus
            // die aktuell geparkten Repräsentanten (für Insel-Starts).
            var available = new List<SimZb>(poolReps);
            foreach (var (_, parked) in state.State.ParkedZbsByMachineIdx)
                available.AddRange(parked);

            var outList = edges[key];
            foreach (var opt in BubblewonderSolver.GenerateOptions(state, available, relevantAttrs))
            {
                if (opt.Run.SurvivorCount > 0) scoring.Add(key);
                var nextKey = StateKey(opt.Run.FinalGrid, relevantAttrs);
                outList.Add(nextKey);
                if (!stateByKey.ContainsKey(nextKey))
                {
                    stateByKey[nextKey] = opt.Run.FinalGrid;
                    edges[nextKey] = new List<string>();
                    queue.Enqueue(nextKey);
                }
            }
        }

        // Rückwärts-BFS von allen scorenden Zuständen.
        var rev = new Dictionary<string, List<string>>();
        foreach (var (from, tos) in edges)
            foreach (var to in tos)
                (rev.TryGetValue(to, out var l) ? l : rev[to] = new List<string>()).Add(from);

        var dist = new Dictionary<string, int>();
        var bfs = new Queue<string>();
        foreach (var s in scoring) { dist[s] = 0; bfs.Enqueue(s); }
        while (bfs.Count > 0)
        {
            var c = bfs.Dequeue();
            if (!rev.TryGetValue(c, out var preds)) continue;
            foreach (var p in preds)
                if (!dist.ContainsKey(p)) { dist[p] = dist[c] + 1; bfs.Enqueue(p); }
        }
        return new BubblewonderReachability(dist, stateByKey.Count, complete: !capped);
    }

    /// <summary>Kanonischer Schlüssel des Spielzustands: Switch-Stellungen +
    /// Sticky-Belegung (per ZB-Signatur) + Insel-Parken (per Signatur). Zwei
    /// Zustände mit gleichem Schlüssel verhalten sich routing-identisch.</summary>
    private static string StateKey(BubblewonderGridModel grid, byte[] relevantAttrs)
    {
        var s = grid.State;
        var sb = new System.Text.StringBuilder();
        foreach (var (pos, idx) in s.SwitchStateByCell.OrderBy(kv => kv.Key))
            sb.Append(pos).Append(':').Append(idx).Append(',');
        sb.Append('|');
        foreach (var (pos, zb) in s.StickyTrappedByCell.OrderBy(kv => kv.Key))
            sb.Append(pos).Append(':').Append(BubblewonderSolver.CanonicalSig(zb, relevantAttrs)).Append(',');
        sb.Append('|');
        foreach (var (mIdx, list) in s.ParkedZbsByMachineIdx.OrderBy(kv => kv.Key))
        {
            sb.Append(mIdx).Append(':');
            foreach (var sig in list.Select(z => BubblewonderSolver.CanonicalSig(z, relevantAttrs)).OrderBy(x => x))
                sb.Append(sig).Append('.');
            sb.Append(';');
        }
        return sb.ToString();
    }
}
