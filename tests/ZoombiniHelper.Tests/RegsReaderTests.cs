using Xunit;
using ZoombiniHelper.Bubblewonder;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Tests für den REGS-Resource-Parser. Fixtures sind die ersten 2 Bytes-Folgen
/// aus maze2.mhk: Resource 16600 (Diff 1, 340 B, 16 records) und Resource 16606
/// (Diff 4 v1, 780 B, 38 records). Extrahiert via mohawk_parser.py.
/// </summary>
public class RegsReaderTests
{
    // Diff 1 v1 — 340 B, 16 records, header [16, 1, 4, 0, ...]
    // Bytes from maze2.mhk REGS resource 16600, extracted via mohawk_parser.py.
    private const string Regs16600_Hex =
        "0010000100040000000000000000000000000000000200040009000100000000000000010003000000020005000a000100000001000000000001000000020004000600010000000000000001000300000002000600060001000000000000000100030000000400040008000100010001000000000000000100030000000300010000000100000000000100000003000500030001000000000001000000020000000300040004000100000001000000000001000000030006000400010000000100000000000100000003000b000400010001000000000000000000000003000200060001000000000001000000020000000300060008000100010000000000000000000000030004000a00010001000000000000000000000003000b000a000100010000000000000000000000010005000c00000000000000000000000000000001000200090000000000000000000000000000";

    // Diff 4 v1 — 780 B, 38 records, header [38, 2, 4, 12, ...]
    private const string Regs16606_Hex =
        "002600020004000c000000000000000000000000000200030009000100010000000000000000000000020007000500010000000100000000000100000002000700040001000000010000000000010000000200070002000100000000000000010003000000020001000400010000000100000000000100000002000a0008000100000000000000010003000000020005000a0001000000000001000000020000000200050005000100000000000000010003000000020005000700010000000100000000000100000003000300010001000000010000000000010000000300070001000100000001000000000001000000030009000100010000000100000000000100000003000500040001000100000000000000000000000300090004000100010000000000000000000000030009000500010001000000000000000000000003000b00060001000100000000000000000000000300070007000100010000000000000000000000030005000800010000000000010000000200000003000200090001000000010000000000010000000300070009000100010000000000000000000000030007000a0001000000000001000000020000000100050002000000000000000000000000000000010001000300000000000000000000000000000001000000070000000000000000000000000000000100080009000000000000000000000000000000010005000b0000000000000000000000000000000600010005000400000000000000000000000000040007000b00040000000100000001000100000004000a00090004000100000000000100030000000600080004000500000000000000000000000000050003000400050000000000000000000000000005000b0005000500000000000000000000000000060008000500060000000000000000000000000005000a0005000600000000000000000000000000040002000700060000000100010000000100000006000a0007000700000000000000000000000000040001000700070001000100000001000000000004000a00060001000100010000000000000001";

    /// <summary>Real REGS bytes from maze2.mhk extracted via mohawk_parser.py.
    /// Single source of truth — when these change we know our extraction broke.</summary>
    private static byte[] Regs16600 => Convert.FromHexString(StripWhitespace(Regs16600_Hex));
    private static byte[] Regs16606 => Convert.FromHexString(StripWhitespace(Regs16606_Hex));
    private static string StripWhitespace(string s) => s.Replace(" ", "").Replace("\n", "").Replace("\r", "");

    [Fact]
    public void Header_ReadsCountFromBigEndian()
    {
        // 0x0010 = 16 (Diff 1 v1 has 16 records)
        var header = RegsHeader.FromBytes(Regs16600);
        Assert.Equal(16, header.Count);
        Assert.Equal(1, header.H1);  // header[1] = 1 (variant marker?)
        Assert.Equal(4, header.H2);  // header[2] = 4 (= 4 attribute axes?)
    }

    [Fact]
    public void Diff4_HasExpectedHeader()
    {
        var header = RegsHeader.FromBytes(Regs16606);
        Assert.Equal(38, header.Count);
        Assert.Equal(2, header.H1);
        Assert.Equal(4, header.H2);
        Assert.Equal(12, header.H3);
    }

    [Fact]
    public void Parse_Diff1_ReturnsAllRecords()
    {
        var (header, records) = RegsReader.Parse(Regs16600);
        Assert.Equal(16, header.Count);
        Assert.Equal(16, records.Count);
    }

    [Fact]
    public void Parse_Diff4_ReturnsAllRecords()
    {
        var (header, records) = RegsReader.Parse(Regs16606);
        Assert.Equal(38, header.Count);
        Assert.Equal(38, records.Count);
    }

    [Fact]
    public void Diff4_FirstRecord_MatchesKnownValues()
    {
        // From manual analysis (Pass 26 in bubble_disasm.py):
        // REGS 16606 record[0] = (2, 3, 9, 1, 1, 0, 0, 0, 0, 0)
        var (_, records) = RegsReader.Parse(Regs16606);
        var r0 = records[0];
        Assert.Equal(2, r0.F0);
        Assert.Equal(3, r0.F1);
        Assert.Equal(9, r0.F2);
        Assert.Equal(1, r0.F3);
        Assert.Equal(1, r0.F4);  // F4 set (Hair conditional?)
        Assert.Equal(0, r0.F5);
        Assert.Equal(0, r0.F6);
        Assert.Equal(0, r0.F7);
        Assert.Equal(0, r0.F8);
        Assert.Equal(0, r0.F9);
    }

    [Fact]
    public void Direction_DetectsOneHotInF4ToF7()
    {
        // Diff 4 record[0]: F4=1 → Direction.Up
        var (_, records) = RegsReader.Parse(Regs16606);
        var r0 = records[0];
        Assert.Equal(ArrowDirection.Up, r0.Direction);

        // Diff 4 record[1]: F5=1 → Direction.Right
        var r1 = records[1];
        Assert.Equal(ArrowDirection.Right, r1.Direction);

        // Diff 4 record[3]: F7=1 → Direction.Left
        var r3 = records[3];
        Assert.Equal(ArrowDirection.Left, r3.Direction);
    }

    [Fact]
    public void IsConditional_F0_2_TrueRegardlessOfF9()
    {
        // f0=2 = Conditional (User-verifiziert 2026-05-01: ZB 0x0008 lief
        // über Mech [14] (2,8) f0=2 als Conditional ohne f9=1).
        var cond1 = new RegsRecord(2, 3, 9, 1, 1, 0, 0, 0, 0, 0);  // f0=2, f9=0
        Assert.True(cond1.IsConditional);

        var cond2 = new RegsRecord(2, 7, 4, 1, 0, 0, 1, 0, 2, 1);  // f0=2, f9=1
        Assert.True(cond2.IsConditional);

        var notCond = new RegsRecord(3, 9, 7, 1, 1, 0, 0, 0, 0, 0);  // f0=3
        Assert.False(notCond.IsConditional);
    }

    [Fact]
    public void NoDirection_AllFlagsZero_ReturnsNull()
    {
        // Build a record manually with no F4..F7 set, F0=3 (not conditional)
        var notCond = new RegsRecord(3, 3, 9, 1, 0, 0, 0, 0, 0, 0);
        Assert.Null(notCond.Direction);
        Assert.Null(notCond.ConditionalAttribute);
    }

    [Fact]
    public void IsConditional_MultipleDirectionFlags_ReturnsTrue()
    {
        // Switch-Cell hat oft mehrere Directions (z.B. f4 + f5).
        // f0=2 → Conditional, egal wie viele Direction-Bits.
        var multi = new RegsRecord(2, 3, 9, 1, 1, 1, 0, 0, 0, 0);
        Assert.True(multi.IsConditional);
        Assert.Equal(2, multi.AllDirections.Count);   // ↑ + →
    }

    [Fact]
    public void Parse_TruncatedData_Throws()
    {
        // Header says 16 records but data only has space for 5
        var truncated = new byte[RegsHeader.SizeInBytes + 5 * RegsRecord.SizeInBytes];
        // Set count = 16 in BE
        truncated[0] = 0x00;
        truncated[1] = 0x10;
        Assert.Throws<ArgumentException>(() => RegsReader.Parse(truncated));
    }

    [Fact]
    public void Resources_Difficulty4_HasThreeVariants()
    {
        Assert.Equal(3, BubblewonderRegsResources.ByDifficulty[4].Count);
        Assert.Equal(16606, BubblewonderRegsResources.ByDifficulty[4][0]);
        Assert.Equal(16607, BubblewonderRegsResources.ByDifficulty[4][1]);
        Assert.Equal(16608, BubblewonderRegsResources.ByDifficulty[4][2]);
    }

    [Fact]
    public void Resources_Resolve_ValidDifficulty()
    {
        Assert.Equal(16600, BubblewonderRegsResources.Resolve(1, 0));
        Assert.Equal(16606, BubblewonderRegsResources.Resolve(4, 0));
        Assert.Equal(16608, BubblewonderRegsResources.Resolve(4, 2));
        Assert.Equal(16609, BubblewonderRegsResources.Resolve(5, 0));
    }

    [Fact]
    public void Resources_Resolve_InvalidVariant_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BubblewonderRegsResources.Resolve(1, 99));
    }

    [Fact]
    public void Diff4_FieldDistribution_MatchesDocumentation()
    {
        // Doc says (across all 10 REGS): F0 100% nonzero, F4-F7 each ~26-31%.
        // Spot-check on Diff 4 v1 alone: F0 should always be set, F3 mostly set.
        var (_, records) = RegsReader.Parse(Regs16606);
        int f0Set = records.Count(r => r.F0 != 0);
        int f3Set = records.Count(r => r.F3 != 0);
        Assert.Equal(records.Count, f0Set);  // F0 always set per doc
        Assert.True(f3Set > records.Count * 0.85, $"F3 should be ≥85% set, was {f3Set}/{records.Count}");
    }
}
