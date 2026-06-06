using System.Buffers.Binary;

namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Live-Snapshot eines Bubble-Engine-Objects aus der globalen linked-list.
///
/// <para>Ein "Bubble" ist ein Mechanismus auf dem Grid (Schalter, Conditional,
/// Toggle, etc.) — physisch repräsentiert durch einen Engine-Object-Knoten.
/// Identifiziert per <c>handle == 0x04188000</c>. Pro Diff/Variant gibt es
/// N Bubbles (REGS-Header.Count = 19 für Diff 1 v1, 38 für Diff 4 v1, etc.).</para>
///
/// <para>Layout-Notes (aus Live-Korrelation memdump-233815):</para>
/// <list type="bullet">
///   <item>Engine-Header bei Node-Offset 0x00..0x2F</item>
///   <item>Payload startet bei Node-Offset 0x30</item>
///   <item>Bubble-rec_attrs bei +0x60..+0x63 (= node+0x90, im Dump als
///       "(176, 139, 21, 112)") — Bedeutung noch unklar, sieht nach
///       Sprite-Identifier aus</item>
///   <item>Property-Felder bei <see cref="BubblewonderMemoryMap.BubbleProp1Offset"/>
///       (+0x72) und <see cref="BubblewonderMemoryMap.BubbleProp2Offset"/> (+0x74)</item>
///   <item>REGS-Record-Copy bei +0x78..+0x8C (10 BE-words wie im REGS-File)</item>
///   <item>State bei +0xC8 (3 = ready, andere TBD)</item>
/// </list>
/// </summary>
public sealed record BubbleObject(
    nint NodeAddress,
    ushort HeaderId,        // = hdr1A im Dump
    uint Handle,            // = 0x04188000 für Bubbles
    /// <summary>prop1 aus Bubble-Object (+0x72) — Engine-berechnete Grid-Koordinate.</summary>
    ushort Prop1,
    /// <summary>prop2 aus Bubble-Object (+0x74) — Engine-berechnete Grid-Koordinate.</summary>
    ushort Prop2,
    /// <summary>State-Wert (+0xC8). 3 = ready für Match, andere TBD.</summary>
    ushort State,
    /// <summary>REGS-Record-Copy aus dem Bubble-Object (+0x6C..+0x7F).
    /// 10 LE-words.</summary>
    IReadOnlyList<ushort> RegsRecordCopy,
    /// <summary>+0xE2 = primary active flag (boolean 0/1).</summary>
    byte ActiveFlag,
    /// <summary>+0x12C = secondary active flag (boolean 0/1).
    /// FUN_0044A920 filtert auf <c>+0xE2 != 0 &amp;&amp; +0x12C != 0</c>.</summary>
    byte SecondaryActiveFlag,
    /// <summary>+0xF8 = countdown byte (Tick-Trigger).</summary>
    byte Countdown,
    /// <summary>+0x94 = linked engine-object handle.</summary>
    ushort LinkedHandle,
    /// <summary>+0xE0 = aktueller Animation-event-type (word). Steuert Tick-CB-switch.
    /// 0x14/0x1E/0x28/0x32 = 4 Pair-Match-Channels;
    /// 0x15/0x1F/0x29/0x33/0x3D = Passthrough/Score; sonst = idle.</summary>
    ushort EventType,
    /// <summary>+0xF0..+0xF3 = Live-Filter-Configuration (4 bytes).
    /// Layout: (hairFilter, eyesFilter, noseFilter, feetFilter), je 0=irrelevant
    /// oder 1..5=Variant. Quelle: FUN_0044A920.</summary>
    FilterConfig FilterConfig,
    /// <summary>+0x82 (word) = Conditional-Attribut-Code (live).
    /// Engine-Mapping: 01=Nase, 02=Augen?, 03=Haar, 04=Füße?
    /// Gilt nur wenn die Cell ein Conditional-Filter ist (REGS-f0=2).</summary>
    ushort ConditionalAttrCode = 0,
    /// <summary>+0x84 (word) = Conditional-Variante (1-5, live).
    /// Mapping: gleiches wie ZB-Attribute (1=Spitze Haare/Grüne Nase, ...).</summary>
    ushort ConditionalVariant = 0,
    /// <summary>+0x7C = Switch-State-Index (0=Up, 1=Right, 2=Down, 3=Left).
    /// Nur sinnvoll wenn REGS-f0 == 4 (Switch). Sonst Heap-Müll.</summary>
    byte SwitchStateIndex = 0,
    /// <summary>+0x86 = hdr1A des aktuell klebenden ZBs in Sticky-Cell (REGS-f0=5).
    /// 0 = leer, sonst Handle. Nur sinnvoll für Sticky-Cells.</summary>
    ushort StickyTrappedZb = 0,
    /// <summary>+0x166 = hdr1A des Target-Switches in Trigger-Cell (REGS-f0=6).
    /// Statisch lesbar — kein Trigger-Hit nötig. 0 wenn nicht in den gelesenen Bytes.</summary>
    ushort TriggerTargetHandle = 0,
    /// <summary>Roh-Bytes des Engine-Nodes — für Spezial-Scans (z.B. Hex-Dump,
    /// Word-Search). Länge variiert mit dem tatsächlichen Read.</summary>
    byte[]? RawBytes = null)
{
    /// <summary>Versucht den REGS-Record-Copy als RegsRecord zu interpretieren.</summary>
    public RegsRecord AsRegsRecord() => RegsRecordCopy.Count >= 10
        ? new RegsRecord(RegsRecordCopy[0], RegsRecordCopy[1], RegsRecordCopy[2],
                          RegsRecordCopy[3], RegsRecordCopy[4], RegsRecordCopy[5],
                          RegsRecordCopy[6], RegsRecordCopy[7], RegsRecordCopy[8],
                          RegsRecordCopy[9])
        : default;

    public bool IsReadyForMatch => State == 3;
    public bool IsActive => ActiveFlag != 0;

    /// <summary>True wenn der Bubble FUN_0044A920's Filter erfüllt
    /// (= Conditional-Filter-Cell mit aktivem State).</summary>
    public bool IsActiveConditionalFilter => ActiveFlag != 0 && SecondaryActiveFlag != 0;

    /// <summary>Aktive Filter-Channel-Familie aus EventType.</summary>
    public BubbleEventFamily EventFamily => EventType switch
    {
        0x14 or 0x1E or 0x28 or 0x32 => BubbleEventFamily.PairMatchFilter,
        0x15 or 0x1F or 0x29 or 0x33 or 0x3D => BubbleEventFamily.Passthrough,
        _ => BubbleEventFamily.Idle,
    };

    /// <summary>Channel-Index (0..3) wenn EventFamily == PairMatchFilter, sonst -1.
    /// **Probable**-Mapping: 0=Hair, 1=Eyes, 2=Nose, 3=Feet (nicht final live verifiziert).</summary>
    public int FilterChannelIndex => EventType switch
    {
        0x14 => 0,
        0x1E => 1,
        0x28 => 2,
        0x32 => 3,
        _ => -1,
    };

    public override string ToString() =>
        $"Bubble hdr1A=0x{HeaderId:X4} pos=({Prop1},{Prop2}) state={State} " +
        $"event=0x{EventType:X2} flags=({ActiveFlag},{SecondaryActiveFlag}) " +
        $"filter={FilterConfig}";
}

/// <summary>Familie des aktuellen Bubble-event-types (aus +0xE0).</summary>
public enum BubbleEventFamily
{
    /// <summary>Nicht im Pair-Match- oder Passthrough-Modus.</summary>
    Idle,
    /// <summary>0x14/0x1E/0x28/0x32 — Pair-Match-Channel aktiv.</summary>
    PairMatchFilter,
    /// <summary>0x15/0x1F/0x29/0x33/0x3D — Score/Passthrough.</summary>
    Passthrough,
}

/// <summary>4-Byte Filter-Configuration aus <c>obj[+0xF0..+0xF3]</c>.
/// Pro Attribut: 0 = irrelevant, 1..5 = verlangter Variant.
/// ZB öffnet Filter ⇔ alle Bytes mit Wert &gt; 0 stimmen mit ZB-Attribut überein.</summary>
public readonly record struct FilterConfig(byte Hair, byte Eyes, byte Nose, byte Feet)
{
    /// <summary>True wenn alle 4 Bytes 0 sind (= kein Filter).</summary>
    public bool IsEmpty => Hair == 0 && Eyes == 0 && Nose == 0 && Feet == 0;

    /// <summary>Anzahl der aktiven Filter-Bytes (0..4).</summary>
    public int ActiveAttributeCount =>
        (Hair > 0 ? 1 : 0) + (Eyes > 0 ? 1 : 0) +
        (Nose > 0 ? 1 : 0) + (Feet > 0 ? 1 : 0);

    public override string ToString() =>
        $"({Hair},{Eyes},{Nose},{Feet})";
}

/// <summary>
/// Walker der Live-Bubbles aus der Engine-linked-list extrahiert.
///
/// <para>Filter-Pattern: <c>handle == 0x04188000</c> identifiziert die
/// Bubble-Objects. Andere handles sind Map/Go-Buttons (0x04008000, 0x04988000),
/// UI-Elements (0x0508A000) oder ZBs (0x00000001).</para>
///
/// <para>Pro Bubble lesen wir 256 Bytes aus dem Engine-Node und extrahieren
/// die relevanten Felder. Bytes sind Engine-internal (LE in x86), nur die
/// REGS-Record-Copy bei +0x78 ist BE wie im Mohawk-Original.</para>
/// </summary>
public static class BubbleObjectScanner
{
    /// <summary>Filter-Handle für Bubble-Objects (= Mechanismen am Grid).</summary>
    public const uint BubbleHandle = 0x04188000;

    /// <summary>Bytes pro Engine-Node die wir lesen müssen.
    /// Erweitert auf 0x200 für Pfeil-Direction-Hunt (2026-05-01) — wir suchen
    /// das Byte/Word das pro Bubble unterschiedlich ist und die Routing-Richtung
    /// der "leeren" Pfeil-Bubbles kodiert.</summary>
    /// <summary>Bytes pro Engine-Node die wir lesen wollen.
    /// Hinweis: tatsächliche Memory-Reads können WENIGER Bytes zurückgeben
    /// (z.B. an Page-Boundaries). Filter darf nicht strikt sein, sonst
    /// fehlen Bubbles im Live-Snapshot.</summary>
    public const int BytesPerNode = 0x200;
    public const int MinBytesPerNode = 0x100;  // Mindestbedarf für unsere Felder

    /// <summary>Walke linked-list und extrahiere alle Bubble-Objects.</summary>
    public static IReadOnlyList<BubbleObject> Scan(IMemoryReader mem)
    {
        var bubbles = new List<BubbleObject>();
        foreach (var node in EngineObjectList.Walk(mem, BytesPerNode))
        {
            if (node.Handle != BubbleHandle) continue;
            if (node.Bytes.Length < MinBytesPerNode) continue;

            byte[] b = node.Bytes;
            ushort hdr1A = BitConverter.ToUInt16(b, 0x1A);
            ushort prop1 = BitConverter.ToUInt16(b, BubblewonderMemoryMap.BubbleProp1Offset);
            ushort prop2 = BitConverter.ToUInt16(b, BubblewonderMemoryMap.BubbleProp2Offset);
            ushort state = BitConverter.ToUInt16(b, BubblewonderMemoryMap.BubbleStateOffset);
            byte activeFlag = b[BubblewonderMemoryMap.BubbleActiveFlagOffset];
            byte secondaryActive = b[BubblewonderMemoryMap.BubbleSecondaryActiveOffset];
            byte countdown = b[BubblewonderMemoryMap.BubbleCountdownOffset];
            ushort linkedHandle = BitConverter.ToUInt16(b, BubblewonderMemoryMap.BubbleLinkedHandleOffset);
            ushort eventType = BitConverter.ToUInt16(b, BubblewonderMemoryMap.BubbleEventTypeOffset);
            int fcOff = BubblewonderMemoryMap.BubbleFilterConfigOffset;
            var filterConfig = new FilterConfig(b[fcOff], b[fcOff + 1], b[fcOff + 2], b[fcOff + 3]);
            ushort condAttr = BitConverter.ToUInt16(b, BubblewonderMemoryMap.BubbleConditionalAttrOffset);
            ushort condVariant = BitConverter.ToUInt16(b, BubblewonderMemoryMap.BubbleConditionalVariantOffset);
            byte switchState = b[BubblewonderMemoryMap.SwitchStateOffset];
            ushort stickyTrapped = BitConverter.ToUInt16(b, BubblewonderMemoryMap.StickyTrappedZbOffset);
            ushort triggerTarget = b.Length > BubblewonderMemoryMap.TriggerTargetHandleOffset + 1
                ? BitConverter.ToUInt16(b, BubblewonderMemoryMap.TriggerTargetHandleOffset)
                : (ushort)0;

            // REGS-Record-Copy bei +0x6C (Little-Endian — Engine konvertiert beim Loading)
            var regs = new ushort[10];
            for (int i = 0; i < 10; i++)
            {
                int off = BubblewonderMemoryMap.BubbleRegsCopyStart + i * 2;
                regs[i] = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(off, 2));
            }

            bubbles.Add(new BubbleObject(
                NodeAddress: node.Address,
                HeaderId: hdr1A,
                Handle: node.Handle,
                Prop1: prop1, Prop2: prop2, State: state,
                RegsRecordCopy: regs,
                ActiveFlag: activeFlag,
                SecondaryActiveFlag: secondaryActive,
                Countdown: countdown,
                LinkedHandle: linkedHandle,
                EventType: eventType,
                FilterConfig: filterConfig,
                ConditionalAttrCode: condAttr,
                ConditionalVariant: condVariant,
                SwitchStateIndex: switchState,
                StickyTrappedZb: stickyTrapped,
                TriggerTargetHandle: triggerTarget,
                RawBytes: b));
        }
        return bubbles;
    }
}
