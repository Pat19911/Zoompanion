namespace ZoombiniHelper;

/// <summary>
/// Watches the Stone Rise field tick by tick and records which zoombini
/// landed in each slot. The engine doesn't expose a stable per-slot zb-id
/// mapping (we tried — see analysis notes), so we infer it the same way a
/// human would: when a slot transitions empty (type 506) → filled
/// (type 508), and a zoombini was held in the previous tick, that
/// zoombini is now in the slot.
///
/// <para>Placements survive across ticks within the same puzzle session
/// but reset when the puzzle structure changes (different active-slot set
/// = different puzzle).</para>
/// </summary>
public sealed class StoneRisePlacementTracker
{
    public readonly record struct ZbAttrs(byte Hair, byte Eyes, byte Nose, byte Feet);

    private readonly Dictionary<int, ZbAttrs> _placed = new();
    private HashSet<int>? _previousFilledSlots;
    private ZbAttrs? _previousHeld;
    private string _puzzleKey = "";

    /// <summary>Slot tile-index → zoombini placed there. Read-only snapshot.</summary>
    public IReadOnlyDictionary<int, ZbAttrs> Placed => _placed;

    /// <summary>Update the tracker with this tick's snapshot. Returns true if
    /// a new placement was detected.</summary>
    public bool OnTick(StoneRiseState state, ZbAttrs? held)
    {
        // Reset everything if the puzzle layout changed (= different active
        // slot set). Easiest stable signature: sorted tile-index list.
        string key = string.Join(",", state.PairSlots.Select(p => p.TileIndex).OrderBy(i => i));
        if (key != _puzzleKey)
        {
            _puzzleKey = key;
            _placed.Clear();
            _previousFilledSlots = null;
        }

        var nowFilled = state.PairSlots.Where(p => p.IsFilled).Select(p => p.TileIndex).ToHashSet();
        bool changed = false;
        if (_previousFilledSlots is not null)
        {
            // Newly filled slots = filled now, not filled before.
            foreach (var slot in nowFilled)
            {
                if (_previousFilledSlots.Contains(slot)) continue;
                if (_placed.ContainsKey(slot)) continue;
                // Newly filled. Attribute it to whatever was held just before
                // this tick — that's the zoombini the player dropped here.
                if (_previousHeld is { } prev)
                {
                    _placed[slot] = prev;
                    changed = true;
                }
            }
            // Slots emptied (placed zb removed somehow): drop tracked entry.
            foreach (var slot in _placed.Keys.ToList())
                if (!nowFilled.Contains(slot)) { _placed.Remove(slot); changed = true; }
        }
        _previousFilledSlots = nowFilled;
        _previousHeld = held;
        return changed;
    }

    /// <summary>Forget all placements (e.g. after the user resets the puzzle
    /// without the layout changing).</summary>
    public void Reset()
    {
        _placed.Clear();
        _previousFilledSlots = null;
        _previousHeld = null;
    }

    /// <summary>Adopt a placement that was discovered out-of-band (= the
    /// <see cref="StoneRisePlacedResolver"/> looked it up from heap state
    /// because the helper attached mid-puzzle and missed the live transition).
    /// Returns true if the entry was new. Live transitions take precedence:
    /// we never overwrite a slot we've already tracked ourselves.</summary>
    public bool Adopt(int slotTile, ZbAttrs attrs)
    {
        if (_placed.ContainsKey(slotTile)) return false;
        _placed[slotTile] = attrs;
        return true;
    }
}
