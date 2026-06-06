namespace ZoombiniHelper;

/// <summary>
/// Snapshot of Cliff-puzzle (bridge.mhk) state, decoded from raw memory.
/// Build via <see cref="Read"/>. Pure data — no rendering, no I/O after construction.
/// Held-zoombini detection lives in <c>Drag.HeldZoombini</c>; this class only
/// carries the puzzle-specific rule/bookkeeping fields.
/// </summary>
public sealed class CliffState
{
    /// <summary>Single rule: which attribute type and which variant value(s) trigger a sneeze.</summary>
    public readonly record struct Rule(byte Type, byte Value)
    {
        public int LowVariant  => Value & 0x0F;
        public int HighVariant => (Value >> 4) & 0x0F;
        public bool Matches(int variant) => variant == LowVariant
                                         || (HighVariant != 0 && variant == HighVariant);
    }

    public byte NAllerg { get; }
    public ushort Attempts { get; }
    public ushort WhichCliff { get; }
    /// <summary>1-based difficulty (1=Easy..4=Very Hard). Read from
    /// <c>0x0049453C</c>, which the rule generator dispatches off
    /// (0-based there, +1 here for human display).</summary>
    public int Difficulty { get; }
    public IReadOnlyList<Rule> Rules { get; }

    /// <summary>The bridge that sneezes (rejects matching zoombinis).
    /// In v2: which_cliff=0 means Lower accepts → Upper rejects.</summary>
    public string RejectingBridgeLabel =>
        Localization.Loc.T(WhichCliff == 0 ? "bridge.upper" : "bridge.lower");

    /// <summary>The bridge that accepts everything (no sneeze).</summary>
    public string AcceptingBridgeLabel =>
        Localization.Loc.T(WhichCliff == 0 ? "bridge.lower" : "bridge.upper");

    /// <summary>One-letter marker for the rejecting bridge (language-safe;
    /// the raw label's first character isn't distinct in every language).</summary>
    public string RejectingBridgeAbbr =>
        Localization.Loc.T(WhichCliff == 0 ? "bridge.upper.abbr" : "bridge.lower.abbr");

    /// <summary>One-letter marker for the accepting bridge.</summary>
    public string AcceptingBridgeAbbr =>
        Localization.Loc.T(WhichCliff == 0 ? "bridge.lower.abbr" : "bridge.upper.abbr");

    public bool IsActive => Rules.Count > 0;

    private CliffState(byte nAllerg, ushort attempts, ushort which, int difficulty,
                       IReadOnlyList<Rule> rules)
    {
        NAllerg = nAllerg;
        Attempts = attempts;
        WhichCliff = which;
        Difficulty = difficulty;
        Rules = rules;
    }

    public static CliffState Read(IMemoryReader mem)
    {
        var rules = new List<Rule>();
        for (int i = 0; i < CliffMemoryMap.AllergySlots; i++)
        {
            byte type = mem.ReadByte(CliffMemoryMap.AllergyType0 + i);
            byte val  = mem.ReadByte(CliffMemoryMap.AllergyVal0 + i);
            // Active slot: type is one of {Hair, Eyes, Nose, Feet}
            if (type is >= 1 and <= 4)
                rules.Add(new Rule(type, val));
        }

        return new CliffState(
            nAllerg: mem.ReadByte(CliffMemoryMap.NAllerg),
            attempts: mem.ReadWord(CliffMemoryMap.Attempts),
            which: mem.ReadWord(CliffMemoryMap.WhichCliff),
            difficulty: mem.ReadWord(CliffMemoryMap.Difficulty) + 1, // engine: 0-based, UI: 1-based
            rules: rules);
    }
}
