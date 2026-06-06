using Xunit;
using ZoombiniHelper;
using ZoombiniHelper.Bubblewonder;

namespace ZoombiniHelper.Tests;

public class BubbleObjectScannerTests
{
    private sealed class FakeMem : IMemoryReader
    {
        private readonly Dictionary<nint, byte[]> _data = new();
        public void Set(nint va, byte[] bytes) => _data[va] = bytes;
        public void SetDword(nint va, uint v) => Set(va, BitConverter.GetBytes(v));
        public byte[]? ReadBytes(nint va, int n)
        {
            if (_data.TryGetValue(va, out var b))
                return b.Length >= n ? b[..n] : Pad(b, n);
            return new byte[n];
        }
        public ushort ReadWord(nint va) => BitConverter.ToUInt16(ReadBytes(va, 2)!, 0);
        public byte ReadByte(nint va) => ReadBytes(va, 1)![0];
        private static byte[] Pad(byte[] src, int n) {
            var r = new byte[n]; Array.Copy(src, r, src.Length); return r;
        }
    }

    /// <summary>Helper: build a synthetic Bubble-Engine-Object node with the
    /// expected layout (handle=0x04188000, hdr1A at +0x1A, prop1 at +0x72,
    /// prop2 at +0x74, state at +0xC8, REGS-record at +0x78..+0x8C BE).</summary>
    private static byte[] MakeBubbleNode(ushort hdr1A, ushort prop1, ushort prop2,
                                          ushort state, ushort[] regsBE,
                                          byte activeFlag = 1, byte countdown = 5)
    {
        var node = new byte[BubbleObjectScanner.BytesPerNode];
        // next pointer @ 0x00, handle @ 0x20
        BitConverter.GetBytes(BubbleObjectScanner.BubbleHandle).CopyTo(node, 0x20);
        // hdr1A at +0x1A
        BitConverter.GetBytes(hdr1A).CopyTo(node, 0x1A);
        // prop1, prop2, state
        BitConverter.GetBytes(prop1).CopyTo(node, BubblewonderMemoryMap.BubbleProp1Offset);
        BitConverter.GetBytes(prop2).CopyTo(node, BubblewonderMemoryMap.BubbleProp2Offset);
        BitConverter.GetBytes(state).CopyTo(node, BubblewonderMemoryMap.BubbleStateOffset);
        node[BubblewonderMemoryMap.BubbleActiveFlagOffset] = activeFlag;
        node[BubblewonderMemoryMap.BubbleCountdownOffset] = countdown;
        // REGS-Record-Copy at +0x78 (Little-Endian — wie Live-Memory)
        for (int i = 0; i < 10; i++)
        {
            int off = BubblewonderMemoryMap.BubbleRegsCopyStart + i * 2;
            node[off]     = (byte)(regsBE[i] & 0xff);
            node[off + 1] = (byte)((regsBE[i] >> 8) & 0xff);
        }
        return node;
    }

    [Fact]
    public void Scan_NoEngineObjects_ReturnsEmpty()
    {
        var mem = new FakeMem();
        // Head pointer is null (default 0)
        Assert.Empty(BubbleObjectScanner.Scan(mem));
    }

    [Fact]
    public void Scan_SingleBubble_ExtractsAllFields()
    {
        var mem = new FakeMem();
        const uint nodeVa = 0x007E8000;  // realistic engine-heap address
        // Set linked-list head to point to our fake node
        mem.SetDword(EngineObjectList.HeadAddress, nodeVa);

        // prop1 (+0x72) and prop2 (+0x74) sit INSIDE REGS-Copy region (+0x6C..+0x7F),
        // mapping to REGS f3 and f4. So they will always equal those REGS values.
        // For this test: f3 = 5 (= prop1), f4 = 12 (= prop2 — though physically only
        // 0/1 in real data, we use distinct values to test the read).
        var regs = new ushort[] { 2, 4, 9, 5, 12, 0, 0, 0, 0, 0 };
        var node = MakeBubbleNode(
            hdr1A: 0x0019, prop1: 5, prop2: 12, state: 3,
            regsBE: regs, activeFlag: 1, countdown: 7);
        // Next-pointer (offset 0) = 0 → ends walk after this node
        mem.Set(unchecked((nint)nodeVa), node);

        var bubbles = BubbleObjectScanner.Scan(mem);
        Assert.Single(bubbles);
        var b = bubbles[0];
        Assert.Equal((ushort)0x0019, b.HeaderId);
        Assert.Equal(BubbleObjectScanner.BubbleHandle, b.Handle);
        Assert.Equal((ushort)5, b.Prop1);
        Assert.Equal((ushort)12, b.Prop2);
        Assert.Equal((ushort)3, b.State);
        Assert.True(b.IsReadyForMatch);
        Assert.True(b.IsActive);
        Assert.Equal((byte)7, b.Countdown);
        // REGS-Record-Copy: 10 BE words
        Assert.Equal(10, b.RegsRecordCopy.Count);
        Assert.Equal((ushort)2, b.RegsRecordCopy[0]);
        Assert.Equal((ushort)4, b.RegsRecordCopy[1]);
        Assert.Equal((ushort)9, b.RegsRecordCopy[2]);

        // AsRegsRecord conversion
        var rec = b.AsRegsRecord();
        Assert.Equal(2, rec.F0);
        Assert.Equal(ArrowDirection.Up, rec.Direction);
        // f0=2 → IsConditional (User-verifiziert 2026-05-01)
        Assert.True(rec.IsConditional);
    }

    [Fact]
    public void Scan_FiltersNonBubbleHandles()
    {
        var mem = new FakeMem();
        const uint nodeVa = 0x007E8000;  // realistic engine-heap address
        mem.SetDword(EngineObjectList.HeadAddress, nodeVa);

        var node = new byte[BubbleObjectScanner.BytesPerNode];
        // Wrong handle (e.g. ZB pool entry)
        BitConverter.GetBytes(0x00000001u).CopyTo(node, 0x20);
        mem.Set(unchecked((nint)nodeVa), node);

        Assert.Empty(BubbleObjectScanner.Scan(mem));
    }
}
