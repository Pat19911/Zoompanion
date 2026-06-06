namespace ZoombiniHelper;

/// <summary>
/// Snapshot of one zoombini in the active group's pool. All offsets are
/// relative to <see cref="Address"/> (= the zoombini record proper, after
/// the engine list-node header):
///   +0x00..+0x01  word: sprite-id
///   +0x1A..+0x1B  word: current screen-Y (0 while off-stage)
///   +0xC0..+0xC3  bytes: Hair, Eyes, Nose, Feet variant indices (1..5)
///   +0xE0..+0xE1  word: Captain Cajun current-seat backref (1-based,
///                       0 = not on a seat). Verified at 0x004137A7.
///
/// <see cref="HeaderId"/> is the engine-assigned per-zb identity word from
/// the list-node header (offset 0x1A in the 0x30-byte header — NOT in the
/// payload). Stable across the puzzle session, masked to 0xFFFD while held.
/// Used by Stone Rise to match placed-slot tile.w1 against the actual zb
/// without ambiguity even when several zbs in the pool share attributes.
///
/// <see cref="CajunPlacedFlag"/> is non-zero (typically 1) when a zb is
/// currently placed on a ferry seat (low byte of word at <c>node + 0x12C</c>).
/// Empirically reliable across multiple dumps.
///
/// <see cref="CajunSeatBackref"/> turned out to NOT be reliable — neither
/// +0xE0/+0xE2 nor +0x12D actually carry the seat index. The high byte of
/// +0x12C is an animation/timer counter that changes per tick. The Cajun
/// engine apparently doesn't expose the seat number anywhere reachable
/// from the zb record. We map placed zbs to seats geometrically instead,
/// using <see cref="ScreenX"/>/<see cref="ScreenY"/> matched against the
/// engine's per-seat position table at <c>0x4A4018</c>.
///
/// <see cref="ScreenX"/>/<see cref="ScreenY"/> is the zb sprite's current
/// screen position (in engine coords). Read from the payload at +0x18/+0x1A.
/// </summary>
public readonly record struct PoolMember(
    nint Address,
    byte Hair, byte Eyes, byte Nose, byte Feet,
    ushort YPosition,
    ushort SpriteId,
    ushort HeaderId = 0,
    ushort CajunSeatBackref = 0,
    byte CajunPlacedFlag = 0,
    ushort ScreenX = 0,
    ushort ScreenY = 0,
    // Engine-Handle (+0x20). 0x00000001 = im Pool (verfügbar), 0x04008001 =
    // losgeschickt (auf dem Grid: in Falle / gescort / geparkt), 0x04001001 =
    // gerade hochgehoben. Für Bubblewonder entscheidend, um echten Pool von
    // losgeschickten ZBs zu trennen.
    uint Handle = 0,
    // Grid-Position des ZB (+0x72=row, +0x74=col). Nur für losgeschickte ZBs
    // (Handle 0x04008001) sinnvoll — Pool-ZBs sind off-grid. 0xFFFF = unbekannt.
    ushort GridRow = 0xFFFF,
    ushort GridCol = 0xFFFF,
    // Bewegungsrichtung (+0x58): 0=Left,1=Down,2=Right,3=Up (F-Bit-Kodierung).
    // Disassembly-verifiziert (v2 fn 0x42a7a0). Eintrittsrichtung eines in einer
    // Klebefalle steckenden ZBs. 0xFFFF = unbekannt.
    ushort MovementDirRaw = 0xFFFF,
    // Outcome-Typ (+0x76) = celltype − 0x14 (disasm-verifiziert, fn 0x425f30):
    //   0 = Start/Pool/in-transit, 1/2 = auf Zwischenstation (INSEL geparkt),
    //   3 = Ziel (gescort). STABIL — anders als die Grid-Position (+0x72/74), die
    //   auf (0,0) flackert. Primäres Insel-Signal im Klassifikator. 0xFFFF = unbekannt.
    ushort OutcomeType = 0xFFFF);

/// <summary>
/// Gruppiert Pool-ZBs nach y-Position in Cluster (= verschiedene Pools/Inseln).
/// In Bubblewonder-Layouts mit Insel-Maschinen sind geparkte ZBs weiterhin
/// im PoolScanner sichtbar, aber mit deutlich höherem y (~500+ statt ~100).
/// Y-Lücken > 50 markieren einen neuen Cluster.
/// </summary>
public static class PoolClusterer
{
    public const int ClusterGapThreshold = 50;

    public static IReadOnlyList<IReadOnlyList<PoolMember>> Cluster(IReadOnlyList<PoolMember> pool)
    {
        if (pool.Count == 0) return Array.Empty<IReadOnlyList<PoolMember>>();
        var sorted = pool.OrderBy(z => z.YPosition).ToList();
        var clusters = new List<List<PoolMember>>();
        var current = new List<PoolMember> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            int gap = sorted[i].YPosition - sorted[i - 1].YPosition;
            if (gap > ClusterGapThreshold)
            {
                clusters.Add(current);
                current = new List<PoolMember>();
            }
            current.Add(sorted[i]);
        }
        clusters.Add(current);
        return clusters.Cast<IReadOnlyList<PoolMember>>().ToList();
    }

    /// <summary>Trennt den Hauptpool (= Cluster 0, lowest y) von Insel-geparkten
    /// ZBs (alle anderen Cluster).</summary>
    public static (IReadOnlyList<PoolMember> Hauptpool, IReadOnlyList<PoolMember> ParkedOnIslands)
        SplitMainAndIslands(IReadOnlyList<PoolMember> pool)
    {
        var clusters = Cluster(pool);
        if (clusters.Count == 0)
            return (Array.Empty<PoolMember>(), Array.Empty<PoolMember>());
        var main = clusters[0];
        var parked = clusters.Skip(1).SelectMany(c => c).ToList();
        return (main, parked);
    }
}

/// <summary>
/// Enumerates the active puzzle's zoombini pool. The pool is the subset of
/// the engine object list (see <see cref="EngineObjectList"/>) whose nodes
/// carry <c>handle == 0x00000001</c> and have plausible attribute bytes —
/// every other entry is a sprite, drag helper, or UI overlay.
///
/// Verified live in 5 puzzles (Cliffs / Pizza / Caves / Fleens / Mirror)
/// and across variable group sizes (16 down to 2).
/// </summary>
public static class PoolScanner
{
    /// <summary>handle@+0x20 markers we recognise as a pool zoombini —
    /// the single source of truth is <see cref="ZoombiniHandle.All"/>. We keep
    /// the set explicit (rather than masking on bit 0) to avoid sweeping in
    /// unrelated engine objects that happen to set the low bit; the attribute
    /// validation in <see cref="TryReadZoombini"/> is the second guard.</summary>
    private static readonly uint[] KnownPoolZoombiniHandles = ZoombiniHandle.All;

    /// <summary>Per-node read window: large enough to reach +0x12C (= the
    /// Cajun "placed" flag). Header is 0x30, payload extends past 0x100.</summary>
    private const int NodeReadSize = 0x130;

    public static List<PoolMember> Scan(IMemoryReader mem)
    {
        var found = new List<PoolMember>();
        foreach (var node in EngineObjectList.Walk(mem, NodeReadSize))
            if (TryReadZoombini(node, out var pm)) found.Add(pm);

        // Top-to-bottom on screen. Off-stage zoombinis (y=0) come first —
        // that's fine, they're still part of the pool.
        found.Sort((a, b) => a.YPosition.CompareTo(b.YPosition));
        return found;
    }

    private static bool TryReadZoombini(EngineNode node, out PoolMember pm)
    {
        pm = default;
        if (Array.IndexOf(KnownPoolZoombiniHandles, node.Handle) < 0) return false;

        int recOff = EngineObjectList.HeaderSize;
        byte h = node.Bytes[recOff + 0xC0];
        byte e = node.Bytes[recOff + 0xC1];
        byte n = node.Bytes[recOff + 0xC2];
        byte f = node.Bytes[recOff + 0xC3];
        if (h is < 1 or > 5 || e is < 1 or > 5 || n is < 1 or > 5 || f is < 1 or > 5)
            return false;

        ushort sprite = BitConverter.ToUInt16(node.Bytes, recOff + 0x00);
        ushort yPos   = BitConverter.ToUInt16(node.Bytes, recOff + 0x1A);
        // Screen X/Y for placed-zb ↔ seat geometric matching.
        ushort screenX = BitConverter.ToUInt16(node.Bytes, recOff + 0x18);
        ushort screenY = yPos;
        // Header-id at offset 0x1A in the 0x30-byte node header (BEFORE the
        // payload). This is the engine's per-zb identity word; Stone Rise
        // uses it to mark which zb sits in each placed slot via tile.w1.
        ushort headerId = BitConverter.ToUInt16(node.Bytes, 0x1A);
        // Captain Cajun fields packed into the word at +0x12C:
        //   byte +0x12C = placement flag (1 when on a seat, else 0)
        //   byte +0x12D = 0-based seat index
        // Empirically verified in memdump-184849: 4 placed zbs had words
        // 0x0301 / 0x0901 / 0x0001 / 0x0201 at +0x12C — low byte = 1
        // (= placed) and high byte = distinct seat numbers (3/9/0/2).
        // The earlier hypothesis that +0xE2 was the seat-backref was wrong:
        // +0xE2 is a constant 0x0001 for any placed zb.
        byte cajunPlaced = node.Bytes.Length > 0x12C
            ? node.Bytes[0x12C] : (byte)0;
        byte cajunSeat0Based = node.Bytes.Length > 0x12D
            ? node.Bytes[0x12D] : (byte)0;
        // Expose as 1-based to match the prior contract (renderer treats 0
        // as "unknown", subtracts 1 for the seat index).
        ushort cajunSeatBackref = cajunPlaced != 0 ? (ushort)(cajunSeat0Based + 1) : (ushort)0;
        // Grid-Position (+0x72=row, +0x74=col) — absolute Objekt-Offsets.
        // Nur für losgeschickte ZBs (Bubblewonder) sinnvoll; sonst harmlos.
        ushort gridRow = node.Bytes.Length > 0x73
            ? BitConverter.ToUInt16(node.Bytes, 0x72) : (ushort)0xFFFF;
        ushort gridCol = node.Bytes.Length > 0x75
            ? BitConverter.ToUInt16(node.Bytes, 0x74) : (ushort)0xFFFF;
        // +0x58 (word) = aktuelle Bewegungsrichtung des ZB (0=Left,1=Down,2=Right,
        // 3=Up — F-Bit-Kodierung). Disassembly-verifiziert in v2 fn 0x42a7a0
        // (befreit ZB → springt über Jump-Table [+0x58] in genau diese Richtung).
        // Für einen in einer Klebefalle steckenden ZB ist das die Eintrittsrichtung,
        // in die er nach Befreiung weiterläuft. 0xFFFF = unbekannt/kein gültiger Wert.
        ushort moveDir = node.Bytes.Length > 0x59
            ? BitConverter.ToUInt16(node.Bytes, 0x58) : (ushort)0xFFFF;
        // +0x76 = Outcome-Typ (celltype−0x14): stabiles Insel-Signal (1/2=Insel, 3=Ziel).
        ushort outcome76 = node.Bytes.Length > 0x77
            ? BitConverter.ToUInt16(node.Bytes, 0x76) : (ushort)0xFFFF;
        pm = new PoolMember(node.Address + recOff, h, e, n, f, yPos, sprite,
                            headerId, cajunSeatBackref, cajunPlaced, screenX, screenY,
                            node.Handle, gridRow, gridCol, moveDir, outcome76);
        return true;
    }

    /// <summary>Bridge recommendation for a zoombini given the current cliff
    /// rules: matches any allergy → must go to the accepting bridge.</summary>
    public static bool MatchesAnyAllergy(PoolMember zb, IReadOnlyList<CliffState.Rule> rules)
    {
        foreach (var rule in rules)
        {
            byte attr = rule.Type switch { 1 => zb.Hair, 2 => zb.Eyes, 3 => zb.Nose, 4 => zb.Feet, _ => 0 };
            if (rule.Matches(attr)) return true;
        }
        return false;
    }
}
