namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Position eines Mechanismus auf dem Bubblewonder-Grid.
///
/// <para>Aus disasm: Engine-Object-Felder <c>+0x72</c> (Prop1, 0..12) und
/// <c>+0x74</c> (Prop2, 0..4). Live-2D-Index in Counter-Tabelle bei
/// <c>0x49ACA0</c> = <c>(Prop1 * 13 + Prop2) * 6</c>.</para>
///
/// <para><b>Achtung Mapping ungeklärt:</b> ob Prop1 = Reihe und Prop2 = Spalte
/// (oder umgekehrt) ist noch nicht live-validiert. Auch ob die Werte exakt
/// die statischen REGS-Felder f1, f2 spiegeln oder ob die Engine sie
/// transformiert (siehe FUN_0040A980).</para>
/// </summary>
public readonly record struct GridPosition(byte Prop1, byte Prop2)
{
    /// <summary>2D-Index entsprechend dem Engine-Layout (für Counter-Tabelle).</summary>
    public int Index => Prop1 * 13 + Prop2;

    public override string ToString() => $"({Prop1},{Prop2})";
}

/// <summary>
/// Einzelner Mechanismus auf dem Bubblewonder-Grid. Statische Definition
/// aus REGS-Record + Verkettungs-Topologie. Live-Zustand kommt separat in
/// einer Snapshot-Klasse (Task 5).
///
/// <para>Provenance pro Feld:</para>
/// <list type="bullet">
///   <item><c>SlotId</c> = Index 0..N-1 in der REGS-Resource</item>
///   <item><c>Type</c> = Hypothese aus REGS-Feld f0 (1..6)</item>
///   <item><c>Position</c> = REGS f1, f2 (vermutlich)</item>
///   <item><c>ConditionalAttribute</c> = REGS f4-f7 one-hot
///       (4=Hair, 5=Eyes, 6=Nose, 7=Feet); null wenn nicht conditional</item>
///   <item><c>LinkedSlotIds</c> = Triple-Verkettung aus FUN_004265F0:
///       primary, secondary (bedingt), tertiary. Live aus Memory zu lesen.</item>
/// </list>
/// </summary>
public sealed record Mechanism(
    int SlotId,
    MechanismType Type,
    GridPosition Position,
    /// <summary>Welches ZB-Attribut prüft der Mechanismus (1=Hair..4=Feet).
    /// Null wenn Type != Conditional. Wert kommt aus REGS f4-f7 one-hot.</summary>
    byte? ConditionalAttribute,
    /// <summary>Welcher Variant-Wert (1..5) öffnet die conditional Route.
    /// Engine-versteckt (FUN_0040A980). Null bis Task 7 gelöst ist.</summary>
    byte? ConditionalValue,
    /// <summary>IDs der Slots die mit diesem Mechanismus verkettet sind.
    /// Aktivierung dieses Slots kann State der verlinkten Slots ändern.
    /// Aus FUN_004265F0 + Live-Memory (Tabellen 0x49ABB8, 0x49ABF0).</summary>
    IReadOnlyList<int> LinkedSlotIds,
    /// <summary>Roher REGS-Record (10 BE-words) für Diagnose / weitere Decodierung.</summary>
    IReadOnlyList<ushort> RawFields,
    /// <summary>Pfeil-Richtung der Cell aus REGS f4-f7. Null wenn Trap/Switch
    /// ohne Direction. Für Conditional Cells gilt die Direction nur wenn ZB matcht.</summary>
    ArrowDirection? Direction = null)
{
    /// <summary>True wenn dieser Mechanismus eigenschaftsabhängig routet.</summary>
    public bool IsConditional => Type == MechanismType.Conditional && ConditionalAttribute.HasValue;

    /// <summary>True wenn dieser Mechanismus seinen Zustand bei Durchlauf ändert.
    /// (Toggle ändert pro Durchlauf, SwitchActivated nur wenn passende Bedingung.)</summary>
    public bool MutatesOnTraverse => Type is MechanismType.Toggle or MechanismType.SwitchActivated;

    public override string ToString() =>
        $"Slot {SlotId}: {Type} @ {Position}" +
        (IsConditional ? $" [Attr={ConditionalAttribute}]" : "") +
        (LinkedSlotIds.Count > 0 ? $" → {string.Join(",", LinkedSlotIds)}" : "");
}
