using System.Drawing;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Carries the labels the renderer writes into. Passed by reference so the
/// renderer doesn't need to know about the Form's internals.
/// </summary>
public sealed class OverlayLabels
{
    public string Title  { get; set; } = "";
    public Color  TitleColor { get; set; } = Color.WhiteSmoke;
    public string Body   { get; set; } = "";

    /// <summary>Optional grid painter — when set, the helper window allocates
    /// a graphics panel below the body and invokes this on every repaint.
    /// The painter receives a clean Graphics surface and the panel's
    /// client rectangle. Setting this to null hides the panel.</summary>
    public Action<System.Drawing.Graphics, System.Drawing.Rectangle>? PaintGrid { get; set; }

    /// <summary>How tall the grid panel should be in pixels. Renderers that
    /// set <see cref="PaintGrid"/> should also pick a sensible height.</summary>
    public int GridHeight { get; set; } = 0;
}

/// <summary>
/// Renders the overlay UI for one specific puzzle. Implementations are
/// picked by <see cref="OverlayRenderer"/> based on the active PuzzleId.
/// Pure read-only on the inputs — no side effects, no state.
/// </summary>
public interface IPuzzleRenderer
{
    /// <summary>Which puzzle this renderer handles.</summary>
    PuzzleId Handles { get; }
    void Render(IPuzzleDetector detector, PuzzleDetection detection,
                IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels);
}

/// <summary>Optional facet for renderers that support a cycling hint level
/// (F1 = next stage). The overlay queries this via the renderer registry
/// without knowing which puzzle is active — keeps HelperOverlay free of
/// per-puzzle wiring.</summary>
public interface IHintCyclingRenderer : IPuzzleRenderer
{
    void CycleHintLevel();
}
