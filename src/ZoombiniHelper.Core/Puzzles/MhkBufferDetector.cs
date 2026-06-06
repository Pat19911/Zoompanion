using ZoombiniHelper.Localization;

namespace ZoombiniHelper.Puzzles;

/// <summary>
/// Detects a puzzle by reading its dedicated MHK-filename-buffer word. Verified
/// pattern across all 17 puzzles + locations: each one's buffer holds a non-zero
/// word only while that puzzle/location is loaded; every other game state clears
/// it. One read per detector, no false positives.
/// </summary>
public sealed class MhkBufferDetector : IPuzzleDetector
{
    public PuzzleId Id { get; }
    public string MhkName { get; }

    /// <summary>Localization key for the overlay title (e.g. "puzzle.hotel").
    /// Resolved lazily so a runtime language switch shows immediately.</summary>
    private readonly string _displayNameKey;
    public string DisplayName => Loc.T(_displayNameKey);

    private readonly nint _bufferAddress;

    public MhkBufferDetector(PuzzleId id, string mhkName, string displayNameKey, nint bufferAddress)
    {
        Id = id;
        MhkName = mhkName;
        _displayNameKey = displayNameKey;
        _bufferAddress = bufferAddress;
    }

    public PuzzleDetection Detect(IMemoryReader mem)
    {
        ushort buffer = mem.ReadWord(_bufferAddress);
        bool active = buffer != 0;
        int conf = active ? 100 : 0;
        return new PuzzleDetection(active, conf, buffer, $"mhkBuffer=0x{buffer:X4}");
    }
}
