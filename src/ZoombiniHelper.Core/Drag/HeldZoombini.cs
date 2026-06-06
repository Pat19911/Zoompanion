namespace ZoombiniHelper.Drag;

/// <summary>
/// Identifies the zoombini the user is currently dragging.
///
/// Verified live (memdump-095458 + memdump-101131) and traced in v2 disasm:
/// the engine's <c>begin_drag</c> at <c>0x00448F30</c> ORs the picked
/// zoombini's handle with <c>0x04001000</c>:
/// <code>
///   0x00448FD7  mov  eax, [edi+0x20]      ; orig handle (= 0x00000001 for pool zb)
///   0x00448FDE  or   eax, 0x04001000      ; set drag bits
///   0x00448FEA  mov  [edi+0x20], eax      ; → handle becomes 0x04001001
/// </code>
///
/// Two gotchas discovered the hard way:
///  1. The drag-on flag at <c>0x00494522</c> is sticky — it's set BEFORE
///     find_object_at_cursor, so a click into empty space leaves it = 1.
///  2. The <c>0x04001000</c> bits are also sticky — after drop, the engine
///     does not clear them; only the next pickup OR's a different handle.
///
/// Robust answer: combine both. Held only if (a) drag-on flag is set AND
/// (b) some node still carries the marked handle. If both are true and the
/// node's attribute bytes are sane, that's the held zoombini.
/// </summary>
public static class HeldZoombini
{
    /// <summary>Per-puzzle drag flags. Cliff and Caves use different memory
    /// locations: Cliff sets <c>0x00494522 = 1</c> at its pickup site
    /// (<c>0x004068C7</c>), Caves sets <c>0x004A204C = 1</c> at its pickup
    /// site (<c>0x004510A1</c>). The drag is active if any of them is set.
    /// Other puzzles (Pizza, Hotel, etc.) likely have their own flags too —
    /// add as discovered.</summary>
    private static readonly nint[] DragFlagAddrs = { 0x00494522, 0x004A204C };

    /// <summary>Bits the engine OR's into the held node's handle. The original
    /// pool-zb handle is 0x00000001, so the typical value is 0x04001001 — but
    /// we mask-test instead of equality so a different orig handle (e.g. in
    /// puzzles we haven't seen yet) still matches.</summary>
    private const uint HeldHandleMask = 0x04001000;
    private const int  NodeReadSize   = EngineObjectList.HeaderSize + 0xC4;

    /// <summary>Engine writes <c>0xFFFD</c> (= -3) to Y when picking a node up
    /// (<c>begin_drag</c> at <c>0x00448FE4: mov word ptr [edi+0x1a], 0xfffd</c>).
    /// More reliable than handle bits because the engine resets Y on drop while
    /// the handle bits stay sticky.</summary>
    private const ushort OffStageY = 0xFFFD;

    public static bool IsDragActive(IMemoryReader mem)
    {
        foreach (var addr in DragFlagAddrs)
            if (mem.ReadWord(addr) != 0) return true;
        return false;
    }

    /// <summary>Find the currently held zoombini. Tries (in order):
    ///  1. node with <c>handle &amp; 0x04001000</c> AND valid pool-record attrs
    ///  2. node with pool-record y == 0xFFFD (off-stage marker, set by begin_drag)
    /// Both checks include the 1..5 attribute sanity filter so we never return
    /// a non-zb node. Independent of drag flags — those are puzzle-specific.</summary>
    public static PoolMember? Find(IMemoryReader mem)
    {
        // y=0xFFFD ist ein STICKY-Marker — Engine setzt ihn beim Pickup, RESETTET
        // ihn aber NICHT immer beim Drop. Daher allein nicht zuverlässig.
        // Nur wenn auch ein Drag-Flag oder Handle-Bit gesetzt ist, gilt y=0xFFFD
        // als echter Drag-Indikator.
        bool dragFlagSet = IsDragActive(mem);

        PoolMember? viaHandle = null;
        PoolMember? viaY      = null;
        int handleCount = 0;
        int recOff = EngineObjectList.HeaderSize;

        foreach (var node in EngineObjectList.Walk(mem, NodeReadSize))
        {
            byte h = node.Bytes[recOff + 0xC0];
            byte e = node.Bytes[recOff + 0xC1];
            byte n = node.Bytes[recOff + 0xC2];
            byte f = node.Bytes[recOff + 0xC3];
            bool validAttrs = h is >= 1 and <= 5 && e is >= 1 and <= 5
                           && n is >= 1 and <= 5 && f is >= 1 and <= 5;
            if (!validAttrs) continue;

            ushort sprite = BitConverter.ToUInt16(node.Bytes, recOff + 0x00);
            ushort yPos   = BitConverter.ToUInt16(node.Bytes, recOff + 0x1A);
            // HeaderId aus dem Engine-Node-Header (offset 0x1A im 0x30-Byte
            // Header VOR dem Record-Offset) — sonst ist HeaderId immer 0 und
            // Match gegen Solver-Plan funktioniert nie.
            ushort headerId = BitConverter.ToUInt16(node.Bytes, 0x1A);
            var pm = new PoolMember(node.Address + recOff, h, e, n, f, yPos, sprite, headerId);

            if ((node.Handle & HeldHandleMask) == HeldHandleMask) { viaHandle ??= pm; handleCount++; }
            if (yPos == OffStageY)                                viaY      ??= pm;
        }

        // Verschiedene Puzzles markieren das Hochheben unterschiedlich (gleiche
        // Engine-begin_drag, aber unterschiedliches Aufräum-Verhalten):
        //   - Cliff/Caves: setzen y=0xFFFD UND ein puzzle-spezifisches Drag-Flag;
        //     das Handle-Bit bleibt nach dem Drop STICKY → Flag/y nötig zur
        //     Unterscheidung "echt gehalten" vs "Marker hängt noch".
        //   - Bubblewonder: setzt NUR das Handle-Bit (y bleibt Bildschirm-Pos),
        //     räumt es aber beim Drop wieder weg (live verifiziert: ohne Drag
        //     0 Kandidaten, mit Drag genau 1).
        // Reihenfolge der Indikatoren von stärkstem zu schwächstem:
        bool sameZbBoth = viaHandle is not null && viaY is not null
                       && viaHandle.Value.Address == viaY.Value.Address;
        if (sameZbBoth) return viaHandle;                            // Handle + y
        if (dragFlagSet && viaHandle is not null) return viaHandle;  // Flag + Handle
        if (dragFlagSet && viaY is not null) return viaY;            // Flag + y
        // Handle-Bit allein, aber nur wenn EINDEUTIG (genau ein Kandidat). Deckt
        // Puzzles ab die das Bit beim Drop clearen (Bubblewonder). Bei sticky-
        // Puzzles akkumulieren mehrere Kandidaten → greift dort nicht fälschlich.
        if (handleCount == 1 && viaHandle is not null) return viaHandle;
        return null;
    }
}
