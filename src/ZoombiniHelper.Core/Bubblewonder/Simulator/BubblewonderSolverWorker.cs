namespace ZoombiniHelper.Bubblewonder.Simulator;

/// <summary>
/// Asynchroner Solver: rechnet die optimale Lösung im Background-Thread.
/// UI bleibt während langer Compute-Zeiten responsive (auch auf alter HW).
///
/// <para>Workflow:</para>
/// <list type="number">
///   <item><see cref="StartCompute"/> spawnt einen Background-Task.</item>
///   <item><see cref="Status"/> zeigt "🔄 Rechne…" oder "✓ Fertig" oder "⚠ Fehler".</item>
///   <item><see cref="LatestResult"/> ist null bis fertig, danach das beste
///         gefundene Ergebnis.</item>
///   <item>Bei neuem <see cref="StartCompute"/> wird die laufende Berechnung
///         per CancellationToken abgebrochen — kein Race.</item>
/// </list>
/// </summary>
public sealed class BubblewonderSolverWorker : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private readonly object _lock = new();

    /// <summary>Sicherheitsnetz gegen Endlos-Suche. Bei korrektem Modell finishen
    /// Layouts weit darunter (Greedy-Floor &gt; 0 → Pruning greift → Sekunden). Greift
    /// dieses Limit überhaupt, ist das ein Warnsignal: entweder pathologisch großer
    /// Suchraum ODER (wie bei REGS 16608) ein Modell-Bug, der Greedy=0 liefert und
    /// damit das Pruning aushebelt. Dann kommt die beste gefundene Lösung mit klarer
    /// „nicht garantiert optimal"-Markierung statt eines Hängers.</summary>
    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(60);

    public SolverResult? LatestResult { get; private set; }
    /// <summary>Grid + Pool, auf denen <see cref="LatestResult"/> berechnet wurde
    /// (für die Plan-Stabilität: der Renderer verfolgt den realen Verlauf relativ
    /// zu diesem Ausgangszustand). Atomar zusammen mit LatestResult gesetzt.</summary>
    public BubblewonderGridModel? ResultBaseGrid { get; private set; }
    public IReadOnlyList<SimZb>? ResultBasePool { get; private set; }
    public string Status { get; private set; } = "(noch nicht gestartet)";
    public bool IsRunning => _runningTask is { IsCompleted: false };
    public TimeSpan? LastDuration { get; private set; }
    /// <summary>Während compute: bisheriger best-found Survivor-Count (info-only,
    /// NICHT als Plan-Output verwendbar weil noch nicht optimal).</summary>
    public int? RunningBestSurvivors { get; internal set; }
    /// <summary>Live-Fortschritt der laufenden Suche (Knoten + beste bisher).</summary>
    public SolverProgress? Progress { get; private set; }
    /// <summary>UTC-Start der laufenden Berechnung (für die verstrichene Zeit
    /// in der Fortschrittsanzeige). null wenn nichts läuft.</summary>
    public DateTime? StartedAt { get; private set; }

    public void StartCompute(BubblewonderGridModel grid, IReadOnlyList<SimZb> zbs)
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var startedAt = DateTime.UtcNow;
            StartedAt = startedAt;
            var progress = new SolverProgress();
            Progress = progress;
            // Vorigen Result löschen — bis das neue Compute fertig ist gibt's KEINE Empfehlung.
            LatestResult = null;
            RunningBestSurvivors = null;
            // Gesamt-ZB-Zahl = Haupt-Pool + auf Inseln geparkte + in Klebefallen
            // gefangene ZBs. Alle drei sind „im Spiel" und können noch gerettet
            // werden (geparkte per Re-Launch, gefangene per Trigger-/Anstups-
            // Befreiung). Der Nenner muss alle zeigen, sonst „verschwinden" sie optisch.
            int totalZbs = zbs.Count
                + grid.State.ParkedZbsByMachineIdx.Sum(kv => kv.Value.Count)
                + grid.State.StickyTrappedByCell.Count;
            Status = $"🔄 Rechne optimale Lösung… ({totalZbs} ZBs)";

            // Hintergrund-Thread mit niedriger Priorität — auf alter
            // Single-Core-HW darf der Solver das Spiel nicht ausbremsen.
            // Das OS gibt dem Solver weniger CPU wenn das Spiel arbeiten will.
            _runningTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    var origPrio = System.Threading.Thread.CurrentThread.Priority;
                    System.Threading.Thread.CurrentThread.Priority =
                        System.Threading.ThreadPriority.BelowNormal;
                    try
                    {
                        // Zeitbudget als Sicherheitsnetz: bei korrektem Modell
                        // finisht der Solver weit darunter mit bewiesenem Optimum;
                        // greift das Limit, kommt die beste gefundene Lösung.
                        var result = BubblewonderSolver.SolveDfs(
                            grid, zbs, TimeBudget, ct,
                            onNewBest: count => { lock (_lock) RunningBestSurvivors = count; },
                            progress: progress);
                        if (ct.IsCancellationRequested) return;
                        var dur = DateTime.UtcNow - startedAt;
                        // Drei Ausgänge unterscheiden: (a) Modell-Befund „kein Ziel
                        // erreichbar" (beweisbar unlösbar IM MODELL → bei real 100%-
                        // lösbaren Boards = Layout-Lesefehler, nicht Solver-Schwäche),
                        // (b) bewiesenes Optimum, (c) Zeitlimit mit bester gefundener Lösung.
                        bool noGoal = result.Strategy.Contains("kein Ziel erreichbar");
                        bool optimal = result.Strategy.Contains("optimal");
                        lock (_lock)
                        {
                            LatestResult = result;
                            ResultBaseGrid = grid;
                            ResultBasePool = zbs;
                            LastDuration = dur;
                            Status = noGoal
                                ? $"⚠ Kein Ziel im Modell erreichbar (0/{totalZbs}) — Layout-Lesefehler? "
                                  + $"({dur.TotalSeconds:F1}s, {result.Strategy})"
                                : optimal
                                ? $"✓ Optimal: {result.Survivors}/{totalZbs} ({dur.TotalSeconds:F1}s, {progress.Nodes:N0} Knoten)"
                                : $"⏱ Zeitlimit {dur.TotalSeconds:F0}s: beste gefundene {result.Survivors}/{totalZbs} "
                                  + $"(nicht garantiert optimal · {progress.Nodes:N0} Knoten, {progress.RootPercent:F0} % der Startzüge)";
                        }
                    }
                    finally
                    {
                        System.Threading.Thread.CurrentThread.Priority = origPrio;
                    }
                }
                catch (Exception ex)
                {
                    lock (_lock) Status = $"⚠ Solver-Fehler: {ex.Message}";
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            Status = "(abgebrochen)";
        }
    }

    public void Dispose() => Cancel();
}
