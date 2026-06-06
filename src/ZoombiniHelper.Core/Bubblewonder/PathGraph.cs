namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Eine Edge im Bubblewonder-Pfad-Graphen: ZB läuft von Mechanismus
/// <see cref="From"/> nach Mechanismus <see cref="To"/>, eventuell
/// abhängig von einer Bedingung.
///
/// <para>Quellen aus denen Edges identifiziert werden:</para>
/// <list type="bullet">
///   <item><b>Static-Triple</b> aus ConnectionBuilder: jeder ActionSlot
///       hat (primary, secondary, tertiary) Verlinkung — das gibt die
///       grundlegende Topologie.</item>
///   <item><b>linked_handle</b> (+0x94 in Bubble-Object): direkter
///       next-Pointer pro Mechanismus.</item>
///   <item><b>Live-observed</b>: aus ZB-Durchlauf-Diffs (welche
///       Mechanismen incrementieren counter wenn ZB durchläuft).</item>
/// </list>
/// </summary>
public sealed record PathEdge(
    int FromMechanismId,
    int ToMechanismId,
    PathEdgeSource Source,
    /// <summary>Welche ZB-Eigenschaft löst diese Edge aus (1=Hair..4=Feet).
    /// Null wenn unconditional (z.B. Toggle/Passthrough).</summary>
    byte? ConditionalAttribute = null,
    /// <summary>True wenn diese Edge geht "wenn Match" (= ZB hat die richtige
    /// Eigenschaft), False wenn "wenn no-Match". Nur für Conditional-Mechanismen.</summary>
    bool? OnMatch = null,
    /// <summary>Optional: Anzahl Live-Beobachtungen die diese Edge bestätigen.
    /// Steigt mit jedem ZB-Durchlauf wo wir die Edge sehen.</summary>
    int LiveObservations = 0)
{
    public override string ToString()
    {
        string cond = ConditionalAttribute is byte a
            ? $" [if attr={a} {(OnMatch == true ? "match" : OnMatch == false ? "no-match" : "?")}]"
            : "";
        return $"{FromMechanismId} → {ToMechanismId} ({Source}{cond}, obs={LiveObservations})";
    }
}

/// <summary>Quelle einer Pfad-Edge.</summary>
public enum PathEdgeSource : byte
{
    /// <summary>Aus ConnectionBuilder-Triple (statisch, Engine-Topologie).</summary>
    StaticTriple,
    /// <summary>Aus Bubble.linked_handle (+0x94, statisch im Engine-Object).</summary>
    LinkedHandle,
    /// <summary>Live durch ZB-Durchlauf beobachtet (Position-Counter incrementiert).</summary>
    LiveObserved,
}

/// <summary>
/// Vollständiger Pfad-Graph für eine Bubblewonder-Runde. Container für
/// alle Edges, plus Helper für Pfad-Analyse.
/// </summary>
public sealed class PathGraph
{
    private readonly List<PathEdge> _edges;

    public PathGraph(IEnumerable<PathEdge> edges)
    {
        _edges = edges.ToList();
    }

    public IReadOnlyList<PathEdge> Edges => _edges;

    /// <summary>Alle Edges die aus einem bestimmten Mechanismus rausgehen.</summary>
    public IEnumerable<PathEdge> EdgesFrom(int mechanismId) =>
        _edges.Where(e => e.FromMechanismId == mechanismId);

    /// <summary>Alle Edges die in einen bestimmten Mechanismus reingehen.</summary>
    public IEnumerable<PathEdge> EdgesTo(int mechanismId) =>
        _edges.Where(e => e.ToMechanismId == mechanismId);

    /// <summary>Mechanismus-IDs ohne eingehende Edges = potenzielle Start-Punkte.</summary>
    public IEnumerable<int> Sources(int mechanismCount)
    {
        var hasIncoming = new HashSet<int>(_edges.Select(e => e.ToMechanismId));
        return Enumerable.Range(0, mechanismCount).Where(id => !hasIncoming.Contains(id));
    }

    /// <summary>Mechanismus-IDs ohne ausgehende Edges = potenzielle Endpunkte
    /// (Goal oder Trap).</summary>
    public IEnumerable<int> Sinks(int mechanismCount)
    {
        var hasOutgoing = new HashSet<int>(_edges.Select(e => e.FromMechanismId));
        return Enumerable.Range(0, mechanismCount).Where(id => !hasOutgoing.Contains(id));
    }
}

/// <summary>
/// Builder der einen <see cref="PathGraph"/> aus einem
/// <see cref="BubblewonderState"/> aufbaut. Kombiniert statische Quellen
/// (ConnectionBuilder, linked_handle) zu einer initialen Edge-Liste, die
/// später mit Live-Beobachtungen verfeinert werden kann.
/// </summary>
public static class PathGraphBuilder
{
    /// <summary>Baut den initialen Graph aus statischen Quellen.</summary>
    public static PathGraph BuildStatic(BubblewonderState state)
    {
        var edges = new List<PathEdge>();

        // Build hdr1A → mechanism-id mapping. Bubble hdr1A bei Diff 1 sind
        // 0x19, 0x1A, ... Sequenz, mappen direkt auf REGS-Record-Index 0..N-1.
        var hdr1aToMechanism = new Dictionary<ushort, int>();
        for (int i = 0; i < state.LiveBubbles.Count && i < state.Grid.Mechanisms.Count; i++)
        {
            var bubble = state.LiveBubbles[i];
            // Skip the special hdr1A=0x0002 entry (= score/exit, not a mechanism)
            if (bubble.HeaderId < 0x10) continue;
            hdr1aToMechanism[bubble.HeaderId] = i;
        }

        // Source 1: linked_handle (+0x94) per bubble — direct next-pointer
        foreach (var bubble in state.LiveBubbles)
        {
            if (!hdr1aToMechanism.TryGetValue(bubble.HeaderId, out var fromId)) continue;
            if (bubble.LinkedHandle == 0) continue;
            if (!hdr1aToMechanism.TryGetValue(bubble.LinkedHandle, out var toId)) continue;
            if (fromId == toId) continue;  // self-loop, skip
            edges.Add(new PathEdge(fromId, toId, PathEdgeSource.LinkedHandle));
        }

        // Source 2: ConnectionBuilder triples — primary→tertiary edges
        foreach (var conn in state.Connections)
        {
            if (conn.PrimaryHandle == 0) continue;
            if (!hdr1aToMechanism.TryGetValue(conn.PrimaryHandle, out var fromId)) continue;
            if (conn.TertiaryHandle.HasValue
                && hdr1aToMechanism.TryGetValue(conn.TertiaryHandle.Value, out var toId)
                && fromId != toId)
            {
                edges.Add(new PathEdge(fromId, toId, PathEdgeSource.StaticTriple));
            }
            if (conn.SecondaryHandle.HasValue
                && hdr1aToMechanism.TryGetValue(conn.SecondaryHandle.Value, out var toId2)
                && fromId != toId2)
            {
                edges.Add(new PathEdge(fromId, toId2, PathEdgeSource.StaticTriple));
            }
        }

        // Dedup: same (from, to, source) — keep one
        var deduped = edges
            .GroupBy(e => (e.FromMechanismId, e.ToMechanismId, e.Source))
            .Select(g => g.First())
            .ToList();

        return new PathGraph(deduped);
    }
}
