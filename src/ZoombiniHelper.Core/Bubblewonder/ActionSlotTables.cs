namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Statische Tabellen aus dem v2 EXE für die 24 Action-Slots eines
/// Bubblewonder-Layouts. Wird von <see cref="ConnectionBuilder"/> genutzt
/// um aus Live-Memory die Verkettungs-Topologie zu rekonstruieren.
///
/// <para>Beide Tabellen sind im EXE bei <c>0x0048D658</c> und <c>0x0048D690</c>
/// (jeweils 24 × 2 Bytes), extrahiert via pe_loader.py. Werte konstant zur
/// Compile-Zeit, hier hardcoded.</para>
/// </summary>
public static class ActionSlotTables
{
    /// <summary>Anzahl Action-Slots im Bubblewonder-System.</summary>
    public const int SlotCount = 24;

    /// <summary>Per Slot 0..23: Flag aus EXE-Tabelle <c>0x0048D658</c>.
    /// 0 = kein secondary Object, !=0 = secondary Object existiert (sollte
    /// aus <c>DAT_0049ABF0</c> gelesen werden). Wert 2 nur für Slot 23 (special).</summary>
    public static readonly IReadOnlyList<byte> HasSecondaryFlag = new byte[]
    {
        0, 0, 0, 1, 1, 1, 1, 1,
        1, 0, 0, 0, 1, 1, 0, 0,
        0, 0, 0, 0, 1, 1, 1, 2,
    };

    /// <summary>Per Slot 0..23: Initiale SCRB-ID aus <c>0x0048D690</c>.
    /// Slots 0-13 haben SCRB-IDs im 0x2328-0x232F-Bereich (= 9000-9007),
    /// Slots 18-23 haben kleine Werte (= Position-Indizes in einer anderen
    /// Tabelle).</summary>
    public static readonly IReadOnlyList<ushort> InitialScrbId = new ushort[]
    {
        0x2328, 0x2328, 0x2328, 0x2329, 0x2329, 0x2329, 0x2329, 0x2329,
        0x2329, 0x2328, 0x2328, 0x2328, 0x232B, 0x232B, 0x232D, 0x232E,
        0x232F, 0x0000, 0x005B, 0x0127, 0x008A, 0x0126, 0x00B2, 0x0124,
    };

    /// <summary>True wenn Slot eine secondary Object-Verbindung hat (= Triple statt Pair).</summary>
    public static bool SlotHasSecondary(int slotId) =>
        slotId >= 0 && slotId < SlotCount && HasSecondaryFlag[slotId] != 0;
}

/// <summary>
/// Verkettung eines Action-Slots mit den Engine-Objects, die ihn implementieren.
/// Pro Slot 0..23 baut die Engine via <c>FUN_004265F0</c> ein Triple aus
/// (Primary, Secondary, Tertiary)-Objects, das die Routing-Logik trägt.
///
/// <para>Live-Daten:</para>
/// <list type="bullet">
///   <item><see cref="PrimaryHandle"/> aus <c>[0x49ABB8 + slot*2]</c></item>
///   <item><see cref="SecondaryHandle"/> aus <c>[0x49ABF0 + slot]</c>
///       (nur wenn <see cref="ActionSlotTables.HasSecondaryFlag"/> != 0)</item>
///   <item><see cref="TertiaryHandle"/> aus dem primary-Object bei Offset +0x25</item>
/// </list>
/// </summary>
public sealed record MechanismConnection(
    int SlotId,
    ushort PrimaryHandle,
    ushort? SecondaryHandle,
    ushort? TertiaryHandle,
    ushort InitialScrbId)
{
    /// <summary>Anzahl Engine-Objects in diesem Triple (1, 2 oder 3).</summary>
    public int LinkedObjectCount =>
        1 + (SecondaryHandle.HasValue ? 1 : 0) + (TertiaryHandle.HasValue ? 1 : 0);

    /// <summary>True wenn dieser Slot eine echte Triple-Verkettung hat (alle 3 Objects).</summary>
    public bool IsTriple => SecondaryHandle.HasValue && TertiaryHandle.HasValue;
}
