namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Statisches Gesamt-Modell des Bubblewonder-Grids für eine Runde —
/// alle Mechanismen, ihre Positionen, ihre Verkettung. Dies ist das
/// "innere Bild" das ein Spieler/Solver braucht um Reihenfolge-Entscheidungen
/// zu treffen.
///
/// <para><b>Statisch vs. live:</b> Diese Klasse hält die Definition des
/// Layouts (= unveränderlich für die Dauer der Runde, kommt aus REGS
/// + Engine-Init). Live-Zustand pro Mechanismus (Counter, gespeicherte
/// Handles, Toggle-Position) lebt in einer separaten Snapshot-Klasse,
/// die pro Tick aus Memory neu gelesen wird.</para>
///
/// <para><b>Quellen für die Felder:</b></para>
/// <list type="bullet">
///   <item><see cref="Difficulty"/>: aus <c>0x499B1C</c> (1..4) plus
///       Variant-Counter aus <c>0x49B098..0x49B09E</c></item>
///   <item><see cref="Mechanisms"/>: aus REGS-Resource <c>16600..16609</c>
///       je nach Difficulty + Variant (Loader-Logik in <c>FUN_004273F0</c>)</item>
///   <item><see cref="GridDimensions"/>: aus disasm-Hypothese (Prop1 ∈ 0..12,
///       Prop2 ∈ 0..4) — ggf. live zu validieren</item>
///   <item>Verkettung in <see cref="Mechanism.LinkedSlotIds"/>: aus
///       <c>FUN_004265F0</c> + Memory-Tabellen <c>0x49ABB8</c>, <c>0x49ABF0</c></item>
/// </list>
/// </summary>
public sealed record BubblewonderGrid(
    /// <summary>1-basierte Difficulty (1..4) plus optional 5 für Bonus-Modus.</summary>
    int Difficulty,
    /// <summary>Welche Variant der Difficulty geladen wurde
    /// (= Counter aus 0x49B098 etc., entscheidet Resource-ID-Wahl).</summary>
    int DifficultyVariant,
    /// <summary>Mohawk-Resource-ID (16600..16609) aus der das Layout stammt.</summary>
    int RegsResourceId,
    /// <summary>Alle Mechanismen, geordnet nach SlotId.</summary>
    IReadOnlyList<Mechanism> Mechanisms,
    /// <summary>Grid-Dimensionen (max Prop1+1, max Prop2+1).
    /// Aktuelle Hypothese: 13×5. Live-Validation pending.</summary>
    (int Width, int Height) GridDimensions)
{
    /// <summary>Anzahl Mechanismen total.</summary>
    public int Count => Mechanisms.Count;

    /// <summary>Lookup eines Mechanismus per SlotId. Null wenn nicht vorhanden.</summary>
    public Mechanism? this[int slotId] =>
        slotId >= 0 && slotId < Mechanisms.Count ? Mechanisms[slotId] : null;

    /// <summary>Alle Mechanismen an einer Grid-Position (kann mehrere geben
    /// wenn z.B. Bubble + Verschieber an gleicher Stelle).</summary>
    public IEnumerable<Mechanism> AtPosition(GridPosition pos) =>
        Mechanisms.Where(m => m.Position == pos);

    /// <summary>Mechanismen die ein bestimmtes ZB-Attribut prüfen
    /// (= relevante Conditionals für eine Eigenschaft).</summary>
    public IEnumerable<Mechanism> ConditionalsForAttribute(byte attribute) =>
        Mechanisms.Where(m => m.IsConditional && m.ConditionalAttribute == attribute);

    /// <summary>Statistik für Trust-Audit + Debug-Anzeige.</summary>
    public IReadOnlyDictionary<MechanismType, int> TypeHistogram() =>
        Mechanisms.GroupBy(m => m.Type)
                  .ToDictionary(g => g.Key, g => g.Count());
}
