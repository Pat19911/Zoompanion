namespace ZoombiniHelper;

/// <summary>
/// Builds the Stone Rise solver pool from the live object scan plus the
/// currently-held zoombini, guaranteeing each physical zoombini appears
/// exactly once.
///
/// <para><b>Why this exists.</b> <see cref="PoolScanner.Scan"/> returns every
/// zoombini node whose handle is in <see cref="ZoombiniHandle.All"/> — and that
/// set includes the drag handle <c>0x04001001</c> (<see cref="ZoombiniHandle.Held"/>).
/// So while the player drags a zoombini, the scan ALREADY contains it. The
/// renderer also receives the held zb separately. If it naively appended the
/// held zb, the pool would carry it twice: the pool count then exceeds the slot
/// count, and the perfect-matching solver can fill every slot while leaving a
/// real zoombini stranded — a physically impossible "solution" whose
/// recommended move dead-ends the moment the player follows it
/// (symptom: "keine weiteren Spielzüge möglich" right after the suggested drop).</para>
///
/// <para>This builder drops the scanned copy of the held zb (matched by node
/// address — both the scanner and <c>HeldZoombini.Find</c> compute the same
/// <see cref="PoolMember.Address"/>) and then re-adds it once as the canonical
/// held entry, so <c>SolverPool.Count</c> always equals the zoombini count.</para>
/// </summary>
public static class StoneRisePoolBuilder
{
    /// <param name="BaseMembers">The scanned pool with the held zb removed,
    /// index-aligned with the first <c>BaseMembers.Count</c> entries of
    /// <see cref="SolverPool"/>. Use these for the placed-slot HeaderId lookup.</param>
    /// <param name="SolverPool">The solver-ready pool: base members followed by
    /// the held zb (if any). Exactly one entry per physical zoombini.</param>
    /// <param name="HeldPoolIndex">Index of the held zb in <see cref="SolverPool"/>,
    /// or -1 if nothing is held.</param>
    public readonly record struct Result(
        IReadOnlyList<PoolMember> BaseMembers,
        IReadOnlyList<StoneRiseSolver.PoolZb> SolverPool,
        int HeldPoolIndex);

    public static Result Build(IReadOnlyList<PoolMember> scannedPool, PoolMember? held)
    {
        // Remove the scanned copy of the held zb (if present) so it is not
        // counted twice. Matching by Address is exact: the scanner and the
        // held-zb finder both build PoolMember.Address as node.Address + recOff.
        var baseMembers = held is { } h
            ? scannedPool.Where(p => p.Address != h.Address).ToList()
            : scannedPool.ToList();

        var solverPool = baseMembers
            .Select(p => new StoneRiseSolver.PoolZb(p.Hair, p.Eyes, p.Nose, p.Feet))
            .ToList();

        int heldPoolIndex = -1;
        if (held is { } heldZb)
        {
            heldPoolIndex = solverPool.Count;
            solverPool.Add(new StoneRiseSolver.PoolZb(heldZb.Hair, heldZb.Eyes, heldZb.Nose, heldZb.Feet));
        }

        return new Result(baseMembers, solverPool, heldPoolIndex);
    }
}
