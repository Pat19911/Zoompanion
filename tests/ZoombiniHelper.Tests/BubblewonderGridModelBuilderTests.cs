using ZoombiniHelper;
using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Bubblewonder.Simulator;

namespace ZoombiniHelper.Tests;

public class BubblewonderGridModelBuilderTests
{
    private sealed class FakeMem : IMemoryReader
    {
        private readonly Dictionary<nint, byte[]> _data = new();
        public void Set(nint va, byte[] bytes) => _data[va] = bytes;
        public void SetDword(nint va, uint v) => Set(va, BitConverter.GetBytes(v));
        public void SetWord(nint va, ushort v) => Set(va, BitConverter.GetBytes(v));

        public byte[]? ReadBytes(nint va, int n)
        {
            if (_data.TryGetValue(va, out var b))
                return b.Length >= n ? b[..n] : Pad(b, n);
            return new byte[n];
        }
        public ushort ReadWord(nint va) => BitConverter.ToUInt16(ReadBytes(va, 2)!, 0);
        public byte ReadByte(nint va) => ReadBytes(va, 1)![0];
        private static byte[] Pad(byte[] src, int n)
        {
            var r = new byte[n]; Array.Copy(src, r, src.Length); return r;
        }
    }

    /// <summary>Bubble-Engine-Object-Node mit den Feldern die der Builder liest.</summary>
    private static byte[] MakeBubbleNode(
        ushort hdr1A,
        ushort[] regsLE,                  // F0..F9 als Little-Endian-Words
        byte switchState = 0,
        ushort stickyTrapped = 0,
        ushort triggerTarget = 0,
        ushort condAttr = 0,
        ushort condVariant = 0)
    {
        var node = new byte[BubbleObjectScanner.BytesPerNode];
        BitConverter.GetBytes(BubbleObjectScanner.BubbleHandle).CopyTo(node, 0x20);
        BitConverter.GetBytes(hdr1A).CopyTo(node, 0x1A);
        for (int i = 0; i < 10; i++)
        {
            int off = BubblewonderMemoryMap.BubbleRegsCopyStart + i * 2;
            BitConverter.GetBytes(regsLE[i]).CopyTo(node, off);
        }
        // prop1/prop2 sind ALIASES zu F3/F4 (überlappen): F3 = prop1, F4 = prop2.
        // Der ScannerSetzt prop1/prop2 zusätzlich aus seinen eigenen Offsets — diese
        // werden hier nicht separat gesetzt weil sie schon im REGS-Bereich liegen.
        node[BubblewonderMemoryMap.SwitchStateOffset] = switchState;
        BitConverter.GetBytes(stickyTrapped).CopyTo(node, BubblewonderMemoryMap.StickyTrappedZbOffset);
        BitConverter.GetBytes(triggerTarget).CopyTo(node, BubblewonderMemoryMap.TriggerTargetHandleOffset);
        BitConverter.GetBytes(condAttr).CopyTo(node, BubblewonderMemoryMap.BubbleConditionalAttrOffset);
        BitConverter.GetBytes(condVariant).CopyTo(node, BubblewonderMemoryMap.BubbleConditionalVariantOffset);
        return node;
    }

    private static byte[] MakeMachineNode(int dirCode, int targetIdx = 0, int px = 0, int py = 0)
    {
        var node = new byte[0x90];  // groß genug für +0x8a (TargetIdx)
        BitConverter.GetBytes(BubblewonderGridModelBuilder.MachineHandle).CopyTo(node, 0x20);
        BitConverter.GetBytes((short)dirCode).CopyTo(node, 0x30);
        BitConverter.GetBytes((short)px).CopyTo(node, 0x32);
        BitConverter.GetBytes((short)py).CopyTo(node, 0x34);
        BitConverter.GetBytes((short)targetIdx).CopyTo(node, BubblewonderMemoryMap.BubbleTargetIdxOffset);
        return node;
    }

    private static FakeMem WithLinkedList(params (nint addr, byte[] node)[] nodes)
    {
        var mem = new FakeMem();
        if (nodes.Length == 0)
        {
            mem.SetDword(EngineObjectList.HeadAddress, 0);
            return mem;
        }
        mem.SetDword(EngineObjectList.HeadAddress, (uint)nodes[0].addr);
        for (int i = 0; i < nodes.Length; i++)
        {
            uint nextPtr = i + 1 < nodes.Length ? (uint)nodes[i + 1].addr : 0;
            BitConverter.GetBytes(nextPtr).CopyTo(nodes[i].node, 0);
            mem.Set(nodes[i].addr, nodes[i].node);
        }
        return mem;
    }

    private static void SetDifficultyAndRegs(FakeMem mem, int diff, int regsId)
    {
        // BubblewonderState.Read liest UserDifficulty + RegsHeapPointer.
        // Wir können RegsResourceId nicht direkt setzen — der State leitet sie
        // anders ab. Für die Builder-Tests reicht es, den Mem-Setup zu simulieren.
        mem.SetWord(BubblewonderMemoryMap.UserDifficulty, (ushort)diff);
    }

    [Fact]
    public void FromState_EmptyState_YieldsEmptyGrid()
    {
        var mem = WithLinkedList();
        SetDifficultyAndRegs(mem, diff: 1, regsId: 16600);

        var bubbles = BubbleObjectScanner.Scan(mem);
        var grid = BubblewonderGridModelBuilder.FromBubbles(bubbles, regsResourceId: 16600, mem);

        Assert.Empty(grid.CellsInChannel(0));
    }

    [Fact]
    public void FromState_BubbleWithRegs_BuildsCellAtPosition()
    {
        // Static-Deflector (f0=3) bei Pos (3,5), Channel=2.
        // F-Bit-Mapping (verifiziert 2026-05-03): F4=Left, F5=Down, F6=Right, F7=Up.
        // Setze F5=1 → erwartete Direction = Down.
        var node = MakeBubbleNode(
            hdr1A: 0x0010,
            regsLE: new ushort[] { 3, 3, 5, 2, 0, 1, 0, 0, 0, 0 });
        var mem = WithLinkedList((0x007E8000, node));
        SetDifficultyAndRegs(mem, diff: 1, regsId: 16600);

        var bubbles = BubbleObjectScanner.Scan(mem);
        var grid = BubblewonderGridModelBuilder.FromBubbles(bubbles, regsResourceId: 16600, mem);

        var cell = grid.CellAt(3 * 13 + 5);
        Assert.Equal(MechanismType.StaticDeflector, cell.Type);
        Assert.Equal(2, cell.Channel);
        Assert.Equal(Direction.Down, cell.PrimaryDirection);
    }

    [Fact]
    public void FromState_SwitchCell_CapturesLiveSwitchState()
    {
        // Switch (f0=4) bei Pos (5,5), Channel=1, F4..F7 = (Up, Right, Down, Left)
        // Live-State +0x7C = 2 (Down).
        var node = MakeBubbleNode(
            hdr1A: 0x0020,
            regsLE: new ushort[] { 4, 5, 5, 1, 1, 1, 1, 1, 0, 0 },
            switchState: 2);
        var mem = WithLinkedList((0x007E9000, node));
        SetDifficultyAndRegs(mem, diff: 1, regsId: 16600);

        var grid = BubblewonderGridModelBuilder.FromBubbles(BubbleObjectScanner.Scan(mem), 16600, mem);

        Assert.Equal(2, grid.State.SwitchStateByCell[5 * 13 + 5]);
    }

    [Fact]
    public void FromState_StickyWithTrappedZb_CapturesLiveTrapped()
    {
        var node = MakeBubbleNode(
            hdr1A: 0x0030,
            regsLE: new ushort[] { 5, 7, 3, 4, 0, 0, 0, 0, 0, 0 },
            stickyTrapped: 0x99);
        var mem = WithLinkedList((0x007EA000, node));
        SetDifficultyAndRegs(mem, diff: 1, regsId: 16600);

        var grid = BubblewonderGridModelBuilder.FromBubbles(BubbleObjectScanner.Scan(mem), 16600, mem);

        Assert.Equal((ushort)0x99, grid.State.StickyTrappedByCell[7 * 13 + 3].HeaderId);
    }

    [Fact]
    public void FromState_ConditionalCell_BuildsMatchRule()
    {
        // Conditional (f0=2), Channel=1, F4=Right, attr=2 (Eyes), variant=3.
        var node = MakeBubbleNode(
            hdr1A: 0x0040,
            regsLE: new ushort[] { 2, 4, 8, 1, 0, 1, 0, 0, 0, 0 },
            condAttr: 2,
            condVariant: 3);
        var mem = WithLinkedList((0x007EB000, node));
        SetDifficultyAndRegs(mem, diff: 1, regsId: 16600);

        var grid = BubblewonderGridModelBuilder.FromBubbles(BubbleObjectScanner.Scan(mem), 16600, mem);
        var cell = grid.CellAt(4 * 13 + 8);

        Assert.Equal(MechanismType.Conditional, cell.Type);
        Assert.True(cell.MatchesZb(new SimZb(0x100, Hair: 0, Eyes: 3, Nose: 0, Feet: 0)));
        Assert.False(cell.MatchesZb(new SimZb(0x101, Hair: 0, Eyes: 4, Nose: 0, Feet: 0)));
    }

    [Fact]
    public void FromBubbles_MachinesFromHardcodedMapping_RegsId16601()
    {
        // 16601 hat Spawn-Cells [8, 61] — beide nicht in Insel-Zone.
        // Maschinen-Direction wird aus den live Maschinen-Objects gelesen.
        var machine1 = MakeMachineNode(dirCode: 2);  // Down
        var machine2 = MakeMachineNode(dirCode: 3);  // Right
        var mem = WithLinkedList((0x00800000, machine1), (0x00800100, machine2));

        var grid = BubblewonderGridModelBuilder.FromBubbles(
            BubbleObjectScanner.Scan(mem), regsResourceId: 16601, mem);

        Assert.Equal(2, grid.Machines.Count);
        Assert.Equal(8, grid.Machines[0].StartCellIndex);
        Assert.Equal(61, grid.Machines[1].StartCellIndex);
        Assert.Equal(Direction.Down, grid.Machines[0].StartDirection);
        Assert.Equal(Direction.Right, grid.Machines[1].StartDirection);
        Assert.False(grid.Machines[0].IsIsland);
        Assert.False(grid.Machines[1].IsIsland);
    }

    [Fact]
    public void FromBubbles_RegsId16606_HardcodedHasOnlyThrowers_NoIslandGuess()
    {
        // 16606 hardcoded = NUR Werfer [34, 76] (2026-05-30): der Insel-Re-Launch-Spawn
        // ist nicht zuverlässig statisch (Mining: (3,2) vs (4,1) je Variante) → wird live
        // gelernt, nicht geraten. Daher modelliert der Builder hier KEINE Insel-Maschine
        // (kein tödliches Raten mehr).
        var m1 = MakeMachineNode(dirCode: 2);
        var m2 = MakeMachineNode(dirCode: 2);
        var mem = WithLinkedList((0x00800000, m1), (0x00800100, m2));

        var grid = BubblewonderGridModelBuilder.FromBubbles(
            BubbleObjectScanner.Scan(mem), regsResourceId: 16606, mem);

        Assert.Equal(2, grid.Machines.Count);
        Assert.DoesNotContain(grid.Machines, m => m.IsIsland);
    }

    [Fact]
    public void FromBubbles_WithLearnedIslandSpawn_AddsAndMarksIslandMachine()
    {
        // 16606 hardcoded = nur Werfer {34,76}. Ein live gelernter Insel-Spawn 53=(4,1)
        // wird gemergt → 3 Maschinen, die bei 53 ist als Insel markiert (auch wenn 53
        // NICHT in der Eckzone liegt). So wird Gelerntes WIRKLICH benutzt.
        var m1 = MakeMachineNode(dirCode: 2);
        var m2 = MakeMachineNode(dirCode: 2);
        var m3 = MakeMachineNode(dirCode: 2);
        var bubbleAt53 = MakeBubbleNode(
            hdr1A: 0x0001, regsLE: new ushort[] { 3, 4, 1, 0, 0, 0, 1, 0, 0, 0 });
        var mem = WithLinkedList(
            (0x00800000, m1), (0x00800100, m2), (0x00800200, m3), (0x00900000, bubbleAt53));

        var grid = BubblewonderGridModelBuilder.FromBubbles(
            BubbleObjectScanner.Scan(mem), regsResourceId: 16606, mem, learnedIslandSpawn: 53);

        Assert.Equal(3, grid.Machines.Count);
        Assert.Contains(grid.Machines, m => m.StartCellIndex == 53 && m.IsIsland);
        Assert.Equal(2, grid.Machines.Count(m => !m.IsIsland));
    }

    [Fact]
    public void DetectMachines_IslandFromTargetIdx_NotFromCellType()
    {
        // Verifiziert (Ghidra 2026-05-29): Insel = Maschine +0x8a (TargetIdx) != 0,
        // NICHT der Standort-Zelltyp (Werfer stehen auf der Start-Insel 0x14).
        // 2 Werfer (TargetIdx=0) + 1 Insel (TargetIdx=1).
        var thrower0 = MakeMachineNode(dirCode: 1, targetIdx: 0, px: 178, py: 292);
        var thrower1 = MakeMachineNode(dirCode: 2, targetIdx: 0, px: 193, py: 309);
        var island  = MakeMachineNode(dirCode: 2, targetIdx: 1, px: 90,  py: 93);
        var mem = WithLinkedList(
            (0x007E0000, thrower0), (0x007E1000, thrower1), (0x007E2000, island));

        var placements = BubblewonderGridModelBuilder.DetectMachines(
            mem, new List<BubbleObject>());

        Assert.Equal(3, placements.Count);
        Assert.Single(placements, p => p.IsIsland);
        Assert.True(placements.Single(p => p.TargetIdx == 1).IsIsland);
        Assert.All(placements.Where(p => p.TargetIdx == 0), p => Assert.False(p.IsIsland));
    }

    [Fact]
    public void FromState_OutOfGridPositions_AreIgnored()
    {
        // F1=15 (= row 15) ist außerhalb des 12er-Grids.
        var node = MakeBubbleNode(
            hdr1A: 0x0050,
            regsLE: new ushort[] { 3, 15, 5, 0, 0, 1, 0, 0, 0, 0 });
        var mem = WithLinkedList((0x007EC000, node));
        SetDifficultyAndRegs(mem, diff: 1, regsId: 16600);

        var grid = BubblewonderGridModelBuilder.FromBubbles(BubbleObjectScanner.Scan(mem), 16600, mem);
        var cell = grid.CellAt(15 * 13 + 5);
        Assert.Equal(MechanismType.Passthrough, cell.Type);  // = Empty default
    }
}
