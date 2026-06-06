namespace ZoombiniHelper.Win32;

/// <summary>
/// High-level read API on top of <see cref="Pe32ProcessMemory"/>. All addresses
/// passed in are static VAs from the binary analysis (assuming ImageBase
/// 0x00400000); ASLR is handled by the underlying memory reader.
/// </summary>
public sealed class Pe32GameState : IMemoryReader
{
    private readonly Pe32ProcessMemory _mem;

    public Pe32GameState(Pe32ProcessMemory mem) => _mem = mem;

    public nint ModuleBase => _mem.ModuleBase;
    public nint AslrSlide => _mem.AslrSlide;

    /// <summary>16-bit word; returns 0 on read failure (use sparingly — failures
    /// are silently indistinguishable from a real zero).</summary>
    public ushort ReadWord(nint staticVa) => _mem.ReadWord(staticVa) ?? 0;

    /// <summary>8-bit byte; returns 0 on read failure.</summary>
    public byte ReadByte(nint staticVa) => _mem.ReadByteAt(staticVa) ?? 0;

    public byte[]? ReadBytes(nint staticVa, int count) => _mem.ReadBytes(staticVa, count);

    /// <summary>Expose the attached process id so renderers can find the
    /// game window (used for the Stone Rise live-screenshot minimap).</summary>
    public int AttachedProcessId => _mem.AttachedProcessId;

    public ushort[] ReadWordArray(nint staticVa, int count)
    {
        var raw = ReadBytes(staticVa, count * 2);
        if (raw is null) return [];
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
            result[i] = BitConverter.ToUInt16(raw, i * 2);
        return result;
    }
}
