namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Live-Match-Funktion: gegeben eine Bubble-Cell mit Filter-Configuration und einen Zoombini,
/// "öffnet" die Cell für diesen ZB?
///
/// <para>Quelle: <c>FUN_0044A920</c> in v2 PE32. Filter-Configuration steht direkt
/// im Engine-Object bei <c>+0xF0..+0xF3</c> als 4 Bytes (hair, eyes, nose, feet).
/// Match-Regel: für jedes Byte mit Wert &gt; 0 muss ZB-Attribut gleich sein.
/// Bytes mit Wert 0 sind irrelevant (=Wildcard).</para>
///
/// <para>Damit ist die "öffnender Wert"-Logik DIREKT live aus der Memory lesbar —
/// keine Pre-Computing-Heuristik, keine SCRB-Bytecode-Interpretation, keine vtable-Suche.</para>
/// </summary>
public static class ConditionalMatcher
{
    /// <summary>Resultat einer Match-Anfrage.</summary>
    public enum MatchResult
    {
        /// <summary>Filter ist leer (alle 4 Bytes 0) — jeder ZB öffnet.</summary>
        Wildcard,
        /// <summary>Bubble öffnet für diesen ZB.</summary>
        Match,
        /// <summary>Bubble öffnet NICHT.</summary>
        NoMatch,
    }

    /// <summary>Prüft live ob ein ZB die Filter-Cell öffnet.
    /// Nutzt direkt die <see cref="FilterConfig"/> aus dem Bubble-Object.</summary>
    /// <param name="filter">Live-Filter-Configuration (aus <c>obj[+0xF0]</c>).</param>
    /// <param name="hair">ZB-Attribut Hair (1..5).</param>
    /// <param name="eyes">ZB-Attribut Eyes (1..5).</param>
    /// <param name="nose">ZB-Attribut Nose (1..5).</param>
    /// <param name="feet">ZB-Attribut Feet (1..5).</param>
    public static MatchResult CheckMatch(FilterConfig filter,
                                         byte hair, byte eyes, byte nose, byte feet)
    {
        if (filter.IsEmpty) return MatchResult.Wildcard;

        if (filter.Hair > 0 && filter.Hair != hair) return MatchResult.NoMatch;
        if (filter.Eyes > 0 && filter.Eyes != eyes) return MatchResult.NoMatch;
        if (filter.Nose > 0 && filter.Nose != nose) return MatchResult.NoMatch;
        if (filter.Feet > 0 && filter.Feet != feet) return MatchResult.NoMatch;

        return MatchResult.Match;
    }

    /// <summary>Convenience-Overload für PoolMember.</summary>
    public static MatchResult CheckMatch(FilterConfig filter, PoolMember zb) =>
        CheckMatch(filter, zb.Hair, zb.Eyes, zb.Nose, zb.Feet);

    /// <summary>Convenience-Overload für BubbleObject + ZB.</summary>
    public static MatchResult CheckMatch(BubbleObject bubble, PoolMember zb) =>
        CheckMatch(bubble.FilterConfig, zb);

    /// <summary>Status-Beschreibung für UI/Debug.</summary>
    public static string DescribeResult(MatchResult r) => r switch
    {
        MatchResult.Wildcard => "*",
        MatchResult.Match => "✓",
        MatchResult.NoMatch => "✗",
        _ => "??",
    };
}
