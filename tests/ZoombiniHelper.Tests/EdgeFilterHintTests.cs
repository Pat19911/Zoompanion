using Xunit;
using ZoombiniHelper.Bubblewonder;

namespace ZoombiniHelper.Tests;

/// <summary>Tests für EdgeFilterHint — die aus Live-Beobachtung abgeleitete
/// Filter-Bedingung pro Edge im Bubblewonder-Grid.</summary>
public class EdgeFilterHintTests
{
    [Fact]
    public void IsEmpty_AllZero()
    {
        var h = new EdgeFilterHint(0, 0, 0, 0, 0);
        Assert.True(h.IsEmpty);
        Assert.True(h.Accepts(1, 2, 3, 4));   // wildcard accepts everything
    }

    [Fact]
    public void Accepts_RequiresAllSetAttributesMatch()
    {
        var h = new EdgeFilterHint(Hair: 0, Eyes: 1, Nose: 0, Feet: 4, ObservedZbCount: 2);
        Assert.True(h.Accepts(5, 1, 5, 4));
        Assert.True(h.Accepts(3, 1, 5, 4));
        Assert.False(h.Accepts(5, 2, 5, 4));   // Eyes wrong
        Assert.False(h.Accepts(5, 1, 5, 3));   // Feet wrong
    }

    [Fact]
    public void ToString_FormatsActiveAttributes()
    {
        Assert.Equal("E=1,F=4", new EdgeFilterHint(0, 1, 0, 4, 2).ToString());
        Assert.Equal("H=3", new EdgeFilterHint(3, 0, 0, 0, 1).ToString());
        Assert.Equal("*", new EdgeFilterHint(0, 0, 0, 0, 0).ToString());
        Assert.Equal("H=1,E=2,N=3,F=4", new EdgeFilterHint(1, 2, 3, 4, 5).ToString());
    }
}
