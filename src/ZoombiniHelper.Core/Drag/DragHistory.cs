namespace ZoombiniHelper.Drag;

/// <summary>
/// Ring buffer of recent pickup events for post-mortem diagnostics.
/// The UI feeds it on every tick; the F12 dump prints the buffer so the
/// user only has to press F12 *after* a misfire — the history shows what
/// the helper recommended and what was visible.
/// </summary>
public sealed class DragHistory
{
    public readonly record struct Entry(
        DateTime At,
        byte Hair, byte Eyes, byte Nose, byte Feet,
        int? RecommendedCave,
        int PoolCount,
        string PuzzleId);

    private const int Capacity = 16;
    private readonly LinkedList<Entry> _entries = new();
    private bool _lastWasHeld;

    /// <summary>Most-recent first.</summary>
    public IEnumerable<Entry> Recent => _entries;

    /// <summary>Call every tick. Records a new entry on the rising edge of
    /// "is holding a zoombini" — i.e. when a fresh pickup begins.</summary>
    public void OnTick(PoolMember? held, int? recommendedCave, int poolCount, string puzzleId)
    {
        bool isHeld = held.HasValue;
        if (isHeld && !_lastWasHeld)
        {
            var h = held!.Value;
            _entries.AddFirst(new Entry(DateTime.Now, h.Hair, h.Eyes, h.Nose, h.Feet,
                recommendedCave, poolCount, puzzleId));
            while (_entries.Count > Capacity) _entries.RemoveLast();
        }
        _lastWasHeld = isHeld;
    }
}
