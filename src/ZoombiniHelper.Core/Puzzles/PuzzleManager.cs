namespace ZoombiniHelper.Puzzles;

/// <summary>
/// Aggregates detectors for all known puzzles and decides which one is currently active.
///
/// Each MHK-buffer detector is exclusive (only one puzzle's buffer is non-zero at a
/// time), so the active detector usually wins outright. The freshness/sticky-lock
/// machinery below covers the brief tick during a puzzle switch where another
/// detector might also see its buffer set, plus the case where two detectors both
/// report active because of address overlap.
///
/// Selection rules:
///   1. Any active detector with a FRESH signature change wins (just (re)loaded).
///   2. No fresh contender → previous sticky winner stays as long as it's still
///      active. This keeps the UI calm while a puzzle is being played.
///   3. Sticky winner has gone inactive → highest-confidence active detector wins.
///
/// "No active detector" returns the sentinel <see cref="DetectResult.None"/>.
/// </summary>
public sealed class PuzzleManager
{
    public sealed record DetectResult(
        PuzzleId Id,
        IPuzzleDetector? Detector,
        PuzzleDetection Detection)
    {
        public static readonly DetectResult None =
            new(PuzzleId.None, null, PuzzleDetection.Inactive("no detector reported active"));
        public bool IsActive => Detector is not null && Detection.IsActive;
    }

    public sealed record DiagnosticEntry(
        IPuzzleDetector Detector,
        PuzzleDetection Detection,
        DateTime SignatureChangedAt)
    {
        public TimeSpan Age(DateTime now) => now - SignatureChangedAt;
        public bool IsFresh(DateTime now) => Age(now) <= FreshnessWindow;
    }

    /// <summary>Time window after a signature change during which a puzzle counts
    /// as "fresh". 3 s covers one slow tick plus a few jitter ticks.</summary>
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromSeconds(3);

    private readonly IReadOnlyList<IPuzzleDetector> _detectors;
    private readonly Dictionary<PuzzleId, (ulong sig, DateTime changedAt)> _signatures = new();
    private PuzzleId _stickyWinner = PuzzleId.None;

    public PuzzleManager(IEnumerable<IPuzzleDetector> detectors)
        => _detectors = detectors.ToList();

    /// <summary>One full sweep of every detector. Updates the freshness map as a
    /// side effect and returns the winning detection per the rules above.</summary>
    public DetectResult Detect(IMemoryReader mem)
    {
        var now = DateTime.UtcNow;
        var samples = SampleAll(mem, now, updateState: true);

        var active = samples.Where(s => s.Detection.IsActive).ToList();
        if (active.Count == 0)
        {
            _stickyWinner = PuzzleId.None;
            return DetectResult.None;
        }

        var fresh = active.Where(s => s.IsFresh(now)).ToList();
        DiagnosticEntry top;
        if (fresh.Count > 0)
        {
            fresh.Sort(static (a, b) =>
            {
                int byChange = b.SignatureChangedAt.CompareTo(a.SignatureChangedAt);
                return byChange != 0 ? byChange : b.Detection.Confidence.CompareTo(a.Detection.Confidence);
            });
            top = fresh[0];
            _stickyWinner = top.Detector.Id;
        }
        else if (_stickyWinner != PuzzleId.None &&
                 active.FirstOrDefault(s => s.Detector.Id == _stickyWinner) is { } prev)
        {
            top = prev;
        }
        else
        {
            active.Sort(static (a, b) => b.Detection.Confidence.CompareTo(a.Detection.Confidence));
            top = active[0];
            _stickyWinner = top.Detector.Id;
        }
        return new DetectResult(top.Detector.Id, top.Detector, top.Detection);
    }

    /// <summary>Read-only snapshot of every detector's current verdict — for the
    /// F12 diagnostic dump. Doesn't update the manager's freshness state.</summary>
    public IReadOnlyList<DiagnosticEntry> Diagnose(IMemoryReader mem)
        => SampleAll(mem, DateTime.UtcNow, updateState: false);

    private List<DiagnosticEntry> SampleAll(IMemoryReader mem, DateTime now, bool updateState)
    {
        var rows = new List<DiagnosticEntry>(_detectors.Count);
        foreach (var det in _detectors)
        {
            PuzzleDetection res;
            try { res = det.Detect(mem); }
            catch (Exception ex) { res = PuzzleDetection.Inactive($"read failed: {ex.GetType().Name}"); }

            DateTime changedAt;
            if (_signatures.TryGetValue(det.Id, out var prev) && prev.sig == res.Signature)
            {
                changedAt = prev.changedAt;
            }
            else
            {
                changedAt = now;
                if (updateState) _signatures[det.Id] = (res.Signature, now);
            }
            rows.Add(new DiagnosticEntry(det, res, changedAt));
        }
        return rows;
    }
}
