using System.Security.Cryptography;
using System.Text;

namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Hardcoded Goal-Cell-Tabelle pro Map-Layout. Wird beim Tracker-Init in
/// die KnownGoalCells des Sims geladen, damit der Solver vom ersten ZB an
/// alle bekannten Exits kennt — auch nach Round-Reset.
///
/// <para>Map-Identifier ist ein SHA1 über die sortierte Mechanism-Liste
/// (Type, Prop1, Prop2). Die hier hinterlegten Goal-Cells stammen aus
/// historischen Live-Beobachtungen (memdump-Analyse aller Sessions).</para>
///
/// <para>Bei Anwendung filtert <see cref="Simulator.BubblewonderSimulator"/>
/// per Cell-Typ raus: ist auf der Cell ein StaticDeflector / Conditional /
/// Switch / Sticky / Trap, gewinnt die Mechanik (Out-of-Grid → Scored
/// kommt automatisch über die normale Sim-Logik). Hardcoded Goal greift
/// nur, wenn die Cell passiv ist (leer / Passthrough / Unknown).</para>
/// </summary>
public static class BubblewonderKnownMaps
{
    /// <summary>Map-Fingerprint → Goal-Cell-Positionen (row*13 + col).
    /// Aufgebaut aus 34 historischen Dumps.</summary>
    private static readonly Dictionary<string, int[]> GoalCellsByFingerprint = new()
    {
        // GELEERT 2026-05-27. Diese Tabelle wurde VOR dem +0x76-Outcome-Fix
        // (2026-05-26) per Live-Beobachtung „gelernt" und war systematisch mit
        // Nicht-Ziel-Zellen verseucht (geparkte/gefangene ZB-Positionen als Goal):
        // z.B. (0,8),(3,7),(4,7),(1,3),(2,8),(1,8) liegen NICHT im Ziel-Steinbereich.
        //
        // Ersetzt durch die engine-eigene Zelltyp-Tabelle
        // (BubblewonderMemoryMap.CellTypeTable, 0x17 = Ziel), die der
        // BubblewonderGridModelBuilder pro Tick LIVE liest — map-unabhängig,
        // kein Lernen, keine Verschmutzung. Verifiziert über 62 Dumps / 8 Layouts.
    };

    /// <summary>SHA1 der sortierten "(Type@Row,Col)"-Liste, erste 16 Hex-Chars.
    /// Stabiler Map-Fingerprint, unabhängig von Mechanism-Slot-Reihenfolge.</summary>
    public static string ComputeFingerprint(IEnumerable<Mechanism> mechanisms)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var m in mechanisms
                     .Select(m => (m.Type, m.Position.Prop1, m.Position.Prop2))
                     .OrderBy(t => t.Type).ThenBy(t => t.Prop1).ThenBy(t => t.Prop2))
        {
            if (!first) sb.Append(';');
            sb.Append(m.Type).Append('@').Append(m.Prop1).Append(',').Append(m.Prop2);
            first = false;
        }
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var hex = new StringBuilder(16);
        for (int i = 0; i < 8; i++) hex.Append(hash[i].ToString("x2"));
        return hex.ToString();
    }

    /// <summary>Bekannte Goal-Cells für diese Map. Leeres Array wenn die
    /// Map noch nie beobachtet wurde — dann lebt der Solver nur von der
    /// Live-Lernung.</summary>
    public static IReadOnlyList<int> GetGoalCells(IEnumerable<Mechanism> mechanisms)
    {
        var fp = ComputeFingerprint(mechanisms);
        return GoalCellsByFingerprint.TryGetValue(fp, out var cells)
            ? cells
            : Array.Empty<int>();
    }

    public static IReadOnlyList<int> GetGoalCells(BubblewonderState state) =>
        GetGoalCells(state.Grid.Mechanisms);
}
