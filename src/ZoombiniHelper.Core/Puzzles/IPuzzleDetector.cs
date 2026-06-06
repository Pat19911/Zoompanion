namespace ZoombiniHelper.Puzzles;

/// <summary>
/// One detection sample from a single <see cref="IPuzzleDetector"/>.
/// </summary>
/// <param name="IsActive">True if the detector believes its puzzle is currently loaded.</param>
/// <param name="Confidence">0..100 — how strong the evidence is. Engagement flag alone = 30,
/// engagement + plausible data = 70, engagement + plausible data + a unique marker = 90+.
/// Used to disambiguate when multiple detectors fire (sticky engagement flags happen).</param>
/// <param name="Signature">Short hash of the puzzle-specific data. Changes when a puzzle
/// (re)loads. The <see cref="PuzzleManager"/> uses signature changes to break ties between
/// equally-confident detectors — the most-recently-changed one is "fresh" and active.</param>
/// <param name="Reason">Human-readable explanation for diagnostics. e.g.
/// "engagement=1, rules=[3=0x05] (valid)".</param>
public readonly record struct PuzzleDetection(
    bool IsActive,
    int Confidence,
    ulong Signature,
    string Reason)
{
    public static PuzzleDetection Inactive(string reason) =>
        new(false, 0, 0, reason);
}

/// <summary>
/// Detects whether a single specific puzzle is currently loaded in the running game.
///
/// Implementations must be pure (no caching beyond the IMemoryReader call) — the
/// <see cref="PuzzleManager"/> handles state across ticks (signature/freshness).
/// </summary>
public interface IPuzzleDetector
{
    PuzzleId Id { get; }
    /// <summary>Canonical lowercase MHK filename, e.g. "bridge.mhk". Mostly diagnostic.</summary>
    string MhkName { get; }
    /// <summary>German display name, shown in the overlay title.</summary>
    string DisplayName { get; }
    PuzzleDetection Detect(IMemoryReader mem);
}

/// <summary>
/// Display style for a single puzzle / location: title text, body line, accent
/// colour. Lives next to the detection metadata so the UI doesn't need a switch
/// statement per location — it just looks up the style for the active puzzle.
/// </summary>
/// <param name="Title">e.g. "🗺  Auf der Karte"</param>
/// <param name="Body">e.g. "Wähle ein Puzzle." (multi-line OK)</param>
/// <param name="AccentArgb">title-bar foreground colour, packed ARGB</param>
public readonly record struct PuzzleDisplayStyle(string Title, string Body, uint AccentArgb);
