using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Helper for "Stone Rise" (slides.mhk). Combines text + a custom-painted
/// grid:
/// <list type="bullet">
///   <item>Body text shows solver stats (number of valid full assignments,
///   solve time).</item>
///   <item>Grid panel paints all active tiles at their engine-side screen
///   coordinates: pair-slots as circles, connectors as colored lines.</item>
///   <item>When a zoombini is picked up, the slot it belongs to in plan #1
///   pulses green so the player knows where to drop it.</item>
/// </list>
/// </summary>
public sealed class StoneRiseRenderer : IPuzzleRenderer
{
    public PuzzleId Handles => PuzzleId.StoneRise;

    private const int GridPanelHeight = 540;

    /// <summary>Persistent across ticks — tracks which zoombini ended up in
    /// which slot so the solver can keep the user honest after every drop.</summary>
    private readonly StoneRisePlacementTracker _tracker = new();

    /// <summary>Cached tile_idx → engine-coord mapping. Built once per puzzle,
    /// at the moment we see all slots empty AND the engine's hit-test table
    /// has the same number of entries as our PairSlots (= clean initial state).
    /// Necessary because the engine COMPACTS the hit-test table the moment a
    /// slot gets filled, so an index-based mapping is only safe before any
    /// placement. Caching the initial layout keeps the minimap stable for
    /// the whole puzzle.</summary>
    private readonly Dictionary<int, (int X, int Y)> _slotEnginePositions = new();
    private string _layoutKey = "";

    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var s = StoneRiseState.Read(mem);
        var held = HeldZoombini.Find(mem);
        labels.TitleColor = Color.FromArgb(180, 180, 220);
        labels.Title = Loc.T("stonerise.title", s.Difficulty);

        // Update placement tracking — if a slot just transitioned empty→
        // filled, the previously-held zb is now placed there.
        var trackerHeld = held is { } h0
            ? new StoneRisePlacementTracker.ZbAttrs(h0.Hair, h0.Eyes, h0.Nose, h0.Feet)
            : (StoneRisePlacementTracker.ZbAttrs?)null;
        _tracker.OnTick(s, trackerHeld);

        // Mid-puzzle re-attach: if the field has filled slots we never saw
        // get filled (no live transition observed), look them up by zb-id
        // matching against every record on the heap.
        int filledCount = s.PairSlots.Count(p => p.IsFilled);
        if (_tracker.Placed.Count < filledCount)
            foreach (var (tile, attrs) in StoneRisePlacedResolver.Resolve(s, mem))
                _tracker.Adopt(tile, attrs);

        UpdateSlotPositionCache(s);

        // The solver enumerates all complete N-to-N assignments — every zb
        // placed into exactly one slot. So solverPool MUST have exactly the
        // same count as PairSlots; otherwise the solver finds "solutions"
        // that leave some zbs unassigned, which aren't real solutions.
        //
        // Construction:
        //   - Each visible pool zb is one entry (placed zbs stay in the pool
        //     because they keep handle=0x00000001 — no duplication).
        //   - The held zb is added once (it has a different handle so the
        //     pool scanner skipped it).
        //   - Placed slots are pinned by finding the matching pool entry's
        //     hdr1A (= tile.w1 of the slot) and using THAT index in
        //     fixedAssignments. No new entries appended.
        var solverPool = pool.Select(p => new StoneRiseSolver.PoolZb(p.Hair, p.Eyes, p.Nose, p.Feet)).ToList();
        int heldPoolIndex = -1;
        if (held is { } heldZb)
        {
            heldPoolIndex = solverPool.Count;
            solverPool.Add(new StoneRiseSolver.PoolZb(heldZb.Hair, heldZb.Eyes, heldZb.Nose, heldZb.Feet));
        }
        var poolIdxByHeaderId = new Dictionary<ushort, int>(pool.Count);
        for (int i = 0; i < pool.Count; i++)
            if (pool[i].HeaderId != 0)
                poolIdxByHeaderId[pool[i].HeaderId] = i;

        var fixedAssignments = new Dictionary<int, int>();
        foreach (var slot in s.PairSlots)
        {
            if (!slot.IsFilled || slot.PlacedZbId == 0) continue;
            if (poolIdxByHeaderId.TryGetValue(slot.PlacedZbId, out int poolIdx))
                fixedAssignments[slot.TileIndex] = poolIdx;
        }

        StoneRiseSolver.Result? result = null;
        long solveMs = 0;
        if (s.IsActive && solverPool.Count > 0)
        {
            var sw = Stopwatch.StartNew();
            result = StoneRiseSolver.Solve(s, solverPool, fixedAssignments);
            sw.Stop();
            solveMs = sw.ElapsedMilliseconds;
        }

        int? heldTargetTile = (heldPoolIndex >= 0 && result is not null)
            ? FindTargetForPoolIndex(result, heldPoolIndex) : null;
        labels.Body = BuildBody(s, pool.Count, result, solveMs, _tracker.Placed.Count,
                                 isHolding: held is not null,
                                 heldHasTarget: heldTargetTile is not null);

        if (s.IsActive && s.PairSlots.Count > 0)
        {
            // Snapshot the cache so the paint callback (runs later on the UI
            // thread) sees a stable view, not a dictionary that might mutate
            // between ticks.
            var posSnapshot = new Dictionary<int, (int X, int Y)>(_slotEnginePositions);
            labels.GridHeight = GridPanelHeight;
            labels.PaintGrid = (g, bounds) => PaintLiveMinimap(g, bounds, s, posSnapshot, heldTargetTile);
        }
    }

    /// <summary>Build (or invalidate) the cached engine-coord mapping. The
    /// engine's per-active-slot lookup table at 0x49C7B0 gives the exact
    /// tile_idx for each engine position entry — we use that directly so
    /// the layout is correct regardless of the engine's chosen ordering
    /// (Diff 3 groups by visual column, Diff 4 happens to match tile-index
    /// ascending). The cache survives placements (which compact the engine
    /// table) and resets on puzzle change.</summary>
    private void UpdateSlotPositionCache(StoneRiseState s)
    {
        string key = string.Join(",", s.PairSlots.Select(p => p.TileIndex).OrderBy(i => i));
        if (key != _layoutKey)
        {
            _layoutKey = key;
            _slotEnginePositions.Clear();
        }
        if (_slotEnginePositions.Count == s.PairSlots.Count) return;
        if (s.ActiveSlotEnginePositions.Count != s.PairSlots.Count) return;
        if (s.ActiveSlotToTileIndex.Count != s.PairSlots.Count) return;

        for (int i = 0; i < s.ActiveSlotEnginePositions.Count; i++)
        {
            int tileIdx = s.ActiveSlotToTileIndex[i];
            _slotEnginePositions[tileIdx] = s.ActiveSlotEnginePositions[i];
        }
    }

    /// <summary>Render a minimap using the cached engine-coord positions
    /// captured at puzzle start (when ActiveSlotEnginePositions still
    /// matched PairSlots one-to-one). The cache is stable across placements,
    /// so the layout stays put when slots get filled.</summary>
    private static void PaintLiveMinimap(Graphics g, Rectangle bounds, StoneRiseState s,
                                          IReadOnlyDictionary<int, (int X, int Y)> enginePositions,
                                          int? targetTile)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (enginePositions.Count == 0)
        {
            DrawTextCentered(g, bounds, Loc.T("stonerise.minimapWaiting"));
            return;
        }

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var (x, y) in enginePositions.Values)
        { if (x < minX) minX = x; if (x > maxX) maxX = x;
          if (y < minY) minY = y; if (y > maxY) maxY = y; }
        minX -= 30; minY -= 30; maxX += 30; maxY += 30;
        int srcW = Math.Max(1, maxX - minX);
        int srcH = Math.Max(1, maxY - minY);

        const int margin = 16;
        float scale = Math.Min((bounds.Width - 2 * margin) / (float)srcW,
                               (bounds.Height - 2 * margin) / (float)srcH);

        Point Project(int x, int y) => new(
            bounds.Left + margin + (int)((x - minX) * scale),
            bounds.Top  + margin + (int)((y - minY) * scale));

        var slotPosByTile = new Dictionary<int, Point>(enginePositions.Count);
        foreach (var (tile, (x, y)) in enginePositions)
            slotPosByTile[tile] = Project(x, y);
        // Erfüllte Connectors so weit zurücknehmen, dass sie das Bild nicht
        // vollmüllen — sie sind „done", die Aufmerksamkeit gehört den noch
        // offenen Verbindungen. Glyph + Plate komplett weglassen wenn IsFilled.
        // Attribut-lose Connectors (AttributeId=0) sind reine strukturelle
        // Brücken (z.B. Spalten-Querverbindungen) — auch ohne Glyph, nur als
        // dezente neutrale Linie damit die Topologie sichtbar bleibt.
        using var labelFont = new Font("Segoe UI", 9, FontStyle.Bold);
        foreach (var c in s.Connectors)
        {
            if (!slotPosByTile.TryGetValue(c.PairTileA, out var pa)) continue;
            if (!slotPosByTile.TryGetValue(c.PairTileB, out var pb)) continue;
            if (c.IsFilled)
            {
                using var donePen = new Pen(Color.FromArgb(45, 90, 100, 110), 1.5f);
                g.DrawLine(donePen, pa, pb);
                continue;
            }
            if (c.AttributeId == 0)
            {
                using var bridgePen = new Pen(Color.FromArgb(110, 140, 150, 165), 2f);
                g.DrawLine(bridgePen, pa, pb);
                continue;
            }
            var col = AttrColor(c.AttributeId);
            using (var pen = new Pen(Color.FromArgb(200, col), 2.5f))
                g.DrawLine(pen, pa, pb);
            int mx = (pa.X + pb.X) / 2, my = (pa.Y + pb.Y) / 2;
            string glyph = AttrChar(c.AttributeId).ToString();
            var sz = g.MeasureString(glyph, labelFont);
            using var plate = new SolidBrush(Color.FromArgb(220, 18, 22, 36));
            g.FillRectangle(plate, mx - sz.Width / 2 - 2, my - sz.Height / 2,
                            sz.Width + 4, sz.Height);
            using var textBrush = new SolidBrush(col);
            g.DrawString(glyph, labelFont, textBrush, mx - sz.Width / 2, my - sz.Height / 2);
        }

        // Slot stones at their cached engine positions.
        const int stoneR = 14;
        var filledByTile = s.PairSlots.ToDictionary(p => p.TileIndex, p => p.IsFilled);
        foreach (var (tile, p) in slotPosByTile)
        {
            bool isFilled = filledByTile.TryGetValue(tile, out var f) && f;
            DrawStone(g, p.X, p.Y, stoneR, isFilled);
        }

        // Target highlight (yellow ring) at the solver-picked tile.
        if (targetTile is { } tt && slotPosByTile.TryGetValue(tt, out var targetP))
        {
            using var glow = new Pen(Color.FromArgb(255, 235, 110), 4);
            g.DrawEllipse(glow, targetP.X - stoneR - 5, targetP.Y - stoneR - 5,
                          2 * stoneR + 10, 2 * stoneR + 10);
        }

    }

    private static void DrawTextCentered(Graphics g, Rectangle bounds, string text)
    {
        using var f = new Font("Segoe UI", 10);
        using var br = new SolidBrush(Color.FromArgb(180, 180, 200));
        var sz = g.MeasureString(text, f);
        g.DrawString(text, f, br,
            bounds.Left + (bounds.Width - sz.Width) / 2,
            bounds.Top + (bounds.Height - sz.Height) / 2);
    }

    private static void DrawStone(Graphics g, int cx, int cy, int r, bool isFilled)
    {
        var rect = new Rectangle(cx - r, cy - r, 2 * r, 2 * r);
        using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            g.FillEllipse(shadow, rect.X + 2, rect.Y + 3, rect.Width, rect.Height);
        // Belegte Steine deutlich gedämpfter: dunkles Grau-Grün, kein 3D-
        // Highlight, kein heller Body. Offene Steine bleiben hell, damit das
        // Auge sofort sieht wo noch Arbeit zu erledigen ist.
        Color body = isFilled ? Color.FromArgb(55, 75, 60) : Color.FromArgb(150, 150, 165);
        Color edge = isFilled ? Color.FromArgb(35, 50, 40) : Color.FromArgb(100, 100, 115);
        using (var br = new SolidBrush(body)) g.FillEllipse(br, rect);
        using (var pen = new Pen(edge, 1.8f)) g.DrawEllipse(pen, rect);
        if (!isFilled)
        {
            using var hi = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            g.FillEllipse(hi, cx - r + 3, cy - r + 3, r, r / 2);
        }
    }

    private static Color AttrColor(byte attrId) => attrId switch
    {
        1 => Color.FromArgb(255, 200, 130),  // Hair = warm orange
        2 => Color.FromArgb(120, 200, 255),  // Eyes = light blue
        3 => Color.FromArgb(180, 255, 130),  // Nose = green
        4 => Color.FromArgb(220, 130, 255),  // Feet = purple
        _ => Color.Gray,
    };

    private static char AttrChar(byte attrId) => attrId switch
    { 1 => 'H', 2 => 'E', 3 => 'N', 4 => 'F', _ => '?' };

    private static string BuildBody(StoneRiseState s, int poolCount,
                                     StoneRiseSolver.Result? result, long ms,
                                     int trackedPlacements,
                                     bool isHolding, bool heldHasTarget)
    {
        var sb = new StringBuilder();
        if (!s.IsActive)
        {
            sb.AppendLine(Loc.T("stonerise.waiting"));
            return sb.ToString();
        }
        int openConn = s.Connectors.Count(c => !c.IsFilled);
        sb.AppendLine(Loc.T("stonerise.stats.slots", s.PairSlots.Count, s.Connectors.Count, openConn));
        sb.AppendLine(Loc.T("stonerise.stats.pool", poolCount, trackedPlacements));

        if (result is null)
        {
            sb.AppendLine(Loc.T("stonerise.solverWaiting"));
            return sb.ToString();
        }
        string countText = result.HitCap ? Loc.T("stonerise.cap", StoneRiseSolver.MaxSolutions)
                                         : result.SolutionCount.ToString();
        sb.AppendLine(Loc.T("stonerise.solutions", countText, ms));
        sb.AppendLine();
        if (result.SolutionCount == 0)
        {
            sb.AppendLine(Loc.T("stonerise.deadend.1"));
            sb.AppendLine(Loc.T("stonerise.deadend.2"));
        }
        else if (isHolding && !heldHasTarget)
        {
            sb.AppendLine(Loc.T("stonerise.heldNoFit.1"));
            sb.AppendLine(Loc.T("stonerise.heldNoFit.2"));
            sb.AppendLine(Loc.T("stonerise.heldNoFit.3"));
        }
        else if (isHolding)
        {
            sb.AppendLine(Loc.T("stonerise.heldMarked"));
        }
        else
        {
            sb.AppendLine(Loc.T("stonerise.idle.1"));
            sb.AppendLine(Loc.T("stonerise.idle.2"));
        }
        return sb.ToString();
    }

    /// <summary>Look up which tile gets the zoombini at the given solver-pool
    /// index. Searches all stored solutions, not just plan #1: with many open
    /// slots and a surplus of candidate zbs, the held zb may not appear in
    /// the first plan even though it has a valid placement in a later one.
    /// Returns the tile from the first plan that places this zb, or null if
    /// no stored plan uses it (= the held zb genuinely doesn't fit anywhere
    /// in the remaining solution space).</summary>
    private static int? FindTargetForPoolIndex(StoneRiseSolver.Result result, int poolIdx)
    {
        if (poolIdx < 0) return null;
        foreach (var plan in result.Solutions)
            foreach (var (tile, idx) in plan.SlotTileToZbIndex)
                if (idx == poolIdx) return tile;
        return null;
    }

}
