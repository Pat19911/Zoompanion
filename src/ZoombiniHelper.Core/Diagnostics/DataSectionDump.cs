namespace ZoombiniHelper.Diagnostics;

/// <summary>
/// Dump every non-zero word in the .data section. Used to find a new puzzle's
/// state region when the assumed VAs hold zero on the active puzzle.
/// </summary>
public static class DataSectionDump
{
    private const nint Start = 0x0048B000;
    private const nint End   = 0x004A6E38;
    private const int  Chunk = 0x1000;

    public static string Run(IMemoryReader mem)
    {
        var lines = new List<string> { "", "=== Full .data section dump (non-zero words) ===", "" };
        int hits = 0;
        for (nint p = Start; p < End; p += Chunk)
        {
            int len = (int)Math.Min(Chunk, End - p);
            var raw = mem.ReadBytes(p, len);
            if (raw is null) continue;
            for (int i = 0; i + 1 < raw.Length; i += 2)
            {
                ushort w = BitConverter.ToUInt16(raw, i);
                if (w != 0)
                {
                    lines.Add($"  0x{(p + i):X8} = {w,5}  (0x{w:X4})");
                    hits++;
                }
            }
        }
        lines.Insert(2, $"  {hits} non-zero words.");
        return string.Join("\n", lines);
    }
}
