namespace ZoombiniHelper;

/// <summary>
/// Minimal abstraction over a process memory reader. Exists so the Core
/// domain library has no Win32 dependency — implementations live in the
/// app project (Pe32GameState) and in test fakes.
/// </summary>
public interface IMemoryReader
{
    ushort ReadWord(nint staticVa);
    byte ReadByte(nint staticVa);
    byte[]? ReadBytes(nint staticVa, int count);

    /// <summary>The OS process the reader is attached to. Used by renderers
    /// that need to find the game window via Win32 (e.g. for the live
    /// minimap). Returns 0 for in-process or test fakes.</summary>
    int AttachedProcessId => 0;
}
