using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ZoombiniHelper.Localization;

/// <summary>
/// Central string lookup for the helper's player-facing UI. Keys map to
/// templates per language, loaded from embedded JSON resources
/// (Localization/Resources/{code}.json). German is the baseline and the
/// fallback for any key missing in another language.
///
/// <para>Usage: <c>Loc.T("cliff.title", DifficultyLabel(d))</c>. Templates use
/// indexed placeholders (<c>{0}</c>, <c>{1}</c>) and are filled with
/// <see cref="CultureInfo.InvariantCulture"/> so number formatting stays
/// predictable regardless of the chosen language.</para>
///
/// <para>Scope: only player-facing UI is localized. Diagnostics (the
/// Bubblewonder plan-log, the F12 memory dump) stay German by design and do
/// NOT go through this class.</para>
/// </summary>
public static class Loc
{
    private static volatile Language _current = Language.German;

    /// <summary>The active language. Set once at startup (before the overlay is
    /// built); changing it at runtime is supported and takes effect on the next
    /// per-tick render, since renderers rebuild their text every tick.</summary>
    public static Language Current
    {
        get => _current;
        set => _current = value;
    }

    // Per-language key→template tables, loaded lazily and cached.
    private static readonly ConcurrentDictionary<Language, IReadOnlyDictionary<string, string>> _tables = new();

    /// <summary>Look up <paramref name="key"/> in the current language, falling
    /// back to German, then to the key itself (so a missing key is visible in
    /// the UI rather than throwing). Indexed placeholders are filled from
    /// <paramref name="args"/> with invariant formatting.</summary>
    public static string T(string key, params object?[] args)
    {
        string template = Lookup(_current, key) ?? Lookup(Language.German, key) ?? key;
        if (args is null || args.Length == 0) return template;
        try { return string.Format(CultureInfo.InvariantCulture, template, args); }
        catch (FormatException) { return template; }  // malformed template → show as-is
    }

    /// <summary>True if <paramref name="key"/> exists in the German baseline.
    /// Used by tests and to decide whether a string is localizable at all.</summary>
    public static bool Has(string key) => Lookup(Language.German, key) != null;

    /// <summary>All keys defined in a given language's table (for tests).</summary>
    public static IReadOnlyCollection<string> Keys(Language lang) =>
        (IReadOnlyCollection<string>)Table(lang).Keys;

    private static string? Lookup(Language lang, string key) =>
        Table(lang).TryGetValue(key, out var v) ? v : null;

    private static IReadOnlyDictionary<string, string> Table(Language lang) =>
        _tables.GetOrAdd(lang, Load);

    private static IReadOnlyDictionary<string, string> Load(Language lang)
    {
        string code = lang.Code();
        var asm = typeof(Loc).Assembly;
        // Match by suffix so we don't depend on the exact RootNamespace prefix.
        string? resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".{code}.json", StringComparison.OrdinalIgnoreCase));
        if (resName is null)
            return new Dictionary<string, string>();

        using Stream? stream = asm.GetManifestResourceStream(resName);
        if (stream is null)
            return new Dictionary<string, string>();

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            return dict ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
