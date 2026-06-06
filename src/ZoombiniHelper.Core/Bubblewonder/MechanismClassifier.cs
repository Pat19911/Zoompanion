namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Klassifiziert einen rohen REGS-Record in einen <see cref="MechanismType"/>.
///
/// <para><b>Empirische Klassifikation</b> aus Analyse aller 289 Records über
/// die 10 REGS-Resources (siehe <c>scratch/regs_classify.py</c>):</para>
///
/// <list type="bullet">
///   <item><b>f0=1</b> (21 records, 7%): immer f3=0, nie conditional →
///       <b>Trap/Killer</b> (gelb markiert im Spiel-UI). ZB der hier landet
///       platzt und ist verloren. Live-verifiziert 2026-05-01 via Diff 1
///       Dump (memdump-151220.txt) + User-Screenshot.</item>
///   <item><b>f0=2</b> (71 records, 25%): immer f3=1, 98.6% conditional auf
///       Hair/Eyes/Nose/Feet → "easy" Conditional Verschieber</item>
///   <item><b>f0=3</b> (141 records, 49%): immer f3=1, 96.5% conditional →
///       Standard Conditional Verschieber, häufigster Typ</item>
///   <item><b>f0=4</b> (26 records, 9%): f3 ∈ {1,4,5,6,7}, nie conditional →
///       Toggle-Mechanismus mit 5 Sub-Types (f3-Wert kodiert Variant)</item>
///   <item><b>f0=5</b> (10 records, 3.5%): f3 ∈ {3,4,5,6}, nie conditional →
///       Special-Mechanismus (Goal? Bonus?). Nur in Diff 2,4,5 vorhanden.</item>
///   <item><b>f0=6</b> (20 records, 7%): f3 ∈ {4,5,6,7}, nie conditional →
///       weiterer Special-Typ. Nicht in Diff 1.</item>
/// </list>
///
/// <para><b>Konfidenz:</b> die f0-zu-Conditional-Korrelation ist klar
/// (statistisch validiert). Welcher konkrete Spielmechanismus hinter
/// f0=4/5/6 liegt (Toggle vs. SwitchActivated vs. Goal/Trap) muss noch
/// live verifiziert werden — wir markieren sie konservativ.</para>
/// </summary>
public static class MechanismClassifier
{
    /// <summary>Klassifiziere einen REGS-Record. Default = Unknown wenn nichts passt.</summary>
    public static MechanismType Classify(RegsRecord r)
    {
        return r.F0 switch
        {
            1 => MechanismType.Trap,
            // f0=2 = Conditional-Filter (reagiert nur auf ZBs mit bestimmtem Attribut).
            //   Direction aus f4-f7 (= wohin lenken wenn ZB matcht).
            //   Attribut-Index aus f8 (1..4 = Hair/Eyes/Nose/Feet).
            //   f9=1 ist ein zusätzliches Special-Bit (genaue Bedeutung offen).
            // Live-verifiziert via User: ZB 0x0008 lief über (2,8) Mech [14] mit f0=2,
            //   das war eine "wenn Rollschuhe nach rechts" Conditional Cell.
            2 => r.HasDirection ? MechanismType.Conditional : MechanismType.Unknown,
            // f0=3 = unconditional StaticDeflector (immer in der Direction f4-f7).
            3 => r.HasDirection ? MechanismType.StaticDeflector : MechanismType.Unknown,
            // f0=4 = echter Switch (rot, umstellbar von Triggern). State in +0x7C.
            4 => MechanismType.SwitchActivated,
            // f0=5 = Sticky/Festklebefeld (Sternchen). Color-Channel in f3, klebender ZB in +0x86.
            //   Live-verifiziert 2026-05-02 via Pair-Detection (pos 43 + 148 in REGS 16608).
            5 => MechanismType.Sticky,
            // f0=6 = Trigger/Umswitcher (blau). Target-Switch-Handle in +0x166.
            //   Live-verifiziert 2026-05-02 via Action-at-Distance Test (Trigger pos=56 → Switch pos=44).
            6 => MechanismType.Trigger,
            _ => MechanismType.Unknown,
        };
    }

    /// <summary>Liste der bekannten f0-Werte (1..6).</summary>
    public static readonly IReadOnlyList<byte> KnownF0Values = new byte[] { 1, 2, 3, 4, 5, 6 };

    /// <summary>Welche f0-Werte sind conditional (= prüfen ZB-Eigenschaft)?</summary>
    public static bool IsConditionalType(byte f0) => f0 == 2 || f0 == 3;

    /// <summary>Welche f0-Werte sind aktive Mechanismen (state-mutierend)?
    /// f0=1 ist passive, 2-6 sind aktiv (jede Durchquerung tut etwas).</summary>
    public static bool IsActiveMechanism(byte f0) => f0 != 1;
}
