namespace ZoombiniHelper;

/// <summary>One round's permutation: which zoombini attribute lands in which
/// fleen slot, and the per-slot value bijection.
///
/// <para><c>TypeMap[a]</c> = the fleen slot (0..3) that displays
/// zoombini attribute <c>a</c>. <c>{0,1,2,3}</c> = identity.</para>
///
/// <para><c>ValueMap[s][v]</c> = the fleen-slot-<c>s</c> value (1..5) that a
/// zoombini whose <em>incoming</em> attribute (after the type shuffle)
/// equals <c>v</c> (1..5) ends up showing. Indexed 0..5; index 0 is unused
/// so values 1..5 read naturally. Reverse-engineered live: v2 uses an
/// arbitrary bijection per slot, not the cyclic shift the v1 doc claims.</para>
/// </summary>
public readonly record struct FleensPermutation(byte[] TypeMap, byte[][] ValueMap)
{
    /// <summary>Apply this permutation to a zoombini's attributes and
    /// return the fleen visual it should produce.</summary>
    public byte[] Apply(byte hair, byte eyes, byte nose, byte feet)
    {
        var src = new[] { hair, eyes, nose, feet };
        var dst = new byte[4];
        for (int a = 0; a < 4; a++)
        {
            int targetSlot = TypeMap[a];
            dst[targetSlot] = ValueMap[targetSlot][src[a]];
        }
        return dst;
    }
}

/// <summary>
/// Reverse-engineers the round's permutation from the visible zoombinis
/// and fleens. Two-stage solve:
///
/// <list type="number">
///   <item><b>Type-map</b> from per-attribute value distributions:
///   the multiset of pool-attribute-<c>a</c> values must equal the
///   multiset of fleen-slot-<c>s</c> values for the slot <c>s</c> that
///   <em>renders</em> attribute <c>a</c>. Usually unique.</item>
///   <item><b>Per-slot value bijection</b> by full bipartite enumeration
///   over count-equivalent values, validated by the global collision-free
///   pool-to-fleens match. Search space stays small (≪ 10⁵ in practice)
///   because most counts are distinct.</item>
/// </list>
///
/// Returns every permutation that fits the data — usually exactly one.
/// </summary>
public static class FleensSolver
{
    public static List<FleensPermutation> SolveAll(
        IReadOnlyList<PoolMember> zoombinis,
        IReadOnlyList<FleenMember> fleens)
    {
        var results = new List<FleensPermutation>();
        if (zoombinis.Count == 0 || fleens.Count == 0) return results;
        if (zoombinis.Count != fleens.Count) return results;

        var poolAttrs  = ExtractColumns(zoombinis,
            zb => new byte[] { zb.Hair, zb.Eyes, zb.Nose, zb.Feet });
        var fleenAttrs = ExtractColumns(fleens,
            fl => new byte[] { fl.A0, fl.A1, fl.A2, fl.A3 });

        // Stage 1: candidate type-maps consistent with multiset distributions.
        foreach (var typeMap in CandidateTypeMaps(poolAttrs, fleenAttrs))
        {
            // Stage 2: per-slot value bijections consistent with the typeMap.
            // The "incoming" multiset for slot s is the pool column for
            // attribute a where typeMap[a] == s.
            var incomingForSlot = new byte[4][];
            for (int a = 0; a < 4; a++) incomingForSlot[typeMap[a]] = poolAttrs[a];

            var perSlotBijections = new List<byte[]>[4];
            for (int s = 0; s < 4; s++)
                perSlotBijections[s] = ValueBijections(incomingForSlot[s], fleenAttrs[s]);

            // Cross-product of the per-slot bijections, validate each combo.
            EnumerateBijectionTuples(perSlotBijections, valueMap =>
            {
                var perm = new FleensPermutation(typeMap, valueMap);
                if (FindCollisionFreeMatch(perm, zoombinis, fleens))
                    results.Add(perm);
            });
        }
        return results;
    }

    /// <summary>Pull out the 4 "columns" (one per attribute) as byte arrays.</summary>
    private static byte[][] ExtractColumns<T>(IReadOnlyList<T> items, Func<T, byte[]> getRow)
    {
        var cols = new byte[4][];
        for (int c = 0; c < 4; c++) cols[c] = new byte[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var row = getRow(items[i]);
            for (int c = 0; c < 4; c++) cols[c][i] = row[c];
        }
        return cols;
    }

    /// <summary>Type-maps where, for every attribute <c>a</c>, the multiset
    /// of pool-column-<c>a</c> equals the multiset of fleen-column-<c>typeMap[a]</c>.
    /// Without that match, no value bijection can possibly succeed.</summary>
    private static IEnumerable<byte[]> CandidateTypeMaps(byte[][] poolCols, byte[][] fleenCols)
    {
        // Pre-compute fleen sorted multisets for fast equality.
        var fleenSorted = new int[4][];
        for (int s = 0; s < 4; s++) fleenSorted[s] = SortedCounts(fleenCols[s]);
        var poolSorted = new int[4][];
        for (int a = 0; a < 4; a++) poolSorted[a] = SortedCounts(poolCols[a]);

        foreach (var perm in AllPermutationsOf4())
        {
            bool ok = true;
            for (int a = 0; a < 4; a++)
            {
                if (!ArrayEqual(poolSorted[a], fleenSorted[perm[a]])) { ok = false; break; }
            }
            if (ok) yield return perm;
        }
    }

    /// <summary>Returns count of each value 1..5 sorted ascending — the
    /// shape-invariant of the column distribution.</summary>
    private static int[] SortedCounts(byte[] col)
    {
        var counts = new int[6];
        foreach (var b in col) if (b >= 1 && b <= 5) counts[b]++;
        var sorted = new int[5];
        Array.Copy(counts, 1, sorted, 0, 5);
        Array.Sort(sorted);
        return sorted;
    }

    private static bool ArrayEqual(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>All bijections {1..5} → {1..5} that map source-value-counts
    /// to target-value-counts. Each source value <c>v</c> can only map to a
    /// target value <c>v'</c> whose count equals <c>v</c>'s count.</summary>
    private static List<byte[]> ValueBijections(byte[] sourceCol, byte[] targetCol)
    {
        var srcCount = new int[6];
        var tgtCount = new int[6];
        foreach (var b in sourceCol) if (b >= 1 && b <= 5) srcCount[b]++;
        foreach (var b in targetCol) if (b >= 1 && b <= 5) tgtCount[b]++;

        // Group target values by count → list of options for each source value.
        var byCount = new Dictionary<int, List<byte>>();
        for (byte v = 1; v <= 5; v++)
            if (tgtCount[v] > 0)
            {
                if (!byCount.TryGetValue(tgtCount[v], out var lst))
                    byCount[tgtCount[v]] = lst = new List<byte>();
                lst.Add(v);
            }

        var bijections = new List<byte[]>();
        var current = new byte[6]; // index 0 unused; current[v] = mapped target
        var used    = new bool[6];
        EnumerateBijection(srcCount, byCount, current, used, sourceValue: 1, bijections);
        return bijections;
    }

    private static void EnumerateBijection(int[] srcCount,
        Dictionary<int, List<byte>> byCount, byte[] current, bool[] used,
        int sourceValue, List<byte[]> sink)
    {
        // Skip source values that don't appear in the source column —
        // their mapping doesn't matter for the bijection over present values.
        while (sourceValue <= 5 && srcCount[sourceValue] == 0) sourceValue++;
        if (sourceValue > 5)
        {
            // Snapshot, fill unused slots on the copy, push, and leave the
            // shared state untouched so the caller's recursion is correct.
            var snapshot = (byte[])current.Clone();
            var snapshotUsed = (bool[])used.Clone();
            FillUnusedSlots(snapshot, snapshotUsed);
            sink.Add(snapshot);
            return;
        }
        if (!byCount.TryGetValue(srcCount[sourceValue], out var options)) return;
        foreach (var t in options)
        {
            if (used[t]) continue;
            current[sourceValue] = t;
            used[t] = true;
            EnumerateBijection(srcCount, byCount, current, used, sourceValue + 1, sink);
            used[t] = false;
            current[sourceValue] = 0;
        }
    }

    /// <summary>Source values not present in the pool column have no
    /// constraint from data — assign them to any leftover targets so the
    /// bijection is total. Variation here doesn't change Apply's output for
    /// any actual zoombini, so we just pick one arbitrary completion.</summary>
    private static void FillUnusedSlots(byte[] current, bool[] used)
    {
        byte t = 1;
        for (byte v = 1; v <= 5; v++)
            if (current[v] == 0)
            {
                while (t <= 5 && used[t]) t++;
                if (t > 5) return; // shouldn't happen — bijection over 5 elements
                current[v] = t;
                used[t] = true;
            }
    }

    /// <summary>Recursively pick one bijection per slot, build the full
    /// ValueMap, and pass it to the caller. Clones at the leaf so callers
    /// can store the array without it being mutated by later recursion.</summary>
    private static void EnumerateBijectionTuples(
        List<byte[]>[] perSlot, Action<byte[][]> emit, int slot = 0, byte[][]? acc = null)
    {
        acc ??= new byte[4][];
        if (slot == 4)
        {
            var snapshot = new byte[4][];
            for (int s = 0; s < 4; s++) snapshot[s] = acc[s];
            emit(snapshot);
            return;
        }
        if (perSlot[slot].Count == 0) return;
        foreach (var bij in perSlot[slot])
        {
            acc[slot] = bij;
            EnumerateBijectionTuples(perSlot, emit, slot + 1, acc);
        }
    }

    /// <summary>Greedy bipartite match — claim the first unclaimed fleen
    /// matching the expected attrs for each zoombini in turn. If any
    /// zoombini has no match, the permutation is rejected.</summary>
    private static bool FindCollisionFreeMatch(
        FleensPermutation perm,
        IReadOnlyList<PoolMember> zoombinis,
        IReadOnlyList<FleenMember> fleens)
    {
        Span<bool> taken = stackalloc bool[fleens.Count];
        foreach (var z in zoombinis)
        {
            byte[] expected = perm.Apply(z.Hair, z.Eyes, z.Nose, z.Feet);
            int matchIdx = -1;
            for (int fi = 0; fi < fleens.Count; fi++)
            {
                if (taken[fi]) continue;
                var f = fleens[fi];
                if (f.A0 == expected[0] && f.A1 == expected[1] &&
                    f.A2 == expected[2] && f.A3 == expected[3])
                {
                    matchIdx = fi;
                    break;
                }
            }
            if (matchIdx < 0) return false;
            taken[matchIdx] = true;
        }
        return true;
    }

    private static IEnumerable<byte[]> AllPermutationsOf4()
    {
        var arr = new byte[] { 0, 1, 2, 3 };
        var sink = new List<byte[]>(24);
        Permute(arr, 0, sink);
        return sink;
    }

    private static void Permute(byte[] arr, int start, List<byte[]> sink)
    {
        if (start == arr.Length - 1) { sink.Add((byte[])arr.Clone()); return; }
        for (int i = start; i < arr.Length; i++)
        {
            (arr[start], arr[i]) = (arr[i], arr[start]);
            Permute(arr, start + 1, sink);
            (arr[start], arr[i]) = (arr[i], arr[start]);
        }
    }
}
