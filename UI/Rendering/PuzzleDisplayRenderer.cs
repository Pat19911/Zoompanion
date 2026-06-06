using System.Drawing;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Catch-all renderer for puzzles/locations that only need a styled title +
/// body line — no per-puzzle solver yet. Looks the style up from
/// <see cref="PuzzleDisplay"/>; if there is none, shows a generic
/// "Helper folgt" panel with the detector's metadata and the current pool.
///
/// Not in the per-puzzle dictionary — picked explicitly as fallback by
/// <see cref="OverlayRenderer"/>, so it doesn't implement
/// <see cref="IPuzzleRenderer"/>.
/// </summary>
public sealed class PuzzleDisplayRenderer
{
    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var held = HeldZoombini.Find(mem);

        if (PuzzleDisplay.StyleFor(detector.Id) is { } style)
        {
            labels.TitleColor = Color.FromArgb(unchecked((int)style.AccentArgb));
            labels.Title      = style.Title;
            labels.Body       = held is { } hh ? FormatHeldBlock(hh) + "\n" + style.Body : style.Body;
            return;
        }

        labels.TitleColor = Color.FromArgb(180, 200, 140);
        labels.Title      = Loc.T("fallback.title", detector.DisplayName);

        var sb = new System.Text.StringBuilder();
        if (held is { } h) sb.AppendLine(FormatHeldBlock(h));
        sb.AppendLine(Loc.T("fallback.detected", detector.DisplayName));
        sb.AppendLine(Loc.T("fallback.confidence", detection.Confidence));
        sb.AppendLine(Loc.T("fallback.evidence", detection.Reason));
        sb.AppendLine();
        if (pool.Count > 0)
        {
            sb.AppendLine(Loc.T("pool.generic", pool.Count));
            foreach (var zb in pool.Take(16))
                sb.AppendLine($"  y{zb.YPosition,3}: {zb.Hair},{zb.Eyes},{zb.Nose},{zb.Feet}");
        }
        labels.Body = sb.ToString();
    }

    private static string FormatHeldBlock(PoolMember h) =>
        Loc.T("held.header") + "\n"
      + Loc.T("held.row", Loc.T("attr.hair"), ZoombiniVariants.VariantName(ZoombiniVariants.Hair, h.Hair)) + "\n"
      + Loc.T("held.row", Loc.T("attr.eyes"), ZoombiniVariants.VariantName(ZoombiniVariants.Eyes, h.Eyes)) + "\n"
      + Loc.T("held.row", Loc.T("attr.nose"), ZoombiniVariants.VariantName(ZoombiniVariants.Nose, h.Nose)) + "\n"
      + Loc.T("held.row", Loc.T("attr.feet"), ZoombiniVariants.VariantName(ZoombiniVariants.Feet, h.Feet)) + "\n"
      + Loc.T("held.footer") + "\n";
}
