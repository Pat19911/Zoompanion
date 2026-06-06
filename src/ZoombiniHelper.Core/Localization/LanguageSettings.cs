namespace ZoombiniHelper.Localization;

/// <summary>
/// Persists the chosen UI language in a tiny text file next to the EXE
/// (<c>zoombini-helper-lang.txt</c>, same convention as
/// <c>bubblewonder-spawns.txt</c>). The startup picker is shown only when no
/// language has been remembered yet; afterwards the saved choice is loaded
/// silently. Best-effort: any IO failure falls back to "not set".
/// </summary>
public static class LanguageSettings
{
    public const string FileName = "zoombini-helper-lang.txt";

    /// <summary>Directory the settings file lives in — the real on-disk EXE
    /// folder, NOT the single-file extraction temp dir.</summary>
    public static string DefaultDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? AppContext.BaseDirectory;

    private static string PathIn(string dir) => Path.Combine(dir, FileName);

    /// <summary>Read the remembered language, or null if none is stored yet
    /// (→ the picker should be shown).</summary>
    public static Language? Load(string? directory = null)
    {
        try
        {
            string path = PathIn(directory ?? DefaultDirectory);
            if (!File.Exists(path)) return null;
            string code = File.ReadAllText(path).Trim();
            if (code.Length == 0) return null;
            return LanguageInfo.FromCode(code);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Persist the chosen language. Silent on failure.</summary>
    public static void Save(Language lang, string? directory = null)
    {
        try
        {
            File.WriteAllText(PathIn(directory ?? DefaultDirectory), lang.Code());
        }
        catch
        {
            /* best-effort */
        }
    }
}
