namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Kompakter Live-Snapshot der Bubblewonder-Schalter/Counter zur Event-Erkennung.
/// Wird vom <see cref="BubblewonderTracker"/> pro Tick aufgenommen — viel
/// schlanker als <see cref="BubblewonderState"/>.
/// </summary>
public readonly record struct BubblewonderTickSnapshot(
    DateTime At,
    ushort CurrentZbInTransit,
    ushort CurrentZbScored,
    IReadOnlyList<ushort> PositionCounters,   // 156 entries (12 rows × 13 cols)
    IReadOnlyList<ushort> PositionHandles,     // 156 entries
    IReadOnlyList<ushort> ActionSlotState,    // 24 entries
    ushort PoppedCount,
    ushort ScoredCount);

/// <summary>
/// Diff-Event zwischen zwei aufeinanderfolgenden Tick-Snapshots — was
/// hat sich auf dem Grid geändert?
/// </summary>
public sealed record BubblewonderEvent(
    DateTime At,
    BubblewonderEventKind Kind,
    string Description);

/// <summary>Filter-Bedingung pro Edge — die Attribute die ALLE bisher beobachteten
/// ZBs gemeinsam hatten, abgeleitet aus der Live-Beobachtung.
/// Wert 0 = Attribut ist NICHT konstant über alle Beobachtungen (= irrelevant für Filter).
/// Wert 1..5 = alle ZBs hatten exakt diesen Variant.</summary>
public readonly record struct EdgeFilterHint(byte Hair, byte Eyes, byte Nose, byte Feet, int ObservedZbCount)
{
    public bool IsEmpty => Hair == 0 && Eyes == 0 && Nose == 0 && Feet == 0;

    /// <summary>Prüft ob ein ZB diese Filter-Bedingung erfüllt
    /// (= ALLE bekannten Attribute matchen).</summary>
    public bool Accepts(byte h, byte e, byte n, byte f)
    {
        if (Hair > 0 && Hair != h) return false;
        if (Eyes > 0 && Eyes != e) return false;
        if (Nose > 0 && Nose != n) return false;
        if (Feet > 0 && Feet != f) return false;
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Hair > 0) parts.Add($"H={Hair}");
        if (Eyes > 0) parts.Add($"E={Eyes}");
        if (Nose > 0) parts.Add($"N={Nose}");
        if (Feet > 0) parts.Add($"F={Feet}");
        return parts.Count == 0 ? "*" : string.Join(",", parts);
    }
}

public enum BubblewonderEventKind : byte
{
    /// <summary>Neuer ZB gepoppt (= aus Pool ins Grid gekommen).</summary>
    ZbPopped,
    /// <summary>ZB hat eine Position-aktivierung im Grid getriggert.</summary>
    PositionActivated,
    /// <summary>ZB hat eine Position-deaktivierung im Grid getriggert.</summary>
    PositionDeactivated,
    /// <summary>ActionSlot-State hat sich geändert (Schalter umgelegt).</summary>
    SlotStateChanged,
    /// <summary>ZB wurde gescored (= durchgekommen).</summary>
    ZbScored,
    /// <summary>Bytes einer f0=4-Switch- oder f0=6-Trigger-Cell haben sich
    /// geändert. Wird zum Aufspüren des Trigger→Target-Mappings genutzt:
    /// nach einem Trigger-Hit erscheinen die korrelierten Switch-Byte-Änderungen.</summary>
    SwitchByteChanged,
    /// <summary>Ein ZB hat eine f0=6-Trigger-Cell betreten — Korrelations-Marker
    /// für den anschließenden Switch-State-Change am Target.</summary>
    TriggerHit,
    /// <summary>HAWK: vorhergesagter Sim-Pfad weicht vom beobachteten Live-Pfad ab.
    /// Markiert einen potentiellen Fehler im Simulator — sofort untersuchen.</summary>
    HawkMismatch,
    /// <summary>HAWK: Simulator hat erfolgreich einen Pfad vorhergesagt der
    /// vollständig dem Live-Pfad entsprochen hat (= Vertrauen ins Modell).</summary>
    HawkPredictionVerified,
}

/// <summary>
/// Tickt im Helper-OnTick und behält ringbuffer der letzten ~20 Sekunden
/// Bubblewonder-State. Erkennt Events durch Diff zwischen Snapshots.
///
/// <para>Lösung für das "F12 ist zu langsam"-Problem: ZBs laufen in 500ms-1s
/// durch das Grid, F12 zeigt nur einen Moment. Mit dem Tracker schreiben wir
/// stattdessen die EVENT-TIMELINE — alle Aktivierungen, Schalter-Wechsel,
/// Scoring — in den Dump.</para>
/// </summary>
public sealed class BubblewonderTracker
{
    private const int MaxSnapshots = 100;   // ~20s bei 200ms Tick
    private const int MaxEvents = 500;

    private readonly LinkedList<BubblewonderTickSnapshot> _snapshots = new();
    private readonly LinkedList<BubblewonderEvent> _events = new();

    /// <summary>Per ZB-Handle: Sequenz der Positionen die er aktiviert hat
    /// (= sein Pfad durch das Grid). Reset wenn ZB scored wird.</summary>
    private readonly Dictionary<ushort, List<int>> _pathByZb = new();

    /// <summary>Beobachtete Edges (positionA → positionB) mit Counter
    /// wie oft die Edge live observiert wurde.</summary>
    private readonly Dictionary<(int, int), int> _observedEdges = new();

    /// <summary>Per Edge die Liste der ZB-Attribut-Tupel die diese Edge nahmen.</summary>
    private readonly Dictionary<(int, int), List<(byte H, byte E, byte N, byte F)>> _edgeAttrs = new();

    /// <summary>Cache: hdr1A → ZB-Attribute. Gefüllt wenn ZB gepoppt wird.</summary>
    private readonly Dictionary<ushort, (byte H, byte E, byte N, byte F)> _zbAttrs = new();

    /// <summary>Pro abgeschlossenem ZB-Pfad: hdr1A, attrs, Position-Sequence.</summary>
    private readonly List<(ushort Hdr1A, (byte H, byte E, byte N, byte F) Attrs, IReadOnlyList<int> Path)> _completedPaths = new();

    /// <summary>Cache des letzten Round-Identifiers (Difficulty, RegsResourceId).
    /// Bei Wechsel: automatischer Reset weil das Layout sich komplett ändert.</summary>
    private (int diff, int regsId)? _lastRoundKey;

    /// <summary>Pro Runde gesehene Spawn-Positionen (= erste Position jedes ZB-Pfades).
    /// Reset bei Round-Wechsel.</summary>
    private readonly Dictionary<int, int> _spawnPositionCount = new();

    /// <summary>Pro Spawn-Cell: Liste der gestarteten ZBs + Pool-Y für Cluster-Zuordnung.</summary>
    private readonly Dictionary<int, List<(ushort handle, int poolY)>> _spawnDetails = new();

    /// <summary>Pro Spawn-Cell: live beobachtete Spawn-Direction
    /// (= aus dem ersten Pfad-Schritt nach Spawn). Direkt aus den ZB-Pfaden
    /// abgeleitet, zuverlässiger als die Engine-Object-dirCode-Heuristik.</summary>
    private readonly Dictionary<int, Simulator.Direction> _spawnDirections = new();

    /// <summary>Pro ZB: aktuell letzte beobachtete Position + Outcome. Wird
    /// kontinuierlich geupdated solange der ZB Pfad-Activations zeigt.
    /// Beim Pfad-Reset (nächster ZbPop) bleibt die letzte überschriebene Pos
    /// als finaler Endpoint stehen — das ist die Cell vor dem "Blase poppt".</summary>
    private readonly Dictionary<ushort, (byte H, byte E, byte N, byte F, int LastPos, string Outcome)> _outcomeEndpoints = new();

    /// <summary>ZB-IDs für die ein ZbScored-Event gefeuert wurde. Beim Endpoint-
    /// Logging entscheidet das ob "Scored" oder "NotScored" (= Sticky/Insel/Loop).</summary>
    private readonly HashSet<ushort> _scoredZbs = new();

    /// <summary>ZB-IDs die NACHWEISLICH auf der Schluss-Steininsel angekommen
    /// sind (echtes Score). Marker: Engine-Objekt <c>ZB[+0x76] == 3</c>
    /// (= Jump-Table-Fall 3 in PROCESS-fn 0x425a00, mit Steininsel-/Win-Check).
    /// Live verifiziert 2026-05-26: durchgelaufener ZB hatte +0x76==3, während
    /// Sticky-gefangene (+0x76∈{0,1}) und Insel-geparkte ZBs es NICHT hatten.
    /// Wird pro Tick aktualisiert; einmal gesetzt bleibt es (Fall 3 ist final),
    /// damit ein kurzes +0x76-Fenster vor dem Verschwinden nicht verloren geht.
    /// NUR ZBs hier drin tragen eine echte Goal-Cell bei — das verhindert die
    /// Verschmutzung durch geparkte/gefangene ZBs.</summary>
    private readonly HashSet<ushort> _steinInselScoredZbs = new();

    /// <summary>Outcome-Code 3 in ZB[+0x76] = "auf Steininsel angekommen".</summary>
    private const ushort SteinInselOutcomeCode = 3;

    /// <summary>Alle ZBs die in dieser Runde ein ZbScored-Event hatten.
    /// Vom Renderer genutzt um sie aus dem Pool-Cluster zu filtern (sonst
    /// werden sie fälschlich als "Insel-geparkt" erkannt).</summary>
    public IReadOnlyCollection<ushort> ScoredZbs => _scoredZbs;

    /// <summary>Live-gelernte Goal-Cells: Positionen wo ein ZB die Schluss-
    /// Steininsel erreicht hat. NUR ZBs mit nachgewiesenem <c>+0x76==3</c>
    /// (<see cref="_steinInselScoredZbs"/>) zählen — damit landen Sticky-
    /// gefangene und Insel-geparkte ZBs NICHT mehr fälschlich in der
    /// Goal-Cell-Liste (das war die Quelle der Verschmutzung).</summary>
    public IReadOnlyCollection<int> LearnedGoalCells =>
        _outcomeEndpoints.Where(kv => _steinInselScoredZbs.Contains(kv.Key))
            .Select(kv => kv.Value.LastPos).Distinct().ToList();

    /// <summary>Hawk-Modus: pro ZB der vorhergesagte Pfad. Wird beim Spawn
    /// einmalig durch den Simulator berechnet und beim Live-Walk Schritt für
    /// Schritt gegen den tatsächlichen Pfad gediffed.</summary>
    private readonly Dictionary<ushort, IReadOnlyList<int>> _predictedPaths = new();

    /// <summary>Hawk-Modus: pro ZB die Anzahl beobachteter Mismatches
    /// (Live-Pfad-Schritt weicht von Sim-Pfad ab).</summary>
    private readonly Dictionary<ushort, int> _hawkMismatchCount = new();

    /// <summary>Hawk-Modus: ZBs die ihren Pfad bereits vollständig korrekt
    /// abgelaufen sind (= Sim-Vorhersage bestätigt).</summary>
    private readonly HashSet<ushort> _hawkVerifiedZbs = new();

    public IEnumerable<BubblewonderTickSnapshot> Snapshots => _snapshots;
    public IEnumerable<BubblewonderEvent> Events => _events;
    public IReadOnlyDictionary<(int, int), int> ObservedEdges => _observedEdges;
    public IReadOnlyDictionary<(int, int), List<(byte H, byte E, byte N, byte F)>> EdgeAttributes => _edgeAttrs;
    public IReadOnlyList<(ushort Hdr1A, (byte H, byte E, byte N, byte F) Attrs, IReadOnlyList<int> Path)> CompletedPaths => _completedPaths;
    public IReadOnlyDictionary<ushort, List<int>> ActivePaths => _pathByZb;
    public IReadOnlyDictionary<ushort, (byte H, byte E, byte N, byte F)> KnownZbAttrs => _zbAttrs;
    /// <summary>Pro Runde live beobachtete Spawn-Cell-Positionen (= echte Maschinen-Cells).
    /// Vom Tracker beim Spawn-Detect erfasst. Wird vom Builder genutzt um die
    /// Maschinen-Liste zu bauen (zuverlässiger als hardcoded SpawnMappings).</summary>
    /// <summary>Zählt hoch bei jedem Runden-/Layout-Wechsel (Reset). Der Renderer
    /// erkennt daran einen Rundenwechsel und verwirft seinen alten Plan SAUBER
    /// (statt ihn gegen das neue Layout zu vergleichen → falsche „Abweichung").</summary>
    public int RoundEpoch { get; private set; }

    /// <summary>Grund des letzten <see cref="Reset"/> (= RoundEpoch-Inkrement) — für
    /// die Diagnose der „Neue Runde mitten im Spiel"-Frage. Unterscheidet einen echten
    /// Layout-Wechsel (diff/heapPtr) von einem transienten „Puzzle inaktiv gelesen"-
    /// Reset (Memory-Scan-Glitch). null = noch kein Reset.</summary>
    public string? LastResetReason { get; private set; }

    /// <summary>Insel-Re-Launch-Spawn-Zelle, die DIESE Runde live beobachtet wurde
    /// (ein auf einer Insel-Zwischenstation geparkter ZB wurde neu losgeschickt → seine
    /// erste Pfad-Zelle). null bis beobachtet. Hochsicheres Signal (kommt nachweislich
    /// von einer Insel-Zelle) → der Renderer persistiert es pro (REGS,Variant).</summary>
    public int? LearnedIslandSpawn { get; private set; }

    public IEnumerable<int> ObservedSpawnPositions => _spawnPositionCount.Keys;
    /// <summary>Pro Spawn-Cell die live beobachtete Direction (aus erstem Pfad-Schritt).</summary>
    public IReadOnlyDictionary<int, Simulator.Direction> ObservedSpawnDirections => _spawnDirections;

    /// <summary>Gibt für eine Edge die Filter-Bedingung aus (= Attribute die ALLE durchgelaufenen ZBs gemeinsam haben).
    /// Returns null wenn keine Beobachtungen oder keine eindeutige Bedingung.</summary>
    public EdgeFilterHint? FilterFor((int from, int to) edge)
    {
        if (!_edgeAttrs.TryGetValue(edge, out var attrs) || attrs.Count == 0)
            return null;
        var hairs = attrs.Select(a => a.H).Distinct().ToList();
        var eyes = attrs.Select(a => a.E).Distinct().ToList();
        var noses = attrs.Select(a => a.N).Distinct().ToList();
        var feet = attrs.Select(a => a.F).Distinct().ToList();
        return new EdgeFilterHint(
            Hair: hairs.Count == 1 ? hairs[0] : (byte)0,
            Eyes: eyes.Count == 1 ? eyes[0] : (byte)0,
            Nose: noses.Count == 1 ? noses[0] : (byte)0,
            Feet: feet.Count == 1 ? feet[0] : (byte)0,
            ObservedZbCount: attrs.Count);
    }

    /// <summary>Wird vom Helper im OnTick aufgerufen. Liest kompakt den
    /// State und detektiert Events gegen den letzten Snapshot.
    /// <paramref name="pool"/> wird genutzt um ZB-Attribute zu cachen wenn
    /// ein neuer ZB ins Grid kommt.</summary>
    public void Tick(IMemoryReader mem, IReadOnlyList<PoolMember>? pool = null)
    {
        // Round-Wechsel-Detection: wenn Difficulty oder Heap-Pointer
        // sich ändert, ist es eine neue Runde — alle alten Pfade/Edges
        // sind ungültig, weil die Mechanismus-Layouts neu sind.
        int diff = mem.ReadWord(BubblewonderMemoryMap.UserDifficulty);
        int heapPtr = 0;
        var heapPtrBytes = mem.ReadBytes(BubblewonderMemoryMap.RegsHeapPointer, 4);
        if (heapPtrBytes is { Length: >= 4 })
            heapPtr = BitConverter.ToInt32(heapPtrBytes, 0);
        // Nur tracken wenn Puzzle aktiv ist (diff > 0 und heap initialisiert).
        if (diff <= 0 || diff > 5 || heapPtr == 0)
        {
            if (_lastRoundKey is not null)
            {
                LastResetReason = $"Puzzle inaktiv gelesen (diff={diff}, heapPtr=0x{heapPtr:X})";
                Reset();
            }
            _lastRoundKey = null;
            return;
        }
        var key = (diff, heapPtr);
        if (_lastRoundKey is not null && _lastRoundKey != key)
        {
            // neue Runde — Difficulty oder Heap-Pointer hat sich geändert
            LastResetReason = $"Layout-Wechsel {_lastRoundKey}→{key}";
            Reset();
        }
        _lastRoundKey = key;

        var snap = ReadCompactSnapshot(mem);
        var prev = _snapshots.Last?.Value;

        // (Kein Counter-basierter Reset mehr — die Engine setzt Pop/Scored-Counter
        // zwischen Würfen auf 0, das hat das Tracking innerhalb einer Runde
        // zerstört. Reset jetzt nur bei Heap-/Difficulty-Wechsel.)

        if (prev is BubblewonderTickSnapshot p)
            DetectEvents(p, snap, pool, mem);

        _snapshots.AddLast(snap);
        while (_snapshots.Count > MaxSnapshots) _snapshots.RemoveFirst();
    }


    /// <summary>Liest die y-Pool-Koordinate eines ZBs aus der Engine-Liste.
    /// Wird für Pool-Cluster-Zuordnung beim Spawn-Tracking benötigt.</summary>
    private static int ReadZbPoolY(IMemoryReader mem, ushort targetHdr1A)
    {
        foreach (var node in EngineObjectList.Walk(mem, EngineObjectList.HeaderSize + 0xC4))
        {
            if (node.Bytes.Length < EngineObjectList.HeaderSize + 0x20) continue;
            ushort hdr = BitConverter.ToUInt16(node.Bytes, 0x1A);
            if (hdr != targetHdr1A) continue;
            // y bei Engine-Header-Offset ähnlich wie PoolScanner
            return BitConverter.ToInt16(node.Bytes, EngineObjectList.HeaderSize + 0x10);
        }
        return 0;
    }

    /// <summary>Walke linked-list und finde ZB mit gegebenem hdr1A (auch wenn
    /// handle != 0x00000001 weil ZB im transit ist). Cache attrs wenn gefunden.</summary>
    private void TryReadZbAttrsFromList(IMemoryReader mem, ushort targetHdr1A)
    {
        foreach (var node in EngineObjectList.Walk(mem, EngineObjectList.HeaderSize + 0xC4))
        {
            if (node.Bytes.Length < EngineObjectList.HeaderSize + 0xC4) continue;
            ushort hdr1A = BitConverter.ToUInt16(node.Bytes, 0x1A);
            if (hdr1A != targetHdr1A) continue;
            byte h = node.Bytes[EngineObjectList.HeaderSize + 0xC0];
            byte e = node.Bytes[EngineObjectList.HeaderSize + 0xC1];
            byte n = node.Bytes[EngineObjectList.HeaderSize + 0xC2];
            byte f = node.Bytes[EngineObjectList.HeaderSize + 0xC3];
            if (h is < 1 or > 5 || e is < 1 or > 5 || n is < 1 or > 5 || f is < 1 or > 5) return;
            _zbAttrs[targetHdr1A] = (h, e, n, f);
            return;
        }
    }

    private static BubblewonderTickSnapshot ReadCompactSnapshot(IMemoryReader mem)
    {
        // 12 rows × 13 cols = 156 max position-index (Mechanismen mit pos=(11,*) existieren in REGS)
        const int MaxGridPositions = 156;
        var counters = new ushort[MaxGridPositions];
        var handles = new ushort[MaxGridPositions];
        var posBytes = mem.ReadBytes(BubblewonderMemoryMap.PositionCounterTable, MaxGridPositions * 6);
        if (posBytes != null)
        {
            for (int i = 0; i < MaxGridPositions; i++)
            {
                counters[i] = BitConverter.ToUInt16(posBytes, i * 6);
                handles[i] = BitConverter.ToUInt16(posBytes, i * 6 + 2);
            }
        }

        var slots = new ushort[24];
        var slotBytes = mem.ReadBytes(BubblewonderMemoryMap.ActionSlotHandlesPrimary, 48);
        if (slotBytes != null)
            for (int i = 0; i < 24; i++)
                slots[i] = BitConverter.ToUInt16(slotBytes, i * 2);

        return new BubblewonderTickSnapshot(
            At: DateTime.Now,
            CurrentZbInTransit: mem.ReadWord(BubblewonderMemoryMap.PoppedHandlesList),
            CurrentZbScored: mem.ReadWord(BubblewonderMemoryMap.ScoredHandlesList),
            PositionCounters: counters,
            PositionHandles: handles,
            ActionSlotState: slots,
            PoppedCount: mem.ReadWord(BubblewonderMemoryMap.PoppedHandlesCount),
            ScoredCount: mem.ReadWord(BubblewonderMemoryMap.ScoredHandlesCount));
    }

    /// <summary>Walkt die Engine-Objektliste und markiert jeden ZB dessen
    /// Outcome-Feld <c>+0x76 == 3</c> ist als echt-durch (Schluss-Steininsel).
    /// Nur ZB-artige Knoten (siehe <see cref="ZoombiniHandle.All"/>) — Bubbles
    /// (0x04188000) und Maschinen (0x04108000) haben an +0x76 keinen Outcome.</summary>
    private void ScanSteinInselScored(IMemoryReader mem)
    {
        foreach (var node in EngineObjectList.Walk(mem, 0x80))
        {
            if (node.Bytes.Length < 0x78) continue;
            uint handle = node.Handle;
            if (Array.IndexOf(ZoombiniHandle.All, handle) < 0) continue;
            ushort hdr1A = BitConverter.ToUInt16(node.Bytes, 0x1A);
            if (hdr1A == 0) continue;
            ushort outcome76 = BitConverter.ToUInt16(node.Bytes, 0x76);
            if (outcome76 == SteinInselOutcomeCode)
                _steinInselScoredZbs.Add(hdr1A);
        }
    }

    private void DetectEvents(BubblewonderTickSnapshot prev, BubblewonderTickSnapshot now, IReadOnlyList<PoolMember>? pool, IMemoryReader mem)
    {
        // Schluss-Steininsel-Marker pro Tick erfassen: jeder ZB dessen
        // Engine-Objekt +0x76 == 3 hat, ist echt durch (Fall 3 der Outcome-
        // Jump-Table). Einmal gesetzt bleibt es — Fall 3 ist final, und das
        // +0x76==3-Fenster kann sich vor dem Verschwinden des ZB schließen.
        ScanSteinInselScored(mem);

        // Pop-Transit-Wechsel: ANY Übergang (auch nach 0) finalisiert den vorigen Pfad
        if (prev.CurrentZbInTransit != now.CurrentZbInTransit)
        {
            if (prev.CurrentZbInTransit != 0
                && _pathByZb.TryGetValue(prev.CurrentZbInTransit, out var oldPath)
                && oldPath.Count > 0)
            {
                var oldAttrs = _zbAttrs.TryGetValue(prev.CurrentZbInTransit, out var pa)
                    ? pa : ((byte)0, (byte)0, (byte)0, (byte)0);
                _completedPaths.Add((prev.CurrentZbInTransit, oldAttrs, oldPath.ToArray()));
                if (_completedPaths.Count > 200) _completedPaths.RemoveAt(0);
                // Endpoint update: letzte beobachtete Position. "Scored" NUR
                // wenn der ZB nachweislich auf der Steininsel war (+0x76==3) —
                // sonst ist er gefangen/geparkt/noch unterwegs (= NotScored).
                string outcome = _steinInselScoredZbs.Contains(prev.CurrentZbInTransit)
                    ? "Scored" : "NotScored";
                _outcomeEndpoints[prev.CurrentZbInTransit] = (
                    oldAttrs.Item1, oldAttrs.Item2, oldAttrs.Item3, oldAttrs.Item4,
                    oldPath[^1], outcome);
                _pathByZb.Remove(prev.CurrentZbInTransit);
            }
            else if (prev.CurrentZbInTransit != 0)
            {
                // Path-Liste war leer/unbekannt — trotzdem aufräumen
                _pathByZb.Remove(prev.CurrentZbInTransit);
            }

            if (now.CurrentZbInTransit != 0)
            {
                // Cache ZB attrs — first try pool, then linked-list (ZB in transit hat anderen handle)
                if (pool is not null)
                {
                    var zb = pool.FirstOrDefault(p => p.HeaderId == now.CurrentZbInTransit);
                    if (zb.HeaderId == now.CurrentZbInTransit)
                        _zbAttrs[now.CurrentZbInTransit] = (zb.Hair, zb.Eyes, zb.Nose, zb.Feet);
                }
                if (!_zbAttrs.ContainsKey(now.CurrentZbInTransit))
                    TryReadZbAttrsFromList(mem, now.CurrentZbInTransit);
                string attrStr = _zbAttrs.TryGetValue(now.CurrentZbInTransit, out var a)
                    ? $" attrs=({a.H},{a.E},{a.N},{a.F})" : "";
                AddEvent(now.At, BubblewonderEventKind.ZbPopped,
                    $"ZB hdr1A=0x{now.CurrentZbInTransit:X4}{attrStr} gepoppt");
            }
        }

        if (prev.CurrentZbScored != now.CurrentZbScored && now.CurrentZbScored != 0)
        {
            AddEvent(now.At, BubblewonderEventKind.ZbScored,
                $"ZB hdr1A=0x{now.CurrentZbScored:X4} gescored");
            // ZB-ID merken für Outcome-Klassifikation beim Endpoint-Logging.
            _scoredZbs.Add(now.CurrentZbScored);
        }

        // Live-Endpoint-Update: für JEDEN ZB der schon ScoreEvent hatte,
        // die letzte beobachtete Position kontinuierlich updaten. So bleibt
        // beim Pfad-Reset die wirkliche letzte Animation-End-Pos drin —
        // statt Race-Condition-Werte.
        //
        // path.Count > 1 ist Pflicht: wenn der Handle gerade recycled wurde
        // (alter ZB scoring → handle für neuen ZB wiederverwendet) und der
        // neue Pfad nur den Spawn-Punkt enthält, würden wir sonst die Spawn-
        // Cell als "Scored-Endpoint" speichern. Das verschmutzt Goal-Cell-
        // Learning systematisch.
        foreach (var (handle, path) in _pathByZb)
        {
            if (path.Count < 2) continue;
            if (!_scoredZbs.Contains(handle)) continue;
            // Outcome aus dem echten Steininsel-Marker (+0x76==3), nicht aus
            // der mehrdeutigen _scoredZbs-Liste: gefangene/geparkte ZBs landen
            // sonst als "Scored" in der Goal-Cell-Lernung.
            string outcome = _steinInselScoredZbs.Contains(handle) ? "Scored" : "NotScored";
            var attrs = _zbAttrs.TryGetValue(handle, out var pa)
                ? pa : ((byte)0, (byte)0, (byte)0, (byte)0);
            _outcomeEndpoints[handle] = (
                attrs.Item1, attrs.Item2, attrs.Item3, attrs.Item4,
                path[^1], outcome);
        }

        for (int i = 0; i < prev.PositionCounters.Count && i < now.PositionCounters.Count; i++)
        {
            if (prev.PositionCounters[i] == 0 && now.PositionCounters[i] != 0)
            {
                ushort handle = now.PositionHandles[i];
                AddEvent(now.At, BubblewonderEventKind.PositionActivated,
                    $"Position[{i}] aktiviert (counter=1, handle=0x{handle:X4})");

                // Track path per ZB and derive an observed edge.
                if (handle != 0)
                {
                    if (!_pathByZb.TryGetValue(handle, out var path))
                        _pathByZb[handle] = path = new List<int>();
                    // Spawn-Detection:
                    //   1. path.Count == 0 → IMMER Spawn (Initial)
                    //   2. path.Count > 0, weit-Sprung, vorige Zelle war Insel-
                    //      Zwischenstation (0x15/0x16) → Insel-RE-LAUNCH. Die neue Pos ist
                    //      die INSEL-Spawn-Zelle. (Ersetzt die alte Eckzonen-Heuristik, die
                    //      Nicht-Eck-Spawns wie (4,1) verfehlte — DER Grund warum das Lernen
                    //      früher nicht klappte.)
                    //   3. Sonst: Sticky-Befreiung / Sampling-Bug → KEIN Spawn
                    bool isSpawn = path.Count == 0;
                    bool islandRelaunch = false;
                    if (!isSpawn)
                    {
                        int last = path[^1];
                        int dRow = Math.Abs(i / 13 - last / 13);
                        int dCol = Math.Abs(i % 13 - last % 13);
                        bool jumped = (dRow > 1 || dCol > 1);
                        if (jumped && IsIslandCellLive(mem, last))
                        {
                            isSpawn = true;
                            islandRelaunch = true;
                            path.Clear();
                        }
                    }
                    // Auch der Initial-Spawn-Fall (path leer) kann ein Insel-Re-Launch sein,
                    // wenn der ZB beim Hochheben kurz aus dem Pfad-Tracking fiel: prüfe den
                    // zuletzt bekannten Endpoint des ZB.
                    if (isSpawn && !islandRelaunch
                        && _outcomeEndpoints.TryGetValue(handle, out var prevEp)
                        && IsIslandCellLive(mem, prevEp.LastPos))
                        islandRelaunch = true;
                    if (isSpawn)
                    {
                        _spawnPositionCount[i] = _spawnPositionCount.GetValueOrDefault(i) + 1;
                        if (islandRelaunch) LearnedIslandSpawn = i;
                        int poolY = ReadZbPoolY(mem, handle);
                        if (!_spawnDetails.TryGetValue(i, out var lst))
                            _spawnDetails[i] = lst = new List<(ushort, int)>();
                        lst.Add((handle, poolY));
                        // Hawk-Modus: berechne Sim-Pfad-Vorhersage.
                        TryPredictPath(handle, spawnPos: i, mem, now.At);
                    }
                    if (path.Count > 0)
                    {
                        var edge = (path[^1], i);
                        _observedEdges.TryGetValue(edge, out var c);
                        _observedEdges[edge] = c + 1;
                        // Per-edge: speichere ZB-Attribute (für Conditional-Routing-Analyse)
                        if (_zbAttrs.TryGetValue(handle, out var attrs))
                        {
                            if (!_edgeAttrs.TryGetValue(edge, out var list))
                                _edgeAttrs[edge] = list = new List<(byte, byte, byte, byte)>();
                            list.Add(attrs);
                        }
                        // Spawn-Direction live ableiten: erster Schritt nach Spawn
                        // ist die Maschinen-Direction. path[0] ist die Spawn-Cell.
                        if (path.Count == 1 && !_spawnDirections.ContainsKey(path[0]))
                        {
                            int from = path[0];
                            int dRow = i / 13 - from / 13;
                            int dCol = i % 13 - from % 13;
                            Simulator.Direction? dir = (dRow, dCol) switch
                            {
                                (-1,  0) => Simulator.Direction.Up,
                                ( 1,  0) => Simulator.Direction.Down,
                                ( 0, -1) => Simulator.Direction.Left,
                                ( 0,  1) => Simulator.Direction.Right,
                                _ => null,
                            };
                            if (dir is { } d) _spawnDirections[from] = d;
                        }
                    }
                    // Hawk-Diff: vergleiche aktuelle Position gegen Vorhersage
                    // (path.Count entspricht dem Index in predictedPath für die
                    // gerade gemeldete Position, weil wir add unten machen).
                    CheckHawkPrediction(handle, currentPos: i, stepIndex: path.Count, now.At,
                        pathSoFar: path, mem: mem);
                    path.Add(i);
                }
            }
            else if (prev.PositionCounters[i] != 0 && now.PositionCounters[i] == 0)
                AddEvent(now.At, BubblewonderEventKind.PositionDeactivated,
                    $"Position[{i}] cleared");
        }

        // ZB scored — nur Event loggen, NICHT path löschen (score feuert mehrfach pro ZB).
        // Path-Reset passiert beim NÄCHSTEN Pop (s.o.).
        // → keine Aktion mehr hier; das passiert oben bei ZbPopped

        for (int i = 0; i < 24; i++)
        {
            if (prev.ActionSlotState[i] != now.ActionSlotState[i])
                AddEvent(now.At, BubblewonderEventKind.SlotStateChanged,
                    $"Slot[{i,2}] 0x{prev.ActionSlotState[i]:X4} → 0x{now.ActionSlotState[i]:X4}");
        }
    }

    private void AddEvent(DateTime at, BubblewonderEventKind kind, string desc)
    {
        _events.AddLast(new BubblewonderEvent(at, kind, desc));
        while (_events.Count > MaxEvents) _events.RemoveFirst();
    }

    /// <summary>Hawk-Modus: berechnet den Simulator-Pfad für einen frisch gespawneten
    /// ZB und cacht ihn für späteren Live-Vergleich. Pre-Conditions: ZB-Attribute
    /// müssen im _zbAttrs-Cache liegen, Spawn-Position muss zu einer Maschine passen.</summary>
    private void TryPredictPath(ushort handle, int spawnPos, IMemoryReader mem, DateTime at)
    {
        // Re-Spawn-Reset: alter predicted-Pfad ist obsolet wenn der gleiche ZB
        // erneut von einer (anderen) Maschine startet, z.B. nach Insel-Parking.
        _predictedPaths.Remove(handle);
        _hawkMismatchCount.Remove(handle);
        _hawkVerifiedZbs.Remove(handle);

        if (!_zbAttrs.TryGetValue(handle, out var attrs)) return;
        try
        {
            var bwState = BubblewonderState.Read(mem);
            var grid = Simulator.BubblewonderGridModelBuilder.FromState(
                bwState, mem,
                liveSpawnPositions: _spawnPositionCount.Keys.ToArray(),
                liveSpawnDirections: _spawnDirections,
                knownGoalCells: LearnedGoalCells);
            var simZb = new Simulator.SimZb(handle, attrs.H, attrs.E, attrs.N, attrs.F);
            var machine = grid.Machines.FirstOrDefault(m => m.StartCellIndex == spawnPos);
            if (machine is null) return;
            var simResult = Simulator.BubblewonderSimulator.Simulate(grid, simZb, machine.Index);
            _predictedPaths[handle] = simResult.PathPositions;
            AddEvent(at, BubblewonderEventKind.ZbPopped,
                $"  ↳ Sim-Vorhersage Pfad: {string.Join("→", simResult.PathPositions)}  ({simResult.Outcome})");
        }
        catch (Exception ex)
        {
            AddEvent(at, BubblewonderEventKind.HawkMismatch,
                $"⚠ HAWK Sim-Vorhersage fehlgeschlagen für ZB hdr1A=0x{handle:X4}: {ex.Message}");
        }
    }

    /// <summary>Hawk-Modus: vergleicht aktuelle Live-Position mit Sim-Vorhersage.
    /// Bei Mismatch beim ersten Step: re-predict mit korrigierter Direction
    /// (= Self-Correcting Hawk).</summary>
    private void CheckHawkPrediction(ushort handle, int currentPos, int stepIndex, DateTime at,
                                      IReadOnlyList<int>? pathSoFar, IMemoryReader mem)
    {
        if (!_predictedPaths.TryGetValue(handle, out var predicted)) return;
        if (stepIndex >= predicted.Count)
        {
            AddEvent(at, BubblewonderEventKind.HawkMismatch,
                $"⚠ HAWK ZB hdr1A=0x{handle:X4}: live läuft länger als Sim-Vorhersage " +
                $"(step {stepIndex}, predicted endet bei {predicted.Count})");
            _hawkMismatchCount[handle] = _hawkMismatchCount.GetValueOrDefault(handle) + 1;
            return;
        }
        if (predicted[stepIndex] != currentPos)
        {
            // Self-Correcting: Mismatch beim ERSTEN Step → die ursprüngliche
            // Maschinen-Direction war wohl falsch. Re-predict mit der echten
            // Bewegung als neuer Start.
            if (stepIndex == 1 && pathSoFar is { Count: >= 1 })
            {
                if (TryRePredictFromCurrent(handle, fromPos: pathSoFar[0],
                    actualNextPos: currentPos, mem, at))
                    return;
            }
            AddEvent(at, BubblewonderEventKind.HawkMismatch,
                $"⚠ HAWK ZB hdr1A=0x{handle:X4} step={stepIndex}: " +
                $"predicted pos={predicted[stepIndex]}, actual pos={currentPos}");
            _hawkMismatchCount[handle] = _hawkMismatchCount.GetValueOrDefault(handle) + 1;
            return;
        }
        // Letzter Step + match → vollständig verifiziert.
        if (stepIndex == predicted.Count - 1 && _hawkMismatchCount.GetValueOrDefault(handle) == 0)
        {
            if (_hawkVerifiedZbs.Add(handle))
            {
                AddEvent(at, BubblewonderEventKind.HawkPredictionVerified,
                    $"✓ HAWK ZB hdr1A=0x{handle:X4} Pfad vollständig wie vorhergesagt " +
                    $"({predicted.Count} steps)");
            }
        }
    }

    /// <summary>Self-Correcting Hawk: re-predict ab actualNextPos mit der live
    /// beobachteten Direction (von fromPos zu actualNextPos). Wird ausgelöst
    /// wenn die initiale Maschinen-Direction-Schätzung falsch war.</summary>
    private bool TryRePredictFromCurrent(ushort handle, int fromPos, int actualNextPos,
                                          IMemoryReader mem, DateTime at)
    {
        if (!_zbAttrs.TryGetValue(handle, out var attrs)) return false;
        int dRow = actualNextPos / 13 - fromPos / 13;
        int dCol = actualNextPos % 13 - fromPos % 13;
        Simulator.Direction? dir = (dRow, dCol) switch
        {
            (-1,  0) => Simulator.Direction.Up,
            ( 1,  0) => Simulator.Direction.Down,
            ( 0, -1) => Simulator.Direction.Left,
            ( 0,  1) => Simulator.Direction.Right,
            _ => null,
        };
        if (dir is not { } liveDir) return false;
        // Lerne die korrigierte Direction für die Spawn-Pos — kommt ja jetzt
        // aus der echten Live-Beobachtung, nicht aus der Engine-dirCode-Schätzung.
        // ABER: nur wenn Spawn-Cell keine Routing-Cell ist (sonst spiegelt
        // der erste Step den Cell-Effekt, nicht die echte Maschinen-Direction).
        try
        {
            var bwState = BubblewonderState.Read(mem);
            var grid = Simulator.BubblewonderGridModelBuilder.FromState(
                bwState, mem,
                liveSpawnPositions: _spawnPositionCount.Keys.ToArray(),
                liveSpawnDirections: _spawnDirections,
                knownGoalCells: LearnedGoalCells);
            var simZb = new Simulator.SimZb(handle, attrs.H, attrs.E, attrs.N, attrs.F);
            var simResult = Simulator.BubblewonderSimulator.SimulateFromPosition(
                grid, simZb, actualNextPos, liveDir);
            // Neue predicted: fromPos + actualNextPos + Sim-Pfad ab actualNextPos
            var rebuilt = new List<int> { fromPos };
            rebuilt.AddRange(simResult.PathPositions);
            _predictedPaths[handle] = rebuilt;
            AddEvent(at, BubblewonderEventKind.ZbPopped,
                $"  ↳ Sim-Re-Vorhersage (korrigierte Direction {liveDir}): " +
                $"{string.Join("→", rebuilt)}");
            return true;
        }
        catch { return false; }
    }

    /// <summary>F12-Dump-Section: pro Cell, welche Eintritts→Ausgangs-Richtungen
    /// wurden bisher live beobachtet. Aus den abgeschlossenen + aktiven ZB-Pfaden
    /// rekonstruiert. Ziel: das echte F4..F7-Routing-Schema empirisch ableiten.</summary>
    public void WriteRoutingObservations(StreamWriter sw, IMemoryReader mem)
    {
        sw.WriteLine("=== Routing-Beobachtungen pro Cell (live) ===");
        sw.WriteLine("  Pro Cell: alle (EntryDir → ExitDir)-Übergänge aus beobachteten ZB-Pfaden.");
        sw.WriteLine("  Wenn eine Cell mit gleicher EntryDir verschiedene ExitDirs zeigt → nicht-deterministisch.");
        sw.WriteLine();

        // Pro Cell-PositionIndex: Dictionary EntryDir → Set<ExitDir>
        var perCell = new Dictionary<int, Dictionary<string, HashSet<string>>>();
        var allPaths = _completedPaths.Select(c => c.Path).Concat(_pathByZb.Values).ToList();

        foreach (var path in allPaths)
        {
            for (int i = 1; i < path.Count - 1; i++)
            {
                int prev = path[i - 1], curr = path[i], next = path[i + 1];
                string entryDir = DirectionTag(prev, curr);
                string exitDir = DirectionTag(curr, next);
                if (!perCell.TryGetValue(curr, out var byEntry))
                    perCell[curr] = byEntry = new();
                if (!byEntry.TryGetValue(entryDir, out var exits))
                    byEntry[entryDir] = exits = new();
                exits.Add(exitDir);
            }
        }

        if (perCell.Count == 0)
        {
            sw.WriteLine("  (noch keine Übergänge — schick ein paar ZBs durch und drück F12)");
            return;
        }

        // Lade Cell-Layout für Type/F4-F7-Annotation
        Dictionary<int, (string type, string fbits)>? cellInfo = null;
        try
        {
            var bwState = BubblewonderState.Read(mem);
            cellInfo = new();
            foreach (var b in bwState.LiveBubbles)
            {
                var regs = b.AsRegsRecord();
                if (regs.F1 >= 12 || regs.F2 >= 13) continue;
                int pos = regs.PositionIndex;
                string type = MechanismClassifier.Classify(regs).ToString();
                string fbits = $"({regs.F4},{regs.F5},{regs.F6},{regs.F7})";
                cellInfo[pos] = (type, fbits);
            }
        }
        catch { /* best-effort */ }

        foreach (var pos in perCell.Keys.OrderBy(p => p))
        {
            int row = pos / 13, col = pos % 13;
            string typeTag = cellInfo is { } ci && ci.TryGetValue(pos, out var info)
                ? $"[{info.type}, F4..F7={info.fbits}]"
                : "[leer/unbekannt]";
            sw.WriteLine($"  Pos {pos,3} ({row,2},{col,2}) {typeTag}");
            foreach (var (entry, exits) in perCell[pos].OrderBy(kv => kv.Key))
            {
                string flag = exits.Count > 1 ? " ⚠ NICHT-DETERMINISTISCH" : "";
                sw.WriteLine($"      Eintritt {entry,-7} → Ausgang {{{string.Join(",", exits.OrderBy(e => e))}}}{flag}");
            }
        }
    }

    /// <summary>Bestimmt die Bewegungsrichtung von posA zu posB im 12×13-Grid.</summary>
    private static string DirectionTag(int from, int to)
    {
        int fr = from / 13, fc = from % 13;
        int tr = to / 13, tc = to % 13;
        if (tr < fr) return "↑Up";
        if (tr > fr) return "↓Down";
        if (tc < fc) return "←Left";
        if (tc > fc) return "→Right";
        return "?";
    }

    /// <summary>F12-Dump-Section: Solver-Plan für den aktuellen Pool.
    /// Liest Live-Pool + Live-Grid und berechnet die beste ZB→Maschine-Sequenz.</summary>
    public void WriteSolverPlan(StreamWriter sw, IMemoryReader mem)
    {
        sw.WriteLine("=== Bubblewonder Solver-Plan ===");
        try
        {
            var bwState = BubblewonderState.Read(mem);
            if (!bwState.IsActive)
            {
                sw.WriteLine("  (Bubblewonder nicht aktiv)");
                return;
            }
            var grid = Simulator.BubblewonderGridModelBuilder.FromState(
                bwState, mem,
                liveSpawnPositions: _spawnPositionCount.Keys.ToArray(),
                liveSpawnDirections: _spawnDirections,
                knownGoalCells: LearnedGoalCells);
            if (grid.Machines.Count == 0)
            {
                sw.WriteLine("  ⚠ Keine Maschinen erkannt — Spawn-Mapping für REGS " +
                             $"{bwState.RegsResourceId} fehlt evtl.");
                return;
            }
            var pool = PoolScanner.Scan(mem);
            if (pool.Count == 0)
            {
                sw.WriteLine("  (Pool leer — nichts zu schicken)");
                return;
            }
            var simZbs = pool
                .Select(p => new Simulator.SimZb(p.HeaderId, p.Hair, p.Eyes, p.Nose, p.Feet))
                .ToList();

            sw.WriteLine($"  Pool: {pool.Count} ZBs, {grid.Machines.Count} Maschinen, " +
                         $"REGS {bwState.RegsResourceId}");
            for (int m = 0; m < grid.Machines.Count; m++)
            {
                var mach = grid.Machines[m];
                int row = mach.StartCellIndex / 13, col = mach.StartCellIndex % 13;
                string isleTag = mach.IsIsland ? " [INSEL]" : "";
                sw.WriteLine($"    Maschine[{m}] @ ({row,2},{col,2}) → {mach.StartDirection}{isleTag}");
            }

            var result = simZbs.Count <= Simulator.BubblewonderSolver.BruteForceMaxZbs
                ? Simulator.BubblewonderSolver.SolveBruteForce(grid, simZbs)
                : Simulator.BubblewonderSolver.SolveGreedy(grid, simZbs);

            sw.WriteLine($"  Strategie: {result.Strategy}");
            sw.WriteLine($"  Vorhersage: {result.Survivors}/{simZbs.Count} ZBs überleben");
            sw.WriteLine();
            sw.WriteLine("  Empfohlene Sequenz:");
            for (int i = 0; i < result.Assignments.Count; i++)
            {
                var a = result.Assignments[i];
                var mach = grid.Machines[a.MachineIdx];
                int row = mach.StartCellIndex / 13, col = mach.StartCellIndex % 13;
                sw.WriteLine($"    {i + 1,2}. ZB hdr1A=0x{a.Zb.HeaderId:X4} " +
                             $"(H{a.Zb.Hair} E{a.Zb.Eyes} N{a.Zb.Nose} F{a.Zb.Feet})  " +
                             $"→ Maschine[{a.MachineIdx}] @({row,2},{col,2})");
            }
        }
        catch (Exception ex)
        {
            sw.WriteLine($"  ⚠ Solver-Fehler: {ex.Message}");
        }
    }

    /// <summary>F12-Dump-Section: alle live beobachteten Endpunkte gescorte ZBs.
    /// Aus den letzten Pfad-Positionen leitet sich ab WO im Grid die
    /// Ziel-Cells liegen.</summary>
    /// <summary>F12-Dump: ZB-Outcome-Felder direkt aus den Engine-Objekten.
    /// Aus der Disasm (PROCESS-fn 0x425a00, Jump-Table 0x425ee4) klassifiziert
    /// das Spiel das ZB-Outcome über <c>ZB[+0x76]</c> (0..3):
    ///   case 0/2 → Handle[+0x20] := 1        (zurück in Pool / Insel-Park)
    ///   case 1   → Handle[+0x20] := 0x8001
    ///   case 3   → Handle[+0x20] := 0x4008001 (auf Steininsel = echtes Score)
    /// Diese Sektion dumpt +0x1A/+0x20/+0x76 jedes Knotens, damit wir live
    /// verifizieren welcher +0x76-Wert "auf Insel geparkt" bedeutet. Die
    /// vom Tracker als "Scored" geführten ZBs werden markiert.</summary>
    public void WriteZbOutcomeFields(StreamWriter sw, IMemoryReader mem)
    {
        sw.WriteLine("=== ZB-Outcome-Felder (Engine-Objekte: +0x1A/+0x20/+0x76) ===");
        sw.WriteLine("  Disasm-Hypothese: +0x76 = Outcome-Typ. Handle +0x20: 1=Pool/Insel,");
        sw.WriteLine("  0x8001=Insel-geparkt (Zwischenstation, +0x76=1), 0x04008001=auf Steininsel");
        sw.WriteLine("  (echtes Score). ★ = Tracker hält für 'Scored'.");
        int count = 0;
        foreach (var node in EngineObjectList.Walk(mem, 0x80))
        {
            if (node.Bytes.Length < 0x78) continue;
            ushort hdr1A = BitConverter.ToUInt16(node.Bytes, 0x1A);
            uint handle = node.Handle;            // = +0x20
            ushort outcome76 = BitConverter.ToUInt16(node.Bytes, 0x76);
            // Nur ZB-artige Knoten: Bubbles (0x04188000) und Maschinen
            // (0x04108000) raus, sonst wird die Liste unübersichtlich.
            if (handle == 0x04188000 || handle == 0x04108000) continue;
            // hdr1A 0 = kein echtes Spielobjekt
            if (hdr1A == 0) continue;
            string handleTag = handle switch
            {
                ZoombiniHandle.Pool => "Pool/Insel",
                ZoombiniHandle.Launched => "Steininsel(Score)",
                ZoombiniHandle.Held => "drag-marked",
                ZoombiniHandle.Parked => "Insel-geparkt",
                _ => $"0x{handle:X8}",
            };
            string scoredMark = _steinInselScoredZbs.Contains(hdr1A) ? " ★Steininsel(+0x76==3)"
                              : _scoredZbs.Contains(hdr1A) ? " (in alter Score-Liste, aber +0x76≠3)" : "";
            string endpointTag = _outcomeEndpoints.TryGetValue(hdr1A, out var ep)
                ? $" Endpoint=({ep.LastPos / 13},{ep.LastPos % 13}) [{ep.Outcome}]" : "";
            sw.WriteLine($"  hdr1A=0x{hdr1A:X4}  +0x20=0x{handle:X8} ({handleTag})  " +
                         $"+0x76={outcome76}{scoredMark}{endpointTag}");
            count++;
        }
        if (count == 0)
            sw.WriteLine("  (keine ZB-Knoten in der Engine-Liste gefunden)");
    }

    public void WriteOutcomeEndpoints(StreamWriter sw)
    {
        sw.WriteLine("=== Live Outcome-Endpunkte (wo ZBs scoring/landen) ===");
        // Aktuell laufende Pfade (vor dem Finalisieren beim nächsten ZbPop)
        // auch als "vorläufige Endpunkte" anzeigen — sonst sieht der User nix
        // bevor ein zweiter ZB gepoppt wurde.
        if (_pathByZb.Count > 0)
        {
            sw.WriteLine("  --- Laufende Pfade (letzte beobachtete Pos) ---");
            foreach (var (handle, path) in _pathByZb)
            {
                if (path.Count == 0) continue;
                int last = path[^1];
                int row = last / 13, col = last % 13;
                string scoredFlag = _scoredZbs.Contains(handle) ? " [bereits Scored-Event]" : "";
                sw.WriteLine($"  ZB hdr1A=0x{handle:X4} → bisher letzte Pos {last} ({row,2},{col,2}){scoredFlag}");
            }
            sw.WriteLine();
        }
        if (_outcomeEndpoints.Count == 0)
        {
            sw.WriteLine("  (noch keine Endpoints erfasst)");
            return;
        }
        sw.WriteLine("  --- Pro ZB letzter Endpoint ---");
        foreach (var (hdr, ep) in _outcomeEndpoints)
        {
            int row = ep.LastPos / 13, col = ep.LastPos % 13;
            sw.WriteLine($"  ZB hdr1A=0x{hdr:X4} ({ep.H},{ep.E},{ep.N},{ep.F}) " +
                         $"→ {ep.Outcome} bei Pos {ep.LastPos} ({row,2},{col,2})");
        }
        sw.WriteLine();
        sw.WriteLine($"  --- ZIEL-Cells (Scored) — gelernt aus Live-Beobachtung ---");
        var scoredByPos = _outcomeEndpoints.Values.Where(o => o.Outcome == "Scored")
            .GroupBy(o => o.LastPos)
            .Select(g => (Pos: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count).ToList();
        if (scoredByPos.Count == 0) sw.WriteLine("    (noch kein ZB gescored)");
        foreach (var (pos, count) in scoredByPos)
        {
            int row = pos / 13, col = pos % 13;
            sw.WriteLine($"    Pos {pos,3} ({row,2},{col,2})  ×{count}");
        }
        sw.WriteLine();
        sw.WriteLine($"  --- NICHT-Ziel-Cells (Sticky/Insel/Loop) ---");
        var notScoredByPos = _outcomeEndpoints.Values.Where(o => o.Outcome != "Scored")
            .GroupBy(o => o.LastPos)
            .Select(g => (Pos: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count).ToList();
        if (notScoredByPos.Count == 0) sw.WriteLine("    (alle erfassten ZBs gescored)");
        foreach (var (pos, count) in notScoredByPos)
        {
            int row = pos / 13, col = pos % 13;
            sw.WriteLine($"    Pos {pos,3} ({row,2},{col,2})  ×{count}");
        }
    }

    /// <summary>F12-Dump-Section: Hawk-Status pro ZB.</summary>
    public void WriteHawkStatus(StreamWriter sw)
    {
        sw.WriteLine($"=== Hawk-Modus (Sim vs. Live) ===");
        if (_predictedPaths.Count == 0)
        {
            sw.WriteLine("  (noch keine Vorhersagen — kein ZB seit Round-Start gepoppt)");
            return;
        }
        // Lookup: completed-paths (ZBs die bereits gescored sind, aus _pathByZb raus)
        var completedByHandle = _completedPaths.ToDictionary(c => c.Hdr1A, c => c.Path);

        foreach (var (handle, predicted) in _predictedPaths)
        {
            int mismatches = _hawkMismatchCount.GetValueOrDefault(handle);
            // Aktiver Pfad bevorzugt; sonst zuletzt abgeschlossener Pfad.
            IReadOnlyList<int>? actual = _pathByZb.TryGetValue(handle, out var ap) ? ap
                : completedByHandle.TryGetValue(handle, out var cp) ? cp
                : null;
            int actualSteps = actual?.Count ?? 0;
            // "Done" ist entweder: ZB scoring abgeschlossen (= raus aus _pathByZb,
            // oder Score-Event eingegangen — der Pfad bleibt bis zum nächsten
            // Transit-Wechsel in _pathByZb stehen, daher zusätzlich _scoredZbs
            // prüfen) oder: live-Pfad hat predicted-Länge erreicht (= ZB klebt
            // fest / ist geparkt, hat den Sim-Pfad komplett abgelaufen).
            bool isDone = !_pathByZb.ContainsKey(handle)
                       || _scoredZbs.Contains(handle)
                       || (actual is not null && actualSteps >= predicted.Count);
            // Off-by-one akzeptieren: Sim simuliert oft den finalen Out-of-Grid-
            // Step (z.B. von Reihe 10 in Reihe 11), den der Live-Tracker nicht
            // mehr als PositionActivated mitkriegt. Wenn alle vorherigen Steps
            // korrekt waren, ist das KEIN echter Fehler.
            bool verifiedExact = actualSteps == predicted.Count;
            bool verifiedOffByOne = actualSteps == predicted.Count - 1 && isDone;
            bool verified = mismatches == 0 && (verifiedExact || verifiedOffByOne)
                         && actual is not null;
            string status = verified ? "✓ VERIFIED" :
                            mismatches > 0 ? $"⚠ {mismatches} MISMATCHES" :
                            isDone ? "📍 abgeschlossen (Längen-Diff)" :
                            "… läuft";
            sw.WriteLine($"  ZB hdr1A=0x{handle:X4}: {status}  " +
                         $"(predicted {predicted.Count} steps, actual {actualSteps})");
            if ((mismatches > 0 || (isDone && !verified)) && actual is not null)
            {
                sw.WriteLine($"    predicted: {string.Join("→", predicted)}");
                sw.WriteLine($"    actual:    {string.Join("→", actual)}");
            }
        }
    }

    /// <summary>Schreibe Event-Timeline in den Dump. Zeigt was in den letzten
    /// ~20 Sekunden auf dem Grid passiert ist.</summary>
    public void WriteTimeline(StreamWriter sw)
    {
        sw.WriteLine($"=== Bubblewonder Event-Timeline (last {_events.Count} events, ~{_snapshots.Count * 200}ms) ===");
        if (_events.Count == 0)
        {
            sw.WriteLine("  (no events yet — tracker just started or no Bubblewonder activity)");
            return;
        }
        DateTime? lastAt = null;
        foreach (var ev in _events)
        {
            string delta = lastAt.HasValue
                ? $"+{(ev.At - lastAt.Value).TotalMilliseconds,5:F0}ms"
                : "  start";
            sw.WriteLine($"  {ev.At:HH:mm:ss.fff}  {delta}  [{ev.Kind,-22}]  {ev.Description}");
            lastAt = ev.At;
        }
    }

    /// <summary>Schreibe alle live-beobachteten Edges (Position-Sequenzen
    /// pro ZB) mit Conditional-Attribut-Analyse.</summary>

    /// <summary>Schreibt eine kompakte Übersicht der Sticky-Cells (f0=5, "Festklebefeld").
    /// Zeigt: Position, REGS-Daten, Pair-Gruppen nach f3 (vermutete Farb-Channel),
    /// Hex-Dump 0x60..0x180 für Diff-Analyse zwischen Snapshots.</summary>
    public void WriteStickyCells(StreamWriter sw, IMemoryReader mem)
    {
        sw.WriteLine($"=== Sticky-Cells (f0=5, 'Festklebefeld' / Sternchen) ===");
        var stickies = BubbleObjectScanner.Scan(mem)
            .Where(b => b.AsRegsRecord().F0 == 5)
            .ToList();
        if (stickies.Count == 0)
        {
            sw.WriteLine("  (keine Sticky-Cells im Grid)");
            return;
        }

        var byChannel = stickies.GroupBy(b => b.AsRegsRecord().F3).ToList();
        sw.WriteLine($"  {stickies.Count} Cells, {byChannel.Count} Channel-Gruppen (nach f3):");
        foreach (var grp in byChannel.OrderBy(g => g.Key))
        {
            var positions = string.Join(", ",
                grp.Select(b => $"pos={b.AsRegsRecord().PositionIndex} (hdr=0x{b.HeaderId:X4})"));
            string sizeMark = grp.Count() >= 2 ? "✓ Pair/Group" : "⚠ Singleton";
            sw.WriteLine($"    f3={grp.Key}: {grp.Count()}× — {positions} {sizeMark}");
        }

        sw.WriteLine();
        sw.WriteLine("  --- Live-State (welcher ZB klebt aktuell) ---");
        foreach (var b in stickies.OrderBy(b => b.AsRegsRecord().PositionIndex))
        {
            var regs = b.AsRegsRecord();
            string state = b.StickyTrappedZb == 0
                ? "leer"
                : $"⭐ ZB hdr1A=0x{b.StickyTrappedZb:X4} klebt fest";
            sw.WriteLine($"  Sticky pos={regs.PositionIndex,3}  Color-Channel f3={regs.F3}  → {state}");
        }

        sw.WriteLine();
        sw.WriteLine("  --- Pro Cell: Hex 0x60..0x180 ---");
        foreach (var b in stickies.OrderBy(b => b.AsRegsRecord().PositionIndex))
        {
            var regs = b.AsRegsRecord();
            sw.WriteLine($"  Sticky hdr=0x{b.HeaderId:X4} pos={regs.PositionIndex,3}  f3={regs.F3}  f8={regs.F8}");
            byte[]? raw = b.RawBytes;
            if (raw is null) continue;
            for (int row = 0x60; row < 0x180; row += 16)
            {
                if (row + 16 > raw.Length) break;
                string hex = BitConverter.ToString(raw, row, 16).Replace("-", " ");
                sw.WriteLine($"    +0x{row:X3}: {hex}");
            }
        }
    }

    /// <summary>Statische Trigger→Target-Erkennung über den verifizierten +0x166-Read.
    /// Jede f0=6 Trigger-Cell hält dort den hdr1A des Target-Switches.</summary>
    public void WriteTriggerStaticCrossRef(StreamWriter sw, IMemoryReader mem)
    {
        sw.WriteLine($"=== Statische Trigger→Target-Cross-Reference (+0x{BubblewonderMemoryMap.TriggerTargetHandleOffset:X3}) ===");
        var bubbles = BubbleObjectScanner.Scan(mem);
        var triggers = bubbles.Where(b => b.AsRegsRecord().F0 == 6).ToList();
        var switches = bubbles.Where(b => b.AsRegsRecord().F0 == 4).ToList();
        var switchByHdr = switches.ToDictionary(s => s.HeaderId);

        if (triggers.Count == 0)
        {
            sw.WriteLine("  (keine Trigger-Cells im Grid)");
            return;
        }

        sw.WriteLine($"  Triggers: {triggers.Count}, Switches: {switches.Count}");
        sw.WriteLine();

        foreach (var t in triggers.OrderBy(t => t.AsRegsRecord().PositionIndex))
        {
            var tRegs = t.AsRegsRecord();
            int triggerSlot = tRegs.F3;
            string targetStr;
            if (t.TriggerTargetHandle == 0)
                targetStr = "(kein Target gesetzt)";
            else if (switchByHdr.TryGetValue(t.TriggerTargetHandle, out var target))
                targetStr = $"→ Switch hdr=0x{t.TriggerTargetHandle:X4} pos={target.AsRegsRecord().PositionIndex}";
            else
                targetStr = $"→ unknown handle 0x{t.TriggerTargetHandle:X4} (kein Switch im Grid)";
            sw.WriteLine($"  Trigger hdr=0x{t.HeaderId:X4} pos={tRegs.PositionIndex,3} slot={triggerSlot}: {targetStr}");
        }

        // Umgekehrt: pro Switch alle Trigger anzeigen die ihn referenzieren
        sw.WriteLine();
        sw.WriteLine("  --- Umgekehrt: pro Switch die referenzierenden Trigger ---");
        foreach (var s in switches.OrderBy(s => s.AsRegsRecord().PositionIndex))
        {
            var refs = triggers.Where(t => t.TriggerTargetHandle == s.HeaderId).ToList();
            string refStr = refs.Count > 0
                ? string.Join(", ", refs.Select(t =>
                    $"Trigger pos={t.AsRegsRecord().PositionIndex} (slot={t.AsRegsRecord().F3})"))
                : "(keine Trigger-Refs)";
            sw.WriteLine($"  Switch hdr=0x{s.HeaderId:X4} pos={s.AsRegsRecord().PositionIndex,3}: {refStr}");
        }
    }

    /// <summary>Schreibt aktuelle Live-States aller f0=4-Switches + f0=6-Trigger:
    /// initial-state (beim ersten Sehen), aktueller state, komplette Wechsel-Historie
    /// mit Timestamps. Plus REGS-f4..f7-Interpretation.</summary>
    public void WriteSwitchLiveStates(StreamWriter sw, IMemoryReader mem)
    {
        sw.WriteLine($"=== Live-Switch-States (f0=4 + f0=6, +0x{BubblewonderMemoryMap.SwitchStateOffset:X2} = State-Index) ===");
        var entries = BubbleObjectScanner.Scan(mem)
            .Where(b => b.AsRegsRecord().F0 == 4 || b.AsRegsRecord().F0 == 6)
            .OrderBy(b => b.AsRegsRecord().PositionIndex)
            .ToList();
        if (entries.Count == 0)
        {
            sw.WriteLine("  (keine Switches/Trigger im Grid)");
            return;
        }
        // F-Bit-Index → Direction-Anzeige (verifiziert 2026-05-03)
        string[] fBitNames = { "←Left", "↓Down", "→Right", "↑Up" };
        foreach (var b in entries)
        {
            var regs = b.AsRegsRecord();
            string typeTag = regs.F0 == 4 ? "Switch " : "Trigger";
            byte[] dirs = { (byte)regs.F4, (byte)regs.F5, (byte)regs.F6, (byte)regs.F7 };
            var setDirs = new List<string>();
            for (int i = 0; i < 4; i++)
                if (dirs[i] != 0) setDirs.Add(fBitNames[i]);
            string allDirs = setDirs.Count > 0 ? string.Join("/", setDirs) : "(keine)";
            byte stateIdx = b.SwitchStateIndex;
            string interp = stateIdx < 4
                ? fBitNames[stateIdx] + (dirs[stateIdx] != 0 ? "" : "  ⚠ (state ist nicht in REGS-Dirs!)")
                : $"? state={stateIdx} (out of range)";
            sw.WriteLine($"  {typeTag} hdr=0x{b.HeaderId:X4} pos={regs.PositionIndex,3}  state={stateIdx}  REGS-Dirs: {allDirs}  → aktiv: {interp}");
        }
    }

    public void WriteObservedEdges(StreamWriter sw)
    {
        sw.WriteLine($"=== Bubblewonder Live-Observed Edges ({_observedEdges.Count} unique) ===");
        if (_observedEdges.Count == 0)
        {
            sw.WriteLine("  (keine Edges beobachtet — noch kein ZB durchgelaufen)");
            return;
        }
        foreach (var ((from, to), cnt) in _observedEdges.OrderByDescending(kv => kv.Value))
        {
            string attrSummary = "";
            if (_edgeAttrs.TryGetValue((from, to), out var attrs) && attrs.Count > 0)
            {
                // Find which attribute(s) are common across all observations of this edge
                var hairs = attrs.Select(a => a.H).Distinct().ToList();
                var eyes = attrs.Select(a => a.E).Distinct().ToList();
                var noses = attrs.Select(a => a.N).Distinct().ToList();
                var feet = attrs.Select(a => a.F).Distinct().ToList();
                var summary = new List<string>();
                if (hairs.Count == 1) summary.Add($"H={hairs[0]}");
                if (eyes.Count == 1) summary.Add($"E={eyes[0]}");
                if (noses.Count == 1) summary.Add($"N={noses[0]}");
                if (feet.Count == 1) summary.Add($"F={feet[0]}");
                if (summary.Count > 0)
                    attrSummary = $"  ALL ZBs share: {string.Join(",", summary)}";
                else
                    attrSummary = $"  ZBs: {string.Join("; ", attrs.Select(a => $"({a.H},{a.E},{a.N},{a.F})"))}";
            }
            sw.WriteLine($"  Position[{from,3}] → Position[{to,3}]   (×{cnt} obs){attrSummary}");
        }

        // Vollständige abgeschlossene Pfade pro ZB
        if (_completedPaths.Count > 0)
        {
            sw.WriteLine();
            sw.WriteLine($"  --- Komplette abgeschlossene ZB-Pfade ({_completedPaths.Count}) ---");
            foreach (var (handle, attrs, path) in _completedPaths.TakeLast(20))
            {
                string attrStr = attrs == ((byte)0, (byte)0, (byte)0, (byte)0)
                    ? "" : $" attrs=({attrs.Item1},{attrs.Item2},{attrs.Item3},{attrs.Item4})";
                sw.WriteLine($"  ZB hdr1A=0x{handle:X4}{attrStr}:  {string.Join(" → ", path)}");
            }
        }

        // Plus aktuelle laufende ZB-Pfade (= unfinished traces)
        if (_pathByZb.Count > 0)
        {
            sw.WriteLine();
            sw.WriteLine("  --- Aktuell laufende ZB-Pfade (unfinished) ---");
            foreach (var (handle, path) in _pathByZb)
            {
                string attrStr = _zbAttrs.TryGetValue(handle, out var a)
                    ? $" attrs=({a.H},{a.E},{a.N},{a.F})" : "";
                sw.WriteLine($"  ZB hdr1A=0x{handle:X4}{attrStr}: {string.Join(" → ", path)}");
            }
        }
    }

    /// <summary>Reset — z.B. wenn Puzzle wechselt.</summary>
    /// <summary>Hdr1As aller ZBs, deren BEOBACHTETER Endpoint (letzte Pfad-Position) auf
    /// einer Insel-Zwischenstation (0x15/0x16) liegt = stabil GEPARKT. Aus den vom Tracker
    /// über mehrere Ticks gesammelten Pfaden — NICHT aus einem einzelnen, flackernden
    /// Snapshot (Grid-Pos/+0x76 können pro Tick auf (0,0)/0 springen). Der Renderer nutzt
    /// das, um Insel-ZBs zuverlässig zu erkennen, auch wenn der aktuelle Scan sie verfehlt.</summary>
    public HashSet<ushort> GetIslandParkedHdrs(IMemoryReader mem)
    {
        var set = new HashSet<ushort>();
        foreach (var (hdr, ep) in _outcomeEndpoints)
        {
            if (hdr == 0) continue;
            if (IsIslandCellLive(mem, ep.LastPos)) set.Add(hdr);
        }
        return set;
    }

    /// <summary>Ist die Grid-Position eine Insel-Zwischenstation (Zelltyp 0x15/0x16)?
    /// Liest die engine-eigene Zelltyp-Tabelle. Grundlage der Insel-Re-Launch-Erkennung.</summary>
    private static bool IsIslandCellLive(IMemoryReader mem, int pos)
    {
        if (pos < 0 || pos >= 12 * 13) return false;
        var t = mem.ReadBytes(BubblewonderMemoryMap.CellTypeTable + pos * 2, 2);
        if (t is null || t.Length < 2) return false;
        int ct = t[0] | (t[1] << 8);
        return ct == 0x15 || ct == 0x16;
    }

    public void Reset()
    {
        RoundEpoch++;
        _snapshots.Clear();
        _events.Clear();
        _pathByZb.Clear();
        _observedEdges.Clear();
        _edgeAttrs.Clear();
        _zbAttrs.Clear();
        _completedPaths.Clear();
        _spawnPositionCount.Clear();
        _spawnDetails.Clear();
        _spawnDirections.Clear();
        LearnedIslandSpawn = null;
        _outcomeEndpoints.Clear();
        _scoredZbs.Clear();
        _steinInselScoredZbs.Clear();
        _predictedPaths.Clear();
        _hawkMismatchCount.Clear();
        _hawkVerifiedZbs.Clear();
    }

    /// <summary>Schreibt die observierten Spawn-Positionen (= erste Pfad-Position jedes ZBs).
    /// Jede einzigartige Spawn-Pos = eine Bubble-Maschine. Aus mehreren ZBs hintereinander
    /// lernt sich pro Runde wie viele Maschinen es gibt und wo sie sind.</summary>
    public void WriteSpawnPositions(StreamWriter sw)
    {
        sw.WriteLine($"=== Spawn-Positionen (= Bubble-Maschinen, live aus ZB-Pfad-Anfängen) ===");
        if (_spawnPositionCount.Count == 0)
        {
            sw.WriteLine("  (noch keine ZBs durchgelaufen — schick einen los und drück F12)");
            return;
        }
        sw.WriteLine($"  {_spawnPositionCount.Count} verschiedene Spawn-Positionen beobachtet:");
        foreach (var (pos, count) in _spawnPositionCount.OrderByDescending(kv => kv.Value))
        {
            int row = pos / 13, col = pos % 13;
            string mark = count >= 3 ? " ★ (Maschine)" : count == 1 ? " ⚠ (Singleton — vielleicht Insel-Spawn)" : "";
            sw.WriteLine($"    Pos {pos,3} ({row,2},{col,2})  {count}× ZBs gestartet{mark}");
            // Pool-Cluster-Zuordnung: aus welchem Pool kamen die ZBs?
            if (_spawnDetails.TryGetValue(pos, out var details))
            {
                var byPool = details.GroupBy(d => d.poolY < 300 ? "Hauptpool" : $"Insel(y≈{d.poolY})");
                foreach (var g in byPool)
                    sw.WriteLine($"        {g.Count()}× aus {g.Key}: hdr1As=[{string.Join(",", g.Select(d => $"0x{d.handle:X2}"))}]");
            }
        }
    }

    /// <summary>Schreibt die statisch erkannten Bubble-Maschinen-Objects
    /// (Engine-Handle 0x04108000) PLUS Cross-Reference auf welche Bubble-Cell
    /// jede Maschine als Spawn-Ziel referenziert.</summary>
    /// <summary>Schreibt die hardcoded Spawn-Cells für die aktuelle REGS-Resource.
    /// Out-of-the-box ohne ZB-Spawns nötig.</summary>
    public void WriteHardcodedSpawnMapping(StreamWriter sw, IMemoryReader mem)
    {
        sw.WriteLine($"=== Hardcoded Spawn-Mapping (out-of-the-box pro REGS) ===");
        // Verwende den BubblewonderState der die REGS-ID korrekt aus Heap-Header liest
        var bwState = BubblewonderState.Read(mem);
        int regsId = bwState.RegsResourceId;
        sw.WriteLine($"  Aktuelle REGS-ID: {regsId} (0x{regsId:X4})");

        var cells = BubblewonderSpawnMappings.GetSpawnCells(regsId);
        if (cells is null)
        {
            sw.WriteLine($"  ⚠ KEIN hardcoded Mapping für REGS {regsId} — Tracker fällt auf Live-Detection zurück.");
            return;
        }
        sw.WriteLine($"  ✓ Hardcoded: {cells.Count} Spawn-Cells:");
        foreach (var pos in cells)
        {
            int row = pos / 13, col = pos % 13;
            string zone = BubblewonderSpawnMappings.IsIslandCell(pos)
                ? (row <= 3 ? " [INSEL oben-links]" : " [INSEL unten-rechts]")
                : " [Hauptpool]";
            sw.WriteLine($"    Pos {pos,3} ({row,2},{col,2}){zone}");
        }
    }

    /// <summary>Live: zeigt Anzahl der Bubble-Maschinen-Objects im aktuellen Layout
    /// (handle=0x04108000) plus Direction-Code pro Maschine.</summary>
    public void WriteBubbleMachines(StreamWriter sw, IMemoryReader mem)
    {
        const uint MachineHandle = 0x04108000;
        sw.WriteLine($"=== Bubble-Maschinen (statisch, handle=0x{MachineHandle:X8}) ===");
        int count = 0;
        foreach (var node in EngineObjectList.Walk(mem, 0x40))
        {
            if (node.Handle != MachineHandle || node.Bytes.Length < 0x40) continue;
            count++;
            ushort hdr = BitConverter.ToUInt16(node.Bytes, 0x1A);
            int dirCode = BitConverter.ToInt16(node.Bytes, 0x30);
            int mx = BitConverter.ToInt16(node.Bytes, 0x32);
            int my = BitConverter.ToInt16(node.Bytes, 0x34);
            // dirCode-Mapping für Maschinen verifiziert 2026-05-03:
            // 1=Left, 2=Down, 3=Right, 4=Up (= F-Bit-Konvention)
            string dirArrow = dirCode switch
            {
                1 => "←Left", 2 => "↓Down", 3 => "→Right", 4 => "↑Up", _ => $"?{dirCode}"
            };
            sw.WriteLine($"  Maschine hdr=0x{hdr:X4}  dir={dirArrow}  pixel=({mx},{my})");
        }
        sw.WriteLine($"  Total: {count} Maschinen.");

        // Geometrische Zuordnung (Pixel→Cell + Zelltyp→Insel) — der NEUE, dynamische
        // Erkennungs-Pfad. Zeigt pro Maschine die abgeleitete Zelle + ob Insel.
        var bubbles = Bubblewonder.BubbleObjectScanner.Scan(mem);
        var placements = Simulator.BubblewonderGridModelBuilder.DetectMachines(mem, bubbles);
        sw.WriteLine("  --- Maschinen-Erkennung (Sprite-Standort Pixel→Cell; Insel aus TargetIdx +0x8a) ---");
        sw.WriteLine("    HINWEIS: Cell = Sprite-STANDORT, NICHT die ZB-Spawn-Zelle (= Laufzeit-Verkettung).");
        if (placements.Count == 0)
            sw.WriteLine("    (keine Maschinen erkannt)");
        foreach (var p in placements)
        {
            string cellStr = p.CellPos >= 0 ? $"({p.CellPos / 13},{p.CellPos % 13})" : "(?? kein Mapping)";
            sw.WriteLine($"    hdr=0x{p.Hdr1A:X4} pixel=({p.Px},{p.Py}) → Standort {cellStr} " +
                         $"dir={p.Direction} TargetIdx={p.TargetIdx} {(p.IsIsland ? "🏝 INSEL" : "→ wirft")}");
        }
        // Roh-Felder pro Maschine zur Verifikation des Positions-Felds (+0x72/74).
        sw.WriteLine("  --- Maschinen-Rohfelder (+0x30 dir, +0x32/34 pixel, +0x72/74 grid) ---");
        foreach (var node in EngineObjectList.Walk(mem, 0x80))
        {
            if (node.Handle != MachineHandle || node.Bytes.Length < 0x76) continue;
            ushort hdr = BitConverter.ToUInt16(node.Bytes, 0x1A);
            int g72 = BitConverter.ToUInt16(node.Bytes, 0x72);
            int g74 = BitConverter.ToUInt16(node.Bytes, 0x74);
            sw.WriteLine($"    hdr=0x{hdr:X4} +0x72={g72} +0x74={g74}  " +
                         $"(falls Grid: ({g72},{g74}))");
        }
    }
}
