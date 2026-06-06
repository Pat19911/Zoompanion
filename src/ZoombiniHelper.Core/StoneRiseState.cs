namespace ZoombiniHelper;

/// <summary>
/// Snapshot of Stone Rise state. Reads the engine's tile array, extracts
/// the active pair-slots and connectors, and exposes the shape of the
/// puzzle as a graph of pair-slots connected by attribute-typed
/// connectors.
///
/// <para>The engine stores 117 tile records of 18 bytes each at
/// <see cref="StoneRiseMemoryMap.TilesBase"/>. We care about three types:</para>
/// <list type="bullet">
///   <item><b>506</b> — empty pair-slot (can hold one zoombini).</item>
///   <item><b>508</b> — filled pair-slot.</item>
///   <item><b>501/502</b> — empty/filled connector with level (510=Hair,
///   511=Eyes, 512=Nose, 513=Feet) — verified by disassembly of the
///   match function at 0x0043B2B6..0x0043B321.</item>
/// </list>
///
/// <para>Two adjacent pair-slots that share a connector form a "rule":
/// to fill both slots, the two zoombinis placed there must share the
/// connector's required attribute.</para>
/// </summary>
public sealed class StoneRiseState
{
    /// <summary><see cref="PlacedZbId"/> is the engine's per-zb identity word
    /// (the value at offset +0x1A in the zb record). It's written into tile.w1
    /// when a zb is placed (verified at 0x0043A36C..0x0043A37D). For empty
    /// slots it's 0; for filled slots it lets us look up the exact zb that
    /// occupies the slot, even if the helper attached mid-puzzle.</summary>
    public readonly record struct PairSlot(int TileIndex, bool IsFilled, ushort PlacedZbId = 0);

    public readonly record struct Connector(
        int TileIndex,
        bool IsFilled,
        byte AttributeId,           // 1=Hair, 2=Eyes, 3=Nose, 4=Feet
        int PairTileA,              // adjacent pair-slot tile index
        int PairTileB);             // adjacent pair-slot tile index

    public int Difficulty { get; }
    public IReadOnlyList<PairSlot> PairSlots { get; }
    public IReadOnlyList<Connector> Connectors { get; }

    /// <summary>Per-tile static layout positions from <c>.rdata</c>. Kept
    /// as a fallback / for connector tile positioning. Indexed by tile
    /// index 0..116.</summary>
    public IReadOnlyList<(int X, int Y)> TilePositions { get; }

    /// <summary>Per-active-slot (x, y) in engine coords — what the engine
    /// actually uses for hit-testing. Indexed by active-slot order, NOT
    /// tile_idx. Length = number of active slots. Use
    /// <see cref="ActiveSlotToTileIndex"/> at the same index to map back.</summary>
    public IReadOnlyList<(int X, int Y)> ActiveSlotEnginePositions { get; }

    /// <summary>Per-active-slot tile index (= which entry in the 117-tile
    /// array this active slot refers to). Same length as
    /// <see cref="ActiveSlotEnginePositions"/>; entry i is the tile_idx
    /// that lives at engine position [i]. The engine does not order this
    /// table by tile-index — for Diff 3 it groups slots by visual column.</summary>
    public IReadOnlyList<int> ActiveSlotToTileIndex { get; }

    /// <summary>Live cursor X, Y in engine coords. Both 0 if not actively
    /// dragging. Same coord space as <see cref="ActiveSlotEnginePositions"/>.</summary>
    public int CursorX { get; }
    public int CursorY { get; }

    /// <summary>1-based active-slot index under cursor (1..N), or 0 if
    /// not over any slot. Already in 1-based form per engine's wrapper.</summary>
    public int CursorActiveSlot { get; }

    public bool IsActive => PairSlots.Count > 0;

    internal StoneRiseState(int difficulty,
                           IReadOnlyList<PairSlot> slots,
                           IReadOnlyList<Connector> connectors,
                           IReadOnlyList<(int X, int Y)>? tilePositions = null,
                           IReadOnlyList<(int X, int Y)>? activeSlotPositions = null,
                           IReadOnlyList<int>? activeSlotToTileIndex = null,
                           int cursorX = 0, int cursorY = 0, int cursorActiveSlot = 0)
    {
        Difficulty = difficulty;
        PairSlots = slots;
        Connectors = connectors;
        TilePositions = tilePositions ?? Array.Empty<(int, int)>();
        ActiveSlotEnginePositions = activeSlotPositions ?? Array.Empty<(int, int)>();
        ActiveSlotToTileIndex = activeSlotToTileIndex ?? Array.Empty<int>();
        CursorX = cursorX;
        CursorY = cursorY;
        CursorActiveSlot = cursorActiveSlot;
    }

    public static StoneRiseState Read(IMemoryReader mem)
    {
        int diff = mem.ReadWord(StoneRiseMemoryMap.Difficulty);
        var bytes = mem.ReadBytes(StoneRiseMemoryMap.TilesBase,
                                  StoneRiseMemoryMap.TileCount * StoneRiseMemoryMap.TileStride);
        if (bytes is null)
            return new StoneRiseState(diff + 1, Array.Empty<PairSlot>(), Array.Empty<Connector>());

        var slots = new List<PairSlot>();
        var connectors = new List<Connector>();
        for (int i = 0; i < StoneRiseMemoryMap.TileCount; i++)
        {
            ushort type = ReadW(bytes, i, 0);
            // 506 = empty pair-slot
            // 507 = filled, no connector validated yet
            // 508 = filled AND at least one connector validated
            //
            // Both 507 and 508 mean a zoombini is in the slot. The engine
            // lumps them together at 0x0043A3B8 (cmp cx, 0x1fb / je / cmp
            // cx, 0x1fc / jne) for the "slot contains a placed zb" code
            // path. Verified live in memdump-134400: 6 tiles are type=507
            // with w1 values that match the hdr1A of placed zbs.
            if (type == 506) slots.Add(new PairSlot(i, IsFilled: false));
            else if (type == 507 || type == 508)
                slots.Add(new PairSlot(i, IsFilled: true, PlacedZbId: ReadW(bytes, i, 1)));
            else if (type == 501 || type == 502)
            {
                var (a, b) = FindAdjacentPairsTransitive(bytes, i);
                if (a < 0 || b < 0) continue;
                // attr=0 → structural bridge (no rule); we keep it for the
                // visual layout but the solver will skip it.
                byte attr = LevelToAttribute(ReadW(bytes, i, 1));
                connectors.Add(new Connector(i, IsFilled: type == 502,
                                             AttributeId: attr,
                                             PairTileA: a, PairTileB: b));
            }
        }
        // Dedupe connectors by their {slotA, slotB} endpoint pair: a single
        // visual bridge made of 3 chained connector tiles produces 3 entries
        // here, all with identical endpoints. Keep one per pair, preferring
        // the attribute-bearing entry over an attr-less structural bridge
        // when the chain happens to mix both.
        connectors = DedupeConnectors(connectors);

        var activePos = ReadActiveSlotPositions(mem);
        var activeMap = ReadActiveSlotToTileIndex(mem, activePos.Count);
        return new StoneRiseState(
            diff + 1, slots, connectors,
            tilePositions: ReadPositions(mem, diff),
            activeSlotPositions: activePos,
            activeSlotToTileIndex: activeMap,
            cursorX: mem.ReadWord(StoneRiseMemoryMap.CursorX),
            cursorY: mem.ReadWord(StoneRiseMemoryMap.CursorY),
            cursorActiveSlot: ReadCursorActiveSlot(mem));
    }

    /// <summary>Read the active-slot → tile-index lookup table. The engine
    /// uses this whenever it needs to map a per-active-slot loop counter
    /// (e.g. from hit-testing or position lookup) back to the 117-entry
    /// tile array. Crucial for layout: the engine does NOT order this by
    /// tile-index ascending — Diff 3 groups slots by visual column.</summary>
    private static IReadOnlyList<int> ReadActiveSlotToTileIndex(IMemoryReader mem, int count)
    {
        if (count <= 0 || count > 64) return Array.Empty<int>();
        var bytes = mem.ReadBytes(StoneRiseMemoryMap.ActiveSlotToTileIndex, count * 2);
        if (bytes is null) return Array.Empty<int>();
        var result = new int[count];
        for (int i = 0; i < count; i++)
            result[i] = BitConverter.ToUInt16(bytes, i * 2);
        return result;
    }

    /// <summary>Read the engine's per-active-slot position table. Each entry
    /// is 4 bytes (x_word, y_word). Length comes from <see cref="StoneRiseMemoryMap.ActiveSlotCount"/>.</summary>
    private static IReadOnlyList<(int X, int Y)> ReadActiveSlotPositions(IMemoryReader mem)
    {
        int count = mem.ReadWord(StoneRiseMemoryMap.ActiveSlotCount);
        if (count <= 0 || count > 64) return Array.Empty<(int, int)>();
        var bytes = mem.ReadBytes(StoneRiseMemoryMap.ActiveSlotPositions, count * 4);
        if (bytes is null) return Array.Empty<(int, int)>();
        var result = new (int X, int Y)[count];
        for (int i = 0; i < count; i++)
        {
            int x = BitConverter.ToUInt16(bytes, i * 4);
            int y = BitConverter.ToUInt16(bytes, i * 4 + 2);
            result[i] = (x, y);
        }
        return result;
    }

    /// <summary>Mirror the engine's wrapper at 0x00448E60: returns the 1-based
    /// active-slot index under the cursor, or 0 if not over any slot.</summary>
    private static int ReadCursorActiveSlot(IMemoryReader mem)
    {
        if (mem.ReadWord(StoneRiseMemoryMap.CursorActiveSlotValid) == 0) return 0;
        return mem.ReadWord(StoneRiseMemoryMap.CursorActiveSlotIndex) + 1;
    }

    private static IReadOnlyList<(int X, int Y)> ReadPositions(IMemoryReader mem, int rawDiff)
    {
        nint table = rawDiff == 3 ? StoneRiseMemoryMap.TileScreenCoordsHard
                                  : StoneRiseMemoryMap.TileScreenCoordsEasy;
        var bytes = mem.ReadBytes(table, StoneRiseMemoryMap.TileCount * 4);
        if (bytes is null) return Array.Empty<(int, int)>();
        var pos = new (int X, int Y)[StoneRiseMemoryMap.TileCount];
        for (int i = 0; i < StoneRiseMemoryMap.TileCount; i++)
        {
            int x = BitConverter.ToUInt16(bytes, i * 4);
            int y = BitConverter.ToUInt16(bytes, i * 4 + 2);
            pos[i] = (x, y);
        }
        return pos;
    }

    /// <summary>Find the two pair-slots a connector tile bridges, walking
    /// through chains of intermediate connector tiles when present. Some
    /// puzzles use multi-tile spans (e.g. slot → conn → conn → conn → slot)
    /// to connect distant slots visually; treating each tile as if it had
    /// to directly touch a slot would lose those entirely.
    /// Returns (-1, -1) if the connector doesn't bridge exactly two slots
    /// — single-ended dead-ends or splits with three+ slots are skipped
    /// rather than guessed.</summary>
    private static (int a, int b) FindAdjacentPairsTransitive(byte[] tiles, int connectorIdx)
    {
        var slots = new List<int>();
        var visited = new HashSet<int> { connectorIdx };
        var stack = new Stack<int>();
        stack.Push(connectorIdx);
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            for (int w = 2; w <= 7; w++)
            {
                ushort neighbor = ReadW(tiles, cur, w);
                if (neighbor == 0xFFFF || neighbor >= StoneRiseMemoryMap.TileCount) continue;
                if (!visited.Add(neighbor)) continue;
                ushort neighborType = ReadW(tiles, neighbor, 0);
                if (neighborType is 506 or 507 or 508)
                {
                    if (!slots.Contains(neighbor)) slots.Add(neighbor);
                    if (slots.Count > 2) return (-1, -1);
                }
                else if (neighborType is 501 or 502)
                {
                    stack.Push(neighbor);
                }
                // anything else (stairs 504/505 etc.) terminates this branch
            }
        }
        if (slots.Count != 2) return (-1, -1);
        return (slots[0], slots[1]);
    }

    private static List<Connector> DedupeConnectors(List<Connector> raw)
    {
        var byPair = new Dictionary<(int, int), Connector>(raw.Count);
        foreach (var c in raw)
        {
            int lo = Math.Min(c.PairTileA, c.PairTileB);
            int hi = Math.Max(c.PairTileA, c.PairTileB);
            var key = (lo, hi);
            if (!byPair.TryGetValue(key, out var existing))
                byPair[key] = c;
            else if (existing.AttributeId == 0 && c.AttributeId != 0)
                byPair[key] = c;
        }
        return byPair.Values.ToList();
    }

    private static byte LevelToAttribute(ushort level) => level switch
    {
        510 => 1, // Hair
        511 => 2, // Eyes
        512 => 3, // Nose
        513 => 4, // Feet
        _ => 0,
    };

    private static ushort ReadW(byte[] tiles, int tileIdx, int wordIdx)
    {
        int off = tileIdx * StoneRiseMemoryMap.TileStride + wordIdx * 2;
        return (off + 1 < tiles.Length) ? BitConverter.ToUInt16(tiles, off) : (ushort)0;
    }
}
