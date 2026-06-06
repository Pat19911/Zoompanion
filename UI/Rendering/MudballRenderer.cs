using System.Drawing;
using System.Text;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Helper for "Mudball Wall" (net.mhk). Hints are progressive on the
/// WALL-DIMENSION axis, not the button axis: each F1 reveals one more
/// piece of wall coordinate (Section → Row → Column) for ALL active
/// targets, plus the corresponding button to press.
/// </summary>
public sealed class MudballRenderer : IHintCyclingRenderer
{
    public PuzzleId Handles => PuzzleId.MudballWall;

    public const int MaxHintLevel = 4;
    public int HintLevel { get; private set; } = 0;

    public void CycleHintLevel() => HintLevel = (HintLevel + 1) % (MaxHintLevel + 1);

    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var s = MudballState.Read(mem);
        labels.TitleColor = Color.FromArgb(200, 170, 130);
        labels.Title = Loc.T("mudball.title", s.Difficulty, s.ActiveTargets.Count);
        labels.Body = BuildBody(s);
    }

    private string BuildBody(MudballState s)
    {
        var sb = new StringBuilder();
        if (!s.IsActive)
        {
            sb.AppendLine(Loc.T("mudball.waiting"));
            return sb.ToString();
        }
        if (s.ActiveTargets.Count == 0)
        {
            sb.AppendLine(Loc.T("mudball.allDone"));
            return sb.ToString();
        }

        sb.AppendLine(Loc.T("mudball.hintLevel", HintLevel, MaxHintLevel));
        sb.AppendLine();

        bool is3D = s.Difficulty >= 3;

        // Stage 0: pure prompt, no info (everything else is visible on the wall)
        if (HintLevel == 0)
        {
            sb.AppendLine(Loc.T("mudball.hint0"));
            return sb.ToString();
        }

        // Stage 1+: property → wall-dimension mapping
        sb.AppendLine(Loc.T("mudball.whatDetermines"));
        if (s.PropertyForSection is { } sp)
            sb.AppendLine(Loc.T("mudball.axis.section", Label(sp)));
        sb.AppendLine(Loc.T("mudball.axis.row", Label(s.PropertyForRow)));
        sb.AppendLine(Loc.T("mudball.axis.column", Label(s.PropertyForColumn)));
        if (s.Difficulty == 4 && s.RotationSteps > 0)
            sb.AppendLine(Loc.T("mudball.rotation", s.RotationSteps));
        sb.AppendLine();

        if (HintLevel == 1)
        {
            sb.AppendLine(Loc.T("mudball.hint1"));
            return sb.ToString();
        }

        // Stage 2+: per-target list, growing by one wall dimension per stage
        sb.AppendLine(Loc.T("mudball.targets"));
        foreach (var t in s.ActiveTargets)
        {
            sb.Append(Loc.T("mudball.target.dots", t.Dots));
            int dims = HintLevel - 1;  // stage 2 → 1 dim, stage 3 → 2, stage 4 → 3
            if (is3D)
            {
                if (dims >= 1) sb.Append(Loc.T("mudball.seg.section", t.Section + 1));
                if (dims >= 2) sb.Append(Loc.T("mudball.seg.rowNext", t.Row + 1));
                if (dims >= 3) sb.Append(Loc.T("mudball.seg.colNext", t.Column + 1));
            }
            else
            {
                if (dims >= 1) sb.Append(Loc.T("mudball.seg.rowFirst", t.Row + 1));
                if (dims >= 2) sb.Append(Loc.T("mudball.seg.colNext", t.Column + 1));
            }
            // Stage 4: full buttons too
            if (HintLevel == MaxHintLevel)
            {
                sb.Append(is3D
                    ? Loc.T("mudball.btn3d", t.Axis1 + 1, t.Axis2 + 1, t.Axis3 + 1)
                    : Loc.T("mudball.btn2d", t.Axis2 + 1, t.Axis3 + 1));
            }
            sb.AppendLine();
        }

        if (HintLevel < MaxHintLevel)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("mudball.next"));
        }
        return sb.ToString();
    }

    private static string Label(MudballProperty p) => p switch
    {
        MudballProperty.MudColour   => Loc.T("mudball.prop.mud"),
        MudballProperty.Shape       => Loc.T("mudball.prop.shape"),
        MudballProperty.StampColour => Loc.T("mudball.prop.stamp"),
        _ => p.ToString(),
    };
}
