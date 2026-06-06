using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Stage-1 helper for Captain Cajun's Ferryboat. Currently shows:
/// <list type="bullet">
///   <item>Detected difficulty (1..4) read from <c>0x4A2188</c>.</item>
///   <item>Pool size + visible held zoombini.</item>
///   <item>Mini-map of the 16 seat positions read from <c>0x4A4018</c>
///   (the engine's hit-test position table — same address as Stone Rise,
///   the engine recycles it per puzzle).</item>
/// </list>
///
/// <para>**Not yet implemented:** the constraint logic (which seats are
/// neighbors, the "must share at least one attribute" rule), per-seat
/// zb-occupancy tracking, and a solver. Those need another disasm session
/// to find the adjacency table and per-seat zb-id mechanism.</para>
/// </summary>
public sealed class CaptainCajunRenderer : IHintCyclingRenderer
{
    public PuzzleId Handles => PuzzleId.CaptainCajun;

    private const int GridPanelHeight = 320;

    /// <summary>3 Stufen analog zu Hotel/Mudball:
    /// 0 = keine Hilfe (nur Titel),
    /// 1 = Lösungs-Anzahl + Sackgassen-Warnung (kein Ziel-Marker),
    /// 2 = volles Souffleuren inkl. Zielsitz + Minimap-Ring.</summary>
    public const int MaxHintLevel = 2;
    public int HintLevel { get; private set; } = 0;
    public void CycleHintLevel() => HintLevel = (HintLevel + 1) % (MaxHintLevel + 1);

    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var s = CaptainCajunState.Read(mem);
        var held = HeldZoombini.Find(mem);

        labels.TitleColor = Color.FromArgb(140, 200, 255);
        labels.Title      = s.IsActive
            ? Loc.T("captain.title.active", s.Difficulty)
            : Loc.T("captain.title.waiting");

        // Hint-Stufe 0 = aus. Solver gar nicht erst laufen lassen,
        // Minimap nicht zeichnen, kein Body außer dem Stufen-Hinweis.
        if (HintLevel == 0)
        {
            var sb0 = new StringBuilder();
            sb0.AppendLine(Loc.T("captain.hint0.level", MaxHintLevel));
            sb0.AppendLine();
            sb0.AppendLine(Loc.T("captain.hint0.1"));
            sb0.AppendLine(Loc.T("captain.hint0.2"));
            sb0.AppendLine(Loc.T("captain.hint0.3"));
            labels.Body = sb0.ToString();
            return;
        }

        // Map each seat to its placed zb via the engine's per-seat hdr1A
        // table at 0x4A3398. Seat[i].PlacedZbHeaderId == zb.HeaderId for
        // exactly the zb that sits there. No geometric fallback needed.
        var poolByHdr = new Dictionary<ushort, PoolMember>(pool.Count);
        foreach (var p in pool)
            if (p.HeaderId != 0) poolByHdr[p.HeaderId] = p;
        var placedZbs = new List<PoolMember>();
        var zbBySeat = new Dictionary<int, PoolMember>();
        foreach (var seat in s.Seats)
        {
            if (seat.PlacedZbHeaderId == 0) continue;
            if (poolByHdr.TryGetValue(seat.PlacedZbHeaderId, out var zb))
            {
                zbBySeat[seat.Index] = zb;
                placedZbs.Add(zb);
            }
        }

        // Solver pool: visible pool + held zb. Pool already contains the
        // placed zbs (they keep handle=0x00000001), so no duplication.
        var solverPool = pool.Select(p => new CaptainCajunSolver.PoolZb(p.Hair, p.Eyes, p.Nose, p.Feet)).ToList();
        int heldPoolIndex = -1;
        if (held is { } heldZb)
        {
            heldPoolIndex = solverPool.Count;
            solverPool.Add(new CaptainCajunSolver.PoolZb(heldZb.Hair, heldZb.Eyes, heldZb.Nose, heldZb.Feet));
        }
        // Fix-assign each placed zb to its seat by attribute match. Track
        // which pool indices are already claimed so two placed zbs with
        // identical attributes (rare but possible) get distinct slots.
        var fixedAssignments = new Dictionary<int, int>();
        var claimedPoolIdx = new HashSet<int>();
        foreach (var (seatIdx, zb) in zbBySeat)
        {
            for (int i = 0; i < solverPool.Count; i++)
            {
                if (claimedPoolIdx.Contains(i)) continue;
                var p = solverPool[i];
                if (p.Hair == zb.Hair && p.Eyes == zb.Eyes && p.Nose == zb.Nose && p.Feet == zb.Feet)
                {
                    fixedAssignments[seatIdx] = i;
                    claimedPoolIdx.Add(i);
                    break;
                }
            }
        }

        var seatPositions = s.Seats.Select(se => (se.X, se.Y)).ToList();
        CaptainCajunSolver.Result? result = null;
        long solveMs = 0;
        if (s.IsActive && seatPositions.Count > 0 && solverPool.Count > 0)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                result = CaptainCajunSolver.Solve(seatPositions, solverPool, fixedAssignments);
            }
            catch
            {
                // Solver should never throw, but a defective state shouldn't
                // freeze or kill the helper. Leave result null and let the
                // body fall through to the "no solver yet" message.
                result = null;
            }
            sw.Stop();
            solveMs = sw.ElapsedMilliseconds;
        }

        int? heldTargetSeat = (heldPoolIndex >= 0 && result is not null && held is { } h2)
            ? FindStableTargetForPoolIndex(result, heldPoolIndex, h2.Hair, h2.Eyes, h2.Nose, h2.Feet)
            : null;

        var sb = new StringBuilder();
        sb.AppendLine(Loc.T("captain.hintLevel", HintLevel, MaxHintLevel));
        sb.AppendLine(Loc.T("captain.pool", pool.Count, Loc.T(held is null ? "captain.held.no" : "captain.held.yes")));
        sb.AppendLine(Loc.T("captain.seats", s.Seats.Count, placedZbs.Count));
        if (result is not null)
        {
            string countText = result.HitCap ? Loc.T("captain.cap", CaptainCajunSolver.MaxSolutions)
                                             : result.SolutionCount.ToString();
            sb.AppendLine(Loc.T("captain.solutions", countText, solveMs));
        }
        sb.AppendLine();
        if (result?.SolutionCount == 0)
        {
            sb.AppendLine(Loc.T("captain.deadend.1"));
            sb.AppendLine(Loc.T("captain.deadend.2"));
            sb.AppendLine(Loc.T("captain.deadend.3"));
        }
        else if (HintLevel < 2)
        {
            // Stufe 1: Anzahl + Sackgassen-Status, aber NICHT verraten,
            // wo der gehaltene Zoombini hingehört.
            if (held is not null && heldTargetSeat is null)
                sb.AppendLine(Loc.T("captain.stage1.noFit"));
            else
                sb.AppendLine(Loc.T("captain.stage1.solvable"));
        }
        else if (held is not null && heldTargetSeat is null)
        {
            sb.AppendLine(Loc.T("captain.held.noFit.1"));
            sb.AppendLine(Loc.T("captain.held.noFit.2"));
        }
        else if (held is not null && heldTargetSeat is int targetSeat)
        {
            sb.AppendLine(Loc.T("captain.held.targetSeat", targetSeat));
        }
        else
        {
            sb.AppendLine(Loc.T("captain.idle.1"));
            sb.AppendLine(Loc.T("captain.idle.2"));
        }
        labels.Body = sb.ToString();

        if (s.IsActive && s.Seats.Count > 0)
        {
            var seatsSnapshot = s.Seats.ToList();
            var occupiedSet = new HashSet<int>(zbBySeat.Keys);
            labels.GridHeight = GridPanelHeight;
            int diffSnapshot = s.Difficulty;
            // Ziel-Ring nur bei voller Reveal-Stufe.
            int? targetSnapshot = HintLevel >= 2 ? heldTargetSeat : null;
            labels.PaintGrid = (g, bounds) => PaintLayout(g, bounds, seatsSnapshot, occupiedSet, diffSnapshot, targetSnapshot);
        }
    }

    /// <summary>Sticky target keyed by the held zb's ATTRIBUTES — not its
    /// pool index. The pool index can shift between ticks when the engine
    /// temporarily hides or re-orders zbs (held entering/leaving tray etc.),
    /// which would invalidate an index-based stickiness and cause flicker
    /// even when the user changed nothing. The attribute tuple is invariant
    /// for the same physical zoombini, so this stays stable.</summary>
    private (byte H, byte E, byte N, byte F)? _lastHeldKey;
    private int? _lastHeldTargetSeat;

    private int? FindStableTargetForPoolIndex(
        CaptainCajunSolver.Result result, int poolIdx,
        byte hairKey, byte eyesKey, byte noseKey, byte feetKey)
    {
        if (poolIdx < 0 || result.SolutionCount == 0)
        {
            _lastHeldKey = null; _lastHeldTargetSeat = null;
            return null;
        }
        var currentKey = (hairKey, eyesKey, noseKey, feetKey);
        // Reset stickiness only when the held zb itself changed — same zb
        // across re-solves keeps its target as long as it stays valid.
        if (_lastHeldKey is { } prevKey && prevKey == currentKey
            && _lastHeldTargetSeat is int last && result.CanPlaceAt(poolIdx, last))
            return last;
        int? best = result.MostFrequentSeatFor(poolIdx);
        _lastHeldKey = currentKey;
        _lastHeldTargetSeat = best;
        return best;
    }

    /// <summary>Render every seat at its engine position (scaled to fit).
    /// We don't yet know which entries in the 16-slot table are real seats
    /// vs. preallocated garbage — for now we draw all of them and let the
    /// player visually compare to the game.</summary>
    private static void PaintLayout(Graphics g, Rectangle bounds,
                                     IReadOnlyList<CaptainCajunState.Seat> seats,
                                     IReadOnlySet<int> occupiedSeats,
                                     int diff,
                                     int? targetSeat)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (seats.Count == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var s in seats)
        {
            if (s.X < minX) minX = s.X; if (s.X > maxX) maxX = s.X;
            if (s.Y < minY) minY = s.Y; if (s.Y > maxY) maxY = s.Y;
        }
        minX -= 30; minY -= 30; maxX += 30; maxY += 30;
        int srcW = Math.Max(1, maxX - minX);
        int srcH = Math.Max(1, maxY - minY);

        const int margin = 16;
        float scale = Math.Min((bounds.Width - 2 * margin) / (float)srcW,
                               (bounds.Height - 2 * margin) / (float)srcH);

        Point Project(int x, int y) => new(
            bounds.Left + margin + (int)((x - minX) * scale),
            bounds.Top  + margin + (int)((y - minY) * scale));

        const int seatR = 14;
        using var idFont = new Font("Consolas", 8f, FontStyle.Bold);
        foreach (var seat in seats)
        {
            var p = Project(seat.X, seat.Y);
            var rect = new Rectangle(p.X - seatR, p.Y - seatR, 2 * seatR, 2 * seatR);
            bool isOcc = occupiedSeats.Contains(seat.Index);
            using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillEllipse(shadow, rect.X + 2, rect.Y + 3, rect.Width, rect.Height);
            // Sitz-Hintergrundfarbe: per Attribut color-coded (Hair=orange,
            // Eyes=blau, Nose=grün), Helligkeit reduziert wenn besetzt.
            var attr = CaptainCajunLayouts.AttrFor(diff, seat.Index);
            Color body = AttrColor(attr, dimmed: isOcc);
            Color edge = AttrColor(attr, dimmed: true);
            using (var br = new SolidBrush(body)) g.FillEllipse(br, rect);
            using (var pen = new Pen(edge, 1.5f)) g.DrawEllipse(pen, rect);
            // Glyph: H/E/N für die Attribut-Klasse (klein, mittig).
            string glyph = AttrGlyph(attr);
            var sz = g.MeasureString(glyph, idFont);
            Color glyphColor = isOcc ? Color.FromArgb(255, 255, 255)
                                     : Color.FromArgb(50, 60, 80);
            using var glyphBrush = new SolidBrush(glyphColor);
            g.DrawString(glyph, idFont, glyphBrush,
                p.X - sz.Width / 2, p.Y - sz.Height / 2);

            if (targetSeat == seat.Index)
            {
                using var glow = new Pen(Color.FromArgb(255, 235, 110), 4);
                g.DrawEllipse(glow, p.X - seatR - 5, p.Y - seatR - 5,
                              2 * seatR + 10, 2 * seatR + 10);
            }
        }
    }

    private static Color AttrColor(CaptainCajunLayouts.SeatAttr attr, bool dimmed) => attr switch
    {
        CaptainCajunLayouts.SeatAttr.Hair => dimmed ? Color.FromArgb(150, 110, 60)  : Color.FromArgb(255, 200, 130),
        CaptainCajunLayouts.SeatAttr.Eyes => dimmed ? Color.FromArgb(60, 110, 150)  : Color.FromArgb(120, 200, 255),
        CaptainCajunLayouts.SeatAttr.Nose => dimmed ? Color.FromArgb(90, 140, 70)   : Color.FromArgb(180, 255, 130),
        _                                  => dimmed ? Color.FromArgb(100, 100, 110) : Color.FromArgb(180, 180, 195),
    };

    private static string AttrGlyph(CaptainCajunLayouts.SeatAttr attr) => attr switch
    {
        CaptainCajunLayouts.SeatAttr.Hair => "H",
        CaptainCajunLayouts.SeatAttr.Eyes => "E",
        CaptainCajunLayouts.SeatAttr.Nose => "N",
        _                                  => "?",
    };
}
