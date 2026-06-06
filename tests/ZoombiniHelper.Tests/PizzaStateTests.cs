using ZoombiniHelper;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Tests for the Pizza-Trolle state reader. Fixtures are derived from four
/// live dumps captured 2026-04-28 (memdump-155353, -155418, -155435,
/// -155450) — one per difficulty level. The dumps are abridged into a
/// FakeMemory below; only the pizza-relevant addresses are populated.
/// </summary>
public class PizzaStateTests
{
    // Difficulty values throughout the tests are 1-based (1..4) — same as
    // the engine's 0-based 0x49BC36 + 1, matching the in-game UI selection.
    // Verified mapping across 4 user dumps captured 2026-04-28T17:19..17:21.

    [Fact]
    public void Schwierigkeit1_OneTrollOnly_Arno_ReadsThreeWants()
    {
        var mem = NewMem(rawDifficulty: 0,
            arnoSlots:   new[] { 1, 2, 3 },
            willaSlots:  Array.Empty<int>(),
            shylerSlots: Array.Empty<int>());
        var pizza = PizzaState.Read(mem);

        Assert.Equal(1, pizza.Difficulty);
        Assert.Single(pizza.ActiveTrolls);
        Assert.Equal("Arno", pizza.ActiveTrolls[0].Name);
        Assert.Equal(new[] { 1, 2, 3 }, pizza.Arno.WantedSlots);
        Assert.False(pizza.Willa.IsActive);
        Assert.False(pizza.Shyler.IsActive);
    }

    [Fact]
    public void Schwierigkeit2_TwoTrolls_Slot4Skipped()
    {
        var mem = NewMem(rawDifficulty: 1,
            arnoSlots:   new[] { 1, 2 },
            willaSlots:  new[] { 3, 5, 6 },
            shylerSlots: Array.Empty<int>());
        var pizza = PizzaState.Read(mem);

        Assert.Equal(2, pizza.Difficulty);
        Assert.Equal(2, pizza.ActiveTrolls.Count);
        Assert.DoesNotContain(4, pizza.Arno.WantedSlots);
        Assert.DoesNotContain(4, pizza.Willa.WantedSlots);
    }

    [Fact]
    public void Schwierigkeit3_ThreeTrolls_AllActive()
    {
        // Live dump shows shyler also wanting toppings at this difficulty.
        var mem = NewMem(rawDifficulty: 2,
            arnoSlots:   new[] { 2 },
            willaSlots:  new[] { 0, 1, 3, 4, 6 },
            shylerSlots: new[] { 5 });
        var pizza = PizzaState.Read(mem);

        Assert.Equal(3, pizza.Difficulty);
        Assert.Equal(3, pizza.ActiveTrolls.Count);
    }

    [Fact]
    public void Schwierigkeit4_AllEightSlotsAssigned_AcrossThreeTrolls()
    {
        var mem = NewMem(rawDifficulty: 3,
            arnoSlots:   new[] { 1, 7 },
            willaSlots:  new[] { 0, 4, 5, 6 },
            shylerSlots: new[] { 2, 3 });
        var pizza = PizzaState.Read(mem);

        Assert.Equal(4, pizza.Difficulty);
        var allWanted = pizza.ActiveTrolls.SelectMany(t => t.WantedSlots).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, allWanted);
    }

    [Fact]
    public void NoTrollWantsAnything_ReturnsEmptyActiveList()
    {
        var mem = NewMem(rawDifficulty: 0,
            arnoSlots:   Array.Empty<int>(),
            willaSlots:  Array.Empty<int>(),
            shylerSlots: Array.Empty<int>());
        var pizza = PizzaState.Read(mem);
        Assert.Empty(pizza.ActiveTrolls);
    }

    [Fact]
    public void Toppings_UnknownDifficulty_FallsBackToSlotN()
    {
        // No Visible entry for difficulty 5 → bare slot fallback.
        Assert.Equal("Slot 0", PizzaToppings.Label(difficulty: 5, slot: 0));
    }

    [Fact]
    public void Toppings_MappedSlot_AppendsNameInParens()
    {
        Assert.StartsWith("Position 1 (", PizzaToppings.Label(difficulty: 4, slot: 0));
    }

    [Fact]
    public void Toppings_Schwierigkeit2_SkipsSlot4_PositionFiveIsSlot5()
    {
        // The skip in the visible list means memory slot 5 shows as
        // Position 5 (not 6) — this is the whole point of using the
        // visible-button order rather than slot+1.
        Assert.Equal("Position 5 (Cherry (ice cream))",
            PizzaToppings.Label(difficulty: 2, slot: 5));
        Assert.Equal("Position 6 (Cream (ice cream))",
            PizzaToppings.Label(difficulty: 2, slot: 6));
    }

    [Fact]
    public void Toppings_Schwierigkeit2_Slot4_NotInVisibleList()
    {
        // Slot 4 is intentionally absent from Schwierigkeit 2's visible list.
        // If a wants-array ever contained it (shouldn't, by skip_slot=4), we
        // fall back to a label that signals the anomaly to the user.
        var label = PizzaToppings.Label(difficulty: 2, slot: 4);
        Assert.Contains("nicht in der Auswahl", label);
    }

    /// <summary>Builds a sparse FakeMemory containing only the pizza addresses.
    /// Anything else read returns 0/empty — same as the live dump format.</summary>
    private static FakeMemory NewMem(int rawDifficulty, int[] arnoSlots, int[] willaSlots, int[] shylerSlots)
    {
        var mem = new FakeMemory();
        mem.SetWord(PizzaMemoryMap.Difficulty, (ushort)rawDifficulty);
        foreach (var s in arnoSlots)   mem.SetWord(PizzaMemoryMap.ArnoWants   + s * 2, 1);
        foreach (var s in willaSlots)  mem.SetWord(PizzaMemoryMap.WillaWants  + s * 2, 1);
        foreach (var s in shylerSlots) mem.SetWord(PizzaMemoryMap.ShylerWants + s * 2, 1);
        return mem;
    }

    private sealed class FakeMemory : IMemoryReader
    {
        private readonly Dictionary<nint, ushort> _words = new();
        public void SetWord(nint va, ushort v) => _words[va] = v;
        public ushort ReadWord(nint va) => _words.TryGetValue(va, out var v) ? v : (ushort)0;
        public byte ReadByte(nint va) => (byte)(ReadWord(va) & 0xFF);
        public byte[]? ReadBytes(nint va, int count) => null;
    }
}
