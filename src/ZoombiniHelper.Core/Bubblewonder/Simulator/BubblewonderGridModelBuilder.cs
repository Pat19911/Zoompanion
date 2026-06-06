namespace ZoombiniHelper.Bubblewonder.Simulator;

/// <summary>
/// Brücke zwischen Live-Memory und <see cref="BubblewonderGridModel"/>.
/// Liest <see cref="BubblewonderState"/> + Bubble-Maschinen aus dem Memory
/// und baut das Simulator-Input-Modell daraus.
/// </summary>
public static class BubblewonderGridModelBuilder
{
    /// <summary>Engine-Handle für Bubble-Maschinen-Objects.</summary>
    public const uint MachineHandle = 0x04108000;

    private const int GridRows = 12;
    private const int GridCols = 13;

    /// <summary>Convenience: liest State + Memory in einem Schritt.</summary>
    public static BubblewonderGridModel FromLiveMemory(IMemoryReader mem)
    {
        var state = BubblewonderState.Read(mem);
        return FromBubbles(state.LiveBubbles, state.RegsResourceId, mem,
            knownGoalCells: BubblewonderKnownMaps.GetGoalCells(state));
    }

    /// <summary>Lädt insel-geparkte ZBs in den GridState. Die Liste kommt
    /// bereits klassifiziert von <see cref="BubblewonderPoolClassifier"/>
    /// (Handle + Zelltyp), NICHT mehr aus der y-Pixel-Cluster-Heuristik —
    /// sonst landeten in Fallen steckende ZBs fälschlich als „geparkt" im State.</summary>
    public static BubblewonderGridModel WithParkedZbs(
        BubblewonderGridModel grid, IReadOnlyList<PoolMember> parkedZbs)
    {
        if (parkedZbs.Count == 0) return grid;

        var islandMachines = grid.Machines.Where(m => m.IsIsland).ToList();
        if (islandMachines.Count == 0) return grid;

        var newState = grid.CloneState();
        // Pragmatisch: alle geparkten ZBs der ersten Insel-Maschine zuordnen.
        // (Bei Layouts mit mehreren Insel-Maschinen müssten wir per y-Cluster
        //  die richtige Maschine matchen — TODO wenn relevant.)
        int targetMachineIdx = islandMachines[0].Index;
        if (!newState.ParkedZbsByMachineIdx.TryGetValue(targetMachineIdx, out var list))
            newState.ParkedZbsByMachineIdx[targetMachineIdx] = list = new List<SimZb>();
        foreach (var zb in parkedZbs)
        {
            list.Add(new SimZb(zb.HeaderId, zb.Hair, zb.Eyes, zb.Nose, zb.Feet));
        }
        return grid.WithState(newState);
    }

    /// <summary>Ersetzt für jeden in einer Klebefalle (Sticky) steckenden ZB die
    /// vom Live-Lesen unvollständigen State-Daten durch echte Werte aus dem
    /// Pool-Scan: (1) die ATTRIBUTE (<see cref="BuildGridState"/> legt nur einen
    /// Stub <c>SimZb(handle,0,0,0,0)</c> ab → falsches Conditional-Routing nach
    /// Befreiung) und (2) die EINTRITTSRICHTUNG aus dem ZB-Feld +0x58
    /// (<see cref="PoolMember.MovementDirRaw"/>, disassembly-verifiziert v2
    /// fn 0x42a7a0). Ohne (2) defaultete die Kanal-Befreiung auf <c>Down</c> —
    /// der befreite ZB lief dann evtl. in die falsche Richtung.</summary>
    public static BubblewonderGridModel WithStickyAttributes(
        BubblewonderGridModel grid, IReadOnlyList<PoolMember> pool)
    {
        if (pool.Count == 0) return grid;

        // Matching über die GRID-POSITION, NICHT über HeaderId: der Sticky speichert
        // in +0x86 einen Engine-Handle (≠ hdr1A), ein HeaderId-Match schlägt fehl
        // (→ Stub 0,0,0,0 blieb stehen). Ein festklebender ZB ist aber eindeutig
        // der losgeschickte Pool-ZB (Handle ZoombiniHandle.Launched), dessen
        // Grid-Position (+0x72/74) auf einer Sticky-Zelle liegt. (Bewusst nur
        // Launched, nicht Parked: ein festklebender ZB ist aktiv-gefangen, kein
        // Beleg dass er je den geparkten Ruhe-Handle trägt.)
        GridState? newState = null;
        foreach (var pm in pool)
        {
            if (pm.Handle != ZoombiniHandle.Launched) continue;
            if (pm.GridRow >= GridRows || pm.GridCol >= GridCols) continue;
            int pos = pm.GridRow * GridCols + pm.GridCol;
            if (grid.CellAt(pos).Type != MechanismType.Sticky) continue;

            newState ??= grid.CloneState();
            newState.StickyTrappedByCell[pos] =
                new SimZb(pm.HeaderId, pm.Hair, pm.Eyes, pm.Nose, pm.Feet);
            // Eintrittsrichtung aus +0x58 (disassembly-verifiziert). Ohne sie
            // defaultet ApplyChannelEffect fälschlich auf Down.
            if (pm.MovementDirRaw <= 3)
                newState.StickyEntryDirByCell[pos] = CellModel.FBitToDirection[pm.MovementDirRaw];
        }
        return newState is null ? grid : grid.WithState(newState);
    }

    /// <summary>Baut Grid-Modell aus bereits gelesenem State plus dem
    /// Memory-Reader. Optional: <paramref name="liveSpawnPositions"/> und
    /// <paramref name="liveSpawnDirections"/> aus dem Tracker — überschreiben
    /// die hardcoded SpawnMappings + dirCode-Heuristik (zuverlässiger).</summary>
    public static BubblewonderGridModel FromState(
        BubblewonderState state, IMemoryReader mem,
        IReadOnlyList<int>? liveSpawnPositions = null,
        IReadOnlyDictionary<int, Direction>? liveSpawnDirections = null,
        IReadOnlyCollection<int>? knownGoalCells = null,
        int? learnedIslandSpawn = null)
    {
        // Hardcoded Goal-Cells für diese Map (aus historischen Beobachtungen)
        // mit den Live-gelernten kombinieren — Sim sieht beide Quellen.
        var hardcoded = BubblewonderKnownMaps.GetGoalCells(state);
        IReadOnlyCollection<int>? merged = (knownGoalCells, hardcoded.Count) switch
        {
            (null, 0) => null,
            (null, _) => hardcoded,
            (_, 0) => knownGoalCells,
            _ => knownGoalCells.Concat(hardcoded).ToHashSet(),
        };
        return FromBubbles(state.LiveBubbles, state.RegsResourceId, mem,
            liveSpawnPositions, liveSpawnDirections, merged, learnedIslandSpawn);
    }

    /// <summary>Baut Grid-Modell direkt aus Bubble-Liste + REGS-ID.</summary>
    public static BubblewonderGridModel FromBubbles(
        IReadOnlyList<BubbleObject> bubbles, int regsResourceId, IMemoryReader mem,
        IReadOnlyList<int>? liveSpawnPositions = null,
        IReadOnlyDictionary<int, Direction>? liveSpawnDirections = null,
        IReadOnlyCollection<int>? knownGoalCells = null,
        int? learnedIslandSpawn = null)
    {
        var cells = BuildCells(bubbles);
        AddStoneCells(cells, mem);
        var machines = BuildMachines(regsResourceId, mem, liveSpawnPositions, liveSpawnDirections, learnedIslandSpawn);
        MarkIslandMachineCells(cells, machines);
        var gridState = BuildGridState(bubbles);
        if (knownGoalCells is { Count: > 0 })
            foreach (var p in knownGoalCells) gridState.KnownGoalCells.Add(p);
        return new BubblewonderGridModel(cells, machines, gridState);
    }

    private static Dictionary<int, CellModel> BuildCells(IReadOnlyList<BubbleObject> bubbles)
    {
        var cells = new Dictionary<int, CellModel>();
        foreach (var bubble in bubbles)
        {
            var regs = bubble.AsRegsRecord();
            if (!IsValidGridPosition(regs)) continue;
            cells[regs.PositionIndex] = BuildCell(bubble, regs);
        }
        return cells;
    }

    /// <summary>Liest die engine-eigene Zelltyp-Tabelle (<see cref="BubblewonderMemoryMap.CellTypeTable"/>)
    /// und fügt die Steinbereich-Zellen hinzu: 0x17 = Ziel (oben-rechts) → <see cref="MechanismType.Goal"/>,
    /// 0x14/0x15/0x16 = Start/Zwischenstationen → <see cref="MechanismType.StoneArea"/>.
    /// <para>Diese Zellen stehen NICHT in den REGS (live verifiziert: 100 % REGS-Match
    /// über 62 Dumps), nur in dieser Tabelle — deshalb „sah" der Simulator das Ziel
    /// vorher nicht und wertete stattdessen jeden Gitter-Austritt fälschlich als Score.
    /// REGS-Mechanik hat Vorrang, falls eine Position doppelt belegt wäre (kommt real nicht vor).</para></summary>
    private static void AddStoneCells(Dictionary<int, CellModel> cells, IMemoryReader mem)
    {
        int n = GridRows * GridCols;
        var bytes = mem.ReadBytes(BubblewonderMemoryMap.CellTypeTable, n * 2);
        if (bytes is null || bytes.Length < n * 2) return;
        for (int pos = 0; pos < n; pos++)
        {
            int t = bytes[pos * 2] | (bytes[pos * 2 + 1] << 8);
            MechanismType type = t switch
            {
                0x17 => MechanismType.Goal,
                0x14 or 0x15 or 0x16 => MechanismType.StoneArea,
                _ => MechanismType.Unknown,
            };
            if (type == MechanismType.Unknown || cells.ContainsKey(pos)) continue;
            cells[pos] = new CellModel(
                Type: type, Channel: 0, ActiveDirections: new bool[4],
                PrimaryDirection: null, ConditionalAttrCode: 0, ConditionalVariant: 0);
        }
    }

    private static CellModel BuildCell(BubbleObject bubble, RegsRecord regs)
    {
        // F4..F7 in Reihenfolge der F-Bit-Indices ablegen (NICHT Direction-Index).
        // Mapping F-Index → Direction siehe CellModel.FBitToDirection.
        var dirs = new[]
        {
            regs.F4 != 0,
            regs.F5 != 0,
            regs.F6 != 0,
            regs.F7 != 0,
        };
        Direction? primary = null;
        for (int i = 0; i < 4; i++)
        {
            if (dirs[i]) { primary = CellModel.FBitToDirection[i]; break; }
        }
        return new CellModel(
            Type: MechanismClassifier.Classify(regs),
            Channel: regs.F3,
            ActiveDirections: dirs,
            PrimaryDirection: primary,
            ConditionalAttrCode: bubble.ConditionalAttrCode,
            ConditionalVariant: bubble.ConditionalVariant);
    }

    /// <summary>Pixel→Cell-Skalierung, abgeleitet aus Bubble-Cells (die sowohl
    /// Pixel als auch Cell-Pos kennen). <c>Col = round((px-OriginX)/CellW)</c>,
    /// <c>Row = round((py-OriginY)/CellH)</c>.</summary>
    internal readonly record struct PixelToCellScale(
        double CellW, double CellH, double OriginX, double OriginY)
    {
        public int ToCell(int px, int py)
        {
            int col = (int)Math.Round((px - OriginX) / CellW);
            int row = (int)Math.Round((py - OriginY) / CellH);
            if (row < 0 || row >= 12 || col < 0 || col >= 13) return -1;
            return row * 13 + col;
        }
    }

    /// <summary>Leitet die Pixel→Cell-Skala aus den Bubble-Cell-Ankern ab
    /// (Cells die Pixel UND Cell-Pos kennen). null wenn zu wenig Anker.</summary>
    internal static PixelToCellScale? DerivePixelScale(IReadOnlyList<BubbleObject> bubbles)
    {
        var anchors = new List<(int Px, int Py, int Row, int Col)>();
        foreach (var b in bubbles)
        {
            if (b.RawBytes is null || b.RawBytes.Length < 0x36) continue;
            var regs = b.AsRegsRecord();
            if (regs.F1 >= 12 || regs.F2 >= 13) continue;
            int px = BitConverter.ToInt16(b.RawBytes, 0x32);
            int py = BitConverter.ToInt16(b.RawBytes, 0x34);
            if (px == 0 && py == 0) continue;  // unitialisiert
            anchors.Add((px, py, regs.F1, regs.F2));
        }
        if (anchors.Count < 2) return null;

        var byCol = anchors.GroupBy(a => a.Col).Select(g => (Col: g.Key, AvgPx: g.Average(a => (double)a.Px))).OrderBy(t => t.Col).ToList();
        var byRow = anchors.GroupBy(a => a.Row).Select(g => (Row: g.Key, AvgPy: g.Average(a => (double)a.Py))).OrderBy(t => t.Row).ToList();
        if (byCol.Count < 2 || byRow.Count < 2) return null;
        double cellW = (byCol[^1].AvgPx - byCol[0].AvgPx) / (byCol[^1].Col - byCol[0].Col);
        double cellH = (byRow[^1].AvgPy - byRow[0].AvgPy) / (byRow[^1].Row - byRow[0].Row);
        if (cellW <= 0 || cellH <= 0) return null;
        double originX = byCol[0].AvgPx - byCol[0].Col * cellW;
        double originY = byRow[0].AvgPy - byRow[0].Row * cellH;
        return new PixelToCellScale(cellW, cellH, originX, originY);
    }

    /// <summary>Liest die engine-eigene Cell→Pixel-Tabelle (*CellPixelTablePtr) und
    /// liefert eine Funktion, die ein Bildschirm-Pixel der NÄCHSTGELEGENEN Grid-Zelle
    /// zuordnet — EXAKT (perspektivisch korrekt), weil es dieselbe Tabelle ist, mit
    /// der die Engine ZBs platziert (disassembly-verifiziert fn 0x42a7a0). null wenn
    /// die Tabelle (noch) nicht alloziert ist. Tabelle: pro Zelle (row*13+col) ein
    /// DWORD = (x:int16, y:int16).</summary>
    internal static Func<int, int, int>? EnginePixelToCell(IMemoryReader mem)
    {
        var ptr = mem.ReadBytes(BubblewonderMemoryMap.CellPixelTablePtr, 4);
        if (ptr is null) return null;
        uint tableVa = BitConverter.ToUInt32(ptr, 0);
        if (tableVa < 0x10000) return null;  // nicht alloziert
        int n = GridRows * 13;
        var tbl = mem.ReadBytes((nint)tableVa, n * 4);
        if (tbl is null || tbl.Length < n * 4) return null;
        var cells = new (int X, int Y)[n];
        for (int c = 0; c < n; c++)
            cells[c] = (BitConverter.ToInt16(tbl, c * 4), BitConverter.ToInt16(tbl, c * 4 + 2));
        return (px, py) =>
        {
            int best = -1; long bestD = long.MaxValue;
            for (int c = 0; c < n; c++)
            {
                var (x, y) = cells[c];
                if (x == 0 && y == 0) continue;  // leere/ungenutzte Zelle
                long d = (long)(x - px) * (x - px) + (long)(y - py) * (y - py);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        };
    }

    /// <summary>Eine live erkannte Maschine: Engine-hdr1A, Cell-Position (oder -1),
    /// Wurf-Richtung (aus dirCode), Insel-Flag (aus TargetIdx +0x8a), Pixel, TargetIdx.</summary>
    internal readonly record struct MachinePlacement(
        ushort Hdr1A, int CellPos, Direction Direction, bool IsIsland, int Px, int Py, int TargetIdx);

    /// <summary>Erkennt ALLE Bubble-Maschinen rein dynamisch aus den Engine-Objekten:
    /// Pixel→Cell für die Sprite-STANDORT-Position (nur Diagnose), dirCode (+0x30) für die
    /// Wurf-Richtung, <b>TargetIdx (+0x8a)</b> für den Insel-Status.
    /// <para><b>Insel-Erkennung via TargetIdx (Ghidra-verifiziert 2026-05-29):</b> die
    /// Board-Init (fn 0x4249fd) setzt Maschine[+0x8a] = 0x48d674[def-1] = Pool-Gruppe;
    /// <c>==0</c> = Hauptpool-Werfer, <c>!=0</c> = eigene Ziel-Bubble (= Zoombini-Insel).
    /// Ersetzt den alten Zelltyp-0x14/15/16-Check, der FALSCH war: Werfer stehen auf der
    /// Start-Insel (Zelltyp 0x14) und wurden dadurch fälschlich als Insel klassifiziert
    /// (→ 16606 alle 3 Maschinen Insel → Solver 0/15).</para>
    /// <para>HINWEIS: <see cref="MachinePlacement.CellPos"/> ist der Sprite-STANDORT
    /// (Pixel→Zelle), <b>nicht</b> die ZB-Spawn-Zelle — die ist eine Laufzeit-Verkettung
    /// und wird live aus dem ersten ZB-Pfad gelernt. CellPos nur für Diagnose nutzen.</para></summary>
    internal static List<MachinePlacement> DetectMachines(
        IMemoryReader mem, IReadOnlyList<BubbleObject> bubbles)
    {
        var result = new List<MachinePlacement>();
        // engine-eigene Cell→Pixel-Tabelle (perspektivisch korrekt) für den Sprite-Standort.
        var engineMap = EnginePixelToCell(mem);
        var scale = DerivePixelScale(bubbles);
        foreach (var node in EngineObjectList.Walk(mem, 0x90))
        {
            if (node.Handle != MachineHandle || node.Bytes.Length < 0x36) continue;
            ushort hdr = BitConverter.ToUInt16(node.Bytes, 0x1A);
            int dirCode = BitConverter.ToInt16(node.Bytes, 0x30);
            int px = BitConverter.ToInt16(node.Bytes, 0x32);
            int py = BitConverter.ToInt16(node.Bytes, 0x34);
            // Sprite-Standort-Zelle (Diagnose) über die engine-eigene Geometrie-Tabelle.
            int cell = engineMap?.Invoke(px, py) ?? -1;
            if (cell < 0) cell = scale?.ToCell(px, py) ?? -1;
            // dirCode-Mapping (verifiziert: SCRB 7000+def + memdump): 1=Left,2=Down,3=Right,4=Up.
            Direction dir = dirCode switch
            {
                1 => Direction.Left, 2 => Direction.Down,
                3 => Direction.Right, 4 => Direction.Up, _ => Direction.Down,
            };
            // TargetIdx (+0x8a) = Pool-Gruppe → Insel-Status (verifiziert, s.o.).
            int targetIdx = node.Bytes.Length >= BubblewonderMemoryMap.BubbleTargetIdxOffset + 2
                ? BitConverter.ToInt16(node.Bytes, BubblewonderMemoryMap.BubbleTargetIdxOffset)
                : -1;
            bool isIsland = targetIdx > 0;
            result.Add(new MachinePlacement(hdr, cell, dir, isIsland, px, py, targetIdx));
        }
        return result;
    }

    private static List<MachineModel> BuildMachines(
        int regsId, IMemoryReader mem,
        IReadOnlyList<int>? liveSpawnPositions,
        IReadOnlyDictionary<int, Direction>? liveSpawnDirections,
        int? learnedIslandSpawn = null)
    {
        var bubbles = BubbleObjectScanner.Scan(mem);

        // Routing-Cell-Check (für die Richtungs-Quelle bei Spawn auf Conditional o.ä.)
        var cellType = new Dictionary<int, MechanismType>();
        foreach (var b in bubbles)
        {
            var regs = b.AsRegsRecord();
            if (regs.F1 < 12 && regs.F2 < 13)
                cellType[regs.PositionIndex] = MechanismClassifier.Classify(regs);
        }
        bool IsRouting(int pos) => cellType.TryGetValue(pos, out var t) &&
            (t is MechanismType.Conditional or MechanismType.StaticDeflector or MechanismType.SwitchActivated);

        // Maschinen-Objekte (für dirCode + Pixel-Rang). Die engine-Cell-Pixel-Tabelle
        // taugt NICHT für die Maschinen-Position: die Maschinen-SPRITE-Pixel liegen am
        // Spielfeld-Rand (auf Stein-Zellen) — das ist NICHT die Spawn-Zelle, in die der
        // ZB geworfen wird. Deshalb hardcoded Spawn-Cells für die Position.
        var placements = DetectMachines(mem, bubbles);

        var mapped = BubblewonderSpawnMappings.GetSpawnCells(regsId);
        IReadOnlyList<int>? spawnCells = mapped is { Count: > 0 } ? mapped
            : (liveSpawnPositions is { Count: > 0 } ? liveSpawnPositions : null);
        // Live gelernten Insel-Spawn dazumergen: Werfer bleiben hardcoded, die
        // Insel-Spawn-Zelle ist NICHT statisch (Mining: variant-abhängig) und kommt
        // aus Beobachtung+Persistenz. So wird das Gelernte auch WIRKLICH benutzt (früher
        // ignorierte `mapped ?? live` es — ein Grund warum das Lernen nicht hielt).
        if (learnedIslandSpawn is { } isp && isp >= 0 && (spawnCells is null || !spawnCells.Contains(isp)))
            spawnCells = (spawnCells ?? Array.Empty<int>()).Append(isp).ToList();
        if (spawnCells is null || spawnCells.Count == 0) return new();

        // Insel-Erkennung: gelernte Insel-Zelle (verifiziert via Re-Launch) ODER
        // Eckzonen-Heuristik (für Layouts deren Insel-Spawn zufällig in der Ecke liegt).
        bool IsIsland(int pos) => pos == learnedIslandSpawn || BubblewonderSpawnMappings.IsIslandCell(pos);

        // Zuordnung Maschinen-Objekt ↔ Spawn-Cell + Insel-Status.
        // PRIMÄR (verifiziert 2026-05-29): TargetIdx (+0x8a) trennt Insel (!=0) von
        // Werfern (==0). Innerhalb jeder Gruppe Pixel-x-Rang ↔ Cell-(row,col)-Rang →
        // gibt Richtung UND Insel-Status je Cell. Robuster als der frühere Gesamt-x-Rang,
        // der in 16606 Insel/Werfer vertauschte.
        // FALLBACK (TargetIdx nicht lesbar / Anzahl-Mismatch): Gesamt-x-Rang + Eckzone.
        var dirByCell = new Dictionary<int, Direction>();
        var islandByCell = new Dictionary<int, bool>();
        var spriteByCell = new Dictionary<int, (int X, int Y)>();

        bool targetIdxOk = placements.Count == spawnCells.Count
                           && placements.All(p => p.TargetIdx >= 0);
        int detectedIslands = placements.Count(p => p.IsIsland);
        int cellIslands = spawnCells.Count(IsIsland);

        if (targetIdxOk && detectedIslands == cellIslands)
        {
            void AssignGroup(IEnumerable<MachinePlacement> ps, IEnumerable<int> cells, bool island)
            {
                var pl = ps.OrderBy(p => p.Px).ToList();
                var cl = cells.OrderBy(c => c / GridCols).ThenBy(c => c % GridCols).ToList();
                for (int r = 0; r < pl.Count && r < cl.Count; r++)
                {
                    dirByCell[cl[r]] = pl[r].Direction;
                    islandByCell[cl[r]] = island;
                    spriteByCell[cl[r]] = (pl[r].Px, pl[r].Py);
                }
            }
            AssignGroup(placements.Where(p => p.IsIsland),
                        spawnCells.Where(IsIsland), true);
            AssignGroup(placements.Where(p => !p.IsIsland),
                        spawnCells.Where(c => !IsIsland(c)), false);
        }
        else if (placements.Count == spawnCells.Count)
        {
            var objsByX = placements.OrderBy(p => p.Px).ToList();
            var cellsByRow = spawnCells.OrderBy(c => c / GridCols).ThenBy(c => c % GridCols).ToList();
            for (int r = 0; r < cellsByRow.Count; r++)
            {
                dirByCell[cellsByRow[r]] = objsByX[r].Direction;
                spriteByCell[cellsByRow[r]] = (objsByX[r].Px, objsByX[r].Py);
            }
        }

        var fb = new List<MachineModel>(spawnCells.Count);
        for (int i = 0; i < spawnCells.Count; i++)
        {
            int pos = spawnCells[i];
            Direction dir = ResolveSpawnDirection(pos, dirByCell, placements);
            if (!IsRouting(pos) && liveSpawnDirections is not null
                && liveSpawnDirections.TryGetValue(pos, out var liveDir))
                dir = liveDir;
            bool isIsland = islandByCell.TryGetValue(pos, out var isl)
                ? isl : IsIsland(pos);
            var (sx, sy) = spriteByCell.TryGetValue(pos, out var sp) ? sp : (-1, -1);
            fb.Add(new MachineModel(i, pos, dir, isIsland, sx, sy));
        }
        return fb;
    }

    /// <summary>Spawn-Richtung einer Wurf-Zelle bestimmen. PRIMÄR die per Insel/Pixel-Rang
    /// zugeordnete Richtung (<paramref name="dirByCell"/>). FALLBACK bei Count-Mismatch
    /// (Maschinen-Erkennung ≠ Spawn-Zellen, z.B. REGS 16606: DetectMachines findet 3 Objekte
    /// mit Müll-TargetIdx → alle „Insel" → 3≠2 Spawn-Zellen → keine Rang-Zuordnung): statt
    /// blind <see cref="Direction.Down"/> die Richtung des GEOMETRISCH NÄCHSTEN Maschinen-
    /// Sprites nehmen. Beleg memdump-162215: (2,8)-Werfer ist Sprite (2,10)←Left → der ZB
    /// läuft (2,10)→(2,9)→(2,8)→… Left, NICHT Down. Das Modell hatte Down vorhergesagt
    /// (→ tot/scort), real Left (→ Insel) → erster Plan falsch → „Abweichung"/oberer Pfad.
    /// Rein additiv: greift NUR wenn dirByCell die Zelle nicht kennt → kann korrekt
    /// zugeordnete Runden nicht verschlechtern (dort wird der Fallback nie erreicht).</summary>
    internal static Direction ResolveSpawnDirection(
        int pos, IReadOnlyDictionary<int, Direction> dirByCell,
        IReadOnlyList<MachinePlacement> placements)
    {
        if (dirByCell.TryGetValue(pos, out var ranked)) return ranked;
        if (placements.Count == 0) return Direction.Down;
        int pr = pos / GridCols, pc = pos % GridCols;
        return placements
            .OrderBy(p => Math.Abs(p.CellPos / GridCols - pr) + Math.Abs(p.CellPos % GridCols - pc))
            .First().Direction;
    }

    private static void MarkIslandMachineCells(
        Dictionary<int, CellModel> cells, IReadOnlyList<MachineModel> machines)
    {
        foreach (var machine in machines)
        {
            if (!machine.IsIsland) continue;
            if (cells.TryGetValue(machine.StartCellIndex, out var cell))
            {
                cells[machine.StartCellIndex] = cell with
                {
                    IsIslandMachine = true,
                    MachineIdx = machine.Index,
                };
            }
        }
    }

    private static GridState BuildGridState(IReadOnlyList<BubbleObject> bubbles)
    {
        var state = new GridState();
        foreach (var bubble in bubbles)
        {
            var regs = bubble.AsRegsRecord();
            if (!IsValidGridPosition(regs)) continue;
            int pos = regs.PositionIndex;
            switch (regs.F0)
            {
                case 4:
                    state.SwitchStateByCell[pos] = bubble.SwitchStateIndex;
                    break;
                case 5:
                    if (bubble.StickyTrappedZb != 0)
                    {
                        // Live-Memory liefert nur die HeaderId — Attribute sind
                        // an dieser Stelle unbekannt (kommen aus dem Pool-Read).
                        // Stub-SimZb genügt für reine State-Repräsentation;
                        // Folge-Simulationen müssen die volle SimZb von außen
                        // einsetzen wenn Conditional-Routing nötig wird.
                        state.StickyTrappedByCell[pos] =
                            new SimZb(bubble.StickyTrappedZb, 0, 0, 0, 0);
                    }
                    // StickyEntryDirByCell ist nicht aus Memory ablesbar — die
                    // Engine speichert die Eintrittsrichtung nicht explizit.
                    // Beim Live-Lesen verloren, beim Simulieren neu gesetzt.
                    break;
            }
        }
        return state;
    }

    private static bool IsValidGridPosition(RegsRecord regs) =>
        regs.F1 < GridRows && regs.F2 < GridCols;
}
