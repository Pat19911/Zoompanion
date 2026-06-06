using System.Globalization;
using System.Text.RegularExpressions;

namespace ZoombiniHelper.Diagnostics;

/// <summary>
/// IMemoryReader-Implementation die einen MemoryDumpFile-erstellten .txt-Dump
/// liest. Praktisch für Offline-Validation: lese Snapshots aus alten Dumps
/// und führe State-Klassen darüber aus.
///
/// <para>Nur die <c>=== Full .data section dump (non-zero words) ===</c>
/// Sektion wird geparst — Heap-Adressen sind nicht im Dump enthalten.
/// State-Klassen die Heap-Pointer dereferenzieren, kommen damit nicht zu
/// vollen Daten — aber alles in <c>.data</c> ist verifizierbar.</para>
/// </summary>
public sealed class DumpFileReader : IMemoryReader
{
    private readonly Dictionary<nint, ushort> _wordsByVa;

    private static readonly Regex LineRe = new(
        @"^\s*0x([0-9A-Fa-f]+)\s*=\s*-?\d+\s*\(0x([0-9A-Fa-f]+)\)",
        RegexOptions.Compiled);

    public DumpFileReader(string dumpPath)
    {
        _wordsByVa = new Dictionary<nint, ushort>();
        bool inSection = false;
        foreach (var line in File.ReadLines(dumpPath))
        {
            if (line.StartsWith("=== Full .data section dump"))
            {
                inSection = true;
                continue;
            }
            if (!inSection) continue;
            if (line.StartsWith("==="))
            {
                inSection = false;
                continue;
            }
            var m = LineRe.Match(line);
            if (m.Success)
            {
                var va = (nint)long.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
                var w = ushort.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
                _wordsByVa[va] = w;
            }
        }
    }

    public ushort ReadWord(nint va) => _wordsByVa.TryGetValue(va, out var v) ? v : (ushort)0;
    public byte ReadByte(nint va)
    {
        // Dump speichert nur word-aligned. Lese aligned-down word, nimm low/high byte.
        nint alignedVa = va & ~1;
        ushort w = ReadWord(alignedVa);
        return (byte)((va & 1) == 0 ? w & 0xff : (w >> 8) & 0xff);
    }
    public byte[]? ReadBytes(nint va, int n)
    {
        // Konstruiere n bytes aus den word-Einträgen (oder Nullen für fehlende).
        var bytes = new byte[n];
        for (int i = 0; i < n; i++) bytes[i] = ReadByte(va + i);
        return bytes;
    }
}
