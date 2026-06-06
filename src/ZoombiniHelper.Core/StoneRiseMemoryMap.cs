namespace ZoombiniHelper;

/// <summary>
/// Static virtual addresses for Stone Rise (slides.mhk) state in v2.
/// Reverse-engineered from the MHK loader at 0x00439220 and the init
/// loop at 0x0043930B which builds the 117-tile field array.
///
/// <para>**Status: incomplete.** The tile records are decoded but the
/// per-field-element semantics (which 18-byte slot means what) is still
/// being investigated. The dumper exposes the raw record contents so the
/// helper can be built incrementally.</para>
/// </summary>
public static class StoneRiseMemoryMap
{
    /// <summary>0-based difficulty (0..3). Set from <c>call 0x44be90</c>
    /// at 0x00439343, stored at 0x49C784.</summary>
    public const nint Difficulty = 0x0049C784;

    /// <summary>Base of the 117-record tile array. Each record is 18 bytes
    /// (9 words). Init at 0x0043930B fills word 0 of every record with
    /// 0x1F4 (= 500) — the "empty tile" type marker.</summary>
    public const nint TilesBase = 0x0049BD82;

    public const int TileCount = 117;

    /// <summary>One tile record is 9 words = 18 bytes.</summary>
    public const int TileStride = 18;

    /// <summary>Type code at word 0 of each tile record. Observed values:
    /// 500 = empty/default, 501 = connector slot (1 zoombini), 506 = pair
    /// slot (2 zoombinis). The exact set is not yet exhaustively enumerated.</summary>
    public const ushort TileTypeEmpty     = 500;
    public const ushort TileTypeConnector = 501;
    public const ushort TileTypePair      = 506;

    /// <summary>Cleared 234-byte (117-word) parallel array — likely a
    /// per-tile state/flag.</summary>
    public const nint TileFlags = 0x0049C874;

    /// <summary>Cleared 234-byte (117-word) parallel array — purpose
    /// unknown, possibly per-tile sprite or animation state.</summary>
    public const nint TileAux = 0x0049C670;

    /// <summary>Cleared 40-byte (20-word) array — observed to fill up
    /// during play, possibly a current-pair-candidates list.</summary>
    public const nint PairList = 0x0049C820;

    /// <summary>Static .rdata table of per-tile screen coordinates used at
    /// Difficulty 1/2/3. Each entry is 4 bytes: <c>(x_word, y_word)</c>.
    /// Indexed by tile index 0..116. Verified via dispatch at 0x00418D1C
    /// (<c>mov ecx, [edi*4 + 0x48bfc0]</c>) — the engine uses this for
    /// sprite anchoring with small additional offsets at draw time.</summary>
    public const nint TileScreenCoordsEasy = 0x0048BFC0;

    /// <summary>Static .rdata table of per-tile screen coordinates used at
    /// Difficulty 4 only. Different layout from <see cref="TileScreenCoordsEasy"/>.
    /// Verified via dispatch at 0x00418D59.</summary>
    public const nint TileScreenCoordsHard = 0x0048C028;

    // --- Live engine-coord arrays (verified 2026-04-29 from disasm of the
    // mouse hit-test loop at 0x004492F9..0x00449401) ---

    /// <summary>Number of active hit-testable slots — the engine's per-frame
    /// loop iterates 0..[ActiveSlotCount-1] when figuring out what's under
    /// the cursor.</summary>
    public const nint ActiveSlotCount = 0x004A32B4;

    /// <summary>Per-active-slot (X, Y) position in engine coords. Each
    /// entry is 4 bytes (x_word, y_word). Indexed by active-slot order
    /// 0..[ActiveSlotCount-1] — NOT by tile_idx. Mapping to tile_idx is
    /// via <see cref="ActiveSlotToTileIndex"/>.</summary>
    public const nint ActiveSlotPositions = 0x004A4018;

    /// <summary>Lookup table: active-slot-index → tile-index. Verified
    /// against memdump-141432: the engine accesses this 1-based (its loop
    /// counter starts at 1, not 0), so word [0x49C7B0] is a junk sentinel
    /// and real entries start at +2. We expose the table from the first
    /// real entry to keep our code 0-indexed. Each entry is one word
    /// (16-bit tile index). Length = <see cref="ActiveSlotCount"/>.</summary>
    public const nint ActiveSlotToTileIndex = 0x0049C7B2;

    /// <summary>Hit-test tolerance (half-width of the slot rect in engine
    /// coords). Verified value 15 in live dumps.</summary>
    public const nint HitTestTolerance = 0x004A218E;

    /// <summary>Live cursor / held-zoombini X position in engine coords.
    /// Updated every frame the held zoombini's tick runs.</summary>
    public const nint CursorX = 0x004A27C8;

    /// <summary>Live cursor / held-zoombini Y position in engine coords.</summary>
    public const nint CursorY = 0x004A2810;

    /// <summary>Active-slot index currently under the cursor, or 0 if none.
    /// Read via <c>[ActiveSlotIndex] + 1</c> in the engine's wrapper at
    /// 0x00448E60.</summary>
    public const nint CursorActiveSlotIndex = 0x004A218C;

    /// <summary>Flag: whether <see cref="CursorActiveSlotIndex"/> is valid.
    /// 0 = no slot under cursor, non-zero = the index field is current.</summary>
    public const nint CursorActiveSlotValid = 0x004A21D4;
}
