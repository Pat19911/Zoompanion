namespace ZoombiniHelper;

/// <summary>
/// Snapshot of Pizza-Trolle (pizza.mhk) state.
///
/// Each of up to three trolls (Arno, Willa, Shyler) has a list of wanted
/// topping slots. The player must serve a pizza with exactly those toppings —
/// no more, no less. We just expose the slot indices; the visual identity
/// of "slot N at difficulty D" lives in <see cref="PizzaToppings"/>.
///
/// Active-troll detection is derived from the wants arrays themselves: a
/// troll is active iff at least one of its 8 wants slots is non-zero. The
/// engine's per-troll flag bytes (documented in PIZZA_PASS.md) turned out
/// not to mean what the doc claimed — see the Aktiv-Flag-Korrektur there.
/// </summary>
public sealed class PizzaState
{
    /// <summary>1-based difficulty (1..4) — matches the user's UI selection.
    /// The engine stores it 0-based at <c>0x0049BC36</c>; we add 1 here so the
    /// helper UI matches what the player sees in the difficulty picker.</summary>
    public int Difficulty { get; }
    public TrollWants Arno   { get; }
    public TrollWants Willa  { get; }
    public TrollWants Shyler { get; }

    /// <summary>One troll's pizza preference: which of the 8 topping slots
    /// it wants. <see cref="WantedSlots"/> holds the slot indices in
    /// ascending order; <see cref="IsActive"/> is true iff that troll is
    /// currently asking for a pizza this round.</summary>
    public readonly record struct TrollWants(string Name, IReadOnlyList<int> WantedSlots)
    {
        public bool IsActive => WantedSlots.Count > 0;
    }

    public IReadOnlyList<TrollWants> ActiveTrolls
        => new[] { Arno, Willa, Shyler }.Where(t => t.IsActive).ToList();

    private PizzaState(int diff, TrollWants arno, TrollWants willa, TrollWants shyler)
    {
        Difficulty = diff;
        Arno   = arno;
        Willa  = willa;
        Shyler = shyler;
    }

    public static PizzaState Read(IMemoryReader mem)
    {
        return new PizzaState(
            // Engine: 0-based (0..3); UI: 1-based (1..4) — match the difficulty
            // picker the player sees. Verified across 6 dumps 2026-04-28.
            diff:   mem.ReadWord(PizzaMemoryMap.Difficulty) + 1,
            arno:   ReadWants(mem, "Arno",   PizzaMemoryMap.ArnoWants),
            willa:  ReadWants(mem, "Willa",  PizzaMemoryMap.WillaWants),
            shyler: ReadWants(mem, "Shyler", PizzaMemoryMap.ShylerWants));
    }

    private static TrollWants ReadWants(IMemoryReader mem, string name, nint baseVa)
    {
        var slots = new List<int>(PizzaMemoryMap.ToppingSlots);
        for (int i = 0; i < PizzaMemoryMap.ToppingSlots; i++)
            if (mem.ReadWord(baseVa + i * 2) != 0) slots.Add(i);
        return new TrollWants(name, slots);
    }
}
