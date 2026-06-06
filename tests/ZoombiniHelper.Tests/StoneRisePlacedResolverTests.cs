using Xunit;
using ZoombiniHelper;

namespace ZoombiniHelper.Tests;

public class StoneRisePlacedResolverTests
{
    [Fact]
    public void Resolve_MatchesFilledSlotByZbId()
    {
        // Two zbs on the heap, one slot filled with the zb whose id is 0x42.
        var mem = new ByteMemory();
        var zbA = LayOutZb(mem, addr: 0x04800000, nextAddr: 0x04800200, id: 0x42, h:1, e:2, n:3, f:4);
        var zbB = LayOutZb(mem, addr: 0x04800200, nextAddr: 0,           id: 0x77, h:5, e:5, n:5, f:5);
        mem.SetDword(EngineObjectList.HeadAddress, (uint)0x04800000);

        var slots = new[] {
            new StoneRiseState.PairSlot(TileIndex: 10, IsFilled: true,  PlacedZbId: 0x42),
            new StoneRiseState.PairSlot(TileIndex: 11, IsFilled: false),
        };
        var state = NewStateWithSlots(slots);

        var resolved = StoneRisePlacedResolver.Resolve(state, mem);

        Assert.Single(resolved);
        Assert.Equal(new StoneRisePlacementTracker.ZbAttrs(1, 2, 3, 4), resolved[10]);
    }

    [Fact]
    public void Resolve_IgnoresEmptySlots()
    {
        var mem = new ByteMemory();
        LayOutZb(mem, 0x04800000, 0, id: 0x42, h:1, e:2, n:3, f:4);
        mem.SetDword(EngineObjectList.HeadAddress, 0x04800000);

        var state = NewStateWithSlots(new[] {
            new StoneRiseState.PairSlot(TileIndex: 10, IsFilled: false),
        });
        var resolved = StoneRisePlacedResolver.Resolve(state, mem);
        Assert.Empty(resolved);
    }

    [Fact]
    public void Resolve_SkipsSlotWhenIdHasNoMatchingZb()
    {
        // A slot claims to hold zb id 0x99, but no such zb on the heap.
        var mem = new ByteMemory();
        LayOutZb(mem, 0x04800000, 0, id: 0x42, h:1, e:2, n:3, f:4);
        mem.SetDword(EngineObjectList.HeadAddress, 0x04800000);

        var state = NewStateWithSlots(new[] {
            new StoneRiseState.PairSlot(TileIndex: 10, IsFilled: true, PlacedZbId: 0x99),
        });
        var resolved = StoneRisePlacedResolver.Resolve(state, mem);
        Assert.Empty(resolved);
    }

    [Fact]
    public void Resolve_FindsZbInLaterListNode()
    {
        var mem = new ByteMemory();
        LayOutZb(mem, 0x04800000, 0x04800200, id: 0x11, h:1, e:1, n:1, f:1);
        LayOutZb(mem, 0x04800200, 0x04800400, id: 0x22, h:2, e:2, n:2, f:2);
        LayOutZb(mem, 0x04800400, 0,           id: 0x33, h:3, e:3, n:3, f:3);
        mem.SetDword(EngineObjectList.HeadAddress, 0x04800000);

        var state = NewStateWithSlots(new[] {
            new StoneRiseState.PairSlot(7, IsFilled: true, PlacedZbId: 0x33),
        });
        var resolved = StoneRisePlacedResolver.Resolve(state, mem);
        Assert.Single(resolved);
        Assert.Equal(new StoneRisePlacementTracker.ZbAttrs(3, 3, 3, 3), resolved[7]);
    }

    private static StoneRiseState NewStateWithSlots(StoneRiseState.PairSlot[] slots)
    {
        var ctor = typeof(StoneRiseState).GetConstructors(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
        return (StoneRiseState)ctor.Invoke(new object?[] {
            /* difficulty */ 4,
            /* slots */ (IReadOnlyList<StoneRiseState.PairSlot>)slots,
            /* connectors */ (IReadOnlyList<StoneRiseState.Connector>)System.Array.Empty<StoneRiseState.Connector>(),
            /* tilePositions */ null,
            /* activeSlotPositions */ null,
            /* activeSlotToTileIndex */ null,
            /* cursorX */ 0, /* cursorY */ 0, /* cursorActiveSlot */ 0,
        });
    }

    private static (nint addr, ushort id) LayOutZb(ByteMemory mem, uint addr, uint nextAddr,
                                                    ushort id, byte h, byte e, byte n, byte f)
    {
        // Header (0x30 bytes): next at +0x00, id at +0x1A, handle at +0x20.
        // The id is the engine's per-zb identity word LIVING IN THE HEADER —
        // not in the 0xC4-byte payload. This is what tile.w1 of a placed
        // slot matches against.
        mem.SetDword((nint)addr, nextAddr);
        mem.SetWord((nint)addr + 0x1A, id);
        mem.SetDword((nint)addr + 0x20, 0x00000001);  // handle (ignored by resolver)
        // Record at +0x30: attrs at +0xC0..+0xC3
        mem.SetByte((nint)addr + 0x30 + 0xC0, h);
        mem.SetByte((nint)addr + 0x30 + 0xC1, e);
        mem.SetByte((nint)addr + 0x30 + 0xC2, n);
        mem.SetByte((nint)addr + 0x30 + 0xC3, f);
        return ((nint)addr, id);
    }

    private sealed class ByteMemory : IMemoryReader
    {
        private readonly Dictionary<nint, byte> _bytes = new();
        public int AttachedProcessId => 0;
        public void SetByte(nint va, byte v) => _bytes[va] = v;
        public void SetWord(nint va, ushort v)
        { _bytes[va] = (byte)(v & 0xFF); _bytes[va+1] = (byte)(v >> 8); }
        public void SetDword(nint va, uint v)
        { for (int i = 0; i < 4; i++) _bytes[va+i] = (byte)((v >> (i*8)) & 0xFF); }
        public byte ReadByte(nint va) => _bytes.TryGetValue(va, out var b) ? b : (byte)0;
        public ushort ReadWord(nint va) => (ushort)(ReadByte(va) | (ReadByte(va+1) << 8));
        public byte[]? ReadBytes(nint va, int count)
        {
            var buf = new byte[count];
            for (int i = 0; i < count; i++) buf[i] = ReadByte(va + i);
            return buf;
        }
    }
}
