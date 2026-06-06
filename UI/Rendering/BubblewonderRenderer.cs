using System.Drawing;
using System.Text;
using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Bubblewonder.Simulator;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Helper für "Bubblewonder Abyss" (maze2.mhk).
///
/// <para><b>Spielmechanik (User-Modell)</b>: Conditional-Filter-Cells lenken
/// ZBs um die das geforderte Attribut HABEN. ZBs ohne das Attribut laufen
/// einfach durch. Switches/Toggles ändern Routing dynamisch. Trap-Cells
/// sind gelbe Killer und müssen vermieden werden. Routing-Cells
/// (= "Pfeile" auf Bubbles ohne REGS-Mechanismus) lenken jeden ZB in eine
/// feste Richtung.</para>
///
/// <para><b>Hauptansicht</b>: für jeden gerade laufenden oder zuletzt
/// abgeschlossenen ZB der vollständige Pfad mit Cell-Type-Annotation pro
/// Schritt. So sieht der User direkt welche Mechanismen den ZB beeinflusst
/// haben (Conditional-Match), wo Routing-Pfeile waren, und wann er
/// raus war.</para>
/// </summary>
public sealed class BubblewonderRenderer : IPuzzleRenderer
{
    private readonly BubblewonderTracker _tracker;
    private readonly BubblewonderSolverWorker _solver = new();

    // --- Plan-Stabilität ---
    // Der aktive (fixierte) Plan + der Zustand, auf dem er berechnet wurde. Solange
    // der reale Spielverlauf AUF diesem Plan liegt (LocateOnPlan != null), bleibt die
    // Empfehlung stabil und es wird NICHT neu gerechnet. Nur bei echter Abweichung
    // (anders platzierter ZB / Modell≠Realität) wird der Plan verworfen + neu gerechnet.
    private SolverResult? _activePlan;
    private BubblewonderGridModel? _planBaseGrid;
    private List<SimZb>? _planBasePool;
    private bool _lastWasDeviation;
    // Letzte gesehene Tracker-RoundEpoch — bei Änderung (neue Runde/Layout) wird der
    // Plan sauber verworfen statt fälschlich als „Abweichung" behandelt. -1 = noch keine.
    private int _lastRoundEpoch = -1;
    // Wie viele Plan-Schritte bereits ausgeführt sind (= Index des NÄCHSTEN empfohlenen
    // Schritts). Die Empfehlung zeigt Assignments[_planStep], NICHT immer [0] — so rückt
    // sie weiter, während der Plan ERHALTEN bleibt (z.B. ein ZB erreicht planmäßig die
    // Insel = Schritt erledigt, NICHT Abweichung). Monoton vorwärts geklemmt, damit der
    // Insel-/Transit-Scan-Flicker den Zeiger nicht auf einen schon erledigten Schritt
    // (z.B. „schick ZB über Spawn-Maschine") zurückwirft. Reset auf 0 bei neuem Plan.
    private int _planStep;

    // Entprellung gegen transiente Pool-Scan-Glitches (ZBs verschwinden kurz während
    // einer Wurf-Animation und kommen wieder): eine Abweichung wird erst dann als ECHT
    // behandelt und löst Neuberechnung aus, wenn dieselbe abweichende Signatur länger
    // als DeviationDebounce anhält. Ein kurzer Glitch (Sig springt zurück) wird ignoriert.
    private string? _pendingDeviationSig;
    private DateTime _pendingDeviationSince;
    // KONSERVATIV (2026-06-04): nur neu rechnen, wenn die Abweichung STABIL UND SETTLED
    // (kein ZB unterwegs) über ein LÄNGERES Fenster anhält. Race-/Übergangs-Mismatches
    // beim Switch-Auslösen / Sticky-Belegen / Insel-Eintreffen lösen sich in <3s auf (der
    // Live-Read ist mitten im Update: Switch-Bit schon gekippt, ZB-Pos noch alt etc.) und
    // dürfen KEINE teure 60s-Neuberechnung auslösen (die manchmal nur 13/16 findet). Nur
    // ein echter, anhaltender Plan-Bruch (settled + stabil off-plan > Fenster) rechtfertigt
    // den Recompute. (Vorher 1s → zu kurz, bestätigte transiente Übergänge fälschlich.)
    private static readonly TimeSpan DeviationDebounce = TimeSpan.FromMilliseconds(3500);

    // --- Plan-Stabilität-Diagnose-Log ---
    // Schreibt jede Plan-Entscheidung (stabil / Abweichung / neu gerechnet) in eine
    // Datei neben der EXE (ggf. ein Sync-Ordner). Nur bei ÄNDERUNG, damit
    // kein Tick-Spam. So sehen wir nach einer gespielten Runde GENAU, was der Solver
    // Zug für Zug entschieden hat — ohne auf den F12-Zeitpunkt angewiesen zu sein.
    private string? _planLogPath;
    private string _lastLogLine = "";

    // --- Spawn-Zellen-Persistenz (pro REGS,Variant) ---
    // Der Insel-Re-Launch-Spawn ist NICHT statisch (variant-abhängig) → wir lernen ihn
    // live (Tracker erkennt Re-Launch von einer Insel-Zelle) und merken ihn dauerhaft in
    // einer Datei neben der EXE. So ist jede (REGS,Variant) nach dem ersten Re-Launch
    // für immer korrekt — der Grund, warum frühere (nur-Session-)Lernversuche „nur
    // kurzzeitig" hielten.
    private BubblewonderSpawnStore? _spawnStore;
    private string? _spawnStorePath;
    private static readonly object _planLogLock = new();
    /// <summary>Letzter Pool-Stand wenn KEIN Drag aktiv war. Während Drag hat
    /// der gedragger ZB y=0xFFFD (off-stage), das würde ihn in den Insel-
    /// Cluster werfen und den Solver fälschlich neu triggern. Stattdessen
    /// nutzen wir den letzten stabilen Stand.</summary>
    private List<PoolMember>? _stablePool;

    public BubblewonderRenderer(BubblewonderTracker tracker)
    {
        _tracker = tracker;
    }

    public PuzzleId Handles => PuzzleId.BubblewonderAbyss;

    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var s = BubblewonderState.Read(mem);
        labels.TitleColor = Color.FromArgb(150, 200, 230);
        labels.Title = Loc.T("bubble.title", s.Difficulty);

        // Drag-aware Pool: während Drag hat der gehobene ZB y=0xFFFD und würde
        // den Pool-Cluster verschieben → Solver triggert sinnlos neu. Cache
        // den letzten stabilen Stand und nutze ihn während Drag.
        bool dragActive = HeldZoombini.Find(mem) is not null;
        if (!dragActive) _stablePool = pool.ToList();
        pool = (_stablePool ?? pool.ToList()).AsReadOnly();

        // Gescorte ZBs aus Pool filtern: sie liegen im Output-Pool mit hohem y
        // (würden sonst fälschlich als "Insel-geparkt" geclustert werden).
        // _tracker.ScoredZbs enthält alle ZBs die ein ZbScored-Event hatten.
        var scoredIds = new HashSet<ushort>(_tracker.ScoredZbs);
        if (scoredIds.Count > 0)
            // NUR ECHT am Ziel (0x17) gescorte ZBs (+0x76=3) rausfiltern. Die Engine feuert
            // ein ZbScored-Event AUCH (a) beim Landen auf einer Zwischen-Insel (+0x76 ∈ {1,2}
            // — re-losschickbar, NICHT gescort) und (b) VERFRÜHT, während ein ZB noch übers
            // Grid LÄUFT (+0x76=0, in Transit). Beide dürfen NICHT rausgefiltert werden:
            //   - Insel (1/2): sonst verschwindet der ZB aus Pool UND Insel → Bestand falsch,
            //     Plan-Schritt friert (belegt memdump-203818: 0x000F auf (10,11), +0x76=2).
            //   - In-Transit (0): sonst verschwindet der LAUFENDE ZB aus dem Pool → Bestand
            //     zeigt 15/16, „unterwegs" fehlt, der In-Transit-Halt greift nicht → der
            //     laufende ZB fehlt in der Signatur → FALSCH-Abweichung mitten im Lauf
            //     (User-belegt 2026-06-04: „15 von 16 während Transit + Planabweichung,
            //     obwohl der Lauf der Vorhersage entspricht"). Nur +0x76=3 = echt gescort.
            pool = pool.Where(p => !scoredIds.Contains(p.HeaderId) || p.OutcomeType is 0 or 1 or 2)
                       .ToList().AsReadOnly();

        // Build per-Position lookup zu Live-Bubble (für Conditional-Variant aus +0x82/+0x84)
        var bubbleByPos = new Dictionary<(int, int), BubbleObject>();
        foreach (var b in s.LiveBubbles)
        {
            if (b.RegsRecordCopy.Count < 3) continue;
            int p1 = b.RegsRecordCopy[1], p2 = b.RegsRecordCopy[2];
            bubbleByPos[(p1, p2)] = b;
        }

        labels.Body = BuildBody(s, bubbleByPos, pool, mem);
    }

    private static readonly string[] AttrNames = { "Haar", "Augen", "Nase", "Füße" };

    /// <summary>Engine-Attribut-Code (+0x82) → unsere Attribut-Bezeichnung.
    /// Live-verifiziert: 01=Nase, 03=Haar (User 2026-05-01).
    /// 02 und 04 vermutet aus Lücken-Logik.</summary>
    private static string AttrFromEngineCode(ushort code) => code switch
    {
        1 => "Nase",
        2 => "Augen",   // unbestätigt
        3 => "Haar",
        4 => "Füße",    // unbestätigt
        _ => $"attr={code}",
    };

    /// <summary>Variante (1-5) → Variant-Name, je nach Attribut.</summary>
    private static string VariantNameFor(ushort attrCode, ushort variant)
    {
        // attrCode → Index in Standard-Attribut-Mapping
        int attrIdx = attrCode switch { 3 => 1, 2 => 2, 1 => 3, 4 => 4, _ => 0 };
        if (attrIdx == 0 || variant == 0) return $"V{variant}";
        return ZoombiniVariants.VariantName((byte)attrIdx, variant);
    }

    private string BuildBody(BubblewonderState s,
                             Dictionary<(int, int), BubbleObject> bubbleByPos,
                             IReadOnlyList<PoolMember> pool,
                             IMemoryReader mem)
    {
        var sb = new StringBuilder();

        if (!s.IsActive)
        {
            sb.AppendLine(Loc.T("bubble.waiting"));
            return sb.ToString();
        }

        // Mechanismus-Übersicht (statisch aus REGS)
        var traps = s.Grid.Mechanisms.Count(m => m.Type == MechanismType.Trap);
        var switches = s.Grid.Mechanisms.Count(m => m.Type == MechanismType.SwitchActivated);
        var conds = s.Grid.Mechanisms.Count(m => m.IsConditional);
        sb.AppendLine(Loc.T("bubble.grid", conds, switches, traps));
        sb.AppendLine();

        // Hinweis: die laufenden/durchgelaufenen ZB-Pfade werden NICHT mehr in
        // der Live-UI angezeigt — das war nur Clutter. Der Tracker merkt sie
        // sich weiterhin intern (ActivePaths/CompletedPaths) für den F12-Dump
        // und den Hawk-Modus; nur die ständige UI-Ausgabe ist raus.

        // Solver-Empfehlung: nächste(n) ZB(s) + Maschinen-Wahl
        AppendSolverRecommendation(sb, s, pool, mem, _tracker);

        return sb.ToString();
    }

    /// <summary>Reaktiver Solver-Renderer mit Background-Worker.</summary>
    private void AppendSolverRecommendation(
        StringBuilder sb, BubblewonderState s,
        IReadOnlyList<PoolMember> pool, IMemoryReader mem,
        BubblewonderTracker tracker)
    {
        // Rundenwechsel (neues Layout) → alten Plan SAUBER verwerfen, statt ihn gegen das
        // neue Layout zu vergleichen (→ falsche „Abweichung vom Plan" direkt beim Start).
        // RoundEpoch kommt aus dem Tracker (Heap-Pointer-basierte Runden-Erkennung).
        if (tracker.RoundEpoch != _lastRoundEpoch)
        {
            // Diagnose der „Neue Runde mitten im Spiel"-Frage: ein Epoch-Sprung von −1
            // (Feld-Init) = der Helper ist gerade GESTARTET (harmlos, einmalig). Jeder
            // andere Sprung = echtes Tracker-Reset (Layout-Wechsel ODER transienter
            // „inaktiv gelesen"-Glitch) — Grund steht in LastResetReason. So ist aus
            // dem Log allein erkennbar, ob eine Neuberechnung berechtigt war.
            bool helperStart = _lastRoundEpoch < 0;
            string why = helperStart
                ? "Helper-Start"
                : $"Rundenwechsel: {tracker.LastResetReason ?? "(unbekannt)"}";
            _lastRoundEpoch = tracker.RoundEpoch;
            _activePlan = null;
            _planBaseGrid = null;
            _planBasePool = null;
            _planStep = 0;
            _pendingDeviationSig = null;
            _lastWasDeviation = false;
            PlanLog($"Neue Runde (Epoch {tracker.RoundEpoch}, {why}) → Plan verworfen, rechne neu");
        }
        if (pool.Count == 0)
        {
            sb.AppendLine(Loc.T("bubble.pool.empty"));
            return;
        }
        BubblewonderGridModel grid;
        IReadOnlyList<PoolMember> mainPool, parked;
        try
        {
            var liveSpawns = tracker.ObservedSpawnPositions.ToArray();
            int? islandSpawn = ResolveIslandSpawn(s, tracker);
            grid = BubblewonderGridModelBuilder.FromState(s, mem,
                liveSpawnPositions: liveSpawns.Length > 0 ? liveSpawns : null,
                liveSpawnDirections: tracker.ObservedSpawnDirections,
                knownGoalCells: tracker.LearnedGoalCells,
                learnedIslandSpawn: islandSpawn);
            // Sticky-Attribute SOFORT (positions-basiert) füllen — damit
            // StickyTrappedByCell echte HeaderIds trägt, BEVOR wir Insel-vs-Falle
            // entscheiden. Der Dedup unten braucht echte HeaderIds; früher lief das
            // erst NACH der Insel-Klassifikation und konnte daher nicht dedupen.
            grid = BubblewonderGridModelBuilder.WithStickyAttributes(grid, pool);
            // Pool/Insel-Trennung über Handle + Zelltyp (NICHT y-Cluster): ein in
            // einer Falle steckender ZB ist losgeschickt, aber nicht auf einer
            // Zwischenstation → weder Pool noch Insel.
            (mainPool, parked) = BubblewonderPoolClassifier.Split(pool, mem);
            // STABILE Insel-Erkennung über das BEOBACHTETE Tracker-Endpoint: ZBs, deren
            // Pfad nachweislich auf einer Insel-Zelle endete, sind geparkt — auch wenn der
            // aktuelle Snapshot ihre Grid-Pos/+0x76 gerade verfehlt (flackert). Das ist der
            // robuste Fix für „Insel-ZB wird nicht erkannt": der Tracker WEISS es aus dem
            // Pfad. ZBs mit Drag-Handle (in der Hand) bleiben ausgenommen (nicht geparkt).
            var islandHdrs = tracker.GetIslandParkedHdrs(mem);
            if (islandHdrs.Count > 0)
            {
                var moved = mainPool.Where(p =>
                    islandHdrs.Contains(p.HeaderId) && p.Handle != ZoombiniHandle.Held).ToList();
                if (moved.Count > 0)
                {
                    var movedHdrs = moved.Select(p => p.HeaderId).ToHashSet();
                    mainPool = mainPool.Where(p => !movedHdrs.Contains(p.HeaderId)).ToList();
                    var parkedHdrs = parked.Select(p => p.HeaderId).ToHashSet();
                    parked = parked.Concat(moved.Where(p => parkedHdrs.Add(p.HeaderId))).ToList();
                }
            }
            // DE-DUP Insel vs. Klebefalle: ein ZB kann NICHT zugleich auf einer Insel
            // geparkt UND in einer Klebefalle gefangen sein. Die Sticky-Belegung wird
            // POSITIONS-basiert aus der Zelle gelesen (zuverlässig); die Insel-Klassifikation
            // kann auf einem STALEN +0x76=2 beruhen (ein von der Insel weitergelaufener ZB
            // behält den Wert). Beleg (memdump-144039): ZB 0x05 klebt real auf Sticky (10,5)
            // [Hawk-verifiziert: Sim==Live], trägt aber +0x76=2 → wurde als Insel UND als
            // Falle gezählt (17 statt 16 ZBs) → Phantom-Insel-ZB → der Planer baute einen
            // Insel-Re-Launch, der real keinen Insel-ZB hatte und stattdessen den echten
            // Insel-Zugang blockierte. Die physische Falle gewinnt: solche ZBs aus Insel
            // UND Hauptpool entfernen (sie sind über StickyTrappedByCell vollständig erfasst).
            if (grid.State.StickyTrappedByCell.Count > 0)
            {
                var trappedHdrs = grid.State.StickyTrappedByCell.Values
                    .Select(z => z.HeaderId).ToHashSet();
                int beforeP = parked.Count, beforeM = mainPool.Count;
                parked = parked.Where(p => !trappedHdrs.Contains(p.HeaderId)).ToList();
                mainPool = mainPool.Where(p => !trappedHdrs.Contains(p.HeaderId)).ToList();
                if (parked.Count != beforeP || mainPool.Count != beforeM)
                    PlanLog($"DEDUP Insel/Falle: {beforeP - parked.Count} aus Insel + " +
                            $"{beforeM - mainPool.Count} aus Pool entfernt (stale +0x76, real in Klebefalle)");
            }
            // INSEL-ZB-ATTRIBUTE aus dem Tracker-Cache nachfüllen: das Engine-Objekt
            // eines auf der Insel geparkten ZB liefert oft STUB-Attribute (0,0,0,0)
            // (es exponiert sie nicht wie ein Pool-ZB). Dann ist seine CanonicalSig 0,
            // während die Plan-Re-Simulation (LocateOnPlan) den ZB mit ECHTEN Attributen
            // parkt → die I-Komponente der Plan-Signatur matcht NIE → „Abweichung" bei
            // JEDER Insel-Landung (Beleg memdump-152811: ZB 0x12 (0,0,0,0) auf (10,11),
            // echt H2 E2 N5 F4). Der Tracker hat die echten Attribute gelernt, als der ZB
            // noch durchs Grid lief → von dort nachfüllen. (Analog WithStickyAttributes
            // für Klebefallen; ohne das blieb die Insel die letzte Stub-Quelle.)
            var knownAttrs = tracker.KnownZbAttrs;
            parked = parked.Select(p =>
                (p.Hair == 0 && p.Eyes == 0 && p.Nose == 0 && p.Feet == 0
                 && knownAttrs.TryGetValue(p.HeaderId, out var a))
                    ? p with { Hair = a.H, Eyes = a.E, Nose = a.N, Feet = a.F }
                    : p).ToList();
            grid = BubblewonderGridModelBuilder.WithParkedZbs(grid, parked);

            // DIAGNOSE (2026-05-31): pro Tick die ROH-Reads + Verdict jedes „interessanten"
            // ZB (nicht reiner Pool) ins Log — dedup'd, also werden Übergänge/Flackern
            // sichtbar. Beantwortet endgültig: liest der Renderer den Insel-ZB sauber
            // (dann ist es ein Plan-/Re-Eval-Bug) oder flackert der Read wirklich?
            var interesting = pool.Where(p =>
                p.Handle != ZoombiniHandle.Pool || (p.OutcomeType is 1 or 2 or 3)
                || (p.GridRow != 0 && p.GridRow != 0xFFFF)).ToList();
            if (interesting.Count > 0)
            {
                var parkedSet = parked.Select(p => p.HeaderId).ToHashSet();
                string det = string.Join("  ", interesting.Select(p =>
                    $"0x{p.HeaderId:X2}[h={p.Handle:X}+76={(p.OutcomeType == 0xFFFF ? -1 : p.OutcomeType)}" +
                    $"g=({(p.GridRow == 0xFFFF ? -1 : p.GridRow)},{(p.GridCol == 0xFFFF ? -1 : p.GridCol)})" +
                    $"{(parkedSet.Contains(p.HeaderId) ? "→INSEL" : "→pool")}]"));
                PlanLog($"READ-TRACE: {det}");
            }
            // (Sticky-Attribute werden bereits direkt nach FromState gefüllt — siehe oben.)
        }
        catch (Exception ex)
        {
            // Exception ins Plan-Log (vorher still verschluckt → bei „eingefrorenem" Overlay
            // war nicht erkennbar, dass/wo Render wirft). So ist der nächste Dump eindeutig.
            PlanLog($"GRID-BUILD EXCEPTION: {ex.GetType().Name}: {ex.Message} @ {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            sb.AppendLine(Loc.T("bubble.gridBuildFailed", pool.Count));
            return;
        }
        if (grid.Machines.Count == 0)
        {
            sb.AppendLine(Loc.T("bubble.noMachines", pool.Count, s.RegsResourceId));
            return;
        }

        // Hauptpool als Solver-Input (geparkte ZBs sind im GridState)
        var mainSimZbs = mainPool
            .Select(p => new SimZb(p.HeaderId, p.Hair, p.Eyes, p.Nose, p.Feet))
            .ToList();

        // IN-TRANSIT-ZBs: losgeschickt und laufen GERADE übers Brett — weder im Pool,
        // noch auf Insel/Falle, noch gescort. Sie sind im Engine-Read enthalten, fielen
        // aber durch ALLE Kategorien (Split gibt einen on-grid-ZB auf einer Nicht-Insel-
        // Zelle in keine Liste) → wurden NICHT gezählt: der Bestand zeigte 15 statt 16,
        // während ein ZB lief (User-belegt 2026-06-04). Jetzt explizit als „unterwegs"
        // zählen — UND der Plan-Stabilität melden (ein laufender ZB = Schritt in Arbeit).
        int trappedCount = grid.State.StickyTrappedByCell.Count;
        var classifiedHdrs = new HashSet<ushort>(mainPool.Select(p => p.HeaderId));
        classifiedHdrs.UnionWith(parked.Select(p => p.HeaderId));
        classifiedHdrs.UnionWith(grid.State.StickyTrappedByCell.Values.Select(z => z.HeaderId));
        int inTransit = pool.Count(p => !classifiedHdrs.Contains(p.HeaderId) && p.OutcomeType != 3);
        int totalToSolve = mainSimZbs.Count + parked.Count + trappedCount + inTransit;

        MaintainPlan(grid, mainSimZbs, parked, inTransit);

        sb.AppendLine(Loc.T("bubble.solver", _solver.Status));
        // SYNCHRONE Bestandszeile — immer korrekt, unabhängig vom async Solver-Status.
        // KOMPAKT (nur Zahlen, keine Attribut-Listen — die ZBs sieht man am Brett),
        // damit das Overlay nicht überläuft. Zeigt wo die ZBs gerade sind.
        if (parked.Count > 0 || trappedCount > 0 || inTransit > 0)
        {
            var teile = new List<string> { Loc.T("bubble.inv.pool", mainSimZbs.Count) };
            if (inTransit > 0) teile.Add(Loc.T("bubble.inv.transit", inTransit));
            if (parked.Count > 0) teile.Add(Loc.T("bubble.inv.island", parked.Count));
            if (trappedCount > 0) teile.Add(Loc.T("bubble.inv.trap", trappedCount));
            sb.AppendLine(Loc.T("bubble.inventory", totalToSolve, string.Join(" · ", teile)));
        }
        if (_solver.IsRunning)
        {
            // Live-Fortschritt. Primär die GROBE, stets sichtbare Achse:
            // „Suchzweig X/N (P %)" = wie viele der ersten Züge schon komplett
            // abgesucht sind. Diese steigt sichtbar, anders als der feine
            // Baum-Anteil (der bei tiefen Bäumen lange nahe 0 bleibt). Dazu die
            // tatsächlich besuchten Knoten und — sobald sinnvoll — die
            // hochgerechnete Gesamt-Knotenzahl.
            double secs = _solver.StartedAt is { } st
                ? (DateTime.UtcNow - st).TotalSeconds : 0;
            var prog = _solver.Progress;
            int best = prog?.BestSurvivors ?? _solver.RunningBestSurvivors ?? 0;
            long doneNodes = prog?.Nodes ?? 0;
            long totalNodes = prog?.EstimatedTotalNodes ?? 0;
            int rootDone = prog?.RootBranchesDone ?? 0;
            int rootTotal = prog?.RootBranchesTotal ?? 0;
            double rootPct = prog?.RootPercent ?? 0;

            string branchInfo = rootTotal > 0
                ? Loc.T("bubble.progress.branch", rootDone, rootTotal) : Loc.T("bubble.progress.starting");
            string bestInfo = best > 0 ? Loc.T("bubble.progress.best", best, totalToSolve) : "—";
            // Eine kompakte Zeile statt zwei (Overlay-Platz sparen).
            sb.AppendLine(Loc.T("bubble.progress", secs.ToString("F0"), branchInfo, doneNodes.ToString("N0"), bestInfo));
        }
        sb.AppendLine();

        // Empfehlung aus dem AKTIVEN (fixierten) Plan — bleibt stabil, solange der
        // reale Verlauf auf dem Plan liegt. Während neu gerechnet wird, ist er null.
        var plan = _activePlan;
        if (plan is null)
        {
            string grund = _lastWasDeviation
                ? Loc.T("bubble.deviation")
                : Loc.T("bubble.computing", BubblewonderSolverWorker.TimeBudget.TotalSeconds.ToString("F0"));
            sb.AppendLine(grund);
            return;
        }

        if (!plan.Strategy.Contains("optimal"))
            sb.AppendLine(Loc.T("bubble.notOptimal"));

        // Aktueller Schritt = wie viele Plan-Schritte schon erledigt sind. Sind ALLE
        // erledigt, ist nichts mehr zu tun (warten bis die letzten ZBs durch sind).
        int curStep = Math.Min(_planStep, plan.Assignments.Count);
        if (curStep >= plan.Assignments.Count)
        {
            sb.AppendLine(Loc.T("bubble.plan.line", plan.Survivors, totalToSolve, plan.Strategy));
            sb.AppendLine();
            // EHRLICH unterscheiden: ein VOLLSTÄNDIGER Plan (alle ZBs gelöst) → einfach
            // warten. Ein UNVOLLSTÄNDIGER Plan (Solver hat im Zeitlimit nur N<alle
            // gefunden, oder die letzten Schritte sind „angestoßen", aber es stecken
            // real noch ZBs fest) → NICHT „fertig" suggerieren (das war der „Unfug":
            // Plan 13/16, Meldung „alle durch", aber 16 ZBs auf dem Brett).
            bool incomplete = plan.Survivors < totalToSolve
                || plan.Strategy.Contains("time-limit") || plan.Strategy.Contains("Zeitlimit");
            if (incomplete)
            {
                sb.AppendLine(Loc.T("bubble.plan.incomplete.1", plan.Survivors, totalToSolve));
                sb.AppendLine(Loc.T("bubble.plan.incomplete.2"));
            }
            else
            {
                sb.AppendLine(Loc.T("bubble.plan.allStarted"));
            }
            return;
        }

        // Nenner = ALLE noch im Spiel befindlichen ZBs (Pool + Insel + Klebefalle),
        // konsistent mit der Bestandszeile + dem Worker-Status. (Vorher fehlten die
        // gefangenen → „Plan 13/14" neben „Solver 13/16".)
        var held = HeldZoombini.Find(mem);
        if (held is PoolMember h)
            AppendHeldZbFromPlan(sb, grid, pool, h, plan, totalToSolve, curStep);
        else
            AppendOverallPlanFromPlan(sb, grid, plan, totalToSolve, parked, curStep);
    }

    /// <summary>Steht der empfohlene ZB GERADE auf einer Insel-Zwischenstation
    /// (live aus <see cref="BubblewonderPoolClassifier"/>, nicht aus dem evtl.
    /// veralteten Plan)? Dann muss die Anweisung „VON DER INSEL" lauten statt
    /// „aus dem Pool hochheben" — sonst sucht der User den ZB im Pool, wo er
    /// nicht ist.</summary>
    private static bool IsRecommendedZbOnIsland(IReadOnlyList<PoolMember> parked, SimZb zb) =>
        parked.Any(p => p.HeaderId == zb.HeaderId
            || (p.Hair == zb.Hair && p.Eyes == zb.Eyes
                && p.Nose == zb.Nose && p.Feet == zb.Feet));

    /// <summary>Plan-Stabilität: hält den einmal berechneten Plan, solange der reale
    /// Spielverlauf AUF dem Plan-Pfad liegt — dann bleibt die Empfehlung stabil und
    /// es wird NICHT neu gerechnet. Nur bei echter Abweichung (anders platzierter ZB
    /// / Modell≠Realität) wird der Plan verworfen und neu gerechnet.</summary>
    /// <summary>Insel-Spawn-Zelle fürs aktuelle Layout (REGS,Variant) aus dem persistenten
    /// Store. Lernt der Tracker diese Runde eine neue (Re-Launch von einer Insel-Zelle), wird
    /// sie sofort gespeichert. null = noch nie beobachtet → Solver routet die Insel (noch)
    /// nicht (kein Raten → kein ZB-Tod). Nach dem ersten Re-Launch dauerhaft korrekt.</summary>
    private int? ResolveIslandSpawn(BubblewonderState s, BubblewonderTracker tracker)
    {
        try
        {
            if (_spawnStore is null)
            {
                _spawnStorePath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? ".",
                    "bubblewonder-spawns.txt");
                _spawnStore = BubblewonderSpawnStore.Load(_spawnStorePath);
            }
            int regs = s.RegsResourceId, variant = s.Variant;
            if (tracker.LearnedIslandSpawn is { } learned)
            {
                Direction? dir = tracker.ObservedSpawnDirections.TryGetValue(learned, out var d)
                    ? d : (Direction?)null;
                if (_spawnStore.Observe(regs, variant, learned, dir))
                {
                    _spawnStore.Save(_spawnStorePath!);
                    PlanLog($"Insel-Spawn GELERNT+gespeichert: REGS {regs} variant {variant} " +
                            $"→ Zelle {learned} ({learned / 13},{learned % 13})");
                }
            }
            var stored = _spawnStore.Get(regs, variant);
            return stored.Count > 0 ? stored[0].Pos : (int?)null;
        }
        catch { return null; }  // Persistenz ist best-effort, darf den Renderer nie crashen
    }

    private void MaintainPlan(BubblewonderGridModel grid, IReadOnlyList<SimZb> mainSimZbs,
        IReadOnlyList<PoolMember> parked, int inTransit)
    {
        int pool = mainSimZbs.Count;
        int insel = grid.State.ParkedZbsByMachineIdx.Sum(kv => kv.Value.Count);
        int falle = grid.State.StickyTrappedByCell.Count;
        string where = $"Pool={pool} Insel={insel} Falle={falle}";

        // 1. Fertig gerechneten Plan übernehmen (atomar mit seinem Ausgangszustand).
        if (!_solver.IsRunning && _solver.LatestResult is { } res
            && !ReferenceEquals(res, _activePlan)
            && _solver.ResultBaseGrid is { } bg && _solver.ResultBasePool is { } bp)
        {
            _activePlan = res;
            _planBaseGrid = bg;
            _planBasePool = bp.ToList();
            _planStep = 0;   // neuer Plan → Zeiger auf den ersten Schritt
            PlanLog($"{where} | PLAN ÜBERNOMMEN: {res.Survivors} rettbar, {res.Assignments.Count} Schritte ({res.Strategy})");

            // Vollständige Zug-Sequenz EINMAL pro Plan mitloggen. Macht scheinbar
            // „dumme" Einzelschritte nachvollziehbar: ein Zug über die INSEL-Maschine
            // ist ein Re-Launch (oft SETUP, der einen Switch flippt und so das Ziel
            // öffnet — NICHT eine Strandung des Insel-ZBs). Damit ist die Live-Frage
            // „warum schickt er den dahin?" aus dem Log allein beantwortbar.
            var sbPlan = new System.Text.StringBuilder("  PLAN-ZÜGE: ");
            for (int i = 0; i < res.Assignments.Count; i++)
            {
                var a = res.Assignments[i];
                var m = a.MachineIdx >= 0 && a.MachineIdx < bg.Machines.Count ? bg.Machines[a.MachineIdx] : null;
                string ml = m is null ? $"M{a.MachineIdx}"
                    : $"{(m.IsIsland ? "INSEL" : "M")}{a.MachineIdx}({m.StartCellIndex / 13},{m.StartCellIndex % 13})";
                sbPlan.Append($"{i}:0x{a.Zb.HeaderId:X2}→{ml}  ");
            }
            PlanLog(sbPlan.ToString());
        }

        // 2. Liegt der aktuelle Zustand noch auf dem aktiven Plan? ZUERST prüfen!
        //    Eine PLANMÄSSIGE Insel-Landung ist ein erledigter Plan-Schritt, KEINE
        //    Abweichung (User-Prinzip, mehrfach: „das Ankommen auf der Insel und der Weg
        //    dahin sind IM Plan, keine PlanÄNDERUNG"). LocateOnPlan re-simuliert den Plan;
        //    parkt der Schritt-ZB erwartungsgemäß auf der Insel, matcht der Post-Zustand
        //    → der Zeiger rückt vor, statt fälschlich „Abweichung" zu melden.
        int? step = null;
        if (_activePlan is not null && _planBaseGrid is not null && _planBasePool is not null)
            step = BubblewonderSolver.LocateOnPlan(
                _planBaseGrid, _planBasePool, _activePlan.Assignments, grid, mainSimZbs);

        // 3. Auf Plan → nichts tun (stabil). Der Zeiger rückt NUR vorwärts (ein erledigter
        //    Schritt zieht ihn weiter; ein Flicker-Snapshot wirft ihn nicht zurück).
        if (step is int k)
        {
            _lastWasDeviation = false;
            _pendingDeviationSig = null;
            _planStep = Math.Max(_planStep, k);
            PlanLog($"{where} | AUF PLAN @ Schritt {_planStep}/{_activePlan!.Assignments.Count} (stabil)");
            return;
        }
        if (_solver.IsRunning) { PlanLog($"{where} | rechnet noch…"); return; }

        // SCHRITT-IN-ARBEIT: Solange ein ZB UNTERWEGS ist (in Transit, läuft gerade übers
        // Grid), ist der Zustand MITTEN in einem Schritt. Der laufende ZB hat evtl. schon
        // einen Switch geflippt (Trigger durchlaufen), aber sein Outcome (Insel/Falle/Ziel)
        // ist noch NICHT da. Dieser Halb-Zustand (Switch geflippt + Outcome fehlt) matcht
        // weder den „vorher"- noch den „nachher"-Schnappschuss des Plans → er SÄHE wie eine
        // Abweichung aus, ist aber nur „Schritt läuft". KEINE Abweichung werten und NICHT
        // neu rechnen, bis der ZB zur Ruhe kommt — sonst rechnet der Solver auf einem
        // Halb-Zustand, und wenn der ZB dann landet, weicht er erneut ab (Kaskade, belegt
        // plan-log 15:28:08→15:28:18: erst Switch-Flip mid-walk, dann Fallen-Landung). Den
        // Debounce-Timer zurücksetzen, damit eine ECHTE Abweichung erst nach dem Settle
        // frisch entprellt wird (kein vorzeitiges Feuern in dem Moment, wo inTransit→0 fällt).
        if (inTransit > 0 && _activePlan is not null)
        {
            _pendingDeviationSig = null;
            PlanLog($"{where} | Schritt läuft ({inTransit} ZB unterwegs) — Plan gehalten");
            return;
        }

        // HINWEIS: Früher gab es hier eine SOFORTIGE Plan-Invalidierung „Plan-ZB auf Insel
        // gelandet" OHNE Debounce. Das war fatal: ein transienter Pool=0-Scan-Glitch
        // (alle Pool-ZBs fallen kurz aus dem Scan, nur Insel-ZBs bleiben) triggerte den
        // Sofort-Recompute → der Solver rechnete auf einem LEEREN Pool → degenerierter Plan
        // (Pool=0 als Basis) → danach wich jeder echte Scan (volle ZB-Zahl) permanent ab
        // → Dauer-„Abweichung" (belegt memdump-212934, Log 21:17:05: Pool=0→Plan veraltet→
        // ÜBERNOMMEN 0 Schritte→ABWEICHUNG, BASIS-Pool leer vs AKTUELL 13). Eine echte
        // Insel-Landung erkennt LocateOnPlan oben bereits als „auf Plan"; eine UNERWARTETE
        // Landung läuft jetzt durch denselben Debounce unten (filtert den Glitch).

        // Abweichung erkannt. ENTPRELLEN: nur neu rechnen, wenn dieselbe abweichende
        // Signatur länger als DeviationDebounce anhält (filtert transiente Scan-Glitches,
        // bei denen ZBs während der Wurf-Animation kurz aus dem Pool-Scan fallen).
        if (_activePlan is not null)
        {
            string curSig = BubblewonderSolver.DebugStateSignature(grid, mainSimZbs);
            if (curSig != _pendingDeviationSig)
            {
                // erste Sichtung dieser Abweichung → Timer starten, Plan VORERST behalten.
                _pendingDeviationSig = curSig;
                _pendingDeviationSince = DateTime.Now;
                PlanLog($"{where} | ⏳ Abweichung gesichtet — warte {DeviationDebounce.TotalMilliseconds:F0}ms (transient?)");
                return;
            }
            if (DateTime.Now - _pendingDeviationSince < DeviationDebounce)
            {
                PlanLog($"{where} | ⏳ Abweichung hält an…");
                return;   // noch nicht lange genug stabil → Plan behalten
            }
            // Bestätigt: stabil abweichend > Debounce → echte Abweichung.
            _lastWasDeviation = true;
            if (_planBaseGrid is not null && _planBasePool is not null)
            {
                string baseSig = BubblewonderSolver.DebugStateSignature(_planBaseGrid, _planBasePool);
                PlanLog($"{where} | ⚠ ABWEICHUNG (bestätigt) → neu rechnen");
                PlanLog($"      PLAN-BASIS-SIG: {baseSig}");
                PlanLog($"      AKTUELLE  SIG: {curSig}");
            }
        }
        else
        {
            _lastWasDeviation = false;
            PlanLog($"{where} | KEIN PLAN → rechnen");
        }
        _pendingDeviationSig = null;
        _activePlan = null;                            // alter Plan ungültig bis neuer fertig
        _solver.StartCompute(grid, mainSimZbs);
    }

    /// <summary>Schreibt eine Plan-Entscheidung ins Diagnose-Log (Datei neben der EXE).
    /// Nur bei inhaltlicher Änderung — kein Tick-Spam.</summary>
    private void PlanLog(string line)
    {
        if (line == _lastLogLine) return;   // unverändert → nicht doppelt loggen
        _lastLogLine = line;
        try
        {
            _planLogPath ??= System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? ".",
                "bubblewonder-plan.log");
            lock (_planLogLock)
                System.IO.File.AppendAllText(_planLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n");
        }
        catch { /* Logging ist best-effort */ }
    }

    /// <summary>Menschlich verständliche Lage-Beschreibung der Maschinen-Cell +
    /// Lauf-Richtung. Wenn <paramref name="zb"/> gegeben ist, wird die Richtung aus der
    /// TATSÄCHLICHEN Bewegung des simulierten ZB-Pfads abgeleitet (nicht aus dem rohen
    /// Maschinen-dirCode) — wichtig, weil viele Spawn-Zellen selbst Routing-Cells sind
    /// (Conditional/Deflector) und den ZB sofort umlenken; dann ist die rohe Maschinen-
    /// Richtung irreführend (verifiziert 2026-05-30: M0@(2,8) dirCode=Down, ZB lief real
    /// nach oben-links).</summary>
    private static string MachineLocation(BubblewonderGridModel grid, int machineIdx, SimZb? zb = null)
    {
        // Der Plan stammt aus dem Background-Solver und kann auf einem grid mit
        // anderer Maschinen-Anzahl gelaufen sein (Insel-Maschine erscheint/
        // verschwindet, async-Timing). Bounds-Check verhindert IndexOutOfRange.
        if (machineIdx < 0 || machineIdx >= grid.Machines.Count)
            return Loc.T("bubble.machine.stale", machineIdx);
        var mach = grid.Machines[machineIdx];
        // Bildschirm-Orientierung: das Feld wird perspektivisch TRANSPONIERT
        // dargestellt. Live kalibriert über fünf Anker (2026-05-26 + 2026-05-30 Pixel):
        //   intern row+ = Bildschirm rechts, col+ = unten →
        //   Up=links, Down=rechts, Left=oben, Right=unten.
        int row = mach.StartCellIndex / 13, col = mach.StartCellIndex % 13;
        string horz = Loc.T(row <= 3 ? "bubble.dir.left" : row <= 7 ? "bubble.dir.mid" : "bubble.dir.right");
        string vert = Loc.T(col <= 4 ? "bubble.dir.top"  : col <= 8 ? "bubble.dir.mid" : "bubble.dir.bottom");
        Direction effDir = EffectivePathDirection(grid, zb, machineIdx) ?? mach.StartDirection;
        string wurf = effDir switch
        {
            Direction.Up    => Loc.T("bubble.dir.left"),
            Direction.Down  => Loc.T("bubble.dir.right"),
            Direction.Left  => Loc.T("bubble.dir.top"),
            Direction.Right => Loc.T("bubble.dir.bottom"),
            _               => "?",
        };
        string kind = Loc.T(mach.IsIsland ? "bubble.machine.island" : "bubble.machine.bubble");
        return Loc.T("bubble.machine.location", kind, vert, horz, wurf);
    }

    /// <summary>Effektive Lauf-Richtung des ZB ab dieser Maschine = dominante Grid-Achsen-
    /// Bewegung (Δrow vs Δcol) seines simulierten Pfads, in Bildschirm-Richtung übersetzt.
    /// Deckt Spawn-Zellen, die als Routing-Cell sofort umlenken. <c>null</c> wenn kein ZB
    /// oder kein Pfad.</summary>
    private static Direction? EffectivePathDirection(BubblewonderGridModel grid, SimZb? zb, int machineIdx)
    {
        if (zb is not { } z || machineIdx < 0 || machineIdx >= grid.Machines.Count) return null;
        var run = BubblewonderRunner.RunSingle(grid, z, machineIdx);
        var path = run.Outcomes.FirstOrDefault(o => o.Zb.HeaderId == z.HeaderId)?.Path;
        if (path is null || path.Count < 2) return null;
        // ERSTER ZUG (path[0]→path[1]) = die WURF-Richtung, die der Spieler an der
        // Maschine SIEHT und an der er sie auswählt — nach evtl. Umlenkung durch die
        // Spawn-Zelle, also die echte sichtbare Auswurf-Richtung. NICHT Anfang→Ende:
        // das ist die NETTO-Verschiebung übers ganze Brett und kann der Wurf-Richtung
        // ENTGEGENGESETZT sein. Beleg (memdump-080136, User-verifiziert): die empfohlene
        // Maschine M0(1,8) WIRFT intern Left (= Bildschirm OBEN, sichere Maschine, scort),
        // der ZB läuft danach aber lang runter zum Ziel (10,1) → Netto Down (= Bildschirm
        // RECHTS) → das alte Label sagte „wirft nach rechts" → der Spieler nahm die
        // RECHTS-werfende (Todes-)Maschine. Korrekt ist „nach oben" (erster Zug).
        int dr = path[1] / 13 - path[0] / 13;
        int dc = path[1] % 13 - path[0] % 13;
        if (dr == 0 && dc == 0) return null;
        return Math.Abs(dc) >= Math.Abs(dr)
            ? (dc < 0 ? Direction.Left : Direction.Right)
            : (dr < 0 ? Direction.Up : Direction.Down);
    }

    private static void AppendHeldZbFromPlan(
        StringBuilder sb, BubblewonderGridModel grid,
        IReadOnlyList<PoolMember> pool, PoolMember held,
        SolverResult plan, int total, int step)
    {
        var heldZb = new SimZb(held.HeaderId, held.Hair, held.Eyes, held.Nose, held.Feet);
        sb.AppendLine(Loc.T("bubble.held.inHand", heldZb.Hair, heldZb.Eyes, heldZb.Nose, heldZb.Feet));
        sb.AppendLine();

        if (step >= plan.Assignments.Count)
        {
            sb.AppendLine(Loc.T("bubble.held.noStep"));
            return;
        }

        var firstAssignment = plan.Assignments[step];
        // Match über HeaderId ODER alle 4 Attribute. Wegen Equivalence-Klassen
        // im Solver kann der Plan einen ZB mit gleichen Attributen aber anderer
        // HeaderId vorschlagen — der gehobene wäre dann genauso gültig.
        bool heldIsFirst = firstAssignment.Zb.HeaderId == heldZb.HeaderId
                        || (firstAssignment.Zb.Hair == heldZb.Hair
                            && firstAssignment.Zb.Eyes == heldZb.Eyes
                            && firstAssignment.Zb.Nose == heldZb.Nose
                            && firstAssignment.Zb.Feet == heldZb.Feet);
        if (heldIsFirst)
        {
            sb.AppendLine(Loc.T("bubble.held.yes", MachineLocation(grid, firstAssignment.MachineIdx, firstAssignment.Zb)));
            sb.AppendLine(Loc.T("bubble.held.saves", plan.Survivors, total));
        }
        else
        {
            sb.AppendLine(Loc.T("bubble.held.no"));
            sb.AppendLine(Loc.T("bubble.held.sendFirst",
                firstAssignment.Zb.Hair, firstAssignment.Zb.Eyes,
                firstAssignment.Zb.Nose, firstAssignment.Zb.Feet,
                MachineLocation(grid, firstAssignment.MachineIdx, firstAssignment.Zb)));
            int heldIdx = -1;
            for (int i = step; i < plan.Assignments.Count; i++)
            {
                var pZb = plan.Assignments[i].Zb;
                if (pZb.HeaderId == heldZb.HeaderId
                    || (pZb.Hair == heldZb.Hair && pZb.Eyes == heldZb.Eyes
                        && pZb.Nose == heldZb.Nose && pZb.Feet == heldZb.Feet))
                { heldIdx = i; break; }
            }
            if (heldIdx >= 0)
            {
                var heldStep = plan.Assignments[heldIdx];
                sb.AppendLine(Loc.T("bubble.held.thisStep", heldIdx + 1, plan.Assignments.Count,
                              MachineLocation(grid, heldStep.MachineIdx, heldStep.Zb)));
            }
            else
            {
                sb.AppendLine(Loc.T("bubble.held.notInSeq"));
            }
            sb.AppendLine(Loc.T("bubble.held.savesTotal", plan.Survivors, total));
        }
    }

    private static void AppendOverallPlanFromPlan(
        StringBuilder sb, BubblewonderGridModel grid, SolverResult plan, int total,
        IReadOnlyList<PoolMember> parked, int step)
    {
        sb.AppendLine(Loc.T("bubble.overall.line", plan.Survivors, total, plan.Strategy, step + 1, plan.Assignments.Count));
        sb.AppendLine();
        if (step < plan.Assignments.Count)
        {
            var next = plan.Assignments[step];
            string attrs = $"({next.Zb.Hair},{next.Zb.Eyes},{next.Zb.Nose},{next.Zb.Feet})";
            if (IsRecommendedZbOnIsland(parked, next.Zb))
                sb.AppendLine(Loc.T("bubble.overall.fromIsland", attrs));
            else
                sb.AppendLine(Loc.T("bubble.overall.pickUp", attrs));
            sb.AppendLine(Loc.T("bubble.overall.sendVia", MachineLocation(grid, next.MachineIdx, next.Zb)));
        }
    }


    /// <summary>Schreibt einen ZB-Pfad mit Cell-Type pro Schritt:
    /// "Pos 8 (0,8) Routing → Pos 7 (0,7) Filter[Augen=1] → ..."</summary>
    private static void AppendAnnotatedPath(StringBuilder sb, BubblewonderState s,
                                             Dictionary<(int, int), BubbleObject> bubbleByPos,
                                             ushort handle, IReadOnlyList<int> path)
    {
        if (path.Count == 0)
        {
            sb.AppendLine($"  0x{handle:X4}: (kein Pfad)");
            return;
        }

        for (int i = 0; i < path.Count; i++)
        {
            int posIdx = path[i];
            int row = posIdx / 13, col = posIdx % 13;
            var mech = FindMechanism(s, row, col);
            BubbleObject? liveBubble = null;
            if (bubbleByPos.TryGetValue((row, col), out var b)) liveBubble = b;
            string typeTag = ClassifyCell(mech, liveBubble);
            string prefix = i == 0 ? "  " : "    ";
            string arrow = i == 0 ? "" : "→ ";
            sb.AppendLine($"{prefix}{arrow}Pos {posIdx,3} ({row,2},{col,2})  {typeTag}");
        }
    }

    private static Mechanism? FindMechanism(BubblewonderState s, int row, int col)
    {
        foreach (var m in s.Grid.Mechanisms)
            if (m.Position.Prop1 == row && m.Position.Prop2 == col)
                return m;
        return null;
    }

    /// <summary>Kompakter Type-Tag pro Cell für die Pfad-Annotation.
    /// Klassifikation aus REGS-Bytes (live-verifiziert 2026-05-01):
    ///   - F0=1 → Trap (gelbes Killer-Feld)
    ///   - F0=4 → Switch
    ///   - F0=2/3 + F4..F7 one-hot → StaticDeflector mit Direction (Pfeil ↑↓←→)
    ///   - F0=2/3 + F9=1 → Conditional-Filter
    /// Cells ohne REGS-Eintrag = leere/passive Bubbles.</summary>
    private static string ClassifyCell(Mechanism? mech, BubbleObject? liveBubble = null)
    {
        if (mech is null) return "leer";
        return mech.Type switch
        {
            MechanismType.Trap => "⚠ TRAP",
            MechanismType.SwitchActivated => DescribeSwitch(mech),
            MechanismType.Sticky => $"⭐ Sticky (Color {mech.RawFields[3]})",
            MechanismType.Trigger => $"🔵 Trigger (Slot {mech.RawFields[3]})",
            MechanismType.Toggle => "🔄 Toggle",
            MechanismType.Conditional => DescribeConditional(mech, liveBubble),
            MechanismType.StaticDeflector => mech.Direction is { } d
                ? $"Pfeil {d.AsArrow()}"
                : "Pfeil ?",
            MechanismType.Passthrough => "Passthrough",
            _ => $"? raw=[{string.Join(",", mech.RawFields)}]",
        };
    }

    private static string DescribeConditional(Mechanism mech, BubbleObject? liveBubble)
    {
        string dir = mech.Direction is { } d ? $" {d.AsArrow()}" : "";
        // Live Attribut + Variante kommen aus der Bubble-Engine-Object bei
        // +0x82 (Attribut: 1=Hair, 2=Eyes, 3=Nose, 4=Feet) und +0x84 (Variant 1..5).
        // Verifiziert via 4-Cell-Diff am 2026-05-01 (Sonnenbrille=Eyes/2, Nose/3 etc.).
        if (liveBubble is null)
            return $"Conditional{dir} (kein Live-Bubble)";
        byte attr = (byte)liveBubble.ConditionalAttrCode;
        int variant = liveBubble.ConditionalVariant;
        if (attr == 0)
            return $"Conditional{dir} (noch nicht initialisiert)";
        string attrName = ZoombiniVariants.AttributeName(attr);
        string variantName = ZoombiniVariants.VariantName(attr, variant);
        return $"{variantName}{dir}  ({attrName} V{variant})";
    }

    /// <summary>Switch-Cell: zeigt beide möglichen Schalt-Stellungen (aus REGS).
    /// Welche aktuell aktiv ist, müsste aus Switch-Bitmap kommen — TODO.</summary>
    private static string DescribeSwitch(Mechanism mech)
    {
        // Switch hat oft 2 Direction-Bits gesetzt = zwei Stellungen
        var rec = new RegsRecord(
            mech.RawFields[0], mech.RawFields[1], mech.RawFields[2], mech.RawFields[3],
            mech.RawFields[4], mech.RawFields[5], mech.RawFields[6], mech.RawFields[7],
            mech.RawFields[8], mech.RawFields[9]);
        var dirs = rec.AllDirections;
        if (dirs.Count == 0) return "🔘 Switch";
        if (dirs.Count == 1) return $"🔘 Switch ({dirs[0].AsArrow()})";
        // Zwei Stellungen — zeige beide
        return $"🔘 Switch ({string.Join("↔", dirs.Select(d => d.AsArrow()))})";
    }
}
