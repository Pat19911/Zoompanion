namespace ZoombiniHelper.Localization;

/// <summary>
/// Supported UI languages. German is the canonical baseline (the source the
/// other tables are translated from and the fallback for any missing key).
/// </summary>
public enum Language
{
    German,
    English,
    French,
    Spanish,
    Italian,
}

/// <summary>Metadata for a language: the resource-file code and a native
/// display name shown in the startup picker.</summary>
public static class LanguageInfo
{
    /// <summary>Lowercase resource code, e.g. "de" → Localization/Resources/de.json.</summary>
    public static string Code(this Language lang) => lang switch
    {
        Language.German  => "de",
        Language.English => "en",
        Language.French  => "fr",
        Language.Spanish => "es",
        Language.Italian => "it",
        _ => "de",
    };

    /// <summary>Native name for the picker — each language in its own tongue.</summary>
    public static string NativeName(this Language lang) => lang switch
    {
        Language.German  => "Deutsch",
        Language.English => "English",
        Language.French  => "Français",
        Language.Spanish => "Español",
        Language.Italian => "Italiano",
        _ => lang.ToString(),
    };

    /// <summary>All languages in display order.</summary>
    public static readonly Language[] All =
    {
        Language.German, Language.English, Language.French,
        Language.Spanish, Language.Italian,
    };

    /// <summary>Parse a resource code back to a language; German on unknown.</summary>
    public static Language FromCode(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "de" => Language.German,
        "en" => Language.English,
        "fr" => Language.French,
        "es" => Language.Spanish,
        "it" => Language.Italian,
        _ => Language.German,
    };
}
