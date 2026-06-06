namespace ZoombiniHelper;

/// <summary>One Fleen creature on screen. Same attribute layout as a pool
/// zoombini (4 bytes at +0xC0..+0xC3), just a different handle marker.
/// The visual attribute values are the *permuted* form — i.e. they show
/// what the Fleen looks like, not what its target zoombini's attributes
/// originally were.
///
/// <para><see cref="TreeMarker"/> is set by <see cref="FleensState"/> after
/// the special-indices lookup: 1 if this fleen is one of the 3 tree targets,
/// 0 otherwise. Not read from the engine node itself.</para></summary>
public readonly record struct FleenMember(
    nint Address,
    byte A0, byte A1, byte A2, byte A3,
    ushort YPosition,
    ushort SpriteId,
    byte TreeMarker);

/// <summary>
/// Enumerates the 16 visible Fleens during a Fleens! puzzle round. They
/// share the engine object list (see <see cref="EngineObjectList"/>) with
/// pool zoombinis and other sprites, but use the dedicated marker
/// <c>handle == 0x04000002</c>.
///
/// The Fleens' attribute bytes are the *visual* state (after the engine
/// applied the round's permutation). Working out the inverse permutation
/// is <see cref="FleensSolver"/>'s job.
/// </summary>
public static class FleensScanner
{
    /// <summary>handle@+0x20 marker for a Fleen creature.</summary>
    public const uint FleenHandle = 0x04000002;

    /// <summary>Marker for the currently-active zoombini that the player has
    /// clicked and which is now walking on the field. Same record layout as
    /// a pool zoombini, just promoted to "active" by the engine.</summary>
    public const uint ActiveZoombiniHandle = 0x04000001;

    private const int NodeReadSize = EngineObjectList.HeaderSize + 0xC4;

    /// <summary>All Fleens currently on screen. Empty when not in a Fleens
    /// round (the handle simply doesn't appear elsewhere).</summary>
    public static List<FleenMember> ScanFleens(IMemoryReader mem)
    {
        var found = new List<FleenMember>();
        foreach (var node in EngineObjectList.Walk(mem, NodeReadSize))
            if (node.Handle == FleenHandle && TryRead(node, out var fm)) found.Add(fm);
        // y-sort: top to bottom on screen — gives stable ordering for tests.
        found.Sort((a, b) => a.YPosition.CompareTo(b.YPosition));
        return found;
    }

    /// <summary>Pool zoombinis plus active and dragged ones — every variant
    /// of a "real" zoombini node, regardless of how the engine has decorated
    /// the handle. Solver needs the complete set for collision-free matching.
    ///
    /// Three handle variants are valid: <c>0x00000001</c> (in pool),
    /// <c>0x04000001</c> (active on field), <c>0x04001001</c> (active +
    /// drag-marker). They differ only in the <c>0x04001000</c> decoration
    /// bits (drag bit + active bit), so we mask those out before comparing.
    ///
    /// <b>Order matters</b>: the list comes back in <em>engine-list order</em>
    /// (head-to-tail of the linked list). That order is stable across drag
    /// events — the y-position changes when the player picks up a zoombini
    /// (engine writes <c>0xFFFD</c>), so a y-sort would shuffle indices and
    /// break any "is this the special index" lookup. We trade visual sort
    /// order (the F12 dump can re-sort if it wants) for index stability.</summary>
    public static List<PoolMember> ScanAllZoombinis(IMemoryReader mem)
    {
        var found = new List<PoolMember>();
        foreach (var node in EngineObjectList.Walk(mem, NodeReadSize))
        {
            uint baseHandle = node.Handle & ~ZoombiniDecorationBits;
            if (baseHandle != BaseZoombiniHandle) continue;
            int recOff = EngineObjectList.HeaderSize;
            byte h = node.Bytes[recOff + 0xC0];
            byte e = node.Bytes[recOff + 0xC1];
            byte n = node.Bytes[recOff + 0xC2];
            byte f = node.Bytes[recOff + 0xC3];
            if (h is < 1 or > 5 || e is < 1 or > 5 || n is < 1 or > 5 || f is < 1 or > 5) continue;
            ushort sprite = BitConverter.ToUInt16(node.Bytes, recOff + 0x00);
            ushort yPos   = BitConverter.ToUInt16(node.Bytes, recOff + 0x1A);
            found.Add(new PoolMember(node.Address + recOff, h, e, n, f, yPos, sprite));
        }
        return found;
    }

    private const uint BaseZoombiniHandle      = 0x00000001;
    private const uint ZoombiniDecorationBits  = 0x04001000;

    private static bool TryRead(EngineNode node, out FleenMember fm)
    {
        fm = default;
        int recOff = EngineObjectList.HeaderSize;
        byte a0 = node.Bytes[recOff + 0xC0];
        byte a1 = node.Bytes[recOff + 0xC1];
        byte a2 = node.Bytes[recOff + 0xC2];
        byte a3 = node.Bytes[recOff + 0xC3];
        if (a0 is < 1 or > 5 || a1 is < 1 or > 5 || a2 is < 1 or > 5 || a3 is < 1 or > 5) return false;
        ushort sprite = BitConverter.ToUInt16(node.Bytes, recOff + 0x00);
        ushort yPos   = BitConverter.ToUInt16(node.Bytes, recOff + 0x1A);
        fm = new FleenMember(node.Address + recOff, a0, a1, a2, a3, yPos, sprite, TreeMarker: 0);
        return true;
    }
}
