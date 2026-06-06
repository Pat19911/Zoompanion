namespace ZoombiniHelper;

/// <summary>
/// Die engine-zugewiesenen Handle-Werte (Node-Offset +0x20), die einen
/// Listen-Knoten als <b>echtes Zoombini-Objekt</b> markieren — im Gegensatz zu
/// Sprites, Bubble-Mechanismen (0x04188000) oder UI-Overlays. Für Pool-ZBs
/// kodiert der Wert zugleich, <b>wo</b> der ZB gerade ist.
///
/// <para><b>Anlass (2026-05-30):</b> diese Menge war über <see cref="PoolScanner"/>,
/// den Bubblewonder-Klassifikator und den Tracker dupliziert. Als ein bis dahin
/// ungesehener Wert auftauchte (<see cref="Parked"/> = 0x00008001, memdump-073530:
/// ein gerade auf die Insel verfrachteter ZB), musste er durch mehrere Dateien
/// nachgezogen werden und wurde an einer Stelle vergessen → Solver übersah den ZB.
/// Seitdem ist dies die <b>einzige Quelle</b>: ein neuer Wert wird nur hier ergänzt.</para>
///
/// <para><b>Konfidenz:</b> <see cref="Pool"/> ist live in 5 Puzzles verifiziert;
/// <see cref="Held"/>/<see cref="Launched"/> Disasm + live; <see cref="Parked"/>
/// einmal live beobachtet (2026-05-30) und per Byte-Parse bestätigt (+0x76=1,
/// Grid auf Zwischenstation 0x15). Die genaue Engine-Semantik der Bits ist als
/// Hypothese dokumentiert, nicht abschließend verifiziert.</para>
/// </summary>
public static class ZoombiniHandle
{
    /// <summary>Im Pool (verfügbar / off-grid). Verifiziert in 5 Puzzles.</summary>
    public const uint Pool = 0x00000001;

    /// <summary>Gerade hochgehoben (Drag): +0x20 erhält das 0x04001000-Bit.
    /// Disasm fn 0x00448FD7 (orig 0x00000001 → 0x04001001).</summary>
    public const uint Held = 0x04001001;

    /// <summary>Losgeschickt und <b>aktiv</b> auf dem Grid (das 0x04000000-Bit =
    /// in Bewegung / animiert ist gesetzt). Auch von Stone Rise genutzt.</summary>
    public const uint Launched = 0x04008001;

    /// <summary>Losgeschickt und <b>zur Ruhe gekommen</b> — <see cref="Launched"/>
    /// ohne das 0x04000000-„aktiv/bewegt"-Bit. In Bubblewonder: auf einer
    /// Zwischenstation/Insel geparkt. Erstmals beobachtet 2026-05-30.</summary>
    public const uint Parked = 0x00008001;

    /// <summary>Alle Handles, die ein echtes ZB-Objekt markieren. Das ist die
    /// Menge, die der <see cref="PoolScanner"/> erfasst. (Attribut-Validierung
    /// h/e/n/f ∈ 1..5 schützt zusätzlich vor Fehl-Erfassung fremder Objekte.)</summary>
    public static readonly uint[] All = { Pool, Held, Launched, Parked };

    /// <summary>Ist der ZB losgeschickt und steht auf dem Grid (aktiv ODER
    /// geparkt)? Relevant für die Insel-/Pfad-Klassifikation.</summary>
    public static bool IsOnGrid(uint handle) => handle == Launched || handle == Parked;
}
