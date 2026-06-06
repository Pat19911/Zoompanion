using System.Drawing;
using System.Windows.Forms;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Custom-painted panel that delegates drawing to whatever the active
/// puzzle renderer set on <see cref="OverlayLabels.PaintGrid"/>. The
/// HelperOverlay sets <see cref="PaintAction"/> after each render tick;
/// the panel itself is a thin shell, all the puzzle-specific drawing
/// lives in the renderer.
/// </summary>
public sealed class GridPanel : Panel
{
    public Action<Graphics, Rectangle>? PaintAction { get; set; }

    public GridPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 28, 44);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        PaintAction?.Invoke(e.Graphics, ClientRectangle);
    }
}
