namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Bekannte Memory-Adressen für Bubblewonder Abyss in v2 PE32.
/// Alle Konstanten sind via Live-Dump-Korrelation oder direktem Code-Pattern verifiziert.
///
/// <para>Spawn-Cell-Mappings pro REGS-ID liegen in <see cref="BubblewonderSpawnMappings"/>
/// (out-of-the-box, kein Live-Lernen nötig).</para>
/// </summary>
public static class BubblewonderMemoryMap
{
    // ------------------- Difficulty / Init State -------------------

    public const nint UserDifficulty = 0x00499B1C;
    public const nint Difficulty1Based = 0x0049A39C;
    public const nint Difficulty0Based = 0x0049A39A;
    public const nint DifficultyCache = 0x0049A3E2;

    /// <summary>Per-Difficulty Variation-Counter — entscheidet welche
    /// REGS-Resource-Variant geladen wird.</summary>
    public const nint VariationCounterDiff1 = 0x0049B098;
    public const nint VariationCounterDiff2 = 0x0049B09A;
    public const nint VariationCounterDiff3 = 0x0049B09C;
    public const nint VariationCounterDiff4 = 0x0049B09E;

    // ------------------- Resource / Slot Pointer -------------------

    /// <summary>Heap-Pointer auf die geladene REGS-Resource (Bubble-Layout-Daten).</summary>
    public const nint RegsHeapPointer = 0x0049ABA8;
    public const nint ResourceHandleSlot = 0x0049A5D8;

    /// <summary>Slot-Array des Generators (Stride 2 Bytes pro Slot, 10 Werte pro Bubble).</summary>
    public const nint SlotArray = 0x0049A274;
    public const nint SlotCounter = 0x0049A2CE;

    // ------------------- Action-Slot-Verkettung -------------------

    public const nint ActionSlotHandlesPrimary = 0x0049ABB8;
    public const nint ActionSlotHandlesSecondary = 0x0049ABF0;
    public const nint ActionTargetMapLeft = 0x0049A416;
    public const nint ActionTargetMapRight = 0x0049A5D6;
    public const nint ActionTargetHandles = 0x0049A820;
    public const nint SlotActiveFlags = 0x0049AB54;

    // ------------------- Pair-/Position-State (Tick-Callback) -------------------

    /// <summary>Counter-Tabelle pro Grid-Position, Index = (prop1*13 + prop2)*6.</summary>
    public const nint PositionCounterTable = 0x0049ACA0;
    public const nint PositionHandleTable = 0x0049ACA2;

    /// <summary>Engine-eigene Zelltyp-Tabelle: 1 Word pro Grid-Zelle,
    /// Index = row*13 + col (156 Zellen, 12×13). Werte: 0=leer, 1-6=REGS-Mechanik,
    /// 0x14=Start (unten-links), 0x15/0x16=Zwischenstation, 0x17=Ziel (oben-rechts).
    /// <para><b>Verifiziert (Disasm + Live):</b> Reader PROCESS 0x425a00 liest pro Tick
    /// den Typ aus <c>(&amp;table)[(row*13+col)*2]</c> und dispatcht; Handler fn 0x425f30
    /// setzt ZB[+0x76] := celltype − 0x14 (0x17 → 3 → Win-Flag 0x49a55c). 100 % Match
    /// gegen die sichtbaren REGS-Mechanismen über 62 Dumps / 8 Layouts / Diff 1-4;
    /// Ziel immer (10,0)(10,1)(10,2)(11,2).</para></summary>
    public const nint CellTypeTable = 0x00499EFC;

    /// <summary>POINTER (DWORD) auf die engine-eigene Cell→Pixel-Tabelle. Disassembly-
    /// verifiziert (v2): fn 0x4249FD alloziert 0x3e80 Bytes und schreibt den Pointer
    /// hierhin; fn 0x42a7a0 liest <c>(*CellPixelTablePtr)[(row*13+col)*4]</c> = (x:int16,
    /// y:int16) = Bildschirm-Pixel der Zelle. Damit lässt sich ein Maschinen-Pixel
    /// EXAKT (perspektivisch korrekt) der Grid-Zelle zuordnen — anders als die lineare
    /// Näherung, die bei der isometrischen Darstellung scheitert.</summary>
    public const nint CellPixelTablePtr = 0x0049A20C;

    public const nint MatchedHandlesList = 0x0049A83C;
    public const nint MatchedHandlesCount = 0x0049AC76;
    public const nint ScoredHandlesList = 0x0049A3A4;
    public const nint ScoredHandlesCount = 0x0049A206;
    public const nint PoppedHandlesList = 0x0049A448;
    public const nint PoppedHandlesCount = 0x0049A414;

    // ------------------- Animation-Delta-Tabellen -------------------

    public const nint XDeltaTablePtr = 0x0049A828;
    public const nint YDeltaTablePtr = 0x0049A830;
    public const nint MovementXDeltaPtr = 0x0049A870;
    public const nint MovementYDeltaPtr = 0x0049A86C;

    // ------------------- Bubble Engine-Object Layout -------------------

    /// <summary>+0x72 = REGS f3 nach Copy bei +0x6C. Wird als prop1 für Position-Index genutzt.</summary>
    public const int BubbleProp1Offset = 0x72;

    /// <summary>+0x74 = REGS f4 nach Copy. Conditional one-hot bit / prop2.</summary>
    public const int BubbleProp2Offset = 0x74;

    /// <summary>+0xC8 = state (3 = ready für Match).</summary>
    public const int BubbleStateOffset = 0xC8;

    public const int BubbleSlotIdxOffset = 0x88;
    public const int BubbleTargetIdxOffset = 0x8A;
    public const int BubbleLinkedHandleOffset = 0x94;

    /// <summary>+0x6C..+0x7F = REGS-Record-Copy (10 LE-words) — die Roh-REGS-Daten der Cell.</summary>
    public const int BubbleRegsCopyStart = 0x6C;

    public const int BubbleActiveFlagOffset = 0xE2;
    public const int BubbleCountdownOffset = 0xF8;

    /// <summary>+0x82 (word) = Conditional-Attribut-Code: 1=Hair, 2=Eyes, 3=Nose, 4=Feet.
    /// Live-verifiziert über alle 4 Attribute: Sonnenbrille=Eyes/2, Spitze Haare=Hair/1,
    /// Blaue Nase=Nose/3, Schuhe+Räder=Feet/4. Gilt nur für Conditional-Cells (REGS-f0=2).</summary>
    public const int BubbleConditionalAttrOffset = 0x82;

    /// <summary>+0x84 (word) = Conditional-Variante (1..5). Bedeutung pro Variant identisch
    /// zur ZB-Attribut-Codierung (siehe ZoombiniVariants.VariantName).</summary>
    public const int BubbleConditionalVariantOffset = 0x84;

    /// <summary>+0x12C = sekundäres Active-Flag.
    /// FUN_0044A920 filtert auf <c>obj[+0xE2] != 0 &amp;&amp; obj[+0x12C] != 0</c>.</summary>
    public const int BubbleSecondaryActiveOffset = 0x12C;

    /// <summary>+0xF0..+0xF3 = 4-Byte Filter-Configuration für aktive Match-Bubbles.
    /// HINWEIS: Im idle-State uninitialisierter Heap-Müll. Conditional-Cells haben
    /// ihre Variant in +0x82/+0x84.</summary>
    public const int BubbleFilterConfigOffset = 0xF0;

    /// <summary>+0xE0 = aktueller Animation-event-type (word).
    /// 0x14/0x1E/0x28/0x32 = 4 Pair-Match-Filter-Channels;
    /// 0x15/0x1F/0x29/0x33/0x3D = Passthrough/Score; sonst = idle.</summary>
    public const int BubbleEventTypeOffset = 0xE0;

    // ------------------- Trigger → Switch Verknüpfung -------------------

    /// <summary>+0x166 in Trigger-Bubble (REGS-f0=6) = hdr1A des Target-Switches.
    /// Statisch lesbar — Trigger-Hit setzt diesen Switch um.</summary>
    public const int TriggerTargetHandleOffset = 0x166;

    // ------------------- Switch Live-State -------------------

    /// <summary>+0x7C in Switch-Bubble (REGS-f0=4) = Direct-Direction-Bit-Index.
    /// Werte: 0=Up, 1=Right, 2=Down, 3=Left. Definiert welche der REGS f4..f7
    /// Direction-Bits aktuell aktiv ist.</summary>
    public const int SwitchStateOffset = 0x7C;

    // ------------------- Sticky-Cell (f0=5) -------------------

    /// <summary>+0x86 in Sticky-Bubble = hdr1A des aktuell klebenden ZBs.
    /// Wert 0 = leer, sonst hdr1A des gefangenen ZBs. Beim Schubsen überschrieben,
    /// bei Pair-Befreiung auf 0 zurückgesetzt.</summary>
    public const int StickyTrappedZbOffset = 0x86;

    // ------------------- Aggregator (Save-Game-Buffer) -------------------

    public const nint AggregatorPointer = 0x004A2818;

    /// <summary>Switch-Bitmap-Offsets im Aggregator (Channel A/B/C).</summary>
    public const int AggregatorSwitchBitmapAOffset = 0x52;
    public const int AggregatorSwitchBitmapBOffset = 0x53;
    public const int AggregatorSwitchBitmapCOffset = 0x54;

    /// <summary>ZB-Pro-ZB-Tabelle im Aggregator: +0xB83C + i*0x14 (20 Bytes pro ZB).</summary>
    public const int AggregatorZbTableOffset = 0xB83C;
    public const int AggregatorZbStride = 0x14;
}

/// <summary>
/// Mapping von Bubblewonder-Difficulty (1..4) auf Mohawk-Resource-IDs.
/// Resource-Loader FUN_004273F0 wählt per Switch:
///   Diff 1 → 16600/16601
///   Diff 2 → 16602/16603
///   Diff 3 → 16604/16605
///   Diff 4 → 16606/16607/16608
///   Diff 5 (Bonus) → 16609
/// </summary>
public static class BubblewonderRegsResources
{
    public static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> ByDifficulty =
        new Dictionary<int, IReadOnlyList<int>>
        {
            [1] = new[] { 16600, 16601 },
            [2] = new[] { 16602, 16603 },
            [3] = new[] { 16604, 16605 },
            [4] = new[] { 16606, 16607, 16608 },
            [5] = new[] { 16609 },
        };

    public static int Resolve(int difficulty, int variant)
    {
        if (!ByDifficulty.TryGetValue(difficulty, out var variants))
            throw new ArgumentOutOfRangeException(nameof(difficulty), $"Unknown difficulty {difficulty}");
        if (variant < 0 || variant >= variants.Count)
            throw new ArgumentOutOfRangeException(nameof(variant),
                $"Variant {variant} out of range for difficulty {difficulty} (valid: 0..{variants.Count - 1})");
        return variants[variant];
    }
}
