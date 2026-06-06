namespace ZoombiniHelper;

/// <summary>
/// One round of "Fleens!" decoded from memory: the zoombinis on the
/// playfield, the fleens currently shown, and any permutations that
/// explain how the engine mapped one to the other.
///
/// The permutation list usually has exactly one element. It can have
/// more when the pool is symmetric enough that the problem is genuinely
/// under-determined (see <see cref="FleensSolver"/>); it can be empty
/// when zoombinis and fleens haven't been read yet (e.g. the round just
/// loaded and one of the lists is still being populated).
/// </summary>
public sealed class FleensState
{
    public IReadOnlyList<PoolMember>         Zoombinis    { get; }
    public IReadOnlyList<FleenMember>        Fleens       { get; }
    public IReadOnlyList<FleensPermutation>  Permutations { get; }

    /// <summary>The three "boss" fleen indices the engine selected for this
    /// round (1-based). Empty list when none could be read. Indexing semantics
    /// not yet live-verified — see <see cref="FleensMemoryMap"/>.</summary>
    public IReadOnlyList<int> SpecialIndices { get; }

    public bool IsActive => Zoombinis.Count > 0 && Fleens.Count > 0;

    private FleensState(IReadOnlyList<PoolMember> zb, IReadOnlyList<FleenMember> fl,
                        IReadOnlyList<FleensPermutation> perms,
                        IReadOnlyList<int> specials)
    {
        Zoombinis      = zb;
        Fleens         = fl;
        Permutations   = perms;
        SpecialIndices = specials;
    }

    public static FleensState Read(IMemoryReader mem)
    {
        var zoombinis = FleensScanner.ScanAllZoombinis(mem);
        IReadOnlyList<FleenMember> fleens = FleensScanner.ScanFleens(mem);
        var perms     = FleensSolver.SolveAll(zoombinis, fleens);
        var specials  = ReadSpecials(mem, maxIndex: zoombinis.Count);

        // Resolve specials → tree fleens via shared-state heap struct.
        // For each special index s (1-based ZB index in the game-internal
        // ZB array), read the ZB attrs at [SharedStatePtr] + 0xb83c + (s-1)*0x14,
        // then run them through the permutation to find the matching fleen.
        // Mark that fleen as TreeMarker=1.
        if (perms.Count > 0 && specials.Count > 0)
        {
            var basePtrBytes = mem.ReadBytes(FleensMemoryMap.SharedStatePtr, 4);
            if (basePtrBytes is not null)
            {
                uint basePtr = BitConverter.ToUInt32(basePtrBytes, 0);
                if (basePtr >= 0x10000 && basePtr < 0x80000000)
                    fleens = MarkTreeFleens(mem, fleens, perms[0], specials, (nint)basePtr);
            }
        }

        return new FleensState(zoombinis, fleens, perms, specials);
    }

    /// <summary>For each special-index, look up the corresponding zoombini's
    /// attributes in the heap struct, apply the round's permutation to get
    /// the matching fleen's expected visual, and flip TreeMarker on the
    /// matching fleen in our list.</summary>
    private static IReadOnlyList<FleenMember> MarkTreeFleens(
        IMemoryReader mem, IReadOnlyList<FleenMember> fleens,
        FleensPermutation perm, IReadOnlyList<int> specials, nint basePtr)
    {
        var result = new List<FleenMember>(fleens.Count);
        var treeAttrs = new HashSet<(byte, byte, byte, byte)>();

        foreach (var sIdx in specials)
        {
            int di = sIdx - 1;
            nint attrsAddr = basePtr + FleensMemoryMap.ZbAttrsOffset + di * FleensMemoryMap.ZbStride;
            var raw = mem.ReadBytes(attrsAddr, 4);
            if (raw is null) continue;
            byte h = raw[0], e = raw[1], n = raw[2], f = raw[3];
            if (h is < 1 or > 5 || e is < 1 or > 5 || n is < 1 or > 5 || f is < 1 or > 5) continue;
            byte[] expected = perm.Apply(h, e, n, f);
            treeAttrs.Add((expected[0], expected[1], expected[2], expected[3]));
        }

        foreach (var f in fleens)
        {
            byte mark = treeAttrs.Contains((f.A0, f.A1, f.A2, f.A3)) ? (byte)1 : (byte)0;
            result.Add(f with { TreeMarker = mark });
        }
        return result;
    }

    private static List<int> ReadSpecials(IMemoryReader mem, int maxIndex)
    {
        var raw = new[]
        {
            mem.ReadWord(FleensMemoryMap.SpecialIndex1),
            mem.ReadWord(FleensMemoryMap.SpecialIndex2),
            mem.ReadWord(FleensMemoryMap.SpecialIndex3),
        };
        var list = new List<int>(3);
        foreach (var v in raw)
            if (v >= 1 && (maxIndex == 0 || v <= maxIndex)) list.Add(v);
        return list;
    }

    /// <summary>Look up the held zoombini's 1-based index in the engine-list
    /// order (heuristic — same order <see cref="FleensScanner.ScanAllZoombinis"/>
    /// would yield if it weren't y-sorted). Returns null when no zoombini
    /// is held or the held one isn't in the pool.</summary>
    public int? IndexOf(PoolMember held)
    {
        for (int i = 0; i < Zoombinis.Count; i++)
            if (Zoombinis[i].Address == held.Address) return i + 1;
        return null;
    }
}
