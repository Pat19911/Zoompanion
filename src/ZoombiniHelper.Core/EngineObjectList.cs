namespace ZoombiniHelper;

/// <summary>One node in the engine's global object list. Bytes hold the raw
/// node payload (header + record), so consumers can read further fields
/// without a second memory round-trip.</summary>
public readonly record struct EngineNode(nint Address, uint Next, uint Handle, byte[] Bytes);

/// <summary>
/// The Riverdeep engine keeps every visible game object — pool zoombinis,
/// sprites, drag helpers, UI overlays — in a singly-linked list whose head
/// pointer lives at <c>*(0x004A35C0)</c>. Each node has a 0x30-byte header
/// (next-pointer at +0x00, handle at +0x20) followed by an object-specific
/// payload. Pool zoombinis use the 0xC4-byte zoombini record layout starting
/// at +0x30 with attribute bytes at +0x30+0xC0.
///
/// This class is the one place that knows how to walk that list. Pool
/// scanning and the F12 diagnostic both go through <see cref="Walk"/>; if
/// the head moves or the header layout ever changes, only this file does.
/// </summary>
public static class EngineObjectList
{
    /// <summary>Engine global. Verified in v2 disasm and live across all
    /// puzzles — diagnostic walks always start here.</summary>
    public const nint HeadAddress = 0x004A35C0;

    /// <summary>Bytes of header before the object-specific payload begins.
    /// Pool record layout offsets are relative to header end, i.e. attribute
    /// bytes for a zoombini sit at <c>node + HeaderSize + 0xC0</c>.</summary>
    public const int HeaderSize = 0x30;

    /// <summary>Cycle/runaway guard. The real list never exceeds ~64 nodes;
    /// we cap higher so a corrupted next-pointer terminates the walk cleanly
    /// instead of looping the whole memory space.</summary>
    private const int MaxDepth = 256;

    /// <summary>Walk the list, yielding every node up to <paramref name="readPerNode"/>
    /// bytes deep. Stops on null/cyclic pointers, on a failed read, or after
    /// <see cref="MaxDepth"/> nodes — whichever comes first. Returns an empty
    /// sequence if the head is unset (between puzzles).</summary>
    public static IEnumerable<EngineNode> Walk(IMemoryReader mem, int readPerNode)
    {
        var headBytes = mem.ReadBytes(HeadAddress, 4);
        if (headBytes is null) yield break;
        uint head = BitConverter.ToUInt32(headBytes, 0);
        if (!LooksLikePointer(head)) yield break;

        var seen = new HashSet<nint>();
        nint node = (nint)head;
        for (int i = 0; i < MaxDepth && node != 0; i++)
        {
            if (!seen.Add(node)) yield break;
            var raw = mem.ReadBytes(node, readPerNode);
            if (raw is null) yield break;
            uint next   = BitConverter.ToUInt32(raw, 0x00);
            uint handle = BitConverter.ToUInt32(raw, 0x20);
            yield return new EngineNode(node, next, handle, raw);
            node = (nint)next;
        }
    }

    /// <summary>Anything above the first 64 KB and below the kernel boundary.
    /// This is wider than just "the heap": engine objects have been seen at
    /// VAs as low as 0x007C8000 (lives in a separate low-address pool, not
    /// the main 0x04xxxxxx heap). Win32 ReadProcessMemory returns null for
    /// unmapped pages, so a broad filter is safe — false positives stop the
    /// walk on the next read.</summary>
    private static bool LooksLikePointer(uint v) =>
        v >= 0x00010000 && v < 0x80000000;
}
