using System.Drawing;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Dispatches puzzle-detection results to the right per-puzzle renderer.
/// Anything without a registered renderer falls through to
/// <see cref="PuzzleDisplayRenderer"/>, which has a different signature
/// because it doesn't depend on <see cref="IMemoryReader"/>.
/// </summary>
public sealed class OverlayRenderer
{
    private readonly Dictionary<PuzzleId, IPuzzleRenderer> _byId = new();
    private readonly PuzzleDisplayRenderer _fallback;

    public OverlayRenderer(PuzzleDisplayRenderer fallback,
                           IEnumerable<IPuzzleRenderer> specific)
    {
        _fallback = fallback;
        foreach (var r in specific) _byId[r.Handles] = r;
    }

    /// <summary>Renders the supplied detection. <paramref name="result"/> with
    /// <c>IsActive == false</c> writes a "no puzzle" panel.</summary>
    public void Render(PuzzleManager.DetectResult result, IMemoryReader mem,
                       IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        if (!result.IsActive)
        {
            labels.TitleColor = Color.FromArgb(200, 180, 140);
            labels.Title      = Loc.T("nopuzzle.title");
            labels.Body       = Loc.T("nopuzzle.body");
            return;
        }
        if (_byId.TryGetValue(result.Id, out var renderer))
            renderer.Render(result.Detector!, result.Detection, mem, pool, labels);
        else
            _fallback.Render(result.Detector!, result.Detection, mem, pool, labels);
    }

    /// <summary>Forwards an F1 cycle to the active puzzle's renderer if it
    /// supports hint cycling. Silent no-op for puzzles without staged hints
    /// or when no puzzle is active.</summary>
    public void CycleHintsFor(PuzzleId id)
    {
        if (_byId.TryGetValue(id, out var renderer) && renderer is IHintCyclingRenderer h)
            h.CycleHintLevel();
    }
}
