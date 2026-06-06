using ZoombiniHelper.Localization;

namespace ZoombiniHelper;

/// <summary>
/// Display names for the 5 variants per attribute type, indexed 1..5. The
/// German originals were all live-verified by user observation 2026-04-28;
/// the actual strings now come from the localization tables
/// (variant.{type}.{1..5}) so the helper can show them in any supported
/// language. Index 0 is the "unknown" placeholder "?".
///
/// Color-pair caveats (user-flagged): Nose [1]/[2] (grün/orange) and
/// [4]/[5] (lila/blau) are easy to confuse on small sprites — if a match
/// recommendation feels off and the color pair is one of these, suspect a
/// swap before assuming a logic bug.
/// </summary>
public static class ZoombiniVariants
{
    public const byte Hair = 1, Eyes = 2, Nose = 3, Feet = 4;

    /// <summary>Attribute type id → localization sub-key ("hair"/"eyes"/…).</summary>
    private static string TypeKey(byte type) => type switch
    {
        Hair => "hair",
        Eyes => "eyes",
        Nose => "nose",
        Feet => "feet",
        _ => "",
    };

    public static string AttributeName(byte type) => type switch
    {
        Hair => Loc.T("attr.hair"),
        Eyes => Loc.T("attr.eyes"),
        Nose => Loc.T("attr.nose"),
        Feet => Loc.T("attr.feet"),
        _ => $"?({type})",
    };

    public static string VariantName(byte type, int index)
    {
        if (index == 0) return "?";
        string tk = TypeKey(type);
        if (tk.Length == 0 || index < 1 || index > 5)
            return Loc.T("variant.fallback", index);
        string key = $"variant.{tk}.{index}";
        return Loc.Has(key) ? Loc.T(key) : Loc.T("variant.fallback", index);
    }

    /// <summary>Decode a CliffState rule's value byte: low+high nibble = 1 or 2 variants.</summary>
    public static string DescribeRuleValue(byte type, byte value)
    {
        int low = value & 0x0F;
        int high = (value >> 4) & 0x0F;
        return high == 0
            ? VariantName(type, low)
            : Loc.T("rule.or", VariantName(type, low), VariantName(type, high));
    }
}
