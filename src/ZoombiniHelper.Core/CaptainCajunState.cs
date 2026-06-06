namespace ZoombiniHelper;

/// <summary>
/// Snapshot of Captain Cajun's Ferryboat state.
///
/// <para>**Status: stage 1.** Reads difficulty + seat positions only.
/// Adjacency rules and per-seat zb-mapping not yet decoded — solver and
/// per-seat constraint enforcement come in a future iteration.</para>
/// </summary>
public sealed class CaptainCajunState
{
    /// <summary>The engine's per-seat side-tables (<c>0x4A37B4</c> +
    /// <c>0x4A32D0</c>) are populated during init with the seat's own
    /// hotspot/sprite id and a valid-flag — NOT with placement state.
    /// We expose them for diagnostics only; real occupancy comes from the
    /// per-zb backref at <c>+0xE0</c> instead. Resolved by the renderer.</summary>
    public readonly record struct Seat(int Index, int X, int Y, ushort RawZbIdField, byte RawOccupancyByte, ushort PlacedZbHeaderId);

    /// <summary>1-based difficulty as displayed in the game (1..4).</summary>
    public int Difficulty { get; }

    /// <summary>All seat positions populated by the engine. Note the engine
    /// pre-allocates 16 slots regardless of actual difficulty — for low
    /// difficulties some entries may be unused/garbage. Without the active
    /// seat-count address (still TBD), all 16 are exposed and the renderer
    /// has to infer which are real from the layout.</summary>
    public IReadOnlyList<Seat> Seats { get; }

    public bool IsActive => Difficulty > 0;

    internal CaptainCajunState(int difficulty, IReadOnlyList<Seat> seats)
    {
        Difficulty = difficulty;
        Seats = seats;
    }

    public static CaptainCajunState Read(IMemoryReader mem)
    {
        int diff = mem.ReadWord(CaptainCajunMemoryMap.Difficulty);
        int count = mem.ReadWord(CaptainCajunMemoryMap.ActiveSlotCount);
        if (count <= 0 || count > 32) count = 16;

        var zbIdBytes = mem.ReadBytes(CaptainCajunMemoryMap.SeatZbIds, (count + 1) * 2);
        var occBytes  = mem.ReadBytes(CaptainCajunMemoryMap.SeatOccupancy, count + 1);
        var posBytes  = mem.ReadBytes(CaptainCajunMemoryMap.SeatPositions, count * 4);
        // Real per-seat placed-zb-hdr1A array — verified by 3-state diff.
        var placedHdrBytes = mem.ReadBytes(CaptainCajunMemoryMap.PerSeatZbId, count * 2);

        var seats = new List<Seat>(count);
        if (posBytes is not null)
            for (int i = 0; i < count; i++)
            {
                int x = BitConverter.ToUInt16(posBytes, i * 4);
                int y = BitConverter.ToUInt16(posBytes, i * 4 + 2);
                ushort rawId = zbIdBytes is not null
                    ? BitConverter.ToUInt16(zbIdBytes, (i + 1) * 2) : (ushort)0;
                byte rawOcc = occBytes is not null ? occBytes[i + 1] : (byte)0;
                ushort placedHdr = placedHdrBytes is not null
                    ? BitConverter.ToUInt16(placedHdrBytes, i * 2) : (ushort)0;
                seats.Add(new Seat(i, x, y, rawId, rawOcc, placedHdr));
            }
        return new CaptainCajunState(diff, seats);
    }
}
