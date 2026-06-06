namespace ZoombiniHelper;

/// <summary>
/// Constraint-satisfaction solver for Captain Cajun's Ferryboat. Each
/// puzzle is a graph of seats connected by geometric adjacency; a valid
/// solution assigns one pool zoombini to every seat such that for every
/// pair of adjacent seats, the two zoombinis there share at least one
/// of their four attributes (Hair / Eyes / Nose / Feet).
///
/// <para>Rule confirmed by the user: it's the classic manual rule
/// (min. 1 attribute shared with every neighbor), NOT the per-seat
/// attribute hypothesis. The SCRB per-seat attribute is just a sprite
/// variant for display, not a constraint.</para>
///
/// <para>Adjacency: derived from the seat positions (engine coords) at
/// runtime — two seats are neighbors iff their euclidean distance is
/// below <see cref="NeighborDistance"/>. For 4×4 grid layouts that gives
/// the cardinal up/down/left/right neighbors. For irregular layouts
/// (Diff 1 with row-transitions) it picks up the natural connections.</para>
/// </summary>
public sealed class CaptainCajunSolver
{
    public readonly record struct PoolZb(byte Hair, byte Eyes, byte Nose, byte Feet);

    public sealed class Solution
    {
        public IReadOnlyDictionary<int, int> SeatToZbIndex { get; init; }
            = new Dictionary<int, int>();
    }

    public sealed class Result
    {
        public int SolutionCount { get; init; }
        public bool HitCap { get; init; }
        public IReadOnlyList<Solution> Solutions { get; init; } = Array.Empty<Solution>();

        /// <summary>Frequency matrix: how many solutions assign zb-index <c>z</c>
        /// to seat <c>s</c>. Indexed as <c>[s, z]</c>. Filled across ALL
        /// enumerated solutions (not just the 5 stored ones), so it gives
        /// stable per-(zb,seat) statistics even when the solver has thousands
        /// of valid plans. Empty if <see cref="SolutionCount"/> is zero.</summary>
        public int[,] Frequencies { get; init; } = new int[0, 0];

        /// <summary>For a held zb at the given pool index, return the seat
        /// where it appears most often in valid solutions. Stable across
        /// re-solves because <see cref="Frequencies"/> is exact, not sampled.
        /// Tie-break by lowest seat index for determinism.</summary>
        public int? MostFrequentSeatFor(int poolIdx)
        {
            int seats = Frequencies.GetLength(0);
            int zbs = Frequencies.GetLength(1);
            if (poolIdx < 0 || poolIdx >= zbs) return null;
            int bestSeat = -1, bestCount = 0;
            for (int s = 0; s < seats; s++)
            {
                int c = Frequencies[s, poolIdx];
                if (c > bestCount) { bestCount = c; bestSeat = s; }
            }
            return bestSeat >= 0 ? bestSeat : null;
        }

        /// <summary>True if poolIdx ever appears at the given seat in any
        /// valid solution. Used by the renderer to keep a sticky target
        /// stable even when it isn't the most-frequent pick.</summary>
        public bool CanPlaceAt(int poolIdx, int seat)
        {
            int seats = Frequencies.GetLength(0);
            int zbs = Frequencies.GetLength(1);
            if (poolIdx < 0 || poolIdx >= zbs) return false;
            if (seat < 0 || seat >= seats) return false;
            return Frequencies[seat, poolIdx] > 0;
        }
    }

    public const int MaxSolutions = 10_000;
    public const int SolutionsToKeep = 5;

    /// <summary>Wall-clock deadline for one solve call. Prevents the UI
    /// thread from freezing when the user creates a state where proving
    /// "no solutions" requires exhausting the full ~16-deep search tree.
    /// On timeout the solver returns whatever it found so far with
    /// HitCap=true.</summary>
    public static readonly TimeSpan SolveTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>Engine-coord distance threshold for "these two seats are
    /// neighbors". Set to 60 — cardinal-grid steps in observed layouts are
    /// ~45 px (Diff 4) and ~47 px (Diff 3); the threshold accepts those
    /// while keeping diagonal pairs (~63 px) out.</summary>
    public const double NeighborDistance = 60.0;

    public static Result Solve(
        IReadOnlyList<(int X, int Y)> seatPositions,
        IReadOnlyList<PoolZb> pool,
        IReadOnlyDictionary<int, int>? fixedAssignments = null)
    {
        if (seatPositions.Count == 0 || pool.Count == 0) return new Result();
        if (pool.Count > 64)
            throw new NotSupportedException("Forward-checking domain bitset uses ulong (max 64 zbs).");

        int n = seatPositions.Count;
        var neighbors = new List<int>[n];
        for (int i = 0; i < n; i++) neighbors[i] = new();
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                double dx = seatPositions[i].X - seatPositions[j].X;
                double dy = seatPositions[i].Y - seatPositions[j].Y;
                if (Math.Sqrt(dx * dx + dy * dy) <= NeighborDistance)
                {
                    neighbors[i].Add(j);
                    neighbors[j].Add(i);
                }
            }

        // Pre-compute pairwise compatibility: bit z' set in compat[z] iff
        // zbs z and z' share at least one attribute. Uses ulong to support
        // up to 64 zbs (always 16 in practice, but cheap to overshoot).
        var compat = new ulong[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            for (int j = 0; j < pool.Count; j++)
            {
                if (i == j) continue;
                var a = pool[i]; var b = pool[j];
                if (a.Hair == b.Hair || a.Eyes == b.Eyes
                    || a.Nose == b.Nose || a.Feet == b.Feet)
                    compat[i] |= 1UL << j;
            }
        }

        var assignment = new int[n];
        Array.Fill(assignment, -1);
        ulong used = 0;

        // Per-slot domain: bit z set = "zb z is still a candidate for this slot".
        // Initially all zbs are candidates for all slots.
        var domain = new ulong[n];
        ulong allZbs = pool.Count == 64 ? ulong.MaxValue : (1UL << pool.Count) - 1;
        for (int s = 0; s < n; s++) domain[s] = allZbs;

        // Apply fixed assignments + propagate forward-check from each.
        if (fixedAssignments is not null)
            foreach (var (seat, poolIdx) in fixedAssignments)
            {
                if (seat < 0 || seat >= n) continue;
                if (poolIdx < 0 || poolIdx >= pool.Count) continue;
                ulong bit = 1UL << poolIdx;
                if ((used & bit) != 0) return new Result();
                assignment[seat] = poolIdx;
                used |= bit;
                // Forward-check from this fixed assignment.
                ulong propMask = compat[poolIdx];
                foreach (var nb in neighbors[seat])
                {
                    if (assignment[nb] != -1)
                    {
                        // Pinned-vs-pinned: must already be compatible.
                        if ((compat[poolIdx] & (1UL << assignment[nb])) == 0)
                            return new Result();
                        continue;
                    }
                    domain[nb] &= propMask;
                    if (domain[nb] == 0) return new Result();
                }
                // Remove this zb from every other slot's domain.
                for (int s = 0; s < n; s++)
                    if (s != seat) domain[s] &= ~bit;
            }

        var solutions = new List<Solution>();
        var frequencies = new int[n, pool.Count];
        int count = 0;
        bool hitCap = false;
        var deadline = DateTime.UtcNow + SolveTimeout;
        bool timedOut = false;

        // Most-Remaining-Values heuristic: pick the unassigned slot whose
        // domain has fewest remaining candidates. With FC this is just a
        // popcount over domain bits, no per-zb iteration needed.
        int SelectVariable()
        {
            int best = -1, bestCount = int.MaxValue;
            for (int s = 0; s < n; s++)
            {
                if (assignment[s] != -1) continue;
                int c = System.Numerics.BitOperations.PopCount(domain[s]);
                if (c < bestCount) { best = s; bestCount = c; }
                if (c == 0) return s; // dead-end, no point looking further
                if (c == 1) return s; // singleton — best possible, take it
            }
            return best;
        }

        void Recurse()
        {
            if (hitCap || timedOut) return;
            if ((count & 0xFF) == 0 && DateTime.UtcNow > deadline)
            {
                timedOut = true; hitCap = true; return;
            }
            int unfilled = 0;
            for (int s = 0; s < n; s++) if (assignment[s] == -1) unfilled++;
            if (unfilled == 0)
            {
                count++;
                if (solutions.Count < SolutionsToKeep)
                    solutions.Add(Snapshot(assignment));
                for (int s = 0; s < n; s++)
                    if (assignment[s] >= 0) frequencies[s, assignment[s]]++;
                if (count >= MaxSolutions) hitCap = true;
                return;
            }
            int slot = SelectVariable();
            if (slot == -1) return;
            ulong slotDomain = domain[slot];
            // Iterate over set bits in slotDomain — these are the only zbs
            // worth trying. Any zb NOT in slotDomain has already been
            // pruned by forward-checking.
            while (slotDomain != 0)
            {
                if (hitCap || timedOut) return;
                int z = System.Numerics.BitOperations.TrailingZeroCount(slotDomain);
                ulong zBit = 1UL << z;
                slotDomain &= ~zBit;
                if ((used & zBit) != 0) continue;

                // Forward-check: tentatively place z at slot, restrict each
                // unassigned neighbor's domain to "must be compatible with z".
                // Track the deltas so we can undo on backtrack.
                var savedDomains = new (int slotIdx, ulong oldDomain)[neighbors[slot].Count];
                int savedCount = 0;
                bool deadEnd = false;
                ulong propMask = compat[z];
                foreach (var nb in neighbors[slot])
                {
                    if (assignment[nb] != -1) continue;
                    ulong newD = domain[nb] & propMask;
                    if (newD == 0) { deadEnd = true; break; }
                    savedDomains[savedCount++] = (nb, domain[nb]);
                    domain[nb] = newD;
                }
                if (!deadEnd)
                {
                    // Also remove z from every OTHER unassigned slot's domain
                    // (since each zb can only be placed once). Track changes.
                    var savedZRemoval = new int[n];
                    int savedZCount = 0;
                    for (int s = 0; s < n; s++)
                    {
                        if (s == slot || assignment[s] != -1) continue;
                        if ((domain[s] & zBit) != 0)
                        {
                            domain[s] &= ~zBit;
                            savedZRemoval[savedZCount++] = s;
                        }
                    }
                    assignment[slot] = z;
                    used |= zBit;
                    Recurse();
                    assignment[slot] = -1;
                    used &= ~zBit;
                    for (int i = 0; i < savedZCount; i++)
                        domain[savedZRemoval[i]] |= zBit;
                }
                // Restore neighbor domains regardless of dead-end branch.
                for (int i = 0; i < savedCount; i++)
                    domain[savedDomains[i].slotIdx] = savedDomains[i].oldDomain;
            }
        }

        Recurse();
        return new Result { SolutionCount = count, HitCap = hitCap, Solutions = solutions, Frequencies = frequencies };
    }

    private static Solution Snapshot(int[] assignment)
    {
        var dict = new Dictionary<int, int>(assignment.Length);
        for (int i = 0; i < assignment.Length; i++)
            if (assignment[i] != -1) dict[i] = assignment[i];
        return new Solution { SeatToZbIndex = dict };
    }
}
