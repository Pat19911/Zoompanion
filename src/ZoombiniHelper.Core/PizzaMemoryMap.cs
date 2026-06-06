namespace ZoombiniHelper;

/// <summary>
/// Static virtual addresses for Pizza-Trolle (pizza.mhk) state in the v2
/// binary. ImageBase = 0x00400000.
///
/// All offsets cross-checked against four live dumps (one per difficulty,
/// captured 2026-04-28). The three <c>wants[]</c> arrays + DIFFICULTY are
/// the only addresses we actually need to render the helper — the
/// "active troll" status is derived from the wants arrays themselves
/// (any non-zero entry → that troll is active), which is more reliable
/// than the engine's per-troll flag bytes (see PIZZA_PASS.md, "Aktiv-Flag
/// Korrektur").
/// </summary>
public static class PizzaMemoryMap
{
    /// <summary>0-based difficulty (0=Easy, 1 troll .. 3=Very Hard, 3 trolls).
    /// Verified across 4 live dumps. At Diff 0 the word is plain zero (i.e.
    /// reads as 0, not as a sentinel like 0xFFFF).</summary>
    public const nint Difficulty = 0x0049BC36;

    /// <summary>Arno's wanted-toppings array — 8 words, each 0 or 1.
    /// wants[i] is at <c>ArnoWants + 2*i</c>. Verified: array length is
    /// always 8 even when fewer slots are visible (extra slots stay 0).</summary>
    public const nint ArnoWants   = 0x0049BA34;
    public const nint WillaWants  = 0x0049BB44;
    public const nint ShylerWants = 0x0049BC38;

    /// <summary>Number of topping slots in each wants[] array. Constant
    /// across all difficulties; only the visible/active subset varies.</summary>
    public const int  ToppingSlots = 8;
}
