namespace ZoombiniHelper.Puzzles;

/// <summary>
/// Single source of truth for which puzzle detectors are registered. New
/// puzzles get added here, not in the overlay. All detectors use the same
/// MHK-filename-buffer pattern: each puzzle has a dedicated word in .data
/// that's non-zero only while that puzzle is loaded.
/// </summary>
public static class PuzzleRegistry
{
    public static IReadOnlyList<IPuzzleDetector> AllDetectors() => new IPuzzleDetector[]
    {
        // 12 puzzles — third arg is a localization key (resolved per active language)
        new MhkBufferDetector(PuzzleId.AllergicCliffs,   "bridge.mhk",  "puzzle.cliffs",       0x00494568),
        new MhkBufferDetector(PuzzleId.StoneColdCaves,   "caves.mhk",   "puzzle.caves",        0x004A2AC4),
        new MhkBufferDetector(PuzzleId.PizzaPass,        "pizza.mhk",   "puzzle.pizza",        0x0049BBEC),
        new MhkBufferDetector(PuzzleId.HotelDimensia,    "hotel.mhk",   "puzzle.hotel",        0x00496584),
        new MhkBufferDetector(PuzzleId.Fleens,           "fleens.mhk",  "puzzle.fleens",       0x00495D34),
        new MhkBufferDetector(PuzzleId.MudballWall,      "net.mhk",     "puzzle.mudball",      0x0049B248),
        new MhkBufferDetector(PuzzleId.LionsLair,        "tunnels.mhk", "puzzle.lions",        0x00494674),
        new MhkBufferDetector(PuzzleId.BubblewonderAbyss,"maze2.mhk",   "puzzle.bubblewonder", 0x0049A050),
        new MhkBufferDetector(PuzzleId.CaptainCajun,     "ferry.mhk",   "puzzle.captain",      0x00495A90),
        new MhkBufferDetector(PuzzleId.TattooedToads,    "lilly.mhk",   "puzzle.toads",        0x004997F4),
        new MhkBufferDetector(PuzzleId.StoneRise,        "slides.mhk",  "puzzle.stonerise",    0x0049C9C8),
        new MhkBufferDetector(PuzzleId.MirrorMachine,    "smoke.mhk",   "puzzle.mirror",       0x0049CB60),

        // 5 between-puzzle locations
        new MhkBufferDetector(PuzzleId.ZoombiniIsland,   "island.mhk",  "loc.island",          0x0049B894),
        new MhkBufferDetector(PuzzleId.ShelterRock,      "basecamp.mhk","loc.shelterrock",     0x00494274),
        new MhkBufferDetector(PuzzleId.LobbyMap,         "rodmap.mhk",  "loc.lobbymap",        0x00499E6C),
        new MhkBufferDetector(PuzzleId.ShadeTree,        "bctwo.mhk",   "loc.shadetree",       0x004943E4),
        new MhkBufferDetector(PuzzleId.Zoombiniville,    "town.mhk",    "loc.zoombiniville",   0x004A2A90),
    };

    public static PuzzleManager CreateManager() => new(AllDetectors());
}
