namespace ZoombiniHelper.Bubblewonder.Simulator;

/// <summary>
/// Statisches Grid-Modell für die Simulation. Enthält pro Cell: Type,
/// Channel, Direction-Bits, Conditional-Match-Regel. Plus die Maschinen-
/// Liste und den aktuellen <see cref="GridState"/>.
///
/// <para>Wird beim Round-Start aus <see cref="BubblewonderState"/> gebaut
/// (siehe <see cref="FromLiveState"/>) — danach immutable bis auf den
/// veränderlichen <see cref="State"/>.</para>
/// </summary>
public sealed class BubblewonderGridModel
{
    private readonly Dictionary<int, CellModel> _cellsByPosition;
    private readonly Dictionary<int, List<(int CellIdx, CellModel Cell)>> _cellsByChannel;

    public IReadOnlyList<MachineModel> Machines { get; }
    public GridState State { get; private set; }

    public BubblewonderGridModel(
        IReadOnlyDictionary<int, CellModel> cellsByPosition,
        IReadOnlyList<MachineModel> machines,
        GridState? state = null)
    {
        _cellsByPosition = new Dictionary<int, CellModel>(cellsByPosition);
        Machines = machines;
        State = state ?? new GridState();
        _cellsByChannel = new();
        foreach (var (pos, cell) in _cellsByPosition)
        {
            if (!_cellsByChannel.TryGetValue(cell.Channel, out var list))
                _cellsByChannel[cell.Channel] = list = new();
            list.Add((pos, cell));
        }
    }

    public CellModel CellAt(int positionIndex) =>
        _cellsByPosition.TryGetValue(positionIndex, out var c) ? c : CellModel.Empty;

    public IEnumerable<(int CellIdx, CellModel Cell)> CellsInChannel(int channel) =>
        _cellsByChannel.TryGetValue(channel, out var list)
            ? list
            : Enumerable.Empty<(int, CellModel)>();

    public GridState CloneState() => State.Clone();

    public BubblewonderGridModel WithState(GridState newState) =>
        new(_cellsByPosition, Machines, newState);
}

/// <summary>Eine Cell im Grid — leitet aus REGS-Record + Live-Bubble-Daten ab.
///
/// <para><see cref="ActiveDirections"/> ist indexed by F-Bit-Position (0=F4, 1=F5,
/// 2=F6, 3=F7), nicht by <see cref="Direction"/>. Das matcht zur Engine-Konvention:
/// Switch-State (`+0x7C`) ist der gleiche F-Bit-Index. Die Mapping F-Bit→Direction
/// ist <see cref="FBitToDirection"/> — live verifiziert 2026-05-03 über 7+
/// StaticDeflector-Beobachtungen.</para></summary>
public sealed record CellModel(
    MechanismType Type,
    int Channel,                            // = REGS-F3
    bool[] ActiveDirections,                // [F4, F5, F6, F7] = [Left, Down, Right, Up]
    Direction? PrimaryDirection,            // erste aktive Direction (Direction-typed)
    int ConditionalAttrCode,                // 0=keine, 1=Hair, 2=Eyes, 3=Nose, 4=Feet
    int ConditionalVariant,                 // 1..5 für die geforderte Variante
    bool IsIslandMachine = false,
    int? MachineIdx = null)
{
    /// <summary>F-Bit-Index → logische Bewegungsrichtung. Live-verifiziert
    /// 2026-05-03 über mehrere StaticDeflector mit eindeutigem F-Bit.</summary>
    public static readonly Direction[] FBitToDirection =
    {
        Direction.Left,   // F4 (index 0)
        Direction.Down,   // F5 (index 1)
        Direction.Right,  // F6 (index 2)
        Direction.Up,     // F7 (index 3)
    };

    /// <summary>Inverse von <see cref="FBitToDirection"/>: Direction → F-Bit-Index.</summary>
    public static int FBitIndexFor(Direction d) => d switch
    {
        Direction.Left  => 0,
        Direction.Down  => 1,
        Direction.Right => 2,
        Direction.Up    => 3,
        _ => -1,
    };

    /// <summary>Helper für Tests: konstruiert ActiveDirections-Array mit den
    /// gegebenen Bewegungsrichtungen aktiv.</summary>
    public static bool[] MakeFBits(params Direction[] dirs)
    {
        var result = new bool[4];
        foreach (var d in dirs)
        {
            int idx = FBitIndexFor(d);
            if (idx >= 0) result[idx] = true;
        }
        return result;
    }

    public static readonly CellModel Empty = new(
        MechanismType.Passthrough, 0, new bool[4], null, 0, 0);

    public bool MatchesZb(SimZb zb) => ConditionalAttrCode switch
    {
        1 => zb.Hair == ConditionalVariant,
        2 => zb.Eyes == ConditionalVariant,
        3 => zb.Nose == ConditionalVariant,
        4 => zb.Feet == ConditionalVariant,
        _ => false,
    };

    public bool HasDirectionAtStateIndex(int idx) =>
        idx >= 0 && idx < 4 && ActiveDirections[idx];

    public Direction? DirectionAtStateIndex(int idx) =>
        HasDirectionAtStateIndex(idx) ? FBitToDirection[idx] : null;
}

/// <summary>Eine Bubble-Maschine — Spawn-Punkt mit Start-Richtung.
/// <para><see cref="SpriteX"/>/<see cref="SpriteY"/> = Bildschirm-Pixel des sichtbaren
/// Maschinen-Sprites (für eine UI-Lage, die zu dem passt, was der Spieler SIEHT —
/// der Sprite-Standort ≠ die Spawn-Zelle). −1 = unbekannt.</para></summary>
public sealed record MachineModel(
    int Index,
    int StartCellIndex,
    Direction StartDirection,
    bool IsIsland,
    int SpriteX = -1,
    int SpriteY = -1);
