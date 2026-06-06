namespace ZoombiniHelper;

/// <summary>
/// Per-difficulty list of visible pizza toppings, in the order they appear
/// in the on-screen button strip.
///
/// Each entry pairs a memory slot index (0..7, used in the wants[] arrays)
/// with the topping's visual identity. The list's order IS the visual order
/// — the N-th entry (1-based) is the N-th button the player sees.
///
/// At Schwierigkeit 2 the engine never assigns slot 4 (skip_slot=4), and the
/// button is also visually absent: only 6 buttons appear, mapping to slots
/// {0,1,2,3,5,6}. This means the player's "5th button" is actually slot 5
/// in memory — without this mapping the helper would say "Topping Nr. 6"
/// and confuse the player.
///
/// At Schwierigkeiten 1, 3, 4 there is no skip and the mapping is identity
/// (visual position N = slot N-1).
///
/// Topping names are filled in by the user as they identify them in-game.
/// Empty entries fall back to a positional label.
/// </summary>
public static class PizzaToppings
{
    public readonly record struct Topping(int MemorySlot, string? Name);

    /// <summary>Visible toppings per 1-based UI difficulty (1..4), in
    /// left-to-right button order. Index <c>i</c> in the array is the
    /// <c>(i+1)</c>-th button on screen.</summary>
    public static readonly Dictionary<int, Topping[]> Visible = new()
    {
        [1] = new Topping[]
        {
            new(0, "Black dots (Olives?)"),
            new(1, "Green stuff (Paprika, or Jalapeños?)"),
            new(2, "Longish black stuff (Tuna?)"),
            new(3, "Mushrooms"),
            new(4, "Cheese"),
        },
        [2] = new Topping[]
        {
            new(0, "Black dots (Olives?)"),
            new(1, "Green stuff (Paprika?)"),
            new(2, "Longish black stuff (Tuna?)"),
            new(3, "Mushrooms"),
            // slot 4 is skipped — no visible button at this difficulty
            new(5, "Cherry (ice cream)"),
            new(6, "Cream (ice cream)"),
        },
        [3] = new Topping[]
        {
            new(0, "Black dots (Olives?)"),
            new(1, "Green stuff (Paprika, or Jalapeños?)"),
            new(2, "Longish black stuff (Tuna?)"),
            new(3, "Mushrooms"),
            new(4, "Cheese"),
            new(5, "Cherry (ice cream)"),
            new(6, "Cream (ice cream)"),
        },
        [4] = new Topping[]
        {
            new(0, "Black dots (Olives?)"),
            new(1, "Green stuff (Paprika, or Jalapeños?)"),
            new(2, "Longish black stuff (Tuna?)"),
            new(3, "Mushrooms"),
            new(4, "Cheese"),
            new(5, "Cherry (ice cream)"),
            new(6, "Cream (ice cream)"),
            new(7, "Chocolate (ice cream)"),
        },
    };

    /// <summary>Render a label for a wanted slot at a given difficulty:
    /// "Position N (Name)" or just "Position N" if the topping isn't named yet.
    /// Falls back to "Slot N (nicht in der Auswahl?)" if the slot isn't in
    /// the visible list for this difficulty (shouldn't happen with valid data).</summary>
    public static string Label(int difficulty, int slot)
    {
        if (!Visible.TryGetValue(difficulty, out var list)) return $"Slot {slot}";
        for (int i = 0; i < list.Length; i++)
            if (list[i].MemorySlot == slot)
                return list[i].Name is null
                    ? $"Position {i + 1}"
                    : $"Position {i + 1} ({list[i].Name})";
        return $"Slot {slot} (nicht in der Auswahl?)";
    }

    /// <summary>List of memory-slot indices that have no display name yet for
    /// this difficulty — used by the renderer to nudge the user to fill them in.</summary>
    public static IEnumerable<int> UnnamedSlots(int difficulty)
    {
        if (!Visible.TryGetValue(difficulty, out var list)) yield break;
        foreach (var t in list)
            if (t.Name is null) yield return t.MemorySlot;
    }
}
