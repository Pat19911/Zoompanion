# v2 Cliff-Allergie Wert-Mapping

> **WICHTIGE ERKENNTNIS (2026-04-26)**: das Mapping „Bit-Position → Variante"
> ist **NICHT statisch**, sondern wird **pro Spielstart zufällig neu
> generiert**. Es kann also keine globale Tabelle aufgebaut werden, die über
> mehrere Spielstarts hinweg gültig ist.
>
> Innerhalb einer Spielsession ist das Mapping aber konstant — der Spieler
> kann an der ersten Klippe einer Session die Zuordnung lernen und sie für
> die restliche Session anwenden.
>
> Beobachtung die das beweist: Bei zwei verschiedenen Spielstarts zeigte
> derselbe Memory-Wert (`val=5` für Nase) zwei völlig verschiedene
> Allergien — einmal {Orange, Rot}, das andere Mal {Blau}.
>
> Memory-Adressen (Live-Verified):
> - allergy_type[0] @ `0x004945B5` (1=Hair, 2=Eyes, 3=Nose, 4=Feet)
> - allergy_value[0] @ `0x004945BA` (Wertebereich noch unklar — bisher 4 und 5 gesehen)

## v1-Referenz (nur als Vergleich)

| Attribut | v1 Werte (0..4) |
|----------|------------------|
| Hair | 0=Spiked, 1=Ponytail, 2=Green Cap, 3=Straight, 4=Balding |
| Eyes | 0=Brown, 1=One-eye, 2=Sleepy, 3=Spectacles, 4=Sunglasses |
| Nose | 0=Green, 1=Orange, 2=Red, 3=Purple, 4=Blue |
| Feet | 0=Shoes, 1=Skates, 2=Wheels, 3=Propeller, 4=Springs |

## v2 Beobachtungen (live aus dem Spiel)

### Hair (Type 1) — Bitfield-Encoding
| Bit | Wert (2^bit) | Variante im Spiel |
|-----|--------------|-------------------|
| Bit 0 | 1 | **flach/glatt (eines davon)** ← Live 2026-04-26 (val=3 Test, ungemischte Gruppe) |
| Bit 1 | 2 | **flach/glatt (eines davon)** ← Live 2026-04-26 (val=3 Test) |
| Bit 2 | 4 | ? |
| Bit 3 | 8 | ? |
| Bit 4 | 16 | ? |

**Disambiguation noch offen**: bei `val=3` (Bits 0+1) gingen nur „flache/glatte
Haar"-Zoombinis unten durch. Welcher der beiden Bits welche der zwei
flach/glatt-Varianten ist (z. B. Straight vs. Balding aus v1), braucht eine
Klippen-Konfiguration mit nur einem Bit gesetzt (z. B. val=1 oder val=2).

### Eyes (Type 2) — Bitfield-Encoding
| Bit | Wert (2^bit) | Variante im Spiel |
|-----|--------------|-------------------|
| Bit 0 | 1 | **Große Augen (Brown/normal)** ← Live-bestätigt 2026-04-26 |
| Bit 1 | 2 | ? |
| Bit 2 | 4 | **Brille (Spectacles)** ← Live-bestätigt 2026-04-26 |
| Bit 3 | 8 | ? |
| Bit 4 | 16 | ? |

### Nose (Type 3) — Bitfield-Encoding bestätigt (Bit-Zuordnung KORRIGIERT)
| Bit | Wert (2^bit) | Variante im Spiel |
|-----|--------------|-------------------|
| Bit 0 | 1 | **ROT** ← Live-bestätigt 2026-04-26 (val=3 Test mit nur Rot durchgelassen) |
| Bit 1 | 2 | ? (war in Test-Gruppe nicht vertreten) |
| Bit 2 | 4 | **ORANGE** ← Live-bestätigt 2026-04-26 (val=5 = Bits 0+2 = Rot+Orange) |
| Bit 3 | 8 | ? (mögliche Kandidaten: Grün, Lila, Blau) |
| Bit 4 | 16 | ? |

Logik:
- val=5 (Bits 0+2) → UNTEN ließ Rot+Orange durch → Bits 0,2 = {Rot, Orange}
- val=3 (Bits 0+1) → UNTEN ließ NUR Rot durch → Bit 0 = Rot
- → daher Bit 2 = Orange (durch Ausschluss aus val=5)

### Feet (Type 4)
| Wert | Variante im Spiel |
|------|-------------------|
| 1 | ? |
| 2 | ? |
| 3 | ? |
| 4 | ? |
| 5 | ? |

## Encoding (Live-bestätigt)

**`allergy_value` ist ein Bitfield über 5 Bits** — jedes Bit steht für eine
Variante. Mehrere Bits gleichzeitig gesetzt = Klippe akzeptiert mehrere
Varianten. Beispiel: `val=5 = 00101` = Bit 0 + Bit 2 = zwei Varianten.

**Cheat-Print-Semantik (wichtig!)**: „Lower bridge accepts: …" bedeutet
**Lower akzeptiert NUR die genannten Varianten**, alle anderen müssen über
die andere Brücke. NICHT „Lower niest bei den genannten" — das war ein Bug
im Tool, jetzt korrigiert.

## Bisheriger Memory-Mitschnitt

Aus dem Diagnose-Log:
- Snap 1: type=3 (Nose), value=5 → User sagt: Rot
- Snap 3+: type=2 (Eyes), value=4 → visuell unbekannt

→ Brauche noch mind. 1 Beobachtung pro Attribut, um Encoding zu fixieren.
