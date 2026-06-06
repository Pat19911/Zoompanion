using System.Drawing;
using System.Text;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Helper for "Hotel Dimensia" (hotel.mhk). Two-stage progression:
/// <list type="bullet">
///   <item>Stage 1: which attribute drives each axis — always available
///   from the engine's static state, no zoombini needs to be placed yet.</item>
///   <item>Stage 2 (Diff 3 only): a complete placement plan derived by
///   <see cref="HotelSolver"/> from the boarded cells + live pool. The
///   plan is one of the (typically ~50) permutations consistent with the
///   boarded cells, refined by drops the player has already made.
///   Following it guarantees all 16 zoombinis fit. While the player is
///   holding a zoombini the body shrinks down to "your held one goes here".</item>
/// </list>
/// </summary>
public sealed class HotelRenderer : IHintCyclingRenderer
{
    public PuzzleId Handles => PuzzleId.HotelDimensia;

    public const int MaxHintLevel = 2;
    public int HintLevel { get; private set; } = 0;

    public void CycleHintLevel() => HintLevel = (HintLevel + 1) % (MaxHintLevel + 1);

    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var s = HotelState.Read(mem);
        labels.TitleColor = Color.FromArgb(170, 200, 230);
        labels.Title = Loc.T("hotel.title", s.Difficulty);
        labels.Body = BuildBody(s, pool, mem);
    }

    private string BuildBody(HotelState s, IReadOnlyList<PoolMember> pool, IMemoryReader mem)
    {
        var sb = new StringBuilder();
        if (!s.IsActive)
        {
            sb.AppendLine(Loc.T("hotel.waiting"));
            return sb.ToString();
        }

        sb.AppendLine(Loc.T("hotel.hintLevel", HintLevel, MaxHintLevel));
        sb.AppendLine();

        if (HintLevel == 0)
        {
            sb.AppendLine(Loc.T("hotel.hint0"));
            return sb.ToString();
        }

        AppendAxes(sb, s);

        if (HintLevel < 2)
        {
            sb.AppendLine();
            sb.AppendLine(s.Difficulty == 3
                ? Loc.T("hotel.hint1.diff3")
                : Loc.T("hotel.hint1.other"));
            return sb.ToString();
        }

        // Stage 2: solver-driven placement plan (only meaningful at Diff 3).
        if (s.Difficulty != 3)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("hotel.noNailed.1"));
            sb.AppendLine(Loc.T("hotel.noNailed.2"));
            sb.AppendLine(Loc.T("hotel.noNailed.3"));
            return sb.ToString();
        }

        AppendDiff3Plan(sb, s, pool, HeldZoombini.Find(mem));
        return sb.ToString();
    }

    private static void AppendAxes(StringBuilder sb, HotelState s)
    {
        sb.AppendLine(Loc.T("hotel.sortedBy"));
        sb.AppendLine(Loc.T("hotel.axis.cols", AttrLabel(s.AxisX)));
        if (s.AxisCount >= 2)
            sb.AppendLine(Loc.T("hotel.axis.rows", AttrLabel(s.AxisY)));
        if (s.AxisCount >= 3)
            sb.AppendLine(Loc.T("hotel.axis.floors", AttrLabel(s.AxisZ)));
    }

    private static void AppendDiff3Plan(StringBuilder sb, HotelState s,
                                         IReadOnlyList<PoolMember> pool, PoolMember? held)
    {
        var solverPool = pool.Select(p => new HotelSolver.PoolZb(p.Hair, p.Eyes, p.Nose, p.Feet)).ToList();
        var result = HotelSolver.Solve(s, solverPool);

        if (result.Candidates.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("hotel.noSolution.1"));
            sb.AppendLine(Loc.T("hotel.noSolution.2"));
            return;
        }

        var plan = result.Candidates[0];

        // If the player is holding one, focus on that one — that's the moment
        // the recommendation actually matters.
        if (held is { } z)
        {
            byte hx = AttrOf(z, s.AxisX);
            byte hy = AttrOf(z, s.AxisY);
            int col = Array.IndexOf(plan.PermX, hx);
            int row = Array.IndexOf(plan.PermY, hy);
            sb.AppendLine();
            sb.AppendLine(Loc.T("hotel.held.lifted", z.Hair, z.Eyes, z.Nose, z.Feet));
            sb.AppendLine();
            if (col >= 0 && row >= 0)
            {
                sb.AppendLine(Loc.T("hotel.held.setIn", col + 1, row + 1));
                sb.AppendLine(Loc.T("hotel.held.coords", AttrShort(s.AxisX), hx, AttrShort(s.AxisY), hy));
            }
            else
            {
                sb.AppendLine(Loc.T("hotel.held.noFit"));
            }
            sb.AppendLine();
            sb.AppendLine(Loc.T("hotel.held.permCount", result.Candidates.Count));
            return;
        }

        // Idle (no held): full grid view as overview.
        sb.AppendLine();
        sb.AppendLine(Loc.T("hotel.plan.header", result.Candidates.Count));
        sb.AppendLine();

        sb.Append("        ");
        for (int c = 0; c < 5; c++) sb.Append($" {AttrShort(s.AxisX)}={plan.PermX[c]} ");
        sb.AppendLine();

        for (int r = 0; r < 5; r++)
        {
            sb.Append($"  {AttrShort(s.AxisY)}={plan.PermY[r]}  ");
            for (int c = 0; c < 5; c++)
            {
                bool boarded = s.Boarded.Any(b => b.Row == r && b.Column == c);
                if (boarded) { sb.Append(" ▓▓▓ "); continue; }
                int n = CountZbForCell(c, r, plan, pool, s.AxisX, s.AxisY);
                sb.Append(n > 0 ? $"  {n}  " : "  ·  ");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine(Loc.T("hotel.plan.legend1"));
        sb.AppendLine(Loc.T("hotel.plan.legend2"));
    }

    private static int CountZbForCell(int col, int row, HotelSolver.Permutation plan,
                                       IReadOnlyList<PoolMember> pool, byte axisX, byte axisY)
    {
        byte wantX = plan.PermX[col];
        byte wantY = plan.PermY[row];
        int n = 0;
        for (int i = 0; i < pool.Count; i++)
            if (AttrOf(pool[i], axisX) == wantX && AttrOf(pool[i], axisY) == wantY)
                n++;
        return n;
    }

    private static byte AttrOf(PoolMember p, byte axisAttrId) => axisAttrId switch
    {
        1 => p.Hair, 2 => p.Eyes, 3 => p.Nose, 4 => p.Feet,
        _ => (byte)0,
    };

    private static string AttrLabel(byte attrId) => attrId switch
    {
        1 => Loc.T("hotel.attr.hair"),
        2 => Loc.T("hotel.attr.eyes"),
        3 => Loc.T("hotel.attr.nose"),
        4 => Loc.T("hotel.attr.feet"),
        _ => Loc.T("hotel.attr.none"),
    };

    private static string AttrShort(byte attrId) => attrId switch
    {
        1 => "H", 2 => "E", 3 => "N", 4 => "F", _ => "?",
    };
}
