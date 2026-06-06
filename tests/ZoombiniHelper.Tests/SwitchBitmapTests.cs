using Xunit;
using ZoombiniHelper;
using ZoombiniHelper.Bubblewonder;

namespace ZoombiniHelper.Tests;

/// <summary>Tests für SwitchBitmap — die 24-Bit Switch-Aktivierungs-Bitmap
/// im ZB-Aggregator (FUN_0042B8C0 in v2 PE32).</summary>
public class SwitchBitmapTests
{
    [Fact]
    public void EmptyBitmap_NoSwitchesActive()
    {
        var bm = new SwitchBitmap(0, 0, 0);
        Assert.Equal(0, bm.TotalActivatedSwitches);
        Assert.False(bm.ChannelABit(0));
        Assert.False(bm.ChannelCBit(15));
    }

    [Fact]
    public void ChannelA_BitN_ReturnsCorrectBit()
    {
        var bm = new SwitchBitmap(ChannelA: 0b00010100, 0, 0);
        Assert.True(bm.ChannelABit(2));
        Assert.True(bm.ChannelABit(4));
        Assert.False(bm.ChannelABit(0));
        Assert.False(bm.ChannelABit(7));
        Assert.Equal(2, bm.TotalActivatedSwitches);
    }

    [Fact]
    public void ChannelC_Is16Bit()
    {
        var bm = new SwitchBitmap(0, 0, ChannelC: 0x8001);  // bit 0 + bit 15
        Assert.True(bm.ChannelCBit(0));
        Assert.True(bm.ChannelCBit(15));
        Assert.False(bm.ChannelCBit(8));
        Assert.Equal(2, bm.TotalActivatedSwitches);
    }

    [Fact]
    public void ChannelBitN_OutOfRange_ReturnsFalse()
    {
        var bm = new SwitchBitmap(0xFF, 0xFF, 0xFFFF);
        Assert.False(bm.ChannelABit(-1));
        Assert.False(bm.ChannelABit(8));
        Assert.False(bm.ChannelBBit(8));
        Assert.False(bm.ChannelCBit(16));
    }

    [Fact]
    public void Read_NullAggregatorPointer_ReturnsNull()
    {
        // Mock-Reader liefert lauter 0en für Aggregator-Pointer
        var mem = new InMemoryReader();
        Assert.Null(SwitchBitmap.Read(mem));
    }

    [Fact]
    public void Read_ValidPointer_ReturnsBitmap()
    {
        var mem = new InMemoryReader();
        // Aggregator-Pointer auf simulierte Heap-Adresse
        mem.SetBytes(BubblewonderMemoryMap.AggregatorPointer,
            BitConverter.GetBytes((uint)0x00800000));
        // Switch-Bitmap-Bytes bei heap+0x52
        mem.SetBytes((nint)0x00800000 + BubblewonderMemoryMap.AggregatorSwitchBitmapAOffset,
            new byte[] { 0x05, 0x12, 0x34, 0x56 });   // ChA=0x05, ChB=0x12, ChC=0x5634
        var bm = SwitchBitmap.Read(mem);
        Assert.NotNull(bm);
        Assert.Equal((byte)0x05, bm!.ChannelA);
        Assert.Equal((byte)0x12, bm.ChannelB);
        Assert.Equal((ushort)0x5634, bm.ChannelC);
    }

    /// <summary>Minimaler Test-Reader der konfigurierbare Bytes pro Adresse liefert.</summary>
    private sealed class InMemoryReader : IMemoryReader
    {
        private readonly Dictionary<nint, byte[]> _data = new();
        public void SetBytes(nint addr, byte[] bytes) => _data[addr] = bytes;
        public byte[]? ReadBytes(nint addr, int size)
        {
            if (_data.TryGetValue(addr, out var bytes))
                return bytes.Length >= size ? bytes[..size] : bytes;
            return new byte[size];
        }
        public ushort ReadWord(nint addr) => BitConverter.ToUInt16(ReadBytes(addr, 2)!);
        public byte ReadByte(nint addr) => ReadBytes(addr, 1)![0];
    }
}
