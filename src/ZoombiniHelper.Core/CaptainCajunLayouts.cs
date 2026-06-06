namespace ZoombiniHelper;

/// <summary>
/// Per-difficulty seat layouts for Captain Cajun's Ferryboat. Extracted
/// statically from <c>ferry.mhk</c> SCRBs <c>0x5E6/0x5EB/0x5F0/0x5F5</c>
/// (one per difficulty 1..4). The SCRB-loader at <c>0x00411660</c> reads
/// these records into the engine's runtime tables (positions at
/// <c>0x4A4018</c>, etc.) — this hardcoded copy lets the helper know the
/// layout WITHOUT depending on a live mhk parse.
///
/// <para>Each entry is <c>(Attr, X, Y)</c> in engine coordinates (matched
/// 1:1 to <c>0x4A4018</c>). The Attr value is a sprite-variant id from
/// the SCRB; see <see cref="CaptainCajunSeatAttr"/> for what each means.
/// SCRB also has Feet (=4) entries that turned out to be staircase /
/// transit markers, NOT actual seats — those are filtered out here.
/// Result: exactly 16 entries per difficulty (matches the engine's
/// <c>ActiveSlotCount=16</c>).</para>
/// </summary>
public static class CaptainCajunLayouts
{
    public enum SeatAttr : byte { None = 0, Hair = 1, Eyes = 2, Nose = 3 }

    public readonly record struct Seat(SeatAttr Attr, int X, int Y);

    public static readonly Dictionary<int, Seat[]> Layouts = new()
    {
        [1] = new[] {
            new Seat(SeatAttr.Hair, 489, 247), new Seat(SeatAttr.Nose, 444, 247),
            new Seat(SeatAttr.Eyes, 399, 247), new Seat(SeatAttr.Nose, 354, 247),
            new Seat(SeatAttr.Hair, 309, 247), new Seat(SeatAttr.Nose, 264, 247),
            new Seat(SeatAttr.Eyes, 219, 247),
            new Seat(SeatAttr.Hair, 229, 287),
            new Seat(SeatAttr.Eyes, 239, 327),
            new Seat(SeatAttr.Nose, 537, 367), new Seat(SeatAttr.Hair, 489, 367),
            new Seat(SeatAttr.Eyes, 441, 367), new Seat(SeatAttr.Hair, 393, 367),
            new Seat(SeatAttr.Nose, 345, 367), new Seat(SeatAttr.Hair, 297, 367),
            new Seat(SeatAttr.Nose, 249, 367),
        },
        [2] = new[] {
            new Seat(SeatAttr.Eyes, 551, 287), new Seat(SeatAttr.Nose, 505, 287),
            new Seat(SeatAttr.Hair, 459, 287), new Seat(SeatAttr.Nose, 413, 287),
            new Seat(SeatAttr.Eyes, 367, 287), new Seat(SeatAttr.Hair, 321, 287),
            new Seat(SeatAttr.Eyes, 275, 287), new Seat(SeatAttr.Nose, 229, 287),
            new Seat(SeatAttr.Nose, 568, 327), new Seat(SeatAttr.Eyes, 521, 327),
            new Seat(SeatAttr.Nose, 474, 327), new Seat(SeatAttr.Hair, 427, 327),
            new Seat(SeatAttr.Eyes, 380, 327), new Seat(SeatAttr.Nose, 333, 327),
            new Seat(SeatAttr.Eyes, 286, 327), new Seat(SeatAttr.Hair, 239, 327),
        },
        [3] = new[] {
            new Seat(SeatAttr.Nose, 444, 247), new Seat(SeatAttr.Eyes, 399, 247),
            new Seat(SeatAttr.Hair, 354, 247), new Seat(SeatAttr.Nose, 309, 247),
            new Seat(SeatAttr.Hair, 459, 287), new Seat(SeatAttr.Nose, 413, 287),
            new Seat(SeatAttr.Hair, 367, 287), new Seat(SeatAttr.Eyes, 321, 287),
            new Seat(SeatAttr.Nose, 474, 327), new Seat(SeatAttr.Eyes, 427, 327),
            new Seat(SeatAttr.Nose, 380, 327), new Seat(SeatAttr.Hair, 333, 327),
            new Seat(SeatAttr.Eyes, 489, 367), new Seat(SeatAttr.Hair, 441, 367),
            new Seat(SeatAttr.Nose, 393, 367), new Seat(SeatAttr.Eyes, 345, 367),
        },
        [4] = new[] {
            new Seat(SeatAttr.Hair, 453, 247), new Seat(SeatAttr.Eyes, 408, 247),
            new Seat(SeatAttr.Hair, 363, 247), new Seat(SeatAttr.Nose, 318, 247),
            new Seat(SeatAttr.Nose, 485, 287), new Seat(SeatAttr.Hair, 439, 287),
            new Seat(SeatAttr.Nose, 393, 287), new Seat(SeatAttr.Eyes, 347, 287),
            new Seat(SeatAttr.Hair, 471, 327), new Seat(SeatAttr.Nose, 424, 327),
            new Seat(SeatAttr.Hair, 377, 327), new Seat(SeatAttr.Eyes, 330, 327),
            new Seat(SeatAttr.Eyes, 504, 367), new Seat(SeatAttr.Hair, 456, 367),
            new Seat(SeatAttr.Eyes, 408, 367), new Seat(SeatAttr.Nose, 360, 367),
        },
    };

    /// <summary>For a given engine seat (by index in <c>0x4A4018</c> table),
    /// return the SCRB attribute. Returns <see cref="SeatAttr.None"/> if
    /// the difficulty isn't known or the index is out of range.</summary>
    public static SeatAttr AttrFor(int difficulty, int engineSeatIndex)
    {
        if (!Layouts.TryGetValue(difficulty, out var seats)) return SeatAttr.None;
        if (engineSeatIndex < 0 || engineSeatIndex >= seats.Length) return SeatAttr.None;
        return seats[engineSeatIndex].Attr;
    }
}
