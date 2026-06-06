namespace ZoombiniHelper;

/// <summary>
/// Static virtual addresses for Captain Cajun's Ferryboat in v2.
///
/// <para>State addresses verified by disassembly of the Ferry init function
/// at <c>0x0040FE30..0x0041025F</c>. The init pushes the ferry.mhk pointer
/// (string at <c>0x48BDDC</c>) at <c>0x0040FF04</c>, then registers ~25
/// SCRB hotspot scripts, then dispatches per difficulty via a 4-entry jump
/// table at <c>0x00411648</c>:</para>
///
/// <code>
///   diff 0 → SCRB id 0x5E6
///   diff 1 → SCRB id 0x5EB
///   diff 2 → SCRB id 0x5F0
///   diff 3 → SCRB id 0x5F5
/// </code>
///
/// <para>Each SCRB encodes the seat layout for that difficulty; the loader
/// at <c>0x00411660</c> parses it and populates the engine-shared position
/// table at <see cref="SeatPositions"/>.</para>
///
/// <para>**Status: incomplete.** Difficulty + seat positions decoded.
/// Adjacency / constraint table and per-seat zb-mapping not yet identified
/// — those need another disasm session.</para>
/// </summary>
public static class CaptainCajunMemoryMap
{
    /// <summary>Generic engine difficulty word (1..4). Used by every puzzle.
    /// The wrapper at <c>0x0044BE90</c> reads this and returns 0-based
    /// difficulty (subtracts 1, clamps to [0..3]).</summary>
    public const nint Difficulty = 0x004A2188;

    /// <summary>Cajun's internal difficulty cache (0..3). Set during
    /// init from <see cref="Difficulty"/> via <c>0x44BE90</c>. Used by the
    /// jump table at <c>0x00411648</c>.</summary>
    public const nint DifficultyCajun = 0x00495A98;

    /// <summary>Per-active-slot (x, y) position in engine coords. Each entry
    /// is 4 bytes (x_word, y_word). Populated by the SCRB-driven seat init
    /// at <c>0x00411800</c>. SHARED with Stone Rise — the engine recycles
    /// this table per puzzle.</summary>
    public const nint SeatPositions = 0x004A4018;

    /// <summary>Number of active hit-testable slots — engine-wide. Always
    /// 16 in observed dumps (= engine maximum). The actual ferry seat
    /// count for a given difficulty is not yet identified.</summary>
    public const nint ActiveSlotCount = 0x004A32B4;

    /// <summary>Sequential per-slot index/sprite-id table — one word per
    /// slot. Populated by the same init loop. Shared with other puzzles.</summary>
    public const nint SlotIndices = 0x004A36B8;

    /// <summary>Per-seat zb-identity table. One word per seat = the placed
    /// zb's hdr1A (or 0 if empty). Verified at 0x004137C7-0x004137D6:
    /// <code>
    ///   mov dx, word ptr [eax*2 + 0x4a37b4]   ; load
    ///   cmp dx, word ptr [ecx + 0x1a]         ; vs zb hdr1A (= +0x1A in node header)
    ///   mov dword ptr [eax*2 + 0x4a37b4], ebx ; clear (when un-placing)
    /// </code>
    /// Same lookup mechanic as Stone Rise's tile.w1 — match against
    /// PoolMember.HeaderId to identify the placed zb. Indexed by seat
    /// number (1-based per disasm convention — entry at index 0 is junk).
    /// </summary>
    public const nint SeatZbIds = 0x004A37B4;

    /// <summary>Per-seat occupancy byte. One byte per seat (0=empty,
    /// 1=occupied). Verified at 0x004137E4: <c>mov byte ptr [eax + 0x4a32d0], 0</c>
    /// when un-placing.</summary>
    public const nint SeatOccupancy = 0x004A32D0;

    /// <summary>Offset within a zoombini node payload where the engine
    /// stores the seat number the zb currently sits in (0 = none, 1..N
    /// = seat index). Verified at 0x004137A7: <c>mov ax, word ptr [ecx + 0xe0]</c>
    /// — ecx points to the zb node, +0xE0 holds the seat reference.</summary>
    public const int ZbCurrentSeatOffset = 0xE0;

    /// <summary>Cajun-specific state region. Verified via 73+ ref addresses
    /// in disassembly. Most-referenced individual addresses:
    /// <list type="bullet">
    ///   <item>0x495AFC (25 refs) — heavily-used flag/counter</item>
    ///   <item>0x495A66 (14 refs)</item>
    ///   <item>0x495A6E (13 refs)</item>
    ///   <item>0x495A80 (13 refs)</item>
    ///   <item>0x495A54 (10 refs)</item>
    /// </list>
    /// Specific semantics not yet decoded.</summary>
    public const nint StateRegionStart = 0x00495A00;
    public const nint StateRegionEnd   = 0x00495B00;

    /// <summary>Per-seat zb-identity table indexed by 0-based seat number.
    /// Word per seat: <c>0</c> if empty, otherwise the placed zb's hdr1A
    /// (= word at <c>node + 0x1A</c>, matches <c>PoolMember.HeaderId</c>).
    ///
    /// <para>Verified empirically by 3-state diff (memdump-204605 empty,
    /// 204619 with 1 placed on seat 10 with hdr1A=0x000D, 204713 with a
    /// second placed on seat 3 with hdr1A=0x0012):</para>
    /// <code>
    ///   addr 0x4A33AC = base + 10*2 → got 0x000D after seat-10 placement
    ///   addr 0x4A339E = base + 3*2  → got 0x0012 after seat-3 placement
    ///   ⇒ base = 0x4A3398, stride = 2 bytes
    /// </code>
    /// Init code at <c>0x00411862</c> zeroes this array via
    /// <c>mov word ptr [ecx*2 + 0x4a3398], bp</c> while seat layout is built.
    /// </summary>
    public const nint PerSeatZbId = 0x004A3398;

    /// <summary>Last placement's seat index (most recent only). Useful as a
    /// secondary cross-check, but the per-seat array at
    /// <see cref="PerSeatZbId"/> is the primary source of truth.</summary>
    public const nint LastPlacementSeat = 0x004A218C;

    /// <summary>Last placement's zb hdr1A (most recent only).</summary>
    public const nint LastPlacementZbId = 0x004A21D4;
}
