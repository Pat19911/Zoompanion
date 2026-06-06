using System.Buffers.Binary;

namespace ZoombiniHelper.Bubblewonder;

/// <summary>4 Cardinal-Directions für Routing-Pfeile auf Bubble-Cells.
/// Live-verifiziert via User-Beobachtung 2026-05-01.</summary>
public enum ArrowDirection : byte
{
    Up = 0,    // F4=1, visuell nach oben
    Right = 1, // F5=1, visuell nach rechts
    Down = 2,  // F6=1, vermutet (Symmetrie)
    Left = 3,  // F7=1, vermutet (Symmetrie)
}

/// <summary>Symbol-Pfeile für die UI-Anzeige.</summary>
public static class ArrowDirectionExtensions
{
    public static string AsArrow(this ArrowDirection d) => d switch
    {
        ArrowDirection.Up => "↑",
        ArrowDirection.Right => "→",
        ArrowDirection.Down => "↓",
        ArrowDirection.Left => "←",
        _ => "?",
    };
}

/// <summary>
/// Roher REGS-Record aus einer Mohawk-Resource. Format ist fest:
/// 20 Bytes = 10 Big-Endian 16-bit Words.
///
/// <para>Field-Bedeutungen (aus Cross-Reference REGS-Records ↔ disasm
/// FUN_00426E60 + FUN_004270F0):</para>
/// <list type="bullet">
///   <item><see cref="F0"/> (1..6, 100% nonzero): Mechanismus-Typ</item>
///   <item><see cref="F1"/> (1..11, 93% nonzero): Position/Property A</item>
///   <item><see cref="F2"/> (1..12, 98% nonzero): Position/Property B</item>
///   <item><see cref="F3"/> (immer 1, 93% nonzero): aktiv-flag</item>
///   <item><see cref="F4"/>..<see cref="F7"/> (one-hot, je ~26-31% nonzero):
///       <b>Pfeil-Direction der Cell</b>: F4=OBEN, F5=RECHTS, F6=UNTEN(?), F7=LINKS(?).
///       Live-verifiziert für F4 + F5 via Pfad-Beobachtung 2026-05-01 (User: 124,121=oben mit f4=1; 119=rechts mit f5=1).
///       F6/F7 noch nicht live-bestätigt (Symmetrie-Annahme).</item>
///   <item><see cref="F8"/> (0..3, 57% nonzero): redundanter Index der gesetzten
///       Position in F4-F7</item>
///   <item><see cref="F9"/> (0..1, nur 3% nonzero): Special-Bit</item>
/// </list>
/// </summary>
public readonly record struct RegsRecord(
    ushort F0, ushort F1, ushort F2, ushort F3, ushort F4,
    ushort F5, ushort F6, ushort F7, ushort F8, ushort F9)
{
    /// <summary>Größe eines Records in Bytes (= 10 BE-words).</summary>
    public const int SizeInBytes = 20;

    /// <summary>Pfeil-Richtung der Cell, abgeleitet aus F4-F7 (one-hot).
    /// Live-verifiziert 2026-05-01 via User-Beobachtung:
    ///   - F4=1 → OBEN (Mech [5], [6] beide blau)
    ///   - F5=1 → RECHTS (Mech [4] rot)
    ///   - F6=1 → UNTEN (Symmetrie-Annahme)
    ///   - F7=1 → LINKS (Symmetrie-Annahme)
    /// Null wenn keine Direction gesetzt (= Trap, Switch, Layout-Filler).</summary>
    public ArrowDirection? Direction => F4 != 0 ? ArrowDirection.Up
                                      : F5 != 0 ? ArrowDirection.Right
                                      : F6 != 0 ? ArrowDirection.Down
                                      : F7 != 0 ? ArrowDirection.Left
                                      : null;

    /// <summary>True wenn die Cell ein Conditional-Filter ist (= reagiert nur auf
    /// ZBs mit bestimmtem Attribut). Marker: F0=2.
    /// Live-verifiziert via User-Beobachtung 2026-05-01: ZB 0x0008 (5,4,1,4) lief
    /// über Mech [14] (2,8) f0=2 — User: "zweite funktional identische Conditional".
    /// f9=1 markiert vermutlich einen Special-Subtype (z.B. einzigartig im Layout).</summary>
    public bool IsConditional => F0 == 2;

    /// <summary>True wenn genau eines von F4..F7 gesetzt ist (= hat eindeutige Direction).</summary>
    public bool HasDirection => Direction.HasValue;

    /// <summary>Bei Switch-Cells: alle gesetzten Direction-Bits.
    /// Switch (Mech [17]) hat oft 2 Bits gesetzt = die zwei Schalt-Stellungen.
    /// Welche AKTUELL aktiv ist, kommt aus der Live-Switch-Bitmap (*[0x4A2818]+0x52..0x54).</summary>
    public IReadOnlyList<ArrowDirection> AllDirections
    {
        get
        {
            var dirs = new List<ArrowDirection>();
            if (F4 != 0) dirs.Add(ArrowDirection.Up);
            if (F5 != 0) dirs.Add(ArrowDirection.Right);
            if (F6 != 0) dirs.Add(ArrowDirection.Down);
            if (F7 != 0) dirs.Add(ArrowDirection.Left);
            return dirs;
        }
    }

    /// <summary>Welches ZB-Attribut prüft die Conditional-Cell? Bei IsConditional=true
    /// ist F8 vermutlich der Attribut-Index (1..4).
    /// Bei NICHT-conditional Cells ist F8 nur der redundante Direction-Index.</summary>
    public byte? ConditionalAttribute => IsConditional ? (byte)F8 : null;

    /// <summary>Liste der 10 Felder als ReadOnlyList — für Diagnose.</summary>
    public IReadOnlyList<ushort> AsList() => new[] { F0, F1, F2, F3, F4, F5, F6, F7, F8, F9 };

    /// <summary>Linearer Position-Index = F1 * 13 + F2 (für 12×13-Grid-Lookups).</summary>
    public int PositionIndex => F1 * 13 + F2;

    /// <summary>Parse einen REGS-Record aus Bytes (Big-Endian — wie Mohawk-File-Format).
    /// Für Live-Memory siehe <see cref="FromBytesLittleEndian"/>.</summary>
    public static RegsRecord FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < SizeInBytes)
            throw new ArgumentException($"Need {SizeInBytes} bytes, got {bytes.Length}", nameof(bytes));
        return new RegsRecord(
            BinaryPrimitives.ReadUInt16BigEndian(bytes[0..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[6..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[10..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[12..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[14..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[18..]));
    }

    /// <summary>Parse einen REGS-Record aus Bytes (Little-Endian — wie Live-Memory
    /// nach Engine-Loading). Bestätigt via Live-Dump 1. Mai 2026: Heap-bytes
    /// "2A 00 03 00 ..." → LE count=42 matched REGS 16608.</summary>
    public static RegsRecord FromBytesLittleEndian(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < SizeInBytes)
            throw new ArgumentException($"Need {SizeInBytes} bytes, got {bytes.Length}", nameof(bytes));
        return new RegsRecord(
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[0..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]));
    }
}

/// <summary>
/// Header einer REGS-Resource. 20 Bytes = 10 BE-words. Erste Word ist
/// die Anzahl Records die folgen. Andere Header-Felder sind teilweise
/// gesetzt (Diff 4 v1: header[1]=2, header[2]=4, header[3]=12) — Bedeutung
/// noch nicht eindeutig identifiziert.
/// </summary>
public readonly record struct RegsHeader(
    ushort Count, ushort H1, ushort H2, ushort H3, ushort H4,
    ushort H5, ushort H6, ushort H7, ushort H8, ushort H9)
{
    public const int SizeInBytes = 20;

    public static RegsHeader FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < SizeInBytes)
            throw new ArgumentException($"Need {SizeInBytes} bytes, got {bytes.Length}", nameof(bytes));
        return new RegsHeader(
            BinaryPrimitives.ReadUInt16BigEndian(bytes[0..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[6..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[10..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[12..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[14..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[18..]));
    }

    public static RegsHeader FromBytesLittleEndian(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < SizeInBytes)
            throw new ArgumentException($"Need {SizeInBytes} bytes, got {bytes.Length}", nameof(bytes));
        return new RegsHeader(
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[0..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]));
    }
}

/// <summary>
/// Parser für REGS-Resources. Format: 20-Byte-Header + N×20-Byte-Records.
/// Total-Size = 20 + Header.Count * 20.
///
/// <para>Loader-Logik die diese Records nutzt:</para>
/// <list type="bullet">
///   <item><c>FUN_004273F0</c> lädt eine REGS-Resource per Difficulty
///       und schreibt den Heap-Pointer nach <c>0x49ABA8</c></item>
///   <item><c>FUN_00427710</c> erzeugt Bubble-Engine-Objects und kopiert
///       Records ins Object bei Offset +0x78..+0x8C</item>
/// </list>
/// </summary>
public static class RegsReader
{
    /// <summary>Parse eine komplette REGS-Resource (Header + alle Records).
    /// Default = Big-Endian (Mohawk-File-Format). Für Live-Memory siehe
    /// <see cref="ParseLittleEndian"/>.</summary>
    public static (RegsHeader Header, IReadOnlyList<RegsRecord> Records) Parse(ReadOnlySpan<byte> data)
        => ParseInternal(data, littleEndian: false);

    /// <summary>Parse aus Live-Memory (LE — Engine konvertiert beim Loading).</summary>
    public static (RegsHeader Header, IReadOnlyList<RegsRecord> Records) ParseLittleEndian(ReadOnlySpan<byte> data)
        => ParseInternal(data, littleEndian: true);

    private static (RegsHeader Header, IReadOnlyList<RegsRecord> Records) ParseInternal(ReadOnlySpan<byte> data, bool littleEndian)
    {
        if (data.Length < RegsHeader.SizeInBytes)
            throw new ArgumentException($"REGS too small: {data.Length} < header size", nameof(data));

        var header = littleEndian
            ? RegsHeader.FromBytesLittleEndian(data)
            : RegsHeader.FromBytes(data);
        int expectedSize = RegsHeader.SizeInBytes + header.Count * RegsRecord.SizeInBytes;
        if (data.Length < expectedSize)
            throw new ArgumentException(
                $"REGS truncated: declared {header.Count} records require {expectedSize} bytes, got {data.Length}",
                nameof(data));

        var records = new RegsRecord[header.Count];
        for (int i = 0; i < header.Count; i++)
        {
            int offset = RegsHeader.SizeInBytes + i * RegsRecord.SizeInBytes;
            var slice = data[offset..(offset + RegsRecord.SizeInBytes)];
            records[i] = littleEndian
                ? RegsRecord.FromBytesLittleEndian(slice)
                : RegsRecord.FromBytes(slice);
        }
        return (header, records);
    }
}
