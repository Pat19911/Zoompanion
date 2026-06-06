using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Bubblewonder.Simulator;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.Diagnostics;

/// <summary>
/// Writes the F12 memory-dump file. Pure I/O over <see cref="IMemoryReader"/>
/// — no UI, no Win32. Each section is a method so a future "live diagnostic
/// panel" can reuse the building blocks without rewriting them.
/// </summary>
public static class MemoryDumpFile
{
    public static void Write(StreamWriter sw, IMemoryReader mem, PuzzleManager mgr,
                             string processName, int processId, nint moduleBase,
                             string? helperTitle = null, string? helperBody = null,
                             DragHistory? history = null,
                             BubblewonderTracker? bubbleTracker = null)
    {
        sw.WriteLine($"# Memory dump — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sw.WriteLine($"# Process: {processName} (PID {processId})");
        sw.WriteLine($"# Module-Base: 0x{moduleBase:X8}");
        sw.WriteLine();

        if (helperTitle is not null || helperBody is not null)
        {
            sw.WriteLine("=== Helper-UI (was the user sees right now) ===");
            sw.WriteLine($"  TITLE: {helperTitle}");
            sw.WriteLine("  BODY:");
            foreach (var line in (helperBody ?? "").Split('\n'))
                sw.WriteLine($"    {line.TrimEnd('\r')}");
            sw.WriteLine();
        }

        if (history is not null)
        {
            sw.WriteLine("=== Recent pickup history (most recent first) ===");
            int idx = 0;
            foreach (var e in history.Recent)
            {
                sw.WriteLine($"  [{idx++,2}] {e.At:HH:mm:ss}  {e.PuzzleId,-18}  attrs=({e.Hair},{e.Eyes},{e.Nose},{e.Feet})  pool={e.PoolCount,2}  → cave={(e.RecommendedCave?.ToString() ?? "—")}");
            }
            if (idx == 0) sw.WriteLine("  (no pickups recorded yet)");
            sw.WriteLine();
        }

        WritePuzzleDetection(sw, mem, mgr);
        WriteCliffSnapshot(sw, mem);
        WriteCavesSnapshot(sw, mem);
        WriteStoneRiseSnapshot(sw, mem);
        WriteCaptainCajunSnapshot(sw, mem);
        WriteBubblewonderSnapshot(sw, mem);
        if (bubbleTracker is not null)
        {
            sw.WriteLine();
            bubbleTracker.WriteHardcodedSpawnMapping(sw, mem);
            sw.WriteLine();
            bubbleTracker.WriteBubbleMachines(sw, mem);
            sw.WriteLine();
            bubbleTracker.WriteSwitchLiveStates(sw, mem);
            sw.WriteLine();
            bubbleTracker.WriteTriggerStaticCrossRef(sw, mem);
            sw.WriteLine();
            bubbleTracker.WriteStickyCells(sw, mem);
            sw.WriteLine();
            bubbleTracker.WriteSpawnPositions(sw);
            sw.WriteLine();
            bubbleTracker.WriteSolverPlan(sw, mem);
            sw.WriteLine();
            bubbleTracker.WriteOutcomeEndpoints(sw);
            sw.WriteLine();
            bubbleTracker.WriteZbOutcomeFields(sw, mem);
            sw.WriteLine();
            bubbleTracker.WriteHawkStatus(sw);
            sw.WriteLine();
            bubbleTracker.WriteRoutingObservations(sw, mem);
            sw.WriteLine();
            bubbleTracker.WriteTimeline(sw);
            sw.WriteLine();
            bubbleTracker.WriteObservedEdges(sw);
            sw.WriteLine();
        }
        WriteDragState(sw, mem);
        WritePoolSummary(sw, mem);
        WriteLinkedListWalk(sw, mem);
        sw.Write(DataSectionDump.Run(mem));
    }

    /// <summary>Bubblewonder Abyss live-snapshot — Grid mit allen Mechanismen,
    /// Verkettungen, Schalter-Zuständen und Bubble-Engine-Objects. Zeigt
    /// das vollständige Modell das der Helper aufbaut.</summary>
    private static void WriteBubblewonderSnapshot(StreamWriter sw, IMemoryReader mem)
    {
        var s = BubblewonderState.Read(mem);
        sw.WriteLine("=== Bubblewonder snapshot ===");
        sw.WriteLine($"  difficulty={s.Difficulty}  variant={s.Variant}  diff_cache={s.DifficultyCache}");

        // Diagnose: what does the heap-pointer dereference yield?
        var heapPtrBytes = mem.ReadBytes(BubblewonderMemoryMap.RegsHeapPointer, 4);
        uint heapVa = heapPtrBytes != null ? BitConverter.ToUInt32(heapPtrBytes, 0) : 0;
        sw.WriteLine($"  RegsHeapPointer [0x{BubblewonderMemoryMap.RegsHeapPointer:X8}] = 0x{heapVa:X8}");
        if (heapVa >= 0x10000)
        {
            var headerProbe = mem.ReadBytes((nint)heapVa, 20);
            if (headerProbe != null)
            {
                sw.WriteLine($"  Header bytes at 0x{heapVa:X8}: {BitConverter.ToString(headerProbe).Replace("-", " ")}");
                ushort countBE = (ushort)((headerProbe[0] << 8) | headerProbe[1]);
                ushort countLE = BitConverter.ToUInt16(headerProbe, 0);
                sw.WriteLine($"  Header[0] interpreted: BE={countBE}  LE={countLE}");
            }
            else
            {
                sw.WriteLine($"  Header bytes at 0x{heapVa:X8}: READ FAILED (null)");
            }
        }

        if (!s.IsActive)
        {
            sw.WriteLine("  (not active — no REGS loaded)");
            sw.WriteLine();
            return;
        }
        sw.WriteLine($"  REGS resource id = 0x{s.RegsResourceId:X4} ({s.RegsResourceId})");
        sw.WriteLine($"  Mechanisms: {s.Grid.Count}  (type histogram below)");
        foreach (var (type, count) in s.Grid.TypeHistogram())
            sw.WriteLine($"    {type,-18} × {count}");
        sw.WriteLine($"  Counters: popped={s.PoppedCount}  scored={s.ScoredCount}  matched-pairs={s.MatchedPairCount}");
        sw.WriteLine($"  Current ZB in transit: hdr1A=0x{s.CurrentZbInTransit:X4}");

        sw.WriteLine();
        sw.WriteLine("  --- Mechanisms (statisch aus REGS) ---");
        for (int i = 0; i < s.Grid.Mechanisms.Count; i++)
        {
            var m = s.Grid.Mechanisms[i];
            string condStr = m.IsConditional
                ? $" attr={m.ConditionalAttribute}"
                : "";
            sw.WriteLine($"  [{i,2}] {m.Type,-16} pos=({m.Position.Prop1,2},{m.Position.Prop2,2}){condStr}  raw={string.Join(",", m.RawFields)}");
        }

        // Offline-Trace: baut das LIVE-Grid (mit Conditional attr/variant aus
        // +0x82/+0x84, Goal-Zellen aus der Zelltyp-Tabelle) und simuliert jeden
        // Pool-ZB von jeder Haupt-Maschine. Zeigt direkt WO ein ZB stirbt —
        // genau das, was zum Debuggen des „0/16"-Routing-Bugs (16608) fehlt.
        try { WriteBubblewonderSimTrace(sw, mem); }
        catch (Exception ex) { sw.WriteLine($"  [Sim-Trace fehlgeschlagen: {ex.Message}]"); }

        sw.WriteLine();
        sw.WriteLine("  --- ActionSlot states (live aus 0x49ABB8) ---");
        for (int i = 0; i < s.ActionSlotState.Count; i++)
        {
            ushort v = s.ActionSlotState[i];
            string interp = v == 0 ? "" : v < 0x10 ? "  (small=toggle/counter)" : "  (zb-handle)";
            sw.WriteLine($"  Slot[{i,2}] = 0x{v:X4}{interp}");
        }

        sw.WriteLine();
        sw.WriteLine("  --- Live Bubble Engine-Objects (handle=0x04188000) ---");
        sw.WriteLine($"  count: {s.LiveBubbles.Count}");
        sw.WriteLine("  Format: hdr1A pos state act/sec event filter REGS-copy");
        sw.WriteLine("    event=0x14/1E/28/32 = PairMatch-Channels (vermutlich Hair/Eyes/Nose/Feet)");
        sw.WriteLine("    event=0x15/1F/29/33/3D = Passthrough/Score");
        sw.WriteLine("    filter=(hair,eyes,nose,feet), je 0=irrelevant oder 1..5=Variant");
        foreach (var b in s.LiveBubbles)
        {
            string famTag = b.EventFamily switch
            {
                BubbleEventFamily.PairMatchFilter => $"PMatch ch{b.FilterChannelIndex}",
                BubbleEventFamily.Passthrough => "Passthr",
                _ => "idle",
            };
            sw.WriteLine(
                $"  hdr1A=0x{b.HeaderId:X4} pos=({b.Prop1,2},{b.Prop2,2}) " +
                $"state={b.State} act/sec={b.ActiveFlag}/{b.SecondaryActiveFlag} " +
                $"event=0x{b.EventType:X2}({famTag,-10}) filter={b.FilterConfig} " +
                $"linked→0x{b.LinkedHandle:X4}  REGS=[{string.Join(",", b.RegsRecordCopy)}]");
        }

        // === ASCII-Grid: visuelle Karte zum Abgleich mit Spielbildschirm ===
        sw.WriteLine();
        WriteAsciiGrid(sw, mem, s);

        // === Switch-Bitmap (live, aus Aggregator) ===
        sw.WriteLine();
        sw.WriteLine("  --- Switch-Bitmap (live aus *[0x4A2818]+0x52..0x54) ---");
        var switchBm = SwitchBitmap.Read(mem);
        if (switchBm is null)
            sw.WriteLine("    (Aggregator nicht initialisiert oder Pointer null)");
        else
        {
            sw.WriteLine($"    {switchBm}");
            for (int i = 0; i < 8; i++)
                if (switchBm.ChannelABit(i)) sw.WriteLine($"      Channel A bit {i} aktiviert");
            for (int i = 0; i < 8; i++)
                if (switchBm.ChannelBBit(i)) sw.WriteLine($"      Channel B bit {i} aktiviert");
            for (int i = 0; i < 16; i++)
                if (switchBm.ChannelCBit(i)) sw.WriteLine($"      Channel C bit {i} aktiviert");
        }

        // === Path Graph (statisch aus Connections + linked_handle) ===
        sw.WriteLine();
        sw.WriteLine("  --- Path Graph (statisch — Edges aus ConnectionTriples + linked_handle) ---");
        var graph = PathGraphBuilder.BuildStatic(s);
        sw.WriteLine($"  Edges: {graph.Edges.Count}");
        foreach (var edge in graph.Edges)
            sw.WriteLine($"    {edge}");
        var sources = graph.Sources(s.Grid.Mechanisms.Count).ToList();
        var sinks = graph.Sinks(s.Grid.Mechanisms.Count).ToList();
        sw.WriteLine($"  Source-Mechanismen (kein incoming): {string.Join(",", sources)}");
        sw.WriteLine($"  Sink-Mechanismen (kein outgoing):    {string.Join(",", sinks)}");

        // Diagnose: kompletter Hex-Dump aktiver ZB-Engine-Objects
        // (= ZBs mit handle in transit, vermutlich gerade im Grid).
        // Wir suchen das Feld das die aktuelle Grid-Position trackt
        // (FUN_004270F0 nutzt +0x72 als prop1, +0x74 als prop2).
        sw.WriteLine();
        sw.WriteLine("  --- Raw bytes of in-transit ZBs (handle = 0x04008001 oder Cajun ähnliche) ---");
        int zbDumped = 0;
        foreach (var node in EngineObjectList.Walk(mem, 0x100))
        {
            // Filter: ZB in transit (handle has 0x04 bit set OR is the standard 0x00000001
            // but located in the bubble area — for now just dump objects with valid attrs
            // and high-bit handle = in-transit)
            if (zbDumped >= 5) break;
            if (node.Bytes.Length < 0x100) continue;
            // Check ZB-attr validity at +0xF0..+0xF3
            byte h = node.Bytes[0xF0], e = node.Bytes[0xF1];
            byte n = node.Bytes[0xF2], f = node.Bytes[0xF3];
            if (h is < 1 or > 5 || e is < 1 or > 5 || n is < 1 or > 5 || f is < 1 or > 5) continue;
            // Skip stationary pool ZBs (handle exactly ZoombiniHandle.Pool);
            // only show ones with extra bits set (in-transit / parked).
            if (node.Handle == ZoombiniHandle.Pool) continue;
            ushort hdr1A = BitConverter.ToUInt16(node.Bytes, 0x1A);
            sw.WriteLine($"  ZB hdr1A=0x{hdr1A:X4} attrs=({h},{e},{n},{f}) @ 0x{(uint)node.Address:X8}  handle=0x{node.Handle:X8}:");
            for (int row = 0; row < 16; row++)
            {
                int off = row * 16;
                var hex = BitConverter.ToString(node.Bytes, off, 16).Replace("-", " ");
                sw.WriteLine($"    +0x{off:02X}: {hex}");
            }
            zbDumped++;
        }
        if (zbDumped == 0)
            sw.WriteLine("    (keine in-transit ZBs — F12 während ein ZB durchläuft drücken)");

        // === ActionSlot-Pfeile (handle 0x04988000) VOLL dumpen ===
        // Die Pfeile markieren, WO ZBs aus einer Maschine rauskommen. Wenn die
        // Spawn-Zelle als Feld auf dem Pfeil steht (Kandidaten +0x72/+0x74 = Grid,
        // +0x8a = TargetIdx), können wir sie VOR dem Losschicken lesen → deterministisch.
        // Annotiert die wahrscheinlichen Cell-Felder + zeigt jeden 16-bit-Wert, der wie
        // ein Grid-Index (0..168) aussieht, mit (row,col).
        sw.WriteLine();
        sw.WriteLine("  --- ActionSlot-Pfeile (handle=0x04988000) — Suche Spawn-Zellen-Marker ---");
        int arrowDumped = 0;
        foreach (var node in EngineObjectList.Walk(mem, 0x100))
        {
            if (node.Handle != 0x04988000 || node.Bytes.Length < 0x100) continue;
            ushort hdr1A = BitConverter.ToUInt16(node.Bytes, 0x1A);
            ushort f72 = BitConverter.ToUInt16(node.Bytes, 0x72);
            ushort f74 = BitConverter.ToUInt16(node.Bytes, 0x74);
            ushort f8a = BitConverter.ToUInt16(node.Bytes, 0x8a);
            sw.WriteLine($"  Pfeil hdr1A=0x{hdr1A:X4} @ 0x{(uint)node.Address:X8}  " +
                         $"+0x72={f72} +0x74={f74} (→Grid ({f72},{f74}))  +0x8a(TargetIdx)={f8a}");
            // Alle 16-bit-Werte, die wie ein Grid-Index aussehen (0..168), mit (row,col):
            var cands = new List<string>();
            for (int off = 0x20; off + 1 < node.Bytes.Length; off += 2)
            {
                ushort v = BitConverter.ToUInt16(node.Bytes, off);
                if (v is > 0 and < 169) cands.Add($"+0x{off:X2}={v}({v / 13},{v % 13})");
            }
            sw.WriteLine($"      Grid-Index-Kandidaten (0..168): {string.Join(" ", cands)}");
            for (int row = 0; row < 16; row++)
            {
                int off = row * 16;
                var hex = BitConverter.ToString(node.Bytes, off, 16).Replace("-", " ");
                sw.WriteLine($"    +0x{off:02X}: {hex}");
            }
            arrowDumped++;
        }
        if (arrowDumped == 0)
            sw.WriteLine("    (keine Pfeil-Objekte gefunden)");

        // === Pre-Computed-Match-Tabelle Hunt ===
        // FUN_0044A990 schreibt pro ZB Daten in [*0x4A2818 + 0xB83C].
        // Wenn dort Match-Werte vorberechnet sind, sehen wir sie.
        sw.WriteLine();
        sw.WriteLine("  --- [*0x4A2818 + 0xB83C..0xB970] (ZB-Aggregator-Tabelle, 320 bytes) ---");
        var aggPtrBytes = mem.ReadBytes(0x004A2818, 4);
        if (aggPtrBytes != null)
        {
            uint aggPtr = BitConverter.ToUInt32(aggPtrBytes, 0);
            sw.WriteLine($"  *0x4A2818 = 0x{aggPtr:X8}");
            if (aggPtr > 0x10000)
            {
                var aggData = mem.ReadBytes((nint)aggPtr + 0xB83C, 0x140);
                if (aggData != null)
                {
                    for (int row = 0; row < aggData.Length; row += 16)
                    {
                        var hex = BitConverter.ToString(aggData, row, Math.Min(16, aggData.Length - row)).Replace("-", " ");
                        sw.WriteLine($"  +0x{0xB83C + row:04X}: {hex}");
                    }
                }
            }
        }

        // Plus: andere wahrscheinliche Match-Tabellen-Bereiche dumpen
        sw.WriteLine();
        sw.WriteLine("  --- Memory rund um 0x49ACA0 (Position-Counter) — sucht nach kleinen Werten ---");
        var aroundCounter = mem.ReadBytes(0x0049AB00, 0x300);
        if (aroundCounter != null)
        {
            for (int row = 0; row < aroundCounter.Length; row += 32)
            {
                var hex = BitConverter.ToString(aroundCounter, row, Math.Min(32, aroundCounter.Length - row)).Replace("-", " ");
                sw.WriteLine($"  0x{0x0049AB00 + row:08X}: {hex}");
            }
        }

        // Auch erste 2 Bubble-Objects als Referenz
        sw.WriteLine();
        sw.WriteLine("  --- Raw bytes of first 2 Bubble-Objects (Referenz) ---");
        int dumped = 0;
        foreach (var b in s.LiveBubbles)
        {
            if (dumped++ >= 2) break;
            var bytes = mem.ReadBytes(b.NodeAddress, 0x100);
            if (bytes is null) continue;
            sw.WriteLine($"  hdr1A=0x{b.HeaderId:X4} @ 0x{(uint)b.NodeAddress:X8}:");
            for (int row = 0; row < 16; row++)
            {
                int off = row * 16;
                var hex = BitConverter.ToString(bytes, off, 16).Replace("-", " ");
                sw.WriteLine($"    +0x{off:02X}: {hex}");
            }
        }

        sw.WriteLine();
        sw.WriteLine("  --- Position Counter Table (live aus 0x49ACA0) ---");
        if (s.PositionCounters.Count == 0) sw.WriteLine("    (all zero)");
        foreach (var (idx, cnt) in s.PositionCounters)
        {
            int prop1 = idx / 13, prop2 = idx % 13;
            ushort handle = s.PositionHandles.TryGetValue(idx, out var h) ? h : (ushort)0;
            sw.WriteLine($"    idx={idx,3} ({prop1},{prop2})  counter={cnt}  handle=0x{handle:X4}");
        }
        sw.WriteLine();
    }

    /// <summary>Offline-Sim-Trace fürs Routing-Debugging: baut das Live-Grid und
    /// simuliert jeden Pool-ZB von jeder Haupt-Maschine — Pfad + Outcome. Dazu die
    /// aufgelösten Conditional-Zellen (LIVE attr/variant/match-Richtung), Switch-
    /// Initialzustände und Goal-Zellen. Das ist alles, was fehlte, um den „0/16"-
    /// Routing-Bug offline zu finden: man sieht direkt, an welcher Zelle ein ZB
    /// stirbt und ob das Conditional-Matching ihn dorthin schickt.</summary>
    private static void WriteBubblewonderSimTrace(StreamWriter sw, IMemoryReader mem)
    {
        var grid = BubblewonderGridModelBuilder.FromLiveMemory(mem);
        sw.WriteLine();
        sw.WriteLine("  --- SIM-TRACE (offline-Routing, aus Live-Grid) ---");

        // Goal-Zellen (Typ 0x17 + bekannte) — ohne die ist Scoren unmöglich.
        var goals = new List<string>();
        for (int p = 0; p < 12 * 13; p++)
            if (grid.CellAt(p).Type == MechanismType.Goal) goals.Add($"({p / 13},{p % 13})");
        sw.WriteLine($"  Goal-Zellen (0x17): {(goals.Count > 0 ? string.Join(" ", goals) : "KEINE!")}");
        // Stein-Insel-Zellen MIT Typ-Wert (0x14=Start, 0x15/0x16=Zwischenstation,
        // 0x17=Ziel) — der Pool-Klassifizierer zählt nur 0x15/0x16 als Insel-Park.
        var cellTable = mem.ReadBytes(BubblewonderMemoryMap.CellTypeTable, 12 * 13 * 2);
        int CellType(int p) => cellTable != null && p * 2 + 1 < cellTable.Length
            ? cellTable[p * 2] | (cellTable[p * 2 + 1] << 8) : -1;
        var stones = new List<string>();
        for (int p = 0; p < 12 * 13; p++)
            if (grid.CellAt(p).Type == MechanismType.StoneArea)
                stones.Add($"({p / 13},{p % 13})=0x{CellType(p):X2}");
        sw.WriteLine($"  Stein-Insel-Zellen (Typ): {(stones.Count > 0 ? string.Join(" ", stones) : "(keine)")}");
        if (grid.State.KnownGoalCells.Count > 0)
            sw.WriteLine($"  KnownGoalCells: {string.Join(" ", grid.State.KnownGoalCells.Select(p => $"({p / 13},{p % 13})"))}");

        sw.WriteLine($"  Maschinen: {grid.Machines.Count}");
        foreach (var m in grid.Machines)
            sw.WriteLine($"    M{m.Index} @ ({m.StartCellIndex / 13},{m.StartCellIndex % 13}) → {m.StartDirection}{(m.IsIsland ? " [INSEL]" : "")}");

        sw.WriteLine("  Conditional-Zellen (live attr/variant/matchDir — Routing-Verdächtiger):");
        for (int p = 0; p < 12 * 13; p++)
        {
            var c = grid.CellAt(p);
            if (c.Type != MechanismType.Conditional) continue;
            sw.WriteLine($"    ({p / 13},{p % 13}) attr={c.ConditionalAttrCode} variant={c.ConditionalVariant} " +
                         $"matchDir={c.PrimaryDirection?.ToString() ?? "—"} activeDirs=[{DirsStr(c.ActiveDirections)}]");
        }

        sw.WriteLine("  Switch-Zellen (initialer State):");
        for (int p = 0; p < 12 * 13; p++)
        {
            var c = grid.CellAt(p);
            if (c.Type != MechanismType.SwitchActivated) continue;
            int st = grid.State.SwitchStateByCell.GetValueOrDefault(p, -1);
            sw.WriteLine($"    ({p / 13},{p % 13}) state={st} activeDirs=[{DirsStr(c.ActiveDirections)}]");
        }

        var pool = PoolScanner.Scan(mem);

        // Pool-/Insel-Klassifikation pro gescanntem ZB — zeigt warum ein ZB als
        // Pool / Insel / fallengelassen (gescannt, aber weder noch) gilt.
        var (clsMain, clsIsland) = BubblewonderPoolClassifier.Split(pool, mem);
        var mainIds = clsMain.Select(m => m.HeaderId).ToHashSet();
        var islandIds = clsIsland.Select(m => m.HeaderId).ToHashSet();
        sw.WriteLine($"  Klassifikation: {pool.Count} gescannt → {clsMain.Count} Pool, {clsIsland.Count} Insel, " +
                     $"{pool.Count - clsMain.Count - clsIsland.Count} fallengelassen:");
        foreach (var pm in pool)
        {
            int cpos = pm.GridRow * 13 + pm.GridCol;
            int ct = (pm.GridRow < 12 && pm.GridCol < 13 && cellTable != null && cpos * 2 + 1 < cellTable.Length)
                ? cellTable[cpos * 2] | (cellTable[cpos * 2 + 1] << 8) : -1;
            string cls = mainIds.Contains(pm.HeaderId) ? "Pool"
                       : islandIds.Contains(pm.HeaderId) ? "INSEL" : "FALLENGELASSEN";
            sw.WriteLine($"    0x{pm.HeaderId:X4} handle=0x{pm.Handle:X8} grid=({pm.GridRow},{pm.GridCol}) " +
                         $"zelltyp=0x{ct:X2} → {cls}");
        }

        sw.WriteLine($"  Trace {pool.Count} Pool-ZBs × Haupt-Maschinen (Pfad gekappt bei 60 Zellen):");
        // Invariante (User 2026-05-28): im echten Spiel läuft NIE ein ZB über den
        // Gitter-Rand hinaus. Jeder modellierte Gitter-Austritt ist daher ein
        // ROUTING-BUG genau an dieser Zelle (falsche/fehlende umlenkende Zelle).
        var exitCells = new Dictionary<int, int>();
        foreach (var pm in pool)
        {
            var zb = new SimZb(pm.HeaderId, pm.Hair, pm.Eyes, pm.Nose, pm.Feet);
            for (int mi = 0; mi < grid.Machines.Count; mi++)
            {
                if (grid.Machines[mi].IsIsland) continue;
                var r = BubblewonderSimulator.Simulate(grid, zb, mi);
                var cells = r.PathPositions.Take(60).Select(p => $"({p / 13},{p % 13})");
                string path = string.Join("→", cells) +
                    (r.PathPositions.Count > 60 ? $"…(+{r.PathPositions.Count - 60})" : "");
                bool leftGrid = LeftGridExit(grid, r);
                sw.WriteLine($"    ZB 0x{pm.HeaderId:X4} ({pm.Hair},{pm.Eyes},{pm.Nose},{pm.Feet}) M{mi} → {r.Outcome}" +
                             (leftGrid ? "  ⚠ LÄUFT AUS DEM GRID (Routing-Bug)" : ""));
                sw.WriteLine($"        {path}");
                if (leftGrid && r.PathPositions.Count > 0)
                {
                    int last = r.PathPositions[^1];
                    exitCells[last] = exitCells.GetValueOrDefault(last) + 1;
                }
            }
        }
        if (exitCells.Count > 0)
        {
            sw.WriteLine();
            sw.WriteLine("  ⚠⚠ ROUTING-BUGS — diese Zellen schicken ZBs aus dem Grid (real unmöglich):");
            foreach (var (pos, n) in exitCells.OrderByDescending(kv => kv.Value))
                sw.WriteLine($"     ({pos / 13},{pos % 13}): {n} Pfad(e) — hier fehlt eine umlenkende Zelle ODER ein Pfeil weiter oben stimmt nicht");
        }
    }

    /// <summary>True wenn der ZB das Grid über den Rand verlassen hat (Outcome=Dead,
    /// aber die letzte Zelle ist KEINE Falle/Steinzelle — d.h. er lief einfach
    /// hinaus). Im echten Spiel unmöglich → Routing-Bug an der letzten Zelle.</summary>
    private static bool LeftGridExit(BubblewonderGridModel grid, SimResult r)
    {
        if (r.Outcome != SimOutcome.Dead || r.PathPositions.Count == 0) return false;
        var lastType = grid.CellAt(r.PathPositions[^1]).Type;
        return lastType is not (MechanismType.Trap or MechanismType.StoneArea);
    }

    private static string DirsStr(bool[] active)
    {
        var names = new List<string>();
        for (int i = 0; i < 4 && i < active.Length; i++)
            if (active[i]) names.Add(CellModel.FBitToDirection[i].ToString());
        return string.Join(",", names);
    }

    /// <summary>Zeige die letzten gecachten Aggregator-Conditional-Bytes
    /// (= Bytes die jemals != leer beobachtet wurden vom Tracker).</summary>
    private static void WriteAsciiGrid(StreamWriter sw, IMemoryReader mem, BubblewonderState s)
    {
        sw.WriteLine("=== ASCII-Grid (zum Abgleich mit Spielbildschirm) ===");
        sw.WriteLine("  Legende: T=Trap, S=Switch, ?=REGS-Cell, .=leer, x=außerhalb");
        sw.WriteLine();
        const int Cols = 13;
        const int Rows = 12;

        // Build grid: marker pro Position
        var grid = new char[Rows, Cols];
        var posInfo = new Dictionary<int, string>();   // posIdx -> mech raw bytes
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                grid[r, c] = '.';

        foreach (var m in s.Grid.Mechanisms)
        {
            int r = m.Position.Prop1, c = m.Position.Prop2;
            if (r < 0 || r >= Rows || c < 0 || c >= Cols) continue;
            grid[r, c] = m.Type switch
            {
                MechanismType.Trap => 'T',
                MechanismType.SwitchActivated => 'S',
                MechanismType.Sticky => '*',
                MechanismType.Trigger => 'B',
                _ => '?',
            };
            posInfo[r * Cols + c] = $"raw=[{string.Join(",", m.RawFields)}]";
        }

        // Render grid mit row/col labels
        // Header (col indices)
        sw.Write("        ");
        for (int c = 0; c < Cols; c++) sw.Write($" {c,2}");
        sw.WriteLine();
        sw.Write("        ");
        for (int c = 0; c < Cols; c++) sw.Write(" --");
        sw.WriteLine();
        for (int r = 0; r < Rows; r++)
        {
            sw.Write($"  row {r,2} |");
            for (int c = 0; c < Cols; c++)
                sw.Write($"  {grid[r, c]}");
            sw.WriteLine();
        }

        // Wenn ein Pfad getrackt: zweite Karte mit Pfad-Schritten überlagert
        // (Nur wenn wir gerade Bytes für den Pfad haben — overlay-Zahlen sind
        // hilfreicher als nur '?')
        sw.WriteLine();
        sw.WriteLine("  REGS-Cell-Details:");
        foreach (var (posIdx, info) in posInfo.OrderBy(kv => kv.Key))
            sw.WriteLine($"    ({posIdx / Cols,2},{posIdx % Cols,2})  {info}");
    }

    /// <summary>Letzter abgeschlossener ZB-Pfad — pro Position die kompletten
    /// Live-Bubble-Bytes ausgeben. So kann der User pro Cell visuell sagen
    /// welcher Type sie ist, und wir vergleichen die Bytes byte-genau um den
    /// Discriminator zu finden.</summary>
    private static void WriteStoneRiseSnapshot(StreamWriter sw, IMemoryReader mem)
    {
        int diff = mem.ReadWord(StoneRiseMemoryMap.Difficulty);
        sw.WriteLine("=== Stone Rise snapshot ===");
        sw.WriteLine($"  difficulty (0-based): {diff}  (= 1-based {diff + 1})");

        int nonDefault = 0;
        var lines = new List<string>();
        for (int i = 0; i < StoneRiseMemoryMap.TileCount; i++)
        {
            nint tileBase = StoneRiseMemoryMap.TilesBase + i * StoneRiseMemoryMap.TileStride;
            var bytes = mem.ReadBytes(tileBase, StoneRiseMemoryMap.TileStride);
            if (bytes is null) continue;
            var words = new ushort[9];
            for (int w = 0; w < 9; w++) words[w] = BitConverter.ToUInt16(bytes, w * 2);

            // "Default empty" = type 500, all other slots 0xFFFF or 0, plus
            // sequential id at word 8. Skip those to keep the dump small.
            bool isDefault = words[0] == StoneRiseMemoryMap.TileTypeEmpty
                          && words[1] == 0
                          && words[2] == 0xFFFF && words[3] == 0xFFFF
                          && words[4] == 0xFFFF && words[5] == 0xFFFF
                          && words[6] == 0xFFFF && words[7] == 0xFFFF;
            if (isDefault) continue;
            nonDefault++;
            // Also read the 4 bytes IMMEDIATELY BEFORE the tile record. The
            // engine reads `[tile_addr - 2]` as the placement marker on
            // type-508 setters (verified in disasm at 0x43C49C / 0x43C607),
            // and uses it as a lookup key into the engine object list.
            // For most tiles it's just w8 of the previous tile (= seq) and
            // doesn't change, but for tiles that DO change on placement we
            // can spot the difference here.
            var preBytes = mem.ReadBytes(tileBase - 4, 4);
            string preHex = preBytes is null ? "(unread)"
                : $"{preBytes[0]:X2} {preBytes[1]:X2} {preBytes[2]:X2} {preBytes[3]:X2}";
            ushort preWord = preBytes is null ? (ushort)0
                : BitConverter.ToUInt16(preBytes, 2);
            lines.Add($"  tile[{i,3}] @ 0x{(uint)tileBase:X8}  pre=[{preHex}] (word=0x{preWord:X4})  "
                    + $"type={words[0]}  w1={words[1]}  w2={words[2]}  w3={words[3]}  "
                    + $"w4={words[4]}  w5={words[5]}  w6={words[6]}  w7={words[7]}  seq={words[8]}");
        }
        sw.WriteLine($"  non-default tiles: {nonDefault} / {StoneRiseMemoryMap.TileCount}");
        foreach (var l in lines) sw.WriteLine(l);

        // Aux arrays in compact non-zero form.
        WriteNonZeroArray(sw, mem, "TileFlags @ 0x49C874", StoneRiseMemoryMap.TileFlags, StoneRiseMemoryMap.TileCount);
        WriteNonZeroArray(sw, mem, "TileAux   @ 0x49C670", StoneRiseMemoryMap.TileAux,   StoneRiseMemoryMap.TileCount);
        WriteNonZeroArray(sw, mem, "PairList  @ 0x49C820", StoneRiseMemoryMap.PairList, 20);

        // Engine hit-test table — needed to debug minimap layout. Helper
        // builds a tile→position cache from this on first sight; if the
        // count or order doesn't match our PairSlots assumption, slots end
        // up at wrong positions or missing entirely.
        int activeCount = mem.ReadWord(StoneRiseMemoryMap.ActiveSlotCount);
        sw.WriteLine($"  ActiveSlotCount @ 0x{(uint)StoneRiseMemoryMap.ActiveSlotCount:X8} = {activeCount}");
        int readCount = Math.Clamp(activeCount, 0, 64);
        var posBytes = mem.ReadBytes(StoneRiseMemoryMap.ActiveSlotPositions, readCount * 4);
        var mapBytes = mem.ReadBytes(StoneRiseMemoryMap.ActiveSlotToTileIndex, readCount * 2);
        if (posBytes is not null && mapBytes is not null)
        {
            sw.WriteLine($"  ActiveSlot[] (positions @ 0x{(uint)StoneRiseMemoryMap.ActiveSlotPositions:X8}, tile_idx @ 0x{(uint)StoneRiseMemoryMap.ActiveSlotToTileIndex:X8}):");
            for (int i = 0; i < readCount; i++)
            {
                ushort x = BitConverter.ToUInt16(posBytes, i * 4);
                ushort y = BitConverter.ToUInt16(posBytes, i * 4 + 2);
                ushort tileIdx = BitConverter.ToUInt16(mapBytes, i * 2);
                sw.WriteLine($"    [{i,2}] tile_idx={tileIdx,3}  x={x,4} y={y,4}");
            }
        }
        sw.WriteLine();
    }

    private static void WriteNonZeroArray(StreamWriter sw, IMemoryReader mem, string label, nint baseAddr, int wordCount)
    {
        var bytes = mem.ReadBytes(baseAddr, wordCount * 2);
        if (bytes is null) { sw.WriteLine($"  {label}: <unread>"); return; }
        var nonZero = new List<string>();
        for (int i = 0; i < wordCount; i++)
        {
            ushort v = BitConverter.ToUInt16(bytes, i * 2);
            if (v != 0) nonZero.Add($"[{i}]={v}");
        }
        sw.WriteLine($"  {label}: {(nonZero.Count == 0 ? "(all zero)" : string.Join(" ", nonZero))}");
    }

    /// <summary>Captain Cajun snapshot — stage 1: difficulty + seat layout
    /// from the engine-shared position table. Key Cajun state addresses
    /// (0x495A30..0x495AFF) are dumped raw so further reverse-engineering
    /// can correlate them with gameplay actions.</summary>
    private static void WriteCaptainCajunSnapshot(StreamWriter sw, IMemoryReader mem)
    {
        var s = CaptainCajunState.Read(mem);
        sw.WriteLine("=== Captain Cajun snapshot ===");
        sw.WriteLine($"  Difficulty (1-based @ 0x{(uint)CaptainCajunMemoryMap.Difficulty:X8}) = {s.Difficulty}");
        ushort cajun = mem.ReadWord(CaptainCajunMemoryMap.DifficultyCajun);
        sw.WriteLine($"  DifficultyCajun (0-based @ 0x{(uint)CaptainCajunMemoryMap.DifficultyCajun:X8}) = {cajun}");
        sw.WriteLine($"  Seats (engine table @ 0x{(uint)CaptainCajunMemoryMap.SeatPositions:X8}, count={s.Seats.Count}):");
        foreach (var seat in s.Seats)
            sw.WriteLine($"    [{seat.Index,2}] x={seat.X,4} y={seat.Y,4}  placed_hdr1A=0x{seat.PlacedZbHeaderId:X4}  raw_id=0x{seat.RawZbIdField:X4}  raw_occ={seat.RawOccupancyByte}");

        // State region 0x495A00..0x495AFF — for further RE
        var stateBytes = mem.ReadBytes(CaptainCajunMemoryMap.StateRegionStart,
            (int)(CaptainCajunMemoryMap.StateRegionEnd - CaptainCajunMemoryMap.StateRegionStart));
        if (stateBytes is not null)
        {
            sw.WriteLine($"  State region (non-zero words 0x{(uint)CaptainCajunMemoryMap.StateRegionStart:X8}..):");
            for (int i = 0; i < stateBytes.Length; i += 2)
            {
                ushort w = BitConverter.ToUInt16(stateBytes, i);
                if (w == 0) continue;
                sw.WriteLine($"    +0x{i:X3} (0x{(uint)CaptainCajunMemoryMap.StateRegionStart + i:X8}) = 0x{w:X4} ({w})");
            }
        }

        sw.WriteLine();
    }

    private static void WriteCavesSnapshot(StreamWriter sw, IMemoryReader mem)
    {
        var caves = CavesState.Read(mem);
        sw.WriteLine("=== Caves snapshot ===");
        sw.WriteLine($"  difficulty={caves.Difficulty}  axis_count={caves.AxisCount}");
        sw.Write($"  axis 1: invert={caves.Axis1.Invert} ");
        foreach (var c in caves.Axis1.Conditions)
            sw.Write($" {ZoombiniVariants.AttributeName(c.AttrType)}={ZoombiniVariants.VariantName(c.AttrType, c.Variant)}");
        sw.WriteLine();
        if (caves.AxisCount >= 2)
        {
            sw.Write($"  axis 2: invert={caves.Axis2.Invert} ");
            foreach (var c in caves.Axis2.Conditions)
                sw.Write($" {ZoombiniVariants.AttributeName(c.AttrType)}={ZoombiniVariants.VariantName(c.AttrType, c.Variant)}");
            sw.WriteLine();
        }
        var raw = mem.ReadBytes(CavesMemoryMap.CaveStruct, 0x20);
        if (raw is not null)
        {
            sw.Write($"  raw @ 0x{(uint)CavesMemoryMap.CaveStruct:X8}:");
            foreach (var b in raw) sw.Write($" {b:X2}");
            sw.WriteLine();
        }
        sw.WriteLine();

        // Per-zoombini cave analysis: show BOTH the raw match (any-of conditions)
        // AND the post-inversion match for each axis. Helps pinpoint whether the
        // 2D matrix or the inversion is wrong when a recommendation misfires.
        var held = HeldZoombini.Find(mem);
        if (held is { } h)
        {
            sw.WriteLine("=== Held-zb cave analysis ===");
            sw.WriteLine($"  zb attrs: Hair={h.Hair} Eyes={h.Eyes} Nose={h.Nose} Feet={h.Feet}");
            DumpAxisAnalysis(sw, "Axis 1", caves.Axis1, h);
            if (caves.AxisCount >= 2) DumpAxisAnalysis(sw, "Axis 2", caves.Axis2, h);
            int? rec = caves.FindAcceptingCave(h);
            sw.WriteLine($"  → Helper-Empfehlung: Höhle {rec}");
            sw.WriteLine();
        }
    }

    private static void DumpAxisAnalysis(StreamWriter sw, string label, CavesState.AxisFilter axis, PoolMember zb)
    {
        bool rawAny = false;
        foreach (var (t, v) in axis.Conditions)
        {
            byte zbAttr = t switch { 1 => zb.Hair, 2 => zb.Eyes, 3 => zb.Nose, 4 => zb.Feet, _ => (byte)0 };
            bool match = zbAttr == v;
            sw.WriteLine($"  {label}: {ZoombiniVariants.AttributeName(t)}={ZoombiniVariants.VariantName(t, v)}  zb has {zbAttr}  {(match ? "✓ MATCH" : "✗")}");
            if (match) rawAny = true;
        }
        bool finalMatch = rawAny ^ axis.Invert;
        sw.WriteLine($"  {label}: rawAny={rawAny}  invert={axis.Invert}  final={finalMatch}");
    }

    private static void WritePuzzleDetection(StreamWriter sw, IMemoryReader mem, PuzzleManager mgr)
    {
        sw.WriteLine("=== Puzzle detection (every detector) ===");
        var winner = mgr.Detect(mem);
        var now = DateTime.UtcNow;
        sw.WriteLine($"  → winner: {winner.Id} (active={winner.IsActive}, conf={winner.Detection.Confidence})");
        foreach (var entry in mgr.Diagnose(mem))
        {
            string fresh = entry.IsFresh(now) ? "  ⭐FRESH" : "";
            sw.WriteLine($"  [{entry.Detector.Id,-18}] active={entry.Detection.IsActive,-5} "
                       + $"conf={entry.Detection.Confidence,3}  age={entry.Age(now).TotalSeconds,5:F1}s{fresh}  "
                       + $"sig=0x{entry.Detection.Signature:X16}  reason={entry.Detection.Reason}");
        }
        sw.WriteLine();
    }

    private static void WriteCliffSnapshot(StreamWriter sw, IMemoryReader mem)
    {
        var cliff = CliffState.Read(mem);
        sw.WriteLine("=== Cliff snapshot ===");
        sw.WriteLine($"  n_allerg={cliff.NAllerg}  attempts={cliff.Attempts}  which_cliff={cliff.WhichCliff}");
        sw.WriteLine($"  accepting={cliff.AcceptingBridgeLabel}  rejecting={cliff.RejectingBridgeLabel}");
        for (int i = 0; i < cliff.Rules.Count; i++)
        {
            var r = cliff.Rules[i];
            sw.WriteLine($"  rule[{i}] type={r.Type} ({ZoombiniVariants.AttributeName(r.Type)}) "
                       + $"value=0x{r.Value:X2} ({ZoombiniVariants.DescribeRuleValue(r.Type, r.Value)})");
        }
        sw.WriteLine();
    }

    private static void WriteDragState(StreamWriter sw, IMemoryReader mem)
    {
        sw.WriteLine("=== Drag-state ===");
        sw.WriteLine($"  cliff drag-flag [0x00494522] = {mem.ReadWord(0x00494522)}");
        sw.WriteLine($"  caves drag-flag [0x004A204C] = {mem.ReadWord(0x004A204C)}");
        sw.WriteLine($"  IsDragActive  = {HeldZoombini.IsDragActive(mem)}");
        sw.WriteLine();

        // Auto-analysis: walk every list node and classify it.
        const int NodeReadSize = EngineObjectList.HeaderSize + 0xC4;
        const int recOff = EngineObjectList.HeaderSize;
        var draggedByHandle = new List<(nint addr, byte h, byte e, byte n, byte f, ushort y, uint handle)>();
        var draggedByY      = new List<(nint addr, byte h, byte e, byte n, byte f, ushort y, uint handle)>();
        foreach (var node in EngineObjectList.Walk(mem, NodeReadSize))
        {
            byte h = node.Bytes[recOff + 0xC0];
            byte e = node.Bytes[recOff + 0xC1];
            byte n = node.Bytes[recOff + 0xC2];
            byte f = node.Bytes[recOff + 0xC3];
            ushort y = BitConverter.ToUInt16(node.Bytes, recOff + 0x1A);
            bool validAttrs = h is >= 1 and <= 5 && e is >= 1 and <= 5 && n is >= 1 and <= 5 && f is >= 1 and <= 5;
            if (!validAttrs) continue;
            if ((node.Handle & 0x04001000) == 0x04001000)
                draggedByHandle.Add((node.Address, h, e, n, f, y, node.Handle));
            if (y == 0xFFFD)
                draggedByY.Add((node.Address, h, e, n, f, y, node.Handle));
        }

        sw.WriteLine($"  Drag-marked candidates (handle & 0x04001000) with valid attrs: {draggedByHandle.Count}");
        foreach (var c in draggedByHandle)
            sw.WriteLine($"    @ 0x{(uint)c.addr:X8}  attrs=({c.h},{c.e},{c.n},{c.f})  y={c.y}  handle=0x{c.handle:X8}");

        sw.WriteLine($"  Off-stage candidates (y == 0xFFFD) with valid attrs: {draggedByY.Count}");
        foreach (var c in draggedByY)
            sw.WriteLine($"    @ 0x{(uint)c.addr:X8}  attrs=({c.h},{c.e},{c.n},{c.f})  y={c.y}  handle=0x{c.handle:X8}");

        var held = HeldZoombini.Find(mem);
        if (held is { } z)
            sw.WriteLine($"  → HELD picked: @ 0x{(uint)z.Address:X8}  attrs=({z.Hair},{z.Eyes},{z.Nose},{z.Feet})  y={z.YPosition}");
        else
            sw.WriteLine("  → HELD picked: (none — no candidate has valid attrs + drag-marker or y=0xFFFD)");
        sw.WriteLine();
    }

    private static void WriteLinkedListWalk(StreamWriter sw, IMemoryReader mem)
    {
        sw.WriteLine("=== Linked-list walk via [0x4A35C0] ===");
        DiagnosticDumper.WalkObjectList(sw, mem);
        sw.WriteLine();
    }

    private static void WritePoolSummary(StreamWriter sw, IMemoryReader mem)
    {
        var pool = PoolScanner.Scan(mem);
        sw.WriteLine($"=== Pool ({pool.Count} zoombini(s) via engine list) ===");
        foreach (var zb in pool)
        {
            string cajunNote = zb.CajunPlacedFlag != 0
                ? $"  PLACED at screen=({zb.ScreenX},{zb.ScreenY})"
                : "";
            sw.WriteLine($"  @ 0x{(uint)zb.Address:X8}  y={zb.YPosition,3}  attrs=({zb.Hair},{zb.Eyes},{zb.Nose},{zb.Feet})  sprite=0x{zb.SpriteId:X4}  hdr1A=0x{zb.HeaderId:X4}{cajunNote}");
        }

        // Pool-Cluster nach y-Koordinate (= Multi-Pool-Erkennung für Insel-Layouts).
        // Heuristik: Sortiere ZBs nach y. Lücken > 50px markieren einen neuen Cluster.
        if (pool.Count >= 2)
        {
            var sorted = pool.OrderBy(z => z.YPosition).ToList();
            var clusters = new List<List<PoolMember>>();
            var current = new List<PoolMember> { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
            {
                int gap = sorted[i].YPosition - sorted[i - 1].YPosition;
                if (gap > 50)
                {
                    clusters.Add(current);
                    current = new List<PoolMember>();
                }
                current.Add(sorted[i]);
            }
            clusters.Add(current);

            sw.WriteLine();
            sw.WriteLine($"  --- Pool-Cluster (= Pools/Inseln, getrennt durch y-Lücken > 50) ---");
            sw.WriteLine($"  {clusters.Count} Cluster:");
            for (int i = 0; i < clusters.Count; i++)
            {
                var c = clusters[i];
                sw.WriteLine($"    Cluster {i}: {c.Count} ZBs, y={c.Min(z => z.YPosition)}..{c.Max(z => z.YPosition)}  hdr1As={string.Join(",", c.Select(z => $"0x{z.HeaderId:X2}"))}");
            }
        }
        sw.WriteLine();
    }
}
