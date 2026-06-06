namespace ZoombiniHelper.Bubblewonder;

/// <summary>
/// Klassifikation eines Bubblewonder-Mechanismus auf dem Grid.
///
/// <para><b>Fundamentale Constraint (live-bestätigt):</b> jeder Mechanismus
/// kann nur durch einen Zoombini aktiviert werden, der über ihn fährt. Es
/// gibt keinen direkten "Schalter drücken"-Input des Spielers — alle
/// Zustands-Änderungen kommen über ZB-Durchläufe. Das macht Reihenfolge-
/// Planung kritisch: jede gewünschte Schalter-Position kostet einen ZB.</para>
///
/// <para>Hypothese: f0 (Wert 1..6) im REGS-Record kodiert diesen Typ.
/// Wird in Task 3 (Mechanismus-Typ-Klassifizierung) verifiziert.</para>
/// </summary>
public enum MechanismType : byte
{
    /// <summary>Typ noch nicht klassifiziert (Default für rohe Records).</summary>
    Unknown = 0,

    /// <summary>Reine Durchgangs-Bubble — kein Routing-Effekt, kein State-Wechsel.
    /// In der Praxis bisher nicht im REGS-Layout beobachtet (f0=1 ist Trap, nicht Passthrough).
    /// Bleibt im Enum für mögliche Zukunfts-Klassifikationen.</summary>
    Passthrough,

    /// <summary>Routing-Verschieber: lenkt jeden Zoombini in eine feste Richtung
    /// (links / rechts / oben / unten). Kein State-Wechsel.</summary>
    StaticDeflector,

    /// <summary>Toggle-Verschieber: alterniert zwischen zwei Zuständen bei jedem
    /// durchlaufenden Zoombini. Routing wechselt damit auch.</summary>
    Toggle,

    /// <summary>Conditional-Verschieber: prüft eine ZB-Eigenschaft (Hair/Eyes/Nose/Feet)
    /// und routet je nach Match anders. ZB-Attribut entscheidet über Pfad.</summary>
    Conditional,

    /// <summary>Switch-aktivierter Mechanismus: ändert seinen Zustand nur, wenn
    /// ein anderer (verlinkter) Schalter im richtigen Zustand ist. Sonst statisch.
    /// Verbindungs-Topologie über <c>LinkedSlotIds</c>.</summary>
    SwitchActivated,

    /// <summary>Goal/Sink: Zoombini hat es geschafft. Kein State-Wechsel,
    /// Zoombini wird aus dem Pool entfernt. Entspricht Zelltyp 0x17 in der
    /// engine-eigenen Zelltyp-Tabelle (oben-rechts = Ziel-Steininsel).</summary>
    Goal,

    /// <summary>Nicht-Ziel-Steinbereich: Start-Insel (unten-links, Zelltyp 0x14)
    /// oder Zwischenstation (oben-links 0x15 / unten-rechts 0x16). Ein ZB der hier
    /// landet ist aus dem Rennen (kein Score). Quelle: engine-eigene Zelltyp-Tabelle
    /// (0x499efc), Schreiber fn 0x425f30 setzt +0x76 := celltype-0x14.</summary>
    StoneArea,

    /// <summary>Trap/Loss: Zoombini geht verloren (z.B. Strudel, Sackgasse).
    /// ZB ist endgültig weg. Auf hohen Schwierigkeitsgraden vermeiden.</summary>
    Trap,

    /// <summary>Sticky/Festklebefeld (f0=5): Sternchen-Cell mit Color-Channel.
    /// ZB der drüber läuft klebt fest. Befreiung durch zweiten ZB der entweder
    /// direkt drauf läuft (= schubst weg) oder über die gleichfarbige Pair-Cell.
    /// Color-Channel in REGS f3.</summary>
    Sticky,

    /// <summary>Trigger/Umswitcher (f0=6): blaue Cell die beim Betreten einen
    /// entfernten Switch umstellt. Target-Switch-Handle in Bubble-Bytes +0x166.
    /// ZB läuft hindurch ohne Direction-Effekt (passthrough), aber Side-Effect
    /// auf den Target-Switch.</summary>
    Trigger,
}
