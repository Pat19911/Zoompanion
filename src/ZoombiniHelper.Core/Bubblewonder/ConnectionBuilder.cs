namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Baut die Verkettungs-Topologie eines Bubblewonder-Layouts aus Live-Memory.
///
/// <para>Logik gespiegelt aus <c>FUN_004265F0</c> (Action-Slot-Setup):</para>
/// <code>
/// for slot in 0..23:
///   primary = [0x49ABB8 + slot*2]   // word
///   secondary = (HasSecondaryFlag[slot] != 0) ? [0x49ABF0 + slot] : null
///   tertiary = lookup_engine_object(primary).field_at_0x25
/// </code>
///
/// <para>Diese Klasse löst die Handles aus Memory auf, baut aber den
/// Tertiary-Lookup absichtlich NICHT auf — der setzt voraus dass wir den
/// Engine-Object-Heap walken (was Stone Rise / Cajun via separate Resolver
/// machen). Tertiary-Resolution geschieht in einer höheren Schicht
/// (Live-State-Reader, Task 5).</para>
/// </summary>
public static class ConnectionBuilder
{
    /// <summary>Baut alle 24 Action-Slot-Verkettungen aus Memory. Tertiary
    /// bleibt null hier — wird im Live-State-Reader nachgereicht.</summary>
    public static IReadOnlyList<MechanismConnection> BuildFromMemory(IMemoryReader mem)
    {
        var primaryBytes = mem.ReadBytes(BubblewonderMemoryMap.ActionSlotHandlesPrimary,
                                          ActionSlotTables.SlotCount * 2);
        var secondaryBytes = mem.ReadBytes(BubblewonderMemoryMap.ActionSlotHandlesSecondary,
                                            ActionSlotTables.SlotCount);

        var connections = new List<MechanismConnection>(ActionSlotTables.SlotCount);
        for (int slot = 0; slot < ActionSlotTables.SlotCount; slot++)
        {
            ushort primary = primaryBytes is not null
                ? BitConverter.ToUInt16(primaryBytes, slot * 2)
                : (ushort)0;
            ushort? secondary = ActionSlotTables.SlotHasSecondary(slot) && secondaryBytes is not null
                ? secondaryBytes[slot]
                : (ushort?)null;
            connections.Add(new MechanismConnection(
                SlotId: slot,
                PrimaryHandle: primary,
                SecondaryHandle: secondary,
                TertiaryHandle: null,
                InitialScrbId: ActionSlotTables.InitialScrbId[slot]));
        }
        return connections;
    }
}
