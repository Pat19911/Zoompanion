namespace ZoombiniHelper;

/// <summary>
/// Snapshot of Stone Cold Caves filter state.
///
/// The 4 visible caves implement a 2D matching grid based on up to two
/// attribute axes. The match function at <c>0x00452CA0</c> in the binary
/// computes per-axis "any-of" matches and dispatches via cave index:
///
/// <list type="bullet">
///   <item>Cave 1: axis 1 match AND axis 2 match</item>
///   <item>Cave 2: axis 1 match AND axis 2 NOT-match</item>
///   <item>Cave 3: axis 1 NOT-match AND axis 2 NOT-match</item>
///   <item>Cave 4: axis 1 NOT-match AND axis 2 match</item>
/// </list>
///
/// Single-axis mode (Diff 1) collapses caves 1+2 (match) and 3+4 (no-match).
/// </summary>
public sealed class CavesState
{
    /// <summary>One axis filter: a list of (attr_type, variant) pairs plus
    /// an invert flag. A zoombini "raw-matches" the axis if it satisfies ANY
    /// of the pairs; the final match is XOR'd with <see cref="Invert"/>.
    ///
    /// The invert flag mirrors the engine's behaviour: at <c>0x00452D40</c>
    /// in the binary, <c>cmp word ptr [struct+2], 0; jne skip_invert</c>
    /// inverts the axis-1 match when the flag byte is zero. Same pattern at
    /// <c>+0x10</c> for axis 2. Means a flag value of 0 → INVERT, non-zero
    /// → keep direct.</summary>
    public readonly record struct AxisFilter(bool Invert, IReadOnlyList<(byte AttrType, byte Variant)> Conditions)
    {
        public bool Matches(PoolMember zb)
        {
            bool any = false;
            foreach (var (type, variant) in Conditions)
            {
                byte zbAttr = type switch
                {
                    1 => zb.Hair,
                    2 => zb.Eyes,
                    3 => zb.Nose,
                    4 => zb.Feet,
                    _ => 0,
                };
                if (zbAttr == variant) { any = true; break; }
            }
            return any ^ Invert;
        }
    }

    public int Difficulty { get; }
    public int AxisCount { get; }   // 1 = single axis (Diff 1), 2 = dual axis (Diff 2..4)
    public AxisFilter Axis1 { get; }
    public AxisFilter Axis2 { get; } // only meaningful when AxisCount == 2

    public bool IsActive => Axis1.Conditions.Count > 0;

    private CavesState(int diff, int axisCount, AxisFilter a1, AxisFilter a2)
    {
        Difficulty = diff;
        AxisCount = axisCount;
        Axis1 = a1;
        Axis2 = a2;
    }

    public static CavesState Read(IMemoryReader mem)
    {
        var raw = mem.ReadBytes(CavesMemoryMap.CaveStruct, 0x20);
        if (raw is null)
            return new CavesState(
                mem.ReadWord(CavesMemoryMap.Difficulty),
                axisCount: 0,
                a1: new AxisFilter(false, Array.Empty<(byte, byte)>()),
                a2: new AxisFilter(false, Array.Empty<(byte, byte)>()));

        int axisCount = BitConverter.ToUInt16(raw, 0);
        // Invert flag: byte at struct+2 (axis 1), struct+0x10 (axis 2).
        // Engine logic: flag == 0 → invert.
        bool invertA1 = BitConverter.ToUInt16(raw, 0x02) == 0;
        bool invertA2 = BitConverter.ToUInt16(raw, 0x10) == 0;
        var a1 = ReadAxis(raw, filterCountOff: 0x04, typeOff: 0x05, varOff: 0x0A, invert: invertA1);
        var a2 = axisCount >= 2
            ? ReadAxis(raw, filterCountOff: 0x12, typeOff: 0x13, varOff: 0x18, invert: invertA2)
            : new AxisFilter(false, Array.Empty<(byte, byte)>());

        return new CavesState(
            diff: mem.ReadWord(CavesMemoryMap.Difficulty),
            axisCount: axisCount,
            a1: a1,
            a2: a2);
    }

    private static AxisFilter ReadAxis(byte[] raw, int filterCountOff, int typeOff, int varOff, bool invert)
    {
        byte count = raw[filterCountOff];
        if (count is < 1 or > 5) return new AxisFilter(invert, Array.Empty<(byte, byte)>());
        var conditions = new List<(byte, byte)>(count);
        for (int i = 0; i < count; i++)
        {
            byte t = raw[typeOff + i];
            byte v = raw[varOff + i];
            if (t is < 1 or > 4 || v is < 1 or > 5) continue;
            conditions.Add((t, v));
        }
        return new AxisFilter(invert, conditions);
    }

    /// <summary>Returns the cave index (1..4) that accepts the given zoombini,
    /// or null if no cave accepts it (rare — usually exactly one cave does).
    ///
    /// In the v2 binary's match function (<c>0x00452CA0</c>), single-axis
    /// caves 1+2 are bytewise-identical and so are caves 3+4. Visual cave
    /// indexing apparently doesn't line up 1:1 with the code-side caves:
    /// user-verified that for a matching zb, cave 2 accepts (not cave 1).
    /// We therefore recommend cave 2 / cave 3 at single-axis to align with
    /// what the player actually sees.</summary>
    public int? FindAcceptingCave(PoolMember zb)
    {
        bool m1 = Axis1.Matches(zb);
        if (AxisCount < 2) return m1 ? 2 : 3;
        bool m2 = Axis2.Matches(zb);
        return (m1, m2) switch
        {
            (true,  true)  => 1,
            (true,  false) => 2,
            (false, false) => 3,
            (false, true)  => 4,
        };
    }
}
