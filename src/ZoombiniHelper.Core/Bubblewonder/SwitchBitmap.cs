namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Live Switch-Aktivations-Bitmap aus dem ZB-Aggregator.
///
/// <para>Quelle: <c>FUN_0042B8C0</c> in v2 PE32. Bei jedem Switch-Action-Event
/// (DAT_0049B0E8 in {9, 0xc, 0xf, 0x12}) wird ein Bit in
/// <c>*[0x4A2818] + 0x52..0x54</c> gesetzt:</para>
/// <code>
/// switch(DAT_0049b0e8) {
/// case 9:    if (DAT_0049b0e4 == 4)  *(0x4A2818 + 0x52) |= bit;       // Channel A
/// case 0xc:  if (DAT_0049b0e4 == 5)  *(0x4A2818 + 0x54) |= bit;       // Channel C-low
/// case 0xf:  if (DAT_0049b0e4 == 5)  *(0x4A2818 + 0x54) |= bit&lt;&lt;4;  // Channel C-high
/// case 0x12: if (DAT_0049b0e4 == 6)  *(0x4A2818 + 0x53) |= bit;       // Channel B
/// }
/// </code>
///
/// <para>Insgesamt 24 Bits = bis zu 24 verschiedene Switches im Grid.</para>
/// </summary>
public sealed record SwitchBitmap(byte ChannelA, byte ChannelB, ushort ChannelC)
{
    /// <summary>Bit N in Channel A (Action-Type 9). 0..7.</summary>
    public bool ChannelABit(int n) => n is >= 0 and < 8 && (ChannelA & (1 << n)) != 0;

    /// <summary>Bit N in Channel B (Action-Type 0x12). 0..7.</summary>
    public bool ChannelBBit(int n) => n is >= 0 and < 8 && (ChannelB & (1 << n)) != 0;

    /// <summary>Bit N in Channel C (Action-Type 0xC + 0xF). 0..15.
    /// 0..7 = Type 0xC, 8..15 = Type 0xF (geshiftet via &lt;&lt;4 + bytewise overflow).</summary>
    public bool ChannelCBit(int n) => n is >= 0 and < 16 && (ChannelC & (1 << n)) != 0;

    /// <summary>Anzahl gesetzter Bits über alle drei Channels (= aktivierte Switches).</summary>
    public int TotalActivatedSwitches =>
        System.Numerics.BitOperations.PopCount((uint)ChannelA) +
        System.Numerics.BitOperations.PopCount((uint)ChannelB) +
        System.Numerics.BitOperations.PopCount((uint)ChannelC);

    /// <summary>Liest die Switch-Bitmap live aus dem Aggregator.
    /// Returns null wenn der Aggregator-Pointer nicht initialisiert ist.</summary>
    public static SwitchBitmap? Read(IMemoryReader mem)
    {
        var ptrBytes = mem.ReadBytes(BubblewonderMemoryMap.AggregatorPointer, 4);
        if (ptrBytes is null || ptrBytes.Length < 4) return null;
        nint heapVa = (nint)BitConverter.ToUInt32(ptrBytes, 0);
        if (heapVa == 0) return null;

        // 4 bytes ab +0x52: 1 byte ChA, 1 byte ChB, 2 bytes ChC
        var bytes = mem.ReadBytes(heapVa + BubblewonderMemoryMap.AggregatorSwitchBitmapAOffset, 4);
        if (bytes is null || bytes.Length < 4) return null;
        return new SwitchBitmap(
            ChannelA: bytes[0],
            ChannelB: bytes[1],
            ChannelC: BitConverter.ToUInt16(bytes, 2));
    }

    public override string ToString() =>
        $"Switches: A=0x{ChannelA:X2} B=0x{ChannelB:X2} C=0x{ChannelC:X4} " +
        $"({TotalActivatedSwitches} aktiviert)";
}
