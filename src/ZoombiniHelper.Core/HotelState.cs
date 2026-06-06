namespace ZoombiniHelper;

/// <summary>
/// Snapshot of Hotel Dimensia state. Reads the engine's axis-attribute
/// assignments, the boarded-cell list (Diff 3 only), and the per-room
/// constraint arrays that fill in as the player drops zoombinis.
///
/// <para>The engine generates a hidden 5-element X/Y permutation during
/// init (Diff 3 only — needed to pre-decide which empty cells get boarded)
/// but memsets those scratch arrays at end of init. So at the moment the
/// puzzle becomes playable, the only persistent state is the boarded-cell
/// markers; the column→X-value mapping has to be recovered by the
/// <see cref="HotelSolver"/> from the boarded cells + the live pool.</para>
///
/// <para>Once the player drops a zoombini, the engine writes
/// <c>constraint_X[room] = zb.attrs[axis_X_type]</c> and the corresponding
/// Y value, which directly pins down that column's and row's permutation
/// entry. Live constraints from <see cref="ConstraintX"/>/<see cref="ConstraintY"/>
/// give the solver enough information to nail down the full grid after a
/// placement or two.</para>
/// </summary>
public sealed class HotelState
{
    public readonly record struct BoardedCell(int Index, int Row, int Column);

    /// <summary>1-based difficulty (1..4) — matches the in-game UI.</summary>
    public int Difficulty { get; }

    /// <summary>5×5 = 25 at Diff 1/2/3, 5×5×5 = 125 at Diff 4.</summary>
    public int NumRooms { get; }

    /// <summary>How many axes are in play: Diff 1 → 1, Diff 2/3 → 2, Diff 4 → 3.</summary>
    public int AxisCount => Difficulty switch { 1 => 1, 4 => 3, _ => 2 };

    /// <summary>Attribute (1-based <c>ZoombiniVariants</c> id) on the X axis
    /// (columns, left→right). Always present.</summary>
    public byte AxisX { get; }

    /// <summary>Attribute on the Y axis (rows, top→bottom). 0 at Diff 1.</summary>
    public byte AxisY { get; }

    /// <summary>Attribute on the Z axis (Diff 4 only). 0 otherwise.</summary>
    public byte AxisZ { get; }

    /// <summary>Cells the engine boarded shut (Diff 3 only). Each entry's
    /// Row/Column is its position in the visible 5×5 grid.</summary>
    public IReadOnlyList<BoardedCell> Boarded { get; }

    /// <summary>X-axis attribute value (1..5) the engine has pinned for each
    /// of the 25 rooms. 0 means the room hasn't received a zoombini yet
    /// (or doesn't exist at this difficulty). Indexed row-major: room
    /// <c>r*5+c</c> is at column c, row r.</summary>
    public IReadOnlyList<byte> ConstraintX { get; }

    /// <summary>Y-axis attribute value (1..5) per room — same layout as
    /// <see cref="ConstraintX"/>.</summary>
    public IReadOnlyList<byte> ConstraintY { get; }

    public bool IsActive => NumRooms is 25 or 125 && AxisX is >= 1 and <= 4;

    internal HotelState(int difficulty, int numRooms,
                       byte axisX, byte axisY, byte axisZ,
                       IReadOnlyList<BoardedCell> boarded,
                       IReadOnlyList<byte> cx, IReadOnlyList<byte> cy)
    {
        Difficulty = difficulty;
        NumRooms = numRooms;
        AxisX = axisX;
        AxisY = axisY;
        AxisZ = axisZ;
        Boarded = boarded;
        ConstraintX = cx;
        ConstraintY = cy;
    }

    public static HotelState Read(IMemoryReader mem)
    {
        int rawDiff = mem.ReadWord(HotelMemoryMap.Difficulty);
        int n       = mem.ReadWord(HotelMemoryMap.NumRooms);
        byte axisX  = ToAttributeId(mem.ReadWord(HotelMemoryMap.AxisXType));
        byte axisY  = ToAttributeId(mem.ReadWord(HotelMemoryMap.AxisYType));
        byte axisZ  = ToAttributeId(mem.ReadWord(HotelMemoryMap.AxisZType));

        var boarded = (rawDiff == 2 && n == 25)
            ? ReadBoarded(mem)
            : (IReadOnlyList<BoardedCell>)Array.Empty<BoardedCell>();

        var (cx, cy) = (n == 25)
            ? ReadConstraints(mem)
            : (Array.Empty<byte>(), Array.Empty<byte>());

        return new HotelState(rawDiff + 1, n, axisX, axisY, axisZ, boarded, cx, cy);
    }

    private static IReadOnlyList<BoardedCell> ReadBoarded(IMemoryReader mem)
    {
        var bytes = mem.ReadBytes(HotelMemoryMap.RoomState, 25 * 2);
        if (bytes is null) return Array.Empty<BoardedCell>();
        // The engine stores rooms COLUMN-major: index `i` is column `i/5`,
        // row `i%5`. Verified against memdump-113414 vs the user's visual
        // boarded-cell report — row-major reading produced wrong (col, row)
        // pairs for every cell.
        var result = new List<BoardedCell>();
        for (int i = 0; i < 25; i++)
        {
            ushort v = BitConverter.ToUInt16(bytes, i * 2);
            if (v == HotelMemoryMap.BoardedMarker)
                result.Add(new BoardedCell(i, Row: i % 5, Column: i / 5));
        }
        return result;
    }

    private static (byte[] cx, byte[] cy) ReadConstraints(IMemoryReader mem)
    {
        var xBytes = mem.ReadBytes(HotelMemoryMap.ConstraintX, 25 * 2);
        var yBytes = mem.ReadBytes(HotelMemoryMap.ConstraintY, 25 * 2);
        var cx = new byte[25];
        var cy = new byte[25];
        if (xBytes is not null)
            for (int i = 0; i < 25; i++)
                cx[i] = (byte)BitConverter.ToUInt16(xBytes, i * 2);
        if (yBytes is not null)
            for (int i = 0; i < 25; i++)
                cy[i] = (byte)BitConverter.ToUInt16(yBytes, i * 2);
        return (cx, cy);
    }

    private static byte ToAttributeId(int raw)
        => raw is >= 0 and <= 3 ? (byte)(raw + 1) : (byte)0;
}
