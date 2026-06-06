using Xunit;
using ZoombiniHelper;
using ZoombiniHelper.Bubblewonder;

namespace ZoombiniHelper.Tests;

public class BubblewonderStateTests
{
    /// <summary>Konvertiert BE-encoded REGS-Bytes (wie aus maze2.mhk) zu LE
    /// (wie der Engine sie nach Loading im Memory hat). Pro 2-Byte-Word swappen.</summary>
    private static byte[] BeToLe(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        for (int i = 0; i + 1 < bytes.Length; i += 2)
            (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
        return bytes;
    }

    /// <summary>Test-Mock IMemoryReader. Kann statische VAs UND beliebige
    /// (heap-) Adressen liefern. Defaults zu Nullen für unbekannte Adressen.</summary>
    private sealed class FakeMem : IMemoryReader
    {
        private readonly Dictionary<nint, byte[]> _data = new();
        public void Set(nint va, byte[] bytes) => _data[va] = bytes;
        public void SetWord(nint va, ushort v) =>
            Set(va, new[] { (byte)(v & 0xff), (byte)(v >> 8) });
        public void SetDword(nint va, uint v) => Set(va, BitConverter.GetBytes(v));

        public byte[]? ReadBytes(nint va, int n)
        {
            if (_data.TryGetValue(va, out var bytes))
            {
                if (bytes.Length >= n) return bytes[..n];
                var padded = new byte[n];
                Array.Copy(bytes, padded, bytes.Length);
                return padded;
            }
            return new byte[n];  // zeros
        }
        public ushort ReadWord(nint va) => BitConverter.ToUInt16(ReadBytes(va, 2)!, 0);
        public byte ReadByte(nint va) => ReadBytes(va, 1)![0];
    }

    [Fact]
    public void Read_NoDifficulty_ReturnsInactive()
    {
        var mem = new FakeMem();  // alle null/0
        var state = BubblewonderState.Read(mem);
        Assert.False(state.IsActive);
        Assert.Equal(0, state.Difficulty);
        Assert.Empty(state.Grid.Mechanisms);
    }

    [Fact]
    public void Read_DifficultyButNoHeap_ReturnsActiveButEmpty()
    {
        var mem = new FakeMem();
        mem.SetWord(BubblewonderMemoryMap.UserDifficulty, 4);
        // RegsHeapPointer bleibt 0 → kein REGS lesbar
        var state = BubblewonderState.Read(mem);
        Assert.Equal(4, state.Difficulty);
        Assert.False(state.IsActive);  // weil keine Mechanismen
        Assert.Empty(state.Grid.Mechanisms);
    }

    [Fact]
    public void Read_ResolvesResourceIdFromDifficultyAndVariant()
    {
        var mem = new FakeMem();
        mem.SetWord(BubblewonderMemoryMap.UserDifficulty, 4);
        mem.SetWord(BubblewonderMemoryMap.VariationCounterDiff4, 1);
        var state = BubblewonderState.Read(mem);
        Assert.Equal(16607, state.RegsResourceId);  // Diff 4, variant 1
    }

    [Fact]
    public void Read_FullPipeline_BuildsGridFromHeapRegs()
    {
        var mem = new FakeMem();
        // Difficulty 1 → REGS 16600 (which we have as test fixture)
        mem.SetWord(BubblewonderMemoryMap.UserDifficulty, 1);
        // Heap pointer points to a fake address
        const uint fakeHeapVa = 0x80000000;
        mem.SetDword(BubblewonderMemoryMap.RegsHeapPointer, fakeHeapVa);
        // Place REGS 16600 bytes at that address (from our hex fixture)
        var regsBytes = BeToLe(
            "0010000100040000000000000000000000000000000200040009000100000000000000010003000000020005000a000100000001000000000001000000020004000600010000000000000001000300000002000600060001000000000000000100030000000400040008000100010001000000000000000100030000000300010000000100000000000100000003000500030001000000000001000000020000000300040004000100000001000000000001000000030006000400010000000100000000000100000003000b000400010001000000000000000000000003000200060001000000000001000000020000000300060008000100010000000000000000000000030004000a00010001000000000000000000000003000b000a000100010000000000000000000000010005000c00000000000000000000000000000001000200090000000000000000000000000000");
        mem.Set(unchecked((nint)fakeHeapVa), regsBytes);

        var state = BubblewonderState.Read(mem);
        Assert.True(state.IsActive);
        Assert.Equal(1, state.Difficulty);
        Assert.Equal(16, state.Grid.Mechanisms.Count);  // REGS 16600 has 16 records
    }

    [Fact]
    public void Read_BuildsConnectionsFromActionSlotTables()
    {
        var mem = new FakeMem();
        mem.SetWord(BubblewonderMemoryMap.UserDifficulty, 1);
        const uint fakeHeapVa = 0x80000000;
        mem.SetDword(BubblewonderMemoryMap.RegsHeapPointer, fakeHeapVa);
        // Use the same REGS 16600 fixture (BE→LE for live-memory simulation)
        mem.Set(unchecked((nint)fakeHeapVa), BeToLe(
            "0010000100040000000000000000000000000000000200040009000100000000000000010003000000020005000a000100000001000000000001000000020004000600010000000000000001000300000002000600060001000000000000000100030000000400040008000100010001000000000000000100030000000300010000000100000000000100000003000500030001000000000001000000020000000300040004000100000001000000000001000000030006000400010000000100000000000100000003000b000400010001000000000000000000000003000200060001000000000001000000020000000300060008000100010000000000000000000000030004000a00010001000000000000000000000003000b000a000100010000000000000000000000010005000c00000000000000000000000000000001000200090000000000000000000000000000"));

        var state = BubblewonderState.Read(mem);
        Assert.Equal(24, state.Connections.Count);
    }

    [Fact]
    public void Read_PositionCountersCollected()
    {
        var mem = new FakeMem();
        mem.SetWord(BubblewonderMemoryMap.UserDifficulty, 4);
        const uint fakeHeapVa = 0x80000000;
        mem.SetDword(BubblewonderMemoryMap.RegsHeapPointer, fakeHeapVa);
        // Plant a tiny REGS so we get past the empty-grid early return (LE!)
        var regs = new byte[20 + 20];  // header + 1 record
        regs[0] = 1; regs[1] = 0;  // count = 1 LE
        // record at offset 20: F0=1 (passive) LE
        regs[20] = 1; regs[21] = 0;
        mem.Set(unchecked((nint)fakeHeapVa), regs);

        // Now plant counter at index (3*13+2)*6 = 246 in the position table
        var counterBytes = new byte[65 * 6];
        int idx = 3 * 13 + 2;
        counterBytes[idx * 6] = 2;        // counter = 2
        counterBytes[idx * 6 + 2] = 0xab; // handle low
        counterBytes[idx * 6 + 3] = 0x00; // handle high
        mem.Set(BubblewonderMemoryMap.PositionCounterTable, counterBytes);

        var state = BubblewonderState.Read(mem);
        Assert.Equal((ushort)2, state.PositionCounters[idx]);
        Assert.Equal((ushort)0xab, state.PositionHandles[idx]);
    }
}
