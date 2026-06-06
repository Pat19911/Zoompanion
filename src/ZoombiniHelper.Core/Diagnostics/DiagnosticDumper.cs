using System.Text;

namespace ZoombiniHelper.Diagnostics;

/// <summary>
/// All the heavy F12 memory-dump logic. Pure functions over IMemoryReader →
/// no UI thread, no Win32 specifics, fully unit-testable.
///
/// Each writer takes a StreamWriter so the caller controls file lifetime
/// and can interleave its own header lines (process name, PID, etc.).
/// </summary>
public static class DiagnosticDumper
{
    /// <summary>Hex + DWORD dump of an arbitrary memory region.</summary>
    public static void DumpRegion(StreamWriter sw, IMemoryReader mem, string label, nint va, int n)
    {
        sw.WriteLine($"--- {label} @ 0x{va:X8} ({n} bytes) ---");
        var raw = mem.ReadBytes(va, n);
        if (raw is null) { sw.WriteLine("  (read failed)"); return; }
        for (int row = 0; row < n; row += 16)
        {
            int len = Math.Min(16, n - row);
            var hex = new StringBuilder();
            for (int col = 0; col < len; col++) hex.Append($"{raw[row + col]:X2} ");
            sw.WriteLine($"  +0x{row:X2}  {hex}");
        }
        sw.WriteLine("  as DWORDs:");
        for (int i = 0; i + 4 <= n; i += 4)
        {
            uint dw = BitConverter.ToUInt32(raw, i);
            sw.WriteLine($"  +0x{i:X2}  0x{dw:X8}  ({dw})");
        }
    }

    /// <summary>Diagnostic walk over <see cref="EngineObjectList"/>. Reads
    /// 0x130 bytes per node so the held-flag at +0x12C and the state byte
    /// at +0x128 are visible alongside the attribute quadruplet at +0xC0.
    /// </summary>
    public static void WalkObjectList(StreamWriter sw, IMemoryReader mem)
    {
        const int NodeReadSize = 0x130;
        var headBytes = mem.ReadBytes(EngineObjectList.HeadAddress, 4);
        uint head = headBytes is null ? 0 : BitConverter.ToUInt32(headBytes, 0);
        sw.WriteLine($"  *(0x{EngineObjectList.HeadAddress:X8}) = 0x{head:X8}");

        int count = 0, heldHits = 0;
        foreach (var node in EngineObjectList.Walk(mem, NodeReadSize))
        {
            var raw = node.Bytes;
            // Pool-record attrs live at node+0xF0..0xF3 (= record+0xC0..0xC3 since
            // the record starts at node+0x30). Show them so we can spot where
            // the real held zoombini lives even when handle bits are sticky.
            byte ph = raw[0xF0], pe = raw[0xF1], pn = raw[0xF2], pf = raw[0xF3];
            ushort recY = BitConverter.ToUInt16(raw, 0x30 + 0x1A);
            // Engine lookup at 0x456380 walks this list comparing arg vs
            // word-at-(node+0x1A) — that's the 16-bit header field we need
            // to correlate against tile placement markers.
            ushort hdr1A = BitConverter.ToUInt16(raw, 0x1A);
            byte state128 = raw[0x128];
            byte held12C  = raw[0x12C];
            bool zbLike = ph is >= 1 and <= 5 && pe is >= 1 and <= 5 && pn is >= 1 and <= 5 && pf is >= 1 and <= 5;
            string zbMark = zbLike ? "  ZB" : "";
            string heldMark = (node.Handle & 0x04001000) == 0x04001000 ? "  ⭐ DRAG-MARKED" : "";
            if (held12C == 1) heldHits++;
            sw.WriteLine($"  [{count,2}] @ 0x{(uint)node.Address:X8}  next=0x{node.Next:X8}  handle=0x{node.Handle:X8}"
                       + $"  rec_attrs=({ph},{pe},{pn},{pf})  rec_y={recY,5}  +128={state128:X2}  +12C={held12C:X2}  hdr1A=0x{hdr1A:X4}{zbMark}{heldMark}");
            count++;
        }
        sw.WriteLine($"  (walked {count} node(s); {heldHits} with +12C=1)");
    }
}
