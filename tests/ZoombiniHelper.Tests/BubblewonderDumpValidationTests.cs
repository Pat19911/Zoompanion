using Xunit;
using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Bubblewonder.Simulator;
using ZoombiniHelper.Diagnostics;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Validation der BubblewonderState-Klasse gegen die 4 vorhandenen
/// memdumps (verschiedene Difficulties). Skip wenn Dumps nicht verfügbar
/// (= CI / andere Maschinen).
///
/// <para>Die Dumps haben nur die .data section, keinen Heap. Daher kann
/// REGS nicht von der Heap-Pointer dereferenziert werden — Tests fokussieren
/// auf Difficulty + Counter-Tabellen + Variation-Counter, alles in .data.</para>
/// </summary>
public class BubblewonderDumpValidationTests
{
    // Local-only validation: point ZOOMBINI_DUMP_DIR at a folder holding the
    // four reference memdumps to run it. Unset (CI / other machines) → the
    // tests skip cleanly. No hardcoded personal path.
    private static readonly string DumpDir =
        Environment.GetEnvironmentVariable("ZOOMBINI_DUMP_DIR") ?? "";

    private static (string Path, int ExpectedDiff)[] DumpExpectations = new[]
    {
        ("memdump-213610.txt", 1),
        ("memdump-213625.txt", 2),
        ("memdump-213642.txt", 3),
        ("memdump-213702.txt", 4),
    };

    private static bool DumpsAvailable => Directory.Exists(DumpDir) &&
        DumpExpectations.All(d => File.Exists(Path.Combine(DumpDir, d.Path)));

    /// <summary>Run-once when dumps unavailable: don't fail tests, just no-op.
    /// Better than xunit.SkippableFact dependency for a single use case.</summary>
    private static bool RequireDumps()
    {
        if (!DumpsAvailable) return false;
        return true;
    }

    [Fact]
    public void AllFourDumps_ReadDifficultyMatchesExpected()
    {
        if (!RequireDumps()) return;
        foreach (var (path, expectedDiff) in DumpExpectations)
        {
            var reader = new DumpFileReader(Path.Combine(DumpDir, path));
            var state = BubblewonderState.Read(reader);
            Assert.Equal(expectedDiff, state.Difficulty);
        }
    }

    [Fact]
    public void AllFourDumps_DiffCacheIsZeroBased()
    {
        if (!RequireDumps()) return;
        foreach (var (path, expectedDiff) in DumpExpectations)
        {
            var reader = new DumpFileReader(Path.Combine(DumpDir, path));
            var state = BubblewonderState.Read(reader);
            Assert.Equal(expectedDiff - 1, state.DifficultyCache);
        }
    }

    [Fact]
    public void AllFourDumps_ResolveResourceIdConsistent()
    {
        if (!RequireDumps()) return;
        foreach (var (path, expectedDiff) in DumpExpectations)
        {
            var reader = new DumpFileReader(Path.Combine(DumpDir, path));
            var state = BubblewonderState.Read(reader);
            Assert.Contains(state.RegsResourceId,
                BubblewonderRegsResources.ByDifficulty[expectedDiff]);
        }
    }

    [Fact]
    public void AllFourDumps_PositionTablesParseable()
    {
        if (!RequireDumps()) return;
        foreach (var (path, _) in DumpExpectations)
        {
            var reader = new DumpFileReader(Path.Combine(DumpDir, path));
            var state = BubblewonderState.Read(reader);
            Assert.NotNull(state.PositionCounters);
            Assert.NotNull(state.PositionHandles);
        }
    }

    /// <summary>Der GridModelBuilder muss die Ziel-Steininsel (oben-rechts) aus
    /// der engine-eigenen Zelltyp-Tabelle (0x499efc, Typ 0x17) extrahieren — das
    /// behebt den Kernbug „jeder Gitter-Austritt = Score". Verifiziert gegen einen
    /// echten Live-Dump: Ziel ist immer (10,0)(10,1)(10,2)(11,2).</summary>
    [Fact]
    public void GridModelBuilder_ExtractsGoalCells_FromRealDumpCellTypeTable()
    {
        // Irgendein Bubblewonder-Dump mit .data-Sektion genügt — die Steinzellen
        // sind layout-unabhängig fest. Nimm den ersten verfügbaren.
        string? dump = Directory.Exists(DumpDir)
            ? Directory.EnumerateFiles(DumpDir, "memdump-*.txt")
                .FirstOrDefault(p => File.ReadAllText(p).Contains("winner: BubblewonderAbyss"))
            : null;
        if (dump is null) return;  // keine Dumps verfügbar (CI / andere Maschine)

        var reader = new DumpFileReader(dump);
        var grid = BubblewonderGridModelBuilder.FromBubbles(
            System.Array.Empty<BubbleObject>(), regsResourceId: 16606, reader);

        var goals = System.Linq.Enumerable.Range(0, 12 * 13)
            .Where(p => grid.CellAt(p).Type == MechanismType.Goal)
            .OrderBy(p => p)
            .ToArray();

        // (10,0)=130, (10,1)=131, (10,2)=132, (11,2)=145
        Assert.Equal(new[] { 130, 131, 132, 145 }, goals);
    }
}
