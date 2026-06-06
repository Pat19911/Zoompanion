namespace ZoombiniHelper;

/// <summary>
/// Constraint-satisfaction solver for Stone Rise. Each puzzle is a graph
/// of pair-slots connected by attribute-typed connectors; a valid
/// solution assigns one pool zoombini to every pair-slot such that for
/// every connector, the two zoombinis in its endpoints share the
/// connector's required attribute.
///
/// <para>The solver does plain backtracking with a most-constrained-variable
/// heuristic (pick the unfilled slot with the fewest still-valid pool
/// candidates). On the verified Diff-4 fixture (16 slots, 21 connectors,
/// some slots in 6 connectors at once) it returns the full solution
/// count in milliseconds. On loosely-constrained grids the count can
/// blow up — we cap enumeration at <see cref="MaxSolutions"/> and
/// expose <see cref="Result.HitCap"/> so the renderer can say
/// "hundreds, showing first 5".</para>
/// </summary>
public sealed class StoneRiseSolver
{
    public readonly record struct PoolZb(byte Hair, byte Eyes, byte Nose, byte Feet);

    /// <summary>One full assignment slot-tile-index → pool-zb-index.</summary>
    public sealed class Solution
    {
        public IReadOnlyDictionary<int, int> SlotTileToZbIndex { get; init; }
            = new Dictionary<int, int>();
    }

    public sealed class Result
    {
        public int SolutionCount { get; init; }
        public bool HitCap { get; init; }
        public IReadOnlyList<Solution> Solutions { get; init; } = Array.Empty<Solution>();
    }

    /// <summary>How many distinct solutions to enumerate before stopping the
    /// counter. Counting stops at this value; the renderer can report
    /// "≥ MaxSolutions".</summary>
    public const int MaxSolutions = 10_000;

    /// <summary>How many full solutions to keep in <see cref="Result.Solutions"/>.</summary>
    public const int SolutionsToKeep = 5;

    public static Result Solve(StoneRiseState state, IReadOnlyList<PoolZb> pool,
                                IReadOnlyDictionary<int, int>? fixedAssignments = null)
    {
        if (!state.IsActive || pool.Count == 0)
            return new Result();

        // Index pair slots by their position in the slot list (0..N-1).
        var slotTiles = state.PairSlots.Select(s => s.TileIndex).ToArray();
        var slotIdxByTile = new Dictionary<int, int>(slotTiles.Length);
        for (int i = 0; i < slotTiles.Length; i++) slotIdxByTile[slotTiles[i]] = i;

        // Build neighbor table: per slot, list of (other_slot, attr_idx 0..3).
        int n = slotTiles.Length;
        var neighbors = new List<(int otherSlot, int attrIdx)>[n];
        for (int i = 0; i < n; i++) neighbors[i] = new();
        foreach (var c in state.Connectors)
        {
            if (c.IsFilled) continue;  // already-completed connectors are de-facto satisfied
            if (c.AttributeId == 0) continue;  // structural bridge, no rule to enforce
            if (!slotIdxByTile.TryGetValue(c.PairTileA, out int sa)) continue;
            if (!slotIdxByTile.TryGetValue(c.PairTileB, out int sb)) continue;
            int attrIdx = c.AttributeId - 1; // 0..3
            neighbors[sa].Add((sb, attrIdx));
            neighbors[sb].Add((sa, attrIdx));
        }

        var assignment = new int[n];
        Array.Fill(assignment, -1);
        var used = new bool[pool.Count];

        // Lock in any caller-supplied fixed assignments — these are slots
        // where the player has already placed a known zoombini. The
        // solver works around them, treating them as immovable constraints.
        if (fixedAssignments is not null)
        {
            foreach (var (tile, poolIdx) in fixedAssignments)
            {
                if (!slotIdxByTile.TryGetValue(tile, out int slotIdx)) continue;
                if (poolIdx < 0 || poolIdx >= pool.Count) continue;
                if (used[poolIdx]) return new Result();  // double-assignment is impossible
                assignment[slotIdx] = poolIdx;
                used[poolIdx] = true;
            }
            // Verify the locked assignments don't already violate any constraint —
            // if they do, the puzzle is dead and we short-circuit.
            for (int s = 0; s < n; s++)
            {
                if (assignment[s] == -1) continue;
                if (!CompatibleHelper(s, assignment[s], assignment, used, neighbors, pool))
                    return new Result();
            }
        }

        var solutions = new List<Solution>();
        int count = 0;
        bool hitCap = false;

        bool Compatible(int slot, int zbIdx)
        {
            byte[] zb = ZbBytes(pool[zbIdx]);
            foreach (var (other, attrIdx) in neighbors[slot])
            {
                if (assignment[other] == -1) continue;
                byte[] otherZb = ZbBytes(pool[assignment[other]]);
                if (zb[attrIdx] != otherZb[attrIdx]) return false;
            }
            return true;
        }

        int SelectVariable()
        {
            int best = -1, bestCount = int.MaxValue;
            for (int s = 0; s < n; s++)
            {
                if (assignment[s] != -1) continue;
                int c = 0;
                for (int z = 0; z < pool.Count; z++)
                {
                    if (used[z]) continue;
                    if (Compatible(s, z)) c++;
                }
                if (c < bestCount) { best = s; bestCount = c; }
                if (c == 0) return s; // dead end shortcut — caller will see no candidate fits
            }
            return best;
        }

        void Recurse()
        {
            if (hitCap) return;
            int unfilled = 0;
            for (int s = 0; s < n; s++) if (assignment[s] == -1) unfilled++;
            if (unfilled == 0)
            {
                count++;
                if (solutions.Count < SolutionsToKeep)
                    solutions.Add(SnapshotAssignment(slotTiles, assignment));
                if (count >= MaxSolutions) hitCap = true;
                return;
            }
            int slot = SelectVariable();
            if (slot == -1) return;
            for (int z = 0; z < pool.Count; z++)
            {
                if (hitCap) return;
                if (used[z]) continue;
                if (!Compatible(slot, z)) continue;
                assignment[slot] = z; used[z] = true;
                Recurse();
                assignment[slot] = -1; used[z] = false;
            }
        }

        Recurse();

        return new Result
        {
            SolutionCount = count,
            HitCap = hitCap,
            Solutions = solutions,
        };
    }

    private static Solution SnapshotAssignment(int[] slotTiles, int[] assignment)
    {
        var dict = new Dictionary<int, int>(slotTiles.Length);
        for (int i = 0; i < slotTiles.Length; i++)
            dict[slotTiles[i]] = assignment[i];
        return new Solution { SlotTileToZbIndex = dict };
    }

    private static byte[] ZbBytes(PoolZb z) => new[] { z.Hair, z.Eyes, z.Nose, z.Feet };

    /// <summary>Same as the inner Compatible closure but as a static helper —
    /// used during fixed-assignment validation before the recursion starts.</summary>
    private static bool CompatibleHelper(int slot, int zbIdx, int[] assignment, bool[] used,
                                          List<(int otherSlot, int attrIdx)>[] neighbors,
                                          IReadOnlyList<PoolZb> pool)
    {
        byte[] zb = ZbBytes(pool[zbIdx]);
        foreach (var (other, attrIdx) in neighbors[slot])
        {
            if (other == slot) continue;
            if (assignment[other] == -1) continue;
            byte[] otherZb = ZbBytes(pool[assignment[other]]);
            if (zb[attrIdx] != otherZb[attrIdx]) return false;
        }
        return true;
    }
}
