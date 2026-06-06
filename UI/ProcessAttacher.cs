using ZoombiniHelper.Win32;

namespace ZoombiniHelper.UI;

/// <summary>
/// Holds the connection to the running game process and its <see cref="Pe32GameState"/>
/// view. Re-attaches automatically if the game starts later or restarts.
///
/// Returns a status enum so the UI can decide what to display — keeps the
/// attach logic free of label/colour knowledge.
/// </summary>
public sealed class ProcessAttacher : IDisposable
{
    public enum Status { WaitingForGame, JustAttached, Attached, Lost }

    private readonly Pe32ProcessMemory _mem = new();
    private Pe32GameState? _state;

    public Pe32ProcessMemory Memory => _mem;
    public Pe32GameState? State => _state;

    /// <summary>Probe the current state and (re)attach if needed. Call every tick.</summary>
    public Status Tick()
    {
        if (_mem.IsAttached)
        {
            // Sanity probe: is the process still alive at all?
            if (_mem.ReadWord(0x00401000) != null) return Status.Attached;
            _mem.Dispose();
            _state = null;
            return Status.Lost;
        }
        if (!_mem.TryAttach()) return Status.WaitingForGame;
        _state = new Pe32GameState(_mem);
        return Status.JustAttached;
    }

    public void Dispose() => _mem.Dispose();
}
