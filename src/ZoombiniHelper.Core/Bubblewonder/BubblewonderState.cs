namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Live-Snapshot des Bubblewonder-Zustands. Pro Tick aus Memory frisch gelesen.
/// Enthält das statische Layout (Mechanismen, Verkettungen) plus den
/// dynamischen Spielstand (Counter pro Position, gespeicherte Handles,
/// gepoppte/gescorde/gematchte Listen).
///
/// <para>Pattern-konform zu CavesState/CaptainCajunState: <see cref="Read"/>
/// als static factory, <see cref="IsActive"/> als Spielzustands-Test.</para>
/// </summary>
public sealed class BubblewonderState
{
    /// <summary>1-basierte Difficulty (1..4 normal, 5 = Bonus).</summary>
    public int Difficulty { get; }

    /// <summary>Diff-Cache aus 0x49A3E2 (Argument an Master-Orchestrator).</summary>
    public int DifficultyCache { get; }

    /// <summary>Welche REGS-Variant geladen ist (= Variation-Counter).</summary>
    public int Variant { get; }

    /// <summary>Resource-ID der aktuell geladenen REGS (16600..16609).</summary>
    public int RegsResourceId { get; }

    /// <summary>Statisches Grid-Modell der Runde.</summary>
    public BubblewonderGrid Grid { get; }

    /// <summary>Verkettungs-Topologie (24 Action-Slots → linked Engine-Objects).</summary>
    public IReadOnlyList<MechanismConnection> Connections { get; }

    /// <summary>Live-Counter pro Position (= wie oft die Position aktiviert wurde).
    /// Index = (prop1*13 + prop2). Wert = aktueller Counter aus 0x49ACA0.</summary>
    public IReadOnlyDictionary<int, ushort> PositionCounters { get; }

    /// <summary>Live-Handles pro Position (= aktuell gespeichertes Bubble-Handle
    /// aus 0x49ACA2). Nur Einträge mit non-null Handle.</summary>
    public IReadOnlyDictionary<int, ushort> PositionHandles { get; }

    /// <summary>Anzahl Bubbles die bereits "popped" wurden (aus 0x49A414).</summary>
    public int PoppedCount { get; }

    /// <summary>Anzahl Bubbles die "scored" wurden (aus 0x49A206).</summary>
    public int ScoredCount { get; }

    /// <summary>Anzahl Bubbles die in matched-Pair-Liste sind (aus 0x49AC76 / 2).</summary>
    public int MatchedPairCount { get; }

    /// <summary>Live-Bubble-Engine-Objects aus der globalen linked-list.
    /// Enthält für jeden aktiven Mechanismus auf dem Grid: Position (prop1/prop2),
    /// State, REGS-Record-Copy. <see cref="BubbleObjectScanner"/>.</summary>
    public IReadOnlyList<BubbleObject> LiveBubbles { get; }

    /// <summary>Live-State pro ActionSlot 0..23 (= Schalter-Zustand).
    /// <c>[0x49ABB8 + slot*2]</c>. Speichert je nach Schalter-Typ:
    /// Toggle-Wert (1↔2), Counter, oder hdr1A des letzten ZB der durchlief.</summary>
    public IReadOnlyList<ushort> ActionSlotState { get; }

    /// <summary>Aktueller "im Pfad befindlicher" ZB-Handle (PoppedHandlesList[0]).
    /// = 0 wenn kein ZB gerade läuft.</summary>
    public ushort CurrentZbInTransit { get; }

    /// <summary>True wenn die Runde aktiv ist (Difficulty gesetzt + REGS geladen).</summary>
    public bool IsActive => Difficulty > 0 && Grid.Mechanisms.Count > 0;

    private BubblewonderState(
        int difficulty, int diffCache, int variant, int regsResourceId,
        BubblewonderGrid grid, IReadOnlyList<MechanismConnection> connections,
        IReadOnlyDictionary<int, ushort> positionCounters,
        IReadOnlyDictionary<int, ushort> positionHandles,
        int popped, int scored, int matchedPairs,
        IReadOnlyList<BubbleObject> liveBubbles,
        IReadOnlyList<ushort> actionSlotState,
        ushort currentZbInTransit)
    {
        Difficulty = difficulty;
        DifficultyCache = diffCache;
        Variant = variant;
        RegsResourceId = regsResourceId;
        Grid = grid;
        Connections = connections;
        PositionCounters = positionCounters;
        PositionHandles = positionHandles;
        PoppedCount = popped;
        ScoredCount = scored;
        MatchedPairCount = matchedPairs;
        LiveBubbles = liveBubbles;
        ActionSlotState = actionSlotState;
        CurrentZbInTransit = currentZbInTransit;
    }

    /// <summary>Liest den kompletten Bubblewonder-Zustand aus dem laufenden
    /// Spielprozess. Gibt einen "leeren" State zurück wenn das Puzzle nicht
    /// initialisiert ist (Heap-Pointer null oder Difficulty == 0).</summary>
    public static BubblewonderState Read(IMemoryReader mem)
    {
        int diff = mem.ReadWord(BubblewonderMemoryMap.UserDifficulty);
        int diffCache = mem.ReadWord(BubblewonderMemoryMap.DifficultyCache);
        int variant = ReadVariationCounter(mem, diff);
        var emptyGrid = new BubblewonderGrid(
            Difficulty: diff, DifficultyVariant: variant,
            RegsResourceId: 0,
            Mechanisms: Array.Empty<Mechanism>(),
            GridDimensions: (13, 5));

        if (diff <= 0)
            return Empty();

        int resourceId = ResolveResourceId(diff, variant);
        var (header, records) = TryReadRegsFromHeap(mem);
        if (records.Count == 0)
            return Empty(diff, diffCache, variant, resourceId);

        var mechanisms = BuildMechanisms(records);
        var grid = new BubblewonderGrid(
            Difficulty: diff, DifficultyVariant: variant,
            RegsResourceId: resourceId, Mechanisms: mechanisms,
            GridDimensions: (13, 5));

        var connections = ConnectionBuilder.BuildFromMemory(mem);
        var (counters, handles) = ReadPositionTables(mem);
        int popped = mem.ReadWord(BubblewonderMemoryMap.PoppedHandlesCount);
        int scored = mem.ReadWord(BubblewonderMemoryMap.ScoredHandlesCount);
        int matched = mem.ReadWord(BubblewonderMemoryMap.MatchedHandlesCount) / 2;
        var liveBubbles = BubbleObjectScanner.Scan(mem);
        var actionSlotState = ReadActionSlotState(mem);
        ushort currentZb = mem.ReadWord(BubblewonderMemoryMap.PoppedHandlesList);

        return new BubblewonderState(diff, diffCache, variant, resourceId,
            grid, connections, counters, handles, popped, scored, matched,
            liveBubbles, actionSlotState, currentZb);
    }

    private static BubblewonderState Empty(int diff = 0, int diffCache = 0, int variant = 0, int resourceId = 0)
    {
        var emptyGrid = new BubblewonderGrid(
            Difficulty: diff, DifficultyVariant: variant,
            RegsResourceId: resourceId,
            Mechanisms: Array.Empty<Mechanism>(),
            GridDimensions: (13, 5));
        return new BubblewonderState(diff, diffCache, variant, resourceId, emptyGrid,
            Array.Empty<MechanismConnection>(),
            new Dictionary<int, ushort>(), new Dictionary<int, ushort>(),
            0, 0, 0,
            Array.Empty<BubbleObject>(),
            Array.Empty<ushort>(),
            currentZbInTransit: 0);
    }

    private static IReadOnlyList<ushort> ReadActionSlotState(IMemoryReader mem)
    {
        var bytes = mem.ReadBytes(BubblewonderMemoryMap.ActionSlotHandlesPrimary,
                                   ActionSlotTables.SlotCount * 2);
        if (bytes is null) return Array.Empty<ushort>();
        var result = new ushort[ActionSlotTables.SlotCount];
        for (int i = 0; i < ActionSlotTables.SlotCount; i++)
            result[i] = BitConverter.ToUInt16(bytes, i * 2);
        return result;
    }

    private static int ReadVariationCounter(IMemoryReader mem, int diff) => diff switch
    {
        1 => mem.ReadWord(BubblewonderMemoryMap.VariationCounterDiff1),
        2 => mem.ReadWord(BubblewonderMemoryMap.VariationCounterDiff2),
        3 => mem.ReadWord(BubblewonderMemoryMap.VariationCounterDiff3),
        4 => mem.ReadWord(BubblewonderMemoryMap.VariationCounterDiff4),
        _ => 0,
    };

    private static int ResolveResourceId(int diff, int variant)
    {
        if (!BubblewonderRegsResources.ByDifficulty.TryGetValue(diff, out var variants))
            return 0;
        // Variant might be larger than available count (= reset state); clamp.
        int idx = variant >= 0 && variant < variants.Count ? variant : 0;
        return variants[idx];
    }

    private static (RegsHeader Header, IReadOnlyList<RegsRecord> Records) TryReadRegsFromHeap(IMemoryReader mem)
    {
        var ptrBytes = mem.ReadBytes(BubblewonderMemoryMap.RegsHeapPointer, 4);
        if (ptrBytes is null) return (default, Array.Empty<RegsRecord>());
        uint heapVa = BitConverter.ToUInt32(ptrBytes, 0);
        if (heapVa < 0x10000) return (default, Array.Empty<RegsRecord>());  // null/invalid

        var headerBytes = mem.ReadBytes((nint)heapVa, RegsHeader.SizeInBytes);
        if (headerBytes is null) return (default, Array.Empty<RegsRecord>());
        // Live-Memory ist Little-Endian — Engine konvertiert beim Loading aus BE.
        var header = RegsHeader.FromBytesLittleEndian(headerBytes);
        if (header.Count == 0 || header.Count > 200) return (header, Array.Empty<RegsRecord>());

        int totalSize = RegsHeader.SizeInBytes + header.Count * RegsRecord.SizeInBytes;
        var allBytes = mem.ReadBytes((nint)heapVa, totalSize);
        if (allBytes is null) return (header, Array.Empty<RegsRecord>());

        var (_, records) = RegsReader.ParseLittleEndian(allBytes);
        return (header, records);
    }

    private static IReadOnlyList<Mechanism> BuildMechanisms(IReadOnlyList<RegsRecord> records)
    {
        var mechanisms = new List<Mechanism>(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            var rec = records[i];
            var type = MechanismClassifier.Classify(rec);
            // Use REGS f1, f2 as Position. May not match Engine prop1/prop2 1:1
            // — that mapping is engine-transformed (FUN_0040A980) and remains a
            // Loose End (Task 7).
            var pos = new GridPosition((byte)(rec.F1 & 0xff), (byte)(rec.F2 & 0xff));
            mechanisms.Add(new Mechanism(
                SlotId: i,
                Type: type,
                Position: pos,
                ConditionalAttribute: rec.ConditionalAttribute,
                ConditionalValue: null,  // engine-versteckt
                LinkedSlotIds: Array.Empty<int>(),  // wird via Connections separat geliefert
                RawFields: rec.AsList(),
                Direction: rec.Direction));
        }
        return mechanisms;
    }

    /// <summary>Liest die Position-Counter + Handle-Tabelle bei 0x49ACA0/0x49ACA2.
    /// Index = (prop1*13 + prop2). Stride 6 Bytes (Counter @ +0, Handle @ +2).</summary>
    private static (IReadOnlyDictionary<int, ushort>, IReadOnlyDictionary<int, ushort>) ReadPositionTables(IMemoryReader mem)
    {
        // Volles Grid: bis zu 12 Reihen × 13 Spalten = 156 Positionen × 6 Bytes Stride.
        // Mechanismen mit pos=(11,*) existieren in REGS — Limit auf 65 (= 5 Reihen)
        // hat Pfade ab Position 65 abgeschnitten (verifiziert via Diff 1 Dump 2026-05-01).
        const int maxIndex = 156;
        const int stride = 6;
        var counters = new Dictionary<int, ushort>();
        var handles = new Dictionary<int, ushort>();
        var bytes = mem.ReadBytes(BubblewonderMemoryMap.PositionCounterTable, maxIndex * stride);
        if (bytes is null) return (counters, handles);
        for (int i = 0; i < maxIndex; i++)
        {
            int off = i * stride;
            ushort cnt = BitConverter.ToUInt16(bytes, off);
            ushort handle = BitConverter.ToUInt16(bytes, off + 2);
            if (cnt != 0) counters[i] = cnt;
            if (handle != 0) handles[i] = handle;
        }
        return (counters, handles);
    }
}
