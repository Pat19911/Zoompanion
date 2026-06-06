using ZoombiniHelper.Bubblewonder.Simulator;

namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Persistiert live beobachtete Spawn-Zellen pro <b>(REGS-ID, Variant)</b> in einer
/// Textdatei neben der EXE.
///
/// <para><b>Anlass (2026-05-30, Daten-Mining 283 Dumps):</b> Die Werfer-Spawns sind
/// konstant pro REGS-ID, aber der <b>Insel-Re-Launch-Spawn variiert</b> (an den
/// Variation-Counter gebunden: 16606 zeigte (3,2) UND (4,1) je nach Variante). Ein
/// hardcoded Insel-Wert pro REGS-ID ist daher grundsätzlich falsch und kann tödlich
/// fehl-routen. Lösung: Insel-Spawn live lernen und pro (REGS,Variant) merken — die
/// Beobachtung ist immer korrekt (= Realität), und nach dem ersten Durchlauf dauerhaft
/// bekannt.</para>
///
/// <para>Dateiformat (eine Zeile pro Layout):
/// <c>regs,variant = pos:dir,pos:dir,...</c> — <c>dir</c> = 0..3 (FBitToDirection) oder
/// leer. Robust gegen Tippfehler: unparsebare Zeilen werden übersprungen.</para>
/// </summary>
public sealed class BubblewonderSpawnStore
{
    public readonly record struct Spawn(int Pos, Direction? Dir);

    private readonly Dictionary<(int Regs, int Variant), Dictionary<int, Direction?>> _data = new();

    public static string KeyOf(int regs, int variant) => $"{regs},{variant}";

    /// <summary>Beobachtete Spawns für ein Layout (leer wenn unbekannt).</summary>
    public IReadOnlyList<Spawn> Get(int regs, int variant) =>
        _data.TryGetValue((regs, variant), out var m)
            ? m.Select(kv => new Spawn(kv.Key, kv.Value)).OrderBy(s => s.Pos).ToList()
            : Array.Empty<Spawn>();

    /// <summary>Fügt eine beobachtete Spawn-Zelle hinzu. True = war neu oder Richtung
    /// neu gesetzt (→ Aufrufer sollte speichern).</summary>
    public bool Observe(int regs, int variant, int pos, Direction? dir)
    {
        if (!_data.TryGetValue((regs, variant), out var m))
            _data[(regs, variant)] = m = new();
        bool isNew = !m.TryGetValue(pos, out var existing);
        if (isNew) { m[pos] = dir; return true; }
        if (existing is null && dir is not null) { m[pos] = dir; return true; }  // Richtung nachgereicht
        return false;
    }

    // ---- Pure Parse/Format (unit-testbar, ohne Datei-I/O) ----

    public static BubblewonderSpawnStore Parse(string text)
    {
        var store = new BubblewonderSpawnStore();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            var keyParts = line[..eq].Split(',');
            if (keyParts.Length != 2
                || !int.TryParse(keyParts[0].Trim(), out int regs)
                || !int.TryParse(keyParts[1].Trim(), out int variant)) continue;
            foreach (var tok in line[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var pd = tok.Split(':');
                if (!int.TryParse(pd[0].Trim(), out int pos)) continue;
                Direction? dir = null;
                if (pd.Length > 1 && int.TryParse(pd[1].Trim(), out int d) && d is >= 0 and <= 3)
                    dir = (Direction)d;
                store.Observe(regs, variant, pos, dir);
            }
        }
        return store;
    }

    public string Format()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Bubblewonder Spawn-Zellen (live gelernt). Format: regs,variant = pos:dir,...");
        foreach (var ((regs, variant), m) in _data.OrderBy(kv => kv.Key.Regs).ThenBy(kv => kv.Key.Variant))
        {
            var toks = m.OrderBy(kv => kv.Key)
                        .Select(kv => kv.Value is { } d ? $"{kv.Key}:{(int)d}" : $"{kv.Key}");
            sb.AppendLine($"{regs},{variant} = {string.Join(",", toks)}");
        }
        return sb.ToString();
    }

    // ---- Datei-I/O ----

    public static BubblewonderSpawnStore Load(string path)
    {
        try
        {
            if (File.Exists(path)) return Parse(File.ReadAllText(path));
        }
        catch { /* korrupte Datei → leerer Store, kein Crash */ }
        return new BubblewonderSpawnStore();
    }

    public void Save(string path)
    {
        try { File.WriteAllText(path, Format()); }
        catch { /* best-effort */ }
    }
}
