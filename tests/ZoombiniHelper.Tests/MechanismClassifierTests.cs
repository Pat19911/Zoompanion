using Xunit;
using ZoombiniHelper.Bubblewonder;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Tests für MechanismClassifier. Verifiziert die Cell-Type-Klassifikation
/// gegen User-bestätigte Beobachtungen 2026-05-01:
///   - F0=1 → Trap (gelbe Killer-Cells, Diff 1 Dump)
///   - F0=4 → SwitchActivated (rote umschaltende Cell)
///   - F0=2/3 + F4-F7 one-hot → StaticDeflector (Pfeil mit Direction)
///   - F0=2/3 + F9=1 → Conditional (Filter-Cell wie "wenn Rollschuhe")
/// </summary>
public class MechanismClassifierTests
{
    [Fact]
    public void Classify_F0_1_NoFlags_ReturnsTrap()
    {
        // f0=1 mit allen Folgebytes 0 = Trap/Killer (gelb im Spiel-UI).
        var r = new RegsRecord(1, 5, 12, 0, 0, 0, 0, 0, 0, 0);
        Assert.Equal(MechanismType.Trap, MechanismClassifier.Classify(r));
    }

    [Fact]
    public void Classify_F0_3_WithDirection_ReturnsStaticDeflector()
    {
        // Mech [6] (9,7) raw=3,9,7,1,1,0,0,0,0,0 — User: unconditional Pfeil nach OBEN
        var r = new RegsRecord(3, 9, 7, 1, 1, 0, 0, 0, 0, 0);
        Assert.Equal(MechanismType.StaticDeflector, MechanismClassifier.Classify(r));
        Assert.Equal(ArrowDirection.Up, r.Direction);
        Assert.False(r.IsConditional);
    }

    [Fact]
    public void Classify_F0_3_RightArrow_ReturnsStaticDeflector()
    {
        // Mech [4] (9,2) raw=3,9,2,1,0,1,0,0,1,0 — User: unconditional Pfeil nach RECHTS
        var r = new RegsRecord(3, 9, 2, 1, 0, 1, 0, 0, 1, 0);
        Assert.Equal(MechanismType.StaticDeflector, MechanismClassifier.Classify(r));
        Assert.Equal(ArrowDirection.Right, r.Direction);
        Assert.False(r.IsConditional);
    }

    [Fact]
    public void Classify_Conditional_F9Set_ReturnsConditional()
    {
        // Mech [16] (7,4) raw=2,7,4,1,0,0,1,0,2,1 — User: Conditional "wenn Rollschuhe"
        // F9=1 als einziger Outlier in REGS = Conditional-Marker.
        var r = new RegsRecord(2, 7, 4, 1, 0, 0, 1, 0, 2, 1);
        Assert.Equal(MechanismType.Conditional, MechanismClassifier.Classify(r));
        Assert.True(r.IsConditional);
        Assert.Equal(ArrowDirection.Down, r.Direction);  // F6=1 → unten
        Assert.Equal((byte)2, r.ConditionalAttribute);   // F8=2 als Attribut-Index
    }

    [Fact]
    public void Classify_F0_4_NotConditional_ReturnsSwitchActivated()
    {
        var r = new RegsRecord(4, 4, 8, 4, 0, 0, 0, 0, 0, 0);
        Assert.Equal(MechanismType.SwitchActivated, MechanismClassifier.Classify(r));
        Assert.False(r.IsConditional);
    }

    [Fact]
    public void Classify_F0_5_ReturnsSticky()
    {
        var r = new RegsRecord(5, 7, 6, 5, 0, 0, 0, 0, 0, 0);
        Assert.Equal(MechanismType.Sticky, MechanismClassifier.Classify(r));
    }

    [Fact]
    public void Classify_F0_6_ReturnsTrigger()
    {
        var r = new RegsRecord(6, 1, 5, 4, 0, 0, 0, 0, 0, 0);
        Assert.Equal(MechanismType.Trigger, MechanismClassifier.Classify(r));
    }

    [Fact]
    public void Classify_F0_Unknown_ReturnsUnknown()
    {
        var r = new RegsRecord(7, 0, 0, 0, 0, 0, 0, 0, 0, 0);  // out of [1..6]
        Assert.Equal(MechanismType.Unknown, MechanismClassifier.Classify(r));
    }

    [Fact]
    public void Direction_Mapping_AllFour()
    {
        Assert.Equal(ArrowDirection.Up,    new RegsRecord(3, 0, 0, 1, 1, 0, 0, 0, 0, 0).Direction);
        Assert.Equal(ArrowDirection.Right, new RegsRecord(3, 0, 0, 1, 0, 1, 0, 0, 1, 0).Direction);
        Assert.Equal(ArrowDirection.Down,  new RegsRecord(3, 0, 0, 1, 0, 0, 1, 0, 2, 0).Direction);
        Assert.Equal(ArrowDirection.Left,  new RegsRecord(3, 0, 0, 1, 0, 0, 0, 1, 3, 0).Direction);
    }

    [Fact]
    public void IsActiveMechanism_F0_1_IsPassive()
    {
        Assert.False(MechanismClassifier.IsActiveMechanism(1));
        for (byte f0 = 2; f0 <= 6; f0++)
            Assert.True(MechanismClassifier.IsActiveMechanism(f0), $"f0={f0} should be active");
    }
}
