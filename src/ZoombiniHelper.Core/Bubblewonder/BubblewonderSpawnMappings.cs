namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Statische Spawn-Cell-Mappings pro REGS-Resource-ID, basierend auf Live-Beobachtungen
/// am 2026-05-02 (35+ Dumps über alle Difficulty-Stufen 1-4).
///
/// <para>Validierung: Hauptpool-Cells sind über mehrfache Beobachtungen exakt konsistent.
/// Insel-Cells (in Diff 4) sind in der Eck-Zone (oben-links: row≤3 AND col≤3,
/// ODER unten-rechts: row≥9 AND col≥9) — nicht in der Mitte.</para>
///
/// <para>Out-of-the-box: User installiert Helper, Mappings sind sofort verfügbar
/// für alle hier gelisteten REGS. Tracker dient nur noch zur Verifikation +
/// als Fallback wenn eine REGS auftaucht die hier fehlt (z.B. 16607 noch unbekannt).</para>
/// </summary>
public static class BubblewonderSpawnMappings
{
    /// <summary>Spawn-Cell-Pool pro REGS-ID. Wert = Liste von Position-Indices
    /// (= prop1 * 13 + prop2). Eine REGS-ID nicht in dieser Map = unbekanntes Layout,
    /// Tracker fällt auf Live-Detection zurück.</summary>
    public static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> ByRegsId =
        new Dictionary<int, IReadOnlyList<int>>
        {
            // Diff 1 (2 Maschinen, kein Insel)
            [16600] = new[] { 8, 34 },
            [16601] = new[] { 8, 61 },

            // Diff 2 (1-2 Maschinen, kein Insel)
            [16602] = new[] { 21 },
            [16603] = new[] { 8, 61 },

            // Diff 3 (1 Maschine, kein Insel)
            [16604] = new[] { 34 },
            [16605] = new[] { 34 },

            // Diff 4 (3 Maschinen, mit Insel-Maschine)
            // NUR WERFER (verifiziert konstant pro REGS über 100+ Beobachtungen).
            // Der Insel-Re-Launch-Spawn ist NICHT zuverlässig statisch (Daten-Mining
            // 2026-05-30: 16606 zeigte (3,2) UND (4,1) je nach Variante) → wird NICHT
            // mehr geraten, sondern live gelernt + pro (REGS,Variant) persistiert
            // (BubblewonderSpawnStore). Ein falscher Insel-Spawn hat einen ZB getötet.
            [16606] = new[] { 34, 76 },        // Werfer (2,8),(5,11)
            // [16607] = noch unbekannt — Engine wählt 16607 nur zufällig
            [16608] = new[] { 21, 75, 139 },   // (5,10),(1,8),(10,9) — 139 mehrfach als Werfer-Start belegt

            // Diff 5 (Bonus) — noch unbekannt
        };

    /// <summary>Liefert Spawn-Cells für eine REGS, oder null wenn unbekannt.</summary>
    public static IReadOnlyList<int>? GetSpawnCells(int regsId) =>
        ByRegsId.TryGetValue(regsId, out var cells) ? cells : null;

    /// <summary>True wenn diese REGS eine Insel-Maschine hat (= mindestens eine Cell
    /// in Eck-Zone oben-links oder unten-rechts).</summary>
    public static bool HasIsland(int regsId)
    {
        var cells = GetSpawnCells(regsId);
        if (cells is null) return false;
        return cells.Any(IsIslandCell);
    }

    /// <summary>True wenn die Cell-Position in einer Insel-Zone liegt (Eck-Bereich).
    /// HINWEIS: nur noch grobe Heuristik für die Maschinen-Gruppierung; der echte
    /// Insel-Re-Launch-Spawn wird live gelernt (siehe <see cref="BubblewonderSpawnStore"/>),
    /// weil er nicht zuverlässig statisch ist.</summary>
    public static bool IsIslandCell(int posIdx)
    {
        int row = posIdx / 13, col = posIdx % 13;
        return (row <= 3 && col <= 3) || (row >= 9 && col >= 9);
    }
}
