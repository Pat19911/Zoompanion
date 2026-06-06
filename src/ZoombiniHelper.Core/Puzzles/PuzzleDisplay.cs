using ZoombiniHelper.Localization;

namespace ZoombiniHelper.Puzzles;

/// <summary>
/// Display styles for puzzles and locations whose helper logic is "just show
/// where you are". Puzzles with full helpers (currently only Cliff) render
/// their own UI and don't appear here. Title/body text is resolved from the
/// localization tables on every lookup, so a language switch takes effect
/// immediately; only the accent colour is fixed per location.
/// </summary>
public static class PuzzleDisplay
{
    /// <summary>Localization key stem + accent colour for each styled location.</summary>
    private static readonly Dictionary<PuzzleId, (string Key, uint Accent)> Styles = new()
    {
        [PuzzleId.LobbyMap]       = ("display.lobbymap",      0xFF8CC8DC),
        [PuzzleId.ZoombiniIsland] = ("display.island",        0xFFB4DCB4),
        [PuzzleId.ShelterRock]    = ("display.shelterrock",   0xFFDCC88C),
        [PuzzleId.ShadeTree]      = ("display.shadetree",     0xFFA0DCA0),
        [PuzzleId.Zoombiniville]  = ("display.zoombiniville", 0xFFFFC8DC),
    };

    /// <summary>Look up the display style for a puzzle/location, or null if it
    /// has its own bespoke renderer (e.g. Cliff with bridge recommendations).</summary>
    public static PuzzleDisplayStyle? StyleFor(PuzzleId id)
    {
        if (!Styles.TryGetValue(id, out var s)) return null;
        string title = Loc.T($"{s.Key}.title");
        string body  = Loc.T($"{s.Key}.lead") + "\n\n" + Loc.T("common.dumphint");
        return new PuzzleDisplayStyle(title, body, s.Accent);
    }
}
