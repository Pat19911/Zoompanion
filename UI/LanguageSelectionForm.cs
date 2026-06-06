using System.Drawing;
using System.Windows.Forms;
using ZoombiniHelper.Localization;

namespace ZoombiniHelper.UI;

/// <summary>
/// One-time startup picker: lets the player choose the helper's UI language.
/// Shown only on first run (no remembered choice); the selection is persisted
/// by the caller. Each language is offered in its own native name so it's
/// recognizable regardless of the current language.
/// </summary>
public sealed class LanguageSelectionForm : Form
{
    /// <summary>The language the user picked (German if the dialog is closed
    /// without an explicit choice).</summary>
    public Language Selected { get; private set; } = Language.German;

    public LanguageSelectionForm(Language preselect)
    {
        Selected = preselect;

        Text = "Zoombini Helper";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        BackColor = Color.FromArgb(18, 22, 36);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(320, 90 + LanguageInfo.All.Length * 44);

        var prompt = new Label
        {
            Text = Loc.T("lang.picker.prompt"),
            ForeColor = Color.FromArgb(140, 220, 255),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(16, 16, ClientSize.Width - 32, 40),
        };
        Controls.Add(prompt);

        int y = 64;
        foreach (var lang in LanguageInfo.All)
        {
            var btn = new Button
            {
                Text = lang.NativeName(),
                Tag = lang,
                Bounds = new Rectangle(40, y, ClientSize.Width - 80, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = lang == preselect ? Color.FromArgb(40, 70, 110) : Color.FromArgb(30, 38, 58),
                ForeColor = Color.WhiteSmoke,
                Cursor = Cursors.Hand,
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 110, 150);
            btn.Click += (_, _) =>
            {
                Selected = (Language)btn.Tag!;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btn);
            y += 44;
        }

        // Enter/Esc both confirm the preselected language so the dialog is fast.
        AcceptButton = null;
    }

    protected override bool ShowWithoutActivation => false;
}
