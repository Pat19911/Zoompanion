using System;
using System.Windows.Forms;
using ZoombiniHelper.Localization;
using ZoombiniHelper.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Language selection: load the remembered choice, or — on first run —
        // show the picker once and persist the result. Must happen BEFORE the
        // overlay is constructed so every title/string resolves in the chosen
        // language from the very first tick.
        var remembered = LanguageSettings.Load();
        if (remembered is { } lang)
        {
            Loc.Current = lang;
        }
        else
        {
            using var picker = new LanguageSelectionForm(Language.German);
            picker.ShowDialog();
            Loc.Current = picker.Selected;
            LanguageSettings.Save(picker.Selected);
        }

        Application.Run(new HelperOverlay());
    }
}
