namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Trennt die Bubblewonder-Pool-Liste in <b>verfügbaren Hauptpool</b> und
/// <b>insel-geparkte (re-losschickbare)</b> ZBs — anhand harter Engine-Signale
/// statt der alten y-Pixel-Cluster-Heuristik.
///
/// <para><b>Anlass (2026-05-27):</b> ein in einer Falle steckender ZB wurde
/// vom y-Cluster (PoolClusterer) als „auf Insel" klassifiziert, weil er ein
/// abgesetztes y hatte. Falle ≠ Insel.</para>
///
/// <para><b>Diskriminatoren (verifiziert Disasm + Live):</b>
/// <list type="bullet">
/// <item>Handle (+0x20): siehe <see cref="ZoombiniHandle"/> — Pool (verfügbar),
///   Held (hochgehoben), Launched (losgeschickt & aktiv) bzw. Parked
///   (losgeschickt & zur Ruhe gekommen). <see cref="ZoombiniHandle.IsOnGrid"/>
///   fasst die beiden Grid-Zustände zusammen.</item>
/// <item>Grid-Zelle eines losgeschickten ZB (Position +0x72/+0x74) in der
///   engine-eigenen Zelltyp-Tabelle (<see cref="BubblewonderMemoryMap.CellTypeTable"/>):
///   nur <c>0x15</c>/<c>0x16</c> (Zwischenstationen) = echte Insel. <c>0x17</c>
///   = gescort, Trap (Typ 1) / sonst = nicht verfügbar.</item>
/// </list></para>
/// </summary>
public static class BubblewonderPoolClassifier
{
    private const int GridRows = 12;
    private const int GridCols = 13;

    public static (IReadOnlyList<PoolMember> Main, IReadOnlyList<PoolMember> IslandParked)
        Split(IReadOnlyList<PoolMember> pool, IMemoryReader mem)
    {
        var table = mem.ReadBytes(BubblewonderMemoryMap.CellTypeTable, GridRows * GridCols * 2);
        var main = new List<PoolMember>();
        var island = new List<PoolMember>();
        foreach (var p in pool)
        {
            // GEHALTEN (in der Hand): immer Haupt-Pool. +0x76/Grid sind dann STALE
            // (ein von der Insel hochgehobener ZB behält +0x76=2) → nicht als Insel werten.
            if (p.Handle == ZoombiniHandle.Held)
            {
                main.Add(p);
                continue;
            }

            // PRIMÄR: +0x76 (Outcome-Typ) — disasm-verifiziert, STABIL (anders als die
            // Grid-Position +0x72/74, die auf (0,0) flackert und den Insel-ZB sonst durch-
            // rutschen ließ — DER Grund, warum Insel-ZBs immer wieder „nicht erkannt" wurden):
            //   1/2 = auf Zwischenstation (Insel GEPARKT), 3 = Ziel (gescort), 0 = Pool/in-transit.
            if (p.OutcomeType is 1 or 2) { island.Add(p); continue; }
            if (p.OutcomeType == 3) continue;   // am Ziel gescort → weder Pool noch Insel

            // SEKUNDÄR (Outcome 0 oder unbekannt): Grid-Zelltyp als Fallback.
            if (ZoombiniHandle.IsOnGrid(p.Handle))
            {
                // Losgeschickt, noch nicht gelandet: nur echte Insel zählt; Falle/in-flight → weder.
                if (IsIntermediateStation(table, p.GridRow, p.GridCol))
                    island.Add(p);
            }
            else if (p.Handle == ZoombiniHandle.Pool || p.Handle == 0)
            {
                if (IsIntermediateStation(table, p.GridRow, p.GridCol))
                    island.Add(p);
                else
                    main.Add(p);
            }
        }
        return (main, island);
    }

    private static bool IsIntermediateStation(byte[]? table, ushort row, ushort col)
    {
        if (table is null || row >= GridRows || col >= GridCols) return false;
        int pos = row * GridCols + col;
        if (pos * 2 + 1 >= table.Length) return false;
        int t = table[pos * 2] | (table[pos * 2 + 1] << 8);
        return t == 0x15 || t == 0x16;
    }
}
