namespace ZoombiniHelper.Puzzles;

/// <summary>
/// Canonical identifier for each Zoombinis puzzle. Order matches the
/// puzzle table in CLAUDE.md (segment-map). New puzzles append at the end.
/// </summary>
public enum PuzzleId
{
    None = 0,
    AllergicCliffs,
    StoneColdCaves,
    PizzaPass,
    HotelDimensia,
    MirrorMachine,
    BubblewonderAbyss,
    LionsLair,
    MudballWall,
    StoneRise,
    Fleens,
    TattooedToads,
    CaptainCajun,

    /// <summary>The world map / picker screen between puzzles.
    /// Not a puzzle itself, but useful as a positive "user is between puzzles"
    /// signal — verified at 0x00499E6C across 3 lobby dumps + 9 puzzle dumps.</summary>
    LobbyMap,

    /// <summary>Zoombini Island — the character-creation / starting screen
    /// before the world map. Verified exclusive at 0x0049B894 = 102.</summary>
    ZoombiniIsland,

    /// <summary>Shelter Rock — the first camp the Zoombinis reach after
    /// crossing Allergic Cliffs. Loads basecamp.mhk. Verified exclusive at
    /// 0x00494274 = 102 across 12 dumps.</summary>
    ShelterRock,

    /// <summary>Shade Tree — the second camp. Loads bctwo.mhk.
    /// Verified exclusive at 0x004943E4 = 322 across 13 dumps.</summary>
    ShadeTree,

    /// <summary>Zoombiniville — the final town the Zoombinis reach after all
    /// puzzles. Loads town.mhk. Verified exclusive at 0x004A2A90 = 322
    /// across 14 dumps.</summary>
    Zoombiniville,
}
