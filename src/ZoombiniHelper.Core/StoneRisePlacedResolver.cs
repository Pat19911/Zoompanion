namespace ZoombiniHelper;

/// <summary>
/// Resolves which zoombini sits in each filled Stone-Rise slot by matching
/// the slot's <c>w1</c> field against the per-zb identity word at offset
/// <b>+0x1A in the node HEADER</b> (not payload!).
///
/// <para>Mechanism (verified live against memdump-123046):</para>
/// <list type="bullet">
///   <item>Engine setter at 0x0043A4F0 writes <c>tile.w1 = [edx + 0x1A]</c>
///   where edx is the placed zb record. The +0x1A field lives inside the
///   0x30-byte engine list-node header — NOT inside the 0xC4-byte payload.</item>
///   <item>Engine lookup at 0x00456380 walks the list comparing the arg
///   against <c>[node + 0x1A]</c> on every node — same offset.</item>
///   <item>Empirical correlation in memdump-123046: tile[40] type=507 w1=15
///   ↔ zb-node hdr1A=0x000F = (2,2,3,5); tile[55] type=508 w1=7 ↔
///   zb-node hdr1A=0x0007 = (3,2,3,2).</item>
/// </list>
///
/// <para>This was the offset that broke the previous version: the payload's
/// +0x1A holds the screen-y position (used for tray sorting in PoolScanner),
/// which is unrelated to placement identity. The header's +0x1A is the
/// stable engine-assigned id.</para>
/// </summary>
public static class StoneRisePlacedResolver
{
    /// <summary>Offset of the per-zb identity word inside the engine list-node
    /// header. NOT inside the payload — directly within the 0x30-byte
    /// header that precedes the zoombini record.</summary>
    private const int HeaderIdOffset = 0x1A;

    private const int RecordOffset = EngineObjectList.HeaderSize;
    private const int RecordSize = 0xC4;

    /// <summary>Engine-assigned hdr1A value used while a zb is being held.
    /// When a zb is picked up, its identity word is overwritten with this
    /// sentinel; when released, the original id is restored. So a held zb
    /// won't match its tile.w1 — we patch that case below.</summary>
    private const ushort HeldSentinelId = 0xFFFD;

    public static Dictionary<int, StoneRisePlacementTracker.ZbAttrs>
        Resolve(StoneRiseState state, IMemoryReader mem)
    {
        var result = new Dictionary<int, StoneRisePlacementTracker.ZbAttrs>();
        var filled = state.PairSlots
            .Where(p => p.IsFilled && p.PlacedZbId != 0)
            .ToList();
        if (filled.Count == 0) return result;

        var idToAttrs = new Dictionary<ushort, StoneRisePlacementTracker.ZbAttrs>();
        var heldAttrsList = new List<StoneRisePlacementTracker.ZbAttrs>();
        foreach (var node in EngineObjectList.Walk(mem, RecordOffset + RecordSize))
        {
            if (node.Bytes.Length < RecordOffset + RecordSize) continue;
            byte h = node.Bytes[RecordOffset + 0xC0];
            byte e = node.Bytes[RecordOffset + 0xC1];
            byte n = node.Bytes[RecordOffset + 0xC2];
            byte f = node.Bytes[RecordOffset + 0xC3];
            if (h is < 1 or > 5 || e is < 1 or > 5 || n is < 1 or > 5 || f is < 1 or > 5) continue;
            ushort id = BitConverter.ToUInt16(node.Bytes, HeaderIdOffset);
            var attrs = new StoneRisePlacementTracker.ZbAttrs(h, e, n, f);
            if (id == HeldSentinelId) { heldAttrsList.Add(attrs); continue; }
            if (id == 0) continue;
            idToAttrs[id] = attrs;
        }

        var unmatched = new List<int>();
        foreach (var slot in filled)
        {
            if (idToAttrs.TryGetValue(slot.PlacedZbId, out var attrs))
                result[slot.TileIndex] = attrs;
            else
                unmatched.Add(slot.TileIndex);
        }

        // Held-zb backfill: if exactly one slot couldn't be matched and there's
        // exactly one held zb (its hdr1A is masked to 0xFFFD while held, so
        // it'd never match by id lookup), assume that's the one currently
        // sitting in the unmatched slot. Verified live in memdump-134400 vs
        // memdump-134408: tile[74] w1=4 was unmatched in 134400 because the
        // held zb (4,2,5,4) had hdr1A=FFFD; in 134408, after a different zb
        // got picked up, (4,2,5,4) returned to the pool with hdr1A=4 and
        // matched tile[74] directly.
        if (unmatched.Count == 1 && heldAttrsList.Count == 1)
            result[unmatched[0]] = heldAttrsList[0];

        return result;
    }
}
