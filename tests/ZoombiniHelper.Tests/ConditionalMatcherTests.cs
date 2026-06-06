using Xunit;
using ZoombiniHelper;
using ZoombiniHelper.Bubblewonder;

namespace ZoombiniHelper.Tests;

/// <summary>Tests für den Live-Conditional-Matcher.
/// Quelle: <c>FUN_0044A920</c> in v2 PE32, Filter-Configuration aus Engine-Object
/// bei <c>+0xF0..+0xF3</c>. Match-Regel: für jedes Filter-Byte mit Wert &gt; 0
/// muss ZB-Attribut gleich sein.</summary>
public class ConditionalMatcherTests
{
    private static PoolMember MakeZb(byte hair, byte eyes, byte nose, byte feet) =>
        new PoolMember(Address: 0, Hair: hair, Eyes: eyes, Nose: nose, Feet: feet,
                       YPosition: 0, SpriteId: 0);

    [Fact]
    public void EmptyFilter_ReturnsWildcard()
    {
        var filter = new FilterConfig(0, 0, 0, 0);
        Assert.Equal(ConditionalMatcher.MatchResult.Wildcard,
            ConditionalMatcher.CheckMatch(filter, MakeZb(1, 1, 1, 1)));
    }

    [Fact]
    public void SingleAttributeFilter_MatchesCorrectVariant()
    {
        // Filter verlangt Hair=3, andere irrelevant
        var filter = new FilterConfig(Hair: 3, Eyes: 0, Nose: 0, Feet: 0);
        Assert.Equal(ConditionalMatcher.MatchResult.Match,
            ConditionalMatcher.CheckMatch(filter, MakeZb(3, 1, 1, 1)));
        Assert.Equal(ConditionalMatcher.MatchResult.Match,
            ConditionalMatcher.CheckMatch(filter, MakeZb(3, 5, 5, 5)));
        Assert.Equal(ConditionalMatcher.MatchResult.NoMatch,
            ConditionalMatcher.CheckMatch(filter, MakeZb(2, 1, 1, 1)));
    }

    [Fact]
    public void MultiAttributeFilter_RequiresAllMatch()
    {
        // Filter verlangt Hair=2 UND Feet=4
        var filter = new FilterConfig(Hair: 2, Eyes: 0, Nose: 0, Feet: 4);
        Assert.Equal(ConditionalMatcher.MatchResult.Match,
            ConditionalMatcher.CheckMatch(filter, MakeZb(2, 5, 5, 4)));
        Assert.Equal(ConditionalMatcher.MatchResult.NoMatch,
            ConditionalMatcher.CheckMatch(filter, MakeZb(2, 1, 1, 5)));   // Feet falsch
        Assert.Equal(ConditionalMatcher.MatchResult.NoMatch,
            ConditionalMatcher.CheckMatch(filter, MakeZb(3, 1, 1, 4)));   // Hair falsch
    }

    [Fact]
    public void FullFilter_AllFourMustMatch()
    {
        var filter = new FilterConfig(1, 2, 3, 4);
        Assert.Equal(ConditionalMatcher.MatchResult.Match,
            ConditionalMatcher.CheckMatch(filter, MakeZb(1, 2, 3, 4)));
        Assert.Equal(ConditionalMatcher.MatchResult.NoMatch,
            ConditionalMatcher.CheckMatch(filter, MakeZb(1, 2, 3, 5)));
    }

    [Fact]
    public void DescribeResult_ReturnsExpectedSymbols()
    {
        Assert.Equal("*", ConditionalMatcher.DescribeResult(ConditionalMatcher.MatchResult.Wildcard));
        Assert.Equal("✓", ConditionalMatcher.DescribeResult(ConditionalMatcher.MatchResult.Match));
        Assert.Equal("✗", ConditionalMatcher.DescribeResult(ConditionalMatcher.MatchResult.NoMatch));
    }

    [Fact]
    public void FilterConfig_ActiveAttributeCount()
    {
        Assert.Equal(0, new FilterConfig(0, 0, 0, 0).ActiveAttributeCount);
        Assert.Equal(1, new FilterConfig(3, 0, 0, 0).ActiveAttributeCount);
        Assert.Equal(2, new FilterConfig(3, 0, 0, 5).ActiveAttributeCount);
        Assert.Equal(4, new FilterConfig(1, 2, 3, 4).ActiveAttributeCount);
    }

    [Fact]
    public void FilterConfig_IsEmpty()
    {
        Assert.True(new FilterConfig(0, 0, 0, 0).IsEmpty);
        Assert.False(new FilterConfig(1, 0, 0, 0).IsEmpty);
    }
}
