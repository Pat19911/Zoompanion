using ZoombiniHelper;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Tests for the bridge-recommendation logic. These cover the rule
/// encoding (single + double-nibble) and the bridge-label flip — the two
/// places where wrong values would produce a wrong bridge recommendation.
/// </summary>
public class CliffStateTests
{
    [Fact]
    public void Rule_LowNibbleOnly_MatchesSingleVariant()
    {
        // Diff 0: n_allerg=1, value=0x05 = "Nose variant 5" only.
        var rule = new CliffState.Rule(Type: 3, Value: 0x05);
        Assert.Equal(5, rule.LowVariant);
        Assert.Equal(0, rule.HighVariant);
        Assert.True(rule.Matches(5));
        Assert.False(rule.Matches(4));
        Assert.False(rule.Matches(0));
    }

    [Fact]
    public void Rule_HighNibbleSet_MatchesEitherVariant()
    {
        // Diff 1: n_allerg=2, value=0x35 = "Nose variant 3 OR variant 5".
        // (Live-observed encoding: 2026-04-26 cliff session.)
        var rule = new CliffState.Rule(Type: 3, Value: 0x35);
        Assert.Equal(5, rule.LowVariant);
        Assert.Equal(3, rule.HighVariant);
        Assert.True(rule.Matches(5));
        Assert.True(rule.Matches(3));
        Assert.False(rule.Matches(4));
    }

    [Fact]
    public void Rule_HighNibbleZero_DoesNotMatchVariantZero()
    {
        // High nibble is "no second variant" sentinel — must NOT match variant 0.
        var rule = new CliffState.Rule(Type: 3, Value: 0x05);
        Assert.False(rule.Matches(0));
    }

    [Fact]
    public void BridgeLabels_WhichCliff0_LowerAccepts()
    {
        var cliff = MakeCliff(which: 0);
        Assert.Equal("UNTERE Brücke", cliff.AcceptingBridgeLabel);
        Assert.Equal("OBERE Brücke",  cliff.RejectingBridgeLabel);
    }

    [Fact]
    public void BridgeLabels_WhichCliff1_UpperAccepts()
    {
        var cliff = MakeCliff(which: 1);
        Assert.Equal("OBERE Brücke",  cliff.AcceptingBridgeLabel);
        Assert.Equal("UNTERE Brücke", cliff.RejectingBridgeLabel);
    }

    [Fact]
    public void IsActive_NoRules_ReturnsFalse()
    {
        var cliff = MakeCliff();
        Assert.False(cliff.IsActive);
    }

    [Fact]
    public void IsActive_WithRules_ReturnsTrue()
    {
        var cliff = MakeCliff(rules: [new CliffState.Rule(Type: 3, Value: 0x05)]);
        Assert.True(cliff.IsActive);
    }

    /// <summary>Construct a CliffState directly via the private constructor
    /// (visible via InternalsVisibleTo). We don't go through Read() because
    /// it needs a live IMemoryReader.</summary>
    private static CliffState MakeCliff(
        IReadOnlyList<CliffState.Rule>? rules = null,
        ushort which = 0,
        byte nAllerg = 1,
        ushort attempts = 0,
        int difficulty = 1)
    {
        var ctor = typeof(CliffState).GetConstructors(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
        return (CliffState)ctor.Invoke(new object?[] {
            nAllerg, attempts, which, difficulty, rules ?? Array.Empty<CliffState.Rule>()
        });
    }
}
