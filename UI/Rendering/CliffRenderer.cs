using System.Drawing;
using System.Text;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Bridge-recommendation helper for "Allergische Klippen". Lists the allergy
/// rules and the pool with per-zoombini hints. Held-zoombini identification
/// is exact: <see cref="HeldZoombini.Find"/> reads the engine's drag marker.
/// </summary>
public sealed class CliffRenderer : IPuzzleRenderer
{
    public PuzzleId Handles => PuzzleId.AllergicCliffs;

    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var cliff = CliffState.Read(mem);
        var held  = HeldZoombini.Find(mem);
        labels.Title = Loc.T("cliff.title", DifficultyLabel(cliff.Difficulty));
        labels.TitleColor = Color.FromArgb(140, 220, 140);
        labels.Body = BuildBody(cliff, pool, held);
    }

    private static string DifficultyLabel(int difficulty) => difficulty switch
    {
        >= 1 and <= 4 => Loc.T($"cliff.diff.{difficulty}"),
        _ => Loc.T("cliff.diff.n", difficulty),
    };

    private static string BuildBody(CliffState cliff, IReadOnlyList<PoolMember> pool, PoolMember? held)
    {
        var sb = new StringBuilder();

        if (held is { } h)
        {
            bool match  = PoolScanner.MatchesAnyAllergy(h, cliff.Rules);
            string verdict = match
                ? Loc.T("cliff.held.mustOn", cliff.AcceptingBridgeLabel)
                : Loc.T("cliff.held.mayOn", cliff.RejectingBridgeLabel);
            sb.AppendLine(Loc.T("held.header"));
            sb.AppendLine(Loc.T("held.row", Loc.T("attr.hair"), ZoombiniVariants.VariantName(ZoombiniVariants.Hair, h.Hair)));
            sb.AppendLine(Loc.T("held.row", Loc.T("attr.eyes"), ZoombiniVariants.VariantName(ZoombiniVariants.Eyes, h.Eyes)));
            sb.AppendLine(Loc.T("held.row", Loc.T("attr.nose"), ZoombiniVariants.VariantName(ZoombiniVariants.Nose, h.Nose)));
            sb.AppendLine(Loc.T("held.row", Loc.T("attr.feet"), ZoombiniVariants.VariantName(ZoombiniVariants.Feet, h.Feet)));
            sb.AppendLine();
            sb.AppendLine($"  {verdict}");
            sb.AppendLine(Loc.T("held.footer"));
            sb.AppendLine();
        }

        string oneEnough = cliff.Rules.Count > 1 ? Loc.T("cliff.sneezes.oneEnough") : "";
        sb.AppendLine(Loc.T("cliff.sneezes", cliff.RejectingBridgeLabel, oneEnough));
        foreach (var rule in cliff.Rules)
            sb.AppendLine(Loc.T("rule.bullet",
                ZoombiniVariants.AttributeName(rule.Type),
                ZoombiniVariants.DescribeRuleValue(rule.Type, rule.Value)));
        sb.AppendLine();
        if (pool.Count > 0)
        {
            sb.AppendLine(Loc.T("cliff.pool", pool.Count));
            foreach (var zb in pool.Take(16))
            {
                bool matches = PoolScanner.MatchesAnyAllergy(zb, cliff.Rules);
                string mark  = matches ? "↑" : "↓";
                string label = matches ? cliff.AcceptingBridgeAbbr : cliff.RejectingBridgeAbbr;
                sb.AppendLine($"  {mark}{label} y{zb.YPosition,3}: {zb.Hair},{zb.Eyes},{zb.Nose},{zb.Feet}");
            }
        }
        else sb.AppendLine(Loc.T("cliff.poolScanning"));
        sb.AppendLine();
        sb.Append(Loc.T("cliff.attempts", cliff.Attempts));
        return sb.ToString();
    }
}
