using System.Drawing;
using System.Text;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Helper for "Steinwächter" (Stone Cold Caves). Renders the 2D-grid filter
/// (up to 4 caves) and tells the player which cave will accept the held
/// zoombini.
/// </summary>
public sealed class CavesRenderer : IPuzzleRenderer
{
    public PuzzleId Handles => PuzzleId.StoneColdCaves;

    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var caves = CavesState.Read(mem);
        var held  = HeldZoombini.Find(mem);

        labels.Title = Loc.T("caves.title", DifficultyLabel(caves.Difficulty));
        labels.TitleColor = Color.FromArgb(180, 200, 230);
        labels.Body = BuildBody(caves, pool, held);
    }

    private static string DifficultyLabel(int d) => d switch
    {
        >= 1 and <= 4 => Loc.T($"caves.diff.{d}"),
        _ => Loc.T("caves.diff.n", d),
    };

    private static string BuildBody(CavesState caves, IReadOnlyList<PoolMember> pool, PoolMember? held)
    {
        var sb = new StringBuilder();

        if (held is { } h)
        {
            int? cave = caves.FindAcceptingCave(h);
            sb.AppendLine(Loc.T("held.header"));
            sb.AppendLine(Loc.T("held.row.num", Loc.T("attr.hair"), h.Hair, ZoombiniVariants.VariantName(ZoombiniVariants.Hair, h.Hair)));
            sb.AppendLine(Loc.T("held.row.num", Loc.T("attr.eyes"), h.Eyes, ZoombiniVariants.VariantName(ZoombiniVariants.Eyes, h.Eyes)));
            sb.AppendLine(Loc.T("held.row.num", Loc.T("attr.nose"), h.Nose, ZoombiniVariants.VariantName(ZoombiniVariants.Nose, h.Nose)));
            sb.AppendLine(Loc.T("held.row.num", Loc.T("attr.feet"), h.Feet, ZoombiniVariants.VariantName(ZoombiniVariants.Feet, h.Feet)));
            sb.AppendLine();
            sb.AppendLine(Loc.T("caves.held.cave", cave));
            sb.AppendLine(Loc.T("held.footer"));
            sb.AppendLine();
        }

        if (caves.IsActive)
        {
            string a1Yes = AxisAsClause(caves.Axis1, negated: false);
            string a1No  = AxisAsClause(caves.Axis1, negated: true);
            if (caves.AxisCount >= 2)
            {
                string a2Yes = AxisAsClause(caves.Axis2, negated: false);
                string a2No  = AxisAsClause(caves.Axis2, negated: true);
                sb.AppendLine(Loc.T("caves.letThrough"));
                sb.AppendLine(Loc.T("caves.row.bottom12", a1Yes));
                sb.AppendLine(Loc.T("caves.row.bottom34", a1No));
                sb.AppendLine(Loc.T("caves.row.top14", a2Yes));
                sb.AppendLine(Loc.T("caves.row.top23", a2No));
            }
            else
            {
                sb.AppendLine(Loc.T("caves.letThrough"));
                sb.AppendLine(Loc.T("caves.row.cave2", a1Yes));
                sb.AppendLine(Loc.T("caves.row.cave3", a1No));
            }
        }
        else
        {
            sb.AppendLine(Loc.T("caves.filterReading"));
        }

        if (pool.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("caves.pool", pool.Count));
            foreach (var zb in pool.Take(16))
            {
                int? cave = caves.FindAcceptingCave(zb);
                string mark = cave.HasValue ? $" {cave}" : "  ?";
                sb.AppendLine($"  {mark} y{zb.YPosition,3}: {zb.Hair},{zb.Eyes},{zb.Nose},{zb.Feet}");
            }
        }
        return sb.ToString();
    }

    /// <summary>One axis as a sentence fragment that fits after "wer …".
    /// Examples (German):
    ///   positive single:  "Pferdeschwanz hat"
    ///   positive multi:   "Pferdeschwanz oder Brille hat"
    ///   negative single:  "keinen Pferdeschwanz hat"
    ///   negative multi:   "weder Pferdeschwanz noch Brille hat"
    /// <paramref name="negated"/> flips the cave-side; the axis's own invert
    /// flag is folded in so the caller doesn't need to know about it.</summary>
    private static string AxisAsClause(CavesState.AxisFilter axis, bool negated)
    {
        if (axis.Conditions.Count == 0) return Loc.T("caves.clause.none");
        bool finalNegated = axis.Invert ^ negated;
        var labels = axis.Conditions
            .Select(c => ZoombiniVariants.VariantName(c.AttrType, c.Variant))
            .ToArray();
        if (finalNegated)
            return labels.Length == 1
                ? Loc.T("caves.clause.negSingle", labels[0])
                : Loc.T("caves.clause.negMulti", string.Join(Loc.T("caves.join.nor"), labels));
        return Loc.T("caves.clause.pos", string.Join(Loc.T("caves.join.or"), labels));
    }
}
