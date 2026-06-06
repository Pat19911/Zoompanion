using System.Text.RegularExpressions;
using ZoombiniHelper.Localization;

namespace ZoombiniHelper.Tests;

/// <summary>
/// Guards the localization layer: every translation table must carry exactly
/// the German baseline's keys with matching placeholders, and lookups must
/// resolve per language with a German→key fallback chain.
/// </summary>
public class LocalizationTests
{
    private static readonly Language[] Translations =
        { Language.English, Language.French, Language.Spanish, Language.Italian };

    [Fact]
    public void EveryLanguage_HasExactlyTheGermanKeySet()
    {
        var german = Loc.Keys(Language.German).ToHashSet();
        Assert.NotEmpty(german);
        foreach (var lang in Translations)
        {
            var keys = Loc.Keys(lang).ToHashSet();
            var missing = german.Except(keys).OrderBy(k => k).ToList();
            var extra   = keys.Except(german).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0, $"{lang}: fehlende Keys: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0,   $"{lang}: zusätzliche Keys: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void EveryLanguage_PreservesPlaceholders()
    {
        var saved = Loc.Current;
        try
        {
            foreach (var key in Loc.Keys(Language.German))
            {
                Loc.Current = Language.German;
                var dePh = Placeholders(Loc.T(key));
                foreach (var lang in Translations)
                {
                    Loc.Current = lang;
                    var ph = Placeholders(Loc.T(key));
                    Assert.True(dePh.SequenceEqual(ph),
                        $"{lang}/{key}: Platzhalter [{string.Join(",", ph)}] != de [{string.Join(",", dePh)}]");
                }
            }
        }
        finally { Loc.Current = saved; }
    }

    [Fact]
    public void BridgeAbbreviations_AreDistinctPerLanguage()
    {
        var saved = Loc.Current;
        try
        {
            foreach (var lang in new[] { Language.German }.Concat(Translations))
            {
                Loc.Current = lang;
                Assert.NotEqual(Loc.T("bridge.upper.abbr"), Loc.T("bridge.lower.abbr"));
            }
        }
        finally { Loc.Current = saved; }
    }

    [Fact]
    public void T_ResolvesLanguageSpecificValue()
    {
        var saved = Loc.Current;
        try
        {
            Loc.Current = Language.German;  Assert.Equal("Haare",   Loc.T("attr.hair"));
            Loc.Current = Language.English; Assert.Equal("Hair",    Loc.T("attr.hair"));
            Loc.Current = Language.French;  Assert.Equal("Cheveux", Loc.T("attr.hair"));
            Loc.Current = Language.Spanish; Assert.Equal("Pelo",    Loc.T("attr.hair"));
            Loc.Current = Language.Italian; Assert.Equal("Capelli", Loc.T("attr.hair"));
        }
        finally { Loc.Current = saved; }
    }

    [Fact]
    public void T_UnknownKey_ReturnsKeyItself()
    {
        var saved = Loc.Current;
        try
        {
            Loc.Current = Language.French;
            Assert.Equal("__does.not.exist__", Loc.T("__does.not.exist__"));
        }
        finally { Loc.Current = saved; }
    }

    [Fact]
    public void T_FormatsIndexedArguments()
    {
        var saved = Loc.Current;
        try
        {
            Loc.Current = Language.German;
            Assert.Contains("7", Loc.T("cliff.attempts", 7));
            // Multiple args land in their indexed slots.
            Assert.Contains("3", Loc.T("bubble.grid", 3, 4, 5));
            Assert.Contains("5", Loc.T("bubble.grid", 3, 4, 5));
        }
        finally { Loc.Current = saved; }
    }

    [Fact]
    public void FromCode_RoundTripsThroughLanguageCodes()
    {
        foreach (var lang in new[] { Language.German }.Concat(Translations))
            Assert.Equal(lang, LanguageInfo.FromCode(lang.Code()));
        Assert.Equal(Language.German, LanguageInfo.FromCode("xx"));  // unknown → German
    }

    private static List<string> Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)\}").Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();
}
