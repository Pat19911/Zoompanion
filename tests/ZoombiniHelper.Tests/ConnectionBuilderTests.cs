using Xunit;
using ZoombiniHelper;
using ZoombiniHelper.Bubblewonder;

namespace ZoombiniHelper.Tests;

/// <summary>Tests für ConnectionBuilder mit synthetischem IMemoryReader.</summary>
public class ConnectionBuilderTests
{
    /// <summary>Mock-Reader der konfigurierbare Bytes pro Adresse zurückgibt.</summary>
    private sealed class FakeMem : IMemoryReader
    {
        private readonly Dictionary<nint, byte[]> _data = new();

        public void Set(nint va, byte[] bytes) => _data[va] = bytes;

        public byte[] ReadBytes(nint va, int n)
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

        public ushort ReadWord(nint va) => BitConverter.ToUInt16(ReadBytes(va, 2), 0);
        public byte ReadByte(nint va) => ReadBytes(va, 1)[0];
        public uint ReadDword(nint va) => BitConverter.ToUInt32(ReadBytes(va, 4), 0);
    }

    [Fact]
    public void Build_AllZeroMemory_ReturnsEmptyTriples()
    {
        var mem = new FakeMem();
        var conns = ConnectionBuilder.BuildFromMemory(mem);
        Assert.Equal(24, conns.Count);
        // All primary = 0, no secondary, no tertiary
        Assert.All(conns, c => Assert.Equal(0, c.PrimaryHandle));
        Assert.All(conns, c => Assert.Null(c.TertiaryHandle));
    }

    [Fact]
    public void Build_SetsPrimaryHandlesFromMemory()
    {
        var mem = new FakeMem();
        // Set 24 word handles starting at primary table
        var primaryBytes = new byte[24 * 2];
        for (int i = 0; i < 24; i++)
        {
            primaryBytes[i * 2] = (byte)(0x10 + i);     // little-endian low
            primaryBytes[i * 2 + 1] = 0x00;
        }
        mem.Set(BubblewonderMemoryMap.ActionSlotHandlesPrimary, primaryBytes);

        var conns = ConnectionBuilder.BuildFromMemory(mem);
        Assert.Equal((ushort)0x10, conns[0].PrimaryHandle);
        Assert.Equal((ushort)0x11, conns[1].PrimaryHandle);
        Assert.Equal((ushort)0x27, conns[23].PrimaryHandle);
    }

    [Fact]
    public void Build_SecondaryOnlyWhereFlagSet()
    {
        var mem = new FakeMem();
        var secondaryBytes = new byte[24];
        for (int i = 0; i < 24; i++)
            secondaryBytes[i] = (byte)(0x80 + i);  // distinct values
        mem.Set(BubblewonderMemoryMap.ActionSlotHandlesSecondary, secondaryBytes);

        var conns = ConnectionBuilder.BuildFromMemory(mem);

        // Per ActionSlotTables.HasSecondaryFlag: slot 0,1,2 = no secondary
        Assert.Null(conns[0].SecondaryHandle);
        Assert.Null(conns[1].SecondaryHandle);
        Assert.Null(conns[2].SecondaryHandle);
        // Slots 3-8 all have secondary
        Assert.Equal((ushort)0x83, conns[3].SecondaryHandle);
        Assert.Equal((ushort)0x88, conns[8].SecondaryHandle);
        // Slot 9-11 again none
        Assert.Null(conns[9].SecondaryHandle);
        // Slot 23 has special flag (=2) but still treated as having secondary
        Assert.Equal((ushort)(0x80 + 23), conns[23].SecondaryHandle);
    }

    [Fact]
    public void Build_PreservesInitialScrbIdsFromStaticTable()
    {
        var mem = new FakeMem();
        var conns = ConnectionBuilder.BuildFromMemory(mem);
        // From ActionSlotTables.InitialScrbId
        Assert.Equal((ushort)0x2328, conns[0].InitialScrbId);
        Assert.Equal((ushort)0x2329, conns[3].InitialScrbId);
        Assert.Equal((ushort)0x232B, conns[12].InitialScrbId);
        Assert.Equal((ushort)0x0000, conns[17].InitialScrbId);  // Slot 17 has 0
        Assert.Equal((ushort)0x0124, conns[23].InitialScrbId);
    }

    [Fact]
    public void MechanismConnection_Triple_DetectedCorrectly()
    {
        var withTriple = new MechanismConnection(0, 0x100, 0x200, 0x300, 0x2328);
        Assert.True(withTriple.IsTriple);
        Assert.Equal(3, withTriple.LinkedObjectCount);

        var pair = new MechanismConnection(0, 0x100, 0x200, null, 0x2328);
        Assert.False(pair.IsTriple);
        Assert.Equal(2, pair.LinkedObjectCount);

        var single = new MechanismConnection(0, 0x100, null, null, 0x2328);
        Assert.False(single.IsTriple);
        Assert.Equal(1, single.LinkedObjectCount);
    }

    [Fact]
    public void ActionSlotTables_HasSecondary_MatchesEXEData()
    {
        // Spot-check from raw EXE table
        Assert.False(ActionSlotTables.SlotHasSecondary(0));
        Assert.True(ActionSlotTables.SlotHasSecondary(3));
        Assert.True(ActionSlotTables.SlotHasSecondary(8));
        Assert.False(ActionSlotTables.SlotHasSecondary(9));
        Assert.True(ActionSlotTables.SlotHasSecondary(12));
        Assert.True(ActionSlotTables.SlotHasSecondary(20));
        Assert.True(ActionSlotTables.SlotHasSecondary(23));  // value=2 still truthy
    }
}
